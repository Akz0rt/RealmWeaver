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

        /// <summary>Per-type footprint bounds (tiles, inclusive) used when GENERATING a floor. Kept
        /// separate from Default: Default is the fixed size a hand-added or migrated room gets, while
        /// Range is the spread the generator rolls within, so no two generated dungeons look alike.
        /// Every Default must lie inside its own Range (self-tested).</summary>
        public static (int min, int max) Range(RoomType type)
        {
            switch (type)
            {
                case RoomType.Entrance: return (5, 8);
                case RoomType.Boss:     return (8, 14);
                default:                return (4, 8);   // Normal
            }
        }

        /// <summary>Roll one generated room's footprint. W and H roll INDEPENDENTLY within the type's
        /// Range, so a floor gets both square and elongated chambers rather than N scaled squares.
        ///
        /// Takes the CALLER's seeded System.Random on purpose — DungeonGraphGenerator.Generate is
        /// documented deterministic by seed and self-tested for it. Drawing from UnityEngine.Random here
        /// would make the same seed produce different dungeons, and would only surface as a flaky test.
        ///
        /// Named Roll, not Random: a static Random(RoomType, System.Random) member invites exactly the
        /// same-name shadowing this codebase has already been bitten by more than once.</summary>
        public static (int w, int h) Roll(RoomType type, System.Random rng)
        {
            var (min, max) = Range(type);
            return (Clamp(rng.Next(min, max + 1)), Clamp(rng.Next(min, max + 1)));   // max+1: Next's upper bound is exclusive
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
