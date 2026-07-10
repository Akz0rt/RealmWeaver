using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.RegionLabels;
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
        public RegionLabelOverlay regionLabelOverlay;
        public RegionLabelManager regionLabelManager;

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
            ThemeService.Tag(panelImg, ThemeRole.Panel);
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
            AddLayerToggleRow(t, "Границы регионов", false, on => mapRenderer?.SetShowRegionBordersLayer(on));
            AddLayerToggleRow(t, "Береговая линия",  false, on => mapRenderer?.SetShowCoastlineLayer(on));
            AddLayerToggleRow(t, "Декорации", mapRenderer != null && mapRenderer.decorationConfig.enabled,
                on => mapRenderer?.SetShowDecorations(on));
            AddLayerToggleRow(t, "Названия регионов", true, on => regionLabelOverlay?.SetVisible(on));
            AddLayerToggleRow(t, "Редактировать названия", false, on => regionLabelOverlay?.SetEditMode(on));
            AddSliderRow(t, "Плотность названий", 0f, 1f,
                mapRenderer != null ? mapRenderer.labelDensity : 0.4f,
                v => { if (mapRenderer != null) mapRenderer.labelDensity = v; });
            AddActionButtonRow(t, "Пересоздать названия", () => regionLabelManager?.SeedFromCells(),
                "+ Название", () => regionLabelOverlay?.ToggleAddMode());
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

        /// <summary>Two small text-buttons side by side (mirrors the "Сбросить" link-button in
        /// BuildHeaderRow: a Text component doubling as its own Button.targetGraphic).</summary>
        void AddActionButtonRow(Transform parent, string label1, System.Action onClick1, string label2, System.Action onClick2)
        {
            var rowGO = new GameObject("RegionLabelActionsRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = true;
            hLayout.childAlignment = TextAnchor.MiddleCenter;

            AddActionButton(rowGO.transform, label1, onClick1);
            AddActionButton(rowGO.transform, label2, onClick2);
        }

        void AddActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 10;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Accent);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = text;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        // ── Widget helpers ──

        /// <summary>Labeled 0..1 slider row (label + %-value above, slider below), mirroring
        /// EditorBrushPanel's BuildLabeledSlider/BuildSlider (same Slider/fill/handle setup + theming).</summary>
        void AddSliderRow(Transform parent, string label, float min, float max, float def, System.Action<float> onChanged)
        {
            var groupGO = new GameObject($"{label}Group");
            groupGO.transform.SetParent(parent, false);
            var gvlg = groupGO.AddComponent<VerticalLayoutGroup>();
            gvlg.spacing = 3f;
            gvlg.childControlWidth = true;
            gvlg.childForceExpandWidth = true;
            gvlg.childControlHeight = true;
            gvlg.childForceExpandHeight = false;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var headGO = new GameObject($"{label}Head");
            headGO.transform.SetParent(groupGO.transform, false);
            headGO.AddComponent<LayoutElement>().preferredHeight = 16f;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(headGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 12;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var valGO = new GameObject("Value");
            valGO.transform.SetParent(headGO.transform, false);
            var valueLabel = valGO.AddComponent<Text>();
            valueLabel.font = builtinFont;
            valueLabel.fontSize = 11;
            ThemeService.Tag(valueLabel, ThemeRole.Accent);
            valueLabel.alignment = TextAnchor.MiddleRight;
            var valRect = valGO.GetComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.4f, 0f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            var sliderGO = new GameObject($"{label}Slider");
            sliderGO.transform.SetParent(groupGO.transform, false);
            var slider = BuildSlider(sliderGO, def, min, max);
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 14f;

            void Refresh(float v) => valueLabel.text = $"{Mathf.RoundToInt(v * 100f)}%";
            Refresh(def);
            slider.onValueChanged.AddListener(v => { Refresh(v); onChanged?.Invoke(v); });
        }

        /// <summary>Ported from EditorBrushPanel.BuildSlider: standard Background/Fill/HandleArea/Handle
        /// slider construction with the same theming so it matches the rest of the app.</summary>
        Slider BuildSlider(GameObject sliderGO, float defaultValue, float min, float max)
        {
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = defaultValue;

            var bg = new GameObject("Bg");
            bg.transform.SetParent(sliderGO.transform, false);
            var bgImg = bg.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.2f);
            fillRect.anchorMax = new Vector2(0f, 0.8f);
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRect = handleArea.AddComponent<RectTransform>();
            haRect.anchorMin = Vector2.zero;
            haRect.anchorMax = Vector2.one;
            haRect.sizeDelta = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImg = handle.AddComponent<Image>();
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(12f, 18f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

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
