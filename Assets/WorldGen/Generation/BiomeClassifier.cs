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

    /// <summary>Определяет биом из матрицы температура×влажность (BiomeMatrix). Высота охлаждает
    /// ЭФФЕКТИВНУЮ температуру в момент классификации (spec §2): effTemp = temperature −
    /// elevationTempDrop·elevation, что сдвигает биом к более холодному соседу по матрице. Вода/пляж
    /// определяются до матрицы. Чистая функция (drop передаётся явно — тестируемо).</summary>
    public static class BiomeClassifier
    {
        public static Biome Classify(float temperature, float moisture, float elevation,
                                     float elevationTempDrop, bool isOcean, bool isLake,
                                     float beachElevationThreshold = 0.1f)
        {
            if (isOcean) return Biome.Ocean;
            if (isLake) return Biome.Lake;
            if (elevation < beachElevationThreshold) return Biome.Beach;

            float effTemp = Math.Clamp(temperature - elevationTempDrop * elevation, 0f, 1f);
            return BiomeMatrix.Get(BiomeMatrix.Level5(effTemp), BiomeMatrix.Level5(moisture));
        }
    }
}
