using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>F4 optimization check. The fill sweeps gained two skips — (1) a room is not re-tried against an
    /// anchor it has already failed against, and (2) phase 3 does not re-walk the flush (d == 0) search phase 2
    /// already took to a fixpoint. Both are argued EXACT (an anchor never moves, the contour never changes and
    /// IsFree only tightens, so a failed candidate stays failed), which is a claim about RESULTS, not just about
    /// speed. This runs the same single pipeline WITH the skips and WITHOUT them over the pack corpus and compares
    /// the kept room set AND every room's exact X/Y.
    ///
    /// THREE pairs, because the first two do not exercise the case the skips exist for:
    ///   • spread / compact — shipped single pipelines vs. the SAME single pipelines cut out of e409a9c. Both
    ///     sides are SLIDE-FREE, so `maxSlide` is 0 over the flush-filtered lists and cut 2 never gets to skip a
    ///     SLID d == 0 pass. These check the skips against the real pre-F4 code.
    ///   • compact+slide — CompactOnlyLayout (compact+slide WITH both cuts) vs. CompactSlideNoCuts (the same
    ///     pipeline with sync.ps1's two regex kills applied: `minSeq` forced to 0 and `flushDoneSeq` forced to 0).
    ///     This is the ONLY pair that runs the slide, i.e. the only one in which the cuts skip slid work at all.</summary>
    public static class OptCheck
    {
        const int T = DungeonLayout.TilesPerAxis;

        delegate int PackFn(InteriorFloor floor, int columnId, float cx, float cy, InteriorFloor contour, float margin);

        public static void Run()
        {
            float m = FloorFootprint.ContourMargin;
            int cases = 0, mismatches = 0, roomsCompared = 0;
            var pairs = new (string name, PackFn now, PackFn before)[]
            {
                ("spread pipeline (no slide)  ", SpreadOnlyLayout.PackAroundColumnWithinFootprint, PreSlideSpreadOnly.PackAroundColumnWithinFootprint),
                ("compact pipeline (no slide) ", CompactNoSlideLayout.PackAroundColumnWithinFootprint, PreSlideCompactOnly.PackAroundColumnWithinFootprint),
                ("compact+slide (THE SLIDE ON)", CompactOnlyLayout.PackAroundColumnWithinFootprint, CompactSlideNoCuts.PackAroundColumnWithinFootprint),
            };
            var perPair = new int[pairs.Length];

            for (int contourSeed = 1; contourSeed <= 60; contourSeed++)
                for (int groundRooms = 3; groundRooms <= 10; groundRooms++)
                {
                    if (!Sweep.TryGroundContour(contourSeed, groundRooms, out var ground, out var col)) continue;
                    float cx = col.X * T, cy = col.Y * T;
                    int budget = BuildingGenerator.MaxRoomsByArea(ground, col.SizeW, col.SizeH);
                    for (int packSeed = 0; packSeed < 6; packSeed++)
                    {
                        cases++;
                        for (int p = 0; p < pairs.Length; p++)
                        {
                            var fNow = Sweep.StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                            var fOld = Sweep.StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                            pairs[p].now(fNow, fNow.Rooms[0].Id, cx, cy, ground, m);
                            pairs[p].before(fOld, fOld.Rooms[0].Id, cx, cy, ground, m);
                            bool bad = fNow.Rooms.Count != fOld.Rooms.Count;
                            foreach (var r in fOld.Rooms)
                            {
                                var q = fNow.GetRoom(r.Id);
                                roomsCompared++;
                                if (q == null || q.X != r.X || q.Y != r.Y) bad = true;
                            }
                            if (bad)
                            {
                                mismatches++; perPair[p]++;
                                if (perPair[p] <= 3)
                                    Console.WriteLine($"  MISMATCH {pairs[p].name} contour {contourSeed}/{groundRooms} packSeed {packSeed}: "
                                        + $"kept {fNow.Rooms.Count} vs {fOld.Rooms.Count}");
                            }
                        }
                    }
                }
            Console.WriteLine($"F4 skip-optimization check: {cases} packs x {pairs.Length} pipelines, {roomsCompared} room positions compared");
            for (int p = 0; p < pairs.Length; p++)
                Console.WriteLine($"  {pairs[p].name}: {perPair[p]} mismatches");
            Console.WriteLine(mismatches == 0
                ? "  -> IDENTICAL: the two skips changed no placement anywhere in the corpus"
                : $"  -> {mismatches} MISMATCHES — a skip is NOT result-preserving");
        }
    }
}
