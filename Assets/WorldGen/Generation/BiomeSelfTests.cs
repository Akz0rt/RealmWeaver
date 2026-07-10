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
    }
}
