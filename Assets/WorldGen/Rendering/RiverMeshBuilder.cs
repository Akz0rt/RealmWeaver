using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Rendering
{
    /// <summary>Как выглядит один конец русла. Цвета берутся из палитры карты вызывающим
    /// (см. WorldMapRenderer.RiverEndStyleFor) — строителю меша палитра не нужна.</summary>
    public struct RiverEndStyle
    {
        /// <summary>Цвет у кромки русла — светлое мелководье, как у берега озера.</summary>
        public UnityEngine.Color32 Edge;
        /// <summary>Цвет по оси русла — глубина, как в середине озера.</summary>
        public UnityEngine.Color32 Center;
        /// <summary>Конец на суше: скруглить, чтобы река не обрывалась ножом.</summary>
        public bool Round;
        /// <summary>Длина, на которой конец гаснет до полной прозрачности (0 — не гасить). Отмеряется
        /// от кончика русла, а кончик у устья лежит УЖЕ ВНУТРИ водоёма: до самой кромки река идёт в
        /// полную силу и растворяется только за ней, в воде того же цвета. Поэтому у берега не
        /// остаётся ни шва, ни бледного хвоста — река читается продолжением водоёма.</summary>
        public float FadeLength;
        /// <summary>Длина, на которой поперечный профиль распрямляется: тёмная ось русла подходит к
        /// устью, уже сойдясь в светлое мелководье. Иначе тёмная сердцевина торчала бы языком в
        /// светлую прибрежную полосу водоёма (0 — не распрямлять).</summary>
        public float FlattenLength;
    }

    /// <summary>
    /// Строит плоскую ленту русла по сглаженной кривой (см. RiverPaintOps.BuildCurve). Лента лежит
    /// в плоскости карты, как границы регионов и береговая линия (MapBorderBuilder), — не
    /// LineRenderer: тот разворачивается к камере и на наклоне «встал бы на ребро».
    ///
    /// Три вершины на поперечник (край—ось—край), а не две: вода в проекте красится светлым
    /// мелководьем у берега и тёмной глубиной в середине (см. MapRasterizer.ColorForWaterPixel), и
    /// у реки то же правило — но нарисовать его можно, только имея вершину на оси русла.
    /// Вдоль русла цвета переливаются от стиля одного конца к стилю другого: река из озера в море
    /// меняет цвет по дороге. Прозрачность у устья гасится, свободный конец закругляется веером.
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

        public static UnityEngine.Mesh Build(IReadOnlyList<Vector2> polyline, float width, float yHeight,
                                             RiverEndStyle start, RiverEndStyle end)
        {
            var mesh = new UnityEngine.Mesh();
            if (polyline == null || polyline.Count < 2 || width <= 0f) return mesh;

            float half = width * 0.5f;
            int n = polyline.Count;

            // Длина по дуге до каждой точки — по ней и цвет вдоль русла, и затухание у концов.
            var distance = new float[n];
            for (int i = 1; i < n; i++)
                distance[i] = distance[i - 1] + Vector2.Distance(polyline[i - 1], polyline[i]);
            float total = distance[n - 1];
            if (total < 1e-5f) return mesh;

            var verts = new List<UnityEngine.Vector3>(n * 3 + CapSegments * 2 + 4);
            var colors = new List<UnityEngine.Color32>(verts.Capacity);
            var tris = new List<int>((n - 1) * 12 + CapSegments * 6);

            for (int i = 0; i < n; i++)
            {
                Vector2 offset = OffsetAt(polyline, i, half);
                var p = polyline[i];
                float t = distance[i] / total;
                float alpha = AlphaAt(distance[i], total, start, end);
                var edgeBase = Lerp(start.Edge, end.Edge, t);
                // У устья профиль распрямляется: чем ближе к воде, тем меньше ось отличается от края.
                var centerBase = Lerp(edgeBase, Lerp(start.Center, end.Center, t),
                                      DepthAt(distance[i], total, start, end));
                var edge = Fade(edgeBase, alpha);
                var center = Fade(centerBase, alpha);

                verts.Add(new UnityEngine.Vector3(p.X - offset.X, yHeight, p.Y - offset.Y)); colors.Add(edge);
                verts.Add(new UnityEngine.Vector3(p.X, yHeight, p.Y));                        colors.Add(center);
                verts.Add(new UnityEngine.Vector3(p.X + offset.X, yHeight, p.Y + offset.Y)); colors.Add(edge);
            }

            // Четыре треугольника на сегмент: половина ленты слева от оси, половина справа.
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 3, b = (i + 1) * 3;
                tris.Add(a + 0); tris.Add(b + 0); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b + 0); tris.Add(b + 1);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(a + 2);
                tris.Add(a + 2); tris.Add(b + 1); tris.Add(b + 2);
            }

            if (start.Round) AddCap(verts, colors, tris, polyline[0], polyline[1], half, yHeight,
                                     Fade(start.Edge, AlphaAt(0f, total, start, end)),
                                     Fade(start.Center, AlphaAt(0f, total, start, end)));
            if (end.Round) AddCap(verts, colors, tris, polyline[n - 1], polyline[n - 2], half, yHeight,
                                   Fade(end.Edge, AlphaAt(total, total, start, end)),
                                   Fade(end.Center, AlphaAt(total, total, start, end)));

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Полукруглая «шапка» на свободном конце: веер от оси русла наружу, от одного
        /// края поперечника через кончик к другому. Внутрь реки не заходит — продолжает её.</summary>
        static void AddCap(List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris,
                           Vector2 tip, Vector2 inward, float half, float yHeight,
                           UnityEngine.Color32 edgeColor, UnityEngine.Color32 centerColor)
        {
            Vector2 dir = inward - tip;
            if (dir.LengthSquared() < 1e-8f) return;
            dir = Vector2.Normalize(dir);
            Vector2 outward = -dir;
            Vector2 side = new Vector2(-dir.Y, dir.X);

            int centerIndex = verts.Count;
            verts.Add(new UnityEngine.Vector3(tip.X, yHeight, tip.Y));
            colors.Add(centerColor);

            int firstRim = verts.Count;
            for (int k = 0; k <= CapSegments; k++)
            {
                double angle = -System.Math.PI / 2.0 + System.Math.PI * k / CapSegments;
                float cos = (float)System.Math.Cos(angle), sin = (float)System.Math.Sin(angle);
                Vector2 rim = tip + (outward * cos + side * sin) * half;
                verts.Add(new UnityEngine.Vector3(rim.X, yHeight, rim.Y));
                colors.Add(edgeColor);
            }

            for (int k = 0; k < CapSegments; k++)
            {
                tris.Add(centerIndex);
                tris.Add(firstRim + k);
                tris.Add(firstRim + k + 1);
            }
        }

        /// <summary>Прозрачность в точке: полная непрозрачность в теле реки, плавный уход в ноль на
        /// длине FadeLength у того конца, что упирается в водоём.</summary>
        static float AlphaAt(float distance, float total, RiverEndStyle start, RiverEndStyle end)
        {
            float alpha = 1f;
            if (start.FadeLength > 0f) alpha = System.Math.Min(alpha, distance / start.FadeLength);
            if (end.FadeLength > 0f) alpha = System.Math.Min(alpha, (total - distance) / end.FadeLength);
            return System.Math.Clamp(alpha, 0f, 1f);
        }

        /// <summary>Насколько в этой точке проявлена «глубина» русла: 1 в теле реки, 0 у самого
        /// устья. Профиль распрямляется заранее, чтобы в водоём река входила ровной полосой
        /// мелководья — такой же, какой у водоёма его собственный берег.</summary>
        static float DepthAt(float distance, float total, RiverEndStyle start, RiverEndStyle end)
        {
            float depth = 1f;
            if (start.FlattenLength > 0f) depth = System.Math.Min(depth, distance / start.FlattenLength);
            if (end.FlattenLength > 0f) depth = System.Math.Min(depth, (total - distance) / end.FlattenLength);
            return System.Math.Clamp(depth, 0f, 1f);
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
