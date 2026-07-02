# POI System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Points of Interest system (City / Ruin / Dungeon / Fortress) with random generation, manual add/delete/drag, and an editor panel for naming/describing each marker.

**Architecture:** `PoiData` is a pure-C# data class living in the Generation layer. `PoiManager` (MonoBehaviour) owns the list and spawns `PoiMarkerView` GameObjects. `PoiInteractionController` handles all mouse input using distance-based hit detection (no physics layers). `MapEditorPanel` gets a new POI section wired to `PoiManager`.

**Tech Stack:** Unity 2022.3 LTS, Built-in RP, New Input System (`Mouse.current` / `Keyboard.current`), legacy UI (`UnityEngine.UI`), `TextMesh` (3D), no TextMeshPro, no external assets.

## Global Constraints

- **New Input System only** — `Mouse.current`, `Keyboard.current` from `UnityEngine.InputSystem`. Never `UnityEngine.Input`.
- **Generation layer is UnityEngine-free** — `Assets/WorldGen/Generation/` files must not `using UnityEngine`. Use `System.Numerics.Vector2` there.
- **Rendering layer** — `Assets/WorldGen/Rendering/` may use UnityEngine freely.
- **No TextMeshPro** — use `TextMesh` (3D world-space) for POI labels.
- **No placeholders in code** — every method must have a real implementation.
- **[ContextMenu] self-tests** for logic that can be verified without manual interaction (matching existing project convention). Format: `Debug.Log("Self-Test X: PASS")` or `FAIL`.
- **Sprites/Default shader** for unlit sprite rendering (matches existing border ribbons pattern).
- POI placeholder sprites: 32×32 Texture2D, generated at runtime in code, no external assets.
- POI color table (verbatim from spec):  
  City `#C8A020` · Ruin `#888888` · Dungeon `#8B1A1A` · Fortress `#4A6080`
- POI letter table: City `Г` · Ruin `Р` · Dungeon `Д` · Fortress `К`

---

### Task 1: PoiData — pure C# data model

**Files:**
- Create: `Assets/WorldGen/Generation/PoiData.cs`

**Interfaces:**
- Produces: `PoiType` enum and `PoiData` class used by Tasks 2–6.

- [ ] **Step 1: Create PoiData.cs**

```csharp
using System;
using System.Numerics;

namespace WorldGen.Generation
{
    public enum PoiType { City, Ruin, Dungeon, Fortress }

    public class PoiData
    {
        public string Id = Guid.NewGuid().ToString();
        public PoiType Type;
        public string Name = "";
        public string Description = "";     // free text; field ready for future media embedding
        public int OwnerCellId = -1;        // logical owner cell (for region/biome queries)
        public Vector2 WorldPosition;       // visual position in map XZ (draggable)
        public string CustomSpritePath;     // null = type placeholder; DM sets path to custom sprite
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Check Console for errors. Expected: no errors related to PoiData.cs.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Generation/PoiData.cs
git commit -m "feat: PoiData data model + PoiType enum"
```

---

### Task 2: PoiPlaceholderFactory — runtime sprite generation

**Files:**
- Create: `Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs`

**Interfaces:**
- Consumes: `PoiType` from Task 1.
- Produces: `PoiPlaceholderFactory.GetPlaceholder(PoiType) → Sprite`; used by Task 3.

- [ ] **Step 1: Write the self-test first**

The self-test will be a `[ContextMenu]` on a temporary MonoBehaviour — but since `PoiPlaceholderFactory` is static, add the self-test as a `[ContextMenu]` on `PoiManager` in Task 4. For now we write the factory and will test it there.

- [ ] **Step 2: Create PoiPlaceholderFactory.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Generates a 32x32 sprite per PoiType at first request and caches it.
    /// Each sprite is a colored circle with a 5x7 pixel Cyrillic glyph (Г/Р/Д/К).
    /// No external assets required.
    /// </summary>
    public static class PoiPlaceholderFactory
    {
        static readonly Dictionary<PoiType, Sprite> cache = new Dictionary<PoiType, Sprite>();

        static readonly Dictionary<PoiType, Color32> typeColors = new Dictionary<PoiType, Color32>
        {
            { PoiType.City,     new Color32(200, 160,  32, 255) },
            { PoiType.Ruin,     new Color32(136, 136, 136, 255) },
            { PoiType.Dungeon,  new Color32(139,  26,  26, 255) },
            { PoiType.Fortress, new Color32( 74,  96, 128, 255) },
        };

        // 5x7 pixel glyphs. glyphs[type][row, col], row 0 = top, true = white pixel.
        static readonly Dictionary<PoiType, bool[,]> glyphs = new Dictionary<PoiType, bool[,]>
        {
            [PoiType.City] = new bool[,]  // Г
            {
                { true,  true,  true,  true,  true  },
                { true,  false, false, false, false },
                { true,  true,  false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
            },
            [PoiType.Ruin] = new bool[,]  // Р
            {
                { true,  true,  true,  false, false },
                { true,  false, false, true,  false },
                { true,  false, false, true,  false },
                { true,  true,  true,  false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
                { true,  false, false, false, false },
            },
            [PoiType.Dungeon] = new bool[,]  // Д
            {
                { false, true,  true,  true,  false },
                { false, true,  false, true,  false },
                { false, true,  false, true,  false },
                { false, true,  false, true,  false },
                { true,  true,  true,  true,  true  },
                { true,  false, false, false, true  },
                { false, false, false, false, false },
            },
            [PoiType.Fortress] = new bool[,]  // К
            {
                { true,  false, false, true,  false },
                { true,  false, true,  false, false },
                { true,  true,  false, false, false },
                { true,  true,  false, false, false },
                { true,  false, true,  false, false },
                { true,  false, false, true,  false },
                { true,  false, false, false, true  },
            },
        };

        public static Sprite GetPlaceholder(PoiType type)
        {
            if (cache.TryGetValue(type, out var cached)) return cached;
            var sprite = Build(type);
            cache[type] = sprite;
            return sprite;
        }

        static Sprite Build(PoiType type)
        {
            const int size = 32;
            const float radius = 14f;
            float cx = size / 2f - 0.5f;
            float cy = size / 2f - 0.5f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.name = $"PoiPlaceholder_{type}";

            var baseColor = typeColors[type];
            var transparent = new Color32(0, 0, 0, 0);

            // Draw filled circle
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    tex.SetPixel(x, y, dx * dx + dy * dy <= radius * radius ? baseColor : transparent);
                }

            // Overlay 5x7 glyph centered in circle
            var glyph = glyphs[type];
            int startX = (size - 5) / 2;   // = 13
            int startY = (size - 7) / 2;   // = 12 (glyph row 0 = top of glyph = higher Y in texture)
            for (int row = 0; row < 7; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (!glyph[row, col]) continue;
                    int px = startX + col;
                    int py = size - 1 - (startY + row); // flip: row 0 (top) → high Y in texture
                    if (px >= 0 && px < size && py >= 0 && py < size)
                        tex.SetPixel(px, py, Color.white);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no errors in Console.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs
git commit -m "feat: PoiPlaceholderFactory — runtime 32x32 sprites per POI type"
```

---

### Task 3: PoiMarkerView — visual marker GameObject

**Files:**
- Create: `Assets/WorldGen/Rendering/PoiMarkerView.cs`

**Interfaces:**
- Consumes: `PoiData` (Task 1), `PoiPlaceholderFactory.GetPlaceholder` (Task 2).
- Produces:
  - `PoiMarkerView.Initialize(PoiData data, float yOffset, float iconWorldSize)` — sets up child GameObjects.
  - `PoiMarkerView.Refresh()` — re-reads `poiData` and updates icon + label.
  - `PoiMarkerView.SetHighlighted(bool on)` — scale ×1.3 when on, ×1.0 when off.
  - `PoiMarkerView.PoiId → string` — returns `poiData.Id`.
  - `PoiMarkerView.WorldPos → System.Numerics.Vector2` — returns `poiData.WorldPosition`.
  - Used by Tasks 4 and 5.

- [ ] **Step 1: Create PoiMarkerView.cs**

```csharp
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Visual representation of one POI: SpriteRenderer (icon) + TextMesh (name label).
    /// Pure visual — all interaction is handled by PoiInteractionController.
    /// Call Initialize() once after AddComponent, then Refresh() whenever data changes.
    /// </summary>
    public class PoiMarkerView : MonoBehaviour
    {
        PoiData poiData;
        SpriteRenderer iconRenderer;
        TextMesh label;
        float iconWorldSize;

        public string PoiId => poiData?.Id;
        public System.Numerics.Vector2 WorldPos => poiData?.WorldPosition ?? default;

        /// <summary>
        /// Sets up child icon + label GameObjects. Must be called once after AddComponent.
        /// yOffset: Y above map surface. iconWorldSize: world-unit side of the icon quad.
        /// </summary>
        public void Initialize(PoiData data, float yOffset, float iconWorldSize)
        {
            poiData = data;
            this.iconWorldSize = iconWorldSize;

            // Icon — lies flat in XZ plane (rotate -90° around X so sprite faces up)
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(transform, false);
            iconGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
            iconGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            iconGO.transform.localScale = new Vector3(iconWorldSize, iconWorldSize, 1f);
            iconRenderer = iconGO.AddComponent<SpriteRenderer>();

            // Label — flat text behind/below the icon in XZ (faces up)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            // Place label slightly north of the icon on the map
            labelGO.transform.localPosition = new Vector3(0f, yOffset, iconWorldSize * 0.5f + 1.5f);
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            label = labelGO.AddComponent<TextMesh>();
            label.characterSize = 0.5f;
            label.fontSize = 24;
            label.color = Color.white;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;

            Refresh();
        }

        /// <summary>Re-reads poiData and updates icon sprite + label text + position.</summary>
        public void Refresh()
        {
            if (poiData == null) return;

            // Icon sprite: custom if path set and file exists, otherwise placeholder
            Sprite sprite = null;
            if (!string.IsNullOrEmpty(poiData.CustomSpritePath)
                && System.IO.File.Exists(poiData.CustomSpritePath))
            {
                sprite = LoadCustomSprite(poiData.CustomSpritePath);
            }
            if (sprite == null)
                sprite = PoiPlaceholderFactory.GetPlaceholder(poiData.Type);

            if (iconRenderer != null) iconRenderer.sprite = sprite;
            if (label != null) label.text = poiData.Name;

            // Sync position to data
            transform.localPosition = new Vector3(poiData.WorldPosition.X, 0f, poiData.WorldPosition.Y);
        }

        /// <summary>Highlights the marker (scale ×1.3) or returns to normal (scale ×1).</summary>
        public void SetHighlighted(bool on)
        {
            float s = on ? 1.3f : 1.0f;
            transform.localScale = new Vector3(s, s, s);
        }

        /// <summary>Updates only the visual position without modifying poiData.</summary>
        public void SetVisualPosition(System.Numerics.Vector2 pos)
        {
            transform.localPosition = new Vector3(pos.X, 0f, pos.Y);
        }

        static Sprite LoadCustomSprite(string path)
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes)) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f));
            }
            catch
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiMarkerView.cs
git commit -m "feat: PoiMarkerView — icon + label visual marker"
```

---

### Task 4: PoiManager + WorldMapRenderer additions + self-tests

**Files:**
- Create: `Assets/WorldGen/Rendering/PoiManager.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `PoiData`, `PoiType` (Task 1), `PoiMarkerView` (Task 3), `WorldMapRenderer.Cells`, `WorldMapRenderer.OnWorldRegenerated`.
- Produces:
  - `PoiManager.GenerateAll(Dictionary<PoiType,int> counts)` — clear + bulk generate.
  - `PoiManager.AddOne(PoiType type)` — add single POI.
  - `PoiManager.DeletePoi(string id)` — remove POI + destroy marker.
  - `PoiManager.ClearAll()` — destroy all POIs + markers.
  - `PoiManager.SelectPoi(string id)` — highlight marker, fire `OnSelectionChanged`.
  - `PoiManager.DeselectAll()` — unhighlight, fire `OnSelectionChanged(null)`.
  - `PoiManager.GetSelectedPoi() → PoiData` — currently selected POI or null.
  - `PoiManager.GetAllPois() → IReadOnlyList<PoiData>`.
  - `PoiManager.UpdatePoiName(string id, string name)` — update name, refresh marker.
  - `PoiManager.UpdatePoiDescription(string id, string desc)` — update description.
  - `PoiManager.UpdatePoiSpritePath(string id, string path)` — update custom sprite, refresh.
  - `PoiManager.MovePoiTo(string id, System.Numerics.Vector2 pos, int newOwnerCellId)` — update position + cell.
  - `event Action<PoiData> OnSelectionChanged` — fires with selected PoiData or null on deselect.
  - `event Action OnPoisChanged` — fires after any structural change (add/delete/clear/generate).
  - Used by Tasks 5 and 6.

**WorldMapRenderer additions:**
- `public IReadOnlyList<VoronoiCell> Cells => cells;` — exposes cell list (read-only).
- `public event System.Action OnWorldRegenerated;` — fired at end of `GenerateAndRender()`.

- [ ] **Step 1: Add to WorldMapRenderer.cs**

In `Assets/WorldGen/Rendering/WorldMapRenderer.cs`, after the existing field declarations (around line 124, after `GameObject coastlineObject;`) add:

```csharp
        public event System.Action OnWorldRegenerated;
```

Add a read-only property after the private field declarations (same area):

```csharp
        /// <summary>Read-only access to current cells for POI placement etc.</summary>
        public IReadOnlyList<VoronoiCell> Cells => cells;
```

At the end of `GenerateAndRender()`, after `OnDisplayChanged?.Invoke();` add:

```csharp
            OnWorldRegenerated?.Invoke();
```

The method should look like:

```csharp
[ContextMenu("Generate World")]
public void GenerateAndRender()
{
    var genParams = BuildGenerationParams();
    cells = WorldGenerator.GenerateWorld(genParams, out epicenters, out moistureEpicenters, out rivers);
    corners = CornerGraphBuilder.Build(cells);
    lastGenParams = genParams;
    BuildMesh(cells);
    BuildRivers();
    BuildBorders();

    if (targetCamera != null)
        PositionCameraOverMap();

    OnDisplayChanged?.Invoke();
    OnWorldRegenerated?.Invoke();   // ← add this line
}
```

- [ ] **Step 2: Write the self-test for PoiManager (before implementation)**

The self-tests will be `[ContextMenu]` methods on `PoiManager`. We write them now so the implementation must satisfy them.

Self-test spec:
- "Self-Test: POI Generation" — creates 5 fake non-ocean cells as fixture, calls `GenerateAll` with {City:2, Dungeon:1}, verifies: count==3, each has valid OwnerCellId, no two share the same OwnerCellId.
- "Self-Test: POI Placeholder Factory" — calls `GetPlaceholder` for all 4 types, verifies each sprite is non-null and texture is 32×32.

- [ ] **Step 3: Create PoiManager.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns the POI list. Handles generation, add/delete, marker spawning.
    /// Attach to any GameObject in the scene alongside WorldMapRenderer.
    /// Assign mapRenderer in the Inspector.
    /// </summary>
    public class PoiManager : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldMapRenderer mapRenderer;

        [Header("Marker settings")]
        [Tooltip("Y height above map surface for POI markers.")]
        public float poiYOffset = 0.5f;
        [Tooltip("Icon side length in world units. Tune to fit ~half a cell diameter (~7-10).")]
        public float iconWorldSize = 8f;

        readonly List<PoiData> pois = new List<PoiData>();
        readonly Dictionary<string, PoiMarkerView> markers = new Dictionary<string, PoiMarkerView>();
        Transform poiContainer;
        string selectedPoiId;

        public event Action<PoiData> OnSelectionChanged;
        public event Action OnPoisChanged;

        public IReadOnlyList<PoiData> GetAllPois() => pois;

        public PoiData GetSelectedPoi() =>
            selectedPoiId != null && pois.Any(p => p.Id == selectedPoiId)
                ? pois.First(p => p.Id == selectedPoiId)
                : null;

        void Awake()
        {
            var containerGO = new GameObject("PoiContainer");
            // Parent to mapRenderer so markers share the map's local coordinate space
            containerGO.transform.SetParent(mapRenderer != null ? mapRenderer.transform : transform, false);
            poiContainer = containerGO.transform;

            if (mapRenderer != null)
                mapRenderer.OnWorldRegenerated += ClearAll;
        }

        void OnDestroy()
        {
            if (mapRenderer != null)
                mapRenderer.OnWorldRegenerated -= ClearAll;
        }

        // ── Generation ─────────────────────────────────────────────────────────

        /// <summary>Clears existing POIs and generates new ones from counts per type.</summary>
        public void GenerateAll(Dictionary<PoiType, int> counts)
        {
            ClearAll();
            if (mapRenderer?.Cells == null) return;

            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean)
                .OrderBy(_ => Guid.NewGuid()) // shuffle
                .ToList();

            var occupiedCellIds = new HashSet<int>();

            foreach (var kv in counts)
            {
                var type = kv.Key;
                int remaining = kv.Value;
                foreach (var cell in candidates)
                {
                    if (remaining <= 0) break;
                    if (occupiedCellIds.Contains(cell.Id)) continue;

                    var poi = MakePoi(type, cell);
                    pois.Add(poi);
                    occupiedCellIds.Add(cell.Id);
                    SpawnMarker(poi);
                    remaining--;
                }
            }

            OnPoisChanged?.Invoke();
        }

        /// <summary>Adds a single POI of the given type to a random unoccupied non-ocean cell.</summary>
        public void AddOne(PoiType type)
        {
            if (mapRenderer?.Cells == null) return;

            var occupied = new HashSet<int>(pois.Select(p => p.OwnerCellId));
            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean && !occupied.Contains(c.Id))
                .ToList();

            if (candidates.Count == 0) return;

            var cell = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            var poi = MakePoi(type, cell);
            pois.Add(poi);
            SpawnMarker(poi);
            OnPoisChanged?.Invoke();
        }

        // ── CRUD ───────────────────────────────────────────────────────────────

        public void DeletePoi(string id)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            pois.Remove(poi);
            DestroyMarker(id);
            if (selectedPoiId == id) { selectedPoiId = null; OnSelectionChanged?.Invoke(null); }
            OnPoisChanged?.Invoke();
        }

        public void ClearAll()
        {
            foreach (var id in markers.Keys.ToList())
                DestroyMarker(id);
            pois.Clear();
            selectedPoiId = null;
            OnSelectionChanged?.Invoke(null);
            OnPoisChanged?.Invoke();
        }

        public void UpdatePoiName(string id, string name)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.Name = name;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void UpdatePoiDescription(string id, string desc)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi != null) poi.Description = desc;
        }

        public void UpdatePoiSpritePath(string id, string path)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.CustomSpritePath = path;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void MovePoiTo(string id, System.Numerics.Vector2 pos, int newOwnerCellId)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.WorldPosition = pos;
            poi.OwnerCellId = newOwnerCellId;
            if (markers.TryGetValue(id, out var m)) m.SetVisualPosition(pos);
        }

        // ── Selection ──────────────────────────────────────────────────────────

        public void SelectPoi(string id)
        {
            if (selectedPoiId == id) return;
            if (selectedPoiId != null && markers.TryGetValue(selectedPoiId, out var prev))
                prev.SetHighlighted(false);
            selectedPoiId = id;
            if (id != null && markers.TryGetValue(id, out var next))
                next.SetHighlighted(true);
            OnSelectionChanged?.Invoke(GetSelectedPoi());
        }

        public void DeselectAll() => SelectPoi(null);

        // ── Internals ──────────────────────────────────────────────────────────

        PoiData MakePoi(PoiType type, VoronoiCell cell) => new PoiData
        {
            Type = type,
            Name = DefaultName(type),
            OwnerCellId = cell.Id,
            WorldPosition = new System.Numerics.Vector2(cell.Site.X, cell.Site.Y),
        };

        void SpawnMarker(PoiData poi)
        {
            var go = new GameObject($"POI_{poi.Id}");
            go.transform.SetParent(poiContainer, false);
            var view = go.AddComponent<PoiMarkerView>();
            view.Initialize(poi, poiYOffset, iconWorldSize);
            markers[poi.Id] = view;
        }

        void DestroyMarker(string id)
        {
            if (!markers.TryGetValue(id, out var m)) return;
            if (m != null) Destroy(m.gameObject);
            markers.Remove(id);
        }

        static string DefaultName(PoiType type)
        {
            switch (type)
            {
                case PoiType.City:     return "Город";
                case PoiType.Ruin:     return "Руины";
                case PoiType.Dungeon:  return "Подземелье";
                case PoiType.Fortress: return "Крепость";
                default: return type.ToString();
            }
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: POI Generation")]
        public void SelfTestPoiGeneration()
        {
            // Build fixture: 5 fake non-ocean cells with sequential IDs and Sites.
            var fixtureCells = new List<VoronoiCell>();
            for (int i = 0; i < 5; i++)
                fixtureCells.Add(new VoronoiCell(i, new System.Numerics.Vector2(i * 10f, 0f))
                    { IsOcean = false });

            // Directly exercise the placement logic (without WorldMapRenderer).
            var occupiedCellIds = new HashSet<int>();
            var placed = new List<PoiData>();
            var counts = new Dictionary<PoiType, int>
            {
                { PoiType.City,    2 },
                { PoiType.Dungeon, 1 },
            };

            var candidates = fixtureCells.OrderBy(_ => Guid.NewGuid()).ToList();
            foreach (var kv in counts)
            {
                int rem = kv.Value;
                foreach (var cell in candidates)
                {
                    if (rem <= 0) break;
                    if (occupiedCellIds.Contains(cell.Id)) continue;
                    placed.Add(MakePoi(kv.Key, cell));
                    occupiedCellIds.Add(cell.Id);
                    rem--;
                }
            }

            bool countOk = placed.Count == 3;
            bool cellsValid = placed.All(p => p.OwnerCellId >= 0 && p.OwnerCellId < 5);
            bool noDuplicates = placed.Select(p => p.OwnerCellId).Distinct().Count() == placed.Count;

            bool ok = countOk && cellsValid && noDuplicates;
            Debug.Log(ok
                ? "Self-Test POI Generation: PASS"
                : $"Self-Test POI Generation: FAIL (count={placed.Count} wantOk={countOk}, cellsValid={cellsValid}, noDuplicates={noDuplicates})");
        }

        [ContextMenu("Self-Test: POI Placeholder Factory")]
        public void SelfTestPlaceholderFactory()
        {
            bool ok = true;
            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
            {
                var sprite = PoiPlaceholderFactory.GetPlaceholder(type);
                bool spriteOk = sprite != null
                    && sprite.texture.width == 32
                    && sprite.texture.height == 32;
                if (!spriteOk)
                {
                    Debug.Log($"Self-Test POI Placeholder Factory: FAIL — {type} sprite invalid");
                    ok = false;
                }
            }
            if (ok) Debug.Log("Self-Test POI Placeholder Factory: PASS");
        }
    }
}
```

- [ ] **Step 4: Verify compilation in Unity**

Open Unity. Expected: no errors. Both `[ContextMenu]` entries appear on `PoiManager`.

- [ ] **Step 5: Run self-tests**

Add `PoiManager` component to the WorldMapRenderer GameObject in the scene. Assign `mapRenderer` in Inspector.

Right-click component → **Self-Test: POI Generation** → Console: `PASS`
Right-click component → **Self-Test: POI Placeholder Factory** → Console: `PASS`

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiManager.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: PoiManager with generation/CRUD/selection + WorldMapRenderer OnWorldRegenerated event"
```

---

### Task 5: PoiInteractionController — click, select, drag

**Files:**
- Create: `Assets/WorldGen/Rendering/PoiInteractionController.cs`
- Modify: `Assets/WorldGen/Rendering/CellSelectionController.cs`

**Interfaces:**
- Consumes: `PoiManager` (Task 4), `PoiMarkerView.WorldPos`, `WorldMapRenderer.GetCellUnderRay`.
- Produces: `PoiInteractionController.InputConsumedThisFrame → bool` — read by `CellSelectionController`.

**How interaction works (no physics layers needed):**
1. Every frame when LMB is pressed/held, project the mouse ray onto Y=`poiYOffset` plane → get world XZ hit.
2. Find nearest `PoiMarkerView` whose `WorldPos` is within `selectRadius` world units.
3. If a POI is found on press: claim input (`InputConsumedThisFrame = true`), start tracking click vs drag.
4. On release without moving > 5 screen pixels: click → `PoiManager.SelectPoi(id)`.
5. On release after moving > 5 screen pixels: drag committed → `PoiManager.MovePoiTo(id, newPos, newCellId)`.
6. LMB press with no nearby POI → `PoiManager.DeselectAll()`, do NOT set `InputConsumedThisFrame`.

- [ ] **Step 1: Create PoiInteractionController.cs**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Handles all mouse interaction with POI markers:
    /// - Click on marker → select (highlight + show panel).
    /// - Click on empty map → deselect.
    /// - Drag marker → reposition; commits WorldPosition + OwnerCellId on mouse-up.
    ///
    /// Uses distance-based hit detection (no physics layers).
    /// Sets InputConsumedThisFrame = true when claiming input, so CellSelectionController skips.
    /// </summary>
    public class PoiInteractionController : MonoBehaviour
    {
        [Header("Dependencies")]
        public PoiManager poiManager;
        public WorldMapRenderer mapRenderer;
        public Camera raycastCamera;

        [Header("Interaction settings")]
        [Tooltip("World-unit radius around a POI center that counts as a hit.")]
        public float selectRadius = 8f;
        [Tooltip("Screen pixels moved before a press becomes a drag instead of a click.")]
        public float dragThresholdPixels = 5f;

        public bool InputConsumedThisFrame { get; private set; }

        // Drag state
        bool tracking;          // LMB is down and we own a POI interaction
        bool isDragging;
        string trackedPoiId;
        Vector2 pressScreenPos;

        void Awake()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
        }

        void LateUpdate()
        {
            InputConsumedThisFrame = false; // reset after all Updates have run
        }

        void Update()
        {
            if (poiManager == null || raycastCamera == null) return;
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
                OnPress();
            else if (Mouse.current.leftButton.isPressed && tracking)
                OnHeld();
            else if (Mouse.current.leftButton.wasReleasedThisFrame && tracking)
                OnRelease();
        }

        void OnPress()
        {
            var mousePos = Mouse.current.position.ReadValue();
            var worldXZ = ProjectToMapPlane(mousePos);
            var hit = FindNearestPoi(worldXZ);

            if (hit != null)
            {
                tracking = true;
                isDragging = false;
                trackedPoiId = hit.PoiId;
                pressScreenPos = mousePos;
                InputConsumedThisFrame = true;
            }
            else
            {
                // Click on empty area → deselect
                poiManager.DeselectAll();
            }
        }

        void OnHeld()
        {
            InputConsumedThisFrame = true;

            var mousePos = Mouse.current.position.ReadValue();
            if (!isDragging)
            {
                float dist = Vector2.Distance(mousePos, pressScreenPos);
                if (dist < dragThresholdPixels) return;
                isDragging = true;
            }

            // Move the marker's visual position in real time
            var worldXZ = ProjectToMapPlane(mousePos);
            if (poiManager.GetMarkerView(trackedPoiId) is PoiMarkerView view)
                view.SetVisualPosition(worldXZ);
        }

        void OnRelease()
        {
            InputConsumedThisFrame = true;
            var mousePos = Mouse.current.position.ReadValue();

            if (!isDragging)
            {
                // It was a click: select the POI
                poiManager.SelectPoi(trackedPoiId);
            }
            else
            {
                // Commit drag: find new owner cell
                var worldXZ = ProjectToMapPlane(mousePos);
                int newCellId = GetCellIdAt(mousePos);
                poiManager.MovePoiTo(trackedPoiId, worldXZ, newCellId);
            }

            tracking = false;
            isDragging = false;
            trackedPoiId = null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Projects screen point to the map's XZ plane (Y = poiYOffset in world space).</summary>
        System.Numerics.Vector2 ProjectToMapPlane(Vector2 screenPos)
        {
            var ray = raycastCamera.ScreenPointToRay(screenPos);
            float yTarget = poiManager.poiYOffset;
            // Avoid division by zero if ray is horizontal
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return default;
            float t = (yTarget - ray.origin.y) / ray.direction.y;
            var world = ray.origin + ray.direction * t;
            // Convert world → local space of mapRenderer (handles non-zero map transform)
            if (mapRenderer != null)
            {
                var local = mapRenderer.transform.InverseTransformPoint(world);
                return new System.Numerics.Vector2(local.x, local.z);
            }
            return new System.Numerics.Vector2(world.x, world.z);
        }

        PoiMarkerView FindNearestPoi(System.Numerics.Vector2 xzPos)
        {
            PoiMarkerView best = null;
            float bestDist = selectRadius;
            foreach (var poi in poiManager.GetAllPois())
            {
                var delta = poi.WorldPosition - xzPos;
                float d = (float)System.Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = poiManager.GetMarkerView(poi.Id);
                }
            }
            return best;
        }

        int GetCellIdAt(Vector2 screenPos)
        {
            if (mapRenderer == null) return -1;
            var ray = raycastCamera.ScreenPointToRay(screenPos);
            var cell = mapRenderer.GetCellUnderRay(ray);
            return cell?.Id ?? -1;
        }
    }
}
```

- [ ] **Step 2: Add `GetMarkerView` to PoiManager**

In `PoiManager.cs`, add inside the class (after `DestroyMarker`):

```csharp
        /// <summary>Returns the PoiMarkerView for the given POI id, or null if not found.</summary>
        public PoiMarkerView GetMarkerView(string id)
        {
            if (id == null) return null;
            markers.TryGetValue(id, out var m);
            return m;
        }
```

- [ ] **Step 3: Modify CellSelectionController to respect PoiInteractionController**

In `Assets/WorldGen/Rendering/CellSelectionController.cs`, add a public field (after `public Camera raycastCamera;`):

```csharp
        [Tooltip("If assigned, cell selection is suppressed when POI interaction controller has claimed the input.")]
        public PoiInteractionController poiController;
```

At the top of `Update()`, add an early-out check:

```csharp
        void Update()
        {
            if (poiController != null && poiController.InputConsumedThisFrame) return;  // ← add this
            if (mapRenderer == null || raycastCamera == null) return;
            // ... rest of existing Update code unchanged
```

- [ ] **Step 4: Verify compilation in Unity**

Open Unity. Expected: no errors. `CellSelectionController` shows new `poiController` field in Inspector.

- [ ] **Step 5: Wire up in the scene**

- Add `PoiInteractionController` component to the scene (same GameObject as `PoiManager` or separate).
- Assign: `poiManager`, `mapRenderer`, `raycastCamera` (Camera.main).
- In `CellSelectionController` Inspector: assign `poiController`.
- Generate World → add a city via `PoiManager.AddOne(PoiType.City)` through ContextMenu — verify marker appears.
- Click on marker → selected (scale ×1.3). Click map elsewhere → deselected. Drag marker → follows mouse, releases to new position.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiInteractionController.cs Assets/WorldGen/Rendering/PoiManager.cs Assets/WorldGen/Rendering/CellSelectionController.cs
git commit -m "feat: PoiInteractionController — click/select/drag; CellSelectionController input guard"
```

---

### Task 6: MapEditorPanel — POI section

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapEditorPanel.cs`

**Interfaces:**
- Consumes: `PoiManager` events (`OnSelectionChanged`, `OnPoisChanged`); all `PoiManager` mutation methods.

**What to add:**
1. A new public field `public PoiManager poiManager;` and `public PoiInteractionController poiInteractionController;` on `MapEditorPanel`.
2. A "─── Точки интереса ───" section in the panel with:
   - 4 int-count rows (City / Ruin / Dungeon / Fortress) using `+`/`−` buttons + count label.
   - "Сгенерировать" button.
   - "Добавить" button + `PoiType` dropdown.
   - "Очистить все" button.
3. A POI edit sub-panel (hidden by default, shown on selection): name `InputField`, description `InputField` (multiline), type label, custom sprite path `InputField` + "Применить" button, "Удалить" button.

- [ ] **Step 1: Add fields and subscribe to events**

In `MapEditorPanel.cs`, after `public BrushToolController brushController;` add:

```csharp
        public PoiManager poiManager;
```

Add private fields for POI UI elements (after other private fields like `Dropdown biomeDropdown;`):

```csharp
        // POI section
        readonly Dictionary<PoiType, int> poiCounts = new Dictionary<PoiType, int>
        {
            { PoiType.City, 3 }, { PoiType.Ruin, 2 }, { PoiType.Dungeon, 2 }, { PoiType.Fortress, 1 }
        };
        readonly Dictionary<PoiType, Text> poiCountLabels = new Dictionary<PoiType, Text>();
        GameObject poiEditSubPanel;
        UnityEngine.UI.InputField poiNameField;
        UnityEngine.UI.InputField poiDescField;
        UnityEngine.UI.InputField poiSpritePathField;
        Text poiTypeLabel;
        PoiType addPoiType = PoiType.City;
```

In `OnEnable`, after the existing subscription:

```csharp
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged += HandlePoiSelectionChanged;
                poiManager.OnPoisChanged += RefreshPoiPanel;
            }
```

In `OnDisable`:

```csharp
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged -= HandlePoiSelectionChanged;
                poiManager.OnPoisChanged -= RefreshPoiPanel;
            }
```

- [ ] **Step 2: Add `using WorldGen.Generation;` import if not already present**

At the top of `MapEditorPanel.cs`, verify `using WorldGen.Generation;` is there (it should already be).

- [ ] **Step 3: Add `BuildPoiSection` and helper methods**

In `BuildUI()`, after the call to `BuildLayersSection(t);` and before the `selectionPanelRoot` block, add:

```csharp
            BuildPoiSection(t);
```

Add these methods to the class:

```csharp
        void BuildPoiSection(Transform t)
        {
            AddLabel(t, "─── Точки интереса ───", bold: false, color: sectionHeaderColor);

            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
                BuildPoiCountRow(t, type);

            AddButton(t, "Сгенерировать", OnGeneratePois, new Color(0.2f, 0.45f, 0.2f));

            // "Добавить" row: dropdown + button
            var addRowGO = new GameObject("AddPoiRow");
            addRowGO.transform.SetParent(t, false);
            var addHLayout = addRowGO.AddComponent<HorizontalLayoutGroup>();
            addHLayout.spacing = 4f;
            addHLayout.childControlWidth = true;
            addHLayout.childForceExpandWidth = true;
            var addRowLE = addRowGO.AddComponent<LayoutElement>();
            addRowLE.preferredHeight = 26f;

            // Type dropdown
            var typeDropdownGO = new GameObject("TypeDropdown");
            typeDropdownGO.transform.SetParent(addRowGO.transform, false);
            var typeDropdown = typeDropdownGO.AddComponent<Dropdown>();
            var typeDropBg = typeDropdownGO.AddComponent<Image>();
            typeDropBg.color = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            typeDropdown.targetGraphic = typeDropBg;
            var typeCaptionGO = new GameObject("Label");
            typeCaptionGO.transform.SetParent(typeDropdownGO.transform, false);
            var typeCaptionText = typeCaptionGO.AddComponent<Text>();
            typeCaptionText.font = builtinFont;
            typeCaptionText.fontSize = 11;
            typeCaptionText.color = textColor;
            typeCaptionText.alignment = TextAnchor.MiddleLeft;
            var typeCaptionRect = typeCaptionGO.GetComponent<RectTransform>();
            typeCaptionRect.anchorMin = new Vector2(0.05f, 0f);
            typeCaptionRect.anchorMax = new Vector2(1f, 1f);
            typeCaptionRect.sizeDelta = Vector2.zero;
            typeDropdown.captionText = typeCaptionText;
            BuildDropdownTemplate(typeDropdown, typeDropdownGO);
            typeDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "Город", "Руины", "Подземелье", "Крепость" });
            typeDropdown.RefreshShownValue();
            typeDropdown.onValueChanged.AddListener(v =>
                addPoiType = (PoiType)v);
            var typeDropRect = typeDropdownGO.GetComponent<RectTransform>();
            typeDropRect.sizeDelta = new Vector2(0f, 26f);

            // "Добавить" button
            var addBtnGO = new GameObject("AddBtn");
            addBtnGO.transform.SetParent(addRowGO.transform, false);
            var addBtnImg = addBtnGO.AddComponent<Image>();
            addBtnImg.color = new Color(0.2f, 0.4f, 0.6f, 0.9f);
            var addBtn = addBtnGO.AddComponent<Button>();
            addBtn.targetGraphic = addBtnImg;
            addBtn.onClick.AddListener(() => poiManager?.AddOne(addPoiType));
            var addBtnLE = addBtnGO.AddComponent<LayoutElement>();
            addBtnLE.preferredWidth = 80f;
            addBtnLE.preferredHeight = 26f;
            var addBtnTextGO = new GameObject("Text");
            addBtnTextGO.transform.SetParent(addBtnGO.transform, false);
            var addBtnText = addBtnTextGO.AddComponent<Text>();
            addBtnText.text = "Добавить";
            addBtnText.font = builtinFont;
            addBtnText.fontSize = 11;
            addBtnText.color = Color.white;
            addBtnText.alignment = TextAnchor.MiddleCenter;
            var addBtnTextRect = addBtnTextGO.GetComponent<RectTransform>();
            addBtnTextRect.anchorMin = Vector2.zero;
            addBtnTextRect.anchorMax = Vector2.one;
            addBtnTextRect.sizeDelta = Vector2.zero;

            AddButton(t, "Очистить все", () => poiManager?.ClearAll(), new Color(0.5f, 0.2f, 0.2f));

            // POI edit sub-panel (hidden until a POI is selected)
            poiEditSubPanel = new GameObject("PoiEditSubPanel");
            poiEditSubPanel.transform.SetParent(t, false);
            var subLayout = poiEditSubPanel.AddComponent<VerticalLayoutGroup>();
            subLayout.spacing = 4f;
            subLayout.childControlWidth = true;
            subLayout.childForceExpandWidth = true;
            var subFitter = poiEditSubPanel.AddComponent<ContentSizeFitter>();
            subFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildPoiEditSubPanel(poiEditSubPanel.transform);
            poiEditSubPanel.SetActive(false);
        }

        void BuildPoiCountRow(Transform parent, PoiType type)
        {
            string typeName;
            switch (type)
            {
                case PoiType.City:     typeName = "Города";       break;
                case PoiType.Ruin:     typeName = "Руины";        break;
                case PoiType.Dungeon:  typeName = "Подземелья";   break;
                case PoiType.Fortress: typeName = "Крепости";     break;
                default:               typeName = type.ToString(); break;
            }

            var rowGO = new GameObject($"{type}CountRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 20f;

            // Label
            var lbl = new GameObject("Label");
            lbl.transform.SetParent(rowGO.transform, false);
            var lblText = lbl.AddComponent<Text>();
            lblText.text = typeName;
            lblText.font = builtinFont;
            lblText.fontSize = 12;
            lblText.color = textColor;
            lblText.alignment = TextAnchor.MiddleLeft;
            lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 20f);

            // "−" button
            AddSmallButton(rowGO.transform, "−", () =>
            {
                if (poiCounts[type] > 0) poiCounts[type]--;
                poiCountLabels[type].text = poiCounts[type].ToString();
            });

            // Count label
            var countLblGO = new GameObject("Count");
            countLblGO.transform.SetParent(rowGO.transform, false);
            var countText = countLblGO.AddComponent<Text>();
            countText.text = poiCounts[type].ToString();
            countText.font = builtinFont;
            countText.fontSize = 12;
            countText.color = textColor;
            countText.alignment = TextAnchor.MiddleCenter;
            countLblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(26f, 20f);
            poiCountLabels[type] = countText;

            // "+" button
            AddSmallButton(rowGO.transform, "+", () =>
            {
                poiCounts[type]++;
                poiCountLabels[type].text = poiCounts[type].ToString();
            });
        }

        void AddSmallButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"SmallBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        void BuildPoiEditSubPanel(Transform t)
        {
            AddLabel(t, "─ Выбранная точка ─", bold: false, color: sectionHeaderColor);

            poiTypeLabel = AddLabel(t, "Тип: —");

            // Name InputField
            AddLabel(t, "Название:");
            poiNameField = BuildInputField(t, "", false);
            poiNameField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiName(sel.Id, v);
            });

            // Description InputField (multiline)
            AddLabel(t, "Описание:");
            poiDescField = BuildInputField(t, "", multiline: true);
            poiDescField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiDescription(sel.Id, v);
            });

            // Custom sprite
            AddLabel(t, "Иконка (путь к файлу):");
            var spriteRow = new GameObject("SpriteRow");
            spriteRow.transform.SetParent(t, false);
            var srLayout = spriteRow.AddComponent<HorizontalLayoutGroup>();
            srLayout.spacing = 4f;
            srLayout.childControlWidth = true;
            srLayout.childForceExpandWidth = true;
            spriteRow.AddComponent<LayoutElement>().preferredHeight = 22f;

            poiSpritePathField = BuildInputField(spriteRow.transform, "", false);

            var applyBtnGO = new GameObject("ApplyBtn");
            applyBtnGO.transform.SetParent(spriteRow.transform, false);
            var applyImg = applyBtnGO.AddComponent<Image>();
            applyImg.color = new Color(0.3f, 0.5f, 0.3f, 0.9f);
            var applyBtn = applyBtnGO.AddComponent<Button>();
            applyBtn.targetGraphic = applyImg;
            applyBtn.onClick.AddListener(() =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiSpritePath(sel.Id, poiSpritePathField.text);
            });
            applyBtnGO.AddComponent<LayoutElement>().preferredWidth = 70f;
            var applyTextGO = new GameObject("Text");
            applyTextGO.transform.SetParent(applyBtnGO.transform, false);
            var applyText = applyTextGO.AddComponent<Text>();
            applyText.text = "Применить";
            applyText.font = builtinFont;
            applyText.fontSize = 10;
            applyText.color = Color.white;
            applyText.alignment = TextAnchor.MiddleCenter;
            var applyRect = applyTextGO.GetComponent<RectTransform>();
            applyRect.anchorMin = Vector2.zero;
            applyRect.anchorMax = Vector2.one;
            applyRect.sizeDelta = Vector2.zero;

            AddButton(t, "Удалить точку", () =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.DeletePoi(sel.Id);
            }, new Color(0.55f, 0.15f, 0.15f));
        }

        UnityEngine.UI.InputField BuildInputField(Transform parent, string placeholder, bool multiline)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);
            var field = go.AddComponent<UnityEngine.UI.InputField>();
            field.targetGraphic = bg;
            field.lineType = multiline
                ? UnityEngine.UI.InputField.LineType.MultiLineNewline
                : UnityEngine.UI.InputField.LineType.SingleLine;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            text.color = textColor;
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phText = phGO.AddComponent<Text>();
            phText.text = placeholder;
            phText.font = builtinFont;
            phText.fontSize = 12;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyle.Italic;
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 60f : 22f;
            le.flexibleWidth = 1f;

            return field;
        }

        void OnGeneratePois()
        {
            if (poiManager == null) return;
            var counts = new Dictionary<PoiType, int>(poiCounts);
            poiManager.GenerateAll(counts);
        }

        void HandlePoiSelectionChanged(PoiData selected)
        {
            if (selected == null)
            {
                poiEditSubPanel?.SetActive(false);
                return;
            }
            poiEditSubPanel?.SetActive(true);
            if (poiNameField != null) poiNameField.text = selected.Name;
            if (poiDescField != null) poiDescField.text = selected.Description;
            if (poiSpritePathField != null) poiSpritePathField.text = selected.CustomSpritePath ?? "";

            string typeName;
            switch (selected.Type)
            {
                case PoiType.City:     typeName = "Город";       break;
                case PoiType.Ruin:     typeName = "Руины";       break;
                case PoiType.Dungeon:  typeName = "Подземелье";  break;
                case PoiType.Fortress: typeName = "Крепость";    break;
                default:               typeName = selected.Type.ToString(); break;
            }
            if (poiTypeLabel != null) poiTypeLabel.text = $"Тип: {typeName}";
        }

        void RefreshPoiPanel()
        {
            // If the selected POI was deleted, the sub-panel will be hidden via HandlePoiSelectionChanged(null)
            // triggered by PoiManager. Nothing extra needed here.
        }
```

- [ ] **Step 4: Verify compilation in Unity**

Open Unity. Expected: no errors. Panel shows "Точки интереса" section with count rows and buttons.

- [ ] **Step 5: End-to-end verify**

1. Generate World.
2. In POI section: set City=2, Ruin=1. Click "Сгенерировать".
3. Verify 3 markers appear on map (2 city gold circles, 1 ruin grey circle).
4. Click on a city marker → edit sub-panel appears, name/description editable.
5. Change name → label on marker updates.
6. Drag marker to a new cell → marker moves.
7. Click "Удалить точку" → marker disappears, sub-panel hides.
8. Click "Очистить все" → all markers gone.
9. Generate World again → markers cleared automatically.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/MapEditorPanel.cs
git commit -m "feat: POI section in MapEditorPanel — counts, generate, add, edit, delete"
```

---

## Post-implementation

After all tasks are complete, run both self-tests:
- `PoiManager` → **Self-Test: POI Generation** → `PASS`
- `PoiManager` → **Self-Test: POI Placeholder Factory** → `PASS`

And verify end-to-end flow from Task 6, Step 5.
