using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.RegionLabels;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Регионы" tab — political-region panel (user-controlled regions). Left, full-height column
    /// (same shape as PoiToolPanel), two segments:
    ///   • «Создание» — count/min-size sliders + «Генерировать регионы» (confirms replacement via
    ///     ConfirmDialog if regions already exist).
    ///   • «Список» — «+ регион», a pinned «Стереть» pseudo-target row (selectedRegionId = -1), and
    ///     one row per RegionData: colour swatch (cycles the palette), editable name (InputField),
    ///     click-row-to-select-as-paint-target, and a «✕» delete.
    /// Owns the region brush while this tab is active — mirrors EditorBrushPanel's ownership of the
    /// brush controller, but for BrushTool.Region: sets activeTool/selectedRegionId/brushModeActive on
    /// enable, turns brushModeActive off on disable (leaves activeTool/selectedRegionId as-is, same as
    /// EditorBrushPanel does for its own tool).
    /// </summary>
    public class RegionsPanel : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public BrushToolController brushController;
        public RegionManager regionManager;

        Font builtinFont;

        int genCount = 8;
        int genMinSize = 20;

        Text headerCountLabel;
        Transform listContent;

        int selectedId = -1; // -1 = «Стереть» (erase / unassign). Persists across tab enable/disable.
        readonly Dictionary<int, RowVisual> rowVisuals = new Dictionary<int, RowVisual>();
        readonly Dictionary<int, int> colorCycleIndex = new Dictionary<int, int>();
        const int ColorCycleSteps = 24; // palette (8) + enough golden-angle fallback steps for visible variety
        int addRegionCounter;

        struct RowVisual { public Image Bg; public Outline Outline; }

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void OnEnable()
        {
            // Вкладка "Регионы" стала активной — эта панель теперь владеет кистью (см. EditorBrushPanel
            // для того же паттерна с BrushTool.Elevation/Biome/...).
            if (brushController != null)
            {
                brushController.activeTool = BrushTool.Region;
                brushController.selectedRegionId = selectedId;
                brushController.brushModeActive = true;
            }
            RebuildList();
        }

        void OnDisable()
        {
            // Ушли с вкладки "Регионы" — гасим кисть, чтобы она не оставалась живой на других вкладках
            // (Карта/Редактор/Точки). activeTool/selectedRegionId намеренно НЕ сбрасываем (как и
            // EditorBrushPanel не сбрасывает activeTool на своих полях) - возврат на вкладку восстановит.
            if (brushController != null) brushController.brushModeActive = false;
        }

        // ── Actions: generate ────────────────────────────────────────────────────

        void OnGenerateClicked()
        {
            if (regionManager != null && regionManager.Regions.Count > 0)
            {
                ConfirmDialog.Show(builtinFont, "Заменить существующие регионы?",
                    "Текущие регионы и их привязка к клеткам будут удалены и пересозданы заново.",
                    confirmed => { if (confirmed) DoGenerate(); });
            }
            else
            {
                DoGenerate();
            }
        }

        void DoGenerate()
        {
            mapRenderer?.GenerateRegionsOnly(genCount, genMinSize);
            // Region ids restart at 0 after a regenerate - stale cycle indices from the previous
            // generation's ids would otherwise desync swatch recolor cycling from the new regions.
            colorCycleIndex.Clear();
            // Старый выбор кисти мог указывать на id, который перегенерация уже не воспроизводит
            // (ids пересобираются с нуля) — безопасный сброс на «Стереть».
            SelectRegion(-1);
            RebuildList();
        }

        // ── Actions: list ────────────────────────────────────────────────────────

        void OnAddRegionClicked()
        {
            if (regionManager == null) return;
            var r = regionManager.Add(GenerateUniqueRegionName(), regionManager.NextColor());
            mapRenderer?.RebuildBorders();
            mapRenderer?.UploadRegionColors(); // GPU "Регионы" fill (Task 6) - new region's color is in the table before it's ever painted
            mapRenderer?.RefreshRegionLabels(); // Task 7 - metadata list changed (no centroid yet until painted)
            mapRenderer?.NotifyDisplayChanged(); // Region-mode legend follows the newly added region
            SelectRegion(r.Id);
            RebuildList();
        }

        /// <summary>Deterministic fantasy name (same generator as continent/region-label naming),
        /// bumped into a key range past what GenerateRegionsOnly uses so a freshly-added region doesn't
        /// echo an existing one, plus the same bounded suffix-on-collision loop used there (see
        /// biome-matrix-branch-state memory: ContinentName's pool is finite, so the loop must be bounded
        /// by a growing suffix, not by retrying the generator).</summary>
        string GenerateUniqueRegionName()
        {
            var used = new HashSet<string>();
            if (regionManager != null)
                foreach (var r in regionManager.Regions) used.Add(r.Name);
            int seed = mapRenderer != null ? mapRenderer.seed : 0;
            int key = 100000 + addRegionCounter++;
            string name = RegionLabelNames.ContinentName(seed, key);
            string baseName = name;
            int suffix = 2;
            while (!used.Add(name)) name = baseName + " " + suffix++;
            return name;
        }

        void SelectRegion(int id)
        {
            selectedId = id;
            if (brushController != null) brushController.selectedRegionId = id;
            ApplySelectionHighlight(id);
        }

        void CycleRegionColor(int id, Image swatchImg)
        {
            if (regionManager == null) return;
            if (!colorCycleIndex.TryGetValue(id, out int idx)) idx = id;
            idx = (idx + 1) % ColorCycleSteps;
            colorCycleIndex[id] = idx;
            var c = RegionColorPalette.GetRegionColor(idx);
            regionManager.SetColor(id, c);
            if (swatchImg != null) swatchImg.color = c;
            mapRenderer?.RebuildBorders();
            mapRenderer?.RefreshAfterCellDataChange();
            mapRenderer?.UploadRegionColors(); // GPU "Регионы" fill (Task 6) - keep _RegionColor in sync with the recolor
            mapRenderer?.NotifyDisplayChanged(); // Region-mode legend follows the recolor
        }

        void DeleteRegionClicked(int id)
        {
            mapRenderer?.DeleteRegion(id);
            if (selectedId == id) SelectRegion(-1);
            RebuildList();
        }

        // ── List rebuild ─────────────────────────────────────────────────────────

        void RebuildList()
        {
            if (listContent == null) return;
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);
            rowVisuals.Clear();

            BuildEraserRow(listContent);
            if (regionManager != null)
                foreach (var r in regionManager.Regions)
                    BuildRow(r);

            ApplySelectionHighlight(selectedId);
            UpdateCountLabel();
        }

        void UpdateCountLabel()
        {
            int n = regionManager != null ? regionManager.Regions.Count : 0;
            if (headerCountLabel != null) headerCountLabel.text = n.ToString();
        }

        void ApplySelectionHighlight(int selected)
        {
            foreach (var kvp in rowVisuals)
            {
                bool on = kvp.Key == selected;
                kvp.Value.Bg.color = on ? ThemeService.Get(ThemeRole.AccentSoft) : new Color(0f, 0f, 0f, 0f);
                kvp.Value.Outline.effectColor = ThemeService.Get(ThemeRole.Accent);
                kvp.Value.Outline.enabled = on;
            }
        }

        // ── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("RegionsToolCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("RegionsPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.offsetMin = new Vector2(20f, 20f);
            // The 40px menu-bar term is gone from this top inset — the rect PaneChromeFrame confines this
            // canvas to is the pane's ContentArea, already below the menu bar AND below the tab strip. Full
            // reasoning: MapLayersPanel.cs:74.
            panelRect.offsetMax = new Vector2(20f + 262f, -(MapToolbarUI.BarHeightPixels + 20f));

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            UiShadow.Add(panelRect);

            BuildHeader(panelGO.transform);
            BuildGenerationSegment(panelGO.transform);
            AddSeparator(panelGO.transform);
            BuildBrushSegment(panelGO.transform);
            AddSeparator(panelGO.transform);
            BuildListSegment(panelGO.transform);
        }

        void BuildHeader(Transform t)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Регионы";
            title.font = builtinFont;
            title.fontSize = 14;
            title.fontStyle = FontStyle.Bold;
            ThemeService.Tag(title, ThemeRole.Txt);
            title.alignment = TextAnchor.MiddleLeft;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = new Vector2(-6f, 0f);

            var countGO = new GameObject("Count");
            countGO.transform.SetParent(rowGO.transform, false);
            headerCountLabel = countGO.AddComponent<Text>();
            headerCountLabel.text = "0";
            headerCountLabel.font = builtinFont;
            headerCountLabel.fontSize = 12;
            ThemeService.Tag(headerCountLabel, ThemeRole.Mut);
            headerCountLabel.alignment = TextAnchor.MiddleRight;
            var countRect = countGO.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(0.6f, 0f);
            countRect.anchorMax = new Vector2(1f, 1f);
            countRect.offsetMin = Vector2.zero;
            countRect.offsetMax = Vector2.zero;
        }

        // ── Segment 1: creation (always visible) ─────────────────────────────────

        void BuildGenerationSegment(Transform t)
        {
            AddCaption(t, "СОЗДАНИЕ");

            AddIntSliderRow(t, "Количество", 2, 40, genCount, v => genCount = v);
            AddIntSliderRow(t, "Мин. размер", 1, 200, genMinSize, v => genMinSize = v);

            AddWideButton(t, "Генерировать регионы", ThemeRole.Accent, OnGenerateClicked);
        }

        // ── Segment: brush (region-paint brush radius, shared BrushToolController) ─

        void BuildBrushSegment(Transform t)
        {
            AddCaption(t, "КИСТЬ");
            float initialRadius = brushController != null ? brushController.brushRadius : 42f;
            AddFloatSliderRow(t, "Размер кисти", 8f, 120f, initialRadius, v =>
            {
                if (brushController != null) brushController.brushRadius = v;
            });
        }

        // ── Segment 2: list ──────────────────────────────────────────────────────

        void BuildListSegment(Transform t)
        {
            AddCaption(t, "СПИСОК");
            AddWideButton(t, "+ регион", ThemeRole.Elev, OnAddRegionClicked);
            BuildList(t);
        }

        void BuildList(Transform t)
        {
            var scrollGO = new GameObject("ListScroll");
            scrollGO.transform.SetParent(t, false);
            var le = scrollGO.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 80f;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f); // прозрачный, но raycast-target для колеса
            viewportGO.AddComponent<RectMask2D>();
            var vpRect = viewportGO.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            scroll.viewport = vpRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var cvlg = contentGO.AddComponent<VerticalLayoutGroup>();
            cvlg.spacing = 4f;
            cvlg.childControlWidth = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cRect = contentGO.GetComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.pivot = new Vector2(0.5f, 1f);
            cRect.sizeDelta = Vector2.zero;
            scroll.content = cRect;

            listContent = contentGO.transform;
        }

        void BuildEraserRow(Transform parent)
        {
            var rowGO = new GameObject("Row_Eraser");
            rowGO.transform.SetParent(parent, false);
            var bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            var outline = rowGO.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.enabled = false;
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => SelectRegion(-1));
            rowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(rowGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "✕  Стереть (снять регион)";
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);

            rowVisuals[-1] = new RowVisual { Bg = bg, Outline = outline };
        }

        void BuildRow(RegionData r)
        {
            var rowGO = new GameObject($"Row_{r.Id}");
            rowGO.transform.SetParent(listContent, false);
            var bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            var outline = rowGO.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.enabled = false;
            var selectBtn = rowGO.AddComponent<Button>();
            selectBtn.targetGraphic = bg;
            int id = r.Id;
            // Клик по фону строки выбирает регион как цель кисти. Дочерние виджеты ниже (swatch/
            // поле имени/удаление) — свои собственные Selectable, поэтому клик по НИМ не всплывает
            // сюда (стандартный uGUI-баблинг IPointerClickHandler останавливается на первом предке,
            // который его реализует) — тот же паттерн, что в PoiToolPanel.BuildRow.
            selectBtn.onClick.AddListener(() => SelectRegion(id));
            rowGO.AddComponent<LayoutElement>().preferredHeight = 34f;

            // Цветовой образец: клик циклически меняет цвет региона по палитре (простейший приемлемый
            // recolor — см. брифинг задачи).
            var swatchGO = new GameObject("Swatch");
            swatchGO.transform.SetParent(rowGO.transform, false);
            var swatchImg = swatchGO.AddComponent<Image>();
            swatchImg.color = r.Color;
            var swatchOutline = swatchGO.AddComponent<Outline>();
            swatchOutline.effectColor = ThemeService.Get(ThemeRole.Border);
            swatchOutline.effectDistance = new Vector2(1f, -1f);
            var swatchBtn = swatchGO.AddComponent<Button>();
            swatchBtn.targetGraphic = swatchImg;
            swatchBtn.transition = Selectable.Transition.None; // сохраняем настоящий цвет образца (без hover-тона)
            swatchBtn.onClick.AddListener(() => CycleRegionColor(id, swatchImg));
            var swatchRect = swatchGO.GetComponent<RectTransform>();
            swatchRect.anchorMin = new Vector2(0f, 0.5f);
            swatchRect.anchorMax = new Vector2(0f, 0.5f);
            swatchRect.pivot = new Vector2(0f, 0.5f);
            swatchRect.anchoredPosition = new Vector2(6f, 0f);
            swatchRect.sizeDelta = new Vector2(20f, 20f);

            // Редактируемое имя.
            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(rowGO.transform, false);
            var nameBg = nameGO.AddComponent<Image>();
            nameBg.color = new Color(0f, 0f, 0f, 0f);
            var nameField = nameGO.AddComponent<InputField>();
            nameField.targetGraphic = nameBg;
            nameField.lineType = InputField.LineType.SingleLine;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(32f, 4f);
            nameRect.offsetMax = new Vector2(-30f, -4f);

            var nameTextGO = new GameObject("Text");
            nameTextGO.transform.SetParent(nameGO.transform, false);
            var nameText = nameTextGO.AddComponent<Text>();
            nameText.font = builtinFont;
            nameText.fontSize = 12;
            ThemeService.Tag(nameText, ThemeRole.Txt);
            nameText.supportRichText = false;
            var nameTextRect = nameTextGO.GetComponent<RectTransform>();
            nameTextRect.anchorMin = Vector2.zero;
            nameTextRect.anchorMax = Vector2.one;
            nameTextRect.offsetMin = new Vector2(4f, 0f);
            nameTextRect.offsetMax = new Vector2(-4f, 0f);
            nameField.textComponent = nameText;
            nameField.text = r.Name;
            nameField.onEndEdit.AddListener(v =>
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    regionManager?.SetName(id, v);
                    mapRenderer?.RefreshRegionLabels(); // Task 7 - map label text follows the rename
                    mapRenderer?.NotifyDisplayChanged(); // Region-mode legend follows the rename
                }
            });

            // «✕» удаление.
            var delGO = new GameObject("Delete");
            delGO.transform.SetParent(rowGO.transform, false);
            var delImg = delGO.AddComponent<Image>();
            delImg.color = new Color(0f, 0f, 0f, 0f);
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            delBtn.onClick.AddListener(() => DeleteRegionClicked(id));
            var delRect = delGO.GetComponent<RectTransform>();
            delRect.anchorMin = new Vector2(1f, 0.5f);
            delRect.anchorMax = new Vector2(1f, 0.5f);
            delRect.pivot = new Vector2(1f, 0.5f);
            delRect.anchoredPosition = new Vector2(-6f, 0f);
            delRect.sizeDelta = new Vector2(22f, 22f);

            var delTextGO = new GameObject("X");
            delTextGO.transform.SetParent(delGO.transform, false);
            var delText = delTextGO.AddComponent<Text>();
            delText.text = "✕";
            delText.font = builtinFont;
            delText.fontSize = 13;
            ThemeService.Tag(delText, ThemeRole.Danger);
            delText.alignment = TextAnchor.MiddleCenter;
            var delTextRect = delTextGO.GetComponent<RectTransform>();
            delTextRect.anchorMin = Vector2.zero;
            delTextRect.anchorMax = Vector2.one;
            delTextRect.sizeDelta = Vector2.zero;

            rowVisuals[id] = new RowVisual { Bg = bg, Outline = outline };
        }

        // ── Shared widget helpers (mirror PoiToolPanel/MapLayersPanel/EditorBrushPanel idioms) ─────

        void AddSeparator(Transform t)
        {
            var go = new GameObject("Separator");
            go.transform.SetParent(t, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Border);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
        }

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

        void AddWideButton(Transform parent, string label, ThemeRole role, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, role);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 28f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, role == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        /// <summary>Labeled integer slider row (label + value above, slider below) — same shape as
        /// EditorBrushPanel.BuildLabeledSlider/MapLayersPanel.AddSliderRow, but whole-number valued
        /// (Количество/Мин. размер are cell/region counts, not a 0..1 fraction or a px radius).</summary>
        void AddIntSliderRow(Transform parent, string label, int min, int max, int def, System.Action<int> onChanged)
        {
            var groupGO = new GameObject($"{label}Group");
            groupGO.transform.SetParent(parent, false);
            var gvlg = groupGO.AddComponent<VerticalLayoutGroup>();
            gvlg.spacing = 3f;
            gvlg.childControlWidth = true;
            gvlg.childForceExpandWidth = true;
            gvlg.childControlHeight = true;
            gvlg.childForceExpandHeight = false;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
            var valueLabel = valGO.AddComponent<Text>();
            valueLabel.font = builtinFont;
            valueLabel.fontSize = 11;
            ThemeService.Tag(valueLabel, ThemeRole.Accent);
            valueLabel.alignment = TextAnchor.MiddleRight;
            var valRect = valGO.GetComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.4f, 0f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            var sliderGO = new GameObject($"{label}Slider");
            sliderGO.transform.SetParent(groupGO.transform, false);
            var slider = BuildSlider(sliderGO, def, min, max);
            slider.wholeNumbers = true;
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 14f;

            void Refresh(float v) => valueLabel.text = Mathf.RoundToInt(v).ToString();
            Refresh(def);
            slider.onValueChanged.AddListener(v => { Refresh(v); onChanged?.Invoke(Mathf.RoundToInt(v)); });
        }

        /// <summary>Labeled float slider row — same shape/construction as AddIntSliderRow above, but for
        /// a continuous px value (brush radius) instead of a whole-number count. Value label formatting
        /// mirrors EditorBrushPanel.BuildLabeledSlider's non-percent case ("NN px").</summary>
        void AddFloatSliderRow(Transform parent, string label, float min, float max, float def, System.Action<float> onChanged)
        {
            var groupGO = new GameObject($"{label}Group");
            groupGO.transform.SetParent(parent, false);
            var gvlg = groupGO.AddComponent<VerticalLayoutGroup>();
            gvlg.spacing = 3f;
            gvlg.childControlWidth = true;
            gvlg.childForceExpandWidth = true;
            gvlg.childControlHeight = true;
            gvlg.childForceExpandHeight = false;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
            var valueLabel = valGO.AddComponent<Text>();
            valueLabel.font = builtinFont;
            valueLabel.fontSize = 11;
            ThemeService.Tag(valueLabel, ThemeRole.Accent);
            valueLabel.alignment = TextAnchor.MiddleRight;
            var valRect = valGO.GetComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.4f, 0f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            var sliderGO = new GameObject($"{label}Slider");
            sliderGO.transform.SetParent(groupGO.transform, false);
            var slider = BuildSlider(sliderGO, def, min, max);
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 14f;

            void Refresh(float v) => valueLabel.text = $"{Mathf.RoundToInt(v)} px";
            Refresh(def);
            slider.onValueChanged.AddListener(v => { Refresh(v); onChanged?.Invoke(v); });
        }

        /// <summary>Ported from EditorBrushPanel/MapLayersPanel.BuildSlider: standard Background/Fill/
        /// HandleArea/Handle slider construction with the same theming so it matches the rest of the app.</summary>
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
