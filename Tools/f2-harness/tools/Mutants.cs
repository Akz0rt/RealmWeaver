using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>NON-VACUITY harness: run the REAL self-test suites (Column Packing, and Battle Grid) against
    /// copies of themselves that are bound to a MUTANT (one rule removed each) or to an older/partial
    /// pipeline. Every assertion in a suite claims to pin some rule down; a mutant that removes that rule
    /// must make the suite FAIL. A row reading "0 errors" for a mutant means the suite does not actually
    /// test what that mutant broke.</summary>
    public static class Mutants
    {
        public static void Run()
        {
            var cases = new List<(string name, string what, Action run)>
            {
                ("MutAnchorOuter",    "SeatAgainstAnyPlaced loops swapped to anchor-outer/distance-inner",
                    () => new WorldGen.MutantTests.MutAnchorOuterSelfTests().SelfTestColumnPacking()),
                ("MutNoLinkPref",     "already-linked anchors no longer tried first",
                    () => new WorldGen.MutantTests.MutNoLinkPrefSelfTests().SelfTestColumnPacking()),
                ("MutTightBounds",    "margin term dropped from the bbox pre-test",
                    () => new WorldGen.MutantTests.MutTightBoundsSelfTests().SelfTestColumnPacking()),
                ("MutTightCut",       "MaxUsefulDistance's cut-off tightened by one tile (best -= 1f)",
                    () => new WorldGen.MutantTests.MutTightCutSelfTests().SelfTestColumnPacking()),
                ("MutOneSideCut",     "MaxUsefulDistance's four-side max collapsed to a single side (up only)",
                    () => new WorldGen.MutantTests.MutOneSideCutSelfTests().SelfTestColumnPacking()),
                ("MutNoDedup",        "AddLinkIfAbsent's duplicate check removed",
                    () => new WorldGen.MutantTests.MutNoDedupSelfTests().SelfTestColumnPacking()),
                ("MutFwdDedupOnly",   "AddLinkIfAbsent keeps only the FORWARD half of the pair test",
                    () => new WorldGen.MutantTests.MutFwdDedupOnlySelfTests().SelfTestColumnPacking()),
                ("MutNoSlide",        "F4's lateral slide removed (candidate offsets collapse to {0})",
                    () => new WorldGen.MutantTests.MutNoSlideSelfTests().SelfTestColumnPacking()),
                ("MutNoDoorBound",    "the slide's DoorGapTiles shared-wall bound removed (slides to a corner kiss)",
                    () => new WorldGen.MutantTests.MutNoDoorBoundSelfTests().SelfTestColumnPacking()),
                ("MutSlideFarFirst",  "the offset ladder runs largest-magnitude-first (centre no longer wins)",
                    () => new WorldGen.MutantTests.MutSlideFarFirstSelfTests().SelfTestColumnPacking()),
                ("SpreadOnlyLayout",  "the COMPACT run deleted (spread pipeline only)",
                    () => new WorldGen.MutantTests.SpreadOnlyLayoutSelfTests().SelfTestColumnPacking()),
                ("CompactOnlyLayout", "the SPREAD run deleted (compact pipeline only)",
                    () => new WorldGen.MutantTests.CompactOnlyLayoutSelfTests().SelfTestColumnPacking()),
                ("PreReviewLayout",   "the packer exactly as reviewed at dd6e3dc",
                    () => new WorldGen.MutantTests.PreReviewLayoutSelfTests().SelfTestColumnPacking()),
                ("PreSlideLayout",    "the packer exactly as SHIPPED before F4 (e409a9c) — no lateral slide",
                    () => new WorldGen.MutantTests.PreSlideLayoutSelfTests().SelfTestColumnPacking()),

                ("MutNoRing",         "BattleGridGenerator.Generate's wall-ring condition forced to false (no ring at all)",
                    () => new WorldGen.MutantTests.MutNoRingSelfTests().SelfTestGenerator()),
                ("MutNoYFlip",        "BattleGridGenerator.AlongVertical's top-relative numerator flipped to bottom-relative",
                    () => new WorldGen.MutantTests.MutNoYFlipSelfTests().SelfTestDoors()),
                ("MutFirstTouch",     "BattleGridStroke.Paint's first-touch guard replaced with an unconditional record",
                    () => new WorldGen.MutantTests.MutFirstTouchSelfTests().SelfTestOps()),
                ("MutFillDiagonal",   "BattleGridOps.Fill gains the four diagonal Enqueue calls (leaks through a diagonal pinch)",
                    () => new WorldGen.MutantTests.MutFillDiagonalSelfTests().SelfTestOps()),

                ("MutNoInsideFilter",  "SettlementGenerator.PlaceBuildings drops the wall.Contains(cx, cy) term from the keep condition",
                    () => new WorldGen.MutantTests.MutNoInsideFilterSelfTests().SelfTestBuildings()),
                ("MutNoWallClearance", "SettlementGenerator.PlaceBuildings drops the wall.DistanceToEdge(...) >= half term",
                    () => new WorldGen.MutantTests.MutNoWallClearanceSelfTests().SelfTestBuildings()),
                ("MutGateAtCentre",    "SettlementGenerator.PointAtArcLength returns the wall centre (0.5,0.5) instead of the interpolated point",
                    () => new WorldGen.MutantTests.MutGateAtCentreSelfTests().SelfTestGates()),
                ("MutStreetsNoGrowth", "SettlementStreets.GenerateStreets' Prim-style growth loop skipped (trunks only)",
                    () => new WorldGen.MutantTests.MutStreetsNoGrowthSelfTests().SelfTestStreets()),
            };

            Console.WriteLine("Baseline: the shipped packer against the shipped suite");
            UnityEngine.Debug.Errors = 0;
            var quiet = Console.Out;
            new WorldGen.Rendering.CompactLayoutSelfTests().SelfTestColumnPacking();
            Console.WriteLine($"  -> {UnityEngine.Debug.Errors} errors (must be 0)");
            Console.WriteLine();

            foreach (var (name, what, run) in cases)
            {
                Console.WriteLine($"--- {name}: {what}");
                UnityEngine.Debug.Errors = 0;
                run();
                Console.WriteLine($"  -> {UnityEngine.Debug.Errors} errors" + (UnityEngine.Debug.Errors == 0
                    ? "   *** NOT DETECTED — an assertion is vacuous ***" : ""));
                Console.WriteLine();
            }
        }
    }
}
