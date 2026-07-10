# Region Labels — Biome-Zone Naming Redesign (design)

## Context

The shipped region-labels feature (`Assets/WorldGen/Rendering/RegionLabels/`) auto-seeds
Latin biome-family labels, renders them as a zoom-LOD screen-space TMP overlay, and lets the
DM rename/move/delete/add them (persisted in the `.dndproj`). Live Editor testing surfaced two
problems:

1. **Labels don't convey meaning or place.** The placer names each connected biome-**family**
   patch with a fixed Latin family name, so the same name repeats across every disconnected patch
   (`SILVA UMBRARUM` ×3, `SILVA IGNEA` ×2 on one map). The biome is already conveyed by the map
   colors + the biome legend, so the Latin labels mostly duplicate the legend while looking like
   many identically-named regions. Pure Latin also doesn't tell a non-Latin reader that `SILVA`
   is a forest.
2. **Labels block the cursor.** Each label container carries a transparent `Image` with
   `raycastTarget = true` (added for click-to-select editing). `MapCameraController.HandleScrollZoom`
   skips zoom when `EventSystem.current.IsPointerOverGameObject()` (`MapCameraController.cs:91`),
   so hovering any label kills scroll-wheel zoom. (Pan is right-mouse-drag, so it does not
   conflict with left-click label editing.)

This redesign keeps labels as **biome-zone names** but makes them meaningful, unique, sparse, and
non-blocking. It does NOT change what a label fundamentally is (a biome-family zone), nor the
persistence/LOD/save-load machinery.

## Goals

- Each labeled zone gets a **unique, Russian, descriptive** name whose noun states the biome
  (adjective supplies uniqueness): e.g. `Сумрачный Лес`, `Пепельная Пустошь`, `Золотые Луга`.
- **Few labels by default** (~4–7 on a typical map) — only zones above a size threshold are named;
  smaller zones stay colored but unnamed. A **density slider** lets the DM tune the threshold.
- **Labels never intercept the cursor in normal use** (zoom/pan work everywhere). Editing lives
  behind an **"Редактировать названия" (edit-mode) toggle**; only in edit mode are labels
  clickable.
- Cyrillic-capable font (**Forum**, OFL) replaces IM Fell English (Latin-only). MedievalSharp and
  Uncial Antiqua were considered but ruled out — both are Latin-only on Google Fonts and would
  render the Russian names as missing-glyph boxes.

## Non-goals / unchanged

- Persistence format, save/load wiring, the LOD screen-space overlay, the visibility layer toggle
  ("Названия регионов"), and the manager's CRUD + event model are unchanged.
- Grouping is still by biome **family** (connected components) — NOT by the map's `RegionId`
  territories. (Region-territory naming was considered and declined; the DM wants biome zones.)
- No change to world generation, biome classification, or the region/border systems.

## Future-proofing (explicit DM constraint)

The DM plans to **revisit biome logic and biome types** in the future. Therefore the
**biome → Russian-noun mapping and the biome-family set must be a single, isolated, easily-edited
data table** (one dictionary/table in `RegionLabelNames`), so a future biome rework touches only
that table, not the placer/overlay/manager logic. No biome name may be hardcoded outside that
table.

## Design

### 1. Russian descriptive name generator (new: `RegionLabelNames`)

A new pure-C# static class `Assets/WorldGen/Rendering/RegionLabels/RegionLabelNames.cs`.

- **Noun table** — `BiomeFamily → (noun, grammatical gender)`, the single isolated table above:

  | BiomeFamily | Noun | Gender |
  |---|---|---|
  | Forest | Лес | m |
  | ForestWarm | Дубрава | f |
  | Badlands | Пустошь | f |
  | Plains | Луга | pl |
  | Highland | Кряж | m |
  | Snow | Снега | pl |
  | Moor | Топь | f |
  | Tundra | Тундра | f |
  | Sea | Море | n |

  Coast/Lake stay unnamed (as today). Nouns/gender are placeholders the DM may edit; the code must
  not assume any specific noun.

- **Adjective pool** — ~24–30 evocative adjectives, each stored with its four agreement forms
  (m / f / n / pl): e.g. `Сумрачный/Сумрачная/Сумрачное/Сумрачные`, `Пепельный/…`, `Золотой/…`,
  `Вечный`, `Северный`, `Древний`, `Багряный`, `Туманный`, `Забытый`, `Стылый`, `Мёртвый`,
  `Гиблый`, `Тихий`, `Дикий`, `Хладный`, `Ветреный`, `Полуночный`, `Седой`, `Угрюмый`, …
  Enough that a single map rarely exhausts a biome's variety.

- **Selection** — deterministic + unique per map:
  - `NameFor(BiomeFamily fam, int zoneKey, ISet<int> usedAdjIndicesForThisFamily) → string`.
  - `zoneKey` is a stable per-zone identifier (the **minimum cell Id** in the component — stable for
    a given seed). `adjIndex = Hash(seed, zoneKey) mod pool.Count`, then linear-probe forward until
    an index not already used **for that biome family on this map** (guarantees no two same-biome
    zones share an adjective; different biomes may reuse an adjective — `Сумрачный Лес` and
    `Сумрачная Топь` are fine). Deterministic: same seed ⇒ same names.
  - Compose `"{adjective[gender]} {noun}"` with correct agreement.

### 2. Sparse zones + density slider

In `RegionLabelPlacer.Place` (existing):

- Keep the BFS grouping into connected same-family components.
- Replace the fixed `minPatchCells` gate with a **size threshold driven by a density value**:
  `Place(..., float labelDensity)` where `labelDensity ∈ [0,1]`. Map density to a minimum
  component size in cells, e.g. `minZoneCells = Lerp(bigThreshold, smallThreshold, labelDensity)`
  (density 0 → only the largest zones ~3–4; density 1 → include medium ~≤10). Default density
  (~0.4) yields ~4–7 labels on a typical map. Only components with `cellCount ≥ minZoneCells` are
  named; the rest stay unnamed.
- Names come from `RegionLabelNames.NameFor(...)` instead of the Latin `LandNames` dict (removed).
- **On-land anchor:** keep the area-weighted centroid, but if it falls outside the component's
  land (over water/lake or outside any of the component's cells), snap the anchor to the `Site` of
  the component's cell nearest the centroid — so a label never floats on water.
- **Sea labels:** keep the ≤2 open-ocean anchors, but name them via `RegionLabelNames` with the
  `Sea` noun (`Стылое Море`, …) instead of fixed Latin.

`labelDensity` is a serialized `WorldMapRenderer` field (default ~0.4) surfaced as a
**"Плотность названий" slider** in `MapLayersPanel`. `RegionLabelManager.SeedFromCells` reads it and
passes it to `Place`. Changing the slider does **not** live-reseed (that would discard edits);
it applies on the next generation or when the DM presses **"Пересоздать названия"** (which reseeds,
discarding manual edits — same as today).

### 3. Edit-mode toggle (fixes cursor blocking)

- `RegionLabelOverlay` gains `public void SetEditMode(bool)` and an internal `editMode` flag
  (default **false**).
  - **editMode = false (default):** every label container's click `Image.raycastTarget = false`
    (or the click Image/pointer-handler is disabled) → labels never register as
    `IsPointerOverGameObject`, so scroll-zoom and all map input pass through. The overlay still
    projects + LOD-fades + shows labels (pure display). Add-mode, drag, rename, delete are inert.
  - **editMode = true:** containers' `raycastTarget = true` → the existing Task-5 editing
    (click-select, inline rename, drag-move, "×" delete, "+ Название" add-mode) becomes active.
  - Any open rename box / selection is torn down when leaving edit mode
    (`SetEditMode(false)` → `manager.DeselectAll()` and destroy edit UI).
- `MapLayersPanel` gets an **"Редактировать названия" toggle** driving
  `regionLabelOverlay.SetEditMode(on)`. The existing **"+ Название"** and **"Пересоздать названия"**
  buttons are only meaningful in edit mode (enable/disable or reveal them with the mode).
- The visibility layer toggle **"Названия регионов"** (show/hide) stays independent of edit mode.

### 4. Placement polish

- With fewer labels, overlap is rarer; keep the overlay's existing collision-nudge.
- The on-land anchor guarantee (§2) keeps names off water.

### 5. Font — Forum (Cyrillic)

- Replace IM Fell English with **Forum** (OFL, Google Fonts, antique Roman inscriptional caps with
  a `cyrillic` + `cyrillic-ext` subset — an elegant "carved in stone" cartographic look). This is a
  DM Editor step: import the `.ttf`, run TMP Font Asset Creator with a **Unicode Range** covering
  Latin + Cyrillic (`20-7E,A0-FF,400-4FF`) at 1024×1024, and assign the resulting SDF asset to
  `RegionLabelOverlay.labelFont`.
- No code depends on the specific font; `labelFont` is already a serialized `TMP_FontAsset` ref, so
  swapping fonts later (e.g. after the planned biome rework, or to another Cyrillic face) needs no
  code change.

## Affected components

- **New:** `RegionLabelNames.cs` (noun table + adjective pool + deterministic unique selection).
- **Modified:** `RegionLabelPlacer.cs` (density threshold, Russian names via `RegionLabelNames`,
  on-land anchor; drop Latin `LandNames`). `RegionLabelPlacer.Place` signature gains `labelDensity`.
- **Modified:** `RegionLabelManager.cs` (`SeedFromCells` reads `labelDensity` from `mapRenderer`,
  passes it to `Place`).
- **Modified:** `RegionLabelOverlay.cs` (`SetEditMode(bool)` + gate raycastTarget/editing on it;
  default display-only).
- **Modified:** `WorldMapRenderer.cs` (serialized `labelDensity` field ~0.4).
- **Modified:** `MapLayersPanel.cs` ("Редактировать названия" toggle + "Плотность названий" slider;
  wire the existing add/regenerate buttons to edit mode).
- **DM Editor step:** Forum SDF font asset (Latin+Cyrillic) → `labelFont`.

## Testing

- `RegionLabelNames`: `[ContextMenu]` self-test — deterministic (same seed+zoneKey ⇒ same name),
  unique adjectives within a family, correct gender agreement per noun.
- `RegionLabelPlacer`: extend the existing self-test — density threshold filters small components;
  named zones carry Russian names; on-land anchor snaps a water-centroid onto a land cell.
- Edit-mode: DM Editor checkpoint — labels don't block zoom in display mode; editing works in edit
  mode; leaving edit mode tears down any open rename box.
- Agents can't run Unity: implementers write code + static self-review; `[ContextMenu]` self-tests
  and visual/interaction checks are the DM's Editor checkpoints.

## Backward compatibility

- Persistence unchanged (labels are still `{Id, Text, WorldPosition, SeedFamily}`); existing saved
  labels keep their stored text. Re-seeding a loaded map produces the new Russian names.
- `labelDensity` is a new serialized field → old scenes fall back to the C# default (~0.4)
  (per the project's Unity-serialization gotcha, the DM should eyeball it in the Inspector).
