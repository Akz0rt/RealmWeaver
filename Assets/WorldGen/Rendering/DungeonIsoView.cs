using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// View-only 2D screen-space isometric render of one dungeon floor (Task 5, sub-project 3). Hosted
    /// under DungeonEditorScreen.IsoArea (same bounds as MapArea) behind the Граф/Изо toggle.
    ///
    /// A single custom UGUI Graphic (this class IS the Graphic — subclasses UnityEngine.UI.Graphic and
    /// overrides OnPopulateMesh) that builds ONE mesh of every iso shape (room floor diamonds, wall
    /// parallelograms, passage strips, junction diamonds) with flat per-vertex colors. A mesh (not
    /// Image/UGUI rects) is the only way to draw arbitrary iso quads in screen space; it depth-sorts
    /// naturally via triangle emission order and sets up the later tile atlas (swap flat colors for UVs
    /// in the small Add* helpers below — the single swap point for real art).
    ///
    /// Geometry pipeline per Refresh/OnPopulateMesh: gather every shape into a flat list tagged with an
    /// IsoProjection.DepthKey (painter's order — farthest/lowest first), sort ascending, THEN fit the
    /// combined screen-space bounds of every shape into this view's rect (centered, uniformly scaled),
    /// and finally emit each shape's quad in sorted order so nearer/taller shapes draw over farther ones.
    ///
    /// NEVER read this.rectTransform.rect at Bind time — the host screen can Bind before the screen is
    /// activated, when rect=={0,0} (same gotcha as DungeonGraphView; see that class's doc comment).
    /// Refresh() guards this and defers to the first valid-rect LateUpdate, mirroring
    /// DungeonGraphView.needsInitialRelayout.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class DungeonIsoView : Graphic
    {
        public System.Action<int> OnRoomSelected;   // click-to-select wiring is Task 6 — field only, unwired here

        DungeonData dungeon;
        int levelIndex;
        Font font;   // unused for mesh drawing today; kept for interface parity + future room-label text (Task 6+)
        bool needsInitialRelayout;

        DungeonLevel BoundLevel =>
            dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Levels.Count
                ? dungeon.Levels[levelIndex] : null;

        // Classic 2:1 iso tile projection size (px). Kept separate from DungeonGraphView.PxPerTile — that
        // constant sizes flat top-down node cards, unrelated to this view's projected diamond geometry.
        const float TileW = 32f;
        const float TileH = 16f;
        const float WallPx = 14f;              // wall extrusion height in screen px
        const float FloorHeight = 0f;          // DepthKey height for floors/passages/junctions
        const float WallHeight = 1f;           // DepthKey height for walls — sorts a wall just above its own floor
        const float PassageHalfWidthTiles = 0.35f;
        const float JunctionSizeTiles = 1.6f;
        const float FitPadding = 0.9f;         // leaves a small margin around the fitted map

        /// <summary>(Re)bind to a level and rebuild. Always re-binds unconditionally (no "same binding"
        /// short-circuit like DungeonGraphView — this view carries no per-binding selection state to
        /// preserve, so a plain Bind-every-time is simplest and guarantees freshness after edits made via
        /// the sidebar inspector while this view is showing).</summary>
        public void Bind(DungeonData dungeon, int levelIndex, Font font)
        {
            this.dungeon = dungeon;
            this.levelIndex = levelIndex;
            this.font = font;
            Refresh();
        }

        /// <summary>Marks the mesh dirty for a rebuild (OnPopulateMesh does the actual geometry work). If
        /// the host rect hasn't laid out yet (rect=={0,0}), defers to the first valid-rect LateUpdate —
        /// same idiom as DungeonGraphView's needsInitialRelayout guard.</summary>
        public void Refresh()
        {
            var rt = rectTransform;
            if (rt == null || rt.rect.width <= 0f || rt.rect.height <= 0f)
            {
                needsInitialRelayout = true;
                return;
            }
            needsInitialRelayout = false;
            SetVerticesDirty();
        }

        void LateUpdate()
        {
            if (!needsInitialRelayout) return;
            var rt = rectTransform;
            if (rt == null || rt.rect.width <= 0f || rt.rect.height <= 0f) return;   // still not laid out — retry next frame
            needsInitialRelayout = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var lvl = BoundLevel;
            if (lvl == null) return;

            var rt = rectTransform;
            if (rt == null) return;
            var rect = rt.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;   // not laid out — Refresh()/LateUpdate retries

            var shapes = new List<IsoShape>();

            foreach (var r in lvl.Rooms)
            {
                float cx = r.X * DungeonLayout.TilesPerAxis;
                float cy = r.Y * DungeonLayout.TilesPerAxis;
                var (w, h) = EffectiveSize(r);
                FootprintCorners(cx, cy, w, h, out var n, out var e, out var s, out var wCorner);

                shapes.Add(new IsoShape(IsoProjection.DepthKey(cx, cy, FloorHeight), n, e, s, wCorner, FloorColor(r.Type)));

                var wallColor = WallColor(r.Type);
                AddWall(shapes, e, s, cx, cy, wallColor);       // +X-facing (east) edge
                AddWall(shapes, s, wCorner, cx, cy, wallColor); // +Y-facing (south) edge
            }

            var rg = DungeonLayout.BuildRenderGraph(lvl);
            foreach (var seg in rg.Segments)
            {
                float ax = seg.A.X * DungeonLayout.TilesPerAxis, ay = seg.A.Y * DungeonLayout.TilesPerAxis;
                float bx = seg.B.X * DungeonLayout.TilesPerAxis, by = seg.B.Y * DungeonLayout.TilesPerAxis;
                AddPassage(shapes, ax, ay, bx, by, PassageColor());
            }
            foreach (var j in rg.Junctions)
            {
                float jx = j.X * DungeonLayout.TilesPerAxis, jy = j.Y * DungeonLayout.TilesPerAxis;
                AddFloorDiamond(shapes, jx, jy, JunctionSizeTiles, JunctionSizeTiles, JunctionColor());
            }

            if (shapes.Count == 0) return;

            // Painter's order: farthest/lowest first (smaller DepthKey) so nearer/taller shapes are added
            // to the mesh LAST and overwrite the ones behind them.
            shapes.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            // Fit-to-host: bounds over every projected point (pre-transform), then center+scale into rect.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var sh in shapes)
            {
                Expand(sh.A, ref minX, ref maxX, ref minY, ref maxY);
                Expand(sh.B, ref minX, ref maxX, ref minY, ref maxY);
                Expand(sh.C, ref minX, ref maxX, ref minY, ref maxY);
                Expand(sh.D, ref minX, ref maxX, ref minY, ref maxY);
            }
            float boundsW = Mathf.Max(1e-3f, maxX - minX);
            float boundsH = Mathf.Max(1e-3f, maxY - minY);
            var boundsCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float scale = Mathf.Min(rect.width / boundsW, rect.height / boundsH) * FitPadding;

            foreach (var sh in shapes)
            {
                vh.AddUIVertexQuad(MakeQuad(
                    (sh.A - boundsCenter) * scale, (sh.B - boundsCenter) * scale,
                    (sh.C - boundsCenter) * scale, (sh.D - boundsCenter) * scale, sh.Color));
            }
        }

        // ── Shape emission helpers (isolated so the real tile atlas later only needs to touch these) ──

        readonly struct IsoShape
        {
            public readonly float Depth;
            public readonly Vector2 A, B, C, D;
            public readonly Color Color;
            public IsoShape(float depth, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            { Depth = depth; A = a; B = b; C = c; D = d; Color = color; }
        }

        /// <summary>Projects one tile-space point to screen space. IsoProjection's sy grows toward the
        /// iso-"down"/near side (see its self-test); Unity's UI local space has +y UP, so sy is negated
        /// here — the single inversion point, same fix DungeonGraphView.PointCenter applies for its own
        /// (grid-Y-down vs rect-Y-up) mismatch.</summary>
        static Vector2 Proj(float tileX, float tileY)
        {
            var (sx, sy) = IsoProjection.ToScreen(tileX, tileY, TileW, TileH);
            return new Vector2(sx, -sy);
        }

        // The 4 corners of a tile-space footprint rectangle, projected to screen space and named by iso
        // screen position: N (top vertex), E (right), S (bottom, nearest/front), W (left).
        static void FootprintCorners(float cx, float cy, float wTiles, float hTiles,
            out Vector2 n, out Vector2 e, out Vector2 s, out Vector2 w)
        {
            float hw = wTiles * 0.5f, hh = hTiles * 0.5f;
            n = Proj(cx - hw, cy - hh);
            e = Proj(cx + hw, cy - hh);
            s = Proj(cx + hw, cy + hh);
            w = Proj(cx - hw, cy + hh);
        }

        static void AddFloorDiamond(List<IsoShape> shapes, float cx, float cy, float wTiles, float hTiles, Color color)
        {
            FootprintCorners(cx, cy, wTiles, hTiles, out var n, out var e, out var s, out var w);
            shapes.Add(new IsoShape(IsoProjection.DepthKey(cx, cy, FloorHeight), n, e, s, w, color));
        }

        // One extruded wall face along a top edge (p0→p1, screen space), darkened `color`. depthCx/depthCy
        // is the OWNING room's tile center (not the edge) so the wall's DepthKey sits right above its own
        // floor's, regardless of how far the edge itself is from the room center.
        static void AddWall(List<IsoShape> shapes, Vector2 p0, Vector2 p1, float depthCx, float depthCy, Color color)
        {
            var down = new Vector2(0f, WallPx);
            Vector2 p0b = p0 - down, p1b = p1 - down;
            float depth = IsoProjection.DepthKey(depthCx, depthCy, WallHeight);
            shapes.Add(new IsoShape(depth, p0, p1, p1b, p0b, color));
        }

        // A thin floor strip between two tile-space points, `PassageHalfWidthTiles` wide, perpendicular
        // computed in TILE space (then each of the 4 corners individually projected) so the strip's width
        // reads correctly under the iso skew rather than looking uniform in raw screen space.
        static void AddPassage(List<IsoShape> shapes, float ax, float ay, float bx, float by, Color color)
        {
            float dx = bx - ax, dy = by - ay;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;   // degenerate (coincident endpoints) — nothing to draw
            float px = -dy / len * PassageHalfWidthTiles, py = dx / len * PassageHalfWidthTiles;
            Vector2 a1 = Proj(ax + px, ay + py), a2 = Proj(ax - px, ay - py);
            Vector2 b1 = Proj(bx + px, by + py), b2 = Proj(bx - px, by - py);
            float depth = IsoProjection.DepthKey((ax + bx) * 0.5f, (ay + by) * 0.5f, FloorHeight);
            shapes.Add(new IsoShape(depth, a1, b1, b2, a2, color));
        }

        static void Expand(Vector2 p, ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        static UIVertex[] MakeQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var quad = new UIVertex[4];
            quad[0] = Vert(a, color);
            quad[1] = Vert(b, color);
            quad[2] = Vert(c, color);
            quad[3] = Vert(d, color);
            return quad;
        }

        static UIVertex Vert(Vector2 p, Color color)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = color;
            return v;
        }

        // ── Placeholder color/size mapping (swap point for the real tile atlas) ─────────────────────────

        static (int w, int h) EffectiveSize(Room r)
        {
            int w = r.SizeW, h = r.SizeH;
            if (w <= 0 || h <= 0)
            {
                var (dw, dh) = RoomSizing.Default(r.Type);
                if (w <= 0) w = dw;
                if (h <= 0) h = dh;
            }
            return (RoomSizing.Clamp(w), RoomSizing.Clamp(h));
        }

        // Matches DungeonGraphView.TypeRole (entrance gold/Accent, boss crimson/Danger, normal stone/Elev).
        static ThemeRole TypeRole(RoomType t) => t switch
        {
            RoomType.Entrance => ThemeRole.Accent,
            RoomType.Boss => ThemeRole.Danger,
            _ => ThemeRole.Elev,
        };

        static Color FloorColor(RoomType t) => Brighten(ThemeService.Get(TypeRole(t)), 0.18f);
        static Color WallColor(RoomType t) => Darken(ThemeService.Get(TypeRole(t)), 0.35f);
        static Color PassageColor() => ThemeService.Get(ThemeRole.Mut);
        static Color JunctionColor() => Brighten(ThemeService.Get(ThemeRole.Mut), 0.15f);

        static Color Brighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);
    }
}
