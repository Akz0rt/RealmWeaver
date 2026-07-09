using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Сменяемый источник арта: один RGBA32-атлас + UV-rect на (type, style, artVariant).
    /// v1 — процедурные плейсхолдеры; позже baker пакует готовые Sprite'ы, движок/рендерер те же.</summary>
    public class DecorationCatalog
    {
        public Texture2D Atlas { get; private set; }

        struct Slot { public int col, row; }
        readonly Dictionary<(DecorationType, DecorationStyleCategory), List<Slot>> slots = new();
        int cols, rows, tile;

        // Какие (type, style) существуют и сколько вариантов у каждого.
        static readonly (DecorationType t, DecorationStyleCategory s, int variants)[] Layout =
        {
            (DecorationType.Mountain, DecorationStyleCategory.Bare, 3),
            (DecorationType.Mountain, DecorationStyleCategory.Snowy, 3),
            (DecorationType.Mountain, DecorationStyleCategory.Forested, 3),
            (DecorationType.Hill, DecorationStyleCategory.Bare, 1),
            (DecorationType.Hill, DecorationStyleCategory.Snowy, 1),
            (DecorationType.Hill, DecorationStyleCategory.Forested, 1),
            (DecorationType.Pine, DecorationStyleCategory.Plain, 1),
            (DecorationType.Pine, DecorationStyleCategory.Snowy, 1),
            (DecorationType.AutumnTree, DecorationStyleCategory.Plain, 1),
            (DecorationType.Mesa, DecorationStyleCategory.Plain, 1),
        };

        public static DecorationCatalog BuildPlaceholder(int tile = 64)
        {
            var c = new DecorationCatalog { tile = tile };
            int total = 0;
            foreach (var l in Layout) total += l.variants;
            c.cols = Mathf.CeilToInt(Mathf.Sqrt(total));
            c.rows = Mathf.CeilToInt(total / (float)c.cols);
            c.Atlas = new Texture2D(c.cols * tile, c.rows * tile, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[c.cols * tile * c.rows * tile];
            c.Atlas.SetPixels32(clear);

            int idx = 0;
            foreach (var l in Layout)
            {
                var list = new List<Slot>();
                for (int v = 0; v < l.variants; v++)
                {
                    int col = idx % c.cols, row = idx / c.cols;
                    var tilePx = DecorationPlaceholderFactory.DrawTile(l.t, l.s, tile, v);
                    // Тайл рисуется в координатах «y вниз»; текстура — «y вверх». Флипаем по Y при заливке.
                    var flipped = new Color32[tile * tile];
                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile; x++)
                            flipped[(tile - 1 - y) * tile + x] = tilePx[y * tile + x];
                    c.Atlas.SetPixels32(col * tile, row * tile, tile, tile, flipped);
                    list.Add(new Slot { col = col, row = row });
                    idx++;
                }
                c.slots[(l.t, l.s)] = list;
            }
            c.Atlas.Apply(false);
            return c;
        }

        public int VariantCount(DecorationType t, DecorationStyleCategory s)
            => slots.TryGetValue((t, s), out var l) ? l.Count : 0;

        /// <summary>UV-rect (x,y = смещение, z,w = размер) для (type, style, artVariant).
        /// Fallback на первый существующий стиль типа, если (type,style) не в раскладке.</summary>
        public Vector4 UvRect(DecorationType t, DecorationStyleCategory s, int artVariant)
        {
            if (!slots.TryGetValue((t, s), out var list) || list.Count == 0)
            {
                // fallback: любой стиль этого типа
                foreach (var kv in slots) if (kv.Key.Item1 == t && kv.Value.Count > 0) { list = kv.Value; break; }
                if (list == null || list.Count == 0) return new Vector4(0, 0, 1f / cols, 1f / rows);
            }
            var slot = list[((artVariant % list.Count) + list.Count) % list.Count];
            return new Vector4(slot.col / (float)cols, slot.row / (float)rows, 1f / cols, 1f / rows);
        }
    }
}
