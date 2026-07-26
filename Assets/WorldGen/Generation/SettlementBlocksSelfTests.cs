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
            //   LOWER (0.20) — nothing can manufacture cells. The wall radius is WallRadiusFor(target), whose
            //     interior is ~2.89 * (r/Pitch)^2 cells; the one-cell ring street eats the whole boundary
            //     (~6*r/Pitch cells) and the subdivision streets eat more, so the buildable core is a
            //     MINORITY of the interior at every town size — about half of it is street.
            //
            // MEASURED, not guessed: a sweep over targets {5,8,12,20,30,40,55,60,80} x seeds 1..60 (540
            // towns, reported in task-A3-report.md) puts the achieved/requested ratio in [0.250, 0.800]. The
            // band below is that measurement with headroom on both sides, so it is a real constraint over
            // THAT swept range, and not one widened until the code fit.
            //
            // NOT A PROPERTY OF Generate FOR EVERY target, though — only for target >= 5, which is where the
            // sweep starts and where Check() below is ever called. targets 1..4 are ALSO production-reachable
            // (DungeonInspectorPanel.StepTargetBuildings clamps only to Max(1, ...)) and break the UPPER bound
            // by construction, not by any layout defect: achieved is never less than one whole building, so
            // target 1 alone can read ratio 3.00 (1..3 buildings observed), target 2 up to 1.50, 3 up to 1.33,
            // 4 up to 1.00 — all measured, none of them a collapse. Check() is scoped to target >= 5 below;
            // this test makes no claim about the small-target regime.
            const float MinRatio = 0.20f, MaxRatio = 0.90f;
            const int MinBandTarget = 5;

            // One full structural sweep of a generated layout. Called for several (seed, target) pairs below
            // so the invariants are pinned across towns, not on one lucky fixture.
            void Check(int seed, int target)
            {
                var wall = WallContour.Rounded(seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(target),
                                               SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
                var layout = SettlementBlocks.Generate(wall, seed, target);
                string at = $"[seed {seed}, target {target}]";

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

                // ---- 5. SUBDIVISION ACTUALLY CARVES: NO BLOCK SURVIVES OVER THE TARGET SIZE ------------
                // The blocks are not part of the layout's output, so they are RECOVERED here the only way
                // they can be: the 4-connected components of (interior cells MINUS street cells). Two
                // different blocks are never 4-adjacent — a cut always leaves its one-cell street strip
                // between them — so those components ARE the blocks.
                //
                // The bound is EXACT, not a rule of thumb: subdivision stops either at
                // SettlementBlocks.BlockTargetCells or because the block is too thin to cut on either axis,
                // and "too thin on both axes" means both bbox extents are under 3, i.e. at most 2x2 = 4
                // cells — already inside the bound. So no block may exceed it for any reason.
                var interior = SettlementBlocks.InteriorCells(wall);
                var unstreeted = new System.Collections.Generic.List<(int i, int j)>();
                foreach (var c in interior) if (!streetSet.Contains(c)) unstreeted.Add(c);
                foreach (var comp in SettlementBlocks.Components(unstreeted))
                    if (comp.Count > SettlementBlocks.BlockTargetCells)
                    {
                        var bb = SettlementFootprint.Bounds(comp);
                        Debug.LogError($"FAIL blocks {at}: a block of {comp.Count} cells (bbox {bb}) came through subdivision uncut, want <= {SettlementBlocks.BlockTargetCells} — the streets did not carve the interior"); ok = false; break;
                    }

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
                var again = SettlementBlocks.Generate(wall, seed, target);
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
            var wallA = WallContour.Rounded(13, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(40),
                                            SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var seedA = SettlementBlocks.Generate(wallA, 13, 40);
            var seedB = SettlementBlocks.Generate(wallA, 14, 40);   // SAME wall, different seed
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
            var empty = SettlementBlocks.Generate(null, 1, 40);
            if (empty.StreetCells.Count != 0 || empty.Buildings.Count != 0 || empty.GateCells.Count != 0)
            { Debug.LogError($"FAIL blocks: a null contour yielded {empty.StreetCells.Count} streets / {empty.Buildings.Count} buildings / {empty.GateCells.Count} gates, want an empty layout"); ok = false; }

            if (ok) Debug.Log("Settlement Blocks: PASS");
        }

        [ContextMenu("Self-Test: Blocks Sanity")]
        public void SelfTestBlocksSanity()
        {
            // Trailing non-reboundable sentinel: a plain smoke check so mutant-reboundable tests are never
            // last (sync.ps1's rebind scans forward for the NEXT method marker and would truncate otherwise).
            bool ok = true;
            var layout = SettlementBlocks.Generate(new WallContour(), 1, 10);
            if (layout == null)
            { Debug.LogError("FAIL blocks-sanity: Generate returned null for an empty contour"); ok = false; }
            else if (layout.StreetCells == null || layout.Buildings == null || layout.GateCells == null)
            { Debug.LogError("FAIL blocks-sanity: Generate returned a layout with a null list"); ok = false; }

            if (ok) Debug.Log("Settlement Blocks Sanity: PASS");
        }
    }
}
