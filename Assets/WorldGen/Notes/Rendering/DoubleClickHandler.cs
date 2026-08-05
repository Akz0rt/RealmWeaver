using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Detects a double-click (Unity's built-in PointerEventData.clickCount == 2) on the
    /// GameObject it's attached to and invokes OnDoubleClick.
    ///
    /// Its first two callers (NotesTreeSidebar's inline-rename rows, and the divider reset-to-default in
    /// NotesLayoutController/NotesTreeSidebar) went away with those classes in Task 10c; the card's
    /// «двойной клик открывает правку» shield is the current one. It is the project's stated double-click
    /// convention, cited as such from outside this namespace (DungeonViewController.cs).
    ///
    /// IT HANDLES ONLY IPointerClickHandler, AND THAT IS THE POINT. Press, drag and release are dispatched
    /// to the nearest ancestor handling THOSE interfaces, so a shield carrying this component swallows the
    /// double click while single clicks and drags still reach the object underneath it.
    /// </summary>
    public class DoubleClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnDoubleClick;

        /// <summary>Same event, but carrying the pointer — a caller that must know WHERE the double click
        /// landed (the card, deciding between its title and its body) cannot get that from OnDoubleClick.</summary>
        public System.Action<PointerEventData> OnDoubleClickAt;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount != 2) return;
            OnDoubleClick?.Invoke();
            OnDoubleClickAt?.Invoke(eventData);
        }
    }
}
