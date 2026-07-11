using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Озеро (связная компонента озёрных клеток) целиком принадлежит одному региону, чтобы
    /// граница региона проходила по внешней кромке озера, а не сквозь него. Здесь — переиспользуемые
    /// чистые помощники (генерация И кисти зовут одно и то же) и генерационный проход UnifyLakes.
    /// </summary>
    public static class LakeRegionUnifier
    {
        /// <summary>BFS: связная компонента озёрных клеток, содержащая startCellId.
        /// Пустой список, если стартовая клетка не озеро или не найдена.</summary>
        public static List<VoronoiCell> FindLakeComponent(int startCellId, Dictionary<int, VoronoiCell> cellById)
        {
            var component = new List<VoronoiCell>();
            if (!cellById.TryGetValue(startCellId, out var start) || !start.EffectiveIsLake)
                return component;

            var visited = new HashSet<int> { startCellId };
            var queue = new Queue<int>();
            queue.Enqueue(startCellId);
            while (queue.Count > 0)
            {
                var cell = cellById[queue.Dequeue()];
                component.Add(cell);
                foreach (var neighborId in cell.NeighborIds)
                {
                    if (visited.Contains(neighborId)) continue;
                    if (!cellById.TryGetValue(neighborId, out var neighbor)) continue;
                    if (!neighbor.EffectiveIsLake) continue;
                    visited.Add(neighborId);
                    queue.Enqueue(neighborId);
                }
            }
            return component;
        }

        /// <summary>Регион по большинству голосов соседних СУШНЫХ клеток компоненты озера.
        /// -1, если ни одна соседняя суша не имеет назначенного региона (изолированное озеро).</summary>
        public static int MajorityLandRegion(List<VoronoiCell> component, Dictionary<int, VoronoiCell> cellById)
        {
            var regionVotes = new Dictionary<int, int>();
            foreach (var lakeCell in component)
            {
                foreach (var neighborId in lakeCell.NeighborIds)
                {
                    if (!cellById.TryGetValue(neighborId, out var neighbor)) continue;
                    if (neighbor.EffectiveIsLake || neighbor.EffectiveIsOcean) continue;
                    if (neighbor.RegionId < 0) continue;
                    regionVotes.TryGetValue(neighbor.RegionId, out var count);
                    regionVotes[neighbor.RegionId] = count + 1;
                }
            }
            if (regionVotes.Count == 0) return -1;
            return regionVotes.OrderByDescending(kv => kv.Value).First().Key;
        }

        /// <summary>Покрыто ли озеро кистью достаточно, чтобы переназначить его целиком:
        /// coveredCount / lakeCellCount >= thresholdPercent%. Целочисленно, без float.</summary>
        public static bool CoversLakeEnough(int coveredCount, int lakeCellCount, int thresholdPercent)
            => lakeCellCount > 0 && coveredCount * 100 >= lakeCellCount * thresholdPercent;

        /// <summary>Генерационный проход: каждое озеро → регион большинства соседей-суши.</summary>
        public static void UnifyLakes(List<VoronoiCell> cells)
        {
            var cellById = cells.ToDictionary(c => c.Id);
            var visited = new HashSet<int>();

            foreach (var startCell in cells)
            {
                if (!startCell.EffectiveIsLake || visited.Contains(startCell.Id)) continue;

                var component = FindLakeComponent(startCell.Id, cellById);
                foreach (var lc in component) visited.Add(lc.Id);

                int winnerRegion = MajorityLandRegion(component, cellById);
                if (winnerRegion < 0) continue; // изолированное озеро без сушных соседей - оставляем как есть
                foreach (var lakeCell in component)
                    lakeCell.RegionId = winnerRegion;
            }
        }
    }
}
