using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Полоска свойств выделенной карточки: восемь цветов рамки и три кегля текста. Висит
    /// над самой карточкой и едет за ней — свойства объекта живут у объекта, а не в дальнем углу
    /// экрана.
    ///
    /// Показывается, только когда выделена ИМЕННО карточка: у рисунка и картинки свойств пока нет,
    /// и пустая полоска над ними читалась бы как поломка.</summary>
    public class CardPropertyBar : MonoBehaviour
    {
        const float Swatch = 20f;

        public RectTransform RowRect { get; private set; }

        CanvasInteractionController controller;
        NotesCanvasController canvasController;
        Image[] colorFrames;
        Image[] fontFrames;
        Color activeColor;
        Color idleColor;
        string cardId;

        /// <summary>Чернильный из палитры — цвет карандаша, а не рамки: на рамке он неотличим от
        /// чёрной обводки, и в этой полоске его нет.</summary>
        static readonly int FrameColorCount = NotesPalette.InkIndex;

        static readonly (CardFontSize size, string label)[] FontDefs =
        {
            (CardFontSize.Small, "Малый"),
            (CardFontSize.Medium, "Средний"),
            (CardFontSize.Large, "Крупный"),
        };

        public void Initialize(CanvasInteractionController interactionController,
                               NotesCanvasController canvas, Transform parent)
        {
            controller = interactionController;
            canvasController = canvas;
            activeColor = ThemeService.Get(ThemeRole.Accent);
            idleColor = ThemeService.Get(ThemeRole.Border);
            var builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rowGO = new GameObject("CardPropertyBar");
            rowGO.transform.SetParent(parent, false);
            var rowBg = rowGO.AddComponent<Image>();
            ThemeService.Tag(rowBg, ThemeRole.Bg, 0.9f);
            var rowOutline = rowGO.AddComponent<Outline>();
            rowOutline.effectColor = ThemeService.Get(ThemeRole.Border);
            rowOutline.effectDistance = new Vector2(1f, -1f);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 5f;
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
            // ЯКОРЬ В ЦЕНТРЕ РОДИТЕЛЯ, А НЕ В УГЛУ, и это не вкусовщина: положение считается через
            // ScreenPointToLocalPointInRectangle, а он отдаёт координаты от ЦЕНТРА прямоугольника.
            // При угловом якоре полоска уехала бы ровно на половину доски. Так же устроена подсказка
            // в NotesToolbar.
            RowRect.anchorMin = new Vector2(0.5f, 0.5f);
            RowRect.anchorMax = new Vector2(0.5f, 0.5f);
            RowRect.pivot = new Vector2(0.5f, 0f);

            colorFrames = new Image[FrameColorCount];
            for (int i = 0; i < FrameColorCount; i++)
            {
                int index = i;
                var c = NotesPalette.At(i);
                colorFrames[i] = BuildSwatch(rowGO.transform, $"Frame_{i}",
                    new Color32(c.R, c.G, c.B, 255), () => ChooseColor(index));
            }

            fontFrames = new Image[FontDefs.Length];
            for (int i = 0; i < FontDefs.Length; i++)
            {
                var def = FontDefs[i];
                fontFrames[i] = BuildLabelButton(rowGO.transform, def.label, builtinFont, () => ChooseFont(def.size));
            }

            controller.OnSelectedObjectChanged += ShowFor;
            ShowFor(null);
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnSelectedObjectChanged -= ShowFor;
        }

        /// <summary>Выделили другой объект. Полоска остаётся только над карточкой — вид объекта
        /// спрашивается у самого представления, а не угадывается по данным.</summary>
        void ShowFor(string objectId)
        {
            cardId = null;
            if (objectId != null && canvasController != null
                && canvasController.GetView(objectId) is NoteCardView)
                cardId = objectId;

            if (RowRect != null) RowRect.gameObject.SetActive(cardId != null);
            if (cardId != null) RefreshVisuals();
        }

        /// <summary>Полоска догоняет карточку каждый кадр: карточку двигают мышью, доску возят и
        /// приближают, и подписки на всё это нет — LateUpdate дешевле трёх событий и не может от них
        /// отстать.</summary>
        void LateUpdate()
        {
            if (cardId == null || RowRect == null || canvasController == null) return;
            var view = canvasController.GetView(cardId) as NoteCardView;
            if (view == null || view.RectTransform == null) { RowRect.gameObject.SetActive(false); return; }

            var corners = new Vector3[4];
            view.RectTransform.GetWorldCorners(corners);
            // 1 — левый верхний, 2 — правый верхний: середина верхнего края.
            var worldTop = (corners[1] + corners[2]) * 0.5f;

            // ДВЕ КАМЕРЫ, И ПУТАТЬ ИХ НЕЛЬЗЯ. Мир → экран считается камерой доски (карточка живёт под
            // масштабируемым CanvasContainer), а экран → локальные координаты — камерой холста САМОЙ
            // полоски: у ScreenSpaceOverlay это null, и передать туда uiCamera значит промахнуться
            // тем сильнее, чем дальше карточка от центра.
            var screen = RectTransformUtility.WorldToScreenPoint(controller.uiCamera, worldTop);

            // Карточку могли увезти за край доски — тогда полоска повисла бы поверх вкладок и панели
            // инструментов, показывая свойства того, чего не видно.
            if (!controller.IsScreenPointOverViewport(screen)) { RowRect.gameObject.SetActive(false); return; }
            if (!RowRect.gameObject.activeSelf) RowRect.gameObject.SetActive(true);

            var parentRect = (RectTransform)RowRect.parent;
            var parentCanvas = parentRect.GetComponentInParent<Canvas>();
            var cam = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? parentCanvas.worldCamera
                : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, cam, out var local))
                RowRect.anchoredPosition = local + new Vector2(0f, 6f);
        }

        void ChooseColor(int index)
        {
            if (cardId == null) return;
            controller.SetCardFrameColor(cardId, index);
            RefreshVisuals();
        }

        void ChooseFont(CardFontSize size)
        {
            if (cardId == null) return;
            controller.SetCardFontSize(cardId, size);
            RefreshVisuals();
        }

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

        /// <summary>Кнопка с подписью — «Малый / Средний / Крупный». Шрифт легаси, как у всей
        /// обвязки: правило арка «панели не переезжают на TMP» касается и этой полоски.</summary>
        Image BuildLabelButton(Transform parent, string label, Font font, System.Action onClick)
        {
            var go = new GameObject($"Font_{label}");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(62f, Swatch);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 62f;
            le.preferredHeight = Swatch;

            var frame = go.AddComponent<Image>();
            frame.sprite = RoundedRectSprite.Get();
            frame.type = Image.Type.Sliced;
            frame.color = idleColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = font;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            text.text = label;

            return frame;
        }

        void RefreshVisuals()
        {
            var card = cardId != null && canvasController != null
                ? (canvasController.GetView(cardId) as NoteCardView)?.Data as NoteCardData
                : null;
            if (card == null) return;

            for (int i = 0; i < colorFrames.Length; i++)
                colorFrames[i].color = card.FrameColorIndex == i ? activeColor : idleColor;

            for (int i = 0; i < fontFrames.Length; i++)
                fontFrames[i].color = card.FontSize == FontDefs[i].size ? activeColor : idleColor;
        }
    }
}
