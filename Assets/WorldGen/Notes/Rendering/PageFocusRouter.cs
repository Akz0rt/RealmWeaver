using UnityEngine.EventSystems;
using WorldGen.Workspace.Rendering;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Задача 5 арки «две страницы рядом»: ОДИН вопрос — «чьи сейчас клавиши» — и одно место, где на него
    /// отвечают. Задача 6 добавила к ответу третий вариант («развёрнутой доски в панели N») и второго
    /// спрашивающего (CanvasInteractionController), но не второй разбор: разбор по-прежнему один —
    /// ResolveFocus, — а публичных вопросов к нему три, потому что владелец НАБОРА и владелец ИСТОРИИ
    /// ОТМЕНЫ, пока ДМ работает в доске, честно разные. С задачи 4 вид страницы строится ПО ЭКЗЕМПЛЯРУ НА
    /// ПАНЕЛЬ (PageSurfaceHost), так что обработчик клавиш, попап «@» и Ctrl+F больше не могут быть
    /// проведены к «тому самому единственному виду» — до этой задачи они были жёстко привязаны к панели 0,
    /// и печать во второй панели проваливалась в голые TMP-поля: текст правился, а Enter/Tab/Backspace/
    /// отмена не доходили до списка блоков.
    ///
    /// ОПРОС, А НЕ ПОЛЕ «ТЕКУЩИЙ ВИД», и это главное решение этого класса. Поле пришлось бы обновлять на
    /// КАЖДОМ пути смены фокуса — клик по вкладке, клик в содержимое другой панели, продвижение панели при
    /// закрытии вкладки, восстановление раскладки из настроек, перетаскивание вкладки в другую панель,
    /// «↗» разворачивающая доску в соседнюю панель — и одного забытого пути хватает, чтобы поле
    /// разошлось с действительностью НАВСЕГДА. Промах при этом выглядит не как «ничего не работает», а как
    /// «отмена прилетела в чужую панель», то есть как порча данных на глазах у ДМ. Опрос каретки такого
    /// состояния не имеет вовсе: он читает то, что есть ПРЯМО СЕЙЧАС, и потому самовосстанавливается —
    /// любой путь, который мы не предусмотрели, уже учтён, потому что каретка после него стоит там, где
    /// стоит.
    ///
    /// ЖИВЁТ РОВНО СТОЛЬКО, СКОЛЬКО ХОСТ, КОТОРЫЙ ОБОРАЧИВАЕТ. Обычный C#-класс (не MonoBehaviour):
    /// WorkspaceBuilder.Awake создаёт его рядом с PageSurfaceHost и раздаёт ссылку на него, а следующая
    /// пересборка оболочки создаёт и хост, и маршрутизатор заново — так что указать на снесённый
    /// PageSurfaceHost он не может в принципе. Это тот же довод, которым SurfaceRegistry обходится без
    /// «восстановления после перезагрузки домена».
    ///
    /// СЛОЙ. Это первая ссылка из WorldGen.Notes.Rendering в WorldGen.Workspace.Rendering (до сих пор
    /// notes-слой знал только WorldGen.Workspace.Data — см. MentionSuggest, DocumentPageView). Сделано
    /// сознательно и по заданию: вопрос «какая панель активна» ОТНОСИТСЯ к оболочке, и заводить ради него
    /// три делегата-переходника значило бы прятать ту же самую зависимость, а не убирать её. Чистая
    /// половина notes-слоя (WorldGen.Notes.Data) этим не задета — офлайн-харнесс компилирует только её.
    /// </summary>
    public class PageFocusRouter
    {
        readonly PageSurfaceHost host;
        readonly CanvasSurfaceHost canvasHost;
        readonly WorkspaceController controller;

        public PageFocusRouter(PageSurfaceHost host, CanvasSurfaceHost canvasHost, WorkspaceController controller)
        {
            this.host = host;
            this.canvasHost = canvasHost;
            this.controller = controller;
        }

        /// <summary>Вид, которому принадлежат КЛАВИШИ НАБОРА: вид с кареткой, при отсутствии каретки — вид
        /// АКТИВНОЙ панели; null, если такого вида нет вовсе. Разбор — в ResolveFocus ниже, здесь только
        /// один из трёх его ответов.
        ///
        /// NULL — ЗАКОННЫЙ ОТВЕТ, а не дыра. Если активная панель показывает не страницу, у клавиш
        /// страницы владельца нет: ноль владельцев здесь честнее, чем «отдадим соседней панели» —
        /// последнее вернуло бы ровно ту отмену-не-в-той-панели, ради которой задача и делается. Ровно ДВА
        /// владельца невозможны структурно: DocKeyboardController в проекте один (NotesRootBuilder
        /// AddComponent-ит его единожды), и он спрашивает этот метод один раз за кадр.
        ///
        /// РАЗВЁРНУТАЯ ДОСКА ОТВЕЧАЕТ ЗДЕСЬ «НИКОМУ», И ЭТО НЕ ДЫРА, А СУЖЕНИЕ (задача 6). Доска (Р4) —
        /// это БЛОК страницы, и её карточки — обычные TMP-поля, не строки страницы. Пока ДМ печатает в
        /// карточке, ни один вид страницы клавиш не получает: иначе Enter, дошедший до
        /// DocKeyboardController, расколол бы строку ЧУЖОЙ страницы в соседней панели (FindFocusedRow не
        /// видит поля карточки, `live` там null, а `lastFocusedId` всё ещё называет строку прозы, в
        /// которой ДМ был до клика в доску) — тот же дефект, что уже ловили на полях шапки персонажа.
        /// Отмена — единственный аккорд, которому в этом состоянии есть законный адресат, и она спрашивает
        /// не этот метод, а UndoTargetView ниже.</summary>
        public DocumentPageView ActiveView()
        {
            ResolveFocus(out var pageView, out _);
            return pageView;
        }

        /// <summary>Куда уходит Ctrl+Z (и Ctrl+Y). Отличается от ActiveView РОВНО ОДНИМ случаем — каретка в
        /// развёрнутой доске, — и в нём отдаёт вид, который ПОКАЗЫВАЕТ страницу-владелицу этой доски.
        ///
        /// ПОЧЕМУ ЭТО ОТДЕЛЬНЫЙ ВОПРОС, А НЕ РАСШИРЕНИЕ ПРЕДЫДУЩЕГО. У доски своей истории отмены нет:
        /// снимок кладёт CanvasSurfaceHost.BeforeMutation, и кладёт его в вид, показывающий страницу-
        /// владелицу. Значит, когда ДМ работает в доске, владелец НАБОРА (никто — см. ActiveView) и
        /// владелец ИСТОРИИ (тот самый вид) — разные, и делать вид, что это один ответ, значит отдать в
        /// придачу к отмене ещё и Enter с Tab. Инвариант, который отсюда следует и который стоит помнить
        /// целиком: Ctrl+Z попадает ровно в тот стек, в который доска пишет, — включая «никуда», когда
        /// страницу-владелицу не показывает ни одна панель (тогда доска и снимка не делала, и откатывать
        /// нечего).
        ///
        /// Раньше здесь была молчащая отмена: задача 5 отвечала null, потому что панель с доской показывает
        /// не страницу, а до неё отмена уходила в жёстко привязанный вид панели 0 — то есть в НЕВИДИМУЮ
        /// страницу и в стек, куда доска как раз ничего и не писала. «Срабатывало» это в одной раскладке
        /// из трёх, случайно.</summary>
        public DocumentPageView UndoTargetView()
        {
            ResolveFocus(out var pageView, out int boardPane);
            if (pageView != null) return pageView;
            return boardPane >= 0 && canvasHost != null ? canvasHost.HistoryViewFor(boardPane) : null;
        }

        /// <summary>Панель, чьей РАЗВЁРНУТОЙ ДОСКЕ сейчас принадлежат клавиши, или −1. Спрашивает
        /// CanvasInteractionController про свои три клавиши (Ctrl+V, Delete, Esc): досок на экране может
        /// быть две, каждая опрашивает клавиатуру из СВОЕГО Update, и без этого вопроса одно нажатие
        /// сработало бы дважды. Ровно тот же приём, что и DocumentPageView.KeyboardTargetProbe у Ctrl+F, и
        /// ровно тот же источник истины — разбор ниже.</summary>
        public int ActiveBoardPane()
        {
            ResolveFocus(out _, out int boardPane);
            return boardPane;
        }

        /// <summary>ЕДИНСТВЕННЫЙ разбор «чей сейчас фокус», из которого выводятся все три ответа выше.
        /// Отдаёт ровно одно из трёх: вид страницы, номер панели с развёрнутой доской, или ничего.
        ///
        /// Порядок ступеней:
        ///   1. каретка стоит в поле, лежащем внутри ВИДИМОГО вида страницы → этот вид;
        ///   2. каретка стоит внутри развёрнутой доски → её панель, и ступени 3–4 УЖЕ НЕ СПРАШИВАЮТСЯ:
        ///      «доска, но её страницу никто не показывает» — это ответ «никому», а не повод отдать
        ///      клавиши странице в соседней панели (и не повод положиться на то, что фокус панели уже
        ///      переехал за кликом: PaneFocusOnClick применяет его только в СВОЁМ LateUpdate);
        ///   3. каретки нет (обычное состояние — сразу после отмены, после клика по кнопке или вкладке):
        ///      вид АКТИВНОЙ панели, если он показывает страницу;
        ///   4. иначе — доска активной панели, если панель показывает доску;
        ///   5. иначе ничего: активная панель показывает карту, подземелье или город.
        ///
        /// SurfaceVisible, А НЕ `Page != null`, НА ОБЕИХ СТУПЕНЯХ СО СТРАНИЦЕЙ. PageSurfaceHost.Hide
        /// СОЗНАТЕЛЬНО не отвязывает страницу от вида (иначе каждая синхронизация поверхностей стирала бы
        /// историю отмены), так что вид остаётся привязан к странице ещё долго после того, как перестал её
        /// показывать: `Page != null` означает «когда-то показывал», и только `SurfaceVisible` — «показывает
        /// сейчас». Без этой проверки Ctrl+Z в панели, где открыта карта, тихо откатывал бы правку в
        /// НЕВИДИМОЙ странице этой же панели.
        ///
        /// SurfaceVisible ТРЕБУЕТСЯ И НА СТУПЕНИ 1, хотя выглядит недостижимым. Первая редакция обходилась
        /// без проверки и объясняла это тем, что «выключенный объект не бывает currentSelectedGameObject».
        /// Ревью проверило по исходникам пакетов, а не по памяти, и это НЕПРАВДА: `EventSystem` нигде не
        /// сверяет своё выделение с `activeInHierarchy`, `TMP_InputField.OnDisable → DeactivateInputField`
        /// выделения EventSystem не трогает, а единственное место, которое его снимает
        /// (`InputSystemUIInputModule`), делает это на нажатии указателя. То есть гашение `root` само по
        /// себе выделение НЕ снимает — ступень 1 защищена лишь тем, что каждый путь, скрывающий вид,
        /// проходит через клик или уничтожает выделенный объект. Это свойство ЧУЖИХ компонентов, а не
        /// инвариант этого класса, а цена ошибки несимметрична: вид сидит на обёртке «PageSurface», которая
        /// остаётся ВКЛЮЧЁННОЙ всегда (гасится только её ребёнок `root`), так что `GetComponentInParent`
        /// спокойно вернул бы СКРЫТЫЙ вид.
        ///
        /// `GetComponentInParent` — это и есть подъём по `transform.parent` до первого попавшегося
        /// DocumentPageView; писать цикл вручную значило бы повторить его же, только с собственной
        /// опечаткой. Доска таким подъёмом не находится (её карточки не лежат ни в каком
        /// DocumentPageView), поэтому про неё спрашивают её собственного хозяина —
        /// CanvasSurfaceHost.PaneOfBoardContaining.
        ///
        /// НИЧЕГО НЕ ХРАНИТСЯ, вопрос задаётся заново каждый раз — см. класс-док про то, почему опрос, а не
        /// поле «текущий вид».</summary>
        /// <summary>ВРЕМЕННЫЙ ПРИБОР (8 августа 2026), снимается вместе с аккордом в DocKeyboardController.
        /// Отвечает на вопрос, который два круга чтения кода закрыть не смогли: почему в строку поиска
        /// перестаёт идти набор, хотя каретка видна. Печатает то, что разбор ВИДИТ (кто выделен и где он
        /// лежит) и что ОТВЕЧАЕТ, — и главное, полный путь выделенного объекта: если набор перестал
        /// доходить, значит выделение уехало, и путь называет вора по имени.</summary>
        public string Describe()
        {
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;

            var underCaret = selected != null ? selected.GetComponentInParent<DocumentPageView>() : null;
            int inBoard = selected != null && canvasHost != null
                ? canvasHost.PaneOfBoardContaining(selected.transform) : -1;
            var layout = controller != null ? controller.Layout : null;

            ResolveFocus(out var pageView, out int boardPane);

            var sb = new System.Text.StringBuilder("[ФОКУС] ");
            sb.Append("выделен=").Append(selected != null ? PathOf(selected.transform) : "—");
            sb.Append(" | вид под кареткой=").Append(underCaret != null
                ? (underCaret.SurfaceVisible ? "есть, видим" : "есть, СКРЫТ") : "нет");
            sb.Append(" | в доске панели=").Append(inBoard);
            sb.Append(" | активная панель=").Append(layout != null ? layout.FocusedPane.ToString() : "—");
            sb.Append(" || ActiveView=").Append(pageView != null ? "есть" : "НЕТ");
            sb.Append(" ActiveBoardPane=").Append(boardPane);
            sb.Append(" UndoTarget=").Append(UndoTargetView() != null ? "есть" : "НЕТ");

            var view = pageView ?? (host != null && layout != null ? host.ViewFor(layout.FocusedPane) : null);
            if (view != null)
                sb.Append(" || у вида: SearchOwnsKeys=").Append(view.SearchOwnsKeys)
                  .Append(" PaletteOpen=").Append(view.PaletteOpen)
                  .Append(" KeyboardSuspended=").Append(view.KeyboardSuspended)
                  .Append(" IsKeyboardTarget=").Append(view.IsKeyboardTarget);

            return sb.ToString();
        }

        static string PathOf(UnityEngine.Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        void ResolveFocus(out DocumentPageView pageView, out int boardPane)
        {
            pageView = null;
            boardPane = -1;

            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null)
            {
                var underCaret = selected.GetComponentInParent<DocumentPageView>();
                if (underCaret != null && underCaret.SurfaceVisible) { pageView = underCaret; return; }

                int inBoard = canvasHost != null ? canvasHost.PaneOfBoardContaining(selected.transform) : -1;
                if (inBoard >= 0) { boardPane = inBoard; return; }
            }

            var layout = controller != null ? controller.Layout : null;
            if (layout == null) return;
            int pane = layout.FocusedPane;

            var focused = host != null ? host.ViewFor(pane) : null;
            if (focused != null && focused.SurfaceVisible) { pageView = focused; return; }

            if (canvasHost != null && canvasHost.ShowsBoard(pane)) boardPane = pane;
        }
    }
}
