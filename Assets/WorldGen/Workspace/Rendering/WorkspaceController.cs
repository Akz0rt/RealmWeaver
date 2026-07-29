using UnityEngine;
using UnityEngine.UI;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Owns the in-memory WorkspaceLayout and is the ONLY place that mutates it. Every structural
    /// change (open/close/focus) routes through WorkspaceOps — this class never touches Tabs lists
    /// itself. The two scalar fields WorkspaceOps deliberately has no op for (SplitRatio,
    /// PaneState.ActiveIndex — see their doc comments on WorkspaceLayout/PaneState, "no op in this
    /// layer moves it") are set directly here, which is the one place that is meant to happen.
    ///
    /// WorkspaceBuilder builds the RectTransform/LayoutElement hierarchy and hands the pieces this
    /// class needs to Initialize(); from then on this class applies Layout onto them and reports
    /// gestures (drag, click) back into Layout.
    /// </summary>
    public class WorkspaceController : MonoBehaviour
    {
        public WorkspaceLayout Layout { get; private set; }

        public event System.Action OnLayoutChanged;

        RectTransform primaryContent;
        LayoutElement primaryLayoutElement;
        RectTransform secondaryContent;
        LayoutElement secondaryLayoutElement;
        RectTransform dividerRect;

        void Awake()
        {
            Layout = WorkspaceOps.NewDefault();
        }

        /// <summary>Wires the RectTransforms/LayoutElements WorkspaceBuilder just constructed, then applies
        /// the freshly-created default Layout onto them once so the initial frame is already correct
        /// (single pane, secondary + divider hidden) instead of waiting for the first mutation.</summary>
        public void Initialize(RectTransform primaryContentRect, LayoutElement primaryElement,
                                RectTransform secondaryContentRect, LayoutElement secondaryElement,
                                RectTransform dividerRectTransform)
        {
            primaryContent = primaryContentRect;
            primaryLayoutElement = primaryElement;
            secondaryContent = secondaryContentRect;
            secondaryLayoutElement = secondaryElement;
            dividerRect = dividerRectTransform;

            ReflowPanes();
        }

        // ── Interface WorkspaceController produces ───────────────────────────────

        /// <summary>Where a surface parents itself. Pane 0 (Primary) always exists — WorkspaceLayout's own
        /// invariant guarantees there is never a moment with an empty/absent Primary. Pane 1 (Secondary) is
        /// absent (returns null) whenever Layout.Secondary is null, matching WorkspaceOps.PaneAt's own
        /// "null means absent" contract.
        ///
        /// Task 5 returns the pane container RectTransform itself — the whole pane IS the content area for
        /// now. Task 6 will carve a tab strip off the top of each pane and must re-point this at the
        /// narrower content child it creates; that child does not exist yet, by design (scope discipline:
        /// build only the containers this task owns).</summary>
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

        /// <summary>Activates a tab within its pane. There is no WorkspaceOps op for this — ActiveIndex is a
        /// scalar, not a list — so the range check here IS what keeps it valid; WorkspaceOps.PaneAt supplies
        /// the pane, and the explicit 0..Tabs.Count-1 bound is exactly the invariant
        /// FixActiveIndexAfterRemoval documents as "-1 only for an empty pane, otherwise a real index",
        /// which this call preserves rather than violates.</summary>
        public void SetActive(int pane, int index)
        {
            PaneState p = WorkspaceOps.PaneAt(Layout, pane);
            if (p?.Tabs == null || index < 0 || index >= p.Tabs.Count || index == p.ActiveIndex) return;

            p.ActiveIndex = index;
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
            secondaryContent.gameObject.SetActive(split);
            dividerRect.gameObject.SetActive(split);

            float ratio = Layout.SplitRatio;
            primaryLayoutElement.flexibleWidth = split ? ratio : 1f;
            secondaryLayoutElement.flexibleWidth = split ? (1f - ratio) : 0f;

            dividerRect.anchorMin = new Vector2(ratio, 0f);
            dividerRect.anchorMax = new Vector2(ratio, 1f);
        }

        void RaiseChanged() => OnLayoutChanged?.Invoke();
    }
}
