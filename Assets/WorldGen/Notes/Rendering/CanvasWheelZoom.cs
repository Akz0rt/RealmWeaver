using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The wheel's zoom ARITHMETIC, in one place, because two different callers now produce it: the expanded
    /// view polls the mouse directly (CanvasInteractionController.Update) and the inline frame receives uGUI's
    /// own scroll event (CanvasWheelRelay below). Two copies of the exponent, the tick normalisation and the
    /// cursor anchoring would drift apart, and the drift would show up as one of the two views feeling broken.
    ///
    /// THE STEP ITSELF IS NOT SHARED, and that is not the same thing. The two views have different step
    /// constants on purpose — see each one's doc — because one is a whole pane being worked in and the other
    /// is a small frame inside text being read. Same rule, two distances.
    /// </summary>
    public static class CanvasWheelZoom
    {
        /// <summary>How much one wheel notch multiplies the zoom by in the EXPANDED view: e^0.22 ≈ 1.25, so a
        /// notch is 25% in or out. MULTIPLICATIVE, not additive, and that is the half of "make the wheel
        /// faster" that matters more than the number. The old rule ADDED 0.12 to the scale per notch, which is
        /// a twelfth of a step at 1.5× and half a step at 0.25× — near the bottom of the range the board
        /// leapt, near the top it crawled. A factor feels identical everywhere, which is what lets the step be
        /// this large without becoming unusable when zoomed out.</summary>
        public const float ExpandedStepPerTick = 0.22f;

        /// <summary>The INLINE frame's Ctrl+wheel step: e^0.055 ≈ 1.057, a notch is under 6%. A QUARTER of the
        /// expanded step, and that difference is deliberate rather than an oversight to be unified later.
        ///
        /// The two views are aimed at different things. The expanded board is a whole pane the DM is working
        /// in, where a quarter per notch is how you cross the range in a couple of flicks. The inline frame is
        /// a 320-pixel window inside a page they are READING: the same quarter throws the contents past the
        /// edges of a small frame in one notch, which reads as the board jumping rather than as zooming, and
        /// it happens while the DM's other hand is on Ctrl and the page is scrolling underneath. Small steps
        /// are what "smoother" means here — there is no animation to add, only a distance per notch to
        /// choose, and this one is gentler than even the pre-Р4 rule was.</summary>
        public const float InlineStepPerTick = 0.055f;

        /// <summary>Wheel notches from whatever the caller had. Unity's Input System reports the raw device
        /// value (120 per notch on Windows), while uGUI's PointerEventData.scrollDelta is already normalised to
        /// about 1 — and there are mice and trackpads on both sides of that. Deciding by MAGNITUDE rather than
        /// by which caller it is keeps one rule for both, and keeps a device that reports ±1 to the polled path
        /// from silently zooming a hundredth of a notch.</summary>
        public static float ToTicks(float raw) => Mathf.Abs(raw) > 10f ? raw / 120f : raw;

        /// <summary>Starts a smoothed zoom around the CURSOR — not an instant one, and not around the viewport
        /// centre, which is what the old polled path did. All three parts answer the same complaint. A step
        /// around a fixed centre flings whatever the DM was looking at out of the frame; a step applied in one
        /// frame reads as the board teleporting however small the step is; and anchoring at the pointer is
        /// what makes zooming feel like moving closer rather than like the board jumping. The glide itself
        /// belongs to NotesCanvasController.ZoomTowards, which re-applies the anchor every frame.</summary>
        /// <param name="stepPerTick">ExpandedStepPerTick or InlineStepPerTick — passed rather than read from
        /// the controller's Mode so the number is chosen at the CALL SITE, where the gesture it belongs to is
        /// visible.</param>
        public static void Apply(NotesCanvasController controller, float rawScroll, Vector2 screenPos,
                                 Camera uiCamera, float stepPerTick)
        {
            if (controller == null || controller.CanvasContainer == null) return;
            float ticks = ToTicks(rawScroll);
            if (Mathf.Abs(ticks) < 0.001f) return;

            // FROM THE TARGET, not from what is on screen — see NotesCanvasController.ZoomTarget. While a
            // previous notch is still gliding, multiplying the LIVE scale would make each notch of a fast
            // scroll shorter than the one before it.
            float from = controller.ZoomTarget;
            if (from < 0.0001f) from = 1f;
            // ZoomTowards glides there over the next few frames, clamping to [0.25, 3] and keeping the point
            // under the cursor fixed the whole way.
            controller.ZoomTowards(from * Mathf.Exp(ticks * stepPerTick), screenPos, uiCamera);
        }
    }

    /// <summary>
    /// Ctrl+wheel zooms a board drawn INLINE in a page; a plain wheel goes on scrolling the page.
    ///
    /// WHY A RELAY AND NOT A FLAG ON THE INTERACTION CONTROLLER. The inline view deliberately builds no
    /// editing gestures at all — that is the whole reason a board can live inside a document without fighting
    /// it (CanvasInteractionController.Update returns immediately in Inline mode). Zoom is the one exception
    /// the DM asked for, so it arrives as its own small component on the frame rather than by re-opening that
    /// door: nothing else about the inline board changes, and there is no mode flag anywhere that could later
    /// let pan, drawing or link-dragging back in by accident.
    ///
    /// WHY THE PLAIN WHEEL HAS TO BE FORWARDED BY HAND. uGUI does not bubble: EventSystem walks up from the
    /// object under the pointer, hands the event to the FIRST ancestor that implements IScrollHandler, and
    /// stops. Merely declining to act on it here would therefore swallow it — the page would freeze whenever
    /// the cursor happened to be over a board, which is precisely the defect the "the wheel always belongs to
    /// the page" rule exists to prevent. Re-dispatching from the PARENT continues the search past this object
    /// and reaches the page's ScrollRect/PageWheelRelay exactly as if this component were not here. This is
    /// the same manoeuvre TMP_InputField performs for the same reason.
    /// </summary>
    public class CanvasWheelRelay : MonoBehaviour, IScrollHandler
    {
        public NotesCanvasController canvasController;
        public Camera uiCamera;

        public void OnScroll(PointerEventData eventData)
        {
            var keyboard = Keyboard.current;
            bool ctrl = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            if (ctrl && canvasController != null)
            {
                CanvasWheelZoom.Apply(canvasController, eventData.scrollDelta.y, eventData.position, uiCamera,
                                      CanvasWheelZoom.InlineStepPerTick);
                return;
            }

            var parent = transform.parent;
            if (parent != null)
                ExecuteEvents.ExecuteHierarchy(parent.gameObject, eventData, ExecuteEvents.scrollHandler);
        }
    }

    /// <summary>
    /// Drag the board inside a page to move it, and double-click it to fit it back.
    ///
    /// THE SECOND HALF IS NOT A BONUS. Panning is the first gesture that can put the board's contents somewhere
    /// the DM cannot see, and the automatic fit stops the moment they aim the view themselves (by design — see
    /// DocBlock.CanvasViewSet). Inside a page there is no toolbar, no scrollbar and no «reset» anywhere, so a
    /// pan with no way back is a way to lose a board's contents inside a 320-pixel window. The double-click
    /// gives the fit back and costs nothing.
    ///
    /// EMPTY SPACE ONLY, and that falls out of uGUI rather than being checked here: a drag that starts on a
    /// CARD is delivered to the card's own IDragHandler, which moves the card, and never reaches this
    /// component at all. So "drag the board" and "drag a card on the board" are told apart by where the press
    /// landed, with no rule to get wrong.
    ///
    /// ANY BUTTON, deliberately. The expanded view pans with the MIDDLE button (its left button belongs to the
    /// active tool); here there is no tool, so the left button is free and is what a DM will actually try.
    /// Accepting both means the habit built in one view works in the other.
    /// </summary>
    public class CanvasInlinePan : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        public NotesCanvasController canvasController;

        public void OnBeginDrag(PointerEventData eventData)
        {
            // A glide still running would keep writing the scale — and the pan correction inside it would drag
            // the board back out from under the cursor for as long as it lasted.
            if (canvasController != null) canvasController.CancelZoomAnimation();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (canvasController != null) canvasController.Pan(eventData.delta);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount >= 2 && canvasController != null) canvasController.ResetViewToFit();
        }
    }
}
