# Combined Map Visualization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Combined` display mode that shows elevation (relief shading), climate/biome (base color), and region + coastline borders simultaneously, as independently toggleable layers.

**Architecture:** Stay in the existing single-mesh, vertex-color, flat-XZ rendering model. Relief is baked into vertex colors on the CPU (so it updates with the elevation brush via `RecolorOnly`). Borders are two separate ribbon meshes (region, coastline) rendered as child objects, toggled with `SetActive`. The three existing focused modes (`Height/Region/Biome`) stay working untouched.

**Tech Stack:** Unity 2022.3 LTS, Built-in Render Pipeline, C#. No new packages.

## Global Constraints

- Unity 2022.3 LTS, **Built-in Render Pipeline** (not URP). One line each, verbatim.
- **New Input System** only (`UnityEngine.InputSystem`) — not relevant to this plan's files but never reintroduce legacy `UnityEngine.Input`.
- The **Generation layer** (`Assets/WorldGen/Generation/`) must stay free of `UnityEngine` dependencies. All work in this plan lives in the **Rendering layer**, which may use `UnityEngine`.
- Cell geometry uses `System.Numerics.Vector2` (`cell.Site`, `cell.Polygon`). Map → world is `(p.X, 0, p.Y)` (XZ plane, Y up).
- No automated test framework exists in this project. Verification is **`[ContextMenu]` self-check methods** that log `PASS`/`FAIL`, run by clicking the context-menu item on the component in the Unity Inspector, plus visual checks in the editor.
- The project is **not under version control**. "Commit" steps below are optional; if you run `git init` first they become real commits, otherwise treat them as checkpoints.

## File Structure

- **Create** `Assets/WorldGen/Rendering/MapBorderBuilder.cs` — pure edge classification (`ClassifyBorderEdges`, no UnityEngine types in its logic) + ribbon `Mesh` construction (`BuildRibbonMesh`). One responsibility: turn cells into border geometry.
- **Modify** `Assets/WorldGen/Rendering/RegionColorPalette.cs` — add `GetNeutralBaseColor` (neutral land/water tone when biome layer is off) and `HillshadeBrightness` (pure float→float relief math).
- **Modify** `Assets/WorldGen/Rendering/WorldMapRenderer.cs` — extend `MapDisplayMode` with `Combined`; add layer-toggle fields + relief/border params; `cellById` map; `Combined` branch in `GetColorForCell`; gradient helper; border build/rebuild + child objects; runtime `SetShow*Layer` setters; two `[ContextMenu]` self-checks.
- **Modify** `Assets/WorldGen/Rendering/MapEditorPanel.cs` — add an always-visible "Layers" section with 4 toggles wired to the renderer setters.

---

### Task 1: Border edge classification + self-check

**Files:**
- Create: `Assets/WorldGen/Rendering/MapBorderBuilder.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add `[ContextMenu]` self-check)

**Interfaces:**
- Produces:
  - `struct MapBorderBuilder.Edge { System.Numerics.Vector2 A; System.Numerics.Vector2 B; }`
  - `static void MapBorderBuilder.ClassifyBorderEdges(IReadOnlyList<VoronoiCell> cells, out List<Edge> regionEdges, out List<Edge> coastEdges)`

- [ ] **Step 1: Write the self-check first (it will not compile — that is the failing state)**

Add this method inside the `WorldMapRenderer` class (next to the other `[ContextMenu]` helpers):

```csharp
[ContextMenu("Self-Test: Border Classification")]
public void SelfTestBorderClassification()
{
    // Фикстура: две квадратные клетки, общее ребро (1,0)-(1,1).
    var a = new VoronoiCell(0, new System.Numerics.Vector2(0.5f, 0.5f))
    {
        Polygon = new List<System.Numerics.Vector2>
        { new(0, 0), new(1, 0), new(1, 1), new(0, 1) },
        NeighborIds = new List<int> { 1 }
    };
    var b = new VoronoiCell(1, new System.Numerics.Vector2(1.5f, 0.5f))
    {
        Polygon = new List<System.Numerics.Vector2>
        { new(1, 0), new(2, 0), new(2, 1), new(1, 1) },
        NeighborIds = new List<int> { 0 }
    };
    var fixture = new List<VoronoiCell> { a, b };
    bool ok = true;

    // 1) Разные регионы, обе суша -> одна граница региона, берегов нет.
    a.RegionId = 0; b.RegionId = 1; a.IsOcean = false; b.IsOcean = false;
    a.Biome = Biome.Grassland; b.Biome = Biome.Grassland;
    MapBorderBuilder.ClassifyBorderEdges(fixture, out var rEdges, out var cEdges);
    ok &= rEdges.Count == 1 && cEdges.Count == 0;

    // 2) Одна клетка вода -> один берег, границ регионов нет.
    a.RegionId = 0; b.RegionId = 0; a.IsOcean = false; b.IsOcean = true;
    a.Biome = Biome.Grassland; b.Biome = Biome.Ocean;
    MapBorderBuilder.ClassifyBorderEdges(fixture, out rEdges, out cEdges);
    ok &= cEdges.Count == 1 && rEdges.Count == 0;

    // 3) Один регион, обе суша -> ничего.
    a.RegionId = 0; b.RegionId = 0; a.IsOcean = false; b.IsOcean = false;
    a.Biome = Biome.Grassland; b.Biome = Biome.Grassland;
    MapBorderBuilder.ClassifyBorderEdges(fixture, out rEdges, out cEdges);
    ok &= rEdges.Count == 0 && cEdges.Count == 0;

    Debug.Log(ok
        ? "Self-Test Border Classification: PASS"
        : "Self-Test Border Classification: FAIL");
}
```

- [ ] **Step 2: Verify it fails (compile error)**

In Unity, let the editor recompile. Expected: compile error `The name 'MapBorderBuilder' does not exist in the current context` — confirming the test targets code that does not exist yet.

- [ ] **Step 3: Create `MapBorderBuilder` with the classification implementation**

Create `Assets/WorldGen/Rendering/MapBorderBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Превращает список клеток в геометрию границ: классифицирует общие рёбра соседних
    /// клеток на границы регионов (суша/суша с разным RegionId) и береговую линию (суша/вода),
    /// и строит из набора рёбер тонкий меш-ленту для рендера. Классификация (ClassifyBorderEdges)
    /// не зависит от UnityEngine - её можно проверять self-check'ом; построение меша (BuildRibbonMesh)
    /// использует UnityEngine.Mesh (типы UnityEngine указаны полным именем, т.к. System.Numerics.Vector2
    /// и UnityEngine.Vector2/Vector3 конфликтуют по короткому имени).
    /// </summary>
    public static class MapBorderBuilder
    {
        public struct Edge
        {
            public Vector2 A;
            public Vector2 B;
            public Edge(Vector2 a, Vector2 b) { A = a; B = b; }
        }

        /// <summary>Округляет точку до целых тысячных карты - чтобы общие вершины соседних
        /// полигонов с микроскопическим float-расхождением попадали в один ключ ребра.</summary>
        static (long, long) Quantize(Vector2 p)
            => ((long)System.Math.Round(p.X * 1000.0), (long)System.Math.Round(p.Y * 1000.0));

        static (long, long, long, long) EdgeKey(Vector2 a, Vector2 b)
        {
            var qa = Quantize(a);
            var qb = Quantize(b);
            // Канонический порядок концов - чтобы (a,b) и (b,a) давали один ключ.
            bool aFirst = qa.Item1 < qb.Item1 || (qa.Item1 == qb.Item1 && qa.Item2 <= qb.Item2);
            return aFirst
                ? (qa.Item1, qa.Item2, qb.Item1, qb.Item2)
                : (qb.Item1, qb.Item2, qa.Item1, qa.Item2);
        }

        public static void ClassifyBorderEdges(
            IReadOnlyList<VoronoiCell> cells,
            out List<Edge> regionEdges,
            out List<Edge> coastEdges)
        {
            regionEdges = new List<Edge>();
            coastEdges = new List<Edge>();
            if (cells == null) return;

            var idToCell = new Dictionary<int, VoronoiCell>();
            foreach (var c in cells) idToCell[c.Id] = c;

            // Ключ ребра -> (геометрия ребра, список Id клеток, которым оно принадлежит).
            var edgeToCells = new Dictionary<(long, long, long, long), (Edge edge, List<int> cellIds)>();

            foreach (var cell in cells)
            {
                var poly = cell.Polygon;
                if (poly == null || poly.Count < 3) continue;
                for (int i = 0; i < poly.Count; i++)
                {
                    var p0 = poly[i];
                    var p1 = poly[(i + 1) % poly.Count];
                    var key = EdgeKey(p0, p1);
                    if (!edgeToCells.TryGetValue(key, out var entry))
                    {
                        entry = (new Edge(p0, p1), new List<int>());
                        edgeToCells[key] = entry;
                    }
                    entry.cellIds.Add(cell.Id); // entry.cellIds - ссылка, общий список, мутируется на месте
                }
            }

            foreach (var kv in edgeToCells)
            {
                var edge = kv.Value.edge;
                var ids = kv.Value.cellIds;
                if (ids.Count != 2) continue; // ребро по краю карты или вырожденное - не граница

                var ca = idToCell[ids[0]];
                var cb = idToCell[ids[1]];
                bool aWater = ca.EffectiveIsOcean || ca.EffectiveIsLake;
                bool bWater = cb.EffectiveIsOcean || cb.EffectiveIsLake;

                if (aWater != bWater)
                {
                    coastEdges.Add(edge);
                }
                else if (!aWater && !bWater && ca.RegionId != cb.RegionId)
                {
                    regionEdges.Add(edge);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the self-check and verify PASS**

In Unity: select the GameObject with `WorldMapRenderer`, click the gear/⋮ → `Self-Test: Border Classification`. Expected Console output: `Self-Test Border Classification: PASS`.

- [ ] **Step 5: Commit (optional — see Global Constraints)**

```bash
git add Assets/WorldGen/Rendering/MapBorderBuilder.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: border edge classification (region + coastline) with self-check"
```

---

### Task 2: Border ribbon mesh + render integration

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapBorderBuilder.cs` (add `BuildRibbonMesh`)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (extend enum, add fields, build/rebuild borders, hook into generation and water override)

**Interfaces:**
- Consumes: `MapBorderBuilder.ClassifyBorderEdges`, `MapBorderBuilder.Edge` (Task 1).
- Produces:
  - `static UnityEngine.Mesh MapBorderBuilder.BuildRibbonMesh(IReadOnlyList<Edge> edges, float width, float yHeight)`
  - `MapDisplayMode.Combined` enum value
  - `WorldMapRenderer.BuildBorders()` (private), called from `GenerateAndRender` and `ApplyWaterOverride`
  - Layer fields `showRegionBordersLayer`, `showCoastlineLayer` (used in later tasks)

- [ ] **Step 1: Add `BuildRibbonMesh` to `MapBorderBuilder`**

Append this method inside the `MapBorderBuilder` class (after `ClassifyBorderEdges`):

```csharp
/// <summary>Строит один меш из тонких quad-лент вдоль каждого ребра (ширина width,
/// в плоскости XZ на высоте yHeight). Один меш = один draw call на тип границы.</summary>
public static UnityEngine.Mesh BuildRibbonMesh(IReadOnlyList<Edge> edges, float width, float yHeight)
{
    var verts = new List<UnityEngine.Vector3>();
    var tris = new List<int>();
    float half = width * 0.5f;

    if (edges != null)
    {
        foreach (var e in edges)
        {
            var p0 = new UnityEngine.Vector3(e.A.X, yHeight, e.A.Y);
            var p1 = new UnityEngine.Vector3(e.B.X, yHeight, e.B.Y);
            var dir = p1 - p0;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();
            var side = new UnityEngine.Vector3(-dir.z, 0f, dir.x) * half;

            int bi = verts.Count;
            verts.Add(p0 - side);
            verts.Add(p0 + side);
            verts.Add(p1 + side);
            verts.Add(p1 - side);

            tris.Add(bi + 0); tris.Add(bi + 2); tris.Add(bi + 1);
            tris.Add(bi + 0); tris.Add(bi + 3); tris.Add(bi + 2);
        }
    }

    var mesh = new UnityEngine.Mesh();
    mesh.indexFormat = verts.Count > 65000
        ? UnityEngine.Rendering.IndexFormat.UInt32
        : UnityEngine.Rendering.IndexFormat.UInt16;
    mesh.SetVertices(verts);
    mesh.SetTriangles(tris, 0);
    mesh.RecalculateBounds();
    return mesh;
}
```

- [ ] **Step 2: Extend the display-mode enum**

In `WorldMapRenderer.cs`, change the enum at the top of the namespace:

```csharp
public enum MapDisplayMode { Height, Region, Biome, Combined }
```

- [ ] **Step 3: Add layer fields and make `Combined` the default**

In the `[Header("Отображение")]` block, replace the `displayMode` line and add the layer fields:

```csharp
[Header("Отображение")]
public MapDisplayMode displayMode = MapDisplayMode.Combined;

[Header("Combined: слои")]
public bool showBiomeLayer = true;
public bool showReliefLayer = true;
public bool showRegionBordersLayer = true;
public bool showCoastlineLayer = true;

[Header("Combined: рельеф (hillshade)")]
public float reliefStrength = 3f;
public float reliefLightAzimuth = 315f;
[Range(0f, 1f)] public float reliefAmbient = 0.5f;

[Header("Combined: границы")]
public Color regionBorderColor = new Color(0.10f, 0.10f, 0.10f, 0.9f);
public float regionBorderWidth = 1.5f;
public Color coastlineColor = new Color(0.05f, 0.10f, 0.20f, 0.95f);
public float coastlineWidth = 2.5f;
```

- [ ] **Step 4: Add the border child-object fields**

Next to the other private fields (e.g. after `Transform riverContainer;`):

```csharp
Transform borderContainer;        // родитель для меш-объектов границ
GameObject regionBorderObject;    // меш-лента границ регионов
GameObject coastlineObject;       // меш-лента береговой линии
```

- [ ] **Step 5: Add `BuildBorders` and a helper**

Add these methods to `WorldMapRenderer` (near `BuildRivers`):

```csharp
/// <summary>Классифицирует граничные рёбра и строит два меш-объекта (границы регионов
/// и берег). Видимость каждого зависит от Combined-режима и соответствующего тоггла.</summary>
void BuildBorders()
{
    if (borderContainer != null)
        Destroy(borderContainer.gameObject);
    if (cells == null) return;

    var containerGO = new GameObject("MapBorders");
    containerGO.transform.SetParent(transform, false);
    borderContainer = containerGO.transform;

    MapBorderBuilder.ClassifyBorderEdges(cells, out var regionEdges, out var coastEdges);

    // Y чуть выше карты (Y=0) и ниже рек (Y=0.5), чтобы избежать z-fighting.
    regionBorderObject = CreateBorderObject(
        "RegionBorders", MapBorderBuilder.BuildRibbonMesh(regionEdges, regionBorderWidth, 0.4f), regionBorderColor);
    coastlineObject = CreateBorderObject(
        "Coastline", MapBorderBuilder.BuildRibbonMesh(coastEdges, coastlineWidth, 0.3f), coastlineColor);

    bool combined = displayMode == MapDisplayMode.Combined;
    regionBorderObject.SetActive(combined && showRegionBordersLayer);
    coastlineObject.SetActive(combined && showCoastlineLayer);
}

GameObject CreateBorderObject(string name, Mesh mesh, Color color)
{
    var go = new GameObject(name);
    go.transform.SetParent(borderContainer, false);
    go.AddComponent<MeshFilter>().mesh = mesh;
    var mr = go.AddComponent<MeshRenderer>();
    // Sprites/Default: unlit, без culling (двусторонний), поддерживает material.color - как у рек.
    var mat = new Material(Shader.Find("Sprites/Default"));
    mat.color = color;
    mr.sharedMaterial = mat;
    return go;
}
```

- [ ] **Step 6: Call `BuildBorders` from generation and water override**

In `GenerateAndRender`, add the call right after `BuildRivers();`:

```csharp
BuildMesh(cells);
BuildRivers();
BuildBorders();
```

In `ApplyWaterOverride`, add a rebuild after `RecolorOnly()` (coastline can change when a cell's water status flips):

```csharp
CellOverrideService.ApplyWaterOverride(targetCells, waterType, beachElevationThreshold);
RecolorOnly();
BuildBorders();
OnDisplayChanged?.Invoke();
```

- [ ] **Step 7: Verify visually**

In Unity, run `Generate World` (context menu on `WorldMapRenderer`). Expected: in the Hierarchy a `MapBorders` child appears with `RegionBorders` and `Coastline` children; dark lines trace the coastline and the boundaries between differently-colored regions on the map. (Colors/relief come in later tasks; this task only proves the border geometry renders.)

- [ ] **Step 8: Commit (optional)**

```bash
git add Assets/WorldGen/Rendering/MapBorderBuilder.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: render region + coastline border ribbon meshes"
```

---

### Task 3: Hillshade brightness + neutral base color + self-check

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionColorPalette.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add `[ContextMenu]` self-check)

**Interfaces:**
- Produces:
  - `static Color RegionColorPalette.GetNeutralBaseColor(VoronoiCell cell)`
  - `static float RegionColorPalette.HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient)`

- [ ] **Step 1: Write the self-check first (will not compile)**

Add to the `WorldMapRenderer` class:

```csharp
[ContextMenu("Self-Test: Hillshade Brightness")]
public void SelfTestHillshade()
{
    const float az = 315f, strength = 3f, ambient = 0.5f;

    float flat = RegionColorPalette.HillshadeBrightness(0f, 0f, strength, az, ambient);
    // Свет с СЗ (азимут 315): склон, "обращённый к свету" - градиент (+x, -y) - ярче обратного.
    float toward = RegionColorPalette.HillshadeBrightness(0.707f, -0.707f, strength, az, ambient);
    float away = RegionColorPalette.HillshadeBrightness(-0.707f, 0.707f, strength, az, ambient);

    bool ok = flat >= ambient - 1e-4f && flat <= 1f + 1e-4f
              && toward > away && Mathf.Abs(toward - away) > 1e-3f;

    Debug.Log(ok
        ? $"Self-Test Hillshade: PASS (flat={flat:F2}, toward={toward:F2}, away={away:F2})"
        : $"Self-Test Hillshade: FAIL (flat={flat:F2}, toward={toward:F2}, away={away:F2})");
}
```

- [ ] **Step 2: Verify it fails (compile error)**

Let Unity recompile. Expected: `'RegionColorPalette' does not contain a definition for 'HillshadeBrightness'`.

- [ ] **Step 3: Implement the palette additions**

Add both methods to `RegionColorPalette`:

```csharp
/// <summary>Нейтральный базовый тон, когда слой биома выключен: вода - синяя/озёрная,
/// суша - песочный, чтобы рельеф оставался читаемым без биомной раскраски.</summary>
public static Color GetNeutralBaseColor(VoronoiCell cell)
{
    if (cell.EffectiveIsOcean) return new Color(0.10f, 0.25f, 0.50f);
    if (cell.EffectiveIsLake) return new Color(0.30f, 0.55f, 0.65f);
    return new Color(0.82f, 0.78f, 0.65f); // нейтральная суша (tan)
}

/// <summary>Яркость рельефного затенения [ambient..1] из градиента высоты клетки.
/// Псевдонормаль строится из градиента (Y - вверх), освещается направленным светом
/// под азимутом lightAzimuthDeg и фиксированным углом возвышения 45°.</summary>
public static float HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient)
{
    var normal = new Vector3(-gradX * strength, 1f, -gradY * strength).normalized;
    float az = lightAzimuthDeg * Mathf.Deg2Rad;
    var lightDir = new Vector3(Mathf.Sin(az), 1f, Mathf.Cos(az)).normalized;
    float ndotl = Mathf.Clamp01(Vector3.Dot(normal, lightDir));
    return Mathf.Lerp(ambient, 1f, ndotl);
}
```

- [ ] **Step 4: Run the self-check and verify PASS**

In Unity: `WorldMapRenderer` context menu → `Self-Test: Hillshade Brightness`. Expected Console: `Self-Test Hillshade: PASS (...)` with `toward` greater than `away` and `flat` between 0.5 and 1.

- [ ] **Step 5: Commit (optional)**

```bash
git add Assets/WorldGen/Rendering/RegionColorPalette.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: hillshade brightness + neutral base color helpers with self-check"
```

---

### Task 4: Combined-mode cell coloring (relief × biome)

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `RegionColorPalette.GetNeutralBaseColor`, `RegionColorPalette.HillshadeBrightness` (Task 3); `showBiomeLayer`, `showReliefLayer`, `reliefStrength`, `reliefLightAzimuth`, `reliefAmbient` fields (Task 2).
- Produces: `cellById` map; `ComputeCellGradient`; `Combined` case in `GetColorForCell`; updated `SetDisplayMode` and new `SetShow*Layer` setters (consumed by Task 5).

- [ ] **Step 1: Build a cell-id lookup in `BuildMesh`**

Add the field near the other private fields:

```csharp
Dictionary<int, VoronoiCell> cellById;
```

In `BuildMesh`, right after `cells = sourceCells;`, add:

```csharp
cellById = new Dictionary<int, VoronoiCell>(cells.Count);
foreach (var c in cells) cellById[c.Id] = c;
```

- [ ] **Step 2: Add the gradient helper**

Add to `WorldMapRenderer`:

```csharp
/// <summary>Оценивает градиент высоты клетки по соседям (направление "вверх по склону").
/// Используется для рельефного затенения в Combined-режиме.</summary>
System.Numerics.Vector2 ComputeCellGradient(VoronoiCell cell)
{
    var g = System.Numerics.Vector2.Zero;
    if (cellById == null) return g;
    foreach (int nId in cell.NeighborIds)
    {
        if (!cellById.TryGetValue(nId, out var n)) continue;
        var dir = n.Site - cell.Site;
        float len = dir.Length();
        if (len < 1e-4f) continue;
        dir /= len;
        g += dir * (n.EffectiveElevation - cell.EffectiveElevation);
    }
    return g;
}
```

- [ ] **Step 3: Add the `Combined` branch to `GetColorForCell`**

Insert a new case before `case MapDisplayMode.Region:` in the `switch (displayMode)`:

```csharp
case MapDisplayMode.Combined:
{
    Biome effBiome = cell.EffectiveIsOcean ? Biome.Ocean
                   : cell.EffectiveIsLake ? Biome.Lake
                   : cell.Biome;
    bool isWater = cell.EffectiveIsOcean || cell.EffectiveIsLake;

    Color baseColor = showBiomeLayer
        ? RegionColorPalette.GetBiomeColor(effBiome)
        : RegionColorPalette.GetNeutralBaseColor(cell);

    if (showReliefLayer && !isWater)
    {
        var grad = ComputeCellGradient(cell);
        float b = RegionColorPalette.HillshadeBrightness(
            grad.X, grad.Y, reliefStrength, reliefLightAzimuth, reliefAmbient);
        baseColor = new Color(baseColor.r * b, baseColor.g * b, baseColor.b * b, baseColor.a);
    }
    return baseColor;
}
```

- [ ] **Step 4: Update `SetDisplayMode` to toggle border visibility**

Replace the body of `SetDisplayMode` with:

```csharp
public void SetDisplayMode(MapDisplayMode mode)
{
    displayMode = mode;
    if (cells != null) RecolorOnly();
    bool combined = mode == MapDisplayMode.Combined;
    if (regionBorderObject != null) regionBorderObject.SetActive(combined && showRegionBordersLayer);
    if (coastlineObject != null) coastlineObject.SetActive(combined && showCoastlineLayer);
    OnDisplayChanged?.Invoke();
}
```

- [ ] **Step 5: Add the runtime layer setters**

Add to `WorldMapRenderer` (near `SetDisplayMode`):

```csharp
public void SetShowBiomeLayer(bool on)
{
    showBiomeLayer = on;
    if (cells != null) RecolorOnly();
    OnDisplayChanged?.Invoke();
}

public void SetShowReliefLayer(bool on)
{
    showReliefLayer = on;
    if (cells != null) RecolorOnly();
    OnDisplayChanged?.Invoke();
}

public void SetShowRegionBordersLayer(bool on)
{
    showRegionBordersLayer = on;
    if (regionBorderObject != null)
        regionBorderObject.SetActive(displayMode == MapDisplayMode.Combined && on);
    OnDisplayChanged?.Invoke();
}

public void SetShowCoastlineLayer(bool on)
{
    showCoastlineLayer = on;
    if (coastlineObject != null)
        coastlineObject.SetActive(displayMode == MapDisplayMode.Combined && on);
    OnDisplayChanged?.Invoke();
}
```

- [ ] **Step 6: Verify visually**

In Unity, `Generate World`. Expected with all layers on: land is biome-colored with visible relief shading (NW-lit slopes brighter), water flat, dark coastline and region-boundary lines overlaid. Toggle the inspector fields `showReliefLayer` / `showBiomeLayer` off and re-run `Generate World` (or change `displayMode`): relief off → flat biome fill; biome off → sandy/blue neutral terrain with relief still visible. Use the elevation brush (Brush mode) on land and confirm relief shading updates live as elevation changes.

- [ ] **Step 7: Commit (optional)**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: Combined display mode compositing relief over biome with borders"
```

---

### Task 5: Layer-toggle UI in MapEditorPanel

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapEditorPanel.cs`

**Interfaces:**
- Consumes: `WorldMapRenderer.SetShowReliefLayer/SetShowBiomeLayer/SetShowRegionBordersLayer/SetShowCoastlineLayer` (Task 4); existing `AddToggle`, `AddLabel` helpers.

- [ ] **Step 1: Add a "Layers" section builder + row helper**

Add these two methods to `MapEditorPanel`:

```csharp
void BuildLayersSection(Transform t)
{
    AddLabel(t, "─── Слои (Combined) ───", bold: false, color: sectionHeaderColor);
    AddLayerToggleRow(t, "Рельеф", true, on => mapRenderer?.SetShowReliefLayer(on));
    AddLayerToggleRow(t, "Биом / климат", true, on => mapRenderer?.SetShowBiomeLayer(on));
    AddLayerToggleRow(t, "Границы регионов", true, on => mapRenderer?.SetShowRegionBordersLayer(on));
    AddLayerToggleRow(t, "Береговая линия", true, on => mapRenderer?.SetShowCoastlineLayer(on));
}

void AddLayerToggleRow(Transform parent, string label, bool defaultOn, System.Action<bool> onChanged)
{
    var rowGO = new GameObject($"{label}LayerRow");
    rowGO.transform.SetParent(parent, false);
    var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
    hLayout.spacing = 6f;
    hLayout.childControlWidth = false;
    hLayout.childControlHeight = false;
    var rowLE = rowGO.AddComponent<LayoutElement>();
    rowLE.preferredHeight = 20f;

    var toggle = AddToggle(rowGO.transform, defaultOn);
    toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
    AddLabel(rowGO.transform, label);
}
```

- [ ] **Step 2: Call the section builder from `BuildUI`**

In `BuildUI`, add the call right after the mode-row block (after the two `AddModeButton` lines that create `selectionModeButton` / `brushModeButton`), so the layer toggles are always visible regardless of editor mode:

```csharp
selectionModeButton = AddModeButton(modeRowGO.transform, "Selection & Override", () => SetMode(EditorMode.SelectionOverride));
brushModeButton = AddModeButton(modeRowGO.transform, "Brush", () => SetMode(EditorMode.Brush));

BuildLayersSection(t);
```

- [ ] **Step 3: Verify visually**

In Unity, enter Play mode (the panel builds in `Awake`). Expected: under the mode buttons a "Слои (Combined)" header with 4 checkboxes, all checked. Unchecking "Рельеф" flattens the shading; "Биом / климат" switches land to neutral tan; "Границы регионов" and "Береговая линия" hide their lines. Re-checking restores each. Confirm no errors in the Console.

- [ ] **Step 4: Commit (optional)**

```bash
git add Assets/WorldGen/Rendering/MapEditorPanel.cs
git commit -m "feat: layer-toggle UI for Combined map view"
```

---

## Notes / Out of Scope

- `MapLegendUI` is not modified. It switches on `displayMode`; for `Combined` it simply produces no legend entries (a `switch` with no matching case does nothing — not an error). A `Combined` legend can be added later if wanted.
- No real 3D relief, contour lines, raw temperature/moisture overlays, or biome-boundary lines (deferred per spec).
- `Destroy` (not `DestroyImmediate`) is used for the border container, matching the existing `BuildRivers` pattern.
