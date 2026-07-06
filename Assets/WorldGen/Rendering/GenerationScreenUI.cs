using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    public enum MapSizePreset { Small, Medium, Large }
    public enum LandShapePreset { Continent, Archipelago, Islands }

    /// <summary>
    /// Empty-state screen shown when WorldMapRenderer.Cells == null. Collects seed/size/
    /// land-shape/region-detail, then hands off to MapScreenController.StartGeneration.
    /// Laid out to match design_handoff_realmweaver_ui screens/*/02-generate.png (560px card,
    /// icon-plate header, description, tight field sections). Self-contained -- add to the scene,
    /// assign `controller` and `projectMenuBar` in the Inspector.
    /// </summary>
    public class GenerationScreenUI : MonoBehaviour
    {
        public MapScreenController controller;
        public ProjectMenuBar projectMenuBar;

        const int MinRegions = 4;
        const int MaxRegions = 40;
        const int DefaultRegions = 24;

        Font builtinFont;
        InputField seedField;
        MapSizePreset selectedSize = MapSizePreset.Medium;
        LandShapePreset selectedShape = LandShapePreset.Continent;
        int selectedRegions = DefaultRegions;

        Button[] sizeButtons = new Button[3];
        Button[] shapeButtons = new Button[3];
        Text regionsValueLabel;

        RectTransform cardRect;
        RectTransform contentRect;
        float cardTargetWidth;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        public static int StableSeedHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        static string RandomSeedString()
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            var rng = new System.Random();
            var chars = new char[8];
            for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
            return new string(chars) + "-" + rng.Next(1000, 9999);
        }

        // ── Build ────────────────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("GenerationScreenCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // below ProjectMenuBar's popups (100+)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var bgGO = new GameObject("Backdrop");
            bgGO.transform.SetParent(canvasTransform, false);
            var bgImg = bgGO.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Bg);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            cardTargetWidth = Mathf.Min(560f, Screen.width - 40f);

            var cardGO = new GameObject("GenerationCard");
            cardGO.transform.SetParent(canvasTransform, false);
            var cardImg = cardGO.AddComponent<Image>();
            ThemeService.Tag(cardImg, ThemeRole.Panel);
            AddBorder(cardGO, ThemeRole.Border);
            cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(cardTargetWidth, 0f);
            cardRect.anchoredPosition = Vector2.zero;

            var scrollRect = cardGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(cardGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 14f;                    // gap between sections
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            var t = contentGO.transform;
            BuildHeader(t);
            AddParagraph(t, "Процедурная полигональная генерация: высоты, биомы, климат, сезоны и регионы. " +
                            "Настройте параметры или начните со случайного сида.", 40f);

            BuildSeedRow(BuildField(t, "СИД"));
            BuildSizeSegment(BuildField(t, "РАЗМЕР КАРТЫ"));
            BuildShapeSegment(BuildField(t, "ФОРМА СУШИ"));
            BuildRegionsField(t);

            BuildGenerateButton(t);
            AddOrSeparator(t);
            BuildOpenProjectButton(t);

            ApplyCardHeight();
        }

        void ApplyCardHeight()
        {
            Canvas.ForceUpdateCanvases();
            float contentHeight = LayoutUtility.GetPreferredHeight(contentRect);
            const float screenMargin = 40f;
            float maxHeight = Screen.height - screenMargin * 2f;
            cardRect.sizeDelta = new Vector2(cardTargetWidth, Mathf.Min(contentHeight, maxHeight));
        }

        // ── Header ────────────────────────────────────────────────────────────────

        void BuildHeader(Transform parent)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 42f;
            var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childControlWidth = true;
            hl.childForceExpandWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;

            // Icon plate (40x40, accent-soft with a border and an accent glyph)
            var iconGO = new GameObject("IconPlate");
            iconGO.transform.SetParent(rowGO.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            ThemeService.Tag(iconImg, ThemeRole.AccentSoft);
            AddBorder(iconGO, ThemeRole.Accent);
            var iconLE = iconGO.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 40f;
            iconLE.preferredHeight = 40f;
            iconLE.flexibleWidth = 0f;
            var glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(iconGO.transform, false);
            var glyph = glyphGO.AddComponent<Text>();
            glyph.text = "✦";
            glyph.font = builtinFont;
            glyph.fontSize = 20;
            glyph.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(glyph, ThemeRole.Accent);
            Stretch(glyphGO);

            // Title + subtitle column
            var colGO = new GameObject("HeaderText");
            colGO.transform.SetParent(rowGO.transform, false);
            var colLE = colGO.AddComponent<LayoutElement>();
            colLE.flexibleWidth = 1f;
            var cvl = colGO.AddComponent<VerticalLayoutGroup>();
            cvl.spacing = 2f;
            cvl.childControlWidth = true;
            cvl.childForceExpandWidth = true;
            cvl.childControlHeight = true;
            cvl.childForceExpandHeight = false;
            cvl.childAlignment = TextAnchor.MiddleLeft;
            AddLabel(colGO.transform, "Создать карту мира", 18, bold: true, role: ThemeRole.Txt, height: 22f);
            AddLabel(colGO.transform, "Карта ещё не сгенерирована", 12, bold: false, role: ThemeRole.Mut, height: 16f);
        }

        // ── Field grouping ─────────────────────────────────────────────────────────

        /// <summary>Creates a section container with a tight (6px) label→control gap and returns
        /// the container transform; the caller adds the control after the already-added label.</summary>
        Transform BuildField(Transform parent, string labelText)
        {
            var fieldGO = new GameObject(labelText + "Field");
            fieldGO.transform.SetParent(parent, false);
            var vl = fieldGO.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6f;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandHeight = false;
            AddFieldLabel(fieldGO.transform, labelText);
            return fieldGO.transform;
        }

        // ── Labels ────────────────────────────────────────────────────────────────

        void AddLabel(Transform parent, string text, int fontSize, bool bold, ThemeRole role, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = builtinFont;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(t, role);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        void AddFieldLabel(Transform parent, string text) =>
            AddLabel(parent, text, 11, bold: true, role: ThemeRole.Mut, height: 14f);

        void AddParagraph(Transform parent, string text, float height)
        {
            var go = new GameObject("Paragraph");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = builtinFont;
            t.fontSize = 12;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            ThemeService.Tag(t, ThemeRole.Mut);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        void AddOrSeparator(Transform parent)
        {
            var go = new GameObject("OrSeparator");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = "или";
            t.font = builtinFont;
            t.fontSize = 11;
            t.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(t, ThemeRole.Mut);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
        }

        // ── Seed ──────────────────────────────────────────────────────────────────

        void BuildSeedRow(Transform parent)
        {
            var rowGO = new GameObject("SeedRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 38f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            var fieldGO = new GameObject("SeedField");
            fieldGO.transform.SetParent(rowGO.transform, false);
            var fieldImg = fieldGO.AddComponent<Image>();
            ThemeService.Tag(fieldImg, ThemeRole.Elev);
            AddBorder(fieldGO, ThemeRole.Border);
            fieldGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            seedField = fieldGO.AddComponent<InputField>();
            var seedTextGO = new GameObject("Text");
            seedTextGO.transform.SetParent(fieldGO.transform, false);
            var seedText = seedTextGO.AddComponent<Text>();
            seedText.font = builtinFont;
            seedText.fontSize = 13;
            seedText.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(seedText, ThemeRole.Txt);
            var seedTextRect = seedTextGO.GetComponent<RectTransform>();
            seedTextRect.anchorMin = Vector2.zero;
            seedTextRect.anchorMax = Vector2.one;
            seedTextRect.offsetMin = new Vector2(12f, 4f);
            seedTextRect.offsetMax = new Vector2(-12f, -4f);
            seedField.textComponent = seedText;
            seedField.text = RandomSeedString();

            var randomBtnGO = new GameObject("RandomButton");
            randomBtnGO.transform.SetParent(rowGO.transform, false);
            var randomBtnImg = randomBtnGO.AddComponent<Image>();
            ThemeService.Tag(randomBtnImg, ThemeRole.Elev);
            AddBorder(randomBtnGO, ThemeRole.Border);
            randomBtnGO.AddComponent<LayoutElement>().preferredWidth = 116f;
            var randomBtn = randomBtnGO.AddComponent<Button>();
            randomBtn.targetGraphic = randomBtnImg;
            randomBtn.onClick.AddListener(() => seedField.text = RandomSeedString());
            var randomTextGO = new GameObject("Text");
            randomTextGO.transform.SetParent(randomBtnGO.transform, false);
            var randomText = randomTextGO.AddComponent<Text>();
            randomText.text = "↻ Случайно";
            randomText.font = builtinFont;
            randomText.fontSize = 12;
            randomText.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(randomText, ThemeRole.Txt);
            Stretch(randomTextGO);
        }

        // ── Segments ────────────────────────────────────────────────────────────────

        void BuildSizeSegment(Transform parent)
        {
            string[] labels = { "Малый", "Средний", "Большой" };
            BuildSegmentRow(parent, "SizeSegment", labels, sizeButtons, 1, softActive: false);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                sizeButtons[i].onClick.AddListener(() => { selectedSize = (MapSizePreset)captured; RefreshSegmentColors(sizeButtons, captured, softActive: false); });
            }
        }

        void BuildShapeSegment(Transform parent)
        {
            string[] labels = { "Материк", "Архипелаг", "Острова" };
            BuildSegmentRow(parent, "ShapeSegment", labels, shapeButtons, 0, softActive: true);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                shapeButtons[i].onClick.AddListener(() => { selectedShape = (LandShapePreset)captured; RefreshSegmentColors(shapeButtons, captured, softActive: true); });
            }
        }

        void BuildSegmentRow(Transform parent, string name, string[] labels, Button[] buttons, int defaultIndex, bool softActive)
        {
            var rowGO = new GameObject(name);
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            for (int i = 0; i < labels.Length; i++)
            {
                var btnGO = new GameObject($"Segment_{labels[i]}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                var outline = btnGO.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1f, -1f);
                buttons[i] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = labels[i];
                text.font = builtinFont;
                text.fontSize = 13;
                text.alignment = TextAnchor.MiddleCenter;
                Stretch(textGO);
            }
            RefreshSegmentColors(buttons, defaultIndex, softActive);
        }

        void RefreshSegmentColors(Button[] buttons, int activeIndex, bool softActive)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool active = i == activeIndex;
                var img = buttons[i].targetGraphic as Image;
                var text = buttons[i].GetComponentInChildren<Text>();
                var outline = buttons[i].GetComponent<Outline>();

                if (active && softActive)
                {
                    // Shape "Материк": accent-soft fill + accent border + accent text
                    ThemeService.Tag(img, ThemeRole.AccentSoft);
                    ThemeService.Tag(text, ThemeRole.Accent);
                    outline.effectColor = ThemeService.Get(ThemeRole.Accent);
                }
                else if (active)
                {
                    // Size "Средний": solid accent + accent-ink text, no border
                    ThemeService.Tag(img, ThemeRole.Accent);
                    ThemeService.Tag(text, ThemeRole.AccentInk);
                    outline.effectColor = new Color(0f, 0f, 0f, 0f);
                }
                else
                {
                    ThemeService.Tag(img, ThemeRole.Elev);
                    ThemeService.Tag(text, ThemeRole.Txt);
                    outline.effectColor = ThemeService.Get(ThemeRole.Border);
                }
            }
        }

        // ── Region-detail slider (label + value on one row, slider below) ────────────

        void BuildRegionsField(Transform parent)
        {
            var fieldGO = new GameObject("RegionsField");
            fieldGO.transform.SetParent(parent, false);
            var vl = fieldGO.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6f;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandHeight = false;

            // Label row: "ДЕТАЛИЗАЦИЯ · РЕГИОНОВ" left, value right
            var labelRowGO = new GameObject("LabelRow");
            labelRowGO.transform.SetParent(fieldGO.transform, false);
            labelRowGO.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lrl = labelRowGO.AddComponent<HorizontalLayoutGroup>();
            lrl.childControlWidth = true;
            lrl.childForceExpandWidth = true;
            lrl.childControlHeight = true;
            lrl.childForceExpandHeight = true;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(labelRowGO.transform, false);
            var label = labelGO.AddComponent<Text>();
            label.text = "ДЕТАЛИЗАЦИЯ · РЕГИОНОВ";
            label.font = builtinFont;
            label.fontSize = 11;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(label, ThemeRole.Mut);
            labelGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(labelRowGO.transform, false);
            regionsValueLabel = valueGO.AddComponent<Text>();
            regionsValueLabel.text = DefaultRegions.ToString();
            regionsValueLabel.font = builtinFont;
            regionsValueLabel.fontSize = 12;
            regionsValueLabel.fontStyle = FontStyle.Bold;
            regionsValueLabel.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(regionsValueLabel, ThemeRole.Accent);
            valueGO.AddComponent<LayoutElement>().preferredWidth = 36f;

            // Slider
            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(fieldGO.transform, false);
            sliderGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = MinRegions;
            slider.maxValue = MaxRegions;
            slider.wholeNumbers = true;
            slider.value = DefaultRegions;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgImg = bgGO.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.35f);
            bgRect.anchorMax = new Vector2(1f, 0.65f);
            bgRect.sizeDelta = Vector2.zero;
            slider.targetGraphic = bgImg;

            var fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillAreaGO.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRect.sizeDelta = Vector2.zero;
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            var handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.sizeDelta = Vector2.zero;
            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            var handleImg = handleGO.AddComponent<Image>();
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(14f, 14f);
            slider.handleRect = handleRect;

            slider.onValueChanged.AddListener(v =>
            {
                selectedRegions = Mathf.RoundToInt(v);
                regionsValueLabel.text = selectedRegions.ToString();
            });
        }

        // ── Action buttons ──────────────────────────────────────────────────────────

        void BuildGenerateButton(Transform parent)
        {
            var btnGO = new GameObject("GenerateButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 48f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnGenerateClicked);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "✦ Сгенерировать карту";
            text.font = builtinFont;
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.AccentInk);
            Stretch(textGO);
        }

        void BuildOpenProjectButton(Transform parent)
        {
            var btnGO = new GameObject("OpenProjectButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            AddBorder(btnGO, ThemeRole.Border);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => projectMenuBar?.TriggerOpenFromExternal());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "Открыть проект…";
            text.font = builtinFont;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            Stretch(textGO);
        }

        // ── Small helpers ─────────────────────────────────────────────────────────

        static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Adds a 1px outline in the given theme role (uGUI's cheap border stand-in).</summary>
        void AddBorder(GameObject go, ThemeRole role)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(role);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        void OnGenerateClicked()
        {
            var p = new GenerationRequest(seedField.text, selectedSize, selectedShape, selectedRegions);
            controller?.StartGeneration(p);
        }
    }

    /// <summary>
    /// Plain data the Generation screen hands to MapScreenController.StartGeneration.
    /// Named GenerationRequest (not GenerationParams) to avoid colliding with
    /// WorldGen.Generation.GenerationParams: both WorldMapRenderer.cs and this file live in
    /// namespace WorldGen.Rendering, and a type declared directly in that namespace always
    /// wins over one pulled in by "using WorldGen.Generation;" -- an unqualified GenerationParams
    /// here would silently shadow the real one project-wide and break WorldMapRenderer.cs.
    /// </summary>
    public class GenerationRequest
    {
        public readonly string SeedText;
        public readonly MapSizePreset Size;
        public readonly LandShapePreset Shape;
        public readonly int RegionCount;

        public GenerationRequest(string seedText, MapSizePreset size, LandShapePreset shape, int regionCount)
        {
            SeedText = seedText;
            Size = size;
            Shape = shape;
            RegionCount = regionCount;
        }
    }
}
