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
    /// pane's ContentArea, the way PageSurfaceHost.Show used to move its one DocumentPageView between panes
    /// (Task 4 replaced that with a view built inside each pane) — was rejected:
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
    /// default ConstantPixelSize/scaleFactor 1 — e.g. MapToolbarUI.cs:97), so a RectTransform world position
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

            // The class doc's "screen pixels ARE world units" identity is the one thing here that is asserted
            // rather than derived, and a silent mis-inset is exactly the failure mode this project keeps
            // hitting — code believing one thing while the screen shows another. So read the real value once,
            // at frame creation, and SAY SO if it ever stops being 1. NOT a guarantee, and the earlier version
            // of this comment wrongly claimed it was: EnsureFrames checks activeInHierarchy on the ROOT and
            // then calls GetComponentsInChildren(true, …), which includes INACTIVE children — MapToolbarUI's
            // barCanvasGO is exactly that (SetChromeVisible(false) deactivates it while the root stays
            // active), so a canvas whose CanvasScaler.OnEnable has not run can reach here. It fails SAFE:
            // Canvas.scaleFactor defaults to 1, so the check can miss a real mismatch but can never invent
            // one. A missed detection costs the same silent mis-inset that existed before this check, so a
            // one-sided check is strictly an improvement over none. The frame is still built: a mis-inset
            // frame is strictly better than none, which would leave the chrome anchored to the whole window.
            // The full fix — dividing Apply's offsets by canvas.scaleFactor — is deliberately not taken:
            // nothing in this project configures a CanvasScaler, and Р5 replaces this chrome outright, so
            // paying a per-frame GetComponent to support a mode no canvas uses is not worth it.
            var canvas = canvasTransform.GetComponent<Canvas>();
            if (canvas != null && !Mathf.Approximately(canvas.scaleFactor, 1f))
                Debug.LogError($"[PaneChromeFrame] '{canvasTransform.name}' has scaleFactor {canvas.scaleFactor}, " +
                               "not 1 — Apply treats a world position as a screen pixel and will mis-inset it.");

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
            //
            // ONE PANEL NO LONGER RELIES ON THAT INTERIM. MapLegendUI's content-driven height grew tall enough
            // to be cut in a short or split pane, which the DM reported as unreadable, so it now measures its
            // own PARENT rect — this frame — and caps how many rows it builds (see its class doc and its
            // MaxRowsForHeight). That is the PANEL adapting to the mask, not the mask making an exception: the
            // clip below still applies to it unchanged, and is still what would catch a miscalculated cap. The
            // other framed panels are untouched, and the sentence above still describes them.
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
        /// clamped to whatever rect that pane last had. Through Task 10b the stale clamp had a SECOND owner to
        /// defend against — MapScreenController/ScreenSwitcher drove this chrome's active state independently
        /// via an `AppScreen.MapEditor` that Task 10c deleted, so the chrome could be re-shown behind a Hide()
        /// this host had already issued (see MapSurfaceHost's now-closed KNOWN SEAM paragraph). What it
        /// defends against NOW is narrower and still real: this host's own next Show landing in a DIFFERENT
        /// pane, and a scene with no workspace shell in it at all, where this chrome is never hosted.
        ///
        /// WHY NOT PLAIN ZERO. Zeroing all four offsets restores the frame to the full canvas — which WAS the
        /// window-anchored geometry the chrome was written for, and stopped being it in the same change that
        /// created this class: Task 10a removed the 40px menu-bar term from all six top-anchored map panels
        /// (MapLayersPanel.cs:74 carries the full reasoning) because a pane's ContentArea already excludes the
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
