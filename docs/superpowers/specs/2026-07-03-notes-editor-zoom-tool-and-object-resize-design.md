# Notes Editor — Zoom Tool + Object Resize — Design

**Date:** 2026-07-03
**Status:** Approved, ready for planning
**Branch:** `worktree-notes-editor-links`

---

## Goal

Two small, independent additions to the notes editor canvas, both raised during manual testing of the Link tool redesign:

1. A dedicated **Zoom tool** — click-and-drag, Photoshop-style, as an alternative to the existing scroll-wheel zoom.
2. **Resizing** canvas objects (note cards, images, drawings) via corner drag handles, shown when an object is selected.

Both ship in the same branch as the Link tool work, after it. Neither touches the Link tool itself except where resizing needs to keep attached links correctly anchored (already-existing `RefreshLinksFor` mechanism).

---

## Part 1: Zoom Tool

### Interaction Model

- New toolbar tool, `NotesTool.Zoom`, with a magnifying-glass icon — 6th button, added after Изображение.
- Press and hold over the canvas: dragging the mouse **right** zooms in, **left** zooms out, continuously while the button is held, proportional to horizontal drag distance from the press point.
- The screen point where the press started stays visually fixed while zooming (matching Photoshop's zoom tool) — the view zooms "around" that point, not around the viewport center.
- Releasing the mouse ends the zoom drag. No click-only (tap) behavior is defined beyond "zero drag distance = no visible change."
- Same zoom range as the existing scroll-wheel zoom: clamped to `[0.25, 3.0]`.
- Not undoable, consistent with existing pan/scroll-zoom (`NotesCanvasController.Pan`/`Zoom` only call `SaveCameraState()`, no undo command).

### Technical Approach

- `NotesCanvasController.Zoom(float scrollDelta, Vector2 screenPivot)` currently ignores `screenPivot` — it always scales `CanvasContainer.localScale` in place, which visually zooms around the viewport center (since `CanvasContainer` is itself centered in the viewport). This method is left untouched; scroll-wheel zoom keeps its current center-pivot behavior.
- A new method, `NotesCanvasController.ZoomAroundScreenPoint(float newScale, Vector2 screenPos, Camera uiCamera)`, is added for the drag-to-zoom gesture:
  - Converts `screenPos` to a local point in `CanvasContainer` *before* the scale changes.
  - Applies `newScale` (clamped to `[0.25, 3.0]`) to `CanvasContainer.localScale`.
  - Re-converts the same `screenPos` to a local point *after* the scale change, and shifts `CanvasContainer.anchoredPosition` by the difference — keeping the world point under `screenPos` visually stationary.
- `CanvasInteractionController` gains: a press-start screen position and starting zoom level (recorded in `HandlePress` for the `Zoom` tool case), and a per-frame drag handler (alongside the existing `HandlePan`/`HandlePaintDrag` frame-polling pattern in `Update()`) that computes `newScale = startZoom + (currentScreenPos.x - startScreenPos.x) * sensitivity` and calls `ZoomAroundScreenPoint`.

---

## Part 2: Object Resize

### Interaction Model

- When an object (note card, image, or drawing) is selected via the Курсор tool, 4 small square handles appear at its corners — same visual language as the existing link anchor dots, but only shown for the **selected** object, not on hover.
- Dragging a corner handle freely resizes the object (width and height independently, no aspect lock); the opposite corner stays fixed as the drag anchor.
- A minimum size (40×40 canvas units) prevents collapsing an object to zero or negative size.
- For images and drawings, resizing only changes the **display size** (the object's on-canvas rect) — it does not resample the underlying pixel data (`ImageObjectData.ImageBytes` / `DrawingObjectData.PixelDataPng` stay as-is; the raster is just stretched, exactly like `RawImage`/`Image` scaling already behaves for any size other than native resolution). Re-resampling the actual paintable pixel grid is out of scope.
- On drag end, the resize is pushed to the undo stack (symmetric to the existing move undo), and any links attached to the object are refreshed via the existing `NotesCanvasController.RefreshLinksFor`.
- Handles counteract canvas zoom to stay a constant screen size, using the same technique just added for link anchor dots/the bend handle.

### Technical Approach

- New component `ObjectResizeController`, attached alongside each object view by `NotesCanvasController.SpawnView` (parallel to how `AddLinkAnchors` wires up `LinkAnchorController`).
- Visibility driven by `CanvasInteractionController.selectedObjectId` — `NotesCanvasController` needs a way to tell resize controllers when selection changes; simplest is a new `NotesCanvasController.SetSelectedObject(string objectId)` (called from `CanvasInteractionController` wherever `selectedObjectId` is currently set) that loops over tracked `ObjectResizeController`s and toggles each one's handles, mirroring `SetSelectedLink`.
- Dragging a handle updates the object's `CanvasObjectData.Size` (and repositions `Position` so the opposite corner stays put, since `Position` is the object's *center*), then calls the object view's `Refresh()` and `NotesCanvasController.RefreshLinksFor(objectId)` live during the drag (so it feels responsive), with the undo push happening once on release (old size/position vs new, symmetric to `MoveCommand`).
- A new `NotesUndoManager` command, `ResizeCommand`, storing old `Position`+`Size` and restoring both on `Undo()`.

---

## Components Touched

- **`NotesCanvasController.cs`** — `ZoomAroundScreenPoint`, `SetSelectedObject`, wiring `ObjectResizeController` into `SpawnView`/cleanup (mirroring the `linkAnchors` dictionary pattern), tracking dictionary for resize controllers.
- **`CanvasInteractionController.cs`** — `NotesTool.Zoom` enum value, press/drag handling for the zoom gesture, calling `SetSelectedObject` wherever `selectedObjectId` changes.
- **`NotesToolbar.cs` / `NotesIconFactory.cs`** — new Zoom tool button + magnifying-glass icon.
- **New file `ObjectResizeController.cs`** — corner handles, drag-to-resize, mirrors `LinkAnchorController`'s structure (per-object component, 4 handles, zoom-counter-scaling).
- **`NotesUndoManager.cs`** — `ResizeCommand`, `PushResize` (or similar), matching the existing `PushMove` pattern.

## Edge Cases

- Resizing below the 40×40 minimum: drag is clamped, doesn't go smaller.
- Resizing an object with links attached: links reflow live during the drag, not just on release.
- Zoom-dragging past the `[0.25, 3.0]` clamp: stops at the clamp, further drag in the same direction has no additional effect (matches existing scroll-wheel clamp behavior).
- Switching tools mid-zoom-drag or mid-resize-drag isn't specifically handled — both gestures only run while their originating tool/selection stays active and the mouse button is held, consistent with how existing drag gestures (pan, paint) behave.

## Out of Scope

- Aspect-ratio-locked resize (e.g. Shift+drag).
- Resampling actual pixel content for images/drawings on resize.
- Undo for zoom/pan (matches existing behavior).
- Any change to the Link tool itself beyond the already-existing `RefreshLinksFor` call.
