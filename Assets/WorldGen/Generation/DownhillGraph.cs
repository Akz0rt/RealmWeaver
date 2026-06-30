using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Для каждого corner находит соседа с наименьшей elevation - "куда потечёт вода".
    /// Используется для трассировки рек (RiverTracer) и в будущем может быть полезен
    /// для watershed-анализа (Patel упоминает это как возможное направление развития).
    /// </summary>
    public static class DownhillGraph
    {
        /// <summary>Возвращает словарь corner.Id -> Id соседа с наименьшей elevation (downhill-направление), или null если corner уже локальный минимум (ниже всех соседей).</summary>
        public static Dictionary<int, int?> ComputeDownhill(List<Corner> corners)
        {
            var cornerById = corners.ToDictionary(c => c.Id);
            var downhill = new Dictionary<int, int?>();

            foreach (var corner in corners)
            {
                if (corner.IsOcean)
                {
                    downhill[corner.Id] = null; // океан - конечная точка, дальше течь некуда
                    continue;
                }

                int? bestNeighborId = null;
                float bestElevation = corner.Elevation;

                foreach (var neighborId in corner.NeighborCornerIds)
                {
                    var neighbor = cornerById[neighborId];
                    if (neighbor.Elevation < bestElevation)
                    {
                        bestElevation = neighbor.Elevation;
                        bestNeighborId = neighborId;
                    }
                }

                downhill[corner.Id] = bestNeighborId; // null, если corner - локальный минимум (озеро без явного стока, например)
            }

            return downhill;
        }
    }
}
