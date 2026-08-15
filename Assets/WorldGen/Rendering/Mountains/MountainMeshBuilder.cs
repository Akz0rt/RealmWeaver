using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation.Mountains;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Собирает все горы массива в ОДИН меш, в котором треугольники уже уложены в порядке маляра:
    /// дальние вперёд, ближние следом.
    ///
    /// Порядок внутри одной горы: тело → крошка → контур. Тело первым, потому что оно обязано
    /// закрыть тушь ДАЛЬНЕЙ горы; контур последним, потому что он обязан остаться поверх собственной
    /// крошки.
    ///
    /// Манера здесь ровно из трёх частей, и лишнего нет ни одной:
    ///   • КОНТУР — одна непрерывная лента вдоль ломаной (MountainOutline), жирная у вершины,
    ///     сходящая на нет книзу. Прежняя редакция клала вместо неё короткие штрихи в точках
    ///     границы плюс отдельную обводку склона на скачке — оттуда на карте были рваный контур и
    ///     чёрные клинья у вершин.
    ///   • КРОШКА — только в полной тени, порогом, а не вероятностью.
    ///   • Освещённый склон — ЧИСТАЯ БУМАГА. Промоины сняты: они шли как раз по свету и на образце
    ///     ДМ их нет.
    ///
    /// Собирается В ФОНЕ (см. MountainMeshData). На главном потоке остаётся только заливка в Mesh.
    /// </summary>
    public static class MountainMeshBuilder
    {
        /// <summary>
        /// На сколько поднимается каждая следующая гора. Волосок: пятьсот гор дают четверть
        /// десятой мировой единицы — на виде сверху это неразличимо, а глубине хватает, чтобы
        /// различить порядок.
        /// </summary>
        public const float DepthStep = 5e-4f;

        /// <summary>
        /// Горы → массивы меша. Чистый счёт: ни одного обращения к живому движку, поэтому зовётся
        /// из фонового потока. Порядок подачи — тот, в котором пришёл список (сортировку делает
        /// MountainGeometry.SortForPainting).
        /// </summary>
        public static void BuildData(MountainMeshData data, List<MountainShape> shapes,
                                     in MountainPaintStyle style, LiftSamples profile, float radius)
        {
            if (data == null) return;
            data.Clear();
            if (shapes == null || shapes.Count == 0 || profile == null) return;

            Vec2 light = MountainInk.Light;
            // Буферы раскладки заводятся ОДИН раз на весь массив, а не на гору: гор сотни, и
            // отдельный список на каждую — это сотни выбросов в мусор за один пересчёт.
            var scratchVerts = new List<Vec2>();
            var scratchTris = new List<int>();
            var line = new List<Vec2>();
            var radii = new List<float>();
            var rise = new List<float>();

            // Каждая следующая гора кладётся на волосок ВЫШЕ предыдущей. Порядок маляра остаётся
            // тем же (дальние раньше), но теперь он записан не только в очерёдности, а в самой
            // глубине — и линия дальней горы отсекается телом ближней, где бы её ни подали.
            int index = 0;
            foreach (var shape in shapes)
            {
                if (shape?.Base == null || shape.Base.Length < 3) continue;
                if (shape.LevelR == null || shape.LevelR.Length < 2) continue;
                data.Mountains++;

                float y = style.LayerY + index * DepthStep;
                index++;

                float density = style.Density(shape.Tier);
                AddBody(data, shape, y, profile, scratchVerts, scratchTris);
                AddGrit(data, shape, style, y, profile, light, density, radius);
                MountainOutline.Build(shape, profile, line, radii);
                MountainOutline.Heights(line, rise);
                AddOutline(data, shape, style, y, profile, line, radii, rise, density, radius);
            }
            data.Seal();
        }

        /// <summary>Заливка массивов в меш. ЕДИНСТВЕННОЕ, что обязано идти на главном потоке.</summary>
        public static void Upload(Mesh mesh, MountainMeshData data)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (data == null || data.Verts.Count == 0) return;

            mesh.indexFormat = data.Verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(data.Verts);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Всё разом — для одноразовых вызовов, где фон не нужен (шип, стенд).</summary>
        public static void Build(Mesh mesh, List<MountainShape> shapes, in MountainPaintStyle style,
                                 LiftSamples profile, float radius)
        {
            var data = new MountainMeshData();
            BuildData(data, shapes, style, profile, radius);
            Upload(mesh, data);
        }

        // ── тело ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Тело — ярусы: у каждого свой веер и полоса до соседнего. Раскладку даёт чистый слой
        /// (MountainTriangulation.Fill), здесь остаётся перевести точки в мировые.
        ///
        /// Тело НЕ КРАСИТ КАРТУ. Оно кладётся прозрачным и нужно ровно затем, чтобы записать
        /// глубину: линия дальней горы отсекается телом ближней. Так «окно в карту» становится
        /// точным — сквозь гору видна ровно та карта, что нарисована, со всем её сглаживанием,
        /// зерном и виньеткой.
        ///
        /// Красить тело цветом клетки (BuildGroundProbe) оказалось нельзя, и это стоило двух
        /// заходов: цвет клетки — плоское число, а карта на экране получается из него со
        /// сглаживанием между клетками и наложениями. На снежной палитре расхождение вышло таким,
        /// что горы легли белыми лоскутами поверх серо-голубой карты.
        /// </summary>
        static void AddBody(MountainMeshData data, MountainShape shape, float y,
                            LiftSamples profile, List<Vec2> scratchVerts, List<int> scratchTris)
        {
            MountainTriangulation.Fill(shape, profile, scratchVerts, scratchTris);
            if (scratchTris.Count == 0) return;

            int start = data.Verts.Count;
            for (int i = 0; i < scratchVerts.Count; i++)
            {
                Vec2 p = scratchVerts[i];
                data.Verts.Add(new Vector3(p.X, y, p.Y));
                data.Colors.Add(Invisible);
            }
            for (int i = 0; i < scratchTris.Count; i++) data.Tris.Add(start + scratchTris[i]);
        }

        /// <summary>Прозрачная краска тела: карту не трогает, глубину пишет.</summary>
        static readonly Color32 Invisible = new Color32(0, 0, 0, 0);

        // ── контур ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Контур — ОДНА лента переменной толщины вдоль ломаной, полосой треугольников.
        ///
        /// Разрывается она ровно в одном случае: когда толщина упала ниже пола (MountainInk.PenFloor)
        /// — то есть у самого низа горы, где граница садится на подошву. Это и есть «низа нет вовсе»
        /// с образца ДМ, а не разрыв линии. Внутри же горы линия непрерывна по построению: точки идут
        /// слева направо, каждое звено сшито со следующим общими вершинами.
        ///
        /// Направление ленты берётся ХОРДОЙ через соседей, а не отрезком: у вершины поворот резкий, и
        /// на отрезке лента дала бы залом. Краска непрозрачна, поэтому нахлёст на повороте не темнеет
        /// и шва не видно.
        ///
        /// Подрезка по суше — по МЕСТУ НА ЗЕМЛЕ под точкой контура: точка нарисована приподнятой на
        /// H·lift(r), и спрашивать про нарисованное место значило бы срезать макушку прибрежной горы,
        /// за которой лежит вода.
        /// </summary>
        static void AddOutline(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                               float y, LiftSamples profile, List<Vec2> line, List<float> radii,
                               List<float> rise, float density, float radius)
        {
            int count = line.Count;
            if (count < 2) return;
            Color32 ink = style.Ink;

            int prev = -1;                       // номер верхней вершины предыдущей поставленной точки
            for (int k = 0; k < count; k++)
            {
                float half = MountainInk.HalfWidth(rise[k], shape, radius, density);
                if (half <= 0f || !Grounded(style, shape, profile, line[k], radii[k])) { prev = -1; continue; }

                Vec2 dir = Chord(line, k);
                Vec2 nrm = new Vec2(-dir.Y, dir.X) * half;
                Vec2 p = line[k];

                int at = data.Verts.Count;
                data.Verts.Add(new Vector3(p.X - nrm.X, y, p.Y - nrm.Y));
                data.Verts.Add(new Vector3(p.X + nrm.X, y, p.Y + nrm.Y));
                data.Colors.Add(ink);
                data.Colors.Add(ink);

                if (prev >= 0)
                {
                    data.InkTris.Add(prev); data.InkTris.Add(at); data.InkTris.Add(prev + 1);
                    data.InkTris.Add(prev + 1); data.InkTris.Add(at); data.InkTris.Add(at + 1);
                }
                prev = at;
            }
        }

        /// <summary>Направление ленты в точке: хорда через соседей. У края берётся то, что есть.</summary>
        static Vec2 Chord(List<Vec2> line, int k)
        {
            Vec2 a = line[k > 0 ? k - 1 : k];
            Vec2 b = line[k + 1 < line.Count ? k + 1 : k];
            Vec2 d = b - a;
            if (d.LengthSquared() < 1e-10f) return new Vec2(1f, 0f);
            return Vec2.Normalize(d);
        }

        /// <summary>Стоит ли эта точка контура на суше. Место на земле = нарисованное минус подъём
        /// того яруса, на котором точка достигнута.</summary>
        static bool Grounded(in MountainPaintStyle style, MountainShape shape, LiftSamples profile,
                             Vec2 point, float r)
        {
            if (style.OnLand == null) return true;
            return style.OnLand(new Vec2(point.X, point.Y - shape.Height * profile.LiftAt(r)));
        }

        // ── крошка ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Зернистая штриховка ПОЛНОЙ ТЕНИ, и только её.
        ///
        /// Порог, а не вероятность (MountainInk.InShadow). Прежде метка ставилась с вероятностью,
        /// растущей от освещённости, и одиночные зёрна сеялись по всей горе — включая освещённую
        /// сторону, где на образце ДМ чистая бумага. Полутонов между «в тени» и «на свету» на
        /// образце нет, значит и в правиле их быть не должно.
        ///
        /// Меток кладём по ПЛОЩАДИ подошвы, в долях R², — мелкие и крупные горы выглядят одинаково.
        /// Каждая метка — прямоугольник: треугольник той же ширины кроет вдвое меньше и вблизи
        /// читается треугольником, а не зерном.
        /// </summary>
        static void AddGrit(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                            float y, LiftSamples profile, Vec2 light, float density, float radius)
        {
            int total = MountainInk.MarkCount(shape.FootArea, radius, density, out bool capped);
            if (capped) data.GritCapped = true;
            if (total <= 0) return;

            var normals = shape.Normal;
            int n = shape.Base.Length;
            Color32 ink = style.Ink;

            for (int id = 1; id <= total; id++)
            {
                int i = (int)(Rand(shape.Seed, 7u, id) * n);
                if (i >= n) i = n - 1;
                if (!MountainInk.InShadow(normals[i], light)) continue;

                // Ярус тянем со смещением вверх: степень и есть «крошка редеет вниз».
                float r = MountainInk.MarkR(Rand(shape.Seed, 3u, id), MountainInk.GritFall);
                if (!MountainInk.Grounded(style.OnLand, shape, i, r)) continue;
                Vec2 p = MountainTriangulation.Meridian(shape, i, r, profile);

                // Разброс вдоль кольца, чтобы метки не выстраивались по лучам.
                int j = (i + 1) % n;
                float u = Rand(shape.Seed, 211u, id);
                Vec2 q = MountainTriangulation.Meridian(shape, j, r, profile);
                p = new Vec2(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u);

                float s = MountainInk.MarkSize(radius, Rand(shape.Seed, 307u, id));
                float hw = s * 0.5f, hh = s * 0.35f;
                int start = data.Verts.Count;
                data.Verts.Add(new Vector3(p.X - hw, y, p.Y - hh));
                data.Verts.Add(new Vector3(p.X + hw, y, p.Y - hh));
                data.Verts.Add(new Vector3(p.X - hw, y, p.Y + hh));
                data.Verts.Add(new Vector3(p.X + hw, y, p.Y + hh));
                for (int k = 0; k < 4; k++) data.Colors.Add(ink);
                data.InkTris.Add(start); data.InkTris.Add(start + 2); data.InkTris.Add(start + 1);
                data.InkTris.Add(start + 1); data.InkTris.Add(start + 2); data.InkTris.Add(start + 3);
                data.GritMarks++;
            }
        }

        // ── лента следа кисти ───────────────────────────────────────────────────────────────────

        /// <summary>Лента постоянной ширины вдоль ломаной — след кисти. Нужна, чтобы показать ДМ,
        /// куда лёг мазок (шип).</summary>
        public static Mesh BuildRibbon(List<Vec2> line, float width, float yHeight, Color32 color)
        {
            var mesh = new Mesh();
            BuildRibbon(mesh, line, width, yHeight, color);
            return mesh;
        }

        public static void BuildRibbon(Mesh mesh, List<Vec2> line, float width, float yHeight, Color32 color)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (line == null || line.Count < 2 || width <= 0f) return;

            var data = new MountainMeshData();
            float half = width * 0.5f;
            for (int i = 0; i + 1 < line.Count; i++)
                AddSegment(data, line[i], line[i + 1], half, yHeight, color);
            data.Seal();
            Upload(mesh, data);
        }

        static void AddSegment(MountainMeshData data, Vec2 a, Vec2 b, float half, float y, Color32 color)
        {
            Vec2 d = b - a;
            if (d.LengthSquared() < 1e-10f) return;
            d = Vec2.Normalize(d);
            Vec2 nrm = new Vec2(-d.Y, d.X) * half;

            int start = data.Verts.Count;
            data.Verts.Add(new Vector3(a.X - nrm.X, y, a.Y - nrm.Y));
            data.Verts.Add(new Vector3(a.X + nrm.X, y, a.Y + nrm.Y));
            data.Verts.Add(new Vector3(b.X - nrm.X, y, b.Y - nrm.Y));
            data.Verts.Add(new Vector3(b.X + nrm.X, y, b.Y + nrm.Y));
            for (int k = 0; k < 4; k++) data.Colors.Add(color);
            data.InkTris.Add(start); data.InkTris.Add(start + 2); data.InkTris.Add(start + 1);
            data.InkTris.Add(start + 1); data.InkTris.Add(start + 2); data.InkTris.Add(start + 3);
        }

        // ── зерно ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Случайное число от зерна горы. Зерно взято от МЕСТА на карте, поэтому крошка не
        /// пересеивается при перерисовке и хребет не дрожит, когда ДМ ведёт соседний ползунок.</summary>
        static float Rand(uint seed, uint salt, int id)
        {
            unchecked
            {
                uint h = seed + salt * 668265263u + (uint)id * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) >> 8) / 16777216f;
            }
        }
    }
}
