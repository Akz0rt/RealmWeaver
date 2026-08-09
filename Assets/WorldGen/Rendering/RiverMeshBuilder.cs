using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Строит плоскую ленту русла по сглаженной кривой (см. RiverPaintOps.Smooth). Лента лежит
    /// в плоскости карты, как границы регионов и береговая линия (MapBorderBuilder), — не
    /// LineRenderer: тот разворачивается к камере и на наклоне «встал бы на ребро».
    ///
    /// Стыки сегментов сводятся УСОМ (miter): у каждой точки берётся усреднённая нормаль двух
    /// соседних отрезков, и лента идёт сплошной полосой. Если строить по quad'у на отрезок, на
    /// внешней стороне каждого поворота оставался бы клин пустоты — на реке в 6 единиц шириной
    /// это видно как зазубрины.
    ///
    /// Типы UnityEngine пишутся полным именем: System.Numerics.Vector2 и UnityEngine.Vector2
    /// конфликтуют по короткому имени (та же причина, что в MapBorderBuilder).
    /// </summary>
    public static class RiverMeshBuilder
    {
        /// <summary>Предел растяжения уса на крутом повороте: без него на почти-развороте
        /// смещение уходит в бесконечность и лента выворачивается наизнанку.</summary>
        const float MinMiterDot = 0.4f;

        public static UnityEngine.Mesh Build(IReadOnlyList<Vector2> polyline, float width, float yHeight)
        {
            var mesh = new UnityEngine.Mesh();
            if (polyline == null || polyline.Count < 2 || width <= 0f) return mesh;

            float half = width * 0.5f;
            int n = polyline.Count;
            var verts = new List<UnityEngine.Vector3>(n * 2);
            var tris = new List<int>((n - 1) * 6);

            for (int i = 0; i < n; i++)
            {
                Vector2 offset = OffsetAt(polyline, i, half);
                var p = polyline[i];
                verts.Add(new UnityEngine.Vector3(p.X - offset.X, yHeight, p.Y - offset.Y));
                verts.Add(new UnityEngine.Vector3(p.X + offset.X, yHeight, p.Y + offset.Y));
            }

            for (int i = 0; i < n - 1; i++)
            {
                int b = i * 2;
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

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
