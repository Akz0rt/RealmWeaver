using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Применяет ручной override к произвольному набору клеток - будь то целая область
    /// или произвольная (не обязательно смежная) подобласть. Поддерживает два уровня
    /// переопределения, которые применяются в следующем порядке приоритета (от высшего к низшему):
    ///
    /// 1. ElevationOverride / WaterOverride - переопределение ландшафта, влияет на биом-классификацию
    /// 2. TemperatureOverride / MoistureOverride - переопределение климата, влияет на биом-классификацию
    /// 3. Computed values (Height, IsOcean, Temperature, Humidity) - baseline от генератора
    ///
    /// После применения любого override этот сервис автоматически пересчитывает cell.Biome
    /// по EffectiveTemperature/EffectiveMoisture/EffectiveElevation. Это значит, что cell.Biome
    /// всегда актуален и рендер обновится корректно после вызова WorldMapRenderer.RecolorOnly().
    /// </summary>
    public static class CellOverrideService
    {
        /// <summary>Карта-широкая константа высотного охлаждения биома (spec §2). Задаётся один раз
        /// при генерации (из GenerationParams.ElevationTempDrop) и при загрузке (из
        /// WorldMapRenderer.elevationTempDrop); читается RecomputeBiome. Ambient, чтобы не тащить
        /// параметр через все override-методы (у каждого биом всё равно один и тот же drop).</summary>
        public static float ElevationTempDrop = 0.4f;

        // --- Climate override ---

        /// <summary>
        /// Применяет override температуры/влажности к указанным клеткам.
        /// null для temperature/moisture = не трогать это поле.
        /// </summary>
        public static void ApplyClimateOverride(IEnumerable<VoronoiCell> targetCells,
                                                  float? temperature, float? moisture,
                                                  float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                if (temperature.HasValue) cell.TemperatureOverride = temperature.Value;
                if (moisture.HasValue) cell.MoistureOverride = moisture.Value;
                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        /// <summary>Снимает climate override (по отдельности температуру и/или влажность) с указанных клеток.</summary>
        public static void ClearClimateOverride(IEnumerable<VoronoiCell> targetCells,
                                                  bool clearTemperature, bool clearMoisture,
                                                  float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                if (clearTemperature) cell.TemperatureOverride = null;
                if (clearMoisture) cell.MoistureOverride = null;
                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        // --- Landscape override ---

        /// <summary>
        /// Применяет override elevation к указанным клеткам.
        /// null для elevation = снять elevation override (вернуть к computed).
        /// </summary>
        public static void ApplyElevationOverride(IEnumerable<VoronoiCell> targetCells,
                                                    float? elevation,
                                                    float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                cell.ElevationOverride = elevation; // null корректно снимает override
                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        /// <summary>
        /// Применяет override water-статуса к указанным клеткам. Дополнительно подстраивает
        /// ElevationOverride (если он не был явно задан пользователем ранее), чтобы клетка
        /// визуально и физически "утонула"/"всплыла" вместе со сменой water-статуса - без этого
        /// клетка осталась бы со старым elevation (например, гора), но закрашенной как вода,
        /// что выглядит некорректно.
        /// WaterOverrideType.None = снять water override (вернуть к corner-graph статусу).
        /// </summary>
        public static void ApplyWaterOverride(IEnumerable<VoronoiCell> targetCells,
                                               WaterOverrideType waterType,
                                               float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                cell.WaterOverride = waterType;

                // Автоматическая подстройка elevation - только если elevation НЕ был явно
                // переопределён пользователем отдельно (не перетираем чужой explicit override).
                switch (waterType)
                {
                    case WaterOverrideType.ForceOcean:
                    case WaterOverrideType.ForceLake:
                        if (!cell.ElevationOverride.HasValue)
                            cell.ElevationOverride = 0f; // топим клетку - elevation 0 однозначно ниже любого порога пляжа
                        break;

                    case WaterOverrideType.ForceLand:
                        if (!cell.ElevationOverride.HasValue || cell.ElevationOverride.Value < beachElevationThreshold + 0.05f)
                            cell.ElevationOverride = beachElevationThreshold + 0.1f; // поднимаем чуть выше порога пляжа
                        break;

                    case WaterOverrideType.None:
                        // Снятие water override не трогает elevation override - пользователь мог
                        // намеренно задать elevation отдельно и не хочет, чтобы оно сбрасывалось.
                        break;
                }

                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        /// <summary>Снимает ВСЕ override (climate + landscape) с указанных клеток - полный сброс к computed.</summary>
        public static void ClearAllOverrides(IEnumerable<VoronoiCell> targetCells, float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                cell.TemperatureOverride = null;
                cell.MoistureOverride = null;
                cell.ElevationOverride = null;
                cell.WaterOverride = WaterOverrideType.None;
                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        // --- Relative ("кисть") изменения - применяются к ОДНОЙ клетке за раз, прибавляют
        // delta к текущему ЭФФЕКТИВНОМУ значению (учитывает уже применённый override, если есть) ---
        //
        // Биом всегда производный: кисть высоты/температуры/влажности меняет climate/landscape
        // override, а RecomputeBiome пересчитывает cell.Biome под новые значения.

        /// <summary>Прибавляет delta к текущей эффективной elevation клетки (clamped [0,1]).</summary>
        public static void AdjustElevation(VoronoiCell cell, float delta, float beachElevationThreshold)
        {
            float current = cell.EffectiveElevation;
            cell.ElevationOverride = System.Math.Clamp(current + delta, 0f, 1f);
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Steps the cell's temperature by ±1 level (dir = +1/−1), snapping to the level band-center.</summary>
        public static void StepTemperatureLevel(VoronoiCell cell, int dir, float beachElevationThreshold)
        {
            int lvl = System.Math.Clamp(BiomeMatrix.Level5(cell.EffectiveTemperature) + dir, 0, 4);
            cell.TemperatureOverride = BiomeMatrix.LevelCenter(lvl);
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Steps the cell's moisture by ±1 level (dir = +1/−1), snapping to the level band-center.</summary>
        public static void StepMoistureLevel(VoronoiCell cell, int dir, float beachElevationThreshold)
        {
            int lvl = System.Math.Clamp(BiomeMatrix.Level5(cell.EffectiveMoisture) + dir, 0, 4);
            cell.MoistureOverride = BiomeMatrix.LevelCenter(lvl);
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Absolutely sets temperature and/or moisture to a level's band-center (null = leave axis
        /// untouched). Used by the biome brush, the 5×5 palette, and the precise-selection panel.</summary>
        public static void SetClimateLevels(VoronoiCell cell, int? tempLevel, int? moistLevel, float beachElevationThreshold)
        {
            if (tempLevel.HasValue)  cell.TemperatureOverride = BiomeMatrix.LevelCenter(tempLevel.Value);
            if (moistLevel.HasValue) cell.MoistureOverride    = BiomeMatrix.LevelCenter(moistLevel.Value);
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Enumerable overload for precise-selection edits over a whole selection.</summary>
        public static void SetClimateLevels(IEnumerable<VoronoiCell> cells, int? tempLevel, int? moistLevel, float beachElevationThreshold)
        {
            foreach (var cell in cells) SetClimateLevels(cell, tempLevel, moistLevel, beachElevationThreshold);
        }

        /// <summary>
        /// Пересчитывает cell.Biome классификацией по EffectiveTemperature/EffectiveMoisture/
        /// EffectiveElevation/EffectiveIsOcean/EffectiveIsLake. Вызывается автоматически после
        /// каждого применения/снятия любого override.
        /// </summary>
        public static void RecomputeBiome(VoronoiCell cell, float beachElevationThreshold)
        {
            cell.Biome = BiomeClassifier.Classify(
                cell.EffectiveTemperature,
                cell.EffectiveMoisture,
                cell.EffectiveElevation,
                ElevationTempDrop,
                cell.EffectiveIsOcean,
                cell.EffectiveIsLake,
                beachElevationThreshold
            );
        }

        /// <summary>Классифицирует биом для каждой клетки. Используется
        /// генерацией/загрузкой/перегенерацией температуры ПОСЛЕ того, как температура посчитана.
        /// beachElevationThreshold=0 при генерации/загрузке - пляжи назначает BeachClassifier далее.</summary>
        public static void ClassifyAll(System.Collections.Generic.IEnumerable<VoronoiCell> cells, float beachElevationThreshold)
        {
            foreach (var cell in cells)
                RecomputeBiome(cell, beachElevationThreshold);
        }
    }
}
