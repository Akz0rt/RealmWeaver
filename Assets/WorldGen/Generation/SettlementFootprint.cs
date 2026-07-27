using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>A settlement building's occupied cells, in ABSOLUTE lattice indices, plus the lattice itself.
    /// Pure, UnityEngine-free, no state of its own.
    ///
    /// THE LATTICE IS FIXED. Cell i spans normalized [i*Pitch, (i+1)*Pitch) on each axis — a half-open span
    /// whose origin is normalized 0, NOT the position of any building. That is the whole point: a cell index
    /// is a property of the coordinate alone, so it cannot change because some OTHER building moved. The
    /// previous lattice (SettlementTileGrid.Allocate) anchored on the min-X/min-Y building, which meant moving
    /// that one building renumbered every cell in town; three separate fixes in the 2.5D-render arc existed
    /// only to defend against that, and they are what this fixed origin retires.
    ///
    /// FLOOR, NEVER ROUND. Because the span is half-open, the coordinate exactly on a boundary belongs to the
    /// UPPER cell; rounding would split a cell's own span between two indices and put boundary coordinates in
    /// the wrong one (MutFootprintRoundNotFloor pins this).
    ///
    /// SHAPES ARE ARBITRARY. The DM chose free-form blocks, so a footprint may be an L, a bar, or a RING
    /// around an internal courtyard — 4-connectivity of the filled cells says nothing about holes, and
    /// IsConnected4 deliberately accepts a ring. What is NOT allowed is a footprint whose cells fall apart
    /// into separate islands.
    ///
    /// WIRE FORM. Stored on Room.Cells as a FLAT int array, [i0,j0, i1,j1, …] — a jagged/tuple form would
    /// cost a wrapper object per cell in every save. Decode never throws: a null, empty or odd-length array
    /// (a corrupt or hand-edited save) yields an EMPTY footprint, so a bad file degrades instead of failing
    /// the whole load.</summary>
    public static class SettlementFootprint
    {
        /// <summary>Normalized pitch of the lattice — the SAME constant the building grid uses, so a change
        /// there is a change here and the two can never describe different lattices.</summary>
        public const float Pitch = SettlementGenerator.BuildingCell;   // 0.03f, normalized

        /// <summary>The pitch every save written before format 11 was laid out on. Used ONLY by migration,
        /// to recover cells from coordinates that were authored on the old lattice. Interpreting a legacy
        /// town on the CURRENT pitch would spread houses that stood flush 2.33 cells apart and open gaps
        /// through the whole town, which is why this constant exists rather than reusing Pitch.</summary>
        public const float LegacyPitch = 0.07f;

        /// <summary>CellOf against <see cref="LegacyPitch"/> — the cell a PRE-v11 coordinate was authored in.
        /// Same floor-not-round rule and the same fixed origin; only the pitch differs.</summary>
        public static int LegacyCellOf(float norm) => (int)System.Math.Floor(norm / LegacyPitch);

        /// <summary>The cell whose half-open span [i*Pitch, (i+1)*Pitch) contains `norm`. Floor, not round —
        /// see the class doc. Negative coordinates map to negative cells (Math.Floor(-0.5) == -1), which is
        /// what keeps the mapping monotonic across 0 instead of folding two spans onto cell 0.</summary>
        public static int CellOf(float norm) => (int)System.Math.Floor(norm / Pitch);

        /// <summary>The normalized CENTRE of a cell — the point a tile draws at, and the inverse CellOf
        /// round-trips through (CellOf(CenterOf(k)) == k for every k, since the centre sits half a cell clear
        /// of both boundaries).</summary>
        public static float CenterOf(int cell) => (cell + 0.5f) * Pitch;

        /// <summary>Read a stored footprint. NEVER throws: null, empty, or an odd length (a truncated or
        /// hand-edited save) all yield an empty footprint — the WHOLE array is dropped (there is no
        /// trustworthy way to tell which int is the odd one out), matching the class doc above.</summary>
        public static List<(int i, int j)> Decode(int[] flat)
        {
            var cells = new List<(int i, int j)>();
            if (flat == null || flat.Length % 2 != 0) return cells;
            // Length is even here (or we already returned), so `k < flat.Length` alone never reads flat[k+1]
            // out of bounds on the last pair — the more defensive-looking `k + 1 < flat.Length` decides the
            // exact same set of k's and was dropped as redundant, not as a behaviour change.
            for (int k = 0; k < flat.Length; k += 2) cells.Add((flat[k], flat[k + 1]));
            return cells;
        }

        /// <summary>Flatten a footprint for storage. A null/empty footprint encodes to an empty array (not
        /// null) — the caller decides whether to store null instead, which is what keeps the key off the wire
        /// for every non-settlement room.</summary>
        public static int[] Encode(IReadOnlyList<(int i, int j)> cells)
        {
            if (cells == null) return System.Array.Empty<int>();
            var flat = new int[cells.Count * 2];
            for (int k = 0; k < cells.Count; k++)
            {
                flat[2 * k] = cells[k].i;
                flat[2 * k + 1] = cells[k].j;
            }
            return flat;
        }

        /// <summary>True when every cell is reachable from the first by 4-connected steps — i.e. the building
        /// is ONE piece. A ring around a hole passes (the hole is not one of the cells, so nothing has to
        /// reach it); two cells touching only at a corner do not.
        ///
        /// A null or EMPTY footprint reads as NOT connected — a deliberate choice, not an oversight: there is
        /// no building there, so "this footprint is a single well-formed piece" is false, and a later
        /// validation pass that asks this question gets the answer it wants for a building that lost its
        /// cells. Duplicated cells are harmless: the reached count is compared against the DISTINCT cell
        /// count, so listing a cell twice never makes a connected footprint read as broken.</summary>
        public static bool IsConnected4(IReadOnlyList<(int i, int j)> cells)
        {
            if (cells == null || cells.Count == 0) return false;

            var all = new HashSet<(int i, int j)>();
            foreach (var c in cells) all.Add(c);

            var seen = new HashSet<(int i, int j)>();
            var stack = new List<(int i, int j)> { cells[0] };
            seen.Add(cells[0]);
            while (stack.Count > 0)
            {
                var (i, j) = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                Step(all, seen, stack, i - 1, j);
                Step(all, seen, stack, i + 1, j);
                Step(all, seen, stack, i, j - 1);
                Step(all, seen, stack, i, j + 1);
            }
            return seen.Count == all.Count;
        }

        static void Step(HashSet<(int i, int j)> all, HashSet<(int i, int j)> seen,
                         List<(int i, int j)> stack, int i, int j)
        {
            if (!all.Contains((i, j)) || !seen.Add((i, j))) return;
            stack.Add((i, j));
        }

        /// <summary>True when the two footprints share at least one cell. Null/empty on either side is no
        /// overlap.</summary>
        public static bool Overlaps(IReadOnlyList<(int i, int j)> a, IReadOnlyList<(int i, int j)> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return false;
            var set = new HashSet<(int i, int j)>();
            foreach (var c in b) set.Add(c);
            foreach (var c in a) if (set.Contains(c)) return true;
            return false;
        }

        /// <summary>The one cell that stands in for the whole building — the LOWEST in row-major order
        /// (smallest j, then smallest i). Deliberately NOT the centroid: an L-shaped or ring-shaped
        /// footprint's true centroid can fall in a cell the building does not occupy, so anything keyed on it
        /// (a label, a click target, a depth key) would sit off the building. This rule always returns one of
        /// the footprint's OWN cells, and always the same one for the same set. Empty/null yields (0,0) —
        /// there is nothing to represent.</summary>
        public static (int i, int j) Representative(IReadOnlyList<(int i, int j)> cells)
        {
            if (cells == null || cells.Count == 0) return (0, 0);
            var best = cells[0];
            for (int k = 1; k < cells.Count; k++)
            {
                var c = cells[k];
                if (c.j < best.j || (c.j == best.j && c.i < best.i)) best = c;
            }
            return best;
        }

        /// <summary>Inclusive cell bounding box. An empty/null footprint reports an EMPTY box —
        /// (0, 0, -1, -1), i.e. maxI &lt; minI — so a width computed as maxI - minI + 1 comes out 0 rather
        /// than a phantom 1.</summary>
        public static (int minI, int minJ, int maxI, int maxJ) Bounds(IReadOnlyList<(int i, int j)> cells)
        {
            if (cells == null || cells.Count == 0) return (0, 0, -1, -1);
            int minI = cells[0].i, maxI = cells[0].i, minJ = cells[0].j, maxJ = cells[0].j;
            for (int k = 1; k < cells.Count; k++)
            {
                var c = cells[k];
                if (c.i < minI) minI = c.i;
                if (c.i > maxI) maxI = c.i;
                if (c.j < minJ) minJ = c.j;
                if (c.j > maxJ) maxJ = c.j;
            }
            return (minI, minJ, maxI, maxJ);
        }

        /// <summary>A COPY of the footprint shifted by (di, dj). Shape, connectivity and cell order are all
        /// preserved; the input is never mutated (a caller mid-drag needs the original to fall back to).</summary>
        public static List<(int i, int j)> Translate(IReadOnlyList<(int i, int j)> cells, int di, int dj)
        {
            var moved = new List<(int i, int j)>();
            if (cells == null) return moved;
            foreach (var c in cells) moved.Add((c.i + di, c.j + dj));
            return moved;
        }

        /// <summary>LOAD NORMALIZATION (v10, extended in v11): give every settlement BUILDING **or GATE** room
        /// that carries no footprint a SINGLE-CELL one, at the cell its stored point falls in. This is what
        /// makes an existing save open unchanged — one building still occupies one cell, exactly as every town
        /// the DM has authored so far does — instead of being restructured into blocks behind their back.
        ///
        /// GATES TOO, FROM v11 (TypeId 0). A gate is a cell on the wall ring now — SettlementGenerator
        /// .BuildFloor stores it — and the v11 recentring translates the town by moving CELLS, so a gate with
        /// no cells would be the one node left behind in the middle of the field while the town moved around
        /// it. One widened type guard, not a second pass, deliberately: there stays exactly ONE `r.Cells =`
        /// write in this method for a mutant to break.
        ///
        /// WHICH PITCH THE CELL IS DERIVED ON IS THE CALLER'S TO DECIDE, and it must follow the FILE'S FORMAT
        /// VERSION — which is why <paramref name="legacyLattice"/> has no default and ProjectSerializer, the
        /// only place that knows the version, is the only production caller.
        ///
        ///   • legacyLattice TRUE (a file at format &lt;= 10): the point means a cell on the pre-v11 0.07
        ///     pitch, so <see cref="LegacyCellOf"/> recovers it. Reading it on the current 0.03 lattice would
        ///     scatter the town's cells 2.33x apart and open a gap between every pair of houses that stood
        ///     flush. The v11 migration (SettlementMigration.RecentreFloor, then RederivePositions) then
        ///     translates those legacy indices bodily onto the current lattice's centre and rewrites every
        ///     point from them, so the pair is what makes an old town open as the same town.
        ///   • legacyLattice FALSE (a file already at format 11): the point means a CURRENT-lattice cell, so
        ///     <see cref="CellOf"/> recovers it. This branch should never fire in practice — every v11 writer
        ///     stores cells alongside points (SettlementGenerator.BuildFloor for generated rooms,
        ///     DungeonOps.AddRoom for hand-added ones) — but "should never" is not "cannot", and a hand-edited
        ///     or partially-written v11 file that DID reach here on the legacy branch would get a 0.07-pitch
        ///     index written back to disk that no later pass ever repairs: RecentreFloor and RederivePositions
        ///     are both version-gated off at v11. The render masks it (SettlementTileGrid.FootprintOf rule (b)
        ///     re-derives a disagreeing single-cell footprint from the point), so it would be silently wrong
        ///     data at rest rather than a visible defect — exactly the class of bug that survives a checkpoint.
        ///     Keeping the pitch tied to the version is what makes "a v11 file's cells are v11 cells" hold by
        ///     construction instead of by convention.
        ///
        /// Lives here, not in ProjectSerializer, so it can be exercised headlessly: ProjectSerializer drags in
        /// System.IO/Newtonsoft/the notes+region model and cannot compile in the offline harness, which would
        /// leave this pass — the one that runs over every project the user has ever saved — with no test and
        /// no mutant. ProjectSerializer keeps the CALL, beside RoomSizing.ApplyDefaults.
        ///
        /// IDEMPOTENT, AND IT NEVER OVERWRITES. A room whose Cells is already non-empty is skipped outright,
        /// so re-running this pass (or running it over an already-normalized file) is a no-op — which is why
        /// the PASS itself is not version-gated, exactly like the RoomSizing.ApplyDefaults call beside it. A
        /// guard on FormatVersion is a thing a future bump forgets to widen; an idempotent normalization is
        /// not. Only its PITCH follows the version, through the parameter above — the pass still fires at
        /// every version, it just no longer assumes which lattice it is repairing.
        ///
        /// SizeW/SizeH ARE NOT TOUCHED, and must never be. They are TILES (one lattice cell is 0.03 * 128
        /// = 3.84 tiles) and ApplyDefaults has already given every settlement building 6x6 in every existing
        /// save; had the footprint reused them with the units redefined as cells, every saved town would have
        /// inflated by orders of magnitude in area, silently, on load. The footprint is a SEPARATE field.
        ///
        /// Kind == Settlement only: a BUILDING interior (Ц2 recursion) and a dungeon also hold TypeId==1 and
        /// TypeId==0 rooms, and neither has any business carrying a settlement lattice footprint.</summary>
        public static void EnsureFootprints(InteriorData interior, bool legacyLattice)
        {
            if (interior == null || interior.Kind != InteriorKind.Settlement || interior.Floors == null) return;
            foreach (var floor in interior.Floors)
            {
                if (floor == null || floor.Rooms == null) continue;
                foreach (var r in floor.Rooms)
                {
                    if (r == null || (r.TypeId != 1 && r.TypeId != 0)) continue;
                    // Guard on the DECODED count, not r.Cells.Length: an odd-length array (corrupt or
                    // hand-edited) is non-empty by Length but Decodes to zero cells, so a Length-only guard
                    // would read it as "already footprinted" and skip it FOREVER — the same idempotent
                    // never-overwrite rule that makes a good footprint permanent then makes a bad one
                    // permanent too. Decode(...).Count > 0 instead lets exactly the input Decode was
                    // hardened against (and only that input) self-heal on the very next load.
                    if (Decode(r.Cells).Count > 0) continue;   // already footprinted — never overwrite
                    var cell = legacyLattice ? (LegacyCellOf(r.X), LegacyCellOf(r.Y)) : (CellOf(r.X), CellOf(r.Y));
                    var one = new List<(int i, int j)> { cell };
                    r.Cells = Encode(one);
                }
            }
        }
    }
}
