using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>What one pass of block generation produced, all on <see cref="SettlementFootprint"/>'s FIXED
    /// absolute lattice (cell (0,0) spans normalized [0,Pitch)) — the same lattice Room.Cells,
    /// SettlementParams.StreetCells and SettlementTileGrid already speak.
    ///
    /// STREETS include the ring; GATES are a SUBSET of the streets (a gate is the ring-street cell a
    /// subdivision street runs out into), which is what makes "every street cell is reachable from a gate
    /// through street cells only" a statement about this one list rather than two unrelated ones.</summary>
    public sealed class BlockLayout
    {
        /// <summary>Every street cell, ring included. Subdivision strips are exactly one cell wide; the ring
        /// is 4-connected but only MOSTLY one cell wide — see <see cref="SettlementBlocks.RingStreet"/>.</summary>
        public List<(int i, int j)> StreetCells = new List<(int i, int j)>();
        /// <summary>One footprint per building — non-empty, 4-connected, pairwise disjoint, and each one
        /// 4-adjacent to at least one street cell (nothing is walled in).</summary>
        public List<List<(int i, int j)>> Buildings = new List<List<(int i, int j)>>();
        /// <summary>The ring-street cells that read as gates. Always a subset of StreetCells.</summary>
        public List<(int i, int j)> GateCells = new List<(int i, int j)>();
    }

    /// <summary>BLOCK GENERATION: streets carve a walled interior into blocks, and each block is filled with
    /// flush buildings. Pure, UnityEngine-free, deterministic in `seed` — the same discipline every other
    /// stage of this generator already keeps. References only WallContour / SettlementFootprint / System.*.
    ///
    /// FIVE PASSES, IN THIS ORDER, each separately testable:
    ///   1. <see cref="InteriorCells"/> — the lattice cells whose CENTRE lies inside the contour, reduced to
    ///      the single largest 4-connected component (a jittered contour can in principle pinch off a stray).
    ///   2. <see cref="RingStreet"/> — the 4-connected ring just inside the contour: every interior cell with
    ///      a non-interior 4-neighbour, reconnected to one lap. This is the street every outermost block
    ///      fronts onto, and it is what keeps a building from ever standing flush against the wall.
    ///   3. <see cref="Subdivide"/> — recursive axis-aligned subdivision of what is left (the CORE) by
    ///      one-cell street strips, until a block is at or below <see cref="BlockTargetCells"/>.
    ///   4. <see cref="PlaceGates"/> — a gate where a subdivision street runs out into the ring.
    ///   5. <see cref="FillBlock"/> — each block filled with disjoint, flush footprints of varied size.
    ///
    /// SUBDIVISION STREETS ARE ONE CELL WIDE, ALWAYS. THE RING IS NOT — it is 4-connected, and only MOSTLY
    /// one cell wide: measured over 540 towns (task-A3-report.md), 25.9% of ring cells are genuinely TWO
    /// cells deep (all four orthogonal neighbours interior), stable at 21-27% across every town size from a
    /// 5-building hamlet to an 80-building city. This is not slack to tighten: 4-connectivity and strict
    /// one-cell width are incompatible on a digitized disk (see <see cref="RingStreet"/>'s own doc for why).
    /// A wider main axis is deliberately NOT in this arc: it would add a street-class concept nothing else
    /// models yet, and it would arrive as a hidden default rather than a choice the DM made.
    ///
    /// `targetBuildings` IS ADVISORY. It steers how big the buildings come out (see
    /// <see cref="SizeClassFor"/>), never how many are emitted: the achieved count is whatever the geometry
    /// yields. There is deliberately no exact-count contract — the previous attempt at one is what forced a
    /// building cap that then had to be reverted.</summary>
    public static class SettlementBlocks
    {
        /// <summary>A block stops subdividing at or below this many cells. 9 is a 3x3 city block: big enough
        /// to read as a block of row-houses rather than a lone house, small enough that almost every cell in
        /// it fronts a street (only a 3x3's own centre does not, and a two-cell building routinely swallows
        /// that). Measured (task-A3-report.md) against 12/16/20: the achieved building count is FLAT across
        /// that whole range (average ratio 0.466 / 0.484 / 0.494 / 0.490 at target 40) because the two costs
        /// trade off — smaller blocks spend more of the interior on street strips, larger ones leave more
        /// cells with no street frontage at all. 9 is chosen on STRUCTURE, not yield: it is the largest value
        /// that still cuts a 20-building town at all (its core is ~12 cells), and a town with no internal
        /// street is the schematic donut this arc exists to replace.</summary>
        public const int BlockTargetCells = 9;

        /// <summary>Largest size class the fill will roll (see SizeClassFor). Capped so a very small town —
        /// where the cell budget per requested building is huge — still gets recognizable houses instead of
        /// one compound swallowing a whole block.</summary>
        public const int MaxSizeClass = 4;

        /// <summary>The deepest subdivision level whose street strips open a GATE. 0 = the FIRST cut of the
        /// core only — the town's main axis, and nothing below it. Not a cosmetic cap on a count: a gate is a
        /// break in the town's defences, so the wall opens for the street a traveller would actually arrive
        /// on, not for every alley that happens to dead-end at the wall. Measured (task-A3-report.md) over a
        /// 9-target x 60-seed sweep: gating on EVERY subdivision street opened up to 17 gates on a large
        /// town, depth &lt;= 1 up to 9, and depth 0 lands every town in 2..6 — the range
        /// SettlementGenerator.GateCountFor (2..4) has always described, widened only where a ragged strip
        /// end abuts more than one ring cell.</summary>
        public const int GateCutDepth = 0;

        public static BlockLayout Generate(WallContour wall, int seed, int targetBuildings)
        {
            var layout = new BlockLayout();

            // ---- 1. interior --------------------------------------------------------------------------
            var interior = InteriorCells(wall);
            if (interior.Count == 0) return layout;                     // degenerate contour → empty layout
            var interiorSet = new HashSet<(int i, int j)>(interior);

            // ---- 2. the ring street just inside the contour (4-connected, mostly one cell wide) -------
            var ring = RingStreet(interior, interiorSet);
            var ringSet = new HashSet<(int i, int j)>(ring);

            var core = new List<(int i, int j)>();
            foreach (var c in interior) if (!ringSet.Contains(c)) core.Add(c);

            // ---- 3. recursive axis-aligned subdivision of the core ------------------------------------
            // Its own Random, seeded from `seed` alone: the fill below takes a SEPARATE one, so a change to
            // how many rolls the fill makes can never shift the subdivision (and vice versa).
            var subStreets = new List<(int i, int j)>();
            var primaryStreets = new List<(int i, int j)>();
            var blocks = new List<List<(int i, int j)>>();
            Subdivide(core, new System.Random(seed * 31 + 5), subStreets, primaryStreets, blocks);

            // ---- 4. gates where a PRIMARY subdivision street reaches the ring -------------------------
            layout.GateCells.AddRange(PlaceGates(ring, primaryStreets, ringSet));

            // Streets = ring ∪ subdivision strips, in one row-major order. Sorted, not concatenated: this
            // list is SERIALIZED (SettlementParams.StreetCells), so a stable order keeps a re-generated town
            // byte-comparable with itself.
            layout.StreetCells.AddRange(ring);
            layout.StreetCells.AddRange(subStreets);
            layout.StreetCells.Sort(RowMajor);
            var streetSet = new HashSet<(int i, int j)>(layout.StreetCells);

            // ---- 5. fill each block ------------------------------------------------------------------
            int blockCells = 0;
            foreach (var b in blocks) blockCells += b.Count;
            int sizeClass = SizeClassFor(blockCells, targetBuildings);

            blocks.Sort(ByLowestCell);            // deterministic block order → deterministic rng consumption
            var fillRng = new System.Random(seed * 977 + 41);
            foreach (var b in blocks)
                FillBlock(b, streetSet, fillRng, sizeClass, layout.Buildings);

            return layout;
        }

        // ---- pass 1: interior ------------------------------------------------------------------------

        /// <summary>The lattice cells whose CENTRE lies inside the contour, reduced to the single largest
        /// 4-connected component. CENTRE, not any corner: a cell is the unit this whole arc places, so a cell
        /// that is only half inside is not a place a house can stand. The largest-component reduction is
        /// cheap insurance — a jittered contour is convex in practice and yields one component, but a stray
        /// cell pinched off by a concave wobble would otherwise become its own "block" with its own ring
        /// street and its own gate, i.e. a one-house village outside the town.</summary>
        public static List<(int i, int j)> InteriorCells(WallContour wall)
        {
            var inside = new List<(int i, int j)>();
            if (wall == null || !wall.IsClosedSane()) return inside;

            float minX = wall.Points[0].X, minY = wall.Points[0].Y, maxX = minX, maxY = minY;
            foreach (var p in wall.Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            int i0 = SettlementFootprint.CellOf(minX), i1 = SettlementFootprint.CellOf(maxX);
            int j0 = SettlementFootprint.CellOf(minY), j1 = SettlementFootprint.CellOf(maxY);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (wall.Contains(SettlementFootprint.CenterOf(i), SettlementFootprint.CenterOf(j)))
                        inside.Add((i, j));

            var comps = Components(inside);
            if (comps.Count == 0) return inside;
            int best = 0;
            for (int k = 1; k < comps.Count; k++)
                if (comps[k].Count > comps[best].Count) best = k;    // strict > → ties keep the first found
            return comps[best];
        }

        // ---- pass 2: the ring street -----------------------------------------------------------------

        /// <summary>The ring just inside the contour — every interior cell with a 4-neighbour that is NOT
        /// interior — RECONNECTED so it is a single 4-connected lap of the town.
        ///
        /// THE RECONNECT IS NOT OPTIONAL. The raw rule above is exactly "the cells touching the wall", and it
        /// FRAGMENTS at the poles of any rounded contour: where the shape tapers by more than one cell
        /// between adjacent rows, the narrow row's cells have no 4-neighbour in the wider row that is itself
        /// a ring cell, so the cap breaks off as its own component and a lap of the town stops being
        /// drivable. Measured, not theorised: with the raw rule alone EVERY town in a 7-target x 20-seed
        /// sweep came out fragmented (seed 13 / target 40 reached only 29 of its 40 street cells from a
        /// gate).
        ///
        /// WHAT RECONNECT ACTUALLY PRODUCES, and this corrects an earlier draft of this doc: it is NOT a thin
        /// one-cell repair sitting alongside a thicker eight-neighbour alternative. Measured over 540 towns
        /// (task-A3-report.md) the reconnected ring's cell set is EXACTLY the eight-neighbour boundary — every
        /// interior cell with an 8-neighbour that is not interior — zero mismatches, every single town. That
        /// is not a defect to fix: 4-connectivity and strict one-cell width are provably incompatible on a
        /// digitized disk, because the raw 4-boundary (the only ring that IS always one cell wide) fragments
        /// on 538 of those same 540 towns. So the ring this method returns is 4-connected but only MOSTLY one
        /// cell wide — genuinely TWO cells deep wherever the contour curves (25.9% of ring cells overall,
        /// stable at 21-27% across every town size, from a 5-building hamlet to an 80-building city).
        /// 4-connectivity was chosen over strict width because the alternative — a ring that fragments on
        /// 538/540 towns, i.e. almost always — is the far worse defect.</summary>
        public static List<(int i, int j)> RingStreet(IReadOnlyList<(int i, int j)> interior,
                                                     HashSet<(int i, int j)> interiorSet)
        {
            var ring = new List<(int i, int j)>();
            if (interior == null || interiorSet == null) return ring;

            var ringSet = new HashSet<(int i, int j)>();
            foreach (var c in interior)
                if (!interiorSet.Contains((c.i - 1, c.j)) || !interiorSet.Contains((c.i + 1, c.j)) ||
                    !interiorSet.Contains((c.i, c.j - 1)) || !interiorSet.Contains((c.i, c.j + 1)))
                { ring.Add(c); ringSet.Add(c); }

            Reconnect(ring, ringSet, interiorSet);
            ring.Sort(RowMajor);
            return ring;
        }

        static readonly int[] DiagI = { 1, 1, -1, -1 };
        static readonly int[] DiagJ = { 1, -1, 1, -1 };

        /// <summary>Turn the ring's 8-connectivity into 4-connectivity, in place. The 4-boundary of a
        /// 4-connected interior is 8-connected (the digital-topology dual), so the ONLY way two ring cells
        /// can be neighbours-but-unreachable is a DIAGONAL pair with neither shared orthogonal cell in the
        /// ring; promoting one of those two shared cells — whichever is interior, the row-major-lower when
        /// both are — turns that diagonal step into a 4-path. Repeat to a fixed point, since a promoted cell
        /// can itself form a new diagonal pair.
        ///
        /// A diagonal pair whose BOTH shared cells lie outside the interior is a pinch in the interior
        /// itself and is deliberately left alone: there is no interior cell to promote, and the boundary is a
        /// closed loop, so the two sides still meet the long way round.</summary>
        static void Reconnect(List<(int i, int j)> ring, HashSet<(int i, int j)> ringSet,
                              HashSet<(int i, int j)> interiorSet)
        {
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 64)
            {
                changed = false;
                int n = ring.Count;
                for (int k = 0; k < n; k++)
                {
                    var p = ring[k];
                    for (int d = 0; d < 4; d++)
                    {
                        var q = (p.i + DiagI[d], p.j + DiagJ[d]);
                        if (!ringSet.Contains(q)) continue;
                        var a = (p.i + DiagI[d], p.j);
                        var b = (p.i, p.j + DiagJ[d]);
                        if (ringSet.Contains(a) || ringSet.Contains(b)) continue;   // already 4-linked
                        bool ia = interiorSet.Contains(a), ib = interiorSet.Contains(b);
                        if (!ia && !ib) continue;                                    // interior pinch — see doc
                        var pick = (ia && ib) ? (RowMajor(a, b) <= 0 ? a : b) : (ia ? a : b);
                        if (ringSet.Add(pick)) { ring.Add(pick); changed = true; }
                    }
                }
            }
        }

        // ---- pass 3: subdivision ---------------------------------------------------------------------

        /// <summary>Carve the core into blocks with one-cell street strips, breadth-first so the widest cuts
        /// land first (which is also what puts the gates on the town's main axes). A block at or below
        /// BlockTargetCells is done; one too thin to cut on EITHER axis is done as well, whatever its cell
        /// count, since a cut needs a whole cell left on both sides of the strip.
        ///
        /// TERMINATION: a cut index is always strictly between the block's own bbox lo and hi on that axis,
        /// and the block by definition has a cell at lo and a cell at hi — so both sides come out non-empty
        /// and every recursion step is on a STRICTLY smaller cell set. The `guard` counter is belt-and-braces
        /// against a future edit breaking that argument, not a live bound.</summary>
        public static void Subdivide(List<(int i, int j)> core, System.Random rng,
                                     List<(int i, int j)> streets, List<(int i, int j)> primaryStreets,
                                     List<List<(int i, int j)>> blocks)
        {
            if (core == null || core.Count == 0) return;

            var pending = new List<(List<(int i, int j)> cells, int depth)>();
            foreach (var comp in Components(core)) pending.Add((comp, 0));

            int guard = 0, head = 0;
            while (head < pending.Count && guard++ < 100000)
            {
                var (block, depth) = pending[head++];
                if (block.Count <= BlockTargetCells) { blocks.Add(block); continue; }

                var (minI, minJ, maxI, maxJ) = SettlementFootprint.Bounds(block);
                // Cut the LONGER axis so blocks stay squarish rather than degenerating into slivers; a tie
                // cuts a COLUMN (vertical strip) purely so the choice is fixed and not rng-dependent.
                bool vertical = (maxI - minI) >= (maxJ - minJ);
                // A PRIMARY street is one cut at depth <= GateCutDepth — the town's main axes, and the only
                // ones that open a gate (see PlaceGates). Every cut, primary or not, still goes into
                // `streets`: they are all one-cell streets and all carry traffic; the distinction is purely
                // about where the wall opens.
                var into = depth <= GateCutDepth ? primaryStreets : null;
                if (!TryCut(block, vertical, depth, rng, streets, into, pending) &&
                    !TryCut(block, !vertical, depth, rng, streets, into, pending))
                    blocks.Add(block);                                // too thin to cut on either axis
            }
            // Anything the guard cut short is still a block, never silently dropped.
            for (; head < pending.Count; head++) blocks.Add(pending[head].cells);
        }

        /// <summary>One axis-aligned cut. Returns false when the block's extent on that axis is under 3
        /// cells, i.e. there is no index that leaves a cell on both sides. The cut index is the bbox midpoint
        /// jittered by one cell either way (seeded), then clamped strictly inside — so two towns of the same
        /// shape still get different block rhythms.</summary>
        static bool TryCut(List<(int i, int j)> block, bool vertical, int depth, System.Random rng,
                           List<(int i, int j)> streets, List<(int i, int j)> primaryStreets,
                           List<(List<(int i, int j)> cells, int depth)> pending)
        {
            var (minI, minJ, maxI, maxJ) = SettlementFootprint.Bounds(block);
            int lo = vertical ? minI : minJ, hi = vertical ? maxI : maxJ;
            if (hi - lo < 2) return false;

            int cut = lo + (hi - lo) / 2 + rng.Next(-1, 2);
            if (cut <= lo) cut = lo + 1;
            if (cut >= hi) cut = hi - 1;

            var lower = new List<(int i, int j)>();
            var upper = new List<(int i, int j)>();
            foreach (var c in block)
            {
                int k = vertical ? c.i : c.j;
                if (k < cut) lower.Add(c);
                else if (k > cut) upper.Add(c);
                else { streets.Add(c); primaryStreets?.Add(c); }
            }
            // Components, not the raw halves: a ragged block can fall into separate pieces across the cut,
            // and each piece must get its own bbox (a shared one would put the next cut through open ground).
            foreach (var comp in Components(lower)) pending.Add((comp, depth + 1));
            foreach (var comp in Components(upper)) pending.Add((comp, depth + 1));
            return true;
        }

        // ---- pass 4: gates ---------------------------------------------------------------------------

        /// <summary>A gate is the RING cell a PRIMARY subdivision street (see GateCutDepth) runs out into —
        /// the town opens where its main streets already reach the wall, so a gate is never a hole in a
        /// random stretch of wall. Emitted in cut order (widest cuts first, since Subdivide is
        /// breadth-first) and de-duplicated, so one ring cell fed by two streets is one gate.
        ///
        /// FALLBACK, and it is a judgement call: a town too small to subdivide at all (a hamlet whose whole
        /// core is under BlockTargetCells) produces no subdivision street and therefore no gate by the rule
        /// above — yet a walled settlement with no way in is not a thing. Such a town gets a gate at the
        /// first ring cell in row-major order, plus a SECOND at the last ring cell — normally the two extreme
        /// ends of the ring, landing on opposite sides rather than side by side.
        ///
        /// THIS PROMISES TWO GATES BUT CAN DELIVER ONE: when the ring is small enough that its row-major
        /// first and last cell are the SAME cell, "first" and "last" collapse and only one gate comes out.
        /// Measured, not theorised: a WallContour radius of ~0.05 reproduces this. It is unreachable through
        /// SettlementGenerator.BuildFloor/Generate — WallRadiusFor floors at 0.16, safely above where the
        /// collapse starts — but this method is public and does not itself enforce that floor, so a caller
        /// building a WallContour directly can still hit it. Left as a known one-gate edge case rather than
        /// patched: fabricating a second gate on a ring this degenerate (small enough to have no distinct
        /// "opposite side" left) would need a rule invented for a shape production code never builds.</summary>
        public static List<(int i, int j)> PlaceGates(IReadOnlyList<(int i, int j)> ring,
                                                     IReadOnlyList<(int i, int j)> primaryStreets,
                                                     HashSet<(int i, int j)> ringSet)
        {
            var gates = new List<(int i, int j)>();
            if (ring == null || ring.Count == 0 || ringSet == null) return gates;

            var seen = new HashSet<(int i, int j)>();
            if (primaryStreets != null)
                foreach (var s in primaryStreets)
                {
                    TakeGate(ringSet, seen, gates, (s.i - 1, s.j));
                    TakeGate(ringSet, seen, gates, (s.i + 1, s.j));
                    TakeGate(ringSet, seen, gates, (s.i, s.j - 1));
                    TakeGate(ringSet, seen, gates, (s.i, s.j + 1));
                }
            if (gates.Count > 0) return gates;

            var first = ring[0]; var last = ring[0];
            foreach (var c in ring)
            {
                if (RowMajor(c, first) < 0) first = c;
                if (RowMajor(c, last) > 0) last = c;
            }
            gates.Add(first);
            if (last != first) gates.Add(last);
            return gates;
        }

        static void TakeGate(HashSet<(int i, int j)> ringSet, HashSet<(int i, int j)> seen,
                             List<(int i, int j)> gates, (int i, int j) c)
        {
            if (!ringSet.Contains(c) || !seen.Add(c)) return;
            gates.Add(c);
        }

        // ---- pass 5: filling a block -----------------------------------------------------------------

        /// <summary>How big the buildings in this town come out, 1 (single cells, with the odd pair) up to
        /// MaxSizeClass. THE ONE PLACE `targetBuildings` IS CONSULTED, and the only way it steers the result:
        /// the buildable cell budget divided by the requested count is roughly the average area a building
        /// may take if the count is to land near the request, so that ratio IS the size class. It cannot go
        /// below 1 — nothing can manufacture cells — which is exactly why the achieved count is allowed to
        /// come in under the request and why the count assertion is a band rather than an equality.</summary>
        public static int SizeClassFor(int blockCells, int targetBuildings)
        {
            if (targetBuildings <= 0) return 1;
            int k = (int)System.Math.Round(blockCells / (double)targetBuildings);
            if (k < 1) k = 1;
            if (k > MaxSizeClass) k = MaxSizeClass;
            return k;
        }

        /// <summary>Fill one block with disjoint, flush rectangular footprints, appending them to `into`.
        ///
        /// SEEDED ON FRONTAGE ONLY. A building starts at a block cell that already has a street 4-neighbour,
        /// so "every building fronts a street" holds BY CONSTRUCTION rather than by a filter afterwards — the
        /// footprint always contains its own seed cell. Cells with no street frontage are never seeded; they
        /// are reachable only by a neighbouring house growing into them, and whatever is left over stays open
        /// as the block's inner courtyard. That is the intended reading of a dense block, not a gap.
        ///
        /// RECTANGLES, GROWN. A rect is 4-connected and flush with its neighbours by construction, and
        /// growing one column/row at a time (each step taking the whole strip or none of it) keeps it a rect
        /// even when the block runs out under it. Row-major seed order makes the whole pass deterministic
        /// given the block's own cell order.</summary>
        public static void FillBlock(List<(int i, int j)> block, HashSet<(int i, int j)> streets,
                                     System.Random rng, int sizeClass, List<List<(int i, int j)>> into)
        {
            if (block == null || block.Count == 0 || into == null) return;
            var blockSet = new HashSet<(int i, int j)>(block);
            var claimed = new HashSet<(int i, int j)>();

            foreach (var seed in block)
            {
                if (!Available(seed, blockSet, claimed)) continue;
                if (!FrontsStreet(seed, streets)) continue;

                var (w, h) = PickSize(rng, sizeClass);
                int gw = 1, gh = 1;
                bool grew = true;
                while (grew && (gw < w || gh < h))
                {
                    grew = false;
                    if (gw < w && StripAvailable(seed, gw, 0, gw, gh, blockSet, claimed)) { gw++; grew = true; }
                    if (gh < h && StripAvailable(seed, 0, gh, gw, gh, blockSet, claimed)) { gh++; grew = true; }
                }

                var fp = new List<(int i, int j)>(gw * gh);
                for (int dj = 0; dj < gh; dj++)
                    for (int di = 0; di < gw; di++)
                    {
                        var c = (seed.i + di, seed.j + dj);
                        claimed.Add(c);
                        fp.Add(c);
                    }
                into.Add(fp);
            }
        }

        /// <summary>A cell this footprint may take: inside the block AND not already claimed by another
        /// building. The `!claimed` term is THE disjointness rule of the fill — every footprint cell is
        /// tested through here, both the seed and every grown strip, so there is exactly one line to break.</summary>
        static bool Available((int i, int j) c, HashSet<(int i, int j)> blockSet, HashSet<(int i, int j)> claimed)
            => blockSet.Contains(c) && !claimed.Contains(c);

        /// <summary>True when the WHOLE next strip is available: the column at offset (dx, 0..gh-1) when
        /// dx == gw, or the row at (0..gw-1, dy) when dy == gh. All-or-nothing is what keeps the footprint a
        /// rectangle instead of a clipped L.</summary>
        static bool StripAvailable((int i, int j) origin, int dx, int dy, int gw, int gh,
                                   HashSet<(int i, int j)> blockSet, HashSet<(int i, int j)> claimed)
        {
            if (dx > 0)
            {
                for (int k = 0; k < gh; k++)
                    if (!Available((origin.i + dx, origin.j + k), blockSet, claimed)) return false;
                return true;
            }
            for (int k = 0; k < gw; k++)
                if (!Available((origin.i + k, origin.j + dy), blockSet, claimed)) return false;
            return true;
        }

        static bool FrontsStreet((int i, int j) c, HashSet<(int i, int j)> streets)
            => streets != null && (streets.Contains((c.i - 1, c.j)) || streets.Contains((c.i + 1, c.j))
                                || streets.Contains((c.i, c.j - 1)) || streets.Contains((c.i, c.j + 1)));

        /// <summary>Roll one building's requested extent. ONE rng draw per building, always — so the roll
        /// sequence does not depend on how far the previous building managed to grow. The tables are weighted
        /// towards the class's own area but never uniform: a street of identical boxes is exactly the
        /// schematic look this arc replaces.</summary>
        static (int w, int h) PickSize(System.Random rng, int sizeClass)
        {
            int roll = rng.Next(8);
            switch (sizeClass)
            {
                case 1:  return roll < 6 ? (1, 1) : (roll < 7 ? (2, 1) : (1, 2));                     // avg ~1.25
                case 2:  return roll < 3 ? (2, 1) : (roll < 6 ? (1, 2) : (roll < 7 ? (2, 2) : (1, 1)));// avg ~2.0
                case 3:  return roll < 3 ? (2, 2) : (roll < 5 ? (3, 1) : (roll < 7 ? (1, 3) : (2, 1)));// avg ~2.75
                default: return roll < 3 ? (2, 2) : (roll < 5 ? (3, 2) : (roll < 7 ? (2, 3) : (3, 3)));// avg ~3.9
            }
        }

        // ---- shared helpers --------------------------------------------------------------------------

        /// <summary>The 4-connected components of a cell list, each row-major sorted, discovered in the
        /// input's own order — so the output is a pure function of the input list, with no hash-order
        /// dependence anywhere.</summary>
        public static List<List<(int i, int j)>> Components(List<(int i, int j)> cells)
        {
            var comps = new List<List<(int i, int j)>>();
            if (cells == null || cells.Count == 0) return comps;

            var all = new HashSet<(int i, int j)>(cells);
            var seen = new HashSet<(int i, int j)>();
            var stack = new List<(int i, int j)>();
            foreach (var start in cells)
            {
                if (!seen.Add(start)) continue;
                var comp = new List<(int i, int j)>();
                stack.Clear();
                stack.Add(start);
                while (stack.Count > 0)
                {
                    var c = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    comp.Add(c);
                    Reach(all, seen, stack, (c.i - 1, c.j));
                    Reach(all, seen, stack, (c.i + 1, c.j));
                    Reach(all, seen, stack, (c.i, c.j - 1));
                    Reach(all, seen, stack, (c.i, c.j + 1));
                }
                comp.Sort(RowMajor);
                comps.Add(comp);
            }
            return comps;
        }

        static void Reach(HashSet<(int i, int j)> all, HashSet<(int i, int j)> seen,
                          List<(int i, int j)> stack, (int i, int j) c)
        {
            if (!all.Contains(c) || !seen.Add(c)) return;
            stack.Add(c);
        }

        /// <summary>Row-major order: smaller j first, then smaller i — the SAME order
        /// SettlementFootprint.Representative calls "lowest", so a footprint's first cell is its
        /// representative.</summary>
        static int RowMajor((int i, int j) a, (int i, int j) b)
            => a.j != b.j ? a.j.CompareTo(b.j) : a.i.CompareTo(b.i);

        static int ByLowestCell(List<(int i, int j)> a, List<(int i, int j)> b)
            => RowMajor(SettlementFootprint.Representative(a), SettlementFootprint.Representative(b));
    }
}
