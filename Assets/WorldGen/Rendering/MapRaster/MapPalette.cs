using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    public enum MapPaletteTheme { ColdTwilight, MoonlitSteel, EmeraldAbyss, AmethystNight }

    /// <summary>Визуальное семейство биома - слот палитры. Богатая 16-значная Whittaker-таблица
    /// (BiomeClassifier) не переклассифицируется, а мапится на одно из этих семейств для окраски.</summary>
    public enum BiomeFamily { Sea, Lake, Coast, Snow, Tundra, Highland, Badlands, Forest, ForestWarm, Moor, Plains, Steppe, Savanna, Desert }

    public enum PaletteSlot
    {
        Abyss, Sea, Shallow, Glow, Coast, Marsh, Plains, Moor, Forest, ForestWarm,
        Badlands, Tundra, Highland, Peak, Snow, LakeD, LakeS, Outline, MtnL, MtnS,
        Light, Road, Accent, AccentCold, FogA, FogB, TintCool, TintWarm,
        Steppe, Savanna, Desert
    }

    /// <summary>
    /// 4 палитры тёмного фэнтези-рендера (см. docs/superpowers/specs/2026-07-07-map-terrain-raster-design.md).
    /// Значения token'ов взяты из design_handoff_realmweaver_map/Terra Umbrarum.dc.html.
    /// </summary>
    public static class MapPalette
    {
        // Порядок в каждом массиве: ColdTwilight, MoonlitSteel, EmeraldAbyss, AmethystNight.
        static readonly Dictionary<PaletteSlot, Color32[]> table = new Dictionary<PaletteSlot, Color32[]>
        {
            [PaletteSlot.Abyss] = new[] { new Color32(6, 15, 24, 255), new Color32(8, 14, 30, 255), new Color32(4, 20, 20, 255), new Color32(14, 10, 30, 255) },
            [PaletteSlot.Sea] = new[] { new Color32(11, 30, 44, 255), new Color32(16, 32, 62, 255), new Color32(8, 40, 44, 255), new Color32(26, 22, 54, 255) },
            [PaletteSlot.Shallow] = new[] { new Color32(30, 84, 100, 255), new Color32(46, 96, 150, 255), new Color32(30, 102, 98, 255), new Color32(74, 66, 132, 255) },
            [PaletteSlot.Glow] = new[] { new Color32(120, 200, 214, 255), new Color32(140, 196, 244, 255), new Color32(120, 224, 204, 255), new Color32(168, 150, 244, 255) },
            [PaletteSlot.Coast] = new[] { new Color32(92, 86, 64, 255), new Color32(84, 88, 96, 255), new Color32(86, 92, 58, 255), new Color32(92, 80, 86, 255) },
            [PaletteSlot.Marsh] = new[] { new Color32(36, 58, 50, 255), new Color32(38, 54, 64, 255), new Color32(26, 62, 50, 255), new Color32(48, 44, 72, 255) },
            [PaletteSlot.Plains] = new[] { new Color32(74, 86, 58, 255), new Color32(70, 84, 98, 255), new Color32(86, 102, 54, 255), new Color32(96, 86, 96, 255) },
            [PaletteSlot.Moor] = new[] { new Color32(64, 66, 74, 255), new Color32(70, 74, 90, 255), new Color32(60, 78, 72, 255), new Color32(78, 72, 92, 255) },
            [PaletteSlot.Forest] = new[] { new Color32(24, 58, 46, 255), new Color32(28, 56, 72, 255), new Color32(18, 64, 48, 255), new Color32(42, 48, 80, 255) },
            [PaletteSlot.ForestWarm] = new[] { new Color32(150, 96, 44, 255), new Color32(168, 110, 60, 255), new Color32(176, 116, 44, 255), new Color32(168, 96, 96, 255) },
            [PaletteSlot.Badlands] = new[] { new Color32(128, 84, 54, 255), new Color32(140, 96, 66, 255), new Color32(150, 102, 48, 255), new Color32(150, 90, 84, 255) },
            [PaletteSlot.Tundra] = new[] { new Color32(120, 132, 140, 255), new Color32(150, 168, 190, 255), new Color32(126, 156, 146, 255), new Color32(150, 144, 176, 255) },
            [PaletteSlot.Highland] = new[] { new Color32(74, 80, 88, 255), new Color32(70, 84, 104, 255), new Color32(56, 86, 80, 255), new Color32(76, 70, 98, 255) },
            [PaletteSlot.Peak] = new[] { new Color32(110, 116, 128, 255), new Color32(112, 126, 150, 255), new Color32(92, 120, 116, 255), new Color32(116, 108, 142, 255) },
            [PaletteSlot.Snow] = new[] { new Color32(214, 224, 232, 255), new Color32(224, 234, 248, 255), new Color32(210, 230, 220, 255), new Color32(228, 222, 244, 255) },
            [PaletteSlot.LakeD] = new[] { new Color32(16, 44, 58, 255), new Color32(20, 42, 74, 255), new Color32(12, 50, 52, 255), new Color32(30, 26, 66, 255) },
            [PaletteSlot.LakeS] = new[] { new Color32(46, 110, 126, 255), new Color32(60, 116, 164, 255), new Color32(40, 116, 110, 255), new Color32(80, 72, 148, 255) },
            [PaletteSlot.Outline] = new[] { new Color32(6, 10, 16, 255), new Color32(8, 12, 22, 255), new Color32(4, 14, 14, 255), new Color32(12, 10, 22, 255) },
            [PaletteSlot.MtnL] = new[] { new Color32(140, 150, 164, 255), new Color32(152, 168, 198, 255), new Color32(122, 158, 150, 255), new Color32(146, 138, 172, 255) },
            [PaletteSlot.MtnS] = new[] { new Color32(40, 46, 56, 255), new Color32(42, 50, 70, 255), new Color32(30, 48, 46, 255), new Color32(48, 44, 68, 255) },
            [PaletteSlot.Light] = new[] { new Color32(100, 150, 190, 255), new Color32(140, 180, 235, 255), new Color32(92, 190, 168, 255), new Color32(150, 132, 232, 255) },
            [PaletteSlot.Road] = new[] { new Color32(176, 150, 96, 255), new Color32(168, 158, 128, 255), new Color32(172, 158, 96, 255), new Color32(178, 150, 120, 255) },
            [PaletteSlot.Accent] = new[] { new Color32(230, 178, 92, 255), new Color32(240, 185, 106, 255), new Color32(240, 191, 90, 255), new Color32(240, 173, 84, 255) },
            [PaletteSlot.AccentCold] = new[] { new Color32(143, 216, 230, 255), new Color32(169, 204, 255, 255), new Color32(127, 232, 204, 255), new Color32(195, 172, 255, 255) },
            [PaletteSlot.FogA] = new[] { new Color32(16, 24, 34, 255), new Color32(20, 28, 48, 255), new Color32(10, 30, 30, 255), new Color32(26, 22, 46, 255) },
            [PaletteSlot.FogB] = new[] { new Color32(34, 52, 66, 255), new Color32(44, 60, 90, 255), new Color32(26, 60, 58, 255), new Color32(50, 44, 80, 255) },
            [PaletteSlot.TintCool] = new[] { new Color32(32, 86, 116, 255), new Color32(58, 96, 162, 255), new Color32(26, 116, 104, 255), new Color32(74, 70, 152, 255) },
            [PaletteSlot.TintWarm] = new[] { new Color32(150, 102, 46, 255), new Color32(110, 96, 78, 255), new Color32(108, 104, 54, 255), new Color32(126, 88, 96, 255) },
            [PaletteSlot.Steppe] = new[] { new Color32(108, 106, 72, 255), new Color32(104, 110, 108, 255), new Color32(110, 118, 74, 255), new Color32(116, 106, 102, 255) },
            [PaletteSlot.Savanna] = new[] { new Color32(140, 118, 64, 255), new Color32(150, 128, 84, 255), new Color32(150, 128, 60, 255), new Color32(150, 120, 96, 255) },
            [PaletteSlot.Desert] = new[] { new Color32(166, 140, 92, 255), new Color32(172, 150, 110, 255), new Color32(176, 150, 90, 255), new Color32(176, 142, 120, 255) },
        };

        public static Color32 GetSlotColor(MapPaletteTheme theme, PaletteSlot slot) => table[slot][(int)theme];

        // ColdTwilight per-biome LAND colors (user palette). Other themes fall back to the biome's family color.
        // Beach is intentionally absent → falls back to the Coast slot; Ocean/Lake must NOT be passed here.
        static readonly Dictionary<Biome, Color32> biomeColdTwilight = new Dictionary<Biome, Color32>
        {
            [Biome.IceWaste]      = new Color32(150, 164, 172, 255),
            [Biome.Tundra]        = new Color32(120, 132, 140, 255),
            [Biome.Snow]          = new Color32(214, 224, 232, 255),
            [Biome.Glacier]       = new Color32(150, 176, 190, 255),
            [Biome.ColdSteppe]    = new Color32(104, 110,  84, 255),
            [Biome.ForestTundra]  = new Color32( 80, 100,  88, 255),
            [Biome.Taiga]         = new Color32( 30,  62,  58, 255),
            [Biome.ConiferForest] = new Color32( 22,  50,  46, 255),
            [Biome.Steppe]        = new Color32(104,  98,  58, 255),
            [Biome.Grassland]     = new Color32( 78, 106,  60, 255),
            [Biome.Forest]        = new Color32( 24,  58,  46, 255),
            [Biome.RainForest]    = new Color32( 20,  66,  52, 255),
            [Biome.SemiDesert]    = new Color32(150, 128,  84, 255),
            [Biome.Shrubland]     = new Color32(104, 112,  70, 255),
            [Biome.Savanna]       = new Color32(150, 140,  78, 255),
            [Biome.WarmForest]    = new Color32(150,  96,  44, 255),
            [Biome.Desert]        = new Color32(150, 128,  78, 255),
            [Biome.TropicalForest]= new Color32( 26,  88,  66, 255),
        };

        /// <summary>Per-biome land color. ColdTwilight uses the explicit table; other themes fall back to the
        /// biome's family color. Beach → Coast slot (family fallback). MUST NOT be called for Ocean/Lake
        /// (FamilyToSlot(Sea/Lake) throws — water is colored by depth, not here).</summary>
        public static Color32 GetBiomeColor(MapPaletteTheme theme, Biome biome)
        {
            if (theme == MapPaletteTheme.ColdTwilight && biomeColdTwilight.TryGetValue(biome, out var c))
                return c;
            return GetSlotColor(theme, FamilyToSlot(GetFamily(biome)));
        }

        /// <summary>Плоский базовый цвет для ЛЕНДовых семейств (Sea/Lake не имеют единого слота -
        /// их цвет зависит от глубины воды, см. MapRasterizer.ColorForWaterPixel).</summary>
        public static Color32 GetSlotColor(MapPaletteTheme theme, BiomeFamily family) => GetSlotColor(theme, FamilyToSlot(family));

        static PaletteSlot FamilyToSlot(BiomeFamily family) => family switch
        {
            BiomeFamily.Coast => PaletteSlot.Coast,
            BiomeFamily.Snow => PaletteSlot.Snow,
            BiomeFamily.Tundra => PaletteSlot.Tundra,
            BiomeFamily.Highland => PaletteSlot.Highland,
            BiomeFamily.Badlands => PaletteSlot.Badlands,
            BiomeFamily.Forest => PaletteSlot.Forest,
            BiomeFamily.ForestWarm => PaletteSlot.ForestWarm,
            BiomeFamily.Moor => PaletteSlot.Moor,
            BiomeFamily.Plains => PaletteSlot.Plains,
            BiomeFamily.Steppe => PaletteSlot.Steppe,
            BiomeFamily.Savanna => PaletteSlot.Savanna,
            BiomeFamily.Desert => PaletteSlot.Desert,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family,
                "Sea/Lake не имеют плоского слота - глубина воды сэмплируется отдельно."),
        };

        /// <summary>Маппинг богатой Whittaker-таблицы (BiomeClassifier, 16 значений) на визуальное
        /// семейство палитры. Явный switch с default-throw - защита от добавления нового Biome
        /// без записи здесь (см. Self-Test: Biome Family Coverage).</summary>
        public static BiomeFamily GetFamily(Biome biome) => biome switch
        {
            Biome.Ocean => BiomeFamily.Sea,
            Biome.Lake => BiomeFamily.Lake,
            Biome.Beach => BiomeFamily.Coast,
            Biome.IceWaste => BiomeFamily.Snow,
            Biome.Snow => BiomeFamily.Snow,
            Biome.Glacier => BiomeFamily.Snow,
            Biome.Tundra => BiomeFamily.Tundra,
            Biome.ForestTundra => BiomeFamily.Tundra,
            Biome.Taiga => BiomeFamily.Forest,
            Biome.ConiferForest => BiomeFamily.Forest,
            Biome.Forest => BiomeFamily.Forest,
            Biome.RainForest => BiomeFamily.Forest,
            Biome.WarmForest => BiomeFamily.ForestWarm,
            Biome.TropicalForest => BiomeFamily.ForestWarm,
            Biome.Grassland => BiomeFamily.Plains,
            Biome.Shrubland => BiomeFamily.Moor,
            Biome.SemiDesert => BiomeFamily.Badlands,
            Biome.ColdSteppe => BiomeFamily.Steppe,
            Biome.Steppe => BiomeFamily.Steppe,
            Biome.Savanna => BiomeFamily.Savanna,
            Biome.Desert => BiomeFamily.Desert,
            _ => throw new ArgumentOutOfRangeException(nameof(biome), biome, "Новый Biome без записи в таблице BiomeFamily"),
        };

        public static string DisplayName(MapPaletteTheme theme) => theme switch
        {
            MapPaletteTheme.ColdTwilight => "Холодный сумрак",
            MapPaletteTheme.MoonlitSteel => "Лунная сталь",
            MapPaletteTheme.EmeraldAbyss => "Изумрудная бездна",
            MapPaletteTheme.AmethystNight => "Аметистовая ночь",
            _ => theme.ToString(),
        };
    }
}
