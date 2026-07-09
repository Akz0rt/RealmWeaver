# Map Decorations — Iso Terrain Sprites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Draw side-view "fake-iso" terrain sprites (mountains, hills, pine, autumn trees, mesa) over the top-down map, placed by biome/elevation, depth-sorted, theme-tinted, GPU-instanced, updated on brush stroke-end.

**Architecture:** Three decoupled parts under `Assets/WorldGen/Rendering/Decorations/` — a pure-C# `DecorationPlacer` (cells → deterministic `List<DecorationInstance>`), a swappable `DecorationCatalog`/`DecorationAtlasBaker` (procedural placeholder sprites → one RGBA32 atlas + UV-rects), and a `DecorationRenderer` MonoBehaviour that GPU-instances flat quads sampling the atlas. `WorldMapRenderer` drives placement after generation/load and re-places the touched rect on `EndBrushStroke`.

**Tech Stack:** Unity 6000.3.2f1, C#, HLSL (unlit instanced shader), `Graphics.DrawMeshInstanced` + `MaterialPropertyBlock` vector arrays, `Texture2D` procedural atlas.

**Spec:** `docs/superpowers/specs/2026-07-09-map-decorations-iso-sprites-design.md`

## Global Constraints

- Namespace: `WorldGen.Rendering.Decorations`. Files under `Assets/WorldGen/Rendering/Decorations/`.
- Agents cannot run Unity → every `[ContextMenu]` self-test is run by the USER in the Editor; code reviews are static. Only `DecorationPlacer` (pure C#) has real self-tests; renderer/atlas/shader are user-verified visually.
- Map coordinate space: everything parents to `mapRenderer.transform`; local pos `(x, yOffset, z)` in `[0..mapWidth]×[0..mapHeight]`, XZ plane, top-down ortho camera. A cell's world pos = `(cell.Site.X, y, cell.Site.Y)`. `VoronoiCell.Site` is `System.Numerics.Vector2`.
- Cell data (verbatim names): `cell.Biome`, `cell.EffectiveElevation` (float 0..1), `cell.EffectiveTemperature` (float), `cell.EffectiveIsOcean`, `cell.EffectiveIsLake`. Land test: `RegionCategories.IsLandCell(cell)` = `!(EffectiveIsOcean || EffectiveIsLake)`.
- Biome→family: `MapPalette.GetFamily(cell.Biome)` → `BiomeFamily { Sea, Lake, Coast, Snow, Tundra, Highland, Badlands, Forest, ForestWarm, Moor, Plains }`.
- Palette: `MapPalette.GetSlotColor(MapPaletteTheme theme, PaletteSlot slot)` and `...(theme, BiomeFamily family)` → `Color32`. Theme source: `mapRenderer.paletteTheme` (`MapPaletteTheme`). Slots include `MtnL, MtnS, Snow, Forest, ForestWarm, Badlands, Highland, Outline`.
- Nearest-cell query: `nearestLookup.FindNearest(System.Numerics.Vector2 p)` → `VoronoiCell` (null if none; excludes degenerate `Polygon.Count<3` cells).
- **CRITICAL (build):** `Decorations.shader` is loaded at runtime via `Shader.Find` → it WILL be stripped from the build unless added to Project Settings → Graphics → Always Included Shaders. This exact class of bug froze v0.2.0 generation at 67% (MapTerrain shader). Task 8 adds it; do not skip.
- Y-layer for decorations: `0.45` (above borders `0.4`, below POI `0.5`).
- Deferred (NOT this plan): reeds (no Marsh biome), POI medallions, region labels, chrome, fog of war, authored sprite art (placeholders only), RenderMeshIndirect/culling upgrade.

---

### Task 1: Decoration data types + config

**Files:**
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationTypes.cs`
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationConfig.cs`

**Interfaces:**
- Produces: `enum DecorationType { Mountain, Hill, Pine, AutumnTree, Mesa }`; `enum DecorationStyleCategory { Bare, Snowy, Forested, Plain }`; `struct DecorationInstance`; `[Serializable] class DecorationConfig` + nested `[Serializable] class TypeDensity`.

- [ ] **Step 1: Create the data types**

`Assets/WorldGen/Rendering/Decorations/DecorationTypes.cs`:
```csharp
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    public enum DecorationType { Mountain, Hill, Pine, AutumnTree, Mesa }

    /// <summary>Контекст-категория варианта (детерминирована по биому/температуре клетки).
    /// Bare/Snowy/Forested — для гор и холмов; Snowy/Plain — для хвои; Plain — для остальных.</summary>
    public enum DecorationStyleCategory { Bare, Snowy, Forested, Plain }

    /// <summary>Один спрайт декорации: что и где рисовать. worldPos в координатах карты (XZ).</summary>
    public struct DecorationInstance
    {
        public Vector2 worldPos;              // x = worldX, y = worldZ (карта XZ)
        public DecorationType type;
        public DecorationStyleCategory style;
        public int artVariant;                // индекс картинки внутри (type, style)
        public float scale;                   // мировой размер (высота спрайта в мировых единицах)
        public Color32 tint;
        public float sortZ;                   // = worldPos.y; back-to-front по возрастанию
    }
}
```

- [ ] **Step 2: Create the config**

`Assets/WorldGen/Rendering/Decorations/DecorationConfig.cs`:
```csharp
using System;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Параметры расстановки/рендера декораций. Один сериализованный экземпляр на
    /// WorldMapRenderer (правится в Inspector, живой OnValidate); тот же объект передаётся в
    /// DecorationPlacer как вход.</summary>
    [Serializable]
    public class DecorationConfig
    {
        public bool enabled = true;

        [Header("Пороги высоты (EffectiveElevation 0..1)")]
        [Range(0f, 1f)] public float mountainMinElevation = 0.72f; // >= => гора
        [Range(0f, 1f)] public float hillMinElevation = 0.55f;     // [hill,mtn) => холм
        [Tooltip("EffectiveTemperature ниже этого => снежная категория (горы/холмы/хвоя).")]
        public float coldTemperature = 0.32f;

        [Header("Плотность (шаг грида в мировых единицах; меньше = гуще)")]
        public float mountainGridStep = 26f;
        public float hillGridStep = 30f;
        public float pineGridStep = 12f;
        public float autumnGridStep = 12f;
        public float mesaGridStep = 34f;

        [Header("Вероятность постановки в грид-точке [0..1]")]
        [Range(0f, 1f)] public float mountainProbability = 0.55f;
        [Range(0f, 1f)] public float hillProbability = 0.35f;
        [Range(0f, 1f)] public float pineProbability = 0.66f;
        [Range(0f, 1f)] public float autumnProbability = 0.62f;
        [Range(0f, 1f)] public float mesaProbability = 0.18f;

        [Header("Размеры (мировые единицы, высота спрайта)")]
        public float mountainSize = 34f;
        public float hillSize = 16f;
        public float treeSize = 13f;
        public float mesaSize = 14f;
        [Range(0.1f, 3f)] public float globalScale = 1f;
        [Range(0f, 0.6f)] public float sizeJitter = 0.25f; // ± доля к размеру

        [Header("Производительность")]
        public int maxInstances = 6000;

        public float GridStep(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainGridStep,
            DecorationType.Hill => hillGridStep,
            DecorationType.Pine => pineGridStep,
            DecorationType.AutumnTree => autumnGridStep,
            DecorationType.Mesa => mesaGridStep,
            _ => 20f,
        };

        public float Probability(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainProbability,
            DecorationType.Hill => hillProbability,
            DecorationType.Pine => pineProbability,
            DecorationType.AutumnTree => autumnProbability,
            DecorationType.Mesa => mesaProbability,
            _ => 0f,
        };

        public float BaseSize(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainSize,
            DecorationType.Hill => hillSize,
            DecorationType.Pine => treeSize,
            DecorationType.AutumnTree => treeSize,
            DecorationType.Mesa => mesaSize,
            _ => 12f,
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations/DecorationTypes.cs Assets/WorldGen/Rendering/Decorations/DecorationConfig.cs
git commit -m "feat(decorations): data types + DecorationConfig"
```

> USER: after Unity imports, commit the generated `.meta` files (`DecorationTypes.cs.meta`, `DecorationConfig.cs.meta`).

---

### Task 2: DecorationPlacer — classification (which type + style for a cell)

**Files:**
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationPlacer.cs`
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs`

**Interfaces:**
- Consumes: `DecorationConfig`, `VoronoiCell`, `MapPalette.GetFamily`, `RegionCategories.IsLandCell`.
- Produces: `static bool DecorationPlacer.TryClassify(VoronoiCell cell, DecorationConfig cfg, DecorationType type, out DecorationStyleCategory style)` — true if `type` is allowed on `cell`, with the context style; `static uint DecorationPlacer.Hash(int x, int y, int salt)`.

- [ ] **Step 1: Write the classification + hash (start the file)**

`Assets/WorldGen/Rendering/Decorations/DecorationPlacer.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Чистый C# движок расстановки декораций: клетки → детерминированный список
    /// инстансов. Без Unity-рендера (юнит-тестируемый как генераторы в Generation/).</summary>
    public static class DecorationPlacer
    {
        // --- Классификация: подходит ли тип к клетке + контекст-категория стиля ---

        static bool IsColdCell(VoronoiCell c, DecorationConfig cfg)
        {
            var fam = MapPalette.GetFamily(c.Biome);
            return c.EffectiveTemperature < cfg.coldTemperature
                   || fam == BiomeFamily.Snow || fam == BiomeFamily.Tundra;
        }

        /// <summary>Категория для гор/холмов: Snowy если холодно, иначе Forested над лесом, иначе Bare.</summary>
        static DecorationStyleCategory ReliefStyle(VoronoiCell c, DecorationConfig cfg)
        {
            if (IsColdCell(c, cfg)) return DecorationStyleCategory.Snowy;
            var fam = MapPalette.GetFamily(c.Biome);
            if (fam == BiomeFamily.Forest || fam == BiomeFamily.ForestWarm) return DecorationStyleCategory.Forested;
            return DecorationStyleCategory.Bare;
        }

        public static bool TryClassify(VoronoiCell cell, DecorationConfig cfg,
                                       DecorationType type, out DecorationStyleCategory style)
        {
            style = DecorationStyleCategory.Plain;
            if (!RegionCategories.IsLandCell(cell)) return false;

            float e = cell.EffectiveElevation;
            var fam = MapPalette.GetFamily(cell.Biome);

            switch (type)
            {
                case DecorationType.Mountain:
                    if (e < cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Hill:
                    if (e < cfg.hillMinElevation || e >= cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Pine:
                    if (fam != BiomeFamily.Forest) return false;
                    style = IsColdCell(cell, cfg) ? DecorationStyleCategory.Snowy : DecorationStyleCategory.Plain;
                    return true;
                case DecorationType.AutumnTree:
                    if (fam != BiomeFamily.ForestWarm) return false;
                    style = DecorationStyleCategory.Plain; return true;
                case DecorationType.Mesa:
                    if (fam != BiomeFamily.Badlands) return false;
                    style = DecorationStyleCategory.Plain; return true;
                default: return false;
            }
        }

        // --- Детерминированный хеш (fract от целочисленного mix) ---
        public static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        static float Hash01(int x, int y, int salt) => Hash(x, y, salt) / 4294967295f;
    }
}
```

- [ ] **Step 2: Write the classification self-tests**

`Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs`:
```csharp
using System.Numerics;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>[ContextMenu] self-tests запускает ПОЛЬЗОВАТЕЛЬ в Editor (агенты не гоняют Unity).
    /// Добавь этот компонент на любой объект сцены, ПКМ по компоненту → выбери тест.</summary>
    public class DecorationSelfTests : MonoBehaviour
    {
        // Клетка суши с заданными biome/elev/temp. VoronoiCell(int id, System.Numerics.Vector2 site).
        static VoronoiCell LandCell(int id, Biome biome, float elev, float temp)
        {
            var c = new VoronoiCell(id, new Vector2(id * 10f, 0f))
            {
                Biome = biome, Height = elev, Temperature = temp,
                IsOcean = false,
            };
            return c;
        }

        [ContextMenu("Self-Test: Decoration Classify")]
        public void SelfTestClassify()
        {
            var cfg = new DecorationConfig();
            bool ok = true;

            // Гора только на высокой клетке.
            var high = LandCell(1, Biome.Bare, 0.80f, 0.5f);
            ok &= DecorationPlacer.TryClassify(high, cfg, DecorationType.Mountain, out var ms) && ms == DecorationStyleCategory.Bare;
            var low = LandCell(2, Biome.Grassland, 0.20f, 0.5f);
            ok &= !DecorationPlacer.TryClassify(low, cfg, DecorationType.Mountain, out _);

            // Снежная гора при холоде.
            var coldHigh = LandCell(3, Biome.Bare, 0.85f, 0.1f);
            ok &= DecorationPlacer.TryClassify(coldHigh, cfg, DecorationType.Mountain, out var cs) && cs == DecorationStyleCategory.Snowy;

            // Лесистая гора над Forest-семейством (тёплой).
            var forestHigh = LandCell(4, Biome.TemperateDeciduousForest, 0.80f, 0.7f);
            ok &= DecorationPlacer.TryClassify(forestHigh, cfg, DecorationType.Mountain, out var fs) && fs == DecorationStyleCategory.Forested;

            // Хвоя только на Forest-семействе; осень — на ForestWarm.
            ok &= DecorationPlacer.TryClassify(LandCell(5, Biome.Taiga, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= !DecorationPlacer.TryClassify(LandCell(6, Biome.Grassland, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= DecorationPlacer.TryClassify(LandCell(7, Biome.TropicalRainForest, 0.3f, 0.8f), cfg, DecorationType.AutumnTree, out _);

            // Меса только на Badlands.
            ok &= DecorationPlacer.TryClassify(LandCell(8, Biome.SubtropicalDesert, 0.3f, 0.8f), cfg, DecorationType.Mesa, out _);

            // Вода — всегда пусто.
            var ocean = new VoronoiCell(9, new Vector2(0, 0)) { Biome = Biome.Ocean, IsOcean = true };
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Mountain, out _);
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Pine, out _);

            Debug.Log(ok ? "Self-Test Decoration Classify: PASS" : "Self-Test Decoration Classify: FAIL");
        }
    }
}
```

- [ ] **Step 3: USER runs the self-test in Editor**

Add `DecorationSelfTests` to a scene GameObject, right-click the component → **Self-Test: Decoration Classify**.
Expected in Console: `Self-Test Decoration Classify: PASS`.
(Confirm `VoronoiCell` has settable `Biome`/`Height`/`Temperature`/`IsOcean` and a `(int, System.Numerics.Vector2)` ctor; if `EffectiveElevation`/`EffectiveTemperature` derive from other fields, adjust `LandCell` to set the source fields — check `VoronoiCell.cs`.)

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations/DecorationPlacer.cs Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs
git commit -m "feat(decorations): cell classification (type + context style) + self-test"
```

---

### Task 3: DecorationPlacer — placement (grid sampling, determinism, rect-scoped, cap)

**Files:**
- Modify: `Assets/WorldGen/Rendering/Decorations/DecorationPlacer.cs`
- Modify: `Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs`

**Interfaces:**
- Consumes: `NearestCellLookup.FindNearest`, `MapPalette.GetSlotColor`, Task 2 `TryClassify`/`Hash`.
- Produces: `static List<DecorationInstance> DecorationPlacer.Place(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup, int seed, float mapW, float mapH, DecorationConfig cfg, MapPaletteTheme theme)`; `static void DecorationPlacer.PlaceRect(List<DecorationInstance> into, NearestCellLookup lookup, int seed, float mapW, float mapH, DecorationConfig cfg, MapPaletteTheme theme, Rect worldRect)` (appends instances whose grid-points fall inside `worldRect`).

- [ ] **Step 1: Add the tint helper + per-type grid pass**

Append to `DecorationPlacer` class (before the closing brace):
```csharp
        static readonly DecorationType[] AllTypes =
        { DecorationType.Mountain, DecorationType.Hill, DecorationType.Pine, DecorationType.AutumnTree, DecorationType.Mesa };

        static int SaltOf(DecorationType t) => (int)t * 101 + 17;

        static Color32 TintFor(DecorationType type, DecorationStyleCategory style, MapPaletteTheme theme, float brightness)
        {
            Color32 baseC = type switch
            {
                DecorationType.Mountain or DecorationType.Hill => style == DecorationStyleCategory.Snowy
                        ? MapPalette.GetSlotColor(theme, PaletteSlot.Snow)
                        : style == DecorationStyleCategory.Forested
                            ? MapPalette.GetSlotColor(theme, PaletteSlot.Forest)
                            : MapPalette.GetSlotColor(theme, PaletteSlot.MtnL),
                DecorationType.Pine => MapPalette.GetSlotColor(theme, PaletteSlot.Forest),
                DecorationType.AutumnTree => MapPalette.GetSlotColor(theme, PaletteSlot.ForestWarm),
                DecorationType.Mesa => MapPalette.GetSlotColor(theme, PaletteSlot.Badlands),
                _ => new Color32(200, 200, 200, 255),
            };
            return new Color32(
                (byte)Mathf.Clamp(baseC.r * brightness, 0, 255),
                (byte)Mathf.Clamp(baseC.g * brightness, 0, 255),
                (byte)Mathf.Clamp(baseC.b * brightness, 0, 255), 255);
        }

        /// <summary>Один тип: джиттер-грид по всей карте (или по rect). Грид-индексы стабильны от 0,
        /// поэтому rect-подвыборка совпадает с полным проходом на пересечении.</summary>
        static void PlaceType(List<DecorationInstance> into, DecorationType type,
            NearestCellLookup lookup, int seed, float mapW, float mapH, DecorationConfig cfg,
            MapPaletteTheme theme, Rect? worldRect)
        {
            float step = cfg.GridStep(type);
            if (step <= 0.01f) return;
            int salt = SaltOf(type) + seed;
            int nx = Mathf.CeilToInt(mapW / step);
            int ny = Mathf.CeilToInt(mapH / step);

            int gx0 = 0, gy0 = 0, gx1 = nx, gy1 = ny;
            if (worldRect.HasValue)
            {
                var r = worldRect.Value;
                gx0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin / step));
                gy0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin / step));
                gx1 = Mathf.Min(nx, Mathf.CeilToInt(r.xMax / step) + 1);
                gy1 = Mathf.Min(ny, Mathf.CeilToInt(r.yMax / step) + 1);
            }

            float prob = cfg.Probability(type);
            float baseSize = cfg.BaseSize(type);

            for (int gy = gy0; gy < gy1; gy++)
            for (int gx = gx0; gx < gx1; gx++)
            {
                if (Hash(gx, gy, salt) / 4294967295f > prob) continue;
                float jx = (Hash(gx, gy, salt + 1) / 4294967295f) * step;
                float jz = (Hash(gx, gy, salt + 2) / 4294967295f) * step;
                float wx = gx * step + jx;
                float wz = gy * step + jz;
                if (wx >= mapW || wz >= mapH) continue;

                if (worldRect.HasValue && !worldRect.Value.Contains(new Vector2(wx, wz))) continue;

                var cell = lookup.FindNearest(new System.Numerics.Vector2(wx, wz));
                if (cell == null) continue;
                if (!TryClassify(cell, cfg, type, out var style)) continue;

                float sizeJit = 1f + (Hash(gx, gy, salt + 3) / 4294967295f - 0.5f) * 2f * cfg.sizeJitter;
                float brightness = 0.88f + (Hash(gx, gy, salt + 4) / 4294967295f) * 0.24f;
                into.Add(new DecorationInstance
                {
                    worldPos = new Vector2(wx, wz),
                    type = type, style = style,
                    artVariant = (int)(Hash(gx, gy, salt + 5) & 0xFFFF),
                    scale = baseSize * sizeJit * cfg.globalScale,
                    tint = TintFor(type, style, theme, brightness),
                    sortZ = wz,
                });
            }
        }

        public static List<DecorationInstance> Place(IReadOnlyList<VoronoiCell> cells,
            NearestCellLookup lookup, int seed, float mapW, float mapH,
            DecorationConfig cfg, MapPaletteTheme theme)
        {
            var list = new List<DecorationInstance>();
            if (cfg == null || !cfg.enabled || lookup == null) return list;
            foreach (var t in AllTypes)
                PlaceType(list, t, lookup, seed, mapW, mapH, cfg, theme, null);

            if (list.Count > cfg.maxInstances)
            {
                Debug.LogWarning($"[Decorations] placed {list.Count} > cap {cfg.maxInstances}; truncated. Increase maxInstances or grid steps.");
                list.RemoveRange(cfg.maxInstances, list.Count - cfg.maxInstances);
            }
            list.Sort((a, b) => a.sortZ.CompareTo(b.sortZ));
            return list;
        }

        /// <summary>Дописывает в into инстансы всех типов, чьи грид-точки попадают в worldRect.
        /// Вызывающий сам чистит старые инстансы этого rect и ре-сортирует.</summary>
        public static void PlaceRect(List<DecorationInstance> into,
            NearestCellLookup lookup, int seed, float mapW, float mapH,
            DecorationConfig cfg, MapPaletteTheme theme, Rect worldRect)
        {
            if (cfg == null || !cfg.enabled || lookup == null) return;
            foreach (var t in AllTypes)
                PlaceType(into, t, lookup, seed, mapW, mapH, cfg, theme, worldRect);
        }
```

- [ ] **Step 2: Add placement self-tests**

Append these `[ContextMenu]` methods to `DecorationSelfTests`:
```csharp
        // Строит крошечную карту-фикстуру: сетка клеток, левая половина суша-высокая, остальное низина.
        static (System.Collections.Generic.List<VoronoiCell> cells, WorldGen.Rendering.MapRaster.NearestCellLookup lookup)
            Fixture(float mapSize, float spacing)
        {
            var cells = new System.Collections.Generic.List<VoronoiCell>();
            int id = 0;
            for (float z = spacing * 0.5f; z < mapSize; z += spacing)
            for (float x = spacing * 0.5f; x < mapSize; x += spacing)
            {
                float elev = x < mapSize * 0.5f ? 0.85f : 0.15f; // левая половина — горы
                var c = new VoronoiCell(id++, new System.Numerics.Vector2(x, z))
                { Biome = Biome.Bare, Height = elev, Temperature = 0.5f, IsOcean = false };
                cells.Add(c);
            }
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, spacing);
            return (cells, lookup);
        }

        [ContextMenu("Self-Test: Decoration Placement")]
        public void SelfTestPlacement()
        {
            const float M = 400f;
            var (cells, lookup) = Fixture(M, 40f);
            var cfg = new DecorationConfig();
            var theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight;
            bool ok = true;

            var a = DecorationPlacer.Place(cells, lookup, 7, M, M, cfg, theme);
            var b = DecorationPlacer.Place(cells, lookup, 7, M, M, cfg, theme);
            ok &= a.Count == b.Count && a.Count > 0; // детерминизм: одинаковый размер
            for (int i = 0; i < a.Count && ok; i++)
                ok &= a[i].worldPos == b[i].worldPos && a[i].type == b[i].type && a[i].style == b[i].style;

            // Горы только в левой половине (высокая суша).
            foreach (var d in a)
                if (d.type == DecorationType.Mountain) ok &= d.worldPos.x < M * 0.5f + 40f;

            // sortZ неубывающий (отсортировано back-to-front).
            for (int i = 1; i < a.Count; i++) ok &= a[i].sortZ >= a[i - 1].sortZ;

            // rect == full: подвыборка правого-нижнего квадранта совпадает с фильтром полного прохода.
            var rect = new Rect(M * 0.5f, M * 0.5f, M * 0.5f, M * 0.5f);
            var rectList = new System.Collections.Generic.List<DecorationInstance>();
            DecorationPlacer.PlaceRect(rectList, lookup, 7, M, M, cfg, theme, rect);
            int fullInRect = 0;
            foreach (var d in a) if (rect.Contains(d.worldPos)) fullInRect++;
            ok &= rectList.Count == fullInRect;

            // Плотность: удвоение вероятности не уменьшает число гор.
            var dense = new DecorationConfig { mountainProbability = 1f };
            var denseList = DecorationPlacer.Place(cells, lookup, 7, M, M, dense, theme);
            int mtnA = 0, mtnD = 0;
            foreach (var d in a) if (d.type == DecorationType.Mountain) mtnA++;
            foreach (var d in denseList) if (d.type == DecorationType.Mountain) mtnD++;
            ok &= mtnD >= mtnA;

            Debug.Log(ok ? "Self-Test Decoration Placement: PASS" : "Self-Test Decoration Placement: FAIL");
        }
```

- [ ] **Step 3: USER runs the placement self-test in Editor**

Right-click `DecorationSelfTests` → **Self-Test: Decoration Placement**. Expected: `PASS`.
(If `NearestCellLookup` ctor signature differs, adjust `Fixture`; check `NearestCellLookup.cs`.)

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations/DecorationPlacer.cs Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs
git commit -m "feat(decorations): grid placement (deterministic, rect-scoped, capped) + self-tests"
```

---

### Task 4: DecorationCatalog + placeholder atlas

**Files:**
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationPlaceholderFactory.cs`
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationCatalog.cs`

**Interfaces:**
- Produces: `class DecorationCatalog` with `Texture2D Atlas`, `Vector4 UvRect(DecorationType type, DecorationStyleCategory style, int artVariant)` (xy = offset, zw = size in UV), `int VariantCount(DecorationType, DecorationStyleCategory)`, and a static `DecorationCatalog BuildPlaceholder()`.

- [ ] **Step 1: Placeholder factory — draw simple side-view sprites into a tile**

`Assets/WorldGen/Rendering/Decorations/DecorationPlaceholderFactory.cs`:
```csharp
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Процедурно рисует простые side-view спрайты-плейсхолдеры в квадратный тайл
    /// (RGBA, прозрачный фон, пивот низ-центр). Зеркало PoiPlaceholderFactory. Заменяемо:
    /// подложить готовый арт вместо этих рисовалок (см. спеку, шов замены).</summary>
    public static class DecorationPlaceholderFactory
    {
        // Рисует один тайл size×size. Тон приходит из per-instance tint в шейдере, поэтому
        // здесь рисуем в оттенках серого (luminance) + альфа; форма/затенение — важное.
        public static Color32[] DrawTile(DecorationType type, DecorationStyleCategory style, int size, int variant)
        {
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            switch (type)
            {
                case DecorationType.Mountain: DrawMountain(px, size, style, variant); break;
                case DecorationType.Hill: DrawHill(px, size, style); break;
                case DecorationType.Pine: DrawPine(px, size, style); break;
                case DecorationType.AutumnTree: DrawBlobTree(px, size); break;
                case DecorationType.Mesa: DrawMesa(px, size); break;
            }
            return px;
        }

        static void Set(Color32[] px, int size, int x, int y, byte lum, byte a)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            px[y * size + x] = new Color32(lum, lum, lum, a);
        }

        // Тёмный контур + двухтоновый силуэт: левая грань светлее (свет слева).
        static void DrawMountain(Color32[] px, int size, DecorationStyleCategory style, int variant)
        {
            int baseY = size - 2;
            int peakX = size / 2 + (variant % 3 - 1) * size / 10; // лёгкий сдвиг пика по варианту
            int peakY = 2;
            float halfW = size * 0.42f;
            for (int y = peakY; y <= baseY; y++)
            {
                float t = (y - peakY) / (float)(baseY - peakY);
                int spread = Mathf.RoundToInt(halfW * t);
                for (int x = peakX - spread; x <= peakX + spread; x++)
                {
                    bool lit = x < peakX;                 // левая грань — освещённая
                    byte lum = (byte)(lit ? 210 : 120);
                    Set(px, size, x, y, lum, 255);
                }
                // контур
                Set(px, size, peakX - spread, y, 20, 255);
                Set(px, size, peakX + spread, y, 20, 255);
            }
            if (style == DecorationStyleCategory.Snowy)
                for (int y = peakY; y < peakY + size / 4; y++)
                {
                    float t = (y - peakY) / (float)(baseY - peakY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = peakX - spread; x <= peakX + spread; x++) Set(px, size, x, y, 245, 255);
                }
            if (style == DecorationStyleCategory.Forested) // тёмная «лесная» юбка снизу
                for (int y = baseY - size / 5; y <= baseY; y++)
                {
                    float t = (y - peakY) / (float)(baseY - peakY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = peakX - spread; x <= peakX + spread; x++) Set(px, size, x, y, 70, 255);
                }
        }

        static void DrawHill(Color32[] px, int size, DecorationStyleCategory style)
        {
            int cx = size / 2, baseY = size - 2;
            float r = size * 0.42f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - cx) / r, dy = (y - baseY) / r;
                if (dy <= 0 && dx * dx + dy * dy <= 1f)
                    Set(px, size, x, y, (byte)(x < cx ? 190 : 130), 255);
            }
        }

        static void DrawPine(Color32[] px, int size, DecorationStyleCategory style)
        {
            int cx = size / 2, baseY = size - 2;
            // трунк
            for (int y = baseY - size / 6; y <= baseY; y++) { Set(px, size, cx, y, 90, 255); Set(px, size, cx - 1, y, 90, 255); }
            // 3 яруса треугольников
            for (int tier = 0; tier < 3; tier++)
            {
                int topY = 2 + tier * (size / 4);
                int botY = topY + size / 3;
                float halfW = size * (0.36f - tier * 0.06f);
                for (int y = topY; y <= botY; y++)
                {
                    float t = (y - topY) / (float)(botY - topY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = cx - spread; x <= cx + spread; x++) Set(px, size, x, y, (byte)(x < cx ? 150 : 90), 255);
                }
            }
            if (style == DecorationStyleCategory.Snowy)
                for (int x = cx - 2; x <= cx + 2; x++) Set(px, size, x, 3, 245, 255);
        }

        static void DrawBlobTree(Color32[] px, int size)
        {
            int cx = size / 2, cy = size / 2 - 1, baseY = size - 2;
            for (int y = baseY - size / 6; y <= baseY; y++) { Set(px, size, cx, y, 90, 255); Set(px, size, cx - 1, y, 90, 255); }
            float r = size * 0.34f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r * r) Set(px, size, x, y, (byte)(x < cx ? 190 : 130), 255);
            }
        }

        static void DrawMesa(Color32[] px, int size)
        {
            int baseY = size - 2, topY = size / 2;
            for (int y = topY; y <= baseY; y++)
            {
                float t = (y - topY) / (float)(baseY - topY);
                int half = Mathf.RoundToInt(size * (0.22f + 0.14f * t));
                for (int x = size / 2 - half; x <= size / 2 + half; x++) Set(px, size, x, y, (byte)(x < size / 2 ? 175 : 120), 255);
            }
        }
    }
}
```

- [ ] **Step 2: Catalog — pack tiles into one atlas + expose UV-rects**

`Assets/WorldGen/Rendering/Decorations/DecorationCatalog.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Сменяемый источник арта: один RGBA32-атлас + UV-rect на (type, style, artVariant).
    /// v1 — процедурные плейсхолдеры; позже baker пакует готовые Sprite'ы, движок/рендерер те же.</summary>
    public class DecorationCatalog
    {
        public Texture2D Atlas { get; private set; }

        struct Slot { public int col, row; }
        readonly Dictionary<(DecorationType, DecorationStyleCategory), List<Slot>> slots = new();
        int cols, rows, tile;

        // Какие (type, style) существуют и сколько вариантов у каждого.
        static readonly (DecorationType t, DecorationStyleCategory s, int variants)[] Layout =
        {
            (DecorationType.Mountain, DecorationStyleCategory.Bare, 3),
            (DecorationType.Mountain, DecorationStyleCategory.Snowy, 3),
            (DecorationType.Mountain, DecorationStyleCategory.Forested, 3),
            (DecorationType.Hill, DecorationStyleCategory.Bare, 1),
            (DecorationType.Hill, DecorationStyleCategory.Snowy, 1),
            (DecorationType.Hill, DecorationStyleCategory.Forested, 1),
            (DecorationType.Pine, DecorationStyleCategory.Plain, 1),
            (DecorationType.Pine, DecorationStyleCategory.Snowy, 1),
            (DecorationType.AutumnTree, DecorationStyleCategory.Plain, 1),
            (DecorationType.Mesa, DecorationStyleCategory.Plain, 1),
        };

        public static DecorationCatalog BuildPlaceholder(int tile = 64)
        {
            var c = new DecorationCatalog { tile = tile };
            int total = 0;
            foreach (var l in Layout) total += l.variants;
            c.cols = Mathf.CeilToInt(Mathf.Sqrt(total));
            c.rows = Mathf.CeilToInt(total / (float)c.cols);
            c.Atlas = new Texture2D(c.cols * tile, c.rows * tile, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color32[c.cols * tile * c.rows * tile];
            c.Atlas.SetPixels32(clear);

            int idx = 0;
            foreach (var l in Layout)
            {
                var list = new List<Slot>();
                for (int v = 0; v < l.variants; v++)
                {
                    int col = idx % c.cols, row = idx / c.cols;
                    var tilePx = DecorationPlaceholderFactory.DrawTile(l.t, l.s, tile, v);
                    // Тайл рисуется в координатах «y вниз»; текстура — «y вверх». Флипаем по Y при заливке.
                    var flipped = new Color32[tile * tile];
                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile; x++)
                            flipped[(tile - 1 - y) * tile + x] = tilePx[y * tile + x];
                    c.Atlas.SetPixels32(col * tile, row * tile, tile, tile, flipped);
                    list.Add(new Slot { col = col, row = row });
                    idx++;
                }
                c.slots[(l.t, l.s)] = list;
            }
            c.Atlas.Apply(false);
            return c;
        }

        public int VariantCount(DecorationType t, DecorationStyleCategory s)
            => slots.TryGetValue((t, s), out var l) ? l.Count : 0;

        /// <summary>UV-rect (x,y = смещение, z,w = размер) для (type, style, artVariant).
        /// Fallback на первый существующий стиль типа, если (type,style) не в раскладке.</summary>
        public Vector4 UvRect(DecorationType t, DecorationStyleCategory s, int artVariant)
        {
            if (!slots.TryGetValue((t, s), out var list) || list.Count == 0)
            {
                // fallback: любой стиль этого типа
                foreach (var kv in slots) if (kv.Key.Item1 == t && kv.Value.Count > 0) { list = kv.Value; break; }
                if (list == null || list.Count == 0) return new Vector4(0, 0, 1f / cols, 1f / rows);
            }
            var slot = list[((artVariant % list.Count) + list.Count) % list.Count];
            return new Vector4(slot.col / (float)cols, slot.row / (float)rows, 1f / cols, 1f / rows);
        }
    }
}
```

- [ ] **Step 3: Add a catalog self-test**

Append to `DecorationSelfTests`:
```csharp
        [ContextMenu("Self-Test: Decoration Catalog")]
        public void SelfTestCatalog()
        {
            var cat = DecorationCatalog.BuildPlaceholder(48);
            bool ok = cat.Atlas != null;
            ok &= cat.VariantCount(DecorationType.Mountain, DecorationStyleCategory.Snowy) > 0;
            ok &= cat.VariantCount(DecorationType.Pine, DecorationStyleCategory.Plain) > 0;
            // UV-rect'ы в границах [0..1].
            var uv = cat.UvRect(DecorationType.Mountain, DecorationStyleCategory.Bare, 0);
            ok &= uv.x >= 0 && uv.y >= 0 && uv.x + uv.z <= 1.0001f && uv.y + uv.w <= 1.0001f;
            Debug.Log(ok ? "Self-Test Decoration Catalog: PASS" : "Self-Test Decoration Catalog: FAIL");
        }
```

- [ ] **Step 4: USER runs the catalog self-test**

Right-click `DecorationSelfTests` → **Self-Test: Decoration Catalog**. Expected: `PASS`.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations/DecorationPlaceholderFactory.cs Assets/WorldGen/Rendering/Decorations/DecorationCatalog.cs Assets/WorldGen/Rendering/Decorations/DecorationSelfTests.cs
git commit -m "feat(decorations): placeholder atlas + catalog (swappable art source) + self-test"
```

---

### Task 5: Decorations.shader (unlit, instanced, atlas + per-instance tint)

**Files:**
- Create: `Assets/WorldGen/Rendering/Decorations.shader`

**Interfaces:**
- Produces: shader `"WorldGen/Decorations"` with texture `_Atlas`, instanced props `_UVRect` (float4), `_Tint` (float4).

- [ ] **Step 1: Write the shader**

`Assets/WorldGen/Rendering/Decorations.shader`:
```hlsl
Shader "WorldGen/Decorations"
{
    Properties { _Atlas ("Atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _Atlas;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _UVRect)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 tint : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };

            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 r = UNITY_ACCESS_INSTANCED_PROP(Props, _UVRect);
                o.uv = r.xy + v.uv * r.zw;
                o.tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 c = tex2D(_Atlas, i.uv);
                // Плейсхолдеры серые (luminance) → красим per-instance tint'ом, храня затенение.
                c.rgb *= i.tint.rgb;
                clip(c.a - 0.01);
                return c;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: USER verifies compile + registers shader**

In the Editor, confirm the shader imports with no errors (select it, Inspector shows no compile error).
**Then add it to Project Settings → Graphics → Always Included Shaders** (drag `Decorations.shader` in) — else it strips from the build (Task 8 re-verifies).

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations.shader
git commit -m "feat(decorations): unlit instanced atlas shader (per-instance UV + tint)"
```

> USER: commit the generated `Decorations.shader.meta` after import (note its guid — needed for Always Included Shaders in Task 8).

---

### Task 6: DecorationRenderer (MonoBehaviour, instanced submit)

**Files:**
- Create: `Assets/WorldGen/Rendering/Decorations/DecorationRenderer.cs`

**Interfaces:**
- Consumes: `DecorationCatalog`, `DecorationInstance` list.
- Produces: `class DecorationRenderer : MonoBehaviour` with `void SetInstances(List<DecorationInstance> list)`, `bool Visible { get; set; }`, `float LayerY` (default 0.45).

- [ ] **Step 1: Write the renderer**

`Assets/WorldGen/Rendering/Decorations/DecorationRenderer.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Держит массив инстансов декораций и сабмитит их GPU-инстансингом каждый кадр
    /// (immediate-mode). Плоские квадры в XZ, семплят атлас каталога, тонируются per-instance.
    /// Без коллайдеров → некликабельно. Родитель — mapRenderer.transform (наследует его локальные коорд.).</summary>
    public class DecorationRenderer : MonoBehaviour
    {
        const int BatchMax = 1023;
        public float LayerY = 0.45f;
        public bool Visible = true;

        Mesh quad;
        Material material;
        DecorationCatalog catalog;
        List<DecorationInstance> instances = new();

        // Переиспользуемые буферы батча.
        readonly Matrix4x4[] mtx = new Matrix4x4[BatchMax];
        readonly Vector4[] uvRects = new Vector4[BatchMax];
        readonly Vector4[] tints = new Vector4[BatchMax];
        MaterialPropertyBlock mpb;

        void EnsureResources()
        {
            if (quad == null) quad = BuildQuad();
            if (material == null)
            {
                material = new Material(Shader.Find("WorldGen/Decorations"));
                if (material.shader == null)
                    Debug.LogError("[Decorations] Shader 'WorldGen/Decorations' not found — add it to Graphics → Always Included Shaders (else stripped from build).");
                material.enableInstancing = true;
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
        }

        // Квад в плоскости XZ, пивот низ-центр: локально X∈[-0.5,0.5], Z∈[0,1] (высота вверх по +Z «экрана»).
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "DecorationQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f), new Vector3(-0.5f, 0f, 1f),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        public void Init(DecorationCatalog cat) { catalog = cat; EnsureResources(); if (material != null) material.mainTexture = cat.Atlas; }

        public void SetInstances(List<DecorationInstance> list) { instances = list ?? new List<DecorationInstance>(); }

        void LateUpdate()
        {
            if (!Visible || catalog == null || material == null || instances.Count == 0) return;
            EnsureResources();
            material.mainTexture = catalog.Atlas;

            // Матрица инстанса: масштаб по scale, позиция = локальная (x, LayerY, z) в родителе (mapRenderer.transform).
            int i = 0;
            while (i < instances.Count)
            {
                int n = Mathf.Min(BatchMax, instances.Count - i);
                for (int b = 0; b < n; b++)
                {
                    var d = instances[i + b];
                    var local = new Vector3(d.worldPos.x, LayerY, d.worldPos.y);
                    var world = transform.localToWorldMatrix * Matrix4x4.TRS(local, Quaternion.identity, new Vector3(d.scale, d.scale, d.scale));
                    mtx[b] = world;
                    uvRects[b] = catalog.UvRect(d.type, d.style, d.artVariant);
                    tints[b] = (Color)d.tint;
                }
                mpb.Clear();
                mpb.SetVectorArray("_UVRect", TrimTo(uvRects, n));
                mpb.SetVectorArray("_Tint", TrimTo(tints, n));
                Graphics.DrawMeshInstanced(quad, 0, material, mtx, n, mpb);
                i += n;
            }
        }

        // SetVectorArray требует ровно count элементов (или фиксированный размер); отдаём срез.
        static readonly List<Vector4> tmp = new();
        static List<Vector4> TrimTo(Vector4[] src, int n)
        { tmp.Clear(); for (int k = 0; k < n; k++) tmp.Add(src[k]); return tmp; }

        void OnDestroy() { if (material != null) Destroy(material); if (quad != null) Destroy(quad); }
    }
}
```

- [ ] **Step 2: USER smoke-check (deferred to Task 7)**

The renderer needs cells/instances from `WorldMapRenderer` (Task 7) to show anything. Compile-check only here: confirm no errors in Console after import.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/Decorations/DecorationRenderer.cs
git commit -m "feat(decorations): GPU-instanced renderer (DrawMeshInstanced + per-instance UV/tint)"
```

> Note: `DrawMeshInstanced` + `MaterialPropertyBlock.SetVectorArray("_UVRect"/"_Tint")` feeds the shader's `UNITY_DEFINE_INSTANCED_PROP` props. If per-instance props don't vary in Editor, verify the shader has `#pragma multi_compile_instancing` and `material.enableInstancing = true` (both set above).

---

### Task 7: Integrate into WorldMapRenderer (placement + live edit + theme)

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `DecorationPlacer.Place/PlaceRect`, `DecorationRenderer`, `DecorationCatalog`, `DecorationConfig`; existing `cells`, `nearestLookup`, `seed`, `mapWidth`, `mapHeight`, `paletteTheme`, `EndBrushStroke()`, `RefreshAfterCellDataChange()`.
- Produces: `public DecorationConfig decorationConfig`; `void RebuildDecorations()`; `void RefreshDecorationsRect(Rect worldRect)`.

- [ ] **Step 1: Add fields + a container/renderer + rebuild methods**

Add near the other serialized fields (e.g. after `rasterLongSide`):
```csharp
        [Header("Декорации (iso-спрайты террейна)")]
        public WorldGen.Rendering.Decorations.DecorationConfig decorationConfig = new WorldGen.Rendering.Decorations.DecorationConfig();

        WorldGen.Rendering.Decorations.DecorationRenderer decorationRenderer;
        WorldGen.Rendering.Decorations.DecorationCatalog decorationCatalog;
        System.Collections.Generic.List<WorldGen.Rendering.Decorations.DecorationInstance> decorationInstances;
```

Add these methods (near `RebuildSpatialIndex`):
```csharp
        void EnsureDecorationRenderer()
        {
            if (decorationCatalog == null)
                decorationCatalog = WorldGen.Rendering.Decorations.DecorationCatalog.BuildPlaceholder();
            if (decorationRenderer == null)
            {
                var go = new GameObject("Decorations");
                go.transform.SetParent(transform, false); // локальные коорд. карты
                decorationRenderer = go.AddComponent<WorldGen.Rendering.Decorations.DecorationRenderer>();
                decorationRenderer.Init(decorationCatalog);
            }
        }

        /// <summary>Полная перерасстановка декораций из текущих клеток/сида/темы.</summary>
        public void RebuildDecorations()
        {
            EnsureDecorationRenderer();
            if (cells == null || nearestLookup == null) return;
            decorationInstances = WorldGen.Rendering.Decorations.DecorationPlacer.Place(
                cells, nearestLookup, seed, mapWidth, mapHeight, decorationConfig, paletteTheme);
            decorationRenderer.SetInstances(decorationInstances);
            decorationRenderer.Visible = decorationConfig.enabled;
        }

        /// <summary>Rect-scoped обновление: выкинуть инстансы в rect, дорасставить, ре-сортировать.</summary>
        public void RefreshDecorationsRect(Rect worldRect)
        {
            if (decorationInstances == null) { RebuildDecorations(); return; }
            EnsureDecorationRenderer();
            decorationInstances.RemoveAll(d => worldRect.Contains(d.worldPos));
            WorldGen.Rendering.Decorations.DecorationPlacer.PlaceRect(
                decorationInstances, nearestLookup, seed, mapWidth, mapHeight, decorationConfig, paletteTheme, worldRect);
            decorationInstances.Sort((a, b) => a.sortZ.CompareTo(b.sortZ));
            decorationRenderer.SetInstances(decorationInstances);
        }
```

- [ ] **Step 2: Call full rebuild after generation/load render**

In `FinishLoadFromCells()` (after `BuildBorders();`), add:
```csharp
            RebuildDecorations();
```
And at the end of the synchronous `RebakeAll()` path (find the method that regenerates for non-stepped/in-place regen — the one that calls `BuildBorders`/`OnWorldRegenerated`), add `RebuildDecorations();` so ContextMenu/regen paths also refresh. (Grep `OnWorldRegenerated?.Invoke` — add `RebuildDecorations();` immediately before each invoke that follows a full re-render.)

- [ ] **Step 3: Wire live edit on stroke-end**

In `EndBrushStroke()` (line ~538), after the existing finalize/label work, add a rect refresh over the stroke's touched area. Use the brush's dirty world-rect if available; otherwise refresh the whole map (safe fallback):
```csharp
            // Декорации: пересчитать затронутую область (или всю карту, если rect не отслеживается).
            RefreshDecorationsRect(new Rect(0, 0, mapWidth, mapHeight));
```
> If `BrushToolController`/`EndBrushStroke` already tracks a dirty world-rect (grep for a `Rect`/min-max the finalize uses), pass THAT rect instead of the full-map rect for cheaper updates. Full-map is correct but does more work.

Also add to `RefreshAfterCellDataChange()` (line ~2609), at the end:
```csharp
            RebuildDecorations();
```

- [ ] **Step 4: Live config edits in play mode**

Add/extend `OnValidate()` (create if absent) to refresh when sliders change in play mode:
```csharp
        void OnValidate()
        {
            if (Application.isPlaying && cells != null && nearestLookup != null) RebuildDecorations();
        }
```
(If `OnValidate` already exists, add the `RebuildDecorations()` guarded line into it rather than duplicating the method.)

- [ ] **Step 5: USER verifies in Editor (Play mode)**

Enter Play, generate a world. Expected: side-view mountains on high terrain (snowy where cold, forested over forests, bare otherwise), pines on cold forests, autumn trees on warm forests, mesas on badlands; sprites overlap correctly (nearer/south over farther/north). Toggle `decorationConfig.enabled` off → they vanish. Paint biomes/elevation with the brush → on mouse-release the touched area's decorations update. Adjust density/threshold sliders in play mode → decorations refresh.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(decorations): integrate placement into WorldMapRenderer (gen/load + stroke-end + live sliders)"
```

> USER: after Editor wiring, commit the modified scene if the Inspector persisted new serialized `decorationConfig` values.

---

### Task 8: Layers-panel toggle + Always Included Shaders + final review

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapLayersPanel.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`
- Modify: `ProjectSettings/GraphicsSettings.asset`

**Interfaces:**
- Consumes: `WorldMapRenderer.decorationConfig`, `RebuildDecorations`.
- Produces: `WorldMapRenderer.SetShowDecorations(bool on)`.

- [ ] **Step 1: Add the renderer toggle method**

In `WorldMapRenderer.cs`, mirror `SetShowBiomeLayer` (~line 2507):
```csharp
        public void SetShowDecorations(bool on)
        {
            decorationConfig.enabled = on;
            if (decorationRenderer != null) decorationRenderer.Visible = on;
            EnsureDecorationRenderer();
            if (on && (decorationInstances == null || decorationInstances.Count == 0)) RebuildDecorations();
        }
```

- [ ] **Step 2: Add the "Декорации" checkbox row in MapLayersPanel**

In `MapLayersPanel.cs`, find where the existing rows (Рельеф / Биом / Границы / Береговая линия) are built and their toggles wired to `mapRenderer.SetShow*`. Add a row identical in structure:
```csharp
            AddLayerRow("Декорации", mapRenderer != null && mapRenderer.decorationConfig.enabled,
                on => mapRenderer.SetShowDecorations(on));
```
(Use the panel's actual row-builder signature — grep for how "Рельеф"/`SetShowReliefLayer` row is created and copy that exact call shape.)

- [ ] **Step 3: Add Decorations shader to Always Included Shaders**

Get the shader guid: read `Assets/WorldGen/Rendering/Decorations.shader.meta` → `guid:` value. In `ProjectSettings/GraphicsSettings.asset`, under `m_AlwaysIncludedShaders:`, add a line after the last entry (mirror the MapTerrain fix):
```yaml
  - {fileID: 4800000, guid: <DECORATIONS_SHADER_GUID>, type: 3}
```

- [ ] **Step 4: USER verifies in Editor + local build**

- Project Settings → Graphics → Always Included Shaders shows `Decorations` resolved (not "missing"). If reverted (Unity was open), add it via the UI (drag `Decorations.shader`).
- MapLayersPanel shows a "Декорации" checkbox that hides/shows decorations.
- **Do a local File → Build**, run it, generate a world → decorations render in the BUILD (this is the exact bug class that hit MapTerrain — verify it does NOT recur).

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/MapLayersPanel.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs ProjectSettings/GraphicsSettings.asset
git commit -m "feat(decorations): layers-panel toggle + include shader in build (Always Included Shaders)"
```

---

## Self-Review

**Spec coverage:**
- Iso sprites (mountain/hill/pine/autumn/mesa) → Tasks 2–4 (classify/place/art). ✓
- Two-level variants (context styleCategory + hash artVariant) → Task 2 (`TryClassify` style) + Task 3 (`artVariant` hash). ✓
- Mountains Snowy/Forested/Bare by biome/temp → `ReliefStyle` (Task 2). ✓
- Deterministic placement + rect-scoped → Task 3 (`Place`/`PlaceRect`, hash) + self-tests. ✓
- Swappable catalog + placeholder atlas → Task 4. ✓
- GPU instancing + atlas + tint + sort → Tasks 5–6. ✓
- Live update on stroke-end → Task 7 Step 3. ✓
- Layer toggle → Task 8. ✓
- Cap + no silent truncation (LogWarning) → Task 3 `Place`. ✓
- Non-interactive (no colliders) → Task 6 (instanced draw, no GameObjects per sprite). ✓
- Persistence: derived, not saved → nothing added to serializer (correct; no task needed). ✓
- Shader-strip risk → Task 5 Step 2 + Task 8 Step 3. ✓
- Reeds deferred → not implemented (correct). ✓

**Placeholder scan:** No TBD/TODO. All code blocks complete. Integration steps that reference existing methods (`EndBrushStroke`, `OnWorldRegenerated`, MapLayersPanel row-builder) instruct grepping the exact call shape because those line numbers shift — acceptable (the code to add is given verbatim; only the insertion point is grep-located).

**Type consistency:** `DecorationInstance`/`DecorationType`/`DecorationStyleCategory`/`DecorationConfig` (Task 1) used consistently in Tasks 2–7. `DecorationPlacer.Place`/`PlaceRect`/`TryClassify`/`Hash` signatures match between definition (Tasks 2–3) and callers (Task 7). `DecorationCatalog.UvRect`/`VariantCount`/`BuildPlaceholder` match between Task 4 and Task 6. `DecorationRenderer.Init`/`SetInstances`/`Visible` match Task 6 ↔ Task 7. `SetShowDecorations` defined Task 8 Step 1, used Step 2. ✓

**Known verification gaps (user, Editor):** all `[ContextMenu]` tests + all visual/build checks are user-run (agents can't run Unity). The `VoronoiCell` fixture construction (Tasks 2–3) and `NearestCellLookup` ctor assume settable `Biome/Height/Temperature/IsOcean` and a `(int, System.Numerics.Vector2)` ctor — verify against `VoronoiCell.cs`/`NearestCellLookup.cs` at Task 2 Step 3 and adjust if the fields are computed.
