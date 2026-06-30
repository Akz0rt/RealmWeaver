using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Находит связные группы lake corners (IsWater=true, IsOcean=false) через BFS по
    /// corner-графу, и "осушает" (переводит в IsWater=false) группы меньше minLakeSize -
    /// это убирает визуальный мусор из мелких случайных впадин, оставляя только
    /// достаточно крупные, осмысленные озёра. Прямой аналог старой клеточной системы
    /// MinLakeSize, перенесённый на corner-граф.
    ///
    /// ВАЖНО: вызывать ПОСЛЕ CornerOceanFloodFill.MarkOcean (нужно знать, какие water
    /// corners уже определены как океан, чтобы не трогать их) и ДО ElevationField/MoistureField
    /// (которые зависят от финального IsWater статуса).
    /// </summary>
    public static class LakeSizeFilter
    {
        public static void RemoveSmallLakes(List<Corner> corners, int minLakeSize)
        {
            var cornerById = corners.ToDictionary(c => c.Id);
            var visited = new HashSet<int>();

            foreach (var corner in corners)
            {
                if (corner.IsOcean) continue;
                if (!corner.IsWater) continue;
                if (visited.Contains(corner.Id)) continue;

                // BFS по связной группе lake corners.
                var group = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(corner.Id);
                visited.Add(corner.Id);

                while (queue.Count > 0)
                {
                    int currentId = queue.Dequeue();
                    group.Add(currentId);
                    var current = cornerById[currentId];

                    foreach (var neighborId in current.NeighborCornerIds)
                    {
                        if (visited.Contains(neighborId)) continue;
                        var neighbor = cornerById[neighborId];
                        if (neighbor.IsOcean) continue;
                        if (!neighbor.IsWater) continue;

                        visited.Add(neighborId);
                        queue.Enqueue(neighborId);
                    }
                }

                if (group.Count < minLakeSize)
                {
                    // Группа слишком мала - осушаем (делаем сушей).
                    foreach (var id in group)
                        cornerById[id].IsWater = false;
                }
            }
        }
    }
}
