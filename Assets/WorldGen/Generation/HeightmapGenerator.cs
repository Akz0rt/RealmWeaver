namespace WorldGen.Generation
{
    /// <summary>
    /// Генератор высоты рельефа через многослойный (fractal) OpenSimplex2-шум с предварительным
    /// domain warping (искажением координат), плюс island falloff - функция, которая гарантированно
    /// "топит" края карты под уровень моря, формируя единый материк произвольной формы, окружённый
    /// океаном, независимо от того, что нагенерировал сам шум по краям.
    ///
    /// ВАЖНОЕ ИЗМЕНЕНИЕ: добавлен innerRadius - доля карты от центра (по Chebyshev-расстоянию),
    /// которая гарантированно НЕ топится falloff'ом вообще (falloff = 0 внутри этого радиуса).
    /// Без этого параметра falloff растёт слишком резко даже при разумных значениях falloffPower,
    /// и материк может занимать лишь малую долю карты, оставляя непропорционально много воды
    /// (при innerRadius=0 и falloffPower=2.5 доля воды доходила до ~78% площади карты).
    ///
    /// ЗАВИСИМОСТЬ: требует файл FastNoiseLite.cs (однофайловая библиотека, не NuGet/UPM-пакет).
    /// Скачать актуальную версию: https://github.com/Auburn/FastNoiseLite/blob/master/CSharp/FastNoiseLite.cs
    /// Положить рядом в ту же папку Generation (или в отдельную ThirdParty-папку проекта).
    /// </summary>
    public class HeightmapGenerator
    {
        readonly FastNoiseLite baseNoise;
        readonly FastNoiseLite warpNoise;
        readonly float mapWidth;
        readonly float mapHeight;
        readonly float falloffPower;
        readonly float innerRadius;

        public HeightmapGenerator(int seed, float mapWidth, float mapHeight, float baseFrequency = 0.01f, int octaves = 4,
                                    float warpAmplitude = 40f, float warpFrequency = 0.01f, float falloffPower = 2.5f, float innerRadius = 0.5f)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.falloffPower = falloffPower;
            this.innerRadius = innerRadius;

            baseNoise = new FastNoiseLite(seed);
            baseNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            baseNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            baseNoise.SetFractalOctaves(octaves);
            baseNoise.SetFrequency(baseFrequency);

            // Отдельный инстанс для domain warp - намеренно другой seed, чтобы паттерн warp
            // не коррелировал с паттерном базового шума высоты.
            warpNoise = new FastNoiseLite(seed + 1);
            warpNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
            warpNoise.SetDomainWarpAmp(warpAmplitude);
            warpNoise.SetFrequency(warpFrequency);
        }

        /// <summary>
        /// Возвращает высоту в диапазоне примерно [0, 1] (возможны небольшие отрицательные значения
        /// у самого края карты из-за falloff - это нормально, дальше всё, что ниже SeaLevel, считается водой).
        /// </summary>
        public float GetHeight(float x, float y)
        {
            float wx = x, wy = y;
            warpNoise.DomainWarp(ref wx, ref wy); // искажает wx, wy "по месту" (ref-параметры)

            float raw = baseNoise.GetNoise(wx, wy); // диапазон [-1, 1]
            float normalized = (raw + 1f) * 0.5f;    // нормализуем в [0, 1]

            float falloff = ComputeFalloff(x, y);
            return normalized - falloff;
        }

        /// <summary>
        /// Falloff растёт от центра карты (0 внутри innerRadius) к краям (около 1 у самой границы),
        /// возведённый в степень falloffPower для контроля резкости спада к берегу.
        /// Использован square bump (Chebyshev-подобное расстояние через max(|nx|,|ny|)),
        /// который даёт более "квадратный" материк, ближе к границам прямоугольной карты,
        /// чем чисто евклидовый остров-круг - что лучше заполняет прямоугольную область карты.
        ///
        /// innerRadius - доля расстояния от центра (в той же шкале [0,1], что и d), внутри которой
        /// falloff гарантированно равен 0 - материк никогда не топится в этой зоне независимо
        /// от шума. Без этого параметра (innerRadius=0) falloff растёт от центра сразу,
        /// что при разумных falloffPower даёт слишком много воды на карте (см. комментарий класса).
        /// </summary>
        float ComputeFalloff(float x, float y)
        {
            float nx = 2f * (x / mapWidth) - 1f;   // нормализация в [-1, 1]
            float ny = 2f * (y / mapHeight) - 1f;

            float d = System.MathF.Max(System.MathF.Abs(nx), System.MathF.Abs(ny));

            if (d < innerRadius) return 0f;

            float adjusted = (d - innerRadius) / (1f - innerRadius);
            return System.MathF.Pow(adjusted, falloffPower);
        }
    }
}

