using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §9 «Гармошка»: звено раздувается в долю — силуэт, широкий в середине и перетянутый на
    /// позвонках. Полуширина берётся из поля расстояний (она уже лежит в AxisLink.Ws с наложенным
    /// потолком), а вдоль звена умножается на профиль
    ///
    ///     h(u) = w(u) · ( t + (1 − t)·sin^0.6(π·u) )
    ///
    /// где t — «талия», доля полной полуширины на позвонке. На стыке соседние доли имеют одинаковую
    /// ширину t·w, поэтому силуэт непрерывен и лишь перетянут — это и даёт гармошку. Показатель 0.6
    /// вместо 1 делает переход от талии к вершине быстрее, а саму вершину площе.
    ///
    /// Порт `linkOutline` из `docs/mountain-brush-step1.html`. Отличие одно: частота выборки задаётся
    /// параметром, а не константой «каждые 3 px» — на карте нет пикселей, и шаг обязан ехать вместе
    /// с масштабом гор, иначе крупные горы выйдут гранёными, а мелкие — вычисленными впустую.
    /// </summary>
    public static class LinkOutline
    {
        /// <summary>Минимум точек на долю: у совсем короткого звена иначе не получится силуэта.</summary>
        const int MinSamples = 8;

        /// <summary>Замкнутый силуэт доли: левая сторона от начала к концу, затем правая обратно.
        /// Возвращает null, если звено выродилось в точку.</summary>
        public static List<Vector2> Build(AxisLink link, float waist, float sampleStep)
        {
            if (link == null || link.Pts.Count < 2 || link.Ws.Count != link.Pts.Count) return null;
            if (sampleStep <= 0f) return null;

            var pts = link.Pts;
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++)
                cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length();
            float total = cum[cum.Length - 1];
            if (total < sampleStep * 0.25f) return null;

            int n = Math.Max(MinSamples, (int)Math.Ceiling(total / sampleStep));
            var left = new List<Vector2>(n + 1);
            var right = new List<Vector2>(n + 1);

            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n;
                Sample(pts, link.Ws, cum, total * u, out Vector2 p, out Vector2 normal, out float w);
                float h = w * (waist + (1f - waist) * (float)Math.Pow(Math.Sin(Math.PI * u), 0.6));
                left.Add(p + normal * h);
                right.Add(p - normal * h);
            }

            var outline = new List<Vector2>(left.Count + right.Count);
            outline.AddRange(left);
            for (int i = right.Count - 1; i >= 0; i--) outline.Add(right[i]);
            return outline;
        }

        /// <summary>Точка, левая нормаль и полуширина на расстоянии s вдоль звена.</summary>
        static void Sample(List<Vector2> pts, List<float> ws, float[] cum, float s,
                           out Vector2 point, out Vector2 normal, out float width)
        {
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }
            if (lo > cum.Length - 2) lo = cum.Length - 2;

            float seg = cum[lo + 1] - cum[lo];
            float t = seg > 0f ? (s - cum[lo]) / seg : 0f;

            Vector2 a = pts[lo], b = pts[lo + 1];
            Vector2 d = b - a;
            float len = d.Length();
            if (len <= 0f) { d = new Vector2(1f, 0f); len = 1f; }

            point = a + (b - a) * t;
            normal = new Vector2(-d.Y / len, d.X / len);
            width = ws[lo] + (ws[lo + 1] - ws[lo]) * t;
        }
    }
}
