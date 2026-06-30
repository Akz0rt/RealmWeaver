using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public enum EditorMode { SelectionOverride, Brush }

    /// <summary>
    /// Единая UI-панель редактора карты, объединяющая два прежних раздельных инструмента
    /// (CellOverridePanel + BrushToolPanel) в один Canvas с переключателем режима сверху -
    /// убирает визуальное перекрытие панелей и одновременно решает конфликт одновременной
    /// активности CellSelectionController/BrushToolController: переключение режима здесь
    /// автоматически включает нужный контроллер и выключает другой.
    ///
    /// Режимы:
    /// - Selection & Override: выбор клеток мышкой (клик/Shift/drag) + применение АБСОЛЮТНЫХ
    ///   значений climate/landscape override через слайдеры/dropdown.
    /// - Brush: применение ОТНОСИТЕЛЬНОГО изменения (+/-step) к одному параметру при наведении
    ///   курсора, пока зажата ЛКМ, с историей Undo (Ctrl+Z).
    /// </summary>
    public class MapEditorPanel : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public CellSelectionController selectionController;
        public BrushToolController brushController;
        public PoiManager poiManager;

        [Header("Внешний вид")]
        public Vector2 panelAnchoredPosition = new Vector2(20f, -20f);
        public Color panelBackgroundColor = new Color(0f, 0f, 0f, 0.7f);
        public Color textColor = Color.white;
        public Color sectionHeaderColor = new Color(0.7f, 0.85f, 1f);
        public Color activeModeColor = new Color(0.2f, 0.55f, 0.3f);
        public Color inactiveModeColor = new Color(0.3f, 0.3f, 0.3f);

        EditorMode currentMode = EditorMode.SelectionOverride;

        Button selectionModeButton;
        Button brushModeButton;
        GameObject selectionPanelRoot;
        GameObject brushPanelRoot;

        Text selectionCountLabel;
        Slider temperatureSlider;
        Slider moistureSlider;
        Toggle temperatureToggle;
        Toggle moistureToggle;
        Slider elevationSlider;
        Toggle elevationToggle;
        Dropdown waterDropdown;
        Toggle waterToggle;
        Dropdown biomeDropdown;
        Toggle biomeToggle;

        Dropdown toolDropdown;
        Slider stepSlider;
        Text stepValueLabel;
        Toggle increaseToggle;

        // POI section
        readonly Dictionary<PoiType, int> poiCounts = new Dictionary<PoiType, int>
        {
            { PoiType.City, 3 }, { PoiType.Ruin, 2 }, { PoiType.Dungeon, 2 }, { PoiType.Fortress, 1 }
        };
        readonly Dictionary<PoiType, Text> poiCountLabels = new Dictionary<PoiType, Text>();
        GameObject poiEditSubPanel;
        InputField poiNameField;
        InputField poiDescField;
        InputField poiSpritePathField;
        Text poiTypeLabel;
        PoiType addPoiType = PoiType.City;

        Font builtinFont;

        void Awake()
        {
            EnsureEventSystemExists();
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            SetMode(EditorMode.SelectionOverride);
        }

        void OnEnable()
        {
            if (selectionController != null)
                selectionController.OnSelectionChanged += HandleSelectionChanged;
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged += HandlePoiSelectionChanged;
                poiManager.OnPoisChanged += RefreshPoiPanel;
            }
        }

        void OnDisable()
        {
            if (selectionController != null)
                selectionController.OnSelectionChanged -= HandleSelectionChanged;
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged -= HandlePoiSelectionChanged;
                poiManager.OnPoisChanged -= RefreshPoiPanel;
            }
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            Debug.Log("MapEditorPanel: EventSystem не найден в сцене - создан автоматически с InputSystemUIInputModule.");
        }

        void HandleSelectionChanged(IReadOnlyCollection<VoronoiCell> selected)
        {
            selectionCountLabel.text = $"Выбрано клеток: {selected.Count}";
        }

        void SetMode(EditorMode mode)
        {
            currentMode = mode;

            bool selectionActive = mode == EditorMode.SelectionOverride;

            if (selectionController != null) selectionController.enabled = selectionActive;
            if (brushController != null) brushController.brushModeActive = !selectionActive;

            selectionPanelRoot.SetActive(selectionActive);
            brushPanelRoot.SetActive(!selectionActive);

            var selImg = selectionModeButton.GetComponent<Image>();
            var brushImg = brushModeButton.GetComponent<Image>();
            selImg.color = selectionActive ? activeModeColor : inactiveModeColor;
            brushImg.color = !selectionActive ? activeModeColor : inactiveModeColor;
        }

        void ApplyOverride()
        {
            if (mapRenderer == null || selectionController == null) return;

            var selected = selectionController.GetSelectedCells().ToList();
            if (selected.Count == 0) return;

            if (temperatureToggle.isOn)
                mapRenderer.ApplyClimateOverride(selected, temperatureSlider.value, null);
            if (moistureToggle.isOn)
                mapRenderer.ApplyClimateOverride(selected, null, moistureSlider.value);

            if (elevationToggle.isOn)
                mapRenderer.ApplyElevationOverride(selected, elevationSlider.value);

            if (waterToggle.isOn)
            {
                var waterType = (WaterOverrideType)waterDropdown.value;
                mapRenderer.ApplyWaterOverride(selected, waterType);
            }

            if (biomeToggle.isOn)
            {
                int biomeIdx = biomeDropdown.value;
                if (biomeIdx == 0)
                    mapRenderer.ApplyBiomeOverride(selected, null);
                else
                {
                    var biomeValues = (Biome[])System.Enum.GetValues(typeof(Biome));
                    if (biomeIdx - 1 < biomeValues.Length)
                        mapRenderer.ApplyBiomeOverride(selected, biomeValues[biomeIdx - 1]);
                }
            }
        }

        void ClearAllOverridesOnSelection()
        {
            if (mapRenderer == null || selectionController == null) return;

            var selected = selectionController.GetSelectedCells().ToList();
            if (selected.Count == 0) return;

            mapRenderer.ClearAllOverrides(selected);
        }

        void OnBrushValuesChanged()
        {
            if (brushController == null) return;

            brushController.activeTool = (BrushTool)toolDropdown.value;
            brushController.brushStep = stepSlider.value;
            brushController.increaseMode = increaseToggle.isOn;
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var panelGO = new GameObject("EditorPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImage = panelGO.AddComponent<Image>();
            panelImage.color = panelBackgroundColor;

            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = panelAnchoredPosition;
            panelRect.sizeDelta = new Vector2(300f, 10f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panelGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = panelGO.transform;

            AddLabel(t, "Map Editor", bold: true);

            var modeRowGO = new GameObject("ModeRow");
            modeRowGO.transform.SetParent(t, false);
            var modeRowLayout = modeRowGO.AddComponent<HorizontalLayoutGroup>();
            modeRowLayout.spacing = 4f;
            modeRowLayout.childControlWidth = true;
            modeRowLayout.childForceExpandWidth = true;
            var modeRowLE = modeRowGO.AddComponent<LayoutElement>();
            modeRowLE.preferredHeight = 28f;

            selectionModeButton = AddModeButton(modeRowGO.transform, "Selection & Override", () => SetMode(EditorMode.SelectionOverride));
            brushModeButton = AddModeButton(modeRowGO.transform, "Brush", () => SetMode(EditorMode.Brush));

            BuildLayersSection(t);
            BuildPoiSection(t);

            selectionPanelRoot = new GameObject("SelectionOverrideSection");
            selectionPanelRoot.transform.SetParent(t, false);
            var selRootLayout = selectionPanelRoot.AddComponent<VerticalLayoutGroup>();
            selRootLayout.spacing = 5f;
            selRootLayout.childControlWidth = true;
            selRootLayout.childForceExpandWidth = true;
            var selRootFitter = selectionPanelRoot.AddComponent<ContentSizeFitter>();
            selRootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildSelectionOverrideSection(selectionPanelRoot.transform);

            brushPanelRoot = new GameObject("BrushSection");
            brushPanelRoot.transform.SetParent(t, false);
            var brushRootLayout = brushPanelRoot.AddComponent<VerticalLayoutGroup>();
            brushRootLayout.spacing = 5f;
            brushRootLayout.childControlWidth = true;
            brushRootLayout.childForceExpandWidth = true;
            var brushRootFitter = brushPanelRoot.AddComponent<ContentSizeFitter>();
            brushRootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildBrushSection(brushPanelRoot.transform);
        }

        Button AddModeButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"ModeBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = inactiveModeColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            return btn;
        }

        void BuildSelectionOverrideSection(Transform t)
        {
            selectionCountLabel = AddLabel(t, "Выбрано клеток: 0");

            AddLabel(t, "─── Климат ───", bold: false, color: sectionHeaderColor);
            (temperatureSlider, _, temperatureToggle) = AddSliderRow(t, "Температура", 0.5f);
            (moistureSlider, _, moistureToggle) = AddSliderRow(t, "Влажность", 0.5f);

            AddLabel(t, "─── Ландшафт ───", bold: false, color: sectionHeaderColor);
            (elevationSlider, _, elevationToggle) = AddSliderRow(t, "Elevation", 0.5f);

            var waterOptions = new List<string> { "Не менять", "Суша (ForceLand)", "Озеро (ForceLake)", "Океан (ForceOcean)" };
            (waterDropdown, waterToggle) = AddDropdownRow(t, "Water-статус", waterOptions);

            var biomeNames = new List<string> { "Авто (computed)" };
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                biomeNames.Add(b.ToString());
            (biomeDropdown, biomeToggle) = AddDropdownRow(t, "Биом напрямую", biomeNames);

            AddLabel(t, "─────────────", bold: false, color: new Color(0.5f, 0.5f, 0.5f));
            AddButton(t, "Применить к выбору", ApplyOverride, new Color(0.2f, 0.5f, 0.2f));
            AddButton(t, "Очистить все override", ClearAllOverridesOnSelection, new Color(0.5f, 0.2f, 0.2f));
            AddButton(t, "Сбросить выбор", () => selectionController?.ClearSelection(), new Color(0.25f, 0.35f, 0.5f));
        }

        void BuildBrushSection(Transform t)
        {
            var toolDropdownGO = new GameObject("ToolDropdown");
            toolDropdownGO.transform.SetParent(t, false);
            toolDropdown = toolDropdownGO.AddComponent<Dropdown>();
            var toolBg = toolDropdownGO.AddComponent<Image>();
            toolBg.color = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            toolDropdown.targetGraphic = toolBg;

            var toolCaptionGO = new GameObject("Label");
            toolCaptionGO.transform.SetParent(toolDropdownGO.transform, false);
            var toolCaptionText = toolCaptionGO.AddComponent<Text>();
            toolCaptionText.font = builtinFont;
            toolCaptionText.fontSize = 12;
            toolCaptionText.color = textColor;
            toolCaptionText.alignment = TextAnchor.MiddleLeft;
            var toolCaptionRect = toolCaptionGO.GetComponent<RectTransform>();
            toolCaptionRect.anchorMin = new Vector2(0.05f, 0f);
            toolCaptionRect.anchorMax = new Vector2(1f, 1f);
            toolCaptionRect.sizeDelta = Vector2.zero;
            toolDropdown.captionText = toolCaptionText;

            BuildDropdownTemplate(toolDropdown, toolDropdownGO); // обязательно ДО AddOptions/RefreshShownValue
            toolDropdown.AddOptions(new List<string> { "Elevation", "Temperature", "Moisture" });
            toolDropdown.RefreshShownValue();
            toolDropdown.onValueChanged.AddListener(_ => OnBrushValuesChanged());

            var toolDropdownRect = toolDropdownGO.GetComponent<RectTransform>();
            toolDropdownRect.sizeDelta = new Vector2(0f, 24f);
            var toolDropdownLE = toolDropdownGO.AddComponent<LayoutElement>();
            toolDropdownLE.preferredHeight = 24f;

            var dirRowGO = new GameObject("DirectionRow");
            dirRowGO.transform.SetParent(t, false);
            var dirRowLayout = dirRowGO.AddComponent<HorizontalLayoutGroup>();
            dirRowLayout.spacing = 6f;
            dirRowLayout.childControlWidth = false;
            dirRowLayout.childControlHeight = false;
            var dirRowFitter = dirRowGO.AddComponent<ContentSizeFitter>();
            dirRowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var dirRowLE = dirRowGO.AddComponent<LayoutElement>();
            dirRowLE.preferredHeight = 20f;

            increaseToggle = AddToggle(dirRowGO.transform, true);
            increaseToggle.onValueChanged.AddListener(_ => OnBrushValuesChanged());
            AddLabel(dirRowGO.transform, "Увеличение (+) / Уменьшение (-)");

            var stepRowGO = new GameObject("StepRow");
            stepRowGO.transform.SetParent(t, false);
            var stepRowLayout = stepRowGO.AddComponent<HorizontalLayoutGroup>();
            stepRowLayout.spacing = 6f;
            stepRowLayout.childControlWidth = false;
            stepRowLayout.childControlHeight = false;
            var stepRowFitter = stepRowGO.AddComponent<ContentSizeFitter>();
            stepRowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var stepLabelGO = new GameObject("Label");
            stepLabelGO.transform.SetParent(stepRowGO.transform, false);
            var stepLabelText = stepLabelGO.AddComponent<Text>();
            stepLabelText.text = "Шаг";
            stepLabelText.font = builtinFont;
            stepLabelText.fontSize = 12;
            stepLabelText.color = textColor;
            stepLabelText.alignment = TextAnchor.MiddleLeft;
            var stepLabelRect = stepLabelGO.GetComponent<RectTransform>();
            stepLabelRect.sizeDelta = new Vector2(40f, 20f);

            var stepSliderGO = new GameObject("Slider");
            stepSliderGO.transform.SetParent(stepRowGO.transform, false);
            stepSlider = BuildSlider(stepSliderGO, 0.02f, 0f, 0.2f);
            var stepSliderRect = stepSliderGO.GetComponent<RectTransform>();
            stepSliderRect.sizeDelta = new Vector2(140f, 20f);

            var stepValueGO = new GameObject("Value");
            stepValueGO.transform.SetParent(stepRowGO.transform, false);
            stepValueLabel = stepValueGO.AddComponent<Text>();
            stepValueLabel.text = "0.02";
            stepValueLabel.font = builtinFont;
            stepValueLabel.fontSize = 12;
            stepValueLabel.color = textColor;
            stepValueLabel.alignment = TextAnchor.MiddleLeft;
            var stepValueRect = stepValueGO.GetComponent<RectTransform>();
            stepValueRect.sizeDelta = new Vector2(45f, 20f);

            stepSlider.onValueChanged.AddListener(v =>
            {
                stepValueLabel.text = v.ToString("F2");
                OnBrushValuesChanged();
            });

            var stepRowLE = stepRowGO.AddComponent<LayoutElement>();
            stepRowLE.preferredHeight = 20f;

            var undoHint = AddLabel(t, "Зажми ЛКМ и веди по карте. Ctrl+Z - отменить мазок.", bold: false);
            undoHint.color = new Color(0.7f, 0.7f, 0.7f);
            undoHint.fontSize = 11;

            OnBrushValuesChanged();
        }

        void BuildLayersSection(Transform t)
        {
            AddLabel(t, "─── Слои (Combined) ───", bold: false, color: sectionHeaderColor);
            AddLayerToggleRow(t, "Рельеф", true, on => mapRenderer?.SetShowReliefLayer(on));
            AddLayerToggleRow(t, "Биом / климат", true, on => mapRenderer?.SetShowBiomeLayer(on));
            AddLayerToggleRow(t, "Границы регионов", true, on => mapRenderer?.SetShowRegionBordersLayer(on));
            AddLayerToggleRow(t, "Береговая линия", true, on => mapRenderer?.SetShowCoastlineLayer(on));
        }

        void AddLayerToggleRow(Transform parent, string label, bool defaultOn, System.Action<bool> onChanged)
        {
            var rowGO = new GameObject($"{label}LayerRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 20f;

            var toggle = AddToggle(rowGO.transform, defaultOn);
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            AddLabel(rowGO.transform, label);
        }

        Text AddLabel(Transform parent, string text, bool bold = false, Color? color = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = bold ? 15 : 12;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = color ?? textColor;
            label.alignment = TextAnchor.MiddleLeft;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = bold ? 20f : 16f;
            return label;
        }

        /// <summary>
        /// Строит обязательный template для Dropdown - при программном создании через
        /// AddComponent&lt;Dropdown&gt;() (в отличие от GameObject -&gt; UI -&gt; Dropdown в редакторе)
        /// template НЕ создаётся автоматически, и без него Dropdown не может открыть список
        /// вариантов (ошибка "The dropdown template is not assigned" при попытке клика).
        /// Структура соответствует стандартному Dropdown template Unity: Template -&gt; Viewport
        /// (с Mask) -&gt; Content -&gt; Item (Toggle + Background + Checkmark + Label).
        /// </summary>
        void BuildDropdownTemplate(Dropdown dropdown, GameObject dropdownGO)
        {
            var templateGO = new GameObject("Template");
            templateGO.transform.SetParent(dropdownGO.transform, false);
            var templateRect = templateGO.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, 2f);
            templateRect.sizeDelta = new Vector2(0f, 150f); // высота выпадающего списка

            var templateImage = templateGO.AddComponent<Image>();
            templateImage.color = new Color(0.12f, 0.12f, 0.18f, 0.98f);
            var templateCanvas = templateGO.AddComponent<Canvas>();
            templateCanvas.overrideSorting = true;
            templateCanvas.sortingOrder = 30000; // поверх остальной панели, чтобы список не обрезался layout-группами родителя
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
            viewportGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f); // почти прозрачный - Mask требует Graphic на этом же объекте
            var mask = viewportGO.AddComponent<Mask>();
            mask.showMaskGraphic = false;

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
            itemBg.color = new Color(0.25f, 0.4f, 0.6f, 0.6f);
            var itemBgRect = itemBgGO.GetComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.sizeDelta = Vector2.zero;
            itemToggle.targetGraphic = itemBg;

            var itemCheckGO = new GameObject("Item Checkmark");
            itemCheckGO.transform.SetParent(itemGO.transform, false);
            var itemCheck = itemCheckGO.AddComponent<Image>();
            itemCheck.color = new Color(0.3f, 0.9f, 0.4f);
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
            itemLabel.color = Color.white;
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

            templateGO.SetActive(false); // Dropdown сам активирует template при открытии списка
        }

        (Slider, Text, Toggle) AddSliderRow(Transform parent, string label, float defaultValue)
        {
            var rowGO = new GameObject($"{label}Row");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            var rowFitter = rowGO.AddComponent<ContentSizeFitter>();
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var toggle = AddToggle(rowGO.transform, true);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            labelText.color = textColor;
            labelText.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(90f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = BuildSlider(sliderGO, defaultValue, 0f, 1f);
            var sliderRect = sliderGO.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(110f, 20f);

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueText = valueGO.AddComponent<Text>();
            valueText.text = defaultValue.ToString("F2");
            valueText.font = builtinFont;
            valueText.fontSize = 12;
            valueText.color = textColor;
            valueText.alignment = TextAnchor.MiddleLeft;
            var valueRect = valueGO.GetComponent<RectTransform>();
            valueRect.sizeDelta = new Vector2(35f, 20f);

            slider.onValueChanged.AddListener(v => valueText.text = v.ToString("F2"));

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 20f;

            return (slider, valueText, toggle);
        }

        (Dropdown, Toggle) AddDropdownRow(Transform parent, string label, List<string> options)
        {
            var rowGO = new GameObject($"{label}Row");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            var rowFitter = rowGO.AddComponent<ContentSizeFitter>();
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var toggle = AddToggle(rowGO.transform, false);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            labelText.color = textColor;
            labelText.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(90f, 22f);

            var dropdownGO = new GameObject("Dropdown");
            dropdownGO.transform.SetParent(rowGO.transform, false);
            var dropdown = dropdownGO.AddComponent<Dropdown>();
            var dropBg = dropdownGO.AddComponent<Image>();
            dropBg.color = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            dropdown.targetGraphic = dropBg;

            var captionGO = new GameObject("Label");
            captionGO.transform.SetParent(dropdownGO.transform, false);
            var captionText = captionGO.AddComponent<Text>();
            captionText.font = builtinFont;
            captionText.fontSize = 11;
            captionText.color = textColor;
            captionText.alignment = TextAnchor.MiddleLeft;
            var captionRect = captionGO.GetComponent<RectTransform>();
            captionRect.anchorMin = new Vector2(0.05f, 0f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.sizeDelta = Vector2.zero;
            dropdown.captionText = captionText;

            BuildDropdownTemplate(dropdown, dropdownGO); // обязательно ДО AddOptions/RefreshShownValue
            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();

            var dropRect = dropdownGO.GetComponent<RectTransform>();
            dropRect.sizeDelta = new Vector2(155f, 22f);

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 22f;

            return (dropdown, toggle);
        }

        Toggle AddToggle(Transform parent, bool defaultOn)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            toggle.targetGraphic = bg;
            toggle.isOn = defaultOn;

            var checkGO = new GameObject("Check");
            checkGO.transform.SetParent(go.transform, false);
            var checkImg = checkGO.AddComponent<Image>();
            checkImg.color = new Color(0.3f, 0.9f, 0.4f);
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkRect.sizeDelta = Vector2.zero;
            toggle.graphic = checkImg;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(18f, 18f);
            return toggle;
        }

        Slider BuildSlider(GameObject sliderGO, float defaultValue, float min, float max)
        {
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = defaultValue;

            var bg = new GameObject("Bg");
            bg.transform.SetParent(sliderGO.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.15f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.6f, 0.9f, 0.9f);
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
            handleImg.color = Color.white;
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(12f, 18f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            return slider;
        }

        void AddButton(Transform parent, string label, System.Action onClick, Color? bgColor = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.25f, 0.45f, 0.7f, 0.9f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        // ── POI section ────────────────────────────────────────────────────────

        void BuildPoiSection(Transform t)
        {
            AddLabel(t, "─── Точки интереса ───", bold: false, color: sectionHeaderColor);

            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
                BuildPoiCountRow(t, type);

            AddButton(t, "Сгенерировать", OnGeneratePois, new Color(0.2f, 0.45f, 0.2f));

            // "Добавить" row: type dropdown + button
            var addRowGO = new GameObject("AddPoiRow");
            addRowGO.transform.SetParent(t, false);
            var addHLayout = addRowGO.AddComponent<HorizontalLayoutGroup>();
            addHLayout.spacing = 4f;
            addHLayout.childControlWidth = true;
            addHLayout.childForceExpandWidth = true;
            addRowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            var typeDropdownGO = new GameObject("TypeDropdown");
            typeDropdownGO.transform.SetParent(addRowGO.transform, false);
            var typeDropdown = typeDropdownGO.AddComponent<Dropdown>();
            var typeDropBg = typeDropdownGO.AddComponent<Image>();
            typeDropBg.color = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            typeDropdown.targetGraphic = typeDropBg;
            var typeCaptionGO = new GameObject("Label");
            typeCaptionGO.transform.SetParent(typeDropdownGO.transform, false);
            var typeCaptionText = typeCaptionGO.AddComponent<Text>();
            typeCaptionText.font = builtinFont;
            typeCaptionText.fontSize = 11;
            typeCaptionText.color = textColor;
            typeCaptionText.alignment = TextAnchor.MiddleLeft;
            var typeCaptionRect = typeCaptionGO.GetComponent<RectTransform>();
            typeCaptionRect.anchorMin = new Vector2(0.05f, 0f);
            typeCaptionRect.anchorMax = new Vector2(1f, 1f);
            typeCaptionRect.sizeDelta = Vector2.zero;
            typeDropdown.captionText = typeCaptionText;
            BuildDropdownTemplate(typeDropdown, typeDropdownGO);
            typeDropdown.AddOptions(new List<string> { "Город", "Руины", "Подземелье", "Крепость" });
            typeDropdown.RefreshShownValue();
            typeDropdown.onValueChanged.AddListener(v => addPoiType = (PoiType)v);

            var addBtnGO = new GameObject("AddBtn");
            addBtnGO.transform.SetParent(addRowGO.transform, false);
            var addBtnImg = addBtnGO.AddComponent<Image>();
            addBtnImg.color = new Color(0.2f, 0.4f, 0.6f, 0.9f);
            var addBtn = addBtnGO.AddComponent<Button>();
            addBtn.targetGraphic = addBtnImg;
            addBtn.onClick.AddListener(() => poiManager?.AddOne(addPoiType));
            var addBtnLE = addBtnGO.AddComponent<LayoutElement>();
            addBtnLE.preferredWidth = 80f;
            addBtnLE.preferredHeight = 26f;
            var addBtnTextGO = new GameObject("Text");
            addBtnTextGO.transform.SetParent(addBtnGO.transform, false);
            var addBtnText = addBtnTextGO.AddComponent<Text>();
            addBtnText.text = "Добавить";
            addBtnText.font = builtinFont;
            addBtnText.fontSize = 11;
            addBtnText.color = Color.white;
            addBtnText.alignment = TextAnchor.MiddleCenter;
            var addBtnTextRect = addBtnTextGO.GetComponent<RectTransform>();
            addBtnTextRect.anchorMin = Vector2.zero;
            addBtnTextRect.anchorMax = Vector2.one;
            addBtnTextRect.sizeDelta = Vector2.zero;

            AddButton(t, "Очистить все", () => poiManager?.ClearAll(), new Color(0.5f, 0.2f, 0.2f));

            // Edit sub-panel, hidden until a POI is selected
            poiEditSubPanel = new GameObject("PoiEditSubPanel");
            poiEditSubPanel.transform.SetParent(t, false);
            var subLayout = poiEditSubPanel.AddComponent<VerticalLayoutGroup>();
            subLayout.spacing = 4f;
            subLayout.childControlWidth = true;
            subLayout.childForceExpandWidth = true;
            var subFitter = poiEditSubPanel.AddComponent<ContentSizeFitter>();
            subFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildPoiEditSubPanel(poiEditSubPanel.transform);
            poiEditSubPanel.SetActive(false);
        }

        void BuildPoiCountRow(Transform parent, PoiType type)
        {
            string typeName;
            switch (type)
            {
                case PoiType.City:     typeName = "Города";       break;
                case PoiType.Ruin:     typeName = "Руины";        break;
                case PoiType.Dungeon:  typeName = "Подземелья";   break;
                case PoiType.Fortress: typeName = "Крепости";     break;
                default:               typeName = type.ToString(); break;
            }

            var rowGO = new GameObject($"{type}CountRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var lbl = new GameObject("Label");
            lbl.transform.SetParent(rowGO.transform, false);
            var lblText = lbl.AddComponent<Text>();
            lblText.text = typeName;
            lblText.font = builtinFont;
            lblText.fontSize = 12;
            lblText.color = textColor;
            lblText.alignment = TextAnchor.MiddleLeft;
            lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 20f);

            AddSmallButton(rowGO.transform, "−", () =>
            {
                if (poiCounts[type] > 0) poiCounts[type]--;
                poiCountLabels[type].text = poiCounts[type].ToString();
            });

            var countLblGO = new GameObject("Count");
            countLblGO.transform.SetParent(rowGO.transform, false);
            var countText = countLblGO.AddComponent<Text>();
            countText.text = poiCounts[type].ToString();
            countText.font = builtinFont;
            countText.fontSize = 12;
            countText.color = textColor;
            countText.alignment = TextAnchor.MiddleCenter;
            countLblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(26f, 20f);
            poiCountLabels[type] = countText;

            AddSmallButton(rowGO.transform, "+", () =>
            {
                poiCounts[type]++;
                poiCountLabels[type].text = poiCounts[type].ToString();
            });
        }

        void AddSmallButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"SmallBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var tr2 = textGO.GetComponent<RectTransform>();
            tr2.anchorMin = Vector2.zero;
            tr2.anchorMax = Vector2.one;
            tr2.sizeDelta = Vector2.zero;
        }

        void BuildPoiEditSubPanel(Transform t)
        {
            AddLabel(t, "─ Выбранная точка ─", bold: false, color: sectionHeaderColor);
            poiTypeLabel = AddLabel(t, "Тип: —");

            AddLabel(t, "Название:");
            poiNameField = BuildPoiInputField(t, multiline: false);
            poiNameField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiName(sel.Id, v);
            });

            AddLabel(t, "Описание:");
            poiDescField = BuildPoiInputField(t, multiline: true);
            poiDescField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiDescription(sel.Id, v);
            });

            AddLabel(t, "Иконка (путь к файлу):");
            var spriteRow = new GameObject("SpriteRow");
            spriteRow.transform.SetParent(t, false);
            var srLayout = spriteRow.AddComponent<HorizontalLayoutGroup>();
            srLayout.spacing = 4f;
            srLayout.childControlWidth = true;
            srLayout.childForceExpandWidth = true;
            spriteRow.AddComponent<LayoutElement>().preferredHeight = 22f;

            poiSpritePathField = BuildPoiInputField(spriteRow.transform, multiline: false);

            var applyBtnGO = new GameObject("ApplyBtn");
            applyBtnGO.transform.SetParent(spriteRow.transform, false);
            var applyImg = applyBtnGO.AddComponent<Image>();
            applyImg.color = new Color(0.3f, 0.5f, 0.3f, 0.9f);
            var applyBtn = applyBtnGO.AddComponent<Button>();
            applyBtn.targetGraphic = applyImg;
            applyBtn.onClick.AddListener(() =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiSpritePath(sel.Id, poiSpritePathField.text);
            });
            applyBtnGO.AddComponent<LayoutElement>().preferredWidth = 70f;
            var applyTextGO = new GameObject("Text");
            applyTextGO.transform.SetParent(applyBtnGO.transform, false);
            var applyText = applyTextGO.AddComponent<Text>();
            applyText.text = "Применить";
            applyText.font = builtinFont;
            applyText.fontSize = 10;
            applyText.color = Color.white;
            applyText.alignment = TextAnchor.MiddleCenter;
            var applyRect = applyTextGO.GetComponent<RectTransform>();
            applyRect.anchorMin = Vector2.zero;
            applyRect.anchorMax = Vector2.one;
            applyRect.sizeDelta = Vector2.zero;

            AddButton(t, "Удалить точку", () =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.DeletePoi(sel.Id);
            }, new Color(0.55f, 0.15f, 0.15f));
        }

        InputField BuildPoiInputField(Transform parent, bool multiline)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);
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
            text.color = textColor;
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
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyle.Italic;
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 60f : 22f;
            le.flexibleWidth = 1f;

            return field;
        }

        void OnGeneratePois()
        {
            if (poiManager == null) return;
            poiManager.GenerateAll(new Dictionary<PoiType, int>(poiCounts));
        }

        void HandlePoiSelectionChanged(PoiData selected)
        {
            if (selected == null)
            {
                poiEditSubPanel?.SetActive(false);
                return;
            }
            poiEditSubPanel?.SetActive(true);
            if (poiNameField != null) poiNameField.text = selected.Name;
            if (poiDescField != null) poiDescField.text = selected.Description;
            if (poiSpritePathField != null) poiSpritePathField.text = selected.CustomSpritePath ?? "";

            string typeName;
            switch (selected.Type)
            {
                case PoiType.City:     typeName = "Город";      break;
                case PoiType.Ruin:     typeName = "Руины";      break;
                case PoiType.Dungeon:  typeName = "Подземелье"; break;
                case PoiType.Fortress: typeName = "Крепость";   break;
                default:               typeName = selected.Type.ToString(); break;
            }
            if (poiTypeLabel != null) poiTypeLabel.text = $"Тип: {typeName}";
        }

        void RefreshPoiPanel()
        {
            // Sub-panel visibility is managed by HandlePoiSelectionChanged; nothing extra needed here.
        }
    }
}
