using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Вычисляет температуру клетки через inverse distance weighting (IDW) от набора
    /// температурных эпицентров, с hard cutoff по радиусу каждого эпицентра (за пределами
    /// своего Radius эпицентр не вносит вклад в клетку вообще).
    ///
    /// Дополнительно учитывает высоту - горы холоднее (адиабатическое охлаждение),
    /// штраф растёт линейно выше уровня моря.
    /// </summary>
    public static class TemperatureField
    {
        const float Epsilon = 0.01f; // защита от деления на ноль, если клетка точно совпадает с позицией эпицентра

        public static void ApplyTemperature(List<VoronoiCell> cells, List<TemperatureEpicenter> epicenters,
                                              float baseTemperature, float heightCoolingFactor, float seaLevel)
        {
            foreach (var cell in cells)
            {
                float baseTemp = ComputeBaseTemperature(cell.Site, epicenters, baseTemperature);

                // Высота выше уровня моря охлаждает - чем выше горы, тем холоднее, независимо от эпицентров.
                float heightAboveSea = MathF.Max(0f, cell.Height - seaLevel);
                float coolingFromHeight = heightAboveSea * heightCoolingFactor;

                cell.Temperature = Math.Clamp(baseTemp - coolingFromHeight, 0f, 1f);
            }
        }

        static float ComputeBaseTemperature(Vector2 cellPosition, List<TemperatureEpicenter> epicenters, float fallbackTemperature)
        {
            if (epicenters == null || epicenters.Count == 0)
                return fallbackTemperature;

            float weightedSum = 0f;
            float totalWeight = 0f;

            foreach (var epicenter in epicenters)
            {
                float distance = Vector2.Distance(cellPosition, epicenter.Position);
                if (distance > epicenter.Radius) continue; // hard cutoff - эпицентр не влияет за пределами своего радиуса

                float weight = 1f / (distance * distance + Epsilon);
                weightedSum += epicenter.Temperature * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
                return fallbackTemperature; // клетка не попала в радиус ни одного эпицентра

            return weightedSum / totalWeight;
        }
    }
}
