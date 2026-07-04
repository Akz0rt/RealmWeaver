# Project Save / Export / Import — Design

**Date:** 2026-07-04
**Status:** Approved, ready for implementation
**Branch:** implement off `main` (project has no separate feature branches / no git remote)

---

## Goal

Let the DM save the entire working project (generated map + manual overrides, POIs, and the notes document) to a single file on disk, and reload it later — on the same machine, in the same build of the tool. This is the first persistence layer for the tool; today everything (map, POIs, notes) is fully in-memory and lost on exit. Only two small UI prefs (map/notes split fraction, sidebar width) currently survive via `PlayerPrefs`, and those are untouched by this feature.

Audience for the saved file: **this tool only**. No requirement to be read by another instrument (Foundry, Roll20, etc.) and no requirement to guarantee compatibility across tool versions beyond a best-effort version tag — see [Versioning](#versioning-and-compatibility).

---

## Scope

**In scope:**
- Save the active project to a file the user picks (or to its already-known path).
- Load a project from a file, replacing whatever is currently active.
- A "File" menu: Save / Save As… / Open… / Open Recent.
- One combined save file containing map + POIs + notes (matches how the user framed the request — "всего проекта").

**Out of scope (v1):**
- Autosave (confirmed with user — manual save only for v1).
- Multiple simultaneously-open projects/tabs — one active project at a time, with a "recent files" list for switching between saved projects one at a time.
- Exporting a map as an image/PNG snapshot — different feature, not requested here.
- Cross-tool export formats (Foundry/Roll20/etc).
- Cloud sync / auto-backup.

---

## What gets saved

A **project file** is a single JSON document (produced via Newtonsoft.Json — see [Serialization](#serialization-mechanics)) with this top-level shape:

```csharp
public class ProjectSaveData
{
    public int FormatVersion;              // schema version, see Versioning
    public string SavedAtUtc;               // ISO-8601 timestamp, informational only

    public GenerationParams GenerationParams; // kept for reference only — NOT used to rebuild the map
    public List<VoronoiCell> Cells;         // full authoritative map state

    public List<PoiData> Pois;

    public NotesDocument Notes;             // existing Notes/Data classes, reused as-is
}
```

### Map cells

`Cells` stores every field of `VoronoiCell` needed to reconstruct the map exactly as the DM left it: `Id`, `Site`, `Polygon`, `NeighborIds`, `Height`, `IsOcean`, `RegionId`, `Temperature`, `Humidity`, and all five override fields (`TemperatureOverride`, `MoistureOverride`, `ElevationOverride`, `WaterOverride`, `BiomeOverride`), plus `Biome`.

**`GenerationParams` is stored for reference only** — it is *not* replayed through `WorldGenerator` to reconstruct the map, and **v1 does not add any UI to display it** (no params-inspector panel). It's saved purely so the raw seed/size/noise settings aren't lost from the file, for possible future use (debugging, manual JSON inspection, or a display panel added later). The cell list is the single source of truth on load, because manual per-cell overrides applied via the brush tools are free-form edits that cannot be derived from `GenerationParams` + seed. This matches the existing rationale in [[design-decisions]] that overrides are layered on top of, not derived from, generation.

**Not saved, recomputed after load:**
- The **corner graph** (`Corner.cs`) — `WorldGenerator` already documents that corners are always rebuilt deterministically from cells via `CornerGraphBuilder.Build(cells)`, with no RNG involved. Loading calls the same builder, so corners never need to hit disk.
- **Temperature/moisture epicenters** — these are generation-time inputs; their effect is already baked into each cell's `Temperature`/`Humidity` fields, so the epicenters themselves add nothing to reconstructing current map state.
- **Rendering artifacts** — mesh, border ribbons, river `LineRenderer`s, POI marker GameObjects. All of these are already rebuilt from data today (at generation time); load reuses the exact same rebuild path.
- **Region metadata** — confirmed there is no separate per-region data class; `RegionColorPalette.GetRegionColor(regionId)` is a pure deterministic function of the region index, so `VoronoiCell.RegionId` alone is sufficient.

### POIs

`Pois` serializes the existing `PoiData` class directly (already a plain POCO with no Unity types — no separate DTO needed). One field is added to `PoiData`:

```csharp
public byte[] CustomIconBytes;   // null = use type placeholder
```

`CustomSpritePath` (the existing filesystem-path field) is **kept alongside** `CustomIconBytes`, not removed — it still exists purely as scratch state for the "pick a file" UI flow (the path the DM just browsed to, before its bytes are read). `CustomIconBytes` is the one authoritative field that gets serialized and is what `PoiMarkerView` renders from once set. This resolves the earlier design concern (a path-based reference would break if the DM moves/deletes/renames the original icon file, or opens the save on a different machine) by making `CustomIconBytes` self-contained. Concretely:
- `PoiEditPanel`'s "Сменить иконку" flow reads the picked file into `CustomIconBytes` immediately via `File.ReadAllBytes` (using the already-integrated `StandaloneFileBrowser` for the picker), storing the path in `CustomSpritePath` only for display (e.g. showing the filename in the panel).
- `PoiMarkerView`'s icon-loading path uses `CustomIconBytes` (`Texture2D.LoadImage(bytes)`) whenever it's set; `CustomSpritePath` is no longer read for actual image loading — it becomes purely informational.

### Notes

`NotesDocument` (`Groups` → `Pages` → `Objects`/`Links`) is reused exactly as defined in `Assets/WorldGen/Notes/Data/NotesData.cs` — already a plain-POCO tree using `System.Numerics.Vector2`, with images/drawings already embedding raw bytes (`ImageBytes`, `PixelDataPng`). No data-model changes needed here beyond the polymorphism handling described below.

---

## Serialization mechanics

Add the **`com.unity.nuget.newtonsoft-json`** UPM package (official Unity-maintained package; no other new third-party dependency). Reasons over `JsonUtility`:
- `JsonUtility` silently drops/mishandles `Nullable<T>` fields (`float?`, `Biome?`) — exactly the override fields that matter most for save fidelity.
- `JsonUtility` has no built-in polymorphism support, needed for the `CanvasObjectData` hierarchy (`NoteCardData`/`ImageObjectData`/`DrawingObjectData`) stored in a single `List<CanvasObjectData>` on each `NotesPage`.

**Polymorphism:** a custom `JsonConverter<CanvasObjectData>` (`Assets/WorldGen/Persistence/CanvasObjectDataConverter.cs`) writes/reads an explicit `"Kind"` string discriminator (`"NoteCard"` / `"Image"` / `"Drawing"`) rather than relying on `TypeNameHandling.Auto` (which would embed assembly-qualified .NET type names into the JSON — unnecessary noise for a tool-internal format, and fragile if classes get renamed/moved).

**Entry point:** a single static class `Assets/WorldGen/Persistence/ProjectSerializer.cs`:

```csharp
public static class ProjectSerializer
{
    public static void Save(string path, GenerationParams genParams, IReadOnlyList<VoronoiCell> cells,
                             IReadOnlyList<PoiData> pois, NotesDocument notes);

    public static ProjectLoadResult Load(string path);
}

public class ProjectLoadResult
{
    public bool Success;
    public string ErrorMessage;   // user-facing, set when Success == false
    public GenerationParams GenerationParams;
    public List<VoronoiCell> Cells;
    public List<PoiData> Pois;
    public NotesDocument Notes;
}
```

`Save`/`Load` are pure data-layer functions (no `MonoBehaviour` dependency) so they can be exercised directly by a self-test without a running scene.

---

## Versioning and compatibility

`FormatVersion` starts at `1`. On load:
- If `FormatVersion` is a version this build recognizes, load normally.
- If `FormatVersion` is **higher** than what this build understands, show a warning dialog ("Файл сохранён более новой версией инструмента — часть данных может не загрузиться") but still attempt the load, skipping unknown fields (Newtonsoft.Json ignores unrecognized JSON properties by default, so this degrades gracefully without extra code).
- No migration code for old formats is written yet — not needed until `FormatVersion` actually increments. This is intentionally minimal (YAGNI): the version field costs nothing to add now and is expensive to retrofit later.

---

## UI

### File menu

A persistent top menu bar (new — no such bar exists today): a thin full-width strip (~32px tall) pinned to the very top of the screen, above *both* the map and notes columns — not scoped to either side of the existing map/notes split. This means the map camera's viewport rect and the notes area's anchors (currently `anchorMin/anchorMax` spanning the full `0..1` vertical range, per `NotesLayoutController.Apply()`) both need their top edge to start below the bar instead of at the screen top, so the bar never overlaps `MapEditorPanel`'s tabs or the floating `NotesToolbar` row. Concretely: the bar's own `RectTransform` anchors to `(0,1)-(1,1)` with a fixed `sizeDelta.y`, and `NotesLayoutController`/`WorldMapRenderer`'s camera rect calculations get their usable vertical range reduced by the bar's height (a single shared constant, e.g. `MenuBar.HeightPixels`, read by both). The bar renders on a Canvas above `NotesCanvas` in sort order, same "later sibling wins" rule already established for the notes toolbar.

The dropdown itself hosts a single **"Файл"** menu:
- **Сохранить** — writes to the project's current known path. If the project has never been saved (no known path yet), falls through to Save As.
- **Сохранить как…** — opens the existing `StandaloneFileBrowser` save-file dialog, writes to the chosen path, and remembers it as the current known path.
- **Открыть…** — opens the `StandaloneFileBrowser` open-file dialog, loads the chosen file.
- **Открыть последние** — submenu listing the last 5 opened/saved paths (persisted via `PlayerPrefs`, same storage mechanism already used for the split fraction and sidebar width). Clicking an entry loads that path directly. Paths that no longer exist on disk are skipped when the submenu is built, not shown as broken entries.

### Load behavior

On a successful load: clear the current world (regenerate the mesh/border/river renderers from the loaded `Cells`), clear and repopulate `PoiManager` from the loaded `Pois` (same teardown path `PoiManager` already uses on `OnWorldRegenerated`), and replace `NotesDocumentController.Document` with the loaded `NotesDocument`, then refresh the notes sidebar/canvas to the document's first page — mirroring how the notes editor already initializes on a fresh in-memory document today.

---

## Error handling

- **File not found / unreadable / malformed JSON** → `Load` returns `Success = false` with a user-facing `ErrorMessage`; the UI shows a single-button dialog (reusing the existing `ConfirmDialog` pattern, adapted for a single "OK" acknowledgement instead of Yes/No) and leaves the current project untouched.
- **Newer `FormatVersion`** → warning dialog as described above, load still attempted.
- Saving never fails silently: a save I/O exception (e.g. disk full, permissions) shows the same single-button error dialog; the in-memory project is never mutated by a failed save.

---

## Testing

No automated test runner in this project (established convention) — verification via `[ContextMenu("Self-Test: ...")]` methods plus manual Play-mode testing:

**Self-Test: Project Round-Trip** (on `ProjectSerializer` or a thin test host) — builds a small in-memory fixture (a handful of `VoronoiCell`s including at least one with every override field set, one `PoiData` with `CustomIconBytes` set and one without, a `NotesDocument` with one page containing one of each `CanvasObjectData` subtype and one `LinkData`), calls `Save` to a temp path, `Load`s it back, and asserts field-for-field equality against the original fixture (including that nullable override fields round-trip correctly — the specific risk that ruled out `JsonUtility`).

**Self-Test: Corrupt File Handling** — calls `Load` on a deliberately malformed/truncated file, asserts `Success == false` and a non-empty `ErrorMessage`, with no exception escaping the call.

Manual verification: save a real generated map with overrides + POIs + a multi-page notes document, restart Play mode, load it back, confirm visually that map/POIs/notes match.

---

## Out of Scope (v1)

- Autosave.
- Multiple simultaneously-open projects.
- Image/PNG export of the map.
- Cross-tool export formats.
- Migration logic for old `FormatVersion`s (added only once a real format change happens).
- "Открыть последние" pruning missing files from the persisted list itself (they're just skipped when building the menu — the stale path stays in `PlayerPrefs` until overwritten by the natural rolling-5 list).
