# Notes Editor — Link Tool Redesign (Bezier + Anchors) — Design

**Date:** 2026-07-03
**Status:** Approved, ready for planning
**Branch:** `worktree-notes-editor-links`

---

## Goal

Replace the notes editor's current "Связь" (Link) tool — a separate toolbar mode where you click one object then another to create a straight line between their centers — with a direct-manipulation gesture available under any tool: drag from a small anchor point on an object's edge to another object, drawing a curved (not straight) connector. This is one of three UX items deferred during the 2026-07-03 notes-editor stabilization work; the other two (map/notes 2:1 split redesign, gating editor UI behind world generation) are out of scope here.

---

## Interaction Model

- The "Связь" tool is removed from `NotesToolbar` entirely. Four tools remain: Курсор, Заметка, Рисунок, Изображение. Link creation is no longer a mode — it's always available, the same way object dragging always is.
- Hovering any canvas object (note card, image, drawing) reveals four small anchor dots at the midpoints of its top/bottom/left/right edges (matching the Miro/draw.io convention).
- Pressing and dragging from one of these dots starts a link-creation drag, regardless of the currently active tool. A live rubber-band preview curve follows the cursor while dragging.
- Releasing over another object creates a `LinkData` between the source and target object. Releasing over empty canvas cancels — no link is created, nothing is pushed to the undo stack.
- Releasing on the same object the drag started from (self-link) is rejected, same as the existing `AddLink` guard (`fromObjectId == toObjectId`).

## Data Model & Curve Rendering

- `LinkData` (`Assets/WorldGen/Notes/Data/NotesData.cs`) gains one new field: `System.Numerics.Vector2? ControlPointOffset` (nullable, matching the `System.Numerics.Vector2` type already used elsewhere in this file). It stores the curve's bend point as an offset from the straight-line midpoint between the two objects' current anchor points, in canvas units.
  - `null`/unset: the curve computes a pleasant automatic bend (a perpendicular bulge whose direction/magnitude is derived from the two anchor sides, e.g. a small S-curve when the anchors are opposite sides).
  - Set (once the user has dragged the bend handle): that explicit offset is used instead of the automatic one.
- **Anchor side is never stored.** Which side (N/S/E/W) each end of the link attaches to is recomputed every frame from the two objects' current relative positions (nearest-side heuristic), so a link always looks correct after either connected object moves — it never needs to be "fixed up."
- `LinkView.cs` is rewritten:
  - Computes the two anchor points (one per connected object, on the nearest side to the other object) and the control point (`midpoint + ControlPointOffset`, or the automatic bulge if unset).
  - Samples the resulting quadratic Bezier at a fixed number of segments (~12–16) and renders each segment as a short rotated `Image` rectangle chained end-to-end — the same "rotated Image" technique the current straight-line implementation already uses, just repeated per segment instead of once. This was chosen over a custom mesh-based `Graphic` (`OnPopulateMesh`) as unnecessary complexity for this project's scale and conventions; the segmented approach also gives click hit-testing "for free" (see below).
  - When the link is selected or hovered, shows a draggable circular handle at the current control point; dragging it sets `ControlPointOffset` explicitly.

## Selection & Deletion

- Clicking on or near any segment of a link's curve selects that link (parallel to how object selection already works via `selectedObjectId`; links get an analogous `selectedLinkId`).
- Delete key on a selected link removes just that link, independent of its endpoint objects — this is new: currently a link can only be removed indirectly, as a side effect of deleting one of its endpoint objects (`NotesCanvasController.RemoveObject` cascades to `RemoveLink` for orphaned links; that cascade is unchanged).
- Deleting an object that has a currently-selected link clears the link selection too (parallel to the existing `OnSelectionCleared` handling for object selection).

## Components Touched

- **`NotesData.cs`** — `LinkData` + `ControlPointOffset`.
- **`LinkView.cs`** — rewritten: segmented Bezier rendering, per-segment hit-testing, draggable control-point handle.
- **New component (working name `LinkAnchorController`)** — attached alongside each object view (`NoteCardView`/`ImageObjectView`/`DrawingObjectView`) by `NotesCanvasController.SpawnView`. Owns the four hover-revealed anchor dots and the drag-to-create-link gesture. Implemented as a standalone component rather than a shared base class for the three view types — the view types don't currently share a base class, and introducing one is a larger refactor not needed just for this feature.
- **`CanvasInteractionController.cs`** — remove the `NotesTool.Link` branch in `HandleObjectClicked` and the `linkDragSourceId` field (superseded by the anchor-drag gesture); add `selectedLinkId` tracking and Delete-key handling for links, analogous to the existing object selection/delete flow.
- **`NotesToolbar.cs` / `NotesIconFactory.cs`** — remove the "Связь" button and its icon.
- **`NotesTool` enum** (`CanvasInteractionController.cs`) — remove the `Link` value (after confirming no other remaining usages).

## Edge Cases

- Self-link (drag released back onto the same object it started from): rejected, no link created.
- Drag released over empty canvas: cancelled, no link created, no undo entry pushed.
- Multiple links between the same pair of objects: allowed, unchanged from current behavior (not something this redesign touches).
- Deleting an object with a selected link attached: link selection is cleared along with object selection.

---

## Out of Scope

- Map/notes 2:1 split redesign and world-generation UI gating — separate deferred items, tracked independently.
- Multi-select or bulk link operations.
- Changing how links render when zoomed/panned beyond what already applies to `CanvasContainer`'s existing scale/offset transform.
- Any change to note card / image / drawing object creation or editing behavior.
