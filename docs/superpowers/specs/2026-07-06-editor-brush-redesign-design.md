# Editor-Brush Panel Redesign (Screen C) — Design

**Date:** 2026-07-06
**Status:** Approved, ready for implementation planning
**Branch:** implement off `main`

---

## Goal

Bring Screen C ("Редактор — кисти") from `design_handoff_realmweaver_ui/README.md` into
`EditorBrushPanel.cs` (created as a mechanical extraction in the Main-screen shell phase,
[[2026-07-06-main-screen-redesign-design.md]]). Second of four sub-projects (A→**C**→D→F).

Unlike the shell phase, this is real new functionality: a radius-based multi-cell paint
brush with Raise/Lower/Smooth modes and a Biome target, replacing the current single-cell
step-brush as the panel's default/primary mode.

---

## Current State

- `BrushToolController.cs`: paints exactly the single cell under the cursor (`PaintAtCursor`, `BrushToolController.cs:95-122`), 3 targets (`BrushTool.Elevation/Temperature/Moisture`, no Biome), a `+`/`-` direction toggle + one `brushStep` slider (no radius, no shape, no Smooth mode). One full stroke (press→release) = one undo entry (`BeginBrushStroke`/`EndBrushStroke`), Ctrl+Z undoes the single most recent stroke only, no "undo everything" action.
- `VoronoiCell.cs`: `Site` (`Vector2`, world-space position), `NeighborIds` (`List<int>`, adjacency via Delaunay/Voronoi edges) — sufficient for both radius queries (Euclidean distance from cursor to `Site`) and Smooth mode (averaging with neighbors), no new geometry needed.
- The existing "Выбор и override" mode (multi-cell selection + exact-value slider/dropdown assignment for climate/elevation/water-status/biome) has no equivalent in the mockup, but provides precise value-setting the new relative-delta brush can't replicate — **kept alongside** the new brush per this session's decision, reachable via a small toggle not present in the mockup.

---

## Scope

**In scope:**
1. Small mode toggle (not in the mockup) atop `EditorBrushPanel.cs`: **Кисть** (new, default) / **Точное выделение** (existing `selectionPanelRoot`, unchanged).
2. New **Кисть** UI, matching the mockup:
   - **Что редактируем** — 2×2 radio grid: Высота / Температура / Влажность / **Биом** (new).
   - **Режим** — segment: Поднять / Опустить / **Сгладить** (new) — hidden when target = Биом.
   - **Форма** — 2 icon buttons: круг (default) / квадрат.
   - **Размер** — slider, brush radius in map/world units (same space as `VoronoiCell.Site`); labeled "px" in the UI to match the mockup's copy even though it isn't screen pixels — a fixed world-unit radius will visibly scale with the new camera zoom from Screen A, which is expected/standard for this kind of tool.
   - **Сила** — slider 0–100%, scales delta-per-tick for Raise/Lower/Smooth; unused for Биом target.
   - Footer: **"Отменить всё"** (new) — empties the entire brush undo stack for the session in one action, no confirmation dialog.
3. New **contextual biome palette** (bottom-left, 264px, shown only when target = Биом): clickable grid of biome swatches (reusing `RegionColorPalette`'s colors — unlike the read-only `MapLegendUI`, these are selectable). A biome must be selected before painting is active.
4. `BrushToolController.cs` rewritten to support: radius-based cell queries (circle = Euclidean distance from cursor hit point to `Site` ≤ radius; square = axis-aligned box test), Raise/Lower (existing delta mechanism, applied to every cell in radius uniformly, hard-edged — no falloff/feather control), Smooth (each tick, every affected cell's value moves a Strength-scaled fraction toward the average of itself + its `NeighborIds`' values), Biome painting (sets `BiomeOverride` directly on every cell in radius to the selected palette biome — a hard set, not blended; Strength unused).
5. "Отменить всё" — new bulk-undo action clearing the whole brush undo stack (`mapRenderer.BrushUndoStackCount`-driven), no confirmation.

**Out of scope (this phase):**
- Any change to the "Точное выделение" (Selection+Override) mode's own UI/behavior — preserved as-is.
- Main-screen shell, POI screen, Modals — other specs.
- Falloff/feathering at the brush edge (not shown in the mockup, not requested).

---

## Design

### Mode toggle
A small link/segment (2 options) above the rest of the panel content, not part of the mockup: **Кисть** (default, shown first-load) / **Точное выделение**. Switching toggles which sub-root is active, same `SetActive` pattern as the existing `selectionPanelRoot`/`brushPanelRoot` split — this toggle simply becomes the new top-level switch, with "Кисть" now hosting the redesigned content described below instead of the old step-brush.

### Кисть (Paint brush) UI
- **Что редактируем**: 2×2 `Toggle`/radio grid (Высота active by default, Температура, Влажность, Биом) — `BrushTarget` enum gains `Biome`.
- **Режим**: 3-way segment (Поднять / Опустить / Sглaдить) — `BrushMode` enum `{ Raise, Lower, Smooth }`. Hidden (not rendered / disabled) when target = Биом, since raise/lower/smooth are meaningless for a category value.
- **Форма**: 2 icon buttons (circle/square, mutually exclusive) — `BrushShape` enum `{ Circle, Square }`.
- **Размер**: `Slider`, range approximate (not deeply tuned, same precedent as the Generation screen's land-shape presets — confirmed acceptable) — implementer picks a default that reliably covers a handful of cells on a Medium-preset map, tunes visually.
- **Сила**: `Slider` 0–100%, default 60% (matches the mockup's example value).
- Footer: "Отменить всё" button → clears the full brush undo stack.

### Brush cell-query + application (`BrushToolController.cs` rewrite)
On each paint tick (same press/hold/repeat-timer mechanism as today):
1. Raycast to find the cursor's hit cell (unchanged, `WorldMapRenderer.GetCellUnderRay`).
2. Gather affected cells: iterate all cells, test `Vector2.Distance(cell.Site, hitPoint) <= radius` (circle) or `Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius` (square, axis-aligned).
3. Apply per target/mode:
   - **Raise/Lower** (Height/Temperature/Moisture): `signedDelta = (mode == Raise ? +1 : -1) * brushStep * (strength/100)` applied to every affected cell — same underlying `BrushAdjustElevation`/`Temperature`/`Moisture` calls as today, just looped over the affected set instead of one cell.
   - **Smooth** (Height/Temperature/Moisture): for each affected cell, `newValue = Lerp(currentValue, AverageOfSelfAndNeighbors(currentValue, NeighborIds), strength/100)`.
   - **Biome**: for each affected cell, `cell.BiomeOverride = selectedPaletteBiome` (requires a biome to be selected in the contextual palette first — if none selected, painting is a no-op, per standard "nothing to paint" behavior).
4. One full press→release stroke = one undo entry covering every cell touched during the stroke (same `BeginBrushStroke`/`EndBrushStroke` bracketing as today, just recording a larger cell set per tick now).

### Contextual biome palette
Bottom-left, 264px, visible only when target = Биом. Grid of clickable swatches sourced from `RegionColorPalette`'s existing biome colors (the same values `MapLegendUI` already displays read-only) — clicking one sets `selectedPaletteBiome` and highlights it with an `Accent` border, matching the mockup's "у выбранного — обводка `--accent`".

---

## Error Handling

No new error paths. Painting with no biome selected is a no-op (not an error). "Отменить всё" on an empty undo stack is a no-op.

---

## Testing

- **Self-Test: Brush Radius Query** — construct a small set of cells with known `Site` positions, assert circle/square queries return the expected subset for a known radius/center.
- **Self-Test: Smooth Averaging** — assert `AverageOfSelfAndNeighbors` computes the expected value for a cell with known neighbor values.
- **Manual:** Play-mode verification of Raise/Lower/Smooth against the real generated map at various radii/shapes; Biome painting with the contextual palette; "Отменить всё" clears a multi-stroke session; "Точное выделение" toggle still reaches the unchanged existing precise-editing UI.

---

## Out of Scope (this phase)

- Main-screen shell (done, [[2026-07-06-main-screen-redesign-design.md]]).
- POI screen, Modals — remaining specs.
- Brush edge falloff/feathering.
