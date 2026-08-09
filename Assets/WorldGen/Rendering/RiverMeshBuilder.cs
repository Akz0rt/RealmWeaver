using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Лента русла для ПРЕДПРОСМОТРА открытого мазка — и только для него.
    ///
    /// Готовые реки этим не рисуются: они топятся в маске суша/вода (см. RiverMask), и всю их
    /// раскраску — глубину, ореол у берега, тёмную обводку, песчаную кромку — карта делает сама,
    /// потому что река ТАМ и есть водоём. Но пересчёт маски задевает поля дистанции на всю карту,
    /// и гонять его на каждое движение мыши нельзя, поэтому пока кнопка нажата ДМ видит вот эту
    /// лёгкую полупрозрачную ленту, а настоящая вода появляется на отпускании. Ровно тем же живёт
    /// кисть рельефа: мгновенная гранёная заплатка во время мазка, честный берег на отпускании.
    ///
    /// Лента лежит в плоскости карты, как границы регионов (MapBorderBuilder), — не LineRenderer:
    /// тот разворачивается к камере и на наклоне встал бы на ребро. Стыки сегментов сводятся усом
    /// (miter), иначе на внешней стороне каждого поворота оставался бы клин пустоты. Типы UnityEngine
    /// пишутся полным именем: System.Numerics.Vector2 и UnityEngine.Vector2 конфликтуют по короткому.
    /// </summary>
    public static class RiverMeshBuilder
    {
        /// <summary>Предел растяжения уса на крутом повороте: без него на почти-развороте
        /// смещение уходит в бесконечность и лента выворачивается наизнанку.</summary>
        const float MinMiterDot = 0.4f;

        /// <summary>Сегментов в полукруге на конце ленты.</summary>
        const int CapSegments = 8;

        public static UnityEngine.Mesh Build(IReadOnlyList<Vector2> curve, float width, float yHeight,
                                             UnityEngine.Color32 color)
        {
            var mesh = new UnityEngine.Mesh();
            if (curve == null || curve.Count < 2 || width <= 0f) return mesh;

            int n = curve.Count;
            float half = width * 0.5f;

            var verts = new List<UnityEngine.Vector3>(n * 2 + CapSegments * 2 + 4);
            var colors = new List<UnityEngine.Color32>(verts.Capacity);
            var tris = new List<int>((n - 1) * 6 + CapSegments * 6);

            for (int i = 0; i < n; i++)
            {
                Vector2 offset = OffsetAt(curve, i, half);
                var p = curve[i];
                verts.Add(new UnityEngine.Vector3(p.X - offset.X, yHeight, p.Y - offset.Y)); colors.Add(color);
                verts.Add(new UnityEngine.Vector3(p.X + offset.X, yHeight, p.Y + offset.Y)); colors.Add(color);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = (i + 1) * 2;
                tris.Add(a + 0); tris.Add(b + 0); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b + 0); tris.Add(b + 1);
            }

            AddCap(verts, colors, tris, curve[0], curve[1], half, yHeight, color);
            AddCap(verts, colors, tris, curve[n - 1], curve[n - 2], half, yHeight, color);

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Полукруглая шапка на конце: веер от кончика наружу.</summary>
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
