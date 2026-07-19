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

        // AREA is only an UPPER BOUND on the room count — it can't predict whether the flush pack around the fixed
        // column will actually realize that many on a given contour SHAPE. So the displayed cap comes from a real
        // packing PROBE (MaxRoomsPackable); MaxRoomsByArea just sizes that probe. It is therefore GENEROUS: usable
        // area over the SMALLEST room footprint (MinSide² = 16), an upper bound the true packable max can't exceed,
        // clamped so the probe stays cheap.
        static float MinRoomArea { get { float s = MinSide; return s * s; } }   // 4*4 = 16
        const int ProbeSeeds = 10;        // fixed-seed packing probes → the deterministic, ACHIEVABLE displayed max
        const int MaxProbeBudget = 24;    // ceiling on probe room budget (keeps probing cheap)

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

        /// <summary>AREA UPPER BOUND on the total room count (INCLUDING the Лестница column) for
        /// <paramref name="contourFloor"/>'s contour: usable area minus the column, over the smallest room
        /// footprint, clamped to <see cref="MaxProbeBudget"/>. NOT the displayed cap — it only sizes the packing
        /// probe (<see cref="MaxRoomsPackable"/>); the real achievable max is measured, not estimated. Always ≥ 1.</summary>
        public static int MaxRoomsByArea(InteriorFloor contourFloor, int colW, int colH)
        {
            float usable = FloorFootprint.UsableAreaTiles(contourFloor, FloorFootprint.ContourMargin);
            float avail = usable - colW * colH;
            int extra = avail > 0f ? (int)(avail / MinRoomArea) : 0;
            return Math.Min(MaxProbeBudget, 1 + Math.Max(0, extra));
        }

        /// <summary>The ACTUAL maximum rooms (incl. the column) the packer can place around the column inside this
        /// contour — found by PROBING (a real pack), because area only bounds it: a small/awkward contour packs
        /// fewer than its area allows. Deterministic (fixed probe seeds) so the «из N» readout is stable, and
        /// always ACHIEVABLE (it is a real packing result), so any count ≤ it is GUARANTEED to generate via
        /// <see cref="TryBuildUpperFloorExact"/>. ≥ 1 (the column alone always fits).</summary>
        public static int MaxRoomsPackable(float colX, float colY, int colW, int colH, InteriorFloor contourFloor)
        {
            int budget = MaxRoomsByArea(contourFloor, colW, colH);
            int best = 1;
            for (int s = 0; s < ProbeSeeds; s++)
            {
                var f = GenerateFloorAroundColumn(new Random(s), budget, colX, colY, colW, colH, contourFloor, out _);
                if (f.Rooms.Count > best) best = f.Rooms.Count;
            }
            return best;
        }

        /// <summary>Build an upper floor with EXACTLY <paramref name="targetCount"/> rooms (incl. the column),
        /// GUARANTEED to succeed whenever targetCount ≤ <see cref="MaxRoomsPackable"/>. Phase 1 tries
        /// <paramref name="variety"/> seeds derived from <paramref name="varietySeed"/> for a fresh natural layout
        /// that packs AT LEAST targetCount, then trims it down to exactly targetCount; phase 2 falls back to the
        /// fixed probe seeds, which reach the packable max by construction — so an in-range count always yields a
        /// valid arrangement instead of a "won't fit" dead end. Returns false only if targetCount genuinely
        /// exceeds what the contour can pack.</summary>
        public static bool TryBuildUpperFloorExact(int targetCount, int varietySeed, int variety,
            float colX, float colY, int colW, int colH, InteriorFloor contourFloor, out InteriorFloor floor, out Room stair)
        {
            targetCount = Math.Max(1, targetCount);
            int budget = Math.Max(targetCount, MaxRoomsByArea(contourFloor, colW, colH));

            // Phase 1: fresh (varietySeed-derived) seeds — a new-looking layout each press.
            for (int i = 0; i < variety; i++)
                if (TryPackAtLeast(unchecked(varietySeed + i), targetCount, budget, colX, colY, colW, colH, contourFloor, out floor, out stair))
                { TrimToRoomCount(floor, targetCount, stair.Id); return true; }

            // Phase 2: deterministic probe seeds — these reach the packable max, so any in-range count is found here.
            for (int s = 0; s < ProbeSeeds; s++)
                if (TryPackAtLeast(s, targetCount, budget, colX, colY, colW, colH, contourFloor, out floor, out stair))
                { TrimToRoomCount(floor, targetCount, stair.Id); return true; }

            floor = null; stair = null;
            return false;
        }

        static bool TryPackAtLeast(int seed, int targetCount, int budget,
            float colX, float colY, int colW, int colH, InteriorFloor contourFloor, out InteriorFloor floor, out Room stair)
        {
            floor = GenerateFloorAroundColumn(new Random(seed), budget, colX, colY, colW, colH, contourFloor, out stair);
            return floor.Rooms.Count >= targetCount;
        }

        /// <summary>Reduce a freshly generated floor to EXACTLY <paramref name="targetCount"/> rooms, keeping the
        /// column and a CONNECTED subset (a BFS prefix from the column) and dropping the rest with their links.
        /// Lets an exact requested count be hit from a pack that placed more. No-op if already ≤ targetCount.</summary>
        public static void TrimToRoomCount(InteriorFloor floor, int targetCount, int columnRoomId)
        {
            if (floor == null || floor.Rooms.Count <= targetCount) return;
            var keep = new HashSet<int> { columnRoomId };
            var queue = new Queue<int>();
            queue.Enqueue(columnRoomId);
            while (queue.Count > 0 && keep.Count < targetCount)
            {
                int cur = queue.Dequeue();
                foreach (var lk in floor.Links)
                {
                    if (keep.Count >= targetCount) break;
                    int other = lk.RoomA == cur ? lk.RoomB : (lk.RoomB == cur ? lk.RoomA : -1);
                    if (other < 0 || keep.Contains(other)) continue;
                    keep.Add(other); queue.Enqueue(other);
                }
            }
            // Disconnected remainder (a connected pack shouldn't hit this) — top up with any rooms to reach target.
            if (keep.Count < targetCount)
                foreach (var r in floor.Rooms) { if (keep.Count >= targetCount) break; keep.Add(r.Id); }
            floor.Rooms.RemoveAll(r => !keep.Contains(r.Id));
            floor.Links.RemoveAll(l => !keep.Contains(l.RoomA) || !keep.Contains(l.RoomB));
            int maxId = 0; foreach (var r in floor.Rooms) if (r.Id > maxId) maxId = r.Id;
            floor.NextRoomId = maxId + 1;
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
