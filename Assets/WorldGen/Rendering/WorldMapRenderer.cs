using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public enum MapDisplayMode { Height, Region, Biome, Combined }

    /// <summary>
    /// Строит единый Mesh для всей карты (один draw call) из списка VoronoiCell.
    /// Каждая клетка триангулируется веером (fan) от своего центра; цвет задаётся
    /// через vertex colors, поэтому материал карты должен использовать шейдер,
    /// который их учитывает (см. комментарий ниже про материал).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Параметры генерации")]
        public int seed = 1337;
        public float mapWidth = 500f;
        public float mapHeight = 500f;
        public float minPointDistance = 15f;
        public int lloydIterations = 2;
        public int numberOfRegions = 6;

        [Header("Island Shape (форма материка)")]
        [Tooltip("Больше значение = материк занимает больше площади карты, берег обрывается резче у самого края.")]
        public float falloffPower = 2.5f;
        [Tooltip("Доля расстояния от центра карты, внутри которой материк гарантированно не топится falloff'ом. Стандартное значение 0.5.")]
        public float innerRadius = 0.5f;
        [Tooltip("Порог island-shape функции, ниже которого corner считается водой.")]
        public float seaLevel = 0.35f;
        [Tooltip("Минимальный размер связной группы corners, чтобы остаться озером. Больше значение = меньше озёр. 0 или 1 отключает фильтрацию.")]
        public int minLakeSize = 5;

        [Header("Elevation (Patel-стиль: distance-from-coast + шум)")]
        [Tooltip("Вес компонента 'расстояние от берега' в итоговой elevation. coastWeight + noiseWeight обычно должны давать ~1.0.")]
        public float elevationCoastWeight = 0.6f;
        [Tooltip("Вес компонента 'локальный шум' в итоговой elevation - позволяет горам появляться рядом с побережьем.")]
        public float elevationNoiseWeight = 0.4f;
        public float elevationNoiseFrequency = 0.015f;
        public int elevationNoiseOctaves = 4;

        [Header("Moisture (Patel-стиль: distance от свежей воды)")]
        [Tooltip("Дистанция в шагах corner-графа от свежей воды (озёр) для полного высыхания.")]
        public float moistureFalloffDistance = 20f;

        [Header("Эпицентры влажности (аддитивная поправка)")]
        [Tooltip("Количество случайных зон аномальной влажности на карте.")]
        public int numberOfMoistureEpicenters = 3;
        public float moistureEpicenterMinRadius = 150f;
        public float moistureEpicenterMaxRadius = 300f;
        [Tooltip("Диапазон случайной поправки к влажности. Положительное - влажная зона, отрицательное - аномально сухая.")]
        public float moistureEpicenterMinDelta = -0.5f;
        public float moistureEpicenterMaxDelta = 0.5f;

        [Header("Реки")]
        [Tooltip("Включить трассировку и рендер рек. Отключено по умолчанию - текущий рендер (прямые линии по corner-рёбрам) выглядит зигзагообразно без дополнительного сглаживания.")]
        public bool enableRivers = false;
        [Tooltip("Количество рек, трассируемых от случайных высоких точек.")]
        public int numberOfRivers = 20;
        [Tooltip("Минимальная elevation (после redistribution) для стартовой точки реки.")]
        public float riverMinStartElevation = 0.6f;
        [Tooltip("Толщина самой тонкой реки (flow=1) при рендере, в единицах карты.")]
        public float riverMinWidth = 1f;
        [Tooltip("Множитель толщины реки относительно sqrt(flow) - больше значение = заметнее разница между крупными и мелкими реками.")]
        public float riverWidthMultiplier = 1.5f;
        public Color riverColor = new Color(0.2f, 0.4f, 0.8f);

        [Header("Биом")]
        [Tooltip("Порог elevation, ниже которого клетка считается пляжем.")]
        public float beachElevationThreshold = 0.1f;

        [Header("Температура (point-based эпицентры)")]
        [Tooltip("Количество случайных эпицентров температуры на карте.")]
        public int numberOfTemperatureEpicenters = 3;
        public float epicenterMinRadius = 150f;
        public float epicenterMaxRadius = 300f;
        [Tooltip("Температура для клеток, не попавших в радиус ни одного эпицентра.")]
        public float baseTemperature = 0.5f;
        [Tooltip("Насколько сильно elevation охлаждает клетку.")]
        public float heightCoolingFactor = 0.6f;

        [Header("Отображение")]
        public MapDisplayMode displayMode = MapDisplayMode.Combined;

        [Header("Combined: слои")]
        public bool showBiomeLayer = true;
        public bool showReliefLayer = true;
        public bool showRegionBordersLayer = true;
        public bool showCoastlineLayer = true;

        [Header("Combined: рельеф (hillshade)")]
        public float reliefStrength = 3f;
        public float reliefLightAzimuth = 315f;
        [Range(0f, 1f)] public float reliefAmbient = 0.5f;

        [Header("Combined: границы")]
        public Color regionBorderColor = new Color(0.10f, 0.10f, 0.10f, 0.9f);
        public float regionBorderWidth = 1.5f;
        public Color coastlineColor = new Color(0.05f, 0.10f, 0.20f, 0.95f);
        public float coastlineWidth = 2.5f;

        [Header("Камера (опционально)")]
        [Tooltip("Если назначено - камера автоматически встанет над центром карты при каждой генерации.")]
        public Camera targetCamera;

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MeshCollider meshCollider;
        bool cameraPlacedOnce;

        List<VoronoiCell> cells;
        Dictionary<int, VoronoiCell> cellById;
        Dictionary<int, int> oceanDistanceFromLand; // только для океанских клеток - BFS-расстояние (в клетках) от ближайшей не-океанской суши, для чисто визуальной "глубины"
        int maxOceanDistanceFromLand = 1;
        List<Corner> corners;
        List<TemperatureEpicenter> epicenters;
        List<MoistureEpicenter> moistureEpicenters;
        List<River> rivers;
        GenerationParams lastGenParams; // храним последние параметры, чтобы RegenerateTemperature мог работать без полной генерации
        int[] triangleToCellId;
        Transform riverContainer; // родительский объект для всех LineRenderer рек - упрощает очистку при перегенерации
        Transform borderContainer;        // родитель для меш-объектов границ
        GameObject regionBorderObject;    // меш-лента границ регионов
        GameObject coastlineObject;       // меш-лента береговой линии

        /// <summary>Fired at the end of GenerateAndRender(). PoiManager subscribes to clear all POIs on regen.</summary>
        public event System.Action OnWorldRegenerated;

        /// <summary>Read-only access to current cells for POI placement.</summary>
        public IReadOnlyList<VoronoiCell> Cells => cells;

        /// <summary>The GenerationParams that actually produced the current Cells (via GenerateAndRender or LoadFromCells) — used by ProjectMenuBar to save it for reference.</summary>
        public GenerationParams LastGenParams => lastGenParams;

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            if (meshRenderer.sharedMaterial == null)
                Debug.LogWarning("WorldMapRenderer: материал не назначен. Цвета клеток не будут видны без шейдера, читающего Vertex Color.");
        }

        /// <summary>
        /// Диагностический помощник: создаёт яркий куб в центре карты на видимой высоте.
        /// Если куб виден в Game-вьюпорте, а сама карта - нет, проблема специфична для
        /// динамически генерируемого Mesh (например, материал/шейдер), а не для камеры.
        /// Если не виден даже куб - проблема в камере (позиция/направление/слой/clipping).
        /// </summary>
        [ContextMenu("Debug: Spawn Test Cube At Map Center")]
        public void SpawnDebugCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "DEBUG_TestCube";
            cube.transform.position = new Vector3(mapWidth * 0.5f, 20f, mapHeight * 0.5f);
            cube.transform.localScale = new Vector3(30f, 30f, 30f);
            cube.GetComponent<Renderer>().material.color = Color.magenta;
            Debug.Log($"WorldMapRenderer: тестовый куб создан в позиции {cube.transform.position}. Если он не виден в Game-вьюпорте - проблема в камере, не в карте.");
        }

        [ContextMenu("Generate World")]
        public void GenerateAndRender()
        {
            var genParams = BuildGenerationParams();

            cells = WorldGenerator.GenerateWorld(genParams, out epicenters, out moistureEpicenters, out rivers);
            corners = CornerGraphBuilder.Build(cells); // тот же детерминированный пересчёт, что внутри генератора - нужен здесь только для рендера рек по позициям corners
            lastGenParams = genParams;
            BuildMesh(cells);
            BuildRivers();
            BuildBorders();

            if (targetCamera != null)
                PositionCameraOverMap();

            OnDisplayChanged?.Invoke();
            OnWorldRegenerated?.Invoke();
        }

        /// <summary>
        /// Loads a previously-saved map directly from its full cell list, bypassing
        /// WorldGenerator entirely — the loaded cells (including manual overrides) are
        /// authoritative, not reproducible from referenceParams + seed. referenceParams is
        /// kept only for display/reference and to reposition the camera correctly.
        /// </summary>
        public void LoadFromCells(List<VoronoiCell> loadedCells, GenerationParams referenceParams)
        {
            cells = loadedCells;
            corners = CornerGraphBuilder.Build(cells); // same deterministic topology rebuild GenerateAndRender already does; rivers aren't persisted so this is only needed if enableRivers is later re-checked
            rivers = new List<River>();
            epicenters = new List<TemperatureEpicenter>();
            moistureEpicenters = new List<MoistureEpicenter>();
            lastGenParams = referenceParams;
            seed = referenceParams.Seed;
            mapWidth = referenceParams.Width;
            mapHeight = referenceParams.Height;

            BuildMesh(cells);
            BuildRivers();
            BuildBorders();

            if (targetCamera != null)
                PositionCameraOverMap();

            OnDisplayChanged?.Invoke();
            OnWorldRegenerated?.Invoke();
        }

        /// <summary>Строит LineRenderer для каждой реки, используя позиции corners из River.CornerPath. Толщина линии - через sqrt(flow) на каждом сегменте, как у Patel. Ничего не делает, если enableRivers выключен.</summary>
        void BuildRivers()
        {
            if (riverContainer != null)
                Destroy(riverContainer.gameObject);

            if (!enableRivers) return;
            if (rivers == null || rivers.Count == 0) return;

            var riverContainerGO = new GameObject("Rivers");
            riverContainerGO.transform.SetParent(transform, false);
            riverContainer = riverContainerGO.transform;

            var cornerById = corners.ToDictionary(c => c.Id);
            var flow = RiverFlowAccumulator.ComputeFlow(rivers);

            foreach (var river in rivers)
            {
                if (river.CornerPath.Count < 2) continue;

                var lineGO = new GameObject("River");
                lineGO.transform.SetParent(riverContainer, false);
                var lr = lineGO.AddComponent<LineRenderer>();

                lr.positionCount = river.CornerPath.Count;
                for (int i = 0; i < river.CornerPath.Count; i++)
                {
                    var corner = cornerById[river.CornerPath[i]];
                    // Y = 0.5 - немного выше поверхности карты (Y=0), чтобы избежать z-fighting с мешем.
                    lr.SetPosition(i, transform.position + new Vector3(corner.Position.X, 0.5f, corner.Position.Y));
                }

                // Толщина варьируется по длине реки - берём максимальный flow среди сегментов этой реки
                // как репрезентативную толщину (простой подход; для точного per-segment рендера потребовался
                // бы AnimationCurve по lr.widthCurve, что можно добавить позже, если нужна большая точность).
                int maxFlow = 1;
                for (int i = 0; i < river.CornerPath.Count - 1; i++)
                {
                    int a = river.CornerPath[i], b = river.CornerPath[i + 1];
                    var key = a < b ? (a, b) : (b, a);
                    if (flow.TryGetValue(key, out var f)) maxFlow = Mathf.Max(maxFlow, f);
                }

                float width = riverMinWidth + Mathf.Sqrt(maxFlow) * riverWidthMultiplier;
                lr.startWidth = width;
                lr.endWidth = width;
                lr.useWorldSpace = true;

                // Material.color через простой Unlit с тем же подходом, что и для карты (vertex/material color достаточно для линии).
                var mat = new Material(Shader.Find("Sprites/Default")); // встроенный шейдер Unity, поддерживает LineRenderer.material.color из коробки
                mat.color = riverColor;
                lr.material = mat;
            }
        }

        /// <summary>Классифицирует граничные рёбра и строит два меш-объекта (границы регионов
        /// и берег). Видимость каждого зависит от Combined-режима и соответствующего тоггла.</summary>
        void BuildBorders()
        {
            if (borderContainer != null)
            {
                DestroyBorderObjectAssets(regionBorderObject);
                DestroyBorderObjectAssets(coastlineObject);
                regionBorderObject = null;
                coastlineObject = null;
                Destroy(borderContainer.gameObject);
            }
            if (cells == null) return;

            var containerGO = new GameObject("MapBorders");
            containerGO.transform.SetParent(transform, false);
            borderContainer = containerGO.transform;

            MapBorderBuilder.ClassifyBorderEdges(cells, out var regionEdges, out var coastEdges);

            // Y чуть выше карты (Y=0) и ниже рек (Y=0.5), чтобы избежать z-fighting.
            regionBorderObject = CreateBorderObject(
                "RegionBorders", MapBorderBuilder.BuildRibbonMesh(regionEdges, regionBorderWidth, 0.4f), regionBorderColor);
            coastlineObject = CreateBorderObject(
                "Coastline", MapBorderBuilder.BuildRibbonMesh(coastEdges, coastlineWidth, 0.3f), coastlineColor);

            bool combined = displayMode == MapDisplayMode.Combined;
            regionBorderObject.SetActive(combined && showRegionBordersLayer);
            coastlineObject.SetActive(combined && showCoastlineLayer);
        }

        GameObject CreateBorderObject(string name, Mesh mesh, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(borderContainer, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            // Sprites/Default: unlit, без culling (двусторонний), поддерживает material.color - как у рек.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            mr.sharedMaterial = mat;
            return go;
        }

        /// <summary>Освобождает Mesh и Material объекта границы перед его уничтожением -
        /// Unity не освобождает эти ассеты автоматически при Destroy самого GameObject,
        /// а BuildBorders вызывается часто (в т.ч. на каждый water-override), иначе они утекают.</summary>
        void DestroyBorderObjectAssets(GameObject obj)
        {
            if (obj == null) return;
            var mf = obj.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            var mr = obj.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
        }

        /// <summary>
        /// Перегенерирует ТОЛЬКО температуру (новые случайные эпицентры), не трогая
        /// elevation/moisture/biome/регионы. Требует, чтобы карта уже была сгенерирована
        /// хотя бы раз (GenerateAndRender).
        /// </summary>
        [ContextMenu("Regenerate Temperature Only")]
        public void RegenerateTemperatureOnly()
        {
            if (cells == null)
            {
                Debug.LogWarning("WorldMapRenderer: нельзя перегенерировать температуру - карта ещё не сгенерирована. Сначала вызови Generate World.");
                return;
            }

            var genParams = BuildGenerationParams();
            epicenters = WorldGenerator.GenerateRandomEpicenters(genParams);
            WorldGenerator.RegenerateTemperature(cells, genParams, epicenters);
            lastGenParams = genParams;

            RecolorOnly(); // только перекрашиваем - геометрия и биомы не менялись
            OnDisplayChanged?.Invoke();
        }

        /// <summary>
        /// Применяет ручной override температуры/влажности к указанному набору клеток.
        /// null для temperature/moisture = не трогать это поле.
        /// </summary>
        public void ApplyClimateOverride(IEnumerable<VoronoiCell> targetCells, float? temperature, float? moisture)
        {
            if (cells == null)
            {
                Debug.LogWarning("WorldMapRenderer: нельзя применить climate override - карта ещё не сгенерирована.");
                return;
            }

            CellOverrideService.ApplyClimateOverride(targetCells, temperature, moisture, beachElevationThreshold);
            RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Снимает climate override с указанных клеток и пересчитывает биом обратно на computed-значения.</summary>
        public void ClearClimateOverride(IEnumerable<VoronoiCell> targetCells, bool clearTemperature = true, bool clearMoisture = true)
        {
            if (cells == null) return;

            CellOverrideService.ClearClimateOverride(targetCells, clearTemperature, clearMoisture, beachElevationThreshold);
            RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Применяет override elevation [0,1] к указанным клеткам. null - снять override.</summary>
        public void ApplyElevationOverride(IEnumerable<VoronoiCell> targetCells, float? elevation)
        {
            if (cells == null) return;

            CellOverrideService.ApplyElevationOverride(targetCells, elevation, beachElevationThreshold);
            RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Применяет override water-статуса к указанным клеткам.</summary>
        public void ApplyWaterOverride(IEnumerable<VoronoiCell> targetCells, WaterOverrideType waterType)
        {
            if (cells == null) return;

            CellOverrideService.ApplyWaterOverride(targetCells, waterType, beachElevationThreshold);
            RecolorOnly();
            BuildBorders();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Применяет прямой override биома к указанным клеткам. null - снять override.</summary>
        public void ApplyBiomeOverride(IEnumerable<VoronoiCell> targetCells, Biome? biome)
        {
            if (cells == null) return;

            CellOverrideService.ApplyBiomeOverride(targetCells, biome, beachElevationThreshold);
            RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Снимает ВСЕ override (climate + landscape) с указанных клеток - полный сброс к computed.</summary>
        public void ClearAllOverrides(IEnumerable<VoronoiCell> targetCells)
        {
            if (cells == null) return;

            CellOverrideService.ClearAllOverrides(targetCells, beachElevationThreshold);
            RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        // ---- Brush API (относительные изменения одной клетки + Undo) ----

        readonly BrushUndoManager brushUndo = new BrushUndoManager();

        /// <summary>Начинает новый мазок кистью - вызывать при нажатии ЛКМ в режиме кисти.</summary>
        public void BeginBrushStroke() => brushUndo.BeginStroke();

        /// <summary>Завершает текущий мазок кистью, кладёт его в историю Undo - вызывать при отпускании ЛКМ.</summary>
        public void EndBrushStroke()
        {
            brushUndo.EndStroke();
            // Перекрашиваем/перестраиваем меш один раз по завершении мазка - во время самого мазка
            // (BrushAdjust*) рендер уже обновляется на каждое изменение, так что здесь это просто
            // финальная подстраховка на случай рассинхрона.
            if (cells != null) RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Прибавляет delta к elevation клетки (относительное изменение, кисть). Записывает "досмазковое" состояние клетки в текущий мазок перед изменением.</summary>
        public void BrushAdjustElevation(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustElevation(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }

        /// <summary>Прибавляет delta к температуре клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustTemperature(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustTemperature(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }

        /// <summary>Прибавляет delta к влажности клетки (относительное изменение, кисть).</summary>
        public void BrushAdjustMoisture(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustMoisture(cell, delta, beachElevationThreshold);
            RecolorOnly();
        }

        /// <summary>
        /// Отменяет последний завершённый мазок кистью (Ctrl+Z) - восстанавливает все затронутые
        /// этим мазком клетки к состоянию до мазка. Возвращает false, если истории нет.
        /// </summary>
        public bool UndoLastBrushStroke()
        {
            if (cells == null) return false;
            bool didUndo = brushUndo.Undo();
            if (didUndo)
            {
                RecolorOnly();
                OnDisplayChanged?.Invoke();
            }
            return didUndo;
        }

        public int BrushUndoStackCount => brushUndo.UndoStackCount;

        /// <summary>ПРИМЕР использования: применяет override "вечная зима" (низкая температура, средняя
        /// влажность) к случайно выбранному региону на карте - демонстрация API ApplyClimateOverride
        /// для области целиком. Вызвать через контекстное меню после генерации карты.
        /// </summary>
        [ContextMenu("DEBUG: Apply Eternal Winter To Random Region")]
        public void DebugApplyEternalWinterToRandomRegion()
        {
            if (cells == null)
            {
                Debug.LogWarning("WorldMapRenderer: сначала вызови Generate World.");
                return;
            }

            int regionCount = GetActualRegionCount();
            if (regionCount == 0)
            {
                Debug.LogWarning("WorldMapRenderer: на карте нет регионов суши для применения override.");
                return;
            }

            int targetRegionId = Random.Range(0, regionCount);
            var targetCells = cells.Where(c => c.RegionId == targetRegionId).ToList();

            ApplyClimateOverride(targetCells, temperature: 0.05f, moisture: 0.5f);
            Debug.Log($"WorldMapRenderer: применён override 'вечная зима' к региону {targetRegionId} ({targetCells.Count} клеток).");
        }

        [ContextMenu("Self-Test: Hillshade Brightness")]
        public void SelfTestHillshade()
        {
            const float az = 315f, strength = 3f, ambient = 0.5f;

            float flat = RegionColorPalette.HillshadeBrightness(0f, 0f, strength, az, ambient);
            // Свет с СЗ (азимут 315): склон, "обращённый к свету" - градиент (+x, -y) - ярче обратного.
            float toward = RegionColorPalette.HillshadeBrightness(0.707f, -0.707f, strength, az, ambient);
            float away = RegionColorPalette.HillshadeBrightness(-0.707f, 0.707f, strength, az, ambient);

            bool ok = flat >= ambient - 1e-4f && flat <= 1f + 1e-4f
                      && toward > away && Mathf.Abs(toward - away) > 1e-3f;

            Debug.Log(ok
                ? $"Self-Test Hillshade: PASS (flat={flat:F2}, toward={toward:F2}, away={away:F2})"
                : $"Self-Test Hillshade: FAIL (flat={flat:F2}, toward={toward:F2}, away={away:F2})");
        }

        [ContextMenu("Self-Test: Border Classification")]
        public void SelfTestBorderClassification()
        {
            // Фикстура: две квадратные клетки, общее ребро (1,0)-(1,1).
            var a = new VoronoiCell(0, new System.Numerics.Vector2(0.5f, 0.5f))
            {
                Polygon = new List<System.Numerics.Vector2>
                { new(0, 0), new(1, 0), new(1, 1), new(0, 1) },
                NeighborIds = new List<int> { 1 }
            };
            var b = new VoronoiCell(1, new System.Numerics.Vector2(1.5f, 0.5f))
            {
                Polygon = new List<System.Numerics.Vector2>
                { new(1, 0), new(2, 0), new(2, 1), new(1, 1) },
                NeighborIds = new List<int> { 0 }
            };
            var fixture = new List<VoronoiCell> { a, b };
            bool ok = true;

            // 1) Разные регионы, обе суша -> одна граница региона, берегов нет.
            a.RegionId = 0; b.RegionId = 1; a.IsOcean = false; b.IsOcean = false;
            a.Biome = Biome.Grassland; b.Biome = Biome.Grassland;
            MapBorderBuilder.ClassifyBorderEdges(fixture, out var rEdges, out var cEdges);
            ok &= rEdges.Count == 1 && cEdges.Count == 0;

            // 2) Одна клетка вода -> один берег, границ регионов нет.
            a.RegionId = 0; b.RegionId = 0; a.IsOcean = false; b.IsOcean = true;
            a.Biome = Biome.Grassland; b.Biome = Biome.Ocean;
            MapBorderBuilder.ClassifyBorderEdges(fixture, out rEdges, out cEdges);
            ok &= cEdges.Count == 1 && rEdges.Count == 0;

            // 3) Один регион, обе суша -> ничего.
            a.RegionId = 0; b.RegionId = 0; a.IsOcean = false; b.IsOcean = false;
            a.Biome = Biome.Grassland; b.Biome = Biome.Grassland;
            MapBorderBuilder.ClassifyBorderEdges(fixture, out rEdges, out cEdges);
            ok &= rEdges.Count == 0 && cEdges.Count == 0;

            // 4) Суша <-> внутреннее озеро (разные регионы) -> 1 граница региона, 0 берегов.
            //    Озеро входит в регион через RegionGrowing и обводится его границей.
            a.RegionId = 0; b.RegionId = 1; a.IsOcean = false; b.IsOcean = false;
            a.Biome = Biome.Grassland; b.Biome = Biome.Lake;
            MapBorderBuilder.ClassifyBorderEdges(fixture, out rEdges, out cEdges);
            ok &= rEdges.Count == 1 && cEdges.Count == 0;

            Debug.Log(ok
                ? "Self-Test Border Classification: PASS"
                : "Self-Test Border Classification: FAIL");
        }

        [ContextMenu("Self-Test: Lake Region Unification")]
        public void SelfTestLakeRegionUnification()
        {
            // Компонент A: одна озёрная клетка (R1), окружена двумя сушными клетками R0 → должна стать R0.
            var lakeA = new VoronoiCell(1, new System.Numerics.Vector2(1, 0))
                { IsOcean = false, Biome = Biome.Lake, RegionId = 1, NeighborIds = new List<int> { 10, 11 } };
            var land10 = new VoronoiCell(10, new System.Numerics.Vector2(0, 0))
                { IsOcean = false, Biome = Biome.Grassland, RegionId = 0, NeighborIds = new List<int> { 1 } };
            var land11 = new VoronoiCell(11, new System.Numerics.Vector2(2, 0))
                { IsOcean = false, Biome = Biome.Grassland, RegionId = 0, NeighborIds = new List<int> { 1 } };

            // Компонент B: одна озёрная клетка (R0), окружена двумя сушными клетками R1 → должна стать R1.
            var lakeB = new VoronoiCell(2, new System.Numerics.Vector2(5, 0))
                { IsOcean = false, Biome = Biome.Lake, RegionId = 0, NeighborIds = new List<int> { 20, 21 } };
            var land20 = new VoronoiCell(20, new System.Numerics.Vector2(4, 0))
                { IsOcean = false, Biome = Biome.Grassland, RegionId = 1, NeighborIds = new List<int> { 2 } };
            var land21 = new VoronoiCell(21, new System.Numerics.Vector2(6, 0))
                { IsOcean = false, Biome = Biome.Grassland, RegionId = 1, NeighborIds = new List<int> { 2 } };

            var cells = new List<VoronoiCell> { lakeA, land10, land11, lakeB, land20, land21 };
            LakeRegionUnifier.UnifyLakes(cells);

            bool ok = lakeA.RegionId == 0 && lakeB.RegionId == 1;
            Debug.Log(ok
                ? "Self-Test Lake Region Unification: PASS"
                : $"Self-Test Lake Region Unification: FAIL (lakeA.RegionId={lakeA.RegionId} expected 0; lakeB.RegionId={lakeB.RegionId} expected 1)");
        }

        [ContextMenu("Self-Test: Ocean Connectivity")]
        public void SelfTestOceanConnectivity()
        {
            // Цепочка ocean(0) - lake(1) - lake(2) должна вся стать океаном;
            // изолированное озеро(3), соседствующее только с сушей(4), остаётся озером.
            VoronoiCell C(int id, bool ocean, params int[] nbrs) =>
                new VoronoiCell(id, new System.Numerics.Vector2(id, 0f))
                { IsOcean = ocean, NeighborIds = new List<int>(nbrs) };

            var c0 = C(0, true, 1);
            var c1 = C(1, false, 0, 2);
            var c2 = C(2, false, 1);
            var c3 = C(3, false, 4);
            var c4 = C(4, false, 3);
            var cells = new List<VoronoiCell> { c0, c1, c2, c3, c4 };
            var waterCellIds = new HashSet<int> { 0, 1, 2, 3 }; // клетка 4 - суша

            CellWaterAssigner.PromoteOceanConnectedWater(cells, waterCellIds);

            bool ok = c0.IsOcean && c1.IsOcean && c2.IsOcean && !c3.IsOcean && !c4.IsOcean;
            Debug.Log(ok
                ? "Self-Test Ocean Connectivity: PASS"
                : $"Self-Test Ocean Connectivity: FAIL (c1={c1.IsOcean}, c2={c2.IsOcean}, c3={c3.IsOcean})");
        }

        GenerationParams BuildGenerationParams()
        {
            return new GenerationParams
            {
                Seed = seed,
                Width = mapWidth,
                Height = mapHeight,
                MinPointDistance = minPointDistance,
                LloydRelaxIterations = lloydIterations,
                NumberOfRegions = numberOfRegions,
                FalloffPower = falloffPower,
                InnerRadius = innerRadius,
                SeaLevel = seaLevel,
                MinLakeSize = minLakeSize,
                ElevationCoastWeight = elevationCoastWeight,
                ElevationNoiseWeight = elevationNoiseWeight,
                ElevationNoiseFrequency = elevationNoiseFrequency,
                ElevationNoiseOctaves = elevationNoiseOctaves,
                MoistureFalloffDistance = moistureFalloffDistance,
                NumberOfMoistureEpicenters = numberOfMoistureEpicenters,
                MoistureEpicenterMinRadius = moistureEpicenterMinRadius,
                MoistureEpicenterMaxRadius = moistureEpicenterMaxRadius,
                MoistureEpicenterMinDelta = moistureEpicenterMinDelta,
                MoistureEpicenterMaxDelta = moistureEpicenterMaxDelta,
                NumberOfRivers = numberOfRivers,
                EnableRivers = enableRivers,
                RiverMinStartElevation = riverMinStartElevation,
                BeachElevationThreshold = beachElevationThreshold,
                NumberOfTemperatureEpicenters = numberOfTemperatureEpicenters,
                EpicenterMinRadius = epicenterMinRadius,
                EpicenterMaxRadius = epicenterMaxRadius,
                BaseTemperature = baseTemperature,
                HeightCoolingFactor = heightCoolingFactor
            };
        }

        /// <summary>Ставит назначенную камеру по центру карты сверху вниз - но только один раз за
        /// сессию, чтобы не сбрасывать пользовательский зум/пан при повторной генерации/загрузке
        /// (см. MapCameraController).</summary>
        void PositionCameraOverMap()
        {
            if (cameraPlacedOnce) return;
            cameraPlacedOnce = true;

            float maxSide = Mathf.Max(mapWidth, mapHeight);
            targetCamera.transform.position = new Vector3(mapWidth * 0.5f, maxSide * 1.5f, mapHeight * 0.5f);
            targetCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (targetCamera.farClipPlane < maxSide * 3f)
                targetCamera.farClipPlane = maxSide * 3f;
        }

        public void BuildMesh(List<VoronoiCell> sourceCells)
        {
            cells = sourceCells;

            cellById = new Dictionary<int, VoronoiCell>(cells.Count);
            foreach (var c in cells) cellById[c.Id] = c;
            oceanDistanceFromLand = ComputeOceanDistanceFromLand();

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var triToCell = new List<int>();

            foreach (var cell in cells)
            {
                // Пропускаем вырожденные клетки (например, полностью обрезанные за пределы карты при clipping).
                if (cell.Polygon.Count < 3) continue;

                int baseIndex = vertices.Count;
                Color cellColor = GetColorForCell(cell);

                // Вершина 0 локального веера - центр клетки.
                vertices.Add(ToWorldPos(cell.Site));
                colors.Add(cellColor);

                // Вершины периметра по порядку, plus дублируем первую вершину в конец,
                // чтобы fan triangulation замкнул полигон без отдельного "замыкающего" треугольника.
                foreach (var p in cell.Polygon)
                {
                    vertices.Add(ToWorldPos(p));
                    colors.Add(cellColor);
                }
                vertices.Add(ToWorldPos(cell.Polygon[0])); // дублированная замыкающая вершина
                colors.Add(cellColor);

                int vertCountInFan = cell.Polygon.Count + 2; // центр + периметр + дубликат первой
                var fanTris = PolygonTriangulator.TriangulateFan(vertCountInFan);

                for (int i = 0; i < fanTris.Length; i += 3)
                {
                    // Порядок индексов 0,2,1 (а не 0,1,2) - разворачивает winding order треугольника.
                    // Это исправляет backface culling: без этого лицевая сторона меша смотрела вниз
                    // (от карты в землю) вместо вверх к камере, потому что Voronoi-полигон от
                    // DelaunatorSharp идёт в порядке, противоположном тому, что Unity считает "лицевым"
                    // при взгляде с +Y в left-handed координатах.
                    triangles.Add(baseIndex + fanTris[i]);
                    triangles.Add(baseIndex + fanTris[i + 2]);
                    triangles.Add(baseIndex + fanTris[i + 1]);
                    triToCell.Add(cell.Id);
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = null; // обязательно сбросить перед переприсваиванием - иначе Unity не обновит коллизию на месте
            meshCollider.sharedMesh = mesh;

            triangleToCellId = triToCell.ToArray();
        }

        /// <summary>
        /// Срабатывает при смене режима отображения ИЛИ после новой генерации/перегенерации -
        /// то есть в любой момент, когда легенде имеет смысл перестроить свой список записей.
        /// </summary>
        public event System.Action OnDisplayChanged;

        public void SetDisplayMode(MapDisplayMode mode)
        {
            displayMode = mode;
            if (cells != null) RecolorOnly();
            bool combined = mode == MapDisplayMode.Combined;
            if (regionBorderObject != null) regionBorderObject.SetActive(combined && showRegionBordersLayer);
            if (coastlineObject != null) coastlineObject.SetActive(combined && showCoastlineLayer);
            OnDisplayChanged?.Invoke();
        }

        public void SetShowBiomeLayer(bool on)
        {
            showBiomeLayer = on;
            if (cells != null) RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        public void SetShowReliefLayer(bool on)
        {
            showReliefLayer = on;
            if (cells != null) RecolorOnly();
            OnDisplayChanged?.Invoke();
        }

        public void SetShowRegionBordersLayer(bool on)
        {
            showRegionBordersLayer = on;
            if (regionBorderObject != null)
                regionBorderObject.SetActive(displayMode == MapDisplayMode.Combined && on);
            OnDisplayChanged?.Invoke();
        }

        public void SetShowCoastlineLayer(bool on)
        {
            showCoastlineLayer = on;
            if (coastlineObject != null)
                coastlineObject.SetActive(displayMode == MapDisplayMode.Combined && on);
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Текущее количество РЕАЛЬНО используемых регионов на карте (может быть меньше numberOfRegions, если генерация прошла нестандартно) - нужно легенде, чтобы не показывать несуществующие записи.</summary>
        public int GetActualRegionCount()
        {
            if (cells == null) return 0;
            return cells.Where(c => !c.IsOcean && c.RegionId >= 0)
                         .Select(c => c.RegionId)
                         .Distinct()
                         .Count();
        }

        /// <summary>Перекрашивает существующий меш без полного перестроения геометрии - быстрее при переключении режима отображения.</summary>
        void RecolorOnly()
        {
            var mesh = meshFilter.mesh;
            var colors = new List<Color>(mesh.vertexCount);

            foreach (var cell in cells)
            {
                if (cell.Polygon.Count < 3) continue;
                Color c = GetColorForCell(cell);
                int vertCountInFan = cell.Polygon.Count + 2;
                for (int i = 0; i < vertCountInFan; i++)
                    colors.Add(c);
            }

            mesh.SetColors(colors);
        }

        /// <summary>Оценивает градиент высоты клетки по соседям (направление "вверх по склону").
        /// Используется для рельефного затенения в Combined-режиме.</summary>
        System.Numerics.Vector2 ComputeCellGradient(VoronoiCell cell)
        {
            var g = System.Numerics.Vector2.Zero;
            if (cellById == null) return g;
            foreach (int nId in cell.NeighborIds)
            {
                if (!cellById.TryGetValue(nId, out var n)) continue;
                var dir = n.Site - cell.Site;
                float len = dir.Length();
                if (len < 1e-4f) continue;
                dir /= len;
                g += dir * (n.EffectiveElevation - cell.EffectiveElevation);
            }
            return g;
        }

        /// <summary>
        /// Multi-source BFS по клеткам от берега (не-океанских клеток) вглубь океана - чисто
        /// визуальное расстояние "как далеко от суши", НЕ elevation. EffectiveElevation у океанских
        /// клеток почти всегда 0 (см. ElevationField.ApplyElevation), поэтому она не подходит для
        /// имитации "чем дальше от берега - тем глубже"; это расстояние - отдельная метрика для рендера.
        /// </summary>
        Dictionary<int, int> ComputeOceanDistanceFromLand()
        {
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();

            foreach (var cell in cells)
            {
                if (cell.EffectiveIsOcean) continue;
                distance[cell.Id] = 0;
                foreach (int nId in cell.NeighborIds)
                {
                    if (!cellById.TryGetValue(nId, out var n)) continue;
                    if (n.EffectiveIsOcean && !distance.ContainsKey(n.Id))
                    {
                        distance[n.Id] = 1;
                        queue.Enqueue(n.Id);
                    }
                }
            }

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                int currentDist = distance[currentId];
                if (!cellById.TryGetValue(currentId, out var current)) continue;

                foreach (int nId in current.NeighborIds)
                {
                    if (distance.ContainsKey(nId)) continue;
                    if (!cellById.TryGetValue(nId, out var n) || !n.EffectiveIsOcean) continue;
                    distance[nId] = currentDist + 1;
                    queue.Enqueue(nId);
                }
            }

            maxOceanDistanceFromLand = 1;
            foreach (var cell in cells)
                if (cell.EffectiveIsOcean && distance.TryGetValue(cell.Id, out var d) && d > maxOceanDistanceFromLand)
                    maxOceanDistanceFromLand = d;

            return distance;
        }

        /// <summary>[0,1] "глубина" океанской клетки по расстоянию от берега - 0 у берега, 1 в самой дальней от суши точке. Клетки без записи (не должно случаться для океана) считаются мелководьем.</summary>
        float GetOceanDepth01(VoronoiCell cell)
        {
            if (oceanDistanceFromLand == null) return 0f;
            if (!oceanDistanceFromLand.TryGetValue(cell.Id, out var d)) return 0f;
            return (float)d / maxOceanDistanceFromLand;
        }

        /// <summary>[0,1] "глубина" любой водной клетки для окраски рельефа: океан - по расстоянию от берега, озеро - по elevation (ниже elevation = глубже, т.к. озёра малы и BFS-расстояние там не даёт полезного градиента).</summary>
        float GetWaterDepth01(VoronoiCell cell)
        {
            if (cell.EffectiveIsOcean) return GetOceanDepth01(cell);
            return 1f - Mathf.Clamp01(cell.EffectiveElevation);
        }

        Color GetColorForCell(VoronoiCell cell)
        {
            switch (displayMode)
            {
                case MapDisplayMode.Height:
                    // Используем EffectiveElevation - учитывает ElevationOverride.
                    // Для воды передаём соответствующий биом, чтобы GetHeightColor знал, океан это или озеро.
                    Biome heightBiome = cell.EffectiveIsOcean ? Biome.Ocean
                                      : cell.EffectiveIsLake ? Biome.Lake
                                      : cell.Biome;
                    return RegionColorPalette.GetHeightColor(cell.EffectiveElevation, heightBiome, GetWaterDepth01(cell));

                case MapDisplayMode.Biome:
                    // cell.Biome уже пересчитан через CellOverrideService.RecomputeBiome при любом override.
                    return RegionColorPalette.GetBiomeColor(cell.Biome);

                case MapDisplayMode.Combined:
                {
                    Biome effBiome = cell.EffectiveIsOcean ? Biome.Ocean
                                   : cell.EffectiveIsLake ? Biome.Lake
                                   : cell.Biome;
                    bool isWater = cell.EffectiveIsOcean || cell.EffectiveIsLake;

                    Color baseColor;
                    if (isWater && showReliefLayer)
                    {
                        // Подводный рельеф: цвет по глубине, а не плоская заливка - имитирует
                        // видимый сквозь воду шельф материка (для океана - расстояние от берега,
                        // чем дальше - тем глубже; для озера - по elevation).
                        baseColor = RegionColorPalette.GetWaterColor(GetWaterDepth01(cell), cell.EffectiveIsOcean);
                    }
                    else
                    {
                        baseColor = showBiomeLayer
                            ? RegionColorPalette.GetBiomeColor(effBiome)
                            : RegionColorPalette.GetNeutralBaseColor(cell);
                    }

                    if (showReliefLayer)
                    {
                        var grad = ComputeCellGradient(cell);
                        float b = RegionColorPalette.HillshadeBrightness(
                            grad.X, grad.Y, reliefStrength, reliefLightAzimuth, reliefAmbient);
                        // Подводный рельеф затеняем мягче (вода приглушает контраст света/тени).
                        if (isWater) b = Mathf.Lerp(1f, b, 0.5f);
                        baseColor = new Color(baseColor.r * b, baseColor.g * b, baseColor.b * b, baseColor.a);
                    }
                    return baseColor;
                }

                case MapDisplayMode.Region:
                default:
                    // Используем EffectiveIsOcean - учитывает WaterOverride.
                    if (cell.EffectiveIsOcean)
                        return RegionColorPalette.GetWaterColor(GetOceanDepth01(cell), isOcean: true);
                    return RegionColorPalette.GetRegionColor(cell.RegionId);
            }
        }

        Vector3 ToWorldPos(System.Numerics.Vector2 p)
        {
            // Карта лежит в плоскости XZ (вид сверху), Y = 0. Если нужен 2D top-down (ортографическая камера
            // смотрит вдоль Z) - поменять местами: new Vector3(p.X, p.Y, 0).
            return new Vector3(p.X, 0f, p.Y);
        }

        /// <summary>
        /// Рисует bounds меша как зелёный wireframe-куб напрямую через Gizmos API -
        /// это рисуется в Scene-вьюпорте ВСЕГДА, независимо от материала, шейдера,
        /// режима отображения (Shaded/Wireframe) или культинга.
        /// </summary>
        void OnDrawGizmos()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Gizmos.color = Color.green;
                var bounds = meshFilter.sharedMesh.bounds;
                Gizmos.DrawWireCube(transform.position + bounds.center, bounds.size);
            }

            // Визуализация эпицентров температуры - цвет от синего (холодно) к красному (жарко),
            // прозрачная сфера показывает зону действия (Radius) с hard cutoff.
            if (epicenters != null)
            {
                foreach (var e in epicenters)
                {
                    Vector3 worldPos = transform.position + new Vector3(e.Position.X, 5f, e.Position.Y);
                    Color tempColor = Color.Lerp(Color.cyan, Color.red, e.Temperature);

                    Gizmos.color = new Color(tempColor.r, tempColor.g, tempColor.b, 0.15f);
                    Gizmos.DrawSphere(worldPos, e.Radius);

                    Gizmos.color = tempColor;
                    Gizmos.DrawWireSphere(worldPos, e.Radius);
                    Gizmos.DrawSphere(worldPos, 8f); // маленький непрозрачный маркер центра
                }
            }

            // Визуализация эпицентров влажности - цвет от коричневого (сухо, отрицательный delta)
            // к синему (влажно, положительный delta). Рисуются немного выше температурных (Y=10),
            // чтобы сферы не сливались визуально при совпадении позиций.
            if (moistureEpicenters != null)
            {
                foreach (var e in moistureEpicenters)
                {
                    Vector3 worldPos = transform.position + new Vector3(e.Position.X, 10f, e.Position.Y);
                    float t = Mathf.InverseLerp(-1f, 1f, e.MoistureDelta);
                    Color moistColor = Color.Lerp(new Color(0.6f, 0.4f, 0.2f), new Color(0.2f, 0.4f, 0.9f), t);

                    Gizmos.color = new Color(moistColor.r, moistColor.g, moistColor.b, 0.15f);
                    Gizmos.DrawSphere(worldPos, e.Radius);

                    Gizmos.color = moistColor;
                    Gizmos.DrawWireSphere(worldPos, e.Radius);
                    Gizmos.DrawCube(worldPos, Vector3.one * 10f); // кубический маркер - отличает от круглого маркера температуры
                }
            }

            // Визуализация клеток с активным climate override - жёлтый маркер над каждой
            // переопределённой клеткой, чтобы было видно, где применён override, независимо
            // от текущего displayMode (полезно при отладке/использовании ApplyClimateOverride).
            if (cells != null)
            {
                Gizmos.color = Color.yellow;
                foreach (var cell in cells)
                {
                    if (!cell.TemperatureOverride.HasValue && !cell.MoistureOverride.HasValue) continue;
                    Vector3 worldPos = transform.position + new Vector3(cell.Site.X, 15f, cell.Site.Y);
                    Gizmos.DrawSphere(worldPos, 3f);
                }
            }
        }

        /// <summary>Возвращает клетку под курсором/прицелом по физическому рейкасту в коллайдер карты.</summary>
        public VoronoiCell GetCellUnderRay(Ray ray, float maxDistance = 2000f)
        {
            if (cells == null) return null;
            if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                int cellId = triangleToCellId[hit.triangleIndex];
                return cells.FirstOrDefault(c => c.Id == cellId);
            }
            return null;
        }
    }
}
