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
            // (Biome.Forest -> BiomeFamily.Forest, Biome.Grassland -> BiomeFamily.Plains,
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
                    Biome = Biome.Forest,
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
            // A lone 1-cell patch is below the density threshold, so it must not produce a label.
            var loneSite = new System.Numerics.Vector2(50f, 50f);
            var loneCell = new VoronoiCell(14, loneSite)
            {
                Polygon = SquarePolygon(loneSite),
                Biome = Biome.Snow,
                IsOcean = false,
            };
            cells.Add(loneCell);

            // Old call used minPatchCells: 6. New call passes seed + a high density so the ~7-cell patches qualify.
            var labels = RegionLabelPlacer.Place(cells, /*nearest*/ null, 100f, 100f, seed: 1, labelDensity: 1f);

            bool ok = labels.Count == 2;                                          // two big patches named, lone cell dropped
            ok &= labels.Exists(l => l.Text != null && l.Text.EndsWith(" Лес"));   // Forest zone -> "... Лес"
            ok &= labels.Exists(l => l.Text != null && l.Text.EndsWith(" Луга"));  // Plains zone -> "... Луга"
            // On-land anchor: each label sits at one of its component cells' Sites (bbox 0..100).
            var forest = labels.Find(l => l.Text.EndsWith(" Лес"));
            ok &= forest != null && forest.WorldPosition.X >= 0 && forest.WorldPosition.X <= 100;
            // Determinism: a second identical Place gives identical names.
            var labels2 = RegionLabelPlacer.Place(cells, null, 100f, 100f, seed: 1, labelDensity: 1f);
            ok &= labels2.Count == labels.Count
               && labels2.Find(l => l.Text.EndsWith(" Лес"))?.Text == forest.Text;
            // Density threshold drops small patches: at low density the ~7-cell patches fall below MaxZoneCells=40.
            var sparse = RegionLabelPlacer.Place(cells, null, 100f, 100f, seed: 1, labelDensity: 0f);
            ok &= sparse.Count == 0;

            // Kind/Priority tagging: biome labels are Kind=Biome with Priority>0.
            ok &= forest != null && forest.Kind == RegionLabelData.LabelKind.Biome && forest.Priority > 0f;
            // Fixture patches are ~7 cells each (< ContinentMinCells=40) → no continent label is emitted.
            ok &= labels.TrueForAll(l => l.Kind != RegionLabelData.LabelKind.Continent);

            Debug.Log(ok ? "Self-Test Region Label Placer: PASS" : "Self-Test Region Label Placer: FAIL");
        }

        [ContextMenu("Self-Test: Political Region Anchor On Region")]
        public void SelfTestPoliticalRegionAnchorOnRegion()
        {
            // Регион 0 РАЗОРВАН на два несвязанных куска: крупный A (клетки 0-1-2 у x=0..2) и мелкий
            // B (клетки 3-4 у x=10..11). Наивный центроид = среднее = x≈4.8 → в пустоте МЕЖДУ кусками,
            // ВНЕ региона (так подпись «Каэрморн» и улетала в океан). Правильный якорь обязан сесть на
            // клетку КРУПНОГО куска A и совпасть с реальной клеткой региона.
            var cells = new List<VoronoiCell>();
            void Add(int id, float x, params int[] nbrs)
            {
                var site = new System.Numerics.Vector2(x, 0f);
                var cell = new VoronoiCell(id, site)
                { Polygon = SquarePolygon(site, 0.5f), Biome = Biome.Grassland, IsOcean = false, RegionId = 0 };
                foreach (var n in nbrs) cell.NeighborIds.Add(n);
                cells.Add(cell);
            }
            Add(0, 0f, 1);      // кусок A: 0-1-2 связаны
            Add(1, 1f, 0, 2);
            Add(2, 2f, 1);
            Add(3, 10f, 4);     // кусок B: 3-4 связаны, но НЕ связаны с A
            Add(4, 11f, 3);

            var anchors = WorldGen.Rendering.RegionLabels.PoliticalRegionAnchors.Compute(cells);
            bool has = anchors.TryGetValue(0, out var anchor);

            // Якорь совпадает с Site реальной клетки региона 0 → гарантированно НА суше региона.
            bool onRegion = has && cells.Exists(c => c.Site == anchor);
            // ...и именно в КРУПНОМ куске A (x<=2), а не в мелком B (x>=10).
            bool inLargest = has && anchor.X <= 2f;
            // Документируем баг: наивный центроид (x≈4.8) НЕ совпадает ни с одной клеткой региона.
            var naive = new System.Numerics.Vector2((0f + 1f + 2f + 10f + 11f) / 5f, 0f); // (4.8, 0)
            bool naiveOffRegion = !cells.Exists(c => c.Site == naive);

            bool ok = onRegion && inLargest && naiveOffRegion;
            Debug.Log(ok
                ? $"Self-Test Political Region Anchor On Region: PASS (anchor=({anchor.X},{anchor.Y}))"
                : $"Self-Test Political Region Anchor On Region: FAIL (has={has}, onRegion={onRegion}, inLargest={inLargest}, naiveOffRegion={naiveOffRegion})");
        }

        [ContextMenu("Self-Test: Continent Names")]
        public void SelfTestContinentNames()
        {
            string a1 = RegionLabelNames.ContinentName(1, 5);
            string a2 = RegionLabelNames.ContinentName(1, 5);
            bool ok = !string.IsNullOrEmpty(a1) && a1 == a2;                 // deterministic
            ok &= !string.IsNullOrEmpty(RegionLabelNames.ContinentName(2, 5)); // other seed still names
            // Reroll (seed varies via salt) generally yields a different name for the same landmass:
            ok &= a1 != RegionLabelNames.ContinentName(9, 5)
               || a1 != RegionLabelNames.ContinentName(17, 5);              // at least one of two other seeds differs
            Debug.Log(ok ? "Self-Test Continent Names: PASS" : "Self-Test Continent Names: FAIL");
        }

        [ContextMenu("Self-Test: Region Label Names")]
        public void SelfTestNames()
        {
            // Determinism: same (family, seed, zoneKey) + fresh used-set -> identical name.
            string a1 = RegionLabelNames.NameFor(BiomeFamily.Forest, 1, 5, new System.Collections.Generic.HashSet<int>());
            string a2 = RegionLabelNames.NameFor(BiomeFamily.Forest, 1, 5, new System.Collections.Generic.HashSet<int>());
            bool ok = a1 != null && a1 == a2 && a1.EndsWith(" Лес");

            // Gender agreement: same seed+zoneKey+fresh set -> same adjective index, different gender forms.
            // Forest is Masculine, Badlands is Feminine, so the adjective token must differ in ending.
            string f = RegionLabelNames.NameFor(BiomeFamily.Forest,   7, 3, new System.Collections.Generic.HashSet<int>());
            string b = RegionLabelNames.NameFor(BiomeFamily.Badlands, 7, 3, new System.Collections.Generic.HashSet<int>());
            ok &= f.EndsWith(" Лес") && b.EndsWith(" Пустошь");
            string fAdj = f.Substring(0, f.Length - " Лес".Length);
            string bAdj = b.Substring(0, b.Length - " Пустошь".Length);
            ok &= fAdj != bAdj;                          // masculine vs feminine form differ

            // Uniqueness within a family: shared set -> two zones get different adjectives.
            var shared = new System.Collections.Generic.HashSet<int>();
            string z1 = RegionLabelNames.NameFor(BiomeFamily.Plains, 2, 10, shared);
            string z2 = RegionLabelNames.NameFor(BiomeFamily.Plains, 2, 11, shared);
            ok &= z1 != z2 && z1.EndsWith(" Луга") && z2.EndsWith(" Луга");

            // Unnamed families -> null.
            ok &= RegionLabelNames.NameFor(BiomeFamily.Coast, 1, 1, new System.Collections.Generic.HashSet<int>()) == null;
            ok &= RegionLabelNames.NameFor(BiomeFamily.Lake,  1, 1, new System.Collections.Generic.HashSet<int>()) == null;

            Debug.Log(ok ? "Self-Test Region Label Names: PASS" : "Self-Test Region Label Names: FAIL");
        }
    }
}
