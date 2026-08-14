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
    /// Выгрузка ЗВЕНЬЕВ для живого превью в браузере.
    ///
    /// Зачем именно звенья, а не готовые горы: всё, что ДМ крутит на превью (острота, высота, число
    /// ярусов, талия, растяжение, краски), стоит ниже звеньев по течению и считается из них за
    /// микросекунды — это можно повторить на JS. А всё, что выше — маска, поле расстояний, скелет,
    /// кольца, нарезка на звенья, — стоит десятки миллисекунд и переписыванию на JS не подлежит.
    /// Поэтому дорогую половину конвейера считает стенд НАСТОЯЩИМ кодом слоя один раз, а дешёвую
    /// повторяет страница на каждое движение ползунка.
    ///
    /// Прямое следствие, о котором надо помнить: на превью нельзя менять радиус горы, длину звена и
    /// сглаживание — они меняют сами звенья. Для них пришлось бы выгружать новый набор.
    /// </summary>
    static class Export
    {
        /// <summary>Та же высота холста, что у Preview.Flip: превью переворачивает Y этим числом.</summary>
        public const float CanvasH = 900f;

        public static void WriteJson(string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\"canvasH\":").Append(N(CanvasH)).Append(",\"fixtures\":[");

            Fixture(sb, "Гряда", Ridge(), true);
            sb.Append(',');
            Fixture(sb, "Массив с хвостом", HeadAndTail(), false);
            sb.Append(',');
            Fixture(sb, "Горная страна", Country(), false);
            sb.Append(',');
            // Тот же хребет, но радиусом крупнее: в манере туши размер горы решает всё. В мелкую
            // гору не помещается ни жирная линия, ни крошка — цепь мелких вершин читается не
            // хребтом, а бахромой. В приложении это тот же ползунок радиуса.
            Fixture(sb, "Крупные горы", Ridge(), false, 26f);

            sb.Append("]}");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path} ({sb.Length / 1024} КБ)");
        }

        static void Fixture(StringBuilder sb, string name, List<IReadOnlyList<Vector2>> polys, bool first,
                            float radius = 10f)
        {
            var settings = new MountainSettings { Radius = radius };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(radius, radius));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(0.5f * radius / mask.Cell));
            MountainGeometry.BuildFromMask(mask, settings, out var links);

            int maxTier = 0;
            foreach (var link in links) if (link.Tier > maxTier) maxTier = link.Tier;
            Console.WriteLine($"{name}: звеньев {links.Count}, слоёв {maxTier + 1}");

            sb.Append("{\"name\":\"").Append(name).Append("\",\"tiers\":").Append(settings.Tiers)
              .Append(",\"sampleStep\":").Append(N(settings.SampleStep))
              .Append(",\"links\":[");

            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"p\":[");
                for (int j = 0; j < link.Pts.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(N(link.Pts[j].X)).Append(',').Append(N(link.Pts[j].Y));
                }
                sb.Append("],\"w\":[");
                for (int j = 0; j < link.Ws.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(N(link.Ws[j]));
                }
                sb.Append("],\"m\":[").Append(N(link.Mid.X)).Append(',').Append(N(link.Mid.Y))
                  .Append("],\"t\":[").Append(N(link.Tan.X)).Append(',').Append(N(link.Tan.Y))
                  .Append("],\"mw\":").Append(N(link.MidW))
                  .Append(",\"tier\":").Append(link.Tier)
                  .Append(",\"ts\":").Append(N(link.TierScale))
                  .Append(",\"hj\":").Append(N(link.HeightJitter))
                  .Append(",\"f0\":").Append(link.FreeStart ? 1 : 0)
                  .Append(",\"f1\":").Append(link.FreeEnd ? 1 : 0)
                  .Append('}');
            }
            sb.Append("]}");
        }

        // ── фикстуры ────────────────────────────────────────────────────────────────────────────

        static List<IReadOnlyList<Vector2>> Ridge()
            => Along(new[]
            {
                new Vector2(140, 650), new Vector2(215, 560), new Vector2(240, 450),
                new Vector2(355, 380), new Vector2(385, 265), new Vector2(330, 165),
            }, 32f);

        static List<IReadOnlyList<Vector2>> HeadAndTail()
        {
            var polys = Along(new[]
            {
                new Vector2(180, 690), new Vector2(260, 600), new Vector2(300, 500),
                new Vector2(330, 420),
            }, 28f);
            polys.AddRange(Disc(new Vector2(360, 300), 78f));
            return polys;
        }

        static List<IReadOnlyList<Vector2>> Country()
        {
            var polys = Disc(new Vector2(250, 300), 72f);
            polys.AddRange(Along(new[] { new Vector2(250, 300), new Vector2(430, 380), new Vector2(560, 320) }, 26f));
            polys.AddRange(Along(new[] { new Vector2(430, 380), new Vector2(470, 530), new Vector2(620, 620) }, 30f));
            polys.AddRange(Along(new[] { new Vector2(560, 320), new Vector2(670, 250) }, 22f));
            polys.AddRange(Disc(new Vector2(700, 220), 56f));
            polys.AddRange(Along(new[] { new Vector2(150, 620), new Vector2(230, 520), new Vector2(250, 380) }, 24f));
            return polys;
        }

        // ── клетки карты ────────────────────────────────────────────────────────────────────────
        //
        // Те же 15 единиц шага, что у настоящей карты, и те же квадраты вместо многоугольников
        // Вороного: превью про ВИД гор, а не про форму клеток.

        const float Step = 15f;

        static List<IReadOnlyList<Vector2>> Along(Vector2[] screenPath, float halfWidth)
        {
            var path = new Vector2[screenPath.Length];
            for (int i = 0; i < screenPath.Length; i++)
                path[i] = new Vector2(screenPath[i].X, CanvasH - screenPath[i].Y);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in path)
            {
                minX = Math.Min(minX, p.X - halfWidth); maxX = Math.Max(maxX, p.X + halfWidth);
                minY = Math.Min(minY, p.Y - halfWidth); maxY = Math.Max(maxY, p.Y + halfWidth);
            }

            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = Snap(minX); x <= maxX; x += Step)
                for (float y = Snap(minY); y <= maxY; y += Step)
                {
                    var c = new Vector2(x, y);
                    if (DistanceToPath(path, c) > halfWidth) continue;
                    polys.Add(Cell(c));
                }
            return polys;
        }

        static List<IReadOnlyList<Vector2>> Disc(Vector2 screenCentre, float radius)
        {
            var centre = new Vector2(screenCentre.X, CanvasH - screenCentre.Y);
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = Snap(centre.X - radius); x <= centre.X + radius; x += Step)
                for (float y = Snap(centre.Y - radius); y <= centre.Y + radius; y += Step)
                {
                    var c = new Vector2(x, y);
                    if ((c - centre).Length() > radius) continue;
                    polys.Add(Cell(c));
                }
            return polys;
        }

        static float Snap(float v) => (float)Math.Floor(v / Step) * Step;

        static IReadOnlyList<Vector2> Cell(Vector2 c)
        {
            float h = Step * 0.5f;
            return new List<Vector2>
            {
                new Vector2(c.X - h, c.Y - h), new Vector2(c.X + h, c.Y - h),
                new Vector2(c.X + h, c.Y + h), new Vector2(c.X - h, c.Y + h),
            };
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

        static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
