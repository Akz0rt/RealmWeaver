using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for the battle grid. Every assertion names the exact cell the
    /// rule under test changes — never a count, never an area. The project's #1 past failure mode was a
    /// test that passes whether or not the rule holds (see CompactLayoutSelfTests for the same discipline).</summary>
    public class BattleGridSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Battle Grid Codec")]
        public void SelfTestCodec()
        {
            bool ok = true;

            // ---- 1. Round-trip preserves EVERY cell, not just the length -------------------------------
            // Mis-order the digits/letter in a run, or map a cell to the wrong letter, and a specific cell
            // comes back wrong. (Run MERGING is assertion 2's job — this sequence has no adjacent equals.)
            var cells = new GridCell[12];
            for (int i = 0; i < 12; i++) cells[i] = (GridCell)(i % 7);
            string s = BattleGridCodec.Encode(cells);
            if (!BattleGridCodec.TryDecode(s, 4, 3, out var back))
            { Debug.LogError($"FAIL codec: TryDecode rejected its own Encode output '{s}'"); ok = false; }
            else
                for (int i = 0; i < 12; i++)
                    if (back[i] != cells[i])
                    { Debug.LogError($"FAIL codec: cell {i} encoded as {cells[i]}, decoded as {back[i]} (string '{s}')"); ok = false; break; }

            // ---- 2. Adjacent equal cells MERGE into one run --------------------------------------------
            // Delete the merge and this becomes "1F1F1F1F", which is 8 chars, not 2.
            string flat = BattleGridCodec.Encode(new[] { GridCell.Floor, GridCell.Floor, GridCell.Floor, GridCell.Floor });
            if (flat != "4F")
            { Debug.LogError($"FAIL codec: four Floor cells encoded as '{flat}', want '4F'"); ok = false; }

            // ---- 3. Strict parse: wrong total, unknown letter, empty, missing count ---------------------
            // Each of these must be REJECTED whole. Accepting one silently loads half a map.
            if (BattleGridCodec.TryDecode("3F", 4, 3, out _))
            { Debug.LogError("FAIL codec: '3F' accepted for a 4x3 grid (needs 12 cells, declares 3)"); ok = false; }
            if (BattleGridCodec.TryDecode("11F1Z", 4, 3, out _))
            { Debug.LogError("FAIL codec: unknown letter 'Z' accepted"); ok = false; }
            if (BattleGridCodec.TryDecode("", 4, 3, out _))
            { Debug.LogError("FAIL codec: empty string accepted"); ok = false; }
            if (BattleGridCodec.TryDecode("F12F", 4, 3, out _))
            { Debug.LogError("FAIL codec: run without a leading count accepted"); ok = false; }
            if (BattleGridCodec.TryDecode("13F", 4, 3, out _))
            { Debug.LogError("FAIL codec: '13F' accepted for a 12-cell grid (declares 13)"); ok = false; }

            // ---- 4. Buffer indexing: y=0 is the BOTTOM row, index = y*Width + x ------------------------
            // Flip the row order and (0,0) stops being index 0.
            var buf = new GridBuffer(3, 2);
            buf.Set(0, 0, GridCell.Wall);
            buf.Set(2, 1, GridCell.Chasm);
            if (buf.Cells[0] != GridCell.Wall)
            { Debug.LogError($"FAIL buffer: (0,0) must be index 0, got {buf.Cells[0]} there"); ok = false; }
            if (buf.Cells[5] != GridCell.Chasm)
            { Debug.LogError($"FAIL buffer: (2,1) must be index 5 in a 3-wide grid, got {buf.Cells[5]} there"); ok = false; }
            if (buf.InBounds(3, 0) || buf.InBounds(-1, 0) || buf.InBounds(0, 2))
            { Debug.LogError("FAIL buffer: InBounds accepted an out-of-range coordinate"); ok = false; }

            // ---- 5. Model round-trip, at dimensions a SAVED grid is allowed to have -------------------
            // 5x4 rather than the 3x2 above on purpose: FromModel rejects out-of-range dimensions, because
            // they arrive from a file and TryDecode sizes an allocation from their product.
            var storable = new GridBuffer(5, 4);
            storable.Set(3, 2, GridCell.Chasm);
            var model = storable.ToModel();
            var reborn = GridBuffer.FromModel(model);
            if (reborn == null || reborn.Get(3, 2) != GridCell.Chasm || reborn.Width != 5 || reborn.Height != 4)
            { Debug.LogError("FAIL buffer: ToModel/FromModel lost the (3,2) Chasm or the dimensions"); ok = false; }

            // ---- 6. Corrupt model → null (caller regenerates) ------------------------------------------
            // Legal dimensions on purpose: 5x4 passes the bounds guard, so the ONLY thing that can reject
            // this is the decode itself (99 cells declared, 20 required). With a sub-minimum width the
            // guard would short-circuit first and this would prove nothing about TryDecode.
            var corrupt = new BattleGrid { Width = 5, Height = 4, Cells = "99F" };
            if (GridBuffer.FromModel(corrupt) != null)
            { Debug.LogError("FAIL buffer: FromModel returned a buffer for a corrupt string instead of null"); ok = false; }

            // ---- 6b. Out-of-range stored dimensions are refused -----------------------------------------
            // Delete the bounds check in FromModel and this returns a buffer, letting a corrupt file drive
            // a huge allocation inside TryDecode.
            if (GridBuffer.FromModel(new BattleGrid { Width = 3, Height = 4, Cells = "12F" }) != null)
            { Debug.LogError("FAIL buffer: FromModel accepted a stored width of 3, below the 4 minimum"); ok = false; }
            if (GridBuffer.FromModel(new BattleGrid { Width = 41, Height = 4, Cells = "164F" }) != null)
            { Debug.LogError("FAIL buffer: FromModel accepted a stored width of 41, above the 40 maximum"); ok = false; }

            // ---- 7. Clamp ------------------------------------------------------------------------------
            if (BattleGridCodec.Clamp(1) != 4 || BattleGridCodec.Clamp(99) != 40 || BattleGridCodec.Clamp(12) != 12)
            { Debug.LogError($"FAIL clamp: got {BattleGridCodec.Clamp(1)}/{BattleGridCodec.Clamp(99)}/{BattleGridCodec.Clamp(12)}, want 4/40/12"); ok = false; }

            if (ok) Debug.Log("Battle Grid Codec: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Generator")]
        public void SelfTestGenerator()
        {
            bool ok = true;

            // ---- 1. Size = footprint + a one-cell wall ring on EACH side -------------------------------
            // Drop the "+2" and this becomes 6x4; put the ring INSIDE and the usable floor shrinks (checked below).
            var room = new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f, SizeW = 6, SizeH = 4 };
            var buf = BattleGridGenerator.Generate(room);
            if (buf.Width != 8 || buf.Height != 6)
            { Debug.LogError($"FAIL gen: a 6x4 room produced a {buf.Width}x{buf.Height} grid, want 8x6"); ok = false; }

            // ---- 2. The ring is Wall on all four sides, checked at named cells --------------------------
            // Remove any single side of the ring and the matching assertion below names it.
            if (buf.Get(0, 0) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: corner (0,0) is {buf.Get(0, 0)}, want Wall"); ok = false; }
            if (buf.Get(7, 5) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: corner (7,5) is {buf.Get(7, 5)}, want Wall"); ok = false; }
            if (buf.Get(3, 0) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: bottom wall (3,0) is {buf.Get(3, 0)}, want Wall"); ok = false; }
            if (buf.Get(3, 5) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: top wall (3,5) is {buf.Get(3, 5)}, want Wall"); ok = false; }
            if (buf.Get(0, 3) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: left wall (0,3) is {buf.Get(0, 3)}, want Wall"); ok = false; }
            if (buf.Get(7, 3) != GridCell.Wall)
            { Debug.LogError($"FAIL gen: right wall (7,3) is {buf.Get(7, 3)}, want Wall"); ok = false; }

            // ---- 3. Everything inside the ring is Floor, INCLUDING the cells adjacent to it -------------
            // A ring drawn two cells thick, or an off-by-one interior loop, flips (1,1) or (6,4).
            if (buf.Get(1, 1) != GridCell.Floor)
            { Debug.LogError($"FAIL gen: inner corner (1,1) is {buf.Get(1, 1)}, want Floor"); ok = false; }
            if (buf.Get(6, 4) != GridCell.Floor)
            { Debug.LogError($"FAIL gen: inner corner (6,4) is {buf.Get(6, 4)}, want Floor"); ok = false; }

            // ---- 4. A tiny room still yields a legal grid, and the clamp is what makes it legal ---------
            // SizeW=1 -> 1+2 = 3, below MinSide 4. Without the clamp this constructs a 3-wide grid.
            var tiny = new Room { Id = 2, TypeId = 1, X = 0.5f, Y = 0.5f, SizeW = 1, SizeH = 1 };
            var tb = BattleGridGenerator.Generate(tiny);
            if (tb.Width != 4 || tb.Height != 4)
            { Debug.LogError($"FAIL gen: a 1x1 room produced {tb.Width}x{tb.Height}, want the 4x4 minimum"); ok = false; }

            // ---- 5. Size comes from EffectiveSize, not the raw fields -----------------------------------
            // SizeW=0 means "unset"; EffectiveSize substitutes the type default (6,6 for Normal) -> 8x8.
            // Read room.SizeW directly instead and this collapses to the 4x4 minimum.
            var unset = new Room { Id = 3, TypeId = 1, X = 0.5f, Y = 0.5f, SizeW = 0, SizeH = 0 };
            var ub = BattleGridGenerator.Generate(unset);
            if (ub.Width != 8 || ub.Height != 8)
            { Debug.LogError($"FAIL gen: an unsized Normal room produced {ub.Width}x{ub.Height}, want 8x8 from the type default"); ok = false; }

            if (ok) Debug.Log("Battle Grid Generator: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Doors")]
        public void SelfTestDoors()
        {
            bool ok = true;

            // Two rooms side by side on one floor, linked. A is 6x4 at tile (32,64); B sits to its RIGHT,
            // so the link leaves A through its RIGHT wall. Positions are normalized (tile / TilesPerAxis).
            const float T = DungeonLayout.TilesPerAxis;
            var floor = new InteriorFloor { NextRoomId = 3 };
            var a = new Room { Id = 1, TypeId = 1, SizeW = 6, SizeH = 4, X = 32f / T, Y = 64f / T };
            var b = new Room { Id = 2, TypeId = 1, SizeW = 6, SizeH = 4, X = 52f / T, Y = 64f / T };
            floor.Rooms.Add(a); floor.Rooms.Add(b);
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });

            var buf = BattleGridGenerator.Generate(a);          // 8x6
            var doors = BattleGridGenerator.ProjectDoors(floor, a, buf);

            // ---- 1. A's door is on A's RIGHT wall column, and nowhere else ------------------------------
            // Drop the wall-attribution branch and B's own door (on B's left wall) leaks in at x==0.
            if (doors.Count == 0)
            { Debug.LogError("FAIL doors: a linked room produced no door at all"); ok = false; }
            foreach (var d in doors)
                if (d.X != buf.Width - 1)
                { Debug.LogError($"FAIL doors: door at ({d.X},{d.Y}) is not on A's right wall column x={buf.Width - 1}"); ok = false; }

            // ---- 2. A door never lands on a corner — corners are not walls you can walk through ---------
            foreach (var d in doors)
                if (d.Y <= 0 || d.Y >= buf.Height - 1)
                { Debug.LogError($"FAIL doors: door at ({d.X},{d.Y}) sits on the ring corner row"); ok = false; }

            // ---- 3. Y FLIPS: tile-space Y grows DOWN the screen, grid Y grows UP ------------------------
            // A door at the tile rect's TOP edge (max tile-Y, drawn at the BOTTOM of the schematic) must
            // land on grid row 0. Feed the formula a synthetic point to pin the direction with no
            // dependence on which wall the router happened to choose.
            // A spans tile Y 62..66 (centre 64, height 4). Inner rows are 1..4.
            var lowY  = BattleGridGenerator.ProjectDoorPoint(a, buf, 35f, 62.0f);   // tile top-of-screen
            var highY = BattleGridGenerator.ProjectDoorPoint(a, buf, 35f, 65.9f);   // tile bottom-of-screen
            if (lowY.Y != 4)
            { Debug.LogError($"FAIL doors: tile Y 62 (screen-top) mapped to grid row {lowY.Y}, want 4 (grid top)"); ok = false; }
            if (highY.Y != 1)
            { Debug.LogError($"FAIL doors: tile Y 65.9 (screen-bottom) mapped to grid row {highY.Y}, want 1 (grid bottom)"); ok = false; }

            // ---- 4. Natural-size grid maps 1:1 — a one-tile step moves the door exactly one row ---------
            // Remove the +2 ring compensation and consecutive tiles collapse onto one row.
            var r1 = BattleGridGenerator.ProjectDoorPoint(a, buf, 35f, 63.5f);
            var r2 = BattleGridGenerator.ProjectDoorPoint(a, buf, 35f, 64.5f);
            if (System.Math.Abs(r1.Y - r2.Y) != 1)
            { Debug.LogError($"FAIL doors: one tile apart mapped to rows {r1.Y} and {r2.Y}, want a 1-row step"); ok = false; }

            // ---- 5. A hand-resized grid maps PROPORTIONALLY along the same wall -------------------------
            // Inner height doubles from 4 to 8, so the same two tile positions must now be 2 rows apart.
            var wide = new GridBuffer(8, 10);
            var w1 = BattleGridGenerator.ProjectDoorPoint(a, wide, 35f, 63.5f);
            var w2 = BattleGridGenerator.ProjectDoorPoint(a, wide, 35f, 64.5f);
            if (System.Math.Abs(w1.Y - w2.Y) != 2)
            { Debug.LogError($"FAIL doors: on a 10-tall grid the same one-tile step gave rows {w1.Y} and {w2.Y}, want 2 apart"); ok = false; }

            // ---- 6. Nothing was written INTO the buffer ------------------------------------------------
            // This is the whole safety argument: the derived layer must not mutate the DM's cells.
            for (int i = 0; i < buf.Cells.Length; i++)
                if (buf.Cells[i] == GridCell.Door)
                { Debug.LogError($"FAIL doors: ProjectDoors wrote a Door cell into the buffer at index {i}"); ok = false; break; }

            if (ok) Debug.Log("Battle Grid Doors: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Ops")]
        public void SelfTestOps()
        {
            bool ok = true;

            // ---- 1. A 3-wide stamp covers a 3x3 block CENTRED on the cursor cell ------------------------
            // Use an even-sized offset (size/2 without centring) and (4,4) or (6,6) falls outside.
            var buf = new GridBuffer(10, 10);
            var s1 = new BattleGridStroke();
            BattleGridOps.Stamp(buf, s1, 5, 5, 3, GridCell.Wall);
            if (buf.Get(4, 4) != GridCell.Wall || buf.Get(6, 6) != GridCell.Wall || buf.Get(5, 5) != GridCell.Wall)
            { Debug.LogError("FAIL ops: a size-3 stamp at (5,5) did not cover the corners (4,4)/(6,6)"); ok = false; }
            if (buf.Get(3, 5) != GridCell.Empty || buf.Get(7, 5) != GridCell.Empty)
            { Debug.LogError("FAIL ops: a size-3 stamp at (5,5) reached (3,5) or (7,5) — it is 5 wide, not 3"); ok = false; }

            // ---- 2. The stamp records EVERY changed cell once, with its PREVIOUS value ------------------
            if (s1.Indices.Count != 9)
            { Debug.LogError($"FAIL ops: a 3x3 stamp recorded {s1.Indices.Count} cells, want 9"); ok = false; }
            for (int i = 0; i < s1.Previous.Count; i++)
                if (s1.Previous[i] != GridCell.Empty)
                { Debug.LogError($"FAIL ops: recorded previous value {s1.Previous[i]} at slot {i}, want Empty"); ok = false; break; }

            // ---- 3. First touch wins: repainting a cell in the SAME stroke keeps the ORIGINAL previous --
            // Record on every touch instead and undo would restore Wall, not Empty.
            BattleGridOps.Stamp(buf, s1, 5, 5, 1, GridCell.Chasm);
            int centre = buf.Index(5, 5);
            int slot = s1.Indices.IndexOf(centre);
            if (s1.Indices.LastIndexOf(centre) != slot)
            { Debug.LogError("FAIL ops: the centre cell was recorded twice in one stroke"); ok = false; }
            if (s1.Previous[slot] != GridCell.Empty)
            { Debug.LogError($"FAIL ops: after repainting, the centre's recorded previous is {s1.Previous[slot]}, want the ORIGINAL Empty"); ok = false; }

            // ---- 4. Painting a cell with the value it already holds records NOTHING ---------------------
            // Without this an idle click pushes an undo step that visibly does nothing.
            var s2 = new BattleGridStroke();
            BattleGridOps.Stamp(buf, s2, 5, 5, 1, GridCell.Chasm);
            if (!s2.IsEmpty)
            { Debug.LogError("FAIL ops: repainting a cell with its own value produced a non-empty stroke"); ok = false; }

            // ---- 5. Line leaves NO GAP on a steep diagonal ----------------------------------------------
            // Paint only the endpoints and (1,1)..(2,4) stay Empty; this names the first missing cell.
            var lineBuf = new GridBuffer(10, 10);
            BattleGridOps.Line(lineBuf, new BattleGridStroke(), 0, 0, 3, 9, 1, GridCell.Floor);
            for (int y = 0; y <= 9; y++)
            {
                bool any = false;
                for (int x = 0; x <= 3; x++) if (lineBuf.Get(x, y) == GridCell.Floor) { any = true; break; }
                if (!any) { Debug.LogError($"FAIL ops: the line from (0,0) to (3,9) skipped row {y} entirely"); ok = false; break; }
            }

            // ---- 6. Rect fills its interior AND its border, and stops one cell short outside ------------
            var rectBuf = new GridBuffer(10, 10);
            BattleGridOps.Rect(rectBuf, new BattleGridStroke(), 6, 2, 2, 5, GridCell.Rough);   // reversed corners on purpose
            if (rectBuf.Get(2, 2) != GridCell.Rough || rectBuf.Get(6, 5) != GridCell.Rough || rectBuf.Get(4, 3) != GridCell.Rough)
            { Debug.LogError("FAIL ops: Rect did not normalise reversed corners — (2,2)/(6,5)/(4,3) should all be Rough"); ok = false; }
            if (rectBuf.Get(1, 2) != GridCell.Empty || rectBuf.Get(7, 2) != GridCell.Empty || rectBuf.Get(2, 6) != GridCell.Empty)
            { Debug.LogError("FAIL ops: Rect bled one cell past its corners"); ok = false; }

            // ---- 7. Fill spreads by 4-neighbours only, and a diagonal-only gap STOPS it -----------------
            // The pinch is built so 4- and 8-neighbour fills give DIFFERENT results: (0,0) touches (1,1)
            // ONLY diagonally, because (1,0) and (0,1) are Wall. Add the diagonal neighbours and (1,1)
            // floods; keep 4 neighbours and it must not. An open pocket would make this assertion vacuous.
            var fillBuf = new GridBuffer(5, 5);
            fillBuf.Set(1, 0, GridCell.Wall);
            fillBuf.Set(0, 1, GridCell.Wall);
            BattleGridOps.Fill(fillBuf, new BattleGridStroke(), 0, 0, GridCell.Liquid);
            if (fillBuf.Get(0, 0) != GridCell.Liquid)
            { Debug.LogError("FAIL ops: Fill did not even paint its own origin (0,0)"); ok = false; }
            if (fillBuf.Get(1, 1) == GridCell.Liquid)
            { Debug.LogError("FAIL ops: Fill leaked to (1,1) through a diagonal-only pinch — it must use 4 neighbours"); ok = false; }
            if (fillBuf.Get(1, 0) != GridCell.Wall)
            { Debug.LogError("FAIL ops: Fill overwrote the Wall at (1,0) — it must only spread across cells matching the origin"); ok = false; }

            // ---- 7b. ...but it DOES cross an opening -----------------------------------------------------
            // Without this, a fill that only ever paints one cell would satisfy 7 above.
            var openBuf = new GridBuffer(5, 5);
            openBuf.Set(1, 0, GridCell.Wall);
            BattleGridOps.Fill(openBuf, new BattleGridStroke(), 0, 0, GridCell.Liquid);
            if (openBuf.Get(4, 4) != GridCell.Liquid)
            { Debug.LogError("FAIL ops: Fill stopped early — (4,4) is reachable by 4-neighbour steps and should be filled"); ok = false; }

            // ---- 8. Resize anchors BOTTOM-LEFT: old content keeps its coordinates, new cells are Empty --
            // Anchor anywhere else and the marker cell moves.
            var small = new GridBuffer(5, 5);
            small.Set(1, 1, GridCell.Chasm);
            var grown = BattleGridOps.Resize(small, 8, 7);
            if (grown.Width != 8 || grown.Height != 7)
            { Debug.LogError($"FAIL ops: Resize produced {grown.Width}x{grown.Height}, want 8x7"); ok = false; }
            if (grown.Get(1, 1) != GridCell.Chasm)
            { Debug.LogError($"FAIL ops: after growing, the marker at (1,1) is {grown.Get(1, 1)} — content moved"); ok = false; }
            if (grown.Get(7, 6) != GridCell.Empty)
            { Debug.LogError("FAIL ops: a newly added cell (7,6) is not Empty"); ok = false; }

            // ---- 9. Shrinking counts EXACTLY the non-empty cells that fall outside ----------------------
            // Count every dropped cell (including Empty ones) and this reports 16 instead of 2.
            var marked = new GridBuffer(6, 6);
            marked.Set(5, 0, GridCell.Wall);
            marked.Set(0, 5, GridCell.Wall);
            marked.Set(1, 1, GridCell.Floor);        // stays inside — must NOT be counted
            int lost = BattleGridOps.CountLostOnResize(marked, 4, 4);
            if (lost != 2)
            { Debug.LogError($"FAIL ops: shrinking 6x6 to 4x4 reported {lost} lost cells, want exactly 2"); ok = false; }

            if (ok) Debug.Log("Battle Grid Ops: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Undo")]
        public void SelfTestUndo()
        {
            bool ok = true;
            var undo = new BattleGridUndo();

            // ---- 1. Undoing a stroke restores EVERY touched cell to its pre-stroke value ---------------
            var buf = new GridBuffer(6, 6);
            buf.Set(2, 2, GridCell.Floor);
            var stroke = new BattleGridStroke();
            BattleGridOps.Stamp(buf, stroke, 2, 2, 3, GridCell.Wall);
            undo.PushStroke(stroke);
            if (!undo.TryUndo(ref buf))
            { Debug.LogError("FAIL undo: TryUndo returned false with one stroke on the stack"); ok = false; }
            if (buf.Get(2, 2) != GridCell.Floor)
            { Debug.LogError($"FAIL undo: (2,2) came back as {buf.Get(2, 2)}, want the pre-stroke Floor"); ok = false; }
            if (buf.Get(1, 1) != GridCell.Empty)
            { Debug.LogError($"FAIL undo: (1,1) came back as {buf.Get(1, 1)}, want the pre-stroke Empty"); ok = false; }

            // ---- 2. An empty stroke is never pushed ----------------------------------------------------
            var before = undo.Count;
            undo.PushStroke(new BattleGridStroke());
            if (undo.Count != before)
            { Debug.LogError($"FAIL undo: pushing an empty stroke changed the depth {before} -> {undo.Count}"); ok = false; }

            // ---- 3. Undoing a SNAPSHOT restores the old SIZE as well as the content --------------------
            // Store a delta for resize instead and the buffer comes back 8x8 with the old cells shifted.
            var sized = new GridBuffer(5, 5);
            sized.Set(4, 4, GridCell.Chasm);
            var undo2 = new BattleGridUndo();
            undo2.PushSnapshot(sized);
            sized = BattleGridOps.Resize(sized, 8, 8);
            if (!undo2.TryUndo(ref sized))
            { Debug.LogError("FAIL undo: TryUndo returned false with one snapshot on the stack"); ok = false; }
            if (sized.Width != 5 || sized.Height != 5)
            { Debug.LogError($"FAIL undo: after undoing a resize the buffer is {sized.Width}x{sized.Height}, want 5x5"); ok = false; }
            if (sized.Get(4, 4) != GridCell.Chasm)
            { Debug.LogError($"FAIL undo: after undoing a resize (4,4) is {sized.Get(4, 4)}, want Chasm"); ok = false; }

            // ---- 4. A snapshot is a COPY — mutating the live buffer afterwards must not alter it -------
            var undo3 = new BattleGridUndo();
            var live = new GridBuffer(4, 4);
            undo3.PushSnapshot(live);
            live.Set(0, 0, GridCell.Wall);
            undo3.TryUndo(ref live);
            if (live.Get(0, 0) != GridCell.Empty)
            { Debug.LogError("FAIL undo: the snapshot aliased the live buffer — (0,0) survived the undo"); ok = false; }

            // ---- 5. Depth is capped, and it is the OLDEST entry that is dropped ------------------------
            var undo4 = new BattleGridUndo();
            var deep = new GridBuffer(4, 4);
            for (int i = 0; i < BattleGridUndo.MaxDepth + 10; i++)
            {
                var st = new BattleGridStroke();
                st.Paint(deep, 0, 0, (GridCell)(1 + i % 6));
                undo4.PushStroke(st);
            }
            if (undo4.Count != BattleGridUndo.MaxDepth)
            { Debug.LogError($"FAIL undo: depth is {undo4.Count} after {BattleGridUndo.MaxDepth + 10} pushes, want the {BattleGridUndo.MaxDepth} cap"); ok = false; }
            int steps = 0;
            while (undo4.TryUndo(ref deep)) steps++;
            if (steps != BattleGridUndo.MaxDepth)
            { Debug.LogError($"FAIL undo: unwound {steps} steps, want {BattleGridUndo.MaxDepth}"); ok = false; }

            // The counts above are identical under "drop oldest" and "drop newest" — only the RESTORED
            // VALUE tells them apart. Entries 10..73 survive the cap, so the last undo restores what
            // stroke 9 painted: 1 + 9 % 6 = 4 = Rough. Under drop-newest, entries 0..73 minus the
            // rejected tail survive, the final restore yields entry 0's recorded Empty, and this fails.
            if (deep.Get(0, 0) != GridCell.Rough)
            { Debug.LogError($"FAIL undo: after unwinding a capped stack, (0,0) is {deep.Get(0, 0)}, want Rough — the OLDEST entries must be the ones dropped"); ok = false; }

            if (undo4.TryUndo(ref deep))
            { Debug.LogError("FAIL undo: TryUndo succeeded on an empty stack"); ok = false; }

            if (ok) Debug.Log("Battle Grid Undo: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Count")]
        public void SelfTestCount()
        {
            bool ok = true;
            var floor = new InteriorFloor { NextRoomId = 4 };
            floor.Rooms.Add(new Room { Id = 1, TypeId = 1, Grid = new GridBuffer(4, 4).ToModel() });
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1 });
            floor.Rooms.Add(new Room { Id = 3, TypeId = 1, Grid = new GridBuffer(5, 5).ToModel() });

            int n = BattleGridGenerator.CountGrids(floor);
            if (n != 2)
            { Debug.LogError($"FAIL count: a floor with 2 painted maps out of 3 rooms reported {n}"); ok = false; }
            if (BattleGridGenerator.CountGrids(null) != 0)
            { Debug.LogError("FAIL count: a null floor did not report 0"); ok = false; }

            if (ok) Debug.Log("Battle Grid Count: PASS");
        }

        [ContextMenu("Self-Test: Battle Grid Authored Content")]
        public void SelfTestAuthored()
        {
            bool ok = true;

            // ---- 1. A generated floor with NO battle map is not "authored" ------------------------------
            // This is the guard that keeps the regenerate confirm from firing on every press.
            var floor = new InteriorFloor { NextRoomId = 3 };
            var room = new Room { Id = 1, TypeId = 1, SizeW = 6, SizeH = 6 };
            floor.Rooms.Add(room);
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 6, SizeH = 6 });
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });   // a generator-made link: Authored stays false
            if (DungeonOps.HasAuthoredContent(floor))
            { Debug.LogError("FAIL authored: a floor with no titles, no authored links and no grids counted as authored"); ok = false; }

            // ---- 2. A room carrying a battle map IS authored --------------------------------------------
            // Drop the Grid check and «Перегенерировать этаж» silently destroys painted maps.
            room.Grid = new GridBuffer(4, 4).ToModel();
            if (!DungeonOps.HasAuthoredContent(floor))
            { Debug.LogError("FAIL authored: a room with Grid != null did not count as authored content"); ok = false; }

            if (ok) Debug.Log("Battle Grid Authored Content: PASS");
        }
    }
}
