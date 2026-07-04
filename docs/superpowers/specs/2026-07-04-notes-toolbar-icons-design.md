# Notes Editor — Toolbar Redesign + Icon Glyph Fix — Design

**Date:** 2026-07-04
**Status:** Approved, ready for planning
**Branch:** main

---

## Goal

The notes editor's tool toolbar (`NotesToolbar.cs`) currently renders as a row of opaque, fixed-color square buttons (36×36, colored background always visible) that reserves its own fixed-height strip above the canvas viewport. Two problems:

1. **Wasted space** — every button always shows a solid background square, and the row eats a dedicated horizontal strip out of the canvas's vertical space, even though the toolbar only has 5 buttons and is used briefly (pick a tool, then work on the canvas).
2. **Broken icon glyphs** — the procedurally-drawn icons in `NotesIconFactory.cs` don't read as their intended shapes. Confirmed via screenshot: the "Изображение" (Image) icon's mountain triangle is drawn peak-down/base-up (a literal coordinate bug — `peak.y` uses `min.y + ...` while `baseL/baseR.y` use `max.y - ...`, backwards for a texture where `y=0` is the bottom row). The cursor, pencil, and zoom icons were also confirmed to look wrong on screen, even though their coordinate math reads as directionally correct in isolation — most likely they just don't read as recognizable shapes at 32×32. There is no global render-side flip (the mountain renders exactly as its buggy coordinates compute, proving the display pipeline doesn't flip anything on its own).

This redesign (a) makes the toolbar a borderless floating overlay that doesn't consume its own layout row, and (b) rewrites all 5 icon glyphs with deliberately simple, verified-correct shapes.

## Icon Glyphs (`NotesIconFactory.cs`)

Rewrite all `Draw*` methods. Texture convention throughout: `y=0` is the bottom row, `y=size-1` is the top row (this is `Texture2D`'s documented convention, and the display pipeline doesn't flip it — confirmed empirically). Each method gets a one-line comment stating this convention next to its coordinate math, since the current bug is exactly a case of getting it backwards.

- **Cursor (Select)** — classic arrow/pointer shape: hotspot tip is the topmost-leftmost vertex, body flares down-right, small tail notch cut near the bottom-right. Rework so the "tip" (the point a user would associate with "this is where you click") is unambiguously the top-left vertex, not split across two same-x vertices as today.
- **Note** — unchanged: rectangle outline with a folded top-right corner. Already confirmed to read correctly.
- **Drawing (pencil)** — diagonal line, pointed tip at the bottom-right end, blunt end top-left. Recheck line length/angle proportions at 32×32 for legibility.
- **Image** — fix the confirmed bug: mountain peak near the top of the frame, base corners near the bottom (swap the current backwards min/max usage). Sun circle position is unchanged (already correct, near the top-left).
- **Zoom** — magnifying glass: lens ring upper-left, handle extending down-right out of the ring. Recheck radius/handle-length proportions for legibility at 32×32.

## Toolbar Visual Style (`NotesToolbar.cs`)

- Per-button background `Image` is no longer shown by default — buttons render as bare icons floating directly over the canvas.
- **Active tool**: a circular translucent backdrop appears behind the icon, using the existing `activeColor` (green).
- **Hover** (mouse over a non-active button, before click): a dimmer circular backdrop appears (soft white/gray, ~15% alpha), replacing today's "icon only, no feedback until the tooltip appears" behavior. The existing tooltip (label near cursor) is unchanged and still appears alongside the hover backdrop.
- Both backdrops are simple circle `Image`s sized to the button, toggled the same way `SetActive(NotesTool)` already toggles the active-color square today, plus a new hover-tracking check alongside the existing tooltip hover-tracking in `Update()`.

## Toolbar Layout (`NotesRootBuilder.cs` + `NotesToolbar.cs`)

Today, `RightColumn` is a `VerticalLayoutGroup` stacking `Toolbar` (fixed `preferredHeight`) then `CanvasViewport` (`flexibleHeight = 1`) — the toolbar always reserves its own horizontal strip.

New structure:

```
RightColumn
  CanvasViewport   (stretches to fill 100% of RightColumn — anchorMin (0,0), anchorMax (1,1))
  NotesToolbar     (floats on top: anchored to the top-left corner, fixed-size row,
                     parented AFTER CanvasViewport so it draws above it and still
                     receives clicks/hover)
```

- `RightColumn` stops being a plain top-to-bottom `VerticalLayoutGroup` stack for these two children — `CanvasViewport` now stretches to the full rect via anchors, and `NotesToolbar`'s row is a sibling positioned by anchors (top-left) rather than a layout-flow element. This is the same "floats over, doesn't reserve space" relationship the map/notes split already uses for other overlays in this codebase.
- Visually the toolbar stays in the same top-left spot it occupies today — only the layout mechanism changes (overlay instead of stacked row) and the canvas now gets the entire `RightColumn` height underneath it.
- No change to `CanvasController`/`CanvasInteractionController` internals — `viewportRect` still refers to the same `CanvasViewport` RectTransform, just resized to fill more space.

## Edge Cases

- Toolbar overlapping canvas content: acceptable — the toolbar only covers its own small top-left footprint (5 buttons × 36px + padding), same footprint it has today, just floating instead of pushing the canvas down.
- Hover and active state on the same button (cursor lingering over the currently-active tool): active backdrop takes precedence — no separate hover backdrop layered underneath it.
- Icon legibility at 32×32: if any redrawn icon still doesn't read clearly after this pass, that's a follow-up tweak (same manual Play-mode iteration pattern already used elsewhere in this project), not a blocker for this spec.

## Out of Scope

- Sidebar CRUD (rename/delete groups & pages) and search — separate spec, next in sequence.
- User-draggable/resizable panel splits (map/notes split, sidebar width) — separate spec, after CRUD/search.
- Any change to tool behavior itself (`CanvasInteractionController.SetTool`, drawing/note/image/link/zoom functionality) — purely visual + layout.
