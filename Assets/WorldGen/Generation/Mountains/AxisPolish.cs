using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §7 «Доводка оси». Скелет, снятый с растра, для нарезки на горы ещё не годится: он идёт
    /// ступеньками, дрожит на ячейку туда-сюда и обрывается, не дойдя до торца мазка. Четыре
    /// операции приводят его в порядок, и порядок их применения существен.
    ///
    /// Все пороги — В ЯЧЕЙКАХ сетки, не в мировых единицах и не в пикселях. Сетка привязана к
    /// масштабу гор (MountainMask.CellsPerR), поэтому пороги в ячейках означают «доля радиуса горы»
    /// и остаются верными при любом размере кисти.
    /// </summary>
    public static class AxisPolish
    {
        /// <summary>Шаг перебора при подтяжке к гребню.</summary>
        public const float RidgeStep = 0.2f;

        /// <summary>Сколько раз повторяется подтяжка. Одного прохода мало: точка тянется к гребню, а
        /// нормаль у неё вычислена по старым соседям.</summary>
        const int RidgePasses = 2;

        /// <summary>
        /// Подтяжка к гребню: каждая точка сдвигается вдоль НОРМАЛИ туда, где поле расстояний больше.
        /// Утоньшение оставляет ось где придётся в пределах ячейки, а гора должна стоять на самом
        /// толстом месте массы, иначе её подошва вылезет за край с одной стороны.
        ///
        /// Поле сюда подаётся СГЛАЖЕННОЕ: на сыром поле максимум прыгает от ячейки к ячейке, и ось,
        /// гоняясь за ним, обрастает зубьями.
        /// </summary>
        public static List<Vector2> SnapToRidge(List<Vector2> pts, float[] blurred, int w, int h, float maxOffset)
        {
            var cur = pts;
            for (int pass = 0; pass < RidgePasses; pass++)
            {
                var next = new List<Vector2>(cur.Count);
                for (int i = 0; i < cur.Count; i++)
                {
                    Vector2 p = cur[i];
                    Vector2 a = cur[Math.Max(0, i - 1)];
                    Vector2 b = cur[Math.Min(cur.Count - 1, i + 1)];
                    Vector2 t = b - a;
                    float len = t.Length();
                    if (len < 1e-6f) { next.Add(p); continue; }

                    var normal = new Vector2(-t.Y / len, t.X / len);
                    Vector2 best = p;
                    float bestValue = SampleInside(blurred, w, h, p.X, p.Y);
                    for (float off = -maxOffset; off <= maxOffset; off += RidgeStep)
                    {
                        Vector2 q = p + normal * off;
                        float v = SampleInside(blurred, w, h, q.X, q.Y);
                        if (v > bestValue) { bestValue = v; best = q; }
                    }
                    next.Add(best);
                }
                cur = next;
            }
            return cur;
        }

        /// <summary>Прореживание: точки ближе minStep друг к другу выбрасываются. На растровой оси их
        /// по одной на ячейку, и сглаживать такую густоту бессмысленно дорого.</summary>
        public static List<Vector2> Resample(List<Vector2> pts, float minStep)
        {
            var result = new List<Vector2>();
            if (pts == null || pts.Count == 0) return result;

            result.Add(pts[0]);
            for (int i = 1; i < pts.Count; i++)
                if (Vector2.Distance(result[result.Count - 1], pts[i]) >= minStep) result.Add(pts[i]);

            // Последняя точка возвращается на место: конец оси задаёт торец горы, и терять его нельзя.
            if (result.Count > 1) result[result.Count - 1] = pts[pts.Count - 1];
            return result;
        }

        /// <summary>Сглаживание маской 1–2–1. У незамкнутой оси концы не двигаются: их только что
        /// поставили осмысленно.</summary>
        public static List<Vector2> Smooth(List<Vector2> pts, bool closed, int passes)
        {
            if (pts.Count < 3) return pts;
            var cur = pts;
            for (int pass = 0; pass < passes; pass++)
            {
                int n = cur.Count;
                var next = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1)) { next.Add(cur[i]); continue; }
                    Vector2 a = cur[(i - 1 + n) % n];
                    Vector2 b = cur[(i + 1) % n];
                    next.Add((a + cur[i] * 2f + b) * 0.25f);
                }
                cur = next;
            }
            return cur;
        }

        /// <summary>
        /// Продление концов. Медиальная ось обрывается за полуширину до кончика мазка — так устроено
        /// утоньшение, у него на кончике попросту нет материала. Если оставить как есть, торцы и
        /// закругления массы останутся незаполненными: гора не дойдёт до края собственного мазка.
        /// Поэтому конец продолжается по своему направлению, пока под ним есть масса.
        ///
        /// Продлеваются только СВОБОДНЫЕ концы. Конец, упирающийся в развилку, продлевать нельзя: он
        /// полезет вдоль соседней ветки и посадит там лишнюю гору поперёк.
        /// </summary>
        public static List<Vector2> ExtendEnds(List<Vector2> pts, float[] field, int w, int h,
                                               bool head, bool tail, float step, float stopDistance, int maxSteps)
        {
            int n = pts.Count;
            var result = new List<Vector2>();

            if (head)
            {
                var grown = Grow(pts[Math.Min(3, n - 1)], pts[0], field, w, h, step, stopDistance, maxSteps);
                grown.Reverse();
                result.AddRange(grown);
            }
            result.AddRange(pts);
            if (tail)
                result.AddRange(Grow(pts[Math.Max(0, n - 4)], pts[n - 1], field, w, h, step, stopDistance, maxSteps));

            return result;
        }

        /// <summary>Шагает от b в сторону «от a к b», пока под ногами масса.</summary>
        static List<Vector2> Grow(Vector2 a, Vector2 b, float[] field, int w, int h,
                                  float step, float stopDistance, int maxSteps)
        {
            var result = new List<Vector2>();
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 1e-6f) return result;

            Vector2 dir = d / len;
            Vector2 p = b;
            for (int k = 0; k < maxSteps; k++)
            {
                p += dir * step;
                if (SampleInside(field, w, h, p.X, p.Y) < stopDistance) break;
                result.Add(p);
            }
            return result;
        }

        /// <summary>Выборка поля с ЯВНЫМ провалом за пределами сетки: за краем возвращается −1, а не
        /// прижатое к границе значение. Иначе и подтяжка, и продление уползали бы вдоль рамки.</summary>
        static float SampleInside(float[] field, int w, int h, float x, float y)
        {
            if (x < 0f || y < 0f || x > w - 2 || y > h - 2) return -1f;
            return DistanceField.Sample(field, w, h, x, y);
        }
    }
}
