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
        Text titleText;
        InputField bodyField;
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

        static Font builtinFont;

        public void Initialize(NoteCardData cardData, RectTransform canvasContainer)
        {
            data = cardData;
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var bg = gameObject.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = builtinFont;
            titleText.fontSize = 14;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.UpperLeft;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -4f);
            titleRect.sizeDelta = new Vector2(-8f, 22f);

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(transform, false);
            var bodyBg = bodyGO.AddComponent<Image>();
            bodyBg.color = new Color(1f, 1f, 1f, 0.01f);
            bodyField = bodyGO.AddComponent<InputField>();
            bodyField.targetGraphic = bodyBg;
            bodyField.lineType = InputField.LineType.MultiLineNewline;
            var bodyRect = bodyGO.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(4f, 4f);
            bodyRect.offsetMax = new Vector2(-4f, -26f);

            var bodyTextGO = new GameObject("Text");
            bodyTextGO.transform.SetParent(bodyGO.transform, false);
            var bodyText = bodyTextGO.AddComponent<Text>();
            bodyText.font = builtinFont;
            bodyText.fontSize = 12;
            bodyText.color = Color.white;
            bodyText.supportRichText = false;
            var bodyTextRect = bodyTextGO.GetComponent<RectTransform>();
            bodyTextRect.anchorMin = Vector2.zero;
            bodyTextRect.anchorMax = Vector2.one;
            bodyTextRect.sizeDelta = Vector2.zero;
            bodyField.textComponent = bodyText;
            bodyField.onEndEdit.AddListener(v => data.Body = v);

            titleText.text = data.Title;
            bodyField.text = data.Body;

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            titleText.text = data.Title;
            if (bodyField != null) bodyField.text = data.Body;
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
