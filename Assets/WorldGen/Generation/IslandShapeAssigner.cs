using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Определяет land/water на corners через island-shape функцию (существующий
    /// HeightmapGenerator с island falloff, переиспользованный здесь как чистая
    /// "форма острова" - без отдельной интерпретации как физической высоты).
    /// Это шаг 1 из Patel-пайплайна: land/water определяется ДО elevation,
    /// а не наоборот.
    /// </summary>
    public static class IslandShapeAssigner
    {
        public static void AssignWaterCorners(List<Corner> corners, HeightmapGenerator islandShapeGen, float seaLevel)
        {
            foreach (var corner in corners)
            {
                float shapeValue = islandShapeGen.GetHeight(corner.Position.X, corner.Position.Y);
                corner.IsWater = shapeValue < seaLevel;
            }
        }
    }
}
