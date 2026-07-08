# Удаление CPU-рендера карты (GPU-only) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline) или superpowers:subagent-driven-development. Steps — чекбоксы.
> **Верификация — только в Unity Editor у пользователя** (агенты Unity не запускают). Батчмод-компиляция не идёт при открытом Editor — полагаемся на перекомпиляцию Editor'ом + ручную проверку сценариев.

**Goal:** Удалить CPU-путь рендера карты, оставив GPU (`MapTerrain` шейдер) единственным. Убрать флаг `useGpuRenderer` и все `if(GPU)…else CPU` ветки → GPU всегда.

**Rationale (решение пользователя 2026-07-08):** CPU-софт-рендер грузит одно ядро СИЛЬНЕЕ, чем GPU-шейдер — то есть для слабого железа он ХУЖЕ, а не «страховка». GPU-перенос и был нацелен на разгрузку слабых машин. Держать CPU-путь смысла нет.

**Важно — что НЕ входит:** сглаживание берега / кривые границы биомов / общая «графичность» карты — ОТДЕЛЬНАЯ будущая тема (CPU vs GPU — решится потом). Сейчас GPU-шейдер уже имеет свои органичные границы (domain warp), обводку и свечение берега, так что визуально ничего не теряется. Этот план — ТОЛЬКО удаление мёртвого CPU-пути, без изменения вида.

## Что удалить
- `Assets/WorldGen/Rendering/MapRaster/MapRasterizer.cs` (+ `.meta`) — CPU painted-конвейер (`MapRasterConfig`, `MapRasterBuffers`, `Bake`/`RebakeRegion`/`BakeFieldsRect`/`ColorAndVignetteRect`/`BakePaintedPixel`/`ColorForLandPixel*`/`ComputeCoastDistanceRect` и т.д.).
- `Assets/WorldGen/Rendering/MapRaster/CoastlineContour.cs` (+ `.meta`) — CPU-трассировка/растеризация берега (`TraceSmoothedLoops`/`RasterizeIsLand`/`RasterizeRegionLabel`/`EdgesOverlappingY`).
- (Опционально, если станут полностью бесхозными после чистки:) `Assets/WorldGen/Rendering/VertexColorUnlit.shader`, `WorldMaterial.mat` — уже инертны; проверить и удалить, если ни на что не ссылаются.

## Что ОСТАВИТЬ (переиспользуется GPU / UI / кистями)
- `MapRaster/NearestCellLookup.cs` — хит-тест + бейк cell-id текстуры.
- `MapRaster/MapPalette.cs` — палитры GPU (`GpuMapRenderer`, `CellAttributeTexture.GetFamily`).
- `MapRaster/Noise.cs` — `BrushSetWater` (шумовая высота новой суши); (HLSL-шум в шейдере отдельный).
- `RegionColorPalette.cs` — легенда (`MapLegendUI`) и палитра биомов в `EditorBrushPanel`. (Часть методов, напр. `GetColorForCell`-related `HillshadeBrightness`, может осиротеть — можно оставить или подчистить, но САМ файл нужен.)
- `PolygonTriangulator.cs` — overlay выделения (`CellSelectionController.RebuildOverlay`).
- Весь `GpuMap/*`, `MapTerrain.shader`, quad-меш, `RebuildSpatialIndex`/`nearestLookup`, `ComputeTexSize`.

## Хирургия по `WorldMapRenderer.cs`
Удалить CPU-растровые члены и сделать GPU единственным путём:
- Поля: `rasterTexture`, `rasterMaterial`, `rasterBuffers`, `useGpuRenderer` (флаг), `EnsureRasterMaterial()`, `OnDestroy` очистка `rasterTexture/rasterMaterial` (оставить очистку, если что-то ещё, но rasterTexture/Material уходят).
- Методы: `BuildRasterConfig()`, `GetColorForCell(...)`, `GetWaterDepth01(...)`, `ComputeTouchedPixelRect(...)` (нужен был для CPU-rect; GPU им не пользуется — проверить и удалить), CPU-тело `RebakeRegion` (весь метод уходит — GPU-путь идёт через `RebakeAffectedCells`→`gpuRenderer.UpdateCells`).
- Ветвление: убрать `if (useGpuRenderer && gpuRenderer != null) {…} else {CPU}` в `RebakeAll`, `RebakeAllStepped`, `RebakeAffectedCells`, `RefreshAfterCellDataChange`, `SetShowBiomeLayer/ReliefLayer`, override-методах — оставить только GPU-ветку. `gpuRenderer` создаётся всегда в `Awake` (убрать условие `if (useGpuRenderer)`).
- Self-тесты CPU-растеризатора (удалить): `SelfTestRasterHardModeParity`, `SelfTestChunkedBakeContinuity`, `SelfTestCoastlineContour*`, `SelfTestRasterizeRegionLabel*`, `SelfTestSmoothedCategory*`, `SelfTestSmoothedFlatFill*`, `SelfTestContourDecimation`, `SelfTestHillshade` и все, дёргающие `MapRasterizer`/`CoastlineContour`.
- Self-тесты ОСТАВИТЬ: `SelfTestNoise`, `SelfTestNearestCellLookup`, `SelfTestBiomeFamilyCoverage`, `SelfTest GPU CellId Texture`, `SelfTest GPU Attribute Texture` (тестируют оставленные хелперы).

## Порядок (2 стадии — снижает риск)
### Стадия 1: GPU единственным (поведение)
- [ ] Убрать флаг `useGpuRenderer` и все `else`-CPU-ветки → всегда GPU; `gpuRenderer` всегда создаётся.
- [ ] Компиляция в Editor без ошибок; **проверка пользователем**: генерация / загрузка старого `.dndproj` / правка кистью / кисть воды / выделение / слои Биом-Рельеф — всё работает как сейчас.
- [ ] Коммит: `refactor(map): GPU render is the only path (drop useGpuRenderer flag)`.

### Стадия 2: удаление мёртвого CPU-кода
- [ ] Удалить `MapRasterizer.cs`, `CoastlineContour.cs` (+ `.meta`).
- [ ] Удалить осиротевшие CPU-члены/self-тесты из `WorldMapRenderer.cs` (после стадии 1 они недостижимы — чистое удаление).
- [ ] Компиляция без ошибок (компилятор ловит любые оставшиеся ссылки); прогнать оставшиеся self-тесты (Noise/NearestCellLookup/BiomeFamily/GPU CellId/GPU Attribute) — PASS.
- [ ] **Проверка пользователем**: тот же сценарий, что в стадии 1 — ничего не сломалось.
- [ ] Коммит: `refactor(map): remove dead CPU raster pipeline (MapRasterizer, CoastlineContour)`.

## После плана
- Обновить ledger; предложить finishing-a-development-branch (теперь `main` можно влить чистым GPU-only).
- Отдельно (НЕ здесь): обсудить реимплементацию графических улучшений берега/границ (CPU vs GPU).
