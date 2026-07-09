# Region Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-seeded, editable, persistent Latin region-name labels rendered as a zoom-LOD screen-space overlay (visible zoomed-out, fade on zoom-in).

**Architecture:** Three decoupled parts under `Assets/WorldGen/Rendering/RegionLabels/` — a pure-C# `RegionLabelPlacer` (flood-fill biome-family patches → centroids + Latin names), a `RegionLabelManager` MonoBehaviour (CRUD + seed + events + persistence handoff, a lighter `PoiManager`), and a `RegionLabelOverlay` MonoBehaviour (uGUI/TMP labels: world→screen projection, LOD alpha, basic collision, inline rename/move/delete/add). Persistence adds a `RegionLabels` list to the project save. Spec: `docs/superpowers/specs/2026-07-09-region-labels-design.md`.

**Tech Stack:** Unity 6000.3.2f1, C#, uGUI ScreenSpaceOverlay, TextMeshPro, Newtonsoft (int-enum serialization), System.Numerics.Vector2 for map coords.

## Global Constraints

- **Unity, no CLI test runner:** implementers write code + a static hand-trace self-review + commit (report DONE_WITH_CONCERNS since they can't compile). `[ContextMenu]` self-tests and visual checks are the USER's Editor step, batched at checkpoints. Do NOT claim tests were run.
- **Namespace:** `WorldGen.Rendering.RegionLabels` for the new files. `BiomeFamily` lives in the `MapRaster` namespace (`Assets/WorldGen/Rendering/MapRaster/MapPalette.cs`) — reference it fully-qualified or `using` it; grep the file for its exact namespace before use.
- **`System.Numerics.Vector2` vs `UnityEngine.Vector2`:** map/world coords use `System.Numerics.Vector2` (`VoronoiCell.Site`, `RegionLabelData.WorldPosition`). UI code uses `UnityEngine.Vector2`. This project has been bitten by CS0104 ambiguity 3× — fully-qualify wherever both `using`s are present.
- **Family source:** group by `RegionCategories.FamilyCategoryOf(cell)` (int family for land, −1 for water) so labels match the rendered biomes. `RegionCategories` is in `Assets/WorldGen/Rendering/MapRaster/RegionCategories.cs`.
- **Latin name table** (exact, uppercase): Forest→`SILVA UMBRARUM`, ForestWarm→`SILVA IGNEA`, Badlands→`VASTA CINERIS`, Plains→`CAMPI CANI`, Highland→`DORSUM CORVI`, Snow→`NIX AETERNA`, Moor→`PALUS NIGRA`, Tundra→`GLACIES`. Sea→`OCEANUS UMBRAE` / `MARE GELIDUM`. Coast/Lake unnamed (skip).
- **Defaults:** `minPatchCells = 6`; LOD `farFrac = 0.8f`, `nearFrac = 0.35f` (alpha full when `orthoSize ≥ farFrac·NaturalFitSize`, 0 when `≤ nearFrac·NaturalFitSize`); labels constant screen size.
- **Persistence is additive:** new `List<RegionLabelData> RegionLabels` in `ProjectSaveData`; old `.dndproj` (no field) → null → `Load` substitutes empty list. No format-version bump.
- **Not saved-recomputed:** labels are user-owned after the generation seed; brush edits do NOT re-run the placer.

---

### Task 1: RegionLabelData + RegionLabelPlacer (pure C# + self-test)

**Files:**
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelData.cs`
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs`
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs`

**Interfaces:**
- Produces: `RegionLabelData { string Id, string Text, System.Numerics.Vector2 WorldPosition, BiomeFamily SeedFamily }`.
- Produces: `RegionLabelPlacer.Place(IReadOnlyList<VoronoiCell> cells, NearestCellLookup nearest, float mapWidth, float mapHeight, int minPatchCells=6) : List<RegionLabelData>`.

- [ ] **Step 1: Data model**

`RegionLabelData.cs`:
```csharp
using System;
using WorldGen.Rendering.MapRaster; // BiomeFamily (verify this is the enum's namespace by grepping MapPalette.cs)

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>One editable region name label. Seeded from a biome-family patch centroid, then
    /// user-owned (rename/move/delete/add) and saved in the .dndproj. Latin name is the default Text.</summary>
    [Serializable]
    public class RegionLabelData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text;                            // shown name (Latin default; DM edits)
        public System.Numerics.Vector2 WorldPosition;  // XZ world anchor (map coords)
        public BiomeFamily SeedFamily;                 // family it was seeded from (reference)
    }
}
```

- [ ] **Step 2: Placer — flood-fill patches + Latin names + sea labels**

`RegionLabelPlacer.cs`. First grep `VoronoiCell.cs` to confirm field types (`int Id`, `System.Numerics.Vector2 Site`, `List<System.Numerics.Vector2> Polygon`, `List<int> NeighborIds`, `bool EffectiveIsOcean`) and `NearestCellLookup`'s nearest-cell method name (grep `NearestCellLookup.cs`; the call below assumes `NearestCell(System.Numerics.Vector2) : VoronoiCell` — adapt to the real signature).
```csharp
using System;
using System.Collections.Generic;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster; // BiomeFamily, RegionCategories

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Pure, deterministic. Groups adjacent land cells of the same BiomeFamily into connected
    /// patches (BFS over NeighborIds) and emits one Latin-named label per patch >= minPatchCells, at the
    /// area-weighted centroid. Adds 1-2 sea labels at open-ocean anchor points. No Random.</summary>
    public static class RegionLabelPlacer
    {
        public const int DefaultMinPatchCells = 6;

        static readonly Dictionary<BiomeFamily, string> LandNames = new Dictionary<BiomeFamily, string>
        {
            { BiomeFamily.Forest,     "SILVA UMBRARUM" },
            { BiomeFamily.ForestWarm, "SILVA IGNEA" },
            { BiomeFamily.Badlands,   "VASTA CINERIS" },
            { BiomeFamily.Plains,     "CAMPI CANI" },
            { BiomeFamily.Highland,   "DORSUM CORVI" },
            { BiomeFamily.Snow,       "NIX AETERNA" },
            { BiomeFamily.Moor,       "PALUS NIGRA" },
            { BiomeFamily.Tundra,     "GLACIES" },
            // Coast, Lake, Sea intentionally absent -> unnamed (skipped / sea handled separately).
        };

        public static List<RegionLabelData> Place(IReadOnlyList<VoronoiCell> cells,
            NearestCellLookup nearest, float mapWidth, float mapHeight, int minPatchCells = DefaultMinPatchCells)
        {
            var result = new List<RegionLabelData>();
            if (cells == null || cells.Count == 0) return result;

            var byId = new Dictionary<int, VoronoiCell>();
            foreach (var c in cells) byId[c.Id] = c;

            var visited = new HashSet<int>();
            foreach (var start in cells)
            {
                if (visited.Contains(start.Id)) continue;
                int fam = RegionCategories.FamilyCategoryOf(start);
                if (fam < 0) { visited.Add(start.Id); continue; } // water: skip (marked visited so we don't re-scan)

                // BFS connected component of the same family.
                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        if (RegionCategories.FamilyCategoryOf(nc) != fam) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }

                if (comp.Count < minPatchCells) continue;
                if (!LandNames.TryGetValue((BiomeFamily)fam, out var name)) continue; // Coast etc. unnamed

                result.Add(new RegionLabelData
                {
                    Text = name,
                    WorldPosition = AreaWeightedCentroid(comp),
                    SeedFamily = (BiomeFamily)fam,
                });
            }

            AddSeaLabels(result, nearest, mapWidth, mapHeight);
            return result;
        }

        static System.Numerics.Vector2 AreaWeightedCentroid(List<VoronoiCell> comp)
        {
            double sx = 0, sy = 0, sw = 0;
            foreach (var c in comp)
            {
                float w = PolygonArea(c.Polygon);
                if (w <= 0f) w = 1f;
                sx += (double)c.Site.X * w; sy += (double)c.Site.Y * w; sw += w;
            }
            if (sw <= 0) return comp[0].Site;
            return new System.Numerics.Vector2((float)(sx / sw), (float)(sy / sw));
        }

        static float PolygonArea(List<System.Numerics.Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                a += (double)p.X * q.Y - (double)q.X * p.Y;
            }
            return (float)(Math.Abs(a) * 0.5);
        }

        // Two candidate open-ocean anchors (handoff normalized positions). Emit a label only if the
        // nearest cell there is actually water -> avoids labels on the continent for oddly-shaped maps.
        static void AddSeaLabels(List<RegionLabelData> result, NearestCellLookup nearest, float mapW, float mapH)
        {
            if (nearest == null) return;
            (float nx, float ny, string name)[] cands =
            {
                (0.135f, 0.43f, "MARE GELIDUM"),
                (0.835f, 0.90f, "OCEANUS UMBRAE"),
            };
            foreach (var (nx, ny, name) in cands)
            {
                var pos = new System.Numerics.Vector2(nx * mapW, ny * mapH);
                var cell = nearest.NearestCell(pos); // adapt to real API
                if (cell != null && cell.EffectiveIsOcean)
                    result.Add(new RegionLabelData { Text = name, WorldPosition = pos, SeedFamily = BiomeFamily.Sea });
            }
        }
    }
}
```

- [ ] **Step 3: Self-test host**

`RegionLabelSelfTests.cs` — mirror `ProjectSerializerSelfTests`'s `[ContextMenu]` MonoBehaviour style. Build a fixture of ~12 cells forming two land patches of different families (each ≥6 cells) plus some water, with square polygons on each cell (`Polygon` = a small 4-corner square around `Site` — required so `NearestCellLookup`/area work; mirror `ProjectSerializerSelfTests` cell fixtures and `DecorationPlacer`'s fixture that adds 4-corner polygons), wire `NeighborIds` so each patch is connected, and assert:
```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.RegionLabels
{
    public class RegionLabelSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Region Label Placer")]
        public void SelfTestPlacer()
        {
            // Build fixture: patch A (Forest) cells 0-6 chained, patch B (Plains) cells 7-13 chained, all land.
            // (Grep BiomeClassifier for a Biome whose GetFamily == Forest / == Plains to set cell.Biome so
            //  RegionCategories.FamilyCategoryOf returns those families; e.g. Biome.TemperateRainForest / Grassland.)
            var cells = new List<VoronoiCell>();
            // ... construct per the comment: each cell gets Id, Site (spread apart), a 4-corner square Polygon,
            //     Biome set so its family matches, IsOcean=false, and NeighborIds chaining within its patch ...

            var labels = RegionLabelPlacer.Place(cells, /*nearest*/ null, 100f, 100f, minPatchCells: 6);

            bool ok = labels.Count == 2;                                   // two patches labeled
            ok &= labels.Exists(l => l.Text == "SILVA UMBRARUM");
            ok &= labels.Exists(l => l.Text == "CAMPI CANI");
            // centroid of patch A lies within its cells' bbox:
            var a = labels.Find(l => l.Text == "SILVA UMBRARUM");
            ok &= a != null && a.WorldPosition.X >= 0 && a.WorldPosition.X <= 100;
            // below-threshold patch is dropped: re-run with a 1-cell family, expect it absent (add a lone cell).

            Debug.Log(ok ? "Self-Test Region Label Placer: PASS" : "Self-Test Region Label Placer: FAIL");
        }
    }
}
```
Write the fixture construction out in full (no `...`) — spread `Site`s so patches don't touch, give each cell a 4-corner square `Polygon`, and chain `NeighborIds` within each patch. Include a lone under-threshold land cell of a third family and assert it produces no label.

- [ ] **Step 4: USER runs the self-test**
User adds a `RegionLabelSelfTests` component to a scene GameObject → right-click → "Self-Test: Region Label Placer" → expect PASS.

- [ ] **Step 5: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelData.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs
git commit -m "feat(region-labels): data model + flood-fill placer (Latin biome names + sea labels) + self-test"
```

---

### Task 2: RegionLabelManager (CRUD + seed + events)

**Files:**
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelManager.cs`

**Interfaces:**
- Consumes: `RegionLabelPlacer.Place`, `RegionLabelData`, `NearestCellLookup`, `WorldMapRenderer` (for cells + mapWidth/mapHeight + nearestLookup).
- Produces: `RegionLabelManager` with `List<RegionLabelData> GetAll()`, events `OnLabelsChanged`, `OnSelectionChanged(RegionLabelData)`, and methods `SeedFromCells()`, `LoadLabels(List<RegionLabelData>)`, `ClearAll()`, `AddLabel(System.Numerics.Vector2, string)→string id`, `DeleteLabel(string)`, `RenameLabel(string,string)`, `MoveLabel(string, System.Numerics.Vector2)`, `SelectLabel(string)`, `DeselectAll()`, `RegionLabelData GetSelected()`.

- [ ] **Step 1: Manager class**

Mirror `PoiManager`'s shape (list + events + CRUD + `[ContextMenu]` self-tests). It needs an Inspector ref to `WorldMapRenderer mapRenderer` (source of `Cells`, `mapWidth`, `mapHeight`, `nearestLookup` — grep `PoiManager`/`WorldMapRenderer` for the exact member names, e.g. `mapRenderer.Cells`, `mapRenderer.mapWidth`; `PoiManager` already reads `mapRenderer.Cells`/`mapRenderer.mapWidth` and a nearest lookup — copy that access).
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Owns the editable region-label list. Auto-seeds from biome-family patches on world
    /// generation, then labels are user-owned (rename/move/delete/add) and saved in the project.</summary>
    public class RegionLabelManager : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;

        readonly List<RegionLabelData> labels = new List<RegionLabelData>();
        string selectedId;

        public event Action OnLabelsChanged;
        public event Action<RegionLabelData> OnSelectionChanged;

        public IReadOnlyList<RegionLabelData> GetAll() => labels;
        public RegionLabelData GetSelected() =>
            selectedId != null ? labels.FirstOrDefault(l => l.Id == selectedId) : null;

        /// <summary>Runs the placer over the current map and REPLACES the list (fresh seed per generation).</summary>
        public void SeedFromCells()
        {
            if (mapRenderer == null || mapRenderer.Cells == null) return;
            var seeded = RegionLabelPlacer.Place(mapRenderer.Cells, mapRenderer.NearestLookup,
                mapRenderer.mapWidth, mapRenderer.mapHeight);
            labels.Clear();
            labels.AddRange(seeded);
            selectedId = null;
            OnLabelsChanged?.Invoke();
        }

        public void LoadLabels(List<RegionLabelData> loaded)
        {
            labels.Clear();
            if (loaded != null) labels.AddRange(loaded);
            selectedId = null;
            OnLabelsChanged?.Invoke();
        }

        public void ClearAll()
        {
            labels.Clear(); selectedId = null; OnLabelsChanged?.Invoke();
        }

        public string AddLabel(System.Numerics.Vector2 worldPos, string text)
        {
            var d = new RegionLabelData { Text = string.IsNullOrEmpty(text) ? "NOVA REGIO" : text, WorldPosition = worldPos };
            labels.Add(d);
            OnLabelsChanged?.Invoke();
            SelectLabel(d.Id);
            return d.Id;
        }

        public void DeleteLabel(string id)
        {
            int n = labels.RemoveAll(l => l.Id == id);
            if (n > 0) { if (selectedId == id) selectedId = null; OnLabelsChanged?.Invoke(); }
        }

        public void RenameLabel(string id, string text)
        {
            var d = labels.FirstOrDefault(l => l.Id == id);
            if (d != null) { d.Text = text; OnLabelsChanged?.Invoke(); }
        }

        public void MoveLabel(string id, System.Numerics.Vector2 worldPos)
        {
            var d = labels.FirstOrDefault(l => l.Id == id);
            if (d != null) { d.WorldPosition = worldPos; OnLabelsChanged?.Invoke(); }
        }

        public void SelectLabel(string id)
        {
            selectedId = id;
            OnSelectionChanged?.Invoke(GetSelected());
        }

        public void DeselectAll()
        {
            selectedId = null;
            OnSelectionChanged?.Invoke(null);
        }
    }
}
```
Note: `mapRenderer.NearestLookup` / `mapRenderer.Cells` / `mapRenderer.mapWidth` / `mapRenderer.mapHeight` — grep `WorldMapRenderer.cs` for the EXACT public accessors (PoiManager already uses `mapRenderer.Cells` and `mapRenderer.mapWidth`; find how PoiManager reaches the nearest lookup and reuse it). If a member is non-public, use the same route PoiManager uses.

- [ ] **Step 2: `[ContextMenu]` self-tests on the manager**

Add self-tests (mirror `PoiManager`'s `[ContextMenu]` tests): `SelfTestCrud` — start empty, `AddLabel` → count 1 + selected; `RenameLabel` → Text changed; `MoveLabel` → WorldPosition changed; `DeleteLabel` → count 0 + selection cleared; each asserted, `Debug.Log` PASS/FAIL. (Seed is covered by Task 1's placer test + the visual checkpoint.)

- [ ] **Step 3: USER runs the self-test** → "Self-Test: Region Label CRUD" → expect PASS.

- [ ] **Step 4: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelManager.cs
git commit -m "feat(region-labels): manager (CRUD + seed + events) + self-test"
```

---

### Task 3: Persistence (ProjectSerializer + round-trip test)

**Files:**
- Modify: `Assets/WorldGen/Persistence/ProjectSerializer.cs` (+ `ProjectSaveData`, `ProjectLoadResult`)
- Modify: `Assets/WorldGen/Persistence/ProjectSerializerSelfTests.cs`

**Interfaces:**
- Consumes: `RegionLabelData` (Task 1).
- Produces: `ProjectSerializer.Save(path, genParams, cells, pois, notes, regionLabels)` + `ProjectLoadResult.RegionLabels`.

- [ ] **Step 1: Extend the save model + Save/Load**

Read `ProjectSerializer.cs` and its `ProjectSaveData` (grep for `class ProjectSaveData` — likely in the same file or a sibling). Add `public List<RegionLabelData> RegionLabels;` to `ProjectSaveData` and `ProjectLoadResult`. Extend `Save`'s signature with `IReadOnlyList<RegionLabelData> regionLabels` (append as the last param) and set `RegionLabels = new List<RegionLabelData>(regionLabels)`. In `Load`, set `result.RegionLabels = data.RegionLabels ?? new List<RegionLabelData>()` (null-safe like `Cells`/`Pois`). Add `using WorldGen.Rendering.RegionLabels;`.

Then update the ONE existing `ProjectSerializer.Save(...)` call site (grep the codebase for `ProjectSerializer.Save(` — likely in `WorldMapRenderer`/`MapScreenController`/a save controller) to pass the region-label list (`regionLabelManager.GetAll()` cast to a `List` — the wiring for that reference is Task 6; for now pass whatever list the save controller can reach, or an empty list with a `// TODO Task 6: wire regionLabelManager.GetAll()` — NO, do not leave a TODO: if the manager ref isn't available at the call site yet, pass `new List<RegionLabelData>()` and note it in the report as "call site passes empty until Task 6 wires the manager"). The round-trip self-test below exercises the format directly, not this call site.

- [ ] **Step 2: Round-trip self-test**

Extend `ProjectSerializerSelfTests` (mirror the existing `SelfTestRoundTrip` / `SelfTestPoiTypeBackwardCompat` pattern that uses `ProjectSerializer.Save(path,...)`+`Load(path)`). Add a new `[ContextMenu("Self-Test: Region Labels Round-Trip")]`:
```csharp
var regionLabels = new System.Collections.Generic.List<WorldGen.Rendering.RegionLabels.RegionLabelData>
{
    new WorldGen.Rendering.RegionLabels.RegionLabelData
    { Text = "Мои Земли", WorldPosition = new System.Numerics.Vector2(12.5f, 34.5f),
      SeedFamily = WorldGen.Rendering.MapRaster.BiomeFamily.Forest },
};
string path = System.IO.Path.Combine(Application.temporaryCachePath, "region_labels_selftest.json");
ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10f, Height = 10f },
    new System.Collections.Generic.List<VoronoiCell>(),
    new System.Collections.Generic.List<PoiData>(),
    new NotesDocument(), regionLabels);
var result = ProjectSerializer.Load(path);
// old-save compat: a JSON with no RegionLabels field -> empty list (not null).
string legacy = System.IO.File.ReadAllText(path).Replace("\"RegionLabels\"", "\"RegionLabelsRenamed\"");
System.IO.File.WriteAllText(path, legacy);
var legacyResult = ProjectSerializer.Load(path);
System.IO.File.Delete(path);

bool ok = result.Success
    && result.RegionLabels.Count == 1
    && result.RegionLabels[0].Text == "Мои Земли"
    && result.RegionLabels[0].WorldPosition == new System.Numerics.Vector2(12.5f, 34.5f)
    && legacyResult.Success && legacyResult.RegionLabels != null && legacyResult.RegionLabels.Count == 0;
Debug.Log(ok ? "Self-Test Region Labels Round-Trip: PASS" : "Self-Test Region Labels Round-Trip: FAIL");
```
Match the file's real helper conventions (it already `using`s the needed namespaces; add `RegionLabels` if missing).

- [ ] **Step 3: USER runs the self-test** → "Self-Test: Region Labels Round-Trip" → PASS.

- [ ] **Step 4: Commit**
```bash
git add Assets/WorldGen/Persistence/ProjectSerializer.cs Assets/WorldGen/Persistence/ProjectSerializerSelfTests.cs
git commit -m "feat(region-labels): persist RegionLabels in .dndproj (additive) + round-trip test"
```

---

### Task 4: RegionLabelOverlay — render + LOD (no editing)

**Files:**
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs`
- USER Editor asset step (font).

**Interfaces:**
- Consumes: `RegionLabelManager` (`GetAll`, `OnLabelsChanged`), `MapCameraController` (`targetCamera`, `NaturalFitSize`), a `TMP_FontAsset` ref.
- Produces: `RegionLabelOverlay` with `SetVisible(bool)`.

- [ ] **Step 1: USER — bundle the font**
User: download **IM Fell English** (OFL) from Google Fonts → put `.ttf` in `Assets/Fonts/` → Window ▸ TextMeshPro ▸ Font Asset Creator → create a TMP Font Asset (Latin uppercase + digits + punctuation suffice) → save under `Assets/Fonts/`. Also do TMP ▸ Import TMP Essentials once if prompted. (The overlay exposes a `public TMP_FontAsset labelFont;` to assign.)

- [ ] **Step 2: Overlay — canvas + per-label TMP construction + projection + LOD**

Mirror `PoiEditPanel`'s ScreenSpaceOverlay canvas creation (grep `PoiEditPanel.BuildUI` for the `Canvas`/`CanvasScaler`/`GraphicRaycaster` setup and `PoiMarkerView`/`PoiEditPanel` for `EnsureEventSystemExists`). Build one `TextMeshProUGUI` per label, rebuild on `OnLabelsChanged`, and in `LateUpdate` project + LOD-fade:
```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Screen-space overlay for region labels: one TMP text per label, projected from its world
    /// centroid each frame, alpha driven by camera zoom (visible zoomed-out, fades in when zoomed in).</summary>
    public class RegionLabelOverlay : MonoBehaviour
    {
        [Header("Источники")]
        public RegionLabelManager manager;
        public MapCameraController cameraController;
        public TMP_FontAsset labelFont;

        [Header("LOD (доли от NaturalFitSize)")]
        [Range(0f,1f)] public float farFrac = 0.8f;   // >= этого (отдалено) -> полностью видно
        [Range(0f,1f)] public float nearFrac = 0.35f; // <= этого (приближено) -> скрыто
        public float baseFontSize = 26f;
        public float labelYOffsetWorld = 0.5f;         // приподнять точку привязки над картой

        bool visible = true;
        RectTransform canvasRect;
        readonly Dictionary<string, TextMeshProUGUI> views = new Dictionary<string, TextMeshProUGUI>();

        void Awake()
        {
            BuildCanvas();          // mirror PoiEditPanel's canvas setup; store canvasRect
            if (manager != null) manager.OnLabelsChanged += Rebuild;
            Rebuild();
        }
        void OnDestroy() { if (manager != null) manager.OnLabelsChanged -= Rebuild; }

        public void SetVisible(bool on) { visible = on; if (canvasRect != null) canvasRect.gameObject.SetActive(on); }

        void Rebuild()
        {
            foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
            views.Clear();
            if (manager == null) return;
            foreach (var d in manager.GetAll()) views[d.Id] = CreateLabelView(d);
        }

        TextMeshProUGUI CreateLabelView(RegionLabelData d)
        {
            var go = new GameObject($"RegionLabel_{d.Id}");
            go.transform.SetParent(canvasRect, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (labelFont != null) tmp.font = labelFont;
            tmp.text = d.Text;
            tmp.fontSize = baseFontSize;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 8f;                 // letter-spacing
            tmp.color = new Color(0.86f, 0.84f, 0.78f, 1f);
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;                 // click handled by an overlaid button in Task 5
            tmp.outlineWidth = 0.18f;                  // dark halo (needs an outline-capable material preset)
            tmp.outlineColor = new Color32(6, 10, 16, 220);
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(220f, 34f);
            return tmp;
        }

        void LateUpdate()
        {
            if (!visible || manager == null || cameraController == null) return;
            var cam = cameraController.targetCamera;
            float refSize = cameraController.NaturalFitSize;
            if (cam == null || refSize <= 0f) return;

            float alpha = LodAlpha(cam.orthographicSize / refSize);

            // basic collision: keep placed screen rects, nudge overlapping labels down.
            var placed = new List<Rect>();
            foreach (var d in manager.GetAll())
            {
                if (!views.TryGetValue(d.Id, out var tmp) || tmp == null) continue;
                Vector3 world = new Vector3(d.WorldPosition.X, labelYOffsetWorld, d.WorldPosition.Y);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool onScreen = sp.z > 0f && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
                var c = tmp.color; c.a = onScreen ? alpha : 0f; tmp.color = c;
                if (!onScreen || alpha <= 0.01f) { tmp.rectTransform.anchoredPosition = new Vector2(-9999, -9999); continue; }

                // screen -> canvas anchoredPosition (canvas is ScreenSpaceOverlay so 1:1 with screen px, pivot .5)
                Vector2 pos = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);
                var rect = new Rect(pos.x - 110, pos.y - 17, 220, 34);
                int guard = 0;
                while (guard++ < 8 && placed.Exists(r => r.Overlaps(rect))) { pos.y -= 30f; rect.y -= 30f; }
                placed.Add(rect);
                tmp.rectTransform.anchoredPosition = pos;
                // also fade the outline alpha with the text (optional): tmp.fontMaterial... keep simple for v1.
            }
        }

        float LodAlpha(float zoomRatio) // orthoSize/NaturalFitSize; large = zoomed out
        {
            if (zoomRatio >= farFrac) return 1f;
            if (zoomRatio <= nearFrac) return 0f;
            float t = (zoomRatio - nearFrac) / Mathf.Max(1e-4f, (farFrac - nearFrac));
            return Mathf.SmoothStep(0f, 1f, t);
        }
    }
}
```
Confirm `MapCameraController.targetCamera` / `NaturalFitSize` are public (they are — used by `PoiManager` zoom scaling). For `outlineWidth` to show, the TMP font asset's material must be an SDF material with outline (default TMP material supports `_OutlineWidth`); if the halo doesn't render, note it for the checkpoint (the user can pick an outline material preset). The canvas anchoredPosition math assumes the canvas' child RectTransforms are centered (anchorMin=anchorMax=0.5); set that in `BuildCanvas`/`CreateLabelView` — verify against `PoiEditPanel`'s overlay conventions and adjust the `-Screen.width*0.5f` offset if the canvas uses a corner anchor instead.

- [ ] **Step 3: USER checkpoint A (Editor)**
Add `RegionLabelManager` + `RegionLabelOverlay` GameObjects to the scene, wire refs (manager.mapRenderer; overlay.manager/cameraController/labelFont). Call `manager.SeedFromCells()` after a generation (temporary: via a `[ContextMenu]` on the manager, or Task 6 wiring). Verify: labels appear on the right biomes with Latin names; italic + letter-spacing + halo readable; fade out when zooming in, back when zooming out; no gross overlap on a sparse map. (Editing not yet — Task 5.)

- [ ] **Step 4: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs
git commit -m "feat(region-labels): screen-space TMP overlay with zoom LOD + basic collision"
```

---

### Task 5: RegionLabelOverlay — editing (select / rename / move / delete / add)

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs`

**Interfaces:**
- Consumes: `RegionLabelManager` CRUD (`SelectLabel/RenameLabel/MoveLabel/DeleteLabel/AddLabel`), `MapCameraController.targetCamera` (screen→world unproject).

- [ ] **Step 1: Click-to-select + inline rename + delete**
Add a transparent `Button` (raycastTarget Image, alpha 0) sibling/behind each label's TMP so clicks select it (`manager.SelectLabel(d.Id)`). Subscribe to `manager.OnSelectionChanged`: for the selected label swap its TMP for (or overlay) a `TMP_InputField` seeded with the text; on `onEndEdit` call `manager.RenameLabel(id, value)` and deselect; show a small "×" button that calls `manager.DeleteLabel(id)`. Mirror `PoiEditPanel`'s InputField construction (grep `PoiEditPanel.BuildInputField`).

- [ ] **Step 2: Drag-to-move**
On the selected label, implement pointer drag (IDragHandler or manual `Mouse.current` in LateUpdate when selected): each frame of drag, take the mouse screen point, unproject to the map plane `y=0` via a ray from `cameraController.targetCamera` (`Ray r = cam.ScreenPointToRay(mouse); t = -r.origin.y / r.direction.y; world = r.origin + r.direction*t;`), then `manager.MoveLabel(id, new System.Numerics.Vector2(world.x, world.z))`. Skip when pointer is over other UI (`EventSystem.current.IsPointerOverGameObject()` guard as elsewhere).

- [ ] **Step 3: Add mode**
Add `public bool addMode;` + a small toolbar button "+ Название" (or expose a `[ContextMenu]`/public `ToggleAddMode()` that a Task-6 layers/tool button drives). When `addMode` and the user left-clicks the map (not over UI), unproject to world (same ray math) → `manager.AddLabel(worldPos, null)` (defaults "NOVA REGIO", auto-selects for immediate rename) → exit addMode.

- [ ] **Step 4: USER checkpoint B (Editor)**
Verify: click a label → rename inline (persists in memory); drag a label → it moves and stays; "×" deletes; add-mode + map click → new label appears and is editable; none of these paint/pan the map (input guard). Save the project, reload → all edits persisted (needs Task 3 + Task 6 save wiring).

- [ ] **Step 5: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs
git commit -m "feat(region-labels): CRUD editing UI (select/rename/move/delete/add)"
```

---

### Task 6: Integration (seed on gen/load + regenerate + layers toggle + save wiring)

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` and/or `Assets/WorldGen/Rendering/MapScreenController.cs`
- Modify: `Assets/WorldGen/Rendering/MapLayersPanel.cs`
- Modify: the project-save call site (grep `ProjectSerializer.Save(`) + project-load handler.

**Interfaces:**
- Consumes: `RegionLabelManager` (`SeedFromCells`, `LoadLabels`, `GetAll`), `RegionLabelOverlay` (`SetVisible`, add-mode).

- [ ] **Step 1: Seed on generation, load on project-open**
Find where POI markers are seeded/loaded (grep `PoiManager` calls in `WorldMapRenderer`/`MapScreenController` — the gen path and the `LoadFromCells` path). At the SAME points: after a fresh world generation completes, call `regionLabelManager.SeedFromCells()`; on project load, call `regionLabelManager.LoadLabels(loadResult.RegionLabels)`. Do NOT call the placer on brush edits.

- [ ] **Step 2: Save wiring**
Update the `ProjectSerializer.Save(...)` call site to pass `new List<RegionLabelData>(regionLabelManager.GetAll())` as the new last argument (replaces the empty-list placeholder from Task 3 Step 1).

- [ ] **Step 3: Layers toggle + "regenerate" + add-mode button**
In `MapLayersPanel` add a row `AddLayerToggleRow(t, "Названия регионов", true, on => regionLabelOverlay?.SetVisible(on));` (mirror the existing rows; grep the coastline/decorations rows). Add a small button "Пересоздать названия" that calls `regionLabelManager.SeedFromCells()` (re-seed from current biomes, discarding edits) and a "+ Название" button that drives `regionLabelOverlay` add-mode — place them near the layer toggles or the POI tools, matching an existing panel's button style.

- [ ] **Step 4: USER checkpoint C (Editor + scene wiring)**
Wire the manager/overlay refs in the scene (MapLayersPanel→overlay/manager; renderer→manager). Then end-to-end: Generate → labels seed automatically; toggle hides/shows; edit some labels; Save → Load → edits persist; "Пересоздать названия" resets to auto; brush-edit biomes → labels stay put. Commit the scene + generated `.meta` after wiring.

- [ ] **Step 5: Commit**
```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapScreenController.cs Assets/WorldGen/Rendering/MapLayersPanel.cs
git commit -m "feat(region-labels): integrate seed on gen/load + save + layers toggle + regenerate/add"
```
(User separately commits the scene + `.meta` after Editor wiring, per the project's convention.)

---

## Self-Review

**Spec coverage:** placer/flood-fill+Latin names+sea (T1) ✓; manager CRUD+seed+events (T2) ✓; persistence additive+round-trip (T3) ✓; screen-space overlay+LOD+collision+font+halo (T4) ✓; full CRUD editing UI (T5) ✓; seed-on-gen/load + toggle + regenerate + save wiring (T6) ✓; not-recomputed-on-brush ✓; out-of-scope items untouched ✓.

**Placeholder scan:** Task 3 Step 1 deliberately passes an empty list at the save call site until Task 6 wires the manager — this is stated, not a hidden TODO, and Task 6 Step 2 closes it. All code steps show complete code except where they instruct grepping an existing pattern (canvas/InputField construction, exact accessor names) — those point at concrete existing files, per "follow established patterns."

**Type consistency:** `System.Numerics.Vector2` for all world coords (`RegionLabelData.WorldPosition`, placer, manager, MoveLabel/AddLabel); `UnityEngine.Vector2` only in overlay UI math. `RegionLabelPlacer.Place(cells, nearest, mapW, mapH, minPatchCells)` signature identical across T1 (def), T2 (call). `ProjectSerializer.Save(...)` gains the same trailing `regionLabels` param in T3 (def+test) and T6 (real call). Manager method names (`SeedFromCells/LoadLabels/GetAll/AddLabel/DeleteLabel/RenameLabel/MoveLabel/SelectLabel/DeselectAll`) consistent T2↔T4↔T5↔T6.

**Unity adaptation:** every "run the test" is a USER `[ContextMenu]` checkpoint; implementers commit DONE_WITH_CONCERNS. New MonoBehaviours require scene wiring (checkpoints A/C) — flagged.
