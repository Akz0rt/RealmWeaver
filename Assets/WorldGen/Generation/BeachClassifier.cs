using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Пляж по смежности с океаном: клетка суши, у которой хоть один сосед — океан, становится
    /// Biome.Beach. Даёт непрерывный тонкий песчаный кант в 1 клетку по всему океанскому берегу,
    /// независимо от высоты (в отличие от старого высотного правила в BiomeClassifier).
    ///
    /// Вызывается ПОСЛЕ CellClimateAverager (перезаписывает биом прибрежной суши) в обоих путях
    /// WorldGenerator. Озёрные берега не трогаем (пляж только против океана - по требованию дизайна).
    /// Мягкий переход песок→биом по coast-distance - это уже подпроект B (рендер).
    /// </summary>
    public static class BeachClassifier
    {
        public static void AssignCoastalBeaches(List<VoronoiCell> cells)
        {
            var byId = cells.ToDictionary(c => c.Id);
            foreach (var cell in cells)
            {
                if (cell.IsOcean || cell.Biome == Biome.Lake) continue; // только суша (не океан, не озеро)

                bool coastal = cell.NeighborIds.Any(id =>
                    byId.TryGetValue(id, out var neighbor) && neighbor.IsOcean);

                if (coastal) cell.Biome = Biome.Beach;
            }
        }
    }
}
