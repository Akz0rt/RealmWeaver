using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;
using ThemeMode = WorldGen.Rendering.Theme.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Drop shadow for floating uGUI cards/panels/dialogs, used to lift them off the background
    /// and stand in for a border. Implemented with the built-in <see cref="Shadow"/> mesh effect
    /// on the panel's own graphic — so the shadow always tracks the panel's rect exactly, with no
    /// separate follower object to drift or mis-size. Theme-aware: a dark shadow in the light
    /// theme, a light glow in the dark theme (a hard shadow reads as dirt on a dark background).
    /// </summary>
    public static class UiShadow
    {
        // spread/alpha kept for call-site compatibility; the themed component sets its own values.
        public static void Add(RectTransform panel, float spread = 22f, float alpha = 0.4f)
        {
            if (panel == null) return;
            if (panel.GetComponent<UiThemedShadow>() == null)
                panel.gameObject.AddComponent<UiThemedShadow>();
        }

        public static void Add(RectTransform panel, float spread, Vector2 offset, float alpha) => Add(panel, spread, alpha);
    }

    /// <summary>Keeps a <see cref="Shadow"/> effect on this graphic coloured for the current theme,
    /// updating when the theme is toggled (polled — ThemeService has no change event).</summary>
    [RequireComponent(typeof(Graphic))]
    public class UiThemedShadow : MonoBehaviour
    {
        Shadow shadow;
        ThemeMode lastTheme;
        bool applied;

        void OnEnable()
        {
            if (shadow == null) shadow = GetComponent<Shadow>();
            if (shadow == null) shadow = gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(4f, -5f);
            shadow.useGraphicAlpha = false;
            applied = false;
            Apply();
        }

        void Update()
        {
            if (!applied || ThemeService.Current != lastTheme) Apply();
        }

        void Apply()
        {
            if (shadow == null) return;
            lastTheme = ThemeService.Current;
            applied = true;
            // Light theme → soft dark shadow; dark theme → subtle light glow.
            shadow.effectColor = ThemeService.Current == ThemeMode.Light
                ? new Color(0f, 0f, 0f, 0.32f)
                : new Color(1f, 1f, 1f, 0.14f);
        }
    }
}
