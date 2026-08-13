using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §8 «Нарезка на звенья»: готовая ось режется на куски примерно одинаковой длины, и над каждым
    /// куском потом вырастет одна гора. Порт `splitPolyline` из `docs/mountain-brush-step1.html`.
    ///
    /// Три вещи здесь неочевидны и все три существенны.
    ///
    /// 1. ЧИСЛО ЗВЕНЬЕВ подбирается по отношению, а не по разности: n = argmin |ln((S/n)/T)|. Разность
    ///    штрафует длинные звенья слабее коротких (перелёт на 30 единиц при цели 70 — это ×1.4, а
    ///    недолёт на те же 30 — это ×0.57), и ось систематически резалась бы на слишком длинные куски.
    ///
    /// 2. МЕТРИКА анизотропна: вертикаль дороже горизонтали в a раз. Смотрим на землю под углом, и
    ///    вертикальный участок оси на экране короче, чем на самом деле; без поправки на вертикальных
    ///    участках хребта горы вышли бы редкими и растянутыми.
    ///
    /// 3. У ЗАМКНУТОЙ оси шов ставится в случайное место. Иначе на всех кольцах массива стык звеньев
    ///    приходился бы на одну и ту же сторону, и глаз сразу видит правильность.
    ///
    /// Отличие от прототипа одно, и оно намеренное. Прототип выбрасывает всю ось, если она короче
    /// 0.15·T («крошка»), — а короткая ветка между двумя развилками (§14) как раз такая, и на её месте
    /// в массиве остаётся ДЫРА. Здесь ось по длине не выбрасывается никогда: слишком короткая просто
    /// становится ОДНИМ звеном, а строить ли над ним гору, решает уже геометрия — доля с нулевым
    /// силуэтом (LinkOutline) и подошва уже минимума (MoundBuilder.minSpan) горы не дают. Решение
    /// принимается там, где видна форма, а не там, где известна одна длина.
    /// </summary>
    public static class LinkSplitter
    {
        /// <summary>Разброс высоты (§8): 0.82…1.18 от расчётной.</summary>
        public const float HeightJitterLow = 0.82f;
        public const float HeightJitterSpan = 0.36f;

        /// <summary>Замкнутая ось длиной хотя бы в 0.9·T режется минимум надвое: одно звено,
        /// замкнутое само на себя, — это не гора, а бублик.</summary>
        public const float ClosedTwoLinks = 0.9f;

        /// <summary>
        /// Режет ось на звенья. Точки, ширины и глубины — в МИРОВЫХ единицах и одной длины.
        /// tip0/tip1 — свободные концы оси (см. AxisLink.FreeStart). target — T, jitter — разброс
        /// длин, aniso — a. Возвращает пустой список, если резать нечего.
        /// </summary>
        public static List<AxisLink> Split(IReadOnlyList<Vector2> points, IReadOnlyList<float> widths,
                                           IReadOnlyList<float> depths, bool closed, bool tip0, bool tip1,
                                           float target, float jitter, float aniso, Mulberry32 rng)
        {
            var links = new List<AxisLink>();
            if (points == null || widths == null || depths == null || rng == null) return links;
            if (points.Count < 2 || widths.Count != points.Count || depths.Count != points.Count) return links;
            if (target <= 0f) return links;

            List<Vector2> p; List<float> wd, dp;
            if (closed)
            {
                // Сдвиг шва: ось прокручивается на случайное число точек, а потом замыкается сама на
                // себя добавленной первой точкой.
                int k = (int)(rng.Next() * points.Count);
                if (k < 0) k = 0;
                if (k >= points.Count) k = points.Count - 1;
                p = Rotate(points, k); wd = Rotate(widths, k); dp = Rotate(depths, k);
                p.Add(p[0]); wd.Add(wd[0]); dp.Add(dp[0]);
            }
            else
            {
                p = new List<Vector2>(points); wd = new List<float>(widths); dp = new List<float>(depths);
            }

            var cum = new float[p.Count];
            for (int i = 1; i < p.Count; i++)
                cum[i] = cum[i - 1] + MetricLength(p[i] - p[i - 1], aniso);
            float total = cum[cum.Length - 1];
            if (total <= 0f) return links;

            int count = Math.Max(1, (int)Math.Floor(total / target));
            if (Deviation(total, count + 1, target) < Deviation(total, count, target)) count++;
            if (closed && total > target * ClosedTwoLinks) count = Math.Max(2, count);

            // Веса длин: сначала все, потом уже разброс высот — порядок обращений к генератору
            // важен, иначе тот же мазок посчитается иначе, чем в прототипе.
            var weight = new float[count];
            float weightSum = 0f;
            for (int i = 0; i < count; i++)
            {
                weight[i] = 1f + (rng.Next() * 2f - 1f) * jitter;
                if (weight[i] < 0.05f) weight[i] = 0.05f;
                weightSum += weight[i];
            }

            var marks = new float[count + 1];
            float acc = 0f;
            for (int i = 0; i < count; i++)
            {
                acc += total * weight[i] / weightSum;
                marks[i + 1] = Math.Min(acc, total);
            }
            marks[count] = total;

            for (int i = 0; i < count; i++)
            {
                Sample(p, wd, dp, cum, marks[i], out var a);
                Sample(p, wd, dp, cum, marks[i + 1], out var b);
                Sample(p, wd, dp, cum, (marks[i] + marks[i + 1]) * 0.5f, out var mid);

                var link = new AxisLink { Mid = mid.P, Tan = mid.T, MidW = mid.W, MidDepth = mid.D };
                link.Pts.Add(a.P); link.Ws.Add(a.W);
                for (int j = 0; j < p.Count; j++)
                    if (cum[j] > marks[i] + 1e-4f && cum[j] < marks[i + 1] - 1e-4f)
                    {
                        link.Pts.Add(p[j]); link.Ws.Add(wd[j]);
                    }
                link.Pts.Add(b.P); link.Ws.Add(b.W);

                link.Length = marks[i + 1] - marks[i];
                link.HeightJitter = HeightJitterLow + rng.Next() * HeightJitterSpan;
                // У замкнутой оси соседи есть у каждого звена: последнее упирается в первое.
                link.FreeStart = !closed && i == 0 && tip0;
                link.FreeEnd = !closed && i == count - 1 && tip1;
                links.Add(link);
            }
            return links;
        }

        /// <summary>
        /// Длина, в которой вертикаль ДЕШЕВЛЕ горизонтали в a раз, то есть на гряде, уходящей вверх
        /// по картинке, горы ставятся РЕЖЕ.
        ///
        /// §8 требует обратного: там вертикаль дороже, «гряды видны с более острого угла и кажутся
        /// сжатыми», и позвонки ложатся чаще. Правило перевёрнуто 2026-08-14 по решению ДМ, потому
        /// что замером показано: сжатие тут работает не на перспективу, а против неё. Гора не
        /// меняется от наклона звена (замер: ширина 22, высота 22, юбка 7 при любом угле) — меняется
        /// только то, КАК соседи закрывают друг друга. На вертикали шаг выходил 10 при высоте горы
        /// 22, ближняя съедала дальнюю целиком, и от неё оставался серпик: гряда читалась рыбьей
        /// чешуёй, а не горами. Поперёк экрана горы стоят бок о бок и не мешают друг другу, поэтому
        /// там шаг остаётся целевым.
        /// </summary>
        public static float MetricLength(Vector2 d, float aniso)
        {
            float a = Math.Max(1f, aniso);
            return (float)Math.Sqrt(d.X * d.X + d.Y / a * (d.Y / a));
        }

        /// <summary>Насколько шаг при n звеньях не похож на целевой — в разах, а не в единицах.</summary>
        static float Deviation(float total, int n, float target)
            => n <= 0 ? float.PositiveInfinity : (float)Math.Abs(Math.Log(total / n / target));

        static List<T> Rotate<T>(IReadOnlyList<T> src, int k)
        {
            var result = new List<T>(src.Count);
            for (int i = 0; i < src.Count; i++) result.Add(src[(i + k) % src.Count]);
            return result;
        }

        struct Spot
        {
            public Vector2 P;
            public Vector2 T;
            public float W;
            public float D;
        }

        /// <summary>Точка, касательная, полуширина и глубина на расстоянии s вдоль оси.</summary>
        static void Sample(List<Vector2> p, List<float> wd, List<float> dp, float[] cum, float s,
                           out Spot spot)
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
            Vector2 d = p[lo + 1] - p[lo];
            float len = d.Length();

            spot = new Spot
            {
                P = p[lo] + d * t,
                T = len > 1e-6f ? d / len : new Vector2(1f, 0f),
                W = wd[lo] + (wd[lo + 1] - wd[lo]) * t,
                D = dp[lo] + (dp[lo + 1] - dp[lo]) * t,
            };
        }
    }
}
