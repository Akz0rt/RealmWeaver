# Широкая подсветка берега — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сделать светлую подсветку берега (со стороны воды) значительно шире и мягче, управляемой полем `coastlineGlowWidth` (0–64px, дефолт 16), через масштабируемое поле дистанции до берега — стоимость не зависит от ширины ореола.

**Architecture:** После растеризации маски `IsLand` считаем для каждого водного пикселя приближённое расстояние до ближайшей суши (two-pass chamfer distance transform, `O(пикселей)`, нормализованные веса → расстояние сразу в пикселях), храним в новом буфере `CoastDistance`. `ColorForWaterPixel` заменяет 1px-проверку соседей на градиентное свечение `Glow` с силой, затухающей от кромки к нулю на `glowWidth`. DT засевается с границы rect из уже готового буфера — частичный (кистью) пересчёт бесшовен; dirty-rect кисти расширяется на `glowWidth`. Тёмная обводка суши не трогается.

**Tech Stack:** Unity 6000.3.2f1, C# (Built-in Render Pipeline), `System.Numerics.Vector2` для геометрии генерации, `UnityEngine.Mathf` для растеризации.

## Global Constraints

- Активно ТОЛЬКО в painted-режиме (`displayMode == MapDisplayMode.Combined && config.SmoothBorders == true`) — как весь берег; прочие режимы не трогаются.
- Тёмная обводка суши (`ColorForLandPixel`, жёсткая замена на `Outline` при соседе-воде через `HasNeighborWithWaterStatus(..., wantWater: true)`) — БЕЗ изменений, остаётся 1px. `HasNeighborWithWaterStatus` остаётся (его использует сторона суши); меняется только сторона воды.
- Новое поле `coastlineGlowWidth`: `int`, `[Range(0, 64)]`, default `16`, сериализованное поле `WorldMapRenderer`, без UI (подпроект 6 добавит слайдер). `MapRasterConfig.CoastlineGlowWidth` — соответствующее поле, default `16`.
- `CoastDistance` — расстояние в ПИКСЕЛЯХ (нормализованные chamfer-веса: ортогональный шаг `1.0`, диагональный `1.41421356`); суша = `0`; клампится сверху на `glowWidth + 1`.
- Формула свечения: сила `gk = (0.32f + coldAmt*0.5f) * clamp01(1 − dist/glowWidth)`; `glowWidth = 0` → свечения нет (guard от деления на ноль). Базовая формула `0.32 + coldAmt*0.5` не меняется — добавляется только модуляция по дистанции.
- DT читает соседей ЗА границей rect из существующего `CoastDistance` (валидные значения прошлого полного запека) → бесшовный частичный пересчёт. Границы проверяются по ИЗОБРАЖЕНИЮ (`x-1 >= 0`, `x+1 < w`), НЕ по rect — именно это включает засев из буфера.
- `CoastlineContour.cs`, генерация, `BrushToolController.cs`, тёмная обводка не меняются.

---

### Task 1: Distance transform + буфер `CoastDistance` (утилита + самотесты)

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (два новых `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `MapRasterBuffers.IsLand` (`bool[]`, уже существует, заполняется `CoastlineContour.RasterizeIsLand`).
- Produces: `MapRasterBuffers.CoastDistance` (`float[]`); `MapRasterizer.ComputeCoastDistanceRect(MapRasterBuffers buffers, int w, int h, float maxDist, int rectX, int rectY, int rectW, int rectH)` (public static, void) — используется Task 2. Ничто ещё не вызывает его из пайплайна (чистая утилита + буфер + тесты, по образцу CoastlineContour в прошлой фиче).

- [ ] **Step 1: Добавить буфер `CoastDistance` в `MapRasterBuffers` + аллокацию**

В `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`, в классе `MapRasterBuffers` (после поля `IsLand`):

```csharp
        /// <summary>true = суша по сглаженному контуру берега (только Combined+SmoothBorders -
        /// см. CoastlineContour). В прочих режимах не заполняется и не читается.</summary>
        public bool[] IsLand;

        /// <summary>Приближённое (chamfer) расстояние в пикселях от водного пикселя до ближайшей
        /// суши (по IsLand); суша = 0; клампится на glowWidth+1. Только Combined+SmoothBorders при
        /// CoastlineGlowWidth > 0 - см. MapRasterizer.ComputeCoastDistanceRect. Питает широкую
        /// подсветку берега в ColorForWaterPixel.</summary>
        public float[] CoastDistance;
```

В `CreateEmptyBuffers` (добавить в инициализатор после `IsLand = new bool[n],`):

```csharp
                PreVignette = new Color32[n],
                IsLand = new bool[n],
                CoastDistance = new float[n],
            };
```

- [ ] **Step 2: Написать самотест distance transform (упадёт — метода ещё нет)**

Добавить в `Assets/WorldGen/Rendering/WorldMapRenderer.cs` после последнего существующего `[ContextMenu]` self-test метода (`SelfTestCoastlineMaskUpdatesWithBrushDirtyRect` — найди его закрывающую `}`, вставь после неё, внутри класса):

```csharp
        /// <summary>Distance transform: единственный пиксель суши в центре 11x11, проверка что
        /// CoastDistance даёт приближённое евклидово расстояние в пикселях (ортогональный шаг 1,
        /// диагональный √2), суша = 0, и клампится на maxDist.</summary>
        [ContextMenu("Self-Test: Coast Distance Transform")]
        public void SelfTestCoastDistanceTransform()
        {
            const int n = 11;
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(n, n);
            for (int i = 0; i < n * n; i++) buffers.IsLand[i] = false;
            buffers.IsLand[5 * n + 5] = true; // суша только в (x=5,y=5)

            WorldGen.Rendering.MapRaster.MapRasterizer.ComputeCoastDistanceRect(buffers, n, n, 20f, 0, 0, n, n);

            const float D2 = 1.41421356f;
            bool center0 = Mathf.Abs(buffers.CoastDistance[5 * n + 5] - 0f) < 0.01f;      // (5,5)
            bool ortho1 = Mathf.Abs(buffers.CoastDistance[5 * n + 6] - 1f) < 0.01f;       // (6,5)
            bool ortho2 = Mathf.Abs(buffers.CoastDistance[7 * n + 5] - 2f) < 0.01f;       // (5,7)
            bool diag2 = Mathf.Abs(buffers.CoastDistance[7 * n + 7] - 2f * D2) < 0.01f;   // (7,7)
            bool ortho3 = Mathf.Abs(buffers.CoastDistance[5 * n + 8] - 3f) < 0.01f;       // (8,5)

            // Кламп: с maxDist=2 дальний пиксель (8,5) (истинно 3) обрезается до 2.
            var clampBuf = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(n, n);
            for (int i = 0; i < n * n; i++) clampBuf.IsLand[i] = false;
            clampBuf.IsLand[5 * n + 5] = true;
            WorldGen.Rendering.MapRaster.MapRasterizer.ComputeCoastDistanceRect(clampBuf, n, n, 2f, 0, 0, n, n);
            bool clamped = Mathf.Abs(clampBuf.CoastDistance[5 * n + 8] - 2f) < 0.01f;

            bool ok = center0 && ortho1 && ortho2 && diag2 && ortho3 && clamped;
            Debug.Log(ok
                ? "Self-Test Coast Distance Transform: PASS"
                : $"Self-Test Coast Distance Transform: FAIL (center0={center0}, ortho1={ortho1}, ortho2={ortho2}, diag2={diag2}, ortho3={ortho3}, clamped={clamped})");
        }

        /// <summary>Бесшовность частичного пересчёта: land в (5,5), полный DT = эталон; затем та же
        /// IsLand + CoastDistance предзаполнены эталоном (как после прошлого полного запека), и
        /// пересчитываем ТОЛЬКО под-прямоугольник (7,7,3,3), НЕ содержащий сушу. Единственный способ
        /// для этих пикселей получить верное расстояние - засев с границы rect из буфера; если он
        /// работает, под-прямоугольник совпадает с эталоном пиксель-в-пиксель.</summary>
        [ContextMenu("Self-Test: Coast Distance Transform Seam-Safe Partial")]
        public void SelfTestCoastDistanceTransformSeamSafe()
        {
            const int n = 11;
            var full = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(n, n);
            for (int i = 0; i < n * n; i++) full.IsLand[i] = false;
            full.IsLand[5 * n + 5] = true;
            WorldGen.Rendering.MapRaster.MapRasterizer.ComputeCoastDistanceRect(full, n, n, 20f, 0, 0, n, n);

            var partial = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(n, n);
            System.Array.Copy(full.IsLand, partial.IsLand, n * n);
            System.Array.Copy(full.CoastDistance, partial.CoastDistance, n * n);
            // Под-прямоугольник x∈[7,9], y∈[7,9] - суша (5,5) снаружи него.
            WorldGen.Rendering.MapRaster.MapRasterizer.ComputeCoastDistanceRect(partial, n, n, 20f, 7, 7, 3, 3);

            bool match = true;
            for (int y = 7; y < 10; y++)
                for (int x = 7; x < 10; x++)
                    if (Mathf.Abs(partial.CoastDistance[y * n + x] - full.CoastDistance[y * n + x]) > 0.001f)
                        match = false;

            Debug.Log(match
                ? "Self-Test Coast Distance Transform Seam-Safe Partial: PASS"
                : "Self-Test Coast Distance Transform Seam-Safe Partial: FAIL (partial sub-rect diverged from full DT - seam seeding broken)");
        }
```

- [ ] **Step 3: Реализовать `ComputeCoastDistanceRect`**

В `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs` добавить public static метод. Разместить сразу перед `BakePaintedFields` (то есть в блоке painted-хуков, после `ColorAndVignetteRect`/`ReapplyDarkness`/`ApplyDarknessRect` — конкретно перед комментарием `// ---- Painted-pipeline hooks ----` или сразу после него, но как отдельный public static метод класса `MapRasterizer`):

```csharp
        /// <summary>Заполняет buffers.CoastDistance для пикселей внутри rect: приближённое (chamfer,
        /// веса 1 / √2 - нормализованы в пиксели) расстояние до ближайшего пикселя суши по
        /// buffers.IsLand; суша = 0, вода = расстояние, клампленное сверху на maxDist (дальше
        /// свечению не нужно - там оно 0; заодно maxDist играет роль +∞ при инициализации).
        /// Соседей ЗА границей rect читает из уже заполненного buffers.CoastDistance (валидные
        /// значения предыдущего полного запека) - это делает частичный (кистью) пересчёт бесшовным.
        /// Границы проверяются по ИЗОБРАЖЕНИЮ (x-1>=0, x+1<w), не по rect, поэтому крайние пиксели
        /// rect читают внешних соседей из буфера. При полном запеке (rect = всё изображение) внешних
        /// соседей нет, инициализация воды = maxDist работает как +∞. См. design doc
        /// docs/superpowers/specs/2026-07-07-coastline-glow-width-design.md.</summary>
        public static void ComputeCoastDistanceRect(
            MapRasterBuffers buffers, int w, int h, float maxDist,
            int rectX, int rectY, int rectW, int rectH)
        {
            const float D1 = 1f;             // ортогональный шаг (1 пиксель)
            const float D2 = 1.41421356f;    // диагональный шаг (√2)

            // Инициализация: суша = 0, вода = maxDist (роль +∞ и клампа разом).
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    buffers.CoastDistance[idx] = buffers.IsLand[idx] ? 0f : maxDist;
                }

            // Прямой проход (сверху-слева вниз-вправо): релаксация от уже обработанных соседей
            // (in-rect - свежие этого прохода; out-of-rect - валидные из буфера прошлого запека).
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    float d = buffers.CoastDistance[idx];
                    if (x - 1 >= 0) d = Mathf.Min(d, buffers.CoastDistance[idx - 1] + D1);
                    if (y - 1 >= 0) d = Mathf.Min(d, buffers.CoastDistance[idx - w] + D1);
                    if (x - 1 >= 0 && y - 1 >= 0) d = Mathf.Min(d, buffers.CoastDistance[idx - w - 1] + D2);
                    if (x + 1 < w && y - 1 >= 0) d = Mathf.Min(d, buffers.CoastDistance[idx - w + 1] + D2);
                    buffers.CoastDistance[idx] = Mathf.Min(d, maxDist);
                }

            // Обратный проход (снизу-справа вверх-влево).
            for (int y = rectY + rectH - 1; y >= rectY; y--)
                for (int x = rectX + rectW - 1; x >= rectX; x--)
                {
                    int idx = y * w + x;
                    float d = buffers.CoastDistance[idx];
                    if (x + 1 < w) d = Mathf.Min(d, buffers.CoastDistance[idx + 1] + D1);
                    if (y + 1 < h) d = Mathf.Min(d, buffers.CoastDistance[idx + w] + D1);
                    if (x + 1 < w && y + 1 < h) d = Mathf.Min(d, buffers.CoastDistance[idx + w + 1] + D2);
                    if (x - 1 >= 0 && y + 1 < h) d = Mathf.Min(d, buffers.CoastDistance[idx + w - 1] + D2);
                    buffers.CoastDistance[idx] = Mathf.Min(d, maxDist);
                }
        }
```

- [ ] **Step 4: Проверить компиляцию и прогнать самотесты**

Если доступен Unity Editor без конфликта с открытым проектом пользователя: дождаться перекомпиляции без ошибок, затем правым кликом на `WorldMapRenderer` прогнать `Self-Test: Coast Distance Transform` и `Self-Test: Coast Distance Transform Seam-Safe Partial` — оба должны дать `PASS`. Существующие самотесты не затрагиваются (сигнатуры не менялись).

Если Editor недоступен (открыт у пользователя) — перечитать добавленный метод и тесты построчно: баланс скобок, индексы `idx = y*w+x`, границы `x-1>=0`/`x+1<w`, что `CoastDistance` добавлен и в класс, и в `CreateEmptyBuffers`. Отметить в отчёте, что компиляция проверена ручным ревью, не компилятором.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): add chamfer coast-distance transform (seam-safe partial)"
```

---

### Task 2: Потребление — широкое градиентное свечение + поле + проводка

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (два новых `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `MapRasterizer.ComputeCoastDistanceRect` + `MapRasterBuffers.CoastDistance` (Task 1); существующие `BakeFieldsRect`/`ColorForWaterPixel`/`BuildRasterConfig`/`ComputeTouchedPixelRect`.
- Produces: `MapRasterConfig.CoastlineGlowWidth` (`int`, default 16); сериализованное `WorldMapRenderer.coastlineGlowWidth` (`int`, `[Range(0,64)]`, default 16), проброшенное в конфиг; расширенный на `glowWidth` dirty-rect кисти.

- [ ] **Step 1: Добавить поле `CoastlineGlowWidth` в `MapRasterConfig`**

В `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`, в классе `MapRasterConfig`, сразу после поля `CoastlineSmoothness` (и его xml-doc):

```csharp
        public int CoastlineSmoothness = 3;

        /// <summary>Ширина светлого ореола берега со стороны воды, в пикселях (только Combined+
        /// SmoothBorders). 0 = нет свечения; масштабируется через поле дистанции CoastDistance,
        /// стоимость не зависит от ширины. См. design doc
        /// docs/superpowers/specs/2026-07-07-coastline-glow-width-design.md.</summary>
        public int CoastlineGlowWidth = 16;
```

- [ ] **Step 2: Считать поле дистанции в `BakeFieldsRect` (painted-ветка)**

В методе `BakeFieldsRect`, в блоке `if (painted) { ... }`, ПОСЛЕ вычисления маски `IsLand` (после `if (loops.Count == 0) {...} else {...}`) и ПЕРЕД `BakePaintedFields(...)`, вставить вычисление дистанции (guard: только при ширине > 0):

Текущий блок:
```csharp
                else
                {
                    CoastlineContour.RasterizeIsLand(loops, buffers.IsLand, w, h, config.MapWidth, config.MapHeight, rectX, rectY, rectW, rectH);
                }
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
```

Стало:
```csharp
                else
                {
                    CoastlineContour.RasterizeIsLand(loops, buffers.IsLand, w, h, config.MapWidth, config.MapHeight, rectX, rectY, rectW, rectH);
                }
                if (config.CoastlineGlowWidth > 0)
                    ComputeCoastDistanceRect(buffers, w, h, config.CoastlineGlowWidth + 1f, rectX, rectY, rectW, rectH);
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
```

- [ ] **Step 3: Заменить 1px-свечение на градиентное в `ColorForWaterPixel`**

В `ColorForWaterPixel` заменить текущий блок свечения:
```csharp
            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: false))
            {
                float gk = 0.32f + coldAmt * 0.5f;
                r += (palette.Glow.r - r) * gk;
                g += (palette.Glow.g - g) * gk;
                b += (palette.Glow.b - b) * gk;
            }
```
на градиентное по полю дистанции:
```csharp
            // Свечение берега со стороны воды - широкий мягкий ореол по полю дистанции CoastDistance
            // (см. ComputeCoastDistanceRect): полная сила у самой кромки, плавно до нуля на
            // расстоянии CoastlineGlowWidth пикселей. Ширину задаёт поле; стоимость не зависит от
            // неё (в отличие от старой пососедней проверки в 1px). Тёмная обводка суши не трогается.
            float glowWidth = config.CoastlineGlowWidth;
            float glowT = glowWidth > 0f ? Mathf.Clamp01(1f - buffers.CoastDistance[y * w + x] / glowWidth) : 0f;
            if (glowT > 0f)
            {
                float gk = (0.32f + coldAmt * 0.5f) * glowT;
                r += (palette.Glow.r - r) * gk;
                g += (palette.Glow.g - g) * gk;
                b += (palette.Glow.b - b) * gk;
            }
```

(`HasNeighborWithWaterStatus` НЕ удаляется — его по-прежнему использует `ColorForLandPixel` для тёмной обводки суши со стороны суши.)

- [ ] **Step 4: Добавить сериализованное поле `coastlineGlowWidth` + проброс в конфиг**

В `Assets/WorldGen/Rendering/WorldMapRenderer.cs`, в блоке `[Header("Combined: тёмный рендер (MapRaster)")]`, сразу после `coastlineSmoothness` (и его атрибутов), перед `rasterLongSide`:

```csharp
        [Tooltip("Число итераций сглаживания Чайкина для контура берега (только Combined+smoothBorders). 0 = точные грани клеток Вороного (текущее поведение при выключенном сглаживании).")]
        [Range(0, 5)] public int coastlineSmoothness = 3;
        [Tooltip("Ширина светлого ореола берега со стороны воды, в пикселях (только Combined+smoothBorders). 0 = нет свечения. Масштабируется через поле дистанции - стоимость не зависит от ширины.")]
        [Range(0, 64)] public int coastlineGlowWidth = 16;
        [Tooltip("Большая сторона запекаемой текстуры карты в пикселях; меньшая считается по аспекту mapWidth:mapHeight.")]
        public int rasterLongSide = 2048;
```

В `BuildRasterConfig()`, сразу после `CoastlineSmoothness = coastlineSmoothness,`:

```csharp
                CoastlineSmoothness = coastlineSmoothness,
                CoastlineGlowWidth = coastlineGlowWidth,
```

- [ ] **Step 5: Расширить dirty-rect кисти на `glowWidth`**

В `ComputeTouchedPixelRect`, заменить строку вычисления отступа:
```csharp
            float pad = minPointDistance * 1.5f;
```
на (добавляем glowWidth пикселей, переведённых в мировые единицы; аспект текстуры сохраняет `mapWidth/texWidth == mapHeight/texHeight`, так что один множитель верен для обеих осей):
```csharp
            // Отступ = smoothRadius (протекание блендинга) + coastlineGlowWidth (ореол берега тянется
            // на столько пикселей от суши, поэтому пиксели в этой полосе должны пересчитаться при
            // правке берега кистью). glowWidth в пикселях -> мировые единицы через worldPerPixel.
            float pad = minPointDistance * 1.5f + coastlineGlowWidth * (mapWidth / texWidth);
```

- [ ] **Step 6: Написать самотесты (градиент + glowWidth=0)**

Добавить в `WorldMapRenderer.cs` после `SelfTestCoastDistanceTransformSeamSafe` (из Task 1):

```csharp
        /// <summary>Градиентное свечение: остров (центральная клетка 3x3) на текстуре 30x30 над
        /// картой 3x3 (10px/ед.), CoastlineSmoothness=0 (берег ровно по грани клетки x=1.5→пиксель 15),
        /// glowWidth=8. Дельта цвета водного пикселя от того же пикселя, запечённого с glowWidth=0
        /// (без свечения) = вклад ореола. Проверка: у кромки (dist≈1) вклад заметно больше, чем на
        /// ~4px, а дальше glowWidth (~10px) вклада нет вовсе.</summary>
        [ContextMenu("Self-Test: Coastline Glow Gradient")]
        public void SelfTestCoastlineGlowGradient()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    bool isCenter = c == 1 && r == 1;
                    var cell = new VoronoiCell(nextId++, new System.Numerics.Vector2(c, r))
                    {
                        Biome = isCenter ? Biome.Grassland : Biome.Ocean,
                        IsOcean = !isCenter,
                    };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    fixtureCells.Add(cell);
                }
            var fixtureById = fixtureCells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 1f);

            WorldGen.Rendering.MapRaster.MapRasterConfig MakeConfig(int glowWidth) => new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 30, TexHeight = 30, MapWidth = 3f, MapHeight = 3f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 0, CoastlineGlowWidth = glowWidth,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 0.6f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            Color BakePixel(int glowWidth, int px, int py)
            {
                var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(30, 30);
                var tex = new Texture2D(30, 30, TextureFormat.RGBA32, false);
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, MakeConfig(glowWidth), tex, buffers, 0, 0, 30, 30);
                Color c = tex.GetPixel(px, py);
                Destroy(tex);
                return c;
            }

            float Delta(int px, int py)
            {
                Color on = BakePixel(8, px, py);
                Color off = BakePixel(0, px, py);
                return Mathf.Abs(on.r - off.r) + Mathf.Abs(on.g - off.g) + Mathf.Abs(on.b - off.b);
            }

            // Все три пикселя - вода справа от острова (грань суши на x=1.5 → пиксель 15), y=10.
            float nearDelta = Delta(16, 10); // ~1px от берега
            float midDelta = Delta(19, 10);  // ~4px
            float farDelta = Delta(25, 10);  // ~10px > glowWidth 8

            bool ok = nearDelta > midDelta && midDelta > 0.001f && farDelta < 0.001f;
            Debug.Log(ok
                ? "Self-Test Coastline Glow Gradient: PASS"
                : $"Self-Test Coastline Glow Gradient: FAIL (nearDelta={nearDelta:F3}, midDelta={midDelta:F3}, farDelta={farDelta:F3}; ожидалось near>mid>0 и far≈0)");
        }

        /// <summary>glowWidth=0 → свечения нет: водный пиксель у самой кромки берега равен базовому
        /// водному цвету (тому, что был бы вообще без прохода свечения). Регрессия на guard от
        /// деления на ноль и на "0 = выключено".</summary>
        [ContextMenu("Self-Test: Coastline Glow Zero Width Off")]
        public void SelfTestCoastlineGlowZeroWidthOff()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    bool isCenter = c == 1 && r == 1;
                    var cell = new VoronoiCell(nextId++, new System.Numerics.Vector2(c, r))
                    {
                        Biome = isCenter ? Biome.Grassland : Biome.Ocean,
                        IsOcean = !isCenter,
                    };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    fixtureCells.Add(cell);
                }
            var fixtureById = fixtureCells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 30, TexHeight = 30, MapWidth = 3f, MapHeight = 3f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 0, CoastlineGlowWidth = 0,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 0.6f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(30, 30);
            var tex = new Texture2D(30, 30, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 30, 30);

            // Базовый водный цвет ColdTwilight shallow (30,84,100) без ряби (для океанской клетки
            // рябь есть - поэтому сравниваем "нет сдвига в сторону Glow", а не точное равенство):
            // при glowWidth=0 пиксель у кромки не должен быть ближе к Glow (120,200,214), чем
            // пиксель глубоко в воде (оба - только базовый цвет + рябь, без ореола).
            Color shorePixel = tex.GetPixel(16, 10);   // ~1px от берега
            Color deepPixel = tex.GetPixel(28, 10);    // глубоко в воде, у края карты
            // Color32 (байты 0-255), НЕ Color - иначе неявная конверсия нормализовала бы в 0-1
            // и деление на 255 ниже стало бы неверным.
            Color32 glow = WorldGen.Rendering.MapRaster.MapPalette.GetSlotColor(
                WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight, WorldGen.Rendering.MapRaster.PaletteSlot.Glow);

            float DistToGlow(Color c) => Mathf.Abs(c.r - glow.r / 255f) + Mathf.Abs(c.g - glow.g / 255f) + Mathf.Abs(c.b - glow.b / 255f);
            // Без свечения близость к Glow у кромки и в глубине примерно одинакова (разница только
            // от ряби, малая ~0.12); свечение сделало бы shorePixel заметно ближе к Glow (сдвиг ~0.4+).
            bool noGlowHalo = Mathf.Abs(DistToGlow(shorePixel) - DistToGlow(deepPixel)) < 0.2f;

            Destroy(tex);
            Debug.Log(noGlowHalo
                ? "Self-Test Coastline Glow Zero Width Off: PASS"
                : "Self-Test Coastline Glow Zero Width Off: FAIL (пиксель у кромки заметно ближе к Glow при glowWidth=0 - свечение не выключилось)");
        }
```

- [ ] **Step 7: Проверить компиляцию и прогнать ВСЕ самотесты**

Если доступен Unity Editor без конфликта: дождаться перекомпиляции, правым кликом на `WorldMapRenderer` прогнать 4 новых самотеста этой фичи (`Coast Distance Transform`, `Coast Distance Transform Seam-Safe Partial`, `Coastline Glow Gradient`, `Coastline Glow Zero Width Off`) — все `PASS`; перепрогнать существующие самотесты берега/подпроекта 1 — ни один не должен сломаться (сигнатуры `ColorForWaterPixel`/`BakeFieldsRect`/`RebakeRegion` не менялись, только тела/конфиг).

Если Editor недоступен — перечитать изменения построчно: поле в конфиге и в инспекторе, проброс в `BuildRasterConfig`, guard `CoastlineGlowWidth > 0` перед DT, формула `glowT`, `idx = y*w+x` в `ColorForWaterPixel`, отступ в `ComputeTouchedPixelRect`. Отметить в отчёте ручную проверку.

- [ ] **Step 8: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): wide graded coastline glow via coast-distance field"
```
