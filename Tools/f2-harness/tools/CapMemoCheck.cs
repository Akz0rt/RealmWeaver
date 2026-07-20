using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>Review finding I-1 (third F4 pass): the only executable check on DungeonEditorScreen's probed-cap
    /// memo (mirrored by Perf.cs's private CapMemo) was a same-key round-trip at Perf.cs:171-174 — cold, it
    /// computes MaxRoomsPackable with the SAME arguments already used one line above and stores it, then compares
    /// the stored value to itself. Since `ground` is never mutated anywhere in Perf.cs, `Same` returns true on
    /// every later call and the "stale" branch never fires. That check can only fail if MaxRoomsPackable is
    /// non-deterministic (already asserted elsewhere, BuildingGeneratorSelfTests.cs:278-280). Key COMPLETENESS
    /// and INVALIDATION-ON-MUTATION — the two properties the DM's correctness actually rests on — had zero
    /// executable coverage.
    ///
    /// This command closes that gap. For a corpus of real floor-0 contours it walks a MUTATION LADDER against a
    /// LIVE memo: baseline probe, then eight named mutations applied cumulatively (each individually a DM-
    /// reachable edit to floor 0 or the column). After EVERY rung it compares three things computed against the
    /// SAME (mutated) contour:
    ///   • freshCap  — BuildingGenerator.MaxRoomsPackable called directly, no memo — the ground truth;
    ///   • memoCap   — the SHIPPED key (column pin + every room's Id/TypeId/X/Y/SizeW/SizeH), replicated exactly
    ///                 as DungeonEditorScreen.TryGetColumnAndCap/Perf.cs's CapMemo do (that file is Unity-side and
    ///                 not compiled here — same reason CapWith/RegenWith replicate BuildingGenerator);
    ///   • controlCap — a NEGATIVE CONTROL: byte-identical except TypeId is dropped from the key.
    /// memoCap must equal freshCap on EVERY rung, for every contour — any mismatch is a real stale-cap bug and is
    /// reported before anything else (see Run()'s early-exit). controlCap is expected to match everywhere EXCEPT
    /// the "flip a TypeId" rung, where dropping TypeId from the key must produce a STALE (mismatching) cap —
    /// otherwise this whole ladder would be exercising nothing a simpler check couldn't, i.e. exactly as vacuous
    /// as the one it replaces.
    ///
    /// Why "flip a TypeId" needs a SETUP rung first: DungeonProjection.EffectiveSize only reads TypeId as a
    /// FALLBACK, when SizeW/SizeH <= 0 (RoomSizing.ApplyDefaults' "migration / new rooms" case — a real,
    /// DM-reachable state, not a contrivance). Every corpus room starts with an explicit positive size, so a
    /// TypeId flip on an as-generated room changes nothing observable and would make BOTH memos agree trivially.
    /// Rung 3 zero-sizes one designated room first (itself checked like any other rung — the shipped memo must
    /// track a resize-to-zero same as any other resize); only then does rung 4's TypeId flip actually move the
    /// room's effective footprint (RoomSizing.Default(1) = 6x6 -> Default(2) = 10x10), which is what makes the
    /// negative control's blind spot observable instead of hypothetical.</summary>
    public static class CapMemoCheck
    {
        const int T = DungeonLayout.TilesPerAxis;

        /// <summary>The shipped key, replicated literally: column pin (X,Y,SizeW,SizeH) + every floor-0 room's
        /// Id/TypeId/X/Y/SizeW/SizeH, in list order. Compared element-by-element, exact (no epsilon) — same as
        /// DungeonEditorScreen.CapKey/SameCapKey and Perf.cs's CapMemo.</summary>
        sealed class CapMemo
        {
            InteriorFloor contour;
            float[] key;
            int cap;
            public int Recomputes;   // how many times this instance actually re-probed (reported, not asserted)

            static float[] KeyOf(Room column, InteriorFloor c)
            {
                var k = new float[4 + c.Rooms.Count * 6];
                k[0] = column.X; k[1] = column.Y; k[2] = column.SizeW; k[3] = column.SizeH;
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

            public int Cap(Room column, InteriorFloor c)
            {
                var k = KeyOf(column, c);
                if (ReferenceEquals(contour, c) && Same(key, k)) return cap;
                Recomputes++;
                cap = BuildingGenerator.MaxRoomsPackable(column.X * T, column.Y * T, column.SizeW, column.SizeH, c);
                contour = c; key = k;
                return cap;
            }
        }

        /// <summary>NEGATIVE CONTROL — byte-identical to <see cref="CapMemo"/> except TypeId is dropped from the
        /// per-room key. Exists only to prove the ladder CAN catch a real stale-cap bug; not shipped anywhere.</summary>
        sealed class CapMemoNoTypeId
        {
            InteriorFloor contour;
            float[] key;
            int cap;

            static float[] KeyOf(Room column, InteriorFloor c)
            {
                var k = new float[4 + c.Rooms.Count * 5];   // 5, not 6 — TypeId dropped
                k[0] = column.X; k[1] = column.Y; k[2] = column.SizeW; k[3] = column.SizeH;
                int i = 4;
                foreach (var r in c.Rooms)
                {
                    k[i++] = r.Id;
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

            public int Cap(Room column, InteriorFloor c)
            {
                var k = KeyOf(column, c);
                if (ReferenceEquals(contour, c) && Same(key, k)) return cap;
                cap = BuildingGenerator.MaxRoomsPackable(column.X * T, column.Y * T, column.SizeW, column.SizeH, c);
                contour = c; key = k;
                return cap;
            }
        }

        struct RungResult
        {
            public int Contours, Changed, MemoMismatches, ControlMismatches;
        }

        public static void Run()
        {
            string[] rungNames =
            {
                "0  cold probe (first floor-tab click)",
                "1  move a room by one tile",
                "2  resize a room",
                "3  zero a room's size (setup for rung 4)",
                "4  flip that room's TypeId (1 -> 2)",
                "5  delete a room",
                "6  add a room",
                "7  move the column",
                "8  resize the column",
            };
            int rungs = rungNames.Length;
            var results = new RungResult[rungs];
            var memoMismatchExamples = new List<string>();
            var controlMismatchExamples = new List<string>();
            int contoursTested = 0;

            for (int contourSeed = 1; contourSeed <= 25; contourSeed++)
                for (int groundRooms = 6; groundRooms <= 12; groundRooms += 2)
                {
                    if (!Sweep.TryGroundContour(contourSeed, groundRooms, out var ground, out var col)) continue;
                    // Need 4 distinct non-column, non-entrance rooms for the move/resize/flip/delete rungs.
                    var others = ground.Rooms.Where(r => r.Id != col.Id && r.TypeId != 0)
                                              .OrderBy(r => r.Id).ToList();
                    if (others.Count < 4) continue;
                    contoursTested++;

                    var moveRoom = others[0];
                    var resizeRoom = others[1];
                    var flipRoom = others[2];
                    var deleteRoom = others[3];

                    var memo = new CapMemo();
                    var control = new CapMemoNoTypeId();
                    int prevFresh = int.MinValue;
                    string tag = $"contour {contourSeed}/{groundRooms}";

                    void Check(int rung)
                    {
                        int fresh = BuildingGenerator.MaxRoomsPackable(col.X * T, col.Y * T, col.SizeW, col.SizeH, ground);
                        int memoCap = memo.Cap(col, ground);
                        int controlCap = control.Cap(col, ground);
                        ref var res = ref results[rung];
                        res.Contours++;
                        if (prevFresh != int.MinValue && fresh != prevFresh) res.Changed++;
                        prevFresh = fresh;
                        if (memoCap != fresh)
                        {
                            res.MemoMismatches++;
                            if (memoMismatchExamples.Count < 10)
                                memoMismatchExamples.Add($"  BAD MEMO {tag} rung {rungNames[rung]}: memo {memoCap} != fresh probe {fresh}");
                        }
                        if (controlCap != fresh)
                        {
                            res.ControlMismatches++;
                            if (controlMismatchExamples.Count < 6)
                                controlMismatchExamples.Add($"  {tag} rung {rungNames[rung]}: control(no TypeId) {controlCap} != fresh probe {fresh}");
                        }
                    }

                    // Rung 0 — cold: the one probe the DM pays on the first floor-tab click.
                    Check(0);
                    // Rung 1 — move a room by one tile.
                    moveRoom.X += 1f / T;
                    Check(1);
                    // Rung 2 — resize a room.
                    resizeRoom.SizeW += 1;
                    Check(2);
                    // Rung 3 — zero a room's size (a real pre-ApplyDefaults state; sets up rung 4).
                    flipRoom.SizeW = 0; flipRoom.SizeH = 0;
                    Check(3);
                    // Rung 4 — flip that (now size-degenerate) room's TypeId: RoomSizing.Default(1)=6x6 ->
                    // Default(2)=10x10, so this genuinely moves the effective footprint. Id/X/Y/SizeW/SizeH are
                    // untouched, so a key that drops TypeId is BYTE-IDENTICAL before and after this rung.
                    flipRoom.TypeId = 2;
                    Check(4);
                    // Rung 5 — delete a room.
                    ground.Rooms.Remove(deleteRoom);
                    ground.Links.RemoveAll(l => l.RoomA == deleteRoom.Id || l.RoomB == deleteRoom.Id);
                    Check(5);
                    // Rung 6 — add a room.
                    int newId = ground.NextRoomId++;
                    ground.Rooms.Add(new Room { Id = newId, TypeId = 1, SizeW = 4, SizeH = 4,
                        X = col.X + 3f / T, Y = col.Y + 3f / T });
                    Check(6);
                    // Rung 7 — move the column. The column is one of ground.Rooms (M-6), so this changes BOTH
                    // the explicit pin (key[0..3]) and that room's own tail entry — redundant by construction.
                    col.X += 1f / T;
                    Check(7);
                    // Rung 8 — resize the column.
                    col.SizeW += 1;
                    Check(8);
                }

            Console.WriteLine($"capmemo: mutation ladder over {contoursTested} corpus contours (contour seeds 1-25 x ground rooms 6,8,10,12)");
            Console.WriteLine("Each rung: freshCap = BuildingGenerator.MaxRoomsPackable (no memo, ground truth); memoCap = the SHIPPED");
            Console.WriteLine("key (column pin + every room's Id/TypeId/X/Y/SizeW/SizeH); controlCap = the same memo with TypeId DROPPED");
            Console.WriteLine("from the per-room key (negative control — expected to go stale on rung 4 only).");
            Console.WriteLine("| rung                                      | contours | cap changed vs prev | memo mismatches | control(no TypeId) mismatches |");
            for (int i = 0; i < rungs; i++)
            {
                var r = results[i];
                string changedCol = i == 0 ? "n/a (baseline)" : r.Changed.ToString();
                Console.WriteLine($"| {rungNames[i],-42} | {r.Contours,8} | {changedCol,20} | {r.MemoMismatches,16} | {r.ControlMismatches,30} |");
            }

            int totalMemoMismatches = 0, totalControlMismatches = 0, controlMismatchesOffRung4 = 0;
            for (int i = 0; i < rungs; i++)
            {
                totalMemoMismatches += results[i].MemoMismatches;
                totalControlMismatches += results[i].ControlMismatches;
                if (i != 4) controlMismatchesOffRung4 += results[i].ControlMismatches;
            }

            if (memoMismatchExamples.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("SHIPPED MEMO MISMATCHES (examples):");
                foreach (var s in memoMismatchExamples) Console.WriteLine(s);
            }
            Console.WriteLine();
            Console.WriteLine(totalMemoMismatches == 0
                ? $"-> SHIPPED CAP MEMO: 0 mismatches over {contoursTested} contours x {rungs} rungs. Key is complete and invalidates on every rung."
                : $"-> SHIPPED CAP MEMO: {totalMemoMismatches} MISMATCHES — the memo served a STALE cap. This is a real correctness bug.");

            Console.WriteLine($"-> NEGATIVE CONTROL (TypeId dropped): {results[4].ControlMismatches}/{results[4].Contours} mismatches on rung 4 "
                + $"(expected: all of them), {controlMismatchesOffRung4} mismatches on every OTHER rung combined (expected: 0).");
            if (controlMismatchExamples.Count > 0)
            {
                Console.WriteLine("  examples:");
                foreach (var s in controlMismatchExamples) Console.WriteLine(s);
            }

            bool controlWorksAsDesigned = results[4].ControlMismatches > 0 && controlMismatchesOffRung4 == 0;
            Console.WriteLine(controlWorksAsDesigned
                ? "-> Negative control behaves exactly as designed: stale ONLY where TypeId was the only thing that changed."
                : "-> Negative control did NOT behave as designed (see counts above) — the ladder's rung 4 setup needs revisiting.");

            if (totalMemoMismatches > 0)
                throw new Exception($"capmemo: the SHIPPED CapMemo went stale {totalMemoMismatches} time(s) — see the examples above. "
                    + "Stop and report this before anything else; it is a real stale-«из N» bug, not a test artifact.");
            if (!controlWorksAsDesigned)
                throw new Exception("capmemo: the negative control did not mismatch cleanly on rung 4 only — the ladder is not yet a valid "
                    + "non-vacuity proof (see counts above).");
        }
    }
}
