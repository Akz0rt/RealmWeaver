using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The one thing a Document page shows about itself that is not prose: the character card, when
    /// `NotesPage.Character` is set. Portrait 96×96 on the left, four labelled fields on the right —
    /// «Кто · Где искать · Чего хочет · Как играть» — each growing with its own text.
    ///
    /// ABSENT, NOT EMPTY, exactly like PageFooterView's two lists. `Show`/`Hide` are the whole public
    /// surface DocumentPageView drives: shown above the rows when CharacterOps.IsCharacter(Page), hidden
    /// otherwise. An ordinary page never sees this object active at all.
    ///
    /// CHROME, NOT A DocBlock. It lives inside `content` — a sibling the rows share their scroll with —
    /// but it takes no part in NotesDocOps.VisibleIndices, cannot be dragged, collapsed or deleted, and
    /// DocumentPageView.Rebuild never touches its GameObject the way it destroys and remakes every row.
    ///
    /// BUILT ONCE, UPDATED IN PLACE — unlike PageFooterView, which throws its rows away on every
    /// Refresh() because they hold no state worth keeping. This object's four fields ARE live
    /// TMP_InputFields the DM may be mid-keystroke in, and DocumentPageView.Rebuild() runs far more
    /// often than the character card actually changes (any structural edit to any ROW rebuilds the
    /// whole page). Attach() builds the chrome exactly once; every later Rebuild calls Show(), which
    /// only re-syncs text and re-measures height — the same "SetTextWithoutNotify only if it actually
    /// differs" idiom DocBlockView.Refresh() already uses to let an undo update a field's displayed text
    /// without fighting the caret sitting inside it. Ctrl+Z routes through DocKeyboardController's own
    /// keyboard poll and does NOT care which UI element currently holds focus (see that class's own
    /// doc on why undo "must work with nothing focused at all") — so an undo CAN land while the DM's
    /// caret is inside one of these fields, and Show() must overwrite it unconditionally rather than
    /// skip a focused field, or Ctrl+Z on a header field would silently do nothing.
    ///
    /// THE HEIGHT SETTLES IN THE SAME FRAME AS THE REBUILD, same trap as every row's own height
    /// (DocumentPageView.SettleHeights' own doc). This class carries no ContentSizeFitter — `content`'s
    /// VerticalLayoutGroup already controls this object's height through its LayoutElement, and putting
    /// a fitter on top of that is the trap that has bitten this project repeatedly. ApplyHeightNow is
    /// public and called a second time from SettleHeights' own two-pass, after the column has a real
    /// width, for the same reason DocBlockView.ApplyHeightNow is: a header that measured short on the
    /// first frame would drag the page's scroll position, since it sits ABOVE the rows in the same
    /// scroll view.
    /// </summary>
    public class CharacterHeaderView : MonoBehaviour
    {
        public const string ObjectName = "CharacterHeader";

        const int FieldCount = 4;
        static readonly string[] Captions = { "Кто", "Где искать", "Чего хочет", "Как играть" };
        static readonly string[] Hints =
        {
            "роль, занятие",
            "город, здание, где искать",
            "чего добивается",
            "одна примета: голос, привычка, жест",
        };

        const float TopPadding = 20f;
        const float BottomPadding = 20f;
        const float PortraitSize = 96f;
        const float FieldColumnGap = 18f;
        const float FieldsLeftX = PortraitSize + FieldColumnGap;
        const float LabelWidth = 108f;
        const float LabelValueGap = 10f;
        const float LabelHeight = 18f;
        const float RowGap = 10f;
        const float MinValueHeight = 26f;
        const float FieldTextInset = 8f;

        DocumentPageView host;
        RectTransform self;
        LayoutElement element;
        float appliedHeight = -1f;

        NotesPage page;

        RectTransform portraitRect;
        RawImage picture;
        GameObject silhouetteGO;
        byte[] lastPortraitBytes;
        Texture2D ownedTexture;

        readonly TMP_InputField[] valueFields = new TMP_InputField[FieldCount];
        readonly TMP_Text[] valueTexts = new TMP_Text[FieldCount];
        readonly RectTransform[] labelRects = new RectTransform[FieldCount];
        readonly RectTransform[] valueRects = new RectTransform[FieldCount];
        readonly bool[] historyPushed = new bool[FieldCount];

        /// <summary>Builds the header into the scrolling column, replacing any previous one — the same
        /// "rebuilt rather than repaired" reasoning PageFooterView.Attach documents: a domain reload does
        /// not restore a UnityEvent listener list (Button.onClick, TMP_InputField.onValueChanged/onEndEdit),
        /// so re-finding this object after a reload would give back fields that accept typing and do
        /// nothing with it. Called twice in the object's life — once from Initialize, once from
        /// EnsureWired after a reload — and NEVER from an ordinary Rebuild, which calls Show()/Hide()
        /// instead (see the class doc for why that split matters here and not in PageFooterView).</summary>
        public static CharacterHeaderView Attach(RectTransform content, DocumentPageView host)
        {
            if (content == null || host == null) return null;

            var existing = content.Find(ObjectName);
            if (existing != null) DestroyImmediate(existing.gameObject);

            var go = new GameObject(ObjectName, typeof(RectTransform));
            go.transform.SetParent(content, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var view = go.AddComponent<CharacterHeaderView>();
            view.host = host;
            view.self = rect;
            view.element = go.AddComponent<LayoutElement>();
            view.BuildChrome(host.BodyFont);
            view.element.preferredHeight = 0f;
            go.SetActive(false);
            return view;
        }

        /// <summary>Shows the card for `p` — called from DocumentPageView.Rebuild whenever
        /// CharacterOps.IsCharacter(Page) is true, including for a page that was ALREADY the character
        /// being shown (a plain refresh) and for a page an undo just swapped `Character` out from under.
        ///
        /// `self.SetAsFirstSibling()` reclaims the top of the column every call, the same way
        /// PageFooterView.Refresh reclaims the bottom with SetAsLastSibling — Rebuild's freshly made rows
        /// are appended to `content` AFTER whatever was already there, so without this the header would
        /// drift below them the first time any row rebuild happened.</summary>
        public void Show(NotesPage p)
        {
            if (self == null) return;

            // Другая страница пришла на смену прежней — «уже толкнули снимок в этом визите» относится
            // к СТАРОЙ карточке и здесь не значит ничего. Без сброса первая правка любого поля на НОВОЙ
            // странице не толкнёт снимок вовсе: OnActivePageChanged уже вызвал History.Clear(), так что
            // за отсутствующим снимком нет вообще ничего, и Ctrl+Z на первой же правке карточки B станет
            // пустышкой — то самое правило 4 брифа, потерянное в реальном пути, а не в теории.
            if (!ReferenceEquals(p, page))
                for (int i = 0; i < FieldCount; i++) historyPushed[i] = false;

            page = p;
            gameObject.SetActive(true);
            self.SetAsFirstSibling();

            var card = p != null ? p.Character : null;
            for (int i = 0; i < FieldCount; i++)
            {
                string v = card != null ? (GetField(card, i) ?? "") : "";
                // SetTextWithoutNotify, не обычное присваивание: иначе досинхронизация после отмены сама
                // попала бы в OnValueChanged и родила бы новый шаг отмены поверх только что
                // восстановленного состояния — тот же приём, которым DocBlockView.Refresh синхронизирует
                // редактируемую строку с данными, не трогая то, что уже написано.
                if (valueFields[i] != null && valueFields[i].text != v) valueFields[i].SetTextWithoutNotify(v);
            }
            RefreshPortraitTexture(card);
            ApplyHeightNow();
        }

        /// <summary>Hides the card — called whenever `Page` is not a character. Height goes to zero
        /// immediately rather than waiting for the next measure pass, the same reason PageFooterView zeroes
        /// `element.preferredHeight` when both its lists are empty: a stale non-zero height would hold open
        /// space above the rows of a page that has nothing to show there.</summary>
        public void Hide()
        {
            if (self == null) return;
            page = null;
            if (element != null) element.preferredHeight = 0f;
            appliedHeight = 0f;
            gameObject.SetActive(false);
        }

        // ── layout ────────────────────────────────────────────────────────────

        void BuildChrome(Font font)
        {
            BuildPortrait();
            for (int i = 0; i < FieldCount; i++) BuildFieldRow(i, font);
        }

        void BuildPortrait()
        {
            var maskGO = new GameObject("Portrait", typeof(RectTransform));
            maskGO.transform.SetParent(self, false);
            portraitRect = (RectTransform)maskGO.transform;
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            // Скруглённый фон И маска одним объектом: Image несёт 9-slice RoundedRectSprite (тот же
            // спрайт, которым CardPropertyBar/NoteCardView красят рамку карточки), а Mask поверх него
            // обрезает детей по его же альфе — showMaskGraphic=true, поэтому сам фон при этом виден
            // (в отличие от EditorBrushPanel's viewport-маски, которая существует только ради формы и
            // раскраивается прозрачной).
            var bg = maskGO.AddComponent<Image>();
            bg.sprite = RoundedRectSprite.Get();
            bg.type = Image.Type.Sliced;
            ThemeService.Tag(bg, ThemeRole.Panel2);
            maskGO.AddComponent<Mask>().showMaskGraphic = true;

            var button = maskGO.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(PickPortrait);

            var pictureGO = new GameObject("Bitmap", typeof(RectTransform));
            pictureGO.transform.SetParent(maskGO.transform, false);
            var pictureRect = (RectTransform)pictureGO.transform;
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = Vector2.zero;
            pictureRect.offsetMax = Vector2.zero;
            picture = pictureGO.AddComponent<RawImage>();
            picture.raycastTarget = false;
            pictureGO.SetActive(false);

            var silGO = new GameObject("Silhouette", typeof(RectTransform));
            silGO.transform.SetParent(maskGO.transform, false);
            var silRect = (RectTransform)silGO.transform;
            silRect.anchorMin = new Vector2(0.5f, 0.5f);
            silRect.anchorMax = new Vector2(0.5f, 0.5f);
            silRect.pivot = new Vector2(0.5f, 0.5f);
            silRect.sizeDelta = new Vector2(PortraitSize * 0.6f, PortraitSize * 0.6f);
            var silImg = silGO.AddComponent<Image>();
            silImg.sprite = SilhouetteSprite();
            silImg.raycastTarget = false;
            ThemeService.Tag(silImg, ThemeRole.Mut);
            silhouetteGO = silGO;
        }

        void BuildFieldRow(int i, Font font)
        {
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(self, false);
            labelRects[i] = (RectTransform)labelGO.transform;
            var labelText = labelGO.AddComponent<Text>();
            labelText.font = font;
            labelText.fontSize = 12;
            labelText.text = Captions[i];
            // MiddleLeft, not UpperLeft — the DM asked the caption centred against the value's full
            // height rather than pinned to its first line. Same idiom as PageFooterView.Label
            // (PageFooterView.cs:201) and PageSearchBar's own labels (PageSearchBar.cs:308,320). The text
            // component alone is not enough for this to show — see ApplyHeightNow, which now sizes the
            // label's RECT to the row's full height so there is something to centre within.
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.raycastTarget = false;
            ThemeService.Tag(labelText, ThemeRole.Mut);

            var fieldGO = new GameObject("Value", typeof(RectTransform));
            fieldGO.transform.SetParent(self, false);
            valueRects[i] = (RectTransform)fieldGO.transform;

            var fieldBg = fieldGO.AddComponent<Image>();
            fieldBg.sprite = RoundedRectSprite.Get();
            fieldBg.type = Image.Type.Sliced;
            ThemeService.Tag(fieldBg, ThemeRole.Panel2, 0.9f);

            var viewportGO = new GameObject("TextViewport", typeof(RectTransform));
            viewportGO.transform.SetParent(fieldGO.transform, false);
            var viewportRect = (RectTransform)viewportGO.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(FieldTextInset, FieldTextInset);
            viewportRect.offsetMax = new Vector2(-FieldTextInset, -FieldTextInset);
            viewportGO.AddComponent<RectMask2D>();

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(viewportGO.transform, false);
            var textRect = (RectTransform)textGO.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            ApplyValueStyle(text);
            ThemeService.Tag(text, ThemeRole.Txt);
            valueTexts[i] = text;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(viewportGO.transform, false);
            var placeholderRect = (RectTransform)placeholderGO.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
            ApplyValueStyle(placeholder);
            placeholder.text = Hints[i];
            ThemeService.Tag(placeholder, ThemeRole.Mut);

            var field = fieldGO.AddComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = fieldBg;
            // MultiLineNewline, NOT MultiLineSubmit: unlike a row, a header field is not part of the
            // block flow — Enter has nothing to split — so it is free text and Enter simply writes a
            // newline into it, TMP's ordinary behaviour for this line type.
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.richText = false;
            field.onFocusSelectAll = false;
            field.restoreOriginalTextOnEscape = false;
            field.caretWidth = 1;
            field.customCaretColor = false;

            // Captured into a local, exactly like PageFooterView.Row's reference/id capture — otherwise
            // all four listeners close over the same loop variable and every field would write into the
            // last one.
            int captured = i;
            field.onValueChanged.AddListener(v => OnValueChanged(captured, v));
            field.onEndEdit.AddListener(_ => OnEndEdit(captured));

            // THE WHEEL STOPS AT AN EDITING FIELD WITHOUT THIS — the identical defect and the identical
            // fix DocumentPageView.Rebuild applies to every row's own TMP_InputField (see its own
            // comment): a scroll travels only as far as the first IScrollHandler, and TMP_InputField is
            // one.
            field.gameObject.AddComponent<PageWheelRelay>().page = host;

            valueFields[i] = field;
        }

        void ApplyValueStyle(TMP_Text text)
        {
            NotesTypography.ApplyBody(text);
            // Компактнее обычной прозы страницы — это форма, не абзац; подписи рядом на 12px делают 16px
            // тяжеловесным.
            text.fontSize = 14f;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
        }

        /// <summary>Grows every field to fit its text and settles the header's own height — the same job
        /// DocBlockView.ApplyHeightNow does for one row, done here for four fields plus the portrait.
        /// Public and called from two places: after every text edit (immediate growth while the DM
        /// types), and from DocumentPageView.SettleHeights' own two-pass, after the column has a real
        /// width — see the class doc for why the second call exists at all.</summary>
        public void ApplyHeightNow()
        {
            if (self == null || element == null) return;
            if (!gameObject.activeInHierarchy) return;   // скрыта — измерять нечего, Hide() уже обнулила высоту

            float y = TopPadding;
            for (int i = 0; i < FieldCount; i++)
            {
                float valueH = MinValueHeight;
                if (valueTexts[i] != null)
                    valueH = Mathf.Max(MinValueHeight, valueTexts[i].preferredHeight + FieldTextInset * 2f);
                float rowH = Mathf.Max(LabelHeight, valueH);

                // The label's rect now spans the WHOLE row (rowH), not just LabelHeight — MiddleLeft
                // alignment on the Text component centres within whatever box it is given, and a box
                // pinned at LabelHeight (one line, top of the row) would leave the caption looking
                // unchanged no matter what alignment is set. LabelHeight itself still only sets the
                // row's MINIMUM height a line below (Mathf.Max above) — this does not feed back into
                // rowH, so the height ApplyHeightNow hands to `element.preferredHeight` is unchanged by
                // this edit; only where the label's own rect sits within that height changes. The value
                // field keeps StretchRow untouched — top-aligned text, growing downward, as before.
                FixedRow(labelRects[i], FieldsLeftX, y, LabelWidth, rowH);
                StretchRow(valueRects[i], FieldsLeftX + LabelWidth + LabelValueGap, y, rowH);

                y += rowH;
                if (i < FieldCount - 1) y += RowGap;
            }

            if (portraitRect != null) portraitRect.anchoredPosition = new Vector2(0f, -TopPadding);

            float fieldsHeight = y;
            float portraitHeight = TopPadding + PortraitSize;
            float total = Mathf.Max(fieldsHeight, portraitHeight) + BottomPadding;

            if (Mathf.Abs(total - appliedHeight) > 0.5f)
            {
                appliedHeight = total;
                element.preferredHeight = total;
                // Explicit, not trusted to the property setter alone — DocBlockView.ResizeCanvasBy marks
                // its parent dirty right after the same kind of assignment, and this mirrors that rather
                // than assuming LayoutElement re-triggers content's VerticalLayoutGroup on its own. `self`
                // IS `content`'s child, so `self.parent` reaches it without a stored reference.
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)self.parent);
            }
        }

        /// <summary>Full width from `left` to the header's own right edge, fixed height, hung from the top
        /// by `y` — identical in shape to PageFooterView.Stretch, which is where this was copied from: a
        /// value field must follow the column's width when the pane is resized, so it is expressed as an
        /// anchor stretch rather than a fixed sizeDelta.</summary>
        static void StretchRow(RectTransform rect, float left, float y, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(0f, -y);
        }

        /// <summary>A fixed-width box hung from the top-left by `y`/`left` — the label column, which never
        /// needs to follow the pane's width the way the value beside it does.</summary>
        static void FixedRow(RectTransform rect, float left, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        // ── field data ────────────────────────────────────────────────────────

        static string GetField(CharacterCard card, int i) => i switch
        {
            0 => card.Who,
            1 => card.Where,
            2 => card.Wants,
            3 => card.HowToPlay,
            _ => "",
        };

        static void SetField(CharacterCard card, int i, string value)
        {
            switch (i)
            {
                case 0: card.Who = value; break;
                case 1: card.Where = value; break;
                case 2: card.Wants = value; break;
                case 3: card.HowToPlay = value; break;
            }
        }

        void OnValueChanged(int i, string value)
        {
            if (page == null || page.Character == null) return;

            if (!historyPushed[i])
            {
                historyPushed[i] = true;
                // Читаем карточку СЕЙЧАС, ДО записи ниже — PushHistory сам заглянет в page.Character в
                // этот же вызов, и там всё ещё значение ДО этой правки. Если бы запись случилась
                // раньше, снимок унёс бы уже новое значение, и Ctrl+Z на этом поле стал бы пустышкой —
                // ровно то предупреждение, которое DocumentPageView.PushHistoryBeforeFirstEdit оставляет
                // задаче 6 в своём комментарии.
                //
                // LastFocusedBlockId называет СТРОКУ, а не это поле — у поля шапки нет своего id в модели
                // отмены, так что снимок занимает фокус у той строки, где ДМ был раньше; отмена может
                // увести каретку в эту постороннюю строку вместо самого поля. Известное ограничение,
                // поведение не меняю.
                host.PushHistory(host.LastFocusedBlockId, host.LastFocusedCaret);
            }

            SetField(page.Character, i, value);
            host.MarkDocumentMutated();
            ApplyHeightNow();
        }

        /// <summary>One undo step per VISIT to a field, the same rule DocBlockView.OnFieldChanged applies
        /// to a row: reset here, on end-edit, rather than on focus-gained, so the very first character of
        /// the NEXT visit is what triggers the next push — merely clicking into an empty-looking field
        /// and clicking back out is not a step.</summary>
        void OnEndEdit(int i) => historyPushed[i] = false;

        // ── portrait ──────────────────────────────────────────────────────────

        void PickPortrait()
        {
            var bytes = ImagePicker.OpenFileDialog();
            if (bytes == null || bytes.Length == 0) return;   // ДМ отменил диалог
            ApplyNewPortrait(bytes);
        }

        /// <summary>Re-measures every frame — the same reason DocBlockView.LateUpdate calls its own
        /// ApplyHeightNow every frame rather than relying only on the edit/rebuild call sites.
        /// DocumentPageView.ApplyColumnMeasure runs from ITS OWN LateUpdate and can rewrap every field by
        /// narrowing or widening `self` on a pane or window resize WITH NO Rebuild in between — and
        /// SettleHeights, the two-pass call site that measures against a real width, is reached from
        /// exactly one place, Rebuild(). Without this the header's rect would stay stale after such a
        /// resize until the next rebuild or keystroke, and the value fields' own RectMask2D would clip
        /// the overflow meanwhile. The same gap covers the very first Rebuild from Initialize, where
        /// SettleHeights early-returns at content.rect.width <= 1f — DocBlockView's own comment on that
        /// early return says its rows are "sized a frame later" by their own LateUpdate; this is the
        /// header's equivalent rescue. The 0.5px epsilon inside ApplyHeightNow already makes this a
        /// no-op on every frame nothing actually changed, so it does not cost a layout pass per frame.</summary>
        void LateUpdate() => ApplyHeightNow();

        /// <summary>Ctrl+V while the pointer is over the portrait. Polled rather than event-driven, the
        /// same technique LinkDropTarget.Update uses to track a drag with no hover notification of its
        /// own — Mouse/Keyboard.current do not care which UI element has focus, so this has to ask
        /// geometrically whether the pointer is over the portrait right now.</summary>
        void Update()
        {
            if (!gameObject.activeInHierarchy || portraitRect == null || page == null) return;
            if (host != null && host.KeyboardSuspended) return;   // Ctrl+K/Ctrl+F own the keys right now

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;
            if (!(keyboard.ctrlKey.isPressed || keyboard.rightCtrlKey.isPressed)) return;
            if (!keyboard.vKey.wasPressedThisFrame) return;

            Vector2 screenPos = mouse.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(portraitRect, screenPos, ClipCamera())) return;

            var bytes = ClipboardImage.TryGetImageBytes();
            if (bytes == null) return;   // буфер без изображения — тихий отказ, как у канваса (CanvasInteractionController.HandleClipboardPaste)
            ApplyNewPortrait(bytes);
        }

        /// <summary>Identical to LinkDropTarget.MarkerCamera: null for a screen-space-overlay canvas
        /// (RectTransformUtility's own convention for "no camera"), the canvas' own worldCamera
        /// otherwise.</summary>
        Camera ClipCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary>Replaces the portrait whole — never written into, per CharacterCard.Portrait's own
        /// contract. One undo step per replacement, pushed unconditionally rather than gated by a
        /// per-visit flag: picking or pasting a file is a single discrete action, not a run of
        /// keystrokes to coalesce — the same reasoning DocumentPageView.InsertImageAfterFocused already
        /// applies to a picture dropped into the prose.</summary>
        void ApplyNewPortrait(byte[] raw)
        {
            if (host == null || page == null || page.Character == null) return;

            byte[] fitted = ShrinkPortrait(raw);
            // Тот же занятый фокус чужой строки, что и в OnValueChanged — см. комментарий там.
            host.PushHistory(host.LastFocusedBlockId, host.LastFocusedCaret);
            page.Character.Portrait = fitted;
            host.MarkDocumentMutated();
            RefreshPortraitTexture(page.Character);
        }

        /// <summary>Downscales through PortraitOps.Fit (the pure size decision) and re-encodes with
        /// Unity's own texture pipeline — RenderTexture + Graphics.Blit + ReadPixels — the exact sequence
        /// Assets/WorldGen/Rendering/ImageImport.cs (ImageImport.LoadAndShrink) already uses to shrink a
        /// room preview image. That file recomputes its own scale from a maxSide argument; this one asks
        /// PortraitOps.Fit for the scale instead, so the two never disagree about what "too big" means for
        /// a portrait specifically (MaxSide=512, not ImageImport's caller-supplied limit).</summary>
        static byte[] ShrinkPortrait(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return raw;

            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(raw)) { Destroy(tex); return raw; }

            if (!PortraitOps.Fit(tex.width, tex.height, out int ow, out int oh))
            {
                Destroy(tex);
                return raw;   // уже укладывается в PortraitOps.MaxSide — байты не трогаем
            }

            var rt = RenderTexture.GetTemporary(ow, oh);
            Graphics.Blit(tex, rt);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, ow, oh), 0, 0);
            small.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            var png = small.EncodeToPNG();
            Destroy(tex);
            Destroy(small);
            return png;
        }

        /// <summary>Loads the card's current portrait bytes into the RawImage, or shows the silhouette
        /// placeholder when there are none. Skips the reload when the byte[] REFERENCE is unchanged —
        /// Show() runs on every DocumentPageView.Rebuild, which is far more often than the portrait
        /// actually changes, and CharacterCard.Portrait's own contract ("replaced whole, never written
        /// into") is exactly what makes reference equality a safe, cheap test for "nothing to do here".
        /// Without this a long session would decode the same PNG into a new Texture2D on every keystroke
        /// in any row on the page and leak the old one — Texture2D is not garbage-collected memory.</summary>
        void RefreshPortraitTexture(CharacterCard card)
        {
            byte[] bytes = card != null ? card.Portrait : null;
            if (ReferenceEquals(bytes, lastPortraitBytes)) return;
            lastPortraitBytes = bytes;

            if (ownedTexture != null) { Destroy(ownedTexture); ownedTexture = null; }

            if (bytes != null && bytes.Length > 0)
            {
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(bytes))
                {
                    ownedTexture = texture;
                    picture.texture = texture;
                    // Слот 96×96 — квадрат, а фото ДМ почти наверняка нет. RawImage без uvRect растянул
                    // бы прямоугольное фото на квадратную рамку и исказил пропорции; AspectRatioFitter
                    // сюда не годится — он спорил бы с фиксированным размером слота (правило брифа: слот
                    // остаётся ровно 96×96). Вместо этого вырезаем центральный квадрат самого изображения
                    // через uvRect — та же идея, что кадрирование по центру у любого превью-аватара.
                    picture.uvRect = CenterCropUvRect(texture.width, texture.height);
                    picture.gameObject.SetActive(true);
                    if (silhouetteGO != null) silhouetteGO.SetActive(false);
                    return;
                }
                Destroy(texture);
            }

            picture.texture = null;
            picture.gameObject.SetActive(false);
            if (silhouetteGO != null) silhouetteGO.SetActive(true);
        }

        /// <summary>The UV rect that crops a `width`×`height` texture down to its centred square — a
        /// wide photo loses its left/right margins, a tall one its top/bottom, a square is returned
        /// whole. `RawImage.uvRect` is the standard way to sample a sub-rect of a texture without
        /// touching the mesh or the fixed 96×96 slot it fills.</summary>
        static Rect CenterCropUvRect(int width, int height)
        {
            if (width <= 0 || height <= 0) return new Rect(0f, 0f, 1f, 1f);
            if (width > height)
            {
                float frac = (float)height / width;
                return new Rect((1f - frac) * 0.5f, 0f, frac, 1f);
            }
            if (height > width)
            {
                float frac = (float)width / height;
                return new Rect(0f, (1f - frac) * 0.5f, 1f, frac);
            }
            return new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>A generic head-and-shoulders glyph, drawn once and cached — the same runtime-texture
        /// technique NotesIconFactory uses for its toolbar glyphs (distance-to-centre fills, no external
        /// image files), reimplemented locally rather than added to that factory: NotesIconFactory keys
        /// its cache on NotesTool, a canvas-toolbar enum this page has no business extending, and this
        /// task's own file-change list does not include that file.
        ///
        /// PUBLIC because it has a second consumer: the navigator's character rows (a later task) show
        /// the same placeholder for a character with no portrait, and must draw the identical glyph
        /// rather than invent their own. Kept here rather than moved into NotesIconFactory — the reason
        /// above still holds, and this also keeps this arc off a file a parallel branch is editing.</summary>
        static Sprite silhouetteSprite;
        public static Sprite SilhouetteSprite()
        {
            if (silhouetteSprite != null) return silhouetteSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "CharacterPortraitSilhouette";

            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, clear);

            // Голова — окружность чуть выше центра.
            var head = new Vector2(size * 0.5f, size * 0.62f);
            float headRadius = size * 0.16f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if ((p - head).sqrMagnitude <= headRadius * headRadius)
                        tex.SetPixel(x, y, Color.white);
                }

            // Плечи — верхняя дуга овала, чей центр вынесен ЗА нижний край холста, так что в кадр
            // попадает только его макушка, и она читается как плечи, срезанные рамкой портрета.
            var shoulders = new Vector2(size * 0.5f, -size * 0.05f);
            float rx = size * 0.40f;
            float ry = size * 0.34f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - shoulders.x) / rx;
                    float dy = (y + 0.5f - shoulders.y) / ry;
                    if (dx * dx + dy * dy <= 1f)
                        tex.SetPixel(x, y, Color.white);
                }

            tex.Apply();
            silhouetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return silhouetteSprite;
        }

        void OnDestroy()
        {
            // Отписываться от конкретных делегатов здесь незачем — RemoveAllListeners безопасен именно
            // потому, что этот объект владеет своими полями безраздельно: ничто снаружи на них не
            // подписывается (в отличие от DocumentPageView.documentController, откуда отписка — парная
            // операция с чужой подпиской).
            for (int i = 0; i < FieldCount; i++)
            {
                var f = valueFields[i];
                if (f == null) continue;
                f.onValueChanged.RemoveAllListeners();
                f.onEndEdit.RemoveAllListeners();
            }
            if (ownedTexture != null) Destroy(ownedTexture);
        }
    }
}
