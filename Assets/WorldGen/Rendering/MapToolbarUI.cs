using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// 46px toolbar strip below the (40px) menu bar: tab segment (Карта/Редактор/Точки) on the
    /// left, zoom controls on the right. Owns which of the three docked panels is active.
    /// </summary>
    public class MapToolbarUI : MonoBehaviour
    {
        public const float BarHeightPixels = 46f;

        [Header("Источники")]
        public MapCameraController cameraController;
        [Tooltip("Панели, докающиеся под тулбар - в порядке Карта/Редактор/Точки.")]
        public GameObject mapLayersPanel;
        public GameObject editorBrushPanel;
        public GameObject poiToolPanel;

        Font builtinFont;
        Button[] tabButtons = new Button[3];
        Text zoomPercentLabel;
        int activeTab;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            SetActiveTab(0);
        }

        void Update()
        {
            if (cameraController != null && zoomPercentLabel != null)
                zoomPercentLabel.text = $"{Mathf.RoundToInt(cameraController.CurrentZoomPercent)}%";
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapToolbarCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // above the map, below floating panels (which use higher orders elsewhere)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var barGO = new GameObject("ToolbarBar");
            barGO.transform.SetParent(canvasTransform, false);
            var barImg = barGO.AddComponent<Image>();
            ThemeService.Tag(barImg, ThemeRole.Panel);
            var barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -40f); // sits directly below the 40px menu bar
            barRect.sizeDelta = new Vector2(0f, BarHeightPixels);

            BuildTabSegment(barGO.transform);
            BuildZoomControls(barGO.transform);
        }

        void BuildTabSegment(Transform parent)
        {
            var containerGO = new GameObject("TabSegment");
            containerGO.transform.SetParent(parent, false);
            var containerImg = containerGO.AddComponent<Image>();
            ThemeService.Tag(containerImg, ThemeRole.Bg);
            var containerRect = containerGO.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 0.5f);
            containerRect.anchorMax = new Vector2(0f, 0.5f);
            containerRect.pivot = new Vector2(0f, 0.5f);
            containerRect.anchoredPosition = new Vector2(12f, 0f);
            containerRect.sizeDelta = new Vector2(240f, 34f);

            var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            string[] labels = { "Карта", "Редактор", "Точки" };
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                var btnGO = new GameObject($"Tab_{labels[i]}");
                btnGO.transform.SetParent(containerGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActiveTab(captured));
                tabButtons[i] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = labels[i];
                text.font = builtinFont;
                text.fontSize = 13;
                text.alignment = TextAnchor.MiddleCenter;
                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }
        }

        void BuildZoomControls(Transform parent)
        {
            var rowGO = new GameObject("ZoomControls");
            rowGO.transform.SetParent(parent, false);
            var rowRect = rowGO.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(1f, 0.5f);
            rowRect.anchorMax = new Vector2(1f, 0.5f);
            rowRect.pivot = new Vector2(1f, 0.5f);
            rowRect.anchoredPosition = new Vector2(-12f, 0f);
            rowRect.sizeDelta = new Vector2(220f, 34f);

            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childAlignment = TextAnchor.MiddleRight;

            // "-" zooms out (sees more, orthographicSize grows -> multiplier > 1), "+" zooms in
            // (sees less/magnified, orthographicSize shrinks -> multiplier < 1). See
            // MapCameraController.ZoomBy's doc comment.
            AddIconButton(rowGO.transform, "−", () => cameraController?.ZoomBy(cameraController.buttonZoomStep));
            zoomPercentLabel = AddZoomLabel(rowGO.transform, () => cameraController?.ResetZoom());
            AddIconButton(rowGO.transform, "+", () => cameraController?.ZoomBy(1f / cameraController.buttonZoomStep));
            AddFitButton(rowGO.transform, () => cameraController?.ResetZoom());
        }

        void AddIconButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"ZoomBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 30f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        Text AddZoomLabel(Transform parent, System.Action onClick)
        {
            var go = new GameObject("ZoomPercent");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 50f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "100%";
            text.font = builtinFont;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            return text;
        }

        void AddFitButton(Transform parent, System.Action onClick)
        {
            var go = new GameObject("FitButton");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 90f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "По размеру";
            text.font = builtinFont;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        public void SetActiveTab(int index)
        {
            activeTab = index;
            if (mapLayersPanel != null) mapLayersPanel.SetActive(index == 0);
            if (editorBrushPanel != null) editorBrushPanel.SetActive(index == 1);
            if (poiToolPanel != null) poiToolPanel.SetActive(index == 2);

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                bool active = i == index;
                ThemeService.Tag(tabButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Bg);
                var label = tabButtons[i].GetComponentInChildren<Text>();
                ThemeService.Tag(label, active ? ThemeRole.AccentInk : ThemeRole.Mut);
            }
        }
    }
}
