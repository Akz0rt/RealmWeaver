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
        Dropdown poiTypeDropdown;
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
            // Legend height can change (display mode/row count), so re-anchor every frame rather
            // than only on selection - cheap (a handful of RectTransform reads) and keeps the two
            // panels visually glued together without needing an explicit "legend changed" event.
            RepositionUnderLegend();
        }

        void RepositionUnderLegend()
        {
            if (panelRect == null) return;

            float legendBottomFromTop = 20f; // fallback if legendUI isn't assigned
            if (legendUI != null && legendUI.PanelRect != null)
            {
                var corners = new Vector3[4];
                legendUI.PanelRect.GetWorldCorners(corners);
                // corners[0] = bottom-left in world space; ScreenSpaceOverlay world space == screen pixels.
                legendBottomFromTop = Screen.height - corners[0].y;
            }

            float topY = -(legendBottomFromTop + gapBelowLegend);
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
            poiTypeDropdown.value = (int)selected.Type;
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

            // Scroll area: content auto-sizes via ContentSizeFitter, but RepositionUnderLegend()
            // clamps scrollAreaLE.preferredHeight to whatever screen space remains below the
            // legend, so tall content scrolls instead of pushing the panel off-screen.
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
            var typeRowGO = new GameObject("TypeDropdownRow");
            typeRowGO.transform.SetParent(t, false);
            typeRowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            poiTypeDropdown = typeRowGO.AddComponent<Dropdown>();
            var typeBg = typeRowGO.AddComponent<Image>();
            ThemeService.Tag(typeBg, ThemeRole.Panel2, 0.95f);
            poiTypeDropdown.targetGraphic = typeBg;

            var typeCaptionGO = new GameObject("Label");
            typeCaptionGO.transform.SetParent(typeRowGO.transform, false);
            var typeCaptionText = typeCaptionGO.AddComponent<Text>();
            typeCaptionText.font = builtinFont;
            typeCaptionText.fontSize = 12;
            ThemeService.Tag(typeCaptionText, ThemeRole.Txt);
            typeCaptionText.alignment = TextAnchor.MiddleLeft;
            var typeCaptionRect = typeCaptionGO.GetComponent<RectTransform>();
            typeCaptionRect.anchorMin = new Vector2(0.05f, 0f);
            typeCaptionRect.anchorMax = new Vector2(1f, 1f);
            typeCaptionRect.sizeDelta = Vector2.zero;
            poiTypeDropdown.captionText = typeCaptionText;
            BuildDropdownTemplate(poiTypeDropdown, typeRowGO);
            poiTypeDropdown.AddOptions(new List<string>
                { "Неизвестно", "Город", "Руины", "Подземелье", "Крепость" });
            poiTypeDropdown.RefreshShownValue();
            poiTypeDropdown.onValueChanged.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiType(sel.Id, (PoiType)v);
            });

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

        void BuildDropdownTemplate(Dropdown dropdown, GameObject dropdownGO)
        {
            var templateGO = new GameObject("Template");
            templateGO.transform.SetParent(dropdownGO.transform, false);
            var templateRect = templateGO.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, 2f);
            templateRect.sizeDelta = new Vector2(0f, 150f);

            var templateBg = templateGO.AddComponent<Image>();
            ThemeService.Tag(templateBg, ThemeRole.Panel2, 0.98f);
            var templateCanvas = templateGO.AddComponent<Canvas>();
            templateCanvas.overrideSorting = true;
            templateCanvas.sortingOrder = 30000;
            templateGO.AddComponent<GraphicRaycaster>();

            var scrollRect = templateGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(templateGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 28f);

            var itemGO = new GameObject("Item");
            itemGO.transform.SetParent(contentGO.transform, false);
            var itemRect = itemGO.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, 26f);
            var itemToggle = itemGO.AddComponent<Toggle>();

            var itemBgGO = new GameObject("Item Background");
            itemBgGO.transform.SetParent(itemGO.transform, false);
            var itemBg = itemBgGO.AddComponent<Image>();
            ThemeService.Tag(itemBg, ThemeRole.AccentSoft, 0.6f);
            var itemBgRect = itemBgGO.GetComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.sizeDelta = Vector2.zero;
            itemToggle.targetGraphic = itemBg;

            var itemCheckGO = new GameObject("Item Checkmark");
            itemCheckGO.transform.SetParent(itemGO.transform, false);
            var itemCheck = itemCheckGO.AddComponent<Image>();
            ThemeService.Tag(itemCheck, ThemeRole.Accent);
            var itemCheckRect = itemCheckGO.GetComponent<RectTransform>();
            itemCheckRect.anchorMin = new Vector2(0f, 0.5f);
            itemCheckRect.anchorMax = new Vector2(0f, 0.5f);
            itemCheckRect.sizeDelta = new Vector2(16f, 16f);
            itemCheckRect.anchoredPosition = new Vector2(10f, 0f);
            itemToggle.graphic = itemCheck;

            var itemLabelGO = new GameObject("Item Label");
            itemLabelGO.transform.SetParent(itemGO.transform, false);
            var itemLabel = itemLabelGO.AddComponent<Text>();
            itemLabel.font = builtinFont;
            itemLabel.fontSize = 12;
            ThemeService.Tag(itemLabel, ThemeRole.Txt);
            itemLabel.alignment = TextAnchor.MiddleLeft;
            var itemLabelRect = itemLabelGO.GetComponent<RectTransform>();
            itemLabelRect.anchorMin = new Vector2(0f, 0f);
            itemLabelRect.anchorMax = new Vector2(1f, 1f);
            itemLabelRect.offsetMin = new Vector2(28f, 1f);
            itemLabelRect.offsetMax = new Vector2(-8f, -1f);

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            templateGO.SetActive(false);
        }
    }
}
