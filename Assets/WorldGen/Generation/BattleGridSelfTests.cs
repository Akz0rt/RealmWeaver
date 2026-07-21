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
    }
}
