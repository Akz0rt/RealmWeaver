using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Чистый C# движок расстановки декораций: клетки → детерминированный список
    /// инстансов. Без Unity-рендера (юнит-тестируемый как генераторы в Generation/).</summary>
    public static class DecorationPlacer
    {
        // --- Классификация: подходит ли тип к клетке + контекст-категория стиля ---

        static bool IsColdCell(VoronoiCell c, DecorationConfig cfg)
        {
            var fam = MapPalette.GetFamily(c.Biome);
            return c.EffectiveTemperature < cfg.coldTemperature
                   || fam == BiomeFamily.Snow || fam == BiomeFamily.Tundra;
        }

        /// <summary>Категория для гор/холмов: Snowy если холодно, иначе Forested над лесом, иначе Bare.</summary>
        static DecorationStyleCategory ReliefStyle(VoronoiCell c, DecorationConfig cfg)
        {
            if (IsColdCell(c, cfg)) return DecorationStyleCategory.Snowy;
            var fam = MapPalette.GetFamily(c.Biome);
            if (fam == BiomeFamily.Forest || fam == BiomeFamily.ForestWarm) return DecorationStyleCategory.Forested;
            return DecorationStyleCategory.Bare;
        }

        public static bool TryClassify(VoronoiCell cell, DecorationConfig cfg,
                                       DecorationType type, out DecorationStyleCategory style)
        {
            style = DecorationStyleCategory.Plain;
            if (!RegionCategories.IsLandCell(cell)) return false;

            float e = cell.EffectiveElevation;
            var fam = MapPalette.GetFamily(cell.Biome);

            switch (type)
            {
                case DecorationType.Mountain:
                    if (e < cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Hill:
                    if (e < cfg.hillMinElevation || e >= cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Pine:
                    if (fam != BiomeFamily.Forest) return false;
                    style = IsColdCell(cell, cfg) ? DecorationStyleCategory.Snowy : DecorationStyleCategory.Plain;
                    return true;
                case DecorationType.AutumnTree:
                    if (fam != BiomeFamily.ForestWarm) return false;
                    style = DecorationStyleCategory.Plain; return true;
                case DecorationType.Mesa:
                    if (fam != BiomeFamily.Badlands) return false;
                    style = DecorationStyleCategory.Plain; return true;
                default: return false;
            }
        }

        // --- Детерминированный хеш (fract от целочисленного mix) ---
        public static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        static float Hash01(int x, int y, int salt) => Hash(x, y, salt) / 4294967295f;
    }
}
