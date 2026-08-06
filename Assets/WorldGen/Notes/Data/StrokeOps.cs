using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>Чистка цепочки точек, выполняемая ОДИН РАЗ при отпускании кнопки.
    ///
    /// ЧТО ЗДЕСЬ НЕ ДЕЛАЕТСЯ: толщина. Она пишется в точку сразу при её появлении и чисткой не
    /// трогается, потому что скорость известна только в тот миг, когда точка появилась, и из
    /// готовой цепочки её уже не восстановить. Следствие, которое видит ДМ: при отпускании линия
    /// меняет ФОРМУ, но не толщину — прыжок ровно один, а не два наложенных.
    ///
    /// «НЕ ПЕРЕБОРЩИТЬ СО СГЛАЖИВАНИЕМ» — прямое требование ДМ, и у него есть проверяемая часть:
    /// при SmoothingStrength = 0 всё обязано выродиться в тождество, чтобы сглаживание можно было
    /// выключить одним числом, а не выкидыванием кода.</summary>
    public static class StrokeOps
    {
        public const float MinPointDistance = 0.002f;
        public const float SmoothingStrength = 0.35f;
        public const int SmoothingWindow = 5;
        public const float CornerAngleDegrees = 40f;
        public const float SimplifyEpsilon = 0.002f;

        public static List<StrokePoint> Cleanup(List<StrokePoint> points)
        {
            var deduped = Dedupe(points, MinPointDistance);
            var smoothed = Smooth(deduped, SmoothingStrength, SmoothingWindow, CornerAngleDegrees);
            return Simplify(smoothed, SimplifyEpsilon);
        }

        public static List<StrokePoint> Dedupe(List<StrokePoint> points, float minDistance)
        {
            var kept = new List<StrokePoint>();
            if (points == null) return kept;
            foreach (var p in points)
            {
                if (kept.Count > 0)
                {
                    var last = kept[kept.Count - 1];
                    float dx = p.X - last.X, dy = p.Y - last.Y;
                    if (dx * dx + dy * dy < minDistance * minDistance) continue;
                }
                kept.Add(p);
            }
            return kept;
        }

        /// <summary>Индексы точек, в которых направление поворачивает круче порога. Первая и
        /// последняя не считаются углами — они и так закреплены.</summary>
        public static List<int> CornerIndices(List<StrokePoint> points, float angleDegrees)
        {
            var corners = new List<int>();
            if (points == null || points.Count < 3) return corners;
            float cosLimit = Mathf.Cos(angleDegrees * Mathf.Deg2Rad);

            for (int i = 1; i < points.Count - 1; i++)
            {
                float ax = points[i].X - points[i - 1].X, ay = points[i].Y - points[i - 1].Y;
                float bx = points[i + 1].X - points[i].X, by = points[i + 1].Y - points[i].Y;
                float la = Mathf.Sqrt(ax * ax + ay * ay), lb = Mathf.Sqrt(bx * bx + by * by);
                if (la < 0.00001f || lb < 0.00001f) continue;
                float cos = (ax * bx + ay * by) / (la * lb);
                if (cos < cosLimit) corners.Add(i);
            }
            return corners;
        }

        /// <summary>Скользящее среднее по положениям.
        ///
        /// ОКНО НЕ ПЕРЕСЕКАЕТ УГОЛ: цепочка разрезается в каждой угловой точке, и каждый кусок
        /// сглаживается отдельно с закреплёнными концами. Формулировка «вершина остаётся на месте»
        /// СЛАБЕЕ и недостаточна — закрепив одну вершину, но позволив окну через неё проезжать,
        /// мы утянем её соседей к хорде: угол видимо скруглится, хотя сама вершина будет стоять
        /// точно там, где стояла.</summary>
        public static List<StrokePoint> Smooth(List<StrokePoint> points, float strength, int window,
                                               float cornerAngleDegrees)
        {
            var result = new List<StrokePoint>(points != null ? points.Count : 0);
            if (points == null) return result;
            result.AddRange(points);
            if (points.Count < 3 || strength <= 0f || window < 3) return result;

            var cuts = CornerIndices(points, cornerAngleDegrees);
            int half = window / 2;

            int start = 0;
            for (int c = 0; c <= cuts.Count; c++)
            {
                int end = c < cuts.Count ? cuts[c] : points.Count - 1;
                SmoothRange(points, result, start, end, half, strength);
                start = end;   // угловая точка — конец одного куска и начало следующего
            }
            return result;
        }

        /// <summary>Сглаживает точки строго между start и end, оба конца оставляя на месте.</summary>
        static void SmoothRange(List<StrokePoint> source, List<StrokePoint> target,
                                int start, int end, int half, float strength)
        {
            for (int i = start + 1; i < end; i++)
            {
                float sx = 0f, sy = 0f;
                int n = 0;
                for (int k = -half; k <= half; k++)
                {
                    int j = i + k;
                    if (j < start || j > end) continue;   // окно упирается в границы куска
                    sx += source[j].X; sy += source[j].Y;
                    n++;
                }
                if (n == 0) continue;
                var p = source[i];
                target[i] = new StrokePoint(Mathf.Lerp(p.X, sx / n, strength),
                                            Mathf.Lerp(p.Y, sy / n, strength),
                                            p.W);   // толщина не трогается, см. док класса
            }
        }

        /// <summary>Дуглас–Пекер. Нужен только чтобы в файле не лежало по точке на кадр; форму при
        /// малом допуске уже не меняет. Толщина берётся у оставшихся точек как есть.</summary>
        public static List<StrokePoint> Simplify(List<StrokePoint> points, float epsilon)
        {
            var result = new List<StrokePoint>();
            if (points == null) return result;
            if (points.Count < 3) { result.AddRange(points); return result; }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            Recurse(points, 0, points.Count - 1, epsilon, keep);

            for (int i = 0; i < points.Count; i++)
                if (keep[i]) result.Add(points[i]);
            return result;
        }

        static void Recurse(List<StrokePoint> pts, int first, int last, float epsilon, bool[] keep)
        {
            if (last <= first + 1) return;
            float ax = pts[first].X, ay = pts[first].Y;
            float bx = pts[last].X, by = pts[last].Y;
            float dx = bx - ax, dy = by - ay;
            float len = Mathf.Sqrt(dx * dx + dy * dy);

            float worst = -1f;
            int worstAt = -1;
            for (int i = first + 1; i < last; i++)
            {
                float d = len < 0.00001f
                    ? Mathf.Sqrt((pts[i].X - ax) * (pts[i].X - ax) + (pts[i].Y - ay) * (pts[i].Y - ay))
                    : Mathf.Abs(dy * pts[i].X - dx * pts[i].Y + bx * ay - by * ax) / len;
                if (d > worst) { worst = d; worstAt = i; }
            }
            if (worst <= epsilon || worstAt < 0) return;

            keep[worstAt] = true;
            Recurse(pts, first, worstAt, epsilon, keep);
            Recurse(pts, worstAt, last, epsilon, keep);
        }
    }
}
