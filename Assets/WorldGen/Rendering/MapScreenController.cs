using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns app-flow POLICY: computes which mutually-exclusive screen should be visible from app
    /// state (map exists? generating? editing a POI?) and delegates the actual show/hide to a
    /// ScreenSwitcher (the MECHANISM). Screens: Generation / Progress / MapEditor / PoiEditor.
    /// Also runs the world-generation coroutine.
    /// </summary>
    public class MapScreenController : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;
        public GenerationScreenUI generationScreen;
        public GenerationProgressUI progressScreen;
        public GameObject mapEditorPanelGO;
        public GameObject mapLegendUiGO;
        [Tooltip("Тулбар вкладок — прячет свою шапку + докнутые панели вне экрана карты (они сиблинги, не дети mapEditorPanelGO).")]
        public MapToolbarUI mapToolbar;

        [Header("POI editor screen")]
        public GameObject poiEditorScreenGO;
        public PoiEditorScreen poiEditorScreen;
        public PoiInfoPopup poiInfoPopup;

        [Header("Dungeon editor screen")]
        public GameObject dungeonEditorScreenGO;
        public DungeonEditorScreen dungeonEditorScreen;
        public DungeonManager dungeonManager;

        Coroutine activeGeneration;
        PoiData editingPoi;
        DungeonData editingDungeon;
        ScreenSwitcher switcher;

        void Awake()
        {
            progressScreen.OnCancelRequested += CancelGeneration;
        }

        void Start()
        {
            // Build the switcher FIRST — before any event subscription — so the invariant
            // "the switcher exists before any handler that could call RefreshScreenState runs"
            // is structural, not merely incidental to statement order.
            switcher = new ScreenSwitcher(
                new Dictionary<AppScreen, GameObject[]>
                {
                    { AppScreen.Generation, new[] { generationScreen.gameObject } },
                    { AppScreen.Progress,   new[] { progressScreen.gameObject } },
                    { AppScreen.MapEditor,  new[] { mapEditorPanelGO, mapLegendUiGO } },
                    { AppScreen.PoiEditor,  new[] { poiEditorScreenGO } },
                    { AppScreen.Dungeon,   new[] { dungeonEditorScreenGO } },
                },
                screen => { if (mapToolbar != null) mapToolbar.SetChromeVisible(screen == AppScreen.MapEditor); });

            mapRenderer.OnWorldRegenerated += OnWorldRegenerated;
            if (poiEditorScreen != null) poiEditorScreen.OnCloseRequested = ClosePoiEditor;
            if (poiInfoPopup != null) poiInfoPopup.OnEditRequested = OpenPoiEditor;
            if (poiEditorScreen != null) poiEditorScreen.OnOpenDungeonRequested = OpenDungeonEditor;
            if (dungeonEditorScreen != null) dungeonEditorScreen.OnCloseRequested = CloseDungeonEditor;

            RefreshScreenState();
        }

        void OnWorldRegenerated()
        {
            editingPoi = null; // a fresh world drops any open POI editor
            editingDungeon = null; // a fresh world drops any open dungeon editor
            RefreshScreenState();
        }

        /// <summary>Opens the full-screen POI editor for a point (single source of truth for both the
        /// info-popup «Редактировать» button and double-click). Hides the popup + map view.</summary>
        public void OpenPoiEditor(PoiData poi)
        {
            if (poi == null) return;
            editingPoi = poi;
            if (poiInfoPopup != null) poiInfoPopup.Hide();
            if (poiEditorScreen != null) poiEditorScreen.Bind(poi);
            RefreshScreenState();
        }

        /// <summary>Closes the POI editor and returns to the world map.</summary>
        public void ClosePoiEditor()
        {
            editingPoi = null;
            RefreshScreenState();
        }

        /// <summary>Opens the cave-dungeon editor for a POI (get-or-create its dungeon). Returns to the
        /// POI editor on close (editingPoi stays set), not the world map.</summary>
        public void OpenDungeonEditor(PoiData poi)
        {
            if (poi == null || dungeonManager == null) return;
            editingDungeon = dungeonManager.GetOrCreateForPoi(poi.Id);
            if (dungeonEditorScreen != null) dungeonEditorScreen.Bind(editingDungeon);
            RefreshScreenState();
        }

        public void CloseDungeonEditor()
        {
            editingDungeon = null;   // editingPoi is still set → DesiredScreen returns PoiEditor
            RefreshScreenState();
        }

        void RefreshScreenState()
        {
            switcher.Show(DesiredScreen());
        }

        /// <summary>The single truth table mapping app state to the one visible screen. Reproduces
        /// the old hasMap/generating/editorOpen/mapReady booleans exactly. The switcher then hides
        /// every other screen's members (incl. the toolbar chrome via the after-show hook), so
        /// nothing can leak by omission.</summary>
        AppScreen DesiredScreen()
        {
            bool hasMap = mapRenderer.Cells != null;
            bool generating = activeGeneration != null;
            if (generating) return AppScreen.Progress;
            if (!hasMap) return AppScreen.Generation;
            if (editingDungeon != null) return AppScreen.Dungeon;
            if (editingPoi != null) return AppScreen.PoiEditor;
            return AppScreen.MapEditor;
        }

        public void StartGeneration(WorldGen.Rendering.GenerationRequest uiParams)
        {
            if (activeGeneration != null) return;

            ApplyUiParamsToRenderer(uiParams);
            var genParams = BuildGenerationParams(uiParams);

            RefreshScreenState(); // hasMap is still false here, but activeGeneration isn't set yet either -- set it first
            activeGeneration = StartCoroutine(RunGeneration(genParams));
        }

        void ApplyUiParamsToRenderer(WorldGen.Rendering.GenerationRequest uiParams)
        {
            mapRenderer.seed = GenerationScreenUI.StableSeedHash(uiParams.SeedText);

            switch (uiParams.Size)
            {
                case MapSizePreset.Small:  mapRenderer.continentWidth = 350f; mapRenderer.continentHeight = 350f; break;
                case MapSizePreset.Medium: mapRenderer.continentWidth = 500f; mapRenderer.continentHeight = 500f; break;
                case MapSizePreset.Large:  mapRenderer.continentWidth = 700f; mapRenderer.continentHeight = 700f; break;
            }

            switch (uiParams.Shape)
            {
                case LandShapePreset.Continent:   mapRenderer.falloffPower = 3.0f; mapRenderer.innerRadius = 0.6f; mapRenderer.seaLevel = 0.30f; break;
                case LandShapePreset.Archipelago:  mapRenderer.falloffPower = 1.8f; mapRenderer.innerRadius = 0.3f; mapRenderer.seaLevel = 0.45f; break;
                case LandShapePreset.Islands:       mapRenderer.falloffPower = 1.5f; mapRenderer.innerRadius = 0.1f; mapRenderer.seaLevel = 0.55f; break;
            }

            mapRenderer.numberOfRegions = uiParams.RegionCount;
        }

        GenerationParams BuildGenerationParams(WorldGen.Rendering.GenerationRequest uiParams)
        {
            // Mirrors WorldMapRenderer.BuildGenerationParams()'s field-by-field copy, since
            // GenerateWorldStepped (unlike GenerateAndRender) is called directly here, not
            // through WorldMapRenderer. mapWidth/mapHeight are DERIVED from the stable
            // continentWidth/Height + oceanPadding, recomputed here before every generation -
            // see WorldMapRenderer.BuildGenerationParams for the runaway-safety rationale.
            mapRenderer.mapWidth = mapRenderer.continentWidth * (1f + 2f * mapRenderer.oceanPadding);
            mapRenderer.mapHeight = mapRenderer.continentHeight * (1f + 2f * mapRenderer.oceanPadding);
            return new GenerationParams
            {
                Seed = mapRenderer.seed,
                Width = mapRenderer.mapWidth,
                Height = mapRenderer.mapHeight,
                ContinentWidth = mapRenderer.continentWidth,
                ContinentHeight = mapRenderer.continentHeight,
                OceanPadding = mapRenderer.oceanPadding,
                MinPointDistance = mapRenderer.minPointDistance,
                LloydRelaxIterations = mapRenderer.lloydIterations,
                NumberOfRegions = mapRenderer.numberOfRegions,
                FalloffPower = mapRenderer.falloffPower,
                InnerRadius = mapRenderer.innerRadius,
                CoastRoughness = (float)(new System.Random(mapRenderer.seed + 6000).NextDouble() * 0.5),
                ContinentCenterJitter = mapRenderer.continentCenterJitter,
                SeaLevel = mapRenderer.seaLevel,
                MinLakeSize = mapRenderer.minLakeSize,
                ElevationCoastWeight = mapRenderer.elevationCoastWeight,
                ElevationNoiseWeight = mapRenderer.elevationNoiseWeight,
                ElevationNoiseFrequency = mapRenderer.elevationNoiseFrequency,
                ElevationNoiseOctaves = mapRenderer.elevationNoiseOctaves,
                ElevationContrast = mapRenderer.elevationContrast,
                MoistureFalloffDistance = mapRenderer.moistureFalloffDistance,
                NumberOfTemperatureEpicenters = mapRenderer.numberOfTemperatureEpicenters,
                EpicenterMinRadius = mapRenderer.epicenterMinRadius,
                EpicenterMaxRadius = mapRenderer.epicenterMaxRadius,
                BaseTemperature = mapRenderer.baseTemperature,
                ElevationTempDrop = mapRenderer.elevationTempDrop,
                NumberOfMoistureEpicenters = mapRenderer.numberOfMoistureEpicenters,
                MoistureEpicenterMinRadius = mapRenderer.moistureEpicenterMinRadius,
                MoistureEpicenterMaxRadius = mapRenderer.moistureEpicenterMaxRadius,
                MoistureEpicenterMinDelta = mapRenderer.moistureEpicenterMinDelta,
                MoistureEpicenterMaxDelta = mapRenderer.moistureEpicenterMaxDelta,
                EnableRivers = mapRenderer.enableRivers,
                NumberOfRivers = mapRenderer.numberOfRivers,
                RiverMinStartElevation = mapRenderer.riverMinStartElevation,
            };
        }

        System.Collections.IEnumerator RunGeneration(GenerationParams genParams)
        {
            RefreshScreenStateForGenerating();

            List<VoronoiCell> generatedCells = null;
            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) => generatedCells = cells);

            mapRenderer.PrepareLoadFromCells(generatedCells, genParams);
            yield return mapRenderer.RebakeAllStepped(bakeFrac => progressScreen.SetStep("Отрисовка карты", (5f + bakeFrac) / 6f));
            mapRenderer.FinishLoadFromCells();

            progressScreen.SetStep("Готово", 1f);
            activeGeneration = null;
            RefreshScreenState();
        }

        // During generation START, activeGeneration hasn't been assigned yet (StartCoroutine runs
        // RunGeneration's first segment synchronously, before the handle is stored), so DesiredScreen()
        // would not yet see "generating". Show Progress directly. Unlike the old hand-rolled version,
        // the switcher also hides the toolbar chrome (via the hook), so it can't linger on Progress
        // during a regenerate.
        void RefreshScreenStateForGenerating()
        {
            switcher.Show(AppScreen.Progress);
        }

        void CancelGeneration()
        {
            if (activeGeneration == null) return;
            StopCoroutine(activeGeneration);
            activeGeneration = null;
            RefreshScreenState();
        }
    }
}
