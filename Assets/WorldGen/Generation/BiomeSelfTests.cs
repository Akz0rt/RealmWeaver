using UnityEngine;

namespace WorldGen.Generation
{
    /// <summary>Editor-only [ContextMenu] self-tests for the biome catalog, matrix, cooling and
    /// landform. Attach to any GameObject and run each item from the component's context menu.
    /// (Project convention: no CLI test runner — self-tests Debug.Log PASS/FAIL.)</summary>
    public class BiomeSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Landform")]
        public void SelfTestLandform()
        {
            bool ok = true;
            ok &= LandformClassifier.Of(0.00f) == Landform.Plain;
            ok &= LandformClassifier.Of(0.10f) == Landform.Plain;
            ok &= LandformClassifier.Of(0.30f) == Landform.Hills;
            ok &= LandformClassifier.Of(0.60f) == Landform.Mountains;
            ok &= LandformClassifier.Of(0.90f) == Landform.Peaks;
            ok &= LandformClassifier.Of(1.00f) == Landform.Peaks; // clamp at the top band
            Debug.Log(ok ? "Self-Test Landform: PASS" : "Self-Test Landform: FAIL");
        }

        [ContextMenu("Self-Test: Biome Matrix")]
        public void SelfTestBiomeMatrix()
        {
            bool ok = true;
            ok &= BiomeMatrix.Level5(0.00f) == 0 && BiomeMatrix.Level5(0.5f) == 2 && BiomeMatrix.Level5(1.0f) == 4;
            ok &= BiomeMatrix.Get(0, 0) == Biome.IceWaste;      // Ледяной × Сухой
            ok &= BiomeMatrix.Get(4, 4) == Biome.TropicalForest; // Жаркий × Мокрый
            ok &= BiomeMatrix.Get(2, 2) == Biome.Forest;         // Умеренный × Умеренный
            ok &= BiomeMatrix.Get(4, 0) == Biome.Desert;         // Жаркий × Сухой
            ok &= BiomeMatrix.Get(0, 4) == Biome.Glacier;        // Ледяной × Мокрый
            Debug.Log(ok ? "Self-Test Biome Matrix: PASS" : "Self-Test Biome Matrix: FAIL");
        }

        [ContextMenu("Self-Test: Biome Classifier (cooling)")]
        public void SelfTestBiomeClassifierCooling()
        {
            bool ok = true;
            // Water / beach short-circuits.
            ok &= BiomeClassifier.Classify(0.9f, 0.5f, 0.5f, 0.4f, isOcean: true,  isLake: false) == Biome.Ocean;
            ok &= BiomeClassifier.Classify(0.9f, 0.5f, 0.5f, 0.4f, isOcean: false, isLake: true)  == Biome.Lake;
            ok &= BiomeClassifier.Classify(0.9f, 0.5f, 0.05f, 0.4f, false, false, beachElevationThreshold: 0.1f) == Biome.Beach;
            // Hot lowland → Savanna (t=4, m=2). No cooling at elevation 0.
            ok &= BiomeClassifier.Classify(0.9f, 0.5f, 0.0f, 0.4f, false, false, 0f) == Biome.Savanna;
            // Same climate at the peak: effTemp = 0.9 − 0.4 = 0.5 → t-level 2 → Forest.
            ok &= BiomeClassifier.Classify(0.9f, 0.5f, 1.0f, 0.4f, false, false, 0f) == Biome.Forest;
            Debug.Log(ok ? "Self-Test Biome Classifier: PASS" : "Self-Test Biome Classifier: FAIL");
        }

        // Regression guard for the lake-loss bug: after the pipeline reorder, lake identity
        // (Biome==Lake) must be set by CellWaterAssigner.MarkLakes BEFORE classification, and the
        // later ClassifyAll pass must PRESERVE it (EffectiveIsLake reads Biome==Lake) rather than
        // reclassifying the cell as land.
        [ContextMenu("Self-Test: Lake Preserved")]
        public void SelfTestLakePreserved()
        {
            // Inland lake (water, not ocean), ocean, and land cells.
            var lake  = new VoronoiCell(1, new System.Numerics.Vector2(0, 0)) { IsOcean = false, Height = 0.05f, Humidity = 0.9f, Temperature = 0.6f };
            var ocean = new VoronoiCell(2, new System.Numerics.Vector2(0, 0)) { IsOcean = true };
            var land  = new VoronoiCell(3, new System.Numerics.Vector2(0, 0)) { IsOcean = false, Height = 0.5f, Humidity = 0.5f, Temperature = 0.6f };
            var cells = new System.Collections.Generic.List<VoronoiCell> { lake, ocean, land };
            var waterCellIds = new System.Collections.Generic.HashSet<int> { lake.Id, ocean.Id };

            CellWaterAssigner.MarkLakes(cells, waterCellIds);
            bool ok = lake.Biome == Biome.Lake         // inland water → Lake
                   && ocean.Biome != Biome.Lake         // ocean is not marked a lake
                   && land.Biome != Biome.Lake;         // land not in waterCellIds → untouched

            // Classification must PRESERVE the lake (the regression reclassified it to land).
            CellOverrideService.ElevationTempDrop = 0.4f;
            CellOverrideService.ClassifyAll(cells, beachElevationThreshold: 0f);
            ok &= lake.Biome == Biome.Lake;              // still a lake after classification
            ok &= ocean.Biome == Biome.Ocean;           // ocean stays ocean
            ok &= land.Biome != Biome.Lake && land.Biome != Biome.Ocean; // land got a real land biome

            Debug.Log(ok ? "Self-Test Lake Preserved: PASS" : "Self-Test Lake Preserved: FAIL");
        }
    }
}
