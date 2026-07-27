using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>Editing keys a document row understands. Deliberately not UnityEngine.KeyCode: the decision
    /// of what a key DOES belongs in this pure layer, and the view's only job is to translate a real keystroke
    /// into one of these and then apply the result.</summary>
    public enum DocKey { Enter, Backspace, Tab, ShiftTab, Up, Down }

    /// <summary>What the view must do after a key was handled.</summary>
    public class DocKeyResult
    {
        /// <summary>False means the keystroke was not consumed — the view must let it fall through to the
        /// InputField (so ordinary typing, and Backspace mid-word, still work normally).</summary>
        public bool Handled;
        /// <summary>The block list changed shape, so rows must be rebuilt rather than just re-focused.</summary>
        public bool Rebuild;
        /// <summary>Where the caret should end up. Null means leave focus where it is.</summary>
        public string FocusBlockId;
        /// <summary>Caret position inside FocusBlockId; -1 means the end of its text.</summary>
        public int CaretOffset = -1;
        /// <summary>A block with no text field (Image, BoardRef) that should be shown as SELECTED instead of
        /// focused — pressing Backspace again then deletes it.</summary>
        public string SelectBlockId;

        public static DocKeyResult NotHandled => new DocKeyResult { Handled = false };
    }

    /// <summary>
    /// The keyboard behaviour of a document page, as pure functions. This is the fiddliest part of the whole
    /// feature and the part legacy uGUI helps with least, so the RULES live here where they can be tested
    /// exhaustively offline. What cannot be tested here — whether the caret visually lands where it should,
    /// whether uGUI actually moves focus, how it all FEELS to type in — is exactly what the user checkpoint is
    /// for, and nothing else.
    ///
    /// Every method mutates the block list through NotesDocOps, so the invariants live in one place.
    /// </summary>
    public static class DocKeyboardOps
    {
        /// <param name="caretOffset">Caret position inside the focused block's text; ignored for blocks that
        /// have no text field.</param>
        /// <param name="atFirstLine">True when the caret sits on the focused field's FIRST visual line — only
        /// the view can know this, since it depends on wrapping.</param>
        /// <param name="atLastLine">Likewise for the last visual line.</param>
        public static DocKeyResult Apply(List<DocBlock> blocks, string focusedBlockId, int caretOffset,
                                        bool atFirstLine, bool atLastLine, DocKey key)
        {
            if (blocks == null) return DocKeyResult.NotHandled;
            int i = IndexOf(blocks, focusedBlockId);
            if (i < 0) return DocKeyResult.NotHandled;

            switch (key)
            {
                case DocKey.Enter:     return OnEnter(blocks, i, caretOffset);
                case DocKey.Backspace: return OnBackspace(blocks, i, caretOffset);
                case DocKey.Tab:       return OnIndent(blocks, i, outward: false);
                case DocKey.ShiftTab:  return OnIndent(blocks, i, outward: true);
                case DocKey.Up:        return atFirstLine ? OnVertical(blocks, i, caretOffset, -1) : DocKeyResult.NotHandled;
                case DocKey.Down:      return atLastLine ? OnVertical(blocks, i, caretOffset, +1) : DocKeyResult.NotHandled;
            }
            return DocKeyResult.NotHandled;
        }

        static DocKeyResult OnEnter(List<DocBlock> blocks, int i, int caretOffset)
        {
            var b = blocks[i];

            // A block with no text field: Enter starts a row under it rather than doing nothing.
            if (!HasText(b))
            {
                var row = NotesDocOps.NewBlock(BlockKind.Item, b.Depth);
                NotesDocOps.Insert(blocks, i + 1, row);
                return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = row.Id, CaretOffset = 0 };
            }

            string text = b.Text ?? "";
            if (caretOffset < 0) caretOffset = 0;
            if (caretOffset > text.Length) caretOffset = text.Length;

            // Mid-text: split, and both halves keep the kind — splitting a heading yields two headings.
            if (caretOffset < text.Length)
            {
                var created = NotesDocOps.SplitAt(blocks, b.Id, caretOffset);
                if (created == null) return DocKeyResult.NotHandled;
                return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = created.Id, CaretOffset = 0 };
            }

            // At the end of a SECTION heading, the DM wants to start listing under it, not to open a second
            // heading — so this is the one place the new block's kind differs from the old one's. It also lands
            // as the heading's FIRST child (i + 1), whereas a row's new sibling goes after the row's whole
            // subtree so its children stay attached to the row they were written under.
            bool fromSection = b.Kind == BlockKind.Section;
            var next = NotesDocOps.NewBlock(fromSection ? BlockKind.Item : b.Kind, fromSection ? 1 : b.Depth);
            NotesDocOps.Insert(blocks, fromSection ? i + 1 : i + NotesDocOps.SubtreeLength(blocks, i), next);
            return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = next.Id, CaretOffset = 0 };
        }

        static DocKeyResult OnBackspace(List<DocBlock> blocks, int i, int caretOffset)
        {
            var b = blocks[i];

            // A selected Image or BoardRef is deleted outright — the second press of the two-step described
            // below. Focus falls back to whatever row precedes it.
            if (!HasText(b))
            {
                string previousId = i > 0 ? blocks[i - 1].Id : null;
                NotesDocOps.RemoveWithChildren(blocks, b.Id);
                return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = previousId, CaretOffset = -1 };
            }

            // Anywhere but the very start of the text, Backspace is ordinary text editing.
            if (caretOffset != 0) return DocKeyResult.NotHandled;
            if (i == 0) return DocKeyResult.NotHandled;

            // There is no sensible way to merge text INTO a picture, so the first press selects it instead.
            var prev = blocks[i - 1];
            if (!HasText(prev))
                return new DocKeyResult { Handled = true, SelectBlockId = prev.Id };

            if (!NotesDocOps.MergeWithPrevious(blocks, b.Id, out int caret))
                return DocKeyResult.NotHandled;

            return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = prev.Id, CaretOffset = caret };
        }

        static DocKeyResult OnIndent(List<DocBlock> blocks, int i, bool outward)
        {
            var b = blocks[i];
            bool moved = outward ? NotesDocOps.Outdent(blocks, b.Id) : NotesDocOps.Indent(blocks, b.Id);
            if (!moved) return new DocKeyResult { Handled = true };   // consumed, so Unity's own focus
                                                                      // navigation never steals Tab
            return new DocKeyResult { Handled = true, Rebuild = true, FocusBlockId = b.Id, CaretOffset = -1 };
        }

        /// <summary>Moves focus to the previous or next VISIBLE block, keeping the horizontal caret position
        /// as closely as the target's length allows. Rows hidden inside a collapsed section are skipped —
        /// arrowing into a row the DM cannot see would be a bug, not a feature.</summary>
        static DocKeyResult OnVertical(List<DocBlock> blocks, int i, int caretOffset, int direction)
        {
            var visible = NotesDocOps.VisibleIndices(blocks);
            int at = visible.IndexOf(i);
            if (at < 0) return DocKeyResult.NotHandled;

            int targetAt = at + direction;
            if (targetAt < 0 || targetAt >= visible.Count) return DocKeyResult.NotHandled;

            var target = blocks[visible[targetAt]];
            if (!HasText(target))
                return new DocKeyResult { Handled = true, SelectBlockId = target.Id };

            int caret = caretOffset;
            int len = (target.Text ?? "").Length;
            if (caret < 0 || caret > len) caret = len;
            return new DocKeyResult { Handled = true, FocusBlockId = target.Id, CaretOffset = caret };
        }

        static bool HasText(DocBlock b) => b.Kind != BlockKind.Image && b.Kind != BlockKind.BoardRef;

        static int IndexOf(IReadOnlyList<DocBlock> blocks, string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return -1;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].Id == blockId) return i;
            return -1;
        }
    }
}
