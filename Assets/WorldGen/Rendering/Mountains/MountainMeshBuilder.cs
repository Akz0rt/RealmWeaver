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
    /// единая масса со складками, а не цепочка значков. Цвет берётся от ЯРУСА (решение ДМ
    /// 2026-08-14): у соседей по слою он в точности одинаков, а слои расходятся ступенькой — так и
    /// читается форма массы, как полосы на рельефной карте. Обводится только гребень: дуга подошвы
    /// прочерчивала бы соседа поперёк.
    /// </summary>
    public static class MountainMeshBuilder
    {
        /// <summary>
        /// Складывает горы в готовый меш. Порядок подачи — тот, в котором список пришёл: сортировку
        /// делает чистый слой (MountainGeometry.SortForPainting), ярусы — он же (AssignTiers), здесь
        /// ярус только превращается в цвет.
        ///
        /// mesh — меш, который надо переписать. Пересчёт идёт за каждый мазок, и меш тут крупный;
        /// поэтому его переиспользуют, а не заводят новый: иначе за мазок в памяти оседает десяток
        /// мёртвых мешей, и убирать их приходится вручную.
        /// </summary>
        public static void Build(UnityEngine.Mesh mesh, List<MountainShape> shapes,
                                 in MountainPaintStyle style)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (shapes == null || shapes.Count == 0) return;

            var verts = new List<UnityEngine.Vector3>();
            var colors = new List<UnityEngine.Color32>();
            var tris = new List<int>();

            foreach (var shape in shapes)
            {
                AddFill(shape, style.Fill(shape.Tier), style.LayerY, verts, colors, tris);
                AddCrest(shape, style.Ink, style.CrestWidth, style.LayerY, verts, colors, tris);
            }

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Тот же меш, но новым объектом — так удобнее одноразовым вызовам (шип).</summary>
        public static UnityEngine.Mesh Build(List<MountainShape> shapes, in MountainPaintStyle style)
        {
            var mesh = new UnityEngine.Mesh();
            Build(mesh, shapes, style);
            return mesh;
        }

        /// <summary>Лента постоянной ширины вдоль ломаной — след кисти. Нужна дважды: показать
        /// ДМ, куда лёг мазок (шип), и рисовать мгновенное превью, пока кнопка зажата, потому что
        /// сами горы считаются в фоне и за курсором не успевают.</summary>
        public static UnityEngine.Mesh BuildRibbon(List<Vec2> line, float width, float yHeight,
                                                   UnityEngine.Color32 color)
        {
            var mesh = new UnityEngine.Mesh();
            BuildRibbon(mesh, line, width, yHeight, color);
            return mesh;
        }

        /// <summary>Та же лента в готовый меш: превью пересобирается на каждое движение курсора.</summary>
        public static void BuildRibbon(UnityEngine.Mesh mesh, List<Vec2> line, float width,
                                       float yHeight, UnityEngine.Color32 color)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (line == null || line.Count < 2 || width <= 0f) return;

            var verts = new List<UnityEngine.Vector3>(line.Count * 2);
            var colors = new List<UnityEngine.Color32>(line.Count * 2);
            var tris = new List<int>((line.Count - 1) * 6);
            var shape = new MountainShape { Crest = line };
            AddCrest(shape, color, width, yHeight, verts, colors, tris);

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Тело горы: точки как есть, треугольники — из чистого слоя, который умеет сшивать
        /// полосу и при завернувшейся подошве (см. MountainTriangulation).</summary>
        static void AddFill(MountainShape shape, UnityEngine.Color32 color, float y,
                            List<UnityEngine.Vector3> verts, List<UnityEngine.Color32> colors, List<int> tris)
        {
            var indices = MountainTriangulation.Fill(shape);
            if (indices.Length == 0) return;

            int baseIndex = verts.Count;
            foreach (var p in shape.Crest) { verts.Add(new UnityEngine.Vector3(p.X, y, p.Y)); colors.Add(color); }
            foreach (var p in shape.Front) { verts.Add(new UnityEngine.Vector3(p.X, y, p.Y)); colors.Add(color); }
            foreach (int index in indices) tris.Add(baseIndex + index);
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
