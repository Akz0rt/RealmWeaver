# Region Labels — Zoom LOD Tiers, Continents & Contrast (design)

## Context

After the biome-zone naming redesign shipped, the DM's Editor test surfaced three
presentation problems (readability, drift, deterministic "Пересоздать") which were fixed
inline. A follow-up round of feedback asks for three more improvements:

1. **Labels overlap each other when zoomed out.** The drift fix removed the old
   collision-nudge (which slid labels off their biomes), so now crowded labels overlap.
2. **A macro zoom tier.** When zoomed out too far, biome-zone names should disappear and
   instead show **continent (landmass) name(s) + sea names**.
3. **Labels blend into the map.** Even after the contrast bump, text needs stronger
   separation from the terrain.

Current LOD (`RegionLabelOverlay.LodAlpha`): alpha ramps up as you zoom OUT
(`orthoSize/NaturalFitSize` ≥ `farFrac`=0.8 → full; ≤ `nearFrac`=0.35 → hidden). The camera
zoom ratio ranges `[minSizeFraction 0.08 … maxSizeFraction 3.0]`, with fit-to-screen = 1.0.
Labels are a single mixed list (biome zones + 2 sea anchors); there is no continent concept.

## Goals

- **Three-tier zoom LOD:** close = nothing; mid (incl. fit-to-screen) = biome-zone names;
  far (zoomed out beyond fit) = continent + sea names, biomes hidden.
- **Continents:** one invented (fantasy proper-noun) name per connected landmass, shown at
  the far tier.
- **Declutter without drift:** within a visible tier, hide (not move) lower-priority labels
  that overlap higher-priority ones; labels stay pinned to their world anchors.
- **Contrast:** a soft drop-shadow (TMP underlay) behind label text (plus the existing outline).

## Non-goals / unchanged

- Biome-zone naming, density slider, edit mode, reroll, persistence format, save/load wiring.
- Sub-dividing a landmass into multiple named "realms" (one continent name per landmass).

## Design

### 1. Label kinds + priority (`RegionLabelData`)

Add two fields (additive, auto-serialized by Newtonsoft like the existing `SeedFamily` enum —
NO `ProjectSerializer` change; old `.dndproj` → defaults):

```csharp
public enum LabelKind { Biome = 0, Continent = 1, Sea = 2 }   // in the RegionLabels namespace
...
public LabelKind Kind;      // default Biome (0) → legacy saves load as biome labels
public float Priority;      // higher wins overlap culling (biome = zone cell count; continent/sea = large)
```

### 2. Continent name generator (`RegionLabelNames`)

Add an **isolated** syllable-based invented-name generator (separate from the biome noun
table; independent of the planned biome rework). Cyrillic, deterministic, shares the reroll
salt via the same `seed` argument:

```csharp
// Isolated continent syllable pools (edit freely; unrelated to the biome table).
static readonly string[] ContinentOnsets  = { "Вэл","Каэр","Тарн","Морн","Драг","Эль","Вор","Нар","Ске","Тир","Улл","Фэн","Гэл","Хад","Рун","Аск" };
static readonly string[] ContinentCodas   = { "дрим","вейл","морн","гард","холд","рун","тар","нор","вен","дал","мир","рат","гейт","ланд" };

public static string ContinentName(int seed, int key)   // deterministic; combine one onset + one coda
```

Compose `Onsets[hashA % n] + Codas[hashB % m]` from two decorrelated hashes of `(seed, key)`
(reuse the FNV `Hash`, mixing `key` with a constant for the second draw). Result e.g.
`"Вэлдрим"`, `"Каэрхолд"`, `"Тарнвейл"`.

### 3. Placer emits continents + sets Kind/Priority (`RegionLabelPlacer.Place`)

- **Continents:** a second BFS pass groups connected **land** cells (biome-agnostic —
  `RegionCategories.IsLandCell`) into landmass components. Each component ≥ a continent size
  threshold (e.g. `ContinentMinCells = 40`, larger than biome zones) → one `Continent` label:
  `ContinentName(seed, landmassKey)` (landmassKey = min land-cell Id), on-land anchor
  (`OnLandAnchor`), `Priority` = component cell count + a large bias so continents outrank
  biomes in culling. Usually one landmass ⇒ one continent label.
- **Biome labels:** unchanged selection/naming, but now set `Kind = Biome`,
  `Priority = comp.Count`.
- **Sea labels:** set `Kind = Sea`, `Priority` = large bias (so they aren't culled by biomes —
  though they never share a tier with biomes anyway).
- `Place` still returns one `List<RegionLabelData>` (continents + biomes + seas mixed); the
  overlay separates them by `Kind`.

### 4. Three-tier LOD (`RegionLabelOverlay`)

Replace the single `LodAlpha` with per-kind alpha driven by the zoom ratio
`r = orthoSize / NaturalFitSize`. Serialized, DM-tunable fracs (defaults chosen so
fit-to-screen r≈1.0 still shows biomes; continents engage only when zoomed OUT past fit):

- `nearFrac = 0.35`, `farFrac = 0.6` — biome fade-in band (as today, lower `farFrac`).
- `macroLoFrac = 1.3`, `macroHiFrac = 1.8` — biome→macro crossover band.
- **Biome alpha** = `smoothstep(nearFrac, farFrac, r) · (1 − smoothstep(macroLoFrac, macroHiFrac, r))`
  → visible in the mid band `[~0.6, ~1.3]`, fading at both ends.
- **Continent & Sea alpha** = `smoothstep(macroLoFrac, macroHiFrac, r)` → visible far
  `[~1.3, 3.0]`.
- Close (`r < nearFrac`): all ~0.

Each label's target alpha is chosen by its `Kind`. (A label already off-screen still parks at
`(-9999,-9999)` as today.)

### 5. Overlap culling without drift (`RegionLabelOverlay.LateUpdate`)

Labels stay pinned to their projected anchors (no nudge). Add per-frame culling **within the
currently-visible set**:

- Build the list of on-screen labels whose kind-alpha > ~0.01, sorted by `Priority` descending.
- Walk it: keep a list of placed screen rects; for each label, if its rect overlaps any placed
  rect, set its alpha to 0 (hidden this frame); else place it and keep it.
- Continents/seas (high Priority) win; among biomes, bigger zones win. Since biomes and
  continents never co-occur (different tiers), culling is effectively within-tier.

This declutters the mid tier while keeping every shown label exactly on its region.

### 6. Contrast — soft drop-shadow (TMP underlay)

Give the label TMP a material with **underlay** (soft shadow) enabled, in addition to the
existing outline:

- Create one shared material instance from `labelFont.material`, `EnableKeyword("UNDERLAY_ON")`,
  set `_UnderlayColor` (dark, ~`(0,0,0,0.7)`), small `_UnderlayOffsetX/Y` (≈1/-1),
  `_UnderlaySoftness` (≈0.35), assign it as the labels' `fontSharedMaterial`.
- Keep `outlineWidth`/`outlineColor` from the previous fix. Underlay gives the soft lift;
  outline keeps the crisp edge.
- The default TMP SDF material (from Font Asset Creator) supports underlay. If the shadow needs
  tuning, the DM adjusts the material properties. This is the one part only visually verifiable
  in the Editor.

## Affected components

- **Modify:** `RegionLabelData.cs` (LabelKind enum + Kind/Priority fields).
- **Modify:** `RegionLabelNames.cs` (continent name generator — new isolated syllable pools).
- **Modify:** `RegionLabelPlacer.cs` (continent BFS pass + Kind/Priority on all labels).
- **Modify:** `RegionLabelOverlay.cs` (per-kind 3-tier LOD, overlap culling, underlay material).
- **Modify:** `RegionLabelSelfTests.cs` (continent-name determinism; placer emits a continent
  label with Kind=Continent).
- **Unchanged:** persistence (additive fields auto-serialize), manager, panel, save/load.

## Testing

- `RegionLabelNames`: continent-name determinism + varies with seed (reroll salt).
- `RegionLabelPlacer`: a land fixture yields one `Continent` label (Kind=Continent, invented
  name) plus the biome labels (Kind=Biome), each with a sensible Priority.
- LOD / culling / underlay: DM Editor checkpoint (zoom through the three tiers; verify no
  overlap in the mid tier; verify the shadow lifts text off the terrain). Agents can't run Unity.

## Backward compatibility

- `Kind`/`Priority` are additive; old `.dndproj` loads with `Kind=Biome`, `Priority=0` (biome
  labels behave as before; they just get culled/tiered by the new overlay). No format bump.
