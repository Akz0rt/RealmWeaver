using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Parameters for generating a settlement. Size is a single knob (TargetBuildings); the wall
    /// radius and gate count derive from it, so one generator spans a hamlet to a capital.</summary>
    public class SettlementConfig
    {
        public int Seed;
        public int TargetBuildings = 40;
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
        /// a multi-cell house is 8.96 tiles per cell, not 6 tiles total. Left compiling, and left DOCUMENTED
        /// as dead, because Task 5 removes this whole preliminary-gate path along with SettlementStreets.</summary>
        public const float NominalBuildingTiles = 6f;

        /// <summary>Wall radius (normalized) for a building count: bigger towns need more room. Clamped so a
        /// wall always fits inside the 0..1 canvas with margin.</summary>
        public static float WallRadiusFor(int buildingCount)
        {
            float r = 0.16f + 0.0045f * buildingCount;   // ~0.2 at 8, ~0.34 at 40, ~0.43 at 60
            return r > 0.45f ? 0.45f : r;
        }

        /// <summary>Gate count for a building count: 2 for a small town, up to 4 for a large one.</summary>
        public static int GateCountFor(int buildingCount)
        {
            if (buildingCount >= 55) return 4;
            if (buildingCount >= 30) return 3;
            return 2;
        }

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

        /// <summary>Normalized pitch of the building grid. One building per cell, so no two are closer than
        /// this — the anti-overlap guarantee that replaces the dungeon packer.</summary>
        public const float BuildingCell = 0.07f;

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
        /// A BUILDING IS A FOOTPRINT NOW. SettlementBlocks.Generate carves the notional contour's interior
        /// into blocks with one-cell streets and fills each block with flush, disjoint footprints; every
        /// building room carries its own cells on Room.Cells and every street cell is stored once on
        /// SettlementParams.StreetCells. Both are ABSOLUTE lattice indices (SettlementFootprint), the same
        /// frame SettlementTileGrid draws from, so the stored town and the drawn town cannot disagree.
        ///
        /// A ROOM'S POINT IS ITS REPRESENTATIVE CELL'S CENTRE, never a centroid: SettlementTileGrid.FootprintOf
        /// treats a SINGLE-cell footprint that disagrees with the room's point as stale and re-derives it from
        /// the point, so a point in some other cell would silently relocate every one-cell house in town.
        ///
        /// GATES COME FROM THE LAYOUT — the ring-street cells its primary streets run out into — not from a
        /// preliminary fence any more (Ц2.6's SettlementFence.Derive → PlaceGates path). A wall-less village
        /// gets none; it still gets its streets.
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
            var placement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, WallRadiusFor(cfg.TargetBuildings), WallSides, WallJitter);
            var layout = SettlementBlocks.Generate(placement, cfg.Seed, cfg.TargetBuildings);

            // A wall-less village has nothing to open a gate IN, so it takes none of the layout's.
            var gates = new List<GatePoint>();
            if (cfg.HasWall)
                foreach (var gc in layout.GateCells)
                    gates.Add(new GatePoint { X = SettlementFootprint.CenterOf(gc.i), Y = SettlementFootprint.CenterOf(gc.j) });

            // The street stage still works in POINTS (Task 5 replaces it wholesale), so each footprint is
            // handed to it as its representative cell's centre — the same point the room below carries.
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
                floor.Rooms.Add(new Room { Id = next, TypeId = 0, X = gates[i].X, Y = gates[i].Y });
                next++;
            }
            int activeCount = cfg.ActiveBuildings < 0 ? 0 : cfg.ActiveBuildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                idByIndex[gates.Count + i] = next;
                floor.Rooms.Add(new Room
                {
                    Id = next, TypeId = 1, X = buildings[i].X, Y = buildings[i].Y,
                    Cells = SettlementFootprint.Encode(layout.Buildings[i]),
                    IsDummy = i >= activeCount,
                });
                next++;
            }
            floor.NextRoomId = next;
            foreach (var e in edges)
                floor.Links.Add(new Link { RoomA = idByIndex[e.A], RoomB = idByIndex[e.B] });
            floor.SettlementParams = new SettlementParams
            {
                TargetBuildings = cfg.TargetBuildings,
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
