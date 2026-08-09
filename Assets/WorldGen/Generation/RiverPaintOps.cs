using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Чистая геометрия кисти рек (без UnityEngine — гоняется самотестами без сцены).
    /// Путь от мазка до русла:
    ///   • AppendAnchor  — цепочка клеток под курсором без повторов подряд;
    ///   • TrimToShore   — концы, ушедшие в воду, обрезаются по кромке водоёма (плюс небольшой
    ///                     заход за неё) и заодно сообщается, во что упёрся КАЖДЫЙ конец;
    ///   • BuildCurve    — прореживание, сглаживание углов и сплайн: из ломаной по центрам клеток
    ///                     получается плавная река.
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
        /// Обрезает концы русла по кромке воды и определяет, во что каждый конец упирается.
        ///
        /// Кромка берётся не на глаз: у соседних клеток Вороного общее ребро — это серединный
        /// перпендикуляр между их центрами, так что СЕРЕДИНА отрезка «центр суши → центр воды»
        /// лежит точно на границе клеток, то есть на береговой линии.
        ///
        /// За кромку русло заходит на overshoot: растр рисует СГЛАЖЕННЫЙ берег, и точная граница
        /// клеток с ним не совпадает пиксель в пиксель — без захода между рекой и водой оставался
        /// бы зазор. Заход не мозолит глаза, потому что на этом отрезке река гасится по
        /// прозрачности (см. RiverMeshBuilder), а не рисуется поверх воды сплошной полосой.
        ///
        /// Клетки воды ВНУТРИ мазка (река прошла через озерцо насквозь) сохраняются как есть —
        /// подрезаются только концы. Мазок целиком по воде рекой не становится: возвращается
        /// пустой список, и вызывающий его выбрасывает.
        /// </summary>
        public static List<Vector2> TrimToShore(IReadOnlyList<Vector2> sites, IReadOnlyList<RiverMouth> waterKind,
                                                float overshoot, out RiverMouth startMouth, out RiverMouth endMouth)
        {
            startMouth = RiverMouth.None;
            endMouth = RiverMouth.None;

            var result = new List<Vector2>();
            if (sites == null || waterKind == null || sites.Count != waterKind.Count) return result;

            int first = -1, last = -1;
            for (int i = 0; i < sites.Count; i++)
            {
                if (waterKind[i] != RiverMouth.None) continue;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0) return result;   // ни одной клетки суши — реки нет

            if (first > 0)
            {
                startMouth = waterKind[first - 1];
                result.Add(ShorePoint(sites[first], sites[first - 1], overshoot));
            }
            for (int i = first; i <= last; i++) result.Add(sites[i]);
            if (last < sites.Count - 1)
            {
                endMouth = waterKind[last + 1];
                result.Add(ShorePoint(sites[last], sites[last + 1], overshoot));
            }

            if (result.Count < 2)   // одна точка — это не река
            {
                result.Clear();
                startMouth = endMouth = RiverMouth.None;
            }
            return result;
        }

        /// <summary>Точка на берегу между центрами клеток суши и воды, отодвинутая в сторону воды
        /// на overshoot.</summary>
        static Vector2 ShorePoint(Vector2 landSite, Vector2 waterSite, float overshoot)
        {
            Vector2 boundary = (landSite + waterSite) * 0.5f;
            if (overshoot <= 0f) return boundary;
            Vector2 dir = boundary - landSite;
            if (dir.LengthSquared() < 1e-8f) return boundary;
            return boundary + Vector2.Normalize(dir) * overshoot;
        }

        /// <summary>Полный путь от опорных точек к рисуемой кривой: проредить → сгладить углы →
        /// сплайн. Одна дверь для рендера и для попадания курсором, чтобы река была там же, где её
        /// видно.</summary>
        public static List<Vector2> BuildCurve(IReadOnlyList<Vector2> anchors, float width, int subdivisions = 8)
        {
            // Прореживание с шагом в ширину русла: центры соседних клеток стоят гуще, чем река
            // широка, и каждый лишний узел — это лишний излом на глаз. Ширина как мерка удобна:
            // тонкий ручей вправе вилять чаще толстой реки.
            var thinned = Thin(anchors, Math.Max(width, 0.001f));
            // Два прохода усреднения срезают «пилу» от неровно стоящих центров клеток, оставляя
            // сам изгиб. Без них река выходит дёрганой (видно на карте как зигзаг вместо русла).
            var relaxed = Relax(thinned, passes: 2);
            return Smooth(relaxed, subdivisions);
        }

        /// <summary>Выбрасывает точки, стоящие ближе minSpacing к предыдущей оставленной. Концы
        /// сохраняются всегда: у них своё значение (устье, скругление).</summary>
        public static List<Vector2> Thin(IReadOnlyList<Vector2> points, float minSpacing)
        {
            var result = new List<Vector2>();
            if (points == null || points.Count == 0) return result;

            result.Add(points[0]);
            for (int i = 1; i < points.Count - 1; i++)
                if (Vector2.Distance(points[i], result[result.Count - 1]) >= minSpacing)
                    result.Add(points[i]);

            if (points.Count > 1) result.Add(points[points.Count - 1]);
            return result;
        }

        /// <summary>Усредняет каждую внутреннюю точку с соседями (1:2:1). Концы не двигаются —
        /// иначе устье уползло бы с берега, ради которого его и вычисляли.</summary>
        public static List<Vector2> Relax(IReadOnlyList<Vector2> points, int passes = 1)
        {
            var current = new List<Vector2>(points ?? new List<Vector2>());
            if (current.Count < 3) return current;

            for (int p = 0; p < passes; p++)
            {
                var next = new List<Vector2>(current.Count) { current[0] };
                for (int i = 1; i < current.Count - 1; i++)
                    next.Add(current[i - 1] * 0.25f + current[i] * 0.5f + current[i + 1] * 0.25f);
                next.Add(current[current.Count - 1]);
                current = next;
            }
            return current;
        }

        /// <summary>
        /// Сглаживает ломаную ЦЕНТРИПЕТАЛЬНЫМ сплайном Catmull-Rom (alpha = 0.5): кривая проходит
        /// через каждую опорную точку, а между ними изгибается. Центрипетальная параметризация, а
        /// не равномерная, потому что точки стоят на РАЗНЫХ расстояниях друг от друга (центры
        /// клеток Вороного), а равномерная на таких данных даёт петли и клювы на крутых поворотах.
        /// Крайние точки дублируются как «виртуальные соседи» — иначе первому и последнему сегменту
        /// не из чего считать касательную.
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

                float t0 = 0f;
                float t1 = t0 + KnotSpan(p0, p1);
                float t2 = t1 + KnotSpan(p1, p2);
                float t3 = t2 + KnotSpan(p2, p3);
                if (t2 - t1 < 1e-6f) continue;   // совпавшие точки — сегмента нет

                for (int s = 0; s < steps; s++)
                {
                    float t = t1 + (t2 - t1) * s / steps;
                    result.Add(Interpolate(p0, p1, p2, p3, t0, t1, t2, t3, t));
                }
            }
            result.Add(anchors[n - 1]);
            return result;
        }

        /// <summary>Шаг узла для центрипетальной параметризации: корень из расстояния (alpha=0.5).
        /// Нулевой шаг запрещён — на совпавших точках интерполяция делила бы на ноль.</summary>
        static float KnotSpan(Vector2 a, Vector2 b)
            => Math.Max((float)Math.Sqrt(Vector2.Distance(a, b)), 1e-4f);

        static Vector2 Interpolate(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                                   float t0, float t1, float t2, float t3, float t)
        {
            Vector2 a1 = Blend(p0, p1, t0, t1, t);
            Vector2 a2 = Blend(p1, p2, t1, t2, t);
            Vector2 a3 = Blend(p2, p3, t2, t3, t);
            Vector2 b1 = Blend(a1, a2, t0, t2, t);
            Vector2 b2 = Blend(a2, a3, t1, t3, t);
            return Blend(b1, b2, t1, t2, t);
        }

        static Vector2 Blend(Vector2 a, Vector2 b, float ta, float tb, float t)
        {
            float span = tb - ta;
            if (Math.Abs(span) < 1e-6f) return a;
            float k = (t - ta) / span;
            return a * (1f - k) + b * k;
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
