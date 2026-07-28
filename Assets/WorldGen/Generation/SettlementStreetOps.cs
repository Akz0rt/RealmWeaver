using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>The rule that keeps a town walkable after the DM edits it (findings ·4 and ·9):
    ///
    ///   1. every building has at least one cell 4-adjacent to a street cell, AND
    ///   2. the street network is a single 4-connected component.
    ///
    /// HALF 2 IS NOT PEDANTRY. Without it a house can be served by an isolated lane — formally "has access",
    /// in fact cut off. It is also what closes ·9 without a line of wall code: streets are part of
    /// SettlementTileGrid.BuildWallRing's dilation seed, so stitching an outlying house's lane onto the
    /// network makes the wall wrap the lane and the house by itself.
    ///
    /// PURE AND UNITY-FREE, deliberately. The offline harness compiles Generation and not Rendering, so
    /// everything here is testable and mutable, unlike the four places that call it.</summary>
    public static class SettlementStreetOps
    {
        static readonly int[] DI = { -1, 1, 0, 0 };
        static readonly int[] DJ = { 0, 0, -1, 1 };

        /// <summary>The street cells that would have to be ADDED for the invariant to hold. Writes nothing.
        /// The returned list is DUPLICATE-FREE and sorted row-major — later callers (Task 4's drag preview,
        /// any future undo-diff or DM-facing count) are entitled to rely on both.
        ///
        /// VIOLATIONS ARE RE-DERIVED AFTER EACH CARVE, not collected once up front: carving a road for one
        /// building can also connect an orphan component, so a batch computed in advance would over-carve
        /// and EnsureAccess would stop being idempotent. At a few hundred cells the repeated scan costs
        /// nothing (measured: 0.002 ms per search on a large town).</summary>
        public static List<(int i, int j)> MissingAccess(InteriorFloor floor)
        {
            var added = new List<(int i, int j)>();
            if (floor == null || floor.SettlementParams == null) return added;

            var buildings = new HashSet<(int i, int j)>();
            var perRoom = new List<List<(int i, int j)>>();
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 1) continue;
                var fp = SettlementTileGrid.FootprintOf(r);
                if (fp.Count == 0) continue;
                perRoom.Add(fp);
                foreach (var c in fp) buildings.Add(c);
            }
            if (perRoom.Count == 0) return added;          // nothing to serve
            perRoom.Sort((x, y) => RowMajor(SettlementFootprint.Representative(x),
                                            SettlementFootprint.Representative(y)));

            var streets = new HashSet<(int i, int j)>(SettlementFootprint.Decode(floor.SettlementParams.StreetCells));

            // BOOTSTRAP. A town with no streets at all is the COMMON legacy shape (a v9 save carries none),
            // not a degenerate one, so it is handled explicitly rather than falling out of the loop below:
            // the row-major-first building donates one street-frontage cell, and that cell IS the network.
            if (streets.Count == 0)
            {
                var seedCell = FirstFreeNeighbour(perRoom[0], buildings);
                if (seedCell == null) return added;        // fully boxed in — nothing safe to do
                if (streets.Add(seedCell.Value)) added.Add(seedCell.Value);
            }

            // 1. every building fronts a street
            for (int pass = 0; pass < perRoom.Count; pass++)
            {
                var fp = perRoom[pass];
                if (Fronts(fp, streets)) continue;
                var path = CarveToNetwork(fp, buildings, streets);
                if (path == null) continue;                // boxed in; leave the floor as it is
                // Gated on streets.Add's OWN return value, not a second bookkeeping set: "already in
                // streets" is the exact, single source of truth for "not actually new," and streets is
                // already the HashSet paying for that check. Provably a no-op HERE (CarveToNetwork's own
                // starts are the footprint's non-street neighbours — see Fronts above, which is why the
                // carve ran at all — so nothing this BFS returns can already be in streets); kept for the
                // same reason the guard below is NOT optional.
                foreach (var c in path) if (streets.Add(c)) added.Add(c);
            }

            // 2. one component. Re-derived each time for the same reason as above.
            while (true)
            {
                var orphan = SmallestOrphanComponent(streets);
                if (orphan == null) break;
                var path = CarveBetween(orphan, streets, buildings);
                // path.Count == 0 ("already touching") is unreachable today — orphan and target are
                // disjoint by construction in CarveBetween — but stopping on it too is free insurance
                // against a future refactor turning this loop into a spin if that ever stops being true.
                if (path == null || path.Count == 0) break; // cannot be joined; stop rather than loop
                // NOT a no-op here, unlike pass 1's identical-looking guard above: CarveBetween's own BFS
                // starts ARE the orphan's cells, which are by construction already members of `streets`
                // (an orphan is a component OF the street set), and Bfs's path reconstruction always walks
                // back to — and includes — the start it began from (it has no `prev` entry to stop the
                // walk any earlier). Every successful orphan-join therefore touches one already-street
                // cell, and without this guard it would silently ride along as if newly added — exactly
                // the discrepancy an empirical check caught (EnsureAccess returning 9 where only 8 cells
                // were actually new).
                foreach (var c in path) if (streets.Add(c)) added.Add(c);
            }

            added.Sort(RowMajor);
            return added;
        }

        /// <summary>Commit: adds MissingAccess's cells to the floor's stored streets, kept sorted row-major
        /// and duplicate-free. Returns how many were added — 0 on a floor that already satisfies the
        /// invariant, which is what makes a second call a no-op.</summary>
        public static int EnsureAccess(InteriorFloor floor)
        {
            var add = MissingAccess(floor);
            if (add.Count == 0) return 0;
            var all = new List<(int i, int j)>(SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
            var seen = new HashSet<(int i, int j)>(all);
            foreach (var c in add) if (seen.Add(c)) all.Add(c);
            all.Sort(RowMajor);
            floor.SettlementParams.StreetCells = SettlementFootprint.Encode(all);
            return add.Count;
        }

        // ---- helpers ---------------------------------------------------------------------------------

        static int RowMajor((int i, int j) a, (int i, int j) b)
            => a.j != b.j ? a.j.CompareTo(b.j) : a.i.CompareTo(b.i);

        static bool Fronts(List<(int i, int j)> fp, HashSet<(int i, int j)> streets)
        {
            foreach (var c in fp)
                for (int k = 0; k < 4; k++)
                    if (streets.Contains((c.i + DI[k], c.j + DJ[k]))) return true;
            return false;
        }

        /// <summary>The row-major smallest cell 4-adjacent to this footprint that no building occupies.</summary>
        static (int i, int j)? FirstFreeNeighbour(List<(int i, int j)> fp, HashSet<(int i, int j)> buildings)
        {
            (int i, int j)? best = null;
            foreach (var c in fp)
                for (int k = 0; k < 4; k++)
                {
                    var n = (i: c.i + DI[k], j: c.j + DJ[k]);
                    if (buildings.Contains(n)) continue;
                    if (best == null || RowMajor(n, best.Value) < 0) best = n;
                }
            return best;
        }

        /// <summary>Shortest 4-connected path of NON-building cells from this footprint's frontage to the
        /// nearest street cell, excluding both endpoints — so the returned cells are exactly what must
        /// become street. Null when no route exists.
        ///
        /// TIE-BREAK: BFS expands neighbours in a fixed order and never revisits, so the first time a street
        /// is reached the path is both shortest and the row-major-earliest of the shortest — deterministic
        /// without a second pass.</summary>
        static List<(int i, int j)> CarveToNetwork(List<(int i, int j)> fp, HashSet<(int i, int j)> buildings,
                                                   HashSet<(int i, int j)> streets)
        {
            var starts = new List<(int i, int j)>();
            foreach (var c in fp)
                for (int k = 0; k < 4; k++)
                {
                    var n = (i: c.i + DI[k], j: c.j + DJ[k]);
                    if (!buildings.Contains(n)) starts.Add(n);
                }
            starts.Sort(RowMajor);
            return Bfs(starts, buildings, streets);
        }

        static List<(int i, int j)> CarveBetween(List<(int i, int j)> orphan, HashSet<(int i, int j)> streets,
                                                 HashSet<(int i, int j)> buildings)
        {
            var target = new HashSet<(int i, int j)>(streets);
            foreach (var c in orphan) target.Remove(c);
            var starts = new List<(int i, int j)>(orphan);
            starts.Sort(RowMajor);
            return Bfs(starts, buildings, target);
        }

        static List<(int i, int j)> Bfs(List<(int i, int j)> starts, HashSet<(int i, int j)> buildings,
                                        HashSet<(int i, int j)> target)
        {
            // EFFECTIVE target: drop any member also claimed by a building. Below, `buildings.Contains(n)`
            // is checked BEFORE `target.Contains(n)` on every candidate cell — that ordering exists so a
            // building is never mistaken for open ground, but its side effect is that a target cell buried
            // under a building can never terminate the search: it is skipped every time a neighbour tries
            // to reach it, never marked seen, never returned. Grid coordinates are plain ints with no
            // bounding box, so a target that is non-empty but ENTIRELY buried does not fail fast — it
            // expands outward forever (measured: an unfixed EnsureAccess call did not return in 10+
            // seconds on a one-cell fixture). Filtering ONCE, here, beats reordering the two checks below:
            // reordering would still let the search wander indefinitely past every OTHER buried target
            // before happening to find a reachable one, where filtering rules buried cells out up front.
            // A FRESH set, not a mutation of the caller's `target`: CarveToNetwork passes its live
            // `streets` set directly (not a copy), so writing to `target` in place would corrupt it.
            var effectiveTarget = new HashSet<(int i, int j)>();
            foreach (var t in target) if (!buildings.Contains(t)) effectiveTarget.Add(t);
            target = effectiveTarget;

            if (target.Count == 0) return null;
            var prev = new Dictionary<(int i, int j), (int i, int j)>();
            var seen = new HashSet<(int i, int j)>();
            var q = new List<(int i, int j)>();
            foreach (var s in starts)
            {
                if (buildings.Contains(s) || !seen.Add(s)) continue;
                if (target.Contains(s)) return new List<(int i, int j)>();   // already touching
                q.Add(s);
            }
            for (int head = 0; head < q.Count; head++)
            {
                var cur = q[head];
                for (int k = 0; k < 4; k++)
                {
                    var n = (i: cur.i + DI[k], j: cur.j + DJ[k]);
                    if (buildings.Contains(n) || !seen.Add(n)) continue;
                    prev[n] = cur;
                    if (target.Contains(n))
                    {
                        var path = new List<(int i, int j)>();
                        var walk = cur;
                        while (true)
                        {
                            path.Add(walk);
                            if (!prev.TryGetValue(walk, out var p)) break;
                            walk = p;
                        }
                        path.Reverse();
                        return path;
                    }
                    q.Add(n);
                }
            }
            return null;
        }

        /// <summary>The SMALLEST 4-connected component of the street set when there is more than one; null
        /// when the network is already a single piece. Smallest-first because it is the cheapest join to find
        /// and because merging pairwise terminates in at most (components - 1) carves; ties break on the
        /// component's own row-major minimum, so the repair order never depends on set enumeration.
        ///
        /// This is a PAIRWISE MERGE, not "everything joins the biggest": with three components the second
        /// merge may be between two that were both orphans a moment ago. The caller re-derives components
        /// after every carve, so that resolves itself.</summary>
        static List<(int i, int j)> SmallestOrphanComponent(HashSet<(int i, int j)> streets)
        {
            var ordered = new List<(int i, int j)>(streets);
            ordered.Sort(RowMajor);
            var seen = new HashSet<(int i, int j)>();
            var comps = new List<List<(int i, int j)>>();
            foreach (var s in ordered)
            {
                if (!seen.Add(s)) continue;
                var comp = new List<(int i, int j)> { s };
                var stack = new List<(int i, int j)> { s };
                while (stack.Count > 0)
                {
                    var cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    for (int k = 0; k < 4; k++)
                    {
                        var n = (i: cur.i + DI[k], j: cur.j + DJ[k]);
                        if (streets.Contains(n) && seen.Add(n)) { comp.Add(n); stack.Add(n); }
                    }
                }
                comp.Sort(RowMajor);
                comps.Add(comp);
            }
            if (comps.Count <= 1) return null;
            var best = comps[0];
            foreach (var c in comps)
                if (c.Count < best.Count || (c.Count == best.Count && RowMajor(c[0], best[0]) < 0)) best = c;
            return best;
        }
    }
}
