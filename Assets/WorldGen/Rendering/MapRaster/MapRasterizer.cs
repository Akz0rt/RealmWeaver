using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Параметры одного запекания - неизменны между Bake/RebakeRegion для одной и той же
    /// карты, кроме смены палитры/твиков (подпроект 6 добавит UI, поля уже существуют).</summary>
    public class MapRasterConfig
    {
        public int TexWidth;
        public int TexHeight;
        public float MapWidth;
        public float MapHeight;
        public int Seed;
        public MapPaletteTheme Theme = MapPaletteTheme.ColdTwilight;
        public float ColdLight = 58f;
        public float RegionVariation = 45f;
        public float Darkness = 72f;
        public bool SmoothBorders = true;
        public float SmoothRadius = 1f;
        public float ReliefStrength = 3f;
        public float ReliefLightAzimuth = 315f;
        public float ReliefAmbient = 0.5f;

        /// <summary>Число итераций Chaikin-сглаживания контура берега (только Combined+
        /// SmoothBorders). 0 = точные грани клеток Вороного (без трассировки/сглаживания
        /// вообще - см. MapRasterizer.BakeFieldsRect). См. design doc
        /// docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md.</summary>
        public int CoastlineSmoothness = 3;

        /// <summary>Соответствуют существующим тумблерам MapLayersPanel - выключение биомного слоя
        /// даёт нейтральную земляную заливку вместо цвета семейства биома; выключение рельефа
        /// убирает hillshade/холодный подсвет на суше и градиент глубины на воде (плоский цвет).</summary>
        public bool ShowBiomeLayer = true;
        public bool ShowReliefLayer = true;

        /// <summary>Цвет клетки для Height/Region/Biome и Combined-без-сглаживания - привязан к
        /// WorldMapRenderer.GetColorForCell конкретного экземпляра, чтобы не дублировать эту логику.</summary>
        public Func<VoronoiCell, Color> HardModeColor;

        /// <summary>[0,1] "глубина" водной клетки - привязан к WorldMapRenderer.GetWaterDepth01.</summary>
        public Func<VoronoiCell, float> WaterDepth01;
    }

    /// <summary>Все per-pixel буферы одного запекания - хранятся на WorldMapRenderer между вызовами,
    /// т.к. RebakeRegion (кисть) трогает только часть текстуры за раз и должно читать соседние,
    /// ранее запечённые пиксели без их пересчёта.</summary>
    public class MapRasterBuffers
    {
        public int Width, Height;
        public int[] CellId;
        public float[] Elevation;
        public float[] Temperature;
        public Color32[] FamilyColor;
        public Color32[] PreVignette;

        /// <summary>true = суша по сглаженному контуру берега (только Combined+SmoothBorders -
        /// см. CoastlineContour). В прочих режимах не заполняется и не читается.</summary>
        public bool[] IsLand;
    }

    /// <summary>
    /// Запекает клетки Вороного в Texture2D + параллельный cellId-буфер для хит-тестинга.
    /// Height/Region/Biome и Combined-без-сглаживания используют "hard" сэмплинг (ближайшая клетка,
    /// без блендинга, через HardModeColor - визуально идентично старому vertex-color рендеру).
    /// Combined+smoothBorders включает полный "нарисованный" конвейер (см. Task 6 подпроекта 1) +
    /// сглаженный контур берега вместо категоризации по ближайшей клетке (см. CoastlineContour).
    /// </summary>
    public static class MapRasterizer
    {
        public static MapRasterBuffers CreateEmptyBuffers(int width, int height)
        {
            int n = width * height;
            return new MapRasterBuffers
            {
                Width = width,
                Height = height,
                CellId = new int[n],
                Elevation = new float[n],
                Temperature = new float[n],
                FamilyColor = new Color32[n],
                PreVignette = new Color32[n],
                IsLand = new bool[n],
            };
        }

        /// <summary>Удобная обёртка: полный запек всего изображения "с нуля" в новую текстуру.</summary>
        public static Texture2D Bake(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            out MapRasterBuffers buffers)
        {
            var texture = new Texture2D(config.TexWidth, config.TexHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            buffers = CreateEmptyBuffers(config.TexWidth, config.TexHeight);
            RebakeRegion(cells, cellById, lookup, corners, displayMode, config, texture, buffers, 0, 0, config.TexWidth, config.TexHeight);
            return texture;
        }

        /// <summary>Перезапекает прямоугольную под-область текстуры/буферов на месте. rectX/Y/W/H уже
        /// в пиксельных координатах и уже включают отступ под smoothRadius - эта функция не добавляет
        /// собственный отступ (см. WorldMapRenderer.ComputeTouchedPixelRect). Требует, чтобы
        /// вне прямоугольника буферы либо не существовали вовсе (полный запек - rect = всё изображение),
        /// либо уже содержали валидные данные предыдущего полного запека (кисть).</summary>
        public static void RebakeRegion(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            BakeFieldsRect(cells, cellById, lookup, corners, displayMode, config, buffers, rectX, rectY, rectW, rectH);
            ColorAndVignetteRect(cellById, displayMode, config, texture, buffers, rectX, rectY, rectW, rectH);
        }

        /// <summary>Проход 1 (cellId) + проход 1.5 (контур берега + BakePaintedFields, если painted)
        /// для заданного прямоугольника. Трассировка/сглаживание контура (CoastlineContour) всегда
        /// выполняется заново на ВСЕХ клетках карты (дёшево - масштаб числа клеток, не пикселей),
        /// растеризация в IsLand - только в переданный rect (безопасно для частичных перезапеканий
        /// кистью). Сам по себе не читает ничего ЗА пределами rect в буферах, поэтому безопасно
        /// вызывать для части изображения, даже если буферы вне rect ещё вообще не заполнены - в
        /// отличие от ColorAndVignetteRect, которому нужны уже готовые соседние строки/пиксели.</summary>
        public static void BakeFieldsRect(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    var point = PixelToSite(x, y, w, h, config.MapWidth, config.MapHeight);
                    var nearest = lookup.FindNearest(point);
                    buffers.CellId[y * w + x] = nearest.Id;
                }
            }

            if (painted)
            {
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, config.CoastlineSmoothness);
                if (loops.Count == 0)
                {
                    // Нет ни одной петли границы вода/суша - вся карта однородна (все клетки одной
                    // категории). RasterizeIsLand по even-odd правилу залил бы прямоугольник целиком
                    // водой (0 пересечений на строке = "снаружи"), но если хоть одна клетка - суша,
                    // значит воды нет вовсе и правильный результат - вся суша. Достижимо только кистью
                    // (ForceLand на последнюю водную клетку); при генерации край карты всегда топится
                    // falloff'ом, так что петля берега всегда есть. См. финальное ревью фичи, находка #1.
                    bool anyLand = false;
                    foreach (var c in cells)
                        if (!(c.EffectiveIsOcean || c.EffectiveIsLake)) { anyLand = true; break; }

                    for (int y = rectY; y < rectY + rectH; y++)
                        for (int x = rectX; x < rectX + rectW; x++)
                            buffers.IsLand[y * w + x] = anyLand;
                }
                else
                {
                    CoastlineContour.RasterizeIsLand(loops, buffers.IsLand, w, h, config.MapWidth, config.MapHeight, rectX, rectY, rectW, rectH);
                }
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }
        }

        /// <summary>Проход 2 (цвет) + проход 3 (виньетка) для заданного прямоугольника. Требует, чтобы
        /// CellId/Elevation/Temperature/FamilyColor/IsLand уже были заполнены BakeFieldsRect не только
        /// для этого прямоугольника, но и для его непосредственно соседних строк/столбцов (градиент
        /// рельефа и проверка берега читают ±1 пиксель за границу rect).</summary>
        public static void ColorAndVignetteRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    buffers.PreVignette[idx] = painted
                        ? BakePaintedPixel(cell, buffers, idx, x, y, w, h, config)
                        : (Color32)config.HardModeColor(cell);
                }
            }

            ApplyDarknessRect(texture, buffers, config.Darkness, rectX, rectY, rectW, rectH);
        }

        /// <summary>Переприменяет только финальный проход виньетки (шаг 10) поверх уже готовых
        /// PreVignette-пикселей всего изображения - самый дешёвый путь при смене только darkness.</summary>
        public static void ReapplyDarkness(Texture2D texture, MapRasterBuffers buffers, float darkness)
        {
            ApplyDarknessRect(texture, buffers, darkness, 0, 0, buffers.Width, buffers.Height);
        }

        static void ApplyDarknessRect(Texture2D texture, MapRasterBuffers buffers, float darkness, int rectX, int rectY, int rectW, int rectH)
        {
            int w = buffers.Width;
            var outPixels = new Color32[rectW * rectH];

            for (int y = 0; y < rectH; y++)
            {
                int py = rectY + y;
                for (int x = 0; x < rectW; x++)
                {
                    int px = rectX + x;
                    int idx = py * w + px;
                    Color32 c = buffers.PreVignette[idx];

                    float dx = (px + 0.5f) / buffers.Width - 0.5f;
                    float dy = (py + 0.5f) / buffers.Height - 0.5f;
                    float dist01 = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 0.5f);
                    float keep = 1f - dist01 * Mathf.Clamp01(darkness / 100f);

                    outPixels[y * rectW + x] = new Color32(
                        (byte)(c.r * keep), (byte)(c.g * keep), (byte)(c.b * keep), 255);
                }
            }

            texture.SetPixels32(rectX, rectY, rectW, rectH, outPixels);
            texture.Apply(false);
        }

        // ---- Painted-pipeline hooks ----

        /// <summary>Проход 1.5 (только суша, только painted-режим): блендированные elevation/
        /// temperature/базовый цвет семейства среди соседей в радиусе smoothRadius, вес
        /// 1/(distance²+1). Категория суша/вода берётся из уже растеризованной buffers.IsLand
        /// (сглаженный контур - см. BakeFieldsRect), НЕ из cell.EffectiveIsOcean/IsLake напрямую -
        /// иначе пиксель, который сглаженный контур относит к суше, но чья ближайшая клетка
        /// технически вода, никогда не получил бы своего FamilyColor (оставался бы чёрным).</summary>
        static void BakePaintedFields(
            IReadOnlyList<VoronoiCell> cells, IReadOnlyDictionary<int, VoronoiCell> cellById, NearestCellLookup lookup,
            MapRasterConfig config, MapRasterBuffers buffers, int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;

            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    bool isWater = !buffers.IsLand[idx];

                    if (isWater)
                    {
                        buffers.Elevation[idx] = cell.EffectiveElevation;
                        buffers.Temperature[idx] = cell.EffectiveTemperature;
                        continue;
                    }

                    var point = PixelToSite(x, y, w, h, config.MapWidth, config.MapHeight);
                    float sumW = 0f, elev = 0f, temp = 0f, cr = 0f, cg = 0f, cb = 0f;

                    foreach (var (neighbor, distance) in lookup.FindWithinRadius(point, config.SmoothRadius))
                    {
                        if (neighbor.EffectiveIsOcean || neighbor.EffectiveIsLake) continue;
                        float weight = 1f / (distance * distance + 1f);
                        sumW += weight;
                        elev += weight * neighbor.EffectiveElevation;
                        temp += weight * neighbor.EffectiveTemperature;
                        Color32 fc = MapPalette.GetSlotColor(config.Theme, MapPalette.GetFamily(neighbor.Biome));
                        cr += weight * fc.r; cg += weight * fc.g; cb += weight * fc.b;
                    }

                    if (sumW <= 0f)
                    {
                        buffers.Elevation[idx] = cell.EffectiveElevation;
                        buffers.Temperature[idx] = cell.EffectiveTemperature;
                        buffers.FamilyColor[idx] = MapPalette.GetSlotColor(config.Theme, MapPalette.GetFamily(cell.Biome));
                    }
                    else
                    {
                        buffers.Elevation[idx] = elev / sumW;
                        buffers.Temperature[idx] = temp / sumW;
                        buffers.FamilyColor[idx] = new Color32(
                            (byte)Mathf.Clamp(cr / sumW, 0f, 255f),
                            (byte)Mathf.Clamp(cg / sumW, 0f, 255f),
                            (byte)Mathf.Clamp(cb / sumW, 0f, 255f), 255);
                    }
                }
            }
        }

        struct ResolvedPalette
        {
            public Color32 Shallow, Abyss, LakeS, LakeD, Glow, Outline, Light, TintCool, TintWarm;
        }

        static ResolvedPalette ResolvePalette(MapPaletteTheme theme) => new ResolvedPalette
        {
            Shallow = MapPalette.GetSlotColor(theme, PaletteSlot.Shallow),
            Abyss = MapPalette.GetSlotColor(theme, PaletteSlot.Abyss),
            LakeS = MapPalette.GetSlotColor(theme, PaletteSlot.LakeS),
            LakeD = MapPalette.GetSlotColor(theme, PaletteSlot.LakeD),
            Glow = MapPalette.GetSlotColor(theme, PaletteSlot.Glow),
            Outline = MapPalette.GetSlotColor(theme, PaletteSlot.Outline),
            Light = MapPalette.GetSlotColor(theme, PaletteSlot.Light),
            TintCool = MapPalette.GetSlotColor(theme, PaletteSlot.TintCool),
            TintWarm = MapPalette.GetSlotColor(theme, PaletteSlot.TintWarm),
        };

        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            var palette = ResolvePalette(config.Theme);
            float coldAmt = 0.10f + (config.ColdLight / 100f) * 0.30f;
            float varAmt = config.RegionVariation / 100f;

            bool isWater = !buffers.IsLand[idx];
            return isWater
                ? ColorForWaterPixel(cell, buffers, x, y, w, h, config, palette, coldAmt)
                : ColorForLandPixel(buffers, idx, x, y, w, h, config, palette, coldAmt, varAmt);
        }

        static Color32 ColorForWaterPixel(
            VoronoiCell cell, MapRasterBuffers buffers,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt)
        {
            Color32 shallowOrLakeS = cell.EffectiveIsLake ? palette.LakeS : palette.Shallow;
            Color32 deep = cell.EffectiveIsLake ? palette.LakeD : palette.Abyss;

            // Слой рельефа выключен (тумблер MapLayersPanel) - плоский цвет мелководья без
            // градиента глубины, как "рельеф выключен" для суши ниже отключает hillshade.
            if (!config.ShowReliefLayer)
                return ClampColor32(shallowOrLakeS.r, shallowOrLakeS.g, shallowOrLakeS.b);

            float depth = Mathf.Clamp01(config.WaterDepth01(cell));

            float r = Mathf.Lerp(shallowOrLakeS.r, deep.r, depth);
            float g = Mathf.Lerp(shallowOrLakeS.g, deep.g, depth);
            float b = Mathf.Lerp(shallowOrLakeS.b, deep.b, depth);

            if (!cell.EffectiveIsLake)
            {
                float ripple = (Noise.Fbm(x / 40f, y / 26f, config.Seed + 401, 2) - 0.5f) * 10f;
                r += ripple; g += ripple; b += ripple;
            }

            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: false))
            {
                float gk = 0.32f + coldAmt * 0.5f;
                r += (palette.Glow.r - r) * gk;
                g += (palette.Glow.g - g) * gk;
                b += (palette.Glow.b - b) * gk;
            }

            return ClampColor32(r, g, b);
        }

        static Color32 ColorForLandPixel(
            MapRasterBuffers buffers, int idx,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt, float varAmt)
        {
            // Слой биомов выключен (тумблер MapLayersPanel) - нейтральная земляная заливка вместо
            // цвета семейства биома, как старый GetNeutralBaseColor для суши.
            Color32 fam = config.ShowBiomeLayer ? buffers.FamilyColor[idx] : new Color32(209, 199, 166, 255);
            float r = fam.r, g = fam.g, b = fam.b;

            // Региональная тонировка (шаг 5а) - к tintCool/tintWarm по температуре, вес 0.38 фиксирован.
            float temperature = buffers.Temperature[idx];
            float wn = Mathf.InverseLerp(0.28f, 0.70f, temperature);
            float tr = Mathf.Lerp(palette.TintCool.r, palette.TintWarm.r, wn);
            float tg = Mathf.Lerp(palette.TintCool.g, palette.TintWarm.g, wn);
            float tb = Mathf.Lerp(palette.TintCool.b, palette.TintWarm.b, wn);
            r += (tr - r) * 0.38f; g += (tg - g) * 0.38f; b += (tb - b) * 0.38f;

            // Региональная вариация - крупнозернистый цветовой шум (шаг 5б).
            if (varAmt > 0f)
            {
                float nx = x / (float)w, ny = y / (float)h;
                float rgA = Noise.Fbm(nx * 1.6f + 20f, ny * 1.6f + 40f, config.Seed + 1500, 2);
                float rr = (rgA - 0.5f) * 38f * varAmt;
                r += rr; g += rr * 0.9f; b += rr * 0.7f;
            }

            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: true))
            {
                // Береговая обводка (шаг 7, сторона суши) - жёсткая замена, перекрывает hillshade.
                r = palette.Outline.r; g = palette.Outline.g; b = palette.Outline.b;
            }
            else if (config.ShowReliefLayer)
            {
                // Рельеф + холодный лунный подсвет (шаг 6).
                float gradX = (buffers.Elevation[ClampIdx(x - 1, y, w, h)] - buffers.Elevation[ClampIdx(x + 1, y, w, h)]) * 0.5f;
                float gradY = (buffers.Elevation[ClampIdx(x, y - 1, w, h)] - buffers.Elevation[ClampIdx(x, y + 1, w, h)]) * 0.5f;
                float brightness = RegionColorPalette.HillshadeBrightness(
                    gradX, gradY, config.ReliefStrength, config.ReliefLightAzimuth, config.ReliefAmbient, out float ndotl);

                r = r * brightness + palette.Light.r * ndotl * coldAmt;
                g = g * brightness + palette.Light.g * ndotl * coldAmt;
                b = b * brightness + palette.Light.b * ndotl * coldAmt;
            }
            // else: слой рельефа выключен - оставляем базовый (тонированный) цвет без hillshade.

            // Зерно (шаг 8) - применяется всегда, включая береговую обводку (как в референсе).
            float grain = (Noise.ValueNoise(x * 0.5f, y * 0.5f, config.Seed + 61) - 0.5f) * 7f;
            r += grain; g += grain; b += grain;

            // Дополнительная лайтнесс-вариация (шаг 9, только суша).
            if (varAmt > 0f)
            {
                float nx = x / (float)w, ny = y / (float)h;
                float rgB = Noise.Fbm(nx * 2.0f + 50f, ny * 2.0f + 70f, config.Seed + 1600, 2) - 0.5f;
                float lf = 1f + rgB * 0.24f * varAmt;
                r *= lf; g *= lf; b *= lf;
            }

            return ClampColor32(r, g, b);
        }

        static bool HasNeighborWithWaterStatus(
            MapRasterBuffers buffers, int x, int y, int w, int h, bool wantWater)
        {
            return Check(ClampIdx(x - 1, y, w, h)) || Check(ClampIdx(x + 1, y, w, h))
                || Check(ClampIdx(x, y - 1, w, h)) || Check(ClampIdx(x, y + 1, w, h));

            bool Check(int idx)
            {
                bool isWaterPixel = !buffers.IsLand[idx];
                return isWaterPixel == wantWater;
            }
        }

        static int ClampIdx(int x, int y, int w, int h) => Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1);

        static Color32 ClampColor32(float r, float g, float b) => new Color32(
            (byte)Mathf.Clamp(r, 0f, 255f), (byte)Mathf.Clamp(g, 0f, 255f), (byte)Mathf.Clamp(b, 0f, 255f), 255);

        static System.Numerics.Vector2 PixelToSite(int x, int y, int w, int h, float mapWidth, float mapHeight)
        {
            float px = (x + 0.5f) / w * mapWidth;
            float pz = (y + 0.5f) / h * mapHeight;
            return new System.Numerics.Vector2(px, pz);
        }
    }
}
