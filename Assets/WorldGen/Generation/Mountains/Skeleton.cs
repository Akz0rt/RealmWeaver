using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Ветка оси: цепочка точек в координатах СЕТКИ. Tip0/Tip1 — «свободный кончик», то есть конец,
    /// который упирается в край массы, а не в развилку. Это различие нужно доводке: кончик надо
    /// дотягивать до торца мазка (§7), а развилку — ни в коем случае, иначе ось полезет поперёк
    /// соседней ветки.
    /// </summary>
    public sealed class AxisPath
    {
        public List<Vector2> Pts = new List<Vector2>();
        public bool Tip0;
        public bool Tip1;
    }

    /// <summary>
    /// §6 «Скелет там, где кольцо выродилось»: медиальная ось маски.
    ///
    /// Три шага. Первый — утоньшение Zhang–Suen: с массы слой за слоем снимаются граничные ячейки,
    /// но только те, удаление которых не рвёт связность (число переходов 0→1 по кольцу соседей равно
    /// одному) и не укорачивает ветку (соседей не меньше двух). Остаётся линия толщиной в одну
    /// ячейку, повторяющая форму массы вместе со всеми её отростками.
    ///
    /// Второй — разбор растровой линии в граф и полилинии. Тут одна тонкость, без которой всё
    /// разваливается: диагональное ребро отбрасывается, если тот же переход достижим по ортогонали.
    /// Наклонная линия на растре — «лесенка», и у каждой её ступеньки диагональ идёт бок о бок с
    /// парой ортогональных шагов; без отбрасывания каждая ступенька выглядела бы как развилка, и ось
    /// рассыпалась бы на сотни двухточечных обрывков.
    ///
    /// Третий — чистка. Утоньшение честно отражает КАЖДУЮ неровность края: бугорок на границе маски
    /// рождает шип, а пара сближенных бугорков — «пузырь», две ветки, идущие рядом между теми же
    /// развилками. Ни то, ни другое не гора, и обе помехи убираются здесь, до того как ось попадёт
    /// в нарезку на звенья.
    ///
    /// Считается на ПОЛНОЙ сетке маски, без огрубления, хотя план и предлагал огрубить её вдвое ради
    /// скорости. Огрубление сдвинуло бы все пороги, записанные в ячейках (зазор пузыря 5, радиус
    /// кластера 3.5, плечо направления 10): в клетках они остались бы прежними, а в мире стали бы
    /// вдвое больше. Цена решения измерена на мазке длиной в сорок гор (стенд, режим time): кисть
    /// шириной в один радиус горы — 7 мс, в два — 22 мс, в четыре — 75 мс, в десять — 530 мс. Обычная
    /// кисть, ради которой всё и затевалось, стоит десятки миллисекунд; дорого становится только на
    /// исполинском массиве, а он и так уходит в фон целиком.
    ///
    /// Стоимость определяется не площадью сетки, а объёмом массы, помноженным на её толщину: список
    /// активных ячеек тает с каждым проходом. Поэтому в горячем цикле нет ни деления индекса на
    /// ширину, ни проверок границ — только они и давали три с лишним раза замедления.
    /// </summary>
    public static class Skeleton
    {
        /// <summary>Восемь соседей по кругу, начиная с северного и по часовой стрелке. Порядок важен:
        /// на нём стоит подсчёт переходов 0→1.</summary>
        static readonly int[] RingDx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        static readonly int[] RingDy = { -1, -1, 0, 1, 1, 1, 0, -1 };

        /// <summary>Насколько близко должны идти две ветки между одними и теми же развилками, чтобы
        /// счесть их пузырём. Настоящая дыра в кляксе так не выглядит: её стороны расходятся далеко.</summary>
        public const float BubbleGap = 5f;

        /// <summary>Потолок числа проходов утоньшения — предохранитель, а не рабочий предел.</summary>
        const int ThinGuard = 600;

        /// <summary>Сколько раз чередуются обрезка шипов и схлопывание пузырей. Обрезка шипа может
        /// открыть новый шип (ветка, что держалась только им), поэтому одного прохода мало.</summary>
        const int CleanRounds = 8;

        /// <summary>Утоньшение Zhang–Suen. Маска не портится — работа идёт по копии.</summary>
        public static byte[] Thin(MountainMask mask)
        {
            int w = mask.W, h = mask.H;
            var img = (byte[])mask.Cells.Clone();

            // Активными считаются только внутренние ячейки: у краевой нет полного кольца соседей, а
            // маска и так строится с полем фона по краям (MountainMask.PadCells).
            var active = new List<int>();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    if (img[y * w + x] != 0) active.Add(y * w + x);

            var kill = new List<int>();
            var next = new List<int>();
            var p = new int[8];

            // Смещения тех же восьми соседей в одномерном индексе. Активными бывают только внутренние
            // ячейки, поэтому сосед всегда существует и проверять границы в горячем цикле незачем —
            // как и делить индекс на ширину.
            var offset = new int[8];
            for (int k = 0; k < 8; k++) offset[k] = RingDy[k] * w + RingDx[k];

            bool changed = true;
            int guard = 0;
            while (changed && guard++ < ThinGuard)
            {
                changed = false;
                for (int step = 0; step < 2; step++)
                {
                    kill.Clear();
                    next.Clear();

                    foreach (int idx in active)
                    {
                        if (img[idx] == 0) continue;
                        next.Add(idx);

                        int b = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            p[k] = img[idx + offset[k]];
                            b += p[k];
                        }

                        // Соседей меньше двух — это кончик ветки, его снимать нельзя: ось укоротится.
                        // Больше шести — ячейка глубоко внутри массы, до неё очередь ещё не дошла.
                        if (b < 2 || b > 6) continue;

                        // Ровно один переход «фон → масса» по кольцу означает, что соседи ячейки
                        // образуют одну связную дугу. Тогда её удаление не разорвёт линию.
                        int a = 0;
                        for (int k = 0; k < 8; k++) if (p[k] == 0 && p[(k + 1) % 8] == 1) a++;
                        if (a != 1) continue;

                        int north = p[0], east = p[2], south = p[4], west = p[6];
                        if (step == 0)
                        {
                            if (north != 0 && east != 0 && south != 0) continue;
                            if (east != 0 && south != 0 && west != 0) continue;
                        }
                        else
                        {
                            if (north != 0 && east != 0 && west != 0) continue;
                            if (north != 0 && south != 0 && west != 0) continue;
                        }
                        kill.Add(idx);
                    }

                    var swap = active; active = next; next = swap;
                    if (kill.Count > 0)
                    {
                        changed = true;
                        foreach (int i in kill) img[i] = 0;
                    }
                }
            }
            return img;
        }

        /// <summary>
        /// Разбирает утоньшённую линию в ветки, попутно убирая шипы и пузыри. Массив sk ПОРТИТСЯ:
        /// чистка стирает ячейки прямо в нём.
        /// </summary>
        /// <param name="pruneLen">Ветка короче этого числа ячеек, у которой есть свободный конец, —
        /// шип, а не отросток.</param>
        public static List<AxisPath> Branches(byte[] sk, int w, int h, int pruneLen)
        {
            var used = new HashSet<long>();

            for (int round = 0; round < CleanRounds; round++)
            {
                var graph = BuildGraph(sk, w, h);
                var paths = TracePaths(graph, used);
                bool removed = false;

                foreach (var path in paths)
                {
                    if (path.Count >= pruneLen) continue;
                    if (graph.Deg(path[0]) != 1 && graph.Deg(path[path.Count - 1]) != 1) continue;
                    for (int i = 0; i < path.Count; i++)
                    {
                        // Развилку на конце шипа оставляем: она принадлежит и другим веткам.
                        bool keep = (i == 0 || i == path.Count - 1) && graph.Deg(path[i]) >= 3;
                        if (!keep) sk[path[i]] = 0;
                    }
                    removed = true;
                }

                // Пузыри ищем только тогда, когда шипов уже нет: шип может оказаться одной из сторон
                // мнимого пузыря, и убрать его дешевле и правильнее.
                if (!removed) removed = CollapseBubbles(graph, paths, sk, w);
                if (!removed) break;
            }

            var final = BuildGraph(sk, w, h);
            var result = new List<AxisPath>();
            foreach (var path in TracePaths(final, used))
            {
                if (path.Count < 2) continue;
                var axis = new AxisPath
                {
                    Tip0 = final.Deg(path[0]) <= 1,
                    Tip1 = final.Deg(path[path.Count - 1]) <= 1,
                };
                foreach (int i in path) axis.Pts.Add(new Vector2(i % w, i / w));
                result.Add(axis);
            }
            return result;
        }

        sealed class SkeletonGraph
        {
            /// <summary>Ячейки скелета по возрастанию индекса. Порядок задан явно, чтобы результат не
            /// зависел от того, как словарь разложил ключи.</summary>
            public List<int> Nodes = new List<int>();
            public Dictionary<int, List<int>> Adj = new Dictionary<int, List<int>>();
            public int Deg(int i) => Adj.TryGetValue(i, out var list) ? list.Count : 0;
        }

        static SkeletonGraph BuildGraph(byte[] sk, int w, int h)
        {
            var graph = new SkeletonGraph();
            var isNode = new bool[w * h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (sk[i] == 0) continue;
                    isNode[i] = true;
                    graph.Nodes.Add(i);
                    graph.Adj[i] = new List<int>();
                }
            }

            foreach (int i in graph.Nodes)
            {
                int x = i % w, y = i / w;
                var list = graph.Adj[i];
                for (int k = 0; k < 8; k++)
                {
                    int dx = RingDx[k], dy = RingDy[k];
                    int j = (y + dy) * w + (x + dx);
                    if (!isNode[j]) continue;
                    // Диагональ не нужна, если тот же сосед достижим ортогонально: иначе каждая
                    // ступенька наклонной линии выглядела бы развилкой.
                    if (dx != 0 && dy != 0 && (isNode[y * w + x + dx] || isNode[(y + dy) * w + x])) continue;
                    list.Add(j);
                }
            }
            return graph;
        }

        /// <summary>Ключ ребра, не зависящий от направления.</summary>
        static long EdgeKey(int a, int b) =>
            a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>
        /// Режет граф на ветки. Сначала от узлов (кончиков и развилок) — иначе ветка, начатая с
        /// середины, обошлась бы только в одну сторону; потом то, что осталось, — это чистые циклы,
        /// у которых узлов нет вовсе.
        /// </summary>
        static List<List<int>> TracePaths(SkeletonGraph graph, HashSet<long> used)
        {
            used.Clear();
            var paths = new List<List<int>>();

            foreach (int i in graph.Nodes)
            {
                if (graph.Deg(i) == 2) continue;
                foreach (int j in graph.Adj[i])
                    if (!used.Contains(EdgeKey(i, j))) paths.Add(Walk(graph, used, i, j));
            }
            foreach (int i in graph.Nodes)
                foreach (int j in graph.Adj[i])
                    if (!used.Contains(EdgeKey(i, j))) paths.Add(Walk(graph, used, i, j));

            return paths;
        }

        static List<int> Walk(SkeletonGraph graph, HashSet<long> used, int from, int to)
        {
            var pts = new List<int> { from, to };
            used.Add(EdgeKey(from, to));
            int prev = from, cur = to;

            while (graph.Deg(cur) == 2)
            {
                int next = -1;
                foreach (int v in graph.Adj[cur])
                    if (v != prev && !used.Contains(EdgeKey(cur, v))) { next = v; break; }
                if (next < 0) break;
                used.Add(EdgeKey(cur, next));
                pts.Add(next);
                prev = cur; cur = next;
            }
            return pts;
        }

        /// <summary>
        /// Схлопывает пузыри: если между одной и той же парой развилок идут две ветки и они нигде не
        /// расходятся дальше BubbleGap ячеек, лишняя стирается. Настоящая дыра в кляксе так не
        /// выглядит — её стороны расходятся на всю ширину дыры, и она уцелеет.
        /// </summary>
        static bool CollapseBubbles(SkeletonGraph graph, List<List<int>> paths, byte[] sk, int w)
        {
            // Развилка на растре — это пятно из нескольких соседних ячеек, а не одна точка, поэтому
            // сросшиеся узлы сначала объединяются в один «конец».
            var cluster = new Dictionary<int, int>();
            int clusters = 0;
            var stack = new Stack<int>();

            foreach (int i in graph.Nodes)
            {
                if (graph.Deg(i) == 2 || cluster.ContainsKey(i)) continue;
                cluster[i] = clusters;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (int v in graph.Adj[u])
                        if (graph.Deg(v) != 2 && !cluster.ContainsKey(v)) { cluster[v] = clusters; stack.Push(v); }
                }
                clusters++;
            }

            var groupOf = new Dictionary<long, int>();
            var groups = new List<List<int>>();
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                // Чистый цикл узлов не имеет, и парой развилок не описывается — пропускаем.
                if (!cluster.TryGetValue(path[0], out int a)) continue;
                if (!cluster.TryGetValue(path[path.Count - 1], out int b)) continue;
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!groupOf.TryGetValue(key, out int gi))
                {
                    gi = groups.Count;
                    groupOf[key] = gi;
                    groups.Add(new List<int>());
                }
                groups[gi].Add(i);
            }

            bool removed = false;
            foreach (var group in groups)
            {
                if (group.Count < 2) continue;
                // Длинная ветка выживает. При равной длине побеждает младший номер — иначе
                // неустойчивая сортировка сделала бы результат зависящим от реализации.
                group.Sort((x, y) =>
                {
                    int d = paths[y].Count.CompareTo(paths[x].Count);
                    return d != 0 ? d : x.CompareTo(y);
                });

                for (int k = 1; k < group.Count; k++)
                {
                    var path = paths[group[k]];
                    if (Apart(path, paths[group[0]], w) > BubbleGap) continue;
                    for (int i = 1; i < path.Count - 1; i++) sk[path[i]] = 0;
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>Насколько далеко ветка a отходит от ветки b: худшее из расстояний «от точки a до
        /// ближайшей точки b». Точки берутся через одну — мера грубая, и точнее ей быть незачем.</summary>
        static float Apart(List<int> a, List<int> b, int w)
        {
            float worst = 0f;
            for (int i = 0; i < a.Count; i += 2)
            {
                int ax = a[i] % w, ay = a[i] / w;
                float best = float.MaxValue;
                for (int j = 0; j < b.Count; j++)
                {
                    float dx = ax - b[j] % w, dy = ay - b[j] / w;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < best) best = d;
                }
                if (best > worst) worst = best;
            }
            return worst;
        }
    }
}
