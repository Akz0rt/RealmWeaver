using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WorldGen.Notes.Data;

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

        /// <summary>Where a surface is currently open, as (pane, index), or (-1, -1) when it is open nowhere.
        /// Task 10c needs it because every Close* path in MapScreenController holds a SurfaceRef and nothing
        /// else — the tab it wants to close was opened arbitrarily long ago, possibly in either pane, possibly
        /// with other tabs opened and closed around it since, so there is no index to have remembered.
        ///
        /// Defers to the SAME IndexOfSurface (and therefore the same SameSurface) that Open's
        /// reopen-does-not-duplicate rule uses — R1 and "close the tab I opened" have to agree about what
        /// counts as the same surface, or a Close could miss the very tab a preceding Open had focused.
        ///
        /// PRIMARY FIRST, and the first match wins. A surface CAN be open in both panes at once (Open only
        /// dedupes within its TARGET pane — see R1's own wording), so "where is it" genuinely has two answers
        /// sometimes; picking Primary deterministically beats an ordering that depends on FocusedPane, because
        /// a Close that closed a different tab depending on which pane happened to be focused would be
        /// unpredictable in exactly the situation the user is least able to reason about.</summary>
        public static bool FindSurface(WorkspaceLayout l, SurfaceRef s, out int pane, out int index)
        {
            pane = -1;
            index = -1;
            if (l == null || s == null) return false;

            for (int p = 0; p <= 1; p++)
            {
                int i = IndexOfSurface(PaneAt(l, p), s);
                if (i < 0) continue;
                pane = p;
                index = i;
                return true;
            }
            return false;
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

        /// <summary>Activates a tab within its pane. Same validation shape as CloseTab: an absent pane or an
        /// out-of-range index mutates nothing and returns false. Unlike CloseTab, no tab is removed, so
        /// neither FixActiveIndexAfterRemoval nor NormalizeSplit applies here — this call cannot make a pane
        /// empty or unbalance the split, only move which of its existing tabs is active. Returns false (no
        /// mutation) when index already equals ActiveIndex, so callers can use the return value to decide
        /// whether anything actually changed.</summary>
        public static bool SetActiveTab(WorkspaceLayout l, int pane, int index)
        {
            if (l == null) return false;
            PaneState p = PaneAt(l, pane);
            if (p?.Tabs == null || index < 0 || index >= p.Tabs.Count) return false;
            if (index == p.ActiveIndex) return false;

            p.ActiveIndex = index;
            return true;
        }

        /// <summary>Moves one tab, possibly across panes. Creates the destination pane the same way Open's
        /// inOtherPane does if it does not exist yet. The moved tab becomes the active tab wherever it lands
        /// — a tab you just dragged is the one you meant to look at.
        ///
        /// `toIndex` IS A PRE-REMOVAL INDEX: an index into the destination pane's tab list as it looks when
        /// the call is made, INCLUDING the tab being moved when the destination is its own pane. The
        /// insertAt-- below is what reconciles that with the removal, and it is the only place that
        /// adjustment may happen — a caller that also subtracts one cancels every forward same-pane reorder
        /// into a no-op. Stated here because the shape is genuinely ambiguous and has already been
        /// misremembered once (Task 10d's brief asserted the opposite): the drag gesture computes its index
        /// by walking a strip that still contains the dragged tab, so pre-removal is the index a UI naturally
        /// has, and WorkspaceOpsSelfTests.SelfTestMoveTabDrop pins both no-op forms against it.</summary>
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

        // ── Persistence ────────────────────────────────────────────────────────

        const string PersistHeader = "WORKSPACE/1";
        const int TabFieldCount = 5;        // pane, kind, id, active, title
        const int SettingsFieldCount = 4;   // focusedPane, splitRatio, navigatorCollapsed, navigatorWidth

        const float MinSplitRatio = 0.25f;
        const float MaxSplitRatio = 0.75f;
        const float MinNavigatorWidth = 160f;
        const float MaxNavigatorWidth = 420f;

        /// <summary>Public (not just used by TryDeserialize on read): the Rendering-layer drag handle that
        /// live-moves SplitRatio is "whatever op moves it" per the field's own doc comment on WorkspaceLayout,
        /// and must clamp with these exact bounds. Promoted rather than duplicated as a second literal
        /// 0.25/0.75 pair in Unity code — the same reasoning Task 2 used to promote NotesDocOps.Escape instead
        /// of writing a second escaper.</summary>
        public static float ClampSplitRatio(float v) => v < MinSplitRatio ? MinSplitRatio : (v > MaxSplitRatio ? MaxSplitRatio : v);
        static float ClampNavigatorWidth(float v) => v < MinNavigatorWidth ? MinNavigatorWidth : (v > MaxNavigatorWidth ? MaxNavigatorWidth : v);

        /// <summary>Hand-rolled tab-separated shape, same reason and same escaper as NotesDocOps.SerializeBlocks:
        /// this layer must stay free of Newtonsoft to keep compiling in the offline harness, which stubs only
        /// Newtonsoft's attributes. Header, then one line per tab (both panes, Primary first) reading
        /// pane/kind/id/active/title, then a trailing settings line reading
        /// focusedPane/splitRatio/navigatorCollapsed/navigatorWidth. "active" is a per-TAB flag (which tab in
        /// its pane is the active one), not an index — mirroring "one line per tab."</summary>
        public static string Serialize(WorkspaceLayout l)
        {
            if (l == null) return "";

            var sb = new StringBuilder();
            sb.Append(PersistHeader);
            AppendPaneLines(sb, l.Primary, 0);
            AppendPaneLines(sb, l.Secondary, 1);

            sb.Append('\n')
              .Append(l.FocusedPane.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(l.SplitRatio.ToString("0.####", CultureInfo.InvariantCulture)).Append('\t')
              .Append(l.NavigatorCollapsed ? '1' : '0').Append('\t')
              .Append(l.NavigatorWidth.ToString("0.####", CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        static void AppendPaneLines(StringBuilder sb, PaneState p, int paneIndex)
        {
            if (p?.Tabs == null) return;
            for (int i = 0; i < p.Tabs.Count; i++)
            {
                var t = p.Tabs[i];
                sb.Append('\n')
                  .Append(paneIndex).Append('\t')
                  .Append(t.Surface.Kind).Append('\t')
                  .Append(NotesDocOps.Escape(t.Surface.Id)).Append('\t')
                  .Append(i == p.ActiveIndex ? '1' : '0').Append('\t')
                  .Append(NotesDocOps.Escape(t.Title));
            }
        }

        /// <summary>Parses a payload written by Serialize. Returns false on anything malformed rather than
        /// throwing — this comes from PlayerPrefs, a plain user-writable file that anything could have written.
        /// SplitRatio and NavigatorWidth are clamped HERE, on read, rather than trusted from storage — see the
        /// comments on WorkspaceLayout.SplitRatio/NavigatorWidth. A stray FocusedPane == 1 with no surviving
        /// Secondary is likewise corrected back to 0, the same "never a dangling pane reference" guarantee
        /// Focus() enforces at runtime.</summary>
        public static bool TryDeserialize(string payload, out WorkspaceLayout layout)
        {
            layout = null;
            if (string.IsNullOrEmpty(payload)) return false;

            string[] lines = payload.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0] != PersistHeader) return false;

            string[] settingsFields = lines[lines.Length - 1].Split('\t');
            if (settingsFields.Length != SettingsFieldCount) return false;

            if (!int.TryParse(settingsFields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int focusedPane))
                return false;
            if (!float.TryParse(settingsFields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float splitRatio))
                return false;
            bool navigatorCollapsed = settingsFields[2] == "1";
            if (!float.TryParse(settingsFields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float navigatorWidth))
                return false;

            var primaryTabs = new List<TabState>();
            var secondaryTabs = new List<TabState>();
            int primaryActive = -1;
            int secondaryActive = -1;

            for (int i = 1; i < lines.Length - 1; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] f = lines[i].Split('\t');
                if (f.Length != TabFieldCount) return false;

                if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pane) || (pane != 0 && pane != 1))
                    return false;
                if (!TryParseSurfaceKind(f[1], out SurfaceKind kind))
                    return false;

                var tab = new TabState
                {
                    Surface = new SurfaceRef { Kind = kind, Id = NotesDocOps.Unescape(f[2]) ?? "" },
                    Title = NotesDocOps.Unescape(f[4]) ?? "",
                };
                bool active = f[3] == "1";

                if (pane == 0) { primaryTabs.Add(tab); if (active) primaryActive = primaryTabs.Count - 1; }
                else { secondaryTabs.Add(tab); if (active) secondaryActive = secondaryTabs.Count - 1; }
            }

            var primary = new PaneState
            {
                Tabs = primaryTabs,
                ActiveIndex = primaryTabs.Count == 0 ? -1 : (primaryActive >= 0 ? primaryActive : 0),
            };
            PaneState secondary = null;
            if (secondaryTabs.Count > 0)
            {
                secondary = new PaneState
                {
                    Tabs = secondaryTabs,
                    ActiveIndex = secondaryActive >= 0 ? secondaryActive : 0,
                };
            }

            layout = new WorkspaceLayout
            {
                Primary = primary,
                Secondary = secondary,
                FocusedPane = (focusedPane == 1 && secondary != null) ? 1 : 0,
                SplitRatio = ClampSplitRatio(splitRatio),
                NavigatorCollapsed = navigatorCollapsed,
                NavigatorWidth = ClampNavigatorWidth(navigatorWidth),
            };
            return true;
        }

        /// <summary>The whole restore pipeline as ONE pure function: parse, drop every tab that cannot be
        /// shown, and report whether anything usable survived. WorkspacePrefs' three callers (app start, a
        /// project load, a Play-mode shell rebuild) all go through here, so "what a stored layout becomes"
        /// has one definition and one set of tests instead of three call sites agreeing by inspection.
        ///
        /// NULL MEANS «KEEP WHAT YOU HAVE», never «show an empty workspace», and it is returned for three
        /// different reasons that are deliberately not distinguished: nothing stored, a payload
        /// TryDeserialize rejects, and — the one worth naming — a payload every one of whose tabs was pruned
        /// away. That last case is NOT covered by any invariant in this file: NormalizeSplit repairs an empty
        /// Primary only by PROMOTING a non-empty Secondary, so with both panes emptied it takes neither
        /// branch and leaves a legal, zero-tab layout behind. Applying that would hand the DM a workspace
        /// showing nothing at all in place of the one they were looking at, which is strictly worse than the
        /// default map tab the caller already holds. (Not unrecoverable — each pane's tab strip keeps its
        /// «+», and Ctrl+K is global — but a blank window is not a restore.)
        ///
        /// TWO DROP RULES, ONE PREDICATE, and the ORDER inside it is meaningful. SurfaceIds.IsWellFormed
        /// first: an id no encoder in this codebase could have produced is refused outright, because the
        /// alternative is handing it to a decoder that would split it at the wrong place and resolve it to a
        /// DIFFERENT room under a correctly-labelled tab. `exists` second: a well-formed id whose target is
        /// gone. Both drop exactly one tab, silently — deliberately the same outcome, because to the DM they
        /// are the same event ("that tab is not coming back") and neither is worth an error tab. REJECTED:
        /// refusing the whole payload on one bad id, which would cost every other tab for a fault in one.
        ///
        /// `exists` MAY BE NULL, meaning "assume every target exists". That is not a convenience default —
        /// it is the app-start case stated honestly. At Awake nothing has loaded yet (no document, no POIs,
        /// no interiors), so a real existence predicate would answer "no" for every tab and prune the whole
        /// layout by construction; passing null says "I cannot answer this question yet" rather than
        /// answering it wrongly. The three callers and what each passes are listed on
        /// WorkspaceController.RestoreFromPrefs (null, at app start and on a shell rebuild) and
        /// EndProjectSwitch (the real predicate, once a project load has made the answer knowable) — NOT on
        /// WorkspacePrefs, which only forwards whatever it is given.</summary>
        public static WorkspaceLayout Restore(string payload, Func<SurfaceRef, bool> exists)
        {
            if (!TryDeserialize(payload, out WorkspaceLayout l)) return null;

            PruneMissing(l, s => IsRestorable(s, exists));

            if (l.Primary?.Tabs == null || l.Primary.Tabs.Count == 0) return null;
            return l;
        }

        static bool IsRestorable(SurfaceRef s, Func<SurfaceRef, bool> exists)
        {
            if (s == null) return false;
            if (!SurfaceIds.IsWellFormed(s.Kind, s.Id)) return false;
            return exists == null || exists(s);
        }

        /// <summary>Explicit switch on the enum's written name, mirroring NotesDocOps.TryParseKind — NOT
        /// Enum.TryParse, which would happily accept a bare numeral like "7" and let an unrecognized kind
        /// through a hand-edited PlayerPrefs value.</summary>
        static bool TryParseSurfaceKind(string raw, out SurfaceKind kind)
        {
            switch (raw)
            {
                case "Page":              kind = SurfaceKind.Page;             return true;
                case "WorldMap":          kind = SurfaceKind.WorldMap;         return true;
                case "Settlement":        kind = SurfaceKind.Settlement;       return true;
                case "BuildingInterior":  kind = SurfaceKind.BuildingInterior; return true;
                case "Dungeon":           kind = SurfaceKind.Dungeon;          return true;
                case "BattleGrid":        kind = SurfaceKind.BattleGrid;       return true;
                case "PoiEditor":         kind = SurfaceKind.PoiEditor;        return true;
            }
            kind = SurfaceKind.Page;
            return false;
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
