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
    }

    /// <summary>
    /// Запекает клетки Вороного в Texture2D + параллельный cellId-буфер для хит-тестинга.
    /// Height/Region/Biome и Combined-без-сглаживания используют "hard" сэмплинг (ближайшая клетка,
    /// без блендинга, через HardModeColor - визуально идентично старому vertex-color рендеру).
    /// Combined+smoothBorders включает полный "нарисованный" конвейер (см. Task 6).
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
            };
        }

        /// <summary>Удобная обёртка: полный запек всего изображения "с нуля" в новую текстуру.</summary>
        public static Texture2D Bake(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
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
            RebakeRegion(cells, cellById, lookup, displayMode, config, texture, buffers, 0, 0, config.TexWidth, config.TexHeight);
            return texture;
        }

        /// <summary>Перезапекает прямоугольную под-область текстуры/буферов на месте. rectX/Y/W/H уже
        /// в пиксельных координатах и уже включают отступ под smoothRadius - эта функция не добавляет
        /// собственный отступ (см. WorldMapRenderer.ComputeTouchedPixelRect в Task 7/8).</summary>
        public static void RebakeRegion(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

            // Проход 1: ближайшая клетка на пиксель (cellId-буфер) - нужен всегда.
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
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }

            // Проход финальной раскраски (до виньетки - кэшируется в PreVignette).
            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    buffers.PreVignette[idx] = painted
                        ? BakePaintedPixel(cell, buffers, cellById, idx, x, y, w, h, config)
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

        // ---- Painted-pipeline hooks - stubbed here, implemented in Task 6 ----

        static void BakePaintedFields(
            IReadOnlyList<VoronoiCell> cells, IReadOnlyDictionary<int, VoronoiCell> cellById, NearestCellLookup lookup,
            MapRasterConfig config, MapRasterBuffers buffers, int rectX, int rectY, int rectW, int rectH)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }

        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }

        static System.Numerics.Vector2 PixelToSite(int x, int y, int w, int h, float mapWidth, float mapHeight)
        {
            float px = (x + 0.5f) / w * mapWidth;
            float pz = (y + 0.5f) / h * mapHeight;
            return new System.Numerics.Vector2(px, pz);
        }
    }
}
