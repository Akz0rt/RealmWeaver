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
            {
                string actual = l.Primary == null ? "null" : $"{l.Primary.Tabs.Count} tab(s), ActiveIndex {l.Primary.ActiveIndex}";
                Debug.LogError($"FAIL open: NewDefault primary = [{actual}], want 1 tab, ActiveIndex 0");
                ok = false;
            }
            else
            {
                var seed = l.Primary.Tabs[0];
                if (seed.Surface == null || seed.Surface.Kind != SurfaceKind.WorldMap || seed.Surface.Id != "")
                { Debug.LogError($"FAIL open: seed tab surface = {seed.Surface?.Kind}/«{seed.Surface?.Id}», want WorldMap/«»"); ok = false; }
                if (seed.Title != "Карта мира")
                { Debug.LogError($"FAIL open: seed tab title «{seed.Title}», want «Карта мира»"); ok = false; }
            }
            if (l.Secondary != null)
            { Debug.LogError($"FAIL open: NewDefault secondary = [{Dump(l.Secondary)}], want null (no split)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL open: NewDefault focus = {l.FocusedPane}, want 0"); ok = false; }
            if (l.NavigatorCollapsed)
            { Debug.LogError($"FAIL open: NewDefault NavigatorCollapsed = {l.NavigatorCollapsed}, want false"); ok = false; }
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

            // R7 through the collapse door — pruning the SECONDARY down to nothing must collapse the split
            // and fall focus back to 0, exactly like CloseTab/MoveTab emptying it (R3), not leave an empty
            // Secondary sitting there unpruned-looking.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("gone-only"), "Мертва", true);   // Secondary = [gone-only], focus -> 1
            int droppedCollapse = WorkspaceOps.PruneMissing(l, s => s.Kind != SurfaceKind.Page || s.Id != "gone-only");
            if (droppedCollapse != 1)
            { Debug.LogError($"FAIL prune: droppedCollapse = {droppedCollapse}, want 1 (R7)"); ok = false; }
            if (l.Secondary != null)
            { Debug.LogError($"FAIL prune: emptying the secondary via prune must collapse the split (R7/R3), secondary = [{Dump(l.Secondary)}]"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL prune: focus = {l.FocusedPane} after prune-collapse, want 0 (R7/R3)"); ok = false; }

            // R7 across BOTH panes — the returned count must include what Secondary lost too, and survivors
            // in BOTH panes must be checked, not just Primary's.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("gone-p"), "МертваП", false);   // Primary = [Карта мира, gone-p]
            WorkspaceOps.Open(l, Page("kept-p"), "ЖиваП", false);     // Primary = [Карта мира, gone-p, kept-p]
            WorkspaceOps.Open(l, Page("gone-s"), "МертваС", true);    // Secondary = [gone-s], focus -> 1
            WorkspaceOps.Open(l, Page("kept-s"), "ЖиваС", false);     // lands in the focused pane: Secondary
            int droppedBoth = WorkspaceOps.PruneMissing(l, s => s.Kind != SurfaceKind.Page || (s.Id != "gone-p" && s.Id != "gone-s"));
            if (droppedBoth != 2)
            { Debug.LogError($"FAIL prune: dropped {droppedBoth} across both panes, want 2 — Secondary's dead tab must be counted too (R7)"); ok = false; }
            if (l.Secondary == null || l.Primary.Tabs.Count != 2 || l.Secondary.Tabs.Count != 1)
            { Debug.LogError($"FAIL prune: after pruning both panes, primary = [{Dump(l.Primary)}], secondary = [{Dump(l.Secondary)}], want 2 tabs / 1 tab surviving"); ok = false; }
            else
            {
                foreach (var t in l.Primary.Tabs)
                    if (t.Surface.Id == "gone-p")
                    { Debug.LogError("FAIL prune: a dead tab survived in Primary"); ok = false; }
                foreach (var t in l.Secondary.Tabs)
                    if (t.Surface.Id == "gone-s")
                    { Debug.LogError("FAIL prune: a dead tab survived in Secondary"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Workspace Move And Prune: PASS" : "Self-Test Workspace Move And Prune: FAIL");
        }

        [ContextMenu("Self-Test: Workspace Persistence")]
        public void SelfTestPersistence()
        {
            bool ok = true;
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "Сессия 1", false);
            WorkspaceOps.Open(l, new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = "poi-7" }, "Пепельный Курган", true);
            l.SplitRatio = 0.62f; l.NavigatorCollapsed = true; l.NavigatorWidth = 300f;

            string payload = WorkspaceOps.Serialize(l);
            if (!WorkspaceOps.TryDeserialize(payload, out var back))
            { Debug.LogError($"FAIL persist: TryDeserialize returned false, want true, for a payload we just produced — [{payload}]"); ok = false; }
            else
            {
                if (Dump(back.Primary) != Dump(l.Primary) || Dump(back.Secondary) != Dump(l.Secondary))
                { Debug.LogError($"FAIL persist: back = [{Dump(back.Primary)}] / [{Dump(back.Secondary)}], want [{Dump(l.Primary)}] / [{Dump(l.Secondary)}]"); ok = false; }
                if (back.FocusedPane != l.FocusedPane)
                { Debug.LogError($"FAIL persist: focus {back.FocusedPane}, want {l.FocusedPane}"); ok = false; }
                if (System.Math.Abs(back.SplitRatio - 0.62f) > 0.001f || !back.NavigatorCollapsed
                    || System.Math.Abs(back.NavigatorWidth - 300f) > 0.001f)
                {
                    Debug.LogError($"FAIL persist: settings back = ratio {back.SplitRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)}, collapsed {back.NavigatorCollapsed}, width {back.NavigatorWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)} — want ratio 0.62, collapsed True, width 300");
                    ok = false;
                }
                var dungeon = back.Secondary.Tabs[0].Surface;
                if (dungeon.Kind != SurfaceKind.Dungeon || dungeon.Id != "poi-7")
                { Debug.LogError($"FAIL persist: surface came back {dungeon.Kind}/{dungeon.Id}, want Dungeon/poi-7"); ok = false; }
            }

            // A title carrying a tab or newline must not corrupt the payload.
            var tricky = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(tricky, Page("x"), "имя\tс табом\nи переносом", false);
            WorkspaceOps.TryDeserialize(WorkspaceOps.Serialize(tricky), out var back2);
            if (back2 == null || back2.Primary.Tabs.Count < 2 || back2.Primary.Tabs[1].Title != "имя\tс табом\nи переносом")
            {
                string actualTitle = back2 == null ? "<parse failed>"
                    : back2.Primary.Tabs.Count < 2 ? $"<only {back2.Primary.Tabs.Count} tab(s) came back>"
                    : back2.Primary.Tabs[1].Title;
                Debug.LogError($"FAIL persist: title back = «{actualTitle}», want «имя\tс табом\nи переносом»");
                ok = false;
            }

            // The plain "nonsense" junk above is refused by the settings line's int/float parses failing
            // outright — it does not by itself prove the FIELD-COUNT checks matter. These two add fields the
            // existing fields still parse cleanly around, so only a field-count check catches them: one
            // settings line with a trailing extra field, one tab line with a trailing extra field.
            foreach (string junk in new[]
            {
                "", "мусор", "WORKSPACE/1\nnonsense",
                "WORKSPACE/1\n0\t0.5\t0\t236\textra",
                "WORKSPACE/1\n0\tPage\ta\t1\ttitle\textra\n0\t0.5\t0\t236",
            })
                if (WorkspaceOps.TryDeserialize(junk, out _))
                { Debug.LogError($"FAIL persist: «{junk.Replace("\n", "\\n")}» must be refused"); ok = false; }

            // Out-of-range stored values are clamped, not trusted. Guarded so the check cannot pass having
            // exercised nothing: first pin that the payload really does carry the literal "0.62" the Replace
            // below depends on (if Serialize's float format ever changes, Replace silently becomes a no-op
            // and this assertion would otherwise test nothing), then require TryDeserialize to actually
            // SUCCEED with a clamped value — not merely "did not report an out-of-range ratio," which a null
            // `wild` (a refused parse) would satisfy just as emptily.
            string clampPayload = WorkspaceOps.Serialize(l);
            if (!clampPayload.Contains("0.62"))
            {
                Debug.LogError($"FAIL persist: clamp test's own payload has no literal \"0.62\" to mutate, want it present — [{clampPayload}]");
                ok = false;
            }
            string mutatedPayload = clampPayload.Replace("0.62", "9.9");
            if (!WorkspaceOps.TryDeserialize(mutatedPayload, out var wild) || wild == null)
            {
                Debug.LogError($"FAIL persist: TryDeserialize returned false/null for an out-of-range-but-otherwise-valid payload, want true with SplitRatio clamped — [{mutatedPayload}]");
                ok = false;
            }
            else if (wild.SplitRatio > 0.75f || wild.SplitRatio < 0.25f)
            {
                Debug.LogError($"FAIL persist: SplitRatio {wild.SplitRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)} was not clamped, want within 0.25..0.75");
                ok = false;
            }

            Debug.Log(ok ? "Self-Test Workspace Persistence: PASS" : "Self-Test Workspace Persistence: FAIL");
        }
    }
}
