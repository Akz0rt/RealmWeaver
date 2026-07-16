using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Pure floor-graph generator: place N rooms, connect them into ONE connected corridor graph
    /// (random spanning tree + a few loop edges), make room 1 the Entrance, pick a Boss far from it
    /// (BFS distance >= minBossDistance), lay out X/Y by BFS depth. No Unity types — self-testable
    /// headless. Deterministic by seed.
    /// </summary>
    public static class DungeonGraphGenerator
    {
        public const int DefaultMinBossDistance = 3;

        public static DungeonLevel Generate(int seed, int roomCount, int minBossDistance = DefaultMinBossDistance)
        {
            var lvl = new DungeonLevel();
            if (roomCount <= 0) return lvl;                 // empty floor (validator will flag "no entrance")

            var rng = new Random(seed);

            // 1. Rooms, ids 1..roomCount. Room 1 is the entrance.
            for (int i = 0; i < roomCount; i++)
                lvl.Rooms.Add(new Room { Id = i + 1, Type = RoomType.Normal });
            lvl.NextRoomId = roomCount + 1;
            lvl.Rooms[0].Type = RoomType.Entrance;

            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            void Link(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                lvl.Corridors.Add(new Corridor { RoomA = a, RoomB = b });
            }

            // 2. Guaranteed SPINE 0-1-...-spineLen. A plain random-recursive-tree is too shallow on small
            //    floors (a 6-room tree has NO room >= 3 hops from the entrance ~60% of the time), so the
            //    spine is what makes a far-enough boss reliable. spineLen rooms sit in a line from the entrance.
            int spineLen = Math.Min(minBossDistance, roomCount - 1);
            for (int i = 1; i <= spineLen; i++)
                Link(lvl.Rooms[i].Id, lvl.Rooms[i - 1].Id);

            // 3. Remaining rooms attach to a random earlier room (random branches off the spine/tree).
            for (int i = spineLen + 1; i < roomCount; i++)
            {
                int parent = rng.Next(0, i);                // 0..i-1
                Link(lvl.Rooms[i].Id, lvl.Rooms[parent].Id);
            }

            // 4. Loop edges ONLY among the non-spine rooms (indices > spineLen), so a loop can never shortcut
            //    the spine path and the spine end stays at distance spineLen from the entrance.
            int nonSpineStart = spineLen + 1;
            int nonSpineCount = roomCount - nonSpineStart;
            int extra = roomCount / 5;
            int guard = 0;
            while (extra > 0 && guard++ < roomCount * 8 && nonSpineCount >= 2)
            {
                int a = nonSpineStart + rng.Next(nonSpineCount);
                int b = nonSpineStart + rng.Next(nonSpineCount);
                if (a == b || adj[lvl.Rooms[a].Id].Contains(lvl.Rooms[b].Id)) continue;
                Link(lvl.Rooms[a].Id, lvl.Rooms[b].Id);
                extra--;
            }

            // 5. BFS distances from the entrance (id 1).
            var dist = Bfs(lvl.Rooms[0].Id, adj);

            // 6. Boss = the farthest non-entrance room, ALWAYS placed when a non-entrance room exists. The
            //    spine guarantees this distance is >= min(minBossDistance, roomCount-1); on tiny 2-3 room
            //    floors it is closer and the validator warns, but a boss always exists for the DM to see.
            int bossId = 0, bestDist = -1;
            foreach (var r in lvl.Rooms)
            {
                if (r.Id == lvl.Rooms[0].Id) continue;
                int d = dist.TryGetValue(r.Id, out var dd) ? dd : -1;
                if (d > bestDist) { bestDist = d; bossId = r.Id; }
            }
            if (bossId != 0) lvl.GetRoom(bossId).Type = RoomType.Boss;

            // 7. Layout X/Y by BFS depth so the initial layout is readable, not a clump.
            LayoutByDepth(lvl, dist);

            return lvl;
        }

        static Dictionary<int, int> Bfs(int startId, Dictionary<int, HashSet<int>> adj)
        {
            var dist = new Dictionary<int, int> { [startId] = 0 };
            var q = new Queue<int>(); q.Enqueue(startId);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (int nb in adj[cur])
                    if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; q.Enqueue(nb); }
            }
            return dist;
        }

        static void LayoutByDepth(DungeonLevel lvl, Dictionary<int, int> dist)
        {
            int maxDepth = 0;
            foreach (var kv in dist) if (kv.Value > maxDepth) maxDepth = kv.Value;
            int unreachedLayer = maxDepth + 1;   // disconnected rooms (shouldn't happen) sink to the bottom

            var layers = new Dictionary<int, List<Room>>();
            foreach (var r in lvl.Rooms)
            {
                int layer = dist.TryGetValue(r.Id, out var d) ? d : unreachedLayer;
                if (!layers.ContainsKey(layer)) layers[layer] = new List<Room>();
                layers[layer].Add(r);
            }
            int layerCount = Math.Max(1, unreachedLayer + (layers.ContainsKey(unreachedLayer) ? 1 : 0));
            // Normalize into [0.08, 0.92] on both axes; entrance layer at top (Y small), deeper = larger Y.
            foreach (var kv in layers)
            {
                int layer = kv.Key; var rooms = kv.Value;
                float y = layerCount <= 1 ? 0.5f : 0.08f + 0.84f * (layer / (float)(layerCount - 1));
                for (int i = 0; i < rooms.Count; i++)
                {
                    float x = rooms.Count == 1 ? 0.5f : 0.08f + 0.84f * (i / (float)(rooms.Count - 1));
                    rooms[i].X = x; rooms[i].Y = y;
                }
            }
        }
    }
}
