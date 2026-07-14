using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Pure organic-cave generator: place chamber nodes → connect with a spanning tree (+ a few loop
    /// edges) → carve blob chambers + tunnels → cellular-automata smoothing → guarantee one connected
    /// floor component that includes every chamber node. Chamber nodes become the numbered key.
    /// No Unity types — self-testable headless.
    /// </summary>
    public static class CaveGenerator
    {
        public static DungeonLevel Generate(int seed, int width, int height, int chamberCount, float sizeFactor)
        {
            var rng = new Random(seed);
            sizeFactor = Clamp01(sizeFactor);
            var level = new DungeonLevel { Width = width, Height = height, Tiles = new DungeonTile[width * height] };
            // start all-wall
            for (int i = 0; i < level.Tiles.Length; i++) level.Tiles[i] = DungeonTile.Wall;

            // 1. Nodes via min-distance sampling, inset from the border.
            int inset = 3 + (int)((1f - sizeFactor) * 4);         // sprawl pushes nodes closer to edges
            float minDist = Lerp(6f, 3.5f, sizeFactor) * ScaleForCount(width, height, chamberCount);
            var nodes = SampleNodes(rng, width, height, chamberCount, inset, minDist);

            // 2. Spanning tree (Prim) + a few loop edges.
            var edges = SpanningTree(nodes);
            AddLoopEdges(rng, nodes, edges);

            // 3. Carve blobs at nodes + tunnels along edges.
            int tunnelHalf = sizeFactor > 0.5f ? 1 : 0;           // width 1 or 3 tiles
            foreach (var n in nodes)
            {
                int r = 2 + (int)Math.Round(Lerp(1f, 4f, sizeFactor)) + rng.Next(0, 2);
                CarveDisk(level, n.x, n.y, r);
            }
            foreach (var e in edges) CarveLine(level, nodes[e.a], nodes[e.b], tunnelHalf);

            // 4. Cellular-automata smoothing (border stays wall).
            int passes = 3;
            for (int p = 0; p < passes; p++) SmoothPass(level);

            // 5. Guarantee each node cell is floor, then one connected component covering all nodes.
            foreach (var n in nodes) CarveDisk(level, n.x, n.y, 1);
            EnsureConnected(level, nodes);
            KeepLargestComponent(level, nodes[0]);

            // 6. Chambers (numbered by placement order).
            for (int i = 0; i < nodes.Count; i++)
                level.Chambers.Add(new KeyChamber { Number = i + 1, MarkerCellX = nodes[i].x, MarkerCellY = nodes[i].y });

            return level;
        }

        struct Node { public int x, y; public Node(int x, int y) { this.x = x; this.y = y; } }
        struct Edge { public int a, b; public Edge(int a, int b) { this.a = a; this.b = b; } }

        static List<Node> SampleNodes(Random rng, int w, int h, int count, int inset, float minDist)
        {
            var nodes = new List<Node>();
            int attempts = 0, maxAttempts = count * 60;
            float minDistSq = minDist * minDist;
            while (nodes.Count < count && attempts++ < maxAttempts)
            {
                int x = inset + rng.Next(w - 2 * inset);
                int y = inset + rng.Next(h - 2 * inset);
                bool ok = true;
                foreach (var n in nodes)
                {
                    int dx = n.x - x, dy = n.y - y;
                    if (dx * dx + dy * dy < minDistSq) { ok = false; break; }
                }
                if (ok) nodes.Add(new Node(x, y));
            }
            // If min-distance was too strict to reach count, relax and top up (rare on 48x48).
            while (nodes.Count < count)
                nodes.Add(new Node(inset + rng.Next(w - 2 * inset), inset + rng.Next(h - 2 * inset)));
            return nodes;
        }

        static List<Edge> SpanningTree(List<Node> nodes)
        {
            var edges = new List<Edge>();
            if (nodes.Count < 2) return edges;
            var inTree = new bool[nodes.Count];
            inTree[0] = true;
            for (int added = 1; added < nodes.Count; added++)
            {
                int bestA = -1, bestB = -1; long best = long.MaxValue;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!inTree[i]) continue;
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        if (inTree[j]) continue;
                        long d = Dist2(nodes[i], nodes[j]);
                        if (d < best) { best = d; bestA = i; bestB = j; }
                    }
                }
                inTree[bestB] = true;
                edges.Add(new Edge(bestA, bestB));
            }
            return edges;
        }

        static void AddLoopEdges(Random rng, List<Node> nodes, List<Edge> edges)
        {
            int extra = Math.Max(0, edges.Count / 5);   // ~20%
            var have = new HashSet<(int, int)>();
            foreach (var e in edges) have.Add((Math.Min(e.a, e.b), Math.Max(e.a, e.b)));
            int guard = 0;
            while (extra > 0 && guard++ < 200 && nodes.Count > 2)
            {
                int a = rng.Next(nodes.Count), b = rng.Next(nodes.Count);
                if (a == b) continue;
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (have.Contains(key)) continue;
                have.Add(key); edges.Add(new Edge(a, b)); extra--;
            }
        }

        static void CarveDisk(DungeonLevel lvl, int cx, int cy, int r)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!lvl.InBounds(x, y)) continue;
                    if (IsBorder(lvl, x, y)) continue;             // keep a solid wall frame
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r * r) lvl.Set(x, y, DungeonTile.Floor);
                }
        }

        static void CarveLine(DungeonLevel lvl, Node a, Node b, int half)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                CarveDisk(lvl, x0, y0, half);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        static void SmoothPass(DungeonLevel lvl)
        {
            var next = (DungeonTile[])lvl.Tiles.Clone();
            for (int y = 1; y < lvl.Height - 1; y++)
                for (int x = 1; x < lvl.Width - 1; x++)
                {
                    int walls = 0;
                    for (int yy = -1; yy <= 1; yy++)
                        for (int xx = -1; xx <= 1; xx++)
                            if (!(xx == 0 && yy == 0) && lvl.Get(x + xx, y + yy) == DungeonTile.Wall) walls++;
                    next[y * lvl.Width + x] = walls >= 5 ? DungeonTile.Wall : DungeonTile.Floor;
                }
            lvl.Tiles = next;
        }

        static void EnsureConnected(DungeonLevel lvl, List<Node> nodes)
        {
            // Flood from node[0]; any node not reached gets a straight tunnel carved to node[0], then re-flood.
            for (int guard = 0; guard < nodes.Count + 2; guard++)
            {
                var reached = FloodFrom(lvl, nodes[0].x, nodes[0].y);
                bool allIn = true;
                for (int i = 1; i < nodes.Count; i++)
                    if (!reached[nodes[i].y * lvl.Width + nodes[i].x])
                    {
                        CarveLine(lvl, nodes[i], nodes[0], 1);
                        allIn = false;
                    }
                if (allIn) return;
            }
        }

        static void KeepLargestComponent(DungeonLevel lvl, Node keep)
        {
            var reached = FloodFrom(lvl, keep.x, keep.y);
            for (int i = 0; i < lvl.Tiles.Length; i++)
                if (lvl.Tiles[i] == DungeonTile.Floor && !reached[i]) lvl.Tiles[i] = DungeonTile.Wall;
        }

        static bool[] FloodFrom(DungeonLevel lvl, int sx, int sy)
        {
            var reached = new bool[lvl.Tiles.Length];
            if (lvl.Get(sx, sy) != DungeonTile.Floor) return reached;
            var q = new Queue<int>();
            int start = sy * lvl.Width + sx; reached[start] = true; q.Enqueue(start);
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int idx = q.Dequeue(); int x = idx % lvl.Width, y = idx / lvl.Width;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (!lvl.InBounds(nx, ny)) continue;
                    int ni = ny * lvl.Width + nx;
                    if (reached[ni] || lvl.Tiles[ni] != DungeonTile.Floor) continue;
                    reached[ni] = true; q.Enqueue(ni);
                }
            }
            return reached;
        }

        static bool IsBorder(DungeonLevel lvl, int x, int y) => x == 0 || y == 0 || x == lvl.Width - 1 || y == lvl.Height - 1;
        static long Dist2(Node a, Node b) { long dx = a.x - b.x, dy = a.y - b.y; return dx * dx + dy * dy; }
        static float ScaleForCount(int w, int h, int count) => Math.Max(0.5f, Math.Min(w, h) / (float)(count * 6));
        static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
