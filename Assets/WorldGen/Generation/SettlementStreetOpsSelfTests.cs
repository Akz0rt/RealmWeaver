using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>DM findings ·4 and ·9: after any edit every house still has a street to it, and the street
    /// network is one piece. The tests below are the whole automated coverage of that promise — the four
    /// places that CALL it live in Rendering and Persistence, which the offline harness never compiles.</summary>
    public class SettlementStreetOpsSelfTests : MonoBehaviour
    {
        // A settlement floor with one single-cell building per listed cell and the given street cells.
        static InteriorFloor Floor(System.Collections.Generic.List<(int i, int j)> streets,
                                   params (int i, int j)[] buildings)
        {
            var f = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = true } };
            int id = 1;
            foreach (var (i, j) in buildings)
                f.Rooms.Add(new Room
                {
                    Id = id++, TypeId = 1,
                    X = SettlementFootprint.CenterOf(i), Y = SettlementFootprint.CenterOf(j),
                    Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (i, j) })
                });
            if (streets != null && streets.Count > 0)
                f.SettlementParams.StreetCells = SettlementFootprint.Encode(streets);
            return f;
        }

        static System.Collections.Generic.List<(int i, int j)> Cells(params (int i, int j)[] c)
            => new System.Collections.Generic.List<(int i, int j)>(c);

        // BOTH halves of the invariant, spelled out inline rather than shared with the code under test —
        // a helper that called SettlementStreetOps would be testing the implementation against itself.
        static bool InvariantHolds(InteriorFloor f, out string why)
        {
            why = null;
            var streets = new System.Collections.Generic.HashSet<(int i, int j)>(
                SettlementFootprint.Decode(f.SettlementParams?.StreetCells));
            foreach (var r in f.Rooms)
            {
                if (r.TypeId != 1) continue;
                bool fronts = false;
                foreach (var c in SettlementTileGrid.FootprintOf(r))
                    if (streets.Contains((c.i - 1, c.j)) || streets.Contains((c.i + 1, c.j))
                     || streets.Contains((c.i, c.j - 1)) || streets.Contains((c.i, c.j + 1))) { fronts = true; break; }
                if (!fronts) { why = $"building {r.Id} has no street 4-neighbour"; return false; }
            }
            if (streets.Count == 0) return true;
            // one 4-connected component
            var seen = new System.Collections.Generic.HashSet<(int i, int j)>();
            var stack = new System.Collections.Generic.List<(int i, int j)>();
            foreach (var s in streets) { stack.Add(s); seen.Add(s); break; }
            while (stack.Count > 0)
            {
                var cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                var n4 = new[] { (cur.i - 1, cur.j), (cur.i + 1, cur.j), (cur.i, cur.j - 1), (cur.i, cur.j + 1) };
                foreach (var n in n4)
                    if (streets.Contains(n) && seen.Add(n)) stack.Add(n);
            }
            if (seen.Count != streets.Count)
            {
                why = $"street network is {streets.Count - seen.Count} cells short of one component "
                    + $"({seen.Count} reached of {streets.Count})";
                return false;
            }
            return true;
        }

        /// <summary>The repair, on the real corpus and on the three shapes that break it.</summary>
        [ContextMenu("Self-Test: Street Access")]
        public void SelfTestStreetAccess()
        {
            bool ok = true;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };

            // 1. A FRESHLY GENERATED town already satisfies the invariant, so the repair is a no-op.
            //    If this ever fails it is a finding about the GENERATOR, not about this code — say so.
            foreach (var size in sizes)
                for (int k = 0; k < 40; k++)
                {
                    int seed = 1000 + k;
                    var cfg = new SettlementConfig { Seed = seed, Size = size, ActiveBuildings = 1, HasWall = true };
                    var floor = SettlementGenerator.Generate(cfg, "poi").Floors[0];
                    if (!InvariantHolds(floor, out string why))
                    {
                        Debug.LogError($"SelfTestStreetAccess: {size} seed {seed}: the GENERATOR emitted a town "
                                     + $"that already breaks the invariant — {why}");
                        ok = false;
                    }
                    int added = SettlementStreetOps.EnsureAccess(floor);
                    if (added != 0)
                    {
                        Debug.LogError($"SelfTestStreetAccess: {size} seed {seed}: EnsureAccess added {added} "
                                     + "cells to a freshly generated town, which should already be legal");
                        ok = false;
                    }
                }

            // 2. A BUILDING MOVED FAR OUT — DM finding ·9. Take a real town, translate one building's
            //    footprint 12 cells east of the town's bounding box, and repair.
            foreach (var size in sizes)
                for (int k = 0; k < 40; k++)
                {
                    int seed = 1000 + k;
                    var cfg = new SettlementConfig { Seed = seed, Size = size, ActiveBuildings = 1, HasWall = true };
                    var floor = SettlementGenerator.Generate(cfg, "poi").Floors[0];
                    Room moved = null;
                    int maxI = int.MinValue;
                    foreach (var r in floor.Rooms)
                    {
                        if (r.TypeId != 1) continue;
                        if (moved == null) moved = r;
                        foreach (var c in SettlementTileGrid.FootprintOf(r)) if (c.i > maxI) maxI = c.i;
                    }
                    if (moved == null) continue;
                    var fp = SettlementTileGrid.FootprintOf(moved);
                    int dx = maxI + 12 - fp[0].i;
                    var movedCells = SettlementFootprint.Translate(fp, dx, 0);
                    moved.Cells = SettlementFootprint.Encode(movedCells);
                    var rep = SettlementFootprint.Representative(movedCells);
                    moved.X = SettlementFootprint.CenterOf(rep.i);
                    moved.Y = SettlementFootprint.CenterOf(rep.j);

                    int added = SettlementStreetOps.EnsureAccess(floor);
                    if (added <= 0)
                    {
                        Debug.LogError($"SelfTestStreetAccess: {size} seed {seed}: a building moved 12 cells "
                                     + $"clear of town needed no new street — EnsureAccess added {added}");
                        ok = false;
                    }
                    if (!InvariantHolds(floor, out string why2))
                    {
                        Debug.LogError($"SelfTestStreetAccess: {size} seed {seed}: after EnsureAccess — {why2}");
                        ok = false;
                    }
                    if (SettlementStreetOps.EnsureAccess(floor) != 0)
                    {
                        Debug.LogError($"SelfTestStreetAccess: {size} seed {seed}: EnsureAccess is not "
                                     + "idempotent — a second call added cells");
                        ok = false;
                    }
                }

            // 3. A BUILDING DELETED. The delete SITE is in Rendering and unreachable here; what is provable
            //    is that the repair still holds on the floor a delete leaves behind.
            {
                var floor = Floor(Cells((1, 0), (2, 0), (3, 0)), (1, 1), (2, 1), (3, 1));
                floor.Rooms.RemoveAt(1);
                if (SettlementStreetOps.EnsureAccess(floor) != 0)
                {
                    Debug.LogError("SelfTestStreetAccess: deleting a middle building needed a repair, but its "
                                 + "street already served the survivors");
                    ok = false;
                }
                if (!InvariantHolds(floor, out string why3))
                {
                    Debug.LogError($"SelfTestStreetAccess: after a delete — {why3}");
                    ok = false;
                }
            }

            // 4. A LEGACY TOWN WITH NO STREETS AT ALL — the v9 shape, and the common case rather than a
            //    degenerate one. The bootstrap must invent a network and connect everything to it, and —
            //    since this is exactly the shape the load path exercises on EVERY legacy file, so its
            //    bootstrap firing exactly ONCE per load matters — a second call must add nothing.
            {
                var floor = Floor(null, (0, 0), (0, 1), (5, 5));
                int added = SettlementStreetOps.EnsureAccess(floor);
                if (added <= 0)
                {
                    Debug.LogError($"SelfTestStreetAccess: a street-less town got {added} cells — the "
                                 + "bootstrap did not fire");
                    ok = false;
                }
                if (!InvariantHolds(floor, out string why4))
                {
                    Debug.LogError($"SelfTestStreetAccess: street-less town after repair — {why4}");
                    ok = false;
                }
                if (SettlementStreetOps.EnsureAccess(floor) != 0)
                {
                    Debug.LogError("SelfTestStreetAccess: a second EnsureAccess on the bootstrapped v9 town "
                                 + "added more cells — the bootstrap path is not idempotent");
                    ok = false;
                }
            }

            // 5. DETERMINISM UNDER REVERSED INSERTION ORDER. Two floors describing the SAME town — same
            //    buildings, same street cells — but built with its rooms and its street cells appended in
            //    REVERSED order the second time, must still yield identical MissingAccess output. An
            //    IDENTICALLY-constructed second copy (the shape this case used to be) does not test this:
            //    for (int, int) tuples .NET's hashing is not randomised, so a HashSet's enumeration order is
            //    itself a deterministic function of insertion order — a bug that let some decision ride along
            //    with HashSet enumeration, instead of an explicit row-major tie-break, would still return the
            //    same list both times on two identically-ordered builds, and this case would have passed
            //    right over it. Reversing the insertion order is what a hidden dependence on incidental
            //    enumeration order cannot survive.
            //
            //    The fixture is TWO EQUAL-SIZED, already-served orphan stubs — the shape where
            //    SmallestOrphanComponent must break a genuine size tie — at coordinates chosen empirically
            //    to exercise it. Reversing case 7's ORIGINAL two-stub pair, (1,0)/(10,0), was tried FIRST and
            //    does NOT diverge, even under a deliberately injected order-dependence bug: for only two
            //    non-colliding int-pair hashes, HashSet<(int,int)> enumeration order depends on hash-bucket
            //    placement, not insertion order, so both builds enumerate in the same relative order either
            //    way. (2,2)/(9,9) DOES diverge under the same injected bug — confirmed by running both pairs
            //    against a temporarily-broken SmallestOrphanComponent (its sort and its tie-break both
            //    removed, so a genuine size tie rides along with whatever order the streets HashSet happens
            //    to enumerate): (1,0)/(10,0) still passed; (2,2)/(9,9) failed with "MissingAccess is not
            //    deterministic — cell 1 was (4, 2) one run and (3, 3) the next". So this is the pair that
            //    actually exercises the property, not one that merely looks like it does.
            {
                var a = Floor(Cells((2, 2), (9, 9)), (2, 3), (9, 8));
                var b = Floor(Cells((9, 9), (2, 2)), (9, 8), (2, 3));
                var ca = SettlementStreetOps.MissingAccess(a);
                var cb = SettlementStreetOps.MissingAccess(b);
                if (ca.Count != cb.Count)
                {
                    Debug.LogError($"SelfTestStreetAccess: MissingAccess is not deterministic — {ca.Count} "
                                 + $"cells one run, {cb.Count} the next");
                    ok = false;
                }
                else
                    for (int k = 0; k < ca.Count; k++)
                        if (ca[k] != cb[k])
                        {
                            Debug.LogError($"SelfTestStreetAccess: MissingAccess is not deterministic — cell "
                                         + $"{k} was {ca[k]} one run and {cb[k]} the next");
                            ok = false;
                            break;
                        }
                if (ca.Count == 0)
                {
                    Debug.LogError("SelfTestStreetAccess: the determinism fixture needed no repair, so it "
                                 + "compares two empty lists and proves nothing");
                    ok = false;
                }
            }

            // 6. NO BUILDINGS AT ALL — must not throw and must not invent streets.
            {
                var floor = Floor(null);
                if (SettlementStreetOps.EnsureAccess(floor) != 0)
                {
                    Debug.LogError("SelfTestStreetAccess: an empty town got streets");
                    ok = false;
                }
            }

            // 7. TWO DISCONNECTED STREET STUBS, each already serving its own building. Every other case's
            //    frontage carve happens to land ON the existing network (CarveToNetwork's BFS target IS the
            //    current street set), so half 1 alone never leaves the network in two pieces — this is the
            //    one fixture where NEITHER building needs a new carve and only half 2 (one component) forces
            //    a repair, which is exactly what an "orphan components never joined" mutant would miss.
            //    It is ALSO the fixture that caught a real accounting bug on review: CarveBetween's BFS
            //    starts from the orphan's OWN cells, which are already street, and its path always walks
            //    back through — and includes — its own start, so an unguarded append reported the
            //    pre-existing cell (1,0) as if newly added (measured before the fix: EnsureAccess returned
            //    9 where the stored array only grew by 8). The checks below pin MissingAccess's contract
            //    directly: no duplicate within its own returned list, no already-street cell counted as
            //    new, and the reported count matching the OBSERVED growth of the stored array — not just
            //    self-consistency between MissingAccess and EnsureAccess, which would stay equal even if
            //    both over-counted the same way.
            //
            //    A THIRD BUILDING, (5,10), is folded into this SAME fixture for the row-major assertion
            //    below, and it has to be a third one rather than reusing (1,1)/(10,1): those two already
            //    front their own stub, so pass 1 never carves for them, and their pass-2 join always runs
            //    from the row-major-SMALLER orphan toward the larger one — an inherently ASCENDING path, so
            //    `added` comes out already sorted with or without MissingAccess's own final `Sort`, and an
            //    assertion added only against that geometry would be vacuous (verified: with the final Sort
            //    temporarily removed, this exact two-stub fixture still passed a row-major check). (5,10)
            //    does not front the pre-existing stub at (5,3), so it forces an actual pass-1 carve, and
            //    that carve runs NORTH-TO-SOUTH-REVERSED — from the building's high-j frontage down to the
            //    low-j street — i.e. DESCENDING j, landing in `added` BEFORE pass 2's ascending j=0 cells in
            //    insertion order. Only the final `Sort` fixes that ordering; confirmed empirically: with
            //    `added.Sort(RowMajor)` temporarily removed, this fixture's `missing` list failed the
            //    row-major check below 10 times over, e.g. "MissingAccess returned (5, 9) before (5, 8) …
            //    which is not row-major order" — and with the Sort restored the same fixture passes clean.
            {
                var floor = Floor(Cells((1, 0), (10, 0), (5, 3)), (1, 1), (10, 1), (5, 10));
                var before = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));

                var missing = SettlementStreetOps.MissingAccess(floor);
                var seenInMissing = new System.Collections.Generic.HashSet<(int i, int j)>();
                (int i, int j)? prevMissing = null;
                foreach (var c in missing)
                {
                    if (!seenInMissing.Add(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: MissingAccess returned duplicate cell {c} "
                                     + "for the two-stub-plus-descending-carve fixture");
                        ok = false;
                    }
                    if (before.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: MissingAccess counted already-street cell "
                                     + $"{c} as newly added for the two-stub-plus-descending-carve fixture");
                        ok = false;
                    }
                    // ROW-MAJOR, pinned directly: the doc promises it and nothing previously asserted it.
                    // Row-major here means j primary, i secondary — the same key RowMajor uses internally.
                    if (prevMissing != null)
                    {
                        var p = prevMissing.Value;
                        bool inOrder = p.j < c.j || (p.j == c.j && p.i <= c.i);
                        if (!inOrder)
                        {
                            Debug.LogError($"SelfTestStreetAccess: MissingAccess returned {p} before {c} for "
                                         + "the two-stub-plus-descending-carve fixture, which is not row-major order");
                            ok = false;
                        }
                    }
                    prevMissing = c;
                }

                int added = SettlementStreetOps.EnsureAccess(floor);
                if (added <= 0)
                {
                    Debug.LogError($"SelfTestStreetAccess: two disconnected street stubs plus a descending "
                                 + $"carve got {added} cells — the orphan components were never joined");
                    ok = false;
                }
                var after = SettlementFootprint.Decode(floor.SettlementParams.StreetCells);
                int grew = after.Count - before.Count;
                if (added != grew)
                {
                    Debug.LogError($"SelfTestStreetAccess: EnsureAccess reported {added} cells added but "
                                 + $"the stored streets only grew by {grew} — an already-street cell was "
                                 + "double-counted");
                    ok = false;
                }
                if (!InvariantHolds(floor, out string why7))
                {
                    Debug.LogError($"SelfTestStreetAccess: after bridging the three components — {why7}");
                    ok = false;
                }
            }

            // 8. DEGENERATE: a building standing on its OWN street cell, and no other street exists — a
            //    shape the validator already treats as representable rather than impossible (it REPORTS a
            //    building coinciding with a street rather than forbidding it), and the exact shape Tasks
            //    3-5's load path can hand this repair from a legacy or hand-edited save. Building (5,5)'s
            //    only candidate target cell is (5,5) itself, buried under the building — so CarveToNetwork
            //    must come back empty-handed instead of searching forever.
            //
            //    THIS IS THE SHAPE THAT HUNG BEFORE THE FIX. Confirmed with a bounded-timeout standalone
            //    probe run against the unfixed Bfs (buildings.Contains(n) short-circuited before
            //    target.Contains(n) was ever checked, so the one and only target cell could never
            //    terminate the search, and with plain-int grid coordinates and no bounding box the
            //    frontier had nothing to stop it): EnsureAccess did not return within 10 seconds on this
            //    exact fixture. A synchronous self-test cannot assert "did not hang" directly — if the
            //    defect regresses, this method never reaches the assertions below and the whole harness
            //    run hangs with it, which is the detection mechanism a synchronous suite has for a true
            //    hang. What IS checkable, and what this pins for regression once the call DOES return, is
            //    that it reports nothing added and leaves the floor's stored streets byte-for-byte the
            //    same — "returns what it could solve and nothing else."
            {
                var floor = Floor(Cells((5, 5)), (5, 5));
                var beforeSet = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));

                int added8 = SettlementStreetOps.EnsureAccess(floor);
                if (added8 != 0)
                {
                    Debug.LogError($"SelfTestStreetAccess: building 1, buried under its own only street "
                                 + $"cell (5,5), got {added8} cells added instead of being left alone");
                    ok = false;
                }

                var afterSet = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
                foreach (var c in afterSet)
                    if (!beforeSet.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: the buried-street fixture's stored streets "
                                     + $"gained cell {c} when nothing should have been written");
                        ok = false;
                    }
                foreach (var c in beforeSet)
                    if (!afterSet.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: the buried-street fixture's stored streets "
                                     + $"lost cell {c} when nothing should have been written");
                        ok = false;
                    }
            }

            // 9. AN ENCLOSED STREET CELL — the SECOND way a target can be unreachable, and the one the
            //    buried-target filter above (case 8) does NOT close: a street cell ringed by buildings on
            //    its whole 8-neighbourhood has no building sitting ON it, so the filter keeps it as a live
            //    target, but 4-connected movement can never cross the diagonal ring to reach it. The 4
            //    ORTHOGONAL ring buildings front (5,5) directly and need no carve; the 4 CORNER ring
            //    buildings and the building well outside the ring do, and each of their searches goes
            //    looking for the one surviving target from the unbounded OUTSIDE region.
            //
            //    THIS IS THE SHAPE THE FINAL WHOLE-BRANCH REVIEW'S 10-SECOND WATCHDOG CAUGHT, and it HUNG
            //    before the bounded-frontier fix: confirmed by running this exact fixture (street (5,5)
            //    ringed by 8 buildings, plus a 9th building at (20,20)) against the pre-fix Bfs on a
            //    background thread with a 10-second join timeout — it did not return
            //    ("A enclosed-street ring: DID NOT RETURN within 10011 ms"). What is checkable once the
            //    call DOES return: the sealed cell truly cannot be reached from outside its own ring by
            //    4-connected movement, so CarveToNetwork correctly
            //    reports "boxed in" for every building that needs it and EnsureAccess adds nothing, leaving
            //    the stored streets byte-for-byte unchanged — the same "returns what it could solve and
            //    nothing else" contract as case 8, reached by the other route.
            {
                var floor = Floor(Cells((5, 5)),
                    (4, 4), (4, 5), (4, 6), (5, 4), (5, 6), (6, 4), (6, 5), (6, 6), (20, 20));
                var beforeSet9 = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));

                int added9 = SettlementStreetOps.EnsureAccess(floor);
                if (added9 != 0)
                {
                    Debug.LogError($"SelfTestStreetAccess: the sealed-ring fixture got {added9} cells added "
                                 + "— a street cell ringed by buildings on its whole 8-neighbourhood must be "
                                 + "unreachable from outside the ring");
                    ok = false;
                }

                var afterSet9 = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
                foreach (var c in afterSet9)
                    if (!beforeSet9.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: the sealed-ring fixture's stored streets "
                                     + $"gained cell {c} when nothing should have been written");
                        ok = false;
                    }
                foreach (var c in beforeSet9)
                    if (!afterSet9.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: the sealed-ring fixture's stored streets "
                                     + $"lost cell {c} when nothing should have been written");
                        ok = false;
                    }
            }

            if (ok) Debug.Log("Self-Test Street Access: PASS");
        }

        /// <summary>Trailing sentinel — see the arc's trailing-sentinel rule. Asserts nothing.</summary>
        [ContextMenu("Self-Test: Street Ops Sentinel")]
        public void SelfTestStreetOpsSentinel()
        {
            Debug.Log("Street Ops Sentinel: no-op terminator (asserts nothing, not a test result)");
        }
    }
}
