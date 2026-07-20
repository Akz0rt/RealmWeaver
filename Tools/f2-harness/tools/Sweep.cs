using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>THREE-VARIANT corpus sweep for review finding I1 (plus a fifth column for I2): does the
    /// best-of-two packer actually beat the spread-only pipeline, or is the second run pure cost?
    ///   (a) old packer            — LegacyPacker (a verbatim copy of the pre-fix PackAroundColumnWithinFootprint)
    ///   (b) spread-only           — SpreadOnlyLayout   (old seeding + phases 2/3)
    ///   (c) max(compact, spread)  — CompactLayout      (whatever is currently shipped)
    ///   (c') compact-only, no slide — CompactNoSlideLayout (flush seeding + phases 2/3, F4's slide OFF). This is
    ///        the column the class doc's historical "the compact run alone regressed on 2.7% of packs" figure is
    ///        about. It used to be CompactOnlyLayout, but F4 turned THAT class into the compact+SLIDE pipeline,
    ///        which is a different measurement — so it now has its own column, (c'').
    ///   (c'') compact+slide only  — CompactOnlyLayout  (flush seeding + the lateral slide) = run 1 on its own
    ///   (d) no-link-pref          — MutNoLinkPref      (shipped minus the I6 linked-anchor preference), for I2
    ///   (e) pre-F4                — PreSlideLayout     (CompactLayout at e409a9c, the build the DM tested)
    ///   (f) F4 runs 1+2 only      — NoPlainRunLayout   (shipped MINUS run 3, the slide-free compact fallback).
    ///        "(f) vs (e)" re-derives the "34 of 1200 caps FALL without run 3" figure that makes run 3
    ///        load-bearing rather than optional — quoted in CompactLayout's class doc and the F4 report.
    /// (b), (c'), (c''), (d), (f) are mechanically derived from the shipped CompactLayout.cs by sync.ps1, so they cannot
    /// drift. (d) reuses the SAME class the self-test mutant table binds to for non-vacuity (Mutants.cs) — it
    /// is a full two-run pipeline identical to shipped except SeatAgainstAnyPlaced never prefers an anchor the
    /// room is already linked to, so (c) vs (d) isolates I6's cost in rooms kept / cap, not just link length.
    /// Corpus: real floor-0 contours from BuildingGenerator.Generate, real stair-floor graphs, real budgets.</summary>
    public static class Sweep
    {
        const int T = DungeonLayout.TilesPerAxis;
        const int MinSide = 4, MaxSideExclusive = 7;

        // Same construction as BuildingGenerator.BuildStairFloorGraph (private there).
        public static InteriorFloor StairGraph(Random rng, int roomCount, int colW, int colH)
        {
            var floor = new InteriorFloor();
            for (int i = 0; i < roomCount; i++)
                floor.Rooms.Add(new Room { Id = i + 1, TypeId = (i == 0) ? 2 : 1 });
            floor.NextRoomId = roomCount + 1;
            floor.Rooms[0].SizeW = colW; floor.Rooms[0].SizeH = colH;
            for (int i = 1; i < floor.Rooms.Count; i++)
            {
                floor.Rooms[i].SizeW = rng.Next(MinSide, MaxSideExclusive);
                floor.Rooms[i].SizeH = rng.Next(MinSide, MaxSideExclusive);
            }
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in floor.Rooms) adj[r.Id] = new HashSet<int>();
            void Connect(int a, int b)
            {
                if (a == b || adj[a].Contains(b)) return;
                adj[a].Add(b); adj[b].Add(a);
                floor.Links.Add(new Link { RoomA = a, RoomB = b });
            }
            for (int i = 1; i < roomCount; i++) Connect(floor.Rooms[i].Id, floor.Rooms[rng.Next(0, i)].Id);
            int extra = roomCount / 5, guard = 0;
            while (extra > 0 && guard++ < roomCount * 8 && roomCount >= 2)
            {
                int a = rng.Next(roomCount), b = rng.Next(roomCount);
                if (a == b || adj[floor.Rooms[a].Id].Contains(floor.Rooms[b].Id)) continue;
                Connect(floor.Rooms[a].Id, floor.Rooms[b].Id);
                extra--;
            }
            return floor;
        }

        public static bool TryGroundContour(int seed, int groundRooms, out InteriorFloor ground, out Room col)
        {
            var b = BuildingGenerator.Generate(seed, "p", groundRooms, 1);
            ground = b.Floors[0];
            col = null;
            foreach (var r in ground.Rooms) if (r.TypeId != 0) { col = r; break; }
            return col != null;
        }

        struct Tally
        {
            public long Total; public int Better, Equal, Worse, WorstDelta;
            public void Add(int mine, int baseline)
            {
                Total += mine;
                if (mine > baseline) Better++;
                else if (mine == baseline) Equal++;
                else { Worse++; if (baseline - mine > WorstDelta) WorstDelta = baseline - mine; }
            }
            public string Row(string name, int cases)
                => $"| {name,-22} | {Total,6} | {Total / (double)cases,6:F2} | {Better,5} | {Equal,5} | {Worse,5} | {(WorstDelta == 0 ? "0" : "-" + WorstDelta),5} |";
        }

        /// <summary>Per-pack room counts across the corpus, all four variants, plus the (c)-vs-(b) delta
        /// histogram that answers "does the extra run place rooms a DM would notice".</summary>
        public static void RunPacks()
        {
            float m = FloorFootprint.ContourMargin;
            int cases = 0;
            Tally ta = default, tb = default, tc = default, tcc = default, tcs = default, td = default,
                  te = default, tf = default;   // vs (a)
            Tally cVsB = default, cVsD = default, cVsE = default, fVsE = default;
            var deltaHist = new Dictionary<int, int>();
            int contoursWhereCBeatsB = 0;
            var exampleCBeatsB = new List<string>();

            for (int contourSeed = 1; contourSeed <= 60; contourSeed++)
                for (int groundRooms = 3; groundRooms <= 10; groundRooms++)
                {
                    if (!TryGroundContour(contourSeed, groundRooms, out var ground, out var col)) continue;
                    float cx = col.X * T, cy = col.Y * T;
                    int budget = BuildingGenerator.MaxRoomsByArea(ground, col.SizeW, col.SizeH);
                    bool contourHit = false;

                    for (int packSeed = 0; packSeed < 6; packSeed++)
                    {
                        var fa = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fb = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fc = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fcc = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fcs = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fd = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var fe = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        var ff = StairGraph(new Random(packSeed), budget, col.SizeW, col.SizeH);
                        int na = LegacyPacker.Pack(fa, fa.Rooms[0].Id, cx, cy, ground, m, flushOnly: false);
                        int nb = SpreadOnlyLayout.PackAroundColumnWithinFootprint(fb, fb.Rooms[0].Id, cx, cy, ground, m);
                        int nc = CompactLayout.PackAroundColumnWithinFootprint(fc, fc.Rooms[0].Id, cx, cy, ground, m);
                        int ncc = CompactNoSlideLayout.PackAroundColumnWithinFootprint(fcc, fcc.Rooms[0].Id, cx, cy, ground, m);
                        int ncs = CompactOnlyLayout.PackAroundColumnWithinFootprint(fcs, fcs.Rooms[0].Id, cx, cy, ground, m);
                        int nd = MutNoLinkPref.PackAroundColumnWithinFootprint(fd, fd.Rooms[0].Id, cx, cy, ground, m);
                        int ne = PreSlideLayout.PackAroundColumnWithinFootprint(fe, fe.Rooms[0].Id, cx, cy, ground, m);
                        int nf = NoPlainRunLayout.PackAroundColumnWithinFootprint(ff, ff.Rooms[0].Id, cx, cy, ground, m);

                        cases++;
                        ta.Add(na, na); tb.Add(nb, na); tc.Add(nc, na); tcc.Add(ncc, na); tcs.Add(ncs, na);
                        td.Add(nd, na); te.Add(ne, na); tf.Add(nf, na);
                        cVsB.Add(nc, nb);
                        cVsD.Add(nc, nd);
                        cVsE.Add(nc, ne);
                        fVsE.Add(nf, ne);
                        int d = nc - nb;
                        deltaHist.TryGetValue(d, out int n0); deltaHist[d] = n0 + 1;
                        if (d > 0 && !contourHit) { contourHit = true; contoursWhereCBeatsB++; }
                        if (d > 0 && exampleCBeatsB.Count < 6)
                            exampleCBeatsB.Add($"contour {contourSeed}/{groundRooms} packSeed {packSeed}: (c) {nc} > (b) {nb} (old {na}, compact-no-slide {ncc}, budget {budget})");

                        Validate($"{contourSeed}/{groundRooms}/{packSeed}", fc, ground, m);
                    }
                }

            Console.WriteLine($"=== PER-PACK ROOMS KEPT over {cases} packs (60 contour seeds x ground rooms 3..10 x 6 pack seeds) ===");
            Console.WriteLine("| variant                |  kept |  avg/pack | vs(a) better | equal | worse | worst |");
            Console.WriteLine(ta.Row("(a) old packer", cases));
            Console.WriteLine(tb.Row("(b) spread-only", cases));
            Console.WriteLine(tc.Row("(c) max(compact,spread)", cases));
            Console.WriteLine(tcc.Row("(c') compact, no slide", cases));
            Console.WriteLine(tcs.Row("(c'') compact+slide", cases));
            Console.WriteLine(td.Row("(d) no-link-pref (I2)", cases));
            Console.WriteLine(te.Row("(e) pre-F4 e409a9c", cases));
            Console.WriteLine(tf.Row("(f) F4 runs 1+2 only", cases));
            Console.WriteLine($"(c) vs (b): better {cVsB.Better}, equal {cVsB.Equal}, worse {cVsB.Worse} (worst -{cVsB.WorstDelta}); "
                              + $"total rooms {tc.Total} vs {tb.Total} (+{tc.Total - tb.Total}); "
                              + $"packs where (c)>(b): {cVsB.Better}/{cases} = {100.0 * cVsB.Better / cases:F2}%, "
                              + $"contours with at least one such pack: {contoursWhereCBeatsB}");
            Console.WriteLine($"(c) vs (d) [I2 — the I6 linked-anchor-preference cost]: better {cVsD.Better}, equal {cVsD.Equal}, "
                              + $"worse {cVsD.Worse} (worst -{cVsD.WorstDelta}); total rooms {tc.Total} vs {td.Total} ({tc.Total - td.Total:+0;-0;0})");
            Console.WriteLine($"(c) vs (e) [F4 — the lateral slide]: better {cVsE.Better}, equal {cVsE.Equal}, "
                              + $"worse {cVsE.Worse} (worst -{cVsE.WorstDelta}); total rooms {tc.Total} vs {te.Total} "
                              + $"({tc.Total - te.Total:+0;-0;0})");
            Console.WriteLine($"(f) vs (e) [run 3 DELETED — what F4's slid runs ALONE would ship]: better {fVsE.Better}, "
                              + $"equal {fVsE.Equal}, worse {fVsE.Worse} (worst -{fVsE.WorstDelta}); total rooms "
                              + $"{tf.Total} vs {te.Total} ({tf.Total - te.Total:+0;-0;0})");
            var keys = new List<int>(deltaHist.Keys); keys.Sort();
            Console.Write("(c)-(b) delta histogram:");
            foreach (var k in keys) Console.Write($" {k:+0;-0;0}:{deltaHist[k]}");
            Console.WriteLine();
            foreach (var s in exampleCBeatsB) Console.WriteLine("  e.g. " + s);
        }

        /// <summary>The USER-VISIBLE number: «из N» = max over the 10 fixed probe seeds. Same three variants.</summary>
        public static void RunCaps()
        {
            float m = FloorFootprint.ContourMargin;
            int contours = 0;
            Tally ta = default, tb = default, tc = default, tcc = default, tcs = default, td = default,
                  te = default, tf = default;
            Tally cVsB = default, cVsD = default, cVsE = default, fVsE = default;
            var examples = new List<string>();

            for (int contourSeed = 1; contourSeed <= 120; contourSeed++)
                for (int groundRooms = 3; groundRooms <= 12; groundRooms++)
                {
                    if (!TryGroundContour(contourSeed, groundRooms, out var ground, out var col)) continue;
                    float cx = col.X * T, cy = col.Y * T;
                    int budget = BuildingGenerator.MaxRoomsByArea(ground, col.SizeW, col.SizeH);
                    int ca = 1, cb = 1, cc = 1, ccc = 1, ccs = 1, cd = 1, ce = 1, cf = 1;
                    for (int s = 0; s < 10; s++)
                    {
                        var fa = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fb = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fc = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fcc = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fcs = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fd = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var fe = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        var ff = StairGraph(new Random(s), budget, col.SizeW, col.SizeH);
                        int na = LegacyPacker.Pack(fa, fa.Rooms[0].Id, cx, cy, ground, m, false);
                        int nb = SpreadOnlyLayout.PackAroundColumnWithinFootprint(fb, fb.Rooms[0].Id, cx, cy, ground, m);
                        int nc = CompactLayout.PackAroundColumnWithinFootprint(fc, fc.Rooms[0].Id, cx, cy, ground, m);
                        int ncc = CompactNoSlideLayout.PackAroundColumnWithinFootprint(fcc, fcc.Rooms[0].Id, cx, cy, ground, m);
                        int ncs = CompactOnlyLayout.PackAroundColumnWithinFootprint(fcs, fcs.Rooms[0].Id, cx, cy, ground, m);
                        int nd = MutNoLinkPref.PackAroundColumnWithinFootprint(fd, fd.Rooms[0].Id, cx, cy, ground, m);
                        int ne = PreSlideLayout.PackAroundColumnWithinFootprint(fe, fe.Rooms[0].Id, cx, cy, ground, m);
                        int nf = NoPlainRunLayout.PackAroundColumnWithinFootprint(ff, ff.Rooms[0].Id, cx, cy, ground, m);
                        if (na > ca) ca = na;
                        if (nb > cb) cb = nb;
                        if (nc > cc) cc = nc;
                        if (ncc > ccc) ccc = ncc;
                        if (ncs > ccs) ccs = ncs;
                        if (nd > cd) cd = nd;
                        if (ne > ce) ce = ne;
                        if (nf > cf) cf = nf;
                    }
                    contours++;
                    ta.Add(ca, ca); tb.Add(cb, ca); tc.Add(cc, ca); tcc.Add(ccc, ca); tcs.Add(ccs, ca);
                    td.Add(cd, ca); te.Add(ce, ca); tf.Add(cf, ca);
                    cVsB.Add(cc, cb);
                    cVsD.Add(cc, cd);
                    cVsE.Add(cc, ce);
                    fVsE.Add(cf, ce);
                    if (cc > cb && examples.Count < 6)
                        examples.Add($"contour {contourSeed}/{groundRooms}: cap (c) {cc} > (b) {cb} (old {ca}, compact-only {ccc}, no-link-pref {cd})");
                }

            Console.WriteLine($"=== PROBED «из N» CAP over {contours} contours (120 seeds x ground rooms 3..12) ===");
            Console.WriteLine("| variant                | sum   |  avg/cont | vs(a) better | equal | worse | worst |");
            Console.WriteLine(ta.Row("(a) old packer", contours));
            Console.WriteLine(tb.Row("(b) spread-only", contours));
            Console.WriteLine(tc.Row("(c) max(compact,spread)", contours));
            Console.WriteLine(tcc.Row("(c') compact, no slide", contours));
            Console.WriteLine(tcs.Row("(c'') compact+slide", contours));
            Console.WriteLine(td.Row("(d) no-link-pref (I2)", contours));
            Console.WriteLine(te.Row("(e) pre-F4 e409a9c", contours));
            Console.WriteLine(tf.Row("(f) F4 runs 1+2 only", contours));
            Console.WriteLine($"(c) vs (b): better {cVsB.Better}, equal {cVsB.Equal}, worse {cVsB.Worse} (worst -{cVsB.WorstDelta}); "
                              + $"sum {tc.Total} vs {tb.Total} (+{tc.Total - tb.Total})");
            Console.WriteLine($"(c) vs (d) [I2 — the I6 linked-anchor-preference cost on the «из N» cap]: better {cVsD.Better}, "
                              + $"equal {cVsD.Equal}, worse {cVsD.Worse} (worst -{cVsD.WorstDelta}); sum {tc.Total} vs {td.Total} "
                              + $"({tc.Total - td.Total:+0;-0;0})");
            Console.WriteLine($"(c) vs (e) [F4 — the lateral slide, on the «из N» cap]: better {cVsE.Better}, "
                              + $"equal {cVsE.Equal}, worse {cVsE.Worse} (worst -{cVsE.WorstDelta}); sum {tc.Total} vs {te.Total} "
                              + $"({tc.Total - te.Total:+0;-0;0})");
            Console.WriteLine($"(f) vs (e) [run 3 DELETED — the «из N» caps F4's slid runs ALONE would ship]: better "
                              + $"{fVsE.Better}, equal {fVsE.Equal}, worse {fVsE.Worse} (worst -{fVsE.WorstDelta}); "
                              + $"sum {tf.Total} vs {te.Total} ({tf.Total - te.Total:+0;-0;0})");
            foreach (var s in examples) Console.WriteLine("  e.g. " + s);
        }

        // Containment / overlap / orphan / duplicate-link invariants on a packed floor.
        static void Validate(string tag, InteriorFloor f, InteriorFloor ground, float m)
        {
            foreach (var r in f.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                if (!FloorFootprint.ContainsRect(ground, m, r.X * T, r.Y * T, w, h))
                    Console.WriteLine($"  BAD containment {tag} room {r.Id}");
            }
            for (int i = 0; i < f.Rooms.Count; i++)
                for (int j = i + 1; j < f.Rooms.Count; j++)
                {
                    var (aw, ah) = DungeonProjection.EffectiveSize(f.Rooms[i]);
                    var (bw, bh) = DungeonProjection.EffectiveSize(f.Rooms[j]);
                    float dx = Math.Abs((f.Rooms[i].X - f.Rooms[j].X) * T) - (aw + bw) * 0.5f;
                    float dy = Math.Abs((f.Rooms[i].Y - f.Rooms[j].Y) * T) - (ah + bh) * 0.5f;
                    if (dx < -0.01f && dy < -0.01f) Console.WriteLine($"  BAD overlap {tag}: {f.Rooms[i].Id}/{f.Rooms[j].Id}");
                }
            var seen = new HashSet<int> { f.Rooms[0].Id };
            var q = new Queue<int>(); q.Enqueue(f.Rooms[0].Id);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (var l in f.Links)
                {
                    int o = l.RoomA == cur ? l.RoomB : (l.RoomB == cur ? l.RoomA : -1);
                    if (o >= 0 && seen.Add(o)) q.Enqueue(o);
                }
            }
            foreach (var r in f.Rooms) if (!seen.Contains(r.Id)) Console.WriteLine($"  BAD orphan {tag} room {r.Id}");
            for (int i = 0; i < f.Links.Count; i++)
                for (int j = i + 1; j < f.Links.Count; j++)
                {
                    var a = f.Links[i]; var c = f.Links[j];
                    if ((a.RoomA == c.RoomA && a.RoomB == c.RoomB) || (a.RoomA == c.RoomB && a.RoomB == c.RoomA))
                        Console.WriteLine($"  BAD duplicate link {tag}: {a.RoomA}-{a.RoomB}");
                }
        }
    }
}
