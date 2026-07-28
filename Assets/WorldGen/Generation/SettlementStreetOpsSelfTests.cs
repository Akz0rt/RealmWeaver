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
            //    degenerate one. The bootstrap must invent a network and connect everything to it.
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
            }

            // 5. DETERMINISM. The same floor twice must yield the same cells, in the same order.
            {
                var a = Floor(Cells((1, 0)), (1, 1), (7, 7));
                var b = Floor(Cells((1, 0)), (1, 1), (7, 7));
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
            {
                var floor = Floor(Cells((1, 0), (10, 0)), (1, 1), (10, 1));
                var before = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));

                var missing = SettlementStreetOps.MissingAccess(floor);
                var seenInMissing = new System.Collections.Generic.HashSet<(int i, int j)>();
                foreach (var c in missing)
                {
                    if (!seenInMissing.Add(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: MissingAccess returned duplicate cell {c} "
                                     + "for the two-stub fixture");
                        ok = false;
                    }
                    if (before.Contains(c))
                    {
                        Debug.LogError($"SelfTestStreetAccess: MissingAccess counted already-street cell "
                                     + $"{c} as newly added for the two-stub fixture");
                        ok = false;
                    }
                }

                int added = SettlementStreetOps.EnsureAccess(floor);
                if (added <= 0)
                {
                    Debug.LogError($"SelfTestStreetAccess: two disconnected street stubs got {added} cells — "
                                 + "the orphan components were never joined");
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
                    Debug.LogError($"SelfTestStreetAccess: after bridging two street stubs — {why7}");
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
