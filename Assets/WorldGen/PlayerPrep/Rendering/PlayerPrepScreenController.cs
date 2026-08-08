using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Корень сцены игрока. Держит три состояния — список листов, мастер, лист — и владеет
    /// «текущим» персонажем: кто его открыл и куда его сохранять, знает ровно один объект, а мастер и
    /// лист (задачи 10–12) только пользуются.
    ///
    /// Сцена игрока НЕ знает про .dndproj: ни одного using WorldGen.Persistence здесь нет и появиться
    /// не должно. Дверь между подготовкой игрока и подготовкой мастера односторонняя.
    ///
    /// Интерфейс строится кодом в Awake, как и на главном экране, поэтому PlayerPrep.unity содержит
    /// ровно один объект с этим компонентом.</summary>
    public class PlayerPrepScreenController : MonoBehaviour
    {
        /// <summary>Единственный на сцену. Задачи 10–12 достают через него Current/CurrentPath и
        /// переключают состояния.</summary>
        public static PlayerPrepScreenController Instance { get; private set; }

        /// <summary>Лист, с которым игрок сейчас работает. null на экране списка.</summary>
        public CharacterFile Current { get; private set; }

        /// <summary>Файл, из которого лист открыт. null, если он ещё ни разу не сохранён —
        /// SaveCurrent в этом случае спрашивает имя.</summary>
        public string CurrentPath { get; private set; }

        const float HeaderHeight = 56f;
        // Строка авторства — 480 знаков, кеглем 13 на ширине 1920 это две-три строки. Высота полосы
        // взята с запасом и КОНСТАНТОЙ, а не из Text.preferredHeight: в Awake раскладки ещё не было,
        // и preferredHeight там врёт (та же ловушка, что описана в задаче 11 про ContentSizeFitter).
        const float FooterHeight = 132f;
        const int AttributionFontSize = 13;

        static readonly Color Muted = new Color(0.55f, 0.53f, 0.50f);
        static readonly Color HairLine = new Color(0.20f, 0.20f, 0.22f);

        RectTransform contentRt;
        RulesData rules;        // null = справочник не загрузился, работать не с чем
        string rulesError;

        void Awake()
        {
            Instance = this;

            // Очищает непрозрачный модальный слой, который мог остаться после перезагрузки домена в
            // режиме Play — см. ConfirmDialog.DismissStranded. ProjectMenuBar делает это в своём Awake
            // ровно по той же причине, но его в сцене игрока нет: здесь всегда присутствующая
            // «обвязка» приложения — этот компонент, значит чинить некому, кроме него.
            ConfirmDialog.DismissStranded();
            DemolishForRebuild();

            LoadRules();
            BuildChrome();
            BackToList();
        }

        /// <summary>Сносит то, что этот же Awake построил в ПРОШЛЫЙ раз, чтобы BuildChrome строил с чистого
        /// листа. Пересборка скриптов в режиме Play (Unity пересобирает, когда окну возвращают фокус — тот
        /// самый alt-tab) заново зовёт Awake на уже существующем компоненте, а BuildChrome — это цепочка
        /// new GameObject(...). Без сноса в сцене оказываются ДВА холста ScreenSpaceOverlay с одинаковым
        /// sortingOrder 0, и сверху может лечь СТАРЫЙ, с непрозрачным фоном и стёртыми перезагрузкой
        /// слушателями: экран выглядит правильно и не отвечает ни на одно нажатие. Дотянуться до старого
        /// нечем — contentRt к тому времени указывает уже на новый Content.
        ///
        /// DestroyImmediate, а не Destroy, и это не вкусовщина: Destroy откладывается до конца кадра, так
        /// что старый «Canvas» ещё кадр оставался бы ребёнком, отвечал бы на Transform.Find и читался бы как
        /// не-null через Unity-совместимое ==, пока этот же метод строит второй с тем же именем. В Awake
        /// DestroyImmediate разрешён. Ровно тот же приём и по той же причине — в
        /// WorkspaceBuilder.DemolishForRebuild и ProjectMenuBar.DemolishForRebuild.
        ///
        /// Довод из ClearContent («уничтожать нельзя, состояние переключают из обработчика нажатия») сюда не
        /// относится вовсе: Awake никто не зовёт из onClick.
        ///
        /// На холодном запуске это пустой цикл: в PlayerPrep.unity у объекта с этим компонентом детей нет.
        /// EventSystem под снос не попадает — он создаётся в корне сцены, а не ребёнком; уцелев, он ровно
        /// поэтому и не пересоздаётся проверкой в BuildChrome.</summary>
        void DemolishForRebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Справочник читается ОДИН раз и через try: SheetRulesSource.Rules не возвращает
        /// null, а бросает (файла нет / JSON не разобрался). Падать всей сценой из-за этого нельзя —
        /// игрок должен увидеть, что случилось.</summary>
        void LoadRules()
        {
            try
            {
                rules = SheetRulesSource.Rules;
            }
            catch (System.Exception ex)
            {
                rules = null;
                rulesError = ex.Message;
                Debug.LogError($"[PlayerPrep] справочник правил не загрузился: {ex.Message}");
            }
        }

        // ── Обвязка сцены ────────────────────────────────────────────────────────

        void BuildChrome()
        {
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGO.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.09f, 0.09f, 0.10f);

            BuildHeader(canvasGO.transform);

            contentRt = NewRect(canvasGO.transform, "Content");
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = new Vector2(0f, FooterHeight);
            contentRt.offsetMax = new Vector2(0f, -HeaderHeight);

            BuildFooter(canvasGO.transform);
        }

        void BuildHeader(Transform parent)
        {
            var header = NewRect(parent, "Header", typeof(Image));
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(0f, -HeaderHeight);
            header.offsetMax = Vector2.zero;
            header.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.13f);
            EdgeLine(header, bottom: true);

            var back = UiKit.Button(header, "← Главный экран", () =>
            {
                // Ровно код из задачи 1, шаг 4: выгрузка сцены уничтожает несохранённую работу молча,
                // а отслеживания изменений в листе нет вовсе — поэтому спрашиваем безусловно.
                ConfirmDialog.Show(UiKit.Font, "Выйти на главный экран?",
                    "Несохранённые изменения листа будут потеряны.",
                    confirmed => { if (confirmed) SceneManager.LoadScene("Home"); });
            });
            // Прямоугольник кнопке задаётся ЯВНО. UiKit.Button рождает RectTransform нулевого размера;
            // на главном экране его растягивает VerticalLayoutGroup, а здесь раскладки нет — без этих
            // четырёх строк кнопка есть, но не видна и не нажимается.
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 0.5f);
            backRt.anchorMax = new Vector2(0f, 0.5f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.anchoredPosition = new Vector2(24f, 0f);
            backRt.sizeDelta = new Vector2(240f, 40f);

            var title = UiKit.Label(header, "Подготовка игрока", 22, TextAnchor.MiddleRight);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(1f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.pivot = new Vector2(1f, 0.5f);
            titleRt.anchoredPosition = new Vector2(-24f, 0f);
            titleRt.sizeDelta = new Vector2(520f, 40f);
        }

        /// <summary>Нижняя полоса. Строка Attribution показывается ЦЕЛИКОМ — это требование лицензии
        /// CC-BY 4.0, а не украшение: сокращать её, прятать за кнопкой или выносить в отдельное окно
        /// нельзя.</summary>
        void BuildFooter(Transform parent)
        {
            var footer = NewRect(parent, "Footer", typeof(Image));
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.offsetMin = Vector2.zero;
            footer.offsetMax = new Vector2(0f, FooterHeight);
            footer.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.13f);
            EdgeLine(footer, bottom: false);

            // Колонка — отдельный объект: VerticalLayoutGroup на самой полосе управлял бы и волосяной
            // линией тоже, и она уехала бы в поток текста.
            var column = NewRect(footer, "FooterColumn", typeof(VerticalLayoutGroup));
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(24f, 10f);
            column.offsetMax = new Vector2(-24f, -12f);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            string titleLine = rules != null
                ? $"Справочник: {rules.Title}"
                : "Справочник правил не загрузился.";
            var head = UiKit.Label(column, titleLine, 15);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            string attribution = rules != null ? rules.Attribution : rulesError;
            var body = UiKit.Label(column, string.IsNullOrEmpty(attribution) ? "" : attribution,
                AttributionFontSize);
            body.color = new Color(0.72f, 0.70f, 0.67f);
            var bodyLe = body.gameObject.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = 74f;
            bodyLe.flexibleHeight = 1f;
        }

        // ── Состояния ────────────────────────────────────────────────────────────

        /// <summary>Единственное место, где пара «текущий лист + куда его сохранять» меняется вместе.
        ///
        /// ИНВАРИАНТ: CurrentPath никогда не указывает на файл, из которого пришёл НЕ тот лист, что лежит в
        /// Current. Разъехаться этой паре нельзя: SaveCurrent при непустом CurrentPath пишет молча, без
        /// вопросов, и рассинхронизация означает не ошибку на экране, а тихо переписанного другого
        /// персонажа. Поэтому Current присваивается здесь и только здесь — вместе с путём.</summary>
        void SetCurrent(CharacterFile file, string path)
        {
            Current = file;
            CurrentPath = path;
        }

        /// <summary>Показать мастер. Задачи 10 и 12 зовут это, чтобы отправить игрока доделывать лист.
        ///
        /// path — файл, из которого лист пришёл; НЕ передавать для только что созданного персонажа. Раньше
        /// аргумента не было вовсе, и естественный вызов «создать ещё одного» — OpenWizard(new CharacterFile
        /// { … }) — оставлял CurrentPath от ранее открытого персонажа, после чего первое же «Сохранить»
        /// молча переписывало чужой файл. Умолчание именно null, а не CurrentPath, потому что цена ошибки
        /// несимметрична: забыть путь стоит одного лишнего вопроса «куда сохранить» — это заметно и
        /// поправимо, а унаследовать чужой путь стоит потерянного персонажа и незаметно. Продолжая работу
        /// над УЖЕ сохранённым листом, передавайте OpenWizard(file, CurrentPath).</summary>
        public void OpenWizard(CharacterFile file, string path = null)
        {
            if (file == null) return;
            SetCurrent(file, path);
            ClearContent();
            ShowWizard(file);
        }

        /// <summary>Показать лист. CurrentPath не трогается: открыть лист можно и у ещё не сохранённого
        /// персонажа, только что собранного мастером. Инвариант при этом цел, а не нарушен ради удобства: на
        /// всех вызовах — из OpenFromDisk, из OpenRecent и из возврата мастера (задача 10) — Current уже
        /// равен file, так что этот вызов лист не МЕНЯЕТ, а подтверждает, и разъезжаться паре не с чем.</summary>
        public void OpenSheet(CharacterFile file)
        {
            if (file == null) return;
            SetCurrent(file, CurrentPath);
            ClearContent();
            ShowSheet(file);
        }

        /// <summary>Вернуться к списку. Current и CurrentPath ОБНУЛЯЮТСЯ намеренно: иначе SaveCurrent,
        /// вызванный после возврата, молча переписал бы файл, который игрок считает закрытым. Мастеру и
        /// листу файл передаётся аргументом, так что ни на что оставшееся это не влияет.</summary>
        public void BackToList()
        {
            SetCurrent(null, null);
            ClearContent();
            ShowList();
        }

        /// <summary>Мастер (задача 10). Обратный вызов ведёт на лист того же самого персонажа: OpenSheet
        /// пути не трогает, а Current к этому времени уже равен file, поэтому пара «лист + путь» остаётся
        /// целой.</summary>
        void ShowWizard(CharacterFile file)
        {
            WizardView.Build(contentRt, file, () => OpenSheet(file));
        }

        /// <summary>ЗАГЛУШКА ЗАДАЧИ 9. Задача 11 заменяет тело на
        /// <c>SheetView.Build(contentRt, file);</c> и заводит СВОЁ поле под возвращённый компонент —
        /// задача 12 достаёт через него Refresh(). Поля заранее нет намеренно: типа ещё не существует.</summary>
        void ShowSheet(CharacterFile file)
        {
            Placeholder("Лист персонажа появится в задаче 11.");
        }

        /// <summary>Временное содержимое с выходом. Без кнопки возврата ДМ на контрольной точке
        /// оказался бы заперт в пустом экране.</summary>
        void Placeholder(string message)
        {
            var column = NewRect(contentRt, "Placeholder", typeof(VerticalLayoutGroup));
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(48f, 24f);
            column.offsetMax = new Vector2(-48f, -28f);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var label = UiKit.Label(column, message, 20);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            AddButton(column, "← К списку листов", 260f, BackToList);
        }

        void ShowList()
        {
            var column = NewRect(contentRt, "SheetList", typeof(VerticalLayoutGroup));
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(48f, 24f);
            column.offsetMax = new Vector2(-48f, -28f);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            if (rules == null)
            {
                var err = UiKit.Label(column,
                    "Без справочника правил ни создать, ни открыть лист нельзя:\n" + rulesError, 18);
                err.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
                return;
            }

            var buttons = NewRect(column, "Actions", typeof(HorizontalLayoutGroup));
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            var row = buttons.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childForceExpandWidth = false;
            row.childControlHeight = true;
            row.childForceExpandHeight = true;
            AddButton(buttons, "Создать персонажа", 260f, CreateNew);
            AddButton(buttons, "Открыть лист…", 230f, OpenFromDisk);

            Spacer(column, 18f);
            var caption = UiKit.Label(column, "Последние листы", 20);
            caption.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var recents = RecentSheetsList.Get();
            if (recents.Count == 0)
            {
                var empty = UiKit.Label(column, "(пусто)", 16);
                empty.color = Muted;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
                return;
            }

            foreach (string path in recents) AddRecentRow(column, path);
        }

        void AddRecentRow(Transform parent, string path)
        {
            var rowRt = NewRect(parent, "Recent", typeof(HorizontalLayoutGroup));
            rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var row = rowRt.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childForceExpandWidth = false;
            row.childControlHeight = true;
            row.childForceExpandHeight = true;

            var btn = UiKit.Button(rowRt, DescribeSheet(path), () => OpenRecent(path));
            btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 460f;

            var pathLabel = UiKit.Label(rowRt, ShortPath(path), 13);
            pathLabel.color = Muted;
            pathLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        /// <summary>Убирает содержимое предыдущего состояния. SetParent(null) ПЕРЕД Destroy — потому
        /// что Destroy откладывается до конца кадра, и без отвязки старая раскладка ещё кадр делила бы
        /// место с новой. DestroyImmediate здесь нельзя: состояния переключаются из обработчиков нажатия,
        /// а он уничтожил бы кнопку прямо посреди её собственного клика.</summary>
        void ClearContent()
        {
            for (int i = contentRt.childCount - 1; i >= 0; i--)
            {
                var child = contentRt.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Destroy(child);
            }
        }

        // ── Создать / открыть / сохранить ────────────────────────────────────────

        void CreateNew()
        {
            // rules — это и есть SheetRulesSource.Rules, прочитанный один раз в Awake; здесь он заведомо
            // не null, потому что при null список кнопок вообще не строится.
            // Путь НЕ передаётся: персонаж ещё нигде не лежит, и первое «Сохранить» обязано спросить имя.
            OpenWizard(new CharacterFile { RulesId = rules.Id });
        }

        void OpenFromDisk()
        {
            try
            {
                var (file, path) = SheetFileService.Open();
                if (file == null) return;              // отменили — ничего не делаем
                SetCurrent(file, path);
                // Файл собран другим справочником — открываем, но предупреждаем сверху (задача 11).
                OpenSheet(file);
            }
            catch (System.Exception ex)
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось открыть лист", ex.Message);
            }
        }

        void OpenRecent(string path)
        {
            if (!File.Exists(path))
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось открыть лист",
                    $"файла больше нет: {path}");
                return;
            }
            try
            {
                var file = SheetFileService.LoadFrom(path);
                SetCurrent(file, path);
                OpenSheet(file);
            }
            catch (System.Exception ex)
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось открыть лист", ex.Message);
            }
        }

        /// <summary>Сохранить туда, откуда открыли; если файла ещё нет — спросить имя. Отмена диалога
        /// имени ничего не меняет.
        ///
        /// ОТВЕЧАЕТ, СОСТОЯЛАСЬ ЛИ ЗАПИСЬ, и зовущему это нужно: мастер показывает после сохранения
        /// «лист сохранён», а сказать так про отменённый диалог имени или про упавшую запись значило
        /// бы соврать в единственном месте, где человеку нечем проверить. Хуже того, ConfirmDialog
        /// держит на экране ровно один диалог: радостное сообщение просто СМЕНИЛО БЫ СОБОЙ рассказ об
        /// ошибке, который поставила эта же строчка catch.</summary>
        public bool SaveCurrent()
        {
            if (Current == null) return false;
            try
            {
                if (string.IsNullOrEmpty(CurrentPath))
                {
                    string path = SheetFileService.SaveAs(Current);
                    if (string.IsNullOrEmpty(path)) return false;   // отменили
                    CurrentPath = path;
                }
                else SheetFileService.Save(Current, CurrentPath);
                return true;
            }
            catch (System.Exception ex)
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось сохранить лист", ex.Message);
                return false;
            }
        }

        /// <summary>«Сохранить как…». Живёт здесь, а не в вызывающем экране, потому что у CurrentPath
        /// приватный сеттер: позови задача 11 SheetFileService.SaveAs напрямую — файл сохранился бы, а
        /// «куда сохранять дальше» осталось бы старым.</summary>
        public void SaveCurrentAs()
        {
            if (Current == null) return;
            try
            {
                string path = SheetFileService.SaveAs(Current);
                if (string.IsNullOrEmpty(path)) return;       // отменили
                CurrentPath = path;
            }
            catch (System.Exception ex)
            {
                ConfirmDialog.ShowInfo(UiKit.Font, "Не удалось сохранить лист", ex.Message);
            }
        }

        // ── Мелочи ───────────────────────────────────────────────────────────────

        /// <summary>Подпись строки списка. Файл читается НАПРЯМУЮ, а не через SheetFileService.LoadFrom:
        /// LoadFrom зовёт RecentSheetsList.Push, и показ пяти строк переставил бы сам список задом наперёд.
        /// Показ не имеет права ничего менять.</summary>
        string DescribeSheet(string path)
        {
            // fallback объявлен СНАРУЖИ try (его читает catch), а заполняется ВНУТРИ: сам
            // Path.GetFileNameWithoutExtension на Mono бросает ArgumentException на недопустимых знаках
            // пути, а сюда приходит строка из PlayerPrefs, которую мог испортить кто угодно. Стой этот
            // вызов над try — исключение улетело бы через AddRecentRow и ShowList прямо в Awake и оставило
            // бы экран недостроенным и немым. Начальное значение — сам путь: он длинный, но строку списка
            // опознать по нему можно, а бросить он не может.
            string fallback = path;
            try
            {
                fallback = Path.GetFileNameWithoutExtension(path);
                var file = CharacterSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
                string name = string.IsNullOrWhiteSpace(file.Name) ? fallback : file.Name;
                string className = ClassName(file.ClassId);
                return className == null ? name : $"{name} — {className} {file.Level}";
            }
            catch (System.Exception)
            {
                // Файла нет, он битый или собран версией новее — строку всё равно показываем: пропасть
                // из списка молча она не должна, а что именно случилось, скажет попытка открыть.
                return fallback;
            }
        }

        /// <summary>null, если класс не выбран или пришёл из чужого справочника — подставлять вместо
        /// незнакомого идентификатора чужое имя нельзя.</summary>
        string ClassName(string classId)
        {
            if (rules == null || string.IsNullOrEmpty(classId)) return null;
            foreach (var c in rules.Classes)
                if (c.Id == classId) return c.Name;
            return null;
        }

        static string ShortPath(string path)
        {
            const int max = 64;
            if (string.IsNullOrEmpty(path) || path.Length <= max) return path;
            return "…" + path.Substring(path.Length - (max - 1));
        }

        static RectTransform NewRect(Transform parent, string name, params System.Type[] extra)
        {
            var types = new System.Type[extra.Length + 1];
            types[0] = typeof(RectTransform);
            for (int i = 0; i < extra.Length; i++) types[i + 1] = extra[i];
            var go = new GameObject(name, types);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void AddButton(Transform parent, string label, float width, System.Action onClick)
        {
            var btn = UiKit.Button(parent, label, onClick);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 52f;
        }

        static void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        static void EdgeLine(RectTransform band, bool bottom)
        {
            var rt = NewRect(band, "EdgeLine", typeof(Image));
            rt.anchorMin = new Vector2(0f, bottom ? 0f : 1f);
            rt.anchorMax = new Vector2(1f, bottom ? 0f : 1f);
            rt.pivot = new Vector2(0.5f, bottom ? 0f : 1f);
            rt.offsetMin = new Vector2(0f, bottom ? 0f : -1f);
            rt.offsetMax = new Vector2(0f, bottom ? 1f : 0f);
            rt.GetComponent<Image>().color = HairLine;
        }
    }
}
