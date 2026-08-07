using System.Collections.Generic;
using UnityEngine;
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
    /// ПОЧЕМУ НЕ ГЛОБАЛЬНЫЙ ОВЕРЛЕЙ, КАК QuickOpenPopup. Тот палет открывается ГЛОБАЛЬНЫМ Ctrl+K и опрашивает
    /// клавиатуру САМ (его собственный Update, до LateUpdate чего бы то ни было ещё). Этот попап привязан к
    /// ОДНОЙ конкретной строке в момент, когда её поле уже в фокусе, и триггер («@», набранный посимвольно) —
    /// вопрос о ТЕКСТЕ строки, а не о хардварной клавише. DocKeyboardController уже читает live-строку, её
    /// текст и её каретку КАЖДЫЙ LateUpdate ПОСЛЕ TMP-дрейна (см. его класс-док) — это единственное место, где
    /// «текст только что стал таким-то» и «каретка теперь здесь» согласованы друг с другом, поэтому открытие/
    /// закрытие/сужение попапа управляется ОТТУДА (Update/Refresh/Close ниже — чистые команды без своего
    /// опроса клавиатуры), а не изнутри этого класса.
    ///
    /// ПОЗИЦИОНИРОВАНИЕ — ТОТ ЖЕ ПЕРЕВОД МИР→ЭКРАН→ЛОКАЛЬ, ЧТО У NavContextMenu.Show (NavigatorView.cs), с
    /// той же причиной для camera=null на ОБОИХ концах: страница живёт под ScreenSpaceOverlay-канвасом
    /// воркспейса (WorkspaceBuilder.cs, `canvas.renderMode = RenderMode.ScreenSpaceOverlay`), и попап строит
    /// СВОЙ собственный такой же канвас — см. CardPropertyBar.cs:128-132 про то, почему обе половины должны
    /// быть согласованы, а не взяты с потолка. «Под кареткой» реализовано как «под самой СТРОКОЙ» (левый-нижний
    /// угол её RectTransform), а не под точным пикселем символа: DocBlockView не отдаёт наружу x-координату
    /// каретки построчно (TryGetCaretLineWorldY отдаёт только верх/низ строки, без x — см. её класс-док), а
    /// заводить для этого новый публичный метод в DocBlockView ради одного вызова — расширение поверхности,
    /// которого бриф не просил. Разница на короткой строке (а запрос «@…» почти всегда короткий) незаметна.
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
        Transform listContent;

        readonly List<MentionCandidate> candidates = new List<MentionCandidate>();
        readonly List<Image> rowBackgrounds = new List<Image>();
        int highlighted = -1;
        /// <summary>Единственная строка «Создать персонажа "…"» вместо обычного списка — рулинг 3: «нет
        /// совпадений».</summary>
        bool creatingRow;

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
        /// сам GameObject канваса, а его невидимый full-screen backdrop продолжает глотать клики.</summary>
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

            BuildPopup(row);
            RunSearch();
        }

        /// <summary>Каждый следующий набранный (или стёртый) символ запроса — список пересчитывается
        /// целиком, без дебаунса, тем же приёмом, что QuickOpenPopup.RunSearch на каждое нажатие.</summary>
        public void Refresh(string newQuery)
        {
            if (popupGO == null) return;
            query = newQuery ?? "";
            RunSearch();
        }

        public void Close()
        {
            if (popupGO == null) return;
            popupGO.SetActive(false);   // клики не проходят сквозь backdrop этот же кадр, Destroy — в конце.
            Destroy(popupGO);
            popupGO = null;
            listContent = null;
            rowBackgrounds.Clear();
            candidates.Clear();
            highlighted = -1;
            creatingRow = false;
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

            if (documentController != null && pageView != null)
            {
                var doc = documentController.Document;
                var current = pageView.Page;
                var world = pageView.WorldSource != null ? pageView.WorldSource() : null;
                candidates.AddRange(MentionSuggest.Rank(doc, current, world, query, recentIds, Limit));
            }

            // Рулинг 3: нет совпадений — единственный пункт «Создать персонажа "…"». «Нет совпадений»
            // читается буквально: пустой список от Rank, каким бы ни был запрос (в т.ч. пустым — «Создать
            // персонажа ""» тогда просто получит имя по умолчанию, см. CharacterOps.CreateCharacter).
            if (candidates.Count == 0) creatingRow = true;

            highlighted = (creatingRow || candidates.Count > 0) ? 0 : -1;
            RebuildRows();
        }

        // ── построение ──────────────────────────────────────────────────────────────────────────────

        void BuildPopup(DocBlockView row)
        {
            var canvasGO = new GameObject(CanvasName, typeof(RectTransform));
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            popupGO = canvasGO;

            // Клик мимо попапа закрывает его — тот же выбор и та же причина, что у QuickOpenPopup/
            // NavContextMenu: обычное десктопное поведение всплывающего списка, а рискa «случайно стереть
            // что-то важное» тут нет (закрытие ничего не удаляет — набранный текст остаётся, рулинг 4).
            var backdropGO = new GameObject("Backdrop", typeof(RectTransform));
            backdropGO.transform.SetParent(canvasGO.transform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = Color.clear;
            var backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.onClick.AddListener(Close);
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            var panelGO = new GameObject("Panel", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);   // после backdrop → выигрывает рейкаст.
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel2);
            var panelRect = panelGO.GetComponent<RectTransform>();
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

            Canvas.ForceUpdateCanvases();   // тот же приём, что NavContextMenu.Show — размеры должны
                                             // осесть ДО того, как ниже читается позиция строки на экране.

            PositionUnder(row, canvasGO.GetComponent<RectTransform>(), panelRect);
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

        /// <summary>Мир→экран→локаль по строке `row`, тот же перевод, что у NavContextMenu.Show — см.
        /// класс-док этого файла про camera=null на обоих концах. Свисает вниз от левого-нижнего угла
        /// строки, зажато в границы экрана тем же приёмом (PoiInfoPopup.Reposition), что уже используется
        /// в этом проекте для точечно позиционируемых попапов.</summary>
        void PositionUnder(DocBlockView row, RectTransform canvasRect, RectTransform panelRect)
        {
            var rowRect = row.transform as RectTransform;
            if (rowRect == null) { panelRect.anchoredPosition = Vector2.zero; return; }

            var corners = new Vector3[4];
            rowRect.GetWorldCorners(corners);
            Vector3 bottomLeft = corners[0];   // 0 — левый нижний, тот же порядок, что и везде в проекте.

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, bottomLeft);
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
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    rowBackgrounds.Add(BuildRow(i, c.Name, c.Subtitle, i == highlighted, isCreate: false));
                }
            }
        }

        Image BuildRow(int index, string name, string subtitle, bool isHighlighted, bool isCreate)
        {
            var rowGO = new GameObject($"Row_{index}", typeof(RectTransform));
            rowGO.transform.SetParent(listContent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            rowGO.AddComponent<RectMask2D>();

            var bg = rowGO.AddComponent<Image>();
            TagRowBackground(bg, isHighlighted);

            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            int capturedIndex = index;
            btn.onClick.AddListener(() => Choose(isCreate ? 0 : capturedIndex));

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

            // Подпись — рулинг 6: различает два объекта с одинаковым именем. Пустая строка у обычной
            // страницы (MentionCandidate.Subtitle = "") просто рисует ничего, без пустой полки под именем —
            // тот же компромисс, на который уже пошёл QuickOpenPopup (Snippet ?? Kind, «может быть пустым»).
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
    }
}
