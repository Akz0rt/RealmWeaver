# Theme System + Generation/Progress Screens — Design

**Date:** 2026-07-05
**Status:** Approved, ready for implementation
**Branch:** implement off `main` (project has no separate feature branches/worktrees)

---

## Goal

Bring the UI redesign produced with Claude Design (`design_handoff_realmweaver_ui/README.md`, screenshots in `screens/dark/` and `screens/light/`) into the existing runtime-built `UnityEngine.UI` codebase. This phase covers:

1. A theme token system (`ThemeService`) with Dark/Light palettes, retrofitted onto **all** existing runtime-built UI (21 files, ~139 inline color literals today) — recoloring only, no layout changes.
2. A **Generation screen** (empty state) shown when no map exists yet, replacing the map editor/legend with a parameter form and a "Сгенерировать карту" action.
3. A **Progress screen** shown while generation runs, backed by a real staged/coroutine version of the generation pipeline (not a fake progress bar).

Remaining screens from the design handoff (main-screen layout polish, brush editor panel redesign, POI screen redesign, modal restyling) are out of scope for this phase — queued separately.

---

## Scope

**In scope:**
- `ThemeService` (roles, Dark/Light dictionaries, `ApplyTheme`, tagging mechanism), persisted via `PlayerPrefs`.
- Recoloring all 21 existing runtime-UI files to use theme roles instead of inline `Color`/`Color32` literals. No geometry/layout changes.
- `GenerationScreenUI` (seed, size, land shape, region-detail slider, generate/open-project actions).
- A real staged generation pipeline (`WorldGenerator.GenerateWorldStepped`) reporting progress at 5 checkpoints, plus `GenerationProgressUI` driving it.
- `MapScreenController` switching between Generation / Progress / (existing) Map-Editor-Panel+Legend based on `WorldMapRenderer.Cells == null` and a `generating` flag.

**Out of scope (this phase):**
- Main screen's exact spacing/radius/typography polish per the mockup (today's layout is kept, only recolored).
- Brush editor panel redesign, POI screen redesign, modal dialog restyling — later phase.
- True multi-select or any change to `StandaloneFileBrowser`.
- Auto-placement of POI candidates during generation (the mockup's progress checklist mentions this; confirmed with the user this feature doesn't exist and isn't being added — the checklist step is dropped).
- Windowed/resizable display mode (separate queued item).

---

## 1. Theme token system

New file `Assets/WorldGen/Rendering/Theme/ThemeService.cs`:

```csharp
public enum ThemeRole
{
    Bg, Panel, Panel2, Elev, Border, Txt, Mut, Accent, AccentInk, AccentSoft,
    MapOcean, MapLand, MapCoast, Dot, Danger
}

public enum Theme { Dark, Light }

public static class ThemeService
{
    public static Theme Current { get; private set; }
    public static void ApplyTheme(Theme theme);   // sets Current, restyles every registered ThemedGraphic, saves to PlayerPrefs
    public static Color Get(ThemeRole role);       // looks up Current's dictionary
    public static void Tag(Graphic graphic, ThemeRole role); // adds/updates a ThemedGraphic marker, applies current color immediately
}
```

Dark/Light `Dictionary<ThemeRole, Color>` populated verbatim from the README's hex table (`--bg` #141419/#E7E1D3, `--panel` #1C1C22/#F4F0E7, ... `--danger` #C9605A in both themes).

`ThemedGraphic` — a tiny `MonoBehaviour` holding a `Graphic` reference and a `ThemeRole`, self-registers in a static list on `OnEnable`, deregisters on `OnDestroy`. `ApplyTheme` iterates the list and sets `graphic.color = dict[role]`. `ThemeService.Tag(...)` is the one-line call added at every existing `AddComponent<Image>()`/`AddComponent<Text>()` call site: `ThemeService.Tag(img, ThemeRole.Panel);` right after construction.

Theme choice persists in `PlayerPrefs` (matching the existing convention for split-fraction/sidebar-width), restored on first `ThemeService` access. A theme toggle is added to the existing "Файл" menu in `ProjectMenuBar` (new popup action, "Тёмная/Светлая тема").

**Map/biome semantic colors** (`RegionColorPalette.cs`'s 33 literals, the ocean/forest/grass/etc. legend colors) are explicitly **not** retrofitted — the design doc states these are theme-independent by design.

---

## 2. Recoloring existing UI (no layout changes)

Every `new Color(...)`/`Color32(...)` literal across the 21 files below is replaced with a single `ThemeService.Tag(graphic, ThemeRole.X)` call right after the `Image`/`Text` is constructed — `Tag` both applies the current theme's color immediately and registers the graphic so `ApplyTheme` recolors it on later theme switches; no separate `.color = ...` assignment is needed. The role is chosen by matching the color's current visual purpose (dark panel background → `Panel`, muted/secondary text → `Mut`, accent/selection outline → `Accent`, the existing delete/danger red → `Danger`, etc.) — a judgment call per usage, not a mechanical find-replace. Geometry (sizes, anchors, padding) is untouched in this phase.

Grouped into 4 implementation tasks by subsystem:
- **Map/rendering:** `WorldMapRenderer.cs`, `MapEditorPanel.cs`, `MapLegendUI.cs`, `RegionColorPalette.cs` (map/biome literals excluded, see above), `PoiPlaceholderFactory.cs`, `CellSelectionController.cs`, `CanvasInteractionController.cs`
- **Notes:** `NotesToolbar.cs`, `NotesTreeSidebar.cs`, `LinkView.cs`, `LinkAnchorController.cs`, `NoteCardView.cs`, `DrawingObjectView.cs`, `DraggableDivider.cs`, `ObjectResizeController.cs`, `NotesIconFactory.cs`, `NotesRootBuilder.cs`
- **POI:** `PoiEditPanel.cs`
- **Project/system:** `ProjectMenuBar.cs` (also gets the theme-toggle menu action), `ConfirmDialog.cs`, `UpdateChecker.cs`

---

## 3. Generation screen (empty state)

State signal: `WorldMapRenderer.Cells == null` (existing, already used by `ProjectMenuBar`/`PoiManager` for the same "no map yet" check — no new field needed).

New `Assets/WorldGen/Rendering/MapScreenController.cs` — subscribes to `WorldMapRenderer.OnWorldRegenerated`, and switches active state between three mutually-exclusive views by `SetActive`-ing their root GameObjects: **GenerationScreenUI** / **GenerationProgressUI** (section 4) / the existing `MapEditorPanel` + `MapLegendUI` pair. This is a whole-region swap (matching the mockup, where the toolbar/tabs/layers-panel/legend all disappear together), not a 4th tab inside `MapEditorPanel`.

New `Assets/WorldGen/Rendering/GenerationScreenUI.cs` — 560px centered card:
- **Seed** — text input field + "↻ Случайно" button (fills the field with a random string).
- **Map size** — 3-way segmented control: Малый (350×350) / Средний (500×500, default) / Большой (700×700) → sets `mapWidth`/`mapHeight` on `WorldMapRenderer`.
- **Land shape** — 3-way segmented control, approximate presets (confirmed with user — not deeply tuned):
  | | falloffPower | innerRadius | seaLevel |
  |---|---|---|---|
  | Материк (default) | 3.0 | 0.6 | 0.30 |
  | Архипелаг | 1.8 | 0.3 | 0.45 |
  | Острова | 1.5 | 0.1 | 0.55 |
- **Детализация · регионов** — slider, range 4–40, default 24 → `numberOfRegions`.
- **"✦ Сгенерировать карту"** — hashes the seed string into `WorldMapRenderer.seed` using a small hand-rolled stable hash (**not** `string.GetHashCode()` — .NET randomizes that per-process by default, which would silently break the entire point of a seed: typing the same seed string again, even in the same session after a restart, could produce a different map):
  ```csharp
  static int StableSeedHash(string s)
  {
      unchecked
      {
          int hash = 23;
          foreach (char c in s) hash = hash * 31 + c;
          return hash;
      }
  }
  ```
  applies all the above fields, starts the staged generation coroutine (section 4).
- **"Открыть проект…"** — reuses the existing `ProjectMenuBar.DoOpen()` / `StandaloneFileBrowser` path unchanged.

---

## 4. Progress screen + staged generation pipeline

`Assets/WorldGen/Generation/WorldGenerator.cs` gets a new method alongside the existing `GenerateWorld` (which stays untouched, for backward compatibility with self-tests and any other reference):

```csharp
public static IEnumerator GenerateWorldStepped(GenerationParams p, Action<string, float> onProgress, Action<List<VoronoiCell>, List<TemperatureEpicenter>, List<MoistureEpicenter>, List<River>> onComplete)
```

Same body as `GenerateWorld`, with one safe reordering — **temperature computation moves earlier, right after moisture** (confirmed safe: `BiomeClassifier.Classify` at `CellClimateAverager.cs:49` only consumes `avgElevation`/`avgMoisture`, never temperature; region growing has never depended on temperature either) — so the 5 progress checkpoints land in the same order the mockup's checklist shows, with a `yield return null` between each:

1. **"Генерация высот"** — points → Voronoi → Lloyd relaxation → corner graph → island-shape assignment
2. **"Океаны и озёра"** — ocean/lake flood fill → small-lake filter → water status onto cells → elevation + redistribution
3. **"Температура и влажность"** — moisture epicenters + field, then temperature epicenters + field (moved up)
4. **"Расчёт биомов"** — `CellClimateAverager`
5. **"Границы регионов"** — `RegionGrowing` + `LakeRegionUnifier`

New `Assets/WorldGen/Rendering/GenerationProgressUI.cs` — spinner ring, mono parameter summary line, 5-item checklist (done = accent checkmark, current = accent ring with dot, pending = muted empty ring), progress bar, "Отмена" button. `MapScreenController` drives it: `StartCoroutine(WorldGenerator.GenerateWorldStepped(params, (label, frac) => progressUI.SetStep(label, frac), OnGenerationComplete))`. Cancel calls `StopCoroutine` and switches back to the Generation screen — nothing needs cleanup since `WorldMapRenderer.cells` is only assigned once, at the very end, in `onComplete`.

---

## Error handling

No new error paths: all Generation-screen inputs are pre-constrained (segmented controls, slider, always-valid seed string), so there's nothing invalid to reject. "Открыть проект…" reuses `ProjectSerializer`/`ConfirmDialog`'s existing error handling unchanged.

---

## Testing

No automated test runner in this project (established convention) — verification via `[ContextMenu("Self-Test: ...")]` plus manual Play-mode testing:

- **Self-Test: Theme Apply** — call `ApplyTheme(Dark)` then `ApplyTheme(Light)`, assert every registered `ThemedGraphic`'s `graphic.color` matches the expected dictionary value for its role.
- **Manual:** fresh scene (no map) shows the Generation screen; clicking "Сгенерировать карту" shows the Progress screen with the 5-step checklist advancing in order; on completion, the normal Карта/Редактор/Точки screen appears with the generated map. "Отмена" mid-generation returns to the Generation screen. Toggling Тёмная/Светлая theme recolors every screen at once, including the pre-existing ones (map editor, POI panel, notes, dialogs).

---

## Out of Scope (this phase)

- Main/Editor/POI/Modals screens' layout polish per the mockup (recoloring only, this phase).
- Auto-placement of POI candidates during generation.
- Windowed/resizable display mode.
- Land-shape preset fine-tuning beyond the approximate values above.
