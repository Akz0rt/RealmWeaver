namespace WorldGen.Generation
{
    /// <summary>
    /// Генератор высоты рельефа: многослойный OpenSimplex2-шум с domain warping + island falloff,
    /// который топит края карты под уровень моря, формируя материк, окружённый океаном.
    ///
    /// Falloff — РАДИАЛЬНЫЙ (евклидов) от, возможно, смещённого по сиду центра материка, с добавкой
    /// низкочастотного "берегового" шума (изрезанность: полуострова/бухты) и гарантированной водной
    /// кромкой у самой границы карты (borderWaterMargin) — чтобы материк никогда не упирался в край
    /// и вода на краю текстуры бесшовно стыковалась с фоном редактора (см. camera-bg companion).
    ///
    /// ЗАВИСИМОСТЬ: FastNoiseLite.cs (однофайловая либа, лежит рядом в папке Generation).
    /// </summary>
    public class HeightmapGenerator
    {
        readonly FastNoiseLite baseNoise;
        readonly FastNoiseLite warpNoise;
        readonly FastNoiseLite coastNoise;
        readonly float mapWidth;
        readonly float mapHeight;
        readonly float falloffPower;
        readonly float innerRadius;
        readonly float coastRoughness;
        readonly float borderWaterMargin;
        readonly float centerOffsetX;
        readonly float centerOffsetY;

        public HeightmapGenerator(int seed, float mapWidth, float mapHeight, float baseFrequency = 0.01f, int octaves = 4,
                                    float warpAmplitude = 40f, float warpFrequency = 0.01f, float falloffPower = 1.8f,
                                    float innerRadius = 0.2f, float coastRoughness = 0.2f, float coastRoughnessFrequency = 0.004f,
                                    float continentCenterJitter = 0.18f, float borderWaterMargin = 0.06f)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.falloffPower = falloffPower;
            this.innerRadius = innerRadius;
            this.coastRoughness = coastRoughness;
            this.borderWaterMargin = borderWaterMargin;

            baseNoise = new FastNoiseLite(seed);
            baseNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            baseNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            baseNoise.SetFractalOctaves(octaves);
            baseNoise.SetFrequency(baseFrequency);

            warpNoise = new FastNoiseLite(seed + 1);
            warpNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
            warpNoise.SetDomainWarpAmp(warpAmplitude);
            warpNoise.SetFrequency(warpFrequency);

            // Низкочастотный шум для изрезанности берега - свой seed-сдвиг, чтобы не коррелировать.
            coastNoise = new FastNoiseLite(seed + 4000);
            coastNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            coastNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            coastNoise.SetFractalOctaves(3);
            coastNoise.SetFrequency(coastRoughnessFrequency);

            // Детерминированное по сиду смещение центра материка (в нормированном [-1,1] пространстве).
            var rng = new System.Random(seed + 5000);
            centerOffsetX = (float)(rng.NextDouble() * 2.0 - 1.0) * continentCenterJitter;
            centerOffsetY = (float)(rng.NextDouble() * 2.0 - 1.0) * continentCenterJitter;
        }

        /// <summary>
        /// Высота примерно в [0,1] (у края возможны отрицательные из-за falloff - это нормально,
        /// всё ниже SeaLevel считается водой).
        /// </summary>
        public float GetHeight(float x, float y)
        {
            float wx = x, wy = y;
            warpNoise.DomainWarp(ref wx, ref wy);

            float raw = baseNoise.GetNoise(wx, wy);   // [-1, 1]
            float normalized = (raw + 1f) * 0.5f;     // [0, 1]

            float falloff = ComputeFalloff(x, y);
            return normalized - falloff;
        }

        /// <summary>
        /// Радиальный falloff от смещённого центра материка + береговой шум, плюс гарантированная
        /// водная кромка у самой границы карты.
        /// </summary>
        float ComputeFalloff(float x, float y)
        {
            // Координаты относительно ЦЕНТРА КАРТЫ - для гарантии водной кромки по периметру.
            float mnx = 2f * (x / mapWidth) - 1f;
            float mny = 2f * (y / mapHeight) - 1f;
            float border = System.MathF.Max(System.MathF.Abs(mnx), System.MathF.Abs(mny));
            if (border > 1f - borderWaterMargin) return 1f; // гарантированный водный "ров" у края

            // Координаты относительно смещённого ЦЕНТРА МАТЕРИКА - для радиальной формы.
            float nx = mnx - centerOffsetX;
            float ny = mny - centerOffsetY;
            float d = System.MathF.Sqrt(nx * nx + ny * ny);

            // Изрезанность берега: гуляющий радиус. GetNoise∈[-1,1] → вклад ±0.5·coastRoughness.
            d += coastNoise.GetNoise(x, y) * 0.5f * coastRoughness;

            if (d < innerRadius) return 0f;

            float adjusted = (d - innerRadius) / (1f - innerRadius);
            adjusted = System.Math.Clamp(adjusted, 0f, 1f);
            return System.MathF.Pow(adjusted, falloffPower);
        }
    }
}
