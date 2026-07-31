using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Thin draggable bar. Reports only a raw screen-space delta.x per drag frame and a one-shot
    /// "drag ended" callback — it has zero awareness of fractions, pixel widths, or what it's
    /// resizing; each caller interprets the delta its own way. Highlights on hover; stays fully
    /// transparent otherwise.
    ///
    /// Its two original callers (the map/notes split and the notes sidebar width) were deleted in Task 10c
    /// along with the classes that owned them. The one live caller now is the workspace shell's
    /// Primary|Secondary divider — WorkspaceBuilder.BuildPaneContainer, which turns the delta into a
    /// SplitRatio. It still lives in WorldGen.Notes.Rendering rather than moving into the workspace layer:
    /// the move would be pure churn, and Р4 (the canvas block) is expected to want a divider of its own.
    /// </summary>
    public class DraggableDivider : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<float> OnDragDeltaX;
        public System.Action OnDragEnd;

        static readonly Color IdleColor = Color.clear;
        static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.25f);

        Image image;

        /// <summary>Builds the divider GameObject under `parent`, anchored at the given point
        /// (anchorMin == anchorMax) with `pivot` controlling which side of that point the bar
        /// extends into. Callers must pick a pivot that keeps the bar entirely within a region
        /// with no other raycastable UI created after it at the same screen position — a UI
        /// sibling created later always wins raycasts over one created earlier there. `ignoreLayout`
        /// is set so the parent's own LayoutGroup (if any) doesn't try to reposition/resize this
        /// bar itself.</summary>
        public static DraggableDivider Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float width)
        {
            var go = new GameObject("DraggableDivider");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var img = go.AddComponent<Image>();
            img.color = IdleColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;

            var divider = go.AddComponent<DraggableDivider>();
            divider.image = img;
            return divider;
        }

        public void OnDrag(PointerEventData eventData) => OnDragDeltaX?.Invoke(eventData.delta.x);

        public void OnEndDrag(PointerEventData eventData) => OnDragEnd?.Invoke();

        public void OnPointerEnter(PointerEventData eventData) => image.color = HoverColor;

        public void OnPointerExit(PointerEventData eventData) => image.color = IdleColor;
    }
}
