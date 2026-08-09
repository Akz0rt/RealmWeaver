using System;

namespace WorldGen.Generation
{
    /// <summary>Каталог биомов. Вода (Ocean/Lake/Beach) — вне матрицы; далее 18 сухопутных типов,
    /// получаемых из 5×5 матрицы температура×влажность (см. BiomeMatrix).</summary>
    public enum Biome
    {
        Ocean, Lake, Beach,
        IceWaste, Tundra, Snow, Glacier,                 // Ледяная пустошь, Тундра, Снега, Ледники
        ColdSteppe, ForestTundra, Taiga, ConiferForest,  // Холодная степь, Лесотундра, Тайга, Хвойный лес
        Steppe, Grassland, Forest, RainForest,           // Степь, Луга, Лес, Дождевой лес
        SemiDesert, Shrubland, Savanna, WarmForest,      // Полупустыня, Кустарники, Саванна, Тёплый лес
        Desert, TropicalForest                           // Пустыня, Тропический лес
    }

    /// <summary>Определяет биом из матрицы температура×влажность (BiomeMatrix). Высоту НЕ смотрит:
    /// раньше она охлаждала эффективную температуру прямо здесь (effTemp = temperature −
    /// elevationTempDrop·elevation) и сдвигала биом к холодному соседу — из-за чего кисть биома на
    /// горе давала не тот биом, который выбрали, да ещё и заново уезжала при каждом открытии
    /// проекта (биом производный, он пересчитывается при загрузке).
    ///
    /// Горы от этого тёплыми не стали: охлаждение переехало в ГЕНЕРАЦИЮ и вычитается из самой
    /// температуры клетки (см. TemperatureField.ApplyTemperature). Так снежные пики генерируются
    /// как раньше, а нарисованное кистью остаётся нарисованным.
    ///
    /// Единственная оставшаяся связь с высотой — пляж: клетка ниже beachElevationThreshold остаётся
    /// берегом (машинерия побережья, см. BeachClassifier). Вода/пляж определяются до матрицы.
    /// Чистая функция.</summary>
    public static class BiomeClassifier
    {
        public static Biome Classify(float temperature, float moisture, float elevation,
                                     bool isOcean, bool isLake,
                                     float beachElevationThreshold = 0.1f)
        {
            if (isOcean) return Biome.Ocean;
            if (isLake) return Biome.Lake;
            if (elevation < beachElevationThreshold) return Biome.Beach;

            float temp = Math.Clamp(temperature, 0f, 1f);
            return BiomeMatrix.Get(BiomeMatrix.Level5(temp), BiomeMatrix.Level5(moisture));
        }
    }
}
