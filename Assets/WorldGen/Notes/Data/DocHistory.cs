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
    /// WHY SNAPSHOTS AND NOT COMMANDS. NotesUndoManager is a command stack, and every command it owns knows
    /// only how to UNDO itself — there is no redo path in it at all, and none can be retrofitted, because a
    /// command like "the object was deleted" cannot replay what it destroyed. Р2's toolbar carries ↷ as well
    /// as ↶, so the page needs a history that can go both ways. A document is also small — a session sheet
    /// is on the order of a hundred short blocks — so copying the list costs less than the bookkeeping an
    /// inverse-command per operation would need, and it cannot drift from the document the way a hand-written
    /// inverse can. The canvas keeps NotesUndoManager exactly as it is; this is a second history for a second
    /// kind of page, not a replacement.
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
    /// at blocks from outside the document, and the canvas side already lost this exact fight once
    /// (NotesUndoManager.DeleteObjectCommand's own note: "re-created object gets a new Id").
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
                });
            }
            return copy;
        }
    }
}
