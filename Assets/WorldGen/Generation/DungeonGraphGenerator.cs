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

            foreach (var r in lvl.Rooms) { var (w, h) = RoomSizing.Default(r.Type); r.SizeW = w; r.SizeH = h; }

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

        /// <summary>Lay rooms out by BFS depth: one row per layer, deepest last. Spacing is computed in
        /// TILE units from the actual room footprints (+ DesiredGapTiles), NOT by normalizing layers across
        /// the whole [0.08,0.92] range as before — that old spread put ~13 tiles of void between 3-tile
        /// rooms regardless of their size, which is what made the rendered map read as specks on emptiness
        /// (spec R7). The block is then centred on the field. DungeonLayout.Separate still resolves any
        /// residual overlap afterwards.</summary>
        static void LayoutByDepth(DungeonLevel lvl, Dictionary<int, int> dist)
        {
            const float DesiredGapTiles = 3f;   // edge-to-edge target; self-test asserts <= 4 post-cascade

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

            var keys = new List<int>(layers.Keys);
            keys.Sort();

            // Row heights: each layer is as tall as its tallest room; rows are gap-separated.
            float totalH = 0f;
            var rowH = new Dictionary<int, float>();
            foreach (int k in keys)
            {
                float h = 1f;
                foreach (var r in layers[k]) h = Math.Max(h, r.SizeH);
                rowH[k] = h;
                totalH += h + DesiredGapTiles;
            }
            totalH -= DesiredGapTiles;   // no trailing gap after the last row

            float cursorY = (DungeonLayout.TilesPerAxis - totalH) * 0.5f;   // centre the block vertically
            foreach (int k in keys)
            {
                var rooms = layers[k];
                float h = rowH[k];

                float totalW = 0f;
                foreach (var r in rooms) totalW += r.SizeW + DesiredGapTiles;
                totalW -= DesiredGapTiles;

                float cursorX = (DungeonLayout.TilesPerAxis - totalW) * 0.5f;   // centre the row horizontally
                foreach (var r in rooms)
                {
                    r.X = Clamp01((cursorX + r.SizeW * 0.5f) / DungeonLayout.TilesPerAxis);
                    r.Y = Clamp01((cursorY + h * 0.5f) / DungeonLayout.TilesPerAxis);
                    cursorX += r.SizeW + DesiredGapTiles;
                }
                cursorY += h + DesiredGapTiles;
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
