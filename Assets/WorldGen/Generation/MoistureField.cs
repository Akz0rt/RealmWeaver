using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Вычисляет moisture на corners как убывающую функцию BFS-расстояния от свежей воды
    /// (озёра И реки - не океан, как у Patel: "moisture decreases as distance from fresh water
    /// increases"), плюс АДДИТИВНУЮ поправку от point-based эпицентров влажности.
    ///
    /// ВАЖНО: redistribution (ValueRedistributor.RedistributeMoisture) НЕ применяется здесь -
    /// он принудительно растягивает распределение на равное количество сухих/влажных клеток
    /// независимо от реальной физической влажности карты, что создаёт искусственные пустыни
    /// даже когда вся карта должна быть одинаково влажной (см. обсуждение в чате). Эпицентры
    /// дают более осмысленный, географически обоснованный контроль над разнообразием влажности.
    /// </summary>
    public static class MoistureField
    {
        const float EpsilonIDW = 0.01f;

        /// <summary>riverCornerIds - опциональное множество corner.Id, через которые проходит хотя бы одна река (см. RiverFlowAccumulator.GetRiverCornerIds) - добавляются как дополнительные источники BFS наряду с озёрами.</summary>
        public static void ApplyMoisture(List<Corner> corners, float falloffDistance, List<MoistureEpicenter> epicenters = null, HashSet<int> riverCornerIds = null)
        {
            var distanceFromFreshWater = ComputeDistanceFromFreshWater(corners, riverCornerIds);

            foreach (var corner in corners)
            {
                if (corner.IsOcean)
                {
                    corner.Moisture = 0f; // у океана своя роль - влажность считаем только для суши/озёр
                    continue;
                }

                float baseMoisture;
                int dist = distanceFromFreshWater.TryGetValue(corner.Id, out var d) ? d : int.MaxValue;
                if (dist == int.MaxValue)
                    baseMoisture = 0f; // недостижим от свежей воды через граф - максимально сухо по умолчанию
                else
                    baseMoisture = 1f - System.Math.Clamp(dist / falloffDistance, 0f, 1f);

                float epicenterDelta = ComputeEpicenterContribution(corner.Position, epicenters);

                corner.Moisture = System.Math.Clamp(baseMoisture + epicenterDelta, 0f, 1f);
            }
        }

        /// <summary>Суммарная поправка от всех эпицентров, в радиус которых попадает точка - IDW-взвешенная сумма, с hard cutoff по Radius каждого эпицентра.</summary>
        static float ComputeEpicenterContribution(System.Numerics.Vector2 position, List<MoistureEpicenter> epicenters)
        {
            if (epicenters == null || epicenters.Count == 0) return 0f;

            float weightedSum = 0f;
            float totalWeight = 0f;

            foreach (var epicenter in epicenters)
            {
                float distance = System.Numerics.Vector2.Distance(position, epicenter.Position);
                if (distance > epicenter.Radius) continue; // hard cutoff

                float weight = 1f / (distance * distance + EpsilonIDW);
                weightedSum += epicenter.MoistureDelta * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f) return 0f;
            return weightedSum / totalWeight;
        }

        /// <summary>Multi-source BFS от озёрных corners И речных corners (если переданы) - "свежая вода".</summary>
        static Dictionary<int, int> ComputeDistanceFromFreshWater(List<Corner> corners, HashSet<int> riverCornerIds)
        {
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();
            var cornerById = corners.ToDictionary(c => c.Id);

            foreach (var corner in corners)
            {
                bool isLake = corner.IsWater && !corner.IsOcean;
                bool isRiver = riverCornerIds != null && riverCornerIds.Contains(corner.Id) && !corner.IsOcean;

                if (isLake || isRiver)
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
                    if (neighbor.IsOcean) continue; // океан не передаёт "свежую" влажность дальше

                    distance[neighborId] = currentDist + 1;
                    queue.Enqueue(neighborId);
                }
            }

            return distance;
        }
    }
}
