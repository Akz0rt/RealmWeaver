using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>One vertex of a settlement wall, in normalized 0..1 space (same frame as Room.X/Y).</summary>
    public class WallPoint
    {
        public float X;
        public float Y;
    }

    /// <summary>A settlement's wall: a closed polyline in normalized space. Stored (not derived) because a
    /// town is walled FIRST and built inside. Pure geometry, no Unity. Gates are NOT stored here — a gate is
    /// a Room node whose position lies on this line; the renderer derives the gap from proximity.</summary>
    public class WallContour
    {
        public List<WallPoint> Points = new List<WallPoint>();

        /// <summary>At least 3 points and a non-zero span — guards degenerate contours before they reach
        /// point-in-polygon math.</summary>
        public bool IsClosedSane()
        {
            if (Points == null || Points.Count < 3) return false;
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            foreach (var p in Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            return (maxX - minX) > 1e-3f && (maxY - minY) > 1e-3f;
        }

        /// <summary>Ray-cast point-in-polygon (closed: last point connects to first).</summary>
        public bool Contains(float x, float y)
        {
            if (Points == null || Points.Count < 3) return false;
            bool inside = false;
            int n = Points.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = Points[i]; var pj = Points[j];
                bool crosses = (pi.Y > y) != (pj.Y > y);
                if (crosses)
                {
                    float t = (y - pi.Y) / (pj.Y - pi.Y);
                    float xCross = pi.X + t * (pj.X - pi.X);
                    if (x < xCross) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Shortest distance from (x,y) to any wall segment. Used to decide whether a gate node
        /// sits on the wall (≈0) and where the renderer opens a gap.</summary>
        public float DistanceToEdge(float x, float y)
        {
            float best = float.MaxValue;
            int n = Points.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float d = SegDist(x, y, Points[j].X, Points[j].Y, Points[i].X, Points[i].Y);
                if (d < best) best = d;
            }
            return best;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 0f ? 0f : ((px - ax) * dx + (py - ay) * dy) / len2;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            float cx = ax + t * dx, cy = ay + t * dy;
            float ex = px - cx, ey = py - cy;
            return (float)System.Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>A rounded, slightly-jittered regular polygon centred at (cx,cy). Deterministic from
        /// seed. `jitter` is the fraction of `radius` each vertex may wobble in and out, so a town reads as
        /// a walled shape rather than a perfect polygon.</summary>
        public static WallContour Rounded(int seed, float cx, float cy, float radius, int sides, float jitter)
        {
            var rng = new System.Random(seed);
            var c = new WallContour();
            for (int i = 0; i < sides; i++)
            {
                double ang = 2.0 * System.Math.PI * i / sides;
                float wob = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float r = radius * wob;
                c.Points.Add(new WallPoint
                {
                    X = cx + (float)System.Math.Cos(ang) * r,
                    Y = cy + (float)System.Math.Sin(ang) * r,
                });
            }
            return c;
        }
    }
}
