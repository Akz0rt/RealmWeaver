using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure generator for BUILDING interiors around a single vertical STAIRWELL COLUMN. Floor 0 is an
    /// entrance + compact room graph (freely authored later); it also hosts the column — one Лестница room
    /// (the generator seats it at a central non-entrance room; the user can move/place it on floor 0). Every
    /// floor ABOVE is GENERATED: a Лестница of the SAME footprint at the SAME (x,y) as the column, plus compact
    /// Комнаты packed around it, the whole floor kept within floor 0's footprint bbox. Consecutive Лестница
    /// rooms are joined by Stairs portals. No UnityEngine types — self-testable headless. Deterministic by
    /// seed: ONE <see cref="Random"/> drives the entire building.
    ///
    /// Because the column is a single shared (x,y), every floor's Лестница aligns exactly, and there is never a
    /// "stair to nowhere": a floor's up-link exists only when a floor above exists (handled by the badge /
    /// transition layer). SHRINK = FEWER ROOMS: upper floors have fewer chambers (sizes stay a modest 4..6), so
    /// they pack smaller and nest within the outline; footprints are never scaled.
    ///
    /// Room sizes are rolled here (NOT via <see cref="RoomSizing"/>, whose ranges are keyed on dungeon Boss
    /// semantics and would blow a plain room up to boss size). Buildings want modest, roughly-uniform chambers.</summary>
    public static class BuildingGenerator
    {
        // Independent per-axis roll range (tiles, inclusive) for a building room footprint — small and narrow.
        const int MinSide = 4;
        const int MaxSideExclusive = 7;   // Random.Next upper bound is exclusive -> rolls 4..6

        // Building room type ids — mirror BuildingProfile.RoomTypes order: 0 Вход, 1 Комната, 2 Лестница.
        const int EntranceTypeId = 0;
        const int RoomTypeId = 1;
        public const int StairTypeId = 2;   // Лестница — the stairwell column (the editor's +этаж reads this)

        // Deterministic AREA capacity. Sides roll uniformly in [MinSide, MaxSideExclusive) -> E[side]=5,
        // E[area]=25 tile². PackFill discounts for the gaps a real flush-pack around a fixed column leaves
        // (empirical; tune at the checkpoint like ContourMargin). Used to reject an impossible room count BEFORE
        // a seed-dependent pack, and to only offer counts a retry-driven pack can actually realize.
        static float AvgRoomArea { get { float s = (MinSide + MaxSideExclusive - 1) * 0.5f; return s * s; } }
        public const float PackFill = 0.72f;

        static int T => DungeonLayout.TilesPerAxis;

        public static InteriorData Generate(int seed, string ownerPoiId, int roomCount, int floorCount)
        {
            roomCount = Math.Max(1, roomCount);
            floorCount = Math.Max(1, floorCount);

            var rng = new Random(seed);   // ONE rng for the whole building — the seed reproduces everything.
            var data = new InteriorData
            {
                OwnerPoiId = ownerPoiId,
                Kind = InteriorKind.Building,
                Floors = new List<InteriorFloor>(),
            };

            // --- Ground floor (0): entrance + compact graph on the full field. Its footprint bbox is the
            //     building outline every upper floor packs within. ---------------------------------------------
            var ground = BuildGroundFloorGraph(rng, roomCount);
            CompactLayout.Arrange(ground);
            data.Floors.Add(ground);
            if (floorCount <= 1) return data;   // single floor: no stairwell needed

            // --- Stairwell column: a central-ish NON-entrance room on floor 0 becomes the Лестница (central so
            //     upper floors can pack around it and stay inside the outline). Its (x,y) + footprint are the
            //     column every upper floor's Лестница reuses. ------------------------------------------------
            var (minX, minY, maxX, maxY) = DungeonProjection.ContentBoundsTiles(ground);
            var column = NearestNonEntranceRoomToCentre(ground, minX, minY, maxX, maxY);
            if (column == null) return data;   // degenerate: floor 0 is only the entrance -> no stairwell possible
            column.TypeId = StairTypeId;
            float colX = column.X * T, colY = column.Y * T;
            int colW = column.SizeW, colH = column.SizeH;

            // --- Upper floors (1..): each a Лестница (the column footprint) at the column + Комнаты around it,
            //     within floor 0's bbox; joined by a Stairs portal up the column. ----------------------------
            Room lowerStair = column;
            int budget = roomCount;
            for (int k = 1; k < floorCount; k++)
            {
                budget = Math.Max(1, budget - rng.Next(1, 3));   // fewer rooms upstairs
                var upper = GenerateFloorAroundColumn(rng, budget, colX, colY, colW, colH, ground, out var upperStair);
                lowerStair.Portals.Add(new Portal
                {
                    Kind = PortalKind.Stairs,
                    Hidden = false,
                    TargetFloorIndex = k,
                    TargetRoomId = upperStair.Id,
                    Bidirectional = true,
                    Label = "Лестница",
                });
                data.Floors.Add(upper);
                lowerStair = upperStair;
            }
            return data;
        }

        /// <summary>Collapse building room types that no longer exist in the current palette
        /// {0 Вход, 1 Комната, 2 Лестница} down to the plain room (TypeId 1), so a saved building doesn't render
        /// them as the entrance — <see cref="Rendering.InteriorProfile.TypeOf"/> clamps any out-of-range id to
        /// index 0 (Вход). Valid ids 0/1/2 are untouched; only the dropped Служебная/Особая (old TypeId 3/4)
        /// collapse to 1. A no-op for non-building interiors. Applied on load; headless.
        /// CAVEAT (unreleased dev data only): a save from before TypeId 2 became Лестница had TypeId 2 =
        /// "Приватная"; it will now read as a Лестница. Dev buildings regenerate — acceptable.</summary>
        public static void NormalizeTypes(InteriorData d)
        {
            if (d == null || d.Kind != InteriorKind.Building || d.Floors == null) return;
            foreach (var f in d.Floors)
                foreach (var r in f.Rooms)
                    if (r.TypeId >= 3) r.TypeId = RoomTypeId;
        }

        /// <summary>Return floor 0's stairwell column (its Лестница room). If floor 0 has none yet, DESIGNATE a
        /// central non-entrance room as the column (the user's single column, per the spec) and return it. Null
        /// only when floor 0 is empty or has just the entrance (no room to host a stairwell). Used by the
        /// editor's +этаж so a new floor can be generated around the SAME column. Deterministic; headless.</summary>
        public static Room EnsureFloorZeroColumn(InteriorData d)
        {
            if (d == null || d.Floors == null || d.Floors.Count == 0) return null;
            var existing = FindFloorZeroColumn(d);
            if (existing != null) return existing;   // already has a column
            var floor0 = d.Floors[0];
            var (minX, minY, maxX, maxY) = DungeonProjection.ContentBoundsTiles(floor0);
            var col = NearestNonEntranceRoomToCentre(floor0, minX, minY, maxX, maxY);
            if (col != null) col.TypeId = StairTypeId;
            return col;
        }

        /// <summary>Floor 0's stairwell column (its Лестница) if one is already designated, else null. NON-mutating,
        /// unlike <see cref="EnsureFloorZeroColumn"/> — safe to call from display/refresh paths (the toolbar's
        /// capacity readout) that must not designate a column as a side effect.</summary>
        public static Room FindFloorZeroColumn(InteriorData d)
        {
            if (d == null || d.Floors == null || d.Floors.Count == 0) return null;
            foreach (var r in d.Floors[0].Rooms) if (r.TypeId == StairTypeId) return r;
            return null;
        }

        /// <summary>Deterministic capacity: the largest TOTAL room count (INCLUDING the Лестница column) that fits
        /// by AREA inside <paramref name="contourFloor"/>'s drawn contour. The contour's usable area, minus the
        /// column footprint, divided by the average room footprint (discounted by <see cref="PackFill"/> for
        /// packing gaps). Seed-INDEPENDENT — unlike a single greedy pack it gives the SAME verdict every time, so
        /// «Перегенерировать» stops flip-flopping between "won't fit" and success on one count. Always ≥ 1.</summary>
        public static int MaxRoomsByArea(InteriorFloor contourFloor, int colW, int colH)
        {
            float usable = FloorFootprint.UsableAreaTiles(contourFloor, FloorFootprint.ContourMargin);
            float avail = usable - colW * colH;
            int extra = avail > 0f ? (int)(avail * PackFill / AvgRoomArea) : 0;
            return 1 + Math.Max(0, extra);
        }

        /// <summary>Generate ONE upper floor around the stairwell column: a Лестница (room 0, the column's
        /// footprint) pinned exactly at (colX,colY) + Комнаты packed flush around it, EVERY room kept inside
        /// <paramref name="contourFloor"/>'s footprint SHAPE (the drawn contour — floor 0). SHAPE-AWARE: rooms
        /// are placed only into valid in-footprint slots and as many as fit are KEPT (the rest dropped) — no
        /// reduce-to-one. Deterministic.</summary>
        public static InteriorFloor GenerateFloorAroundColumn(Random rng, int roomBudget,
            float colX, float colY, int colW, int colH, InteriorFloor contourFloor, out Room stair)
        {
            var floor = BuildStairFloorGraph(rng, Math.Max(1, roomBudget), colW, colH);
            var column = floor.Rooms[0];   // the Лестница (never dropped — it IS the column)
            CompactLayout.PackAroundColumnWithinFootprint(floor, column.Id, colX, colY, contourFloor, FloorFootprint.ContourMargin);
            stair = column;
            return floor;
        }

        /// <summary>Attempt to place EXACTLY <paramref name="roomCount"/> rooms around the column inside
        /// <paramref name="contourFloor"/>'s footprint. Returns true iff EVERY requested room fit (none dropped);
        /// the editor's «Перегенерировать» uses this to report "N rooms don't fit" and keep the current floor.
        /// Deterministic by seed.</summary>
        public static bool TryGenerateFloorAroundColumn(int seed, int roomCount,
            float colX, float colY, int colW, int colH, InteriorFloor contourFloor, out InteriorFloor floor, out Room stair)
        {
            var rng = new Random(seed);
            roomCount = Math.Max(1, roomCount);
            floor = BuildStairFloorGraph(rng, roomCount, colW, colH);
            var column = floor.Rooms[0];
            int placed = CompactLayout.PackAroundColumnWithinFootprint(floor, column.Id, colX, colY, contourFloor, FloorFootprint.ContourMargin);
            stair = column;
            return placed == roomCount;
        }

        /// <summary>Ground floor graph: room 0 is the entrance (TypeId 0), the rest plain rooms (TypeId 1);
        /// all sized 4..6; connected spanning tree + a few loops. Laid out by the caller (Arrange).</summary>
        static InteriorFloor BuildGroundFloorGraph(Random rng, int roomCount)
        {
            roomCount = Math.Max(1, roomCount);
            var floor = new InteriorFloor();
            for (int i = 0; i < roomCount; i++)
                floor.Rooms.Add(new Room { Id = i + 1, TypeId = (i == 0) ? EntranceTypeId : RoomTypeId });
            floor.NextRoomId = roomCount + 1;
            RollSizes(rng, floor, from: 0);
            BuildLinks(rng, floor, roomCount);
            return floor;
        }

        /// <summary>Upper floor graph: room 0 is the stairwell (Лестница, the column's footprint), the rest
        /// plain rooms sized 4..6; connected spanning tree + a few loops. Laid out by the caller.</summary>
        static InteriorFloor BuildStairFloorGraph(Random rng, int roomCount, int colW, int colH)
        {
            roomCount = Math.Max(1, roomCount);
            var floor = new InteriorFloor();
            for (int i = 0; i < roomCount; i++)
                floor.Rooms.Add(new Room { Id = i + 1, TypeId = (i == 0) ? StairTypeId : RoomTypeId });
            floor.NextRoomId = roomCount + 1;
            floor.Rooms[0].SizeW = colW; floor.Rooms[0].SizeH = colH;   // the stairwell == the column footprint
            RollSizes(rng, floor, from: 1);   // rooms 1..n-1 (the fixed-size Лестница at index 0 is skipped)
            BuildLinks(rng, floor, roomCount);
            return floor;
        }

        // Roll a modest 4..6 footprint for rooms from index `from` onward (deterministic).
        static void RollSizes(Random rng, InteriorFloor floor, int from)
        {
            for (int i = from; i < floor.Rooms.Count; i++)
            {
                floor.Rooms[i].SizeW = rng.Next(MinSide, MaxSideExclusive);
                floor.Rooms[i].SizeH = rng.Next(MinSide, MaxSideExclusive);
            }
        }

        // Connected spanning tree (each room i>0 links to a random EARLIER room -> linear-branching bias) plus
        // ~roomCount/5 optional loop edges. Named Connect (not Link) to avoid shadowing the Link TYPE.
        static void BuildLinks(Random rng, InteriorFloor floor, int roomCount)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in floor.Rooms) adj[r.Id] = new HashSet<int>();
            void Connect(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                floor.Links.Add(new Link { RoomA = a, RoomB = b });
            }

            for (int i = 1; i < roomCount; i++)
                Connect(floor.Rooms[i].Id, floor.Rooms[rng.Next(0, i)].Id);

            int extra = roomCount / 5, guard = 0;
            while (extra > 0 && guard++ < roomCount * 8 && roomCount >= 2)
            {
                int a = rng.Next(roomCount), b = rng.Next(roomCount);
                if (a == b || adj[floor.Rooms[a].Id].Contains(floor.Rooms[b].Id)) continue;
                Connect(floor.Rooms[a].Id, floor.Rooms[b].Id);
                extra--;
            }
        }

        /// <summary>The NON-entrance room whose centre is nearest the footprint-bbox centre — a central room to
        /// host the stairwell so upper floors can pack around it and stay within the outline; ties broken by
        /// lowest Id. Null if the floor has only the entrance. Deterministic.</summary>
        static Room NearestNonEntranceRoomToCentre(InteriorFloor floor, float minX, float minY, float maxX, float maxY)
        {
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            Room best = null; float bestD = float.MaxValue;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == EntranceTypeId) continue;
                float dx = r.X * T - cx, dy = r.Y * T - cy;
                float d = dx * dx + dy * dy;
                if (best == null || d < bestD - 1e-6f
                    || (Math.Abs(d - bestD) <= 1e-6f && r.Id < best.Id)) { bestD = d; best = r; }
            }
            return best;
        }
    }
}
