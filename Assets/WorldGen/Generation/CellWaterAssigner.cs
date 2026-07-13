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
                    // Хотя бы один ocean-corner → клетка граничит с открытым морем → это океан.
                    // Inland lake corners никогда не бывают ocean (flood fill идёт от края карты),
                    // поэтому oceanCount > 0 безопасно не затрагивает настоящие внутренние озёра.
                    cell.IsOcean = oceanCount > 0;
                }
                else
                {
                    cell.IsOcean = false;
                }

            }

            // Озеро, связанное с океаном через цепочку водных клеток, - это на самом деле часть моря,
            // а не отдельный внутренний водоём. Промоутим такую воду в океан на уровне клеток.
            PromoteOceanConnectedWater(cells, waterCellIds);

            // Оставшаяся вода (в waterCellIds, но не ставшая океаном) - настоящие внутренние озёра.
            // Помечаем их Biome.Lake ЗДЕСЬ, у источника water-статуса. Идентичность озера
            // (VoronoiCell.EffectiveIsLake читает Biome==Lake) должна быть задана ДО классификации
            // биома, которая теперь идёт отдельным поздним проходом (CellOverrideService.ClassifyAll,
            // после температуры). Без этого озёра классифицировались бы как суша.
            MarkLakes(cells, waterCellIds);
        }

        /// <summary>Помечает внутренние озёра: водную клетку, оставшуюся не-океаном после
        /// PromoteOceanConnectedWater. Устанавливает cell.Biome = Biome.Lake, задавая lake-идентичность
        /// до классификации биома. Public для изолированного self-test.</summary>
        public static void MarkLakes(List<VoronoiCell> cells, HashSet<int> waterCellIds)
        {
            foreach (var cell in cells)
                if (!cell.IsOcean && waterCellIds.Contains(cell.Id))
                    cell.Biome = Biome.Lake;
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

        /// <summary>Effective-aware: BFS от океана по воде (EffectiveIsOcean/EffectiveIsLake).
        /// Возвращает клетки-озёра (EffectiveIsLake), достижимые от океана через воду — на самом деле
        /// часть моря. Суша — стена. НЕ мутирует: вызывающий пишет ForceOcean + снимок undo.
        /// Та же семантика, что и генерационный PromoteOceanConnectedWater, но по эффективным статусам
        /// и для промоушена уже существующих озёр (кисть воды соединила озеро с океаном).</summary>
        public static List<VoronoiCell> FindOceanConnectedLakes(List<VoronoiCell> cells)
        {
            var cellById = cells.ToDictionary(c => c.Id);
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            var result = new List<VoronoiCell>();

            foreach (var cell in cells)
                if (cell.EffectiveIsOcean) { visited.Add(cell.Id); queue.Enqueue(cell.Id); }

            while (queue.Count > 0)
            {
                var cell = cellById[queue.Dequeue()];
                foreach (var neighborId in cell.NeighborIds)
                {
                    if (visited.Contains(neighborId)) continue;
                    if (!cellById.TryGetValue(neighborId, out var neighbor)) continue;
                    bool isWater = neighbor.EffectiveIsOcean || neighbor.EffectiveIsLake;
                    if (!isWater) continue;               // суша — стена
                    visited.Add(neighborId);
                    queue.Enqueue(neighborId);
                    if (neighbor.EffectiveIsLake) result.Add(neighbor); // озеро, связанное с морем
                }
            }
            return result;
        }
    }
}
