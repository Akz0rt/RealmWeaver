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
    /// break (N1..N4 in the plan), not a derived summary number.
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

            // N4 — nodes target PAGES.
            groups = NavigatorTree.Build(doc, "");
            foreach (var g in groups)
                foreach (var n in g.Nodes)
                    if (n.Target.Kind != SurfaceKind.Page || n.Target.Id != PageIdOf(doc, n.Title))
                    { Debug.LogError($"FAIL tree: node «{n.Title}» targets {n.Target.Kind}/{n.Target.Id}, want Page/{PageIdOf(doc, n.Title)} (N4)"); ok = false; }

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
