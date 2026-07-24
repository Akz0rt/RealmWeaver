using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>THE ISOLATED, SWAPPABLE SETTLEMENT FENCE STAGE (Ц2.5). Derives a town's fence as the traced
    /// boundary of the rasterized, inflated union of its node rects — like a building's floor contour is
    /// derived from its rooms (FloorFootprint precedent), never stored. References only LinkNode / WallContour /
    /// WallPoint and System.* — never InteriorData, Room, SettlementGenerator or UnityEngine. TILE space in and
    /// out. Rules: no holes (outside flood-fill), one closed loop (stray-bridge + de-saddle), passes through
    /// gates (gates rasterized as points so the fence hugs them). When the fence approach changes, ONLY this
    /// file changes.
    ///
    /// Pipeline: rasterize buildings (inflated rects) + gates (single centre cell) into a boolean grid →
    /// bridge stray components into one 4-connected region → de-saddle diagonal pinches → flood-fill the
    /// OUTSIDE from the border so enclosed pockets stay inside (no holes) → trace the single boundary loop
    /// with inside-kept-on-the-right directed unit edges, then collapse collinear runs. Fully deterministic
    /// from its inputs (no RNG), so a re-derive with identical nodes yields an identical point list.</summary>
    public static class SettlementFence
    {
        /// <summary>How far the fence clears each building rect, in tiles (buildings inflated by this; gates
        /// are rasterized as bare points). TUNABLE — the DM eyeballs it at the checkpoint.</summary>
        public const float FenceMarginTiles = 2f;

        /// <summary>Grid margin beyond the town's AABB, in tiles — guarantees a full ring of non-town cells
        /// around the town for the outside flood-fill to seed from.</summary>
        const int GridMargin = 4;

        /// <summary>Half-width of the straight stray bridge, in tiles: 1 → a 3-tile-wide bridge.</summary>
        const int BridgeHalfWidth = 1;

        public static WallContour Derive(IReadOnlyList<LinkNode> buildings, IReadOnlyList<LinkNode> gates, float marginTiles)
        {
            if (buildings == null || buildings.Count == 0) return null;   // no town → no fence

            // 1. Grid AABB over inflated building rects + gate centres, expanded by GridMargin on all sides.
            float fMinX = float.MaxValue, fMinY = float.MaxValue, fMaxX = float.MinValue, fMaxY = float.MinValue;
            foreach (var b in buildings)
            {
                float hw = b.W * 0.5f + marginTiles, hh = b.H * 0.5f + marginTiles;
                if (b.CX - hw < fMinX) fMinX = b.CX - hw;
                if (b.CX + hw > fMaxX) fMaxX = b.CX + hw;
                if (b.CY - hh < fMinY) fMinY = b.CY - hh;
                if (b.CY + hh > fMaxY) fMaxY = b.CY + hh;
            }
            if (gates != null)
                foreach (var gp in gates)
                {
                    if (gp.CX < fMinX) fMinX = gp.CX;
                    if (gp.CX > fMaxX) fMaxX = gp.CX;
                    if (gp.CY < fMinY) fMinY = gp.CY;
                    if (gp.CY > fMaxY) fMaxY = gp.CY;
                }
            int minX = (int)System.Math.Floor(fMinX) - GridMargin, minY = (int)System.Math.Floor(fMinY) - GridMargin;
            int maxX = (int)System.Math.Ceiling(fMaxX) + GridMargin, maxY = (int)System.Math.Ceiling(fMaxY) + GridMargin;
            int gw = maxX - minX + 1, gh = maxY - minY + 1;
            var town = new bool[gw * gh];

            // 2. Rasterize: buildings = inflated rects (cell-CENTRE-in test), gates = single centre cell.
            foreach (var b in buildings)
            {
                float hw = b.W * 0.5f + marginTiles, hh = b.H * 0.5f + marginTiles;
                int x0 = (int)System.Math.Floor(b.CX - hw), x1 = (int)System.Math.Ceiling(b.CX + hw);
                int y0 = (int)System.Math.Floor(b.CY - hh), y1 = (int)System.Math.Ceiling(b.CY + hh);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float ccx = x + 0.5f, ccy = y + 0.5f;
                        if (ccx >= b.CX - hw && ccx <= b.CX + hw && ccy >= b.CY - hh && ccy <= b.CY + hh)
                            town[(y - minY) * gw + (x - minX)] = true;
                    }
            }
            if (gates != null)
                foreach (var gp in gates)
                {
                    int gx = (int)System.Math.Floor(gp.CX), gy = (int)System.Math.Floor(gp.CY);
                    town[(gy - minY) * gw + (gx - minX)] = true;   // a POINT, no inflation (see class doc)
                }

            // 3. Bridge stray components into one connected region.
            BridgeStrays(town, gw, gh);
            // 4. De-saddle diagonal pinches so the boundary traces as a single simple rectilinear loop.
            DeSaddle(town, gw, gh);
            // 5. Flood-fill the OUTSIDE from the border; inside = !outside (enclosed pockets stay inside → no hole).
            var inside = InsideFromOutsideFill(town, gw, gh);
            // 6. Trace the single boundary loop into an ordered WallContour (tile space).
            return TraceBoundary(inside, gw, gh, minX, minY);
        }

        // ---- helpers (all deterministic, grid-space except TraceBoundary, which emits tile space) ----

        /// <summary>Rasterize a straight 3-tile-wide (BridgeHalfWidth on each side) bridge from every stray
        /// 4-connected component to the MAIN (largest) one, so the whole town is one connected region — a
        /// prerequisite for a single traced loop. Each stray bridges from the component cell nearest its
        /// centroid to the nearest MAIN cell, as an axis-then-axis (x then y) L. Deterministic: components are
        /// discovered in ascending cell order, and every nearest-tie breaks to the lower cell index.</summary>
        static void BridgeStrays(bool[] town, int gw, int gh)
        {
            var comp = new int[gw * gh];
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            var components = new List<List<int>>();          // component id → its cell indices
            var stack = new List<int>();
            for (int c0 = 0; c0 < town.Length; c0++)
            {
                if (!town[c0] || comp[c0] != -1) continue;
                int cid = components.Count;
                var cells = new List<int>();
                comp[c0] = cid;
                stack.Clear();
                stack.Add(c0);
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    cells.Add(c);
                    int cx = c % gw, cy = c / gw;
                    if (cx > 0)      { int n = c - 1;  if (town[n] && comp[n] == -1) { comp[n] = cid; stack.Add(n); } }
                    if (cx < gw - 1) { int n = c + 1;  if (town[n] && comp[n] == -1) { comp[n] = cid; stack.Add(n); } }
                    if (cy > 0)      { int n = c - gw; if (town[n] && comp[n] == -1) { comp[n] = cid; stack.Add(n); } }
                    if (cy < gh - 1) { int n = c + gw; if (town[n] && comp[n] == -1) { comp[n] = cid; stack.Add(n); } }
                }
                components.Add(cells);
            }
            if (components.Count <= 1) return;

            // Largest component (ties: lower id, i.e. lower min-cell-index) is MAIN.
            int main = 0;
            for (int i = 1; i < components.Count; i++)
                if (components[i].Count > components[main].Count) main = i;

            // Bridge every non-main component (ascending id == ascending min-cell-index) to MAIN.
            for (int cid = 0; cid < components.Count; cid++)
            {
                if (cid == main) continue;
                var cells = components[cid];
                long sumX = 0, sumY = 0;
                foreach (var c in cells) { sumX += c % gw; sumY += c / gw; }
                int centX = (int)(sumX / cells.Count), centY = (int)(sumY / cells.Count);

                // Start = the component cell nearest the centroid (ties: lower cell index).
                int start = cells[0];
                long bestS = long.MaxValue;
                foreach (var c in cells)
                {
                    long dx = c % gw - centX, dy = c / gw - centY;
                    long dd = dx * dx + dy * dy;
                    if (dd < bestS) { bestS = dd; start = c; }
                }
                int sxg = start % gw, syg = start / gw;

                // Target = the nearest MAIN cell to start (ties: lower cell index).
                int target = components[main][0];
                long bestT = long.MaxValue;
                foreach (var c in components[main])
                {
                    long dx = c % gw - sxg, dy = c / gw - syg;
                    long dd = dx * dx + dy * dy;
                    if (dd < bestT) { bestT = dd; target = c; }
                }
                int txg = target % gw, tyg = target / gw;

                StampThick(town, gw, gh, sxg, syg, txg, syg);   // horizontal leg at row syg
                StampThick(town, gw, gh, txg, syg, txg, tyg);   // vertical leg at col txg
            }
        }

        /// <summary>Mark a 3-tile-wide axis-aligned strip of town cells along the segment (x0,y0)-(x1,y1)
        /// (one of the two must be axis-aligned; the L's two legs are stamped separately). Clamped to grid.</summary>
        static void StampThick(bool[] town, int gw, int gh, int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)   // horizontal leg
            {
                int lo = System.Math.Min(x0, x1), hi = System.Math.Max(x0, x1);
                for (int x = lo; x <= hi; x++)
                    for (int dy = -BridgeHalfWidth; dy <= BridgeHalfWidth; dy++)
                    {
                        int y = y0 + dy;
                        if (x >= 0 && x < gw && y >= 0 && y < gh) town[y * gw + x] = true;
                    }
            }
            else            // vertical leg (x0 == x1)
            {
                int lo = System.Math.Min(y0, y1), hi = System.Math.Max(y0, y1);
                for (int y = lo; y <= hi; y++)
                    for (int dx = -BridgeHalfWidth; dx <= BridgeHalfWidth; dx++)
                    {
                        int x = x0 + dx;
                        if (x >= 0 && x < gw && y >= 0 && y < gh) town[y * gw + x] = true;
                    }
            }
        }

        /// <summary>Flip the two empty cells of every diagonal-checkerboard 2×2 block to town, iterated to a
        /// fixed point. A checkerboard (town on one diagonal, empty on the other) is a diagonal pinch that
        /// would give the trace an ambiguous corner (two edges starting at the same corner); filling it makes
        /// the boundary a single simple rectilinear loop. A flip can create a new checkerboard, so repeat
        /// until a full scan makes no change — bounded by the cell count (each pass adds ≥1 town cell).</summary>
        static void DeSaddle(bool[] town, int gw, int gh)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int y = 0; y < gh - 1; y++)
                    for (int x = 0; x < gw - 1; x++)
                    {
                        int a = y * gw + x;             // (x,   y)
                        int b = y * gw + (x + 1);       // (x+1, y)
                        int c = (y + 1) * gw + x;       // (x,   y+1)
                        int d = (y + 1) * gw + (x + 1); // (x+1, y+1)
                        bool ta = town[a], tb = town[b], tc = town[c], td = town[d];
                        if (ta && td && !tb && !tc) { town[b] = true; town[c] = true; changed = true; }
                        else if (tb && tc && !ta && !td) { town[a] = true; town[d] = true; changed = true; }
                    }
            }
        }

        /// <summary>4-connected BFS over non-town cells seeded from every non-town border cell → the OUTSIDE
        /// set. Returns inside = !outside, so an enclosed empty pocket (never reached from the border) reads as
        /// inside and no hole ever appears in the traced fence (Rule 1, by construction).</summary>
        static bool[] InsideFromOutsideFill(bool[] town, int gw, int gh)
        {
            var outside = new bool[gw * gh];
            var stack = new List<int>();
            for (int x = 0; x < gw; x++)
            {
                int top = x, bot = (gh - 1) * gw + x;
                if (!town[top] && !outside[top]) { outside[top] = true; stack.Add(top); }
                if (!town[bot] && !outside[bot]) { outside[bot] = true; stack.Add(bot); }
            }
            for (int y = 0; y < gh; y++)
            {
                int left = y * gw, right = y * gw + (gw - 1);
                if (!town[left] && !outside[left]) { outside[left] = true; stack.Add(left); }
                if (!town[right] && !outside[right]) { outside[right] = true; stack.Add(right); }
            }
            while (stack.Count > 0)
            {
                int c = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int cx = c % gw, cy = c / gw;
                if (cx > 0)      { int n = c - 1;  if (!town[n] && !outside[n]) { outside[n] = true; stack.Add(n); } }
                if (cx < gw - 1) { int n = c + 1;  if (!town[n] && !outside[n]) { outside[n] = true; stack.Add(n); } }
                if (cy > 0)      { int n = c - gw; if (!town[n] && !outside[n]) { outside[n] = true; stack.Add(n); } }
                if (cy < gh - 1) { int n = c + gw; if (!town[n] && !outside[n]) { outside[n] = true; stack.Add(n); } }
            }
            var inside = new bool[gw * gh];
            for (int i = 0; i < inside.Length; i++) inside[i] = !outside[i];
            return inside;
        }

        /// <summary>Trace the single boundary of the inside region into an ordered tile-space WallContour.
        /// For each inside cell, each 4-neighbour that is !inside (outside or off-grid) contributes ONE
        /// directed unit edge along the shared side, oriented so inside is kept on the RIGHT (a clockwise
        /// outer loop). Cell (gx,gy) spans tile square [x,x+1]×[y,y+1] with x=minX+gx, y=minY+gy. De-saddle
        /// guarantees every corner is the start of exactly one edge, so start→end forms a bijection; walk it
        /// from the lexicographically-smallest corner (deterministic) and collapse collinear runs into the
        /// turn corners. If the edges do not form a single loop this returns the partial loop it traced — the
        /// self-tests catch that regression.</summary>
        static WallContour TraceBoundary(bool[] inside, int gw, int gh, int minX, int minY)
        {
            var next = new Dictionary<(int, int), (int, int)>();
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    if (!inside[gy * gw + gx]) continue;
                    int x = minX + gx, y = minY + gy;
                    bool insL = gx > 0      && inside[gy * gw + (gx - 1)];
                    bool insR = gx < gw - 1 && inside[gy * gw + (gx + 1)];
                    bool insD = gy > 0      && inside[(gy - 1) * gw + gx];
                    bool insU = gy < gh - 1 && inside[(gy + 1) * gw + gx];
                    if (!insD) next[(x + 1, y)]     = (x, y);          // bottom side: (x+1,y) -> (x,y)
                    if (!insL) next[(x, y)]         = (x, y + 1);      // left side:   (x,y)   -> (x,y+1)
                    if (!insU) next[(x, y + 1)]     = (x + 1, y + 1);  // top side:    (x,y+1) -> (x+1,y+1)
                    if (!insR) next[(x + 1, y + 1)] = (x + 1, y);      // right side:  (x+1,y+1) -> (x+1,y)
                }
            if (next.Count == 0) return null;

            // Deterministic start: the lexicographically-smallest corner (min Y, then min X).
            (int, int) start = default;
            bool have = false;
            foreach (var kv in next)
            {
                var k = kv.Key;
                if (!have || k.Item2 < start.Item2 || (k.Item2 == start.Item2 && k.Item1 < start.Item1))
                { start = k; have = true; }
            }

            // Walk the loop start → next[start] → ... → start.
            var corners = new List<(int x, int y)>();
            var cur = start;
            int guard = next.Count + 1;
            do
            {
                corners.Add(cur);
                if (!next.TryGetValue(cur, out cur)) break;
                guard--;
            } while (cur != start && guard > 0);

            // Single-loop guard: the walk must have consumed every directed edge. If it didn't, a hole loop or
            // a disconnected boundary component exists (BridgeStrays/DeSaddle/InsideFromOutsideFill failed to
            // guarantee one connected inside region with no holes) — refuse to silently drop it.
            if (corners.Count != next.Count) return null;

            // Collapse collinear runs → the turn corners only.
            var contour = new WallContour();
            int n = corners.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = corners[(i - 1 + n) % n];
                var here = corners[i];
                var nxt = corners[(i + 1) % n];
                int dx1 = here.x - prev.x, dy1 = here.y - prev.y;
                int dx2 = nxt.x - here.x, dy2 = nxt.y - here.y;
                if (dx1 != dx2 || dy1 != dy2)
                    contour.Points.Add(new WallPoint { X = here.x, Y = here.y });
            }
            return contour;
        }
    }
}
