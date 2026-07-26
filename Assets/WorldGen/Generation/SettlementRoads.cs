using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>THE SETTLEMENT ROAD ROUTER (Ц1.6 spec §2.2). Routes every street edge with A* over a
    /// fixed 1-tile grid plus a precomputed obstacle mask, so roads go AROUND buildings — never through —
    /// at RoadClearanceTiles of clearance. Replaces RoomLinkGeometry FOR SETTLEMENTS ONLY (see
    /// DungeonLayout.BuildRenderGraph's settlementRoads flag); dungeon Fast/Clean paths are untouched.
    ///
    /// Same contract as RoomLinkGeometry.Build: tile-space LinkNode/LinkEdge in (edge A/B are node IDS),
    /// LinkGeometry out (Segments carry EdgeIndex, Doors are the wall-gap points, Forks unused). Pure,
    /// deterministic, UnityEngine-free, re-buildable every frame. The obstacle mask rasterizes the
    /// ACTUAL node rects on every Build — a dragged building sits OFF the BuildingCell grid, so the
    /// mask must never assume grid placement.
    ///
    /// ORDERING CONTRACT (with SettlementStreets): edges are routed in INPUT order, and later roads pay
    /// RoadReuseFactor (< 1) for cells earlier roads already claimed — arterials come first in the input,
    /// so branches merge into arterial lanes and junctions read as T-junctions.
    ///
    /// Cost: the mask is O(grid); each A* is bounded by the fixed grid (≤ ~10k cells at the 80-building
    /// cap), so a full Build is linear in edge count — contrast BuildRenderGraph(Clean)'s per-link Hanan
    /// grid over all rooms, which measured 20–34 s at 60 nodes.
    ///
    /// Ц2.6: the fence is DERIVED FROM the roads (SettlementFence.Derive wraps whatever the roads do), so
    /// the wall is no longer a road obstacle — the Ц1.7 wall-blocking rule this class used to enforce is
    /// REMOVED. Roads route avoiding ONLY buildings; Build takes no wall parameter any more.</summary>
    public static class SettlementRoads
    {
        /// <summary>How far a road keeps clear of a building, in tiles.
        ///
        /// ITS OWN DERIVATION WAS STALE (fixed arc A, task 3): this constant used to be justified by "the
        /// inter-building free gap is ~2.96 tiles (pitch 8.96 − footprint 6)", which was true back when a
        /// settlement building's road node came from a fixed 6-tile EffectiveSize regardless of its actual
        /// footprint. Since DungeonLayout.LinkNodeFor started sizing a settlement building's road node from
        /// its own FOOTPRINT, a flush building's node spans its WHOLE lattice cell — (extent+1) * Pitch,
        /// 8.96 tiles for a single-cell house — so that 2.96-tile gap no longer exists: two flush buildings'
        /// node rects meet with nothing between them. What RoadClearanceTiles actually carves lane-room out
        /// of now is the STREET cell itself: a road only ever runs on a cell SettlementBlocks marked as
        /// street, exactly one cell (8.96 tiles) wide, so clearance eats into that lane from both sides —
        /// 8.96 − 2*1.0 = 6.96 tiles left to route in. The constant's VALUE still works (that lane is still
        /// comfortably wide enough to route and read as a road); only the comment's derivation was wrong.
        /// Ц2.6 bumped it from 0.5 (tuned back when it also doubled as the wall clearance, before the wall
        /// stopped being a road obstacle). TUNABLE — the user eyeballs it.</summary>
        public const float RoadClearanceTiles = 1.0f;

        /// <summary>A* pays this many tiles per 90° turn — straight, readable roads. TUNABLE.</summary>
        public const float RoadTurnPenalty = 2f;

        /// <summary>Step-cost multiplier for a cell an EARLIER road already uses. Below 1 makes branches
        /// merge into arterial lanes (T-junctions) instead of running parallel one lane over. TUNABLE.</summary>
        public const float RoadReuseFactor = 0.5f;

        /// <summary>Grid margin beyond the nodes' AABB, in tiles — room to loop around the outer row.</summary>
        const int GridMargin = 4;

        static readonly int[] Dxs = { 1, -1, 0, 0 };
        static readonly int[] Dys = { 0, 0, 1, -1 };

        public static LinkGeometry Build(IReadOnlyList<LinkNode> nodes, IReadOnlyList<LinkEdge> edges)
        {
            var g = new LinkGeometry();
            if (nodes == null || edges == null || nodes.Count == 0 || edges.Count == 0) return g;

            var byId = new Dictionary<int, LinkNode>();
            foreach (var n in nodes) byId[n.Id] = n;

            // Grid extent: AABB of every node rect + margin, 1-tile pitch, integer coordinates.
            float fMinX = float.MaxValue, fMinY = float.MaxValue, fMaxX = float.MinValue, fMaxY = float.MinValue;
            foreach (var n in nodes)
            {
                if (n.CX - n.W * 0.5f < fMinX) fMinX = n.CX - n.W * 0.5f;
                if (n.CX + n.W * 0.5f > fMaxX) fMaxX = n.CX + n.W * 0.5f;
                if (n.CY - n.H * 0.5f < fMinY) fMinY = n.CY - n.H * 0.5f;
                if (n.CY + n.H * 0.5f > fMaxY) fMaxY = n.CY + n.H * 0.5f;
            }
            int minX = (int)System.Math.Floor(fMinX) - GridMargin, minY = (int)System.Math.Floor(fMinY) - GridMargin;
            int maxX = (int)System.Math.Ceiling(fMaxX) + GridMargin, maxY = (int)System.Math.Ceiling(fMaxY) + GridMargin;
            int gw = maxX - minX + 1, gh = maxY - minY + 1;

            // Obstacle mask from the ACTUAL rects (dragged buildings sit off-grid), inflated by clearance.
            var blocked = new bool[gw * gh];
            foreach (var n in nodes)
            {
                float hw = n.W * 0.5f + RoadClearanceTiles, hh = n.H * 0.5f + RoadClearanceTiles;
                int x0 = System.Math.Max(minX, (int)System.Math.Ceiling(n.CX - hw));
                int x1 = System.Math.Min(maxX, (int)System.Math.Floor(n.CX + hw));
                int y0 = System.Math.Max(minY, (int)System.Math.Ceiling(n.CY - hh));
                int y1 = System.Math.Min(maxY, (int)System.Math.Floor(n.CY + hh));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        blocked[(y - minY) * gw + (x - minX)] = true;
            }

            // A* state = cell × incoming direction (0..3; 4 = start, no direction yet).
            var best = new float[gw * gh * 5];
            var parent = new int[gw * gh * 5];
            var closed = new bool[gw * gh * 5];
            var roads = new HashSet<int>();          // cells already claimed by earlier roads
            var heap = new Heap();
            var path = new List<(int x, int y)>();

            for (int ei = 0; ei < edges.Count; ei++)
            {
                if (!byId.TryGetValue(edges[ei].A, out var A) || !byId.TryGetValue(edges[ei].B, out var B)) continue;
                path.Clear();
                if (!Route(A, B, blocked, roads, best, parent, closed, heap, minX, minY, maxX, maxY, gw, path))
                {
                    // Graceful degradation (never fail the Build): the straight centre-to-centre line.
                    EmitPolyline(g, ei, A, B, new List<LinkPoint>
                    {
                        new LinkPoint { X = A.CX, Y = A.CY }, new LinkPoint { X = B.CX, Y = B.CY }
                    });
                    continue;
                }
                foreach (var c in path) roads.Add((c.y - minY) * gw + (c.x - minX));

                // Collapse collinear runs into corner points.
                var pts = new List<LinkPoint> { new LinkPoint { X = path[0].x, Y = path[0].y } };
                for (int i = 1; i < path.Count - 1; i++)
                {
                    bool turnX = (path[i].x - path[i - 1].x) != (path[i + 1].x - path[i].x);
                    bool turnY = (path[i].y - path[i - 1].y) != (path[i + 1].y - path[i].y);
                    if (turnX || turnY) pts.Add(new LinkPoint { X = path[i].x, Y = path[i].y });
                }
                pts.Add(new LinkPoint { X = path[path.Count - 1].x, Y = path[path.Count - 1].y });
                EmitPolyline(g, ei, A, B, pts);
            }
            return g;
        }

        static bool Route(LinkNode A, LinkNode B, bool[] blocked, HashSet<int> roads,
                          float[] best, int[] parent, bool[] closed, Heap heap,
                          int minX, int minY, int maxX, int maxY,
                          int gw, List<(int x, int y)> outPath)
        {
            int sx = Clamp((int)System.Math.Round(A.CX), minX, maxX), sy = Clamp((int)System.Math.Round(A.CY), minY, maxY);
            int tx = Clamp((int)System.Math.Round(B.CX), minX, maxX), ty = Clamp((int)System.Math.Round(B.CY), minY, maxY);
            System.Array.Fill(best, float.MaxValue);
            System.Array.Fill(closed, false);
            heap.Clear();
            int s0 = ((sy - minY) * gw + (sx - minX)) * 5 + 4;
            best[s0] = 0f;
            heap.Push(0f, s0);
            int goalCell = (ty - minY) * gw + (tx - minX);
            int found = -1;
            while (heap.Count > 0)
            {
                // CLOSED-SET A*, not an f-vs-g stale check: the heap priority is f = g + h while best[]
                // holds g — comparing them skips EVERY expansion (h > 0) and degrades the whole Build to
                // straight-line fallbacks. h is consistent (min step cost × Manhattan), so the first pop
                // of a state is optimal and later pops are safely dropped.
                int st = heap.Pop(out _);
                if (closed[st]) continue;
                closed[st] = true;
                int cell = st / 5, pd = st % 5;
                if (cell == goalCell) { found = st; break; }
                int cx = cell % gw + minX, cy = cell / gw + minY;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Dxs[d], ny = cy + Dys[d];
                    if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                    int nc = (ny - minY) * gw + (nx - minX);
                    // Passable: free cell, or inside ONE OF THIS EDGE'S OWN endpoint rects (the carve
                    // that lets a road leave/enter its own building through the clearance ring).
                    if (blocked[nc] && !InsideInflated(nx, ny, A) && !InsideInflated(nx, ny, B)) continue;
                    float step = roads.Contains(nc) ? RoadReuseFactor : 1f;
                    if (pd != 4 && pd != d) step += RoadTurnPenalty;
                    float ng = best[st] + step;
                    int ns = nc * 5 + d;
                    if (ng < best[ns])
                    {
                        best[ns] = ng;
                        parent[ns] = st;
                        // Manhattan h scaled by RoadReuseFactor: the smallest possible step cost, so the
                        // heuristic stays admissible even along fully-discounted lanes.
                        float h = (System.Math.Abs(nx - tx) + System.Math.Abs(ny - ty)) * RoadReuseFactor;
                        heap.Push(ng + h, ns);
                    }
                }
            }
            if (found < 0) return false;
            int cur = found;
            while (true)
            {
                outPath.Add((cur / 5 % gw + minX, cur / 5 / gw + minY));
                if (cur == s0) break;
                cur = parent[cur];
            }
            outPath.Reverse();
            return true;
        }

        /// <summary>Clip the polyline's two ends at its own endpoints' UNinflated rect boundaries and
        /// emit the door points + the clipped segments. A polyline fully inside one rect (two rooms
        /// dragged onto each other) degrades to the raw centre line with no doors.</summary>
        static void EmitPolyline(LinkGeometry g, int edgeIndex, LinkNode A, LinkNode B, List<LinkPoint> pts)
        {
            int startIdx = 0;
            LinkPoint startPt = pts[0];
            for (int i = 1; i < pts.Count; i++)
                if (!InsideRect(pts[i], A)) { startPt = ClipExit(pts[i - 1], pts[i], A); startIdx = i; g.Doors.Add(startPt); break; }
            int endIdx = pts.Count - 1;
            LinkPoint endPt = pts[pts.Count - 1];
            for (int i = pts.Count - 2; i >= 0; i--)
                if (!InsideRect(pts[i], B)) { endPt = ClipExit(pts[i + 1], pts[i], B); endIdx = i; g.Doors.Add(endPt); break; }
            if (startIdx > endIdx + 1) { startIdx = 1; endIdx = pts.Count - 2; startPt = pts[0]; endPt = pts[pts.Count - 1]; }

            var prev = startPt;
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (System.Math.Abs(pts[i].X - prev.X) > 1e-5f || System.Math.Abs(pts[i].Y - prev.Y) > 1e-5f)
                    g.Segments.Add(new LinkSegment { A = prev, B = pts[i], EdgeIndex = edgeIndex });
                prev = pts[i];
            }
            if (System.Math.Abs(endPt.X - prev.X) > 1e-5f || System.Math.Abs(endPt.Y - prev.Y) > 1e-5f)
                g.Segments.Add(new LinkSegment { A = prev, B = endPt, EdgeIndex = edgeIndex });
        }

        static bool InsideRect(LinkPoint p, LinkNode n)
            => System.Math.Abs(p.X - n.CX) <= n.W * 0.5f && System.Math.Abs(p.Y - n.CY) <= n.H * 0.5f;

        static bool InsideInflated(int x, int y, LinkNode n)
            => System.Math.Abs(x - n.CX) <= n.W * 0.5f + RoadClearanceTiles
            && System.Math.Abs(y - n.CY) <= n.H * 0.5f + RoadClearanceTiles;

        /// <summary>Where the segment inside→outside crosses n's rect boundary (inPt is inside).</summary>
        static LinkPoint ClipExit(LinkPoint inPt, LinkPoint outPt, LinkNode n)
        {
            float hw = n.W * 0.5f, hh = n.H * 0.5f;
            float t = 1f;
            float dx = outPt.X - inPt.X, dy = outPt.Y - inPt.Y;
            if (dx > 1e-6f) t = System.Math.Min(t, (n.CX + hw - inPt.X) / dx);
            if (dx < -1e-6f) t = System.Math.Min(t, (n.CX - hw - inPt.X) / dx);
            if (dy > 1e-6f) t = System.Math.Min(t, (n.CY + hh - inPt.Y) / dy);
            if (dy < -1e-6f) t = System.Math.Min(t, (n.CY - hh - inPt.Y) / dy);
            if (t < 0f) t = 0f;
            return new LinkPoint { X = inPt.X + t * dx, Y = inPt.Y + t * dy };
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Minimal binary min-heap (Unity's .NET has no PriorityQueue). Push-sequence tie-break
        /// keeps popping order — and therefore the whole router — deterministic.</summary>
        class Heap
        {
            readonly List<(float f, int seq, int st)> a = new List<(float, int, int)>();
            int seq;
            public int Count => a.Count;
            public void Clear() { a.Clear(); seq = 0; }
            public void Push(float f, int st)
            {
                a.Add((f, seq++, st));
                int i = a.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (Less(a[i], a[p])) { (a[i], a[p]) = (a[p], a[i]); i = p; } else break;
                }
            }
            public int Pop(out float f)
            {
                var top = a[0]; f = top.f;
                a[0] = a[a.Count - 1]; a.RemoveAt(a.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < a.Count && Less(a[l], a[m])) m = l;
                    if (r < a.Count && Less(a[r], a[m])) m = r;
                    if (m == i) break;
                    (a[i], a[m]) = (a[m], a[i]); i = m;
                }
                return top.st;
            }
            static bool Less((float f, int seq, int st) x, (float f, int seq, int st) y)
                => x.f < y.f || (x.f == y.f && x.seq < y.seq);
        }
    }
}
