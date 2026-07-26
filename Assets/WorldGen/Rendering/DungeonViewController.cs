using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns ALL dungeon-floor editing mechanics: binding, selection, link mode, drag, delete/add, and the
    /// animated cascade. Draws NOTHING — it delegates every visual to an IDungeonRenderer, which the host
    /// installs through SetRenderer (spec R5).
    ///
    /// THE REAL SPLIT (arc C.1). There is no Граф/Изо toggle — that iso renderer was built and dropped in
    /// sub-project 3. The two renderers that exist are chosen by what is being EDITED, not by a view switch,
    /// and a given bind only ever has one right answer: DungeonFlatRenderer draws dungeons and building
    /// interiors as the flat schematic; SettlementVolumeRenderer draws settlements as the 2.5D volumetric
    /// tile view. The host only INSTALLS both (SetRenderers); the Kind-gate itself lives here, in
    /// RendererForKind, so no caller has to remember which interior gets which view.
    ///
    /// The key move that makes one controller drive both: pointer input is resolved down to an AREA-LOCAL
    /// point against the active renderer's own RectTransform, and the renderer maps that point to a room id /
    /// normalized position in ITS OWN coordinate space (IDungeonRenderer.HitRoomId / TryAreaToNorm — Task 6).
    /// The flat renderer does that in DungeonProjection TILE space; the volumetric one inverts the same
    /// projection down to a building-lattice CELL and snaps to it. Neither needs a second editing code path
    /// here, which is exactly why the mapping was pushed behind the interface.
    ///
    /// Sits on the same GameObject hierarchy as before (a child of DungeonEditorScreen.MapArea) and
    /// carries the same rect gotcha: never read a rect at Bind time (it is {0,0} pre-activation) —
    /// ResolveProjection is retried from LateUpdate until it succeeds.
    /// </summary>
    public class DungeonViewController : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<int> OnRoomSelected;   // fires with a room id, or 0 when selection clears
        public System.Action<int> OnRoomDoubleClicked;  // fires with a room id — opens its battle map
        public System.Action OnGraphMutated;        // fires after add/delete/link/drag-end (structural change)
        public System.Action<int> OnJumpToLevel;    // fires with a target level index (badge click)
        // Fires when the room positions have reached their FINAL resting values — either at the end of the
        // animation (Update's completion branch) or immediately from BeginCascade's !anyMoved early return.
        // The host uses it to re-run VALIDATION only: between BeginCascade and this callback the rooms are
        // deliberately rolled back to their pre-settle start positions so they can be animated, so anything
        // that inspects geometry in that window sees a state that is about to be thrown away. Must NOT be
        // wired to anything that mutates the graph or calls BeginCascade again — that would re-enter and loop.
        // BeginCascade's `if (cascading) LandRunningCascade` guard does NOT cover that case and is not meant
        // to: both paths that raise this callback clear `cascading` BEFORE invoking it, so a BeginCascade from
        // here sees no running cascade, starts a fresh one, and raises this callback again. The guard covers
        // the OTHER re-entry — an ordinary edit landing mid-animation (see BeginCascade's own doc).
        public System.Action OnCascadeSettled;
        // Fires whenever «+ Здание» placement mode arms/disarms (Task 8b). The host uses it ONLY to repaint
        // the toolbar button (active look + «Отмена (Esc)» label) — exactly PoiManager.OnPlacementArmedChanged's
        // role on the world map. Must not mutate the graph: DisarmPlacement raises it from inside the commit
        // path, one line before the room is created.
        public System.Action<bool> OnPlacementArmedChanged;

        public int SelectedRoomId { get; private set; }
        public bool LinkMode { get; private set; }
        /// <summary>True while «+ Здание» is armed and the next click on a free tile will create a building
        /// there (Task 8b). Transient UI state — never stored, never serialized, never saved.</summary>
        public bool PlacementArmed { get; private set; }
        /// <summary>True between BeginCascade and OnCascadeSettled — i.e. while the rooms are animating and
        /// their positions are deliberately NOT the settled ones. A host that reads room geometry (the shaft
        /// check is the only such rule today) must wait for OnCascadeSettled instead of reading it now.</summary>
        public bool Cascading => cascading;

        InteriorData dungeon;
        int levelIndex;
        InteriorFloor boundLevel;   // last-bound level OBJECT (not just index) — see Bind's sameBinding check
        Font font;
        IDungeonRenderer renderer;
        // The two renderers the host installs once (SetRenderers, Task 8). The ACTIVE one is `renderer` above
        // and is re-picked from the bound interior's Kind on every Bind — there is no user-facing view toggle.
        // Both may be null in a host that installs only one (SetRenderer still works standalone).
        IDungeonRenderer flatRenderer;
        IDungeonRenderer volumeRenderer;
        int pendingLinkId;

        // ── The pending fit, and WHAT IT COSTS TO APPLY (Task B4 review fix) ────────────────────────────────
        // ONE field, not a bool plus a "needs a rebuild" companion, and that is the point: two bools can latch
        // out of step — clear the first without the second and every later resize silently takes the expensive
        // path again for the rest of the session, restoring the very defect this enum exists to remove. With a
        // single value the stale state is unrepresentable.
        //
        //   Rebuild   — the CONTENT changed (a bind, a renderer swap, «+ Здание», a containment overflow), so
        //               the fit must be applied through Refresh: a new graph, a new fit, a full RebuildView.
        //   FromCache — only the RECT changed (a window/dock resize). The drawn layout is still exactly right;
        //               only the SCALE is wrong. Resolve the projection and repaint the renderer's own cached
        //               level+graph through IDungeonRenderer.SetProjection, routing NOTHING. See LateUpdate.
        //
        // Rebuild always wins a race: a resize arriving while a Rebuild is pending must not downgrade it (the
        // content really did change), which is why OnRectTransformDimensionsChange only writes over None.
        enum PendingFit { None, FromCache, Rebuild }
        PendingFit pendingFit;
        // The bounds the LIVE projection was actually fitted to — written only where a fit is APPLIED, so it
        // can never describe a fit that ResolveProjection refused (a {0,0} rect). RefitIfContentOverflows
        // compares the freshly drawn extent against these; see its doc for why containment, and not mutation,
        // is what re-arms the fit.
        (float minX, float minY, float maxX, float maxY) fittedBounds;
        bool hasFittedBounds;
        // The LAST render graph handed to the renderer, from either build site (Refresh and RepositionNow) —
        // i.e. always the same instance the renderer itself has cached as `lastRg`. Held so the resize re-fit
        // can size a settlement to the roads that are ACTUALLY DRAWN without building a graph of its own; see
        // RefitFromCache for why the two caches cannot disagree at the moment it is read.
        RenderGraph lastBuiltRg;
        int draggingRoomId;
        // The room the current settle should treat as the "just-moved" one: the last room the DM dragged
        // (OnEndDrag) or a freshly added room (AddRoomAtCenter). For a BUILDING it's the room BeginCascade
        // nudges off overlaps; for a DUNGEON it's the leash/Separate anchor. Held here because OnEndDrag has
        // already cleared draggingRoomId by the time OnGraphMutated → BeginCascade runs.
        int lastAnchorRoomId;

        // ── «+ Здание» click-to-place (Task 8b) ─────────────────────────────────────────────────────────
        // THE WHOLE STATE MACHINE, and deliberately no bigger: one bool for "armed", and one cell under the
        // cursor. Everything else about the mode is derived.
        //   • not armed  — the shipped behaviour, untouched (click = select, background click = clear).
        //   • armed      — pressing the settlement toolbar's «+ Здание». Every frame, UpdatePlacement re-reads
        //                  the cell under the pointer into hoverValid/hoverNx/hoverNy/hoverPlaceable and paints
        //                  it green (free) or red (taken). A click COMMITS THAT STORED CELL — see
        //                  PlaceHoveredBuilding for why the click must not re-sample the pointer itself.
        // Ways out (all of them): a successful placement, Esc, pressing the button again, the toolbar being
        // rebuilt (floor/interior switch), the binding ceasing to be a settlement, and OnDisable (leaving the
        // screen). ArmPlacement itself refuses for anything but a settlement drawn by the volumetric renderer,
        // so a dungeon or a building interior can never enter this state at all.
        bool hoverValid;          // false = no cell under the pointer (off the map, or nothing drawn yet)
        float hoverNx, hoverNy;   // the hovered cell's CENTRE, normalized — what a commit writes verbatim
        bool hoverPlaceable;      // the colour currently on screen: true = green, false = red

        // Reflex-double-click guard (Task 8b Minor 2 fix). PlaceHoveredBuilding disarms the mode but does NOT
        // consume the click sequence, so a DM's ordinary reflex double-click lands its second click on the
        // now-disarmed mode, hitting the just-created room with clickCount == 2 — which would immediately fire
        // OnRoomDoubleClicked and open the new building's interior, navigating away from the town the DM only
        // just clicked into. Set the moment a building is placed; the clickCount == 2 branch in OnPointerClick
        // swallows exactly that one pairing when the id matches, and EVERY non-double-click click clears it —
        // the plain fallthrough for a click that hit a room, the id == 0 branch for a click on empty space — so
        // a LATER, deliberate double-click on the same room (necessarily preceded by its own single click,
        // which reaches one of those two first) still opens it normally. Transient only.
        int lastPlacedRoomId;

        // Animated cascade state — moved verbatim from DungeonGraphView (Checkpoint-B tuning, user-approved
        // "мне нравится"). cascadeTargets holds the resolved end position per room id (computed once by
        // DungeonLayout.Separate at cascade start); cascadeVel is SmoothDamp's per-room velocity
        // accumulator. Both null when not cascading. DO NOT retune without a user checkpoint.
        bool cascading;
        Dictionary<int, (float x, float y)> cascadeTargets;
        Dictionary<int, Vector2> cascadeVel;
        const float CascadeSmoothTime = 0.18f;
        const float CascadeDoneEpsilon = 5e-4f;

        // Drag clamp in NORMALIZED space — same 0.04..0.96 margin the old DungeonGraphView.OnCardDragged
        // used, so a room can never be dragged fully off the board.
        const float DragClampMin = 0.04f;
        const float DragClampMax = 0.96f;

        /// <summary>The settlement CELL LATTICE to snap a room position onto, or null when the bound interior
        /// is not a settlement (dungeons and building interiors keep their free, continuous positions).
        ///
        /// WHY THE CONTROLLER RE-SNAPS AT ALL. SettlementVolumeRenderer.TryAreaToNorm already answers with a
        /// cell CENTRE, so a position that came straight out of it is on the lattice. The hazard is every
        /// controller-side write that then MODIFIES that answer — the drag clamp, the drag-settle nudge, and
        /// «+ Здание»'s fixed (0.5, 0.5). One object = one cell is the whole model of this view, and a room
        /// parked between cells would keep its stored position half a cell off the tile it draws on, so
        /// anything that measures from the stored point (street routing, the fence, nearest-neighbour) and
        /// anything that measures from the drawn cell would quietly disagree.
        ///
        /// ORDER NO LONGER MATTERS (it used to, badly). The lattice is now the ABSOLUTE one —
        /// SettlementFootprint's fixed origin, cell (0,0) spanning normalized [0,Pitch) — so
        /// SettlementTileGrid.Allocate derives the same mapping whatever rooms it is handed. Before that,
        /// Allocate anchored on the min-X/min-Y building, which made this method order-critical: a lattice
        /// derived AFTER an off-lattice write was anchored on the very room that needed correcting, so
        /// SnapX/SnapY became an exact no-op against a lattice that had already shifted. That whole failure
        /// mode is gone; the call sites here still capture the lattice first, which is now merely tidy rather
        /// than load-bearing.</summary>
        SettlementTileGrid LatticeFor(InteriorFloor lvl)
            => dungeon != null && dungeon.Kind == InteriorKind.Settlement && lvl != null
                 ? SettlementTileGrid.Allocate(lvl.Rooms)
                 : null;

        /// <summary>Snap one room onto a lattice captured BEFORE it was written. No-op for a null lattice
        /// (non-settlement) or a room that has since vanished.</summary>
        static void SnapToLattice(SettlementTileGrid lattice, Room room)
        {
            if (lattice == null || room == null) return;
            room.X = lattice.SnapX(room.X);
            room.Y = lattice.SnapY(room.Y);
        }

        InteriorFloor BoundLevel =>
            dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Floors.Count
                ? dungeon.Floors[levelIndex] : null;

        // Extra breathing room (tiles) fit around the contour so the blue outline isn't flush against the
        // canvas edge. Beyond ContourMargin (which sets how far the outline sits from the rooms).
        const float ContourViewPad = 2f;

        // ── What a settlement tile actually DRAWS past its own cell (Task B4) ───────────────────────────────
        // The fit describes the GROUND LATTICE and already budgets half a cell around the outermost cell
        // CENTRE — i.e. exactly the cell boundary. How much further the PIXELS reach is a property of the
        // RENDERER, not a constant here: it is a function of `tileOverdraw`, one of the sliders the DM dials
        // live at the Task 9 checkpoint (Range 1.00-1.25). Baking its default in under-budgets by up to ~1.6
        // tiles the moment the slider moves, so both terms are read off the renderer exactly the way
        // ExtrusionHeadroomTiles below already is — see SettlementVolumeRenderer.TileOverhangMin/MaxCells for
        // the derivation and for why a FLAT (Road/Void) tile needs no term at all.
        //   • MIN — west (min-X) and north (min-Y): the tile's own faces. 0.02 cell = 0.179 tiles at the 1.04
        //     default, 0.125 cell = 1.12 tiles at 1.25.
        //   • MAX — east (max-X) and south (max-Y): the drop shadow, which is offset right and DOWN the screen
        //     (+Y in TILE space, since DungeonProjection inverts Y once). 0.238 cell = 2.132 tiles at the
        //     default, 0.359 cell = 3.21 tiles at 1.25.
        // One cell = SettlementGenerator.BuildingCell x DungeonLayout.TilesPerAxis = 8.96 tiles.

        /// <summary>Tile-space bounds to fit the projection to for `lvl`. For a BUILDING the fit is CONSTANT
        /// across floors — floor 0's footprint bbox expanded by the contour margin + a little pad — so every
        /// floor renders at the IDENTICAL scale and position, and the stairwell column (and every room) looks
        /// absolutely the same on every floor (user 2026-07-19). The current floor is NOT unioned in: upper
        /// floors are generated within this outline (nothing is clipped), and floor 0's own rooms ARE the
        /// outline, so a per-floor union would only make the fit jitter between floors.
        ///
        /// SETTLEMENT (Task 8 — this now fits the 2.5D TILE GRID, not the fence polyline). What is drawn for a
        /// town is SettlementTileGrid's cell lattice, and it reaches MUCH further out than the fence polyline
        /// the old fit unioned in, so that fit clipped the wall ring off-screen. The numbers, all in tiles
        /// (one lattice cell = SettlementGenerator.BuildingCell × TilesPerAxis = 8.96 tiles):
        ///   • the wall ring sits CourtyardCells+1 = 2 cells beyond the occupied seed (SettlementTileGrid.
        ///     BuildWallRing's dilation radius);
        ///   • on the CLEAN tier — which is what a bind draws — that seed includes the ROAD cells, and roads
        ///     reach the gates, which sit a building half-width (3) + SettlementFence.FenceMarginTiles (2) =
        ///     5 tiles outside the buildings, i.e. ONE more cell (SettlementRoads' own A* grid is bounded to
        ///     the nodes' bbox + GridMargin 4 tiles, so nothing can bulge past that);
        ///   • each drawn tile extends half a cell past its own centre.
        /// 2 + 1 + 0.5 = 3.5 cells = 31.4 tiles past the outermost building CENTRE — which is exactly the
        /// extent SettlementTileGrid.Allocate already allocates (its MarginCells is 3) taken ±half a cell. So
        /// the fit is read straight off Allocate rather than re-deriving a margin here, and STILL no road
        /// routing happens on the fit path (the old DeriveTownFence(includeRoads: true) call is gone — the
        /// ring's INNER edge at ~17.9 tiles already strictly encloses that polyline at ~5 tiles, so it
        /// contributed nothing but a ~12.5 ms road A* per bind). Task B4 hands Allocate the caller's ALREADY
        /// ROUTED road segments so the fitted grid is the drawn grid — see the rg parameter.
        ///
        /// A WALL-LESS settlement (village/camp) keeps its room-bounds fit, widened by a full cell: it draws no
        /// ring, so the allocated grid's 3-cell margin would shrink it for nothing, but its building tiles are
        /// still a full cell wide where a room footprint is 6 tiles.
        ///
        /// BOTH settlement cases add ExtrusionHeadroomTiles at the NORTH (min-Y) edge: bounds describe the
        /// GROUND plane only and every box is drawn upward out of it, so without it the back row's roofs are
        /// clipped off the top of the panel. The SOUTH edge needs no headroom — a box's front face bottom sits
        /// exactly on its ground footprint edge — but it does need the drop shadow, which hangs below it; that
        /// and the tile overdraw are the TileOverhangMin/MaxCells terms applied at the end.
        ///
        /// Dungeons: the current floor's own bounds — byte-identical to the pre-contour per-floor fit.
        ///
        /// <paramref name="rg"/> (Task B4) is the render graph the caller is ABOUT to draw. It was introduced
        /// to close the fit/draw grid asymmetry against the ROUTED-ROAD renderer this call site originally
        /// fit against; that renderer call signature no longer exists. As of arc C.1 task 2,
        /// SettlementTileGrid.Build(floor) takes no `roads` parameter at all — it calls
        /// Allocate(floor.Rooms, null, streets) internally, folding footprints ∪ STORED STREET CELLS
        /// (SettlementParams.StreetCells) into the cell bbox. This fit still calls
        /// Allocate(lvl.Rooms, RoadsForFit(rg)) — footprints ∪ routed ROAD ENDPOINTS, no streets — so `rg`
        /// remains what widens the fit past the buildings-only extent for the gate/road geometry described
        /// below. Pass null (or a non-settlement binding) and the buildings-only extent is restored verbatim.
        /// NOTHING IS ROUTED HERE: the segments are read out of the graph the caller already built, through
        /// the renderer's own RoadsFromGraph, so this stays a pure O(rooms + segments) pass.</summary>
        (float minX, float minY, float maxX, float maxY) FitBoundsFor(InteriorFloor lvl, RenderGraph rg)
        {
            if (dungeon != null && dungeon.Kind == InteriorKind.Building && dungeon.Floors.Count > 0)
            {
                var c = DungeonProjection.ContentBoundsTiles(dungeon.Floors[0]);
                float pad = FloorFootprint.ContourMargin + ContourViewPad;
                return (c.minX - pad, c.minY - pad, c.maxX + pad, c.maxY + pad);
            }
            if (dungeon != null && dungeon.Kind == InteriorKind.Settlement && lvl != null)
            {
                var (minX, minY, maxX, maxY) = DungeonProjection.ContentBoundsTiles(lvl);
                const float T = DungeonLayout.TilesPerAxis;
                float halfCell = SettlementGenerator.BuildingCell * 0.5f * T;   // 4.48 tiles

                if (lvl.SettlementParams?.HasWall == true)
                {
                    // Allocate ONLY — cheap (two passes over the rooms plus one over the road endpoints, no
                    // dilate/flood-fill/A*) — but with the SAME road list the renderer hands Build, so the
                    // fitted extent is the drawn extent and not a narrower one (see the rg param above). The
                    // union with the room bounds is kept for the same reason the pre-Task-8 fit unioned the
                    // fence in — a room can never be clipped — and it is what carries the degenerate case
                    // below.
                    var g = SettlementTileGrid.Allocate(lvl.Rooms, RoadsForFit(rg));
                    // W == H == 1 is Allocate's documented "no buildings at all" grid, anchored at (0,0) and
                    // NOT at the town: unioning that box in would drag the fit to the corner of the field.
                    // Nothing is drawn in that state anyway, so fall through to the rooms' own bounds.
                    if (g.W > 1 || g.H > 1)
                    {
                        minX = System.Math.Min(minX, g.CenterX(g.OriginI) * T - halfCell);
                        minY = System.Math.Min(minY, g.CenterY(g.OriginJ) * T - halfCell);
                        maxX = System.Math.Max(maxX, g.CenterX(g.OriginI + g.W - 1) * T + halfCell);
                        maxY = System.Math.Max(maxY, g.CenterY(g.OriginJ + g.H - 1) * T + halfCell);
                    }
                }
                else
                {
                    // Village (wall-less) fit margin (review fix, Minor): a village's streets route through
                    // the same SettlementRoads grid (GridMargin = 4), so a road can reach
                    // maxBuildingCentre + 7 tiles — one cell further out than halfCell budgets, which showed
                    // up as a road tile clipped in half at the panel edge. A full cell instead of half a cell
                    // covers it; 8.96 extra tiles of margin on a village is cosmetically negligible.
                    float cell = SettlementGenerator.BuildingCell * T;
                    minX -= cell; minY -= cell; maxX += cell; maxY += cell;
                }

                // The active renderer IS the volumetric one whenever Kind == Settlement: SetRenderers/Bind
                // apply RendererForKind BEFORE anything can call this (Bind writes `dungeon` first, then
                // gates, and SetRenderer assigns `renderer` before its own Refresh). The ?? 0f is therefore
                // unreachable in practice and exists so a host that installed only the flat renderer still
                // fits to something sane instead of throwing.
                var vol = renderer as SettlementVolumeRenderer;
                float headroom = vol?.ExtrusionHeadroomTiles ?? 0f;

                // Everything above budgets the GROUND PLANE out to the cell boundary; these two budget the
                // pixels that hang past it (see the TileOverhang note above the method, and the renderer's own
                // properties, for the derivation). Applied to BOTH settlement cases: a village draws the same
                // extruded, shadow-casting houses a city does, its whole-cell margin merely happens to absorb
                // most of them already. min-Y takes the overhang on TOP of the extrusion headroom rather than
                // instead of it — headroom carries the roof's CENTRE up to h, the overhang covers the extra
                // half-height the roof quad itself adds there.
                float overhangMin = (vol?.TileOverhangMinCells ?? 0f) * SettlementGenerator.BuildingCell * T;
                float overhangMax = (vol?.TileOverhangMaxCells ?? 0f) * SettlementGenerator.BuildingCell * T;
                return (minX - overhangMin, minY - overhangMin - headroom,
                        maxX + overhangMax, maxY + overhangMax);
            }
            return DungeonProjection.ContentBoundsTiles(lvl);
        }

        /// <summary>The road segments the RENDERER will fold into its tile grid, or null when this binding
        /// draws none — mirroring SettlementVolumeRenderer.RepositionRooms' own
        /// `(isSettlement &amp;&amp; includeRoadsInFence) ? RoadsFromGraph(rg) : null` line by line, and calling the
        /// very same conversion so the two cannot drift.
        ///
        /// COSTS NO ROUTING, and that is load-bearing: <paramref name="rg"/> is a graph the caller has ALREADY
        /// built (Refresh builds exactly one per rebuild and hands the same instance to the fit, the
        /// containment check and RebuildView), so this is an O(segments) re-frame — not a second pass through
        /// the ~12.5 ms road A* the two-tier Fast/Clean signal exists to keep off drag frames. A null rg falls
        /// back to the buildings-only extent, which is what the pre-Task-B4 fit always used.</summary>
        IReadOnlyList<LinkSegment> RoadsForFit(RenderGraph rg)
            => rg != null && SettlementRoadsFor(RoomLinkGeometry.RoutingMode.Clean)
                 ? SettlementVolumeRenderer.RoadsFromGraph(rg)
                 : null;

        /// <summary>Re-arm the fit when what is now drawn no longer sits inside what the projection was
        /// actually fitted to. CONTAINMENT is the trigger, never "something changed": re-scaling on every edit
        /// would make the town jump under the cursor, which spec R6 forbids outright. Accepted, and intended:
        /// the view never zooms back IN when a town shrinks — only a rebind or a panel resize does that.
        ///
        /// COST: one SettlementTileGrid.Allocate (two O(rooms) passes, one O(segments) pass and a W×H enum
        /// array — a few hundred cells for a real town) plus four float compares, ONCE PER REBUILD. It routes
        /// NOTHING: the segments come from the graph the caller already built (see RoadsForFit). It is
        /// deliberately not on the per-frame path at all — drag samples and cascade frames go through
        /// RepositionNow, which never reaches Refresh.
        ///
        /// NEVER MID-DRAG. draggingRoomId != 0 is the exact flag OnDrag/OnEndDrag gate on, and `cascading` is
        /// the settle animation's own — together they cover the whole window in which room positions are
        /// transient. Refresh IS reached during a cascade (BeginCascade ends in one), so this guard is live,
        /// not defensive.
        ///
        /// AND BECAUSE IT IS LIVE, Update's completion branch must call this too (review fix). BeginCascade
        /// sets `cascading` BEFORE its own Refresh, so on the anyMoved path this guard rejects that call; the
        /// animation then lands through RepositionNow, not Refresh, and OnCascadeSettled → RevalidateOnly
        /// redraws nothing — so without the second call site the overflow would never be tested at all and the
        /// town would stay clipped until some later rebuild. Not a corner case: this file's own BeginCascade
        /// calls an exact-centre collision "a NORMAL outcome" for a settlement, i.e. dropping a building onto
        /// an occupied cell takes precisely the anyMoved branch. (The !anyMoved branch was always safe — it
        /// clears `cascading` before its Refresh.)
        ///
        /// SETTLEMENTS ONLY — a deliberate narrowing of Task B4's brief, which did not scope it. The brief's
        /// own hard constraint is that dungeons, building interiors and battle grids must behave IDENTICALLY,
        /// and an unscoped check would not leave them so: a dungeon's fit is simply
        /// DungeonProjection.ContentBoundsTiles(lvl), which GROWS the moment any room is dragged outward, and
        /// the drag clamp lets a room travel out to 0.96 normalized — so every outward drag would rescale the
        /// dungeon on settle, where today it fits once per bind and holds (spec R6). A settlement, by
        /// contrast, is the case the DM actually hit, and its content can grow without any drag at all («+
        /// Здание», an auto-linked street, the wall ring re-derived around both). Panel resize is NOT narrowed
        /// this way: OnRectTransformDimensionsChange fixes a genuine clipping bug for every view, and it fires
        /// only when the rect really changed.</summary>
        void RefitIfContentOverflows(InteriorFloor lvl, RenderGraph rg)
        {
            if (!hasFittedBounds || pendingFit != PendingFit.None || lvl == null) return;
            if (dungeon == null || dungeon.Kind != InteriorKind.Settlement) return;
            if (draggingRoomId != 0 || cascading) return;
            const float eps = 1e-3f;
            var b = FitBoundsFor(lvl, rg);
            if (b.minX < fittedBounds.minX - eps || b.minY < fittedBounds.minY - eps ||
                b.maxX > fittedBounds.maxX + eps || b.maxY > fittedBounds.maxY + eps)
                pendingFit = PendingFit.Rebuild;   // the CONTENT grew — a repaint at a new scale is not enough
        }

        /// <summary>Unity sends this on ANY dimension change of THIS component's own RectTransform. That is the
        /// right rect: DungeonEditorScreen creates «DungeonView» with a RectTransform and Stretch()es it
        /// (anchors 0..1, zero offsets) inside MapArea, which is itself stretched inside Body inside the
        /// screen's Root — and the editor canvas is a bare AddComponent&lt;CanvasScaler&gt;(), i.e.
        /// ConstantPixelSize, so that chain tracks the Unity window 1:1. A window resize, a dock/undock, a
        /// maximize-on-play therefore all land here. Both renderers are Stretch()ed children of this same
        /// object, so the rect ResolveProjection measures is congruent to this one by construction.
        ///
        /// The scale used to be computed ONCE per binding with no layout hook anywhere under Assets/WorldGen,
        /// so a resized panel kept a scale fitted to the OLD rect — oversized tiles, the town clipped on three
        /// edges. Re-arming here is the fix.
        ///
        /// ONE LINE, ON PURPOSE. Unity can send this DURING layout; doing work here (a fit, a rebuild) would
        /// re-enter it. Setting the flag cannot — LateUpdate consumes it on the next frame, off the layout
        /// pass, and holds it while a drag or cascade is live.
        ///
        /// FromCache, AND ONLY OVER None (review fix — the Critical one). A resize changes the RECT and
        /// nothing else, so the layout the renderer already holds is still correct at the new scale and
        /// LateUpdate can repaint it without routing; arming Rebuild here would pay one BuildRenderGraph per
        /// RESIZED FRAME — a Clean route for a dungeon or building interior, measured at 106 ms median /
        /// 221 ms max for 20 rooms (.superpowers/sdd/town-scale-measurement.md), i.e. ~5-9 fps while the DM
        /// drags a window edge. Writing only over None is what stops a resize DOWNGRADING a Rebuild that a
        /// real content change armed: N of these in one frame still cost one consume, and a resize during a
        /// pending rebuild keeps the rebuild.</summary>
        void OnRectTransformDimensionsChange()
        {
            if (pendingFit == PendingFit.None) pendingFit = PendingFit.FromCache;
        }

        /// <summary>Install BOTH renderers at once (Task 8) and activate the one the CURRENT binding calls for.
        /// The host builds them; the KIND-GATE lives here, so no caller has to remember which interior gets
        /// which view (spec: "the host does the Kind-gating" was revised to "the host installs, the controller
        /// gates" — one rule, one place, and Bind can re-apply it when the Kind changes under a live screen).
        ///
        /// The explicit deactivation is LOAD-BEARING and not something SetRenderer can do for us: SetRenderer
        /// only ever deactivates the OUTGOING renderer, and at install time there is none — so the renderer we
        /// are not choosing would stay active and draw its own visuals straight on top of the chosen one (flat
        /// room cards over the 2.5D tiles). The `!ReferenceEquals(..., want)` guard matters just as much in the
        /// other direction: SetRenderer early-returns when the wanted renderer is already active, so
        /// deactivating it here unconditionally would leave the view permanently blank.</summary>
        public void SetRenderers(IDungeonRenderer flat, IDungeonRenderer volume)
        {
            flatRenderer = flat;
            volumeRenderer = volume;
            var want = RendererForKind();
            if (flat != null && flat.Host != null && !ReferenceEquals(flat, want)) flat.Host.SetActive(false);
            if (volume != null && volume.Host != null && !ReferenceEquals(volume, want)) volume.Host.SetActive(false);
            SetRenderer(want);
        }

        /// <summary>The renderer the CURRENT binding calls for: a settlement (town/city) draws 2.5D volumetric
        /// tiles, everything else — dungeons and building interiors — keeps the flat schematic. Falls back to
        /// the flat renderer whenever the volumetric one was never installed, so a host that wires only one
        /// (or a headless/test host) degrades to the pre-Task-8 behaviour instead of drawing nothing.</summary>
        IDungeonRenderer RendererForKind()
            => dungeon != null && dungeon.Kind == InteriorKind.Settlement && volumeRenderer != null
                 ? volumeRenderer
                 : flatRenderer;

        /// <summary>Swap the active renderer (Граф ⇄ Изо). Deactivates the old host, activates the new,
        /// re-fits the new renderer's projection to the bound level and rebuilds it. Selection, link mode
        /// and cascade state all survive the swap — they live here, not in the renderer.</summary>
        public void SetRenderer(IDungeonRenderer next)
        {
            if (next == null || ReferenceEquals(next, renderer)) return;
            if (renderer != null && renderer.Host != null) renderer.Host.SetActive(false);
            renderer = next;
            if (renderer.Host != null) renderer.Host.SetActive(true);
            // Rebuild, not FromCache: the INCOMING renderer has drawn nothing yet, so it has no cache to
            // repaint. Its rect may also not have laid out — LateUpdate retries.
            pendingFit = PendingFit.Rebuild;
            Refresh();
        }

        /// <summary>(Re)bind to a level and rebuild. Selection/link state resets only when the dungeon or
        /// the bound InteriorFloor OBJECT actually changes — a same-level re-Bind (DungeonEditorScreen
        /// re-Binds on every refresh) preserves the current selection instead of stomping it. Keying on the
        /// OBJECT, not the index: RemoveCurrentLevel re-binds the same index to a DIFFERENT level, and
        /// keying on the index alone would let a stale SelectedRoomId match an unrelated room.</summary>
        public void Bind(InteriorData dungeon, int levelIndex, Font font)
        {
            var newLevel = (dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Floors.Count)
                ? dungeon.Floors[levelIndex] : null;
            bool sameBinding = this.dungeon == dungeon && this.levelIndex == levelIndex && newLevel == boundLevel;
            this.dungeon = dungeon;
            this.levelIndex = levelIndex;
            this.font = font;
            boundLevel = newLevel;
            if (!sameBinding)
            {
                SelectedRoomId = 0;
                pendingLinkId = 0;
                // Same stale-id-across-bind hazard as SelectedRoomId: a leftover anchor from the previous
                // level may coincidentally match a room here, making this level's first cascade depend on
                // what was dragged before the switch. Fall back to the leash's own deterministic root.
                lastAnchorRoomId = 0;
                // A stale cascade from the PREVIOUS level must not keep running against the new one
                // (wrong ids, wrong targets) — cancel outright on a genuine level switch.
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                // Re-fit the scale to the new level's content (spec R6: fit once per bind, then hold). Rebuild:
                // the content is a DIFFERENT level, so the renderer's cached layout is the wrong one to repaint.
                pendingFit = PendingFit.Rebuild;
            }
            // Kind-gate the renderer (Task 8). Deliberately AFTER the field writes above: SetRenderer calls
            // Refresh() itself, and Refresh reads `dungeon` / `boundLevel` — gating before the writes would
            // paint the PREVIOUS binding into the new renderer for one frame, and FitBoundsFor would fit it to
            // the previous interior's bounds. When the right renderer is already active this is a no-op
            // (SetRenderer early-returns on ReferenceEquals), which is the normal case: the Kind only changes
            // when the DM opens a different interior in the same screen (town ⇄ its building's own interior).
            var want = RendererForKind();
            bool swapped = want != null && !ReferenceEquals(want, renderer);
            SetRenderer(want);
            // SetRenderer already rebuilt through Refresh() when it actually swapped; a second Refresh here
            // would re-route a settlement's roads (~12.5 ms) for nothing on every town open.
            if (!swapped) Refresh();
        }

        /// <summary>Full visual rebuild from the bound level — and THE one place a fit is applied (Task B4).
        ///
        /// The render graph is now built BEFORE the fit, where it used to be built after, and the order is
        /// load-bearing rather than cosmetic: for a settlement the fit has to size itself to the same tile grid
        /// this very graph's road segments are about to widen (FitBoundsFor's rg param). BuildRenderGraph is
        /// pure in `lvl` — it reads room positions and links and nothing about the projection — so moving it
        /// ahead of ResolveProjection changes no output, and it is built EXACTLY ONCE either way: the same
        /// instance feeds the fit, RebuildView, the containment check — and lastBuiltRg, so a later resize
        /// re-fit can size itself to this same graph without building one of its own.
        ///
        /// THIS IS THE EXPENSIVE PATH, and only content changes should reach it: BuildRenderGraph is the
        /// 106 ms-at-20-rooms Clean route for a dungeon or building interior. A pure RESIZE goes through
        /// RefitFromCache instead — see PendingFit.</summary>
        public void Refresh()
        {
            if (renderer == null) return;
            var lvl = BoundLevel;
            if (lvl == null)
            {
                lastBuiltRg = new RenderGraph();
                renderer.RebuildView(dungeon, levelIndex, null, lastBuiltRg, font, OnJumpToLevel);
                return;
            }

            var rg = lastBuiltRg = DungeonLayout.BuildRenderGraph(lvl, RouteMode(RoomLinkGeometry.RoutingMode.Clean), SettlementRoadsFor(RoomLinkGeometry.RoutingMode.Clean));

            bool justFitted = false;
            if (pendingFit != PendingFit.None)
            {
                var b = FitBoundsFor(lvl, rg);
                if (renderer.ResolveProjection(b.minX, b.minY, b.maxX, b.maxY))
                {
                    // A Refresh satisfies EITHER kind of pending fit — it rebuilds as well as re-fits — so the
                    // whole state clears here, in one write. (That single write is the reason PendingFit is one
                    // field: a separate "needs a rebuild" bool left un-cleared beside this line would latch, and
                    // every resize for the rest of the session would take the routing path again.)
                    pendingFit = PendingFit.None;
                    // Record what the LIVE projection is fitted to, at the only moment it is known to have
                    // been accepted. A refused fit (rect still {0,0}) deliberately leaves both untouched.
                    fittedBounds = b;
                    hasFittedBounds = true;
                    justFitted = true;
                }
            }

            if (SelectedRoomId != 0 && lvl.GetRoom(SelectedRoomId) == null) SelectedRoomId = 0;
            renderer.RebuildView(dungeon, levelIndex, lvl, rg, font, OnJumpToLevel);
            RefreshHighlights();
            // Skipped when this call just fitted: the bounds were computed from this very rg, so the check
            // could only re-derive them and find them equal to themselves.
            if (!justFitted) RefitIfContentOverflows(lvl, rg);
        }

        /// <summary>Consumes a pending fit, one frame after whatever armed it — down ONE of two paths, chosen
        /// by what armed it (review fix — the Critical one).
        ///
        ///   Rebuild (a bind, a renderer swap, «+ Здание», a containment overflow) → Refresh, i.e. a fresh
        ///     BuildRenderGraph, a fit computed from it, and a full RebuildView. Unchanged, and it must stay
        ///     that way: the content really did change, so there is nothing correct to repaint from.
        ///   FromCache (a window/dock resize) → RefitFromCache: resolve the projection and repaint the
        ///     renderer's cached layout, routing NOTHING.
        ///
        /// The split exists because a resize is the one arming site that fires ONCE PER CHANGED FRAME for as
        /// long as the DM holds the window edge. Sending that down Refresh costs one BuildRenderGraph per
        /// frame — Clean for a dungeon or a building interior (settlements alone are forced Fast by RouteMode),
        /// measured at 106 ms median / 221 ms max at 20 rooms and 2.9 s at 40
        /// (.superpowers/sdd/town-scale-measurement.md), with DungeonEditorScreen's upper-floor stepper capped
        /// at exactly 20 and a hand-grown dungeon not capped at all. That is a hang, not a jank. Deferring by a
        /// frame would not help: it still pays a full route at the end of every resize.
        ///
        /// A settlement is the same shape and milder — RouteMode forces Fast, but SettlementRoadsFor(Clean) is
        /// still true, so Refresh would pay one road grid A* (12.5 ms median, 17.5 max —
        /// .superpowers/sdd/roads-perf-spike.md) plus a full rebuild per frame. Same fix, same path.
        ///
        /// The rect pre-check is what the removed ResolveProjection call used to give us for free, and it is
        /// kept for the same reason: while the panel is still {0,0} (the bind-time rect gotcha) there is
        /// nothing to fit to, and falling through would rebuild — and, for a settlement, re-route — every
        /// frame until layout catches up. It gates BOTH paths, since the cheap one cannot fit to a {0,0} rect
        /// either; RefitFromCache's own refusal branch is then only ever reached on a rect that changed
        /// between this check and the resolve.
        ///
        /// IT IS THE UNION OF BOTH RENDERERS' OWN READINESS TESTS, and it has to be at least that strict or
        /// this becomes an editor hang rather than a fix. DungeonFlatRenderer.ResolveProjection tests
        /// `rect.width &lt;= 0f || rect.height &lt;= 0f` (its Area is a hard cast, so no null branch);
        /// SettlementVolumeRenderer.ResolveProjection tests `area == null` as well. This tests both, so it can
        /// only ever be STRICTER than the renderer it is gating — and a stricter pre-check merely defers a fit
        /// by a frame. The dangerous direction is the other one: were this LOOSER, a ResolveProjection that
        /// refused would leave pendingFit armed while this method kept calling Refresh — a full card teardown
        /// plus a Clean corridor route — every frame, forever. Widen this, never narrow it.
        ///
        /// HOLDS THE SCALE while a drag or a settle animation is live: the flag simply stays armed and the
        /// next frame after the cursor lifts consumes it.</summary>
        void LateUpdate()
        {
            if (pendingFit == PendingFit.None || renderer == null) return;
            if (draggingRoomId != 0 || cascading) return;
            var lvl = BoundLevel;
            if (lvl == null) return;
            var area = renderer.Area;
            if (area == null || area.rect.width <= 0f || area.rect.height <= 0f) return;   // retry next frame
            if (pendingFit == PendingFit.Rebuild) { Refresh(); return; }
            RefitFromCache(lvl);
        }

        /// <summary>Apply a RESIZE-armed fit: re-scale the projection, then repaint what the renderer already
        /// holds. NO ROUTING HAPPENS ANYWHERE ON THIS PATH — that is its entire reason to exist, and it is
        /// verifiable by inspection: there is no BuildRenderGraph call here, none in FitBoundsFor (RoadsForFit
        /// is a pure O(segments) re-frame of an existing graph), and none in IDungeonRenderer.SetProjection,
        /// which repaints from the renderer's own cached lvl/rg.
        ///
        /// ResolveProjection THEN SetProjection, and the apparent redundancy is deliberate. ResolveProjection
        /// is the only thing that knows how to turn tile-space bounds + this renderer's rect into a projection
        /// (the volumetric one folds in its own serialized tiltSquash, which is private to it), and it writes
        /// the result straight onto Projection — but it does not repaint. SetProjection is the repaint-from-
        /// cache primitive. Handing it back the projection ResolveProjection just stored is therefore a no-op
        /// assignment followed by exactly the redraw we want, and it needs no new interface member.
        ///
        /// THE TWO CACHES CANNOT DISAGREE, which is what makes feeding the fit from lastBuiltRg sound. Every
        /// site that builds a graph — Refresh and RepositionNow, the only two — stores the SAME INSTANCE into
        /// lastBuiltRg and hands it to the renderer, which keeps it as its own lastRg. So the roads this fit
        /// reads are literally the roads the repaint below rasterizes, and neither side routes to obtain them.
        /// The one graph that is not settle-equivalent — a Fast, road-less drag/cascade frame — can never be
        /// the one read here: LateUpdate's `draggingRoomId != 0 || cascading` guard holds the pending fit for
        /// exactly the window in which such a graph is the newest, and every exit from that window (Update's
        /// completion RepositionNow(Clean), BeginCascade's !anyMoved Refresh) replaces it with a Clean one
        /// first.
        ///
        /// A REFUSED fit (rect still {0,0}) leaves pendingFit armed and repaints nothing — LateUpdate's own
        /// pre-check makes that near-unreachable, and a retry next frame is the correct fallback either way.
        /// No containment check follows: the bounds were just derived from the very graph that is being
        /// repainted, so it could only compare them with themselves.</summary>
        void RefitFromCache(InteriorFloor lvl)
        {
            var b = FitBoundsFor(lvl, lastBuiltRg);
            if (!renderer.ResolveProjection(b.minX, b.minY, b.maxX, b.maxY)) return;
            pendingFit = PendingFit.None;
            fittedBounds = b;
            hasFittedBounds = true;
            renderer.SetProjection(renderer.Projection);   // repaint from the renderer's cache — routes nothing
        }

        // ── Cascade (moved verbatim from DungeonGraphView.cs:151-241) ────────────

        /// <summary>Entry point for the room-cascade separation. Snapshots current positions, resolves the
        /// target layout via Separate (which mutates rooms to their end positions), then either self-skips
        /// (nothing moved — a link/delete edit that never overlapped) with one static redraw, or restores
        /// the start positions and animates to the captured targets in Update() via SmoothDamp.
        ///
        /// RE-ENTRANT DURING AN ANIMATION (final-review fix). This is genuinely reachable — the cascade runs
        /// ~0.18 s and any OnChanged edit in that window (an inspector field, a size stepper) routes through
        /// DungeonEditorScreen.RevalidateAndRefresh straight back to here. Room X/Y are then MID-FLIGHT
        /// SmoothDamp values, which used to be snapshotted as both `start` and (via Separate, which mostly
        /// no-ops on an already-separated floor) `targets` — freezing the rooms wherever the animation happened
        /// to be. Worse for a settlement: LatticeFor would be anchored on a min-X/min-Y building that is itself
        /// off-lattice mid-flight, and SnapToLattice would then be an exact no-op against a shifted lattice —
        /// the precise hazard LatticeFor's own doc warns about, leaving a room permanently between cells and
        /// sliding the whole town's cell indices by up to half a cell.</summary>
        public void BeginCascade()
        {
            var lvl = BoundLevel;
            if (lvl == null) return;

            // Snapshot FIRST, land SECOND — the order is the whole point. `start` keeps the mid-flight
            // positions, so the animation resumes visually from where it is instead of jumping; the landing
            // below then puts the ROOM DATA on the settled positions the in-flight run was converging to, so
            // everything the resolve step reads (Separate's overlaps, LatticeFor's anchor) sees settled,
            // on-lattice geometry.
            var start = new Dictionary<int, (float x, float y)>();
            foreach (var r in lvl.Rooms) start[r.Id] = (r.X, r.Y);

            // RESTART, NOT IGNORE. Ignoring a re-entrant call would silently drop the new edit's settle (its
            // anti-overlap nudge above all) and leave the old cascade converging on targets computed before
            // that edit existed. Restarting from the landed state costs one extra resolve and keeps the
            // invariant that every BeginCascade produces a settle for the state it was called on.
            if (cascading) LandRunningCascade(lvl);

            if (dungeon != null && (dungeon.Kind == InteriorKind.Building || dungeon.Kind == InteriorKind.Settlement))
            {
                // BUILDING (spec C4, revised 2026-07-19): a dragged room STAYS exactly where the DM dropped
                // it — NO floor re-pack, NO cascade of other rooms (the user's hard rule: dragging a room
                // must never move another). The sole correction is anti-overlap on the just-moved room itself
                // (lastAnchorRoomId): if it landed on top of another room, IT ALONE is shoved clear. A room
                // parked outside floor 0's contour is LEFT there (C2' red-flags it) — out-of-contour is a
                // deliberate choice, not auto-fixed. This mutates at most that one room's X/Y, so the shared
                // snapshot→rollback→SmoothDamp path below animates only its small nudge (every other room has
                // start==target and does not move). Floor 0 and upper floors behave identically now (no
                // per-floor contour containment).
                //
                // SETTLEMENT (final-review fix I3): a settlement's buildings are placed non-overlapping BY
                // CONSTRUCTION (PlaceBuildings) and its "corridors" are trunk/branch streets spanning up to
                // ~80 tiles — far past DungeonLayout.MaxCorridorTiles (8). Falling into the DUNGEON branch
                // below (Separate → EnforceCorridorLeash → Separate) violently yanked every linked building
                // toward the dragged one and collapsed the whole town on the FIRST BeginCascade, which fires
                // on drag-end AND on any OnChanged edit (e.g. attaching a building preview image) — so this
                // could fire before the DM ever dragged anything. A settlement wants exactly the BUILDING
                // treatment: stays put, anti-overlap only, never leashed. This is safe to share verbatim:
                // SettleDraggedRoom → RealignUpperFloorsToColumn early-returns on Floors.Count <= 1 (a
                // settlement has exactly one floor), so it is a no-op for a settlement, and
                // NudgeRoomOffOverlaps early-returns for an unresolved/zero room id (e.g. a non-drag edit,
                // where lastAnchorRoomId is stale or 0) — so a pure OnChanged edit with no drag moves nothing
                // and this branch redraws statically, exactly fixing the "adding a preview image collapses
                // the town" symptom. NudgeRoomOffOverlaps is O(n) over the floor's own room list (a plain
                // overlap scan, not a routing pass), so this introduces no new cost even at 40-80 buildings.
                //
                // The shaft auto-realign (commit e61473a) is PART of this step and lives inside
                // SettleDraggedRoom, not at the RevalidateAndRefresh call site above it: the nudge can move
                // floor 0's stairwell column (the DM may drag the Лестница itself), so a realign that ran
                // BEFORE it would sync the upper floors to a position the column is about to leave. The
                // ordering is pinned headlessly by BuildingGeneratorSelfTests.SelfTestDragSettleOrdering.
                //
                // Realigning here — before the target snapshot below — puts the shaft back in sync as part of
                // the same settle. NOTE what it does NOT do: the snapshot/rollback/animate machinery below
                // only ever sees `lvl.Rooms`, i.e. the CURRENTLY VIEWED floor. When the viewed floor is floor
                // 0 (the usual case — only floor 0 is draggable), the upper-floor rooms this realign
                // translates are in NO snapshot: they are neither rolled back nor animated, they simply jump
                // to their settled positions immediately. That is fine visually (they are off-screen) but it
                // means the viewed floor and the rest of the building are momentarily inconsistent — floor 0
                // sits at its rolled-back DROP position while the upper floors already sit at the SETTLED
                // one. Any shaft check run in that window sees a mismatch of the nudge distance and reports a
                // false «лестница не совпадает со столбом». Hence OnCascadeSettled: the host re-validates only
                // once the animation has landed. Do not move validation back before the animation.
                //
                // SETTLEMENT re-snap (Task 8, handoff b). NudgeRoomOffOverlaps measures in TILES and shoves by
                // the penetration depth, which is never a whole number of cells — so for a settlement the
                // nudge is the second controller-side write that lands a room off the lattice, and the anchor
                // hazard in LatticeFor applies in full. The nudge is deliberately RUN and then corrected,
                // NOT skipped: cell snapping makes an exact-centre collision a NORMAL outcome (drop a building
                // on an occupied cell and the two rooms share one X/Y exactly), and two rooms at one position
                // are not merely ugly — HitRoomId's tie-break (larger Y, then larger Id) then returns the SAME
                // one forever, so the other can never be selected or dragged again. With the nudge, identical
                // centres give overlapX == overlapY, which takes the Y branch and shoves a full room width
                // (6.005 tiles) — past the half-cell (4.48) rounding threshold — so the re-snap lands it on
                // the ADJACENT cell rather than back on top. Residual, not fixed here: if that neighbour is
                // occupied too, the least-penetration shove oscillates into PlaceOutwardFromPoint's nearest-
                // free-slot fallback, whose position can still round back onto an occupied cell.
                var lattice = LatticeFor(lvl);   // null for a Building — captured BEFORE the nudge moves anything
                BuildingGenerator.SettleDraggedRoom(dungeon, lvl, lastAnchorRoomId);
                // Only lastAnchorRoomId can have moved: NudgeRoomOffOverlaps moves that room alone, and
                // RealignUpperFloorsToColumn early-returns for a settlement's single floor.
                SnapToLattice(lattice, lvl.GetRoom(lastAnchorRoomId));
            }
            else
            {
                // DUNGEON — byte-identical to the pre-C4 cascade; do not touch (user re-verifies at the
                // checkpoint that dungeon drag/settle is unchanged).
                //
                // Separate → leash → Separate. Separate resolves overlap but knows nothing about corridors, so
                // on its own it can leave a corridor stretched past the leash with nothing to pull it back
                // (the leash otherwise only runs during a drag). The leash pass then re-pulls, and the second
                // Separate cleans up any overlap that pull introduced.
                //
                // Best-effort, NOT a proven joint fixpoint: the final Separate could in principle re-stretch a
                // corridor a little. Both passes are convergent and cheap, and any residual is small and
                // cosmetic — the alternative is a combined solver, which this does not need.
                DungeonLayout.Separate(lvl);
                DungeonLayout.EnforceCorridorLeash(lvl, lastAnchorRoomId);
                DungeonLayout.Separate(lvl);   // mutates rooms to resolved target positions
            }

            const float eps = 1e-4f;
            var targets = new Dictionary<int, (float x, float y)>();
            bool anyMoved = false;
            foreach (var r in lvl.Rooms)
            {
                targets[r.Id] = (r.X, r.Y);
                if (start.TryGetValue(r.Id, out var s) &&
                    (Mathf.Abs(s.x - r.X) > eps || Mathf.Abs(s.y - r.Y) > eps))
                    anyMoved = true;
            }

            if (!anyMoved)
            {
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                Refresh();
                // Nothing to animate — the settled state IS the current state, so the "settled" moment is
                // now. Fires here too so the host has ONE re-validation hook that covers every path out of
                // BeginCascade (an upper floor can still have been realigned even when the viewed floor
                // itself did not move).
                OnCascadeSettled?.Invoke();
                return;
            }

            // Roll rooms back to their pre-Separate start so Update() can animate start → target.
            foreach (var r in lvl.Rooms)
                if (start.TryGetValue(r.Id, out var s)) { r.X = s.x; r.Y = s.y; }

            cascadeTargets = targets;
            cascadeVel = new Dictionary<int, Vector2>();
            foreach (var r in lvl.Rooms) cascadeVel[r.Id] = Vector2.zero;
            cascading = true;

            Refresh();   // draw the restored start state now; Update() takes over from here
        }

        /// <summary>End an in-flight cascade IMMEDIATELY by writing every room onto the target it was
        /// converging to, then clearing the cascade state. Exactly Update()'s completion branch minus the two
        /// things that would be wrong here: no redraw (the BeginCascade this serves ends in a Refresh of its
        /// own either way) and NO OnCascadeSettled — that callback means "the rooms have reached their final
        /// resting values", which is not true yet, since the caller is about to resolve a new layout. The new
        /// cascade fires it on its own completion (or straight away via the !anyMoved path), so every
        /// BeginCascade still ends in exactly one settled signal.
        ///
        /// A room ADDED since the cascade began has no entry in cascadeTargets and simply keeps its position —
        /// it was never animating.</summary>
        void LandRunningCascade(InteriorFloor lvl)
        {
            if (cascadeTargets != null)
                foreach (var r in lvl.Rooms)
                    if (cascadeTargets.TryGetValue(r.Id, out var target)) { r.X = target.x; r.Y = target.y; }
            cascading = false;
            cascadeTargets = null;
            cascadeVel = null;
        }

        void Update()
        {
            // Task 8b — unconditionally first, and self-gated on PlacementArmed. Update() early-returns just
            // below when nothing is animating, which is the normal state while the DM is placing a building,
            // so the hover MUST be sampled before that return or the preview would only track the cursor
            // during a cascade.
            UpdatePlacement();
            if (!cascading) return;
            var lvl = BoundLevel;
            if (lvl == null || cascadeTargets == null || renderer == null)
            {
                cascading = false; cascadeTargets = null; cascadeVel = null;
                return;
            }

            float maxRemaining = 0f;
            foreach (var r in lvl.Rooms)
            {
                if (!cascadeTargets.TryGetValue(r.Id, out var target)) continue;
                Vector2 cur = new Vector2(r.X, r.Y);
                Vector2 tgt = new Vector2(target.x, target.y);
                Vector2 vel = cascadeVel.TryGetValue(r.Id, out var v) ? v : Vector2.zero;
                Vector2 next = Vector2.SmoothDamp(cur, tgt, ref vel, CascadeSmoothTime);
                cascadeVel[r.Id] = vel;

                r.X = Mathf.Clamp01(next.x);
                r.Y = Mathf.Clamp01(next.y);
                maxRemaining = Mathf.Max(maxRemaining, (tgt - next).magnitude);
            }
            RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Fast);

            if (maxRemaining < CascadeDoneEpsilon)
            {
                // Snap exactly to target (SmoothDamp asymptotically approaches but never reaches it).
                foreach (var r in lvl.Rooms)
                {
                    if (!cascadeTargets.TryGetValue(r.Id, out var target)) continue;
                    r.X = target.x; r.Y = target.y;
                }
                RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Clean);
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                // THE OVERFLOW TEST FOR THE ANIMATED PATH (review fix). This is the only place it can run for a
                // settle that actually moved something: BeginCascade's own Refresh happens with `cascading`
                // already true, so RefitIfContentOverflows rejects it there, and nothing else redraws before
                // the animation lands here. Deliberately AFTER the three clears above — the check guards on
                // `cascading` too — and BEFORE OnCascadeSettled, so a host handler that rebuilds sees the fit
                // already armed. lastBuiltRg is the graph RepositionNow just built one line up, i.e. the drawn
                // one; the check routes nothing.
                RefitIfContentOverflows(lvl, lastBuiltRg);
                // Rooms are now EXACTLY on their targets — the first moment the whole interior is
                // self-consistent again (see BeginCascade's building branch). Fire last, after the state is
                // cleared, so a handler that somehow re-enters cannot see a half-torn-down cascade.
                OnCascadeSettled?.Invoke();
            }
        }

        void RepositionNow(InteriorFloor lvl, RoomLinkGeometry.RoutingMode mode)
        {
            if (renderer == null || lvl == null) return;
            // ONE signal drives both the map's roads and the fence's roads, so they can never disagree: false on
            // Fast/drag frames (fence skips the road A*, per .superpowers/sdd/roads-perf-spike.md), true on the
            // Clean settle. This is what keeps an 80-building walled-city drag off the 12.5 ms road router.
            bool includeRoads = SettlementRoadsFor(mode);
            // Cached for the resize re-fit, which must size itself to the graph the renderer is ACTUALLY
            // holding — see RefitFromCache. Storing it at BOTH build sites (here and Refresh) is what keeps
            // the two caches the same instance: a settle lands through this method, not through Refresh, so
            // caching in Refresh alone would leave the fit reading a graph built from pre-animation positions.
            lastBuiltRg = DungeonLayout.BuildRenderGraph(lvl, RouteMode(mode), includeRoads);
            renderer.RepositionRooms(lvl, lastBuiltRg, includeRoads);
        }

        // A settlement's link graph is large (40–80 nodes); BuildRenderGraph's Clean mode measured 20–34 s
        // at 60 nodes (see .superpowers/sdd/town-scale-measurement.md), so a settlement ALWAYS routes Fast
        // (6 ms). Dungeons/buildings keep their requested mode.
        RoomLinkGeometry.RoutingMode RouteMode(RoomLinkGeometry.RoutingMode requested)
            => dungeon != null && dungeon.Kind == InteriorKind.Settlement
                 ? RoomLinkGeometry.RoutingMode.Fast
                 : requested;

        // Ц1.6: settlements route ROADS on the settle path (bind + cascade-settle, the Clean-requests)
        // ALWAYS; during live drag/cascade frames (the Fast-requests) only if the perf spike cleared the
        // frame budget (.superpowers/sdd/roads-perf-spike.md). When false, drag frames keep the cheap
        // L/Z scorer — roads may transiently cross houses WHILE DRAGGING and snap clean on settle,
        // exactly the dungeon's own Fast[drag]/Clean[settle] pattern.
        const bool RoadsDuringDrag = false;

        bool SettlementRoadsFor(RoomLinkGeometry.RoutingMode requested)
            => dungeon != null && dungeon.Kind == InteriorKind.Settlement
               && (RoadsDuringDrag || requested == RoomLinkGeometry.RoutingMode.Clean);

        // ── Commands ─────────────────────────────────────────────────────────────

        public void SetLinkMode(bool on)
        {
            LinkMode = on;
            pendingLinkId = 0;
            RefreshHighlights();
        }

        // ── «+ Здание» placement mode (Task 8b) ─────────────────────────────────────────────────────────

        /// <summary>Can this binding place buildings by click at all? SETTLEMENTS ONLY, and only while the
        /// volumetric renderer is the active one — that renderer is what owns the cell lattice, the tile types
        /// the green/red test reads, and the preview quad. A dungeon or a building interior fails this and can
        /// therefore never arm, which is the single gate that keeps their «+ Комната» behaviour (including
        /// AttachNewRoom's "placement is FINAL" rule) byte-identical to what shipped.</summary>
        public bool SupportsClickPlacement
            => dungeon != null && dungeon.Kind == InteriorKind.Settlement
               && renderer is SettlementVolumeRenderer;

        public void ArmPlacement()
        {
            if (PlacementArmed || !SupportsClickPlacement) return;
            PlacementArmed = true;
            hoverValid = false;   // no cell sampled yet — a click before the first Update places nothing
            OnPlacementArmedChanged?.Invoke(true);
        }

        public void DisarmPlacement()
        {
            if (!PlacementArmed) return;
            PlacementArmed = false;
            hoverValid = false;
            (renderer as SettlementVolumeRenderer)?.HidePlacementHighlight();
            OnPlacementArmedChanged?.Invoke(false);
        }

        /// <summary>The «+ Здание» button's whole job — arm, or cancel if already armed. Same shape as
        /// PoiManager.TogglePlacement on the world map, so the two screens behave identically.</summary>
        public void TogglePlacement()
        {
            if (PlacementArmed) DisarmPlacement();
            else ArmPlacement();
        }

        /// <summary>Leaving the screen cancels placement. ScreenSwitcher deactivates the whole
        /// DungeonEditorScreen GameObject, and this controller is a descendant of it, so this is the one hook
        /// that catches EVERY exit — «← Назад», opening a building interior, a battle map, the POI editor —
        /// without each of them having to remember. Mirrors PoiToolPanel.OnDisable's own DisarmPlacement.
        /// Also the reason no highlight can leak: a disarm always hides the quad.</summary>
        void OnDisable() => DisarmPlacement();

        /// <summary>ONE frame of the armed mode: cancel key, then re-sample the cell under the cursor and
        /// repaint the preview. Runs from Update (see the call site) — i.e. before this frame is rendered, so
        /// the quad the DM sees is always THIS frame's cursor position, never last frame's.
        ///
        /// Input System throughout (Keyboard.current / Mouse.current): legacy UnityEngine.Input compiles but
        /// THROWS at runtime in this project — BrushToolController.HandleUndo is the template, including its
        /// "no device, do nothing" guard.
        ///
        /// Containment is RectangleContainsScreenPoint against the renderer's own rect, NOT
        /// EventSystem.IsPointerOverGameObject(): the controller's own hit-plate IS a raycast target covering
        /// the whole map area, so that test is true everywhere it matters and would suppress the preview
        /// entirely.</summary>
        void UpdatePlacement()
        {
            if (!PlacementArmed) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { DisarmPlacement(); return; }

            // The binding can change under an armed mode (a rebind to a different interior, a renderer swap).
            // One guard covers every such case, and disarming is the honest answer: the cell lattice the mode
            // is about no longer exists.
            var vol = renderer as SettlementVolumeRenderer;
            var lvl = BoundLevel;
            if (!SupportsClickPlacement || vol == null || lvl == null) { DisarmPlacement(); return; }

            // Invalidate FIRST. Every early return below therefore leaves the mode armed but with NO cell to
            // commit, which is exactly right: pointer off the map, no mouse, or nothing drawn yet all mean
            // "there is no highlighted cell", and a click must then do nothing rather than fall back to a
            // stale one.
            hoverValid = false;

            var area = vol.Area;
            if (Mouse.current == null || area == null) { vol.HidePlacementHighlight(); return; }
            Vector2 screen = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(area, screen, null)
                || !TryScreenToAreaLocal(screen, out var local)
                || !vol.TryPlacementCell(local, lvl, out float nx, out float ny, out bool placeable))
            {
                vol.HidePlacementHighlight();
                return;
            }

            hoverValid = true; hoverNx = nx; hoverNy = ny; hoverPlaceable = placeable;
            vol.ShowPlacementHighlight(nx, ny, placeable);
        }

        /// <summary>Commit «+ Здание» onto the cell the preview is CURRENTLY showing — the stored hover cell,
        /// never a fresh sample of the pointer. That distinction is the feature's whole contract ("the building
        /// lands exactly on the cell that was highlighted"), and it is not merely stylistic: EventSystem's
        /// update runs at an UNDEFINED order relative to this component's Update, so a click handler that
        /// re-derived the cell could resolve a pointer sample the DM never saw painted. Committing the stored
        /// value commits the cell that was actually on screen when they pressed.
        ///
        /// A red (occupied) cell, or no cell at all, does NOTHING and leaves the mode armed — the DM simply
        /// clicks somewhere else (user decision 2).
        ///
        /// NO ANTI-OVERLAP NUDGE ON THIS PATH, and provably so rather than incidentally: the cell was verified
        /// free, so there is nothing to resolve, and a nudge would move the building off the very cell that was
        /// clicked. lastAnchorRoomId is pinned to 0 before OnGraphMutated fires, and BeginCascade's settlement
        /// branch passes it to BuildingGenerator.SettleDraggedRoom → CompactLayout.NudgeRoomOffOverlaps, which
        /// early-returns for a room id that resolves to no room (floor.GetRoom(0) == null). SnapToLattice in the
        /// same branch is likewise handed that null room and no-ops. So nothing moves the new building after it
        /// is written — the DM's cell is final.
        ///
        /// THE AUTO-LINK, AND WHY IT IS NOT OPTIONAL (final-review fix). DungeonOps.AddRoom creates a room with
        /// NO links. A settlement's streets are routed from lvl.Links (SettlementRoads.Build over
        /// BuildRenderGraph's edge list), so a linkless building gets no street — and, worse, no ROAD CELLS.
        /// SettlementTileGrid.BuildWallRing dilates the occupied seed (buildings ∪ road cells) by
        /// CourtyardCells + 1 = 2 and flood-fills the outside: two clusters whose nearest cells are ≥ 6 apart on
        /// one axis, or ≥ 5 apart on BOTH, stay disconnected, and each grows its OWN wall ring (that pass has no
        /// equivalent of SettlementFence.BridgeStrays — a documented limitation). The placeable region reaches
        /// that far: SettlementVolumeRenderer.TryPlacementCell accepts any on-field cell that is not
        /// Building/Wall/Gate, which on a big walled city includes the diagonal corners of Allocate's 3-cell
        /// margin band. So a linkless placement out there would be a lone house sealed in its own little wall
        /// with no street, and NOTHING would ever repair it — the Clean tier can only fold a road into the seed
        /// if a road exists. Linking the new building to the nearest existing one restores the generator's own
        /// invariant (every building hangs off the street tree, SettlementStreets), and the road that link
        /// produces rasterizes into the seed and merges the two clusters back into one ring.
        ///
        /// The link is the GENERATOR's shape, not the DM's: authored stays false (AddCorridor's default), the
        /// same as every SettlementStreets edge, so DungeonOps.HasAuthoredContent — and therefore the
        /// «Перегенерировать» / «× Этаж» confirm — behaves exactly as it did before. Dummy buildings are
        /// candidates like any other: generated streets connect them too.
        ///
        /// The write itself is already on the lattice: nx/ny are SettlementTileGrid.CenterX/CenterY of the
        /// target cell. The lattice cannot shift under it either — it is SettlementFootprint's ABSOLUTE one
        /// (cell (0,0) spans normalized [0,Pitch)), so adding a building cannot renumber or move any other
        /// building's cell, wherever the new one lands.</summary>
        void PlaceHoveredBuilding()
        {
            var lvl = BoundLevel;
            if (lvl == null || !hoverValid || !hoverPlaceable) return;   // stay armed, change nothing

            // Nearest neighbour resolved BEFORE the add, against the hovered cell centre — which is where the
            // new room is about to be written. Taking it first is what makes "the new room is never its own
            // neighbour" structural rather than a filter that could be dropped later.
            int neighbourId = NearestBuildingId(lvl, hoverNx, hoverNy);
            var room = DungeonOps.AddRoom(lvl, hoverNx, hoverNy);
            // 0 = this is the floor's FIRST building: a one-building town needs no street, and there is
            // nothing to be walled off from.
            if (neighbourId != 0) DungeonOps.AddCorridor(lvl, room.Id, neighbourId);
            lastAnchorRoomId = 0;
            lastPlacedRoomId = room.Id;   // guards the reflex double-click — see the field's own doc
            DisarmPlacement();
            // REFIT (final-review fix). The grid just grew — by the new building's own cell, by its street, and
            // by the 2-cell ring re-derived around both — and FitBoundsFor reads that extent straight off
            // SettlementTileGrid.Allocate, so without this the new house (or its stretch of wall) can land
            // outside the fitted panel. Set BEFORE Refresh deliberately: Refresh resolves a pending fit itself,
            // so the rebuild below already draws at the new scale instead of drawing once at the old one and
            // being rebuilt again by LateUpdate. PLACEMENT ONLY — drag and drag-end must NOT refit (the spec
            // forbids rescaling under the cursor), which is why this lives here and not in OnDrag/OnEndDrag.
            // Rebuild, not FromCache: a building was ADDED, so the cached graph no longer describes the town.
            pendingFit = PendingFit.Rebuild;
            // Same order AddRoomAtCenter uses, and it matters: RebuildView clears the renderer's highlight set,
            // so SelectRoom must come AFTER Refresh or the new building would be created unselected.
            Refresh();
            SelectRoom(room.Id);
            OnGraphMutated?.Invoke();
        }

        /// <summary>The building (TypeId 1) room nearest the normalized point (nx, ny), or 0 when the floor
        /// holds no building at all. Plain squared Euclidean distance with ties broken by the LOWER room id —
        /// the same "pure distance, ties by lower index" rule SettlementStreets' growth pass uses, so the edge
        /// this picks is the one the street generator would have picked. Gates (TypeId 0) are deliberately not
        /// candidates: a gate is not a street tree node a building hangs off, and its own cell is not a
        /// Building cell, so linking to one would not seed the dilation blob the way a building does.</summary>
        static int NearestBuildingId(InteriorFloor lvl, float nx, float ny)
        {
            int best = 0;
            float bestD2 = float.MaxValue;
            foreach (var r in lvl.Rooms)
            {
                if (r.TypeId != 1) continue;
                float dx = r.X - nx, dy = r.Y - ny;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2 || (d2 == bestD2 && r.Id < best)) { bestD2 = d2; best = r.Id; }
            }
            return best;
        }

        /// <summary>Removes the selected room (DungeonOps also strips its corridors and any secrets
        /// anywhere in the dungeon that targeted it), clears selection, rebuilds.</summary>
        public void DeleteSelected()
        {
            if (SelectedRoomId == 0 || dungeon == null) return;
            DungeonOps.RemoveRoom(dungeon, levelIndex, SelectedRoomId);
            SelectRoom(0);   // clears via the same path as a background click — fires OnRoomSelected(0) so
                             // the host drops the deleted id too
            Refresh();
            OnGraphMutated?.Invoke();
        }

        /// <summary>Adds a Normal room at the canvas center, selects it, rebuilds.</summary>
        public Room AddRoomAtCenter()
        {
            var lvl = BoundLevel;
            if (lvl == null) return null;
            // SETTLEMENT (Task 8, handoff b — the THIRD write of that class, beyond the two the handoff named):
            // «+ Здание» is on the settlement toolbar (DungeonEditorScreen.RefreshToolbar's free-edit branch),
            // and DungeonOps.AddRoom writes a TypeId 1 room — a BUILDING, which Allocate's anchor pass counts —
            // at the fixed canvas centre (0.5, 0.5), which is not a cell centre. Captured before the add for
            // the usual reason: the new room could itself be the min-X/min-Y building.
            var lattice = LatticeFor(lvl);
            var room = DungeonOps.AddRoom(lvl, 0.5f, 0.5f);
            SnapToLattice(lattice, room);
            // BUILDING (spec C6 / user 2026-07-19): a + room must become PART of the building, never float in
            // empty space outside the contour — attach it flush to the nearest room, so the footprint grows to
            // wrap it. The placement is FINAL — we do NOT set lastAnchorRoomId, so BeginCascade's anti-overlap
            // nudge does not drag the new room off its chosen spot. Dungeons keep their previous behaviour
            // (Separate cascade handles a new room as before).
            //
            // Only the GROUND floor is reachable here for a building: a building's UPPER floors are
            // GENERATE-ONLY, and RefreshToolbar builds them a stepper + «Перегенерировать» toolbar with NO
            // «+ Комната» button at all (DungeonEditorScreen.RefreshToolbar). The upper-floor branch this used
            // to carry — CompactLayout.PlaceNewRoomInContour, placing the room at the freest spot inside floor
            // 0's contour — was therefore dead, and its comment described a button that no longer exists. That
            // primitive is still in CompactLayout, self-tested, for the day an upper-floor «+» comes back.
            if (dungeon != null && dungeon.Kind == InteriorKind.Building)
                CompactLayout.AttachNewRoom(lvl, room.Id);
            // SETTLEMENTS NO LONGER REACH THIS METHOD (Task 8b). «+ Здание» now ARMS click-to-place
            // (DungeonEditorScreen.RefreshToolbar routes a settlement to TogglePlacement) and the building is
            // created by PlaceHoveredBuilding on the cell the DM clicked, so the fixed (0.5, 0.5) centre that
            // made this path a problem is gone at the root. This is the ONLY call site of AddRoomAtCenter —
            // verified by grep — so nothing else can bring a settlement back here.
            //
            // The `lastAnchorRoomId = room.Id` that commit 31193f1 set here for a settlement was REMOVED with
            // that change. Its sole job was to let BeginCascade's anti-overlap nudge shove the new building off
            // whichever occupied cell (0.5, 0.5) happened to snap onto — a workaround for a placement the DM
            // did not choose. Nothing else read it: the field's only other writer is OnEndDrag, and its only
            // readers are BeginCascade's two branches, neither of which needs an add to have set it (the
            // nudge/leash simply no-op on an id that resolves to no room).
            //
            // SnapToLattice above is kept: it is correct and free if a future caller ever does add a
            // settlement room here, and it is what stops an off-lattice room from re-anchoring the whole grid.
            Refresh();
            SelectRoom(room.Id);
            OnGraphMutated?.Invoke();
            return room;
        }

        // ── Input → area-local point; hit-test and screen→norm now live on the active renderer (Task 6) ──

        /// <summary>Pointer screen position → this renderer's local px. False if the renderer is missing or
        /// its rect has not laid out (never act on a garbage sample). Everything past this point — local →
        /// tile, tile → room id / tile → normalized — is the renderer's own job (HitRoomId,
        /// TryAreaToNorm), so a non-projection renderer (Task 7) can hit-test and map in its own space.</summary>
        bool TryPointerToAreaLocal(PointerEventData data, out Vector2 local)
            => TryScreenToAreaLocal(data.position, out local);

        /// <summary>The screen→area-local step itself, split out of TryPointerToAreaLocal (Task 8b) so the
        /// EVENT path (click/drag, which has a PointerEventData) and the POLLED path (the placement hover,
        /// which reads Mouse.current) share ONE copy of the readiness gates. Two copies would be free to drift,
        /// and hover/click disagreeing about whether a point is usable is precisely the failure this feature
        /// must not have.</summary>
        bool TryScreenToAreaLocal(Vector2 screenPos, out Vector2 local)
        {
            local = default;
            if (renderer == null || renderer.Area == null) return false;
            // An unfitted projection (PxPerTile == 0, before the first ResolveProjection) makes
            // LocalToTile return (0,0) by design. Acting on that would clamp a dragged room to the
            // corner. The rect can be valid a frame before LateUpdate fits — reject that window.
            if (renderer.Projection.PxPerTile <= 0f) return false;
            var area = renderer.Area;
            if (area.rect.width <= 0f || area.rect.height <= 0f) return false;
            // ScreenPointToLocalPointInRectangle returns pivot-relative coords; the projection's local
            // space is CENTRE-relative. area is stretched with a 0.5 pivot, so they already coincide —
            // this is the same assumption DungeonGraphView.PointCenter made.
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPos, null, out local);
        }

        public void OnPointerClick(PointerEventData data)
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            // Release of a drag — not a click. Read data.dragging; do NOT consume draggingRoomId here.
            // Unity's release order is pointerUp → pointerCLICK → endDrag, so this handler runs BEFORE
            // OnEndDrag. Clearing draggingRoomId here would make OnEndDrag's guard swallow the
            // OnGraphMutated that drives the cascade — a silent no-settle-after-drag regression. And
            // because pointerPress == pointerDrag (this component is both handlers on one GameObject),
            // eligibleForClick is never cleared, so this click DOES fire on every in-place release.
            // data.dragging is still true here; the input module clears it only after endDrag.
            if (data.dragging) return;

            // «+ Здание» armed (Task 8b): the click is CONSUMED by placement mode either way — it never
            // selects a room and never clears the selection. Deliberately before the pointer is mapped: the
            // commit uses the STORED hover cell (the one currently painted green/red), not a fresh sample of
            // this event's position. See PlaceHoveredBuilding.
            //
            // LEFT BUTTON ONLY (Task 8b Minor 4 fix): armed placement is a left-click gesture, matching the
            // POI-placement precedent on the world map (PoiInteractionController.Update gates its own press on
            // Mouse.current.leftButton). Scoped to the ARMED branch only — an unarmed right-click still selects
            // exactly as before; this must not touch that path.
            if (PlacementArmed)
            {
                if (data.button != PointerEventData.InputButton.Left) return;
                PlaceHoveredBuilding();
                return;
            }

            if (!TryPointerToAreaLocal(data, out var local)) return;
            int id = renderer.HitRoomId(local, lvl);
            // Background → clear selection. Clears lastPlacedRoomId too (final-review fix): this branch
            // returns before the plain fallthrough at the bottom, so without it the field's doc ("the plain
            // fallthrough clears it") would be true only for clicks that hit a room. Harmless either way —
            // the guard below also requires an id MATCH — but code and comment now say the same thing.
            if (id == 0) { lastPlacedRoomId = 0; SelectRoom(0); return; }

            // Double-click opens the room's battle map — the same shortcut a POI already has on the world
            // map. clickCount == 2 matches Notes' DoubleClickHandler, the project's existing convention.
            // Gated on !LinkMode on purpose: while «Связать» is armed both clicks are link picks, and
            // hijacking the second one would silently swallow half of a linking gesture. The first click
            // of the pair has already selected the room, so the inspector is on the right room either way.
            if (data.clickCount == 2 && !LinkMode)
            {
                SelectRoom(id);
                // Reflex-double-click guard (Task 8b Minor 2 fix): click 1 of this pair was
                // PlaceHoveredBuilding, which disarms the mode but does not consume the click sequence, so
                // this second click reaches here as an ordinary double-click ON THE ROOM JUST PLACED — which
                // would immediately open its interior and navigate away from the town. Swallow exactly that
                // one pairing; lastPlacedRoomId is cleared below on the next plain click, so a later,
                // deliberate double-click on the same room still opens it.
                if (id == lastPlacedRoomId) return;
                OnRoomDoubleClicked?.Invoke(id);
                return;
            }
            lastPlacedRoomId = 0;
            OnRoomActivated(id);
        }

        public void OnBeginDrag(PointerEventData data)
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            // «+ Здание» ARMED = no dragging (final-review fix). OnPointerClick already routes the whole click
            // into placement, so a press-and-drag while armed used to place nothing yet still pick up and MOVE
            // a house — with the green preview quad tracking the cursor over it. Confusing, and the DM will hit
            // it: "press on the cell I want" and "press and slide a little" are the same gesture on a trackpad.
            // Leaving draggingRoomId at 0 is the whole suppression — OnDrag and OnEndDrag both early-return on
            // it, so no house moves and no settle fires. The gesture places NOTHING either, exactly as before
            // this fix: OnPointerClick's `data.dragging` gate swallows any drag-release. The mode stays armed
            // with the preview live, so the DM just clicks again — a drag is deliberately not a placement
            // gesture, and turning it into one would mean reordering that gate ahead of the shipped drag path.
            if (PlacementArmed) return;
            // Building UPPER floors are GENERATE-ONLY (spec stairwell stage B): rooms can't be dragged — the
            // floor is (re)generated around the column. Leaving draggingRoomId at 0 makes OnDrag a no-op.
            if (dungeon != null && dungeon.Kind == InteriorKind.Building && levelIndex > 0) return;
            if (!TryPointerToAreaLocal(data, out var local)) return;
            draggingRoomId = renderer.HitRoomId(local, lvl);
        }

        public void OnDrag(PointerEventData data)
        {
            if (draggingRoomId == 0) return;
            // Second half of the "armed = no dragging" rule above. OnBeginDrag is the LIVE gate (nothing can
            // arm placement mid-gesture today — TogglePlacement is only reachable from the toolbar button);
            // this one keeps the rule true if a drag ever does survive into an armed mode, rather than letting
            // a half-suppressed gesture move a house.
            if (PlacementArmed) return;
            var lvl = BoundLevel;
            var room = lvl?.GetRoom(draggingRoomId);
            if (room == null) return;
            if (!TryPointerToAreaLocal(data, out var local)) return;
            // FALSE means the renderer genuinely cannot place this point (no tile grid drawn yet, degenerate
            // projection) — nx/ny are meaningless then, so bail rather than write a corner position.
            if (!renderer.TryAreaToNorm(local, out float nx, out float ny)) return;

            // SETTLEMENT (Task 8, handoff b): the clamp below is off-lattice by construction — 0.04/0.96 are
            // not cell centres — so re-snap after it. Captured BEFORE the write; see LatticeFor for why the
            // order is load-bearing. For a dungeon/building LatticeFor is null and the clamp stands alone,
            // byte-identical to before.
            var lattice = LatticeFor(lvl);
            room.X = Mathf.Clamp(nx, DragClampMin, DragClampMax);
            room.Y = Mathf.Clamp(ny, DragClampMin, DragClampMax);
            // The snap can push a clamped value back OUT of [0.04, 0.96] by up to half a cell (0.035), so for
            // a settlement the clamp is now approximate: worst case ~0.005, still on the board — and the view
            // itself is fit to the buildings, so it follows them out rather than cropping them.
            SnapToLattice(lattice, room);

            // BUILDING (spec C4): the room moves FREELY with the cursor — NO corridor leash — so the DM can
            // pull it right out of the contour and watch C2' flag it live (RepositionRooms re-tests the
            // contour every sample). On release (BeginCascade) the ONLY correction is anti-overlap on THIS
            // room; it otherwise stays exactly where dropped and no other room ever moves.
            // DUNGEON: stitched-together feel — a corridor may not stretch past MaxCorridorTiles, so dragging
            // this room drags its linked rooms along, and they drag theirs. Runs per drag sample (live),
            // unlike the cascade — the pull must be felt while moving, not on release. The dragged room is the
            // anchor and never yields.
            // Settlements skip the leash too (final-review fix I3, same rationale as BeginCascade's Building
            // branch above): a settlement's streets can be far longer than MaxCorridorTiles, so leashing this
            // LIVE per-drag-sample pull would yank the whole town toward the cursor while dragging, not just
            // on release.
            if (dungeon == null || (dungeon.Kind != InteriorKind.Building && dungeon.Kind != InteriorKind.Settlement))
                DungeonLayout.EnforceCorridorLeash(lvl, draggingRoomId);
            RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Fast);
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (draggingRoomId == 0) return;
            // Clear on EVERY drag path, here and nowhere else. If the release lands outside this
            // hit-plate (over the toolbar, or a badge Button), no click fires at all — leaving the clear
            // to OnPointerClick would strand draggingRoomId set and swallow the next click.
            lastAnchorRoomId = draggingRoomId;
            draggingRoomId = 0;
            OnGraphMutated?.Invoke();
        }

        void OnRoomActivated(int id)
        {
            if (!LinkMode) { SelectRoom(id); return; }

            if (pendingLinkId == 0) { pendingLinkId = id; RefreshHighlights(); return; }

            int a = pendingLinkId;
            pendingLinkId = 0;
            if (a == id) { RefreshHighlights(); return; }   // same room twice — cancel silently, no dialog

            var lvl = BoundLevel;
            if (lvl == null) return;
            // authored: true — this IS the «Связать» action, the only path that hand-links two rooms
            // (see DungeonOps.HasAuthoredContent, which the regen/floor-delete confirm gates on).
            string reason = DungeonOps.AddCorridor(lvl, a, id, authored: true);
            if (reason != null)
            {
                RefreshHighlights();
                ConfirmDialog.ShowInfo(font, "Нельзя связать", reason);
            }
            else
            {
                Refresh();
                OnGraphMutated?.Invoke();
            }
        }

        void SelectRoom(int id)
        {
            SelectedRoomId = id;
            RefreshHighlights();
            OnRoomSelected?.Invoke(id);
        }

        void RefreshHighlights()
        {
            if (renderer == null) return;
            var lvl = BoundLevel;
            if (lvl == null) return;
            foreach (var r in lvl.Rooms)
                renderer.SetHighlight(r.Id, r.Id == SelectedRoomId || (LinkMode && r.Id == pendingLinkId));
        }
    }
}
