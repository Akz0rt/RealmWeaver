using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of fixed-size icon buttons (Select/Note/Link/Drawing/Image) above the notes
    /// canvas. Clicking a button calls CanvasInteractionController.SetTool and highlights
    /// itself; hovering shows a floating Russian-label tooltip near the cursor.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public const float ButtonSize = 36f;
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f);
        public Color inactiveColor = new Color(0.3f, 0.3f, 0.3f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;
        Canvas rootCanvas;
        RectTransform tooltipRect;
        Text tooltipText;
        CanvasGroup tooltipGroup;
        int hoveredIndex = -1;

        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Link, "Связь"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
        };

        public void Initialize(CanvasInteractionController interactionController, Transform parent)
        {
            controller = interactionController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rootCanvas = parent.GetComponentInParent<Canvas>();

            var rowGO = new GameObject("NotesToolbar");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.padding = new RectOffset(6, 6, 4, 4);
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = ButtonSize + 8f;

            BuildTooltip(rootCanvas.transform);

            buttons = new Button[ToolDefs.Length];
            for (int i = 0; i < ToolDefs.Length; i++)
            {
                int index = i;
                var (tool, label) = ToolDefs[i];

                var btnGO = new GameObject($"Tool_{tool}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var btnRect = btnGO.AddComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
                var le = btnGO.AddComponent<LayoutElement>();
                le.preferredWidth = ButtonSize;
                le.preferredHeight = ButtonSize;

                var img = btnGO.AddComponent<Image>();
                img.color = inactiveColor;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActive(tool));
                buttons[index] = btn;

                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(btnGO.transform, false);
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = NotesIconFactory.GetIcon(tool);
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                var iconRect = iconImg.rectTransform;
                iconRect.anchorMin = new Vector2(0.15f, 0.15f);
                iconRect.anchorMax = new Vector2(0.85f, 0.85f);
                iconRect.sizeDelta = Vector2.zero;

            }

            SetActive(NotesTool.Select);
        }

        void Update()
        {
            if (Mouse.current == null) return;
            var screenPos = Mouse.current.position.ReadValue();

            int newHoveredIndex = -1;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)buttons[i].transform, screenPos, null))
                {
                    newHoveredIndex = i;
                    break;
                }
            }

            // Only touch the tooltip when the hovered button actually changes, not every
            // frame — no need to keep re-setting the same text/position 60 times a second.
            if (newHoveredIndex == hoveredIndex) return;
            hoveredIndex = newHoveredIndex;

            if (hoveredIndex >= 0)
                ShowTooltip(ToolDefs[hoveredIndex].label, screenPos);
            else
                HideTooltip();
        }

        void BuildTooltip(Transform canvasRoot)
        {
            var tooltipGO = new GameObject("Tooltip");
            tooltipGO.transform.SetParent(canvasRoot, false);
            var img = tooltipGO.AddComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
            img.raycastTarget = false;
            tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);
            tooltipRect.sizeDelta = new Vector2(90f, 20f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(tooltipGO.transform, false);
            tooltipText = textGO.AddComponent<Text>();
            tooltipText.font = builtinFont;
            tooltipText.fontSize = 11;
            tooltipText.color = Color.white;
            tooltipText.alignment = TextAnchor.MiddleCenter;
            tooltipText.raycastTarget = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            // Stays active for the tooltip's entire lifetime — visibility is controlled via
            // CanvasGroup.alpha instead of GameObject.SetActive so it keeps being redrawn by
            // the Canvas every frame (see NotesRootBuilder's notesAreaBg comment for why that
            // matters here) rather than needing an OnEnable-time relayout each time it appears.
            tooltipGroup = tooltipGO.AddComponent<CanvasGroup>();
            tooltipGroup.alpha = 0f;
            tooltipGroup.blocksRaycasts = false;
            tooltipGroup.interactable = false;
        }

        void ShowTooltip(string label, Vector2 screenPos)
        {
            tooltipText.text = label;
            tooltipGroup.alpha = 1f;
            var canvasRect = (RectTransform)tooltipRect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local);
            tooltipRect.anchoredPosition = local + new Vector2(12f, -12f);
        }

        void HideTooltip()
        {
            tooltipGroup.alpha = 0f;
        }

        void SetActive(NotesTool tool)
        {
            controller.SetTool(tool);
            for (int i = 0; i < ToolDefs.Length; i++)
                buttons[i].GetComponent<Image>().color = ToolDefs[i].tool == tool ? activeColor : inactiveColor;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Toolbar — Icon Caching")]
        public void SelfTestIconCaching()
        {
            bool ok = true;
            foreach (NotesTool tool in System.Enum.GetValues(typeof(NotesTool)))
            {
                var a = NotesIconFactory.GetIcon(tool);
                var b = NotesIconFactory.GetIcon(tool);
                if (a == null || !ReferenceEquals(a, b)) { ok = false; break; }
            }
            Debug.Log(ok
                ? "Self-Test Notes Toolbar — Icon Caching: PASS"
                : "Self-Test Notes Toolbar — Icon Caching: FAIL (icon missing or not cached for some tool)");
        }
    }
}
