using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>Confines a WINDOW-anchored root canvas to a PANE's live screen rect, without touching the
    /// canvas itself: one stretched child ("__PaneFrame") is inserted between the canvas and its existing
    /// children, and that child's offsets are then driven from the pane's on-screen corners every frame.
    /// Everything the canvas already built keeps anchoring exactly as it did — to what it believes is "the
    /// window" — except that "the window" is now the pane.
    ///
    /// WHY A FRAME AND NOT A REPARENT. The obvious alternative — re-parent the whole legacy canvas under the
    /// pane's ContentArea, the way PageSurfaceHost.Show re-parents DocumentPageView.Root — was rejected:
    /// a Canvas nested inside another Canvas stops being a root canvas, which silently disables its own
    /// CanvasScaler (the nested canvas inherits the ROOT's scaleFactor instead) and stacks a second
    /// GraphicRaycaster inside the shell's, a combination this project has no reason to take on for a set of
    /// panels Р5 is going to redesign anyway. Inserting a plain RectTransform leaves the canvas a root
    /// canvas with its own scaler/raycaster semantics completely untouched.
    ///
    /// WHY NO sortingOrder CHANGES ARE NEEDED. The shell paints nothing inside the pane the map is shown in:
    /// MapSurfaceHost.SetBackgroundsEnabled(container, false) has already disabled all three of the opaque
    /// Images that would otherwise cover it (see that method's own doc). The legacy 40-60 chrome band
    /// therefore still composites above the camera and below ProjectMenuBar exactly as before — this class
    /// only moves pixels, never restacks them.
    ///
    /// SCREEN PIXELS == WORLD UNITS. Every canvas this is applied to is ScreenSpaceOverlay at scaleFactor 1
    /// (the CanvasScalers in this project are AddComponent-ed and never configured, so they stay at the
    /// default ConstantPixelSize/scaleFactor 1 — e.g. MapToolbarUI.cs:68), so a RectTransform world position
    /// IS a screen-pixel position and Apply needs no conversion. The same identity MapSurfaceHost.ApplyViewport
    /// already relies on when it turns shownIn.GetWorldCorners() straight into a Camera.rect.</summary>
    public static class PaneChromeFrame
    {
        const string FrameName = "__PaneFrame";

        /// <summary>Idempotent: returns the existing frame if this canvas already has one. Safe to call every
        /// frame (MapSurfaceHost's lazy resolution does exactly that for a panel that has not been built yet
        /// — see EnsureFrames there), because a second call is a single Transform.Find and nothing else.</summary>
        public static RectTransform Ensure(Transform canvasTransform)
        {
            if (canvasTransform == null) return null;
            if (canvasTransform.Find(FrameName) is RectTransform existing) return existing;

            var frame = new GameObject(FrameName, typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(canvasTransform, false);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            // Structural confinement, not arithmetic: with the mask, "stays inside its pane" holds no matter
            // how any individual panel chose to anchor itself, and a panel too wide for a narrow pane is CUT
            // at the pane boundary instead of spilling across the divider into the neighbour (where the other
            // pane's own background, still enabled at sortingOrder 70, would paint over it and make a
            // mispositioned panel read as a truncated one — the exact symptom the user reported).
            // Same structural-over-arithmetic choice TabStripView already made for tab titles (its per-tab
            // RectMask2D, commit b533632), for the same reason: the arithmetic version silently stops holding
            // the moment the layout changes shape. Panels sized for a full window WILL look cramped in a
            // half-width pane; that is Р5's redesign to solve, and a clean cut is the honest interim.
            frame.gameObject.AddComponent<RectMask2D>();

            // Downward iteration keeps indices below `i` stable as children leave. worldPositionStays:false
            // preserves local coords, and the frame is identical to the canvas rect right now, so nothing
            // moves. SetAsFirstSibling on each in turn restores the original sibling (= draw) order.
            for (int i = canvasTransform.childCount - 1; i >= 0; i--)
            {
                var child = canvasTransform.GetChild(i);
                if (child == frame) continue;
                child.SetParent(frame, false);
                child.SetAsFirstSibling();
            }
            return frame;
        }

        /// <summary>`paneCorners` is a pane ContentArea's GetWorldCorners buffer (index 0 = bottom-left,
        /// 2 = top-right). See the class doc for why a world position is used as a screen position verbatim.
        ///
        /// WRITES ONLY ON CHANGE — an ordinary optimisation, and deliberately NOT justified by the
        /// rebuild-re-entrancy hazard an earlier version of this comment claimed. That claim was WRONG and is
        /// recorded here so nobody re-derives it: CanvasUpdateRegistry subscribes its own PerformUpdate to
        /// Canvas.willRenderCanvases in its CONSTRUCTOR (CanvasUpdateRegistry.cs:91 in
        /// com.unity.ugui@52e65280e89e), so it is a SIBLING subscriber on that multicast event, not a pass
        /// this class runs inside. MapSurfaceHost's handler is invoked after PerformUpdate has returned and
        /// its m_PerformingLayoutUpdate/m_PerformingGraphicUpdate flags are back to false — so there is no
        /// re-entrancy, no LogError, no dropped rebuild. An unconditional write would simply queue a layout
        /// rebuild for the NEXT frame's PerformUpdate, every frame, forever. Comparing first makes the steady
        /// state (no drag, no resize) a pure read that queues nothing.
        ///
        /// Vector2's == compares squared distance against ~1e-10, i.e. ~1e-5 px — effectively exact, not a
        /// sub-pixel tolerance. It is used because it is the natural operator, not because it absorbs jitter;
        /// GetWorldCorners on a static layout returns bit-identical values anyway.</summary>
        public static void Apply(RectTransform frame, Vector3[] paneCorners)
        {
            if (frame == null || paneCorners == null || paneCorners.Length < 3) return;
            var min = new Vector2(paneCorners[0].x, paneCorners[0].y);
            var max = new Vector2(paneCorners[2].x - Screen.width, paneCorners[2].y - Screen.height);
            if (frame.offsetMin != min) frame.offsetMin = min;
            if (frame.offsetMax != max) frame.offsetMax = max;
        }

        /// <summary>Undoes Apply: the frame gives the whole window back to the chrome inside it, MINUS the
        /// menu-bar strip. Called from MapSurfaceHost.Hide so a surface that stops owning a pane is not left
        /// clamped to whatever rect that pane last had — a stale clamp would survive into any path that shows
        /// this chrome OUTSIDE the workspace (MapScreenController/ScreenSwitcher still drive its active state
        /// independently — see MapSurfaceHost's KNOWN SEAM paragraph, and specifically the case it names:
        /// closing the POI editor re-asserts AppScreen.MapEditor, re-activating the chrome behind a Hide()
        /// this host already issued).
        ///
        /// WHY NOT PLAIN ZERO. Zeroing all four offsets restores the frame to the full canvas — which WAS the
        /// window-anchored geometry the chrome was written for, and stopped being it in the same change that
        /// created this class: Task 10a removed the 40px menu-bar term from all six top-anchored map panels
        /// (MapLayersPanel.cs:68 carries the full reasoning) because a pane's ContentArea already excludes the
        /// bar. A zeroed frame would therefore put the toolbar at window y=0, UNDER ProjectMenuBar (canvas
        /// order 100 against the toolbar's 40), with the five panels 40px too high and overlapping it.
        /// Insetting the top by MenuBarInset instead puts those panels exactly where the removed 40f used to,
        /// so the un-hosted layout is preserved rather than merely "restored" to something now wrong.
        /// WorkspaceBuilder.MenuBarInset is the same constant the shell reserves for that bar, itself derived
        /// from ProjectMenuBar.BarHeightPixels — one number, three readers, no copy.</summary>
        public static void Reset(RectTransform frame)
        {
            if (frame == null) return;
            var max = new Vector2(0f, -WorkspaceBuilder.MenuBarInset);
            // Same write-only-on-change rule as Apply — Hide can be called repeatedly by SyncSurfaces.
            if (frame.offsetMin != Vector2.zero) frame.offsetMin = Vector2.zero;
            if (frame.offsetMax != max) frame.offsetMax = max;
        }
    }
}
