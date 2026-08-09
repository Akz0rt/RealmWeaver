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

        // Своей таблицы характеристик здесь НЕТ. Идентификаторы и русские названия приходят из
        // SheetMath.AbilityOrder и SheetMath.AbilityName: копия в слое рисования уже была, и ничто не
        // мешало ей разойтись с листом.

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

        /// <summary>0 — стандартный набор, 1 — покупка очков, 2 — броски. Умолчания у поля НЕТ:
        /// его ставит DetectAbilityMode при входе, потому что мастер открывают и на уже собранном
        /// листе (задача 12), а показать «стандартный набор» над чужими числами значило бы соврать —
        /// и первым же нажатием стрелки эти числа стереть.</summary>
        int abilityMode;

        /// <summary>0 — +2/+1, 1 — +1/+1/+1. Прибавки предыстории, шаг 5.</summary>
        int bumpLayout;
        string bumpPlus2, bumpPlus1;

        /// <summary>Были ли уже на шаге снаряжения. Простого «уже были» ХВАТАЕТ, потому что
        /// пересборкой набора при смене класса или предыстории занят WizardOps.ResetEquipment в момент
        /// самой смены; здесь остаётся только первый показ. Пока пересборка жила в показе, поля-флага
        /// было мало и приходилось помнить пару «класс|предыстория».</summary>
        bool equipmentSeeded;

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
            view.abilityMode = view.DetectAbilityMode();
            view.Rebuild();
            return view;
        }

        /// <summary>Каким способом собраны числа, которые УЖЕ лежат в файле.
        ///
        /// Без этого поле оставалось нулём — «стандартный набор» — на любом листе, а первое же
        /// нажатие стрелки уходило в CycleStandard, не находило числа в наборе и раскладывало набор
        /// заново поверх выброшенных 17 и 16. Отмены на листе нет: любопытство стоило персонажа.
        ///
        /// Покупку очков от бросков отличить НЕЛЬЗЯ — оба способа дают просто шесть чисел, и
        /// стандартный набор, кстати, стоит ровно 27 очков, то есть законен и для покупки. Поэтому
        /// всё, что не стандартный набор, объявляется бросками: это единственный из трёх режимов,
        /// который ничего не заменяет при входе.</summary>
        int DetectAbilityMode()
        {
            if (file == null) return 0;
            if (AbilitiesAreBlank()) return 0;          // нетронутый лист — обычное начало
            return LooksLikeStandardArray() ? 0 : 2;
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
            // нельзя. BackToList обнуляет Current и CurrentPath, поэтому уход идёт через общий вопрос
            // о несохранённом (задача 11б): раньше он был безусловным, потому что изменений никто не
            // считал, а теперь их считает снимок на корне сцены.
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

        /// <summary>Вопрос про несохранённое задаёт КОРЕНЬ, а не мастер: он один знает снимок
        /// сохранённого состояния, и вторая копия этого разговора здесь означала бы два разных ответа
        /// на один и тот же вопрос — из мастера и с листа.
        ///
        /// Проверки «if (this == null)» здесь больше нет и она не нужна: замыкание, живущее через
        /// кадр, теперь держит корень, а не мастер, и проверяет себя само (AskBeforeLeaving).</summary>
        void AskBackToList()
        {
            var root = PlayerPrepScreenController.Instance;
            if (root == null) return;
            root.AskBeforeLeaving("Вернуться к списку листов?", root.BackToList);
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
            // Выдач бывает две (Плут на 1 и 6, Бард и Следопыт на 2 и 9) — перечисляем ВСЕ, иначе
            // строка обещала бы половину того, что класс даёт. Слово согласовано по числу:
            // «1 навык», «2 навыка» (прежде здесь стояло «1 навыка» при любом числе).
            if (chosen.HasExpertise())
                AddLabel(details, "Компетентность: " + string.Join(", ", chosen.ExpertiseGrants
                             .Where(g => g != null)
                             .Select(g => $"{g.PickCount} {SheetMath.SkillsAfterNumber(g.PickCount)} "
                                        + $"с {g.Level} уровня").ToArray()), 17, null, null);
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
                ApplyClassAndKit(newClassId);
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
                    ApplyClassAndKit(newClassId);
                    Rebuild();
                });
        }

        /// <summary>Смена класса целиком: сам класс и стартовый набор под него.
        ///
        /// ДВА ВЫЗОВА, А НЕ ОДИН, И ОБА ОБЯЗАТЕЛЬНЫ. ApplyClassChange снаряжения не трогает намеренно
        /// («класс — это не персонаж целиком»), поэтому без ResetEquipment в файле остались бы вещи
        /// прежнего класса: строк для них на седьмом шаге больше нет — невидимые, неснимаемые, — а
        /// лист считает по ним класс доспеха, и «Чего не хватает» о снаряжении молчит. Раньше набор
        /// пересобирался только при показе седьмого шага, и достаточно было уйти со смены класса
        /// сразу на «Готово», чтобы кожаный доспех Плута молча уехал в файл Воина.
        ///
        /// Про эту самую потерю предупреждает WizardOps.DescribeClassChange — потому оба вызова и
        /// стоят в одном месте, чтобы обещание и дело не разъехались.</summary>
        void ApplyClassAndKit(string newClassId)
        {
            WizardOps.ApplyClassChange(file, rules, newClassId);
            WizardOps.ResetEquipment(file, rules);
            equipmentSeeded = true;   // набор только что собран — первому показу нечего добавить
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
        /// этого показал бы готовые «3 из 3», которых игрок не выбирал.
        ///
        /// Сам идентификатор ставит WizardOps.ApplyBackgroundChange, а не эта строка кода: вместе с
        /// ним оттуда уходят из РУЧНОГО выбора навыки, которые новая предыстория даёт даром (иначе
        /// они занимали бы ячейки класса, оставаясь при этом серыми надписями без кнопки), и
        /// осиротевшая на них компетентность. Снаряжение пересобирается тут же и по той же причине,
        /// что при смене класса, — см. ApplyClassAndKit.</summary>
        void ChooseBackground(string newBackgroundId)
        {
            if (file.BackgroundId == newBackgroundId) return;
            WizardOps.ApplyBackgroundChange(file, rules, newBackgroundId);
            bumpLayout = 0;
            bumpPlus2 = null;
            bumpPlus1 = null;
            WriteBackgroundBumps();
            WizardOps.ResetEquipment(file, rules);
            equipmentSeeded = true;
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
            var btn = AddButton(parent, label, 260f, () => SwitchAbilityMode(mode));
            if (abilityMode == mode) MarkSelected(btn, true);
        }

        /// <summary>Переключает способ ввода чисел, СПРОСИВ, если переключение эти числа заменит.
        ///
        /// Два режима из трёх разрушительны: стандартный набор кладёт свои шесть значений, покупка
        /// очков начинает с восьмёрок. Отмены на листе нет вовсе, а игрок нажимает такие кнопки из
        /// любопытства — значит вопрос обязателен ровно тогда, когда терять есть что.</summary>
        void SwitchAbilityMode(int mode)
        {
            string warning = ModeReplacementWarning(mode);
            if (warning == null) { ApplyAbilityMode(mode); return; }

            // Кнопка подтверждения в ConfirmDialog подписана словом «Удалить» — общий код части ДМ,
            // трогать его в этой арке нельзя. Поэтому что именно случится, сказано в самом тексте.
            ConfirmDialog.Show(UiKit.Font, "Заменить числа характеристик?", warning,
                confirmed =>
                {
                    // Ответ приходит через кадр — мастера к этому времени может не быть вовсе.
                    if (this == null) return;
                    if (confirmed) ApplyAbilityMode(mode);
                });
        }

        /// <summary>Текст предупреждения или null, если переключение ничего не затрёт: числа уже
        /// подходят режиму либо их нет вовсе (нетронутый лист терять нечего).</summary>
        string ModeReplacementWarning(int mode)
        {
            if (AbilitiesAreBlank()) return null;
            if (mode == 0 && !LooksLikeStandardArray())
                return "Нынешние шесть чисел заменятся стандартным набором 15, 14, 13, 12, 10, 8. "
                     + "Вернуть их будет нечем.";
            if (mode == 1 && !WizardOps.IsPointBuyLegal(file.Base))
                return "Покупка очков начинается с восьмёрок, а нынешние числа за 27 очков не купить. "
                     + "Все шесть станут восьмёрками. Вернуть прежние будет нечем.";
            return null;
        }

        void ApplyAbilityMode(int mode)
        {
            abilityMode = mode;
            // Стандартный набор раскладывается сразу, если сейчас лежит не он: иначе «стандартный
            // набор» показывал бы броски. Покупка очков — только если нынешние числа ей незаконны:
            // законные (в том числе её же собственные, если из режима выходили и вернулись) трогать
            // не за что, а вот 18 из бросков оставлять нельзя, за них нечем заплатить.
            if (mode == 0 && !LooksLikeStandardArray()) ApplyStandardArray();
            if (mode == 1 && !WizardOps.IsPointBuyLegal(file.Base)) WizardOps.ApplyPointBuyStart(file.Base);
            Rebuild();
        }

        bool AbilitiesAreBlank() => SheetMath.AbilityOrder().All(id => file.Base.Get(id) == 0);

        void BuildStandardArray(Transform content)
        {
            // Нетронутые характеристики (все шесть нулевые) раскладываются сразу: шесть нулей под
            // заголовком «стандартный набор» читаются как поломка, а затирать здесь нечего.
            if (AbilitiesAreBlank()) ApplyStandardArray();

            AddLabel(content, "Шесть заранее известных чисел — 15, 14, 13, 12, 10, 8 — раскладываются по "
                              + "характеристикам. Стрелки меняют числа местами: набор всегда остаётся целым.",
                15, Muted, 44f);
            AddButton(AddRow(content, 44f), "Предложить под класс", 300f, () =>
            {
                ApplyStandardArray();
                Rebuild();
            });

            foreach (var abilityId in SheetMath.AbilityOrder())
            {
                string id = abilityId;
                var row = AddRow(content, RowHeight);
                AddFixedLabel(row, SheetMath.AbilityName(id), 220f, 17, null);
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

        /// <summary>Покупка очков. Таблицу стоимости и бюджет считает WizardOps — правила лежат в
        /// чистом слое под самопроверками, как и всё остальное (см. шапку файла); здесь только
        /// счётчик и погашенные кнопки.
        ///
        /// Решение ДМ дословно: приложение существует ровно затем, чтобы не требовать от игрока
        /// хороших знаний правил, — режим, молча пускающий невозможного персонажа, замыслу арки
        /// противоречит. Поэтому «осталось N» видно всегда, а «+», на который не хватает, погашен
        /// заранее, а не ругается задним числом.</summary>
        void BuildPointBuy(Transform content)
        {
            int spent = WizardOps.PointBuySpent(file.Base);
            AddCaption(content, spent < 0
                ? "Нынешние числа покупкой не собираются"
                : $"Осталось {WizardOps.PointBuyBudget - spent} очков из {WizardOps.PointBuyBudget}");
            AddLabel(content, $"Каждая характеристика начинается с {WizardOps.PointBuyFloor} и поднимается "
                              + $"не выше {WizardOps.PointBuyCeiling}. Стоимость по правилам: 8 — 0 очков, "
                              + "9 — 1, 10 — 2, 11 — 3, 12 — 4, 13 — 5, 14 — 7, 15 — 9. Прибавки от "
                              + "предыстории покупаются не очками и приходят сверху.", 15, Muted, 44f);
            BuildSteppers(content, pointBuy: true);
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
            BuildSteppers(content, pointBuy: false);
        }

        /// <summary>Ряды с «−» и «+». Общие для покупки очков и бросков: и там и там числа
        /// произвольные, в отличие от стандартного набора, где они переставляются.
        ///
        /// pointBuy меняет ровно две вещи — границы шага и то, какие кнопки погашены. Кто из них
        /// доступен, решает WizardOps: «хватает ли очков» — это правило, а не оформление.</summary>
        void BuildSteppers(Transform content, bool pointBuy)
        {
            foreach (var abilityId in SheetMath.AbilityOrder())
            {
                string id = abilityId;
                var row = AddRow(content, RowHeight);
                AddFixedLabel(row, SheetMath.AbilityName(id), 220f, 17, null);
                AddFixedLabel(row, file.Base.Get(id).ToString(), 60f, 20, Accent);
                bool canLower = !pointBuy || WizardOps.CanLowerByPointBuy(file.Base, id);
                bool canRaise = !pointBuy || WizardOps.CanRaiseByPointBuy(file.Base, id);
                AddButton(row, "-", 54f, () => { StepAbility(id, -1, pointBuy); Rebuild(); }, canLower);
                AddButton(row, "+", 54f, () => { StepAbility(id, +1, pointBuy); Rebuild(); }, canRaise);
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
                    var btn = AddButton(plus2, SheetMath.AbilityName(id), 230f, () =>
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
                    var btn = AddButton(plus1, SheetMath.AbilityName(id), 230f, () =>
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

        /// <summary>«Ловкость 15 → 17 (+2)». ЗОВЁТ ЧИСТЫЙ СЛОЙ, а не считает сам: правило переехало в
        /// SheetMath.ExplainAbility под самопроверки, потому что здесь оно было второй копией — лист
        /// объяснял то же самое число своей строкой, и разойтись им ничто не мешало.</summary>
        string ExplainAbility(string abilityId) => SheetMath.ExplainAbility(file, abilityId);

        void ApplyStandardArray()
        {
            var order = WizardOps.SuggestedAssignment(CurrentClass());
            for (int i = 0; i < WizardOps.StandardArray.Length && i < order.Count; i++)
                SetBase(order[i], WizardOps.StandardArray[i]);
        }

        bool LooksLikeStandardArray()
        {
            var mine = SheetMath.AbilityOrder().Select(id => file.Base.Get(id)).OrderBy(v => v).ToList();
            var array = WizardOps.StandardArray.OrderBy(v => v).ToList();
            return mine.SequenceEqual(array);
        }

        /// <summary>Сдвигает характеристику на соседнее число набора, меняясь с тем, у кого оно сейчас.
        /// Обмен, а не присваивание: стандартный набор — это ровно шесть чисел, и потерять одно из них
        /// значило бы собрать персонажа не по правилам, ничего об этом не сказав.
        ///
        /// Число НЕ ИЗ НАБОРА — повод не делать ничего. Раньше здесь стояло «раскладываем заново», и
        /// это было тихое разрушение: мастер, открытый на уже собранном листе, показывал стрелки над
        /// выброшенными 17 и 16, и первое же нажатие из любопытства заменяло все шесть на
        /// 15/14/13/12/10/8 без единого вопроса и без отмены. В сам режим на чужих числах теперь не
        /// попасть (DetectAbilityMode и SwitchAbilityMode), а этот возврат — вторая застёжка: она
        /// стоит дешевле, чем ещё раз доказывать, что первая нигде не расстёгивается.</summary>
        void CycleStandard(string abilityId, int direction)
        {
            int current = file.Base.Get(abilityId);
            int index = Array.IndexOf(WizardOps.StandardArray, current);
            if (index < 0) return;

            int next = index + direction;
            if (next < 0 || next >= WizardOps.StandardArray.Length) return;
            int wanted = WizardOps.StandardArray[next];

            foreach (var other in SheetMath.AbilityOrder())
            {
                if (other == abilityId || file.Base.Get(other) != wanted) continue;
                SetBase(other, current);
                break;
            }
            SetBase(abilityId, wanted);
        }

        /// <summary>В покупке очков границы — её собственные, 8…15: значения вне их покупкой
        /// НЕДОСТИЖИМЫ, и пускать туда шаг значило бы разрешить персонажа, за которого не заплатить.
        /// В бросках границы — крайние значения листа: там числа приходят с костей, и переставить
        /// выпавшую восьмёрку в семёрку игрок вправе.</summary>
        void StepAbility(string abilityId, int delta, bool pointBuy)
        {
            int lo = pointBuy ? WizardOps.PointBuyFloor : 3;
            int hi = pointBuy ? WizardOps.PointBuyCeiling : 20;
            int value = Mathf.Clamp(file.Base.Get(abilityId) + delta, lo, hi);
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

        /// <summary>Второй список — только если класс УЖЕ открыл персонажу хоть один навык
        /// компетентности. Число открытых считает ClassDef.ExpertisePicksAt — та же функция, по
        /// которой считает лист: иначе мастер предлагал бы выбор, который лист не показывает, либо
        /// (что и было) гасил кнопки на второй выдаче, которой не знал.</summary>
        void BuildExpertise(Transform content, ClassDef cls)
        {
            int allowed = cls.ExpertisePicksAt(file.Level);
            if (allowed <= 0) return;

            AddSpacer(content, 12f);
            AddCaption(content, $"Компетентность: выбрано {file.ExpertiseIds.Count} из {allowed}");
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

            // Класс вправе сузить список (ClassDef.ExpertiseChoices): у Волшебника компетентность
            // берётся из шести названных навыков, а Проницательность в их число не входит, хотя
            // владеть ею класс даёт. Решает справочник, а не условие «если это Волшебник».
            var eligible = file.SkillIds.Where(cls.AllowsExpertiseIn).ToList();
            if (cls.ExpertiseChoices != null && cls.ExpertiseChoices.Count > 0)
                AddLabel(content, "Этот класс даёт компетентность только в: "
                                  + string.Join(", ", cls.ExpertiseChoices
                                        .Select(id => rules.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id)
                                        .ToArray()) + ".", 15, Muted, null);
            if (eligible.Count == 0)
            {
                AddLabel(content, "Ни один из этих навыков ещё не взят — выберите такой выше.",
                         16, Faint, null);
                return;
            }

            foreach (var skillId in eligible)
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
                }, chosen || file.ExpertiseIds.Count < allowed);
                if (chosen) MarkSelected(btn, true);
            }
        }

        string SkillAbilityHint(string skillId)
        {
            var def = rules.Skills.FirstOrDefault(s => s.Id == skillId);
            return def == null ? "" : SheetMath.AbilityName(def.AbilityId).ToLowerInvariant();
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

            // Что предлагать и откуда оно пришло, считает WizardOps: по этому же правилу теперь
            // ПРАВЯТ файл при смене класса и предыстории, а показ и правка обязаны считать одним
            // куском кода — разойдись они, в файле оказались бы вещи, которых на экране нет.
            var offered = WizardOps.StartingEquipment(cls, bg, rules);
            SeedEquipmentOnFirstVisit();

            AddCaption(content, "С чем ты выходишь в первый поход");
            AddLabel(content, "Галочки сняты — предмета у персонажа нет. Доспех влияет на класс "
                              + "доспеха на листе.", 15, Muted, null);

            foreach (var option in offered)
            {
                var o = option;
                bool chosen = file.Equipment.Contains(o.Id);
                var row = AddRow(content, RowHeight);
                var btn = AddButton(row, $"{(chosen ? "[×]" : "[ ]")} {o.Name}", 380f, () =>
                {
                    if (chosen) file.Equipment.Remove(o.Id);
                    else file.Equipment.Add(o.Id);
                    Rebuild();
                });
                if (chosen) MarkSelected(btn, true);
                AddLabel(row, o.Source, 15, Faint, null)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        /// <summary>Первый приход на шаг ставит все галочки: стартовый набор игрок обычно берёт целиком,
        /// а пустой список снаряжения дал бы на листе класс доспеха 10 без объяснения. Просто прийти на
        /// шаг второй раз ничего не меняет — снятые галочки должны оставаться снятыми.
        ///
        /// Персонаж, пришедший в мастер со своим снаряжением (открыли сохранённый лист), не трогается
        /// вовсе: это уже чей-то осознанный выбор.
        ///
        /// ПЕРЕСБОРКОЙ НАБОРА ПРИ СМЕНЕ КЛАССА ИЛИ ПРЕДЫСТОРИИ ЭТОТ МЕТОД БОЛЬШЕ НЕ ЗАНИМАЕТСЯ — ею
        /// занят WizardOps.ResetEquipment в момент самой смены (ApplyClassAndKit, ChooseBackground).
        /// Пока пересборка жила здесь, до неё можно было не дойти: сменил класс и ушёл сразу на
        /// «Готово» — и вещи прежнего класса молча уехали в файл. Поэтому здесь теперь простое «уже
        /// были», а не пара «класс|предыстория».</summary>
        void SeedEquipmentOnFirstVisit()
        {
            if (equipmentSeeded) return;
            equipmentSeeded = true;
            if (file.Equipment.Count > 0) return;
            WizardOps.ResetEquipment(file, rules);
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

        /// <summary>«Сохранить…» ОТВЕЧАЕТ ВСЛУХ. Первое в жизни сохранение спрашивает имя файла, и
        /// диалог сам по себе служит ответом; второе и все следующие писали молча в уже известный
        /// путь и перерисовывали ту же строку — человек нажимал и не понимал, случилось ли хоть
        /// что-нибудь, а проверить ему нечем.
        ///
        /// Сообщение показывается ТОЛЬКО при удавшейся записи. Отменённый диалог имени — не событие,
        /// а про упавшую запись уже рассказал сам SaveCurrent, и наше сообщение сменило бы его собой:
        /// ConfirmDialog держит на экране ровно один диалог.</summary>
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

        // ── Мелкие построители ───────────────────────────────────────────────────

        ClassDef CurrentClass() =>
            rules == null ? null : rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);

        BackgroundDef CurrentBackground() =>
            rules == null ? null : rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId);

        static string JoinAbilityNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "—";
            return string.Join(", ", ids.Select(SheetMath.AbilityName).ToArray());
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
        /// текстом справа, поэтому обе колонки — обычные вертикальные раскладки без заданной высоты.
        ///
        /// ШИРИНЫ ЗАДАНЫ ОБЕИМ КОЛОНКАМ И ВСЕ ТРИ — И ЭТО НЕ ПЕРЕСТРАХОВКА. Раньше у левой стоял один
        /// preferredWidth, а у правой — один flexibleWidth, и левая колонка на шагах «Вид» и
        /// «Предыстория» схлопывалась примерно до 90 точек: список видов превращался в столбик обрезков.
        ///
        /// Причина в том, как uGUI считает предпочтительную ширину ТЕКСТА: Text.preferredWidth меряет
        /// строку БЕЗ ПЕРЕНОСА, в одну линию. Текст особенности Голиафа — 954 знака, у Гнома 877, у
        /// Эльфа 850, у черты «Посвящённый в магию» 782; кеглем 15 это тысячи точек. Своего
        /// LayoutElement.preferredWidth у правой колонки не было, поэтому за неё отвечала её же
        /// VerticalLayoutGroup, а та берёт максимум по детям — и просила около шести тысяч. Сумма
        /// предпочтительных ширин выходила много больше доступных 1860, а в этом случае
        /// HorizontalLayoutGroup раздаёт не предпочтительное, а Lerp(min, preferred, t) с общим
        /// t = (доступно − сумма min) / (сумма preferred − сумма min) ≈ 0.3. У левой колонки min не был
        /// задан вовсе (−1 → 0), вот она и получала 0.3 × 300 ≈ 90.
        ///
        /// Теперь суммы конечны и от длины текста не зависят вовсе: min и preferred левой — 300,
        /// preferred правой — 0. Сумма предпочтительных 300 + 0 + 24 (промежуток) = 324 при доступных
        /// 1860, значит излишек 1536 положителен, t = 1, и левая получает ровно свои 300, а весь
        /// излишек уходит правой по гибкой ширине — 1536 точек, в которые её текст ПЕРЕНОСИТСЯ.
        ///
        /// flexibleWidth = 0 левой колонке обязателен. Незаданную гибкость LayoutElement не сообщает
        /// вовсе (−1 «не задано» пропускается), и вместо неё уGUI спросил бы VerticalLayoutGroup, а та
        /// при childForceExpandWidth = true отвечает 1 — излишек разделился бы пополам, и список
        /// разъехался бы на 1068 точек.</summary>
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
            var listLe = list.gameObject.AddComponent<LayoutElement>();
            listLe.minWidth = ListColumnWidth;        // ниже не сжимается ни при какой длине текста
            listLe.preferredWidth = ListColumnWidth;
            listLe.flexibleWidth = 0f;                // излишек — весь правой, см. шапку метода

            var details = Column(row, "Details");
            var detailsLe = details.gameObject.AddComponent<LayoutElement>();
            // Ноль, а не «оставшаяся ширина» числом: оставшуюся не из чего посчитать в тот момент,
            // когда строится раскладка, а «0 + вся гибкость» и есть оставшаяся, посчитанная самим
            // uGUI. Важно только, что число КОНЕЧНО и не приходит из длины текста.
            detailsLe.preferredWidth = 0f;
            detailsLe.flexibleWidth = 1f;
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
