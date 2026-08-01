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
    /// Height is driven explicitly from the text's preferred height rather than by a ContentSizeFitter.
    /// A ContentSizeFitter on a child whose height its parent VerticalLayoutGroup also controls inflates
    /// that child — a trap this project has hit repeatedly.
    /// </summary>
    public class DocBlockView : MonoBehaviour
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
        float appliedHeight = -1f;
        // A pending caret must be an explicit flag, not a sentinel value: a plain "-1 means end" default
        // would jam the caret to the end of the text the moment the DM clicked into the row by hand.
        bool caretPending;
        int pendingCaret;
        int caretWhenEditingEnded = -1;
        int textChangedOnFrame = -1;

        public DocBlock Data => data;
        public InputField Field { get; private set; }
        public Text TextComponent { get; private set; }
        public string BlockId => data != null ? data.Id : null;

        /// <summary>Where the caret was at the instant this field stopped being edited, or -1 if it never has
        /// been. This exists because Enter DESTROYS the answer: InputField.DeactivateInputField
        /// (InputField.cs:3237) clears m_AllowInput — so `Field.isFocused` is already false — then fires
        /// onEndEdit, and only after that sets m_CaretPosition = m_CaretSelectPosition = 0. The onEndEdit
        /// callback is therefore the last moment in the frame at which the caret is still the DM's and not
        /// yet zero, and it runs INSIDE the same event drain that applied the character they typed just
        /// before pressing Enter — so this value and DocBlock.Text describe the same instant, which is the
        /// whole point (see DocKeyboardController's class doc).</summary>
        public int CaretWhenEditingEnded => caretWhenEditingEnded;

        /// <summary>True when the field changed this block's text during THIS frame's event drain. The one
        /// caller that needs it is DocKeyboardController's Backspace branch: the field performs its own
        /// Backspace before that branch runs, so a caret sitting at 0 is ambiguous — it means either "the DM
        /// pressed Backspace at the start of the row" (ours: merge with the row above) or "the DM deleted the
        /// first character and the field moved the caret there" (not ours). The field's Backspace at offset 0
        /// deletes nothing (InputField.cs:2335 requires caretPositionInternal > 0) and so raises no
        /// onValueChanged, which makes this flag the exact discriminator.</summary>
        public bool TextChangedThisFrame => textChangedOnFrame == Time.frameCount;

        public event System.Action<string> OnTextChanged;
        public event System.Action<string> OnToggleCollapse;
        public event System.Action<string> OnBulletPressed;

        public static float IndentFor(int depth) => IndentBase + Mathf.Max(0, depth) * IndentPerLevel;

        public void Initialize(DocBlock block, RectTransform parent, Font font)
        {
            data = block;

            // The row GameObject is built with a RectTransform already on it (see DocumentPageView.Rebuild),
            // so this only has to cope with being handed a bare one.
            var rect = GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            transform.SetParent(parent, false);
            layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = MinRowHeight;

            float indent = IndentFor(data.Depth);

            // A section is the only row with a collapse control, and it sits in the indent gutter so the
            // heading text still lines up with the rows beneath it.
            if (data.Kind == BlockKind.Section)
            {
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
                // gesture is reported from here and never from the text field — the text must stay selectable.
                var bulletGO = new GameObject("Bullet", typeof(RectTransform));
                bulletGO.transform.SetParent(transform, false);
                var bulletRect = bulletGO.GetComponent<RectTransform>();
                bulletRect.anchorMin = new Vector2(0f, 1f);
                bulletRect.anchorMax = new Vector2(0f, 1f);
                bulletRect.pivot = new Vector2(0f, 1f);
                bulletRect.anchoredPosition = new Vector2(indent - 12f, -2f);
                bulletRect.sizeDelta = new Vector2(12f, 18f);

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
                // No bullet — a read-aloud paragraph gets an accent bar down its left edge instead.
                //
                // Task 10f review round 1 (Minor): this branch is currently unreachable for NEW content —
                // CreateSessionSheet's «Сильное начало» row was the only place a Prose block was ever
                // created, and Task 10f removed it (the scaffold seeding, not this rendering). DocKeyboardOps
                // only propagates an EXISTING Prose block's kind (Enter/split), never originates one, and
                // TryDeserializeBlocks only round-trips one already present in a payload. A document saved
                // before Task 10f still shows this bar correctly for its existing Prose rows — kept for
                // exactly that reason — but the DM has lost the read-aloud-paragraph affordance for new
                // writing. Recorded as a Р2 product gap, not fixed here: restoring it needs a UI decision
                // (a second button next to «+ Раздел»? a Tab-cycle through kinds?) that is not this review's
                // to make.
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

            // ── the text field ────────────────────────────────────────────────
            var fieldGO = new GameObject("Field", typeof(RectTransform));
            fieldGO.transform.SetParent(transform, false);
            textArea = fieldGO.GetComponent<RectTransform>();
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(indent, VerticalPadding * 0.5f);
            textArea.offsetMax = new Vector2(-8f, -VerticalPadding * 0.5f);

            var fieldBg = fieldGO.AddComponent<Image>();
            fieldBg.color = new Color(1f, 1f, 1f, 0.01f);   // invisible, but needed as a raycast target

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(fieldGO.transform, false);
            TextComponent = textGO.AddComponent<Text>();
            TextComponent.font = font;
            TextComponent.fontSize = data.Kind == BlockKind.Section ? 15 : 13;
            TextComponent.fontStyle = data.Kind == BlockKind.Section ? FontStyle.Bold : FontStyle.Normal;
            TextComponent.supportRichText = false;
            TextComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            TextComponent.verticalOverflow = VerticalWrapMode.Overflow;
            TextComponent.alignment = TextAnchor.UpperLeft;
            ThemeService.Tag(TextComponent, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Field = fieldGO.AddComponent<InputField>();
            Field.textComponent = TextComponent;
            Field.targetGraphic = fieldBg;
            // MultiLineSubmit, NOT MultiLineNewline: the text still wraps across as many lines as it needs,
            // but Enter never inserts a newline — it is ours to interpret as "make a new block". Without this
            // the field would swallow Enter before DocKeyboardController ever sees it.
            Field.lineType = InputField.LineType.MultiLineSubmit;
            Field.text = data.Text ?? "";
            Field.onValueChanged.AddListener(OnFieldChanged);
            // Fires from inside DeactivateInputField, before it zeroes the caret — see CaretWhenEditingEnded.
            Field.onEndEdit.AddListener(_ => caretWhenEditingEnded = Field != null ? Field.caretPosition : -1);
        }

        void OnFieldChanged(string value)
        {
            if (data == null) return;
            data.Text = value;
            textChangedOnFrame = Time.frameCount;
            OnTextChanged?.Invoke(data.Id);
        }

        public void Refresh()
        {
            if (data == null) return;
            if (Field != null && Field.text != (data.Text ?? "")) Field.text = data.Text ?? "";
            if (collapseGlyph != null) collapseGlyph.text = data.Collapsed ? "▸" : "▾";
        }

        /// <summary>True between FocusAt and the frame LateUpdate manages to place the caret. In that gap the
        /// field's own caret is NOT the one anyone asked for: ActivateInputField's activation runs OnFocus,
        /// which calls SelectAll (InputField.cs:1285) and leaves caretPosition reading 0 with the whole text
        /// selected. DocKeyboardController checks this before refreshing its cache from a live field, because
        /// caching that 0 and then acting on it would split a row at the front instead of where focus was
        /// requested.</summary>
        public bool CaretPending => caretPending;

        /// <summary>Puts the caret in this row. offset &lt; 0 means the end of the text. The caret is applied
        /// on a later frame because ActivateInputField only takes effect once the field is actually focused,
        /// and setting caretPosition before that gets overwritten.</summary>
        public void FocusAt(int offset)
        {
            if (Field == null) return;
            pendingCaret = offset;
            caretPending = true;
            Field.ActivateInputField();
        }

        /// <summary>Which visual line the caret is on, and how many there are. Legacy InputField exposes
        /// neither, so this reads the text generator directly — it is what decides whether Up/Down move the
        /// caret inside this row or jump to the next one.
        ///
        /// KNOWN LAG, measured and left alone (Task 10g, Step 2). `Text.cachedTextGenerator` is a render-time
        /// artefact — Text only repopulates it from OnPopulateMesh, during the canvas rebuild at the END of
        /// the frame — while `Field.caretPosition` below is current. So for one frame after a keystroke that
        /// changes the wrapping, the caret is compared against the previous frame's line map, and Up can jump
        /// to the previous BLOCK instead of moving up a visual line inside this row. The obvious fix, calling
        /// Populate here with the settings OnPopulateMesh uses, does NOT work and was reverted: while the
        /// field is focused — the only time these flags are read — UpdateLabel assigns the Text component a
        /// WINDOW into the field's text (`m_TextComponent.text = processed.Substring(m_DrawStart, …)`,
        /// InputField.cs:2554-2558; the full range is restored only in the `!m_AllowInput` branch at :2531).
        /// A line map built from that string is indexed in window coordinates while the caret is indexed in
        /// whole-text coordinates, so refreshing it trades one disagreement for another. Doing this properly
        /// means tracking m_DrawStart, which InputField keeps private, and it needs the Editor to validate —
        /// so it is a task, not a line. The DM has not reported it; the window is one frame.</summary>
        public bool CaretOnFirstLine => CaretLine() <= 0;

        public bool CaretOnLastLine
        {
            get
            {
                var gen = TextComponent != null ? TextComponent.cachedTextGenerator : null;
                return gen == null || gen.lineCount <= 0 || CaretLine() >= gen.lineCount - 1;
            }
        }

        int CaretLine()
        {
            var gen = TextComponent != null ? TextComponent.cachedTextGenerator : null;
            if (gen == null || gen.lineCount <= 0 || Field == null) return 0;

            int caret = Field.caretPosition;
            int line = 0;
            for (int i = 0; i < gen.lineCount; i++)
                if (caret >= gen.lines[i].startCharIdx) line = i;
            return line;
        }

        void LateUpdate()
        {
            if (data == null) return;

            // Apply a queued caret as soon as the field has really taken focus.
            if (caretPending && Field != null && Field.isFocused)
            {
                int caret = pendingCaret < 0 ? (Field.text ?? "").Length : Mathf.Min(pendingCaret, (Field.text ?? "").Length);
                Field.caretPosition = caret;
                Field.selectionAnchorPosition = caret;
                Field.selectionFocusPosition = caret;
                caretPending = false;
            }

            // Grow the row to fit its text. Driven here rather than by a ContentSizeFitter (see the class
            // comment), and only written when it actually changes, so this does not dirty the layout every
            // frame for every row on the page.
            if (TextComponent == null || layoutElement == null) return;
            float desired = Mathf.Max(MinRowHeight, TextComponent.preferredHeight + VerticalPadding);
            if (Mathf.Abs(desired - appliedHeight) > 0.5f)
            {
                appliedHeight = desired;
                layoutElement.preferredHeight = desired;
            }
        }
    }
}
