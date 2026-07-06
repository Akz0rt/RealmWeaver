using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Shown while WorldGenerator.GenerateWorldStepped runs. Self-contained -- add to the
    /// scene, no Inspector wiring needed beyond what MapScreenController assigns at runtime.
    /// </summary>
    public class GenerationProgressUI : MonoBehaviour
    {
        public event Action OnCancelRequested;

        static readonly string[] StepLabels =
        {
            "Генерация высот", "Океаны и озёра", "Температура и влажность",
            "Расчёт биомов", "Границы регионов"
        };

        Font builtinFont;
        Text stepLineLabel;
        Text percentLabel;
        Image progressFill;
        RectTransform progressFillRect;
        readonly List<Text> checklistLabels = new List<Text>();
        readonly List<Image> checklistDots = new List<Image>();
        int currentStepIndex = -1;

        RectTransform cardRect;
        RectTransform contentRect;
        float cardTargetWidth;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("GenerationProgressCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
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

            // Width is capped to the screen so the card never overhangs a narrow window; height
            // is derived from actual content in ApplyCardHeight below, with a ScrollRect as the
            // fallback for screens too short to show it all -- see GenerationScreenUI.cs for the
            // same treatment (a fixed-size card there overflowed off-screen at some resolutions).
            cardTargetWidth = Mathf.Min(560f, Screen.width - 40f);

            var cardGO = new GameObject("ProgressCard");
            cardGO.transform.SetParent(canvasTransform, false);
            var cardImg = cardGO.AddComponent<Image>();
            ThemeService.Tag(cardImg, ThemeRole.Panel);
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
            layout.padding = new RectOffset(28, 28, 28, 28);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(contentGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Создание мира…";
            title.font = builtinFont;
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;
            ThemeService.Tag(title, ThemeRole.Txt);
            titleGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            var stepLineGO = new GameObject("StepLine");
            stepLineGO.transform.SetParent(contentGO.transform, false);
            stepLineGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            var stepLineLayout = stepLineGO.AddComponent<HorizontalLayoutGroup>();
            stepLineLayout.childControlWidth = true;

            var stepGO = new GameObject("StepLabel");
            stepGO.transform.SetParent(stepLineGO.transform, false);
            stepGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            stepLineLabel = stepGO.AddComponent<Text>();
            stepLineLabel.font = builtinFont;
            stepLineLabel.fontSize = 13;
            ThemeService.Tag(stepLineLabel, ThemeRole.Txt);

            var pctGO = new GameObject("Percent");
            pctGO.transform.SetParent(stepLineGO.transform, false);
            pctGO.AddComponent<LayoutElement>().preferredWidth = 50f;
            percentLabel = pctGO.AddComponent<Text>();
            percentLabel.font = builtinFont;
            percentLabel.fontSize = 13;
            percentLabel.fontStyle = FontStyle.Bold;
            percentLabel.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(percentLabel, ThemeRole.Accent);

            BuildProgressBar(contentGO.transform);
            BuildChecklist(contentGO.transform);
            BuildCancelButton(contentGO.transform);

            ApplyCardHeight();
        }

        void ApplyCardHeight()
        {
            Canvas.ForceUpdateCanvases();
            float contentHeight = LayoutUtility.GetPreferredHeight(contentRect);
            const float screenMargin = 40f; // breathing room from top/bottom screen edge
            float maxHeight = Screen.height - screenMargin * 2f;
            cardRect.sizeDelta = new Vector2(cardTargetWidth, Mathf.Min(contentHeight, maxHeight));
        }

        void BuildProgressBar(Transform parent)
        {
            var trackGO = new GameObject("ProgressTrack");
            trackGO.transform.SetParent(parent, false);
            trackGO.AddComponent<LayoutElement>().preferredHeight = 8f;
            var trackImg = trackGO.AddComponent<Image>();
            ThemeService.Tag(trackImg, ThemeRole.Elev);

            var fillGO = new GameObject("ProgressFill");
            fillGO.transform.SetParent(trackGO.transform, false);
            progressFill = fillGO.AddComponent<Image>();
            ThemeService.Tag(progressFill, ThemeRole.Accent);
            progressFillRect = fillGO.GetComponent<RectTransform>();
            progressFillRect.anchorMin = new Vector2(0f, 0f);
            progressFillRect.anchorMax = new Vector2(0f, 1f);
            progressFillRect.sizeDelta = Vector2.zero;
            progressFillRect.pivot = new Vector2(0f, 0.5f);
        }

        void BuildChecklist(Transform parent)
        {
            var listGO = new GameObject("Checklist");
            listGO.transform.SetParent(parent, false);
            var listLayout = listGO.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandWidth = true;
            listGO.AddComponent<LayoutElement>().preferredHeight = StepLabels.Length * 26f;

            foreach (var label in StepLabels)
            {
                var rowGO = new GameObject($"Step_{label}");
                rowGO.transform.SetParent(listGO.transform, false);
                rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
                var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childControlWidth = false;

                var dotGO = new GameObject("Dot");
                dotGO.transform.SetParent(rowGO.transform, false);
                dotGO.AddComponent<LayoutElement>().preferredWidth = 16f;
                var dotImg = dotGO.AddComponent<Image>();
                ThemeService.Tag(dotImg, ThemeRole.Border);
                checklistDots.Add(dotImg);

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(rowGO.transform, false);
                textGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = builtinFont;
                text.fontSize = 12;
                ThemeService.Tag(text, ThemeRole.Mut);
                checklistLabels.Add(text);
            }
        }

        void BuildCancelButton(Transform parent)
        {
            var btnGO = new GameObject("CancelButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnCancelRequested?.Invoke());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "Отмена";
            text.font = builtinFont;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        /// <summary>Called by MapScreenController's onProgress callback.</summary>
        public void SetStep(string label, float fraction)
        {
            stepLineLabel.text = label;
            percentLabel.text = $"{Mathf.RoundToInt(fraction * 100f)}%";
            progressFillRect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);

            int stepIndex = Array.IndexOf(StepLabels, label);
            if (stepIndex < 0) return; // "Готово" or unrecognized label -- leave checklist as-is (all done)
            currentStepIndex = stepIndex;

            for (int i = 0; i < checklistLabels.Count; i++)
            {
                if (i < currentStepIndex)
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Accent);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Txt);
                }
                else if (i == currentStepIndex)
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Accent);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Txt);
                }
                else
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Border);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Mut);
                }
            }
        }
    }
}
