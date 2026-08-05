using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable card showing a NoteCardData's title + body. Drag moves it within its
    /// parent canvas container; a plain click (no movement) fires OnClicked instead.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class NoteCardView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        NoteCardData data;
        RectTransform rect;
        TMP_Text titleText;
        TMP_InputField bodyField;

        /// <summary>Рамка карточки — она же корневой Image. Цвет ей ставит ApplyStyle из палитры, и
        /// ИМЕННО ПОЭТОМУ на неё не вешается тег темы: тег перекрасил бы её при смене темы, стерев
        /// выбор ДМ. Рабочая область (bodyBg) тег, наоборот, сохраняет и за темой следует.</summary>
        Image frameImage;
        RectTransform titleRoot;
        TMP_InputField titleField;
        TMP_Text titlePlaceholder;
        RectTransform bodyRect;
        TMP_Text bodyText;
        bool editable;

        /// <summary>Прозрачный щит поверх карточки. ДЕРЖИТ ОДИНОЧНЫЙ КЛИК НА САМОЙ КАРТОЧКЕ: пока он
        /// на месте, клик и протаскивание выделяют и двигают её, а не попадают в поле ввода. Раньше
        /// ручкой служила шапка, но заголовок стал редактируемым, и хвататься осталось не за что.
        ///
        /// Щит не обрабатывает нажатие и протаскивание вовсе — только двойной клик. Остальное
        /// всплывает к самой карточке, поэтому выделение, перенос и рамка размера работают как были.</summary>
        GameObject shield;

        /// <summary>Шаг отмены за этот заход в ЗАГОЛОВОК уже взят. Отдельный от textEditPushed:
        /// заголовок и текст — два разных поля, и заход в одно не закрывает заход в другое.</summary>
        bool titleEditPushed;

        /// <summary>The body when it is NOT editable. Exactly one of this and bodyField is ever non-null —
        /// which is what Refresh reads to know which half was built.</summary>
        TMP_Text bodyLabel;
        /// <summary>Шаг отмены за этот заход в текст уже взят. Сбрасывается по окончании набора, поэтому
        /// один заход = один шаг, сколько бы букв ни набрали.</summary>
        bool textEditPushed;

        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        /// <summary>When set, self-move-drag is only allowed while its ActiveTool is Select.</summary>
        public CanvasInteractionController interactionController;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        /// <summary>The DM has begun moving this object — raised on the FIRST movement of a press, not on the
        /// press itself, so a plain click does not spend an undo step.
        ///
        /// IT EXISTS ONLY SO THE UNDO SNAPSHOT IS TAKEN IN TIME. OnDragEnded fires after this view has already
        /// written the new position into the data, and a snapshot taken then would record the very state it
        /// is supposed to undo — the board would jump back to where it already is. See
        /// CanvasInteractionController.HandleObjectDragStarted.</summary>
        public event System.Action<string> OnDragStarted;

        /// <summary>Builds the card. `editable` is FALSE for a canvas shown inline in a page.
        ///
        /// NOT A COSMETIC SWITCH. A TMP_InputField is an IScrollHandler and swallows the wheel over itself
        /// (TMP_InputField.cs:2414-2423) — the exact defect Р2 spent a checkpoint round on for page rows. In
        /// the flow of a page the wheel ALWAYS belongs to the page, so the editing half simply is not built.
        /// The DM edits a card in the expanded view, where the page is not underneath it.
        ///
        /// TMP AND LITERATA, unlike every other panel in the app: a card on a board is body text of a page,
        /// which is exactly the boundary the arc's "panels do not migrate" rule draws. The toolbar, the
        /// inspectors and the confirm dialogs stay on LegacyRuntime.ttf.</summary>
        public void Initialize(NoteCardData cardData, RectTransform canvasContainer, bool editable)
        {
            data = cardData;
            this.editable = editable;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Фон карточки стал РАМКОЙ: рабочая область лежит поверх него и не достаёт до краёв на
            // CardChrome.BorderPx. Тега темы здесь больше нет — цвет приходит из палитры (см. frameImage).
            var bg = gameObject.AddComponent<Image>();
            bg.sprite = RoundedRectSprite.Get();
            bg.type = Image.Type.Sliced;
            frameImage = bg;

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(transform, false);
            titleRoot = titleGO.GetComponent<RectTransform>();
            titleRoot.anchorMin = new Vector2(0f, 1f);
            titleRoot.anchorMax = new Vector2(1f, 1f);
            titleRoot.pivot = new Vector2(0.5f, 1f);
            titleRoot.anchoredPosition = new Vector2(0f, -CardChrome.BorderPx);
            titleRoot.sizeDelta = new Vector2(-12f, CardChrome.HeaderHeightPx);

            var titleTextGO = new GameObject("Text", typeof(RectTransform));
            titleTextGO.transform.SetParent(titleGO.transform, false);
            titleText = titleTextGO.AddComponent<TextMeshProUGUI>();
            // Guarded rather than assigned outright, the way NotesTypography.ApplyBody guards: a missing
            // font asset is a real state (it complains loudly and returns null), and writing null into
            // TMP_Text.font turns a legible fallback into a NullReferenceException at first layout.
            if (NotesTypography.Bold != null) titleText.font = NotesTypography.Bold;
            titleText.fontSize = CardChrome.TitlePointSize;
            titleText.alignment = TextAlignmentOptions.Left;
            titleText.raycastTarget = false;
            // Тега темы у заголовка НЕТ намеренно: он лежит на цветной рамке, и его цвет считает
            // ApplyStyle по яркости этой рамки. Тег вернул бы его к цвету темы на первой же смене темы.
            var titleTextRect = titleTextGO.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.sizeDelta = Vector2.zero;

            var bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(transform, false);
            bodyRect = bodyGO.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(CardChrome.BorderPx, CardChrome.BorderPx);
            bodyRect.offsetMax = new Vector2(-CardChrome.BorderPx, -(CardChrome.BorderPx + CardChrome.HeaderHeightPx));

            // РАБОЧАЯ ОБЛАСТЬ ОСТАЁТСЯ ЦВЕТА ТЕМЫ — цвет ДМ задаёт только рамке. Поэтому тег Panel2
            // переезжает сюда с корневого фона, а не пропадает: иначе внутренность карточки застыла
            // бы в цвете, который был на момент сборки, и перестала бы следить за светлой/тёмной темой.
            var bodyBg = bodyGO.AddComponent<Image>();
            bodyBg.sprite = RoundedRectSprite.Get();
            bodyBg.type = Image.Type.Sliced;
            ThemeService.Tag(bodyBg, ThemeRole.Panel2, 0.95f);

            var bodyTextGO = new GameObject("Text", typeof(RectTransform));
            bodyTextGO.transform.SetParent(bodyGO.transform, false);
            bodyText = bodyTextGO.AddComponent<TextMeshProUGUI>();
            if (NotesTypography.Body != null) bodyText.font = NotesTypography.Body;
            bodyText.fontSize = CardChrome.BodyPointSize(data.FontSize);
            ThemeService.Tag(bodyText, ThemeRole.Txt);
            var bodyTextRect = bodyTextGO.GetComponent<RectTransform>();
            bodyTextRect.anchorMin = Vector2.zero;
            bodyTextRect.anchorMax = Vector2.one;
            bodyTextRect.offsetMin = new Vector2(4f, 4f);
            bodyTextRect.offsetMax = new Vector2(-4f, -4f);

            if (editable)
            {
                BuildTitleField(titleGO);

                // ВЫКЛЮЧАЕМ ОБЪЕКТ ДО AddComponent И ВКЛЮЧАЕМ ПОСЛЕ — это не осторожность, это единственный
                // способ вообще получить каретку. TMP_InputField создаёт объект каретки в OnEnable и только
                // при условии `m_TextComponent != null` (TMP_InputField.cs:1171). У живого объекта OnEnable
                // срабатывает прямо на AddComponent, когда textComponent ещё не присвоен, — условие ложно,
                // каретка не создаётся, а второго OnEnable уже не будет. Итог: печатать можно, выделять
                // можно, а НАРИСОВАТЬ каретку и подсветку выделения нечем. Там же читается и textViewport.
                //
                // DocBlockView.BuildField делает ровно это с самого начала и объясняет причину (строка 514);
                // карточка доски была написана мимо этого правила.
                bodyGO.SetActive(false);

                bodyField = bodyGO.AddComponent<TMP_InputField>();
                bodyField.targetGraphic = bodyBg;
                // TMP_InputField — это Selectable, и её ColorTint по умолчанию перекрашивает
                // targetGraphic на наведении и фокусе, воюя с ThemeService.Tag. Пока фон рабочей
                // области был прозрачным на 0.01, этого не было видно; теперь он непрозрачный, и без
                // этой строки карточка темнела бы под курсором. Та же ловушка — в NotesToolbar.cs:107.
                bodyField.transition = Selectable.Transition.None;
                // ОБЯЗАТЕЛЬНО, ХОТЯ ВЫГЛЯДИТ НЕОБЯЗАТЕЛЬНЫМ. TMP_InputField разыменовывает m_TextViewport
                // без единой проверки, как только курсор выделения уезжает за границу поля
                // (MouseDragOutsideRect, TMP_InputField.cs:1936) — а туда попадает всякий, кто тянет мышью
                // по тексту карточки и промахивается мимо её края. Без этой строки — NullReferenceException
                // каждый кадр протаскивания. DocBlockView задаёт viewport с самого начала (строка 540),
                // карточка доски — нет; здесь это собственный прямоугольник поля, отдельного viewport-узла
                // у неё нет.
                bodyField.textViewport = bodyRect;
                bodyField.textComponent = bodyText;
                bodyField.lineType = TMP_InputField.LineType.MultiLineNewline;
                // Kept from the legacy card, where it was supportRichText=false: a body the DM typed is
                // TEXT, and a stray '<' in it must stay a '<' rather than be eaten as markup. Set on the
                // FIELD, not the label — TMP_InputField pushes its own richText onto its text component.
                bodyField.richText = false;
                // ВИДНО, ГДЕ ПЕЧАТАЕШЬ И ЧТО ВЫДЕЛЕНО. По умолчанию каретка TMP — это одна серая линия в
                // цвет текста шириной 1 px, а доска рисуется в масштабе (зум меньше единицы делает её
                // тоньше пикселя и она пропадает). Акцентный цвет и двойная ширина держатся на любом зуме.
                //
                // Цвета взяты у темы разово, а не тегом: caretColor и selectionColor — обычные поля
                // TMP_InputField, а не Graphic, и ThemeService.Tag их не перекрашивает. Переключение
                // темы при открытой карточке оставит их прежними до следующей перестройки доски.
                bodyField.customCaretColor = true;
                bodyField.caretColor = ThemeService.Get(ThemeRole.Accent);
                bodyField.caretWidth = 2;
                var sel = ThemeService.Get(ThemeRole.Accent);
                bodyField.selectionColor = new Color(sel.r, sel.g, sel.b, 0.35f);
                // Off, for the same reason DocBlockView turns it off: on a writing surface it loses a
                // paragraph to one keystroke with no undo behind it.
                bodyField.restoreOriginalTextOnEscape = false;

                // ТЕКСТ ПИШЕТСЯ В ДАННЫЕ НА КАЖДОЙ БУКВЕ, А ШАГ ОТМЕНЫ БЕРЁТСЯ ОДИН РАЗ ЗА ЗАХОД.
                // Раньше набор не создавал шага отмены вовсе, поэтому верхним шагом оставалось «состояние
                // до создания карточки», и Ctrl+Z сразу после набора удалял карточку целиком.
                // Снимок берётся ДО присваивания data.Body — иначе он запомнит уже новый текст.
                bodyField.onValueChanged.AddListener(v =>
                {
                    // ТОЧКА ОТКАТА — НА ПЕРВОЙ БУКВЕ ЗАХОДА И НА КАЖДОМ ПРОБЕЛЕ, поэтому Ctrl+Z снимает
                    // написанное по слову, а не весь заход разом. Снимок берётся ДО присваивания
                    // data.Body: на пробеле после «привет» он держит «привет», и отмена возвращает
                    // законченное слово.
                    //
                    // Считаем ПРОБЕЛЫ, а не смотрим на последний символ: каретка бывает в середине текста,
                    // и вставка слова внутрь фразы — такая же законченная мысль, как дописывание в конец.
                    // Условие «стало больше» заодно молчит на удалении: стирание пробела точку не ставит.
                    bool wordFinished = CountWhitespace(v) > CountWhitespace(data.Body);
                    if (!textEditPushed || wordFinished)
                    {
                        textEditPushed = true;
                        if (interactionController != null) interactionController.HandleTextEditStarted(ObjectId);
                    }
                    data.Body = v;
                });
                bodyField.onEndEdit.AddListener(v =>
                {
                    data.Body = v;
                    if (!textEditPushed) return;
                    textEditPushed = false;
                    if (interactionController != null) interactionController.HandleTextEditEnded(ObjectId);
                });

                // БЕЗ УВЕДОМЛЕНИЯ: обычное присваивание text дёрнуло бы слушателя выше и записало бы в
                // историю шаг «карточку построили», которого ДМ не делал.
                bodyField.SetTextWithoutNotify(data.Body);

                // Теперь OnEnable отработает по полностью собранному полю — и создаст каретку.
                bodyGO.SetActive(true);

                BuildShield();
            }
            else
            {
                // No field, so no IScrollHandler, so the wheel over an inline board belongs to the page.
                bodyLabel = bodyText;
                bodyText.richText = false;
                bodyText.raycastTarget = false;
                bodyText.text = data.Body;
            }

            if (titleField == null) titleText.text = data.Title;
            Refresh();
        }

        /// <summary>Заголовок в развёрнутой доске редактируется так же, как текст карточки, и по тем же
        /// правилам: объект выключается до AddComponent (иначе TMP не создаст каретку — см. длинный
        /// комментарий у тела карточки), а шаг отмены берётся раз за заход и на каждом пробеле.</summary>
        void BuildTitleField(GameObject titleGO)
        {
            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(titleGO.transform, false);
            titlePlaceholder = placeholderGO.AddComponent<TextMeshProUGUI>();
            if (NotesTypography.Bold != null) titlePlaceholder.font = NotesTypography.Bold;
            titlePlaceholder.fontSize = CardChrome.TitlePointSize;
            titlePlaceholder.alignment = TextAlignmentOptions.Left;
            titlePlaceholder.raycastTarget = false;
            titlePlaceholder.text = "Заголовок";
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = Vector2.zero;

            titleGO.SetActive(false);

            titleField = titleGO.AddComponent<TMP_InputField>();
            titleField.transition = Selectable.Transition.None;
            titleField.textViewport = titleRoot;
            titleField.textComponent = titleText;
            titleField.placeholder = titlePlaceholder;
            titleField.lineType = TMP_InputField.LineType.SingleLine;
            titleField.richText = false;
            titleField.customCaretColor = true;
            titleField.caretColor = ThemeService.Get(ThemeRole.Accent);
            titleField.caretWidth = 2;
            var sel = ThemeService.Get(ThemeRole.Accent);
            titleField.selectionColor = new Color(sel.r, sel.g, sel.b, 0.35f);
            // Как и у тела карточки: Esc иначе теряет написанное, и позади этого нет шага отмены.
            titleField.restoreOriginalTextOnEscape = false;

            titleField.onValueChanged.AddListener(v =>
            {
                bool wordFinished = CountWhitespace(v) > CountWhitespace(data.Title);
                if (!titleEditPushed || wordFinished)
                {
                    titleEditPushed = true;
                    if (interactionController != null) interactionController.HandleTextEditStarted(ObjectId);
                }
                data.Title = v;
            });
            titleField.onEndEdit.AddListener(v =>
            {
                data.Title = v;
                if (!titleEditPushed) return;
                titleEditPushed = false;
                if (interactionController != null) interactionController.HandleTextEditEnded(ObjectId);
            });
            titleField.SetTextWithoutNotify(data.Title);

            titleGO.SetActive(true);
        }

        /// <summary>Пробелы, переводы строк и табуляции — всё, чем ДМ отделяет слово от слова.</summary>
        static int CountWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            foreach (var c in s)
                if (char.IsWhiteSpace(c)) n++;
            return n;
        }

        /// <summary>Собирается ПОСЛЕДНИМ и потому лежит поверх обоих полей: uGUI отдаёт клик тому, кто
        /// выше, а выше — тот, кто позже в списке детей.</summary>
        void BuildShield()
        {
            var shieldGO = new GameObject("Shield", typeof(RectTransform));
            shieldGO.transform.SetParent(transform, false);
            var shieldRect = shieldGO.GetComponent<RectTransform>();
            shieldRect.anchorMin = Vector2.zero;
            shieldRect.anchorMax = Vector2.one;
            shieldRect.sizeDelta = Vector2.zero;

            // Полностью прозрачный, но клики ловит: uGUI проверяет попадание в прямоугольник, а не в
            // пиксель картинки, пока не задан alphaHitTestMinimumThreshold.
            var img = shieldGO.AddComponent<Image>();
            img.color = Color.clear;

            var dbl = shieldGO.AddComponent<DoubleClickHandler>();
            dbl.OnDoubleClickAt = EnterEditMode;

            shield = shieldGO;
        }

        /// <summary>Двойной клик — вход в правку. Каретка ставится в то поле, по которому попали:
        /// в шапку — правится заголовок, ниже — текст.</summary>
        void EnterEditMode(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (shield == null) return;
            shield.SetActive(false);

            var cam = interactionController != null ? interactionController.uiCamera : null;
            bool onHeader = titleField != null && titleRoot != null && titleRoot.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(titleRoot, eventData.position, cam);

            if (onHeader)
            {
                titleField.Select();
                titleField.ActivateInputField();
            }
            else
            {
                FocusBody();
            }
        }

        /// <summary>Выход из правки: щит возвращается, каретка гаснет. Зовётся, когда с карточки сняли
        /// выделение — то есть кликнули по пустому месту доски или по другому объекту.
        ///
        /// Каретку снимаем ЯВНО. Поле ввода TMP не отпускает фокус само от того, что поверх него снова
        /// положили картинку: набор с клавиатуры продолжал бы идти в невидимую карточку.</summary>
        public void ExitEditMode()
        {
            if (shield == null || shield.activeSelf) return;
            if (titleField != null) titleField.DeactivateInputField();
            if (bodyField != null) bodyField.DeactivateInputField();
            shield.SetActive(true);
        }

        /// <summary>Ставит каретку в текст карточки — вызывается сразу после того, как карточку вставили,
        /// чтобы ДМ печатал без лишнего клика.
        ///
        /// Молча ничего не делает у карточки, построенной с editable: false — у неё поля ввода не
        /// существует вовсе (см. Initialize: в потоке страницы TMP_InputField съедал бы колесо). Это
        /// обычное состояние, а не ошибка.</summary>
        public void FocusBody()
        {
            if (bodyField == null) return;
            // Только что вставленная карточка сразу в правке — правило прошлого арка «вставил —
            // редактируешь». Без этой строки каретка встала бы под щит и первая буква пропала бы.
            if (shield != null) shield.SetActive(false);
            bodyField.Select();
            bodyField.ActivateInputField();
        }

        /// <summary>Красит рамку, подбирает цвет заголовка, ставит кегль текста и высоту шапки.
        ///
        /// ВЫЗЫВАЕТСЯ И ИЗ Refresh, а не только при сборке. Иначе отмена вернёт данные, а карточка
        /// останется перекрашенной — ровно та же ловушка, из-за которой прошлый арк чинил
        /// «отмена не доходит до вкладки доски».</summary>
        void ApplyStyle()
        {
            var frame = NotesPalette.At(data.FrameColorIndex);
            frameImage.color = new Color32(frame.R, frame.G, frame.B, 255);

            bool hasTitle = CardChrome.HasTitle(data.Title);
            float header = CardChrome.HeaderHeight(hasTitle, editable);
            titleRoot.gameObject.SetActive(header > 0f);
            titleRoot.sizeDelta = new Vector2(titleRoot.sizeDelta.x, header);

            // Цвет заголовка НЕ из темы: он лежит на рамке, а её цвет темы не знает.
            var ink = NotesPalette.PrefersDarkText(frame)
                ? new Color(0.12f, 0.12f, 0.14f)
                : new Color(0.96f, 0.96f, 0.98f);
            if (titleText != null) titleText.color = ink;
            if (titlePlaceholder != null) titlePlaceholder.color = new Color(ink.r, ink.g, ink.b, 0.4f);

            bodyText.fontSize = CardChrome.BodyPointSize(data.FontSize);
            bodyRect.offsetMin = new Vector2(CardChrome.BorderPx, CardChrome.BorderPx);
            bodyRect.offsetMax = new Vector2(-CardChrome.BorderPx, -(CardChrome.BorderPx + header));
        }

        public void Refresh()
        {
            if (data == null) return;
            if (titleField != null) titleField.SetTextWithoutNotify(data.Title);
            else titleText.text = data.Title;
            // Тоже без уведомления: перерисовка — это не правка ДМ, а слушатель onValueChanged взял бы за
            // неё шаг отмены и записал бы в данные то, что и так там лежит.
            if (bodyField != null) bodyField.SetTextWithoutNotify(data.Body);
            else if (bodyLabel != null) bodyLabel.text = data.Body;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
            ApplyStyle();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        bool CanSelfMove => interactionController == null || interactionController.ActiveTool == NotesTool.Select;

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanSelfMove) return;
            if (!dragging) OnDragStarted?.Invoke(data.Id);
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
