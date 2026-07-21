using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>One undoable painting gesture: press → drag → release. Records each changed cell's
    /// PREVIOUS value exactly once, on first touch, so undo restores the state at press time even when a
    /// cell is painted several times mid-stroke. Same shape as BrushUndoManager's per-stroke snapshot on
    /// the world map — the idea transfers, the code does not (that one is keyed by VoronoiCell).</summary>
    public class BattleGridStroke
    {
        public readonly List<int> Indices = new List<int>();
        public readonly List<GridCell> Previous = new List<GridCell>();
        readonly HashSet<int> touched = new HashSet<int>();

        public bool IsEmpty => Indices.Count == 0;

        /// <summary>Write one cell, recording it if this is its first change in this stroke. A write that
        /// changes nothing is not recorded — an idle click must not push an undo step that does nothing.</summary>
        public void Paint(GridBuffer buf, int x, int y, GridCell value)
        {
            if (!buf.InBounds(x, y)) return;
            var prev = buf.Get(x, y);
            if (prev == value) return;
            int idx = buf.Index(x, y);
            if (touched.Add(idx)) { Indices.Add(idx); Previous.Add(prev); }
            buf.Set(x, y, value);
        }
    }

    /// <summary>Pure painting operations over a GridBuffer. Every one takes the stroke that will record
    /// it, so brush, rectangle and fill are all a single undo step by construction.</summary>
    public static class BattleGridOps
    {
        /// <summary>Square brush of odd side `size` (1/3/5) CENTRED on (cx,cy). Odd sizes only: an even
        /// square has no centre cell, so the highlight could not match what gets painted.</summary>
        public static void Stamp(GridBuffer buf, BattleGridStroke stroke, int cx, int cy, int size, GridCell value)
        {
            int r = size / 2;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    stroke.Paint(buf, x, y, value);
        }

        /// <summary>Stamp along a Bresenham line. The cursor can jump several cells between frames; without
        /// this the stroke comes out as a dotted line.</summary>
        public static void Line(GridBuffer buf, BattleGridStroke stroke, int x0, int y0, int x1, int y1, int size, GridCell value)
        {
            int dx = System.Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -System.Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Stamp(buf, stroke, x0, y0, size, value);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Filled rectangle between two corners in any order.</summary>
        public static void Rect(GridBuffer buf, BattleGridStroke stroke, int x0, int y0, int x1, int y1, GridCell value)
        {
            int lo_x = x0 < x1 ? x0 : x1, hi_x = x0 < x1 ? x1 : x0;
            int lo_y = y0 < y1 ? y0 : y1, hi_y = y0 < y1 ? y1 : y0;
            for (int y = lo_y; y <= hi_y; y++)
                for (int x = lo_x; x <= hi_x; x++)
                    stroke.Paint(buf, x, y, value);
        }

        /// <summary>Flood fill from (x,y) across cells holding the SAME value as the origin, by
        /// 4-neighbours. Diagonals are excluded on purpose: a diagonal pinch reads as a closed wall to a
        /// player, so paint must not leak through it either.</summary>
        public static void Fill(GridBuffer buf, BattleGridStroke stroke, int x, int y, GridCell value)
        {
            if (!buf.InBounds(x, y)) return;
            var target = buf.Get(x, y);
            if (target == value) return;

            var queue = new Queue<int>();
            var queued = new HashSet<int>();
            queue.Enqueue(buf.Index(x, y));
            queued.Add(buf.Index(x, y));

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int px = idx % buf.Width, py = idx / buf.Width;
                if (buf.Get(px, py) != target) continue;
                stroke.Paint(buf, px, py, value);
                Enqueue(px + 1, py); Enqueue(px - 1, py);
                Enqueue(px, py + 1); Enqueue(px, py - 1);
            }

            void Enqueue(int nx, int ny)
            {
                if (!buf.InBounds(nx, ny)) return;
                if (buf.Get(nx, ny) != target) return;
                int i = buf.Index(nx, ny);
                if (queued.Add(i)) queue.Enqueue(i);
            }
        }

        /// <summary>How many NON-EMPTY cells a shrink would drop. Empty cells are not counted: warning
        /// "384 cells will be lost" when the DM drew nothing there is noise, and noise gets clicked through.</summary>
        public static int CountLostOnResize(GridBuffer buf, int newWidth, int newHeight)
        {
            int lost = 0;
            for (int y = 0; y < buf.Height; y++)
                for (int x = 0; x < buf.Width; x++)
                    if ((x >= newWidth || y >= newHeight) && buf.Get(x, y) != GridCell.Empty) lost++;
            return lost;
        }

        /// <summary>Resize anchored at the BOTTOM-LEFT corner: every surviving cell keeps its coordinates,
        /// new cells arrive Empty. Anchoring anywhere else would slide the DM's drawing under them.</summary>
        public static GridBuffer Resize(GridBuffer buf, int newWidth, int newHeight)
        {
            var next = new GridBuffer(newWidth, newHeight);
            for (int y = 0; y < newHeight && y < buf.Height; y++)
                for (int x = 0; x < newWidth && x < buf.Width; x++)
                    next.Set(x, y, buf.Get(x, y));
            return next;
        }
    }
}
