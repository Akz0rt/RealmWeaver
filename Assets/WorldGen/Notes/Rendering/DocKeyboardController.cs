using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Translates real keystrokes into DocKey values, hands them to the pure DocKeyboardOps, and applies the
    /// intent that comes back. Deliberately holds no rules of its own — everything about what a key MEANS is
    /// tested offline in DocKeyboardOpsSelfTests, and this class only has to be right about Unity.
    ///
    /// BEING RIGHT ABOUT UNITY HERE IS ENTIRELY A QUESTION OF *WHEN*. A keystroke reaches the legacy
    /// InputField at one exact point in the frame: EventSystem.Update runs the input module, whose
    /// ProcessNavigation sends updateSelectedHandler to the selected object
    /// (InputSystemUIInputModule.cs:807), and InputField.OnUpdateSelected (InputField.cs:2023) then drains the
    /// whole IMGUI event queue with Event.PopEvent, applying every character queued since the last drain and
    /// raising onValueChanged for each. Call that moment THE DRAIN. Two values the keys below are decided by
    /// (Tab needs neither) sat on opposite sides of it:
    ///   • DocBlock.Text, which DocBlockView.OnFieldChanged writes from onValueChanged, i.e. DURING the drain,
    ///     and which DocKeyboardOps reads LIVE off the page;
    ///   • the caret, which this class caches for itself.
    /// The drain happens in an Update, and Unity gives no ordering between two components' Update methods, so
    /// a cache filled from this class's own Update could be either side of it. That mismatch was the bug the
    /// DM reported as «в новую строку переносится последний символ из предыдущей»: with the EventSystem's
    /// Update running first, the drain applied the final typed character (text length n) and then the Enter,
    /// which deactivated the field — so FindFocusedRow returned null, no refresh happened, and the caret left
    /// over from the previous frame still said n-1. DocKeyboardOps.OnEnter (DocKeyboardOps.cs:81) saw
    /// caretOffset < text.Length, took its mid-text branch, and NotesDocOps.SplitAt moved that one character
    /// into the new row. It needs no unusual typing speed: the final character and the Enter only have to
    /// arrive in the same drain, and at the frame rate a loaded Editor scene runs at they routinely do.
    ///
    /// So every key is now consumed in LateUpdate, which Unity runs after EVERY Update in the frame and
    /// therefore after the drain, in either ordering. Both values are then read from the same side of it.
    /// Script Execution Order would also pin the ordering and is rejected: it lives in ProjectSettings, is
    /// invisible at this call site, and the next person to reorder scripts for an unrelated reason would
    /// silently break this again.
    ///
    /// THE PRICE OF ACTING AFTER THE DRAIN, and the rule the rest of this class follows: by the time we run,
    /// the field has already acted on the same keystroke — nothing here consumes an event or hides it from
    /// uGUI. So every key has to be checked for whether it can share. Enter can (the field only deactivates).
    /// Tab can, now that DocBlockView refuses the tab CHARACTER. Backspace cannot be told apart by the caret
    /// alone, because the field's own Backspace moves it — hence the "did this row's text change in this
    /// drain" test below. Up/Down cannot be shared at all on a wrapped row, because the field moves the caret
    /// INSIDE it, so they are taken only where that movement is invisible. None of that is new — consuming in
    /// Update did not avoid a single one of these collisions, it only made each happen in one Update ordering
    /// and not the other. This is what they look like once the coin-flip is gone.
    /// </summary>
    public class DocKeyboardController : MonoBehaviour
    {
        public DocumentPageView pageView;

        // Refreshed in LateUpdate from whichever row is focused THEN — i.e. after the drain, so the caret
        // already includes this frame's typing and the block text it indexes into is final.
        //
        // On the frame Enter is pressed there is no focused row to read: that same Enter has already made the
        // field run DeactivateInputField (InputField.cs:3237), which clears m_AllowInput (isFocused false, so
        // FindFocusedRow returns null), fires onEndEdit, and only then sets m_CaretPosition =
        // m_CaretSelectPosition = 0. Reading the dead field would give 0 and split the entire row away. The
        // fallback below therefore takes DocBlockView.CaretWhenEditingEnded, sampled inside that onEndEdit —
        // the last instant in the drain at which the caret is still the DM's.
        //
        // A focused row is also skipped while its caret is still PENDING (DocBlockView.CaretPending): between
        // FocusAt and the frame that applies it, the field reports the caret SelectAll left behind, not the
        // one this class asked for. AdoptFocus already put the requested offset in the cache, and that value
        // stands until the field can be believed again.
        string lastFocusedId;
        int lastCaret;

        public string FocusedBlockId => lastFocusedId;

        void LateUpdate()
        {
            if (pageView == null || pageView.Page == null) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var live = FindFocusedRow();
            if (live != null && !live.CaretPending)
            {
                lastFocusedId = live.BlockId;
                lastCaret = live.Field != null ? live.Field.caretPosition : 0;
            }
            else if (live == null)
            {
                // No live row: either an Enter just tore the field down (the case this exists for) or the DM
                // clicked away. Both leave CaretWhenEditingEnded holding the caret as of the moment that row
                // stopped being edited, which is the only honest answer for a key aimed at it.
                var ended = pageView.ViewOf(lastFocusedId);
                if (ended != null && ended.CaretWhenEditingEnded >= 0) lastCaret = ended.CaretWhenEditingEnded;
            }

            if (string.IsNullOrEmpty(lastFocusedId)) return;

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // wasPressedThisFrame still answers truthfully this late in the frame — which is what makes
            // consuming from LateUpdate possible at all. It compares against InputUpdate.s_UpdateStepCount
            // (ButtonControl.cs:296), and that advances once per input update, in EarlyUpdate ahead of every
            // Update; nothing advances it again between Update and LateUpdate.
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            { Handle(DocKey.Enter); return; }

            if (keyboard.tabKey.wasPressedThisFrame)
            { Handle(shift ? DocKey.ShiftTab : DocKey.Tab); return; }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                // Only the very start of the text is ours; anywhere else Backspace is ordinary text editing.
                // "The caret is at 0" is not enough to tell those apart from here, because the field did its
                // OWN Backspace in the drain a moment ago and that is what put the caret at 0: deleting the
                // 'b' of "abc" with the caret at 1 leaves the caret at 0, and merging on that reading would
                // fold a row into the one above it when the DM only meant to delete a character. The field's
                // Backspace at offset 0 deletes nothing at all (InputField.cs:2335 needs
                // caretPositionInternal > 0) and so raises no onValueChanged — "this row's text did not
                // change in this frame's drain" is exactly what separates our case from the field's.
                //
                // `live != null` is a deliberate tightening, not a null-guard: Backspace pressed while NO row
                // is being edited used to merge two rows anyway, off the cached id of whatever was focused
                // last. Merging two blocks is a structural edit and the DM must be inside one of them to ask
                // for it. Note this makes Backspace stricter than Enter, which still acts on the just-ended
                // row through CaretWhenEditingEnded — that asymmetry is the point: Enter's row was torn down
                // by Enter itself a moment ago, Backspace's was abandoned.
                if (live != null && lastCaret == 0 && !live.TextChangedThisFrame && !HasSelection(live))
                    Handle(DocKey.Backspace);
                return;
            }

            // Up/Down are ours ONLY on a row whose whole text is one visual line. On any other row the field
            // has already moved the caret within the row during the drain — the UpArrow/DownArrow cases at
            // InputField.cs:1936/:1942 call MoveUp/MoveDown — so stealing the key as well would do two things
            // at once: move the caret AND jump to another block. Consuming in LateUpdate made that double
            // action reliable instead of ordering-dependent, which is how it was caught.
            //
            // On a single-line row the same double action is harmless and invisible: LineUpCharacterPosition
            // returns 0 for a caret already on the first line and LineDownCharacterPosition returns
            // text.Length for one on the last, so the field only walks the caret to the start or the end of
            // the row we are leaving anyway, and the row is repainted without focus regardless. That is
            // the whole reason the narrow version is safe where the wide one was not. A wrapped row now keeps
            // its arrows entirely — the caret moves inside it and focus stays — which is the behaviour uGUI
            // gives for free and the only one that can be right without InputField's private m_DrawStart.
            // The gate answers "cannot prove it" as false, so an unmeasurable row is left to the field too.
            if (keyboard.upArrowKey.wasPressedThisFrame)
            { if (VerticalIsOurs(live)) Handle(DocKey.Up); return; }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            { if (VerticalIsOurs(live)) Handle(DocKey.Down); return; }
        }

        /// <summary>Whether a vertical arrow belongs to this class rather than to the field. Two conditions:
        /// the row's caret must actually have been placed — while it is still PENDING the field is showing
        /// either SelectAll's whole-text selection or a caret this class has not yet moved to the offset it
        /// asked for, so no reading of "where is the DM" is worth acting on — and the row must be provably a
        /// single visual line.</summary>
        static bool VerticalIsOurs(DocBlockView view)
            => view != null && !view.CaretPending && view.IsSingleVisualLine();

        static bool HasSelection(DocBlockView view)
            => view != null && view.Field != null
               && view.Field.selectionAnchorPosition != view.Field.selectionFocusPosition;

        void Handle(DocKey key)
        {
            var page = pageView.Page;
            // atFirstLine/atLastLine are constants from THIS caller: a vertical key only reaches here from a
            // row VerticalIsOurs proved to be a single visual line, where the caret is on the first and the
            // last one at once. DocKeyboardOps keeps both parameters, and DocKeyboardOpsSelfTests still pins
            // the false branches, because the rule "an arrow inside a wrapped row is not a block move" belongs
            // to the pure layer even though the view can no longer be the one to report it.
            var result = DocKeyboardOps.Apply(page.Blocks, lastFocusedId, lastCaret,
                                              atFirstLine: true, atLastLine: true, key);
            if (!result.Handled) return;

            if (result.Rebuild)
            {
                string focusId = result.FocusBlockId ?? lastFocusedId;
                AdoptFocus(focusId, result.CaretOffset);
                pageView.RebuildAndFocus(focusId, result.CaretOffset);
                return;
            }

            if (!string.IsNullOrEmpty(result.FocusBlockId))
            {
                AdoptFocus(result.FocusBlockId, result.CaretOffset);
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

            // Handled but nothing to do — a refused Tab. Re-assert focus anyway: the frame's navigation has
            // already been dispatched by the time this runs (see the class doc), so if Unity's own focus
            // navigation moved the selection, this is the first moment at which putting it back sticks.
            var current = pageView.ViewOf(lastFocusedId);
            if (current != null) current.FocusAt(lastCaret);
        }

        /// <summary>Moves the cache onto the row focus is being handed to, in the same statement that hands it
        /// over. Without this the caret would go on describing the row the DM has just LEFT until the next
        /// LateUpdate managed to refresh it — and that refresh needs the new field to be focused, which costs
        /// a frame or two (FocusAt only queues ActivateInputField; InputField.LateUpdate is what actually
        /// takes focus, InputField.cs:1442). A key pressed inside that window would be answered with the
        /// previous row's caret. Nothing about line wrapping is cached alongside it: the vertical keys measure
        /// that at the moment they are pressed, off the live row, so there is no second value here to keep
        /// honest.</summary>
        void AdoptFocus(string blockId, int caretOffset)
        {
            lastFocusedId = blockId;
            if (caretOffset >= 0) { lastCaret = caretOffset; return; }

            // caretOffset < 0 is DocKeyResult's "put it at the end" — resolve it against the text now rather
            // than leaving a negative in the cache, which OnEnter would read as offset 0 and split on.
            // pageView.Page needs no guard: LateUpdate returns early without one, and it is Handle's only
            // caller, which is AdoptFocus's only caller.
            var block = pageView.Page.Blocks.Find(b => b.Id == blockId);
            lastCaret = block != null ? (block.Text ?? "").Length : 0;
        }

        DocBlockView FindFocusedRow()
        {
            foreach (var row in pageView.Rows)
                if (row != null && row.Field != null && row.Field.isFocused) return row;
            return null;
        }
    }
}
