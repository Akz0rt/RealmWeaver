using UnityEngine;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Windowed vs fullscreen display mode, persisted in PlayerPrefs and applied at launch.
    /// The toggle lives in ProjectMenuBar's "Вид" menu. Fullscreen = borderless desktop-sized
    /// window (FullScreenWindow); windowed = a resizable window sized to ~80% of the display.
    ///
    /// Note: Screen.SetResolution only takes effect in a real player build — in the Editor the
    /// Game view is unaffected, so windowed mode is verified from the installed app, not Play mode.
    /// </summary>
    public static class DisplayModeService
    {
        const string Key = "display_windowed";

        public static bool IsWindowed => PlayerPrefs.GetInt(Key, 0) == 1;

        /// <summary>Applies the saved preference — call once at startup.</summary>
        public static void ApplySaved() => ApplyToScreen(IsWindowed);

        public static void SetWindowed(bool windowed)
        {
            PlayerPrefs.SetInt(Key, windowed ? 1 : 0);
            PlayerPrefs.Save();
            ApplyToScreen(windowed);
        }

        public static void Toggle() => SetWindowed(!IsWindowed);

        static void ApplyToScreen(bool windowed)
        {
            int sysW = Display.main.systemWidth;
            int sysH = Display.main.systemHeight;

            if (windowed)
            {
                int w = Mathf.Clamp(Mathf.RoundToInt(sysW * 0.8f), 1024, Mathf.Max(1024, sysW - 80));
                int h = Mathf.Clamp(Mathf.RoundToInt(sysH * 0.8f), 640, Mathf.Max(640, sysH - 80));
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            else
            {
                Screen.SetResolution(sysW, sysH, FullScreenMode.FullScreenWindow);
            }
        }
    }
}
