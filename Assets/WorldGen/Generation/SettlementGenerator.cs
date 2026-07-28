using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Parameters for generating a settlement. Scale is a single knob — the SIZE CLASS — and the wall
    /// radius, gate count and building target all derive from it through SettlementSizing, so one generator
    /// spans a hamlet to a capital and nothing can disagree about how big the town is.
    ///
    /// NO legacy count field here, unlike SettlementParams: SettlementConfig is never serialized (it is the
    /// argument to Generate/BuildFloor, built fresh at every call site), so there is no old wire value for it
    /// to have to read back.</summary>
    public class SettlementConfig
    {
        public int Seed;
        public SettlementSize Size = SettlementSize.Medium;
        public int ActiveBuildings = 10;
        public bool HasWall = true;
    }

    /// <summary>A gate position on the wall, in normalized space. Becomes a Room node (TypeId 0) at assembly.</summary>
    public struct GatePoint { public float X, Y; }

    /// <summary>A placed building's centre, normalized. Becomes a Room node (TypeId 1) at assembly.</summary>
    public struct PlacedBuilding { public float X, Y; }

    /// <summary>Deterministic settlement geometry: wall, gates, building placement (Tasks 2–3), and assembly
    /// into an InteriorData (Task 5). Pure, no Unity — the whole point is headless testability, since the
    /// dungeon packer measured 18–48 overlapping pairs at 40 nodes and cannot be reused.</summary>
    public static class SettlementGenerator
    {
        public const int WallSides = 9;
        public const float WallJitter = 0.12f;

        /// <summary>Nominal footprint (tiles, both axes) a placed building projects as when the preliminary
        /// fence is derived from it (Ц2.6: gates are spaced on a fence traced around the ACTUAL buildings, not
        /// the raw notional wall). Pinned to <see cref="DungeonProjection.EffectiveSize"/>'s default for a
        /// fresh <see cref="Room"/>: TypeId defaults to 1 ("Normal" — the same TypeId BuildFloor assigns every
        /// building room below), SizeW/H default to 0 ("unset"), so EffectiveSize falls through to
        /// RoomSizing.Default(1)'s default case — (6,6), both sides already inside RoomSizing.Clamp's 1..16
        /// range unchanged. 6 is therefore the exact size a building room would render/pack at if it ever
        /// went through the normal room-sizing path, so the preliminary fence hugs buildings at the same
        /// nominal scale the rest of the codebase already assumes for a TypeId-1 room.
        ///
        /// NO LONGER CALLED BY ANYTHING (arc A, task 3). BuildFloor does not derive a preliminary fence any
        /// more — gates come out of SettlementBlocks — and the road/fence adapter now sizes a settlement
        /// building from its FOOTPRINT (DungeonLayout.LinkNodeFor), which is the whole point of that change:
        /// a multi-cell house is 3.84 tiles per cell, not 6 tiles total. Left compiling, and left DOCUMENTED
        /// as dead, because Task 5 is STILL PENDING to remove this whole preliminary-gate path along with
        /// SettlementStreets (see BuildFloor's own doc below for the up-to-date state of that task).</summary>
        public const float NominalBuildingTiles = 6f;

        /// <summary>Wall radius (normalized) for a size class. A one-line delegation to SettlementSizing on
        /// purpose: the table is the ONE place a town's scale is decided, and the old count→radius formula
        /// (0.16 + 0.0045·count, clamped at 0.45) is exactly the kind of second opinion that made the stored
        /// building count promise a size the geometry never delivered. Kept as a method here only because
        /// every call site and self-test already reaches for it through SettlementGenerator.</summary>
        public static float WallRadiusFor(SettlementSize size) => SettlementSizing.WallRadiusNorm(size);

        /// <summary>Gate count for a size class — likewise one line, one source of truth.</summary>
        public static int GateCountFor(SettlementSize size) => SettlementSizing.GateCount(size);

        /// <summary>Place `gateCount` gates spread around the wall by ARC LENGTH (offset by a seeded phase so
        /// towns differ), each landing exactly on a wall segment. `gateCount` is supplied by the caller (via
        /// GateCountFor), so there is ONE source of truth for the count — PlaceGates never re-derives it.</summary>
        public static List<GatePoint> PlaceGates(WallContour wall, int gateCount, int seed)
        {
            var gates = new List<GatePoint>();
            if (wall == null || !wall.IsClosedSane() || gateCount <= 0) return gates;

            // Perimeter length so gates are spread by ARC LENGTH, not by vertex index (a jittered polygon has
            // uneven sides; index-spacing would cluster gates on the short sides).
            int n = wall.Points.Count;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                cum[i + 1] = cum[i] + (float)System.Math.Sqrt(dx * dx + dy * dy);
            }
            float total = cum[n];

            var rng = new System.Random(seed * 31 + 17);
            float phase = (float)rng.NextDouble() * total;
            for (int g = 0; g < gateCount; g++)
            {
                float target = (phase + total * g / gateCount) % total;
                gates.Add(PointAtArcLength(wall, cum, target));
            }
            return gates;
        }

        static GatePoint PointAtArcLength(WallContour wall, float[] cum, float target)
        {
            int n = wall.Points.Count;
            for (int i = 0; i < n; i++)
            {
                if (target <= cum[i + 1] || i == n - 1)
                {
                    var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                    float segLen = cum[i + 1] - cum[i];
                    float t = segLen <= 0f ? 0f : (target - cum[i]) / segLen;
                    return new GatePoint { X = a.X + t * (b.X - a.X), Y = a.Y + t * (b.Y - a.Y) };
                }
            }
            return new GatePoint { X = wall.Points[0].X, Y = wall.Points[0].Y };
        }

        /// <summary>Normalized pitch of the building lattice — THE constant that makes the size table
        /// (SettlementSizing) fit the field.
        ///
        /// WHY 0.03 AND NOT THE 0.07 EVERY SAVE BEFORE FORMAT 11 WAS WRITTEN ON. A settlement's scale is a
        /// SIZE CLASS now, and the largest class wants ~10.0 cells of radius to hold ~120 buildings (Task D's MEASURED figure —
        /// SettlementSizing's class doc; the 9.1 this comment quoted before Task D was the pre-measurement
        /// algebraic guess). At 0.07 that is 0.70 normalized, so the town would span 0.5 ± 0.70 = −0.20..1.20 — off both ends of a
        /// 0..1 field, and far outside DungeonViewController's 0.04..0.96 drag clamp, which is the real bound
        /// (a building the DM cannot drag to where it is drawn is worse than a small town). At 0.03 the same
        /// 10.0 cells are 0.30 normalized and the town spans 0.20..0.80 — comfortably inside the clamp, with
        /// room for the wall ring and courtyard the tile grid adds outside the buildings.
        ///
        /// A FINER PITCH IS A FINER LATTICE, NOT A SMALLER TOWN. The town covers LESS of the normalized field
        /// but MORE cells, which is the whole point: cells are what blocks, streets and footprints are counted
        /// in. Nothing about the model is re-scaled — the view fits to the town's own bounds
        /// (DungeonViewController.FitBoundsFor), so a 0.30-radius town (Task D's measured Large, was 0.273
        /// before Task D re-measured WallRadiusCells) fills the panel exactly as a 0.45-radius one used to.
        ///
        /// EVERY TILE-SPACE RATIO MOVED WITH IT: one cell is BuildingCell * DungeonLayout.TilesPerAxis =
        /// 3.84 tiles, down from 8.96. See SettlementRoads.RoadClearanceTiles and DungeonLayout.LinkNodeFor
        /// for the two places that ratio is load-bearing. SettlementFootprint.LegacyPitch keeps the old value
        /// for the v11 migration, which must read pre-v11 coordinates on the lattice they were authored on.</summary>
        public const float BuildingCell = 0.03f;

        public static List<PlacedBuilding> PlaceBuildings(WallContour wall, int seed, int targetCount)
        {
            var kept = new List<PlacedBuilding>();
            if (wall == null || !wall.IsClosedSane()) return kept;

            // Bounding box of the wall. Seeded from the first point (domain-agnostic — no assumption that
            // the contour lies within 0..1) rather than from 1f/0f sentinels, matching the same hardening
            // WallContour.IsClosedSane applies. IsClosedSane above guarantees Points.Count >= 3.
            float minX = wall.Points[0].X, minY = wall.Points[0].Y, maxX = wall.Points[0].X, maxY = wall.Points[0].Y;
            foreach (var p in wall.Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }

            float half = BuildingCell * 0.5f;
            // Cell centres on a regular grid; keep those inside the wall and clear of the line.
            for (float cy = minY + half; cy <= maxY - half + 1e-6f; cy += BuildingCell)
                for (float cx = minX + half; cx <= maxX - half + 1e-6f; cx += BuildingCell)
                    if (wall.Contains(cx, cy) && wall.DistanceToEdge(cx, cy) >= half)
                        kept.Add(new PlacedBuilding { X = cx, Y = cy });

            // Deterministic Fisher–Yates shuffle so the kept-but-dropped buildings vary by seed, then trim.
            var rng = new System.Random(seed * 131 + 71);
            for (int i = kept.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kept[i], kept[j]) = (kept[j], kept[i]);
            }
            if (kept.Count > targetCount) kept.RemoveRange(targetCount, kept.Count - targetCount);
            return kept;
        }

        /// <summary>Assemble one settlement floor from a BLOCK LAYOUT: gate rooms (TypeId 0) then building
        /// rooms (TypeId 1) in the SAME order the street stage indexes them (gates first), streets → links.
        ///
        /// A BUILDING IS A FOOTPRINT NOW. SettlementBlocks.Generate lays one-cell streets through the notional
        /// contour's interior wherever a house would otherwise have no frontage, and fills what is left
        /// between them with flush, disjoint footprints; every
        /// building room carries its own cells on Room.Cells and every street cell is stored once on
        /// SettlementParams.StreetCells. Both are ABSOLUTE lattice indices (SettlementFootprint), the same
        /// frame SettlementTileGrid draws from, so the stored town and the drawn town cannot disagree.
        ///
        /// A ROOM'S POINT IS ITS REPRESENTATIVE CELL'S CENTRE, never a centroid: SettlementTileGrid.FootprintOf
        /// treats a SINGLE-cell footprint that disagrees with the room's point as stale and re-derives it from
        /// the point, so a point in some other cell would silently relocate every one-cell house in town.
        ///
        /// GATES COME FROM THE LAYOUT — ring-street cells spread by ANGLE around the town, SettlementSizing's
        /// count of them (SettlementBlocks.PlaceGateCells) — not from a preliminary fence any more (Ц2.6's
        /// SettlementFence.Derive → PlaceGates path). A wall-less village gets none; it still gets its
        /// streets.
        ///
        /// STILL LIVE, AND STILL TASK 5's TO DELETE: SettlementStreets.GenerateStreets and the
        /// gates-then-buildings id↔index contract below. Links are what SettlementRoads routes the drawn
        /// roads from, and a link-less floor routes nothing at all, so the contract cannot go until
        /// SettlementStreets itself does. PlaceBuildings, PlaceGates and GateCountFor, by contrast, are no
        /// longer called from here at all — they compile, they are still self-tested directly, and they are
        /// dead as far as generation is concerned.</summary>
        public static InteriorFloor BuildFloor(SettlementConfig cfg)
        {
            // Placement region: a NOTIONAL contour (identical Rounded call regardless of HasWall) that block
            // generation carves up — never stored, so nothing renders it directly.
            var placement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, WallRadiusFor(cfg.Size), WallSides, WallJitter);
            var layout = SettlementBlocks.Generate(placement, cfg.Seed, cfg.Size);

            // A wall-less village has nothing to open a gate IN, so it takes none of the layout's.
            var gates = new List<GatePoint>();
            if (cfg.HasWall)
                foreach (var gc in layout.GateCells)
                    gates.Add(new GatePoint { X = SettlementFootprint.CenterOf(gc.i), Y = SettlementFootprint.CenterOf(gc.j) });

            // The street stage still works in POINTS (Task 5 is STILL PENDING to replace it wholesale — see
            // this method's own class doc above), so each footprint is handed to it as its representative
            // cell's centre — the same point the room below carries.
            var buildings = new List<PlacedBuilding>(layout.Buildings.Count);
            foreach (var fp in layout.Buildings)
            {
                var rep = SettlementFootprint.Representative(fp);
                buildings.Add(new PlacedBuilding { X = SettlementFootprint.CenterOf(rep.i), Y = SettlementFootprint.CenterOf(rep.j) });
            }

            var edges = SettlementStreets.GenerateStreets(placement, buildings, gates, cfg.Seed);

            var floor = new InteriorFloor();
            // Node index i (gates first, then buildings) → room id (i+1). Ids are stable and dense.
            var idByIndex = new int[gates.Count + buildings.Count];
            int next = 1;
            for (int i = 0; i < gates.Count; i++)
            {
                idByIndex[i] = next;
                // A GATE CARRIES ITS CELL TOO (v11). `gates` is built one-for-one from layout.GateCells in
                // order just above (and only when HasWall, which is also the only way this loop runs at all),
                // so index i names the same gate in both lists. Storing it makes a gate translatable by
                // SettlementMigration.RecentreFloor — which moves the town by moving CELLS — instead of the
                // one node left behind when everything else moves.
                floor.Rooms.Add(new Room
                {
                    Id = next, TypeId = 0, X = gates[i].X, Y = gates[i].Y,
                    Cells = SettlementFootprint.Encode(new[] { layout.GateCells[i] }),
                });
                next++;
            }
            int activeCount = cfg.ActiveBuildings < 0 ? 0 : cfg.ActiveBuildings;

            // SPREAD, NOT A PREFIX. DM report: a Large town with a small active count had every active
            // building bunched in one corner. Root cause was here — buildings are emitted BLOCK BY BLOCK in
            // a fixed spatial order (SettlementBlocks.Generate sorts blocks before filling them), and the old
            // rule (`i >= activeCount`) made "active" mean "the first N buildings in emission order", which
            // is always "every building in the first block or two".
            //
            // THE FIX PARTITIONS EMISSION ORDER INTO activeGoal CONTIGUOUS, ROUGHLY-EQUAL BUCKETS and rolls
            // exactly one active pick inside each bucket. This is a STRUCTURAL guarantee, not a probabilistic
            // one: for every bucket b in 0..activeGoal-1, EXACTLY one building whose emission index falls in
            // that bucket's [lo,hi) range is marked active — on every seed, not merely a typical one. A
            // uniform shuffle-then-take-first-N was considered and rejected for exactly this reason: it only
            // makes "not clustered" typical (probability 1 - 1/C(buildings.Count, activeGoal) of avoiding the
            // old prefix outright), so a self-test over it could only ever assert "these particular seeds
            // happen to spread" rather than a property true of the rule itself.
            //
            // Seeded distinctly from every other pass already in this arc, so a change to how many rolls one
            // of them makes can never shift which buildings THIS pass marks active: SettlementBlocks uses
            // seed*7919+13 (gates) and seed*977+41 (fill); this file's own PlaceGates/PlaceBuildings (dead as
            // far as generation is concerned, still self-tested directly — see BuildFloor's class doc) use
            // seed*31+17 and seed*131+71.
            int activeGoal = activeCount > buildings.Count ? buildings.Count : activeCount;
            var isActiveBuilding = new bool[buildings.Count];
            if (activeGoal > 0)
            {
                var activeRng = new System.Random(cfg.Seed * 3001 + 293);
                for (int b = 0; b < activeGoal; b++)
                {
                    int lo = (int)((long)b * buildings.Count / activeGoal);
                    int hi = (int)((long)(b + 1) * buildings.Count / activeGoal);
                    isActiveBuilding[lo + activeRng.Next(hi - lo)] = true;
                }
            }
            for (int i = 0; i < buildings.Count; i++)
            {
                idByIndex[gates.Count + i] = next;
                floor.Rooms.Add(new Room
                {
                    Id = next, TypeId = 1, X = buildings[i].X, Y = buildings[i].Y,
                    Cells = SettlementFootprint.Encode(layout.Buildings[i]),
                    IsDummy = !isActiveBuilding[i],
                });
                next++;
            }
            floor.NextRoomId = next;
            foreach (var e in edges)
                floor.Links.Add(new Link { RoomA = idByIndex[e.A], RoomB = idByIndex[e.B] });
            floor.SettlementParams = new SettlementParams
            {
                Size = cfg.Size,
                ActiveBuildings = cfg.ActiveBuildings,
                HasWall = cfg.HasWall,
                // Null, not an empty array, when there are no streets at all — SettlementParams.StreetCells
                // is NullValueHandling.Ignore, and an empty array would put the key on the wire for nothing.
                StreetCells = layout.StreetCells.Count > 0 ? SettlementFootprint.Encode(layout.StreetCells) : null,
            };
            return floor;
        }

        public static InteriorData Generate(SettlementConfig cfg, string ownerPoiId)
        {
            var data = new InteriorData { OwnerPoiId = ownerPoiId, Kind = InteriorKind.Settlement };
            data.Floors.Add(BuildFloor(cfg));
            return data;
        }
    }
}
