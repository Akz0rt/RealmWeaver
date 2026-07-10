using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>
    /// Thin MonoBehaviour hosting [ContextMenu] self-tests for RegionLabelPlacer, matching this
    /// project's convention of self-tests living on a component (see ProjectSerializerSelfTests).
    /// RegionLabelPlacer itself is a static pure-C# class with no natural scene home.
    /// </summary>
    public class RegionLabelSelfTests : MonoBehaviour
    {
        /// <summary>Small square polygon around a site (mirrors WorldMapRenderer.SquarePolygon) - needed
        /// so PolygonArea area-weighting in RegionLabelPlacer has a non-degenerate (>=3 corner) polygon
        /// to work with for every fixture cell.</summary>
        static List<System.Numerics.Vector2> SquarePolygon(System.Numerics.Vector2 site, float half = 1f) => new List<System.Numerics.Vector2>
        {
            new System.Numerics.Vector2(site.X - half, site.Y - half),
            new System.Numerics.Vector2(site.X + half, site.Y - half),
            new System.Numerics.Vector2(site.X + half, site.Y + half),
            new System.Numerics.Vector2(site.X - half, site.Y + half),
        };

        [ContextMenu("Self-Test: Region Label Placer")]
        public void SelfTestPlacer()
        {
            // Build fixture: patch A (Forest) cells 0-6 chained, patch B (Plains) cells 7-13 chained, all land,
            // plus a lone under-threshold Snow cell (id 14) that must NOT produce a label.
            // (Biome.TemperateRainForest -> BiomeFamily.Forest, Biome.Grassland -> BiomeFamily.Plains,
            //  Biome.Snow -> BiomeFamily.Snow, per MapPalette.GetFamily.)
            var cells = new List<VoronoiCell>();

            // ---- Patch A: Forest, 7 cells (ids 0-6), chained in a line via NeighborIds so BFS reaches all
            // of them. Sites clustered near x=10..28, y=10 - far from patch B and the lone cell, so the
            // patches never share a neighbor.
            for (int i = 0; i < 7; i++)
            {
                var site = new System.Numerics.Vector2(10f + i * 3f, 10f);
                var cell = new VoronoiCell(i, site)
                {
                    Polygon = SquarePolygon(site),
                    Biome = Biome.TemperateRainForest,
                    IsOcean = false,
                };
                if (i > 0) cell.NeighborIds.Add(i - 1);
                if (i < 6) cell.NeighborIds.Add(i + 1);
                cells.Add(cell);
            }

            // ---- Patch B: Plains, 7 cells (ids 7-13), chained the same way. Sites clustered near
            // x=70..88, y=80 - well separated from patch A.
            for (int i = 0; i < 7; i++)
            {
                int id = 7 + i;
                var site = new System.Numerics.Vector2(70f + i * 3f, 80f);
                var cell = new VoronoiCell(id, site)
                {
                    Polygon = SquarePolygon(site),
                    Biome = Biome.Grassland,
                    IsOcean = false,
                };
                if (i > 0) cell.NeighborIds.Add(id - 1);
                if (i < 6) cell.NeighborIds.Add(id + 1);
                cells.Add(cell);
            }

            // ---- Lone under-threshold cell: Snow, id 14, no neighbors, isolated at (50,50) - patch size
            // 1 < minPatchCells 6, so it must not produce a label even though Snow has a Latin name entry.
            var loneSite = new System.Numerics.Vector2(50f, 50f);
            var loneCell = new VoronoiCell(14, loneSite)
            {
                Polygon = SquarePolygon(loneSite),
                Biome = Biome.Snow,
                IsOcean = false,
            };
            cells.Add(loneCell);

            var labels = RegionLabelPlacer.Place(cells, /*nearest*/ null, 100f, 100f, minPatchCells: 6);

            bool ok = labels.Count == 2;                                   // two patches labeled; sea skipped (nearest == null)
            ok &= labels.Exists(l => l.Text == "SILVA UMBRARUM");
            ok &= labels.Exists(l => l.Text == "CAMPI CANI");
            // centroid of patch A lies within its cells' bbox / the map bounds:
            var a = labels.Find(l => l.Text == "SILVA UMBRARUM");
            ok &= a != null && a.WorldPosition.X >= 0 && a.WorldPosition.X <= 100;
            // below-threshold patch (lone Snow cell) is dropped:
            ok &= !labels.Exists(l => l.SeedFamily == BiomeFamily.Snow);

            Debug.Log(ok ? "Self-Test Region Label Placer: PASS" : "Self-Test Region Label Placer: FAIL");
        }
    }
}
