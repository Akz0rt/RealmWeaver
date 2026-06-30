namespace WorldGen.Generation
{
    public enum Biome
    {
        Ocean,
        Lake,
        Beach,
        Snow,
        Tundra,
        Bare,
        Scorched,
        Taiga,
        Shrubland,
        TemperateDesert,
        TemperateRainForest,
        TemperateDeciduousForest,
        Grassland,
        TropicalRainForest,
        TropicalSeasonalForest,
        SubtropicalDesert
    }

    /// <summary>
    /// Определяет биом через точную Whittaker-таблицу 4x6 из Amit Patel's mapgen2
    /// (elevation x moisture, оба в [0,1] после redistribution). Биомы воды/пляжа определяются
    /// отдельно, до этой таблицы.
    ///
    /// Принимает elevation/moisture/isOcean/isLake как отдельные параметры (не сам Corner),
    /// чтобы один и тот же метод можно было использовать как для corner (напрямую), так и
    /// для клетки (с усреднёнными по corners значениями) - см. CellClimateAverager.
    /// </summary>
    public static class BiomeClassifier
    {
        public static Biome Classify(float elevation, float moisture, bool isOcean, bool isLake, float beachElevationThreshold = 0.1f)
        {
            if (isOcean) return Biome.Ocean;
            if (isLake) return Biome.Lake;
            if (elevation < beachElevationThreshold) return Biome.Beach;

            return ClassifyByWhittaker(elevation, moisture);
        }

        /// <summary>
        /// Таблица 4x6 из оригинальной статьи. Elevation разбит на 4 зоны (1=низко..4=высоко),
        /// Moisture на 6 зон (1=сухо..6=влажно) - именно такая гранулярность сохранена из
        /// оригинала, а не упрощена до своей собственной шкалы.
        /// </summary>
        static Biome ClassifyByWhittaker(float elevation, float moisture)
        {
            int e = ElevationZone(elevation);   // 1..4
            int m = MoistureZone(moisture);     // 1..6

            if (e == 4) // высоко
            {
                if (m >= 5) return Biome.Snow;
                if (m >= 3) return Biome.Tundra;
                if (m >= 2) return Biome.Bare;
                return Biome.Scorched;
            }

            if (e == 3)
            {
                if (m >= 5) return Biome.Taiga;
                if (m >= 3) return Biome.Shrubland;
                return Biome.TemperateDesert;
            }

            if (e == 2)
            {
                if (m >= 6) return Biome.TemperateRainForest;
                if (m >= 4) return Biome.TemperateDeciduousForest;
                if (m >= 3) return Biome.Grassland;
                return Biome.TemperateDesert;
            }

            // e == 1, низко
            if (m >= 6) return Biome.TropicalRainForest;
            if (m >= 4) return Biome.TropicalSeasonalForest;
            if (m >= 2) return Biome.Grassland;
            return Biome.SubtropicalDesert;
        }

        static int ElevationZone(float e)
        {
            if (e < 0.25f) return 1;
            if (e < 0.5f) return 2;
            if (e < 0.75f) return 3;
            return 4;
        }

        static int MoistureZone(float m)
        {
            // 6 равных зон по [0,1]
            int zone = (int)(m * 6f) + 1;
            return System.Math.Clamp(zone, 1, 6);
        }
    }
}
