using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Flat (top-down) schematic renderer — the Граф view. Draw-only: DungeonViewController owns every
    /// interaction (spec R5). Layers are built in draw order — LinesLayer, JunctionsLayer, NodesLayer
    /// (later siblings render on top), so segments sit behind junction diamonds, which sit behind cards.
    /// The controller's own hit-plate lives above them all.
    ///
    /// Every pixel — card SIZE and card POSITION alike — now comes from Projection (SquashY 1.0). The old
    /// DungeonGraphView sized cards at a fixed 14px/tile while positioning them across the rect at
    /// ~29px/tile, so rooms drew at half their true footprint and "flush" cascade neighbours showed a
    /// ~45px gap (spec B2); and it mapped X/Y to width/height independently, making the square 48×48 tile
    /// field anisotropic (spec B3). One PxPerTile for both axes and both concerns kills both.
    /// </summary>
    public class DungeonFlatRenderer : MonoBehaviour, IDungeonRenderer
    {
        public DungeonProjection Projection { get; private set; }
        public RectTransform Area => (RectTransform)transform;
        public GameObject Host => gameObject;

        InteriorProfile profile;

        RectTransform linesLayer, junctionsLayer, nodesLayer, contourLayer, wallsLayer, townWallLayer;
        readonly Dictionary<int, Outline> outlines = new Dictionary<int, Outline>();
        // Out-of-contour red flag (C2' — Building only). Separate from `outlines` (selection highlight):
        // a room can be selected AND flagged at once, and the two must toggle independently.
        readonly Dictionary<int, Outline> violationOutlines = new Dictionary<int, Outline>();
        readonly Dictionary<int, RectTransform> cards = new Dictionary<int, RectTransform>();
        readonly List<RectTransform> lineRects = new List<RectTransform>();
        readonly List<RectTransform> junctionRects = new List<RectTransform>();
        readonly List<RectTransform> contourEdges = new List<RectTransform>();
        readonly List<RectTransform> wallRects = new List<RectTransform>();
        readonly List<(Vector2 a, Vector2 b)> wallSegBuf = new List<(Vector2, Vector2)>();
        readonly List<float> wallGapBuf = new List<float>();
        // Settlement wall (Ц1, Task 10) — its OWN pool, independent of wallRects (Building room-outline
        // walls) above. Gates are draggable (DungeonViewController.OnDrag has no TypeId/Kind guard for a
        // settlement), so the gap position shifts live; RebuildTownWall recomputes every reposition and
        // SyncPool reconciles the segment count, same cadence as RebuildWalls.
        readonly List<RectTransform> townWallRects = new List<RectTransform>();
        readonly List<(Vector2 a, Vector2 b)> townWallSegBuf = new List<(Vector2, Vector2)>();
        readonly List<float> townWallGapBuf = new List<float>();
        readonly List<Vector2> gateTileBuf = new List<Vector2>();
        bool built;

        // Last drawn level+graph, cached so SetProjection (pan/zoom) can repaint without the controller
        // having to hand them back. Both renderers keep this pair for the same reason.
        InteriorFloor lastLvl;
        RenderGraph lastRg = new RenderGraph();

        // The Building floor-0 contour (spec C6): traces floor 0's FOOTPRINT SHAPE (union of its room rects
        // + a small margin), NOT a bounding rectangle — so an L/T-shaped building reads as its real shape.
        // The SAME reference on every floor. `contourSegs` (tile-space outline edges) is recomputed once per
        // RebuildView (not per drag/cascade frame — matches the "fit once per bind" cadence); `contourFloor`
        // is floor 0 itself, used LIVE by the per-room red-flag test (a floor-0 room is always inside its own
        // footprint, so it never self-flags; upper-floor rooms flag when they poke outside floor 0's shape).
        // `hasContour` gates BOTH the contour outline and the per-room red flag; strictly false for dungeons
        // so their card hierarchy stays untouched.
        bool hasContour;
        InteriorFloor contourFloor;
        readonly List<(float x0, float y0, float x1, float y1)> contourSegs = new List<(float, float, float, float)>();

        // Settlement-only (Ц1, Task 10): true iff the bound interior is Kind==Settlement. Gates the wall
        // layer entirely so dungeon/building rendering is byte-for-byte unaffected — see hasContour above
        // for the same "cheap bool gate, set false before the null-lvl guard" idiom.
        bool isSettlement;

        // Has-interior mark (Ц2, Task 5): room ids (settlement BUILDING nodes only) whose own interior
        // already exists on file, e.g. { InteriorOps.RoomsWithInterior(dungeonManager.GetAll(), poiId) }.
        // This is EXTERNAL state — unlike hasContour/isSettlement above (both re-derived from `dungeon`
        // every RebuildView) it cannot be read off anything RebuildView's own parameters carry, so
        // DungeonEditorScreen.Bind sets it directly on this field BEFORE the RebuildView chain fires, the
        // same "settable property held by the renderer" role Projection plays for pan/zoom (SetProjection).
        // Deliberately NOT reset inside RebuildView (Projection isn't either) — it must survive until the
        // next external set. Defaults to an empty set so an unset/non-settlement bind never shows the mark.
        public HashSet<int> RoomsWithInterior { get; set; } = new HashSet<int>();

        const float MinCardPx = 20f;      // a 1-tile room must stay a usable click target
        const float LineThickness = 3f;
        const float JunctionPx = 9f;
        const float ContourThickness = 3f;
        const float WallThickness = 5f;     // room-outline wall stroke (bold enough to read at a glance)
        // Opening carved out of a wall at each door, in tiles. Owned by CompactLayout because the PACKER has to
        // read it too (its lateral slide may never leave a pair sharing less wall than the door it draws here).
        const float DoorGapTiles = CompactLayout.DoorGapTiles;
        // Half-width (tiles) of the opening cut into the settlement wall at each gate — one BuildingCell
        // pitch, the same clearance PlaceBuildings already keeps between a building and the wall, so the
        // gap reads as "one building-cell wide" rather than a magic literal. Both operands are const, so
        // this folds to a compile-time constant like DoorGapTiles above.
        const float GateGapHalfTiles = SettlementGenerator.BuildingCell * DungeonLayout.TilesPerAxis * 0.5f;
        static readonly Color ContourColor = new Color(0.45f, 0.75f, 1f, 0.9f);     // provisional — user will tune
        static readonly Color ViolationColor = new Color(0.85f, 0.25f, 0.2f, 0.85f); // provisional — user will tune
        static readonly Color WallColor = new Color(0.04f, 0.04f, 0.06f, 1f);        // near-black, opaque — provisional

        void Awake() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard

            contourLayer = MakeLayer("ContourLayer");       // FIRST → the building outline sits behind everything
            townWallLayer = MakeLayer("TownWallLayer");     // settlement wall — same "backdrop" role as the contour
            linesLayer = MakeLayer("LinesLayer");
            junctionsLayer = MakeLayer("JunctionsLayer");   // AFTER lines → draws on top of segments
            nodesLayer = MakeLayer("NodesLayer");           // AFTER junctions → draws on top
            wallsLayer = MakeLayer("WallsLayer");           // LAST → room-outline walls draw on top of the fills
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

        public bool ResolveProjection(float minX, float minY, float maxX, float maxY)
        {
            var rect = Area.rect;
            if (rect.width <= 0f || rect.height <= 0f) return false;   // not laid out — controller retries
            Projection = DungeonProjection.FitBounds(minX, minY, maxX, maxY, rect.width, rect.height, 1f);
            return true;
        }

        public void SetProjection(DungeonProjection p)
        {
            Projection = p;
            RepositionRooms(lastLvl, lastRg);
        }

        public void RebuildView(InteriorData dungeon, int levelIndex, InteriorFloor lvl, RenderGraph rg, Font font,
                                System.Action<int> onJumpToLevel)
        {
            profile = dungeon != null ? Profiles.ForRoom(dungeon) : Profiles.For(InteriorKind.Dungeon);
            EnsureBuilt();
            DungeonUiKit.ClearLayer(nodesLayer);
            DungeonUiKit.ClearLayer(linesLayer);
            DungeonUiKit.ClearLayer(junctionsLayer);
            DungeonUiKit.ClearLayer(contourLayer);
            DungeonUiKit.ClearLayer(wallsLayer);
            DungeonUiKit.ClearLayer(townWallLayer);
            outlines.Clear(); violationOutlines.Clear(); cards.Clear();
            lineRects.Clear(); junctionRects.Clear(); contourEdges.Clear(); wallRects.Clear();
            townWallRects.Clear();
            contourSegs.Clear(); contourFloor = null;
            lastLvl = lvl; lastRg = rg ?? new RenderGraph();
            hasContour = false;
            isSettlement = false;
            if (lvl == null) return;

            // C6 — Building coherence overlay: the floor-0 FOOTPRINT SHAPE (not a bbox) is the SAME reference
            // on every floor. Strictly gated on Kind==Building — dungeons get neither the contour nor the
            // out-of-contour red flag, and this leaves their card hierarchy untouched.
            hasContour = dungeon != null && dungeon.Kind == InteriorKind.Building && dungeon.Floors.Count > 0;
            if (hasContour)
            {
                contourFloor = dungeon.Floors[0];
                contourSegs.AddRange(FloorFootprint.OutlineSegments(contourFloor, FloorFootprint.ContourMargin));
                for (int i = 0; i < contourSegs.Count; i++) contourEdges.Add(BuildContourEdgeRect());
            }

            // Settlement wall layer (Ц1, Task 10) — mutually exclusive with hasContour (Building): only one
            // of Kind==Building / Kind==Settlement is ever true for a given bind, so the two layers never
            // fight over the same frame.
            isSettlement = dungeon != null && dungeon.Kind == InteriorKind.Settlement;

            foreach (var seg in lastRg.Segments) lineRects.Add(BuildLineRect());
            foreach (var j in lastRg.Junctions) junctionRects.Add(BuildJunctionRect());
            foreach (var r in lvl.Rooms) BuildCard(dungeon, levelIndex, r, font, onJumpToLevel);

            RepositionRooms(lvl, lastRg);
        }

        public void RepositionRooms(InteriorFloor lvl, RenderGraph rg)
        {
            lastLvl = lvl; lastRg = rg ?? new RenderGraph();
            if (lvl == null) return;
            rg = lastRg;

            // The routed graph re-derives on every call and its segment/junction COUNT varies frame to
            // frame — crossings appear and vanish as rooms move, and Fast vs Clean produce different leg
            // counts. The line/junction rects are a POOL sized once at RebuildView; without reconciling it
            // here, a frame with FEWER segments leaves the surplus rects sitting on the PREVIOUS frame's
            // geometry — phantom "doubled"/extra corridors that never clear. Grow the pool to fit and hide
            // the surplus.
            SyncPool(lineRects, rg.Segments.Count, BuildLineRect);
            SyncPool(junctionRects, rg.Junctions.Count, BuildJunctionRect);

            // Reposition every frame (not just on RebuildView) so the red flag tracks a LIVE drag/cascade,
            // not just the position at the last structural rebuild — a room dragged out from under the
            // contour should flag immediately, and clear immediately if dragged back in.
            if (hasContour) RepositionContour();

            foreach (var r in lvl.Rooms)
            {
                if (!cards.TryGetValue(r.Id, out var rt) || rt == null) continue;
                rt.anchoredPosition = Local(r.X * DungeonLayout.TilesPerAxis, r.Y * DungeonLayout.TilesPerAxis);
                rt.sizeDelta = FootprintPx(r);
                if (hasContour && violationOutlines.TryGetValue(r.Id, out var vOutline) && vOutline != null)
                    vOutline.enabled = OutsideContour(r);
            }
            for (int i = 0; i < rg.Segments.Count && i < lineRects.Count; i++)
            {
                var seg = rg.Segments[i];
                PlaceLine(lineRects[i],
                    Local(seg.A.X * DungeonLayout.TilesPerAxis, seg.A.Y * DungeonLayout.TilesPerAxis),
                    Local(seg.B.X * DungeonLayout.TilesPerAxis, seg.B.Y * DungeonLayout.TilesPerAxis),
                    LineThickness);
            }
            for (int i = 0; i < rg.Junctions.Count && i < junctionRects.Count; i++)
            {
                var j = rg.Junctions[i];
                junctionRects[i].anchoredPosition = Local(j.X * DungeonLayout.TilesPerAxis, j.Y * DungeonLayout.TilesPerAxis);
            }

            // Room-outline walls with door openings — recomputed every frame like the corridor pool, so the
            // openings track a live drag/regen. Building only (flush rooms need boundary clarity); dungeons keep
            // their spread-card look untouched.
            if (hasContour) RebuildWalls(lvl, rg);

            // Settlement wall with gate gaps — Settlement only. Recomputed every reposition (gates are
            // draggable rooms, same as any other room) so a gap tracks a live drag, matching RebuildWalls'
            // cadence above. lvl.Wall is null for a HasWall==false config (a wall-less village); skip
            // cleanly rather than throwing — the pool then simply stays at whatever RebuildView left it
            // (empty, since townWallRects.Clear() runs there).
            if (isSettlement && lvl.Wall != null) RebuildTownWall(lvl);
        }

        /// <summary>Draw each room's 4 walls as thin strokes, leaving a <see cref="DoorGapTiles"/>-wide gap where a
        /// door (from the routed link graph) sits on the wall — so a compact building reads as walled rooms with
        /// openings between them, not one colour blob. Pooled like the corridor rects; rebuilt each reposition.</summary>
        void RebuildWalls(InteriorFloor lvl, RenderGraph rg)
        {
            wallSegBuf.Clear();
            int T = DungeonLayout.TilesPerAxis;
            foreach (var r in lvl.Rooms)
            {
                float cx = r.X * T, cy = r.Y * T;
                var (w, h) = DungeonProjection.EffectiveSize(r);
                float hw = w * 0.5f, hh = h * 0.5f;
                float left = cx - hw, right = cx + hw, bottom = cy - hh, top = cy + hh;
                AddWall(rg, T, horizontal: true,  perp: bottom, a0: left,   a1: right);
                AddWall(rg, T, horizontal: true,  perp: top,    a0: left,   a1: right);
                AddWall(rg, T, horizontal: false, perp: left,   a0: bottom, a1: top);
                AddWall(rg, T, horizontal: false, perp: right,  a0: bottom, a1: top);
            }
            SyncPool(wallRects, wallSegBuf.Count, BuildWallRect);
            for (int i = 0; i < wallSegBuf.Count; i++)
                PlaceLine(wallRects[i], wallSegBuf[i].a, wallSegBuf[i].b, WallThickness);
        }

        // Emit the sub-segments of ONE wall (a horizontal wall at fixed Y=perp spanning X in [a0,a1], or a vertical
        // wall at fixed X=perp spanning Y in [a0,a1]), skipping a DoorGapTiles-wide opening at every door lying on
        // that wall line and within its extent. Tile space in; buffered as Local-space endpoints.
        void AddWall(RenderGraph rg, int T, bool horizontal, float perp, float a0, float a1)
        {
            wallGapBuf.Clear();
            foreach (var d in rg.Doors)
            {
                float dx = d.X * T, dy = d.Y * T;
                float dPerp = horizontal ? dy : dx;
                float dAlong = horizontal ? dx : dy;
                if (Mathf.Abs(dPerp - perp) > 0.2f) continue;              // not on this wall's line
                if (dAlong < a0 - 0.05f || dAlong > a1 + 0.05f) continue;  // outside the wall's extent
                wallGapBuf.Add(dAlong);
            }
            wallGapBuf.Sort();
            float cursor = a0, half = DoorGapTiles * 0.5f;
            foreach (var g in wallGapBuf)
            {
                float gStart = Mathf.Max(a0, g - half), gEnd = Mathf.Min(a1, g + half);
                if (gStart > cursor) EmitWallSeg(horizontal, perp, cursor, gStart);
                cursor = Mathf.Max(cursor, gEnd);
            }
            if (cursor < a1) EmitWallSeg(horizontal, perp, cursor, a1);
        }

        void EmitWallSeg(bool horizontal, float perp, float from, float to)
        {
            if (to - from < 0.05f) return;   // opening ate the whole run
            Vector2 p0 = horizontal ? Local(from, perp) : Local(perp, from);
            Vector2 p1 = horizontal ? Local(to, perp) : Local(perp, to);
            wallSegBuf.Add((p0, p1));
        }

        RectTransform BuildWallRect()
        {
            var go = new GameObject("Wall", typeof(RectTransform));
            go.transform.SetParent(wallsLayer, false);
            var img = go.AddComponent<Image>();
            img.color = WallColor;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Settlement wall (Ц1, Task 10): draw <see cref="InteriorFloor.Wall"/> as a closed polyline
        /// (last point connects to first — WallContour's own convention, see Contains/DistanceToEdge), with a
        /// GateGapHalfTiles-wide opening cut wherever a gate room (TypeId 0) sits on it. Same "collect gaps →
        /// sort → emit the remaining runs" shape as AddWall/EmitWallSeg above, generalized to an
        /// arbitrary-direction edge (the wall polygon is not axis-aligned like a room's rectangle) via
        /// dot-product projection instead of raw X/Y comparison. Reuses WallColor/WallThickness — a wall
        /// reads the same regardless of which kind of interior it belongs to.</summary>
        void RebuildTownWall(InteriorFloor lvl)
        {
            townWallSegBuf.Clear();
            var wall = lvl.Wall;
            int n = wall.Points == null ? 0 : wall.Points.Count;
            if (n < 2) { SyncPool(townWallRects, 0, BuildTownWallRect); return; }
            int T = DungeonLayout.TilesPerAxis;

            // Gate positions in TILE space — the gap centres. TypeId 0 is a gate ONLY on a settlement floor,
            // and this method only runs when isSettlement is true.
            gateTileBuf.Clear();
            foreach (var r in lvl.Rooms)
                if (r.TypeId == 0) gateTileBuf.Add(new Vector2(r.X * T, r.Y * T));

            for (int i = 0; i < n; i++)
            {
                var pa = wall.Points[i];
                var pb = wall.Points[(i + 1) % n];   // closes last→first
                AddTownWallEdge(pa.X * T, pa.Y * T, pb.X * T, pb.Y * T);
            }
            SyncPool(townWallRects, townWallSegBuf.Count, BuildTownWallRect);
            for (int i = 0; i < townWallSegBuf.Count; i++)
                PlaceLine(townWallRects[i], townWallSegBuf[i].a, townWallSegBuf[i].b, WallThickness);
        }

        // One edge of the wall polygon (tile space, A→B). Projects every gate onto the edge's own direction
        // (t = 0..1 fraction along it); a gate whose perpendicular distance to the line is ~0 and whose t
        // falls within the edge (with a little slack for float rounding at a shared vertex) opens a gap
        // there. Two adjacent edges meeting exactly at a gate vertex each cut their own half-gap — the two
        // halves sum to one full-width opening centred on the vertex, so the vertex case self-resolves.
        void AddTownWallEdge(float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float len = Mathf.Sqrt(len2);
            if (len < 1e-4f) return;   // degenerate edge (two coincident wall points) — nothing to draw

            townWallGapBuf.Clear();
            foreach (var g in gateTileBuf)
            {
                float t = ((g.x - ax) * dx + (g.y - ay) * dy) / len2;
                float px = ax + t * dx, py = ay + t * dy;
                float perpDist = Mathf.Sqrt((g.x - px) * (g.x - px) + (g.y - py) * (g.y - py));
                if (perpDist > 0.5f) continue;            // not on this edge's line
                if (t < -0.02f || t > 1.02f) continue;    // outside this edge's extent
                townWallGapBuf.Add(Mathf.Clamp01(t) * len);   // arc-length position along the edge
            }
            townWallGapBuf.Sort();

            float cursor = 0f;
            foreach (var gAlong in townWallGapBuf)
            {
                float gStart = Mathf.Max(0f, gAlong - GateGapHalfTiles), gEnd = Mathf.Min(len, gAlong + GateGapHalfTiles);
                if (gStart > cursor) EmitTownWallSeg(ax, ay, dx, dy, len, cursor, gStart);
                cursor = Mathf.Max(cursor, gEnd);
            }
            if (cursor < len) EmitTownWallSeg(ax, ay, dx, dy, len, cursor, len);
        }

        void EmitTownWallSeg(float ax, float ay, float dx, float dy, float len, float from, float to)
        {
            if (to - from < 0.05f) return;   // opening ate the whole run
            float invLen = 1f / len;
            Vector2 p0 = Local(ax + dx * (from * invLen), ay + dy * (from * invLen));
            Vector2 p1 = Local(ax + dx * (to * invLen), ay + dy * (to * invLen));
            townWallSegBuf.Add((p0, p1));
        }

        RectTransform BuildTownWallRect()
        {
            var go = new GameObject("TownWall", typeof(RectTransform));
            go.transform.SetParent(townWallLayer, false);
            var img = go.AddComponent<Image>();
            img.color = WallColor;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Grow `pool` to at least `want` rects (via `make`) and hide any surplus, so exactly the
        /// current `want` rects are visible. The pool only grows — a full RebuildView resets it.</summary>
        static void SyncPool(List<RectTransform> pool, int want, System.Func<RectTransform> make)
        {
            while (pool.Count < want) pool.Add(make());
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) pool[i].gameObject.SetActive(i < want);
        }

        public void SetHighlight(int roomId, bool on)
        {
            if (roomId == 0)   // interface contract: 0 = clear every highlight
            {
                foreach (var o in outlines.Values) if (o != null) o.enabled = false;
                return;
            }
            if (outlines.TryGetValue(roomId, out var outline) && outline != null) outline.enabled = on;
        }

        Vector2 Local(float tx, float ty)
        {
            var (lx, ly) = Projection.TileToLocal(tx, ty);
            return new Vector2(lx, ly);
        }

        /// <summary>Card size in px straight from the projection — the SAME PxPerTile that positions it.
        /// MinCardPx floors a 1-tile room to a still-clickable size.</summary>
        Vector2 FootprintPx(Room r)
        {
            var (w, h) = DungeonProjection.EffectiveSize(r);
            return new Vector2(
                Mathf.Max(MinCardPx, w * Projection.PxPerTile),
                Mathf.Max(MinCardPx, h * Projection.PxPerTile * Projection.SquashY));
        }

        void BuildCard(InteriorData dungeon, int levelIndex, Room r, Font font, System.Action<int> onJumpToLevel)
        {
            var go = new GameObject($"Room_{r.Id}", typeof(RectTransform));
            go.transform.SetParent(nodesLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);   // centre-anchored; position via anchoredPosition
            rt.pivot = new Vector2(0.5f, 0.5f);

            // Dummy building (Ц1.5, Task 5): a settlement building placed purely as decorative filler
            // (SettlementGenerator marks the tail of its building list IsDummy once ActiveBuildings is
            // exhausted). Gated on Kind==Settlement (isSettlement) so dungeon/building cards — where
            // IsDummy is never set — are byte-for-byte unaffected; TypeId==1 excludes a gate (TypeId 0 is
            // never a dummy per Room.IsDummy's own doc comment, but the check costs nothing extra here).
            bool isDummyBuilding = isSettlement && r.IsDummy && r.TypeId == 1;

            var img = go.AddComponent<Image>();
            // ThemeRole.Dot — the SAME "free, neutral" role BuildPreviewIndicator uses below, and the
            // dimmest neutral role in the palette that still reads dimmer than RoomCommon in BOTH themes
            // (checked against Bg/Panel/Panel2/Elev/Border/Mut: several of those actually paint BRIGHTER
            // than RoomCommon in the Light palette, e.g. Panel2 #FBF8F1 vs RoomCommon #EDE3D0). Dot sits
            // low-luminance under RoomCommon in Dark (#2A2A33 vs #3A3128) and Light (#D3CCBB vs #EDE3D0)
            // alike, so a dummy recedes into the backdrop instead of reading as a place, regardless of theme.
            ThemeService.Tag(img, isDummyBuilding ? ThemeRole.Dot : TypeRole(r.TypeId));
            img.raycastTarget = false;   // the CONTROLLER hit-tests in tile space — cards must not eat clicks

            // Gate shape cue (Ц1, Task 10): TypeId 0 already paints via profile.TypeOf(0).Role == Accent —
            // SettlementProfile wired the "Ворота" role in Task 6, generically, through the SAME TypeRole()
            // call every other room type goes through. That colour alone is the distinguishing cue; no extra
            // notch/marker shape was added (the brief allows it, and a plain accent card is the cheaper option
            // for Ц1 — revisit only if the DM checkpoint finds gates hard to spot among buildings).

            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;
            outlines[r.Id] = outline;

            // Out-of-contour red flag (C2', Building only) — a second, independently-toggled Outline so a
            // flagged room still shows the accent selection outline too if selected. Dungeon cards never
            // get this component: `hasContour` is false for the whole RebuildView, keeping their hierarchy
            // exactly as before.
            if (hasContour)
            {
                var violationOutline = go.AddComponent<Outline>();
                violationOutline.effectColor = ViolationColor;
                violationOutline.effectDistance = new Vector2(3f, -3f);
                violationOutline.enabled = false;
                violationOutlines[r.Id] = violationOutline;
            }

            // No number/name label for a dummy — it is unlabelled filler, not an addressable place (brief's
            // "leave the label empty / skip the label build for this card"). Skip the Text object entirely
            // rather than building it with empty content: `lbl` isn't referenced past this block (nothing
            // caches it per-room like `cards`/`outlines` do), so there is nothing else to keep in sync.
            if (!isDummyBuilding)
            {
                var lbl = DungeonUiKit.MakeText(go.transform, font, NodeLabel(r), 11, LabelRole(r.TypeId),
                                                FontStyle.Bold, TextAnchor.MiddleCenter);
                DungeonUiKit.Stretch(lbl.rectTransform);
                lbl.raycastTarget = false;
            }

            DungeonBadgeStrip.Build(go.transform, dungeon, profile.FloorLinks, levelIndex, r, font, onJumpToLevel);

            // Has-image indicator (Ц1, Task 10): a building card whose Room.Preview is non-null gets a tiny
            // corner marker — NOT the image itself (a 512px preview per building is exactly the bloat the
            // size-bounded import was designed to avoid; it opens full-size in Ц2). Built as a CHILD of the
            // card, like the label/badge strip above, so it inherits the card's position/lifecycle for free
            // and needs no separate SyncPool-style bookkeeping: Room.Preview is read-only world definition
            // that cannot change mid-drag/cascade, so unlike the corridor/wall pools (whose element COUNT is
            // re-derived from live routing every reposition) this is a one-time decision made when the card
            // is built. It is "pooled and deactivated" in exactly the sense the file's own outlines/
            // violationOutlines already use: added once per card, then only .enabled/SetActive toggles — and
            // it is destroyed with the rest of the card on the next RebuildView's ClearLayer(nodesLayer), so
            // switching settlements can never leave a ghost indicator behind.
            // !isDummyBuilding is redundant with r.Preview being always-null on a dummy (SettlementGenerator
            // never assigns one), but the brief calls for an explicit skip here for clarity/robustness —
            // a dummy must never grow the indicator even if something upstream ever stamped a stray preview.
            if (isSettlement && r.TypeId == 1 && !isDummyBuilding)
            {
                var indicator = BuildPreviewIndicator(go.transform);
                indicator.SetActive(r.Preview != null);
            }

            // Has-interior mark (Ц2, Task 5): a SECOND, independently-toggled corner mark — top-LEFT so it
            // can never be mistaken for the has-image indicator above (top-right), and ThemeRole.Accent (not
            // Dot) so it reads as "has content" rather than "neutral/free" like Dot's own doc-comment says
            // of itself. Same gate shape as the indicator above (isSettlement/TypeId==1/!isDummyBuilding);
            // the extra, independent condition is membership in the externally-fed RoomsWithInterior set
            // rather than a field read straight off `r`. Built/toggled with the exact same "always add the
            // child, SetActive the visibility" idiom, so it shares the card's lifecycle for free too.
            if (isSettlement && r.TypeId == 1 && !isDummyBuilding)
            {
                var interiorMark = BuildInteriorMark(go.transform);
                interiorMark.SetActive(RoomsWithInterior.Contains(r.Id));
            }

            cards[r.Id] = rt;
        }

        const float PreviewIndicatorPx = 8f;   // a few-pixel corner mark — deliberately small, not a thumbnail

        /// <summary>Small filled square pinned to a building card's top-right corner, flagging "this room has
        /// an authored preview image". ThemeRole.Dot was a free, neutral role meaning neither "selected"
        /// (Accent) nor "problem" (Danger) when this indicator claimed it — Task 5 reuses it for the dummy-
        /// building fill above (same "neutral, de-emphasized" intent, just a card fill instead of a marker),
        /// so it is no longer this method's alone but the two never collide (a dummy never gets an indicator).</summary>
        GameObject BuildPreviewIndicator(Transform parent)
        {
            var go = new GameObject("PreviewIndicator", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(PreviewIndicatorPx, PreviewIndicatorPx);
            rt.anchoredPosition = new Vector2(-2f, -2f);

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Dot);
            img.raycastTarget = false;
            return go;
        }

        /// <summary>Small filled square pinned to a building card's top-LEFT corner, flagging "this room's
        /// own interior already exists on file" (Ц2, Task 5) — deliberately the OPPOSITE corner from
        /// BuildPreviewIndicator above so the two marks can never be mistaken for one another when both show
        /// at once. ThemeRole.Accent, not Dot: Dot is this file's "neutral/free" role (already claimed by
        /// the has-image indicator and the dummy-building fill); Accent is the file's own "this is notable"
        /// role (already used for the selection outline and the TypeId==0 Gate fill) and reads with strong
        /// contrast against RoomCommon in BOTH themes, which AccentSoft does not (near-invisible against
        /// RoomCommon in Dark — #2B2617 on #3A3128). No collision with the Gate fill: this mark only ever
        /// shows on a TypeId==1 Building card, never on a TypeId==0 Gate card.</summary>
        GameObject BuildInteriorMark(Transform parent)
        {
            var go = new GameObject("InteriorMark", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(PreviewIndicatorPx, PreviewIndicatorPx);
            rt.anchoredPosition = new Vector2(2f, -2f);

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent);
            img.raycastTarget = false;
            return go;
        }

        RectTransform BuildLineRect()
        {
            var go = new GameObject("Segment", typeof(RectTransform));
            go.transform.SetParent(linesLayer, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Mut);
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>One edge of the floor-0 footprint outline (C6, Building only) — a fixed light-blue
        /// colour, NOT ThemeService-tagged, so it does not repaint on a Dark/Light theme switch (spec's
        /// colours are provisional and theme-independent for now). Draw-only, never selectable.</summary>
        RectTransform BuildContourEdgeRect()
        {
            var go = new GameObject("ContourEdge", typeof(RectTransform));
            go.transform.SetParent(contourLayer, false);
            var img = go.AddComponent<Image>();
            img.color = ContourColor;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Re-place the cached footprint-outline edge rects (one per <see cref="contourSegs"/>
        /// segment) through the CURRENT Projection. Runs every RepositionRooms call (not just RebuildView) so
        /// the contour tracks a future pan/zoom (Task 5) the same way cards do; the segments themselves only
        /// change on the next RebuildView, matching the "fit once per bind" cadence.</summary>
        void RepositionContour()
        {
            for (int i = 0; i < contourSegs.Count && i < contourEdges.Count; i++)
            {
                var s = contourSegs[i];
                PlaceLine(contourEdges[i], Local(s.x0, s.y0), Local(s.x1, s.y1), ContourThickness);
            }
        }

        /// <summary>True if room `r`'s tile-space footprint is not FULLY inside floor 0's footprint SHAPE
        /// (union of its room rects + margin) — i.e. it pokes outside the drawn contour and should be red-
        /// flagged (spec C6: "change or remove me"). A floor-0 room is part of that footprint, so it never
        /// self-flags; upper-floor rooms flag when they extend past floor 0's shape. Footprint via
        /// EffectiveSize, same as the contour/HitTest use.</summary>
        bool OutsideContour(Room r)
        {
            if (contourFloor == null) return false;
            float cx = r.X * DungeonLayout.TilesPerAxis;
            float cy = r.Y * DungeonLayout.TilesPerAxis;
            var (w, h) = DungeonProjection.EffectiveSize(r);
            return !FloorFootprint.ContainsRect(contourFloor, FloorFootprint.ContourMargin, cx, cy, w, h);
        }

        /// <summary>Draw-only crossing marker: a small diamond (square rotated 45°). Never selectable, no
        /// Button/EventTrigger, raycastTarget off — junctions are DERIVED and must never reach selection,
        /// ops or the validator.</summary>
        RectTransform BuildJunctionRect()
        {
            var go = new GameObject("Junction", typeof(RectTransform));
            go.transform.SetParent(junctionsLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(JunctionPx, JunctionPx);
            rt.localEulerAngles = new Vector3(0f, 0f, 45f);

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Txt);
            img.raycastTarget = false;
            return rt;
        }

        static void PlaceLine(RectTransform lineRect, Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 mid = (p0 + p1) * 0.5f;
            Vector2 dir = p1 - p0;
            float len = dir.magnitude;
            lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = new Vector2(len, thickness);
            lineRect.anchoredPosition = mid;
            lineRect.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // t is Room.TypeId: profile.TypeOf(t) clamps out-of-range ids to index 0 (never throws).
        ThemeRole TypeRole(int t) => profile.TypeOf(t).Role;

        ThemeRole LabelRole(int t) => profile.TypeOf(t).LabelRole;

        // CardLabel, NOT Label: the untitled-room card fallback. For a dungeon Normal room this is "Комната"
        // (matching the pre-profile TypeLabel default), while the inspector's picker button uses Label "Обычная".
        string TypeLabel(int t) => profile.TypeOf(t).CardLabel;

        string NodeLabel(Room r) => $"{r.Id}. {(string.IsNullOrEmpty(r.Title) ? TypeLabel(r.TypeId) : r.Title)}";
    }
}
