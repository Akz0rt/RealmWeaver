using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Чистые (без зависимости от UnityEngine) геометрия и математика radius-кисти
    /// (см. BrushToolController). Вынесены отдельно, чтобы запрос клеток по радиусу и
    /// усреднение для режима "Сгладить" можно было прогонять [ContextMenu] self-тестами
    /// без запущенной сцены. Координаты — в пространстве VoronoiCell.Site (плоскость карты XZ,
    /// где Site.X — ось X, Site.Y — ось Z).
    /// </summary>
    public static class BrushOps
    {
        /// <summary>
        /// Возвращает все клетки, чьё Site попадает в отпечаток кисти с центром (centerX, centerZ).
        /// Круг: евклидово расстояние ≤ radius. Квадрат: осевой бокс, |dx| ≤ radius и |dz| ≤ radius.
        /// </summary>
        public static List<VoronoiCell> CellsInRadius(IReadOnlyList<VoronoiCell> cells,
                                                      float centerX, float centerZ,
                                                      float radius, bool square)
        {
            var result = new List<VoronoiCell>();
            if (cells == null || radius <= 0f) return result;

            float r2 = radius * radius;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                float dx = cell.Site.X - centerX;
                float dz = cell.Site.Y - centerZ;
                bool inside = square
                    ? (Math.Abs(dx) <= radius && Math.Abs(dz) <= radius)
                    : (dx * dx + dz * dz) <= r2;
                if (inside) result.Add(cell);
            }
            return result;
        }

        /// <summary>
        /// Целевое значение для режима "Сгладить": сдвигает current на долю strength (0..1) в сторону
        /// среднего между ним самим и значениями соседей. strength=0 — без изменений; strength=1 —
        /// ровно среднее. Соседей может не быть (тогда среднее = само значение → результат = current).
        /// </summary>
        public static float SmoothedValue(float current, IEnumerable<float> neighborValues, float strength)
        {
            float sum = current;
            int count = 1;
            if (neighborValues != null)
            {
                foreach (var v in neighborValues)
                {
                    sum += v;
                    count++;
                }
            }
            float avg = sum / count;
            return current + (avg - current) * strength;
        }
    }
}
