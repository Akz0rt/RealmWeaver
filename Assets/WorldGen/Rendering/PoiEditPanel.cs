using System.Collections.Generic;
using SFB;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Standalone screen-space panel for editing the currently selected POI: type, name,
    /// description, cell binding, custom icon path, and per-POI icon/label scale.
    /// Anchored top-right, automatically positioned 20px below MapLegendUI's actual bottom
    /// edge every frame (legend height changes as its rows change). Height auto-fits content
    /// but is clamped to the remaining screen space below that position, scrolling if needed.
    /// Shows itself when a POI is selected via PoiManager.OnSelectionChanged, hides on deselect.
    /// </summary>
    public class PoiEditPanel : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;
        [Tooltip("Легенда карты - панель редактирования точки автоматически встаёт на 20px ниже её нижней грани.")]
        public MapLegendUI legendUI;
        [Tooltip("Notes root — resolves/creates the POI's linked page group when \"Открыть страницы\" is clicked.")]
        public NotesRootBuilder notesRoot;

        [Header("Внешний вид")]
        [Tooltip("Горизонтальный отступ от правого края экрана.")]
        public float rightMargin = 20f;
        [Tooltip("Отступ снизу от нижней грани легенды.")]
        public float gapBelowLegend = 20f;
        [Tooltip("Отступ от нижнего края экрана, ниже которого панель не опускается.")]
        public float bottomScreenMargin = 20f;
        public float panelWidth = 240f;

        GameObject panelGO;
        RectTransform panelRect;
        RectTransform contentRect;
        LayoutElement scrollAreaLE;
        readonly Dictionary<PoiType, (Image bg, Outline outline)> typeButtons =
            new Dictionary<PoiType, (Image, Outline)>();
        InputField poiNameField;
        InputField poiDescField;
        InputField poiSpritePathField;
        Text poiCellLabel;
        Slider iconScaleSlider;
        Slider labelScaleSlider;
        Font builtinFont;

        static readonly ExtensionFilter[] IconFilters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
            panelGO.SetActive(false);
            NotesLayoutController.OnSplitFractionChanged += UpdateSplitAnchor;
        }

        void OnDestroy()
        {
            NotesLayoutController.OnSplitFractionChanged -= UpdateSplitAnchor;
        }

        void UpdateSplitAnchor(float fraction)
        {
            panelRect.anchorMin = new Vector2(fraction, 1f);
            panelRect.anchorMax = new Vector2(fraction, 1f);
        }

        void OnEnable()
        {
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged += HandleSelectionChanged;
                poiManager.OnPoisChanged += RefreshFromSelection;
            }
        }

        void OnDisable()
        {
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged -= HandleSelectionChanged;
                poiManager.OnPoisChanged -= RefreshFromSelection;
            }
        }

        void LateUpdate()
        {
            // Content height changes with the selected POI's description length, so re-clamp the
            // scroll area to the remaining screen space every frame (cheap: a couple of RectTransform reads).
            RepositionPanel();
        }

        void RepositionPanel()
        {
            if (panelRect == null) return;

            // Фиксированная позиция: правый край у линии раздела карта/заметки (anchor = SplitFraction,
            // pivot справа), верх — сразу под верхней панелью (40 меню + 46 тулбар + отступ). Раньше
            // панель докалась под легенду, но в Screen A легенда уехала в низ-лево, так что отвязываем.
            float topY = -(40f + MapToolbarUI.BarHeightPixels + 20f);
            panelRect.anchoredPosition = new Vector2(-rightMargin, topY);

            float availableHeight = Screen.height + topY - bottomScreenMargin;
            float contentHeight = LayoutUtility.GetPreferredHeight(contentRect);
            float desiredHeight = Mathf.Min(contentHeight, Mathf.Max(availableHeight, 0f));

            scrollAreaLE.preferredHeight = desiredHeight;
            panelRect.sizeDelta = new Vector2(panelWidth, desiredHeight + 16f); // +padding
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        void HandleSelectionChanged(PoiData selected)
        {
            if (selected == null)
            {
                panelGO.SetActive(false);
                return;
            }

            panelGO.SetActive(true);
            ApplyTypeHighlight(selected.Type);
            poiNameField.text = selected.Name;
            poiDescField.text = selected.Description;
            poiCellLabel.text = $"Клетка: #{selected.OwnerCellId}";
            poiSpritePathField.text = string.IsNullOrEmpty(selected.CustomSpritePath)
                ? ""
                : System.IO.Path.GetFileName(selected.CustomSpritePath);
            iconScaleSlider.SetValueWithoutNotify(selected.IconScale);
            labelScaleSlider.SetValueWithoutNotify(selected.LabelScale);
        }

        void RefreshFromSelection()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel != null) poiCellLabel.text = $"Клетка: #{sel.OwnerCellId}";
        }

        // ── Type selector (4 icon buttons, replacing the old dropdown) ────────────

        void BuildTypeSelector(Transform t)
        {
            var rowGO = new GameObject("TypeSelector");
            rowGO.transform.SetParent(t, false);
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 44f;

            AddTypeButton(rowGO.transform, PoiType.City, "Город");
            AddTypeButton(rowGO.transform, PoiType.Ruin, "Руины");
            AddTypeButton(rowGO.transform, PoiType.Dungeon, "Подзем.");
            AddTypeButton(rowGO.transform, PoiType.Fortress, "Креп.");
        }

        void AddTypeButton(Transform parent, PoiType type, string label)
        {
            var go = new GameObject($"Type_{type}");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = false;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiType(sel.Id, type);
                ApplyTypeHighlight(type);
            });

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var icon = iconGO.AddComponent<Image>();
            icon.sprite = PoiPlaceholderFactory.GetPlaceholder(type);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -4f);
            iconRect.sizeDelta = new Vector2(22f, 22f);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var lbl = labelGO.AddComponent<Text>();
            lbl.text = label;
            lbl.font = builtinFont;
            lbl.fontSize = 9;
            ThemeService.Tag(lbl, ThemeRole.Txt);
            lbl.alignment = TextAnchor.LowerCenter;
            lbl.raycastTarget = false;
            var lblRect = labelGO.GetComponent<RectTransform>();
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(1f, 0f);
            lblRect.pivot = new Vector2(0.5f, 0f);
            lblRect.anchoredPosition = new Vector2(0f, 3f);
            lblRect.sizeDelta = new Vector2(0f, 12f);

            typeButtons[type] = (bg, outline);
        }

        void ApplyTypeHighlight(PoiType type)
        {
            foreach (var kvp in typeButtons)
            {
                bool on = kvp.Key == type;
                ThemeService.Tag(kvp.Value.bg, on ? ThemeRole.AccentSoft : ThemeRole.Elev);
                kvp.Value.outline.effectColor = ThemeService.Get(ThemeRole.Accent);
                kvp.Value.outline.enabled = on;
            }
        }

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiEditCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            panelGO = new GameObject("PoiEditPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.75f);
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.anchorMax = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(panelWidth, 0f);
            UiShadow.Add(panelRect);

            // Scroll area: content auto-sizes via ContentSizeFitter, but RepositionPanel() clamps
            // scrollAreaLE.preferredHeight to whatever screen space remains below the top chrome,
            // so tall content scrolls instead of pushing the panel off-screen.
            var scrollAreaGO = new GameObject("ScrollArea");
            scrollAreaGO.transform.SetParent(panelGO.transform, false);
            scrollAreaLE = scrollAreaGO.AddComponent<LayoutElement>();
            var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
            panelVLG.childControlWidth = true;
            panelVLG.childControlHeight = true;
            panelVLG.childForceExpandWidth = true;
            panelVLG.padding = new RectOffset(8, 8, 8, 8);

            var scrollRect = scrollAreaGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollAreaGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.spacing = 3f;
            contentVLG.childControlWidth = true;
            contentVLG.childForceExpandWidth = true;
            contentVLG.childControlHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            var t = contentGO.transform;

            AddLabel(t, "─ Точка интереса ─", bold: true, role: ThemeRole.Mut);

            AddLabel(t, "Тип:");
            BuildTypeSelector(t);

            AddLabel(t, "Название:");
            poiNameField = BuildInputField(t, multiline: false);
            poiNameField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiName(sel.Id, v);
            });

            AddLabel(t, "Описание:");
            poiDescField = BuildInputField(t, multiline: true);
            poiDescField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiDescription(sel.Id, v);
            });

            AddLabel(t, "Привязка к клетке:");
            var cellRowGO = new GameObject("CellRow");
            cellRowGO.transform.SetParent(t, false);
            var cellHLG = cellRowGO.AddComponent<HorizontalLayoutGroup>();
            cellHLG.spacing = 4f;
            cellHLG.childControlWidth = false;
            cellHLG.childControlHeight = false;
            cellRowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var cellLblGO = new GameObject("CellLabel");
            cellLblGO.transform.SetParent(cellRowGO.transform, false);
            poiCellLabel = cellLblGO.AddComponent<Text>();
            poiCellLabel.text = "Клетка: —";
            poiCellLabel.font = builtinFont;
            poiCellLabel.fontSize = 12;
            ThemeService.Tag(poiCellLabel, ThemeRole.Txt);
            poiCellLabel.alignment = TextAnchor.MiddleLeft;
            cellLblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 20f);

            var snapBtnGO = new GameObject("SnapCellBtn");
            snapBtnGO.transform.SetParent(cellRowGO.transform, false);
            var snapImg = snapBtnGO.AddComponent<Image>();
            ThemeService.Tag(snapImg, ThemeRole.Elev, 0.9f);
            var snapBtn = snapBtnGO.AddComponent<Button>();
            snapBtn.targetGraphic = snapImg;
            snapBtnGO.GetComponent<RectTransform>().sizeDelta = new Vector2(76f, 20f);
            snapBtn.onClick.AddListener(() =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel == null) return;
                poiManager.SnapOwnerCellToPosition(sel.Id);
                poiCellLabel.text = $"Клетка: #{sel.OwnerCellId}";
            });
            var snapTextGO = new GameObject("Text");
            snapTextGO.transform.SetParent(snapBtnGO.transform, false);
            var snapText = snapTextGO.AddComponent<Text>();
            snapText.text = "По позиции";
            snapText.font = builtinFont;
            snapText.fontSize = 10;
            ThemeService.Tag(snapText, ThemeRole.Txt);
            snapText.alignment = TextAnchor.MiddleCenter;
            var snapTr = snapTextGO.GetComponent<RectTransform>();
            snapTr.anchorMin = Vector2.zero;
            snapTr.anchorMax = Vector2.one;
            snapTr.sizeDelta = Vector2.zero;

            AddLabel(t, "Иконка:");
            var spriteRow = new GameObject("SpriteRow");
            spriteRow.transform.SetParent(t, false);
            var srHLG = spriteRow.AddComponent<HorizontalLayoutGroup>();
            srHLG.spacing = 4f;
            srHLG.childControlWidth = true;
            srHLG.childForceExpandWidth = true;
            spriteRow.AddComponent<LayoutElement>().preferredHeight = 20f;

            poiSpritePathField = BuildInputField(spriteRow.transform, multiline: false);
            poiSpritePathField.interactable = false;

            var pickBtnGO = new GameObject("PickIconBtn");
            pickBtnGO.transform.SetParent(spriteRow.transform, false);
            var pickImg = pickBtnGO.AddComponent<Image>();
            ThemeService.Tag(pickImg, ThemeRole.Accent, 0.9f);
            var pickBtn = pickBtnGO.AddComponent<Button>();
            pickBtn.targetGraphic = pickImg;
            pickBtn.onClick.AddListener(OnPickIconClicked);
            pickBtnGO.AddComponent<LayoutElement>().preferredWidth = 90f;
            var pickTextGO = new GameObject("Text");
            pickTextGO.transform.SetParent(pickBtnGO.transform, false);
            var pickText = pickTextGO.AddComponent<Text>();
            pickText.text = "Выбрать файл…";
            pickText.font = builtinFont;
            pickText.fontSize = 10;
            ThemeService.Tag(pickText, ThemeRole.AccentInk);
            pickText.alignment = TextAnchor.MiddleCenter;
            var pickRect = pickTextGO.GetComponent<RectTransform>();
            pickRect.anchorMin = Vector2.zero;
            pickRect.anchorMax = Vector2.one;
            pickRect.sizeDelta = Vector2.zero;

            AddLabel(t, "─── Размер на карте ───", bold: false, role: ThemeRole.Mut);
            iconScaleSlider = AddScaleSliderRow(t, "Иконка", 1f, v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiIconScale(sel.Id, v);
            });
            labelScaleSlider = AddScaleSliderRow(t, "Название", 1f, v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiLabelScale(sel.Id, v);
            });

            AddButton(t, "Удалить точку", () =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.DeletePoi(sel.Id);
            }, ThemeRole.Danger);

            AddButton(t, "Открыть страницы", OnOpenPagesClicked, ThemeRole.Accent);
        }

        void OnOpenPagesClicked()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null || notesRoot == null) return;

            var doc = notesRoot.DocumentController;
            var group = doc.FindGroupByPoiId(sel.Id);
            if (group == null)
            {
                group = doc.CreateGroup(sel.Name, sel.Id);
                doc.CreatePage(group.Id, "Страница 1");
            }

            doc.OpenPage(group.Pages[0].Id);
        }

        void OnPickIconClicked()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null) return;

            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать иконку", "", IconFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

            byte[] bytes = System.IO.File.ReadAllBytes(paths[0]);
            poiManager.UpdatePoiIconBytes(sel.Id, bytes, paths[0]);
            poiSpritePathField.text = System.IO.Path.GetFileName(paths[0]);
        }

        Slider AddScaleSliderRow(Transform parent, string label, float defaultValue, System.Action<float> onChanged)
        {
            var rowGO = new GameObject($"{label}ScaleRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(70f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 0.25f;
            slider.maxValue = 4f;
            slider.value = defaultValue;
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 20f);

            var bg = new GameObject("Bg");
            bg.transform.SetParent(sliderGO.transform, false);
            var bgImg = bg.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent, 0.9f);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.2f);
            fillRect.anchorMax = new Vector2(0f, 0.8f);
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRect = handleArea.AddComponent<RectTransform>();
            haRect.anchorMin = Vector2.zero;
            haRect.anchorMax = Vector2.one;
            haRect.sizeDelta = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImg = handle.AddComponent<Image>();
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(12f, 18f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueText = valueGO.AddComponent<Text>();
            valueText.text = defaultValue.ToString("F2");
            valueText.font = builtinFont;
            valueText.fontSize = 12;
            ThemeService.Tag(valueText, ThemeRole.Txt);
            valueText.alignment = TextAnchor.MiddleLeft;
            valueGO.GetComponent<RectTransform>().sizeDelta = new Vector2(35f, 20f);

            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = v.ToString("F2");
                onChanged(v);
            });

            return slider;
        }

        Text AddLabel(Transform parent, string text, bool bold = false, ThemeRole? role = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = bold ? 14 : 11;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            ThemeService.Tag(label, role ?? ThemeRole.Txt);
            label.alignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = bold ? 18f : 14f;
            return label;
        }

        void AddButton(Transform parent, string label, System.Action onClick, ThemeRole? role = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (role.HasValue) ThemeService.Tag(img, role.Value);
            else ThemeService.Tag(img, ThemeRole.Elev, 0.9f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 22f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.AccentInk);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        InputField BuildInputField(Transform parent, bool multiline)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.lineType = multiline
                ? InputField.LineType.MultiLineNewline
                : InputField.LineType.SingleLine;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phText = phGO.AddComponent<Text>();
            phText.font = builtinFont;
            phText.fontSize = 12;
            ThemeService.Tag(phText, ThemeRole.Mut);
            phText.fontStyle = FontStyle.Italic;
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 34f : 20f;
            le.flexibleWidth = 1f;
            return field;
        }
    }
}
