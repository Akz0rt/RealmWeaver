using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Переносит land/water/ocean статус с corners на клетки. Клетка считается водой,
    /// если хотя бы половина (настраиваемая доля) её corners - вода; океаном - если
    /// хотя бы половина её водных corners - именно океан (а не озеро).
    /// Это прямая адаптация подхода Patel ("clipped corners determine the polygon"),
    /// перенесённая на структуру, где рендер работает с центрами клеток.
    /// </summary>
    public static class CellWaterAssigner
    {
        public static void AssignFromCorners(List<VoronoiCell> cells, List<Corner> corners, float waterFractionThreshold = 0.5f)
        {
            // Строим обратный индекс: для каждой клетки - список её corners (через TouchingCellIds на corner).
            var cornersByCell = new Dictionary<int, List<Corner>>();
            foreach (var corner in corners)
            {
                foreach (var cellId in corner.TouchingCellIds)
                {
                    if (!cornersByCell.TryGetValue(cellId, out var list))
                    {
                        list = new List<Corner>();
                        cornersByCell[cellId] = list;
                    }
                    list.Add(corner);
                }
            }

            var waterCellIds = new HashSet<int>();

            foreach (var cell in cells)
            {
                if (!cornersByCell.TryGetValue(cell.Id, out var cellCorners) || cellCorners.Count == 0)
                {
                    cell.IsOcean = false;
                    continue;
                }

                int waterCount = cellCorners.Count(c => c.IsWater);
                int oceanCount = cellCorners.Count(c => c.IsOcean);

                float waterFraction = (float)waterCount / cellCorners.Count;
                bool isWater = waterFraction >= waterFractionThreshold;

                if (isWater)
                {
                    waterCellIds.Add(cell.Id);
                    // Среди водных corners клетки - большинство океан или озеро?
                    float oceanFractionOfWater = waterCount > 0 ? (float)oceanCount / waterCount : 0f;
                    cell.IsOcean = oceanFractionOfWater >= 0.5f;
                }
                else
                {
                    cell.IsOcean = false;
                }

                // cell.Height пока не трогаем - не-океанские water клетки (озёра) будут
                // обработаны на следующем шаге (elevation), где IsLake выводится явно.
            }

            // Озеро, связанное с океаном через цепочку водных клеток, - это на самом деле часть моря,
            // а не отдельный внутренний водоём. Промоутим такую воду в океан на уровне клеток.
            PromoteOceanConnectedWater(cells, waterCellIds);
        }

        /// <summary>
        /// BFS от океанских клеток по соседям-водным клеткам: любая водная клетка, достижимая от
        /// океана через воду, становится океаном. Суша - стена. Настоящие внутренние озёра
        /// (вода, не связанная с океаном через воду) остаются озёрами (IsOcean = false).
        /// Вынесено отдельным public-методом, чтобы проверять логику изолированно.
        /// </summary>
        public static void PromoteOceanConnectedWater(List<VoronoiCell> cells, HashSet<int> waterCellIds)
        {
            var cellById = cells.ToDictionary(c => c.Id);
            var queue = new Queue<int>();

            foreach (var cell in cells)
                if (cell.IsOcean) queue.Enqueue(cell.Id);

            while (queue.Count > 0)
            {
                var cell = cellById[queue.Dequeue()];
                foreach (var neighborId in cell.NeighborIds)
                {
                    if (!cellById.TryGetValue(neighborId, out var neighbor)) continue;
                    if (neighbor.IsOcean) continue;          // уже океан
                    if (!waterCellIds.Contains(neighborId)) continue; // суша - стена, через неё океан не распространяется

                    neighbor.IsOcean = true;
                    queue.Enqueue(neighborId);
                }
            }
        }
    }
}
