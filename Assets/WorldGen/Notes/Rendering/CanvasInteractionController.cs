using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    public enum NotesTool { Select, Note, Link, Drawing, Image }

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

        string linkDragSourceId;
        string paintingDrawingObjectId;
        bool panning;
        Vector2 lastPanScreenPos;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            linkDragSourceId = null;
            paintingDrawingObjectId = null;
        }

        void Update()
        {
            if (canvasController == null || Mouse.current == null) return;

            HandleClipboardPaste();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                HandlePress();
            else if (Mouse.current.leftButton.isPressed && panning)
                HandlePan();
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                panning = false;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && IsOverViewport(Mouse.current.position.ReadValue()))
                canvasController.Zoom(scroll * 0.001f, Mouse.current.position.ReadValue());
        }

        void HandlePress()
        {
            var screenPos = Mouse.current.position.ReadValue();
            if (!IsOverViewport(screenPos)) return;

            switch (ActiveTool)
            {
                case NotesTool.Select:
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
                case NotesTool.Note:
                    undoManager.PushCreateNoteCard(canvasController, ScreenToCanvasPoint(screenPos));
                    break;
                case NotesTool.Drawing:
                    undoManager.PushCreateDrawing(canvasController, ScreenToCanvasPoint(screenPos), defaultDrawingWidth, defaultDrawingHeight);
                    break;
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                        undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
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
            if (ActiveTool == NotesTool.Link)
            {
                if (linkDragSourceId == null)
                {
                    linkDragSourceId = objectId;
                }
                else if (linkDragSourceId != objectId)
                {
                    undoManager.PushCreateLink(canvasController, linkDragSourceId, objectId);
                    linkDragSourceId = null;
                }
            }
        }

        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
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
