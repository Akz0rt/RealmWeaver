using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Карта" tab content - layer visibility toggles. Laid out to match
    /// design_handoff_realmweaver_ui screens/*/01-main.png ("Слои" card): a "СЛОИ" header with a
    /// "Сбросить" link, tight checkbox+label rows, checked rows tinted with accent-soft.
    /// </summary>
    public class MapLayersPanel : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;

        Font builtinFont;
        readonly List<Toggle> toggles = new List<Toggle>();

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
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.96f);
            AddBorder(panelGO, ThemeRole.Border);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f - MapToolbarUI.BarHeightPixels - 40f); // below 40px menu + 46px toolbar
            panelRect.sizeDelta = new Vector2(216f, 0f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 3f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiShadow.Add(panelRect);

            var t = panelGO.transform;
            BuildHeaderRow(t);
            AddLayerToggleRow(t, "Рельеф",           true, on => mapRenderer?.SetShowReliefLayer(on));
            AddLayerToggleRow(t, "Биом / климат",    true, on => mapRenderer?.SetShowBiomeLayer(on));
            AddLayerToggleRow(t, "Границы регионов", true, on => mapRenderer?.SetShowRegionBordersLayer(on));
            AddLayerToggleRow(t, "Береговая линия",  true, on => mapRenderer?.SetShowCoastlineLayer(on));
        }

        void BuildHeaderRow(Transform parent)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 18f;
            var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childForceExpandWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "СЛОИ";
            title.font = builtinFont;
            title.fontSize = 11;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(title, ThemeRole.Mut);
            titleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var resetGO = new GameObject("Reset");
            resetGO.transform.SetParent(rowGO.transform, false);
            var reset = resetGO.AddComponent<Text>();
            reset.text = "Сбросить";
            reset.font = builtinFont;
            reset.fontSize = 11;
            reset.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(reset, ThemeRole.Accent);
            resetGO.AddComponent<LayoutElement>().preferredWidth = 64f;
            var resetBtn = resetGO.AddComponent<Button>();
            resetBtn.targetGraphic = reset;
            resetBtn.transition = Selectable.Transition.None;
            resetBtn.onClick.AddListener(ResetLayers);
        }

        void ResetLayers()
        {
            foreach (var toggle in toggles)
                toggle.isOn = true; // fires onValueChanged, which re-shows the layer and re-tints the row
        }

        void AddLayerToggleRow(Transform parent, string label, bool defaultOn, System.Action<bool> onChanged)
        {
            var rowGO = new GameObject($"{label}LayerRow");
            rowGO.transform.SetParent(parent, false);
            var rowBg = rowGO.AddComponent<Image>();
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(6, 6, 0, 0);
            hLayout.spacing = 8f;
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleLeft;

            var toggle = AddCheckbox(rowGO.transform, defaultOn);
            toggles.Add(toggle);
            AddLabel(rowGO.transform, label);

            void ApplyRow(bool on)
            {
                onChanged?.Invoke(on);
                ThemeService.Tag(rowBg, ThemeRole.AccentSoft, on ? 1f : 0f); // checked row → subtle accent-soft tint
            }
            toggle.onValueChanged.AddListener(v => ApplyRow(v));
            ApplyRow(defaultOn);
        }

        // ── Widget helpers ──

        Text AddLabel(Transform parent, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = 12;
            ThemeService.Tag(label, ThemeRole.Txt);
            label.alignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;      // fill the remaining row width
            le.preferredHeight = 20f;
            return label;
        }

        Toggle AddCheckbox(Transform parent, bool defaultOn)
        {
            var go = new GameObject("Checkbox");
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            AddBorder(go, ThemeRole.Border);
            toggle.targetGraphic = bg;
            toggle.isOn = defaultOn;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 16f;
            le.preferredHeight = 16f;

            var checkGO = new GameObject("Check");
            checkGO.transform.SetParent(go.transform, false);
            var checkImg = checkGO.AddComponent<Image>();
            ThemeService.Tag(checkImg, ThemeRole.Accent);
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.18f, 0.18f);
            checkRect.anchorMax = new Vector2(0.82f, 0.82f);
            checkRect.sizeDelta = Vector2.zero;
            toggle.graphic = checkImg;
            return toggle;
        }

        void AddBorder(GameObject go, ThemeRole role)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(role);
            outline.effectDistance = new Vector2(1f, -1f);
        }
    }
}
