namespace WorldGen.Generation
{
    /// <summary>Builds a room's starting battle map. Pure and deterministic: the same room always yields
    /// the same grid, which is why an untouched grid need not be persisted at all (Room.Grid stays null
    /// until the DM edits it) and still looks identical in the next session.</summary>
    public static class BattleGridGenerator
    {
        /// <summary>The grid size a room's CURRENT footprint calls for: the footprint plus a one-cell wall
        /// ring on each side, clamped. The ring is added OUTSIDE the contour rather than carved out of it —
        /// carving would leave a 4x4 room with 2x2 of usable floor.</summary>
        public static (int w, int h) NaturalSize(Room room)
        {
            var (fw, fh) = DungeonProjection.EffectiveSize(room);
            return (BattleGridCodec.Clamp(fw + 2), BattleGridCodec.Clamp(fh + 2));
        }

        public static GridBuffer Generate(Room room)
        {
            var (w, h) = NaturalSize(room);
            var buf = new GridBuffer(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    buf.Set(x, y, (x == 0 || y == 0 || x == w - 1 || y == h - 1) ? GridCell.Wall : GridCell.Floor);
            return buf;
        }
    }
}
