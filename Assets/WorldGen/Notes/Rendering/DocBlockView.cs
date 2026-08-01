using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// One row of a document page. Thin on purpose: every RULE about what a row means lives in
    /// NotesDocOps/DocKeyboardOps, and this class only draws it and reports gestures.
    ///
    /// TWO STATES, AND THE REASON IS NOT COSMETIC. At rest the row is a TMP_Text; while the DM edits it, a
    /// TMP_InputField takes its place showing the RAW source. A clickable [[link]] inside an ACTIVE input
    /// field is not achievable — in an input field a click is a caret placement, and that is the whole job of
    /// an input field — so the two jobs get two components and only one of them exists at a time. Р2 Task 6
    /// builds the swap with display text equal to source text; Task 7 makes the display differ.
    ///
    /// Height is driven explicitly from the text's preferred height rather than by a ContentSizeFitter. A
    /// ContentSizeFitter on a child whose height its parent VerticalLayoutGroup also controls inflates that
    /// child — a trap this project has hit repeatedly, and one TextMeshPro does nothing to remove.
    ///
    /// SCOPE: only the row's TEXT is TextMeshPro. The bullet, the collapse arrow and the read-aloud rule stay
    /// on legacy uGUI and the built-in font, per the arc's constraint that panels do not migrate.
    /// </summary>
    public class DocBlockView : MonoBehaviour, IPointerClickHandler
    {
        public const float MinRowHeight = 22f;
        const float IndentBase = 16f;
        const float IndentPerLevel = 18f;
        const float VerticalPadding = 4f;

        DocBlock data;
        LayoutElement layoutElement;
        RectTransform textArea;
        Text bulletText;
        Button collapseButton;
        Text collapseGlyph;
        TMP_Text fieldText;
        float appliedHeight = -1f;
        // A pending caret must be an explicit flag, not a sentinel value: a plain "-1 means end" default
        // would jam the caret to the end of the text the moment the DM clicked into the row by hand.
        bool caretPending;
        int pendingCaret;
        int caretWhenEditingEnded = -1;
        int textChangedOnFrame = -1;
        bool editing;
        bool endEditPending;

        public DocBlock Data => data;

        /// <summary>The editing half. Present from Initialize but INACTIVE until the DM edits this row, so
        /// `Field != null` never means "this row is being edited" — `Field.isFocused` does, which is what
        /// DocKeyboardController already asks.</summary>
        public TMP_InputField Field { get; private set; }

        /// <summary>The resting half: what the row looks like when nobody is typing in it. Task 7 gives this
        /// rich text and link tags; here it carries the block's text verbatim.</summary>
        public TMP_Text Display { get; private set; }

        public string BlockId => data != null ? data.Id : null;

        /// <summary>Where the caret was at the instant this field stopped being edited, or -1 if it never has
        /// been.
        ///
        /// RE-DERIVED FOR TextMeshPro, AND THE OLD REASONING NO LONGER APPLIES. Under legacy InputField this
        /// value was a rescue: DeactivateInputField fired onEndEdit and then set m_CaretPosition = 0
        /// (InputField.cs:3254 then :3262), so the callback was the last moment in the frame at which the
        /// caret was still the DM's. TMP_InputField does neither of those things in that order. Its
        /// DeactivateInputField does not send onEndEdit at all — ReleaseSelection does (TMP_InputField.cs
        /// :4410), reached from DeactivateInputField only when resetOnDeActivation is set, which it is by
        /// default — and the lines that would zero the caret are COMMENTED OUT in the shipped source
        /// (:4443-4444). So the caret survives past the end of editing and this value is no longer the only
        /// way to learn it.
        ///
        /// It is kept anyway, for two reasons. It is still exactly right — the callback runs after
        /// m_AllowInput goes false and before anything else touches the position — and DocKeyboardController
        /// reads it on a row that has stopped being edited, where asking the field directly would mean
        /// depending on the absence of a reset that a future TMP version could restore by uncommenting two
        /// lines.</summary>
        public int CaretWhenEditingEnded => caretWhenEditingEnded;

        /// <summary>True when the field changed this block's text during THIS frame's event drain. The one
        /// caller that needs it is DocKeyboardController's Backspace branch: the field performs its own
        /// Backspace before that branch runs, so a caret sitting at 0 is ambiguous — it means either "the DM
        /// pressed Backspace at the start of the row" (ours: merge with the row above) or "the DM deleted the
        /// first character and the field moved the caret there" (not ours). A Backspace at offset 0 deletes
        /// nothing and so raises no onValueChanged, which makes this flag the exact discriminator.</summary>
        public bool TextChangedThisFrame => textChangedOnFrame == Time.frameCount;

        public event System.Action<string> OnTextChanged;
        public event System.Action<string> OnToggleCollapse;
        public event System.Action<string> OnBulletPressed;

        public static float IndentFor(int depth) => IndentBase + Mathf.Max(0, depth) * IndentPerLevel;

        public void Initialize(DocBlock block, RectTransform parent, Font font)
        {
            data = block;

            var rect = GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            transform.SetParent(parent, false);
            layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = MinRowHeight;

            float indent = IndentFor(data.Depth);

            BuildChrome(font, indent);

            // ── the text area: one rect, two occupants, never both at once ────
            var areaGO = new GameObject("TextArea", typeof(RectTransform));
            areaGO.transform.SetParent(transform, false);
            textArea = areaGO.GetComponent<RectTransform>();
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(indent, VerticalPadding * 0.5f);
            textArea.offsetMax = new Vector2(-8f, -VerticalPadding * 0.5f);

            // Invisible, but the raycast target that makes a click anywhere in the row start editing —
            // including on an EMPTY row, which has no glyphs to hit. The click is handled by this component
            // (OnPointerClick), which the event system finds by bubbling up from here.
            var areaBg = areaGO.AddComponent<Image>();
            areaBg.color = new Color(1f, 1f, 1f, 0.01f);

            BuildDisplay();
            BuildField();

            RefreshDisplay();
        }

        void BuildChrome(Font font, float indent)
        {
            if (data.Kind == BlockKind.Section)
            {
                // A section is the only row with a collapse control, and it sits in the indent gutter so the
                // heading text still lines up with the rows beneath it.
                var collapseGO = new GameObject("Collapse", typeof(RectTransform));
                collapseGO.transform.SetParent(transform, false);
                var collapseRect = collapseGO.GetComponent<RectTransform>();
                collapseRect.anchorMin = new Vector2(0f, 1f);
                collapseRect.anchorMax = new Vector2(0f, 1f);
                collapseRect.pivot = new Vector2(0f, 1f);
                collapseRect.anchoredPosition = new Vector2(0f, -2f);
                collapseRect.sizeDelta = new Vector2(16f, 18f);

                collapseGlyph = collapseGO.AddComponent<Text>();
                collapseGlyph.font = font;
                collapseGlyph.fontSize = 12;
                collapseGlyph.alignment = TextAnchor.MiddleCenter;
                ThemeService.Tag(collapseGlyph, ThemeRole.Mut);

                collapseButton = collapseGO.AddComponent<Button>();
                collapseButton.targetGraphic = collapseGlyph;
                collapseButton.onClick.AddListener(() => OnToggleCollapse?.Invoke(data.Id));
            }
            else if (data.Kind == BlockKind.Item)
            {
                // The bullet doubles as the drag handle and the multi-select hit target, which is why the
                // gesture is reported from here and never from the text — the text must stay selectable.
                var bulletGO = new GameObject("Bullet", typeof(RectTransform));
                bulletGO.transform.SetParent(transform, false);
                var bulletRect = bulletGO.GetComponent<RectTransform>();
                bulletRect.anchorMin = new Vector2(0f, 1f);
                bulletRect.anchorMax = new Vector2(0f, 1f);
                bulletRect.pivot = new Vector2(0f, 1f);
                bulletRect.anchoredPosition = new Vector2(indent - 12f, -3f);
                bulletRect.sizeDelta = new Vector2(12f, 20f);

                bulletText = bulletGO.AddComponent<Text>();
                bulletText.font = font;
                bulletText.fontSize = 13;
                bulletText.text = "·";
                bulletText.alignment = TextAnchor.MiddleCenter;
                ThemeService.Tag(bulletText, ThemeRole.Mut);

                var bulletButton = bulletGO.AddComponent<Button>();
                bulletButton.targetGraphic = bulletText;
                bulletButton.onClick.AddListener(() => OnBulletPressed?.Invoke(data.Id));
            }
            else if (data.Kind == BlockKind.Prose)
            {
                // A read-aloud paragraph gets an accent rule down its left edge instead of a bullet.
                //
                // Still unreachable for NEW content, and still deliberately kept: CreateSessionSheet stopped
                // seeding Prose blocks at Task 10f, and DocKeyboardOps only propagates an existing block's
                // kind, never originates one. A document saved before that still renders its Prose rows
                // correctly — which is what this branch is for. Restoring the affordance needs the new page
                // toolbar (Р2 Task 8) and, for a rule of its own rather than a borrowed one, the BlockKind
                // that Р3's format bump can afford to add.
                var barGO = new GameObject("AccentBar", typeof(RectTransform));
                barGO.transform.SetParent(transform, false);
                var barRect = barGO.GetComponent<RectTransform>();
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(0f, 1f);
                barRect.pivot = new Vector2(0f, 0.5f);
                barRect.anchoredPosition = new Vector2(indent - 10f, 0f);
                barRect.sizeDelta = new Vector2(3f, -4f);
                ThemeService.Tag(barGO.AddComponent<Image>(), ThemeRole.Accent);
            }
        }

        void BuildDisplay()
        {
            var displayGO = new GameObject("Display", typeof(RectTransform));
            displayGO.transform.SetParent(textArea, false);
            var r = displayGO.GetComponent<RectTransform>();
            Stretch(r);

            Display = displayGO.AddComponent<TextMeshProUGUI>();
            ApplyTextStyle(Display);
            // Rich text ON here and OFF on the field, which is the whole display/edit split in one property:
            // the resting row renders link and colour tags (Task 7), the editing row shows the source they
            // came from. Harmless today, when the two strings are identical.
            Display.richText = true;
            // The display is NOT a raycast target: the invisible Image behind it already catches every click
            // in the row, and two targets would make an empty row behave differently from a full one.
            Display.raycastTarget = false;
        }

        void BuildField()
        {
            // Built INACTIVE, and built inactive BEFORE the component is added: TMP_InputField wires its
            // caret and its viewport in OnEnable, and adding it to a live GameObject would run that against
            // a half-assigned textComponent.
            var fieldGO = new GameObject("Field", typeof(RectTransform));
            fieldGO.transform.SetParent(textArea, false);
            fieldGO.SetActive(false);
            Stretch(fieldGO.GetComponent<RectTransform>());

            var fieldBg = fieldGO.AddComponent<Image>();
            fieldBg.color = new Color(1f, 1f, 1f, 0.01f);

            var viewportGO = new GameObject("TextViewport", typeof(RectTransform));
            viewportGO.transform.SetParent(fieldGO.transform, false);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportGO.AddComponent<RectMask2D>();

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(viewportGO.transform, false);
            Stretch(textGO.GetComponent<RectTransform>());
            fieldText = textGO.AddComponent<TextMeshProUGUI>();
            ApplyTextStyle(fieldText);

            Field = fieldGO.AddComponent<TMP_InputField>();
            Field.textViewport = viewportRect;
            Field.textComponent = fieldText;
            Field.targetGraphic = fieldBg;

            // MultiLineSubmit, NOT MultiLineNewline: the text still wraps across as many lines as it needs,
            // but Enter never inserts a newline — it is ours to interpret as "make a new block". TMP honours
            // this the same way legacy did, returning EditState.Finish for Return on any type but
            // MultiLineNewline (TMP_InputField.cs:2249-2256).
            Field.lineType = TMP_InputField.LineType.MultiLineSubmit;

            // RICH TEXT OFF, AND THIS IS LOAD-BEARING TWICE OVER. First, the field shows the RAW source, and
            // raw means raw: a DM who types "<b>" must see "<b>". Second, TMP_InputField.caretPosition is an
            // index into the text info's CHARACTER list, not into the string (TMP_InputField.cs:1009-1013),
            // and those two diverge exactly when rich-text tags are parsed away. With rich text off they are
            // the same number, which is what lets DocKeyboardController keep passing caretPosition to
            // DocKeyboardOps as a string offset without a conversion at every call site.
            Field.richText = false;

            // Focus must NOT select everything. TMP's default is to select-all on focus, which is where the
            // legacy row's "caretPosition reads 0 with the whole text selected" trouble came from; refusing
            // it at the source removes that state instead of detecting it afterwards.
            Field.onFocusSelectAll = false;

            // ESCAPE MUST NOT THROW THE TEXT AWAY. Legacy's affordance was to restore what the row started
            // with, and TMP offers the same (DeactivateInputField reassigns m_OriginalText at
            // TMP_InputField.cs:4430) — but on a WRITING SURFACE that means a paragraph the DM typed can
            // vanish to one keystroke, with no undo behind it to get it back (page undo is still unbuilt).
            // The DM hit exactly this at the first checkpoint. With the flag off, Escape means only "stop
            // editing this row", the text stays, and discarding an edit becomes undo's job — which is where
            // it belongs, because undo can also discard the edit BEFORE the one Escape happened to catch.
            Field.restoreOriginalTextOnEscape = false;

            Field.caretWidth = 1;
            // customCaretColor FALSE on purpose: TMP then reports the text component's own colour as the
            // caret's (TMP_InputField.cs:731), and that colour is themed by ThemeService.Tag above. Setting
            // an explicit caret colour here would freeze it at whichever theme was live when the row was
            // built, and the caret would keep the old theme's ink after a switch.
            Field.customCaretColor = false;

            // The field is a MULTILINE one, so TMP's own guard against control characters does not fire for
            // us and a Tab would land in BOTH layers: DocKeyboardController would indent the block, and the
            // field would append a literal tab a moment later. Rejecting the character here (a validator
            // returning '\0' drops the input) leaves indentation to DocKeyboardController alone. The cost is
            // that pasting multi-line text flattens — a block is one paragraph in this model, so a newline
            // inside one has nothing to mean. This does NOT retire the clipboard rule that tabs and newlines
            // survive serialization: that pins the data layer, which still carries both for old blocks and
            // for Detail strings, while this only governs what a row will accept from a keyboard.
            Field.onValidateInput = (_, __, added) => added == '\t' || added == '\n' || added == '\r' ? '\0' : added;

            Field.onValueChanged.AddListener(OnFieldChanged);
            Field.onEndEdit.AddListener(OnFieldEndEdit);
        }

        void ApplyTextStyle(TMP_Text text)
        {
            // A section heading is a LABEL and body text is prose; every difference between them lives in
            // NotesTypography, so this method chooses which one and decides nothing itself.
            if (data.Kind == BlockKind.Section)
            {
                NotesTypography.ApplySectionHeading(text);
                ThemeService.Tag(text, ThemeRole.Accent);
            }
            else
            {
                NotesTypography.ApplyBody(text);
                ThemeService.Tag(text, ThemeRole.Txt);
            }
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        // ── editing ───────────────────────────────────────────────────────────

        /// <summary>Enters edit mode with the caret at a SOURCE offset. Negative means the end of the text.
        /// Idempotent: asking a row that is already editing to edit only moves the caret.</summary>
        public void BeginEdit(int sourceCaret)
        {
            if (Field == null || Display == null) return;

            if (!editing)
            {
                editing = true;
                Display.gameObject.SetActive(false);
                Field.gameObject.SetActive(true);
                Field.SetTextWithoutNotify(data.Text ?? "");
            }

            // A swap-back queued by an earlier blur must not land on the edit we are starting right now.
            endEditPending = false;
            pendingCaret = sourceCaret;
            caretPending = true;
            // ActivateInputField selects the object itself when it is not already selected
            // (TMP_InputField.cs:4329-4330), so calling SetSelectedGameObject here as well would only add a
            // second route into OnSelect for the same activation.
            Field.ActivateInputField();
        }

        /// <summary>Records the caret and QUEUES the swap back to the display — it does not perform it.
        ///
        /// Deactivating the field's GameObject from inside the field's own onEndEdit means mutating the
        /// hierarchy in the middle of TMP's ReleaseSelection, which is the shape of Unity bug that shows up
        /// later as a null inside somebody else's callback. LateUpdate is a moment nothing is mid-flight, and
        /// it is the same moment DocKeyboardController was built around for the same class of reason. The
        /// cost is one frame in which the row still shows its field, which is imperceptible and, unlike a
        /// re-entrant teardown, cannot be wrong.</summary>
        void OnFieldEndEdit(string _)
        {
            caretWhenEditingEnded = Field != null ? Field.caretPosition : -1;
            endEditPending = true;
        }

        void EndEdit()
        {
            if (!editing) return;
            editing = false;
            caretPending = false;
            if (Field != null) Field.gameObject.SetActive(false);
            if (Display != null) Display.gameObject.SetActive(true);
            RefreshDisplay();
        }

        /// <summary>Rows are destroyed and rebuilt wholesale on every mutation (DocumentPageView.Rebuild), and
        /// a destroyed field runs DeactivateInputField on its way out — which would queue a swap-back on a
        /// component that is going away. Dropping the listeners here keeps that from firing into a corpse.</summary>
        void OnDestroy()
        {
            if (Field == null) return;
            Field.onValueChanged.RemoveListener(OnFieldChanged);
            Field.onEndEdit.RemoveListener(OnFieldEndEdit);
        }

        void OnFieldChanged(string value)
        {
            if (data == null) return;
            data.Text = value;
            textChangedOnFrame = Time.frameCount;
            OnTextChanged?.Invoke(data.Id);
        }

        /// <summary>A click anywhere in the row's text area starts editing, with the caret on the character
        /// clicked. Reached by event bubbling from the invisible Image on TextArea — the bullet and the
        /// collapse arrow are Buttons and consume their own clicks before this ever sees them.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (editing || Display == null) return;
            BeginEdit(SourceCaretFromPointer(eventData));
        }

        /// <summary>Which SOURCE offset a click landed on.
        ///
        /// Uses the character's ARRAY INDEX in textInfo.characterInfo, not characterInfo[i].index. The two
        /// are the same today and will not be in Task 7: TMP creates a characterInfo entry only for a
        /// RENDERED character, so once the display carries link and colour tags, the array index still counts
        /// clean display characters while `.index` counts into the tagged string. The array index is
        /// therefore the display offset that Task 7's DisplayText.ToSource expects, and picking it now means
        /// that task changes one line here instead of discovering an off-by-a-tag.</summary>
        int SourceCaretFromPointer(PointerEventData eventData)
        {
            string source = data?.Text ?? "";
            if (source.Length == 0) return 0;

            int nearest = TMP_TextUtilities.FindNearestCharacter(Display, eventData.position, eventData.pressEventCamera, true);
            var info = Display.textInfo;
            if (nearest < 0 || info == null || nearest >= info.characterCount) return source.Length;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Display.rectTransform, eventData.position, eventData.pressEventCamera, out var local);

            var ci = info.characterInfo[nearest];
            int displayCaret = local.x < (ci.origin + ci.xAdvance) * 0.5f ? nearest : nearest + 1;
            return DisplayToSource(displayCaret);
        }

        /// <summary>Display offset → source offset. The identity here because Task 6's display IS the source;
        /// Task 7 replaces the body with NotesLinkOps' index map and nothing else in this file moves.</summary>
        int DisplayToSource(int displayCaret) => displayCaret;

        // ── refresh ───────────────────────────────────────────────────────────

        public void Refresh()
        {
            if (data == null) return;
            if (editing)
            {
                // Assigning into a FOCUSED field would move the DM's caret, so an unchanged value is left
                // alone — and SetTextWithoutNotify is used because writing the block's own text back into it
                // through onValueChanged would mark the project dirty on every rebuild.
                if (Field != null && Field.text != (data.Text ?? "")) Field.SetTextWithoutNotify(data.Text ?? "");
            }
            else
            {
                RefreshDisplay();
            }
            if (collapseGlyph != null) collapseGlyph.text = data.Collapsed ? "▸" : "▾";
        }

        /// <summary>What the row shows at rest. Task 7 puts NotesLinkOps.BuildDisplay behind this, which is
        /// the only reason it is a method rather than one line inside Refresh.</summary>
        void RefreshDisplay()
        {
            if (Display == null || data == null) return;
            Display.text = data.Text ?? "";
        }

        // ── measurement ───────────────────────────────────────────────────────

        /// <summary>True between FocusAt and the frame the caret is actually placed. In that gap the field's
        /// own caret is not the one anyone asked for, and DocKeyboardController checks this before refreshing
        /// its cache from a live field — caching a stale position and then acting on it would split a row
        /// somewhere nobody pointed at.</summary>
        public bool CaretPending => caretPending;

        /// <summary>Puts the caret in this row, entering edit mode if it is not already there. offset &lt; 0
        /// means the end of the text. The caret is applied on a later frame because ActivateInputField only
        /// takes effect once the field has really been focused, and a position written before that is
        /// overwritten.</summary>
        public void FocusAt(int offset) => BeginEdit(offset);

        /// <summary>Whether this row's whole text occupies ONE visual line. False also means "cannot prove
        /// it": every path that fails to measure answers false, so a caller may narrow a gesture on a true
        /// answer and never on the absence of one.
        ///
        /// TEXTMESHPRO MADE THIS HONEST, AND THE OLD APPARATUS IS GONE. Under legacy Text this question could
        /// not be answered from outside the class at all: cachedTextGenerator is a render-time artefact
        /// holding the PREVIOUS frame's wrapping, and while focused the Text component was given a windowed
        /// substring of the field's text (m_DrawStart is private), so a line map built from it was indexed in
        /// window coordinates while the caret was indexed in whole-text ones. The row carried its own
        /// TextGenerator to dodge both. TMP keeps the WHOLE text in its text component and scrolls by moving
        /// the transform, so textInfo.lineCount describes the same string the caret indexes, and
        /// ForceMeshUpdate makes it current rather than last frame's.</summary>
        public bool IsSingleVisualLine()
        {
            var measured = editing && fieldText != null ? fieldText : Display;
            if (measured == null) return false;

            // An empty row is one line BY CONSTRUCTION, answered before anything is measured. Not a shortcut:
            // a row Enter just created is both empty and un-laid-out, so the width guard below would refuse to
            // prove it, and the DM would lose the arrows on precisely the row they are most likely to arrow
            // out of. Nothing can wrap a string with no characters in it.
            string text = editing && Field != null ? Field.text : (data?.Text ?? "");
            if (string.IsNullOrEmpty(text)) return true;

            // Before the first layout pass a fresh row's rect is still zero-wide, and wrapping text into a
            // zero-width box would report a line per character. Unprovable answers false.
            if (measured.rectTransform.rect.width <= 1f) return false;

            measured.ForceMeshUpdate();
            var info = measured.textInfo;
            if (info == null || info.lineCount <= 0) return false;
            return info.lineCount == 1;
        }

        void LateUpdate()
        {
            if (data == null) return;

            // Swap back to the display, queued by onEndEdit — see OnFieldEndEdit for why it is not done there.
            if (endEditPending)
            {
                endEditPending = false;
                EndEdit();
            }

            // Apply a queued caret as soon as the field has really taken focus.
            if (caretPending && Field != null && Field.isFocused)
            {
                string text = Field.text ?? "";
                int caret = pendingCaret < 0 ? text.Length : Mathf.Min(pendingCaret, text.Length);
                Field.caretPosition = caret;
                Field.selectionAnchorPosition = caret;
                Field.selectionFocusPosition = caret;
                caretPending = false;
            }

            ApplyHeightNow();
        }

        /// <summary>Grows the row to fit its text. Driven here rather than by a ContentSizeFitter (see the
        /// class comment), and written only when it actually changes, so this does not dirty the layout every
        /// frame for every row on the page.
        ///
        /// PUBLIC BECAUSE A REBUILD CANNOT WAIT FOR LateUpdate. DocumentPageView.Rebuild throws every row away
        /// and makes new ones, and a new row starts at MinRowHeight; if the heights only arrived on the next
        /// frame's LateUpdate, the whole column would be briefly ~22px per row, the ScrollRect would clamp a
        /// scrolled page back to the top, and the DM's position would be gone before anything could restore
        /// it. That was the «после Enter страницу перелистывает наверх» defect. Rebuild therefore lays out
        /// once for widths, calls this on every row, and lays out again — all inside the one frame.</summary>
        public void ApplyHeightNow()
        {
            var measured = editing && fieldText != null ? fieldText : Display;
            if (measured == null || layoutElement == null) return;
            float desired = Mathf.Max(MinRowHeight, measured.preferredHeight + VerticalPadding);
            if (Mathf.Abs(desired - appliedHeight) > 0.5f)
            {
                appliedHeight = desired;
                layoutElement.preferredHeight = desired;
            }
        }

        /// <summary>The world-space top and bottom of the LINE the caret is on, so the page can scroll it into
        /// view. False whenever that cannot be answered honestly — the row is not being edited, the field has
        /// not taken focus yet, or the caret this class asked for has not been applied (caretPending), where
        /// the field still reports the position it had before FocusAt.
        ///
        /// THE LINE, NOT THE ROW. A row is one paragraph and a long one wraps to more lines than the pane is
        /// tall; scrolling the whole row into view would then put the DM at its top while they are typing at
        /// its bottom. lineInfo's ascender/descender bound exactly the strip the caret is drawn in.</summary>
        public bool TryGetCaretLineWorldY(out float top, out float bottom)
        {
            top = bottom = 0f;
            if (!editing || caretPending || Field == null || fieldText == null || !Field.isFocused) return false;

            var rt = fieldText.rectTransform;
            var info = fieldText.textInfo;

            // No glyph to hang the caret on — an empty row, or text TMP has not laid out yet. The row's own
            // rect is where the caret is drawn in that case, and it is one line tall by construction.
            if (info == null || info.characterCount == 0 || info.lineCount == 0)
            {
                rt.GetWorldCorners(cornerBuffer);
                bottom = cornerBuffer[0].y;
                top = cornerBuffer[1].y;
                return true;
            }

            // caretPosition may sit one past the last character (the caret at the end of the text); the last
            // character's own line is the right answer there.
            int idx = Mathf.Clamp(Field.caretPosition, 0, info.characterCount - 1);
            int lineNumber = Mathf.Clamp(info.characterInfo[idx].lineNumber, 0, info.lineCount - 1);
            var line = info.lineInfo[lineNumber];

            top = rt.TransformPoint(new Vector3(0f, line.ascender, 0f)).y;
            bottom = rt.TransformPoint(new Vector3(0f, line.descender, 0f)).y;
            return true;
        }

        /// <summary>Reused by TryGetCaretLineWorldY so a per-frame reveal check does not allocate a Vector3[4]
        /// every time it runs.</summary>
        static readonly Vector3[] cornerBuffer = new Vector3[4];
    }
}
