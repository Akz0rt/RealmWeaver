using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Панель плана прокачки: таблица 1–20 своего класса поверх листа. Здесь игрок намечает
    /// подкласс, черты и прибавки — в том числе на уровни, до которых ещё не дорос.
    ///
    /// НИ ОДНОГО ПРАВИЛА ЗДЕСЬ НЕТ. Что можно выбрать (LevelChoiceOps.FeatOptions), что при этом
    /// происходит с файлом (ChooseSubclass/ChooseFeat/ChooseAsi/Clear), где потолок характеристики и
    /// какие прибавки уже действуют (SheetMath.AbilityTotal) — всё в чистом слое и всё под
    /// самопроверками. Панель только показывает и зовёт.
    ///
    /// СВОЙ ХОЛСТ, sortingOrder 30500 — выше экранов игрока (те рисуются на 0), но НИЖЕ вопроса о
    /// несохранённом (31000) и ConfirmDialog (32000). Порядок именно такой, потому что из-под панели
    /// можно уйти обеими дорогами: «Закрыть» ведёт к пересчёту листа, а тот показывает ошибки через
    /// ConfirmDialog. Ляг мы выше — рассказ об ошибке оказался бы под панелью, из-за которой появился.
    ///
    /// ПОДЛОЖКА ПЕРЕХВАТЫВАЕТ НАЖАТИЯ и не закрывает панель. Без Button на подложке кнопки листа,
    /// накрытые панелью, остались бы живыми: «Сохранить» под таблицей нажимался бы вслепую. А
    /// закрывать по щелчку мимо нельзя: игрок выбирает характеристики в два нажатия, и промах между
    /// ними стоил бы ему всего выбора. Дверь одна и подписана — «Закрыть».</summary>
    public class LevelPlanView : MonoBehaviour
    {
        /// <summary>Имя корневого объекта. Константа, потому что DismissStranded ищет панель ПО ИМЕНИ:
        /// после перезагрузки домена имя — единственная уцелевшая зацепка. Тот же приём и по той же
        /// причине, что у UnsavedChangesPrompt.CanvasName.</summary>
        const string CanvasName = "PlayerLevelPlanCanvas";

        static GameObject activeGO;

        const float PanelWidth = 1240f;
        const float PanelHeight = 900f;
        const float HeadHeight = 58f;
        const float RowHeight = 34f;
        const float LevelWidth = 62f;
        const float ChoiceWidth = 430f;

        /// <summary>Заведомо больше любой строки панели — та же уловка и по той же причине, что
        /// SheetView.StretchWidth: вертикальная раскладка отдаёт ребёнку Clamp(ширина колонки, min,
        /// preferred), поэтому строка с таким preferred получает ровно ширину колонки, а не ширину
        /// текста, который в неё положили.</summary>
        const float StretchWidth = 4000f;

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.62f);
        static readonly Color PanelFill = new Color(0.14f, 0.14f, 0.15f);
        static readonly Color RowFill = new Color(0.18f, 0.17f, 0.16f);
        static readonly Color HighlightFill = new Color(0.27f, 0.24f, 0.16f);
        static readonly Color Accent = new Color(0.85f, 0.78f, 0.58f);
        // Те же два цвета, что у листа (SheetView.Muted/Faint), и это СОЗНАТЕЛЬНАЯ копия: цвет — не
        // правило, а вид, и таскать его через чужой публичный API ради двух чисел незачем. Разойдись
        // они — панель и лист просто станут разными на глаз, а не начнут считать по-разному.
        static readonly Color Muted = new Color(0.55f, 0.53f, 0.50f);
        static readonly Color Faint = new Color(0.40f, 0.39f, 0.38f);

        CharacterFile file;
        RulesData rules;
        Action onClosed;

        /// <summary>Уровень, чью строку подсветить (0 — никакую). Ставится после «Повысить уровень»:
        /// без подсветки игрок, поднявшийся на 4 уровень, ищет свою строку среди двадцати одинаковых.</summary>
        int highlight;

        /// <summary>Уровень, чей список вариантов сейчас раскрыт, либо 0. Раскрыт ровно один: два
        /// списка по двадцать строк каждый превратили бы таблицу в простыню.</summary>
        int openLevel;

        /// <summary>Характеристики, отмеченные в дороге «Повысить характеристики», пока игрок не нажал
        /// «Готово». ДО нажатия в файле не меняется ничего: выбор из двух шагов, и записывать его
        /// после первого значило бы записать половину.</summary>
        readonly List<string> asiPick = new List<string>();

        Transform content;

        /// <summary>Убирает панель, до которой не дотянется ни один живой обработчик. Зовётся из
        /// PlayerPrepScreenController.Awake рядом с ConfirmDialog.DismissStranded и
        /// UnsavedChangesPrompt.DismissStranded — и ровно по той же причине: перезагрузка домена в
        /// режиме Play (обычная перекомпиляция при возврате фокуса окну) стирает и статическое поле
        /// activeGO, и все runtime-подписки кнопок, а холст остаётся жить. Осталась бы затемняющая
        /// подложка, глотающая нажатия поверх всей сцены, и убрать её было бы нечем.</summary>
        public static void DismissStranded()
        {
            if (activeGO == null) activeGO = GameObject.Find(CanvasName);
            if (activeGO == null) return;
            // SetActive(false) ДО отложенного Destroy: подложка перестаёт закрывать экран в этом
            // кадре, а не в конце его.
            activeGO.SetActive(false);
            UnityEngine.Object.Destroy(activeGO);
            activeGO = null;
        }

        /// <summary>Открыть панель. onClosed зовётся ровно один раз, панель к этому мигу уже
        /// уничтожена — чтобы обработчик мог показать поверх себя что угодно.
        ///
        /// Справочник передаётся снаружи, а не читается здесь: у листа он уже прочитан и, если не
        /// прочитался, разговор об этом ведёт лист. Второе чтение здесь означало бы второе место, где
        /// объясняют непрочитанный справочник.</summary>
        public static LevelPlanView Open(CharacterFile file, RulesData rules, int highlightLevel, Action onClosed)
        {
            if (activeGO != null) UnityEngine.Object.Destroy(activeGO);

            var canvasGO = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30500;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            activeGO = canvasGO;

            var view = canvasGO.AddComponent<LevelPlanView>();
            view.file = file;
            view.rules = rules;
            view.highlight = highlightLevel;
            // Подсвеченная строка сразу и раскрыта: панель после «Повысить уровень» открывается ради
            // одного-единственного выбора, и заставлять игрока искать и нажимать ту самую строку
            // значит открыть её зря. На уровне без ячейки выбора это ничего не раскрывает (см. Rebuild).
            view.openLevel = highlightLevel;
            view.onClosed = onClosed;
            view.BuildChrome();
            view.Rebuild();
            return view;
        }

        // ── Обвязка ──────────────────────────────────────────────────────────────

        void BuildChrome()
        {
            // Button без слушателя — тот же приём, что у ConfirmDialog и UnsavedChangesPrompt: глотает
            // нажатия, не делая ничего.
            var backdrop = NewRect(transform, "Backdrop", typeof(Image), typeof(Button));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = Backdrop;

            var panel = NewRect(transform, "Plan", typeof(Image));
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = PanelFill;

            var title = UiKit.Label(panel, "План прокачки", 24);
            title.color = Accent;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(20f, -14f);
            titleRt.sizeDelta = new Vector2(640f, 32f);

            var close = UiKit.Button(panel, "Закрыть", Close);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-20f, -12f);
            closeRt.sizeDelta = new Vector2(180f, 40f);

            // Прокрутка обязательна: строк двадцать, а под раскрытой ячейкой ещё список вариантов —
            // на 900 точках это не помещается никогда. Связка ScrollRect + Viewport(RectMask2D) +
            // Content(VLG + ContentSizeFitter) — та же, что у SheetView.BuildBody.
            //
            // ContentSizeFitter на САМОЙ панели не годится, в отличие от UnsavedChangesPrompt: у той
            // высота идёт от содержимого потому, что прокрутки внутри нет вовсе, а прокрутке нужен
            // прямоугольник заданного размера — иначе она растёт вместе с содержимым и не прокручивает
            // ничего.
            var scrollRt = NewRect(panel, "Body", typeof(ScrollRect));
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(20f, 20f);
            scrollRt.offsetMax = new Vector2(-20f, -HeadHeight);
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

            var body = NewRect(viewport, "Content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var vlg = body.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(0, 12, 0, 12);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.anchorMin = new Vector2(0f, 1f);
            body.anchorMax = new Vector2(1f, 1f);
            body.pivot = new Vector2(0.5f, 1f);
            body.sizeDelta = Vector2.zero;
            body.anchoredPosition = Vector2.zero;
            scroll.content = body;

            content = body;
        }

        /// <summary>Уничтожает панель и отвечает наружу. Destroy, а не DestroyImmediate: сюда попадают
        /// из обработчика нажатия, а тот уничтожил бы кнопку прямо посреди её собственного клика.</summary>
        void Close()
        {
            var owner = gameObject;
            var callback = onClosed;
            onClosed = null;                       // ответ ровно один раз
            if (activeGO == owner) activeGO = null;
            Destroy(owner);
            if (callback != null) callback();
        }

        // ── Таблица ──────────────────────────────────────────────────────────────

        void Rebuild()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                // Отвязка перед отложенным Destroy: иначе старая раскладка ещё кадр делила бы место с
                // новой. Тот же приём, что в SheetView.Rebuild.
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            var cls = rules == null || file == null ? null
                : rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            var rows = LevelPlanOps.Rows(cls, file);
            if (rows.Count == 0)
            {
                // Пустая таблица — не пустая панель: класса может не быть вовсе, а может быть чужой.
                // Молчание читалось бы как поломка.
                AddNote(content, cls == null
                    ? "Класс не выбран (или неизвестен справочнику) — таблицы прокачки нет."
                    : $"У класса «{cls.Name}» в справочнике нет ни одного уровня.", 17, Muted);
                return;
            }

            foreach (var row in rows)
            {
                BuildRow(row, cls);
                // Проверка на ячейку обязательна, а не «раз уровень совпал». openLevel ставится не
                // только нажатием, но и снаружи — подсвеченным уровнем после «Повысить уровень», — а
                // тот запросто оказывается уровнем без всякого выбора.
                if (openLevel == row.Level && !string.IsNullOrEmpty(row.ChoiceKind)) BuildOptions(row, cls);
            }
        }

        void BuildRow(PlanRow row, ClassDef cls)
        {
            bool choice = !string.IsNullOrEmpty(row.ChoiceKind);
            var rt = NewRect(content, "Row" + row.Level, typeof(Image), typeof(HorizontalLayoutGroup));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.preferredWidth = StretchWidth;
            rt.GetComponent<Image>().color = row.Level == highlight ? HighlightFill : RowFill;
            var hlg = rt.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(10, 10, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            if (choice)
            {
                // Кнопка ВИСИТ НА САМОЙ СТРОКЕ, а подписи внутри неё нажатий не крадут: uGUI ищет
                // ближайшего вверх по дереву, кто умеет отвечать на щелчок, — и находит эту кнопку.
                int level = row.Level;
                var btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = rt.GetComponent<Image>();
                btn.onClick.AddListener(() =>
                {
                    // Второе нажатие по той же строке сворачивает список. Отметки характеристик
                    // сбрасываются при КАЖДОМ переключении: перенеси мы их на соседний уровень,
                    // «Готово» записало бы туда выбор, которого игрок там не делал.
                    openLevel = openLevel == level ? 0 : level;
                    asiPick.Clear();
                    Rebuild();
                });
            }

            // Будущие уровни приглушены: таблица сразу читается как «вот докуда я дошёл».
            Color main = row.Reached ? new Color(0.92f, 0.90f, 0.86f) : Muted;
            Color side = row.Reached ? Muted : Faint;

            var level0 = UiKit.Label(rt, row.Level.ToString(), 17);
            level0.color = row.Reached ? Accent : Muted;
            Fixed(level0.gameObject, LevelWidth);

            var summary = UiKit.Label(rt, row.Summary ?? "—", 15);
            summary.color = side;
            var sumLe = summary.gameObject.AddComponent<LayoutElement>();
            sumLe.preferredWidth = 0f;              // остаток ширины, а не ширина текста
            sumLe.flexibleWidth = 1f;

            var cell = UiKit.Label(rt, choice ? ChoiceText(row, cls) : "", 15);
            cell.color = choice ? main : side;
            Fixed(cell.gameObject, ChoiceWidth);
        }

        /// <summary>Что написано в ячейке выбора: чем она может быть занята и чем занята сейчас.</summary>
        string ChoiceText(PlanRow row, ClassDef cls)
        {
            string what = row.ChoiceKind == "subclass" ? "подкласс" : "повышение характеристик или черта";
            return what + ": " + ChosenText(row, cls);
        }

        /// <summary>Что намечено. Три случая на каждое имя — «не выбрано», «неизвестно «id»» и само
        /// имя — разбирает SheetView.Part: та же тройка живёт в подзаголовке листа, и вторая её копия
        /// здесь склеила бы «выбран незнакомый» с «не выбран», как это уже было на листе.</summary>
        string ChosenText(PlanRow row, ClassDef cls)
        {
            if (row.ChosenKind == "subclass")
            {
                string name = cls == null ? null
                    : cls.Subclasses.FirstOrDefault(s => s.Id == row.ChosenId)?.Name;
                return SheetView.Part(name, row.ChosenId, "подкласс", "не выбрано");
            }
            if (row.ChosenKind == "feat")
            {
                string name = rules == null ? null
                    : rules.Feats.FirstOrDefault(f => f.Id == row.ChosenId)?.Name;
                return SheetView.Part(name, row.ChosenId, "черта", "не выбрано");
            }
            if (row.ChosenKind == "asi")
            {
                var bumps = BumpsAt(row.Level);
                // Пометка без прибавок — законное состояние: обе характеристики могли упереться в
                // потолок 20. Говорим об этом словами, а не пустым местом.
                if (bumps.Count == 0) return "прибавки не записаны";
                return string.Join(", ", bumps
                    .Select(b => SheetMath.Signed(b.Amount) + " " + SheetMath.AbilityName(b.AbilityId))
                    .ToArray());
            }
            return "не выбрано";
        }

        List<AbilityBump> BumpsAt(int level) => file == null
            ? new List<AbilityBump>()
            : file.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == level).ToList();

        // ── Варианты под раскрытой строкой ───────────────────────────────────────

        void BuildOptions(PlanRow row, ClassDef cls)
        {
            if (row.ChoiceKind == "subclass") BuildSubclassOptions(row, cls);
            else if (row.ChoiceKind == "asi") BuildAsiOptions(row, cls);
            // Третьего вида ячейки в правилах нет, но справочник — данные: незнакомый вид выбора
            // показывается словами, а не списком чужих вариантов. Подсунуть игроку черты там, где
            // класс просит чего-то своего, — худший ответ, чем честное «не знаю».
            else AddNote(content, $"Выбор вида «{row.ChoiceKind}» эта панель показывать не умеет.", 15, Muted);

            if (!string.IsNullOrEmpty(row.ChosenKind))
            {
                int level = row.Level;
                AddWideButton("Убрать выбор", () =>
                {
                    LevelChoiceOps.Clear(file, level, cls);
                    openLevel = 0;
                    asiPick.Clear();
                    Rebuild();
                });
            }
        }

        void BuildSubclassOptions(PlanRow row, ClassDef cls)
        {
            AddNote(content, "Подкласс на " + row.Level + " уровне", 17, Accent);
            var subs = cls == null ? new List<SubclassDef>() : cls.Subclasses;
            if (subs.Count == 0)
            {
                // Класс без подклассов в справочнике — не поломка панели, а состояние данных.
                AddNote(content, cls == null
                    ? "Класса нет — выбирать не из чего."
                    : $"У класса «{cls.Name}» в справочнике нет ни одного подкласса.", 15, Muted);
                return;
            }
            foreach (var sub in subs)
            {
                string id = sub.Id;
                int level = row.Level;
                AddWideButton(sub.Name, () =>
                {
                    LevelChoiceOps.ChooseSubclass(file, level, id);
                    openLevel = 0;
                    Rebuild();
                });
                if (!string.IsNullOrEmpty(sub.Text)) AddNote(content, sub.Text, 14, Faint);
            }
        }

        void BuildAsiOptions(PlanRow row, ClassDef cls)
        {
            int level = row.Level;

            AddNote(content, "Повысить характеристики", 17, Accent);
            AddNote(content, "Одна характеристика на +2 либо две по +1. Выше 20 не поднимается.", 14, Faint);

            var line = NewRect(content, "Abilities", typeof(HorizontalLayoutGroup));
            var lineLe = line.gameObject.AddComponent<LayoutElement>();
            lineLe.preferredHeight = 40f;
            lineLe.preferredWidth = StretchWidth;
            var hlg = line.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            foreach (var id in SheetMath.AbilityOrder())
            {
                string abilityId = id;
                // «Сколько сейчас» спрашивается у чистого слоя и НА ТОМ уровне, куда ставится прибавка:
                // к этому мигу действуют все прибавки по него включительно. Потолок 20 тоже его
                // правило — здесь только показ недоступной кнопки.
                int now = SheetMath.AbilityTotal(file, abilityId, level);
                bool capped = now >= 20;
                bool picked = asiPick.Contains(abilityId);
                string label = (picked ? "[×] " : "[ ] ") + SheetMath.AbilityName(abilityId) + " " + now;
                var btn = UiKit.Button(line, label, () =>
                {
                    if (asiPick.Contains(abilityId)) asiPick.Remove(abilityId);
                    else if (asiPick.Count < 2) asiPick.Add(abilityId);
                    Rebuild();
                }, !capped);
                var text = btn.GetComponentInChildren<Text>();
                if (text != null) { text.fontSize = 15; if (picked) text.color = Accent; }
                Fixed(btn.gameObject, 186f);
            }

            bool ready = asiPick.Count == 1 || asiPick.Count == 2;
            var pickedNames = asiPick.Select(SheetMath.AbilityName).ToArray();
            string done = asiPick.Count == 1 ? $"Готово: +2 {pickedNames[0]}"
                        : asiPick.Count == 2 ? $"Готово: +1 {pickedNames[0]}, +1 {pickedNames[1]}"
                        : "Готово (отметь одну или две)";
            AddWideButton(done, () =>
            {
                LevelChoiceOps.ChooseAsi(file, level, new List<string>(asiPick));
                openLevel = 0;
                asiPick.Clear();
                Rebuild();
            }, ready);

            AddNote(content, "Взять черту", 17, Accent);
            var feats = LevelChoiceOps.FeatOptions(rules, level);
            if (feats.Count == 0)
            {
                AddNote(content, "Черт этого разряда в справочнике нет.", 15, Muted);
                return;
            }
            foreach (var feat in feats)
            {
                string id = feat.Id;
                AddWideButton(feat.Name, () =>
                {
                    LevelChoiceOps.ChooseFeat(file, level, id);
                    openLevel = 0;
                    asiPick.Clear();
                    Rebuild();
                });
                if (!string.IsNullOrEmpty(feat.Text)) AddNote(content, feat.Text, 14, Faint);
            }
        }

        // ── Мелочи ───────────────────────────────────────────────────────────────

        void AddWideButton(string label, Action onClick, bool enabled = true)
        {
            var btn = UiKit.Button(content, label, onClick, enabled);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 38f;
            le.preferredWidth = StretchWidth;
            var text = btn.GetComponentInChildren<Text>();
            if (text != null) { text.fontSize = 16; text.alignment = TextAnchor.MiddleLeft; }
        }

        /// <summary>Подпись отдельной строкой. Высота НЕ задаётся: её спрашивает у самого текста
        /// вертикальная раскладка, уже посчитавшая ширину. Напиши мы сюда число — рассказ о черте
        /// длиннее одной строки нарисовался бы ПОВЕРХ следующей: у подписей проекта verticalOverflow =
        /// Overflow, они не режутся.</summary>
        static void AddNote(Transform parent, string text, int size, Color color)
        {
            var label = UiKit.Label(parent, text, size);
            label.color = color;
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = StretchWidth;
        }

        static void Fixed(GameObject go, float width)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
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
