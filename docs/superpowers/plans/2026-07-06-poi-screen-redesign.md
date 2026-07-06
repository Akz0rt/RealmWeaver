# POI Screen Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a real POI list panel (search/filter/rows, none exists today), an arm-then-click placement tool, and restyle `PoiEditPanel`'s Type selector, per `docs/superpowers/specs/2026-07-06-poi-screen-redesign-design.md`.

**Architecture:** `PoiManager` gains a position-taking `AddAt` method. `PoiPlaceholderFactory`'s glyphs are redrawn as primitive shapes shared by markers/list/edit-panel. A new `PoiListPanel` renders/filters/searches `PoiManager.GetAllPois()`. `PoiInteractionController` gains a small armed-placement state, checked on empty-space clicks. `PoiEditPanel`'s Type `Dropdown` becomes 4 icon buttons.

**Tech Stack:** C# runtime `UnityEngine.UI`, new Input System, `ThemeService.Tag(...)`.

## Global Constraints

- **Depends on** `docs/superpowers/plans/2026-07-06-editor-brush-redesign.md`'s Task 1 having already added `WorldMapRenderer.GetCellById(int) : VoronoiCell` — this plan's Task 1 reuses it. If that plan hasn't run yet, add `GetCellById` first (see that plan's Task 1, Step 1) before starting this plan's Task 1.
- **Depends on** `docs/superpowers/plans/2026-07-06-main-screen-redesign.md` having produced `PoiToolPanel.cs` (mechanical, unchanged extraction) — this plan does NOT modify `PoiToolPanel.cs` further; the new `PoiListPanel.cs` is a separate, additional panel (the design spec's "list panel" is new functionality, not a rework of the old count-spinner tab content, which stays reachable via the "⋯" overflow menu per the spec).
- Arm-then-click placement: clicking "+ Добавить точку" arms; placing (or Esc, or re-clicking the button) disarms. Never places on an existing marker (falls through to normal selection).
- The shared per-type icon (`PoiPlaceholderFactory.GetPlaceholder`) is used in exactly 3 places: on-map markers, list row icons, `PoiEditPanel` type buttons — one source of truth.
- `Unknown` keeps its existing "?" glyph — it has no mockup button and stays reachable only as the pre-selection default.
- No automated test runner — `[ContextMenu("Self-Test: ...")]` + manual Play-mode verification.

---

### Task 1: `PoiManager.AddAt` — position-taking placement

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiManager.cs`

**Interfaces:**
- Produces: `public void AddAt(int cellId, System.Numerics.Vector2 worldPosition)`. Consumed by Task 4 (`PoiInteractionController`).
- Consumes: `WorldMapRenderer.GetCellById(int) : VoronoiCell` (from the Editor-brush plan's Task 1 — see Global Constraints).

- [ ] **Step 1: Add `AddAt` to `PoiManager.cs`**

Add directly after the existing `AddOne()` method (`PoiManager.cs:84-100`):

```csharp
/// <summary>Adds a single typeless POI at an exact clicked position, owned by the given cell.
/// Used by the arm-then-click placement tool (PoiInteractionController) - unlike AddOne, this
/// places exactly where the user clicked rather than a random cell.</summary>
public void AddAt(int cellId, System.Numerics.Vector2 worldPosition)
{
    if (mapRenderer == null) return;
    var cell = mapRenderer.GetCellById(cellId);
    if (cell == null || cell.IsOcean) return; // same non-ocean constraint as AddOne/GenerateAll

    var poi = MakePoi(PoiType.Unknown, cell);
    poi.WorldPosition = worldPosition;
    pois.Add(poi);
    SpawnMarker(poi);
    OnPoisChanged?.Invoke();
}
```

- [ ] **Step 2: Manual verification**

Deferred to Task 4 (no UI hook exists yet to trigger this method).

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiManager.cs
git commit -m "feat: add PoiManager.AddAt for exact-position POI placement"
```

---

### Task 2: `PoiPlaceholderFactory.cs` — primitive icons replacing letter glyphs

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs`

**Interfaces:**
- Produces: same public API (`GetPlaceholder(PoiType) : Sprite`), unchanged signature — only the `glyphs` dictionary's content changes. Consumed by Task 3 (list rows), Task 5 (`PoiEditPanel` type buttons), and the existing on-map marker code (`PoiMarkerView`, already calling this factory — unaffected by this change beyond the visual glyph).

- [ ] **Step 1: Replace the `glyphs` dictionary**

In `Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs`, replace the `glyphs` dictionary (lines 26-78) with:

```csharp
// 5x7 pixel glyphs. glyphs[type][row, col], row 0 = top, true = white pixel.
// Redrawn 2026-07-06 as primitive shapes (Main-screen redesign, Screen D) - Unknown keeps its
// "?" mark (no mockup equivalent, still used as the pre-selection default); City/Ruin/Dungeon/
// Fortress became house/columns/arch/crenellated-wall per design_handoff_realmweaver_ui/README.md.
static readonly Dictionary<PoiType, bool[,]> glyphs = new Dictionary<PoiType, bool[,]>
{
    [PoiType.Unknown] = new bool[,]  // ?
    {
        { false, true,  true,  true,  false },
        { true,  false, false, false, true  },
        { false, false, false, false, true  },
        { false, false, true,  true,  false },
        { false, false, true,  false, false },
        { false, false, false, false, false },
        { false, false, true,  false, false },
    },
    [PoiType.City] = new bool[,]  // house with door cutout
    {
        { false, false, true,  false, false },
        { false, true,  true,  true,  false },
        { true,  true,  true,  true,  true  },
        { true,  false, false, false, true  },
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  true,  false, true,  true  },
    },
    [PoiType.Ruin] = new bool[,]  // 3 columns on a base
    {
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  false, true,  false, true  },
        { true,  true,  true,  true,  true  },
    },
    [PoiType.Dungeon] = new bool[,]  // arch
    {
        { false, true,  true,  true,  false },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
    },
    [PoiType.Fortress] = new bool[,]  // crenellated wall
    {
        { true,  false, true,  false, true  },
        { true,  true,  true,  true,  true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  false, false, false, true  },
        { true,  true,  true,  true,  true  },
    },
};
```

- [ ] **Step 2: Manual verification**

Play mode: run `PoiManager`'s existing "Self-Test: POI Placeholder Factory" context menu — expect `PASS` (this test only checks sprite dimensions, unaffected by glyph content, so it should already pass; it's a smoke test that the factory still produces valid sprites after the edit). Visually inspect a generated map's POI markers — City/Ruin/Dungeon/Fortress now show the new shapes instead of Cyrillic letters.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs
git commit -m "feat: replace POI marker letter glyphs with primitive shape icons"
```

---

### Task 3: `PoiListPanel.cs` — search, filter, rows, overflow menu

**Files:**
- Create: `Assets/WorldGen/Rendering/PoiListPanel.cs`

**Interfaces:**
- Consumes: `PoiManager.GetAllPois()`, `.OnPoisChanged`, `.OnSelectionChanged`, `.SelectPoi(string)`, `.GenerateAll(int)`, `.ClearAll()` (all existing), `WorldMapRenderer.GetCellById(int).RegionId` (region display), `PoiPlaceholderFactory.GetPlaceholder(PoiType)` (Task 2).
- Produces: `public bool PlacementArmed { get; private set; }`, `public void ToggleArmed()`, `public event Action OnArmedChanged` — consumed by Task 4 (`PoiInteractionController`).

- [ ] **Step 1: Create `PoiListPanel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Left-side POI list: search, type filter chips, rows synced with map selection, and a
    /// "+ Добавить точку" button that arms a click-to-place tool (PoiInteractionController
    /// checks PlacementArmed on empty-space clicks). Generate/Clear-all are relocated here from
    /// the old MapEditorPanel.BuildPoiTab into a small "⋯" overflow menu, unchanged behavior.
    /// </summary>
    public class PoiListPanel : MonoBehaviour
    {
        public PoiManager poiManager;
        public WorldMapRenderer mapRenderer;

        public event Action OnArmedChanged;
        public bool PlacementArmed { get; private set; }

        Font builtinFont;
        Transform rowContainer;
        Text countLabel;
        InputField searchField;
        PoiType? activeFilter; // null = "Все"
        Button armButton;
        Text armButtonLabel;
        int overflowGenerateCount = 5;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            if (poiManager != null)
            {
                poiManager.OnPoisChanged += RefreshRows;
                poiManager.OnSelectionChanged += _ => RefreshRowHighlights();
            }
            RefreshRows();
        }

        void OnDestroy()
        {
            if (poiManager != null) poiManager.OnPoisChanged -= RefreshRows;
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiListCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("PoiListPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.95f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -(20f + MapToolbarUI.BarHeightPixels + 40f));
            panelRect.offsetMin = new Vector2(0f, 0f);
            panelRect.sizeDelta = new Vector2(262f, panelRect.sizeDelta.y);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;

            BuildHeaderRow(panelGO.transform);
            BuildSearchField(panelGO.transform);
            BuildFilterChips(panelGO.transform);
            BuildRowScrollArea(panelGO.transform);
            BuildFooter(panelGO.transform);
        }

        void BuildHeaderRow(Transform parent)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Точки интереса";
            title.font = builtinFont;
            title.fontSize = 13;
            title.fontStyle = FontStyle.Bold;
            ThemeService.Tag(title, ThemeRole.Txt);
            titleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var countGO = new GameObject("Count");
            countGO.transform.SetParent(rowGO.transform, false);
            countLabel = countGO.AddComponent<Text>();
            countLabel.font = builtinFont;
            countLabel.fontSize = 12;
            ThemeService.Tag(countLabel, ThemeRole.Mut);
            countGO.AddComponent<LayoutElement>().preferredWidth = 24f;

            var overflowGO = new GameObject("Overflow");
            overflowGO.transform.SetParent(rowGO.transform, false);
            var overflowImg = overflowGO.AddComponent<Image>();
            ThemeService.Tag(overflowImg, ThemeRole.Elev);
            var overflowBtn = overflowGO.AddComponent<Button>();
            overflowBtn.targetGraphic = overflowImg;
            overflowBtn.onClick.AddListener(ToggleOverflowMenu);
            overflowGO.AddComponent<LayoutElement>().preferredWidth = 24f;

            var overflowTextGO = new GameObject("Text");
            overflowTextGO.transform.SetParent(overflowGO.transform, false);
            var overflowText = overflowTextGO.AddComponent<Text>();
            overflowText.text = "⋯";
            overflowText.font = builtinFont;
            overflowText.fontSize = 16;
            overflowText.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(overflowText, ThemeRole.Txt);
            var overflowTextRect = overflowTextGO.GetComponent<RectTransform>();
            overflowTextRect.anchorMin = Vector2.zero;
            overflowTextRect.anchorMax = Vector2.one;
            overflowTextRect.sizeDelta = Vector2.zero;
        }

        GameObject overflowMenuGO;

        void ToggleOverflowMenu()
        {
            if (overflowMenuGO != null) { Destroy(overflowMenuGO); overflowMenuGO = null; return; }

            overflowMenuGO = new GameObject("OverflowMenu");
            overflowMenuGO.transform.SetParent(transform.GetChild(0).Find("PoiListPanel"), false);
            var img = overflowMenuGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var rect = overflowMenuGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-10f, -40f);
            rect.sizeDelta = new Vector2(200f, 84f);

            var layout = overflowMenuGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;

            AddOverflowRow(overflowMenuGO.transform, $"Сгенерировать ({overflowGenerateCount})", () =>
            {
                poiManager.GenerateAll(overflowGenerateCount);
                ToggleOverflowMenu();
            });
            AddOverflowRow(overflowMenuGO.transform, "Очистить все", () =>
            {
                poiManager.ClearAll();
                ToggleOverflowMenu();
            });
        }

        void AddOverflowRow(Transform parent, string label, Action onClick)
        {
            var go = new GameObject($"Row_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.sizeDelta = Vector2.zero;
        }

        void BuildSearchField(Transform parent)
        {
            var fieldGO = new GameObject("SearchField");
            fieldGO.transform.SetParent(parent, false);
            fieldGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            var img = fieldGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            searchField = fieldGO.AddComponent<InputField>();

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(fieldGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
            searchField.textComponent = text;

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(fieldGO.transform, false);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "Поиск…";
            placeholder.font = builtinFont;
            placeholder.fontSize = 12;
            placeholder.fontStyle = FontStyle.Italic;
            ThemeService.Tag(placeholder, ThemeRole.Mut);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8f, 4f);
            placeholderRect.offsetMax = new Vector2(-8f, -4f);
            searchField.placeholder = placeholder;

            searchField.onValueChanged.AddListener(_ => RefreshRows());
        }

        Button[] filterButtons;
        PoiType?[] filterValues = { null, PoiType.City, PoiType.Ruin, PoiType.Dungeon, PoiType.Fortress };

        void BuildFilterChips(Transform parent)
        {
            var rowGO = new GameObject("FilterChips");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            string[] labels = { "Все", "Города", "Руины", "Подземелья", "Крепости" };
            filterButtons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var btnGO = new GameObject($"Filter_{labels[i]}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => { activeFilter = filterValues[captured]; RefreshFilterColors(); RefreshRows(); });
                filterButtons[i] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = labels[i];
                text.font = builtinFont;
                text.fontSize = 10;
                text.alignment = TextAnchor.MiddleCenter;
                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }
            RefreshFilterColors();
        }

        void RefreshFilterColors()
        {
            for (int i = 0; i < filterButtons.Length; i++)
            {
                bool active = filterValues[i] == activeFilter;
                ThemeService.Tag(filterButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(filterButtons[i].GetComponentInChildren<Text>(), active ? ThemeRole.AccentInk : ThemeRole.Txt);
            }
        }

        void BuildRowScrollArea(Transform parent)
        {
            var scrollGO = new GameObject("ScrollArea");
            scrollGO.transform.SetParent(parent, false);
            scrollGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 3f;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childControlHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            rowContainer = contentGO.transform;
        }

        void BuildFooter(Transform parent)
        {
            var btnGO = new GameObject("AddButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Bg);
            var outline = btnGO.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Border);
            outline.effectDistance = new Vector2(1f, -1f);
            armButton = btnGO.AddComponent<Button>();
            armButton.targetGraphic = img;
            armButton.onClick.AddListener(ToggleArmed);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            armButtonLabel = textGO.AddComponent<Text>();
            armButtonLabel.text = "+ Добавить точку";
            armButtonLabel.font = builtinFont;
            armButtonLabel.fontSize = 12;
            armButtonLabel.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(armButtonLabel, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        public void ToggleArmed()
        {
            PlacementArmed = !PlacementArmed;
            armButtonLabel.text = PlacementArmed ? "Кликните по карте…" : "+ Добавить точку";
            ThemeService.Tag(armButtonLabel, PlacementArmed ? ThemeRole.AccentInk : ThemeRole.Txt);
            ThemeService.Tag(armButton.targetGraphic as Image, PlacementArmed ? ThemeRole.Accent : ThemeRole.Bg);
            OnArmedChanged?.Invoke();
        }

        /// <summary>Called by PoiInteractionController after successfully placing a POI, or on Esc.</summary>
        public void Disarm()
        {
            if (!PlacementArmed) return;
            ToggleArmed();
        }

        void RefreshRows()
        {
            foreach (Transform child in rowContainer) Destroy(child.gameObject);
            if (poiManager == null) return;

            var all = poiManager.GetAllPois();
            countLabel.text = all.Count.ToString();

            string query = searchField != null ? searchField.text : "";
            var filtered = all.Where(p =>
                (activeFilter == null || p.Type == activeFilter) &&
                (string.IsNullOrEmpty(query) || p.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var poi in filtered)
                BuildRow(poi);
        }

        Dictionary<string, Image> rowHighlights = new Dictionary<string, Image>();

        void BuildRow(PoiData poi)
        {
            var rowGO = new GameObject($"Row_{poi.Id}");
            rowGO.transform.SetParent(rowContainer, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 34f;
            var rowImg = rowGO.AddComponent<Image>();
            ThemeService.Tag(rowImg, ThemeRole.Panel2);
            rowHighlights[poi.Id] = rowImg;
            var rowBtn = rowGO.AddComponent<Button>();
            rowBtn.targetGraphic = rowImg;
            rowBtn.onClick.AddListener(() => poiManager.SelectPoi(poi.Id));

            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 6f;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(rowGO.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = poi.CustomIconBytes != null ? null : PoiPlaceholderFactory.GetPlaceholder(poi.Type);
            iconGO.AddComponent<LayoutElement>().preferredWidth = 26f;
            iconGO.GetComponent<LayoutElement>().preferredHeight = 26f;

            var textColGO = new GameObject("TextCol");
            textColGO.transform.SetParent(rowGO.transform, false);
            textColGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var textColLayout = textColGO.AddComponent<VerticalLayoutGroup>();
            textColLayout.childControlWidth = true;
            textColLayout.childForceExpandWidth = true;

            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(textColGO.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.text = poi.Name;
            nameText.font = builtinFont;
            nameText.fontSize = 12;
            ThemeService.Tag(nameText, ThemeRole.Txt);
            nameGO.AddComponent<LayoutElement>().preferredHeight = 16f;

            var subGO = new GameObject("Subtitle");
            subGO.transform.SetParent(textColGO.transform, false);
            var subText = subGO.AddComponent<Text>();
            int regionId = mapRenderer?.GetCellById(poi.OwnerCellId)?.RegionId ?? -1;
            subText.text = $"{TypeLabel(poi.Type)} · Регион {regionId}";
            subText.font = builtinFont;
            subText.fontSize = 10;
            ThemeService.Tag(subText, ThemeRole.Mut);
            subGO.AddComponent<LayoutElement>().preferredHeight = 14f;
        }

        static string TypeLabel(PoiType type) => type switch
        {
            PoiType.City => "Город",
            PoiType.Ruin => "Руины",
            PoiType.Dungeon => "Подземелье",
            PoiType.Fortress => "Крепость",
            _ => "Неизвестно"
        };

        void RefreshRowHighlights()
        {
            string selectedId = poiManager.GetSelectedPoi()?.Id;
            foreach (var kvp in rowHighlights)
                ThemeService.Tag(kvp.Value, kvp.Key == selectedId ? ThemeRole.AccentSoft : ThemeRole.Panel2);
        }
    }
}
```

- [ ] **Step 2: Manual verification**

Deferred to Task 4 (arm/disarm needs `PoiInteractionController` wired to be end-to-end testable). Confirm on its own first: rows populate from generated POIs, search narrows by name, filter chips narrow by type, row click selects the map marker (confirm via `PoiEditPanel` opening), "⋯" menu's generate/clear-all still work.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiListPanel.cs Assets/WorldGen/Rendering/PoiListPanel.cs.meta
git commit -m "feat: add PoiListPanel (search, filter, rows, relocated generate/clear-all)"
```

---

### Task 4: `PoiInteractionController.cs` — arm-then-click placement

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiInteractionController.cs`

**Interfaces:**
- Consumes: `PoiListPanel.PlacementArmed`, `.Disarm()` (Task 3), `PoiManager.AddAt(int, System.Numerics.Vector2)` (Task 1).

- [ ] **Step 1: Add a `PoiListPanel` reference and Esc handling**

Add a public field near the other `[Header("Dependencies")]` fields:

```csharp
public PoiListPanel listPanel;
```

Add Esc-to-disarm to `Update()` (right after the existing `if (Mouse.current == null) return;` line):

```csharp
if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && listPanel != null && listPanel.PlacementArmed)
    listPanel.Disarm();
```

(This requires `using UnityEngine.InputSystem;`'s `Keyboard.current` — already imported in this file.)

- [ ] **Step 2: Handle armed placement in `OnPress`**

Replace the existing `OnPress()`'s `else` branch (currently just `poiManager.DeselectAll();`, around line 84-87) with:

```csharp
else
{
    if (listPanel != null && listPanel.PlacementArmed)
    {
        int cellId = GetCellIdAt(mousePos);
        if (cellId >= 0)
        {
            poiManager.AddAt(cellId, worldXZ);
            listPanel.Disarm();
            InputConsumedThisFrame = true;
            return;
        }
    }
    poiManager.DeselectAll();
}
```

(`worldXZ` and `mousePos` are already computed above this point in the existing `OnPress()` body — reuse them, don't recompute.)

- [ ] **Step 3: Manual verification**

Play mode: click "+ Добавить точку" (button highlights, label changes to "Кликните по карте…"), click empty map space → a new POI appears there and the tool disarms; click an existing marker while armed → falls through to normal selection instead of placing (verify no duplicate POI is created); press Esc while armed → disarms without placing; click the button again while armed → disarms without placing.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiInteractionController.cs
git commit -m "feat: wire arm-then-click POI placement into PoiInteractionController"
```

---

### Task 5: `PoiEditPanel.cs` — Type icon buttons, reposition

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs`

**Interfaces:**
- Consumes: `PoiPlaceholderFactory.GetPlaceholder(PoiType)` (Task 2), `poiManager.UpdatePoiType(string, PoiType)` (existing).

- [ ] **Step 1: Read the current Type dropdown code**

Open `Assets/WorldGen/Rendering/PoiEditPanel.cs`, read the current Type `Dropdown` construction (per the design spec's current-state notes: options "Неизвестно / Город / Руины / Подземелье / Крепость", calling `poiManager.UpdatePoiType`) to get its exact surrounding code (what it's parented to, what runs before/after it) before replacing it.

- [ ] **Step 2: Replace the Type dropdown with 4 icon buttons**

Replace the dropdown construction with:

```csharp
void BuildTypeButtons(Transform parent)
{
    var rowGO = new GameObject("TypeRow");
    rowGO.transform.SetParent(parent, false);
    rowGO.AddComponent<LayoutElement>().preferredHeight = 44f;
    var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 4f;
    layout.childControlWidth = true;
    layout.childForceExpandWidth = true;

    var types = new[] { PoiType.City, PoiType.Ruin, PoiType.Dungeon, PoiType.Fortress };
    typeButtons = new Button[types.Length];

    for (int i = 0; i < types.Length; i++)
    {
        int captured = i;
        var btnGO = new GameObject($"Type_{types[i]}");
        btnGO.transform.SetParent(rowGO.transform, false);
        var img = btnGO.AddComponent<Image>();
        ThemeService.Tag(img, ThemeRole.Elev);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            var poi = poiManager.GetSelectedPoi();
            if (poi == null) return;
            poiManager.UpdatePoiType(poi.Id, types[captured]);
            RefreshTypeButtonSelection(types[captured]);
        });
        typeButtons[i] = btn;

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(btnGO.transform, false);
        var iconImg = iconGO.AddComponent<Image>();
        iconImg.sprite = PoiPlaceholderFactory.GetPlaceholder(types[i]);
        var iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(28f, 28f);
    }
}

void RefreshTypeButtonSelection(PoiType selected)
{
    var types = new[] { PoiType.City, PoiType.Ruin, PoiType.Dungeon, PoiType.Fortress };
    for (int i = 0; i < typeButtons.Length; i++)
    {
        var outline = typeButtons[i].GetComponent<Outline>();
        bool isSelected = types[i] == selected;
        if (isSelected && outline == null)
        {
            outline = typeButtons[i].gameObject.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(2f, -2f);
        }
        else if (!isSelected && outline != null)
        {
            Destroy(outline);
        }
    }
}
```

Add the field `Button[] typeButtons;` near the class's other private fields. Call `BuildTypeButtons(...)` where the old dropdown construction used to be called, and call `RefreshTypeButtonSelection(poi.Type)` wherever the panel refreshes its fields for a newly-selected POI (the existing method that populates `poiNameField.text`/`poiDescField.text`/etc. on selection change — add the same call there).

- [ ] **Step 3: Reposition the panel to a fixed 308px right anchor**

Find the panel's current anchor code (chained under `MapLegendUI`'s bottom edge, per the design spec's current-state notes — `RepositionUnderLegend`/`LateUpdate` logic). Remove that chaining logic entirely; replace with a fixed anchor:

```csharp
panelRect.anchorMin = new Vector2(1f, 1f);
panelRect.anchorMax = new Vector2(1f, 1f);
panelRect.pivot = new Vector2(1f, 1f);
panelRect.anchoredPosition = new Vector2(-20f, -(20f + MapToolbarUI.BarHeightPixels + 40f));
panelRect.sizeDelta = new Vector2(308f, panelRect.sizeDelta.y);
```

Delete the now-unused `RepositionUnderLegend`/legend-chasing `LateUpdate` code and the `gapBelowLegend` field, since the panel no longer depends on the Legend's position (the Legend itself moved to bottom-left in the Main-screen shell plan, decoupling these two panels entirely).

- [ ] **Step 4: Manual verification**

Play mode: select a POI, confirm the 4 type icon buttons show the correct shared icons, clicking one updates the POI's type (marker + list row icon update too, confirming the shared `PoiPlaceholderFactory` source), the panel sits fixed at the right edge regardless of Legend state.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiEditPanel.cs
git commit -m "feat: replace PoiEditPanel Type dropdown with icon buttons, fix panel to right-side anchor"
```

---

### Task 6: Scene wiring

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (via a temporary batchmode Editor script, per this project's established convention)

- [ ] **Step 1: Wire `PoiListPanel` and cross-references**

Following the same convention as the Main-screen shell plan's Task 5: create a temporary `Assets/Editor/TempSceneBootstrap_PoiScreen.cs`, run it once to add a `PoiListPanel` root GameObject (wiring `poiManager`/`mapRenderer`), assign `PoiInteractionController.listPanel` to it, then delete the temporary script.

- [ ] **Step 2: Verify compile**

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -projectPath "D:\D&D" -quit -logFile -
```

Expect exit code 0, no `error CS` lines.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: wire PoiListPanel into scene"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1 (placement API), Task 2 (shared icons), Task 3 (list/search/filter/overflow), Task 4 (arm-then-click), Task 5 (edit-panel type buttons + reposition), Task 6 (scene wiring) — all in-scope spec items covered.
- **Placeholder scan:** "read the current X code before replacing" instructions point at specific, named existing behavior (confirmed via this session's code exploration) — not vague TBDs.
- **Type consistency:** `PoiListPanel.PlacementArmed`/`.Disarm()` defined in Task 3, consumed with matching names in Task 4. `PoiManager.AddAt(int, System.Numerics.Vector2)` defined in Task 1, called with matching signature in Task 4.
