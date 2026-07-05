# Notes Sidebar Rename/Delete/Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add double-click-to-rename, a persistent "×" delete button, and a search box to the notes editor's page-tree sidebar (`NotesTreeSidebar.cs`), using the rename/delete methods that already exist on `NotesDocumentController`.

**Architecture:** A new static `ConfirmDialog` utility is extracted from `NotesUndoManager`'s existing (private, duplicated-if-left-alone) confirm-dialog code, so both canvas-object deletion and the new sidebar group/page deletion share one dialog implementation. A new small `DoubleClickToRename` component (`IPointerClickHandler`, checks `eventData.clickCount == 2`) is attached directly to each row's existing click target — the page row's `Button` GameObject for pages (so it doesn't shadow the existing single-click "open page" handler — see Task 3's comment for why), or the bare title `Text` GameObject for groups (which has no existing click handler to preserve). `NotesTreeSidebar.cs` gets a search `InputField` that re-runs the existing `Rebuild()` on every keystroke with the query threaded through row construction, plus a shared `AddRenameAndDelete` helper called from both `BuildGroupRow`/`BuildPageRow` that wires up the inline rename `InputField` and the "×" delete button (confirmed via `ConfirmDialog`).

**Tech Stack:** Unity 6000.3.2f1, Built-in Render Pipeline, legacy `UnityEngine.UI` (no TextMeshPro), new Input System (`UnityEngine.InputSystem`), code-only UI construction (`new GameObject()` + `AddComponent<>()`).

## Global Constraints

- No automated Unity test runner exists in this project. Verification is via the codebase's established `[ContextMenu("Self-Test: ...")]` method pattern plus manual Play-mode testing performed by the user — the implementer has no direct Unity Editor access.
- No undo (Ctrl+Z) for group/page deletion — confirm-only, per spec decision (canvas object/link deletion already has undo via `NotesUndoManager`; group/page deletion deliberately does not, to avoid snapshotting an entire page's contents for restoration).
- Out of scope (do not implement): drag-and-drop reordering, right-click context menus, user-draggable/resizable panel splits (separate spec, next in sequence).

---

### Task 1: Extract shared `ConfirmDialog` utility from `NotesUndoManager`

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs:93-97` (field removal), `:142-166` (call sites), `:183-256` (method removal)

**Interfaces:**
- Produces: `ConfirmDialog.Show(Font font, string message, System.Action<bool> onResult)` — static method, shows a message box with "Отмена"/"Удалить" buttons, invokes `onResult(true)` on confirm or `onResult(false)` on cancel. Only one dialog is ever shown at a time (a new `Show` call destroys any dialog already on screen). Task 3 depends on this exact signature.

- [ ] **Step 1: Create `ConfirmDialog.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Shared "message + Отмена/Удалить" modal, extracted from NotesUndoManager so
    /// canvas-object deletion and sidebar group/page deletion reuse the same dialog
    /// instead of duplicating the UI-building code. Only one dialog is ever shown at once.
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject activeDialogGO;

        public static void Show(Font font, string message, System.Action<bool> onResult)
        {
            if (activeDialogGO != null) Object.Destroy(activeDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeDialogGO = canvasGO;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.7f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(280f, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            var msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = font;
            msgText.fontSize = 13;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0.4f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.sizeDelta = Vector2.zero;

            AddDialogButton(font, panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(false);
            });
            AddDialogButton(font, panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), new Color(0.55f, 0.15f, 0.15f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(true);
            });
        }

        static void AddDialogButton(Font font, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }
    }
}
```

- [ ] **Step 2: Redirect `NotesUndoManager.cs`'s two call sites and remove the duplicated field/methods**

Open `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs`. Replace lines 93-97:

```csharp
        [Header("Confirm dialog UI (built at runtime, not scene-assigned)")]
        public Font builtinFont;

        readonly Stack<Command> undoStack = new Stack<Command>();
        GameObject confirmDialogGO;
```

with:

```csharp
        [Header("Confirm dialog UI (built at runtime, not scene-assigned)")]
        public Font builtinFont;

        readonly Stack<Command> undoStack = new Stack<Command>();
```

Replace lines 142-166 (`RequestDeleteObject` and `RequestDeleteLink`):

```csharp
        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog($"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void RequestDeleteLink(NotesCanvasController canvas, LinkData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog("Удалить связь?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveLink(data.Id);
                    undoStack.Push(new DeleteLinkCommand { Canvas = canvas, FromObjectId = data.FromObjectId, ToObjectId = data.ToObjectId });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }
```

with:

```csharp
        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ConfirmDialog.Show(builtinFont, $"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void RequestDeleteLink(NotesCanvasController canvas, LinkData data, System.Action<bool> onConfirmed)
        {
            ConfirmDialog.Show(builtinFont, "Удалить связь?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveLink(data.Id);
                    undoStack.Push(new DeleteLinkCommand { Canvas = canvas, FromObjectId = data.FromObjectId, ToObjectId = data.ToObjectId });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }
```

Delete lines 183-256 entirely (the `ShowConfirmDialog` and `AddDialogButton` methods — everything from `void ShowConfirmDialog(string message, System.Action<bool> onResult)` through the closing `}` of `AddDialogButton`).

- [ ] **Step 3: Verify no leftover references**

Run:
```bash
grep -n "ShowConfirmDialog\|AddDialogButton\|confirmDialogGO" "Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs"
```
Expected: no matches.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "refactor: extract shared ConfirmDialog utility from NotesUndoManager"
```

---

### Task 2: `DoubleClickToRename` component

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/DoubleClickToRename.cs`

**Interfaces:**
- Produces: `DoubleClickToRename : MonoBehaviour, IPointerClickHandler` with a public `System.Action OnDoubleClick` field. Task 3 depends on this exact type/member name — attach via `gameObject.AddComponent<DoubleClickToRename>()` then set `.OnDoubleClick = () => ...`.

- [ ] **Step 1: Create `DoubleClickToRename.cs`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick. Used by NotesTreeSidebar to
    /// enter inline-rename mode on group/page rows.
    /// </summary>
    public class DoubleClickToRename : MonoBehaviour, IPointerClickHandler
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

- [ ] **Step 2: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/DoubleClickToRename.cs
git commit -m "feat: add DoubleClickToRename double-click detector component"
```

---

### Task 3: Search, rename, and delete in `NotesTreeSidebar.cs`

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` (full rewrite)

**Interfaces:**
- Consumes: `ConfirmDialog.Show(Font, string, Action<bool>)` (Task 1), `DoubleClickToRename` + its `OnDoubleClick` field (Task 2), `NotesDocumentController.RenameGroup(string, string)`, `.DeleteGroup(string)`, `.RenamePage(string, string)`, `.DeletePage(string)`, and the pre-existing `event Action<NotesPage> OnActivePageChanged` (all pre-existing on `NotesDocumentController`, unchanged).
- Produces: `NotesTreeSidebar.Initialize(NotesDocumentController, Transform parent)` — **signature unchanged**.

**Why `DoubleClickToRename` attaches to different GameObjects for groups vs. pages:** Unity's event system finds the *nearest ancestor* (starting from the exact GameObject the raycast hit) that has *any* component implementing `IPointerClickHandler`, then invokes *every* matching component on that one GameObject — it does not keep climbing past the first GameObject with a match. Page rows already have a `Button` (`IPointerClickHandler`) on the row's own GameObject (`rowGO`) to open the page on click; if `DoubleClickToRename` were attached to the page row's child `Text` GameObject instead, the child would be found first and the row's `Button` would stop receiving clicks entirely (broken "open page"). So for pages, `DoubleClickToRename` is attached to the same `rowGO` the `Button` is already on — both components exist on the one GameObject, and Unity invokes both. Group titles have no existing click handler, so `DoubleClickToRename` attaches directly to the title's `Text` GameObject with no such conflict.

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
    /// persistent "×" delete button (confirmed via ConfirmDialog).
    /// </summary>
    public class NotesTreeSidebar : MonoBehaviour
    {
        public const float ExpandedWidth = 200f;
        public const float CollapsedWidth = 28f;

        NotesDocumentController documentController;
        Font builtinFont;
        Transform listContent;
        GameObject listGO;
        GameObject headerTextGO;
        GameObject addGroupButtonGO;
        GameObject searchInputGO;
        InputField searchInput;
        LayoutElement rootLayoutElement;
        bool expanded = true;
        bool rebuildPending;
        string searchQuery = "";

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
            rootLayoutElement = rootGO.AddComponent<LayoutElement>();
            rootLayoutElement.preferredWidth = ExpandedWidth;

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
            rootLayoutElement.preferredWidth = expanded ? ExpandedWidth : CollapsedWidth;
        }

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
            // DoubleClickToRename end up on the SAME GameObject — see this task's header
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

            var doubleClick = clickCatcherGO.AddComponent<DoubleClickToRename>();
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
            if (ok && !Mathf.Approximately(rootLayoutElement.preferredWidth, ExpandedWidth))
            { ok = false; reason = $"expected preferredWidth={ExpandedWidth} while expanded, got {rootLayoutElement.preferredWidth}"; }

            if (ok)
            {
                ToggleExpanded();
                if (expanded) { ok = false; reason = "expected collapsed after first toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, CollapsedWidth))
                { ok = false; reason = $"expected preferredWidth={CollapsedWidth} while collapsed, got {rootLayoutElement.preferredWidth}"; }
                else if (listGO.activeSelf || headerTextGO.activeSelf || addGroupButtonGO.activeSelf || searchInputGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput all inactive while collapsed"; }
            }

            if (ok)
            {
                ToggleExpanded();
                if (!expanded) { ok = false; reason = "expected expanded after second toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, ExpandedWidth))
                { ok = false; reason = $"expected preferredWidth={ExpandedWidth} after re-expanding, got {rootLayoutElement.preferredWidth}"; }
                else if (!listGO.activeSelf || !headerTextGO.activeSelf || !addGroupButtonGO.activeSelf || !searchInputGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput all active after re-expanding"; }
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
    }
}
```

- [ ] **Step 2: Verify no leftover references to removed/renamed members**

Run:
```bash
grep -n "class NotesTreeSidebar\|searchInputGO\|AddRenameAndDelete\|DoubleClickToRename\|pageRowImages\|Rebuild();" "Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs"
```
Expected: `searchInputGO` appears in its field declaration, `Initialize` (construction + `onValueChanged` capture is via the outer `searchInputGO`/`searchInput` fields), `ToggleExpanded`, and both `SelfTestCollapseToggle` assertions; `AddRenameAndDelete` appears in its own method definition plus one call each from `BuildGroupRow`/`BuildPageRow`; `DoubleClickToRename` appears once (inside `AddRenameAndDelete`); `pageRowImages` appears in its field declaration, `OnActivePageChanged`, `Rebuild()`, and `BuildPageRow`; bare `Rebuild();` calls appear in `LateUpdate`, the search box's `onValueChanged` listener, and both self-tests — **not** inside `BuildPageRow`'s `btn.onClick` listener (that would reintroduce the double-click-breaking bug this task's design works around).

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "feat: sidebar rename, delete, and search"
```

- [ ] **Step 4: Manual Play-mode verification (performed by the user, not the implementer)**

In the Unity Editor:
1. Enter Play Mode. Confirm no console errors.
2. Right-click the `NotesTreeSidebar` component in the Inspector and run **Self-Test: Notes Tree Sidebar — Collapse Toggle** and **Self-Test: Notes Tree Sidebar — Search Filter** — both should log `PASS`.
3. Also right-click the `NotesUndoManager` component and run **Self-Test: Notes Undo — Create/Undo Card** — should still `PASS` (confirms Task 1's `ConfirmDialog` extraction didn't break existing undo).
4. Type into the new search box above the page list: confirm the list filters to matching groups/pages as you type, and clearing the box restores the full list.
5. Create a second page so there's an active page and a non-active one. Double-click the **non-active** page's name directly (without clicking it open first): confirm it still turns into an editable text field — this is the case Task 3's `pageRowImages`/`OnActivePageChanged` redesign specifically exists to keep working (a naive rebuild-on-click approach would silently break double-click here).
6. Double-click a page's name: confirm it turns into an editable text field with the current name selected. Press Enter with new text — confirm the page's name updates in the list. Double-click again, press Escape — confirm it reverts to the original name without saving.
7. Repeat step 6 for a group's title. Click between pages (single clicks) and confirm the active-page highlight still moves correctly.
8. Click the "×" next to a page: confirm a confirm dialog appears ("Удалить страницу ..."); cancelling leaves it in place, confirming removes it from the list.
9. Click the "×" next to a group with pages in it: confirm the dialog mentions the page count; confirming removes the group and all its pages.
10. On the canvas, create an object and delete it via the Delete key (existing flow): confirm the same-looking confirm dialog still appears and deletion/Ctrl+Z undo still work exactly as before (regression check for Task 1's extraction).
11. Report any bugs found back for fixing before moving to `finishing-a-development-branch`.

---

## Post-plan

Once all three tasks are complete and the user confirms Play-mode verification passes, this sub-project is done. Per the agreed sequence, the next spec to brainstorm is user-draggable/resizable panel splits (map/notes split, notes sidebar width).
