using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for block generation. Every assertion names the exact cell /
    /// building / count the rule changes — never a bare "it worked" (this project's #1 recorded failure mode
    /// is a test that passes whether or not the rule holds).</summary>
    public class SettlementBlocksSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Settlement Blocks")]
        public void SelfTestBlocks()
        {
            bool ok = true;

            // ---- THE STATED COUNT TOLERANCE ------------------------------------------------------------
            // `targetBuildings` is ADVISORY: subdivision aims for it and the achieved count is whatever the
            // geometry yields, so this is a BAND, never an equality (an exact-count contract is what forced
            // the reverted building cap). The band is asymmetric on purpose, because the two sides are bounded
            // by different things:
            //
            //   UPPER (0.90) — the fill's size class scales with the cell budget (SizeClassFor), so a town
            //     with far more cells than the DM asked for buildings gets BIGGER buildings rather than more
            //     of them. The request is therefore a ceiling in practice, never a floor.
            //   LOWER — nothing can manufacture cells. The contour's interior is ~2.89 * r_cells^2 cells; the
            //     one-cell ring street eats the whole boundary (~6*r_cells) and the subdivision streets eat
            //     more, so the buildable core is a MINORITY of the interior at every town size.
            //
            // RE-MEASURED FOR THE v11 LATTICE (arc C.2, task B). The old band [0.20, 0.90] came from a sweep
            // against the RETIRED radius formula (0.16 + 0.0045*target, clamped at 0.45 NORMALIZED), which
            // the size-class model replaced: a town's radius is a property of its size class now, derived in
            // CELLS from SettlementSizing's own 0.63*(pi*r^2 - 7.9*r) = target fit. That fit is deliberately
            // calibrated for ratio ~1, i.e. it asks for a radius that COULD deliver the target, where the old
            // formula systematically under-provided — so the achieved/requested ratio moved up bodily and the
            // old band is stale by construction, not by any defect in Generate. See task-B-report.md for the
            // measured range this band is set from; Task D re-derives both this and SettlementSizing's own
            // columns from a full seed sweep.
            //
            // THE NEW MEASUREMENT, targets {5,8,12,20,30,40,50,55,60,80,120} x seeds 1..60 (660 towns, run
            // through the harness, per-target rows in task-B-report.md): the ratio lands in [0.517, 1.400],
            // against the old model's [0.250, 0.800]. The band below is that measurement with the SAME
            // headroom the old band used — 0.8x the measured minimum, ~1.13x the measured maximum — so it is
            // a real constraint over the swept range and not one widened until the code fit.
            //
            // NOT A PROPERTY OF Generate FOR EVERY target, though — only for target >= MinBandTarget, which
            // is where Check() below is ever called. Very small targets break the UPPER bound by
            // construction, not by any layout defect: achieved is never less than one whole building, and the
            // upper end of the band above comes from exactly that regime (target 5 reaches 1.400, target 8
            // reaches 1.375, while target 120 tops out at 0.792).
            const float MinRatio = 0.41f, MaxRatio = 1.58f;
            const int MinBandTarget = 5;

            // The contour a town sized for `target` buildings gets. SettlementGenerator.WallRadiusFor takes a
            // SIZE CLASS now, and this sweep deliberately walks targets BETWEEN the three classes — so it
            // inverts SettlementSizing's own derivation rather than inventing a second one:
            //     0.63 * (pi*r^2 - 7.9*r) = target   ->   pi*r^2 - 7.9*r - target/0.63 = 0
            // solved by the quadratic formula for the positive root. At the three shipped targets this
            // reproduces the table exactly (20 -> 4.68, 50 -> 6.44, 120 -> 9.15 cells against the table's
            // 4.7 / 6.4 / 9.1), so a swept target is measured against the same model production uses.
            float SweepRadiusNorm(int target)
            {
                double c = target / 0.63;
                double r = (7.9 + System.Math.Sqrt(7.9 * 7.9 + 4.0 * System.Math.PI * c)) / (2.0 * System.Math.PI);
                return (float)r * SettlementFootprint.Pitch;
            }

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
                // no frontage, so a block's SIZE is not a property anything bounds any more. What replaced it
                // is the frontage rule and the block-DEPTH property it implies, both asserted in
                // SelfTestFrontage below (a 40-cell block is fine as long as no cell in it is more than one
                // step from a street).

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

        /// <summary>THE FRONTAGE RULE AND WHAT IT IMPLIES, over 12 seeds x 3 size classes. Everything here is
        /// a property of the town's GEOMETRY — which cell is a street, which cell touches one — never of a
        /// count or a ratio: a metric can be satisfied by the wrong shape, and this line of work has shipped
        /// assertions that were.</summary>
        [ContextMenu("Self-Test: Frontage Streets")]
        public void SelfTestFrontage()
        {
            bool ok = true;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            var seeds = new[] { 1, 2, 3, 5, 7, 11, 13, 17, 23, 29, 37, 41 };

            foreach (var size in sizes)
            {
                // Per-size roll-up for task D's calibration sweep — printed once per size, never asserted on.
                int achievedMin = int.MaxValue, achievedMax = 0, achievedSum = 0;

                foreach (var seed in seeds)
                {
                    var wall = WallContour.Rounded(seed, 0.5f, 0.5f, SettlementSizing.WallRadiusNorm(size),
                                                   SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
                    var layout = SettlementBlocks.Generate(wall, seed, size);
                    string at = $"[seed {seed}, size {size}]";

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

                    // ---- 5. BLOCK DEPTH ----------------------------------------------------------------
                    // Stated as the GEOMETRY, not as a size cap or a ratio: no block may contain a cell whose
                    // four orthogonal neighbours are all in that same block, because such a cell has no
                    // frontage in any direction. That is the two-row property — a block is at most two cells
                    // deep from a street on every axis — expressed as the one cell that would break it.
                    var blockCells = new System.Collections.Generic.List<(int i, int j)>();
                    foreach (var c in core) if (!streetSet.Contains(c)) blockCells.Add(c);
                    var blocks = SettlementBlocks.Components(blockCells);
                    // `depthReported` and not `ok`: this sweep must run on its own merits even when an
                    // earlier section has already failed, or a mutant that trips section 1 would leave this
                    // one silently unexercised and its non-vacuity unproven.
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

                    int achieved = layout.Buildings.Count;
                    if (achieved < achievedMin) achievedMin = achieved;
                    if (achieved > achievedMax) achievedMax = achieved;
                    achievedSum += achieved;
                }

                // MEASUREMENT, NOT AN ASSERTION. Task D re-derives SettlementSizing's radii and guaranteed
                // minimums from exactly these numbers, so they are printed rather than pinned — a band here
                // would only have to move again the moment the table does.
                Debug.Log($"Frontage yield [{size}]: target {SettlementSizing.TargetBuildings(size)}, achieved min {achievedMin} / avg {achievedSum / (float)seeds.Length:F1} / max {achievedMax} over {seeds.Length} seeds (guarantee claims {SettlementSizing.GuaranteedMinBuildings(size)})");
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
