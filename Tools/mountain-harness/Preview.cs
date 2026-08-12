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
