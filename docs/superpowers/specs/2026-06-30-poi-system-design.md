# Points of Interest (POI) System — Design

**Date:** 2026-06-30  
**Status:** Approved, ready for implementation  
**Branch:** implement off `main`

---

## Goal

Add a general Points of Interest system to the D&D world-map tool. The DM places typed markers (City, Ruin, Dungeon, Fortress) on the finished map, each with a name, description, and icon. Markers can be auto-generated in bulk, then individually added, deleted, or repositioned by dragging.

POI placement is a **separate stage** from map generation — the map is generated first, then the DM populates it with POIs.

---

## POI Types (v1)

| Type     | Enum value  | Placeholder color | Label letter |
|----------|-------------|-------------------|--------------|
| City     | `City`      | Gold (#C8A020)    | Г            |
| Ruin     | `Ruin`      | Grey (#888888)    | Р            |
| Dungeon  | `Dungeon`   | Dark red (#8B1A1A)| Д            |
| Fortress | `Fortress`  | Steel blue (#4A6080)| К          |

---

## Data Model

**`WorldGen/Generation/PoiData.cs`** — pure C#, no UnityEngine dependency.

```csharp
public enum PoiType { City, Ruin, Dungeon, Fortress }

public class PoiData
{
    public string Id;                    // GUID, assigned on creation
    public PoiType Type;
    public string Name;
    public string Description;           // free text; field exists for future media support
    public int OwnerCellId;              // logical owner cell (region/biome queries)
    public System.Numerics.Vector2 WorldPosition; // visual position on XZ plane (draggable)
    public string CustomSpritePath;      // null = use type placeholder; future: path to DM sprite
}
```

`WorldPosition` defaults to `cell.Site` on creation. Dragging updates `WorldPosition` only; `OwnerCellId` updates on mouse-up (nearest cell under cursor).

---

## Architecture

### New files

**`WorldGen/Generation/PoiData.cs`**  
Data class + `PoiType` enum. Pure C#, no UnityEngine.

**`WorldGen/Rendering/PoiManager.cs`**  
MonoBehaviour. Owns `List<PoiData>`. Responsibilities:
- Random generation: pick N non-ocean cells per type, create `PoiData`, spawn marker views.
- Add single POI (type + random or specified cell).
- Delete POI by Id.
- Spawn/destroy `PoiMarkerView` GameObjects to match the list.
- Holds reference to `WorldMapRenderer` for cell access.
- Exposes `event Action OnPoisChanged` for panel refresh.
- On map regeneration: cleared by `WorldMapRenderer` event (see below).

**`WorldGen/Rendering/PoiMarkerView.cs`**  
MonoBehaviour, one per POI GameObject. Responsibilities:
- Owns `SpriteRenderer` (icon) and `TextMesh` (name label).
- Tracks its `PoiData` reference; refreshes visuals when data changes.
- Handles drag: mouse-down on collider → drag mode → update `transform.position` each frame → mouse-up → commit `PoiData.WorldPosition`, call `WorldMapRenderer.GetCellUnderRay` to update `OwnerCellId`.
- Highlights on selection (scale ×1.3).

**`WorldGen/Rendering/PoiInteractionController.cs`**  
MonoBehaviour. New Input System (`Mouse.current`, `Keyboard.current`). Each `Update`:
1. Raycast on `PoiLayer` (dedicated Unity layer, configured in Inspector).
2. If hit → claim the input, pass to `PoiMarkerView` for drag/click; `CellSelectionController` skips this frame.
3. If miss → do nothing; `CellSelectionController` handles as usual.
4. Click vs drag threshold: 5 screen pixels.

**`WorldGen/Rendering/PoiPlaceholderFactory.cs`**  
Static class. `GetPlaceholder(PoiType) → Sprite`. Creates a 32×32 `Texture2D` at first call per type (filled circle in type color + white letter via pixel font blit or simple cross-pattern fallback), converts to `Sprite`, caches. No external assets required.

### Modified files

**`WorldGen/Rendering/MapEditorPanel.cs`**  
Adds a "Точки интереса" section after the existing mode/layer controls:
- 4 int fields (one per type) for bulk generation counts.
- **"Сгенерировать"** button → calls `PoiManager.GenerateAll(counts)` (clears existing POIs first).
- **"Добавить"** button + type dropdown → calls `PoiManager.AddOne(type)`.
- **"Очистить все"** button → calls `PoiManager.ClearAll()` (no confirmation dialog in v1 — keep simple).
- POI selection sub-panel (shown when a marker is selected):
  - Name `InputField`
  - Description `InputField` (multiline, 4 rows)
  - Type label (readonly)
  - "Сменить иконку" — path `InputField` + "Применить" button (sets `CustomSpritePath`, reloads sprite via `File.ReadAllBytes` → `Texture2D.LoadImage`).
  - "Удалить" button.

**`WorldGen/Rendering/WorldMapRenderer.cs`**  
Add `public event Action OnWorldRegenerated`. Fire it at the end of `GenerateWorld()` (after mesh + borders are built). `PoiManager` subscribes and clears all POIs when this fires.

---

## Rendering Detail

All POI GameObjects live under a single `poiContainer` Transform (child of the map, same pattern as `borderContainer`):

```
poiContainer (Transform, Y = 0)
  └── POI_{guid} (GameObject, PoiLayer)
        ├── SpriteRenderer  — icon, local rotation (-90°, 0°, 0°) to lie flat on XZ
        └── TextMesh        — name, position offset (0, 0, iconSize + 2f), white + black shadow mesh
```

Y height of markers: serialized `poiYOffset` field on `PoiManager` (default `0.5f`), set high enough to sit visually above both the map surface and border ribbon meshes. Adjust in Inspector if z-fighting occurs.

`BoxCollider` sized to icon world-space footprint on `PoiLayer` for raycasting.

Placeholder sprite: 32×32 `Texture2D`, filled circle using pixel-distance-from-center check, type color fill, single white letter (Г/Р/Д/К) drawn as a minimal 5×7 pixel glyph centered in the circle. No font asset required.

---

## Interaction Flow

### Bulk generation
1. DM sets counts in panel → clicks "Сгенерировать".
2. `PoiManager.GenerateAll`: collect all non-ocean cells → shuffle → pick first N for each type (skipping cells whose `Id` is already the `OwnerCellId` of an existing `PoiData`) → create `PoiData` for each → spawn `PoiMarkerView`.
3. If fewer cells available than requested, place as many as possible (no error, just fewer markers).

### Single add
1. DM clicks "Добавить" with type selected.
2. `PoiManager.AddOne(type)`: pick random non-ocean cell whose `Id` is not already any POI's `OwnerCellId` → create `PoiData` at cell site → spawn `PoiMarkerView`.

### Select and edit
1. Click on marker → `PoiInteractionController` detects raycast hit → notifies `PoiManager.SelectPoi(id)`.
2. Marker highlights (scale ×1.3). Panel shows POI sub-panel with current name/description.
3. DM edits fields → changes reflected in `PoiData` on each field change.
4. Click elsewhere on map → deselect, sub-panel hides.

### Drag to reposition
1. Mouse-down on marker, move > 5px → drag mode.
2. Each frame: project mouse ray onto XZ plane (Y=0), move marker to hit point.
3. Mouse-up → commit `PoiData.WorldPosition`, call `GetCellUnderRay` → update `OwnerCellId`.

### Map regeneration
`WorldMapRenderer` fires `OnWorldRegenerated` → `PoiManager.ClearAll()` → all marker GameObjects destroyed, list cleared.

---

## Storage (v1 scope)

POIs live in memory only for now. Persistence will be added as part of the map export/import feature (roadmap item 3). The `PoiData` class is designed to be JSON-serializable (plain fields, no Unity types in the data layer) so that `JsonUtility` or `System.Text.Json` can serialize `List<PoiData>` directly when that step arrives.

---

## Testing

`[ContextMenu]` self-checks on `PoiManager` (matching project convention):

**Self-Test: POI Generation** — generates 2 City + 1 Dungeon on a minimal fixture of 5 non-ocean cells. Verifies count, that each has a valid `OwnerCellId`, that no two share the same `OwnerCellId`.

**Self-Test: POI Placeholder Factory** — calls `GetPlaceholder` for all 4 types, verifies non-null sprites with correct dimensions (32×32).

---

## Out of Scope (v1)

- Type-specific placement rules (cities prefer fertile land, etc.) — random placement for now.
- Persistence / save-load — deferred to export/import feature.
- Image/gif in description — field `CustomSpritePath` exists; media embedding is future work.
- Per-type icon management UI — DM sets path per individual POI.
- Confirmation dialog on "Clear all" — keep simple for now.
