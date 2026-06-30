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
            { PoiType.City,     new Color32(200, 160,  32, 255) },
            { PoiType.Ruin,     new Color32(136, 136, 136, 255) },
            { PoiType.Dungeon,  new Color32(139,  26,  26, 255) },
            { PoiType.Fortress, new Color32( 74,  96, 128, 255) },
        };

        // 5x7 pixel glyphs. glyphs[type][row, col], row 0 = top, true = white pixel.
        static readonly Dictionary<PoiType, bool[,]> glyphs = new Dictionary<PoiType, bool[,]>
        {
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
            const int size = 32;
            const float radius = 14f;
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.name = $"PoiPlaceholder_{type}";

            var baseColor = typeColors[type];
            var transparent = new Color32(0, 0, 0, 0);

            // Draw filled circle
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    tex.SetPixel(x, y, dx * dx + dy * dy <= radius * radius ? baseColor : transparent);
                }

            // Overlay 5x7 glyph centered in circle
            var glyph = glyphs[type];
            int startX = (size - 5) / 2;   // = 13
            int startY = (size - 7) / 2;   // = 12 (glyph row 0 = top of glyph = higher Y in texture)
            for (int row = 0; row < 7; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (!glyph[row, col]) continue;
                    int px = startX + col;
                    int py = size - 1 - (startY + row); // flip: row 0 (top) → high Y in texture
                    if (px >= 0 && px < size && py >= 0 && py < size)
                        tex.SetPixel(px, py, Color.white);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
