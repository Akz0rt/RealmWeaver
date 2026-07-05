using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    public enum EditorMode { SelectionOverride, Brush }

    /// <summary>
    /// UI panel with three tabs: Карта (layer toggles), Редактор (cell override/brush), Точки (POI management).
    /// Uses ScrollRect inside each tab area so content can exceed the panel height.
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
        public Color panelBackgroundColor = ThemeService.Get(ThemeRole.Panel);
        public Color textColor = Color.white;
        public Color sectionHeaderColor = ThemeService.Get(ThemeRole.Mut);
        public Color activeModeColor = ThemeService.Get(ThemeRole.Accent);
        public Color inactiveModeColor = ThemeService.Get(ThemeRole.Elev);

        EditorMode currentMode = EditorMode.SelectionOverride;

        // Tab system
        int activeTab = 0;
        Button[] tabButtons;
        GameObject[] tabPanels;

        // Panel auto-sizing
        RectTransform panelRect;
        RectTransform contentRect;
        LayoutElement scrollAreaLE;

        // Editor tab
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

        // POI tab
        int poiCount = 5;
        Text poiCountLabel;

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
        }

        void OnDisable()
        {
            if (selectionController != null)
                selectionController.OnSelectionChanged -= HandleSelectionChanged;
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            Debug.Log("MapEditorPanel: EventSystem создан автоматически с InputSystemUIInputModule.");
        }

        void HandleSelectionChanged(IReadOnlyCollection<VoronoiCell> selected)
        {
            if (selectionCountLabel != null)
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
            selectionModeButton.GetComponent<Image>().color = selectionActive ? activeModeColor : inactiveModeColor;
            brushModeButton.GetComponent<Image>().color = !selectionActive ? activeModeColor : inactiveModeColor;
            RefreshPanelLayout();
        }

        void SetTab(int index)
        {
            activeTab = index;
            for (int i = 0; i < tabPanels.Length; i++)
                tabPanels[i].SetActive(i == index);
            for (int i = 0; i < tabButtons.Length; i++)
                tabButtons[i].GetComponent<Image>().color = i == index ? activeModeColor : inactiveModeColor;
            RefreshPanelLayout();
        }

        void RefreshPanelLayout()
        {
            if (tabPanels == null || panelRect == null) return;

            Canvas.ForceUpdateCanvases();
            var activePanelRect = tabPanels[activeTab].GetComponent<RectTransform>();
            float contentHeight = LayoutUtility.GetPreferredHeight(activePanelRect);

            const float tabBarHeight = 24f;
            const float contentPadding = 16f; // Content VerticalLayoutGroup top+bottom padding
            float maxScreenHeight = Screen.height - Mathf.Abs(panelAnchoredPosition.y) - 20f;
            float desiredScrollHeight = Mathf.Min(contentHeight + contentPadding, maxScreenHeight - tabBarHeight);

            scrollAreaLE.flexibleHeight = 0f;
            scrollAreaLE.preferredHeight = Mathf.Max(desiredScrollHeight, 0f);
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, tabBarHeight + scrollAreaLE.preferredHeight);
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
                mapRenderer.ApplyWaterOverride(selected, (WaterOverrideType)waterDropdown.value);
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

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("MapEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Panel: fixed size, vertical stack of tab bar + scroll area
            var panelGO = new GameObject("EditorPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.7f);
            var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
            panelVLG.childControlWidth = true;
            panelVLG.childControlHeight = true;
            panelVLG.childForceExpandWidth = true;
            panelVLG.childForceExpandHeight = false;
            panelVLG.spacing = 0f;
            panelVLG.padding = new RectOffset(0, 0, 0, 0);
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = panelAnchoredPosition;
            panelRect.sizeDelta = new Vector2(300f, Screen.height - Mathf.Abs(panelAnchoredPosition.y));

            // Tab bar (fixed, compact height)
            var tabBarGO = new GameObject("TabBar");
            tabBarGO.transform.SetParent(panelGO.transform, false);
            var tabBarImg = tabBarGO.AddComponent<Image>();
            ThemeService.Tag(tabBarImg, ThemeRole.Panel2);
            var tabBarHLG = tabBarGO.AddComponent<HorizontalLayoutGroup>();
            tabBarHLG.spacing = 1f;
            tabBarHLG.childControlWidth = true;
            tabBarHLG.childForceExpandWidth = true;
            tabBarHLG.childControlHeight = true;
            tabBarHLG.childForceExpandHeight = true;
            tabBarHLG.padding = new RectOffset(2, 2, 2, 2);
            tabBarGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            tabButtons = new Button[3];
            tabButtons[0] = BuildTabButton(tabBarGO.transform, "Карта",    () => SetTab(0));
            tabButtons[1] = BuildTabButton(tabBarGO.transform, "Редактор", () => SetTab(1));
            tabButtons[2] = BuildTabButton(tabBarGO.transform, "Точки",    () => SetTab(2));

            // Scroll area: height is recomputed per-tab in RefreshPanelLayout to match content,
            // capped at maxContentHeight - this is what lets the panel shrink-to-fit instead of
            // always reserving full scrollable space.
            var scrollAreaGO = new GameObject("ScrollArea");
            scrollAreaGO.transform.SetParent(panelGO.transform, false);
            scrollAreaLE = scrollAreaGO.AddComponent<LayoutElement>();
            scrollAreaLE.flexibleHeight = 1f;

            var scrollRect = scrollAreaGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport uses RectMask2D — no Image needed, avoids the alpha=0 stencil issue
            // that made Mask+Image(clear) clip all children.
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollAreaGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            // Content: grows to fit children via ContentSizeFitter
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.padding = new RectOffset(10, 10, 8, 8);
            contentVLG.spacing = 5f;
            contentVLG.childControlWidth = true;
            contentVLG.childControlHeight = false;
            contentVLG.childForceExpandWidth = true;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            var t = contentGO.transform;

            tabPanels = new GameObject[3];

            tabPanels[0] = MakeTabPanel(t, "MapTab");
            BuildMapTab(tabPanels[0].transform);

            tabPanels[1] = MakeTabPanel(t, "EditorTab");
            BuildEditorTab(tabPanels[1].transform);

            tabPanels[2] = MakeTabPanel(t, "PoiTab");
            BuildPoiTab(tabPanels[2].transform);

            SetTab(0);
        }

        GameObject MakeTabPanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 5f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        Button BuildTabButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"TabBtn_{label}");
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
            text.fontSize = 10;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            return btn;
        }

        void BuildMapTab(Transform t)
        {
            AddLabel(t, "─── Слои ───", bold: false, color: sectionHeaderColor);
            AddLayerToggleRow(t, "Рельеф",            true, on => mapRenderer?.SetShowReliefLayer(on));
            AddLayerToggleRow(t, "Биом / климат",     true, on => mapRenderer?.SetShowBiomeLayer(on));
            AddLayerToggleRow(t, "Границы регионов",  true, on => mapRenderer?.SetShowRegionBordersLayer(on));
            AddLayerToggleRow(t, "Береговая линия",   true, on => mapRenderer?.SetShowCoastlineLayer(on));
        }

        void BuildEditorTab(Transform t)
        {
            AddLabel(t, "Режим:", bold: false, color: sectionHeaderColor);

            var modeRowGO = new GameObject("ModeRow");
            modeRowGO.transform.SetParent(t, false);
            var modeRowHLG = modeRowGO.AddComponent<HorizontalLayoutGroup>();
            modeRowHLG.spacing = 4f;
            modeRowHLG.childControlWidth = true;
            modeRowHLG.childForceExpandWidth = true;
            modeRowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            selectionModeButton = AddModeButton(modeRowGO.transform, "Selection & Override",
                () => SetMode(EditorMode.SelectionOverride));
            brushModeButton = AddModeButton(modeRowGO.transform, "Brush",
                () => SetMode(EditorMode.Brush));

            selectionPanelRoot = new GameObject("SelectionOverrideSection");
            selectionPanelRoot.transform.SetParent(t, false);
            var selVLG = selectionPanelRoot.AddComponent<VerticalLayoutGroup>();
            selVLG.spacing = 5f;
            selVLG.childControlWidth = true;
            selVLG.childForceExpandWidth = true;
            selVLG.childControlHeight = false;
            selectionPanelRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildSelectionOverrideSection(selectionPanelRoot.transform);

            brushPanelRoot = new GameObject("BrushSection");
            brushPanelRoot.transform.SetParent(t, false);
            var brushVLG = brushPanelRoot.AddComponent<VerticalLayoutGroup>();
            brushVLG.spacing = 5f;
            brushVLG.childControlWidth = true;
            brushVLG.childForceExpandWidth = true;
            brushVLG.childControlHeight = false;
            brushPanelRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildBrushSection(brushPanelRoot.transform);
        }

        void BuildPoiTab(Transform t)
        {
            AddLabel(t, "─── Точки интереса ───", bold: false, color: sectionHeaderColor);

            var countRowGO = new GameObject("PoiCountRow");
            countRowGO.transform.SetParent(t, false);
            var cHLG = countRowGO.AddComponent<HorizontalLayoutGroup>();
            cHLG.spacing = 4f;
            cHLG.childControlWidth = false;
            cHLG.childControlHeight = false;
            countRowGO.AddComponent<LayoutElement>().preferredHeight = 22f;

            var cLblGO = new GameObject("Label");
            cLblGO.transform.SetParent(countRowGO.transform, false);
            var cLblText = cLblGO.AddComponent<Text>();
            cLblText.text = "Количество:";
            cLblText.font = builtinFont;
            cLblText.fontSize = 12;
            cLblText.color = textColor;
            cLblText.alignment = TextAnchor.MiddleLeft;
            cLblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 22f);

            AddSmallButton(countRowGO.transform, "−", () =>
            {
                if (poiCount > 0) poiCount--;
                if (poiCountLabel != null) poiCountLabel.text = poiCount.ToString();
            });

            var countDisplayGO = new GameObject("CountDisplay");
            countDisplayGO.transform.SetParent(countRowGO.transform, false);
            poiCountLabel = countDisplayGO.AddComponent<Text>();
            poiCountLabel.text = poiCount.ToString();
            poiCountLabel.font = builtinFont;
            poiCountLabel.fontSize = 12;
            poiCountLabel.color = textColor;
            poiCountLabel.alignment = TextAnchor.MiddleCenter;
            countDisplayGO.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 22f);

            AddSmallButton(countRowGO.transform, "+", () =>
            {
                poiCount++;
                if (poiCountLabel != null) poiCountLabel.text = poiCount.ToString();
            });

            AddButton(t, "Сгенерировать точки интереса", OnGeneratePois, ThemeRole.Accent);
            AddButton(t, "Добавить одну точку", () => poiManager?.AddOne(), ThemeRole.Elev);
            AddButton(t, "Очистить все", () => poiManager?.ClearAll(), ThemeRole.Danger);

            var hint = AddLabel(t, "Кликните по точке на карте, чтобы её отредактировать в отдельной панели.");
            ThemeService.Tag(hint, ThemeRole.Mut);
            hint.fontSize = 11;
            hint.fontStyle = FontStyle.Italic;
        }

        void OnGeneratePois()
        {
            if (poiManager == null) return;
            poiManager.GenerateAll(poiCount);
        }

        // ── Sub-section builders ─────────────────────────────────────────────────

        void BuildSelectionOverrideSection(Transform t)
        {
            selectionCountLabel = AddLabel(t, "Выбрано клеток: 0");

            AddLabel(t, "─── Климат ───", bold: false, color: sectionHeaderColor);
            (temperatureSlider, _, temperatureToggle) = AddSliderRow(t, "Температура", 0.5f);
            (moistureSlider, _, moistureToggle) = AddSliderRow(t, "Влажность", 0.5f);

            AddLabel(t, "─── Ландшафт ───", bold: false, color: sectionHeaderColor);
            (elevationSlider, _, elevationToggle) = AddSliderRow(t, "Elevation", 0.5f);

            var waterOptions = new List<string>
                { "Не менять", "Суша (ForceLand)", "Озеро (ForceLake)", "Океан (ForceOcean)" };
            (waterDropdown, waterToggle) = AddDropdownRow(t, "Water-статус", waterOptions);

            var biomeNames = new List<string> { "Авто (computed)" };
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                biomeNames.Add(b.ToString());
            (biomeDropdown, biomeToggle) = AddDropdownRow(t, "Биом напрямую", biomeNames);

            var separatorLabel = AddLabel(t, "─────────────", bold: false);
            ThemeService.Tag(separatorLabel, ThemeRole.Mut);
            AddButton(t, "Применить к выбору", ApplyOverride, ThemeRole.Accent);
            AddButton(t, "Очистить все override", ClearAllOverridesOnSelection, ThemeRole.Danger);
            AddButton(t, "Сбросить выбор", () => selectionController?.ClearSelection(), ThemeRole.Elev);
        }

        void BuildBrushSection(Transform t)
        {
            var toolDropdownGO = new GameObject("ToolDropdown");
            toolDropdownGO.transform.SetParent(t, false);
            toolDropdown = toolDropdownGO.AddComponent<Dropdown>();
            var toolBg = toolDropdownGO.AddComponent<Image>();
            ThemeService.Tag(toolBg, ThemeRole.Panel2);
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
            BuildDropdownTemplate(toolDropdown, toolDropdownGO);
            toolDropdown.AddOptions(new List<string> { "Elevation", "Temperature", "Moisture" });
            toolDropdown.RefreshShownValue();
            toolDropdown.onValueChanged.AddListener(_ => OnBrushValuesChanged());
            toolDropdownGO.AddComponent<LayoutElement>().preferredHeight = 24f;

            var dirRowGO = new GameObject("DirectionRow");
            dirRowGO.transform.SetParent(t, false);
            var dirHLG = dirRowGO.AddComponent<HorizontalLayoutGroup>();
            dirHLG.spacing = 6f;
            dirHLG.childControlWidth = false;
            dirHLG.childControlHeight = false;
            dirRowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            increaseToggle = AddToggle(dirRowGO.transform, true);
            increaseToggle.onValueChanged.AddListener(_ => OnBrushValuesChanged());
            AddLabel(dirRowGO.transform, "Увеличение (+) / Уменьшение (-)");

            var stepRowGO = new GameObject("StepRow");
            stepRowGO.transform.SetParent(t, false);
            var stepHLG = stepRowGO.AddComponent<HorizontalLayoutGroup>();
            stepHLG.spacing = 6f;
            stepHLG.childControlWidth = false;
            stepHLG.childControlHeight = false;
            stepRowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var stepLabelGO = new GameObject("Label");
            stepLabelGO.transform.SetParent(stepRowGO.transform, false);
            var stepLabelText = stepLabelGO.AddComponent<Text>();
            stepLabelText.text = "Шаг";
            stepLabelText.font = builtinFont;
            stepLabelText.fontSize = 12;
            stepLabelText.color = textColor;
            stepLabelText.alignment = TextAnchor.MiddleLeft;
            stepLabelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 20f);

            var stepSliderGO = new GameObject("Slider");
            stepSliderGO.transform.SetParent(stepRowGO.transform, false);
            stepSlider = BuildSlider(stepSliderGO, 0.02f, 0f, 0.2f);
            stepSliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 20f);

            var stepValueGO = new GameObject("Value");
            stepValueGO.transform.SetParent(stepRowGO.transform, false);
            stepValueLabel = stepValueGO.AddComponent<Text>();
            stepValueLabel.text = "0.02";
            stepValueLabel.font = builtinFont;
            stepValueLabel.fontSize = 12;
            stepValueLabel.color = textColor;
            stepValueLabel.alignment = TextAnchor.MiddleLeft;
            stepValueGO.GetComponent<RectTransform>().sizeDelta = new Vector2(45f, 20f);

            stepSlider.onValueChanged.AddListener(v =>
            {
                stepValueLabel.text = v.ToString("F2");
                OnBrushValuesChanged();
            });

            var undoHint = AddLabel(t, "Зажми ЛКМ и веди по карте. Ctrl+Z - отменить мазок.", bold: false);
            ThemeService.Tag(undoHint, ThemeRole.Mut);
            undoHint.fontSize = 11;

            OnBrushValuesChanged();
        }

        void AddLayerToggleRow(Transform parent, string label, bool defaultOn, System.Action<bool> onChanged)
        {
            var rowGO = new GameObject($"{label}LayerRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var toggle = AddToggle(rowGO.transform, defaultOn);
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            AddLabel(rowGO.transform, label);
        }

        // ── Widget helpers ───────────────────────────────────────────────────────

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
            go.AddComponent<LayoutElement>().preferredHeight = bold ? 20f : 16f;
            return label;
        }

        /// <summary>
        /// Builds the required Dropdown template. Without this, programmatically created Dropdowns
        /// cannot open their option list ("The dropdown template is not assigned" error on click).
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
            templateRect.sizeDelta = new Vector2(0f, 150f);

            var templateBg = templateGO.AddComponent<Image>();
            ThemeService.Tag(templateBg, ThemeRole.Panel2);
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
            ThemeService.Tag(itemBg, ThemeRole.AccentSoft);
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
            templateGO.SetActive(false);
        }

        (Slider, Text, Toggle) AddSliderRow(Transform parent, string label, float defaultValue)
        {
            var rowGO = new GameObject($"{label}Row");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            rowGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var toggle = AddToggle(rowGO.transform, true);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            labelText.color = textColor;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = BuildSlider(sliderGO, defaultValue, 0f, 1f);
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 20f);

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueText = valueGO.AddComponent<Text>();
            valueText.text = defaultValue.ToString("F2");
            valueText.font = builtinFont;
            valueText.fontSize = 12;
            valueText.color = textColor;
            valueText.alignment = TextAnchor.MiddleLeft;
            valueGO.GetComponent<RectTransform>().sizeDelta = new Vector2(35f, 20f);

            slider.onValueChanged.AddListener(v => valueText.text = v.ToString("F2"));
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;
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
            rowGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var toggle = AddToggle(rowGO.transform, false);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            labelText.color = textColor;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 22f);

            var dropdownGO = new GameObject("Dropdown");
            dropdownGO.transform.SetParent(rowGO.transform, false);
            var dropdown = dropdownGO.AddComponent<Dropdown>();
            var dropBg = dropdownGO.AddComponent<Image>();
            ThemeService.Tag(dropBg, ThemeRole.Panel2);
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

            BuildDropdownTemplate(dropdown, dropdownGO);
            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();
            dropdownGO.GetComponent<RectTransform>().sizeDelta = new Vector2(155f, 22f);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            return (dropdown, toggle);
        }

        Toggle AddToggle(Transform parent, bool defaultOn)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            toggle.targetGraphic = bg;
            toggle.isOn = defaultOn;

            var checkGO = new GameObject("Check");
            checkGO.transform.SetParent(go.transform, false);
            var checkImg = checkGO.AddComponent<Image>();
            ThemeService.Tag(checkImg, ThemeRole.Accent);
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkRect.sizeDelta = Vector2.zero;
            toggle.graphic = checkImg;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(18f, 18f);
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
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent);
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

        void AddButton(Transform parent, string label, System.Action onClick, ThemeRole? role = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, role ?? ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

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

        void AddSmallButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"SmallBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
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
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

    }
}
