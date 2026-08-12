using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>Линия уровня поля расстояний. Точки — в координатах СЕТКИ, не мира.</summary>
    public sealed class IsoContour
    {
        public List<Vector2> Points = new List<Vector2>();
        public bool Closed;
    }

    /// <summary>
    /// §4: извлечение линий уровня марширующими квадратами. В каждой ячейке 2×2 по знакам D − L
    /// выбирается конфигурация отрезков, точки пересечения находятся линейной интерполяцией вдоль
    /// рёбер, неоднозначные конфигурации (5 и 10) разрешаются по значению в центре ячейки.
    ///
    /// Отрезки сшиваются в полилинии по СОВПАДЕНИЮ КОНЦОВ, и совпадение это точное, а не «в пределах
    /// допуска»: соседние ячейки считают общее ребро одной и той же формулой из одних и тех же двух
    /// значений, поэтому получают бит в бит одинаковую точку. Округление до тысячной — только чтобы
    /// сделать из точки ключ словаря.
    /// </summary>
    public static class IsoContours
    {
        public static List<IsoContour> Trace(float[] field, int w, int h, float level)
        {
            var segments = new List<(Vector2 A, Vector2 B)>();

            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    float v0 = field[y * w + x];
                    float v1 = field[y * w + x + 1];
                    float v2 = field[(y + 1) * w + x + 1];
                    float v3 = field[(y + 1) * w + x];

                    int idx = 0;
                    if (v0 >= level) idx |= 1;
                    if (v1 >= level) idx |= 2;
                    if (v2 >= level) idx |= 4;
                    if (v3 >= level) idx |= 8;
                    if (idx == 0 || idx == 15) continue;

                    Vector2 Top() => Lerp(x, y, v0, x + 1, y, v1, level);
                    Vector2 Right() => Lerp(x + 1, y, v1, x + 1, y + 1, v2, level);
                    Vector2 Bottom() => Lerp(x, y + 1, v3, x + 1, y + 1, v2, level);
                    Vector2 Left() => Lerp(x, y, v0, x, y + 1, v3, level);

                    switch (idx)
                    {
                        case 1: case 14: segments.Add((Left(), Top())); break;
                        case 2: case 13: segments.Add((Top(), Right())); break;
                        case 3: case 12: segments.Add((Left(), Right())); break;
                        case 4: case 11: segments.Add((Right(), Bottom())); break;
                        case 6: case 9: segments.Add((Top(), Bottom())); break;
                        case 7: case 8: segments.Add((Left(), Bottom())); break;
                        case 5:
                        {
                            float center = (v0 + v1 + v2 + v3) * 0.25f;
                            if (center >= level) { segments.Add((Left(), Bottom())); segments.Add((Top(), Right())); }
                            else { segments.Add((Left(), Top())); segments.Add((Right(), Bottom())); }
                            break;
                        }
                        case 10:
                        {
                            float center = (v0 + v1 + v2 + v3) * 0.25f;
                            if (center >= level) { segments.Add((Left(), Top())); segments.Add((Right(), Bottom())); }
                            else { segments.Add((Left(), Bottom())); segments.Add((Top(), Right())); }
                            break;
                        }
                    }
                }
            }

            return Stitch(segments);
        }

        static Vector2 Lerp(float x1, float y1, float v1, float x2, float y2, float v2, float level)
        {
            float denominator = v2 - v1;
            float t = Math.Abs(denominator) < 1e-12f ? 0.5f : (level - v1) / denominator;
            return new Vector2(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
        }

        sealed class Node
        {
            public Vector2 Point;
            public readonly List<int> Segments = new List<int>();
        }

        static List<IsoContour> Stitch(List<(Vector2 A, Vector2 B)> segments)
        {
            var result = new List<IsoContour>();
            if (segments.Count == 0) return result;

            var nodes = new Dictionary<long, Node>();
            for (int i = 0; i < segments.Count; i++)
            {
                Attach(nodes, segments[i].A, i);
                Attach(nodes, segments[i].B, i);
            }

            var used = new bool[segments.Count];

            // Сначала открытые линии: они начинаются в узле, у которого ровно один отрезок. Если
            // начать с середины, половина линии осталась бы необойдённой — обход идёт в одну сторону.
            foreach (var pair in nodes)
            {
                var node = pair.Value;
                if (node.Segments.Count != 1 || used[node.Segments[0]]) continue;
                var points = Walk(nodes, segments, used, pair.Key);
                if (points.Count > 2) result.Add(new IsoContour { Points = points, Closed = false });
            }

            // Что осталось — замкнутые кольца: у них нет конца, начинать можно откуда угодно.
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;
                var points = Walk(nodes, segments, used, Key(segments[i].A));
                if (points.Count > 2) result.Add(new IsoContour { Points = points, Closed = true });
            }

            return result;
        }

        static void Attach(Dictionary<long, Node> nodes, Vector2 p, int segment)
        {
            long key = Key(p);
            if (!nodes.TryGetValue(key, out var node))
            {
                node = new Node { Point = p };
                nodes[key] = node;
            }
            node.Segments.Add(segment);
        }

        static List<Vector2> Walk(Dictionary<long, Node> nodes, List<(Vector2 A, Vector2 B)> segments,
                                  bool[] used, long startKey)
        {
            var points = new List<Vector2> { nodes[startKey].Point };
            long current = startKey;
            while (true)
            {
                var node = nodes[current];
                int pick = -1;
                foreach (int i in node.Segments) if (!used[i]) { pick = i; break; }
                if (pick < 0) break;

                used[pick] = true;
                var segment = segments[pick];
                Vector2 other = Key(segment.A) == current ? segment.B : segment.A;
                points.Add(other);
                current = Key(other);
            }
            return points;
        }

        static long Key(Vector2 p)
        {
            long x = (long)Math.Round(p.X * 1000.0);
            long y = (long)Math.Round(p.Y * 1000.0);
            return (x << 32) ^ (y & 0xFFFFFFFFL);
        }
    }
}
