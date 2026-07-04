using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick. Used by NotesTreeSidebar for
    /// inline-rename mode on group/page rows, and by DraggableDivider-based UI for
    /// reset-to-default.
    /// </summary>
    public class DoubleClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnDoubleClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 2)
                OnDoubleClick?.Invoke();
        }
    }
}
