# Notes Editor UX/UI Fixes — Design

**Date:** 2026-07-03
**Status:** Approved, ready for planning
**Branch:** `worktree-notes-editor`

---

## Goal

The notes editor built from the previous plan (2026-07-02) is visually broken and unusable: the toolbar (Курсор/Заметка/Связь/Рисунок/Изображение) renders as a cramped ~100×100px cluster with word-wrapped, unreadable button labels, and clicking Note/Link/Drawing/Image tools produces no visible effect. This spec fixes the root structural bug and does a visual pass so the panel is actually usable and matches the existing map-editor's dark/green theme.

---

## Root Cause (confirmed from a screenshot + code audit)

`NotesRootBuilder.Awake()` creates an intermediate wrapper GameObject for the toolbar:

```csharp
var toolbarRowGO = new GameObject("ToolbarRow");
toolbarRowGO.transform.SetParent(notesAreaGO.transform, false);
```

This GameObject never gets a `RectTransform` (no component that requires one is ever added to it) and it has no `LayoutElement`/`LayoutGroup`. Two consequences:

1. `notesAreaGO`'s `VerticalLayoutGroup` skips it when building `m_RectChildren` (no `RectTransform` to measure), so it doesn't reserve vertical space in the sidebar → toolbar → viewport stack.
2. `NotesToolbar.Initialize` parents its actual button row (`rowGO`, which *does* get a `RectTransform` via its own `HorizontalLayoutGroup`) under this broken wrapper. With no parent `LayoutGroup`/anchor-stretch driving its size, `rowGO` keeps Unity's bare-script-AddComponent default RectTransform values (`anchorMin=anchorMax=(0,0)`, `sizeDelta=(100,100)`) — a fixed 100×100px box, regardless of the actual panel width. Its `HorizontalLayoutGroup` then divides that ~100px among 5 buttons (~20px each), producing the cramped, wrapped labels seen in the screenshot.

Since the toolbar reserves no proper vertical space either, the sidebar/toolbar/viewport rows overlap rather than stack cleanly — matching the screenshot's overlapping green tree rows and misplaced black viewport.

**Why "click Note/Link/etc, nothing happens" is a symptom of the same bug:** the tool buttons *do* work (`SetTool` fires, `CanvasInteractionController.ActiveTool` changes — visible as the "Курсор" button highlighting green), but the canvas viewport itself is mis-sized/mis-positioned by the same broken layout cascade, so newly created cards/drawings likely render outside the clipped/visible area.

---

## Fix 1: Structural — remove the broken wrapper

- **`NotesRootBuilder.cs`**: delete the `toolbarRowGO` wrapper entirely. Call `NotesToolbar.Initialize(interaction, notesAreaGO.transform)` directly, so the toolbar row becomes a direct child of `notesAreaGO` (same level as the sidebar and viewport), correctly participating in its `VerticalLayoutGroup`.
- **`NotesTreeSidebar.cs`**: remove the dead `addGroupGO` GameObject (created, parented, then never used — the actual "+ Группа" button is added straight to `rootGO.transform` a few lines later).
- **`LinkView.cs`**: add `[RequireComponent(typeof(RectTransform))]` defensively, for the same reason `NoteCardView`/`ImageObjectView`/`DrawingObjectView` needed it (Task 12's fix) — LinkView's root GameObject currently has no explicit RectTransform either, and its two children (`lineRect`, `arrowRect`) are positioned via `anchoredPosition` values computed in `CanvasContainer`'s coordinate space, which only stays correct if LinkView's own rect aligns cleanly with its parent.

## Fix 2: Visual redesign — match the existing dark/green theme

Reuse the exact palette already used by `MapEditorPanel`/`PoiEditPanel`/`MapLegendUI`, instead of inventing new colors:

| Role | Color |
|---|---|
| Panel background | `(0, 0, 0, 0.7)` |
| Text | `Color.white` |
| Section header | `(0.7, 0.85, 1)` |
| Active/selected state | `(0.2, 0.55, 0.3)` (green) |
| Inactive state | `(0.3, 0.3, 0.3)` (gray) |
| Destructive action | `(0.55, 0.15, 0.15)` (red, matches "Удалить точку") |

**Toolbar (`NotesToolbar.cs`):** replace text labels with small glyph icons drawn at runtime into a `Texture2D` (same technique as `PoiPlaceholderFactory` — no external art assets): a cursor arrow (Select), a rectangle with a corner fold (Note), a line with an arrowhead (Link), a pencil (Drawing), a picture frame (Image). Buttons become fixed 36×36px squares. Add a simple hover-tooltip: a floating `Text` label (dark background, white text) that appears immediately on `IPointerEnterHandler` (no delay — keeps the implementation state-free) near the cursor, showing the Russian tool name, and disappears on `IPointerExitHandler`. Active tool keeps the existing green/gray highlight logic.

**Note cards (`NoteCardView.cs`):** replace the yellow sticky-note background with a dark card background (a shade lighter than the panel background, e.g. `(0.18, 0.18, 0.2, 0.95)`, so it reads as a distinct surface against the black canvas) and white/light text throughout (title + body), matching the rest of the app.

**Page tree (`NotesTreeSidebar.cs`):** increase row height from 18px to 30px and font size from 11pt to 13pt for both group and page rows. Page rows currently fake their left indent with three literal leading spaces baked into the label string (`"   • {page.Name}"`) and no actual rect padding (`textRect.offsetMin`/`offsetMax` both zero) — replace this with real padding via `textRect.offsetMin = new Vector2(16f, 0f)` (matching the header row's existing `offsetMin = (6, 0)` pattern) and drop the leading spaces from the string, keeping just `"• {page.Name}"`.

**Delete-confirm dialog (`NotesUndoManager.cs`):** restyle the panel background, message text, and the two buttons ("Отмена"/"Удалить") to use the shared palette above instead of the current ad-hoc gray tones — "Удалить" becomes the destructive-red, "Отмена" the neutral gray.

---

## Out of Scope

- Resizable/draggable split between map and notes areas (fixed 2:1 stays).
- Any change to interaction logic (tool routing, painting, linking) — this spec is visual/structural only, not behavioral.
- Animated transitions, custom fonts, or icon packs — glyphs stay simple runtime-drawn shapes matching the project's existing placeholder-icon convention.
