# Генерация: океанское поле вокруг материка (enlarge-canvas) + jitter default — Implementation Brief

> Follow-up to sub-project A (`2026-07-08-worldgen-shape-relief-beach`). Single cohesive change; execute as one implementer task + review. Steps use `- [ ]`.

**Goal:** Материк сохраняет абсолютный размер и детализацию, а вокруг него во все стороны добавляется настоящее кольцо океана (генерация на увеличенном домене). Плюс мелкий твик: дефолт `continentCenterJitter` → 0.01.

**Approach (enlarge-canvas, two-field, no runaway):**
- Пользователь задаёт **размер материка** (`continentWidth/continentHeight`) и **долю океана** (`oceanPadding`).
- **Полный домен** генерации = `continent × (1 + 2·oceanPadding)`. На нём работают Poisson/Voronoi (больше клеток при той же плотности → материк сохраняет детализацию, кольцо получает новые клетки океана).
- **`mapWidth/mapHeight`** (поля, которые рендер/камера/GPU уже используют для кадрирования) становятся **производными** = полный домен, и **пересчитываются в начале каждой генерации** из `continentWidth+padding`. Рендер-код НЕ меняется (Explore подтвердил: quad/камера/GPU-текстура выводят границы ровно из `mapWidth/mapHeight`).
- **Falloff** переводится в координаты **материкового ядра**, центрированного в полном домене (origin = padding-отступ, размер = continent). Точки в кольце padding'а дают `d > 1` → falloff = 1 → океан автоматически.
- **Runaway-защита:** `BuildGenerationParams` читает СТАБИЛЬНЫЙ `continentWidth` (не `mapWidth`), поэтому `mapWidth = continent×(1+2·pad)` не растёт от генерации к генерации.

## Global Constraints
- Генерация (`Assets/WorldGen/Generation/`) — чистый C#, без `UnityEngine`.
- Оба пути: `WorldGenerator.GenerateWorld` и `GenerateWorldStepped`.
- Агенты не запускают Unity (Editor залочен) → транскрибируй + статически верифицируй; компиляцию/self-тесты/визуал проверяет пользователь в Editor.
- Unity serialization gotcha: НОВЫЕ поля (`continentWidth/Height`, `oceanPadding`) подхватят C#-дефолт при импорте. `mapWidth/mapHeight` теперь производные — их значение в сцене (сейчас 750) будет перезаписано в начале первой же генерации на `750×1.4=1050`; править сцену не нужно.
- Self-тесты — `[ContextMenu]` на `WorldMapRenderer`, как в остальном проекте.

## Файлы
- Modify: `Assets/WorldGen/Generation/HeightmapGenerator.cs` — falloff в координатах ядра (origin + core size).
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs` — `GenerationParams` +3 поля; проброс core/origin в оба `new HeightmapGenerator`.
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` — новые поля `continentWidth/Height`, `oceanPadding`; пересчёт `mapWidth/mapHeight` из continent+padding в начале генерации; `BuildGenerationParams`; дефолт `continentCenterJitter` 0.18→0.01; self-тесты.
- Modify: `Assets/WorldGen/Rendering/MapScreenController.cs` — зеркальный `BuildGenerationParams`; size-пресеты пишут `continentWidth/Height` вместо `mapWidth/Height`.
- Modify: `Assets/Scenes/SampleScene.unity` — значение `continentCenterJitter` (существующее поле) 0.18→0.01. (`continentWidth/Height`/`oceanPadding` — новые, не трогать.)

НЕ трогать: рендер-геометрию (`BuildQuadMesh`, `PositionCameraOverMap`, `MapCameraController`, `GpuMapRenderer`, `CellIdTexture`) — они уже читают `mapWidth/mapHeight` и авто-подхватят полный домен.

---

### Задача (одна): океанское поле + jitter default

**Step 1 — `GenerationParams` (+3 поля) в `WorldGenerator.cs`**

В блок island-shape параметров добавить:
```csharp
        /// <summary>Абсолютный размер материкового ядра по ширине (мир. единицы). Океан-кольцо добавляется ВОКРУГ него.</summary>
        public float ContinentWidth = 750f;
        /// <summary>Абсолютный размер материкового ядра по высоте.</summary>
        public float ContinentHeight = 750f;
        /// <summary>Доля океана, добавляемая с КАЖДОЙ стороны как поле voids вокруг материка (fraction от continent). 0 = без поля. Полный домен = continent×(1+2·oceanPadding).</summary>
        public float OceanPadding = 0.2f;
```
`Width/Height` остаются (= ПОЛНЫЙ домен, их выставляет `BuildGenerationParams`).

**Step 2 — `HeightmapGenerator` falloff в координатах ядра**

Конструктор: заменить `float mapWidth, float mapHeight` на размеры ЯДРА + origin. Новая сигнатура (остальные параметры без изменений):
```csharp
public HeightmapGenerator(int seed, float coreWidth, float coreHeight, float originX, float originY,
                          float baseFrequency = 0.01f, int octaves = 4, float warpAmplitude = 40f, float warpFrequency = 0.01f,
                          float falloffPower = 1.8f, float innerRadius = 0.2f, float coastRoughness = 0.2f,
                          float coastRoughnessFrequency = 0.004f, float continentCenterJitter = 0.18f, float borderWaterMargin = 0.06f)
```
Сохранить `originX/originY/coreWidth/coreHeight` в поля (заменив `mapWidth/mapHeight`). В `ComputeFalloff` нормировать относительно ядра, центрированного в домене:
```csharp
        float ComputeFalloff(float x, float y)
        {
            // Координаты относительно ЦЕНТРА ЯДРА (материка), смещённого в домене на origin.
            float mnx = 2f * ((x - originX) / coreWidth) - 1f;
            float mny = 2f * ((y - originY) / coreHeight) - 1f;
            float border = System.MathF.Max(System.MathF.Abs(mnx), System.MathF.Abs(mny));
            if (border > 1f - borderWaterMargin) return 1f; // водная кромка по краю ЯДРА (кольцо снаружи — тоже океан)

            float nx = mnx - centerOffsetX;
            float ny = mny - centerOffsetY;
            float d = System.MathF.Sqrt(nx * nx + ny * ny);
            d += coastNoise.GetNoise(x, y) * 0.5f * coastRoughness;

            if (d < innerRadius) return 0f;
            float adjusted = System.Math.Clamp((d - innerRadius) / (1f - innerRadius), 0f, 1f);
            return System.MathF.Pow(adjusted, falloffPower);
        }
```
`GetHeight(x,y)` не меняет сигнатуру (x,y — координаты ПОЛНОГО домена; для точек в кольце `border > 1` → возвращает `> 1-borderWaterMargin`? нет — для точек вне ядра `|mnx|>1`, поэтому `border>1 > 1-borderWaterMargin` → return 1 → океан). ✅

**Step 3 — `WorldGenerator` проброс core/origin (оба пути)**

Заменить ОБА `new HeightmapGenerator(p.Seed, p.Width, p.Height, ...)` на:
```csharp
            float padX = p.ContinentWidth * p.OceanPadding;
            float padY = p.ContinentHeight * p.OceanPadding;
            var islandShapeGen = new HeightmapGenerator(p.Seed, p.ContinentWidth, p.ContinentHeight, padX, padY,
                                                          p.HeightFrequency, p.HeightOctaves, p.WarpAmplitude,
                                                          falloffPower: p.FalloffPower, innerRadius: p.InnerRadius,
                                                          coastRoughness: p.CoastRoughness, coastRoughnessFrequency: p.CoastRoughnessFrequency,
                                                          continentCenterJitter: p.ContinentCenterJitter, borderWaterMargin: p.BorderWaterMargin);
```
`PoissonDiskSampling.Generate(p.Width, p.Height, ...)` и `VoronoiBuilder.Build(points, p.Width, p.Height)` НЕ трогать — `p.Width/Height` теперь полный домен (Step 4 выставляет). Всё downstream domain-agnostic.

**Step 4 — `WorldMapRenderer`: поля + пересчёт mapWidth + `BuildGenerationParams` + jitter default**

Добавить сериализованные поля (рядом с `mapWidth`/`mapHeight`):
```csharp
        [Header("Размер материка и океан вокруг")]
        public float continentWidth = 750f;
        public float continentHeight = 750f;
        [Range(0f, 1f)] public float oceanPadding = 0.2f;
```
Изменить существующее поле: `continentCenterJitter` C#-дефолт `0.18f` → `0.01f`.

Переобозначить `mapWidth/mapHeight` как ПРОИЗВОДНЫЕ: в НАЧАЛЕ `BuildGenerationParams()` (перед `return new GenerationParams{...}`) пересчитать поля из continent+padding, чтобы рендер/камера кадрировали полный домен:
```csharp
        GenerationParams BuildGenerationParams()
        {
            mapWidth  = continentWidth  * (1f + 2f * oceanPadding);   // полный домен = кадр рендера/камеры
            mapHeight = continentHeight * (1f + 2f * oceanPadding);
            return new GenerationParams
            {
                Seed = seed,
                Width = mapWidth,            // полный домен для Poisson/Voronoi
                Height = mapHeight,
                ContinentWidth = continentWidth,
                ContinentHeight = continentHeight,
                OceanPadding = oceanPadding,
                // ... остальные поля как были ...
            };
        }
```
(Пересчёт `mapWidth` в начале BuildGenerationParams — до генерации — гарантирует, что `PositionCameraOverMap`/quad/GPU увидят полный домен. `BuildGenerationParams` читает СТАБИЛЬНЫЙ `continentWidth`, не `mapWidth`, → нет runaway.)

**Step 5 — `MapScreenController`: зеркало + size-пресеты**

В `MapScreenController.BuildGenerationParams()` так же выставить `mapRenderer.mapWidth/mapHeight = continent×(1+2·pad)` в начале, и передать `ContinentWidth = mapRenderer.continentWidth`, `ContinentHeight = mapRenderer.continentHeight`, `OceanPadding = mapRenderer.oceanPadding` (+ `Width/Height = mapRenderer.mapWidth/mapHeight`).

Size-пресеты (`ApplyUiParamsToRenderer`, MapSizePreset Small/Medium/Large) сейчас пишут `mapRenderer.mapWidth/mapHeight` — переключить на `mapRenderer.continentWidth/continentHeight` (350/500/700), т.к. пресет задаёт размер МАТЕРИКА, а `mapWidth` теперь производный.

**Step 6 — Сцена**

В `SampleScene.unity` найти `continentCenterJitter:` под компонентом `WorldMapRenderer` → 0.18 → `0.01`. (Новые поля не трогать — подхватят C#-дефолты.)

**Step 7 — Self-тест в `WorldMapRenderer.cs`**

```csharp
        [ContextMenu("Self-Test: Ocean Padding Frames Continent")]
        public void SelfTestOceanPaddingFramesContinent()
        {
            // Материк 100×100, padding 0.25 → домен 150×150, origin (25,25), ядро [25..125].
            float core = 100f, pad = 0.25f, origin = core * pad; // 25
            var gen = new WorldGen.Generation.HeightmapGenerator(seed: 3, coreWidth: core, coreHeight: core, originX: origin, originY: origin);
            const float seaLevel = 0.35f;

            // Точка в кольце padding (за пределами ядра, напр. (10,75)) → falloff=1 → вода.
            bool ringIsWater = gen.GetHeight(10f, 75f) < seaLevel && gen.GetHeight(75f, 10f) < seaLevel
                            && gen.GetHeight(140f, 75f) < seaLevel && gen.GetHeight(75f, 140f) < seaLevel;
            // Центр ядра (75,75) обычно суша (falloff 0 внутри innerRadius) — детерминизм проверяем повтором.
            float c = gen.GetHeight(75f, 75f);
            var gen2 = new WorldGen.Generation.HeightmapGenerator(seed: 3, coreWidth: core, coreHeight: core, originX: origin, originY: origin);
            bool deterministic = gen2.GetHeight(75f, 75f) == c;

            bool ok = ringIsWater && deterministic;
            Debug.Log(ok ? "Self-Test Ocean Padding Frames Continent: PASS"
                         : $"Self-Test Ocean Padding Frames Continent: FAIL (ring={ringIsWater}, det={deterministic})");
        }
```
(Точки кольца: (10,75) даёт mnx = 2·((10-25)/100)-1 = -1.3 → border 1.3 > 0.94 → return 1 → вода. Детерминированно.)

**Step 8 — Компиляция + self-тесты + визуал (пользователь, Editor)**
Console чист; прогнать "Self-Test: Ocean Padding Frames Continent" (+ старый "Island Shape Ocean Border" всё ещё PASS — он строит генератор по-новому? НЕТ: старый self-тест звал `new HeightmapGenerator(seed, 500, 500)` по СТАРОЙ сигнатуре — его тоже надо обновить на новую сигнатуру `(seed, coreWidth, coreHeight, originX:0, originY:0)`; при origin 0 и core 500 поведение как раньше). Регенерировать → материк того же размера, вокруг заметное кольцо океана; покрутить `oceanPadding` слайдером.

**Step 9 — Commit**
```bash
git add Assets/WorldGen/Generation/HeightmapGenerator.cs Assets/WorldGen/Generation/WorldGenerator.cs \
        Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapScreenController.cs \
        Assets/Scenes/SampleScene.unity
git commit -m "feat(worldgen): ocean padding ring around continent (enlarge-canvas) + jitter default 0.01"
```

## Риски / заметки
- **Обновить старый self-тест `SelfTestIslandShapeOceanBorder`** (Task 1 A1) под новую конструкторную сигнатуру (`coreWidth/coreHeight/originX:0/originY:0`) — иначе не скомпилируется. Указано в Step 8.
- Проверить, что `IslandShapeAssigner`/`ElevationField` и прочие downstream не зависят от того, что материк раньше заполнял домен (они работают по corners/cells — не зависят).
- `PositionCameraOverMap` имеет `cameraPlacedOnce` guard: `mapWidth` пересчитывается в `BuildGenerationParams` ДО генерации/бейка, поэтому первая установка камеры уже видит полный домен. Если камера всё же кадрирует по старому — проверить порядок вызова BuildGenerationParams относительно PositionCameraOverMap (визуальная проверка пользователем).
- Runaway исключён: `continentWidth` — стабильный вход, `mapWidth` каждый раз пересчитывается из него.
