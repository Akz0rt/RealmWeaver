using System;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Правила манеры «тушь» (§13) — те, в которых можно ошибиться числом.
    ///
    /// Живут в ЧИСТОМ слое нарочно. Всё, что лежит в Rendering, не проверяет никто, кроме
    /// компилятора: стенд туда не смотрит, а Unity в счёт не запустишь. Сама раскладка треугольников
    /// пусть остаётся в рендере — там ошибка видна глазом на первом же снимке. А вот «сколько меток
    /// класть», «какая сторона в тени» и «как линия сходит на нет» глазом не проверить: они врут
    /// тихо и на всей карте разом.
    /// </summary>
    public static class MountainInk
    {
        /// <summary>Сколько меток крошки приходится на одну R² площади подошвы при густоте 1.
        /// Подобрано так, чтобы «крошка 0,88» дала ту же плотность, что ДМ видел в браузерном
        /// превью на фикстуре радиуса 26.</summary>
        public const float GritPerArea = 74f;

        /// <summary>
        /// Потолок меток на одну гору — предохранитель от разгона, а не рабочее ограничение. При
        /// числах ДМ (крошка 0,88) самая густая гора просит около шестисот с небольшим, поэтому
        /// потолок стоит выше: замерено на маске приложения, при 600 он срезал внутренний слой и
        /// тот выходил реже внешнего — ровно наоборот замыслу.
        ///
        /// Молчать он не должен: MarkCount выдаёт наружу признак «упёрлись», а слой говорит об этом
        /// вслух. Тихая обрезка читается как «нарисовано всё», а нарисовано не всё.
        /// </summary>
        public const int GritCap = 900;

        /// <summary>
        /// Сколько меток крошки положить на гору.
        ///
        /// Считается от ПЛОЩАДИ ПОДОШВЫ, и это главное. В браузерном прототипе плотность считалась
        /// по ярусам, и она тайно цеплялась за ползунок «ярусов»: подняв его ради гладкого силуэта,
        /// ДМ разом затемнил бы всю картину и не понял, отчего. Площадь мерится в R², поэтому
        /// мелкие и крупные горы выглядят одинаково — как и всё остальное в этом алгоритме.
        /// </summary>
        public static int MarkCount(float footArea, float radius, float grit, float density,
                                    out bool capped)
        {
            capped = false;
            if (grit <= 0f || footArea <= 0f) return 0;

            float r2 = Math.Max(1e-4f, radius * radius);
            int count = (int)(grit * density * footArea / r2 * GritPerArea + 0.5f);
            if (count <= 0) return 0;
            if (count > GritCap) { capped = true; return GritCap; }
            return count;
        }

        /// <summary>
        /// Толщина линии в точке границы, в долях жирности. r — то самое значение, на котором
        /// граница достигнута: 1 у ближнего края, ближе к нулю у вершины.
        ///
        /// При r = 1 выходит РОВНО ноль при любом показателе. Поэтому подошва не обводится сама
        /// собой, и контур обрывается — то, чего требовал образец ДМ. Ни одной проверки «а здесь не
        /// обводить» в коде нет и быть не должно: правило одно и работает везде.
        /// </summary>
        public static float Taper(float r, float exponent)
        {
            float t = 1f - r;
            if (t <= 0f) return 0f;
            if (t > 1f) t = 1f;
            return (float)Math.Pow(t, Math.Max(0.01f, exponent));
        }

        /// <summary>Насколько точка в тени: 1 — прямо от света, 0 и ниже — освещена. normal —
        /// наружу глядящая нормаль подошвы, light — единичный вектор направления света.</summary>
        public static float Shade(Vector2 normal, Vector2 light)
            => -(normal.X * light.X + normal.Y * light.Y);

        /// <summary>Густота туши для слоя массы: множит и толщину линии, и плотность крошки.
        /// Градация «внешний / средний / внутренний» переехала сюда из цвета — на образце ДМ цветов
        /// нет вовсе, а требование различать слои осталось.</summary>
        public static float Density(int tier, int tierCount, float contrast, float tierInk)
        {
            float band = MountainTierRamp.Mix(tier, tierCount, contrast);
            float k = 1f + tierInk * (band - 0.5f) * 2f;
            return k < 0.15f ? 0.15f : k;
        }

        /// <summary>
        /// На каком r сидит метка крошки. Смещение вверх задаётся показателем «редеет вниз»: при
        /// нуле метки лежат равномерно по склону, чем больше — тем плотнее жмутся к гребню.
        /// u — равномерное число от 0 до 1.
        /// </summary>
        public static float MarkR(float u, float fall)
        {
            float p = 1f / (Math.Max(0f, fall) + 1f);
            float t = (float)Math.Pow(u < 0f ? 0f : (u > 1f ? 1f : u), p);
            return 1f - (1f - MountainProfile.ApexRadius) * t;
        }

        /// <summary>Гашение по глубине: дальние горы бледнее. Считается от МИРОВОЙ координаты, а не
        /// от размаха нарисованного, — иначе новый хребет на другом конце карты молча перекрасил бы
        /// все прежние (тот же изъян, из-за которого зерно оси берут от её положения).</summary>
        public static float Haze(float depth, float mapHeight, float depthTone)
        {
            if (depthTone <= 0f || mapHeight <= 0f) return 1f;
            float far = depth / mapHeight;
            if (far < 0f) far = 0f; else if (far > 1f) far = 1f;
            return 1f - depthTone * far;
        }
    }
}
