using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure generator for BUILDING interiors: <c>floorCount</c> compact floors, each a small room
    /// graph (spanning tree + a few loop edges, biased linear/branching -- no boss/spine guarantee, buildings
    /// don't need one) packed via <see cref="CompactLayout.Arrange"/>, joined floor-to-floor by explicit
    /// Stairs portals. No UnityEngine types -- self-testable headless. Deterministic by seed: ONE
    /// <see cref="Random"/> drives the ENTIRE building (every floor's rooms/types/sizes/links AND the stair
    /// choices), so one seed reproduces the whole multi-floor structure, not just a single floor.
    ///
    /// FLOOR COHERENCE (Task C1). All floors live in ONE shared X/Y frame (Room.X/Y are directly comparable
    /// across floors), with the invariant <c>bbox(floor N+1) ⊆ bbox(floor N)</c> -- the ground floor is the
    /// biggest and every floor above nests inside the one below, like a real tower narrowing as it rises. The
    /// ground floor is Arranged on the full field and its footprint bbox becomes the building outline. Each
    /// upper floor is generated with FEWER rooms and slid into a sub-box of the floor below (never scaled --
    /// see below), then the room that carries the stairs down is nudged to sit within <see cref="StairAlignTol"/>
    /// tiles of the lower floor's stair room, so consecutive stairs read as "roughly above" each other.
    ///
    /// SHRINK = FEWER ROOMS, NOT SMALLER ROOMS. Room sizes stay a modest 4..6 on every floor. A higher floor
    /// is smaller because it has fewer chambers, so its packed bbox is smaller -- footprints are never scaled
    /// down (that would distort the room proportions the DM reads as chambers).
    ///
    /// Room sizes are rolled directly here (NOT via <see cref="RoomSizing.Roll"/>/<see cref="RoomSizing.Default"/>)
    /// -- RoomSizing's ranges are keyed on DUNGEON type semantics (TypeId 2 == Boss -> 10x10 default, 8..14
    /// roll range), which is meaningless for a building (its types are just {0 Вход, 1 Комната}) and would
    /// wrongly blow a plain room up to boss size. Buildings want modest, roughly-uniform chambers instead, so
    /// this class rolls its own small independent-per-axis range.</summary>
    public static class BuildingGenerator
    {
        // Independent per-axis roll range (tiles, inclusive) for a building room's footprint -- deliberately
        // small and narrow so every room reads as a modest chamber, never a dungeon-sized hall.
        const int MinSide = 4;
        const int MaxSideExclusive = 7;   // Random.Next's upper bound is exclusive -> rolls 4..6

        /// <summary>Max tile distance, on EACH of X and Y, between a consecutive pair's two stair rooms for
        /// them to count as "roughly above" each other. ≈ one modest room (rooms are 4..6 tiles): the upper
        /// floor is centred on the lower stair room and its stair room is nudged straight at it within the
        /// nesting box, so in the common case they align exactly (residual 0) and only a stair sitting hard
        /// against the building's outer edge -- where nesting cannot slide the floor any further -- leaves a
        /// small residual, which this bound absorbs. Small enough that an UN-nested (field-centred) upper
        /// floor, whose rooms sit far from a peripheral lower stair room, exceeds it -- so the stair-align
        /// self-test is non-vacuous.</summary>
        public const float StairAlignTol = 8f;

        static int T => DungeonLayout.TilesPerAxis;

        public static InteriorData Generate(int seed, string ownerPoiId, int roomCount, int floorCount)
        {
            roomCount = Math.Max(1, roomCount);
            floorCount = Math.Max(1, floorCount);

            var rng = new Random(seed);   // ONE rng for the whole building: every floor + every stair choice
                                           // draws from this SAME sequence, so the seed reproduces everything.
            var data = new InteriorData
            {
                OwnerPoiId = ownerPoiId,
                Kind = InteriorKind.Building,
                Floors = new List<InteriorFloor>(),
            };

            // --- Ground floor (0): entrance + compact graph, Arranged on the full field. Its footprint bbox
            //     is the building outline every upper floor must nest inside. -------------------------------
            var ground = BuildFloorGraph(rng, roomCount, isGroundFloor: true);
            CompactLayout.Arrange(ground);
            data.Floors.Add(ground);

            // --- Upper floors (1..): each nests inside the floor below, with its stair room roughly above the
            //     lower floor's stair room. Stairs are wired inline (not in a later pass) because the upper
            //     floor's placement DEPENDS on where the lower floor's stair room sits. -------------------
            int budget = roomCount;
            for (int k = 1; k < floorCount; k++)
            {
                var lower = data.Floors[k - 1];
                var (lMinX, lMinY, lMaxX, lMaxY) = DungeonProjection.ContentBoundsTiles(lower);

                // Stair-DOWN room on the lower floor: the room farthest from that floor's own footprint centre
                // (a peripheral "back room"). Being off-centre is what makes the stair-align invariant
                // meaningful -- an un-nested upper floor would centre on the field, far from this room. (On an
                // upper floor the entrance is itself peripheral, so this may pick that floor's own entrance as
                // the next stair-down -- harmless: that room just hosts both the arrival-down and departure-up.)
                var stairDown = FarthestRoomFromCentre(lower, lMinX, lMinY, lMaxX, lMaxY);
                float sx = stairDown.X * T, sy = stairDown.Y * T;

                // Fewer rooms upstairs (drop 1..2 per floor -> strictly smaller than the floor below), then
                // nest the floor inside the lower bbox, biased over the lower stair room.
                budget = Math.Max(1, budget - rng.Next(1, 3));
                var upper = GenerateNestedUpperFloor(rng, budget,
                    lMinX, lMinY, lMaxX, lMaxY, sx, sy, out var stairUp);

                // The stair-ARRIVAL room is this upper floor's entrance (user 2026-07-19: the room the stairs
                // from below lead into is the floor's entrance). Marked AFTER layout — a pure type change, it
                // moves/resizes nothing (sizes are explicit; the layout already ran on the no-entrance graph).
                stairUp.TypeId = 0;

                // Exactly ONE non-hidden Stairs portal per consecutive pair, stored on the LOWER floor's
                // stair room, pointing up to the chosen (roughly-above) room on the floor immediately above.
                stairDown.Portals.Add(new Portal
                {
                    Kind = PortalKind.Stairs,
                    Hidden = false,
                    TargetFloorIndex = k,
                    TargetRoomId = stairUp.Id,
                    Bidirectional = true,
                    Label = "Лестница",
                });

                data.Floors.Add(upper);
            }

            return data;
        }

        /// <summary>Collapse legacy building room types (Приватная/Служебная/Особая = TypeId 2/3/4, from before
        /// the 2-type simplification) down to the plain room (TypeId 1), so a saved building doesn't render
        /// them as the entrance — the 2-entry building palette makes <see cref="Rendering.InteriorProfile.TypeOf"/>
        /// clamp any out-of-range id to index 0 (Вход). Entrances (0) and plain rooms (1) are untouched.
        /// A no-op for non-building interiors. Applied on load; deterministic, headless.</summary>
        public static void NormalizeTypes(InteriorData d)
        {
            if (d == null || d.Kind != InteriorKind.Building || d.Floors == null) return;
            foreach (var f in d.Floors)
                foreach (var r in f.Rooms)
                    if (r.TypeId >= 2) r.TypeId = 1;
        }

        /// <summary>Generate ONE upper floor's room graph and NEST it inside a lower floor's footprint bbox
        /// (tile space): <c>bbox(result) ⊆ [lowerMinX,lowerMaxX]×[lowerMinY,lowerMaxY]</c>, with the room
        /// nearest the lower stair point slid to sit "roughly above" it. The floor is packed at
        /// <paramref name="roomBudget"/> rooms and the count is REDUCED and re-packed if the packed footprint
        /// overflows the shrink sub-box (never scaled -- fewer rooms, not smaller ones). Public so the
        /// self-test can feed a hand-built OFF-CENTRE lower bbox and prove the nesting is real: an un-nested
        /// floor centres on the field and would stick out of an off-centre lower bbox. <paramref name="downStairRoom"/>
        /// is the upper room the lower floor's stairs should target. Consumes <paramref name="rng"/> in a
        /// fixed order (shrink box, then per-attempt graph), so it is deterministic within the building's one
        /// rng sequence.</summary>
        public static InteriorFloor GenerateNestedUpperFloor(Random rng, int roomBudget,
            float lowerMinX, float lowerMinY, float lowerMaxX, float lowerMaxY,
            float lowerStairXTiles, float lowerStairYTiles, out Room downStairRoom)
        {
            roomBudget = Math.Max(1, roomBudget);

            // Target sub-box Tk ⊆ lower bbox: a deterministic per-axis shrink (0.6..1.0 of the lower extent)
            // centred on the lower stair point, clamped to stay inside the lower bbox. This both biases the
            // floor over the stair and gives the room count something to shrink toward.
            var (tMinX, tMinY, tMaxX, tMaxY) = ShrinkBoxAroundPoint(
                lowerMinX, lowerMinY, lowerMaxX, lowerMaxY, lowerStairXTiles, lowerStairYTiles, rng);

            // Pack at the budget; if the footprint overflows Tk, drop a room and re-pack. Terminates at 1 room
            // (a single 4..6 chamber, which fits any Tk derived from a floor of sane size).
            InteriorFloor floor;
            int count = roomBudget;
            while (true)
            {
                floor = BuildFloorGraph(rng, count, isGroundFloor: false);
                bool fits = CompactLayout.ArrangeWithin(floor, tMinX, tMinY, tMaxX, tMaxY);
                if (fits || count <= 1) break;
                count--;
            }

            // Stair-UP room = the room nearest the lower stair point; nudge it straight at that point, clamped
            // so the WHOLE floor stays inside the LOWER bbox (the enforced nesting invariant -- a hair more
            // slack than Tk, so the alignment is as tight as the outline allows while never breaking nesting).
            downStairRoom = NearestRoomToPoint(floor, lowerStairXTiles, lowerStairYTiles);
            CompactLayout.NudgeRoomToward(floor, downStairRoom.Id,
                lowerStairXTiles, lowerStairYTiles, lowerMinX, lowerMinY, lowerMaxX, lowerMaxY);

            return floor;
        }

        /// <summary>Build one floor's room graph (rooms + types + sizes + links) WITHOUT laying it out -- the
        /// caller chooses Arrange (ground) or ArrangeWithin+nudge (upper). Same graph shape the pre-coherence
        /// generator produced; only the layout step moved out.</summary>
        static InteriorFloor BuildFloorGraph(Random rng, int roomCount, bool isGroundFloor)
        {
            roomCount = Math.Max(1, roomCount);
            var floor = new InteriorFloor();

            // 1. Rooms, ids 1..roomCount.
            for (int i = 0; i < roomCount; i++)
                floor.Rooms.Add(new Room { Id = i + 1, TypeId = 1 });
            floor.NextRoomId = roomCount + 1;

            // 2. TYPES (simplified 2-type palette, user 2026-07-19). Floor 0 room 1 (index 0) is the building
            //    entrance (TypeId 0); every OTHER room is a plain room (TypeId 1). An UPPER floor has no
            //    entrance here -- Generate() marks the stair-ARRIVAL room as that floor's entrance after layout.
            //    We still CONSUME one rng draw per non-entrance room (as the old 1..4 roll did) so the rng
            //    sequence -- and thus every seed's sizes/positions -- is byte-identical to pre-simplification.
            for (int i = 0; i < roomCount; i++)
            {
                bool isTheEntrance = isGroundFloor && i == 0;
                if (!isTheEntrance) rng.Next(1, 5);   // consumed to keep the seed sequence stable; value unused
                floor.Rooms[i].TypeId = isTheEntrance ? 0 : 1;
            }

            // 3. SIZES. Rolled independently per axis in a small MODEST range -- see the class doc for why
            //    this deliberately bypasses RoomSizing (its Boss/TypeId-2 default would blow a building's
            //    plain room up to 10x10). Sizes stay 4..6 on EVERY floor: a higher floor shrinks by
            //    having fewer rooms, never smaller ones.
            foreach (var r in floor.Rooms)
            {
                r.SizeW = rng.Next(MinSide, MaxSideExclusive);
                r.SizeH = rng.Next(MinSide, MaxSideExclusive);
            }

            // 4. LINKS: a connected spanning tree -- each room i>1 Connects to a random EARLIER room, the
            //    same shape as DungeonGraphGenerator step 3 (no guaranteed spine here: a building doesn't
            //    need a far-from-entrance boss, just linear-branching connectivity) -- plus a FEW optional
            //    loop edges (~roomCount/5).
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in floor.Rooms) adj[r.Id] = new HashSet<int>();
            // Named Connect, not Link -- a local function sharing a name with the Link TYPE is exactly the
            // same-name shadowing landmine this codebase has been bitten by before.
            void Connect(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                floor.Links.Add(new Link { RoomA = a, RoomB = b });
            }

            for (int i = 1; i < roomCount; i++)
            {
                int parent = rng.Next(0, i);   // 0..i-1: any earlier room -> linear-branching bias
                Connect(floor.Rooms[i].Id, floor.Rooms[parent].Id);
            }

            int extra = roomCount / 5;
            int guard = 0;
            while (extra > 0 && guard++ < roomCount * 8 && roomCount >= 2)
            {
                int a = rng.Next(roomCount);
                int b = rng.Next(roomCount);
                if (a == b || adj[floor.Rooms[a].Id].Contains(floor.Rooms[b].Id)) continue;
                Connect(floor.Rooms[a].Id, floor.Rooms[b].Id);
                extra--;
            }

            return floor;
        }

        /// <summary>Deterministic per-axis shrink of a bbox: each axis keeps a random 60..100% of the lower
        /// extent, and the sub-box is centred on (px,py) then clamped to stay inside the lower bbox. Fewer
        /// rooms then fill this smaller region, so the floor genuinely shrinks (bias toward the stair).</summary>
        static (float minX, float minY, float maxX, float maxY) ShrinkBoxAroundPoint(
            float minX, float minY, float maxX, float maxY, float px, float py, Random rng)
        {
            float fx = 0.6f + (float)rng.NextDouble() * 0.4f;   // 0.6 .. 1.0
            float fy = 0.6f + (float)rng.NextDouble() * 0.4f;
            float w = (maxX - minX) * fx;
            float h = (maxY - minY) * fy;

            float nMinX = px - w * 0.5f, nMaxX = px + w * 0.5f;
            float nMinY = py - h * 0.5f, nMaxY = py + h * 0.5f;

            // Slide the (never-wider-than-parent) sub-box back inside the parent bbox if it poked out.
            if (nMinX < minX) { nMaxX += minX - nMinX; nMinX = minX; }
            if (nMaxX > maxX) { nMinX -= nMaxX - maxX; nMaxX = maxX; }
            if (nMinY < minY) { nMaxY += minY - nMinY; nMinY = minY; }
            if (nMaxY > maxY) { nMinY -= nMaxY - maxY; nMaxY = maxY; }
            return (nMinX, nMinY, nMaxX, nMaxY);
        }

        /// <summary>Room whose centre is farthest (Euclidean) from the given footprint-bbox centre; ties broken
        /// by lowest Id. Deterministic. Used to seat the stairs in a peripheral room.</summary>
        static Room FarthestRoomFromCentre(InteriorFloor floor, float minX, float minY, float maxX, float maxY)
        {
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            Room best = null; float bestD = -1f;
            foreach (var r in floor.Rooms)
            {
                float dx = r.X * T - cx, dy = r.Y * T - cy;
                float d = dx * dx + dy * dy;
                if (d > bestD || (d == bestD && (best == null || r.Id < best.Id))) { bestD = d; best = r; }
            }
            return best;
        }

        /// <summary>Room whose centre is nearest (Euclidean) to a tile-space point; ties broken by lowest Id.
        /// Deterministic. Used to pick the upper stair room closest to the lower stair point.</summary>
        static Room NearestRoomToPoint(InteriorFloor floor, float px, float py)
        {
            Room best = null; float bestD = float.MaxValue;
            foreach (var r in floor.Rooms)
            {
                float dx = r.X * T - px, dy = r.Y * T - py;
                float d = dx * dx + dy * dy;
                if (d < bestD || (d == bestD && (best == null || r.Id < best.Id))) { bestD = d; best = r; }
            }
            return best;
        }
    }
}
