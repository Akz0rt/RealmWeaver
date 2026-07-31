using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Точки" tab — POI panel (Screen D). Left, full-height column, two segments:
    ///   • «Создание» — always-visible generation controls (count spinner, generate, add-one
    ///     arm-placement, clear-all); no overflow menu.
    ///   • «Список» — search + filter chips + a scrollable row list (icon + name + "тип · регион").
    /// Two-way selection sync with the map via PoiManager events; hides the map legend while open,
    /// since the POI edit panel occupies the right side. Placement/marker logic lives in
    /// PoiManager/PoiInteractionController.
    /// </summary>
    public class PoiToolPanel : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;

        Font builtinFont;

        int genCount = 5;
        Text headerCountLabel;
        Text genCountLabel;
        InputField searchField;
        string searchText = "";
        PoiType? filterType = null;

        Transform listContent;
        GameObject armedHintGO;
        Image addBtnBg;
        Text addBtnLabel;
        MapLegendUI legend;

        readonly List<Chip> chips = new List<Chip>();
        readonly Dictionary<string, RowVisual> rowVisuals = new Dictionary<string, RowVisual>();

        class Chip { public PoiType? Type; public Image Bg; public Text Txt; }
        struct RowVisual { public Image Bg; public Outline Outline; }

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void OnEnable()
        {
            if (poiManager != null)
            {
                poiManager.OnPoisChanged += RebuildList;
                poiManager.OnSelectionChanged += HandleSelectionChanged;
                poiManager.OnPlacementArmedChanged += HandleArmedChanged;
            }
            // Прячем легенду, пока открыта вкладка точек (справа — панель редактирования).
            if (legend == null) legend = FindObjectOfType<MapLegendUI>();
            if (legend != null) legend.gameObject.SetActive(false);

            RebuildList();
            HandleArmedChanged(poiManager != null && poiManager.PlacementArmed);
        }

        void OnDisable()
        {
            if (poiManager != null)
            {
                poiManager.OnPoisChanged -= RebuildList;
                poiManager.OnSelectionChanged -= HandleSelectionChanged;
                poiManager.OnPlacementArmedChanged -= HandleArmedChanged;
                poiManager.DisarmPlacement(); // не оставляем взвод активным при уходе с вкладки
            }
            if (legend != null) legend.gameObject.SetActive(true);
        }

        // ── Event handlers ───────────────────────────────────────────────────────

        void HandleSelectionChanged(PoiData poi) => ApplyRowHighlight(poi?.Id);

        void HandleArmedChanged(bool armed)
        {
            if (addBtnLabel != null) addBtnLabel.text = armed ? "Отмена (Esc)" : "+ Добавить точку";
            if (addBtnBg != null) ThemeService.Tag(addBtnBg, armed ? ThemeRole.Accent : ThemeRole.Elev);
            if (addBtnLabel != null) ThemeService.Tag(addBtnLabel, armed ? ThemeRole.AccentInk : ThemeRole.Txt);
            if (armedHintGO != null) armedHintGO.SetActive(armed);
        }

        void OnSearchChanged(string value)
        {
            searchText = value;
            RebuildList();
        }

        void OnFilterChanged(PoiType? type)
        {
            filterType = type;
            foreach (var chip in chips)
            {
                bool on = chip.Type == type;
                ThemeService.Tag(chip.Bg, on ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(chip.Txt, on ? ThemeRole.AccentInk : ThemeRole.Txt);
            }
            RebuildList();
        }

        // ── List rebuild ─────────────────────────────────────────────────────────

        void RebuildList()
        {
            if (listContent == null) return;
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);
            rowVisuals.Clear();

            if (poiManager != null)
            {
                string q = (searchText ?? "").Trim().ToLowerInvariant();
                foreach (var poi in poiManager.GetAllPois())
                {
                    if (filterType.HasValue && poi.Type != filterType.Value) continue;
                    if (q.Length > 0 && !(poi.Name ?? "").ToLowerInvariant().Contains(q)) continue;
                    BuildRow(poi);
                }
                ApplyRowHighlight(poiManager.GetSelectedPoi()?.Id);
            }

            UpdateCountLabel();
        }

        void UpdateCountLabel()
        {
            int n = poiManager != null ? poiManager.GetAllPois().Count : 0;
            if (headerCountLabel != null) headerCountLabel.text = n.ToString();
        }

        void ApplyRowHighlight(string selectedId)
        {
            foreach (var kvp in rowVisuals)
            {
                bool on = kvp.Key == selectedId;
                kvp.Value.Bg.color = on ? ThemeService.Get(ThemeRole.AccentSoft) : new Color(0f, 0f, 0f, 0f);
                kvp.Value.Outline.effectColor = ThemeService.Get(ThemeRole.Accent);
                kvp.Value.Outline.enabled = on;
            }
        }

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiToolCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("PoiPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.offsetMin = new Vector2(20f, 20f);
            // The 40px menu-bar term is gone from this top inset — the pane's own top already excludes the
            // menu bar once PaneChromeFrame confines this canvas to it. Full reasoning: MapLayersPanel.cs:68.
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
            BuildSearch(panelGO.transform);
            BuildFilterChips(panelGO.transform);
            BuildList(panelGO.transform);
        }

        void BuildHeader(Transform t)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Точки интереса";
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

            // Количество: [-] N [+]
            var countRow = new GameObject("CountRow");
            countRow.transform.SetParent(t, false);
            countRow.AddComponent<LayoutElement>().preferredHeight = 22f;

            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(countRow.transform, false);
            var lbl = lblGO.AddComponent<Text>();
            lbl.text = "Количество";
            lbl.font = builtinFont;
            lbl.fontSize = 12;
            ThemeService.Tag(lbl, ThemeRole.Txt);
            lbl.alignment = TextAnchor.MiddleLeft;
            var lblRect = lblGO.GetComponent<RectTransform>();
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(0.6f, 1f);
            lblRect.offsetMin = Vector2.zero;
            lblRect.offsetMax = Vector2.zero;

            AddStepButton(countRow.transform, "−", new Vector2(-56f, 0f), () => { if (genCount > 0) genCount--; UpdateGenCountLabel(); });
            var gcGO = new GameObject("N");
            gcGO.transform.SetParent(countRow.transform, false);
            genCountLabel = gcGO.AddComponent<Text>();
            genCountLabel.text = genCount.ToString();
            genCountLabel.font = builtinFont;
            genCountLabel.fontSize = 12;
            ThemeService.Tag(genCountLabel, ThemeRole.Txt);
            genCountLabel.alignment = TextAnchor.MiddleCenter;
            var gcRect = gcGO.GetComponent<RectTransform>();
            gcRect.anchorMin = new Vector2(1f, 0.5f);
            gcRect.anchorMax = new Vector2(1f, 0.5f);
            gcRect.pivot = new Vector2(1f, 0.5f);
            gcRect.anchoredPosition = new Vector2(-26f, 0f);
            gcRect.sizeDelta = new Vector2(28f, 20f);
            AddStepButton(countRow.transform, "+", new Vector2(0f, 0f), () => { genCount++; UpdateGenCountLabel(); });

            AddWideButton(t, "Сгенерировать", ThemeRole.Accent, () => poiManager?.GenerateAll(genCount));

            // «Добавить точку» — взвод arm-then-click (кнопка становится «Отмена»).
            var addGO = new GameObject("AddButton");
            addGO.transform.SetParent(t, false);
            addBtnBg = addGO.AddComponent<Image>();
            ThemeService.Tag(addBtnBg, ThemeRole.Elev);
            addGO.AddComponent<Outline>().effectColor = ThemeService.Get(ThemeRole.Border);
            var addBtn = addGO.AddComponent<Button>();
            addBtn.targetGraphic = addBtnBg;
            addBtn.onClick.AddListener(() => poiManager?.TogglePlacement());
            addGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            var addTextGO = new GameObject("Text");
            addTextGO.transform.SetParent(addGO.transform, false);
            addBtnLabel = addTextGO.AddComponent<Text>();
            addBtnLabel.text = "+ Добавить точку";
            addBtnLabel.font = builtinFont;
            addBtnLabel.fontSize = 12;
            ThemeService.Tag(addBtnLabel, ThemeRole.Txt);
            addBtnLabel.alignment = TextAnchor.MiddleCenter;
            var atr = addTextGO.GetComponent<RectTransform>();
            atr.anchorMin = Vector2.zero;
            atr.anchorMax = Vector2.one;
            atr.sizeDelta = Vector2.zero;

            armedHintGO = new GameObject("ArmedHint");
            armedHintGO.transform.SetParent(t, false);
            var hint = armedHintGO.AddComponent<Text>();
            hint.text = "Кликните по карте, чтобы добавить";
            hint.font = builtinFont;
            hint.fontSize = 10;
            hint.fontStyle = FontStyle.Italic;
            ThemeService.Tag(hint, ThemeRole.Accent);
            hint.alignment = TextAnchor.MiddleCenter;
            armedHintGO.AddComponent<LayoutElement>().preferredHeight = 13f;
            armedHintGO.SetActive(false);

            AddWideButton(t, "Очистить все", ThemeRole.Danger, () => poiManager?.ClearAll());
        }

        // ── Segment 2: search + list ─────────────────────────────────────────────

        void BuildSearch(Transform t)
        {
            AddCaption(t, "СПИСОК");

            var go = new GameObject("Search");
            go.transform.SetParent(t, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2);
            searchField = go.AddComponent<InputField>();
            searchField.targetGraphic = bg;
            searchField.lineType = InputField.LineType.SingleLine;
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);
            searchField.textComponent = text;

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var ph = phGO.AddComponent<Text>();
            ph.text = "Поиск…";
            ph.font = builtinFont;
            ph.fontSize = 12;
            ph.fontStyle = FontStyle.Italic;
            ThemeService.Tag(ph, ThemeRole.Mut);
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.offsetMin = new Vector2(8f, 0f);
            phRect.offsetMax = new Vector2(-8f, 0f);
            searchField.placeholder = ph;

            searchField.onValueChanged.AddListener(OnSearchChanged);
        }

        void BuildFilterChips(Transform t)
        {
            // Строки чипов — прямые дети панели (без контейнера с ContentSizeFitter: тот внутри
            // управляемого VLG раздувается и создаёт огромные пустые интервалы).
            var row1 = MakeChipRow(t);
            AddChip(row1, "Все", null);
            AddChip(row1, "Города", PoiType.City);
            AddChip(row1, "Руины", PoiType.Ruin);

            var row2 = MakeChipRow(t);
            AddChip(row2, "Подземелья", PoiType.Dungeon);
            AddChip(row2, "Крепости", PoiType.Fortress);

            var row3 = MakeChipRow(t);
            AddChip(row3, "Башни", PoiType.Tower);
            AddChip(row3, "Храмы", PoiType.Temple);
            AddChip(row3, "Встречи", PoiType.Encounter);

            var row4 = MakeChipRow(t);
            AddChip(row4, "Порты", PoiType.Port);

            OnFilterChanged(null); // подсветить "Все"
        }

        Transform MakeChipRow(Transform parent)
        {
            var rowGO = new GameObject("ChipRow");
            rowGO.transform.SetParent(parent, false);
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false; // иначе строка сама «растягивается» и чипы становятся высокими
            hlg.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            return rowGO.transform;
        }

        void AddChip(Transform row, string label, PoiType? type)
        {
            var go = new GameObject($"Chip_{label}");
            go.transform.SetParent(row, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => OnFilterChanged(type));
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 3, 3);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;

            chips.Add(new Chip { Type = type, Bg = bg, Txt = text });
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

        void BuildRow(PoiData poi)
        {
            var rowGO = new GameObject($"Row_{poi.Id}");
            rowGO.transform.SetParent(listContent, false);
            var bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            var outline = rowGO.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.enabled = false;
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            string id = poi.Id;
            btn.onClick.AddListener(() => poiManager?.SelectPoi(id));
            rowGO.AddComponent<LayoutElement>().preferredHeight = 42f;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(rowGO.transform, false);
            var icon = iconGO.AddComponent<Image>();
            icon.sprite = PoiPlaceholderFactory.GetPlaceholder(poi.Type);
            icon.preserveAspect = true;
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(8f, 0f);
            iconRect.sizeDelta = new Vector2(26f, 26f);

            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(rowGO.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.text = string.IsNullOrEmpty(poi.Name) ? "(без имени)" : poi.Name;
            nameText.font = builtinFont;
            nameText.fontSize = 12;
            nameText.fontStyle = FontStyle.Bold;
            ThemeService.Tag(nameText, ThemeRole.Txt);
            nameText.alignment = TextAnchor.LowerLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(42f, 0f);
            nameRect.offsetMax = new Vector2(-8f, -3f);

            var subGO = new GameObject("Subtitle");
            subGO.transform.SetParent(rowGO.transform, false);
            var subText = subGO.AddComponent<Text>();
            subText.text = $"{TypeName(poi.Type)} · {RegionLabel(poi)}";
            subText.font = builtinFont;
            subText.fontSize = 10;
            ThemeService.Tag(subText, ThemeRole.Mut);
            subText.alignment = TextAnchor.UpperLeft;
            subText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var subRect = subGO.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0.5f);
            subRect.offsetMin = new Vector2(42f, 3f);
            subRect.offsetMax = new Vector2(-8f, 0f);

            rowVisuals[poi.Id] = new RowVisual { Bg = bg, Outline = outline };
        }

        // ── Shared helpers ───────────────────────────────────────────────────────

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

        void AddStepButton(Transform parent, string label, Vector2 anchoredPos, System.Action onClick)
        {
            var go = new GameObject($"Step_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(22f, 20f);
            rect.anchoredPosition = anchoredPos;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 14;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        void UpdateGenCountLabel()
        {
            if (genCountLabel != null) genCountLabel.text = genCount.ToString();
        }

        // ── Subtitle helpers ─────────────────────────────────────────────────────

        static string TypeName(PoiType t)
        {
            switch (t)
            {
                case PoiType.City: return "Город";
                case PoiType.Ruin: return "Руины";
                case PoiType.Dungeon: return "Подземелье";
                case PoiType.Fortress: return "Крепость";
                case PoiType.Tower: return "Башня";
                case PoiType.Temple: return "Храм";
                case PoiType.Encounter: return "Встреча";
                case PoiType.Port: return "Порт";
                default: return "Точка";
            }
        }

        static readonly string[] CompassLabels =
        {
            "Восточный", "Северо-восточный", "Северный", "Северо-западный",
            "Западный", "Юго-западный", "Южный", "Юго-восточный",
        };

        /// <summary>Real region name via the cell under the POI (RegionManager metadata). Falls back to
        /// the flavourful cardinal-by-position label (below) when the POI's cell has no region assigned
        /// yet, or the map has no nearest-cell lookup / region manager available.</summary>
        string RegionLabel(PoiData poi)
        {
            var mr = poiManager != null ? poiManager.mapRenderer : null;
            if (mr == null) return "Регион";

            var cell = mr.NearestLookup?.FindNearest(new System.Numerics.Vector2(poi.WorldPosition.X, poi.WorldPosition.Y));
            if (cell != null && cell.RegionId >= 0)
            {
                var region = mr.regionManager?.Get(cell.RegionId);
                if (region != null && !string.IsNullOrEmpty(region.Name)) return region.Name;
            }

            return CompassLabel(mr, poi);
        }

        /// <summary>Флейворное имя региона по положению точки относительно центра карты — фолбэк,
        /// когда клетка под точкой ещё не принадлежит ни одному реальному региону.</summary>
        string CompassLabel(WorldMapRenderer mr, PoiData poi)
        {
            float cx = mr.mapWidth * 0.5f, cy = mr.mapHeight * 0.5f;
            float dx = poi.WorldPosition.X - cx;
            float dy = poi.WorldPosition.Y - cy;
            float maxSide = Mathf.Max(mr.mapWidth, mr.mapHeight);
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < maxSide * 0.18f) return "Центральный регион";

            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            int idx = ((Mathf.RoundToInt(ang / 45f) % 8) + 8) % 8;
            return CompassLabels[idx] + " регион";
        }
    }
}
