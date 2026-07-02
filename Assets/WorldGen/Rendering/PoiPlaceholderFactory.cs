using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Generates a 32x32 sprite per PoiType at first request and caches it.
    /// Each sprite is a colored circle with a 5x7 pixel Cyrillic glyph (Г/Р/Д/К).
    /// No external assets required.
    /// </summary>
    public static class PoiPlaceholderFactory
    {
        static readonly Dictionary<PoiType, Sprite> cache = new Dictionary<PoiType, Sprite>();

        static readonly Dictionary<PoiType, Color32> typeColors = new Dictionary<PoiType, Color32>
        {
            { PoiType.Unknown,  new Color32(100, 100, 100, 255) },
            { PoiType.City,     new Color32(200, 160,  32, 255) },
            { PoiType.Ruin,     new Color32(136, 136, 136, 255) },
            { PoiType.Dungeon,  new Color32(139,  26,  26, 255) },
            { PoiType.Fortress, new Color32( 74,  96, 128, 255) },
        };

        // 5x7 pixel glyphs. glyphs[type][row, col], row 0 = top, true = white pixel.
        static readonly Dictionary<PoiType, bool[,]> glyphs = new Dictionary<PoiType, bool[,]>
        {
            [PoiType.Unknown] = new bool[,]  // ?
            {
                { false, true,  true,  true,  false },
                { true,  false, false, false, true  },
                { false, false, false, false, true  },
                { false, false, true,  true,  false },
                { false, false, true,  false, false },
                { false, false, false, false, false },
                { false, false, true,  false, false },
            },
            [PoiType.City] = new bool[,]  // Г
            {
                { true,  true,  true,  true,  true  },
                { true,  false, false, false, false },
                { true,  true,  false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
            },
            [PoiType.Ruin] = new bool[,]  // Р
            {
                { true,  true,  true,  false, false },
                { true,  false, false, true,  false },
                { true,  false, false, true,  false },
                { true,  true,  true,  false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
            },
            [PoiType.Dungeon] = new bool[,]  // Д
            {
                { false, true,  true,  true,  false },
                { false, true,  false, true,  false },
                { false, true,  false, true,  false },
                { false, true,  false, true,  false },
                { true,  true,  true,  true,  true  },
                { true,  false, false, false, true  },
                { false, false, false, false, false },
            },
            [PoiType.Fortress] = new bool[,]  // К
            {
                { true,  false, false, true,  false },
                { true,  false, true,  false, false },
                { true,  true,  false, false, false },
                { true,  true,  false, false, false },
                { true,  false, true,  false, false },
                { true,  false, false, true,  false },
                { true,  false, false, false, true  },
            },
        };

        public static Sprite GetPlaceholder(PoiType type)
        {
            if (cache.TryGetValue(type, out var cached)) return cached;
            var sprite = Build(type);
            cache[type] = sprite;
            return sprite;
        }

        static Sprite Build(PoiType type)
        {
            const int size = 64;
            const float outlineWidth = 2f; // dark ring around the fill for contrast against any terrain
            const float fillRadius = size / 2f - 1f - outlineWidth;
            const float outlineRadius = size / 2f - 1f;
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.name = $"PoiPlaceholder_{type}";

            var baseColor = typeColors[type];
            var outlineColor = new Color32(15, 15, 15, 255);
            var transparent = new Color32(0, 0, 0, 0);

            // Draw filled circle with a dark outline ring
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float distSq = dx * dx + dy * dy;
                    Color32 px = distSq <= fillRadius * fillRadius ? baseColor
                        : distSq <= outlineRadius * outlineRadius ? outlineColor
                        : transparent;
                    tex.SetPixel(x, y, px);
                }

            // Overlay 5x7 glyph, scaled up and centered in circle
            var glyph = glyphs[type];
            const int glyphScale = 4;
            int glyphW = 5 * glyphScale, glyphH = 7 * glyphScale;
            int startX = (size - glyphW) / 2;
            int startY = (size - glyphH) / 2;
            for (int row = 0; row < 7; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (!glyph[row, col]) continue;
                    for (int sy = 0; sy < glyphScale; sy++)
                        for (int sx = 0; sx < glyphScale; sx++)
                        {
                            int px = startX + col * glyphScale + sx;
                            int py = size - 1 - (startY + row * glyphScale + sy); // flip: row 0 (top) → high Y in texture
                            if (px >= 0 && px < size && py >= 0 && py < size)
                                tex.SetPixel(px, py, Color.white);
                        }
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
