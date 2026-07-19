using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure, headless geometry for a building floor's FOOTPRINT — the union of its room rectangles,
    /// each expanded by a small outward margin, in TILE space. Used to draw the floor-0 contour as the
    /// building's actual SHAPE (L / T / …), not a bounding rectangle (spec C6), to test whether a room sits
    /// inside that shape (out-of-contour red flag), and to test whether a candidate point is inside it (new-
    /// room placement).
    ///
    /// Everything is axis-aligned rectangles, so the union boundary and rect-containment are computed EXACTLY
    /// via an ARRANGEMENT of the rectangles' edge coordinates — no rasterization / approximation. Because the
    /// arrangement's cell boundaries are exactly the rect edges, no rect edge crosses a cell interior, so a
    /// single centre sample per cell decides that whole cell. Tile space via <see cref="DungeonLayout.TilesPerAxis"/>
    /// and <see cref="DungeonProjection.EffectiveSize"/> — the same measures CompactLayout / the renderer use.
    /// No UnityEngine types (headless self-testable).</summary>
    public static class FloorFootprint
    {
        /// <summary>Outward margin (tiles) the contour is inflated by around the rooms — a small gap so the
        /// outline reads as the building's shell, not a tight trace on the walls. The out-of-contour flag and
        /// new-room placement use the SAME value so "inside the drawn contour" means one consistent thing.
        /// Provisional — user-tunable at the C6 checkpoint.</summary>
        public const float ContourMargin = 1.5f;

        struct Box { public float x0, y0, x1, y1; }

        static float ToTile(float norm) => norm * DungeonLayout.TilesPerAxis;

        static List<Box> ExpandedRects(InteriorFloor floor, float margin)
        {
            var rects = new List<Box>();
            if (floor == null) return rects;
            foreach (var r in floor.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                if (w <= 0 || h <= 0) continue;
                float cx = ToTile(r.X), cy = ToTile(r.Y);
                rects.Add(new Box
                {
                    x0 = cx - w * 0.5f - margin, y0 = cy - h * 0.5f - margin,
                    x1 = cx + w * 0.5f + margin, y1 = cy + h * 0.5f + margin
                });
            }
            return rects;
        }

        static bool CoveredByAny(List<Box> rects, float x, float y)
        {
            foreach (var r in rects)
                if (x > r.x0 && x < r.x1 && y > r.y0 && y < r.y1) return true;
            return false;
        }

        /// <summary>True iff tile-space point (xTiles,yTiles) lies inside the floor footprint (any expanded
        /// room rect). The "inside the drawn contour" test for new-room placement.</summary>
        public static bool CoversPoint(InteriorFloor floor, float margin, float xTiles, float yTiles)
            => CoveredByAny(ExpandedRects(floor, margin), xTiles, yTiles);

        /// <summary>True iff the tile-space footprint rect centred at (cx,cy) with size (w,h) lies ENTIRELY
        /// inside the floor footprint — i.e. it does NOT poke outside the drawn contour. Exact: builds the
        /// arrangement of the union rects' edges clipped to the query rect and checks every cell centre is
        /// covered. A room is red-flagged when this is FALSE.</summary>
        public static bool ContainsRect(InteriorFloor floor, float margin, float cx, float cy, float w, float h)
        {
            if (w <= 0 || h <= 0) return true;
            var rects = ExpandedRects(floor, margin);
            if (rects.Count == 0) return false;
            float qx0 = cx - w * 0.5f, qx1 = cx + w * 0.5f, qy0 = cy - h * 0.5f, qy1 = cy + h * 0.5f;

            var xs = ClampedEdges(rects, true, qx0, qx1);
            var ys = ClampedEdges(rects, false, qy0, qy1);
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                    if (!CoveredByAny(rects, (xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f))
                        return false;
            return true;
        }

        /// <summary>Boundary segments (tile space) of the floor footprint — the outline of the union of the
        /// expanded room rects. Each segment is one edge between a covered and an uncovered cell of the
        /// rectangle arrangement, so together they trace the building's SHAPE (concave notches of an L/T
        /// footprint and any interior hole included). Order is unspecified; the renderer draws each segment
        /// independently. Empty for an empty floor.</summary>
        public static List<(float x0, float y0, float x1, float y1)> OutlineSegments(InteriorFloor floor, float margin)
        {
            var segs = new List<(float, float, float, float)>();
            var rects = ExpandedRects(floor, margin);
            if (rects.Count == 0) return segs;

            var xs = AllEdges(rects, true);
            var ys = AllEdges(rects, false);
            int nx = xs.Count - 1, ny = ys.Count - 1;
            var covered = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    covered[i, j] = CoveredByAny(rects, (xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f);

            // Vertical boundary edges at x = xs[i]: emitted where coverage differs across column i-1 | i.
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    bool left = i > 0 && covered[i - 1, j];
                    bool right = i < nx && covered[i, j];
                    if (left != right) segs.Add((xs[i], ys[j], xs[i], ys[j + 1]));
                }
            // Horizontal boundary edges at y = ys[j]: emitted where coverage differs across row j-1 | j.
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    bool below = j > 0 && covered[i, j - 1];
                    bool above = j < ny && covered[i, j];
                    if (below != above) segs.Add((xs[i], ys[j], xs[i + 1], ys[j]));
                }
            return segs;
        }

        /// <summary>Exact area (tile²) enclosed by the floor footprint — the union of the expanded room rects,
        /// i.e. the region inside the drawn contour. Summed over the covered cells of the SAME rectangle
        /// arrangement the outline uses, so "area inside the contour" means exactly what the contour shows (an
        /// L/T notch removes its area). 0 for an empty floor. Used to decide, deterministically, how many rooms
        /// can fit a floor by area (unlike a single seed-dependent pack attempt).</summary>
        public static float UsableAreaTiles(InteriorFloor floor, float margin)
        {
            var rects = ExpandedRects(floor, margin);
            if (rects.Count == 0) return 0f;
            var xs = AllEdges(rects, true);
            var ys = AllEdges(rects, false);
            float area = 0f;
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                    if (CoveredByAny(rects, (xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f))
                        area += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]);
            return area;
        }

        // Sorted, de-duplicated edge coordinates (x or y) of all rects.
        static List<float> AllEdges(List<Box> rects, bool xAxis)
        {
            var raw = new List<float>();
            foreach (var r in rects) { raw.Add(xAxis ? r.x0 : r.y0); raw.Add(xAxis ? r.x1 : r.y1); }
            return Dedup(raw);
        }

        // Rect edges within (lo,hi) plus lo and hi — the cell boundaries of the arrangement clipped to a query.
        static List<float> ClampedEdges(List<Box> rects, bool xAxis, float lo, float hi)
        {
            var raw = new List<float> { lo, hi };
            foreach (var r in rects)
            {
                float a = xAxis ? r.x0 : r.y0, b = xAxis ? r.x1 : r.y1;
                if (a > lo && a < hi) raw.Add(a);
                if (b > lo && b < hi) raw.Add(b);
            }
            return Dedup(raw);
        }

        static List<float> Dedup(List<float> raw)
        {
            raw.Sort();
            var outl = new List<float>();
            foreach (var v in raw)
                if (outl.Count == 0 || v - outl[outl.Count - 1] > 1e-4f) outl.Add(v);
            return outl;
        }
    }
}
