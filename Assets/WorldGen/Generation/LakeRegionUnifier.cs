using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// После RegionGrowing все озёрные клетки уже имеют RegionId, но связное озеро может
    /// оказаться разрезано на части разных регионов, если его клетки были ближе к разным сидам.
    /// Этот шаг находит связные компоненты озёрных клеток и переназначает все клетки компоненты
    /// в один регион (по большинству голосов соседних сушных клеток).
    /// Результат: граница региона проходит по внешней кромке озера, а не сквозь него.
    /// </summary>
    public static class LakeRegionUnifier
    {
        public static void UnifyLakes(List<VoronoiCell> cells)
        {
            var cellById = cells.ToDictionary(c => c.Id);
            var visited = new HashSet<int>();

            foreach (var startCell in cells)
            {
                if (!startCell.EffectiveIsLake || visited.Contains(startCell.Id)) continue;

                // BFS - собираем связную компоненту озёрных клеток.
                var component = new List<VoronoiCell>();
                var queue = new Queue<int>();
                queue.Enqueue(startCell.Id);
                visited.Add(startCell.Id);

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

                // Считаем голоса: каждая соседняя сушная клетка голосует своим RegionId.
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

                if (regionVotes.Count == 0) continue; // изолированное озеро без сушных соседей - оставляем как есть

                int winnerRegion = regionVotes.OrderByDescending(kv => kv.Value).First().Key;
                foreach (var lakeCell in component)
                    lakeCell.RegionId = winnerRegion;
            }
        }
    }
}
