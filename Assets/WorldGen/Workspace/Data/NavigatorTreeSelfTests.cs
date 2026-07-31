using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>
    /// Self-tests for the computed navigator tree. Runs two ways: right-click this component in the
    /// Editor, or offline via Tools/notes-harness (see WorkspaceOpsSelfTests for the exact commands), which
    /// compiles these very sources against UnityEngine stubs.
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would
    /// break (N1..N5, P1 in the plan), not a derived summary number.
    /// </summary>
    public class NavigatorTreeSelfTests : MonoBehaviour
    {
        static NotesDocument Fixture(out NotesPage bound, out NotesPage plain)
        {
            var doc = new NotesDocument();
            var sessions = new PageGroup { Title = "Сессии" };
            plain = new NotesPage { Name = "Сессия 1" };
            sessions.Pages.Add(plain);
            doc.Groups.Add(sessions);

            var world = new PageGroup { Title = "Места" };
            bound = new NotesPage { Name = "Тихий Брод", Bound = new WorldRef { Kind = WorldRefKind.Poi, Id = "poi-1" } };
            world.Pages.Add(bound);
            doc.Groups.Add(world);
            return doc;
        }

        static string PageIdOf(NotesDocument doc, string title)
        {
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Name == title) return p.Id;
            return null;
        }

        static string PageGroupIdOf(NotesDocument doc, string title)
        {
            foreach (var g in doc.Groups)
                if (g.Title == title) return g.Id;
            return null;
        }

        [ContextMenu("Self-Test: Navigator Tree")]
        public void SelfTestTree()
        {
            bool ok = true;
            var doc = Fixture(out var bound, out var plain);

            var groups = NavigatorTree.Build(doc, "");

            // P1 — the pinned world-map row is FIRST, ahead of Мир and every Authored group. Checked by
            // POSITION (groups[0]), not just "a Pinned group exists somewhere" — a mutant that appends it
            // last would pass the weaker check and still leave the map buried below «Сессии»/«Места».
            if (groups.Count == 0 || groups[0].Kind != NavGroupKind.Pinned)
            {
                string actual = groups.Count == 0 ? "no groups" : $"groups[0].Kind = {groups[0].Kind}";
                Debug.LogError($"FAIL tree: {actual}, want groups[0] to be the Pinned group (P1)");
                ok = false;
            }
            else
            {
                var pinnedGroup = groups[0];
                if (pinnedGroup.Nodes.Count != 1 || pinnedGroup.Nodes[0].Title != WorkspaceOps.DefaultWorldMapTitle)
                {
                    string actual = pinnedGroup.Nodes.Count == 0 ? "0 nodes"
                        : $"{pinnedGroup.Nodes.Count} node(s), first «{pinnedGroup.Nodes[0].Title}»";
                    Debug.LogError($"FAIL tree: Pinned group = [{actual}], want 1 node «{WorkspaceOps.DefaultWorldMapTitle}» (P1)");
                    ok = false;
                }
                else
                {
                    var pinnedTarget = pinnedGroup.Nodes[0].Target;
                    if (pinnedTarget == null || pinnedTarget.Kind != SurfaceKind.WorldMap || pinnedTarget.Id != "")
                    {
                        Debug.LogError($"FAIL tree: Pinned node targets {pinnedTarget?.Kind}/«{pinnedTarget?.Id}», want WorldMap/«» (P1)");
                        ok = false;
                    }

                    // P1's REAL point, not just "the fields look right in isolation": this ref must be
                    // byte-identical to WorkspaceOps.NewDefault's own seed tab, or WorkspaceOps.Open creates
                    // a SECOND world-map tab instead of focusing the one already open (SameSurface compares
                    // Kind AND Id). Opening the pinned target against a fresh NewDefault layout — which
                    // already holds exactly that seed tab — must leave the tab count at 1, not grow it to 2.
                    // This is the assertion an Id of "x" (instead of "") or a Kind of Page (instead of
                    // WorldMap) actually fails; the field checks above alone would not catch every mismatch
                    // SameSurface cares about with equal certainty.
                    var freshLayout = WorkspaceOps.NewDefault();
                    WorkspaceOps.Open(freshLayout, pinnedTarget, pinnedGroup.Nodes[0].Title, false);
                    if (freshLayout.Primary == null || freshLayout.Primary.Tabs.Count != 1)
                    {
                        int actualCount = freshLayout.Primary?.Tabs.Count ?? -1;
                        Debug.LogError($"FAIL tree: opening the Pinned target against NewDefault left {actualCount} tab(s), want 1 — the ref must match NewDefault's seed tab exactly (P1)");
                        ok = false;
                    }
                }
            }

            // P1, Мир isolation — the pinned node must NOT also appear inside Мир: that group's membership is
            // Bound-only (N1), and hardcoding a head node into it is exactly the stored-membership exception
            // its own comment forbids.
            if (groups.Exists(g => g.Kind == NavGroupKind.World && g.Nodes.Exists(n => n.Title == WorkspaceOps.DefaultWorldMapTitle)))
            { Debug.LogError("FAIL tree: the world-map row leaked into Мир, want it ONLY in the Pinned group (P1)"); ok = false; }

            // P1, doc-independence (checkpoint3-review.md Important 1): the Pinned row names the world map,
            // not anything derived from `doc` — so Build(null, ...) must still return it. Losing this row on
            // a null document would be the round's own "map unreachable" defect, relocated to whatever path
            // leaves NavigatorView with no document wired (e.g. WorkspaceBuilder.EnsureDocumentController's
            // discovery finding nothing before Task 9's wiring runs). Checked as EXACTLY one group, not just
            // "contains a Pinned group somewhere" — a mutant that also (wrongly) let Мир/Authored survive a
            // null doc would slip past a weaker "Exists" check.
            var nullDocGroups = NavigatorTree.Build(null, "");
            if (nullDocGroups.Count != 1 || nullDocGroups[0].Kind != NavGroupKind.Pinned)
            {
                string actual = nullDocGroups.Count == 0 ? "no groups"
                    : $"{nullDocGroups.Count} group(s), first Kind={nullDocGroups[0].Kind}";
                Debug.LogError($"FAIL tree: Build(null, \"\") = [{actual}], want exactly 1 group, Kind=Pinned (P1, doc-independence)");
                ok = false;
            }
            else if (nullDocGroups[0].Nodes.Count != 1 || nullDocGroups[0].Nodes[0].Title != WorkspaceOps.DefaultWorldMapTitle)
            {
                Debug.LogError($"FAIL tree: Build(null, \"\")'s Pinned group has {nullDocGroups[0].Nodes.Count} node(s), want 1 «{WorkspaceOps.DefaultWorldMapTitle}» (P1, doc-independence)");
                ok = false;
            }

            var world = groups.Find(g => g.Kind == NavGroupKind.World);
            if (world == null || world.Nodes.Count != 1 || world.Nodes[0].Title != "Тихий Брод")
            {
                string actual = world == null ? "no Мир group"
                    : world.Nodes.Count == 0 ? "0 nodes"
                    : $"{world.Nodes.Count} node(s), first «{world.Nodes[0].Title}»";
                Debug.LogError($"FAIL tree: Мир = [{actual}], want 1 node «Тихий Брод» (N1)");
                ok = false;
            }

            // N2 — a bound page still shows in its authored group as well.
            var authored = groups.FindAll(g => g.Kind == NavGroupKind.Authored);
            if (authored.Count != 2)
            { Debug.LogError($"FAIL tree: {authored.Count} authored groups, want 2 (N2)"); ok = false; }

            // N1 again, from the other side: unbinding removes it from МИР with no other change.
            bound.Bound = null;
            world = NavigatorTree.Build(doc, "").Find(g => g.Kind == NavGroupKind.World);
            if (world != null && world.Nodes.Count != 0)
            { Debug.LogError($"FAIL tree: Мир after unbind = {world.Nodes.Count} node(s), want 0 (N1)"); ok = false; }

            // N3 — filtering folds case, and empties whole groups away.
            doc = Fixture(out bound, out plain);
            groups = NavigatorTree.Build(doc, "  ТИХИЙ  ");
            if (groups.Exists(g => g.Nodes.Exists(n => n.Title == "Сессия 1")))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » left «Сессия 1» present, want it excluded (N3)"); ok = false; }
            if (groups.Exists(g => g.Nodes.Count == 0))
            {
                string emptyTitle = groups.Find(g => g.Nodes.Count == 0)?.Title ?? "?";
                Debug.LogError($"FAIL tree: group «{emptyTitle}» survived with 0 nodes, want it omitted entirely (N3)");
                ok = false;
            }

            // N3, from the positive side: the two checks above pass VACUOUSLY if case-folding is broken and
            // the filter ends up matching nothing at all (an empty `groups` excludes both "Сессия 1" and
            // every empty group trivially). Require the actual match to have happened.
            if (!groups.Exists(g => g.Nodes.Exists(n => n.Title == "Тихий Брод")))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » matched 0 pages named «Тихий Брод», want 1 (N3)"); ok = false; }

            // P1 obeys N3 too: a filter that doesn't match «Карта мира» omits the Pinned group entirely
            // (never shown empty, same as every other group), and one that does still surfaces it.
            var pinnedNoMatch = NavigatorTree.Build(doc, "зюзюка");
            if (pinnedNoMatch.Exists(g => g.Kind == NavGroupKind.Pinned))
            { Debug.LogError("FAIL tree: filter «зюзюка» still produced a Pinned group, want it omitted — N3 must apply to the pinned node too (P1)"); ok = false; }

            var pinnedDoesMatch = NavigatorTree.Build(doc, "карта");
            if (!pinnedDoesMatch.Exists(g => g.Kind == NavGroupKind.Pinned && g.Nodes.Exists(n => n.Title == WorkspaceOps.DefaultWorldMapTitle)))
            { Debug.LogError("FAIL tree: filter «карта» dropped the Pinned group, want it to still match «Карта мира» (P1)"); ok = false; }

            // N4 — nodes target PAGES. The Pinned group is excluded from this loop on purpose: its one node
            // deliberately targets the world map, not a page (see NavGroup construction in Build) — folding
            // it into N4 would make a CORRECT implementation fail this check, not a broken one. P1's own
            // checks above already pin that node's exact target shape.
            groups = NavigatorTree.Build(doc, "");
            foreach (var g in groups)
            {
                if (g.Kind == NavGroupKind.Pinned) continue;
                foreach (var n in g.Nodes)
                    if (n.Target.Kind != SurfaceKind.Page || n.Target.Id != PageIdOf(doc, n.Title))
                    { Debug.LogError($"FAIL tree: node «{n.Title}» targets {n.Target.Kind}/{n.Target.Id}, want Page/{PageIdOf(doc, n.Title)} (N4)"); ok = false; }
            }

            // N5 — an Authored group carries its backing PageGroup's id (so a caller can rename/delete the
            // group without re-deriving it by title); the computed Мир group carries none, since there is no
            // PageGroup behind it. The positive check (matching the REAL id, not just "non-empty") is what
            // catches a mutant that populates Id with some other stand-in value instead of g.Id.
            var sessionsGroup = groups.Find(g => g.Kind == NavGroupKind.Authored && g.Title == "Сессии");
            string wantSessionsId = PageGroupIdOf(doc, "Сессии");
            if (sessionsGroup == null || sessionsGroup.Id != wantSessionsId)
            {
                string actual = sessionsGroup == null ? "no «Сессии» group" : $"Id=«{sessionsGroup.Id}»";
                Debug.LogError($"FAIL tree: authored group «Сессии» = [{actual}], want Id=«{wantSessionsId}» (N5)");
                ok = false;
            }
            var worldGroup = groups.Find(g => g.Kind == NavGroupKind.World);
            if (worldGroup != null && worldGroup.Id != "")
            { Debug.LogError($"FAIL tree: Мир group Id = «{worldGroup.Id}», want empty (N5)"); ok = false; }

            Debug.Log(ok ? "Self-Test Navigator Tree: PASS" : "Self-Test Navigator Tree: FAIL");
        }
    }
}
