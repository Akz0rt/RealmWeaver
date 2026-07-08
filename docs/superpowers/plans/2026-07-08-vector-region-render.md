# Векторный рендер регионов (label-текстура сглаженных контуров) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Заменить «гуляющие» шумовые границы GPU-рендера на чёткие сглаженные векторные контуры (группировка биом+пояс высоты), запечённые в label-текстуру; тонкая линия только между семействами биомов; мягкий песок→биом у берега; рельеф/вода/свечение/палитра — как есть.

**Architecture:** CPU трассирует сглаженные (Chaikin) контуры из графа Corner (переиспользуем `CoastlineContour`) и растеризует их в целочисленные буферы `familyLabel`/`bandLabel`, упакованные в `RG8`-текстуру (`RegionLabelTexture`). Шейдер `MapTerrain` заливает сушу цветом `palette[familyLabel]` × пояс из `bandLabel`, рисует меж-биомную линию и песок по coast-distance; domain-warp удаляется. Трассировка/растеризация — только при генерации/загрузке (чанками) и при отпускании кисти (rect-scoped); во время мазка — мгновенная угловатая заливка по клеткам.

**Tech Stack:** Unity 2022.3 (Built-in RP), C# (`Assets/WorldGen/Rendering/`), HLSL (`MapTerrain.shader`), существующая GPU-инфраструктура (`GpuMap/`) + контурная машинерия (`MapRaster/CoastlineContour.cs`).

## Global Constraints
- **Производительность — первоклассное требование** (см. spec): никакой CPU-работы per-frame; никакой трассировки per-stamp; тяжёлые выпечки только чанками. GPU — вся попиксельная работа. Во время мазка — только дешёвый per-cell/rect патч; сглаженная выпечка — на отпускании кисти, rect-scoped, с `EdgesOverlappingY`.
- Оба пути рендера трогаем только в GPU-ветке (`useGpuRenderer==true`, дефолт). CPU-путь `MapRasterizer` (fallback) НЕ трогаем, кроме вынесения общих хелперов (Task 1, behavior-preserving).
- Агенты не запускают Unity (Editor залочен): C#-шаги статически верифицируются + `[ContextMenu]` self-тесты пишутся (прогон — пользователь). **Шейдерные шаги (Task 4, 5) визуально проверяет и тюнит пользователь в Editor** — стартовые константы даны, финал крутится слайдерами (как весь GPU-рендер исторически).
- Модель данных клетки/corner не меняется. Self-тесты — `[ContextMenu]` на `WorldMapRenderer`.
- Label-разрешение = разрешение cell-id (`texWidth×texHeight`, из `ComputeTexSize`).

## File overview
**Создаётся:**
- `Assets/WorldGen/Rendering/MapRaster/RegionCategories.cs` — общие category/priority хелперы (Task 1).
- `Assets/WorldGen/Rendering/MapRaster/RegionLabelBaker.cs` — трассировка+растеризация family/band label-буферов (Task 2).
- `Assets/WorldGen/Rendering/GpuMap/RegionLabelTexture.cs` — упаковка буферов в RG8-текстуру + rect-патч (Task 2).

**Изменяется:**
- `MapRaster/MapRasterizer.cs` — делегировать хелперы в `RegionCategories` (Task 1).
- `GpuMap/GpuMapRenderer.cs` — построить/обновлять label-текстуру, прокинуть `corners`, uniform'ы (Tasks 3, 6).
- `Rendering/WorldMapRenderer.cs` — прокинуть `corners` в `BuildAll`; edit-путь faceted/smoothed; self-тесты (Tasks 3, 6).
- `Rendering/MapTerrain.shader` — заливка из label'ов, убрать warp, линия-граница, пляж (Tasks 4, 5).

---

### Task 1: `RegionCategories` — вынести общие category/priority хелперы

**Files:** Create `Assets/WorldGen/Rendering/MapRaster/RegionCategories.cs`; Modify `MapRaster/MapRasterizer.cs`; Modify `WorldMapRenderer.cs` (self-test).

**Interfaces:** Produces `RegionCategories.IsLandCell(VoronoiCell)->bool`, `FamilyCategoryOf(VoronoiCell)->int`, `BandCategoryOf(VoronoiCell,int bands)->int`, `FamilyPriority (int[])`, `BandPriorityAscending(int bands)->int[]`. Consumed by `MapRasterizer` (Task 1) и `RegionLabelBaker` (Task 2).

- [ ] **Step 1: Создать `RegionCategories.cs`** (перенос из `MapRasterizer.cs:596-621`, 1:1):

```csharp
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Категоризация клеток в "области" для сглаживания контуров: семейство биома и полоса
    /// высоты. Вынесено из MapRasterizer, чтобы CPU-путь и GPU label-baker (RegionLabelBaker) не
    /// дублировали логику.</summary>
    public static class RegionCategories
    {
        public static bool IsLandCell(VoronoiCell c) => !(c.EffectiveIsOcean || c.EffectiveIsLake);

        /// <summary>Индекс BiomeFamily суши, -1 для воды (регионы семейств ограничены сушей).</summary>
        public static int FamilyCategoryOf(VoronoiCell c) => IsLandCell(c) ? (int)MapPalette.GetFamily(c.Biome) : -1;

        /// <summary>Индекс полосы высоты суши, -1 для воды.</summary>
        public static int BandCategoryOf(VoronoiCell c, int bands) =>
            IsLandCell(c) ? Mathf.Clamp((int)(c.EffectiveElevation * bands), 0, bands - 1) : -1;

        /// <summary>Порядок приоритета семейств (младший→старший, старший выигрывает перекрытия).</summary>
        public static readonly int[] FamilyPriority =
        {
            (int)BiomeFamily.Plains, (int)BiomeFamily.Moor, (int)BiomeFamily.Forest, (int)BiomeFamily.ForestWarm,
            (int)BiomeFamily.Coast, (int)BiomeFamily.Tundra, (int)BiomeFamily.Highland, (int)BiomeFamily.Badlands, (int)BiomeFamily.Snow,
        };

        /// <summary>Полосы высоты по возрастанию индекса (выше = сверху).</summary>
        public static int[] BandPriorityAscending(int bands)
        {
            var order = new int[bands];
            for (int i = 0; i < bands; i++) order[i] = i;
            return order;
        }
    }
}
```

- [ ] **Step 2: Делегировать в `MapRasterizer.cs`.** Удалить приватные `IsLandCell`(:596), `FamilyCategoryOf`(:600), `BandCategoryOf`(:603), `FamilyPriority`(:609), `BandPriorityAscending`(:616) и заменить их использования в `RasterizeSmoothedCategoryRect`/`BakeFieldsRect` на `RegionCategories.<X>`. Точки использования: `MapRasterizer.cs:258-259` (`FamilyCategoryOf, FamilyPriority`), `:263-264` (`BandCategoryOf, BandPriorityAscending`), и любые внутренние вызовы `IsLandCell`. Оставить `FlatFamilyColor` (:671-683) в `MapRasterizer` — он только для CPU-раскраски. Поведение неизменно (чистый перенос).

- [ ] **Step 3: Self-тест в `WorldMapRenderer.cs`** (рядом с существующими `[ContextMenu("Self-Test: ...")]`):

```csharp
        [ContextMenu("Self-Test: Region Categories")]
        public void SelfTestRegionCategories()
        {
            var land = new VoronoiCell(0, new System.Numerics.Vector2(0,0)) { IsOcean = false, Biome = Biome.Grassland, Height = 0.42f };
            var sea  = new VoronoiCell(1, new System.Numerics.Vector2(1,0)) { IsOcean = true,  Biome = Biome.Ocean };
            var RC = typeof(WorldGen.Rendering.MapRaster.RegionCategories);
            bool ok =
                WorldGen.Rendering.MapRaster.RegionCategories.IsLandCell(land) &&
                !WorldGen.Rendering.MapRaster.RegionCategories.IsLandCell(sea) &&
                WorldGen.Rendering.MapRaster.RegionCategories.FamilyCategoryOf(sea) == -1 &&
                WorldGen.Rendering.MapRaster.RegionCategories.FamilyCategoryOf(land) == (int)WorldGen.Rendering.MapRaster.MapPalette.GetFamily(Biome.Grassland) &&
                WorldGen.Rendering.MapRaster.RegionCategories.BandCategoryOf(land, 5) == Mathf.Clamp((int)(0.42f * 5), 0, 4) &&
                WorldGen.Rendering.MapRaster.RegionCategories.BandCategoryOf(sea, 5) == -1;
            Debug.Log(ok ? "Self-Test Region Categories: PASS" : "Self-Test Region Categories: FAIL");
        }
```

- [ ] **Step 4: Компиляция (Editor) + self-тест + существующие MapRasterizer self-тесты всё ещё PASS.** Commit:
```bash
git add Assets/WorldGen/Rendering/MapRaster/RegionCategories.cs Assets/WorldGen/Rendering/MapRaster/RegionCategories.cs.meta Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "refactor(map): extract RegionCategories (shared family/band category helpers)"
```
(`.meta` генерит Unity при импорте — если ещё нет, добавить после импорта.)

---

### Task 2: `RegionLabelBaker` + `RegionLabelTexture`

**Files:** Create `MapRaster/RegionLabelBaker.cs`, `GpuMap/RegionLabelTexture.cs`; Modify `WorldMapRenderer.cs` (self-test).

**Interfaces:**
- Produces `RegionLabelBaker.BakeRect(IReadOnlyDictionary<int,VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray, int[] familyLabel, int[] bandLabel, int texW, int texH, float mapW, float mapH, int smoothing, float decimation, int bands, int rectX, int rectY, int rectW, int rectH)` — заполняет `familyLabel`/`bandLabel` (−1 = нет метки) сглаженными контурами в rect. Consumed by `GpuMapRenderer` (Task 3, 6).
- Produces `RegionLabelTexture` (класс): `.Texture`, `.Build(int[] familyLabel,int[] bandLabel,int texW,int texH)`, `.PatchRect(...)`, `.Apply()`, `.Texel`. Consumed by `GpuMapRenderer`.

- [ ] **Step 1: `RegionLabelBaker.cs`** (standalone-аналог `RasterizeSmoothedCategoryRect`, на raw-массивах, через `RegionCategories`+`CoastlineContour`):

```csharp
using System.Collections.Generic;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Печёт сглаженные контуры "семейство биома" и "полоса высоты" в целочисленные
    /// label-буферы (−1 = нет метки), rect-scoped. Трассировка через CoastlineContour, категории —
    /// RegionCategories. GPU-путь упаковывает буферы в RG8-текстуру (RegionLabelTexture).</summary>
    public static class RegionLabelBaker
    {
        public static void BakeRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray,
            int[] familyLabel, int[] bandLabel,
            int texW, int texH, float mapW, float mapH,
            int smoothing, float decimation, int bands,
            int rectX, int rectY, int rectW, int rectH)
        {
            BakeCategory(cellById, corners, cellIdArray, familyLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.FamilyCategoryOf(c), RegionCategories.FamilyPriority, rectX, rectY, rectW, rectH);
            BakeCategory(cellById, corners, cellIdArray, bandLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.BandCategoryOf(c, bands), RegionCategories.BandPriorityAscending(bands), rectX, rectY, rectW, rectH);
        }

        static void BakeCategory(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray,
            int[] label, int texW, int texH, float mapW, float mapH, int smoothing, float decimation,
            System.Func<VoronoiCell, int> categoryOf, IReadOnlyList<int> priorityOrder,
            int rectX, int rectY, int rectW, int rectH)
        {
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                    label[y * texW + x] = -1;

            var present = new HashSet<int>();
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int cat = categoryOf(cellById[cellIdArray[y * texW + x]]);
                    if (cat >= 0) present.Add(cat);
                }
            if (present.Count == 0) return;

            foreach (int category in priorityOrder)
            {
                if (!present.Contains(category)) continue;
                int cat = category;
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, c => categoryOf(c) == cat, smoothing, decimation);
                if (loops.Count == 0) continue;
                CoastlineContour.RasterizeRegionLabel(loops, label, category, texW, texH, mapW, mapH, rectX, rectY, rectW, rectH);
            }
        }
    }
}
```

- [ ] **Step 2: `RegionLabelTexture.cs`** (RG8, R=family, G=band; −1→0 маппится как «нет области», но т.к. суша всегда имеет метку после полного бейка, а вода игнорит label в шейдере, храним `label+1` в R/G? — НЕТ: держим просто clamp(label,0,255); −1 (клин тройного стыка) кодируем 255 = sentinel «нет метки» → шейдер откатывается к family из attribute-текстуры):

```csharp
using UnityEngine;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RG8-текстура сглаженных областей: R = familyLabel, G = bandLabel (индексы 0..254).
    /// Значение 255 = sentinel "нет метки" (клин тройного стыка) → шейдер откатывается к family/band
    /// из attribute-текстуры. Point-фильтр, разрешение = cell-id.</summary>
    public class RegionLabelTexture
    {
        public Texture2D Texture { get; private set; }
        public const byte NoLabel = 255;
        int texW, texH;
        Color32[] pixels;

        public Vector4 Texel => new Vector4(1f / texW, 1f / texH, 0, 0);

        public void Build(int[] familyLabel, int[] bandLabel, int w, int h)
        {
            texW = w; texH = h;
            if (Texture != null) Object.Destroy(Texture);
            Texture = new Texture2D(w, h, TextureFormat.RG16, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Encode(familyLabel[i], bandLabel[i]);
            Apply();
        }

        /// <summary>Патч под-прямоугольника (кисть): пере-кодировать rect из label-буферов.</summary>
        public void PatchRect(int[] familyLabel, int[] bandLabel, int rectX, int rectY, int rectW, int rectH)
        {
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                { int i = y * texW + x; pixels[i] = Encode(familyLabel[i], bandLabel[i]); }
            Apply();
        }

        static Color32 Encode(int family, int band)
        {
            byte r = (byte)(family < 0 ? NoLabel : Mathf.Clamp(family, 0, 254));
            byte g = (byte)(band   < 0 ? NoLabel : Mathf.Clamp(band,   0, 254));
            return new Color32(r, g, 0, 255);
        }

        public void Apply() { Texture.SetPixels32(pixels); Texture.Apply(false); }
        public void Destroy() { if (Texture != null) Object.Destroy(Texture); }
    }
}
```
(Формат `RG16` = 8 бит R + 8 бит G, поддерживается везде; если платформа капризна — `RGBA32`, шейдер читает `.r`/`.g`.)

- [ ] **Step 3: Self-тест** в `WorldMapRenderer.cs`: фикстура (ocean-кольцо вокруг land-клеток разных семейств), `RegionLabelBaker.BakeRect` → проверить, что во внутренней land-точке `familyLabel` = ожидаемому семейству, в воде остаётся −1. (Аналог `SelfTestRasterizeRegionLabel`, использовать `CornerGraphBuilder.Build(fixtureCells)` + `cellIdArray` через `NearestCellLookup` по сетке.) Полный код фикстуры — как в существующем `SelfTestRasterizeRegionLabel` (`WorldMapRenderer.cs:1420`), заменив прямой вызов `RasterizeRegionLabel` на `RegionLabelBaker.BakeRect`.

- [ ] **Step 4: Компиляция + self-тесты. Commit:**
```bash
git add Assets/WorldGen/Rendering/MapRaster/RegionLabelBaker.cs Assets/WorldGen/Rendering/MapRaster/RegionLabelBaker.cs.meta Assets/WorldGen/Rendering/GpuMap/RegionLabelTexture.cs Assets/WorldGen/Rendering/GpuMap/RegionLabelTexture.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): region-label baker + RG8 label texture (smoothed family/band contours)"
```

---

### Task 3: Построить label-текстуру в `GpuMapRenderer` + прокинуть `corners`

**Files:** Modify `GpuMap/GpuMapRenderer.cs`, `Rendering/WorldMapRenderer.cs`.

**Interfaces:** `GpuMapRenderer.BuildAll`/`BuildAllStepped` получают доп. параметр `IReadOnlyList<Corner> corners`. Экспонируют label-текстуру шейдеру как `_LabelTex` + `_LabelTexel`. (Шейдер ещё не читает — Task 4; здесь только данные.)

- [ ] **Step 1: В `GpuMapRenderer.cs`** добавить поля: `RegionLabelTexture labelTex;` `int[] familyLabel, bandLabel;` `List<Corner> bakedCorners;` `IReadOnlyDictionary<int,VoronoiCell> bakedCellById;` `int bakedBands = 5;` `int bakedSmoothing; float bakedDecimation;` (сглаживание/прореживание — из config; стартово `smoothing=2, decimation=0`; вынести в параметры `BuildAll` если нужно тюнить). Добавить `using WorldGen.Rendering.MapRaster;` (уже есть).

- [ ] **Step 2: Сигнатуры `BuildAll`/`BuildAllStepped`** — добавить `IReadOnlyList<Corner> corners` последним доменным параметром (перед `theme` или после — согласовать с вызовом): напр. `BuildAll(cells, lookup, texW, texH, mapW, mapH, theme, corners)`. Пробросить в `FinishBuild(cells, texW, texH, mapW, mapH, theme, corners)`.

- [ ] **Step 3: В `FinishBuild`** после построения `cellIdArray` (там уже кэшируется, строки 66-68) и `attr`, построить label-текстуру:
```csharp
            // Сглаженные области (семейство+пояс) → RG8 label-текстура.
            bakedCorners = new System.Collections.Generic.List<Corner>(corners);
            bakedCellById = BuildCellById(cells);
            int labelLen = texW * texH;
            familyLabel = new int[labelLen];
            bandLabel = new int[labelLen];
            RegionLabelBaker.BakeRect(bakedCellById, bakedCorners, cellIdArray, familyLabel, bandLabel,
                texW, texH, mapW, mapH, bakedSmoothing, bakedDecimation, bakedBands, 0, 0, texW, texH);
            if (labelTex == null) labelTex = new RegionLabelTexture();
            labelTex.Build(familyLabel, bandLabel, texW, texH);
            Material.SetTexture("_LabelTex", labelTex.Texture);
            Material.SetVector("_LabelTexel", labelTex.Texel);
```
Добавить helper `static IReadOnlyDictionary<int,VoronoiCell> BuildCellById(IReadOnlyList<VoronoiCell> cells){ var d=new Dictionary<int,VoronoiCell>(cells.Count); foreach(var c in cells) d[c.Id]=c; return d; }`. Освобождать `labelTex` в `OnDestroy` (`labelTex?.Destroy()`).
   ⚠️ `bakedBands` должен совпадать с `_ElevBands` шейдера (5). Если пользователь крутит `_ElevBands` — label надо пере-печь (в этом плане bands фиксирован 5; слайдер bands → RebakeAll, отметить в Рисках).

- [ ] **Step 4: В `BuildAllStepped`** пробросить `corners` в `FinishBuild` (сама label-выпечка — быстрая после cell-id, оставить в `FinishBuild`; если на больших картах даёт хитч — чанковать в отдельном шаге, отметить). Cell-id остаётся тяжёлым чанковым шагом.

- [ ] **Step 5: В `WorldMapRenderer.RebakeAll`** (GPU-ветка, строка 2382) прокинуть `corners`:
```csharp
                gpuRenderer.BuildAll(cells, nearestLookup, texWidth, texHeight, mapWidth, mapHeight, paletteTheme, corners);
```
И в `RebakeAllStepped` GPU-ветке — аналогично для `BuildAllStepped`. `corners` — уже поле рендера (строится в 246/278).

- [ ] **Step 6: Компиляция + регенерация в Editor.** Визуально ничего не меняется (шейдер ещё не читает `_LabelTex`), но Console чист и карта рисуется как раньше. **Commit:**
```bash
git add Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): bake region-label texture on generation/load (corners threaded in)"
```

---

### Task 4: Шейдер — заливка из label-текстуры, убрать domain-warp

**Files:** Modify `Rendering/MapTerrain.shader`. **Верификация — визуальная, пользователь в Editor.**

- [ ] **Step 1: Объявить label-семплер.** В блоке uniform'ов (после `_CellIdTexel`, ~строка 33) добавить:
```hlsl
            sampler2D _LabelTex;   // RG8: R=familyLabel, G=bandLabel (255 = нет метки)
            float2 _LabelTexel;
```
Добавить в `Properties` (после `_AttrTex`): `_LabelTex ("Region Labels", 2D) = "black" {}`.

- [ ] **Step 2: Убрать warp.** Удалить `warpUV`(:103-107) и его вызов `float2 wuv = warpUV(i.uv);`(:123) → заменить на `float2 wuv = i.uv;`. Удалить uniform'ы `_WarpAmount`/`_WarpScale`/`_Seed`(:29-31) и их установку в `GpuMapRenderer.FinishBuild`(:83-85). (`fbm`/`vnoise`/`hash21` оставить — их использует рябь воды и зерно.)

- [ ] **Step 3: Заливка суши из label'ов.** В land-ветке (`else` на :148) заменить получение `family`/полосы. Добавить хелпер (рядом с `attr`):
```hlsl
            // Метка области в пикселе: R=family, G=band; 255 = нет метки (откат к attribute).
            int2 labelAt(float2 uv)
            {
                float2 l = tex2Dlod(_LabelTex, float4(uv, 0, 0)).rg * 255.0;
                return int2((int)(l.x + 0.5), (int)(l.y + 0.5));
            }
```
Заменить `col = (_ShowBiome > 0.5) ? _Palette[family].rgb : ...`(:151) на:
```hlsl
                    int2 lab = labelAt(i.uv);
                    int famL = (lab.x == 255) ? family : lab.x;   // откат к attribute на клиньях
                    col = (_ShowBiome > 0.5) ? _Palette[famL].rgb : float3(0.82, 0.78, 0.65);
```
И в блоке полос высоты(:153-160) заменить вычисление `band` из `elev` на метку `bandLabel` (сглаженные ступени), с откатом:
```hlsl
                    if (_ShowRelief > 0.5)
                    {
                        int bands = max(2, (int)_ElevBands);
                        int band = (lab.y == 255) ? clamp((int)(elev * bands), 0, bands - 1) : lab.y;
                        float bt = band / max(1.0, (float)(bands - 1));
                        col *= 1.0 + (bt - 0.5) * (_BandContrast / 100.0);
                    }
```
Hillshade/tint/grain/vignette/вода/свечение — БЕЗ изменений (рельеф по-прежнему сэмплит соседние высоты из cell-id+attribute; вода/берег по cell-id+coast). `wuv` теперь = `i.uv`, все `attr(cellAt(wuv...))` работают по прямой UV.

- [ ] **Step 4: Визуальная проверка (Editor).** Регенерировать. Ожидаемо: границы биомов стали **чёткими сглаженными кривыми** (не «пушистыми»), заливка семейств чистая; рельеф/вода/свечение как раньше. Тонкой линии-границы и мягкого пляжа ещё нет (Task 5). Тюнинг сглаживания — через `bakedSmoothing` (Task 3; при желании вынести в слайдер). **Commit** после подтверждения:
```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs
git commit -m "feat(map-shader): fill from smoothed region labels, remove domain-warp"
```

---

### Task 5: Шейдер — линия между семействами биомов + мягкий пляж

**Files:** Modify `Rendering/MapTerrain.shader`, `GpuMap/GpuMapRenderer.cs` (новые uniform'ы). **Верификация — визуальная.**

- [ ] **Step 1: Uniform'ы.** В шейдере добавить: `float4 _BiomeLineColor; float _BiomeLineStrength; float _BeachWidth; float4 _BeachColor;`. В `GpuMapRenderer.FinishBuild` задать стартовые: `_BiomeLineColor` = `PaletteSlot.Outline` (как `_OutlineColor`); `_BiomeLineStrength=0.5`; `_BeachWidth=10` (px); `_BeachColor` = `PaletteSlot.Coast`.

- [ ] **Step 2: Линия между семействами (только суша, не берег).** В land-ветке, ДО зерна, добавить 4-tap тест метки семейства:
```hlsl
                    // тонкая линия между разными семействами биомов (не между поясами, не на берегу)
                    float2 lt = _LabelTexel * 1.5;
                    int f0 = labelAt(i.uv + float2(lt.x,0)).x;
                    int f1 = labelAt(i.uv - float2(lt.x,0)).x;
                    int f2 = labelAt(i.uv + float2(0,lt.y)).x;
                    int f3 = labelAt(i.uv - float2(0,lt.y)).x;
                    bool famEdge = (f0!=famL && f0!=255) || (f1!=famL && f1!=255) || (f2!=famL && f2!=255) || (f3!=famL && f3!=255);
                    if (famEdge) col = lerp(col, _BiomeLineColor.rgb, _BiomeLineStrength);
```

- [ ] **Step 3: Мягкий пляж (песок→биом по coast-distance).** Открытый пункт spec: A пометил берег биомом Beach → у прибрежных клеток `familyLabel=Coast`, и заливка там уже песочная (Coast-цвет). Для **мягкого перехода** подмешать `_BeachColor` в биом по близости к воде на суше:
```hlsl
                    // мягкий песок у берега: сила спадает вглубь суши на _BeachWidth px
                    float cdLand = tex2Dlod(_CoastTex, float4(i.uv, 0, 0)).r; // 0 на суше у самой воды? нет: _CoastTex=0 на суше
```
⚠️ `_CoastTex` = дистанция до суши (0 НА суше), т.е. на суше coast-distance = 0 везде — **не годится** для «расстояния от берега вглубь суши». Нужна дистанция до ВОДЫ. Варианты (решить при реализации): (a) построить второй chamfer-field «дистанция до воды» (аналог `CoastDistanceTexture`, инверсный предикат) — чисто, ~как сейчас; (b) аппроксимация в шейдере через тот же 4-tap `waterAt` с расширением. **Рекомендация:** (a) — добавить `landCoastDist` текстуру (расстояние суши до ближайшей воды) в `GpuMapRenderer` рядом с `coastDistTex` (тот же `CoastDistanceTexture.Build` с предикатом `cid => !waterIds.Contains(cid)`? — нет, нужна дистанция суши-пикселей до воды: `Build(cellIdArray, cid=>!isWater, ...)` даёт дистанцию до суши; для дистанции до воды инвертировать предикат). Финализировать формулу/текстуру при реализации; затем:
```hlsl
                    float beach = saturate(1.0 - landDistToWater / max(1.0, _BeachWidth));
                    col = lerp(col, _BeachColor.rgb, beach * 0.6);
```
Размещение: ПОСЛЕ линии-границы, но линию НЕ рисовать на берегу (famEdge с участием Coast-семейства можно гасить: `famL==(int)Coast` → пропустить линию; уточнить). Это самый тонкий по композиции шаг — итеративно в Editor.

- [ ] **Step 4: Визуальная проверка + тюнинг (Editor).** Линия между биомами тонкая и читаемая; берег — мягкий песок, переходящий в биом, без жёсткой линии. Крутить `_BiomeLineStrength`/`_BeachWidth` слайдерами. **Commit** после подтверждения:
```bash
git add Assets/WorldGen/Rendering/MapTerrain.shader Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs
git commit -m "feat(map-shader): inter-biome border line + soft coast-distance beach transition"
```

---

### Task 6: Редактирование — угловато во время мазка, гладко при отпускании

**Files:** Modify `GpuMap/GpuMapRenderer.cs`, `Rendering/WorldMapRenderer.cs`.

**Interfaces:** `GpuMapRenderer.UpdateCells` дополнительно патчит label-текстуру **faceted** (по клеткам, rect) для затронутых клеток; новый `GpuMapRenderer.FinalizeLabels(rectX,rectY,rectW,rectH)` пере-печёт **сглаженные** метки в rect и вызывается из `EndBrushStroke`.

- [ ] **Step 1: Faceted-патч во время мазка.** В `GpuMapRenderer.UpdateCells`, после `attr.UpdateCell`, отметить label-грязь и патчить faceted: для каждой затронутой клетки пробежать её пиксели в bbox (по `cellIdArray`), выставить `familyLabel/bandLabel` = `RegionCategories.FamilyCategoryOf/BandCategoryOf` этой клетки (угловато, без трассировки), затем `labelTex.PatchRect(bbox)`. Накопить общий dirty-rect в поля `labelDirtyMinX..MaxY`. (bbox клетки — из `cell.Polygon` min/max → пиксели; helper `CellPixelRect(cell, mapW, mapH, texW, texH)`.) Дёшево: только пиксели затронутых клеток.

- [ ] **Step 2: Smoothed re-bake на отпускании.** Новый метод:
```csharp
        public void FinalizeLabels()
        {
            if (!labelDirty || cellIdArray == null) return;
            // расширить dirty-rect на запас под сглаживание/прореживание (как ComputeTouchedPixelRect)
            int pad = LabelRectPad();  // ~1.5*minPointDistance в пикселях + smoothing slack
            int rx = Mathf.Clamp(labelDirtyMinX - pad, 0, bakedTexW - 1);
            int ry = Mathf.Clamp(labelDirtyMinY - pad, 0, bakedTexH - 1);
            int rw = Mathf.Clamp(labelDirtyMaxX + pad, 0, bakedTexW - 1) - rx + 1;
            int rh = Mathf.Clamp(labelDirtyMaxY + pad, 0, bakedTexH - 1) - ry + 1;
            RegionLabelBaker.BakeRect(bakedCellById, bakedCorners, cellIdArray, familyLabel, bandLabel,
                bakedTexW, bakedTexH, bakedMapW, bakedMapH, bakedSmoothing, bakedDecimation, bakedBands, rx, ry, rw, rh);
            labelTex.PatchRect(familyLabel, bandLabel, rx, ry, rw, rh);
            labelDirty = false;
        }
```
(Сохранить `bakedMapW/bakedMapH` в `FinishBuild`. `LabelRectPad` — как запас в `WorldMapRenderer.ComputeTouchedPixelRect`.) ⚠️ Если клетка сменила биом/воду, `bakedCellById[cell.Id]` должен указывать на ту же (мутированную) клетку — так и есть (ссылки). Но `bakedCorners` статичны (геометрия неизменна) — ок.

- [ ] **Step 3: Вызвать из `WorldMapRenderer`.** В `EndBrushStroke` (около `gpuRenderer.FinalizeCoast()`, :527) добавить `gpuRenderer.FinalizeLabels();`. В `RefreshAfterCellDataChange` (:2425, override многих клеток) добавить полный re-bake label'ов: после `UpdateCells(cells)` вызвать `gpuRenderer.RebakeLabelsFull()` (обёртка `BakeRect(...,0,0,texW,texH)` + `labelTex.Build`) — override меняет много клеток, дешевле полный.

- [ ] **Step 4: Self-тест** (`WorldMapRenderer`): построить faceted-патч на фикстуре и проверить, что затронутая клетка получила ожидаемую метку сразу (без трассировки); затем `FinalizeLabels`-эквивалент (`BakeRect` rect) даёт сглаженную метку. (Хотя бы проверить faceted-путь детерминированно; сглаженный — как в Task 2.)

- [ ] **Step 5: Визуальная проверка (Editor):** рисуешь кистью — заливка меняется мгновенно (угловато по клеткам), при отпускании ЛКМ границы сглаживаются. Без фризов. **Commit:**
```bash
git add Assets/WorldGen/Rendering/GpuMap/GpuMapRenderer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(gpu-map): faceted label patch during brush, smoothed re-bake on stroke end"
```

---

## Self-Review

**Покрытие spec'а:**
- Label-текстура (RG8, family+band) → Task 2. ✅
- CPU трассировка+растеризация через `CoastlineContour`, полный бейк при генерации / rect-scoped при отпускании → Tasks 2, 3, 6. ✅
- Шейдер: убрать warp, заливка из label'ов → Task 4; линия только меж-биомная, пляж по coast-distance → Task 5. ✅
- Рельеф/вода/свечение/тон/зерно/виньетка без изменений → Task 4 (сохранены). ✅
- Кисть: угловато во время мазка → гладко при отпускании → Task 6. ✅
- Перф-инварианты (GPU per-frame, CPU только gen/stroke-end, чанки) → Global Constraints + Tasks 3/6. ✅
- Переиспользование `CoastlineContour`/category-хелперов → Tasks 1, 2. ✅
- Открытый пункт «пляж × A3-Beach» → **решён в Task 5 Step 3**: суша-дистанция-до-воды (новое chamfer-поле или инверсный предикат), песок подмешивается по ней; A3-Beach даёт песочную заливку прибрежных клеток, а мягкость — через это поле. Требует финализации формулы при реализации (помечено).

**Плейсхолдеры:** код C#-шагов полный. Шейдерные шаги (4,5) дают полный HLSL + стартовые константы; финальные значения — Editor-тюнинг (природа задачи, не заглушка). Task 5 Step 3 (пляжное поле) — единственное место с двумя вариантами реализации (a/b) и рекомендацией (a); финализируется при реализации, помечено как самый тонкий шаг.

**Согласованность типов/имён:** `RegionCategories.*` (Task 1) используется в `RegionLabelBaker` (Task 2). `RegionLabelBaker.BakeRect` сигнатура едина (Task 2 определение, Task 3/6 вызовы). `RegionLabelTexture` API (`Build/PatchRect/Texel/Destroy`) едино. `BuildAll(...corners)` — Task 3 меняет сигнатуру, оба вызова (`RebakeAll`+`RebakeAllStepped`) обновлены там же. `_LabelTex`/`_LabelTexel`/`labelAt` — Task 4 объявляет, Task 5 использует. `FinalizeLabels`/`labelDirty`/`bakedCorners`/`bakedCellById`/`bakedMapW/H` — Task 3 заводит поля, Task 6 использует.

**Риски (для финального ревью):**
- `bakedBands` (label) должен совпадать с `_ElevBands` (шейдер) = 5; слайдер bands потребует пере-бейка label'ов (сейчас fixed, отмечено в Task 3 Step 3).
- Скорость `FinalizeLabels` на большой карте/большом штрихе — измерить; при нужде кэшировать петли неизменившихся категорий.
- Task 5 Step 3 — самый тонкий (пляжное поле + не рисовать линию на берегу); итеративно в Editor.
- `.meta` новых файлов коммитит пользователь после импорта.
