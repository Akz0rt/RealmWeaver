using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Вычисляет elevation на corners как гибрид двух компонентов:
    /// 1. distance-from-coast (BFS по corner-графу от океанских corners, как в оригинальном
    ///    Patel mapgen2) - даёт общую тенденцию "выше = дальше от берега"
    /// 2. шум (через FastNoiseLite без island falloff - чистый локальный шум) - позволяет
    ///    горам появляться и рядом с побережьем, если шум там даёт локальный всплеск
    ///
    /// Озёрные corners пропускаются при распространении BFS (вода "не несёт" расстояние дальше),
    /// что заставляет соседнюю с озером сушу иметь локальный минимум - озеро естественно
    /// формирует низину/долину вокруг себя, как и описано в оригинальной статье Patel.
    /// </summary>
    public static class ElevationField
    {
        public static void ApplyElevation(List<Corner> corners, float coastWeight, float noiseWeight,
                                            int noiseSeed, float noiseFrequency, int noiseOctaves)
        {
            var distanceFromCoast = ComputeDistanceFromCoast(corners);
            int maxDistance = distanceFromCoast.Values.DefaultIfEmpty(0).Max();
            if (maxDistance == 0) maxDistance = 1; // защита от деления на 0, если воды нет вообще

            // Чистый шум без island falloff - просто локальная вариация, без глобальной формы острова
            // (форма острова уже задана через distance-from-coast компонент).
            var noiseGen = new FastNoiseLite(noiseSeed + 2000); // отдельный сдвиг seed, чтобы не совпадать с island shape
            noiseGen.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            noiseGen.SetFractalType(FastNoiseLite.FractalType.FBm);
            noiseGen.SetFractalOctaves(noiseOctaves);
            noiseGen.SetFrequency(noiseFrequency);

            var cornerById = corners.ToDictionary(c => c.Id);

            foreach (var corner in corners)
            {
                if (corner.IsOcean)
                {
                    corner.Elevation = 0f;
                    continue;
                }

                float distanceComponent;
                if (corner.IsWater) // озеро - не участвовало в BFS, берём среднее по соседям на берегу
                {
                    var neighborDistances = corner.NeighborCornerIds
                        .Where(id => distanceFromCoast.ContainsKey(id))
                        .Select(id => distanceFromCoast[id])
                        .ToList();
                    int avgDist = neighborDistances.Count > 0 ? (int)neighborDistances.Average() : 0;
                    distanceComponent = (float)avgDist / maxDistance;
                }
                else
                {
                    int dist = distanceFromCoast.TryGetValue(corner.Id, out var d) ? d : 0;
                    distanceComponent = (float)dist / maxDistance;
                }

                float noiseRaw = noiseGen.GetNoise(corner.Position.X, corner.Position.Y); // [-1, 1]
                float noiseComponent = (noiseRaw + 1f) * 0.5f; // [0, 1]

                corner.Elevation = distanceComponent * coastWeight + noiseComponent * noiseWeight;
            }
        }

        /// <summary>Multi-source BFS от всех ocean corners. Lake corners пропускаются (не распространяют дальше) - см. комментарий класса.</summary>
        static Dictionary<int, int> ComputeDistanceFromCoast(List<Corner> corners)
        {
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();
            var cornerById = corners.ToDictionary(c => c.Id);

            foreach (var corner in corners)
            {
                if (corner.IsOcean)
                {
                    distance[corner.Id] = 0;
                    queue.Enqueue(corner.Id);
                }
            }

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                int currentDist = distance[currentId];
                var current = cornerById[currentId];

                foreach (var neighborId in current.NeighborCornerIds)
                {
                    if (distance.ContainsKey(neighborId)) continue;
                    var neighbor = cornerById[neighborId];

                    // Озёрные corners не участвуют в распространении - вода "глушит" расстояние,
                    // как у Patel ("I skipped lake polygons in the distance calculation").
                    if (neighbor.IsWater && !neighbor.IsOcean) continue;

                    distance[neighborId] = currentDist + 1;
                    queue.Enqueue(neighborId);
                }
            }

            return distance;
        }
    }
}
