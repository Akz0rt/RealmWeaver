using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Draw-only contract for one dungeon-floor view (sub-project 3 revision). A renderer NEVER handles
    /// input, mutates the model, or knows about selection semantics — DungeonViewController owns all of
    /// that and hit-tests in TILE space via this renderer's Projection.
    ///
    /// That inversion is the entire point of the split: because Граф and Изо differ only by
    /// DungeonProjection.SquashY, one controller drives both, and editing in Изо is structural rather
    /// than a second code path (spec R4/R5).
    /// </summary>
    public interface IDungeonRenderer
    {
        /// <summary>Tile↔pixel mapping for THIS view. The controller reads it for hit-testing and drag;
        /// it is resolved by ResolveProjection and then held (spec R6 — no rescaling mid-drag).</summary>
        DungeonProjection Projection { get; }

        /// <summary>The RectTransform pointer coordinates are resolved against (this renderer's own root).</summary>
        RectTransform Area { get; }

        /// <summary>The GameObject the controller activates/deactivates when the Граф/Изо toggle flips.</summary>
        GameObject Host { get; }

        /// <summary>Fit Projection to the given TILE-space bounds and this renderer's own rect. Called ONCE
        /// per bind, and again on the first frame the rect becomes valid. Returns false if the rect is
        /// still {0,0} (not laid out) — the controller then retries next LateUpdate (rect gotcha).
        ///
        /// Takes bounds rather than a level (C2' revision) so the controller can fit to something other
        /// than one floor's own content — a Building fits to floor 0's contour bounds ALONE, the same on
        /// every floor, so the column and every room render identically floor to floor (the current floor is
        /// deliberately NOT unioned in; see DungeonViewController.FitBoundsFor for why). A Dungeon passes the
        /// current floor's own bounds, unchanged from before.</summary>
        bool ResolveProjection(float minX, float minY, float maxX, float maxY);

        /// <summary>Tear down and rebuild every visual from scratch (structural change: add/delete/link,
        /// level switch, room type/size change).
        ///
        /// NAMED RebuildView, not Rebuild, on purpose: DungeonIsoRenderer derives from UnityEngine.UI.Graphic,
        /// which ALREADY has a public virtual Rebuild(CanvasUpdate). A same-name member would be a legal
        /// overload but a genuine landmine — a mistyped argument would silently bind to Unity's canvas
        /// rebuild instead of ours.</summary>
        void RebuildView(InteriorData dungeon, int levelIndex, InteriorFloor lvl, RenderGraph rg, Font font,
                         System.Action<int> onJumpToLevel);

        /// <summary>Cheap per-frame reposition of existing visuals from current Room.X/Y — NO allocation,
        /// NO destroy/create. Called every cascade frame and every drag sample.
        ///
        /// <paramref name="includeRoadsInFence"/> two-tiers the derived settlement fence exactly like the road
        /// router: pass the SAME value the controller feeds SettlementRoadsFor(mode) to BuildRenderGraph — false
        /// on Fast/drag frames (fence skips the ~12.5 ms road A*, still follows the live building/gate rects),
        /// true on bind + settle (fence wraps the routed roads, matching the map). Ignored for non-settlement
        /// views (they have no fence).</summary>
        void RepositionRooms(InteriorFloor lvl, RenderGraph rg, bool includeRoadsInFence);

        /// <summary>Turn the selection/link highlight on or off for one room. `roomId` 0 = clear all.</summary>
        void SetHighlight(int roomId, bool on);

        /// <summary>Replace the projection wholesale and redraw (pan/zoom — Task 5). The controller
        /// composes the new value; the renderer stores it and repaints from the level+graph it last drew.
        /// Declared here from the start so Task 5 does not have to reopen this interface.</summary>
        void SetProjection(DungeonProjection p);
    }
}
