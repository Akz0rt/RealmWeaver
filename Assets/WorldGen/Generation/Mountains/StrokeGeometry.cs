using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §2 «От мазка к пятну»: расстояния между отрезками и проверка того, что два мазка задевают
    /// друг друга. Мазки, которые пересекаются, обязаны считаться одним объектом — полукольцо плюс
    /// замыкающий мазок это кольцо, а не две дуги, и оси у них общие.
    ///
    /// Порт `ptSegDist`, `segSegDist`, `partBBox`, `partsOverlap` из прототипа.
    /// </summary>
    public static class StrokeGeometry
    {
        /// <summary>Расстояние от точки до отрезка.</summary>
        public static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float len2 = d.LengthSquared();
            float t = len2 > 0f ? Vector2.Dot(p - a, d) / len2 : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return (p - (a + d * t)).Length();
        }

        /// <summary>Расстояние между отрезками: ноль, если они пересекаются, иначе минимум из
        /// четырёх «точка–отрезок». Пересечение проверяется отдельно, потому что у скрещивающихся
        /// отрезков ни один конец не обязан быть близко к другому отрезку.</summary>
        public static float SegmentSegmentDistance(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            Vector2 d1 = a2 - a1, d2 = b2 - b1;
            float den = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(den) > 1e-9f)
            {
                Vector2 w = b1 - a1;
                float s = (w.X * d2.Y - w.Y * d2.X) / den;
                float u = (w.X * d1.Y - w.Y * d1.X) / den;
                if (s >= 0f && s <= 1f && u >= 0f && u <= 1f) return 0f;
            }
            float best = PointSegmentDistance(a1, b1, b2);
            best = Math.Min(best, PointSegmentDistance(a2, b1, b2));
            best = Math.Min(best, PointSegmentDistance(b1, a1, a2));
            best = Math.Min(best, PointSegmentDistance(b2, a1, a2));
            return best;
        }

        /// <summary>Габариты мазка с учётом радиуса кисти: minX, minY, maxX, maxY.</summary>
        public static void Bounds(MountainStroke stroke, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.PositiveInfinity;
            maxX = maxY = float.NegativeInfinity;
            foreach (var p in stroke.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            minX -= stroke.Radius; minY -= stroke.Radius;
            maxX += stroke.Radius; maxY += stroke.Radius;
        }

        /// <summary>Два мазка задевают друг друга, если у них есть отрезки ближе, чем сумма радиусов
        /// кистей. Сначала отсечение по габаритам: на карте мазков будут сотни, и полный перебор
        /// отрезков для каждой пары обошёлся бы дороже всего остального вместе взятого.</summary>
        public static bool Overlap(MountainStroke a, MountainStroke b)
        {
            Bounds(a, out float aMinX, out float aMinY, out float aMaxX, out float aMaxY);
            Bounds(b, out float bMinX, out float bMinY, out float bMaxX, out float bMaxY);
            if (aMinX > bMaxX || bMinX > aMaxX || aMinY > bMaxY || bMinY > aMaxY) return false;

            float reach = a.Radius + b.Radius;
            var p = a.Points;
            var q = b.Points;
            int np = Math.Max(1, p.Count - 1);
            int nq = Math.Max(1, q.Count - 1);
            for (int i = 0; i < np; i++)
            {
                Vector2 a1 = p[i], a2 = p[Math.Min(i + 1, p.Count - 1)];
                for (int j = 0; j < nq; j++)
                {
                    Vector2 b1 = q[j], b2 = q[Math.Min(j + 1, q.Count - 1)];
                    if (SegmentSegmentDistance(a1, a2, b1, b2) <= reach) return true;
                }
            }
            return false;
        }

        /// <summary>Кратчайшее расстояние от точки до всей ломаной мазка.</summary>
        public static float DistanceToStroke(Vector2 p, MountainStroke stroke)
        {
            var pts = stroke.Points;
            if (pts.Count == 0) return float.PositiveInfinity;
            if (pts.Count == 1) return (p - pts[0]).Length();

            float best = float.PositiveInfinity;
            for (int i = 1; i < pts.Count; i++)
                best = Math.Min(best, PointSegmentDistance(p, pts[i - 1], pts[i]));
            return best;
        }

        /// <summary>Точка накрыта мазком, если она ближе радиуса кисти к его ломаной.</summary>
        public static bool Covers(MountainStroke stroke, Vector2 p) =>
            DistanceToStroke(p, stroke) <= stroke.Radius;

        /// <summary>Габариты набора мазков.</summary>
        public static void Bounds(IReadOnlyList<MountainStroke> strokes,
                                  out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.PositiveInfinity;
            maxX = maxY = float.NegativeInfinity;
            foreach (var s in strokes)
            {
                Bounds(s, out float x0, out float y0, out float x1, out float y1);
                if (x0 < minX) minX = x0;
                if (y0 < minY) minY = y0;
                if (x1 > maxX) maxX = x1;
                if (y1 > maxY) maxY = y1;
            }
        }
    }
}
