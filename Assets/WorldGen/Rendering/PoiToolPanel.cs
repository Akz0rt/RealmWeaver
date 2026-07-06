using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Точки" tab content - extracted unchanged from MapEditorPanel.BuildPoiTab (Main-screen shell
    /// redesign, 2026-07-06). Functionally identical to the pre-redesign panel; Screen D's spec
    /// ("POI Screen Redesign") will replace this class's internals with the real list+search+filter
    /// design - do not add list/search/filter logic here.
    /// </summary>
    public class PoiToolPanel : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;

        int poiCount = 5;
        Text poiCountLabel;

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void OnGeneratePois()
        {
            if (poiManager == null) return;
            poiManager.GenerateAll(poiCount);
        }

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiToolCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("PoiPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.7f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f - MapToolbarUI.BarHeightPixels - 40f); // below 40px menu + 46px toolbar
            panelRect.sizeDelta = new Vector2(300f, 0f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;      // respect each child's LayoutElement.preferredHeight
            layout.childForceExpandHeight = false; // ...and don't stretch to fill (default true = the panel-bloat bug)
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiShadow.Add(panelRect);

            BuildPoiTab(panelGO.transform);
        }

        void BuildPoiTab(Transform t)
        {
            AddLabel(t, "─── Точки интереса ───", bold: false, role: ThemeRole.Mut);

            var countRowGO = new GameObject("PoiCountRow");
            countRowGO.transform.SetParent(t, false);
            var cHLG = countRowGO.AddComponent<HorizontalLayoutGroup>();
            cHLG.spacing = 4f;
            cHLG.childControlWidth = false;
            cHLG.childControlHeight = false;
            countRowGO.AddComponent<LayoutElement>().preferredHeight = 22f;

            var cLblGO = new GameObject("Label");
            cLblGO.transform.SetParent(countRowGO.transform, false);
            var cLblText = cLblGO.AddComponent<Text>();
            cLblText.text = "Количество:";
            cLblText.font = builtinFont;
            cLblText.fontSize = 12;
            ThemeService.Tag(cLblText, ThemeRole.Txt);
            cLblText.alignment = TextAnchor.MiddleLeft;
            cLblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 22f);

            AddSmallButton(countRowGO.transform, "−", () =>
            {
                if (poiCount > 0) poiCount--;
                if (poiCountLabel != null) poiCountLabel.text = poiCount.ToString();
            });

            var countDisplayGO = new GameObject("CountDisplay");
            countDisplayGO.transform.SetParent(countRowGO.transform, false);
            poiCountLabel = countDisplayGO.AddComponent<Text>();
            poiCountLabel.text = poiCount.ToString();
            poiCountLabel.font = builtinFont;
            poiCountLabel.fontSize = 12;
            ThemeService.Tag(poiCountLabel, ThemeRole.Txt);
            poiCountLabel.alignment = TextAnchor.MiddleCenter;
            countDisplayGO.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 22f);

            AddSmallButton(countRowGO.transform, "+", () =>
            {
                poiCount++;
                if (poiCountLabel != null) poiCountLabel.text = poiCount.ToString();
            });

            AddButton(t, "Сгенерировать точки интереса", OnGeneratePois, ThemeRole.Accent);
            AddButton(t, "Добавить одну точку", () => poiManager?.AddOne(), ThemeRole.Elev);
            AddButton(t, "Очистить все", () => poiManager?.ClearAll(), ThemeRole.Danger);

            var hint = AddLabel(t, "Кликните по точке на карте, чтобы её отредактировать в отдельной панели.");
            ThemeService.Tag(hint, ThemeRole.Mut);
            hint.fontSize = 11;
            hint.fontStyle = FontStyle.Italic;
        }

        // ── Widget helpers (duplicated from MapEditorPanel, same as every other extracted panel) ──

        Text AddLabel(Transform parent, string text, bool bold = false, ThemeRole? role = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = bold ? 15 : 12;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            ThemeService.Tag(label, role ?? ThemeRole.Txt);
            label.alignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = bold ? 20f : 16f;
            return label;
        }

        void AddButton(Transform parent, string label, System.Action onClick, ThemeRole? role = null)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var backgroundRole = role ?? ThemeRole.Elev;
            ThemeService.Tag(img, backgroundRole);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, backgroundRole == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        void AddSmallButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"SmallBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 14;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }
    }
}
