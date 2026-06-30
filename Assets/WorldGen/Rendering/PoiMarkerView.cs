using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Visual representation of one POI: SpriteRenderer (icon) + TextMesh (name label).
    /// Pure visual — all interaction is handled by PoiInteractionController.
    /// Call Initialize() once after AddComponent, then Refresh() whenever data changes.
    /// </summary>
    public class PoiMarkerView : MonoBehaviour
    {
        PoiData poiData;
        SpriteRenderer iconRenderer;
        TextMesh label;
        float iconWorldSize;

        public string PoiId => poiData?.Id;
        public System.Numerics.Vector2 WorldPos => poiData?.WorldPosition ?? default;

        /// <summary>
        /// Sets up child icon + label GameObjects. Must be called once after AddComponent.
        /// yOffset: Y above map surface. iconWorldSize: world-unit side of the icon quad.
        /// </summary>
        public void Initialize(PoiData data, float yOffset, float iconWorldSize)
        {
            poiData = data;
            this.iconWorldSize = iconWorldSize;

            // Icon — lies flat in XZ plane (rotate -90° around X so sprite faces up)
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(transform, false);
            iconGO.transform.localPosition = new Vector3(0f, yOffset, 0f);
            iconGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            iconGO.transform.localScale = new Vector3(iconWorldSize, iconWorldSize, 1f);
            iconRenderer = iconGO.AddComponent<SpriteRenderer>();

            // Label — flat text slightly north of the icon in XZ (faces up)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0f, yOffset, iconWorldSize * 0.5f + 1.5f);
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            label = labelGO.AddComponent<TextMesh>();
            label.characterSize = 0.5f;
            label.fontSize = 24;
            label.color = Color.white;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;

            Refresh();
        }

        /// <summary>Re-reads poiData and updates icon sprite + label text + position.</summary>
        public void Refresh()
        {
            if (poiData == null) return;

            // Icon sprite: custom if path set and file exists, otherwise placeholder
            Sprite sprite = null;
            if (!string.IsNullOrEmpty(poiData.CustomSpritePath)
                && System.IO.File.Exists(poiData.CustomSpritePath))
            {
                sprite = LoadCustomSprite(poiData.CustomSpritePath);
            }
            if (sprite == null)
                sprite = PoiPlaceholderFactory.GetPlaceholder(poiData.Type);

            if (iconRenderer != null) iconRenderer.sprite = sprite;
            if (label != null) label.text = poiData.Name;

            // Sync local position to data
            transform.localPosition = new Vector3(poiData.WorldPosition.X, 0f, poiData.WorldPosition.Y);
        }

        /// <summary>Highlights the marker (scale ×1.3) or returns to normal (scale ×1).</summary>
        public void SetHighlighted(bool on)
        {
            float s = on ? 1.3f : 1.0f;
            transform.localScale = new Vector3(s, s, s);
        }

        /// <summary>Updates only the visual position without modifying poiData.</summary>
        public void SetVisualPosition(System.Numerics.Vector2 pos)
        {
            transform.localPosition = new Vector3(pos.X, 0f, pos.Y);
        }

        static Sprite LoadCustomSprite(string path)
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes)) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f));
            }
            catch
            {
                return null;
            }
        }
    }
}
