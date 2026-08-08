using System;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Что игрок ответил на вопрос о несохранённом листе.</summary>
    public enum UnsavedChoice
    {
        Save,
        Discard,
        Cancel
    }

    /// <summary>Вопрос при выходе с несохранённым листом: «Сохранить» / «Не сохранять» / «Отмена».
    ///
    /// СВОЙ ДИАЛОГ, А НЕ ConfirmDialog, и это вынужденно. ConfirmDialog даёт РОВНО ДВЕ кнопки, и
    /// кнопка подтверждения у него подписана словом «Удалить» намертво (ConfirmDialog.cs:66) — общий
    /// код части ДМ, править который этой арке запрещено. Третьего ответа там взять неоткуда, а
    /// третий ответ здесь не украшение: без «Отмена» единственный способ передумать — потерять работу.
    ///
    /// СЛОЙ НИЖЕ ConfirmDialog. Тот рисуется на sortingOrder 32000; этот — на 31000, и разница
    /// нужна: «Сохранить» на листе без пути ведёт в «Сохранить как…», а упавшая запись показывает
    /// ошибку ЧЕРЕЗ ConfirmDialog. Займи мы 32000 или выше — рассказ об ошибке лёг бы ПОД тем
    /// вопросом, из-за которого он появился. (На деле к моменту ошибки этот диалог уже уничтожен, но
    /// порядок слоёв — не то, что стоит держать верным по совпадению.)
    ///
    /// Подложка ПОЛУПРОЗРАЧНАЯ, в отличие от непрозрачной у ConfirmDialog: под вопросом «сохранить
    /// ли лист» должен быть виден сам лист — иначе вспомнить, что там несохранённого, не по чему.</summary>
    public static class UnsavedChangesPrompt
    {
        /// <summary>Имя корневого объекта. Константа, потому что DismissStranded ищет диалог ПО ИМЕНИ:
        /// после перезагрузки домена имя — единственная уцелевшая зацепка. Тот же приём и по той же
        /// причине, что у ConfirmDialog.CanvasName.</summary>
        const string CanvasName = "PlayerUnsavedPromptCanvas";

        static GameObject activeGO;

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.62f);
        static readonly Color Panel = new Color(0.14f, 0.14f, 0.15f);
        static readonly Color Accent = new Color(0.85f, 0.78f, 0.58f);
        static readonly Color Muted = new Color(0.72f, 0.70f, 0.67f);

        const float PanelWidth = 620f;

        // Высота сообщения считается по ЧИСЛУ СТРОК, а не Text.preferredHeight: в момент постройки
        // раскладки ещё не было, ширины у текста нет, и preferredHeight там врёт — та же ловушка, что
        // у PlayerPrepScreenController.FooterHeight и полосы предупреждений на листе.
        const int MessageFontSize = 15;
        // Ширина текста = ширина панели минус её поля (24 слева, 24 справа).
        const float MessageWidth = PanelWidth - 48f;
        const float MessageLineHeight = 21f;
        // Кегль 15 на полосе листа шириной 1860 точек даёт около 205 знаков в строке — это 9,07 точки
        // на знак. На 572 точках, стало быть, около 63.
        const float MessageCharsPerLine = MessageWidth / 9.07f;
        // Предел на случай сообщения, которого здесь пока не показывают. Двенадцать строк перебрать
        // нечем: самое длинное здешнее сообщение — фраза про несохранённое плюс путь к файлу, а путь
        // в Windows не длиннее 260 знаков, то есть пять строк с переносом.
        const float MaxMessageHeight = 12f * MessageLineHeight;

        /// <summary>Убирает диалог, до которого не дотянется ни один живой обработчик. Зовётся из
        /// PlayerPrepScreenController.Awake рядом с ConfirmDialog.DismissStranded — и по той же
        /// причине: перезагрузка домена в режиме Play (обычная перекомпиляция при возврате фокуса
        /// окну) стирает и статическое поле activeGO, и все runtime-подписки кнопок, а сам холст
        /// остаётся жить. Что осталось бы на экране — затемняющая подложка, глотающая нажатия, поверх
        /// всей сцены игрока, и убрать её было бы нечем.</summary>
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

        /// <summary>Спросить. Ответ приходит ровно один раз, диалог к этому моменту уже уничтожен —
        /// чтобы обработчик мог, ничего не зная про этот класс, показать поверх себя что угодно
        /// (например, рассказ об упавшей записи).</summary>
        public static void Ask(Font font, string title, string message, Action<UnsavedChoice> onResult)
        {
            if (activeGO != null) UnityEngine.Object.Destroy(activeGO);

            var canvasGO = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            activeGO = canvasGO;

            // Подложка глотает нажатия и НЕ закрывает диалог: у вопроса три равноправных ответа, и
            // «щёлкнул мимо» не значит ни одного из них. Button без слушателя — тот же приём, что у
            // ConfirmDialog.
            var backdrop = NewRect(canvasGO.transform, "Backdrop", typeof(Image), typeof(Button));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = Backdrop;

            var panel = NewRect(canvasGO.transform, "Prompt",
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            panel.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = Panel;
            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            // Высота панели идёт от содержимого. ContentSizeFitter здесь РАЗРЕШЁН, в отличие от блока
            // предыстории на листе: родитель панели — холст, а не раскладка, драться за высоту не с кем.
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleLabel = UiKit.Label(panel, title, 21);
            titleLabel.color = Accent;
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var body = UiKit.Label(panel, message ?? "", MessageFontSize, TextAnchor.UpperLeft);
            body.color = Muted;
            // Высота ПО САМОМУ СООБЩЕНИЮ, а не «две строки с запасом». Запас в 52 точки был взят на
            // глаз и не покрывал единственное сообщение, которое здесь и показывается: фраза про
            // несохранённое плюс строка «Файл: <настоящий путь>» — это три-четыре строки. А
            // verticalOverflow у UiKit.Label — Overflow, подписи проекта не режутся, так что лишние
            // строки рисовались бы ПОВЕРХ полосы кнопок.
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = MessageHeight(message);

            var footer = NewRect(panel, "Footer", typeof(HorizontalLayoutGroup));
            footer.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            var hlg = footer.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandHeight = false;

            AddChoice(font, footer, canvasGO, "Сохранить", 180f, onResult, UnsavedChoice.Save);
            AddChoice(font, footer, canvasGO, "Не сохранять", 180f, onResult, UnsavedChoice.Discard);
            AddChoice(font, footer, canvasGO, "Отмена", 150f, onResult, UnsavedChoice.Cancel);
        }

        /// <summary>Сколько точек займёт сообщение. Переводы строк считаются ОТДЕЛЬНО от переноса по
        /// словам: здешнее сообщение всегда состоит из фразы и строки про файл, и делить на строки
        /// одну только общую длину значило бы слить их в одну, как только обе окажутся короткими.
        ///
        /// Знаки в строке — прикидка, и другой здесь быть не может: точную высоту знает
        /// Text.preferredHeight, а спросить его в момент постройки нельзя (см. выше). ЛИШНЯЯ СТРОКА
        /// ДОБАВЛЯЕТСЯ НАРОЧНО, чтобы прикидка ошибалась только в одну сторону: пустая полоска внизу
        /// панели незаметна, а недостающая строка ложится текстом поверх кнопок.</summary>
        static float MessageHeight(string message)
        {
            if (string.IsNullOrEmpty(message)) return 0f;
            int lines = 1;
            foreach (var paragraph in message.Split('\n'))
                lines += Mathf.Max(1, Mathf.CeilToInt(paragraph.Length / MessageCharsPerLine));
            return Mathf.Min(lines * MessageLineHeight, MaxMessageHeight);
        }

        /// <summary>Кнопка ответа. Холст ловится ЗАМЫКАНИЕМ (owner), а не читается из activeGO в
        /// момент нажатия, и это не стиль: Destroy откладывается до конца кадра, поэтому кнопки
        /// заменённого вопроса ещё кадр живы и нажимаемы. Читай кнопка статическое поле — «Отмена»
        /// прошлого вопроса уничтожила бы НЫНЕШНИЙ, не ответив на него ни да, ни нет, и экран остался
        /// бы без диалога и без действия.</summary>
        static void AddChoice(Font font, Transform parent, GameObject owner, string label, float width,
            Action<UnsavedChoice> onResult, UnsavedChoice choice)
        {
            var btn = UiKit.Button(parent, label, () =>
            {
                // Диалог уничтожается ПЕРЕД ответом: обработчик «Сохранить» открывает системный
                // диалог имени файла и может показать поверх ошибку записи, а вопрос к этому моменту
                // уже не должен ни висеть на экране, ни глотать нажатия.
                if (activeGO == owner) activeGO = null;
                UnityEngine.Object.Destroy(owner);
                if (onResult != null) onResult(choice);
            });
            // Прямоугольник задаётся ЯВНО: UiKit.Button рождает RectTransform нулевого размера, а
            // раскладка полосы кнопок нарочно не управляет их размерами (childControl* = false), иначе
            // три кнопки растянулись бы на всю ширину панели.
            ((RectTransform)btn.transform).sizeDelta = new Vector2(width, 42f);
            var text = btn.GetComponentInChildren<Text>();
            if (text != null) { text.fontSize = 17; text.font = font; }
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
