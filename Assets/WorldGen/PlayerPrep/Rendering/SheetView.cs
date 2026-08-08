using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Лист персонажа: всё, что посчитал SheetMath, показанное разом — и каждое число умеет
    /// рассказать, откуда взялось.
    ///
    /// НИ ОДНОГО ПРАВИЛА ЗДЕСЬ НЕТ. Строку «+3 ловкость, +2 мастерство» собирает чистый слой и держат
    /// самопроверки; лист только выводит готовое. Собери он её сам — на экране появилось бы второе
    /// объяснение того же числа, которое никто не проверяет и которое разойдётся с первым при первой
    /// же правке правил. Ровно эта копия уже жила в WizardView.ExplainAbility и переехала в
    /// SheetMath.ExplainAbility вместе с этой задачей.
    ///
    /// НИЧЕГО НЕ УДАЛЯЕТ. Неизвестные справочнику записи (UnknownIds) перечисляются в полосе
    /// предупреждений и остаются в файле: лист, собранный чужим или более новым справочником,
    /// открывается как есть, а не «чинится» молчаливой потерей выборов игрока.
    ///
    /// СТРОИТСЯ ИЗ Build, А НЕ ИЗ Awake, по той же причине, что и WizardView. Правило
    /// WorkspaceBuilder.DemolishForRebuild (WorkspaceBuilder.cs:133-136) — «строишь интерфейс в Awake,
    /// сначала снеси прошлую постройку, потому что Unity зовёт Awake заново при каждой перекомпиляции
    /// скриптов» — применимо только к тому, кто строит себя в Awake. Здесь Awake нет вовсе, а
    /// жизненным циклом содержимого владеет PlayerPrepScreenController.ClearContent, чей Awake
    /// пересобирает экран с нуля. Заводить Awake ради постройки нельзя: правило станет применимым.
    ///
    /// Обратное правило соблюдается тоже: числа нажимаются, то есть пересборка идёт из обработчика
    /// нажатия, поэтому Rebuild сносит прошлое отложенным Destroy, а не DestroyImmediate.</summary>
    public class SheetView : MonoBehaviour
    {
        const float BottomBarHeight = 66f;
        const float RowHeight = 32f;
        const float LeftColumnWidth = 520f;
        const float TitleWidth = 230f;
        const float ValueWidth = 96f;
        const float PortraitSide = 112f;
        const float MaxBackstoryHeight = 260f;

        /// <summary>Заведомо больше любой колонки листа. Ставится предпочтительной шириной СТРОКАМ:
        /// вертикальная раскладка даёт ребёнку Clamp(ширина колонки, min, preferred), поэтому строка с
        /// таким preferred получает ровно ширину колонки, какой бы та ни была.
        ///
        /// Число, а не «ширина текста внутри строки», и это та самая ловушка, что стоила блокирующей
        /// находки в задаче 10: Text.preferredWidth меряет строку БЕЗ ПЕРЕНОСА, в одну линию, поэтому
        /// длинное объяснение просило бы тысячи точек, сумма предпочтительных ширин перевалила бы за
        /// доступную, и HorizontalLayoutGroup начал бы раздавать Lerp(min, preferred, t) с общим
        /// t &lt; 1 — колонки и подписи схлопнулись бы пропорционально. Ни одно слагаемое ширины на
        /// этом листе из текста не приходит.</summary>
        const float StretchWidth = 4000f;

        // Полоса предупреждений: высота считается по ЧИСЛУ строк, а не Text.preferredHeight. В момент
        // постройки раскладки ещё не было, ширины у текста нет, и preferredHeight там врёт — тот же
        // приём и по той же причине, что у PlayerPrepScreenController.FooterHeight.
        const float WarnLineHeight = 21f;
        const int WarnFontSize = 15;
        const float WarnCharsPerLine = 205f;      // 1860 точек кеглем 15 ≈ 205 знаков в строке
        const float WarnButtonHeight = 40f;
        const float MaxWarnHeight = 300f;

        static readonly Color Muted = new Color(0.55f, 0.53f, 0.50f);
        static readonly Color Faint = new Color(0.40f, 0.39f, 0.38f);
        static readonly Color Accent = new Color(0.85f, 0.78f, 0.58f);
        static readonly Color WarnFill = new Color(0.22f, 0.18f, 0.13f);
        static readonly Color WarnText = new Color(0.90f, 0.78f, 0.55f);
        static readonly Color HairLine = new Color(0.20f, 0.20f, 0.22f);
        static readonly Color PlaceholderFill = new Color(0.15f, 0.15f, 0.16f);

        CharacterFile file;
        RulesData rules;
        string rulesError;
        DerivedSheet sheet;

        /// <summary>Какая строка сейчас раскрыта, либо null. Ключ ОБЯЗАТЕЛЬНО с приставкой
        /// («ability:dex», «save:dex», «skill:stealth»): идентификаторы характеристик встречаются и у
        /// характеристик, и у спасбросков, и голый «dex» раскрыл бы сразу две строки — а «ровно одна
        /// за раз» это требование, а не украшение.</summary>
        string expanded;

        /// <summary>Портрет собирается ОДИН РАЗ в Build и переживает пересборки. Каждое нажатие на
        /// число пересобирает лист целиком; декодируй мы байты в Rebuild, двадцать нажатий оставили бы
        /// в памяти двадцать текстур по 512 точек — без единой ошибки в журнале.</summary>
        Sprite portrait;

        Text backstoryText;
        LayoutElement backstoryElement;
        ScrollRect backstoryScroll;

        ScrollRect bodyScroll;

        /// <summary>Куда была прокручена середина листа до пересборки, и сколько кадров ещё пытаться
        /// туда вернуться. Без этого нажатие на навык в самом низу (а навыков восемнадцать)
        /// перерисовывало лист и отбрасывало игрока в самое начало — к объяснению, ради которого он
        /// нажимал, приходилось прокручивать заново каждый раз.
        ///
        /// Несколько кадров, а не один: раскладка uGUI считается в Canvas.willRenderCanvases, ПОСЛЕ
        /// LateUpdate, поэтому в первый кадр высота содержимого ещё прежняя и доля прокрутки легла бы
        /// не туда. Промах стоит неточной позиции, а не поломки, — поэтому просто повторяем.</summary>
        float pendingScroll;
        int pendingScrollFrames;

        /// <summary>Единственная точка входа. Корень сцены зовёт её из ShowSheet.</summary>
        public static SheetView Build(Transform parent, CharacterFile file)
        {
            var go = new GameObject("Sheet", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var view = go.AddComponent<SheetView>();
            view.file = file;
            view.portrait = MakeSprite(file == null ? null : file.Portrait);
            view.Refresh();
            return view;
        }

        /// <summary>Пересчитать и перерисовать. Зовётся задачей 12 после «Повысить уровень»: там
        /// меняется файл, поэтому мало перерисовать — надо сначала посчитать заново.</summary>
        public void Refresh()
        {
            // Справочник читается через try: SheetRulesSource.Rules не возвращает null, а бросает.
            // Пустой экран вместо объяснения — худшее, чем можно ответить на непрочитанный справочник.
            try { rules = SheetRulesSource.Rules; rulesError = null; }
            catch (Exception ex) { rules = null; rulesError = ex.Message; }

            sheet = rules == null || file == null ? null : SheetMath.Compute(file, rules);
            Rebuild();
        }

        void OnDestroy()
        {
            if (portrait == null) return;
            var texture = portrait.texture;
            Destroy(portrait);
            if (texture != null) Destroy(texture);
            portrait = null;
        }

        /// <summary>Высота предыстории и её прокрутка. Считается ЗДЕСЬ, а не при постройке, потому что
        /// Text.preferredHeight меряется по нынешней ширине прямоугольника, а в момент постройки её
        /// ещё нет — раскладка отработает только к концу кадра.
        ///
        /// ContentSizeFitter на самом блоке предыстории повесить НЕЛЬЗЯ: его высотой управляет
        /// вертикальная раскладка тела листа, и вдвоём они дерутся, а высота скачет. Поэтому высоту
        /// назначаем сами, а ContentSizeFitter остаётся только на содержимом прокрутки, у которого
        /// раскладки-родителя нет.
        ///
        /// Сравнение с уже назначенной высотой обязательно: присваивание preferredHeight помечает
        /// раскладку грязной, и безусловное присваивание каждый кадр пересчитывало бы весь лист.</summary>
        void LateUpdate()
        {
            if (pendingScrollFrames > 0 && bodyScroll != null)
            {
                pendingScrollFrames--;
                bodyScroll.verticalNormalizedPosition = Mathf.Clamp01(pendingScroll);
            }

            if (backstoryText == null || backstoryElement == null || backstoryScroll == null) return;

            float preferred = backstoryText.preferredHeight + 16f;
            float height = Mathf.Min(preferred, MaxBackstoryHeight);
            if (Mathf.Abs(backstoryElement.preferredHeight - height) > 0.5f)
                backstoryElement.preferredHeight = height;

            // Прокрутка появляется ТОЛЬКО по достижении предела: короткая предыстория не должна
            // уезжать под колесом мыши, которое игрок крутит, чтобы пролистать весь лист.
            bool needsScroll = preferred > MaxBackstoryHeight;
            if (backstoryScroll.enabled != needsScroll)
            {
                backstoryScroll.enabled = needsScroll;
                backstoryScroll.vertical = needsScroll;
            }
        }

        // ── Пересборка ───────────────────────────────────────────────────────────

        /// <summary>Перерисовывает лист целиком — одна дорога вместо частичных обновлений.
        ///
        /// Отложенный Destroy, а НЕ DestroyImmediate: сюда попадают из обработчика нажатия (любое
        /// число на листе — кнопка), а DestroyImmediate уничтожил бы кнопку прямо посреди её
        /// собственного клика. SetParent(null) перед Destroy — потому что Destroy откладывается до
        /// конца кадра, и без отвязки старая раскладка ещё кадр делила бы место с новой.</summary>
        void Rebuild()
        {
            // Ссылки на предыстория-блок обнуляются ПЕРВЫМИ. Их читает LateUpdate, а пересборка может
            // не построить блок вовсе (пустая предыстория, непрочитанный справочник) — и ссылки
            // остались бы смотреть на уничтоженные объекты.
            backstoryText = null;
            backstoryElement = null;
            backstoryScroll = null;

            // Куда была прокручена середина — запомнить ДО сноса, вернуть после (см. поле pendingScroll).
            if (bodyScroll != null)
            {
                pendingScroll = bodyScroll.verticalNormalizedPosition;
                pendingScrollFrames = 3;
            }
            bodyScroll = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            var warnings = Warnings();
            float warnHeight = WarnBandHeight(warnings);
            if (warnHeight > 0f) BuildWarnBand(warnings, warnHeight);
            var content = BuildBody(warnHeight);
            BuildBottomBar();

            if (sheet == null)
            {
                AddLabel(content, rules == null
                    ? "Справочник правил не загрузился, считать лист не по чему:\n" + rulesError
                    : "Лист не открыт.", 18, null);
                return;
            }

            BuildHeader(content);
            BuildColumns(content);
            BuildFeatures(content);
            BuildEquipment(content);
            BuildSpells(content);
            BuildBackstory(content);
        }

        // ── Предупреждения ───────────────────────────────────────────────────────

        /// <summary>Что лист обязан сказать вслух, прежде чем игрок поверит числам. Пусто — полосы
        /// нет вовсе.</summary>
        List<string> Warnings()
        {
            var list = new List<string>();
            if (sheet == null) return list;

            if (rules != null && !string.IsNullOrEmpty(file.RulesId) && file.RulesId != rules.Id)
                list.Add($"Лист собран другим справочником ({file.RulesId}). Поля разрешены как смогли.");

            // Перечисляем, но НЕ вычищаем: выбор игрока, которого не знает нынешний справочник,
            // остаётся в файле — вернётся справочник, вернётся и выбор.
            if (sheet.UnknownIds.Count > 0)
                list.Add("Незнакомые записи (оставлены в файле как есть): "
                         + string.Join("; ", sheet.UnknownIds.ToArray()));

            if (sheet.Missing.Count > 0)
                list.Add("Чего не хватает: " + string.Join("; ", sheet.Missing.ToArray()));

            return list;
        }

        static int WarnLines(string message) =>
            Mathf.Max(1, Mathf.CeilToInt(message.Length / WarnCharsPerLine));

        float WarnBandHeight(List<string> warnings)
        {
            if (warnings.Count == 0) return 0f;
            float height = 20f;                                   // отступы полосы
            foreach (var w in warnings) height += WarnLines(w) * WarnLineHeight + 4f;
            if (sheet != null && sheet.Missing.Count > 0) height += WarnButtonHeight + 4f;
            return Mathf.Min(height, MaxWarnHeight);
        }

        void BuildWarnBand(List<string> warnings, float height)
        {
            var band = NewRect(transform, "Warnings", typeof(Image), typeof(VerticalLayoutGroup));
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.offsetMin = new Vector2(24f, -height);
            band.offsetMax = new Vector2(-24f, 0f);
            band.GetComponent<Image>().color = WarnFill;

            var vlg = band.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(14, 14, 8, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            foreach (var w in warnings)
            {
                var label = UiKit.Label(band, w, WarnFontSize);
                label.color = WarnText;
                label.gameObject.AddComponent<LayoutElement>().preferredHeight =
                    WarnLines(w) * WarnLineHeight;
            }

            if (sheet != null && sheet.Missing.Count > 0)
            {
                var row = AddRow(band, WarnButtonHeight);
                AddButton(row, "Доделать в мастере", 280f, BackToWizard);
            }
        }

        /// <summary>Открыть мастер на ЭТОМ же персонаже и с ЕГО путём. Путь передаётся намеренно:
        /// OpenWizard без него забудет, откуда лист открыт, и первое «Сохранить» после доделки
        /// спросило бы имя файла заново.</summary>
        void BackToWizard()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            root.OpenWizard(file, root.CurrentPath);
        }

        // ── Обвязка ──────────────────────────────────────────────────────────────

        /// <summary>Прокручиваемая середина: ScrollRect + Viewport(RectMask2D) + Content
        /// (VerticalLayoutGroup + ContentSizeFitter) — та же связка, что у мастера и у всех боковых
        /// панелей проекта. Лист целиком в экран не помещается никогда.</summary>
        Transform BuildBody(float warnHeight)
        {
            var scrollRt = NewRect(transform, "Body", typeof(ScrollRect));
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(24f, BottomBarHeight);
            scrollRt.offsetMax = new Vector2(-24f, -warnHeight);
            var scroll = scrollRt.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewRect(scrollRt, "Viewport", typeof(RectMask2D), typeof(Image));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            scroll.viewport = viewport;

            var content = NewRect(viewport, "Content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.padding = new RectOffset(0, 12, 12, 12);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;

            bodyScroll = scroll;
            return content;
        }

        void BuildBottomBar()
        {
            var bar = NewRect(transform, "BottomBar");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = new Vector2(24f, 10f);
            bar.offsetMax = new Vector2(-24f, BottomBarHeight);

            var back = UiKit.Button(bar, "← К списку листов", AskBackToList);
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 0.5f);
            backRt.anchorMax = new Vector2(0f, 0.5f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.sizeDelta = new Vector2(240f, 44f);

            var root = PlayerPrepScreenController.Instance;

            var saveAs = UiKit.Button(bar, "Сохранить как…", SaveAsThroughRoot, root != null);
            var saveAsRt = (RectTransform)saveAs.transform;
            saveAsRt.anchorMin = new Vector2(1f, 0.5f);
            saveAsRt.anchorMax = new Vector2(1f, 0.5f);
            saveAsRt.pivot = new Vector2(1f, 0.5f);
            saveAsRt.anchoredPosition = new Vector2(-250f, 0f);
            saveAsRt.sizeDelta = new Vector2(240f, 44f);

            var save = UiKit.Button(bar, "Сохранить", SaveThroughRoot, root != null);
            var saveRt = (RectTransform)save.transform;
            saveRt.anchorMin = new Vector2(1f, 0.5f);
            saveRt.anchorMax = new Vector2(1f, 0.5f);
            saveRt.pivot = new Vector2(1f, 0.5f);
            saveRt.sizeDelta = new Vector2(240f, 44f);

            if (root != null && !string.IsNullOrEmpty(root.CurrentPath))
            {
                var path = UiKit.Label(bar, "Файл: " + root.CurrentPath, 13, TextAnchor.MiddleLeft);
                path.color = Faint;
                var pathRt = path.rectTransform;
                pathRt.anchorMin = new Vector2(0f, 0.5f);
                pathRt.anchorMax = new Vector2(0f, 0.5f);
                pathRt.pivot = new Vector2(0f, 0.5f);
                pathRt.anchoredPosition = new Vector2(256f, 0f);
                pathRt.sizeDelta = new Vector2(900f, 30f);
            }
        }

        void AskBackToList()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            ConfirmDialog.Show(UiKit.Font, "Вернуться к списку листов?",
                "Несохранённые изменения листа будут потеряны.",
                confirmed =>
                {
                    // Ответ приходит ЧЕРЕЗ КАДР, и к этому времени листа может не быть вовсе:
                    // перекомпиляция скриптов при открытом диалоге пересобирает сцену, а замыкание
                    // остаётся жить. Проверка на уничтоженный компонент — единственная защита.
                    if (this == null) return;
                    if (confirmed) root.BackToList();
                });
        }

        /// <summary>«Сохранить» ОТВЕЧАЕТ ВСЛУХ, ровно как у мастера: молчаливая запись в уже известный
        /// путь неотличима от бездействия, а проверить человеку нечем. Сообщение — только при удавшейся
        /// записи: про отменённый диалог имени говорить нечего, а про упавшую запись уже сказал
        /// SaveCurrent, и наше сообщение сменило бы его собой (ConfirmDialog держит один диалог).</summary>
        void SaveThroughRoot()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            bool saved = root.SaveCurrent();
            Rebuild();   // показать путь, если он только что появился
            if (saved)
                ConfirmDialog.ShowInfo(UiKit.Font, "Лист сохранён",
                    string.IsNullOrEmpty(root.CurrentPath) ? "" : "Файл: " + root.CurrentPath);
        }

        /// <summary>«Сохранить как…» МОЛЧИТ в ответ, и это не забывчивость. Сказать «лист сохранён»
        /// здесь не по чему: SaveCurrentAs ничего не возвращает, а отменённый диалог имени оставляет
        /// CurrentPath прежним — на уже сохранённом листе радостное сообщение выскочило бы после
        /// отмены, когда не записано ничего. Хуже того, при упавшей записи SaveCurrentAs сам
        /// показывает «Не удалось сохранить лист», а ConfirmDialog держит на экране ровно один диалог:
        /// наше сообщение СМЕНИЛО БЫ СОБОЙ рассказ об ошибке.
        ///
        /// Ответ игроку тут и так есть — сам диалог выбора имени файла: «первое в жизни сохранение
        /// спрашивает имя, и диалог сам по себе служит ответом» (SaveCurrent). «Сохранить как…»
        /// спрашивает имя ВСЕГДА, значит и отвечает всегда.</summary>
        void SaveAsThroughRoot()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            root.SaveCurrentAs();
            Rebuild();   // показать новый путь в нижней полосе
        }

        // ── Шапка ────────────────────────────────────────────────────────────────

        void BuildHeader(Transform content)
        {
            var row = AddRow(content, PortraitSide);

            var portraitRt = NewRect(row, "Portrait", typeof(Image));
            var le = portraitRt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = PortraitSide;
            le.preferredWidth = PortraitSide;
            le.flexibleWidth = 0f;
            var image = portraitRt.GetComponent<Image>();
            if (portrait != null)
            {
                image.sprite = portrait;
                image.preserveAspect = true;
            }
            else
            {
                image.color = PlaceholderFill;
                var hint = UiKit.Label(portraitRt, "портрет\nне выбран", 13, TextAnchor.MiddleCenter);
                hint.color = Faint;
                var hintRt = hint.rectTransform;
                hintRt.anchorMin = Vector2.zero;
                hintRt.anchorMax = Vector2.one;
                hintRt.offsetMin = Vector2.zero;
                hintRt.offsetMax = Vector2.zero;
            }

            var column = Column(row, "Who");
            var columnLe = column.gameObject.AddComponent<LayoutElement>();
            columnLe.preferredWidth = 0f;
            columnLe.flexibleWidth = 1f;

            var name = AddLabel(column, string.IsNullOrWhiteSpace(file.Name) ? "Без имени" : file.Name, 28, Accent);
            name.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var subtitle = AddLabel(column, Subtitle(), 17, Muted);
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        }

        /// <summary>«полурослик · плут 5 · вор · солдат». Неизвестное справочнику не подменяется чужим
        /// именем и не пропадает — так игрок видит, что именно потерялось.</summary>
        string Subtitle()
        {
            var parts = new List<string>();
            parts.Add(Lower(RaceName()) ?? "вид не выбран");
            string cls = Lower(ClassName());
            parts.Add(cls == null ? "класс не выбран" : $"{cls} {Mathf.Clamp(file.Level, 1, 20)}");
            string sub = Lower(SubclassName());
            if (sub != null) parts.Add(sub);
            parts.Add(Lower(BackgroundName()) ?? "предыстория не выбрана");
            return string.Join(" · ", parts.ToArray());
        }

        static string Lower(string s) => string.IsNullOrEmpty(s) ? null : s.ToLowerInvariant();

        string RaceName() => rules == null ? null
            : rules.Races.FirstOrDefault(r => r.Id == file.RaceId)?.Name;

        ClassDef CurrentClass() => rules == null ? null
            : rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);

        string ClassName() => CurrentClass()?.Name;

        string SubclassName() => CurrentClass()?.Subclasses
            .FirstOrDefault(s => s.Id == file.SubclassId)?.Name;

        string BackgroundName() => rules == null ? null
            : rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId)?.Name;

        // ── Две колонки ──────────────────────────────────────────────────────────

        void BuildColumns(Transform content)
        {
            var (left, right) = AddTwoColumns(content);

            AddCaption(left, "Характеристики");
            foreach (var id in SheetMath.AbilityOrder())
            {
                string abilityId = id;
                AddNumberRow(left, "ability:" + abilityId,
                    SheetMath.AbilityName(abilityId),
                    sheet.Total.Get(abilityId).ToString(),
                    Signed(sheet.Modifiers.Get(abilityId)),
                    sheet.AbilityExplain.TryGetValue(abilityId, out var explain) ? explain : null,
                    marked: false);
            }

            AddCaption(right, "Бой и защита");
            AddNumberRow(right, "ac", "Класс доспеха", sheet.ArmorClass.ToString(), null,
                sheet.ArmorClassExplain, marked: false);
            AddNumberRow(right, "init", "Инициатива", Signed(sheet.Initiative), null,
                sheet.InitiativeExplain, marked: false);
            AddNumberRow(right, "speed", "Скорость", sheet.Speed + " фт", null,
                sheet.SpeedExplain, marked: false);
            AddNumberRow(right, "hp", "Хиты", sheet.MaxHp.ToString(), null,
                sheet.MaxHpExplain, marked: false);
            AddNumberRow(right, null, "Кость хитов",
                string.IsNullOrEmpty(sheet.HitDie) ? "—" : sheet.HitDie, null, null, marked: false);
            AddNumberRow(right, "prof", "Мастерство", Signed(sheet.ProficiencyBonus), null,
                sheet.ProficiencyExplain, marked: false);

            AddCaption(right, "Спасброски");
            foreach (var save in sheet.Saves)
                AddNumberRow(right, "save:" + save.AbilityId, Mark(save.Proficient) + save.Name,
                    Signed(save.Bonus), null, save.Explain, save.Proficient);

            AddCaption(right, "Навыки");
            foreach (var skill in sheet.Skills)
                AddNumberRow(right, "skill:" + skill.SkillId, Mark(skill.Proficient) + skill.Name,
                    Signed(skill.Bonus),
                    SheetMath.AbilityName(skill.AbilityId).ToLowerInvariant()
                        + (skill.Expertise ? " · компетентность" : ""),
                    skill.Explain, skill.Proficient);
        }

        static string Mark(bool proficient) => proficient ? "[×] " : "[ ] ";

        /// <summary>Знак у бонуса ставит чистый слой — тот же, что собирает строки Explain. Своя копия
        /// здесь означала бы «+0 ловкость» в объяснении и «0» в самом числе прямо над ним.</summary>
        static string Signed(int v) => SheetMath.Signed(v);

        /// <summary>Строка «подпись — число — уточнение», где число ЖМЁТСЯ и раскрывает под собой
        /// объяснение из чистого слоя. Раскрыта ровно одна строка на весь лист: второе нажатие
        /// сворачивает, нажатие на другое число переносит раскрытие туда.
        ///
        /// key == null или explain == null — число не кнопка, а подпись: у кости хитов объяснять
        /// нечего, она и есть справочное значение.</summary>
        void AddNumberRow(Transform column, string key, string title, string value, string hint,
            string explain, bool marked)
        {
            var row = AddRow(column, RowHeight);

            var titleLabel = AddLabel(row, title, 17, marked ? Accent : (Color?)null);
            var titleLe = titleLabel.gameObject.AddComponent<LayoutElement>();
            titleLe.minWidth = TitleWidth;              // ниже не сжимается ни при какой длине текста
            titleLe.preferredWidth = TitleWidth;
            titleLe.flexibleWidth = 0f;

            bool clickable = !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(explain);
            if (clickable)
            {
                string target = key;
                var btn = UiKit.Button(row, value, () =>
                {
                    expanded = expanded == target ? null : target;
                    Rebuild();
                });
                var text = btn.GetComponentInChildren<Text>();
                if (text != null) { text.fontSize = 18; text.color = Accent; }
                var btnLe = btn.gameObject.AddComponent<LayoutElement>();
                btnLe.minWidth = ValueWidth;
                btnLe.preferredWidth = ValueWidth;
                btnLe.flexibleWidth = 0f;
                btnLe.preferredHeight = RowHeight - 4f;
            }
            else
            {
                var valueLabel = AddLabel(row, value, 18, Accent);
                var valueLe = valueLabel.gameObject.AddComponent<LayoutElement>();
                valueLe.minWidth = ValueWidth;
                valueLe.preferredWidth = ValueWidth;
                valueLe.flexibleWidth = 0f;
            }

            var hintLabel = AddLabel(row, hint ?? "", 14, Faint);
            var hintLe = hintLabel.gameObject.AddComponent<LayoutElement>();
            hintLe.preferredWidth = 0f;                 // остаток ширины, а не ширина текста
            hintLe.flexibleWidth = 1f;

            // Высота объяснения НЕ задаётся: её спрашивает у самого текста вертикальная раскладка,
            // уже посчитавшая ширину. Пиши мы сюда 22 точки, объяснение длиннее одной строки (у хитов
            // оно и сейчас под шестьдесят знаков, а справочник — данные, не фикстура) нарисовалось бы
            // ПОВЕРХ следующей строки: у подписей проекта verticalOverflow = Overflow, они не режутся.
            if (clickable && expanded == key) AddLabel(column, explain, 15, WarnText);
        }

        // ── Нижние разделы ───────────────────────────────────────────────────────

        void BuildFeatures(Transform content)
        {
            AddCaption(content, "Умения");
            if (sheet.Features.Count == 0)
            {
                AddLabel(content, "Пока ни одного — они приходят от вида, класса и предыстории.", 15, Muted);
                return;
            }

            foreach (var feature in sheet.Features)
            {
                string source = SourceName(feature.Source);
                string suffix = feature.Level > 0 ? $" · {feature.Level} уровень" : "";
                var title = AddLabel(content, $"{feature.Name} ({source}{suffix})", 17, Accent);
                title.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                if (!string.IsNullOrEmpty(feature.Text)) AddLabel(content, feature.Text, 15, Muted);
            }
        }

        static string SourceName(string source)
        {
            switch (source)
            {
                case "race": return "вид";
                case "class": return "класс";
                case "background": return "предыстория";
                case "feat": return "черта";
                default: return source;
            }
        }

        void BuildEquipment(Transform content)
        {
            AddCaption(content, "Снаряжение");
            if (file.Equipment.Count == 0)
            {
                AddLabel(content, "Пусто.", 15, Muted);
                return;
            }

            foreach (var id in file.Equipment)
            {
                // Незнакомый идентификатор показывается САМ СОБОЙ, а не пропускается: строка про него
                // уже стоит в полосе предупреждений, и молча исчезнуть из списка он не должен.
                var item = rules.Items.FirstOrDefault(i => i.Id == id);
                var label = AddLabel(content, "· " + (item == null ? $"неизвестно «{id}»" : item.Name),
                    16, item == null ? WarnText : (Color?)null);
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            }
        }

        void BuildSpells(Transform content)
        {
            if (sheet.SpellSlots == null) return;      // неколдующий — раздела нет вовсе

            AddCaption(content, "Заклинания");
            var slots = new List<string>();
            for (int i = 0; i < sheet.SpellSlots.Length; i++)
                if (sheet.SpellSlots[i] > 0) slots.Add($"{i + 1} круг — {sheet.SpellSlots[i]}");
            var slotsLabel = AddLabel(content, slots.Count == 0
                ? "Ячеек на этом уровне ещё нет."
                : "Ячейки: " + string.Join(",  ", slots.ToArray()), 16, null);
            slotsLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            AddLabel(content, "Заклинания вписываются вручную — справочника заклинаний в этой версии нет.",
                14, Muted).gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            AddInput(content, file.SpellNotes, "Заговоры, подготовленные заклинания, пометки…",
                v => file.SpellNotes = v);
        }

        /// <summary>Предыстория растёт до предела, дальше прокручивается. Высоту назначает LateUpdate —
        /// см. его шапку про то, почему не ContentSizeFitter и не постройка.</summary>
        void BuildBackstory(Transform content)
        {
            AddCaption(content, "Предыстория");
            if (string.IsNullOrWhiteSpace(file.Backstory))
            {
                var empty = AddLabel(content, "Не записана. Её можно вписать в мастере, шаг «Кто ты».",
                    15, Muted);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                return;
            }

            var scrollRt = NewRect(content, "Backstory", typeof(ScrollRect));
            backstoryElement = scrollRt.gameObject.AddComponent<LayoutElement>();
            backstoryElement.preferredWidth = StretchWidth;
            backstoryElement.preferredHeight = 60f;    // до первого LateUpdate, дальше по тексту
            backstoryScroll = scrollRt.GetComponent<ScrollRect>();
            backstoryScroll.horizontal = false;
            backstoryScroll.vertical = false;          // включится, только если текст не влез
            backstoryScroll.enabled = false;
            backstoryScroll.scrollSensitivity = 24f;
            backstoryScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewRect(scrollRt, "Viewport", typeof(RectMask2D), typeof(Image));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0f, 0f);
            viewport.offsetMax = new Vector2(-4f, 0f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            backstoryScroll.viewport = viewport;

            // ContentSizeFitter живёт ЗДЕСЬ и только здесь: у содержимого прокрутки родитель — вьюпорт,
            // а не раскладка, поэтому драться за высоту не с кем.
            backstoryText = UiKit.Label(viewport, file.Backstory, 16, TextAnchor.UpperLeft);
            var textRt = backstoryText.rectTransform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
            backstoryText.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            backstoryScroll.content = textRt;
        }

        // ── Мелкие построители ───────────────────────────────────────────────────

        /// <summary>Две колонки листа. Ширины заданы ОБЕИМ и все три — min, preferred и flexible.
        /// Причина расписана в WizardView.AddTwoColumns: незаданный min превращает левую колонку в
        /// столбик обрезков, как только справа окажется длинный текст, потому что Text.preferredWidth
        /// меряется без переноса. Здесь тот же приём с той же оговоркой.</summary>
        (Transform left, Transform right) AddTwoColumns(Transform content)
        {
            var row = NewRect(content, "Columns", typeof(HorizontalLayoutGroup));
            row.gameObject.AddComponent<LayoutElement>().preferredWidth = StretchWidth;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 24f;
            hlg.childAlignment = TextAnchor.UpperLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false;

            var left = Column(row, "Abilities");
            var leftLe = left.gameObject.AddComponent<LayoutElement>();
            leftLe.minWidth = LeftColumnWidth;
            leftLe.preferredWidth = LeftColumnWidth;
            leftLe.flexibleWidth = 0f;

            var right = Column(row, "Numbers");
            var rightLe = right.gameObject.AddComponent<LayoutElement>();
            rightLe.preferredWidth = 0f;               // «нуль плюс вся гибкость» и есть остаток
            rightLe.flexibleWidth = 1f;
            return (left, right);
        }

        static Transform Column(Transform parent, string name)
        {
            var rt = NewRect(parent, name, typeof(VerticalLayoutGroup));
            var vlg = rt.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>Строка внутри колонки. preferredWidth ставится КОНСТАНТОЙ заведомо больше колонки,
        /// а не суммой ширин детей: вертикальная раскладка отдаёт ребёнку Clamp(ширина колонки, min,
        /// preferred), так что строка получает ровно ширину колонки и не зависит от того, какой длины
        /// текст в неё положили.</summary>
        static Transform AddRow(Transform parent, float height)
        {
            var rt = NewRect(parent, "Row", typeof(HorizontalLayoutGroup));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.preferredWidth = StretchWidth;
            var hlg = rt.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            return rt;
        }

        static Button AddButton(Transform parent, string label, float width, Action onClick)
        {
            var btn = UiKit.Button(parent, label, onClick);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            le.preferredHeight = RowHeight;
            var text = btn.GetComponentInChildren<Text>();
            if (text != null) text.fontSize = 17;
            return btn;
        }

        static Text AddLabel(Transform parent, string text, int size, Color? color)
        {
            var label = UiKit.Label(parent, text, size);
            if (color.HasValue) label.color = color.Value;
            return label;
        }

        static void AddCaption(Transform parent, string text)
        {
            var label = UiKit.Label(parent, text, 20);
            label.color = Accent;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        }

        /// <summary>Многострочное поле. Собирается тем же порядком, что и в WizardView.AddInput и
        /// QuickOpenPopup.BuildInputRow: textComponent и placeholder назначаются ДО присваивания text,
        /// иначе InputField пишет в пустоту.
        ///
        /// Пересборку НЕ зовёт: она уничтожила бы поле прямо во время набора вместе с кареткой.</summary>
        void AddInput(Transform parent, string value, string placeholderText, Action<string> onChanged)
        {
            var rt = NewRect(parent, "Input", typeof(Image), typeof(InputField));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 160f;
            le.preferredWidth = StretchWidth;
            var image = rt.GetComponent<Image>();
            image.color = PlaceholderFill;

            var input = rt.GetComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = InputField.LineType.MultiLineNewline;

            var text = UiKit.Label(rt, "", 16, TextAnchor.UpperLeft);
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            StretchInside(text.rectTransform);
            input.textComponent = text;

            var placeholder = UiKit.Label(rt, placeholderText, 16, TextAnchor.UpperLeft);
            placeholder.color = Faint;
            placeholder.fontStyle = FontStyle.Italic;
            StretchInside(placeholder.rectTransform);
            input.placeholder = placeholder;

            input.text = value ?? "";
            input.onValueChanged.AddListener(v => onChanged(v));
        }

        static void StretchInside(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 6f);
            rt.offsetMax = new Vector2(-10f, -6f);
        }

        /// <summary>Байты портрета в спрайт. Своя копия, потому что общего построителя в проекте нет:
        /// PoiMarkerView, DungeonInspectorPanel и PoiInfoPopup держат по такой же приватной. Тихо
        /// возвращает null на битых байтах — портрет не та вещь, из-за которой лист не должен
        /// открыться.</summary>
        static Sprite MakeSprite(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static RectTransform NewRect(Transform parent, string name, params Type[] extra)
        {
            var types = new Type[extra.Length + 1];
            types[0] = typeof(RectTransform);
            for (int i = 0; i < extra.Length; i++) types[i + 1] = extra[i];
            var go = new GameObject(name, types);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
