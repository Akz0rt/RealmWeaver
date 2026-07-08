# GPU-рендер террейна карты — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Особенность:** Фаза 2 (вид шейдера) — ИНТЕРАКТИВНАЯ. Точный вид настраивается человеком по скриншотам, а не слепым субагентом. Для шейдерных задач «проверка» = компилируется без ошибок + карта отображается + скриншот на сверку человеку. Не помечай такую задачу выполненной без визуального подтверждения человека.

**Goal:** Перенести попиксельную раскраску карты с CPU на GPU fragment-шейдер, чтобы правка кистью стала константной (<1 мс) при любом размере кисти, заменив CPU-путь ~160 мс/штамп.

**Architecture:** Карта = квадр с кастомным unlit fragment-шейдером. Cell-id текстура (`RFloat`, печётся один раз, не меняется при правке) + крошечная текстура атрибутов клеток (`RGBAFloat`, обновляется при правке) + coast-distance текстура (`RFloat`, пересчёт при смене топологии). Шейдер на каждый пиксель искажает координату шумом (органичные границы), читает cell-id → атрибуты клетки → считает цвет/рельеф/свечение. Правка = обновить текстуру атрибутов → GPU перерисовывает бесплатно.

**Tech Stack:** Unity 2022.3 LTS, Built-in Render Pipeline, кастомный unlit fragment-шейдер (ShaderLab + HLSL), текстуры `RFloat`/`RGBAFloat` (point-фильтр). Новых пакетов нет.

**Дизайн-спека:** `docs/superpowers/specs/2026-07-08-gpu-map-terrain-render-design.md`.

## Global Constraints

- Слой генерации (`Assets/WorldGen/Generation/`) — чистый C#, без зависимости от `UnityEngine`. Весь новый Unity-код — под `Assets/WorldGen/Rendering/`.
- New Input System (не легаси `UnityEngine.Input`).
- Self-тесты — по конвенции проекта: `[ContextMenu("Self-Test: ...")]` на `WorldMapRenderer`, строят маленькую фикстуру и `Debug.Log` PASS/FAIL (как `SelfTestChunkedBakeContinuity` и др.).
- `TextureFormat.RFloat`/`RGBAFloat`, `FilterMode.Point` для всех трёх карт-данных текстур (интерполяция cell-id/атрибутов недопустима — это дискретные значения по клеткам).
- Совместимость: только текстуры и uniform'ы, **без `StructuredBuffer`/compute** (Built-in RP, широкое железо).
- Не удалять CPU-путь (`MapRasterizer`, `CoastlineContour`) — он остаётся фолбэком за флагом до конца миграции (Task 12). Не трогать `WorldMaterial.mat`.
- Визуальная цель — эквивалентность или лучше нынешнего вида, НЕ пиксель-в-пиксель. Точные значения warp/шума/цвета настраиваются интерактивно.

## File overview

Новые (`Assets/WorldGen/Rendering/GpuMap/`):
- `CellIdTexture.cs` — строит `RFloat` текстуру cellId-на-пиксель из `NearestCellLookup` (чанкуемо).
- `CellAttributeTexture.cs` — `RGBAFloat` текстура атрибутов клеток; раскладка cellId→тексел, упаковка/обновление, CPU-зеркало.
- `CoastDistanceTexture.cs` — `RFloat` поле дистанции берега (обёртка над существующей chamfer-логикой → текстура).
- `GpuMapRenderer.cs` — управляет материалом/шейдером, тремя текстурами, uniform'ами; API: `BuildAll`, `UpdateCells`, `SetTopologyDirty`, `SetMode`, `SetLayers`.

Новый шейдер:
- `Assets/WorldGen/Rendering/MapTerrain.shader` — unlit fragment: domain warp, cell-id lookup, атрибуты, режимы, полный painted-конвейер.

Модифицируемые:
- `Assets/WorldGen/Rendering/WorldMapRenderer.cs` — снять инструментацию (Task 1); поле `useGpuRenderer` + делегирование в `GpuMapRenderer` (Task 4, 12); `RebakeAffectedCells`/undo → `GpuMapRenderer.UpdateCells` (Task 9); хит-тест уже через `NearestCellLookup` (Task 10 — проверка).
- `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`, `.../CoastlineContour.cs` — снять инструментацию (Task 1).
- `Assets/WorldGen/Rendering/MapScreenController.cs` — шаг «Отрисовка карты» ведёт к GPU-запечке (Task 12).

Не меняются: `MapPalette.cs`, `NearestCellLookup.cs`, `RegionColorPalette.cs`, `BiomeClassifier.cs`, `VoronoiCell.cs`, `CellSelectionController.cs`, `PoiInteractionController.cs`, `BrushToolController.cs` (вызывает `RebakeAffectedCells`, чья реализация меняется, а сигнатура — нет).

**Ключевые интерфейсы существующего кода (для справки исполнителям):**
- `VoronoiCell`: `int Id`, `System.Numerics.Vector2 Site`, `float EffectiveElevation`, `float EffectiveTemperature`, `bool EffectiveIsOcean`, `bool EffectiveIsLake`, `Biome Biome`, `int RegionId`.
- `NearestCellLookup(IEnumerable<VoronoiCell>, float bucketSize)`, `VoronoiCell FindNearest(System.Numerics.Vector2)`.
- `MapPalette.GetFamily(Biome) -> BiomeFamily`, `MapPalette.GetSlotColor(MapPaletteTheme, PaletteSlot) -> Color32`, enum `PaletteSlot` (28 слотов), `BiomeFamily` (11 значений).
- `WorldMapRenderer`: поля `cells`, `cellById`, `nearestLookup`, `mapWidth`, `mapHeight`, `texWidth`, `texHeight`, `minPointDistance`, `seed`, `paletteTheme`, `meshRenderer`; методы `ComputeTexSize(out int,out int)`, `RebakeAll()`, `RebakeAllStepped(Action<float>)`.

---

### Task 1: Снять инструментацию профилировки, забанить фиксы A+B

Инструментация из этой сессии (`debugBrushTiming`, `DebugTiming`, `Last*Ms`, замеры Stopwatch) — временная. Настоящие фиксы (`recomputeCellId` — Fix A, Y-фильтр рёбер `EdgesOverlappingY` — Fix B) остаются. Проверены визуально + 5 self-тестов.

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (убрать `debugBrushTiming`/`brushStampCount` поля и debug-ветку в `RebakeRegion`, оставив `MapRasterizer.RebakeRegion(..., recomputeCellId: false)`)
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs` (убрать `DebugTiming`/`LastApplyMs`/`LastLookupMs`/`LastTraceMs`/`LastRasterMs`/`LastCoastDistMs`/`MsSince` и все замеры `GetTimestamp`; оставить `recomputeCellId`-гейт и логику; `ApplyDarknessRect` — обычный `texture.Apply(false)`)
- Modify: `Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs` (без изменений — там только реальный Fix B, инструментации нет; проверить)

- [ ] **Step 1: Убрать инструментацию из `WorldMapRenderer.cs`**

Удалить поля (около строк 130-134):
```csharp
        // ВРЕМЕННОЕ: профилировка кисти. ...
        [Tooltip("ВРЕМЕННО: логировать разбивку времени перезапекания кисти в Console.")]
        public bool debugBrushTiming = true;
        int brushStampCount;
```
В `RebakeRegion(IEnumerable<VoronoiCell> touchedCells)` вернуть тело к:
```csharp
            ComputeTouchedPixelRect(touchedCells, out int rx, out int ry, out int rw, out int rh);
            if (rw <= 0 || rh <= 0) return;

            var config = BuildRasterConfig();
            // Кисть не двигает сайты Вороного - карта cellId в rect уже верна с прошлого полного
            // запека, поэтому recomputeCellId: false (см. BakeFieldsRect).
            MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, corners, displayMode, config, rasterTexture, rasterBuffers, rx, ry, rw, rh, recomputeCellId: false);
```

- [ ] **Step 2: Убрать инструментацию из `MapRasterizer.cs`**

Удалить блок статиков (`DebugTiming`, `LastApplyMs`, `LastLookupMs/TraceMs/RasterMs/CoastDistMs`, `MsSince`). В `BakeFieldsRect` убрать строку сброса `LastLookupMs = ... = 0;`, обёртки `GetTimestamp`/`MsSince` вокруг lookup-цикла (оставив сам `if (recomputeCellId) { for ... }`), трассировки, RasterizeIsLand и ComputeCoastDistanceRect. В `RasterizeSmoothedCategoryRect` убрать `tT`/`tR` замеры. В `ApplyDarknessRect` вернуть к:
```csharp
            texture.SetPixels32(rectX, rectY, rectW, rectH, outPixels);
            texture.Apply(false);
```

- [ ] **Step 3: Проверить компиляцию и self-тесты (человек, Editor открыт)**

В Editor дождаться перекомпиляции без ошибок. Правый клик по `WorldMapRenderer` → прогнать 5 тестов, ожидая PASS у каждого: `Self-Test: Coastline Contour Rasterize IsLand`, `Self-Test: Rasterize Region Label Writes Inside Only`, `Self-Test: Chunked Bake Continuity`, `Self-Test: Smoothed Category Single Region`, `Self-Test: Smoothed Flat Fill Interior Parity`.

- [ ] **Step 4: Коммит A+B**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs
git commit -m "perf(map-raster): skip cellId recompute on brush + Y-filter rasterizer edges

Fix A: brush edits don't move Voronoi sites, so the per-pixel nearest-cell
lookup is redundant on partial rebakes (recomputeCellId=false) - removes ~90-130ms/stamp.
Fix B: RasterizeIsLand/RegionLabel prefilter loop edges to the rect Y-span
instead of scanning all edges per scanline - ~7x on the raster pass.
Verified: 5 rasterizer self-tests pass, map visually identical."
```

- [ ] **Step 5: Обновить ledger**

Дописать в `.superpowers/sdd/progress.md`: строку про перф-сессию (root cause был НЕ trace как в гипотезе, а lookup+raster; A+B забанены; далее — GPU-редизайн по спеке 2026-07-08). Закоммитить ledger.

---

### Task 2: `CellIdTexture` — RFloat текстура «пиксель → cellId»

**Files:**
- Create: `Assets/WorldGen/Rendering/GpuMap/CellIdTexture.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (self-тест)

**Interfaces:**
- Consumes: `NearestCellLookup.FindNearest`, `VoronoiCell.Id`.
- Produces: `CellIdTexture.Build(NearestCellLookup lookup, int texW, int texH, float mapW, float mapH) -> Texture2D` (`RFloat`, `FilterMode.Point`, значение пикселя = `(float)cellId`); `CellIdTexture.BuildStepped(...) -> IEnumerator` (чанки по строкам, `onProgress`). Потребляется `GpuMapRenderer` (Task 4) и шейдером.

- [ ] **Step 1: Написать `CellIdTexture.cs`**

```csharp
using System.Collections;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RFloat текстура: значение пикселя = (float)cellId ближайшей клетки к центру пикселя.
    /// Печётся ОДИН РАЗ при генерации/загрузке - при правке кистью не меняется (сайты Вороного
    /// неподвижны). Point-фильтр: cellId - дискретная метка, интерполировать нельзя.</summary>
    public static class CellIdTexture
    {
        public static Texture2D Build(NearestCellLookup lookup, int texW, int texH, float mapW, float mapH)
        {
            var tex = NewTex(texW, texH);
            Fill(tex, lookup, texW, texH, mapW, mapH, 0, texH);
            tex.Apply(false);
            return tex;
        }

        public static IEnumerator BuildStepped(NearestCellLookup lookup, int texW, int texH, float mapW, float mapH,
            System.Action<Texture2D> onDone, System.Action<float> onProgress)
        {
            var tex = NewTex(texW, texH);
            const int chunkRows = 64;
            for (int y0 = 0; y0 < texH; y0 += chunkRows)
            {
                int rows = Mathf.Min(chunkRows, texH - y0);
                Fill(tex, lookup, texW, texH, mapW, mapH, y0, rows);
                onProgress?.Invoke((y0 + rows) / (float)texH);
                yield return null;
            }
            tex.Apply(false);
            onDone?.Invoke(tex);
        }

        static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RFloat, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        static void Fill(Texture2D tex, NearestCellLookup lookup, int texW, int texH, float mapW, float mapH, int y0, int rows)
        {
            var px = new Color[texW * rows];
            for (int y = 0; y < rows; y++)
            {
                float wz = (y0 + y + 0.5f) / texH * mapH;
                for (int x = 0; x < texW; x++)
                {
                    float wx = (x + 0.5f) / texW * mapW;
                    var cell = lookup.FindNearest(new System.Numerics.Vector2(wx, wz));
                    px[y * texW + x] = new Color(cell != null ? cell.Id : -1f, 0, 0, 0);
                }
            }
            tex.SetPixels(0, y0, texW, rows, px);
        }
    }
}
```

- [ ] **Step 2: Self-тест в `WorldMapRenderer.cs`**

Рядом с другими `[ContextMenu]` self-тестами:
```csharp
        [ContextMenu("Self-Test: GPU CellId Texture")]
        public void SelfTestGpuCellIdTexture()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(2.5f, 2.5f));
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7.5f, 7.5f));
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(
                new System.Collections.Generic.List<VoronoiCell> { a, b }, 5f);
            var tex = WorldGen.Rendering.GpuMap.CellIdTexture.Build(lookup, 10, 10, 10f, 10f);

            // Пиксель (2,2) → мир (2.5,2.5) → клетка a (Id 0); (7,7) → (7.5,7.5) → b (Id 1).
            int id00 = Mathf.RoundToInt(tex.GetPixel(2, 2).r);
            int id11 = Mathf.RoundToInt(tex.GetPixel(7, 7).r);
            bool ok = id00 == 0 && id11 == 1;
            Destroy(tex);
            Debug.Log(ok ? "Self-Test GPU CellId Texture: PASS" : $"Self-Test GPU CellId Texture: FAIL (id00={id00}, id11={id11})");
        }
```

- [ ] **Step 3: Проверить компиляцию + прогнать self-тест (человек)** — ожидать `PASS`.

- [ ] **Step 4: Коммит**

```bash
git add Assets/WorldGen/Rendering/GpuMap/CellIdTexture.cs Assets/WorldGen/Rendering/GpuMap/CellIdTexture.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): add CellIdTexture (RFloat pixel->cellId baker, chunkable)"
```

---

### Task 3: `CellAttributeTexture` — RGBAFloat атрибуты клеток

**Files:**
- Create: `Assets/WorldGen/Rendering/GpuMap/CellAttributeTexture.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (self-тест)

**Interfaces:**
- Consumes: `VoronoiCell.{Id,EffectiveElevation,EffectiveTemperature,EffectiveIsOcean,EffectiveIsLake,Biome,RegionId}`, `MapPalette.GetFamily`.
- Produces: класс `CellAttributeTexture` с `Texture2D Texture`, `int Width`, конструктор `CellAttributeTexture(IReadOnlyList<VoronoiCell> cells)`, методы `void UpdateCell(VoronoiCell cell)`, `void UpdateCells(IEnumerable<VoronoiCell>)`, `void Apply()`, `void Rebuild(IReadOnlyList<VoronoiCell>)`. Раскладка: cellId → тексел `(cellId % Width, cellId / Width)`; канал A-тексела: R=family, G=elevation, B=temperature, A=waterType (0=суша,1=океан,2=озеро). Второй тексел (в той же текстуре, ряд ниже — 2 тексела на клетку) хранит regionId. Потребляется `GpuMapRenderer` и шейдером.

- [ ] **Step 1: Написать `CellAttributeTexture.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RGBAFloat текстура атрибутов клеток, индекс = cellId. Крошечная (~2 тексела/клетку),
    /// перезаливается целиком при правке (<0.1мс). 2 тексела на клетку: слот A (family, elevation,
    /// temperature, waterType) в строке cellId; слот B (regionId,...) в строке cellId+cellRows.
    /// Point-фильтр. Соответствие раскладки — с MapTerrain.shader (Task 4).</summary>
    public class CellAttributeTexture
    {
        public Texture2D Texture { get; private set; }
        public int Width { get; private set; }
        int cellRows;               // строк на один "слот" (высота = cellRows*2)
        Color[] pixels;
        int cellCount;

        public CellAttributeTexture(IReadOnlyList<VoronoiCell> cells) => Rebuild(cells);

        public void Rebuild(IReadOnlyList<VoronoiCell> cells)
        {
            cellCount = cells.Count;
            Width = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(cellCount)));
            cellRows = Mathf.CeilToInt(cellCount / (float)Width);
            int h = cellRows * 2;   // 2 слота
            if (Texture != null) Object.Destroy(Texture);
            Texture = new Texture2D(Width, h, TextureFormat.RGBAFloat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color[Width * h];
            foreach (var cell in cells) Write(cell);
            Apply();
        }

        void Write(VoronoiCell cell)
        {
            int id = cell.Id;
            int x = id % Width, y = id / Width;
            float waterType = cell.EffectiveIsLake ? 2f : (cell.EffectiveIsOcean ? 1f : 0f);
            pixels[y * Width + x] = new Color(
                (float)MapPalette.GetFamily(cell.Biome),
                cell.EffectiveElevation,
                cell.EffectiveTemperature,
                waterType);
            int yB = (cellRows + y);
            pixels[yB * Width + x] = new Color(cell.RegionId, 0, 0, 0);
        }

        public void UpdateCell(VoronoiCell cell) => Write(cell);
        public void UpdateCells(IEnumerable<VoronoiCell> cells) { foreach (var c in cells) Write(c); }
        public void Apply() { Texture.SetPixels(pixels); Texture.Apply(false); }
        public int CellRows => cellRows;
    }
}
```

- [ ] **Step 2: Self-тест в `WorldMapRenderer.cs`**

```csharp
        [ContextMenu("Self-Test: GPU Attribute Texture")]
        public void SelfTestGpuAttributeTexture()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(1, 1))
                { Biome = Biome.Grassland, Height = 0.4f, Temperature = 0.6f, IsOcean = false, RegionId = 3 };
            var b = new VoronoiCell(1, new System.Numerics.Vector2(2, 2))
                { Biome = Biome.Ocean, Height = 0.0f, Temperature = 0.2f, IsOcean = true, RegionId = -1 };
            var attr = new WorldGen.Rendering.GpuMap.CellAttributeTexture(
                new System.Collections.Generic.List<VoronoiCell> { a, b });

            int w = attr.Width;
            Color a0 = attr.Texture.GetPixel(0 % w, 0 / w);        // cell 0 slot A
            Color b0 = attr.Texture.GetPixel(1 % w, 1 / w);        // cell 1 slot A
            Color a1 = attr.Texture.GetPixel(0 % w, attr.CellRows + 0 / w); // cell 0 slot B (regionId)

            bool ok = Mathf.Approximately(a0.g, 0.4f) && Mathf.Approximately(a0.b, 0.6f)
                      && Mathf.Approximately(a0.a, 0f)   // суша
                      && Mathf.Approximately(b0.a, 1f)   // океан
                      && Mathf.RoundToInt(a1.r) == 3;
            Debug.Log(ok ? "Self-Test GPU Attribute Texture: PASS" : $"Self-Test GPU Attribute Texture: FAIL (a0={a0}, b0={b0}, region={a1.r})");
        }
```

- [ ] **Step 3: Компиляция + self-тест (человек)** — ожидать `PASS`.

- [ ] **Step 4: Коммит**

```bash
git add Assets/WorldGen/Rendering/GpuMap/CellAttributeTexture.cs Assets/WorldGen/Rendering/GpuMap/CellAttributeTexture.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): add CellAttributeTexture (RGBAFloat per-cell attrs, live update)"
```

---

### Task 4: `MapTerrain.shader` (минимальный) + `GpuMapRenderer` — карта отображается плоским цветом семейства

Первая видимая веха: карта рендерится шейдером (пока плоский цвет семейства биома, без эффектов), правка мгновенная.

**Files:**
- Create: `Assets/WorldGen/Rendering/MapTerrain.shader`
- Create: `Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (поле `useGpuRenderer` + вызов `GpuMapRenderer` из генерации/загрузки за флагом)

**Interfaces:**
- Consumes: `CellIdTexture` (Task 2), `CellAttributeTexture` (Task 3), `MapPalette.GetSlotColor`.
- Produces: `GpuMapRenderer` (MonoBehaviour) с `void BuildAll(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup, int texW, int texH, float mapW, float mapH, MapPaletteTheme theme)`, `void UpdateCells(IEnumerable<VoronoiCell>)`, `Material Material`. Шейдер `MapTerrain` с свойствами `_CellIdTex`, `_AttrTex`, `_AttrWidth`, `_CellRows`, `_Palette` (массив цветов), `_MapSize`, `_Mode`.

- [ ] **Step 1: Написать `MapTerrain.shader` (минимальная версия)**

```shaderlab
Shader "WorldGen/MapTerrain"
{
    Properties
    {
        _CellIdTex ("Cell Id", 2D) = "black" {}
        _AttrTex ("Attributes", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CellIdTex;
            sampler2D _AttrTex;
            float _AttrWidth;
            float _CellRows;
            float4 _Palette[16];   // индекс = BiomeFamily (0..10), плоский цвет семейства
            float2 _MapSize;
            float _Mode;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            // тексел атрибутов клетки cid: слот 0 = A (family,elev,temp,water), слот 1 = B (region)
            float4 attr(int cid, int slot)
            {
                int x = cid % (int)_AttrWidth;
                int y = cid / (int)_AttrWidth + slot * (int)_CellRows;
                float2 uv = float2((x + 0.5) / _AttrWidth, (y + 0.5) / (_CellRows * 2.0));
                return tex2Dlod(_AttrTex, float4(uv, 0, 0));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int cid = (int)(tex2Dlod(_CellIdTex, float4(i.uv, 0, 0)).r + 0.5);
                if (cid < 0) return fixed4(0, 0, 0, 1);
                float4 a = attr(cid, 0);
                int family = (int)(a.r + 0.5);
                return fixed4(_Palette[family].rgb, 1);
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: Написать `GpuMapRenderer.cs` (минимальная версия)**

```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>Управляет GPU-рендером карты: cell-id + атрибуты + материал MapTerrain.
    /// Правка = UpdateCells → перезалить атрибуты → GPU перерисует бесплатно.</summary>
    public class GpuMapRenderer : MonoBehaviour
    {
        public Material Material { get; private set; }
        Texture2D cellIdTex;
        CellAttributeTexture attr;
        MeshRenderer meshRenderer;

        void EnsureMaterial()
        {
            if (Material != null) return;
            Material = new Material(Shader.Find("WorldGen/MapTerrain"));
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.material = Material;
        }

        public void BuildAll(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup,
            int texW, int texH, float mapW, float mapH, MapPaletteTheme theme)
        {
            EnsureMaterial();
            if (cellIdTex != null) Destroy(cellIdTex);
            cellIdTex = CellIdTexture.Build(lookup, texW, texH, mapW, mapH);
            attr = new CellAttributeTexture(cells);

            Material.SetTexture("_CellIdTex", cellIdTex);
            Material.SetTexture("_AttrTex", attr.Texture);
            Material.SetFloat("_AttrWidth", attr.Width);
            Material.SetFloat("_CellRows", attr.CellRows);
            Material.SetVector("_MapSize", new Vector4(mapW, mapH, 0, 0));
            Material.SetFloat("_Mode", 3); // Combined
            UploadPalette(theme);
        }

        void UploadPalette(MapPaletteTheme theme)
        {
            var arr = new Vector4[16];
            for (int f = 0; f < 11; f++)
            {
                // Sea/Lake семейств нет плоского слота - берём Coast как заглушку (вода красится отдельно позже).
                var family = (BiomeFamily)f;
                Color32 c = (family == BiomeFamily.Sea || family == BiomeFamily.Lake)
                    ? MapPalette.GetSlotColor(theme, PaletteSlot.Coast)
                    : MapPalette.GetSlotColor(theme, family);
                arr[f] = new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            }
            Material.SetVectorArray("_Palette", arr);
        }

        public void UpdateCells(IEnumerable<VoronoiCell> cells)
        {
            if (attr == null) return;
            attr.UpdateCells(cells);
            attr.Apply();
        }

        void OnDestroy()
        {
            if (cellIdTex != null) Destroy(cellIdTex);
            if (Material != null) Destroy(Material);
        }
    }
}
```

- [ ] **Step 3: Подключить за флагом в `WorldMapRenderer.cs`**

Добавить поле рядом с `rasterLongSide`:
```csharp
        [Header("GPU-рендер (эксперимент)")]
        [Tooltip("Рисовать карту GPU-шейдером (MapTerrain) вместо CPU-запечки текстуры.")]
        public bool useGpuRenderer = true;
        GpuMap.GpuMapRenderer gpuRenderer;
```
В `Awake()` после `EnsureRasterMaterial();`:
```csharp
            if (useGpuRenderer)
                gpuRenderer = gameObject.GetComponent<GpuMap.GpuMapRenderer>()
                              ?? gameObject.AddComponent<GpuMap.GpuMapRenderer>();
```
В `RebakeAll()` (около строки 2155) в самом начале — если GPU-режим, строить через GPU и выйти:
```csharp
            if (useGpuRenderer && gpuRenderer != null)
            {
                ComputeTexSize(out texWidth, out texHeight);
                gpuRenderer.BuildAll(cells, nearestLookup, texWidth, texHeight, mapWidth, mapHeight, paletteTheme);
                return;
            }
```
(Полное разведение GPU/CPU по всем точкам — Task 12; здесь достаточно, чтобы генерация/загрузка вызвали GPU-путь.)

- [ ] **Step 4: Проверить визуально (человек, Play mode)**

Перекомпилировать, войти в Play, сгенерировать карту. Ожидать: **карта отображается плоскими цветами семейств биомов** (тёмная палитра, без рельефа/границ/свечения — это норм для этой вехи). Сделать скриншот на сверку. Проверить, что нет ошибок компиляции шейдера (Console). Если карта чёрная — проверить, что квадр-меш есть и `_CellIdTex`/`_AttrTex` назначены.

- [ ] **Step 5: Коммит** (после визуального ОК человека)

```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/MapTerrain.shader.meta Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): minimal MapTerrain shader + GpuMapRenderer (flat family color)"
```

---

### Task 5: Шейдер — шум, domain warp (органичные границы), тёмная обводка берега

**ИНТЕРАКТИВНАЯ.** Проверка = компилируется + отображается + скриншот; параметры warp настраиваются с человеком.

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapTerrain.shader`
- Modify: `Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs` (uniform'ы warp + seed)

**Interfaces:**
- Produces: HLSL `hash/valueNoise/fbm`, `warpUV`; свойства `_WarpAmount`, `_WarpScale`, `_Seed`, `_OutlineColor`.

- [ ] **Step 1: Добавить в HLSL шейдера функции шума и warp**

Вставить перед `frag`:
```hlsl
            float _WarpAmount;
            float _WarpScale;
            float _Seed;
            float4 _OutlineColor;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1)), d = hash21(i + float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p)
            {
                float s = 0, amp = 0.5, freq = 1;
                for (int k = 0; k < 4; k++) { s += amp * vnoise(p * freq); freq *= 2; amp *= 0.5; }
                return s;
            }
            float2 warpUV(float2 uv)
            {
                float2 n = float2(fbm(uv * _WarpScale + _Seed), fbm(uv * _WarpScale + _Seed + 37.0));
                return uv + (n - 0.5) * _WarpAmount;
            }
            int cellAt(float2 uv)
            {
                return (int)(tex2Dlod(_CellIdTex, float4(uv, 0, 0)).r + 0.5);
            }
```
В `frag` заменить чтение cid на искажённую координату и добавить детект границы:
```hlsl
                float2 wuv = warpUV(i.uv);
                int cid = cellAt(wuv);
                if (cid < 0) return fixed4(0,0,0,1);
                float4 a = attr(cid, 0);
                int family = (int)(a.r + 0.5);
                float3 col = _Palette[family].rgb;

                // тёмная обводка на границе клеток (сосед по искажённой uv отличается)
                float2 px = 1.0 / _ScreenParams.xy; // приблизительно; тонкая линия
                int cN = cellAt(wuv + float2(0, px.y));
                int cE = cellAt(wuv + float2(px.x, 0));
                if (cN != cid || cE != cid) col = lerp(col, _OutlineColor.rgb, 0.6);
                return fixed4(col, 1);
```

- [ ] **Step 2: Uniform'ы в `GpuMapRenderer.BuildAll`**

Добавить (значения — стартовые, крутятся интерактивно):
```csharp
            Material.SetFloat("_WarpAmount", 0.01f);
            Material.SetFloat("_WarpScale", 8f);
            Material.SetFloat("_Seed", 0f);
            var outline = MapPalette.GetSlotColor(theme, PaletteSlot.Outline);
            Material.SetColor("_OutlineColor", new Color(outline.r/255f, outline.g/255f, outline.b/255f, 1f));
```

- [ ] **Step 3: Визуальная настройка (человек, Play mode)**

Скриншот. Ожидать: границы клеток/биомов/берега стали **извилистыми** (не прямые грани Вороного), тонкая тёмная обводка. Совместно подобрать `_WarpAmount`/`_WarpScale` (человек меняет в Inspector материала или называет значения). Зафиксировать понравившиеся значения как стартовые в коде.

- [ ] **Step 4: Коммит** (после ОК)

```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs
git commit -m "feat(gpu-map): noise domain-warp organic borders + coast outline in shader"
```

---

### Task 6: Шейдер — ступени высоты + рельефное затенение + холодный подсвет

**ИНТЕРАКТИВНАЯ.**

**Files:** Modify `MapTerrain.shader`, `GpuMapRenderer.cs`.

**Interfaces:** свойства `_ElevBands`, `_BandContrast`, `_ReliefStrength`, `_LightAzimuth`, `_ReliefAmbient`, `_ColdLight`, `_LightColor`.

- [ ] **Step 1: HLSL — читать высоту клетки, полосу, hillshade**

В `frag` после базового цвета семейства (для суши, `a.a < 0.5`):
```hlsl
                float elev = a.g;
                // ступень высоты
                int bands = (int)_ElevBands;
                int band = clamp((int)(elev * bands), 0, bands - 1);
                float t = band / max(1.0, bands - 1.0);
                col *= 1.0 + (t - 0.5) * (_BandContrast / 100.0);

                // hillshade из градиента высоты соседних клеток
                float eL = attr(cellAt(wuv - float2(px.x,0)), 0).g;
                float eR = attr(cellAt(wuv + float2(px.x,0)), 0).g;
                float eD = attr(cellAt(wuv - float2(0,px.y)), 0).g;
                float eU = attr(cellAt(wuv + float2(0,px.y)), 0).g;
                float gx = (eL - eR) * 0.5, gy = (eD - eU) * 0.5;
                float3 n = normalize(float3(-gx * _ReliefStrength, 1, -gy * _ReliefStrength));
                float az = radians(_LightAzimuth);
                float3 L = normalize(float3(sin(az), 1, cos(az)));
                float ndotl = saturate(dot(n, L));
                float bright = lerp(_ReliefAmbient, 1.0, ndotl);
                col = col * bright + _LightColor.rgb * ndotl * _ColdLight;
```
(Гейт по слою рельефа добавится в Task 9 через `_ShowRelief`.)

- [ ] **Step 2: Uniform'ы в `GpuMapRenderer`** (стартовые из нынешних дефолтов CPU):
```csharp
            Material.SetFloat("_ElevBands", 5);
            Material.SetFloat("_BandContrast", 40f);
            Material.SetFloat("_ReliefStrength", 3f);
            Material.SetFloat("_LightAzimuth", 315f);
            Material.SetFloat("_ReliefAmbient", 0.5f);
            Material.SetFloat("_ColdLight", 0.25f);
            var light = MapPalette.GetSlotColor(theme, PaletteSlot.Light);
            Material.SetColor("_LightColor", new Color(light.r/255f, light.g/255f, light.b/255f, 1f));
```

- [ ] **Step 3: Визуал (человек)** — ожидать рельефную «выпуклость» гор, ступени высоты. Настроить силу. Скриншот.

- [ ] **Step 4: Коммит**
```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs
git commit -m "feat(gpu-map): elevation bands + hillshade + cold moonlight in shader"
```

---

### Task 7: Шейдер — тонировка по температуре + зерно + виньетка + вода (глубина)

**ИНТЕРАКТИВНАЯ.**

**Files:** Modify `MapTerrain.shader`, `GpuMapRenderer.cs`.

**Interfaces:** свойства `_TintCool`, `_TintWarm`, `_Darkness`, `_GrainAmount`, `_SeaShallow`, `_SeaDeep`, `_LakeShallow`, `_LakeDeep`.

- [ ] **Step 1: HLSL — вода отдельной веткой, тонировка/зерно/виньетка общим финалом**

Для воды (`a.a > 0.5`): цвет = lerp(shallow, deep) по elevation (или по будущему coast-distance для глубины); озеро/океан по `a.a`. Для суши: региональная тонировка к `_TintCool/_TintWarm` по температуре `a.b`, вес ~0.38. Общий финал (суша и вода): зерно `(vnoise(i.uv*grainScale)-0.5)*_GrainAmount`, затем виньетка:
```hlsl
                float2 d = i.uv - 0.5;
                float vign = 1.0 - saturate(length(d) / 0.5) * saturate(_Darkness / 100.0);
                col *= vign;
                return fixed4(col, 1);
```

- [ ] **Step 2: Uniform'ы** — палитровые слоты `Shallow/Abyss/LakeS/LakeD/TintCool/TintWarm`, `_Darkness=72`, `_GrainAmount≈0.03`.

- [ ] **Step 3: Визуал (человек)** — ожидать **визуальную эквивалентность нынешнему виду** (вода с глубиной, тонировка, зерно, тёмные края). Совместная финальная настройка. Скриншот-сравнение с CPU-режимом (переключить `useGpuRenderer` для сравнения).

- [ ] **Step 4: Коммит**
```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs
git commit -m "feat(gpu-map): temperature tint + grain + vignette + water depth in shader"
```

---

### Task 8: Coast-distance текстура → широкое свечение берега

**Files:**
- Create: `Assets/WorldGen/Rendering/GpuMap/CoastDistanceTexture.cs`
- Modify: `GpuMapRenderer.cs`, `MapTerrain.shader`, `WorldMapRenderer.cs` (self-тест)

**Interfaces:**
- Produces: `CoastDistanceTexture.Build(NearestCellLookup lookup, IReadOnlyList<VoronoiCell> cells, int texW, int texH, float mapW, float mapH, float maxDist) -> Texture2D` (`RFloat`: 0 на суше, расстояние в пикселях до берега на воде, клампнуто `maxDist`). Внутри — тот же chamfer-подход, что `MapRasterizer.ComputeCoastDistanceRect`, но по land-маске из `lookup.FindNearest(...).EffectiveIsOcean/IsLake`. Шейдер-свойство `_CoastTex`, `_GlowWidth`, `_GlowColor`.

- [ ] **Step 1: Написать `CoastDistanceTexture.cs`** — двухпроходный chamfer по land-маске (land = !ocean && !lake), 0 на суше, dist на воде; вернуть `RFloat` текстуру. (Портировать инициализацию + 2 прохода из `ComputeCoastDistanceRect`; land-маску получить из `CellIdTexture` значений или напрямую `lookup.FindNearest`.)

- [ ] **Step 2: Self-тест** — маленькая карта суша/вода, проверить: пиксель на суше = 0, пиксель у берега со стороны воды < пикселя дальше в воду. `Debug.Log PASS/FAIL`.

- [ ] **Step 3: Шейдер — свечение** — на воде: `glow = saturate(1 - coastDist / _GlowWidth); col = lerp(col, _GlowColor.rgb, glow * k)`.

- [ ] **Step 4: `GpuMapRenderer.BuildAll`** строит и назначает `_CoastTex`, `_GlowWidth=16`, `_GlowColor` из `PaletteSlot.Glow`.

- [ ] **Step 5: Визуал (человек)** — ожидать мягкий светлый ореол вдоль берега со стороны воды. Скриншот. Коммит.

```bash
git add Assets/WorldGen/Rendering/GpuMap/CoastDistanceTexture.cs Assets/WorldGen/Rendering/GpuMap/CoastDistanceTexture.cs.meta Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): coast-distance texture + wide coastline glow in shader"
```

---

### Task 9: Правка кистью/undo обновляют атрибуты; режимы/слои — uniform'ы

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (`RebakeAffectedCells`, undo, смена режима/слоя → GPU); `GpuMapRenderer.cs` (`SetMode`, `SetLayers`).

**Interfaces:**
- Produces: `GpuMapRenderer.SetMode(int mode)`, `GpuMapRenderer.SetLayers(bool biome, bool relief)` → `Material.SetFloat("_Mode"/"_ShowBiome"/"_ShowRelief", ...)`. Шейдер ветвит по `_Mode` (0 Высота,1 Регион,2 Биом,3 Комбинированный) и гейтит рельеф/семейство по `_ShowBiome/_ShowRelief`.

- [ ] **Step 1: `WorldMapRenderer.RebakeAffectedCells`** в GPU-режиме → `gpuRenderer.UpdateCells(touchedCells)` вместо CPU `RebakeRegion`.
- [ ] **Step 2: Undo/override-пути** (`UndoAllBrushStrokes`, `CellOverrideService.Adjust*` вызовы) в GPU-режиме → `gpuRenderer.UpdateCells(...)`.
- [ ] **Step 3: Смена displayMode / слоёв** (`SetDisplayMode`, `SetShowBiomeLayer`, `SetShowReliefLayer`) в GPU-режиме → `gpuRenderer.SetMode/SetLayers` (мгновенно, без перезапека). Реализовать ветки `_Mode` в шейдере (Высота=градиент elevation; Регион=hash(regionId); Биом=плоский family; Комбинированный=полный).
- [ ] **Step 4: Визуал (человек)** — красить кистью (E/T/M, биом): карта **обновляется мгновенно при любом размере кисти**; undo работает; переключение режимов Высота/Регион/Биом/Комбинированный мгновенное. Замерить fps (должно быть fps монитора). Скриншоты. Коммит.

---

### Task 10: Хит-тест кисти/POI через `NearestCellLookup` (проверка)

Хит-тест уже использует `TryGetSiteHitPoint` (raycast квадра) + поиск клетки. Убедиться, что он не зависит от CPU `buffers.CellId` и корректен с GPU-картой.

**Files:** Modify `WorldMapRenderer.cs` (при необходимости — `TryGetSiteHitPoint`/`GetCellById` брать клетку через `nearestLookup.FindNearest`, не через `rasterBuffers.CellId`).

- [ ] **Step 1:** Найти все чтения `rasterBuffers.CellId` в хит-тесте; если есть — заменить на `nearestLookup.FindNearest(worldPoint)`.
- [ ] **Step 2: Визуал (человек)** — в GPU-режиме: кисть попадает точно под курсор; POI ставится/выделяется корректно; «точное выделение» работает. Скриншоты. Коммит.

---

### Task 11: Coast-distance пересчёт при смене топологии суша/вода

**Files:** Modify `WorldMapRenderer.cs`/`GpuMapRenderer.cs`.

**Interfaces:** `GpuMapRenderer.UpdateCells(cells, bool topologyMaybeChanged)` — если true (или отслеживать смену `EffectiveIsOcean/IsLake` до/после), пересчитать `CoastDistanceTexture` и перезалить `_CoastTex`.

- [ ] **Step 1:** В `RebakeAffectedCells` определять, изменился ли статус суша/вода у затронутых клеток (сравнить до/после правки). Если да — `gpuRenderer` пересчитывает coast-distance и заливает.
- [ ] **Step 2: Визуал (человек)** — поднять сушу из моря / залить водный биом кистью: свечение берега корректно перестраивается; обычная покраска (без смены топологии) остаётся мгновенной. Скриншоты. Коммит.

---

### Task 12: Развести GPU/CPU по всем точкам; экран генерации → GPU; фолбэк-флаг

**Files:** Modify `WorldMapRenderer.cs`, `MapScreenController.cs`.

- [ ] **Step 1:** Все точки, где сейчас вызывается `RebakeAll`/`RebakeAllStepped`/`RebakeRegion` (генерация, загрузка, смена палитры/darkness, экран генерации), в GPU-режиме идут через `GpuMapRenderer`; CPU-путь остаётся при `useGpuRenderer=false`.
- [ ] **Step 2:** `MapScreenController` шаг «Отрисовка карты» — чанковая `CellIdTexture.BuildStepped` с прогрессом (вместо `RebakeAllStepped`) в GPU-режиме.
- [ ] **Step 3:** Проверить (человек): полная генерация с прогресс-баром; сохранение/загрузка `.dndproj`; смена темы палитры (перезалить `_Palette`). Переключить `useGpuRenderer=false` — CPU-путь всё ещё работает (фолбэк). Скриншоты. Коммит.

---

### Task 13: Финальная проверка всей фичи

- [ ] **Step 1:** Прогнать ВСЕ self-тесты `WorldMapRenderer` (старые + новые GPU) — все PASS.
- [ ] **Step 2:** Человек — полный сценарий: генерация → покраска большой кистью (fps монитора) → смена режимов → правка берега → сохранение/загрузка → undo. Всё корректно, вид эквивалентен-или-лучше CPU.
- [ ] **Step 3:** Замер fps на большой кисти — подтвердить цель ≥25–30 (ожидается кратно больше). Зафиксировать в ledger.
- [ ] **Step 4:** Решить (с человеком): удалять ли CPU-путь сейчас или оставить фолбэком (по спеке — оставить пока). Финальный коммит + обновление ledger.

---

## Self-Review (проверка плана против спеки)

- **Покрытие спеки:** cell-id текстура (T2), атрибуты (T3), шейдер+эффекты (T4-8), органичные границы (T5), свечение (T8), режимы (T9), правка бесплатно (T9), coast-distance по топологии (T11), совместимость save/POI/undo/хит-тест (T9-12), фолбэк CPU (T4,T12), фиксы A+B (T1) — всё покрыто.
- **Плейсхолдеры:** шейдерные Tasks 5-8 дают стартовый HLSL + интерактивную настройку (это осознанно — вид настраивается человеком, не placeholder); CPU-инфра (T1-3,8) — полный код + self-тесты.
- **Согласованность типов:** `CellAttributeTexture.Width/CellRows`, раскладка cellId→тексел, `_AttrWidth/_CellRows` в шейдере — совпадают между T3, T4 и HLSL `attr()`.
