# Region Labels — LOD Tiers, Continents & Contrast Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 3-tier zoom LOD (close=nothing, mid=biome names, far=continent+sea names), invented continent names per landmass, overlap culling that hides (never drifts) crowded labels, and a soft TMP-underlay drop-shadow for contrast.

**Architecture:** `RegionLabelData` gains `Kind` (Biome/Continent/Sea) + `Priority`. `RegionLabelNames` gains an isolated syllable-based continent-name generator. `RegionLabelPlacer` emits continent labels (landmass BFS) and tags every label with Kind/Priority. `RegionLabelOverlay` drives per-kind alpha by zoom tier, culls overlapping lower-priority labels in-place, and renders text through a shared underlay material.

**Tech Stack:** Unity 6000.3.2f1, C#, TextMeshPro, `System.Numerics.Vector2` for map coords, Newtonsoft (auto-serializes the additive fields).

## Global Constraints

- **Unity, no CLI test runner:** implementers write code + a static hand-trace self-review + commit (report DONE_WITH_CONCERNS — cannot compile). `[ContextMenu]` self-tests + visual checks are the DM's Editor step. Do NOT claim tests were run.
- **Namespace** `WorldGen.Rendering.RegionLabels`. `BiomeFamily`/`RegionCategories`/`NearestCellLookup` are in `WorldGen.Rendering.MapRaster` (already imported by the pure-C# files).
- **`System.Numerics.Vector2`** for all world coords, fully-qualified. Pure-C# files (`RegionLabelData`, `RegionLabelNames`, `RegionLabelPlacer`) have **no `using UnityEngine`** — keep it that way (no CS0104). `RegionLabelOverlay` uses `UnityEngine.Vector2` for UI; never leave a bare `Vector2` for a world coord there.
- **Deterministic, no `Random`.** Continent names use the existing FNV `Hash` and the same `seed` argument (so the reroll salt varies them too).
- **Additive persistence:** `Kind`/`Priority` auto-serialize via Newtonsoft (like the existing `SeedFamily` enum). NO `ProjectSerializer` change. Old `.dndproj` → `Kind=Biome(0)`, `Priority=0`.
- **LOD ratio** `r = orthoSize / NaturalFitSize` ranges `[0.08, 3.0]`; fit-to-screen = 1.0. Defaults keep biomes visible at fit-to-screen and swap to continents only when zoomed OUT past it.

---

### Task 1: RegionLabelData — LabelKind + Kind/Priority fields

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelData.cs`

**Interfaces:**
- Produces: `enum LabelKind { Biome = 0, Continent = 1, Sea = 2 }` and `RegionLabelData.Kind` (LabelKind), `RegionLabelData.Priority` (float).

- [ ] **Step 1: Add the enum + two fields**

The current class is:
```csharp
[Serializable]
public class RegionLabelData
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Text;
    public System.Numerics.Vector2 WorldPosition;
    public BiomeFamily SeedFamily;
}
```
Add the enum (in the same namespace, above or below the class) and two fields:
```csharp
    /// <summary>Which LOD tier a label belongs to. Biome = mid-zoom biome zone; Continent = far-zoom
    /// landmass name; Sea = far-zoom ocean name. Defaults to Biome so legacy saves load unchanged.</summary>
    public enum LabelKind { Biome = 0, Continent = 1, Sea = 2 }
```
and inside `RegionLabelData`:
```csharp
    public LabelKind Kind;   // default Biome (0)
    public float Priority;   // higher wins overlap culling (biome = zone cell count; continent/sea = large)
```
Keep everything else. (Newtonsoft serializes both automatically; old saves lack them → defaults.)

- [ ] **Step 2: Static self-review** — enum is in the `WorldGen.Rendering.RegionLabels` namespace; both fields public; no `using UnityEngine` added; `[Serializable]` still applies.

- [ ] **Step 3: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelData.cs
git commit -m "feat(region-labels): add LabelKind + Kind/Priority to RegionLabelData (additive)"
```

---

### Task 2: RegionLabelNames — invented continent-name generator

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelNames.cs`
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs`

**Interfaces:**
- Consumes: the existing private `Hash(int seed, int zoneKey)`.
- Produces: `RegionLabelNames.ContinentName(int seed, int key) : string`.

- [ ] **Step 1: Add the continent syllable pools + generator**

In `RegionLabelNames` add (isolated from the biome noun table — unrelated to the planned biome rework):
```csharp
        static readonly string[] ContinentOnsets =
        { "Вэл","Каэр","Тарн","Морн","Драг","Эль","Вор","Нар","Ске","Тир","Улл","Фэн","Гэл","Хад","Рун","Аск" };
        static readonly string[] ContinentCodas =
        { "дрим","вейл","морн","гард","холд","рун","тар","нор","вен","дал","мир","рат","гейт","ланд" };

        /// <summary>Deterministic invented (fantasy proper-noun) landmass name, e.g. "Вэлдрим", "Каэрхолд".
        /// Two decorrelated draws from the same FNV hash so onset and coda vary independently.</summary>
        public static string ContinentName(int seed, int key)
        {
            int a = (int)((uint)Hash(seed, key) % (uint)ContinentOnsets.Length);
            int b = (int)((uint)Hash(seed, unchecked(key * 31 + 0x2545F491)) % (uint)ContinentCodas.Length);
            return ContinentOnsets[a] + ContinentCodas[b];
        }
```

- [ ] **Step 2: Add a continent-name self-test to `RegionLabelSelfTests.cs`**

Add a `[ContextMenu]`:
```csharp
[ContextMenu("Self-Test: Continent Names")]
public void SelfTestContinentNames()
{
    string a1 = RegionLabelNames.ContinentName(1, 5);
    string a2 = RegionLabelNames.ContinentName(1, 5);
    bool ok = !string.IsNullOrEmpty(a1) && a1 == a2;          // deterministic
    ok &= RegionLabelNames.ContinentName(2, 5) != null;        // different seed still produces a name
    // reroll (seed changes via salt) generally yields a different name for the same landmass:
    ok &= RegionLabelNames.ContinentName(1, 5) != RegionLabelNames.ContinentName(9, 5)
       || RegionLabelNames.ContinentName(1, 5) != RegionLabelNames.ContinentName(17, 5); // at least one of two other seeds differs
    Debug.Log(ok ? "Self-Test Continent Names: PASS" : "Self-Test Continent Names: FAIL");
}
```

- [ ] **Step 3: DM runs the self-test** → "Self-Test: Continent Names" → PASS. (Batched with Task 3's checkpoint.)

- [ ] **Step 4: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelNames.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs
git commit -m "feat(region-labels): invented syllable-based continent-name generator + self-test"
```

---

### Task 3: RegionLabelPlacer — continent labels + Kind/Priority on every label

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs`
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs`

**Interfaces:**
- Consumes: `RegionLabelNames.ContinentName` (Task 2), `RegionLabelData.Kind`/`Priority` (Task 1), `RegionCategories.IsLandCell`.
- Produces: `Place` now returns labels tagged with `Kind` + `Priority`, including one `Continent` label per large landmass.

- [ ] **Step 1: Tag biome + sea labels, then add a continent BFS pass**

1. Add a constant near the density constants:
```csharp
        const int ContinentMinCells = 40;          // landmass must be at least this big to be named
        const float ContinentPriorityBias = 1_000_000f; // continents/seas outrank biomes in overlap culling
```
2. Where each **biome** label is created in `Place`, set its kind/priority:
```csharp
        result.Add(new RegionLabelData
        {
            Text = name,
            WorldPosition = OnLandAnchor(comp),
            SeedFamily = family,
            Kind = RegionLabelData.LabelKind.Biome,
            Priority = comp.Count,
        });
```
3. In `AddSeaLabels`, set the sea label's kind/priority:
```csharp
        result.Add(new RegionLabelData
        {
            Text = name, WorldPosition = pos, SeedFamily = BiomeFamily.Sea,
            Kind = RegionLabelData.LabelKind.Sea, Priority = ContinentPriorityBias,
        });
```
4. Add a `AddContinentLabels` pass, called from `Place` right before `AddSeaLabels(...)`:
```csharp
        static void AddContinentLabels(List<RegionLabelData> result, IReadOnlyList<VoronoiCell> cells,
            Dictionary<int, VoronoiCell> byId, int seed)
        {
            var visited = new HashSet<int>();
            foreach (var start in cells)
            {
                if (visited.Contains(start.Id)) continue;
                if (!RegionCategories.IsLandCell(start)) { visited.Add(start.Id); continue; }

                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                int landKey = start.Id;
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    if (c.Id < landKey) landKey = c.Id;
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        if (!RegionCategories.IsLandCell(nc)) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }
                if (comp.Count < ContinentMinCells) continue;
                result.Add(new RegionLabelData
                {
                    Text = RegionLabelNames.ContinentName(seed, landKey),
                    WorldPosition = OnLandAnchor(comp),
                    SeedFamily = BiomeFamily.Coast,   // "not a biome zone" sentinel; unused for continents
                    Kind = RegionLabelData.LabelKind.Continent,
                    Priority = ContinentPriorityBias + comp.Count,
                });
            }
        }
```
   Call it in `Place` (you already build `byId`): `AddContinentLabels(result, cells, byId, seed);` then `AddSeaLabels(result, nearest, mapWidth, mapHeight, seed);`.
   (The land-BFS here is biome-agnostic — it groups ALL land, unlike the biome-family BFS above.)

- [ ] **Step 2: Static self-review** — continent BFS uses `IsLandCell` (not family); `OnLandAnchor` keeps the anchor on land; continent `Priority` > any biome `Priority` (bias 1e6 ≫ cell counts); biome/sea labels now carry Kind/Priority; still no `using UnityEngine`; `System.Numerics.Vector2` fully-qualified.

- [ ] **Step 3: Extend `SelfTestPlacer`** — after the existing biome assertions add:
```csharp
// The all-land fixture forms one landmass >= ContinentMinCells? The 14 fixture cells are land, so
// with ContinentMinCells=40 NO continent is emitted at fixture scale — assert none, then assert a
// low-threshold continent appears by not gating on size in a second call is out of scope; instead
// just assert the KIND tagging on the biome labels we already have:
var forestLbl = labels.Find(l => l.Text.EndsWith(" Лес"));
ok &= forestLbl != null && forestLbl.Kind == RegionLabelData.LabelKind.Biome && forestLbl.Priority > 0f;
ok &= labels.TrueForAll(l => l.Kind != RegionLabelData.LabelKind.Continent); // 14-cell island < 40 → no continent
```
   (The continent path is exercised visually at the DM checkpoint on a real map; the fixture is too small to cross `ContinentMinCells`, so the self-test asserts biome Kind/Priority tagging + that no continent is spuriously emitted below threshold.)

- [ ] **Step 4: DM runs the self-tests** → "Region Label Placer", "Continent Names", "Region Label Names" → PASS.

- [ ] **Step 5: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelPlacer.cs Assets/WorldGen/Rendering/RegionLabels/RegionLabelSelfTests.cs
git commit -m "feat(region-labels): emit continent labels (landmass BFS) + tag Kind/Priority on all labels"
```

---

### Task 4: RegionLabelOverlay — 3-tier LOD + overlap culling + underlay shadow

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs`

**Interfaces:**
- Consumes: `RegionLabelData.Kind`/`Priority` (Task 1); `MapCameraController.targetCamera`/`NaturalFitSize`.

- [ ] **Step 1: Replace the LOD fracs + add macro fracs**

Change the serialized LOD block:
```csharp
        [Header("LOD (доли от NaturalFitSize)")]
        [Range(0f,1f)] public float nearFrac = 0.35f;   // ниже — приближено, всё скрыто
        [Range(0f,1.5f)] public float farFrac = 0.6f;   // биомы полностью видны от этого
        [Range(0.5f,3f)] public float macroLoFrac = 1.3f; // отсюда биомы гаснут, материк/моря появляются
        [Range(0.5f,3f)] public float macroHiFrac = 1.8f; // выше — только материк/моря
```
(Replace the old `farFrac = 0.8f` and remove the old single-band assumption.)

- [ ] **Step 2: Per-kind alpha helpers (replace `LodAlpha`)**
```csharp
        // Biome labels: visible in the MID band (fade in from nearFrac, fade out into the macro band).
        float BiomeAlpha(float r)
        {
            float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(nearFrac, farFrac, r));
            float down = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(macroLoFrac, macroHiFrac, r));
            return up * (1f - down);
        }
        // Continents + seas: visible when zoomed OUT past the mid band.
        float MacroAlpha(float r) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(macroLoFrac, macroHiFrac, r));
```
Delete the old `LodAlpha` method.

- [ ] **Step 3: Rewrite `LateUpdate` — per-kind alpha + priority overlap cull (no drift)**

Add reusable buffers as fields:
```csharp
        readonly List<(LabelView lv, Vector2 pos, float a)> cullBuffer = new List<(LabelView, Vector2, float)>();
        readonly List<Rect> placedRects = new List<Rect>();
```
Replace the `LateUpdate` projection/collision body with:
```csharp
        void LateUpdate()
        {
            if (!visible || manager == null || cameraController == null) return;
            var cam = cameraController.targetCamera;
            float refSize = cameraController.NaturalFitSize;
            if (cam == null || refSize <= 0f) return;

            HandleMapClick();

            float r = cam.orthographicSize / refSize;
            float biomeA = BiomeAlpha(r);
            float macroA = MacroAlpha(r);

            cullBuffer.Clear();
            foreach (var d in manager.GetAll())
            {
                if (!views.TryGetValue(d.Id, out var lv) || lv == null || lv.Container == null || lv.Tmp == null) continue;
                float a = d.Kind == RegionLabelData.LabelKind.Biome ? biomeA : macroA;
                Vector3 world = new Vector3(d.WorldPosition.X, labelYOffsetWorld, d.WorldPosition.Y);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool onScreen = sp.z > 0f && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
                if (!onScreen || a <= 0.01f) { Park(lv); continue; }
                Vector2 pos = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);
                cullBuffer.Add((lv, pos, a));
            }

            // Priority overlap cull: the placer put higher Priority on continents/seas and bigger biome
            // zones. We need each buffer entry's priority — capture it alongside; higher wins, overlapping
            // lower ones are hidden. (Labels stay pinned to their anchor — no drift.)
            // Sort a parallel index list by the label's Priority (looked up via manager) descending.
            cullBuffer.Sort((x, y) => y.lv.Priority.CompareTo(x.lv.Priority));
            placedRects.Clear();
            foreach (var e in cullBuffer)
            {
                var rect = new Rect(e.pos.x - 110, e.pos.y - 17, 220, 34);
                bool blocked = false;
                for (int i = 0; i < placedRects.Count; i++) if (placedRects[i].Overlaps(rect)) { blocked = true; break; }
                if (blocked) { Park(e.lv); continue; }
                placedRects.Add(rect);
                SetAlpha(e.lv, e.a);
                e.lv.Container.anchoredPosition = e.pos;
            }

            UpdateEditUIPosition();
        }

        static void SetAlpha(LabelView lv, float a) { var c = lv.Tmp.color; c.a = a; lv.Tmp.color = c; }
        static void Park(LabelView lv) { SetAlpha(lv, 0f); lv.Container.anchoredPosition = new Vector2(-9999, -9999); }
```
   For the `Priority` sort to work, add a `public float Priority;` to the `LabelView` holder and set it in `CreateLabelView` from `d.Priority` (so the cull can read it without a manager lookup). Add the assignment `Priority = d.Priority` when constructing the `LabelView`.

- [ ] **Step 4: Underlay (soft shadow) material**

Add a shared material built once from the font, carrying the outline + underlay. Add a field + helper:
```csharp
        Material labelMat;
        Material EnsureLabelMaterial()
        {
            if (labelMat == null && labelFont != null)
            {
                labelMat = new Material(labelFont.material);
                labelMat.SetFloat("_OutlineWidth", 0.3f);
                labelMat.SetColor("_OutlineColor", new Color32(6, 9, 14, 255));
                labelMat.EnableKeyword("UNDERLAY_ON");
                labelMat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.7f));
                labelMat.SetFloat("_UnderlayOffsetX", 1f);
                labelMat.SetFloat("_UnderlayOffsetY", -1f);
                labelMat.SetFloat("_UnderlaySoftness", 0.35f);
                labelMat.SetFloat("_UnderlayDilate", 0.1f);
            }
            return labelMat;
        }
```
In `CreateLabelView`, after `if (labelFont != null) tmp.font = labelFont;` add:
```csharp
            var lm = EnsureLabelMaterial();
            if (lm != null) tmp.fontSharedMaterial = lm;   // shared → one material, no per-label instances
```
and REMOVE the per-tmp `tmp.outlineWidth = 0.3f;` / `tmp.outlineColor = ...;` lines (outline now lives on the shared material; setting them per-tmp would fork the material and drop the underlay). Keep `tmp.color`, `fontStyle`, `characterSpacing`, `fontSize`, `alignment`.
In `OnDestroy`, free it: `if (labelMat != null) Destroy(labelMat);` (project has a texture/material-leak history).

- [ ] **Step 5: DM checkpoint (Editor)** — zoom through the three tiers: close = no labels; mid/fit-to-screen = biome names, no overlap (crowded ones hidden, not moved, and stay on their biome); zoom out past fit = biome names give way to the continent name + sea names. Verify the underlay shadow lifts text off the terrain (tune `_Underlay*`/`baseFontSize` in the Inspector/material if needed). If the shadow doesn't show, confirm the font's material is a TMP SDF material (Font Asset Creator output) — underlay needs it.

- [ ] **Step 6: Commit**
```bash
git add Assets/WorldGen/Rendering/RegionLabels/RegionLabelOverlay.cs
git commit -m "feat(region-labels): 3-tier zoom LOD + priority overlap culling + underlay shadow"
```

---

## Self-Review

**Spec coverage:** Kind/Priority additive data (T1) ✓; invented continent generator (T2) ✓; continent labels + Kind/Priority tagging (T3) ✓; 3-tier LOD + overlap cull + underlay (T4) ✓; persistence unchanged (additive, no serializer edit) ✓; biome naming/density/edit-mode/reroll untouched ✓.

**Placeholder scan:** all code shown in full. The only lookup pointers are TMP material property names (`_UnderlayColor` etc.) — these are the documented TMP SDF material properties; the DM visually verifies + tunes them (spec §6 flags this as the one Editor-only part).

**Type consistency:** `LabelKind`/`Kind`/`Priority` defined in T1, consumed in T3 (set) and T4 (read). `ContinentName(int seed, int key)` def (T2) = call (T3). `LabelView.Priority` added in T4 mirrors `RegionLabelData.Priority`. LOD fracs (`nearFrac/farFrac/macroLoFrac/macroHiFrac`) defined + used in T4. `System.Numerics.Vector2` only in the pure-C# files; `UnityEngine.Vector2`/`Rect` only in the overlay.
