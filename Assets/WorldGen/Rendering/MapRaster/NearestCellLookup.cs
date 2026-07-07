using System;
using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Grid-bucket поиск ближайшей клетки/клеток в радиусе по позиции VoronoiCell.Site.
    /// Размер бакета ~= minPointDistance (типичное расстояние между сайтами после Lloyd-релаксации),
    /// поэтому поиск амортизированно O(1) на пиксель при равномерном распределении точек.
    /// </summary>
    public class NearestCellLookup
    {
        readonly Dictionary<(int, int), List<VoronoiCell>> buckets = new Dictionary<(int, int), List<VoronoiCell>>();
        readonly float bucketSize;
        const int MaxRingSearch = 128;

        public NearestCellLookup(IEnumerable<VoronoiCell> cells, float bucketSize)
        {
            this.bucketSize = MathF.Max(bucketSize, 0.001f);
            foreach (var cell in cells)
            {
                var key = KeyOf(cell.Site.X, cell.Site.Y);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<VoronoiCell>();
                    buckets[key] = list;
                }
                list.Add(cell);
            }
        }

        (int, int) KeyOf(float x, float y) =>
            ((int)MathF.Floor(x / bucketSize), (int)MathF.Floor(y / bucketSize));

        /// <summary>Ближайшая клетка к точке. null только если в индексе нет вообще ни одной клетки.</summary>
        public VoronoiCell FindNearest(Vector2 point)
        {
            int bx = (int)MathF.Floor(point.X / bucketSize);
            int by = (int)MathF.Floor(point.Y / bucketSize);

            VoronoiCell best = null;
            float bestDistSq = float.MaxValue;

            for (int ring = 0; ring <= MaxRingSearch; ring++)
            {
                ScanRing(bx, by, ring, cell =>
                {
                    float dx = cell.Site.X - point.X, dy = cell.Site.Y - point.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq) { bestDistSq = distSq; best = cell; }
                });

                // Минимальное возможное расстояние до чего-либо в кольце (ring+1) - ring*bucketSize
                // (стандартный результат для grid-bucket поиска). Если текущий кандидат уже ближе -
                // расширять поиск дальше бессмысленно.
                if (best != null && MathF.Sqrt(bestDistSq) <= ring * bucketSize) break;
            }

            return best;
        }

        /// <summary>Все клетки в радиусе radius от точки, с их евклидовым расстоянием до неё.</summary>
        public IEnumerable<(VoronoiCell cell, float distance)> FindWithinRadius(Vector2 point, float radius)
        {
            int bx = (int)MathF.Floor(point.X / bucketSize);
            int by = (int)MathF.Floor(point.Y / bucketSize);
            int ringSpan = (int)MathF.Ceiling(radius / bucketSize) + 1;

            var results = new List<(VoronoiCell, float)>();
            for (int oy = -ringSpan; oy <= ringSpan; oy++)
            {
                for (int ox = -ringSpan; ox <= ringSpan; ox++)
                {
                    if (!buckets.TryGetValue((bx + ox, by + oy), out var list)) continue;
                    foreach (var cell in list)
                    {
                        float dx = cell.Site.X - point.X, dy = cell.Site.Y - point.Y;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist <= radius) results.Add((cell, dist));
                    }
                }
            }
            return results;
        }

        void ScanRing(int bx, int by, int ring, Action<VoronoiCell> visit)
        {
            if (ring == 0)
            {
                if (buckets.TryGetValue((bx, by), out var center))
                    foreach (var c in center) visit(c);
                return;
            }

            for (int dx = -ring; dx <= ring; dx++)
            {
                TryVisitBucket(bx + dx, by - ring, visit);
                TryVisitBucket(bx + dx, by + ring, visit);
            }
            for (int dy = -ring + 1; dy <= ring - 1; dy++)
            {
                TryVisitBucket(bx - ring, by + dy, visit);
                TryVisitBucket(bx + ring, by + dy, visit);
            }
        }

        void TryVisitBucket(int bx, int by, Action<VoronoiCell> visit)
        {
            if (!buckets.TryGetValue((bx, by), out var list)) return;
            foreach (var c in list) visit(c);
        }
    }
}
