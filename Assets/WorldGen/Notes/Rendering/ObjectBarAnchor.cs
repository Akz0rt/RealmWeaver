using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Общее устройство полосок свойств: как полоска строится, как она догоняет свой объект
    /// и как выглядит квадратик выбора.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНОЕ МЕСТО. Полосок стало две — у карточки и у рисунка, — и ряды кнопок у них не
    /// пересекаются НИ ОДНОЙ кнопкой, поэтому общего предка у самих полосок нет и быть не должно.
    /// Общее у них другое: три уже принятых нетривиальных решения, каждое из которых, будучи
    /// скопированным, разошлось бы молча — якорь в центре родителя, выбор камеры и вид квадратика.
    /// Здесь они лежат в одном экземпляре.</summary>
    public static class ObjectBarAnchor
    {
        public const float Swatch = 20f;

        /// <summary>Переиспользуемый буфер: Follow зовётся из LateUpdate каждой видимой полоски
        /// каждый кадр, а GetWorldCorners требует массив на четыре элемента.</summary>
        static readonly Vector3[] corners = new Vector3[4];

        /// <summary>Ряд с фоном, рамкой и раскладкой — общий каркас обеих полосок.</summary>
        public static RectTransform BuildRow(Transform parent, string name)
        {
            var rowGO = new GameObject(name);
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

            var rect = (RectTransform)rowGO.transform;
            // ЯКОРЬ В ЦЕНТРЕ РОДИТЕЛЯ, А НЕ В УГЛУ, и это не вкусовщина: положение считается через
            // ScreenPointToLocalPointInRectangle, а он отдаёт координаты от ЦЕНТРА прямоугольника.
            // При угловом якоре полоска уехала бы ровно на половину доски. Так же устроена подсказка
            // в NotesToolbar.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            return rect;
        }

        /// <summary>Ставит полоску над серединой верхнего края объекта. Возвращает false, если объект
        /// уехал за край доски и полоску надо спрятать.</summary>
        public static bool Follow(RectTransform row, RectTransform target,
                                  CanvasInteractionController controller)
        {
            if (row == null || target == null || controller == null) return false;

            target.GetWorldCorners(corners);
            // 1 — левый верхний, 2 — правый верхний: середина верхнего края.
            var worldTop = (corners[1] + corners[2]) * 0.5f;

            // ДВЕ КАМЕРЫ, И ПУТАТЬ ИХ НЕЛЬЗЯ. Мир → экран считается камерой доски (объект живёт под
            // масштабируемым CanvasContainer), а экран → локальные координаты — камерой холста САМОЙ
            // полоски: у ScreenSpaceOverlay это null, и передать туда uiCamera значит промахнуться
            // тем сильнее, чем дальше объект от центра.
            var screen = RectTransformUtility.WorldToScreenPoint(controller.uiCamera, worldTop);

            // Объект могли увезти за край доски — тогда полоска повисла бы поверх вкладок и панели
            // инструментов, показывая свойства того, чего не видно.
            if (!controller.IsScreenPointOverViewport(screen)) return false;

            var parentRect = (RectTransform)row.parent;
            var parentCanvas = parentRect.GetComponentInParent<Canvas>();
            var cam = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? parentCanvas.worldCamera
                : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, cam, out var local))
                row.anchoredPosition = local + new Vector2(0f, 6f);
            return true;
        }

        /// <summary>Квадратик выбора: скруглённая рамка в две ступени и заливка с отступом 2 px.
        /// Возвращается ИМЕННО рамка — по её цвету полоска показывает, что выбрано сейчас.
        ///
        /// Живёт здесь, а не в каждой полоске: вид квадратика — принятое решение, а две копии
        /// расходятся молча.</summary>
        public static Image BuildSwatch(Transform parent, string name, Color fill, Color idleColor,
                                        System.Action onClick, bool checkered = false)
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
            fillImg.color = checkered ? Color.white : fill;
            fillImg.raycastTarget = false;
            var fillRect = fillImg.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            // ПРОЗРАЧНЫЙ ТОН РИСУЕТСЯ ШАХМАТКОЙ, А НЕ ПУСТЫМ КВАДРАТОМ: пустой квадрат на полоске
            // неотличим от «кнопка не нарисовалась», и ДМ решил бы, что интерфейс сломан.
            if (checkered)
            {
                AddQuad(fillGO.transform, "TL", new Vector2(0f, 0.5f), new Vector2(0.5f, 1f));
                AddQuad(fillGO.transform, "BR", new Vector2(0.5f, 0f), new Vector2(1f, 0.5f));
            }

            return frame;
        }

        static void AddQuad(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.78f, 0.78f, 0.80f, 1f);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
