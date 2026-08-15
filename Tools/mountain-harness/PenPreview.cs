using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// Рисует горы ТЕМИ ЖЕ правилами, что и меш приложения: тело ярусами (окно в карту), один
    /// непрерывный контур, крошка в полной тени. Слой Rendering стенду не виден, но все решения, в
    /// которых можно ошибиться, вынесены в чистый слой (MountainInk, MountainOutline) — значит
    /// картинку можно собрать и здесь, и она будет той же.
    ///
    /// Нужна ровно затем, чтобы посмотреть глазами до того, как ДМ откроет Unity. Компилятор
    /// говорит, что код собирается; проверки говорят, что числа сходятся; а похоже ли это на
    /// нарисованную гору — не скажет ни тот, ни другой.
    ///
    /// ПОРЯДОК РИСОВАНИЯ здесь — не «все тела, потом вся тушь», а по горе: тело, крошка, контур,
    /// следующая гора. Это и есть то, что в приложении делает глубина: тело ближней горы закрывает
    /// тушь дальней. Горы приходят уже отсортированными (MountainGeometry.SortForPainting).
    /// </summary>
    static class PenPreview
    {
        const float W = 760f, H = 760f;

        /// <summary>
        /// zoom — сколько пикселей приходится на мировую единицу. Ради него всё и написано заново:
        /// браузерное превью всегда показывало один и тот же масштаб, а в приложении есть камера, и
        /// вблизи та же крошка из зерна становится квадратами. Проверять вид надо на ТОМ масштабе,
        /// на котором ДМ смотрит: у превью это 0.91 px на единицу, у снимка ДМ — около 1.
        /// </summary>
        public static void Write(string path, float zoom = 0.91f)
        {
            float side = W / Math.Max(0.01f, zoom);
            float x0 = (W - side) * 0.55f, y0 = (H - side) * 0.45f;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W)}' height='{F(H)}' ")
              .Append($"viewBox='{F(x0)} {F(y0)} {F(side)} {F(side)}'>");
            sb.Append($"<rect x='{F(x0)}' y='{F(y0)}' width='{F(side)}' height='{F(side)}' fill='{Hex(Ground0)}'/>");
            // Пятна биомов — видно, как тело оказывается окном в карту и как граница биома проходит
            // сквозь гору.
            foreach (var patch in Patches)
                sb.Append($"<ellipse cx='{F(patch.C.X)}' cy='{F(Flip(patch.C.Y))}' ")
                  .Append($"rx='{F(patch.R.X)}' ry='{F(patch.R.Y)}' fill='{Hex(patch.Colour)}'/>");

            // Та же фикстура, что ДМ снимал в браузере («Горная страна»), и тот же радиус 10:
            // сравнивать вид можно только на одной и той же форме и в одном масштабе.
            Draw(sb, new MountainSettings().Radius, "горная страна", Country());

            sb.Append("</svg>");
            System.IO.File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path} (масштаб {zoom:0.00} px на единицу)");
        }

        /// <summary>
        /// Разбор: печатает числа гор в заданном куске мира — ширину подошвы, высоту, длину контура и
        /// самую толстую линию. Аргументы: x0 x1 y0 y1.
        ///
        /// Нужен, когда картинка врёт, а глазомер не говорит, чем именно. Им найдено, что «частокол»
        /// на одном из отрезков — не манера, а геометрия: горы там стоят в трёх единицах друг от
        /// друга при ширине подошвы в двадцать три.
        /// </summary>
        public static void Probe(string[] args)
        {
            float x0 = Arg(args, 1, 0f), x1 = Arg(args, 2, 1000f);
            float y0 = Arg(args, 3, 0f), y1 = Arg(args, 4, 1000f);
            var settings = new MountainSettings();
            float radius = settings.Radius;
            var mask = MountainMask.FromPolygons(Country(), MountainMask.ChooseCell(radius, radius));
            mask.Smooth((int)Math.Round(MountainSettings.MaskSmoothing / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out _);
            var profile = settings.Profile();
            var line = new List<Vector2>();
            var radii = new List<float>();
            var rise = new List<float>();
            foreach (var s in shapes)
            {
                if (s.Centre.X < x0 || s.Centre.X > x1 || s.Centre.Y < y0 || s.Centre.Y > y1) continue;
                float mnx = float.MaxValue, mxx = float.MinValue;
                foreach (var p in s.Base) { if (p.X < mnx) mnx = p.X; if (p.X > mxx) mxx = p.X; }
                MountainOutline.Build(s, profile, line, radii);
                MountainOutline.Heights(line, rise);
                float density = MountainInk.Density(s.Tier, MountainSettings.Tiers);
                int drawn = 0; float wide = 0f;
                for (int i = 0; i < rise.Count; i++)
                {
                    float h = MountainInk.HalfWidth(rise[i], s, radius, density);
                    if (h > 0f) { drawn++; if (2f * h > wide) wide = 2f * h; }
                }
                float lx0 = float.MaxValue, lx1 = float.MinValue;
                for (int i = 0; i < line.Count; i++)
                { if (radii[i] < 0.99f) { if (line[i].X < lx0) lx0 = line[i].X; if (line[i].X > lx1) lx1 = line[i].X; } }
                Console.WriteLine($"  ярус {s.Tier} центр ({s.Centre.X:0},{s.Centre.Y:0}) ширина подошвы {mxx - mnx:0.0} " +
                                  $"высота {s.Height:0.0} точек {line.Count} рисуем {drawn} самая толстая {wide:0.0} " +
                                  $"контур по x {lx1 - lx0:0.0}");
            }
        }

        /// <summary>Пятна биомов: и фон рисуют, и служат «снимком карты» для тела горы.</summary>
        struct Patch { public Vector2 C, R; public int[] Colour; }

        static readonly int[] Ground0 = { 205, 187, 146 };
        static readonly int[] InkRgb = { 26, 22, 18 };
        static readonly Patch[] Patches =
        {
            new Patch { C = new Vector2(215, 595), R = new Vector2(215, 165), Colour = new[] { 141, 157, 112 } },
            new Patch { C = new Vector2(190, 175), R = new Vector2(190, 145), Colour = new[] { 159, 180, 166 } },
            new Patch { C = new Vector2(730, 700), R = new Vector2(170, 125), Colour = new[] { 216, 214, 204 } },
        };

        /// <summary>Цвет земли под точкой. В приложении тело кладётся прозрачным и только пишет
        /// глубину — то есть показывает ровно эту краску; здесь мы её просто рисуем.</summary>
        static int[] Ground(Vector2 p)
        {
            for (int i = Patches.Length - 1; i >= 0; i--)
            {
                float dx = (p.X - Patches[i].C.X) / Patches[i].R.X;
                float dy = (p.Y - Patches[i].C.Y) / Patches[i].R.Y;
                if (dx * dx + dy * dy <= 1f) return Patches[i].Colour;
            }
            return Ground0;
        }

        static string Hex(int[] c) => $"#{c[0]:x2}{c[1]:x2}{c[2]:x2}";

        static void Draw(StringBuilder sb, float radius, string title,
                         List<IReadOnlyList<Vector2>> polys)
        {
            var settings = new MountainSettings { Radius = radius };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(radius, radius));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(MountainSettings.MaskSmoothing / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out _);
            var profile = settings.Profile();

            var verts = new List<Vector2>();
            var tris = new List<int>();
            var line = new List<Vector2>();
            var radii = new List<float>();
            var rise = new List<float>();
            int marks = 0;

            foreach (var shape in shapes)
            {
                float density = MountainInk.Density(shape.Tier, MountainSettings.Tiers);
                Body(sb, shape, profile, verts, tris);
                marks += GritMarks(sb, shape, profile, radius, density);
                MountainOutline.Build(shape, profile, line, radii);
                MountainOutline.Heights(line, rise);
                Outline(sb, shape, line, rise, radius, density);
            }
            Console.WriteLine($"  {title}: гор {shapes.Count}, меток крошки {marks}");
        }

        /// <summary>Тело — та же раскладка, что уходит в меш, и та же краска, что была бы видна
        /// сквозь него.</summary>
        static void Body(StringBuilder sb, MountainShape shape, LiftSamples profile,
                         List<Vector2> verts, List<int> tris)
        {
            MountainTriangulation.Fill(shape, profile, verts, tris);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                var centre = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
                sb.Append($"<polygon fill='{Hex(Ground(centre))}' points='");
                for (int k = 0; k < 3; k++)
                {
                    var p = verts[tris[i + k]];
                    sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                }
                sb.Append("'/>");
            }
        }

        /// <summary>
        /// Контур — ОДНА лента переменной толщины вдоль ломаной. Рвётся она только там, где толщина
        /// упала ниже пола (у самого низа горы): это и есть «низа нет», а не разрыв.
        /// </summary>
        static void Outline(StringBuilder sb, MountainShape shape, List<Vector2> line, List<float> rise,
                            float radius, float density)
        {
            int i = 0;
            while (i < line.Count)
            {
                if (MountainInk.HalfWidth(rise[i], shape, radius, density) <= 0f) { i++; continue; }
                int j = i;
                while (j + 1 < line.Count && MountainInk.HalfWidth(rise[j + 1], shape, radius, density) > 0f) j++;
                if (j > i) Ribbon(sb, shape, line, rise, i, j, radius, density);
                i = j + 1;
            }
        }

        /// <summary>Лента: сверху идём слева направо, снизу возвращаемся — один многоугольник,
        /// который в меше станет полосой треугольников.</summary>
        static void Ribbon(StringBuilder sb, MountainShape shape, List<Vector2> line, List<float> rise,
                           int from, int to, float radius, float density)
        {
            sb.Append($"<polygon fill='{Hex(InkRgb)}' points='");
            for (int k = from; k <= to; k++)
            {
                var p = Offset(shape, line, k, from, to, +1f, rise[k], radius, density);
                sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
            }
            for (int k = to; k >= from; k--)
            {
                var p = Offset(shape, line, k, from, to, -1f, rise[k], radius, density);
                sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
            }
            sb.Append("'/>");
        }

        static Vector2 Offset(MountainShape shape, List<Vector2> line, int k, int from, int to, float side,
                              float rise, float radius, float density)
        {
            Vector2 a = line[Math.Max(from, k - 1)], b = line[Math.Min(to, k + 1)];
            Vector2 d = b - a;
            if (d.LengthSquared() < 1e-10f) d = new Vector2(1f, 0f); else d = Vector2.Normalize(d);
            Vector2 nrm = new Vector2(-d.Y, d.X);
            return line[k] + nrm * (side * MountainInk.HalfWidth(rise, shape, radius, density));
        }

        /// <summary>Крошка: только полная тень, порогом, а не вероятностью.</summary>
        static int GritMarks(StringBuilder sb, MountainShape shape, LiftSamples profile,
                             float radius, float density)
        {
            int total = MountainInk.MarkCount(shape.FootArea, radius, density, out _);
            var light = MountainInk.Light;
            int n = shape.Base.Length;
            int drawn = 0;

            for (int id = 1; id <= total; id++)
            {
                int i = (int)(Rand(shape.Seed, 7u, id) * n);
                if (i >= n) i = n - 1;
                if (!MountainInk.InShadow(shape.Normal[i], light)) continue;

                float r = MountainInk.MarkR(Rand(shape.Seed, 3u, id), MountainInk.GritFall);
                var p = MountainTriangulation.Meridian(shape, i, r, profile);
                var q = MountainTriangulation.Meridian(shape, (i + 1) % n, r, profile);
                float u = Rand(shape.Seed, 211u, id);
                p = new Vector2(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u);

                float s = MountainInk.MarkSize(radius, Rand(shape.Seed, 307u, id));
                sb.Append($"<rect fill='{Hex(InkRgb)}' ")
                  .Append("x='").Append(F(p.X - s * 0.5f)).Append("' y='").Append(F(Flip(p.Y) - s * 0.35f))
                  .Append("' width='").Append(F(s)).Append("' height='").Append(F(s * 0.7f)).Append("'/>");
                drawn++;
            }
            return drawn;
        }

        static float Rand(uint seed, uint salt, int id)
        {
            unchecked
            {
                uint h = seed + salt * 668265263u + (uint)id * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) >> 8) / 16777216f;
            }
        }

        /// <summary>Фикстура «Горная страна» — один в один из Export.cs, чтобы сравнивать с тем
        /// самым снимком, который ДМ прислал.</summary>
        static List<IReadOnlyList<Vector2>> Country()
        {
            var polys = Disc(new Vector2(250, 300), 72f);
            polys.AddRange(Band(new Vector2(250, 300), new Vector2(430, 380), 26f));
            polys.AddRange(Band(new Vector2(430, 380), new Vector2(560, 320), 26f));
            polys.AddRange(Band(new Vector2(430, 380), new Vector2(470, 530), 30f));
            polys.AddRange(Band(new Vector2(470, 530), new Vector2(620, 620), 30f));
            polys.AddRange(Band(new Vector2(560, 320), new Vector2(670, 250), 22f));
            polys.AddRange(Disc(new Vector2(700, 220), 56f));
            polys.AddRange(Band(new Vector2(150, 620), new Vector2(230, 520), 24f));
            polys.AddRange(Band(new Vector2(230, 520), new Vector2(250, 380), 24f));
            return polys;
        }

        static List<IReadOnlyList<Vector2>> Band(Vector2 a, Vector2 b, float half)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            var d = Vector2.Normalize(b - a);
            var nrm = new Vector2(-d.Y, d.X);
            float length = (b - a).Length();
            for (float s = 0f; s <= length; s += 15f)
                for (float o = -half; o <= half; o += 15f)
                    polys.Add(Cell(a + d * s + nrm * o));
            return polys;
        }

        static List<IReadOnlyList<Vector2>> Disc(Vector2 centre, float radius)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = -radius; x <= radius; x += 15f)
                for (float y = -radius; y <= radius; y += 15f)
                {
                    var q = new Vector2(centre.X + x, centre.Y + y);
                    if ((q - centre).Length() > radius) continue;
                    polys.Add(Cell(q));
                }
            return polys;
        }

        static IReadOnlyList<Vector2> Cell(Vector2 c) => new List<Vector2>
        {
            new Vector2(c.X - 7.5f, c.Y - 7.5f), new Vector2(c.X + 7.5f, c.Y - 7.5f),
            new Vector2(c.X + 7.5f, c.Y + 7.5f), new Vector2(c.X - 7.5f, c.Y + 7.5f),
        };

        static float Arg(string[] args, int at, float fallback)
            => args != null && args.Length > at
               && float.TryParse(args[at], System.Globalization.NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out float v) ? v : fallback;

        static float Flip(float y) => H - y;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
