using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation.Mountains;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Собирает все горы массива в ОДИН меш, в котором треугольники уже уложены в порядке маляра:
    /// дальние вперёд, ближние следом. Ни глубина, ни сортировка Unity в этом не участвуют — их
    /// заменяет порядок подачи (см. MountainPaint.shader: ZWrite Off, ZTest Always).
    ///
    /// Порядок внутри одной горы: тело → крошка → промоины → линия. Тело кладётся первым, потому что
    /// оно обязано закрыть тушь ДАЛЬНЕЙ горы; линия последней, потому что она обязана остаться
    /// поверх собственной крошки.
    ///
    /// Собирается В ФОНЕ (см. MountainMeshData). На главном потоке остаётся только заливка в Mesh.
    /// </summary>
    public static class MountainMeshBuilder
    {
        /// <summary>Отсчётов вдоль промоины.</summary>
        const int GullySteps = 4;

        /// <summary>Отсчётов вдоль склона на скачке границы.</summary>
        const int SlopeSteps = 6;

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

            Vec2 light = style.Light;
            // Буферы раскладки заводятся ОДИН раз на весь массив, а не на гору: гор сотни, и
            // отдельный список на каждую — это сотни выбросов в мусор за один пересчёт.
            var scratchVerts = new List<Vec2>();
            var scratchTris = new List<int>();

            foreach (var shape in shapes)
            {
                if (shape?.Silhouette == null || shape.Silhouette.Length < 3) continue;
                if (shape.LevelR == null || shape.LevelR.Length < 2) continue;
                data.Mountains++;

                float density = style.Density(shape.Tier);
                AddBody(data, shape, style, profile, scratchVerts, scratchTris);
                AddGrit(data, shape, style, profile, light, density, radius);
                AddGullies(data, shape, style, profile, light);
                AddOutline(data, shape, style, profile, density);
            }
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
        /// (MountainTriangulation.Fill), здесь остаётся перевести точки в мировые и спросить цвет.
        ///
        /// Цвет спрашивается В КАЖДОЙ ВЕРШИНЕ, а не один раз на гору: гора на границе биомов иначе
        /// красится бледной заплатой поперёк границы — это было видно на первых же снимках
        /// браузерного превью. У края шва нет ни при каком раскладе, потому что там гора кроет
        /// ровно тот цвет, что кроет.
        /// </summary>
        static void AddBody(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                            LiftSamples profile, List<Vec2> scratchVerts, List<int> scratchTris)
        {
            MountainTriangulation.Fill(shape, profile, scratchVerts, scratchTris);
            if (scratchTris.Count == 0) return;

            float y = style.LayerY;
            int start = data.Verts.Count;
            for (int i = 0; i < scratchVerts.Count; i++)
            {
                Vec2 p = scratchVerts[i];
                data.Verts.Add(new Vector3(p.X, y, p.Y));
                data.Colors.Add(GroundAt(style, p));
            }
            for (int i = 0; i < scratchTris.Count; i++) data.Tris.Add(start + scratchTris[i]);
        }

        /// <summary>
        /// Цвет земли под точкой — и обязательно НЕПРОЗРАЧНЫЙ.
        ///
        /// Цвет клетки приходит с непрозрачностью биома, а она бывает меньше единицы; шейдер слоя
        /// смешивает по альфе, и тело горы вышло полупрозрачным — сквозь гору просвечивали клетки,
        /// деревья и соседние горы. Гора обязана карту ЗАКРЫВАТЬ: на этом держится и порядок маляра
        /// (ближняя закрывает дальнюю), и весь замысел «тело — окно в карту», где окно показывает
        /// карту, а не смешивается с ней.
        /// </summary>
        static Color32 GroundAt(in MountainPaintStyle style, Vec2 p)
        {
            Color32 c = style.Ground != null ? style.Ground(p) : style.FallbackBody;
            c.a = 255;
            return c;
        }

        // ── линия ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Линия туши: короткие штрихи в точках внешней границы.
        ///
        /// Почему штрихи, а не лента вдоль границы. Точка границы на луче — это тот ярус, который на
        /// этом луче торчит дальше всех. У КОНУСА подъём линеен, и максимум всегда сидит на конце:
        /// либо на подошве, либо на вершине. У соседних лучей он скачет между ними, и ломаная,
        /// проведённая по этим точкам подряд, выходит клубком — стенд насчитал 68 самопересечений
        /// из 115 на маске приложения. Штрихи от порядка обхода не зависят вовсе: каждый лежит по
        /// касательной к СВОЕМУ ярусу и перекрывается с соседями, а вместе они читаются линией.
        ///
        /// Толщина — жирность·(1 − r)^сход. У ближнего края r = 1, толщина ноль: подошва не
        /// обводится сама собой, и контур обрывается — ровно как на образце ДМ. Ни одной проверки
        /// «а здесь не обводить» в коде нет и не нужно.
        /// </summary>
        static void AddOutline(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                               LiftSamples profile, float density)
        {
            if (style.PenWidth <= 0f || style.PenAlpha <= 0f) return;

            var loop = shape.Silhouette;
            var rs = shape.SilhouetteR;
            float y = style.LayerY;
            Color32 ink = style.InkAt(shape.Tier, shape.Depth);

            for (int i = 0; i < loop.Length; i++)
            {
                float half = 0.5f * style.PenWidth * MountainInk.Taper(rs[i], style.PenTaper) * density;
                if (half < 1e-3f) continue;

                MountainTriangulation.Dash(shape, i, profile, out Vec2 dir, out float reach);
                if (reach <= 0f) continue;

                Vec2 side = new Vec2(-dir.Y, dir.X) * half;
                Vec2 along = dir * reach;
                Vec2 p = loop[i];

                int start = data.Verts.Count;
                data.Verts.Add(new Vector3(p.X - along.X - side.X, y, p.Y - along.Y - side.Y));
                data.Verts.Add(new Vector3(p.X - along.X + side.X, y, p.Y - along.Y + side.Y));
                data.Verts.Add(new Vector3(p.X + along.X - side.X, y, p.Y + along.Y - side.Y));
                data.Verts.Add(new Vector3(p.X + along.X + side.X, y, p.Y + along.Y + side.Y));
                for (int k = 0; k < 4; k++) data.Colors.Add(ink);
                data.Tris.Add(start); data.Tris.Add(start + 2); data.Tris.Add(start + 1);
                data.Tris.Add(start + 1); data.Tris.Add(start + 2); data.Tris.Add(start + 3);
            }

            AddSlopes(data, shape, style, profile, density, ink);
        }

        /// <summary>
        /// Обводка СКЛОНА — там, где граница скачет между соседними лучами.
        ///
        /// У конуса максимум выноса всегда на конце (подъём линеен), поэтому у одного луча граница
        /// сидит на подошве, у соседнего — уже на вершине, а между ними проходит склон, на котором
        /// точек границы нет вовсе. Без этого вершины обведены, а бока голые — так и вышло на
        /// первом снимке переноса. Ведём линию по меридиану того луча, на котором скачок, с той же
        /// толщиной по (1 − r)^сход: у подошвы ноль, у вершины полная.
        ///
        /// У купола скачков нет — цикл не делает ничего, и правило само выключается.
        /// </summary>
        static void AddSlopes(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                              LiftSamples profile, float density, Color32 ink)
        {
            int n = shape.SilhouetteR.Length;
            for (int i = 0; i < n; i++)
            {
                if (MountainTriangulation.Jump(shape, i) < MountainTriangulation.JumpThreshold) continue;

                int j = (i + 1) % n;
                float ra = shape.SilhouetteR[i], rb = shape.SilhouetteR[j];
                float hi = Math.Max(ra, rb), lo = Math.Min(ra, rb);
                int edge = ra > rb ? i : j;          // ведём по лучу, с которого граница уходит вверх

                Vec2 prev = MountainTriangulation.Meridian(shape, edge, hi, profile);
                float prevHalf = 0.5f * style.PenWidth * MountainInk.Taper(hi, style.PenTaper) * density;
                for (int step = 1; step <= SlopeSteps; step++)
                {
                    float r = hi + (lo - hi) * step / SlopeSteps;
                    Vec2 next = MountainTriangulation.Meridian(shape, edge, r, profile);
                    float half = 0.5f * style.PenWidth * MountainInk.Taper(r, style.PenTaper) * density;
                    AddTaperedSegment(data, prev, next, prevHalf, half, style.LayerY, ink);
                    prev = next;
                    prevHalf = half;
                }
            }
        }

        /// <summary>Отрезок ленты с разной толщиной на концах — линия обязана худеть к подошве.</summary>
        static void AddTaperedSegment(MountainMeshData data, Vec2 a, Vec2 b, float halfA, float halfB,
                                      float y, Color32 color)
        {
            Vec2 d = b - a;
            if (d.LengthSquared() < 1e-10f) return;
            d = Vec2.Normalize(d);
            Vec2 nrm = new Vec2(-d.Y, d.X);

            int start = data.Verts.Count;
            data.Verts.Add(new Vector3(a.X - nrm.X * halfA, y, a.Y - nrm.Y * halfA));
            data.Verts.Add(new Vector3(a.X + nrm.X * halfA, y, a.Y + nrm.Y * halfA));
            data.Verts.Add(new Vector3(b.X - nrm.X * halfB, y, b.Y - nrm.Y * halfB));
            data.Verts.Add(new Vector3(b.X + nrm.X * halfB, y, b.Y + nrm.Y * halfB));
            for (int k = 0; k < 4; k++) data.Colors.Add(color);
            data.Tris.Add(start); data.Tris.Add(start + 2); data.Tris.Add(start + 1);
            data.Tris.Add(start + 1); data.Tris.Add(start + 2); data.Tris.Add(start + 3);
        }

        static Vec2 Normal(Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            if (d.LengthSquared() < 1e-10f) return new Vec2(0f, 1f);
            d = Vec2.Normalize(d);
            return new Vec2(-d.Y, d.X);
        }

        // ── крошка ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Зернистая штриховка теневой стороны: густо у гребня, редеет вниз по склону.
        ///
        /// Меток кладём по ПЛОЩАДИ подошвы, а не по числу ярусов (которых тут и нет вовсе) и не по
        /// горе. В браузере это было ошибкой, найденной на ходу: плотность цеплялась за ползунок
        /// «ярусов», и, подняв его ради гладкого силуэта, ДМ разом затемнил бы всю картину, не поняв,
        /// отчего. Площадь мерится в R², поэтому мелкие и крупные горы выглядят одинаково.
        ///
        /// Каждая метка — ТРЕУГОЛЬНИК, а не четырёхугольник: на своём размере он читается той же
        /// точкой, а вершин и индексов стоит вдвое меньше. Крошка — самая многочисленная часть
        /// рисунка, и на ней эта разница решает.
        /// </summary>
        static void AddGrit(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                            LiftSamples profile, Vec2 light, float density, float radius)
        {
            if (style.PenAlpha <= 0f) return;

            int total = MountainInk.MarkCount(shape.FootArea, radius, style.Grit, density, out bool capped);
            if (capped) data.GritCapped = true;
            if (total <= 0) return;

            var foot = shape.Base;
            var normals = shape.Normal;
            int n = foot.Length;
            float y = style.LayerY;
            Color32 ink = style.InkAt(shape.Tier, shape.Depth);

            for (int id = 1; id <= total; id++)
            {
                int i = (int)(Rand(shape.Seed, 7u, id) * n);
                if (i >= n) i = n - 1;

                float shade = MountainInk.Shade(normals[i], light);
                if (shade <= 0.05f) continue;
                if (Rand(shape.Seed, 11u, id) > Math.Min(1f, shade * 1.6f)) continue;

                // Ярус тянем со смещением вверх: степень и есть «крошка редеет вниз».
                float r = MountainInk.MarkR(Rand(shape.Seed, 3u, id), style.GritFall);
                Vec2 p = MountainTriangulation.Meridian(shape, i, r, profile);

                // Разброс вдоль кольца, чтобы метки не выстраивались по лучам.
                int j = (i + 1) % n;
                float u = Rand(shape.Seed, 211u, id);
                Vec2 q = MountainTriangulation.Meridian(shape, j, r, profile);
                p = new Vec2(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u);

                // Прямоугольник, а не треугольник: вблизи треугольник читается треугольником, а
                // не зерном, и кроет вдвое меньше площади при той же ширине.
                float s = MountainInk.MarkSize(radius, Rand(shape.Seed, 307u, id));
                float hw = s * 0.5f, hh = s * 0.35f;
                int start = data.Verts.Count;
                data.Verts.Add(new Vector3(p.X - hw, y, p.Y - hh));
                data.Verts.Add(new Vector3(p.X + hw, y, p.Y - hh));
                data.Verts.Add(new Vector3(p.X - hw, y, p.Y + hh));
                data.Verts.Add(new Vector3(p.X + hw, y, p.Y + hh));
                for (int k = 0; k < 4; k++) data.Colors.Add(ink);
                data.Tris.Add(start); data.Tris.Add(start + 2); data.Tris.Add(start + 1);
                data.Tris.Add(start + 1); data.Tris.Add(start + 2); data.Tris.Add(start + 3);
                data.GritMarks++;
            }
        }

        // ── промоины ────────────────────────────────────────────────────────────────────────────

        /// <summary>Редкие тонкие штрихи по освещённому склону. Идут по меридиану — по одному и тому
        /// же лучу через несколько значений r, — поэтому сами ложатся вдоль ската.</summary>
        static void AddGullies(MountainMeshData data, MountainShape shape, in MountainPaintStyle style,
                               LiftSamples profile, Vec2 light)
        {
            if (style.Gully <= 0f || style.PenAlpha <= 0f || style.PenWidth <= 0f) return;

            var normals = shape.Normal;
            int n = normals.Length;
            float y = style.LayerY;
            Color32 ink = style.InkAt(shape.Tier, shape.Depth, 0.6f);
            float half = 0.5f * Math.Max(0.02f, style.PenWidth * 0.16f);

            for (int i = 0; i < n; i++)
            {
                if (normals[i].X * light.X + normals[i].Y * light.Y <= 0.25f) continue;
                if (Rand(shape.Seed, 613u, i) > style.Gully * 0.3f) continue;

                MountainInk.Gully(Rand(shape.Seed, 71u, i), Rand(shape.Seed, 89u, i),
                                  out float top, out float length);
                float bottom = Math.Min(0.95f, top + length);

                Vec2 prev = MountainTriangulation.Meridian(shape, i, top, profile);
                for (int step = 1; step <= GullySteps; step++)
                {
                    float r = top + (bottom - top) * step / GullySteps;
                    Vec2 next = MountainTriangulation.Meridian(shape, i, r, profile);
                    AddSegment(data, prev, next, half, y, ink);
                    prev = next;
                }
            }
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
            data.Tris.Add(start); data.Tris.Add(start + 2); data.Tris.Add(start + 1);
            data.Tris.Add(start + 1); data.Tris.Add(start + 2); data.Tris.Add(start + 3);
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
            Upload(mesh, data);
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
