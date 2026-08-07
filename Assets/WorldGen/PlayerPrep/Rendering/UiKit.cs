using System;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Мелкие построители uGUI. Существует, чтобы четыре экрана игрока строили кнопки и
    /// подписи одинаково, а не каждый по-своему.</summary>
    public static class UiKit
    {
        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Text Label(Transform parent, string text, int size, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font; t.fontSize = size; t.text = text; t.alignment = anchor;
            t.color = new Color(0.92f, 0.90f, 0.86f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, bool enabled = true)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = enabled
                ? new Color(0.18f, 0.17f, 0.16f) : new Color(0.13f, 0.13f, 0.13f);
            var btn = go.GetComponent<Button>();
            btn.interactable = enabled;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var text = Label(go.transform, label, 20, TextAnchor.MiddleCenter);
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12f, 0f); rt.offsetMax = new Vector2(-12f, 0f);
            if (!enabled) text.color = new Color(0.5f, 0.48f, 0.46f);
            return btn;
        }
    }
}
