using System.Collections.Generic;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>Which kind of group a navigator entry is. World is the one COMPUTED group — see
    /// NavigatorTree.Build. Authored covers every ordinary PageGroup the document happens to contain,
    /// including «Люди» and «Сессии»: those are plain user groups, not a second fixed kind, deliberately —
    /// see NavigatorTree's class comment.</summary>
    public enum NavGroupKind { World = 0, Authored = 1 }

    public class NavNode
    {
        public string Title;

        /// <summary>Always a Page surface pointing at the page id (N4) — the navigator opens pages, never
        /// world objects directly. Opening the world object itself is a separate action elsewhere («Открыть
        /// город»), not something this tree does.</summary>
        public SurfaceRef Target;
    }

    public class NavGroup
    {
        public NavGroupKind Kind;
        public string Title;
        public List<NavNode> Nodes = new List<NavNode>();
    }

    /// <summary>
    /// Builds the navigator tree fresh from a NotesDocument on every call. Nothing here, or anywhere else,
    /// stores tree membership: the «Мир» group is computed by scanning every page for Bound != null (N1),
    /// and that is the ONLY predicate that decides membership — no "visited" flag, no recency list. That is
    /// deliberate: it keeps the rule for what appears in Мир changeable later by editing one predicate.
    ///
    /// Deliberate narrowing from the umbrella spec: an earlier draft spoke of three fixed groups
    /// «МИР · ЛЮДИ · СЕССИИ». Only Мир is actually computed here — «Люди» and «Сессии» are ordinary
    /// PageGroups the default document happens to ship with, and render through NavGroupKind.Authored like
    /// any other group. There is no page-type or classification mechanism; the design explicitly refuses to
    /// have one.
    ///
    /// Free of any UnityEngine reference, the same arrangement WorkspaceOps and NotesDocOps use, so this
    /// runs in Tools/notes-harness without an Editor.
    /// </summary>
    public static class NavigatorTree
    {
        public const string WorldGroupTitle = "Мир";

        /// <summary>N3: filter matches on title with Trim().ToLowerInvariant().Contains; an empty filter
        /// matches everything; a group left with no surviving nodes is omitted entirely, never shown empty.</summary>
        public static List<NavGroup> Build(NotesDocument doc, string filter)
        {
            var groups = new List<NavGroup>();
            if (doc == null) return groups;

            string needle = (filter ?? "").Trim().ToLowerInvariant();

            // Мир — N1: computed from Bound alone, in document order. A separate top-to-bottom scan rather
            // than folded into the Authored loop below, so this one place is where "what is a member" lives.
            var world = new NavGroup { Kind = NavGroupKind.World, Title = WorldGroupTitle };
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Bound != null && Matches(p.Name, needle))
                        world.Nodes.Add(MakeNode(p));
            if (world.Nodes.Count > 0) groups.Add(world);

            // Authored — N2: every stored group renders as-is, in stored order, with its stored pages. This
            // loop never consults Bound, which is exactly what lets a bound page appear in Мир AND here.
            foreach (var g in doc.Groups)
            {
                var authored = new NavGroup { Kind = NavGroupKind.Authored, Title = g.Title };
                foreach (var p in g.Pages)
                    if (Matches(p.Name, needle))
                        authored.Nodes.Add(MakeNode(p));
                if (authored.Nodes.Count > 0) groups.Add(authored);
            }

            return groups;
        }

        static bool Matches(string title, string needle)
            => needle.Length == 0 || (title ?? "").Trim().ToLowerInvariant().Contains(needle);

        static NavNode MakeNode(NotesPage p)
            => new NavNode { Title = p.Name, Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = p.Id } };
    }
}
