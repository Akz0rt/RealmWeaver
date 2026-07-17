namespace WorldGen.Generation
{
    /// <summary>Type-driven default room footprints (in tiles) + clamps. Pure, headless. The DM overrides
    /// per room via the inspector; these are just the starting sizes on generation / a fresh room / migration.</summary>
    public static class RoomSizing
    {
        public const int MinSide = 1;
        // Raised from 8 (spec R7): a 3×3 room on a 48×48 field read as a speck once the render stopped
        // under-drawing footprints. Bigger rooms + a compacting generator are what make the map read as a
        // dungeon (big chambers, short passages) rather than dots on a void.
        public const int MaxSide = 16;

        public static (int w, int h) Default(RoomType type)
        {
            switch (type)
            {
                case RoomType.Entrance: return (7, 5);
                case RoomType.Boss:     return (10, 10);
                default:                return (6, 6);   // Normal
            }
        }

        public static int Clamp(int side) => side < MinSide ? MinSide : (side > MaxSide ? MaxSide : side);

        /// <summary>Give every room with a non-positive footprint its type default (migration / new rooms).
        /// Idempotent — leaves already-sized rooms untouched.</summary>
        public static void ApplyDefaults(DungeonData dungeon)
        {
            if (dungeon == null) return;
            foreach (var lvl in dungeon.Levels)
                foreach (var r in lvl.Rooms)
                    if (r.SizeW <= 0 || r.SizeH <= 0)
                    {
                        var (w, h) = Default(r.Type);
                        r.SizeW = w; r.SizeH = h;
                    }
        }
    }
}
