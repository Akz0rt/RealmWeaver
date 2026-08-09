using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Чистая геометрия кисти рек (без UnityEngine — гоняется самотестами без сцены).
    /// Три шага, каждый со своей задачей:
    ///   • AppendAnchor  — цепочка клеток под курсором без повторов подряд;
    ///   • TrimToShore   — концы, ушедшие в воду, обрезаются РОВНО по кромке водоёма;
    ///   • Smooth        — по опорным точкам строится плавная кривая (Catmull-Rom).
    /// Плюс DistanceToPolyline — попадание курсора по нарисованной реке (режим «Стереть»).
    /// </summary>
    public static class RiverPaintOps
    {
        /// <summary>Добавляет точку в цепочку, если она не совпадает с последней (курсор всё ещё
        /// в той же клетке). Возвращает true, если цепочка выросла.</summary>
        public static bool AppendAnchor(List<Vector2> anchors, Vector2 site)
        {
            if (anchors == null) return false;
            if (anchors.Count > 0 && anchors[anchors.Count - 1] == site) return false;
            anchors.Add(site);
            return true;
        }

        /// <summary>
        /// Обрезает концы русла по кромке воды. Мазок, заведённый в море или озеро, не должен
        /// рисоваться ПОВЕРХ водоёма: у воды свой градиент глубины, рябь и свечение берега, и
        /// ровная линия одного цвета легла бы по ним заметной полосой. Поэтому русло доводится
        /// до кромки — и там кончается.
        ///
        /// Кромка берётся не на глаз: у соседних клеток Вороного общее ребро — это серединный
        /// перпендикуляр между их центрами, так что СЕРЕДИНА отрезка «центр суши → центр воды»
        /// лежит точно на границе клеток, то есть на береговой линии.
        ///
        /// Клетки воды ВНУТРИ мазка (река прошла через озерцо насквозь) сохраняются как есть —
        /// подрезаются только концы. Мазок целиком по воде рекой не становится: возвращается
        /// пустой список, и вызывающий его выбрасывает.
        /// </summary>
        public static List<Vector2> TrimToShore(IReadOnlyList<Vector2> sites, IReadOnlyList<bool> isWater)
        {
            var result = new List<Vector2>();
            if (sites == null || isWater == null || sites.Count != isWater.Count) return result;

            int first = -1, last = -1;
            for (int i = 0; i < sites.Count; i++)
            {
                if (isWater[i]) continue;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0) return result;   // ни одной клетки суши — реки нет

            if (first > 0) result.Add(Midpoint(sites[first - 1], sites[first]));
            for (int i = first; i <= last; i++) result.Add(sites[i]);
            if (last < sites.Count - 1) result.Add(Midpoint(sites[last], sites[last + 1]));

            if (result.Count < 2) result.Clear();  // одна точка — это не река
            return result;
        }

        static Vector2 Midpoint(Vector2 a, Vector2 b) => new Vector2((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

        /// <summary>
        /// Сглаживает ломаную по опорным точкам сплайном Catmull-Rom: кривая ПРОХОДИТ через каждую
        /// опорную точку, а между ними изгибается, поэтому река получается извилистой, а не
        /// сложенной из отрезков. Крайние точки дублируются как «виртуальные соседи», иначе у
        /// первого и последнего сегмента не из чего считать касательную.
        /// </summary>
        public static List<Vector2> Smooth(IReadOnlyList<Vector2> anchors, int subdivisions = 8)
        {
            var result = new List<Vector2>();
            if (anchors == null || anchors.Count == 0) return result;
            if (anchors.Count < 3)
            {
                result.AddRange(anchors);
                return result;
            }

            int n = anchors.Count;
            int steps = Math.Max(1, subdivisions);
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = anchors[Math.Max(i - 1, 0)];
                Vector2 p1 = anchors[i];
                Vector2 p2 = anchors[i + 1];
                Vector2 p3 = anchors[Math.Min(i + 2, n - 1)];
                for (int s = 0; s < steps; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, (float)s / steps));
            }
            result.Add(anchors[n - 1]);
            return result;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                         + (-p0 + p2) * t
                         + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                         + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Расстояние от точки до ломаной — именно до ОТРЕЗКОВ, а не до их концов:
        /// клик по середине длинного плавного участка обязан попадать в реку.</summary>
        public static float DistanceToPolyline(IReadOnlyList<Vector2> points, Vector2 p)
        {
            if (points == null || points.Count == 0) return float.MaxValue;
            if (points.Count == 1) return Vector2.Distance(points[0], p);

            float best = float.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
            {
                float d = DistanceToSegment(points[i], points[i + 1], p);
                if (d < best) best = d;
            }
            return best;
        }

        static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-8f) return Vector2.Distance(a, p);
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            return Vector2.Distance(a + ab * t, p);
        }
    }
}
