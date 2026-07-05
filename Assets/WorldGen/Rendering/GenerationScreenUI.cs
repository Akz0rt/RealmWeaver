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
    /// Self-contained -- add to the scene, assign `controller` and `projectMenuBar` in the Inspector.
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

        void BuildUI()
        {
            var canvasGO = new GameObject("GenerationScreenCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // below ProjectMenuBar's popups (100+), above nothing else needed
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

            var cardGO = new GameObject("GenerationCard");
            cardGO.transform.SetParent(canvasTransform, false);
            var cardImg = cardGO.AddComponent<Image>();
            ThemeService.Tag(cardImg, ThemeRole.Panel);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(560f, 520f);
            cardRect.anchoredPosition = Vector2.zero;

            var layout = cardGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 28, 28);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            AddLabel(cardGO.transform, "Создать карту мира", 20, bold: true, role: ThemeRole.Txt, height: 26f);
            AddLabel(cardGO.transform, "Карта ещё не сгенерирована", 12, bold: false, role: ThemeRole.Mut, height: 18f);

            AddFieldLabel(cardGO.transform, "СИД");
            BuildSeedRow(cardGO.transform);

            AddFieldLabel(cardGO.transform, "РАЗМЕР КАРТЫ");
            BuildSizeSegment(cardGO.transform);

            AddFieldLabel(cardGO.transform, "ФОРМА СУШИ");
            BuildShapeSegment(cardGO.transform);

            AddFieldLabel(cardGO.transform, "ДЕТАЛИЗАЦИЯ · РЕГИОНОВ");
            BuildRegionsSlider(cardGO.transform);

            BuildGenerateButton(cardGO.transform);
            BuildOpenProjectButton(cardGO.transform);
        }

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

        void AddFieldLabel(Transform parent, string text) => AddLabel(parent, text, 11, bold: true, role: ThemeRole.Mut, height: 16f);

        void BuildSeedRow(Transform parent)
        {
            var rowGO = new GameObject("SeedRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 38f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;

            var fieldGO = new GameObject("SeedField");
            fieldGO.transform.SetParent(rowGO.transform, false);
            var fieldImg = fieldGO.AddComponent<Image>();
            ThemeService.Tag(fieldImg, ThemeRole.Elev);
            fieldGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            seedField = fieldGO.AddComponent<InputField>();
            var seedTextGO = new GameObject("Text");
            seedTextGO.transform.SetParent(fieldGO.transform, false);
            var seedText = seedTextGO.AddComponent<Text>();
            seedText.font = builtinFont;
            seedText.fontSize = 12;
            seedText.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(seedText, ThemeRole.Txt);
            var seedTextRect = seedTextGO.GetComponent<RectTransform>();
            seedTextRect.anchorMin = Vector2.zero;
            seedTextRect.anchorMax = Vector2.one;
            seedTextRect.offsetMin = new Vector2(10f, 4f);
            seedTextRect.offsetMax = new Vector2(-10f, -4f);
            seedField.textComponent = seedText;
            seedField.text = RandomSeedString();

            var randomBtnGO = new GameObject("RandomButton");
            randomBtnGO.transform.SetParent(rowGO.transform, false);
            var randomBtnImg = randomBtnGO.AddComponent<Image>();
            ThemeService.Tag(randomBtnImg, ThemeRole.Elev);
            randomBtnGO.AddComponent<LayoutElement>().preferredWidth = 110f;
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
            var randomTextRect = randomTextGO.GetComponent<RectTransform>();
            randomTextRect.anchorMin = Vector2.zero;
            randomTextRect.anchorMax = Vector2.one;
            randomTextRect.sizeDelta = Vector2.zero;
        }

        void BuildSizeSegment(Transform parent)
        {
            string[] labels = { "Малый", "Средний", "Большой" };
            var rowGO = BuildSegmentRow(parent, "SizeSegment", labels, sizeButtons, 0);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                sizeButtons[i].onClick.AddListener(() => { selectedSize = (MapSizePreset)captured; RefreshSegmentColors(sizeButtons, captured); });
            }
        }

        void BuildShapeSegment(Transform parent)
        {
            string[] labels = { "Материк", "Архипелаг", "Острова" };
            BuildSegmentRow(parent, "ShapeSegment", labels, shapeButtons, 0);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                shapeButtons[i].onClick.AddListener(() => { selectedShape = (LandShapePreset)captured; RefreshSegmentColors(shapeButtons, captured); });
            }
        }

        GameObject BuildSegmentRow(Transform parent, string name, string[] labels, Button[] buttons, int defaultIndex)
        {
            var rowGO = new GameObject(name);
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 38f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;

            for (int i = 0; i < labels.Length; i++)
            {
                var btnGO = new GameObject($"Segment_{labels[i]}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                buttons[i] = btn;

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
                ThemeService.Tag(text, i == defaultIndex ? ThemeRole.AccentInk : ThemeRole.Txt);
                ThemeService.Tag(img, i == defaultIndex ? ThemeRole.Accent : ThemeRole.Elev);
            }

            return rowGO;
        }

        void RefreshSegmentColors(Button[] buttons, int activeIndex)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].targetGraphic as Image;
                var text = buttons[i].GetComponentInChildren<Text>();
                ThemeService.Tag(img, i == activeIndex ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(text, i == activeIndex ? ThemeRole.AccentInk : ThemeRole.Txt);
            }
        }

        void BuildRegionsSlider(Transform parent)
        {
            var rowGO = new GameObject("RegionsRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
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
            bgRect.anchorMin = new Vector2(0f, 0.4f);
            bgRect.anchorMax = new Vector2(1f, 0.6f);
            bgRect.sizeDelta = Vector2.zero;
            slider.targetGraphic = bgImg;

            var fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.4f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.6f);
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            regionsValueLabel = null;
            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            valueGO.AddComponent<LayoutElement>().preferredWidth = 32f;
            regionsValueLabel = valueGO.AddComponent<Text>();
            regionsValueLabel.font = builtinFont;
            regionsValueLabel.fontSize = 12;
            regionsValueLabel.fontStyle = FontStyle.Bold;
            regionsValueLabel.alignment = TextAnchor.MiddleRight;
            regionsValueLabel.text = DefaultRegions.ToString();
            ThemeService.Tag(regionsValueLabel, ThemeRole.Accent);

            slider.onValueChanged.AddListener(v =>
            {
                selectedRegions = Mathf.RoundToInt(v);
                regionsValueLabel.text = selectedRegions.ToString();
            });
        }

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
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void BuildOpenProjectButton(Transform parent)
        {
            var btnGO = new GameObject("OpenProjectButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
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
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void OnGenerateClicked()
        {
            var p = new GenerationParams(seedField.text, selectedSize, selectedShape, selectedRegions);
            controller?.StartGeneration(p);
        }
    }

    /// <summary>Plain data the Generation screen hands to MapScreenController.StartGeneration.</summary>
    public class GenerationParams
    {
        public readonly string SeedText;
        public readonly MapSizePreset Size;
        public readonly LandShapePreset Shape;
        public readonly int RegionCount;

        public GenerationParams(string seedText, MapSizePreset size, LandShapePreset shape, int regionCount)
        {
            SeedText = seedText;
            Size = size;
            Shape = shape;
            RegionCount = regionCount;
        }
    }
}
