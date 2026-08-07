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
        /// <summary>Задача 5 арки «две страницы рядом». ОТКУДА БЕРЁТСЯ ВИД, на который этот класс
        /// действует. Раньше здесь лежала прямая ссылка на единственный DocumentPageView, которую
        /// WorkspaceBuilder проводил к панели 0; с задачи 4 видов два, по одному на панель, и «тот самый»
        /// вид перестал существовать. Маршрутизатор отвечает на это покадрово — см. его собственный
        /// класс-док про то, почему опрос каретки, а не поле «текущий вид».
        ///
        /// Проводится WorkspaceBuilder'ом ОДИН раз на пересборку оболочки, а не по разу на каждый
        /// созданный вид: маршрутизатор не привязан ни к какой панели, поэтому крючок OnViewCreated ему не
        /// нужен. Null в сцене без оболочки — LateUpdate тогда просто ничего не делает.</summary>
        public PageFocusRouter router;

        /// <summary>Вид, которому принадлежат клавиши В ЭТОМ КАДРЕ, — снимок `router.ActiveView()`,
        /// сделанный в начале LateUpdate, чтобы все нижние методы одного кадра работали с ОДНИМ и тем же
        /// видом (иначе клик, случившийся между двумя вызовами, развёл бы их по разным панелям).
        ///
        /// ОН ЖЕ — ХОЗЯИН КЭША `lastFocusedId`/`lastCaret` ниже. Отдельного поля «для какого вида сняты
        /// эти значения» нет намеренно: это была бы вторая копия одного факта, свободная разойтись с
        /// первой (любимая ошибка этого проекта, см. WorkspaceController.shellSuppressed). Смена вида
        /// обнаруживается сравнением НОВОГО ответа маршрутизатора с тем, что лежит здесь, ДО перезаписи —
        /// см. AdoptView.</summary>
        DocumentPageView pageView;

        /// <summary>Задача 10б. Wired by WorkspaceBuilder, same shape as `router` above and at the same
        /// moment — a plain public field an outside builder assigns, not a constructor argument, because this
        /// class is itself AddComponent-ed. Null in a scene with no workspace shell to attach it; every use
        /// below is null-guarded for exactly that reason.</summary>
        public MentionPopup mentionPopup;

        /// <summary>Задача 10б, фикс-раунд 3. `Time.frameCount` as of the last LateUpdate where the popup's
        /// own anchor row was CONFIRMED live-focused — written every such frame in UpdateMentionPopup, and
        /// read only when it currently is NOT (see that method's own doc for the two cases this discriminates
        /// between). Stamped fresh at the moment a popup opens too, since at that instant the row is, by
        /// construction, the one that was just typed in.</summary>
        int mentionAnchorLastFocusedFrame = -1;

        /// <summary>How many consecutive un-focused frames the popup survives before "the anchor row hasn't
        /// been focused in a while" is read as "the DM clicked away", not "mid-refocus" — see
        /// UpdateMentionPopup's own doc for what each of those two readings protects.</summary>
        const int MentionGraceFrames = 2;

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
            // Задача 5: сначала — КОМУ принадлежат клавиши этого кадра, и только потом всё остальное.
            var active = router != null ? router.ActiveView() : null;
            if (active == null) return;

            // Смена вида проверяется ДО проверки на пустую страницу: панель может показывать вид, к
            // которому ещё не привязана страница (заглушка «страницы нет»), и кэш всё равно обязан
            // переехать на неё — иначе он остался бы висеть на прошлой панели и следующий же Ctrl+Z ушёл
            // бы не туда.
            if (pageView != active) AdoptView(active);
            if (pageView.Page == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Задача 10б: снимок ДО того, как что-либо ниже перезаписывает кэш — нужен, чтобы
            // UpdateMentionPopup умел отличить «"@" набран ИМЕННО в этом кадре» от каретки, просто
            // оказавшейся рядом со старым «@» уже в тексте (клик мышью, повторный визит в строку).
            string prevFocusedId = lastFocusedId;
            int prevCaret = lastCaret;

            // Что-то ДРУГОЕ вот-вот заберёт клавиатуру (Ctrl+K, Ctrl+F) — попап «@» обязан закрыться
            // СЕЙЧАС, а не повиснуть на экране: ранний return чуть ниже не даёт этому классу больше ничего
            // сделать в этом кадре, в т.ч. закрыть попап каким-либо путём из обычного потока ниже.
            if (mentionPopup != null && mentionPopup.IsOpen && pageView.KeyboardSuspended) mentionPopup.Close();

            // Something is over the page and owns the keys — today the Ctrl+K palette, in either of its two
            // roles. This class polls the hardware directly (see the class doc for why), and so does the
            // palette, and a row stays "focused" in the cache below after its field is deactivated: without
            // this line, one Enter would choose a row in the palette AND split the row behind it. Undo is
            // gated too, deliberately — the DM pressing Ctrl+Z at a search prompt means the prompt.
            if (pageView.KeyboardSuspended) return;

            var live = FindFocusedRow();

            // ── Задача 10б, фикс-раунд 2 (ROOT CAUSE) ── Перехват Enter/↑/↓/Esc идёт САМЫМ ПЕРВЫМ, до
            // ЧЕГО БЫ ТО НИ БЫЛО, что могло бы закрыть попап как побочный эффект. Раньше этот блок стоял
            // ПОСЛЕ UpdateMentionPopup — и UpdateMentionPopup закрывал попап просто потому, что ПОЛЕ УЖЕ
            // НЕ В ФОКУСЕ этим кадром (Enter — MultiLineSubmit деактивирует поле ДО того, как этот
            // LateUpdate вообще стартует, см. класс-док про «THE DRAIN» — FindFocusedRow() вернул null
            // ЕЩЁ СТРОКОЙ ВЫШЕ) или потому, что КАРЕТКА УЖЕ ПЕРЕСТАВЛЕНА TMP-дрейном (↑/↓ на однострочной
            // строке). «Был ли попап открыт К НАЧАЛУ этого кадра» — единственный вопрос, который эти
            // четыре клавиши обязаны задавать; что успело произойти С ПОЛЕМ за то же самое нажатие не
            // должно на него влиять.
            bool popupWasOpen = mentionPopup != null && mentionPopup.IsOpen;

            // Esc closes REGARDLESS of whether there is anything to choose — it dismisses the POPUP, not a
            // selection inside it — so it stays unconditional even where Up/Down/Enter below are not (фикс-
            // раунд 3, находка №3). Рулинг 4 говорит буквально про текст («не трогает набранное») —
            // восстанавливаем и каретку туда, где она была ДО этого Esc, тем же приёмом, что и стрелки, а не
            // оставляем её там, куда мог утащить чужой обработчик Escape/Cancel в этот же кадр.
            if (popupWasOpen && keyboard.escapeKey.wasPressedThisFrame)
            { mentionPopup.Close(); RestoreCaretAfterPopupKey(live, prevCaret); return; }

            // Up/Down/Enter belong to the popup only while it has something to OFFER — «Начните печатать
            // имя…» (пустой запрос, ничего не найдено) has no row a highlight could land on and nothing an
            // Enter could insert. Гейт на «есть выбор», а не на «попап открыт», — иначе все три клавиши
            // молча проглатывались бы попапом (фикс-раунд 3, находка №3), а ДМ ожидал бы их обычного
            // поведения (навигация между блоками, разбить строку) ровно как если бы попапа не было вовсе.
            if (popupWasOpen && mentionPopup.HasChoice)
            {
                if (keyboard.downArrowKey.wasPressedThisFrame)
                { mentionPopup.MoveHighlight(1); RestoreCaretAfterPopupKey(live, prevCaret); return; }
                if (keyboard.upArrowKey.wasPressedThisFrame)
                { mentionPopup.MoveHighlight(-1); RestoreCaretAfterPopupKey(live, prevCaret); return; }
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                { mentionPopup.ChooseHighlighted(); return; }
            }

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

            // Задача 10б: открыть/сузить/закрыть попап «@» по live-тексту строки (или, если поле этим
            // кадром не в фокусе, по авторитетному DocBlock.Text — см. её собственный класс-док). Enter/
            // ↑/↓/Esc уже перехвачены ВЫШЕ, до этого места — сюда они не доходят, пока попап открыт.
            UpdateMentionPopup(live, prevFocusedId, prevCaret);

            // Задача «персонажи», финальный ревью: узкий гейт на Enter/Tab, ничего больше. Четыре поля
            // карточки персонажа («Кто»/«Где искать»/«Чего хочет»/«Как играть») — TMP_InputField с
            // MultiLineNewline, а НЕ строка страницы: FindFocusedRow() выше (см. её же тело в конце
            // файла) сканирует только pageView.Rows и на них не смотрит, так что `live` уже null, пока каретка сидит
            // в поле шапки, а `lastFocusedId` тем временем всё ещё называет строку прозы, в которой ДМ
            // был ДО клика в шапку (кэш обновляет только последний if/else above, и ни один из них не
            // видит поле шапки как «строку»). Без этой проверки Enter/Tab в поле шапки доходили бы до
            // Handle() ниже и правили бы ЧУЖУЮ строку позади шапки — ровно та MultiLineNewline-вставка
            // новой строки внутри поля, которую CharacterHeaderView.BuildFieldRow объявляет «свободным
            // текстом» (field.lineType = TMP_InputField.LineType.MultiLineNewline), молча ловила бы
            // побочным эффектом ещё и разбиение строки прозы.
            //
            // СОЗНАТЕЛЬНО НЕ ЧЕРЕЗ pageView.KeyboardSuspended (см. её же класс-док чуть выше, :149): тот
            // флаг обрывает LateUpdate ЦЕЛИКОМ, ДО блока Ctrl+Z (:203) и до CharacterHeaderView.Update,
            // где живёт Ctrl+V-вставка портрета, — оба ДМ уже проверил и подтвердил рабочими. Добавление
            // сюда своего собственного условия, а не переиспользование/расширение KeyboardSuspended —
            // единственный способ выключить именно Enter/Tab, не выключив по пути их тоже. НЕ
            // «упрощайте» это обратно в KeyboardSuspended — это и есть тот самый баг.
            //
            // Backspace/↑/↓ ниже это не задевает: все трое уже гейтуются на `live != null` (Backspace —
            // напрямую, ↑/↓ — через VerticalIsOurs), а `live` для поля шапки и так null — им это
            // условие не нужно.
            //
            // Известное смежное ограничение, СОЗНАТЕЛЬНО не трогается здесь: Ctrl+Z с кареткой в поле
            // шапки всё равно уводит фокус в строку прозы (см. CharacterHeaderView.OnValueChanged,
            // комментарий про LastFocusedBlockId) — сама отмена карточки работает, ДМ это проверил;
            // ломать этот путь сужением ниже нельзя.
            if (pageView.CharacterHeaderOwnsKeys) return;

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
        /// <summary>Задача 5. Клавиши перешли к ДРУГОМУ виду — переносим на него всё, что переживает кадр.
        ///
        /// ПОЧЕМУ ЭТО ОБЯЗАТЕЛЬНО. `lastFocusedId`/`lastCaret` описывают строку КОНКРЕТНОГО вида. Оставить
        /// их при смене вида — значит получить самый вероятный дефект этой задачи: отмена в правой панели
        /// восстанавливает фокус на строке из левой (DocumentPageView.Undo читает LastFocusedBlockId, а
        /// хвост LateUpdate пишет туда этот самый кэш КАЖДЫЙ кадр).
        ///
        /// НО И ОБНУЛЯТЬ ИХ НЕЛЬЗЯ — это ломает ровно то же самое с другого конца. Тот же безусловный
        /// `pageView.NoteFocus(lastFocusedId, lastCaret)` в конце LateUpdate записал бы (null, 0) в НОВЫЙ
        /// вид в первый же кадр после переключения — и стёр бы его собственную память о том, где стояла
        /// каретка, которой пользуются и Undo/Redo, и панель инструментов (ConvertFocused,
        /// InsertTokenAtCaret). ДМ увидел бы это как «щёлкнул по вкладке — и кнопки заголовка перестали
        /// что-либо делать».
        ///
        /// Поэтому кэш ПЕРЕНИМАЕТСЯ у самого вида: DocumentPageView.LastFocusedBlockId и есть та самая
        /// «где была каретка в ЭТОЙ странице», которую этот класс туда и записал, когда вид был активен.
        /// Так поведение каждой панели по отдельности остаётся ровно тем же, что было у единственного вида
        /// до арки. `LastFocusedCaret` инициализируется как -1 («не знаем»), а отрицательная каретка ниже
        /// по коду читается как «конец строки»/0 в разных местах — поэтому здесь она зажимается в 0 сразу,
        /// а не оставляется двусмысленной.
        ///
        /// ПОПАП «@» ЗАКРЫВАЕТСЯ, потому что его якорь — строка ПРОШЛОГО вида: показывать список над чужой
        /// панелью нечестно, а вставлять выбранное было бы уже прямой порчей. Он закрылся бы и сам одним
        /// шагом позже (UpdateMentionPopup не нашёл бы блок якоря в новой странице), но полагаться на
        /// побочный эффект там, где речь о записи в документ, не стоит. Окно отсрочки в 2 кадра тоже
        /// принадлежало прошлому виду — его отметка сбрасывается вместе с попапом.</summary>
        void AdoptView(DocumentPageView view)
        {
            pageView = view;
            lastFocusedId = view.LastFocusedBlockId;
            lastCaret = Mathf.Max(0, view.LastFocusedCaret);
            mentionAnchorLastFocusedFrame = -1;
            if (mentionPopup != null && mentionPopup.IsOpen) mentionPopup.Close();
        }

        void AdoptFocus(string blockId, int caretOffset)
        {
            lastFocusedId = blockId;
            if (caretOffset >= 0) { lastCaret = caretOffset; return; }

            // caretOffset < 0 is DocKeyResult's "put it at the end" — resolve it against the text now rather
            // than leaving a negative in the cache, which OnEnter would read as offset 0 and split on.
            //
            // pageView.Page needs no guard, and the reason is narrower than it used to be stated. This
            // method has TWO callers: Handle (which only runs from inside LateUpdate, past its `pageView
            // .Page == null` return) and NoteExternalTokenInsertion, called from MentionPopup.Choose during
            // the Update phase, i.e. outside that protection. The second one is safe not because of where it
            // runs but because of WHAT IT PASSES: `start + token.Length`, which is never negative, so it
            // returns one line above and never reaches this branch. Keep that true — a caller passing a
            // negative offset from outside LateUpdate is the one way to make this line throw.
            var block = pageView.Page.Blocks.Find(b => b.Id == blockId);
            lastCaret = block != null ? (block.Text ?? "").Length : 0;
        }

        DocBlockView FindFocusedRow()
        {
            foreach (var row in pageView.Rows)
                if (row != null && row.Field != null && row.Field.isFocused) return row;
            return null;
        }

        /// <summary>Задача 10б, фикс-раунд 2 (ROOT CAUSE FIX) + фикс-раунд 3 (находка №1). Derives the
        /// popup's open/refresh/close state from the row's TEXT — never from "is the field focused THIS
        /// instant". Enter/↑/↓/Esc are intercepted ABOVE, before this method ever runs on the frame they
        /// arrive, so `live == null` reaching this method is no longer caused by either of them.
        ///
        /// THAT LEAVES TWO GENUINELY DIFFERENT THINGS BEHIND ONE SIGNAL (`live == null`), and фикс-раунд 3
        /// is what tells them apart, by TIME rather than by cause:
        ///   (а) THE ANCHOR ROW IS MID-REFOCUS — Ctrl+Z/Ctrl+Y (never gated off while the popup is open, see
        ///       LateUpdate) rebuilds the whole page and can re-request focus on the very row the popup is
        ///       anchored to; DocBlockView.CaretPending's own well (ActivateInputField takes a frame or two
        ///       to really land) means `live` can read null for a beat while that happens. The popup must
        ///       SURVIVE this — closing it here would be the exact "field not focused THIS INSTANT" mistake
        ///       фикс-раунд 2 already removed for Enter/click, just via a third door.
        ///   (б) THE DM CLICKED SOMETHING THAT IS NOT THE POPUP AND NOT THE ANCHOR ROW — the toolbar, the
        ///       navigator, an image block, empty page space. Nothing is going to refocus the anchor row on
        ///       its own, so `live == null` is the DM's genuine, permanent answer — this is the "backdrop"
        ///       the review round asked to be restored, and the popup must NOT survive it.
        /// `mentionAnchorLastFocusedFrame`, stamped every frame the branch below CONFIRMS the anchor row is
        /// the live one, is the discriminator: fewer than MentionGraceFrames frames since that confirmation
        /// reads as (а); more reads as (б). The frozen-text check further down still runs on every frame
        /// within the grace window too — the grace period buys TIME for a legitimate refocus, it does not
        /// waive the "does the anchor still say what the popup shows" check that closes on an actual edit
        /// (e.g. an Undo that reverted the very characters the popup's query was built from).</summary>
        void UpdateMentionPopup(DocBlockView live, string prevFocusedId, int prevCaret)
        {
            if (mentionPopup == null) return;

            if (mentionPopup.IsOpen)
            {
                if (live != null && live.BlockId == mentionPopup.BlockId)
                {
                    mentionAnchorLastFocusedFrame = Time.frameCount;   // подтверждено: якорь в фокусе сейчас.

                    // Строка В ФОКУСЕ — узнаём АКТУАЛЬНЫЙ query по живой каретке, тем же путём, что и
                    // раньше; это единственный путь, которым запрос СУЖАЕТСЯ/РАСШИРЯЕТСЯ при печати.
                    if (live.CaretPending) return;

                    string text = live.Field.text ?? "";
                    int caret = live.Field.caretPosition;
                    if (MentionQuery.TryFind(text, caret, out int atIndex, out string q) && atIndex == mentionPopup.AtIndex)
                    {
                        mentionPopup.Refresh(q, live);
                        return;
                    }

                    // Не тот же якорь — либо явное закрытие (пробел, стёртый «@», каретка ушла в сторону),
                    // либо ВТОРОЙ «@», набранный ПРЯМО внутри уже открытого запроса. Второй случай обязан
                    // НАЧАТЬ НОВЫЙ запрос, а не просто захлопнуть попап — рулинг 1 не делает исключения
                    // «пока другой попап уже открыт» (находка №7 ревью фикс-раунда 2). Один и тот же гейт
                    // TryDetectFreshAt решает оба: если последний символ — свежий «@», открываемся заново
                    // на новом месте; если нет (пробел и т.п.) — просто остаёмся закрытыми.
                    mentionPopup.Close();
                    if (TryDetectFreshAt(live, prevFocusedId, prevCaret, out int newAt, out string newQuery))
                    {
                        mentionPopup.Open(live, newAt, newQuery);
                        mentionAnchorLastFocusedFrame = Time.frameCount;
                    }
                    return;
                }

                if (live != null && live.BlockId != mentionPopup.BlockId)
                {
                    // Другая строка ЗАБРАЛА фокус — однозначно случай (б): раз фокус УЖЕ сидит на конкретной
                    // другой строке, значит клик состоялся и попал в определённое место — никакой grace не
                    // нужен, тут нет «может, ещё вернётся».
                    mentionPopup.Close();
                    return;
                }

                // live == null — случай (а) или (б), см. класс-док. Grace ещё не истёк → держим как есть
                // (и всё равно сверяем текст ниже — см. класс-док, последний абзац). Grace истёк → закрыть.
                if (Time.frameCount - mentionAnchorLastFocusedFrame > MentionGraceFrames)
                {
                    mentionPopup.Close();
                    return;
                }

                var block = FindBlockById(mentionPopup.BlockId);
                string frozen = block != null ? (block.Text ?? "") : null;
                int at = mentionPopup.AtIndex;
                string expected = mentionPopup.Query ?? "";
                bool stillThere = frozen != null && at >= 0 && at + 1 + expected.Length <= frozen.Length
                                   && frozen[at] == '@' && frozen.Substring(at + 1, expected.Length) == expected;
                if (!stillThere) mentionPopup.Close();
                return;
            }

            // Попап ЗАКРЫТ — открыть его, только если «@» был набран ИМЕННО в этом кадре.
            if (TryDetectFreshAt(live, prevFocusedId, prevCaret, out int freshAt, out string freshQuery))
            {
                mentionPopup.Open(live, freshAt, freshQuery);
                mentionAnchorLastFocusedFrame = Time.frameCount;
            }
        }

        /// <summary>Задача 10б, фикс-раунд 3 (находка №2). Внешняя мутация (MentionPopup.Choose →
        /// DocumentPageView.ReplaceRangeWithToken) уже переписала блок и переставила каретку — этот метод
        /// лишь СИНХРОНИЗИРУЕТ кэш этого класса с тем, что уже произошло, тем же путём (AdoptFocus), которым
        /// Handle() синхронизирует его после СВОИХ собственных правок.
        ///
        /// БЕЗ ЭТОГО ВЫЗОВА: `lastCaret` держал бы позицию ДО вставки токена (3 для «@Ол», например) ещё
        /// один-два кадра — ровно то окно, в которое у СВЕЖЕПЕРЕСОЗДАННОЙ строки (RebuildAndFocus строит
        /// новые DocBlockView) `CaretWhenEditingEnded` ещё не был установлен ни разу (её собственный field
        /// initializer — `-1`, DocBlockView.cs), так что запасной путь LateUpdate (`ended.
        /// CaretWhenEditingEnded >= 0`) не срабатывает и не перезаписывает устаревшее значение. Второй Enter,
        /// нажатый в этом окне, доходил бы до `Handle(DocKey.Enter)` со СТАРОЙ кареткой — и раскалывал бы
        /// строку ВНУТРИ только что вставленного `[[page:id|Олег]]`. Один и тот же путь работает и для
        /// выбора Enter'ом, и для выбора мышью — оба идут через MentionPopup.Choose, который зовёт это ровно
        /// один раз, сразу после успешной вставки.</summary>
        public void NoteExternalTokenInsertion(string blockId, int caretAfterToken) => AdoptFocus(blockId, caretAfterToken);

        /// <summary>Ищет блок страницы по id — то, что делает якорь попапа проверяемым независимо от того,
        /// в фокусе ли его строка ПРЯМО СЕЙЧАС (см. UpdateMentionPopup, ветка live == null).</summary>
        DocBlock FindBlockById(string id)
        {
            if (pageView == null || pageView.Page == null || string.IsNullOrEmpty(id)) return null;
            foreach (var b in pageView.Page.Blocks)
                if (b != null && b.Id == id) return b;
            return null;
        }

        /// <summary>«@» набран ИМЕННО в этом кадре на строке `live`, а не просто оказался перед кареткой
        /// (клик мышью в старый текст с «@» внутри не должен ничего открывать/переоткрывать).
        /// TextChangedThisFrame — тот же флаг, которым уже пользуется Backspace-ветка ниже, и по той же
        /// причине (сравнение СТРОК, а не факт события — см. класс-док). caretPosition == prevCaret + 1
        /// значит «ровно один символ добавился там, где раньше стояла каретка», и именно этот символ
        /// проверяется на «@» ниже. Используется и для «попап закрыт → открыть» (UpdateMentionPopup, хвост),
        /// и для «второй „@“ внутри уже открытого запроса → начать новый» (та же функция, тот же гейт).</summary>
        static bool TryDetectFreshAt(DocBlockView live, string prevFocusedId, int prevCaret, out int atIndex, out string query)
        {
            atIndex = -1;
            query = null;
            if (live == null || live.Field == null || !live.TextChangedThisFrame) return false;
            if (live.BlockId != prevFocusedId) return false;

            string text = live.Field.text ?? "";
            int caret = live.Field.caretPosition;
            if (caret != prevCaret + 1) return false;
            if (prevCaret < 0 || prevCaret >= text.Length || text[prevCaret] != '@') return false;

            return MentionQuery.TryFind(text, caret, out atIndex, out query);
        }

        /// <summary>Rule 5 — arrows navigate the popup's list, never the caret (and Esc leaves it exactly
        /// where it was, per rule 4). By the time this class's own LateUpdate runs, TMP's own Update-phase
        /// drain has already acted on this exact key for this frame (see the class doc's "THE DRAIN"
        /// reasoning) — there is no way to have stopped that from here, only to put the caret back.
        /// `wasCaret` is the caret as of the END OF LAST FRAME (`prevCaret`, captured at the very top of
        /// LateUpdate before anything this frame could touch it), which is the right position to restore to
        /// precisely because nothing else this frame moved it: neither an arrow key nor Esc raises
        /// onValueChanged, so the mention span itself is untouched either way.
        ///
        /// GOES THROUGH AdoptFocus, NOT A BARE FocusAt — every other FocusAt call site in this class is
        /// paired with it (see Handle()). CORRECTED (фикс-раунд 3): an earlier version of this comment
        /// claimed `lastCaret` had "already been refreshed this frame from the field's own post-drain
        /// position" before this runs — true when interception lived AFTER that refresh block, false now
        /// that фикс-раунд 2 moved interception ABOVE it. At the point this method is called, `lastCaret`
        /// still holds LAST frame's value (`wasCaret` IS `prevCaret` IS the cache's current contents — the
        /// refresh block never runs at all this frame, every branch above returns before reaching it), so
        /// AdoptFocus here writes back the SAME value it already held. The call stays anyway: it is the same
        /// call every other FocusAt site in this class makes, so a later change to the ordering above does
        /// not silently stop restoring the caret just because this line looked redundant against TODAY's
        /// order — the moment it stops being redundant is exactly the moment removing it would matter.</summary>
        void RestoreCaretAfterPopupKey(DocBlockView live, int wasCaret)
        {
            if (live == null) return;
            AdoptFocus(live.BlockId, wasCaret);
            live.FocusAt(wasCaret);
        }
    }
}
