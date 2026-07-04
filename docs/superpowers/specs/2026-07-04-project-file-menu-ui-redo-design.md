# Project File Menu UI — Redo Design

**Date:** 2026-07-04
**Status:** Approved, ready for implementation
**Branch:** implement off `main` (project has no separate feature branches / no git remote)
**Supersedes:** the UI portion (Tasks 5–6) of `docs/superpowers/plans/2026-07-04-project-save-export-import.md`. The data layer (Tasks 1–4 of that plan — `ProjectSerializer`, `CanvasObjectDataConverter`, `ProjectSaveData`, `WorldMapRenderer.LoadFromCells`, `PoiManager.LoadPois`, `NotesDocumentController.LoadDocument`, and the POI custom-icon-bytes work) is unaffected and stays exactly as built.

---

## Why this redo

The original UI attempt (a persistent top menu bar that reserved a new 32px screen strip by shrinking `notesAreaRoot`'s anchors and the map camera's viewport rect via `NotesLayoutController.Apply()`, plus matching offset changes in `MapEditorPanel.cs` and `MapLegendUI.cs`) produced a visual bug: a duplicate-looking sliver of UI content appeared right at the boundary of the reserved strip, persisting across clean Play-mode restarts and window resizes. Extensive diagnosis ruled out duplicate GameObjects (confirmed via Hierarchy search — exactly one of each relevant component/canvas existed) and confirmed correct RectTransform geometry, correct Canvas settings, and no exceptions. The root cause was not conclusively identified. Rather than continue diagnosing a specific Unity rendering quirk, the UI was rolled back (`git reset --hard` to the commit ending Task 4) and is being redesigned to avoid the specific mechanism suspected of triggering it — reserving space by reshaping `NotesLayoutController`'s anchor/camera-rect math.

---

## Design

**Placement:** A full-width, 20px-tall bar pinned to the absolute top of the screen (screen-space y = 0 to 20). This height was chosen because it exactly matches the top margin `MapEditorPanel` and `MapLegendUI` already assume (both use `panelAnchoredPosition.y = -20`) — the bar fits inside a margin that already exists on the map side, needing zero changes to either file.

**Notes-side accommodation:** The notes side (`NotesToolbar`, `NotesTreeSidebar`) currently has no top margin — both render flush against the top of `notesAreaGO`. To avoid the bar covering them, `NotesRootBuilder.cs`'s existing `HorizontalLayoutGroup` on `notesAreaGO` gets one added line: `hLayout.padding = new RectOffset(0, 0, 20, 0);`. This shifts `NotesTreeSidebar` and `RightColumn` (and therefore `NotesToolbar`, which is anchored relative to `RightColumn`) down by 20px within `notesAreaGO`'s own already-fixed rect — no change to `notesAreaRoot`'s anchors, no change to the map camera's viewport rect, no change to `NotesLayoutController.cs` at all.

**Files deliberately left untouched this time:** `NotesLayoutController.cs`, `MapEditorPanel.cs`, `MapLegendUI.cs` — the three files modified by the previous (buggy) attempt.

**File menu contents and mechanics:** Unchanged from the original design — a "Файл" button on the left of the bar toggles a popup listing Сохранить / Сохранить как… / Открыть… / Открыть последние (with an in-place expandable list of up to 5 recent paths, backed by `RecentProjectsList`, a `PlayerPrefs`-based store). The popup/backdrop mechanism (backdrop click-to-close, "later sibling wins raycasts" ordering) is reused verbatim from the previous attempt — it was never implicated in the visual bug, only the layout-reservation mechanism was.

**Data layer:** Reused exactly as-is from the original plan: `ProjectSerializer.Save/Load`, `ProjectLoadResult`, `WorldMapRenderer.LoadFromCells`/`LastGenParams`, `PoiManager.LoadPois`, `NotesDocumentController.LoadDocument`. These were never rolled back and remain committed and self-test-verified.

**Rebuilt from scratch (content unchanged from before, only the bar's own height constant changes from 32 to 20, and there is no shared `MenuBarHeightPixels` constant to keep in sync with other files anymore):** `ProjectMenuBar.cs`, `RecentProjectsList.cs`, `ConfirmDialog.ShowInfo` (the single-button info dialog, added as a small refactor of `ConfirmDialog.cs`'s existing `Show` method into a shared `BuildBasePanel` helper).

---

## Testing

Same as the original plan: `[ContextMenu("Self-Test: ...")]` methods run manually in the Unity Editor, plus manual Play-mode verification of the full save → restart → load round trip through the actual "Файл" menu.

---

## Out of scope

Everything listed as out-of-scope in the original design (`docs/superpowers/specs/2026-07-04-project-save-export-import-design.md`) still applies: autosave, multiple simultaneously-open projects, image/PNG map export, cross-tool export formats, `FormatVersion` migration logic.
