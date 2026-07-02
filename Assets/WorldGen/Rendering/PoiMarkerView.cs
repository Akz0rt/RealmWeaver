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
        Transform iconTransform;
        TextMesh label;
        Transform labelTransform;
        float yOffset;
        float baseIconWorldSize;
        float baseLabelCharacterSize;

        public string PoiId => poiData?.Id;
        public System.Numerics.Vector2 WorldPos => poiData?.WorldPosition ?? default;

        /// <summary>
        /// Sets up child icon + label GameObjects. Must be called once after AddComponent.
        /// yOffset: Y above map surface. baseIconWorldSize/baseLabelCharacterSize: shared defaults,
        /// further multiplied per-instance by poiData.IconScale / poiData.LabelScale in Refresh().
        /// </summary>
        public void Initialize(PoiData data, float yOffset, float baseIconWorldSize, float baseLabelCharacterSize)
        {
            poiData = data;
            this.yOffset = yOffset;
            this.baseIconWorldSize = baseIconWorldSize;
            this.baseLabelCharacterSize = baseLabelCharacterSize;

            // Icon — lies flat in XZ plane (rotate -90° around X so sprite faces up)
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(transform, false);
            iconGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            iconTransform = iconGO.transform;
            iconRenderer = iconGO.AddComponent<SpriteRenderer>();

            // Label — flat text slightly north of the icon in XZ (faces up)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            labelTransform = labelGO.transform;
            label = labelGO.AddComponent<TextMesh>();
            label.fontSize = 48;
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

            // Icon/label sizes are the shared base multiplied by this POI's individual scale.
            float iconWorldSize = baseIconWorldSize * poiData.IconScale;
            if (iconTransform != null)
            {
                iconTransform.localPosition = new Vector3(0f, yOffset, 0f);
                iconTransform.localScale = new Vector3(iconWorldSize, iconWorldSize, 1f);
            }
            if (labelTransform != null)
                labelTransform.localPosition = new Vector3(0f, yOffset, iconWorldSize * 0.5f + 1.5f);
            if (label != null)
                label.characterSize = baseLabelCharacterSize * poiData.LabelScale;

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
