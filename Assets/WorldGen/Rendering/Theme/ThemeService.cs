using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Rendering.Theme
{
    public enum ThemeRole
    {
        Bg, Panel, Panel2, Elev, Border, Txt, Mut, Accent, AccentInk, AccentSoft,
        MapOcean, MapLand, MapCoast, Dot, Danger
    }

    public enum Theme { Dark, Light }

    /// <summary>
    /// Global Dark/Light theme. Every runtime-built Image/Text that should repaint on a
    /// theme switch calls ThemeService.Tag(graphic, role) once, right after construction --
    /// no prefabs, matches this project's existing runtime-UI convention.
    /// </summary>
    public static class ThemeService
    {
        const string PrefsKey = "Theme.Current";

        public static Theme Current { get; private set; } = Theme.Dark;

        static readonly List<ThemedGraphic> registered = new List<ThemedGraphic>();
        static bool loadedFromPrefs;

        static readonly Dictionary<ThemeRole, Color> Dark = new Dictionary<ThemeRole, Color>
        {
            { ThemeRole.Bg,         Hex("#141419") },
            { ThemeRole.Panel,      Hex("#1C1C22") },
            { ThemeRole.Panel2,     Hex("#23232B") },
            { ThemeRole.Elev,       Hex("#2B2B34") },
            { ThemeRole.Border,     Hex("#34343F") },
            { ThemeRole.Txt,        Hex("#E9E9EE") },
            { ThemeRole.Mut,        Hex("#8E929E") },
            { ThemeRole.Accent,     Hex("#C9A24B") },
            { ThemeRole.AccentInk,  Hex("#1A1710") },
            { ThemeRole.AccentSoft, Hex("#2B2617") },
            { ThemeRole.MapOcean,   Hex("#122A40") },
            { ThemeRole.MapLand,    Hex("#26352A") },
            { ThemeRole.MapCoast,   Hex("#3C5A44") },
            { ThemeRole.Dot,        Hex("#2A2A33") },
            { ThemeRole.Danger,     Hex("#C9605A") },
        };

        static readonly Dictionary<ThemeRole, Color> Light = new Dictionary<ThemeRole, Color>
        {
            { ThemeRole.Bg,         Hex("#E7E1D3") },
            { ThemeRole.Panel,      Hex("#F4F0E7") },
            { ThemeRole.Panel2,     Hex("#FBF8F1") },
            { ThemeRole.Elev,       Hex("#FFFFFF") },
            { ThemeRole.Border,     Hex("#D5CCB8") },
            { ThemeRole.Txt,        Hex("#2B2822") },
            { ThemeRole.Mut,        Hex("#736A59") },
            { ThemeRole.Accent,     Hex("#4E4E93") },
            { ThemeRole.AccentInk,  Hex("#FFFFFF") },
            { ThemeRole.AccentSoft, Hex("#E4E2F1") },
            { ThemeRole.MapOcean,   Hex("#BFD0D8") },
            { ThemeRole.MapLand,    Hex("#D6DBC2") },
            { ThemeRole.MapCoast,   Hex("#A9B58C") },
            { ThemeRole.Dot,        Hex("#D3CCBB") },
            { ThemeRole.Danger,     Hex("#C9605A") },
        };

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Dictionary<ThemeRole, Color> Palette => Current == Theme.Dark ? Dark : Light;

        static void EnsureLoaded()
        {
            if (loadedFromPrefs) return;
            loadedFromPrefs = true;
            Current = PlayerPrefs.GetInt(PrefsKey, 0) == 1 ? Theme.Light : Theme.Dark;
        }

        public static Color Get(ThemeRole role)
        {
            EnsureLoaded();
            return Palette[role];
        }

        public static void ApplyTheme(Theme theme)
        {
            EnsureLoaded();
            Current = theme;
            PlayerPrefs.SetInt(PrefsKey, theme == Theme.Light ? 1 : 0);

            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var tg = registered[i];
                if (tg == null) { registered.RemoveAt(i); continue; }
                tg.Repaint();
            }
        }

        public static void Tag(Graphic graphic, ThemeRole role, float? alphaOverride = null)
        {
            EnsureLoaded();
            var tg = graphic.GetComponent<ThemedGraphic>();
            if (tg == null) tg = graphic.gameObject.AddComponent<ThemedGraphic>();
            tg.Configure(graphic, role, alphaOverride);
            Register(tg);
            tg.Repaint();
        }

        internal static Color Resolve(ThemeRole role, float? alphaOverride)
        {
            var c = Get(role);
            if (alphaOverride.HasValue) c.a = alphaOverride.Value;
            return c;
        }

        static void Register(ThemedGraphic tg)
        {
            if (!registered.Contains(tg)) registered.Add(tg);
        }

        internal static void Unregister(ThemedGraphic tg)
        {
            registered.Remove(tg);
        }

#if UNITY_EDITOR
        /// <summary>Self-test, invoked via a temporary caller (see plan) -- not a MonoBehaviour context menu, since ThemeService is static.</summary>
        public static bool SelfTestApplyTheme(out string message)
        {
            var probeGO = new GameObject("ThemeSelfTestProbe");
            var img = probeGO.AddComponent<Image>();
            Tag(img, ThemeRole.Accent);

            ApplyTheme(Theme.Dark);
            bool darkOk = img.color == Dark[ThemeRole.Accent];

            ApplyTheme(Theme.Light);
            bool lightOk = img.color == Light[ThemeRole.Accent];

            Object.DestroyImmediate(probeGO);

            bool ok = darkOk && lightOk;
            message = ok ? "Self-Test Theme Apply: PASS" : $"Self-Test Theme Apply: FAIL (darkOk={darkOk}, lightOk={lightOk})";
            return ok;
        }
#endif
    }

    /// <summary>Marker placed on a themed Graphic by ThemeService.Tag(); repaints on ApplyTheme.</summary>
    public class ThemedGraphic : MonoBehaviour
    {
        Graphic graphic;
        ThemeRole role;
        float? alphaOverride;

        public void Configure(Graphic g, ThemeRole r, float? alpha)
        {
            graphic = g;
            role = r;
            alphaOverride = alpha;
        }

        public void Repaint()
        {
            if (graphic != null) graphic.color = ThemeService.Resolve(role, alphaOverride);
        }

        void OnDestroy()
        {
            ThemeService.Unregister(this);
        }
    }
}
