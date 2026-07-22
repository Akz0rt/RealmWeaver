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

    /// <summary>A placed building's centre, normalized. Becomes a Room node (TypeId 1) at assembly.</summary>
    public struct PlacedBuilding { public float X, Y; }

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

        /// <summary>Place `gateCount` gates spread around the wall by ARC LENGTH (offset by a seeded phase so
        /// towns differ), each landing exactly on a wall segment. `gateCount` is supplied by the caller (via
        /// GateCountFor), so there is ONE source of truth for the count — PlaceGates never re-derives it.</summary>
        public static List<GatePoint> PlaceGates(WallContour wall, int gateCount, int seed)
        {
            var gates = new List<GatePoint>();
            if (wall == null || !wall.IsClosedSane() || gateCount <= 0) return gates;

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
            float phase = (float)rng.NextDouble() * total;
            for (int g = 0; g < gateCount; g++)
            {
                float target = (phase + total * g / gateCount) % total;
                gates.Add(PointAtArcLength(wall, cum, target));
            }
            return gates;
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

        /// <summary>Normalized pitch of the building grid. One building per cell, so no two are closer than
        /// this — the anti-overlap guarantee that replaces the dungeon packer.</summary>
        public const float BuildingCell = 0.07f;

        public static List<PlacedBuilding> PlaceBuildings(WallContour wall, int seed, int targetCount)
        {
            var kept = new List<PlacedBuilding>();
            if (wall == null || !wall.IsClosedSane()) return kept;

            // Bounding box of the wall.
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            foreach (var p in wall.Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }

            float half = BuildingCell * 0.5f;
            // Cell centres on a regular grid; keep those inside the wall and clear of the line.
            for (float cy = minY + half; cy <= maxY - half + 1e-6f; cy += BuildingCell)
                for (float cx = minX + half; cx <= maxX - half + 1e-6f; cx += BuildingCell)
                    if (wall.Contains(cx, cy) && wall.DistanceToEdge(cx, cy) >= half)
                        kept.Add(new PlacedBuilding { X = cx, Y = cy });

            // Deterministic Fisher–Yates shuffle so the kept-but-dropped buildings vary by seed, then trim.
            var rng = new System.Random(seed * 131 + 71);
            for (int i = kept.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kept[i], kept[j]) = (kept[j], kept[i]);
            }
            if (kept.Count > targetCount) kept.RemoveRange(targetCount, kept.Count - targetCount);
            return kept;
        }
    }
}
