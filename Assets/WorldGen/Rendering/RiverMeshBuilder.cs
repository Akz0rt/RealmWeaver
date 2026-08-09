using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Rendering
{
    /// <summary>Как ведёт себя один конец русла.</summary>
    public struct RiverEndStyle
    {
        /// <summary>Свободный конец на суше: скруглить, чтобы река не обрывалась ножом. Конец,
        /// упирающийся в другую реку, НЕ скругляется — там слияние, и шапка выпирала бы бугром.</summary>
        public bool Round;

        /// <summary>Длина растворения в водоёме (0 — конец не в воде). На ней русло разом сужается
        /// к точке, гаснет по прозрачности и перекрашивается в MouthWater. Три вещи сразу, потому
        /// что по отдельности каждая оставляет след: сужение без гашения даёт клин, гашение без
        /// сужения — полосу с резкими боками, а верный цвет без первых двух всё равно виден швом,
        /// когда вода вокруг темнее.</summary>
        public float MouthLength;

        /// <summary>Цвет воды ПОД кончиком русла — берётся из-под карты вызывающим
        /// (WorldMapRenderer.MouthWaterColor), а не из общего слота палитры: у берега вода светлее
        /// от прибрежного ореола, глубже — темнее, и промахнуться цветом здесь заметнее всего.</summary>
        public UnityEngine.Color32 MouthWater;
    }

    /// <summary>Одна река на отрисовку: уже сглаженная кривая (см. RiverPaintOps.BuildCurve),
    /// ширина русла и повадки обоих концов.</summary>
    public struct RiverShape
    {
        public IReadOnlyList<Vector2> Curve;
        public float Width;
        public RiverEndStyle Start;
        public RiverEndStyle End;
    }

    /// <summary>
    /// Строит ОДИН плоский меш на все нарисованные реки. Лента лежит в плоскости карты, как границы
    /// регионов и береговая линия (MapBorderBuilder), — не LineRenderer: тот разворачивается к
    /// камере и на наклоне «встал бы на ребро».
    ///
    /// Поперечный градиент (у кромки светлое мелководье, к оси темнее — то же правило, что у
    /// водоёмов, см. MapRasterizer.ColorForWaterPixel) набирается ВЛОЖЕННЫМИ ПОЛОСАМИ: сначала
    /// самая широкая лента светлым цветом, поверх неё поуже и темнее, и так до узкой сердцевины.
    /// Так, а не плавным градиентом на трёх вершинах поперечника, ради ГЛАВНОГО требования ДМ:
    /// реки, пересекающие друг друга, должны читаться одной рекой, без видимых перекрытий. Полосы
    /// это дают даром — на перекрестье встречаются куски ОДНОГО И ТОГО ЖЕ цвета, а одинаковый
    /// непрозрачный цвет поверх себя невидим.
    ///
    /// Отсюда же и «один меш на все реки»: порядок отрисовки должен быть строго «все внешние
    /// полосы всех рек → все следующие → сердцевины». Отдельными объектами это не удержать —
    /// прозрачная очередь сортирует их по расстоянию до камеры, и от поворота камеры сердцевина
    /// одной реки перепрыгнула бы поверх кромки другой (мигающий шов, который на скриншоте не
    /// поймать). Внутри одного меша порядок треугольников и есть порядок закраски (ZWrite off).
    ///
    /// Материал должен быть БЕЛЫМ и полупрозрачным (Sprites/Default) — цвет несут вершины.
    /// Стыки сегментов сводятся усом (miter), иначе на внешней стороне каждого поворота оставался
    /// бы клин пустоты. Типы UnityEngine пишутся полным именем: System.Numerics.Vector2 и
    /// UnityEngine.Vector2 конфликтуют по короткому имени (та же причина, что в MapBorderBuilder).
    /// </summary>
    public static class RiverMeshBuilder
    {
        /// <summary>Предел растяжения уса на крутом повороте: без него на почти-развороте
        /// смещение уходит в бесконечность и лента выворачивается наизнанку.</summary>
        const float MinMiterDot = 0.4f;

        /// <summary>Сегментов в полукруге скруглённого конца. Восьми хватает: конец шириной в
        /// несколько единиц карты, гранёности на глаз не видно.</summary>
        const int CapSegments = 8;

        /// <summary>Вложенных полос в поперечнике. Шесть: разница между кромкой и сердцевиной у
        /// реки невелика (это мелкая вода, а не бездна), и шагами по шестой части её не видно, а
        /// вот перекрестье двух рек полосы делают идеально гладким.</summary>
        public const int DefaultBands = 6;

        public static UnityEngine.Mesh BuildAll(IReadOnlyList<RiverShape> rivers, float yHeight,
                                                UnityEngine.Color32 edge, UnityEngine.Color32 core,
                                                int bands = DefaultBands)
        {
            var mesh = new UnityEngine.Mesh();
            if (rivers == null || rivers.Count == 0) return mesh;
            if (bands < 1) bands = 1;

            var verts = new List<UnityEngine.Vector3>();
            var colors = new List<UnityEngine.Color32>();
            var tris = new List<int>();

            // Внешний цикл — по полосам, внутренний — по рекам. Только такой порядок и даёт
            // независимость от того, в каком порядке ДМ рисовал реки (см. сводку класса).
            for (int b = 0; b < bands; b++)
            {
                float widthScale = 1f - b / (float)bands;
                var bandColor = bands == 1 ? core : Lerp(edge, core, b / (float)(bands - 1));
                foreach (var river in rivers)
                    AddBand(verts, colors, tris, river, widthScale, bandColor, yHeight);
            }

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddBand(List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris,
                            RiverShape shape, float widthScale, UnityEngine.Color32 bandColor, float yHeight)
        {
            var curve = shape.Curve;
            if (curve == null || curve.Count < 2 || shape.Width <= 0f) return;

            int n = curve.Count;
            float half = shape.Width * 0.5f * widthScale;

            var distance = new float[n];
            for (int i = 1; i < n; i++)
                distance[i] = distance[i - 1] + Vector2.Distance(curve[i - 1], curve[i]);
            float total = distance[n - 1];
            if (total < 1e-5f) return;

            // Шапки досылаются после ленты, но красятся и сужаются ТЕМ ЖЕ, чем кончается поперечник
            // под ними: у короткой реки растворение дальнего устья дотягивается до ближнего конца
            // (заход равен 1.25 ширины, а мазок бывает и короче), и шапка «своим» цветом осталась бы
            // непрозрачным пятном на гаснущем хвосте.
            var capColor = new UnityEngine.Color32[2];
            var capHalf = new float[2];

            int firstVert = verts.Count;
            for (int i = 0; i < n; i++)
            {
                float kStart = MouthFactor(distance[i], shape.Start);
                float kEnd = MouthFactor(total - distance[i], shape.End);

                // Сужение по корню, гашение линейно: у самой воды лента уже почти прозрачна, но
                // ещё не выродилась в иглу — иначе конец читался бы наконечником стрелы.
                float k = System.Math.Min(kStart, kEnd);
                float taper = (float)System.Math.Sqrt(k);

                var c = bandColor;
                if (kStart < 1f) c = Lerp(shape.Start.MouthWater, c, kStart);
                else if (kEnd < 1f) c = Lerp(shape.End.MouthWater, c, kEnd);
                c = Fade(c, k);

                if (i == 0) { capColor[0] = c; capHalf[0] = half * taper; }
                if (i == n - 1) { capColor[1] = c; capHalf[1] = half * taper; }

                Vector2 offset = OffsetAt(curve, i, half * taper);
                var p = curve[i];
                verts.Add(new UnityEngine.Vector3(p.X - offset.X, yHeight, p.Y - offset.Y)); colors.Add(c);
                verts.Add(new UnityEngine.Vector3(p.X + offset.X, yHeight, p.Y + offset.Y)); colors.Add(c);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = firstVert + i * 2, d = firstVert + (i + 1) * 2;
                tris.Add(a + 0); tris.Add(d + 0); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(d + 0); tris.Add(d + 1);
            }

            if (shape.Start.Round) AddCap(verts, colors, tris, curve[0], curve[1], capHalf[0], yHeight, capColor[0]);
            if (shape.End.Round) AddCap(verts, colors, tris, curve[n - 1], curve[n - 2], capHalf[1], yHeight, capColor[1]);
        }

        /// <summary>Полукруглая «шапка» на свободном конце: веер от кончика наружу, от одного края
        /// поперечника через кончик к другому. Внутрь реки не заходит — продолжает её.</summary>
        static void AddCap(List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris,
                           Vector2 tip, Vector2 inward, float half, float yHeight, UnityEngine.Color32 color)
        {
            Vector2 dir = inward - tip;
            if (dir.LengthSquared() < 1e-8f) return;
            dir = Vector2.Normalize(dir);
            Vector2 outward = -dir;
            Vector2 side = new Vector2(-dir.Y, dir.X);

            int centerIndex = verts.Count;
            verts.Add(new UnityEngine.Vector3(tip.X, yHeight, tip.Y));
            colors.Add(color);

            int firstRim = verts.Count;
            for (int k = 0; k <= CapSegments; k++)
            {
                double angle = -System.Math.PI / 2.0 + System.Math.PI * k / CapSegments;
                float cos = (float)System.Math.Cos(angle), sin = (float)System.Math.Sin(angle);
                Vector2 rim = tip + (outward * cos + side * sin) * half;
                verts.Add(new UnityEngine.Vector3(rim.X, yHeight, rim.Y));
                colors.Add(color);
            }

            for (int k = 0; k < CapSegments; k++)
            {
                tris.Add(centerIndex);
                tris.Add(firstRim + k);
                tris.Add(firstRim + k + 1);
            }
        }

        /// <summary>Насколько точка ещё «река», а не «уже водоём»: 1 в теле русла, 0 у самого
        /// кончика, если этот конец впадает в воду.</summary>
        static float MouthFactor(float distanceFromTip, RiverEndStyle style)
        {
            if (style.MouthLength <= 0f) return 1f;
            return System.Math.Clamp(distanceFromTip / style.MouthLength, 0f, 1f);
        }

        static UnityEngine.Color32 Lerp(UnityEngine.Color32 a, UnityEngine.Color32 b, float t)
        {
            t = System.Math.Clamp(t, 0f, 1f);
            return new UnityEngine.Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                (byte)(a.a + (b.a - a.a) * t));
        }

        static UnityEngine.Color32 Fade(UnityEngine.Color32 c, float alpha)
            => new UnityEngine.Color32(c.r, c.g, c.b, (byte)System.Math.Clamp(c.a * alpha, 0f, 255f));

        /// <summary>Смещение от осевой линии к краю ленты в точке i. На концах — просто нормаль
        /// соседнего отрезка, внутри — ус: усреднённая нормаль, растянутая так, чтобы ширина ленты
        /// на повороте осталась прежней.</summary>
        static Vector2 OffsetAt(IReadOnlyList<Vector2> pts, int i, float half)
        {
            int n = pts.Count;
            Vector2 nA = i > 0 ? NormalOf(pts[i - 1], pts[i]) : NormalOf(pts[0], pts[1]);
            Vector2 nB = i < n - 1 ? NormalOf(pts[i], pts[i + 1]) : NormalOf(pts[n - 2], pts[n - 1]);

            Vector2 m = nA + nB;
            if (m.LengthSquared() < 1e-8f) return nA * half;   // разворот на 180° — берём одну сторону
            m = Vector2.Normalize(m);

            float dot = Vector2.Dot(m, nA);
            if (dot < MinMiterDot) dot = MinMiterDot;
            return m * (half / dot);
        }

        /// <summary>Единичная нормаль отрезка в плоскости карты (перпендикуляр влево).</summary>
        static Vector2 NormalOf(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            if (d.LengthSquared() < 1e-8f) return new Vector2(0f, 1f);
            d = Vector2.Normalize(d);
            return new Vector2(-d.Y, d.X);
        }
    }
}
