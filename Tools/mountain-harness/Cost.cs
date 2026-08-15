using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// Во что обходится рисунок. Мера, а не догадка: ДМ попросил, чтобы рисование и отмена мазка не
    /// вызывали заминок, а «сколько там треугольников» на глаз не определить.
    ///
    /// Считается по НАСТОЯЩЕЙ маске из клеток — той же, что строит приложение по рельефу: клетки
    /// стоят через 15 единиц, гора радиусом 10. Размер массива берётся такой, какой ДМ может
    /// нарисовать одним хребтом, и ещё вчетверо больший — чтобы видеть, как растёт.
    ///
    /// Тушь в мешах живёт в слое Rendering, куда стенд не смотрит; здесь считаются те же формулы по
    /// чистому слою (MountainInk, MountainTriangulation.FillSize) — то есть ровно те числа, по
    /// которым рендер и раскладывает вершины.
    /// </summary>
    static class Cost
    {
        public static void Report()
        {
            Console.WriteLine("Цена рисунка. Маска из клеток по 15 единиц, гора R = 10.");
            Console.WriteLine();
            Console.WriteLine("  массив              гор   вершин  треугольников   счёт, мс");

            Measure("гряда 120×30", Band(120f, 30f));
            Measure("хребет 300×60", Band(300f, 60f));
            Measure("массив 240×240", Disc(120f));
            Measure("страна 480×480", Disc(240f));
        }

        static void Measure(string name, List<IReadOnlyList<Vector2>> polys)
        {
            var settings = new MountainSettings { Radius = 10f };

            var watch = Stopwatch.StartNew();
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(10f, 10f));
            if (mask == null) { Console.WriteLine($"  {name}: маска не построена"); return; }
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out _);

            var profile = settings.Profile();
            long verts = 0, tris = 0, marks = 0;
            bool capped = false;
            var line = new System.Collections.Generic.List<System.Numerics.Vector2>();
            var radii = new System.Collections.Generic.List<float>();
            foreach (var shape in shapes)
            {
                MountainTriangulation.FillSize(shape, out int bodyVerts, out int bodyTris);
                verts += bodyVerts;
                tris += bodyTris / 3;

                // Контур: одна лента вдоль ломаной — по две вершины и два треугольника на звено.
                float density = MountainInk.Density(shape.Tier, MountainSettings.Tiers);
                MountainOutline.Build(shape, profile, line, radii);
                int drawn = 0;
                for (int i = 0; i < radii.Count; i++)
                    if (MountainInk.HalfWidth(radii[i], shape, settings.Radius, density) > 0f) drawn++;
                verts += drawn * 2;
                tris += Math.Max(0, drawn - 1) * 2;

                // Крошка: четырёхугольник на метку. Промоин больше нет вовсе.
                int grit = MountainInk.MarkCount(shape.FootArea, settings.Radius, density, out bool hit);
                capped |= hit;
                marks += grit;
                verts += grit * 4;
                tris += grit * 2;
            }
            // Секундомер закрывается ЗДЕСЬ, а не после геометрии: контур считается по горе, и его
            // цена — такая же часть «не зависает ли», как и поле расстояний. Первая редакция мерила
            // только геометрию и молчала о половине работы.
            watch.Stop();

            Console.WriteLine($"  {name,-18} {shapes.Count,5} {verts,8} {tris,14} {watch.ElapsedMilliseconds,10}"
                            + (capped ? "   (крошка упёрлась в потолок)" : ""));
        }

        /// <summary>Полоса клеток — гряда.</summary>
        static List<IReadOnlyList<Vector2>> Band(float length, float width)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = 0f; x <= length; x += 15f)
                for (float y = 0f; y <= width; y += 15f)
                    polys.Add(Cell(new Vector2(300f + x, 300f + y)));
            return polys;
        }

        /// <summary>Круг клеток — массив.</summary>
        static List<IReadOnlyList<Vector2>> Disc(float radius)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            var centre = new Vector2(600f, 600f);
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
    }
}
