using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Группирует M клеток Voronoi-диаграммы в N областей (регионов) через multi-source BFS
    /// с приоритетом по "стоимости" перехода между соседними клетками. Стоимость растёт
    /// с разницей высот - это заставляет регион расти преимущественно по равнинам и
    /// "упираться" в горные хребты, что даёт более естественные, нарративно логичные границы.
    /// </summary>
    public static class RegionGrowing
    {
        /// <summary>
        /// allCells - полный список (нужен для доступа по Id через индекс, включая океанские клетки -
        /// они участвуют как "стена", через которую рост региона не проходит).
        /// landCells - только клетки-кандидаты на регион (например, исключая океан) - именно из них
        /// выбираются seed-клетки и для них в итоге будет назначен RegionId.
        /// </summary>
        public static void GroupCells(List<VoronoiCell> allCells, List<VoronoiCell> landCells, int numberOfRegions, int seed, float heightPenaltyWeight = 10f)
        {
            if (numberOfRegions <= 0) throw new ArgumentException("numberOfRegions должно быть больше 0.");
            if (numberOfRegions > landCells.Count) throw new ArgumentException("numberOfRegions не может быть больше количества клеток суши.");

            var rng = new Random(seed);
            var cellById = allCells.ToDictionary(c => c.Id);

            foreach (var cell in landCells) cell.RegionId = -1;

            var seedCells = PickWellSpacedSeeds(landCells, numberOfRegions, rng);
            for (int i = 0; i < seedCells.Count; i++)
                seedCells[i].RegionId = i;

            var pq = new PriorityQueue<(int cellId, int regionId), float>();

            foreach (var seedCell in seedCells)
                foreach (var neighborId in seedCell.NeighborIds)
                    EnqueueIfUnassignedLand(cellById, pq, neighborId, seedCell.RegionId, seedCell, heightPenaltyWeight);

            while (pq.Count > 0)
            {
                var (cellId, regionId) = pq.Dequeue();
                var cell = cellById[cellId];

                if (cell.RegionId != -1) continue; // уже занята другим регионом, который добрался раньше
                if (cell.IsOcean) continue;          // защита - в очередь океанские клетки не попадают, но проверка не лишняя

                cell.RegionId = regionId;
                foreach (var neighborId in cell.NeighborIds)
                    EnqueueIfUnassignedLand(cellById, pq, neighborId, regionId, cell, heightPenaltyWeight);
            }

            // Защита: в редких случаях (сильно несвязный граф соседства, например материк
            // распался на несвязные острова) часть клеток суши может остаться без региона.
            AssignOrphanCells(landCells);
        }

        static void EnqueueIfUnassignedLand(Dictionary<int, VoronoiCell> cellById, PriorityQueue<(int, int), float> pq,
                                          int neighborId, int regionId, VoronoiCell from, float heightPenaltyWeight)
        {
            var neighbor = cellById[neighborId];
            if (neighbor.IsOcean) return;       // регион никогда не растёт через океан
            if (neighbor.RegionId != -1) return;
            float cost = MathF.Abs(neighbor.Height - from.Height) * heightPenaltyWeight;
            pq.Enqueue((neighborId, regionId), cost);
        }

        static List<VoronoiCell> PickWellSpacedSeeds(List<VoronoiCell> cells, int n, Random rng)
        {
            var seeds = new List<VoronoiCell>();
            var shuffled = cells.OrderBy(_ => rng.Next()).ToList();

            // Минимальная дистанция между seed-клетками выводится из площади карты и количества регионов,
            // чтобы не быть привязанной к конкретным магическим числам под один размер карты.
            float approxMapArea = EstimateMapArea(cells);
            float idealSpacing = MathF.Sqrt(approxMapArea / n);
            float minDistSq = idealSpacing * idealSpacing * 0.5f;

            foreach (var c in shuffled)
            {
                if (seeds.Count >= n) break;
                bool farEnough = seeds.All(s => System.Numerics.Vector2.DistanceSquared(s.Site, c.Site) > minDistSq);
                if (farEnough) seeds.Add(c);
            }

            // Fallback - если geometric spacing не позволил набрать N seed'ов (бывает при малом N относительно
            // формы карты), просто доливаем оставшиеся без проверки дистанции.
            int fallbackIndex = 0;
            while (seeds.Count < n && fallbackIndex < shuffled.Count)
            {
                if (!seeds.Contains(shuffled[fallbackIndex]))
                    seeds.Add(shuffled[fallbackIndex]);
                fallbackIndex++;
            }

            return seeds;
        }

        static float EstimateMapArea(List<VoronoiCell> cells)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in cells)
            {
                minX = MathF.Min(minX, c.Site.X);
                minY = MathF.Min(minY, c.Site.Y);
                maxX = MathF.Max(maxX, c.Site.X);
                maxY = MathF.Max(maxY, c.Site.Y);
            }
            return (maxX - minX) * (maxY - minY);
        }

        static void AssignOrphanCells(List<VoronoiCell> landCells)
        {
            var cellById = landCells.ToDictionary(c => c.Id);
            var orphans = landCells.Where(c => c.RegionId == -1).ToList();
            int guard = 0;
            while (orphans.Count > 0 && guard < landCells.Count)
            {
                foreach (var orphan in orphans.ToList())
                {
                    var assignedNeighbor = orphan.NeighborIds
                        .Where(id => cellById.ContainsKey(id))
                        .Select(id => cellById[id])
                        .FirstOrDefault(n => n.RegionId != -1);

                    if (assignedNeighbor != null)
                    {
                        orphan.RegionId = assignedNeighbor.RegionId;
                        orphans.Remove(orphan);
                    }
                }
                guard++;
            }
        }
    }
}
