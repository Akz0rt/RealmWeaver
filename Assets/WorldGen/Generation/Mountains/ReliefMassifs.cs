using System.Collections.Generic;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Разбиение «горных» клеток карты на отдельные МАССИВЫ — связные куски по соседству клеток.
    ///
    /// Появилось с разворотом 2026-08-14: источник гор — рельеф, а не мазок. Раньше единицей расчёта
    /// было пятно из пересекающихся мазков (MountainBlob), теперь — связный кусок клеток, у которых
    /// высота дотянула до «горы». Слияние и разрезание массивов получаются сами собой: подрисовал
    /// перемычку — соседство связало два куска в один, стёр — распались обратно, и хранить для этого
    /// нечего.
    ///
    /// Соседство приходит снаружи функцией, а не списком VoronoiCell, ровно по той же причине, по
    /// какой весь этот слой не знает UnityEngine: так его гоняет офлайн-стенд на графе из десятка
    /// клеток, где правильный и неправильный ответ видно глазами.
    /// </summary>
    public static class ReliefMassifs
    {
        /// <summary>
        /// Связные куски множества ids по соседству. Порядок кусков и порядок клеток внутри куска —
        /// от порядка ids, то есть повторяемый: рисунок обязан быть одинаковым от запуска к запуску.
        /// </summary>
        public static List<List<int>> Split(IReadOnlyList<int> ids,
                                            System.Func<int, IReadOnlyList<int>> neighbours)
        {
            var result = new List<List<int>>();
            if (ids == null || ids.Count == 0 || neighbours == null) return result;

            var members = new HashSet<int>(ids);
            var seen = new HashSet<int>();
            var stack = new Stack<int>();

            foreach (int start in ids)
            {
                if (!seen.Add(start)) continue;
                var piece = new List<int>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    piece.Add(current);
                    var near = neighbours(current);
                    if (near == null) continue;
                    foreach (int n in near)
                        if (members.Contains(n) && seen.Add(n)) stack.Push(n);
                }
                result.Add(piece);
            }
            return result;
        }
    }
}
