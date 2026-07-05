using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Switches between three mutually-exclusive screens based on whether a map exists yet
    /// and whether generation is in progress: GenerationScreenUI (no map) / GenerationProgressUI
    /// (generating) / the existing MapEditorPanel+MapLegendUI pair (map ready).
    /// </summary>
    public class MapScreenController : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;
        public GenerationScreenUI generationScreen;
        public GenerationProgressUI progressScreen;
        public GameObject mapEditorPanelGO;
        public GameObject mapLegendUiGO;

        Coroutine activeGeneration;

        void Awake()
        {
            progressScreen.OnCancelRequested += CancelGeneration;
        }

        void Start()
        {
            mapRenderer.OnWorldRegenerated += RefreshScreenState;
            RefreshScreenState();
        }

        void RefreshScreenState()
        {
            bool hasMap = mapRenderer.Cells != null;
            bool generating = activeGeneration != null;

            generationScreen.gameObject.SetActive(!hasMap && !generating);
            progressScreen.gameObject.SetActive(generating);
            mapEditorPanelGO.SetActive(hasMap && !generating);
            mapLegendUiGO.SetActive(hasMap && !generating);
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
                case MapSizePreset.Small:  mapRenderer.mapWidth = 350f; mapRenderer.mapHeight = 350f; break;
                case MapSizePreset.Medium: mapRenderer.mapWidth = 500f; mapRenderer.mapHeight = 500f; break;
                case MapSizePreset.Large:  mapRenderer.mapWidth = 700f; mapRenderer.mapHeight = 700f; break;
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
            // through WorldMapRenderer.
            return new GenerationParams
            {
                Seed = mapRenderer.seed,
                Width = mapRenderer.mapWidth,
                Height = mapRenderer.mapHeight,
                MinPointDistance = mapRenderer.minPointDistance,
                LloydRelaxIterations = mapRenderer.lloydIterations,
                NumberOfRegions = mapRenderer.numberOfRegions,
                FalloffPower = mapRenderer.falloffPower,
                InnerRadius = mapRenderer.innerRadius,
                SeaLevel = mapRenderer.seaLevel,
                MinLakeSize = mapRenderer.minLakeSize,
                ElevationCoastWeight = mapRenderer.elevationCoastWeight,
                ElevationNoiseWeight = mapRenderer.elevationNoiseWeight,
                ElevationNoiseFrequency = mapRenderer.elevationNoiseFrequency,
                ElevationNoiseOctaves = mapRenderer.elevationNoiseOctaves,
                MoistureFalloffDistance = mapRenderer.moistureFalloffDistance,
                BeachElevationThreshold = mapRenderer.beachElevationThreshold,
                NumberOfTemperatureEpicenters = mapRenderer.numberOfTemperatureEpicenters,
                EpicenterMinRadius = mapRenderer.epicenterMinRadius,
                EpicenterMaxRadius = mapRenderer.epicenterMaxRadius,
                BaseTemperature = mapRenderer.baseTemperature,
                HeightCoolingFactor = mapRenderer.heightCoolingFactor,
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

            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) =>
                {
                    mapRenderer.LoadFromCells(cells, genParams);
                    activeGeneration = null;
                    RefreshScreenState();
                });
        }

        void RefreshScreenStateForGenerating()
        {
            generationScreen.gameObject.SetActive(false);
            progressScreen.gameObject.SetActive(true);
            mapEditorPanelGO.SetActive(false);
            mapLegendUiGO.SetActive(false);
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
