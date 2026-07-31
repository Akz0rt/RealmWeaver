using System.Collections.Generic;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>Which kind of group a navigator entry is. World and Pinned are both COMPUTED groups — see
    /// NavigatorTree.Build. Authored covers every ordinary PageGroup the document happens to contain,
    /// including «Люди» and «Сессии»: those are plain user groups, not a second fixed kind, deliberately —
    /// see NavigatorTree's class comment. Pinned is the one hardcoded row (the world map) that renders
    /// ABOVE World, outside the Bound predicate entirely — see Build's own comment for why it does not
    /// merge into World instead.</summary>
    public enum NavGroupKind { World = 0, Authored = 1, Pinned = 2 }

    public class NavNode
    {
        public string Title;

        /// <summary>A Page surface pointing at the page id (N4) for every node EXCEPT the one Pinned node —
        /// the navigator otherwise opens pages, never world objects directly; opening the world object
        /// itself is a separate action elsewhere («Открыть город»), not something this tree does. The Pinned
        /// node is the deliberate exception (see Build's own comment): there is no page behind the world map,
        /// so its Target names the world map surface directly.</summary>
        public SurfaceRef Target;
    }

    public class NavGroup
    {
        public NavGroupKind Kind;
        public string Title;

        /// <summary>The backing PageGroup's id for an Authored group — carried through so a caller can
        /// rename/delete the group itself (NotesDocumentController.RenameGroup/DeleteGroup) without
        /// re-deriving it by title. Empty for World AND Pinned: both are computed (N1 / Build's Pinned
        /// comment), not a stored PageGroup, so there is nothing for an id to name in either case.</summary>
        public string Id = "";

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
            string needle = (filter ?? "").Trim().ToLowerInvariant();

            // Pinned — P1: ONE hardcoded row, the world map, always first when it survives the filter.
            // Deliberately NOT folded into Мир below: Мир's only membership rule is "p.Bound != null" (N1),
            // scanned fresh off the document every call — see that group's own comment, "this one place is
            // where 'what is a member' lives". A hardcoded head node is exactly the stored-membership
            // exception that rule forbids, so the world map gets its OWN group instead (kind Pinned, empty
            // Title/Id — there is no PageGroup behind it, the same reason Мир's own Id is empty).
            //
            // Built BEFORE the `doc == null` guard below, deliberately: unlike every other group in this
            // method, the Pinned row names the world map, not anything derived from `doc` — so it must not
            // be gated on a document existing. Gating it there (an earlier version of this method did) put
            // the round's own defect back in a different shape: a scene where nothing has wired a
            // NotesDocumentController yet (or, upstream of that, wherever WorkspaceBuilder.EnsureDocument
            // Controller's discovery finds nothing) would lose the one row whose entire job is "the map must
            // be reachable from something other than its default tab". NavigatorTreeSelfTests pins this with
            // `Build(null, "")` returning exactly the Pinned group.
            //
            // The SurfaceRef here must stay byte-identical to WorkspaceOps.NewDefault's own seed tab
            // (WorkspaceOps.cs: Kind=WorldMap, Id="") — see WorkspaceOps.SameSurface. A merely-equal-LOOKING
            // ref (e.g. a different Id) would make WorkspaceOps.Open create a SECOND world-map tab instead
            // of focusing the one NewDefault already opened, which is exactly the "map become unreachable"
            // defect this group exists to fix, just relocated. NavigatorTreeSelfTests pins this by opening
            // the target against a fresh NewDefault layout and asserting the tab count stays 1.
            //
            // Obeys N3 (the same Matches filter every other node uses) and, like every other group here, is
            // omitted entirely rather than shown empty when it doesn't survive the filter.
            var pinned = new NavGroup { Kind = NavGroupKind.Pinned, Title = "", Id = "" };
            if (Matches(WorkspaceOps.DefaultWorldMapTitle, needle))
                pinned.Nodes.Add(new NavNode
                {
                    Title = WorkspaceOps.DefaultWorldMapTitle,
                    Target = new SurfaceRef { Kind = SurfaceKind.WorldMap, Id = "" },
                });
            if (pinned.Nodes.Count > 0) groups.Add(pinned);

            // Everything below IS document-derived (Мир scans doc.Groups directly, Authored mirrors
            // doc.Groups as-is), so the null guard belongs HERE, after Pinned, not before it.
            if (doc == null) return groups;

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
                var authored = new NavGroup { Kind = NavGroupKind.Authored, Title = g.Title, Id = g.Id };
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
