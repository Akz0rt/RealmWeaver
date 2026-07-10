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
    }
}
