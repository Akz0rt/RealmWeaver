using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick.
    ///
    /// CURRENTLY UNUSED — its only two callers (NotesTreeSidebar's inline-rename rows, and the divider
    /// reset-to-default in NotesLayoutController/NotesTreeSidebar) went away with those classes in Task 10c.
    /// Kept rather than deleted because it is the project's stated double-click convention, cited as such
    /// from outside this namespace (DungeonViewController.cs), and NavigatorView's ported rename flow is the
    /// obvious next caller. Delete it if a later task confirms nothing wants it.
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
