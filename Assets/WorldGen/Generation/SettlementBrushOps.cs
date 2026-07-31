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

        /// <summary>Paint ONE building from a stroke's cells. A cell is dropped when it fails any of THREE
        /// independent rules — off the 0..1 field, a Building tile already stands there, or a NON-BUILDING
        /// room (in practice a gate) already owns it — and dropping a middle cell can sever the stroke, so only
        /// the 4-connected component containing the FIRST surviving cell is kept (the connectivity repair below).
        /// A stroke crossing the town's own derived wall PAINTS the wall cell (Wall and Gate are placeable now
        /// — checkpoint-1 amendment — because they are derived and re-derive around whatever is founded),
        /// which is exactly how the DM grows a town by drawing; it no longer stops "on both sides and not the
        /// wall itself" the way the retired rule did. Returns the new room, or null when no cell survived —
        /// and then the floor is untouched.
        ///
        /// NO SIZE CAP (DM ruling): the 6-cell cap and the shape palette are GENERATION rules. The result is
        /// still 4-connected, because AppendSegment's cells are and because dropping cells can only split a
        /// stroke that crossed an obstacle — see the connectivity repair below.</summary>
        public static Room PaintBuilding(InteriorFloor floor, IReadOnlyList<(int i, int j)> cells)
        {
            if (floor?.SettlementParams == null || cells == null || cells.Count == 0) return null;
            var grid = SettlementTileGrid.Build(floor);

            // ROOM OWNERSHIP (checkpoint-1 amendment, closing a hole the other two rules cannot). On a FRESHLY
            // GENERATED town a gate room's own cell is a ring-street cell reading Road, so the tile-type rule
            // alone never let a stroke land on it — the old three-term IsPlaceable additionally refused
            // Wall/Gate, which was doing that job for the one case that matters: a PREVIOUSLY-DRAGGED gate's
            // own cell is normalized onto the wall/gate cell (SettlementTileGrid.GateRoomAt's own doc) and
            // therefore reads Wall or Gate. Narrowing IsPlaceable to "refuses only Building" opened that cell
            // back up, and Precedes makes a founded Building strictly beat a Gate for a shared cell — the gate
            // would become permanently unselectable and undraggable, with nothing in DungeonValidator's
            // TypeId==1-scoped settlement rules ever reporting it. Built ONCE, over every NON-BUILDING room —
            // TypeId != 1, gates in practice — through SettlementTileGrid.FootprintOf, the SAME canonical read
            // every other founding/ownership test in this arc uses; this never decodes Room.Cells itself.
            //
            // TypeId == 1 (Building) ROOMS ARE DELIBERATELY EXCLUDED FROM `owned`, not merely uninteresting to
            // include. Build's own footprint-write loop marks Building from the EXACT SAME `floor.Rooms` list
            // through the EXACT SAME FootprintOf call, so every cell a building room owns already reads
            // TileType.Building on `grid` — the tile-type rule below is already the guard for it. Folding
            // TypeId == 1 rooms into `owned` too would not add coverage; it would make the tile-type rule
            // STRUCTURALLY DEAD CODE inside this method specifically: `grid.At(c) == Building` would then imply
            // `owned.Contains(c)` unconditionally (both sets are the same FootprintOf over the same rooms, one
            // merely narrowed by a TypeId filter), so a mutant that deletes the tile-type check could never be
            // caught by any assertion over the painted footprint — confirmed empirically, not just reasoned:
            // an earlier version of this rule included TypeId == 1 and silently made
            // MutBrushIgnoresPlaceable (which deletes exactly that check) undetectable by cases 5 and 7. The
            // two rules must stay extensionally DISJOINT for their mutants to stay independently killable.
            var owned = new HashSet<(int i, int j)>();
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == 1) continue;   // covered by the tile-type rule below; see the proof above
                foreach (var c in SettlementTileGrid.FootprintOf(r)) owned.Add(c);
            }

            // DEDUPED HERE, not in AppendSegment: a stroke is a PATH and may list a self-crossed cell twice.
            // Every rule below is its OWN statement (never folded into one short-circuited `&&` expression) so
            // a mutant can remove any ONE of them alone — folding them together would make several different
            // regressions the same single edit, and a mutant naming only one rule would silently also break
            // the others.
            var kept = new List<(int i, int j)>();
            var keptSet = new HashSet<(int i, int j)>();
            foreach (var c in cells)
            {
                if (!SettlementVolumeRendererPlacement.OnFieldCell(c.i, c.j)) continue;
                if (!SettlementVolumeRendererPlacement.IsPlaceable(grid.At(c.i, c.j))) continue;
                if (owned.Contains(c)) continue;
                if (keptSet.Add(c)) kept.Add(c);
            }
            if (kept.Count == 0) return null;

            // DROPPING A MIDDLE CELL CAN SPLIT THE STROKE. A footprint that is not 4-connected is an Error the
            // validator reports, so keep only the component containing the FIRST surviving cell — the piece
            // the DM started drawing. The rest of the stroke is simply not painted, which is the same answer
            // the placement rule gives for the obstacle itself.
            var keep = ComponentContainingFirst(kept);

            var room = new Room
            {
                Id = floor.NextRoomId++, TypeId = 1,
                Cells = SettlementFootprint.Encode(keep),
            };
            var rep = SettlementFootprint.Representative(keep);
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
            foreach (var c in cells)
            {
                // THE FIELD BOUND, same rule PaintBuilding applies (Task 3a) and through the same shared
                // expression. An off-field STREET cell is not the Clamp01 hazard a room is — it has no room —
                // but FitBoundsFor folds the stored streets into the fitted extent and the view never zooms
                // back in (DungeonViewController.cs:373), so one stroke past the panel's edge shrinks the town
                // until those cells are erased. Erase is deliberately NOT bounded: it is the way back.
                if (!SettlementVolumeRendererPlacement.OnFieldCell(c.i, c.j)) continue;
                if (seen.Add(c)) { all.Add(c); added++; }
            }
            if (added == 0) return 0;
            all.Sort(RowMajor);
            floor.SettlementParams.StreetCells = SettlementFootprint.Encode(all);
            return added;
        }

        static int RowMajor((int i, int j) a, (int i, int j) b)
            => a.j != b.j ? a.j.CompareTo(b.j) : a.i.CompareTo(b.i);

        /// <summary>Why the eraser's hover is red. Two halves, one for each thing a cell can be.
        ///
        /// A STREET CELL is refused exactly when removing it would make sub-project B's repair put it back.
        /// That is the rule, not an approximation of it: SettlementStreetOps.EnsureAccess runs after every
        /// edit, so a cell whose removal MissingAccess would immediately undo cannot be erased in any
        /// meaningful sense — and letting it through would not leave the town broken, it would leave the DM
        /// watching a DIFFERENT, minimal road appear somewhere else.
        ///
        /// A BUILDING CELL is ALWAYS ERASABLE (DM ruling, checkpoint 2). The old rule refused a cell whose
        /// removal would disconnect the remainder; a disconnected remainder is now a SPLIT, not a refusal —
        /// see RemoveBuildingCell — so there is nothing left for this branch to decide.</summary>
        public static bool CanErase(InteriorFloor floor, (int i, int j) cell)
        {
            if (floor?.SettlementParams == null) return false;

            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 1) continue;
                var fp = SettlementTileGrid.FootprintOf(r);
                if (!Contains(fp, cell)) continue;
                // ALWAYS ERASABLE (DM ruling, checkpoint 2). The old rule refused a cell whose removal would
                // disconnect the remainder; a disconnected remainder is now a SPLIT, not a refusal, so there is
                // nothing left for this branch to decide. Kept as an explicit `return true` rather than deleted
                // so the method still distinguishes "a building is here" from "nothing is here" — the street
                // test below must not run for a building cell.
                return true;
            }

            var streets = new List<(int i, int j)>(SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
            if (!streets.Remove(cell)) return false;                  // nothing here to erase
            var saved = floor.SettlementParams.StreetCells;
            floor.SettlementParams.StreetCells = streets.Count > 0 ? SettlementFootprint.Encode(streets) : null;
            // try/finally (fix round 1, Minor 2): this predicate runs on every hover frame while the eraser is
            // armed over a street cell (UpdatePlacement -> CanErase), not just on a commit, so the temporary
            // mutation below is live for the LONGEST and most-repeated window of anywhere in this file. Without
            // this, an exception thrown out of MissingAccess would skip the restore two lines down and leave
            // the floor's stored streets permanently one cell short — from a call that only ASKED whether a
            // cell could be erased. The two lines the existing mutants (MutEraseAllowsStranding,
            // MutEraseNoRestore) target are unchanged, only moved into try/finally.
            try
            {
                bool safe = SettlementStreetOps.MissingAccess(floor).Count == 0;
                return safe;
            }
            finally
            {
                floor.SettlementParams.StreetCells = saved;               // ALWAYS restored, on both branches
            }
        }

        /// <summary>Remove building cells and street cells under the stroke, skipping any cell CanErase
        /// refuses. Returns how many cells were removed.
        ///
        /// RE-ASKS CanErase AFTER EVERY REMOVAL, against the floor as it stands — never against the floor as
        /// it was when the stroke began. Two cells that are each individually safe can be fatal together: a
        /// lane two cells wide is load-bearing as a pair while neither cell is alone. Evaluating the whole
        /// stroke up front would let one gesture break the invariant that the repair then silently re-carves
        /// somewhere else, which is the exact outcome CanErase exists to prevent.</summary>
        public static int Erase(InteriorFloor floor, IReadOnlyList<(int i, int j)> cells)
        {
            if (floor?.SettlementParams == null || cells == null) return 0;
            int removed = 0;
            // GESTURE ORDER, never sorted (DM decision, checkpoint 2). Task 8's live preview runs this one
            // cell at a time as the cursor moves; only if the batch honours the same order do the preview and
            // the commit compute the same answer. A repeat of an already-erased cell is harmless — CanErase
            // then finds nothing there and returns false.
            foreach (var cell in cells)
            {
                if (!CanErase(floor, cell)) continue;
                if (RemoveBuildingCell(floor, cell)) { removed++; continue; }
                if (RemoveStreetCell(floor, cell)) removed++;
            }
            return removed;
        }

        static bool Contains(List<(int i, int j)> cells, (int i, int j) c)
        {
            for (int k = 0; k < cells.Count; k++) if (cells[k].Equals(c)) return true;
            return false;
        }

        /// <summary>Remove one cell from the building room that owns it. If that DISCONNECTS the remainder,
        /// the building SPLITS (DM ruling, checkpoint 2): the LARGEST piece keeps `r`'s identity — its id, and
        /// therefore title, body, preview, portals, IsDummy and the building interior keyed by
        /// InteriorData.OwnerRoomId — ties broken row-major on the pieces' representative cells; every other
        /// piece becomes a fresh, anonymous room.</summary>
        static bool RemoveBuildingCell(InteriorFloor floor, (int i, int j) cell)
        {
            for (int k = 0; k < floor.Rooms.Count; k++)
            {
                var r = floor.Rooms[k];
                if (r.TypeId != 1) continue;
                var fp = SettlementTileGrid.FootprintOf(r);
                if (!Contains(fp, cell)) continue;
                var rest = new List<(int i, int j)>(fp);
                rest.Remove(cell);
                if (rest.Count == 0) { floor.Rooms.RemoveAt(k); return true; }
                var comps = Components4(rest);
                int best = 0;
                for (int c = 1; c < comps.Count; c++)
                {
                    if (comps[c].Count > comps[best].Count) { best = c; continue; }
                    if (comps[c].Count == comps[best].Count
                     && RowMajor(SettlementFootprint.Representative(comps[c]),
                                 SettlementFootprint.Representative(comps[best])) < 0) best = c;
                }
                AssignFootprint(r, comps[best]);
                for (int c = 0; c < comps.Count; c++)
                {
                    if (c == best) continue;
                    // A NEW, ANONYMOUS building. Title/Body/Preview are deliberately NOT copied: the DM's
                    // decision is that the larger piece stays that building, and duplicating the name onto
                    // both halves is the outcome that decision exists to prevent. IsDummy IS copied — it is a
                    // visual class, not content. Portals, Grid and the building INTERIOR stay with `r` for
                    // free, because `r` keeps its id and the interior is keyed by InteriorData.OwnerRoomId.
                    var split = new Room { Id = floor.NextRoomId++, TypeId = 1, IsDummy = r.IsDummy };
                    AssignFootprint(split, comps[c]);
                    floor.Rooms.Add(split);
                }
                return true;
            }
            return false;
        }

        /// <summary>The cells' 4-connected components, each row-major sorted. One component is the common
        /// case; more than one is exactly the split this method exists to feed.</summary>
        static List<List<(int i, int j)>> Components4(List<(int i, int j)> cells)
        {
            var remaining = new HashSet<(int i, int j)>(cells);
            var comps = new List<List<(int i, int j)>>();
            int[] di = { -1, 1, 0, 0 }, dj = { 0, 0, -1, 1 };
            while (remaining.Count > 0)
            {
                var seed = default((int i, int j));
                foreach (var c in remaining) { seed = c; break; }
                var comp = new List<(int i, int j)>();
                var stack = new List<(int i, int j)> { seed };
                remaining.Remove(seed);
                while (stack.Count > 0)
                {
                    var cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    comp.Add(cur);
                    for (int k = 0; k < 4; k++)
                    {
                        var n = (i: cur.i + di[k], j: cur.j + dj[k]);
                        if (remaining.Remove(n)) stack.Add(n);
                    }
                }
                comp.Sort(RowMajor);
                comps.Add(comp);
            }
            return comps;
        }

        // Components4 deliberately does NOT reuse ComponentContainingFirst — that one answers "the piece the
        // DM started drawing" (keeps whichever component holds cells[0], regardless of size), this one needs
        // ALL of the components so RemoveBuildingCell can rank them by size. Do not merge them.

        /// <summary>Write a footprint onto a room and re-derive its point from it — the convention every
        /// producer in this arc follows (cells are the truth, the point is derived).</summary>
        static void AssignFootprint(Room r, List<(int i, int j)> cells)
        {
            r.Cells = SettlementFootprint.Encode(cells);
            var rep = SettlementFootprint.Representative(cells);
            r.X = SettlementFootprint.CenterOf(rep.i);
            r.Y = SettlementFootprint.CenterOf(rep.j);
        }

        static bool RemoveStreetCell(InteriorFloor floor, (int i, int j) cell)
        {
            var streets = new List<(int i, int j)>(SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
            if (!streets.Remove(cell)) return false;
            floor.SettlementParams.StreetCells = streets.Count > 0 ? SettlementFootprint.Encode(streets) : null;
            return true;
        }

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
    ///
    /// ONLY A BUILDING REFUSES A CELL (checkpoint-1 amendment). Wall and Gate tiles are DERIVED, every
    /// rebuild, from (building footprints ∪ stored street cells): founding a house on a wall cell does not
    /// collide with anything, it makes BuildWallRing re-derive the ring one cell further out, which is
    /// exactly how the DM expands a town by drawing. Refusing them made the town's own wall a hard border
    /// the brush could not cross. This is the same reasoning AreCellsFree already applied to MOVES — see its
    /// doc — now applied to FOUNDING as well, so the two verdicts differ only in the mover exemption.
    ///
    /// Road and Void are legal, which is why painting a building over a lane is allowed and merely earns the
    /// validator's existing warning.</summary>
    public static class SettlementVolumeRendererPlacement
    {
        public static bool IsPlaceable(TileType type) => type != TileType.Building;

        /// <summary>Is the cell's CENTRE inside the 0..1 field a room may legally occupy? The second axis of
        /// the founding verdict, shared verbatim with SettlementVolumeRenderer.AreCellsFree so a brush and a
        /// drag can never disagree about where the board ends.
        ///
        /// It is not theoretical: DungeonViewController's cascade re-reads each room's X/Y every animation
        /// frame and writes back Mathf.Clamp01 of it, so a room stored off-field is pinned to the edge for
        /// the whole of every later cascade. Allocate pads the drawn grid by MarginCells = 3 cells past the
        /// outermost building and a large town reaches 0.95, so cells past 1.0 are genuinely drawn and
        /// genuinely clickable.
        ///
        /// Bound is 0..1 EXACTLY, not the drag clamp's 0.04..0.96 — Clamp01 is the invariant being protected,
        /// and borrowing a drag-feel constant would hide why the limit is where it is.</summary>
        public static bool OnFieldCell(int i, int j)
        {
            float nx = SettlementFootprint.CenterOf(i), ny = SettlementFootprint.CenterOf(j);
            return nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f;
        }
    }
}
