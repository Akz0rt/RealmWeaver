using System.Collections.Generic;
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
        /// <summary>Height of a picture row whose image could not be measured — no texture, or a column that
        /// has not been laid out yet. Big enough to be obviously a picture and to be clicked.</summary>
        const float PictureFallbackHeight = 160f;
        const float IndentBase = 16f;
        const float IndentPerLevel = 18f;
        const float VerticalPadding = 4f;

        /// <summary>Воздух НАД строкой раздела — столько добавляется к её высоте сверх обычного отступа
        /// между строками. Новый раздел должен читаться как начало новой мысли, а не как ещё один абзац.</summary>
        const float SectionLeadGap = 14f;

        /// <summary>How tall a board is when the DM has not resized it. Big enough that a relationship web is
        /// legible without being opened, small enough that it does not push the prose off the screen.</summary>
        public const float DefaultCanvasHeight = 320f;
        public const float MinCanvasHeight = 120f;
        public const float MinCanvasWidth = 240f;
        /// <summary>The strip under the frame that holds the board's caption.</summary>
        const float CaptionHeight = 22f;

        /// <summary>Clear space between the row's hit area and the first and last glyph on a line.
        ///
        /// WHY IT IS NOT THE ROW THAT SHRINKS. The row's whole width is a click target — clicking anywhere
        /// in it places the caret, and on an EMPTY row there are no glyphs to aim at, so narrowing it would
        /// make the margin a dead strip where a click does nothing. So the box stays wide and the TEXT moves
        /// in. The DM asked for this twice: the first attempt widened the page's own side margins, which were
        /// already 306px on their screen — the border their text was touching was this one, not the pane's.
        ///
        /// Applied to the resting text and to the editing field's viewport alike, or the line would shift
        /// sideways at the moment the DM clicks into it.</summary>
        const float TextInset = 12f;

        DocBlock data;
        RawImage picture;
        Image selectionRing;
        /// <summary>width / height of the loaded texture, or 0 when there is none to measure. Drives the row
        /// height at the column's width, which is what DocBlock.DisplayHeight's own doc means by "0 means
        /// derive the height from the aspect ratio".</summary>
        float pictureAspect;
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
        /// <summary>Whether this visit to the row has already contributed an undo step. Reset when the row is
        /// entered, so a second visit is a second step and a hundred keystrokes in one visit are still one.</summary>
        bool historyPushedThisVisit;
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

        RectTransform canvasFrame;
        public RectTransform CanvasFrame => canvasFrame;

        /// <summary>The widest a board may be drawn: the PANE's width less its margins, assigned by
        /// DocumentPageView, which is the only thing that knows how wide the pane is. 0 = not measured yet, in
        /// which case the frame stays at the column width.</summary>
        public float MaxCanvasWidth;

        public event System.Action<string> OnTextChanged;
        public event System.Action<string> OnToggleCollapse;
        public event System.Action<string> OnBulletPressed;
        public event System.Action<string> OnExpandCanvasRequested;
        public event System.Action<string> OnCanvasResizeBegan;
        public event System.Action<string> OnCanvasResizeEnded;

        /// <summary>The DM's first keystroke of this visit to the row is about to change its text. Carries the
        /// block's id and the text as it was BEFORE that keystroke, which is exactly what the page needs to
        /// remember one undo step per visit — see OnFieldChanged.</summary>
        public event System.Action<string, string> OnBeforeFirstEdit;

        /// <summary>The DM clicked a row that cannot hold a caret — a picture — and it should become the
        /// selected one. The page routes it, because selection is a property of the page (only one row can
        /// be selected) and not of any row.</summary>
        public event System.Action<string> OnSelectRequested;

        /// <summary>The DM clicked a rendered link. Carries the token's kind and id — never a name, which is
        /// exactly the thing that goes stale. The row does not know what a "poi" is or where it opens;
        /// DocumentPageView.RouteLink answers both.</summary>
        public event System.Action<string, string> OnLinkActivated;

        /// <summary>What every [[token]] in this row currently points at, or null before the page is wired.
        ///
        /// Assigned by DocumentPageView, never built here: the answer needs the whole document and the whole
        /// world list, and a row that could reach those would be a row that could reach anything. A null one
        /// is the honest pre-wiring state and renders every token as its stored copy — see
        /// NotesLinkOps.BuildDisplay's own null-resolver rule.</summary>
        public NotesLinkOps.NameResolver Resolver;

        /// <summary>Whether a [[kind:id]] token's target carries a character card — asked once per resolved
        /// span, right next to Resolver, whose own null-before-wiring rule this shares: null means "not wired
        /// yet" and every span renders as an ordinary link, never as a plate whose target cannot be checked.
        ///
        /// A PLAIN Func, not a delegate declared next to NameResolver in NotesLinkOps, because the question it
        /// answers is NOT part of the link grammar — a page link CAN be a plate or not depending on state the
        /// grammar itself does not carry (Задача 9's own ruling: no new token kind). Assigned by
        /// DocumentPageView, same as Resolver, from CharacterOps.IsCharacterLink over the same document.</summary>
        public System.Func<string, string, bool> IsCharacterLink;

        /// <summary>The last display built for the resting row: the rendered string plus the two-way index
        /// map. Kept so a click can be turned into a SOURCE caret without re-parsing, and so the caret lands
        /// on the character the DM aimed at rather than that character plus the machinery in front of it.</summary>
        DisplayText displayText = new DisplayText();

        /// <summary>The accent colour the current markup was built with. Link colours are baked into the
        /// string as `<color=#…>` tags, and ThemedGraphic cannot repaint those — it owns the component's own
        /// colour, not the tags inside its text — so a theme switch has to rebuild the markup. Stored as the
        /// COLOUR rather than the theme enum on purpose: `Theme` is also the name of the namespace it lives
        /// in, and this project has been bitten three times by that shadowing.</summary>
        Color markupAccent;

        public static float IndentFor(int depth) => IndentBase + Mathf.Max(0, depth) * IndentPerLevel;

        public void Initialize(DocBlock block, RectTransform parent, Font font, NotesLinkOps.NameResolver resolve,
            System.Func<string, string, bool> isCharacterLink)
        {
            data = block;
            Resolver = resolve;
            IsCharacterLink = isCharacterLink;

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
            // The right inset matches the base indent, so at depth 0 the text has the SAME air on both sides.
            // It used to be 8 against a left of 16, which read as a column leaning left. Deeper rows are
            // deliberately still asymmetric — that gap IS the indentation.
            // VERTICALLY THE AREA TAKES THE WHOLE ROW, and the row's padding stays in its HEIGHT instead
            // (ApplyHeightNow adds VerticalPadding there). Splitting it half above and half below made the
            // text box exactly as tall as its own text — and TMP_InputField skips its internal scrolling only
            // while the text is STRICTLY shorter than the viewport (TMP_InputField.cs:2425). On the equality
            // it would go on to move the text inside the row instead. Giving the box the row's few spare
            // pixels makes that guard strictly true, so the wheel over an editing row belongs to the page.
            textArea.offsetMin = new Vector2(indent, 0f);
            // ПЕРЕД РАЗДЕЛОМ ВОЗДУХА БОЛЬШЕ, ЧЕМ МЕЖДУ АБЗАЦАМИ. Интервал у VerticalLayoutGroup один на
            // все строки, поэтому добавка живёт в самой строке раздела: текст сдвигается вниз, а высота
            // строки растёт ровно на столько же (см. ApplyHeightNow) — иначе заголовок съехал бы за
            // нижний край собственной строки.
            textArea.offsetMax = new Vector2(-IndentBase, data.Kind == BlockKind.Section ? -SectionLeadGap : 0f);

            // Invisible, but the raycast target that makes a click anywhere in the row start editing —
            // including on an EMPTY row, which has no glyphs to hit. The click is handled by this component
            // (OnPointerClick), which the event system finds by bubbling up from here.
            var areaBg = areaGO.AddComponent<Image>();
            areaBg.color = new Color(1f, 1f, 1f, 0.01f);

            if (data.Kind == BlockKind.Image)
            {
                BuildPicture();
                return;
            }

            if (data.Kind == BlockKind.Canvas)
            {
                // A board row is a picture AND a line of text: the frame on top, the caption under it, and the
                // caption is DocBlock.Text edited exactly like any other row — so the text machinery is built
                // as usual, only pushed down into its own strip.
                //
                // ANCHORS FIRST, THEN OFFSETS, and never sizeDelta: writing anchorMin/anchorMax from script
                // leaves offsetMin/offsetMax alone, so the box the lines above described (stretched top to
                // bottom) would collapse to zero height ON the row's bottom edge, and a sizeDelta.y written
                // afterwards would grow it symmetrically around the pivot — hanging half the caption below
                // the row.
                BuildCanvasFrame(font);
                textArea.anchorMin = new Vector2(0f, 0f);
                textArea.anchorMax = new Vector2(1f, 0f);
                textArea.offsetMin = new Vector2(indent, 0f);
                textArea.offsetMax = new Vector2(-IndentBase, CaptionHeight);
            }

            BuildDisplay();
            BuildField();

            RefreshDisplay();
        }

        /// <summary>An image row: a picture and a selection ring, and none of the text machinery.
        ///
        /// IT HAS NO TEXT STATE AT ALL — no display, no field — which every text path in this class already
        /// tolerates by null-guarding `Display`/`Field`, and which the PURE layer already agrees with:
        /// DocKeyboardOps.HasText excludes Image, so no rule tries to put a caret in one. Selection replaces
        /// focus for it (see SetSelected), because the only thing a DM does to a picture in prose is remove
        /// it.
        ///
        /// NOT RESIZABLE and with no caption in Р2. DisplayHeight is stored and honoured, so a later task can
        /// add a drag handle that writes it, and a row saved with one already renders at that height.</summary>
        void BuildPicture()
        {
            var frameGO = new GameObject("Picture", typeof(RectTransform));
            frameGO.transform.SetParent(textArea, false);
            Stretch(frameGO.GetComponent<RectTransform>(), TextInset);

            // The ring is BEHIND the picture and slightly larger, so a selected image is outlined rather than
            // tinted — a tint over a photograph reads as damage to the photograph.
            var ringGO = new GameObject("SelectionRing", typeof(RectTransform));
            ringGO.transform.SetParent(frameGO.transform, false);
            var ringRect = ringGO.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.offsetMin = new Vector2(-3f, -3f);
            ringRect.offsetMax = new Vector2(3f, 3f);
            selectionRing = ringGO.AddComponent<Image>();
            ThemeService.Tag(selectionRing, ThemeRole.Accent);
            ringGO.SetActive(false);

            var pictureGO = new GameObject("Bitmap", typeof(RectTransform));
            pictureGO.transform.SetParent(frameGO.transform, false);
            Stretch(pictureGO.GetComponent<RectTransform>());
            picture = pictureGO.AddComponent<RawImage>();

            // Texture2D.LoadImage the same way ImageObjectView does it — one decode at build time, and the
            // first frame only for an animated GIF, which is that class's documented limitation and equally
            // this one's.
            var texture = new Texture2D(2, 2);
            if (data.ImageBytes != null && data.ImageBytes.Length > 0 && texture.LoadImage(data.ImageBytes))
            {
                picture.texture = texture;
                pictureAspect = texture.height > 0 ? (float)texture.width / texture.height : 0f;
            }
            else
            {
                // A row whose bytes are missing or unreadable stays visible as an empty frame rather than
                // collapsing to nothing: a picture the DM cannot see but CAN select is one they can delete.
                picture.color = ThemeService.Get(ThemeRole.Panel2);
                pictureAspect = 0f;
            }
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
                // Уезжает вниз вместе с текстом заголовка: воздух перед разделом добавляется НАД строкой,
                // и значок сворачивания обязан остаться на одной линии с буквами.
                collapseRect.anchoredPosition = new Vector2(0f, -2f - SectionLeadGap);
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

        /// <summary>The board's frame: a masked box, its own resize grip, and an «развернуть» button.
        ///
        /// A CENTRED CHILD ALLOWED TO EXCEED ITS PARENT, and this is the whole trick of "wider than the text
        /// column". The row's WIDTH is owned by the column's VerticalLayoutGroup and cannot be argued with —
        /// so the frame is not the row, it is a child anchored to the row's horizontal centre with an explicit
        /// width of its own. uGUI does not clip a child that overflows unless something masks it, and the mask
        /// here belongs to the frame. The row's HEIGHT is still the block's to decide (ApplyHeightNow).</summary>
        void BuildCanvasFrame(Font font)
        {
            var frameGO = new GameObject("CanvasFrame", typeof(RectTransform));
            frameGO.transform.SetParent(transform, false);
            canvasFrame = frameGO.GetComponent<RectTransform>();
            canvasFrame.anchorMin = new Vector2(0.5f, 1f);
            canvasFrame.anchorMax = new Vector2(0.5f, 1f);
            canvasFrame.pivot = new Vector2(0.5f, 1f);
            canvasFrame.anchoredPosition = new Vector2(0f, -VerticalPadding * 0.5f);

            var bg = frameGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2);
            // A RAYCAST TARGET, and it has to be: Ctrl+wheel zooms the board (CanvasWheelRelay, attached to
            // this same object), and uGUI delivers a scroll by walking UP from whatever the pointer is over —
            // so over the board's EMPTY space, where no card is, nothing would be walked up from and the
            // gesture would work on the cards and nowhere else.
            //
            // This was false through Task 8, for a real defect: as a target it bubbles every click on the
            // board up to this row's OnPointerClick, which put a caret in the CAPTION. That is fixed at the
            // handler instead — OnPointerClick now ignores a click that did not land in the caption strip —
            // which is the narrower fix of the two and the one that survives this object needing to be
            // hittable. The cards, the grip and «развернуть» carry their own targets and are unaffected.
            bg.raycastTarget = true;
            frameGO.AddComponent<RectMask2D>();

            // «развернуть» — top right, inside the frame. Legacy uGUI and the builtin font, like the collapse
            // arrow and the bullet: this is chrome, and chrome does not migrate to TMP.
            var expandGO = new GameObject("Expand", typeof(RectTransform));
            expandGO.transform.SetParent(frameGO.transform, false);
            var expandRect = expandGO.GetComponent<RectTransform>();
            expandRect.anchorMin = new Vector2(1f, 1f);
            expandRect.anchorMax = new Vector2(1f, 1f);
            expandRect.pivot = new Vector2(1f, 1f);
            expandRect.anchoredPosition = new Vector2(-4f, -4f);
            expandRect.sizeDelta = new Vector2(20f, 20f);
            var expandBg = expandGO.AddComponent<Image>();
            ThemeService.Tag(expandBg, ThemeRole.Panel2, 0.9f);
            var expandButton = expandGO.AddComponent<Button>();
            expandButton.targetGraphic = expandBg;
            expandButton.onClick.AddListener(() => OnExpandCanvasRequested?.Invoke(data.Id));

            var expandGlyphGO = new GameObject("Glyph", typeof(RectTransform));
            expandGlyphGO.transform.SetParent(expandGO.transform, false);
            var expandGlyph = expandGlyphGO.AddComponent<Text>();
            expandGlyph.font = font;
            expandGlyph.fontSize = 13;
            // ↗ (U+2197) rather than the ⤢ the plan named: ⤢ is U+2922, in Supplemental Arrows-B, which the
            // builtin LegacyRuntime font does not carry — it would draw as an empty box. This one is in the
            // same Arrows block as the ↑↓ already shipping in QuickOpenPopup's hint line.
            expandGlyph.text = "↗";
            expandGlyph.alignment = TextAnchor.MiddleCenter;
            expandGlyph.raycastTarget = false;
            ThemeService.Tag(expandGlyph, ThemeRole.Txt);
            var expandGlyphRect = expandGlyphGO.GetComponent<RectTransform>();
            expandGlyphRect.anchorMin = Vector2.zero;
            expandGlyphRect.anchorMax = Vector2.one;
            expandGlyphRect.sizeDelta = Vector2.zero;

            // The grip — bottom right, both axes.
            var gripGO = new GameObject("ResizeGrip", typeof(RectTransform));
            gripGO.transform.SetParent(frameGO.transform, false);
            var gripRect = gripGO.GetComponent<RectTransform>();
            gripRect.anchorMin = new Vector2(1f, 0f);
            gripRect.anchorMax = new Vector2(1f, 0f);
            gripRect.pivot = new Vector2(1f, 0f);
            gripRect.sizeDelta = new Vector2(16f, 16f);
            var gripImage = gripGO.AddComponent<Image>();
            ThemeService.Tag(gripImage, ThemeRole.Accent, 0.6f);
            var resizer = gripGO.AddComponent<CanvasBlockResizer>();
            resizer.Initialize(this);

            ApplyCanvasFrameSize();
        }

        /// <summary>Writes the frame's current size from the block's stored one. 0 means "the default": the
        /// text column's width, and DefaultCanvasHeight.
        ///
        /// PUBLIC AND CALLED TWICE PER REBUILD. Initialize runs before DocumentPageView has had a chance to
        /// assign MaxCanvasWidth, so the first sizing cannot know the pane's width; the page calls this again
        /// right after the assignment. Without that second call a board saved wider than the pane it is
        /// reopened in would render unclamped until the DM happened to drag its grip.</summary>
        public void ApplyCanvasFrameSize()
        {
            if (canvasFrame == null || data == null) return;
            float width = data.DisplayWidth > 0f ? data.DisplayWidth : NotesTypography.MeasureWidthPx;
            if (MaxCanvasWidth > 1f) width = Mathf.Min(width, MaxCanvasWidth);
            width = Mathf.Max(MinCanvasWidth, width);
            float height = Mathf.Max(MinCanvasHeight, data.DisplayHeight > 0f ? data.DisplayHeight : DefaultCanvasHeight);
            canvasFrame.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Called live by CanvasBlockResizer while the grip is dragged: writes the block, resizes the
        /// frame and re-measures the row, all within the frame the DM is dragging in. The UNDO step is not
        /// pushed here — one per drag, at its end, see DocumentPageView.
        ///
        /// SEEDED FROM THE FRAME ON SCREEN, not from the stored size. The two disagree exactly when the stored
        /// width was clamped to a narrower pane — and starting from the stored value there would re-clamp to
        /// the same number, so the first several pixels of the drag would visibly do nothing.</summary>
        public void ResizeCanvasBy(Vector2 delta)
        {
            if (data == null || data.Kind != BlockKind.Canvas) return;
            float shownWidth = canvasFrame != null ? canvasFrame.rect.width : 0f;
            float shownHeight = canvasFrame != null ? canvasFrame.rect.height : 0f;
            float width = (shownWidth > 1f ? shownWidth : (data.DisplayWidth > 0f ? data.DisplayWidth : NotesTypography.MeasureWidthPx)) + delta.x;
            // Minus: PointerEventData.delta is screen-space with +y up, so dragging the grip DOWN is negative
            // and must make the board taller.
            float height = (shownHeight > 1f ? shownHeight : (data.DisplayHeight > 0f ? data.DisplayHeight : DefaultCanvasHeight)) - delta.y;
            float maxWidth = MaxCanvasWidth > 1f ? MaxCanvasWidth : NotesTypography.MeasureWidthPx;
            data.DisplayWidth = Mathf.Clamp(width, MinCanvasWidth, Mathf.Max(MinCanvasWidth, maxWidth));
            data.DisplayHeight = Mathf.Max(MinCanvasHeight, height);
            ApplyCanvasFrameSize();
            ApplyHeightNow();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
        }

        public void RaiseCanvasResizeBegan() => OnCanvasResizeBegan?.Invoke(data != null ? data.Id : null);
        public void RaiseCanvasResizeEnded() => OnCanvasResizeEnded?.Invoke(data != null ? data.Id : null);

        void BuildDisplay()
        {
            var displayGO = new GameObject("Display", typeof(RectTransform));
            displayGO.transform.SetParent(textArea, false);
            var r = displayGO.GetComponent<RectTransform>();
            Stretch(r, TextInset);

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
            // The INSET lives here rather than on the field's own rect: the field carries the raycast target
            // that keeps a click in the margin working while editing, so it must still span the whole row.
            Stretch(viewportRect, TextInset);
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

        static void Stretch(RectTransform r, float horizontalInset = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(horizontalInset, 0f);
            r.offsetMax = new Vector2(-horizontalInset, 0f);
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
                historyPushedThisVisit = false;
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
            HealStoredNames();
            RefreshDisplay();
        }

        /// <summary>Brings each token's stored name up to date with its target's current one, as the row
        /// stops being edited. Purely cosmetic — resolution is the mechanism, and nothing reads the stored
        /// copy while the target exists — but it keeps the RAW text the DM meets next time from naming
        /// something that was renamed months ago. NotesLinkOps.RefreshStoredNames owns the rule and its
        /// tests; this is only where it is applied.
        ///
        /// Silent when nothing changed, and that is the point: the project is marked dirty from
        /// OnTextChanged, so an unconditional rewrite would ask the DM to save for having looked at a line.
        ///
        /// The one corner it can be wrong in, named rather than guarded: if a target is renamed WHILE this
        /// row is open and the DM leaves it with Enter, the text under a caret DocKeyboardController already
        /// read shifts by the name's change in length, so the split lands a few characters off. It needs a
        /// rename in another pane during an open edit, it loses no text, and the alternative — deferring the
        /// heal until the row is provably idle — is machinery for a case the DM cannot reach on purpose.</summary>
        void HealStoredNames()
        {
            if (data == null || Resolver == null) return;
            string healed = NotesLinkOps.RefreshStoredNames(data.Text ?? "", Resolver, out bool changed);
            if (!changed) return;
            data.Text = healed;
            OnTextChanged?.Invoke(data.Id);
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

        /// <summary>The field reporting a new value — which, and this is the whole reason for the comparison
        /// below, does NOT mean the value is new.
        ///
        /// TMP RAISES onValueChanged FOR EDITS THAT REMOVED NOTHING. Its Backspace asks `hasSelection`, and
        /// that property compares the STRING positions (TMP_InputField.cs:996), not the caret positions the
        /// rest of the field exposes; when those two pairs disagree it takes the selection branch, calls
        /// Delete() — which returns immediately, because by ITS measure the range is empty
        /// (TMP_InputField.cs:3044) — and then fires the callback anyway, unconditionally
        /// (TMP_InputField.cs:3168-3173). The value handed to us is byte-for-byte the one we already had.
        ///
        /// THE DEFECT THAT COST A CHECKPOINT ROUND. TextChangedThisFrame is the flag DocKeyboardController
        /// uses to tell "the DM pressed Backspace at the START of the row" (ours: merge with the row above)
        /// from "the DM deleted the first character and the field moved the caret to 0" (not ours). Setting it
        /// on a callback that removed nothing made the FIRST case look like the second, so Backspace at offset
        /// 0 silently did nothing at all — every time, on an empty row most of all. Comparing the strings
        /// makes the flag mean what its name says, and it is the honest fix rather than reaching for TMP's
        /// private notion of a selection: what the caller needs to know is whether the TEXT moved, and that
        /// question has an exact answer right here.
        ///
        /// It also stops a no-op keystroke from marking the project dirty, which is a second, quieter thing
        /// this was doing.</summary>
        void OnFieldChanged(string value)
        {
            if (data == null) return;
            string previous = data.Text ?? "";
            string next = value ?? "";
            data.Text = value;
            if (previous == next) return;

            // ONE UNDO STEP PER VISIT TO A ROW. The page is told the text as it was BEFORE this first
            // keystroke, and not again until the DM leaves the row and comes back — so Ctrl+Z takes a row
            // back to what it was when they entered it. Raised from here rather than from BeginEdit because
            // merely LOOKING at a row must not become a step in the history.
            if (!historyPushedThisVisit)
            {
                historyPushedThisVisit = true;
                OnBeforeFirstEdit?.Invoke(data.Id, previous);
            }

            textChangedOnFrame = Time.frameCount;
            OnTextChanged?.Invoke(data.Id);
        }

        /// <summary>A click anywhere in the row's text area starts editing, with the caret on the character
        /// clicked. Reached by event bubbling from the invisible Image on TextArea — the bullet and the
        /// collapse arrow are Buttons and consume their own clicks before this ever sees them.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            // A picture takes selection instead of a caret — see SetSelected.
            if (data != null && data.Kind == BlockKind.Image)
            {
                OnSelectRequested?.Invoke(data.Id);
                return;
            }

            // ON A BOARD ROW ONLY THE CAPTION TAKES A CARET. Unity delivers a click to the nearest ancestor
            // carrying IPointerClickHandler, and nothing on the board has one — a card handles press/drag/
            // release, not click — so every click INSIDE the board would arrive here and put a caret in the
            // caption, including the press that begins dragging a card.
            if (data != null && data.Kind == BlockKind.Canvas && textArea != null
                && !RectTransformUtility.RectangleContainsScreenPoint(textArea, eventData.position, eventData.pressEventCamera))
                return;

            if (editing || Display == null) return;
            if (TryActivateLink(eventData)) return;
            BeginEdit(SourceCaretFromPointer(eventData));
        }

        /// <summary>A click that landed on a rendered link goes to the link's target instead of into the
        /// text. Only an activated link returns true, so everything else still opens the row for editing —
        /// including a click on a MUTED span, which carries no `<link>` tag at all and therefore cannot be
        /// found here (the deleted-target rule: it is prose again, and prose is editable).
        ///
        /// The id travels as the token's own "kind:id", split at the FIRST colon exactly as the grammar
        /// itself splits it, so the two can never drift apart in what they consider an id.</summary>
        bool TryActivateLink(PointerEventData eventData)
        {
            if (OnLinkActivated == null) return false;

            int index = TMP_TextUtilities.FindIntersectingLink(Display, eventData.position, eventData.pressEventCamera);
            if (index < 0 || Display.textInfo == null || index >= Display.textInfo.linkCount) return false;

            string payload = Display.textInfo.linkInfo[index].GetLinkID();
            int colon = string.IsNullOrEmpty(payload) ? -1 : payload.IndexOf(':');
            if (colon <= 0 || colon >= payload.Length - 1) return false;

            OnLinkActivated.Invoke(payload.Substring(0, colon), payload.Substring(colon + 1));
            return true;
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
            => SourceCaretFromScreen(eventData.position, eventData.pressEventCamera, out _);

        /// <summary>The same answer for a pointer that is not a click — a drag hovering over this row (Task
        /// 11). Reports the DISPLAY offset as well, because an insertion marker has to be drawn against the
        /// glyphs the DM can see, and converting a source offset back into a display one would be a second
        /// mapping that could disagree with this one.</summary>
        public int SourceCaretFromScreen(Vector2 screenPos, Camera cam, out int displayCaret)
        {
            displayCaret = 0;
            string source = data?.Text ?? "";
            if (source.Length == 0 || Display == null) return 0;

            int nearest = TMP_TextUtilities.FindNearestCharacter(Display, screenPos, cam, true);
            var info = Display.textInfo;
            if (nearest < 0 || info == null || nearest >= info.characterCount)
            {
                displayCaret = info != null ? info.characterCount : 0;
                return source.Length;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Display.rectTransform, screenPos, cam, out var local);

            var ci = info.characterInfo[nearest];
            displayCaret = local.x < (ci.origin + ci.xAdvance) * 0.5f ? nearest : nearest + 1;
            return DisplayToSource(displayCaret);
        }

        /// <summary>Where to draw an insertion marker for a display offset, in world space: the top and
        /// bottom of the caret's line, and the x it sits at.
        ///
        /// READS THE RESTING LABEL (`Display`), not the edit field, because this answers for a row nobody is
        /// editing — the row a drag is hovering over. TryGetCaretLineWorldY is its counterpart for the row
        /// that IS being edited, and the two are separate for exactly that reason rather than by
        /// oversight.</summary>
        public bool TryGetDropMarker(int displayCaret, out Vector3 top, out Vector3 bottom)
        {
            top = bottom = Vector3.zero;
            if (Display == null) return false;

            var rt = Display.rectTransform;
            var info = Display.textInfo;
            if (info == null || info.characterCount == 0 || info.lineCount == 0)
            {
                // An empty row still has a place to drop into — its own rect, one line tall by construction.
                rt.GetWorldCorners(cornerBuffer);
                top = cornerBuffer[1];
                bottom = cornerBuffer[0];
                return true;
            }

            // A caret one past the last character sits at that character's TRAILING edge; every other caret
            // sits at its own character's leading edge.
            bool afterLast = displayCaret >= info.characterCount;
            int index = Mathf.Clamp(afterLast ? info.characterCount - 1 : displayCaret, 0, info.characterCount - 1);
            var ci = info.characterInfo[index];
            float x = afterLast ? ci.origin + ci.xAdvance : ci.origin;

            var line = info.lineInfo[Mathf.Clamp(ci.lineNumber, 0, info.lineCount - 1)];
            top = rt.TransformPoint(new Vector3(x, line.ascender, 0f));
            bottom = rt.TransformPoint(new Vector3(x, line.descender, 0f));
            return true;
        }

        /// <summary>Whether a link can be dropped INTO this row's text at all. A picture has no text to put
        /// one in, and a card's row is a rendered reference rather than prose the DM owns — both take a drop
        /// as a new row beside them instead.</summary>
        public bool AcceptsText => data != null && data.Kind != BlockKind.Image && data.Kind != BlockKind.BoardRef;

        /// <summary>Display offset → source offset, through the map the last RefreshDisplay built. Without it
        /// every click into a row containing a link would miss by exactly the machinery's length — the
        /// brackets, the kind, the id and the pipe the DM never sees.</summary>
        // The null case is a row that survived a domain reload: field initializers do not re-run on
        // deserialization, so this comes back null on a component that is otherwise alive. Such a row is
        // destroyed by the next Rebuild (DocumentPageView.EnsureWired's own note), but a click can reach it
        // first, and the identity is the right answer for a row that has rendered nothing since.
        int DisplayToSource(int displayCaret) => displayText != null ? displayText.ToSource(displayCaret) : displayCaret;

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

        /// <summary>What the row shows at rest: every [[token]] replaced by its target's CURRENT name, drawn
        /// in the accent colour and made clickable — or, when the target is gone, drawn muted and NOT
        /// clickable, because prose about something that no longer exists has nowhere to go. A page link whose
        /// target carries a character card additionally gets a plate behind its name (Задача 9) — still the
        /// same [[page:id|Имя]] token, only wrapped differently; see CharacterOps.IsCharacterLink.
        ///
        /// THE COLOUR TAG SITS INSIDE THE LINK TAG, and that nesting was verified against TMP's parser rather
        /// than assumed: `</link>` closes with `linkTextLength = m_characterCount - linkTextfirstCharacterIndex`
        /// (TMP_Text.cs:7620), and a `<color>` tag advances no character count, so the link's character range —
        /// the only thing FindIntersectingLink hit-tests (TMP_TextUtilities.cs:1377-1381) — is unaffected by
        /// what is nested inside it. `<mark>`, the plate's tag, is the SAME kind of tag by TMP's own reckoning
        /// — a formatting instruction with no glyph of its own — so nesting it in the same place changes
        /// nothing this reasoning already covers.
        ///
        /// The index map built here is kept: it is what turns a click at a rendered character into a caret in
        /// the RAW text the editing field shows.</summary>
        void RefreshDisplay()
        {
            if (Display == null || data == null) return;

            displayText = NotesLinkOps.BuildDisplay(data.Text ?? "", Resolver);
            markupAccent = ThemeService.Get(ThemeRole.Accent);
            Display.text = BuildMarkup(displayText);
        }

        /// <summary>The rendered string with TMP tags around each span. Prose between spans is copied
        /// verbatim — including any `<` the DM typed, which TMP will read as a tag it does not know and drop.
        /// That is Task 6's behaviour unchanged (the resting row has had rich text on since the port) and it
        /// is left for a later cleanup rather than fixed here with `<noparse>`, which would put a second class
        /// of tag inside the index map in the very task that first makes display and source differ.</summary>
        string BuildMarkup(DisplayText d)
        {
            if (d == null) return "";
            bool hasSpans = d.Spans.Count > 0;
            bool hasHits = searchHits != null && searchHits.Count > 0;
            if (!hasSpans && !hasHits) return d.Text;

            string accent = ColorUtility.ToHtmlStringRGB(ThemeService.Get(ThemeRole.Accent));
            string muted = ColorUtility.ToHtmlStringRGB(ThemeService.Get(ThemeRole.Mut));
            // The character plate's background. AccentSoft, not a new colour: it is already this project's
            // "soft accent background" role (ConfirmDialog's plate, the navigator's active row, NotesToolbar's
            // active button). 0.65 alpha copies NotesToolbar.ThemedAlpha(AccentSoft, 0.65f) — the closest
            // existing precedent for "this control is a highlighted plate", not a fresh number invented here.
            string plate = ColorUtility.ToHtmlStringRGB(ThemeService.Get(ThemeRole.AccentSoft)) + "A6";

            // TAGS AS INSERTIONS AT POSITIONS, rather than as a single walk over the spans, because two
            // INDEPENDENT sets of ranges now cover the same display text — the links, and the search hits —
            // and they overlap freely: a searched-for word can sit inside a link's name, start before it and
            // end inside it, or contain the whole link. A walk that owned one set and spliced the other into
            // it would have to reason about every one of those cases. Collecting both as boundary markers and
            // sorting them does not: TMP keeps a separate stack per tag type, so `<mark>` opened inside a
            // `<link>` and closed after it is read exactly as written — for `<mark>` VERSUS `<link>`/`<color>`,
            // which are different tag types with their own stacks. `<mark>` versus `<mark>` is a different
            // story — see IsCharacterPlateSuppressedByHit below, right where the character plate's own `<mark>`
            // is decided.
            insertions.Clear();

            foreach (var span in d.Spans)
            {
                int end = span.DisplayStart + span.DisplayLength;
                if (span.Resolved)
                {
                    // Задача 9: a link whose target carries a character card is drawn as a PLATE, still inside
                    // the same <link> and <color> a page link already gets — a character mention must read as
                    // a SIBLING of an ordinary link, not a foreign control. No new token kind exists for this
                    // (see NotesLinkOps' class doc and CharacterOps.IsCharacterLink) — the distinction is drawn
                    // purely, entirely in what wraps the same [[page:id|Имя]] this always was.
                    //
                    // <mark> NESTED INSIDE <link>, SAME AS <color> ALREADY IS. Verified against the same TMP
                    // mechanics the class doc above cites for <color>: neither tag advances the character
                    // count TMP hit-tests a link's range by (TMP_Text.cs's linkTextLength is a character-count
                    // difference), so nesting <mark> here changes nothing FindIntersectingLink or the index map
                    // (DisplayText.ToSource/ToDisplay, built from DisplayStart/DisplayLength alone) can see.
                    //
                    // SUPPRESSED WHEN A SEARCH HIT TOUCHES THIS SPAN — see IsCharacterPlateSuppressedByHit's
                    // own doc for why, and for the rule chosen and its cost.
                    bool isCharacter = IsCharacterLink != null && IsCharacterLink(span.Kind, span.Id)
                        && !IsCharacterPlateSuppressedByHit(span.DisplayStart, end);
                    string open = "<link=\"" + span.Kind + ":" + span.Id + "\">";
                    string close;
                    if (isCharacter)
                    {
                        open += "<mark=#" + plate + ">";
                        close = "</mark>";
                    }
                    else
                    {
                        close = "";
                    }
                    open += "<color=#" + accent + ">";
                    close = "</color>" + close + "</link>";
                    insertions.Add((span.DisplayStart, false, open));
                    insertions.Add((end, true, close));
                }
                else
                {
                    insertions.Add((span.DisplayStart, false, "<color=#" + muted + ">"));
                    insertions.Add((end, true, "</color>"));
                }
            }

            if (hasHits)
            {
                // The current hit is painted stronger than the rest — «эта, из двенадцати» has to be legible
                // at a glance, or stepping through matches tells the DM nothing.
                var accentColor = ThemeService.Get(ThemeRole.Accent);
                string rest = ColorUtility.ToHtmlStringRGB(accentColor) + "44";
                string here = ColorUtility.ToHtmlStringRGB(accentColor) + "99";
                foreach (var hit in searchHits)
                {
                    if (hit == null || hit.Length <= 0) continue;
                    int start = Mathf.Clamp(hit.DisplayStart, 0, d.Text.Length);
                    int end = Mathf.Clamp(start + hit.Length, start, d.Text.Length);
                    if (end == start) continue;
                    insertions.Add((start, false, "<mark=#" + (ReferenceEquals(hit, currentSearchHit) ? here : rest) + ">"));
                    insertions.Add((end, true, "</mark>"));
                }
            }

            // At one position every CLOSING tag comes before every opening one, so a link that ends exactly
            // where the next begins does not get them interleaved. Sorted stably by position first so the
            // text between two insertions is always a forward slice.
            insertions.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At)
                                                  : (a.Closing == b.Closing ? 0 : a.Closing ? -1 : 1));

            var sb = new System.Text.StringBuilder(d.Text.Length + insertions.Count * 24);
            int copied = 0;
            foreach (var insertion in insertions)
            {
                int at = Mathf.Clamp(insertion.At, copied, d.Text.Length);
                sb.Append(d.Text, copied, at - copied);
                sb.Append(insertion.Tag);
                copied = at;
            }
            sb.Append(d.Text, copied, d.Text.Length - copied);
            return sb.ToString();
        }

        /// <summary>Whether a search hit overlaps [start, end) closely enough that drawing the character
        /// plate there would be unsafe — see the review finding this exists to fix.
        ///
        /// THE HAZARD: the plate and a search-hit highlight are both `&lt;mark&gt;`. TMP keeps exactly ONE
        /// stack for that tag (HighlightStateStack, and its Remove() pops the top with no tag matching) —
        /// so two PROPERLY NESTED `&lt;mark&gt;` ranges are safe, but two that only PARTIALLY overlap
        /// ("start before it and end inside it") open-open-close-close instead of open-close-open-close,
        /// and the second close pops the wrong range. A DM's search term landing partway across a
        /// character's name is exactly that case, and it is reachable the instant they search.
        ///
        /// THE RULE CHOSEN: the search highlight wins outright. Rather than split the plate's own `&lt;mark&gt;`
        /// into the sub-ranges the hit does not cover — which would need the same interval-splitting logic
        /// twice, once for each direction a hit can lean into a span — a span with ANY overlapping hit
        /// simply gets no plate `&lt;mark&gt;` at all this render. The `&lt;link&gt;`/`&lt;color&gt;` wrapping is
        /// untouched either way (different stack, see the comment above the insertions loop), so the name
        /// still reads and still opens on click.
        ///
        /// THE COST, STATED RATHER THAN DISCOVERED LATER: a character's plate blinks off for exactly the
        /// rows/frames where the DM's current search overlaps its name, and returns the moment the hit
        /// moves on or the search is cleared. Narrower and cheaper than getting interval splitting wrong.</summary>
        bool IsCharacterPlateSuppressedByHit(int start, int end)
        {
            if (searchHits == null) return false;
            foreach (var hit in searchHits)
            {
                if (hit == null || hit.Length <= 0) continue;
                int hitStart = hit.DisplayStart;
                int hitEnd = hitStart + hit.Length;
                // Half-open interval overlap: [start, end) meets [hitStart, hitEnd) iff each starts before
                // the other ends. Touching at a boundary (hitEnd == start or hitStart == end) is NOT an
                // overlap — adjacent ranges never cross, only ones that share an interior point do.
                if (hitStart < end && start < hitEnd) return true;
            }
            return false;
        }

        /// <summary>Scratch for BuildMarkup. NOT a field initializer for the usual reason — a domain reload
        /// deserializes this component without running one — so it is created on demand instead.</summary>
        List<(int At, bool Closing, string Tag)> insertionBuffer;
        List<(int At, bool Closing, string Tag)> insertions
            => insertionBuffer ?? (insertionBuffer = new List<(int, bool, string)>());

        // ── search highlighting (page search) ────────────────────────────────────

        List<PageSearch.PageHit> searchHits;
        PageSearch.PageHit currentSearchHit;

        /// <summary>Paints this row's search matches. `hits` are the ones in THIS block — the bar has already
        /// split them up — and `current` is the one the DM is standing on, which may belong to another row, in
        /// which case none here is painted as current.
        ///
        /// A row with no hits before and none now is left alone: the bar hands this to EVERY row on every
        /// keystroke in the search field, and on a page-long document most rows never match anything. The
        /// guard skips only that case — it never skips a repaint a change actually needs.</summary>
        public void SetSearchHighlights(List<PageSearch.PageHit> hits, PageSearch.PageHit current)
        {
            bool had = searchHits != null && searchHits.Count > 0;
            bool has = hits != null && hits.Count > 0;
            if (!had && !has && ReferenceEquals(current, currentSearchHit)) return;

            searchHits = hits;
            currentSearchHit = current;
            RefreshDisplay();
        }

        /// <summary>Re-renders when the theme changed under a resting row — the link colours are baked into
        /// the markup and nothing else repaints them (see markupAccent). Called from LateUpdate rather than
        /// from a ThemeService subscription so the row keeps knowing nothing about who else is on screen; the
        /// comparison is one Color per row per frame, against a rebuild that runs only when it actually
        /// differs.</summary>
        void RefreshMarkupIfThemeChanged()
        {
            if (editing || Display == null || data == null) return;
            if (displayText == null || displayText.Spans.Count == 0) return;
            if (markupAccent == ThemeService.Get(ThemeRole.Accent)) return;
            RefreshDisplay();
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

            RefreshMarkupIfThemeChanged();
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
            if (layoutElement == null || data == null) return;

            if (data.Kind == BlockKind.Image)
            {
                ApplyPictureHeight();
                return;
            }

            if (data.Kind == BlockKind.Canvas)
            {
                // The board's own height plus the caption strip under it — the caption is a fixed one-line
                // strip, so unlike a prose row this height is not measured from any text.
                float board = Mathf.Max(MinCanvasHeight, data.DisplayHeight > 0f ? data.DisplayHeight : DefaultCanvasHeight);
                float desiredCanvas = board + CaptionHeight + VerticalPadding;
                if (Mathf.Abs(desiredCanvas - appliedHeight) > 0.5f)
                {
                    appliedHeight = desiredCanvas;
                    layoutElement.preferredHeight = desiredCanvas;
                }
                return;
            }

            var measured = editing && fieldText != null ? fieldText : Display;
            if (measured == null) return;
            float lead = data.Kind == BlockKind.Section ? SectionLeadGap : 0f;
            float desired = Mathf.Max(MinRowHeight + lead, measured.preferredHeight + VerticalPadding + lead);
            if (Mathf.Abs(desired - appliedHeight) > 0.5f)
            {
                appliedHeight = desired;
                layoutElement.preferredHeight = desired;
            }
        }

        /// <summary>How tall a picture row is: its stored height if it has one, and otherwise the height that
        /// keeps the image's own proportions at the width the column gives it. Falls back to a fixed height
        /// while the column has no width yet (the first frame after a rebuild) or when there was no texture
        /// to measure, so an unreadable image is still a row the DM can see and select.</summary>
        void ApplyPictureHeight()
        {
            float stored = data.DisplayHeight;
            float desired;
            if (stored > 0f)
            {
                desired = stored;
            }
            else
            {
                float width = textArea != null ? textArea.rect.width - TextInset * 2f : 0f;
                desired = width > 1f && pictureAspect > 0f ? width / pictureAspect : PictureFallbackHeight;
            }

            desired = Mathf.Max(MinRowHeight, desired + VerticalPadding);
            if (Mathf.Abs(desired - appliedHeight) > 0.5f)
            {
                appliedHeight = desired;
                layoutElement.preferredHeight = desired;
            }
        }

        /// <summary>Draws this row as selected. A picture cannot hold a caret, so selection is what focus is
        /// for it: DocKeyboardOps answers a Backspace or an arrow that reaches one with SelectBlockId, and the
        /// next Backspace deletes it (DocKeyboardOps.OnBackspace's first branch). Harmless on a text row,
        /// which simply has no ring to show.</summary>
        public void SetSelected(bool selected)
        {
            if (selectionRing != null) selectionRing.gameObject.SetActive(selected);
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
