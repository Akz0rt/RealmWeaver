# Draggable Panel Splits Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user drag the map/notes screen split and the notes sidebar width by hand, with both sizes persisted across sessions via `PlayerPrefs`.

**Architecture:** A shared `DraggableDivider` component reports only a raw screen-space drag delta and a drag-end callback — it has no idea what it's resizing. `NotesLayoutController.SplitFraction` changes from `const` to a lazily-`PlayerPrefs`-initialized static property with a change event, so `MapLegendUI`/`PoiEditPanel` (which read it once at their own `Awake()`) can also subscribe and update live. `NotesTreeSidebar.ExpandedWidth` becomes an instance field with the same PlayerPrefs/clamp pattern, simpler since only `NotesTreeSidebar` itself consumes it. Both dividers are positioned so they extend *away* from the other segment (into territory with no competing UI on top) rather than straddling the boundary symmetrically — a UI element created later always wins raycasts over one created earlier at the same screen position, and both `RightColumn` and the sidebar's own content are created after these dividers.

**Tech Stack:** Unity 6000.3.2f1, Built-in Render Pipeline, legacy `UnityEngine.UI` (no TextMeshPro), new Input System (`UnityEngine.InputSystem`), code-only UI construction, `PlayerPrefs` (first use in this project).

## Global Constraints

- No automated Unity test runner exists in this project. Verification is via the codebase's established `[ContextMenu("Self-Test: ...")]` method pattern plus manual Play-mode testing performed by the user — the implementer has no direct Unity Editor access.
- `PlayerPrefs` is being introduced to this project for the first time by this plan — no prior conventions to follow beyond standard Unity usage.
- Any divider must stay entirely within a region that has no other raycastable UI created *after* it at the same screen position — see Task 3/4's placement comments for the specific reasoning in each case.
- Out of scope (do not implement): custom OS cursor changes on hover, any other panel becoming resizable, any settings/preferences UI exposing these values outside of dragging/double-clicking the dividers themselves.

---

### Task 1: Housekeeping — commit orphaned `.meta` files, rename `DoubleClickToRename` → `DoubleClickHandler`

**Files:**
- Add (already present on disk, untracked): `Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs.meta`
- Rename: `Assets/WorldGen/Notes/Rendering/DoubleClickToRename.cs` → `Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs` (and its `.meta`)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` (one usage site)

**Interfaces:**
- Produces: `DoubleClickHandler : MonoBehaviour, IPointerClickHandler` with public `System.Action OnDoubleClick` — identical to the old `DoubleClickToRename`, just renamed now that it has a second, non-rename use (the dividers' reset-to-default). Tasks 3 and 4 depend on this name.

- [ ] **Step 1: Commit the pre-existing untracked `.meta` file**

Unity generates a `.meta` file per script the first time the Editor imports it; `ConfirmDialog.cs.meta` already exists on disk (Unity generated it during the last Play-mode session) but was never committed. Run:

```bash
git status --short Assets/WorldGen/Notes/Rendering/
```

Expected: `ConfirmDialog.cs.meta` and `DoubleClickToRename.cs.meta` both show as untracked (`??`).

```bash
git add Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs.meta
git commit -m "chore: add missing .meta file for ConfirmDialog"
```

- [ ] **Step 2: Rename the file and its `.meta`, and the class inside**

```bash
git mv Assets/WorldGen/Notes/Rendering/DoubleClickToRename.cs Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs
mv Assets/WorldGen/Notes/Rendering/DoubleClickToRename.cs.meta Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs.meta
```

Open `Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs` and replace its entire contents:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick. Used by NotesTreeSidebar for
    /// inline-rename mode on group/page rows, and by DraggableDivider-based UI for
    /// reset-to-default.
    /// </summary>
    public class DoubleClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnDoubleClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 2)
                OnDoubleClick?.Invoke();
        }
    }
}
```

- [ ] **Step 3: Update `NotesTreeSidebar.cs`'s usage**

Run:
```bash
grep -n "DoubleClickToRename" "Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs"
```
Expected: one match, `var doubleClick = clickCatcherGO.AddComponent<DoubleClickToRename>();`

Open `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` and replace that line:

```csharp
            var doubleClick = clickCatcherGO.AddComponent<DoubleClickToRename>();
```

with:

```csharp
            var doubleClick = clickCatcherGO.AddComponent<DoubleClickHandler>();
```

- [ ] **Step 4: Verify no leftover references to the old name**

Run:
```bash
grep -rn "DoubleClickToRename" Assets/
```
Expected: no matches.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs Assets/WorldGen/Notes/Rendering/DoubleClickHandler.cs.meta Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "refactor: rename DoubleClickToRename to DoubleClickHandler"
```

---

### Task 2: Shared `DraggableDivider` component

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/DraggableDivider.cs`

**Interfaces:**
- Produces: `DraggableDivider.Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float width) → DraggableDivider`, with public `System.Action<float> OnDragDeltaX` (raw `PointerEventData.delta.x` per drag frame) and `System.Action OnDragEnd` (fires once when the drag gesture ends). Tasks 3 and 4 depend on this exact signature and these two callback names.

- [ ] **Step 1: Create `DraggableDivider.cs`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Thin draggable bar used by both the map/notes split and the notes sidebar width.
    /// Reports only a raw screen-space delta.x per drag frame and a one-shot "drag ended"
    /// callback — it has zero awareness of fractions, pixel widths, or what it's resizing;
    /// each caller (NotesLayoutController, NotesTreeSidebar) interprets the delta its own way.
    /// Highlights on hover; stays fully transparent otherwise.
    /// </summary>
    public class DraggableDivider : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<float> OnDragDeltaX;
        public System.Action OnDragEnd;

        static readonly Color IdleColor = Color.clear;
        static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.25f);

        Image image;

        /// <summary>Builds the divider GameObject under `parent`, anchored at the given point
        /// (anchorMin == anchorMax) with `pivot` controlling which side of that point the bar
        /// extends into. Callers must pick a pivot that keeps the bar entirely within a region
        /// with no other raycastable UI created after it at the same screen position — a UI
        /// sibling created later always wins raycasts over one created earlier there. `ignoreLayout`
        /// is set so the parent's own LayoutGroup (if any) doesn't try to reposition/resize this
        /// bar itself.</summary>
        public static DraggableDivider Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float width)
        {
            var go = new GameObject("DraggableDivider");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var img = go.AddComponent<Image>();
            img.color = IdleColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;

            var divider = go.AddComponent<DraggableDivider>();
            divider.image = img;
            return divider;
        }

        public void OnDrag(PointerEventData eventData) => OnDragDeltaX?.Invoke(eventData.delta.x);

        public void OnEndDrag(PointerEventData eventData) => OnDragEnd?.Invoke();

        public void OnPointerEnter(PointerEventData eventData) => image.color = HoverColor;

        public void OnPointerExit(PointerEventData eventData) => image.color = IdleColor;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/DraggableDivider.cs
git commit -m "feat: add shared DraggableDivider component"
```

---

### Task 3: Map/notes split becomes runtime-mutable and draggable

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs` (full rewrite)
- Modify: `Assets/WorldGen/Rendering/MapLegendUI.cs:38-41` (Awake, plus new methods)
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs:53-59` (Awake, plus new methods)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs:61-64`

**Interfaces:**
- Consumes: `DraggableDivider.Create(...)` + `OnDragDeltaX`/`OnDragEnd` (Task 2), `DoubleClickHandler` + `OnDoubleClick` (Task 1).
- Produces: `NotesLayoutController.SplitFraction` (unchanged name, now a `static` property instead of `const`, same read syntax `NotesLayoutController.SplitFraction`), `NotesLayoutController.OnSplitFractionChanged` (`static event Action<float>`), `NotesLayoutController.SetSplitFraction(float)`, `NotesLayoutController.SaveSplitFraction()`, `NotesLayoutController.BuildDivider()` (instance method, called once by `NotesRootBuilder`).

- [ ] **Step 1: Replace the entire contents of `NotesLayoutController.cs`**

```csharp
using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Splits the screen between the map area (left) and the notes area (right) using
    /// RectTransform anchors, so both regions rescale proportionally on window resize and
    /// never overlap. The split is user-draggable (via a DraggableDivider straddling
    /// notesAreaRoot's left edge, extending into the map's side so it never competes for
    /// raycasts with the sidebar) and persisted across sessions in PlayerPrefs.
    /// </summary>
    public class NotesLayoutController : MonoBehaviour
    {
        const string PrefsKey = "NotesLayout.SplitFraction";
        const float DefaultSplitFraction = 2f / 3f;
        public const float MinSplitFraction = 0.3f;
        public const float MaxSplitFraction = 0.85f;
        const float DividerWidth = 8f;

        /// <summary>Single source of truth for the map/notes screen split fraction.
        /// MapLegendUI and PoiEditPanel read this directly instead of each declaring their own
        /// copy, which is what let them drift out of sync before this class existed. Lazily
        /// initialized from PlayerPrefs (falling back to DefaultSplitFraction) the first time
        /// any code touches this property — a static property's initializer resolves on first
        /// access regardless of which GameObject's Awake() runs first, the same ordering
        /// guarantee a plain const used to provide (see
        /// docs/superpowers/specs/2026-07-03-map-notes-split-single-source-design.md), while
        /// still allowing later mutation (which a const could never do).</summary>
        public static float SplitFraction { get; private set; } = PlayerPrefs.GetFloat(PrefsKey, DefaultSplitFraction);

        /// <summary>Fires whenever SplitFraction changes (including live during a drag) so
        /// panels anchored to the split (MapLegendUI, PoiEditPanel) can update instead of only
        /// reading the value once at their own Awake().</summary>
        public static event System.Action<float> OnSplitFractionChanged;

        public static void SetSplitFraction(float value)
        {
            value = Mathf.Clamp(value, MinSplitFraction, MaxSplitFraction);
            if (Mathf.Approximately(value, SplitFraction)) return;
            SplitFraction = value;
            OnSplitFractionChanged?.Invoke(value);
        }

        /// <summary>Writes the current SplitFraction to PlayerPrefs. Called on drag-end and on
        /// double-click reset, NOT on every intermediate drag frame — SetSplitFraction alone
        /// already applies the value live via OnSplitFractionChanged.</summary>
        public static void SaveSplitFraction() => PlayerPrefs.SetFloat(PrefsKey, SplitFraction);

        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        void Awake()
        {
            OnSplitFractionChanged += _ => Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(SplitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, SplitFraction, 1f);
        }

        /// <summary>Builds the draggable divider. Called once by NotesRootBuilder right after
        /// notesAreaRoot is assigned — deliberately NOT called from Apply() itself, since
        /// Apply() re-runs on every SplitFraction change (including every drag frame via
        /// OnSplitFractionChanged), which would otherwise spawn a new divider GameObject every
        /// single frame while dragging.</summary>
        public void BuildDivider()
        {
            if (notesAreaRoot == null) return;

            // Anchored at notesAreaRoot's own left edge, pivot=(1,0.5) makes the bar extend
            // LEFTWARD — into the map's 3D-camera-viewport area, which has no UI raycast
            // targets at all — rather than straddling into notesAreaRoot itself, where the
            // sidebar (created later, as notesAreaRoot's first child) would win any raycast
            // in the overlapping region.
            var divider = DraggableDivider.Create(notesAreaRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.5f), DividerWidth);
            divider.OnDragDeltaX += dx => SetSplitFraction(SplitFraction + dx / Screen.width);
            divider.OnDragEnd += SaveSplitFraction;

            var doubleClick = divider.gameObject.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () =>
            {
                SetSplitFraction(DefaultSplitFraction);
                SaveSplitFraction();
            };
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Layout — Split Fraction Clamp")]
        public void SelfTestSplitFractionClamp()
        {
            float original = SplitFraction;
            bool eventFired = false;
            System.Action<float> handler = _ => eventFired = true;
            OnSplitFractionChanged += handler;

            SetSplitFraction(0.1f);
            bool clampedLow = Mathf.Approximately(SplitFraction, MinSplitFraction);

            SetSplitFraction(0.99f);
            bool clampedHigh = Mathf.Approximately(SplitFraction, MaxSplitFraction);

            OnSplitFractionChanged -= handler;
            SetSplitFraction(original);

            bool ok = clampedLow && clampedHigh && eventFired;
            Debug.Log(ok
                ? "Self-Test Notes Layout — Split Fraction Clamp: PASS"
                : $"Self-Test Notes Layout — Split Fraction Clamp: FAIL (clampedLow={clampedLow}, clampedHigh={clampedHigh}, eventFired={eventFired})");
        }
    }
}
```

- [ ] **Step 2: Update `MapLegendUI.cs` to react live to split changes**

Open `Assets/WorldGen/Rendering/MapLegendUI.cs`. Replace lines 38-41:

```csharp
        void Awake()
        {
            BuildCanvasAndPanel();
        }
```

with:

```csharp
        void Awake()
        {
            BuildCanvasAndPanel();
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
```

- [ ] **Step 3: Update `PoiEditPanel.cs` to react live to split changes**

Open `Assets/WorldGen/Rendering/PoiEditPanel.cs`. Replace lines 53-59:

```csharp
        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
            panelGO.SetActive(false);
        }
```

with:

```csharp
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
```

- [ ] **Step 4: Call `BuildDivider()` once from `NotesRootBuilder.cs`**

Open `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`. Replace lines 61-64:

```csharp
            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();
```

with:

```csharp
            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();
            layout.BuildDivider();
```

- [ ] **Step 5: Verify**

Run:
```bash
grep -n "SplitFraction" "Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs" "Assets/WorldGen/Rendering/MapLegendUI.cs" "Assets/WorldGen/Rendering/PoiEditPanel.cs"
```
Expected: `NotesLayoutController.cs` defines `SplitFraction` as a property (not `const`) plus `SetSplitFraction`/`SaveSplitFraction`; `MapLegendUI.cs` and `PoiEditPanel.cs` each still read `NotesLayoutController.SplitFraction` once in their `BuildCanvasAndPanel()`/`BuildUI()` (unchanged from before) and each now also appear in an `OnSplitFractionChanged +=`/`-=` pair.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs Assets/WorldGen/Rendering/MapLegendUI.cs Assets/WorldGen/Rendering/PoiEditPanel.cs Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs
git commit -m "feat: draggable, persisted map/notes screen split"
```

---

### Task 4: Sidebar width becomes runtime-mutable and draggable

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` (full rewrite)

**Interfaces:**
- Consumes: `DraggableDivider.Create(...)` + `OnDragDeltaX`/`OnDragEnd` (Task 2), `DoubleClickHandler` + `OnDoubleClick` (Task 1).
- Produces: `NotesTreeSidebar.Initialize(NotesDocumentController, Transform parent)` — **signature unchanged**. `ExpandedWidth` is no longer a public const (nothing outside this file referenced it — confirmed via repo-wide search before this plan was written).

- [ ] **Step 1: Replace the entire contents of `NotesTreeSidebar.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Collapsible accordion tree: groups expand to show their pages. Selecting a page
    /// opens it via NotesDocumentController. Collapsible via a header toggle button, which
    /// shrinks the whole sidebar column down to a narrow strip (just the toggle) so the
    /// canvas can reclaim that width when the tree isn't needed. A search box filters the
    /// list by group title or page name; each row supports double-click-to-rename and a
    /// persistent "×" delete button (confirmed via ConfirmDialog). The column's expanded
    /// width is user-draggable and persisted across sessions in PlayerPrefs.
    /// </summary>
    public class NotesTreeSidebar : MonoBehaviour
    {
        const string ExpandedWidthPrefsKey = "NotesSidebar.ExpandedWidth";
        const float DefaultExpandedWidth = 200f;
        public const float MinExpandedWidth = 120f;
        public const float MaxExpandedWidth = 400f;
        public const float CollapsedWidth = 28f;
        const float DividerWidth = 8f;

        NotesDocumentController documentController;
        Font builtinFont;
        Transform listContent;
        GameObject listGO;
        GameObject headerTextGO;
        GameObject addGroupButtonGO;
        GameObject searchInputGO;
        GameObject dividerGO;
        InputField searchInput;
        LayoutElement rootLayoutElement;
        bool expanded = true;
        bool rebuildPending;
        string searchQuery = "";
        float expandedWidth;

        // Keyed by page Id so OnActivePageChanged can recolor just the affected rows in place
        // instead of going through Rebuild() — Rebuild() destroys and recreates every row's
        // GameObject, which would reset Unity's double-click tracking (it's keyed by GameObject
        // identity) before a second click could ever register as a double-click.
        readonly Dictionary<string, Image> pageRowImages = new Dictionary<string, Image>();

        InputField activeRenameInput;
        GameObject activeRenameLabelGO;
        bool renameCancelled;

        public void Initialize(NotesDocumentController docController, Transform parent)
        {
            documentController = docController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootGO = new GameObject("NotesTreeSidebar");
            rootGO.transform.SetParent(parent, false);
            var vLayout = rootGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;
            expandedWidth = PlayerPrefs.GetFloat(ExpandedWidthPrefsKey, DefaultExpandedWidth);
            rootLayoutElement = rootGO.AddComponent<LayoutElement>();
            rootLayoutElement.preferredWidth = expandedWidth;

            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rootGO.transform, false);
            var headerImg = headerGO.AddComponent<Image>();
            headerImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            headerBtn.onClick.AddListener(ToggleExpanded);

            headerTextGO = new GameObject("Text");
            headerTextGO.transform.SetParent(headerGO.transform, false);
            var headerText = headerTextGO.AddComponent<Text>();
            headerText.text = "☰ Страницы";
            headerText.font = builtinFont;
            headerText.fontSize = 12;
            headerText.color = Color.white;
            headerText.alignment = TextAnchor.MiddleLeft;
            var headerTextRect = headerTextGO.GetComponent<RectTransform>();
            headerTextRect.anchorMin = new Vector2(0f, 0f);
            headerTextRect.anchorMax = new Vector2(1f, 1f);
            headerTextRect.offsetMin = new Vector2(6f, 0f);
            headerTextRect.offsetMax = Vector2.zero;

            searchInputGO = new GameObject("SearchInput");
            searchInputGO.transform.SetParent(rootGO.transform, false);
            searchInputGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            var searchImg = searchInputGO.AddComponent<Image>();
            searchImg.color = new Color(1f, 1f, 1f, 0.06f);
            searchInput = searchInputGO.AddComponent<InputField>();
            searchInput.targetGraphic = searchImg;

            var searchTextGO = new GameObject("Text");
            searchTextGO.transform.SetParent(searchInputGO.transform, false);
            var searchText = searchTextGO.AddComponent<Text>();
            searchText.font = builtinFont;
            searchText.fontSize = 12;
            searchText.color = Color.white;
            searchText.alignment = TextAnchor.MiddleLeft;
            searchText.supportRichText = false;
            var searchTextRect = searchTextGO.GetComponent<RectTransform>();
            searchTextRect.anchorMin = Vector2.zero;
            searchTextRect.anchorMax = Vector2.one;
            searchTextRect.offsetMin = new Vector2(6f, 0f);
            searchTextRect.offsetMax = new Vector2(-6f, 0f);
            searchInput.textComponent = searchText;

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(searchInputGO.transform, false);
            var placeholderText = placeholderGO.AddComponent<Text>();
            placeholderText.text = "Поиск...";
            placeholderText.font = builtinFont;
            placeholderText.fontSize = 12;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = new Color(1f, 1f, 1f, 0.4f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(6f, 0f);
            placeholderRect.offsetMax = new Vector2(-6f, 0f);
            searchInput.placeholder = placeholderText;

            searchInput.onValueChanged.AddListener(value =>
            {
                searchQuery = value;
                Rebuild();
            });

            listGO = new GameObject("List");
            listGO.transform.SetParent(rootGO.transform, false);
            listGO.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var listScrollRect = listGO.AddComponent<ScrollRect>();
            listScrollRect.horizontal = false;
            listScrollRect.vertical = true;
            listScrollRect.scrollSensitivity = 30f;
            listScrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport uses RectMask2D — no Image needed (same pattern as MapEditorPanel's
            // scroll area), avoids the alpha=0 stencil issue that made Mask+Image(clear)
            // clip all children.
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(listGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            listScrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLayout.spacing = 2f;
            contentVLayout.childControlWidth = true;
            contentVLayout.childControlHeight = false;
            contentVLayout.childForceExpandWidth = true;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            listScrollRect.content = contentRect;
            listContent = contentGO.transform;

            addGroupButtonGO = AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
            });

            documentController.OnDocumentChanged += RequestRebuild;
            documentController.OnActivePageChanged += OnActivePageChanged;
            Rebuild();

            // Anchored at this column's own right edge, pivot=(1,0.5) makes the bar extend
            // LEFTWARD — staying entirely within the sidebar's own bounds — rather than
            // straddling into RightColumn, where RightColumn's own content (built later, by
            // NotesRootBuilder, after this Initialize() call returns) would win any raycast in
            // the overlapping region.
            var divider = DraggableDivider.Create(rootGO.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), DividerWidth);
            dividerGO = divider.gameObject;
            divider.OnDragDeltaX += dx => SetExpandedWidth(expandedWidth + dx);
            divider.OnDragEnd += SaveExpandedWidth;
            var doubleClick = dividerGO.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () =>
            {
                SetExpandedWidth(DefaultExpandedWidth);
                SaveExpandedWidth();
            };
        }

        /// <summary>Recolors just the previously/newly active page rows in place — deliberately
        /// does NOT call Rebuild() (see pageRowImages' field comment for why).</summary>
        void OnActivePageChanged(NotesPage page)
        {
            foreach (var kvp in pageRowImages)
            {
                bool isActive = page != null && kvp.Key == page.Id;
                kvp.Value.color = isActive ? new Color(0.2f, 0.4f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.02f);
            }
        }

        void ToggleExpanded()
        {
            expanded = !expanded;
            listGO.SetActive(expanded);
            headerTextGO.SetActive(expanded);
            addGroupButtonGO.SetActive(expanded);
            searchInputGO.SetActive(expanded);
            dividerGO.SetActive(expanded);
            rootLayoutElement.preferredWidth = expanded ? expandedWidth : CollapsedWidth;
        }

        void SetExpandedWidth(float value)
        {
            expandedWidth = Mathf.Clamp(value, MinExpandedWidth, MaxExpandedWidth);
            if (expanded) rootLayoutElement.preferredWidth = expandedWidth;
        }

        void SaveExpandedWidth() => PlayerPrefs.SetFloat(ExpandedWidthPrefsKey, expandedWidth);

        void RequestRebuild()
        {
            rebuildPending = true;
        }

        void LateUpdate()
        {
            if (!rebuildPending) return;
            rebuildPending = false;
            Rebuild();
        }

        void Update()
        {
            if (activeRenameInput != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CancelActiveRename();
        }

        bool MatchesSearch(string text) =>
            string.IsNullOrEmpty(searchQuery) || text.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant());

        void Rebuild()
        {
            // Any in-progress rename's InputField/label are about to be destroyed below along
            // with the rest of the old rows — clear the tracking fields so Escape (in Update())
            // can't touch already-destroyed GameObjects afterward.
            activeRenameInput = null;
            activeRenameLabelGO = null;
            renameCancelled = false;
            pageRowImages.Clear();

            // SetActive(false) takes effect immediately; Destroy() is deferred to end of
            // frame, so without deactivating first, the old and newly-built rows below would
            // both render for one frame, showing as overlapping/doubled UI.
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            foreach (var group in documentController.Document.Groups)
                BuildGroupRow(group);
        }

        void BuildGroupRow(PageGroup group)
        {
            bool titleMatches = MatchesSearch(group.Title);
            bool hasMatchingPage = false;
            if (!titleMatches)
            {
                foreach (var p in group.Pages)
                {
                    if (MatchesSearch(p.Name)) { hasMatchingPage = true; break; }
                }
            }
            if (!titleMatches && !hasMatchingPage) return;

            var groupGO = new GameObject($"Group_{group.Id}");
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(groupGO.transform, false);
            titleGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            var titleTextGO = new GameObject("Text");
            titleTextGO.transform.SetParent(titleGO.transform, false);
            var titleText = titleTextGO.AddComponent<Text>();
            string suffix = group.LinkedPoiId != null ? " 📍" : "";
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            var titleTextRect = titleTextGO.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.offsetMin = Vector2.zero;
            titleTextRect.offsetMax = Vector2.zero;

            AddRenameAndDelete(titleTextGO, titleGO.transform, titleText, titleTextRect, group.Title,
                newTitle => documentController.RenameGroup(group.Id, newTitle),
                () => ConfirmDialog.Show(builtinFont, $"Удалить группу \"{group.Title}\" и все её страницы ({group.Pages.Count})?", confirmed =>
                {
                    if (confirmed) documentController.DeleteGroup(group.Id);
                }));

            foreach (var page in group.Pages)
            {
                if (titleMatches || MatchesSearch(page.Name))
                    BuildPageRow(groupGO.transform, group, page);
            }

            AddSmallActionButton(groupGO.transform, "  + Страница", () =>
            {
                documentController.CreatePage(group.Id, $"Страница {group.Pages.Count + 1}");
            });
        }

        void BuildPageRow(Transform parent, PageGroup group, NotesPage page)
        {
            var rowGO = new GameObject($"Page_{page.Id}");
            rowGO.transform.SetParent(parent, false);
            var img = rowGO.AddComponent<Image>();
            bool isActive = documentController.ActivePage != null && documentController.ActivePage.Id == page.Id;
            img.color = isActive ? new Color(0.2f, 0.4f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.02f);
            pageRowImages[page.Id] = img;
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = img;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            // Deliberately does NOT call Rebuild() here — OnActivePageChanged (subscribed in
            // Initialize) recolors the affected rows in place instead, so this row's
            // GameObject survives a click (see pageRowImages' field comment for why that
            // matters for double-click-to-rename).
            btn.onClick.AddListener(() => documentController.OpenPage(page.Id));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(rowGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = $"• {page.Name}";
            text.font = builtinFont;
            text.fontSize = 13;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = Vector2.zero;

            // clickCatcherGO is rowGO (not textGO) here specifically so Button and
            // DoubleClickHandler end up on the SAME GameObject — see this task's header
            // comment on why attaching it to the child Text instead would break "open page".
            AddRenameAndDelete(rowGO, rowGO.transform, text, textRect, page.Name,
                newName => documentController.RenamePage(page.Id, newName),
                () => ConfirmDialog.Show(builtinFont, $"Удалить страницу \"{page.Name}\"?", confirmed =>
                {
                    if (confirmed) documentController.DeletePage(page.Id);
                }));
        }

        /// <summary>Shrinks `label`'s rect to leave room for a new "×" delete button anchored to
        /// the row's right edge, and wires up a double-click-to-rename InputField (same rect as
        /// the shrunk label) that commits via onRename on Enter/blur or cancels on Escape.</summary>
        void AddRenameAndDelete(GameObject clickCatcherGO, Transform rowTransform, Text label, RectTransform labelRect, string rawValue, System.Action<string> onRename, System.Action onDeleteRequested)
        {
            labelRect.offsetMax = new Vector2(labelRect.offsetMax.x - 20f, labelRect.offsetMax.y);

            var inputGO = new GameObject("RenameInput");
            inputGO.transform.SetParent(labelRect.parent, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = labelRect.anchorMin;
            inputRect.anchorMax = labelRect.anchorMax;
            inputRect.offsetMin = labelRect.offsetMin;
            inputRect.offsetMax = labelRect.offsetMax;
            var inputImg = inputGO.AddComponent<Image>();
            inputImg.color = new Color(1f, 1f, 1f, 0.1f);
            var input = inputGO.AddComponent<InputField>();
            input.targetGraphic = inputImg;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(inputGO.transform, false);
            var inputText = inputTextGO.AddComponent<Text>();
            inputText.font = builtinFont;
            inputText.fontSize = label.fontSize;
            inputText.color = label.color;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            var inputTextRect = inputTextGO.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(4f, 0f);
            inputTextRect.offsetMax = new Vector2(-4f, 0f);
            input.textComponent = inputText;
            inputGO.SetActive(false);

            var doubleClick = clickCatcherGO.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () => StartRename(label.gameObject, input, rawValue);

            input.onEndEdit.AddListener(newText =>
            {
                bool wasCancelled = renameCancelled;
                activeRenameInput = null;
                activeRenameLabelGO = null;
                renameCancelled = false;
                if (wasCancelled) return;
                inputGO.SetActive(false);
                label.gameObject.SetActive(true);
                if (!string.IsNullOrWhiteSpace(newText))
                    onRename(newText.Trim());
            });

            var deleteGO = new GameObject("Delete");
            deleteGO.transform.SetParent(rowTransform, false);
            var deleteImg = deleteGO.AddComponent<Image>();
            deleteImg.color = new Color(1f, 1f, 1f, 0.06f);
            var deleteBtn = deleteGO.AddComponent<Button>();
            deleteBtn.targetGraphic = deleteImg;
            deleteBtn.onClick.AddListener(() => onDeleteRequested());
            var deleteRect = deleteGO.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(1f, 0f);
            deleteRect.anchorMax = new Vector2(1f, 1f);
            deleteRect.pivot = new Vector2(1f, 0.5f);
            deleteRect.sizeDelta = new Vector2(20f, 0f);
            deleteRect.anchoredPosition = Vector2.zero;

            var deleteTextGO = new GameObject("Text");
            deleteTextGO.transform.SetParent(deleteGO.transform, false);
            var deleteText = deleteTextGO.AddComponent<Text>();
            deleteText.text = "×";
            deleteText.font = builtinFont;
            deleteText.fontSize = 14;
            deleteText.color = new Color(1f, 0.6f, 0.6f);
            deleteText.alignment = TextAnchor.MiddleCenter;
            deleteText.raycastTarget = false;
            var deleteTextRect = deleteTextGO.GetComponent<RectTransform>();
            deleteTextRect.anchorMin = Vector2.zero;
            deleteTextRect.anchorMax = Vector2.one;
            deleteTextRect.sizeDelta = Vector2.zero;
        }

        void StartRename(GameObject labelGO, InputField input, string rawValue)
        {
            activeRenameLabelGO = labelGO;
            activeRenameInput = input;
            renameCancelled = false;
            labelGO.SetActive(false);
            input.gameObject.SetActive(true);
            input.text = rawValue;
            input.Select();
            input.ActivateInputField();
        }

        void CancelActiveRename()
        {
            if (activeRenameInput == null) return;
            renameCancelled = true;
            activeRenameInput.gameObject.SetActive(false);
            if (activeRenameLabelGO != null) activeRenameLabelGO.SetActive(true);
            activeRenameInput = null;
            activeRenameLabelGO = null;
        }

        GameObject AddSmallActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.45f, 0.25f, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 18f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = Vector2.zero;

            return go;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Tree Sidebar — Collapse Toggle")]
        public void SelfTestCollapseToggle()
        {
            if (rootLayoutElement == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Collapse Toggle: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            if (!expanded) { ok = false; reason = "expected to start expanded"; }
            if (ok && !Mathf.Approximately(rootLayoutElement.preferredWidth, expandedWidth))
            { ok = false; reason = $"expected preferredWidth={expandedWidth} while expanded, got {rootLayoutElement.preferredWidth}"; }

            if (ok)
            {
                ToggleExpanded();
                if (expanded) { ok = false; reason = "expected collapsed after first toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, CollapsedWidth))
                { ok = false; reason = $"expected preferredWidth={CollapsedWidth} while collapsed, got {rootLayoutElement.preferredWidth}"; }
                else if (listGO.activeSelf || headerTextGO.activeSelf || addGroupButtonGO.activeSelf || searchInputGO.activeSelf || dividerGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput/divider all inactive while collapsed"; }
            }

            if (ok)
            {
                ToggleExpanded();
                if (!expanded) { ok = false; reason = "expected expanded after second toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, expandedWidth))
                { ok = false; reason = $"expected preferredWidth={expandedWidth} after re-expanding, got {rootLayoutElement.preferredWidth}"; }
                else if (!listGO.activeSelf || !headerTextGO.activeSelf || !addGroupButtonGO.activeSelf || !searchInputGO.activeSelf || !dividerGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput/divider all active after re-expanding"; }
            }

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Collapse Toggle: PASS"
                : $"Self-Test Notes Tree Sidebar — Collapse Toggle: FAIL ({reason})");
        }

        [ContextMenu("Self-Test: Notes Tree Sidebar — Search Filter")]
        public void SelfTestSearchFilter()
        {
            if (documentController == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Search Filter: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            searchQuery = "";
            Rebuild();
            int totalRows = listContent.childCount;
            if (totalRows == 0) { ok = false; reason = "expected at least one group row with no search query"; }

            if (ok)
            {
                searchQuery = "zzz_no_such_page_or_group_zzz";
                Rebuild();
                if (listContent.childCount != 0)
                { ok = false; reason = "expected zero rows for a query matching nothing"; }
            }

            searchQuery = "";
            Rebuild();

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Search Filter: PASS"
                : $"Self-Test Notes Tree Sidebar — Search Filter: FAIL ({reason})");
        }

        [ContextMenu("Self-Test: Notes Tree Sidebar — Width Clamp")]
        public void SelfTestWidthClamp()
        {
            if (rootLayoutElement == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Width Clamp: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            float original = expandedWidth;
            bool ok = true;
            string reason = "";

            SetExpandedWidth(10f);
            if (!Mathf.Approximately(expandedWidth, MinExpandedWidth))
            { ok = false; reason = $"expected clamp to {MinExpandedWidth}, got {expandedWidth}"; }

            if (ok)
            {
                SetExpandedWidth(1000f);
                if (!Mathf.Approximately(expandedWidth, MaxExpandedWidth))
                { ok = false; reason = $"expected clamp to {MaxExpandedWidth}, got {expandedWidth}"; }
            }

            SetExpandedWidth(original);

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Width Clamp: PASS"
                : $"Self-Test Notes Tree Sidebar — Width Clamp: FAIL ({reason})");
        }
    }
}
```

- [ ] **Step 2: Verify**

Run:
```bash
grep -n "expandedWidth\|dividerGO\|DraggableDivider\|public const float ExpandedWidth" "Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs"
```
Expected: no `public const float ExpandedWidth` (it's gone); `expandedWidth` (lowercase instance field) appears in its declaration, `Initialize`, `SetExpandedWidth`, `SaveExpandedWidth`, `ToggleExpanded`, both self-tests; `dividerGO` appears in its declaration, `Initialize`, `ToggleExpanded`, and both `SelfTestCollapseToggle` assertions; `DraggableDivider` appears once (the `Create` call in `Initialize`).

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "feat: draggable, persisted notes sidebar width"
```

- [ ] **Step 4: Manual Play-mode verification (performed by the user, not the implementer)**

In the Unity Editor:
1. Enter Play Mode. Confirm no console errors.
2. Run **Self-Test: Notes Layout — Split Fraction Clamp** (on `NotesLayoutController`), **Self-Test: Notes Tree Sidebar — Collapse Toggle**, **Search Filter**, and **Width Clamp** (all on `NotesTreeSidebar`) — all should log `PASS`.
3. Hover the map/notes boundary (around 2/3 across the screen): confirm a subtle highlight appears. Drag it left/right: confirm the map camera viewport, the notes panel, the legend panel, and the POI edit panel (select a POI to show it) all move together in lockstep, live, with no visual desync.
4. Drag the map/notes boundary all the way to each extreme: confirm it stops at roughly 30%/85% rather than vanishing or overshooting.
5. Double-click the map/notes boundary: confirm it snaps back to the original ~2:3 split.
6. Exit and re-enter Play Mode (or stop and restart the game): confirm the map/notes split you last dragged to is still in effect (persisted).
7. Hover the sidebar's right edge (between the page list and the canvas): confirm a highlight appears. Drag it left/right: confirm the sidebar column resizes and the canvas reclaims/gives up the difference, clamped to roughly 120–400px.
8. Double-click the sidebar's edge: confirm it snaps back to 200px.
9. Collapse the sidebar (header click): confirm its divider disappears too (nothing to drag when collapsed at a fixed 28px). Re-expand: confirm the divider reappears at the last dragged width.
10. Restart Play Mode again: confirm the sidebar width also persisted.
11. Report any bugs found back for fixing before moving to `finishing-a-development-branch`.

---

## Post-plan

Once all four tasks are complete and the user confirms Play-mode verification passes, this closes out the entire original request: toolbar redesign + icon fix, sidebar CRUD + search, and draggable panel splits are all done.
