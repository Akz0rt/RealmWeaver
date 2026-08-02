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
    /// BEING RIGHT ABOUT UNITY HERE IS ENTIRELY A QUESTION OF *WHEN*. A keystroke reaches the field at one
    /// exact point in the frame: EventSystem.Update runs the input module, whose ProcessNavigation sends
    /// updateSelectedHandler to the selected object (InputSystemUIInputModule.cs:807), and
    /// TMP_InputField.OnUpdateSelected (TMP_InputField.cs:2342) then drains the whole IMGUI event queue with
    /// Event.PopEvent (:2350), applying every character queued since the last drain and raising
    /// onValueChanged for each. Call that moment THE DRAIN. Two values the keys below are decided by
    /// (Tab needs neither) sat on opposite sides of it:
    ///   • DocBlock.Text, which DocBlockView.OnFieldChanged writes from onValueChanged, i.e. DURING the drain,
    ///     and which DocKeyboardOps reads LIVE off the page;
    ///   • the caret, which this class caches for itself.
    /// The drain happens in an Update, and Unity gives no ordering between two components' Update methods, so
    /// a cache filled from this class's own Update could be either side of it. That mismatch was the bug the
    /// DM reported as «в новую строку переносится последний символ из предыдущей»: with the EventSystem's
    /// Update running first, the drain applied the final typed character (text length n) and then the Enter,
    /// which deactivated the field — so FindFocusedRow returned null, no refresh happened, and the caret left
    /// over from the previous frame still said n-1. DocKeyboardOps.OnEnter (DocKeyboardOps.cs:88) saw
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
    /// INSIDE it, so they are taken only where that movement is invisible. None of these collisions is new
    /// and consuming in Update avoided none of them; it only decided, for SOME of them, which Update ordering
    /// they surfaced in. Tab's happened in both — the tab character reached DocBlock.Text either way, and the
    /// ordering chose only whether the DM saw it at once or at the next rebuild. This is what the whole set
    /// looks like once the coin-flip is gone.
    /// </summary>
    public class DocKeyboardController : MonoBehaviour
    {
        public DocumentPageView pageView;

        // Refreshed in LateUpdate from whichever row is focused THEN — i.e. after the drain, so the caret
        // already includes this frame's typing and the block text it indexes into is final.
        //
        // On the frame Enter is pressed there is no focused row to read: Enter on any line type but
        // MultiLineNewline returns EditState.Finish (TMP_InputField.cs:2249-2256), which deactivates the
        // field and clears m_AllowInput — isFocused goes false, so FindFocusedRow returns null. The fallback
        // below therefore takes DocBlockView.CaretWhenEditingEnded, sampled inside onEndEdit.
        //
        // WHY THAT IS STILL THE RIGHT SOURCE UNDER TMP, THOUGH THE ORIGINAL REASON IS GONE. Legacy InputField
        // zeroed the caret immediately after firing onEndEdit, so the callback was the only instant the value
        // still existed. TMP does not: its zeroing lines are commented out in the shipped source
        // (TMP_InputField.cs:4443-4444), and onEndEdit reaches us from ReleaseSelection (:4410) rather than
        // from DeactivateInputField at all. Reading the deactivated field directly would therefore work
        // today — and would be depending on the continued ABSENCE of a reset that two uncommented lines
        // would restore. The sampled value costs nothing and does not make that bet.
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

            // Something is over the page and owns the keys — today the Ctrl+K palette, in either of its two
            // roles. This class polls the hardware directly (see the class doc for why), and so does the
            // palette, and a row stays "focused" in the cache below after its field is deactivated: without
            // this line, one Enter would choose a row in the palette AND split the row behind it. Undo is
            // gated too, deliberately — the DM pressing Ctrl+Z at a search prompt means the prompt.
            if (pageView.KeyboardSuspended) return;

            var live = FindFocusedRow();

            // A PICTURE IS "FOCUSED" BY BEING SELECTED. It has no field to focus, so the page holds that
            // state (DocumentPageView.SelectedBlockId) and this class reads it — which is how a Backspace
            // aimed at an image the DM merely CLICKED reaches DocKeyboardOps' delete branch, and not only one
            // they arrowed into. A text row taking focus ends the selection: the two are the same idea for
            // two kinds of row, and both cannot be true at once.
            if (live != null) pageView.SetSelectedBlock(null);
            else if (!string.IsNullOrEmpty(pageView.SelectedBlockId))
            {
                lastFocusedId = pageView.SelectedBlockId;
                lastCaret = 0;
            }

            // Undo and redo, before every early return below: they must work with nothing focused at all,
            // which is exactly the state the DM is in right after an undo rebuilt the page. Neither
            // TMP_InputField nor legacy uGUI implements Ctrl+Z of its own, so there is no second handler to
            // fight over the key — unlike the wheel, which had one.
            if (keyboard.ctrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
            {
                bool shifted = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                if (keyboard.zKey.wasPressedThisFrame)
                {
                    if (shifted) pageView.Redo(); else pageView.Undo();
                    return;
                }
                if (keyboard.yKey.wasPressedThisFrame) { pageView.Redo(); return; }
            }

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

            // The page needs the same answer for its toolbar, and taking it from here rather than working it
            // out again is what keeps "where is the caret" a question with one owner. See
            // DocumentPageView.NoteFocus for why the page needs the LAST focus rather than the live one.
            pageView.NoteFocus(lastFocusedId, lastCaret);

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
                // Backspace at offset 0 deletes nothing at all (TMP_InputField.cs:3162, whose every branch
                // requires stringPositionInternal or caretPositionInternal > 0) and so raises no
                // onValueChanged — "this row's text did not
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
            // TMP_InputField.cs:2224/:2230 call MoveUp/MoveDown — so stealing the key as well would do two things
            // at once: move the caret AND jump to another block. Consuming in LateUpdate made that double
            // action reliable instead of ordering-dependent, which is how it was caught.
            //
            // On a single-line row the same double action is harmless and invisible: LineUpCharacterPosition
            // returns 0 for a caret already on the first line and LineDownCharacterPosition returns
            // text.Length for one on the last, so the field only walks the caret to the start or the end of
            // the row we are leaving anyway, and the row is repainted without focus regardless. That is
            // the whole reason the narrow version is safe where the wide one was not. A wrapped row now keeps
            // its arrows entirely — the caret moves inside it and focus stays — which is the behaviour the
            // field gives for free. Under legacy Text this narrowing was also the only option, because the
            // wrapping could not be measured from outside the class at all; TMP removed that obstacle (see
            // DocBlockView.IsSingleVisualLine) but not the reason, which is that two layers must not both act
            // on one keystroke. The gate answers "cannot prove it" as false, so an unmeasurable row is left
            // to the field too.
            if (keyboard.upArrowKey.wasPressedThisFrame)
            { if (VerticalIsOurs(live)) Handle(DocKey.Up); return; }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            { if (VerticalIsOurs(live)) Handle(DocKey.Down); return; }
        }

        /// <summary>Whether a vertical arrow belongs to this class rather than to the field. THREE conditions,
        /// and each one removes cross-block arrow navigation from a situation where it used to happen — worth
        /// listing in full, because the wrapped row is the only one of the three that is obvious:
        ///   • `view != null` — no row is being edited. Arrows used to move between blocks anyway, off the
        ///     cached id of whatever was focused last, exactly as Backspace used to merge them; same
        ///     tightening, same reason (a block move is a structural act and the DM must be inside a block to
        ///     ask for it), and the same asymmetry with Enter, which still acts on the row it just tore down.
        ///   • `!CaretPending` — the 1-2 frames between FocusAt and the frame that places the caret. The
        ///     field is showing either SelectAll's whole-text selection or a caret not yet moved to the offset
        ///     this class asked for, so no reading of "where is the DM" is worth acting on. Arrows in that
        ///     window are DROPPED, not deferred; the alternative is acting on a position the DM cannot see.
        ///     AdoptFocus used to keep them alive here by guessing the wrapping, which is what round 1 removed.
        ///   • `IsSingleVisualLine()` — the wrapped row, where the field has already moved the caret inside
        ///     the row and taking the key as well would do two things at once. Also false whenever the row
        ///     cannot be measured, by that method's own guarantee.
        /// The first two are microseconds wide in practice; the third is the one the DM can feel.</summary>
        static bool VerticalIsOurs(DocBlockView view)
            => view != null && !view.CaretPending && view.IsSingleVisualLine();

        /// <summary>Whether anything is HIGHLIGHTED — the caret pair, which is the one the DM can see.
        ///
        /// DELIBERATELY NOT TMP'S OWN NOTION. The field decides "is there a selection" from its STRING pair
        /// instead (TMP_InputField.cs:996), and those two can disagree; that disagreement is the leading
        /// suspect for the phantom onValueChanged that OnFieldChanged now filters out. Widening this method to
        /// refuse whenever EITHER pair differs was written and then withdrawn, because it would block exactly
        /// the keystroke the filter exists to let through — one fix undoing the other, in the same commit.
        /// The rule that stands is the visible one: a Backspace with something highlighted belongs to the
        /// field, and "highlighted" means what the DM sees.</summary>
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
            // THE PAGE AS IT IS BEFORE THE KEY, kept only long enough to find out whether the key changed
            // anything. Apply mutates the list in place, so there is no reading the old state afterwards —
            // and pushing UNCONDITIONALLY would fill the history with steps for keys that did nothing (a
            // refused Tab, a Backspace mid-word that falls through to the field), each of which would look to
            // the DM like a Ctrl+Z that did nothing at all. `Rebuild` is precisely "the block list changed
            // shape", so it is the honest gate. The copy costs a hundred small objects on a structural key.
            var before = DocHistory.Copy(page.Blocks);

            var result = DocKeyboardOps.Apply(page.Blocks, lastFocusedId, lastCaret,
                                              atFirstLine: true, atLastLine: true, key);

            if (!result.Handled) return;

            if (result.Rebuild)
            {
                pageView.PushHistoryOf(before, lastFocusedId, lastCaret);
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
                // A picture cannot take a caret, so it takes SELECTION instead — the first half of the
                // two-step DocKeyboardOps.OnBackspace describes: this press selects it, the next one deletes
                // it. AdoptFocus as well as the ring, because the delete branch keys off lastFocusedId.
                AdoptFocus(result.SelectBlockId, 0);
                pageView.SetSelectedBlock(result.SelectBlockId);
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
        /// a frame or two (FocusAt only queues ActivateInputField; the field takes focus on a later frame of
        /// its own). A key pressed inside that window would be answered with the
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
