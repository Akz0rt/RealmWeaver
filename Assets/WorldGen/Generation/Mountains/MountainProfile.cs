using System;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Шкала остроты (§10): «на какой высоте сидит ярус такого-то радиуса». Одно число от 0 до 1
    /// плавно ведёт от купола через синусоиду и конус к шпилю. Заменяет прежний показатель склона,
    /// вместе с которым ушёл и весь класс изъянов самопересекающегося силуэта.
    ///
    /// Все четыре опорные кривые убывают по r, поэтому убывает и любая их смесь: ярусы
    /// гарантированно идут снизу вверх и не перескакивают друг через друга при любом положении
    /// ползунка. Это свойство ПОСТРОЕНИЯ, а не проверка, — и именно поэтому острота сделана смесью
    /// готовых кривых, а не свободной формулой.
    ///
    /// r зажимается в 0…1 намеренно: у синусоиды Acos от значения больше единицы даёт NaN, а r
    /// может прийти из сериализованного поля, а не из посчитанного отношения.
    /// </summary>
    public static class MountainProfile
    {
        /// <summary>Радиус верхнего кольца. Не ноль: нулевое кольцо вырождается в точку, и полоса
        /// под ним теряет ширину. Пятая часть от 1/4 подошвы — площадка, которой не видно.</summary>
        public const float ApexRadius = 0.05f;

        /// <summary>Купол: √(1 − r²).</summary>
        public static float Dome(float r) => (float)Math.Sqrt(Math.Max(0f, 1f - r * r));

        /// <summary>Синусоида: arccos(2r − 1) / π.</summary>
        public static float Sine(float r) => (float)(Math.Acos(2.0 * r - 1.0) / Math.PI);

        /// <summary>Конус: 1 − r.</summary>
        public static float Cone(float r) => 1f - r;

        /// <summary>Шпиль: (1 − r)^1.6 — ровно прежний показатель склона.</summary>
        public static float Spire(float r) => (float)Math.Pow(1f - r, 1.6);

        /// <summary>Подъём яруса радиуса r, в долях высоты горы. sharp — острота 0…1.</summary>
        public static float Lift(float sharp, float r)
        {
            r = Clamp01(r);
            float s = Clamp01(sharp) * 3f;
            int i = (int)s;
            if (i > 2) i = 2;
            float f = s - i;

            float a = Anchor(i, r);
            float b = Anchor(i + 1, r);
            return a + (b - a) * f;
        }

        static float Anchor(int index, float r)
        {
            switch (index)
            {
                case 0: return Dome(r);
                case 1: return Sine(r);
                case 2: return Cone(r);
                default: return Spire(r);
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
