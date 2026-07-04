using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Shared modal dialogs, extracted from NotesUndoManager so canvas-object deletion and
    /// sidebar group/page deletion reuse the same UI instead of duplicating it. Only one
    /// dialog is ever shown at once (Show/ShowInfo both replace the previous one).
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject activeDialogGO;

        public static void Show(Font font, string message, System.Action<bool> onResult)
        {
            var panelGO = BuildBasePanel(font, message);

            AddDialogButton(font, panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(false);
            });
            AddDialogButton(font, panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), new Color(0.55f, 0.15f, 0.15f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(true);
            });
        }

        /// <summary>Single-button acknowledgement dialog, for errors/warnings that need no
        /// yes/no choice (e.g. project load failures).</summary>
        public static void ShowInfo(Font font, string message, System.Action onDismiss = null)
        {
            var panelGO = BuildBasePanel(font, message);

            AddDialogButton(font, panelGO.transform, "OK", new Vector2(0.25f, 0.1f), new Vector2(0.75f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Object.Destroy(activeDialogGO);
                onDismiss?.Invoke();
            });
        }

        static GameObject BuildBasePanel(Font font, string message)
        {
            if (activeDialogGO != null) Object.Destroy(activeDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeDialogGO = canvasGO;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.7f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(300f, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            var msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = font;
            msgText.fontSize = 13;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0.4f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.sizeDelta = Vector2.zero;

            return panelGO;
        }

        static void AddDialogButton(Font font, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }
    }
}
