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
    /// out with uneven thickness.</summary>
    public class BattleGridRenderer : MonoBehaviour
    {
        public const float LineThickness = 1f;

        RectTransform host;
        RawImage cellsImage;
        AspectRatioFitter fitter;
        Texture2D texture;
        RectTransform overlay;

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
        }

        public void SetGrid(GridBuffer buffer)
        {
            grid = buffer;
            if (grid == null) return;
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

            SyncMarkers(doorPool, doors, DoorColor, cw, ch, x0, y0, 0.34f);
            SyncMarkers(highlightPool, highlight, HighlightColor, cw, ch, x0, y0, 1f);
        }

        void PlaceLine(ref int idx, float x, float y, float w, float h)
        {
            var img = Take(linePool, LineColor, idx);
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = new Vector2(x + w * 0.5f, y + h * 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            idx++;
        }

        // A door mark is a bar across the wall cell (fraction of the cell's short side), so it never
        // reads as a hand-painted GridCell.Door square.
        void SyncMarkers(List<Image> pool, List<GridPoint> points, Color color,
                         float cw, float ch, float x0, float y0, float fill)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var img = Take(pool, color, i);
                var rt = (RectTransform)img.transform;
                rt.sizeDelta = new Vector2(cw * fill, ch * fill);
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

        /// <summary>Screen point → cell. Y is flipped here: UI local Y grows up and so does grid Y, so the
        /// only correction needed is the origin shift. Returns false outside the grid.</summary>
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
