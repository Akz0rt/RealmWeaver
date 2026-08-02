using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>One remembered state of a page: its whole block list, plus where the caret was when that
    /// state was left. The caret travels with it because an undo that restores the text but drops the DM
    /// somewhere else makes them find their place again after every keystroke they take back.</summary>
    public class DocSnapshot
    {
        public List<DocBlock> Blocks = new List<DocBlock>();
        public string FocusId;
        /// <summary>-1 means the end of the focused block's text, the same convention DocKeyResult uses.</summary>
        public int Caret = -1;
    }

    /// <summary>
    /// Undo and redo for a document page, as whole-list snapshots.
    ///
    /// WHY SNAPSHOTS AND NOT COMMANDS. The canvas used to carry its own command stack (NotesUndoManager,
    /// deleted in Р4), and every command in it knew only how to UNDO itself — there was no redo path and none
    /// could be retrofitted, because a command like "the object was deleted" cannot replay what it destroyed.
    /// Р2's toolbar carries ↷ as well as ↶, so the page needs a history that can go both ways. A document is
    /// also small — a session sheet is on the order of a hundred short blocks — so copying the list costs less
    /// than the bookkeeping an inverse-command per operation would need, and it cannot drift from the document
    /// the way a hand-written inverse can.
    ///
    /// Р4 then removed the second stack outright: a board lives INSIDE a DocBlock now, so a page snapshot
    /// already contains it, and two stacks on one page would have made Ctrl+Z mean different things depending
    /// on where the mouse was. This is a deliberate reversal of Р2's "the canvas keeps NotesUndoManager
    /// exactly as it is", not a forgotten decision — the premise it rested on (a board is a separate KIND of
    /// page) is gone.
    ///
    /// WHAT COUNTS AS ONE STEP. Every structural change is one — a new row, a split, a merge, an indent, a
    /// kind change, an inserted image or link. TYPING is one step per visit to a row: the state is remembered
    /// as the DM's first keystroke lands in a row and not again until they leave it and come back. So Ctrl+Z
    /// returns a row to what it was when they entered it, which is a rule that can be said in one sentence —
    /// and Р2 deliberately has no timer coalescing typing into paragraph-sized steps, because a window nobody
    /// can verify offline is a tunable, not a rule. If the DM wants finer steps, that is a one-line change to
    /// where Push is called from, not to anything here.
    ///
    /// IDS SURVIVE. Copy carries DocBlock.Id verbatim rather than letting the field initializer mint a new
    /// one — the id is promised stable across "edit, reorder and undo" because П6's session state will point
    /// at blocks from outside the document, and the canvas side already lost this exact fight once: its
    /// command stack "undid" a deletion by re-creating the object with a fresh id, which broke every link that
    /// had pointed at it.
    /// </summary>
    public class DocHistory
    {
        /// <summary>How many steps back the page remembers. Deep enough that no session of writing reaches
        /// it, shallow enough that a hundred copies of a hundred small blocks is not a memory question.</summary>
        public const int MaxEntries = 100;

        readonly List<DocSnapshot> past = new List<DocSnapshot>();
        readonly List<DocSnapshot> future = new List<DocSnapshot>();

        public bool CanUndo => past.Count > 0;
        public bool CanRedo => future.Count > 0;

        /// <summary>Remembers the state a change is about to be made TO. Called before the mutation, never
        /// after — the snapshot is what Ctrl+Z goes back to.
        ///
        /// A NEW CHANGE ENDS THE FUTURE. Undoing three steps and then typing means the three redos describe a
        /// document that no longer exists, so they are dropped rather than left to restore text the DM has
        /// since replaced.</summary>
        public void Push(IReadOnlyList<DocBlock> blocks, string focusId, int caret)
        {
            if (blocks == null) return;
            past.Add(new DocSnapshot { Blocks = Copy(blocks), FocusId = focusId, Caret = caret });
            future.Clear();
            if (past.Count > MaxEntries) past.RemoveAt(0);
        }

        /// <summary>The state to restore, or null when there is nothing to go back to. The CURRENT state is
        /// handed in and becomes the redo entry, which is what makes redo possible without every operation
        /// having to describe its own inverse.</summary>
        public DocSnapshot Undo(IReadOnlyList<DocBlock> current, string focusId, int caret)
        {
            if (past.Count == 0 || current == null) return null;
            future.Add(new DocSnapshot { Blocks = Copy(current), FocusId = focusId, Caret = caret });
            var snapshot = past[past.Count - 1];
            past.RemoveAt(past.Count - 1);
            return snapshot;
        }

        /// <summary>Undo's mirror: the state to restore, or null when the DM has not undone anything since
        /// their last change.</summary>
        public DocSnapshot Redo(IReadOnlyList<DocBlock> current, string focusId, int caret)
        {
            if (future.Count == 0 || current == null) return null;
            past.Add(new DocSnapshot { Blocks = Copy(current), FocusId = focusId, Caret = caret });
            var snapshot = future[future.Count - 1];
            future.RemoveAt(future.Count - 1);
            return snapshot;
        }

        /// <summary>Forgets everything. Called when the page being shown changes: a history taken against one
        /// page's blocks would, applied to another, replace that page with this one's content.</summary>
        public void Clear()
        {
            past.Clear();
            future.Clear();
        }

        /// <summary>A deep copy of the list, EVERY field carried.
        ///
        /// A field added to DocBlock and forgotten here would not fail loudly — it would quietly reset to its
        /// default on the DM's next undo, which is the shape of bug nobody finds until the data is already
        /// gone. DocHistorySelfTests fills every field with a non-default value and reads them all back for
        /// exactly that reason.
        ///
        /// ImageBytes is shared rather than cloned: nothing in the notes layer ever writes INTO that array —
        /// an image is replaced whole or not at all — and copying a few megabytes per keystroke to defend
        /// against a mutation that does not exist would be the expensive half of a bargain with no other
        /// half.</summary>
        public static List<DocBlock> Copy(IReadOnlyList<DocBlock> blocks)
        {
            var copy = new List<DocBlock>(blocks != null ? blocks.Count : 0);
            if (blocks == null) return copy;

            foreach (var b in blocks)
            {
                if (b == null) continue;
                copy.Add(new DocBlock
                {
                    Id = b.Id,
                    Kind = b.Kind,
                    Depth = b.Depth,
                    Text = b.Text,
                    Detail = b.Detail,
                    LinkedPageId = b.LinkedPageId,
                    Collapsed = b.Collapsed,
                    ImageBytes = b.ImageBytes,
                    DisplayHeight = b.DisplayHeight,
                    DisplayWidth = b.DisplayWidth,
                    CanvasPan = b.CanvasPan,
                    CanvasZoom = b.CanvasZoom,
                    CanvasObjects = CopyObjects(b.CanvasObjects),
                    CanvasLinks = CopyLinks(b.CanvasLinks),
                });
            }
            return copy;
        }

        /// <summary>A deep copy of a canvas block's objects — the reason this method exists at all.
        ///
        /// Without it, dragging a card would move the VERY instance the snapshot points at, and Ctrl+Z would
        /// silently do nothing: the "restored" state and the current one would be the same objects. Byte
        /// arrays are SHARED, for exactly the reason DocBlock.ImageBytes is (see Copy's own doc) — an image or
        /// a drawing is replaced whole, never written into.
        ///
        /// SIZE IS ASSIGNED AFTER CONSTRUCTION, and that is not style. NoteCardData's and DrawingObjectData's
        /// constructors both WRITE Size, so a card or a drawing the DM has resized would come back from an
        /// undo at its default size if the copy left that to them.
        ///
        /// A NEW CanvasObjectData SUBCLASS MUST BE ADDED HERE. The default arm drops what it does not
        /// recognise, which is silent data loss on undo; DocHistorySelfTests.SelfTestCanvasBlockDeepCopy pins
        /// the three subclasses that exist.</summary>
        static List<CanvasObjectData> CopyObjects(IReadOnlyList<CanvasObjectData> objects)
        {
            if (objects == null) return null;
            var copy = new List<CanvasObjectData>(objects.Count);
            foreach (var o in objects)
            {
                if (o == null) continue;
                CanvasObjectData c;
                switch (o)
                {
                    case NoteCardData n:    c = new NoteCardData { Title = n.Title, Body = n.Body }; break;
                    case ImageObjectData i: c = new ImageObjectData { ImageBytes = i.ImageBytes }; break;
                    case DrawingObjectData d:
                        c = new DrawingObjectData(d.PixelWidth, d.PixelHeight) { PixelDataPng = d.PixelDataPng };
                        break;
                    default: continue;
                }
                c.Id = o.Id;
                c.Position = o.Position;
                c.Size = o.Size;
                copy.Add(c);
            }
            return copy;
        }

        /// <summary>A deep copy of a canvas block's links. ControlPointOffset is a Vector2? and travels as it
        /// is — a link whose bend the DM adjusted must come back bent, not straightened.</summary>
        static List<LinkData> CopyLinks(IReadOnlyList<LinkData> links)
        {
            if (links == null) return null;
            var copy = new List<LinkData>(links.Count);
            foreach (var l in links)
            {
                if (l == null) continue;
                copy.Add(new LinkData
                {
                    Id = l.Id,
                    FromObjectId = l.FromObjectId,
                    ToObjectId = l.ToObjectId,
                    Directed = l.Directed,
                    ControlPointOffset = l.ControlPointOffset,
                });
            }
            return copy;
        }
    }
}
