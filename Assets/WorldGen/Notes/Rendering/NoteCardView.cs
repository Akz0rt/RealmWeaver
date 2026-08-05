using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable card showing a NoteCardData's title + body. Drag moves it within its
    /// parent canvas container; a plain click (no movement) fires OnClicked instead.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class NoteCardView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        NoteCardData data;
        RectTransform rect;
        TMP_Text titleText;
        TMP_InputField bodyField;

        /// <summary>The body when it is NOT editable. Exactly one of this and bodyField is ever non-null —
        /// which is what Refresh reads to know which half was built.</summary>
        TMP_Text bodyLabel;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        /// <summary>When set, self-move-drag is only allowed while its ActiveTool is Select.</summary>
        public CanvasInteractionController interactionController;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        /// <summary>The DM has begun moving this object — raised on the FIRST movement of a press, not on the
        /// press itself, so a plain click does not spend an undo step.
        ///
        /// IT EXISTS ONLY SO THE UNDO SNAPSHOT IS TAKEN IN TIME. OnDragEnded fires after this view has already
        /// written the new position into the data, and a snapshot taken then would record the very state it
        /// is supposed to undo — the board would jump back to where it already is. See
        /// CanvasInteractionController.HandleObjectDragStarted.</summary>
        public event System.Action<string> OnDragStarted;

        /// <summary>Builds the card. `editable` is FALSE for a canvas shown inline in a page.
        ///
        /// NOT A COSMETIC SWITCH. A TMP_InputField is an IScrollHandler and swallows the wheel over itself
        /// (TMP_InputField.cs:2414-2423) — the exact defect Р2 spent a checkpoint round on for page rows. In
        /// the flow of a page the wheel ALWAYS belongs to the page, so the editing half simply is not built.
        /// The DM edits a card in the expanded view, where the page is not underneath it.
        ///
        /// TMP AND LITERATA, unlike every other panel in the app: a card on a board is body text of a page,
        /// which is exactly the boundary the arc's "panels do not migrate" rule draws. The toolbar, the
        /// inspectors and the confirm dialogs stay on LegacyRuntime.ttf.</summary>
        public void Initialize(NoteCardData cardData, RectTransform canvasContainer, bool editable)
        {
            data = cardData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var bg = gameObject.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(transform, false);
            titleText = titleGO.AddComponent<TextMeshProUGUI>();
            // Guarded rather than assigned outright, the way NotesTypography.ApplyBody guards: a missing
            // font asset is a real state (it complains loudly and returns null), and writing null into
            // TMP_Text.font turns a legible fallback into a NullReferenceException at first layout.
            if (NotesTypography.Bold != null) titleText.font = NotesTypography.Bold;
            titleText.fontSize = 14f;
            titleText.alignment = TextAlignmentOptions.TopLeft;
            titleText.raycastTarget = false;
            ThemeService.Tag(titleText, ThemeRole.Txt);
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -4f);
            titleRect.sizeDelta = new Vector2(-8f, 22f);

            var bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(transform, false);
            var bodyRect = bodyGO.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(4f, 4f);
            bodyRect.offsetMax = new Vector2(-4f, -26f);

            var bodyTextGO = new GameObject("Text", typeof(RectTransform));
            bodyTextGO.transform.SetParent(bodyGO.transform, false);
            var bodyText = bodyTextGO.AddComponent<TextMeshProUGUI>();
            if (NotesTypography.Body != null) bodyText.font = NotesTypography.Body;
            bodyText.fontSize = 12f;
            ThemeService.Tag(bodyText, ThemeRole.Txt);
            var bodyTextRect = bodyTextGO.GetComponent<RectTransform>();
            bodyTextRect.anchorMin = Vector2.zero;
            bodyTextRect.anchorMax = Vector2.one;
            bodyTextRect.sizeDelta = Vector2.zero;

            if (editable)
            {
                var bodyBg = bodyGO.AddComponent<Image>();
                bodyBg.color = new Color(1f, 1f, 1f, 0.01f);
                bodyField = bodyGO.AddComponent<TMP_InputField>();
                bodyField.targetGraphic = bodyBg;
                // ОБЯЗАТЕЛЬНО, ХОТЯ ВЫГЛЯДИТ НЕОБЯЗАТЕЛЬНЫМ. TMP_InputField разыменовывает m_TextViewport
                // без единой проверки, как только курсор выделения уезжает за границу поля
                // (MouseDragOutsideRect, TMP_InputField.cs:1936) — а туда попадает всякий, кто тянет мышью
                // по тексту карточки и промахивается мимо её края. Без этой строки — NullReferenceException
                // каждый кадр протаскивания. DocBlockView задаёт viewport с самого начала (строка 540),
                // карточка доски — нет; здесь это собственный прямоугольник поля, отдельного viewport-узла
                // у неё нет.
                bodyField.textViewport = bodyRect;
                bodyField.textComponent = bodyText;
                bodyField.lineType = TMP_InputField.LineType.MultiLineNewline;
                // Kept from the legacy card, where it was supportRichText=false: a body the DM typed is
                // TEXT, and a stray '<' in it must stay a '<' rather than be eaten as markup. Set on the
                // FIELD, not the label — TMP_InputField pushes its own richText onto its text component.
                bodyField.richText = false;
                // ВИДНО, ГДЕ ПЕЧАТАЕШЬ И ЧТО ВЫДЕЛЕНО. По умолчанию каретка TMP — это одна серая линия в
                // цвет текста шириной 1 px, а доска рисуется в масштабе (зум меньше единицы делает её
                // тоньше пикселя и она пропадает). Акцентный цвет и двойная ширина держатся на любом зуме.
                //
                // Цвета взяты у темы разово, а не тегом: caretColor и selectionColor — обычные поля
                // TMP_InputField, а не Graphic, и ThemeService.Tag их не перекрашивает. Переключение
                // темы при открытой карточке оставит их прежними до следующей перестройки доски.
                bodyField.customCaretColor = true;
                bodyField.caretColor = ThemeService.Get(ThemeRole.Accent);
                bodyField.caretWidth = 2;
                var sel = ThemeService.Get(ThemeRole.Accent);
                bodyField.selectionColor = new Color(sel.r, sel.g, sel.b, 0.35f);
                // Off, for the same reason DocBlockView turns it off: on a writing surface it loses a
                // paragraph to one keystroke with no undo behind it.
                bodyField.restoreOriginalTextOnEscape = false;
                bodyField.onEndEdit.AddListener(v => data.Body = v);
                bodyField.text = data.Body;
            }
            else
            {
                // No field, so no IScrollHandler, so the wheel over an inline board belongs to the page.
                bodyLabel = bodyText;
                bodyText.richText = false;
                bodyText.raycastTarget = false;
                bodyText.text = data.Body;
            }

            titleText.text = data.Title;
            Refresh();
        }

        /// <summary>Ставит каретку в текст карточки — вызывается сразу после того, как карточку вставили,
        /// чтобы ДМ печатал без лишнего клика.
        ///
        /// Молча ничего не делает у карточки, построенной с editable: false — у неё поля ввода не
        /// существует вовсе (см. Initialize: в потоке страницы TMP_InputField съедал бы колесо). Это
        /// обычное состояние, а не ошибка.</summary>
        public void FocusBody()
        {
            if (bodyField == null) return;
            bodyField.Select();
            bodyField.ActivateInputField();
        }

        public void Refresh()
        {
            if (data == null) return;
            titleText.text = data.Title;
            if (bodyField != null) bodyField.text = data.Body;
            else if (bodyLabel != null) bodyLabel.text = data.Body;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        bool CanSelfMove => interactionController == null || interactionController.ActiveTool == NotesTool.Select;

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanSelfMove) return;
            if (!dragging) OnDragStarted?.Invoke(data.Id);
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
