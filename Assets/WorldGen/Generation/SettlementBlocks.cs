using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>What one pass of block generation produced, all on <see cref="SettlementFootprint"/>'s FIXED
    /// absolute lattice (cell (0,0) spans normalized [0,Pitch)) — the same lattice Room.Cells,
    /// SettlementParams.StreetCells and SettlementTileGrid already speak.
    ///
    /// STREETS include the ring; GATES are a SUBSET of the streets (a gate is a ring-street cell), which is
    /// what makes "every street cell is reachable from a gate through street cells only" a statement about
    /// this one list rather than two unrelated ones.</summary>
    public sealed class BlockLayout
    {
        /// <summary>Every street cell: the ring, the arterials and the frontage strips. Arterials and
        /// frontage strips are exactly one cell wide; the ring is 4-connected but only MOSTLY one cell wide —
        /// see <see cref="SettlementBlocks.RingStreet"/>.</summary>
        public List<(int i, int j)> StreetCells = new List<(int i, int j)>();
        /// <summary>One footprint per building — non-empty, 4-connected, pairwise disjoint, and each one
        /// 4-adjacent to at least one street cell (nothing is walled in).</summary>
        public List<List<(int i, int j)>> Buildings = new List<List<(int i, int j)>>();
        /// <summary>The ring-street cells that read as gates. Always a subset of StreetCells.</summary>
        public List<(int i, int j)> GateCells = new List<(int i, int j)>();
    }

    /// <summary>BLOCK GENERATION: streets are laid where a house would otherwise have no frontage, and what
    /// survives between them is filled with flush buildings. Pure, UnityEngine-free, deterministic in `seed` —
    /// the same discipline every other stage of this generator already keeps. References only WallContour /
    /// SettlementFootprint / SettlementSizing / System.*.
    ///
    /// THE RULE THIS REPLACED, AND WHY. Until now the core was carved by RECURSIVE SUBDIVISION: cut the
    /// interior in half with a one-cell street strip, recurse, stop when a block was at or below a target cell
    /// count. That rule spends interior on streets whether or not anyone needs them — it cuts a block because
    /// the block is BIG, not because a house in it cannot reach a road — and it produces the uniform square
    /// blocks a real town does not have. The rule below is the opposite one: a street appears only where a
    /// house would otherwise have no frontage, so the street budget is driven by the demand for frontage and
    /// nothing else. Measured yield rises as a direct consequence (see task-C-report.md).
    ///
    /// SEVEN PASSES, IN THIS ORDER, each separately testable:
    ///   1. <see cref="InteriorCells"/> — the lattice cells whose CENTRE lies inside the contour, reduced to
    ///      the single largest 4-connected component (a jittered contour can in principle pinch off a stray).
    ///   2. <see cref="RingStreet"/> — the 4-connected ring just inside the contour: every interior cell with
    ///      a non-interior 4-neighbour, reconnected to one lap. This is the street every outermost block
    ///      fronts onto, and it is what keeps a building from ever standing flush against the wall. Whatever
    ///      the ring does not take is the CORE.
    ///   3. <see cref="PlaceGateCells"/> — the ring cells that read as gates, spread by ANGLE.
    ///   4. <see cref="Arterials"/> — from each gate, a Manhattan road inward to the town centre.
    ///   5. <see cref="FrontageFill"/> — greedy strips until no core cell lacks a 4-adjacent street.
    ///   6. <see cref="Blocks"/> — what is left after the streets, as 4-connected components.
    ///   7. <see cref="FillBlock"/> — each block filled with disjoint footprints drawn from a shape palette,
    ///      truncated to whatever fits.
    ///
    /// ARTERIAL AND FRONTAGE STREETS ARE ONE CELL WIDE, ALWAYS. THE RING IS NOT — it is 4-connected, and only
    /// MOSTLY one cell wide: measured over 540 towns (task-A3-report.md), 25.9% of ring cells are genuinely
    /// TWO cells deep (all four orthogonal neighbours interior), stable at 21-27% across every town size from
    /// a 5-building hamlet to an 80-building city. This is not slack to tighten: 4-connectivity and strict
    /// one-cell width are incompatible on a digitized disk (see <see cref="RingStreet"/>'s own doc for why).
    ///
    /// THE STREET NETWORK IS ONE PIECE BY CONSTRUCTION, not by a repair pass. The ring is one 4-connected lap;
    /// every gate is a ring cell; every arterial starts on a core cell 4-adjacent to its gate; and every
    /// frontage strip must touch the network already laid at one of its two ends. So there is no way for a
    /// street cell to exist that a gate cannot reach.
    ///
    /// `size` STEERS ONE THING NOW: how many gates the wall opens (SettlementSizing.GateCount). It never sets
    /// how BIG the buildings come out any more (that is the shape palette in <see cref="FillBlock"/>, which
    /// does not read `size` at all) nor how many buildings are emitted: the achieved count is whatever the
    /// geometry yields. There is deliberately no exact-count contract — the previous attempt at one is what
    /// forced a building cap that then had to be reverted. (Task 4 adds a further pass, after the fill loop,
    /// that restores a guaranteed minimum count by splitting the largest footprints.)</summary>
    public static class SettlementBlocks
    {
        /// <summary>No two gate cells may sit closer than this in CHEBYSHEV distance. 3 is the smallest value
        /// that leaves a whole cell of wall between two gates on the diagonal as well as on an axis, i.e. the
        /// smallest value at which two gates cannot read as one wide doorway. Stated in CELLS, not normalized
        /// units, so it keeps its meaning if the lattice pitch moves again.</summary>
        public const int MinGateSeparationCells = 3;

        static readonly int[] StepI = { -1, 1, 0, 0 };
        static readonly int[] StepJ = { 0, 0, -1, 1 };

        /// <summary>The order FrontageFill offers a strip's four directions in: +i, −i, +j, −j. It is part of
        /// the tie-break rule (see FrontageFill), so it is named here rather than written inline.</summary>
        static readonly int[] FillDirI = { 1, -1, 0, 0 };
        static readonly int[] FillDirJ = { 0, 0, 1, -1 };

        public static BlockLayout Generate(WallContour wall, int seed, SettlementSize size)
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
            var coreSet = new HashSet<(int i, int j)>(core);

            // ---- 3. gates on the ring, spread by angle -------------------------------------------------
            layout.GateCells.AddRange(PlaceGateCells(ring, SettlementSizing.GateCount(size), seed));

            // The network as it grows. The ring is its seed, which is what makes every later pass — whose
            // strips must touch the network — connected to every gate without a repair step.
            var streetSet = new HashSet<(int i, int j)>(ring);

            // ---- 4. an arterial inward from each gate --------------------------------------------------
            var centre = CentreCell(ring, core);
            var arterials = Arterials(coreSet, layout.GateCells, centre);
            foreach (var c in arterials) streetSet.Add(c);

            // ---- 5. frontage fill: pave until nothing is stranded --------------------------------------
            // FrontageFill ADDS its cells to `streetSet` as it goes (it has to — each strip is scored against
            // the network the previous strips left behind), so `streetSet` is current after this call.
            var filled = FrontageFill(coreSet, streetSet);

            // Streets = ring ∪ arterials ∪ frontage strips, in one row-major order. Sorted, not concatenated:
            // this list is SERIALIZED (SettlementParams.StreetCells), so a stable order keeps a re-generated
            // town byte-comparable with itself.
            layout.StreetCells.AddRange(ring);
            layout.StreetCells.AddRange(arterials);
            layout.StreetCells.AddRange(filled);
            layout.StreetCells.Sort(RowMajor);

            // ---- 6. what is left is the blocks ---------------------------------------------------------
            var blocks = Blocks(coreSet, streetSet);

            // ---- 7. fill each block --------------------------------------------------------------------
            blocks.Sort(ByLowestCell);            // deterministic block order → deterministic rng consumption
            var fillRng = new System.Random(seed * 977 + 41);
            foreach (var b in blocks)
                FillBlock(b, streetSet, fillRng, layout.Buildings);

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

        // ---- pass 3: gates ---------------------------------------------------------------------------

        /// <summary>Gate cells on the ring, spread by ANGLE around the town centre with a seeded phase, and
        /// never closer than <see cref="MinGateSeparationCells"/> to one another in Chebyshev distance.
        ///
        /// ANGLE, NOT A PERIMETER WALK, and that is a consequence of the type rather than a preference: the
        /// ring arrives here as a SET of cells, not an ordered lap, so there is no arc length to spread along
        /// without first ordering it — and ordering a ring that is two cells deep in a quarter of its length
        /// (see RingStreet) is its own tracing problem with its own failure modes. The angle from the ring's
        /// own centroid is well defined for every cell in the set, needs no ordering, and lands gates on
        /// opposite sides of town exactly the way an arc-length walk would on a convex contour.
        ///
        /// A DROPPED GATE IS LEGAL; TWO GATES IN ONE DOORWAY IS NOT. When no ring cell is far enough from the
        /// gates already placed, that gate is simply not placed, and the town comes out with fewer gates than
        /// `gateCount` asked for. That is reachable only on a DEGENERATE ring — one shorter than roughly
        /// 3 x gateCount cells, e.g. the 8-cell ring of a 3x3 town, where every cell is within Chebyshev 2 of
        /// every other and only ONE gate can ever be placed. No size class production ships is anywhere near
        /// that (the smallest wall radius is 4.7 cells, a ring of ~30), so the drop is a degenerate-input
        /// contract rather than a behaviour towns exhibit. The alternative — placing the gate anyway — would
        /// put two gates in what reads as a single wide doorway, which is a defect a DM can SEE.</summary>
        public static List<(int i, int j)> PlaceGateCells(IReadOnlyList<(int i, int j)> ring, int gateCount, int seed)
        {
            var gates = new List<(int i, int j)>();
            if (ring == null || ring.Count == 0 || gateCount <= 0) return gates;

            double cx = 0, cy = 0;
            foreach (var c in ring) { cx += c.i; cy += c.j; }
            cx /= ring.Count; cy /= ring.Count;

            // Its own Random, seeded from `seed` alone, so a change to how many rolls any other pass makes
            // can never shift where the gates land.
            var rng = new System.Random(seed * 7919 + 13);
            double phase = rng.NextDouble() * 2.0 * System.Math.PI;

            for (int k = 0; k < gateCount; k++)
            {
                double target = phase + k * 2.0 * System.Math.PI / gateCount;
                bool have = false;
                (int i, int j) best = default;
                double bestDelta = 0;
                foreach (var c in ring)
                {
                    if (TooCloseToAGate(c, gates)) continue;
                    double delta = AngleDistance(System.Math.Atan2(c.j - cy, c.i - cx), target);
                    // Strict <, then row-major on an exact tie — the two candidates' angles are computed by
                    // the identical expression, so an exact tie is a real tie and not a rounding artefact.
                    if (!have || delta < bestDelta || (delta == bestDelta && RowMajor(c, best) < 0))
                    { best = c; bestDelta = delta; have = true; }
                }
                if (have) gates.Add(best);
            }
            return gates;
        }

        /// <summary>THE SEPARATION RULE, in one place so there is exactly one line to break. True when `c` is
        /// too close to a gate already placed to be a gate itself.</summary>
        static bool TooCloseToAGate((int i, int j) c, List<(int i, int j)> gates)
        {
            foreach (var g in gates)
                if (Chebyshev(c, g) < MinGateSeparationCells) return true;
            return false;
        }

        static int Chebyshev((int i, int j) a, (int i, int j) b)
        {
            int di = a.i > b.i ? a.i - b.i : b.i - a.i;
            int dj = a.j > b.j ? a.j - b.j : b.j - a.j;
            return di > dj ? di : dj;
        }

        /// <summary>The absolute angular gap between two angles, folded into 0..π — so a gate whose angle is
        /// just BELOW the target and one just above are equally near it, instead of the first reading as
        /// nearly a full turn away.</summary>
        static double AngleDistance(double a, double b)
        {
            double two = 2.0 * System.Math.PI;
            double d = (a - b) % two;
            if (d < 0) d += two;
            if (d > System.Math.PI) d = two - d;
            return d;
        }

        // ---- pass 4: arterials -----------------------------------------------------------------------

        /// <summary>The core cell the town's arterials run to: the core cell nearest the RING's centroid.
        /// PUBLIC so a self-test can name the same cell production does instead of re-deriving it — the
        /// "arterials reach the middle of town" invariant is only worth asserting if the two agree about
        /// which cell the middle is.
        ///
        /// The ring's centroid rather than the core's, because the ring is the town's outline and the core is
        /// whatever the outline left over; on a lopsided contour the two differ by a cell or so and the
        /// outline is the honest one. Falls back to the core's own centroid when the ring is empty (a
        /// degenerate input), and to (0,0) when there is nothing at all to average.</summary>
        public static (int i, int j) CentreCell(IReadOnlyList<(int i, int j)> ring,
                                                IReadOnlyList<(int i, int j)> core)
        {
            var anchor = (ring != null && ring.Count > 0) ? ring : core;
            if (anchor == null || anchor.Count == 0) return (0, 0);

            double cx = 0, cy = 0;
            foreach (var c in anchor) { cx += c.i; cy += c.j; }
            cx /= anchor.Count; cy /= anchor.Count;

            if (core == null || core.Count == 0)
                return ((int)System.Math.Round(cx), (int)System.Math.Round(cy));

            bool have = false;
            (int i, int j) best = default;
            double bestD = 0;
            foreach (var c in core)
            {
                double dx = c.i - cx, dy = c.j - cy;
                double d = dx * dx + dy * dy;
                if (!have || d < bestD || (d == bestD && RowMajor(c, best) < 0)) { best = c; bestD = d; have = true; }
            }
            return best;
        }

        /// <summary>From the core cell just inside each gate, a Manhattan path with at most one turn to the
        /// centre cell: first leg along the dominant axis of (centre − gate), second along the other. Returns
        /// the cells in the order they were paved (the caller sorts row-major before serializing).
        ///
        /// WHAT THIS IS FOR, since it is NOT what makes the network connected (the ring already does that):
        /// it is the road a traveller arriving at a gate actually drives in on. Without it the frontage fill
        /// below would still produce a connected, fully-fronted network — of stubs and alleys hanging off the
        /// ring, with no through route and no reason for a gate to be where it is. The two invariants it does
        /// carry, and that nothing else in this file does, are: the core cell just inside every gate is a
        /// STREET (a gate never opens onto the side of a house), and the town's centre cell is a STREET (the
        /// main roads meet in the middle).
        ///
        /// STOPPING EARLY. An arterial stops on the first cell an EARLIER arterial already paved: the traffic
        /// it carries is already served from there on, and running the second road alongside the first would
        /// spend the core twice for one route. The RING it started from does not count as paved for this
        /// purpose — it is not in `core` at all — or every arterial would stop at length zero. A cell that is
        /// not in `core` (a ring cell where the contour pinches) is SKIPPED rather than paved, and the walk
        /// carries on: a ring cell is already a street, so skipping it cannot open a gap in the road.</summary>
        public static List<(int i, int j)> Arterials(HashSet<(int i, int j)> core,
                                                     IReadOnlyList<(int i, int j)> gateCells,
                                                     (int i, int j) centre)
        {
            var laid = new List<(int i, int j)>();
            if (core == null || core.Count == 0 || gateCells == null) return laid;

            var paved = new HashSet<(int i, int j)>();
            foreach (var gate in gateCells)
            {
                // WHERE THE ROAD STARTS. Normally the core 4-neighbour nearest the centre — that is "the core
                // cell just inside the gate". But the ring is only MOSTLY one cell wide (RingStreet: a quarter
                // of it is genuinely two cells deep), and on a SMALL contour a gate can land in the outer
                // layer with all four of its neighbours still ring, so there is no such cell. Starting the
                // walk AT THE GATE covers that: the paving loop below skips every non-core cell, so the road
                // simply begins at the first core cell the path reaches, however deep the ring is there.
                //
                // MEASURED, not hypothetical — this is exactly what a 3.63-cell contour does (SelfTestFrontage
                // sweep target 8, seed 1). Without the fallback BOTH of that town's gates were skipped, no
                // arterial ran at all, and the centre cell was left unpaved.
                var start = InwardStep(gate, core, centre) ?? gate;

                int di = centre.i - gate.i, dj = centre.j - gate.j;
                bool iFirst = System.Math.Abs(di) >= System.Math.Abs(dj);
                foreach (var cell in LPath(start, centre, iFirst))
                {
                    if (paved.Contains(cell)) break;         // an earlier arterial already carries this route
                    if (!core.Contains(cell)) continue;      // a ring cell / outside — already street, or not ours
                    paved.Add(cell);
                    laid.Add(cell);
                }
            }
            return laid;
        }

        /// <summary>The core cell an arterial starts from: the 4-neighbour of the gate that is in the core
        /// and lies nearest the centre, ties row-major. Nearest-to-centre rather than a fixed compass order
        /// because a gate on a diagonal stretch of wall has TWO inward neighbours and the road should set off
        /// toward town, not sideways along it. Null when the gate has no core 4-neighbour at all — a gate
        /// buried in a two-cell-deep stretch of ring; see Arterials for what happens then.</summary>
        static (int i, int j)? InwardStep((int i, int j) gate, HashSet<(int i, int j)> core, (int i, int j) centre)
        {
            bool have = false;
            (int i, int j) best = default;
            long bestD = 0;
            for (int d = 0; d < 4; d++)
            {
                var n = (i: gate.i + StepI[d], j: gate.j + StepJ[d]);
                if (!core.Contains(n)) continue;
                long dx = n.i - centre.i, dy = n.j - centre.j;
                long dd = dx * dx + dy * dy;
                if (!have || dd < bestD || (dd == bestD && RowMajor(n, best) < 0)) { best = n; bestD = dd; have = true; }
            }
            return have ? best : ((int i, int j)?)null;
        }

        /// <summary>Every cell of the Manhattan path from `from` to `to` with at most one turn, `from` and
        /// `to` included, running the named axis first.</summary>
        static List<(int i, int j)> LPath((int i, int j) from, (int i, int j) to, bool iFirst)
        {
            var path = new List<(int i, int j)> { from };
            var c = from;
            for (int leg = 0; leg < 2; leg++)
            {
                bool alongI = (leg == 0) == iFirst;
                if (alongI) while (c.i != to.i) { c.i += c.i < to.i ? 1 : -1; path.Add(c); }
                else        while (c.j != to.j) { c.j += c.j < to.j ? 1 : -1; path.Add(c); }
            }
            return path;
        }

        // ---- pass 5: frontage fill -------------------------------------------------------------------

        /// <summary>Pave until no core cell lacks a 4-adjacent street. THE ALGORITHMIC HEART OF THIS FILE: a
        /// street exists here for exactly one reason, that some house would otherwise have had no frontage.
        ///
        /// CANDIDATES are axis-aligned runs of core cells that are not already street: from every such cell,
        /// in all FOUR directions, truncated at the first cell that is street or not core, with every PREFIX
        /// of a run its own candidate. A candidate must touch the existing network at ≥1 end (see
        /// <see cref="TouchesNetwork"/>) — that is what keeps the streets one connected piece without a
        /// repair pass afterwards.
        ///
        /// ALL FOUR DIRECTIONS, NOT THE TWO POSITIVE ONES, and the difference is visible in the finished
        /// town. A prefix only ever touches the network through its OWN low end or through the far end of the
        /// WHOLE run, so enumerating +i/+j alone would offer short strips hanging off the network on its low
        /// side and, on the high side, only whole runs spanning the block. The fill would then grow stubs
        /// off the −i/−j faces of the ring and long spans off the other two: an asymmetry with no reason
        /// behind it, and more street than the frontage rule needs.
        ///
        /// SCORE = (unfronted core cells this strip would serve) / (cells it consumes), as a double. Served
        /// counts a cell whether the strip runs THROUGH it (it becomes street, so it no longer needs
        /// frontage) or merely PAST it (it gains a street 4-neighbour). Ties break by the ENUMERATION order —
        /// the strip's own start cell row-major, then direction (+i, −i, +j, −j), then length — because the
        /// comparison is strictly-greater, so the first of several equally efficient strips wins and, among
        /// prefixes of one run, that is the shortest.
        ///
        /// A STRIP THAT ENDS ON THE UNFRONTED CELL ITSELF IS LEGAL, and it is what guarantees termination.
        /// Take any unfronted cell u and walk +i from it: the run of core cells ends where the next cell is a
        /// ring cell (the last interior cell in a row IS a ring cell, by RingStreet's own rule) or an
        /// already-paved street — either way that far end touches the network, so the whole run is a legal
        /// candidate, and it serves at least u. So a candidate with a positive score always exists while any
        /// cell is unfronted, and every iteration paves at least one cell out of a finite core.
        ///
        /// THE "nothing to pave" BREAK IS NOT DEAD CODE. It is unreachable through Generate, by the argument
        /// above — but only because the ring seeds the network. A caller that hands in an EMPTY street set
        /// (the MutBlocksNoRingStreet mutant does exactly this) has no cell for any candidate to touch, so no
        /// candidate is ever legal, and without the break this loop would spin forever instead of degrading.
        ///
        /// MUTATES `streets`: each strip is scored against the network the previous strips left behind, so
        /// the set has to grow as we go. The caller gets the same set back, current.</summary>
        public static List<(int i, int j)> FrontageFill(HashSet<(int i, int j)> core, HashSet<(int i, int j)> streets)
        {
            var paved = new List<(int i, int j)>();
            if (core == null || core.Count == 0 || streets == null) return paved;

            // Row-major, so the candidate sweep below is a pure function of the cell SET and never of a
            // HashSet's enumeration order.
            var ordered = new List<(int i, int j)>(core);
            ordered.Sort(RowMajor);

            var unfronted = new HashSet<(int i, int j)>();
            var served = new HashSet<(int i, int j)>();
            var run = new List<(int i, int j)>();
            var bestRun = new List<(int i, int j)>();

            int guard = 0;
            while (guard++ < 100000)
            {
                unfronted.Clear();
                foreach (var c in ordered)
                    if (!streets.Contains(c) && !FrontsStreet(c, streets)) unfronted.Add(c);
                if (unfronted.Count == 0) break;                       // every house has its frontage

                double bestScore = 0;
                bestRun.Clear();
                foreach (var seed in ordered)
                {
                    if (streets.Contains(seed)) continue;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int stepI = FillDirI[dir], stepJ = FillDirJ[dir];
                        run.Clear();
                        var c = seed;
                        while (core.Contains(c) && !streets.Contains(c))
                        { run.Add(c); c = (c.i + stepI, c.j + stepJ); }
                        var before = (seed.i - stepI, seed.j - stepJ);
                        bool nearTouch = streets.Contains(before);

                        served.Clear();
                        for (int k = 0; k < run.Count; k++)
                        {
                            AddIfUnfronted(served, unfronted, run[k]);
                            for (int d = 0; d < 4; d++)
                                AddIfUnfronted(served, unfronted, (run[k].i + StepI[d], run[k].j + StepJ[d]));

                            var after = (run[k].i + stepI, run[k].j + stepJ);
                            if (!TouchesNetwork(before, after, nearTouch, k == run.Count - 1, streets)) continue;

                            double score = served.Count / (double)(k + 1);
                            if (score <= bestScore) continue;          // strictly greater — see the tie rule
                            bestScore = score;
                            bestRun.Clear();
                            for (int m = 0; m <= k; m++) bestRun.Add(run[m]);
                        }
                    }
                }
                if (bestRun.Count == 0) break;                          // see "nothing to pave" in the doc

                foreach (var c in bestRun) { streets.Add(c); paved.Add(c); }
            }
            return paved;
        }

        /// <summary>Does this candidate strip touch the existing street network at ≥1 end? `before` is the
        /// cell just off its low end and `after` the cell just off its high end; only the FULL run can touch
        /// through `after`, because a shorter prefix's high end is the next core cell of the same run.</summary>
        static bool TouchesNetwork((int i, int j) before, (int i, int j) after, bool nearTouch, bool isFullRun,
                                   HashSet<(int i, int j)> streets)
            => nearTouch || (isFullRun && streets.Contains(after));

        static void AddIfUnfronted(HashSet<(int i, int j)> served, HashSet<(int i, int j)> unfronted,
                                   (int i, int j) c)
        {
            if (unfronted.Contains(c)) served.Add(c);
        }

        // ---- pass 6: blocks --------------------------------------------------------------------------

        /// <summary>The 4-connected components of what is left after the streets. Row-major sorted first, so
        /// the component discovery order — and therefore the order the blocks are filled in — is a function
        /// of the cell set alone and never of a HashSet's enumeration order.</summary>
        public static List<List<(int i, int j)>> Blocks(HashSet<(int i, int j)> core, HashSet<(int i, int j)> streets)
        {
            var left = new List<(int i, int j)>();
            if (core == null) return new List<List<(int i, int j)>>();
            foreach (var c in core) if (streets == null || !streets.Contains(c)) left.Add(c);
            left.Sort(RowMajor);
            return Components(left);
        }

        // ---- pass 7: filling a block -----------------------------------------------------------------

        /// <summary>One entry of the shape palette. `Cells` is ORDERED and carries the invariant that EVERY
        /// PREFIX of it is itself a 4-connected shape containing (0,0) — which is what makes truncation safe:
        /// placement stops at the first unavailable cell and whatever it has taken is still a legal house.
        /// That is the same graceful degradation the old strip growth gave for free, generalized from
        /// rectangles to L, T and П.</summary>
        readonly struct FootprintTemplate
        {
            public readonly int Weight;
            public readonly (int di, int dj)[] Cells;
            public FootprintTemplate(int weight, params (int di, int dj)[] cells) { Weight = weight; Cells = cells; }
        }

        /// <summary>The palette. Every template fits inside a 2x3 box, so every one of them fits a block two
        /// rows deep — which is what blocks are, and what the DM asked to keep. Weights are out of
        /// PaletteWeightTotal and were calibrated against SelfTestBlockForms' 25% multi-cell / 5% non-rect
        /// achieved-share thresholds, NOT against the count guarantee and not against taste: raising a
        /// multi-cell template's weight raises mean footprint size, which — block cells being fixed per size —
        /// lowers the achieved building count. That is why this palette leaves SelfTestSizeCalibration red;
        /// restoring the guarantee is Task 4's job (a further split pass), not a reason to move these weights
        /// back down.</summary>
        static readonly FootprintTemplate[] Palette =
        {
            new FootprintTemplate(10, (0, 0)),                                              // Single
            new FootprintTemplate(16, (0, 0), (1, 0)),                                      // PairH
            new FootprintTemplate(16, (0, 0), (0, 1)),                                      // PairV
            new FootprintTemplate(32, (0, 0), (1, 0), (1, 1)),                              // L3
            new FootprintTemplate( 3, (0, 0), (1, 0), (2, 0)),                              // Line3
            new FootprintTemplate( 8, (0, 0), (1, 0), (0, 1), (1, 1)),                      // Square4
            new FootprintTemplate( 6, (0, 0), (1, 0), (2, 0), (1, 1)),                      // T4
            new FootprintTemplate(24, (0, 0), (1, 0), (2, 0), (0, 1), (2, 1)),              // U5 (П)
            new FootprintTemplate(13, (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)),      // Rect6
        };

        /// <summary>Must equal the sum of every Palette weight — SelfTestBlocksSanity asserts it, so a
        /// hand-edited weight that forgets this constant fails loudly instead of skewing the roll.</summary>
        public const int PaletteWeightTotal = 128;

        /// <summary>The sum PaletteWeightTotal is claimed to equal, computed straight off the live table —
        /// PUBLIC purely so SelfTestBlocksSanity's sentinel (the doc comment above names it) can check the
        /// claim against the table instead of trusting it.</summary>
        public static int PaletteWeightSum()
        {
            int sum = 0;
            for (int k = 0; k < Palette.Length; k++) sum += Palette[k].Weight;
            return sum;
        }

        static FootprintTemplate PickTemplate(System.Random rng)
        {
            int roll = rng.Next(PaletteWeightTotal);
            for (int k = 0; k < Palette.Length; k++)
            {
                roll -= Palette[k].Weight;
                if (roll < 0) return Palette[k];
            }
            return Palette[0];
        }

        /// <summary>Rotate an offset by rot * 90 degrees. (0,0) is rotation-invariant, so the seed stays the
        /// seed and the prefix invariant survives rotation unchanged.</summary>
        static (int di, int dj) Rotate((int di, int dj) c, int rot)
        {
            switch (rot & 3)
            {
                case 1:  return (-c.dj, c.di);
                case 2:  return (-c.di, -c.dj);
                case 3:  return (c.dj, -c.di);
                default: return c;
            }
        }

        /// <summary>Fill one block with disjoint, 4-connected footprints drawn from the shape palette,
        /// appending them to `into`.
        ///
        /// SEEDED ON FRONTAGE ONLY. A building starts at a block cell that already has a street 4-neighbour,
        /// so "every building fronts a street" holds BY CONSTRUCTION rather than by a filter afterwards — the
        /// footprint always contains its own seed cell. Cells with no street frontage are never seeded; they
        /// are reachable only by a neighbouring house growing into them, and whatever is left over stays open
        /// as the block's inner courtyard. Under the frontage rule above there are no such cells left in a
        /// generated town — that is the whole point of it — but the guard stays: FillBlock is public and its
        /// contract must not depend on who carved the block.
        ///
        /// TEMPLATES, TRUNCATED. A rolled template is walked cell by cell in its own fixed order; the walk
        /// stops at the first cell that is not Available, and whatever prefix it took is kept. That prefix is
        /// always a legal house because every prefix of a template is itself 4-connected and contains the seed
        /// (see FootprintTemplate's own doc) — so a П that runs into a claimed cell simply comes out smaller,
        /// never broken. Row-major seed order makes the whole pass deterministic given the block's own cell
        /// order.</summary>
        public static void FillBlock(List<(int i, int j)> block, HashSet<(int i, int j)> streets,
                                     System.Random rng, List<List<(int i, int j)>> into)
        {
            if (block == null || block.Count == 0 || into == null) return;
            var blockSet = new HashSet<(int i, int j)>(block);
            var claimed = new HashSet<(int i, int j)>();

            foreach (var seed in block)
            {
                if (!Available(seed, blockSet, claimed)) continue;
                if (!FrontsStreet(seed, streets)) continue;

                // EXACTLY TWO DRAWS PER BUILDING PLACED, taken here — after the seed's own two guards and
                // before the truncation walk, the same position in the sequence the single PickSize draw used
                // to occupy. The roll sequence must never depend on how far the previous building got.
                var tpl = PickTemplate(rng);
                int rot = rng.Next(4);

                var fp = new List<(int i, int j)>(tpl.Cells.Length);
                for (int k = 0; k < tpl.Cells.Length; k++)
                {
                    var (di, dj) = Rotate(tpl.Cells[k], rot);
                    var c = (seed.i + di, seed.j + dj);
                    if (!Available(c, blockSet, claimed)) break;   // TRUNCATE: every prefix is a legal house
                    fp.Add(c);
                }
                foreach (var c in fp) claimed.Add(c);
                into.Add(fp);
            }
        }

        /// <summary>A cell this footprint may take: inside the block AND not already claimed by another
        /// building. The `!claimed` term is THE disjointness rule of the fill — every footprint cell is
        /// tested through here, both the seed and every walked template cell, so there is exactly one line to
        /// break.</summary>
        static bool Available((int i, int j) c, HashSet<(int i, int j)> blockSet, HashSet<(int i, int j)> claimed)
            => blockSet.Contains(c) && !claimed.Contains(c);

        static bool FrontsStreet((int i, int j) c, HashSet<(int i, int j)> streets)
            => streets != null && (streets.Contains((c.i - 1, c.j)) || streets.Contains((c.i + 1, c.j))
                                || streets.Contains((c.i, c.j - 1)) || streets.Contains((c.i, c.j + 1)));

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
