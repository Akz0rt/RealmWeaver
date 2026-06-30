using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Разделяет water-corners на океан (связан с краем карты через BFS) и озёра
    /// (окружён сушей). Аналог OceanFloodFill, но работает на corner-графе вместо клеток -
    /// это первичный источник истины в Patel-системе.
    /// </summary>
    public static class CornerOceanFloodFill
    {
        public static void MarkOcean(List<Corner> corners, float mapWidth, float mapHeight, float edgeMargin = 1f)
        {
            foreach (var c in corners) c.IsOcean = false;

            var cornerById = corners.ToDictionary(c => c.Id);
            var queue = new Queue<int>();
            var visited = new HashSet<int>();

            foreach (var corner in corners)
            {
                if (!corner.IsWater) continue;

                bool touchesEdge = corner.Position.X <= edgeMargin || corner.Position.X >= mapWidth - edgeMargin
                                 || corner.Position.Y <= edgeMargin || corner.Position.Y >= mapHeight - edgeMargin;

                if (touchesEdge)
                {
                    corner.IsOcean = true;
                    queue.Enqueue(corner.Id);
                    visited.Add(corner.Id);
                }
            }

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                var current = cornerById[currentId];

                foreach (var neighborId in current.NeighborCornerIds)
                {
                    if (visited.Contains(neighborId)) continue;
                    var neighbor = cornerById[neighborId];
                    if (!neighbor.IsWater) continue;

                    neighbor.IsOcean = true;
                    visited.Add(neighborId);
                    queue.Enqueue(neighborId);
                }
            }

            // Water corners, не помеченные IsOcean - это озёра (IsWater=true, IsOcean=false).
        }
    }
}
