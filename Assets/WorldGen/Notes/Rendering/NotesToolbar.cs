using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of borderless icon buttons (Select/Note/Drawing/Image/Zoom) floating over the
    /// notes canvas. Clicking a button calls CanvasInteractionController.SetTool and shows a
    /// circular backdrop behind its icon; hovering shows a dimmer backdrop plus a floating
    /// Russian-label tooltip near the cursor.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public const float ButtonSize = 36f;
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f, 0.65f);
        public Color hoverColor = new Color(1f, 1f, 1f, 0.15f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;
        Canvas rootCanvas;
        RectTransform tooltipRect;
        Text tooltipText;
        CanvasGroup tooltipGroup;
        int hoveredIndex = -1;
        NotesTool activeTool = NotesTool.Select;

        static Sprite cachedBackdropSprite;

        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
            (NotesTool.Zoom, "Лупа"),
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
            var fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Floats over the canvas viewport instead of reserving its own row in a stacked
            // layout — this GameObject has no LayoutGroup parent controlling it (RightColumn
            // no longer stacks Toolbar+Viewport, see NotesRootBuilder), so its anchor
            // (top-left corner) is set directly here; its size comes from ContentSizeFitter
            // reading its own HorizontalLayoutGroup's computed preferred size above.
            var rowRect = (RectTransform)rowGO.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = Vector2.zero;

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
                img.sprite = GetBackdropSprite();
                img.color = Color.clear;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                // This class manages the backdrop color itself (active/hover/neither) — the
                // default ColorTint transition would otherwise fight that by re-tinting
                // targetGraphic.color on every pointer enter/exit/click.
                btn.transition = Selectable.Transition.None;
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

            // Only touch the tooltip/backdrops when the hovered button actually changes, not
            // every frame — no need to keep re-setting the same state 60 times a second.
            if (newHoveredIndex == hoveredIndex) return;
            hoveredIndex = newHoveredIndex;
            RefreshButtonVisuals();

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
            activeTool = tool;
            RefreshButtonVisuals();
        }

        void RefreshButtonVisuals()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].GetComponent<Image>();
                if (ToolDefs[i].tool == activeTool)
                    img.color = activeColor;
                else if (i == hoveredIndex)
                    img.color = hoverColor;
                else
                    img.color = Color.clear;
            }
        }

        static Sprite GetBackdropSprite()
        {
            if (cachedBackdropSprite != null) return cachedBackdropSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "NotesToolbarBackdrop";

            var center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = (p - center).sqrMagnitude <= radius * radius;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            tex.Apply();

            cachedBackdropSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedBackdropSprite;
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

        [ContextMenu("Self-Test: Notes Toolbar — Active/Hover Backdrop")]
        public void SelfTestActiveHoverBackdrop()
        {
            if (buttons == null)
            {
                Debug.Log("Self-Test Notes Toolbar — Active/Hover Backdrop: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            SetActive(NotesTool.Select);
            if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
            { ok = false; reason = "expected Select button to show activeColor after SetActive(Select)"; }
            if (ok && !ColorsApproximatelyEqual(buttons[1].GetComponent<Image>().color, Color.clear))
            { ok = false; reason = "expected non-active button to be Color.clear"; }

            if (ok)
            {
                hoveredIndex = 1;
                RefreshButtonVisuals();
                if (!ColorsApproximatelyEqual(buttons[1].GetComponent<Image>().color, hoverColor))
                { ok = false; reason = "expected hovered non-active button to show hoverColor"; }
                else if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
                { ok = false; reason = "expected active button to stay activeColor while a different button is hovered"; }
            }

            if (ok)
            {
                hoveredIndex = 0;
                RefreshButtonVisuals();
                if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
                { ok = false; reason = "expected active button to stay activeColor (not hoverColor) when hovered"; }
            }

            hoveredIndex = -1;
            RefreshButtonVisuals();

            Debug.Log(ok
                ? "Self-Test Notes Toolbar — Active/Hover Backdrop: PASS"
                : $"Self-Test Notes Toolbar — Active/Hover Backdrop: FAIL ({reason})");
        }

        static bool ColorsApproximatelyEqual(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) &&
            Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
    }
}
