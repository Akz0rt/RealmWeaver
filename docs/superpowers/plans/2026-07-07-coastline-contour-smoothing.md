# Сглаживание контура берега — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить жёсткую (по ближайшей клетке Вороного) категоризацию пикселя суша/вода в растровом пайплайне на категоризацию по сглаженному (Chaikin), трассированному по графу углов контуру берега — только в режиме Combined+`smoothBorders`.

**Architecture:** Новый файл `CoastlineContour.cs` трассирует упорядоченные замкнутые петли границы вода/суша по уже существующему графу `Corner` (не завися от нового прохода по клеткам), сглаживает их и растеризует в булеву маску `IsLand` того же размера, что растровая текстура. `MapRasterizer` читает эту маску вместо теста «ближайшая клетка — океан/озеро?» только при `smoothBorders=true`; все прочие режимы (и рисование кистью в них) не меняются. Кисть получает живое обновление контура «бесплатно»: полный ретрейс петель — дешёвая операция масштаба «число клеток», растеризация — только в уже существующий dirty rect.

**Tech Stack:** Unity 6000.3.2f1, C# (Built-in Render Pipeline), `System.Numerics.Vector2` для геометрии (не `UnityEngine.Vector2` — см. существующий `Corner`/`VoronoiCell`).

## Global Constraints

- Новая категоризация активна ТОЛЬКО когда `displayMode == MapDisplayMode.Combined && config.SmoothBorders == true` — все прочие режимы (Height/Region/Biome/Combined-без-сглаживания) продолжают использовать точную границу по ближайшей клетке, без изменений.
- Новый параметр `coastlineSmoothness`: `int`, default `3`, диапазон `0–5`, без UI в этой работе (как остальные параметры рендера карты — сериализованное поле `WorldMapRenderer`, подпроект 6 добавит UI позже).
- Озёра становятся «дырками» в контуре суши через even-odd правило растеризации — специального кода для различения «океан vs озеро» на уровне петель не нужно.
- Побережье гарантированно не касается края карты (`HeightmapGenerator`'s falloff) → в штатном случае все петли замкнуты; разомкнутые/вырожденные цепочки должны быть пропущены (не растеризованы), НЕ бросать исключение.
- `MapBorderBuilder.cs`, `BrushToolController.cs`, `CornerGraphBuilder.cs`, `Corner.cs` не меняются в этой работе (декоративный риббон берега и кисть — вне scope, кисть получает новое поведение автоматически через существующий `RebakeAffectedCells` → `RebakeRegion` путь).
- Полный ретрейс контура (трассировка + Chaikin) выполняется заново при каждом вызове `BakeFieldsRect` (включая частичный, кистью) — НЕ инкрементально; растеризация в маску ограничена только переданным прямоугольником.

---

### Task 1: `CoastlineContour.cs` — трассировка замкнутых петель границы вода/суша

**Files:**
- Create: `Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (новый `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `WorldGen.Generation.Corner` (`Id`, `Position`, `NeighborCornerIds`, `TouchingCellIds`), `WorldGen.Generation.VoronoiCell` (`EffectiveIsOcean`, `EffectiveIsLake`), `WorldGen.Generation.CornerGraphBuilder.Build(List<VoronoiCell>)` — уже существуют, не меняются.
- Produces: `CoastlineContour.TraceSmoothedLoops(IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById, int smoothingIterations) : List<List<System.Numerics.Vector2>>` — используется Task 3.

- [ ] **Step 1: Написать `CoastlineContour.cs` с трассировкой и Chaikin-сглаживанием**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Строит гладкую границу суша/вода из уже посчитанного графа Corner (см. CornerGraphBuilder):
    /// трассирует упорядоченные замкнутые петли вдоль рёбер клеточных полигонов, где по одну
    /// сторону вода (океан или озеро), по другую суша, и сглаживает их Chaikin corner-cutting.
    /// См. docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md.
    /// </summary>
    public static class CoastlineContour
    {
        static bool IsWaterCell(VoronoiCell cell) => cell.EffectiveIsOcean || cell.EffectiveIsLake;

        /// <summary>Трассирует все замкнутые петли границы суша/вода (материк/острова И озёра —
        /// один и тот же проход, тип петли не важен, см. design doc) и сглаживает каждую
        /// smoothingIterations итерациями Chaikin. Разомкнутые/вырожденные цепочки (не должны
        /// встречаться в штатном случае — падение высоты к краю карты гарантирует замкнутость,
        /// см. design doc "Риски") пропускаются, не бросают исключение.</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById, int smoothingIterations)
        {
            var cornerById = new Dictionary<int, Corner>(corners.Count);
            foreach (var c in corners) cornerById[c.Id] = c;

            // boundaryNeighbors[cornerId] = Id соседних corners, с которыми этот corner связан
            // ребром на границе вода/суша (в невырожденной точке диаграммы Вороного - ровно 2,
            // см. design doc: чётность 2-раскраски треугольного цикла из 3 клеток, встречающихся
            // в одной точке).
            var boundaryNeighbors = new Dictionary<int, List<int>>();

            foreach (var corner in corners)
            {
                foreach (var neighborId in corner.NeighborCornerIds)
                {
                    if (neighborId <= corner.Id) continue; // каждое неориентированное ребро - один раз
                    if (!cornerById.TryGetValue(neighborId, out var neighbor)) continue;

                    var shared = SharedCellIds(corner, neighbor);
                    if (shared.Count != 2) continue; // ребро по краю карты (1 клетка) - не берег

                    bool water0 = cellById.TryGetValue(shared[0], out var c0) && IsWaterCell(c0);
                    bool water1 = cellById.TryGetValue(shared[1], out var c1) && IsWaterCell(c1);
                    if (water0 == water1) continue; // обе клетки одной категории - не граница

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
                loops.Add(ChaikinSmoothClosed(points, smoothingIterations));
            }

            return loops;
        }

        static List<int> SharedCellIds(Corner a, Corner b)
        {
            var result = new List<int>();
            foreach (var id in a.TouchingCellIds)
                if (b.TouchingCellIds.Contains(id))
                    result.Add(id);
            return result;
        }

        static void AddBoundaryNeighbor(Dictionary<int, List<int>> map, int fromId, int toId)
        {
            if (!map.TryGetValue(fromId, out var list))
            {
                list = new List<int>();
                map[fromId] = list;
            }
            if (!list.Contains(toId)) list.Add(toId);
        }

        /// <summary>Идёт по цепочке boundary-соседей от startId, каждый раз выбирая соседа,
        /// отличного от предыдущего corner, пока не вернётся в startId. null, если цепочка не
        /// замкнулась (открытый конец или вырожденный узел с числом boundary-соседей != 2).</summary>
        static List<int> WalkClosedLoop(int startId, Dictionary<int, List<int>> boundaryNeighbors, HashSet<int> visited)
        {
            var loop = new List<int> { startId };
            visited.Add(startId);

            int previousId = -1;
            int currentId = startId;
            int maxSteps = boundaryNeighbors.Count + 1;

            for (int step = 0; step < maxSteps; step++)
            {
                var neighbors = boundaryNeighbors[currentId];
                if (neighbors.Count != 2) return null; // открытый конец или вырожденный узел

                int nextId = neighbors[0] == previousId ? neighbors[1] : neighbors[0];

                if (nextId == startId) return loop; // петля замкнулась

                if (visited.Contains(nextId)) return null; // защита от вырожденной топологии

                loop.Add(nextId);
                visited.Add(nextId);
                previousId = currentId;
                currentId = nextId;
            }

            return null; // не замкнулась за разумное число шагов - вырождение, пропускаем
        }

        /// <summary>Chaikin corner-cutting: заменяет каждый отрезок A-B двумя точками
        /// 0.75A+0.25B и 0.25A+0.75B. iterations=0 - без изменений (текущее "гранёное" поведение).
        /// Сохраняет замкнутость петли на каждой итерации (индексация по модулю).</summary>
        static List<Vector2> ChaikinSmoothClosed(List<Vector2> points, int iterations)
        {
            if (points.Count < 3) return points;

            for (int iter = 0; iter < iterations; iter++)
            {
                var next = new List<Vector2>(points.Count * 2);
                int n = points.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % n];
                    next.Add(a + (b - a) * 0.25f);
                    next.Add(a + (b - a) * 0.75f);
                }
                points = next;
            }

            return points;
        }
    }
}
```

- [ ] **Step 2: Написать самотест трассировки в `WorldMapRenderer.cs`**

Добавить после метода `SelfTestLayerTogglesAffectRasterOutput` (см. `Assets/WorldGen/Rendering/WorldMapRenderer.cs:1058-1090`, ищи конец этого метода — после его закрывающей `}`):

```csharp
        /// <summary>Фикстура: сетка сайтов 3x3 (по аналогии с SquarePolygon-фикстурами других
        /// self-тестов), центральная клетка (1,1) - суша, 8 окружающих - океан. Для регулярной
        /// сетки Vороного-ячейки центра-сайта - в точности единичные квадраты (SquarePolygon) -
        /// это ХОРОШО ИЗВЕСТНЫЙ факт (Vороной регулярной решётки точек = решётка прямоугольников),
        /// поэтому такая фикстура одновременно и простая для ручной проверки, и геометрически
        /// корректная настоящая Vороной-конфигурация (не просто "квадратики для теста").
        /// Ожидание: ровно одна замкнутая петля - 4 угла центральной клетки.</summary>
        [ContextMenu("Self-Test: Coastline Contour Tracing")]
        public void SelfTestCoastlineContourTracing()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            for (int r = 0; r < 3; r++)
            {
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
            }
            var fixtureById = fixtureCells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);

            var loopsUnsmoothed = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(corners, fixtureById, smoothingIterations: 0);
            bool oneLoop = loopsUnsmoothed.Count == 1;
            bool fourPoints = oneLoop && loopsUnsmoothed[0].Count == 4;

            bool ContainsPointNear(List<System.Numerics.Vector2> loop, System.Numerics.Vector2 target, float eps)
            {
                foreach (var p in loop)
                    if ((p - target).Length() < eps) return true;
                return false;
            }

            bool cornersMatch = fourPoints
                && ContainsPointNear(loopsUnsmoothed[0], new System.Numerics.Vector2(0.5f, 0.5f), 0.01f)
                && ContainsPointNear(loopsUnsmoothed[0], new System.Numerics.Vector2(1.5f, 0.5f), 0.01f)
                && ContainsPointNear(loopsUnsmoothed[0], new System.Numerics.Vector2(1.5f, 1.5f), 0.01f)
                && ContainsPointNear(loopsUnsmoothed[0], new System.Numerics.Vector2(0.5f, 1.5f), 0.01f);

            var loopsSmoothed = WorldGen.Rendering.MapRaster.CoastlineContour.TraceSmoothedLoops(corners, fixtureById, smoothingIterations: 2);
            bool smoothedPointCountOk = loopsSmoothed.Count == 1 && loopsSmoothed[0].Count == 16; // 4 * 2^2

            bool ok = oneLoop && fourPoints && cornersMatch && smoothedPointCountOk;
            Debug.Log(ok
                ? "Self-Test Coastline Contour Tracing: PASS"
                : $"Self-Test Coastline Contour Tracing: FAIL (oneLoop={oneLoop}, fourPoints={fourPoints}, cornersMatch={cornersMatch}, smoothedPointCountOk={smoothedPointCountOk})");
        }
```

- [ ] **Step 3: Проверить компиляцию и прогнать самотест**

В Unity Editor: правый клик на компоненте `WorldMapRenderer` в инспекторе → `Self-Test: Coastline Contour Tracing`. Ожидается в консоли: `Self-Test Coastline Contour Tracing: PASS`.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): trace and Chaikin-smooth coastline boundary loops"
```

---

### Task 2: `CoastlineContour.cs` — растеризация петель в маску `IsLand`

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs`
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (новый `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `List<List<System.Numerics.Vector2>>` (формат петель из Task 1).
- Produces: `CoastlineContour.RasterizeIsLand(IReadOnlyList<List<System.Numerics.Vector2>> loops, bool[] isLand, int texWidth, int texHeight, float mapWidth, float mapHeight, int rectX, int rectY, int rectW, int rectH)` — используется Task 3.

- [ ] **Step 1: Добавить `RasterizeIsLand` в `CoastlineContour.cs`**

Добавить как публичный метод класса `CoastlineContour` (после `TraceSmoothedLoops`, перед `SharedCellIds`):

```csharp
        /// <summary>Растеризует набор замкнутых петель (см. TraceSmoothedLoops) в булеву маску
        /// even-odd правилом (стандартный scanline polygon fill) - петли озёр внутри острова
        /// автоматически становятся "дырками", без явного различения типа петли. Пишет ТОЛЬКО
        /// внутри [rectX, rectX+rectW) x [rectY, rectY+rectH) в уже существующий массив isLand -
        /// безопасно вызывать повторно для под-прямоугольника (кисть), не трогая остальную маску.</summary>
        public static void RasterizeIsLand(
            IReadOnlyList<List<Vector2>> loops, bool[] isLand,
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
                        if ((a.Y <= worldY) == (b.Y <= worldY)) continue; // ребро не пересекает эту строку
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
                    isLand[rowBase + x] = inside;
                }
            }
        }
```

- [ ] **Step 2: Написать самотест растеризации в `WorldMapRenderer.cs`**

Добавить сразу после `SelfTestCoastlineContourTracing` (добавленного в Task 1):

```csharp
        /// <summary>Синтетический контур "остров с озером внутри" (без реальных VoronoiCell/Corner -
        /// RasterizeIsLand работает напрямую с петлями точек). Карта 14x14, текстура 14x14 (1
        /// тексель на мировую единицу) - внешняя петля 0..10, внутренняя (озеро) 3..7. Пиксель
        /// (12,12) заведомо ЗА пределами внешней петли (0..10) - проверяет случай "снаружи".</summary>
        [ContextMenu("Self-Test: Coastline Contour Rasterize IsLand")]
        public void SelfTestCoastlineContourRasterizeIsLand()
        {
            var outerLoop = new List<System.Numerics.Vector2>
            {
                new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f),
            };
            var innerLoop = new List<System.Numerics.Vector2> // "озеро" внутри острова
            {
                new(3f, 3f), new(7f, 3f), new(7f, 7f), new(3f, 7f),
            };
            var loops = new List<List<System.Numerics.Vector2>> { outerLoop, innerLoop };

            const int texSize = 14;
            const float mapSize = 14f;
            var isLand = new bool[texSize * texSize];
            WorldGen.Rendering.MapRaster.CoastlineContour.RasterizeIsLand(loops, isLand, texSize, texSize, mapSize, mapSize, 0, 0, texSize, texSize);

            bool insideIslandOnly = isLand[1 * texSize + 1];  // мир (1.5,1.5) - внутри острова, вне озера
            bool insideLake = isLand[5 * texSize + 5];        // мир (5.5,5.5) - внутри озера - должно быть false
            bool outsideIsland = isLand[12 * texSize + 12];   // мир (12.5,12.5) - за пределами острова

            bool fullRectOk = insideIslandOnly && !insideLake && !outsideIsland;

            // Частичное обновление: перерастеризуем маленький под-прямоугольник [0,0,2,2] ДРУГИМ
            // (пустым) набором петель - остальная маска должна остаться нетронутой. Это ровно то,
            // чем пользуется кисть через существующий dirty rect (см. design doc "Кисть и живое
            // обновление") - доказывает, что растеризация безопасна для частичных перезапеканий.
            var emptyLoops = new List<List<System.Numerics.Vector2>>();
            WorldGen.Rendering.MapRaster.CoastlineContour.RasterizeIsLand(emptyLoops, isLand, texSize, texSize, mapSize, mapSize, 0, 0, 2, 2);

            bool subRectCleared = !isLand[0 * texSize + 0] && !isLand[1 * texSize + 1];
            bool restUntouched = !isLand[5 * texSize + 5] && isLand[1 * texSize + 8]; // (8,1) вне озера и вне под-прямоугольника - должен остаться true

            bool ok = fullRectOk && subRectCleared && restUntouched;
            Debug.Log(ok
                ? "Self-Test Coastline Contour Rasterize IsLand: PASS"
                : $"Self-Test Coastline Contour Rasterize IsLand: FAIL (fullRectOk={fullRectOk}, subRectCleared={subRectCleared}, restUntouched={restUntouched})");
        }
```

- [ ] **Step 3: Проверить компиляцию и прогнать самотест**

Правый клик на `WorldMapRenderer` → `Self-Test: Coastline Contour Rasterize IsLand`. Ожидается: `PASS`.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): rasterize coastline loops into an even-odd IsLand mask"
```

---

### Task 3: Подключить маску `IsLand` к `MapRasterizer` (только Combined+smoothBorders)

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (обновить ВСЕ существующие вызовы изменившихся сигнатур — см. Step 2)
- Test: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (новый `[ContextMenu]` self-test)

**Interfaces:**
- Consumes: `CoastlineContour.TraceSmoothedLoops`/`RasterizeIsLand` (Tasks 1-2); существующее поле `WorldMapRenderer.corners` (`List<Corner>`, объявлено на строке 129, заполняется в `GenerateAndRender`/`PrepareLoadFromCells`, строки 212/244 — используем как есть, не создаём заново в продакшн-вызовах).
- Produces: обновлённые сигнатуры `MapRasterizer.Bake`/`RebakeRegion`/`BakeFieldsRect` (добавлен параметр `List<Corner> corners`) и поле `MapRasterConfig.CoastlineSmoothness` (`int`, default `3`) — Task 4 добавит проброс из сериализованного поля `WorldMapRenderer`. `MapRasterBuffers.IsLand` (`bool[]`) — внутренняя деталь, не используется вне `MapRasterizer.cs`.

**ВАЖНО:** этот таск меняет сигнатуры `MapRasterizer.Bake`, `MapRasterizer.RebakeRegion`, `MapRasterizer.BakeFieldsRect` (добавляет обязательный параметр `corners`) — это ломает компиляцию ВСЕХ существующих вызовов этих трёх методов. Все они находятся в `WorldMapRenderer.cs`. Полный список (проверь через `grep -n "MapRasterizer\.\(Bake\|RebakeRegion\|BakeFieldsRect\)\(" Assets/WorldGen/Rendering/WorldMapRenderer.cs` — должно быть ровно 11 совпадений до этого таска). **Все 8 мест обновляются В ЭТОМ таске**, чтобы проект скомпилировался в конце (Task 4 добавит только сериализованное поле поверх уже работающего кода):
1. `SelfTestRasterHardModeParity` (~строка 829)
2. `SelfTestRasterElevationInvariant` (~строка 878)
3. `SelfTestDegenerateCellExcludedFromLookup` (~строка 938)
4. `SelfTestChunkedBakeContinuity` — 4 вызова (~строки 1004, 1010, 1021, 1022)
5. `SelfTestLayerTogglesAffectRasterOutput` (~строка 1082, внутри локальной функции `BakePixel`)
6. `RebakeAll()` (~строка 1273)
7. `RebakeRegion(IEnumerable<VoronoiCell>)` (~строка 1288)
8. `RebakeAllStepped(...)` (~строка 1405)

Для самотестов (1-5) новый аргумент `corners` строится локально в тесте через `WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells)` (см. Step 2). Для продакшн-вызовов (6-8) передаётся уже существующее поле `corners` напрямую (см. Step 3). Позиция нового аргумента везде — сразу после `lookup`, перед `displayMode`. В этом таске НЕ трогаем `BuildRasterConfig()` и НЕ добавляем сериализованное поле — `MapRasterConfig.CoastlineSmoothness` остаётся на своём дефолте `3`, чего достаточно, чтобы всё скомпилировалось и работало; проброс из инспектора добавит Task 4.

- [ ] **Step 1: Обновить `MapRasterizer.cs` — конфиг, буферы, сигнатуры, интеграция**

Полностью заменить содержимое `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs` следующим (это ВЕСЬ файл, не диф — используй его целиком):

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

        /// <summary>Число итераций Chaikin-сглаживания контура берега (только Combined+
        /// SmoothBorders). 0 = точные грани клеток Вороного (без трассировки/сглаживания
        /// вообще - см. MapRasterizer.BakeFieldsRect). См. design doc
        /// docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md.</summary>
        public int CoastlineSmoothness = 3;

        /// <summary>Соответствуют существующим тумблерам MapLayersPanel - выключение биомного слоя
        /// даёт нейтральную земляную заливку вместо цвета семейства биома; выключение рельефа
        /// убирает hillshade/холодный подсвет на суше и градиент глубины на воде (плоский цвет).</summary>
        public bool ShowBiomeLayer = true;
        public bool ShowReliefLayer = true;

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

        /// <summary>true = суша по сглаженному контуру берега (только Combined+SmoothBorders -
        /// см. CoastlineContour). В прочих режимах не заполняется и не читается.</summary>
        public bool[] IsLand;
    }

    /// <summary>
    /// Запекает клетки Вороного в Texture2D + параллельный cellId-буфер для хит-тестинга.
    /// Height/Region/Biome и Combined-без-сглаживания используют "hard" сэмплинг (ближайшая клетка,
    /// без блендинга, через HardModeColor - визуально идентично старому vertex-color рендеру).
    /// Combined+smoothBorders включает полный "нарисованный" конвейер (см. Task 6 подпроекта 1) +
    /// сглаженный контур берега вместо категоризации по ближайшей клетке (см. CoastlineContour).
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
                IsLand = new bool[n],
            };
        }

        /// <summary>Удобная обёртка: полный запек всего изображения "с нуля" в новую текстуру.</summary>
        public static Texture2D Bake(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
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
            RebakeRegion(cells, cellById, lookup, corners, displayMode, config, texture, buffers, 0, 0, config.TexWidth, config.TexHeight);
            return texture;
        }

        /// <summary>Перезапекает прямоугольную под-область текстуры/буферов на месте. rectX/Y/W/H уже
        /// в пиксельных координатах и уже включают отступ под smoothRadius - эта функция не добавляет
        /// собственный отступ (см. WorldMapRenderer.ComputeTouchedPixelRect). Требует, чтобы
        /// вне прямоугольника буферы либо не существовали вовсе (полный запек - rect = всё изображение),
        /// либо уже содержали валидные данные предыдущего полного запека (кисть).</summary>
        public static void RebakeRegion(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            BakeFieldsRect(cells, cellById, lookup, corners, displayMode, config, buffers, rectX, rectY, rectW, rectH);
            ColorAndVignetteRect(cellById, displayMode, config, texture, buffers, rectX, rectY, rectW, rectH);
        }

        /// <summary>Проход 1 (cellId) + проход 1.5 (контур берега + BakePaintedFields, если painted)
        /// для заданного прямоугольника. Трассировка/сглаживание контура (CoastlineContour) всегда
        /// выполняется заново на ВСЕХ клетках карты (дёшево - масштаб числа клеток, не пикселей),
        /// растеризация в IsLand - только в переданный rect (безопасно для частичных перезапеканий
        /// кистью). Сам по себе не читает ничего ЗА пределами rect в буферах, поэтому безопасно
        /// вызывать для части изображения, даже если буферы вне rect ещё вообще не заполнены - в
        /// отличие от ColorAndVignetteRect, которому нужны уже готовые соседние строки/пиксели.</summary>
        public static void BakeFieldsRect(
            IReadOnlyList<VoronoiCell> cells,
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            NearestCellLookup lookup,
            List<Corner> corners,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

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
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, config.CoastlineSmoothness);
                CoastlineContour.RasterizeIsLand(loops, buffers.IsLand, w, h, config.MapWidth, config.MapHeight, rectX, rectY, rectW, rectH);
                BakePaintedFields(cells, cellById, lookup, config, buffers, rectX, rectY, rectW, rectH);
            }
        }

        /// <summary>Проход 2 (цвет) + проход 3 (виньетка) для заданного прямоугольника. Требует, чтобы
        /// CellId/Elevation/Temperature/FamilyColor/IsLand уже были заполнены BakeFieldsRect не только
        /// для этого прямоугольника, но и для его непосредственно соседних строк/столбцов (градиент
        /// рельефа и проверка берега читают ±1 пиксель за границу rect).</summary>
        public static void ColorAndVignetteRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById,
            MapDisplayMode displayMode,
            MapRasterConfig config,
            Texture2D texture,
            MapRasterBuffers buffers,
            int rectX, int rectY, int rectW, int rectH)
        {
            int w = config.TexWidth, h = config.TexHeight;
            bool painted = displayMode == MapDisplayMode.Combined && config.SmoothBorders;

            for (int y = rectY; y < rectY + rectH; y++)
            {
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    int idx = y * w + x;
                    var cell = cellById[buffers.CellId[idx]];
                    buffers.PreVignette[idx] = painted
                        ? BakePaintedPixel(cell, buffers, idx, x, y, w, h, config)
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

        // ---- Painted-pipeline hooks ----

        /// <summary>Проход 1.5 (только суша, только painted-режим): блендированные elevation/
        /// temperature/базовый цвет семейства среди соседей в радиусе smoothRadius, вес
        /// 1/(distance²+1). Категория суша/вода берётся из уже растеризованной buffers.IsLand
        /// (сглаженный контур - см. BakeFieldsRect), НЕ из cell.EffectiveIsOcean/IsLake напрямую -
        /// иначе пиксель, который сглаженный контур относит к суше, но чья ближайшая клетка
        /// технически вода, никогда не получил бы своего FamilyColor (оставался бы чёрным).</summary>
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
                    bool isWater = !buffers.IsLand[idx];

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
            VoronoiCell cell, MapRasterBuffers buffers, int idx, int x, int y, int w, int h, MapRasterConfig config)
        {
            var palette = ResolvePalette(config.Theme);
            float coldAmt = 0.10f + (config.ColdLight / 100f) * 0.30f;
            float varAmt = config.RegionVariation / 100f;

            bool isWater = !buffers.IsLand[idx];
            return isWater
                ? ColorForWaterPixel(cell, buffers, x, y, w, h, config, palette, coldAmt)
                : ColorForLandPixel(cell, buffers, idx, x, y, w, h, config, palette, coldAmt, varAmt);
        }

        static Color32 ColorForWaterPixel(
            VoronoiCell cell, MapRasterBuffers buffers,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt)
        {
            Color32 shallowOrLakeS = cell.EffectiveIsLake ? palette.LakeS : palette.Shallow;
            Color32 deep = cell.EffectiveIsLake ? palette.LakeD : palette.Abyss;

            // Слой рельефа выключен (тумблер MapLayersPanel) - плоский цвет мелководья без
            // градиента глубины, как "рельеф выключен" для суши ниже отключает hillshade.
            if (!config.ShowReliefLayer)
                return ClampColor32(shallowOrLakeS.r, shallowOrLakeS.g, shallowOrLakeS.b);

            float depth = Mathf.Clamp01(config.WaterDepth01(cell));

            float r = Mathf.Lerp(shallowOrLakeS.r, deep.r, depth);
            float g = Mathf.Lerp(shallowOrLakeS.g, deep.g, depth);
            float b = Mathf.Lerp(shallowOrLakeS.b, deep.b, depth);

            if (!cell.EffectiveIsLake)
            {
                float ripple = (Noise.Fbm(x / 40f, y / 26f, config.Seed + 401, 2) - 0.5f) * 10f;
                r += ripple; g += ripple; b += ripple;
            }

            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: false))
            {
                float gk = 0.32f + coldAmt * 0.5f;
                r += (palette.Glow.r - r) * gk;
                g += (palette.Glow.g - g) * gk;
                b += (palette.Glow.b - b) * gk;
            }

            return ClampColor32(r, g, b);
        }

        static Color32 ColorForLandPixel(
            VoronoiCell cell, MapRasterBuffers buffers, int idx,
            int x, int y, int w, int h, MapRasterConfig config, ResolvedPalette palette, float coldAmt, float varAmt)
        {
            // Слой биомов выключен (тумблер MapLayersPanel) - нейтральная земляная заливка вместо
            // цвета семейства биома, как старый GetNeutralBaseColor для суши.
            Color32 fam = config.ShowBiomeLayer ? buffers.FamilyColor[idx] : new Color32(209, 199, 166, 255);
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

            if (HasNeighborWithWaterStatus(buffers, x, y, w, h, wantWater: true))
            {
                // Береговая обводка (шаг 7, сторона суши) - жёсткая замена, перекрывает hillshade.
                r = palette.Outline.r; g = palette.Outline.g; b = palette.Outline.b;
            }
            else if (config.ShowReliefLayer)
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
            // else: слой рельефа выключен - оставляем базовый (тонированный) цвет без hillshade.

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
            MapRasterBuffers buffers, int x, int y, int w, int h, bool wantWater)
        {
            return Check(ClampIdx(x - 1, y, w, h)) || Check(ClampIdx(x + 1, y, w, h))
                || Check(ClampIdx(x, y - 1, w, h)) || Check(ClampIdx(x, y + 1, w, h));

            bool Check(int idx)
            {
                bool isWaterPixel = !buffers.IsLand[idx];
                return isWaterPixel == wantWater;
            }
        }

        static int ClampIdx(int x, int y, int w, int h) => Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1);

        static Color32 ClampColor32(float r, float g, float b) => new Color32(
            (byte)Mathf.Clamp(r, 0f, 255f), (byte)Mathf.Clamp(g, 0f, 255f), (byte)Mathf.Clamp(b, 0f, 255f), 255);

        static System.Numerics.Vector2 PixelToSite(int x, int y, int w, int h, float mapWidth, float mapHeight)
        {
            float px = (x + 0.5f) / w * mapWidth;
            float pz = (y + 0.5f) / h * mapHeight;
            return new System.Numerics.Vector2(px, pz);
        }
    }
}
```

- [ ] **Step 2: Обновить существующие вызовы-самотесты в `WorldMapRenderer.cs`**

Для каждого из 5 самотестов, перечисленных выше (пункты 1-5), добавь строку `var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);` сразу после создания `fixtureById`, и добавь `corners` как новый аргумент в вызов `RebakeRegion`/`BakeFieldsRect` (позиция сразу после `lookup`, перед `displayMode`/`MapDisplayMode.Combined`). Пример для `SelfTestRasterHardModeParity` (было → стало):

```csharp
// Было:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, displayMode, config, tex, buffers, 0, 0, 10, 10);

// Стало:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, displayMode, config, tex, buffers, 0, 0, 10, 10);
```

Примени тот же паттерн к оставшимся 4 самотестам — точные фрагменты «было/стало»:

**`SelfTestRasterElevationInvariant`:**
```csharp
// Было (после var fixtureById = ...):
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
var tex = new Texture2D(20, 20, TextureFormat.RGBA32, false);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 20, 20);

// Стало:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
var tex = new Texture2D(20, 20, TextureFormat.RGBA32, false);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 20, 20);
```

**`SelfTestDegenerateCellExcludedFromLookup`** (фикстура включает клетку-«призрак» `ghost` с `Polygon.Count == 0` — `CornerGraphBuilder.Build` уже безопасно пропускает такие клетки, `Build(fixtureCells)` вызывается как есть, без спецобработки):
```csharp
// Было:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
bool threw = false;
try
{
    WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
}

// Стало:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
bool threw = false;
try
{
    WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
}
```

**`SelfTestChunkedBakeContinuity`** (`corners` строится один раз сразу после `lookup`, передаётся во все 4 вызова ниже по методу — `RebakeRegion` эталона, `BakeFieldsRect` двухфазного, и оба `RebakeRegion` наивного):
```csharp
// Было:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
// Эталон:
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, texRef, buffersRef, 0, 0, 20, 20);
// Двухфазный:
WorldGen.Rendering.MapRaster.MapRasterizer.BakeFieldsRect(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, buffersChunked, 0, 0, 20, 20);
WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 0, 20, 10);
WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 10, 20, 10);
// Наивный:
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 0, 20, 10);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 10, 20, 10);

// Стало (только добавленные/изменённые строки):
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
var config = new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
// Эталон:
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texRef, buffersRef, 0, 0, 20, 20);
// Двухфазный (ColorAndVignetteRect не меняется - у него нет параметра corners):
WorldGen.Rendering.MapRaster.MapRasterizer.BakeFieldsRect(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, buffersChunked, 0, 0, 20, 20);
WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 0, 20, 10);
WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 10, 20, 10);
// Наивный:
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 0, 20, 10);
WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 10, 20, 10);
```

**`SelfTestLayerTogglesAffectRasterOutput`** (`corners` строится один раз снаружи локальной функции `BakePixel` и захватывается её замыканием):
```csharp
// Было:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
WorldGen.Rendering.MapRaster.MapRasterConfig MakeConfig(bool showBiome, bool showRelief) => new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
Color BakePixel(bool showBiome, bool showRelief)
{
    var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
    var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
    var config = MakeConfig(showBiome, showRelief);
    WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
    ...
}

// Стало:
var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
WorldGen.Rendering.MapRaster.MapRasterConfig MakeConfig(bool showBiome, bool showRelief) => new WorldGen.Rendering.MapRaster.MapRasterConfig { ... };
Color BakePixel(bool showBiome, bool showRelief)
{
    var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
    var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
    var config = MakeConfig(showBiome, showRelief);
    WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
    ...
}
```

- [ ] **Step 3: Обновить три продакшн-вызова в `WorldMapRenderer.cs` (передать существующее поле `corners`)**

Эти три вызова используют уже объявленное поле `corners` (`List<Corner>` на строке 129, заполняется при генерации/загрузке) — просто добавь его как новый аргумент сразу после `nearestLookup`. `BuildRasterConfig()` НЕ трогаем (см. «ВАЖНО» выше).

**`RebakeAll()`** (~строка 1273):
```csharp
// Было:
rasterTexture = MapRasterizer.Bake(cells, cellById, nearestLookup, displayMode, config, out rasterBuffers);
// Стало:
rasterTexture = MapRasterizer.Bake(cells, cellById, nearestLookup, corners, displayMode, config, out rasterBuffers);
```

**`RebakeRegion(IEnumerable<VoronoiCell>)`** (~строка 1288):
```csharp
// Было:
MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, displayMode, config, rasterTexture, rasterBuffers, rx, ry, rw, rh);
// Стало:
MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, corners, displayMode, config, rasterTexture, rasterBuffers, rx, ry, rw, rh);
```

**`RebakeAllStepped(...)`** (~строка 1405, вызов `BakeFieldsRect` для всего изображения перед чанковым циклом раскраски):
```csharp
// Было:
MapRasterizer.BakeFieldsRect(cells, cellById, nearestLookup, displayMode, config, rasterBuffers, 0, 0, texWidth, texHeight);
// Стало:
MapRasterizer.BakeFieldsRect(cells, cellById, nearestLookup, corners, displayMode, config, rasterBuffers, 0, 0, texWidth, texHeight);
```

После этого шага проект компилируется (все 8 вызовов сходятся с новыми сигнатурами).

- [ ] **Step 4: Написать самотест паритета (CoastlineSmoothness=0 == старая жёсткая категоризация)**

Добавить после последнего самотеста, добавленного в Task 2 (`SelfTestCoastlineContourRasterizeIsLand`):

```csharp
        /// <summary>Регрессия/паритет: при coastlineSmoothness=0 IsLand-маска (через трассировку +
        /// растеризацию несглаженного контура) должна СОВПАДАТЬ пиксель-в-пиксель со старым тестом
        /// "ближайшая клетка - океан/озеро?" - это математически ожидаемо (nearest-site тест и
        /// point-in-polygon той же самой Vороной-ячейки эквивалентны по построению диаграммы
        /// Vороного), проверяем это явно как регрессионную защиту.</summary>
        [ContextMenu("Self-Test: Coastline Mask Matches Hard Categorization At Zero Smoothness")]
        public void SelfTestCoastlineMaskMatchesHardCategorization()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            for (int r = 0; r < 3; r++)
            {
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
            }
            var fixtureById = fixtureCells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 30, TexHeight = 30, MapWidth = 3f, MapHeight = 3f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 0,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 0.6f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(30, 30);
            var tex = new Texture2D(30, 30, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 30, 30);

            bool mismatchFound = false;
            for (int y = 0; y < 30 && !mismatchFound; y++)
            {
                for (int x = 0; x < 30 && !mismatchFound; x++)
                {
                    float px = (x + 0.5f) / 30f * 3f;
                    float pz = (y + 0.5f) / 30f * 3f;
                    var nearest = lookup.FindNearest(new System.Numerics.Vector2(px, pz));
                    bool expectedIsLand = !(nearest.EffectiveIsOcean || nearest.EffectiveIsLake);
                    bool actualIsLand = buffers.IsLand[y * 30 + x];
                    if (expectedIsLand != actualIsLand) mismatchFound = true;
                }
            }

            Destroy(tex);
            Debug.Log(!mismatchFound
                ? "Self-Test Coastline Mask Matches Hard Categorization At Zero Smoothness: PASS"
                : "Self-Test Coastline Mask Matches Hard Categorization At Zero Smoothness: FAIL (IsLand mask disagrees with nearest-cell test somewhere on the grid)");
        }
```

- [ ] **Step 5: Проверить компиляцию и прогнать самотесты**

Проект теперь должен компилироваться (все 8 вызовов обновлены в Steps 2-3). Если доступен Unity Editor без конфликта с открытым проектом пользователя: дождаться перекомпиляции без ошибок, затем правым кликом на `WorldMapRenderer` прогнать `Self-Test: Coastline Mask Matches Hard Categorization At Zero Smoothness` (должен дать `PASS`) и перепрогнать существующие самотесты подпроекта 1 (`Self-Test: Raster Hard Mode Parity`, `Self-Test: Raster Elevation Invariant`, `Self-Test: Chunked Bake Continuity`, `Self-Test: Layer Toggles Affect Raster Output`, `Self-Test: Degenerate Cell Excluded From Raster Lookup`, `Self-Test: Coastline Contour Tracing`, `Self-Test: Coastline Contour Rasterize IsLand`) — ни один не должен сломаться. Если Unity Editor недоступен — перечитай все изменённые сигнатуры/вызовы построчно (баланс скобок, точные имена параметров, позиция `corners` во всех 8 вызовах).

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): categorize painted-mode pixels via smoothed IsLand mask"
```

---

### Task 4: Экспонировать `coastlineSmoothness` (сериализованное поле + проброс) + самотест кисти

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`

**Interfaces:**
- Consumes: `MapRasterConfig.CoastlineSmoothness` (`int`, поле уже существует после Task 3 с дефолтом `3`). Продакшн-вызовы `RebakeAll`/`RebakeRegion`/`RebakeAllStepped` уже передают `corners` (сделано в Task 3) — в этом таске НЕ трогаем.
- Produces: публичное сериализованное поле `WorldMapRenderer.coastlineSmoothness` (`int`, `[Range(0,5)]`, default `3`), проброшенное в `BuildRasterConfig().CoastlineSmoothness` — до этого таска конфиг использовал дефолт `3` из `MapRasterConfig`, теперь значение управляется из инспектора. Потребуется подпроекту 6 (UI) в будущем.

- [ ] **Step 1: Добавить поле `coastlineSmoothness`**

В `Assets/WorldGen/Rendering/WorldMapRenderer.cs`, в блоке `[Header("Combined: тёмный рендер (MapRaster)")]` (строки 106-114), сразу после `smoothBorders`:

```csharp
        [Header("Combined: тёмный рендер (MapRaster)")]
        public MapPaletteTheme paletteTheme = MapPaletteTheme.ColdTwilight;
        [Range(0f, 100f)] public float coldLight = 58f;
        [Range(0f, 100f)] public float regionVariation = 45f;
        [Range(40f, 100f)] public float darkness = 72f;
        [Tooltip("Сглаженные границы биомов + полный 'нарисованный' конвейер (тонировка, рельеф, зерно, свечение берега). Выключено = старый плоский вид один-в-один, только через текстуру.")]
        public bool smoothBorders = true;
        [Tooltip("Число итераций сглаживания Чайкина для контура берега (только Combined+smoothBorders). 0 = точные грани клеток Вороного (текущее поведение при выключенном сглаживании).")]
        [Range(0, 5)] public int coastlineSmoothness = 3;
        [Tooltip("Большая сторона запекаемой текстуры карты в пикселях; меньшая считается по аспекту mapWidth:mapHeight.")]
        public int rasterLongSide = 2048;
```

- [ ] **Step 2: Пробросить в `BuildRasterConfig()`**

В методе `BuildRasterConfig()` (строки 1320-1343), добавить строку `CoastlineSmoothness = coastlineSmoothness,` сразу после `SmoothBorders = smoothBorders,`:

```csharp
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
                CoastlineSmoothness = coastlineSmoothness,
                SmoothRadius = minPointDistance * 1.5f,
                ReliefStrength = reliefStrength,
                ReliefLightAzimuth = reliefLightAzimuth,
                ReliefAmbient = reliefAmbient,
                ShowBiomeLayer = showBiomeLayer,
                ShowReliefLayer = showReliefLayer,
                HardModeColor = GetColorForCell,
                WaterDepth01 = GetWaterDepth01,
            };
        }
```

- [ ] **Step 3: Написать самотест живого обновления кистью**

Добавить после `SelfTestCoastlineMaskMatchesHardCategorization` (из Task 3):

```csharp
        /// <summary>Симулирует мазок кисти, меняющий WaterOverride соседней клетки (как
        /// BrushSetBiome/"Сила: вода" в редакторе), и перезапекает ТОЛЬКО маленький прямоугольник
        /// вокруг нее - как реальный RebakeAffectedCells. Проверяет, что IsLand-маска внутри этого
        /// прямоугольника отражает новое состояние без пересборки графа Corner (топология графа не
        /// меняется от WaterOverride - меняется только то, какие клетки считаются водой при
        /// трассировке, см. CoastlineContour.TraceSmoothedLoops) и без полного RebakeAll.</summary>
        [ContextMenu("Self-Test: Coastline Mask Updates Within Brush Dirty Rect")]
        public void SelfTestCoastlineMaskUpdatesWithBrushDirtyRect()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            VoronoiCell edited = null;
            for (int r = 0; r < 3; r++)
            {
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
                    if (c == 2 && r == 1) edited = cell; // сосед справа от острова - "мазок кисти" превратит его в сушу
                }
            }
            var fixtureById = fixtureCells.ToDictionary(c => c.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 30, TexHeight = 30, MapWidth = 3f, MapHeight = 3f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 2,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 0.6f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(30, 30);
            var tex = new Texture2D(30, 30, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 30, 30);

            const int px = 22, py = 10; // мир (2.25, 1.05) - внутри клетки (2,1), до правки - океан
            bool wasLandBefore = buffers.IsLand[py * 30 + px];

            // "Мазок кисти": превращаем соседнюю клетку в сушу (как WaterOverride=ForceLand в
            // редакторе), затем перезапекаем ТОЛЬКО небольшой dirty rect вокруг неё - тем же
            // экземпляром corners, без пересборки графа (см. summary метода).
            edited.WaterOverride = WaterOverrideType.ForceLand;
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 20, 5, 10, 10);

            bool isLandAfter = buffers.IsLand[py * 30 + px];

            Destroy(tex);
            bool ok = !wasLandBefore && isLandAfter;
            Debug.Log(ok
                ? "Self-Test Coastline Mask Updates Within Brush Dirty Rect: PASS"
                : $"Self-Test Coastline Mask Updates Within Brush Dirty Rect: FAIL (wasLandBefore={wasLandBefore}, isLandAfter={isLandAfter})");
        }
```

- [ ] **Step 4: Проверить компиляцию и прогнать ВСЕ самотесты**

Если доступен Unity Editor без конфликта открытого проекта: дождаться перекомпиляции, затем правым кликом на `WorldMapRenderer` прогнать по очереди ВСЕ `[ContextMenu]` самотесты файла (старые из подпроекта 1 + 4 новых из этой работы: `Self-Test: Coastline Contour Tracing`, `Self-Test: Coastline Contour Rasterize IsLand`, `Self-Test: Coastline Mask Matches Hard Categorization At Zero Smoothness`, `Self-Test: Coastline Mask Updates Within Brush Dirty Rect`). Все должны дать `PASS` в консоли, ни один из существующих самотестов подпроекта 1 не должен сломаться.

Если Unity Editor недоступен (открыт у пользователя) — перечитать все изменённые файлы целиком построчно, сверяя баланс скобок, точные имена и сигнатуры (см. memory `unity-subagent-driven-dev-lessons` про этот же констрейнт при живых багфиксах подпроекта 1).

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat(map-raster): wire coastlineSmoothness through WorldMapRenderer and brush rebakes"
```
