# Notes Editor — Internal Layout Redesign — Design

**Date:** 2026-07-04
**Status:** Approved, ready for planning
**Branch:** main

---

## Goal

The notes editor's internal layout currently stacks the page-tree sidebar, the tool toolbar, and the drawing canvas vertically, top to bottom, in a single `VerticalLayoutGroup`. The sidebar has no height cap or internal scrolling — its preferred height is the sum of every group/page row it contains (30px each) — so with enough pages it consumes most of the available vertical space before the canvas (which only gets whatever's left over via `flexibleHeight`) gets anything. Combined with the sidebar defaulting to expanded on load, the canvas routinely ends up squeezed into a small strip at the bottom, making it very hard to work with.

This redesign moves the sidebar to a permanent left column spanning the full height of the notes panel (VSCode/Notion-style), so the canvas always gets nearly all the vertical space regardless of how many pages exist.

## Layout

Current structure (`NotesRootBuilder.Awake()`, `notesAreaGO` is a single `VerticalLayoutGroup`):

```
notesAreaGO
  Sidebar   (unbounded height — the bug)
  Toolbar   (fixed height)
  Canvas    (flexibleHeight = 1, gets only the leftover)
```

New structure — `notesAreaGO` becomes a `HorizontalLayoutGroup` with two columns:

```
notesAreaGO (HorizontalLayoutGroup)
  SidebarColumn (fixed width, full height)
    Header (fixed height — collapse toggle)
    ScrollRect > Viewport > Content (page/group list)
  RightColumn (VerticalLayoutGroup, flexibleWidth = 1)
    Toolbar (fixed height, unchanged)
    Canvas  (flexibleHeight = 1 — now gets nearly all available height, independent of page count)
```

- The toolbar stays scoped to the canvas (right column only), not spanning the sidebar — it's a canvas-editing concept (tool selection), not page navigation.
- Sidebar width: fixed `200px` when expanded. Collapsing (same header-toggle-button mechanism that exists today) shrinks the whole column to a narrow `28px` strip (just enough for the toggle button), instead of only hiding the page list — reclaiming width for the canvas, mirroring the "canvas reclaims space when the tree isn't needed" intent already noted in the sidebar's existing doc comment.
- The page/group list is wrapped in a `ScrollRect` (`Viewport` using `RectMask2D`, no `Image` — this project's established pattern, see `MapEditorPanel.cs`'s scroll area — plus a `Content` child sized via `ContentSizeFitter`), so a long list scrolls internally instead of ever growing past the sidebar column's height.

## Components Touched

- **`NotesRootBuilder.cs`** — restructures `Awake()`'s hierarchy: `notesAreaGO` becomes the `HorizontalLayoutGroup`; a new `RightColumnGO` (`VerticalLayoutGroup`, `flexibleWidth = 1`) is inserted to hold the toolbar + canvas viewport, which currently parent directly under `notesAreaGO`.
- **`NotesTreeSidebar.cs`** — `Initialize()`'s root GameObject gets a `LayoutElement` with `preferredWidth` (200, or 28 when collapsed) instead of relying on the parent's `childForceExpandWidth`; `ToggleExpanded()` now also updates that `preferredWidth` (collapsing the whole column) instead of only toggling the list's `SetActive`; the "List" GameObject's plain `VerticalLayoutGroup` + `ContentSizeFitter` is replaced with a `ScrollRect`/`Viewport`/`Content` structure (matching `MapEditorPanel.cs`'s existing scroll-area pattern), with group/page rows built inside `Content` exactly as today.
- No other files change — `NotesToolbar.cs` and the canvas viewport (`CanvasController`/`CanvasInteractionController`) are unaffected; they just get reparented to `RightColumnGO` instead of `notesAreaGO` directly, with no changes to their own internals.

## Edge Cases

- Zero groups/pages: sidebar shows just the header + empty scroll area + "+ Группа" button; no different from today.
- Collapsed sidebar: canvas immediately reclaims the ~172px difference (200px → 28px); toggling back expands it again without losing scroll position or list content (list isn't rebuilt on toggle, only the column width and the list `GameObject`'s active state change, same as `ToggleExpanded()` does today for the list's visibility).
- Very long page list (many groups/pages): scrolls within the sidebar column via the new `ScrollRect`; never pushes into or shrinks the canvas.
- Window resize: sidebar keeps its fixed pixel width in both expanded/collapsed states (via `LayoutElement.preferredWidth`, not a fraction), consistent with how the toolbar row already uses a fixed `preferredHeight`. The canvas absorbs all remaining width exactly as it currently absorbs all remaining height.

## Out of Scope

- User-draggable/resizable sidebar width.
- Any change to the map/notes 2:1 screen split (separate, already-completed task).
- Any change to toolbar contents, canvas rendering, or POI/link functionality.
