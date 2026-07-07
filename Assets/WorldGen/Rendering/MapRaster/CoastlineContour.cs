using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Строит гладкую границу суша/вода из уже посчитанного графа Corner (см. CornerGraphBuilder):
    /// трассирует упорядоченные замкнутые петли вдоль рёбер клеточных полигонов, где по одну
    /// сторону вода (океан или озеро), по другую суша, и сглаживает их Chaikin corner-cutting.
    /// См. docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md.
    /// </summary>
    public static class CoastlineContour
    {
        static bool IsWaterCell(VoronoiCell cell) => cell.EffectiveIsOcean || cell.EffectiveIsLake;

        /// <summary>Трассирует все замкнутые петли границы суша/вода (материк/острова И озёра —
        /// один и тот же проход, тип петли не важен, см. design doc) и сглаживает каждую
        /// smoothingIterations итерациями Chaikin. Разомкнутые/вырожденные цепочки (не должны
        /// встречаться в штатном случае — падение высоты к краю карты гарантирует замкнутость,
        /// см. design doc "Риски") пропускаются, не бросают исключение.</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById, int smoothingIterations)
        {
            var cornerById = new Dictionary<int, Corner>(corners.Count);
            foreach (var c in corners) cornerById[c.Id] = c;

            // boundaryNeighbors[cornerId] = Id соседних corners, с которыми этот corner связан
            // ребром на границе вода/суша (в невырожденной точке диаграммы Вороного - ровно 2,
            // см. design doc: чётность 2-раскраски треугольного цикла из 3 клеток, встречающихся
            // в одной точке).
            var boundaryNeighbors = new Dictionary<int, List<int>>();

            foreach (var corner in corners)
            {
                foreach (var neighborId in corner.NeighborCornerIds)
                {
                    if (neighborId <= corner.Id) continue; // каждое неориентированное ребро - один раз
                    if (!cornerById.TryGetValue(neighborId, out var neighbor)) continue;

                    var shared = SharedCellIds(corner, neighbor);
                    if (shared.Count != 2) continue; // ребро по краю карты (1 клетка) - не берег

                    bool water0 = cellById.TryGetValue(shared[0], out var c0) && IsWaterCell(c0);
                    bool water1 = cellById.TryGetValue(shared[1], out var c1) && IsWaterCell(c1);
                    if (water0 == water1) continue; // обе клетки одной категории - не граница

                    AddBoundaryNeighbor(boundaryNeighbors, corner.Id, neighbor.Id);
                    AddBoundaryNeighbor(boundaryNeighbors, neighbor.Id, corner.Id);
                }
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<int>();

            foreach (var startId in boundaryNeighbors.Keys)
            {
                if (visited.Contains(startId)) continue;
                var loopIds = WalkClosedLoop(startId, boundaryNeighbors, visited);
                if (loopIds == null) continue; // разомкнутая/вырожденная цепочка - пропускаем

                var points = loopIds.Select(id => cornerById[id].Position).ToList();
                loops.Add(ChaikinSmoothClosed(points, smoothingIterations));
            }

            return loops;
        }

        static List<int> SharedCellIds(Corner a, Corner b)
        {
            var result = new List<int>();
            foreach (var id in a.TouchingCellIds)
                if (b.TouchingCellIds.Contains(id))
                    result.Add(id);
            return result;
        }

        static void AddBoundaryNeighbor(Dictionary<int, List<int>> map, int fromId, int toId)
        {
            if (!map.TryGetValue(fromId, out var list))
            {
                list = new List<int>();
                map[fromId] = list;
            }
            if (!list.Contains(toId)) list.Add(toId);
        }

        /// <summary>Идёт по цепочке boundary-соседей от startId, каждый раз выбирая соседа,
        /// отличного от предыдущего corner, пока не вернётся в startId. null, если цепочка не
        /// замкнулась (открытый конец или вырожденный узел с числом boundary-соседей != 2).</summary>
        static List<int> WalkClosedLoop(int startId, Dictionary<int, List<int>> boundaryNeighbors, HashSet<int> visited)
        {
            var loop = new List<int> { startId };
            visited.Add(startId);

            int previousId = -1;
            int currentId = startId;
            int maxSteps = boundaryNeighbors.Count + 1;

            for (int step = 0; step < maxSteps; step++)
            {
                var neighbors = boundaryNeighbors[currentId];
                if (neighbors.Count != 2) return null; // открытый конец или вырожденный узел

                int nextId = neighbors[0] == previousId ? neighbors[1] : neighbors[0];

                if (nextId == startId) return loop; // петля замкнулась

                if (visited.Contains(nextId)) return null; // защита от вырожденной топологии

                loop.Add(nextId);
                visited.Add(nextId);
                previousId = currentId;
                currentId = nextId;
            }

            return null; // не замкнулась за разумное число шагов - вырождение, пропускаем
        }

        /// <summary>Chaikin corner-cutting: заменяет каждый отрезок A-B двумя точками
        /// 0.75A+0.25B и 0.25A+0.75B. iterations=0 - без изменений (текущее "гранёное" поведение).
        /// Сохраняет замкнутость петли на каждой итерации (индексация по модулю).</summary>
        static List<Vector2> ChaikinSmoothClosed(List<Vector2> points, int iterations)
        {
            if (points.Count < 3) return points;

            for (int iter = 0; iter < iterations; iter++)
            {
                var next = new List<Vector2>(points.Count * 2);
                int n = points.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % n];
                    next.Add(a + (b - a) * 0.25f);
                    next.Add(a + (b - a) * 0.75f);
                }
                points = next;
            }

            return points;
        }
    }
}
