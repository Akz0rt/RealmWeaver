using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// The 2.5D VOLUMETRIC settlement renderer (arc C.1) — the second <see cref="IDungeonRenderer"/>, and the
    /// one that REPLACES the flat schematic for a town/city. <see cref="DungeonFlatRenderer"/> keeps dungeons
    /// and building interiors; there is no toggle between them (Task 8 gates on InteriorKind).
    ///
    /// WHAT IT DRAWS. <see cref="SettlementTileGrid"/> classifies the settlement onto the building-cell lattice
    /// as typed tiles (Building/Road/Void/Wall/Gate — the wall ring and its gates are FULL TILES, not a
    /// polyline on edges). This renderer paints one visual per occupied cell, in the exact order
    /// <see cref="SettlementTileGrid.DrawOrder"/> hands back, extruding Building/Wall/Gate into a little box.
    /// A square axis-aligned grid seen through a light camera TILT — deliberately NOT isometric diamonds (an
    /// iso render was built in an earlier arc and rejected) and not a flat plan.
    ///
    /// ── THE PROJECTION, AND WHY THE SIGN IS THE WHOLE BALLGAME ───────────────────────────────────────────
    /// The tilt is expressed through the SHARED <see cref="DungeonProjection"/> with SquashY =
    /// <see cref="tiltSquash"/>, rather than a private formula, for three reasons: (a) it hands the controller
    /// a genuine PxPerTile, which its pointer-readiness gate (DungeonViewController.TryPointerToAreaLocal)
    /// rejects input on when &lt;= 0; (b) ResolveProjection/SetProjection/fit all keep working unchanged; and
    /// (c) it inherits DungeonProjection's SINGLE Y inversion instead of introducing a second one.
    ///
    /// A cell (i,j) maps to area-local pixels as
    ///     Project(i,j) = Projection.TileToLocal(CenterX(i)*T, CenterY(j)*T)
    ///                  = ( CenterX(i)*T*Px + PanX ,  -(CenterY(j)*T*Px*SquashY) + PanY ).
    /// Substituting CenterY(j) = AnchorY + j*Cell and collecting the j-independent part into `oy`:
    ///     local.y = oy - (j - OriginJ) * CH,   CH = Cell*T*Px*SquashY = CW*SquashY,  CW = Cell*T*Px.
    /// Note the MINUS. Larger j therefore sits LOWER on screen (Unity UI local +Y is up). That is exactly what
    /// <see cref="SettlementTileGrid.DepthKey"/> assumes — larger j = SOUTH = NEARER the viewer — so painting
    /// DrawOrder() front-to-last gives back-to-front for free, and the invariant the user stated twice holds:
    /// a front (south) Wall tile is never overlapped by a Building behind it. INVERTING THIS SIGN SILENTLY
    /// PAINTS THE TOWN FRONT-TO-BACK; do not "fix" it to match a naive top-down intuition.
    ///
    /// ── DEPTH IS THE POOL INDEX ─────────────────────────────────────────────────────────────────────────
    /// Tiles are POOLED, never destroyed per frame (a drag frame reclassifies the whole grid, so destroy/create
    /// would churn hundreds of GameObjects per sample). Pool slot k is created by appending to tilesLayer, so
    /// slot index == sibling index == DrawOrder() position, permanently. Assigning order[k] to slot k IS the
    /// painter's order, with no per-frame SetSiblingIndex storm; EnsureSlotDepth only re-seats a slot if the
    /// two ever disagree (hot reload). Sibling index, NOT Canvas.sortingOrder — 101 is already taken by the
    /// dungeon editor's own canvas chrome.
    ///
    /// OVERLAP IS VISUAL ONLY. Height and draw order feed the picture and nothing else: hit-testing inverts
    /// the GROUND-plane projection and ignores every box's height, so which room a click lands on never
    /// depends on how tall its neighbour happens to be.
    ///
    /// ART. Every colour here is a PROCEDURAL PLACEHOLDER and provisional — the user draws the tileset later
    /// and dials these in at the checkpoint (see <see cref="SettlementTileSprites"/>). Fixed colours, not
    /// ThemeService roles: this view is an art-directed scene, the same call DungeonFlatRenderer's ContourColor
    /// / WallColor already make, so it must not repaint on a Dark/Light switch.
    /// </summary>
    public class SettlementVolumeRenderer : MonoBehaviour, IDungeonRenderer
    {
        // ── Tunables (serialized so the DM can dial them live at the Task 9 checkpoint) ──────────────────

        [Header("Camera / geometry")]
        [Tooltip("Vertical squash of the square grid — the 'light camera tilt'. 1 = dead top-down (no volume "
               + "read), 0.5 = strongly oblique. Feeds DungeonProjection.SquashY.")]
        [SerializeField, Range(0.35f, 1f)] float tiltSquash = 0.8f;

        [Tooltip("Multiplies SettlementTileGrid's cell-unit heights into pixels. 1 = a building of height 1.0 "
               + "cell stands exactly one cell-WIDTH tall on screen.")]
        [SerializeField, Range(0.25f, 3f)] float heightScale = 1f;

        [Tooltip("Extruded tiles (Building/Wall/Gate) are drawn this much larger than their lattice pitch so "
               + "neighbours butt up without hairline seams. Flat ground tiles (Road/Void) do NOT use this — "
               + "they inset by gridThicknessPx instead, so the cell-grid line shows through between them. "
               + "The LATTICE step is unaffected either way — this is overdraw only.")]
        [SerializeField, Range(1f, 1.25f)] float tileOverdraw = 1.04f;

        [Tooltip("Width of the darker east strip on a box's front face, as a fraction of the cell width. The "
               + "cheap 'the box turns away from the light' cue; 0 disables it.")]
        [SerializeField, Range(0f, 0.5f)] float eastFaceFraction = 0.22f;

        [Header("Budget")]
        [Tooltip("Draw the courtyard/interior Void tiles. Off = only Building/Road/Wall/Gate, i.e. the town "
               + "floats on the panel background. The first knob to try if the view is slow.")]
        [SerializeField] bool drawVoidTiles = true;

        [Tooltip("Runaway guard on the tile pool. A real town is ~300-500 cells; if this cap is ever reached "
               + "the FRONT-MOST (nearest) tiles are the ones omitted, which is deliberately obvious on screen.")]
        [SerializeField] int maxTiles = 1200;

        [Header("Cell grid")]
        [Tooltip("Thin lines on the cell boundaries, drawn BEHIND every tile and across the whole field so a "
               + "building can be aimed at outside the town. Purely an aid to reading cell edges — keep it "
               + "quiet.")]
        [SerializeField] bool drawCellGrid = true;
        [SerializeField] Color gridColor = new Color(1f, 1f, 1f, 0.06f);
        [SerializeField, Range(1f, 3f)] float gridThicknessPx = 1f;

        [Header("Placeholder palette (provisional — replaced by the tileset)")]
        [SerializeField] Color voidColor      = new Color(0.42f, 0.36f, 0.28f, 1f);   // courtyard earth
        [SerializeField] Color roadColor      = new Color(0.60f, 0.53f, 0.40f, 1f);   // packed sand, lighter
        [SerializeField] Color buildingColor  = new Color(0.66f, 0.38f, 0.24f, 1f);   // terracotta roof
        [SerializeField] Color dummyColor     = new Color(0.40f, 0.37f, 0.33f, 1f);   // decorative filler, receded
        [SerializeField] Color wallColor      = new Color(0.54f, 0.52f, 0.47f, 1f);   // stone
        [SerializeField] Color gateColor      = new Color(0.72f, 0.56f, 0.25f, 1f);   // timber/brass accent
        [SerializeField] Color shadowColor    = new Color(0f, 0f, 0f, 0.35f);

        [Header("Placement preview (Task 8b — «+ Здание» click-to-place)")]
        [Tooltip("Cell under the cursor while «+ Здание» is armed and the cell can take a building.")]
        [SerializeField] Color placeableColor = new Color(0.30f, 0.85f, 0.35f, 0.45f);   // green = free
        [Tooltip("Cell under the cursor while «+ Здание» is armed and the cell is already taken "
               + "(Building / Wall / Gate). A click there does nothing.")]
        [SerializeField] Color blockedColor   = new Color(0.90f, 0.25f, 0.22f, 0.45f);   // red = occupied

        [Tooltip("The south (front) face is the top face's colour times this. < 1 = darker.")]
        [SerializeField, Range(0.2f, 1f)] float frontShade = 0.72f;
        [Tooltip("The east strip is the top face's colour times this. Should be below frontShade.")]
        [SerializeField, Range(0.1f, 1f)] float eastShade = 0.52f;

        [Header("Art")]
        [SerializeField] SettlementTileSprites sprites = new SettlementTileSprites();

        // ── IDungeonRenderer surface ─────────────────────────────────────────────────────────────────────

        public DungeonProjection Projection { get; private set; }

        /// <summary>`as`, not a hard cast (unlike the flat renderer): a bare container GameObject has NO
        /// RectTransform in this project's recorded gotchas, and a null Area is something the controller
        /// already handles (TryPointerToAreaLocal returns false) whereas an InvalidCastException is not.</summary>
        public RectTransform Area => transform as RectTransform;

        public GameObject Host => gameObject;

        /// <summary>Vertical headroom, in TILE-space Y units, that the TALLEST extruded tile occupies ABOVE its
        /// own ground cell. A caller fitting this view (DungeonViewController.FitBoundsFor) must add it at the
        /// NORTH (min-Y) edge of the tile-space bounds it fits, or the back row's roofs are clipped off the top
        /// of the panel — the ground plane is all ResolveProjection's bounds describe, and every box is drawn
        /// UPWARD out of it.
        ///
        /// Derivation, and why it is INDEPENDENT of the scale it is used to compute (no chicken-and-egg):
        ///   h_px = hCells * cw * heightScale,  cw = Cell * T * PxPerTile
        ///   dY(tiles) = h_px / (PxPerTile * SquashY) = hCells * Cell * T * heightScale / SquashY
        /// PxPerTile CANCELS, so this is meaningful before any projection has been resolved — which is exactly
        /// when the fit needs it. WallHeight is the tallest tile the grid can produce (SettlementTileGrid pins
        /// WallHeight &gt; BuildingHeightMax), and it is also what stands on the outermost — i.e. the northmost
        /// drawn — row of a walled town, so it is the right bound for both the walled and the wall-less case
        /// (a village merely over-budgets by the 0.15-cell wall/house difference, ~1.7 tiles).
        ///
        /// Reads the SERIALIZED tunables rather than their defaults on purpose: the DM dials tiltSquash and
        /// heightScale live at the checkpoint, and a fit computed against the defaults would clip the moment
        /// either moves. This is the only reason the fit needs to know this renderer's concrete type.</summary>
        public float ExtrusionHeadroomTiles
        {
            get
            {
                float squash = tiltSquash > 0f ? tiltSquash : 1f;      // same guard ResolveProjection applies
                float scale = heightScale > 0f ? heightScale : 0f;     // 0 = no extrusion drawn = no headroom
                return SettlementTileGrid.WallHeight * SettlementGenerator.BuildingCell
                     * DungeonLayout.TilesPerAxis * scale / squash;
            }
        }

        /// <summary>Has-interior mark (Ц2): building room ids whose own interior already exists on file.
        /// EXTERNAL state, set by DungeonEditorScreen BEFORE the RebuildView chain fires — deliberately NOT
        /// reset inside RebuildView. Declared with the IDENTICAL name/shape as
        /// <see cref="DungeonFlatRenderer.RoomsWithInterior"/> so the screen's two assignment sites — funnelled
        /// through DungeonEditorScreen.SetRoomsWithInterior (Task 8) — work on either renderer without a second
        /// code path.</summary>
        public HashSet<int> RoomsWithInterior { get; set; } = new HashSet<int>();

        // ── State ────────────────────────────────────────────────────────────────────────────────────────

        const string TilesLayerName = "TilesLayer";
        const string GridLayerName = "GridLayer";
        const string PlacementHighlightName = "PlacementHighlight";

        // Task 3: a quiet cell grid drawn BEHIND every tile. Created before tilesLayer (see EnsureBuilt) so
        // it is the earlier sibling and every tile paints over it — the same reasoning tilesLayer itself
        // relies on for the placementHighlight quad, just one layer further back.
        RectTransform gridLayer;
        RectTransform tilesLayer;
        // The «+ Здание» hover preview (Task 8b): ONE quad, deliberately NOT a pool slot and NOT a child of
        // tilesLayer — that layer's whole depth scheme is "pool slot k sits at sibling index k", and a foreign
        // child in it would be re-seated (or hidden) by SlotAt/HideFrom on the next repaint. As the LAST
        // sibling of this renderer's own root it instead draws over every tile, which is what a hover preview
        // must do: the cell it marks may be a Building whose box is drawn a whole cell tall.
        Image placementHighlight;
        bool built;

        readonly List<TileView> tiles = new List<TileView>();
        // Pool of grid-line quads, one flat Image each — reused across rebuilds exactly like `tiles`, never
        // destroyed per frame (RebuildGrid runs on drag frames alongside the tile derive). GridLineView caches
        // its Image the same way TileView caches Shadow/Front/East/Top: GetComponent<Image>() is O(children)
        // and RebuildGrid repositions every pooled line on every drag/cascade frame, so caching turns 36-38
        // GetComponent calls per frame into zero.
        readonly List<GridLineView> gridLines = new List<GridLineView>();
        readonly HashSet<int> highlighted = new HashSet<int>();
        // Cell -> the room that owns it, keyed by DepthKey (unique per cell at this grid's scale). Rebuilt
        // every reposition alongside the grid itself; used for per-building height, marks and dummy shading.
        readonly Dictionary<long, Room> cellRooms = new Dictionary<long, Room>();

        SettlementTileGrid grid;

        // Last drawn level+graph, cached so SetProjection can repaint without the controller handing them
        // back — the same pair, for the same reason, DungeonFlatRenderer keeps.
        InteriorFloor lastLvl;
        RenderGraph lastRg = new RenderGraph();

        // True iff the bound interior is Kind==Settlement. Gates the road source and the building marks, so a
        // non-settlement bind (which should never reach this renderer once Task 8 gates it, but might during
        // a renderer swap) degrades to plain tiles instead of misreading dungeon corridors as roads.
        bool isSettlement;

        void Awake() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            // Hot-reload guard, but stricter than the flat renderer's childCount check: our depth scheme
            // relies on pool slot k sitting at sibling index k, and a domain reload drops the pool list while
            // leaving the GameObjects. Object.Destroy is deferred, so the recovered leftovers are still
            // children right after this clear; SlotAt's SetSiblingIndex(k) then re-seats the freshly rebuilt
            // slots at indices 0..k-1, leaving the (soon-to-be-destroyed) leftovers ON TOP for one frame.
            // Harmless — editor hot-reload only, one frame — but worth getting right.
            // Grid layer FIRST — must be created before tilesLayer so it is the earlier sibling and every
            // tile (a later sibling) draws over it. This ordering is the entire reason it lives here; do not
            // move it after tilesLayer's own setup below.
            var existingGrid = transform.Find(GridLayerName) as RectTransform;
            if (existingGrid != null) { gridLayer = existingGrid; DungeonUiKit.ClearLayer(gridLayer); }
            else gridLayer = MakeLayer(GridLayerName);

            var existing = transform.Find(TilesLayerName) as RectTransform;
            if (existing != null) { tilesLayer = existing; DungeonUiKit.ClearLayer(tilesLayer); }
            else tilesLayer = MakeLayer(TilesLayerName);
            tiles.Clear();
            gridLines.Clear();
            EnsurePlacementHighlight();
            // Field initializers are NOT reliable on a Unity-deserialized component (this repo's recorded
            // gotcha: deserialization silently overrides them). ConfigureTile dereferences this every cell of
            // every frame, so re-establish it here rather than null-check in the hot loop.
            if (sprites == null) sprites = new SettlementTileSprites();
            built = true;
        }

        RectTransform MakeLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            DungeonUiKit.Stretch(rt);
            return rt;
        }

        /// <summary>Create (or, after a hot reload, recover) the single placement-preview quad.
        ///
        /// raycastTarget = false is LOAD-BEARING, for the identical reason MakeFace turns it off on every tile
        /// face: the controller owns input through its own full-area hit-plate, and this quad sits under the
        /// cursor BY DEFINITION whenever it is visible — a raycast target here would swallow the very click
        /// that commits the placement, and the feature would silently never place anything.
        ///
        /// SetAsLastSibling, not a pool slot: see the field's own doc. It is anchored/pivoted at the centre
        /// like a TileView root, so ShowPlacementHighlight can hand it the same Project(i,j) local position.</summary>
        void EnsurePlacementHighlight()
        {
            var found = transform.Find(PlacementHighlightName);
            if (found != null) placementHighlight = found.GetComponent<Image>();
            if (placementHighlight == null)
            {
                var go = new GameObject(PlacementHighlightName, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                placementHighlight = go.AddComponent<Image>();
            }
            placementHighlight.raycastTarget = false;
            placementHighlight.transform.SetAsLastSibling();
            placementHighlight.gameObject.SetActive(false);
        }

        // ── Projection ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>Fit to the caller's TILE-space bounds and this renderer's own rect, exactly like the flat
        /// renderer — only SquashY differs (tiltSquash instead of 1). Returns false on a not-yet-laid-out rect
        /// so the controller retries next LateUpdate (the {0,0} rect gotcha).
        ///
        /// This is what makes Projection.PxPerTile MEANINGFUL for a cell-space renderer: PxPerTile is pixels
        /// per TILE (1/128 of the normalized field), the same quantity the flat renderer fits, and the cell
        /// width follows from it as CW = Cell*TilesPerAxis*PxPerTile. Without a positive PxPerTile the
        /// controller's TryPointerToAreaLocal would discard EVERY click and drag before HitRoomId or
        /// TryAreaToNorm were ever consulted.</summary>
        public bool ResolveProjection(float minX, float minY, float maxX, float maxY)
        {
            var area = Area;
            if (area == null) return false;
            var rect = area.rect;
            if (rect.width <= 0f || rect.height <= 0f) return false;
            float squash = tiltSquash > 0f ? tiltSquash : 1f;   // FitBounds also guards, belt and braces
            Projection = DungeonProjection.FitBounds(minX, minY, maxX, maxY, rect.width, rect.height, squash);
            return true;
        }

        /// <summary>Replace the projection wholesale and repaint the last-drawn layout (pan/zoom seam).
        ///
        /// includeRoadsInFence: true for PARITY with DungeonFlatRenderer.SetProjection — a pan/zoom is a
        /// repaint of a STABLE layout, i.e. settle-equivalent, not a drag frame. LIMITATION worth knowing:
        /// this renderer reads the roads out of the cached render graph rather than re-routing them (see
        /// RoadsFromGraph), so panning immediately after a Fast drag frame — whose graph carries no roads —
        /// repaints without Road cells until the next Clean rebuild. Currently moot: SetProjection has NO
        /// caller anywhere in the project (pan/zoom was never wired), and this exists to satisfy the
        /// interface.</summary>
        public void SetProjection(DungeonProjection p)
        {
            Projection = p;
            RepositionRooms(lastLvl, lastRg, includeRoadsInFence: true);
        }

        // ── Build / reposition ───────────────────────────────────────────────────────────────────────────

        /// <summary>Structural rebuild. Unlike the flat renderer this does NOT tear down and recreate the
        /// visuals: a tile view is generic (it is reconfigured for whatever cell lands in its slot), so the
        /// pool survives a rebind and only the CONTENT is re-derived. `font` and `onJumpToLevel` are unused —
        /// the volumetric view carries no per-room label or floor-jump badge (a settlement has exactly one
        /// floor, and 40-80 labelled cards is what the schematic did badly); they stay in the signature
        /// because the interface owns it.
        ///
        /// RebuildView is always the settle-equivalent CLEAN path (DungeonViewController.Refresh routes with
        /// SettlementRoadsFor(Clean)), so roads are included — same reasoning as the flat renderer's own
        /// hard-coded true.</summary>
        public void RebuildView(InteriorData dungeon, int levelIndex, InteriorFloor lvl, RenderGraph rg, Font font,
                                System.Action<int> onJumpToLevel)
        {
            EnsureBuilt();
            isSettlement = dungeon != null && dungeon.Kind == InteriorKind.Settlement;
            // A stale selection id from the PREVIOUS binding must not light up a coincidentally-matching room
            // here. Safe to clear: the controller calls RefreshHighlights immediately after RebuildView.
            highlighted.Clear();
            // Same rule for the «+ Здание» hover preview (Task 8b): a rebind can arrive while this renderer is
            // being swapped back IN with a stale quad still parked over some cell of the PREVIOUS town. The
            // controller re-shows it on the very next frame if the mode is still armed, so clearing here costs
            // nothing and closes the one window where a leftover could be seen.
            HidePlacementHighlight();
            RepositionRooms(lvl, rg, includeRoadsInFence: true);
        }

        /// <summary>Cheap per-frame repaint from the CURRENT room positions — every drag sample and cascade
        /// frame. "Cheap" here means no allocation of GameObjects and no destroy: the tile grid itself IS
        /// re-derived, because moving a building changes which cells are Building, which cells the wall ring
        /// runs through, and where the gates land. That derive is a few hundred cells of dilate + flood-fill,
        /// far below the cost of the road A* it is deliberately kept clear of.
        ///
        /// TWO-TIER, honouring <paramref name="includeRoadsInFence"/> exactly as SettlementTileGrid.Build
        /// documents it: false (Fast/drag) passes roads == null, so Build takes its buildings-only path — no
        /// Road cells, and the wall ring wraps the live building/gate rects alone, skipping the ~12.5 ms grid
        /// A*. true (bind/settle) passes the routed roads, so they rasterize as Road cells AND fold into the
        /// occupied blob before the ring pass, exactly like the shipped fence. Roads come from `rg`, which the
        /// controller built with the SAME SettlementRoadsFor(mode) flag it passes here, so the two can never
        /// disagree — and it costs no second routing pass.</summary>
        public void RepositionRooms(InteriorFloor lvl, RenderGraph rg, bool includeRoadsInFence)
        {
            EnsureBuilt();
            lastLvl = lvl;
            lastRg = rg ?? new RenderGraph();
            if (lvl == null)
            {
                grid = null;
                cellRooms.Clear();
                HideFrom(0);
                HideGridLines();   // grid == null: nothing to derive cw/ch from, so hide the pool directly
                return;
            }
            // Pre-fit frame (Projection not yet resolved): drawing now would create/activate hundreds of
            // zero-size tiles at the origin for nothing. LateUpdate re-fits and calls Refresh() the moment
            // ResolveProjection succeeds, so this only skips one cosmetic frame, never a permanent blank view.
            if (Projection.PxPerTile <= 0f)
            {
                grid = null;
                cellRooms.Clear();
                HideFrom(0);
                HideGridLines();   // same reasoning, via PxPerTile this time — no cw/ch to compute yet
                return;
            }

            var roads = (isSettlement && includeRoadsInFence) ? RoadsFromGraph(lastRg) : null;
            grid = SettlementTileGrid.Build(lvl, roads);
            RebuildCellRooms(lvl);

            float cw = CellWidthPx;
            float ch = cw * Projection.SquashY;

            RebuildGrid(cw, ch);

            var order = grid.DrawOrder();
            int used = 0;
            for (int k = 0; k < order.Count; k++)
            {
                if (used >= maxTiles) break;
                var cell = order[k];
                var type = grid.At(cell.i, cell.j);
                if (type == TileType.None) continue;                  // DrawOrder excludes these already
                if (type == TileType.Void && !drawVoidTiles) continue;
                var view = SlotAt(used);
                ConfigureTile(view, cell.i, cell.j, type, cw, ch);
                view.Root.gameObject.SetActive(true);
                used++;
            }
            HideFrom(used);
        }

        /// <summary>Roads for the tile grid, taken from the render graph the controller already routed instead
        /// of routing a second time. Two conversions matter and both are silent if wrong:
        ///   - FRAME: RenderGraph segments are NORMALIZED (DungeonLayout.ToLayout runs ToNorm on them), while
        ///     SettlementTileGrid rasterizes in TILE space (it divides by TilesPerAxis itself). Hence the *T.
        ///     Dropping it collapses every road onto a single cell; adding it twice throws them 128x away.
        ///   - TYPE: RenderSegment -> LinkSegment. EdgeIndex is left 0; SettlementTileGrid never reads it.
        /// Junction-splitting in the render graph is harmless here — a segment cut into collinear pieces
        /// rasterizes to the same cell set as the whole.</summary>
        static List<LinkSegment> RoadsFromGraph(RenderGraph rg)
        {
            const float T = DungeonLayout.TilesPerAxis;
            var roads = new List<LinkSegment>(rg.Segments.Count);
            foreach (var s in rg.Segments)
                roads.Add(new LinkSegment
                {
                    A = new LinkPoint { X = s.A.X * T, Y = s.A.Y * T },
                    B = new LinkPoint { X = s.B.X * T, Y = s.B.Y * T },
                });
            return roads;
        }

        /// <summary>Map each room onto its lattice cell so a Building tile can find its own room (height, dummy
        /// shading, the two corner marks). Uses the grid's OWN CellI/CellJ — the identical call
        /// SettlementTileGrid.Build used to place the cell — so the lookup is total for every Building cell.
        /// Two rooms CAN snap to one cell (the lattice is coarse: one cell is ~9 fine tiles); the tie goes to
        /// the visually front-most, i.e. larger Y, then larger Id, matching HitRoomId's own tie-break so the
        /// tile you see and the room you click are the same one.</summary>
        void RebuildCellRooms(InteriorFloor lvl)
        {
            cellRooms.Clear();
            if (grid == null || lvl == null) return;
            foreach (var r in lvl.Rooms)
            {
                long key = SettlementTileGrid.DepthKey(grid.CellI(r.X), grid.CellJ(r.Y));
                if (cellRooms.TryGetValue(key, out var prev) && prev != null && !Precedes(prev, r)) continue;
                cellRooms[key] = r;
            }
        }

        /// <summary>True if `candidate` should displace `held` as the room shown for a shared cell.</summary>
        static bool Precedes(Room held, Room candidate)
            => candidate.Y > held.Y || (candidate.Y == held.Y && candidate.Id > held.Id);

        Room RoomAtCell(int i, int j)
            => cellRooms.TryGetValue(SettlementTileGrid.DepthKey(i, j), out var r) ? r : null;

        // ── Geometry ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>One lattice cell's width in pixels. Cell is a NORMALIZED pitch (SettlementGenerator.
        /// BuildingCell), so it converts to tiles (*TilesPerAxis) before it converts to pixels
        /// (*PxPerTile).</summary>
        float CellWidthPx => grid == null ? 0f : grid.Cell * DungeonLayout.TilesPerAxis * Projection.PxPerTile;

        /// <summary>Cell (i,j) -> area-local pixels of its GROUND-plane centre. See the class doc for the
        /// expansion to `oy - (j - OriginJ)*CH`: larger j is LOWER on screen, hence nearer, hence DrawOrder()
        /// paints back-to-front.</summary>
        Vector2 Project(int i, int j)
        {
            var (lx, ly) = Projection.TileToLocal(grid.CenterX(i) * DungeonLayout.TilesPerAxis,
                                                  grid.CenterY(j) * DungeonLayout.TilesPerAxis);
            return new Vector2(lx, ly);
        }

        /// <summary>Inverse of <see cref="Project"/>: area-local pixels -> the cell whose ground-plane
        /// footprint contains the point. Height is deliberately NOT considered (overlap is visual only).
        /// False on the two states where the answer would be a lie rather than a cell: no grid drawn yet, or a
        /// degenerate projection — DungeonProjection.LocalToTile answers (0,0) for ANY input when PxPerTile or
        /// SquashY is &lt;= 0, which is precisely the "dragged room snaps to the corner" bug.</summary>
        bool TryAreaToCell(Vector2 areaLocalPoint, out int i, out int j)
        {
            i = 0; j = 0;
            if (grid == null) return false;
            if (Projection.PxPerTile <= 0f || Projection.SquashY <= 0f) return false;
            var (tx, ty) = Projection.LocalToTile(areaLocalPoint.x, areaLocalPoint.y);
            i = grid.CellI(tx / DungeonLayout.TilesPerAxis);
            j = grid.CellJ(ty / DungeonLayout.TilesPerAxis);
            return true;
        }

        // ── Hit-test and screen -> norm (renderer-owned since Task 6) ────────────────────────────────────

        /// <summary>The room standing on the clicked CELL, or 0 for a miss.
        ///
        /// Returns 0, not -1, on a miss: the interface contract says 0 = background, and
        /// DungeonViewController.OnPointerClick keys "clear the selection" on exactly `id == 0`. (The task
        /// brief said -1; that would silently break background-click-clears-selection.)
        ///
        /// EVERY room is a candidate, buildings (TypeId 1) and GATES (TypeId 0) alike — a gate is a draggable
        /// room like any other, and filtering to buildings would make gates unselectable. Ties (two rooms
        /// snapped to one coarse cell) resolve front-most-first, the same rule RebuildCellRooms uses to pick
        /// which room the tile DRAWS as, so what you see and what you select agree.
        ///
        /// NOTE the known seam, left for the checkpoint rather than papered over with a heuristic: a Gate TILE
        /// is the wall-ring cell NEAREST the gate room (SettlementTileGrid.MarkGates), which is not necessarily
        /// the gate room's OWN cell. Clicking the gate room's cell always selects it; clicking a drawn Gate
        /// tile that sits on a different cell will not.
        ///
        /// <paramref name="lvl"/> must be non-null — the controller only ever calls this with its bound
        /// level — but a null is handled as a miss rather than a throw.</summary>
        public int HitRoomId(Vector2 areaLocalPoint, InteriorFloor lvl)
        {
            if (lvl == null) return 0;
            if (!TryAreaToCell(areaLocalPoint, out int i, out int j)) return 0;
            int best = 0;
            float bestY = float.MinValue;
            foreach (var r in lvl.Rooms)
            {
                if (grid.CellI(r.X) != i || grid.CellJ(r.Y) != j) continue;
                if (r.Y > bestY || (r.Y == bestY && r.Id > best)) { bestY = r.Y; best = r.Id; }
            }
            return best;
        }

        /// <summary>Area-local point -> normalized room position, SNAPPED to the cell centre so a dragged
        /// building always lands ON the lattice this view draws (1 object = 1 cell, permanently).
        ///
        /// Returns FALSE — meaning "I genuinely cannot place this point", and the caller must not touch nx/ny
        /// — when there is no grid drawn yet or the projection is degenerate. That is a real signal, unlike
        /// DungeonFlatRenderer's unconditional `true`, which answered (0,0) on an unresolved projection and
        /// snapped the dragged room to the corner.
        ///
        /// The snap is STABLE under its own feedback: the lattice anchor is the minimum building X/Y, so if
        /// the dragged building becomes the new minimum it is by construction already an integer number of
        /// cells from the old anchor and the lattice does not shift under the cursor.</summary>
        public bool TryAreaToNorm(Vector2 areaLocalPoint, out float nx, out float ny)
        {
            nx = 0f; ny = 0f;
            if (!TryAreaToCell(areaLocalPoint, out int i, out int j)) return false;
            nx = grid.CenterX(i);
            ny = grid.CenterY(j);
            return true;
        }

        // ── Placement preview (Task 8b) ──────────────────────────────────────────────────────────────────

        /// <summary>Can a new building stand on a cell of this type? The rule, pinned by the task brief:
        /// everything EXCEPT Building, Wall and Gate is placeable. Void (courtyard) and Road are placeable, and
        /// so — the interesting case — is <see cref="TileType.None"/>, a cell outside the current built-up
        /// area: placing out there is how the town grows.
        ///
        /// WHAT ACTUALLY MAKES A None CELL SAFE (corrected at final review — this comment used to claim "the
        /// wall ring and the streets simply re-derive around the new building on the next rebuild", which is
        /// only half of it and the wrong half). Re-deriving is not enough on its own:
        /// SettlementTileGrid.BuildWallRing dilates the occupied seed by CourtyardCells + 1 = 2 cells and
        /// flood-fills the outside, so two clusters whose nearest cells are >= 6 apart on one axis, or >= 5 on
        /// BOTH, never 4-connect — the fill runs between them and EACH grows its own independent wall ring
        /// (that pass has no SettlementFence.BridgeStrays equivalent; see this grid's class doc). On a large
        /// walled city the diagonal corners of Allocate's 3-cell margin band are already that far from the
        /// nearest building, and they pass every test here.
        ///
        /// The thing that closes it is on the COMMIT side, not here: DungeonViewController.PlaceHoveredBuilding
        /// AUTO-LINKS the new room to the nearest existing building. SettlementRoads then routes a street along
        /// that link, the street rasterizes into cells, those cells join BuildWallRing's dilation seed, and the
        /// two clusters merge into ONE ring — with a street to the new house as the same side effect. Without
        /// the link there is no road (DungeonOps.AddRoom creates a room with no links at all), so nothing would
        /// ever bridge the gap and the lone house would stay sealed in its own wall, permanently. Placement
        /// therefore stays permissive HERE and is made whole THERE; do not "simplify" either half alone.
        ///
        /// Static and type-only on purpose: the caller (DungeonViewController) must never make its own
        /// judgement about a cell, so the green/red the DM sees and the accept/reject the click applies are
        /// literally the same expression evaluated twice.</summary>
        public static bool IsPlaceable(TileType type)
            => type != TileType.Building && type != TileType.Wall && type != TileType.Gate;

        /// <summary>Is a normalized position inside the 0..1 field a room may legally occupy? A SECOND axis of
        /// the placement test, alongside the tile-type rule above, and it is not decoration:
        ///
        /// DungeonViewController.Update's cascade re-reads each room's CURRENT X/Y every animation frame and
        /// writes back Mathf.Clamp01 of the smoothed value. A room stored outside 0..1 is therefore pinned to
        /// the field edge for the WHOLE of every later cascade (any drag of any building starts one) and only
        /// snaps back when the animation's final exact-target write lands — a visible teleport of the building
        /// away from the cell the DM chose, which is precisely the surprise click-to-place exists to remove.
        /// (Worse readings exist: with `cur` re-read from the clamped value the remaining-distance test can
        /// fail to converge, leaving the cascade running.)
        ///
        /// This IS reachable, and only through this new path. Allocate pads the grid by MarginCells = 3 cells
        /// (0.21 normalized) beyond the outermost building, and SettlementGenerator places a large town's
        /// buildings out to radius 0.45 from centre 0.5 — so a 40-60 building city genuinely draws, and lets
        /// the DM click, cells at a NEGATIVE normalized X. Nothing else can write such a room: generation stays
        /// inside the placement contour, drags clamp to 0.04..0.96 and then snap out by at most half a cell,
        /// and AddRoomAtCenter writes the canvas centre.
        ///
        /// Bound is 0..1 EXACTLY, not the drag clamp's 0.04..0.96 — Clamp01 is the invariant being protected,
        /// and borrowing a drag-feel constant would hide why the limit is where it is.</summary>
        static bool OnField(float nx, float ny) => nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f;

        /// <summary>Area-local point → the cell under it: its CENTRE as a normalized room position (exactly
        /// what <see cref="TryAreaToNorm"/> answers — same call, same snap) plus whether a building may be
        /// placed there.
        ///
        /// The two placement axes are combined HERE, in one expression, on purpose: the controller paints the
        /// highlight from this answer and its click accepts or rejects from this answer, so the green/red the
        /// DM sees and the verdict the click applies can never be two different judgements.
        ///
        /// A THIRD axis, added alongside the tile-type rule and the on-field guard: the cell must have no room
        /// ALREADY mapped to it (<see cref="AnyRoomAtCell"/>). The tile-type rule alone is not enough — a gate
        /// room (TypeId 0) writes no tile of its own (only TypeId==1 rooms write Building, see
        /// SettlementTileGrid.Build), and its wall-ring Gate tile is the NEAREST ring cell (MarkGates), not
        /// necessarily its own cell — so a gate's own cell commonly reads Void or Road and would pass the
        /// tile-type test clean, letting a second room land on top of it (HitRoomId's tie-break then
        /// permanently hides one of the two from clicks — the exact bug click-to-place exists to remove).
        ///
        /// Returns FALSE with both out-params meaningless in exactly the states TryAreaToCell refuses (no tile
        /// grid built yet, or a degenerate projection). The caller must then show NO highlight and place
        /// nothing — an invented cell would be a lie the DM could act on.</summary>
        public bool TryPlacementCell(Vector2 areaLocalPoint, InteriorFloor lvl, out float nx, out float ny, out bool placeable)
        {
            nx = 0f; ny = 0f; placeable = false;
            if (!TryAreaToCell(areaLocalPoint, out int i, out int j)) return false;
            nx = grid.CenterX(i);
            ny = grid.CenterY(j);
            placeable = IsPlaceable(grid.At(i, j)) && OnField(nx, ny) && !AnyRoomAtCell(lvl, i, j);
            return true;
        }

        /// <summary>True if some room — of ANY type, a gate included — already maps to cell (i,j), via the
        /// identical mapping <see cref="HitRoomId"/> uses (grid.CellI/CellJ). On a settlement floor only
        /// TypeId 0 (gate) and TypeId 1 (building, active or dummy) rooms exist (SettlementGenerator,
        /// DungeonOps.AddRoom); every TypeId 1 room already fails the tile-type rule above (it always owns a
        /// Building tile, which Allocate's bbox guarantees is InBounds and Build's write order never lets a
        /// later pass overwrite). So this term is reachable ONLY for a gate room's own cell — it adds no new
        /// restriction beyond that.</summary>
        bool AnyRoomAtCell(InteriorFloor lvl, int i, int j)
        {
            if (lvl == null) return false;
            foreach (var r in lvl.Rooms)
                if (grid.CellI(r.X) == i && grid.CellJ(r.Y) == j) return true;
            return false;
        }

        /// <summary>Show the hover preview over the cell CONTAINING the normalized point (nx, ny) — in
        /// practice the cell centre <see cref="TryPlacementCell"/> just returned, so the round trip
        /// CenterX(i) → CellI is exact. Green when <paramref name="placeable"/>, red when not.
        ///
        /// Sized and positioned from the SAME cw/ch/Project(i,j) the ground face of a flat tile uses, so the
        /// quad covers precisely the cell footprint the DM is judging — not an approximation of it. Recomputed
        /// on every call rather than cached: the caller drives this once per frame while armed, and the
        /// projection or the grid extent may have changed since the last one.</summary>
        public void ShowPlacementHighlight(float nx, float ny, bool placeable)
        {
            // GUARD, not EnsureBuilt() (Task 8b Minor 5 fix): this runs every hover frame while armed, and
            // rebuild/recovery belongs on the structural path — RepositionRooms and RebuildView both call
            // EnsureBuilt() themselves before this method's only caller can ever see a usable grid. A
            // not-yet-built frame here just skips one highlight; the next structural call repairs it.
            if (!built) return;
            if (placementHighlight == null) return;
            if (grid == null || Projection.PxPerTile <= 0f) { HidePlacementHighlight(); return; }

            int i = grid.CellI(nx), j = grid.CellJ(ny);
            float cw = CellWidthPx;
            float ch = cw * Projection.SquashY;
            var rt = (RectTransform)placementHighlight.transform;
            rt.sizeDelta = new Vector2(cw * tileOverdraw, ch * tileOverdraw);
            rt.anchoredPosition = Project(i, j);
            placementHighlight.color = placeable ? placeableColor : blockedColor;
            if (!placementHighlight.gameObject.activeSelf) placementHighlight.gameObject.SetActive(true);
        }

        /// <summary>Hide the hover preview. Called by the controller whenever the mode is cancelled or the
        /// pointer leaves the map area, and by RebuildView so a highlight can never survive a rebind onto an
        /// unrelated town (this renderer's host is merely DEACTIVATED on a renderer swap, which hides but does
        /// not clear it).</summary>
        public void HidePlacementHighlight()
        {
            if (placementHighlight != null && placementHighlight.gameObject.activeSelf)
                placementHighlight.gameObject.SetActive(false);
        }

        // ── Highlight ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Selection/link highlight. Held as a SET of room ids rather than a single id because
        /// DungeonViewController.RefreshHighlights sweeps every room turning highlights on and off in list
        /// order, and link mode legitimately highlights two rooms at once. roomId 0 clears everything, per the
        /// interface contract. Re-applying is O(pool) but only runs on an actual change, so the sweep is ~80
        /// no-ops plus one repaint.</summary>
        public void SetHighlight(int roomId, bool on)
        {
            if (roomId == 0)
            {
                if (highlighted.Count == 0) return;
                highlighted.Clear();
                ApplyHighlights();
                return;
            }
            bool changed = on ? highlighted.Add(roomId) : highlighted.Remove(roomId);
            if (changed) ApplyHighlights();
        }

        void ApplyHighlights()
        {
            for (int k = 0; k < tiles.Count; k++)
            {
                var v = tiles[k];
                if (v == null || v.Highlight == null) continue;
                v.Highlight.enabled = v.RoomId != 0 && highlighted.Contains(v.RoomId);
            }
        }

        // ── Tile pool ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>One pooled cell visual: a root positioned at the cell's ground centre, with the box's
        /// faces as children in their own painter order (shadow behind, then the front face, the darker east
        /// strip over it, then the raised top face and its corner marks). Every part exists from creation and
        /// is toggled per frame — pool slots get recycled to arbitrary cell types, so lazily creating parts
        /// would converge to the same object count while adding a branch per part per frame.</summary>
        sealed class TileView
        {
            public RectTransform Root;
            public RectTransform ShadowRt, FrontRt, EastRt, TopRt;
            public Image Shadow, Front, East, Top;
            public GameObject PreviewMark, InteriorMark;
            public Outline Highlight;
            public int RoomId;   // 0 when this cell carries no room (Void/Road, or an orphaned Wall cell)
        }

        /// <summary>Pool slot k, grown on demand. Slots are only ever APPENDED to tilesLayer, so slot index
        /// and sibling index coincide by construction — which is what makes "assign order[k] to slot k" the
        /// painter's order with no reordering work. EnsureSlotDepth is the self-heal for the one case that can
        /// break it (a domain reload that leaves foreign children in the layer).</summary>
        TileView SlotAt(int k)
        {
            while (tiles.Count <= k) tiles.Add(MakeTile());
            var v = tiles[k];
            if (v.Root.GetSiblingIndex() != k) v.Root.SetSiblingIndex(k);
            return v;
        }

        void HideFrom(int first)
        {
            for (int k = first; k < tiles.Count; k++)
                if (tiles[k] != null && tiles[k].Root != null) tiles[k].Root.gameObject.SetActive(false);
        }

        TileView MakeTile()
        {
            var root = new GameObject("Tile", typeof(RectTransform));
            root.transform.SetParent(tilesLayer, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;   // a pure positioning node; the faces carry the size

            var v = new TileView { Root = rt };
            v.Shadow = MakeFace(rt, "Shadow", out v.ShadowRt);
            v.Front  = MakeFace(rt, "Front",  out v.FrontRt);
            v.East   = MakeFace(rt, "East",   out v.EastRt);
            v.Top    = MakeFace(rt, "Top",    out v.TopRt);

            // Selection outline on the TOP face — the part that reads as "the object" from above.
            v.Highlight = v.Top.gameObject.AddComponent<Outline>();
            v.Highlight.effectColor = ThemeService.Get(ThemeRole.Accent);
            v.Highlight.effectDistance = new Vector2(2f, -2f);
            v.Highlight.enabled = false;

            // The schematic's two corner marks, re-expressed on the roof instead of on a card: has-image
            // top-RIGHT (DungeonFlatRenderer.BuildPreviewIndicator), has-interior top-LEFT (BuildInteriorMark).
            // Opposite corners so they can never be confused when both show.
            // Roles carried over verbatim from the schematic: Dot is its "neutral/free" role, Accent its
            // "this is notable" one, and the has-interior mark is the notable one of the two.
            v.PreviewMark  = MakeMark(v.TopRt, "PreviewIndicator", new Vector2(1f, 1f), new Vector2(-1f, -1f), ThemeRole.Dot);
            v.InteriorMark = MakeMark(v.TopRt, "InteriorMark",     new Vector2(0f, 1f), new Vector2(1f, -1f), ThemeRole.Accent);

            root.SetActive(false);
            return v;
        }

        static Image MakeFace(RectTransform parent, string name, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            // The CONTROLLER owns input through its own full-area hit-plate and asks this renderer to
            // hit-test; a tile that ate raycasts would steal clicks from it.
            img.raycastTarget = false;
            return img;
        }

        static GameObject MakeMark(RectTransform parent, string name, Vector2 corner, Vector2 inset, ThemeRole role)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = inset;
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, role);
            img.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        // ── Cell grid (Task 3) ───────────────────────────────────────────────────────────────────────────

        /// <summary>One pooled grid-line quad. Mirrors <see cref="TileView"/>'s own reasoning for caching its
        /// faces: RebuildGrid repositions every pooled line on every drag/cascade frame, so the Image is
        /// resolved once at creation instead of via GetComponent&lt;Image&gt;() 36-38 times a frame.</summary>
        sealed class GridLineView
        {
            public RectTransform Rt;
            public Image Img;
        }

        /// <summary>Lay the cell grid over the whole normalized field. Lines sit on cell BOUNDARIES (half a
        /// cell off the centres derived from Project), so they frame cells instead of bisecting them. The
        /// span deliberately covers 0..1 rather than the allocated grid: a building may be placed outside the
        /// town and the DM needs cells to aim at out there.
        ///
        /// Only one corner per axis is ever projected (Project is affine and axis-aligned, per the class doc's
        /// TileToLocal expansion), not every line endpoint. Larger j draws LOWER on screen (the single Y
        /// inversion lives in DungeonProjection.TileToLocal) — a horizontal line for row-boundary j therefore
        /// sits at Project(i0, j).y + ch/2 (half a cell NORTH, i.e. toward smaller j, of that row's centre),
        /// not -ch/2; getting this backwards offsets the whole grid by one row against the tiles it is meant
        /// to frame.
        ///
        /// LOOP BOUNDS run one index PAST i1/j1 (`&lt;= i1 + 2`, not `&lt;= i1 + 1`): i1 = CellI(1)+1 already
        /// carries one cell of margin beyond the raw 0..1 span, and a line at boundary index i only frames
        /// cells up to i-1 on its own (a cell's east edge is the NEXT boundary index). Stopping at i1+1 frames
        /// cells i0..i1 but leaves a ground (Void/Road) cell at i1+1 with only its near edge drawn — a real
        /// possibility since SettlementTileGrid's own allocated bounds are computed independently of this
        /// field-relative span (see the near-symmetric case OnField's own doc calls out: MarginCells beyond
        /// the outermost building can push cells past the field edge on the i0/j0 side too, which this
        /// asymmetric +1 does NOT extend for — worst case there remains the same sub-pixel panel-background
        /// hairline this fixes on the i1/j1 side, left as-is since only the i1/j1 case was in scope here).
        /// The extra iteration costs one more pooled quad per axis.</summary>
        void RebuildGrid(float cw, float ch)
        {
            int used = 0;
            if (drawCellGrid && grid != null && Projection.PxPerTile > 0f)
            {
                int i0 = grid.CellI(0f) - 1, i1 = grid.CellI(1f) + 1;
                int j0 = grid.CellJ(0f) - 1, j1 = grid.CellJ(1f) + 1;

                // Project is affine and axis-aligned, so one corner per axis fixes the whole span.
                Vector2 lo = Project(i0, j1), hi = Project(i1, j0);   // larger j draws LOWER on screen
                float left = lo.x - cw * 0.5f, right = hi.x + cw * 0.5f;
                float bottom = lo.y - ch * 0.5f, top = hi.y + ch * 0.5f;
                float height = top - bottom, width = right - left;

                for (int i = i0; i <= i1 + 2; i++)
                {
                    var line = GridLineAt(used++);
                    line.Rt.sizeDelta = new Vector2(gridThicknessPx, height);
                    line.Rt.anchoredPosition = new Vector2(Project(i, j0).x - cw * 0.5f, bottom + height * 0.5f);
                }
                for (int j = j0; j <= j1 + 2; j++)
                {
                    var line = GridLineAt(used++);
                    line.Rt.sizeDelta = new Vector2(width, gridThicknessPx);
                    line.Rt.anchoredPosition = new Vector2(left + width * 0.5f, Project(i0, j).y + ch * 0.5f);
                }
            }
            HideGridLinesFrom(used);
        }

        /// <summary>Hide every pooled grid line, without touching the pool itself. The two early-return paths
        /// in RepositionRooms (no level bound, or projection not yet resolved) have no cw/ch to hand
        /// RebuildGrid, and used to call it with inert `(0f, 0f)` arguments relying on the `grid == null` /
        /// `PxPerTile &lt;= 0f` guard to make them dead — this says directly what those calls meant.</summary>
        void HideGridLines() => HideGridLinesFrom(0);

        /// <summary>Shared tail with RebuildGrid's own guarded-off case: hide every pooled line from index
        /// `first` onward, the identical "hide, never destroy, surplus past what's needed" idiom
        /// <see cref="HideFrom"/> applies to the tile pool.</summary>
        void HideGridLinesFrom(int first)
        {
            for (int k = first; k < gridLines.Count; k++)
                if (gridLines[k] != null && gridLines[k].Rt != null) gridLines[k].Rt.gameObject.SetActive(false);
        }

        /// <summary>Pool slot k, grown on demand — mirrors <see cref="SlotAt"/>'s idiom exactly: append-only,
        /// so the surplus past `used` is hidden (never destroyed) by <see cref="HideGridLinesFrom"/>, the same
        /// way HideFrom hides the tile pool's surplus. Uses <see cref="MakeFace"/>, the identical Image-creation
        /// helper MakeTile uses for every one of a tile's four faces (centre anchor/pivot, raycastTarget
        /// already off) — a grid line is nothing more than a fifth kind of flat quad, parented to gridLayer
        /// instead of a tile's own root.</summary>
        GridLineView GridLineAt(int k)
        {
            while (gridLines.Count <= k)
            {
                var img = MakeFace(gridLayer, "GridLine", out RectTransform rt);
                gridLines.Add(new GridLineView { Rt = rt, Img = img });
            }
            var line = gridLines[k];
            line.Rt.gameObject.SetActive(true);
            line.Img.color = gridColor;
            return line;
        }

        // ── Per-cell configuration ───────────────────────────────────────────────────────────────────────

        /// <summary>Point one pooled slot at cell (i,j).
        ///
        /// SCREEN LAYOUT of an extruded tile of pixel height H, relative to the cell's ground centre at (0,0):
        ///   front (south) face  x in [-cw/2, +cw/2], y in [-ch/2, -ch/2 + H]
        ///   east strip          the right slice of that same face, drawn over it
        ///   top face            a cw x ch quad centred at (0, H)
        /// The front + top pair COVERS the ground footprint exactly, which is why an extruded tile draws no
        /// separate ground quad; a flat tile (Road/Void) is just the same top face with H = 0.
        ///
        /// The shadow is offset DOWN-RIGHT — toward the viewer and away from a north-west light. That matters
        /// for depth, not just taste: it spills onto cell (i+1, j+1), which is nearer and therefore painted
        /// LATER, so the neighbour correctly draws over it. An upward offset would have a far tile's shadow
        /// land on a nearer one it can never occlude.
        ///
        /// Height uses cw (the UNSQUASHED cell width): vertical extent on screen must not shrink with the
        /// camera tilt, or tilting the camera would flatten the buildings as well as the ground.
        ///
        /// dw/dh (Task B3 review fix): a VOLUME tile (Building/Wall/Gate) keeps the `tileOverdraw` overdraw —
        /// it has no cell-grid line to preserve, since a building's own front face already covers its south
        /// boundary, and the overdraw is what hides the seam between neighbours. A FLAT ground tile
        /// (Road/Void) instead shrinks by a flat `gridThicknessPx`, not a fraction of cw: the top face's
        /// point-anchor is the cell centre, so shrinking both axes by gridThicknessPx insets EVERY edge by
        /// exactly gridThicknessPx/2 from the cell boundary, and the grid line already drawn there (centred on
        /// the boundary, `gridThicknessPx` wide, see RebuildGrid) exactly fills that inset on both
        /// neighbours combined — a gap of gridThicknessPx screen pixels, independent of cw, at any scale.
        /// Multiplying by a fraction of cw (the overdraw's own idiom) would instead make the visible band
        /// scale WITH cw, defeating the "the constraint is one pixel" requirement this exists to meet.</summary>
        void ConfigureTile(TileView v, int i, int j, TileType type, float cw, float ch)
        {
            v.Root.anchoredPosition = Project(i, j);

            var room = type == TileType.Building ? RoomAtCell(i, j) : null;
            v.RoomId = room != null ? room.Id : 0;
            bool dummy = isSettlement && room != null && room.IsDummy && room.TypeId == 1;

            float h = HeightCells(type, room) * cw * heightScale;
            bool volume = h > 0.5f;   // sub-pixel extrusions read as a seam, not a box
            float dw = volume ? cw * tileOverdraw : cw - gridThicknessPx;
            float dh = volume ? ch * tileOverdraw : ch - gridThicknessPx;

            Color face = FaceColor(type, dummy);

            // Top face (also the whole visual for a flat tile).
            v.TopRt.sizeDelta = new Vector2(dw, dh);
            v.TopRt.anchoredPosition = new Vector2(0f, h);
            Paint(v.Top, volume ? sprites.RoofFor(type) : sprites.Resolve(sprites.GroundFor(type)), face);

            if (volume)
            {
                v.FrontRt.sizeDelta = new Vector2(dw, h);
                v.FrontRt.anchoredPosition = new Vector2(0f, -ch * 0.5f + h * 0.5f);
                Paint(v.Front, sprites.Resolve(sprites.wallSprite), Shade(face, frontShade));

                float ew = dw * eastFaceFraction;
                if (ew > 0.5f)
                {
                    v.EastRt.sizeDelta = new Vector2(ew, h);
                    v.EastRt.anchoredPosition = new Vector2(dw * 0.5f - ew * 0.5f, -ch * 0.5f + h * 0.5f);
                    Paint(v.East, sprites.Resolve(sprites.wallSprite), Shade(face, eastShade));
                }

                v.ShadowRt.sizeDelta = new Vector2(dw * 1.15f, dh * 1.15f);
                v.ShadowRt.anchoredPosition = new Vector2(cw * 0.14f, -ch * 0.14f);
                Paint(v.Shadow, sprites.Resolve(sprites.shadowSprite), shadowColor, tintSprite: true);
            }
            v.FrontRt.gameObject.SetActive(volume);
            v.EastRt.gameObject.SetActive(volume && dw * eastFaceFraction > 0.5f);
            v.ShadowRt.gameObject.SetActive(volume);

            // Marks: ACTIVE buildings only, exactly as the schematic gated them (settlement + TypeId 1 + not
            // a dummy). Sized off the cell so they scale with the view instead of pinning to a magic pixel
            // count at every zoom.
            bool markable = isSettlement && type == TileType.Building && room != null && room.TypeId == 1 && !dummy;
            float markPx = Mathf.Max(3f, cw * 0.26f);
            SetMark(v.PreviewMark, markable && room.Preview != null, markPx);
            SetMark(v.InteriorMark, markable && RoomsWithInterior != null && RoomsWithInterior.Contains(room.Id), markPx);

            v.Highlight.enabled = v.RoomId != 0 && highlighted.Contains(v.RoomId);
        }

        static void SetMark(GameObject mark, bool on, float px)
        {
            if (mark == null) return;
            if (on) ((RectTransform)mark.transform).sizeDelta = new Vector2(px, px);
            mark.SetActive(on);
        }

        /// <summary>Assign a face's sprite + colour. A null sprite is the PROCEDURAL placeholder: a UI Image
        /// with no sprite draws a solid quad in Image.color, so there is no separate placeholder code path.
        /// When real art IS resolved the tint drops to white so the sprite shows as authored — except for the
        /// shadow (<paramref name="tintSprite"/>), where the colour IS the effect and a soft white blob is the
        /// right thing to author.</summary>
        static void Paint(Image img, Sprite sprite, Color color, bool tintSprite = false)
        {
            img.sprite = sprite;
            img.color = (sprite != null && !tintSprite) ? Color.white : color;
        }

        /// <summary>Tile height in CELL units — SettlementTileGrid owns every one of these numbers, so the
        /// wall reading as taller than the tallest possible house is its invariant, not a constant duplicated
        /// here. A Building with no resolvable room falls back to the minimum rather than guessing a hash.</summary>
        static float HeightCells(TileType type, Room room)
        {
            switch (type)
            {
                case TileType.Building:
                    return room != null ? SettlementTileGrid.BuildingHeight(room.Id)
                                        : SettlementTileGrid.BuildingHeightMin;
                case TileType.Wall: return SettlementTileGrid.WallHeight;
                case TileType.Gate: return SettlementTileGrid.GateHeight;
                default:            return 0f;   // Road / Void / None lie flat on the ground plane
            }
        }

        Color FaceColor(TileType type, bool dummy)
        {
            switch (type)
            {
                case TileType.Building: return dummy ? dummyColor : buildingColor;
                case TileType.Wall:     return wallColor;
                case TileType.Gate:     return gateColor;
                case TileType.Road:     return roadColor;
                case TileType.Void:     return voidColor;
                default:                return voidColor;
            }
        }

        /// <summary>Multiply RGB, keep alpha — the side faces are the top face "in shade", so they must track
        /// a retuned palette colour instead of being independent constants that drift out of family.</summary>
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }
}
