using System.Collections.Generic;
using WorldGen.Generation.Mountains;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Собирает все горы пятна в ОДИН меш, в котором треугольники уже уложены в порядке маляра:
    /// дальние вперёд, ближние следом. Ни глубина, ни сортировка Unity в этом не участвуют — их
    /// заменяет порядок подачи (см. MountainPaint.shader: ZWrite Off, ZTest Always).
    ///
    /// Почему одним мешем, а не объектом на гору: на пятне средних размеров гор сотни, и каждая
    /// отдельным MeshRenderer'ом — это сотни вызовов отрисовки на кадр плюс своя сортировка Unity
    /// по центрам объектов, которая на перекрывающихся плоских фигурах даёт мерцание. Один меш
    /// снимает оба вопроса разом.
    ///
    /// Заливка каждой горы — сплошной цвет, поэтому перекрытия соседей не дают швов: получается
    /// единая масса со складками, а не цепочка значков. Цвет берётся от глубины (§10 «Порядок и
    /// глубина»), поэтому у соседей он почти одинаков, а по всей сцене работает как воздушная
    /// перспектива. Обводится только гребень: дуга подошвы прочерчивала бы соседа поперёк.
    /// </summary>
    public static class MountainMeshBuilder
    {
        /// <summary>
        /// yHeight — высота слоя над плоскостью карты; far/near — концы тональной шкалы (дальние
        /// светлее); ink — цвет линии гребня; crestWidth — её толщина в мировых единицах.
        /// Список сортируется на месте: дальняя гора (её ближайшая точка ВЫШЕ по экрану) идёт первой.
        /// </summary>
        public static UnityEngine.Mesh Build(List<MountainShape> shapes, float yHeight,
                                             UnityEngine.Color32 far, UnityEngine.Color32 near,
                                             UnityEngine.Color32 ink, float crestWidth)
        {
            var mesh = new UnityEngine.Mesh();
            if (shapes == null || shapes.Count == 0) return mesh;

            // Порядок задаёт чистый слой: при равной глубине там доразбирают по ярусу, а простая
            // сортировка по Depth ещё и неустойчива — рисунок ездил бы между запусками.
            MountainGeometry.SortForPainting(shapes);

            float depthMin = float.PositiveInfinity, depthMax = float.NegativeInfinity;
            foreach (var s in shapes)
            {
                if (s.Depth < depthMin) depthMin = s.Depth;
                if (s.Depth > depthMax) depthMax = s.Depth;
            }
            float span = depthMax - depthMin;
            if (span <= 0f) span = 1f;

            var verts = new List<UnityEngine.Vector3>();
            var colors = new List<UnityEngine.Color32>();
            var tris = new List<int>();

            foreach (var shape in shapes)
            {
                // 0 — самая дальняя гора, 1 — самая ближняя.
                float t = (depthMax - shape.Depth) / span;
                var fill = UnityEngine.Color32.Lerp(far, near, t);
                AddFill(shape, fill, yHeight, verts, colors, tris);
                AddCrest(shape, ink, crestWidth, yHeight, verts, colors, tris);
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

        /// <summary>Лента постоянной ширины вдоль ломаной — след кисти. Нужна дважды: показать
        /// ДМ, куда лёг мазок (шип), и рисовать мгновенное превью, пока кнопка зажата, потому что
        /// сами горы считаются в фоне и за курсором не успевают.</summary>
        public static UnityEngine.Mesh BuildRibbon(List<Vec2> line, float width, float yHeight,
                                                   UnityEngine.Color32 color)
        {
            var mesh = new UnityEngine.Mesh();
            if (line == null || line.Count < 2 || width <= 0f) return mesh;

            var verts = new List<UnityEngine.Vector3>(line.Count * 2);
            var colors = new List<UnityEngine.Color32>(line.Count * 2);
            var tris = new List<int>((line.Count - 1) * 6);
            var shape = new MountainShape { Crest = line };
            AddCrest(shape, color, width, yHeight, verts, colors, tris);

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Тело горы. Силуэт монотонен по горизонтали — гребень и ближняя дуга подошвы идут
        /// слева направо между одними и теми же концами, — поэтому он сшивается проходом по двум
        /// цепям сразу, без общего алгоритма триангуляции.</summary>
        static void AddFill(MountainShape shape, UnityEngine.Color32 color, float y,
                            List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris)
        {
            var crest = shape.Crest;
            var front = shape.Front;
            if (crest.Count < 2 || front.Count < 2) return;

            int baseIndex = verts.Count;
            foreach (var p in crest) { verts.Add(new UnityEngine.Vector3(p.X, y, p.Y)); colors.Add(color); }
            int frontStart = verts.Count;
            foreach (var p in front) { verts.Add(new UnityEngine.Vector3(p.X, y, p.Y)); colors.Add(color); }

            int i = 0, j = 0;
            while (i < crest.Count - 1 || j < front.Count - 1)
            {
                bool advanceCrest = j >= front.Count - 1 ||
                                    (i < crest.Count - 1 && crest[i + 1].X <= front[j + 1].X);
                if (advanceCrest)
                {
                    tris.Add(baseIndex + i); tris.Add(frontStart + j); tris.Add(baseIndex + i + 1);
                    i++;
                }
                else
                {
                    tris.Add(baseIndex + i); tris.Add(frontStart + j); tris.Add(frontStart + j + 1);
                    j++;
                }
            }
        }

        /// <summary>Линия гребня: лента постоянной ширины вдоль двух склонов. Без шапок и без
        /// сужения — это штрих пером, а не река.</summary>
        static void AddCrest(MountainShape shape, UnityEngine.Color32 color, float width, float y,
                             List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris)
        {
            var line = shape.Crest;
            if (line.Count < 2 || width <= 0f) return;
            float half = width * 0.5f;

            int start = verts.Count;
            for (int i = 0; i < line.Count; i++)
            {
                Vec2 offset = NormalAt(line, i) * half;
                verts.Add(new UnityEngine.Vector3(line[i].X - offset.X, y, line[i].Y - offset.Y));
                colors.Add(color);
                verts.Add(new UnityEngine.Vector3(line[i].X + offset.X, y, line[i].Y + offset.Y));
                colors.Add(color);
            }

            for (int i = 0; i < line.Count - 1; i++)
            {
                int a = start + i * 2, b = start + (i + 1) * 2;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        }

        /// <summary>Усреднённая нормаль в точке ломаной. Ус (miter) не растягиваем: на вершине угол
        /// острый, и растянутый ус выдал бы шип длиной в полгоры.</summary>
        static Vec2 NormalAt(List<Vec2> pts, int i)
        {
            int n = pts.Count;
            Vec2 a = i > 0 ? Normal(pts[i - 1], pts[i]) : Normal(pts[0], pts[1]);
            Vec2 b = i < n - 1 ? Normal(pts[i], pts[i + 1]) : Normal(pts[n - 2], pts[n - 1]);
            Vec2 m = a + b;
            return m.LengthSquared() < 1e-8f ? a : Vec2.Normalize(m);
        }

        static Vec2 Normal(Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            if (d.LengthSquared() < 1e-8f) return new Vec2(0f, 1f);
            d = Vec2.Normalize(d);
            return new Vec2(-d.Y, d.X);
        }
    }
}
