using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Parameters for generating a settlement. Size is a single knob (TargetBuildings); the wall
    /// radius and gate count derive from it, so one generator spans a hamlet to a capital.</summary>
    public class SettlementConfig
    {
        public int Seed;
        public int TargetBuildings = 40;
        public bool HasWall = true;
    }

    /// <summary>A gate position on the wall, in normalized space. Becomes a Room node (TypeId 0) at assembly.</summary>
    public struct GatePoint { public float X, Y; }

    /// <summary>Deterministic settlement geometry: wall, gates, building placement (Tasks 2–3), and assembly
    /// into an InteriorData (Task 5). Pure, no Unity — the whole point is headless testability, since the
    /// dungeon packer measured 18–48 overlapping pairs at 40 nodes and cannot be reused.</summary>
    public static class SettlementGenerator
    {
        public const int WallSides = 9;
        public const float WallJitter = 0.12f;

        /// <summary>Wall radius (normalized) for a building count: bigger towns need more room. Clamped so a
        /// wall always fits inside the 0..1 canvas with margin.</summary>
        public static float WallRadiusFor(int buildingCount)
        {
            float r = 0.16f + 0.0045f * buildingCount;   // ~0.2 at 8, ~0.34 at 40, ~0.43 at 60
            return r > 0.45f ? 0.45f : r;
        }

        /// <summary>Gate count for a building count: 2 for a small town, up to 4 for a large one.</summary>
        public static int GateCountFor(int buildingCount)
        {
            if (buildingCount >= 55) return 4;
            if (buildingCount >= 30) return 3;
            return 2;
        }

        public static WallContour BuildWall(SettlementConfig cfg)
        {
            if (!cfg.HasWall) return null;
            return WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, WallRadiusFor(cfg.TargetBuildings), WallSides, WallJitter);
        }

        /// <summary>Gates spread around the wall: sample the contour at evenly-spaced parameter positions
        /// (offset by a seeded phase so towns differ), each landing exactly on a wall segment.</summary>
        public static List<GatePoint> PlaceGates(WallContour wall, int seed)
        {
            var gates = new List<GatePoint>();
            if (wall == null || !wall.IsClosedSane()) return gates;

            // Perimeter length so gates are spread by ARC LENGTH, not by vertex index (a jittered polygon has
            // uneven sides; index-spacing would cluster gates on the short sides).
            int n = wall.Points.Count;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                cum[i + 1] = cum[i] + (float)System.Math.Sqrt(dx * dx + dy * dy);
            }
            float total = cum[n];

            var rng = new System.Random(seed * 31 + 17);
            int gateCount = GateCountForRadiusSafe(wall);
            float phase = (float)rng.NextDouble() * total;
            for (int g = 0; g < gateCount; g++)
            {
                float target = (phase + total * g / gateCount) % total;
                gates.Add(PointAtArcLength(wall, cum, target));
            }
            return gates;
        }

        // Gate count is a property of the TOWN, but PlaceGates only sees the wall. It is re-derived from the
        // wall's own size class so PlaceGates stays a pure (wall, seed) function; the assembly path (Task 5)
        // passes the config-derived count where it matters. Keep both in sync via GateCountFor.
        static int GateCountForRadiusSafe(WallContour wall)
        {
            // Approximate building count back out of the radius bucket used by WallRadiusFor.
            float r = 0f;
            foreach (var p in wall.Points) { float dx = p.X - 0.5f, dy = p.Y - 0.5f; float d = (float)System.Math.Sqrt(dx*dx+dy*dy); if (d > r) r = d; }
            if (r >= 0.40f) return 4;
            if (r >= 0.30f) return 3;
            return 2;
        }

        static GatePoint PointAtArcLength(WallContour wall, float[] cum, float target)
        {
            int n = wall.Points.Count;
            for (int i = 0; i < n; i++)
            {
                if (target <= cum[i + 1] || i == n - 1)
                {
                    var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                    float segLen = cum[i + 1] - cum[i];
                    float t = segLen <= 0f ? 0f : (target - cum[i]) / segLen;
                    return new GatePoint { X = a.X + t * (b.X - a.X), Y = a.Y + t * (b.Y - a.Y) };
                }
            }
            return new GatePoint { X = wall.Points[0].X, Y = wall.Points[0].Y };
        }
    }
}
