# Map/Notes Split — Single Source of Truth — Design

**Date:** 2026-07-03
**Status:** Approved, ready for planning
**Branch:** `main`

---

## Goal

The 2:1 screen split between the 3D map and the notes editor is currently held together by three independent, unsynchronized values instead of one architectural source of truth:

- `NotesLayoutController.splitFraction` (`Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs:21`) — drives the map camera's `Camera.rect` and the notes area's anchors. Inspector default `2f/3f`.
- `MapLegendUI.rightBoundaryFraction` (`Assets/WorldGen/Rendering/MapLegendUI.cs:26`) — C# default `1f` (full screen!); the scene's actual serialized value (`0.6666667`) only matches the split by a developer having manually typed it into the Inspector at some point.
- `PoiEditPanel.rightBoundaryFraction` (`Assets/WorldGen/Rendering/PoiEditPanel.cs:33`) — same pattern as the legend panel, same manual-duplicate scene value.

Nothing keeps these three in sync. Changing `NotesLayoutController.splitFraction` today silently desyncs the map camera viewport from the legend/POI panels, with no compile-time or runtime signal that anything is wrong.

Separately, `NotesLayoutController.mapAreaRoot` / `NotesRootBuilder.mapAreaRoot` — intended (per their existing doc comments) as the shared anchor for the map's 2D UI — are unassigned in the scene and unused by any code. Making them real would require unifying `MapEditorPanel`/`MapLegendUI`/`PoiEditPanel`'s independent Screen Space - Overlay Canvases into one shared Canvas/RectTransform hierarchy — a larger restructuring explicitly out of scope here.

This is an architecture-only fix: same visual result (fixed 2:1 split, not user-resizable), but one authoritative value instead of three, and no misleading dead scaffolding.

## Design

- `NotesLayoutController` gains `public const float SplitFraction = 2f / 3f;`. Its existing `splitFraction` Inspector field is removed; `Apply()` uses the constant directly.
- `MapLegendUI.rightBoundaryFraction` and `PoiEditPanel.rightBoundaryFraction` fields are removed entirely (including their scene-serialized override values). Their anchor-setup code (`panelRect.anchorMin = new Vector2(rightBoundaryFraction, 1f)`, and the matching `anchorMax` line) reads `NotesLayoutController.SplitFraction` directly instead.
- Using a compile-time `const` (rather than a runtime-assigned static field) sidesteps a real ordering hazard: `MapLegendUI`/`PoiEditPanel` apply their boundary fraction inside their own `Awake()`, and Unity does not guarantee `NotesLayoutController`'s `Awake()` (which currently would be the one setting a runtime value) runs first across unrelated GameObjects. A `const` is available immediately, regardless of script execution order.
- `NotesLayoutController.mapAreaRoot` and `NotesRootBuilder.mapAreaRoot` (and their Inspector tooltips referencing it) are removed, since they have no effect today and making them meaningful is out of scope.
- No change to visual behavior: the split stays fixed at 2/3, camera viewport and both panels stay in lockstep because they now all read the same constant.

## Verification

After the change, temporarily editing `NotesLayoutController.SplitFraction` to a different value and entering Play mode should move the map camera viewport, the legend panel, and the POI edit panel together, with no scene data to touch. Revert to `2f / 3f` afterward (or leave changed, if ever actually desired — but that's a separate decision, not part of this fix).

## Out of Scope

- Any user-facing resizable/draggable/collapsible split control.
- Unifying the map's UI Canvases under a real `mapAreaRoot` hierarchy.
- Redesigning the notes editor's internal layout (sidebar/toolbar/canvas proportions) — raised separately during this brainstorm, tracked as its own follow-up topic.
