using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Задача 10б: всплывающий список под кареткой, открываемый «@» прямо в строке. Рендерит
    /// MentionSuggest.Rank — ранжирование НЕ переигрывается здесь, ровно как QuickOpenPopup не переигрывает
    /// QuickOpen.Search (см. класс-док того файла, тот же принцип, тот же повод).
    ///
    /// ФИКС-РАУНД 2 (ревью нашло: «один первопричинный баг, три симптома»). Раньше ЖИВОСТЬ попапа была
    /// завязана на «поле в фокусе ПРЯМО СЕЙЧАС» — а Enter деактивирует поле ДО того, как обработчик вообще
    /// успевает среагировать (MultiLineSubmit — см. DocKeyboardController.cs, класс-док про «THE DRAIN»),
    /// клик по строке попапа снимает фокус с поля НА ТОМ ЖЕ pointer-down, которым сам клик и происходит, а
    /// стрелки уже двигают каретку TMP-дрейном раньше, чем этот класс успевает её прочитать. Итог был один:
    /// три способа «выбрать» ломали ровно то, что должны были выбирать.
    ///
    /// ИСПРАВЛЕНО ПО СВОЙСТВУ, А НЕ ПОРЯДКОМ ВЫЗОВОВ. Открытый попап теперь не закрывается просто оттого,
    /// что поле не в фокусе ЭТИМ КОНКРЕТНЫМ кадром — DocKeyboardController.UpdateMentionPopup при
    /// live==null сверяется с DocBlock.Text НАПРЯМУЮ (авторитетный источник, пишется синхронно из
    /// OnFieldChanged), а не с живой кареткой поля. Явные события — Esc, пробел, текст перестал совпадать,
    /// выбор, фокус ушёл на ДРУГУЮ строку — закрывают его сами по себе, каждое своим путём. Плюс порядок:
    /// DocKeyboardController перехватывает Enter/↑/↓/Esc САМЫМ ПЕРВЫМ действием кадра, до всего, что могло
    /// бы закрыть попап как побочный эффект. Строка списка выбирается по POINTER-DOWN (см. PointerDownRelay
    /// ниже), а не по Button.onClick (который стреляет на pointer-up кадром позже, когда попап уже мёртв).
    ///
    /// ПОЗИЦИОНИРОВАНИЕ — ПОД КАРЕТКОЙ, А НЕ ПОД СТРОКОЙ. TMP_InputField.textComponent ПУБЛИЧНЫЙ (это
    /// обычное свойство UnityEngine, не что-то, что нужно было бы открывать в DocBlockView заново), так что
    /// точная геометрия символа (`textInfo.characterInfo[i]`) достижима отсюда напрямую через
    /// `DocBlockView.Field.textComponent` — см. TryGetCaretWorldPos, повторяющий ту же арифметику, что уже
    /// есть в DocBlockView.TryGetCaretLineWorldY/TryGetDropMarker (ci.origin/xAdvance, line.descender), но
    /// на ПУБЛИЧНОМ объекте вместо приватного поля того класса. Измеряется ПОСЛЕ того, как строки списка
    /// уже построены (RepositionUnder зовётся из Open/Refresh ПОСЛЕ RunSearch), а не во время BuildPopup,
    /// когда список ещё пуст, — иначе клэмп к низу экрана считался бы по высоте одного футера и никогда не
    /// пересчитывался бы заново при сужении списка.
    ///
    /// БЕЗ BACKDROP'А. Это инлайн-автодополнение внутри живой строки, а не модальная палитра: полноэкранный
    /// невидимый перехватчик кликов означал бы, что клик В ДРУГУЮ строку, пока ДМ просто печатает где-то ещё,
    /// стоит двух кликов и не ставит каретку с первого раза. QuickOpenPopup может себе это позволить (это
    /// модальная палитра, поле ввода которой — единственное, что вообще есть на экране в этот момент); этот
    /// попап — нет. Закрытие без клика-мимо остаётся: Esc, пробел, несовпадение текста, выбор, уход фокуса
    /// на другую строку — см. DocKeyboardController.UpdateMentionPopup.
    /// </summary>
    public class MentionPopup : MonoBehaviour
    {
        const float Width = 260f;
        const float RowHeight = 34f;
        const float FooterHeight = 20f;
        /// <summary>Тот же порядок, что и у DM-рулинга «2-3 самых подходящих» и у брифа задачи 10а/10б:
        /// «пока ничего не набрано — три строки».</summary>
        const int Limit = 3;
        /// <summary>«Небольшой размер» из брифа Шага 4 — сессия редко успевает вставить больше нескольких
        /// ссылок между двумя открытиями попапа, и не нужно хранить больше, чем Limit может когда-либо
        /// показать разом.</summary>
        const int RecentCap = 8;

        const int CanvasSortingOrder = 4000;   // тот же уровень, что у QuickOpenPopup — тем более что оба
                                                // никогда не открыты одновременно (см. DocKeyboardController).
        const string CanvasName = "MentionPopupCanvas";

        NotesDocumentController documentController;
        DocumentPageView pageView;
        Font builtinFont;

        GameObject popupGO;
        RectTransform panelRect;
        Transform listContent;

        readonly List<MentionCandidate> candidates = new List<MentionCandidate>();
        readonly List<Image> rowBackgrounds = new List<Image>();
        int highlighted = -1;
        /// <summary>Единственная строка «Создать персонажа "…"» вместо обычного списка — рулинг 3: «нет
        /// совпадений». Только когда `query` НЕ пуст — см. RunSearch.</summary>
        bool creatingRow;
        /// <summary>Пустой запрос и нечего предложить (свежая страница, пустая сессия) — тихая подсказка
        /// вместо «Создать персонажа ""», которая иначе была бы первым впечатлением ДМ от фичи.</summary>
        bool showingHint;

        string blockId;
        int atIndex = -1;
        string query = "";

        /// <summary>«kind:id», самые недавние — первыми, без повторов. Только в памяти этого экземпляра —
        /// см. класс-док про RecentCap и отчёт задачи для того, где это живёт между переключениями страниц
        /// и почему не переживает перезапуск (MentionPopup — обычный компонент сцены, не ассет и не файл
        /// проекта; его поля стираются вместе с доменной перезагрузкой/перезапуском Play Mode ровно как у
        /// любого другого раннтайм-состояния этого слоя).</summary>
        readonly List<string> recentIds = new List<string>();

        public bool IsOpen => popupGO != null;
        public string BlockId => blockId;
        public int AtIndex => atIndex;
        public string Query => query;

        /// <summary>REUSE-OR-ADD, тот же приём, что QuickOpenPopup.Attach — WorkspaceBuilder/NotesRootBuilder
        /// пере-запускают своё построение при каждой Play-mode пересборке, и второй AddComponent завёл бы
        /// вторую копию, полностью не в курсе первой.</summary>
        public static MentionPopup Attach(GameObject host, NotesDocumentController documentController, DocumentPageView pageView)
        {
            var existing = host.GetComponent<MentionPopup>();
            var popup = existing != null ? existing : host.AddComponent<MentionPopup>();
            popup.Close();
            DestroyStrandedCanvas();
            popup.documentController = documentController;
            popup.pageView = pageView;
            popup.builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return popup;
        }

        /// <summary>Тот же приём, что QuickOpenPopup.DestroyStrandedCanvas — канвас попапа корневой (не
        /// дитя ничего, см. BuildPopup), домен-перезагрузка стирает `popupGO` на живом компоненте, но не
        /// сам GameObject канваса. Без backdrop'а он больше не глотает клики сам по себе, но всё ещё стоит
        /// над всем остальным (CanvasSortingOrder) и продолжал бы показывать мёртвый список.</summary>
        static void DestroyStrandedCanvas()
        {
            var stranded = GameObject.Find(CanvasName);
            if (stranded == null) return;
            stranded.SetActive(false);
            Destroy(stranded);
        }

        void OnDestroy() => Close();

        // ── открытие / обновление / закрытие — команды, вызываемые ИЗ DocKeyboardController ──────────

        /// <summary>Открывает список над строкой `row`, чей текст только что получил «@» на позиции
        /// `atIndex`. `query` — то, что уже набрано после «@» (обычно "" в момент открытия).</summary>
        public void Open(DocBlockView row, int atIndex, string query)
        {
            if (row == null || string.IsNullOrEmpty(row.BlockId)) return;
            Close();   // защитно — вызывающая сторона и так не должна звать Open поверх открытого попапа.

            blockId = row.BlockId;
            this.atIndex = atIndex;
            this.query = query ?? "";

            BuildPopup();
            RunSearch();
            RepositionUnder(row);
        }

        /// <summary>Каждый следующий набранный (или стёртый) символ запроса — список пересчитывается
        /// целиком, без дебаунса, тем же приёмом, что QuickOpenPopup.RunSearch на каждое нажатие. `row` —
        /// нужен только для перепозиционирования (каретка могла сдвинуться по x/y), НИКОГДА не сохраняется
        /// полем: между вызовами этот класс не держит ни одной ссылки на DocBlockView, только строковый
        /// BlockId — та же причина, по которой Open тоже не сохраняет `row`.
        ///
        /// РАННИЙ ВЫХОД, КОГДА ЗАПРОС НЕ ИЗМЕНИЛСЯ — И ЭТО НЕ КОСМЕТИКА. DocKeyboardController зовёт этот
        /// метод КАЖДЫЙ LateUpdate, пока попап открыт и строка в фокусе, — не только когда что-то набрано.
        /// Без сравнения строк RunSearch (а с ней highlighted = 0 и полная пересборка строк) запускалась бы
        /// каждый кадр и стирала бы любое движение ↑/↓ мгновением позже — та же ловушка, которую бриф
        /// называет прямо про TMP_InputField («сравнивать строки, а не полагаться на факт события»), только
        /// уровнем выше.</summary>
        public void Refresh(string newQuery, DocBlockView row)
        {
            if (popupGO == null) return;
            newQuery = newQuery ?? "";
            if (newQuery == query) return;
            query = newQuery;
            RunSearch();
            if (row != null) RepositionUnder(row);
        }

        public void Close()
        {
            if (popupGO == null) return;
            popupGO.SetActive(false);   // на случай будущего Destroy-в-конце-кадра — дешёвая страховка.
            Destroy(popupGO);
            popupGO = null;
            panelRect = null;
            listContent = null;
            rowBackgrounds.Clear();
            candidates.Clear();
            highlighted = -1;
            creatingRow = false;
            showingHint = false;
            blockId = null;
            atIndex = -1;
            query = "";
        }

        public void MoveHighlight(int delta)
        {
            int count = creatingRow ? 1 : candidates.Count;
            if (count == 0) { highlighted = -1; return; }
            highlighted = ((highlighted + delta) % count + count) % count;
            RefreshHighlight();
        }

        public void ChooseHighlighted() => Choose(highlighted);

        // ── выбор ───────────────────────────────────────────────────────────────────────────────────

        void Choose(int index)
        {
            if (pageView == null || string.IsNullOrEmpty(blockId)) { Close(); return; }

            // Снимок ДО Close() — Close() стирает blockId/atIndex/query, а вставлять нужно ровно туда,
            // где стоял «@» и набранный после него текст (см. ReplaceRangeWithToken).
            string savedBlockId = blockId;
            int savedStart = atIndex;
            int savedEnd = atIndex + 1 + query.Length;
            string savedQuery = query;

            if (creatingRow)
            {
                Close();   // закрыт ДО мутации документа — тот же порядок, что у QuickOpenPopup.ChooseIndex.
                if (documentController == null) return;
                var page = CharacterOps.CreateCharacter(documentController.Document, savedQuery);
                if (page == null) return;
                documentController.NotifyDocumentChanged();
                pageView.ReplaceRangeWithToken(savedBlockId, savedStart, savedEnd, NotesLinkOps.KindPage, page.Id, page.Name);
                RememberRecent(NotesLinkOps.KindPage, page.Id);
                return;
            }

            if (index < 0 || index >= candidates.Count) { Close(); return; }
            var c = candidates[index];
            Close();
            pageView.ReplaceRangeWithToken(savedBlockId, savedStart, savedEnd, c.Kind, c.Id, c.Name);
            RememberRecent(c.Kind, c.Id);
        }

        /// <summary>Последние вставленные — первыми, без повторов, ограничены RecentCap. «kind:id» —
        /// формат, который сам MentionSuggest документирует как свой контракт (MentionSuggest.cs, класс-док,
        /// «RECENTIDS' ФОРМАТ»); строится здесь, потому что задача 10а сознательно оставила это следующей
        /// задаче.</summary>
        void RememberRecent(string kind, string id)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id)) return;
            string key = kind + ":" + id;
            recentIds.RemoveAll(r => r == key);
            recentIds.Insert(0, key);
            while (recentIds.Count > RecentCap) recentIds.RemoveAt(recentIds.Count - 1);
        }

        // ── поиск / список ──────────────────────────────────────────────────────────────────────────

        void RunSearch()
        {
            candidates.Clear();
            creatingRow = false;
            showingHint = false;

            if (documentController != null && pageView != null)
            {
                var doc = documentController.Document;
                var current = pageView.Page;
                var world = pageView.WorldSource != null ? pageView.WorldSource() : null;
                candidates.AddRange(MentionSuggest.Rank(doc, current, world, query, recentIds, Limit));
            }

            if (candidates.Count == 0)
            {
                // Рулинг 3 — «нет совпадений» читается как «нет совпадений НА ЗАПРОС»: пустой запрос — это
                // ПОКА-НИЧЕГО-НЕ-НАБРАНО, а не запрос, которому ничего не совпало. «Создать персонажа ""»
                // от одного «@» на свежей странице была бы первым впечатлением ДМ от фичи — вместо этого
                // тихая подсказка (или ничего интерактивного вовсе).
                if (!string.IsNullOrWhiteSpace(query)) creatingRow = true;
                else showingHint = true;
            }

            highlighted = creatingRow || candidates.Count > 0 ? 0 : -1;
            RebuildRows();
        }

        // ── построение ──────────────────────────────────────────────────────────────────────────────

        void BuildPopup()
        {
            var canvasGO = new GameObject(CanvasName, typeof(RectTransform));
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            popupGO = canvasGO;

            // НЕТ backdrop'а — см. класс-док, «БЕЗ BACKDROP'А». Панель — единственный ребёнок канваса.
            var panelGO = new GameObject("Panel", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel2);
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);   // левый верхний — список свисает ВНИЗ-вправо от точки.
            panelRect.sizeDelta = new Vector2(Width, 0f);

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 2, 2);
            vlg.spacing = 0f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var listGO = new GameObject("List", typeof(RectTransform));
            listGO.transform.SetParent(panelGO.transform, false);
            var listVlg = listGO.AddComponent<VerticalLayoutGroup>();
            listVlg.childControlWidth = true;
            listVlg.childForceExpandWidth = true;
            listVlg.childControlHeight = true;   // применяет preferredHeight каждой строки к её rect —
            listVlg.childForceExpandHeight = false;   // см. QuickOpenPopup.BuildListContainer, та же пара.
            listVlg.spacing = 0f;
            listGO.AddComponent<LayoutElement>();   // preferredHeight не задаём — растёт по строкам сам.
            listContent = listGO.transform;

            BuildFooter(panelGO.transform);

            // ПОЗИЦИОНИРОВАНИЕ ЗДЕСЬ НЕ СЧИТАЕТСЯ — список ещё пуст (RunSearch не запускался), см. Open/
            // Refresh и RepositionUnder про то, почему это переехало на «после того, как строки построены».
        }

        void BuildFooter(Transform parent)
        {
            var go = new GameObject("Footer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = FooterHeight;

            var text = go.AddComponent<Text>();
            text.text = "↑↓ — выбор · Enter — вставить · Esc — закрыть";
            text.font = builtinFont;
            text.fontSize = 10;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
        }

        /// <summary>Измеряет и позиционирует ПОСЛЕ того, как строки списка уже существуют (RunSearch уже
        /// отработал) — иначе клэмп к низу экрана считается по высоте одного футера и не пересчитывается
        /// при сужении списка (это и была одна из четырёх меньших находок ревью фикс-раунда 2).</summary>
        void RepositionUnder(DocBlockView row)
        {
            if (popupGO == null || panelRect == null || row == null) return;

            Canvas.ForceUpdateCanvases();   // тот же приём, что NavContextMenu.Show — размеры должны осесть
                                             // ДО того, как ниже читается позиция на экране и высота панели.

            Vector3 anchorWorld;
            if (!TryGetCaretWorldPos(row.Field, out anchorWorld))
            {
                // Резервный путь — левый-нижний угол СТРОКИ, как было в первой сдаче: срабатывает, только
                // если у поля почему-то нет измеримого textComponent/textInfo (не должно происходить на
                // живой отредактируемой строке, но дешевле выстоять, чем упасть).
                var rowRect = row.transform as RectTransform;
                if (rowRect == null) { panelRect.anchoredPosition = Vector2.zero; return; }
                var corners = new Vector3[4];
                rowRect.GetWorldCorners(corners);
                anchorWorld = corners[0];
            }

            PositionAt(anchorWorld, popupGO.GetComponent<RectTransform>());
        }

        /// <summary>Мировая позиция низа каретки в живом поле ввода строки — та же арифметика, что уже есть
        /// в DocBlockView.TryGetCaretLineWorldY/TryGetDropMarker (idx = clamp(caretPosition, 0, count-1),
        /// x = afterLast ? ci.origin+ci.xAdvance : ci.origin, y = line.descender), но здесь читается через
        /// ПУБЛИЧНЫЙ `TMP_InputField.textComponent` — обычное свойство UnityEngine, а не что-то, что
        /// потребовало бы нового публичного метода в DocBlockView. Дублирует, а не переиспользует ту
        /// арифметику: DocBlockView-шная версия — приватный instance-метод над приватным полем `fieldText`,
        /// делить с ним код значило бы либо открывать этот метод наружу ради одного вызова, либо копировать
        /// формулу сюда — выбрано второе, дешевле и без расширения поверхности DocBlockView.</summary>
        static bool TryGetCaretWorldPos(TMP_InputField field, out Vector3 pos)
        {
            pos = Vector3.zero;
            var textComp = field != null ? field.textComponent : null;
            if (textComp == null) return false;

            var rt = textComp.rectTransform;
            if (rt == null) return false;

            var info = textComp.textInfo;
            if (info == null || info.characterCount == 0 || info.lineCount == 0)
            {
                // Пустая строка/ещё не размечено — её собственный рект, левый-нижний угол.
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                pos = corners[0];
                return true;
            }

            bool afterLast = field.caretPosition >= info.characterCount;
            int idx = Mathf.Clamp(afterLast ? info.characterCount - 1 : field.caretPosition, 0, info.characterCount - 1);
            var ci = info.characterInfo[idx];
            float x = afterLast ? ci.origin + ci.xAdvance : ci.origin;
            int lineNumber = Mathf.Clamp(ci.lineNumber, 0, info.lineCount - 1);
            var line = info.lineInfo[lineNumber];
            pos = rt.TransformPoint(new Vector3(x, line.descender, 0f));
            return true;
        }

        /// <summary>Мир→экран→локаль, тот же перевод, что у NavContextMenu.Show (NavigatorView.cs), с той
        /// же причиной для camera=null на ОБОИХ концах: страница живёт под ScreenSpaceOverlay-канвасом
        /// воркспейса (WorkspaceBuilder.cs, `canvas.renderMode = RenderMode.ScreenSpaceOverlay`), и этот
        /// попап строит СВОЙ собственный такой же канвас — см. CardPropertyBar.cs:128-132 про то, почему обе
        /// половины должны быть согласованы.</summary>
        void PositionAt(Vector3 anchorWorld, RectTransform canvasRect)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, anchorWorld);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);

            float menuHeight = LayoutUtility.GetPreferredHeight(panelRect);
            float halfW = Screen.width * 0.5f;
            float halfH = Screen.height * 0.5f;
            const float Margin = 4f;
            float x = Mathf.Clamp(local.x, -halfW + Margin, halfW - Width - Margin);
            float y = Mathf.Clamp(local.y, -halfH + menuHeight + Margin, halfH - Margin);
            panelRect.anchoredPosition = new Vector2(x, y);
        }

        void RebuildRows()
        {
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            rowBackgrounds.Clear();

            if (creatingRow)
            {
                rowBackgrounds.Add(BuildRow(0, "Создать персонажа «" + query + "»", "", isHighlighted: true, isCreate: true));
            }
            else if (showingHint)
            {
                BuildHintRow();
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    rowBackgrounds.Add(BuildRow(i, c.Name, c.Subtitle, i == highlighted, isCreate: false));
                }
            }
        }

        /// <summary>Рулинг 5 — пустой запрос, нечего предложить: тихая, некликабельная строка вместо
        /// «Создать персонажа ""». Не входит в `rowBackgrounds` — не участвует в подсветке/выборе.</summary>
        void BuildHintRow()
        {
            var go = new GameObject("Hint", typeof(RectTransform));
            go.transform.SetParent(listContent, false);
            go.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            var text = go.AddComponent<Text>();
            text.text = "Начните печатать имя…";
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
        }

        Image BuildRow(int index, string name, string subtitle, bool isHighlighted, bool isCreate)
        {
            var rowGO = new GameObject($"Row_{index}", typeof(RectTransform));
            rowGO.transform.SetParent(listContent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            rowGO.AddComponent<RectMask2D>();

            var bg = rowGO.AddComponent<Image>();
            TagRowBackground(bg, isHighlighted);

            // POINTER-DOWN, НЕ Button.onClick — см. PointerDownRelay и класс-док «ИСПРАВЛЕНО ПО СВОЙСТВУ».
            var relay = rowGO.AddComponent<PointerDownRelay>();
            int capturedIndex = index;
            relay.OnDown = () => Choose(isCreate ? 0 : capturedIndex);

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(rowGO.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.text = name ?? "";
            nameText.font = builtinFont;
            nameText.fontSize = 13;
            ThemeService.Tag(nameText, isCreate ? ThemeRole.Accent : ThemeRole.Txt);
            nameText.alignment = TextAnchor.UpperLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            nameText.raycastTarget = false;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(10f, -20f);
            nameRect.offsetMax = new Vector2(-10f, -4f);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subGO = new GameObject("Subtitle", typeof(RectTransform));
                subGO.transform.SetParent(rowGO.transform, false);
                var subText = subGO.AddComponent<Text>();
                subText.text = subtitle;
                subText.font = builtinFont;
                subText.fontSize = 10;
                ThemeService.Tag(subText, ThemeRole.Mut);
                subText.alignment = TextAnchor.UpperLeft;
                subText.horizontalOverflow = HorizontalWrapMode.Overflow;
                subText.verticalOverflow = VerticalWrapMode.Truncate;
                subText.raycastTarget = false;
                var subRect = subGO.GetComponent<RectTransform>();
                subRect.anchorMin = new Vector2(0f, 1f);
                subRect.anchorMax = new Vector2(1f, 1f);
                subRect.pivot = new Vector2(0f, 1f);
                subRect.offsetMin = new Vector2(10f, -32f);
                subRect.offsetMax = new Vector2(-10f, -20f);
            }

            return bg;
        }

        void RefreshHighlight()
        {
            for (int i = 0; i < rowBackgrounds.Count; i++)
            {
                if (rowBackgrounds[i] == null) continue;
                TagRowBackground(rowBackgrounds[i], i == highlighted);
            }
        }

        static void TagRowBackground(Image bg, bool isHighlighted)
        {
            if (isHighlighted) ThemeService.Tag(bg, ThemeRole.AccentSoft, 0.9f);
            else ThemeService.Tag(bg, ThemeRole.Panel, 0f);
        }

        /// <summary>Реагирует на POINTER-DOWN, а не на Button.onClick. Button.onClick стреляет на pointer-UP
        /// — а pointer-DOWN где угодно ВНЕ поля уже снимает с него фокус в ТОМ ЖЕ Update-проходе
        /// EventSystem'а, которым сам этот down случился; DocKeyboardController.LateUpdate этого же кадра
        /// (после Update) увидел бы «поле не в фокусе» и — до фикс-раунда 2 — закрывал бы попап ДО того,
        /// как pointer-UP вообще случался. Реагируя на pointer-DOWN, выбор строки происходит В ТОМ ЖЕ
        /// Update-проходе, что и сам клик, — раньше любого LateUpdate этого кадра, так что попап физически
        /// не может быть закрыт побочным эффектом расфокусировки раньше, чем успевает выбрать.</summary>
        class PointerDownRelay : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnDown;
            public void OnPointerDown(PointerEventData eventData) => OnDown?.Invoke();
        }
    }
}
