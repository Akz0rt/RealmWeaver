# Handoff: RealmWeaver — Procedural World Map (Terra Umbrarum)

## Overview
This is the **visual + generation design for RealmWeaver's procedural D&D world map**. The app generates a world with a Fortune/Voronoi-style algorithm; this design replaces flat colored polygons with a hand-drawn, dark-and-cold cartographic look that has a "fake isometry" feel (side-view mountains, trees, and settlements over a smoothly-shaded landmass). It also specifies the **fog-of-war player view** and a **set of point-of-interest (POI) markers**.

The deliverable in this bundle is a single self-contained HTML/Canvas prototype (`Terra Umbrarum.dc.html`) that generates and renders the whole map live, plus this documentation.

## About the Design Files
The file in this bundle is a **design reference created in HTML + Canvas 2D** — a working prototype that shows the intended look, the generation pipeline, and the interactions. **It is not production code to copy directly.** The task is to **recreate this design in the RealmWeaver Unity project** using Unity's rendering (Texture2D / meshes / sprites / shaders) and the existing project conventions. Treat the HTML's JS as *executable pseudocode* for the algorithm — the math is exact and portable; the drawing calls map onto Unity equivalents (see `CLAUDE_CODE_UNITY.md`).

If the Unity project already has a map-generation module, integrate this as the **rendering/style layer** on top of its existing Voronoi/height data. If not, the pipeline below is self-contained and can be implemented from scratch.

## Fidelity
**High-fidelity (hifi).** Final colors, palettes, biome logic, marker shapes, typography, and composition are all specified exactly. Recreate the visual result faithfully. The prototype is resolution-independent (procedural) — reproduce the *rules*, not a pixel grid.

---

## Canvas / Coordinate System
- Design canvas: **1160 × 760 CSS px**, rendered internally at **DPR 1.4 → 1624 × 1064 px**. Aspect ratio ≈ **1.526 : 1**. In Unity, pick any target resolution with this aspect (e.g. 2048×1342 or 4096×2684 for print) — everything is normalized.
- All positions in this doc are given as **normalized [0..1]** of width (`nx`) and height (`ny`) unless a pixel value is stated. Multiply by the target texture width/height.
- The map is drawn back-to-front: **base raster → rivers → roads → depth-sorted sprites (by screen-y) → POI markers → labels → chrome (compass/scale/cartouche/frame) → vignette**.

---

## Generation Pipeline (the "un-flatten the polygons" system)

All noise is **value-noise fBm**. Reference implementation (portable, deterministic; `s` is a per-layer seed offset):

```
hash(ix,iy,s):   h = ix*374761393 + iy*668265263 + s*362437   (all int32, wrapping)
                 h = (h ^ (h>>13)) * 1274126177 ; h ^= h>>16 ; return (h>>>0)/2^32   // 0..1
smooth(t) = t*t*(3-2*t)
valueNoise(x,y,s): bilinear-interpolate hash at the 4 integer corners with smooth(frac)
fbm(x,y,s,oct): sum=0, amp=.5, freq=1; repeat oct times: sum += amp*valueNoise(x*freq,y*freq, s+i*97); freq*=2; amp*=.5
```

Let `nx=x/W`, `ny=y/H`. `SEA = 0.37` (sea level threshold on the elevation field).

### 1. Elevation `E(x,y)` — domain-warped island
```
wx = fbm(nx*2+11.3, ny*2+5.7,  S+31, 3) - 0.5
wy = fbm(nx*2+7.1,  ny*2+13.9, S+53, 3) - 0.5
e  = fbm((nx+wx*0.30)*3.5, (ny+wy*0.30)*3.5, S, 5)         // warped base terrain
d  = hypot((nx-0.5)*2.05, (ny-0.5)*2.05)                    // radial distance from center
E  = e - pow(d, 1.9)*1.12 + 0.15                            // island falloff → sea at edges
```

### 2. Lakes (carved inland water)
Place 2 lakes at `cx=(0.32+0.4*rand)*W, cy=(0.30+0.4*rand)*H, r=(0.05..0.08)*W`. For pixels within a noisy radius `rr = r*(0.7+0.5*fbm(x/60,y/60,S+301,3))`, lower `E` by `(1-dd/rr)*0.22`; if the result drops below `SEA`, tag the pixel as **lake** (rendered with lake colors, not sea colors).

### 3. Moisture and Warmth fields
```
moist(x,y)  = fbm(nx*2.6+5.2, ny*2.6+9.1, S+123, 4)                                   // 0..1
warmth(x,y) = nx*1.02 + ny*0.14 + fbm(nx*1.0+40,ny*1.0+80,S+2000,2)*0.22
              + (fbm(nx*0.7+3,ny*0.7+9,S+2100,2)-0.5)*0.12 - 0.24                       // ≈ -0.2..1.0
```
`warmth` is a **temperature gradient**: low = cold (west/north), high = warm (east/south). This creates the deliberate warm-vs-cold contrast (fiery forest & badlands vs pine forest & tundra).

### 4. Biome classification `biomeKey(E,moist,warmth)`
```
le = E - SEA
if le < 0.016            → coast
if le > 0.29             → (warmth<0.52) ? snow : peak
if le > 0.20             → highland
if le < 0.05 && moist>.70→ marsh
if warmth > 0.56         → (moist>0.52) ? forestWarm : badlands     // warm belt
if warmth < 0.34         → (moist>0.585) ? forest : tundra          // cold belt
if moist > 0.585         → forest
if moist < 0.40          → moor
else                     → plains
```

### 5. Base raster shading (per land pixel)
1. `base = palette[biomeKey]` (see palette tables).
2. **Regional variation** (subtle, keeps same biome from looking flat): blend `base` toward `lerp(tintCool, tintWarm, clamp((warmth-0.28)/0.42))` by **0.38**; then add micro-noise `(fbm(nx*1.6+20,ny*1.6+40,S+1500,2)-0.5)*38*regionVariation` to RGB (r full, g×0.9, b×0.7).
3. **Hillshade relief** (the fake-3D): compute a surface normal from neighbor elevations `nX=(E(x-1)-E(x+1))*95, nY=(E(y+1)-E(y-1))*95, nZ=1`; light dir `L=normalize(-0.6,-0.55,0.72)`; `sh = clamp(0.60 + 0.70*max(0, N·L), .., 1.34)`. Multiply color by `sh`.
4. **Cold moonlight**: add `palette.light * max(0,N·L) * coldAmt`, where `coldAmt = 0.10 + coldLight%/100 * 0.30`.
5. **Coastline**: any land pixel touching a sea pixel (4-neighborhood) → `palette.outline`.
6. **Grain**: add `(valueNoise(x*0.5,y*0.5,S+61)-0.5)*7` to RGB.
7. **Lightness variation**: multiply RGB by `1 + rgB*(land?0.24:0.12)*regionVariation` where `rgB = fbm(nx*2.0+50,ny*2.0+70,S+1600,2)-0.5`.

Water pixels: sea = `lerp(shallow, abyss, depth)` with `depth=clamp((SEA-E)/0.26)`, plus a ripple `(fbm(x/40,y/26,S+401,2)-0.5)*10`; **coast glow** — sea pixels near land (within 3px) blend toward `palette.glow` by `0.32 + coldAmt*0.5`. Lakes = `lerp(lakeS, lakeD, depth)`.

### 6. Polygon fallback (Voronoi)
A jittered-grid Voronoi (`cellSize=40*DPR`, F1/F2) is retained. When the **`smoothBorders`** tweak is OFF, each pixel takes the biome of its Voronoi cell's site and the cell edges (F2−F1 < 1.4px) are drawn as `outline` — this is the "before" (flat polygons) state, useful for debugging/comparison. Default ON.

### 7. Rivers
Pick up to 6 sources where `E > SEA+0.20`; **descend by steepest neighbor** (step 2px, 8-neighborhood) until reaching sea; also spawn one from each lake edge. Draw as a smoothed polyline: dark casing (`rgba(8,20,28,.6)`, width 4.4·DPR) then cold stroke `palette.glow @0.85` (width 2.3·DPR).

---

## Sprites (side-view "iso" elements)
All sprites are **depth-sorted by screen-y** (painter's order) and drawn after roads. Placement is jittered-grid sampling; the biome at each sample decides the sprite.

- **Pine (forest)** — 3 stacked triangles, dark→mid body with a lit left edge; snow dusting when cold (`warmth<0.36`). Grid ~11·DPR, prob 0.66.
- **Autumn tree (forestWarm)** — rounded amber canopy (overlapping circles) + trunk. Grid ~11·DPR, prob 0.62. This is the warm-belt vegetation.
- **Reeds (marsh)** — 3 thin curved strokes with seed-head tufts. Prob 0.42.
- **Mesa (badlands)** — small flat-topped trapezoid rock, lit/shadow faces. Prob 0.16.
- **Hill (highland, sparse)** — rounded bump, lit top. Prob ~0.3.
- **Mountain (peak/highland, `le≥0.20`)** — see next section. Grid 22·DPR, spawn if `hash≥0.52` (deliberately sparse).

### Mountains (hand-drawn range style — important)
Each mountain is a **variable jagged silhouette**, not a fixed triangle. Per-instance hashes pick one of three profiles:
- **single** peak (~30%), **twin** peak (~40%), **triple/ridge** (~30%); width `w = h*(0.54..0.80)`, plus an optional left foothill (~40%).
Rendering, in order:
1. Fill silhouette with a **vertical gradient** `mtnMidC (top) → mtnShDark (bottom)`.
2. **Rock hatching** on the shadow (right-of-main-ridge) side: fine diagonal parallel lines, clipped to the silhouette (`rgba(8,12,18,0.26)`, ~0.7·DPR, spacing `max(2.3·DPR, h*0.11)`).
3. **Lit faces**: for every *ascending* silhouette segment (left-facing slope), fill a light gradient quad `mtnHi (top) → mtnLit (bottom)` down to the base → consistent left-light / right-dark.
4. **Snow caps** on local maxima above `by - h*0.48` (wavy lower edge) when snowy.
5. **Ink outline** over the whole silhouette (`rgba(8,12,18,0.82)`, 1.3·DPR).
6. **Atmospheric haze**: peaks higher on screen (farther) get a translucent pale-blue overlay `rgba(150,178,198, haze*0.5)`, `haze = clamp((H*0.42 - screenY)/(H*0.5), 0, 0.5)` — distant ridges fade.

Derived mountain colors from palette:
```
mtnHi     = mtnL + light*coldAmt*0.9      (lit top)
mtnLit    = mtnL * 0.80                    (lit base)
mtnMidC   = mtnL * 0.50                    (shadow top)
mtnShDark = mtnS * 0.82                    (shadow base)
snow      = palette.snow
```

---

## POI Markers
Markers are drawn as a **medallion**: soft dark outer glow → radial dark disc (`#141c25→#080d14`) → dark rim (2.6·DPR) → **accent-color ring** (1.6·DPR), with the icon **centered inside via measured pixel bounds** (icon scaled so its bounding-box diagonal = `2*R*0.82`, then translated so the bbox center sits at the disc center). Icons use stone tones `dark #2b323d`, `light #414c5b`, `black #0a0d12`, `steel #c9d2dc`, `wood #4a3a28`, with the palette **accent** for flags/details.

Marker types (11):
| Type | Icon |
|---|---|
| `default` | Accent **?** glyph (unassigned POI) |
| `city` | Crenellated keep + flag |
| `fortress` | Three towers (tall center) + flag |
| `village` | Two gabled houses |
| `tower` | Single battlemented tower + flag |
| `temple` | Symmetric colonnade + triangular pediment |
| `ruins` | Two broken columns + fallen lintel on the ground |
| `dungeon` | Stone gate with dark arch + portcullis grid |
| `encounter` | Crossed steel swords |
| `camp` | Symmetric tent + crossed apex poles (no fire) |
| `port` | Anchor |

On the master map, markers are placed at **biome centroids** (see Labels) with small offsets; each registers a footprint so labels avoid it.

---

## Labels & Map Chrome
- **Region labels** are placed at the **centroid of each biome** (average position of all pixels of that key), only if the biome covers > ~10 sampled cells. This guarantees "Лес/Пустоши/Горы" land on the right terrain. Latin names on the map (font supports it): `SILVA UMBRARUM` (forest), `SILVA IGNEA` (forestWarm), `VASTA CINERIS` (badlands), `DORSUM CORVI` (peak), `CAMPI CANI` (plains), `GLACIES` (tundra), `PALUS NIGRA` (marsh). Sea: `MARE GELIDUM` (0.135, 0.43), `OCEANUS UMBRAE` (0.835, 0.90).
- **Cities** are keeps placed at plains/forest/badlands centroids, named (Vael, Corran, Ysmir), connected by dashed warm **roads** (`palette.road`, casing + `[7,5]` dash).
- **Collision avoidance**: a rectangle registry holds cartouche, compass, scale, city labels, and POI footprints; each new label nudges vertically (±0..2.8 line-heights) or is skipped if it can't fit. All labels carry a dark text-shadow halo for readability.
- **Chrome**: 8-point compass rose (top-right), alternating scale bar "mille passuum" (bottom-left), title cartouche "TERRA UMBRARUM / chartula regionum ignotarum" (top-left), double frame with corner ticks, and a radial **vignette** whose strength = `darkness` tweak.

### Typography
- **Display / labels-on-map / city names**: **Cinzel** (600–700). Region/sea labels: **IM Fell English** *italic*. UI/doc body: **Spectral**.
- On a 1160-wide design, region labels ≈ 14–17px italic (letter-spacing ~2px), city names ≈ 13px Cinzel 600 (letter-spacing ~1.4px), cartouche title ≈ 20px Cinzel 700. Scale everything by target-width/1160.

---

## Fog of War (player view)
A second render of the same base map with a fog overlay:
1. Build a cloud layer the size of the map: per-pixel color `lerp(fogA, fogB, t)` where `t = clamp(fbm(x/150+3,y/150+7,S+900,4)*0.75 + fbm(x/44,y/44,S+930,3)*0.35 - 0.05)`, alpha 248.
2. **Reveal** explored areas with `destination-out` radial gradients (radius ~175·DPR) centered on each visited city, plus thick soft strokes (`46..82·DPR`) along the road/route between them — feathered edges.
3. Add a faint **cold rim glow** (`palette.glow`, low alpha) around each revealed circle.
4. Composite: base map → fog layer → vignette. In the real app the reveal mask updates as the party moves.

---

## Tweakable Parameters (expose these in-engine)
| Key | Type | Default | Range | Effect |
|---|---|---|---|---|
| `palette` | enum | `Хладный сумрак` | 4 options (below) | Full color theme |
| `coldLight` | % | 58 | 0–100 | Strength of cold moonlight on lit slopes + coast glow |
| `regionVariation` | % | 45 | 0–100 | Strength of within-biome tonal/lightness drift |
| `darkness` | % | 72 | 40–100 | Vignette + overall dimming ("mrachnost'") — recommended 7/10 |
| `seed` | int | 7 | 1–999 | Regenerates the whole map deterministically |
| `smoothBorders` | bool | true | — | ON = organic biomes; OFF = flat Voronoi polygons |
| `mountains` | enum | `icons` | icons \| fills | Iso mountain sprites vs. plain hillshaded fill |
| `forests` | enum | `icons` | icons \| fills | Tree sprites vs. plain fill |
| `warmAccents` | bool | true | — | Warm gold accent (cities/labels) vs. cold accent |

---

## Design Tokens — Palette Slots (default theme "Хладный сумрак")
RGB arrays are what the shader/CPU code uses; hex is for reference.

| Slot | RGB | Hex | Slot | RGB | Hex |
|---|---|---|---|---|---|
| abyss | 6,15,24 | #060f18 | forestWarm | 150,96,44 | #96602c |
| sea | 11,30,44 | #0b1e2c | badlands | 128,84,54 | #805436 |
| shallow | 30,84,100 | #1e5464 | tundra | 120,132,140 | #78848c |
| glow | 120,200,214 | #78c8d6 | highland | 74,80,88 | #4a5058 |
| coast | 92,86,64 | #5c5640 | peak | 110,116,128 | #6e7480 |
| marsh | 36,58,50 | #243a32 | snow | 214,224,232 | #d6e0e8 |
| plains | 74,86,58 | #4a563a | lakeD | 16,44,58 | #102c3a |
| moor | 64,66,74 | #40424a | lakeS | 46,110,126 | #2e6e7e |
| forest | 24,58,46 | #183a2e | outline | 6,10,16 | #060a10 |
| mtnL | 140,150,164 | #8c96a4 | mtnS | 40,46,56 | #282e38 |
| light (cold) | 100,150,190 | #6496be | road | 176,150,96 | #b09660 |
| accent (warm) | — | #e6b25c | accentCold | — | #8fd8e6 |
| fogA | 16,24,34 | #101822 | fogB | 34,52,66 | #223442 |
| tintCool | 32,86,116 | #205674 | tintWarm | 150,102,46 | #96662e |

### Other palette variants (same slot order; core slots shown)
**Лунная сталь** (blue moonlit + copper): sea `16,32,62`, shallow `46,96,150`, glow `140,196,244`, forest `28,56,72`, forestWarm `168,110,60`, badlands `140,96,66`, tundra `150,168,190`, highland `70,84,104`, peak `112,126,150`, snow `224,234,248`, mtnL `152,168,198`, mtnS `42,50,70`, light `140,180,235`, accent `#f0b96a`, accentCold `#a9ccff`, road `168,158,128`, fogA `20,28,48`, fogB `44,60,90`, tintCool `58,96,162`, tintWarm `110,96,78`, abyss `8,14,30`, coast `84,88,96`, marsh `38,54,64`, plains `70,84,98`, moor `70,74,90`, lakeD `20,42,74`, lakeS `60,116,164`, outline `8,12,22`.

**Изумрудная бездна** (teal + gold): sea `8,40,44`, shallow `30,102,98`, glow `120,224,204`, forest `18,64,48`, forestWarm `176,116,44`, badlands `150,102,48`, tundra `126,156,146`, highland `56,86,80`, peak `92,120,116`, snow `210,230,220`, mtnL `122,158,150`, mtnS `30,48,46`, light `92,190,168`, accent `#f0bf5a`, accentCold `#7fe8cc`, road `172,158,96`, fogA `10,30,30`, fogB `26,60,58`, tintCool `26,116,104`, tintWarm `108,104,54`, abyss `4,20,20`, coast `86,92,58`, marsh `26,62,50`, plains `86,102,54`, moor `60,78,72`, lakeD `12,50,52`, lakeS `40,116,110`, outline `4,14,14`.

**Аметистовая ночь** (indigo + amber): sea `26,22,54`, shallow `74,66,132`, glow `168,150,244`, forest `42,48,80`, forestWarm `168,96,96`, badlands `150,90,84`, tundra `150,144,176`, highland `76,70,98`, peak `116,108,142`, snow `228,222,244`, mtnL `146,138,172`, mtnS `48,44,68`, light `150,132,232`, accent `#f0ad54`, accentCold `#c3acff`, road `178,150,120`, fogA `26,22,46`, fogB `50,44,80`, tintCool `74,70,152`, tintWarm `126,88,96`, abyss `14,10,30`, coast `92,80,86`, marsh `48,44,72`, plains `96,86,96`, moor `78,72,92`, lakeD `30,26,66`, lakeS `80,72,148`, outline `12,10,22`.

### Other tokens
- Background / desk: `#05080c`; page radial `#0c141d → #070b11 → #04060a`.
- Hillshade light dir: normalize(-0.6, -0.55, 0.72). Vignette center at (0.5, 0.42).

## Assets
No external image assets — **everything is generated procedurally** (noise fields + vector-drawn sprites/markers). Fonts: **Cinzel**, **IM Fell English**, **Spectral** (Google Fonts) — bundle equivalents (TMP assets) in Unity. No Anthropic brand assets are used.

## Screenshots (`screens/`)
Reference renders of the default theme ("Хладный сумрак"):
- `screens/01-master-view.png` — DM's full map (all biomes, mountains, POIs, labels, chrome).
- `screens/02-fog-of-war.png` — players' view, only explored land/routes revealed.
- `screens/03-poi-markers.png` — the 11 POI marker medallions with labels.

## Files
- `Terra Umbrarum.dc.html` — the complete working reference: generation, master + fog views, POI legend, palette/decision panels. Open in a browser to inspect any value live; the `<script data-dc-script>` block at the bottom contains the full algorithm (`renderBase`, `renderFog`, `drawPOI`, `drawPOIMarker`, `palettes`).
- `CLAUDE_CODE_UNITY.md` — implementation guide for Claude Code targeting Unity.
