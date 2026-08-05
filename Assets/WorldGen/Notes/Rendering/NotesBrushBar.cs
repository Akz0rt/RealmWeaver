using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Полоска настроек карандаша: девять цветов палитры, ластик и три толщины. Живёт под
    /// панелью инструментов и показывается только при активном «Рисунке» — настройки кисти незачем
    /// занимать место, пока ДМ двигает карточки.
    ///
    /// Собирается той же дорогой, что NotesToolbar: HorizontalLayoutGroup + ContentSizeFitter,
    /// фон тегом Bg, обводка цветом Border, у кнопок transition = None (иначе ColorTint перекрашивал
    /// бы квадратики палитры под курсором в свой цвет).</summary>
    public class NotesBrushBar : MonoBehaviour
    {
        const float Swatch = 22f;
        const float Gap = 6f;

        public RectTransform RowRect { get; private set; }

        CanvasInteractionController controller;
        Image[] colorFrames;
        Image eraserFrame;
        Image[] widthFrames;
        Color activeColor;
        Color idleColor;

        public void Initialize(CanvasInteractionController interactionController, Transform parent, RectTransform toolbarRow)
        {
            controller = interactionController;
            // Цвета читаются здесь, а не в инициализаторе поля: ThemeService идёт в PlayerPrefs, а
            // тот в инициализаторе поля ещё не поднят (та же причина, что у NotesToolbar.activeColor).
            activeColor = ThemeService.Get(ThemeRole.Accent);
            idleColor = ThemeService.Get(ThemeRole.Border);

            // Начальные значения — то, чем рисовали в прошлый раз.
            controller.brushColorIndex = NotesUserPrefs.BrushColorIndex;
            controller.brushWidth = NotesUserPrefs.BrushStroke;
            controller.brushIsEraser = false;

            var rowGO = new GameObject("NotesBrushBar");
            rowGO.transform.SetParent(parent, false);
            var rowBg = rowGO.AddComponent<Image>();
            ThemeService.Tag(rowBg, ThemeRole.Bg, 0.9f);
            var rowOutline = rowGO.AddComponent<Outline>();
            rowOutline.effectColor = ThemeService.Get(ThemeRole.Border);
            rowOutline.effectDistance = new Vector2(1f, -1f);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = Gap;
            hLayout.padding = new RectOffset(6, 6, 4, 4);
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            var fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RowRect = (RectTransform)rowGO.transform;
            RowRect.anchorMin = new Vector2(0f, 1f);
            RowRect.anchorMax = new Vector2(0f, 1f);
            RowRect.pivot = new Vector2(0f, 1f);
            // Под панелью инструментов, ровно по её левому краю. Высота панели читается из её же
            // прямоугольника, а не заводится числом: она собрана ContentSizeFitter'ом и зависит от
            // размера кнопок.
            float toolbarHeight = toolbarRow != null ? toolbarRow.rect.height : 44f;
            var toolbarPos = toolbarRow != null ? toolbarRow.anchoredPosition : new Vector2(12f, -8f);
            RowRect.anchoredPosition = new Vector2(toolbarPos.x, toolbarPos.y - toolbarHeight - 6f);

            colorFrames = new Image[NotesPalette.Count];
            for (int i = 0; i < NotesPalette.Count; i++)
            {
                int index = i;
                var c = NotesPalette.At(i);
                colorFrames[i] = BuildSwatch(rowGO.transform, $"Color_{i}",
                    new Color32(c.R, c.G, c.B, 255), () => ChooseColor(index));
            }

            eraserFrame = BuildSwatch(rowGO.transform, "Eraser", new Color(1f, 1f, 1f, 0.9f), ChooseEraser);

            widthFrames = new Image[3];
            var widths = new[] { BrushWidth.Thin, BrushWidth.Medium, BrushWidth.Thick };
            for (int i = 0; i < widths.Length; i++)
            {
                var w = widths[i];
                widthFrames[i] = BuildWidthButton(rowGO.transform, w, () => ChooseWidth(w));
            }

            // Полоска принадлежит «Рисунку» и появляется вместе с ним. ShowForTool только показывает
            // и прячет — обратно в SetTool она не звонит, иначе получилась бы петля.
            controller.OnToolChanged += ShowForTool;
            ShowForTool(controller.ActiveTool);
            RefreshVisuals();
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnToolChanged -= ShowForTool;
        }

        void ShowForTool(NotesTool tool)
        {
            if (RowRect != null) RowRect.gameObject.SetActive(tool == NotesTool.Drawing);
        }

        void ChooseColor(int index)
        {
            controller.SetBrush(index, controller.brushWidth);
            RefreshVisuals();
        }

        void ChooseEraser()
        {
            // Цвет НЕ трогаем: ДМ стёр кусок и продолжает рисовать тем же, чем рисовал.
            controller.brushIsEraser = true;
            RefreshVisuals();
        }

        void ChooseWidth(BrushWidth width)
        {
            // Толщина выбирается и для ластика тоже, поэтому SetBrush здесь не годится — он снял бы
            // ластик. Пишем толщину напрямую и запоминаем её.
            controller.brushWidth = width;
            NotesUserPrefs.BrushStroke = width;
            RefreshVisuals();
        }

        /// <summary>Квадратик цвета: рамка (её красит выделение) и заливка внутри.</summary>
        Image BuildSwatch(Transform parent, string name, Color fill, System.Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Swatch, Swatch);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = Swatch;
            le.preferredHeight = Swatch;

            var frame = go.AddComponent<Image>();
            frame.sprite = RoundedRectSprite.Get();
            frame.type = Image.Type.Sliced;
            frame.color = idleColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = frame;
            // Цвет рамки этот класс держит сам; ColorTint перекрашивал бы её на наведении.
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(go.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = RoundedRectSprite.Get();
            fillImg.type = Image.Type.Sliced;
            fillImg.color = fill;
            fillImg.raycastTarget = false;
            var fillRect = fillImg.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            return frame;
        }

        /// <summary>Кнопка толщины: чёрная точка настоящего размера, чтобы выбирать глазом, а не по
        /// подписи.</summary>
        Image BuildWidthButton(Transform parent, BrushWidth width, System.Action onClick)
        {
            var frame = BuildSwatch(parent, $"Width_{width}", new Color(0f, 0f, 0f, 0f), onClick);

            var dotGO = new GameObject("Dot");
            dotGO.transform.SetParent(frame.transform, false);
            var dot = dotGO.AddComponent<Image>();
            dot.sprite = GetDotSprite();
            ThemeService.Tag(dot, ThemeRole.Txt);
            dot.raycastTarget = false;
            float d = Mathf.Min(Swatch - 6f, BrushOps.DiameterInCanvasUnits(width) + 2f);
            dot.rectTransform.sizeDelta = new Vector2(d, d);
            dot.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            dot.rectTransform.anchoredPosition = Vector2.zero;

            return frame;
        }

        static Sprite cachedDotSprite;

        /// <summary>Круг для точек толщины — тем же способом, каким NotesToolbar делает свою подложку:
        /// рисуется в рантайме и кэшируется на класс, чтобы в проекте не заводилось файлов-картинок.</summary>
        static Sprite GetDotSprite()
        {
            if (cachedDotSprite != null) return cachedDotSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "NotesBrushDot";

            var center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = (new Vector2(x + 0.5f, y + 0.5f) - center).magnitude;
                    float a = Mathf.Clamp01(radius - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();

            cachedDotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedDotSprite;
        }

        void RefreshVisuals()
        {
            for (int i = 0; i < colorFrames.Length; i++)
                colorFrames[i].color = (!controller.brushIsEraser && controller.brushColorIndex == i) ? activeColor : idleColor;

            eraserFrame.color = controller.brushIsEraser ? activeColor : idleColor;

            var widths = new[] { BrushWidth.Thin, BrushWidth.Medium, BrushWidth.Thick };
            for (int i = 0; i < widthFrames.Length; i++)
                widthFrames[i].color = controller.brushWidth == widths[i] ? activeColor : idleColor;
        }
    }
}
