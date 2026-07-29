using System;

namespace WorldGen.Workspace.Data
{
    /// <summary>
    /// Every operation on the pane/tab layout, as pure functions over WorkspaceLayout. Deliberately free of
    /// any UnityEngine reference, following the arrangement NotesDocOps established — the whole layer runs in
    /// Tools/notes-harness without an Editor.
    ///
    /// The two panes are asymmetric only in that Primary always exists and Secondary may be null (no split).
    /// Every op that can leave a pane empty ends by calling NormalizeSplit, so "an empty Secondary means no
    /// split" (R3) and "there is never an empty Primary beside a full Secondary" (R4) are each enforced in
    /// exactly one place, never re-checked at individual call sites.
    /// </summary>
    public static class WorkspaceOps
    {
        public const string DefaultWorldMapTitle = "Карта мира";

        const float DefaultSplitRatio = 0.5f;
        const float DefaultNavigatorWidth = 236f;

        // ── Lookup ─────────────────────────────────────────────────────────────

        /// <summary>Two refs name the same surface when Kind and Id match. The world map's Id is always
        /// empty, so two WorldMap refs are always the same surface.</summary>
        public static bool SameSurface(SurfaceRef a, SurfaceRef b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            return a.Kind == b.Kind && (a.Id ?? "") == (b.Id ?? "");
        }

        /// <summary>0 = Primary, 1 = Secondary, anything else = an absent pane. Null for pane 1 whenever
        /// there is no split — the ONE place every op asks "does this pane exist".</summary>
        public static PaneState PaneAt(WorkspaceLayout l, int pane)
        {
            if (l == null) return null;
            if (pane == 0) return l.Primary;
            if (pane == 1) return l.Secondary;
            return null;
        }

        static int IndexOfSurface(PaneState p, SurfaceRef s)
        {
            if (p?.Tabs == null) return -1;
            for (int i = 0; i < p.Tabs.Count; i++)
                if (SameSurface(p.Tabs[i].Surface, s)) return i;
            return -1;
        }

        static int OtherPane(int pane) => pane == 0 ? 1 : 0;

        // ── Creation ───────────────────────────────────────────────────────────

        /// <summary>One pane holding a single WorldMap tab, active, focused, at the default split ratio and
        /// navigator width — what a brand-new session (or a workspace file that predates this layer) opens
        /// into.</summary>
        public static WorkspaceLayout NewDefault()
        {
            var primary = new PaneState();
            primary.Tabs.Add(new TabState
            {
                Surface = new SurfaceRef { Kind = SurfaceKind.WorldMap, Id = "" },
                Title = DefaultWorldMapTitle,
            });
            primary.ActiveIndex = 0;

            return new WorkspaceLayout
            {
                Primary = primary,
                Secondary = null,
                FocusedPane = 0,
                SplitRatio = DefaultSplitRatio,
                NavigatorCollapsed = false,
                NavigatorWidth = DefaultNavigatorWidth,
            };
        }

        // ── Open / close / move ───────────────────────────────────────────────

        /// <summary>Opens a surface as a tab. Without inOtherPane it lands in whichever pane currently has
        /// focus. WITH it, it lands in the OTHER pane instead, creating Secondary first if the workspace was
        /// not yet split (R2). Either way the target pane ends up focused — for a plain open that is a no-op,
        /// since the target already was the focused pane.</summary>
        public static void Open(WorkspaceLayout l, SurfaceRef s, string title, bool inOtherPane)
        {
            if (l == null || s == null) return;

            int target = inOtherPane ? OtherPane(l.FocusedPane) : l.FocusedPane;
            if (target != 0 && target != 1) target = 0;

            PaneState pane = PaneAt(l, target);
            if (pane == null)
            {
                pane = new PaneState();
                if (target == 1) l.Secondary = pane; else l.Primary = pane;
            }

            int existing = IndexOfSurface(pane, s);
            if (existing >= 0)
            {
                // R1 — reopening a surface already open in the TARGET pane activates it; it never duplicates.
                pane.ActiveIndex = existing;
            }
            else
            {
                pane.Tabs.Add(new TabState { Surface = s, Title = title ?? "" });
                pane.ActiveIndex = pane.Tabs.Count - 1;
            }

            l.FocusedPane = target;
        }

        public static bool CloseTab(WorkspaceLayout l, int pane, int index)
        {
            if (l == null) return false;
            PaneState p = PaneAt(l, pane);
            if (p?.Tabs == null || index < 0 || index >= p.Tabs.Count) return false;

            p.Tabs.RemoveAt(index);
            FixActiveIndexAfterRemoval(p, index);
            NormalizeSplit(l);
            return true;
        }

        /// <summary>Moves one tab, possibly across panes. Creates the destination pane the same way Open's
        /// inOtherPane does if it does not exist yet. The moved tab becomes the active tab wherever it lands
        /// — a tab you just dragged is the one you meant to look at.</summary>
        public static bool MoveTab(WorkspaceLayout l, int fromPane, int fromIndex, int toPane, int toIndex)
        {
            if (l == null) return false;
            if (toPane != 0 && toPane != 1) return false;

            PaneState from = PaneAt(l, fromPane);
            if (from?.Tabs == null || fromIndex < 0 || fromIndex >= from.Tabs.Count) return false;

            PaneState to = PaneAt(l, toPane);
            if (to == null)
            {
                to = new PaneState();
                if (toPane == 1) l.Secondary = to; else l.Primary = to;
            }

            var tab = from.Tabs[fromIndex];
            bool samePane = ReferenceEquals(from, to);

            from.Tabs.RemoveAt(fromIndex);
            FixActiveIndexAfterRemoval(from, fromIndex);

            int insertAt = toIndex;
            if (samePane && fromIndex < toIndex) insertAt--;   // the removal already shifted this pane left
            if (insertAt < 0) insertAt = 0;
            if (insertAt > to.Tabs.Count) insertAt = to.Tabs.Count;

            to.Tabs.Insert(insertAt, tab);
            to.ActiveIndex = insertAt;

            NormalizeSplit(l);
            return true;
        }

        /// <summary>Focusing a pane that does not exist is ignored rather than leaving FocusedPane dangling
        /// at an index PaneAt would return null for (R6).</summary>
        public static void Focus(WorkspaceLayout l, int pane)
        {
            if (l == null) return;
            if (PaneAt(l, pane) == null) return;
            l.FocusedPane = pane;
        }

        /// <summary>Drops every tab whose surface no longer exists (a deleted page, settlement, etc.),
        /// across both panes, and reports how many were dropped. May collapse the split via R3/R4, through
        /// the same NormalizeSplit every other structural op uses.</summary>
        public static int PruneMissing(WorkspaceLayout l, Func<SurfaceRef, bool> exists)
        {
            if (l == null || exists == null) return 0;

            int dropped = PruneStale(l.Primary, exists) + PruneStale(l.Secondary, exists);
            NormalizeSplit(l);
            return dropped;
        }

        static int PruneStale(PaneState p, Func<SurfaceRef, bool> exists)
        {
            if (p?.Tabs == null) return 0;
            int dropped = 0;
            for (int i = p.Tabs.Count - 1; i >= 0; i--)
            {
                if (exists(p.Tabs[i].Surface)) continue;
                p.Tabs.RemoveAt(i);
                FixActiveIndexAfterRemoval(p, i);
                dropped++;
            }
            return dropped;
        }

        // ── Invariants ─────────────────────────────────────────────────────────

        /// <summary>Keeps ActiveIndex pointing at a real tab (or -1 for none) after a removal at
        /// removedIndex. Shared by CloseTab, MoveTab and PruneMissing so "what the active tab becomes when
        /// its neighbours change" has exactly one definition (R5).</summary>
        static void FixActiveIndexAfterRemoval(PaneState p, int removedIndex)
        {
            if (p.Tabs.Count == 0) { p.ActiveIndex = -1; return; }
            if (removedIndex < p.ActiveIndex) p.ActiveIndex--;
            else if (p.ActiveIndex >= p.Tabs.Count) p.ActiveIndex = p.Tabs.Count - 1;
        }

        /// <summary>The single place R3 and R4 are enforced: an empty Secondary collapses the split (focus
        /// falls back to Primary), and an empty Primary beside a non-empty Secondary is repaired by promoting
        /// Secondary into Primary's place — never the reverse, so there is never an empty left pane beside a
        /// full right one.</summary>
        static void NormalizeSplit(WorkspaceLayout l)
        {
            bool primaryEmpty = l.Primary?.Tabs == null || l.Primary.Tabs.Count == 0;
            bool secondaryEmpty = l.Secondary?.Tabs == null || l.Secondary.Tabs.Count == 0;

            if (primaryEmpty && l.Secondary != null && !secondaryEmpty)
            {
                l.Primary = l.Secondary;      // R4 — promote rather than leave a hole on the left
                l.Secondary = null;
            }
            else if (secondaryEmpty && l.Secondary != null)
            {
                l.Secondary = null;           // R3 — collapse the split
            }

            if (l.Secondary == null) l.FocusedPane = 0;
        }
    }
}
