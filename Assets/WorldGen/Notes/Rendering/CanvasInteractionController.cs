using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    public enum NotesTool { Select, Note, Drawing, Image, Zoom }

    /// <summary>
    /// Routes mouse input to canvas actions based on the active tool:
    /// Select (move/pan), Note (click to create card), Link (drag between objects),
    /// Drawing (click to create, drag-paint when a drawing object is active), Image
    /// (click opens a file picker; Ctrl+V pastes clipboard image anywhere, any tool).
    /// </summary>
    public class CanvasInteractionController : MonoBehaviour
    {
        [Header("Dependencies")]
        public NotesCanvasController canvasController;
        public NotesUndoManager undoManager;
        public RectTransform viewportRect;
        public Camera uiCamera; // null for ScreenSpaceOverlay canvases

        [Header("Drawing settings")]
        public float brushRadius = 6f;
        public Color32 brushColor = new Color32(20, 20, 20, 255);
        public int defaultDrawingWidth = 256;
        public int defaultDrawingHeight = 256;

        public NotesTool ActiveTool { get; private set; } = NotesTool.Select;

        string paintingDrawingObjectId;
        string selectedObjectId;
        string selectedLinkId;
        bool panning;
        Vector2 lastPanScreenPos;
        bool zooming;
        Vector2 zoomStartScreenPos;
        float zoomStartScale;
        const float ZoomDragSensitivity = 0.005f;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            paintingDrawingObjectId = null;
        }

        void SetSelectedObjectId(string objectId)
        {
            selectedObjectId = objectId;
            canvasController.SetSelectedObject(objectId);
        }

        void Update()
        {
            if (canvasController == null || Mouse.current == null) return;

            HandleClipboardPaste();
            HandleDeleteKey();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                HandlePress();
            else if (Mouse.current.leftButton.isPressed && panning)
                HandlePan();
            else if (Mouse.current.leftButton.isPressed && zooming)
                HandleZoomDrag();
            else if (Mouse.current.leftButton.isPressed && paintingDrawingObjectId != null)
                HandlePaintDrag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                HandleRelease();

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && IsOverViewport(Mouse.current.position.ReadValue()))
                canvasController.Zoom(scroll * 0.001f, Mouse.current.position.ReadValue());
        }

        void HandlePress()
        {
            var screenPos = Mouse.current.position.ReadValue();
            if (!IsOverViewport(screenPos)) return;

            // A press starting on a link-creation anchor dot is exclusively handled by that
            // dot's own IPointerDownHandler (AnchorDotHandler) via Unity's event system — without
            // this check, the active tool's own click action (e.g. Note) would ALSO fire for the
            // same press, since this polling loop has no idea a UI element under the cursor is
            // about to start its own gesture.
            if (canvasController.IsScreenPointOverLinkAnchor(screenPos, uiCamera))
                return;
            if (canvasController.IsScreenPointOverResizeHandle(screenPos, uiCamera))
                return;

            switch (ActiveTool)
            {
                case NotesTool.Select:
                    // A press that lands on an object is left to that object's own
                    // IPointerDownHandler/IDragHandler (NoteCardView etc.) — starting a pan here
                    // too would move the whole canvas underneath it at the same time as the
                    // object drags itself, fighting each other.
                    if (canvasController.IsScreenPointOverObject(screenPos, uiCamera))
                        break;

                    string linkAt = canvasController.FindLinkAt(screenPos, uiCamera);
                    if (linkAt != null)
                    {
                        SetSelectedObjectId(null);
                        selectedLinkId = linkAt;
                        canvasController.SetSelectedLink(linkAt);
                        break;
                    }

                    SetSelectedObjectId(null);
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
                case NotesTool.Note:
                    undoManager.PushCreateNoteCard(canvasController, ScreenToCanvasPoint(screenPos));
                    break;
                case NotesTool.Drawing:
                    var existingDrawing = FindDrawingObjectAt(screenPos);
                    if (existingDrawing != null)
                    {
                        paintingDrawingObjectId = existingDrawing.ObjectId;
                        PaintAtScreenPos(existingDrawing, screenPos);
                    }
                    else
                    {
                        undoManager.PushCreateDrawing(canvasController, ScreenToCanvasPoint(screenPos), defaultDrawingWidth, defaultDrawingHeight);
                    }
                    break;
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                        undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
                    break;
                case NotesTool.Zoom:
                    zooming = true;
                    zoomStartScreenPos = screenPos;
                    zoomStartScale = canvasController.CanvasContainer.localScale.x;
                    break;
            }
        }

        void HandlePan()
        {
            var screenPos = Mouse.current.position.ReadValue();
            Vector2 delta = screenPos - lastPanScreenPos;
            lastPanScreenPos = screenPos;
            canvasController.Pan(delta);
        }

        void HandleZoomDrag()
        {
            var screenPos = Mouse.current.position.ReadValue();
            float deltaX = screenPos.x - zoomStartScreenPos.x;
            float newScale = zoomStartScale * Mathf.Pow(2f, deltaX * ZoomDragSensitivity);
            canvasController.ZoomAroundScreenPoint(newScale, zoomStartScreenPos, uiCamera);
        }

        void HandlePaintDrag()
        {
            if (canvasController.GetView(paintingDrawingObjectId) is not DrawingObjectView view)
            {
                paintingDrawingObjectId = null;
                return;
            }
            PaintAtScreenPos(view, Mouse.current.position.ReadValue());
        }

        void HandleRelease()
        {
            panning = false;
            zooming = false;
            if (paintingDrawingObjectId != null)
            {
                if (canvasController.GetView(paintingDrawingObjectId) is DrawingObjectView view)
                    view.CommitToData();
                paintingDrawingObjectId = null;
            }
        }

        /// <summary>Finds the topmost existing DrawingObjectView on the active page whose rect contains the given screen point, or null.</summary>
        DrawingObjectView FindDrawingObjectAt(Vector2 screenPos)
        {
            var page = canvasController.documentController.ActivePage;
            if (page == null) return null;
            foreach (var obj in page.Objects)
            {
                if (obj is not DrawingObjectData) continue;
                if (canvasController.GetView(obj.Id) is DrawingObjectView view
                    && RectTransformUtility.RectangleContainsScreenPoint(view.RectTransform, screenPos, uiCamera))
                    return view;
            }
            return null;
        }

        void PaintAtScreenPos(DrawingObjectView view, Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(view.RectTransform, screenPos, uiCamera, out var local);
            view.PaintAt(local, brushRadius, brushColor);
        }

        bool IsOverViewport(Vector2 screenPos)
        {
            if (viewportRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(viewportRect, screenPos, uiCamera);
        }

        System.Numerics.Vector2 ScreenToCanvasPoint(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasController.CanvasContainer, screenPos, uiCamera, out var local);
            return new System.Numerics.Vector2(local.x, local.y);
        }

        /// <summary>Delete key removes the currently selected object (Select tool click, or the
        /// object just dragged) behind a confirm dialog, per the spec's "Delete key / delete
        /// button" binding.</summary>
        void HandleDeleteKey()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.deleteKey.wasPressedThisFrame) return;

            if (selectedLinkId != null)
            {
                var linkData = canvasController.FindLinkData(selectedLinkId);
                if (linkData == null) { selectedLinkId = null; return; }

                string linkIdToDelete = selectedLinkId;
                undoManager.RequestDeleteLink(canvasController, linkData, confirmed =>
                {
                    if (confirmed && selectedLinkId == linkIdToDelete)
                        selectedLinkId = null;
                });
                return;
            }

            if (selectedObjectId == null) return;

            var data = FindObjectData(selectedObjectId);
            if (data == null) { selectedObjectId = null; return; }

            string idToDelete = selectedObjectId;
            undoManager.RequestDeleteObject(canvasController, data, confirmed =>
            {
                if (confirmed && selectedObjectId == idToDelete)
                    SetSelectedObjectId(null);
            });
        }

        void HandleClipboardPaste()
        {
            if (Keyboard.current == null) return;
            bool ctrl = Keyboard.current.ctrlKey.isPressed;
            bool vPressed = Keyboard.current.vKey.wasPressedThisFrame;
            if (!ctrl || !vPressed) return;

            var bytes = ClipboardImage.TryGetImageBytes();
            if (bytes == null) return;

            var screenPos = Mouse.current.position.ReadValue();
            undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
        }

        // ── Called by object views on click/drag, wired externally by NotesCanvasController's spawn sites ──

        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
            {
                SetSelectedObjectId(objectId);
                if (selectedLinkId != null)
                {
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                }
            }
        }

        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            SetSelectedObjectId(objectId);
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
        }

        /// <summary>Called by LinkAnchorController when an anchor-drag is released over another
        /// object — creates the link through the undo stack, same as the old click-click flow.</summary>
        public void CreateLinkFromAnchorDrag(string fromObjectId, string toObjectId)
        {
            undoManager.PushCreateLink(canvasController, fromObjectId, toObjectId);
        }

        /// <summary>Called live while ObjectResizeController drags a corner handle — applies the
        /// new size/position immediately for responsive feedback; the undo entry is only pushed
        /// once, in CommitResize, when the drag ends.</summary>
        public void ApplyResizePreview(string objectId, System.Numerics.Vector2 newPosition, System.Numerics.Vector2 newSize)
        {
            var data = FindObjectData(objectId);
            if (data == null) return;
            data.Position = newPosition;
            data.Size = newSize;
            canvasController.RefreshView(objectId);
            canvasController.RefreshLinksFor(objectId);
        }

        public void CommitResize(string objectId, System.Numerics.Vector2 oldPosition, System.Numerics.Vector2 oldSize)
        {
            var data = FindObjectData(objectId);
            if (data == null) return;
            undoManager.PushResize(canvasController, data, oldPosition, oldSize);
        }

        CanvasObjectData FindObjectData(string objectId)
        {
            var page = canvasController.documentController.ActivePage;
            if (page == null) return null;
            foreach (var obj in page.Objects)
                if (obj.Id == objectId) return obj;
            return null;
        }
    }
}
