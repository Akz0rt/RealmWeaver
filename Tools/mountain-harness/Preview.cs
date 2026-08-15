using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// Картинка вместо чекпоинта. Считает геометрию настоящим кодом слоя и пишет SVG, в котором
    /// порядок фигур — это в точности порядок подачи треугольников в меш, а оранжевая лента — след
    /// мазка. Смотреть так:
    ///     dotnet run -c Release [SEP] svg
    ///     chrome --headless --screenshot=peek.png --window-size=1200,900 file:///.../peek.svg
    /// Такой прогон ловит ошибки геометрии, не тратя чекпоинт ДМ, — а чекпоинт остаётся для того,
    /// что видно только в Unity: порядка рисования, слоёв карты и масштаба на глаз.
    /// </summary>
    static class Preview
    {
        const float R = 40f;
        const float T = 1.6f * R;
        const float Waist = 0.55f;
        const float HeightFactor = 2.2f;
        const float Stretch = 1.4f;
        const float Squash = 1f / 1.6f;
        const float CanvasH = 900f;

        public static void Write(string path)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'>");
            sb.Append("<rect width='1200' height='900' fill='#efe7d5'/>");

            Draw(sb, new List<Vector2> { new Vector2(150, 700), new Vector2(530, 700) }, false, "прямая ось");
            Draw(sb, Curved(), false, "гряда по дуге");
            Draw(sb, Ring(new Vector2(880, 560), 150f), true, "кольцо");
            Draw(sb, new List<Vector2> { new Vector2(640, 620), new Vector2(640, 860) }, false, "вертикальная ось");

            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        /// <summary>
        /// Вторая картинка — про оси, а не про горы: след мазка, принятые кольца и то, что осталось
        /// от скелета после резки покрытием. Смотреть надо ровно три вещи: не лезет ли ось за край
        /// мазка (продление концов), не идёт ли она вторым слоем поверх кольца (резка покрытием) и
        /// цела ли она в развилке (сшивка).
        /// </summary>
        public static void WriteAxes(string path)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'>");
            sb.Append("<rect width='1200' height='900' fill='#efe7d5'/>");

            Axes(sb, "тонкая масса: кольцу не хватает глубины", 33f,
                 new[] { new[] { new Vector2(80, 800), new Vector2(520, 800) } }, null);

            Axes(sb, "толстая масса: кольцо плюс сердцевина", 66f,
                 new[] { new[] { new Vector2(760, 800), new Vector2(1120, 800) } }, null);

            Axes(sb, "развилка", 30f, new[]
            {
                new[] { new Vector2(80, 480), new Vector2(330, 480) },
                new[] { new Vector2(330, 480), new Vector2(520, 590) },
                new[] { new Vector2(330, 480), new Vector2(520, 370) },
            }, null);

            Axes(sb, "кольцо", 30f, new[] { RingPts(new Vector2(900, 470), 130f) }, null);

            Axes(sb, "ластик посередине", 40f,
                 new[] { new[] { new Vector2(80, 150), new Vector2(520, 150) } },
                 new[] { new[] { new Vector2(300, 60), new Vector2(300, 240) } });

            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        /// <summary>
        /// Третья картинка — сквозная: те же мазки, что и в картинке осей, но доведённые до самих гор
        /// настоящим конвейером (MountainGeometry). Смотреть надо: сплошная ли гряда (перевалы, а не
        /// ряд кучек), не вылезла ли подошва за след мазка у свободных концов, и держится ли порядок
        /// маляра — ближняя гора обязана закрывать дальнюю.
        /// </summary>
        /// <summary>
        /// Лист вариантов внешнего вида для ДМ: одна и та же масса, нарисованная разной остротой
        /// склона и высотой. Показатель склона — единственный рычаг «остриё против взгляда сверху»:
        /// больше единицы даёт вогнутый склон и пик, единица — прямой треугольник, меньше единицы —
        /// выпуклый склон и тупую макушку.
        /// </summary>
        public static void WriteLook(string path)
        {
            float[] exps = { 1.6f, 1.0f, 0.7f, 0.45f };
            for (int i = 0; i < exps.Length; i++)
            {
                var sb = new StringBuilder();
                sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 300 420' width='300' height='420'>");
                OneMound(sb, new Vector2(150, Flip(80f)), exps[i]);
                CellLook(sb, new Vector2(150, Flip(280f)), 95f, exps[i], 2.2f);
                sb.Append("</svg>");
                string file = path.Replace(".svg", $"-{i + 1}.svg");
                File.WriteAllText(file, sb.ToString());
                Console.WriteLine($"готово: {file}");
            }
        }

        /// <summary>
        /// Лист вариантов под ДРУГОЕ устройство горы (предложение ДМ 2026-08-15): гора как стопка
        /// ярусов. Каждая карточка — одна и та же пара картинок: гора крупно (виден профиль) и гряда
        /// из клеток, уходящая ВВЕРХ по картинке с изломом, — то самое место, где ДМ находил
        /// «рыбью чешую» у нынешнего силуэта.
        /// </summary>
        public static void WriteStack(string path)
        {
            var cards = new (string Title, Schedule? Plan, float Height, int Levels, string Ink, string Look)[]
            {
                ("сейчас — остриё",              null,          2.2f, 0,  "none", "slate"),
                ("конус ×2,2, 6 ярусов",         Schedule.Cone, 2.2f, 6,  "none", "slate"),
                ("синусоида ×2,2, 6 ярусов",     Schedule.Sine, 2.2f, 6,  "none", "slate"),
                ("конус ×1,5, 6 ярусов",         Schedule.Cone, 1.5f, 6,  "none", "slate"),
                ("синусоида ×1,5, 6 ярусов",     Schedule.Sine, 1.5f, 6,  "none", "slate"),
                ("синусоида ×1,5, 3 яруса",      Schedule.Sine, 1.5f, 3,  "none", "slate"),
                ("синусоида ×1,5, 10 ярусов",    Schedule.Sine, 1.5f, 10, "none", "slate"),
                ("синусоида ×1,5 — обводка макушки", Schedule.Sine, 1.5f, 6, "top", "slate"),
                ("синусоида ×1,5 — обводка ярусов",  Schedule.Sine, 1.5f, 6, "all", "slate"),
                ("светлые макушки",              Schedule.Sine, 1.5f, 6,  "none", "invert"),
                ("цвет от карты",                Schedule.Sine, 1.5f, 6,  "none", "ground"),
            };

            for (int i = 0; i < cards.Length; i++)
            {
                var sb = new StringBuilder();
                sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 340 580' width='340' height='580'>");
                if (cards[i].Look == "ground")
                {
                    sb.Append("<rect width='150' height='580' fill='#cdbb92'/>");
                    sb.Append("<rect x='150' width='190' height='580' fill='#8d9d70'/>");
                }
                else sb.Append("<rect width='340' height='580' fill='#efe7d5'/>");
                Label(sb, 14, 30, cards[i].Title);
                StackMound(sb, new Vector2(170, Flip(150f)), cards[i].Plan, cards[i].Height,
                           cards[i].Levels, cards[i].Ink, cards[i].Look);
                StackRidge(sb, cards[i].Plan, cards[i].Height, cards[i].Levels, cards[i].Ink,
                           cards[i].Look);
                sb.Append("</svg>");
                string file = path.Replace(".svg", $"-{i + 1}.svg");
                File.WriteAllText(file, sb.ToString());
                Console.WriteLine($"готово: {file} — {cards[i].Title}");
            }
        }

        /// <summary>
        /// Второй лист: ДМ сказал «чуть острее» и «это всё-таки горы, а не холмики». Значит крутятся
        /// сразу два числа — острота (непрерывная шкала от синусоиды к шпилю) и высота, — и у всех
        /// вариантов включена настоящая вершина вместо площадки наверху.
        /// </summary>
        public static void WriteSharp(string path)
        {
            var cards = new (string Title, float Sharp, float Height, bool Apex)[]
            {
                ("прошлый лист: 0, ×1,5",       0f,    1.5f, false),
                ("острота 0,4 · высота ×2,2",   0.4f,  2.2f, true),
                ("острота 0,7 · высота ×2,2",   0.7f,  2.2f, true),
                ("острота 0,4 · высота ×2,8",   0.4f,  2.8f, true),
                ("острота 0,7 · высота ×2,8",   0.7f,  2.8f, true),
                ("острота 1,0 · высота ×2,8",   1f,    2.8f, true),
                ("острота 0,7 · высота ×3,4",   0.7f,  3.4f, true),
            };

            for (int i = 0; i < cards.Length; i++)
            {
                var sb = new StringBuilder();
                sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 340 620' width='340' height='620'>");
                sb.Append("<rect width='340' height='620' fill='#efe7d5'/>");
                Label(sb, 14, 30, cards[i].Title);

                var link = MakeLink(new Vector2(170, Flip(170f)), new Vector2(1, 0), 46f, 30f);
                var outline = LinkOutline.Build(link, 0.55f, 2f);
                if (outline != null)
                {
                    var one = Stack.Build(outline, link, cards[i].Height, 1f / 1.6f, 1.4f, 1.4f,
                                          6, Schedule.Sine, cards[i].Sharp, cards[i].Apex);
                    if (one != null) PaintStacks(sb, new List<StackShape> { one }, "none");
                }

                SharpRidge(sb, cards[i].Sharp, cards[i].Height, cards[i].Apex);
                sb.Append("</svg>");
                string file = path.Replace(".svg", $"-{i + 1}.svg");
                File.WriteAllText(file, sb.ToString());
                Console.WriteLine($"готово: {file} — {cards[i].Title}");
            }
        }

        /// <summary>
        /// Третий лист: ДМ подтвердил, что градация по СЛОЯМ массы (внешний / средний / внутренний,
        /// §11) обязана остаться. Значит цветов два источника разом — слой массы и ярус горы, — и
        /// вопрос лишь в том, сколько шкалы отдать каждому. Слой берёт себе полосу на шкале, ярус
        /// гуляет внутри этой полосы.
        ///
        /// Массив нарочно с толстой головой и тонким хвостом: в голове помещаются все три слоя, в
        /// хвосте — только внешний. Если полоса яруса шире зазора между слоями, «внутренний» хвоста
        /// сравняется с «внешним» головы, и градация перестанет читаться. Это и проверяется глазом.
        /// </summary>
        public static void WriteTiers(string path)
        {
            var cards = new (string Title, float Spread, bool ByLevelOnly)[]
            {
                ("только ярус горы (как на листе)", 0f,    true),
                ("только слой массы",               0f,    false),
                ("слой + ярус, узко",               0.22f, false),
                ("слой + ярус, шире",               0.40f, false),
            };

            for (int i = 0; i < cards.Length; i++)
            {
                var sb = new StringBuilder();
                sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 340 620' width='340' height='620'>");
                sb.Append("<rect width='340' height='620' fill='#efe7d5'/>");
                Label(sb, 14, 30, cards[i].Title);
                TierMassif(sb, cards[i].Spread, cards[i].ByLevelOnly);
                sb.Append("</svg>");
                string file = path.Replace(".svg", $"-{i + 1}.svg");
                File.WriteAllText(file, sb.ToString());
                Console.WriteLine($"готово: {file} — {cards[i].Title}");
            }
        }

        /// <summary>Массив с толстой головой (три слоя) и тонким хвостом (один).</summary>
        static void TierMassif(StringBuilder sb, float spread, bool byLevelOnly)
        {
            var polys = CellsAlongPath(new[]
            {
                new Vector2(95, 570), new Vector2(150, 480), new Vector2(175, 390),
                new Vector2(190, 320),
            }, 28f);
            polys.AddRange(CellsInDisc(new Vector2(190, 250), 62f));

            var settings = new MountainSettings { Radius = 10f, HeightFactor = 2.2f };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(10f, 10f));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            MountainGeometry.BuildFromMask(mask, settings, out var links);

            var shapes = Stack.BuildAll(links, settings, 6, Schedule.Sine, 0.5f, true);
            int maxTier = 0;
            foreach (var s in shapes) if (s.Tier > maxTier) maxTier = s.Tier;
            Console.WriteLine($"  массив: гор {shapes.Count}, слоёв {maxTier + 1}");

            foreach (var shape in shapes)
                EmitStack(sb, shape, 0f, CanvasH, "none",
                          byLevelOnly ? "slate" : "tier", spread, MountainSettings.Tiers);
        }

        static List<IReadOnlyList<Vector2>> CellsInDisc(Vector2 screenCentre, float radius)
        {
            const float step = 15f;
            var centre = new Vector2(screenCentre.X, Flip(screenCentre.Y));
            var polys = new List<IReadOnlyList<Vector2>>();
            float h = step * 0.5f;
            for (float x = (float)Math.Floor((centre.X - radius) / step) * step; x <= centre.X + radius; x += step)
                for (float y = (float)Math.Floor((centre.Y - radius) / step) * step; y <= centre.Y + radius; y += step)
                {
                    var c = new Vector2(x, y);
                    if ((c - centre).Length() > radius) continue;
                    polys.Add(new List<Vector2>
                    {
                        new Vector2(c.X - h, c.Y - h), new Vector2(c.X + h, c.Y - h),
                        new Vector2(c.X + h, c.Y + h), new Vector2(c.X - h, c.Y + h),
                    });
                }
            return polys;
        }

        static void SharpRidge(StringBuilder sb, float sharp, float height, bool apex)
        {
            var path = new[]
            {
                new Vector2(85, 540), new Vector2(125, 490), new Vector2(140, 425),
                new Vector2(205, 380), new Vector2(220, 315),
            };
            var settings = new MountainSettings { Radius = 10f, HeightFactor = height };
            var mask = MountainMask.FromPolygons(CellsAlongPath(path, 30f), MountainMask.ChooseCell(10f, 10f));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            MountainGeometry.BuildFromMask(mask, settings, out var links);
            PaintStacks(sb, Stack.BuildAll(links, settings, 6, Schedule.Sine, sharp, apex), "none");
        }

        /// <summary>
        /// Сетка профилей: расписание ярусов по столбцам, высота по строкам. Одна гора в клетке —
        /// это вопрос «какой формы гора», отдельно от вопроса «как выглядит гряда».
        /// </summary>
        public static void WriteProfiles(string path)
        {
            var plans = new (string Title, Schedule? Plan)[]
            {
                ("сейчас (силуэт)", null),
                ("шпиль",  Schedule.Peak),
                ("конус",  Schedule.Cone),
                ("купол",  Schedule.Dome),
                ("синусоида", Schedule.Sine),
            };
            float[] heights = { 2.2f, 1.5f, 1.0f };
            const int levels = 6;
            const float cellW = 240f, cellH = 210f, gutter = 92f, header = 40f;

            var sb = new StringBuilder();
            float w = gutter + cellW * plans.Length, h = header + cellH * heights.Length;
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 ").Append(F(w)).Append(' ')
              .Append(F(h)).Append("' width='").Append(F(w)).Append("' height='").Append(F(h)).Append("'>");
            sb.Append("<rect width='").Append(F(w)).Append("' height='").Append(F(h)).Append("' fill='#efe7d5'/>");

            for (int c = 0; c < plans.Length; c++)
                Label(sb, gutter + cellW * c + 16f, 26f, plans[c].Title);

            for (int r = 0; r < heights.Length; r++)
            {
                Label(sb, 14f, header + cellH * r + cellH * 0.55f, $"высота ×{heights[r]:0.0}");
                for (int c = 0; c < plans.Length; c++)
                {
                    float ox = gutter + cellW * c + cellW * 0.5f;
                    float oy = header + cellH * r + cellH * 0.86f;
                    var link = MakeLink(new Vector2(0, 0), new Vector2(1, 0), 46f, 30f);
                    var outline = LinkOutline.Build(link, 0.55f, 2f);
                    if (outline == null) continue;

                    if (plans[c].Plan == null)
                    {
                        var m = MoundBuilder.Build(outline, link, heights[r], 1f / 1.6f, 1.4f, 1.4f,
                                                   2f, Prof(1f));
                        if (m == null) continue;
                        sb.Append("<polygon fill='").Append(Tone(0.6f)).Append("' points='");
                        EmitLoop(sb, m, ox, oy, false);
                        sb.Append("'/>");
                        continue;
                    }

                    var shape = Stack.Build(outline, link, heights[r], 1f / 1.6f, 1.4f, 1.4f,
                                            levels, plans[c].Plan.Value);
                    if (shape != null) EmitStack(sb, shape, ox, oy, "none");
                }
            }

            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        /// <summary>Стопка на холст со сдвигом начала: экран = (ox + x, oy − y).</summary>
        static void EmitStack(StringBuilder sb, StackShape shape, float ox, float oy, string ink,
                              string look = "slate", float spread = 0f, int tierCount = 3)
        {
            int n = shape.Levels.Count;
            if (n == 0) return;

            // Подошва целиком — под ней ничего не просвечивает.
            Fill(sb, shape.Levels[0], Paint(look, shape, 0f, spread, tierCount), ox, oy);

            // Между соседними ярусами — ПОЛОСА, а не вертикальная стенка. Вертикальная превращала
            // гору в катушку: у каждой ступени был отвес. Полоса же соединяет край нижнего яруса с
            // краем верхнего, поэтому огибающая стопки выходит гладкой сама собой, а ярусы читаются
            // цветом. Точек у ярусов поровну — они одна и та же доля, сжатая к середине, — так что
            // полоса сшивается «застёжкой» по номеру точки.
            for (int j = 1; j < n; j++)
            {
                string fill = Paint(look, shape, n > 1 ? j / (float)(n - 1) : 0f, spread, tierCount);
                var lower = shape.Levels[j - 1];
                var upper = shape.Levels[j];
                int count = Math.Min(lower.Count, upper.Count);
                for (int k = 0; k + 1 < count; k++)
                    sb.Append("<polygon fill='").Append(fill).Append("' points='")
                      .Append(F(ox + lower[k].X)).Append(',').Append(F(oy - lower[k].Y)).Append(' ')
                      .Append(F(ox + lower[k + 1].X)).Append(',').Append(F(oy - lower[k + 1].Y)).Append(' ')
                      .Append(F(ox + upper[k + 1].X)).Append(',').Append(F(oy - upper[k + 1].Y)).Append(' ')
                      .Append(F(ox + upper[k].X)).Append(',').Append(F(oy - upper[k].Y)).Append("'/>");

                Fill(sb, upper, fill, ox, oy);

                if (ink == "all" || (ink == "top" && j == n - 1))
                {
                    sb.Append("<polygon fill='none' stroke='#f0ece2' stroke-opacity='0.5' stroke-width='1.2' points='");
                    foreach (var p in upper) sb.Append(F(ox + p.X)).Append(',').Append(F(oy - p.Y)).Append(' ');
                    sb.Append("'/>");
                }
            }
        }

        static void Fill(StringBuilder sb, List<Vector2> ring, string colour, float ox, float oy)
        {
            sb.Append("<polygon fill='").Append(colour).Append("' points='");
            foreach (var p in ring) sb.Append(F(ox + p.X)).Append(',').Append(F(oy - p.Y)).Append(' ');
            sb.Append("'/>");
        }


        /// <summary>Выборка кривой подъёма под остроту — одна на карточку.</summary>
        static LiftSamples Prof(float sharp) => new LiftSamples(sharp, MoundBuilder.ProfileSamples);

        /// <summary>Граница горы точками SVG, снизу вверх по экрану уже перевёрнутая вызывающим.</summary>
        static void EmitLoop(StringBuilder sb, MountainShape m, float ox, float oy, bool flip)
        {
            var line = new List<Vector2>();
            var radii = new List<float>();
            MountainOutline.Build(m, Prof(m.Sharp), line, radii);
            foreach (var q in line)
                sb.Append(F(ox + q.X)).Append(',').Append(F(flip ? Flip(q.Y) : oy - q.Y)).Append(' ');
        }
        /// <summary>Одна гора крупно: либо нынешний силуэт, либо стопка ярусов.</summary>
        static void StackMound(StringBuilder sb, Vector2 centre, Schedule? plan, float height,
                               int levels, string ink, string look)
        {
            var link = MakeLink(centre, new Vector2(1, 0), 46f, 30f);
            var outline = LinkOutline.Build(link, 0.55f, 2f);
            if (outline == null) return;

            if (plan == null) { OneMound(sb, centre, 1.6f); return; }

            var shape = Stack.Build(outline, link, height, 1f / 1.6f, 1.4f, 1.4f, levels, plan.Value);
            if (shape != null) PaintStacks(sb, new List<StackShape> { shape }, ink, look);
        }

        /// <summary>
        /// Гряда с изломом, уходящая вверх по картинке. Построена ПУТЁМ ПРИЛОЖЕНИЯ: клетки карты
        /// стоят через 15 единиц, из них собирается маска, дальше — настоящий конвейер.
        /// </summary>
        static void StackRidge(StringBuilder sb, Schedule? plan, float height, int levels,
                               string ink, string look)
        {
            var path = new[]
            {
                new Vector2(85, 500), new Vector2(125, 450), new Vector2(140, 385),
                new Vector2(205, 340), new Vector2(220, 275),
            };
            var polys = CellsAlongPath(path, 30f);

            var settings = new MountainSettings { Radius = 10f, HeightFactor = height };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(10f, 10f));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out var links);

            if (plan == null) { Paint(sb, shapes); return; }
            PaintStacks(sb, Stack.BuildAll(links, settings, levels, plan.Value), ink, look);
        }

        /// <summary>Клетки карты (квадраты шагом 15) вдоль ломаной, заданной В ЭКРАННЫХ координатах.</summary>
        static List<IReadOnlyList<Vector2>> CellsAlongPath(Vector2[] screenPath, float halfWidth)
        {
            const float step = 15f;
            var path = new Vector2[screenPath.Length];
            for (int i = 0; i < screenPath.Length; i++)
                path[i] = new Vector2(screenPath[i].X, Flip(screenPath[i].Y));

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in path)
            {
                minX = Math.Min(minX, p.X - halfWidth); maxX = Math.Max(maxX, p.X + halfWidth);
                minY = Math.Min(minY, p.Y - halfWidth); maxY = Math.Max(maxY, p.Y + halfWidth);
            }

            var polys = new List<IReadOnlyList<Vector2>>();
            float h = step * 0.5f;
            for (float x = (float)Math.Floor(minX / step) * step; x <= maxX; x += step)
                for (float y = (float)Math.Floor(minY / step) * step; y <= maxY; y += step)
                {
                    var c = new Vector2(x, y);
                    if (DistanceToPath(path, c) > halfWidth) continue;
                    polys.Add(new List<Vector2>
                    {
                        new Vector2(c.X - h, c.Y - h), new Vector2(c.X + h, c.Y - h),
                        new Vector2(c.X + h, c.Y + h), new Vector2(c.X - h, c.Y + h),
                    });
                }
            return polys;
        }

        static float DistanceToPath(Vector2[] path, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 a = path[i - 1], b = path[i], d = b - a;
                float len2 = d.LengthSquared();
                float t = len2 < 1e-8f ? 0f : Math.Min(1f, Math.Max(0f, Vector2.Dot(p - a, d) / len2));
                best = Math.Min(best, (p - (a + d * t)).Length());
            }
            return best;
        }

        /// <summary>
        /// Стопки на холст. Внутри горы ярусы идут снизу вверх, поэтому верхний закрывает нижний;
        /// сами горы уже отсортированы порядком маляра. Цвет задаёт ЯРУС ПО ВЫСОТЕ: у соседей он в
        /// точности одинаков, и подошвы сливаются в одну ленту вдоль всей гряды.
        /// </summary>
        static void PaintStacks(StringBuilder sb, List<StackShape> shapes, string ink, string look = "slate")
        {
            foreach (var shape in shapes) EmitStack(sb, shape, 0f, CanvasH, ink, look);
        }

        /// <summary>
        /// Краска яруса. slate — сланцевая шкала, светлый низ и тёмный верх. invert —
        /// наоборот, макушки светлые. ground — та самая проба «горы растут из карты»: нижний
        /// ярус берёт цвет земли под горой и лишь притемняет его, верхний уходит в камень.
        /// Притемнение обязательно: ровно цвет карты стирает внешнюю границу массы,
        /// и хребет перестаёт читаться объектом.
        /// </summary>
        static string Paint(string look, StackShape shape, float t, float spread, int tierCount)
        {
            if (look == "invert") return Tone(1f - t);

            // «tier»: главный источник цвета — СЛОЙ массы (§11, внешний / средний / внутренний), а
            // ярус горы лишь гуляет внутри отведённой слою полосы. Полоса шириной spread: 0 — цвет
            // задаёт один слой и гора заливается ровно, больше нуля — внутри слоя ещё видно объём.
            // Ширину надо держать меньше зазора между слоями, иначе внутренний ярус внешнего слоя
            // сравняется с внешним ярусом среднего, и градация по слоям перестанет читаться.
            if (look == "tier")
            {
                float band = MountainTierRamp.Mix(shape.Tier, tierCount, 1f);
                float mix = Math.Min(1f, Math.Max(0f, band + spread * (t - 0.5f)));
                return Tone(mix);
            }

            if (look != "ground") return Tone(t);

            // Карточка из двух биомов: слева песок, справа лес. Настоящая проба спросит
            // цвет у карты (WorldMapRenderer.GetColorForCell), здесь он подделан по X.
            float[] ground = shape.Centre.X < 150f ? new float[] { 205, 187, 146 } : new float[] { 141, 157, 112 };
            float[] rock = { 43, 53, 64 };
            const float k = 0.78f;   // нижний ярус — земля, притемнённая на пятую часть
            int r = (int)Math.Round(ground[0] * k + (rock[0] - ground[0] * k) * t);
            int g = (int)Math.Round(ground[1] * k + (rock[1] - ground[1] * k) * t);
            int b = (int)Math.Round(ground[2] * k + (rock[2] - ground[2] * k) * t);
            return "rgb(" + r + "," + g + "," + b + ")";
        }

        static void Label(StringBuilder sb, float x, float y, string text)
            => sb.Append("<text x='").Append(F(x)).Append("' y='").Append(F(y))
                 .Append("' font-family='sans-serif' font-size='15' fill='#5a5348'>").Append(text).Append("</text>");

        /// <summary>Одна гора крупно — чтобы силуэт был виден без соседей.</summary>
        static void OneMound(StringBuilder sb, Vector2 centre, float sharp)
        {
            var link = MakeLink(centre, new Vector2(1, 0), 46f, 30f);
            var outline = LinkOutline.Build(link, 0.55f, 2f);
            var m = MoundBuilder.Build(outline, link, 2.2f, 1f / 1.6f, 1.4f, 1.4f, 2f, Prof(sharp));
            if (m == null) return;
            sb.Append("<polygon fill='#48626d' points='");
            EmitLoop(sb, m, 0f, 0f, true);
            sb.Append("'/>");
            sb.Append("<polygon fill='none' stroke='#f0ece2' stroke-opacity='0.55' stroke-width='2' points='");
            EmitLoop(sb, m, 0f, 0f, true);
            sb.Append("'/>");
        }

        /// <summary>Масса, построенная путём ПРИЛОЖЕНИЯ — маска из многоугольников клеток.</summary>
        static void CellLook(StringBuilder sb, Vector2 centre, float radius, float exponent, float height)
        {
            const float step = 15f;
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = centre.X - radius; x <= centre.X + radius; x += step)
                for (float y = centre.Y - radius; y <= centre.Y + radius; y += step)
                {
                    var c = new Vector2(x, y);
                    if ((c - centre).Length() > radius) continue;
                    float h = step * 0.5f;
                    polys.Add(new List<Vector2>
                    {
                        new Vector2(c.X - h, c.Y - h), new Vector2(c.X + h, c.Y - h),
                        new Vector2(c.X + h, c.Y + h), new Vector2(c.X - h, c.Y + h),
                    });
                }

            var settings = new MountainSettings { Radius = 10f, Sharp = exponent, HeightFactor = height };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(10f, 10f));
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            Paint(sb, MountainGeometry.BuildFromMask(mask, settings, out _));
        }

        static AxisLink MakeLink(Vector2 mid, Vector2 tan, float len, float w)
        {
            var link = new AxisLink { Mid = mid, Tan = tan, MidW = w };
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f - 0.5f;
                link.Pts.Add(mid + tan * (len * t));
                link.Ws.Add(w);
            }
            return link;
        }

        // ВРЕМЕННО: путь ПРИЛОЖЕНИЯ — маска из многоугольников клеток + сглаживание.
        public static void WriteCells(string path)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'>");
            sb.Append("<rect width='1200' height='900' fill='#efe7d5'/>");
            CellMassif(sb, new Vector2(300, 250), 120f, 0.5f);
            CellMassif(sb, new Vector2(850, 250), 120f, 0f);
            CellMassif(sb, new Vector2(300, 660), 60f, 0.5f);
            CellMassif(sb, new Vector2(850, 660), 60f, 0f);
            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        /// <summary>Клетки карты стоят через 15 единиц — берём квадраты того же шага, центры внутри круга.</summary>
        static void CellMassif(StringBuilder sb, Vector2 centre, float radius, float smoothing)
        {
            const float step = 15f;
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = centre.X - radius; x <= centre.X + radius; x += step)
                for (float y = centre.Y - radius; y <= centre.Y + radius; y += step)
                {
                    var c = new Vector2(x, y);
                    if ((c - centre).Length() > radius) continue;
                    float h = step * 0.5f;
                    polys.Add(new List<Vector2>
                    {
                        new Vector2(c.X - h, c.Y - h), new Vector2(c.X + h, c.Y - h),
                        new Vector2(c.X + h, c.Y + h), new Vector2(c.X - h, c.Y + h),
                    });
                }

            var settings = new MountainSettings { Radius = 10f };
            float cell = MountainMask.ChooseCell(settings.Radius, settings.Radius);
            var mask = MountainMask.FromPolygons(polys, cell);
            if (smoothing > 0f) mask.Smooth((int)Math.Round(smoothing * settings.Radius / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out var links);
            Console.WriteLine($"клетки r={radius} сглаж={smoothing}: гор {shapes.Count}");
            Paint(sb, shapes);
        }

        public static void WriteAppLike(string path)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'>");
            sb.Append("<rect width='1200' height='900' fill='#efe7d5'/>");
            Massif(sb, "поперёк", 42f, new[] { new[] { new Vector2(80, 780), new Vector2(520, 780) } }, null, 10f);
            Massif(sb, "вдоль", 42f, new[] { new[] { new Vector2(760, 560), new Vector2(760, 860) } }, null, 10f);
            Massif(sb, "наискось", 42f, new[] { new[] { new Vector2(100, 200), new Vector2(480, 520) } }, null, 10f);
            Massif(sb, "клякса", 90f, new[] { new[] { new Vector2(900, 300), new Vector2(950, 330) } }, null, 10f);
            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        public static void WriteMassif(string path)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'>");
            sb.Append("<rect width='1200' height='900' fill='#efe7d5'/>");

            Massif(sb, "тонкая масса", 33f,
                   new[] { new[] { new Vector2(80, 760), new Vector2(520, 760) } }, null);
            Massif(sb, "толстая масса", 66f,
                   new[] { new[] { new Vector2(760, 740), new Vector2(1120, 740) } }, null);
            Massif(sb, "развилка", 30f, new[]
            {
                new[] { new Vector2(80, 440), new Vector2(330, 440) },
                new[] { new Vector2(330, 440), new Vector2(520, 550) },
                new[] { new Vector2(330, 440), new Vector2(520, 330) },
            }, null);
            Massif(sb, "кольцо", 30f, new[] { RingPts(new Vector2(900, 430), 130f) }, null);
            Massif(sb, "ластик посередине", 40f,
                   new[] { new[] { new Vector2(80, 120), new Vector2(520, 120) } },
                   new[] { new[] { new Vector2(300, 30), new Vector2(300, 210) } });

            sb.Append("</svg>");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        static void Massif(StringBuilder sb, string title, float brush, Vector2[][] paint, Vector2[][] erase, float R = 22f)
        {
            var blob = new MountainBlob();
            int id = 1;
            foreach (var p in paint)
            {
                var s = new MountainStroke { Id = id++, Radius = brush };
                s.Points.AddRange(p);
                blob.Strokes.Add(s);
            }
            if (erase != null)
                foreach (var e in erase)
                {
                    var s = new MountainStroke { Id = id++, Radius = brush, Erase = true };
                    s.Points.AddRange(e);
                    blob.Erasers.Add(s);
                }

            var settings = new MountainSettings { Radius = R };
            var shapes = MountainGeometry.Build(blob, settings, out _, out var links);

            foreach (var p in paint)
            {
                sb.Append("<polyline fill='none' stroke='#ded2b8' stroke-width='").Append(F(brush * 2))
                  .Append("' stroke-linecap='round' stroke-linejoin='round' points='");
                foreach (var q in p) sb.Append(F(q.X)).Append(',').Append(F(Flip(q.Y))).Append(' ');
                sb.Append("'/>");
            }

            Paint(sb, shapes);

            int free = 0;
            foreach (var link in links) if (link.FreeStart || link.FreeEnd) free++;
            Console.WriteLine($"{title}: звеньев {links.Count}, гор {shapes.Count}, свободных концов {free}");
        }

        static void Paint(StringBuilder sb, List<MountainShape> shapes)
        {
            foreach (var shape in shapes)
            {
                float t = MountainTierRamp.Mix(shape.Tier, 3, 1f);
                sb.Append("<polygon fill='").Append(Tone(t)).Append("' points='");
                EmitLoop(sb, shape, 0f, 0f, true);
                sb.Append("'/>");
                sb.Append("<polygon fill='none' stroke='#f0ece2' stroke-opacity='0.4' stroke-width='1.2' points='");
                EmitLoop(sb, shape, 0f, 0f, true);
                sb.Append("'/>");
            }
        }

        /// <summary>Краска по доле шкалы: 0 — светлый конец, 1 — тёмный.</summary>
        static string Tone(float t)
        {
            int r = (int)Math.Round(77 + (28 - 77) * t);
            int g = (int)Math.Round(107 + (44 - 107) * t);
            int b = (int)Math.Round(118 + (52 - 118) * t);
            return $"rgb({r},{g},{b})";
        }

        static Vector2[] RingPts(Vector2 c, float radius)
        {
            var pts = new Vector2[73];
            for (int i = 0; i <= 72; i++)
            {
                double a = i / 72.0 * Math.PI * 2;
                pts[i] = new Vector2(c.X + (float)Math.Cos(a) * radius, c.Y + (float)Math.Sin(a) * radius);
            }
            return pts;
        }

        static void Axes(StringBuilder sb, string title, float brush, Vector2[][] paint, Vector2[][] erase)
        {
            const float MountainR = 22f;

            var blob = new MountainBlob();
            int id = 1;
            foreach (var p in paint)
            {
                var s = new MountainStroke { Id = id++, Radius = brush };
                s.Points.AddRange(p);
                blob.Strokes.Add(s);
            }
            if (erase != null)
                foreach (var e in erase)
                {
                    var s = new MountainStroke { Id = id++, Radius = brush, Erase = true };
                    s.Points.AddRange(e);
                    blob.Erasers.Add(s);
                }

            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(MountainR, brush));
            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, MountainR / mask.Cell);

            foreach (var p in paint)
            {
                sb.Append("<polyline fill='none' stroke='#c9bda2' stroke-width='").Append(F(brush * 2))
                  .Append("' stroke-linecap='round' stroke-linejoin='round' points='");
                foreach (var q in p) sb.Append(F(q.X)).Append(',').Append(F(Flip(q.Y))).Append(' ');
                sb.Append("'/>");
            }
            if (erase != null)
                foreach (var e in erase)
                {
                    sb.Append("<polyline fill='none' stroke='#efe7d5' stroke-width='").Append(F(brush * 2))
                      .Append("' stroke-linecap='round' stroke-linejoin='round' points='");
                    foreach (var q in e) sb.Append(F(q.X)).Append(',').Append(F(Flip(q.Y))).Append(' ');
                    sb.Append("'/>");
                }

            int rings = 0, skel = 0;
            foreach (var axis in axes)
            {
                if (axis.FromRing) rings++; else skel++;
                string colour = axis.FromRing ? "#2c6fbb" : "#a3282f";
                sb.Append("<polyline fill='none' stroke='").Append(colour)
                  .Append("' stroke-width='2.2' points='");
                foreach (var g in axis.Points)
                {
                    var world = mask.GridToWorld(g.X, g.Y);
                    sb.Append(F(world.X)).Append(',').Append(F(Flip(world.Y))).Append(' ');
                }
                if (axis.Closed)
                {
                    var first = mask.GridToWorld(axis.Points[0].X, axis.Points[0].Y);
                    sb.Append(F(first.X)).Append(',').Append(F(Flip(first.Y)));
                }
                sb.Append("'/>");

                // Концы осей помечены: по ним видно, докуда дотянулось продление.
                if (!axis.Closed)
                    foreach (int i in new[] { 0, axis.Points.Count - 1 })
                    {
                        var world = mask.GridToWorld(axis.Points[i].X, axis.Points[i].Y);
                        sb.Append("<circle r='3.5' fill='").Append(colour).Append("' cx='")
                          .Append(F(world.X)).Append("' cy='").Append(F(Flip(world.Y))).Append("'/>");
                    }
            }
            var tails = new StringBuilder();
            foreach (var axis in axes)
            {
                if (axis.Closed || axis.FromRing) continue;
                tails.Append($"; концы D = {axis.Depths[0]:0.0} и {axis.Depths[axis.Depths.Length - 1]:0.0}");
            }
            Console.WriteLine($"{title}: колец {rings}, осей от скелета {skel}, сетка {mask.W}×{mask.H}{tails}");
        }

        static List<Vector2> Curved()
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                pts.Add(new Vector2(150 + 700 * t, 260 + (float)Math.Sin(t * Math.PI * 1.3) * 120f));
            }
            return pts;
        }

        static List<Vector2> Ring(Vector2 c, float radius)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 72; i++)
            {
                double a = i / 72.0 * Math.PI * 2;
                pts.Add(new Vector2(c.X + (float)Math.Cos(a) * radius, c.Y + (float)Math.Sin(a) * radius));
            }
            return pts;
        }

        static void Draw(StringBuilder sb, List<Vector2> axis, bool closed, string title)
        {
            var links = Split(axis, T, R);
            var shapes = new List<MountainShape>();
            for (int i = 0; i < links.Count; i++)
            {
                var outline = LinkOutline.Build(links[i], Waist, R / 15f);
                if (outline == null) continue;
                float back = (!closed && i == 0) ? 1f : Stretch;
                float fwd = (!closed && i == links.Count - 1) ? 1f : Stretch;
                var shape = MoundBuilder.Build(outline, links[i], HeightFactor, Squash, back, fwd,
                                               R * 0.1f, Prof(0.66f));
                if (shape != null) shapes.Add(shape);
            }

            // Маляр: дальняя гора — та, чья ближайшая точка ВЫШЕ по экрану, то есть с БОЛЬШИМ Y.
            shapes.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            float dMin = float.MaxValue, dMax = float.MinValue;
            foreach (var s in shapes) { dMin = Math.Min(dMin, s.Depth); dMax = Math.Max(dMax, s.Depth); }
            float span = Math.Max(1f, dMax - dMin);

            sb.Append("<polyline fill='none' stroke='#d2691e' stroke-opacity='0.35' stroke-width='")
              .Append(F(R * 2)).Append("' stroke-linecap='round' stroke-linejoin='round' points='");
            foreach (var p in axis) sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
            sb.Append("'/>");

            foreach (var s in shapes)
            {
                float t = (dMax - s.Depth) / span;
                int g = (int)(150 - 90 * t);
                sb.Append("<polygon fill='rgb(").Append(g - 20).Append(',').Append(g).Append(',').Append((int)(165 - 90 * t)).Append(")' points='");
                EmitLoop(sb, s, 0f, 0f, true);
                sb.Append("'/>");

                sb.Append("<polygon fill='none' stroke='#f4f0e6' stroke-opacity='0.45' stroke-width='1.6' points='");
                EmitLoop(sb, s, 0f, 0f, true);
                sb.Append("'/>");
            }

            Console.WriteLine($"{title}: звеньев {links.Count}, гор {shapes.Count}");
        }

        // Site-координаты: +Y вверх по экрану. SVG: +Y вниз.
        static float Flip(float y) => CanvasH - y;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>ВРЕМЕННАЯ резка на звенья — равными кусками, полуширина постоянная. Настоящая
        /// (§8: анизотропная метрика, разброс, замкнутые оси) придёт в задаче 4 и заменит эту.</summary>
        static List<AxisLink> Split(List<Vector2> axis, float linkLength, float halfWidth)
        {
            var result = new List<AxisLink>();
            if (axis.Count < 2) return result;

            float total = 0f;
            for (int i = 1; i < axis.Count; i++) total += (axis[i] - axis[i - 1]).Length();
            int count = Math.Max(1, (int)Math.Round(total / linkLength));
            float step = total / count;

            var current = new List<Vector2> { axis[0] };
            float taken = 0f;
            int index = 1;
            Vector2 cursor = axis[0];

            while (index < axis.Count)
            {
                Vector2 next = axis[index];
                float segment = (next - cursor).Length();
                if (segment <= 1e-6f) { index++; cursor = next; continue; }

                if (taken + segment < step - 1e-6f)
                {
                    taken += segment;
                    cursor = next;
                    current.Add(cursor);
                    index++;
                    continue;
                }

                float need = step - taken;
                Vector2 cut = cursor + (next - cursor) * (need / segment);
                current.Add(cut);
                result.Add(MakeLink(current, halfWidth, result.Count));
                current = new List<Vector2> { cut };
                cursor = cut;
                taken = 0f;
            }

            if (current.Count >= 2) result.Add(MakeLink(current, halfWidth, result.Count));
            return result;
        }

        static AxisLink MakeLink(List<Vector2> pts, float halfWidth, int index)
        {
            var link = new AxisLink { Pts = new List<Vector2>(pts) };
            for (int i = 0; i < pts.Count; i++) link.Ws.Add(halfWidth);
            Vector2 a = pts[0], b = pts[pts.Count - 1];
            link.Mid = (a + b) * 0.5f;
            Vector2 dir = b - a;
            link.Tan = dir.LengthSquared() < 1e-8f ? new Vector2(1, 0) : Vector2.Normalize(dir);
            link.MidW = halfWidth;
            uint h = (uint)(index * 2654435761u);
            h ^= h >> 15;
            link.HeightJitter = 0.82f + (h % 1000) / 1000f * 0.36f;
            return link;
        }
    }
}
