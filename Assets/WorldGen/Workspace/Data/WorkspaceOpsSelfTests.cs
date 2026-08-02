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
        static SurfaceRef Canvas(string id) => new SurfaceRef { Kind = SurfaceKind.Canvas, Id = id };

        /// <summary>A layout with `s` open in BOTH panes — the state R1b stops Open from ever producing, and
        /// which a WorkspacePrefs payload written before R1b can still restore. Assembled directly rather
        /// than through Open for that reason: routing it through Open would build a MOVE and leave the
        /// duplicate-handling rules (FindSurface's Primary-first tie-break, PruneMissing's drop-every-match)
        /// tested against a state they never see.</summary>
        static WorkspaceLayout Duplicated(SurfaceRef s, string title)
        {
            var l = WorkspaceOps.NewDefault();                      // Primary: [Карта мира]
            l.Primary.Tabs.Add(new TabState { Surface = s, Title = title });
            l.Primary.ActiveIndex = l.Primary.Tabs.Count - 1;
            l.Secondary = new PaneState();
            l.Secondary.Tabs.Add(new TabState { Surface = s, Title = title });
            l.Secondary.ActiveIndex = 0;
            return l;
        }

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

        /// <summary>R1b — opening a surface that is already open in the OTHER pane MOVES its tab into the
        /// target pane instead of creating a second one. R1's other half; see WorkspaceOps.Open's own doc for
        /// why moving is right (every surface host is single-instance, so a duplicate tab is a permanently
        /// blank one) and why the rule is written against the TARGET pane rather than the focused one.
        ///
        /// FOUR RULES, FOUR MUTANTS, each assertion aimed at the geometry the rule changes rather than at a
        /// derived count:
        ///   1. the tab MOVES — it leaves the source pane and appears in the target, and the total tab count
        ///      across both panes does not grow. A duplicating implementation passes any test that only
        ///      checks "the target pane now contains it";
        ///   2. the moved tab is ACTIVE in the target pane, and the target pane is focused — an implementation
        ///      that appends without touching ActiveIndex leaves the DM staring at a different tab;
        ///   3. a SAME-pane reopen still ACTIVATES (R1), and specifically does not fall through into the move
        ///      branch — the failure mode of writing R1b as a bare "is it open anywhere" check;
        ///   4. emptying the source pane collapses the split through the existing NormalizeSplit, with focus
        ///      and the tab landing in the promoted pane — the case where `target` is stale by the time
        ///      MoveTab returns.</summary>
        [ContextMenu("Self-Test: Workspace Open Moves From The Other Pane")]
        public void SelfTestOpenMovesFromOtherPane()
        {
            bool ok = true;

            // ── 1 + 2: the move, and where it lands ────────────────────────────────
            // Primary [Карта мира, A, B], Secondary [C], focus back on Primary.
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);
            WorkspaceOps.Open(l, Page("c"), "C", true);
            WorkspaceOps.Focus(l, 0);

            WorkspaceOps.Open(l, Page("c"), "C", false);

            if (Dump(l.Primary) != "Карта мира,A,B,*C")
            { Debug.LogError($"FAIL move-open: primary = [{Dump(l.Primary)}], want [Карта мира,A,B,*C] — the tab must MOVE here and be active (R1b)"); ok = false; }
            if (l.Secondary != null)
            { Debug.LogError($"FAIL move-open: secondary = [{Dump(l.Secondary)}], want null — moving its only tab out must collapse the split via NormalizeSplit (R1b/R3)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL move-open: focus = {l.FocusedPane}, want 0 (R1b)"); ok = false; }

            // ── 1, the half a "does the target have it?" assertion cannot see ─────
            // The SOURCE pane must have lost it, and the workspace must hold exactly ONE tab for the surface.
            // Built with tabs on both sides of the move so the collapse cannot mask a copy left behind.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);            // Primary [Карта мира, A]
            WorkspaceOps.Open(l, Page("c"), "C", true);             // Secondary [C], focus 1
            WorkspaceOps.Open(l, Page("d"), "D", false);            // Secondary [C, D], focus 1
            WorkspaceOps.Focus(l, 0);
            WorkspaceOps.Open(l, Page("c"), "C", false);

            int copies = 0;
            foreach (var pane in new[] { l.Primary, l.Secondary })
            {
                if (pane == null) continue;
                foreach (var tab in pane.Tabs)
                    if (tab.Surface != null && tab.Surface.Kind == SurfaceKind.Page && tab.Surface.Id == "c") copies++;
            }
            if (copies != 1)
            { Debug.LogError($"FAIL move-open: the surface has {copies} tab(s) across both panes, want exactly 1 — R1b must MOVE, never duplicate"); ok = false; }
            if (Dump(l.Primary) != "Карта мира,A,*C")
            { Debug.LogError($"FAIL move-open: primary = [{Dump(l.Primary)}], want [Карта мира,A,*C] (R1b)"); ok = false; }
            if (Dump(l.Secondary) != "*D")
            { Debug.LogError($"FAIL move-open: secondary = [{Dump(l.Secondary)}], want [*D] — the source pane must lose the tab and keep a valid ActiveIndex (R1b/R5)"); ok = false; }

            // ── 3: R1 still wins inside the target pane ───────────────────────────
            // Re-opening A, which lives in the FOCUSED pane, must activate it in place — not route through
            // the move branch and push it to the end of its own pane, and not touch the other pane at all.
            WorkspaceOps.Open(l, Page("a"), "A", false);
            if (Dump(l.Primary) != "Карта мира,*A,C")
            { Debug.LogError($"FAIL move-open: primary = [{Dump(l.Primary)}], want [Карта мира,*A,C] — a same-pane reopen ACTIVATES in place (R1)"); ok = false; }
            if (Dump(l.Secondary) != "*D")
            { Debug.LogError($"FAIL move-open: a same-pane reopen disturbed the other pane — secondary = [{Dump(l.Secondary)}], want [*D] (R1)"); ok = false; }

            // ── 4: the source pane was PRIMARY, so the collapse PROMOTES ──────────
            // Primary [X] alone, Secondary [Y]; opening X from the secondary empties Primary, and R4 promotes
            // Secondary into Primary's slot — so `target` (1) names a pane that no longer exists by the time
            // MoveTab returns. Focus and ActiveIndex must follow where the tab actually landed.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.CloseTab(l, 0, 0);                          // drop the seeded map tab
            WorkspaceOps.Open(l, Page("x"), "X", false);             // Primary [*X], focus 0
            WorkspaceOps.Open(l, Page("y"), "Y", true);              // Secondary [*Y], focus 1
            WorkspaceOps.Open(l, Page("x"), "X", false);             // focused pane is 1 -> move X out of Primary

            if (l.Secondary != null)
            { Debug.LogError($"FAIL move-open: secondary = [{Dump(l.Secondary)}], want null — emptying PRIMARY must promote, not leave two panes (R1b/R4)"); ok = false; }
            if (Dump(l.Primary) != "Y,*X")
            { Debug.LogError($"FAIL move-open: primary = [{Dump(l.Primary)}], want [Y,*X] — the promoted pane keeps the moved tab active (R1b/R4)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL move-open: focus = {l.FocusedPane} after promotion, want 0 — focus must follow the tab, not the stale target index (R1b/R4)"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Open Moves From Other Pane: PASS" : "Self-Test Workspace Open Moves From Other Pane: FAIL");
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

        [ContextMenu("Self-Test: Workspace Set Active Tab")]
        public void SelfTestSetActiveTab()
        {
            bool ok = true;
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);
            // Primary is now [Карта мира, A, *B] — ActiveIndex 2.
            int activeBefore = l.Primary.ActiveIndex;

            // An absent pane (no Secondary exists here) is refused outright, and Primary is untouched.
            bool absentPaneOk = WorkspaceOps.SetActiveTab(l, 1, 0);
            if (absentPaneOk)
            { Debug.LogError($"FAIL setActive: SetActiveTab on an absent pane returned {absentPaneOk}, want false"); ok = false; }
            if (l.Primary.ActiveIndex != activeBefore)
            { Debug.LogError($"FAIL setActive: an absent-pane call left Primary.ActiveIndex at {l.Primary.ActiveIndex}, want unchanged {activeBefore}"); ok = false; }

            // An out-of-range index on a real pane is refused too — both above and below the valid range.
            bool tooHighOk = WorkspaceOps.SetActiveTab(l, 0, 99);
            if (tooHighOk)
            { Debug.LogError($"FAIL setActive: SetActiveTab(0, 99) returned {tooHighOk}, want false (only {l.Primary.Tabs.Count} tabs)"); ok = false; }
            bool negativeOk = WorkspaceOps.SetActiveTab(l, 0, -1);
            if (negativeOk)
            { Debug.LogError($"FAIL setActive: SetActiveTab(0, -1) returned {negativeOk}, want false"); ok = false; }
            if (l.Primary.ActiveIndex != activeBefore)
            { Debug.LogError($"FAIL setActive: an out-of-range call left Primary.ActiveIndex at {l.Primary.ActiveIndex}, want unchanged {activeBefore}"); ok = false; }

            // Re-requesting the already-active index changes nothing, and says so via its return value.
            bool sameOk = WorkspaceOps.SetActiveTab(l, 0, activeBefore);
            if (sameOk)
            { Debug.LogError($"FAIL setActive: re-activating the already-active index {activeBefore} returned {sameOk}, want false (no change)"); ok = false; }

            // A valid call actually moves ActiveIndex and reports that it did.
            bool movedOk = WorkspaceOps.SetActiveTab(l, 0, 0);
            if (!movedOk)
            { Debug.LogError($"FAIL setActive: SetActiveTab(0, 0) returned {movedOk}, want true"); ok = false; }
            if (l.Primary.ActiveIndex != 0 || Dump(l.Primary) != "*Карта мира,A,B")
            { Debug.LogError($"FAIL setActive: primary = [{Dump(l.Primary)}], want «*Карта мира,A,B»"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Set Active Tab: PASS" : "Self-Test Workspace Set Active Tab: FAIL");
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

        /// <summary>HasSurfaceOfKind — the guard CanvasTabPruner asks before every prune.
        ///
        /// THE MUTANT THIS IS BUILT AGAINST is "looks only at Primary". It is the natural thing to write and
        /// it passes any fixture that opens the board in the first pane — so every case below that involves a
        /// board opens it in the SECONDARY, because that is where «↗» actually puts one (inOtherPane: true).
        /// The Primary case is here too, but as the weaker half.
        ///
        /// The last case is the other direction: a layout full of tabs of OTHER kinds must answer no, or the
        /// guard is a constant true and buys nothing.</summary>
        [ContextMenu("Self-Test: Workspace Has Surface Of Kind")]
        public void SelfTestHasSurfaceOfKind()
        {
            bool ok = true;

            // No layout at all, and an empty one: false, not a throw. WorkspacePrefs can hand back either.
            if (WorkspaceOps.HasSurfaceOfKind(null, SurfaceKind.Canvas))
            { Debug.LogError("FAIL kind: a null layout must answer no"); ok = false; }
            if (WorkspaceOps.HasSurfaceOfKind(new WorkspaceLayout(), SurfaceKind.Canvas))
            { Debug.LogError("FAIL kind: an empty layout must answer no"); ok = false; }

            // A board in the SECONDARY pane — the mutant's case. «↗» opens boards there and nowhere else.
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("p"), "Страница", false);
            WorkspaceOps.Open(l, Canvas("b1"), "Доска", true);
            if (!WorkspaceOps.HasSurfaceOfKind(l, SurfaceKind.Canvas))
            { Debug.LogError("FAIL kind: a board in the SECONDARY pane must be found — reading only Primary is the mutant"); ok = false; }

            // ...and in the primary, through the same call.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Canvas("b2"), "Доска", false);
            if (!WorkspaceOps.HasSurfaceOfKind(l, SurfaceKind.Canvas))
            { Debug.LogError("FAIL kind: a board in the PRIMARY pane must be found"); ok = false; }

            // Pages and the world map across both panes: no board anywhere, so no prune.
            l = WorkspaceOps.NewDefault();                          // Primary: [Карта мира]
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", true);              // Secondary: [B]
            if (WorkspaceOps.HasSurfaceOfKind(l, SurfaceKind.Canvas))
            { Debug.LogError("FAIL kind: a layout of pages and the map must answer no — otherwise the guard is a constant true"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Has Surface Of Kind: PASS" : "Self-Test Workspace Has Surface Of Kind: FAIL");
        }

        /// <summary>The outcomes Task 10d's tab drag newly depends on. The GESTURE (ghost, insertion marker,
        /// hit-testing a strip) is Unity-side and cannot run here; what CAN be pinned is the arithmetic every
        /// drop lands on, and this suite pins the four properties the drop resolver assumes and that nothing
        /// else asserted:
        ///
        /// 1. toIndex is a PRE-REMOVAL index. TabStripView.InsertIndexAt walks the strip with the dragged tab
        ///    still in it, so the index it hands MoveTab counts that tab — MoveTab's own `if (samePane &&
        ///    fromIndex &lt; toIndex) insertAt--` is what reconciles that with the removal. BOTH no-op forms
        ///    are pinned (drop on itself, and drop directly after itself) because they fail for OPPOSITE
        ///    mutations: an unconditional decrement breaks the first, a deleted decrement breaks the second.
        /// 2. A move that empties the SOURCE pane collapses the split — R3/R4 through MoveTab's door rather
        ///    than CloseTab's, which is the door a drag actually uses.
        /// 3. A move into a pane that DOES NOT EXIST YET creates it. That branch of MoveTab is the entire
        ///    split-producing drop, and every other move in this file — new and old — targets a pane
        ///    Open(inOtherPane: true) had already built, so nothing pinned it.
        /// 4. The moved tab is ACTIVE where it lands, including across panes. The drop relies on it: the
        ///    surface a drag puts in the other pane has to be the one that pane then shows.</summary>
        [ContextMenu("Self-Test: Workspace Move Tab Drop")]
        public void SelfTestMoveTabDrop()
        {
            bool ok = true;

            // ── (1a) Dropping a tab on ITSELF (toIndex == fromIndex) changes nothing. ──────────────
            var l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);        // Primary: [Карта мира, A, *B]
            if (!WorkspaceOps.MoveTab(l, 0, 2, 0, 2))
            { Debug.LogError("FAIL move-drop: MoveTab(0,2 -> 0,2) returned false, want true (a valid, if inert, move)"); ok = false; }
            if (Dump(l.Primary) != "Карта мира,A,*B")
            { Debug.LogError($"FAIL move-drop: dropping a tab on its own index reordered the pane — [{Dump(l.Primary)}], want «Карта мира,A,*B» (toIndex is PRE-removal: no adjustment may run when fromIndex == toIndex)"); ok = false; }

            // ── (1b) Dropping it directly AFTER itself (toIndex == fromIndex + 1) also changes nothing. ──
            // Here the adjustment DOES run and must cancel the removal out exactly. Asserted on the tab that
            // is already active, so "the active tab did not change" is a real claim about this move and not
            // an artefact of MoveTab re-activating whatever it moved.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);
            WorkspaceOps.SetActiveTab(l, 0, 1);                 // Primary: [Карта мира, *A, B]
            WorkspaceOps.MoveTab(l, 0, 1, 0, 2);
            if (Dump(l.Primary) != "Карта мира,*A,B")
            { Debug.LogError($"FAIL move-drop: dropping a tab just after itself moved it — [{Dump(l.Primary)}], want «Карта мира,*A,B» (PRE-removal toIndex: the fromIndex < toIndex adjustment must cancel the removal)"); ok = false; }

            // ── (1c) A real same-pane reorder lands where the PRE-REMOVAL index points. ────────────
            // [Карта мира, A, B], drag «Карта мира» onto the gap before B (index 2, counting the dragged tab
            // itself) — it must end up BETWEEN A and B, not after B.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);
            WorkspaceOps.Open(l, Page("b"), "B", false);
            WorkspaceOps.MoveTab(l, 0, 0, 0, 2);
            if (Dump(l.Primary) != "A,*Карта мира,B")
            { Debug.LogError($"FAIL move-drop: same-pane reorder landed at [{Dump(l.Primary)}], want «A,*Карта мира,B» — toIndex 2 is PRE-removal, so the tab goes before the tab that was at index 2"); ok = false; }

            // ── (2) A move that empties the PRIMARY promotes the secondary (R4 through MoveTab). ───
            // The CloseTab door is already covered (SelfTestClose); the drag door is not, and it is the one
            // that makes "drag your last tab into the other pane" survivable rather than a hole on the left.
            l = WorkspaceOps.NewDefault();                       // Primary: [*Карта мира]
            WorkspaceOps.Open(l, Page("a"), "A", true);          // Secondary: [*A], focus 1
            WorkspaceOps.MoveTab(l, 0, 0, 1, 1);                 // drag the map tab into the secondary, at the end
            if (l.Secondary != null)
            { Debug.LogError($"FAIL move-drop: emptying the PRIMARY by dragging must promote the secondary, not leave one — secondary = [{Dump(l.Secondary)}], primary = [{Dump(l.Primary)}] (R4)"); ok = false; }
            if (Dump(l.Primary) != "A,*Карта мира")
            { Debug.LogError($"FAIL move-drop: after promotion primary = [{Dump(l.Primary)}], want «A,*Карта мира» (R4 — the promoted pane keeps its own tabs and the dragged tab stays active)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL move-drop: focus = {l.FocusedPane} after the split collapsed, want 0 (R3/R4 — FocusedPane may never name an absent pane)"); ok = false; }

            // ── (3) A move into a pane that does not exist yet CREATES it — the split-producing drop. ──
            // The whole point of restoring this step («две вкладки рядом друг с другом перетаскиванием
            // вкладки») rests on this one branch of MoveTab, and nothing anywhere pinned it: every other
            // move in this file, new and old, targets a pane Open(inOtherPane: true) had already built. A
            // plausible future "hardening" — refusing a destination pane that does not exist — would leave
            // every other assertion green and silently delete the gesture.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);         // Primary: [Карта мира, *A], no split
            bool made = WorkspaceOps.MoveTab(l, 0, 1, 1, 0);
            if (!made)
            { Debug.LogError("FAIL move-drop: MoveTab into a pane that does not exist yet returned false, want true — creating the destination pane on demand (R2's shape, through MoveTab's door) IS the split-producing drop"); ok = false; }
            if (l.Secondary == null || Dump(l.Secondary) != "*A")
            { Debug.LogError($"FAIL move-drop: secondary = [{Dump(l.Secondary)}], want «*A» — dragging a tab out of the only pane must CREATE the second pane and leave the tab active in it"); ok = false; }
            if (Dump(l.Primary) != "*Карта мира")
            { Debug.LogError($"FAIL move-drop: primary = [{Dump(l.Primary)}] after the split was created, want «*Карта мира» — the source pane keeps its remaining tab with ActiveIndex back in range (R5)"); ok = false; }
            if (l.FocusedPane != 0)
            { Debug.LogError($"FAIL move-drop: focus = {l.FocusedPane}, want 0 — MoveTab does not move focus itself; WorkspaceController.MoveTab focuses the destination deliberately and separately (see its doc)"); ok = false; }

            // ── (4) The moved tab is the ACTIVE tab in the pane it lands in, across panes. ─────────
            // Asserted at a NON-ZERO destination index on purpose: a "make index 0 active" mutation would
            // pass at index 0 by coincidence.
            l = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(l, Page("a"), "A", false);         // Primary: [Карта мира, *A]
            WorkspaceOps.Open(l, Page("b"), "B", true);          // Secondary: [*B], focus 1
            WorkspaceOps.Open(l, Page("c"), "C", false);         // Secondary: [B, *C]
            WorkspaceOps.MoveTab(l, 1, 1, 0, 2);                 // drag C onto the end of the primary's strip
            if (Dump(l.Primary) != "Карта мира,A,*C")
            { Debug.LogError($"FAIL move-drop: primary = [{Dump(l.Primary)}], want «Карта мира,A,*C» — a tab dragged into a pane must be the ACTIVE tab there, at the index it was dropped"); ok = false; }
            if (l.Secondary == null || Dump(l.Secondary) != "*B")
            { Debug.LogError($"FAIL move-drop: secondary = [{Dump(l.Secondary)}], want «*B» — the source pane keeps its remaining tabs with a valid ActiveIndex (R5)"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Move Tab Drop: PASS" : "Self-Test Workspace Move Tab Drop: FAIL");
        }

        /// <summary>FindSurface (Task 10c): every Close* path in MapScreenController holds a SurfaceRef and no
        /// index, so "where is this open" has to be answerable from the layout alone.</summary>
        [ContextMenu("Self-Test: Workspace Find Surface")]
        public void SelfTestFindSurface()
        {
            bool ok = true;

            var l = WorkspaceOps.NewDefault();                             // Primary: [WorldMap]
            WorkspaceOps.Open(l, Page("a"), "A", false);                    // Primary: [WorldMap, A]
            WorkspaceOps.Open(l, new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = "poi-7" }, "Курган", true);
                                                                            // Secondary: [Курган]
            WorkspaceOps.Open(l, Page("b"), "B", false);                    // Secondary: [Курган, B] (focus moved)

            // Found in Primary, at the right index — asserting the INDEX, not merely "true": a FindSurface
            // that reported the wrong slot would close somebody else's tab, which is the actual damage.
            if (!WorkspaceOps.FindSurface(l, Page("a"), out int paneA, out int indexA) || paneA != 0 || indexA != 1)
            { Debug.LogError($"FAIL find: Page(a) reported pane {paneA}/index {indexA}, want pane 0/index 1"); ok = false; }

            // Found in Secondary — the pane the search reaches SECOND, so this is what proves the loop does
            // not stop at Primary.
            var dungeon = new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = "poi-7" };
            if (!WorkspaceOps.FindSurface(l, dungeon, out int paneD, out int indexD) || paneD != 1 || indexD != 0)
            { Debug.LogError($"FAIL find: Dungeon/poi-7 reported pane {paneD}/index {indexD}, want pane 1/index 0"); ok = false; }

            // A DIFFERENT ref naming the same surface must match — the whole point of deferring to
            // SameSurface rather than comparing instances. `dungeon` above is already a fresh instance; this
            // adds the Kind-differs case, which an Id-only comparison would wrongly match.
            if (WorkspaceOps.FindSurface(l, new SurfaceRef { Kind = SurfaceKind.Settlement, Id = "poi-7" }, out int paneS, out int indexS))
            { Debug.LogError($"FAIL find: Settlement/poi-7 matched at pane {paneS}/index {indexS}, want no match — same Id, different Kind"); ok = false; }

            // Absent: reports false AND leaves both outs at -1, so a caller that ignores the bool cannot
            // accidentally close pane 0's tab 0.
            if (WorkspaceOps.FindSurface(l, Page("nope"), out int paneN, out int indexN) || paneN != -1 || indexN != -1)
            { Debug.LogError($"FAIL find: Page(nope) reported {paneN}/{indexN} (returned true?), want false with -1/-1"); ok = false; }

            // Null layout / null surface: false and -1/-1, never a throw — MapScreenController calls this on
            // paths where the workspace may not exist yet.
            try
            {
                if (WorkspaceOps.FindSurface(null, Page("a"), out int pN1, out int iN1) || pN1 != -1 || iN1 != -1)
                { Debug.LogError("FAIL find: FindSurface(null layout, ...) must report false with -1/-1"); ok = false; }
                if (WorkspaceOps.FindSurface(l, null, out int pN2, out int iN2) || pN2 != -1 || iN2 != -1)
                { Debug.LogError("FAIL find: FindSurface(l, null surface) must report false with -1/-1"); ok = false; }
            }
            catch (System.Exception ex)
            { Debug.LogError($"FAIL find: a null argument threw {ex.GetType().Name}, want false with -1/-1"); ok = false; }

            // Open in BOTH panes: Primary wins, deterministically.
            //
            // BUILT BY HAND, and it has to be since R1b: Open no longer produces this state at all (an open
            // of a surface living in the other pane MOVES it), so constructing it through Open would silently
            // stop testing the tie-break and start testing the move. The state is still REACHABLE — a
            // WorkspacePrefs payload written before R1b existed restores whatever it holds, duplicates
            // included — which is exactly why the tie-break is still live code worth pinning.
            var both = Duplicated(Page("dup"), "Dup");
            if (!WorkspaceOps.FindSurface(both, Page("dup"), out int paneB, out int indexB) || paneB != 0)
            { Debug.LogError($"FAIL find: a surface open in both panes reported pane {paneB}/index {indexB}, want pane 0 (Primary first)"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Find Surface: PASS" : "Self-Test Workspace Find Surface: FAIL");
        }

        /// <summary>PruneMissing must drop EVERY tab the predicate rejects, not the first one it finds — the
        /// property MapScreenController.OnWorldRegenerated relies on since Task 10c's fix round. The old
        /// close-by-named-ref version of that method passed a single-instance check and still left duplicates
        /// behind, so both duplicate shapes are pinned here: the SAME surface open in both panes, and two
        /// DIFFERENT surfaces of a rejected kind.
        ///
        /// THE SAME-SURFACE-IN-BOTH-PANES HALF IS NOW BUILT BY HAND. It used to be assembled through
        /// Open(…, inOtherPane) — which R1b turned into a MOVE, so that construction would quietly leave this
        /// test asserting against three tabs in one pane and no duplicate at all. Restoring a pre-R1b
        /// WorkspacePrefs payload is the remaining way to reach the state, and it is a live one, so the shape
        /// stays tested; only the way it is built changed. See Duplicated().</summary>
        [ContextMenu("Self-Test: Workspace Prune Drops Every Match")]
        public void SelfTestPruneDropsEveryMatch()
        {
            bool ok = true;

            var l = WorkspaceOps.NewDefault();                                   // Primary: [WorldMap]
            var grid1 = new SurfaceRef { Kind = SurfaceKind.BattleGrid, Id = "poi-1#0#3" };
            var grid2 = new SurfaceRef { Kind = SurfaceKind.BattleGrid, Id = "poi-1#0#4" };
            var poiEd = new SurfaceRef { Kind = SurfaceKind.PoiEditor, Id = "poi-1" };

            WorkspaceOps.Open(l, grid1, "Бой 3", false);                          // Primary
            WorkspaceOps.Open(l, grid2, "Бой 4", false);                          // Primary — a SECOND grid
            WorkspaceOps.Open(l, Page("notes"), "Заметки", false);                // Primary — must survive
            WorkspaceOps.Open(l, poiEd, "Тихий Брод", false);                     // Primary; focus stays at 0
            // The SAME surface open in BOTH panes, written straight into the layout — see this method's doc
            // for why Open can no longer build it (R1b) and why the shape is still worth pinning. A separate
            // SurfaceRef instance with equal Kind/Id, so the prune cannot pass by reference identity.
            l.Secondary = new PaneState();
            l.Secondary.Tabs.Add(new TabState
            {
                Surface = new SurfaceRef { Kind = SurfaceKind.PoiEditor, Id = "poi-1" },
                Title = "Тихий Брод",
            });
            l.Secondary.ActiveIndex = 0;

            // Same predicate MapScreenController.SurvivesWorldChange uses: pages and the world map survive a
            // regeneration, every ex-screen kind does not.
            int dropped = WorkspaceOps.PruneMissing(l,
                s => s != null && (s.Kind == SurfaceKind.Page || s.Kind == SurfaceKind.WorldMap));

            if (dropped != 4)
            { Debug.LogError($"FAIL prune-all: reported {dropped} dropped, want 4 (two grids + two POI-editor tabs)"); ok = false; }

            // Asserted by SCANNING for survivors of a rejected kind, not by counting: a count would pass if
            // the prune dropped four of the WRONG tabs.
            foreach (var pane in new[] { l.Primary, l.Secondary })
            {
                if (pane == null) continue;
                foreach (var tab in pane.Tabs)
                    if (tab.Surface.Kind != SurfaceKind.Page && tab.Surface.Kind != SurfaceKind.WorldMap)
                    { Debug.LogError($"FAIL prune-all: a {tab.Surface.Kind}/{tab.Surface.Id} tab survived the prune"); ok = false; }
            }

            // ...and the survivors really are still there, so "nothing of that kind remains" was not achieved
            // by emptying the workspace.
            bool keptMap = false, keptPage = false;
            foreach (var tab in l.Primary.Tabs)
            {
                if (tab.Surface.Kind == SurfaceKind.WorldMap) keptMap = true;
                if (tab.Surface.Kind == SurfaceKind.Page && tab.Surface.Id == "notes") keptPage = true;
            }
            if (!keptMap || !keptPage)
            { Debug.LogError($"FAIL prune-all: survivors missing (worldMap={keptMap}, page={keptPage})"); ok = false; }

            // The secondary pane held only rejected tabs, so pruning them collapses the split (R3/R4 via
            // NormalizeSplit) rather than leaving an empty second pane on screen.
            if (l.Secondary != null)
            { Debug.LogError("FAIL prune-all: the secondary pane held only pruned tabs and must have collapsed"); ok = false; }

            if (l.Primary.ActiveIndex < 0 || l.Primary.ActiveIndex >= l.Primary.Tabs.Count)
            { Debug.LogError($"FAIL prune-all: ActiveIndex {l.Primary.ActiveIndex} is out of range for {l.Primary.Tabs.Count} tab(s)"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Prune Drops Every Match: PASS"
                         : "Self-Test Workspace Prune Drops Every Match: FAIL");
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

            // Task 10c added SurfaceKind.PoiEditor, and TryParseSurfaceKind is an EXPLICIT name switch (its own
            // doc says why: Enum.TryParse would accept a bare numeral). A new kind that nobody added a case for
            // therefore round-trips as a REFUSED payload — every tab in it silently dropped — rather than as a
            // compile error, so the new case needs pinning by name here.
            var poiEditor = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(poiEditor, new SurfaceRef { Kind = SurfaceKind.PoiEditor, Id = "poi-3" }, "Тихий Брод", false);
            if (!WorkspaceOps.TryDeserialize(WorkspaceOps.Serialize(poiEditor), out var poiBack) || poiBack == null
                || poiBack.Primary.Tabs.Count < 2
                || poiBack.Primary.Tabs[1].Surface.Kind != SurfaceKind.PoiEditor
                || poiBack.Primary.Tabs[1].Surface.Id != "poi-3")
            {
                string got = poiBack == null ? "<parse failed>"
                    : poiBack.Primary.Tabs.Count < 2 ? $"<only {poiBack.Primary.Tabs.Count} tab(s)>"
                    : $"{poiBack.Primary.Tabs[1].Surface.Kind}/{poiBack.Primary.Tabs[1].Surface.Id}";
                Debug.LogError($"FAIL persist: PoiEditor tab came back {got}, want PoiEditor/poi-3");
                ok = false;
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

        /// <summary>WorkspaceOps.Restore — the pipeline WorkspacePrefs' three callers share (Task 11). Pinned
        /// here rather than walked in the Editor because the three interesting outcomes are all invisible when
        /// they go wrong: a restore that silently keeps a malformed id, a restore that silently applies an
        /// EMPTY workspace, and a restore that drops a tab it should have kept all look like "persistence is
        /// a bit flaky" from the outside.</summary>
        [ContextMenu("Self-Test: Workspace Restore")]
        public void SelfTestRestore()
        {
            bool ok = true;
            const string Poi = "3f2a1c88-5b6d-4e21-9a70-0c1d2e3f4a5b";

            // ── A null `exists` restores VERBATIM (the app-start case) ───────────────
            var saved = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(saved, Page("a"), "Сессия 1", false);
            WorkspaceOps.Open(saved, new SurfaceRef { Kind = SurfaceKind.Settlement, Id = SurfaceIds.Interior(Poi, 0) }, "Тихий Брод", true);
            saved.SplitRatio = 0.62f;
            string payload = WorkspaceOps.Serialize(saved);

            var verbatim = WorkspaceOps.Restore(payload, null);
            if (verbatim == null)
            { Debug.LogError("FAIL restore: a valid payload with a null `exists` returned null, want the layout restored verbatim"); ok = false; }
            else
            {
                if (Dump(verbatim.Primary) != Dump(saved.Primary) || Dump(verbatim.Secondary) != Dump(saved.Secondary))
                { Debug.LogError($"FAIL restore: verbatim = [{Dump(verbatim.Primary)}] / [{Dump(verbatim.Secondary)}], want [{Dump(saved.Primary)}] / [{Dump(saved.Secondary)}]"); ok = false; }
                if (System.Math.Abs(verbatim.SplitRatio - 0.62f) > 0.001f)
                { Debug.LogError($"FAIL restore: SplitRatio {verbatim.SplitRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)}, want 0.62 — the settings line must survive the prune"); ok = false; }
            }

            // ── `exists` drops the tabs it rejects, and ONLY those ───────────────────
            // The settlement's target is gone; the page and the world map are still there. The page must NOT
            // go with it — a predicate applied per-tab is the difference between "one place was deleted" and
            // "the whole session is gone".
            var partial = WorkspaceOps.Restore(payload, s => s.Kind != SurfaceKind.Settlement);
            if (partial == null)
            { Debug.LogError("FAIL restore: dropping ONE tab returned null, want the surviving tabs restored"); ok = false; }
            else
            {
                if (partial.Secondary != null)
                { Debug.LogError($"FAIL restore: secondary = [{Dump(partial.Secondary)}], want null — emptying it must collapse the split"); ok = false; }
                if (Dump(partial.Primary) != "Карта мира,*Сессия 1")
                { Debug.LogError($"FAIL restore: survivors = [{Dump(partial.Primary)}], want [Карта мира,*Сессия 1]"); ok = false; }
            }

            // ── A MALFORMED id is dropped even though `exists` would have kept it ───
            // «<poi>#abc» is what a PoiData.Id carrying a '#' encodes to (SurfaceIds' own doc). `exists` here
            // says yes to everything, so the ONLY thing that can drop this tab is the well-formedness check —
            // which is exactly the mutant this assertion guards.
            var rotten = WorkspaceOps.NewDefault();
            WorkspaceOps.Open(rotten, new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = Poi + "#abc" }, "Пепельный Курган", false);
            var rottenBack = WorkspaceOps.Restore(WorkspaceOps.Serialize(rotten), _ => true);
            if (rottenBack == null)
            { Debug.LogError("FAIL restore: a payload with one malformed id returned null, want the OTHER tabs kept"); ok = false; }
            else if (Dump(rottenBack.Primary) != "*Карта мира")
            { Debug.LogError($"FAIL restore: malformed-id payload restored [{Dump(rottenBack.Primary)}], want [*Карта мира] — the bad tab dropped, the good one kept"); ok = false; }

            // ── Nothing usable left → null, NOT an empty workspace ──────────────────
            // NormalizeSplit repairs an empty Primary only by promoting a non-empty Secondary, so with both
            // panes emptied it leaves a legal ZERO-TAB layout — which is precisely what must never be applied
            // over whatever the caller is already showing.
            var everythingGone = WorkspaceOps.Restore(payload, _ => false);
            if (everythingGone != null)
            {
                Debug.LogError($"FAIL restore: a payload whose every tab was pruned returned a layout with " +
                               $"[{Dump(everythingGone.Primary)}] / [{Dump(everythingGone.Secondary)}], want null");
                ok = false;
            }

            // ── Junk in, null out — the same payloads TryDeserialize refuses ────────
            foreach (string junk in new[] { null, "", "мусор", "WORKSPACE/1\nnonsense" })
                if (WorkspaceOps.Restore(junk, null) != null)
                { Debug.LogError($"FAIL restore: «{(junk ?? "<null>").Replace("\n", "\\n")}» restored a layout, want null"); ok = false; }

            Debug.Log(ok ? "Self-Test Workspace Restore: PASS" : "Self-Test Workspace Restore: FAIL");
        }
    }
}
