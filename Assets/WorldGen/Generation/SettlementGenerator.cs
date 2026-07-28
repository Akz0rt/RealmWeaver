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
        /// 3.84 tiles, down from 8.96. See DungeonLayout.LinkNodeFor and SettlementFence.FenceMarginTiles
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
        /// rooms (TypeId 1), in that order.
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
        /// A SETTLEMENT FLOOR CARRIES NO LINKS AT ALL (Task 5). It used to: a street stage
        /// (SettlementStreets) produced a spanning tree over gates+buildings and every edge of it became a
        /// Link, which a grid-A* router (SettlementRoads) then re-routed into drawn road polylines on every
        /// rebuild. Streets are STORED CELLS now — SettlementParams.StreetCells, laid once by
        /// SettlementBlocks and drawn straight by SettlementTileGrid — so those links described a street
        /// network nothing reads and cost one ~18 ms A* per settlement Refresh. Both stages and the links are
        /// gone. Link itself is untouched and still means what it always did for a DUNGEON or a BUILDING
        /// INTERIOR — and no EDITOR path creates one in a town either: «+ Здание» no longer auto-links a
        /// placed building (DungeonViewController.PlaceHoveredBuilding) and «Связать» is not offered for a
        /// settlement at all (DungeonEditorScreen.RefreshToolbar + DungeonViewController.SupportsLinking).
        /// A town's connectivity is its STREET CELLS. A town SAVED before this change still loads its links
        /// — nothing rejects or strips them, and the inspector's КОРИДОРЫ section can still delete them.
        ///
        /// PlaceBuildings, PlaceGates and GateCountFor are not called from here either — they compile, they
        /// are still self-tested directly, and they are dead as far as generation is concerned.</summary>
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

            // Each footprint's POINT is its representative cell's centre — the same point the room below
            // carries, and the one every consumer that still reads Room.X/Y (hit-testing, the lattice snap,
            // the fence adapter's non-footprint fallback) resolves it to.
            var buildings = new List<PlacedBuilding>(layout.Buildings.Count);
            foreach (var fp in layout.Buildings)
            {
                var rep = SettlementFootprint.Representative(fp);
                buildings.Add(new PlacedBuilding { X = SettlementFootprint.CenterOf(rep.i), Y = SettlementFootprint.CenterOf(rep.j) });
            }

            var floor = new InteriorFloor();
            // Room ids are assigned gates-first, then buildings, and stay stable and dense — the order the
            // whole codebase (and SelfTestAssembly's independent re-derivation) reads a settlement floor in.
            int next = 1;
            for (int i = 0; i < gates.Count; i++)
            {
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

            // SPREAD IN BOTH AXES, NOT A PREFIX OF EMISSION ORDER. DM report: a Large town with a small
            // active count had every active building bunched in one corner. Root cause was here — buildings
            // are emitted BLOCK BY BLOCK in a fixed spatial order (SettlementBlocks.Generate sorts blocks
            // before filling them), and the old rule (`i >= activeCount`) made "active" mean "the first N
            // buildings in emission order", which is always "every building in the first block or two".
            //
            // A FIRST FIX (bucketing emission order into activeGoal contiguous bands, one active pick per
            // band) was tried and REJECTED on review: SettlementBlocks visits blocks in ROW-MAJOR order (j
            // then i — SettlementBlocks.cs's ByLowestCell/RowMajor sort), so a contiguous emission-index band
            // is literally a horizontal STRIP of the town. That only constrains the j-coordinate; nothing
            // stops every active pick from landing near the same wall on the i-axis.
            //
            // FIXED INSTEAD WITH GREEDY FARTHEST-POINT SAMPLING over the buildings' actual LATTICE CELLS (not
            // their emission index): pick one seeded starting building, then repeatedly add whichever
            // remaining building is farthest — by squared cell distance — from EVERY building already picked,
            // breaking ties by the lower emission index (stable; matches the row-major convention the rest
            // of this file uses, and only ever matters on an exact tie). This is the classic greedy k-center /
            // farthest-first-traversal construction, and it constrains BOTH axes by construction: each pick
            // maximizes real 2D separation, not a 1D position in a fixed traversal order.
            //
            // MEASURABLE, NOT A MAGIC NUMBER: SelfTestActiveBuildings re-derives this exact algorithm
            // independently (same seed formula, off the floor's own room cells, never reading this method's
            // internals) and compares the resulting active CELL SET element-for-element against what
            // BuildFloor actually produced — so a future edit that dropped the RNG, hardcoded the starting
            // pick, or collapsed back to a 1-axis rule diverges from the re-derivation and is caught, not
            // merely a metric (a span or a variance) that could still read "spread enough" with the rule
            // broken.
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
                var buildingCellI = new int[buildings.Count];
                var buildingCellJ = new int[buildings.Count];
                for (int i = 0; i < buildings.Count; i++)
                {
                    var rep = SettlementFootprint.Representative(layout.Buildings[i]);
                    buildingCellI[i] = rep.i;
                    buildingCellJ[i] = rep.j;
                }
                var activeRng = new System.Random(cfg.Seed * 3001 + 293);
                int first = activeRng.Next(buildings.Count);
                isActiveBuilding[first] = true;
                // minDistToActive[x] tracks the running minimum squared cell distance from building x to the
                // active set built so far, updated incrementally so each of the remaining activeGoal-1 picks
                // costs O(buildings.Count) rather than recomputing every distance to every active pick from
                // scratch at every step.
                var minDistToActive = new long[buildings.Count];
                for (int x = 0; x < buildings.Count; x++)
                {
                    long dx = buildingCellI[x] - buildingCellI[first], dy = buildingCellJ[x] - buildingCellJ[first];
                    minDistToActive[x] = dx * dx + dy * dy;
                }
                for (int picked = 1; picked < activeGoal; picked++)
                {
                    int best = -1; long bestDist = -1;
                    for (int x = 0; x < buildings.Count; x++)
                    {
                        if (isActiveBuilding[x]) continue;
                        if (minDistToActive[x] > bestDist) { bestDist = minDistToActive[x]; best = x; }
                    }
                    isActiveBuilding[best] = true;
                    for (int x = 0; x < buildings.Count; x++)
                    {
                        if (isActiveBuilding[x]) continue;
                        long dx = buildingCellI[x] - buildingCellI[best], dy = buildingCellJ[x] - buildingCellJ[best];
                        long d = dx * dx + dy * dy;
                        if (d < minDistToActive[x]) minDistToActive[x] = d;
                    }
                }
            }
            for (int i = 0; i < buildings.Count; i++)
            {
                floor.Rooms.Add(new Room
                {
                    Id = next, TypeId = 1, X = buildings[i].X, Y = buildings[i].Y,
                    Cells = SettlementFootprint.Encode(layout.Buildings[i]),
                    IsDummy = !isActiveBuilding[i],
                });
                next++;
            }
            floor.NextRoomId = next;
            // NO LINKS — see this method's own doc. floor.Links stays the empty list InteriorFloor starts
            // with, and SelfTestAssembly asserts exactly that.
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
