using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

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

        [Header("Размер материка и океан вокруг")]
        public float continentWidth = 750f;
        public float continentHeight = 750f;
        [Range(0f, 1f)] public float oceanPadding = 0.2f;

        [Header("Island Shape (форма материка)")]
        [Tooltip("Больше значение = материк занимает больше площади карты, берег обрывается резче у самого края.")]
        public float falloffPower = 1.8f;
        [Tooltip("Доля расстояния от центра карты, внутри которой материк гарантированно не топится falloff'ом. Стандартное значение 0.2.")]
        public float innerRadius = 0.2f;
        [Tooltip("Порог island-shape функции, ниже которого corner считается водой.")]
        public float seaLevel = 0.35f;
        [Tooltip("Минимальный размер связной группы corners, чтобы остаться озером. Больше значение = меньше озёр. 0 или 1 отключает фильтрацию.")]
        public int minLakeSize = 5;

        [Header("Форма материка")]
        [Range(0f, 0.2f)] public float continentCenterJitter = 0.01f;

        [Header("Elevation (Patel-стиль: distance-from-coast + шум)")]
        [Tooltip("Вес компонента 'расстояние от берега' в итоговой elevation. coastWeight + noiseWeight обычно должны давать ~1.0.")]
        public float elevationCoastWeight = 0.6f;
        [Tooltip("Вес компонента 'локальный шум' в итоговой elevation - позволяет горам появляться рядом с побережьем.")]
        public float elevationNoiseWeight = 0.4f;
        public float elevationNoiseFrequency = 0.015f;
        public int elevationNoiseOctaves = 4;
        [Tooltip("Контраст высоты на клетке (вокруг середины). 1 = как есть; больше = выразительнее рельеф и больше высокогорья.")]
        [Range(1f, 2.5f)] public float elevationContrast = 1.5f;

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
        [Tooltip("Высотное охлаждение эффективной температуры при классификации биома. 0.4 ≈ до 2 уровней холоднее на пике.")]
        public float elevationTempDrop = 0.4f;

        [Header("Температура (point-based эпицентры)")]
        [Tooltip("Количество случайных эпицентров температуры на карте.")]
        public int numberOfTemperatureEpicenters = 5;
        public float epicenterMinRadius = 150f;
        public float epicenterMaxRadius = 300f;
        [Tooltip("Базовая температура, только если эпицентров нет вовсе. Иначе температура всегда берётся из эпицентров (мягкий глобальный спад).")]
        public float baseTemperature = 0.5f;

        [Header("Отображение")]
        public MapDisplayMode displayMode = MapDisplayMode.Combined;

        [Header("Combined: слои")]
        public bool showBiomeLayer = true;
        public bool showReliefLayer = true;
        public bool showRegionBordersLayer = false; // оверлей границ регионов - пока выключен по просьбе пользователя, вернёмся позже
        public bool showCoastlineLayer = false;

        [Header("Combined: рельеф (hillshade)")]
        public float reliefStrength = 3f;
        public float reliefLightAzimuth = 315f;
        [Range(0f, 1f)] public float reliefAmbient = 0.5f;

        [Header("Combined: границы")]
        public Color regionBorderColor = new Color(0.10f, 0.10f, 0.10f, 0.9f);
        public float regionBorderWidth = 1.5f;
        public Color coastlineColor = new Color(0.05f, 0.10f, 0.20f, 0.95f);
        public float coastlineWidth = 2.5f;

        [Header("Region labels")]
        [Range(0f, 1f)]
        [Tooltip("Плотность названий зон: меньше = только крупные зоны получают имя, больше = включать средние.")]
        public float labelDensity = 0.4f;

        [Header("Combined: тёмный рендер (MapRaster)")]
        public MapPaletteTheme paletteTheme = MapPaletteTheme.ColdTwilight;
        [Range(0f, 100f)] public float coldLight = 58f;
        [Range(0f, 100f)] public float regionVariation = 45f;
        [Range(40f, 100f)] public float darkness = 72f;
        [Tooltip("Сглаженные границы биомов + полный 'нарисованный' конвейер (тонировка, рельеф, зерно, свечение берега). Выключено = старый плоский вид один-в-один, только через текстуру.")]
        public bool smoothBorders = true;
        [Tooltip("Число итераций сглаживания Чайкина для контура берега (только Combined+smoothBorders). 0 = точные грани клеток Вороного (текущее поведение при выключенном сглаживании).")]
        [Range(0, 5)] public int coastlineSmoothness = 3;
        [Tooltip("Ширина светлого ореола берега со стороны воды, в пикселях (только Combined+smoothBorders). 0 = нет свечения. Масштабируется через поле дистанции - стоимость не зависит от ширины.")]
        [Range(0, 64)] public int coastlineGlowWidth = 16;
        [Tooltip("Плоская заливка суши вместо блендинга (только Combined+smoothBorders): один тон на зону биом+высота, чёткие границы. Выкл = плавный блендинг между биомами.")]
        public bool flatRegionFill = true;
        [Tooltip("Число дискретных полос высоты в плоской заливке (гейт слоя рельефа). Выше = светлее по ступеням.")]
        [Range(2, 8)] public int elevationBands = 5;
        [Tooltip("Размах светлоты между нижней и верхней полосой высоты, %. 0 = полосы не различаются по тону.")]
        [Range(0f, 100f)] public float elevationBandContrast = 40f;
        [Tooltip("Сглаживать (кривить) внутренние границы плоской заливки - семейств биомов и полос высоты - как берег (только Combined+smoothBorders+flatRegionFill). Выкл = грани по клеткам Вороного.")]
        public bool smoothRegionBorders = true;
        [Tooltip("Округлость контуров (берег, биомы, полосы): прореживание вершин перед сглаживанием, в долях среднего размера клетки. 0 = по всем вершинам (детальнее), выше = круглее.")]
        [Range(0f, 3f)] public float borderRoundness = 1f;
        [Tooltip("Большая сторона запекаемой текстуры карты в пикселях; меньшая считается по аспекту mapWidth:mapHeight.")]
        public int rasterLongSide = 2048;

        [Header("Декорации (iso-спрайты террейна)")]
        public WorldGen.Rendering.Decorations.DecorationConfig decorationConfig = new WorldGen.Rendering.Decorations.DecorationConfig();

        WorldGen.Rendering.Decorations.DecorationRenderer decorationRenderer;
        WorldGen.Rendering.Decorations.DecorationCatalog decorationCatalog;
        System.Collections.Generic.List<WorldGen.Rendering.Decorations.DecorationInstance> decorationInstances;

        [Header("Пляж (песок у берега)")]
        [Range(0f, 60f)] public float beachWidth = 20f;
        [Range(0f, 1f)] public float beachStrength = 0f;
        [Tooltip("Жёсткость перехода песок→биом: больше = резче/уже кайма, меньше = мягче/шире растворение.")]
        [Range(0.3f, 4f)] public float beachHardness = 2f;
        public Color beachColor = new Color(0.85f, 0.78f, 0.6f, 1f);

        [Header("GPU-рендер")]
        [Tooltip("Рисовать карту GPU-шейдером (MapTerrain) вместо CPU-запечки текстуры. Выкл = старый CPU-путь (фолбэк).")]
        public bool useGpuRenderer = true;
        GpuMap.GpuMapRenderer gpuRenderer;

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
        Transform riverContainer; // родительский объект для всех LineRenderer рек - упрощает очистку при перегенерации
        Transform borderContainer;        // родитель для меш-объектов границ
        GameObject regionBorderObject;    // меш-лента границ регионов
        GameObject coastlineObject;       // меш-лента береговой линии

        NearestCellLookup nearestLookup;
        Texture2D rasterTexture;
        Material rasterMaterial;
        MapRasterBuffers rasterBuffers;
        int texWidth, texHeight;

        /// <summary>Fired at the end of GenerateAndRender(). PoiManager subscribes to clear all POIs on regen.</summary>
        public event System.Action OnWorldRegenerated;

        /// <summary>Read-only access to current cells for POI placement.</summary>
        public IReadOnlyList<VoronoiCell> Cells => cells;

        /// <summary>Read-only access to the nearest-cell lookup for region-label seeding (sea anchors).</summary>
        public NearestCellLookup NearestLookup => nearestLookup;

        /// <summary>Клетка по Id через готовую карту cellById (O(1)). null — если нет карты или Id неизвестен. Используется radius-кистью в режиме "Сгладить" для чтения соседей.</summary>
        public VoronoiCell GetCellById(int id)
        {
            if (cellById != null && cellById.TryGetValue(id, out var cell)) return cell;
            return null;
        }

        /// <summary>The GenerationParams that actually produced the current Cells (via GenerateAndRender or LoadFromCells) — used by ProjectMenuBar to save it for reference.</summary>
        public GenerationParams LastGenParams => lastGenParams;

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            EnsureRasterMaterial();

            if (useGpuRenderer)
                gpuRenderer = gameObject.GetComponent<GpuMap.GpuMapRenderer>()
                              ?? gameObject.AddComponent<GpuMap.GpuMapRenderer>();
        }

        /// <summary>Живая правка пляжа (ширина/сила/жёсткость) в Inspector применяется мгновенно
        /// в play mode - без пере-бейка карты (см. GpuMapRenderer.SetBeachParams).</summary>
        void OnValidate()
        {
            if (gpuRenderer != null && gpuRenderer.Material != null)
                gpuRenderer.SetBeachParams(beachWidth, beachStrength, beachHardness, beachColor);
            WorldGen.Generation.CellOverrideService.ElevationTempDrop = elevationTempDrop;
            if (Application.isPlaying && cells != null && nearestLookup != null) RebuildDecorations();
        }

        void OnDestroy()
        {
            if (rasterTexture != null) Destroy(rasterTexture);
            if (rasterMaterial != null) Destroy(rasterMaterial);
            if (decorationCatalog != null && decorationCatalog.Atlas != null) Destroy(decorationCatalog.Atlas);
        }

        /// <summary>Sprites/Default: unlit, double-sided (Cull Off) - как у рек/границ в этом же файле
        /// (см. BuildRivers/CreateBorderObject). Предпочтён встроенному Unlit/Texture, чтобы не зависеть
        /// от winding order квада - не нужно подбирать точный порядок вершин, как в старом
        /// BuildMesh для fan-триангуляции клеток. Материал создаётся в коде, поэтому
        /// Assets/WorldGen/Rendering/WorldMaterial.mat больше не используется этим рендерером
        /// (оставлен нетронутым - см. плановые ограничения).</summary>
        void EnsureRasterMaterial()
        {
            if (rasterMaterial != null) return;
            rasterMaterial = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.material = rasterMaterial;
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
            RebuildDecorations();
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
            PrepareLoadFromCells(loadedCells, referenceParams);
            RebakeAll();
            FinishLoadFromCells();
        }

        /// <summary>Первая половина LoadFromCells (данные + геометрия квада, без запека текстуры) -
        /// используется напрямую MapScreenController, чтобы вставить между ней и FinishLoadFromCells
        /// чанковый RebakeAllStepped с прогресс-баром вместо синхронного RebakeAll.</summary>
        public void PrepareLoadFromCells(List<VoronoiCell> loadedCells, GenerationParams referenceParams)
        {
            cells = loadedCells;
            corners = CornerGraphBuilder.Build(cells);

            // Биомы всегда пересчитываются из сохранённого климата (temperature/moisture + их
            // override'ы), а не берутся из загруженного cell.Biome напрямую. Старый per-cell
            // BiomeOverride (снят в Task 5) уже сконвертирован ProjectSerializer'ом в
            // TemperatureOverride/MoistureOverride на этапе Load — здесь он не нужен.
            WorldGen.Generation.CellOverrideService.ElevationTempDrop = elevationTempDrop;
            WorldGen.Generation.CellOverrideService.ClassifyAll(cells, beachElevationThreshold: 0f);
            BeachClassifier.AssignCoastalBeaches(cells);
            rivers = new List<River>();
            epicenters = new List<TemperatureEpicenter>();
            moistureEpicenters = new List<MoistureEpicenter>();
            lastGenParams = referenceParams;
            seed = referenceParams.Seed;
            mapWidth = referenceParams.Width;
            mapHeight = referenceParams.Height;

            RebuildSpatialIndex();
            BuildQuadMesh();
        }

        /// <summary>Вторая половина LoadFromCells (реки, границы, камера, события) - вызывается
        /// MapScreenController после RebakeAllStepped.</summary>
        public void FinishLoadFromCells()
        {
            BuildRivers();
            BuildBorders();
            RebuildDecorations();

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

        /// <summary>Combined+smoothBorders уже рисует собственное свечение берега в самой текстуре -
        /// риббон береговой линии дублировал бы эффект, поэтому скрывается именно в этом случае.
        /// В Height/Region/Biome и Combined-без-сглаживания риббон работает как раньше.</summary>
        bool ShouldShowCoastlineRibbon() =>
            displayMode == MapDisplayMode.Combined && showCoastlineLayer && !smoothBorders;

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
            coastlineObject.SetActive(ShouldShowCoastlineRibbon());
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
        /// elevation/moisture/регионы (биом пересчитывается под новую температуру). Требует, чтобы карта уже была сгенерирована
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
            // Температура влияет на биом → переклассифицируем (было "не трогая biome").
            CellOverrideService.ElevationTempDrop = genParams.ElevationTempDrop;
            CellOverrideService.ClassifyAll(cells, beachElevationThreshold: 0f);
            BeachClassifier.AssignCoastalBeaches(cells);
            lastGenParams = genParams;

            RefreshAfterCellDataChange(); // только перекрашиваем - геометрия не менялась
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
            RefreshAfterCellDataChange();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Precise-selection: sets temperature and/or moisture to a level (null = leave axis) for a
        /// selection, then refreshes. No brush-undo (selection edits are not brush strokes).</summary>
        public void SetClimateLevels(IEnumerable<VoronoiCell> targetCells, int? tempLevel, int? moistLevel)
        {
            if (cells == null) return;
            CellOverrideService.SetClimateLevels(targetCells, tempLevel, moistLevel, beachElevationThreshold);
            RefreshAfterCellDataChange();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Снимает climate override с указанных клеток и пересчитывает биом обратно на computed-значения.</summary>
        public void ClearClimateOverride(IEnumerable<VoronoiCell> targetCells, bool clearTemperature = true, bool clearMoisture = true)
        {
            if (cells == null) return;

            CellOverrideService.ClearClimateOverride(targetCells, clearTemperature, clearMoisture, beachElevationThreshold);
            RefreshAfterCellDataChange();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Применяет override elevation [0,1] к указанным клеткам. null - снять override.</summary>
        public void ApplyElevationOverride(IEnumerable<VoronoiCell> targetCells, float? elevation)
        {
            if (cells == null) return;

            CellOverrideService.ApplyElevationOverride(targetCells, elevation, beachElevationThreshold);
            RefreshAfterCellDataChange();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Применяет override water-статуса к указанным клеткам.</summary>
        public void ApplyWaterOverride(IEnumerable<VoronoiCell> targetCells, WaterOverrideType waterType)
        {
            if (cells == null) return;

            CellOverrideService.ApplyWaterOverride(targetCells, waterType, beachElevationThreshold);
            RefreshAfterCellDataChange();
            BuildBorders();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Снимает ВСЕ override (climate + landscape) с указанных клеток - полный сброс к computed.</summary>
        public void ClearAllOverrides(IEnumerable<VoronoiCell> targetCells)
        {
            if (cells == null) return;

            CellOverrideService.ClearAllOverrides(targetCells, beachElevationThreshold);
            RefreshAfterCellDataChange();
            OnDisplayChanged?.Invoke();
        }

        // ---- Brush API (относительные изменения одной клетки + Undo) ----

        readonly BrushUndoManager brushUndo = new BrushUndoManager();

        /// <summary>Начинает новый мазок кистью - вызывать при нажатии ЛКМ в режиме кисти.</summary>
        public void BeginBrushStroke() => brushUndo.BeginStroke();

        /// <summary>Завершает текущий мазок кистью, кладёт его в историю Undo - вызывать при отпускании ЛКМ.
        /// Здесь мазок "финализируется": FinalizeCoast пересчитывает поле дистанции берега (если мазок
        /// менял сушу/воду), FinalizeLabels пере-печёт сглаженные label'ы взамен угловатой по-клеточной
        /// заплатки, что стояла во время мазка (см. UpdateCells).</summary>
        public void EndBrushStroke()
        {
            brushUndo.EndStroke();
            // Если мазок менял топологию суша/вода - пересчитать поле дистанции берега один раз здесь.
            // FinalizeLabels пере-печёт сглаженные family/band/берег label'ы в затронутой мазком
            // области (во время мазка стояла угловатая по-клеточная заплатка - см. UpdateCells).
            if (useGpuRenderer && gpuRenderer != null) { gpuRenderer.FinalizeCoast(); gpuRenderer.FinalizeLabels(); }
            // Декорации: пересчитать затронутую область (или всю карту, если rect не отслеживается).
            RefreshDecorationsRect(new Rect(0, 0, mapWidth, mapHeight));
            OnDisplayChanged?.Invoke();
        }

        /// <summary>Прибавляет delta к elevation клетки (относительное изменение, кисть). Записывает
        /// "досмазковое" состояние клетки в текущий мазок перед изменением. Не перезапекает текстуру -
        /// BrushToolController вызывает RebakeAffectedCells один раз на весь стемп (см. roadmap-пункт
        /// про перекраску кистью).</summary>
        public void BrushAdjustElevation(VoronoiCell cell, float delta)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.AdjustElevation(cell, delta, beachElevationThreshold);
        }

        /// <summary>Steps this cell's temperature by ±1 level (brush), recording pre-change undo state.</summary>
        public void BrushStepTemperature(VoronoiCell cell, int dir)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.StepTemperatureLevel(cell, dir, beachElevationThreshold);
        }

        /// <summary>Steps this cell's moisture by ±1 level (brush), recording pre-change undo state.</summary>
        public void BrushStepMoisture(VoronoiCell cell, int dir)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.StepMoistureLevel(cell, dir, beachElevationThreshold);
        }

        /// <summary>Sets this cell's temperature to an absolute level (brush smooth), recording undo.</summary>
        public void SetTemperatureLevel(VoronoiCell cell, int level)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.SetClimateLevels(cell, level, null, beachElevationThreshold);
        }

        /// <summary>Sets this cell's moisture to an absolute level (brush smooth), recording undo.</summary>
        public void SetMoistureLevel(VoronoiCell cell, int level)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.SetClimateLevels(cell, null, level, beachElevationThreshold);
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
                RefreshAfterCellDataChange();
                OnDisplayChanged?.Invoke();
            }
            return didUndo;
        }

        public int BrushUndoStackCount => brushUndo.UndoStackCount;

        /// <summary>
        /// Отменяет ВСЕ мазки кисти за сессию разом — откатывает каждую затронутую клетку к её
        /// состоянию до самого первого мазка и очищает историю. Кнопка "Отменить всё" в панели кисти.
        /// </summary>
        public void UndoAllBrushStrokes()
        {
            if (cells == null) return;
            bool any = false;
            while (brushUndo.UndoStackCount > 0)
                any |= brushUndo.Undo();
            if (any)
            {
                RefreshAfterCellDataChange();
                OnDisplayChanged?.Invoke();
            }
        }

        /// <summary>Кисть суша↔вода: makeLand=false → топим клетку (elevation 0) и делает её ForceOcean,
        /// если она граничит с внешним океаном, иначе ForceLake (изолированный внутренний водоём остаётся
        /// озером, а не становится океаном); makeLand=true → ForceLand с ВАРЬИРОВАННОЙ высотой из шума
        /// (разной для каждой клетки), чтобы новая суша была рельефной (холмы/склоны под hillshade), а не
        /// плоским плато одной высоты. Биом пересчитается под новую высоту/климат; пишет "досмазковый"
        /// undo-снимок.</summary>
        public void BrushSetWater(VoronoiCell cell, bool makeLand)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            if (makeLand)
            {
                cell.WaterOverride = WaterOverrideType.ForceLand;
                float n = WorldGen.Rendering.MapRaster.Noise.Fbm(cell.Site.X * 0.045f, cell.Site.Y * 0.045f, seed, 4);
                cell.ElevationOverride = Mathf.Clamp(beachElevationThreshold + 0.08f + n * 0.55f, 0f, 1f);
            }
            else
            {
                // Вода: если клетка НЕ граничит с внешним океаном — это внутренний водоём (озеро), а не океан.
                // Прибрежная/океан-смежная вода становится океаном (продолжает его); изолированная — озером.
                bool touchesOcean = false;
                foreach (int nid in cell.NeighborIds)
                {
                    var n = GetCellById(nid);
                    if (n != null && n.EffectiveIsOcean) { touchesOcean = true; break; }
                }
                cell.WaterOverride = touchesOcean ? WaterOverrideType.ForceOcean : WaterOverrideType.ForceLake;
                cell.ElevationOverride = 0f; // топим клетку под уровень моря в обоих случаях
            }
            CellOverrideService.RecomputeBiome(cell, beachElevationThreshold);
        }

        /// <summary>Biome brush DURING-stroke: paints the selected matrix cell and previews it as that biome
        /// (drop=0, no elevation cooling) for WYSIWYG feedback. Cooling is applied on stroke end via
        /// FinalizeBiomeStroke. Records pre-change undo state.</summary>
        public void BrushSetClimateLevelsPreview(VoronoiCell cell, int t, int m)
        {
            if (cells == null) return;
            brushUndo.RecordBeforeChange(cell);
            CellOverrideService.SetClimateLevelsPreview(cell, t, m, beachElevationThreshold);
        }

        /// <summary>Biome-brush stroke end: reclassifies the painted cells with the REAL elevation cooling
        /// (honest final biome), then rebakes. No new undo snapshot — the stroke's pre-change state was already
        /// captured on first touch, so Ctrl+Z still reverts to before the stroke.</summary>
        public void FinalizeBiomeStroke(System.Collections.Generic.IEnumerable<VoronoiCell> strokeCells)
        {
            if (cells == null) return;
            foreach (var cell in strokeCells)
                CellOverrideService.RecomputeBiome(cell, beachElevationThreshold);
            RebakeAffectedCells(strokeCells);
        }

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

        [ContextMenu("Self-Test: Noise Determinism And Range")]
        public void SelfTestNoise()
        {
            float h1 = WorldGen.Rendering.MapRaster.Noise.Hash(3, 7, 42);
            float h2 = WorldGen.Rendering.MapRaster.Noise.Hash(3, 7, 42);
            float h3 = WorldGen.Rendering.MapRaster.Noise.Hash(4, 7, 42);

            float v1 = WorldGen.Rendering.MapRaster.Noise.ValueNoise(1.3f, 2.7f, 5);
            float v2 = WorldGen.Rendering.MapRaster.Noise.ValueNoise(1.3f, 2.7f, 5);

            float f1 = WorldGen.Rendering.MapRaster.Noise.Fbm(0.5f, 0.5f, 9, 4);
            float f2 = WorldGen.Rendering.MapRaster.Noise.Fbm(0.5f, 0.5f, 9, 4);

            bool ok = h1 == h2 && h1 != h3 && h1 >= 0f && h1 < 1f
                      && v1 == v2 && v1 >= 0f && v1 < 1f
                      && f1 == f2 && f1 >= 0f && f1 <= 1f;

            Debug.Log(ok
                ? "Self-Test Noise Determinism And Range: PASS"
                : $"Self-Test Noise Determinism And Range: FAIL (h1={h1}, h3={h3}, v1={v1}, f1={f1})");
        }

        [ContextMenu("Self-Test: Nearest Cell Lookup")]
        public void SelfTestNearestCellLookup()
        {
            var fixtureCells = new List<VoronoiCell>
            {
                new VoronoiCell(0, new System.Numerics.Vector2(0f, 0f)),
                new VoronoiCell(1, new System.Numerics.Vector2(20f, 0f)),
                new VoronoiCell(2, new System.Numerics.Vector2(0f, 20f)),
                new VoronoiCell(3, new System.Numerics.Vector2(20f, 20f)),
                new VoronoiCell(4, new System.Numerics.Vector2(10f, 10f)),
            };
            // NearestCellLookup исключает вырожденные клетки (Polygon.Count меньше 3) - без этого
            // вся фикстура была бы отброшена и FindNearest везде возвращал бы null.
            foreach (var c in fixtureCells) c.Polygon = SquarePolygon(c.Site);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 10f);

            bool ok = true;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(1f, 1f))?.Id == 0;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(19f, 1f))?.Id == 1;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(1f, 19f))?.Id == 2;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(19f, 19f))?.Id == 3;
            ok &= lookup.FindNearest(new System.Numerics.Vector2(10f, 10f))?.Id == 4;

            // Точка (10,0) равноудалена (dist=10) от клеток 0, 1 и 4 - проверяем только, что
            // возвращается ОДИН ИЗ валидных кандидатов, а не null и не клетка 2/3 (те дальше).
            var boundary = lookup.FindNearest(new System.Numerics.Vector2(10f, 0f));
            ok &= boundary != null && (boundary.Id == 0 || boundary.Id == 1 || boundary.Id == 4);

            Debug.Log(ok ? "Self-Test Nearest Cell Lookup: PASS" : "Self-Test Nearest Cell Lookup: FAIL");
        }

        [ContextMenu("Self-Test: Biome Family Coverage")]
        public void SelfTestBiomeFamilyCoverage()
        {
            bool ok = true;
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                try
                {
                    WorldGen.Rendering.MapRaster.MapPalette.GetFamily(biome);
                }
                catch (System.Exception)
                {
                    ok = false;
                    Debug.LogWarning($"MapPalette.GetFamily не обрабатывает Biome.{biome}");
                }
            }
            Debug.Log(ok ? "Self-Test Biome Family Coverage: PASS" : "Self-Test Biome Family Coverage: FAIL");
        }

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

        [ContextMenu("Self-Test: Island Shape Ocean Border")]
        public void SelfTestIslandShapeOceanBorder()
        {
            var gen = new WorldGen.Generation.HeightmapGenerator(seed: 7, coreWidth: 500f, coreHeight: 500f, originX: 0f, originY: 0f);
            const float seaLevel = 0.35f;

            // Все 4 середины рёбер попадают в borderWaterMargin (0.06) → falloff=1 → высота < 0 < seaLevel.
            bool edgesWater =
                gen.GetHeight(250f, 2f)   < seaLevel &&
                gen.GetHeight(250f, 498f) < seaLevel &&
                gen.GetHeight(2f,   250f) < seaLevel &&
                gen.GetHeight(498f, 250f) < seaLevel;

            // Детерминизм: один сид → один результат.
            float a = gen.GetHeight(123f, 234f);
            var gen2 = new WorldGen.Generation.HeightmapGenerator(seed: 7, coreWidth: 500f, coreHeight: 500f, originX: 0f, originY: 0f);
            bool deterministic = gen2.GetHeight(123f, 234f) == a;

            bool ok = edgesWater && deterministic;
            Debug.Log(ok
                ? "Self-Test Island Shape Ocean Border: PASS"
                : $"Self-Test Island Shape Ocean Border: FAIL (edgesWater={edgesWater}, deterministic={deterministic})");
        }

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

        /// <summary>Маленький квадратный полигон вокруг site - нужен фикстурам самотестов, работающих
        /// с NearestCellLookup/MapRasterizer, чтобы пройти проверку Polygon.Count меньше 3 (клетки без
        /// полигона считаются вырожденными "клетками-призраками" и исключаются из индекса - см. Self-Test:
        /// Degenerate Cell Excluded From Raster Lookup, где это исключение как раз проверяется).</summary>
        static List<System.Numerics.Vector2> SquarePolygon(System.Numerics.Vector2 site, float half = 1f) => new List<System.Numerics.Vector2>
        {
            new(site.X - half, site.Y - half),
            new(site.X + half, site.Y - half),
            new(site.X + half, site.Y + half),
            new(site.X - half, site.Y + half),
        };

        [ContextMenu("Self-Test: Raster Hard Mode Parity")]
        public void SelfTestRasterHardModeParity()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(2.5f, 5f)) { Biome = Biome.Grassland, RegionId = 0, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site);
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7.5f, 5f)) { Biome = Biome.Grassland, RegionId = 1, IsOcean = false };
            b.Polygon = SquarePolygon(b.Site);
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a, [1] = b };

            var savedDisplayMode = displayMode;
            displayMode = MapDisplayMode.Region;
            Color expectedA = GetColorForCell(a);

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 10,
                TexHeight = 10,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = false,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
            var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, displayMode, config, tex, buffers, 0, 0, 10, 10);

            // Пиксель (2,5) на текстуре 10x10 для карты 10x10 сэмплирует мировую точку (2.5, 5.5) -
            // ближе всего к Site клетки a (2.5, 5).
            Color actual = tex.GetPixel(2, 5);
            bool ok = Mathf.Abs(expectedA.r - actual.r) < 0.01f
                      && Mathf.Abs(expectedA.g - actual.g) < 0.01f
                      && Mathf.Abs(expectedA.b - actual.b) < 0.01f;

            displayMode = savedDisplayMode;
            Destroy(tex);

            Debug.Log(ok
                ? "Self-Test Raster Hard Mode Parity: PASS"
                : $"Self-Test Raster Hard Mode Parity: FAIL (expected={expectedA}, actual={actual})");
        }

        [ContextMenu("Self-Test: Raster Elevation Invariant")]
        public void SelfTestRasterElevationInvariant()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(3f, 3f)) { Biome = Biome.Grassland, Height = 0.42f, Temperature = 0.5f, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site);
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7f, 7f)) { Biome = Biome.Grassland, Height = 0.6f, Temperature = 0.5f, IsOcean = false };
            b.Polygon = SquarePolygon(b.Site);
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a, [1] = b };

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 20,
                TexHeight = 20,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = true,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f,
                RegionVariation = 0f,
                Darkness = 40f,
                SmoothRadius = 0.01f, // почти отключаем блендинг с соседом b - проверяем чистый сэмпл клетки a
                ReliefStrength = 3f,
                ReliefLightAzimuth = 315f,
                ReliefAmbient = 0.5f,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
            var tex = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 20, 20);

            int px = Mathf.FloorToInt(3f / 10f * 20f);
            int py = Mathf.FloorToInt(3f / 10f * 20f);
            float sampledElevation = buffers.Elevation[py * 20 + px];
            bool ok = Mathf.Abs(sampledElevation - a.EffectiveElevation) < 0.02f;

            Destroy(tex);
            Debug.Log(ok
                ? "Self-Test Raster Elevation Invariant: PASS"
                : $"Self-Test Raster Elevation Invariant: FAIL (sampled={sampledElevation:F3}, expected={a.EffectiveElevation:F3})");
        }

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

        /// <summary>
        /// Регрессия на баг, найденный при первой реальной генерации мира: клетки с вырожденным
        /// полигоном (Polygon.Count меньше 3 - например, полностью обрезанные за пределы карты)
        /// никогда не получают corners (CornerGraphBuilder.Build пропускает их тем же guard'ом),
        /// из-за чего CellClimateAverager.ApplyToCells тоже пропускает их классификацию - Biome
        /// остаётся на C#-дефолте (Ocean = 0), хотя IsOcean явно false. Раньше такие "клетки-призраки"
        /// никогда не попадали в рендер (старый BuildMesh/RecolorOnly явно пропускали Polygon.Count
        /// меньше 3); фикстура ниже воссоздаёт эту ситуацию напрямую, без запуска полной генерации.
        /// </summary>
        [ContextMenu("Self-Test: Degenerate Cell Excluded From Raster Lookup")]
        public void SelfTestDegenerateCellExcludedFromLookup()
        {
            var good = new VoronoiCell(0, new System.Numerics.Vector2(5f, 5f)) { Biome = Biome.Grassland, IsOcean = false };
            good.Polygon = SquarePolygon(good.Site);
            // "Клетка-призрак": Polygon не заполнен (Count == 0 меньше 3), Biome остаётся на
            // C#-дефолте (Ocean), IsOcean = false - именно так выглядит клетка, которую
            // CellClimateAverager.ApplyToCells пропустил при классификации.
            var ghost = new VoronoiCell(1, new System.Numerics.Vector2(5.3f, 5f)) { IsOcean = false };
            var fixtureCells = new List<VoronoiCell> { good, ghost };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = good, [1] = ghost };

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 10,
                TexHeight = 10,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = true,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f,
                RegionVariation = 45f,
                Darkness = 72f,
                SmoothRadius = 3f,
                ReliefStrength = 3f,
                ReliefLightAzimuth = 315f,
                ReliefAmbient = 0.5f,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };
            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
            var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);

            bool threw = false;
            try
            {
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
            }
            catch (System.Exception e)
            {
                threw = true;
                Debug.LogWarning($"Self-Test Degenerate Cell Excluded From Raster Lookup threw: {e.GetType().Name}: {e.Message}");
            }

            bool ghostFoundAsNearest = lookup.FindNearest(new System.Numerics.Vector2(5.3f, 5f))?.Id == 1;
            bool ghostFoundInRadius = false;
            foreach (var (cell, _) in lookup.FindWithinRadius(new System.Numerics.Vector2(5f, 5f), 5f))
                if (cell.Id == 1) ghostFoundInRadius = true;

            bool ok = !threw && !ghostFoundAsNearest && !ghostFoundInRadius;

            Destroy(tex);
            Debug.Log(ok
                ? "Self-Test Degenerate Cell Excluded From Raster Lookup: PASS"
                : $"Self-Test Degenerate Cell Excluded From Raster Lookup: FAIL (threw={threw}, ghostFoundAsNearest={ghostFoundAsNearest}, ghostFoundInRadius={ghostFoundInRadius})");
        }

        /// <summary>
        /// Регрессия на горизонтальные полосы, найденные при живом тестировании RebakeAllStepped:
        /// прежняя реализация звала полный RebakeRegion (cellId + BakePaintedFields + раскраска +
        /// виньетка) отдельно на каждый чанк строк, так что раскраска последней строки чанка читала
        /// градиент рельефа/проверку берега на СЛЕДУЮЩУЮ строку - а та принадлежала ещё не запечённому
        /// чанку и её Elevation/CellId были нулями по умолчанию, а не настоящими значениями. Тест
        /// сравнивает "честный" полный запек с двухфазным (BakeFieldsRect на всё изображение, потом
        /// ColorAndVignetteRect по кускам) - на границе кусков они должны совпасть пиксель-в-пиксель.
        /// </summary>
        [ContextMenu("Self-Test: Chunked Bake Continuity")]
        public void SelfTestChunkedBakeContinuity()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(3f, 5f)) { Biome = Biome.Grassland, Height = 0.2f, Temperature = 0.5f, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site);
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7f, 5f)) { Biome = Biome.Grassland, Height = 0.8f, Temperature = 0.5f, IsOcean = false };
            b.Polygon = SquarePolygon(b.Site);
            var fixtureCells = new List<VoronoiCell> { a, b };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a, [1] = b };

            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 20,
                TexHeight = 20,
                MapWidth = 10f,
                MapHeight = 10f,
                Seed = 1,
                SmoothBorders = true,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f,
                RegionVariation = 0f, // без шума региональной вариации - сравнение пикселей должно быть точным
                Darkness = 72f,
                SmoothRadius = 4f,
                ReliefStrength = 3f,
                ReliefLightAzimuth = 315f,
                ReliefAmbient = 0.5f,
                ShowBiomeLayer = true,
                ShowReliefLayer = true,
                HardModeColor = GetColorForCell,
                WaterDepth01 = _ => 0f,
            };

            // Эталон: честный полный запек одним вызовом.
            var buffersRef = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
            var texRef = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texRef, buffersRef, 0, 0, 20, 20);

            // Двухфазный чанковый запек (исправление) - поля на всё изображение разом, затем
            // раскраска двумя чанками по 10 строк (граница ровно на строке 10).
            var buffersChunked = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
            var texChunked = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.BakeFieldsRect(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, buffersChunked, 0, 0, 20, 20);
            WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 0, 20, 10);
            WorldGen.Rendering.MapRaster.MapRasterizer.ColorAndVignetteRect(fixtureById, MapDisplayMode.Combined, config, texChunked, buffersChunked, 0, 10, 20, 10);

            // Наивный чанковый запек (старый баг, до исправления) - полный RebakeRegion (все 3
            // прохода разом) отдельно на каждый чанк, будто BakeFieldsRect никогда не прогонялся
            // на всё изображение заранее. Должен РАСХОДИТЬСЯ с эталоном ровно на границе - это
            // доказывает, что тест действительно ловит регресс, а не просто тавтологически проверяет
            // сам себя.
            var buffersNaive = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(20, 20);
            var texNaive = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 0, 20, 10);
            WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, texNaive, buffersNaive, 0, 10, 20, 10);

            bool fixedMatchesRef = true, naiveDiffersFromRef = false;
            for (int x = 0; x < 20; x++)
            {
                for (int y = 8; y <= 11; y++) // как раз вокруг границы чанков (строка 10)
                {
                    Color pr = texRef.GetPixel(x, y);
                    Color pc = texChunked.GetPixel(x, y);
                    Color pn = texNaive.GetPixel(x, y);

                    if (Mathf.Abs(pr.r - pc.r) > 0.004f || Mathf.Abs(pr.g - pc.g) > 0.004f || Mathf.Abs(pr.b - pc.b) > 0.004f)
                        fixedMatchesRef = false;
                    if (Mathf.Abs(pr.r - pn.r) > 0.004f || Mathf.Abs(pr.g - pn.g) > 0.004f || Mathf.Abs(pr.b - pn.b) > 0.004f)
                        naiveDiffersFromRef = true;
                }
            }

            bool ok = fixedMatchesRef && naiveDiffersFromRef;

            Destroy(texRef);
            Destroy(texChunked);
            Destroy(texNaive);
            Debug.Log(ok
                ? "Self-Test Chunked Bake Continuity: PASS"
                : $"Self-Test Chunked Bake Continuity: FAIL (fixedMatchesRef={fixedMatchesRef}, naiveDiffersFromRef={naiveDiffersFromRef} - naive должен был разойтись, доказывая что баг реален)");
        }

        /// <summary>
        /// Регрессия: тумблеры "Биом"/"Рельеф" (MapLayersPanel, существующие поля showBiomeLayer/
        /// showReliefLayer) никогда не попадали в MapRasterConfig, поэтому в painted-конвейере
        /// (Combined+smoothBorders) их выключение не давало вообще никакого визуального эффекта -
        /// притом что RebakeAll() всё равно честно перезапекал всю текстуру (несколько секунд впустую).
        /// Тест печёт одну и ту же клетку с обоими значениями каждого тумблера и проверяет, что цвет
        /// пикселя действительно меняется.
        /// </summary>
        [ContextMenu("Self-Test: Layer Toggles Affect Raster Output")]
        public void SelfTestLayerTogglesAffectRasterOutput()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(5f, 5f)) { Biome = Biome.Grassland, Height = 0.8f, Temperature = 0.5f, IsOcean = false };
            a.Polygon = SquarePolygon(a.Site);
            var fixtureCells = new List<VoronoiCell> { a };
            var fixtureById = new Dictionary<int, VoronoiCell> { [0] = a };
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 5f);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);

            WorldGen.Rendering.MapRaster.MapRasterConfig MakeConfig(bool showBiome, bool showRelief) => new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 10, TexHeight = 10, MapWidth = 10f, MapHeight = 10f, Seed = 1,
                SmoothBorders = true, Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 4f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.3f,
                ShowBiomeLayer = showBiome, ShowReliefLayer = showRelief,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            Color BakePixel(bool showBiome, bool showRelief)
            {
                var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(10, 10);
                var tex = new Texture2D(10, 10, TextureFormat.RGBA32, false);
                var config = MakeConfig(showBiome, showRelief);
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 10, 10);
                Color c = tex.GetPixel(5, 5);
                Destroy(tex);
                return c;
            }

            Color biomeOn = BakePixel(true, true);
            Color biomeOff = BakePixel(false, true);
            Color reliefOn = BakePixel(true, true);
            Color reliefOff = BakePixel(true, false);

            bool biomeDiffers = Mathf.Abs(biomeOn.r - biomeOff.r) > 0.02f || Mathf.Abs(biomeOn.g - biomeOff.g) > 0.02f || Mathf.Abs(biomeOn.b - biomeOff.b) > 0.02f;
            bool reliefDiffers = Mathf.Abs(reliefOn.r - reliefOff.r) > 0.02f || Mathf.Abs(reliefOn.g - reliefOff.g) > 0.02f || Mathf.Abs(reliefOn.b - reliefOff.b) > 0.02f;

            bool ok = biomeDiffers && reliefDiffers;
            Debug.Log(ok
                ? "Self-Test Layer Toggles Affect Raster Output: PASS"
                : $"Self-Test Layer Toggles Affect Raster Output: FAIL (biomeDiffers={biomeDiffers}, reliefDiffers={reliefDiffers})");
        }

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

        /// <summary>RegionLabelBaker.BakeRect на raw-массивах (не через MapRasterizer/CPU-путь): сетка
        /// 5x5, рамка - океан, внутренний 3x3 - суша; левый столбец (c=1) - Snow (высота 0.9 → верхняя
        /// полоса), правые два (c=2,3) - Grassland (высота 0.1 → нижняя полоса). cellIdArray строится
        /// как в GpuMapRenderer.FinishBuild (CellIdTexture.Build → GetPixels → округление .r).
        /// Глубинный пиксель каждого региона получает верную метку биома/полосы; водный пиксель
        /// остаётся -1 (сентинел, не затёрт).</summary>
        [ContextMenu("Self-Test: Region Label Baker")]
        public void SelfTestRegionLabelBaker()
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

            const int texW = 50, texH = 50;
            const float mapW = 5f, mapH = 5f;
            var cellIdTex = WorldGen.Rendering.GpuMap.CellIdTexture.Build(lookup, texW, texH, mapW, mapH);
            var idPixels = cellIdTex.GetPixels();
            var cellIdArray = new int[idPixels.Length];
            for (int i = 0; i < idPixels.Length; i++) cellIdArray[i] = Mathf.RoundToInt(idPixels[i].r);
            Destroy(cellIdTex);

            var familyLabel = new int[texW * texH];
            var bandLabel = new int[texW * texH];
            var isLandMask = new bool[texW * texH];
            WorldGen.Rendering.MapRaster.RegionLabelBaker.BakeRect(
                byId, corners, cellIdArray, familyLabel, bandLabel, isLandMask,
                texW, texH, mapW, mapH, smoothing: 2, decimation: 0f, bands: 5,
                rectX: 0, rectY: 0, rectW: texW, rectH: texH);

            // Глубинные пиксели (центры клеток, 10 текс/ед): Grassland (c=3,r=2)→(30,20); Snow (c=1,r=2)→(10,20).
            int grassIdx = 20 * texW + 30;
            int snowIdx = 20 * texW + 10;
            int waterIdx = 0 * texW + 0; // мир (0.5,0.5) - угол рамки, гарантированно вода
            int grass = (int)Biome.Grassland;
            int snowBiome = (int)Biome.Snow;

            bool grassFamOk = familyLabel[grassIdx] == grass;
            bool snowFamOk = familyLabel[snowIdx] == snowBiome;
            bool grassBandOk = bandLabel[grassIdx] == 0;  // 0.1*5=0
            bool snowBandOk = bandLabel[snowIdx] == 4;    // 0.9*5=4
            bool waterFamStaysUnset = familyLabel[waterIdx] == -1;
            bool waterBandStaysUnset = bandLabel[waterIdx] == -1;
            bool grassIsLandOk = isLandMask[grassIdx];
            bool snowIsLandOk = isLandMask[snowIdx];
            bool waterIsLandOk = !isLandMask[waterIdx];

            bool bakerOk = grassFamOk && snowFamOk && grassBandOk && snowBandOk && waterFamStaysUnset && waterBandStaysUnset
                           && grassIsLandOk && snowIsLandOk && waterIsLandOk;
            Debug.Log(bakerOk
                ? "Self-Test Region Label Baker: PASS"
                : $"Self-Test Region Label Baker: FAIL (grassFam={familyLabel[grassIdx]}/{grass}, snowFam={familyLabel[snowIdx]}/{snowBiome}, grassBand={bandLabel[grassIdx]}/0, snowBand={bandLabel[snowIdx]}/4, waterFam={familyLabel[waterIdx]}, waterBand={bandLabel[waterIdx]}, grassIsLand={isLandMask[grassIdx]}, snowIsLand={isLandMask[snowIdx]}, waterIsLand={isLandMask[waterIdx]})");
        }

        /// <summary>Кисть должна патчить label'ы МГНОВЕННО (угловато, по клеткам) во время мазка, без
        /// ожидания FinalizeLabels - через GpuMapRenderer.UpdateCells → PatchCellLabelFaceted (Task 6).
        /// Фикстура: сетка 5x5 (как в других само-тестах), рамка - океан, внутренний 3x3 - Grassland.
        /// Строит настоящий GpuMapRenderer (BuildAll), затем переводит центральную клетку (c=2,r=2) в
        /// ForceOcean (как реальная water-кисть - см. CellOverrideService.ApplyWaterOverride) и вызывает
        /// UpdateCells БЕЗ FinalizeLabels - глубинный пиксель этой клетки в _LabelTex должен немедленно
        /// показать isLand=false (B=0), что подтверждает реальный вызов PatchCellLabelFaceted и
        /// rect-загрузку на GPU-текстуру (не просто то, что категория пересчитывается где-то в памяти).</summary>
        [ContextMenu("Self-Test: Faceted Label Patch On Brush Edit")]
        public void SelfTestFacetedLabelPatch()
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
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(cells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, 1f);

            const int texW = 50, texH = 50;
            const float mapW = 5f, mapH = 5f;
            int idx = 20 * texW + 20; // глубинный пиксель клетки (c=2,r=2), 10 текс/ед (см. другие само-тесты)

            var go = new GameObject("SelfTest_GpuMapRenderer_FacetedPatch");
            go.AddComponent<MeshRenderer>();
            var gpu = go.AddComponent<WorldGen.Rendering.GpuMap.GpuMapRenderer>();
            gpu.BuildAll(cells, lookup, texW, texH, mapW, mapH, WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight, corners);

            var labelTexBefore = gpu.Material.GetTexture("_LabelTex") as Texture2D;
            bool wasLandBefore = labelTexBefore != null && labelTexBefore.GetPixels32()[idx].b == 255;

            var midCell = cells.First(cc => cc.Site.X == 2 && cc.Site.Y == 2);
            midCell.WaterOverride = WorldGen.Generation.WaterOverrideType.ForceOcean; // имитация water-кисти
            gpu.UpdateCells(new List<VoronoiCell> { midCell }); // намеренно БЕЗ FinalizeLabels

            var labelTexAfter = gpu.Material.GetTexture("_LabelTex") as Texture2D;
            bool sameTextureRef = ReferenceEquals(labelTexBefore, labelTexAfter); // PatchRect не должен пересоздавать текстуру
            bool isWaterAfterPatch = labelTexAfter != null && labelTexAfter.GetPixels32()[idx].b == 0;

            bool ok = wasLandBefore && sameTextureRef && isWaterAfterPatch;
            Debug.Log(ok
                ? "Self-Test Faceted Label Patch On Brush Edit: PASS"
                : $"Self-Test Faceted Label Patch On Brush Edit: FAIL (wasLandBefore={wasLandBefore}, sameTextureRef={sameTextureRef}, isWaterAfterPatch={isWaterAfterPatch})");

            Destroy(go);
        }

        [ContextMenu("Self-Test: GPU CellId Texture")]
        public void SelfTestGpuCellIdTexture()
        {
            var a = new VoronoiCell(0, new System.Numerics.Vector2(2.5f, 2.5f));
            a.Polygon = SquarePolygon(a.Site);   // NearestCellLookup исключает клетки с Polygon.Count<3
            var b = new VoronoiCell(1, new System.Numerics.Vector2(7.5f, 7.5f));
            b.Polygon = SquarePolygon(b.Site);
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
            Color a0 = attr.Texture.GetPixel(0 % w, 0 / w);                 // клетка 0, слот A
            Color b0 = attr.Texture.GetPixel(1 % w, 1 / w);                 // клетка 1, слот A
            Color a1 = attr.Texture.GetPixel(0 % w, attr.CellRows + 0 / w); // клетка 0, слот B (regionId)

            bool ok = Mathf.Approximately(a0.g, 0.4f) && Mathf.Approximately(a0.b, 0.6f)
                      && Mathf.Approximately(a0.a, 0f)   // суша
                      && Mathf.Approximately(b0.a, 1f)   // океан
                      && Mathf.RoundToInt(a1.r) == 3;
            Object.Destroy(attr.Texture);
            Debug.Log(ok ? "Self-Test GPU Attribute Texture: PASS" : $"Self-Test GPU Attribute Texture: FAIL (a0={a0}, b0={b0}, region={a1.r})");
        }

        /// <summary>Регрессионный тест на баг RG16: SetPixels32/GetPixels32 поддерживаются только
        /// RGBA32/ARGB32/RGB24/Alpha8 и молча игнорируются на прочих форматах. На старом RG16 этот
        /// тест читал бы одни нули и падал; на исправленном RGBA32 круговой путь Build → GetPixels32
        /// должен вернуть ровно те же family/band/isLand, что были закодированы (R/G/B).</summary>
        [ContextMenu("Self-Test: Region Label Texture Round-Trip")]
        public void SelfTestRegionLabelTextureRoundTrip()
        {
            int[] familyLabel = { 0, 3, 7, -1 };
            int[] bandLabel   = { 1, 2, -1, 4 };
            bool[] isLandMask = { true, true, false, false };
            byte[] expectedR  = { 0, 3, 7, 255 }; // family, -1 → sentinel 255
            byte[] expectedG  = { 1, 2, 255, 4 }; // band, -1 → sentinel 255
            byte[] expectedB  = { 255, 255, 0, 0 }; // isLand → 255/0

            var labelTex = new WorldGen.Rendering.GpuMap.RegionLabelTexture();
            labelTex.Build(familyLabel, bandLabel, isLandMask, 2, 2);
            Color32[] got = labelTex.Texture.GetPixels32();

            bool ok = got.Length == 4;
            for (int i = 0; i < 4 && ok; i++)
                ok &= got[i].r == expectedR[i] && got[i].g == expectedG[i] && got[i].b == expectedB[i];

            labelTex.Destroy();
            Debug.Log(ok
                ? "Self-Test Region Label Texture Round-Trip: PASS"
                : $"Self-Test Region Label Texture Round-Trip: FAIL (got=[{string.Join(", ", got.Select(p => $"({p.r},{p.g},{p.b})"))}], expectedR=[{string.Join(",", expectedR)}], expectedG=[{string.Join(",", expectedG)}], expectedB=[{string.Join(",", expectedB)}])");
        }

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

        /// <summary>Симулирует мазок кисти, меняющий WaterOverride соседней клетки (как
        /// BrushSetWater/"Сила: вода" в редакторе), и перезапекает ТОЛЬКО маленький прямоугольник
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

        /// <summary>Регрессия на живой краш ArgumentOutOfRangeException (Sea/Lake нет плоского слота):
        /// в плоском режиме пиксель может быть отнесён к суше (IsLand=true), а его ближайшая клетка -
        /// водной; тогда FlatBiomeColor обязан подменить водный биом на Coast, иначе GetSlotColor
        /// падает. На реальной карте это тонкая каёмка у сглаженного берега на вогнутых участках.
        /// Фикстура воспроизводит условие проще: 3x3, центр - озеро, 8 вокруг - суша, БЕЗ океана у края
        /// карты. even-odd заливка стартует "не-суша" от левого края строки, поэтому окружающая суша
        /// помечается водой, а внутренность озера - сушей (IsLand=true), при этом её ближайшая клетка -
        /// центральное озеро (вода). Достаточно, чтобы пройти по крашащему пути. Тест: бак не бросает.</summary>
        [ContextMenu("Self-Test: Flat Fill Coastal Fringe No Crash")]
        public void SelfTestFlatFillCoastalFringeNoCrash()
        {
            var fixtureCells = new List<VoronoiCell>();
            int nextId = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    bool isCenter = c == 1 && r == 1;
                    var cell = new VoronoiCell(nextId++, new System.Numerics.Vector2(c, r))
                    {
                        Biome = isCenter ? Biome.Lake : Biome.Grassland,
                        Height = isCenter ? 0f : 0.5f,
                        IsOcean = false,
                    };
                    cell.Polygon = SquarePolygon(cell.Site, 0.5f);
                    fixtureCells.Add(cell);
                }
            var fixtureById = fixtureCells.ToDictionary(cc => cc.Id);
            var corners = WorldGen.Generation.CornerGraphBuilder.Build(fixtureCells);
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(fixtureCells, 1f);

            var config = new WorldGen.Rendering.MapRaster.MapRasterConfig
            {
                TexWidth = 30, TexHeight = 30, MapWidth = 3f, MapHeight = 3f, Seed = 1,
                SmoothBorders = true, CoastlineSmoothness = 3, CoastlineGlowWidth = 8,
                FlatRegionFill = true, ElevationBands = 5, ElevationBandContrast = 40f,
                Theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight,
                ColdLight = 58f, RegionVariation = 0f, Darkness = 0f, SmoothRadius = 1.5f,
                ReliefStrength = 3f, ReliefLightAzimuth = 315f, ReliefAmbient = 0.5f,
                ShowBiomeLayer = true, ShowReliefLayer = true,
                HardModeColor = GetColorForCell, WaterDepth01 = _ => 0f,
            };

            var buffers = WorldGen.Rendering.MapRaster.MapRasterizer.CreateEmptyBuffers(30, 30);
            var tex = new Texture2D(30, 30, TextureFormat.RGBA32, false);
            bool threw = false;
            try
            {
                WorldGen.Rendering.MapRaster.MapRasterizer.RebakeRegion(fixtureCells, fixtureById, lookup, corners, MapDisplayMode.Combined, config, tex, buffers, 0, 0, 30, 30);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Destroy(tex);
            Debug.Log(!threw
                ? "Self-Test Flat Fill Coastal Fringe No Crash: PASS"
                : "Self-Test Flat Fill Coastal Fringe No Crash: FAIL (плоская раскраска упала на пикселе-суше с водной ближайшей клеткой - Sea/Lake нет плоского слота)");
        }

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

        GenerationParams BuildGenerationParams()
        {
            // mapWidth/mapHeight — ПРОИЗВОДНЫЕ от стабильного continentWidth/Height + oceanPadding,
            // пересчитываются здесь перед КАЖДОЙ генерацией. Рендер/камера/GPU-текстура кадрируют
            // именно mapWidth/mapHeight (полный домен) - см. PositionCameraOverMap/BuildQuadMesh.
            // Читать continentWidth (не mapWidth!) как вход - иначе домен рос бы от генерации к генерации.
            mapWidth = continentWidth * (1f + 2f * oceanPadding);
            mapHeight = continentHeight * (1f + 2f * oceanPadding);
            return new GenerationParams
            {
                Seed = seed,
                Width = mapWidth,
                Height = mapHeight,
                ContinentWidth = continentWidth,
                ContinentHeight = continentHeight,
                OceanPadding = oceanPadding,
                MinPointDistance = minPointDistance,
                LloydRelaxIterations = lloydIterations,
                NumberOfRegions = numberOfRegions,
                FalloffPower = falloffPower,
                InnerRadius = innerRadius,
                CoastRoughness = (float)(new System.Random(seed + 6000).NextDouble() * 0.5),
                ContinentCenterJitter = continentCenterJitter,
                SeaLevel = seaLevel,
                MinLakeSize = minLakeSize,
                ElevationCoastWeight = elevationCoastWeight,
                ElevationNoiseWeight = elevationNoiseWeight,
                ElevationNoiseFrequency = elevationNoiseFrequency,
                ElevationNoiseOctaves = elevationNoiseOctaves,
                ElevationContrast = elevationContrast,
                MoistureFalloffDistance = moistureFalloffDistance,
                NumberOfMoistureEpicenters = numberOfMoistureEpicenters,
                MoistureEpicenterMinRadius = moistureEpicenterMinRadius,
                MoistureEpicenterMaxRadius = moistureEpicenterMaxRadius,
                MoistureEpicenterMinDelta = moistureEpicenterMinDelta,
                MoistureEpicenterMaxDelta = moistureEpicenterMaxDelta,
                NumberOfRivers = numberOfRivers,
                EnableRivers = enableRivers,
                RiverMinStartElevation = riverMinStartElevation,
                NumberOfTemperatureEpicenters = numberOfTemperatureEpicenters,
                EpicenterMinRadius = epicenterMinRadius,
                EpicenterMaxRadius = epicenterMaxRadius,
                BaseTemperature = baseTemperature,
                ElevationTempDrop = elevationTempDrop
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
            // Same fit factor MapCameraController uses for "По размеру" (100%), so the map opens
            // at that reference zoom with margin around it rather than jammed edge-to-edge.
            targetCamera.orthographicSize = maxSide * MapCameraController.FitFactor;

            if (targetCamera.farClipPlane < maxSide * 3f)
                targetCamera.farClipPlane = maxSide * 3f;

            // Фон = цвет глубокого океана: вода на краю текстуры бесшовно перетекает в фон редактора,
            // и изрезанный материк читается как "суша в бескрайнем море" (см. A1 borderWaterMargin).
            if (targetCamera != null)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                Color32 abyss = WorldGen.Rendering.MapRaster.MapPalette.GetSlotColor(paletteTheme, WorldGen.Rendering.MapRaster.PaletteSlot.Abyss);
                targetCamera.backgroundColor = abyss;
            }
        }

        public void BuildMesh(List<VoronoiCell> sourceCells)
        {
            cells = sourceCells;
            RebuildSpatialIndex();
            BuildQuadMesh();
            RebakeAll();
        }

        /// <summary>cellById/oceanDistanceFromLand/nearestLookup всегда пересчитываются вместе -
        /// общий шаг для BuildMesh (генерация/ContextMenu) и PrepareLoadFromCells (генерация через
        /// прогресс-экран, см. MapScreenController).</summary>
        void RebuildSpatialIndex()
        {
            cellById = new Dictionary<int, VoronoiCell>(cells.Count);
            foreach (var c in cells) cellById[c.Id] = c;
            oceanDistanceFromLand = ComputeOceanDistanceFromLand();
            nearestLookup = new NearestCellLookup(cells, minPointDistance);
        }

        void EnsureDecorationRenderer()
        {
            if (decorationCatalog == null)
                decorationCatalog = WorldGen.Rendering.Decorations.DecorationCatalog.BuildPlaceholder();
            if (decorationRenderer == null)
            {
                var go = new GameObject("Decorations");
                go.transform.SetParent(transform, false); // локальные коорд. карты
                decorationRenderer = go.AddComponent<WorldGen.Rendering.Decorations.DecorationRenderer>();
                decorationRenderer.Init(decorationCatalog);
            }
        }

        /// <summary>Полная перерасстановка декораций из текущих клеток/сида/темы.</summary>
        public void RebuildDecorations()
        {
            EnsureDecorationRenderer();
            if (cells == null || nearestLookup == null) return;
            decorationInstances = WorldGen.Rendering.Decorations.DecorationPlacer.Place(
                cells, nearestLookup, seed, mapWidth, mapHeight, decorationConfig, paletteTheme);
            decorationRenderer.SetInstances(decorationInstances);
            decorationRenderer.Visible = decorationConfig.enabled;
        }

        /// <summary>Rect-scoped обновление: выкинуть инстансы в rect, дорасставить, ре-сортировать.</summary>
        public void RefreshDecorationsRect(Rect worldRect)
        {
            if (decorationInstances == null) { RebuildDecorations(); return; }
            EnsureDecorationRenderer();
            decorationInstances.RemoveAll(d => worldRect.Contains(d.worldPos));
            WorldGen.Rendering.Decorations.DecorationPlacer.PlaceRect(
                decorationInstances, nearestLookup, seed, mapWidth, mapHeight, decorationConfig, paletteTheme, worldRect);
            decorationInstances.Sort((a, b) => b.sortZ.CompareTo(a.sortZ)); // descending, see DecorationPlacer.Place
            decorationRenderer.SetInstances(decorationInstances);
        }

        /// <summary>Один плоский квад mapWidth×mapHeight в плоскости XZ - заменяет тысячи
        /// клеточных fan-мешей. Цвет приходит из текстуры (см. RebakeAll), не из vertex color.
        /// Sprites/Default не culлит грани, так что winding order (0,1,2 vs 0,2,1) здесь не важен -
        /// сравни со старым fan-мешем, где неверный winding "смотрел вниз" и требовал разворота.</summary>
        void BuildQuadMesh()
        {
            var mesh = new Mesh();
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(mapWidth, 0f, 0f),
                new Vector3(mapWidth, 0f, mapHeight),
                new Vector3(0f, 0f, mapHeight),
            };
            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            // Обмотка так, чтобы лицо (нормаль) смотрело ВВЕРХ (+Y) — к камере сверху. Иначе MeshCollider.Raycast
            // бьёт в изнанку квада, а Unity по умолчанию (Physics.queriesHitBackfaces=false) back-face не ловит,
            // и весь хит-тест карты (кисть/выделение/POI через TryGetSiteHitPoint/GetCellUnderRay) молча не работает.
            // Материал карты двусторонний (Sprites/Default, Cull Off), поэтому на отрисовку обмотка не влияет.
            var triangles = new[] { 0, 2, 1, 0, 3, 2 };

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = null; // обязательно сбросить перед переприсваиванием - иначе Unity не обновит коллизию на месте
            meshCollider.sharedMesh = mesh;
        }

        /// <summary>
        /// Срабатывает при смене режима отображения ИЛИ после новой генерации/перегенерации -
        /// то есть в любой момент, когда легенде имеет смысл перестроить свой список записей.
        /// </summary>
        public event System.Action OnDisplayChanged;

        public void SetDisplayMode(MapDisplayMode mode)
        {
            displayMode = mode;
            if (cells != null) RebakeAll();
            bool combined = mode == MapDisplayMode.Combined;
            if (regionBorderObject != null) regionBorderObject.SetActive(combined && showRegionBordersLayer);
            if (coastlineObject != null) coastlineObject.SetActive(ShouldShowCoastlineRibbon());
            OnDisplayChanged?.Invoke();
        }

        public void SetShowBiomeLayer(bool on)
        {
            showBiomeLayer = on;
            if (cells != null)
            {
                // GPU: мгновенно через uniform; CPU: полный перезапек (фолбэк).
                if (useGpuRenderer && gpuRenderer != null) gpuRenderer.SetLayers(showBiomeLayer, showReliefLayer, showCoastlineLayer);
                else RebakeAll();
            }
            OnDisplayChanged?.Invoke();
        }

        public void SetShowReliefLayer(bool on)
        {
            showReliefLayer = on;
            if (cells != null)
            {
                if (useGpuRenderer && gpuRenderer != null) gpuRenderer.SetLayers(showBiomeLayer, showReliefLayer, showCoastlineLayer);
                else RebakeAll();
            }
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
                coastlineObject.SetActive(ShouldShowCoastlineRibbon());
            // GPU: мгновенно гейтим береговую линию в шейдере (_ShowCoast), как биом/рельеф.
            if (cells != null && useGpuRenderer && gpuRenderer != null)
                gpuRenderer.SetLayers(showBiomeLayer, showReliefLayer, showCoastlineLayer);
            OnDisplayChanged?.Invoke();
        }

        public void SetShowDecorations(bool on)
        {
            decorationConfig.enabled = on;
            if (decorationRenderer != null) decorationRenderer.Visible = on;
            EnsureDecorationRenderer();
            if (on && (decorationInstances == null || decorationInstances.Count == 0)) RebuildDecorations();
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

        void RebakeAll()
        {
            if (cells == null) return;
            ComputeTexSize(out texWidth, out texHeight);

            // GPU-путь: карта рисуется шейдером MapTerrain из cell-id + атрибутов (Task 4+).
            // CPU-запечка ниже - фолбэк при useGpuRenderer=false.
            if (useGpuRenderer && gpuRenderer != null)
            {
                gpuRenderer.SetContourParams(coastlineSmoothness, borderRoundness * minPointDistance);
                gpuRenderer.BuildAll(cells, nearestLookup, texWidth, texHeight, mapWidth, mapHeight, paletteTheme, corners);
                gpuRenderer.SetLayers(showBiomeLayer, showReliefLayer, showCoastlineLayer);
                gpuRenderer.SetBeachParams(beachWidth, beachStrength, beachHardness, beachColor);
                return;
            }

            var config = BuildRasterConfig();
            var oldTexture = rasterTexture;
            rasterTexture = MapRasterizer.Bake(cells, cellById, nearestLookup, corners, displayMode, config, out rasterBuffers);
            if (oldTexture != null) Destroy(oldTexture);
            EnsureRasterMaterial();
            rasterMaterial.mainTexture = rasterTexture;
        }

        void RebakeRegion(IEnumerable<VoronoiCell> touchedCells)
        {
            if (cells == null) return;
            if (rasterTexture == null) { RebakeAll(); return; }

            ComputeTouchedPixelRect(touchedCells, out int rx, out int ry, out int rw, out int rh);
            if (rw <= 0 || rh <= 0) return;

            var config = BuildRasterConfig();
            // Кисть не двигает сайты Вороного - карта cellId в rect уже верна с прошлого полного
            // запека, поэтому recomputeCellId: false (см. BakeFieldsRect, убирает попиксельный FindNearest).
            MapRasterizer.RebakeRegion(cells, cellById, nearestLookup, corners, displayMode, config, rasterTexture, rasterBuffers, rx, ry, rw, rh, recomputeCellId: false);
        }

        /// <summary>Перезапекает текстуру только вокруг клеток, затронутых кистью в последнем
        /// стемпе - вместо полного RebakeAll на каждое изменение (см. BrushToolController.ApplyStamp).
        /// Закрывает roadmap-пункт "кисть перекрашивает весь меш на каждое движение".</summary>
        public void RebakeAffectedCells(IEnumerable<VoronoiCell> touchedCells)
        {
            // GPU: правка = обновить атрибуты изменённых клеток (перезалить крошечную текстуру) -
            // бесплатно при любом размере кисти. CPU: частичный перезапек rect (фолбэк).
            if (useGpuRenderer && gpuRenderer != null) { gpuRenderer.UpdateCells(touchedCells); return; }
            RebakeRegion(touchedCells);
        }

        /// <summary>Обновить отрисовку после изменения ДАННЫХ клеток без смены геометрии (undo,
        /// climate/biome override многих клеток): GPU - перезаливает атрибуты (UpdateCells) и пере-печёт
        /// сглаженные label'ы затронутых клеток (FinalizeLabels), для массовых правок это НЕ дёшево;
        /// CPU - полный перезапек. Геометрия (cell-id) не трогается - сайты Вороного неподвижны.</summary>
        void RefreshAfterCellDataChange()
        {
            if (useGpuRenderer && gpuRenderer != null) { gpuRenderer.UpdateCells(cells); gpuRenderer.FinalizeCoast(); gpuRenderer.FinalizeLabels(); }
            else RebakeAll();
            RebuildDecorations();
        }

        /// <summary>Самый дешёвый путь при смене только darkness (подпроект 6 добавит слайдер) -
        /// заново применяет только финальный проход виньетки поверх уже готовых PreVignette-пикселей,
        /// не пересчитывая блендинг/тонировку/рельеф/зерно заново. Без вызывающей UI в этом
        /// подпроекте, но нужен уже сейчас как часть публичного API RebakeAll/RebakeRegion пары.</summary>
        public void ReapplyDarkness()
        {
            if (rasterTexture == null || rasterBuffers == null) return;
            MapRasterizer.ReapplyDarkness(rasterTexture, rasterBuffers, darkness);
        }

        void ComputeTexSize(out int w, out int h)
        {
            if (mapWidth >= mapHeight)
            {
                w = Mathf.Max(4, rasterLongSide);
                h = Mathf.Max(4, Mathf.RoundToInt(rasterLongSide * (mapHeight / mapWidth)));
            }
            else
            {
                h = Mathf.Max(4, rasterLongSide);
                w = Mathf.Max(4, Mathf.RoundToInt(rasterLongSide * (mapWidth / mapHeight)));
            }
        }

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
                CoastlineGlowWidth = coastlineGlowWidth,
                FlatRegionFill = flatRegionFill,
                ElevationBands = elevationBands,
                ElevationBandContrast = elevationBandContrast,
                SmoothRegionBorders = smoothRegionBorders,
                BorderRoundnessDistance = borderRoundness * minPointDistance,
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

        /// <summary>Bounding rect (в пикселях текстуры) клеток, затронутых кистью, расширенный на
        /// smoothRadius (протекание блендинга из соседних неизменённых клеток) плюс coastlineGlowWidth
        /// (ореол берега тянется на столько пикселей от суши, поэтому его полоса тоже должна
        /// пересчитаться при правке берега) плюс borderRoundness * minPointDistance (сглаженная/
        /// прореженная граница семейств биомов/полос высоты может сдвинуться на эту величину при
        /// правке кистью) - все эти пиксели рядом с границей должны пересчитаться.</summary>
        void ComputeTouchedPixelRect(IEnumerable<VoronoiCell> touchedCells, out int rx, out int ry, out int rw, out int rh)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var cell in touchedCells)
            {
                foreach (var p in cell.Polygon)
                {
                    any = true;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minZ) minZ = p.Y;
                    if (p.Y > maxZ) maxZ = p.Y;
                }
            }

            if (!any) { rx = ry = rw = rh = 0; return; }

            // Отступ = smoothRadius (протекание блендинга) + coastlineGlowWidth (ореол берега тянется
            // на столько пикселей от суши, поэтому пиксели в этой полосе должны пересчитаться при
            // правке берега кистью) + borderRoundness * minPointDistance (сглаженная/прореженная
            // граница семейств биомов/полос высоты может сдвинуться на эту величину при правке
            // кистью). glowWidth в пикселях -> мировые единицы через worldPerPixel
            // (mapWidth/texWidth). Текстура сохраняет аспект (см. ComputeTexSize), поэтому один
            // множитель по X верен и для Y с точностью до RoundToInt (<0.1%, поглощается Floor/Ceil).
            float pad = minPointDistance * 1.5f + coastlineGlowWidth * (mapWidth / texWidth) + borderRoundness * minPointDistance;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;

            int px0 = Mathf.Clamp(Mathf.FloorToInt(minX / mapWidth * texWidth), 0, texWidth - 1);
            int px1 = Mathf.Clamp(Mathf.CeilToInt(maxX / mapWidth * texWidth), 0, texWidth - 1);
            int py0 = Mathf.Clamp(Mathf.FloorToInt(minZ / mapHeight * texHeight), 0, texHeight - 1);
            int py1 = Mathf.Clamp(Mathf.CeilToInt(maxZ / mapHeight * texHeight), 0, texHeight - 1);

            rx = px0; ry = py0; rw = px1 - px0 + 1; rh = py1 - py0 + 1;
        }

        /// <summary>Чанковый (по строкам текстуры) запек для экрана прогресса генерации - RebakeRegion
        /// уже умеет пересчитывать произвольный прямоугольник "с нуля" (для кисти), здесь он же
        /// вызывается построчными полосами с yield между ними, чтобы UI не подвисал (см.
        /// MapScreenController.RunGeneration). Должен вызываться ПОСЛЕ PrepareLoadFromCells.</summary>
        public System.Collections.IEnumerator RebakeAllStepped(System.Action<float> onProgress)
        {
            if (cells == null) yield break;
            ComputeTexSize(out texWidth, out texHeight);

            // GPU-путь: генерация рисуется шейдером; тяжёлый cell-id бэйк идёт чанково с прогрессом.
            if (useGpuRenderer && gpuRenderer != null)
            {
                gpuRenderer.SetContourParams(coastlineSmoothness, borderRoundness * minPointDistance);
                var e = gpuRenderer.BuildAllStepped(cells, nearestLookup, texWidth, texHeight, mapWidth, mapHeight, paletteTheme, corners, onProgress);
                while (e.MoveNext()) yield return e.Current;
                gpuRenderer.SetLayers(showBiomeLayer, showReliefLayer, showCoastlineLayer);
                gpuRenderer.SetBeachParams(beachWidth, beachStrength, beachHardness, beachColor);
                yield break;
            }

            if (rasterTexture != null) Destroy(rasterTexture);
            rasterTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rasterBuffers = MapRasterizer.CreateEmptyBuffers(texWidth, texHeight);
            EnsureRasterMaterial();
            rasterMaterial.mainTexture = rasterTexture;

            var config = BuildRasterConfig();

            // Сначала cellId + блендированные поля (elevation/temperature/цвет семейства) для ВСЕГО
            // изображения разом - без этого шага раскраска по чанкам ниже читала бы ещё не запечённые
            // (нулевые) значения соседней строки на границе каждого чанка (градиент рельефа и проверка
            // берега смотрят на ±1 пиксель), что давало видимый горизонтальный артефакт каждые chunkRows
            // строк. FindWithinRadius внутри этого прохода - геометрический запрос по NearestCellLookup,
            // не по буферу, поэтому сам этот проход корректен независимо от порядка/чанкования.
            MapRasterizer.BakeFieldsRect(cells, cellById, nearestLookup, corners, displayMode, config, rasterBuffers, 0, 0, texWidth, texHeight);
            yield return null;

            const int chunkRows = 64;
            for (int y0 = 0; y0 < texHeight; y0 += chunkRows)
            {
                int rh = Mathf.Min(chunkRows, texHeight - y0);
                MapRasterizer.ColorAndVignetteRect(cellById, displayMode, config, rasterTexture, rasterBuffers, 0, y0, texWidth, rh);
                onProgress?.Invoke((y0 + rh) / (float)texHeight);
                yield return null;
            }
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
            // прозрачная сфера показывает Radius (силу/ширину влияния); влияние теперь простирается
            // и за сферу, просто слабее (мягкий глобальный спад, без hard cutoff).
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

        /// <summary>Возвращает клетку под курсором/прицелом по физическому рейкасту в коллайдер карты -
        /// через UV попадания на квад (RaycastHit.textureCoord) переведённые в пиксель cellId-буфера,
        /// а не через индекс треугольника (квад больше не хранит per-cell геометрию, см. BuildQuadMesh).</summary>
        public VoronoiCell GetCellUnderRay(Ray ray, float maxDistance = 2000f)
        {
            if (cells == null || nearestLookup == null) return null;
            if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                // Точка попадания → ближайшая клетка (та же, что в cell-id текстуре - её пекут из
                // того же FindNearest). Работает и в GPU-режиме, где CPU-буфера rasterBuffers нет.
                Vector3 local = transform.InverseTransformPoint(hit.point);
                return nearestLookup.FindNearest(new System.Numerics.Vector2(local.x, local.z));
            }
            return null;
        }

        /// <summary>
        /// Точка попадания луча по коллайдеру карты, переведённая в координаты Site (x = Site.X,
        /// y = Site.Y — плоскость карты XZ). Возвращает false, если луч не задел карту. Используется
        /// radius-кистью, чтобы запросить все клетки в круге/квадрате вокруг курсора.
        /// </summary>
        public bool TryGetSiteHitPoint(Ray ray, out Vector2 sitePoint, float maxDistance = 2000f)
        {
            sitePoint = Vector2.zero;
            if (cells == null) return false;
            if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                Vector3 local = transform.InverseTransformPoint(hit.point);
                sitePoint = new Vector2(local.x, local.z);
                return true;
            }
            return false;
        }
    }
}
