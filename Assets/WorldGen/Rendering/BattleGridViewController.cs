using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public enum GridTool { Brush, Rect, Fill }

    /// <summary>Input and edits for one battle map: pointer → cell, strokes, tool dispatch, undo. Draws
    /// nothing — the renderer owns pixels; this owns the buffer and the undo stack. Mirrors the
    /// DungeonViewController / IDungeonRenderer split.</summary>
    public class BattleGridViewController : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerMoveHandler, IPointerExitHandler
    {
        public System.Action OnChanged;          // fires after any edit that must be persisted
        public GridTool Tool = GridTool.Brush;
        public GridCell Material = GridCell.Wall;
        public int BrushSize = 1;                // 1 / 3 / 5

        BattleGridRenderer renderer;
        GridBuffer buffer;
        readonly BattleGridUndo undo = new BattleGridUndo();

        BattleGridStroke stroke;
        int lastX = -1, lastY = -1;
        int rectAnchorX = -1, rectAnchorY = -1;

        public GridBuffer Buffer => buffer;
        public int UndoDepth => undo.Count;

        public void Bind(BattleGridRenderer r, GridBuffer buf)
        {
            renderer = r;
            buffer = buf;
            undo.Clear();
            renderer.SetGrid(buffer);
            renderer.Repaint();
        }

        /// <summary>Replace the whole grid (resize / regenerate), recording the previous state so Ctrl+Z
        /// undoes it like any other step.</summary>
        public void ReplaceBuffer(GridBuffer next)
        {
            undo.PushSnapshot(buffer);
            buffer = next;
            renderer.SetGrid(buffer);
            renderer.Repaint();
            OnChanged?.Invoke();
        }

        void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z) && undo.TryUndo(ref buffer))
            {
                renderer.SetGrid(buffer);
                renderer.Repaint();
                OnChanged?.Invoke();
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!renderer.TryPointerToCell(e.position, e.pressEventCamera, out int x, out int y)) return;
            stroke = new BattleGridStroke();
            lastX = x; lastY = y;

            switch (Tool)
            {
                case GridTool.Brush: BattleGridOps.Stamp(buffer, stroke, x, y, BrushSize, Material); break;
                case GridTool.Fill:  BattleGridOps.Fill(buffer, stroke, x, y, Material); break;
                case GridTool.Rect:  rectAnchorX = x; rectAnchorY = y; break;
            }
            renderer.Repaint();
        }

        public void OnDrag(PointerEventData e)
        {
            if (stroke == null) return;
            if (!renderer.TryPointerToCell(e.position, e.pressEventCamera, out int x, out int y)) return;

            if (Tool == GridTool.Brush)
            {
                // Bresenham from the previous frame's cell: a fast drag jumps several cells and would
                // otherwise leave a dotted line.
                BattleGridOps.Line(buffer, stroke, lastX, lastY, x, y, BrushSize, Material);
                lastX = x; lastY = y;
                renderer.Repaint();
            }
            else if (Tool == GridTool.Rect)
            {
                ShowRectPreview(x, y);
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (stroke == null) return;

            if (Tool == GridTool.Rect &&
                renderer.TryPointerToCell(e.position, e.pressEventCamera, out int x, out int y))
                BattleGridOps.Rect(buffer, stroke, rectAnchorX, rectAnchorY, x, y, Material);

            if (!stroke.IsEmpty)
            {
                undo.PushStroke(stroke);
                OnChanged?.Invoke();
            }
            stroke = null;
            renderer.SetHighlight(null);
            renderer.Repaint();
        }

        public void OnPointerMove(PointerEventData e)
        {
            if (stroke != null) return;                     // mid-stroke the preview would fight the paint
            if (!renderer.TryPointerToCell(e.position, e.pressEventCamera, out int x, out int y))
            { renderer.SetHighlight(null); renderer.Repaint(); return; }
            renderer.SetHighlight(BrushCells(x, y));
            renderer.Repaint();
        }

        public void OnPointerExit(PointerEventData e)
        {
            renderer.SetHighlight(null);
            renderer.Repaint();
        }

        void ShowRectPreview(int x, int y)
        {
            var cells = new List<GridPoint>();
            int lo_x = Mathf.Min(rectAnchorX, x), hi_x = Mathf.Max(rectAnchorX, x);
            int lo_y = Mathf.Min(rectAnchorY, y), hi_y = Mathf.Max(rectAnchorY, y);
            for (int cy = lo_y; cy <= hi_y; cy++)
                for (int cx = lo_x; cx <= hi_x; cx++)
                    if (buffer.InBounds(cx, cy)) cells.Add(new GridPoint { X = cx, Y = cy });
            renderer.SetHighlight(cells);
            renderer.Repaint();
        }

        List<GridPoint> BrushCells(int cx, int cy)
        {
            var cells = new List<GridPoint>();
            if (Tool != GridTool.Brush) { cells.Add(new GridPoint { X = cx, Y = cy }); return cells; }
            int r = BrushSize / 2;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (buffer.InBounds(x, y)) cells.Add(new GridPoint { X = x, Y = y });
            return cells;
        }
    }
}
