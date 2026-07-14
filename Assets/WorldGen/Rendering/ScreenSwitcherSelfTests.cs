using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering
{
    /// <summary>
    /// [ContextMenu] self-test for ScreenSwitcher, matching this project's self-test convention
    /// (see ConfirmDialogSelfTests / BiomeSelfTests). Add to any GameObject temporarily, run from
    /// the Inspector's right-click menu, then remove (no need to save the scene). Builds throwaway
    /// GameObjects, exercises Show, asserts exclusivity + the hook, cleans up.
    /// </summary>
    public class ScreenSwitcherSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: ScreenSwitcher Exclusivity")]
        public void SelfTestExclusivity()
        {
            var gen = new GameObject("t_gen");
            var prog = new GameObject("t_prog");
            var edit = new GameObject("t_edit");
            var legend = new GameObject("t_legend");
            var poi = new GameObject("t_poi");

            AppScreen lastHook = AppScreen.Generation;
            int hookCalls = 0;
            var switcher = new ScreenSwitcher(
                new Dictionary<AppScreen, GameObject[]>
                {
                    { AppScreen.Generation, new[] { gen } },
                    { AppScreen.Progress,   new[] { prog } },
                    { AppScreen.MapEditor,  new[] { edit, legend } },
                    { AppScreen.PoiEditor,  new[] { poi } },
                },
                s => { lastHook = s; hookCalls++; });

            switcher.Show(AppScreen.MapEditor);
            bool mapOk = edit.activeSelf && legend.activeSelf
                         && !gen.activeSelf && !prog.activeSelf && !poi.activeSelf
                         && switcher.Current == AppScreen.MapEditor
                         && lastHook == AppScreen.MapEditor && hookCalls == 1;

            switcher.Show(AppScreen.PoiEditor);
            bool poiOk = poi.activeSelf
                         && !gen.activeSelf && !prog.activeSelf && !edit.activeSelf && !legend.activeSelf
                         && switcher.Current == AppScreen.PoiEditor
                         && lastHook == AppScreen.PoiEditor && hookCalls == 2;

            foreach (var go in new[] { gen, prog, edit, legend, poi }) DestroyImmediate(go);

            bool ok = mapOk && poiOk;
            Debug.Log(ok
                ? "Self-Test ScreenSwitcher Exclusivity: PASS"
                : $"Self-Test ScreenSwitcher Exclusivity: FAIL (mapOk={mapOk}, poiOk={poiOk})");
        }
    }
}
