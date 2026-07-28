using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Translates real keystrokes into DocKey values, hands them to the pure DocKeyboardOps, and applies the
    /// intent that comes back. Deliberately holds no rules of its own — everything about what a key MEANS is
    /// tested offline in DocKeyboardOpsSelfTests, and this class only has to be right about Unity.
    /// </summary>
    public class DocKeyboardController : MonoBehaviour
    {
        public DocumentPageView pageView;

        // The focused row is remembered rather than looked up fresh on the frame a key fires. Enter makes the
        // InputField raise onEndEdit and DEACTIVATE itself, so by the time this Update runs the field may
        // already report isFocused == false — looking it up live would drop the keystroke.
        string lastFocusedId;
        int lastCaret;
        bool lastAtFirstLine = true;
        bool lastAtLastLine = true;

        public string FocusedBlockId => lastFocusedId;

        void Update()
        {
            if (pageView == null || pageView.Page == null) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var live = FindFocusedRow();
            if (live != null)
            {
                lastFocusedId = live.BlockId;
                lastCaret = live.Field != null ? live.Field.caretPosition : 0;
                lastAtFirstLine = live.CaretOnFirstLine;
                lastAtLastLine = live.CaretOnLastLine;
            }

            if (string.IsNullOrEmpty(lastFocusedId)) return;

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            { Handle(DocKey.Enter); return; }

            if (keyboard.tabKey.wasPressedThisFrame)
            { Handle(shift ? DocKey.ShiftTab : DocKey.Tab); return; }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                // Only the very start of the text is ours. Anywhere else Backspace is ordinary text editing
                // and must reach the field untouched — and at offset 0 it would delete nothing anyway, so
                // reading the caret after the field's own update is still correct.
                if (lastCaret == 0 && !HasSelection(live)) Handle(DocKey.Backspace);
                return;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame && lastAtFirstLine)
            { Handle(DocKey.Up); return; }

            if (keyboard.downArrowKey.wasPressedThisFrame && lastAtLastLine)
            { Handle(DocKey.Down); return; }
        }

        static bool HasSelection(DocBlockView view)
            => view != null && view.Field != null
               && view.Field.selectionAnchorPosition != view.Field.selectionFocusPosition;

        void Handle(DocKey key)
        {
            var page = pageView.Page;
            var result = DocKeyboardOps.Apply(page.Blocks, lastFocusedId, lastCaret,
                                              lastAtFirstLine, lastAtLastLine, key);
            if (!result.Handled) return;

            if (result.Rebuild)
            {
                string focusId = result.FocusBlockId ?? lastFocusedId;
                lastFocusedId = focusId;
                pageView.RebuildAndFocus(focusId, result.CaretOffset);
                return;
            }

            if (!string.IsNullOrEmpty(result.FocusBlockId))
            {
                lastFocusedId = result.FocusBlockId;
                var view = pageView.ViewOf(result.FocusBlockId);
                if (view != null) view.FocusAt(result.CaretOffset);
                return;
            }

            if (!string.IsNullOrEmpty(result.SelectBlockId))
            {
                // Images and cards have no text field to focus; selection is theirs (task 7/8). Until those
                // land, the keystroke is still swallowed so nothing else grabs it.
                return;
            }

            // Handled but nothing to do — a refused Tab. Re-assert focus anyway, because Unity's own focus
            // navigation may have moved the selection on the very same frame.
            var current = pageView.ViewOf(lastFocusedId);
            if (current != null) current.FocusAt(lastCaret);
        }

        DocBlockView FindFocusedRow()
        {
            foreach (var row in pageView.Rows)
                if (row != null && row.Field != null && row.Field.isFocused) return row;
            return null;
        }
    }
}
