# Region Labels — Biome-Zone Naming Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the repeated Latin biome-family labels with unique, Russian, descriptive biome-zone names; make labels sparse (density slider); and stop labels from blocking scroll-zoom via an edit-mode toggle.

**Architecture:** A new pure-C# `RegionLabelNames` (isolated biome→noun table + adjective pool + deterministic unique name picker) feeds the existing `RegionLabelPlacer`, which now filters zones by a density threshold and anchors names on land. `RegionLabelManager` threads a `labelDensity` value from `WorldMapRenderer`. `RegionLabelOverlay` gains an edit-mode flag that gates `raycastTarget`/editing (display-only by default → labels no longer intercept the cursor). `MapLayersPanel` gets an edit-mode toggle + a density slider.

**Tech Stack:** Unity 6000.3.2f1, C#, uGUI, TextMeshPro, `System.Numerics.Vector2` for map coords. Spec: `docs/superpowers/specs/2026-07-10-region-labels-biome-zone-redesign-design.md`.

## Global Constraints

- **Unity, no CLI test runner:** implementers write code + a static hand-trace self-review + commit (report DONE_WITH_CONCERNS — they cannot compile). `[ContextMenu]` self-tests and visual/interaction checks are the USER's Editor step at checkpoints. Do NOT claim tests were run.
- **Namespace:** `WorldGen.Rendering.RegionLabels` for new/existing label files. `BiomeFamily`/`RegionCategories`/`NearestCellLookup` live in `WorldGen.Rendering.MapRaster` (already `using`-imported by `RegionLabelPlacer.cs`).
- **`System.Numerics.Vector2` vs `UnityEngine.Vector2`:** map/world coords are `System.Numerics.Vector2`. This project has hit CS0104 ambiguity 3× — fully-qualify wherever both `using`s are present. `RegionLabelNames`/`RegionLabelPlacer` are pure C# with no `UnityEngine` using (safe); the overlay/panel files import `UnityEngine` — never leave a bare `Vector2` for a world coord there.
- **Deterministic, no `Random`:** names must be stable for a given `seed` (same map ⇒ same names) and unique within a biome family on a map. Zones are processed in a deterministic order (ascending `zoneKey`) so the used-adjective set evolves deterministically.
- **Biome→noun table is the ONLY place biome names live** (DM plans a future biome-type rework — keep it a single isolated table; no biome name hardcoded elsewhere).
- **Grouping stays by biome FAMILY** (`RegionCategories.FamilyCategoryOf`, connected components over `NeighborIds`) — NOT by `RegionId`.
- **Font is a DM Editor step, not a code task:** Forum (OFL, Cyrillic) SDF asset → `RegionLabelOverlay.labelFont`. No code depends on the specific font.
- **Density defaults:** `labelDensity` float `[0,1]`, serialized default `0.4f`. Maps to a minimum zone size in cells: `minZoneCells = round(Lerp(40, 6, labelDensity))` (density 0 → 40 = only giants; 1 → 6 = include medium; 0.4 → ~26).

---

### Task 1: RegionLabelNames — biome→noun table + adjective pool + deterministic unique namer

**Files:**
- Create: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelNames.cs`
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs` (add a `[ContextMenu]` names self-test)

**Interfaces:**
- Produces: `RegionLabelNames.NameFor(BiomeFamily family, int seed, int zoneKey, System.Collections.Generic.HashSet<int> usedAdjIndices) : string` — returns a composed `"Прилагательное Существительное"` for named families, or `null` for unnamed families (Coast/Lake). Adds the chosen adjective index to `usedAdjIndices`.

- [ ] **Step 1: Write `RegionLabelNames.cs` in full**

```csharp
using System.Collections.Generic;
using WorldGen.Rendering.MapRaster; // BiomeFamily

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Pure, deterministic Russian region-name generator: a biome-family -> noun table
    /// (the SINGLE place biome names live — kept isolated for the planned biome-type rework) plus a
    /// gender-agreeing adjective pool. Picks a unique adjective per zone within a family. No Random.</summary>
    public static class RegionLabelNames
    {
        public enum Gender { Masculine, Feminine, Neuter, Plural }

        readonly struct Noun
        {
            public readonly string Word;
            public readonly Gender Gender;
            public Noun(string word, Gender gender) { Word = word; Gender = gender; }
        }

        readonly struct Adjective
        {
            public readonly string M, F, N, Pl;
            public Adjective(string m, string f, string n, string pl) { M = m; F = f; N = n; Pl = pl; }
            public string For(Gender g) => g switch
            {
                Gender.Masculine => M,
                Gender.Feminine  => F,
                Gender.Neuter    => N,
                _                => Pl,
            };
        }

        // THE isolated biome-name table. A future biome rework edits ONLY this dictionary.
        static readonly Dictionary<BiomeFamily, Noun> Nouns = new Dictionary<BiomeFamily, Noun>
        {
            { BiomeFamily.Forest,     new Noun("Лес",     Gender.Masculine) },
            { BiomeFamily.ForestWarm, new Noun("Дубрава", Gender.Feminine)  },
            { BiomeFamily.Badlands,   new Noun("Пустошь", Gender.Feminine)  },
            { BiomeFamily.Plains,     new Noun("Луга",    Gender.Plural)    },
            { BiomeFamily.Highland,   new Noun("Кряж",    Gender.Masculine) },
            { BiomeFamily.Snow,       new Noun("Снега",   Gender.Plural)    },
            { BiomeFamily.Moor,       new Noun("Топь",    Gender.Feminine)  },
            { BiomeFamily.Tundra,     new Noun("Тундра",  Gender.Feminine)  },
            { BiomeFamily.Sea,        new Noun("Море",    Gender.Neuter)    },
            // Coast, Lake intentionally absent -> NameFor returns null (unnamed).
        };

        static readonly Adjective[] Adjectives =
        {
            new Adjective("Сумрачный",  "Сумрачная",  "Сумрачное",  "Сумрачные"),
            new Adjective("Пепельный",  "Пепельная",  "Пепельное",  "Пепельные"),
            new Adjective("Золотой",    "Золотая",    "Золотое",    "Золотые"),
            new Adjective("Вечный",     "Вечная",     "Вечное",     "Вечные"),
            new Adjective("Северный",   "Северная",   "Северное",   "Северные"),
            new Adjective("Древний",    "Древняя",    "Древнее",    "Древние"),
            new Adjective("Багряный",   "Багряная",   "Багряное",   "Багряные"),
            new Adjective("Туманный",   "Туманная",   "Туманное",   "Туманные"),
            new Adjective("Забытый",    "Забытая",    "Забытое",    "Забытые"),
            new Adjective("Стылый",     "Стылая",     "Стылое",     "Стылые"),
            new Adjective("Мёртвый",    "Мёртвая",    "Мёртвое",    "Мёртвые"),
            new Adjective("Гиблый",     "Гиблая",     "Гиблое",     "Гиблые"),
            new Adjective("Тихий",      "Тихая",      "Тихое",      "Тихие"),
            new Adjective("Дикий",      "Дикая",      "Дикое",      "Дикие"),
            new Adjective("Хладный",    "Хладная",    "Хладное",    "Хладные"),
            new Adjective("Ветреный",   "Ветреная",   "Ветреное",   "Ветреные"),
            new Adjective("Полуночный", "Полуночная", "Полуночное", "Полуночные"),
            new Adjective("Седой",      "Седая",      "Седое",      "Седые"),
            new Adjective("Угрюмый",    "Угрюмая",    "Угрюмое",    "Угрюмые"),
            new Adjective("Мглистый",   "Мглистая",   "Мглистое",   "Мглистые"),
            new Adjective("Кровавый",   "Кровавая",   "Кровавое",   "Кровавые"),
            new Adjective("Терновый",   "Терновая",   "Терновое",   "Терновые"),
            new Adjective("Вороний",    "Воронья",    "Воронье",    "Вороньи"),
            new Adjective("Волчий",     "Волчья",     "Волчье",     "Волчьи"),
        };

        /// <summary>Deterministic FNV-1a-style mix of seed+zoneKey (no Random, stable across runs).</summary>
        static int Hash(int seed, int zoneKey)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)seed) * 16777619u;
                h = (h ^ (uint)zoneKey) * 16777619u;
                return (int)h;
            }
        }

        /// <summary>Composed name for a zone, or null if the family is unnamed (Coast/Lake).
        /// Picks the hashed adjective index, linear-probing forward for one not yet used by THIS
        /// family on THIS map (mutates usedAdjIndices). If all are used (>pool zones of one family),
        /// reuses the last probed index.</summary>
        public static string NameFor(BiomeFamily family, int seed, int zoneKey, HashSet<int> usedAdjIndices)
        {
            if (!Nouns.TryGetValue(family, out var noun)) return null;
            int n = Adjectives.Length;
            int start = (int)((uint)Hash(seed, zoneKey) % (uint)n);
            int chosen = start;
            for (int probe = 0; probe < n; probe++)
            {
                int cand = (start + probe) % n;
                chosen = cand;
                if (usedAdjIndices.Add(cand)) break; // found an unused adjective for this family
            }
            return Adjectives[chosen].For(noun.Gender) + " " + noun.Word;
        }
    }
}
```

- [ ] **Step 2: Static hand-trace self-review**

Trace: `NameFor(Forest, 1, 5, fresh)` and `NameFor(Badlands, 1, 5, fresh)` pick the SAME `start = Hash(1,5) % 24` (fresh set → probe 0 succeeds) but different gender forms → e.g. `"Сумрачный Лес"` (M) vs `"Сумрачная Пустошь"` (F). `NameFor(Forest, 1, 5, shared)` then `NameFor(Forest, 1, 6, shared)` → second call's hashed index may collide only if `Hash(1,6)%24 == chosen`; if so it probes to the next free index → different adjective. `NameFor(Coast/Lake, …)` → not in `Nouns` → null. Confirm no `UnityEngine` using (pure C#, no CS0104), `BiomeFamily` resolves via `WorldGen.Rendering.MapRaster`.

- [ ] **Step 3: Add the names self-test to `RegionLabelSelfTests.cs`**

Add this `[ContextMenu]` method inside the existing `RegionLabelSelfTests` MonoBehaviour (it already `using`s `WorldGen.Rendering.MapRaster` for `BiomeFamily`):

```csharp
[ContextMenu("Self-Test: Region Label Names")]
public void SelfTestNames()
{
    // Determinism: same (family, seed, zoneKey) + fresh used-set -> identical name.
    string a1 = RegionLabelNames.NameFor(BiomeFamily.Forest, 1, 5, new System.Collections.Generic.HashSet<int>());
    string a2 = RegionLabelNames.NameFor(BiomeFamily.Forest, 1, 5, new System.Collections.Generic.HashSet<int>());
    bool ok = a1 != null && a1 == a2 && a1.EndsWith(" Лес");

    // Gender agreement: same seed+zoneKey+fresh set -> same adjective index, different gender forms.
    // Forest is Masculine, Badlands is Feminine, so the adjective token must differ in ending.
    string f = RegionLabelNames.NameFor(BiomeFamily.Forest,   7, 3, new System.Collections.Generic.HashSet<int>());
    string b = RegionLabelNames.NameFor(BiomeFamily.Badlands, 7, 3, new System.Collections.Generic.HashSet<int>());
    ok &= f.EndsWith(" Лес") && b.EndsWith(" Пустошь");
    string fAdj = f.Substring(0, f.Length - " Лес".Length);
    string bAdj = b.Substring(0, b.Length - " Пустошь".Length);
    ok &= fAdj != bAdj;                          // masculine vs feminine form differ

    // Uniqueness within a family: shared set -> two zones get different adjectives.
    var shared = new System.Collections.Generic.HashSet<int>();
    string z1 = RegionLabelNames.NameFor(BiomeFamily.Plains, 2, 10, shared);
    string z2 = RegionLabelNames.NameFor(BiomeFamily.Plains, 2, 11, shared);
    ok &= z1 != z2 && z1.EndsWith(" Луга") && z2.EndsWith(" Луга");

    // Unnamed families -> null.
    ok &= RegionLabelNames.NameFor(BiomeFamily.Coast, 1, 1, new System.Collections.Generic.HashSet<int>()) == null;
    ok &= RegionLabelNames.NameFor(BiomeFamily.Lake,  1, 1, new System.Collections.Generic.HashSet<int>()) == null;

    Debug.Log(ok ? "Self-Test Region Label Names: PASS" : "Self-Test Region Label Names: FAIL");
}
```

- [ ] **Step 4: USER runs the self-test** → add/keep a `RegionLabelSelfTests` component on a scene object → right-click → "Self-Test: Region Label Names" → expect PASS. (Batched with Task 2's checkpoint.)

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelNames.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs
git commit -m "feat(region-labels): Russian noun+adjective name generator (isolated biome table, deterministic, gender-agreeing)"
```
(USER commits the generated `RegionLabelNames.cs.meta` after Editor import.)

---

### Task 2: RegionLabelPlacer — density threshold + Russian names + on-land anchor

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs`
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs` (rewrite the placer self-test assertions)

**Interfaces:**
- Consumes: `RegionLabelNames.NameFor(...)` (Task 1).
- Produces: `RegionLabelPlacer.Place(IReadOnlyList<VoronoiCell> cells, NearestCellLookup nearest, float mapWidth, float mapHeight, int seed = 0, float labelDensity = 0.4f) : List<RegionLabelData>` — REPLACES the old `minPatchCells` param with `seed` + `labelDensity`. **Both new params have defaults** so the not-yet-updated `RegionLabelManager` 4-arg call still compiles between this task and Task 3 (avoids a mid-plan non-compiling build); Task 3 then passes the real seed+density.

- [ ] **Step 1: Replace the naming/threshold internals of `Place`**

In `RegionLabelPlacer.cs`:
1. Delete the `LandNames` dictionary (lines 15-26) and the `DefaultMinPatchCells` const — names now come from `RegionLabelNames`.
2. Add density constants at the top of the class:
```csharp
public const float DefaultLabelDensity = 0.4f;
const int MaxZoneCells = 40; // density 0 -> only giants
const int MinZoneCells = 6;  // density 1 -> include medium (matches the old minPatchCells floor)
```
3. Change the signature and body. New `Place` (keep the existing BFS component discovery — only the gate, naming, anchor, and sea labels change):
```csharp
public static List<RegionLabelData> Place(IReadOnlyList<VoronoiCell> cells,
    NearestCellLookup nearest, float mapWidth, float mapHeight,
    int seed = 0, float labelDensity = DefaultLabelDensity)
{
    var result = new List<RegionLabelData>();
    if (cells == null || cells.Count == 0) return result;

    int minZoneCells = Mathf_RoundLerp(MaxZoneCells, MinZoneCells, Clamp01(labelDensity));

    var byId = new Dictionary<int, VoronoiCell>();
    foreach (var c in cells) byId[c.Id] = c;

    // Discover connected same-family land components (unchanged BFS), keep those >= threshold.
    var components = new List<(int family, List<VoronoiCell> cellsInZone, int zoneKey)>();
    var visited = new HashSet<int>();
    foreach (var start in cells)
    {
        if (visited.Contains(start.Id)) continue;
        int fam = RegionCategories.FamilyCategoryOf(start);
        if (fam < 0) { visited.Add(start.Id); continue; } // water

        var comp = new List<VoronoiCell>();
        var queue = new Queue<VoronoiCell>();
        queue.Enqueue(start); visited.Add(start.Id);
        int zoneKey = start.Id;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            comp.Add(c);
            if (c.Id < zoneKey) zoneKey = c.Id;      // min cell Id = stable zone key
            foreach (var nid in c.NeighborIds)
            {
                if (visited.Contains(nid)) continue;
                if (!byId.TryGetValue(nid, out var nc)) continue;
                if (RegionCategories.FamilyCategoryOf(nc) != fam) continue;
                visited.Add(nid);
                queue.Enqueue(nc);
            }
        }
        if (comp.Count >= minZoneCells) components.Add((fam, comp, zoneKey));
    }

    // Deterministic naming order: ascending zoneKey so the used-adjective sets evolve stably.
    components.Sort((x, y) => x.zoneKey.CompareTo(y.zoneKey));

    var usedByFamily = new Dictionary<BiomeFamily, HashSet<int>>();
    foreach (var (fam, comp, zoneKey) in components)
    {
        var family = (BiomeFamily)fam;
        if (!usedByFamily.TryGetValue(family, out var used))
        {
            used = new HashSet<int>();
            usedByFamily[family] = used;
        }
        string name = RegionLabelNames.NameFor(family, seed, zoneKey, used);
        if (name == null) continue; // Coast/Lake families never land here (water skipped), defensive

        result.Add(new RegionLabelData
        {
            Text = name,
            WorldPosition = OnLandAnchor(comp),
            SeedFamily = family,
        });
    }

    AddSeaLabels(result, nearest, mapWidth, mapHeight, seed);
    return result;
}

static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
static int Mathf_RoundLerp(int a, int b, float t) => (int)System.Math.Round(a + (b - a) * t);
```
4. Replace `AreaWeightedCentroid` usage with `OnLandAnchor` — the area-weighted centroid snapped to the nearest component cell's `Site` (guarantees on land):
```csharp
static System.Numerics.Vector2 OnLandAnchor(List<VoronoiCell> comp)
{
    var centroid = AreaWeightedCentroid(comp); // keep the existing method
    var best = comp[0];
    double bestD = double.MaxValue;
    foreach (var c in comp)
    {
        double dx = c.Site.X - centroid.X, dy = c.Site.Y - centroid.Y;
        double d = dx * dx + dy * dy;
        if (d < bestD) { bestD = d; best = c; }
    }
    return best.Site; // a cell Site in the component -> always land
}
```
   Keep `AreaWeightedCentroid` and `PolygonArea` as they are.
5. Update `AddSeaLabels` to take `int seed` and name via `RegionLabelNames`:
```csharp
static void AddSeaLabels(List<RegionLabelData> result, NearestCellLookup nearest, float mapW, float mapH, int seed)
{
    if (nearest == null) return;
    (float nx, float ny)[] cands = { (0.135f, 0.43f), (0.835f, 0.90f) };
    var usedSea = new HashSet<int>();
    for (int i = 0; i < cands.Length; i++)
    {
        var pos = new System.Numerics.Vector2(cands[i].nx * mapW, cands[i].ny * mapH);
        var cell = nearest.FindNearest(pos);
        if (cell != null && cell.EffectiveIsOcean)
        {
            string name = RegionLabelNames.NameFor(BiomeFamily.Sea, seed, 1000 + i, usedSea);
            result.Add(new RegionLabelData { Text = name, WorldPosition = pos, SeedFamily = BiomeFamily.Sea });
        }
    }
}
```
6. Update the class XML summary to say "Russian-named biome zones above a density threshold, anchored on land."

- [ ] **Step 2: Static hand-trace self-review**

Trace: a fixture with a big Forest component (≥ threshold) and a tiny one (< threshold): only the big one is named; name ends with " Лес". Two forest components of equal-or-different size both above threshold get DIFFERENT adjectives (shared `usedByFamily[Forest]`). `OnLandAnchor` returns a `Site` from the component (never water). No `UnityEngine` using added (still pure C#); `System.Numerics.Vector2` fully-qualified. Confirm callers of `Place` (manager + self-test) will be updated in this task / Task 3.

- [ ] **Step 3: Rewrite the placer self-test assertions in `RegionLabelSelfTests.cs`**

The existing `SelfTestPlacer` builds two land patches (Forest ids 0-6, Plains ids 7-13) + a lone third-family cell. Update its `Place` call and assertions (fixture construction stays; just change the call + asserts):
```csharp
// Old call used minPatchCells: 6. New call passes seed + a high density so the ~7-cell patches qualify.
var labels = RegionLabelPlacer.Place(cells, /*nearest*/ null, 100f, 100f, seed: 1, labelDensity: 1f);

bool ok = labels.Count == 2;                                  // two big patches named, lone cell dropped
ok &= labels.Exists(l => l.Text != null && l.Text.EndsWith(" Лес"));   // Forest zone -> "... Лес"
ok &= labels.Exists(l => l.Text != null && l.Text.EndsWith(" Луга"));  // Plains zone -> "... Луга"
// On-land anchor: each label sits at one of its component cells' Sites (bbox 0..100).
var forest = labels.Find(l => l.Text.EndsWith(" Лес"));
ok &= forest != null && forest.WorldPosition.X >= 0 && forest.WorldPosition.X <= 100;
// Determinism: a second identical Place gives identical names.
var labels2 = RegionLabelPlacer.Place(cells, null, 100f, 100f, seed: 1, labelDensity: 1f);
ok &= labels2.Count == labels.Count
   && labels2.Find(l => l.Text.EndsWith(" Лес"))?.Text == forest.Text;
// Density threshold drops small patches: at low density the ~7-cell patches fall below MaxZoneCells=40.
var sparse = RegionLabelPlacer.Place(cells, null, 100f, 100f, seed: 1, labelDensity: 0f);
ok &= sparse.Count == 0;

Debug.Log(ok ? "Self-Test Region Label Placer: PASS" : "Self-Test Region Label Placer: FAIL");
```
(The lone under-threshold third-family cell producing no label is now covered by the general threshold — keep the lone cell in the fixture; it stays unnamed.)

- [ ] **Step 4: USER runs both self-tests** → "Self-Test: Region Label Names" + "Self-Test: Region Label Placer" → expect PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs
git commit -m "feat(region-labels): density-thresholded Russian-named biome zones + on-land anchor"
```

---

### Task 3: labelDensity wiring — WorldMapRenderer field + RegionLabelManager seed/density pass-through

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelManager.cs`

**Interfaces:**
- Consumes: `RegionLabelPlacer.Place(cells, nearest, mapW, mapH, seed, labelDensity)` (Task 2); `WorldMapRenderer.seed` (public int, exists), `WorldMapRenderer.labelDensity` (new).
- Produces: `WorldMapRenderer.labelDensity` (serialized float `[0,1]`, default 0.4) read by the panel (Task 5) and the manager.

- [ ] **Step 1: Add the serialized field to `WorldMapRenderer.cs`**

Near the other serialized label/render fields (e.g. next to `regionBorderWidth`/`regionBorderColor` around line 111, or wherever sibling `[Range]` fields sit), add:
```csharp
        [Header("Region labels")]
        [Range(0f, 1f)]
        [Tooltip("Плотность названий зон: меньше = только крупные зоны получают имя, больше = включать средние.")]
        public float labelDensity = 0.4f;
```

- [ ] **Step 2: Thread seed + labelDensity in `RegionLabelManager.SeedFromCells`**

Change the `Place` call (currently `RegionLabelPlacer.Place(mapRenderer.Cells, mapRenderer.NearestLookup, mapRenderer.mapWidth, mapRenderer.mapHeight)`):
```csharp
var seeded = RegionLabelPlacer.Place(mapRenderer.Cells, mapRenderer.NearestLookup,
    mapRenderer.mapWidth, mapRenderer.mapHeight, mapRenderer.seed, mapRenderer.labelDensity);
```
(`mapRenderer.seed` is `public int seed` at `WorldMapRenderer.cs:21`; `labelDensity` from Step 1. No other change to the manager.)

- [ ] **Step 3: Static hand-trace self-review**

Confirm the `Place` arg order/types match Task 2's signature exactly (`int seed`, then `float labelDensity`); `labelDensity` compiles as a serialized `[Range]` float; no other caller of `Place` remains on the old signature except the self-test (updated in Task 2).

- [ ] **Step 4: USER Editor eyeball** → after import, confirm `WorldMapRenderer` Inspector shows `labelDensity = 0.4` (Unity serialization gotcha: brand-new field uses the C# default; verify it isn't 0). Batched with Task 5's checkpoint.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelManager.cs
git commit -m "feat(region-labels): thread seed + labelDensity from renderer into the placer"
```

---

### Task 4: RegionLabelOverlay — edit-mode gate (display-only by default, no cursor interception)

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs`

**Interfaces:**
- Produces: `RegionLabelOverlay.SetEditMode(bool)` (default edit mode OFF). Consumed by `MapLayersPanel` (Task 5).

- [ ] **Step 1: Add the edit-mode flag + gate raycastTarget/editing**

1. Add a field: `bool editMode = false;` and a public setter:
```csharp
public void SetEditMode(bool on)
{
    if (editMode == on) return;
    editMode = on;
    ApplyEditModeToViews();               // flip every existing container's click raycastTarget
    if (!on)
    {
        addMode = false;                  // leaving edit mode cancels add-mode
        if (manager != null) manager.DeselectAll(); // tears down the rename box via OnSelectionChanged
    }
}
```
2. Add `ApplyEditModeToViews()` that sets each label's click-target `Image.raycastTarget = editMode` (the transparent Image on each container built in the current CRUD overlay). Grep this file for where the container's `Image`/`raycastTarget` is created (Task-5 CRUD work — the container's click `Image`); store a reference to it on the per-label view holder (e.g. add `public Image ClickTarget;` to the `LabelView` holder) and, in `ApplyEditModeToViews`, iterate the views setting `ClickTarget.raycastTarget = editMode`.
3. In the per-label construction (`CreateLabelView`/container build), set the click `Image.raycastTarget = editMode` at creation time (so labels rebuilt while in display mode are non-blocking), and record it on the holder.
4. Guard the interaction entry points so they no-op when `!editMode`: at the top of the pointer-handler callbacks (`HandleLabelClicked`/`HandleLabelDragBegin`/`HandleLabelDrag`) and `HandleMapClick`'s add/deselect logic and `ToggleAddMode`, early-return if `!editMode`. (Even with raycastTarget off the labels can't be clicked, but `ToggleAddMode`/`HandleMapClick` are driven by the panel/Mouse polling, so gate them too.)

- [ ] **Step 2: Static hand-trace self-review**

Trace: default `editMode == false` → every container's click `Image.raycastTarget == false` → `EventSystem.IsPointerOverGameObject()` is false over labels → `MapCameraController.HandleScrollZoom` (guards on that) zooms normally over a label. `SetEditMode(true)` → raycastTarget true on all views → Task-5 click/drag/rename/delete/add work. `SetEditMode(false)` → `DeselectAll` tears down any open rename box, `addMode` cleared. Confirm the LOD/projection/display path (Task 4 of the original feature) is untouched — labels still render + fade in display mode. No `using System.Numerics` added; world-coord `System.Numerics.Vector2` in drag/add stays fully-qualified.

- [ ] **Step 3: USER checkpoint (Editor)** → in display mode (default): labels show, and scroll-zoom works with the cursor over a label. Toggle edit mode on (Task 5 wires the button): click-select/rename/drag/delete/"+ Название" work; toggle off: rename box closes, labels stop intercepting the cursor.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs
git commit -m "feat(region-labels): edit-mode toggle — labels are display-only (non-blocking) until editing is enabled"
```

---

### Task 5: MapLayersPanel — edit-mode toggle + density slider + wire refs

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapLayersPanel.cs`

**Interfaces:**
- Consumes: `RegionLabelOverlay.SetEditMode(bool)` (Task 4), `RegionLabelManager.SeedFromCells()`/`ToggleAddMode` route, `WorldMapRenderer.labelDensity` (Task 3).

- [ ] **Step 1: Add serialized refs**

Beside `public WorldMapRenderer mapRenderer;` (line 15) add (the file needs `using WorldGen.Rendering.RegionLabels;` — add if absent):
```csharp
        public RegionLabelOverlay regionLabelOverlay;
        public RegionLabelManager regionLabelManager;
```

- [ ] **Step 2: Add the "Редактировать названия" edit-mode toggle**

After the existing layer-toggle rows / near the "Названия регионов" visibility toggle added in the original feature, add an edit-mode toggle row (mirror `AddLayerToggleRow`; default OFF):
```csharp
AddLayerToggleRow(t, "Редактировать названия", false, on => regionLabelOverlay?.SetEditMode(on));
```

- [ ] **Step 3: Add the "Плотность названий" slider**

Mirror the existing slider construction in `EditorBrushPanel.cs` (grep it for `AddComponent<Slider>` / its slider-row helper — reuse the same GameObject/Image/handle/`Slider` setup and theming). Build a `0..1` slider seeded from `mapRenderer.labelDensity`, and on value change write it back:
```csharp
// pseudo-shape; match EditorBrushPanel's real slider builder signature:
BuildSliderRow(t, "Плотность названий", 0f, 1f, mapRenderer != null ? mapRenderer.labelDensity : 0.4f,
    v => { if (mapRenderer != null) mapRenderer.labelDensity = v; });
```
The slider only stores `labelDensity`; it does NOT live-reseed (that would discard edits). Re-seeding happens on the next generation or via "Пересоздать названия".

- [ ] **Step 4: Gate the add/regenerate controls to edit mode**

The "+ Название" and "Пересоздать названия" buttons (added in the original feature, in this panel) are editing actions. Make "+ Название" only meaningful in edit mode: its handler already routes to `regionLabelOverlay.ToggleAddMode()`, which Task 4 now no-ops outside edit mode — so no change is strictly required, but for clarity set both buttons' `interactable` to follow edit mode if the panel keeps button references (optional; if it adds complexity, leave them and rely on Task 4's guards). "Пересоздать названия" stays available (it reseeds via `regionLabelManager.SeedFromCells()`).

- [ ] **Step 5: Static hand-trace self-review**

Confirm: `AddLayerToggleRow` arity matches the sibling calls (`Transform, string, bool, Action<bool>`); the slider builder mirrors `EditorBrushPanel`'s real one (correct types); refs are serialized and typed `RegionLabelOverlay`/`RegionLabelManager` (namespace `using` present); no CS0104 (no world-coord `Vector2` here).

- [ ] **Step 6: USER checkpoint C (Editor + scene wiring)** → wire `MapLayersPanel.regionLabelOverlay`/`regionLabelManager` in the scene. Verify: edit-mode toggle flips labels between display-only (zoom works over labels) and editable; density slider changes label count after "Пересоздать названия" / regenerate; labels show unique Russian names on the right biomes; font (Forum) renders Cyrillic. Commit the scene + `.meta` after wiring.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Rendering/MapLayersPanel.cs
git commit -m "feat(region-labels): layers-panel edit-mode toggle + density slider"
```
(USER separately commits the scene + `.meta` after Editor wiring.)

---

## DM Editor step (not a code task): Forum font

Download **Forum** (OFL) from Google Fonts → `.ttf` into `Assets/Fonts/` → TMP Font Asset Creator with **Character Set = Unicode Range (Hex)** `20-7E,A0-FF,400-4FF`, Atlas 1024×1024, Render Mode SDFAA → Save the SDF `.asset` in `Assets/Fonts/` → assign it to `RegionLabelOverlay.labelFont`. Commit the font `.ttf`/`.asset` + `.meta`.

---

## Self-Review

**Spec coverage:** Russian descriptive unique names (T1 generator + T2 placer) ✓; isolated biome→noun table for future rework (T1 `Nouns` dict, Global Constraints) ✓; sparse + density slider (T2 threshold + T3 field + T5 slider) ✓; edit-mode toggle fixes cursor blocking (T4 + T5 toggle) ✓; on-land anchor (T2 `OnLandAnchor`) ✓; Forum/Cyrillic font (DM Editor step) ✓; grouping stays biome-family, persistence/LOD/save-load unchanged (no task touches them) ✓.

**Placeholder scan:** all code shown in full (the full `RegionLabelNames` incl. the 24-adjective pool + noun table; the full `Place` rewrite; the self-tests). The only "grep an existing pattern" pointers are the slider builder (T5, points at `EditorBrushPanel.cs`) and the container click-`Image` reference (T4, points at the current CRUD overlay) — both concrete existing code, per "follow established patterns."

**Type consistency:** `NameFor(BiomeFamily, int seed, int zoneKey, HashSet<int>) : string` identical at def (T1) and calls (T2). `Place(cells, nearest, mapW, mapH, int seed = 0, float labelDensity = 0.4f)` identical at def (T2) and calls (T3 manager, T2 self-test); both new params defaulted so the T2→T3 window compiles. `SetEditMode(bool)` identical at def (T4) and call (T5). `labelDensity` (float, serialized, default 0.4) consistent across T3 (def), T2 (consumer via manager), T5 (slider). `RegionLabelNames.Gender`/`Noun`/`Adjective` used only inside T1. World coords are `System.Numerics.Vector2` throughout the pure-C# files; no `UnityEngine.Vector2` collision.
