using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Self-tests for the page's undo/redo history. Runs two ways, as every suite here does: right-click this
    /// component in the Editor, or offline via Tools/notes-harness.
    ///
    /// The assertions target what a change would BREAK — the identity of a block across a round trip, every
    /// field of a copy, the future being dropped by a new change — rather than counting entries, which is a
    /// number that stays right while the content goes wrong.
    /// </summary>
    public class DocHistorySelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Document History")]
        public void SelfTestDocHistory()
        {
            bool ok = true;

            var blocks = new List<DocBlock>
            {
                NotesDocOps.NewBlock(BlockKind.Section, 0),
                NotesDocOps.NewBlock(BlockKind.Item, 1),
            };
            blocks[0].Text = "Сцена";
            blocks[1].Text = "Ольга ждёт у ворот";
            string sectionId = blocks[0].Id;
            string itemId = blocks[1].Id;

            var history = new DocHistory();
            if (history.CanUndo || history.CanRedo)
            { Debug.LogError("FAIL: a fresh history must offer neither undo nor redo"); ok = false; }

            // One step: remember, then change.
            history.Push(blocks, itemId, 3);
            blocks[1].Text = "Ольга ушла";

            var back = history.Undo(blocks, itemId, 10);
            if (back == null || back.Blocks.Count != 2 || back.Blocks[1].Text != "Ольга ждёт у ворот")
            { Debug.LogError($"FAIL: undo gave \"{(back != null && back.Blocks.Count > 1 ? back.Blocks[1].Text : "—")}\", want the text as it was before the change"); ok = false; }

            // THE INVARIANT THAT OUTLIVES Р2: a block's id is promised stable across edit, reorder AND undo,
            // because П6's session state will point at blocks from outside the document. A copy written with
            // an object initializer that forgets Id would mint a fresh Guid here and nothing would complain.
            if (back != null && back.Blocks.Count == 2 && (back.Blocks[0].Id != sectionId || back.Blocks[1].Id != itemId))
            { Debug.LogError("FAIL: undo changed a block's Id — every outside reference to it is now dangling"); ok = false; }

            // The caret travels with the state.
            if (back != null && (back.FocusId != itemId || back.Caret != 3))
            { Debug.LogError($"FAIL: undo restored focus {back.FocusId}/{back.Caret}, want {itemId}/3"); ok = false; }

            // Redo returns the state undo was called FROM — including its caret, which was 10 there.
            var forward = history.Redo(back != null ? back.Blocks : blocks, back != null ? back.FocusId : null, back != null ? back.Caret : -1);
            if (forward == null || forward.Blocks.Count != 2 || forward.Blocks[1].Text != "Ольга ушла")
            { Debug.LogError($"FAIL: redo gave \"{(forward != null && forward.Blocks.Count > 1 ? forward.Blocks[1].Text : "—")}\", want the state undo left"); ok = false; }
            if (forward != null && (forward.FocusId != itemId || forward.Caret != 10))
            { Debug.LogError($"FAIL: redo restored focus {forward.FocusId}/{forward.Caret}, want {itemId}/10"); ok = false; }
            if (forward != null && forward.Blocks[1].Id != itemId)
            { Debug.LogError("FAIL: redo changed a block's Id"); ok = false; }

            // A NEW CHANGE ENDS THE FUTURE. Undo, then type: the redo described a document that no longer
            // exists, and offering it would restore text the DM has since replaced.
            var h2 = new DocHistory();
            h2.Push(blocks, itemId, 0);
            var undone = h2.Undo(blocks, itemId, 0);
            if (!h2.CanRedo)
            { Debug.LogError("FAIL: after an undo there must be something to redo"); ok = false; }
            h2.Push(undone != null ? undone.Blocks : blocks, itemId, 0);
            if (h2.CanRedo)
            { Debug.LogError("FAIL: a new change must drop the redo future"); ok = false; }

            // Nothing to go back to is null, not an exception and not an empty snapshot that would blank the
            // page — the caller shows a disabled button from CanUndo and never acts on a null.
            var empty = new DocHistory();
            if (empty.Undo(blocks, itemId, 0) != null || empty.Redo(blocks, itemId, 0) != null)
            { Debug.LogError("FAIL: undo/redo on an empty history must give null"); ok = false; }

            // THE COPY IS DEEP. The live list keeps being edited after a Push, and a shallow copy would let
            // those edits reach into the remembered state — an undo that restores what it was asked to undo.
            var h3 = new DocHistory();
            h3.Push(blocks, itemId, 0);
            blocks[1].Text = "изменено после снимка";
            blocks.Add(NotesDocOps.NewBlock(BlockKind.Item, 1));
            var snap = h3.Undo(blocks, itemId, 0);
            if (snap == null || snap.Blocks.Count != 2 || snap.Blocks[1].Text != "Ольга ушла")
            { Debug.LogError($"FAIL: the snapshot followed the live list ({(snap != null ? snap.Blocks.Count : -1)} blocks)"); ok = false; }

            // EVERY FIELD SURVIVES. A field added to DocBlock and forgotten in Copy resets to its default on
            // the DM's next undo, silently.
            var full = new DocBlock
            {
                Kind = BlockKind.Image,
                Depth = 2,
                Text = "подпись",
                Detail = "развёрнутая заметка",
                LinkedPageId = "page-7",
                Collapsed = true,
                ImageBytes = new byte[] { 1, 2, 3 },
                DisplayHeight = 180f,
            };
            var copied = DocHistory.Copy(new List<DocBlock> { full });
            var c = copied.Count == 1 ? copied[0] : null;
            if (c == null || c.Id != full.Id || c.Kind != BlockKind.Image || c.Depth != 2 || c.Text != "подпись" ||
                c.Detail != "развёрнутая заметка" || c.LinkedPageId != "page-7" || !c.Collapsed ||
                c.ImageBytes == null || c.ImageBytes.Length != 3 || c.DisplayHeight != 180f)
            { Debug.LogError("FAIL: Copy dropped a field — an undo would silently reset it"); ok = false; }

            // The cap trims the OLDEST step, so the history stays usable rather than stopping at the limit.
            var h4 = new DocHistory();
            var one = new List<DocBlock> { NotesDocOps.NewBlock(BlockKind.Item, 0) };
            for (int i = 0; i <= DocHistory.MaxEntries + 5; i++)
            {
                one[0].Text = $"шаг {i}";
                h4.Push(one, one[0].Id, 0);
            }
            var oldest = h4.Undo(one, one[0].Id, 0);
            if (!h4.CanUndo || oldest == null || oldest.Blocks[0].Text != $"шаг {DocHistory.MaxEntries + 5}")
            { Debug.LogError($"FAIL: past the cap, undo gave \"{(oldest != null ? oldest.Blocks[0].Text : "—")}\" — the NEWEST step must still be the first one back"); ok = false; }

            // Clearing is what happens when the shown page changes: a history taken against one page's blocks
            // would, applied to another, replace that page's content with this one's.
            h4.Clear();
            if (h4.CanUndo || h4.CanRedo)
            { Debug.LogError("FAIL: Clear left something behind"); ok = false; }

            // Null lists are ordinary inputs, not crashes: the page can be asked to remember before one is set.
            var h5 = new DocHistory();
            h5.Push(null, null, 0);
            if (h5.CanUndo)
            { Debug.LogError("FAIL: pushing a null list must remember nothing"); ok = false; }
            if (DocHistory.Copy(null).Count != 0)
            { Debug.LogError("FAIL: copying null must give an empty list, never throw"); ok = false; }

            Debug.Log(ok ? "Self-Test Document History: PASS" : "Self-Test Document History: FAIL");
        }
    }
}
