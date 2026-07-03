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
        /// <summary>Single source of truth for the map/notes screen split — the ONLY place this
        /// fraction is defined. MapLegendUI and PoiEditPanel read this directly instead of each
        /// declaring their own copy, which is what let them drift out of sync before. A const
        /// (not a runtime-assigned static) is used because those two panels apply it inside their
        /// own Awake(), and Unity does not guarantee this class's Awake() runs first.</summary>
        public const float SplitFraction = 2f / 3f;

        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(SplitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, SplitFraction, 1f);
        }
    }
}
