using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>THE v11 LATTICE MIGRATION. Every save written before format 11 laid its towns out on the
    /// 0.07 pitch (SettlementFootprint.LegacyPitch); the lattice is 0.03 now, so the SAME cell indices
    /// describe a town that is 3/7 the normalized size and sits 3/7 of the way from the origin — i.e. up in
    /// the bottom-left corner of the field instead of in the middle of it. These two passes put it back.
    ///
    /// Pure and UnityEngine-free, and deliberately NOT inside ProjectSerializer, for the same reason
    /// SettlementFootprint.EnsureFootprints is not: ProjectSerializer drags in System.IO/Newtonsoft/the
    /// notes+region model and cannot compile in the offline harness, which would leave the pass that runs
    /// over every project the DM has ever saved with no test and no mutant. ProjectSerializer keeps the CALL.
    ///
    /// WHY CELLS ARE THE UNIT AND COORDINATES ARE NOT. A pre-v11 town's Room.Cells / SettlementParams.
    /// StreetCells hold ABSOLUTE lattice indices, and those indices still describe the town's SHAPE exactly:
    /// which houses are flush, which cell is a street, how wide a block is. Only their MAPPING to normalized
    /// space changed. So the migration never touches the indices' relative geometry — it applies ONE common
    /// integer translation to every cell in town (RecentreFloor) and then re-derives every room's stored
    /// point from its own cells (RederivePositions). Nothing is re-scaled, re-shaped, or re-generated.</summary>
    public static class SettlementMigration
    {
        /// <summary>Translate every cell on the floor — every TypeId 0/1 room's footprint and every
        /// SettlementParams.StreetCells entry — by ONE common integer delta, so the town's cell bounding box
        /// is centred on the field's centre cell (SettlementFootprint.CellOf(0.5f)). Because the delta is
        /// common, every relative fact about the town survives: two flush houses stay flush, a street stays
        /// between the same two blocks, a gate stays on the ring.
        ///
        /// VERSION-GATED BY ITS CALLER, unlike EnsureFootprints/ApplyDefaults beside it. This is a one-time
        /// REPAIR of coordinates authored on the old pitch, not a fill-in-the-blank normalization: re-running
        /// it on a town the DM has since deliberately dragged to one side would silently move the town back.
        ///
        /// IDEMPOTENT ANYWAY — an already-centred floor computes a zero delta and returns unchanged, which is
        /// what makes it safe for the caller to run it over every floor of every pre-v11 settlement without
        /// tracking which ones it has already touched. The halving below uses FLOOR division, not C#'s
        /// truncate-toward-zero `/`: with truncation a bbox whose index sum is ODD AND NEGATIVE recentres to
        /// one cell off target and a second call moves the town AGAIN (min+max = -3 truncates to -1, floors to
        /// -2 — the two differ, and only the floor form satisfies floor((s + 2(T - floor(s/2)))/2) == T for
        /// every s). The brief's "integer division" is that floor form.
        ///
        /// A floor with no cells at all (a settlement whose rooms are all footprint-less — impossible after
        /// EnsureFootprints, which is why the caller must run that FIRST) is left alone rather than
        /// translated by a delta derived from an empty box.</summary>
        public static void RecentreFloor(InteriorFloor floor)
        {
            if (floor == null) return;

            bool any = false;
            int minI = 0, minJ = 0, maxI = 0, maxJ = 0;
            void Fold(int i, int j)
            {
                if (!any) { minI = maxI = i; minJ = maxJ = j; any = true; return; }
                if (i < minI) minI = i;
                if (i > maxI) maxI = i;
                if (j < minJ) minJ = j;
                if (j > maxJ) maxJ = j;
            }

            // Buildings AND gates: both carry cells from v11 on (SettlementGenerator.BuildFloor writes the
            // gate's ring cell, EnsureFootprints gives an older one a single cell), and a gate left behind
            // by a translation that moved everything else would sit in the middle of town.
            var rooms = floor.Rooms;
            if (rooms != null)
                foreach (var r in rooms)
                {
                    if (r == null || (r.TypeId != 0 && r.TypeId != 1)) continue;
                    foreach (var c in SettlementFootprint.Decode(r.Cells)) Fold(c.i, c.j);
                }
            var streets = SettlementFootprint.Decode(floor.SettlementParams?.StreetCells);
            foreach (var c in streets) Fold(c.i, c.j);

            if (!any) return;

            int centre = SettlementFootprint.CellOf(0.5f);
            int di = centre - FloorHalf(minI + maxI);
            int dj = centre - FloorHalf(minJ + maxJ);
            if (di == 0 && dj == 0) return;                      // already centred — nothing to write

            if (rooms != null)
                foreach (var r in rooms)
                {
                    if (r == null || (r.TypeId != 0 && r.TypeId != 1)) continue;
                    var cells = SettlementFootprint.Decode(r.Cells);
                    if (cells.Count == 0) continue;               // no footprint → nothing to translate
                    r.Cells = SettlementFootprint.Encode(SettlementFootprint.Translate(cells, di, dj));
                }
            if (streets.Count > 0 && floor.SettlementParams != null)
                floor.SettlementParams.StreetCells =
                    SettlementFootprint.Encode(SettlementFootprint.Translate(streets, di, dj));
        }

        /// <summary>Every settlement room with a non-empty footprint gets X/Y = the normalized centre of its
        /// cells' BOUNDING BOX. Idempotent — running it twice writes the same value.
        ///
        /// ITS CALLER MUST VERSION-GATE IT, exactly like RecentreFloor, and the reason is worth stating here
        /// because "cells are the source of truth" makes it sound as though it need not be. They are not, for
        /// a building the DM has MOVED: dragging writes Room.X/Y from eight editor call sites and never
        /// rewrites Room.Cells — SettlementTileGrid.FootprintOf's rule (b) exists precisely to re-derive a
        /// stale single-cell footprint from the point at render time. So on an already-migrated file this pass
        /// would read the STALE cell and write the point back onto it, undoing the drag. It is a MIGRATION
        /// step (pair it with RecentreFloor, which has just moved every cell in town and left every point
        /// pointing at where the cell used to be), not a normalization.
        ///
        /// THE BBOX CENTRE, NOT THE CENTROID. An L-shaped or ring-shaped footprint's true centroid can fall in
        /// a cell the building does not occupy, so a label/click target keyed on it would sit off the
        /// building; the bbox centre puts the mark where the building LOOKS like it is. (Not
        /// SettlementFootprint.Representative either — that is the lowest row-major CELL, deliberately a
        /// corner, which is right for a depth key and wrong for a mark.)
        ///
        /// IT AGREES EXACTLY WITH GENERATION FOR A SINGLE-CELL FOOTPRINT, which is the case that matters:
        /// SettlementTileGrid.FootprintOf treats a single-cell footprint that disagrees with its room's point
        /// as STALE and re-derives it from the point, so a point in some other cell would silently relocate
        /// every one-cell house in town. For one cell, bbox centre == representative centre == that cell's
        /// centre, so this pass and SettlementGenerator.BuildFloor write the identical value. They differ ONLY
        /// for a MULTI-cell footprint (BuildFloor writes the representative's centre, this writes the bbox
        /// centre) — harmless, because FootprintOf never re-derives a multi-cell footprint from its point, and
        /// the bbox centre is the better mark of the two.</summary>
        public static void RederivePositions(InteriorData dungeon)
        {
            if (dungeon == null || dungeon.Kind != InteriorKind.Settlement || dungeon.Floors == null) return;
            foreach (var floor in dungeon.Floors)
            {
                if (floor == null || floor.Rooms == null) continue;
                foreach (var r in floor.Rooms)
                {
                    if (r == null) continue;
                    var cells = SettlementFootprint.Decode(r.Cells);
                    if (cells.Count == 0) continue;               // nothing to derive from — leave the point
                    var (minI, minJ, maxI, maxJ) = SettlementFootprint.Bounds(cells);
                    r.X = (SettlementFootprint.CenterOf(minI) + SettlementFootprint.CenterOf(maxI)) * 0.5f;
                    r.Y = (SettlementFootprint.CenterOf(minJ) + SettlementFootprint.CenterOf(maxJ)) * 0.5f;
                }
            }
        }

        /// <summary>v / 2 rounded DOWN (toward negative infinity), unlike C#'s `/` which rounds toward zero.
        /// See RecentreFloor's doc for why the difference is load-bearing.</summary>
        static int FloorHalf(int v) => v >= 0 ? v / 2 : -(((-v) + 1) / 2);
    }
}
