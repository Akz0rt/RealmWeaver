# Main Screen Redesign (Shell: Menu, Toolbar, Zoom/Pan, Layers, Legend) — Design

**Date:** 2026-07-06
**Status:** Approved, ready for implementation planning
**Branch:** implement off `main` (project has no separate feature branches/worktrees)

---

## Goal

Bring Screen A ("Главный экран") from `design_handoff_realmweaver_ui/README.md` into the
existing runtime-built `UnityEngine.UI` codebase. This is the first of four sub-projects
covering the remaining UI redesign work (order: **A. Main screen → C. Editor-brush panel →
D. POI screen → F. Modals**); theme system + Generation/Progress screens already shipped.

This spec covers only the **shell** around the map: top menu, a new toolbar strip, real
map zoom/pan, the Layers panel, and repositioning the Legend. It deliberately does **not**
redesign what lives inside the Редактор or Точки tabs' content panels — those are
Screen C's and Screen D's specs respectively. This phase extracts their content out of the
current combined `MapEditorPanel` into standalone panel classes **unchanged**, purely so
the new toolbar/tab-docking mechanism has somewhere to attach them; C and D will later
replace those panels' internals wholesale.

---

## Current State (confirmed via code exploration, 2026-07-06)

- `ProjectMenuBar.cs`: 20px bar, single "Файл" button + dropdown (Сохранить/Сохранить как/Открыть/Открыть последние/тема). No logo, no "Правка"/"Вид", no project-name display.
- `MapEditorPanel.cs`: one 300px floating panel (top-left, `anchoredPosition = (20,-20)`) with 3 tabs (Карта/Редактор/Точки) built as internal `GameObject`s, switched via `SetTab(int)`; height auto-fits active tab via `RefreshPanelLayout()`.
  - Карта tab (`BuildMapTab`): 4 layer toggles (Рельеф/Биом-климат/Границы регионов/Береговая линия).
  - Редактор tab (`BuildEditorTab`): 2-way mode segment (Selection & Override / Brush), each with its own sub-panel (`selectionPanelRoot` / `brushPanelRoot`).
  - Точки tab (`BuildPoiTab`): count spinner + 3 bulk-action buttons (Сгенерировать/Добавить одну/Очистить все) — no list, no search/filter.
- `MapLegendUI.cs`: floating panel anchored **top-right** of the map area.
- `WorldMapRenderer.cs`: `targetCamera` is Orthographic (confirmed in `SampleScene.unity`, size 500), positioned by `PositionCameraOverMap()` (`WorldMapRenderer.cs:661-669`) unconditionally on every regenerate/load. No zoom/pan capability exists anywhere in the map-rendering code.
- Mouse-drag gestures on the map are already fully claimed: `CellSelectionController` (selection-paint drag), `BrushToolController` (brush-paint drag), `PoiInteractionController` (POI drag-to-move). Cell hit-testing is `Camera.ScreenPointToRay` + `MeshCollider` (`WorldMapRenderer.GetCellUnderRay`), which keeps working unmodified as the camera moves/zooms.

---

## Scope

**In scope:**
1. `ProjectMenuBar.cs` grows to 40px: logo square + "REALMWEAVER" wordmark (left), "Файл" (functional, unchanged), "Правка"/"Вид" (inert visual placeholders — muted, no click behavior), project `.dndproj` name (right, reusing the path `ProjectMenuBar` already tracks for Save/Save As).
2. New `MapToolbarUI.cs` (46px strip, below the menu bar, above the map viewport): tab-segment (Карта/Редактор/Точки, moved out of `MapEditorPanel`) + zoom controls (`−` / `100%` / `+` / "По размеру"). Owns which of the three tab panels (`MapLayersPanel`, `EditorBrushPanel`, `PoiToolPanel`) is currently active via `SetActive`, mirroring the mutual-exclusion pattern already used by `MapScreenController`.
3. New `MapCameraController.cs`: real orthographic zoom (scroll wheel + toolbar buttons) and pan (**right-mouse-drag**, confirmed — doesn't collide with any existing left-drag gesture). Clamped: min `orthographicSize` = fully zoomed in; max = the map's natural fit size (same value `PositionCameraOverMap()` computes today) — can't zoom out past the original framing. "100%" and "По размеру" both reset to the natural-fit size/position. Pan is clamped so the viewport can't drift entirely off the map. `WorldMapRenderer.PositionCameraOverMap()` is changed to only run on first placement (guard flag), so it no longer stomps zoom/pan state on every regenerate/load.
4. New `MapLayersPanel.cs`: extracted from `MapEditorPanel.BuildMapTab`, same 4 toggles, repositioned top-left (216px) under the new toolbar strip, restyled to current theme spacing tokens.
5. `MapLegendUI.cs` repositioned from top-right to **bottom-left** (232px), matching the mockup. Content/behavior unchanged.
6. New `EditorBrushPanel.cs` and `PoiToolPanel.cs`: mechanical extraction of `MapEditorPanel.BuildEditorTab` / `BuildPoiTab`'s existing content into their own standalone panel classes, **functionally unchanged** — repositioned to dock under the new toolbar strip, restyled to spacing tokens only. `MapEditorPanel.cs` is retired (deleted) once its three tabs' content has been absorbed into `MapLayersPanel` / `EditorBrushPanel` / `PoiToolPanel`.

**Out of scope (this phase):**
- Any functional redesign of the Редактор tab's brush/selection tools — Screen C's spec.
- Any functional redesign of the Точки tab / POI list / `PoiEditPanel` — Screen D's spec.
- Modal restyling — Screen F's spec.
- Persisting zoom/pan across sessions or into `.dndproj` (view chrome, resets to fit-to-map on regenerate/load — confirmed this session-only behavior with the user).
- Giving "Правка"/"Вид" any actual menu contents (no feature exists yet to put in them).
- Windowed/resizable display mode (separate queued item, unaffected by this phase).

---

## Design

### Top menu bar (`ProjectMenuBar.cs`)
Height 20px → 40px. New left-side content before "Файл": a 16×16 `Image` tagged `ThemeRole.Accent` (logo square) + `Text` "REALMWEAVER" (13px, bold, uppercase — legacy `Text` has no letter-spacing/tracking support, so the mockup's `.14em` tracking is skipped, plain bold uppercase is close enough per the handoff doc's own allowance for minor typographic liberties). Separator (1px `Border`-role line). Then existing "Файл" button (restyled: active tab visually `Elev` background per mockup). Then two new inert labels "Правка" and "Вид" (`Mut` role text, no `Button` component — not clickable, no dropdown). Right-aligned: existing project-path text (reuse whatever `ProjectMenuBar` already tracks for the window/Save-As title), `Mut`, 12px.

### Toolbar strip (`MapToolbarUI.cs`, new)
46px tall, full width, anchored directly below the (now 40px) menu bar. Left: tab-segment container (`Bg` role, `Border` outline, radius-approximated via a rounded sprite or plain rect per existing project convention, padding 3) with 3 buttons Карта/Редактор/Точки — active = `Accent`/`AccentInk`, inactive = `Mut` text on transparent. Clicking a tab calls `SetActiveTab(int)`, which `SetActive(true/false)`s the corresponding panel GameObject (`mapLayersPanel`, `editorBrushPanel`, `poiToolPanel` — Inspector-assigned references) and updates segment colors, mirroring `MapScreenController`'s existing mutual-exclusion style. Right: zoom controls — `−`/`+` icon buttons (30×30, `Elev`, border), a "100%" label/button (live-updates to show current zoom as a percentage of the natural-fit size; click resets zoom), and "По размеру" (secondary button, same reset action, kept separate for discoverability per the mockup). All zoom actions call into `MapCameraController`.

### Real camera zoom/pan (`MapCameraController.cs`, new)
Attached alongside `WorldMapRenderer` (same GameObject as the map camera, or referencing `targetCamera`). Tracks `float naturalFitSize` (computed once, same formula `PositionCameraOverMap()` uses) and `float minSize` (a fixed fraction of natural-fit, e.g. 15%, tuned during implementation).
- **Scroll wheel** (when cursor is over the map viewport, not over a floating panel): adjusts `camera.orthographicSize`, clamped `[minSize, naturalFitSize]`.
- **Toolbar `−`/`+`**: step the same value by a fixed increment.
- **"100%" / "По размеру"**: reset `orthographicSize = naturalFitSize` and camera position to the original centered position.
- **Right-mouse-drag**: translates the camera along its local X/Z plane proportionally to mouse delta and current `orthographicSize` (so pan speed feels consistent at any zoom level); clamped so the visible viewport can't move entirely past the map's bounds (e.g. center position clamped to the map's rect expanded by some margin).
- `WorldMapRenderer.PositionCameraOverMap()` gets a guard (e.g. `bool cameraPlacedOnce`) so it only runs the very first time a map is generated/loaded in a session — subsequent regenerations/loads do NOT reset an in-progress zoom/pan.

### Layers panel (`MapLayersPanel.cs`, new)
Mechanical extraction of `MapEditorPanel.BuildMapTab`'s 4 toggle rows (Рельеф/Биом-климат/Границы регионов/Береговая линия) — same `WorldMapRenderer` setter calls, same toggle behavior. Repositioned: floating top-left, 216px wide, anchored below the new toolbar strip (not below the old 20px menu bar).

### Legend (`MapLegendUI.cs`)
Anchor changed from top-right to bottom-left, width fixed at 232px per the mockup. No content/behavior changes.

### Editor/POI tab panels (`EditorBrushPanel.cs`, `PoiToolPanel.cs`, new — mechanical extraction only)
Straight copy of `MapEditorPanel.BuildEditorTab` (both `selectionPanelRoot` and `brushPanelRoot` sub-modes, `EditorMode` enum, all slider/dropdown/button wiring) into `EditorBrushPanel.cs`, and `BuildPoiTab` (count spinner + 3 bulk buttons) into `PoiToolPanel.cs`. No behavior changes in this phase — these exist purely so `MapToolbarUI` has something to dock and `SetActive`, keeping the app fully functional between this phase and Screens C/D landing. Positioned to match where `MapEditorPanel` used to sit (top-left docking area), since their real repositioning per the mockup (Кисть panel 264px, POI panel 262px) is Screen C's/D's concern.

`MapEditorPanel.cs` itself is deleted once the three extractions are wired and verified working.

---

## Error Handling

No new error paths. Camera clamps are pure math (min/max, no failure mode). Tab switching and zoom/pan can't fail in a way requiring user-facing error handling.

---

## Testing

Established project convention — `[ContextMenu("Self-Test: ...")]` plus manual Play-mode verification:
- **Self-Test: Camera Clamp** — programmatically set `orthographicSize` below `minSize` and above `naturalFitSize`, assert `MapCameraController` clamps both back into range.
- **Manual:** scroll-zoom and toolbar buttons zoom the map in/out correctly; "100%"/"По размеру" reset view; right-mouse-drag pans without breaking cell click/brush/POI-drag interactions; regenerating/loading a map does NOT reset an in-progress zoom/pan; switching Карта/Редактор/Точки tabs shows the correct panel and hides the others; Legend renders bottom-left; menu bar shows logo/wordmark/Правка/Вид (inert)/project name.

---

## Out of Scope (this phase)

- Editor-brush panel functional redesign (Screen C).
- POI list/edit panel functional redesign (Screen D).
- Modal restyling (Screen F).
- Zoom/pan persistence.
- Windowed/resizable display mode.
