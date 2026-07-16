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

            // 2. Connected corridor graph. Spanning tree: each new room links to a random earlier one.
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            void Link(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                lvl.Corridors.Add(new Corridor { RoomA = a, RoomB = b });
            }
            for (int i = 1; i < roomCount; i++)
            {
                int parent = rng.Next(0, i);                // 0..i-1
                Link(lvl.Rooms[i].Id, lvl.Rooms[parent].Id);
            }
            // ~20% extra loop edges between random distinct rooms.
            int extra = roomCount / 5;
            int guard = 0;
            while (extra > 0 && guard++ < roomCount * 8 && roomCount > 2)
            {
                int a = rng.Next(roomCount), b = rng.Next(roomCount);
                if (a == b || adj[lvl.Rooms[a].Id].Contains(lvl.Rooms[b].Id)) continue;
                Link(lvl.Rooms[a].Id, lvl.Rooms[b].Id);
                extra--;
            }

            // 3. BFS distances + depth from the entrance (id 1).
            var dist = Bfs(lvl.Rooms[0].Id, adj);

            // 4. Boss = of rooms with dist >= minBossDistance, the farthest (tie → smallest id, stable).
            int bossId = 0, bestDist = -1;
            foreach (var r in lvl.Rooms)
            {
                if (r.Id == lvl.Rooms[0].Id) continue;
                int d = dist.TryGetValue(r.Id, out var dd) ? dd : -1;
                if (d >= minBossDistance && d > bestDist) { bestDist = d; bossId = r.Id; }
            }
            if (bossId != 0) lvl.GetRoom(bossId).Type = RoomType.Boss;

            // 5. Layout X/Y by BFS depth: layer = depth (unreached → last layer), spread within a layer.
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
