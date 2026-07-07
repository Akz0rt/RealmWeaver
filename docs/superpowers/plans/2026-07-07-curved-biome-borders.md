# Кривые границы биомов и полос высоты + округлость контуров — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В плоской заливке сгладить (искривить) внутренние границы двух категориальных полей суши — семейства биома и полосы высоты — тем же контуром, что и берег, плюс общий регулятор округлости (прореживание вершин) для берега/биомов/полос.

**Architecture:** Обобщаем `CoastlineContour`: предикат категории клетки — параметр (берег = частный случай «вода/не-вода»). Перед Chaikin-сглаживанием прорежаем петлю по длине дуги (`BorderRoundnessDistance`). Новый `RasterizeRegionLabel` пишет метку категории только внутри петли. В `MapRasterizer` общий помощник `RasterizeSmoothedCategoryRect` сбрасывает буфер-метку в rect, находит присутствующие категории и растеризует их по приоритету в один буфер (`FamilyLabel`/`BandLabel`). `ColorForLandPixelFlat` берёт цвет семейства и полосу из метки (или откат к ближайшей клетке при метке -1). Всё активно только в Combined+SmoothBorders+FlatRegionFill+SmoothRegionBorders.

**Tech Stack:** Unity 6000.3.2f1, C# (Built-in Render Pipeline), `System.Numerics.Vector2` (геометрия контура), `UnityEngine.Mathf` (растеризация).

## Global Constraints

- Активно ТОЛЬКО когда `displayMode == Combined && config.SmoothBorders && config.FlatRegionFill && config.SmoothRegionBorders`. При любом из false — путь плоской заливки/блендинга работает как сейчас (метки не читаются).
- Новые поля конфига: `MapRasterConfig.SmoothRegionBorders` (bool, дефолт **`false`**) и `.BorderRoundnessDistance` (float, дефолт **`0f`**). Дефолты намеренно «выкл/без прореживания», чтобы существующие самотесты, не задающие эти поля, шли прежним путём (как с `FlatRegionFill`). Пользовательский дефолт «вкл/1.0» — у сериализованных `WorldMapRenderer.smoothRegionBorders = true` и `.borderRoundness = 1f` (Task 4).
- Новые буферы: `MapRasterBuffers.FamilyLabel` (int[]) и `.BandLabel` (int[]), сентинел `-1` = «нет метки». В `CreateEmptyBuffers` инициализируются `-1`.
- Семейства сглаживаются по индексу `BiomeFamily` суши; вода (`EffectiveIsOcean||EffectiveIsLake`) → категория `-1` (регионы ограничены сушей). Полосы: `Clamp((int)(EffectiveElevation*ElevationBands),0,ElevationBands-1)` суши, `-1` для воды; проход полос только при `ElevationBands > 1`.
- Порядок приоритета семейств (младший→старший, старший перезаписывает младший на перекрытиях): `Plains, Moor, Forest, ForestWarm, Coast, Tundra, Highland, Badlands, Snow`. Порядок полос — по возрастанию индекса.
- Прореживание (`DecimateClosedLoop`): петли с ≤ 8 вершинами не прорежаются; результат не короче 4 вершин; `distance ≤ 0` → без изменений. Применяется к берегу, семействам и полосам (общий код в `TraceSmoothedLoops`).
- Метка `FamilyLabel` никогда не содержит водных семейств (Sea/Lake), т.к. вода даёт категорию `-1` → `GetSlotColor` на метке безопасен. Пиксели с меткой `-1` (клинья тройных стыков) → откат к ближайшей клетке через существующий `FlatFamilyColor`/формулу полосы.
- НЕ меняются: `ColorForWaterPixel`, `ColorForLandPixel` (блендинг), `ComputeCoastDistanceRect`, `RasterizeIsLand`, водный контур берега как таковой (кроме добавления аргумента прореживания), генерация, кисть-контроллер.
- Среда без пакетной компиляции (проект открыт в Editor пользователя): шаг «прогнать тесты» — ручной (пользователь) либо построчная проверка при недоступном Editor; отмечать в отчёте.

---

### Task 1: Обобщить `CoastlineContour` — предикат категории, прореживание, `RasterizeRegionLabel`

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (2 новых `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `Corner` (`.Position` System.Numerics.Vector2, `.NeighborCornerIds`, `.TouchingCellIds`, `.Id`), `VoronoiCell.EffectiveIsOcean/EffectiveIsLake`, существующие приватные `SharedCellIds`/`AddBoundaryNeighbor`/`WalkClosedLoop`/`ChaikinSmoothClosed`.
- Produces:
  - `TraceSmoothedLoops(IReadOnlyList<Corner>, IReadOnlyDictionary<int,VoronoiCell>, Func<VoronoiCell,bool> inRegion, int smoothingIterations, float decimationDistance)` — обобщённый (5 арг).
  - `TraceSmoothedLoops(IReadOnlyList<Corner>, IReadOnlyDictionary<int,VoronoiCell>, int smoothingIterations, float decimationDistance = 0f)` — водная обёртка (сохраняет старый 3-арг вызов и именованный `smoothingIterations:`).
  - `RasterizeRegionLabel(IReadOnlyList<List<Vector2>> loops, int[] label, int labelValue, int texWidth, int texHeight, float mapWidth, float mapHeight, int rectX, int rectY, int rectW, int rectH)` — пишет `labelValue` только внутри петель.

- [ ] **Step 1: Обобщить `TraceSmoothedLoops` (предикат) + прореживание**

Сначала добавить `using System;` в начало `CoastlineContour.cs` (сейчас там только `System.Collections.Generic/Linq/Numerics` + `WorldGen.Generation`; обобщённый метод использует `Func<VoronoiCell,bool>` = `System.Func`):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldGen.Generation;
```

Затем заменить существующий метод `TraceSmoothedLoops` (строки 23–68, целиком от сигнатуры до закрывающей `}`) на обобщённую версию + водную обёртку + прореживание:

```csharp
        /// <summary>Трассирует все замкнутые петли границы категории (клетки, где inRegion различается
        /// по разные стороны ребра) и сглаживает каждую smoothingIterations итерациями Chaikin, с
        /// предварительным прореживанием вершин на decimationDistance (см. DecimateClosedLoop).
        /// Берег - частный случай (inRegion = IsWaterCell); семейства/полосы - другие предикаты (см.
        /// MapRasterizer.RasterizeSmoothedCategoryRect). Разомкнутые/вырожденные цепочки пропускаются.</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById,
            Func<VoronoiCell, bool> inRegion, int smoothingIterations, float decimationDistance)
        {
            var cornerById = new Dictionary<int, Corner>(corners.Count);
            foreach (var c in corners) cornerById[c.Id] = c;

            var boundaryNeighbors = new Dictionary<int, List<int>>();

            foreach (var corner in corners)
            {
                foreach (var neighborId in corner.NeighborCornerIds)
                {
                    if (neighborId <= corner.Id) continue; // каждое неориентированное ребро - один раз
                    if (!cornerById.TryGetValue(neighborId, out var neighbor)) continue;

                    var shared = SharedCellIds(corner, neighbor);
                    if (shared.Count != 2) continue; // ребро по краю карты (1 клетка) - не граница

                    bool in0 = cellById.TryGetValue(shared[0], out var c0) && inRegion(c0);
                    bool in1 = cellById.TryGetValue(shared[1], out var c1) && inRegion(c1);
                    if (in0 == in1) continue; // обе клетки одной категории - не граница

                    AddBoundaryNeighbor(boundaryNeighbors, corner.Id, neighbor.Id);
                    AddBoundaryNeighbor(boundaryNeighbors, neighbor.Id, corner.Id);
                }
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<int>();

            foreach (var startId in boundaryNeighbors.Keys)
            {
                if (visited.Contains(startId)) continue;
                var loopIds = WalkClosedLoop(startId, boundaryNeighbors, visited);
                if (loopIds == null) continue; // разомкнутая/вырожденная цепочка - пропускаем

                var points = loopIds.Select(id => cornerById[id].Position).ToList();
                points = DecimateClosedLoop(points, decimationDistance, MinDecimateVertices);
                loops.Add(ChaikinSmoothClosed(points, smoothingIterations));
            }

            return loops;
        }

        /// <summary>Водная обёртка (контур берега): inRegion = IsWaterCell. Сохраняет старую 3-арг
        /// сигнатуру (decimationDistance по умолчанию 0 = без прореживания).</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int smoothingIterations, float decimationDistance = 0f)
            => TraceSmoothedLoops(corners, cellById, IsWaterCell, smoothingIterations, decimationDistance);

        /// <summary>Петли с ≤ этого числа вершин не прорежаются (мелкие острова/полоски биома -
        /// защита от схлопывания).</summary>
        const int MinDecimateVertices = 8;

        /// <summary>Прорежает замкнутую петлю по длине дуги: оставляет вершину каждые minSegmentDistance
        /// мировых единиц вдоль контура. Реже вершины → крупнее радиусы Chaikin → круглее граница.
        /// minSegmentDistance ≤ 0 ИЛИ петля ≤ minKeepGuard вершин → возвращает исходную петлю без
        /// изменений; результат никогда не короче 4 вершин (иначе откат к исходной петле).</summary>
        static List<Vector2> DecimateClosedLoop(List<Vector2> points, float minSegmentDistance, int minKeepGuard)
        {
            if (minSegmentDistance <= 0f || points.Count <= minKeepGuard) return points;

            var kept = new List<Vector2> { points[0] };
            float acc = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                acc += Vector2.Distance(points[i - 1], points[i]);
                if (acc >= minSegmentDistance)
                {
                    kept.Add(points[i]);
                    acc = 0f;
                }
            }
            if (kept.Count < 4) return points; // прорезали слишком агрессивно - откат
            return kept;
        }
```

(Примечание: `IsWaterCell`, `SharedCellIds`, `AddBoundaryNeighbor`, `WalkClosedLoop`, `ChaikinSmoothClosed` уже есть в файле ниже — не трогать.)

- [ ] **Step 2: Добавить `RasterizeRegionLabel`**

Сразу ПОСЛЕ метода `RasterizeIsLand` (найти его закрывающую `}`, ~строка 115) добавить:

```csharp
        /// <summary>Как RasterizeIsLand (even-odd scanline), но пишет целочисленную метку labelValue
        /// ТОЛЬКО там, где пиксель ВНУТРИ петель, не затирая внешние пиксели. Позволяет растеризовать
        /// несколько категорий последовательно в один буфер-метку (старшая перезаписывает младшую на
        /// перекрытиях). Пишет только в [rectX,rectX+rectW) x [rectY,rectY+rectH).</summary>
        public static void RasterizeRegionLabel(
            IReadOnlyList<List<Vector2>> loops, int[] label, int labelValue,
            int texWidth, int texHeight, float mapWidth, float mapHeight,
            int rectX, int rectY, int rectW, int rectH)
        {
            var crossings = new List<float>();

            for (int y = rectY; y < rectY + rectH; y++)
            {
                float worldY = (y + 0.5f) / texHeight * mapHeight;

                crossings.Clear();
                foreach (var loop in loops)
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % n];
                        if ((a.Y <= worldY) == (b.Y <= worldY)) continue;
                        float t = (worldY - a.Y) / (b.Y - a.Y);
                        crossings.Add(a.X + t * (b.X - a.X));
                    }
                }
                crossings.Sort();

                int rowBase = y * texWidth;
                bool inside = false;
                int crossingIdx = 0;
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    float worldX = (x + 0.5f) / texWidth * mapWidth;
                    while (crossingIdx < crossings.Count && crossings[crossingIdx] <= worldX)
                    {
                        inside = !inside;
                        crossingIdx++;
                    }
                    if (inside) label[rowBase + x] = labelValue; // пишем ТОЛЬКО внутри
                }
            }
        }
```

- [ ] **Step 3: НЕ трогать вызов берега в этом таске**

Существующий 3-арг вызов в `MapRasterizer.BakeFieldsRect` (строка 192):
```csharp
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, config.CoastlineSmoothness);
```
после Step 1 связывается с водной обёрткой `(corners, cellById, int, float = 0f)` → компилируется и ведёт себя как раньше (прореживание 0). **Оставить как есть.** Апгрейд до 4-арг (`, config.BorderRoundnessDistance`) делается в Task 2 Step 4 — там же появляется само поле конфига. Так каждый таск компилируется независимо.

- [ ] **Step 4: Два самотеста в `WorldMapRenderer.cs`**

Добавить после `SelfTestCoastlineContourRasterizeIsLand` (найти его закрывающую `}`, ~строка 1214, вставить после, внутри класса):

```csharp
        /// <summary>Прореживание: петля из ~12 вершин (периметр блока суши 3x3 в сетке 5x5, окружён
        /// океаном) при decimationDistance>0 даёт МЕНЬШЕ вершин, чем при 0 (сравниваем при
        /// smoothingIterations=0, чтобы изолировать прореживание от Chaikin). Мелкая петля (одна
        /// клетка суши = 4 угла, ≤ 8) - защита: число вершин не меняется.</summary>
        [ContextMenu("Self-Test: Contour Decimation Reduces Vertices")]
        public void SelfTestContourDecimation()
        {
            // Сетка 5x5: центр 3x3 - суша (Grassland), рамка - океан → одна петля периметра ~12 вершин.
            List<VoronoiCell> BuildGrid(int size, System.Func<int, int, bool> isLand)
            {
                var cells = new List<VoronoiCell>();
                int id = 0;
                for (int r = 0; r < size; r++)
                    for (int c = 0; c < size; c++)
                    {
                        bool land = isLand(c, r);
                        var cell = new VoronoiCell(id++, new System.Numerics.Vector2(c, r))
                        { Biome = land ? Biome.Grassland : Biome.Ocean, IsOcean = !land };
                        cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                        cells.Add(cell);
                    }
                return cells;
            }

            var block = BuildGrid(5, (c, r) => c >= 1 && c <= 3 && r >= 1 && r <= 3);
            var blockById = block.ToDictionary(c => c.Id);
            var blockCorners = WorldGen.Generation.CornerGraphBuilder.Build(block);

            int Verts(List<List<System.Numerics.Vector2>> ls) { int n = 0; foreach (var l in ls) n += l.Count; return n; }

            var undec = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(blockCorners, blockById, smoothingIterations: 0, decimationDistance: 0f);
            var dec = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(blockCorners, blockById, smoothingIterations: 0, decimationDistance: 2f);
            bool reduced = Verts(dec) > 0 && Verts(dec) < Verts(undec);

            // Мелкая петля: одна клетка суши в центре 3x3, рамка океан → 4 угла ≤ 8 → защита.
            var single = BuildGrid(3, (c, r) => c == 1 && r == 1);
            var singleById = single.ToDictionary(c => c.Id);
            var singleCorners = WorldGen.Generation.CornerGraphBuilder.Build(single);
            var sUndec = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(singleCorners, singleById, smoothingIterations: 0, decimationDistance: 0f);
            var sDec = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(singleCorners, singleById, smoothingIterations: 0, decimationDistance: 5f);
            bool guarded = Verts(sUndec) == Verts(sDec) && Verts(sUndec) == 4;

            bool ok = reduced && guarded;
            Debug.Log(ok
                ? "Self-Test Contour Decimation Reduces Vertices: PASS"
                : $"Self-Test Contour Decimation Reduces Vertices: FAIL (reduced={reduced} undec={Verts(undec)} dec={Verts(dec)}; guarded={guarded} sUndec={Verts(sUndec)} sDec={Verts(sDec)})");
        }

        /// <summary>RasterizeRegionLabel пишет метку ТОЛЬКО внутри петли, не затирая внешние пиксели.
        /// Квадрат (2,2)-(8,8) в мире 10x10 (текстура 10x10, 1 тексель/ед). Буфер предзаполнен 7:
        /// центр (5,5) должен стать 3, угол (0,0) снаружи - остаться 7.</summary>
        [ContextMenu("Self-Test: Rasterize Region Label Writes Inside Only")]
        public void SelfTestRasterizeRegionLabel()
        {
            var square = new List<System.Numerics.Vector2> { new(2f, 2f), new(8f, 2f), new(8f, 8f), new(2f, 8f) };
            var loops = new List<List<System.Numerics.Vector2>> { square };

            const int size = 10;
            var label = new int[size * size];
            for (int i = 0; i < label.Length; i++) label[i] = 7;

            WorldGen.Rendering.MapRaster.CoastlineContour.RasterizeRegionLabel(loops, label, 3, size, size, 10f, 10f, 0, 0, size, size);

            bool insideSet = label[5 * size + 5] == 3;    // мир (5.5,5.5) внутри
            bool outsideKept = label[0 * size + 0] == 7;  // мир (0.5,0.5) снаружи - не затёрт

            bool ok = insideSet && outsideKept;
            Debug.Log(ok
                ? "Self-Test Rasterize Region Label Writes Inside Only: PASS"
                : $"Self-Test Rasterize Region Label Writes Inside Only: FAIL (insideSet={insideSet}, outsideKept={outsideKept})");
        }
```

- [ ] **Step 5: Проверка компиляции / прогон**

Если Editor доступен без конфликта: дождаться перекомпиляции без ошибок; прогнать 2 новых теста (`Contour Decimation Reduces Vertices`, `Rasterize Region Label Writes Inside Only`) → PASS; перепрогнать существующие тесты берега (`Coastline Contour Tracing`, `Coastline Contour Rasterize IsLand`, `Coastline Mask Matches Hard Categorization`) → не сломаны (3-арг вызовы связываются с водной обёрткой, decimation=0 → поведение прежнее).

Если недоступен — построчно: обобщённый `TraceSmoothedLoops` (предикат `inRegion`, вызов `DecimateClosedLoop` перед Chaikin); водная обёртка с `decimationDistance = 0f`; `DecimateClosedLoop` (аккумуляция длины, защита ≤8 и <4); `RasterizeRegionLabel` (`if (inside) label[...] = labelValue`); Step 3 (3-арг или 4-арг в зависимости от наличия поля). Отметить ручную проверку.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): generalize contour tracing to any cell predicate + vertex decimation + region-label rasterizer"
```

---

### Task 2: `MapRasterizer` — поля, буферы, категории, `RasterizeSmoothedCategoryRect`, запись меток

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (2 новых `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `CoastlineContour.TraceSmoothedLoops` (обобщённый, Task 1), `RasterizeRegionLabel` (Task 1), `MapPalette.GetFamily`/`BiomeFamily`, `MapRasterBuffers.CellId` (существует).
- Produces: `MapRasterConfig.SmoothRegionBorders` (bool, default false), `.BorderRoundnessDistance` (float, default 0); `MapRasterBuffers.FamilyLabel`/`.BandLabel` (int[]); `RasterizeSmoothedCategoryRect(...)`, `FamilyCategoryOf`, `BandCategoryOf`, `FamilyPriority`, `BandPriorityAscending` — потребляются `BakeFieldsRect` (пишет метки) и Task 3 (`ColorForLandPixelFlat` читает).

- [ ] **Step 1: Поля конфига**

В `MapRasterConfig`, сразу после `ElevationBandContrast` (и его xml-doc), перед `ShowBiomeLayer`:

```csharp
        public float ElevationBandContrast = 40f;

        /// <summary>Сглаживать (кривить) внутренние границы плоской заливки - семейств биомов и полос
        /// высоты - тем же контуром, что и берег (маски по категориям, см. RasterizeSmoothedCategoryRect).
        /// Дефолт здесь false (как FlatRegionFill): существующие самотесты плоской заливки, не задающие
        /// поле, идут прежним путём "ближайшая клетка". Пользовательский дефолт true - у сериализованного
        /// WorldMapRenderer.smoothRegionBorders. См. docs/superpowers/specs/2026-07-07-curved-biome-borders-design.md.</summary>
        public bool SmoothRegionBorders = false;

        /// <summary>Дистанция прореживания вершин контура (мировые единицы) перед Chaikin - для берега,
        /// границ семейств и полос. Реже вершины → круглее. 0 = без прореживания (текущее поведение).
        /// Резолвится в WorldMapRenderer.BuildRasterConfig как borderRoundness * minPointDistance.</summary>
        public float BorderRoundnessDistance = 0f;
```

- [ ] **Step 2: Буферы + инициализация меток -1**

В `MapRasterBuffers`, после поля `CoastDistance` (и его xml-doc):

```csharp
        public float[] CoastDistance;

        /// <summary>Семейство биома на пиксель для сглаженной плоской заливки (индекс BiomeFamily; -1 =
        /// нет метки → откат к ближайшей клетке). Только Combined+SmoothBorders+FlatRegionFill+
        /// SmoothRegionBorders. Водные семейства (Sea/Lake) сюда не попадают. См. RasterizeSmoothedCategoryRect.</summary>
        public int[] FamilyLabel;

        /// <summary>Полоса высоты на пиксель для сглаженной плоской заливки (индекс полосы; -1 = нет
        /// метки → откат к ближайшей клетке). Только при ElevationBands > 1 и SmoothRegionBorders.</summary>
        public int[] BandLabel;
```

Заменить тело `CreateEmptyBuffers` (объектный литерал `return new MapRasterBuffers { ... };`) на версию с инициализацией меток `-1`:

```csharp
        public static MapRasterBuffers CreateEmptyBuffers(int width, int height)
        {
            int n = width * height;
            var b = new MapRasterBuffers
            {
                Width = width,
                Height = height,
                CellId = new int[n],
                Elevation = new float[n],
                Temperature = new float[n],
                FamilyColor = new Color32[n],
                PreVignette = new Color32[n],
                IsLand = new bool[n],
                CoastDistance = new float[n],
                FamilyLabel = new int[n],
                BandLabel = new int[n],
            };
            for (int i = 0; i < n; i++) { b.FamilyLabel[i] = -1; b.BandLabel[i] = -1; }
            return b;
        }
```

- [ ] **Step 3: Категории, приоритеты, помощник `RasterizeSmoothedCategoryRect`**

Добавить сразу ПЕРЕД `FlatFamilyColor` (найти его xml-doc, ~строка 540):

```csharp
        static bool IsLandCell(VoronoiCell c) => !(c.EffectiveIsOcean || c.EffectiveIsLake);

        /// <summary>Категория "семейство биома" для сглаживания: индекс BiomeFamily суши, -1 для воды
        /// (регионы семейств ограничены сушей; Sea/Lake никогда не попадают в метку).</summary>
        static int FamilyCategoryOf(VoronoiCell c) => IsLandCell(c) ? (int)MapPalette.GetFamily(c.Biome) : -1;

        /// <summary>Категория "полоса высоты" для сглаживания: индекс полосы суши, -1 для воды.</summary>
        static int BandCategoryOf(VoronoiCell c, int bands) =>
            IsLandCell(c) ? Mathf.Clamp((int)(c.EffectiveElevation * bands), 0, bands - 1) : -1;

        /// <summary>Порядок приоритета семейств при композитинге масок (младший → старший; старший
        /// рисуется позже и выигрывает перекрытия). Характерные семейства (скалы/снег) сверху, чтобы их
        /// кривые читались чётко. См. design doc.</summary>
        static readonly int[] FamilyPriority =
        {
            (int)BiomeFamily.Plains, (int)BiomeFamily.Moor, (int)BiomeFamily.Forest, (int)BiomeFamily.ForestWarm,
            (int)BiomeFamily.Coast, (int)BiomeFamily.Tundra, (int)BiomeFamily.Highland, (int)BiomeFamily.Badlands, (int)BiomeFamily.Snow,
        };

        /// <summary>Порядок приоритета полос высоты: по возрастанию индекса (выше = сверху).</summary>
        static int[] BandPriorityAscending(int bands)
        {
            var order = new int[bands];
            for (int i = 0; i < bands; i++) order[i] = i;
            return order;
        }

        /// <summary>Общий проход "сгладить категориальное поле над сушей": сбрасывает labelBuffer в rect
        /// на -1, находит категории, встречающиеся в rect (rect уже расширен вызывающим на
        /// BorderRoundnessDistance - см. WorldMapRenderer.ComputeTouchedPixelRect), и для каждой в
        /// порядке priorityOrder трассирует+прорежает+сглаживает границу (categoryOf==cat) и растеризует
        /// метку ТОЛЬКО внутри (RasterizeRegionLabel). Старшая категория перезаписывает младшую на
        /// перекрытиях; пиксели без метки (клинья тройных стыков) остаются -1 → откат к ближайшей клетке
        /// в ColorForLandPixelFlat. Петли трассируются глобально, но здесь - только присутствующие в rect
        /// категории (на кисти это 1-3 из всех).</summary>
        static void RasterizeSmoothedCategoryRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners,
            MapRasterConfig config, MapRasterBuffers buffers,
            int[] labelBuffer, Func<VoronoiCell, int> categoryOf, IReadOnlyList<int> priorityOrder,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth;

            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                    labelBuffer[y * w + x] = -1;

            var present = new HashSet<int>();
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int cat = categoryOf(cellById[buffers.CellId[y * w + x]]);
                    if (cat >= 0) present.Add(cat);
                }
            if (present.Count == 0) return;

            foreach (int category in priorityOrder)
            {
                if (!present.Contains(category)) continue;
                int cat = category; // захват для лямбды
                var loops = CoastlineContour.TraceSmoothedLoops(
                    corners, cellById, c => categoryOf(c) == cat,
                    config.CoastlineSmoothness, config.BorderRoundnessDistance);
                if (loops.Count == 0) continue;
                CoastlineContour.RasterizeRegionLabel(
                    loops, labelBuffer, category, w, config.TexHeight,
                    config.MapWidth, config.MapHeight, rectX, rectY, rectW, rectH);
            }
        }
```

- [ ] **Step 4: Записать метки в `BakeFieldsRect`**

В `BakeFieldsRect`, в блоке `if (painted)`, заменить хвост (проверить наличие 4-арг вызова берега из Task 1 Step 3):

Было:
```csharp
                if (config.CoastlineGlowWidth > 0)
                    ComputeCoastDistanceRect(buffers, w, h, config.CoastlineGlowWidth + 1f, rectX, rectY, rectW, rectH);
                if (!config.FlatRegionFill)
                    BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }
```
Стало:
```csharp
                if (config.CoastlineGlowWidth > 0)
                    ComputeCoastDistanceRect(buffers, w, h, config.CoastlineGlowWidth + 1f, rectX, rectY, rectW, rectH);
                if (config.FlatRegionFill)
                {
                    if (config.SmoothRegionBorders)
                    {
                        RasterizeSmoothedCategoryRect(cellById, corners, config, buffers,
                            buffers.FamilyLabel, FamilyCategoryOf, FamilyPriority, rectX, rectY, rectW, rectH);
                        if (config.ElevationBands > 1)
                        {
                            int bands = config.ElevationBands;
                            RasterizeSmoothedCategoryRect(cellById, corners, config, buffers,
                                buffers.BandLabel, c => BandCategoryOf(c, bands), BandPriorityAscending(bands),
                                rectX, rectY, rectW, rectH);
                        }
                    }
                }
                else
                {
                    BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
                }
            }
```

Также апгрейднуть вызов берега (строка ~192) до 4-арг — теперь берег тоже получает прореживание (поле `BorderRoundnessDistance` добавлено в этом таске, Step 1):

Было:
```csharp
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, config.CoastlineSmoothness);
```
Стало:
```csharp
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, config.CoastlineSmoothness, config.BorderRoundnessDistance);
```

- [ ] **Step 5: Два самотеста в `WorldMapRenderer.cs`**

Добавить после `SelfTestRasterizeRegionLabel` (Task 1). Оба строят сетку 5x5 с ОКЕАНСКОЙ рамкой (иначе петли семейств у края карты не замкнутся — рёбра края с 1 клеткой пропускаются) и внутренним 3x3-блоком суши. Вызывают `BakeFieldsRect` напрямую и читают `buffers.FamilyLabel`/`.BandLabel`.

```csharp
        /// <summary>Метки семейств/полос: сетка 5x5, рамка - океан, внутренний 3x3 - суша; левый
        /// столбец внутреннего блока (c=1) - Snow (высота 0.9 → верхняя полоса), правые два (c=2,3) -
        /// Grassland (высота 0.1 → нижняя полоса). Оба региона окружены водой/друг другом → петли
        /// замкнуты. Глубинный пиксель каждого региона получает верную метку семейства и полосы.</summary>
        [ContextMenu("Self-Test: Smoothed Category Labels")]
        public void SelfTestSmoothedCategoryLabels()
        {
            var cells = new List<VoronoiCell>();
            int id = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    bool land = c >= 1 && c <= 3 && r >= 1 && r <= 3;
                    bool snow = land && c == 1;
                    var cell = new VoronoiCell(id++, new System.Numerics.Vector2(c, r))
                    {
                        Biome = !land ? Biome.Ocean : (snow ? Biome.Snow : Biome.Grassland),
                        IsOcean = !land,
                        Height = snow ? 0.9f : 0.1f,
                    };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    cells.Add(cell);
                }
            var byId = cells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(cells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 50, TexHeight = 50, MapWidth = 5f, MapHeight = 5f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 2, CoastlineGlowWidth = 0,
                FlatRegionFill = true, SmoothRegionBorders = true, BorderRoundnessDistance = 0f,
                ElevationBands = 5, ElevationBandContrast = 40f,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 1.5f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(50, 50);
            WorldGen.Rendering.MapRaster.MapRasterizer.BakeFieldsRect(cells, byId, lookup, corners, MapDisplayMode.Combined, config, buffers, 0, 0, 50, 50);

            // Глубинные пиксели (центры клеток, 10 текс/ед): Grassland (c=3,r=2)→(30,20); Snow (c=1,r=2)→(10,20).
            int grassIdx = 20 * 50 + 30;
            int snowIdx = 20 * 50 + 10;
            int plains = (int)WorldGen.Rendering.MapRaster.BiomeFamily.Plains;
            int snowFam = (int)WorldGen.Rendering.MapRaster.BiomeFamily.Snow;

            bool grassFamOk = buffers.FamilyLabel[grassIdx] == plains;
            bool snowFamOk = buffers.FamilyLabel[snowIdx] == snowFam;
            bool grassBandOk = buffers.BandLabel[grassIdx] == 0;  // 0.1*5=0
            bool snowBandOk = buffers.BandLabel[snowIdx] == 4;    // 0.9*5=4

            bool ok = grassFamOk && snowFamOk && grassBandOk && snowBandOk;
            Debug.Log(ok
                ? "Self-Test Smoothed Category Labels: PASS"
                : $"Self-Test Smoothed Category Labels: FAIL (grassFam={buffers.FamilyLabel[grassIdx]}/{plains}, snowFam={buffers.FamilyLabel[snowIdx]}/{snowFam}, grassBand={buffers.BandLabel[grassIdx]}/0, snowBand={buffers.BandLabel[snowIdx]}/4)");
        }

        /// <summary>Регрессия: одна категория суши + вода. Сетка 5x5, рамка океан, внутренний 3x3 весь
        /// Grassland (одна высота). Все глубинные пиксели суши получают метку Plains; водный пиксель
        /// остаётся -1 (сентинел, не затёрт). Без исключений.</summary>
        [ContextMenu("Self-Test: Smoothed Category Single Region")]
        public void SelfTestSmoothedCategorySingleRegion()
        {
            var cells = new List<VoronoiCell>();
            int id = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    bool land = c >= 1 && c <= 3 && r >= 1 && r <= 3;
                    var cell = new VoronoiCell(id++, new System.Numerics.Vector2(c, r))
                    { Biome = land ? Biome.Grassland : Biome.Ocean, IsOcean = !land, Height = 0.5f };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    cells.Add(cell);
                }
            var byId = cells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(cells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 50, TexHeight = 50, MapWidth = 5f, MapHeight = 5f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 2, CoastlineGlowWidth = 0,
                FlatRegionFill = true, SmoothRegionBorders = true, BorderRoundnessDistance = 1f,
                ElevationBands = 5, ElevationBandContrast = 40f,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 1.5f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(50, 50);
            bool threw = false;
            try
            {
                WorldGen.Rendering.MapRaster.MapRasterizer.BakeFieldsRect(cells, byId, lookup, corners, MapDisplayMode.Combined, config, buffers, 0, 0, 50, 50);
            }
            catch (System.Exception e) { threw = true; Debug.LogError($"Single-region bake threw: {e}"); }

            int plains = (int)WorldGen.Rendering.MapRaster.BiomeFamily.Plains;
            bool landLabeled = buffers.FamilyLabel[20 * 50 + 20] == plains && buffers.FamilyLabel[20 * 50 + 30] == plains;
            bool waterUnlabeled = buffers.FamilyLabel[0] == -1;  // угол (0,0) - океан, метки нет

            bool ok = !threw && landLabeled && waterUnlabeled;
            Debug.Log(ok
                ? "Self-Test Smoothed Category Single Region: PASS"
                : $"Self-Test Smoothed Category Single Region: FAIL (threw={threw}, landLabeled={landLabeled}, waterUnlabeled={waterUnlabeled})");
        }
```

**ВНИМАНИЕ (доступ к `BiomeFamily`):** enum `BiomeFamily` объявлен `public` в `MapPalette.cs` (namespace `WorldGen.Rendering.MapRaster`), поэтому `WorldGen.Rendering.MapRaster.BiomeFamily.Plains` доступен из теста. `MapRasterizer.BakeFieldsRect` и `CreateEmptyBuffers` — `public static`. Если `BakeFieldsRect` окажется недоступен из-за сигнатуры — использовать `RebakeRegion` (public) с той же фикстурой и читать те же индексы буфера (RebakeRegion внутри вызывает BakeFieldsRect).

- [ ] **Step 6: Проверка компиляции / прогон**

Editor доступен: перекомпиляция без ошибок; 2 новых теста → PASS; существующие тесты плоской заливки (`Flat Fill Merges Same-Biome Zones`, `Flat Fill Elevation Bands`, `Flat Fill Toggle Vs Blend`, `Flat Fill Coastal Fringe No Crash`) → не сломаны (они не задают `SmoothRegionBorders` → конфиг-дефолт false → метки не пишутся/не читаются, путь прежний).

Недоступен — построчно: 2 поля конфига; 2 буфера + `-1`-инициализация в `CreateEmptyBuffers`; `IsLandCell`/`FamilyCategoryOf`/`BandCategoryOf`/`FamilyPriority`/`BandPriorityAscending`/`RasterizeSmoothedCategoryRect` (сброс -1, сбор present, цикл приоритета, захват `cat`); хвост `BakeFieldsRect` (два прохода при `FlatRegionFill && SmoothRegionBorders`, полосы под `ElevationBands>1`, иначе `BakePaintedFields`); 4-арг вызов берега. Отметить ручную проверку.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): write smoothed FamilyLabel/BandLabel category masks in BakeFieldsRect"
```

---

### Task 3: `ColorForLandPixelFlat` читает метки семейства/полосы

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (1 новый `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `MapRasterBuffers.FamilyLabel`/`.BandLabel` (Task 2), `config.SmoothRegionBorders`, `MapPalette.GetSlotColor`, `BiomeFamily`, существующие `FlatFamilyColor`/`HasNeighborWithWaterStatus`/`ClampColor32`/`Noise.ValueNoise`.
- Produces: изменённый `ColorForLandPixelFlat` (тот же список параметров) — цвет семейства/полоса из метки, откат к ближайшей клетке при -1. Видимый эффект в проде появится после Task 4 (там `smoothRegionBorders=true`).

- [ ] **Step 1: Заменить `ColorForLandPixelFlat`**

Заменить метод целиком (от xml-doc до закрывающей `}`) на версию, читающую метки:

```csharp
        /// <summary>Плоская заливка суши (только Combined+SmoothBorders+FlatRegionFill): базовый цвет =
        /// семейство биома, модулированное дискретной полосой высоты (выше = светлее). При
        /// SmoothRegionBorders семейство и полоса берутся из СГЛАЖЕННЫХ меток (buffers.FamilyLabel/
        /// BandLabel); при метке -1 (клин тройного стыка) или выключенном сглаживании - откат к
        /// БЛИЖАЙШЕЙ клетке (как раньше). Плюс тёмная береговая обводка (1px) и лёгкое зерно.
        /// См. docs/superpowers/specs/2026-07-07-curved-biome-borders-design.md.</summary>
        static Color32 ColorForLandPixelFlat(
            VoronoiCell cell, MapRasterBuffers buffers,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette)
        {
            int idx = y * w + x;

            // Базовый цвет семейства: сглаженная метка (если есть) или ближайшая клетка (откат).
            Color32 fam;
            if (config.ShowBiomeLayer)
            {
                int fLabel = config.SmoothRegionBorders ? buffers.FamilyLabel[idx] : -1;
                fam = fLabel >= 0
                    ? MapPalette.GetSlotColor(config.Theme, (BiomeFamily)fLabel)
                    : FlatFamilyColor(config.Theme, cell.Biome);
            }
            else
            {
                fam = new Color32(209, 199, 166, 255);
            }
            float r = fam.r, g = fam.g, b = fam.b;

            // Полоса высоты (гейт ShowReliefLayer): сглаженная метка (если есть) или высота ближайшей клетки.
            if (config.ShowReliefLayer && config.ElevationBands > 1)
            {
                int band = config.SmoothRegionBorders ? buffers.BandLabel[idx] : -1;
                if (band < 0)
                    band = Mathf.Clamp((int)(cell.EffectiveElevation * config.ElevationBands), 0, config.ElevationBands - 1);
                float t = band / (float)(config.ElevationBands - 1);      // нормированная ступень [0,1]
                float factor = 1f + (t - 0.5f) * (config.ElevationBandContrast / 100f);
                r *= factor; g *= factor; b *= factor;
            }

            // Тёмная береговая обводка со стороны суши (1px, жёсткая замена).
            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: true))
            {
                r = palette.Outline.r; g = palette.Outline.g; b = palette.Outline.b;
            }

            // Лёгкое зерно - поверх, включая обводку.
            float grain = (Noise.ValueNoise(x * 0.5f, y * 0.5f, config.Seed + 61) - 0.5f) * 7f;
            r += grain; g += grain; b += grain;

            return ClampColor32(r, g, b);
        }
```

- [ ] **Step 2: Самотест паритета on/off + без краша**

Добавить после `SelfTestSmoothedCategorySingleRegion` (Task 2). Использует фикстуру двух семейств из Task 2 (Snow-столбец + Grassland), полный `RebakeRegion`, сравнивает глубинные пиксели при SmoothRegionBorders on/off — в глубине региона метка = семейство ближайшей клетки, поэтому цвет ДОЛЖЕН СОВПАСТЬ (сглаживание меняет только приграничные пиксели). Плюс отсутствие исключения.

```csharp
        /// <summary>Паритет глубины: в глубине региона сглаженная метка = семейство/полоса ближайшей
        /// клетки, поэтому цвет глубинного пикселя при SmoothRegionBorders on и off совпадает (сглаживание
        /// двигает только приграничные пиксели). Доказывает, что метки корректно питают цвет и путь не
        /// падает. Та же фикстура 5x5 (Snow-столбец + Grassland, рамка океан).</summary>
        [ContextMenu("Self-Test: Smoothed Flat Fill Interior Parity")]
        public void SelfTestSmoothedFlatFillInteriorParity()
        {
            var cells = new List<VoronoiCell>();
            int id = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    bool land = c >= 1 && c <= 3 && r >= 1 && r <= 3;
                    bool snow = land && c == 1;
                    var cell = new VoronoiCell(id++, new System.Numerics.Vector2(c, r))
                    {
                        Biome = !land ? Biome.Ocean : (snow ? Biome.Snow : Biome.Grassland),
                        IsOcean = !land, Height = snow ? 0.9f : 0.1f,
                    };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    cells.Add(cell);
                }
            var byId = cells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(cells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, 1f);

            Color Bake(bool smooth, int px, int py)
            {
                var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
                {
                    TexWidth = 50, TexHeight = 50, MapWidth = 5f, MapHeight = 5f, Seed = 1,
                    SmoothBorders = true, CoastlineSmoothness = 2, CoastlineGlowWidth = 0,
                    FlatRegionFill = true, SmoothRegionBorders = smooth, BorderRoundnessDistance = 0f,
                    ElevationBands = 5, ElevationBandContrast = 40f,
                    Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                    ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 1.5f,
                    ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                    ShowBiomeLayer = true, ShowReliefLayer = true,
                    HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
                };
                var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(50, 50);
                var tex = new Texture2D(50, 50, TextureFormat.RGBA32, false);
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(cells, byId, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 50, 50);
                Color col = tex.GetPixel(px, py);
                Destroy(tex);
                return col;
            }

            float D(Color p, Color q) => Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
            // Глубинный Grassland (30,20) и Snow (10,20) - вдали от границы Snow/Grass (px 15) и берега.
            bool grassParity = D(Bake(true, 30, 20), Bake(false, 30, 20)) < 0.01f;
            bool snowParity = D(Bake(true, 10, 20), Bake(false, 10, 20)) < 0.01f;

            bool ok = grassParity && snowParity;
            Debug.Log(ok
                ? "Self-Test Smoothed Flat Fill Interior Parity: PASS"
                : $"Self-Test Smoothed Flat Fill Interior Parity: FAIL (grassParity={grassParity} d={D(Bake(true, 30, 20), Bake(false, 30, 20)):F3}, snowParity={snowParity} d={D(Bake(true, 10, 20), Bake(false, 10, 20)):F3})");
        }
```

- [ ] **Step 3: Проверка компиляции / прогон**

Editor доступен: перекомпиляция; `Smoothed Flat Fill Interior Parity` → PASS; все тесты Task 1/Task 2 и существующие тесты плоской заливки/берега → не сломаны.

Недоступен — построчно: `ColorForLandPixelFlat` (idx; семейство из `fLabel>=0` иначе `FlatFamilyColor`; полоса из `band>=0` иначе формула; гейты `ShowBiomeLayer`/`ShowReliefLayer && ElevationBands>1`; обводка/зерно без изменений; `(BiomeFamily)fLabel` каст). Отметить ручную проверку.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): flat fill reads smoothed family/band labels with nearest-cell fallback"
```

---

### Task 4: Экспонировать `smoothRegionBorders`/`borderRoundness`, проброс, отступ dirty-rect

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `MapRasterConfig.SmoothRegionBorders`/`BorderRoundnessDistance` (Task 2), `minPointDistance` (существует).
- Produces: сериализованные `WorldMapRenderer.smoothRegionBorders` (bool, default true), `.borderRoundness` (float `[Range(0,3)]`, default 1); проброшены в `BuildRasterConfig`; `ComputeTouchedPixelRect` учитывает округлость в отступе. После этого таска сглаживание границ биомов/полос включено у пользователя по умолчанию.

- [ ] **Step 1: Сериализованные поля**

В блоке `[Header("Combined: тёмный рендер (MapRaster)")]`, сразу после `elevationBandContrast` (перед `rasterLongSide`):

```csharp
        [Range(0f, 100f)] public float elevationBandContrast = 40f;
        [Tooltip("Сглаживать (кривить) внутренние границы плоской заливки - семейств биомов и полос высоты - как берег (только Combined+smoothBorders+flatRegionFill). Выкл = грани по клеткам Вороного.")]
        public bool smoothRegionBorders = true;
        [Tooltip("Округлость контуров (берег, биомы, полосы): прореживание вершин перед сглаживанием, в долях среднего размера клетки. 0 = по всем вершинам (детальнее), выше = круглее.")]
        [Range(0f, 3f)] public float borderRoundness = 1f;
```

- [ ] **Step 2: Проброс в `BuildRasterConfig()`**

Сразу после `ElevationBandContrast = elevationBandContrast,`:

```csharp
                ElevationBandContrast = elevationBandContrast,
                SmoothRegionBorders = smoothRegionBorders,
                BorderRoundnessDistance = borderRoundness * minPointDistance,
```

- [ ] **Step 3: Отступ dirty-rect на округлость**

В `ComputeTouchedPixelRect`, строка с `pad`:

Было:
```csharp
            float pad = minPointDistance * 1.5f + coastlineGlowWidth * (mapWidth / texWidth);
```
Стало:
```csharp
            float pad = minPointDistance * 1.5f + coastlineGlowWidth * (mapWidth / texWidth) + borderRoundness * minPointDistance;
```

Обновить xml-doc метода: добавить, что отступ включает `borderRoundness * minPointDistance` (сглаженная/прореженная граница семейств/полос может сдвинуться на эту величину при правке кистью).

- [ ] **Step 4: Проверка компиляции / инспектор**

Editor доступен: перекомпиляция без ошибок; в инспекторе `WorldMapRenderer` (блок «тёмный рендер») появляются тумблер `smoothRegionBorders` (вкл) и слайдер `borderRoundness` (1). Перегенерировать карту / порисовать кистью в Combined+smoothBorders+flatRegionFill — границы биомов и полос высоты стали кривыми/округлыми; выключение `smoothRegionBorders` возвращает грани; `borderRoundness` меняет округлость вживую; перепрогнать `Coastline Mask Updates Within Brush Dirty Rect` — не сломан.

**Гочapha сериализации Unity:** после первой перекомпиляции проверить в инспекторе, что новые поля показывают дефолты `smoothRegionBorders=вкл`, `borderRoundness=1` (Unity может десериализовать существующий компонент со значением по умолчанию типа - false/0 - переопределяя инициализатор C#; при необходимости выставить вручную и сохранить сцену).

Недоступен — построчно: 2 поля с атрибутами внутри класса; 2 строки проброса (имена `SmoothRegionBorders`/`BorderRoundnessDistance` совпадают регистрозависимо); `pad` с добавленным слагаемым. Отметить ручную проверку.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): expose smoothRegionBorders/borderRoundness; pad dirty-rect for roundness"
```
