# POI Screen Redesign (Screen D) — Design

**Date:** 2026-07-06
**Status:** Approved, ready for implementation planning
**Branch:** implement off `main`

---

## Goal

Bring Screen D ("Точки интереса") from `design_handoff_realmweaver_ui/README.md` into a new
POI list panel (doesn't exist today) plus a restyled `PoiEditPanel.cs`. Third of four
sub-projects (A→C→**D**→F).

Real new functionality: a searchable/filterable POI list (today there's only a count
spinner + bulk buttons), and an arm-then-click map placement tool.

---

## Current State

- `MapEditorPanel.BuildPoiTab` (being retired per the Main-screen shell spec, [[2026-07-06-main-screen-redesign-design.md]] — its content moves into `PoiToolPanel.cs` unchanged for now): count spinner + "Сгенерировать точки интереса" (batch-generate N) + "Добавить одну точку" (places at a default position) + "Очистить все" (bulk delete). **No list, no search, no filter.**
- `PoiEditPanel.cs`: standalone floating panel, shown on POI selection (`poiManager.OnSelectionChanged`). Fields in order: Type (`Dropdown`), Name (`InputField`), Description (`InputField`, multiline), cell-snap row, icon file-picker row (`StandaloneFileBrowser`), icon/label scale sliders, Delete button, "Открыть страницы" button (Notes integration). See spec for exact line references — unchanged from current behavior except the Type control and repositioning.
- `PoiPlaceholderFactory.cs`: generates a 64px circular sprite per `PoiType`, with a hand-drawn 5×7-pixel **Cyrillic letter glyph** (Г/Р/Д/К for City/Ruin/Dungeon/Fortress) overlaid — used for on-map markers today. Same pixel-grid drawing technique can produce different glyph shapes.
- No click-to-place capability exists — `PoiInteractionController.cs` only handles clicking/dragging *existing* markers, never empty map space.

---

## Scope

**In scope:**
1. New POI list panel (left, 262px, full height): header "Точки интереса" + live count, a small "⋯" overflow menu (relocates the existing "Сгенерировать точки интереса" and "Очистить все" bulk actions, unchanged behavior), search field, filter chip row (Все / Города / Руины / Подземелья / Крепости — all 4 real `PoiType` values get a chip, even though the mockup's prose text only names 3; treated as a documentation gap, not an intentional omission), scrollable row list (icon + name + "тип · регион" subtitle), footer "+ Добавить точку".
2. Two-way selection sync: clicking a list row selects/highlights the corresponding map marker (and opens `PoiEditPanel`); selecting a marker on the map highlights its row in the list.
3. Arm-then-click placement tool: clicking "+ Добавить точку" arms a placement mode (cursor/hint changes, e.g. via the toolbar hint text from Screen A's `MapToolbarUI` area or a small inline hint); the next left-click on an empty (non-POI) map cell creates a new POI there and disarms; pressing Esc or clicking "+ Добавить точку" again disarms without placing. Avoids accidental placement (confirmed with the user — arm-first, not always-on).
4. `PoiEditPanel.cs`: Type `Dropdown` → 4 icon buttons (Город/Руины/Подземелье/Крепость), restyled to match the mockup's spacing/right-side 308px anchor. All other fields keep current behavior, restyled only.
5. `PoiPlaceholderFactory.cs` extended: replace the Cyrillic-letter glyphs with the mockup's primitive icon shapes (Город = дом/трапеция с проёмом, Руины = 3 вертикальных штриха, Подземелье = арка, Крепость = зубчатая стена), using the same pixel-grid drawing technique. The **same** generated icon is reused in three places — on-map markers, list row icons, and `PoiEditPanel`'s new type-selector buttons — so the whole app shares one consistent icon language rather than introducing a second icon system just for the new panels.

**Out of scope (this phase):**
- Main-screen shell, Editor-brush panel — other specs (done).
- Modals — Screen F's spec (next).
- Any change to POI data model (`PoiData.cs`) beyond what's needed to back search/filter (name/type/region lookups already exist on the data model).
- User-uploaded custom icon overriding the shared placeholder is unaffected — that path (`StandaloneFileBrowser`) already exists and keeps working as-is; the new shared placeholder is only the *default* icon absent a custom upload.

---

## Design

### List panel (new)
Floating, left side, 262px, full viewport height (below the Screen-A toolbar strip). Header row: "Точки интереса" (bold) + live count (`poiManager`'s current count) + "⋯" button opening a small popup with "Сгенерировать точки интереса" and "Очистить все" (same `poiManager.GenerateAll(n)`/`ClearAll()` calls as today, same count-spinner-driven `n` — just relocated UI, not re-implemented logic). Search `InputField` (filters rows by name substring, case-insensitive). Filter chip row: `Все` (default active) / `Города` / `Руины` / `Подземелья` / `Крепости` — clicking a chip filters the row list by `PoiType`; `Все` clears the filter. Rows: shared per-type icon (26×26) + name (`Text`) + "тип · регион" subtitle (`Mut`, small) — selected row gets `AccentSoft` background + `Accent` border, matching the mockup. Footer: "+ Добавить точку" (dashed border button, matches mockup's "add" affordance style used elsewhere in the project).

### Two-way selection sync
List row click → `poiManager.Select(poiId)` (existing selection API, presumably already used by `PoiInteractionController`'s marker click — reuse the same entry point so both paths stay consistent). `poiManager.OnSelectionChanged` (already consumed by `PoiEditPanel`) also drives the list panel's row highlight — subscribe the same event.

### Arm-then-click placement
New small piece of state (`bool placementArmed`) owned by the list panel or a small new controller. "+ Добавить точку" toggles it on; while armed, `PoiInteractionController` (or a new sibling script) checks for a left-click on empty map space (raycast hits a cell but no existing POI marker) and calls `poiManager.AddAt(cellPosition)` (new method — today's `AddOne()` places at a default/center position with no position argument; this needs a position-taking overload), then disarms. Esc key or re-clicking the button disarms without placing. While armed, the cursor/hint should communicate the state (exact visual — cursor change vs. a toolbar hint text like the mockup's "кликните по карте, чтобы добавить" — implementer's call, consistent with existing project hint-label conventions).

### PoiEditPanel Type selector
Replace the `Dropdown` with 4 icon buttons (one per real `PoiType`, excluding `Unknown` which has no dedicated mockup button — keep `Unknown` reachable only as the pre-selection default state, not a selectable button, matching the mockup's exact 4-button set). Each button shows the shared per-type icon (see below); selected button gets an `Accent` border. Panel repositioned to match the mockup's fixed 308px right-side anchor instead of chaining under the Legend (the Legend itself moved to bottom-left in Screen A's shell spec, so this decouples cleanly).

### Shared per-type icon (`PoiPlaceholderFactory.cs` rewrite)
Same 64px circular sprite + outline structure as today, but the overlaid glyph bitmap is redrawn per type as a primitive shape instead of a Cyrillic letter:
- **Город**: a house/trapezoid silhouette with a door cutout.
- **Руины**: 3 vertical column strokes.
- **Подземелье**: an arch (rounded top).
- **Крепость**: a crenellated wall (notched top edge).
`Unknown` keeps its existing "?" glyph (no mockup equivalent needed, still used as the pre-selection default). The same `Sprite` returned by `PoiPlaceholderFactory.GetPlaceholder(type)` is used for the on-map marker, the list row icon, and the edit panel's type-selector button (scaled to each context's size via normal `Image` scaling) — one source of truth, no duplicate icon-drawing code.

---

## Error Handling

No new error paths. Search with no matches shows an empty list (no special empty-state message needed — same posture as other lists in the project). Placement-armed + clicking an existing marker instead of empty space: falls through to normal marker selection (POI selection takes priority over placement), placement stays armed for a subsequent empty-space click.

---

## Testing

- **Self-Test: Icon Sharing** — assert `PoiPlaceholderFactory.GetPlaceholder(type)` returns the same cached `Sprite` instance across repeated calls and across the three consumers (a cache-identity check, not a pixel comparison).
- **Manual:** search/filter narrows the list correctly; row click selects on map and vice versa; arm-then-click places a POI only on empty space, disarms correctly on Esc/re-click/successful placement; "⋯" menu's generate/clear-all still work; type buttons in `PoiEditPanel` correctly set `PoiType` and reflect the current selection.

---

## Out of Scope (this phase)

- Main-screen shell, Editor-brush panel (done).
- Modals — Screen F's spec (next).
- POI data model changes beyond what's already present.
