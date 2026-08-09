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
        /// <summary>Река, переведённая в кривую: в таком виде её и штампуют, и меряют расстояние до
        /// неё, когда решают, впадает ли в неё чужой кончик.</summary>
        public struct Ribbon
        {
            /// <summary>Id исходной реки — по нему предпросмотр отличает свои куски от готовых рек
            /// (у нарисованных рек Id всегда положителен).</summary>
            public int Id;
            public List<Vector2> Curve;
            public float Width;
            public bool WideStart, WideEnd;
        }

        /// <summary>Отмечает в маске пиксели, накрытые всеми реками. Маска НЕ очищается: вызывающий
        /// решает, начинать ли с чистого листа.</summary>
        public static void StampAll(bool[] mask, int w, int h, float mapWidth, float mapHeight,
                                    IEnumerable<PaintedRiver> rivers,
                                    int rectX = 0, int rectY = 0, int rectW = int.MaxValue, int rectH = int.MaxValue)
        {
            var ribbons = BuildRibbons(rivers);
            foreach (var ribbon in ribbons)
                Stamp(mask, w, h, mapWidth, mapHeight, ribbon.Curve, ribbon.Width,
                      ribbon.WideStart, ribbon.WideEnd, rectX, rectY, rectW, rectH);
        }

        /// <summary>Насколько кончик вправе НЕ ДОТЯНУТЬСЯ до чужого русла и всё-таки считаться
        /// притоком, в долях своей ширины. Нужно потому, что опорные точки мазка — это ЦЕНТРЫ
        /// КЛЕТОК, а не позиция курсора: ведя приток к стволу, ДМ отпускает кнопку, когда курсор
        /// коснулся ствола на глаз, а последняя опора остаётся в центре клетки, до полуклетки не
        /// доходя. Отсюда и жалоба «сужается, только если начать рисовать ОТ реки»: в начале мазка
        /// по стволу нарочно щёлкают, а в конце — как получится.</summary>
        const float SnapReach = 1.5f;

        /// <summary>Переводит реки в кривые и решает про каждый конец, раздаваться ему или нет.
        /// Решается ПО МЕСТУ, а не хранится в реке: сотрут ствол — приток сам увидит, что впадать
        /// больше некуда, и вернёт себе широкий конец.</summary>
        public static List<Ribbon> BuildRibbons(IEnumerable<PaintedRiver> rivers)
        {
            var result = new List<Ribbon>();
            if (rivers == null) return result;

            foreach (var river in rivers)
            {
                if (river == null || river.Points == null || river.Points.Count < 2) continue;
                var curve = RiverPaintOps.BuildCurve(river.Points, river.Width);
                if (curve.Count < 2) continue;
                result.Add(new Ribbon
                {
                    Id = river.Id, Curve = curve, Width = river.Width,
                    WideStart = true, WideEnd = true
                });
            }

            for (int i = 0; i < result.Count; i++)
            {
                var ribbon = result[i];
                ribbon.WideStart = !ResolveJoin(ref ribbon, atStart: true, result, i);
                ribbon.WideEnd = !ResolveJoin(ref ribbon, atStart: false, result, i);
                result[i] = ribbon;
            }
            return result;
        }

        /// <summary>Впадает ли этот конец в чужое русло — и если почти впадает, ДОТЯГИВАЕТ его до
        /// чужой оси. Дотягивание обязательно: без него конец, признанный притоком, и сузился бы, и
        /// остался в стороне — между ним и стволом зияла бы щель там, где ДМ вёл кисть к слиянию.
        ///
        /// Мерка касания — не «половина номинальной ширины», а фактическая полуширина чужой реки в
        /// ближайшей точке плюс полуширина тела самого кончика: спрашиваем ровно то, что нужно, —
        /// «если этот конец сузить, будут ли реки всё ещё соприкасаться».</summary>
        static bool ResolveJoin(ref Ribbon ribbon, bool atStart, IReadOnlyList<Ribbon> ribbons, int skip)
        {
            var tip = atStart ? ribbon.Curve[0] : ribbon.Curve[ribbon.Curve.Count - 1];
            float tipBodyHalf = ribbon.Width * 0.5f * RiverPaintOps.BodyWidthFactor;
            float reach = ribbon.Width * SnapReach;

            float bestDistance = float.MaxValue;
            Vector2 bestPoint = tip;

            for (int i = 0; i < ribbons.Count; i++)
            {
                if (i == skip) continue;
                var other = ribbons[i];
                if (other.Curve == null || other.Curve.Count < 2) continue;

                float distance = ClosestOnRibbon(other, tip, out float halfThere, out var point);
                if (distance <= halfThere + tipBodyHalf) return true;   // уже соприкасаются
                if (distance < bestDistance) { bestDistance = distance; bestPoint = point; }
            }

            if (bestDistance > reach) return false;

            if (atStart) ribbon.Curve.Insert(0, bestPoint);
            else ribbon.Curve.Add(bestPoint);
            return true;
        }

        /// <summary>Ближайшая к точке точка чужого русла: расстояние, полуширина русла там и сама
        /// точка (по ней кончик и дотягивают).</summary>
        static float ClosestOnRibbon(Ribbon ribbon, Vector2 p, out float halfThere, out Vector2 closest)
        {
            var arc = RiverPaintOps.ArcLengths(ribbon.Curve);
            float total = arc[arc.Length - 1];

            float bestSq = float.MaxValue, bestArc = 0f;
            closest = ribbon.Curve[0];
            for (int s = 0; s < ribbon.Curve.Count - 1; s++)
            {
                Vector2 a = ribbon.Curve[s], b = ribbon.Curve[s + 1];
                float t = ProjectOnSegment(a, b, p, out float distSq);
                if (distSq >= bestSq) continue;
                bestSq = distSq;
                bestArc = arc[s] + (arc[s + 1] - arc[s]) * t;
                closest = a + (b - a) * t;
            }

            halfThere = RiverPaintOps.HalfWidthAt(bestArc, total, ribbon.Width, ribbon.WideStart, ribbon.WideEnd);
            return (float)Math.Sqrt(bestSq);
        }

        /// <summary>Отмечает в маске пиксели, накрытые одним руслом: осевая кривая, раздутая на
        /// половину ширины. Идём ПО ОТРЕЗКАМ, а не по общей рамке всей реки: у извилистой реки рамка
        /// занимает пол-карты, а сумма рамок отрезков — только само русло с каёмкой.</summary>
        public static void Stamp(bool[] mask, int w, int h, float mapWidth, float mapHeight,
                                 IReadOnlyList<Vector2> curve, float width,
                                 bool wideStart = true, bool wideEnd = true,
                                 int rectX = 0, int rectY = 0, int rectW = int.MaxValue, int rectH = int.MaxValue)
        {
            if (mask == null || curve == null || curve.Count < 2) return;
            if (w <= 0 || h <= 0 || mapWidth <= 1e-5f || mapHeight <= 1e-5f || width <= 0f) return;

            // Русло не одной толщины: широкое у концов, тонкое в теле (RiverPaintOps.HalfWidthAt).
            var arc = RiverPaintOps.ArcLengths(curve);
            float total = arc[arc.Length - 1];

            int clipX0 = Math.Max(0, rectX);
            int clipY0 = Math.Max(0, rectY);
            int clipX1 = rectW == int.MaxValue ? w - 1 : Math.Min(w - 1, rectX + rectW - 1);
            int clipY1 = rectH == int.MaxValue ? h - 1 : Math.Min(h - 1, rectY + rectH - 1);

            for (int s = 0; s < curve.Count - 1; s++)
            {
                Vector2 a = curve[s], b = curve[s + 1];
                float halfMax = Math.Max(RiverPaintOps.HalfWidthAt(arc[s], total, width, wideStart, wideEnd),
                                         RiverPaintOps.HalfWidthAt(arc[s + 1], total, width, wideStart, wideEnd));

                int x0 = Math.Max(clipX0, PixelFloorX(Math.Min(a.X, b.X) - halfMax, w, mapWidth));
                int x1 = Math.Min(clipX1, PixelCeilX(Math.Max(a.X, b.X) + halfMax, w, mapWidth));
                int y0 = Math.Max(clipY0, PixelFloorX(Math.Min(a.Y, b.Y) - halfMax, h, mapHeight));
                int y1 = Math.Min(clipY1, PixelCeilX(Math.Max(a.Y, b.Y) + halfMax, h, mapHeight));

                for (int y = y0; y <= y1; y++)
                {
                    float py = (y + 0.5f) / h * mapHeight;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = (x + 0.5f) / w * mapWidth;
                        // Полуширина берётся в ТОЙ ЖЕ точке отрезка, куда спроектировался пиксель, и
                        // считается по её настоящему расстоянию вдоль русла, а не смешиванием
                        // концов отрезка: у короткого мазка кривая бывает всего из двух точек, и
                        // смешивание дало бы одну ширину на всю реку — сужение пропало бы совсем.
                        float t = ProjectOnSegment(a, b, new Vector2(px, py), out float distSq);
                        float halfHere = RiverPaintOps.HalfWidthAt(
                            arc[s] + (arc[s + 1] - arc[s]) * t, total, width, wideStart, wideEnd);
                        if (distSq <= halfHere * halfHere)
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

        /// <summary>Куда проектируется точка на отрезок: возвращает параметр [0,1] и заодно квадрат
        /// расстояния до неё (одно вычисление вместо двух — на миллионах пикселей это заметно).</summary>
        static float ProjectOnSegment(Vector2 a, Vector2 b, Vector2 p, out float distSq)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-8f) { distSq = (p - a).LengthSquared(); return 0f; }
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            distSq = (p - (a + ab * t)).LengthSquared();
            return t;
        }
    }
}
