namespace WorldGen.Generation
{
    /// <summary>Type-driven default room footprints (in tiles) + clamps. Pure, headless. The DM overrides
    /// per room via the inspector; these are just the starting sizes on generation / a fresh room / migration.</summary>
    public static class RoomSizing
    {
        public const int MinSide = 1;
        public const int MaxSide = 8;

        public static (int w, int h) Default(RoomType type)
        {
            switch (type)
            {
                case RoomType.Entrance: return (4, 3);
                case RoomType.Boss:     return (5, 5);
                default:                return (3, 3);   // Normal
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
