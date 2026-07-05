using UnityEngine;

namespace WorldGen.Update
{
    /// <summary>
    /// Checks GitHub Releases for a newer version on launch and offers a one-click
    /// silent update. Self-contained — add to any GameObject, no Inspector wiring needed.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        const string ApiUrl = "https://api.github.com/repos/Akz0rt/RealmWeaver/releases/latest";

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ── Self-test ──────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Version Compare")]
        public void SelfTestVersionCompare()
        {
            bool t1 = UpdateVersionCompare.IsNewer("v1.2.0", "1.1.9");   // minor bump -> newer
            bool t2 = UpdateVersionCompare.IsNewer("v1.1.0", "1.1.0");   // equal -> not newer
            bool t3 = UpdateVersionCompare.IsNewer("v1.0.9", "1.1.0");   // remote older -> not newer
            bool t4 = UpdateVersionCompare.IsNewer("v2.0.0", "1.9.9");   // major bump -> newer
            bool t5 = !UpdateVersionCompare.IsNewer("garbage", "1.0.0"); // unparseable -> not newer

            bool ok = t1 && !t2 && !t3 && t4 && t5;
            Debug.Log(ok
                ? "Self-Test Version Compare: PASS"
                : $"Self-Test Version Compare: FAIL (t1={t1}, t2={t2}, t3={t3}, t4={t4}, t5={t5})");
        }
    }
}
