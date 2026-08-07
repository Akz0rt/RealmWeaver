using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Полоска свойств выделенного рисунка: тон листа. Висит над самим рисунком и едет за
    /// ним — так же, как полоска карточки.
    ///
    /// ОТДЕЛЬНЫЙ КЛАСС, А НЕ ВЕТКА В CardPropertyBar: ряды кнопок у карточки и у рисунка не
    /// пересекаются ни одной кнопкой, и ветвление по типу объекта внутри одной полоски было бы
    /// переиспользованием ради переиспользования. Общее у них живёт в ObjectBarAnchor.</summary>
    public class DrawingPropertyBar : MonoBehaviour
    {
        public RectTransform RowRect { get; private set; }

        CanvasInteractionController controller;
        NotesCanvasController canvasController;
        Image[] toneFrames;
        Color activeColor;
        Color idleColor;
        string drawingId;

        public void Initialize(CanvasInteractionController interactionController,
                               NotesCanvasController canvas, Transform parent)
        {
            controller = interactionController;
            canvasController = canvas;
            activeColor = ThemeService.Get(ThemeRole.Accent);
            idleColor = ThemeService.Get(ThemeRole.Border);

            RowRect = ObjectBarAnchor.BuildRow(parent, "DrawingPropertyBar");

            toneFrames = new Image[PaperPalette.Count];
            for (int i = 0; i < PaperPalette.Count; i++)
            {
                int index = i;
                var t = PaperPalette.At(i);
                toneFrames[i] = ObjectBarAnchor.BuildSwatch(RowRect, $"Paper_{i}",
                    new Color32(t.R, t.G, t.B, 255), idleColor, () => ChooseTone(index),
                    checkered: i == PaperPalette.TransparentIndex);
            }

            controller.OnSelectedObjectChanged += ShowFor;
            ShowFor(null);
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnSelectedObjectChanged -= ShowFor;
        }

        /// <summary>Вид объекта спрашивается у самого представления, а не угадывается по данным.</summary>
        void ShowFor(string objectId)
        {
            drawingId = null;
            if (objectId != null && canvasController != null
                && canvasController.GetView(objectId) is DrawingObjectView)
                drawingId = objectId;

            if (RowRect != null) RowRect.gameObject.SetActive(drawingId != null);
            if (drawingId != null) RefreshVisuals();
        }

        /// <summary>Полоска догоняет рисунок каждый кадр — рисунок двигают мышью, доску возят и
        /// приближают, и подписки на всё это нет.</summary>
        void LateUpdate()
        {
            if (drawingId == null || RowRect == null || canvasController == null) return;
            var view = canvasController.GetView(drawingId) as DrawingObjectView;
            if (view == null || view.RectTransform == null) { RowRect.gameObject.SetActive(false); return; }

            if (!ObjectBarAnchor.Follow(RowRect, view.RectTransform, controller))
            { RowRect.gameObject.SetActive(false); return; }
            if (!RowRect.gameObject.activeSelf) RowRect.gameObject.SetActive(true);

            // ПОДСВЕТКА ОБНОВЛЯЕТСЯ КАЖДЫЙ КАДР, а не только при смене выделения. Отмена не меняет
            // выделения, но заменяет данные новыми объектами: без этого после Ctrl+Z рисунок
            // возвращался бы к прежнему тону, а подсвеченным оставался бы отменённый — полоска
            // показывала бы не то, что на листе.
            RefreshVisuals();
        }

        void ChooseTone(int index)
        {
            if (drawingId == null) return;
            controller.SetPaperIndex(drawingId, index);
            RefreshVisuals();
        }

        void RefreshVisuals()
        {
            var drawing = drawingId != null && canvasController != null
                ? (canvasController.GetView(drawingId) as DrawingObjectView)?.Data as DrawingObjectData
                : null;
            if (drawing == null) return;

            for (int i = 0; i < toneFrames.Length; i++)
                toneFrames[i].color = drawing.PaperIndex == i ? activeColor : idleColor;
        }
    }
}
