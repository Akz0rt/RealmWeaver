using System;
using System.Collections.Generic;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster; // BiomeFamily, RegionCategories, NearestCellLookup

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Pure, deterministic. Groups adjacent land cells of the same BiomeFamily into connected
    /// zones (BFS over NeighborIds) and emits one Russian-named label per zone above a density
    /// threshold, anchored on land. Adds 1-2 Russian-named sea labels at open-ocean anchor points.
    /// No Random.</summary>
    public static class RegionLabelPlacer
    {
        public const float DefaultLabelDensity = 0.4f;
        const int MaxZoneCells = 40; // density 0 -> only giants
        const int MinZoneCells = 6;  // density 1 -> include medium (matches the old minPatchCells floor)
        const int ContinentMinCells = 40;               // a landmass must be at least this big to be named
        const float ContinentPriorityBias = 1_000_000f; // continents/seas outrank biomes in overlap culling

        public static List<RegionLabelData> Place(IReadOnlyList<VoronoiCell> cells,
            NearestCellLookup nearest, float mapWidth, float mapHeight,
            int seed = 0, float labelDensity = DefaultLabelDensity)
        {
            var result = new List<RegionLabelData>();
            if (cells == null || cells.Count == 0) return result;

            int minZoneCells = Mathf_RoundLerp(MaxZoneCells, MinZoneCells, Clamp01(labelDensity));

            var byId = new Dictionary<int, VoronoiCell>();
            foreach (var c in cells) byId[c.Id] = c;

            // Discover connected same-family land components (unchanged BFS), keep those >= threshold.
            var components = new List<(int family, List<VoronoiCell> cellsInZone, int zoneKey)>();
            var visited = new HashSet<int>();
            foreach (var start in cells)
            {
                if (visited.Contains(start.Id)) continue;
                int fam = RegionCategories.FamilyCategoryOf(start);
                if (fam < 0) { visited.Add(start.Id); continue; } // water

                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                int zoneKey = start.Id;
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    if (c.Id < zoneKey) zoneKey = c.Id;      // min cell Id = stable zone key
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        if (RegionCategories.FamilyCategoryOf(nc) != fam) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }
                if (comp.Count >= minZoneCells) components.Add((fam, comp, zoneKey));
            }

            // Deterministic naming order: ascending zoneKey so the used-adjective sets evolve stably.
            components.Sort((x, y) => x.zoneKey.CompareTo(y.zoneKey));

            var usedByFamily = new Dictionary<BiomeFamily, HashSet<int>>();
            foreach (var (fam, comp, zoneKey) in components)
            {
                var family = (BiomeFamily)fam;
                // Defensive: a land cell manually painted to an Ocean/Lake biome resolves to family Sea/Lake
                // while still being "land" — skip it so a landmass never gets a "… Море" name.
                if (family == BiomeFamily.Sea || family == BiomeFamily.Lake) continue;
                if (!usedByFamily.TryGetValue(family, out var used))
                {
                    used = new HashSet<int>();
                    usedByFamily[family] = used;
                }
                string name = RegionLabelNames.NameFor(family, seed, zoneKey, used);
                if (name == null) continue; // Coast has no noun in the table -> unnamed

                result.Add(new RegionLabelData
                {
                    Text = name,
                    WorldPosition = OnLandAnchor(comp),
                    SeedFamily = family,
                    Kind = RegionLabelData.LabelKind.Biome,
                    Priority = comp.Count,
                });
            }

            AddContinentLabels(result, cells, byId, seed);
            AddSeaLabels(result, nearest, mapWidth, mapHeight, seed);
            return result;
        }

        // Continents = connected LAND (biome-agnostic, unlike the biome-family BFS above). One invented
        // name per landmass >= ContinentMinCells, shown only at the far (zoomed-out) LOD tier.
        static void AddContinentLabels(List<RegionLabelData> result, IReadOnlyList<VoronoiCell> cells,
            Dictionary<int, VoronoiCell> byId, int seed)
        {
            var visited = new HashSet<int>();
            foreach (var start in cells)
            {
                if (visited.Contains(start.Id)) continue;
                if (!RegionCategories.IsLandCell(start)) { visited.Add(start.Id); continue; }

                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                int landKey = start.Id;
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    if (c.Id < landKey) landKey = c.Id;
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        if (!RegionCategories.IsLandCell(nc)) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }
                if (comp.Count < ContinentMinCells) continue;
                result.Add(new RegionLabelData
                {
                    Text = RegionLabelNames.ContinentName(seed, landKey),
                    WorldPosition = OnLandAnchor(comp),
                    SeedFamily = BiomeFamily.Coast,   // "not a biome zone" sentinel; unused for continents
                    Kind = RegionLabelData.LabelKind.Continent,
                    Priority = ContinentPriorityBias + comp.Count,
                });
            }
        }

        /// <summary>Якорь подписи на суше: взвешенный по площади центроид куска, притянутый к
        /// ближайшей РЕАЛЬНОЙ клетке куска (значит всегда на суше). Public — переиспользуется
        /// PoliticalRegionAnchors для подписей политических регионов (та же логика).</summary>
        public static System.Numerics.Vector2 OnLandAnchor(List<VoronoiCell> comp)
        {
            var centroid = AreaWeightedCentroid(comp); // keep the existing method
            var best = comp[0];
            double bestD = double.MaxValue;
            foreach (var c in comp)
            {
                double dx = c.Site.X - centroid.X, dy = c.Site.Y - centroid.Y;
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best.Site; // a cell Site in the component -> always land
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
        static void AddSeaLabels(List<RegionLabelData> result, NearestCellLookup nearest, float mapW, float mapH, int seed)
        {
            if (nearest == null) return;
            (float nx, float ny)[] cands = { (0.135f, 0.43f), (0.835f, 0.90f) };
            var usedSea = new HashSet<int>();
            for (int i = 0; i < cands.Length; i++)
            {
                var pos = new System.Numerics.Vector2(cands[i].nx * mapW, cands[i].ny * mapH);
                var cell = nearest.FindNearest(pos);
                if (cell != null && cell.EffectiveIsOcean)
                {
                    string name = RegionLabelNames.NameFor(BiomeFamily.Sea, seed, 1000 + i, usedSea);
                    result.Add(new RegionLabelData
                    {
                        Text = name, WorldPosition = pos, SeedFamily = BiomeFamily.Sea,
                        Kind = RegionLabelData.LabelKind.Sea, Priority = ContinentPriorityBias,
                    });
                }
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static int Mathf_RoundLerp(int a, int b, float t) => (int)System.Math.Round(a + (b - a) * t);
    }
}
