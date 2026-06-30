using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Переносит elevation/moisture с corners на клетки через усреднение по всем corners,
    /// принадлежащим клетке (cell.Polygon вершины). Усреднение сглаживает переходы между
    /// биомами на границах клеток - в отличие от голосования по большинству, которое давало
    /// бы более резкие, "рубленые" границы.
    ///
    /// ЭТО ОСНОВНОЙ ПУТЬ ПЕРЕНОСА Patel-системы (elevation/moisture/biome) на структуру,
    /// где рендер работает с центрами клеток, а не с corner-графом напрямую.
    /// </summary>
    public static class CellClimateAverager
    {
        public static void ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float beachElevationThreshold = 0.1f)
        {
            var cornersByCell = new Dictionary<int, List<Corner>>();
            foreach (var corner in corners)
            {
                foreach (var cellId in corner.TouchingCellIds)
                {
                    if (!cornersByCell.TryGetValue(cellId, out var list))
                    {
                        list = new List<Corner>();
                        cornersByCell[cellId] = list;
                    }
                    list.Add(corner);
                }
            }

            foreach (var cell in cells)
            {
                if (!cornersByCell.TryGetValue(cell.Id, out var cellCorners) || cellCorners.Count == 0)
                    continue; // клетка без corners (например, вырожденный полигон) - оставляем как есть

                float avgElevation = cellCorners.Average(c => c.Elevation);
                float avgMoisture = cellCorners.Average(c => c.Moisture);

                // isLake/isOcean для клетки уже посчитаны ранее через CellWaterAssigner -
                // используем cell.IsOcean напрямую. isLake выводим как "большинство water corners не океан".
                int waterCorners = cellCorners.Count(c => c.IsWater);
                int oceanCorners = cellCorners.Count(c => c.IsOcean);
                bool isLake = !cell.IsOcean && waterCorners > 0 && oceanCorners < waterCorners;

                cell.Height = avgElevation;   // переиспользуем существующее поле Height для elevation
                cell.Humidity = avgMoisture;  // переиспользуем существующее поле Humidity для moisture
                cell.Biome = BiomeClassifier.Classify(avgElevation, avgMoisture, cell.IsOcean, isLake, beachElevationThreshold);
            }
        }
    }
}
