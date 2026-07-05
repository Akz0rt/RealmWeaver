# Notes Editor — Internal Layout Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the notes editor's page-tree sidebar from the top of a single vertical stack to a permanent, fixed-width left column spanning the full height of the notes panel, so the drawing canvas always gets nearly all available height regardless of how many pages/groups exist.

**Architecture:** `NotesRootBuilder.Awake()`'s `notesAreaGO` switches from a `VerticalLayoutGroup` (Sidebar → Toolbar → Canvas, stacked) to a `HorizontalLayoutGroup` with two columns: the sidebar (fixed width via `LayoutElement.preferredWidth`, full height via the group's cross-axis stretch) and a new `RightColumn` (`VerticalLayoutGroup`, `flexibleWidth = 1`) holding the existing Toolbar + Canvas viewport stack unchanged. `NotesTreeSidebar.cs`'s page list switches from an unbounded `ContentSizeFitter` list to a `ScrollRect`/`Viewport`/`Content` structure (mirroring `MapEditorPanel.cs`'s established scroll-area pattern), and its collapse toggle now also shrinks the whole column's `LayoutElement.preferredWidth` (200px ↔ 28px) instead of only hiding the list.

**Tech Stack:** Unity 6000.3.2f1, Built-in Render Pipeline, legacy `UnityEngine.UI` (no TextMeshPro), code-only UI construction (`new GameObject()` + `AddComponent<>()`).

## Global Constraints

- No automated Unity test runner exists in this project. Verification is via the codebase's established `[ContextMenu("Self-Test: ...")]` method pattern (see `NotesToolbar.SelfTestIconCaching`, `ObjectResizeController.SelfTestCornerResize`) plus manual Play-mode testing performed by the user — the implementer has no direct Unity Editor access.
- Sidebar width is fixed in pixels (`200px` expanded / `28px` collapsed via `LayoutElement.preferredWidth`), never a fraction of screen width — per spec's Edge Cases section.
- Toolbar stays scoped to the canvas (right column only), never spans the sidebar.
- Out of scope (do not implement): user-draggable/resizable sidebar width; any change to the map/notes 2:1 screen split; any change to toolbar contents, canvas rendering, or POI/link functionality.

---

### Task 1: Restructure `NotesRootBuilder.cs` into a two-column split

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs:44-102`

**Interfaces:**
- Consumes: `NotesTreeSidebar.Initialize(NotesDocumentController, Transform parent)` (unchanged signature — Task 2 keeps this signature), `NotesToolbar.Initialize(CanvasInteractionController, Transform parent)` (unchanged, from `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs:36`).
- Produces: `notesAreaGO` now holds exactly two children — the sidebar's own root GameObject (named `"NotesTreeSidebar"`, built by `NotesTreeSidebar.Initialize`) and a new `"RightColumn"` GameObject. Later tasks (Task 2) rely on `notesAreaGO`'s `HorizontalLayoutGroup` having `childControlHeight = true` so the sidebar auto-stretches to full height without any height-related `LayoutElement` field on the sidebar itself.

- [ ] **Step 1: Replace the vertical stack with a horizontal split + right column**

Open `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`. Replace lines 66-101 (from `var vLayout = notesAreaGO.AddComponent<VerticalLayoutGroup>();` through the `interaction.viewportRect = viewportRect;` line) with:

```csharp
            // NotesArea is a left-to-right split: the page-tree sidebar (fixed width,
            // full height via this group's cross-axis stretch) on the left, and a
            // RightColumn (toolbar + canvas, flexible width) on the right absorbing all
            // remaining space — see NotesTreeSidebar for the sidebar's own fixed/collapsed
            // width handling.
            var hLayout = notesAreaGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = true;

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            var sidebar = gameObject.AddComponent<NotesTreeSidebar>();
            sidebar.Initialize(DocumentController, notesAreaGO.transform);

            var rightColumnGO = new GameObject("RightColumn");
            rightColumnGO.transform.SetParent(notesAreaGO.transform, false);
            var rightColumnVLayout = rightColumnGO.AddComponent<VerticalLayoutGroup>();
            rightColumnVLayout.childControlWidth = true;
            rightColumnVLayout.childForceExpandWidth = true;
            rightColumnVLayout.childControlHeight = true;
            rightColumnVLayout.childForceExpandHeight = true;
            rightColumnGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Created before the viewport so CanvasInteractionController exists (as a component
            // reference) when NotesToolbar.Initialize wires button clicks to it; its dependent
            // fields (canvasController/viewportRect) are only read later, after they're assigned
            // below, never during this construction step.
            var undoManager = gameObject.AddComponent<NotesUndoManager>();
            var interaction = gameObject.AddComponent<CanvasInteractionController>();
            interaction.undoManager = undoManager;

            var toolbar = gameObject.AddComponent<NotesToolbar>();
            toolbar.Initialize(interaction, rightColumnGO.transform);

            var viewportGO = new GameObject("CanvasViewport");
            viewportGO.transform.SetParent(rightColumnGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            var viewportLE = viewportGO.AddComponent<LayoutElement>();
            viewportLE.flexibleHeight = 1f;

            CanvasController = gameObject.AddComponent<NotesCanvasController>();
            CanvasController.Initialize(DocumentController, viewportRect, interaction);

            interaction.canvasController = CanvasController;
            interaction.viewportRect = viewportRect;
```

This is a direct replacement: `notesAreaGO`'s layout group changes from `VerticalLayoutGroup` to `HorizontalLayoutGroup`, the sidebar is still parented directly to `notesAreaGO.transform` (unchanged), and the toolbar + canvas viewport now parent to the new `rightColumnGO.transform` instead of `notesAreaGO.transform` directly.

- [ ] **Step 2: Verify no other reference to the old vertical stack remains**

Run:
```bash
grep -n "notesAreaGO" "Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs"
```
Expected: every remaining `notesAreaGO` reference is either `notesAreaGO.transform.SetParent(...)` (the canvas background/rect setup near the top, unchanged) or the `sidebar.Initialize(DocumentController, notesAreaGO.transform)` / `var hLayout = notesAreaGO.AddComponent<HorizontalLayoutGroup>();` lines from Step 1. No `VerticalLayoutGroup` should remain assigned to `notesAreaGO` itself.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs
git commit -m "refactor: split notes editor into sidebar + right column (horizontal layout)"
```

---

### Task 2: Fixed-width, scrollable, collapsible sidebar column

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` (full rewrite of `Initialize`, `ToggleExpanded`, `AddSmallActionButton`; `Rebuild`/`BuildGroupRow`/`BuildPageRow` unchanged)

**Interfaces:**
- Consumes: `notesAreaGO`'s `HorizontalLayoutGroup` from Task 1 (`childControlHeight = true`), which stretches this component's root GameObject to the full height of `NotesArea` automatically — no height-related field needed here.
- Produces: `NotesTreeSidebar.ExpandedWidth` (`200f`) and `NotesTreeSidebar.CollapsedWidth` (`28f`) public constants (in case other code or a future task needs the collapsed/expanded pixel values). `listContent` (the `Transform` that `Rebuild()`/`BuildGroupRow()` parent rows into) now points at the `ScrollRect`'s `Content` child instead of the plain `List` GameObject directly — no change to `Rebuild()`/`BuildGroupRow()`/`BuildPageRow()` themselves, since they only ever reference the `listContent` field.

- [ ] **Step 1: Rewrite `NotesTreeSidebar.cs`**

Replace the entire contents of `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Collapsible accordion tree: groups expand to show their pages. Selecting a page
    /// opens it via NotesDocumentController. Collapsible via a header toggle button, which
    /// shrinks the whole sidebar column down to a narrow strip (just the toggle) so the
    /// canvas can reclaim that width when the tree isn't needed.
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
        LayoutElement rootLayoutElement;
        bool expanded = true;
        bool rebuildPending;

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
            Rebuild();
        }

        void ToggleExpanded()
        {
            expanded = !expanded;
            listGO.SetActive(expanded);
            headerTextGO.SetActive(expanded);
            addGroupButtonGO.SetActive(expanded);
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

        void Rebuild()
        {
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
            var groupGO = new GameObject($"Group_{group.Id}");
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(groupGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            string suffix = group.LinkedPoiId != null ? " 📍" : "";
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            foreach (var page in group.Pages)
                BuildPageRow(groupGO.transform, group, page);

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
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = img;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            btn.onClick.AddListener(() =>
            {
                documentController.OpenPage(page.Id);
                Rebuild();
            });

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
                else if (listGO.activeSelf || headerTextGO.activeSelf || addGroupButtonGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton all inactive while collapsed"; }
            }

            if (ok)
            {
                ToggleExpanded();
                if (!expanded) { ok = false; reason = "expected expanded after second toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, ExpandedWidth))
                { ok = false; reason = $"expected preferredWidth={ExpandedWidth} after re-expanding, got {rootLayoutElement.preferredWidth}"; }
                else if (!listGO.activeSelf || !headerTextGO.activeSelf || !addGroupButtonGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton all active after re-expanding"; }
            }

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Collapse Toggle: PASS"
                : $"Self-Test Notes Tree Sidebar — Collapse Toggle: FAIL ({reason})");
        }
    }
}
```

- [ ] **Step 2: Verify no leftover references to the old plain-list fields**

Run:
```bash
grep -n "ContentSizeFitter\|VerticalLayoutGroup" "Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs"
```
Expected: `ContentSizeFitter` appears exactly twice (once on `contentGO`, once inside `BuildGroupRow`'s `groupGO`); `VerticalLayoutGroup` appears exactly three times (`rootGO`'s `vLayout`, `contentGO`'s `contentVLayout`, `BuildGroupRow`'s `groupVLayout`). No `VerticalLayoutGroup` should remain directly on `listGO` — it now holds a `ScrollRect` instead.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "feat: fixed-width scrollable sidebar column with collapse-to-strip"
```

- [ ] **Step 4: Manual Play-mode verification (performed by the user, not the implementer)**

In the Unity Editor:
1. Enter Play Mode on the scene that runs `NotesRootBuilder`. Confirm no console errors.
2. Right-click the `NotesTreeSidebar` component in the Inspector (on the same GameObject as `NotesRootBuilder`) and run **Self-Test: Notes Tree Sidebar — Collapse Toggle**. Confirm it logs `PASS`.
3. Confirm the sidebar renders as a fixed-width left column spanning the full height of the notes panel, with the toolbar and canvas to its right — the canvas should now occupy nearly all remaining height regardless of how many pages/groups exist.
4. Add enough groups/pages that the list would have overflowed before this change; confirm the list scrolls inside the sidebar column instead of pushing into the canvas.
5. Click the sidebar header to collapse it; confirm the column shrinks to a narrow strip (just the toggle button) and the canvas reclaims that width. Click again to re-expand; confirm the page list and its scroll position are unaffected.
6. Report any bugs found back for fixing before moving to `finishing-a-development-branch`.

---

## Post-plan

Once both tasks are complete and the user confirms Play-mode verification passes, invoke `superpowers:finishing-a-development-branch` to present merge/PR/keep/discard options (working directly on `main`, so "merge" is not applicable — the branch options reduce to committing as already done, or the user may simply confirm completion).
