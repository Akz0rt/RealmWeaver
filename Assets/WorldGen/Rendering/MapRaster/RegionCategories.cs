using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Категоризация клеток в "области" для сглаживания контуров: семейство биома и полоса
    /// высоты. Вынесено из MapRasterizer, чтобы CPU-путь и GPU label-baker (RegionLabelBaker) не
    /// дублировали логику.</summary>
    public static class RegionCategories
    {
        public static bool IsLandCell(VoronoiCell c) => !(c.EffectiveIsOcean || c.EffectiveIsLake);

        /// <summary>Индекс BiomeFamily суши, -1 для воды (регионы семейств ограничены сушей).</summary>
        public static int FamilyCategoryOf(VoronoiCell c) => IsLandCell(c) ? (int)MapPalette.GetFamily(c.Biome) : -1;

        /// <summary>Индекс полосы высоты суши, -1 для воды.</summary>
        public static int BandCategoryOf(VoronoiCell c, int bands) =>
            IsLandCell(c) ? Mathf.Clamp((int)(c.EffectiveElevation * bands), 0, bands - 1) : -1;

        /// <summary>Порядок приоритета семейств (младший→старший, старший выигрывает перекрытия).</summary>
        public static readonly int[] FamilyPriority =
        {
            (int)BiomeFamily.Plains, (int)BiomeFamily.Steppe, (int)BiomeFamily.Savanna, (int)BiomeFamily.Moor,
            (int)BiomeFamily.Forest, (int)BiomeFamily.ForestWarm, (int)BiomeFamily.Coast, (int)BiomeFamily.Tundra,
            (int)BiomeFamily.Highland, (int)BiomeFamily.Badlands, (int)BiomeFamily.Desert, (int)BiomeFamily.Snow,
        };

        /// <summary>Полосы высоты по возрастанию индекса (выше = сверху).</summary>
        public static int[] BandPriorityAscending(int bands)
        {
            var order = new int[bands];
            for (int i = 0; i < bands; i++) order[i] = i;
            return order;
        }
    }
}
