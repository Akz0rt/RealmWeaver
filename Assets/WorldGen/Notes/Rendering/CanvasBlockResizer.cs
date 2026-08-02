using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>The grip in a canvas block's bottom-right corner: drags BOTH axes.
    ///
    /// Reports the drag's beginning and end separately from its motion, because those are three different
    /// things to the page: the motion resizes the block live (the DM must see what they are doing), while the
    /// UNDO step is exactly one per drag — pushed against the state captured at the beginning, not per frame.
    /// A step-per-frame history would make Ctrl+Z walk the corner back a pixel at a time.
    ///
    /// Deltas are taken from PointerEventData.delta rather than from the pointer's absolute position: the grip
    /// moves WITH the corner it is dragging, so an absolute-position calculation would be measuring against a
    /// target that keeps moving underneath it.</summary>
    public class CanvasBlockResizer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        DocBlockView row;

        public void Initialize(DocBlockView owner) => row = owner;

        public void OnBeginDrag(PointerEventData eventData) => row?.RaiseCanvasResizeBegan();

        public void OnDrag(PointerEventData eventData) => row?.ResizeCanvasBy(eventData.delta);

        public void OnEndDrag(PointerEventData eventData) => row?.RaiseCanvasResizeEnded();
    }
}
