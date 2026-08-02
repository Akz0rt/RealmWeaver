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

        /// <summary>A canvas block's contents are copied DEEP.
        ///
        /// The fixture is built so that a copy-by-reference and a real copy give DIFFERENT answers: after the
        /// snapshot is taken we move a card, resize a drawing and straighten a link, then ask the snapshot
        /// whether it noticed. It must not have. Without that, dragging a card would move the very instance
        /// the snapshot points at and Ctrl+Z would silently do nothing — the shape of defect that looks like
        /// the feature simply not being wired.</summary>
        [ContextMenu("Self-Test: Canvas Block Deep Copy")]
        public void SelfTestCanvasBlockDeepCopy()
        {
            bool ok = true;

            var card = new NoteCardData { Title = "Ольга", Body = "ждёт у ворот" };
            card.Position = new System.Numerics.Vector2(10f, 20f);
            var drawing = new DrawingObjectData(256, 128);
            drawing.Size = new System.Numerics.Vector2(300f, 150f);   // stretched by hand, unlike the constructor's
            var link = new LinkData
            {
                FromObjectId = card.Id,
                ToObjectId = drawing.Id,
                Directed = false,
                ControlPointOffset = new System.Numerics.Vector2(7f, -3f),
            };

            var canvas = NotesDocOps.NewBlock(BlockKind.Canvas, 1, "Связи");
            canvas.CanvasObjects = new List<CanvasObjectData> { card, drawing };
            canvas.CanvasLinks = new List<LinkData> { link };
            canvas.CanvasPan = new System.Numerics.Vector2(-40f, 12f);
            canvas.CanvasZoom = 1.5f;
            canvas.DisplayWidth = 640f;
            canvas.DisplayHeight = 400f;

            var blocks = new List<DocBlock> { NotesDocOps.NewBlock(BlockKind.Section, 0, "S"), canvas };
            var copy = DocHistory.Copy(blocks);

            if (ReferenceEquals(copy[1].CanvasObjects, canvas.CanvasObjects))
            { Debug.LogError("FAIL deep copy: CanvasObjects is the SAME list instance — undo would restore what it is undoing"); ok = false; }
            if (ReferenceEquals(copy[1].CanvasObjects[0], card))
            { Debug.LogError("FAIL deep copy: the card itself is shared, not copied"); ok = false; }

            // Mutate the ORIGINAL after the snapshot. The copy must not have noticed.
            card.Position = new System.Numerics.Vector2(999f, 999f);
            drawing.Size = new System.Numerics.Vector2(1f, 1f);
            link.ControlPointOffset = null;
            canvas.CanvasZoom = 3f;

            var copiedCard = copy[1].CanvasObjects[0] as NoteCardData;
            if (copiedCard == null || copiedCard.Position.X != 10f || copiedCard.Position.Y != 20f)
            { Debug.LogError($"FAIL deep copy: card position = {copiedCard?.Position}, want (10, 20)"); ok = false; }
            if (copiedCard == null || copiedCard.Id != card.Id || copiedCard.Title != "Ольга" || copiedCard.Body != "ждёт у ворот")
            { Debug.LogError($"FAIL deep copy: card identity/text lost (id match={copiedCard?.Id == card.Id}, title={copiedCard?.Title})"); ok = false; }

            var copiedDrawing = copy[1].CanvasObjects[1] as DrawingObjectData;
            if (copiedDrawing == null || copiedDrawing.Size.X != 300f || copiedDrawing.Size.Y != 150f)
            { Debug.LogError($"FAIL deep copy: drawing size = {copiedDrawing?.Size}, want (300, 150) — the constructor's own Size must be overwritten after it runs"); ok = false; }
            if (copiedDrawing == null || copiedDrawing.PixelWidth != 256 || copiedDrawing.PixelHeight != 128)
            { Debug.LogError($"FAIL deep copy: drawing pixels = {copiedDrawing?.PixelWidth}x{copiedDrawing?.PixelHeight}, want 256x128"); ok = false; }

            var copiedLink = copy[1].CanvasLinks[0];
            if (copiedLink.ControlPointOffset == null
                || copiedLink.ControlPointOffset.Value.X != 7f || copiedLink.ControlPointOffset.Value.Y != -3f)
            { Debug.LogError($"FAIL deep copy: link bend = {copiedLink.ControlPointOffset}, want (7, -3)"); ok = false; }
            if (copiedLink.Directed)
            { Debug.LogError("FAIL deep copy: link Directed came back true, want false"); ok = false; }
            if (copiedLink.Id != link.Id || copiedLink.FromObjectId != card.Id || copiedLink.ToObjectId != drawing.Id)
            { Debug.LogError("FAIL deep copy: link identity/endpoints lost"); ok = false; }

            if (copy[1].CanvasZoom != 1.5f || copy[1].CanvasPan.X != -40f || copy[1].CanvasPan.Y != 12f)
            { Debug.LogError($"FAIL deep copy: pan/zoom = {copy[1].CanvasPan}/{copy[1].CanvasZoom}, want (-40, 12)/1.5"); ok = false; }
            if (copy[1].DisplayWidth != 640f || copy[1].DisplayHeight != 400f)
            { Debug.LogError($"FAIL deep copy: size = {copy[1].DisplayWidth}x{copy[1].DisplayHeight}, want 640x400"); ok = false; }

            Debug.Log(ok ? "Self-Test Canvas Block Deep Copy: PASS" : "Self-Test Canvas Block Deep Copy: FAIL");
        }
    }
}
