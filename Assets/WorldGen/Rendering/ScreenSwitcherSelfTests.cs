using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering
{
    /// <summary>
    /// [ContextMenu] self-test for ScreenSwitcher, matching this project's self-test convention
    /// (see ConfirmDialogSelfTests / BiomeSelfTests). Add to any GameObject temporarily, run from
    /// the Inspector's right-click menu, then remove (no need to save the scene). Builds throwaway
    /// GameObjects, exercises Show, asserts exclusivity + the hook, cleans up.
    ///
    /// TASK 10c re-pointed this at the narrowed three-screen table (Generation / Progress / Workspace) and
    /// added the rule that narrowing brought with it: the WORKSPACE is deactivated for Generation and
    /// Progress, not merely left unpopulated. That rule closes the defect the user reported at the Task 10a
    /// checkpoint — the generation form floating against a live shell with «Слои» painted over it.
    ///
    /// WHAT THIS SUITE CANNOT REACH, stated so a green run is not read as more coverage than it is: this
    /// exercises ScreenSwitcher, which is pure "toggle these GameObjects and call the hook". The OTHER half of
    /// the workspace-deactivation rule — WorkspaceController.SetShellActive making SyncSurfaces hide every
    /// registered host, so the map camera's punched hole and the five ex-screen canvases stop drawing — lives
    /// outside a plain GameObject toggle and needs a real workspace, real panes and real hosts. It is an
    /// in-Editor checkpoint item, not something this file asserts.
    /// </summary>
    public class ScreenSwitcherSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: ScreenSwitcher Exclusivity")]
        public void SelfTestExclusivity()
        {
            var gen = new GameObject("t_gen");
            var prog = new GameObject("t_prog");
            var shell = new GameObject("t_shell");

            AppScreen lastHook = AppScreen.Generation;
            int hookCalls = 0;
            // Mirrors MapScreenController.EnsureSwitcher's real table: one member per screen, the workspace's
            // being the shell canvas. The hook records what MapScreenController's own hook forwards to
            // WorkspaceController.SetShellActive — `screen == AppScreen.Workspace`.
            var switcher = new ScreenSwitcher(
                new Dictionary<AppScreen, GameObject[]>
                {
                    { AppScreen.Generation, new[] { gen } },
                    { AppScreen.Progress,   new[] { prog } },
                    { AppScreen.Workspace,  new[] { shell } },
                },
                s => { lastHook = s; hookCalls++; });

            switcher.Show(AppScreen.Workspace);
            bool workspaceOk = shell.activeSelf
                               && !gen.activeSelf && !prog.activeSelf
                               && switcher.Current == AppScreen.Workspace
                               && lastHook == AppScreen.Workspace && hookCalls == 1;

            // THE Task 10c RULE: showing Generation must DEACTIVATE the workspace, not merely leave it
            // unpopulated. Asserted positively (`!shell.activeSelf`) rather than inferred from "Generation is
            // on", because those are different claims — a switcher that activated the target without hiding
            // the rest would pass the second and fail this.
            switcher.Show(AppScreen.Generation);
            bool generationOk = gen.activeSelf
                                && !shell.activeSelf && !prog.activeSelf
                                && switcher.Current == AppScreen.Generation
                                && lastHook == AppScreen.Generation && hookCalls == 2;

            // Same rule for Progress — a separate assertion rather than a loop, because Progress is reached by
            // its own path (RefreshScreenStateForGenerating shows it DIRECTLY, bypassing DesiredScreen) and a
            // regression could plausibly hit one and not the other.
            switcher.Show(AppScreen.Progress);
            bool progressOk = prog.activeSelf
                              && !shell.activeSelf && !gen.activeSelf
                              && switcher.Current == AppScreen.Progress
                              && lastHook == AppScreen.Progress && hookCalls == 3;

            // Coming BACK re-activates the shell: the deactivation must be a state the switcher drives both
            // ways, not a one-way trip that leaves the workspace dead after the first generation.
            switcher.Show(AppScreen.Workspace);
            bool returnOk = shell.activeSelf
                            && !gen.activeSelf && !prog.activeSelf
                            && lastHook == AppScreen.Workspace && hookCalls == 4;

            foreach (var go in new[] { gen, prog, shell }) DestroyImmediate(go);

            bool ok = workspaceOk && generationOk && progressOk && returnOk;
            Debug.Log(ok
                ? "Self-Test ScreenSwitcher Exclusivity: PASS"
                : $"Self-Test ScreenSwitcher Exclusivity: FAIL (workspaceOk={workspaceOk}, " +
                  $"generationOk={generationOk}, progressOk={progressOk}, returnOk={returnOk})");
        }

        /// <summary>A screen with an EMPTY member array — the shape MapScreenController.EnsureSwitcher
        /// produces for AppScreen.Workspace when no workspace shell exists in the scene (the transitional
        /// state until Task 11 wires WorkspaceBuilder in). The switcher must still switch: `Current` and the
        /// hook are what MapScreenController relies on, and a memberless screen must not stop the OTHER
        /// screens' members from being deactivated.</summary>
        [ContextMenu("Self-Test: ScreenSwitcher Memberless Screen")]
        public void SelfTestMemberlessScreen()
        {
            var gen = new GameObject("t_gen");

            AppScreen lastHook = AppScreen.Progress;
            var switcher = new ScreenSwitcher(
                new Dictionary<AppScreen, GameObject[]>
                {
                    { AppScreen.Generation, new[] { gen } },
                    { AppScreen.Workspace,  new GameObject[0] },
                },
                s => lastHook = s);

            switcher.Show(AppScreen.Generation);
            bool genOk = gen.activeSelf && switcher.Current == AppScreen.Generation;

            switcher.Show(AppScreen.Workspace);
            bool workspaceOk = !gen.activeSelf
                               && switcher.Current == AppScreen.Workspace
                               && lastHook == AppScreen.Workspace;

            DestroyImmediate(gen);

            bool ok = genOk && workspaceOk;
            Debug.Log(ok
                ? "Self-Test ScreenSwitcher Memberless Screen: PASS"
                : $"Self-Test ScreenSwitcher Memberless Screen: FAIL (genOk={genOk}, workspaceOk={workspaceOk})");
        }
    }
}
