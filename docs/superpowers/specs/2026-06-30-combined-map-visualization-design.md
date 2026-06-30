# Combined Map Visualization — Design

**Date:** 2026-06-30
**Status:** Approved (design), pending implementation plan

## Goal

Render the generated world map so that elevation, climate/biome, and region/area
boundaries are all visible **at once**, instead of the current mutually-exclusive
`Height | Region | Biome` display modes. Each information layer is independently
toggleable.

## Context (current state)

- `WorldMapRenderer` builds one mesh for the whole map (single draw call), flat
  per-cell color via **vertex colors**, shader `VertexColorUnlit`.
- Map lies flat in the XZ plane, `Y = 0`. `ToWorldPos(p) = (p.X, 0, p.Y)`.
- `MapDisplayMode { Height, Region, Biome }` — only one layer shown at a time.
  `GetColorForCell` switches on it; `RecolorOnly()` recomputes vertex colors
  without rebuilding geometry (used on display-mode change, override, and brush).
- Region borders are **not drawn** today — Region mode only fills cells with
  per-region colors.
- Per-cell data available: `EffectiveElevation`, `EffectiveIsOcean`,
  `EffectiveIsLake`, `Biome`, `RegionId`, `Site`, `Polygon`, `NeighborIds`.
- `RegionColorPalette` provides `GetHeightColor`, `GetBiomeColor`, `GetRegionColor`.
- A `Corner` graph exists (built for rivers) but is not required here.

## Decisions (from brainstorming)

1. **Elevation representation:** 2D relief shading (hillshade). Map stays flat
   top-down; cell picking and the existing pipeline are unchanged. (Rejected:
   real 3D extrusion — would force camera/lighting/raycast rework; contour lines
   — extra noisy line layer.)
2. **Structure:** Toggleable layers (not a single fixed combined mode, not
   replacing the old modes).
3. **Border layer scope:** Region borders **and** coastline (land/water). Biome
   borders excluded (biomes already differ by fill color — too noisy).
4. **"Climate" = biome.** Biome is already `Whittaker(temperature × moisture)`,
   so the biome layer is the climate layer. Raw temperature/moisture overlays are
   out of scope (possible future layer).

## Composition model

A new `MapDisplayMode.Combined` value becomes the default. The old
`Height/Region/Biome` modes remain working and untouched (focused single-layer
inspection). In `Combined` mode the render is driven by four independent toggles
on `WorldMapRenderer` (serialized fields + runtime setters):

| Toggle               | Effect                                                                         |
|----------------------|--------------------------------------------------------------------------------|
| `showBiome`          | base fill = biome color; **off** → neutral tone (land = tan, water = blue) so relief stays readable |
| `showRelief`         | multiply base by hillshade brightness (**land only**; water stays flat)        |
| `showRegionBorders`  | draw lines where neighboring cells have different `RegionId`                    |
| `showCoastline`      | draw lines on the land/water boundary                                          |

Additional parameters: `reliefStrength`, `reliefLightAzimuth` (degrees),
`regionBorderColor`/`regionBorderWidth`, `coastlineColor`/`coastlineWidth`.
Default: all four toggles **on** → "see everything at once."

Composition order (bottom → top): base fill (biome or neutral) → relief
brightness multiply (land) → border line meshes on top.

## Components

### 1. Hillshade (relief shading)

Baked into vertex colors on the CPU (matches existing architecture; no new shader).

For each **land** cell, estimate the height gradient from its neighbors using a
`cellById` lookup:

```
g = Σ over neighbors n of  normalize(siteN - siteC) * (heightN - heightC)
```

`g` points uphill. Build a pseudo-normal `n = normalize(vec3(-g.x * strength, 1, -g.y * strength))`
(Y is up). Brightness `= lerp(ambient, 1, saturate(dot(n, lightDir)))`, where
`lightDir` comes from `reliefLightAzimuth` (classic NW light) plus a fixed
elevation angle. Add a mild absolute-elevation term so flat plateaus still read as
higher. **Water cells:** brightness = 1 (flat). Final cell color = base color ×
brightness.

Computed in `GetColorForCell` for `Combined` mode and recomputed in `RecolorOnly`,
so the elevation brush updates relief live. Voronoi cells are flat-shaded, so the
result is slightly faceted — stylistically acceptable for this map.

The brightness function is factored so it can be unit-tested in pure C#
(monotonic; a slope facing the light is brighter than one facing away).

### 2. Border meshes (`MapBorderBuilder` — new static class, Rendering)

- Build a **shared-edge map**: for each cell, for each consecutive polygon vertex
  pair `(a, b)`, key = unordered, coordinate-rounded `(a, b)` → accumulate the
  cells touching that edge (≤ 2). Rounding epsilon ~1e-3 of map units.
- Classify each shared edge with two cells A, B:
  - **Region border** — both land and `RegionId(A) != RegionId(B)`.
  - **Coastline** — exactly one of A, B is water (`EffectiveIsOcean || EffectiveIsLake`).
  - Map-edge edges (only one cell) are not borders.
- Emit two ribbon meshes (regions, coastline): each border edge `(p0, p1)` →
  a thin quad of the configured width in XZ, at a small `Y` offset above the map
  (like rivers, to avoid z-fighting; coastline slightly below region lines).
- Each mesh → a child GameObject with an unlit colored material; one draw call per
  border type. Toggle via `SetActive`.

The edge-classification step (cells → set of region/coastline edges) is factored
to be unit-testable in pure C#.

### 3. `WorldMapRenderer` integration

- Add the four layer toggle fields + relief/border parameters.
- Maintain a `cellById` dictionary (built after generation) for hillshade and
  border building.
- Runtime setters `SetLayer*(bool)` → recolor and/or rebuild overlays, then
  `OnDisplayChanged?.Invoke()`.
- Build border meshes in `GenerateAndRender`. **Rebuild** after
  `ApplyWaterOverride` (coastline can change). Do **not** rebuild on
  elevation/temperature/moisture brush strokes (regions and coastline unchanged) —
  only relief vertex colors refresh there.

### 4. UI

A small toggle group of 4 checkboxes (built programmatically, in the style of the
existing panels — a section in `MapEditorPanel`), each wired to the corresponding
`SetLayer*`. Minimal, no over-engineering.

## Files

- **New:** `Assets/WorldGen/Rendering/MapBorderBuilder.cs`
- **Modified:**
  - `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (toggles, `Combined` path,
    `cellById`, overlay build/rebuild, setters)
  - `Assets/WorldGen/Rendering/RegionColorPalette.cs` (hillshade brightness +
    neutral land tone helpers)
  - `Assets/WorldGen/Rendering/MapEditorPanel.cs` (layer checkboxes)
  - `MapDisplayMode` enum (+ `Combined`)

## Error handling

- Null/ungenerated `cells` guarded (existing pattern).
- Degenerate cells (`Polygon.Count < 3`) skipped (existing pattern).
- Empty border set → empty mesh (valid, renders nothing).

## Testing

- Pure-C# unit tests: border-edge classification (given cells → expected
  region/coastline edge set) and hillshade brightness (slope toward light brighter
  than away; monotonic in lit-slope angle).
- Visual verification of the composed render (relief + biome + borders, and each
  toggle independently) in the Unity editor.

## Out of scope (future)

- Real 3D relief / contour lines.
- Raw temperature / moisture overlay layers.
- Biome boundary lines.
