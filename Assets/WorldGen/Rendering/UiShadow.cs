using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Soft drop shadows for floating uGUI cards/panels/dialogs. Generates one 9-sliced
    /// soft-edge sprite at runtime (no external assets, like PoiPlaceholderFactory) and drops a
    /// shadow Image behind a panel as a sibling, kept matched to the panel's rect by
    /// UiShadowFollower (so it tracks ContentSizeFitter-driven size changes).
    /// </summary>
    public static class UiShadow
    {
        static Sprite softSprite;
        const int TexSize = 64;
        const int Border = 20; // 9-slice border = soft falloff width

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
            int innerMin = Border;
            int innerMax = TexSize - 1 - Border;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    // Distance outside the opaque inner rectangle (0 inside it).
                    float dx = Mathf.Max(0, Mathf.Max(innerMin - x, x - innerMax));
                    float dy = Mathf.Max(0, Mathf.Max(innerMin - y, y - innerMax));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.Clamp01(d / Border);
                    a *= a; // ease → softer falloff
                    pixels[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            softSprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
            return softSprite;
        }

        /// <summary>Adds a soft shadow behind <paramref name="panel"/> (inserted as the sibling just
        /// before it, so it renders behind). The panel must be a direct child of a Canvas or a
        /// non-layout container — not a child of a LayoutGroup, where the extra sibling would
        /// disturb the layout.</summary>
        public static void Add(RectTransform panel, float spread = 22f, float alpha = 0.4f)
            => Add(panel, spread, new Vector2(0f, -6f), alpha);

        public static void Add(RectTransform panel, float spread, Vector2 offset, float alpha)
        {
            if (panel == null) return;

            var shadowGO = new GameObject("Shadow");
            shadowGO.transform.SetParent(panel.parent, false);
            shadowGO.transform.SetSiblingIndex(panel.GetSiblingIndex()); // before the panel → renders behind it

            var img = shadowGO.AddComponent<Image>();
            img.sprite = GetSoftShadowSprite();
            img.type = Image.Type.Sliced;
            img.color = new Color(0f, 0f, 0f, alpha);
            img.raycastTarget = false;

            var follower = shadowGO.AddComponent<UiShadowFollower>();
            follower.Configure(panel, offset, spread);
        }
    }

    /// <summary>Keeps a shadow Image matched to its target panel's rect (+spread, +offset) every
    /// frame, so shadows track panels that resize via ContentSizeFitter or reposition at runtime.</summary>
    public class UiShadowFollower : MonoBehaviour
    {
        RectTransform target;
        RectTransform self;
        Image image;
        Vector2 offset;
        float spread;

        public void Configure(RectTransform target, Vector2 offset, float spread)
        {
            this.target = target;
            this.offset = offset;
            this.spread = spread;
            self = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            Match();
        }

        void LateUpdate() => Match();

        void Match()
        {
            if (target == null || self == null) return;
            // The shadow is a sibling of the panel, so it stays active even when the panel is
            // hidden via panel.SetActive(false) (e.g. PoiEditPanel) — hide the shadow to match.
            bool visible = target.gameObject.activeInHierarchy;
            if (image != null) image.enabled = visible;
            if (!visible) return;

            self.anchorMin = target.anchorMin;
            self.anchorMax = target.anchorMax;
            self.pivot = target.pivot;
            self.anchoredPosition = target.anchoredPosition + offset;
            self.sizeDelta = target.sizeDelta + new Vector2(spread * 2f, spread * 2f);
        }
    }
}
