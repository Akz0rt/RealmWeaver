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
        /// <summary>Контраст высоты вокруг середины: низины ниже, вершины выше. contrast=1 - без изменений.
        /// Вынесен отдельным методом, чтобы тестировать формулу напрямую (см. self-test).</summary>
        public static float ApplyContrast(float elevation, float contrast)
        {
            return System.Math.Clamp(0.5f + (elevation - 0.5f) * contrast, 0f, 1f);
        }

        public static void ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float elevationContrast = 1f)
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

                float avgElevation = ApplyContrast(cellCorners.Average(c => c.Elevation), elevationContrast);
                float avgMoisture = cellCorners.Average(c => c.Moisture);

                cell.Height = avgElevation;   // переиспользуем поле Height для elevation
                cell.Humidity = avgMoisture;  // переиспользуем поле Humidity для moisture
                // Биом больше НЕ классифицируется здесь - температура ещё не готова.
                // Классификация выполняется отдельным проходом (CellOverrideService.ClassifyAll)
                // ПОСЛЕ TemperatureField, затем BeachClassifier (см. WorldGenerator).
            }
        }
    }
}
