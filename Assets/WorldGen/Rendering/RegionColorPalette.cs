using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Цвета для режимов отображения карты: по региону (политическая карта),
    /// по высоте (физическая карта), и по биому (климатическая карта). Это единственный
    /// файл во всём пайплайне, который содержит UnityEngine-специфичный тип (Color) -
    /// сделан намеренно отдельным от чистого Generation-слоя.
    /// </summary>
    public static class RegionColorPalette
    {
        static readonly Color[] palette = new Color[]
        {
            new Color(0.80f, 0.30f, 0.30f),
            new Color(0.30f, 0.60f, 0.80f),
            new Color(0.40f, 0.70f, 0.30f),
            new Color(0.80f, 0.70f, 0.20f),
            new Color(0.60f, 0.40f, 0.80f),
            new Color(0.85f, 0.50f, 0.20f),
            new Color(0.30f, 0.75f, 0.70f),
            new Color(0.75f, 0.35f, 0.60f),
        };

        public static Color GetRegionColor(int regionId)
        {
            if (regionId < 0) return Color.gray; // не назначено - сигнал об ошибке в данных
            if (regionId < palette.Length) return palette[regionId];

            // Fallback для N больше размера палитры - золотой угол в HSV даёт хорошо
            // различимые соседние цвета без необходимости вручную добавлять записи в массив.
            float hue = (regionId * 0.618033f) % 1f;
            return Color.HSVToRGB(hue, 0.6f, 0.85f);
        }

        /// <summary>
        /// Цвет клетки в режиме отображения "по высоте" (физическая карта).
        /// height - это elevation в смысле Patel (0=побережье/пляж, 1=горы), НЕ "высота над
        /// уровнем моря" в традиционном смысле. Вода (океан/озеро) определяется через biome,
        /// а не через сравнение height с порогом - elevation суши уже всегда начинается от 0.
        /// </summary>
        public static Color GetHeightColor(float height, Biome biome)
        {
            if (biome == Biome.Ocean)
                return new Color(0.10f, 0.25f, 0.50f);
            if (biome == Biome.Lake)
                return new Color(0.30f, 0.55f, 0.65f);

            float h = Mathf.Clamp01(height);

            if (h < 0.05f)
                return new Color(0.90f, 0.85f, 0.60f); // пляж/самая низкая суша
            if (h < 0.55f)
                return Color.Lerp(new Color(0.40f, 0.60f, 0.30f), new Color(0.30f, 0.50f, 0.20f), (h - 0.05f) / 0.50f); // равнина/лес
            if (h < 0.80f)
                return Color.Lerp(new Color(0.50f, 0.45f, 0.40f), new Color(0.40f, 0.35f, 0.30f), (h - 0.55f) / 0.25f); // горы
            return Color.white; // снег на пиках
        }

        public static Color GetBiomeColor(Biome biome)
        {
            switch (biome)
            {
                case Biome.Ocean: return new Color(0.10f, 0.25f, 0.50f);
                case Biome.Lake: return new Color(0.30f, 0.55f, 0.65f);
                case Biome.Beach: return new Color(0.90f, 0.85f, 0.60f);
                case Biome.Snow: return Color.white;
                case Biome.Tundra: return new Color(0.75f, 0.78f, 0.75f);
                case Biome.Bare: return new Color(0.60f, 0.58f, 0.55f);
                case Biome.Scorched: return new Color(0.55f, 0.50f, 0.45f);
                case Biome.Taiga: return new Color(0.35f, 0.50f, 0.40f);
                case Biome.Shrubland: return new Color(0.60f, 0.60f, 0.40f);
                case Biome.TemperateDesert: return new Color(0.85f, 0.80f, 0.55f);
                case Biome.TemperateRainForest: return new Color(0.15f, 0.50f, 0.25f);
                case Biome.TemperateDeciduousForest: return new Color(0.25f, 0.55f, 0.25f);
                case Biome.Grassland: return new Color(0.50f, 0.70f, 0.35f);
                case Biome.TropicalRainForest: return new Color(0.10f, 0.45f, 0.15f);
                case Biome.TropicalSeasonalForest: return new Color(0.30f, 0.55f, 0.20f);
                case Biome.SubtropicalDesert: return new Color(0.90f, 0.80f, 0.45f);
                default: return Color.magenta; // сигнал об ошибке - неучтённый биом
            }
        }

        /// <summary>Нейтральный базовый тон, когда слой биома выключен: вода - синяя/озёрная,
        /// суша - песочный, чтобы рельеф оставался читаемым без биомной раскраски.</summary>
        public static Color GetNeutralBaseColor(VoronoiCell cell)
        {
            if (cell.EffectiveIsOcean) return new Color(0.10f, 0.25f, 0.50f);
            if (cell.EffectiveIsLake) return new Color(0.30f, 0.55f, 0.65f);
            return new Color(0.82f, 0.78f, 0.65f); // нейтральная суша (tan)
        }

        /// <summary>Яркость рельефного затенения [ambient..1] из градиента высоты клетки.
        /// Псевдонормаль строится из градиента (Y - вверх), освещается направленным светом
        /// под азимутом lightAzimuthDeg и фиксированным углом возвышения 45°.</summary>
        public static float HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient)
        {
            var normal = new Vector3(-gradX * strength, 1f, -gradY * strength).normalized;
            float az = lightAzimuthDeg * Mathf.Deg2Rad;
            var lightDir = new Vector3(Mathf.Sin(az), 1f, Mathf.Cos(az)).normalized;
            float ndotl = Mathf.Clamp01(Vector3.Dot(normal, lightDir));
            return Mathf.Lerp(ambient, 1f, ndotl);
        }
    }
}
