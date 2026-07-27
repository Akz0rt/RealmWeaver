using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for block generation. Every assertion names the exact cell /
    /// building / count the rule changes — never a bare "it worked" (this project's #1 recorded failure mode
    /// is a test that passes whether or not the rule holds).</summary>
    public class SettlementBlocksSelfTests : MonoBehaviour
    {
        /// <summary>The contour a town sized for `target` buildings gets, for the BETWEEN-the-three-classes
        /// sweeps below — deliberately NOT SettlementSizing.WallRadiusNorm, which (post-Task-D) is a table of
        /// three MEASURED radii with no formula behind it any more. This inverts the algebraic model
        /// SettlementSizing used to fit its own table BEFORE Task D replaced it:
        ///     0.63 * (pi*r^2 - 7.9*r) = target   ->   pi*r^2 - 7.9*r - target/0.63 = 0
        /// solved by the quadratic formula for the positive root. THIS IS NOW A RETIRED MODEL, kept alive only
        /// to generate a spread of test contours between the shipped classes — it no longer predicts what the
        /// table ships (Task D's measured radii are 4.7 / 7.0 / 10.0 cells at the three targets, against this
        /// formula's 4.68 / 6.44 / 9.15: Small still lands close by coincidence, Medium and Large do not,
        /// because the frontage-street layout's real yield outgrew what a 0.63-efficiency model predicts once
        /// arc C.2 shipped and Task D re-measured against seeds instead of algebra). The sweeps below don't
        /// need it to match: SelfTestBlocks/SelfTestFrontage exercise STRUCTURAL invariants (frontage,
        /// reachability, gate separation, block depth) that must hold for a contour of ANY scale, so a
        /// self-consistent spread of radii serves that purpose exactly as well as production's own three would.
        ///
        /// A CLASS MEMBER, not a local function in one test: SelfTestBlocks and SelfTestFrontage both sweep
        /// these contours, and two copies of a radius model are two things that can drift apart. It stays
        /// OUTSIDE every [ContextMenu] method, so sync.ps1's mutant rebind — which extracts one method body
        /// and leaves the rest of the class alone — carries it into every rebound copy unchanged.</summary>
        static float SweepRadiusNorm(int target)
        {
            double c = target / 0.63;
            double r = (7.9 + System.Math.Sqrt(7.9 * 7.9 + 4.0 * System.Math.PI * c)) / (2.0 * System.Math.PI);
            return (float)r * SettlementFootprint.Pitch;
        }

        [ContextMenu("Self-Test: Settlement Blocks")]
        public void SelfTestBlocks()
        {
            bool ok = true;

            // ---- THE STATED COUNT TOLERANCE ------------------------------------------------------------
            // `targetBuildings` is ADVISORY: the layout aims for it and the achieved count is whatever the
            // geometry yields, so this is a BAND, never an equality (an exact-count contract is what forced
            // the reverted building cap). The band is asymmetric on purpose, because the two sides are bounded
            // by different things:
            //
            //   UPPER — the fill's size class scales with the cell budget (SizeClassFor), so a town with far
            //     more cells than the DM asked for buildings gets BIGGER buildings rather than more of them.
            //   LOWER — nothing can manufacture cells. The contour's interior is ~2.89 * r_cells^2 cells and
            //     the ring street eats the whole boundary (~6*r_cells) before a single house is placed.
            //
            // RE-MEASURED FOR THE FRONTAGE LAYOUT (arc C.2, task C), and the re-measurement was NOT optional.
            // The previous band [0.41, 1.58] was derived — 0.8x the measured minimum, ~1.13x the measured
            // maximum — from a task-B sweep that ran against RECURSIVE SUBDIVISION, and this task replaced
            // that rule wholesale: streets are laid only where a house would otherwise have no frontage, which
            // spends far less of the interior on street and lifted the achieved count substantially at all
            // three shipped sizes. (The task-C report quotes that lift as +23% / +47% / +59%; those three
            // percentages were measured against the PRE-Task-D radii — Medium 6.4 and Large 9.1 cells — which
            // Task D's calibration then moved to 7.0 and 10.0. They have NOT been re-derived at the shipped
            // radii, so read them as the historical evidence that the layout changed, never as the current
            // yield. The current per-size yield is SelfTestSizeCalibration's own 200-seed sweep, which prints
            // min/median/max at exactly these radii.) A band whose stated derivation describes a layout that
            // no longer exists is a band that has stopped constraining anything it claims to.
            //
            // THE MEASUREMENT THIS BAND IS SET FROM: targets {5,8,12,20,30,40,50,55,60,80,120} x seeds 1..60,
            // 660 towns, the SAME fixture shape Check() uses below (wall = SweepRadiusNorm(target), size =
            // FromLegacyTarget(target)), run through the harness. Per-target rows are in task-C-report.md.
            // The ratio lands in [0.500, 1.500] — against the subdivision layout's [0.517, 1.400]:
            //     LOW  0.500 at target 80, seed 19   (target 80 is the worst row overall: 0.500..0.838)
            //     HIGH 1.500 at target  8, seed 48   (target  8 is the loosest row: 0.750..1.500)
            // The band below applies the SAME headroom convention the file has used since task B — 0.8x the
            // measured minimum, ~1.13x the measured maximum — to those two numbers.
            //
            // WHY THE UPPER EDGE SITS ABOVE 1, and it is not slack anyone chose: it is set entirely by the
            // SMALL-TARGET QUANTIZATION regime. A town asked for 8 buildings cannot deliver a fraction of one,
            // so a single extra house is +0.125 of ratio all by itself; targets 5/8/12 reach 1.400/1.500/1.417
            // while every target from 20 up stays at or under 1.150 and target 120 tops out at 0.967. Check()
            // is called with targets 8, 20, 40 and 80, so exactly one of its four fixtures lives in that
            // regime — and MinBandTarget keeps the band from being applied below it at all.
            const float MinRatio = 0.40f, MaxRatio = 1.70f;
            const int MinBandTarget = 5;

            // One full structural sweep of a generated layout. Called for several (seed, target) pairs below
            // so the invariants are pinned across towns, not on one lucky fixture.
            //
            // `target` STILL DRIVES THE WALL, AND ONLY THE WALL (arc C.2, task C). Generate takes a SIZE
            // CLASS now, so the sweep bucket's `target` through SettlementSizing.FromLegacyTarget to get one
            // — which means the number Generate reads for its own size class (TargetBuildings(size)) is NOT
            // this `target`, and the two deliberately disagree for every swept target that is not one of the
            // three shipped ones. That is the point of sweeping between the classes: the invariants below are
            // structural and must hold for a contour of ANY scale, not only the three the table ships.
            void Check(int seed, int target)
            {
                var size = SettlementSizing.FromLegacyTarget(target);
                var wall = WallContour.Rounded(seed, 0.5f, 0.5f, SweepRadiusNorm(target),
                                               SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
                var layout = SettlementBlocks.Generate(wall, seed, size);
                string at = $"[seed {seed}, target {target}, size {size}]";

                var streetSet = new System.Collections.Generic.HashSet<(int i, int j)>();
                foreach (var c in layout.StreetCells) streetSet.Add(c);

                // ---- 1. NO TWO BUILDING FOOTPRINTS SHARE A CELL ----------------------------------------
                // Flush is the design; OVERLAPPING is the defect. Names the shared cell and both buildings.
                var owner = new System.Collections.Generic.Dictionary<(int i, int j), int>();
                for (int b = 0; b < layout.Buildings.Count; b++)
                    foreach (var c in layout.Buildings[b])
                    {
                        if (owner.TryGetValue(c, out int prev))
                        { Debug.LogError($"FAIL blocks {at}: buildings {prev} and {b} both occupy cell ({c.i},{c.j})"); ok = false; }
                        else owner[c] = b;
                    }

                // ---- 2. EVERY FOOTPRINT IS NON-EMPTY AND 4-CONNECTED ------------------------------------
                for (int b = 0; b < layout.Buildings.Count; b++)
                {
                    if (layout.Buildings[b].Count == 0)
                    { Debug.LogError($"FAIL blocks {at}: building {b} has an EMPTY footprint"); ok = false; continue; }
                    if (!SettlementFootprint.IsConnected4(layout.Buildings[b]))
                    {
                        var bb = SettlementFootprint.Bounds(layout.Buildings[b]);
                        Debug.LogError($"FAIL blocks {at}: building {b} ({layout.Buildings[b].Count} cells, bbox {bb}) is not 4-connected — it fell into islands"); ok = false;
                    }
                }

                // ---- 3. EVERY BUILDING FRONTS A STREET — NOTHING IS WALLED IN --------------------------
                // The rule the whole layout exists to guarantee: a house with no street 4-neighbour is a
                // house nobody can reach. Names the offending building and its representative cell.
                for (int b = 0; b < layout.Buildings.Count; b++)
                {
                    bool fronts = false;
                    foreach (var c in layout.Buildings[b])
                        if (streetSet.Contains((c.i - 1, c.j)) || streetSet.Contains((c.i + 1, c.j)) ||
                            streetSet.Contains((c.i, c.j - 1)) || streetSet.Contains((c.i, c.j + 1))) { fronts = true; break; }
                    if (!fronts)
                    {
                        var rep = SettlementFootprint.Representative(layout.Buildings[b]);
                        Debug.LogError($"FAIL blocks {at}: building {b} at cell ({rep.i},{rep.j}) has NO street 4-neighbour — it is walled in"); ok = false;
                    }
                }

                // ---- 4. EVERY STREET CELL IS REACHABLE FROM A GATE, THROUGH STREETS ONLY ---------------
                // ZERO GATES MUST FAIL LOUDLY, and it is asserted BEFORE the BFS on purpose: a BFS seeded
                // from an empty gate list reaches nothing, so with the count check missing a gate-less layout
                // would either pass vacuously (if the town also had no streets) or fail for a reason that
                // names the wrong rule. Every gate must additionally BE a street cell — the two lists
                // describe one network, not two.
                if (layout.GateCells.Count == 0)
                { Debug.LogError($"FAIL blocks {at}: 0 gate cells — a walled town with no gate has no way in, and the reachability check below would be vacuous"); ok = false; }
                foreach (var gc in layout.GateCells)
                    if (!streetSet.Contains(gc))
                    { Debug.LogError($"FAIL blocks {at}: gate cell ({gc.i},{gc.j}) is not one of the street cells"); ok = false; }

                var reached = new System.Collections.Generic.HashSet<(int i, int j)>();
                var stack = new System.Collections.Generic.List<(int i, int j)>();
                foreach (var gc in layout.GateCells)
                    if (streetSet.Contains(gc) && reached.Add(gc)) stack.Add(gc);
                while (stack.Count > 0)
                {
                    var c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                    var n0 = (c.i - 1, c.j); var n1 = (c.i + 1, c.j);
                    var n2 = (c.i, c.j - 1); var n3 = (c.i, c.j + 1);
                    if (streetSet.Contains(n0) && reached.Add(n0)) stack.Add(n0);
                    if (streetSet.Contains(n1) && reached.Add(n1)) stack.Add(n1);
                    if (streetSet.Contains(n2) && reached.Add(n2)) stack.Add(n2);
                    if (streetSet.Contains(n3) && reached.Add(n3)) stack.Add(n3);
                }
                foreach (var c in layout.StreetCells)
                    if (!reached.Contains(c))
                    { Debug.LogError($"FAIL blocks {at}: street cell ({c.i},{c.j}) is unreachable from any gate through street cells ({reached.Count} of {streetSet.Count} street cells reached)"); ok = false; break; }

                // ---- 5. (RETIRED, arc C.2 task C) THE BLOCK-SIZE CAP -----------------------------------
                // What used to stand here required every recovered block to be at or below
                // SettlementBlocks.BlockTargetCells — the recursive subdivision's own stopping rule, restated
                // as an assertion. Subdivision is gone: streets are laid where a house would otherwise have
                // no frontage, so a block's SIZE is not a property anything bounds any more (a 40-cell block
                // is fine as long as no cell in it is more than one step from a street).
                //
                // WHAT REPLACED IT IS THE FRONTAGE RULE — SelfTestFrontage assertion 1, and that alone. The
                // block-DEPTH sweep next to it (assertion 5 there) is NOT a second guard: it is logically
                // EQUIVALENT to the frontage rule in both directions, so the two can only ever fire together.
                // It earns its place by naming the offending BLOCK — cell count and bbox — where the frontage
                // assertion names only the stranded cell, i.e. it is a restatement kept for DIAGNOSIS. See
                // that assertion's own comment for the proof of the equivalence.

                // ---- 6. THE ACHIEVED COUNT SITS IN THE STATED BAND -------------------------------------
                // Scoped to target >= MinBandTarget — see the band's own doc above for why smaller targets
                // are not a case this band describes.
                if (target >= MinBandTarget)
                {
                    int achieved = layout.Buildings.Count;
                    float lo = MinRatio * target, hi = MaxRatio * target;
                    if (achieved < lo || achieved > hi)
                    { Debug.LogError($"FAIL blocks {at}: achieved {achieved} buildings, want {lo:F1}..{hi:F1} (ratio {(target > 0 ? achieved / (float)target : 0f):F2}, band {MinRatio:F2}..{MaxRatio:F2})"); ok = false; }
                }

                // ---- 7. DETERMINISM: THE SAME SEED REPRODUCES THE LAYOUT EXACTLY -----------------------
                // Cell-for-cell, in order — not "the same counts". A shuffled-but-equal layout is still a
                // regression: SettlementParams.StreetCells and Room.Cells are SERIALIZED, so a reordering
                // rewrites every saved town's bytes.
                var again = SettlementBlocks.Generate(wall, seed, size);
                if (again.StreetCells.Count != layout.StreetCells.Count)
                { Debug.LogError($"FAIL blocks {at}: rerun has {again.StreetCells.Count} street cells vs {layout.StreetCells.Count} — not deterministic"); ok = false; }
                else
                    for (int k = 0; k < layout.StreetCells.Count; k++)
                        if (again.StreetCells[k] != layout.StreetCells[k])
                        { Debug.LogError($"FAIL blocks {at}: rerun street cell {k} is ({again.StreetCells[k].i},{again.StreetCells[k].j}) vs ({layout.StreetCells[k].i},{layout.StreetCells[k].j}) — not deterministic"); ok = false; break; }
                if (again.GateCells.Count != layout.GateCells.Count)
                { Debug.LogError($"FAIL blocks {at}: rerun has {again.GateCells.Count} gates vs {layout.GateCells.Count} — not deterministic"); ok = false; }
                if (again.Buildings.Count != layout.Buildings.Count)
                { Debug.LogError($"FAIL blocks {at}: rerun has {again.Buildings.Count} buildings vs {layout.Buildings.Count} — not deterministic"); ok = false; }
                else
                    for (int b = 0; b < layout.Buildings.Count; b++)
                    {
                        if (again.Buildings[b].Count != layout.Buildings[b].Count)
                        { Debug.LogError($"FAIL blocks {at}: rerun building {b} has {again.Buildings[b].Count} cells vs {layout.Buildings[b].Count} — not deterministic"); ok = false; break; }
                        bool same = true;
                        for (int k = 0; k < layout.Buildings[b].Count; k++)
                            if (again.Buildings[b][k] != layout.Buildings[b][k]) { same = false; break; }
                        if (!same)
                        { Debug.LogError($"FAIL blocks {at}: rerun building {b} occupies different cells — not deterministic"); ok = false; break; }
                    }
            }

            // A spread of town sizes AND seeds: the invariants are structural, so a single fixture that
            // happened to subdivide neatly would prove far less than this does.
            Check(13, 40);
            Check(2, 40);
            Check(7, 20);
            Check(5, 80);
            Check(9, 8);

            // ---- 8. A DIFFERENT SEED PRODUCES A DIFFERENT TOWN -------------------------------------------
            // Without this, a Generate that ignored `seed` entirely would satisfy every assertion above
            // (determinism included, vacuously).
            var wallA = WallContour.Rounded(13, 0.5f, 0.5f, SweepRadiusNorm(40),
                                            SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var seedA = SettlementBlocks.Generate(wallA, 13, SettlementSize.Medium);
            var seedB = SettlementBlocks.Generate(wallA, 14, SettlementSize.Medium);   // SAME wall, different seed
            bool differs = seedA.Buildings.Count != seedB.Buildings.Count;
            for (int b = 0; !differs && b < seedA.Buildings.Count; b++)
            {
                if (seedA.Buildings[b].Count != seedB.Buildings[b].Count) { differs = true; break; }
                for (int k = 0; k < seedA.Buildings[b].Count; k++)
                    if (seedA.Buildings[b][k] != seedB.Buildings[b][k]) { differs = true; break; }
            }
            if (!differs)
            { Debug.LogError("FAIL blocks: seeds 13 and 14 produced identical building footprints on the same wall — the seed is inert"); ok = false; }

            // ---- 9. A DEGENERATE CONTOUR DEGRADES, IT DOES NOT THROW -------------------------------------
            var empty = SettlementBlocks.Generate(null, 1, SettlementSize.Medium);
            if (empty.StreetCells.Count != 0 || empty.Buildings.Count != 0 || empty.GateCells.Count != 0)
            { Debug.LogError($"FAIL blocks: a null contour yielded {empty.StreetCells.Count} streets / {empty.Buildings.Count} buildings / {empty.GateCells.Count} gates, want an empty layout"); ok = false; }

            if (ok) Debug.Log("Settlement Blocks: PASS");
        }

        /// <summary>THE FRONTAGE RULE AND WHAT IT IMPLIES, over 12 seeds x SEVEN contours. Everything here is
        /// a property of the town's GEOMETRY — which cell is a street, which cell touches one — never of a
        /// count or a ratio: a metric can be satisfied by the wrong shape, and this line of work has shipped
        /// assertions that were.
        ///
        /// SEVEN CONTOURS, NOT THREE. The three SHIPPED radii (4.7 / 7.0 / 10.0 cells, Task D's MEASURED values
        /// — was 4.7 / 6.4 / 9.1 before) are what production ever builds, so they are swept first. But the
        /// rule these assertions state is STRUCTURAL — it must hold
        /// for a contour of any scale — and the assertion this method replaced (the retired block-size cap in
        /// SelfTestBlocks) ran on SelfTestBlocks' four BETWEEN-the-classes contours, of which only ~4.68
        /// overlaps a shipped radius. Sweeping only the shipped three would therefore have left three of the
        /// four contours the old cap covered with nothing asserting that a core cell is fronted at all — the
        /// surviving "every building fronts a street" is true BY CONSTRUCTION (FillBlock seeds only on cells
        /// that already front one) and so is strictly weaker. Both families are swept here.</summary>
        [ContextMenu("Self-Test: Frontage Streets")]
        public void SelfTestFrontage()
        {
            bool ok = true;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            var seeds = new[] { 1, 2, 3, 5, 7, 11, 13, 17, 23, 29, 37, 41 };

            // The same quadratic inversion of SettlementSizing's own fit that SelfTestBlocks.Check uses for its
            // between-the-classes contours — SweepRadiusNorm, hoisted to a class member so both methods sweep
            // the SAME radii instead of two models that could drift apart. See SelfTestBlocks for its
            // derivation.
            int CheckTown(int seed, float radiusNorm, SettlementSize size, string at)
            {
                {
                    var wall = WallContour.Rounded(seed, 0.5f, 0.5f, radiusNorm,
                                                   SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
                    var layout = SettlementBlocks.Generate(wall, seed, size);

                    var streetSet = new System.Collections.Generic.HashSet<(int i, int j)>();
                    foreach (var c in layout.StreetCells) streetSet.Add(c);

                    // The core is recovered the only way it can be — the interior minus the ring — because
                    // the frontage rule is a statement about the CORE, and neither the core nor the ring is
                    // part of the layout's output. Both helpers are the production ones, so this is a
                    // re-derivation of the cell set, not a second implementation of the rule.
                    var interior = SettlementBlocks.InteriorCells(wall);
                    var interiorSet = new System.Collections.Generic.HashSet<(int i, int j)>(interior);
                    var ring = SettlementBlocks.RingStreet(interior, interiorSet);
                    var ringSet = new System.Collections.Generic.HashSet<(int i, int j)>(ring);
                    var core = new System.Collections.Generic.List<(int i, int j)>();
                    foreach (var c in interior) if (!ringSet.Contains(c)) core.Add(c);

                    bool Fronts((int i, int j) c) =>
                        streetSet.Contains((c.i - 1, c.j)) || streetSet.Contains((c.i + 1, c.j)) ||
                        streetSet.Contains((c.i, c.j - 1)) || streetSet.Contains((c.i, c.j + 1));

                    // ---- 1. FRONTAGE: NOTHING IS STRANDED ---------------------------------------------
                    // The rule this whole pass exists for. A core cell is either a street itself or it has a
                    // street 4-neighbour; there is no third case. Names the exact cell.
                    foreach (var c in core)
                        if (!streetSet.Contains(c) && !Fronts(c))
                        { Debug.LogError($"FAIL frontage {at}: core cell ({c.i},{c.j}) is not a street and has no street 4-neighbour — a house there could not reach a road"); ok = false; break; }

                    // ---- 2. ONE NETWORK ----------------------------------------------------------------
                    // A flood-fill through STREET CELLS ONLY, started at the FIRST gate, must reach every
                    // street cell and every other gate. Asserted as two separate failures so an island of
                    // street and an unreachable gate never get reported as the same defect.
                    if (layout.GateCells.Count == 0)
                    { Debug.LogError($"FAIL frontage {at}: 0 gate cells — the reachability sweep below would be vacuous"); ok = false; }
                    else
                    {
                        var reached = new System.Collections.Generic.HashSet<(int i, int j)>();
                        var stack = new System.Collections.Generic.List<(int i, int j)> { layout.GateCells[0] };
                        reached.Add(layout.GateCells[0]);
                        while (stack.Count > 0)
                        {
                            var c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                            var n0 = (c.i - 1, c.j); var n1 = (c.i + 1, c.j);
                            var n2 = (c.i, c.j - 1); var n3 = (c.i, c.j + 1);
                            if (streetSet.Contains(n0) && reached.Add(n0)) stack.Add(n0);
                            if (streetSet.Contains(n1) && reached.Add(n1)) stack.Add(n1);
                            if (streetSet.Contains(n2) && reached.Add(n2)) stack.Add(n2);
                            if (streetSet.Contains(n3) && reached.Add(n3)) stack.Add(n3);
                        }
                        foreach (var c in layout.StreetCells)
                            if (!reached.Contains(c))
                            { Debug.LogError($"FAIL frontage {at}: street cell ({c.i},{c.j}) is unreachable from gate ({layout.GateCells[0].i},{layout.GateCells[0].j}) through streets ({reached.Count} of {streetSet.Count} reached) — the network fell into islands"); ok = false; break; }
                        foreach (var g in layout.GateCells)
                            if (!reached.Contains(g))
                            { Debug.LogError($"FAIL frontage {at}: gate cell ({g.i},{g.j}) is unreachable from gate ({layout.GateCells[0].i},{layout.GateCells[0].j}) through streets"); ok = false; break; }
                    }

                    // ---- 3. THE GATES ------------------------------------------------------------------
                    // Exactly the count the size table names — a town this size has a ring of ~30 cells or
                    // more, so no gate is ever dropped for want of room (the degenerate case that CAN drop
                    // one is pinned on its own fixture at the end of this method).
                    int wantGates = SettlementSizing.GateCount(size);
                    if (layout.GateCells.Count != wantGates)
                    { Debug.LogError($"FAIL frontage {at}: {layout.GateCells.Count} gate cells, want exactly {wantGates}"); ok = false; }
                    for (int a = 0; a < layout.GateCells.Count; a++)
                        for (int b = a + 1; b < layout.GateCells.Count; b++)
                        {
                            var ga = layout.GateCells[a]; var gb = layout.GateCells[b];
                            int di = ga.i > gb.i ? ga.i - gb.i : gb.i - ga.i;
                            int dj = ga.j > gb.j ? ga.j - gb.j : gb.j - ga.j;
                            int cheb = di > dj ? di : dj;
                            if (cheb < SettlementBlocks.MinGateSeparationCells)
                            { Debug.LogError($"FAIL frontage {at}: gates ({ga.i},{ga.j}) and ({gb.i},{gb.j}) are {cheb} apart in Chebyshev, want >= {SettlementBlocks.MinGateSeparationCells} — they read as one wide doorway"); ok = false; }
                        }

                    // ---- 4. THE ARTERIALS ARE THERE --------------------------------------------------
                    // Two things only the arterial pass delivers, both stated as geometry:
                    //   (a) the core cell just inside a gate is a STREET, so the gate opens onto a road and
                    //       not onto the side of a house;
                    //   (b) the town's CENTRE cell is a street, so the roads from the gates meet in the
                    //       middle instead of dying in the first block they hit.
                    // Neither follows from the frontage rule or from one-network — the ring alone already
                    // satisfies both of those — which is exactly why they are asserted separately.
                    foreach (var g in layout.GateCells)
                    {
                        bool hasCoreNeighbour = false, opensOnStreet = false;
                        var around = new (int i, int j)[] { (g.i - 1, g.j), (g.i + 1, g.j), (g.i, g.j - 1), (g.i, g.j + 1) };
                        foreach (var n in around)
                        {
                            if (!interiorSet.Contains(n) || ringSet.Contains(n)) continue;   // not a core cell
                            hasCoreNeighbour = true;
                            if (streetSet.Contains(n)) opensOnStreet = true;
                        }
                        if (hasCoreNeighbour && !opensOnStreet)
                        { Debug.LogError($"FAIL frontage {at}: gate ({g.i},{g.j}) has core 4-neighbours but not one of them is a street — the gate opens onto a building"); ok = false; }
                    }
                    var centre = SettlementBlocks.CentreCell(ring, core);
                    if (core.Count > 0 && !streetSet.Contains(centre))
                    { Debug.LogError($"FAIL frontage {at}: the town centre cell ({centre.i},{centre.j}) is not a street — no arterial reached the middle of town"); ok = false; }

                    // ---- 5. BLOCK DEPTH — A RESTATEMENT OF ASSERTION 1, FOR DIAGNOSIS ------------------
                    // The brief asks for the two-row property to be stated as GEOMETRY (no block contains a
                    // cell whose four orthogonal neighbours are all in that same block) rather than as a count
                    // or a ratio, and that is what this is. BUT IT ADDS NO DETECTION POWER, and pretending
                    // otherwise would be its own kind of vacuity, so: it is LOGICALLY EQUIVALENT to assertion
                    // 1, in both directions.
                    //   (→) A cell surrounded by its own block on all four sides is itself non-street with no
                    //       street 4-neighbour, so assertion 1 fires on it too.
                    //   (←) Every core cell has all four neighbours INTERIOR — RingStreet's base rule puts any
                    //       interior cell with a non-interior 4-neighbour into the RING, and the core is what
                    //       the ring did not take — so an unfronted non-street core cell forces all four of
                    //       its neighbours into `core \ streets`, hence into its own 4-connected component,
                    //       and this assertion fires on it too.
                    // The two therefore fire together, always. What this one adds is the DIAGNOSIS: it names
                    // the offending block, its cell count and its bbox, which is what a reader needs to see
                    // which block went wrong — assertion 1 names only the stranded cell.
                    var blockCells = new System.Collections.Generic.List<(int i, int j)>();
                    foreach (var c in core) if (!streetSet.Contains(c)) blockCells.Add(c);
                    var blocks = SettlementBlocks.Components(blockCells);
                    // `depthReported` and not `ok`: this sweep must still RUN when an earlier section has
                    // already failed, so its diagnosis appears alongside assertion 1's rather than being
                    // skipped exactly when it would be most useful.
                    bool depthReported = false;
                    for (int b = 0; b < blocks.Count && !depthReported; b++)
                    {
                        var member = new System.Collections.Generic.HashSet<(int i, int j)>(blocks[b]);
                        foreach (var c in blocks[b])
                            if (member.Contains((c.i - 1, c.j)) && member.Contains((c.i + 1, c.j)) &&
                                member.Contains((c.i, c.j - 1)) && member.Contains((c.i, c.j + 1)))
                            {
                                var bb = SettlementFootprint.Bounds(blocks[b]);
                                Debug.LogError($"FAIL frontage {at}: block {b} ({blocks[b].Count} cells, bbox {bb}) surrounds cell ({c.i},{c.j}) on all four sides — that cell can never front a street"); ok = false; depthReported = true; break;
                            }
                    }

                    // ---- 6. DETERMINISM -----------------------------------------------------------------
                    // Cell-for-cell and in order, for all three lists. StreetCells and Buildings are
                    // SERIALIZED, so a shuffled-but-equal layout still rewrites every saved town's bytes.
                    var again = SettlementBlocks.Generate(wall, seed, size);
                    if (again.StreetCells.Count != layout.StreetCells.Count)
                    { Debug.LogError($"FAIL frontage {at}: rerun has {again.StreetCells.Count} street cells vs {layout.StreetCells.Count}"); ok = false; }
                    else
                        for (int k = 0; k < layout.StreetCells.Count; k++)
                            if (again.StreetCells[k] != layout.StreetCells[k])
                            { Debug.LogError($"FAIL frontage {at}: rerun street cell {k} is ({again.StreetCells[k].i},{again.StreetCells[k].j}) vs ({layout.StreetCells[k].i},{layout.StreetCells[k].j})"); ok = false; break; }
                    if (again.GateCells.Count != layout.GateCells.Count)
                    { Debug.LogError($"FAIL frontage {at}: rerun has {again.GateCells.Count} gates vs {layout.GateCells.Count}"); ok = false; }
                    else
                        for (int k = 0; k < layout.GateCells.Count; k++)
                            if (again.GateCells[k] != layout.GateCells[k])
                            { Debug.LogError($"FAIL frontage {at}: rerun gate {k} is ({again.GateCells[k].i},{again.GateCells[k].j}) vs ({layout.GateCells[k].i},{layout.GateCells[k].j})"); ok = false; break; }
                    if (again.Buildings.Count != layout.Buildings.Count)
                    { Debug.LogError($"FAIL frontage {at}: rerun has {again.Buildings.Count} buildings vs {layout.Buildings.Count}"); ok = false; }
                    else
                        for (int b = 0; b < layout.Buildings.Count; b++)
                        {
                            if (again.Buildings[b].Count != layout.Buildings[b].Count)
                            { Debug.LogError($"FAIL frontage {at}: rerun building {b} has {again.Buildings[b].Count} cells vs {layout.Buildings[b].Count}"); ok = false; break; }
                            bool same = true;
                            for (int k = 0; k < layout.Buildings[b].Count; k++)
                                if (again.Buildings[b][k] != layout.Buildings[b][k]) { same = false; break; }
                            if (!same)
                            { Debug.LogError($"FAIL frontage {at}: rerun building {b} occupies different cells"); ok = false; break; }
                        }

                    return layout.Buildings.Count;
                }
            }

            // ---- FAMILY A: THE THREE SHIPPED RADII, with the yield roll-up task D calibrates from ---------
            foreach (var size in sizes)
            {
                int achievedMin = int.MaxValue, achievedMax = 0, achievedSum = 0;
                foreach (var seed in seeds)
                {
                    int achieved = CheckTown(seed, SettlementSizing.WallRadiusNorm(size), size, $"[seed {seed}, size {size}]");
                    if (achieved < achievedMin) achievedMin = achieved;
                    if (achieved > achievedMax) achievedMax = achieved;
                    achievedSum += achieved;
                }

                // MEASUREMENT, NOT AN ASSERTION. Task D re-derives SettlementSizing's radii and guaranteed
                // minimums from exactly these numbers, so they are printed rather than pinned — a band here
                // would only have to move again the moment the table does.
                Debug.Log($"Frontage yield [{size}]: target {SettlementSizing.TargetBuildings(size)}, achieved min {achievedMin} / avg {achievedSum / (float)seeds.Length:F1} / max {achievedMax} over {seeds.Length} seeds (guarantee claims {SettlementSizing.GuaranteedMinBuildings(size)})");
            }

            // ---- FAMILY B: THE FOUR BETWEEN-THE-CLASSES CONTOURS SelfTestBlocks SWEEPS --------------------
            // The same four targets SelfTestBlocks.Check is called with — 8 / 20 / 40 / 80, i.e. 3.63 / 4.68 /
            // 5.93 / 7.73 cells of radius — so the frontage rule is asserted across the SAME continuum the
            // retired block-size cap covered, not only on the three radii the size table ships. `size` is the
            // class the target buckets into, exactly as SelfTestBlocks does it, so the gate count asserted is
            // the one that town would actually get.
            foreach (var target in new[] { 8, 20, 40, 80 })
            {
                var size = SettlementSizing.FromLegacyTarget(target);
                foreach (var seed in seeds)
                    CheckTown(seed, SweepRadiusNorm(target), size, $"[seed {seed}, sweep target {target}, size {size}]");
            }

            // ---- 7. THE SEPARATION RULE, ON THE ONLY RING THAT CAN EXERCISE IT --------------------------
            // A real town's ring is ~30 cells or more and its gates come out 9+ cells apart, so the sweep
            // above can never reach the regime MinGateSeparationCells governs — an assertion that only ever
            // watched real towns would pass with the rule deleted. This is the degenerate ring the rule is
            // FOR: the 8 cells around a 3x3 town, where every pair is within Chebyshev 2, so no second gate
            // can legally be placed however the seeded phase falls. Asking for four gates must therefore
            // yield exactly ONE — dropping a gate is the documented legal outcome; putting two gates in one
            // doorway is not.
            var tinyRing = new System.Collections.Generic.List<(int i, int j)>();
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (i != 0 || j != 0) tinyRing.Add((i, j));
            for (int s = 1; s <= 6; s++)
            {
                var tinyGates = SettlementBlocks.PlaceGateCells(tinyRing, 4, s);
                for (int a = 0; a < tinyGates.Count; a++)
                    for (int b = a + 1; b < tinyGates.Count; b++)
                    {
                        var ga = tinyGates[a]; var gb = tinyGates[b];
                        int di = ga.i > gb.i ? ga.i - gb.i : gb.i - ga.i;
                        int dj = ga.j > gb.j ? ga.j - gb.j : gb.j - ga.j;
                        int cheb = di > dj ? di : dj;
                        if (cheb < SettlementBlocks.MinGateSeparationCells)
                        { Debug.LogError($"FAIL frontage [tiny ring, seed {s}]: gates ({ga.i},{ga.j}) and ({gb.i},{gb.j}) are {cheb} apart in Chebyshev, want >= {SettlementBlocks.MinGateSeparationCells}"); ok = false; }
                    }
                if (tinyGates.Count != 1)
                { Debug.LogError($"FAIL frontage [tiny ring, seed {s}]: asked for 4 gates on an 8-cell ring and got {tinyGates.Count}, want exactly 1 — every other ring cell is within {SettlementBlocks.MinGateSeparationCells} of the first"); ok = false; }
            }

            if (ok) Debug.Log("Settlement Frontage Streets: PASS");
        }

        /// <summary>TASK D's MEASURING INSTRUMENT (step 1, INSTRUMENT ONLY — see the class doc rule "measure
        /// first, edit second"; SettlementSizing's WallRadiusCells/GuaranteedMinBuildings doc names the exact
        /// numbers this sweep produced). 200 FIXED seeds per size (seed = 1000+k, k in 0..199) run through the
        /// REAL production path — SettlementSizing.WallRadiusNorm(size) is exactly the contour
        /// SettlementGenerator hands SettlementBlocks.Generate for a town of this size.
        ///
        /// THE CONTRACT (step 4): every one of the 600 generations must yield AT LEAST
        /// SettlementSizing.GuaranteedMinBuildings(size) buildings — the promise the table now makes and the
        /// UI may rely on. The failure names the offending seed, size and achieved count, never a bare "it
        /// failed": a DM-visible guarantee that breaks silently is worse than one that was never made.
        ///
        /// ONE SUMMARY LOG PER SIZE (unconditional, not gated on `ok` — same convention as SelfTestFrontage's
        /// "Frontage yield" line) carries what a future regression needs to be readable rather than merely red:
        ///   - min / median / max ACHIEVED BUILDING COUNT, against the table's target and guarantee;
        ///   - min / median / max INTERIOR CELL COUNT (the buildable budget before streets eat any of it);
        ///   - the FOOTPRINT-SIZE DISTRIBUTION (how many buildings landed at 1 / 2 / 3 / 4-or-more cells) —
        ///     SizeClassFor's own tuning doc justifies it against 3x3 subdivision blocks that no longer exist,
        ///     so this column is what tells a reader whether today's row-house-vs-compound mix still reads
        ///     right. MEASURED (Task D): 100% of buildings land at 1-2 cells, 0% at 3 or 4+, at every one of
        ///     the three shipped sizes — SizeClassFor's class stays at 1 everywhere. This is NOT a bug to
        ///     retune here: SizeClassFor computes round(blockCells/target), and fitting the radius so achieved
        ///     tracks target (the very thing this sweep does) forces blockCells/target toward class 1's OWN
        ///     average footprint (~1.13-1.16 measured, ~1.25 nominal) — comfortably under the 1.5 ratio class 2
        ///     needs. Lowering that threshold would flip every shipped size to class 2 and roughly HALVE the
        ///     achieved count for the same blockCells (avg footprint ~2.0 instead of ~1.25), which would only
        ///     be fixable by re-deriving every radius above around a deliberately different design goal (fewer,
        ///     bigger buildings) — a scope decision for a follow-up task, not a threshold tweak this one can
        ///     make safely. SettlementBlocks.cs (where SizeClassFor lives) is also outside this task's file
        ///     list. See task-D-report.md for the full derivation;
        ///   - WALL-CLOCK for the 200 generations — FrontageFill's greedy re-enumerates every candidate strip
        ///     each iteration, so its cost is NOT the old subdivision sweep's cost and must be measured, not
        ///     assumed. MEASURED: comfortably under two seconds for all 600 generations combined — cheap enough
        ///     to live in the suite.</summary>
        [ContextMenu("Self-Test: Settlement Size Calibration")]
        public void SelfTestSizeCalibration()
        {
            bool ok = true;
            const int SeedCount = 200;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };

            float Median(System.Collections.Generic.List<int> sorted)
            {
                int n = sorted.Count;
                if (n == 0) return 0f;
                if ((n & 1) == 1) return sorted[n / 2];
                return (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
            }

            foreach (var size in sizes)
            {
                int guarantee = SettlementSizing.GuaranteedMinBuildings(size);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var achieved = new System.Collections.Generic.List<int>(SeedCount);
                var interiorCounts = new System.Collections.Generic.List<int>(SeedCount);
                int f1 = 0, f2 = 0, f3 = 0, f4plus = 0;

                for (int k = 0; k < SeedCount; k++)
                {
                    int seed = 1000 + k;
                    var wall = WallContour.Rounded(seed, 0.5f, 0.5f, SettlementSizing.WallRadiusNorm(size),
                                                   SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
                    var interior = SettlementBlocks.InteriorCells(wall);
                    var layout = SettlementBlocks.Generate(wall, seed, size);

                    int achievedCount = layout.Buildings.Count;
                    achieved.Add(achievedCount);
                    interiorCounts.Add(interior.Count);

                    foreach (var b in layout.Buildings)
                    {
                        int n = b.Count;
                        if (n <= 1) f1++;
                        else if (n == 2) f2++;
                        else if (n == 3) f3++;
                        else f4plus++;
                    }

                    // ---- THE CONTRACT: THE GUARANTEE HOLDS ON EVERY SEED, NOT JUST ON AVERAGE ----------
                    if (achievedCount < guarantee)
                    { Debug.LogError($"FAIL size-calibration [{size}, seed {seed}]: achieved {achievedCount} buildings, below the guaranteed minimum {guarantee}"); ok = false; }
                }
                sw.Stop();

                achieved.Sort();
                interiorCounts.Sort();
                int totalBuildings = f1 + f2 + f3 + f4plus;
                Debug.Log($"SizeCalibration [{size}]: buildings min {achieved[0]} / median {Median(achieved):F1} / max {achieved[achieved.Count - 1]} (target {SettlementSizing.TargetBuildings(size)}, guarantee {guarantee}); "
                    + $"interior cells min {interiorCounts[0]} / median {Median(interiorCounts):F1} / max {interiorCounts[interiorCounts.Count - 1]}; "
                    + $"footprints 1-cell {f1} / 2-cell {f2} / 3-cell {f3} / 4plus-cell {f4plus} (of {totalBuildings} total); "
                    + $"{SeedCount} seeds in {sw.ElapsedMilliseconds} ms");
            }

            if (ok) Debug.Log("Settlement Size Calibration: PASS");
        }

        [ContextMenu("Self-Test: Blocks Sanity")]
        public void SelfTestBlocksSanity()
        {
            // Trailing non-reboundable sentinel: a plain smoke check so mutant-reboundable tests are never
            // last (sync.ps1's rebind scans forward for the NEXT method marker and would truncate otherwise).
            bool ok = true;
            var layout = SettlementBlocks.Generate(new WallContour(), 1, SettlementSize.Small);
            if (layout == null)
            { Debug.LogError("FAIL blocks-sanity: Generate returned null for an empty contour"); ok = false; }
            else if (layout.StreetCells == null || layout.Buildings == null || layout.GateCells == null)
            { Debug.LogError("FAIL blocks-sanity: Generate returned a layout with a null list"); ok = false; }

            if (ok) Debug.Log("Settlement Blocks Sanity: PASS");
        }
    }
}
