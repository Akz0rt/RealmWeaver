using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Review finding I5: the original perf number was taken on a ONE-ROOM 16x16 contour, where
    /// FloorFootprint.ContainsRect degenerates to a single-rect arrangement. A real floor 0 has 6-10 rooms, so
    /// every ContainsRect call walks a real union. This measures the two calls the DM actually waits on:
    ///   • MaxRoomsPackable — 10 probe packs; runs on EVERY floor-tab click (DungeonEditorScreen.RefreshToolbar
    ///     -> UpperCap -> TryGetColumnAndCap, line 300) and again inside DoRegenerateUpperFloor (line 386);
    ///   • one «Перегенерировать» = TryGetColumnAndCap (another MaxRoomsPackable) + TryBuildUpperFloorExact with
    ///     the editor's RegenAttempts = 24 variety seeds, asked for the WORST-case count (== the cap).
    /// The MaxRoomsPackable probe loop is replicated here (BuildingGenerator's is hard-wired to CompactLayout) so
    /// the same 10 probes can be timed for the packer as reviewed (dd6e3dc), the spread-only pipeline, and the
    /// shipped one.</summary>
    public static class Perf
    {
        const int T = DungeonLayout.TilesPerAxis;
        const int RegenAttempts = 24;   // DungeonEditorScreen.RegenAttempts
        const int ProbeSeeds = 10;      // BuildingGenerator.ProbeSeeds

        delegate int PackFn(InteriorFloor floor, int columnId, float cx, float cy, InteriorFloor contour, float margin);

        // BuildingGenerator.MaxRoomsPackable, with the packer swapped out.
        static int CapWith(PackFn pack, float cx, float cy, int colW, int colH, InteriorFloor contour)
        {
            int budget = BuildingGenerator.MaxRoomsByArea(contour, colW, colH);
            int best = 1;
            for (int s = 0; s < ProbeSeeds; s++)
            {
                var f = Sweep.StairGraph(new Random(s), budget, colW, colH);
                int n = pack(f, f.Rooms[0].Id, cx, cy, contour, FloorFootprint.ContourMargin);
                if (n > best) best = n;
            }
            return best;
        }

        // BuildingGenerator.TryBuildUpperFloorExact + TrimToRoomCount, with the packer swapped out — the same
        // replication trick CapWith uses, so the «Перегенерировать» path can be timed for a variant packer too
        // (BuildingGenerator's own copy is hard-wired to CompactLayout). Structure kept line-for-line: `variety`
        // varietySeed-derived attempts, then the fixed probe seeds, each attempt packing a fresh stair graph and
        // succeeding when it placed at least targetCount rooms.
        static void RegenWith(PackFn pack, int targetCount, int varietySeed, int variety,
            float cx, float cy, int colW, int colH, InteriorFloor contour)
        {
            int budget = Math.Max(targetCount, BuildingGenerator.MaxRoomsByArea(contour, colW, colH));
            for (int i = 0; i < variety; i++)
                if (TryPack(pack, unchecked(varietySeed + i), targetCount, budget, cx, cy, colW, colH, contour)) return;
            for (int s = 0; s < ProbeSeeds; s++)
                if (TryPack(pack, s, targetCount, budget, cx, cy, colW, colH, contour)) return;
        }

        static bool TryPack(PackFn pack, int seed, int targetCount, int budget,
            float cx, float cy, int colW, int colH, InteriorFloor contour)
        {
            var f = Sweep.StairGraph(new Random(seed), budget, colW, colH);
            int columnId = f.Rooms[0].Id;
            pack(f, columnId, cx, cy, contour, FloorFootprint.ContourMargin);
            if (f.Rooms.Count < targetCount) return false;
            BuildingGenerator.TrimToRoomCount(f, targetCount, columnId);
            return true;
        }

        static double TimeMs(Action a, int reps)
        {
            a();   // warm up / JIT
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < reps; i++) a();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / reps;
        }

        public static void Run()
        {
            Console.WriteLine("ms per call, average of 50 (cap) / 10 (regen) reps after warm-up.");
            Console.WriteLine("F4 note: `pre-F4` is CompactLayout at e409a9c (the build the DM tested) and `cap` columns are");
            Console.WriteLine("that packer's own «из N» vs the shipped one's — a regen is timed for the cap ITS packer reports,");
            Console.WriteLine("which is the worst case each build actually asks for.");
            Console.WriteLine("| contour                              | rooms | budget | cap pre-F4 | cap now | MaxRoomsPackable: dd6e3dc | spread-only | pre-F4 | SHIPPED | one regen: pre-F4 | SHIPPED | SHIPPED @ pre-F4 cap |");

            var big = new InteriorFloor();
            big.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 16, SizeH = 16, X = 0.5f, Y = 0.5f });
            Report("1-room 16x16 (the OLD measurement)", big, 0.5f * T, 0.5f * T, 4, 4);

            for (int groundRooms = 6; groundRooms <= 10; groundRooms += 2)
                for (int seed = 1; seed <= 3; seed++)
                {
                    if (!Sweep.TryGroundContour(seed, groundRooms, out var ground, out var col)) continue;
                    Report($"real floor 0: seed {seed}, {groundRooms} asked", ground, col.X * T, col.Y * T, col.SizeW, col.SizeH);
                }
        }

        static void Report(string tag, InteriorFloor ground, float cx, float cy, int colW, int colH)
        {
            int budget = BuildingGenerator.MaxRoomsByArea(ground, colW, colH);
            int cap = BuildingGenerator.MaxRoomsPackable(cx, cy, colW, colH, ground);
            int capPreSlide = CapWith(PreSlideLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground);

            double preMs = TimeMs(() => CapWith(PreReviewLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground), 50);
            double spreadMs = TimeMs(() => CapWith(SpreadOnlyLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground), 50);
            double preSlideMs = TimeMs(() => CapWith(PreSlideLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground), 50);
            double nowMs = TimeMs(() => BuildingGenerator.MaxRoomsPackable(cx, cy, colW, colH, ground), 50);
            int i = 0, j = 0;
            double regenPreSlideMs = TimeMs(() =>
            {
                CapWith(PreSlideLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground);
                RegenWith(PreSlideLayout.PackAroundColumnWithinFootprint, capPreSlide, 1000 + (j++), RegenAttempts, cx, cy, colW, colH, ground);
            }, 10);
            double regenMs = TimeMs(() =>
            {
                BuildingGenerator.MaxRoomsPackable(cx, cy, colW, colH, ground);   // TryGetColumnAndCap in DoRegenerateUpperFloor
                BuildingGenerator.TryBuildUpperFloorExact(cap, 1000 + (i++), RegenAttempts, cx, cy, colW, colH, ground, out _, out _);
            }, 10);
            // Same regen, but asking for the count the PRE-F4 build reported. Where the cap rose, this separates
            // "each pack costs more" from "the DM is now offered — and the worst case therefore builds — a bigger
            // floor, so more of the 24 variety attempts fall through": only the first is F4's per-call cost.
            int k = 0;
            double regenSameCapMs = TimeMs(() =>
            {
                BuildingGenerator.MaxRoomsPackable(cx, cy, colW, colH, ground);
                BuildingGenerator.TryBuildUpperFloorExact(capPreSlide, 1000 + (k++), RegenAttempts, cx, cy, colW, colH, ground, out _, out _);
            }, 10);

            Console.WriteLine($"| {tag,-36} | {ground.Rooms.Count,5} | {budget,6} | {capPreSlide,10} | {cap,7} | {preMs,24:F2}"
                + $" | {spreadMs,11:F2} | {preSlideMs,6:F2} | {nowMs,7:F2} | {regenPreSlideMs,17:F2} | {regenMs,7:F2} | {regenSameCapMs,21:F2} |");
        }
    }
}
