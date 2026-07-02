using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Splits the screen 2:1 between the map area (left two-thirds) and the notes area
    /// (right third) using RectTransform anchors, so both regions rescale proportionally
    /// on window resize and never overlap.
    /// </summary>
    public class NotesLayoutController : MonoBehaviour
    {
        [Tooltip("Root RectTransform containing the map/world UI. Anchored to the left two-thirds.")]
        public RectTransform mapAreaRoot;
        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        [Range(0.1f, 0.9f)]
        [Tooltip("Fraction of screen width given to the map area; the rest goes to notes.")]
        public float splitFraction = 2f / 3f;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (mapAreaRoot != null)
            {
                mapAreaRoot.anchorMin = new Vector2(0f, 0f);
                mapAreaRoot.anchorMax = new Vector2(splitFraction, 1f);
                mapAreaRoot.offsetMin = Vector2.zero;
                mapAreaRoot.offsetMax = Vector2.zero;
            }

            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(splitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, splitFraction, 1f);
        }
    }
}
