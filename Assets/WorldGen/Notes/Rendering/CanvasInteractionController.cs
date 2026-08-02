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
        public RectTransform viewportRect;
        public RectTransform toolbarRect;
        public Camera uiCamera; // null for ScreenSpaceOverlay canvases

        /// <summary>The reduced mode — a board in the flow of a page — permits exactly two gestures: dragging
        /// a card (which the card's own IDragHandler performs) and resizing the block (which the row's grip
        /// performs). Panning, zooming, drawing and link-dragging are the four gestures that FIGHT the page's
        /// scroll, and П1 refused to nest a board in a document because of them. They live only in the
        /// expanded view, so the conflict is removed by construction rather than settled by arbitration.</summary>
        public CanvasMode Mode = CanvasMode.Expanded;

        /// <summary>Used only by the confirm dialog this controller raises. Legacy chrome, deliberately.</summary>
        [Header("Confirm dialog")]
        public Font builtinFont;

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

        // The same fallback the deleted NotesUndoManager carried: the confirm dialog is built at runtime and
        // needs a font, and every caller that forgets to hand one over would otherwise raise a dialog with
        // invisible text.
        void Awake()
        {
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>The active tool changed — including when THIS class changed it on its own, which is the
        /// case the toolbar could not otherwise know about. Raised on every SetTool, so a subscriber must not
        /// call SetTool back from it; NotesToolbar's handler only repaints.</summary>
        public event System.Action<NotesTool> OnToolChanged;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            paintingDrawingObjectId = null;
            OnToolChanged?.Invoke(tool);
        }

        /// <summary>Disarms an inserting tool the moment it has inserted something — a note, an image, or a
        /// new drawing. Without this, the tool stays armed and every further click on the board makes ANOTHER
        /// one: the DM places a card, clicks it to start typing, and gets a second card under the cursor.
        ///
        /// PAINTING AN EXISTING DRAWING IS NOT AN INSERTION and deliberately does not disarm — a drawing is
        /// made of many strokes, and dropping the brush after each one would be unusable. The rule is exactly
        /// "the click that ADDS an object is the tool's last click", nothing wider.
        ///
        /// A CANCELLED FILE DIALOG DOES NOT DISARM EITHER: nothing was inserted, so the DM is still trying to
        /// place an image, and taking the tool away would punish them for changing their mind about which file.</summary>
        void ReturnToSelect()
        {
            if (ActiveTool != NotesTool.Select) SetTool(NotesTool.Select);
        }

        void SetSelectedObjectId(string objectId)
        {
            selectedObjectId = objectId;
            canvasController.SetSelectedObject(objectId);
        }

        void Update()
        {
            // Everything below this line is a gesture the reduced mode does not have — including the wheel,
            // which in the flow of a page belongs to the page and nothing else.
            if (Mode == CanvasMode.Inline) return;
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

            var scrollScreenPos = Mouse.current.position.ReadValue();
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && IsOverViewport(scrollScreenPos) && !IsOverToolbar(scrollScreenPos))
                CanvasWheelZoom.Apply(canvasController, scroll, scrollScreenPos, uiCamera,
                                      CanvasWheelZoom.ExpandedStepPerTick);
        }

        void HandlePress()
        {
            var screenPos = Mouse.current.position.ReadValue();
            if (!IsOverViewport(screenPos)) return;

            // The toolbar floats directly over the canvas viewport (see NotesRootBuilder)
            // instead of sitting in its own reserved strip above it, so a click landing on a
            // toolbar button is also geometrically "inside" viewportRect — without this check
            // the active tool's own click action (e.g. Note) would ALSO fire underneath the
            // button being clicked to switch tools.
            if (IsOverToolbar(screenPos)) return;

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
                    canvasController.AddNoteCard(ScreenToCanvasPoint(screenPos));
                    ReturnToSelect();
                    break;
                case NotesTool.Drawing:
                    var existingDrawing = FindDrawingObjectAt(screenPos);
                    if (existingDrawing != null)
                    {
                        // ONE undo step per STROKE, taken here because the pixels start changing on this very
                        // press. CommitToData replaces the whole PNG at the end of the stroke rather than
                        // writing into the old array, which is what makes a snapshot taken now still hold the
                        // pixels as they were — see DocHistory.CopyObjects on why byte arrays are shared.
                        canvasController.BeforeMutation?.Invoke();
                        paintingDrawingObjectId = existingDrawing.ObjectId;
                        PaintAtScreenPos(existingDrawing, screenPos);
                    }
                    else
                    {
                        canvasController.AddDrawing(ScreenToCanvasPoint(screenPos), defaultDrawingWidth, defaultDrawingHeight);
                        ReturnToSelect();
                    }
                    break;
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                    {
                        canvasController.AddImage(ScreenToCanvasPoint(screenPos), bytes);
                        ReturnToSelect();
                    }
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
                canvasController.AfterMutation?.Invoke();
            }
        }

        /// <summary>Finds the topmost existing DrawingObjectView on the active page whose rect contains the given screen point, or null.</summary>
        DrawingObjectView FindDrawingObjectAt(Vector2 screenPos)
        {
            var objects = canvasController.Block?.CanvasObjects;
            if (objects == null) return null;
            foreach (var obj in objects)
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

        bool IsOverToolbar(Vector2 screenPos)
        {
            if (toolbarRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(toolbarRect, screenPos, uiCamera);
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
                ConfirmDialog.Show(builtinFont, "Удалить связь?", "", confirmed =>
                {
                    if (!confirmed) return;
                    canvasController.RemoveLink(linkIdToDelete);   // pushes history itself, via BeforeMutation
                    if (selectedLinkId == linkIdToDelete) selectedLinkId = null;
                });
                return;
            }

            if (selectedObjectId == null) return;

            var data = FindObjectData(selectedObjectId);
            if (data == null) { selectedObjectId = null; return; }

            // KEPT, EVEN THOUGH UNDO IS REAL NOW. Р4 replaced the canvas's own command stack — where deleting
            // was genuinely irreversible, since the "undo" re-created the object with a fresh id and lost its
            // links — with the page's snapshot history, which restores it exactly. Removing the confirmation
            // would still be a behaviour change the spec did not ask for.
            string idToDelete = selectedObjectId;
            ConfirmDialog.Show(builtinFont, "Удалить объект?", $"«{DescribeObject(data)}»", confirmed =>
            {
                if (!confirmed) return;
                canvasController.RemoveObject(idToDelete);
                if (selectedObjectId == idToDelete) SetSelectedObjectId(null);
            });
        }

        static string DescribeObject(CanvasObjectData data) => data switch
        {
            NoteCardData c => string.IsNullOrEmpty(c.Title) ? "заметку" : c.Title,
            ImageObjectData => "изображение",
            DrawingObjectData => "рисунок",
            _ => "объект"
        };

        void HandleClipboardPaste()
        {
            if (Keyboard.current == null) return;
            bool ctrl = Keyboard.current.ctrlKey.isPressed;
            bool vPressed = Keyboard.current.vKey.wasPressedThisFrame;
            if (!ctrl || !vPressed) return;

            var bytes = ClipboardImage.TryGetImageBytes();
            if (bytes == null) return;

            var screenPos = Mouse.current.position.ReadValue();
            canvasController.AddImage(ScreenToCanvasPoint(screenPos), bytes);
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

        /// <summary>The object is about to move. This — not HandleObjectDragEnded — is where the undo step is
        /// taken: by the time the drag ends the view has already written the new position into the data, and a
        /// snapshot of that would restore the object to where it already is.</summary>
        public void HandleObjectDragStarted(string objectId)
        {
            canvasController.BeforeMutation?.Invoke();
        }

        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            SetSelectedObjectId(objectId);
            canvasController.RefreshLinksFor(objectId);
            canvasController.AfterMutation?.Invoke();
        }

        /// <summary>Called by LinkAnchorController when an anchor-drag is released over another object.</summary>
        public void CreateLinkFromAnchorDrag(string fromObjectId, string toObjectId)
        {
            canvasController.AddLink(fromObjectId, toObjectId);
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

        /// <summary>The corner handle was pressed and the object is about to change size. Same timing rule as
        /// HandleObjectDragStarted, and here it is not merely tidier but required: ApplyResizePreview writes
        /// the data on every frame of the drag, so by CommitResize there is nothing left of the old size to
        /// snapshot.</summary>
        public void BeginResize(string objectId)
        {
            canvasController.BeforeMutation?.Invoke();
        }

        public void CommitResize(string objectId, System.Numerics.Vector2 oldPosition, System.Numerics.Vector2 oldSize)
        {
            var data = FindObjectData(objectId);
            if (data == null) return;
            canvasController.AfterMutation?.Invoke();
        }

        CanvasObjectData FindObjectData(string objectId)
        {
            var objects = canvasController.Block?.CanvasObjects;
            if (objects == null) return null;
            foreach (var obj in objects)
                if (obj.Id == objectId) return obj;
            return null;
        }
    }
}
