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
    /// Substituting CenterY(j) = (j + 0.5)*Cell (the ABSOLUTE lattice — see SettlementFootprint) and
    /// collecting the j-independent part into `oy`:
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

        /// <summary>How far a drawn tile's PIXELS reach PAST ITS OWN CELL BOUNDARY, in cells, on the WEST
        /// (min-X) and NORTH (min-Y) sides — the "near" sides, away from the drop shadow. A caller fitting this
        /// view (DungeonViewController.FitBoundsFor) budgets the GROUND LATTICE out to the cell boundary and
        /// must then add this, or the outermost tiles are shaved at the panel edge.
        ///
        /// Two candidate reaches, MAX of the two — both are measured from the same cell centre, so the larger
        /// simply subsumes the smaller; adding them would double-count:
        ///   • the tile's own faces, each sized <see cref="tileOverdraw"/> × the cell pitch → reach od/2;
        ///   • its drop shadow, a quad of od × <see cref="ShadowScale"/> offset <see cref="ShadowOffsetCells"/>
        ///     right and DOWN the screen, so on the near sides the offset SUBTRACTS → reach
        ///     od·ShadowScale/2 − ShadowOffsetCells.
        /// Across the whole slider range (1.00–1.25) the faces win here — the shadow only overtakes them above
        /// od ≈ 1.87 — so the Max is defensive, not load-bearing. At the 1.04 default this is 0.52 − 0.5 = 0.02
        /// cell (0.179 tiles); at 1.25 it is 0.125 cell (1.12 tiles).
        ///
        /// A FLAT tile (Road/Void) needs no term at all: it is drawn NARROWER than its cell (cw −
        /// gridThicknessPx, so the cell-grid line shows through) and its shadow is switched off.
        ///
        /// SERIALIZED, NOT CONSTANT — the same reason <see cref="ExtrusionHeadroomTiles"/> gives for reading its
        /// own tunables: the DM dials tileOverdraw live at the checkpoint, and a fit computed against the
        /// default would clip the moment the slider moves.</summary>
        public float TileOverhangMinCells
        {
            get
            {
                float od = tileOverdraw > 0f ? tileOverdraw : 1f;
                return Mathf.Max(od * 0.5f, od * ShadowScale * 0.5f - ShadowOffsetCells) - 0.5f;
            }
        }

        /// <summary>The same reach on the EAST (max-X) and SOUTH (max-Y) sides, where the drop shadow's offset
        /// ADDS instead of subtracting — so the shadow always wins here (ShadowScale/2 > 0.5 for any overdraw)
        /// and this is the larger of the two margins. Down-screen is +Y in TILE space (DungeonProjection
        /// inverts Y once), which is why the shadow lands on max-Y and not min-Y. At the 1.04 default this is
        /// 0.738 − 0.5 = 0.238 cell (2.132 tiles); at 1.25 it is 0.359 cell (3.21 tiles). See
        /// <see cref="TileOverhangMinCells"/> for the rest of the derivation.</summary>
        public float TileOverhangMaxCells
        {
            get
            {
                float od = tileOverdraw > 0f ? tileOverdraw : 1f;
                return Mathf.Max(od * 0.5f, od * ShadowScale * 0.5f + ShadowOffsetCells) - 0.5f;
            }
        }

        /// <summary>Drop-shadow geometry, in units of the (already overdrawn) tile size and of the cell pitch
        /// respectively. Named rather than inlined at the one draw site because the FIT reads them too
        /// (TileOverhangMin/MaxCells): two copies of 1.15 / 0.14 would be free to drift, and the failure mode —
        /// a shadow shaved at the panel edge — is exactly what Task B4 was fixing.</summary>
        const float ShadowScale = 1.15f;
        const float ShadowOffsetCells = 0.14f;

        /// <summary>Has-interior mark (Ц2): building room ids whose own interior already exists on file.
        /// EXTERNAL state, set by DungeonEditorScreen BEFORE the RebuildView chain fires — deliberately NOT
        /// reset inside RebuildView. Declared with the IDENTICAL name/shape as
        /// <see cref="DungeonFlatRenderer.RoomsWithInterior"/> so the screen's two assignment sites — funnelled
        /// through DungeonEditorScreen.SetRoomsWithInterior (Task 8) — work on either renderer without a second
        /// code path.</summary>
        public HashSet<int> RoomsWithInterior { get; set; } = new HashSet<int>();

        /// <summary>Streets to DRAW but not store — the drag's preview road (sub-project B). The controller
        /// sets it per sample and clears it on release; the renderer only forwards it to
        /// SettlementTileGrid.Build.</summary>
        public System.Collections.Generic.IReadOnlyList<(int i, int j)> PreviewStreets { get; set; }

        /// <summary>Cells the BRUSH is drawing right now, drawn as Building but never written to the floor —
        /// one of three preview channels (see also PreviewStreets and PreviewErased), all set by
        /// DungeonViewController on every stroke sample. The wall ring re-derives around them
        /// (SettlementTileGrid.Build writes them before BuildWallRing), so the DM watches the town's own wall
        /// grow to enclose the stroke before releasing the brush. EXTERNAL STATE, exactly like PreviewStreets:
        /// nothing here clears it on its own — see <see cref="ClearPreviews"/>, the ONE method that clears
        /// every channel together — and a controller that forgets to call it leaves phantom houses drawing on
        /// every later reposition.</summary>
        public System.Collections.Generic.IReadOnlyList<(int i, int j)> PreviewBuildings { get; set; }

        /// <summary>Cells the ERASER's scratch run has actually removed so far this stroke (Task 8) — the
        /// SUBTRACTIVE twin of PreviewBuildings/PreviewStreets, both of which are additive (they draw cells
        /// that are not in the data yet). A removal cannot be expressed that way, which is why this channel
        /// exists at all rather than reusing one of the other two: SettlementTileGrid.Build clears these cells
        /// to None (and drops them from the street mask) BEFORE the wall ring derives, so the DM watches the
        /// wall close IN around the stroke exactly as it watches the OTHER two channels grow it outward.
        /// DungeonViewController computes it INCREMENTALLY, never from the raw stroke: Erase refuses
        /// sequentially (CanErase re-asked after every removal), so a preview built from "the cells the stroke
        /// covered" would promise removals the release then refuses. See DungeonViewController's eraseScratch
        /// field for the construction. EXTERNAL STATE, exactly like the other two — cleared only through
        /// <see cref="ClearPreviews"/>.</summary>
        public System.Collections.Generic.IReadOnlyList<(int i, int j)> PreviewErased { get; set; }

        /// <summary>Clear every preview channel TOGETHER and report whether any of them was actually live
        /// (non-null) beforehand — the ONE place all three are nulled, so a fourth channel added later cannot
        /// repeat the shape this branch shipped three separate times: one channel cleared, its twin left
        /// standing (OnEndDrag's brush branch, SetBrush, Bind). Every call site in DungeonViewController
        /// follows the SAME contract this method's return enforces: clear, and repaint ONLY when this returned
        /// true — never clear-and-forget, and never repaint unconditionally either (a repaint the caller cannot
        /// afford belongs to the caller's own gating, not to a channel staying uncleared to avoid it).</summary>
        public bool ClearPreviews()
        {
            bool hadAny = PreviewStreets != null || PreviewBuildings != null || PreviewErased != null;
            PreviewStreets = null;
            PreviewBuildings = null;
            PreviewErased = null;
            return hadAny;
        }

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
        // Cell -> the room that owns it, keyed by DepthKey (unique per cell at this grid's scale). EVERY cell
        // of every room's FOOTPRINT is keyed, not just its representative point — see RebuildCellRooms for the
        // bug that keying-by-point caused. Rebuilt every reposition alongside the grid itself; used for
        // per-building height, dummy shading, the selection outline, FACE SUPPRESSION (a cell asks whether its
        // neighbour is itself) and the placement occupancy test.
        readonly Dictionary<long, Room> cellRooms = new Dictionary<long, Room>();
        // Room id -> the DepthKey of its footprint's SettlementFootprint.Representative cell. Filled in the
        // same pass as cellRooms so ConfigureTile can draw the two corner marks ONCE per building rather than
        // once per cell, WITHOUT a fourth per-frame FootprintOf pass (Allocate and Build already make two).
        readonly Dictionary<int, long> repCellByRoom = new Dictionary<int, long>();

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

        /// <summary>Replace the projection wholesale and repaint the last-drawn layout — WITHOUT re-routing
        /// anything. That is the whole value of this entry point and the reason it now has a caller:
        /// DungeonViewController.RefitFromCache, the panel-RESIZE re-fit. A resize changes the SCALE and
        /// nothing else, so the cached lvl/rg still describe the layout exactly; rebuilding through Refresh
        /// would re-run BuildRenderGraph once per resized frame, which measured 106 ms at 20 rooms.
        ///
        /// NO TIER TO GET WRONG ANY MORE (Task 5). This used to pass includeRoadsInFence: true for parity with
        /// the flat renderer, and carried a live limitation with it: the tile grid took its Road cells from the
        /// cached render graph, so a repaint taken right after a Fast drag frame (whose graph carried no roads)
        /// would draw without them. The grid reads STORED street cells now and the flag is gone, so a repaint
        /// is identical whenever it is taken.</summary>
        public void SetProjection(DungeonProjection p)
        {
            Projection = p;
            RepositionRooms(lastLvl, lastRg);
        }

        // ── Build / reposition ───────────────────────────────────────────────────────────────────────────

        /// <summary>Structural rebuild. Unlike the flat renderer this does NOT tear down and recreate the
        /// visuals: a tile view is generic (it is reconfigured for whatever cell lands in its slot), so the
        /// pool survives a rebind and only the CONTENT is re-derived. `font` and `onJumpToLevel` are unused —
        /// the volumetric view carries no per-room label or floor-jump badge (a settlement has exactly one
        /// floor, and 40-80 labelled cards is what the schematic did badly); they stay in the signature
        /// because the interface owns it. `rg` is likewise unused by this renderer — a settlement's tiles come
        /// from SettlementTileGrid.Build(lvl), never from the routed graph.</summary>
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
            RepositionRooms(lvl, rg);
        }

        /// <summary>Cheap per-frame repaint from the CURRENT room positions — every drag sample and cascade
        /// frame. "Cheap" here means no allocation of GameObjects and no destroy: the tile grid itself IS
        /// re-derived, because moving a building changes which cells are Building, which cells the wall ring
        /// runs through, and where the gates land. That derive is a few hundred cells of dilate + flood-fill.
        ///
        /// ONE TIER, and the flag that used to select one is gone with the road router (Task 5): the grid is
        /// built from the floor's rooms and its STORED street cells on every frame, so a drag sample and a
        /// bind produce the identical grid.</summary>
        public void RepositionRooms(InteriorFloor lvl, RenderGraph rg)
        {
            EnsureBuilt();
            lastLvl = lvl;
            lastRg = rg ?? new RenderGraph();
            if (lvl == null)
            {
                grid = null;
                ClearCellRooms();
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
                ClearCellRooms();
                HideFrom(0);
                HideGridLines();   // same reasoning, via PxPerTile this time — no cw/ch to compute yet
                return;
            }

            // SettlementTileGrid.Build reads its STORED streets itself (SettlementParams.StreetCells) — `rg`
            // reaches nothing this method draws — and is additionally handed PreviewStreets and
            // PreviewBuildings, the drag's uncommitted preview road/building (sub-project B / checkpoint-1):
            // drawn exactly like stored data, but never written back to the floor (see each property's own
            // doc). PreviewErased (Task 8) is the fourth argument — the SUBTRACTIVE twin of the other two.
            grid = SettlementTileGrid.Build(lvl, PreviewStreets, PreviewBuildings, PreviewErased);
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

        /// <summary>Map EVERY cell of every room's FOOTPRINT onto that room, so a Building tile can find its
        /// own room (height, dummy shading, the corner marks, the selection outline, and whether its neighbour
        /// is itself) no matter WHICH cell of a multi-cell building it happens to be.
        ///
        /// THE BUG THIS REPLACES, stated plainly because it is exactly what the DM reported with a screenshot.
        /// This used to key a room by its representative POINT alone — DepthKey(CellI(r.X), CellJ(r.Y)) — so
        /// for a multi-cell building every cell but one resolved to NO room. ConfigureTile then read those
        /// cells as `dummy == false` and painted decorative filler in the ACTIVE building colour (a town
        /// configured with 2 active buildings showed a dozen), and HeightCells fell back to BuildingHeightMin
        /// on them, putting a STEP inside a single house. Keying every cell is also the precondition for face
        /// suppression: without it no cell could ever tell that its neighbour is the same building.
        ///
        /// Uses <see cref="SettlementTileGrid.FootprintOf"/> — the SAME call SettlementTileGrid.Build makes to
        /// write the Building tiles — so the map is total over every Building cell BY CONSTRUCTION, and it
        /// inherits that method's rule (a) point fallback for a cell-less room and rule (b) re-derive of a
        /// stale single-cell footprint. Nothing here decodes Room.Cells itself; doing so would be a second
        /// reading of the same data, free to drift from the one that decides which tiles exist.
        ///
        /// GATES ARE IN THE MAP, AND INERT FOR DRAWING. ConfigureTile consults it only for TileType.Building,
        /// and a gate (TypeId 0) writes no Building tile — Build writes those from TypeId==1 footprints only.
        /// What genuinely needs gates here is <see cref="AnyRoomAtCell"/>: a gate's own cell commonly reads
        /// Void or Road (its drawn Gate tile is the nearest WALL-RING cell, SettlementTileGrid.MarkGates), so
        /// dropping gates would let «+ Здание» place a second room straight on top of one. <see cref="Precedes"/>'s
        /// buildings-beat-gates term is what stops a Building tile ever resolving to a gate room.
        ///
        /// repCellByRoom is filled in the same pass — see its own field doc.</summary>
        void RebuildCellRooms(InteriorFloor lvl)
        {
            ClearCellRooms();
            if (grid == null || lvl == null) return;
            foreach (var r in lvl.Rooms)
            {
                var fp = SettlementTileGrid.FootprintOf(r);
                var rep = SettlementFootprint.Representative(fp);
                repCellByRoom[r.Id] = SettlementTileGrid.DepthKey(rep.i, rep.j);
                foreach (var c in fp)
                {
                    long key = SettlementTileGrid.DepthKey(c.i, c.j);
                    if (cellRooms.TryGetValue(key, out var prev) && prev != null && !Precedes(prev, r)) continue;
                    cellRooms[key] = r;
                }
            }
        }

        /// <summary>Drop both per-cell maps together. They are filled in one pass and must never be allowed to
        /// describe different frames, so the two early-return paths in RepositionRooms clear them through this
        /// one call rather than remembering to clear each.</summary>
        void ClearCellRooms()
        {
            cellRooms.Clear();
            repCellByRoom.Clear();
        }

        /// <summary>Ordering on the rooms contending for ONE cell. The winner is both the room that cell is
        /// DRAWN as and the room a click on it SELECTS. True if `candidate` should displace `held`.
        ///
        /// BUILDINGS STRICTLY BEAT GATES. That term is not a preference — it is what guarantees ConfigureTile's
        /// RoomAtCell on a Building tile can never hand back a gate room, which would draw the house at a
        /// gate's height with no marks and hand a click on the house to the gate. A gate can only ever own a
        /// cell no building claims. Then the visually FRONT-MOST (larger Y, i.e. nearer the viewer), then the
        /// larger Id — purely so the answer is deterministic instead of list-order-dependent.
        ///
        /// THE ONE COPY OF THE RULE, deliberately: <see cref="RebuildCellRooms"/> (what a cell draws as) and
        /// <see cref="HitRoomId"/> (what a click selects) both call this, so the tile you see and the room you
        /// get agree by construction rather than by two comparisons kept in sync by hand.
        ///
        /// TEMPORARY — the whole Y/Id tie-break below the type term is scheduled for deletion. Two rooms
        /// wanting one cell is an INVARIANT VIOLATION, not a tie: **Task 4** makes the validator report
        /// overlapping footprints, and **Task 7** makes a drag REJECT a translation that would overlap.
        ///
        /// BOTH HALVES OF THAT CONDITION ARE NOW MET, AND THE TIE-BREAK STILL STAYS — the reason has changed,
        /// so read this before deleting it. Task 7 landed: DungeonViewController's footprint drag evaluates
        /// <see cref="AreCellsFree"/> over the whole proposed footprint and simply does not move the building
        /// when any cell is already another room's, so a drag can no longer MANUFACTURE an overlap. Task 4
        /// landed too: DungeonValidator.SettlementIssues now REPORTS two buildings sharing a cell, as an
        /// Error naming both room ids and the cell.
        ///
        /// What neither of them does is REPAIR an overlap that is already in the data — and that state is
        /// still reachable, from a save written while the pre-Task-7 trade was open or from a hand-edited
        /// file. Reporting it is not the same as making it impossible, so a cell with two claimants must
        /// still resolve to exactly one room deterministically, or the DM would be looking at undefined
        /// rendering while reading the very error that describes it. The tie-break is what makes the
        /// reported state legible instead of flickering. Delete it only once something REPAIRS overlaps on
        /// load, not merely flags them.</summary>
        static bool Precedes(Room held, Room candidate)
        {
            bool heldBuilding = held.TypeId == 1, candBuilding = candidate.TypeId == 1;
            if (candBuilding != heldBuilding) return candBuilding;
            return candidate.Y > held.Y || (candidate.Y == held.Y && candidate.Id > held.Id);
        }

        Room RoomAtCell(int i, int j)
            => cellRooms.TryGetValue(SettlementTileGrid.DepthKey(i, j), out var r) ? r : null;

        /// <summary>The face-suppression test: is cell (i,j) owned by the SAME room as `room`?
        ///
        /// `room` is non-null only on a Building tile (ConfigureTile passes null for every other type), so a
        /// Wall, Gate, Road or Void tile can never suppress anything and the wall ring keeps every one of its
        /// faces — which is what it must do, since two adjacent Wall cells share no room and the "is my
        /// neighbour me" question is meaningless for them.
        ///
        /// A neighbour OUTSIDE the grid, one owned by nobody (Void/Road/Wall), and one owned by a DIFFERENT
        /// room all answer false through the same dictionary miss or id mismatch. That is exactly right at the
        /// grid's edges: there is nothing out there to hide the face behind, so the town's outer buildings
        /// keep their south faces and east strips.</summary>
        bool SameRoomAt(Room room, int i, int j)
        {
            if (room == null) return false;
            var other = RoomAtCell(i, j);
            return other != null && other.Id == room.Id;
        }

        /// <summary>Is (i,j) the cell that stands in for the WHOLE of `room` — its footprint's
        /// SettlementFootprint.Representative? The two corner marks are drawn there and nowhere else, so a
        /// 6-cell building carries one has-image dot and one has-interior dot rather than six of each.
        /// O(1) off the map built in <see cref="RebuildCellRooms"/>; never re-derives a footprint.</summary>
        bool IsRepresentativeCell(Room room, int i, int j)
            => room != null && repCellByRoom.TryGetValue(room.Id, out long rep)
               && rep == SettlementTileGrid.DepthKey(i, j);

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
        /// SquashY is &lt;= 0, which is precisely the "dragged room snaps to the corner" bug.
        ///
        /// PUBLIC since Task 7. A footprint drag is a CELL DELTA — the cell the press landed in subtracted
        /// from the cell the pointer is over now — so the controller needs the cell INDEX, not the cell centre
        /// <see cref="TryAreaToNorm"/> answers. Deriving the index from that centre would work but would put a
        /// second area→cell mapping in the controller, free to drift from this one; the index is the primitive
        /// and the centre is derived from it, so the primitive is what gets exposed.</summary>
        public bool TryAreaToCell(Vector2 areaLocalPoint, out int i, out int j)
        {
            i = 0; j = 0;
            if (grid == null) return false;
            if (Projection.PxPerTile <= 0f || Projection.SquashY <= 0f) return false;
            var (tx, ty) = Projection.LocalToTile(areaLocalPoint.x, areaLocalPoint.y);
            i = grid.CellI(tx / DungeonLayout.TilesPerAxis);
            j = grid.CellJ(ty / DungeonLayout.TilesPerAxis);
            return true;
        }

        /// <summary>Nearest Wall or Gate cell to WORLD cell (i, j) — a thin wrapper over
        /// <see cref="SettlementTileGrid.NearestWallCell"/>, THE SAME RULE SettlementTileGrid.MarkGates uses
        /// (final-arc-review fix: this used to be a hand-duplicated copy of that search; the two are now
        /// literally one implementation, compiled and mutation-tested in Generation, so a future divergence
        /// here is structurally impossible rather than merely absent today). Two different answers to "which
        /// wall cell is nearest" is exactly the seam that made a gate un-clickable in the first place — the
        /// drag would move the room onto cell X while MarkGates drew the tile on cell Y. False when the grid
        /// holds no Wall or Gate cell at all (a wall-less town).</summary>
        public bool TryNearestWallCell(int i, int j, out int wallI, out int wallJ)
        {
            wallI = 0; wallJ = 0;
            if (grid == null) return false;
            return SettlementTileGrid.NearestWallCell(grid, i, j, out wallI, out wallJ);
        }

        // ── Hit-test and screen -> norm (renderer-owned since Task 6) ────────────────────────────────────

        /// <summary>The room standing on the clicked CELL, or 0 for a miss.
        ///
        /// FOOTPRINT CONTAINMENT, not the room's representative point. This is REQUIRED by the same defect
        /// RebuildCellRooms fixes, even though sprite-accurate picking is a later arc: with a point test a
        /// multi-cell building is clickable on exactly ONE of its cells and every other cell of the very same
        /// house reads as background — so a click there CLEARS the selection (OnPointerClick keys that on
        /// `id == 0`) and a press-drag there moves nothing. The DM would be dragging houses by a corner they
        /// cannot see. A cell belongs to a room iff it is in SettlementTileGrid.FootprintOf(room), the identical
        /// call that decided which Building tiles exist.
        ///
        /// Returns 0, not -1, on a miss: the interface contract says 0 = background. (The original task brief
        /// said -1; that would silently break background-click-clears-selection.)
        ///
        /// EVERY room is a candidate, buildings (TypeId 1) and GATES (TypeId 0) alike — a gate is a draggable
        /// room like any other, and filtering to buildings would make gates unselectable outright. Contention
        /// for one cell resolves through <see cref="Precedes"/>, the SAME rule RebuildCellRooms uses to pick
        /// which room a tile DRAWS as, so what you see and what you select agree by construction.
        ///
        /// SCANS <paramref name="lvl"/> RATHER THAN READING cellRooms, deliberately. The map would be O(1) and
        /// is normally identical, but it describes the floor LAST DRAWN; this method's contract (IDungeonRenderer)
        /// is that lvl "is the room list the answer is drawn from". Reading the map would need a freshness
        /// guard whose failure mode is every click in the town dead — the worst possible regression on the task
        /// whose whole purpose is making buildings clickable. The scan has no such path and costs ~80 rooms of
        /// a few cells each, once per click.
        ///
        /// A GATE'S DRAWN TILE, closed (Task 2 of the block-forms/gates arc): a Gate TILE is the wall-ring cell
        /// NEAREST the gate room (SettlementTileGrid.MarkGates), not the gate room's own footprint cell, so the
        /// FootprintOf scan above misses it. The fallback past the loop below resolves that tile through
        /// SettlementTileGrid.GateRoomAt, the map MarkGates fills for exactly this.
        ///
        /// <paramref name="lvl"/> must be non-null — the controller only ever calls this with its bound
        /// level — but a null is handled as a miss rather than a throw.</summary>
        public int HitRoomId(Vector2 areaLocalPoint, InteriorFloor lvl)
        {
            if (lvl == null) return 0;
            if (!TryAreaToCell(areaLocalPoint, out int i, out int j)) return 0;
            Room best = null;
            foreach (var r in lvl.Rooms)
            {
                if (!FootprintContains(SettlementTileGrid.FootprintOf(r), i, j)) continue;
                if (best == null || Precedes(best, r)) best = r;
            }
            if (best != null) return best.Id;
            // A drawn Gate tile is the wall-ring cell NEAREST its gate room, not the room's own cell, so the
            // footprint scan above cannot find it. Reading `grid` here is safe where reading it for BUILDINGS
            // would not be: this line is reached only where the scan already missed, so a stale grid can
            // mis-select a gate but can never break a building click.
            if (grid != null && grid.GateRoomAt.TryGetValue(SettlementTileGrid.DepthKey(i, j), out int gateRoom))
                return gateRoom;
            return 0;
        }

        /// <summary>Linear membership test over a footprint. A settlement building holds a handful of cells
        /// (the shape palette's templates cap at 6 cells — SettlementBlocks.Palette, "Rect6" — well inside
        /// single digits), so a HashSet per room per click would cost more to build than this costs to
        /// scan.</summary>
        static bool FootprintContains(List<(int i, int j)> cells, int i, int j)
        {
            for (int k = 0; k < cells.Count; k++)
                if (cells[k].i == i && cells[k].j == j) return true;
            return false;
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
        /// cells from the old anchor and the lattice does not shift under the cursor.
        ///
        /// NO LIVE CALLER SINCE TASK 7, and it is not dead code. DungeonViewController.OnDrag used to route a
        /// settlement drag through here; it now translates the room's CELLS (TranslateFootprintTo) and derives
        /// the point from them, because a point cannot describe where a multi-cell building's other cells went.
        /// This stays because it is part of the IDungeonRenderer contract — the controller calls it through the
        /// interface for the flat renderer's rooms, and a renderer that refused to implement it would not be
        /// substitutable. Removing it would mean narrowing the interface, which is a different task.</summary>
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
        /// NOTHING CLOSES IT TODAY, AND THAT IS AN OPEN DEFECT, NOT A DESIGN (corrected again at Task 5). The
        /// commit side used to claim the answer: DungeonViewController.PlaceHoveredBuilding auto-linked the new
        /// room to its nearest neighbour, a road was routed along that link, the road rasterized into cells,
        /// and those cells joined BuildWallRing's dilation seed so the two clusters merged into ONE ring. Two
        /// separate changes killed that chain — the tile grid stopped taking roads (arc A task 2; the seed is
        /// Building ∪ StreetCells, which no editor path writes) and Task 5 deleted the router — so the link
        /// stopped closing anything long before it was itself removed (Task 5 again: a town has no links).
        /// A building founded well clear of town therefore ends up sealed in its own 5x5 ring, permanently.
        /// The fix is an editor-side StreetCells writer — the new building's cell, and a carved street
        /// reaching it, written into SettlementParams.StreetCells so the ring seed and the drawn streets both
        /// see it — and it belongs to the next arc. Until then this is a known, DM-visible consequence of a
        /// permissive placement rule; a graph edge was never going to substitute for stored geometry.
        ///
        /// Static and type-only on purpose: the caller (DungeonViewController) must never make its own
        /// judgement about a cell, so the green/red the DM sees and the accept/reject the click applies are
        /// literally the same expression evaluated twice.
        ///
        /// FOUNDING AND MOVING NOW AGREE ABOUT DERIVED TILES (checkpoint-1 amendment, Task 3a). Wall and Gate
        /// are refused by NEITHER verdict any more — both are DERIVED from (buildings ∪ streets) and
        /// re-derive around whatever is founded or moved, so refusing them here was never more than a
        /// self-referential test. This rule is still consumed by <see cref="AreCellsPlaceable"/> alone; a DRAG
        /// goes through <see cref="AreCellsFree"/>, which still does not call it — not because a move needs a
        /// different Wall/Gate answer any more, but because AreCellsFree's own ownership term already refuses
        /// a Building cell (RoomAtCell resolves it to the room standing there, and only the mover is exempt
        /// from its own footprint). What still separates the two verdicts is exactly that MOVER EXEMPTION:
        /// <see cref="AreCellsPlaceable"/> always calls with moverRoomId 0 (no room is exempt — there is no
        /// room yet), while <see cref="AreCellsFree"/> exempts whichever room is being dragged from its own
        /// cells.</summary>
        public static bool IsPlaceable(TileType type) => SettlementVolumeRendererPlacement.IsPlaceable(type);

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
        /// nothing — an invented cell would be a lie the DM could act on.
        ///
        /// A SET VERDICT OVER A ONE-CELL SET since Task 7: the whole judgement is
        /// <see cref="AreCellsPlaceable"/>, the same method the arc-2 drag-to-draw shape will hand a
        /// many-cell set. «+ Здание» founds ONE cell (task brief step 3, and DungeonOps.AddRoom writes exactly
        /// that one cell), so the set it builds here is a singleton — but the RULE is no longer written
        /// per-cell, which is what stops a multi-cell caller from having to restate it.</summary>
        public bool TryPlacementCell(Vector2 areaLocalPoint, InteriorFloor lvl, out float nx, out float ny, out bool placeable)
        {
            nx = 0f; ny = 0f; placeable = false;
            if (!TryAreaToCell(areaLocalPoint, out int i, out int j)) return false;
            nx = grid.CenterX(i);
            ny = grid.CenterY(j);
            placeable = AreCellsPlaceable(new List<(int i, int j)> { (i, j) });
            return true;
        }

        /// <summary>THE FOUNDING VERDICT: may a building that does not exist yet occupy exactly
        /// <paramref name="cells"/>? A SET verdict — every cell must pass, or the whole placement is refused.
        /// «+ Здание»'s green/red preview and its click both evaluate this one expression, so what the DM sees
        /// and what the click applies can never be two different judgements.
        ///
        /// It is <see cref="AreCellsFree"/> (on-field, and no cell owned by any existing room — mover id 0
        /// means "no room is exempt") PLUS the tile-type rule <see cref="IsPlaceable"/>. That extra term keeps
        /// a new house out of ANOTHER HOUSE — it no longer keeps one out of the wall ring, since checkpoint-1
        /// amended IsPlaceable to refuse only Building (see its own doc). What keeps a house off a GATE
        /// room's own cell is not this term at all — a gate writes no Building tile of its own — it is
        /// <see cref="AreCellsFree"/>'s room-ownership term, unchanged by this task.</summary>
        public bool AreCellsPlaceable(IReadOnlyList<(int i, int j)> cells)
        {
            if (!AreCellsFree(cells, 0)) return false;
            for (int k = 0; k < cells.Count; k++)
                if (!IsPlaceable(grid.At(cells[k].i, cells[k].j))) return false;
            return true;
        }

        /// <summary>THE MOVE VERDICT: may the room <paramref name="moverRoomId"/> occupy exactly
        /// <paramref name="cells"/>? A SET verdict over the whole proposed footprint — one bad cell refuses
        /// the entire translation, because half a building cannot move.
        ///
        /// TWO AXES, and they are exactly the two the task brief names — "would overlap another footprint, or
        /// leave the field":
        ///   • ON FIELD. Every proposed cell's CENTRE must lie in 0..1. This replaces the old drag clamp
        ///     (DragClampMin/Max) for a settlement, and it is the honest version of it: the clamp squeezed a
        ///     POINT back onto the board, which for a multi-cell building would have said nothing about where
        ///     its far cells ended up. See <see cref="SettlementVolumeRendererPlacement.OnFieldCell"/> for why
        ///     0..1 exactly (the cascade's Clamp01).
        ///   • NO CELL OWNED BY ANOTHER ROOM. cellRooms is FOOTPRINT ownership over every room of every type,
        ///     gates included, so this one term rejects building-onto-building, building-onto-gate, and
        ///     gate-onto-building alike. The last of those is not decoration: <see cref="Precedes"/> makes a
        ///     building STRICTLY beat a gate for a cell, so a gate dropped onto a building's footprint would
        ///     become permanently unselectable (a gate draws no tile of its own to click). Rejecting the
        ///     translation is what closes that.
        ///
        /// THE MOVER IS EXEMPT FROM ITS OWN CELLS, and it must be: translating a 2x2 by one cell proposes a
        /// 1x2 strip it already stands on, and the identity translation proposes all of them. Exemption is by
        /// ROOM ID off the same map, so it costs nothing and cannot disagree with what is drawn.
        ///
        /// WHY THE TILE-TYPE RULE (<see cref="IsPlaceable"/>) IS ABSENT HERE — RESTATED after checkpoint-1,
        /// because the argument this paragraph used to make no longer holds. It used to say: for a NEW
        /// building the wall ring is independent of the candidate, so testing the candidate's cells against it
        /// is meaningful, while for a MOVE it is self-referential (the ring is derived partly FROM the mover's
        /// own current cells) and for a GATE it is simply wrong. That whole distinction was about what
        /// <see cref="IsPlaceable"/> did with Wall/Gate tiles — and checkpoint-1 narrowed
        /// <see cref="IsPlaceable"/> to refuse ONLY Building (see its own doc), so there is no Wall/Gate case
        /// left for founding and moving to disagree about: NEITHER verdict tests against them any more.
        /// Calling <see cref="IsPlaceable"/> here would therefore be redundant, not wrong: the ownership term
        /// above (owner not null, and not the mover, off `cellRooms`) already refuses ANY cell another room
        /// owns — Building tile or not — through `RoomAtCell`, including a gate's own cell whatever tile it
        /// currently reads. What the ownership term buys that a bare tile-type test never
        /// could is the MOVER EXEMPTION itself: a translating room must be allowed back onto its OWN current
        /// cells, and <see cref="IsPlaceable"/> has no concept of "except the mover" to grant that. So the
        /// founding-vs-moving distinction this paragraph used to draw over Wall/Gate has collapsed entirely
        /// into that one exemption — see the <see cref="IsPlaceable"/> doc for the same point from the other
        /// side. THE DISCRIMINATING CHECK for this method is unchanged by the amendment: a gate must be
        /// draggable one cell onto the ring and back, in both directions, purely through the ownership term
        /// above and never through a tile-type refusal.
        ///
        /// ITS DATA-SIDE TWIN IS DungeonValidator.SettlementIssues' overlap rule (Task 4), and the two are
        /// DELIBERATELY NOT one shared predicate. They agree on the term that matters — a cell claimed by
        /// another room — and on how ownership is read (both go through SettlementTileGrid.FootprintOf), but
        /// they are different questions: this one is a MOVE verdict, so it carries a mover exemption and an
        /// on-field bound that a floor-wide invariant has no meaning for, and it answers from `cellRooms`, a
        /// map this renderer rebuilds every reposition frame. Factoring the common term out would mean either
        /// moving that map into Generation (Rendering code that has no business there) or threading it
        /// through as a parameter — more coupling than the single ContainsKey it would save. What the pair
        /// DOES buy is coverage: Rendering cannot be compiled by the offline harness, so this method has no
        /// automated test, while the validator rule is pinned headlessly and by MutValidatorNoOverlapRule.
        ///
        /// EMPTY IS REFUSED. A null/empty set is not "trivially fine" — it is a room with no footprint, and
        /// accepting it would let a caller write an empty Cells array back onto a building.
        ///
        /// grid == null is refused too. Both callers reach here only past TryAreaToCell, which already
        /// refuses that state, but this is now consumed by two features and must not depend on that.</summary>
        public bool AreCellsFree(IReadOnlyList<(int i, int j)> cells, int moverRoomId)
        {
            if (grid == null || cells == null || cells.Count == 0) return false;
            for (int k = 0; k < cells.Count; k++)
            {
                var (i, j) = cells[k];
                if (!SettlementVolumeRendererPlacement.OnFieldCell(i, j)) return false;
                var owner = RoomAtCell(i, j);
                if (owner != null && owner.Id != moverRoomId) return false;
            }
            return true;
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
            public int RoomId;   // 0 when this cell carries no room (Void/Road, or an orphaned Wall cell);
                                  // a Gate cell carries its gate room's id too, resolved via GateRoomAt
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
        /// field-relative span (see the near-symmetric case OnFieldCell's own doc calls out: MarginCells beyond
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
        /// separate ground quad; a flat tile (Road/Void) is just the same top face with H = 0. When the front
        /// face is SUPPRESSED (see the face-suppression block below) that band is covered instead by the
        /// same-room south neighbour, which is drawn later and reaches from its own south boundary up past
        /// this cell's top face — so the coverage argument survives the suppression rather than being an
        /// exception to it.
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
            // A Gate tile's drawn cell is never the gate room's own footprint cell (MarkGates puts the tile on
            // the nearest wall-ring cell), so `room` above is correctly null here and stays null — only
            // v.RoomId is resolved separately, through GateRoomAt, the same map HitRoomId's fallback reads.
            // `room` itself must NOT become the gate room: it also feeds `dummy` (gate rooms are TypeId 0, so
            // the TypeId==1 term keeps this false either way), HeightCells (the Gate case ignores its `room`
            // argument entirely and returns GateHeight unconditionally), the SameRoomAt face-suppression pair
            // below (a gate room is not what "same building" means, and feeding it in could wrongly suppress a
            // wall/gate face if some neighbour cell happened to map to that same gate room's OWN cell in
            // cellRooms) and `markable` (already gated on `type == TileType.Building`, so a Gate never reaches
            // it regardless). None of the four would do anything useful with a gate room — v.RoomId is the only
            // one of the five uses that needs it, because it is the only one that drives selection.
            int gateRoomId = 0;
            if (type == TileType.Gate && grid != null)
                grid.GateRoomAt.TryGetValue(SettlementTileGrid.DepthKey(i, j), out gateRoomId);
            v.RoomId = room != null ? room.Id : gateRoomId;
            bool dummy = isSettlement && room != null && room.IsDummy && room.TypeId == 1;

            float h = HeightCells(type, room) * cw * heightScale;
            bool volume = h > 0.5f;   // sub-pixel extrusions read as a seam, not a box
            float dw = volume ? cw * tileOverdraw : cw - gridThicknessPx;
            float dh = volume ? ch * tileOverdraw : ch - gridThicknessPx;

            Color face = FaceColor(type, dummy);

            // FACE SUPPRESSION — the part that decides whether the DM can read the town at all. A dividing
            // face is drawn only where there is genuinely a division: the south face when the cell to the
            // SOUTH belongs to a different room (or to none), the east strip likewise for the cell EAST. Two
            // cells of ONE building draw no face between them, so a footprint extrudes as a single solid;
            // two DIFFERENT buildings standing flush keep theirs, which is what stops a block reading as one
            // undifferentiated mass. `room` is null for every non-Building tile, so SameRoomAt is false there
            // and the wall ring keeps every face it ever had.
            //
            // WHICH OF THE TWO THE EYE ACTUALLY SEES (worth knowing before retuning either): the EAST strip is
            // the visible one. A same-room south neighbour is drawn LATER (larger j = nearer = later in
            // DrawOrder) and its own front+top together cover this cell's whole front band, so the south face
            // was already invisible in that case — suppressing it is correctness and cost, not pixels. The
            // east strip is NOT covered: the east neighbour's front starts at +0.48·cw while the strip runs
            // from +0.291·cw, so ~0.19·cw of dark shading survives as a vertical bar down the middle of a
            // two-cell-wide house. That bar is what made one wide building read as two narrow ones.
            //
            // Both of those arguments depend on every cell of a footprint resolving to the SAME room, which is
            // what RebuildCellRooms now guarantees — until it did, non-representative cells fell back to
            // BuildingHeightMin and a building was not even flat-topped.
            bool joinSouth = SameRoomAt(room, i, j + 1);
            bool joinEast = SameRoomAt(room, i + 1, j);
            float ew = dw * eastFaceFraction;
            bool drawFront = volume && !joinSouth;
            bool drawEast = volume && !joinEast && ew > 0.5f;

            // Top face (also the whole visual for a flat tile). NEVER suppressed — it is the roof, and a
            // footprint's roof is the continuous surface that makes it read as one building.
            v.TopRt.sizeDelta = new Vector2(dw, dh);
            v.TopRt.anchoredPosition = new Vector2(0f, h);
            Paint(v.Top, volume ? sprites.RoofFor(type) : sprites.Resolve(sprites.GroundFor(type)), face);

            if (drawFront)
            {
                v.FrontRt.sizeDelta = new Vector2(dw, h);
                v.FrontRt.anchoredPosition = new Vector2(0f, -ch * 0.5f + h * 0.5f);
                Paint(v.Front, sprites.Resolve(sprites.wallSprite), Shade(face, frontShade));
            }

            if (drawEast)
            {
                v.EastRt.sizeDelta = new Vector2(ew, h);
                v.EastRt.anchoredPosition = new Vector2(dw * 0.5f - ew * 0.5f, -ch * 0.5f + h * 0.5f);
                Paint(v.East, sprites.Resolve(sprites.wallSprite), Shade(face, eastShade));
            }

            if (volume)
            {
                // The drop shadow is NOT suppressed for an interior cell. It is a flat ground quad offset
                // down-RIGHT, i.e. onto the (i+1, j+1) cell, which is painted LATER and therefore covers it
                // whenever that neighbour is part of the same building — so an interior cell's shadow is
                // already invisible, and gating it on the DIAGONAL neighbour would add a third lookup to buy
                // nothing. Only the footprint's south/east boundary cells cast a shadow anyone can see.
                v.ShadowRt.sizeDelta = new Vector2(dw * ShadowScale, dh * ShadowScale);
                v.ShadowRt.anchoredPosition = new Vector2(cw * ShadowOffsetCells, -ch * ShadowOffsetCells);
                Paint(v.Shadow, sprites.Resolve(sprites.shadowSprite), shadowColor, tintSprite: true);
            }
            v.FrontRt.gameObject.SetActive(drawFront);
            v.EastRt.gameObject.SetActive(drawEast);
            v.ShadowRt.gameObject.SetActive(volume);

            // Marks: ACTIVE buildings only, exactly as the schematic gated them (settlement + TypeId 1 + not
            // a dummy), and now ONCE PER BUILDING rather than once per cell — on the footprint's
            // SettlementFootprint.Representative cell (smallest j, then smallest i, i.e. its back-left cell,
            // whose roof is left all but fully visible by the cells in front of it). A six-cell house wearing
            // six has-image dots would read as six houses, which is the very confusion this task exists to
            // remove. Sized off the cell so they scale with the view instead of pinning to a magic pixel
            // count at every zoom.
            bool markable = isSettlement && type == TileType.Building && room != null && room.TypeId == 1 && !dummy
                         && IsRepresentativeCell(room, i, j);
            float markPx = Mathf.Max(3f, cw * 0.26f);
            SetMark(v.PreviewMark, markable && room.Preview != null, markPx);
            SetMark(v.InteriorMark, markable && RoomsWithInterior != null && RoomsWithInterior.Contains(room.Id), markPx);

            // The selection outline stays PER CELL, unlike the marks. Every cell of the selected building now
            // carries v.RoomId (that is the whole point of the keying fix), so selecting a multi-cell house
            // outlines its whole footprint rather than one cell of it — the honest reading of "this is
            // selected". Cost: Outline duplicates each top face offset by 2px, so the internal cell seams of a
            // selected building show a faint doubling. Left as-is for the Task 9 checkpoint rather than
            // redesigned here — outlining only the footprint's BOUNDARY needs per-edge control the Outline
            // component does not have, and that is a selection-visuals decision, not this task's.
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
