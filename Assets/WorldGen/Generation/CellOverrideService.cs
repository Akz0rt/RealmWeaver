using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Применяет ручной override к произвольному набору клеток - будь то целая область
    /// или произвольная (не обязательно смежная) подобласть. Поддерживает три уровня
    /// переопределения, которые применяются в следующем порядке приоритета (от высшего к низшему):
    ///
    /// 1. BiomeOverride  - прямое переопределение биома, перекрывает всё ниже
    /// 2. ElevationOverride / WaterOverride - переопределение ландшафта, влияет на биом-классификацию
    /// 3. TemperatureOverride / MoistureOverride - переопределение климата, влияет на биом-классификацию
    /// 4. Computed values (Height, IsOcean, Temperature, Humidity) - baseline от генератора
    ///
    /// После применения любого override этот сервис автоматически пересчитывает cell.Biome
    /// через полный стек приоритетов. Это значит, что cell.Biome всегда актуален и рендер
    /// обновится корректно после вызова WorldMapRenderer.RecolorOnly().
    /// </summary>
    public static class CellOverrideService
    {
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

        /// <summary>
        /// Применяет прямой override биома к указанным клеткам - высший приоритет, перекрывает всё.
        /// null для biome = снять biome override (биом вычисляется по стеку ниже).
        /// </summary>
        public static void ApplyBiomeOverride(IEnumerable<VoronoiCell> targetCells,
                                               Biome? biome,
                                               float beachElevationThreshold)
        {
            foreach (var cell in targetCells)
            {
                cell.BiomeOverride = biome;
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
                cell.BiomeOverride = null;
                RecomputeBiome(cell, beachElevationThreshold);
            }
        }

        // --- Relative ("кисть") изменения - применяются к ОДНОЙ клетке за раз, прибавляют
        // delta к текущему ЭФФЕКТИВНОМУ значению (учитывает уже применённый override, если есть) ---
        //
        // Кисть высоты/температуры/влажности снимает жёсткий BiomeOverride (если он был поставлен
        // кистью-биомом или через панель), чтобы биом снова вычислялся по физике: пользователь может
        // закрасить биом, а потом "поводить" по нему кистью параметров - и биом пересчитается под них.
        // Снятое значение сохраняется в Undo-снимке мазка (CellSnapshot), так что Ctrl+Z его вернёт.

        /// <summary>Прибавляет delta к текущей эффективной elevation клетки (clamped [0,1]); снимает biome-override.</summary>
        public static void AdjustElevation(VoronoiCell cell, float delta, float beachElevationThreshold)
        {
            float current = cell.EffectiveElevation;
            cell.ElevationOverride = System.Math.Clamp(current + delta, 0f, 1f);
            cell.BiomeOverride = null;
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Прибавляет delta к текущей эффективной температуре клетки (clamped [0,1]); снимает biome-override.</summary>
        public static void AdjustTemperature(VoronoiCell cell, float delta, float beachElevationThreshold)
        {
            float current = cell.EffectiveTemperature;
            cell.TemperatureOverride = System.Math.Clamp(current + delta, 0f, 1f);
            cell.BiomeOverride = null;
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Прибавляет delta к текущей эффективной влажности клетки (clamped [0,1]); снимает biome-override.</summary>
        public static void AdjustMoisture(VoronoiCell cell, float delta, float beachElevationThreshold)
        {
            float current = cell.EffectiveMoisture;
            cell.MoistureOverride = System.Math.Clamp(current + delta, 0f, 1f);
            cell.BiomeOverride = null;
            RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>
        /// Пересчитывает cell.Biome через полный стек приоритетов:
        /// BiomeOverride (если задан) > классификация по EffectiveElevation/EffectiveIsOcean/EffectiveIsLake/EffectiveMoisture.
        /// Вызывается автоматически после каждого применения/снятия любого override.
        /// </summary>
        public static void RecomputeBiome(VoronoiCell cell, float beachElevationThreshold)
        {
            if (cell.BiomeOverride.HasValue)
            {
                cell.Biome = cell.BiomeOverride.Value;
                return;
            }

            cell.Biome = BiomeClassifier.Classify(
                cell.EffectiveElevation,
                cell.EffectiveMoisture,
                cell.EffectiveIsOcean,
                cell.EffectiveIsLake,
                beachElevationThreshold
            );
        }
    }
}
