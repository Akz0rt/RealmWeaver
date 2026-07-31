using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Owns the in-memory WorkspaceLayout and is the ONLY place that mutates it. Every structural
    /// change (open/close/activate/focus) routes through WorkspaceOps — this class never touches
    /// Tabs lists, or PaneState.ActiveIndex, itself; WorkspaceOps.SetActiveTab owns that scalar the
    /// same way CloseTab owns removal (see WorkspaceLayout.cs:27-28: "WorkspaceOps.
    /// FixActiveIndexAfterRemoval is the one place that keeps this true" — SetActiveTab is the other
    /// place). SplitRatio is the one genuine exception: its own doc comment on WorkspaceLayout says
    /// "no op in this layer moves it", so it is set directly here — see SetSplitRatioLive below.
    ///
    /// WorkspaceBuilder builds the RectTransform/LayoutElement hierarchy and hands the pieces this
    /// class needs to Initialize(); from then on this class applies Layout onto them and reports
    /// gestures (drag, click) back into Layout.
    /// </summary>
    public class WorkspaceController : MonoBehaviour
    {
        /// <summary>The three RectTransform/LayoutElement handles WorkspaceBuilder hands over per pane.
        /// PaneRect is the WHOLE pane (tab strip + content, sized by PaneElement.flexibleWidth from
        /// SplitRatio); ContentRect is the narrower child below the tab strip that PaneContent(int) returns.
        /// A plain struct rather than three more Initialize parameters, now that Task 6 needs a ContentRect
        /// distinct from PaneRect for the first time.</summary>
        public readonly struct PaneHandles
        {
            public readonly RectTransform PaneRect;
            public readonly LayoutElement PaneElement;
            public readonly RectTransform ContentRect;

            public PaneHandles(RectTransform paneRect, LayoutElement paneElement, RectTransform contentRect)
            {
                PaneRect = paneRect;
                PaneElement = paneElement;
                ContentRect = contentRect;
            }
        }

        public WorkspaceLayout Layout { get; private set; }

        public event System.Action OnLayoutChanged;

        RectTransform primaryContent;
        LayoutElement primaryLayoutElement;
        RectTransform secondaryContent;
        LayoutElement secondaryLayoutElement;
        // Only Secondary needs a whole-pane show/hide handle — ReflowPanes toggles this GameObject (tab
        // strip AND content together) off when there is no split. Primary has no equivalent field because
        // it is never hidden, so nothing here would ever read it.
        RectTransform secondaryPaneRect;
        RectTransform dividerRect;

        SurfaceRegistry surfaceRegistry;

        void Awake() => EnsureLayout();

        /// <summary>Guarantees Layout exists, whoever asks first. Awake() calls this, and so does
        /// WorkspaceBuilder.Awake's recompile-guard branch DIRECTLY — that branch recovers this component
        /// with GetComponent (which, unlike AddComponent, does NOT invoke Awake synchronously), so whether
        /// this class's own Awake has already run by then depends on Unity's undefined Awake-dispatch order
        /// between two components on the same GameObject. The likely order is the bad one: WorkspaceBuilder
        /// is the PRE-EXISTING component and WorkspaceController was AddComponent-ed later, so the builder
        /// tends to run first and would otherwise reach SetSurfaceRegistry -> SyncSurfaces -> Layout.FocusedPane
        /// on a null Layout, throwing an NRE inside the very recovery path that exists to survive reloads.
        /// Calling this explicitly is the same direct-call pattern NotesRootBuilder.EnsureBuilt already uses
        /// for exactly this reason, rather than trusting an Awake that may not have fired yet.
        ///
        /// `if (Layout == null)` guards a re-entrant Awake() on a LIVE object — e.g. some other
        /// code path calling Awake() again without a reload in between — where Layout genuinely
        /// still holds what it held before. It does NOT protect the case it looks like it
        /// protects: a Play-mode script recompile. WorkspaceBuilder.cs:111-146 documents why —
        /// Layout is a plain auto-property, not a [SerializeField], so its backing field does
        /// NOT survive a script reload the way the GameObject/component hierarchy does. On that
        /// exact path, Layout IS null when this runs, the condition is true, and a fresh
        /// NewDefault() is created anyway — tabs, split and focus are silently discarded, same
        /// as before this guard existed. Do not read this line as "recompiles are handled".
        ///
        /// Task 11 (WorkspacePrefs) is what actually has to fix this, and it must NOT gate its
        /// restore on `Layout == null` — that condition is not a reliable "first run" signal (see
        /// above, it is also true after every recompile that should instead be recovering saved
        /// state). Task 11 needs to load from WorkspacePrefs unconditionally on startup and apply
        /// the result, independent of whatever Awake() already put in Layout.</summary>
        public void EnsureLayout()
        {
            if (Layout == null) Layout = WorkspaceOps.NewDefault();
        }

        /// <summary>Wires the RectTransforms/LayoutElements WorkspaceBuilder just constructed, then applies
        /// the freshly-created default Layout onto them once so the initial frame is already correct
        /// (single pane, secondary + divider hidden) instead of waiting for the first mutation.</summary>
        public void Initialize(PaneHandles primary, PaneHandles secondary, RectTransform dividerRectTransform)
        {
            primaryContent = primary.ContentRect;
            primaryLayoutElement = primary.PaneElement;
            secondaryContent = secondary.ContentRect;
            secondaryLayoutElement = secondary.PaneElement;
            secondaryPaneRect = secondary.PaneRect;
            dividerRect = dividerRectTransform;

            ReflowPanes();
        }

        /// <summary>Re-acquires the six handles Initialize normally supplies, by hierarchy path, when a
        /// Play-mode script reload has wiped them — the "re-point the references, don't rebuild" pattern
        /// MapSurfaceHost.Rewire/ResolveRootRowBackground already use in this same arc, applied to this
        /// class's own fields. No GameObject is created, moved or duplicated here.
        ///
        /// WHY THIS IS NEEDED AT ALL, and why a null Layout was only half the reload story: every field
        /// Initialize writes is a plain, non-[SerializeField] field, so a domain reload nulls all six —
        /// and Initialize's ONLY caller is WorkspaceBuilder.BuildPaneContainer, on the first-build path
        /// the recompile guard skips. Without this, PaneContent(0) returns null forever after a reload,
        /// so SyncSurfaces shows NOTHING and Hides EVERY host: a blank workspace, not the "surface system
        /// revived" the guard branch is written to produce. The paths below are exactly where
        /// WorkspaceBuilder.BuildPaneContainer/BuildPane construct them, relative to THIS transform —
        /// which is also WorkspaceBuilder's own transform, since WorkspaceBuilder AddComponents this
        /// class onto its own GameObject. Transform.Find locates INACTIVE children too, which matters:
        /// ReflowPanes deactivates SecondaryPane and the divider whenever there is no split.
        ///
        /// ALL-OR-NOTHING on purpose: nothing is assigned until every lookup has succeeded, so a renamed
        /// or restructured hierarchy leaves this class in its existing safe state (ReflowPanes' own
        /// `primaryLayoutElement == null` early-return, PaneContent returning null) instead of half-wired
        /// — ReflowPanes dereferences secondaryPaneRect/dividerRect unguarded once past that check, so a
        /// partial recovery would turn a silent no-op into an NRE.
        ///
        /// Calls EnsureLayout() itself rather than documenting an ordering requirement it would then NRE on:
        /// Initialize below ends in ReflowPanes, which reads Layout.Secondary/SplitRatio. WorkspaceBuilder's
        /// guard branch still calls EnsureLayout explicitly right before this, because the registry it wires
        /// afterwards needs a Layout too — this is belt for any other caller, not a duplicate of that.
        ///
        /// This deliberately does NOT try to revive TabStripView /
        /// NavigatorView / QuickOpenPopup / the divider's drag delegates — see WorkspaceBuilder.Awake's
        /// comment for why a partial recovery THERE is worse than none, and Task 11 for the real fix. The
        /// line between the two: these handles are read by SyncSurfaces, which round 3 made run again
        /// after a reload; the strips and the navigator are read by nothing that runs post-reload.</summary>
        public void EnsurePaneHandles()
        {
            if (primaryContent != null) return;

            EnsureLayout();

            const string PaneContainerPath = "WorkspaceCanvas/RootRow/PaneContainer/";
            var primaryPane = transform.Find(PaneContainerPath + "PrimaryPane") as RectTransform;
            var secondaryPane = transform.Find(PaneContainerPath + "SecondaryPane") as RectTransform;
            var divider = transform.Find(PaneContainerPath + "DraggableDivider") as RectTransform;
            if (primaryPane == null || secondaryPane == null || divider == null) return;

            var primaryContentRect = primaryPane.Find("ContentArea") as RectTransform;
            var secondaryContentRect = secondaryPane.Find("ContentArea") as RectTransform;
            var primaryElement = primaryPane.GetComponent<LayoutElement>();
            var secondaryElement = secondaryPane.GetComponent<LayoutElement>();
            if (primaryContentRect == null || secondaryContentRect == null ||
                primaryElement == null || secondaryElement == null) return;

            Initialize(new PaneHandles(primaryPane, primaryElement, primaryContentRect),
                       new PaneHandles(secondaryPane, secondaryElement, secondaryContentRect),
                       divider);
        }

        /// <summary>Wires the surface registry (Task 9) and runs the first sync immediately — WorkspaceBuilder
        /// calls this AFTER registering every host it built (page, map), which is necessarily after
        /// Initialize/ReflowPanes have already applied the default Layout. Without an immediate sync here,
        /// the workspace's default WorldMap tab (WorkspaceOps.NewDefault) would sit there un-Shown until
        /// the next unrelated layout change happened to trigger one.</summary>
        public void SetSurfaceRegistry(SurfaceRegistry registry)
        {
            surfaceRegistry = registry;
            SyncSurfaces();
        }

        // ── Interface WorkspaceController produces ───────────────────────────────

        /// <summary>Where a surface parents itself. Pane 0 (Primary) always exists — WorkspaceLayout's own
        /// invariant guarantees there is never a moment with an empty/absent Primary. Pane 1 (Secondary) is
        /// absent (returns null) whenever Layout.Secondary is null, matching WorkspaceOps.PaneAt's own
        /// "null means absent" contract.
        ///
        /// Returns the pane's ContentArea child — the narrower rect BELOW the tab strip TabStripView now
        /// occupies, not the whole pane (Task 5 returned the whole pane; Task 6 re-points this here, per
        /// Task 5's own handoff note). WorkspaceOps.NormalizeSplit can still promote Secondary into
        /// Primary's slot; PaneContent(0)/PaneContent(1) keep naming the same PHYSICAL containers regardless
        /// (Task 5's carried note to Task 9, still open, now pointing at these narrower rects): whoever
        /// hosts a surface here must re-parent it on promotion rather than assume index 0 always means the
        /// same logical pane's tabs.</summary>
        public RectTransform PaneContent(int pane)
        {
            if (pane == 0) return primaryContent;
            if (pane == 1) return Layout.Secondary != null ? secondaryContent : null;
            return null;
        }

        public void Open(SurfaceRef s, string title, bool inOtherPane)
        {
            // WorkspaceOps.Open no-ops on a null surface without reporting it, which would otherwise make
            // this fire a spurious OnLayoutChanged for a call that changed nothing.
            if (s == null) return;

            WorkspaceOps.Open(Layout, s, title, inOtherPane);
            ReflowPanes();
            RaiseChanged();
        }

        public void CloseTab(int pane, int index)
        {
            if (!WorkspaceOps.CloseTab(Layout, pane, index)) return;
            ReflowPanes();
            RaiseChanged();
        }

        /// <summary>Activates a tab within its pane, through WorkspaceOps.SetActiveTab — the tested op that
        /// validates the pane and index range and reports whether anything actually changed (false for an
        /// absent pane, an out-of-range index, or re-activating the already-active tab).</summary>
        public void SetActive(int pane, int index)
        {
            if (!WorkspaceOps.SetActiveTab(Layout, pane, index)) return;
            RaiseChanged();
        }

        public void FocusPane(int pane)
        {
            int before = Layout.FocusedPane;
            WorkspaceOps.Focus(Layout, pane);
            if (Layout.FocusedPane != before) RaiseChanged();
        }

        // ── Split-ratio drag (wired by WorkspaceBuilder to the DraggableDivider) ─

        /// <summary>Called on every drag-delta frame: updates SplitRatio and reflows the pane widths + divider
        /// position immediately (the "live" half of "drags SplitRatio live and saves on drag-end"), but does
        /// NOT raise OnLayoutChanged — once persistence is wired (Task 11: "save on every OnLayoutChanged"),
        /// firing this every drag frame would write to PlayerPrefs dozens of times per drag.</summary>
        public void SetSplitRatioLive(float desiredRatio)
        {
            if (Layout.Secondary == null) return;   // nothing to drag against; the divider is hidden anyway

            float clamped = WorkspaceOps.ClampSplitRatio(desiredRatio);
            if (Mathf.Approximately(clamped, Layout.SplitRatio)) return;

            Layout.SplitRatio = clamped;
            ReflowPanes();
        }

        /// <summary>The "saves on drag-end" half: raises OnLayoutChanged once, the hook a future persistence
        /// listener (Task 11) attaches to. Called unconditionally on drag-end, matching
        /// NotesLayoutController.SaveSplitFraction's own drag-end-always-saves behaviour.</summary>
        public void CommitSplitRatio() => RaiseChanged();

        // ── Navigator collapse (wired by NavigatorView to its header button) ─────

        /// <summary>Writes Layout.NavigatorCollapsed directly rather than adding a WorkspaceOps op, the same
        /// exception SetSplitRatioLive takes for SplitRatio and for the same reason: WorkspaceLayout's own doc
        /// comment says "no op in this layer moves it", because NavigatorCollapsed is a free bool with no
        /// cross-field invariant to protect. That is NOT the same situation Task 5's review sent back —
        /// PaneState.ActiveIndex had to move to WorkspaceOps.SetActiveTab because the ops layer owns "in
        /// range, -1 iff empty", an invariant a Rendering-layer write could violate. NavigatorCollapsed has no
        /// such invariant; there is nothing for WorkspaceOps to protect. Unlike the drag-live/drag-commit split
        /// for SplitRatio, a collapse toggle is one discrete click, not a per-frame gesture, so there is no
        /// "live" phase here — it raises OnLayoutChanged immediately, matching CommitSplitRatio's timing.
        ///
        /// LOAD-BEARING FOR TASK 11: this method does not touch any pixel itself — NavigatorView.Rebuild is
        /// what turns NavigatorCollapsed/NavigatorWidth into columnLayoutElement.preferredWidth, and it only
        /// runs when OnLayoutChanged fires. Any future path that changes either field (a WorkspacePrefs
        /// restore, say) MUST raise OnLayoutChanged afterward, or the navigator column will silently stay
        /// the wrong width until something unrelated happens to trigger a rebuild.</summary>
        public void SetNavigatorCollapsed(bool collapsed)
        {
            if (Layout.NavigatorCollapsed == collapsed) return;
            Layout.NavigatorCollapsed = collapsed;
            RaiseChanged();
        }

        // ── Applying Layout onto the built hierarchy ──────────────────────────────

        /// <summary>The single place Layout.Secondary/SplitRatio become pixels: shows/hides the secondary
        /// pane and the divider together, and sizes both panes from SplitRatio via flexibleWidth (with
        /// preferredWidth pinned to 0 on both — see WorkspaceBuilder — so flexibleWidth alone decides the
        /// split, exactly as the brief specifies). Safe to call before Initialize (no-ops rather than
        /// throwing) so an out-of-order call from a self-test cannot NRE.</summary>
        void ReflowPanes()
        {
            if (primaryLayoutElement == null) return;

            bool split = Layout.Secondary != null;
            secondaryPaneRect.gameObject.SetActive(split);   // hides the WHOLE pane (tab strip + content)
            dividerRect.gameObject.SetActive(split);          // together, not just the content area.

            float ratio = Layout.SplitRatio;
            primaryLayoutElement.flexibleWidth = split ? ratio : 1f;
            secondaryLayoutElement.flexibleWidth = split ? (1f - ratio) : 0f;

            dividerRect.anchorMin = new Vector2(ratio, 0f);
            dividerRect.anchorMax = new Vector2(ratio, 1f);
        }

        void RaiseChanged()
        {
            SyncSurfaces();
            OnLayoutChanged?.Invoke();
        }

        // ── Surfaces (Task 9) ──────────────────────────────────────────────────

        /// <summary>Shows each pane's active surface through its registered host, and Hides every registered
        /// host that no pane wants any more. Re-reads PaneContent(pane) fresh on every call rather than
        /// caching it, which is what makes a Secondary->Primary promotion (WorkspaceOps.NormalizeSplit) work
        /// for free: PaneContent(0) already returns the correct NEW physical container after a promotion, and
        /// ISurfaceHost.Show is required to re-parent unconditionally every call — see SurfaceRegistry.cs's
        /// own class doc.
        ///
        /// Iterates with the FOCUSED pane's Show() called LAST: if both panes' active tabs happen to share a
        /// SurfaceKind (the single-host-per-kind limitation ISurfaceHost's signature accepts — see
        /// PageSurfaceHost's own doc), the LAST Show() wins the one real host's parent, and this ordering
        /// makes that always be the pane the user is actually looking at, rather than pane 1 always winning
        /// by coincidence of iteration order.</summary>
        void SyncSurfaces()
        {
            // The real guard: no hosts registered yet (before WorkspaceBuilder's SetSurfaceRegistry call)
            // means there is nothing to show or hide.
            if (surfaceRegistry == null) return;

            // Belt, not the fix: EnsureLayout is what actually guarantees a Layout on every path that
            // reaches here (Awake for a first build, WorkspaceBuilder.Awake's guard branch for a reload —
            // see EnsureLayout's own doc). This stays because SetSurfaceRegistry is public and calls straight
            // through to here, so a future caller that skips EnsureLayout gets an inert no-op instead of an
            // NRE on Layout.FocusedPane / PaneContent / ActiveSurfaceOf below.
            if (Layout == null) return;

            int[] order = Layout.FocusedPane == 1 ? new[] { 0, 1 } : new[] { 1, 0 };
            var shownKinds = new HashSet<SurfaceKind>();

            foreach (int pane in order)
            {
                SurfaceRef active = ActiveSurfaceOf(pane);
                RectTransform paneContent = PaneContent(pane);
                if (active == null || paneContent == null) continue;

                ISurfaceHost host = surfaceRegistry.For(active.Kind);
                if (host == null) continue;   // not registered yet — Task 10 registers the rest.

                host.Show(paneContent, active.Id);
                shownKinds.Add(active.Kind);
            }

            foreach (var host in surfaceRegistry.All)
                if (!shownKinds.Contains(host.Kind))
                    host.Hide();
        }

        /// <summary>The surface the pane's active tab points at, or null for an absent pane / an empty pane.
        /// Same shape as NavigatorView.ActiveSurface, kept separate rather than shared — this one takes an
        /// explicit pane index (both panes, one at a time) where NavigatorView only ever asks about the
        /// FOCUSED pane.</summary>
        SurfaceRef ActiveSurfaceOf(int pane)
        {
            PaneState p = WorkspaceOps.PaneAt(Layout, pane);
            if (p?.Tabs == null || p.ActiveIndex < 0 || p.ActiveIndex >= p.Tabs.Count) return null;
            return p.Tabs[p.ActiveIndex].Surface;
        }
    }
}
