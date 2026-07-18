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
    /// Room sizes are rolled directly here (NOT via <see cref="RoomSizing.Roll"/>/<see cref="RoomSizing.Default"/>)
    /// -- RoomSizing's ranges are keyed on DUNGEON type semantics (TypeId 2 == Boss -> 10x10 default, 8..14
    /// roll range), and a building's TypeId 2 means "Приватная" (private room; see BuildingProfile), which
    /// must NOT come out boss-sized. Buildings want modest, roughly-uniform chambers instead, so this class
    /// rolls its own small independent-per-axis range.</summary>
    public static class BuildingGenerator
    {
        // Independent per-axis roll range (tiles, inclusive) for a building room's footprint -- deliberately
        // small and narrow so every room reads as a modest chamber, never a dungeon-sized hall.
        const int MinSide = 4;
        const int MaxSideExclusive = 7;   // Random.Next's upper bound is exclusive -> rolls 4..6

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

            for (int f = 0; f < floorCount; f++)
                data.Floors.Add(GenerateFloor(rng, roomCount, isGroundFloor: f == 0));

            // Stairs: exactly ONE portal per consecutive floor pair, stored on the LOWER floor's chosen
            // room, pointing up to a chosen room on the floor above. Bidirectional authoring intent.
            for (int f = 0; f < floorCount - 1; f++)
            {
                var lower = data.Floors[f];
                var upper = data.Floors[f + 1];
                var fromRoom = lower.Rooms[rng.Next(lower.Rooms.Count)];
                var toRoom = upper.Rooms[rng.Next(upper.Rooms.Count)];
                fromRoom.Portals.Add(new Portal
                {
                    Kind = PortalKind.Stairs,
                    Hidden = false,
                    TargetFloorIndex = f + 1,
                    TargetRoomId = toRoom.Id,
                    Bidirectional = true,
                    Label = "Лестница",
                });
            }

            return data;
        }

        static InteriorFloor GenerateFloor(Random rng, int roomCount, bool isGroundFloor)
        {
            var floor = new InteriorFloor();

            // 1. Rooms, ids 1..roomCount.
            for (int i = 0; i < roomCount; i++)
                floor.Rooms.Add(new Room { Id = i + 1, TypeId = 1 });
            floor.NextRoomId = roomCount + 1;

            // 2. TYPES. Floor 0 room 1 (index 0) is the building entrance (TypeId 0) -- exactly one, and
            //    ONLY on floor 0. Every other room, on EVERY floor (the rest of floor 0, and ALL rooms on
            //    floors > 0), rolls a building type in 1..4 (see BuildingProfile: 1=Общая, 2=Приватная,
            //    3=Служебная, 4=Особая).
            for (int i = 0; i < roomCount; i++)
            {
                bool isTheEntrance = isGroundFloor && i == 0;
                floor.Rooms[i].TypeId = isTheEntrance ? 0 : rng.Next(1, 5);   // upper bound exclusive -> 1..4
            }

            // 3. SIZES. Rolled independently per axis in a small MODEST range -- see the class doc for why
            //    this deliberately bypasses RoomSizing (its Boss/TypeId-2 default would blow a building's
            //    "Приватная" room up to 10x10).
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

            // 5. LAYOUT: buildings are compact, not spread -- pack linked rooms flush so most links render
            //    as a shared-wall door rather than a corridor.
            CompactLayout.Arrange(floor);

            return floor;
        }
    }
}
