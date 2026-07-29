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

        void Awake()
        {
            // `if (Layout == null)` guards a re-entrant Awake() on a LIVE object — e.g. some other
            // code path calling Awake() again without a reload in between — where Layout genuinely
            // still holds what it held before. It does NOT protect the case it looks like it
            // protects: a Play-mode script recompile. WorkspaceBuilder.cs:30-38 documents why —
            // Layout is a plain auto-property, not a [SerializeField], so its backing field does
            // NOT survive a script reload the way the GameObject/component hierarchy does. On that
            // exact path, Layout IS null when this runs, the condition is true, and a fresh
            // NewDefault() is created anyway — tabs, split and focus are silently discarded, same
            // as before this guard existed. Do not read this line as "recompiles are handled".
            //
            // Task 11 (WorkspacePrefs) is what actually has to fix this, and it must NOT gate its
            // restore on `Layout == null` — that condition is not a reliable "first run" signal (see
            // above, it is also true after every recompile that should instead be recovering saved
            // state). Task 11 needs to load from WorkspacePrefs unconditionally on startup and apply
            // the result, independent of whatever Awake() already put in Layout.
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

        void RaiseChanged() => OnLayoutChanged?.Invoke();
    }
}
