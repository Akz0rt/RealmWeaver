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

        /// <summary>DungeonEditorScreen's probed-cap memo, replicated literally (that file is Unity-side and not
        /// compiled here, the same reason CapWith/RegenWith replicate BuildingGenerator). Key = the column pin
        /// plus every floor-0 room's Id/TypeId/X/Y/SizeW/SizeH, compared element by element — so what is timed
        /// below as a "memo hit" is the REAL cost of the fast path, not an assumed zero.</summary>
        sealed class CapMemo
        {
            InteriorFloor contour;
            float[] key;
            int cap;

            static float[] KeyOf(float colX, float colY, int colW, int colH, InteriorFloor c)
            {
                var k = new float[4 + c.Rooms.Count * 6];
                k[0] = colX; k[1] = colY; k[2] = colW; k[3] = colH;
                int i = 4;
                foreach (var r in c.Rooms)
                {
                    k[i++] = r.Id; k[i++] = r.TypeId;
                    k[i++] = r.X; k[i++] = r.Y;
                    k[i++] = r.SizeW; k[i++] = r.SizeH;
                }
                return k;
            }

            static bool Same(float[] a, float[] b)
            {
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                return true;
            }

            public int Cap(float colX, float colY, int colW, int colH, InteriorFloor c)
            {
                var k = KeyOf(colX, colY, colW, colH, c);
                if (ReferenceEquals(contour, c) && Same(key, k)) return cap;
                cap = BuildingGenerator.MaxRoomsPackable(colX, colY, colW, colH, c);
                contour = c; key = k;
                return cap;
            }
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
            Console.WriteLine("The last two columns are the review-fix (I2) numbers: the floor-tab click served by the probed-cap");
            Console.WriteLine("memo (unchanged floor 0 => no re-probe at all) and a regen that no longer re-probes the cap it was");
            Console.WriteLine("just handed. Both are what the DM actually feels after the first click on a building.");
            Console.WriteLine("M-3: `run3-off` is NoPlainRunLayout (shipped MINUS run 3, F4's slid runs alone) timed the SAME way as");
            Console.WriteLine("SHIPPED, so run 3's cost re-derives against the CURRENT pipeline instead of a two-run intermediate build");
            Console.WriteLine("that no longer exists in the tree.");
            Console.WriteLine("| contour                              | rooms | budget | cap pre-F4 | cap now | MaxRoomsPackable: dd6e3dc | spread-only | pre-F4 | run3-off | SHIPPED | run3 cost | one regen: pre-F4 | SHIPPED | SHIPPED @ pre-F4 cap | I2 click: memo hit | I2 regen: memo cap |");

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
            // M-3: the SAME pipeline the sweep's "(f) vs (e)" column measures rooms-kept for — timed here so
            // run 3's COST (not just the 34/1200 caps it saves) re-derives against the current tree.
            double noPlainMs = TimeMs(() => CapWith(NoPlainRunLayout.PackAroundColumnWithinFootprint, cx, cy, colW, colH, ground), 50);
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

            // I2 (a): the floor-tab click once the memo is warm — the whole TryGetColumnAndCap fast path (build
            // the key over floor 0's rooms, compare it element by element, return the stored cap).
            var memo = new CapMemo();
            memo.Cap(cx, cy, colW, colH, ground);   // cold: the one probe the DM pays on the first click
            double memoHitMs = TimeMs(() => memo.Cap(cx, cy, colW, colH, ground), 2000);
            // M-1: this is a timing smoke-check only (this Perf run never mutates `ground`, so `Same` returns
            // true on every call and this can only fail if MaxRoomsPackable is non-deterministic — already
            // asserted at BuildingGeneratorSelfTests.cs:278-280). The `capmemo` command is the actual
            // completeness/invalidation check. A real mismatch here would still be a correctness bug, so THROW
            // instead of printing a line that could scroll past between two 15-column table rows with no exit
            // code to flag it — and compute the value once instead of re-invoking Cap() a third time.
            int memoCapCheck = memo.Cap(cx, cy, colW, colH, ground);
            if (memoCapCheck != cap)
                throw new Exception($"BAD MEMO {tag}: memo cap {memoCapCheck} != probed cap {cap}");
            // I2 (b): «Перегенерировать» at the cap without the second probe (DoRegenerateUpperFloor's own
            // TryGetColumnAndCap is now a memo hit, because nothing on floor 0 changed since the tab click).
            int n = 0;
            double regenMemoMs = TimeMs(() =>
            {
                memo.Cap(cx, cy, colW, colH, ground);
                BuildingGenerator.TryBuildUpperFloorExact(cap, 1000 + (n++), RegenAttempts, cx, cy, colW, colH, ground, out _, out _);
            }, 10);

            double run3CostPct = noPlainMs > 0 ? (nowMs - noPlainMs) / noPlainMs * 100.0 : 0.0;
            Console.WriteLine($"| {tag,-36} | {ground.Rooms.Count,5} | {budget,6} | {capPreSlide,10} | {cap,7} | {preMs,24:F2}"
                + $" | {spreadMs,11:F2} | {preSlideMs,6:F2} | {noPlainMs,8:F2} | {nowMs,7:F2} | {run3CostPct,8:F1}%"
                + $" | {regenPreSlideMs,17:F2} | {regenMs,7:F2} | {regenSameCapMs,21:F2}"
                + $" | {memoHitMs,18:F4} | {regenMemoMs,18:F2} |");
        }
    }
}
