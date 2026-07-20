using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>F4 optimization check. The fill sweeps gained two skips — (1) a room is not re-tried against an
    /// anchor it has already failed against, and (2) phase 3 does not re-walk the flush (d == 0) search phase 2
    /// already took to a fixpoint. Both are argued EXACT (an anchor never moves, the contour never changes and
    /// IsFree only tightens, so a failed candidate stays failed), which is a claim about RESULTS, not just about
    /// speed. This runs the same single pipeline WITH the skips (cut out of the shipped source) and WITHOUT them
    /// (cut out of e409a9c) over the pack corpus and compares the kept room set AND every room's exact X/Y.
    /// Both pipelines are run slide-free, so the ONLY difference between them is the two skips.</summary>
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
                ("spread pipeline ", SpreadOnlyLayout.PackAroundColumnWithinFootprint, PreSlideSpreadOnly.PackAroundColumnWithinFootprint),
                ("compact pipeline", CompactNoSlideLayout.PackAroundColumnWithinFootprint, PreSlideCompactOnly.PackAroundColumnWithinFootprint),
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
