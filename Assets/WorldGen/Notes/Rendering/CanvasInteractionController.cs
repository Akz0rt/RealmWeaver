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
        public Camera uiCamera; // null for ScreenSpaceOverlay canvases

        readonly System.Collections.Generic.List<RectTransform> chromeRects = new System.Collections.Generic.List<RectTransform>();

        /// <summary>Прямоугольник панели, клик по которой НЕ должен доходить до доски — иначе выбор
        /// цвета кисти заодно ставил бы кляксу. Панелей теперь три: инструменты, настройки кисти и
        /// свойства карточки.</summary>
        public void RegisterChrome(RectTransform rect)
        {
            if (rect != null && !chromeRects.Contains(rect)) chromeRects.Add(rect);
        }

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
        public int brushColorIndex = NotesPalette.InkIndex;
        public BrushWidth brushWidth = BrushWidth.Medium;

        /// <summary>Ластик — СОСТОЯНИЕ, а не цвет с особым индексом. Отдельным «индексом ластика» он
        /// был бы неотличим от битого индекса: NotesPalette.At молча возвращает нейтральный на всё,
        /// что вне списка, и один случайный At(brushColorIndex) сделал бы ластик серым без единой
        /// ошибки в Консоли.</summary>
        public bool brushIsEraser;

        public int defaultDrawingWidth = 256;
        public int defaultDrawingHeight = 256;

        public NotesTool ActiveTool { get; private set; } = NotesTool.Select;

        string paintingDrawingObjectId;

        /// <summary>Мазок, который собирается прямо сейчас. Живёт от нажатия до отпускания и в этот
        /// момент ЕЩЁ НЕ ЛЕЖИТ в данных: он добавляется в список одним куском в HandleRelease, потому
        /// что законченный мазок неизменяем (см. Stroke) — снимок отмены, взятый на нажатии, обязан
        /// остаться без него.</summary>
        Stroke activeStroke;
        StrokePoint lastPoint;
        Vector2 lastLocal;
        float lastSampleTime;
        float widthMultiplier;

        /// <summary>Базовая толщина в долях ширины рисунка. Считается один раз на мазок: рисунок за
        /// время мазка не меняет размера, а делить в каждом кадре значило бы делить одно и то же.</summary>
        float baseWidthFraction;

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
            // ПОДТВЕРЖДЕНИЕ УЖЕ АКТИВНОГО ИНСТРУМЕНТА — НЕ СМЕНА. Правило вынесено в чистый слой
            // (CanvasToolOps.ShouldAbandonStroke), чтобы его видел офлайн-харнесс. Раньше SetTool
            // безусловно бросал недоведённый мазок при ЛЮБОМ вызове, и двойной клик по рисунку
            // (EnterDrawingEditMode зовёт SetTool(Drawing), даже когда «Рисунок» уже активен) съедал
            // второй тычок быстрой пары: клик вниз уже начинал мазок через HandlePress, а клик вверх —
            // распознанный uGUI как двойной — обнулял activeStroke раньше, чем опрос Mouse.current в
            // Update успевал дописать в него точку. Мазок терялся целиком: ни точки на экране, ни
            // записи в данных.
            bool changingTool = CanvasToolOps.ShouldAbandonStroke(ToToolKind(ActiveTool), ToToolKind(tool));
            ActiveTool = tool;

            if (changingTool)
            {
                // НЕДОВЕДЁННЫЙ МАЗОК БРОСАЕТСЯ, А НЕ ЗАВИСАЕТ. Esc посреди протаскивания приходит сюда
                // (HandleEscapeKey), и отпускания кнопки, которое обычно закрывает мазок, уже не будет:
                // без этого поле activeStroke осталось бы занятым навсегда, а вместе с ним представление
                // держало бы рабочие буферы растра — 8 МБ на рисунок при 1024×1024. Перепечатка заодно
                // убирает с экрана сырую линию, которой в данных нет: Esc отменяет мазок целиком.
                if (activeStroke != null && paintingDrawingObjectId != null && canvasController != null
                    && canvasController.GetView(paintingDrawingObjectId) is DrawingObjectView unfinished)
                    unfinished.Rebake();
                ForgetStroke();
            }
            // Смена инструмента снимает привязку — одна точка, а не две. Сюда же приходит и «клик мимо»,
            // который кладёт инструмент в «Курсор», и Esc. Срабатывает В ОБОИХ СЛУЧАЯХ (даже когда
            // инструмент не поменялся): EnterDrawingEditMode выставляет boundObjectId сразу ПОСЛЕ этого
            // вызова, и то же самое верно для OnToolChanged — подписчики (полоска кисти) ждут его на
            // каждый SetTool, а не только на смену.
            boundObjectId = null;
            OnToolChanged?.Invoke(tool);
        }

        /// <summary>Выделение сменилось — полоска свойств узнаёт об этом отсюда и ниоткуда больше.
        /// Поднимается и на снятии выделения (objectId == null), иначе полоска осталась бы висеть
        /// над карточкой, которую уже не выделяют.</summary>
        public event System.Action<string> OnSelectedObjectChanged;

        void SetSelectedObjectId(string objectId)
        {
            selectedObjectId = objectId;
            canvasController.SetSelectedObject(objectId);
            OnSelectedObjectChanged?.Invoke(objectId);
        }

        /// <summary>Правка стиля — такая же правка, как перемещение: снимок ДО изменения, перерисовка
        /// карточки после. Refresh обязателен: данные меняются мимо перестройки доски, и без него
        /// карточка осталась бы прежней до следующего повода перерисоваться.</summary>
        public void SetCardFrameColor(string objectId, int index)
        {
            if (!(FindObjectData(objectId) is NoteCardData card)) return;
            canvasController.BeforeMutation?.Invoke();
            card.FrameColorIndex = index;
            NotesUserPrefs.CardFrameColorIndex = index;
            (canvasController.GetView(objectId) as NoteCardView)?.Refresh();
            canvasController.AfterMutation?.Invoke();
        }

        public void SetCardFontSize(string objectId, CardFontSize size)
        {
            if (!(FindObjectData(objectId) is NoteCardData card)) return;
            canvasController.BeforeMutation?.Invoke();
            card.FontSize = size;
            NotesUserPrefs.CardFont = size;
            (canvasController.GetView(objectId) as NoteCardView)?.Refresh();
            canvasController.AfterMutation?.Invoke();
        }

        /// <summary>Двойной клик по рисунку — вход в его правку, то же правило, что у карточки: одиночный
        /// клик выделяет и двигает, двойной пускает внутрь. «Внутрь» для рисунка означает «можно красить»,
        /// то есть активный инструмент «Рисунок», привязанный именно к этому объекту.
        ///
        /// ПРИВЯЗКА СТАВИТСЯ ПОСЛЕ SetTool, А НЕ ДО: SetTool сам обнуляет её (смена инструмента снимает
        /// привязку — одна точка, а не две), и порядок наоборот молча оставил бы рисунок непривязанным.
        ///
        /// Выхода отсюда нет и не нужно: клик мимо уже кладёт инструмент в «Курсор» и снимает привязку —
        /// тем же общим хвостом HandlePress, каким это работает для только что созданного рисунка.</summary>
        public void EnterDrawingEditMode(string objectId)
        {
            // В странице доска живёт в урезанном режиме: там нет ни рисования, ни панели инструментов.
            if (Mode == CanvasMode.Inline) return;
            if (!(FindObjectData(objectId) is DrawingObjectData)) return;

            SetSelectedObjectId(objectId);
            SetTool(NotesTool.Drawing);
            boundObjectId = objectId;
        }

        /// <summary>Выбор кисти: цвет и толщина запоминаются на следующий раз. Ластик снимается любым
        /// выбором цвета — иначе ДМ выбрал бы красный и продолжил стирать.</summary>
        public void SetBrush(int colorIndex, BrushWidth width)
        {
            brushColorIndex = colorIndex;
            brushWidth = width;
            brushIsEraser = false;
            NotesUserPrefs.BrushColorIndex = colorIndex;
            NotesUserPrefs.BrushStroke = width;
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
                && IsOverViewport(mouseScreenPos) && !IsOverChrome(mouseScreenPos))
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

            // ТЫЧОК КИСТЬЮ КОНЧАЕТСЯ ЗДЕСЬ, А НЕ В ЦЕПОЧКЕ ВЫШЕ. При клике короче кадра система
            // ввода выставляет wasPressedThisFrame и wasReleasedThisFrame ОДНОВРЕМЕННО: побеждает
            // первая ветка, следующий кадр оба флага уже сброшены, и HandleRelease не зовётся
            // никогда. Мазок из одной точки не появлялся бы вовсе, шаг отмены оставался бы пустым,
            // а буферы растра (8 МБ на рисунок) висели бы до следующего нажатия.
            //
            // ХВОСТОМ, А НЕ ПЕРЕСТАНОВКОЙ ВЕТОК: перестановка задела бы панорамирование и зум,
            // которые кончаются той же кнопкой, а условие «кнопка не нажата, а мазок ещё идёт»
            // касается только рисования. Повторного вызова не будет — HandleRelease обнуляет
            // paintingDrawingObjectId, и на обычном отпускании ветка выше уже это сделала.
            if (!Mouse.current.leftButton.isPressed && paintingDrawingObjectId != null)
                HandleRelease();

            var scrollScreenPos = Mouse.current.position.ReadValue();
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && IsOverViewport(scrollScreenPos) && !IsOverChrome(scrollScreenPos))
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
            if (IsOverChrome(screenPos)) return;

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

            // ПРИВЯЗКА МОГЛА УМЕРЕТЬ МЕЖДУ КЛИКАМИ — Ctrl+Z, удаление. Мёртвый id безопасен, но не безвреден:
            // клик по пустому месту прочитался бы как «мимо», инструмент лёг бы, и не создалось бы ничего —
            // ДМ пришлось бы жать «Рисунок» заново. Живость проверяется здесь, а не в CanvasToolOps: чистый
            // слой не знает и не должен знать, существует ли ещё вид.
            if (boundObjectId != null && canvasController.GetView(boundObjectId) == null)
                boundObjectId = null;

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
                    // ОДИН ШАГ ОТМЕНЫ НА МАЗОК, и берётся он ЗДЕСЬ, на нажатии. Снимок остаётся верным
                    // потому, что мазок попадает в данные только в HandleRelease и целиком: список в
                    // снимке — другой объект с теми же ссылками (DocHistory.CopyObjects), а добавление
                    // в конец живого списка его не касается.
                    canvasController.BeforeMutation?.Invoke();
                    StartStroke(view, screenPos);
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

        /// <summary>Очередная точка мазка. Толщина считается от СКОРОСТИ, а не от расстояния за кадр:
        /// расстояние за кадр на слабом компьютере больше при той же руке, и перо выходило бы тоньше
        /// (см. StrokeWidthOps).</summary>
        void HandlePaintDrag()
        {
            if (canvasController.GetView(paintingDrawingObjectId) is not DrawingObjectView view
                || activeStroke == null)
            { ForgetStroke(); return; }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                view.RectTransform, Mouse.current.position.ReadValue(), uiCamera, out var local);

            // Расстояние в ЕДИНИЦАХ ДОСКИ, а не в долях и не в пикселях экрана: локальные координаты
            // прямоугольника и есть единицы доски, потому что рисунок живёт под масштабируемым
            // контейнером.
            float distance = Vector2.Distance(local, lastLocal);
            float dt = Time.unscaledTime - lastSampleTime;
            float target = StrokeWidthOps.MultiplierFor(StrokeWidthOps.SpeedOf(distance, dt));
            // ТОТ ЖЕ dt, по которому мерилась скорость: сглаживание приводится к прошедшему времени,
            // иначе множитель догонял бы цель тем быстрее, чем чаще кадры.
            widthMultiplier = StrokeWidthOps.Smooth(widthMultiplier, target, dt);

            var f = view.LocalToFraction(local);
            var point = new StrokePoint(f.x, f.y, baseWidthFraction * widthMultiplier);
            activeStroke.Points.Add(point);

            view.StampLive(lastPoint, point);

            lastPoint = point;
            lastLocal = local;
            lastSampleTime = Time.unscaledTime;
        }

        void HandleRelease()
        {
            panning = false;
            zooming = false;
            if (paintingDrawingObjectId != null)
            {
                if (canvasController.GetView(paintingDrawingObjectId) is DrawingObjectView view
                    && activeStroke != null && view.Data is DrawingObjectData drawing)
                {
                    AddFinalPoint(view);
                    StrokeWidthOps.FixFirstWidth(activeStroke.Points);
                    // Чистка цепочки появится в задаче 8 — ровно здесь, одной строкой.
                    drawing.Strokes.Add(activeStroke);
                    view.EndStroke();
                }
                ForgetStroke();
                canvasController.AfterMutation?.Invoke();
            }
        }

        /// <summary>Точка ровно там, где кнопку отпустили. На кадре отпускания HandlePaintDrag уже не
        /// выполняется (ветка цепочки требует зажатой кнопки), поэтому без этого мазок кончался бы
        /// там, где курсор был КАДРОМ РАНЬШЕ, — на быстром движении это заметный недомазок.
        ///
        /// Не двинулись — точки не будет: тычок кистью иначе получал бы вторую точку поверх первой,
        /// а мазок из одной точки это законный рисунок (см. StrokeWidthOps.FixFirstWidth), и
        /// вырожденный отрезок в нём ни к чему.</summary>
        void AddFinalPoint(DrawingObjectView view)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                view.RectTransform, Mouse.current.position.ReadValue(), uiCamera, out var local);
            if (Vector2.Distance(local, lastLocal) <= 0.0001f) return;

            var f = view.LocalToFraction(local);
            lastPoint = new StrokePoint(f.x, f.y, baseWidthFraction * widthMultiplier);
            activeStroke.Points.Add(lastPoint);
            lastLocal = local;
        }

        /// <summary>Мазок кончился любым путём. ОДНА ПОМОЩНИЦА НА ВСЕ ВЫХОДЫ: те же два поля
        /// обнулялись в четырёх местах, и каждое успело разойтись с остальными — ранний выход из
        /// HandlePaintDrag забывал activeStroke, и HandleRelease после него в свою ветку уже не
        /// заходил, оставляя мазок и буферы висеть до следующего нажатия.</summary>
        void ForgetStroke()
        {
            activeStroke = null;
            paintingDrawingObjectId = null;
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

        /// <summary>Начало мазка. Базовый диаметр берётся в ЕДИНИЦАХ ДОСКИ (BrushOps) и переводится в
        /// доли ширины ОДИН РАЗ, здесь же, где потом переводится и скорость, — чтобы обе величины
        /// жили в одной системе. Толщина считается по самому рисунку, а не готовым числом пикселей:
        /// разрешение растра выбирается из размера объекта, и «тонко» на растянутом вдвое рисунке
        /// иначе рисовало бы вдвое толще, чем показывает кружок в полоске кисти.
        ///
        /// Ластик — СОСТОЯНИЕ, поэтому цвет у него не спрашивается вовсе: индекс чернил у стирающего
        /// мазка не читается, а к цвету листа его возвращает StrokeRaster, один раз и для всех.</summary>
        void StartStroke(DrawingObjectView view, Vector2 screenPos)
        {
            if (view.Data is not DrawingObjectData) return;

            paintingDrawingObjectId = view.ObjectId;
            widthMultiplier = 1f;

            // ЧЕРЕЗ САМО ПРЕДСТАВЛЕНИЕ, а не делением на drawing.Size.X: толщина и координаты обязаны
            // мериться одной линейкой, и линейка эта — прямоугольник рисунка (см. LengthToFraction).
            float baseDiameter = BrushOps.DiameterInCanvasUnits(brushWidth);
            baseWidthFraction = view.LengthToFraction(baseDiameter);
            // Прямоугольник ещё не прошёл вёрстку и его ширина нулевая — рисовать всё равно чем-то
            // надо, и два процента ширины это видимая линия, а не ноль.
            if (baseWidthFraction <= 0.0001f) baseWidthFraction = 0.02f;

            activeStroke = new Stroke
            {
                IsEraser = brushIsEraser,
                InkIndex = brushIsEraser ? 0 : brushColorIndex,
            };

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                view.RectTransform, screenPos, uiCamera, out lastLocal);
            var f = view.LocalToFraction(lastLocal);
            lastPoint = new StrokePoint(f.x, f.y, baseWidthFraction);
            activeStroke.Points.Add(lastPoint);
            lastSampleTime = Time.unscaledTime;

            view.BeginStroke(activeStroke);
        }

        bool IsOverViewport(Vector2 screenPos)
        {
            if (viewportRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(viewportRect, screenPos, uiCamera);
        }

        /// <summary>Та же проверка, что и внутри, но для полосок: им нужно знать, видно ли ещё
        /// объект, за которым они едут.</summary>
        public bool IsScreenPointOverViewport(Vector2 screenPos) => IsOverViewport(screenPos);

        bool IsOverChrome(Vector2 screenPos)
        {
            foreach (var rect in chromeRects)
            {
                // ВЫКЛЮЧЕННЫЕ ПРОПУСКАЮТСЯ. RectangleContainsScreenPoint отвечает чисто геометрически
                // и знать не знает про activeInHierarchy — спрятанная полоска свойств иначе продолжала
                // бы съедать клики на своём последнем месте, и это читалось бы как «доска перестала
                // отвечать вот в этом углу».
                if (rect == null || !rect.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, uiCamera)) return true;
            }
            return false;
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

        /// <summary>ПЕРВОЕ нажатие клавиши в тексте карточки за этот заход. Снимок берётся здесь по той же
        /// причине, по какой он берётся до перетаскивания: к концу набора данные уже переписаны, и снимок,
        /// сделанный тогда, вернул бы ровно то, что и так на экране.
        ///
        /// Без этого вызова набор текста не создавал шага отмены ВООБЩЕ, и верхним шагом оставалось
        /// «состояние до создания карточки» — Ctrl+Z сразу после набора удалял карточку целиком вместо того,
        /// чтобы отменить написанное. Один шаг на заход, а не на букву: то же правило, что у строк
        /// страницы (DocumentPageView.PushHistoryBeforeFirstEdit).</summary>
        public void HandleTextEditStarted(string objectId)
        {
            canvasController.BeforeMutation?.Invoke();
        }

        /// <summary>Набор закончился — карточка потеряла фокус. Помечает проект изменённым и перерисовывает
        /// страницу, как это делает конец любой другой правки на доске.</summary>
        public void HandleTextEditEnded(string objectId)
        {
            // Карточку могли снести прямо во время набора — отменой или пересборкой доски, и TMP шлёт
            // onEndEdit, когда его поле выключают. Тогда набор ничем не кончился: писать некуда и
            // перерисовывать незачем, а вызов отсюда посреди перестройки означал бы перестройку внутри
            // перестройки.
            if (FindObjectData(objectId) == null) return;
            canvasController.AfterMutation?.Invoke();
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
