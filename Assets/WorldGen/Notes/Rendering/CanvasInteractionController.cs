using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

        /// <summary>Объект, к которому привязан активный инструмент, — тот, кому адресован его следующий
        /// клик. ОТДЕЛЬНОЕ ПОЛЕ ОТ paintingDrawingObjectId, и это принципиально: то живёт ровно один мазок
        /// (обнуляется в HandleRelease), а привязка переживает отпускания кнопки, пока её не снимут явно —
        /// кликом мимо, сменой инструмента, Esc или удалением самого объекта.</summary>
        string boundObjectId;

        string selectedObjectId;
        string selectedLinkId;
        bool panning;

        /// <summary>A middle-button drag is in progress — see Update. Separate from `panning`, which belongs to
        /// the Select tool's left-drag, so the two gestures cannot end each other.</summary>
        bool middlePanning;
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
            // Смена инструмента снимает привязку — одна точка, а не две. Сюда же приходит и «клик мимо»,
            // который кладёт инструмент в «Курсор», и Esc.
            boundObjectId = null;
            OnToolChanged?.Invoke(tool);
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
            HandleEscapeKey();

            // MIDDLE-BUTTON DRAG PANS, with any tool and over anything — including over a card, which is
            // exactly where the left button cannot pan and must not: there, a left drag moves the CARD.
            //
            // Left-drag-on-empty-space still pans (the Select branch of HandlePress), and this does not
            // replace it. It covers the case that one cannot: a board zoomed in far enough to be worth moving
            // around is a board whose objects fill the frame, so "empty space to grab" is precisely what runs
            // out at the moment panning becomes necessary. Every canvas tool the DM has used works this way.
            var mouseScreenPos = Mouse.current.position.ReadValue();
            if (Mouse.current.middleButton.wasPressedThisFrame
                && IsOverViewport(mouseScreenPos) && !IsOverToolbar(mouseScreenPos))
            {
                middlePanning = true;
                lastPanScreenPos = mouseScreenPos;
            }
            else if (Mouse.current.middleButton.wasReleasedThisFrame)
            {
                middlePanning = false;
            }

            // A middle-drag owns the frame outright: no tool action, no wheel zoom underneath it. Its own flag
            // rather than `panning`, so a left-button release cannot end it and a middle-drag cannot be
            // mistaken for the Select tool's pan half-way through.
            if (middlePanning && Mouse.current.middleButton.isPressed)
            {
                HandlePan();
                return;
            }

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

            // ДВА ИНСТРУМЕНТА ЖИВУТ ЗДЕСЬ И НЕ УХОДЯТ В ЧИСТЫЙ СЛОЙ: их логика — это панорамирование,
            // попадание по объекту и попадание по связи, то есть три хит-теста по RectTransform. Утащить
            // их в CanvasToolOps значило бы утащить туда всю геометрию вью ради задачи, которая её не
            // касается.
            if (ActiveTool == NotesTool.Select)
            {
                // A press that lands on an object is left to that object's own
                // IPointerDownHandler/IDragHandler (NoteCardView etc.) — starting a pan here
                // too would move the whole canvas underneath it at the same time as the
                // object drags itself, fighting each other.
                if (canvasController.IsScreenPointOverObject(screenPos, uiCamera))
                    return;

                string linkAt = canvasController.FindLinkAt(screenPos, uiCamera);
                if (linkAt != null)
                {
                    SetSelectedObjectId(null);
                    selectedLinkId = linkAt;
                    canvasController.SetSelectedLink(linkAt);
                    return;
                }

                SetSelectedObjectId(null);
                selectedLinkId = null;
                canvasController.SetSelectedLink(null);
                panning = true;
                lastPanScreenPos = screenPos;
                return;
            }

            if (ActiveTool == NotesTool.Zoom)
            {
                canvasController.CancelZoomAnimation();
                zooming = true;
                zoomStartScreenPos = screenPos;
                zoomStartScale = canvasController.CanvasContainer.localScale.x;
                return;
            }

            var underCursor = FindDrawingObjectAt(screenPos);
            var decision = CanvasToolOps.Decide(
                new CanvasClickInput(ToToolKind(ActiveTool), boundObjectId, underCursor?.ObjectId));

            // Ложь только в одном случае: диалог выбора файла отменён. Тогда ничего не вставлено, ДМ всё
            // ещё выбирает файл, и отнимать у него инструмент нельзя.
            bool acted = true;

            switch (decision.Action)
            {
                case CanvasClickAction.CreateNote:
                    var card = canvasController.AddNoteCard(ScreenToCanvasPoint(screenPos));
                    if (card != null)
                    {
                        SetSelectedObjectId(card.Id);
                        FocusNoteBodyNextFrame(card.Id);
                    }
                    break;

                case CanvasClickAction.CreateImage:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes == null) { acted = false; break; }
                    var image = canvasController.AddImage(ScreenToCanvasPoint(screenPos), bytes);
                    if (image != null) SetSelectedObjectId(image.Id);
                    break;

                case CanvasClickAction.CreateDrawing:
                    var drawing = canvasController.AddDrawing(ScreenToCanvasPoint(screenPos),
                                                              defaultDrawingWidth, defaultDrawingHeight);
                    if (drawing == null) { acted = false; break; }
                    SetSelectedObjectId(drawing.Id);
                    // Привязываемся к тому, что только что создали, — весь смысл правки в этой строке.
                    if (decision.BindCreatedObject) boundObjectId = drawing.Id;
                    break;

                case CanvasClickAction.PaintExisting:
                    if (canvasController.GetView(decision.TargetObjectId) is not DrawingObjectView view)
                    { acted = false; break; }
                    // ONE undo step per STROKE, taken here because the pixels start changing on this very
                    // press. CommitToData replaces the whole PNG at the end of the stroke rather than
                    // writing into the old array, which is what makes a snapshot taken now still hold the
                    // pixels as they were — see DocHistory.CopyObjects on why byte arrays are shared.
                    canvasController.BeforeMutation?.Invoke();
                    paintingDrawingObjectId = view.ObjectId;
                    PaintAtScreenPos(view, screenPos);
                    boundObjectId = decision.BoundObjectId;
                    break;

                case CanvasClickAction.ReleaseBinding:
                    // Выделение НАМЕРЕННО не снимается: иначе после первого промаха теряется возможность
                    // подвинуть или растянуть только что созданный объект — ровно то, ради чего правка и
                    // делается. Инструмент в «Курсор» кладёт общий хвост ниже.
                    break;
            }

            if (acted && decision.ReturnToSelect && ActiveTool != NotesTool.Select)
                SetTool(NotesTool.Select);
        }

        /// <summary>NotesTool живёт здесь, рядом с MonoBehaviour, и в офлайн-харнесс не поедет; его зеркало
        /// в чистом слое — CanvasToolKind. Отображение одно-в-одно.</summary>
        static CanvasToolKind ToToolKind(NotesTool tool) => tool switch
        {
            NotesTool.Note => CanvasToolKind.Note,
            NotesTool.Drawing => CanvasToolKind.Drawing,
            NotesTool.Image => CanvasToolKind.Image,
            NotesTool.Zoom => CanvasToolKind.Zoom,
            _ => CanvasToolKind.Select,
        };

        /// <summary>КАДРОМ ПОЗЖЕ, И ЭТО НЕ ПЕРЕСТРАХОВКА. TMP_InputField, собранный в этом же кадре, ещё не
        /// проходил вёрстку и фокус не принимает — Р2 потратил на родственную задержку («новая строка узнаёт
        /// свою высоту кадром позже») целый круг проверок у ДМ.</summary>
        void FocusNoteBodyNextFrame(string objectId)
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(FocusNoteBodyCoroutine(objectId));
        }

        System.Collections.IEnumerator FocusNoteBodyCoroutine(string objectId)
        {
            yield return null;
            // За этот кадр карточку могли успеть отменить через Ctrl+Z — тогда вида просто нет.
            if (canvasController != null && canvasController.GetView(objectId) is NoteCardView card)
                card.FocusBody();
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

            // ПОКА КАРЕТКА В ПОЛЕ ВВОДА, DELETE ПРАВИТ ТЕКСТ. Мина лежала здесь и до этого арка, просто
            // редко стреляла: чтобы попасть в поле карточки, надо было сначала кликнуть по нему, а объект
            // к тому моменту уже выделен. Теперь каретка встаёт в текст сама при каждой новой заметке, и
            // первая же опечатка на Delete предлагала бы удалить саму карточку.
            var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focused != null && focused.GetComponent<TMP_InputField>() != null) return;

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
                // Иначе следующий клик внутрь бывшего прямоугольника адресован мёртвому id.
                if (boundObjectId == idToDelete) boundObjectId = null;
            });
        }

        /// <summary>Esc кладёт армированный инструмент в «Курсор», а вместе с ним (через SetTool) снимает
        /// привязку. Тот же жест, что гасит кисть в редакторе поселения (DungeonViewController).
        ///
        /// Только когда инструмент НЕ «Курсор»: иначе этот обработчик отбирал бы Esc у TMP_InputField, для
        /// которого Esc — «выйти из поля», и у диалогов, которые им закрываются.</summary>
        void HandleEscapeKey()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (ActiveTool == NotesTool.Select) return;
            SetTool(NotesTool.Select);
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

        /// <summary>Called live while ObjectResizeController drags a corner handle — applies the new
        /// size/position immediately for responsive feedback. The undo entry is pushed once per drag, by
        /// BeginResize below, on the FIRST movement: this method writes the data on every frame, so by the
        /// end of the drag the old size is gone. (It used to be pushed at the end, and that is what the
        /// snapshot then held: the new size, restoring the object to where it already was.)</summary>
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
