using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Draw-only contract for one interior-floor view. A renderer NEVER receives raw input, mutates the
    /// model, or knows about selection semantics — DungeonViewController owns all of that and resolves
    /// PointerEventData down to an AREA-LOCAL point. From there, hit-testing and area-local→normalized
    /// mapping are THIS renderer's own coordinate space (Task 6: HitRoomId / TryAreaToNorm).
    ///
    /// That inversion is the entire point of the split, and there are now two live implementations proving
    /// it: DungeonFlatRenderer (dungeons + building interiors) maps through DungeonProjection in TILE space;
    /// SettlementVolumeRenderer (settlements) inverts the same projection down to a building-lattice CELL and
    /// snaps to it. One controller drives both with no per-renderer editing branch. The host picks the
    /// renderer from the interior's Kind — there is no user-facing view toggle (the Граф/Изо toggle this
    /// interface was originally shaped for never shipped; the iso renderer was built and dropped).
    /// </summary>
    public interface IDungeonRenderer
    {
        /// <summary>Tile↔pixel mapping for THIS view. Both renderers use it for their own hit-testing and drag
        /// mapping; it is resolved by ResolveProjection and then held (spec R6 — no rescaling mid-drag).
        ///
        /// PxPerTile IS LOAD-BEARING BEYOND DRAWING. The controller reads it directly as its pointer-input
        /// readiness gate (TryPointerToAreaLocal), BEFORE any delegation: while PxPerTile &lt;= 0 every click
        /// and drag is discarded and HitRoomId/TryAreaToNorm are never reached. An implementer must therefore
        /// produce a meaningful positive PxPerTile, not merely implement the two mapping methods. The
        /// volumetric renderer satisfies this by expressing its camera tilt as DungeonProjection.SquashY and
        /// fitting normally, so PxPerTile stays exactly "pixels per tile" and its cell width derives from it
        /// (Cell × TilesPerAxis × PxPerTile) — which is why that gate did not have to be revisited.</summary>
        DungeonProjection Projection { get; }

        /// <summary>The RectTransform pointer coordinates are resolved against (this renderer's own root).</summary>
        RectTransform Area { get; }

        // Renderer owns hit-testing and screen→world so a non-projection renderer can supply its own
        // (Task 6). The controller resolves screen → AREA-LOCAL point (it owns PointerEventData) and hands
        // that in; everything past that — local → tile, tile → room / tile → normalized — is the
        // renderer's own coordinate space and stays here.

        /// <summary>Topmost room whose footprint contains areaLocalPoint (mapped into this renderer's own
        /// space), resolving an overlap the way the view makes it LOOK stacked. Returns 0 for a miss
        /// (background) — never a negative id: DungeonViewController.OnPointerClick keys "clear the
        /// selection" on exactly `id == 0`, so any other miss value silently breaks background-click.
        ///
        /// PRECONDITION: <paramref name="lvl"/> must be the controller's currently bound floor and non-null —
        /// this is the room list the answer is drawn from, and the controller guards it before every call. An
        /// implementer should treat null as a miss rather than throwing, but must not rely on being handed
        /// one.</summary>
        int HitRoomId(Vector2 areaLocalPoint, InteriorFloor lvl);

        /// <summary>areaLocalPoint → normalized 0..1 room position, via this renderer's own coordinate map.
        /// The caller must not act on nx/ny when this returns false.
        ///
        /// FALSE means "I cannot place this point", and implementers differ in whether that is reachable —
        /// which is deliberate, not an inconsistency to normalize away. DungeonFlatRenderer returns true
        /// unconditionally: its only failure mode is an unresolved projection, and the controller's own
        /// PxPerTile gate already rejected that before the point ever arrived, so it has nothing left to
        /// refuse. SettlementVolumeRenderer does return false — for a not-yet-built tile grid or a degenerate
        /// projection — because for it those states would silently answer with a corner cell rather than with
        /// nothing, and the drag path needs to be able to tell the difference.</summary>
        bool TryAreaToNorm(Vector2 areaLocalPoint, out float nx, out float ny);

        /// <summary>This renderer's own root GameObject. The controller deactivates the outgoing renderer's
        /// Host and activates the incoming one's whenever SetRenderer swaps them.</summary>
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
        /// NAMED RebuildView, not Rebuild, on purpose: any implementer deriving from UnityEngine.UI.Graphic
        /// (the dropped iso renderer did) ALREADY inherits a public virtual Rebuild(CanvasUpdate). A same-name
        /// member would be a legal overload but a genuine landmine — a mistyped argument would silently bind
        /// to Unity's canvas rebuild instead of ours. Neither renderer that exists today derives from Graphic,
        /// but the name stays: the hazard returns the moment one does.</summary>
        void RebuildView(InteriorData dungeon, int levelIndex, InteriorFloor lvl, RenderGraph rg, Font font,
                         System.Action<int> onJumpToLevel);

        /// <summary>Cheap per-frame reposition of existing visuals from current Room.X/Y — no full teardown of
        /// the view (no ClearLayer/RebuildView-style destroy-and-recreate). Called every cascade frame and
        /// every drag sample. Allocation behaviour is implementer-specific: DungeonFlatRenderer moves its
        /// existing cards with none; SettlementVolumeRenderer re-derives its tile grid each call and reuses a
        /// pooled, appended-only set of tile views.
        ///
        /// ONE TIER. This used to carry an includeRoadsInFence flag, passed the controller's
        /// SettlementRoadsFor(mode), so a settlement's derived fence could skip a ~12.5 ms road A* on drag
        /// frames. The road router is gone (Task 5) and the fence is derived from rooms + stored street cells,
        /// which is cheap on every frame — so there is no tier left to select and the flag went with it.</summary>
        void RepositionRooms(InteriorFloor lvl, RenderGraph rg);

        /// <summary>Turn the selection/link highlight on or off for one room. `roomId` 0 = clear all.</summary>
        void SetHighlight(int roomId, bool on);

        /// <summary>Replace the projection wholesale and redraw (pan/zoom — Task 5). The controller
        /// composes the new value; the renderer stores it and repaints from the level+graph it last drew.
        /// Declared here from the start so Task 5 does not have to reopen this interface.</summary>
        void SetProjection(DungeonProjection p);
    }
}
