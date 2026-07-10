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
    }
}
