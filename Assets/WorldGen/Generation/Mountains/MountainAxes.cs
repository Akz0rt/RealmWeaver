using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Готовая ось: линия, вдоль которой встанет цепочка гор. Точки — в координатах СЕТКИ; мировые
    /// получаются через MountainMask.GridToWorld, ширины и глубины — умножением на MountainMask.Cell.
    /// Единицы держатся в ячейках до последнего, чтобы масштаб пересчитывался ровно в одном месте.
    /// </summary>
    public sealed class MountainAxis
    {
        public List<Vector2> Points = new List<Vector2>();

        /// <summary>Полуширина массы под каждой точкой, В ЯЧЕЙКАХ, с потолком: гора не может быть
        /// шире положенного, как бы толста ни была масса под ней.</summary>
        public float[] Widths;

        /// <summary>Та же величина БЕЗ потолка — мера того, насколько глубоко точка сидит внутри
        /// массы. По ней §11 раздаёт ярусы.</summary>
        public float[] Depths;

        public bool Closed;

        /// <summary>Кольцо (§4) или скелет (§6). Различие видно и в ширине: скелетной оси позволено
        /// раздуться до 1.6 радиуса, кольцу — ровно до радиуса.</summary>
        public bool FromRing;

        /// <summary>Уровень, на котором стоит кольцо. У скелетной оси — 0.</summary>
        public float Level;
    }

    /// <summary>
    /// Сводит §4–§7 воедино: кольца, скелет там, куда кольца не дотянулись, и доводка того и другого.
    ///
    /// Порядок именно такой, и он не случаен. Сначала отбираются кольца — они дают «правильные» оси,
    /// идущие вдоль края массы. Потом считается, что кольца уже покрыли: круг радиуса R, едущий по
    /// кольцу, накрывает полосу шириной 2R, поэтому покрытым считается всё, что ближе R (с небольшим
    /// запасом) к какому-нибудь принятому кольцу. И только НЕПОКРЫТЫЕ куски скелета становятся
    /// осями. Так вложенность колец и скелет в узких местах складываются в одно покрытие без дыр и
    /// без наложений, а вырезать покрытое из маски и строить всё заново не приходится.
    /// </summary>
    public static class AxisBuilder
    {
        /// <summary>Запас к радиусу при проверке покрытия, в ячейках. Без него на стыке кольца и
        /// скелета остаётся щель в одну-две ячейки, и в неё влезает лишняя маленькая гора.</summary>
        public const float CoverageMargin = 2f;

        /// <summary>Минимальный шаг между точками оси после прореживания.</summary>
        public const float ResampleStep = 1.25f;

        /// <summary>Насколько далеко ищется гребень при подтяжке.</summary>
        public const float RidgeSearch = 2.5f;

        public const int RingSmoothPasses = 2;

        /// <summary>Скелетную ось сглаживаем сильнее кольца: она снята с растра и идёт ступеньками,
        /// тогда как кольцо получено интерполяцией и уже гладкое.</summary>
        public const int SkeletonSmoothPasses = 4;

        /// <summary>Потолок полуширины скелетной оси, в радиусах. Полтора с небольшим — предел, до
        /// которого гора расширяется, не перестав быть горой; выше уже нужно второе кольцо.</summary>
        public const float SkeletonWidthCap = 1.6f;

        /// <summary>Ветка короче этой доли радиуса, висящая свободным концом, — шип от неровности
        /// края маски, а не отросток массива.</summary>
        public const float PruneFactor = 0.9f;

        /// <summary>Сглаживание поля перед подтяжкой к гребню.</summary>
        public const int BlurPasses = 3;

        public const float ExtendStep = 1.5f;
        public const float ExtendStop = 0.75f;
        public const int ExtendMaxSteps = 60;

        /// <summary>Зазор, при котором доведённая скелетная ось признаётся замкнутой.</summary>
        public const float ClosedGap = 2f;

        /// <summary>
        /// Строит оси пятна. field — поле расстояний этой же маски (DistanceField.Build).
        /// radiusCells — радиус горы в ячейках.
        /// </summary>
        public static List<MountainAxis> Build(MountainMask mask, float[] field, float radiusCells)
            => Build(mask, field, radiusCells, out _);

        /// <summary>Тот же расчёт, но отдаёт наружу и полный скелет — он нужен отладочному показу
        /// «где ось прошла бы, не будь колец».</summary>
        public static List<MountainAxis> Build(MountainMask mask, float[] field, float radiusCells,
                                               out List<AxisPath> fullSkeleton)
        {
            var axes = new List<MountainAxis>();
            fullSkeleton = new List<AxisPath>();
            if (mask == null || field == null || radiusCells <= 0f) return axes;

            int w = mask.W, h = mask.H;
            var rings = RingSelection.Select(field, w, h, radiusCells);

            foreach (var ring in rings)
            {
                var pts = AxisPolish.Resample(ring.Contour.Points, ResampleStep);
                if (pts.Count < 4) continue;
                pts = AxisPolish.Smooth(pts, ring.Contour.Closed, RingSmoothPasses);
                axes.Add(Finish(pts, field, w, h, ring.Contour.Closed, true, ring.Level, radiusCells));
            }

            var blurred = DistanceField.Blur(field, w, h, BlurPasses);
            var thinned = Skeleton.Thin(mask);
            int pruneLen = (int)Math.Max(5f, PruneFactor * radiusCells);
            fullSkeleton = AxisStitching.Stitch(Skeleton.Branches(thinned, w, h, pruneLen));

            float[] toRings = RingCoverage(rings, w, h);
            float coverLimit = radiusCells + CoverageMargin;

            foreach (var run in UncoveredRuns(fullSkeleton, toRings, w, h, coverLimit))
            {
                var snapped = AxisPolish.SnapToRidge(run.Pts, blurred, w, h, RidgeSearch);
                var pts = AxisPolish.Resample(snapped, ResampleStep);
                if (pts.Count < 3) continue;

                bool closed = pts.Count > 8 && Vector2.Distance(pts[0], pts[pts.Count - 1]) < ClosedGap;
                pts = AxisPolish.Smooth(pts, closed, SkeletonSmoothPasses);
                if (!closed)
                    pts = AxisPolish.ExtendEnds(pts, field, w, h, run.Tip0, run.Tip1,
                                                ExtendStep, ExtendStop, ExtendMaxSteps);

                axes.Add(Finish(pts, field, w, h, closed, false, 0f, radiusCells * SkeletonWidthCap));
            }
            return axes;
        }

        /// <summary>Меряет ширину и глубину под готовой линией и складывает ось.</summary>
        static MountainAxis Finish(List<Vector2> pts, float[] field, int w, int h,
                                   bool closed, bool fromRing, float level, float widthCap)
        {
            var axis = new MountainAxis
            {
                Points = pts,
                Closed = closed,
                FromRing = fromRing,
                Level = level,
                Widths = new float[pts.Count],
                Depths = new float[pts.Count],
            };
            for (int i = 0; i < pts.Count; i++)
            {
                float d = DistanceField.Sample(field, w, h, pts[i].X, pts[i].Y);
                axis.Depths[i] = d;
                axis.Widths[i] = Math.Min(d, widthCap);
            }
            return axis;
        }

        /// <summary>
        /// Расстояние от каждой ячейки до ближайшего принятого кольца. Кольца рисуются в растр, растр
        /// обращается, и по нему считается то же самое точное евклидово расстояние — то есть покрытие
        /// меряется тем же инструментом, что и толщина массы.
        /// Возвращает null, если колец нет вовсе: тогда покрытым не считается ничего.
        /// </summary>
        static float[] RingCoverage(List<RingAxis> rings, int w, int h)
        {
            if (rings.Count == 0) return null;

            var stamped = new byte[w * h];
            foreach (var ring in rings)
            {
                var pts = ring.Contour.Points;
                for (int i = 1; i < pts.Count; i++)
                {
                    Vector2 a = pts[i - 1], b = pts[i];
                    // Шаг вдвое мельче ячейки: при шаге в ячейку наклонный отрезок оставляет в растре
                    // дырки, и сквозь них просачивается «непокрытый» кусок скелета.
                    int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(a, b) * 2f));
                    for (int t = 0; t <= steps; t++)
                    {
                        Vector2 p = a + (b - a) * ((float)t / steps);
                        int x = (int)Math.Round(p.X), y = (int)Math.Round(p.Y);
                        if (x >= 0 && y >= 0 && x < w && y < h) stamped[y * w + x] = 1;
                    }
                }
            }

            var inverted = new byte[w * h];
            for (int i = 0; i < inverted.Length; i++) inverted[i] = stamped[i] != 0 ? (byte)0 : (byte)1;
            return DistanceField.Build(inverted, w, h);
        }

        /// <summary>
        /// Режет скелет на куски, не покрытые кольцами. Каждый кусок захватывает по одной точке
        /// внутрь покрытой зоны с каждой стороны — чтобы цепочка гор на скелете стыковалась с
        /// цепочкой на кольце, а не начиналась в двух ячейках от неё.
        /// </summary>
        static List<AxisPath> UncoveredRuns(List<AxisPath> skeleton, float[] toRings, int w, int h, float limit)
        {
            var runs = new List<AxisPath>();
            foreach (var path in skeleton)
            {
                var pts = path.Pts;
                int i = 0;
                while (i < pts.Count)
                {
                    while (i < pts.Count && Covered(pts[i])) i++;
                    if (i >= pts.Count) break;

                    int j = i;
                    while (j < pts.Count && !Covered(pts[j])) j++;

                    int from = Math.Max(0, i - 1);
                    int to = Math.Min(pts.Count, j + 1);
                    if (to - from >= 3)
                    {
                        var run = new AxisPath
                        {
                            Tip0 = from == 0 && path.Tip0,
                            Tip1 = to == pts.Count && path.Tip1,
                        };
                        for (int k = from; k < to; k++) run.Pts.Add(pts[k]);
                        runs.Add(run);
                    }
                    i = j;
                }
            }
            return runs;

            bool Covered(Vector2 p)
            {
                if (toRings == null) return false;
                int x = (int)p.X, y = (int)p.Y;
                if (x < 0 || y < 0 || x >= w || y >= h) return false;
                return toRings[y * w + x] <= limit;
            }
        }
    }
}
