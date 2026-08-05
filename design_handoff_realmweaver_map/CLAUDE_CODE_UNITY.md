# Instructions for Claude Code — Implementing the RealmWeaver Map in Unity

You are implementing the map design specified in `README.md` inside the **RealmWeaver Unity project**. Read `README.md` first — it contains the exact algorithm and all color tokens. This file is the Unity-specific implementation plan.

## Ground rules
- The bundled `Terra Umbrarum.dc.html` is a **design reference**, not code to port line-by-line. Its JavaScript is *exact pseudocode* for the math — reproduce the algorithm, but render with Unity idioms and match the existing project's architecture, namespaces, and coding conventions.
- If a map/Voronoi/height module already exists in the project, **reuse its data** and add this as the styling/render layer. Do not duplicate generation. Inspect the codebase before writing new systems.
- Keep generation **deterministic from `seed`** — the JS `hash()`/`fbm()` are integer-exact; port them to C# with `unchecked` int32 math so results match across runs and platforms.

## Recommended architecture (C#)
Split into data → raster → sprites → overlays, all driven by one `MapConfig` (the tweak params in README).

```
Assets/RealmWeaver/Map/
  Noise.cs            // hash, valueNoise, fbm  (unchecked int32, matches JS exactly)
  MapConfig.cs        // seed, palette enum, coldLight, regionVariation, darkness,
                      // smoothBorders, mountains/forests enum, warmAccents
  Palette.cs          // 4 palettes as Color32 slot tables (values from README)
  MapFields.cs        // computes E[], lake[], plus moist()/warmth()/biomeKey()
  MapRasterizer.cs    // fills the base Texture2D (or Compute Shader) per README §5
  MapSprites.cs       // pine/decid/reed/mesa/hill/mountain placement + draw
  PoiMarker.cs        // medallion + 11 icon meshes/sprites, bounds-fit centering
  MapLabels.cs        // biome centroids, collision registry, TMP labels
  FogOfWar.cs         // cloud texture + reveal mask (RenderTexture, destination-out)
  MapRenderer.cs      // orchestrates draw order; exposes MasterView / PlayerView
```

## Rendering approach — two viable paths
Pick based on the project's needs; ask if unsure.

**A. Texture-first (matches the prototype 1:1, simplest to verify).**
- Rasterize the base map (elevation shading, biomes, coastline, rivers) into a `Texture2D` on the CPU, or a **Compute Shader** for speed at high res (the per-pixel math is embarrassingly parallel). Fields `E`, `lake`, `moist`, `warmth` can live in `RWStructuredBuffer`/`RenderTexture`.
- Draw **sprites, POI markers, labels, and chrome** as vector shapes onto the same texture (a small immediate-mode canvas helper: filled polygons, strokes, gradients), OR as a second pass with `GL`/`Graphics.DrawMesh`. Depth-sort sprites by screen-y before drawing.
- Output one texture for the **master view** and one for the **player view** (base + fog). Display on a RawImage (screen map) or as an unlit quad/plane material (in-world table map).

**B. Hybrid mesh (better for zoom/interactivity).**
- Base terrain → texture as in A. Mountains/trees/POIs → **billboarded sprites/quads** as real GameObjects so they can be picked, hovered, and the DM can click a POI. This is preferable if the map is interactive (placing/inspecting markers, moving the party). POI markers should be interactive GameObjects regardless (they carry gameplay data).

Given RealmWeaver is a session tool, **markers must be interactive** — implement POIs as prefabs with a `PoiData { PoiType type, string label, Vector2 normPos, RevealState state }`, rendered by `PoiMarker`. `default` (`?`) is the initial state before the DM assigns a type.

## Porting notes / gotchas
- **Noise parity**: JS uses `Math.imul` and `>>>` (unsigned). In C# use `int` with `unchecked` for the multiplies/xor and cast to `uint` for the final `/ 4294967296.0`. Verify a few sample values against the HTML (open it, `console.log` `fbm(...)`) before trusting the whole map.
- **Y axis**: Canvas y is top-down. Unity textures are bottom-up (`Texture2D` row 0 = bottom). Flip when writing pixels or set `Texture2D` accordingly, and flip the hillshade light dir's Y if the map looks lit from the wrong side.
- **Sprite depth**: sort all sprites (mountains, trees, hills, reeds, mesas) by `screenY` ascending and draw far→near so overlaps read as ranges. Mountains carry a `haze` term (README §Mountains) — apply as a pale overlay/tint for distant peaks.
- **POI centering**: reproduce the "measure bbox → fit diagonal to `2R*0.82` → center" step, or bake each icon as a pre-centered sprite of fixed canvas and just scale to the medallion. Do **not** give icons their own drop-shadow (it lands on the medallion).
- **Fog**: implement the reveal as a mask `RenderTexture` you paint into (white = revealed) and sample in the player-view material (`fogColor` where mask==0, clear where ==1, feather the edge). Update the mask when the party moves; persist per save.
- **Fonts**: import Cinzel, IM Fell English, Spectral as TMP font assets. Region labels are italic IM Fell; city/display are Cinzel. Keep the dark text-shadow (TMP underlay) for readability, and keep the collision/nudge logic so labels never overlap markers or each other.
- **Tweaks**: surface `MapConfig` in the DM's generation UI (sliders/toggles/enum dropdowns per README table). Changing `seed`, `palette`, `smoothBorders`, `mountains`, `forests`, `coldLight`, `regionVariation`, or `warmAccents` re-rasterizes the base; `darkness` only needs the vignette/dim pass re-applied (cheap).

## Definition of done
- Same seed → same map, matching the HTML reference's composition and palette.
- Master view and player (fog) view both render; fog reveals around visited cities + routes.
- All 11 POI markers render as centered medallions and are interactive (assign type, `default` `?` fallback).
- All four palettes selectable; `darkness`/`coldLight`/`regionVariation` sliders behave as described.
- Region labels sit on their biomes; nothing overlaps; text stays inside panels/frame.

## Verify against the reference
Open `Terra Umbrarum.dc.html` in a browser. Use the Tweaks panel to toggle `smoothBorders`, switch palettes, and move the sliders to see the intended results, and read any exact constant directly from the `<script data-dc-script>` block. When in doubt, the HTML is the source of truth for numbers.
