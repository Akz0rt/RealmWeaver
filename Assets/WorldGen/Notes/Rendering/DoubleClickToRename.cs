using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick. Used by NotesTreeSidebar to
    /// enter inline-rename mode on group/page rows.
    /// </summary>
    public class DoubleClickToRename : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnDoubleClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 2)
                OnDoubleClick?.Invoke();
        }
    }
}
