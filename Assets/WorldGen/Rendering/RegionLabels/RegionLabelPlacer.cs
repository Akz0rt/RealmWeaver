using System;
using System.Collections.Generic;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster; // BiomeFamily, RegionCategories, NearestCellLookup

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Pure, deterministic. Groups adjacent land cells of the same BiomeFamily into connected
    /// patches (BFS over NeighborIds) and emits one Latin-named label per patch >= minPatchCells, at the
    /// area-weighted centroid. Adds 1-2 sea labels at open-ocean anchor points. No Random.</summary>
    public static class RegionLabelPlacer
    {
        public const int DefaultMinPatchCells = 6;

        static readonly Dictionary<BiomeFamily, string> LandNames = new Dictionary<BiomeFamily, string>
        {
            { BiomeFamily.Forest,     "SILVA UMBRARUM" },
            { BiomeFamily.ForestWarm, "SILVA IGNEA" },
            { BiomeFamily.Badlands,   "VASTA CINERIS" },
            { BiomeFamily.Plains,     "CAMPI CANI" },
            { BiomeFamily.Highland,   "DORSUM CORVI" },
            { BiomeFamily.Snow,       "NIX AETERNA" },
            { BiomeFamily.Moor,       "PALUS NIGRA" },
            { BiomeFamily.Tundra,     "GLACIES" },
            // Coast, Lake, Sea intentionally absent -> unnamed (skipped / sea handled separately).
        };

        public static List<RegionLabelData> Place(IReadOnlyList<VoronoiCell> cells,
            NearestCellLookup nearest, float mapWidth, float mapHeight, int minPatchCells = DefaultMinPatchCells)
        {
            var result = new List<RegionLabelData>();
            if (cells == null || cells.Count == 0) return result;

            var byId = new Dictionary<int, VoronoiCell>();
            foreach (var c in cells) byId[c.Id] = c;

            var visited = new HashSet<int>();
            foreach (var start in cells)
            {
                if (visited.Contains(start.Id)) continue;
                int fam = RegionCategories.FamilyCategoryOf(start);
                if (fam < 0) { visited.Add(start.Id); continue; } // water: skip (marked visited so we don't re-scan)

                // BFS connected component of the same family.
                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        if (RegionCategories.FamilyCategoryOf(nc) != fam) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }

                if (comp.Count < minPatchCells) continue;
                if (!LandNames.TryGetValue((BiomeFamily)fam, out var name)) continue; // Coast etc. unnamed

                result.Add(new RegionLabelData
                {
                    Text = name,
                    WorldPosition = AreaWeightedCentroid(comp),
                    SeedFamily = (BiomeFamily)fam,
                });
            }

            AddSeaLabels(result, nearest, mapWidth, mapHeight);
            return result;
        }

        static System.Numerics.Vector2 AreaWeightedCentroid(List<VoronoiCell> comp)
        {
            double sx = 0, sy = 0, sw = 0;
            foreach (var c in comp)
            {
                float w = PolygonArea(c.Polygon);
                if (w <= 0f) w = 1f;
                sx += (double)c.Site.X * w; sy += (double)c.Site.Y * w; sw += w;
            }
            if (sw <= 0) return comp[0].Site;
            return new System.Numerics.Vector2((float)(sx / sw), (float)(sy / sw));
        }

        static float PolygonArea(List<System.Numerics.Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                a += (double)p.X * q.Y - (double)q.X * p.Y;
            }
            return (float)(Math.Abs(a) * 0.5);
        }

        // Two candidate open-ocean anchors (handoff normalized positions). Emit a label only if the
        // nearest cell there is actually water -> avoids labels on the continent for oddly-shaped maps.
        static void AddSeaLabels(List<RegionLabelData> result, NearestCellLookup nearest, float mapW, float mapH)
        {
            if (nearest == null) return;
            (float nx, float ny, string name)[] cands =
            {
                (0.135f, 0.43f, "MARE GELIDUM"),
                (0.835f, 0.90f, "OCEANUS UMBRAE"),
            };
            foreach (var (nx, ny, name) in cands)
            {
                var pos = new System.Numerics.Vector2(nx * mapW, ny * mapH);
                var cell = nearest.FindNearest(pos);
                if (cell != null && cell.EffectiveIsOcean)
                    result.Add(new RegionLabelData { Text = name, WorldPosition = pos, SeedFamily = BiomeFamily.Sea });
            }
        }
    }
}
