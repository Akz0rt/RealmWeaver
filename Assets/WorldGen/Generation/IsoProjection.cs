namespace WorldGen.Generation
{
    /// <summary>2D screen-space isometric projection (no 3D). Tile-space (x,y) → screen (sx,sy); plus a
    /// painter's-order depth key. Pure, headless.</summary>
    public static class IsoProjection
    {
        public static (float sx, float sy) ToScreen(float tileX, float tileY, float tileW, float tileH)
            => ((tileX - tileY) * tileW * 0.5f, (tileX + tileY) * tileH * 0.5f);

        // Painter's order: draw farthest/lowest first. Larger key = nearer/higher = drawn later (on top).
        public static float DepthKey(float tileX, float tileY, float height)
            => tileX + tileY + height * 0.001f;
    }
}
