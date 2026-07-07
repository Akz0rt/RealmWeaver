# Плоская заливка по регионам + полосы высоты — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** По тумблеру `flatRegionFill` (default on) заменить блендированную заливку суши на плоскую: цвет = биом-семейство ближайшей клетки, модулированный дискретной полосой высоты (выше = светлее), ровный тон + лёгкое зерно.

**Architecture:** В painted-режиме (Combined+SmoothBorders) при `FlatRegionFill` пропускается дорогой проход блендинга (`BakePaintedFields`), а сухопутный пиксель красится новым `ColorForLandPixelFlat` — цвет биом-семейства ближайшей клетки, модулированный дискретной полосой высоты из её `EffectiveElevation`, плюс зерно и тёмная береговая обводка. Вода, гладкий контур берега и широкое свечение не меняются. Блендинг-путь остаётся доступен при выключенном тумблере.

**Tech Stack:** Unity 6000.3.2f1, C# (Built-in Render Pipeline).

## Global Constraints

- Активно ТОЛЬКО когда `displayMode == MapDisplayMode.Combined && config.SmoothBorders == true && config.FlatRegionFill == true`. При `FlatRegionFill == false` painted-путь работает как сейчас (блендинг) — не менять.
- Новые поля: `flatRegionFill` (bool, default `true`), `elevationBands` (int, default `5`, `[Range(2,8)]`), `elevationBandContrast` (float %, default `40`, `[Range(0,100)]`) — сериализованные поля `WorldMapRenderer` без UI. **Пользовательский дефолт «плоская заливка включена» задаётся дефолтом сериализованного поля `flatRegionFill = true`.** Соответствующий `MapRasterConfig.FlatRegionFill` по умолчанию **`false`** (НЕ true) — намеренно: существующие painted-самотесты строят конфиг не задавая это поле и рассчитывают на блендинг-путь (например `SelfTestRasterElevationInvariant` читает `buffers.Elevation`, который в плоском режиме не заполняется, т.к. `BakePaintedFields` пропускается). Дефолт конфига false → эти тесты не меняют поведение; продакшн получает плоскую заливку через проброс сериализованного поля (Task 2). `ElevationBands`/`ElevationBandContrast` в конфиге — 5/40.
- Плоский цвет суши: базовый = `MapPalette.GetSlotColor(Theme, GetFamily(nearestCell.Biome))` (или нейтральный `(209,199,166)` при `!ShowBiomeLayer`); полоса высоты гейтится `ShowReliefLayer` и `ElevationBands > 1`: `band = Clamp((int)(EffectiveElevation*ElevationBands), 0, ElevationBands-1)`, `t = band/(ElevationBands-1)`, `factor = 1 + (t-0.5)*(ElevationBandContrast/100)`, умножается на RGB.
- Сохраняются в плоском режиме: тёмная береговая обводка (1px, `HasNeighborWithWaterStatus(..., wantWater: true)` → `Outline`) и лёгкое зерно (`(Noise.ValueNoise(x*0.5,y*0.5,Seed+61)-0.5)*7`). Убираются: блендинг, hillshade, тонировка по температуре, региональный шум, лайтнесс-вариация.
- В плоском режиме `BakePaintedFields` НЕ вызывается (цвет из ближайшей клетки). `IsLand` и `CoastDistance` считаются как обычно. Порядок в `ColorForLandPixelFlat`: базовый цвет → полоса высоты → береговая обводка (жёсткая замена) → зерно (поверх).
- `ColorForWaterPixel`, `ColorForLandPixel` (блендинг), `CoastlineContour`, `ComputeCoastDistanceRect`, генерация, кисть-контроллер, `ComputeTouchedPixelRect` — не меняются.

---

### Task 1: Плоская заливка в `MapRasterizer` + самотесты

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (три новых `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `MapRasterBuffers.CellId`/`IsLand` (существуют); `MapPalette.GetSlotColor`/`GetFamily`, `Noise.ValueNoise`, `HasNeighborWithWaterStatus`, `ClampColor32`, `ResolvedPalette` (существуют в файле).
- Produces: `MapRasterConfig.FlatRegionFill` (bool, default **false** — см. Global Constraints), `.ElevationBands` (int, default 5), `.ElevationBandContrast` (float, default 40); `ColorForLandPixelFlat(...)` — внутренние, потребляются `BakePaintedPixel`. Task 2 добавляет сериализованное поле `flatRegionFill = true` и пробрасывает его в конфиг (до Task 2 продакшн `BuildRasterConfig` не задаёт `FlatRegionFill`, конфиг-дефолт false → продакшн временно на блендинге; плоская заливка становится дефолтной для пользователя после Task 2).

- [ ] **Step 1: Добавить поля в `MapRasterConfig`**

В `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`, в классе `MapRasterConfig`, сразу после поля `CoastlineGlowWidth` (и его xml-doc), перед `ShowBiomeLayer`:

```csharp
        public int CoastlineGlowWidth = 16;

        /// <summary>Плоская заливка суши вместо блендинга (только Combined+SmoothBorders): цвет =
        /// биом-семейство ближайшей клетки, модулированный дискретной полосой высоты; ровный тон +
        /// зерно. false = старый блендинг-путь (ColorForLandPixel). Дефолт здесь false (не true),
        /// чтобы существующие painted-самотесты, не задающие это поле, шли по блендинг-пути как раньше
        /// (в плоском режиме BakePaintedFields пропускается и buffers.Elevation не заполняется).
        /// Пользовательский дефолт "включено" - у сериализованного WorldMapRenderer.flatRegionFill
        /// (Task 2). См. design doc docs/superpowers/specs/2026-07-07-flat-region-fill-design.md.</summary>
        public bool FlatRegionFill = false;

        /// <summary>Число дискретных полос высоты в плоской заливке (гейт ShowReliefLayer). >1 -
        /// выше = светлее по ступеням; 1 или меньше - без модуляции высоты.</summary>
        public int ElevationBands = 5;

        /// <summary>Размах светлоты между нижней и верхней полосой высоты, % (0 = полосы не различаются
        /// по тону; 40 ≈ ±20% от базового цвета).</summary>
        public float ElevationBandContrast = 40f;
```

- [ ] **Step 2: Пропустить блендинг в плоском режиме (`BakeFieldsRect`)**

В `BakeFieldsRect`, в блоке `if (painted)`, заменить безусловный вызов `BakePaintedFields` на условный (только НЕ-плоский режим). Текущий хвост блока:

```csharp
                if (config.CoastlineGlowWidth > 0)
                    ComputeCoastDistanceRect(buffers, w, h, config.CoastlineGlowWidth + 1f, rectX, rectY, rectW, rectH);
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }
```

Стало:

```csharp
                if (config.CoastlineGlowWidth > 0)
                    ComputeCoastDistanceRect(buffers, w, h, config.CoastlineGlowWidth + 1f, rectX, rectY, rectW, rectH);
                if (!config.FlatRegionFill)
                    BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }
```

- [ ] **Step 3: Развилка суши в `BakePaintedPixel`**

Заменить текущий `BakePaintedPixel` (метод целиком):

```csharp
        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            var palette = ResolvePalette(config.Theme);
            float coldAmt = 0.10f + (config.ColdLight / 100f) * 0.30f;
            float varAmt = config.RegionVariation / 100f;

            bool isWater = !buffers.IsLand[idx];
            return isWater
                ? ColorForWaterPixel(cell, buffers, x, y, w, h, config, palette, coldAmt)
                : ColorForLandPixel(buffers, idx, x, y, w, h, config, palette, coldAmt, varAmt);
        }
```

на версию с плоской веткой суши:

```csharp
        static Color32 BakePaintedPixel(
            VoronoiCell cell, MapRasterBuffers buffers, int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            var palette = ResolvePalette(config.Theme);
            float coldAmt = 0.10f + (config.ColdLight / 100f) * 0.30f;
            float varAmt = config.RegionVariation / 100f;

            bool isWater = !buffers.IsLand[idx];
            if (isWater)
                return ColorForWaterPixel(cell, buffers, x, y, w, h, config, palette, coldAmt);
            return config.FlatRegionFill
                ? ColorForLandPixelFlat(cell, buffers, x, y, w, h, config, palette)
                : ColorForLandPixel(buffers, idx, x, y, w, h, config, palette, coldAmt, varAmt);
        }
```

- [ ] **Step 4: Добавить `ColorForLandPixelFlat`**

Добавить новый метод сразу ПОСЛЕ `ColorForLandPixel` (найди его закрывающую `}`) и перед `HasNeighborWithWaterStatus`:

```csharp
        /// <summary>Плоская заливка суши (только Combined+SmoothBorders+FlatRegionFill): базовый цвет =
        /// биом-семейство БЛИЖАЙШЕЙ клетки напрямую (без блендинга), модулированный дискретной полосой
        /// высоты (выше = светлее). Соседние клетки одного биома+полосы дают один тон - зоны сливаются
        /// без внутренних граней. Плюс тёмная береговая обводка (1px) и лёгкое зерно. Убрано (относительно
        /// ColorForLandPixel): блендинг, тонировка по температуре, региональный шум, hillshade, лайтнесс-
        /// вариация. См. design doc docs/superpowers/specs/2026-07-07-flat-region-fill-design.md.</summary>
        static Color32 ColorForLandPixelFlat(
            VoronoiCell cell, MapRasterBuffers buffers,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette)
        {
            // Базовый цвет = семейство биома ближайшей клетки (или нейтральный тан, если слой биомов выкл).
            Color32 fam = config.ShowBiomeLayer
                ? MapPalette.GetSlotColor(config.Theme, MapPalette.GetFamily(cell.Biome))
                : new Color32(209, 199, 166, 255);
            float r = fam.r, g = fam.g, b = fam.b;

            // Полоса высоты (гейт ShowReliefLayer): дискретная ступень тона, выше = светлее.
            if (config.ShowReliefLayer && config.ElevationBands > 1)
            {
                int band = Mathf.Clamp((int)(cell.EffectiveElevation * config.ElevationBands), 0, config.ElevationBands - 1);
                float t = band / (float)(config.ElevationBands - 1);      // нормированная ступень [0,1]
                float factor = 1f + (t - 0.5f) * (config.ElevationBandContrast / 100f);
                r *= factor; g *= factor; b *= factor;
            }

            // Тёмная береговая обводка со стороны суши (1px, жёсткая замена) - как в ColorForLandPixel.
            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: true))
            {
                r = palette.Outline.r; g = palette.Outline.g; b = palette.Outline.b;
            }

            // Лёгкое зерно (шаг 8) - поверх, включая обводку (как в ColorForLandPixel).
            float grain = (Noise.ValueNoise(x * 0.5f, y * 0.5f, config.Seed + 61) - 0.5f) * 7f;
            r += grain; g += grain; b += grain;

            return ClampColor32(r, g, b);
        }
```

- [ ] **Step 5: Написать три самотеста в `WorldMapRenderer.cs`**

Добавить после последнего существующего `[ContextMenu]` self-test метода (`SelfTestCoastlineGlowZeroWidthOff` — найди его закрывающую `}`, вставь после, внутри класса). Все три используют all-land фикстуры (нет воды → `IsLand` везде true через all-land guard, нет берега/свечения — чистая заливка суши).

```csharp
        /// <summary>Плоская заливка: 3 клетки-полосы (все суша, воды нет). A,B - Grassland одной высоты
        /// (0.5 → средняя полоса, без модуляции); C - Snow. Пиксель в центре A и пиксель A у границы с B
        /// различаются лишь на величину зерна (зона ровная, слились). Клетка C (другой биом) - скачок
        /// цвета много больше зерна.</summary>
        [ContextMenu("Self-Test: Flat Fill Merges Same-Biome Zones")]
        public void SelfTestFlatFillMergesZones()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(1f, 1f)) { Biome = Biome.Grassland, Height = 0.5f, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site, 1f);
            var b = new VoronoiCell(1, new System.Numerics.Vector2(3f, 1f)) { Biome = Biome.Grassland, Height = 0.5f, IsOcean = false };
            b.Polygon = SquarePolygon(b.Site, 1f);
            var c = new VoronoiCell(2, new System.Numerics.Vector2(5f, 1f)) { Biome = Biome.Snow, Height = 0.5f, IsOcean = false };
            c.Polygon = SquarePolygon(c.Site, 1f);
            var fixtureCells = new List<VoronoiCell> { a, b, c };
            var fixtureById = fixtureCells.ToDictionary(cc => cc.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 2f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 60, TexHeight = 20, MapWidth = 6f, MapHeight = 2f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 0, CoastlineGlowWidth = 0,
                FlatRegionFill = true, ElevationBands = 5, ElevationBandContrast = 40f,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 2f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(60, 20);
            var tex = new Texture2D(60, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 60, 20);

            Color aCenter = tex.GetPixel(10, 10);  // world (1.05,1.05) - A
            Color aNearB = tex.GetPixel(19, 10);   // world (1.95,1.05) - A у границы с B (тот же биом+высота)
            Color cCenter = tex.GetPixel(50, 10);  // world (5.05,1.05) - C (Snow)

            float D(Color p, Color q) => Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
            bool merged = D(aCenter, aNearB) < 0.15f;       // только зерно (~0.082 макс)
            bool biomeDiffers = D(aCenter, cCenter) > 0.25f; // Grassland(plains) vs Snow - крупный скачок

            Destroy(tex);
            bool ok = merged && biomeDiffers;
            Debug.Log(ok
                ? "Self-Test Flat Fill Merges Same-Biome Zones: PASS"
                : $"Self-Test Flat Fill Merges Same-Biome Zones: FAIL (merged={merged} d={D(aCenter, aNearB):F3}, biomeDiffers={biomeDiffers} d={D(aCenter, cCenter):F3})");
        }

        /// <summary>Полосы высоты: одинаковый биом (Grassland), клетка elev 0.1 (нижняя полоса, темнее)
        /// vs elev 0.9 (верхняя, светлее) - заметно разный тон при ShowReliefLayer=true; при
        /// ShowReliefLayer=false обе дают базовый тон (различие лишь на зерно). Заодно квантование:
        /// 0.1 и 0.9 попадают в разные полосы.</summary>
        [ContextMenu("Self-Test: Flat Fill Elevation Bands")]
        public void SelfTestFlatFillElevationBands()
        {
            var lo = new VoronoiCell(0, new System.Numerics.Vector2(1f, 1f)) { Biome = Biome.Grassland, Height = 0.1f, IsOcean = false };
            lo.Polygon = SquarePolygon(lo.Site, 1f);
            var hi = new VoronoiCell(1, new System.Numerics.Vector2(3f, 1f)) { Biome = Biome.Grassland, Height = 0.9f, IsOcean = false };
            hi.Polygon = SquarePolygon(hi.Site, 1f);
            var fixtureCells = new List<VoronoiCell> { lo, hi };
            var fixtureById = fixtureCells.ToDictionary(cc => cc.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 2f);

            WorldGen.Rendering.MapRaster.MapRasterConfig MakeConfig(bool relief) => new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 40, TexHeight = 20, MapWidth = 4f, MapHeight = 2f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 0, CoastlineGlowWidth = 0,
                FlatRegionFill = true, ElevationBands = 5, ElevationBandContrast = 40f,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 2f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = relief,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            (Color loP, Color hiP) Bake(bool relief)
            {
                var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(40, 20);
                var tex = new Texture2D(40, 20, TextureFormat.RGBA32, false);
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, MakeConfig(relief), tex, buffers, 0, 0, 40, 20);
                Color lp = tex.GetPixel(10, 10);  // world (1.05,1.05) - low
                Color hp = tex.GetPixel(30, 10);  // world (3.05,1.05) - high
                Destroy(tex);
                return (lp, hp);
            }

            float D(Color p, Color q) => Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
            var on = Bake(true);
            var off = Bake(false);
            bool bandsDiffer = D(on.loP, on.hiP) > 0.15f;  // нижняя(темнее) vs верхняя(светлее)
            bool gateOff = D(off.loP, off.hiP) < 0.15f;     // рельеф выкл → обе базовый plains (только зерно)

            bool ok = bandsDiffer && gateOff;
            Debug.Log(ok
                ? "Self-Test Flat Fill Elevation Bands: PASS"
                : $"Self-Test Flat Fill Elevation Bands: FAIL (bandsDiffer={bandsDiffer} d={D(on.loP, on.hiP):F3}, gateOff={gateOff} d={D(off.loP, off.hiP):F3})");
        }

        /// <summary>Тумблер FlatRegionFill реально переключает путь: пиксель в клетке A у границы с
        /// клеткой B ДРУГОГО биома. Flat=true → чистый цвет A (plains). Flat=false → блендинг plains+snow
        /// (сосед B в радиусе). Результаты заметно отличаются.</summary>
        [ContextMenu("Self-Test: Flat Fill Toggle Vs Blend")]
        public void SelfTestFlatFillToggleVsBlend()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(1f, 1f)) { Biome = Biome.Grassland, Height = 0.5f, Temperature = 0.5f, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site, 1f);
            var b = new VoronoiCell(1, new System.Numerics.Vector2(3f, 1f)) { Biome = Biome.Snow, Height = 0.5f, Temperature = 0.5f, IsOcean = false };
            b.Polygon = SquarePolygon(b.Site, 1f);
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = fixtureCells.ToDictionary(cc => cc.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 3f);

            Color BakePixel(bool flat, int px, int py)
            {
                var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
                {
                    TexWidth = 40, TexHeight = 20, MapWidth = 4f, MapHeight = 2f, Seed = 1,
                    SmoothBorders = true, CoastlineSmoothness = 0, CoastlineGlowWidth = 0,
                    FlatRegionFill = flat, ElevationBands = 5, ElevationBandContrast = 40f,
                    Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                    ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 3f,
                    ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                    ShowBiomeLayer = true, ShowReliefLayer = true,
                    HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
                };
                var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(40, 20);
                var tex = new Texture2D(40, 20, TextureFormat.RGBA32, false);
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 40, 20);
                Color c = tex.GetPixel(px, py);
                Destroy(tex);
                return c;
            }

            // Клетка A у самой границы с B (world ~1.95 → пиксель 19, ближайшая клетка A).
            Color flatPix = BakePixel(true, 19, 10);
            Color blendPix = BakePixel(false, 19, 10);
            float d = Mathf.Abs(flatPix.r - blendPix.r) + Mathf.Abs(flatPix.g - blendPix.g) + Mathf.Abs(flatPix.b - blendPix.b);

            bool ok = d > 0.1f;
            Debug.Log(ok
                ? "Self-Test Flat Fill Toggle Vs Blend: PASS"
                : $"Self-Test Flat Fill Toggle Vs Blend: FAIL (flat vs blend delta={d:F3}, ожидалось >0.1)");
        }
```

- [ ] **Step 6: Проверить компиляцию и прогнать самотесты**

Если доступен Unity Editor без конфликта с открытым проектом пользователя: дождаться перекомпиляции без ошибок, правым кликом на `WorldMapRenderer` прогнать 3 новых теста (`Flat Fill Merges Same-Biome Zones`, `Flat Fill Elevation Bands`, `Flat Fill Toggle Vs Blend`) — все `PASS`; перепрогнать существующие самотесты берега/свечения/подпроекта 1 — ни один не должен сломаться (сигнатуры `ColorForWaterPixel`/`ColorForLandPixel`/`BakeFieldsRect`/`RebakeRegion` не менялись; `BakePaintedPixel` внутренний).

Если Editor недоступен — перечитать построчно: 3 поля конфига; `if (!config.FlatRegionFill)` перед `BakePaintedFields`; развилку в `BakePaintedPixel` (вода → water; суша → flat/blend по `FlatRegionFill`); тело `ColorForLandPixelFlat` (баланс скобок, `Mathf.Clamp`, `GetFamily`/`GetSlotColor`, зерно). Отметить в отчёте ручную проверку.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): flat per-region land fill with discrete elevation bands"
```

---

### Task 2: Экспонировать поля `flatRegionFill`/`elevationBands`/`elevationBandContrast`

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `MapRasterConfig.FlatRegionFill`/`ElevationBands`/`ElevationBandContrast` (существуют после Task 1 с дефолтами true/5/40).
- Produces: сериализованные `WorldMapRenderer.flatRegionFill` (bool, default true), `.elevationBands` (int `[Range(2,8)]`, default 5), `.elevationBandContrast` (float `[Range(0,100)]`, default 40), проброшенные в `BuildRasterConfig`. До этого таска конфиг использовал дефолты из `MapRasterConfig` (плоская заливка уже была включена); теперь значения управляются из инспектора.

- [ ] **Step 1: Добавить сериализованные поля**

В `Assets/WorldGen/Rendering/WorldMapRenderer.cs`, в блоке `[Header("Combined: тёмный рендер (MapRaster)")]`, сразу после `coastlineGlowWidth`, перед `rasterLongSide`:

```csharp
        [Tooltip("Ширина светлого ореола берега со стороны воды, в пикселях (только Combined+smoothBorders). 0 = нет свечения. Масштабируется через поле дистанции - стоимость не зависит от ширины.")]
        [Range(0, 64)] public int coastlineGlowWidth = 16;
        [Tooltip("Плоская заливка суши вместо блендинга (только Combined+smoothBorders): один тон на зону биом+высота, чёткие границы. Выкл = плавный блендинг между биомами.")]
        public bool flatRegionFill = true;
        [Tooltip("Число дискретных полос высоты в плоской заливке (гейт слоя рельефа). Выше = светлее по ступеням.")]
        [Range(2, 8)] public int elevationBands = 5;
        [Tooltip("Размах светлоты между нижней и верхней полосой высоты, %. 0 = полосы не различаются по тону.")]
        [Range(0f, 100f)] public float elevationBandContrast = 40f;
        [Tooltip("Большая сторона запекаемой текстуры карты в пикселях; меньшая считается по аспекту mapWidth:mapHeight.")]
        public int rasterLongSide = 2048;
```

- [ ] **Step 2: Пробросить в `BuildRasterConfig()`**

В `BuildRasterConfig()`, сразу после `CoastlineGlowWidth = coastlineGlowWidth,`:

```csharp
                CoastlineGlowWidth = coastlineGlowWidth,
                FlatRegionFill = flatRegionFill,
                ElevationBands = elevationBands,
                ElevationBandContrast = elevationBandContrast,
```

- [ ] **Step 3: Проверить компиляцию**

Если доступен Unity Editor без конфликта: дождаться перекомпиляции без ошибок; в инспекторе `WorldMapRenderer` появляются 3 новых поля (тумблер + 2 слайдера) в блоке «тёмный рендер». Перегенерировать карту / переключить `flatRegionFill` — плоская заливка должна включаться/выключаться.

Если Editor недоступен — перечитать: 3 поля с атрибутами внутри класса; 3 строки проброса в `BuildRasterConfig` (имена `FlatRegionFill`/`ElevationBands`/`ElevationBandContrast` совпадают с полями `MapRasterConfig` из Task 1 регистрозависимо). Отметить ручную проверку.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): expose flatRegionFill/elevationBands/elevationBandContrast fields"
```
