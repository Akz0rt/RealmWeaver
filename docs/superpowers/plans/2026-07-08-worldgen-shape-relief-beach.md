# Генерация: изрезанный материк, усиленный рельеф, прибрежный пляж — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Переработать вход генерации карты так, чтобы получался крупный изрезанный материк в бескрайнем океане, с выразительным рельефом и гарантированным тонким песчаным поясом по берегу.

**Architecture:** Тюнинг существующего Patel-style пайплайна `WorldGenerator` без смены модели данных клетки. Три независимых изменения: (A1) `HeightmapGenerator.ComputeFalloff` — квадратный Chebyshev-falloff → радиальный с береговым шумом, джиттером центра по сидам и гарантированной водной кромкой; (A2) `CellClimateAverager` — контраст высоты вокруг середины до классификации биома; (A3) новый шаг `BeachClassifier` — суша, смежная с океаном, → `Biome.Beach` (высотное правило пляжа в генерации подавляется, `BiomeClassifier.Classify` и live-edit не трогаются). Плюс маленький render-companion: фон камеры карты = цвет глубокого океана.

**Tech Stack:** Unity 2022.3 (Built-in RP), C# (чистый слой `Assets/WorldGen/Generation/` без `UnityEngine`), `FastNoiseLite` (однофайловая либа, уже в проекте), Newtonsoft.Json (сериализация проекта).

## Global Constraints

- Слой генерации (`Assets/WorldGen/Generation/`) остаётся чистым C# без зависимости от `UnityEngine`. Всё Unity-зависимое (self-тесты, камера) — в `Assets/WorldGen/Rendering/`.
- Все правки вносятся в **оба** пути генерации: `WorldGenerator.GenerateWorld` (строки ~136, 151-166) и `WorldGenerator.GenerateWorldStepped` (строки ~211, 223-245). Они обязаны остаться поведенчески эквивалентны.
- Модель данных `VoronoiCell` не меняется (никаких новых персистентных полей) — старые `.dndproj` грузятся без изменений.
- **`BiomeClassifier.Classify` сигнатуру НЕ менять** и `CellOverrideService`/live-edit субсистему НЕ трогать — они вне объёма A (это B). Генерация подавляет высотный пляж, передавая `beachElevationThreshold: 0f`.
- **Unity serialization gotcha (память проекта, ловили 3×):** сцена (`Assets/Scenes/SampleScene.unity`) хранит значения `[SerializeField]`-полей и перекрывает C#-инициализаторы. При смене дефолта СУЩЕСТВУЮЩЕГО сериализованного поля надо править и значение в сцене. НОВЫЕ поля стора не имеют → подхватывают C#-дефолт сами, но их появление в Инспекторе стоит подтвердить.
- Агенты не запускают Unity. Каждый шаг «проверить компиляцию» / «прогнать self-тест» / «регенерировать и глянуть» выполняется пользователем в открытом Editor. Формулируем ожидаемый результат явно.
- Self-тесты по конвенции проекта: `[ContextMenu("Self-Test: ...")]` на `WorldMapRenderer`, строят маленькую фикстуру и `Debug.Log("...: PASS/FAIL")`.
- Коммиты в конце каждой задачи. Ветка `map-terrain-raster` (worktree). `.meta`-файл нового `.cs` генерирует Unity при импорте — застейджить после импорта.

## File overview

**Изменяются:**
- `Assets/WorldGen/Generation/HeightmapGenerator.cs` — радиальный falloff + береговой шум + джиттер центра + водная кромка + новые параметры конструктора (Task 1).
- `Assets/WorldGen/Generation/WorldGenerator.cs` — новые/изменённые поля `GenerationParams`; проброс новых falloff-параметров и `ElevationContrast`; удаление `BeachElevationThreshold`; вызов `BeachClassifier` в обоих путях (Tasks 1-3).
- `Assets/WorldGen/Generation/CellClimateAverager.cs` — helper контраста + его применение; смена сигнатуры `ApplyToCells` (Tasks 2-3).
- `Assets/WorldGen/Rendering/WorldMapRenderer.cs` — новые сериализованные поля-ручки + их проброс в `BuildGenerationParams`; удаление проброса `BeachElevationThreshold`; self-тесты; camera-bg (Tasks 1-4).
- `Assets/WorldGen/Rendering/MapScreenController.cs` — зеркальный проброс новых полей в своём `BuildGenerationParams`; удаление `BeachElevationThreshold` (Tasks 1-3).
- `Assets/Scenes/SampleScene.unity` — обновить значения изменённых сериализованных дефолтов (Tasks 1, 3).

**Создаётся:**
- `Assets/WorldGen/Generation/BeachClassifier.cs` (Task 3).

**НЕ трогаем:** `BiomeClassifier.cs`, `CellOverrideService.cs`, `ElevationField.cs`, `ValueRedistributor.cs`, `CornerOceanFloodFill.cs`, `CellWaterAssigner.cs`, moisture/temperature системы, `RegionGrowing.cs`, `LakeRegionUnifier.cs`, `ProjectSerializer*`.

---

### Task 1: A1 — Органический радиальный falloff в `HeightmapGenerator`

**Files:**
- Modify: `Assets/WorldGen/Generation/HeightmapGenerator.cs`
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs` (поля `GenerationParams` + проброс в оба `new HeightmapGenerator`)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (self-тест; сериализованные поля-ручки + проброс)
- Modify: `Assets/WorldGen/Rendering/MapScreenController.cs` (зеркальный проброс)
- Modify: `Assets/Scenes/SampleScene.unity` (значения `innerRadius`/`falloffPower`)

**Interfaces:**
- Produces: `HeightmapGenerator(int seed, float mapWidth, float mapHeight, float baseFrequency=0.01f, int octaves=4, float warpAmplitude=40f, float warpFrequency=0.01f, float falloffPower=1.8f, float innerRadius=0.2f, float coastRoughness=0.2f, float coastRoughnessFrequency=0.004f, float continentCenterJitter=0.18f, float borderWaterMargin=0.06f)`. Публичный `GetHeight(float x, float y)` не меняет сигнатуру.
- Produces (в `GenerationParams`): новые поля `CoastRoughness`, `CoastRoughnessFrequency`, `ContinentCenterJitter`, `BorderWaterMargin`; изменённые дефолты `InnerRadius=0.2f`, `FalloffPower=1.8f`.

- [ ] **Step 1: Переписать `HeightmapGenerator.cs`**

Заменить ВЕСЬ файл на:

```csharp
namespace WorldGen.Generation
{
    /// <summary>
    /// Генератор высоты рельефа: многослойный OpenSimplex2-шум с domain warping + island falloff,
    /// который топит края карты под уровень моря, формируя материк, окружённый океаном.
    ///
    /// Falloff — РАДИАЛЬНЫЙ (евклидов) от, возможно, смещённого по сиду центра материка, с добавкой
    /// низкочастотного "берегового" шума (изрезанность: полуострова/бухты) и гарантированной водной
    /// кромкой у самой границы карты (borderWaterMargin) — чтобы материк никогда не упирался в край
    /// и вода на краю текстуры бесшовно стыковалась с фоном редактора (см. camera-bg companion).
    ///
    /// ЗАВИСИМОСТЬ: FastNoiseLite.cs (однофайловая либа, лежит рядом в папке Generation).
    /// </summary>
    public class HeightmapGenerator
    {
        readonly FastNoiseLite baseNoise;
        readonly FastNoiseLite warpNoise;
        readonly FastNoiseLite coastNoise;
        readonly float mapWidth;
        readonly float mapHeight;
        readonly float falloffPower;
        readonly float innerRadius;
        readonly float coastRoughness;
        readonly float borderWaterMargin;
        readonly float centerOffsetX;
        readonly float centerOffsetY;

        public HeightmapGenerator(int seed, float mapWidth, float mapHeight, float baseFrequency = 0.01f, int octaves = 4,
                                    float warpAmplitude = 40f, float warpFrequency = 0.01f, float falloffPower = 1.8f,
                                    float innerRadius = 0.2f, float coastRoughness = 0.2f, float coastRoughnessFrequency = 0.004f,
                                    float continentCenterJitter = 0.18f, float borderWaterMargin = 0.06f)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.falloffPower = falloffPower;
            this.innerRadius = innerRadius;
            this.coastRoughness = coastRoughness;
            this.borderWaterMargin = borderWaterMargin;

            baseNoise = new FastNoiseLite(seed);
            baseNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            baseNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            baseNoise.SetFractalOctaves(octaves);
            baseNoise.SetFrequency(baseFrequency);

            warpNoise = new FastNoiseLite(seed + 1);
            warpNoise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2);
            warpNoise.SetDomainWarpAmp(warpAmplitude);
            warpNoise.SetFrequency(warpFrequency);

            // Низкочастотный шум для изрезанности берега - свой seed-сдвиг, чтобы не коррелировать.
            coastNoise = new FastNoiseLite(seed + 4000);
            coastNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            coastNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            coastNoise.SetFractalOctaves(3);
            coastNoise.SetFrequency(coastRoughnessFrequency);

            // Детерминированное по сиду смещение центра материка (в нормированном [-1,1] пространстве).
            var rng = new System.Random(seed + 5000);
            centerOffsetX = (float)(rng.NextDouble() * 2.0 - 1.0) * continentCenterJitter;
            centerOffsetY = (float)(rng.NextDouble() * 2.0 - 1.0) * continentCenterJitter;
        }

        /// <summary>
        /// Высота примерно в [0,1] (у края возможны отрицательные из-за falloff - это нормально,
        /// всё ниже SeaLevel считается водой).
        /// </summary>
        public float GetHeight(float x, float y)
        {
            float wx = x, wy = y;
            warpNoise.DomainWarp(ref wx, ref wy);

            float raw = baseNoise.GetNoise(wx, wy);   // [-1, 1]
            float normalized = (raw + 1f) * 0.5f;     // [0, 1]

            float falloff = ComputeFalloff(x, y);
            return normalized - falloff;
        }

        /// <summary>
        /// Радиальный falloff от смещённого центра материка + береговой шум, плюс гарантированная
        /// водная кромка у самой границы карты.
        /// </summary>
        float ComputeFalloff(float x, float y)
        {
            // Координаты относительно ЦЕНТРА КАРТЫ - для гарантии водной кромки по периметру.
            float mnx = 2f * (x / mapWidth) - 1f;
            float mny = 2f * (y / mapHeight) - 1f;
            float border = System.MathF.Max(System.MathF.Abs(mnx), System.MathF.Abs(mny));
            if (border > 1f - borderWaterMargin) return 1f; // гарантированный водный "ров" у края

            // Координаты относительно смещённого ЦЕНТРА МАТЕРИКА - для радиальной формы.
            float nx = mnx - centerOffsetX;
            float ny = mny - centerOffsetY;
            float d = System.MathF.Sqrt(nx * nx + ny * ny);

            // Изрезанность берега: гуляющий радиус. GetNoise∈[-1,1] → вклад ±0.5·coastRoughness.
            d += coastNoise.GetNoise(x, y) * 0.5f * coastRoughness;

            if (d < innerRadius) return 0f;

            float adjusted = (d - innerRadius) / (1f - innerRadius);
            adjusted = System.Math.Clamp(adjusted, 0f, 1f);
            return System.MathF.Pow(adjusted, falloffPower);
        }
    }
}
```

- [ ] **Step 2: Обновить `GenerationParams` в `WorldGenerator.cs`**

В `WorldGenerator.cs`, в блоке `// --- Island shape ... ---` (строки ~18-40): изменить дефолты `FalloffPower` и `InnerRadius` и добавить 4 новых поля. Заменить:

```csharp
        public float FalloffPower = 2.5f;
```
на
```csharp
        public float FalloffPower = 1.8f;
```

Заменить:
```csharp
        public float InnerRadius = 0.5f;
```
на
```csharp
        public float InnerRadius = 0.2f;

        /// <summary>Амплитуда низкочастотного берегового шума (изрезанность берега: полуострова/бухты). 0 = гладкий берег.</summary>
        public float CoastRoughness = 0.2f;
        /// <summary>Частота берегового шума. Меньше = крупные мысы/заливы; больше = мелкая рябь берега.</summary>
        public float CoastRoughnessFrequency = 0.004f;
        /// <summary>Разброс центра материка по сидам в нормированном [-1,1] пространстве (0 = всегда центр карты). Держать ≤ 0.2.</summary>
        public float ContinentCenterJitter = 0.18f;
        /// <summary>Доля у самой границы карты, гарантированно затопленная (водная кромка для бесшовной стыковки с фоном).</summary>
        public float BorderWaterMargin = 0.06f;
```

- [ ] **Step 3: Пробросить новые параметры в оба `new HeightmapGenerator`**

В `WorldGenerator.cs` заменить ОБА вхождения (в `GenerateWorld` ~строка 136 и в `GenerateWorldStepped` ~строка 211):

```csharp
            var islandShapeGen = new HeightmapGenerator(p.Seed, p.Width, p.Height, p.HeightFrequency, p.HeightOctaves,
                                                          p.WarpAmplitude, falloffPower: p.FalloffPower, innerRadius: p.InnerRadius);
```
на
```csharp
            var islandShapeGen = new HeightmapGenerator(p.Seed, p.Width, p.Height, p.HeightFrequency, p.HeightOctaves,
                                                          p.WarpAmplitude, falloffPower: p.FalloffPower, innerRadius: p.InnerRadius,
                                                          coastRoughness: p.CoastRoughness, coastRoughnessFrequency: p.CoastRoughnessFrequency,
                                                          continentCenterJitter: p.ContinentCenterJitter, borderWaterMargin: p.BorderWaterMargin);
```

- [ ] **Step 4: Добавить self-тест в `WorldMapRenderer.cs`**

Найти любой существующий `[ContextMenu("Self-Test: ...")]` метод (например `SelfTestBiomeFamilyCoverage`, ~строка 818) и добавить рядом:

```csharp
        [ContextMenu("Self-Test: Island Shape Ocean Border")]
        public void SelfTestIslandShapeOceanBorder()
        {
            var gen = new WorldGen.Generation.HeightmapGenerator(seed: 7, mapWidth: 500f, mapHeight: 500f);
            const float seaLevel = 0.35f;

            // Все 4 середины рёбер попадают в borderWaterMargin (0.06) → falloff=1 → высота < 0 < seaLevel.
            bool edgesWater =
                gen.GetHeight(250f, 2f)   < seaLevel &&
                gen.GetHeight(250f, 498f) < seaLevel &&
                gen.GetHeight(2f,   250f) < seaLevel &&
                gen.GetHeight(498f, 250f) < seaLevel;

            // Детерминизм: один сид → один результат.
            float a = gen.GetHeight(123f, 234f);
            var gen2 = new WorldGen.Generation.HeightmapGenerator(seed: 7, mapWidth: 500f, mapHeight: 500f);
            bool deterministic = gen2.GetHeight(123f, 234f) == a;

            bool ok = edgesWater && deterministic;
            Debug.Log(ok
                ? "Self-Test Island Shape Ocean Border: PASS"
                : $"Self-Test Island Shape Ocean Border: FAIL (edgesWater={edgesWater}, deterministic={deterministic})");
        }
```

- [ ] **Step 5: Добавить сериализованные ручки на `WorldMapRenderer` и пробросить в его `BuildGenerationParams`**

Найти блок сериализованных generation-полей на `WorldMapRenderer` (там уже `public float falloffPower;`, `public float innerRadius;`, `public float seaLevel;` и т.п. — рядом со строкой ~74). Добавить два user-facing поля (частоту берега и water-margin оставляем на дефолтах `GenerationParams`, не выносим в Инспектор — YAGNI):

```csharp
        [Header("Форма материка")]
        [Range(0f, 0.5f)] public float coastRoughness = 0.2f;
        [Range(0f, 0.2f)] public float continentCenterJitter = 0.18f;
```

В `WorldMapRenderer.BuildGenerationParams()` (~строки 2062-2096), рядом со строками, где уже присваиваются `FalloffPower`/`InnerRadius`, добавить:

```csharp
            p.CoastRoughness = coastRoughness;
            p.ContinentCenterJitter = continentCenterJitter;
```

- [ ] **Step 6: Зеркально пробросить в `MapScreenController.BuildGenerationParams`**

В `MapScreenController.BuildGenerationParams()` (~строки 77-114), рядом с `p.FalloffPower = mapRenderer.falloffPower;` (или аналогичной), добавить:

```csharp
            p.CoastRoughness = mapRenderer.coastRoughness;
            p.ContinentCenterJitter = mapRenderer.continentCenterJitter;
```

- [ ] **Step 7: Обновить значения `innerRadius`/`falloffPower` в сцене**

⚠️ Unity gotcha: сцена хранит старые значения и перекроет C#-дефолты. В `Assets/Scenes/SampleScene.unity` найти сериализованные поля компонента `WorldMapRenderer` `innerRadius:` и `falloffPower:` и заменить значения на `0.2` и `1.8` соответственно. (Новые поля `coastRoughness`/`continentCenterJitter` Unity добавит с C#-дефолтом при импорте — их править в сцене не нужно, но стоит подтвердить в Инспекторе, что они появились как 0.2/0.18.)

- [ ] **Step 8: Компиляция + self-тест + визуальная проверка (в Editor)**

В открытом Unity Editor: дождаться рекомпиляции, Console без ошибок. Выбрать GameObject с `WorldMapRenderer` → правый клик по заголовку компонента → "Self-Test: Island Shape Ocean Border". Ожидаемо: `Self-Test Island Shape Ocean Border: PASS`. Затем нажать «Сгенерировать» несколько раз с разными сидами — ожидаемо: материк радиально-органичный, изрезанный берег, океан по всему краю, разные сиды дают разные силуэты.

- [ ] **Step 9: Commit**

```bash
git add Assets/WorldGen/Generation/HeightmapGenerator.cs Assets/WorldGen/Generation/WorldGenerator.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapScreenController.cs Assets/Scenes/SampleScene.unity
git commit -m "feat(worldgen): organic radial island falloff + per-seed shape variety (A1)"
```

---

### Task 2: A2 — Контраст высоты в `CellClimateAverager`

**Files:**
- Modify: `Assets/WorldGen/Generation/CellClimateAverager.cs` (helper контраста + применение)
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs` (поле `GenerationParams.ElevationContrast` + проброс в оба `ApplyToCells`)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (self-тест; сериализованное поле + проброс)
- Modify: `Assets/WorldGen/Rendering/MapScreenController.cs` (зеркальный проброс)

**Interfaces:**
- Produces: `CellClimateAverager.ApplyContrast(float elevation, float contrast) -> float` (чистый helper, тестируемый). Изменённая сигнатура `ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float beachElevationThreshold, float elevationContrast)` (в этой задаче ДОБАВЛЯЕМ `elevationContrast` последним параметром; `beachElevationThreshold` пока сохраняется — его убирает Task 3).
- Consumes: `GenerationParams.ElevationContrast` (new, дефолт 1.5f).

- [ ] **Step 1: Добавить helper и применить контраст в `CellClimateAverager.cs`**

Добавить публичный helper (после открытия класса, перед `ApplyToCells`):

```csharp
        /// <summary>Контраст высоты вокруг середины: низины ниже, вершины выше. contrast=1 - без изменений.
        /// Вынесен отдельным методом, чтобы тестировать формулу напрямую (см. self-test).</summary>
        public static float ApplyContrast(float elevation, float contrast)
        {
            return System.Math.Clamp(0.5f + (elevation - 0.5f) * contrast, 0f, 1f);
        }
```

Изменить сигнатуру `ApplyToCells` (строка 17) — добавить `float elevationContrast` в конец:

```csharp
        public static void ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float beachElevationThreshold = 0.1f, float elevationContrast = 1f)
```

Внутри цикла (строка 38) заменить:

```csharp
                float avgElevation = cellCorners.Average(c => c.Elevation);
```
на
```csharp
                float avgElevation = ApplyContrast(cellCorners.Average(c => c.Elevation), elevationContrast);
```

(Контраст применяется ДО `cell.Height = avgElevation` и ДО `BiomeClassifier.Classify` — рельеф в рендере сильнее, биомы отражают усиленный рельеф.)

- [ ] **Step 2: Добавить `ElevationContrast` в `GenerationParams` и пробросить в оба `ApplyToCells`**

В `WorldGenerator.cs`, в блоке `// --- Elevation ... ---` (после `ElevationNoiseOctaves`, ~строка 57) добавить:

```csharp
        /// <summary>Контраст высоты на клетке (вокруг середины). 1 = как есть; больше = выразительнее рельеф и больше высокогорья. Разумно 1.0-2.5.</summary>
        public float ElevationContrast = 1.5f;
```

Заменить ОБА вызова `ApplyToCells` (в `GenerateWorld` ~строка 166 и `GenerateWorldStepped` ~строка 245):

```csharp
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold);
```
на
```csharp
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold, p.ElevationContrast);
```

- [ ] **Step 3: Ручка на `WorldMapRenderer` + проброс в оба `BuildGenerationParams`**

На `WorldMapRenderer` добавить сериализованное поле (рядом с elevation-полями):

```csharp
        [Range(1f, 2.5f)] public float elevationContrast = 1.5f;
```

В `WorldMapRenderer.BuildGenerationParams()` добавить:
```csharp
            p.ElevationContrast = elevationContrast;
```

В `MapScreenController.BuildGenerationParams()` добавить:
```csharp
            p.ElevationContrast = mapRenderer.elevationContrast;
```

(Поле новое → сцена подхватит C#-дефолт 1.5 сама; править `SampleScene.unity` не нужно, но подтвердить в Инспекторе.)

- [ ] **Step 4: Self-тест в `WorldMapRenderer.cs`**

```csharp
        [ContextMenu("Self-Test: Elevation Contrast Widens Range")]
        public void SelfTestElevationContrastWidensRange()
        {
            float[] vals = { 0.30f, 0.42f, 0.50f, 0.58f, 0.70f };

            float Range(float contrast)
            {
                float mn = 1f, mx = 0f;
                foreach (var v in vals)
                {
                    float c = WorldGen.Generation.CellClimateAverager.ApplyContrast(v, contrast);
                    mn = Mathf.Min(mn, c);
                    mx = Mathf.Max(mx, c);
                }
                return mx - mn;
            }

            float spreadNeutral = Range(1f);
            float spreadBoosted = Range(1.5f);
            // Середина неподвижна, а точки вокруг неё расходятся → диапазон растёт.
            bool ok = spreadBoosted > spreadNeutral
                      && WorldGen.Generation.CellClimateAverager.ApplyContrast(0.5f, 1.5f) == 0.5f;

            Debug.Log(ok
                ? "Self-Test Elevation Contrast Widens Range: PASS"
                : $"Self-Test Elevation Contrast Widens Range: FAIL (neutral={spreadNeutral}, boosted={spreadBoosted})");
        }
```

- [ ] **Step 5: Компиляция + self-тест + визуальная проверка**

В Editor: Console чист. Прогнать "Self-Test: Elevation Contrast Widens Range" → ожидаемо `PASS`. Сгенерировать карту → рельеф заметно выразительнее (сильнее hillshade), чуть больше высокогорных/голых биомов — как принято.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Generation/CellClimateAverager.cs Assets/WorldGen/Generation/WorldGenerator.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapScreenController.cs
git commit -m "feat(worldgen): moderate elevation contrast for stronger relief (A2)"
```

---

### Task 3: A3 — Прибрежный пляж (`BeachClassifier`) + подавление высотного пляжа

**Files:**
- Create: `Assets/WorldGen/Generation/BeachClassifier.cs`
- Modify: `Assets/WorldGen/Generation/CellClimateAverager.cs` (убрать `beachElevationThreshold`, подавить высотный пляж)
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs` (убрать `GenerationParams.BeachElevationThreshold`; вызвать `BeachClassifier`; поправить оба `ApplyToCells`)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (убрать проброс `BeachElevationThreshold` в `BuildGenerationParams`; self-тест)
- Modify: `Assets/WorldGen/Rendering/MapScreenController.cs` (убрать проброс `BeachElevationThreshold`)
- Modify: `Assets/Scenes/SampleScene.unity` (удалить/оставить осиротевшее `beachElevationThreshold` — см. Step 6)

**Interfaces:**
- Produces: `BeachClassifier.AssignCoastalBeaches(List<VoronoiCell> cells)` — суша, смежная с океаном, → `Biome.Beach`.
- Consumes: `VoronoiCell.NeighborIds` (List<int>), `VoronoiCell.IsOcean` (bool), `VoronoiCell.Biome`, `VoronoiCell.Id`.
- Изменённая сигнатура `CellClimateAverager.ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float elevationContrast = 1f)` (убираем `beachElevationThreshold`).
- **НЕ трогаем** `BiomeClassifier.Classify` (остаётся с параметром `beachElevationThreshold`, его используют live-edit/override).

- [ ] **Step 1: Создать `BeachClassifier.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Пляж по смежности с океаном: клетка суши, у которой хоть один сосед — океан, становится
    /// Biome.Beach. Даёт непрерывный тонкий песчаный кант в 1 клетку по всему океанскому берегу,
    /// независимо от высоты (в отличие от старого высотного правила в BiomeClassifier).
    ///
    /// Вызывается ПОСЛЕ CellClimateAverager (перезаписывает биом прибрежной суши) в обоих путях
    /// WorldGenerator. Озёрные берега не трогаем (пляж только против океана - по требованию дизайна).
    /// Мягкий переход песок→биом по coast-distance - это уже подпроект B (рендер).
    /// </summary>
    public static class BeachClassifier
    {
        public static void AssignCoastalBeaches(List<VoronoiCell> cells)
        {
            var byId = cells.ToDictionary(c => c.Id);
            foreach (var cell in cells)
            {
                if (cell.IsOcean || cell.Biome == Biome.Lake) continue; // только суша (не океан, не озеро)

                bool coastal = cell.NeighborIds.Any(id =>
                    byId.TryGetValue(id, out var neighbor) && neighbor.IsOcean);

                if (coastal) cell.Biome = Biome.Beach;
            }
        }
    }
}
```

- [ ] **Step 2: Убрать высотный пляж из генерации в `CellClimateAverager.cs`**

Изменить сигнатуру `ApplyToCells` — убрать `beachElevationThreshold`:

```csharp
        public static void ApplyToCells(List<VoronoiCell> cells, List<Corner> corners, float elevationContrast = 1f)
```

Изменить вызов `Classify` (строка 49) — передавать `beachElevationThreshold: 0f`, чтобы высотное правило пляжа НЕ срабатывало (пляж делает BeachClassifier):

```csharp
                // Beach определяется отдельным шагом BeachClassifier (по смежности с океаном),
                // поэтому высотное правило пляжа здесь подавляем порогом 0.
                cell.Biome = BiomeClassifier.Classify(avgElevation, avgMoisture, cell.IsOcean, isLake, beachElevationThreshold: 0f);
```

- [ ] **Step 3: В `WorldGenerator.cs` — убрать `BeachElevationThreshold`, поправить вызовы, добавить `BeachClassifier`**

Удалить поле из `GenerationParams` (строки ~73-74):
```csharp
        // --- Биом ---
        public float BeachElevationThreshold = 0.1f;
```
(удалить обе строки; если после этого блок `// --- Биом ---` пуст — удалить и комментарий).

Заменить ОБА вызова `ApplyToCells` (`GenerateWorld` ~строка 166, `GenerateWorldStepped` ~строка 245):
```csharp
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold, p.ElevationContrast);
```
на (плюс сразу за ним — новый шаг пляжа):
```csharp
            CellClimateAverager.ApplyToCells(cells, corners, p.ElevationContrast);
            BeachClassifier.AssignCoastalBeaches(cells);
```

- [ ] **Step 4: Убрать проброс `BeachElevationThreshold` из `WorldMapRenderer.BuildGenerationParams`**

Удалить строку `p.BeachElevationThreshold = beachElevationThreshold;` (~строка 2089). ⚠️ НЕ удалять сериализованное поле `WorldMapRenderer.beachElevationThreshold` (строка ~74) — оно продолжает питать live-edit/override субсистему (`CellOverrideService`), которую мы не трогаем.

- [ ] **Step 5: Убрать проброс `BeachElevationThreshold` из `MapScreenController.BuildGenerationParams`**

Удалить строку `p.BeachElevationThreshold = mapRenderer.beachElevationThreshold;` (~строка 99).

- [ ] **Step 6: Сцена — осиротевшее `beachElevationThreshold`**

`GenerationParams.BeachElevationThreshold` удалён, но сериализованное поле `WorldMapRenderer.beachElevationThreshold` в сцене (`SampleScene.unity:889`, `beachElevationThreshold: 0.1`) ОСТАЁТСЯ — оно всё ещё нужно live-edit. Никаких правок сцены в этом шаге не требуется (поле в сцене соответствует существующему C#-полю рендера). Просто подтвердить, что в Инспекторе `Beach Elevation Threshold` всё ещё присутствует (это ручка override-кистей, не генерации).

- [ ] **Step 7: Self-тест в `WorldMapRenderer.cs`**

```csharp
        [ContextMenu("Self-Test: Coastal Beach Classification")]
        public void SelfTestCoastalBeachClassification()
        {
            // Фикстура: c0 суша у океана → Beach; c2 суша без океанских соседей → без изменений;
            // c4 озёрная клетка у океана → не трогаем; c1 океан → не трогаем.
            var c0 = new VoronoiCell(0, new System.Numerics.Vector2(0f, 0f)) { IsOcean = false, Biome = Biome.Grassland };
            var c1 = new VoronoiCell(1, new System.Numerics.Vector2(1f, 0f)) { IsOcean = true,  Biome = Biome.Ocean };
            var c2 = new VoronoiCell(2, new System.Numerics.Vector2(0f, 1f)) { IsOcean = false, Biome = Biome.Grassland };
            var c3 = new VoronoiCell(3, new System.Numerics.Vector2(1f, 1f)) { IsOcean = false, Biome = Biome.Grassland };
            var c4 = new VoronoiCell(4, new System.Numerics.Vector2(2f, 0f)) { IsOcean = false, Biome = Biome.Lake };

            c0.NeighborIds.Add(1);          // сосед - океан
            c1.NeighborIds.Add(0);
            c2.NeighborIds.Add(3);          // сосед - суша
            c3.NeighborIds.Add(2);
            c4.NeighborIds.Add(1);          // озеро у океана

            var cells = new List<VoronoiCell> { c0, c1, c2, c3, c4 };
            WorldGen.Generation.BeachClassifier.AssignCoastalBeaches(cells);

            bool ok = c0.Biome == Biome.Beach      // прибрежная суша → пляж
                      && c2.Biome == Biome.Grassland // внутренняя суша → без изменений
                      && c1.Biome == Biome.Ocean     // океан → без изменений
                      && c4.Biome == Biome.Lake;      // озеро → без изменений

            Debug.Log(ok
                ? "Self-Test Coastal Beach Classification: PASS"
                : $"Self-Test Coastal Beach Classification: FAIL (c0={c0.Biome}, c2={c2.Biome}, c1={c1.Biome}, c4={c4.Biome})");
        }
```

- [ ] **Step 8: Импорт нового файла, компиляция, self-тест, визуал**

В Editor: дождаться импорта `BeachClassifier.cs` (сгенерится `.meta`) и рекомпиляции, Console чист. Прогнать "Self-Test: Coastal Beach Classification" → `PASS`. Сгенерировать карту → по всему океанскому берегу непрерывный песчаный кант в 1 клетку; внутренних «пляжей» в низинах больше нет.

- [ ] **Step 9: Commit**

```bash
git add Assets/WorldGen/Generation/BeachClassifier.cs Assets/WorldGen/Generation/BeachClassifier.cs.meta Assets/WorldGen/Generation/CellClimateAverager.cs Assets/WorldGen/Generation/WorldGenerator.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Rendering/MapScreenController.cs
git commit -m "feat(worldgen): coast-adjacency beach rim, drop elevation-based beach in generation (A3)"
```

**Известное ограничение (задокументировать, вне объёма A):** live-edit кистью (`CellOverrideService.RecomputeBiome`) по-прежнему использует высотный `beachElevationThreshold` и не знает про coast-adjacency-пляж — правка кистью не обновляет пляжный кант вживую (его ставит только генерация). Согласование live-edit с новой моделью пляжа — задача подпроекта B (там перерабатывается edit/render слой).

---

### Task 4: Companion (render-side) — фон камеры карты = цвет глубокого океана

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (установка `backgroundColor` камеры)

**Interfaces:**
- Consumes: `MapPalette.GetSlotColor(MapPaletteTheme theme, PaletteSlot.Abyss) -> Color32` (`Assets/WorldGen/Rendering/MapRaster/MapPalette.cs:60`) — тот же «глубокий океан», что рисует шейдер у дальней воды. Тема берётся из текущего поля темы рендерера (то же, что уходит в `GpuMapRenderer`).
- Consumes: `WorldMapRenderer.PositionCameraOverMap()` / поле `targetCamera` (камера карты; фон в коде сейчас не задаётся — задаётся впервые).

- [ ] **Step 1: Установить фон камеры в `PositionCameraOverMap`**

Найти `WorldMapRenderer.PositionCameraOverMap()` (~строка 2101) и в конец тела метода (при непустой `targetCamera`) добавить:

```csharp
            // Фон = цвет глубокого океана: вода на краю текстуры бесшовно перетекает в фон редактора,
            // и изрезанный материк читается как "суша в бескрайнем море" (см. A1 borderWaterMargin).
            if (targetCamera != null)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                Color32 abyss = WorldGen.Rendering.MapRaster.MapPalette.GetSlotColor(paletteTheme, WorldGen.Rendering.MapRaster.PaletteSlot.Abyss);
                targetCamera.backgroundColor = abyss;
            }
```

⚠️ Уточнить точное имя поля темы на `WorldMapRenderer` (в примере `paletteTheme` — если поле называется иначе, напр. `theme`/`currentTheme`, использовать фактическое имя; это то же значение, что рендерер передаёт в `GpuMapRenderer` при установке слотов). Если у рендерера нет прямого поля темы, взять тему из того же источника, что и `GpuMapRenderer` (см. `GpuMapRenderer.cs:104` `SetSlot(..., theme, ...)`).

- [ ] **Step 2: Компиляция + визуальная проверка**

В Editor: Console чист. Открыть экран карты → фон вокруг карты — глубокий сине-чёрный цвет океана (тон совпадает с `PaletteSlot.Abyss`), край воды карты сливается с фоном без видимого шва/квадратной обрезки.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map): camera background = deep-ocean color (land-in-sea illusion, A companion)"
```

---

## Self-Review

**1. Покрытие spec'а:**
- A1 (изрезанный материк, радиальный falloff, береговой шум, джиттер центра, водная кромка, вариативность по сидам) → Task 1. ✅
- A2 (умеренный контраст рельефа, параметр `ElevationContrast`, до классификации биома) → Task 2. ✅
- A3 (тонкий пляж по смежности с океаном, убрать высотное правило пляжа) → Task 3. ✅
- Companion (фон = океан) → Task 4. ✅
- Оба пути генерации (`GenerateWorld` + `GenerateWorldStepped`) правятся в Tasks 1-3. ✅
- Verify-пункты spec'а закрыты разведкой: сейв хранит клетки (старые сейвы целы); удаление поля безопасно (Newtonsoft Ignore); self-тесты — на `WorldMapRenderer`; bg-цвет = `PaletteSlot.Abyss` на `targetCamera`. ✅
- **Отклонение от spec'а (осознанное):** spec предлагал убрать `beachElevationThreshold` из сигнатуры `BiomeClassifier.Classify`; план этого НЕ делает (иначе тянется вся override-субсистема, 12+ мест — это B). Вместо этого генерация подавляет высотный пляж порогом `0f`. Интент дизайна (пляж = смежность с океаном, без внутренних высотных пляжей в генерации) сохранён. Задокументировано в Task 3.

**2. Плейсхолдеры:** нет TBD/TODO; весь код приведён целиком. Единственные «уточнить на месте» — точные номера строк (даны как ~ориентиры) и имя поля темы в Task 4 Step 1 (дана инструкция, как найти фактическое). Это не заглушки логики.

**3. Согласованность типов/имён:**
- `CellClimateAverager.ApplyToCells` меняется дважды по плану: Task 2 → `(cells, corners, beachElevationThreshold=0.1f, elevationContrast=1f)`; Task 3 → `(cells, corners, elevationContrast=1f)`. Вызовы в `WorldGenerator` обновляются в тех же задачах (Task 2 передаёт оба, Task 3 убирает beach-аргумент). Согласовано.
- `BeachClassifier.AssignCoastalBeaches(List<VoronoiCell>)` — сигнатура едина в определении (Task 3 Step 1), вызове (Step 3) и self-тесте (Step 7).
- `CellClimateAverager.ApplyContrast(float,float)` — едина в helper (Task 2 Step 1), применении (Step 1) и self-тесте (Step 4).
- `HeightmapGenerator` новый конструктор — параметры и их имена совпадают между определением (Task 1 Step 1) и вызовом (Step 3).
- `VoronoiCell` fixture в self-тестах использует реальные поля (`Id` через конструктор, `IsOcean`, `Biome`, `NeighborIds.Add`) — подтверждено чтением `VoronoiCell.cs`.

Проблем не найдено.
