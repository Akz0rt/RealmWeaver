using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Переводит нарисованные реки в пиксели маски «суша/вода» — той самой, из которой карта берёт
    /// ВСЁ, что делает воду водой: цвет по глубине, светлый ореол у берега, тёмную обводку по кромке
    /// и песчаную полосу на суше (см. MapTerrain.shader и MapRasterizer.ColorForWaterPixel).
    ///
    /// Отсюда и весь смысл: река не рисуется ПОВЕРХ карты отдельной лентой, а становится водой
    /// внутри карты — «такой же водоём, как другие, только сильно тоньше», как и просил ДМ. Даром
    /// достаётся и то, за что раньше боролись руками: две реки крест-накрест сливаются (объединение
    /// масок шва не имеет), а река, впадающая в море, просто перестаёт от него отличаться — между
    /// ними нет границы, потому что оба одинаково вода.
    ///
    /// Без UnityEngine — гоняется самотестами без сцены.
    /// </summary>
    public static class RiverMask
    {
        /// <summary>Отмечает в маске пиксели, накрытые всеми реками. Маска НЕ очищается: вызывающий
        /// решает, начинать ли с чистого листа.</summary>
        public static void StampAll(bool[] mask, int w, int h, float mapWidth, float mapHeight,
                                    IEnumerable<PaintedRiver> rivers,
                                    int rectX = 0, int rectY = 0, int rectW = int.MaxValue, int rectH = int.MaxValue)
        {
            if (rivers == null) return;
            foreach (var river in rivers)
            {
                if (river == null || river.Points == null || river.Points.Count < 2) continue;
                Stamp(mask, w, h, mapWidth, mapHeight,
                      RiverPaintOps.BuildCurve(river.Points, river.Width), river.Width,
                      rectX, rectY, rectW, rectH);
            }
        }

        /// <summary>Отмечает в маске пиксели, накрытые одним руслом: осевая кривая, раздутая на
        /// половину ширины. Идём ПО ОТРЕЗКАМ, а не по общей рамке всей реки: у извилистой реки рамка
        /// занимает пол-карты, а сумма рамок отрезков — только само русло с каёмкой.</summary>
        public static void Stamp(bool[] mask, int w, int h, float mapWidth, float mapHeight,
                                 IReadOnlyList<Vector2> curve, float width,
                                 int rectX = 0, int rectY = 0, int rectW = int.MaxValue, int rectH = int.MaxValue)
        {
            if (mask == null || curve == null || curve.Count < 2) return;
            if (w <= 0 || h <= 0 || mapWidth <= 1e-5f || mapHeight <= 1e-5f || width <= 0f) return;

            float half = width * 0.5f;
            float halfSq = half * half;

            int clipX0 = Math.Max(0, rectX);
            int clipY0 = Math.Max(0, rectY);
            int clipX1 = rectW == int.MaxValue ? w - 1 : Math.Min(w - 1, rectX + rectW - 1);
            int clipY1 = rectH == int.MaxValue ? h - 1 : Math.Min(h - 1, rectY + rectH - 1);

            for (int s = 0; s < curve.Count - 1; s++)
            {
                Vector2 a = curve[s], b = curve[s + 1];

                int x0 = Math.Max(clipX0, PixelFloorX(Math.Min(a.X, b.X) - half, w, mapWidth));
                int x1 = Math.Min(clipX1, PixelCeilX(Math.Max(a.X, b.X) + half, w, mapWidth));
                int y0 = Math.Max(clipY0, PixelFloorX(Math.Min(a.Y, b.Y) - half, h, mapHeight));
                int y1 = Math.Min(clipY1, PixelCeilX(Math.Max(a.Y, b.Y) + half, h, mapHeight));

                for (int y = y0; y <= y1; y++)
                {
                    float py = (y + 0.5f) / h * mapHeight;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = (x + 0.5f) / w * mapWidth;
                        if (DistanceToSegmentSq(a, b, new Vector2(px, py)) <= halfSq)
                            mask[y * w + x] = true;
                    }
                }
            }
        }

        /// <summary>Мировая координата → индекс пикселя. Обратно к PixelToSite (центр пикселя лежит
        /// в (i + 0.5) / size), поэтому и вычитание половины.</summary>
        static int PixelFloorX(float world, int size, float mapSize)
            => (int)Math.Floor(world / mapSize * size - 0.5f);

        static int PixelCeilX(float world, int size, float mapSize)
            => (int)Math.Ceiling(world / mapSize * size - 0.5f);

        static float DistanceToSegmentSq(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-8f) return (p - a).LengthSquared();
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            return (p - (a + ab * t)).LengthSquared();
        }
    }
}
