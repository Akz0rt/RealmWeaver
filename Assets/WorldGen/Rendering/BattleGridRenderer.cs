using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Draws one battle map: cell colours as a 1-pixel-per-cell Texture2D under a pooled UI
    /// overlay for grid lines, derived doors and the cursor highlight. Pixels only — it holds no model
    /// state and mutates nothing; BattleGridViewController owns input and edits.
    ///
    /// Why one pixel per cell: at 40x40 the texture is ~6 KB, so re-uploading all of it per frame costs
    /// nothing and the dirty-rectangle machinery a bigger texture would need disappears. Why the overlay
    /// is UI rather than baked pixels: a 1-px line inside a texture scaled by a non-integer factor comes
    /// out with uneven thickness.
    ///
    /// <see cref="Build"/> and <see cref="SetGrid"/> may be called in either order; <see cref="Repaint"/>
    /// is a no-op until both have happened.</summary>
    public class BattleGridRenderer : MonoBehaviour
    {
        public const float LineThickness = 1f;

        RectTransform host;
        RawImage cellsImage;
        AspectRatioFitter fitter;
        Texture2D texture;
        RectTransform overlay;
        // Set when LayoutOverlay bails out on a not-yet-laid-out rect; cleared once a layout actually runs
        // against a real rect. LateUpdate polls this to retry — see LayoutOverlay for why the rect can be
        // degenerate on the same frame Build/SetGrid/Repaint run.
        bool overlayPending;

        readonly List<Image> linePool = new List<Image>();
        readonly List<Image> doorPool = new List<Image>();
        readonly List<Image> highlightPool = new List<Image>();

        GridBuffer grid;
        List<GridPoint> doors = new List<GridPoint>();
        List<GridPoint> highlight = new List<GridPoint>();

        static readonly Color LineColor      = new Color(0f, 0f, 0f, 0.25f);
        static readonly Color DoorColor      = new Color32(0xC9, 0xA2, 0x4B, 0xFF);
        static readonly Color HighlightColor = new Color(1f, 1f, 1f, 0.35f);

        public static Color ColorFor(GridCell c)
        {
            switch (c)
            {
                case GridCell.Floor:  return new Color32(0xD9, 0xD2, 0xC4, 0xFF);
                case GridCell.Wall:   return new Color32(0x4A, 0x46, 0x40, 0xFF);
                case GridCell.Door:   return new Color32(0xC9, 0xA2, 0x4B, 0xFF);
                case GridCell.Rough:  return new Color32(0xA7, 0x9A, 0x6E, 0xFF);
                case GridCell.Liquid: return new Color32(0x3E, 0x6E, 0x8E, 0xFF);
                case GridCell.Chasm:  return new Color32(0x0B, 0x0B, 0x0F, 0xFF);
                default:              return new Color32(0x14, 0x14, 0x1A, 0xFF);   // Empty
            }
        }

        public void Build(RectTransform hostRect)
        {
            host = hostRect;

            var imgGo = new GameObject("Cells", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            imgGo.transform.SetParent(host, false);
            cellsImage = imgGo.GetComponent<RawImage>();
            // REQUIRED, not Unity's incidental default: BattleGridViewController's pointer handlers sit on
            // an ANCESTOR of this rect, and Unity's event system bubbles a pointer event from the hit
            // graphic up to the nearest ancestor that handles it. If this were ever set false, nothing
            // under the cursor would be hit and dragging would silently stop working. Do not "tidy" this
            // away — every other image in this file is explicitly false for the opposite reason.
            cellsImage.raycastTarget = true;
            fitter = imgGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            var imgRt = imgGo.GetComponent<RectTransform>();
            // Centre anchors, NOT stretch: AspectRatioFitter drives anchorMin/anchorMax to (0.5,0.5) in
            // FitInParent mode, so stretch anchors set here would simply be overwritten and mislead the
            // next reader into thinking this rect fills its parent.
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.pivot = new Vector2(0.5f, 0.5f);

            var ovGo = new GameObject("Overlay", typeof(RectTransform));
            ovGo.transform.SetParent(imgGo.transform, false);
            overlay = ovGo.GetComponent<RectTransform>();
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero; overlay.offsetMax = Vector2.zero;

            // A grid supplied via SetGrid BEFORE Build had nothing to attach a texture to at the time — it
            // only stored `grid`. Apply it now that cellsImage/fitter exist, so call order never matters.
            if (grid != null) AttachTexture();
        }

        public void SetGrid(GridBuffer buffer)
        {
            grid = buffer;
            if (grid == null) return;
            if (cellsImage == null) return;   // Build hasn't run yet; Build's own tail applies this grid when it does
            AttachTexture();
        }

        /// <summary>Create/resize the cells texture for the current `grid` and hand it to `cellsImage`, and
        /// set the fitter's aspect ratio. Shared by Build's tail (grid arrived first) and SetGrid (Build
        /// already ran) so the two call orders end up in the identical state.</summary>
        void AttachTexture()
        {
            if (texture == null || texture.width != grid.Width || texture.height != grid.Height)
            {
                if (texture != null) Destroy(texture);
                texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                cellsImage.texture = texture;
            }
            fitter.aspectRatio = (float)grid.Width / grid.Height;
        }

        public void SetDoors(List<GridPoint> value) => doors = value ?? new List<GridPoint>();
        public void SetHighlight(List<GridPoint> value) => highlight = value ?? new List<GridPoint>();

        /// <summary>Push the whole grid to the texture and re-lay the overlay. Cheap enough to call on
        /// every change; there is no incremental path on purpose.</summary>
        public void Repaint()
        {
            if (grid == null || texture == null) return;

            var px = new Color32[grid.Cells.Length];
            for (int i = 0; i < grid.Cells.Length; i++) px[i] = ColorFor(grid.Cells[i]);
            texture.SetPixels32(px);
            texture.Apply(false);

            LayoutOverlay();
        }

        void LayoutOverlay()
        {
            var rect = ((RectTransform)cellsImage.transform).rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                // AspectRatioFitter is an ILayoutSelfController: it sizes this rect during Unity's canvas
                // REBUILD phase, not synchronously when Build/SetGrid run. A host doing
                // Build(); SetGrid(g); Repaint(); in one frame — the natural sequence — can call Repaint
                // before that rebuild has happened, so the rect is still {0,0} (or stale) here. Bail and
                // let LateUpdate retry, same precedent as DungeonFlatRenderer.ResolveProjection /
                // DungeonViewController.LateUpdate.
                overlayPending = true;
                return;
            }
            overlayPending = false;

            float cw = rect.width / grid.Width;
            float ch = rect.height / grid.Height;
            float x0 = -rect.width * 0.5f;
            float y0 = -rect.height * 0.5f;

            int lineIdx = 0;
            for (int x = 0; x <= grid.Width; x++)
                PlaceLine(ref lineIdx, x0 + x * cw - LineThickness * 0.5f, y0, LineThickness, rect.height);
            for (int y = 0; y <= grid.Height; y++)
                PlaceLine(ref lineIdx, x0, y0 + y * ch - LineThickness * 0.5f, rect.width, LineThickness);
            for (int i = lineIdx; i < linePool.Count; i++) linePool[i].gameObject.SetActive(false);

            SyncDoorMarkers(doorPool, doors, DoorColor, cw, ch, x0, y0);
            SyncHighlightMarkers(highlightPool, highlight, HighlightColor, cw, ch, x0, y0);
        }

        /// <summary>Retries a layout that <see cref="LayoutOverlay"/> deferred because the Cells rect was
        /// not yet sized by AspectRatioFitter's canvas-rebuild pass. Self-terminating: LayoutOverlay clears
        /// `overlayPending` the moment it runs against a real rect, so this stops calling Repaint the very
        /// next frame after that — it does not repaint forever.</summary>
        void LateUpdate()
        {
            if (overlayPending && grid != null) Repaint();
        }

        void PlaceLine(ref int idx, float x, float y, float w, float h)
        {
            var img = Take(linePool, LineColor, idx);
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = new Vector2(x + w * 0.5f, y + h * 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            idx++;
        }

        // Fraction of the cell's across-the-wall dimension a door bar fills. Thin on purpose: a full-width
        // bar would read the same as a hand-painted GridCell.Door square from a middle distance.
        const float DoorBarFill = 0.42f;

        // A derived door mark is a BAR across the wall cell, not a centred square — a hand-painted
        // GridCell.Door cell already renders as a filled square (ColorFor), so a derived door must be
        // visually distinct in SHAPE, not just a smaller copy of the same shape. GridPoint carries no wall
        // orientation, but it doesn't need to: a derived door always sits on the grid's perimeter
        // (BattleGridGenerator.ProjectDoors clamps the along-wall coordinate so a door never lands on a
        // corner), so which wall it's on is implied by X/Y alone. A vertical wall's bar spans the cell's
        // FULL height and is thin across; a horizontal wall's bar is the mirror image. Anything that
        // somehow is not on the perimeter (defensive only — should not happen) falls back to the full cell.
        void SyncDoorMarkers(List<Image> pool, List<GridPoint> points, Color color,
                             float cw, float ch, float x0, float y0)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var img = Take(pool, color, i);
                var rt = (RectTransform)img.transform;
                Vector2 size;
                if (p.X == 0 || p.X == grid.Width - 1)
                    size = new Vector2(cw * DoorBarFill, ch);
                else if (p.Y == 0 || p.Y == grid.Height - 1)
                    size = new Vector2(cw, ch * DoorBarFill);
                else
                    size = new Vector2(cw, ch);   // defensive fallback — not on the perimeter
                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(x0 + (p.X + 0.5f) * cw, y0 + (p.Y + 0.5f) * ch);
            }
            for (int i = points.Count; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }

        // The cursor/selection highlight fills the whole cell — unlike a door mark it has no shape
        // constraint to satisfy, just visibility.
        void SyncHighlightMarkers(List<Image> pool, List<GridPoint> points, Color color,
                                  float cw, float ch, float x0, float y0)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var img = Take(pool, color, i);
                var rt = (RectTransform)img.transform;
                rt.sizeDelta = new Vector2(cw, ch);
                rt.anchoredPosition = new Vector2(x0 + (points[i].X + 0.5f) * cw,
                                                  y0 + (points[i].Y + 0.5f) * ch);
            }
            for (int i = points.Count; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }

        Image Take(List<Image> pool, Color color, int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Mark", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(overlay, false);
                var im = go.GetComponent<Image>();
                im.raycastTarget = false;                 // input belongs to the controller, not the overlay
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                pool.Add(im);
            }
            var img = pool[index];
            img.color = color;
            img.gameObject.SetActive(true);
            return img;
        }

        /// <summary>Screen point → cell. No flip needed: grid y=0 is the bottom row (GridBuffer) and UI
        /// local Y also grows up, so they already agree — the only work here is the origin shift from
        /// centre-relative local coords to a 0-based cell index. Boundary convention is a consistent
        /// half-open [0, W) / [0, H) range: the left/bottom edges of the rect map in-bounds, the right/top
        /// edges map out-of-bounds. Returns false outside the grid.</summary>
        public bool TryPointerToCell(Vector2 screenPoint, Camera cam, out int cellX, out int cellY)
        {
            cellX = cellY = -1;
            if (grid == null) return false;
            var rt = (RectTransform)cellsImage.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, cam, out var local)) return false;
            var rect = rt.rect;
            float fx = (local.x - rect.xMin) / rect.width * grid.Width;
            float fy = (local.y - rect.yMin) / rect.height * grid.Height;
            cellX = Mathf.FloorToInt(fx);
            cellY = Mathf.FloorToInt(fy);
            return grid.InBounds(cellX, cellY);
        }

        void OnDestroy() { if (texture != null) Destroy(texture); }
    }
}
