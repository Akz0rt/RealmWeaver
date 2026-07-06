using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Редактор" — панель кисти. Основной режим "Кисть" (radius-кисть по мокапу Screen C:
    /// Что редактируем / Режим / Форма / Размер / Сила / Отменить всё + контекстная палитра биома)
    /// плюс сохранённый вторичный режим "Точное выделение" (старый Selection+Override), доступный
    /// через переключатель в шапке. Вся логика применения кисти — в BrushToolController.
    /// </summary>
    public class EditorBrushPanel : MonoBehaviour
    {
        public enum EditorMode { SelectionOverride, Brush }

        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public CellSelectionController selectionController;
        public BrushToolController brushController;

        EditorMode currentMode = EditorMode.Brush;

        // Header
        Text headerTitle;
        Text headerLink;

        // Sub-roots
        GameObject selectionPanelRoot;
        GameObject brushPanelRoot;

        // Brush controls
        readonly Dictionary<BrushTool, Image> targetPillBg = new Dictionary<BrushTool, Image>();
        readonly Dictionary<BrushTool, Text> targetPillTxt = new Dictionary<BrushTool, Text>();
        readonly Dictionary<BrushMode, Image> modeSegBg = new Dictionary<BrushMode, Image>();
        readonly Dictionary<BrushMode, Text> modeSegTxt = new Dictionary<BrushMode, Text>();
        GameObject modeCaptionGO;
        GameObject modeSegmentGO;
        Image circleIconBg, squareIconBg;
        Outline circleIconOutline, squareIconOutline;
        Slider sizeSlider, strengthSlider;
        Text sizeValue, strengthValue;

        // Biome palette (contextual, shown only in Brush mode with target = Biome)
        GameObject biomePaletteRoot;
        readonly Dictionary<Biome, Outline> swatchOutline = new Dictionary<Biome, Outline>();

        // Selection & Override widgets (unchanged behaviour)
        Text selectionCountLabel;
        Slider temperatureSlider, moistureSlider, elevationSlider;
        Toggle temperatureToggle, moistureToggle, elevationToggle, waterToggle, biomeToggle;
        Dropdown waterDropdown, biomeDropdown;

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            SetMode(EditorMode.Brush);
            OnTargetChanged(BrushTool.Elevation);
            OnModeChanged(BrushMode.Raise);
            OnShapeChanged(BrushShape.Circle);
        }

        void OnEnable()
        {
            if (selectionController != null)
                selectionController.OnSelectionChanged += HandleSelectionChanged;
            // Вкладка "Редактор" стала активной — восстановить liveness кисти/выделения по текущему режиму.
            // (headerTitle != null — значит Awake уже построил UI; на самом первом включении Awake идёт раньше OnEnable.)
            if (headerTitle != null) SetMode(currentMode);
        }

        void OnDisable()
        {
            if (selectionController != null)
                selectionController.OnSelectionChanged -= HandleSelectionChanged;
            // Ушли с вкладки "Редактор" — гасим оба редактирующих контроллера, чтобы кисть/выделение
            // не оставались живыми на других вкладках (Карта/Точки).
            if (brushController != null) brushController.brushModeActive = false;
            if (selectionController != null) selectionController.enabled = false;
        }

        void HandleSelectionChanged(IReadOnlyCollection<VoronoiCell> selected)
        {
            if (selectionCountLabel != null)
                selectionCountLabel.text = $"Выбрано клеток: {selected.Count}";
        }

        // ── Mode switching (Кисть ↔ Точное выделение) ────────────────────────────

        void SetMode(EditorMode mode)
        {
            currentMode = mode;
            bool selectionActive = mode == EditorMode.SelectionOverride;
            if (selectionController != null) selectionController.enabled = selectionActive;
            if (brushController != null) brushController.brushModeActive = !selectionActive;

            selectionPanelRoot.SetActive(selectionActive);
            brushPanelRoot.SetActive(!selectionActive);

            headerTitle.text = selectionActive ? "Точное выделение" : "Кисть";
            headerLink.text = selectionActive ? "← Кисть" : "Точное выделение";

            UpdateBiomePaletteVisibility();
        }

        // ── Target / Mode / Shape handlers ───────────────────────────────────────

        void OnTargetChanged(BrushTool target)
        {
            if (brushController != null) brushController.activeTool = target;

            foreach (var kvp in targetPillBg)
            {
                bool on = kvp.Key == target;
                ThemeService.Tag(kvp.Value, on ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(targetPillTxt[kvp.Key], on ? ThemeRole.AccentInk : ThemeRole.Txt);
            }

            // Режим (Поднять/Опустить/Сгладить) бессмыслен для категории — прячем на биоме.
            bool isBiome = target == BrushTool.Biome;
            if (modeCaptionGO != null) modeCaptionGO.SetActive(!isBiome);
            if (modeSegmentGO != null) modeSegmentGO.SetActive(!isBiome);

            UpdateBiomePaletteVisibility();
        }

        void OnModeChanged(BrushMode mode)
        {
            if (brushController != null) brushController.mode = mode;
            foreach (var kvp in modeSegBg)
            {
                bool on = kvp.Key == mode;
                ThemeService.Tag(kvp.Value, on ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(modeSegTxt[kvp.Key], on ? ThemeRole.AccentInk : ThemeRole.Txt);
            }
        }

        void OnShapeChanged(BrushShape shape)
        {
            if (brushController != null) brushController.shape = shape;
            bool circle = shape == BrushShape.Circle;
            ThemeService.Tag(circleIconBg, circle ? ThemeRole.AccentSoft : ThemeRole.Elev);
            ThemeService.Tag(squareIconBg, !circle ? ThemeRole.AccentSoft : ThemeRole.Elev);
            circleIconOutline.effectColor = ThemeService.Get(ThemeRole.Accent);
            squareIconOutline.effectColor = ThemeService.Get(ThemeRole.Accent);
            circleIconOutline.enabled = circle;
            squareIconOutline.enabled = !circle;
        }

        void OnBiomeSelected(Biome biome)
        {
            if (brushController != null) brushController.selectedBiome = biome;
            foreach (var kvp in swatchOutline)
            {
                bool on = kvp.Key == biome;
                kvp.Value.effectColor = ThemeService.Get(ThemeRole.Accent);
                kvp.Value.enabled = on;
            }
        }

        void UpdateBiomePaletteVisibility()
        {
            if (biomePaletteRoot == null) return;
            bool show = currentMode == EditorMode.Brush
                        && brushController != null
                        && brushController.activeTool == BrushTool.Biome;
            biomePaletteRoot.SetActive(show);
        }

        // ── Selection & Override apply (unchanged) ───────────────────────────────

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

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("EditorBrushCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("EditorPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.92f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f - MapToolbarUI.BarHeightPixels - 40f); // ниже 40px меню + 46px тулбар
            panelRect.sizeDelta = new Vector2(264f, 0f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 9f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiShadow.Add(panelRect);

            BuildEditorTab(panelGO.transform);
        }

        void BuildEditorTab(Transform t)
        {
            BuildHeaderRow(t);

            selectionPanelRoot = MakeSection(t, "SelectionOverrideSection");
            BuildSelectionOverrideSection(selectionPanelRoot.transform);

            brushPanelRoot = MakeSection(t, "BrushSection");
            BuildBrushSection(brushPanelRoot.transform);
        }

        GameObject MakeSection(Transform t, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(t, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 9f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        void BuildHeaderRow(Transform t)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            headerTitle = titleGO.AddComponent<Text>();
            headerTitle.text = "Кисть";
            headerTitle.font = builtinFont;
            headerTitle.fontSize = 14;
            headerTitle.fontStyle = FontStyle.Bold;
            ThemeService.Tag(headerTitle, ThemeRole.Txt);
            headerTitle.alignment = TextAnchor.MiddleLeft;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.6f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            var linkGO = new GameObject("ModeSwitch");
            linkGO.transform.SetParent(rowGO.transform, false);
            headerLink = linkGO.AddComponent<Text>();
            headerLink.text = "Точное выделение";
            headerLink.font = builtinFont;
            headerLink.fontSize = 11;
            ThemeService.Tag(headerLink, ThemeRole.Accent);
            headerLink.alignment = TextAnchor.MiddleRight;
            var linkRect = linkGO.GetComponent<RectTransform>();
            linkRect.anchorMin = new Vector2(0.4f, 0f);
            linkRect.anchorMax = new Vector2(1f, 1f);
            linkRect.offsetMin = Vector2.zero;
            linkRect.offsetMax = Vector2.zero;
            var linkBtn = linkGO.AddComponent<Button>();
            linkBtn.targetGraphic = headerLink;
            linkBtn.onClick.AddListener(() =>
                SetMode(currentMode == EditorMode.Brush ? EditorMode.SelectionOverride : EditorMode.Brush));
        }

        // ── Brush section (mockup Screen C) ──────────────────────────────────────

        void BuildBrushSection(Transform t)
        {
            AddCaption(t, "ЧТО РЕДАКТИРУЕМ");
            BuildTargetGrid(t);

            modeCaptionGO = AddCaption(t, "РЕЖИМ").gameObject;
            BuildModeSegment(t);

            BuildShapeRow(t);

            BuildLabeledSlider(t, "Размер", 8f, 120f, 42f, out sizeSlider, out sizeValue, isPercent: false, v =>
            {
                if (brushController != null) brushController.brushRadius = v;
            });

            BuildLabeledSlider(t, "Сила", 0f, 1f, 0.6f, out strengthSlider, out strengthValue, isPercent: true, v =>
            {
                if (brushController != null) brushController.strength = v;
            });

            BuildBiomePaletteSection(t);

            AddWideButton(t, "Отменить всё", () => mapRenderer?.UndoAllBrushStrokes());
        }

        void BuildTargetGrid(Transform t)
        {
            var gridGO = new GameObject("TargetGrid");
            gridGO.transform.SetParent(t, false);
            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(115f, 28f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            gridGO.AddComponent<LayoutElement>().preferredHeight = 28f * 2f + 6f;

            AddPill(gridGO.transform, BrushTool.Elevation, "Высота");
            AddPill(gridGO.transform, BrushTool.Temperature, "Температура");
            AddPill(gridGO.transform, BrushTool.Moisture, "Влажность");
            AddPill(gridGO.transform, BrushTool.Biome, "Биом");
        }

        void AddPill(Transform parent, BrushTool target, string label)
        {
            var go = new GameObject($"Pill_{target}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnTargetChanged(target));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "● " + label;
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleLeft;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10f, 0f);
            tr.offsetMax = new Vector2(-4f, 0f);

            targetPillBg[target] = img;
            targetPillTxt[target] = text;
        }

        void BuildModeSegment(Transform t)
        {
            var rowGO = new GameObject("ModeSegment");
            rowGO.transform.SetParent(t, false);
            modeSegmentGO = rowGO;
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            AddSegment(rowGO.transform, BrushMode.Raise, "Поднять");
            AddSegment(rowGO.transform, BrushMode.Lower, "Опустить");
            AddSegment(rowGO.transform, BrushMode.Smooth, "Сгладить");
        }

        void AddSegment(Transform parent, BrushMode mode, string label)
        {
            var go = new GameObject($"Seg_{mode}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnModeChanged(mode));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            modeSegBg[mode] = img;
            modeSegTxt[mode] = text;
        }

        void BuildShapeRow(Transform t)
        {
            var rowGO = new GameObject("ShapeRow");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var label = labelGO.AddComponent<Text>();
            label.text = "Форма";
            label.font = builtinFont;
            label.fontSize = 12;
            ThemeService.Tag(label, ThemeRole.Txt);
            label.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            (circleIconBg, circleIconOutline) = AddShapeIcon(rowGO.transform, "○", BrushShape.Circle, xOffset: -34f);
            (squareIconBg, squareIconOutline) = AddShapeIcon(rowGO.transform, "□", BrushShape.Square, xOffset: 0f);
        }

        (Image, Outline) AddShapeIcon(Transform parent, string glyph, BrushShape shape, float xOffset)
        {
            var go = new GameObject($"Shape_{shape}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = false;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnShapeChanged(shape));
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(28f, 28f);
            rect.anchoredPosition = new Vector2(xOffset, 0f);

            var textGO = new GameObject("Glyph");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = glyph;
            text.font = builtinFont;
            text.fontSize = 16;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            return (img, outline);
        }

        void BuildLabeledSlider(Transform t, string label, float min, float max, float def,
                                out Slider slider, out Text valueLabel, bool isPercent,
                                System.Action<float> onChanged)
        {
            // Контейнер группирует "подпись+значение" и слайдер вплотную (иначе межэлементный
            // отступ VLG разносит их так же, как соседние блоки, и группа теряется).
            var groupGO = new GameObject($"{label}Group");
            groupGO.transform.SetParent(t, false);
            var gvlg = groupGO.AddComponent<VerticalLayoutGroup>();
            gvlg.spacing = 3f;
            gvlg.childControlWidth = true;
            gvlg.childForceExpandWidth = true;
            gvlg.childControlHeight = true;
            gvlg.childForceExpandHeight = false;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Строка: подпись слева + значение справа (Accent).
            var headGO = new GameObject($"{label}Head");
            headGO.transform.SetParent(groupGO.transform, false);
            headGO.AddComponent<LayoutElement>().preferredHeight = 16f;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(headGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var valGO = new GameObject("Value");
            valGO.transform.SetParent(headGO.transform, false);
            valueLabel = valGO.AddComponent<Text>();
            valueLabel.font = builtinFont;
            valueLabel.fontSize = 11;
            ThemeService.Tag(valueLabel, ThemeRole.Accent);
            valueLabel.alignment = TextAnchor.MiddleRight;
            var valRect = valGO.GetComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.4f, 0f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            // Слайдер под строкой.
            var sliderGO = new GameObject($"{label}Slider");
            sliderGO.transform.SetParent(groupGO.transform, false);
            slider = BuildSlider(sliderGO, def, min, max);
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 14f;

            var valText = valueLabel;
            System.Action<float> refresh = v =>
                valText.text = isPercent ? $"{Mathf.RoundToInt(v * 100f)}%" : $"{Mathf.RoundToInt(v)} px";
            refresh(def);
            slider.onValueChanged.AddListener(v => { refresh(v); onChanged?.Invoke(v); });
            onChanged?.Invoke(def);
        }

        // ── Contextual biome palette (in-panel section, Brush + target = Biome) ───

        void BuildBiomePaletteSection(Transform t)
        {
            var sectionGO = new GameObject("BiomePalette");
            sectionGO.transform.SetParent(t, false);
            var vlg = sectionGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            sectionGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddCaption(sectionGO.transform, "ПАЛИТРА БИОМА");

            var gridGO = new GameObject("SwatchGrid");
            gridGO.transform.SetParent(sectionGO.transform, false);
            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(26f, 26f);
            grid.spacing = new Vector2(3f, 3f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 8;

            var biomes = (Biome[])System.Enum.GetValues(typeof(Biome));
            int rows = Mathf.CeilToInt(biomes.Length / 8f);
            gridGO.AddComponent<LayoutElement>().preferredHeight = rows * 26f + (rows - 1) * 3f;

            foreach (var biome in biomes)
                AddSwatch(gridGO.transform, biome);

            biomePaletteRoot = sectionGO;
            sectionGO.SetActive(false);
        }

        void AddSwatch(Transform parent, Biome biome)
        {
            var go = new GameObject($"Swatch_{biome}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = RegionColorPalette.GetBiomeColor(biome); // фиксированный цвет биома, не тема
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnBiomeSelected(biome));
            swatchOutline[biome] = outline;
        }

        // ── Selection & Override section (preserved from prior extraction) ────────

        void BuildSelectionOverrideSection(Transform t)
        {
            selectionCountLabel = AddLabel(t, "Выбрано клеток: 0");

            AddLabel(t, "─── Климат ───", bold: false, role: ThemeRole.Mut);
            (temperatureSlider, _, temperatureToggle) = AddSliderRow(t, "Температура", 0.5f);
            (moistureSlider, _, moistureToggle) = AddSliderRow(t, "Влажность", 0.5f);

            AddLabel(t, "─── Ландшафт ───", bold: false, role: ThemeRole.Mut);
            (elevationSlider, _, elevationToggle) = AddSliderRow(t, "Высота", 0.5f);

            var waterOptions = new List<string>
                { "Не менять", "Суша (ForceLand)", "Озеро (ForceLake)", "Океан (ForceOcean)" };
            (waterDropdown, waterToggle) = AddDropdownRow(t, "Water-статус", waterOptions);

            var biomeNames = new List<string> { "Авто (computed)" };
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                biomeNames.Add(b.ToString());
            (biomeDropdown, biomeToggle) = AddDropdownRow(t, "Биом напрямую", biomeNames);

            AddWideButton(t, "Применить к выбору", ApplyOverride, ThemeRole.Accent);
            AddWideButton(t, "Очистить все override", ClearAllOverridesOnSelection, ThemeRole.Danger);
            AddWideButton(t, "Сбросить выбор", () => selectionController?.ClearSelection());
        }

        // ── Shared widget helpers ────────────────────────────────────────────────

        Text AddCaption(Transform parent, string text)
        {
            var go = new GameObject("Caption");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = 10;
            label.fontStyle = FontStyle.Bold;
            ThemeService.Tag(label, ThemeRole.Mut);
            label.alignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            return label;
        }

        Text AddLabel(Transform parent, string text, bool bold = false, ThemeRole? role = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = bold ? 15 : 12;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            ThemeService.Tag(label, role ?? ThemeRole.Txt);
            label.alignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = bold ? 20f : 16f;
            return label;
        }

        void AddWideButton(Transform parent, string label, System.Action onClick, ThemeRole? role = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var backgroundRole = role ?? ThemeRole.Elev;
            ThemeService.Tag(img, backgroundRole);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 32f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, backgroundRole == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
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

        (Slider, Text, Toggle) AddSliderRow(Transform parent, string label, float defaultValue)
        {
            var rowGO = new GameObject($"{label}Row");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childForceExpandWidth = false;

            var toggle = AddToggle(rowGO.transform, true);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(84f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = BuildSlider(sliderGO, defaultValue, 0f, 1f);
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(68f, 20f);

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueText = valueGO.AddComponent<Text>();
            valueText.text = defaultValue.ToString("F2");
            valueText.font = builtinFont;
            valueText.fontSize = 12;
            ThemeService.Tag(valueText, ThemeRole.Txt);
            valueText.alignment = TextAnchor.MiddleLeft;
            valueGO.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 20f);

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
            hLayout.childForceExpandWidth = false;

            var toggle = AddToggle(rowGO.transform, false);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(84f, 22f);

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
            ThemeService.Tag(captionText, ThemeRole.Txt);
            captionText.alignment = TextAnchor.MiddleLeft;
            var captionRect = captionGO.GetComponent<RectTransform>();
            captionRect.anchorMin = new Vector2(0.05f, 0f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.sizeDelta = Vector2.zero;
            dropdown.captionText = captionText;

            BuildDropdownTemplate(dropdown, dropdownGO);
            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();
            dropdownGO.GetComponent<RectTransform>().sizeDelta = new Vector2(118f, 22f);
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
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(12f, 18f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }
    }
}
