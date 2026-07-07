# Тёмный фэнтези-рендер карты — Подпроект 1: Растровая заливка и палитра — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `WorldMapRenderer`'s vertex-color fan-mesh rendering with a CPU-baked `Texture2D` raster on a single quad, adding a 4-theme dark-fantasy palette, smoothed biome blending, hillshade, coastline glow/outline, grain and vignette — while keeping brush/selection/POI hit-testing, save/load, and the existing Height/Region/Biome/Combined-hard display modes visually unchanged.

**Architecture:** A new `Assets/WorldGen/Rendering/MapRaster/` folder holds four pure/near-pure helper types (`Noise`, `NearestCellLookup`, `MapPalette`, `MapRasterizer`). `WorldMapRenderer` builds a 4-vertex quad instead of per-cell fans, bakes its color from these helpers into a `Texture2D` + a parallel `int[]` `cellId` buffer (for hit-testing), and exposes `RebakeAll()` (full rebake), `RebakeAffectedCells(...)` (brush dirty-rect), and `RebakeAllStepped(...)` (chunked coroutine for the generation-progress screen). Height/Region/Biome/Combined-without-smoothing keep going through the existing `GetColorForCell` unchanged (via a delegate), so their output is byte-for-byte the old vertex-color look, just sampled through a texture.

**Tech Stack:** Unity 2022.3 LTS (Built-in RP), C# (`System.Numerics.Vector2` for cell/site space, `UnityEngine` types for rendering), no new packages.

## Global Constraints

- Generation layer (`Assets/WorldGen/Generation/`) stays pure C#, no `UnityEngine` dependency — all new Unity-dependent code goes under `Assets/WorldGen/Rendering/`.
- New Input System only (not used in this plan directly, but don't reintroduce legacy `UnityEngine.Input`).
- Do not delete `Assets/WorldGen/Rendering/PolygonTriangulator.cs` or `Assets/WorldGen/Rendering/VertexColorUnlit.shader` — both become unused by the main render path but are kept (self-tests / `CellSelectionController`'s overlay mesh still use `PolygonTriangulator`; `VertexColorUnlit.shader` is kept for a possible future debug mode).
- Do not touch `Assets/WorldGen/Rendering/WorldMaterial.mat`. The new quad gets its material assigned entirely in code (`Shader.Find("Sprites/Default")`, matching this file's own existing pattern for rivers/borders — see rationale in Task 7) so the old `.mat` asset becomes inert without needing a hand-edited shader GUID.
- Self-tests follow this project's established convention: `[ContextMenu("Self-Test: ...")]` methods on `WorldMapRenderer` that build a tiny fixture and `Debug.Log` PASS/FAIL — even for static helper classes that aren't `MonoBehaviour`s (matches how `MapBorderBuilder`/`LakeRegionUnifier`/`BrushOps` are already tested from `WorldMapRenderer`/`BrushToolController`).
- Every new public entry point must not change the observable behavior of Height/Region/Biome/Combined-without-smoothing display modes, brush painting, cell selection, POI placement, or save/load — only Combined+smoothBorders (the new default) changes visually.

---

## File overview

New (`Assets/WorldGen/Rendering/MapRaster/`):
- `Noise.cs` — hash/valueNoise/fbm, ported 1:1 from the design handoff's JS reference.
- `NearestCellLookup.cs` — grid-bucket nearest/within-radius cell queries.
- `MapPalette.cs` — `MapPaletteTheme`, `BiomeFamily`, `PaletteSlot` enums + the 4-theme color table + `GetFamily`/`GetSlotColor`/`DisplayName`.
- `MapRasterizer.cs` — `MapRasterConfig`, `MapRasterBuffers`, `Bake`/`RebakeRegion`/`ReapplyDarkness`.

Modified:
- `Assets/WorldGen/Rendering/RegionColorPalette.cs` — new `HillshadeBrightness` overload exposing raw `ndotl`.
- `Assets/WorldGen/Rendering/WorldMapRenderer.cs` — quad mesh, raster fields, rebake plumbing, hit-testing rewrite, brush/override call-site swap, generation-progress split, self-tests (touched in every task below).
- `Assets/WorldGen/Rendering/BrushToolController.cs` — call `RebakeAffectedCells` once per stamp instead of relying on per-cell `RecolorOnly`.
- `Assets/WorldGen/Generation/WorldGenerator.cs` — `GenerateWorldStepped` fraction rescale (5→6 steps), drop its own "Готово" emit.
- `Assets/WorldGen/Rendering/GenerationProgressUI.cs` — add "Отрисовка карты" to the checklist.
- `Assets/WorldGen/Rendering/MapScreenController.cs` — drive the new stepped bake between generation and "Готово".

Not changed: `RegionColorPalette.GetHeightColor/GetBiomeColor/GetWaterColor/GetRegionColor/GetNeutralBaseColor`, `MapBorderBuilder.cs`, `PolygonTriangulator.cs`, `CellSelectionController.cs`, `PoiInteractionController.cs`, `CellOverrideService.cs`, `BiomeClassifier.cs`, `VoronoiCell.cs`, `ProjectMenuBar.cs`.

---

### Task 1: `Noise.cs` — deterministic hash/value-noise/fbm

**Files:**
- Create: `Assets/WorldGen/Rendering/MapRaster/Noise.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add one self-test method)

**Interfaces:**
- Produces: `WorldGen.Rendering.MapRaster.Noise.Hash(int ix, int iy, int s) -> float [0,1)`, `Noise.ValueNoise(float x, float y, int s) -> float [0,1)`, `Noise.Fbm(float x, float y, int s, int octaves) -> float [0,1)` — consumed by `MapRasterizer` in Task 6.

- [ ] **Step 1: Write `Noise.cs`**

```csharp
using UnityEngine;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Хэш/шум-функции, побитово портированные из design_handoff_realmweaver_map/Terra Umbrarum.dc.html
    /// (JS: hash/vn/fbm) - unchecked int32 math воспроизводит Math.imul/>>> ровно так же, как в JS,
    /// чтобы один seed давал одинаковый результат на любой платформе.
    /// </summary>
    public static class Noise
    {
        public static float Hash(int ix, int iy, int s)
        {
            unchecked
            {
                int h = ix * 374761393 + iy * 668265263 + s * 362437;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        static float SmoothStep(float t) => t * t * (3f - 2f * t);

        public static float ValueNoise(float x, float y, int s)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;

            float a = Hash(x0, y0, s);
            float b = Hash(x0 + 1, y0, s);
            float c = Hash(x0, y0 + 1, s);
            float d = Hash(x0 + 1, y0 + 1, s);

            float u = SmoothStep(fx), v = SmoothStep(fy);
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        }

        public static float Fbm(float x, float y, int s, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, s + i * 97);
                freq *= 2f;
                amp *= 0.5f;
            }
            return sum;
        }
    }
}
```

- [ ] **Step 2: Add self-test to `WorldMapRenderer.cs`**

Add this method next to the other `[ContextMenu("Self-Test: ...")]` methods (e.g. right after `SelfTestOceanConnectivity`, around line 661):

```csharp
        [ContextMenu("Self-Test: Noise Determinism And Range")]
        public void SelfTestNoise()
        {
            float h1 = WorldGen.Rendering.MapRaster.Noise.Hash(3, 7, 42);
            float h2 = WorldGen.Rendering.MapRaster.Noise.Hash(3, 7, 42);
            float h3 = WorldGen.Rendering.MapRaster.Noise.Hash(4, 7, 42);

            float v1 = WorldGen.Rendering.MapRaster.Noise.ValueNoise(1.3f, 2.7f, 5);
            float v2 = WorldGen.Rendering.MapRaster.Noise.ValueNoise(1.3f, 2.7f, 5);

            float f1 = WorldGen.Rendering.MapRaster.Noise.Fbm(0.5f, 0.5f, 9, 4);
            float f2 = WorldGen.Rendering.MapRaster.Noise.Fbm(0.5f, 0.5f, 9, 4);

            bool ok = h1 == h2 && h1 != h3 && h1 >= 0f && h1 < 1f
                      && v1 == v2 && v1 >= 0f && v1 < 1f
                      && f1 == f2 && f1 >= 0f && f1 <= 1f;

            Debug.Log(ok
                ? "Self-Test Noise Determinism And Range: PASS"
                : $"Self-Test Noise Determinism And Range: FAIL (h1={h1}, h3={h3}, v1={v1}, f1={f1})");
        }
```

- [ ] **Step 3: Verify compile**

Unity Editor is normally open interactively for this project (see `.superpowers/sdd/progress.md` history) — after saving both files, let the Editor recompile and confirm the Console shows no errors. If Unity isn't open, run a batchmode compile check:

```bash
"C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe" -batchmode -quit -projectPath "d:/D&D" -logFile -
```

(Use whichever Unity version this project is pinned to — check `ProjectSettings/ProjectVersion.txt` if unsure.) Expected: no `CS####` compile errors mentioning `Noise.cs` or `WorldMapRenderer.cs`.

- [ ] **Step 4: Run the self-test**

In the Editor, select the `WorldMapRenderer` GameObject, right-click its component header → "Self-Test: Noise Determinism And Range". Expected Console output: `Self-Test Noise Determinism And Range: PASS`.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/Noise.cs Assets/WorldGen/Rendering/MapRaster/Noise.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): add deterministic Noise (hash/valueNoise/fbm) port"
```

(The `.meta` file is generated by Unity on import — if it doesn't exist yet when you stage, let the Editor import the new script first, then `git add` again before committing.)

---

### Task 2: `NearestCellLookup.cs` — grid-bucket spatial index

**Files:**
- Create: `Assets/WorldGen/Rendering/MapRaster/NearestCellLookup.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add one self-test method)

**Interfaces:**
- Consumes: `VoronoiCell.Site` (`System.Numerics.Vector2`), `VoronoiCell.Id`.
- Produces: `NearestCellLookup(IEnumerable<VoronoiCell> cells, float bucketSize)`, `FindNearest(System.Numerics.Vector2 point) -> VoronoiCell`, `FindWithinRadius(System.Numerics.Vector2 point, float radius) -> IEnumerable<(VoronoiCell cell, float distance)>` — consumed by `MapRasterizer` (Task 5/6) and `WorldMapRenderer` (Task 7).

- [ ] **Step 1: Write `NearestCellLookup.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Grid-bucket поиск ближайшей клетки/клеток в радиусе по позиции VoronoiCell.Site.
    /// Размер бакета ~= minPointDistance (типичное расстояние между сайтами после Lloyd-релаксации),
    /// поэтому поиск амортизированно O(1) на пиксель при равномерном распределении точек.
    /// </summary>
    public class NearestCellLookup
    {
        readonly Dictionary<(int, int), List<VoronoiCell>> buckets = new Dictionary<(int, int), List<VoronoiCell>>();
        readonly float bucketSize;
        const int MaxRingSearch = 128;

        public NearestCellLookup(IEnumerable<VoronoiCell> cells, float bucketSize)
        {
            this.bucketSize = MathF.Max(bucketSize, 0.001f);
            foreach (var cell in cells)
            {
                var key = KeyOf(cell.Site.X, cell.Site.Y);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<VoronoiCell>();
                    buckets[key] = list;
                }
                list.Add(cell);
            }
        }

        (int, int) KeyOf(float x, float y) =>
            ((int)MathF.Floor(x / bucketSize), (int)MathF.Floor(y / bucketSize));

        /// <summary>Ближайшая клетка к точке. null только если в индексе нет вообще ни одной клетки.</summary>
        public VoronoiCell FindNearest(Vector2 point)
        {
            int bx = (int)MathF.Floor(point.X / bucketSize);
            int by = (int)MathF.Floor(point.Y / bucketSize);

            VoronoiCell best = null;
            float bestDistSq = float.MaxValue;

            for (int ring = 0; ring <= MaxRingSearch; ring++)
            {
                ScanRing(bx, by, ring, cell =>
                {
                    float dx = cell.Site.X - point.X, dy = cell.Site.Y - point.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq) { bestDistSq = distSq; best = cell; }
                });

                // Минимальное возможное расстояние до чего-либо в кольце (ring+1) - ring*bucketSize
                // (стандартный результат для grid-bucket поиска). Если текущий кандидат уже ближе -
                // расширять поиск дальше бессмысленно.
                if (best != null && MathF.Sqrt(bestDistSq) <= ring * bucketSize) break;
            }

            return best;
        }

        /// <summary>Все клетки в радиусе radius от точки, с их евклидовым расстоянием до неё.</summary>
        public IEnumerable<(VoronoiCell cell, float distance)> FindWithinRadius(Vector2 point, float radius)
        {
            int bx = (int)MathF.Floor(point.X / bucketSize);
            int by = (int)MathF.Floor(point.Y / bucketSize);
            int ringSpan = (int)MathF.Ceiling(radius / bucketSize) + 1;

            var results = new List<(VoronoiCell, float)>();
            for (int oy = -ringSpan; oy <= ringSpan; oy++)
            {
                for (int ox = -ringSpan; ox <= ringSpan; ox++)
                {
                    if (!buckets.TryGetValue((bx + ox, by + oy), out var list)) continue;
                    foreach (var cell in list)
                    {
                        float dx = cell.Site.X - point.X, dy = cell.Site.Y - point.Y;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist <= radius) results.Add((cell, dist));
                    }
                }
            }
            return results;
        }

        void ScanRing(int bx, int by, int ring, Action<VoronoiCell> visit)
        {
            if (ring == 0)
            {
                if (buckets.TryGetValue((bx, by), out var center))
                    foreach (var c in center) visit(c);
                return;
            }

            for (int dx = -ring; dx <= ring; dx++)
            {
                TryVisitBucket(bx + dx, by - ring, visit);
                TryVisitBucket(bx + dx, by + ring, visit);
            }
            for (int dy = -ring + 1; dy <= ring - 1; dy++)
            {
                TryVisitBucket(bx - ring, by + dy, visit);
                TryVisitBucket(bx + ring, by + dy, visit);
            }
        }

        void TryVisitBucket(int bx, int by, Action<VoronoiCell> visit)
        {
            if (!buckets.TryGetValue((bx, by), out var list)) return;
            foreach (var c in list) visit(c);
        }
    }
}
```

- [ ] **Step 2: Add self-test to `WorldMapRenderer.cs`**

Add after the `SelfTestNoise` method from Task 1:

```csharp
        [ContextMenu("Self-Test: Nearest Cell Lookup")]
        public void SelfTestNearestCellLookup()
        {
            var fixtureCells = new List<VoronoiCell>
            {
                new VoronoiCell(0, new System.Numerics.Vector2(0f, 0f)),
                new VoronoiCell(1, new System.Numerics.Vector2(20f, 0f)),
                new VoronoiCell(2, new System.Numerics.Vector2(0f, 20f)),
                new VoronoiCell(3, new System.Numerics.Vector2(20f, 20f)),
                new VoronoiCell(4, new System.Numerics.Vector2(10f, 10f)),
            };
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 10f);

            bool ok = true;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(1f, 1f))?.Id == 0;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(19f, 1f))?.Id == 1;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(1f, 19f))?.Id == 2;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(19f, 19f))?.Id == 3;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(10f, 10f))?.Id == 4;

            // Точка (10,0) равноудалена (dist=10) от клеток 0, 1 и 4 - проверяем только, что
            // возвращается ОДИН ИЗ валидных кандидатов, а не null и не клетка 2/3 (те дальше).
            var boundary = lookup.FindNearest(new System.Numerics.Vector2(10f, 0f));
            ok &= boundary != null && (boundary.Id == 0 || boundary.Id == 1 || boundary.Id == 4);

            Debug.Log(ok ? "Self-Test Nearest Cell Lookup: PASS" : "Self-Test Nearest Cell Lookup: FAIL");
        }
```

- [ ] **Step 3: Verify compile and run self-test**

Same as Task 1 Steps 3-4, this time checking for `Self-Test Nearest Cell Lookup: PASS`.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/NearestCellLookup.cs Assets/WorldGen/Rendering/MapRaster/NearestCellLookup.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): add grid-bucket NearestCellLookup"
```

---

### Task 3: `MapPalette.cs` — theme tokens and biome-family mapping

**Files:**
- Create: `Assets/WorldGen/Rendering/MapRaster/MapPalette.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add one self-test method)

**Interfaces:**
- Produces: `MapPaletteTheme` enum, `BiomeFamily` enum, `PaletteSlot` enum, `MapPalette.GetFamily(Biome) -> BiomeFamily`, `MapPalette.GetSlotColor(MapPaletteTheme, PaletteSlot) -> Color32`, `MapPalette.GetSlotColor(MapPaletteTheme, BiomeFamily) -> Color32` (land families only — throws for `Sea`/`Lake`), `MapPalette.DisplayName(MapPaletteTheme) -> string`. Consumed by `MapRasterizer` (Task 5/6) and (later, subproject 6) a settings UI.

- [ ] **Step 1: Write `MapPalette.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    public enum MapPaletteTheme { ColdTwilight, MoonlitSteel, EmeraldAbyss, AmethystNight }

    /// <summary>Визуальное семейство биома - слот палитры. Богатая 16-значная Whittaker-таблица
    /// (BiomeClassifier) не переклассифицируется, а мапится на одно из этих семейств для окраски.</summary>
    public enum BiomeFamily { Sea, Lake, Coast, Snow, Tundra, Highland, Badlands, Forest, ForestWarm, Moor, Plains }

    public enum PaletteSlot
    {
        Abyss, Sea, Shallow, Glow, Coast, Marsh, Plains, Moor, Forest, ForestWarm,
        Badlands, Tundra, Highland, Peak, Snow, LakeD, LakeS, Outline, MtnL, MtnS,
        Light, Road, Accent, AccentCold, FogA, FogB, TintCool, TintWarm
    }

    /// <summary>
    /// 4 палитры тёмного фэнтези-рендера (см. docs/superpowers/specs/2026-07-07-map-terrain-raster-design.md).
    /// Значения token'ов взяты из design_handoff_realmweaver_map/Terra Umbrarum.dc.html.
    /// </summary>
    public static class MapPalette
    {
        // Порядок в каждом массиве: ColdTwilight, MoonlitSteel, EmeraldAbyss, AmethystNight.
        static readonly Dictionary<PaletteSlot, Color32[]> table = new Dictionary<PaletteSlot, Color32[]>
        {
            [PaletteSlot.Abyss] = new[] { new Color32(6, 15, 24, 255), new Color32(8, 14, 30, 255), new Color32(4, 20, 20, 255), new Color32(14, 10, 30, 255) },
            [PaletteSlot.Sea] = new[] { new Color32(11, 30, 44, 255), new Color32(16, 32, 62, 255), new Color32(8, 40, 44, 255), new Color32(26, 22, 54, 255) },
            [PaletteSlot.Shallow] = new[] { new Color32(30, 84, 100, 255), new Color32(46, 96, 150, 255), new Color32(30, 102, 98, 255), new Color32(74, 66, 132, 255) },
            [PaletteSlot.Glow] = new[] { new Color32(120, 200, 214, 255), new Color32(140, 196, 244, 255), new Color32(120, 224, 204, 255), new Color32(168, 150, 244, 255) },
            [PaletteSlot.Coast] = new[] { new Color32(92, 86, 64, 255), new Color32(84, 88, 96, 255), new Color32(86, 92, 58, 255), new Color32(92, 80, 86, 255) },
            [PaletteSlot.Marsh] = new[] { new Color32(36, 58, 50, 255), new Color32(38, 54, 64, 255), new Color32(26, 62, 50, 255), new Color32(48, 44, 72, 255) },
            [PaletteSlot.Plains] = new[] { new Color32(74, 86, 58, 255), new Color32(70, 84, 98, 255), new Color32(86, 102, 54, 255), new Color32(96, 86, 96, 255) },
            [PaletteSlot.Moor] = new[] { new Color32(64, 66, 74, 255), new Color32(70, 74, 90, 255), new Color32(60, 78, 72, 255), new Color32(78, 72, 92, 255) },
            [PaletteSlot.Forest] = new[] { new Color32(24, 58, 46, 255), new Color32(28, 56, 72, 255), new Color32(18, 64, 48, 255), new Color32(42, 48, 80, 255) },
            [PaletteSlot.ForestWarm] = new[] { new Color32(150, 96, 44, 255), new Color32(168, 110, 60, 255), new Color32(176, 116, 44, 255), new Color32(168, 96, 96, 255) },
            [PaletteSlot.Badlands] = new[] { new Color32(128, 84, 54, 255), new Color32(140, 96, 66, 255), new Color32(150, 102, 48, 255), new Color32(150, 90, 84, 255) },
            [PaletteSlot.Tundra] = new[] { new Color32(120, 132, 140, 255), new Color32(150, 168, 190, 255), new Color32(126, 156, 146, 255), new Color32(150, 144, 176, 255) },
            [PaletteSlot.Highland] = new[] { new Color32(74, 80, 88, 255), new Color32(70, 84, 104, 255), new Color32(56, 86, 80, 255), new Color32(76, 70, 98, 255) },
            [PaletteSlot.Peak] = new[] { new Color32(110, 116, 128, 255), new Color32(112, 126, 150, 255), new Color32(92, 120, 116, 255), new Color32(116, 108, 142, 255) },
            [PaletteSlot.Snow] = new[] { new Color32(214, 224, 232, 255), new Color32(224, 234, 248, 255), new Color32(210, 230, 220, 255), new Color32(228, 222, 244, 255) },
            [PaletteSlot.LakeD] = new[] { new Color32(16, 44, 58, 255), new Color32(20, 42, 74, 255), new Color32(12, 50, 52, 255), new Color32(30, 26, 66, 255) },
            [PaletteSlot.LakeS] = new[] { new Color32(46, 110, 126, 255), new Color32(60, 116, 164, 255), new Color32(40, 116, 110, 255), new Color32(80, 72, 148, 255) },
            [PaletteSlot.Outline] = new[] { new Color32(6, 10, 16, 255), new Color32(8, 12, 22, 255), new Color32(4, 14, 14, 255), new Color32(12, 10, 22, 255) },
            [PaletteSlot.MtnL] = new[] { new Color32(140, 150, 164, 255), new Color32(152, 168, 198, 255), new Color32(122, 158, 150, 255), new Color32(146, 138, 172, 255) },
            [PaletteSlot.MtnS] = new[] { new Color32(40, 46, 56, 255), new Color32(42, 50, 70, 255), new Color32(30, 48, 46, 255), new Color32(48, 44, 68, 255) },
            [PaletteSlot.Light] = new[] { new Color32(100, 150, 190, 255), new Color32(140, 180, 235, 255), new Color32(92, 190, 168, 255), new Color32(150, 132, 232, 255) },
            [PaletteSlot.Road] = new[] { new Color32(176, 150, 96, 255), new Color32(168, 158, 128, 255), new Color32(172, 158, 96, 255), new Color32(178, 150, 120, 255) },
            [PaletteSlot.Accent] = new[] { new Color32(230, 178, 92, 255), new Color32(240, 185, 106, 255), new Color32(240, 191, 90, 255), new Color32(240, 173, 84, 255) },
            [PaletteSlot.AccentCold] = new[] { new Color32(143, 216, 230, 255), new Color32(169, 204, 255, 255), new Color32(127, 232, 204, 255), new Color32(195, 172, 255, 255) },
            [PaletteSlot.FogA] = new[] { new Color32(16, 24, 34, 255), new Color32(20, 28, 48, 255), new Color32(10, 30, 30, 255), new Color32(26, 22, 46, 255) },
            [PaletteSlot.FogB] = new[] { new Color32(34, 52, 66, 255), new Color32(44, 60, 90, 255), new Color32(26, 60, 58, 255), new Color32(50, 44, 80, 255) },
            [PaletteSlot.TintCool] = new[] { new Color32(32, 86, 116, 255), new Color32(58, 96, 162, 255), new Color32(26, 116, 104, 255), new Color32(74, 70, 152, 255) },
            [PaletteSlot.TintWarm] = new[] { new Color32(150, 102, 46, 255), new Color32(110, 96, 78, 255), new Color32(108, 104, 54, 255), new Color32(126, 88, 96, 255) },
        };

        public static Color32 GetSlotColor(MapPaletteTheme theme, PaletteSlot slot) => table[slot][(int)theme];

        /// <summary>Плоский базовый цвет для ЛЕНДовых семейств (Sea/Lake не имеют единого слота -
        /// их цвет зависит от глубины воды, см. MapRasterizer.ColorForWaterPixel).</summary>
        public static Color32 GetSlotColor(MapPaletteTheme theme, BiomeFamily family) => GetSlotColor(theme, FamilyToSlot(family));

        static PaletteSlot FamilyToSlot(BiomeFamily family) => family switch
        {
            BiomeFamily.Coast => PaletteSlot.Coast,
            BiomeFamily.Snow => PaletteSlot.Snow,
            BiomeFamily.Tundra => PaletteSlot.Tundra,
            BiomeFamily.Highland => PaletteSlot.Highland,
            BiomeFamily.Badlands => PaletteSlot.Badlands,
            BiomeFamily.Forest => PaletteSlot.Forest,
            BiomeFamily.ForestWarm => PaletteSlot.ForestWarm,
            BiomeFamily.Moor => PaletteSlot.Moor,
            BiomeFamily.Plains => PaletteSlot.Plains,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family,
                "Sea/Lake не имеют плоского слота - глубина воды сэмплируется отдельно."),
        };

        /// <summary>Маппинг богатой Whittaker-таблицы (BiomeClassifier, 16 значений) на визуальное
        /// семейство палитры. Явный switch с default-throw - защита от добавления нового Biome
        /// без записи здесь (см. Self-Test: Biome Family Coverage).</summary>
        public static BiomeFamily GetFamily(Biome biome) => biome switch
        {
            Biome.Ocean => BiomeFamily.Sea,
            Biome.Lake => BiomeFamily.Lake,
            Biome.Beach => BiomeFamily.Coast,
            Biome.Snow => BiomeFamily.Snow,
            Biome.Tundra => BiomeFamily.Tundra,
            Biome.Bare => BiomeFamily.Highland,
            Biome.Scorched => BiomeFamily.Badlands,
            Biome.Taiga => BiomeFamily.Forest,
            Biome.Shrubland => BiomeFamily.Moor,
            Biome.TemperateDesert => BiomeFamily.Badlands,
            Biome.TemperateRainForest => BiomeFamily.Forest,
            Biome.TemperateDeciduousForest => BiomeFamily.Forest,
            Biome.Grassland => BiomeFamily.Plains,
            Biome.TropicalRainForest => BiomeFamily.ForestWarm,
            Biome.TropicalSeasonalForest => BiomeFamily.ForestWarm,
            Biome.SubtropicalDesert => BiomeFamily.Badlands,
            _ => throw new ArgumentOutOfRangeException(nameof(biome), biome, "Новый Biome без записи в таблице BiomeFamily"),
        };

        public static string DisplayName(MapPaletteTheme theme) => theme switch
        {
            MapPaletteTheme.ColdTwilight => "Холодный сумрак",
            MapPaletteTheme.MoonlitSteel => "Лунная сталь",
            MapPaletteTheme.EmeraldAbyss => "Изумрудная бездна",
            MapPaletteTheme.AmethystNight => "Аметистовая ночь",
            _ => theme.ToString(),
        };
    }
}
```

- [ ] **Step 2: Add self-test to `WorldMapRenderer.cs`**

Add after `SelfTestNearestCellLookup`:

```csharp
        [ContextMenu("Self-Test: Biome Family Coverage")]
        public void SelfTestBiomeFamilyCoverage()
        {
            bool ok = true;
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                try
                {
                    WorldGen.Rendering.MapRaster.MapPalette.GetFamily(biome);
                }
                catch (System.Exception)
                {
                    ok = false;
                    Debug.LogWarning($"MapPalette.GetFamily не обрабатывает Biome.{biome}");
                }
            }
            Debug.Log(ok ? "Self-Test Biome Family Coverage: PASS" : "Self-Test Biome Family Coverage: FAIL");
        }
```

- [ ] **Step 3: Verify compile and run self-test**

Same as before, checking for `Self-Test Biome Family Coverage: PASS`.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapPalette.cs Assets/WorldGen/Rendering/MapRaster/MapPalette.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): add MapPalette (4 dark-fantasy themes + biome-family mapping)"
```

---

### Task 4: Expose raw `ndotl` from `RegionColorPalette.HillshadeBrightness`

**Files:**
- Modify: `Assets/WorldGen/Rendering/RegionColorPalette.cs:115-125`

**Interfaces:**
- Produces: `RegionColorPalette.HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient, out float ndotl) -> float` — consumed by `MapRasterizer` (Task 6) for the cold-moonlight highlight.
- Existing 5-arg overload keeps its exact signature/behavior (delegates to the new one), so `WorldMapRenderer.GetColorForCell` and `SelfTestHillshade` are untouched.

- [ ] **Step 1: Replace the method**

In `Assets/WorldGen/Rendering/RegionColorPalette.cs`, replace:

```csharp
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

with:

```csharp
        /// <summary>Яркость рельефного затенения [ambient..1] из градиента высоты клетки.
        /// Псевдонормаль строится из градиента (Y - вверх), освещается направленным светом
        /// под азимутом lightAzimuthDeg и фиксированным углом возвышения 45°.</summary>
        public static float HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient)
            => HillshadeBrightness(gradX, gradY, strength, lightAzimuthDeg, ambient, out _);

        /// <summary>Тот же расчёт, но дополнительно возвращает сырой N·L (до Lerp с ambient) -
        /// нужен MapRasterizer для "холодного лунного" подсвета освещённых склонов поверх обычного
        /// hillshade (см. design doc, шаг 6).</summary>
        public static float HillshadeBrightness(float gradX, float gradY, float strength, float lightAzimuthDeg, float ambient, out float ndotl)
        {
            var normal = new Vector3(-gradX * strength, 1f, -gradY * strength).normalized;
            float az = lightAzimuthDeg * Mathf.Deg2Rad;
            var lightDir = new Vector3(Mathf.Sin(az), 1f, Mathf.Cos(az)).normalized;
            ndotl = Mathf.Clamp01(Vector3.Dot(normal, lightDir));
            return Mathf.Lerp(ambient, 1f, ndotl);
        }
```

- [ ] **Step 2: Verify compile and existing self-test**

Run `SelfTestHillshade` (already exists on `WorldMapRenderer`, unaffected by this change) — expected: still `Self-Test Hillshade: PASS`.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/RegionColorPalette.cs
git commit -m "feat(map-raster): expose raw ndotl from HillshadeBrightness"
```

---

### Task 5: `MapRasterizer.cs` core — buffers, hard-mode sampling, vignette

**Files:**
- Create: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add one self-test method)

**Interfaces:**
- Consumes: `NearestCellLookup` (Task 2), `MapPalette` (Task 3), `VoronoiCell.EffectiveElevation/EffectiveIsOcean/EffectiveIsLake/Biome`, `MapDisplayMode` (existing enum in `WorldMapRenderer.cs`).
- Produces: `MapRasterConfig` (config class), `MapRasterBuffers` (persisted per-pixel buffers class), `MapRasterizer.CreateEmptyBuffers(w,h)`, `MapRasterizer.Bake(cells, cellById, lookup, displayMode, config, out buffers) -> Texture2D`, `MapRasterizer.RebakeRegion(cells, cellById, lookup, displayMode, config, texture, buffers, rectX, rectY, rectW, rectH)`, `MapRasterizer.ReapplyDarkness(texture, buffers, darkness)`. This task implements only the **hard-mode branch** (`!smoothBorders || displayMode != Combined`) plus the vignette pass; Task 6 adds the painted/smooth branch on top of the same `RebakeRegion` loop.

- [ ] **Step 1: Write `MapRasterizer.cs` (hard-mode + vignette only for now)**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Параметры одного запекания - неизменны между Bake/RebakeRegion для одной и той же
    /// карты, кроме смены палитры/твиков (подпроект 6 добавит UI, поля уже существуют).</summary>
    public class MapRasterConfig
    {
        public int TexWidth;
        public int TexHeight;
        public float MapWidth;
        public float MapHeight;
        public int Seed;
        public MapPaletteTheme Theme = MapPaletteTheme.ColdTwilight;
        public float ColdLight = 58f;
        public float RegionVariation = 45f;
        public float Darkness = 72f;
        public bool SmoothBorders = true;
        public float SmoothRadius = 1f;
        public float ReliefStrength = 3f;
        public float ReliefLightAzimuth = 315f;
        public float ReliefAmbient = 0.5f;

        /// <summary>Цвет клетки для Height/Region/Biome и Combined-без-сглаживания - привязан к
        /// WorldMapRenderer.GetColorForCell конкретного экземпляра, чтобы не дублировать эту логику.</summary>
        public Func<VoronoiCell, Color> HardModeColor;

        /// <summary>[0,1] "глубина" водной клетки - привязан к WorldMapRenderer.GetWaterDepth01.</summary>
        public Func<VoronoiCell, float> WaterDepth01;
    }

    /// <summary>Все per-pixel буферы одного запекания - хранятся на WorldMapRenderer между вызовами,
    /// т.к. RebakeRegion (кисть) трогает только часть текстуры за раз и должно читать соседние,
    /// ранее запечённые пиксели без их пересчёта.</summary>
    public class MapRasterBuffers
    {
        public int Width, Height;
        public int[] CellId;
        public float[] Elevation;
        public float[] Temperature;
        public Color32[] FamilyColor;
        public Color32[] PreVignette;
    }

    /// <summary>
    /// Запекает клетки Вороного в Texture2D + параллельный cellId-буфер для хит-тестинга.
    /// Height/Region/Biome и Combined-без-сглаживания используют "hard" сэмплинг (ближайшая клетка,
    /// без блендинга, через HardModeColor - визуально идентично старому vertex-color рендеру).
    /// Combined+smoothBorders включает полный "нарисованный" конвейер (см. Task 6).
    /// </summary>
    public static class MapRasterizer
    {
        public static MapRasterBuffers CreateEmptyBuffers(int width, int height)
        {
            int n = width * height;
            return new MapRasterBuffers
            {
                Width = width,
                Height = height,
                CellId = new int[n],
                Elevation = new float[n],
                Temperature = new float[n],
                FamilyColor = new Color32[n],
                PreVignette = new Color32[n],
            };
        }

        /// <summary>Удобная обёртка: полный запек всего изображения "с нуля" в новую текстуру.</summary>
        public static Texture2D Bake(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            out MapRasterBuffers buffers)
        {
            var texture = new Texture2D(config.TexWidth, config.TexHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            buffers = CreateEmptyBuffers(config.TexWidth, config.TexHeight);
            RebakeRegion(cells, cellById, lookup, displayMode, config, texture, buffers, 0, 0, config.TexWidth, config.TexHeight);
            return texture;
        }

        /// <summary>Перезапекает прямоугольную под-область текстуры/буферов на месте. rectX/Y/W/H уже
        /// в пиксельных координатах и уже включают отступ под smoothRadius - эта функция не добавляет
        /// собственный отступ (см. WorldMapRenderer.ComputeTouchedPixelRect в Task 7/8).</summary>
        public static void RebakeRegion(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

            // Проход 1: ближайшая клетка на пиксель (cellId-буфер) - нужен всегда.
            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    var point = PixelToSite(x, y, w, h, config.MapWidth, config.MapHeight);
                    var nearest = lookup.FindNearest(point);
                    buffers.CellId[y * w + x] = nearest.Id;
                }
            }

            if (painted)
            {
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }

            // Проход финальной раскраски (до виньетки - кэшируется в PreVignette).
            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    buffers.PreVignette[idx] = painted
                        ? BakePaintedPixel(cell, buffers, cellById, idx, x, y, w, h, config)
                        : (Color32)config.HardModeColor(cell);
                }
            }

            ApplyDarknessRect(texture, buffers, config.Darkness, rectX, rectY, rectW, rectH);
        }

        /// <summary>Переприменяет только финальный проход виньетки (шаг 10) поверх уже готовых
        /// PreVignette-пикселей всего изображения - самый дешёвый путь при смене только darkness.</summary>
        public static void ReapplyDarkness(Texture2D texture, MapRasterBuffers buffers, float darkness)
        {
            ApplyDarknessRect(texture, buffers, darkness, 0, 0, buffers.Width, buffers.Height);
        }

        static void ApplyDarknessRect(Texture2D texture, MapRasterBuffers buffers, float darkness, int rectX, int rectY, int rectW, int rectH)
        {
            int w = buffers.Width;
            var outPixels = new Color32[rectW * rectH];

            for (int y = 0; y < rectH; y++)
            {
                int py = rectY + y;
                for (int x = 0; x < rectW; x++)
                {
                    int px = rectX + x;
                    int idx = py * w + px;
                    Color32 c = buffers.PreVignette[idx];

                    float dx = (px + 0.5f) / buffers.Width - 0.5f;
                    float dy = (py + 0.5f) / buffers.Height - 0.5f;
                    float dist01 = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 0.5f);
                    float keep = 1f - dist01 * Mathf.Clamp01(darkness / 100f);

                    outPixels[y * rectW + x] = new Color32(
                        (byte)(c.r * keep), (byte)(c.g * keep), (byte)(c.b * keep), 255);
                }
            }

            texture.SetPixels32(rectX, rectY, rectW, rectH, outPixels);
            texture.Apply(false);
        }

        // ---- Painted-pipeline hooks - stubbed here, implemented in Task 6 ----

        static void BakePaintedFields(
            IReadOnlyList<VoronoiCell> cells, IReadOnlyDictionary<int, VoronoiCell> cellById, NearestCellLookup lookup,
            MapRasterConfig config, MapRasterBuffers buffers, int rectX, int rectY, int rectW, int rectH)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }

        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }

        static System.Numerics.Vector2 PixelToSite(int x, int y, int w, int h, float mapWidth, float mapHeight)
        {
            float px = (x + 0.5f) / w * mapWidth;
            float pz = (y + 0.5f) / h * mapHeight;
            return new System.Numerics.Vector2(px, pz);
        }
    }
}
```

`BakePaintedFields`/`BakePaintedPixel` above throw `NotImplementedException` for now — Task 6 replaces both bodies with the real painted-pipeline implementation. That's fine for this task: nothing calls the painted branch yet because the self-test below only exercises `SmoothBorders = false` (the hard-mode branch, fully implemented already).

- [ ] **Step 2: Add self-test to `WorldMapRenderer.cs`**

This exercises only the hard-mode branch (`SmoothBorders = false`), which is fully implemented after this task. Add after `SelfTestBiomeFamilyCoverage`:

```csharp
        [ContextMenu("Self-Test: Raster Hard Mode Parity")]
        public void SelfTestRasterHardModeParity()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(2.5f, 5f)) { Biome = Biome.Grassland, RegionId = 0, IsOcean = false };
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7.5f, 5f)) { Biome = Biome.Grassland, RegionId = 1, IsOcean = false };
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a, [1] = b };

            var savedDisplayMode = displayMode;
            displayMode = MapDisplayMode.Region;
            Color expectedA = GetColorForCell(a);

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 10,
                TexHeight = 10,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = false,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
            var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, displayMode, config, tex, buffers, 0, 0, 10, 10);

            // Пиксель (2,5) на текстуре 10x10 для карты 10x10 сэмплирует мировую точку (2.5, 5.5) -
            // ближе всего к Site клетки a (2.5, 5).
            Color actual = tex.GetPixel(2, 5);
            bool ok = Mathf.Abs(expectedA.r - actual.r) < 0.01f
                      && Mathf.Abs(expectedA.g - actual.g) < 0.01f
                      && Mathf.Abs(expectedA.b - actual.b) < 0.01f;

            displayMode = savedDisplayMode;
            Destroy(tex);

            Debug.Log(ok
                ? "Self-Test Raster Hard Mode Parity: PASS"
                : $"Self-Test Raster Hard Mode Parity: FAIL (expected={expectedA}, actual={actual})");
        }
```

- [ ] **Step 3: Verify compile**

Both new files must compile cleanly — no errors expected.

- [ ] **Step 4: Run the self-test**

Expected: `Self-Test Raster Hard Mode Parity: PASS`.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): add MapRasterizer core (hard-mode sampling + vignette)"
```

---

### Task 6: `MapRasterizer` painted pipeline (Combined + smoothBorders)

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs` (replace the two stub methods from Task 5)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (add one self-test method)

**Interfaces:**
- Consumes: `Noise.Fbm`/`Noise.ValueNoise` (Task 1), `MapPalette.GetFamily`/`GetSlotColor` (Task 3), `RegionColorPalette.HillshadeBrightness(...,out ndotl)` (Task 4), `NearestCellLookup.FindWithinRadius` (Task 2).
- Produces: fully working `BakePaintedFields`/`BakePaintedPixel` — no new public API beyond what Task 5 already declared.

- [ ] **Step 1: Replace the two stub methods in `MapRasterizer.cs`**

Replace:

```csharp
        static void BakePaintedFields(
            IReadOnlyList<VoronoiCell> cells, IReadOnlyDictionary<int, VoronoiCell> cellById, NearestCellLookup lookup,
            MapRasterConfig config, MapRasterBuffers buffers, int rectX, int rectY, int rectW, int rectH)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }

        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            throw new NotImplementedException("Реализуется в Task 6 (painted pipeline).");
        }
```

with:

```csharp
        /// <summary>Проход 1.5 (только суша, только painted-режим): блендированные elevation/
        /// temperature/базовый цвет семейства среди соседей в радиусе smoothRadius, вес
        /// 1/(distance²+1) - см. design doc, шаг 3. Вода не блендится (шаг 2: категория
        /// суша/океан/озеро всегда "hard" по ближайшей клетке).</summary>
        static void BakePaintedFields(
            IReadOnlyList<VoronoiCell> cells, IReadOnlyDictionary<int, VoronoiCell> cellById, NearestCellLookup lookup,
            MapRasterConfig config, MapRasterBuffers buffers, int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;

            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    bool isWater = cell.EffectiveIsOcean || cell.EffectiveIsLake;

                    if (isWater)
                    {
                        buffers.Elevation[idx] = cell.EffectiveElevation;
                        buffers.Temperature[idx] = cell.EffectiveTemperature;
                        continue;
                    }

                    var point = PixelToSite(x, y, w, h, config.MapWidth, config.MapHeight);
                    float sumW = 0f, elev = 0f, temp = 0f, cr = 0f, cg = 0f, cb = 0f;

                    foreach (var (neighbor, distance) in lookup.FindWithinRadius(point, config.SmoothRadius))
                    {
                        if (neighbor.EffectiveIsOcean || neighbor.EffectiveIsLake) continue;
                        float weight = 1f / (distance * distance + 1f);
                        sumW += weight;
                        elev += weight * neighbor.EffectiveElevation;
                        temp += weight * neighbor.EffectiveTemperature;
                        Color32 fc = MapPalette.GetSlotColor(config.Theme, MapPalette.GetFamily(neighbor.Biome));
                        cr += weight * fc.r; cg += weight * fc.g; cb += weight * fc.b;
                    }

                    if (sumW <= 0f)
                    {
                        buffers.Elevation[idx] = cell.EffectiveElevation;
                        buffers.Temperature[idx] = cell.EffectiveTemperature;
                        buffers.FamilyColor[idx] = MapPalette.GetSlotColor(config.Theme, MapPalette.GetFamily(cell.Biome));
                    }
                    else
                    {
                        buffers.Elevation[idx] = elev / sumW;
                        buffers.Temperature[idx] = temp / sumW;
                        buffers.FamilyColor[idx] = new Color32(
                            (byte)Mathf.Clamp(cr / sumW, 0f, 255f),
                            (byte)Mathf.Clamp(cg / sumW, 0f, 255f),
                            (byte)Mathf.Clamp(cb / sumW, 0f, 255f), 255);
                    }
                }
            }
        }

        struct ResolvedPalette
        {
            public Color32 Shallow, Abyss, LakeS, LakeD, Glow, Outline, Light, TintCool, TintWarm;
        }

        static ResolvedPalette ResolvePalette(MapPaletteTheme theme) => new ResolvedPalette
        {
            Shallow = MapPalette.GetSlotColor(theme, PaletteSlot.Shallow),
            Abyss = MapPalette.GetSlotColor(theme, PaletteSlot.Abyss),
            LakeS = MapPalette.GetSlotColor(theme, PaletteSlot.LakeS),
            LakeD = MapPalette.GetSlotColor(theme, PaletteSlot.LakeD),
            Glow = MapPalette.GetSlotColor(theme, PaletteSlot.Glow),
            Outline = MapPalette.GetSlotColor(theme, PaletteSlot.Outline),
            Light = MapPalette.GetSlotColor(theme, PaletteSlot.Light),
            TintCool = MapPalette.GetSlotColor(theme, PaletteSlot.TintCool),
            TintWarm = MapPalette.GetSlotColor(theme, PaletteSlot.TintWarm),
        };

        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            var palette = ResolvePalette(config.Theme);
            float coldAmt = 0.10f + (config.ColdLight / 100f) * 0.30f;
            float varAmt = config.RegionVariation / 100f;

            bool isWater = cell.EffectiveIsOcean || cell.EffectiveIsLake;
            return isWater
                ? ColorForWaterPixel(cell, buffers, cellById, x, y, w, h, config, palette, coldAmt)
                : ColorForLandPixel(cell, buffers, cellById, idx, x, y, w, h, config, palette, coldAmt, varAmt);
        }

        static Color32 ColorForWaterPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt)
        {
            float depth = Mathf.Clamp01(config.WaterDepth01(cell));
            Color32 shallowOrLakeS = cell.EffectiveIsLake ? palette.LakeS : palette.Shallow;
            Color32 deep = cell.EffectiveIsLake ? palette.LakeD : palette.Abyss;

            float r = Mathf.Lerp(shallowOrLakeS.r, deep.r, depth);
            float g = Mathf.Lerp(shallowOrLakeS.g, deep.g, depth);
            float b = Mathf.Lerp(shallowOrLakeS.b, deep.b, depth);

            if (!cell.EffectiveIsLake)
            {
                float ripple = (Noise.Fbm(x / 40f, y / 26f, config.Seed + 401, 2) - 0.5f) * 10f;
                r += ripple; g += ripple; b += ripple;
            }

            if (HasNeighborWithWaterStatus(buffers, cellById, x, y, w, h, wantWater: false))
            {
                float gk = 0.32f + coldAmt * 0.5f;
                r += (palette.Glow.r - r) * gk;
                g += (palette.Glow.g - g) * gk;
                b += (palette.Glow.b - b) * gk;
            }

            return ClampColor32(r, g, b);
        }

        static Color32 ColorForLandPixel(
            VoronoiCell cell, MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById, int idx,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt, float varAmt)
        {
            Color32 fam = buffers.FamilyColor[idx];
            float r = fam.r, g = fam.g, b = fam.b;

            // Региональная тонировка (шаг 5а) - к tintCool/tintWarm по температуре, вес 0.38 фиксирован.
            float temperature = buffers.Temperature[idx];
            float wn = Mathf.InverseLerp(0.28f, 0.70f, temperature);
            float tr = Mathf.Lerp(palette.TintCool.r, palette.TintWarm.r, wn);
            float tg = Mathf.Lerp(palette.TintCool.g, palette.TintWarm.g, wn);
            float tb = Mathf.Lerp(palette.TintCool.b, palette.TintWarm.b, wn);
            r += (tr - r) * 0.38f; g += (tg - g) * 0.38f; b += (tb - b) * 0.38f;

            // Региональная вариация - крупнозернистый цветовой шум (шаг 5б).
            if (varAmt > 0f)
            {
                float nx = x / (float)w, ny = y / (float)h;
                float rgA = Noise.Fbm(nx * 1.6f + 20f, ny * 1.6f + 40f, config.Seed + 1500, 2);
                float rr = (rgA - 0.5f) * 38f * varAmt;
                r += rr; g += rr * 0.9f; b += rr * 0.7f;
            }

            if (HasNeighborWithWaterStatus(buffers, cellById, x, y, w, h, wantWater: true))
            {
                // Береговая обводка (шаг 7, сторона суши) - жёсткая замена, перекрывает hillshade.
                r = palette.Outline.r; g = palette.Outline.g; b = palette.Outline.b;
            }
            else
            {
                // Рельеф + холодный лунный подсвет (шаг 6).
                float gradX = (buffers.Elevation[ClampIdx(x - 1, y, w, h)] - buffers.Elevation[ClampIdx(x + 1, y, w, h)]) * 0.5f;
                float gradY = (buffers.Elevation[ClampIdx(x, y - 1, w, h)] - buffers.Elevation[ClampIdx(x, y + 1, w, h)]) * 0.5f;
                float brightness = RegionColorPalette.HillshadeBrightness(
                    gradX, gradY, config.ReliefStrength, config.ReliefLightAzimuth, config.ReliefAmbient, out float ndotl);

                r = r * brightness + palette.Light.r * ndotl * coldAmt;
                g = g * brightness + palette.Light.g * ndotl * coldAmt;
                b = b * brightness + palette.Light.b * ndotl * coldAmt;
            }

            // Зерно (шаг 8) - применяется всегда, включая береговую обводку (как в референсе).
            float grain = (Noise.ValueNoise(x * 0.5f, y * 0.5f, config.Seed + 61) - 0.5f) * 7f;
            r += grain; g += grain; b += grain;

            // Дополнительная лайтнесс-вариация (шаг 9, только суша).
            if (varAmt > 0f)
            {
                float nx = x / (float)w, ny = y / (float)h;
                float rgB = Noise.Fbm(nx * 2.0f + 50f, ny * 2.0f + 70f, config.Seed + 1600, 2) - 0.5f;
                float lf = 1f + rgB * 0.24f * varAmt;
                r *= lf; g *= lf; b *= lf;
            }

            return ClampColor32(r, g, b);
        }

        static bool HasNeighborWithWaterStatus(
            MapRasterBuffers buffers, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int x, int y, int w, int h, bool wantWater)
        {
            return Check(ClampIdx(x - 1, y, w, h)) || Check(ClampIdx(x + 1, y, w, h))
                || Check(ClampIdx(x, y - 1, w, h)) || Check(ClampIdx(x, y + 1, w, h));

            bool Check(int idx)
            {
                var c = cellById[buffers.CellId[idx]];
                bool isWater = c.EffectiveIsOcean || c.EffectiveIsLake;
                return isWater == wantWater;
            }
        }

        static int ClampIdx(int x, int y, int w, int h) => Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1);

        static Color32 ClampColor32(float r, float g, float b) => new Color32(
            (byte)Mathf.Clamp(r, 0f, 255f), (byte)Mathf.Clamp(g, 0f, 255f), (byte)Mathf.Clamp(b, 0f, 255f), 255);
```

- [ ] **Step 2: Add self-test to `WorldMapRenderer.cs`**

Add after `SelfTestRasterHardModeParity`:

```csharp
        [ContextMenu("Self-Test: Raster Elevation Invariant")]
        public void SelfTestRasterElevationInvariant()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(3f, 3f)) { Biome = Biome.Grassland, Height = 0.42f, Temperature = 0.5f, IsOcean = false };
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7f, 7f)) { Biome = Biome.Grassland, Height = 0.6f, Temperature = 0.5f, IsOcean = false };
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a, [1] = b };

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 20,
                TexHeight = 20,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = true,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f,
                RegionVariation = 0f,
                Darkness = 40f,
                SmoothRadius = 0.01f, // почти отключаем блендинг с соседом b - проверяем чистый сэмпл клетки a
                ReliefStrength = 3f,
                ReliefLightAzimuth = 315f,
                ReliefAmbient = 0.5f,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
            var tex = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 20, 20);

            int px = Mathf.FloorToInt(3f / 10f * 20f);
            int py = Mathf.FloorToInt(3f / 10f * 20f);
            float sampledElevation = buffers.Elevation[py * 20 + px];
            bool ok = Mathf.Abs(sampledElevation - a.EffectiveElevation) < 0.02f;

            Destroy(tex);
            Debug.Log(ok
                ? "Self-Test Raster Elevation Invariant: PASS"
                : $"Self-Test Raster Elevation Invariant: FAIL (sampled={sampledElevation:F3}, expected={a.EffectiveElevation:F3})");
        }
```

- [ ] **Step 3: Verify compile and run both new self-tests**

Expected: `Self-Test Raster Elevation Invariant: PASS`, and re-run `Self-Test Raster Hard Mode Parity` to confirm it's still PASS (painted pipeline must not affect the hard branch).

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): implement painted pipeline (blend, tint, hillshade, coastline, grain, vignette)"
```

---

### Task 7: `WorldMapRenderer` integration — quad mesh, rebake plumbing, hit-testing

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (large — fields, `Awake`, `BuildMesh`, `RecolorOnly` removal, `LoadFromCells` split, `GetCellUnderRay`, `BuildBorders`/`SetDisplayMode`/`SetShowCoastlineLayer` coastline-visibility, override call sites)

**Interfaces:**
- Consumes: `MapPaletteTheme`, `MapRasterConfig`, `MapRasterBuffers`, `MapRasterizer.Bake/RebakeRegion/ReapplyDarkness`, `NearestCellLookup` (Tasks 1-6).
- Produces: `WorldMapRenderer.RebakeAll()` (private), `RebakeRegion(IEnumerable<VoronoiCell>)` (private), `RebakeAffectedCells(IEnumerable<VoronoiCell>)` (public, used by Task 8's `BrushToolController`), `ReapplyDarkness()` (public, unused until subproject 6 adds a darkness slider), `PrepareLoadFromCells(...)`/`FinishLoadFromCells()`/`RebakeAllStepped(Action<float>)` (public, used by Task 9's `MapScreenController`). `GetCellUnderRay`/`TryGetSiteHitPoint`/`Cells`/`GetCellById` keep their existing public signatures.

- [ ] **Step 1: Add the `using` and new fields**

At the top of `Assets/WorldGen/Rendering/WorldMapRenderer.cs`, change:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
```

to:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;
```

Then, in the `[Header("Combined: границы")]` block (around line 99-103), add a new header + fields right after it (before `[Header("Камера (опционально)")]`):

```csharp
        [Header("Combined: тёмный рендер (MapRaster)")]
        public MapPaletteTheme paletteTheme = MapPaletteTheme.ColdTwilight;
        [Range(0f, 100f)] public float coldLight = 58f;
        [Range(0f, 100f)] public float regionVariation = 45f;
        [Range(40f, 100f)] public float darkness = 72f;
        [Tooltip("Сглаженные границы биомов + полный 'нарисованный' конвейер (тонировка, рельеф, зерно, свечение берега). Выключено = старый плоский вид один-в-один, только через текстуру.")]
        public bool smoothBorders = true;
        [Tooltip("Большая сторона запекаемой текстуры карты в пикселях; меньшая считается по аспекту mapWidth:mapHeight.")]
        public int rasterLongSide = 2048;
```

Then replace the private fields block (around line 109-127):

```csharp
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MeshCollider meshCollider;
        bool cameraPlacedOnce;

        List<VoronoiCell> cells;
        Dictionary<int, VoronoiCell> cellById;
        Dictionary<int, int> oceanDistanceFromLand; // только для океанских клеток - BFS-расстояние (в клетках) от ближайшей не-океанской суши, для чисто визуальной "глубины"
        int maxOceanDistanceFromLand = 1;
        List<Corner> corners;
        List<TemperatureEpicenter> epicenters;
        List<MoistureEpicenter> moistureEpicenters;
        List<River> rivers;
        GenerationParams lastGenParams; // храним последние параметры, чтобы RegenerateTemperature мог работать без полной генерации
        int[] triangleToCellId;
        Transform riverContainer; // родительский объект для всех LineRenderer рек - упрощает очистку при перегенерации
        Transform borderContainer;        // родитель для меш-объектов границ
        GameObject regionBorderObject;    // меш-лента границ регионов
        GameObject coastlineObject;       // меш-лента береговой линии
```

with:

```csharp
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MeshCollider meshCollider;
        bool cameraPlacedOnce;

        List<VoronoiCell> cells;
        Dictionary<int, VoronoiCell> cellById;
        Dictionary<int, int> oceanDistanceFromLand; // только для океанских клеток - BFS-расстояние (в клетках) от ближайшей не-океанской суши, для чисто визуальной "глубины"
        int maxOceanDistanceFromLand = 1;
        List<Corner> corners;
        List<TemperatureEpicenter> epicenters;
        List<MoistureEpicenter> moistureEpicenters;
        List<River> rivers;
        GenerationParams lastGenParams; // храним последние параметры, чтобы RegenerateTemperature мог работать без полной генерации
        Transform riverContainer; // родительский объект для всех LineRenderer рек - упрощает очистку при перегенерации
        Transform borderContainer;        // родитель для меш-объектов границ
        GameObject regionBorderObject;    // меш-лента границ регионов
        GameObject coastlineObject;       // меш-лента береговой линии

        NearestCellLookup nearestLookup;
        Texture2D rasterTexture;
        Material rasterMaterial;
        MapRasterBuffers rasterBuffers;
        int texWidth, texHeight;
```

(`triangleToCellId` is removed - it belonged to the fan-mesh hit-testing this task replaces.)

- [ ] **Step 2: Update `Awake()`**

Replace:

```csharp
        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            if (meshRenderer.sharedMaterial == null)
                Debug.LogWarning("WorldMapRenderer: материал не назначен. Цвета клеток не будут видны без шейдера, читающего Vertex Color.");
        }
```

with:

```csharp
        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            EnsureRasterMaterial();
        }

        /// <summary>Sprites/Default: unlit, double-sided (Cull Off) - как у рек/границ в этом же файле
        /// (см. BuildRivers/CreateBorderObject). Предпочтён встроенному Unlit/Texture, чтобы не зависеть
        /// от winding order квада - не нужно подбирать точный порядок вершин, как в старом
        /// BuildMesh для fan-триангуляции клеток. Материал создаётся в коде, поэтому
        /// Assets/WorldGen/Rendering/WorldMaterial.mat больше не используется этим рендерером
        /// (оставлен нетронутым - см. плановые ограничения).</summary>
        void EnsureRasterMaterial()
        {
            if (rasterMaterial != null) return;
            rasterMaterial = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.material = rasterMaterial;
        }
```

- [ ] **Step 3: Rewrite `BuildMesh` as a quad builder + extract `RebuildSpatialIndex`**

Replace the entire existing `BuildMesh` method (the fan-triangulation version, roughly lines 718-786) with:

```csharp
        public void BuildMesh(List<VoronoiCell> sourceCells)
        {
            cells = sourceCells;
            RebuildSpatialIndex();
            BuildQuadMesh();
            RebakeAll();
        }

        /// <summary>cellById/oceanDistanceFromLand/nearestLookup всегда пересчитываются вместе -
        /// общий шаг для BuildMesh (генерация/ContextMenu) и PrepareLoadFromCells (генерация через
        /// прогресс-экран, см. MapScreenController).</summary>
        void RebuildSpatialIndex()
        {
            cellById = new Dictionary<int, VoronoiCell>(cells.Count);
            foreach (var c in cells) cellById[c.Id] = c;
            oceanDistanceFromLand = ComputeOceanDistanceFromLand();
            nearestLookup = new NearestCellLookup(cells, minPointDistance);
        }

        /// <summary>Один плоский квад mapWidth×mapHeight в плоскости XZ - заменяет тысячи
        /// клеточных fan-мешей. Цвет приходит из текстуры (см. RebakeAll), не из vertex color.
        /// Sprites/Default не culлит грани, так что winding order (0,1,2 vs 0,2,1) здесь не важен -
        /// сравни со старым fan-мешем, где неверный winding "смотрел вниз" и требовал разворота.</summary>
        void BuildQuadMesh()
        {
            var mesh = new Mesh();
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(mapWidth, 0f, 0f),
                new Vector3(mapWidth, 0f, mapHeight),
                new Vector3(0f, 0f, mapHeight),
            };
            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            var triangles = new[] { 0, 1, 2, 0, 2, 3 };

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = null; // обязательно сбросить перед переприсваиванием - иначе Unity не обновит коллизию на месте
            meshCollider.sharedMesh = mesh;
        }
```

- [ ] **Step 4: Add `RebakeAll`/`RebakeRegion`/`RebakeAffectedCells`/`ComputeTexSize`/`BuildRasterConfig`/`ComputeTouchedPixelRect`, remove old `RecolorOnly`**

Delete the existing private `RecolorOnly()` method (around lines 844-860):

```csharp
        /// <summary>Перекрашивает существующий меш без полного перестроения геометрии - быстрее при переключении режима отображения.</summary>
        void RecolorOnly()
        {
            var mesh = meshFilter.mesh;
            var colors = new List<Color>(mesh.vertexCount);

            foreach (var cell in cells)
            {
                if (cell.Polygon.Count < 3) continue;
                Color c = GetColorForCell(cell);
                int vertCountInFan = cell.Polygon.Count + 2;
                for (int i = 0; i < vertCountInFan; i++)
                    colors.Add(c);
            }

            mesh.SetColors(colors);
        }
```

Replace it with:

```csharp
        void RebakeAll()
        {
            if (cells == null) return;
            ComputeTexSize(out texWidth, out texHeight);

            var config = BuildRasterConfig();
            rasterTexture = MapRasterizer.Bake(cells, cellById, nearestLookup, displayMode, config, out rasterBuffers);
            EnsureRasterMaterial();
            rasterMaterial.mainTexture = rasterTexture;
        }

        void RebakeRegion(IEnumerable<VoronoiCell> touchedCells)
        {
            if (cells == null) return;
            if (rasterTexture == null) { RebakeAll(); return; }

            ComputeTouchedPixelRect(touchedCells, out int rx, out int ry, out int rw, out int rh);
            if (rw <= 0 || rh <= 0) return;

            var config = BuildRasterConfig();
            MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, displayMode, config, rasterTexture, rasterBuffers, rx, ry, rw, rh);
        }

        /// <summary>Перезапекает текстуру только вокруг клеток, затронутых кистью в последнем
        /// стемпе - вместо полного RebakeAll на каждое изменение (см. BrushToolController.ApplyStamp).
        /// Закрывает roadmap-пункт "кисть перекрашивает весь меш на каждое движение".</summary>
        public void RebakeAffectedCells(IEnumerable<VoronoiCell> touchedCells) => RebakeRegion(touchedCells);

        /// <summary>Самый дешёвый путь при смене только darkness (подпроект 6 добавит слайдер) -
        /// заново применяет только финальный проход виньетки поверх уже готовых PreVignette-пикселей,
        /// не пересчитывая блендинг/тонировку/рельеф/зерно заново. Без вызывающей UI в этом
        /// подпроекте, но нужен уже сейчас как часть публичного API RebakeAll/RebakeRegion пары.</summary>
        public void ReapplyDarkness()
        {
            if (rasterTexture == null || rasterBuffers == null) return;
            MapRasterizer.ReapplyDarkness(rasterTexture, rasterBuffers, darkness);
        }

        void ComputeTexSize(out int w, out int h)
        {
            if (mapWidth >= mapHeight)
            {
                w = Mathf.Max(4, rasterLongSide);
                h = Mathf.Max(4, Mathf.RoundToInt(rasterLongSide * (mapHeight / mapWidth)));
            }
            else
            {
                h = Mathf.Max(4, rasterLongSide);
                w = Mathf.Max(4, Mathf.RoundToInt(rasterLongSide * (mapWidth / mapHeight)));
            }
        }

        MapRasterConfig BuildRasterConfig()
        {
            return new MapRasterConfig
            {
                TexWidth = texWidth,
                TexHeight = texHeight,
                MapWidth = mapWidth,
                MapHeight = mapHeight,
                Seed = seed,
                Theme = paletteTheme,
                ColdLight = coldLight,
                RegionVariation = regionVariation,
                Darkness = darkness,
                SmoothBorders = smoothBorders,
                SmoothRadius = minPointDistance * 1.5f,
                ReliefStrength = reliefStrength,
                ReliefLightAzimuth = reliefLightAzimuth,
                ReliefAmbient = reliefAmbient,
                HardModeColor = GetColorForCell,
                WaterDepth01 = GetWaterDepth01,
            };
        }

        /// <summary>Bounding rect (в пикселях текстуры) клеток, затронутых кистью, расширенный на
        /// smoothRadius - чтобы захватить область, куда блендинг "протекает" из соседних клеток,
        /// которые сами не изменились, но их пиксели рядом с границей должны пересчитаться.</summary>
        void ComputeTouchedPixelRect(IEnumerable<VoronoiCell> touchedCells, out int rx, out int ry, out int rw, out int rh)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var cell in touchedCells)
            {
                foreach (var p in cell.Polygon)
                {
                    any = true;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minZ) minZ = p.Y;
                    if (p.Y > maxZ) maxZ = p.Y;
                }
            }

            if (!any) { rx = ry = rw = rh = 0; return; }

            float pad = minPointDistance * 1.5f;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;

            int px0 = Mathf.Clamp(Mathf.FloorToInt(minX / mapWidth * texWidth), 0, texWidth - 1);
            int px1 = Mathf.Clamp(Mathf.CeilToInt(maxX / mapWidth * texWidth), 0, texWidth - 1);
            int py0 = Mathf.Clamp(Mathf.FloorToInt(minZ / mapHeight * texHeight), 0, texHeight - 1);
            int py1 = Mathf.Clamp(Mathf.CeilToInt(maxZ / mapHeight * texHeight), 0, texHeight - 1);

            rx = px0; ry = py0; rw = px1 - px0 + 1; rh = py1 - py0 + 1;
        }

        /// <summary>Чанковый (по строкам текстуры) запек для экрана прогресса генерации - RebakeRegion
        /// уже умеет пересчитывать произвольный прямоугольник "с нуля" (для кисти), здесь он же
        /// вызывается построчными полосами с yield между ними, чтобы UI не подвисал (см.
        /// MapScreenController.RunGeneration). Должен вызываться ПОСЛЕ PrepareLoadFromCells.</summary>
        public System.Collections.IEnumerator RebakeAllStepped(System.Action<float> onProgress)
        {
            if (cells == null) yield break;
            ComputeTexSize(out texWidth, out texHeight);

            rasterTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rasterBuffers = MapRasterizer.CreateEmptyBuffers(texWidth, texHeight);
            EnsureRasterMaterial();
            rasterMaterial.mainTexture = rasterTexture;

            var config = BuildRasterConfig();
            const int chunkRows = 64;

            for (int y0 = 0; y0 < texHeight; y0 += chunkRows)
            {
                int rh = Mathf.Min(chunkRows, texHeight - y0);
                MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, displayMode, config, rasterTexture, rasterBuffers, 0, y0, texWidth, rh);
                onProgress?.Invoke((y0 + rh) / (float)texHeight);
                yield return null;
            }
        }
```

- [ ] **Step 5: Split `LoadFromCells` into `PrepareLoadFromCells`/`RebakeAll`/`FinishLoadFromCells`**

Replace the existing `LoadFromCells` method with:

```csharp
        public void LoadFromCells(List<VoronoiCell> loadedCells, GenerationParams referenceParams)
        {
            PrepareLoadFromCells(loadedCells, referenceParams);
            RebakeAll();
            FinishLoadFromCells();
        }

        /// <summary>Первая половина LoadFromCells (данные + геометрия квада, без запека текстуры) -
        /// используется напрямую MapScreenController, чтобы вставить между ней и FinishLoadFromCells
        /// чанковый RebakeAllStepped с прогресс-баром вместо синхронного RebakeAll.</summary>
        public void PrepareLoadFromCells(List<VoronoiCell> loadedCells, GenerationParams referenceParams)
        {
            cells = loadedCells;
            corners = CornerGraphBuilder.Build(cells);
            rivers = new List<River>();
            epicenters = new List<TemperatureEpicenter>();
            moistureEpicenters = new List<MoistureEpicenter>();
            lastGenParams = referenceParams;
            seed = referenceParams.Seed;
            mapWidth = referenceParams.Width;
            mapHeight = referenceParams.Height;

            RebuildSpatialIndex();
            BuildQuadMesh();
        }

        /// <summary>Вторая половина LoadFromCells (реки, границы, камера, события) - вызывается
        /// MapScreenController после RebakeAllStepped.</summary>
        public void FinishLoadFromCells()
        {
            BuildRivers();
            BuildBorders();

            if (targetCamera != null)
                PositionCameraOverMap();

            OnDisplayChanged?.Invoke();
            OnWorldRegenerated?.Invoke();
        }
```

- [ ] **Step 6: Rewrite `GetCellUnderRay`**

Replace:

```csharp
        /// <summary>Возвращает клетку под курсором/прицелом по физическому рейкасту в коллайдер карты.</summary>
        public VoronoiCell GetCellUnderRay(Ray ray, float maxDistance = 2000f)
        {
            if (cells == null) return null;
            if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                int cellId = triangleToCellId[hit.triangleIndex];
                return cells.FirstOrDefault(c => c.Id == cellId);
            }
            return null;
        }
```

with:

```csharp
        /// <summary>Возвращает клетку под курсором/прицелом по физическому рейкасту в коллайдер карты -
        /// через UV попадания на квад (RaycastHit.textureCoord) переведённые в пиксель cellId-буфера,
        /// а не через индекс треугольника (квад больше не хранит per-cell геометрию, см. BuildQuadMesh).</summary>
        public VoronoiCell GetCellUnderRay(Ray ray, float maxDistance = 2000f)
        {
            if (cells == null || rasterBuffers == null) return null;
            if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                int px = Mathf.Clamp(Mathf.FloorToInt(hit.textureCoord.x * texWidth), 0, texWidth - 1);
                int py = Mathf.Clamp(Mathf.FloorToInt(hit.textureCoord.y * texHeight), 0, texHeight - 1);
                int cellId = rasterBuffers.CellId[py * texWidth + px];
                return GetCellById(cellId);
            }
            return null;
        }
```

- [ ] **Step 7: Swap remaining `RecolorOnly()` call sites to `RebakeAll()`**

In each of these existing methods, replace the `RecolorOnly();` call with `RebakeAll();` (same call position, no other changes to the method):
- `RegenerateTemperatureOnly()`
- `ApplyClimateOverride(...)`
- `ClearClimateOverride(...)`
- `ApplyElevationOverride(...)`
- `ApplyWaterOverride(...)` (also still calls `BuildBorders()` right after — keep that call, just swap `RecolorOnly()`→`RebakeAll()` before it)
- `ApplyBiomeOverride(...)`
- `ClearAllOverrides(...)`
- `UndoLastBrushStroke()` (inside the `if (didUndo) { ... }` block)
- `UndoAllBrushStrokes()` (inside the `if (any) { ... }` block)

- [ ] **Step 8: Simplify the brush per-cell methods and `EndBrushStroke`**

Replace:

```csharp
        /// <summary>Завершает текущий мазок кистью, кладёт его в историю Undo - вызывать при отпускании ЛКМ.</summary>
        public void EndBrushStroke()
        {
            brushUndo.EndStroke();
            // Перекрашиваем/перестраиваем меш один раз по завершении мазка - во время самого мазка
            // (BrushAdjust*) рендер уже обновляется на каждое изменение, так что здесь это просто
            // финальная подстраховка на случай рассинхрона.
            if (cells != null) RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Прибавляет delta к elevation клетки (относительное изменение, кисть). Записывает "досмазковое" состояние клетки в текущий мазок перед изменением.</summary>
        public void BrushAdjustElevation(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustElevation(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }

        /// <summary>Прибавляет delta к температуре клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustTemperature(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustTemperature(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }

        /// <summary>Прибавляет delta к влажности клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustMoisture(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustMoisture(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }
```

with:

```csharp
        /// <summary>Завершает текущий мазок кистью, кладёт его в историю Undo - вызывать при отпускании ЛКМ.
        /// Полный рибейк здесь больше не нужен: BrushToolController.ApplyStamp уже вызвал
        /// RebakeAffectedCells для каждого стемпа мазка, текстура уже в актуальном состоянии.</summary>
        public void EndBrushStroke()
        {
            brushUndo.EndStroke();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Прибавляет delta к elevation клетки (относительное изменение, кисть). Записывает
        /// "досмазковое" состояние клетки в текущий мазок перед изменением. Не перезапекает текстуру -
        /// BrushToolController вызывает RebakeAffectedCells один раз на весь стемп (см. roadmap-пункт
        /// про перекраску кистью).</summary>
        public void BrushAdjustElevation(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustElevation(cell, delta, beachElevationThreshold);
        }

        /// <summary>Прибавляет delta к температуре клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustTemperature(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustTemperature(cell, delta, beachElevationThreshold);
        }

        /// <summary>Прибавляет delta к влажности клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustMoisture(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustMoisture(cell, delta, beachElevationThreshold);
        }
```

And replace:

```csharp
        public void BrushSetBiome(VoronoiCell cell, Biome biome)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            cell.BiomeOverride = biome;
            CellOverrideService.RecomputeBiome(cell, beachElevationThreshold);
            RecolorOnly();
        }
```

with:

```csharp
        public void BrushSetBiome(VoronoiCell cell, Biome biome)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            cell.BiomeOverride = biome;
            CellOverrideService.RecomputeBiome(cell, beachElevationThreshold);
        }
```

(The doc-comment above `BrushSetBiome` already describes the "hard set" behavior and doesn't mention `RecolorOnly` by name, so it needs no wording change.)

- [ ] **Step 9: Hide the coastline ribbon when Combined+smoothBorders is active**

Add a small private helper right above `BuildBorders()`:

```csharp
        /// <summary>Combined+smoothBorders уже рисует собственное свечение берега в самой текстуре -
        /// риббон береговой линии дублировал бы эффект, поэтому скрывается именно в этом случае.
        /// В Height/Region/Biome и Combined-без-сглаживания риббон работает как раньше.</summary>
        bool ShouldShowCoastlineRibbon() =>
            displayMode == MapDisplayMode.Combined && showCoastlineLayer && !smoothBorders;
```

Then in `BuildBorders()`, replace:

```csharp
            bool combined = displayMode == MapDisplayMode.Combined;
            regionBorderObject.SetActive(combined && showRegionBordersLayer);
            coastlineObject.SetActive(combined && showCoastlineLayer);
```

with:

```csharp
            bool combined = displayMode == MapDisplayMode.Combined;
            regionBorderObject.SetActive(combined && showRegionBordersLayer);
            coastlineObject.SetActive(ShouldShowCoastlineRibbon());
```

In `SetDisplayMode(...)`, replace:

```csharp
            if (regionBorderObject != null) regionBorderObject.SetActive(combined && showRegionBordersLayer);
            if (coastlineObject != null) coastlineObject.SetActive(combined && showCoastlineLayer);
```

with:

```csharp
            if (regionBorderObject != null) regionBorderObject.SetActive(combined && showRegionBordersLayer);
            if (coastlineObject != null) coastlineObject.SetActive(ShouldShowCoastlineRibbon());
```

In `SetShowCoastlineLayer(bool on)`, replace:

```csharp
        public void SetShowCoastlineLayer(bool on)
        {
            showCoastlineLayer = on;
            if (coastlineObject != null)
                coastlineObject.SetActive(displayMode == MapDisplayMode.Combined && on);
            OnDisplayChanged?.Invoke();
        }
```

with:

```csharp
        public void SetShowCoastlineLayer(bool on)
        {
            showCoastlineLayer = on;
            if (coastlineObject != null)
                coastlineObject.SetActive(ShouldShowCoastlineRibbon());
            OnDisplayChanged?.Invoke();
        }
```

- [ ] **Step 10: Verify compile**

At this point `WorldMapRenderer.cs` should compile with no references to `RecolorOnly`, `triangleToCellId`, or `PolygonTriangulator` remaining in this file (the `Polygon`/fan-triangulation self-tests `SelfTestBorderClassification` etc. don't touch the mesh, only `MapBorderBuilder`/`LakeRegionUnifier`/`CellWaterAssigner`, so they're unaffected). Search to confirm:

```bash
grep -n "RecolorOnly\|triangleToCellId" "Assets/WorldGen/Rendering/WorldMapRenderer.cs"
```

Expected: no matches.

- [ ] **Step 11: Manual Play Mode check**

Enter Play Mode, use the "Generate World" context menu (or the Generation screen if wired in the scene). Confirm:
- The map renders as a single textured quad, visible from the default top-down camera (not black/culled).
- Switching `displayMode` between Height/Region/Biome/Combined via `MapToolbarUI`/`MapLayersPanel` still shows the expected old-style flat colors for Height/Region/Biome, and Combined shows the new painted look with `smoothBorders = true` by default.
- Clicking a cell still selects the correct cell (`CellSelectionController`'s yellow overlay appears over the clicked cell, not an adjacent one).

- [ ] **Step 12: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): rewire WorldMapRenderer onto quad+texture raster (replaces vertex-color fan mesh)"
```

---

### Task 8: Brush integration — dirty-rect rebake instead of per-cell full recolor

**Files:**
- Modify: `Assets/WorldGen/Rendering/BrushToolController.cs:164-198`

**Interfaces:**
- Consumes: `WorldMapRenderer.RebakeAffectedCells(IEnumerable<VoronoiCell>)` (Task 7).
- No public API changes to `BrushToolController` itself.

- [ ] **Step 1: Update `ApplyStamp` to rebake once per stamp**

Replace:

```csharp
        void ApplyStamp(Vector2 site)
        {
            var affected = BrushOps.CellsInRadius(
                mapRenderer.Cells, site.x, site.y, brushRadius, shape == BrushShape.Square);
            if (affected.Count == 0) return;

            if (activeTool == BrushTool.Biome)
            {
                Biome biome = selectedBiome.Value;
                foreach (var cell in affected)
                    mapRenderer.BrushSetBiome(cell, biome);
                return;
            }

            if (mode == BrushMode.Smooth)
            {
                ApplySmooth(affected);
                return;
            }

            float signedDelta = (mode == BrushMode.Raise ? +1f : -1f) * brushStep * strength;
            if (signedDelta == 0f) return;
            foreach (var cell in affected)
                ApplyDelta(cell, signedDelta);
        }
```

with:

```csharp
        void ApplyStamp(Vector2 site)
        {
            var affected = BrushOps.CellsInRadius(
                mapRenderer.Cells, site.x, site.y, brushRadius, shape == BrushShape.Square);
            if (affected.Count == 0) return;

            if (activeTool == BrushTool.Biome)
            {
                Biome biome = selectedBiome.Value;
                foreach (var cell in affected)
                    mapRenderer.BrushSetBiome(cell, biome);
                mapRenderer.RebakeAffectedCells(affected);
                return;
            }

            if (mode == BrushMode.Smooth)
            {
                ApplySmooth(affected);
                mapRenderer.RebakeAffectedCells(affected);
                return;
            }

            float signedDelta = (mode == BrushMode.Raise ? +1f : -1f) * brushStep * strength;
            if (signedDelta == 0f) return;
            foreach (var cell in affected)
                ApplyDelta(cell, signedDelta);
            mapRenderer.RebakeAffectedCells(affected);
        }
```

- [ ] **Step 2: Verify compile**

No other changes needed in this file — `ApplyDelta`/`ApplySmooth`/`ReadValue`/`NeighborValues` are untouched.

- [ ] **Step 3: Manual Play Mode check**

Enter Play Mode, generate a world, switch to the Редактор tab, and paint with each brush tool (Elevation raise/lower, Temperature, Moisture, Smooth, Biome hard-set). Confirm:
- Painting still visibly changes the map in real time as the cursor moves (no lag spike vs. before).
- Ctrl+Z undoes the last stroke correctly (map reverts to the pre-stroke look).
- "Отменить всё" reverts all strokes from the session.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/BrushToolController.cs
git commit -m "perf(map-raster): rebake only the brush-stamp dirty rect instead of the whole texture per cell"
```

---

### Task 9: Generation-progress pipeline — 6th step for the raster bake

**Files:**
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs:191-257` (`GenerateWorldStepped`)
- Modify: `Assets/WorldGen/Rendering/GenerationProgressUI.cs:17-21` (`StepLabels`)
- Modify: `Assets/WorldGen/Rendering/MapScreenController.cs:115-127` (`RunGeneration`)

**Interfaces:**
- Consumes: `WorldMapRenderer.PrepareLoadFromCells`/`RebakeAllStepped`/`FinishLoadFromCells` (Task 7).
- No change to `GenerateWorldStepped`'s public signature — only its internal progress fractions and the removal of its own "Готово" emit (now owned by the caller, after the bake step).

- [ ] **Step 1: Rescale `GenerateWorldStepped`'s fractions and drop its own "Готово"**

In `Assets/WorldGen/Generation/WorldGenerator.cs`, change each `onProgress?.Invoke(...)` call's denominator from `5f` to `6f`:

```csharp
            // --- Step 1/5: Генерация высот ---
            onProgress?.Invoke("Генерация высот", 0f / 5f);
```
→
```csharp
            // --- Step 1/6: Генерация высот ---
            onProgress?.Invoke("Генерация высот", 0f / 6f);
```

```csharp
            // --- Step 2/5: Океаны и озёра ---
            onProgress?.Invoke("Океаны и озёра", 1f / 5f);
```
→
```csharp
            // --- Step 2/6: Океаны и озёра ---
            onProgress?.Invoke("Океаны и озёра", 1f / 6f);
```

```csharp
            // --- Step 3/5: Температура и влажность ---
            onProgress?.Invoke("Температура и влажность", 2f / 5f);
```
→
```csharp
            // --- Step 3/6: Температура и влажность ---
            onProgress?.Invoke("Температура и влажность", 2f / 6f);
```

```csharp
            // --- Step 4/5: Расчёт биомов ---
            onProgress?.Invoke("Расчёт биомов", 3f / 5f);
```
→
```csharp
            // --- Step 4/6: Расчёт биомов ---
            onProgress?.Invoke("Расчёт биомов", 3f / 6f);
```

```csharp
            // --- Step 5/5: Границы регионов ---
            onProgress?.Invoke("Границы регионов", 4f / 5f);
```
→
```csharp
            // --- Step 5/6: Границы регионов ---
            onProgress?.Invoke("Границы регионов", 4f / 6f);
```

And delete the final line before `onComplete?.Invoke(...)`:

```csharp
            onProgress?.Invoke("Готово", 5f / 5f);
            onComplete?.Invoke(cells, temperatureEpicenters, moistureEpicenters, rivers);
```
→
```csharp
            onComplete?.Invoke(cells, temperatureEpicenters, moistureEpicenters, rivers);
```

Also update the method's doc comment header (currently says "5 progress-reportable stages") to say "5 of 6" for future readers:

```csharp
    /// <summary>
    /// Same pipeline as GenerateWorld, split into 5 progress-reportable stages for the
    /// Generation Progress screen. Temperature is computed right after moisture here
```
→
```csharp
    /// <summary>
    /// Same pipeline as GenerateWorld, split into 5 of 6 progress-reportable stages for the
    /// Generation Progress screen (the 6th, "Отрисовка карты", is owned by MapScreenController
    /// after this coroutine completes - see WorldMapRenderer.RebakeAllStepped). Temperature is
    /// computed right after moisture here
```

- [ ] **Step 2: Add the 6th checklist label**

In `Assets/WorldGen/Rendering/GenerationProgressUI.cs`, replace:

```csharp
        static readonly string[] StepLabels =
        {
            "Генерация высот", "Океаны и озёра", "Температура и влажность",
            "Расчёт биомов", "Границы регионов"
        };
```

with:

```csharp
        static readonly string[] StepLabels =
        {
            "Генерация высот", "Океаны и озёра", "Температура и влажность",
            "Расчёт биомов", "Границы регионов", "Отрисовка карты"
        };
```

- [ ] **Step 3: Wire the stepped bake into `MapScreenController.RunGeneration`**

Replace:

```csharp
        System.Collections.IEnumerator RunGeneration(GenerationParams genParams)
        {
            RefreshScreenStateForGenerating();

            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) =>
                {
                    mapRenderer.LoadFromCells(cells, genParams);
                    activeGeneration = null;
                    RefreshScreenState();
                });
        }
```

with:

```csharp
        System.Collections.IEnumerator RunGeneration(GenerationParams genParams)
        {
            RefreshScreenStateForGenerating();

            List<VoronoiCell> generatedCells = null;
            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) => generatedCells = cells);

            mapRenderer.PrepareLoadFromCells(generatedCells, genParams);
            yield return mapRenderer.RebakeAllStepped(bakeFrac => progressScreen.SetStep("Отрисовка карты", (5f + bakeFrac) / 6f));
            mapRenderer.FinishLoadFromCells();

            progressScreen.SetStep("Готово", 1f);
            activeGeneration = null;
            RefreshScreenState();
        }
```

This needs `List<VoronoiCell>` in scope — add the missing `using` at the top of the file:

```csharp
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
```
→
```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
```

- [ ] **Step 4: Verify compile**

Confirm `MapScreenController.cs` compiles with the new `using System.Collections.Generic;` and that `WorldGenerator.cs`/`GenerationProgressUI.cs` show no leftover references to the old `/5f` denominators:

```bash
grep -n "/ 5f\|5f / 5f" "Assets/WorldGen/Generation/WorldGenerator.cs"
```

Expected: no matches.

- [ ] **Step 5: Manual Play Mode check**

Enter Play Mode from a fresh state (no map yet), use the Generation screen to start a new world. Confirm:
- The progress card's checklist now shows 6 rows, ending with "Отрисовка карты".
- The percentage/fill bar advances smoothly through all 6 steps without jumping backward or freezing the whole app during the raster-bake step (brief per-chunk hitches are fine; a multi-second full freeze is not).
- After "Готово", the map screen swaps in correctly (Generation/Progress screens hide, map+legend show) exactly as before this change.
- Loading a saved project via "Файл → Открыть" still works (this path uses the original synchronous `LoadFromCells`, untouched by this task).

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Generation/WorldGenerator.cs Assets/WorldGen/Rendering/GenerationProgressUI.cs Assets/WorldGen/Rendering/MapScreenController.cs
git commit -m "feat(map-raster): add 'Отрисовка карты' as a 6th generation-progress step"
```

---

### Task 10: Whole-feature verification pass

**Files:** none (verification only — fix forward in the relevant file from Tasks 1-9 if something fails)

- [ ] **Step 1: Full regression pass in Play Mode**

Starting from a fresh Editor session, walk through:
1. Generate a new world (default params). Confirm the Combined view shows the new dark-fantasy painted look (smooth biome blending, hillshade, coastline glow+outline, grain, vignette) — compare visually against `design_handoff_realmweaver_map/screens/*.png` for the general mood (exact pixel match isn't expected, this project samples different data than the prototype's own fbm terrain).
2. Switch to Height, Region, Biome, and back to Combined. Confirm Height/Region/Biome look exactly like they did before this branch (flat, unsmoothed colors, no hillshade painting).
3. Toggle each Combined sub-layer checkbox in `MapLayersPanel` (biome/relief/region-borders/coastline) — confirm the coastline ribbon stays hidden while Combined+smoothBorders is active (it's superseded by the painted glow) and reappears if you inspect the `WorldMapRenderer` component and manually uncheck `smoothBorders` in the Inspector, then toggle Combined mode again.
4. Paint with every brush tool + undo/undo-all (already checked in Task 8, re-confirm here after all later tasks landed).
5. Place, select, drag, and delete a few POIs (`PoiToolPanel`) — confirm click hit-testing still finds the right cell under the new quad-based `GetCellUnderRay`.
6. Select cells with `CellSelectionController` (click, shift-click, drag) and apply a climate override from the override panel — confirm the highlight overlay and the resulting recolor both land on the correct cells.
7. Save the project (`ProjectMenuBar` → Сохранить как), then load it back (Открыть) — confirm the reloaded map looks identical to the saved one (same palette/painted look, same overrides).
8. Resize/generate a Small/Medium/Large map from the Generation screen — confirm the 6-step progress card completes for all three sizes without errors.

- [ ] **Step 2: Re-run every `[ContextMenu]` self-test added in this plan**

On the `WorldMapRenderer` component: `Self-Test: Noise Determinism And Range`, `Self-Test: Nearest Cell Lookup`, `Self-Test: Biome Family Coverage`, `Self-Test: Raster Hard Mode Parity`, `Self-Test: Raster Elevation Invariant`, plus the pre-existing `Self-Test: Hillshade Brightness`, `Self-Test: Border Classification`, `Self-Test: Lake Region Unification`, `Self-Test: Ocean Connectivity`. On `BrushToolController`: `Self-Test: Brush Radius Query`, `Self-Test: Smooth Averaging`. All expected `PASS`.

- [ ] **Step 3: Fix forward**

If any check in Step 1 or 2 fails, identify which Task's change is responsible (most failures will trace to Task 6's painted-pipeline math or Task 7's hit-testing/coordinate mapping), fix in place, and re-run the full Step 1 + Step 2 sweep before considering the branch done.

- [ ] **Step 4: Final commit (if fixes were needed)**

```bash
git add -A
git commit -m "fix(map-raster): address issues found in whole-branch verification pass"
```

(Skip this commit entirely if Steps 1-2 passed clean with no changes needed.)
