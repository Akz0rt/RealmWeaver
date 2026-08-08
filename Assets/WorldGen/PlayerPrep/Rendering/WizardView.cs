using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Мастер создания персонажа: восемь шагов над ОДНИМ и тем же CharacterFile.
    ///
    /// Своей модели у мастера нет — каждый выбор пишется прямо в файл, который передал корень сцены.
    /// Иначе «выбор класса» существовал бы в двух видах, мастерском и листовом, и они разошлись бы;
    /// та же причина записана в шапке WizardOps.
    ///
    /// НИЧЕГО НЕ ЗАПРЕЩАЕМ, ВСЁ ОБЪЯСНЯЕМ: «Назад» доступен всегда, по полоске шагов можно щёлкать в
    /// любом порядке, а «Далее» не проверяет, заполнен ли шаг. Незаконченный персонаж — нормальное
    /// состояние: чего не хватает, перечисляет восьмой шаг, и сохранить его можно всё равно.
    ///
    /// Слой рисования тонкий НАМЕРЕННО. Что доступно, что потеряется при смене класса, сколько
    /// навыков осталось и чего не хватает — считают WizardOps и SheetMath, которые покрыты
    /// самопроверками; здесь этих правил нет и быть не должно. Ровно два места, где вид считает сам,
    /// названы в комментариях по имени (покупка очков и броски) — их место в WizardOps.
    ///
    /// СТРОИТСЯ ИЗ Build, А НЕ ИЗ Awake, и это не вкусовщина. Правило
    /// WorkspaceBuilder.DemolishForRebuild (WorkspaceBuilder.cs:133-136) — «строишь интерфейс в Awake,
    /// снеси прошлую постройку» — здесь неприменимо именно поэтому: Unity зовёт Awake повторно при
    /// каждой перекомпиляции скриптов, но Build зовёт только PlayerPrepScreenController.ShowWizard, а
    /// жизненным циклом всего содержимого владеет ClearContent корня. Awake здесь нет вовсе — и
    /// заводить его, чтобы строить в нём интерфейс, нельзя: тогда правило станет применимым.
    ///
    /// Обратное правило соблюдается тоже: шаги переключаются из обработчика нажатия, поэтому пересборка
    /// идёт через отложенный Destroy, а не DestroyImmediate — см. Rebuild.</summary>
    public class WizardView : MonoBehaviour
    {
        // ── Полоска шагов и фразы «что это значит за столом» ──────────────────────
        // Фразы фиксированные и лежат в коде: это не пересказ правил, а единственное объяснение,
        // зачем шаг нужен игроку. Порядок строк = порядок шагов, менять их врозь нельзя.
        static readonly string[] StepNames =
        {
            "Кто ты", "Вид", "Класс", "Предыстория", "Характеристики", "Навыки", "Снаряжение", "Готово"
        };

        static readonly string[] StepPhrases =
        {
            "Имя и лицо — единственное, что увидят другие игроки прежде цифр.",
            "Вид — это тело: скорость, чувства и пара особенностей, которые с тобой навсегда.",
            "Класс — это чем ты занят в бою и что умеешь между боями.",
            "Предыстория — кем ты был до приключений. От неё же приходят прибавки к характеристикам.",
            "Шесть чисел, из которых считается почти всё остальное. 15 — сильная сторона, 8 — слабая.",
            "Навыки — то, в чём ты хорош вне боя. Уже полученное показано серым: дважды взять нельзя.",
            "С чем ты выходишь в первый поход.",
            "Лист собран. Чего не хватает — перечислено; доделать можно потом.",
        };

        const int StepCount = 8;

        static readonly string[] AbilityIds = { "str", "dex", "con", "int", "wis", "cha" };
        static readonly string[] AbilityNames =
            { "Сила", "Ловкость", "Телосложение", "Интеллект", "Мудрость", "Харизма" };

        const float TopBarHeight = 96f;
        const float PhraseHeight = 46f;
        const float BottomBarHeight = 66f;
        const float ListColumnWidth = 300f;
        const float RowHeight = 38f;

        static readonly Color Muted = new Color(0.55f, 0.53f, 0.50f);
        static readonly Color Faint = new Color(0.40f, 0.39f, 0.38f);
        static readonly Color Accent = new Color(0.85f, 0.78f, 0.58f);
        static readonly Color SelectedFill = new Color(0.28f, 0.26f, 0.21f);
        static readonly Color HairLine = new Color(0.20f, 0.20f, 0.22f);

        CharacterFile file;
        RulesData rules;
        string rulesError;
        Action onFinished;

        int step;

        /// <summary>0 — стандартный набор (по умолчанию), 1 — покупка очков, 2 — броски.</summary>
        int abilityMode;

        /// <summary>0 — +2/+1, 1 — +1/+1/+1. Прибавки предыстории, шаг 5.</summary>
        int bumpLayout;
        string bumpPlus2, bumpPlus1;

        /// <summary>Класс и предыстория, под которые уже разложено снаряжение, — «класс|предыстория».
        /// null = на шаге снаряжения ещё не были. Не bool: простого «уже раскладывали» не хватает, см.
        /// SyncEquipment.</summary>
        string equipmentSeededFor;

        /// <summary>Единственная точка входа. Корень сцены зовёт её из ShowWizard.</summary>
        public static WizardView Build(Transform parent, CharacterFile file, Action onFinished)
        {
            var go = new GameObject("Wizard", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var view = go.AddComponent<WizardView>();
            view.file = file;
            view.onFinished = onFinished;
            // Справочник читается через try: SheetRulesSource.Rules не возвращает null, а бросает.
            // Дойти сюда с непрочитанным справочником нельзя (корень при rules == null не рисует даже
            // кнопку «Создать персонажа»), но пустой экран вместо объяснения — худшее, чем можно
            // ответить на невозможное.
            try { view.rules = SheetRulesSource.Rules; }
            catch (Exception ex) { view.rules = null; view.rulesError = ex.Message; }

            view.SyncBumpStateFromFile();
            view.Rebuild();
            return view;
        }

        /// <summary>Восстанавливает состояние переключателя прибавок по уже лежащим в файле. Мастер
        /// открывают и на сохранённом персонаже (задача 12), и тогда «+2/+1 или +1/+1/+1» должно
        /// показывать то, что человек выбрал в прошлый раз, а не умолчание.</summary>
        void SyncBumpStateFromFile()
        {
            if (file == null) return;
            var fromBg = file.Bumps.Where(b => b.Source == "background").ToList();
            if (fromBg.Count == 3 && fromBg.All(b => b.Amount == 1)) { bumpLayout = 1; return; }
            bumpLayout = 0;
            bumpPlus2 = fromBg.FirstOrDefault(b => b.Amount == 2)?.AbilityId;
            bumpPlus1 = fromBg.FirstOrDefault(b => b.Amount == 1)?.AbilityId;
        }

        // ── Пересборка ───────────────────────────────────────────────────────────

        /// <summary>Пересобирает экран мастера целиком — одна дорога вместо восьми частичных обновлений.
        ///
        /// Отложенный Destroy, а НЕ DestroyImmediate: сюда попадают из обработчика нажатия (кнопка шага,
        /// строка списка, «Далее»), а DestroyImmediate уничтожил бы кнопку прямо посреди её собственного
        /// клика. SetParent(null) перед Destroy — потому что Destroy откладывается до конца кадра, и без
        /// отвязки старая раскладка ещё кадр делила бы место с новой. Ровно то же и по тем же двум
        /// причинам делает PlayerPrepScreenController.ClearContent.</summary>
        void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            BuildTopBar();
            BuildPhrase();
            var content = BuildBody();
            BuildBottomBar();

            if (rules == null)
            {
                AddLabel(content, "Справочник правил не загрузился, собирать персонажа не из чего:\n"
                                  + rulesError, 18, null, 90f);
                return;
            }

            switch (step)
            {
                case 0: BuildStepWhoAreYou(content); break;
                case 1: BuildStepRace(content); break;
                case 2: BuildStepClass(content); break;
                case 3: BuildStepBackground(content); break;
                case 4: BuildStepAbilities(content); break;
                case 5: BuildStepSkills(content); break;
                case 6: BuildStepEquipment(content); break;
                default: BuildStepDone(content); break;
            }
        }

        void GoToStep(int next)
        {
            step = Mathf.Clamp(next, 0, StepCount - 1);
            Rebuild();
        }

        // ── Обвязка мастера ──────────────────────────────────────────────────────

        void BuildTopBar()
        {
            var bar = NewRect(transform, "TopBar");
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(24f, -TopBarHeight);
            bar.offsetMax = new Vector2(-24f, 0f);

            // Возврат к списку живёт здесь, а не только на восьмом шаге: запереть игрока в мастере
            // нельзя. Спрашиваем безусловно — отслеживания изменений в листе нет вовсе, а BackToList
            // обнуляет Current и CurrentPath.
            var back = UiKit.Button(bar, "← К списку листов", AskBackToList);
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 1f);
            backRt.anchorMax = new Vector2(0f, 1f);
            backRt.pivot = new Vector2(0f, 1f);
            backRt.anchoredPosition = new Vector2(0f, -6f);
            backRt.sizeDelta = new Vector2(240f, 34f);

            var title = UiKit.Label(bar, $"Шаг {step + 1} из {StepCount} · {StepNames[step]}", 21,
                TextAnchor.MiddleRight);
            title.color = Accent;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(1f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(1f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -6f);
            titleRt.sizeDelta = new Vector2(620f, 34f);

            // Полоска шагов. По ней можно щёлкать в любом порядке и на любой шаг — это и есть
            // «вернуться можно всегда».
            var strip = NewRect(bar, "Steps", typeof(HorizontalLayoutGroup));
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(0f, 8f);
            strip.offsetMax = new Vector2(0f, 44f);
            var row = strip.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childForceExpandWidth = true;
            row.childControlHeight = true;
            row.childForceExpandHeight = true;

            for (int i = 0; i < StepCount; i++)
            {
                int target = i;
                var chip = UiKit.Button(strip, $"{i + 1}. {StepNames[i]}", () => GoToStep(target));
                chip.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                chip.transform.GetComponentInChildren<Text>().fontSize = 15;
                if (i == step) MarkSelected(chip, true);
            }
        }

        void BuildPhrase()
        {
            var band = NewRect(transform, "Phrase");
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.offsetMin = new Vector2(24f, -(TopBarHeight + PhraseHeight));
            band.offsetMax = new Vector2(-24f, -TopBarHeight);

            var text = UiKit.Label(band, StepPhrases[step], 17);
            text.color = Muted;
            text.fontStyle = FontStyle.Italic;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, 4f);
            rt.offsetMax = new Vector2(0f, -4f);

            var line = NewRect(band, "EdgeLine", typeof(Image));
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.offsetMin = Vector2.zero;
            line.offsetMax = new Vector2(0f, 1f);
            line.GetComponent<Image>().color = HairLine;
        }

        /// <summary>Прокручиваемая середина: ScrollRect + Viewport(RectMask2D) + Content
        /// (VerticalLayoutGroup + ContentSizeFitter) — та же связка, что у всех боковых панелей проекта
        /// (BattleGridScreen.BuildToolbar, DungeonInspectorPanel.BuildScroll). Высота содержимого
        /// диктуется содержимым, а если оно не влезло — прокруткой: список из восемнадцати навыков и
        /// список снаряжения в экран не помещаются.</summary>
        Transform BuildBody()
        {
            var scrollRt = NewRect(transform, "Body", typeof(ScrollRect));
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(24f, BottomBarHeight);
            scrollRt.offsetMax = new Vector2(-24f, -(TopBarHeight + PhraseHeight));
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

            // «Назад» доступен везде, кроме самого первого шага, где идти просто некуда; «Далее» не
            // спрашивает, заполнен ли шаг, — незаполненный шаг это разрешённое состояние.
            var back = UiKit.Button(bar, "← Назад", () => GoToStep(step - 1), step > 0);
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 0.5f);
            backRt.anchorMax = new Vector2(0f, 0.5f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.sizeDelta = new Vector2(220f, 44f);

            var next = UiKit.Button(bar, "Далее →", () => GoToStep(step + 1), step < StepCount - 1);
            var nextRt = (RectTransform)next.transform;
            nextRt.anchorMin = new Vector2(1f, 0.5f);
            nextRt.anchorMax = new Vector2(1f, 0.5f);
            nextRt.pivot = new Vector2(1f, 0.5f);
            nextRt.sizeDelta = new Vector2(220f, 44f);
        }

        void AskBackToList()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            ConfirmDialog.Show(UiKit.Font, "Вернуться к списку листов?",
                "Несохранённые изменения листа будут потеряны.",
                confirmed =>
                {
                    // Ответ приходит ЧЕРЕЗ КАДР, и к этому времени мастера может не быть вовсе:
                    // перекомпиляция скриптов при открытом диалоге пересобирает сцену, а замыкание
                    // остаётся жить. Проверка на уничтоженный компонент — единственная защита.
                    if (this == null) return;
                    if (confirmed) root.BackToList();
                });
        }

        // ── Шаг 1: кто ты ────────────────────────────────────────────────────────

        void BuildStepWhoAreYou(Transform content)
        {
            AddCaption(content, "Имя персонажа");
            AddInput(content, file.Name, "Как его зовут за столом", multiline: false, height: 44f,
                onChanged: v => file.Name = v);

            AddSpacer(content, 10f);
            AddCaption(content, "Портрет");

            var row = AddRow(content, 46f);
            AddButton(row, "Портрет…", 220f, PickPortrait);
            var status = AddLabel(row, PortraitStatus(), 15, Muted, null);
            status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddLabel(content, "Картинка уменьшается до 512 точек по большой стороне и ложится внутрь "
                              + "файла листа — отдельный файл рядом хранить не нужно.", 14, Faint, 40f);
        }

        string PortraitStatus()
        {
            if (file.Portrait == null || file.Portrait.Length == 0) return "портрет не выбран";
            return $"портрет загружен, {Mathf.Max(1, file.Portrait.Length / 1024)} КБ";
        }

        void PickPortrait()
        {
            try
            {
                var bytes = PortraitImport.PickAndDownscale();
                // Отменённый диалог отдаёт null. Затирать им уже выбранный портрет нельзя: отмена
                // означает «ничего не менять», а не «убрать».
                if (bytes == null || bytes.Length == 0) return;
                file.Portrait = bytes;
                Rebuild();
            }
            catch (Exception ex)
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось загрузить портрет", ex.Message);
            }
        }

        // ── Шаг 2: вид ───────────────────────────────────────────────────────────

        void BuildStepRace(Transform content)
        {
            var (list, details) = AddTwoColumns(content);

            foreach (var race in rules.Races)
            {
                var r = race;
                AddChoiceRow(list, r.Name, file.RaceId == r.Id, () =>
                {
                    file.RaceId = r.Id;
                    Rebuild();
                });
            }

            var chosen = rules.Races.FirstOrDefault(r => r.Id == file.RaceId);
            if (chosen == null)
            {
                AddLabel(details, "Выберите вид слева.", 17, Muted, null);
                return;
            }

            AddLabel(details, chosen.Name, 24, null, null);
            AddLabel(details, chosen.Blurb, 17, Muted, null);
            AddLabel(details, $"Скорость: {chosen.Speed} футов", 17, null, null);
            AddSpacer(details, 6f);
            AddLabel(details, "Особенности", 18, Accent, null);
            foreach (var trait in chosen.Traits)
            {
                AddLabel(details, "• " + trait.Name, 16, null, null);
                AddLabel(details, trait.Text, 15, Muted, null);
            }
        }

        // ── Шаг 3: класс ─────────────────────────────────────────────────────────

        void BuildStepClass(Transform content)
        {
            var (list, details) = AddTwoColumns(content);

            foreach (var cls in rules.Classes)
            {
                var c = cls;
                AddChoiceRow(list, c.Name, file.ClassId == c.Id, () => ChooseClass(c.Id));
            }

            var chosen = CurrentClass();
            if (chosen == null)
            {
                AddLabel(details, "Выберите класс слева.", 17, Muted, null);
                return;
            }

            AddLabel(details, chosen.Name, 24, null, null);
            AddLabel(details, chosen.Blurb, 17, Muted, null);
            AddLabel(details, $"Кость хитов: {chosen.HitDie}", 17, null, null);
            AddLabel(details, "Спасброски: " + JoinAbilityNames(chosen.SaveProficiencies), 17, null, null);
            AddLabel(details, $"Навыков на выбор: {chosen.SkillPickCount} (шаг 6)", 17, null, null);
            if (chosen.ExpertiseLevel > 0)
                AddLabel(details, $"Компетентность: {chosen.ExpertisePickCount} навыка "
                                  + $"с {chosen.ExpertiseLevel} уровня", 17, null, null);
        }

        /// <summary>Смена класса — единственное разрушительное действие мастера, поэтому спрашиваем ДО,
        /// а не откатываем ПОСЛЕ. Что именно пропадёт, называет WizardOps.DescribeClassChange, а снимает
        /// ровно то же самое WizardOps.ApplyClassChange: считать потери здесь своими силами означало бы
        /// пообещать одно, а сделать другое.
        ///
        /// Пустой список потерь диалога НЕ показывает — первый в жизни выбор класса вопросов не задаёт.
        /// Повторное нажатие на уже выбранный класс не делает ничего.</summary>
        void ChooseClass(string newClassId)
        {
            if (file.ClassId == newClassId) return;

            var losses = WizardOps.DescribeClassChange(file, rules, newClassId);
            if (losses.Count == 0)
            {
                WizardOps.ApplyClassChange(file, rules, newClassId);
                Rebuild();
                return;
            }

            string name = rules.Classes.FirstOrDefault(c => c.Id == newClassId)?.Name ?? newClassId;
            string title = string.IsNullOrEmpty(file.ClassId)
                ? $"Выбрать класс «{name}»?"
                : $"Сменить класс на «{name}»?";
            // Кнопка подтверждения в ConfirmDialog подписана словом «Удалить» — общий код части ДМ,
            // трогать его в этой арке нельзя. Поэтому список потерь стоит прямо в тексте.
            ConfirmDialog.Show(UiKit.Font, title,
                "Пропадёт:\n• " + string.Join("\n• ", losses.ToArray()),
                confirmed =>
                {
                    // Диалог отвечает через кадр — кнопки, с которой начали, к этому времени уже нет,
                    // а сам мастер мог не пережить перекомпиляцию. Трогаем только file, rules и this.
                    if (this == null) return;
                    if (!confirmed) return;
                    WizardOps.ApplyClassChange(file, rules, newClassId);
                    Rebuild();
                });
        }

        // ── Шаг 4: предыстория ───────────────────────────────────────────────────

        void BuildStepBackground(Transform content)
        {
            var (list, details) = AddTwoColumns(content);

            foreach (var background in rules.Backgrounds)
            {
                var b = background;
                AddChoiceRow(list, b.Name, file.BackgroundId == b.Id, () => ChooseBackground(b.Id));
            }

            var chosen = CurrentBackground();
            if (chosen != null)
            {
                AddLabel(details, chosen.Name, 24, null, null);
                AddLabel(details, chosen.Text, 16, Muted, null);
                AddLabel(details, "Навыки: " + JoinSkillNames(chosen.SkillIds), 17, null, null);
                AddLabel(details, "Прибавки к характеристикам (разложите на шаге 5): "
                                  + JoinAbilityNames(chosen.AbilityChoices), 17, null, null);
                AddLabel(details, "Снаряжение: " + JoinItemNames(chosen.Equipment), 16, null, null);

                var feat = rules.Feats.FirstOrDefault(f => f.Id == chosen.OriginFeatId);
                if (feat != null)
                {
                    AddSpacer(details, 6f);
                    AddLabel(details, $"Черта происхождения: {feat.Name}", 18, Accent, null);
                    AddLabel(details, feat.Text, 15, Muted, null);
                    AddLabel(details, "Она фиксирована — выбирать не нужно.", 14, Faint, null);
                }
            }
            else AddLabel(details, "Выберите предысторию слева.", 17, Muted, null);

            AddSpacer(content, 10f);
            AddCaption(content, "Кем ты был до приключений");
            AddInput(content, file.Backstory, "Своими словами: откуда родом, что оставил позади…",
                multiline: true, height: 150f, onChanged: v => file.Backstory = v);
        }

        /// <summary>Смена предыстории сбрасывает прибавки. Они кладутся ТОЛЬКО в три характеристики,
        /// которые называет предыстория (SheetMath проверяет это буквально), и прибавка, оставшаяся от
        /// прошлой, дала бы на восьмом шаге строку «прибавка положена в характеристику, которой
        /// предыстория не даёт» — непонятную тому, кто просто передумал.
        ///
        /// Раскладка сбрасывается на «+2 и +1» ВМЕСТЕ с ними, и это не мелочь: останься выбранным
        /// «+1/+1/+1», прибавки новой предыстории разложились бы сами собой, не спросив, — а шаг 5 после
        /// этого показал бы готовые «3 из 3», которых игрок не выбирал.</summary>
        void ChooseBackground(string newBackgroundId)
        {
            if (file.BackgroundId == newBackgroundId) return;
            file.BackgroundId = newBackgroundId;
            bumpLayout = 0;
            bumpPlus2 = null;
            bumpPlus1 = null;
            WriteBackgroundBumps();
            Rebuild();
        }

        // ── Шаг 5: характеристики ────────────────────────────────────────────────

        void BuildStepAbilities(Transform content)
        {
            var modes = AddRow(content, 44f);
            AddModeButton(modes, "Стандартный набор", 0);
            AddModeButton(modes, "Покупка очков", 1);
            AddModeButton(modes, "Броски", 2);

            if (abilityMode == 0) BuildStandardArray(content);
            else if (abilityMode == 1) BuildPointBuy(content);
            else BuildRolls(content);

            AddSpacer(content, 12f);
            BuildBackgroundBumps(content);
        }

        void AddModeButton(Transform parent, string label, int mode)
        {
            var btn = AddButton(parent, label, 260f, () =>
            {
                abilityMode = mode;
                // Переключение на стандартный набор раскладывает его сразу, если сейчас в
                // характеристиках лежит не он: иначе «стандартный набор» показывал бы броски.
                if (mode == 0 && !LooksLikeStandardArray()) ApplyStandardArray();
                Rebuild();
            });
            if (abilityMode == mode) MarkSelected(btn, true);
        }

        void BuildStandardArray(Transform content)
        {
            // Нетронутые характеристики (все шесть нулевые) раскладываются сразу: шесть нулей под
            // заголовком «стандартный набор» читаются как поломка, а затирать здесь нечего.
            if (AbilityIds.All(id => file.Base.Get(id) == 0)) ApplyStandardArray();

            AddLabel(content, "Шесть заранее известных чисел — 15, 14, 13, 12, 10, 8 — раскладываются по "
                              + "характеристикам. Стрелки меняют числа местами: набор всегда остаётся целым.",
                15, Muted, 44f);
            AddButton(AddRow(content, 44f), "Предложить под класс", 300f, () =>
            {
                ApplyStandardArray();
                Rebuild();
            });

            for (int i = 0; i < AbilityIds.Length; i++)
            {
                string id = AbilityIds[i];
                var row = AddRow(content, RowHeight);
                AddFixedLabel(row, AbilityNames[i], 220f, 17, null);
                AddFixedLabel(row, file.Base.Get(id).ToString(), 60f, 20, Accent);
                // Стрелки и «×» ниже — из тех знаков, что в проекте уже нарисованы этим шрифтом
                // («← Главный экран» в шапке сцены). Геометрические фигуры (◀, ●) в LegacyRuntime.ttf
                // есть не наверняка, а пустой квадрат вместо стрелки читается как поломка.
                AddButton(row, "←", 54f, () => { CycleStandard(id, -1); Rebuild(); });
                AddButton(row, "→", 54f, () => { CycleStandard(id, +1); Rebuild(); });
                AddLabel(row, ExplainAbility(id), 15, Muted, null)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        void BuildPointBuy(Transform content)
        {
            // ЧЕСТНАЯ ОГОВОРКА, А НЕ ЗАГЛУШКА: таблица стоимости очков (8 — 0, …, 15 — 9) и бюджет в 27
            // — это ПРАВИЛА, а их место в WizardOps, который покрыт самопроверками; слой рисования
            // правил не считает (см. шапку файла). Пока их там нет, счётчик бюджета показывать нельзя:
            // неправильное число хуже отсутствующего. Числа вводятся вручную.
            AddLabel(content, "Бюджет в 27 очков программа пока НЕ считает — распределите его сами. "
                              + "Стоимость по правилам: 8 — 0 очков, 9 — 1, 10 — 2, 11 — 3, 12 — 4, "
                              + "13 — 5, 14 — 7, 15 — 9.", 15, Accent, 44f);
            BuildSteppers(content);
        }

        void BuildRolls(Transform content)
        {
            AddLabel(content, "Четыре кости d6, худшая отбрасывается — и так шесть раз. Числа ложатся по "
                              + "характеристикам от большего к меньшему, начиная с ключевых для класса.",
                15, Muted, 44f);
            AddButton(AddRow(content, 44f), "Бросить кости", 300f, () =>
            {
                RollAbilities();
                Rebuild();
            });
            BuildSteppers(content);
        }

        /// <summary>Ряды с «−» и «+». Общие для покупки очков и бросков: и там и там числа
        /// произвольные, в отличие от стандартного набора, где они переставляются.</summary>
        void BuildSteppers(Transform content)
        {
            for (int i = 0; i < AbilityIds.Length; i++)
            {
                string id = AbilityIds[i];
                var row = AddRow(content, RowHeight);
                AddFixedLabel(row, AbilityNames[i], 220f, 17, null);
                AddFixedLabel(row, file.Base.Get(id).ToString(), 60f, 20, Accent);
                AddButton(row, "-", 54f, () => { StepAbility(id, -1); Rebuild(); });
                AddButton(row, "+", 54f, () => { StepAbility(id, +1); Rebuild(); });
                AddLabel(row, ExplainAbility(id), 15, Muted, null)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        void BuildBackgroundBumps(Transform content)
        {
            var bg = CurrentBackground();
            AddCaption(content, "Прибавки от предыстории");
            if (bg == null)
            {
                AddLabel(content, "Предыстория не выбрана — прибавки приходят от неё (шаг 4).", 16, Muted, null);
                return;
            }

            AddLabel(content, $"Предыстория «{bg.Name}» даёт три очка в пределах: "
                              + JoinAbilityNames(bg.AbilityChoices) + ".", 16, Muted, null);

            var layoutRow = AddRow(content, 44f);
            AddBumpLayoutButton(layoutRow, "+2 и +1", 0);
            AddBumpLayoutButton(layoutRow, "+1, +1 и +1", 1);

            if (bumpLayout == 0)
            {
                AddLabel(content, "Куда +2:", 16, null, null);
                var plus2 = AddRow(content, RowHeight);
                foreach (var abilityId in bg.AbilityChoices)
                {
                    string id = abilityId;
                    var btn = AddButton(plus2, AbilityName(id), 230f, () =>
                    {
                        bumpPlus2 = id;
                        // Одна характеристика не может получить обе прибавки: +1 уступает место.
                        if (bumpPlus1 == id) bumpPlus1 = null;
                        WriteBackgroundBumps();
                        Rebuild();
                    });
                    if (bumpPlus2 == id) MarkSelected(btn, true);
                }

                AddLabel(content, "Куда +1:", 16, null, null);
                var plus1 = AddRow(content, RowHeight);
                foreach (var abilityId in bg.AbilityChoices)
                {
                    string id = abilityId;
                    var btn = AddButton(plus1, AbilityName(id), 230f, () =>
                    {
                        bumpPlus1 = id;
                        if (bumpPlus2 == id) bumpPlus2 = null;
                        WriteBackgroundBumps();
                        Rebuild();
                    }, bumpPlus2 != id);
                    if (bumpPlus1 == id) MarkSelected(btn, true);
                }
            }
            else AddLabel(content, "Все три характеристики предыстории получают по +1.", 16, Muted, null);

            AddSpacer(content, 6f);
            foreach (var abilityId in bg.AbilityChoices)
                AddLabel(content, ExplainAbility(abilityId), 17, null, null);
        }

        void AddBumpLayoutButton(Transform parent, string label, int layout)
        {
            var btn = AddButton(parent, label, 260f, () =>
            {
                bumpLayout = layout;
                WriteBackgroundBumps();
                Rebuild();
            });
            if (bumpLayout == layout) MarkSelected(btn, true);
        }

        /// <summary>Переписывает прибавки предыстории начисто. ПЕРЕПИСЫВАЕТ, а не дописывает: SheetMath
        /// требует, чтобы сумма прибавок с Source == "background" была ровно 3, и дописывание при каждой
        /// перемене раскладки давало бы 6 и строку «разложены на 6 из 3», необъяснимую для игрока.</summary>
        void WriteBackgroundBumps()
        {
            file.Bumps.RemoveAll(b => b.Source == "background");
            var bg = CurrentBackground();
            if (bg == null) return;

            if (bumpLayout == 1)
            {
                foreach (var id in bg.AbilityChoices)
                    file.Bumps.Add(new AbilityBump { Source = "background", AbilityId = id, Amount = 1 });
                return;
            }

            if (!string.IsNullOrEmpty(bumpPlus2))
                file.Bumps.Add(new AbilityBump { Source = "background", AbilityId = bumpPlus2, Amount = 2 });
            if (!string.IsNullOrEmpty(bumpPlus1) && bumpPlus1 != bumpPlus2)
                file.Bumps.Add(new AbilityBump { Source = "background", AbilityId = bumpPlus1, Amount = 1 });
        }

        /// <summary>«Ловкость 15 → 17 (+2)». Итог считается сложением прибавок файла, а не отдельной
        /// памятью мастера, — показанное число и число на листе приходят из одного места.</summary>
        string ExplainAbility(string abilityId)
        {
            int fromBase = file.Base.Get(abilityId);
            int bump = file.Bumps.Where(b => b.AbilityId == abilityId).Sum(b => b.Amount);
            string head = $"{AbilityName(abilityId)} {fromBase} → {fromBase + bump}";
            return bump == 0 ? head : $"{head} ({(bump > 0 ? "+" : "")}{bump})";
        }

        void ApplyStandardArray()
        {
            var order = WizardOps.SuggestedAssignment(CurrentClass());
            for (int i = 0; i < WizardOps.StandardArray.Length && i < order.Count; i++)
                SetBase(order[i], WizardOps.StandardArray[i]);
        }

        bool LooksLikeStandardArray()
        {
            var mine = AbilityIds.Select(id => file.Base.Get(id)).OrderBy(v => v).ToList();
            var array = WizardOps.StandardArray.OrderBy(v => v).ToList();
            return mine.SequenceEqual(array);
        }

        /// <summary>Сдвигает характеристику на соседнее число набора, меняясь с тем, у кого оно сейчас.
        /// Обмен, а не присваивание: стандартный набор — это ровно шесть чисел, и потерять одно из них
        /// значило бы собрать персонажа не по правилам, ничего об этом не сказав.</summary>
        void CycleStandard(string abilityId, int direction)
        {
            int current = file.Base.Get(abilityId);
            int index = Array.IndexOf(WizardOps.StandardArray, current);
            if (index < 0) { ApplyStandardArray(); return; }   // числа не из набора — раскладываем заново

            int next = index + direction;
            if (next < 0 || next >= WizardOps.StandardArray.Length) return;
            int wanted = WizardOps.StandardArray[next];

            foreach (var other in AbilityIds)
            {
                if (other == abilityId || file.Base.Get(other) != wanted) continue;
                SetBase(other, current);
                break;
            }
            SetBase(abilityId, wanted);
        }

        void StepAbility(string abilityId, int delta)
        {
            // Границы — крайние значения листа персонажа, а не бюджета: сколько это стоит очков, здесь
            // не считается (см. BuildPointBuy).
            int value = Mathf.Clamp(file.Base.Get(abilityId) + delta, 3, 20);
            SetBase(abilityId, value);
        }

        /// <summary>Четыре d6, худшая отбрасывается — шесть раз, от большего к меньшему по порядку
        /// SuggestedAssignment. Это способ ВВОДА чисел, а не правило: переставить их потом можно
        /// теми же «-» и «+».</summary>
        void RollAbilities()
        {
            var rolled = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                var dice = new List<int>();
                for (int d = 0; d < 4; d++) dice.Add(UnityEngine.Random.Range(1, 7));
                dice.Sort();
                rolled.Add(dice[1] + dice[2] + dice[3]);
            }
            rolled.Sort();
            rolled.Reverse();

            var order = WizardOps.SuggestedAssignment(CurrentClass());
            for (int i = 0; i < rolled.Count && i < order.Count; i++) SetBase(order[i], rolled[i]);
        }

        /// <summary>У AbilityScores есть Get и Add, но нет Set — присваиваем через разницу, чтобы не
        /// заводить седьмое место со списком из шести имён полей.</summary>
        void SetBase(string abilityId, int value)
        {
            file.Base.Add(abilityId, value - file.Base.Get(abilityId));
        }

        // ── Шаг 6: навыки ────────────────────────────────────────────────────────

        void BuildStepSkills(Transform content)
        {
            var cls = CurrentClass();
            if (cls == null)
            {
                AddLabel(content, "Сначала выберите класс (шаг 3) — именно он решает, из каких навыков "
                                  + "выбирать.", 17, Accent, null);
                return;
            }

            // Остаток НЕ считается здесь: навык от предыстории приходит даром и ячейку класса не
            // занимает, и это правило живёт в WizardOps.RemainingSkillPicks в одном экземпляре.
            int remaining = WizardOps.RemainingSkillPicks(file, rules);
            int picked = Mathf.Max(0, cls.SkillPickCount - remaining);
            AddCaption(content, $"Навыки класса: выбрано {picked} из {cls.SkillPickCount}");

            foreach (var option in WizardOps.AvailableSkills(file, rules))
            {
                var o = option;
                if (o.AlreadyOwned)
                {
                    var owned = AddRow(content, RowHeight);
                    AddFixedLabel(owned, $"[×] {o.Name}", 320f, 17, Faint);
                    AddLabel(owned, $"уже есть: {o.OwnedFrom}", 15, Faint, null)
                        .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                    continue;
                }

                bool chosen = file.SkillIds.Contains(o.Id);
                var row = AddRow(content, RowHeight);
                var btn = AddButton(row, $"{(chosen ? "[×]" : "[ ]")} {o.Name}", 320f, () =>
                {
                    if (chosen)
                    {
                        file.SkillIds.Remove(o.Id);
                        // Компетентность живёт только на выбранных навыках — снятый навык уносит её с
                        // собой, иначе на листе осталась бы компетентность в том, чем персонаж не владеет
                        // (ApplyClassChange соблюдает то же правило при смене класса).
                        file.ExpertiseIds.Remove(o.Id);
                    }
                    else file.SkillIds.Add(o.Id);
                    Rebuild();
                }, chosen || remaining > 0);
                if (chosen) MarkSelected(btn, true);

                AddLabel(row, SkillAbilityHint(o.Id), 15, Muted, null)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            BuildExpertise(content, cls);
        }

        /// <summary>Второй список — только если у класса компетентность есть И её уровень достигнут.
        /// Условие ровно то же, что у SheetMath (SheetMathSkills.cs:17): иначе мастер предлагал бы
        /// выбор, который лист не показывает.</summary>
        void BuildExpertise(Transform content, ClassDef cls)
        {
            if (cls.ExpertiseLevel <= 0 || file.Level < cls.ExpertiseLevel) return;

            AddSpacer(content, 12f);
            AddCaption(content, $"Компетентность: выбрано {file.ExpertiseIds.Count} "
                                + $"из {cls.ExpertisePickCount}");
            AddLabel(content, "Компетентность удваивает бонус мастерства. Выбирается из уже взятых "
                              + "навыков класса.", 15, Muted, null);

            // Список берётся из file.SkillIds, а не из всех навыков персонажа: ApplyClassChange снимает
            // компетентность со всего, чего нет в file.SkillIds, и компетентность на навыке предыстории
            // молча исчезла бы при первой же смене класса.
            if (file.SkillIds.Count == 0)
            {
                AddLabel(content, "Сначала выберите навыки выше.", 16, Faint, null);
                return;
            }

            foreach (var skillId in file.SkillIds)
            {
                string id = skillId;
                string name = rules.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id;
                bool chosen = file.ExpertiseIds.Contains(id);
                var row = AddRow(content, RowHeight);
                var btn = AddButton(row, $"{(chosen ? "[×]" : "[ ]")} {name}", 320f, () =>
                {
                    if (chosen) file.ExpertiseIds.Remove(id);
                    else file.ExpertiseIds.Add(id);
                    Rebuild();
                }, chosen || file.ExpertiseIds.Count < cls.ExpertisePickCount);
                if (chosen) MarkSelected(btn, true);
            }
        }

        string SkillAbilityHint(string skillId)
        {
            var def = rules.Skills.FirstOrDefault(s => s.Id == skillId);
            return def == null ? "" : AbilityName(def.AbilityId).ToLowerInvariant();
        }

        // ── Шаг 7: снаряжение ────────────────────────────────────────────────────

        void BuildStepEquipment(Transform content)
        {
            var cls = CurrentClass();
            var bg = CurrentBackground();
            if (cls == null && bg == null)
            {
                AddLabel(content, "Снаряжение приходит от класса и предыстории — выберите их (шаги 3 и 4).",
                    17, Accent, null);
                return;
            }

            // Порядок и источники. Один и тот же предмет бывает и там и там (Плут-Солдат приходит с
            // коротким луком дважды) — в файле он должен лежать один раз, поэтому список складывается
            // с проверкой на повтор, а не конкатенацией.
            var offered = new List<string>();
            var sources = new Dictionary<string, string>();
            if (cls != null) foreach (var id in cls.StartingEquipment) Offer(offered, sources, id, "класс");
            if (bg != null) foreach (var id in bg.Equipment) Offer(offered, sources, id, "предыстория");

            SyncEquipment(offered);

            AddCaption(content, "С чем ты выходишь в первый поход");
            AddLabel(content, "Галочки сняты — предмета у персонажа нет. Доспех влияет на класс "
                              + "доспеха на листе.", 15, Muted, null);

            foreach (var itemId in offered)
            {
                string id = itemId;
                string name = rules.Items.FirstOrDefault(i => i.Id == id)?.Name ?? id;
                bool chosen = file.Equipment.Contains(id);
                var row = AddRow(content, RowHeight);
                var btn = AddButton(row, $"{(chosen ? "[×]" : "[ ]")} {name}", 380f, () =>
                {
                    if (chosen) file.Equipment.Remove(id);
                    else file.Equipment.Add(id);
                    Rebuild();
                });
                if (chosen) MarkSelected(btn, true);
                AddLabel(row, sources[id], 15, Faint, null)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        static void Offer(List<string> offered, Dictionary<string, string> sources, string id, string source)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (sources.ContainsKey(id))
            {
                if (sources[id] != source) sources[id] = sources[id] + " и " + source;
                return;
            }
            offered.Add(id);
            sources[id] = source;
        }

        /// <summary>Первый приход на шаг ставит все галочки: стартовый набор игрок обычно берёт целиком,
        /// а пустой список снаряжения дал бы на листе класс доспеха 10 без объяснения. Просто прийти на
        /// шаг второй раз ничего не меняет — снятые галочки должны оставаться снятыми.
        ///
        /// НО СМЕНА КЛАССА ИЛИ ПРЕДЫСТОРИИ ПЕРЕСОБИРАЕТ НАБОР ЦЕЛИКОМ, и без этого был бы тихо испорчен
        /// файл. ApplyClassChange снаряжения не трогает намеренно («класс — это не персонаж целиком»),
        /// поэтому после Плут→Воин в file.Equipment остаются вещи Плута, а строк для них на шаге больше
        /// нет: невидимые, неснимаемые — и лист считает по ним класс доспеха из кожаной брони, которой у
        /// персонажа как бы нет. Поэтому запомнена пара «класс|предыстория», а не факт «раскладывали».
        ///
        /// Правило простое и оттого предсказуемое: сменил класс — получил его набор, потерял прежний.
        /// Цена — снятая вручную галочка возвращается, если после неё сменить класс; это видно и
        /// поправимо, в отличие от невидимой вещи в файле.</summary>
        void SyncEquipment(List<string> offered)
        {
            string signature = (file.ClassId ?? "") + "|" + (file.BackgroundId ?? "");
            if (equipmentSeededFor == signature) return;

            bool firstVisit = equipmentSeededFor == null;
            equipmentSeededFor = signature;
            // Персонаж пришёл в мастер со своим снаряжением (открыли сохранённый лист) — раскладывать
            // заново нечего, это уже чей-то осознанный выбор.
            if (firstVisit && file.Equipment.Count > 0) return;

            if (!firstVisit) file.Equipment.RemoveAll(id => !offered.Contains(id));
            foreach (var id in offered)
                if (!file.Equipment.Contains(id)) file.Equipment.Add(id);
        }

        // ── Шаг 8: готово ────────────────────────────────────────────────────────

        void BuildStepDone(Transform content)
        {
            // «Чего не хватает» считает SheetMath — тот же список, что покажет лист. Своей проверки
            // здесь нет намеренно: два списка разошлись бы, и игрок не понял бы, какому верить.
            var missing = SheetMath.Compute(file, rules).Missing;

            AddCaption(content, missing.Count == 0 ? "Всё на месте" : "Чего не хватает");
            if (missing.Count == 0)
                AddLabel(content, "Лист собран полностью.", 17, Muted, null);
            else
            {
                foreach (var line in missing) AddLabel(content, "• " + line, 17, null, null);
                AddLabel(content, "Это не запрет: незаконченного персонажа можно сохранить и доделать "
                                  + "потом — вернувшись на любой шаг мастера.", 15, Muted, null);
            }

            AddSpacer(content, 14f);
            var row = AddRow(content, 52f);
            AddButton(row, "Открыть лист", 260f, () => { if (onFinished != null) onFinished(); });
            // Сохранение идёт ЧЕРЕЗ корень, а не через SheetFileService напрямую: у CurrentPath
            // приватный сеттер, и прямой вызов SaveAs записал бы файл, оставив корень с прежним
            // «куда сохранять» — ровно та рассинхронизация, от которой предостерегает SaveCurrentAs.
            AddButton(row, "Сохранить…", 260f, SaveThroughRoot,
                PlayerPrepScreenController.Instance != null);

            var root = PlayerPrepScreenController.Instance;
            if (root != null && !string.IsNullOrEmpty(root.CurrentPath))
                AddLabel(content, "Файл: " + root.CurrentPath, 14, Faint, null);
        }

        void SaveThroughRoot()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            root.SaveCurrent();
            Rebuild();   // показать путь, если он только что появился
        }

        // ── Мелкие построители ───────────────────────────────────────────────────

        ClassDef CurrentClass() =>
            rules == null ? null : rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);

        BackgroundDef CurrentBackground() =>
            rules == null ? null : rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId);

        static string AbilityName(string abilityId)
        {
            int i = Array.IndexOf(AbilityIds, abilityId);
            return i < 0 ? abilityId : AbilityNames[i];
        }

        static string JoinAbilityNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "—";
            return string.Join(", ", ids.Select(AbilityName).ToArray());
        }

        string JoinSkillNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "—";
            return string.Join(", ", ids
                .Select(id => rules.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id).ToArray());
        }

        string JoinItemNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "—";
            return string.Join(", ", ids
                .Select(id => rules.Items.FirstOrDefault(i => i.Id == id)?.Name ?? id).ToArray());
        }

        /// <summary>Две колонки: слева выбор, справа объяснение выбранного. Высота строки диктуется
        /// текстом справа, поэтому обе колонки — обычные вертикальные раскладки без заданной высоты.</summary>
        (Transform list, Transform details) AddTwoColumns(Transform content)
        {
            var row = NewRect(content, "Columns", typeof(HorizontalLayoutGroup));
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 24f;
            hlg.childAlignment = TextAnchor.UpperLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false;

            var list = Column(row, "List");
            list.gameObject.AddComponent<LayoutElement>().preferredWidth = ListColumnWidth;
            var details = Column(row, "Details");
            details.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return (list, details);
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

        static Transform AddRow(Transform parent, float height)
        {
            var rt = NewRect(parent, "Row", typeof(HorizontalLayoutGroup));
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = rt.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            return rt;
        }

        void AddChoiceRow(Transform parent, string label, bool selected, Action onClick)
        {
            var btn = UiKit.Button(parent, (selected ? "[×] " : "[ ] ") + label, onClick);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.flexibleWidth = 1f;
            if (selected) MarkSelected(btn, true);
        }

        static Button AddButton(Transform parent, string label, float width, Action onClick,
            bool enabled = true)
        {
            var btn = UiKit.Button(parent, label, onClick, enabled);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = RowHeight;
            return btn;
        }

        /// <summary>Подсветка выбранного. Отключать выбранную кнопку нельзя: серое читается как
        /// «недоступно», а выбранное доступно — по нему просто нечего нажимать второй раз.</summary>
        static void MarkSelected(Button btn, bool selected)
        {
            if (!selected) return;
            var image = btn.GetComponent<Image>();
            if (image != null) image.color = SelectedFill;
            var text = btn.GetComponentInChildren<Text>();
            if (text != null) text.color = Accent;
        }

        static Text AddLabel(Transform parent, string text, int size, Color? color, float? preferredHeight)
        {
            var label = UiKit.Label(parent, text, size);
            if (color.HasValue) label.color = color.Value;
            if (preferredHeight.HasValue)
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight.Value;
            return label;
        }

        static Text AddFixedLabel(Transform parent, string text, float width, int size, Color? color)
        {
            var label = AddLabel(parent, text, size, color, null);
            var le = label.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            return label;
        }

        static void AddCaption(Transform parent, string text)
        {
            var label = UiKit.Label(parent, text, 20);
            label.color = Accent;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        }

        static void AddSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        /// <summary>Поле ввода. Собирается вручную, тем же порядком, что и в QuickOpenPopup.BuildInputRow:
        /// textComponent и placeholder назначаются ДО присваивания text, иначе InputField пишет в пустоту.</summary>
        void AddInput(Transform parent, string value, string placeholderText, bool multiline, float height,
            Action<string> onChanged)
        {
            var rt = NewRect(parent, "Input", typeof(Image), typeof(InputField));
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var image = rt.GetComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.16f);

            var input = rt.GetComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

            var text = UiKit.Label(rt, "", 17, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            text.supportRichText = false;
            // UiKit.Label переносит по словам — многострочному полю это нужно, однострочному вредно:
            // длинное имя уехало бы на вторую строку внутри поля высотой в одну.
            text.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = multiline ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            StretchInside(text.rectTransform);
            input.textComponent = text;

            var placeholder = UiKit.Label(rt, placeholderText, 17,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
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
