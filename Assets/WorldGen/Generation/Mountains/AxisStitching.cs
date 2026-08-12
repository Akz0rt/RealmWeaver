using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Сшивание веток в узлах.
    ///
    /// Разбор скелета режет линию в каждой развилке — это правильно для графа и неправильно для оси.
    /// Ось, идущая «насквозь» через развилку, должна остаться ОДНОЙ: горы на ней нарезаются подряд,
    /// одна за другой. Если её оставить двумя, каждая половина получит свою нарезку со своими
    /// концами, и в месте стыка встанут две горы вплотную, торцами друг к другу. Виднее всего это на
    /// кольце, дорисованном вторым мазком: оно распадается на дуги в точке стыка.
    ///
    /// Правило простое: в узле встречаются несколько концов, и парой становятся те два, что идут
    /// НАВСТРЕЧУ друг другу — их направления смотрят в разные стороны, скалярное произведение близко
    /// к −1. Ветка, приходящая сбоку, ни с кем не спарится и останется отдельной осью.
    /// </summary>
    public static class AxisStitching
    {
        /// <summary>Радиус, в котором концы считаются встретившимися в одном узле. Развилка на растре
        /// — пятно из нескольких соседних ячеек, и концы веток расходятся по нему.</summary>
        public const float ClusterRadius = 3.5f;

        /// <summary>Сколько точек берётся, чтобы измерить направление конца. По одному соседу
        /// направление считать нельзя: на растре оно принимает всего восемь значений.</summary>
        public const int DirectionSpan = 10;

        /// <summary>Порог «идут навстречу». −0.25 — это примерно 105°: заметно тупее прямого угла.</summary>
        public const float StraightLimit = -0.25f;

        public static List<AxisPath> Stitch(List<AxisPath> paths)
        {
            var result = new List<AxisPath>();
            if (paths == null || paths.Count == 0) return result;

            // Каждый конец каждой ветки — отдельный участник. side: 0 — начало, 1 — конец.
            var ends = new List<(int Path, int Side, Vector2 P)>();
            for (int i = 0; i < paths.Count; i++)
            {
                var pts = paths[i].Pts;
                ends.Add((i, 0, pts[0]));
                ends.Add((i, 1, pts[pts.Count - 1]));
            }

            var cluster = new int[ends.Count];
            for (int i = 0; i < cluster.Length; i++) cluster[i] = -1;
            int clusters = 0;
            var stack = new Stack<int>();

            for (int i = 0; i < ends.Count; i++)
            {
                if (cluster[i] >= 0) continue;
                cluster[i] = clusters;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    for (int v = 0; v < ends.Count; v++)
                    {
                        if (cluster[v] >= 0) continue;
                        if (Vector2.Distance(ends[u].P, ends[v].P) > ClusterRadius) continue;
                        cluster[v] = clusters;
                        stack.Push(v);
                    }
                }
                clusters++;
            }

            var byCluster = new List<List<int>>();
            for (int i = 0; i < clusters; i++) byCluster.Add(new List<int>());
            for (int i = 0; i < ends.Count; i++) byCluster[cluster[i]].Add(i);

            // partner: ключ = номер ветки × 2 + сторона.
            var partner = new Dictionary<int, int>();

            foreach (var list in byCluster)
            {
                if (list.Count < 2) continue;

                var dirs = new Vector2[list.Count];
                for (int i = 0; i < list.Count; i++)
                    dirs[i] = Direction(paths[ends[list[i]].Path], ends[list[i]].Side);

                var cands = new List<(int I, int J, float Score)>();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        cands.Add((i, j, Vector2.Dot(dirs[i], dirs[j])));

                // Чем ближе к −1, тем прямее продолжение. Равные счёта разводим по номерам, чтобы
                // результат не зависел от того, как сортировка переставила совпадающие элементы.
                cands.Sort((a, b) =>
                {
                    int d = a.Score.CompareTo(b.Score);
                    if (d != 0) return d;
                    d = a.I.CompareTo(b.I);
                    return d != 0 ? d : a.J.CompareTo(b.J);
                });

                // В кластере ровно два конца — это просто разрыв линии, а не развилка: соединяем без
                // оглядки на направления. Проверять «счёт ≤ 1» тут нельзя — скалярное произведение
                // двух единичных векторов из-за округления бывает чуть больше единицы.
                bool unconditional = list.Count == 2;

                var used = new bool[list.Count];
                foreach (var c in cands)
                {
                    if (used[c.I] || used[c.J]) continue;
                    if (!unconditional && c.Score > StraightLimit) continue;
                    used[c.I] = used[c.J] = true;
                    var a = ends[list[c.I]];
                    var b = ends[list[c.J]];
                    partner[a.Path * 2 + a.Side] = b.Path * 2 + b.Side;
                    partner[b.Path * 2 + b.Side] = a.Path * 2 + a.Side;
                }
            }

            var usedPath = new bool[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                if (usedPath[i]) continue;

                // Сперва откатываемся по цепочке назад, до самого её начала, — иначе ось, начатая с
                // середины, потеряет всё, что было до неё.
                int pi = i, side = 0, guard = 0;
                var back = new HashSet<int> { i };
                while (guard++ < 2000)
                {
                    if (!partner.TryGetValue(pi * 2 + side, out int k)) break;
                    int pj = k / 2, sj = k % 2;
                    if (back.Contains(pj)) break;
                    back.Add(pj);
                    pi = pj;
                    side = 1 - sj;
                }

                var pts = new List<Vector2>();
                bool haveHead = false, tip0 = false, tip1 = true;
                int cur = pi, curSide = side;
                var seen = new HashSet<int>();

                while (guard++ < 4000)
                {
                    if (seen.Contains(cur)) break;
                    seen.Add(cur);
                    usedPath[cur] = true;

                    var path = paths[cur];
                    if (!haveHead) { tip0 = curSide == 0 ? path.Tip0 : path.Tip1; haveHead = true; }
                    tip1 = curSide == 0 ? path.Tip1 : path.Tip0;
                    Append(pts, path.Pts, curSide == 1);

                    if (!partner.TryGetValue(cur * 2 + (1 - curSide), out int k)) break;
                    int pj = k / 2, sj = k % 2;
                    // Цепочка замкнулась сама на себя: у такой оси концов нет вовсе.
                    if (seen.Contains(pj)) { tip0 = tip1 = false; break; }
                    cur = pj;
                    curSide = sj;
                }

                if (pts.Count >= 2) result.Add(new AxisPath { Pts = pts, Tip0 = tip0, Tip1 = tip1 });
            }
            return result;
        }

        /// <summary>Единичное направление от конца ветки внутрь неё.</summary>
        static Vector2 Direction(AxisPath path, int side)
        {
            var pts = path.Pts;
            int n = pts.Count;
            int span = Math.Min(DirectionSpan, n - 1);
            Vector2 a = side == 0 ? pts[0] : pts[n - 1];
            Vector2 b = side == 0 ? pts[span] : pts[n - 1 - span];
            Vector2 d = b - a;
            float len = d.Length();
            return len < 1e-6f ? Vector2.Zero : d / len;
        }

        /// <summary>Дописывает ветку к цепочке, при необходимости развернув её. Первая точка новой
        /// ветки совпадает с последней точкой предыдущей и не дублируется.</summary>
        static void Append(List<Vector2> dst, List<Vector2> src, bool reversed)
        {
            int start = dst.Count == 0 ? 0 : 1;
            if (reversed)
                for (int i = src.Count - 1 - start; i >= 0; i--) dst.Add(src[i]);
            else
                for (int i = start; i < src.Count; i++) dst.Add(src[i]);
        }
    }
}
