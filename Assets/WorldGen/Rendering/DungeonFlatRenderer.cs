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

        RectTransform linesLayer, junctionsLayer, nodesLayer;
        readonly Dictionary<int, Outline> outlines = new Dictionary<int, Outline>();
        readonly Dictionary<int, RectTransform> cards = new Dictionary<int, RectTransform>();
        readonly List<RectTransform> lineRects = new List<RectTransform>();
        readonly List<RectTransform> junctionRects = new List<RectTransform>();
        bool built;

        // Last drawn level+graph, cached so SetProjection (pan/zoom) can repaint without the controller
        // having to hand them back. Both renderers keep this pair for the same reason.
        InteriorFloor lastLvl;
        RenderGraph lastRg = new RenderGraph();

        const float MinCardPx = 20f;      // a 1-tile room must stay a usable click target
        const float LineThickness = 3f;
        const float JunctionPx = 9f;

        void Awake() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard

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

        public bool ResolveProjection(InteriorFloor lvl)
        {
            var rect = Area.rect;
            if (rect.width <= 0f || rect.height <= 0f) return false;   // not laid out — controller retries
            Projection = DungeonProjection.Fit(lvl, rect.width, rect.height, 1f);
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
            EnsureBuilt();
            DungeonUiKit.ClearLayer(nodesLayer);
            DungeonUiKit.ClearLayer(linesLayer);
            DungeonUiKit.ClearLayer(junctionsLayer);
            outlines.Clear(); cards.Clear(); lineRects.Clear(); junctionRects.Clear();
            lastLvl = lvl; lastRg = rg ?? new RenderGraph();
            if (lvl == null) return;

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

            foreach (var r in lvl.Rooms)
            {
                if (!cards.TryGetValue(r.Id, out var rt) || rt == null) continue;
                rt.anchoredPosition = Local(r.X * DungeonLayout.TilesPerAxis, r.Y * DungeonLayout.TilesPerAxis);
                rt.sizeDelta = FootprintPx(r);
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

            var lbl = DungeonUiKit.MakeText(go.transform, font, NodeLabel(r), 11, LabelRole(r.TypeId),
                                            FontStyle.Bold, TextAnchor.MiddleCenter);
            DungeonUiKit.Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;

            DungeonBadgeStrip.Build(go.transform, dungeon, levelIndex, r, font, onJumpToLevel);
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

        // t is Room.TypeId: Entrance=0, Normal=1, Boss=2 (see DungeonData.cs).
        static ThemeRole TypeRole(int t) => t switch
        {
            0 => ThemeRole.Accent,
            2 => ThemeRole.Danger,
            _ => ThemeRole.Elev,
        };

        // AccentInk reads on both the Accent and Danger card tints; Normal cards (Elev) use plain Txt.
        static ThemeRole LabelRole(int t) => t == 1 ? ThemeRole.Txt : ThemeRole.AccentInk;

        internal static string TypeLabel(int t) => t switch
        {
            0 => "Вход",
            2 => "Босс",
            _ => "Комната",
        };

        internal static string NodeLabel(Room r) => $"{r.Id}. {(string.IsNullOrEmpty(r.Title) ? TypeLabel(r.TypeId) : r.Title)}";
    }
}
