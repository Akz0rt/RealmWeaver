using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Вычисляет температуру клетки через inverse distance weighting (IDW) от набора
    /// температурных эпицентров. Radius каждого эпицентра теперь задаёт СИЛУ/ширину его влияния
    /// (мягкий глобальный спад, без hard cutoff), так что каждый эпицентр влияет на всю карту и
    /// не остаётся однородных fallback-областей.
    /// </summary>
    public static class TemperatureField
    {
        const float Epsilon = 0.01f; // защита от деления на ноль, если клетка точно совпадает с позицией эпицентра

        public static void ApplyTemperature(List<VoronoiCell> cells, List<TemperatureEpicenter> epicenters,
                                              float baseTemperature)
        {
            foreach (var cell in cells)
            {
                // Региональная температура (только эпицентры). Высотное охлаждение перенесено в
                // BiomeClassifier (spec §2), поэтому здесь Height больше не учитывается.
                float baseTemp = ComputeBaseTemperature(cell.Site, epicenters, baseTemperature);
                cell.Temperature = Math.Clamp(baseTemp, 0f, 1f);
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
                // Мягкое глобальное влияние (без hard cutoff): каждый эпицентр влияет на КАЖДУЮ
                // клетку, а Radius задаёт СИЛУ/ширину влияния (больше радиус — шире тёплая/холодная
                // зона). Так на карте не остаётся однородных "fallback 0.5" областей — климат реально
                // меняется по карте, ближайший эпицентр доминирует, между ними — плавный переход.
                float distance = Vector2.Distance(cellPosition, epicenter.Position);
                float weight = epicenter.Radius / (distance * distance + Epsilon);
                weightedSum += epicenter.Temperature * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
                return fallbackTemperature; // недостижимо при >=1 эпицентре с Radius>0 — на всякий случай
            return weightedSum / totalWeight;
        }
    }
}
