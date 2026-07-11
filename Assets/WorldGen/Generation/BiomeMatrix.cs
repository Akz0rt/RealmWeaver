using System;

namespace WorldGen.Generation
{
    /// <summary>THE isolated 5×5 biome table (temperature × moisture). A future retune edits ONLY
    /// this file (spec §3). Indexed [temperatureLevel 0..4 (cold→hot), moistureLevel 0..4 (dry→wet)].</summary>
    public static class BiomeMatrix
    {
        static readonly Biome[,] Table =
        {
            /* t0 Ледяной   */ { Biome.IceWaste,   Biome.Tundra,       Biome.Tundra,  Biome.Snow,           Biome.Glacier },
            /* t1 Холодный  */ { Biome.ColdSteppe, Biome.ForestTundra, Biome.Taiga,   Biome.Taiga,          Biome.ConiferForest },
            /* t2 Умеренный */ { Biome.Steppe,     Biome.Grassland,    Biome.Forest,  Biome.Forest,         Biome.RainForest },
            /* t3 Тёплый    */ { Biome.SemiDesert, Biome.Shrubland,    Biome.Savanna, Biome.WarmForest,     Biome.WarmForest },
            /* t4 Жаркий    */ { Biome.Desert,     Biome.Desert,       Biome.Savanna, Biome.TropicalForest, Biome.TropicalForest },
        };

        /// <summary>5 равных полос по [0,1] → индекс 0..4 (значение 1.0 попадает в 4).</summary>
        public static int Level5(float v) => Math.Clamp((int)(v * 5f), 0, 4);

        public static Biome Get(int temperatureLevel, int moistureLevel) => Table[temperatureLevel, moistureLevel];

        /// <summary>Inverse of Level5: the representative [0,1] value at the center of a level's band.</summary>
        public static float LevelCenter(int level) => (Math.Clamp(level, 0, 4) + 0.5f) / 5f;

        /// <summary>The first (row-major: temp asc, then moisture asc) matrix cell producing this biome,
        /// or null if the biome is not in the matrix (Ocean/Lake/Beach). Used by migration and the
        /// precise-selection biome dropdown — NOT by the 5×5 palette, where the user picks the cell.</summary>
        public static (int t, int m)? RepresentativeClimate(Biome b)
        {
            for (int t = 0; t < 5; t++)
                for (int m = 0; m < 5; m++)
                    if (Table[t, m] == b) return (t, m);
            return null;
        }
    }
}
