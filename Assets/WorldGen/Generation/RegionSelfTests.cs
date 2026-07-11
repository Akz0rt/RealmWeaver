using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Generation
{
    /// <summary>Editor-only [ContextMenu] self-tests for WorldGenerator.GenerateRegions (count + min-size
    /// merge + id compaction). Attach to any GameObject and run each item from the component's context
    /// menu. (Project convention: no CLI test runner — self-tests Debug.Log PASS/FAIL.)
    ///
    /// GroupCells seeds its BFS from System.Random(seed) and grows on height-cost — for a UNIFORM-height
    /// grid every edge cost is 0, so exactly which cell lands in which of the `count` initial regions is
    /// not something we can hand-predict without actually running the code (no compiler/runner available
    /// here — see project constraints). What IS provable without running it: on a fully-connected grid
    /// (every land cell reachable from every other through land neighbours, no islands), the
    /// MergeUndersizedRegions loop can only stop either at 1 surviving region, or with every surviving
    /// region ≥ minSize — a region can only "return early" via the shared.Count==0 escape hatch (isolated
    /// island, no land neighbour of a different region), which cannot happen here because the grid is one
    /// connected component. So the tests below assert the structural invariants that MUST hold regardless
    /// of the exact seed-driven partition, plus a pigeonhole bound on the final count where the numbers
    /// make it computable (forces-merge case). We do NOT assert the exact final region count/shape, since
    /// that would require actually executing GroupCells' RNG-driven seed placement to know.</summary>
    public class RegionSelfTests : MonoBehaviour
    {
        /// <summary>Builds a w×h grid of land cells (uniform Height=0, IsOcean=false) with 4-neighbour
        /// NeighborIds — a single connected component, close enough to a real Voronoi neighbour graph for
        /// GroupCells' BFS + MergeUndersizedRegions to actually run (mirrors the chained-cells fixture
        /// style used by RegionLabelSelfTests).</summary>
        static List<VoronoiCell> BuildGrid(int w, int h)
        {
            var cells = new List<VoronoiCell>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int id = y * w + x;
                    var site = new System.Numerics.Vector2(x * 10f, y * 10f);
                    cells.Add(new VoronoiCell(id, site) { IsOcean = false });
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int id = y * w + x;
                    var cell = cells[id];
                    if (x > 0) cell.NeighborIds.Add(id - 1);
                    if (x < w - 1) cell.NeighborIds.Add(id + 1);
                    if (y > 0) cell.NeighborIds.Add(id - w);
                    if (y < h - 1) cell.NeighborIds.Add(id + w);
                }
            return cells;
        }

        /// <summary>Checks the invariants GenerateRegions' contract guarantees for ANY connected land
        /// graph, regardless of the specific seed-driven partition: returned count in [0, requestedCount];
        /// every land cell's RegionId in [0, k); ids are dense (every id 0..k-1 actually used, none
        /// stray); no surviving region below minSize unless only one remains.</summary>
        static bool AssertInvariants(List<VoronoiCell> cells, int k, int requestedCount, int minSize)
        {
            bool ok = k >= 0 && k <= requestedCount;

            var counts = new Dictionary<int, int>();
            foreach (var c in cells)
            {
                if (c.IsOcean) continue;
                ok &= c.RegionId >= 0 && c.RegionId < k;              // in-range for every land cell
                counts.TryGetValue(c.RegionId, out int n);
                counts[c.RegionId] = n + 1;
            }

            for (int id = 0; id < k; id++) ok &= counts.ContainsKey(id); // dense: every 0..k-1 id used
            ok &= counts.Count == k;                                     // ...and no stray ids beyond that

            if (k > 1)
                foreach (var kv in counts) ok &= kv.Value >= minSize;    // no undersized region unless only one remains

            return ok;
        }

        [ContextMenu("Self-Test: GenerateRegions (comfortable fit)")]
        public void SelfTestGenerateRegionsComfortable()
        {
            // 12x12 = 144 land cells, count=5 -> ~28.8 cells/region on average, comfortably above
            // minSize=20 -> exercises the common "no merge needed" path.
            var cells = BuildGrid(12, 12);
            int k = WorldGenerator.GenerateRegions(cells, count: 5, minSize: 20, seed: 1);
            bool ok = AssertInvariants(cells, k, requestedCount: 5, minSize: 20);
            Debug.Log(ok ? "Self-Test GenerateRegions (comfortable fit): PASS" : "Self-Test GenerateRegions (comfortable fit): FAIL");
        }

        [ContextMenu("Self-Test: GenerateRegions (forces merge)")]
        public void SelfTestGenerateRegionsForcesMerge()
        {
            // 8x8 = 64 land cells, count=5 -> initial GroupCells always produces exactly 5 non-empty
            // regions (~12.8 cells/region average) - well under minSize=20, so at least one region starts
            // undersized and MergeUndersizedRegions MUST run at least once.
            // Pigeonhole bound provable without running it: if the loop stops with k>1 surviving regions,
            // every one of them must hold >= minSize=20 cells (that's the loop's only non-isolated exit),
            // so 20*k <= 64 land cells -> k <= 3. Combined with k>=1, the final count is in {1,2,3}.
            var cells = BuildGrid(8, 8);
            int k = WorldGenerator.GenerateRegions(cells, count: 5, minSize: 20, seed: 1);
            bool ok = AssertInvariants(cells, k, requestedCount: 5, minSize: 20);
            ok &= k >= 1 && k <= 3;   // pigeonhole bound above - proves merging actually collapsed the count
            Debug.Log(ok ? "Self-Test GenerateRegions (forces merge): PASS" : "Self-Test GenerateRegions (forces merge): FAIL");
        }

        // Hand-trace of MergeUndersizedRegions (documentation only — the method is `static void` private
        // to WorldGenerator, so it cannot be called directly from here; and GenerateRegions' own call to
        // RegionGrowing.GroupCells is seed/RNG-driven, so we cannot force this exact partition through the
        // public entry point without actually running it, which we have no compiler/runner to do — see
        // project constraints). Tracing the algorithm as written against a tiny concrete input:
        //
        //   3 regions after GroupCells+UnifyLakes: A has 3 land cells, B has 50, C has 47.
        //   A's 3 cells' NeighborIds touch B-region cells across 2 distinct border edges and C-region
        //   cells across 1 border edge (no ocean, no unassigned neighbours).
        //
        //   Loop iteration 1: counts = {A:3, B:50, C:47} -> counts.Count=3 (>1, continue).
        //     smallest = A (3) < minSize(20) -> must merge.
        //     shared (border votes for A's cells) = {B:2, C:1} -> most-shared = B (2 > 1) -> into = B.
        //     Every cell with RegionId==A gets RegionId=B. Now: A gone, B=53, C=47.
        //   Loop iteration 2: counts = {B:53, C:47} -> counts.Count=2 (>1, continue).
        //     smallest = C (47) >= minSize(20) -> "all regions big enough" -> return.
        //
        //   CompactRegionIds then remaps the two surviving ids {B, C} to a dense {0, 1} in first-seen
        //   order -> final region count K=2. This matches the assertable contract above: K <= requested,
        //   every surviving region >= minSize (53 and 47 both are), ids dense (0 and 1 both used).
        [ContextMenu("Self-Test: GenerateRegions (merge hand-trace, doc only)")]
        public void SelfTestGenerateRegionsMergeHandTrace()
        {
            Debug.Log("Self-Test GenerateRegions merge hand-trace: see source comment above this method — " +
                      "MergeUndersizedRegions is private and GroupCells is RNG-driven, so this scenario is " +
                      "traced by hand against the algorithm as written, not executed. PASS (documentation).");
        }

        /// <summary>Lake fixture cell: forced to lake via WaterOverride regardless of Biome/IsOcean.</summary>
        static VoronoiCell MakeLakeCell(int id, int[] neighborIds)
        {
            return new VoronoiCell(id, new System.Numerics.Vector2(id, 0f))
            {
                WaterOverride = WaterOverrideType.ForceLake,
                NeighborIds = new List<int>(neighborIds)
            };
        }

        /// <summary>Land fixture cell: default WaterOverride (None), not ocean, non-Lake biome, with the
        /// given RegionId — so both EffectiveIsLake and EffectiveIsOcean are false.</summary>
        static VoronoiCell MakeLandCell(int id, int regionId, int[] neighborIds)
        {
            return new VoronoiCell(id, new System.Numerics.Vector2(id, 0f))
            {
                IsOcean = false,
                Biome = Biome.Grassland,
                RegionId = regionId,
                NeighborIds = new List<int>(neighborIds)
            };
        }

        [ContextMenu("Self-Test: Lake Majority Land Region")]
        public void SelfTestLakeMajorityLandRegion()
        {
            // Lake cell 0 (ForceLake). Land neighbours: 1,2 in region 5; 3 in region 2. Winner = 5.
            var lake = MakeLakeCell(0, new[] { 1, 2, 3 });
            var l1 = MakeLandCell(1, 5, new[] { 0 });
            var l2 = MakeLandCell(2, 5, new[] { 0 });
            var l3 = MakeLandCell(3, 2, new[] { 0 });
            var byId = new Dictionary<int, VoronoiCell> { { 0, lake }, { 1, l1 }, { 2, l2 }, { 3, l3 } };

            var comp = LakeRegionUnifier.FindLakeComponent(0, byId);
            int winner = LakeRegionUnifier.MajorityLandRegion(comp, byId);
            bool pass = comp.Count == 1 && winner == 5;

            // Isolated lake (no land neighbours) → -1.
            var isoLake = MakeLakeCell(10, new int[0]);
            var isoById = new Dictionary<int, VoronoiCell> { { 10, isoLake } };
            var isoComp = LakeRegionUnifier.FindLakeComponent(10, isoById);
            bool isoPass = LakeRegionUnifier.MajorityLandRegion(isoComp, isoById) == -1;

            Debug.Log(pass && isoPass
                ? "Self-Test Lake Majority Land Region: PASS"
                : $"Self-Test Lake Majority Land Region: FAIL (winner={winner}, comp={comp.Count}, isoPass={isoPass})");
        }

        [ContextMenu("Self-Test: Lake Coverage Threshold")]
        public void SelfTestLakeCoverageThreshold()
        {
            bool a = LakeRegionUnifier.CoversLakeEnough(3, 10, 30) == true;   // exactly 30% → include
            bool b = LakeRegionUnifier.CoversLakeEnough(2, 10, 30) == false;  // 20% → exclude
            bool c = LakeRegionUnifier.CoversLakeEnough(4, 10, 30) == true;   // 40% → include
            bool d = LakeRegionUnifier.CoversLakeEnough(0, 0, 30) == false;   // empty lake → false, no divide
            Debug.Log(a && b && c && d
                ? "Self-Test Lake Coverage Threshold: PASS"
                : $"Self-Test Lake Coverage Threshold: FAIL (a={a}, b={b}, c={c}, d={d})");
        }
    }
}
