using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure, headless layout services over a DungeonLevel graph: cascade non-overlap separation
    /// (Separate) and derived corridor-crossing junctions (BuildRenderGraph, Task 3). Positions are
    /// normalized 0..1; sizes are in tiles; all math runs in TILE space via TilesPerAxis. No Unity types.</summary>
    public static class DungeonLayout
    {
        public const int TilesPerAxis = 48;   // normalized 0..1 spans this many tiles (bridges pos↔size units)

        static float ToTile(float norm) => norm * TilesPerAxis;
        static float ToNorm(float tile) => tile / TilesPerAxis;

        /// <summary>Push overlapping room footprints apart (cascade) until none overlap with a minGapTiles
        /// clearance, or maxIterations is reached. Deterministic. Mutates Room.X/Y (kept in [0,1]).</summary>
        public static void Separate(DungeonLevel lvl, float minGapTiles = 1f, int maxIterations = 40)
        {
            if (lvl == null || lvl.Rooms.Count < 2) return;
            var rooms = lvl.Rooms;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool anyOverlap = false;
                for (int i = 0; i < rooms.Count; i++)
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        var a = rooms[i]; var b = rooms[j];
                        float ax = ToTile(a.X), ay = ToTile(a.Y), bx = ToTile(b.X), by = ToTile(b.Y);
                        float halfW = (a.SizeW + b.SizeW) * 0.5f + minGapTiles;   // min center distance on X
                        float halfH = (a.SizeH + b.SizeH) * 0.5f + minGapTiles;   // …and Y for no overlap
                        float dx = bx - ax, dy = by - ay;
                        float overlapX = halfW - Math.Abs(dx);
                        float overlapY = halfH - Math.Abs(dy);
                        if (overlapX <= 0f || overlapY <= 0f) continue;           // not overlapping
                        anyOverlap = true;
                        // Push apart along the axis of LEAST penetration (smallest shove), split evenly.
                        if (overlapX < overlapY)
                        {
                            float push = (overlapX * 0.5f + 0.01f) * (dx >= 0f ? 1f : -1f);
                            ax -= push; bx += push;
                        }
                        else
                        {
                            float push = (overlapY * 0.5f + 0.01f) * (dy >= 0f ? 1f : -1f);
                            ay -= push; by += push;
                        }
                        a.X = Clamp01(ToNorm(ax)); a.Y = Clamp01(ToNorm(ay));
                        b.X = Clamp01(ToNorm(bx)); b.Y = Clamp01(ToNorm(by));
                    }
                if (!anyOverlap) break;
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
