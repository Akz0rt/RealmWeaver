using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Карта" tab content - layer visibility toggles. Extracted unchanged from
    /// MapEditorPanel.BuildMapTab (this project's Main-screen shell redesign,
    /// 2026-07-06) so MapToolbarUI has a standalone panel to dock/undock per tab.
    /// </summary>
    public class MapLayersPanel : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapLayersCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("LayersPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.9f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f - MapToolbarUI.BarHeightPixels - 40f); // below 40px menu + 46px toolbar
            panelRect.sizeDelta = new Vector2(216f, 0f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = panelGO.transform;
            AddLabel(t, "─── Слои ───", bold: false, role: ThemeRole.Mut);
            AddLayerToggleRow(t, "Рельеф",            true, on => mapRenderer?.SetShowReliefLayer(on));
            AddLayerToggleRow(t, "Биом / климат",     true, on => mapRenderer?.SetShowBiomeLayer(on));
            AddLayerToggleRow(t, "Границы регионов",  true, on => mapRenderer?.SetShowRegionBordersLayer(on));
            AddLayerToggleRow(t, "Береговая линия",   true, on => mapRenderer?.SetShowCoastlineLayer(on));
        }

        void AddLayerToggleRow(Transform parent, string label, bool defaultOn, System.Action<bool> onChanged)
        {
            var rowGO = new GameObject($"{label}LayerRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var toggle = AddToggle(rowGO.transform, defaultOn);
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            AddLabel(rowGO.transform, label);
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

        Toggle AddToggle(Transform parent, bool defaultOn)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            toggle.targetGraphic = bg;
            toggle.isOn = defaultOn;

            var checkGO = new GameObject("Check");
            checkGO.transform.SetParent(go.transform, false);
            var checkImg = checkGO.AddComponent<Image>();
            ThemeService.Tag(checkImg, ThemeRole.Accent);
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkRect.sizeDelta = Vector2.zero;
            toggle.graphic = checkImg;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(18f, 18f);
            return toggle;
        }
    }
}
