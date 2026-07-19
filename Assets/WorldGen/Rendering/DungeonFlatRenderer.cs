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

        RectTransform linesLayer, junctionsLayer, nodesLayer, contourLayer;
        readonly Dictionary<int, Outline> outlines = new Dictionary<int, Outline>();
        // Out-of-contour red flag (C2' — Building only). Separate from `outlines` (selection highlight):
        // a room can be selected AND flagged at once, and the two must toggle independently.
        readonly Dictionary<int, Outline> violationOutlines = new Dictionary<int, Outline>();
        readonly Dictionary<int, RectTransform> cards = new Dictionary<int, RectTransform>();
        readonly List<RectTransform> lineRects = new List<RectTransform>();
        readonly List<RectTransform> junctionRects = new List<RectTransform>();
        readonly List<RectTransform> contourEdges = new List<RectTransform>();
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

        const float MinCardPx = 20f;      // a 1-tile room must stay a usable click target
        const float LineThickness = 3f;
        const float JunctionPx = 9f;
        const float ContourThickness = 3f;
        static readonly Color ContourColor = new Color(0.45f, 0.75f, 1f, 0.9f);     // provisional — user will tune
        static readonly Color ViolationColor = new Color(0.85f, 0.25f, 0.2f, 0.85f); // provisional — user will tune

        void Awake() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard

            contourLayer = MakeLayer("ContourLayer");       // FIRST → the building outline sits behind everything
            linesLayer = MakeLayer("LinesLayer");
            junctionsLayer = MakeLayer("JunctionsLayer");   // AFTER lines → draws on top of segments
            nodesLayer = MakeLayer("NodesLayer");           // AFTER junctions → draws on top
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
            outlines.Clear(); violationOutlines.Clear(); cards.Clear();
            lineRects.Clear(); junctionRects.Clear(); contourEdges.Clear();
            contourSegs.Clear(); contourFloor = null;
            lastLvl = lvl; lastRg = rg ?? new RenderGraph();
            hasContour = false;
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

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, TypeRole(r.TypeId));
            img.raycastTarget = false;   // the CONTROLLER hit-tests in tile space — cards must not eat clicks

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

            var lbl = DungeonUiKit.MakeText(go.transform, font, NodeLabel(r), 11, LabelRole(r.TypeId),
                                            FontStyle.Bold, TextAnchor.MiddleCenter);
            DungeonUiKit.Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;

            DungeonBadgeStrip.Build(go.transform, dungeon, profile.FloorLinks, levelIndex, r, font, onJumpToLevel);
            cards[r.Id] = rt;
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
