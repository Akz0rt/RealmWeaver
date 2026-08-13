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

        static void Massif(StringBuilder sb, string title, float brush, Vector2[][] paint, Vector2[][] erase)
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

            var settings = new MountainSettings { Radius = 22f };
            var shapes = MountainGeometry.Build(blob, settings, out _, out var links);

            foreach (var p in paint)
            {
                sb.Append("<polyline fill='none' stroke='#ded2b8' stroke-width='").Append(F(brush * 2))
                  .Append("' stroke-linecap='round' stroke-linejoin='round' points='");
                foreach (var q in p) sb.Append(F(q.X)).Append(',').Append(F(Flip(q.Y))).Append(' ');
                sb.Append("'/>");
            }

            foreach (var shape in shapes)
            {
                // Цвет по ярусу, как в приложении: внешний слой светлый, сердцевина тёмная.
                float t = MountainTierRamp.Mix(shape.Tier, 3, 1f);
                sb.Append("<polygon fill='").Append(Tone(t)).Append("' points='");
                foreach (var p in shape.Crest) sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                for (int i = shape.Front.Count - 1; i >= 0; i--)
                    sb.Append(F(shape.Front[i].X)).Append(',').Append(F(Flip(shape.Front[i].Y))).Append(' ');
                sb.Append("'/>");

                sb.Append("<polyline fill='none' stroke='#f0ece2' stroke-opacity='0.4' stroke-width='1.2' points='");
                foreach (var p in shape.Crest) sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                sb.Append("'/>");
            }

            int free = 0;
            foreach (var link in links) if (link.FreeStart || link.FreeEnd) free++;
            Console.WriteLine($"{title}: звеньев {links.Count}, гор {shapes.Count}, свободных концов {free}");
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
                var shape = MoundBuilder.Build(outline, links[i], HeightFactor, Squash, back, fwd, R * 0.1f);
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
                foreach (var p in s.Crest) sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                for (int i = s.Front.Count - 1; i >= 0; i--)
                    sb.Append(F(s.Front[i].X)).Append(',').Append(F(Flip(s.Front[i].Y))).Append(' ');
                sb.Append("'/>");

                sb.Append("<polyline fill='none' stroke='#f4f0e6' stroke-opacity='0.45' stroke-width='1.6' points='");
                foreach (var p in s.Crest) sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                sb.Append("'/>");
            }

            int notMonotone = 0;
            foreach (var s in shapes)
            {
                for (int i = 1; i < s.Front.Count; i++)
                    if (s.Front[i].X < s.Front[i - 1].X - 1e-3f) { notMonotone++; break; }
            }
            Console.WriteLine($"{title}: звеньев {links.Count}, гор {shapes.Count}, немонотонных подошв {notMonotone}");
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
