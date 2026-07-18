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

        public static InteriorFloor Generate(int seed, int roomCount, int minBossDistance = DefaultMinBossDistance)
        {
            var lvl = new InteriorFloor();
            if (roomCount <= 0) return lvl;                 // empty floor (validator will flag "no entrance")

            var rng = new Random(seed);

            // 1. Rooms, ids 1..roomCount. Room 1 is the entrance.
            for (int i = 0; i < roomCount; i++)
                lvl.Rooms.Add(new Room { Id = i + 1, TypeId = 1 });
            lvl.NextRoomId = roomCount + 1;
            lvl.Rooms[0].TypeId = 0;

            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            // Named Connect, not Link — a local function sharing a name with the Link TYPE is exactly the
            // same-name shadowing landmine this codebase has been bitten by before.
            void Connect(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                lvl.Links.Add(new Link { RoomA = a, RoomB = b });
            }

            // 2. Guaranteed SPINE 0-1-...-spineLen. A plain random-recursive-tree is too shallow on small
            //    floors (a 6-room tree has NO room >= 3 hops from the entrance ~60% of the time), so the
            //    spine is what makes a far-enough boss reliable. spineLen rooms sit in a line from the entrance.
            int spineLen = Math.Min(minBossDistance, roomCount - 1);
            for (int i = 1; i <= spineLen; i++)
                Connect(lvl.Rooms[i].Id, lvl.Rooms[i - 1].Id);

            // 3. Remaining rooms attach to a random earlier room (random branches off the spine/tree).
            for (int i = spineLen + 1; i < roomCount; i++)
            {
                int parent = rng.Next(0, i);                // 0..i-1
                Connect(lvl.Rooms[i].Id, lvl.Rooms[parent].Id);
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
                Connect(lvl.Rooms[a].Id, lvl.Rooms[b].Id);
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
            if (bossId != 0) lvl.GetRoom(bossId).TypeId = 2;

            // Sizes roll AFTER types are final (entrance at step 1, boss just above) — Roll is keyed on
            // type. Uses the generator's own seeded rng, so a seed still reproduces its floor exactly.
            foreach (var r in lvl.Rooms) { var (w, h) = RoomSizing.Roll(r.TypeId, rng); r.SizeW = w; r.SizeH = h; }

            // 7. Layout X/Y by BFS depth so the initial layout is readable, not a clump.
            LayoutByDepth(lvl, dist);

            // 8. Make the floor satisfy the corridor leash BY CONSTRUCTION, anchored at the entrance.
            //    Without this, "the generator's worst case" is a number someone estimated from the layout's
            //    geometry — and a fresh floor could be born already taut, so the DM's first drag would snap
            //    it together with a visible jolt. Now the bound is guaranteed, and the compaction self-test
            //    can assert MaxCorridorTiles directly instead of a hand-derived threshold.
            DungeonLayout.EnforceCorridorLeash(lvl, lvl.Rooms[0].Id);

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
        /// TILE units from the actual footprints (+ DesiredGapTiles), not by normalizing layers across the
        /// axis — the old spread put ~13 tiles of void between 3-tile rooms regardless of their size.
        ///
        /// Columns are PARENT-ALIGNED: each room starts centred under its BFS parent (siblings share the
        /// parent's centre line), then a left-to-right sweep resolves overlaps within the row. Centring
        /// each row independently — the previous approach — left the horizontal offset bounded only by the
        /// row's own width, so a room at one row's edge could sit ~20 tiles from its parent even though
        /// they are joined by a corridor. Corridors now run nearly vertical.
        ///
        /// Generate() runs EnforceCorridorLeash afterwards, which is what actually GUARANTEES the corridor
        /// bound — this method's job is to make that pass a near-no-op, not to prove the bound itself.</summary>
        static void LayoutByDepth(InteriorFloor lvl, Dictionary<int, int> dist)
        {
            const float DesiredGapTiles = 3f;
            const float T = DungeonLayout.TilesPerAxis;

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

            float cursorY = (T - totalH) * 0.5f;   // centre the block vertically
            var xOf = new Dictionary<int, float>();   // resolved tile-space X, by room id

            foreach (int k in keys)
            {
                var rooms = layers[k];
                float h = rowH[k];

                // 1. Group this layer's rooms by BFS parent, and centre each sibling group under it.
                var groups = new Dictionary<int, List<Room>>();
                foreach (var r in rooms)
                {
                    int p = ParentId(lvl, dist, r);
                    if (!groups.ContainsKey(p)) groups[p] = new List<Room>();
                    groups[p].Add(r);
                }
                var groupKeys = new List<int>(groups.Keys);
                groupKeys.Sort();   // deterministic order

                foreach (int p in groupKeys)
                {
                    var sibs = groups[p];
                    sibs.Sort((u, v) => u.Id.CompareTo(v.Id));
                    float groupW = 0f;
                    foreach (var r in sibs) groupW += r.SizeW + DesiredGapTiles;
                    groupW -= DesiredGapTiles;

                    float parentX = xOf.TryGetValue(p, out var px) ? px : T * 0.5f;   // no parent → field centre
                    float cursor = parentX - groupW * 0.5f;
                    foreach (var r in sibs)
                    {
                        xOf[r.Id] = cursor + r.SizeW * 0.5f;
                        cursor += r.SizeW + DesiredGapTiles;
                    }
                }

                // 2. Sweep left-to-right so sibling groups that landed on top of each other separate,
                //    while keeping every room as close to its parent's column as the row allows.
                rooms.Sort((u, v) =>
                {
                    int c = xOf[u.Id].CompareTo(xOf[v.Id]);
                    return c != 0 ? c : u.Id.CompareTo(v.Id);
                });
                for (int i = 1; i < rooms.Count; i++)
                {
                    float minX = xOf[rooms[i - 1].Id]
                               + (rooms[i - 1].SizeW + rooms[i].SizeW) * 0.5f + DesiredGapTiles;
                    if (xOf[rooms[i].Id] < minX) xOf[rooms[i].Id] = minX;
                }

                // 3. Slide the finished row back onto the field if the sweep pushed it off an edge.
                //    Shifting the WHOLE row preserves the parent alignment established above.
                float minEdge = float.MaxValue, maxEdge = float.MinValue;
                foreach (var r in rooms)
                {
                    minEdge = Math.Min(minEdge, xOf[r.Id] - r.SizeW * 0.5f);
                    maxEdge = Math.Max(maxEdge, xOf[r.Id] + r.SizeW * 0.5f);
                }
                float shift = 0f;
                if (minEdge < 0f) shift = -minEdge;
                else if (maxEdge > T) shift = T - maxEdge;

                foreach (var r in rooms)
                {
                    xOf[r.Id] += shift;
                    r.X = Clamp01(xOf[r.Id] / T);
                    r.Y = Clamp01((cursorY + h * 0.5f) / T);
                }
                cursorY += h + DesiredGapTiles;
            }
        }

        /// <summary>This room's BFS parent = its neighbour one layer shallower. 0 when it has none (the
        /// entrance, or a disconnected room).</summary>
        static int ParentId(InteriorFloor lvl, Dictionary<int, int> dist, Room r)
        {
            if (!dist.TryGetValue(r.Id, out int myD)) return 0;
            int best = 0;
            foreach (var c in lvl.Links)
            {
                int other = c.RoomA == r.Id ? c.RoomB : (c.RoomB == r.Id ? c.RoomA : 0);
                if (other == 0) continue;
                if (!dist.TryGetValue(other, out int od) || od != myD - 1) continue;
                if (best == 0 || other < best) best = other;   // lowest id wins → deterministic
            }
            return best;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
