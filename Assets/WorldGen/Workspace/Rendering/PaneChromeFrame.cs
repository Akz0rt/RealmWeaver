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
        /// 2 = top-right). See the class doc for why a world position is used as a screen position verbatim.</summary>
        public static void Apply(RectTransform frame, Vector3[] paneCorners)
        {
            if (frame == null || paneCorners == null || paneCorners.Length < 3) return;
            frame.offsetMin = new Vector2(paneCorners[0].x, paneCorners[0].y);
            frame.offsetMax = new Vector2(paneCorners[2].x - Screen.width, paneCorners[2].y - Screen.height);
        }

        /// <summary>Undoes Apply: the frame becomes the full canvas rect again, so the chrome inside it is
        /// back to being window-anchored. Called from MapSurfaceHost.Hide so a surface that stops owning a
        /// pane is not left clamped to whatever rect that pane last had — a stale clamp would survive into
        /// any path that shows this chrome OUTSIDE the workspace (MapScreenController/ScreenSwitcher still
        /// drive its active state independently — see MapSurfaceHost's KNOWN SEAM paragraph).</summary>
        public static void Reset(RectTransform frame)
        {
            if (frame == null) return;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;
        }
    }
}
