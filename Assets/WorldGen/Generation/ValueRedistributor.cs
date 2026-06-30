using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Перераспределяет значения (elevation или moisture) на corners, чтобы итоговое
    /// распределение соответствовало заданной целевой кумулятивной функции распределения
    /// (CDF) - техника из Patel's mapgen2.
    ///
    /// ВАЖНО: targetCurve здесь - это INVERSE CDF (quantile function), не сама CDF.
    /// Если задача "среди отсортированных значений, x-тая по порядку точка должна получить
    /// такое e, что CDF(e) = x" - то нужно решить относительно e, то есть найти CDF^-1(x).
    ///
    /// Для elevation Patel использует целевую CDF(e) = 1-(1-e)^2 (больше низкой суши, чем
    /// высокой) - обратная функция: e = 1 - sqrt(1-x).
    /// Для moisture целевая CDF(m) = m (равномерное распределение) - обратная функция
    /// тождественна: m = x (CDF совпадает со своей обратной только в этом частном случае).
    /// </summary>
    public static class ValueRedistributor
    {
        /// <summary>
        /// Redistribution elevation по целевой CDF(e) = 1-(1-e)^2 (больше низкой суши, чем горной).
        /// Применяется inverse CDF: e = 1 - sqrt(1-x).
        /// </summary>
        public static void RedistributeElevation(List<Corner> corners)
        {
            Redistribute(corners, c => !c.IsOcean, (c, v) => c.Elevation = v, c => c.Elevation,
                         x => 1f - System.MathF.Sqrt(1f - x));
        }

        /// <summary>
        /// Redistribution moisture по целевой CDF(m) = m (равное количество сухих и влажных регионов).
        /// Inverse CDF тождественна в этом случае: m = x.
        /// </summary>
        public static void RedistributeMoisture(List<Corner> corners)
        {
            Redistribute(corners, c => !c.IsOcean, (c, v) => c.Moisture = v, c => c.Moisture,
                         x => x);
        }

        static void Redistribute(List<Corner> corners, System.Func<Corner, bool> filter,
                                   System.Action<Corner, float> setValue, System.Func<Corner, float> getValue,
                                   System.Func<float, float> inverseCdf)
        {
            var targets = corners.Where(filter).OrderBy(getValue).ToList();
            int n = targets.Count;
            if (n == 0) return;

            for (int i = 0; i < n; i++)
            {
                float x = (float)i / (n - 1 == 0 ? 1 : n - 1); // позиция в сортированном списке, [0,1]
                float redistributed = inverseCdf(x);
                setValue(targets[i], redistributed);
            }
        }
    }
}
