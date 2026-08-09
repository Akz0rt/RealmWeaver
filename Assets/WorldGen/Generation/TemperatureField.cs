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
    ///
    /// ЗДЕСЬ ЖЕ горы становятся холодными — и только здесь. Охлаждение с высотой вычитается из
    /// САМОЙ температуры в момент генерации, а не из «эффективной» в момент классификации биома
    /// (как было до 2026-08-09). Разница принципиальная и ради неё всё и переносилось: генерация
    /// обязана давать снежные пики, а КИСТЬ обязана давать ровно тот биом, который выбрали, — даже
    /// на пике. Пока охлаждение сидело в классификаторе, оно било по обоим: нарисованный биом
    /// уезжал к холодному соседу, и уезжал заново при каждом открытии проекта.
    ///
    /// Теперь у ДМ высокая гора просто ХОЛОДНАЯ — это видно в отладчике клетки, это лежит в файле,
    /// и это переопределяется обычной ручной температурой (TemperatureOverride), которую пишет
    /// кисть. Правка рельефа температуру не трогает: поднять гору кистью — не то же самое, что
    /// сгенерировать мир заново.
    /// </summary>
    public static class TemperatureField
    {
        const float Epsilon = 0.01f; // защита от деления на ноль, если клетка точно совпадает с позицией эпицентра

        /// <param name="elevationTempDrop">Сколько температуры снимает высота 1.0 (0.4 ≈ два уровня
        /// из пяти на пике). 0 — рельеф на температуру не влияет.</param>
        public static void ApplyTemperature(List<VoronoiCell> cells, List<TemperatureEpicenter> epicenters,
                                              float baseTemperature, float elevationTempDrop = 0f)
        {
            foreach (var cell in cells)
            {
                float baseTemp = ComputeBaseTemperature(cell.Site, epicenters, baseTemperature);
                // EffectiveElevation, а не Height: если ДМ поднял гору кистью и ПОСЛЕ этого просит
                // перегенерировать температуру, охлаждать надо по той высоте, что он видит.
                float cooled = baseTemp - elevationTempDrop * cell.EffectiveElevation;
                cell.Temperature = Math.Clamp(cooled, 0f, 1f);
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
