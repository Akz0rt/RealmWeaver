using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Workspace.Data
{
    /// <summary>
    /// Self-tests for the pure pane/tab layout layer. Runs two ways: right-click this component in the
    /// Editor, or offline via Tools/notes-harness (`powershell -File sync.ps1` then `dotnet run -c Release --
    /// selftests` from bash), which compiles these very sources against UnityEngine stubs.
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would break
    /// (R1..R7 in the plan), not a derived summary number.
    /// </summary>
    public class WorkspaceOpsSelfTests : MonoBehaviour
    {
        static SurfaceRef Page(string id) => new SurfaceRef { Kind = SurfaceKind.Page, Id = id };

        static string Dump(PaneState p)
        {
            if (p == null) return "-";
            var parts = new List<string>();
            for (int i = 0; i < p.Tabs.Count; i++) parts.Add((i == p.ActiveIndex ? "*" : "") + p.Tabs[i].Title);
            return string.Join(",", parts);
        }

        [ContextMenu("Self-Test: Workspace Open")]
        public void SelfTestOpen()
        {
            bool ok = true;
            var l = WorkspaceOps.NewDefault();

            // NewDefault's exact numeric contract — later tasks (split drag, navigator resize, persistence)
            // read every one of these, so a dropped field initializer must fail loudly here.
            if (l.Primary == null || l.Primary.Tabs.Count != 1 || l.Primary.ActiveIndex != 0)
            { Debug.LogError("FAIL open: NewDefault must start with one active tab in Primary"); ok = false; }
            else
            {
                var seed = l.Primary.Tabs[0];
                if (seed.Surface == null || seed.Surface.Kind != SurfaceKind.WorldMap || seed.Surface.Id != "")
                { Debug.LogError($"FAIL open: seed tab surface = {seed.Surface?.Kind}/«{seed.Surface?.Id}», want WorldMap/«»"); ok = false; }
                if (seed.Title != "Карта мира")
                { Debug.LogError($"FAIL open: seed tab title «{seed.Title}», want «Карта мира»"); ok = false; }
            }
            if (l.Secondary != null)
            { Debug.LogError("FAIL open: NewDefault must not start split"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL open: NewDefault focus = {l.FocusedPane}, want 0"); ok = false; }
            if (l.NavigatorCollapsed)
            { Debug.LogError("FAIL open: NewDefault must start with the navigator expanded"); ok = false; }
            if (System.Math.Abs(l.SplitRatio - 0.5f) > 0.0001f)
            { Debug.LogError($"FAIL open: NewDefault SplitRatio = {l.SplitRatio}, want 0.5"); ok = false; }
            if (System.Math.Abs(l.NavigatorWidth - 236f) > 0.0001f)
            { Debug.LogError($"FAIL open: NewDefault NavigatorWidth = {l.NavigatorWidth}, want 236"); ok = false; }

            WorkspaceOps.Open(l, Page("a"), "Сессия 1", false);
            WorkspaceOps.Open(l, Page("b"), "Ольга", false);
            if (Dump(l.Primary) != "Карта мира,Сессия 1,*Ольга")
            { Debug.LogError($"FAIL open: primary = [{Dump(l.Primary)}]"); ok = false; }

            // R1 — reopening an already-open surface activates it, it does not duplicate.
            WorkspaceOps.Open(l, Page("a"), "Сессия 1", false);
            if (l.Primary.Tabs.Count != 3 || Dump(l.Primary) != "Карта мира,*Сессия 1,Ольга")
            { Debug.LogError($"FAIL open: reopening duplicated a tab — [{Dump(l.Primary)}]"); ok = false; }

            // R2 — the other pane is created on demand AND takes focus.
            WorkspaceOps.Open(l, Page("c"), "Ржавый Якорь", true);
            if (l.Secondary == null || Dump(l.Secondary) != "*Ржавый Якорь")
            { Debug.LogError($"FAIL open: secondary = [{Dump(l.Secondary)}]"); ok = false; }
            if (l.FocusedPane != 1)
            { Debug.LogError($"FAIL open: focus = {l.FocusedPane}, want 1"); ok = false; }

            // Opening without inOtherPane now goes to the FOCUSED pane, which is the secondary.
            WorkspaceOps.Open(l, Page("d"), "Хель", false);
            if (l.Secondary.Tabs.Count != 2 || l.Primary.Tabs.Count != 3)
            { Debug.LogError("FAIL open: a plain open must land in the focused pane"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Open: PASS" : "Self-Test Workspace Open: FAIL");
        }

        [ContextMenu("Self-Test: Workspace Close And Collapse")]
        public void SelfTestClose()
        {
            bool ok = true;

            // R3 — emptying the secondary collapses the split and returns focus to the primary.
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "Сессия 1", false);
            WorkspaceOps.Open(l, Page("b"), "Ольга", true);
            WorkspaceOps.CloseTab(l, 1, 0);
            if (l.Secondary != null)
            { Debug.LogError("FAIL close: emptying the secondary must collapse the split (R3)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL close: focus = {l.FocusedPane} after collapse, want 0"); ok = false; }

            // R4 — emptying the PRIMARY promotes the secondary rather than leaving a hole on the left.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.CloseTab(l, 0, 0);                       // primary now empty
            WorkspaceOps.Open(l, Page("a"), "Сессия 1", false);
            WorkspaceOps.Open(l, Page("b"), "Ольга", true);
            WorkspaceOps.CloseTab(l, 0, 0);
            if (l.Secondary != null || l.Primary == null || Dump(l.Primary) != "*Ольга")
            { Debug.LogError($"FAIL close: promotion failed — primary [{Dump(l.Primary)}], secondary [{Dump(l.Secondary)}] (R4)"); ok = false; }

            // R5 — closing the active tab keeps ActiveIndex in range.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);
            WorkspaceOps.CloseTab(l, 0, 2);                       // the active one
            if (l.Primary.ActiveIndex < 0 || l.Primary.ActiveIndex >= l.Primary.Tabs.Count)
            { Debug.LogError($"FAIL close: ActiveIndex = {l.Primary.ActiveIndex} out of range (R5)"); ok = false; }

            // An empty pane reports -1, never 0.
            while (l.Primary.Tabs.Count > 0) WorkspaceOps.CloseTab(l, 0, 0);
            if (l.Primary.ActiveIndex != -1)
            { Debug.LogError($"FAIL close: empty pane ActiveIndex = {l.Primary.ActiveIndex}, want -1 (R5)"); ok = false; }

            // R6 — focusing a pane that does not exist is ignored.
            WorkspaceOps.Focus(l, 1);
            if (l.FocusedPane != 0)
            { Debug.LogError("FAIL close: focusing an absent pane must be ignored (R6)"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Close And Collapse: PASS" : "Self-Test Workspace Close And Collapse: FAIL");
        }

        [ContextMenu("Self-Test: Workspace Move And Prune")]
        public void SelfTestMoveAndPrune()
        {
            bool ok = true;
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", true);

            // Moving the last tab out of the secondary collapses it (R3 again, through a different door).
            WorkspaceOps.MoveTab(l, 1, 0, 0, 0);
            if (l.Secondary != null)
            { Debug.LogError("FAIL move: moving the last secondary tab out must collapse the split"); ok = false; }
            if (Dump(l.Primary) != "*B,Карта мира,A")
            { Debug.LogError($"FAIL move: primary = [{Dump(l.Primary)}], want B first and active"); ok = false; }

            // R7 — pruning drops what no longer exists and reports the count.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("gone"), "Удалённая", false);
            WorkspaceOps.Open(l, Page("kept"), "Живая", true);
            int dropped = WorkspaceOps.PruneMissing(l, s => s.Kind != SurfaceKind.Page || s.Id != "gone");
            if (dropped != 1)
            { Debug.LogError($"FAIL prune: dropped {dropped}, want 1 (R7)"); ok = false; }
            foreach (var t in l.Primary.Tabs)
                if (t.Surface.Id == "gone")
                { Debug.LogError("FAIL prune: a dead tab survived"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Move And Prune: PASS" : "Self-Test Workspace Move And Prune: FAIL");
        }
    }
}
