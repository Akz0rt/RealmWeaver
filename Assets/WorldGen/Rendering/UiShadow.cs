using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;
using ThemeMode = WorldGen.Rendering.Theme.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Soft drop shadow for floating uGUI cards/panels/dialogs — lifts them off the background and
    /// stands in for a border. Generates one 9-sliced soft-edge sprite at runtime (no external
    /// assets) and drops a shadow Image behind the panel as a sibling; UiShadowFollower keeps it
    /// matched to the panel's actual rect (robustly, via rect.center/size in the shared parent
    /// space — works for any anchor setup and tracks ContentSizeFitter resizes). Theme-aware: a
    /// dark shadow in the light theme, a light glow in the dark theme.
    /// </summary>
    public static class UiShadow
    {
        static Sprite softSprite;
        const int TexSize = 64;
        const int Border = 20;

        static Sprite GetSoftShadowSprite()
        {
            if (softSprite != null) return softSprite;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                name = "UiSoftShadow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[TexSize * TexSize];
            int innerMin = Border, innerMax = TexSize - 1 - Border;
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(innerMin - x, x - innerMax));
                    float dy = Mathf.Max(0, Mathf.Max(innerMin - y, y - innerMax));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.Clamp01(d / Border);
                    a *= a; // ease → softer
                    pixels[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply();

            softSprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
            return softSprite;
        }

        public static void Add(RectTransform panel, float spread = 20f, float alpha = 1f)
        {
            if (panel == null) return;

            var shadowGO = new GameObject("Shadow");
            shadowGO.transform.SetParent(panel.parent, false);
            shadowGO.transform.SetSiblingIndex(panel.GetSiblingIndex()); // before the panel → renders behind

            var img = shadowGO.AddComponent<Image>();
            img.sprite = GetSoftShadowSprite();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var follower = shadowGO.AddComponent<UiShadowFollower>();
            follower.Configure(panel, spread);
        }

        // Overload kept for older call sites that passed an offset.
        public static void Add(RectTransform panel, float spread, Vector2 offset, float alpha) => Add(panel, spread, alpha);
    }

    /// <summary>Keeps a shadow Image centred on its target panel and sized to the panel + spread,
    /// and coloured for the current theme. Robust to any anchor layout (uses rect.center/size in
    /// the shared parent's space) and tracks ContentSizeFitter-driven resizes each frame.</summary>
    public class UiShadowFollower : MonoBehaviour
    {
        RectTransform target;
        RectTransform self;
        Image image;
        float spread;
        ThemeMode lastTheme;
        bool colored;

        public void Configure(RectTransform target, float spread)
        {
            this.target = target;
            this.spread = spread;
            self = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            self.anchorMin = self.anchorMax = new Vector2(0.5f, 0.5f);
            self.pivot = new Vector2(0.5f, 0.5f);
            colored = false;
            Match();
        }

        void LateUpdate() => Match();

        void Match()
        {
            if (target == null || self == null) return;

            bool visible = target.gameObject.activeInHierarchy;
            if (image != null) image.enabled = visible;
            if (!visible) return;

            // Centre the shadow on the panel's centre, expressed in the shared parent's local space.
            Vector3 worldCenter = target.TransformPoint(target.rect.center);
            Vector3 localCenter = self.parent.InverseTransformPoint(worldCenter);
            self.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            self.sizeDelta = target.rect.size + new Vector2(spread * 2f, spread * 2f);

            if (!colored || ThemeService.Current != lastTheme) ApplyColor();
        }

        void ApplyColor()
        {
            if (image == null) return;
            lastTheme = ThemeService.Current;
            colored = true;
            // Light theme → dark shadow; dark theme → subtle light glow.
            image.color = ThemeService.Current == ThemeMode.Light
                ? new Color(0f, 0f, 0f, 0.35f)
                : new Color(1f, 1f, 1f, 0.16f);
        }
    }
}
