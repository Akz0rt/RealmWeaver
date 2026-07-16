using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public class IsoProjectionSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Iso Projection")]
        public void SelfTestProjection()
        {
            bool ok = true;
            // Origin maps to origin.
            var o = IsoProjection.ToScreen(0, 0, 32, 16); ok &= Mathf.Approximately(o.sx, 0) && Mathf.Approximately(o.sy, 0);
            // +X goes screen-right & down; +Y goes screen-left & down (classic 2:1 iso).
            var px = IsoProjection.ToScreen(1, 0, 32, 16); ok &= px.sx > 0 && px.sy > 0;
            var py = IsoProjection.ToScreen(0, 1, 32, 16); ok &= py.sx < 0 && py.sy > 0;
            // Depth increases with x+y (nearer draws later).
            ok &= IsoProjection.DepthKey(2, 2, 0) > IsoProjection.DepthKey(0, 0, 0);
            // A taller element at the same tile sorts after (on top of) a flat one.
            ok &= IsoProjection.DepthKey(1, 1, 5) > IsoProjection.DepthKey(1, 1, 0);
            Debug.Log(ok ? "Self-Test Iso Projection: PASS" : "Self-Test Iso Projection: FAIL");
        }
    }
}
