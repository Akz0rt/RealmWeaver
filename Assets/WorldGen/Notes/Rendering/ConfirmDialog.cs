using System;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Shared modal dialogs (Screen F). Two shapes over one layout:
    ///   • Show — destructive confirm: danger icon plate, title + body, «Отмена» / «Удалить».
    ///   • ShowInfo — acknowledge/error: info icon plate, title + body, «Ок» (+ optional «Подробнее»).
    /// Both dim the screen with a click-swallowing backdrop that does NOT dismiss on outside-click —
    /// an explicit button is required. Only one dialog exists at a time (each call replaces the prior).
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject activeDialogGO;

        public static void Show(Font font, string title, string message, Action<bool> onResult)
        {
            var (_, footer) = BuildBase(font, title, message, danger: true);

            AddFooterButton(font, footer, "Отмена", 84f, ThemeRole.Elev, () =>
            {
                UnityEngine.Object.Destroy(activeDialogGO);
                onResult(false);
            });
            AddFooterButton(font, footer, "Удалить", 84f, ThemeRole.Danger, () =>
            {
                UnityEngine.Object.Destroy(activeDialogGO);
                onResult(true);
            });
        }

        /// <summary>Single-button acknowledgement for errors/warnings. The «Подробнее» secondary
        /// button is built but hidden unless onDetails is provided.</summary>
        public static void ShowInfo(Font font, string title, string message, Action onDismiss = null, Action onDetails = null)
        {
            var (_, footer) = BuildBase(font, title, message, danger: false);

            var detailsGO = AddFooterButton(font, footer, "Подробнее", 96f, ThemeRole.Elev, () => onDetails?.Invoke());
            detailsGO.SetActive(onDetails != null);

            AddFooterButton(font, footer, "Ок", 72f, ThemeRole.Accent, () =>
            {
                UnityEngine.Object.Destroy(activeDialogGO);
                onDismiss?.Invoke();
            });
        }

        // ── Construction ─────────────────────────────────────────────────────────

        static (GameObject panel, Transform footer) BuildBase(Font font, string title, string message, bool danger)
        {
            if (activeDialogGO != null) UnityEngine.Object.Destroy(activeDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeDialogGO = canvasGO;

            // Backdrop: opaque app-background fill that swallows clicks (Button with no listener),
            // never dismisses. Fully opaque per request — the modal reads on a solid background.
            var backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(canvasGO.transform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            ThemeService.Tag(backdropImg, ThemeRole.Bg);
            backdropImg.raycastTarget = true;
            backdropGO.AddComponent<Button>(); // no onClick → blocks without dismissing
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            // Dialog panel (centered, content-driven height).
            var panelGO = new GameObject("Dialog");
            panelGO.transform.SetParent(canvasGO.transform, false); // after backdrop → renders in front
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(388f, 0f);
            panelRect.anchoredPosition = Vector2.zero;

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            WorldGen.Rendering.UiShadow.Add(panelRect, spread: 32f, alpha: 0.5f);

            BuildHeader(font, panelGO.transform, title, danger);
            BuildBody(font, panelGO.transform, message);
            var footer = BuildFooter(panelGO.transform);
            return (panelGO, footer);
        }

        static void BuildHeader(Font font, Transform parent, string title, bool danger)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 34f;

            // Icon plate.
            var plateGO = new GameObject("IconPlate");
            plateGO.transform.SetParent(rowGO.transform, false);
            var plateImg = plateGO.AddComponent<Image>();
            if (danger) ThemeService.Tag(plateImg, ThemeRole.Danger, 0.2f);
            else ThemeService.Tag(plateImg, ThemeRole.AccentSoft);
            var plateRect = plateGO.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0f, 0.5f);
            plateRect.anchorMax = new Vector2(0f, 0.5f);
            plateRect.pivot = new Vector2(0f, 0.5f);
            plateRect.sizeDelta = new Vector2(34f, 34f);

            var glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(plateGO.transform, false);
            var glyph = glyphGO.AddComponent<Text>();
            glyph.text = danger ? "!" : "i";
            glyph.font = font;
            glyph.fontSize = 22;
            glyph.fontStyle = FontStyle.Bold;
            ThemeService.Tag(glyph, danger ? ThemeRole.Danger : ThemeRole.Accent);
            glyph.alignment = TextAnchor.MiddleCenter;
            var glyphRect = glyphGO.GetComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.sizeDelta = Vector2.zero;

            // Title.
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = font;
            titleText.fontSize = 15;
            titleText.fontStyle = FontStyle.Bold;
            ThemeService.Tag(titleText, ThemeRole.Txt);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(46f, 0f);
            titleRect.offsetMax = Vector2.zero;
        }

        static GameObject BuildBody(Font font, Transform parent, string message)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = message ?? "";
            text.font = font;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return go;
        }

        static Transform BuildFooter(Transform parent)
        {
            var rowGO = new GameObject("Footer");
            rowGO.transform.SetParent(parent, false);
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleRight;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 34f;
            return rowGO.transform;
        }

        static GameObject AddFooterButton(Font font, Transform parent, string label, float width, ThemeRole role, Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, role);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 32f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            // Gold accent → theme ink; red danger → white (its red is theme-independent); else normal text.
            if (role == ThemeRole.Accent) ThemeService.Tag(text, ThemeRole.AccentInk);
            else if (role == ThemeRole.Danger) text.color = Color.white;
            else ThemeService.Tag(text, ThemeRole.Txt);
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            return go;
        }
    }
}
