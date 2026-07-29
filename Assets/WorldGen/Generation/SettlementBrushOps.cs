using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>What a brush STROKE means, as pure functions over lattice cells (DM findings ·1 and ·8).
    ///
    /// EVERYTHING A STROKE DECIDES LIVES HERE, deliberately. The offline harness compiles Generation and not
    /// Rendering, so a rule expressed as "these cells, this floor -> this change" can be tested and mutated,
    /// while anything left in the controller can only be read. The controller's whole job is to collect the
    /// cells under the pointer and call one of these.</summary>
    public static class SettlementBrushOps
    {
        /// <summary>Append the lattice cells of the segment from `from` to `to` — including `to`, excluding a
        /// cell equal to the one already at the end of `into`.
        ///
        /// WHY INTERPOLATION AT ALL: the pointer is sampled once per frame, so a fast drag jumps several cells
        /// between samples. Appending only the sampled cell would paint a dotted line, and a dotted footprint
        /// is not 4-connected — which DungeonValidator reports as an Error. Bresenham over the lattice, taking
        /// ONE AXIS AT A TIME so consecutive cells are always 4-adjacent: a diagonal step would leave exactly
        /// the disconnected footprint this exists to prevent.</summary>
        public static void AppendSegment(List<(int i, int j)> into, (int i, int j) from, (int i, int j) to)
        {
            if (into == null) return;
            if (into.Count == 0) Push(into, from);

            int ci = from.i, cj = from.j;
            int di = System.Math.Sign(to.i - ci), dj = System.Math.Sign(to.j - cj);
            int ri = System.Math.Abs(to.i - ci), rj = System.Math.Abs(to.j - cj);
            int err = ri - rj;
            while (ci != to.i || cj != to.j)
            {
                int e2 = 2 * err;
                // One axis per iteration — never both — so every step is 4-adjacent to the last.
                if (e2 > -rj && ci != to.i) { err -= rj; ci += di; }
                else if (cj != to.j) { err += ri; cj += dj; }
                else if (ci != to.i) { ci += di; }
                Push(into, (ci, cj));
            }
        }

        /// <summary>`into` IS A PATH, NOT A SET — it is ordered, every consecutive pair is 4-adjacent, and a
        /// stroke that crosses itself DOES list the shared cell twice. That is deliberate and the two
        /// properties cannot both hold: the moment a duplicate is skipped, the next cell appended is no longer
        /// adjacent to the one before it, and the contiguity that proves interpolation happened is gone.
        /// Deduplication belongs to the OPS, which already do it (PaintRoad counts only new cells;
        /// PaintBuilding collects into a set). Only a repeat of the IMMEDIATELY preceding cell is dropped,
        /// because a pointer resting still would otherwise append the same cell every frame.</summary>
        static void Push(List<(int i, int j)> into, (int i, int j) c)
        {
            if (into.Count > 0 && into[into.Count - 1].Equals(c)) return;
            into.Add(c);
        }

        /// <summary>Paint ONE building from a stroke's cells. Cells the placement rule rejects are dropped, so
        /// a stroke crossing a wall paints the cells on both sides of it and not the wall itself. Returns the
        /// new room, or null when no cell survived — and then the floor is untouched.
        ///
        /// NO SIZE CAP (DM ruling): the 6-cell cap and the shape palette are GENERATION rules. The result is
        /// still 4-connected, because AppendSegment's cells are and because dropping cells can only split a
        /// stroke that crossed an obstacle — see the connectivity repair below.</summary>
        public static Room PaintBuilding(InteriorFloor floor, IReadOnlyList<(int i, int j)> cells)
        {
            if (floor?.SettlementParams == null || cells == null || cells.Count == 0) return null;
            var grid = SettlementTileGrid.Build(floor);

            // DEDUPED HERE, not in AppendSegment: a stroke is a PATH and may list a self-crossed cell twice.
            // The placement test and the dedup test are kept as TWO separate statements (not one short-
            // circuited `&&` expression) so a mutant can remove the placement rule ALONE — folding them into
            // one expression would make "ignore placement" and "ignore dedup" the same single edit, and a
            // mutant naming only the placement rule would silently also break dedup.
            var kept = new List<(int i, int j)>();
            var keptSet = new HashSet<(int i, int j)>();
            foreach (var c in cells)
            {
                if (!SettlementVolumeRendererPlacement.IsPlaceable(grid.At(c.i, c.j))) continue;
                if (keptSet.Add(c)) kept.Add(c);
            }
            if (kept.Count == 0) return null;

            // DROPPING A MIDDLE CELL CAN SPLIT THE STROKE. A footprint that is not 4-connected is an Error the
            // validator reports, so keep only the component containing the FIRST surviving cell — the piece
            // the DM started drawing. The rest of the stroke is simply not painted, which is the same answer
            // the placement rule gives for the obstacle itself.
            var kill = ComponentContainingFirst(kept);

            var room = new Room
            {
                Id = floor.NextRoomId++, TypeId = 1,
                Cells = SettlementFootprint.Encode(kill),
            };
            var rep = SettlementFootprint.Representative(kill);
            room.X = SettlementFootprint.CenterOf(rep.i);
            room.Y = SettlementFootprint.CenterOf(rep.j);
            var (w, h) = RoomSizing.Default(1); room.SizeW = w; room.SizeH = h;
            floor.Rooms.Add(room);
            return room;
        }

        /// <summary>Add street cells, keeping the stored list sorted row-major and duplicate-free. Returns how
        /// many were NEWLY added, so a stroke re-crossing its own path reports the cells it actually
        /// contributed.</summary>
        public static int PaintRoad(InteriorFloor floor, IReadOnlyList<(int i, int j)> cells)
        {
            if (floor?.SettlementParams == null || cells == null || cells.Count == 0) return 0;
            var all = new List<(int i, int j)>(SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
            var seen = new HashSet<(int i, int j)>(all);
            int added = 0;
            foreach (var c in cells) if (seen.Add(c)) { all.Add(c); added++; }
            if (added == 0) return 0;
            all.Sort(RowMajor);
            floor.SettlementParams.StreetCells = SettlementFootprint.Encode(all);
            return added;
        }

        static int RowMajor((int i, int j) a, (int i, int j) b)
            => a.j != b.j ? a.j.CompareTo(b.j) : a.i.CompareTo(b.i);

        /// <summary>The 4-connected component that contains <c>cells[0]</c> — nothing here compares
        /// component sizes, ever; whichever piece holds the first cell is the one returned, however large or
        /// small the others are. Deterministic because the input order is the stroke's own order, and the
        /// first cell is where the DM started drawing.</summary>
        static List<(int i, int j)> ComponentContainingFirst(List<(int i, int j)> cells)
        {
            var set = new HashSet<(int i, int j)>(cells);
            var comp = new List<(int i, int j)> { cells[0] };
            var seen = new HashSet<(int i, int j)> { cells[0] };
            var stack = new List<(int i, int j)> { cells[0] };
            int[] di = { -1, 1, 0, 0 }, dj = { 0, 0, -1, 1 };
            while (stack.Count > 0)
            {
                var cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                for (int k = 0; k < 4; k++)
                {
                    var n = (i: cur.i + di[k], j: cur.j + dj[k]);
                    if (set.Contains(n) && seen.Add(n)) { comp.Add(n); stack.Add(n); }
                }
            }
            comp.Sort(RowMajor);
            return comp;
        }
    }

    /// <summary>The placement rule, moved out of SettlementVolumeRenderer so the harness can compile it.
    /// A cell may be painted or placed on unless a building, a wall or a gate already occupies it; Road and
    /// Void are legal, which is why painting a building over a lane is allowed and merely earns the
    /// validator's existing warning.</summary>
    public static class SettlementVolumeRendererPlacement
    {
        public static bool IsPlaceable(TileType type)
            => type != TileType.Building && type != TileType.Wall && type != TileType.Gate;
    }
}
