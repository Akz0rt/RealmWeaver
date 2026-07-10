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
        public static Color GetHeightColor(float height, Biome biome, float waterDepth01 = 0f)
        {
            if (biome == Biome.Ocean) return GetWaterColor(waterDepth01, isOcean: true);
            if (biome == Biome.Lake) return GetWaterColor(waterDepth01, isOcean: false);

            float h = Mathf.Clamp01(height);

            if (h < 0.05f)
                return new Color(0.90f, 0.85f, 0.60f); // пляж/самая низкая суша
            if (h < 0.55f)
                return Color.Lerp(new Color(0.40f, 0.60f, 0.30f), new Color(0.30f, 0.50f, 0.20f), (h - 0.05f) / 0.50f); // равнина/лес
            if (h < 0.80f)
                return Color.Lerp(new Color(0.50f, 0.45f, 0.40f), new Color(0.40f, 0.35f, 0.30f), (h - 0.55f) / 0.25f); // горы
            return Color.white; // снег на пиках
        }

        /// <summary>
        /// Цвет воды по "глубине" [0=мелководье у берега, 1=самая глубокая точка] - имитирует
        /// видимый сквозь толщу воды подводный рельеф материка: мелководье светлее и зеленее
        /// (виден песчаный/скалистый шельф), глубокая часть - тёмно-синяя. Чисто визуальный
        /// приём поверх плоского меша (Y=0 везде) - геометрия воды не меняется.
        /// </summary>
        public static Color GetWaterColor(float depth01, bool isOcean)
        {
            float d = Mathf.Clamp01(depth01);
            if (isOcean)
            {
                Color shallow = new Color(0.20f, 0.45f, 0.55f); // мелководье/шельф у берега
                Color deep = new Color(0.05f, 0.12f, 0.30f);    // глубокий океан
                return Color.Lerp(shallow, deep, d);
            }
            else
            {
                Color shallow = new Color(0.45f, 0.65f, 0.60f); // мелководье озера
                Color deep = new Color(0.20f, 0.40f, 0.50f);    // глубокая часть озера
                return Color.Lerp(shallow, deep, d);
            }
        }

        public static Color GetBiomeColor(Biome biome)
        {
            switch (biome)
            {
                case Biome.Ocean: return new Color(0.10f, 0.25f, 0.50f);
                case Biome.Lake: return new Color(0.30f, 0.55f, 0.65f);
                case Biome.Beach: return new Color(0.90f, 0.85f, 0.60f);
                case Biome.IceWaste: return new Color(0.80f, 0.82f, 0.85f);
                case Biome.Tundra: return new Color(0.75f, 0.78f, 0.75f);
                case Biome.Snow: return Color.white;
                case Biome.Glacier: return new Color(0.88f, 0.93f, 0.96f);
                case Biome.ColdSteppe: return new Color(0.62f, 0.64f, 0.50f);
                case Biome.ForestTundra: return new Color(0.50f, 0.60f, 0.52f);
                case Biome.Taiga: return new Color(0.30f, 0.46f, 0.38f);
                case Biome.ConiferForest: return new Color(0.22f, 0.42f, 0.34f);
                case Biome.Steppe: return new Color(0.66f, 0.64f, 0.42f);
                case Biome.Grassland: return new Color(0.50f, 0.70f, 0.35f);
                case Biome.Forest: return new Color(0.25f, 0.53f, 0.28f);
                case Biome.RainForest: return new Color(0.15f, 0.48f, 0.24f);
                case Biome.SemiDesert: return new Color(0.78f, 0.70f, 0.48f);
                case Biome.Shrubland: return new Color(0.58f, 0.60f, 0.40f);
                case Biome.Savanna: return new Color(0.74f, 0.66f, 0.36f);
                case Biome.WarmForest: return new Color(0.42f, 0.56f, 0.24f);
                case Biome.Desert: return new Color(0.88f, 0.78f, 0.52f);
                case Biome.TropicalForest: return new Color(0.12f, 0.46f, 0.18f);
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
            => HillshadeBrightness(gradX, gradY, strength, lightAzimuthDeg, ambient, out _);

        /// <summary>Тот же расчёт, но дополнительно возвращает сырой N·L (до Lerp с ambient) -
        /// нужен MapRasterizer для "холодного лунного" подсвета освещённых склонов поверх обычного
        /// hillshade (см. design doc, шаг 6).</summary>
        public static float HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient, out float ndotl)
        {
            var normal = new Vector3(-gradX * strength, 1f, -gradY * strength).normalized;
            float az = lightAzimuthDeg * Mathf.Deg2Rad;
            var lightDir = new Vector3(Mathf.Sin(az), 1f, Mathf.Cos(az)).normalized;
            ndotl = Mathf.Clamp01(Vector3.Dot(normal, lightDir));
            return Mathf.Lerp(ambient, 1f, ndotl);
        }
    }
}
