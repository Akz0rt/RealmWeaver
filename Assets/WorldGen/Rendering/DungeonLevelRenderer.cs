using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Bakes a DungeonLevel grid into a Texture2D (art-light two-tone). Grid row 0 is the TOP
    /// of the map, so it is written to the TOP texture rows (Texture2D row 0 is the bottom → flip Y).</summary>
    public static class DungeonLevelRenderer
    {
        static readonly Color32 FloorCol = new Color32(214, 205, 186, 255); // light stone
        static readonly Color32 WallCol  = new Color32(38, 34, 30, 255);    // dark

        public static Texture2D Bake(DungeonLevel lvl, int pxPerTile)
        {
            int tw = lvl.Width * pxPerTile, th = lvl.Height * pxPerTile;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[tw * th];
            for (int gy = 0; gy < lvl.Height; gy++)
                for (int gx = 0; gx < lvl.Width; gx++)
                {
                    var col = lvl.Get(gx, gy) == DungeonTile.Floor ? FloorCol : WallCol;
                    int texRow0 = (lvl.Height - 1 - gy) * pxPerTile;   // flip Y
                    for (int yy = 0; yy < pxPerTile; yy++)
                        for (int xx = 0; xx < pxPerTile; xx++)
                            px[(texRow0 + yy) * tw + (gx * pxPerTile + xx)] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Map a pointer position (normalized 0..1 within the map RawImage rect, origin
        /// bottom-left) to a grid cell. Returns false if outside. Y is flipped back to grid space.</summary>
        public static bool NormalizedToCell(DungeonLevel lvl, float nx, float ny, out int gx, out int gy)
        {
            gx = Mathf.FloorToInt(nx * lvl.Width);
            gy = Mathf.FloorToInt((1f - ny) * lvl.Height);   // top row = grid y 0
            return lvl.InBounds(gx, gy);
        }
    }
}
