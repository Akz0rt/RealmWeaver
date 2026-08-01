using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Hands the mouse wheel to DocumentPageView instead of letting the ScrollRect act on it.
    ///
    /// WHY A SEPARATE COMPONENT AT ALL. A scroll event is delivered to the first ancestor that has ANY
    /// IScrollHandler on it, and then to every such component on that one object — so this has to live on the
    /// ScrollRect's own GameObject, which DocumentPageView is not attached to (it builds that object, it is
    /// not it). Nine lines in their own file is the honest price of that; the alternative, moving the page
    /// view onto the scroll root, would tie a controller's lifetime to a piece of furniture it constructs.
    ///
    /// The ScrollRect still receives the same event and still acts on it — there is no way to hide it — which
    /// is why DocumentPageView sets its scrollSensitivity to zero. That is what makes "acts on it" a no-op
    /// rather than a second, competing scroll.
    /// </summary>
    public class PageWheelRelay : MonoBehaviour, IScrollHandler
    {
        public DocumentPageView page;

        public void OnScroll(PointerEventData eventData)
        {
            if (page == null || eventData == null) return;
            page.WheelScroll(eventData.scrollDelta.y);
        }
    }
}
