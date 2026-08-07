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
        /// <summary>Высота ряда задаётся квадратиком — кнопка с подписью обязана быть с ним вровень,
        /// поэтому число берётся оттуда же, а не повторяется здесь.</summary>
        const float Swatch = ObjectBarAnchor.Swatch;

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

            RowRect = ObjectBarAnchor.BuildRow(parent, "CardPropertyBar");

            colorFrames = new Image[FrameColorCount];
            for (int i = 0; i < FrameColorCount; i++)
            {
                int index = i;
                var c = NotesPalette.At(i);
                colorFrames[i] = ObjectBarAnchor.BuildSwatch(RowRect, $"Frame_{i}",
                    new Color32(c.R, c.G, c.B, 255), idleColor, () => ChooseColor(index));
            }

            fontFrames = new Image[FontDefs.Length];
            for (int i = 0; i < FontDefs.Length; i++)
            {
                var def = FontDefs[i];
                fontFrames[i] = BuildLabelButton(RowRect, def.label, builtinFont, () => ChooseFont(def.size));
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

            if (!ObjectBarAnchor.Follow(RowRect, view.RectTransform, controller))
            { RowRect.gameObject.SetActive(false); return; }
            if (!RowRect.gameObject.activeSelf) RowRect.gameObject.SetActive(true);
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
