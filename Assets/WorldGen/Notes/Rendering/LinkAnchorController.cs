using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Attached alongside each canvas object view (NoteCardView/ImageObjectView/
    /// DrawingObjectView). Reveals 4 small anchor dots at the object's edge midpoints on
    /// hover; dragging from one draws a rubber-band preview and, on release over another
    /// object, creates a link via CanvasInteractionController.CreateLinkFromAnchorDrag.
    /// </summary>
    public class LinkAnchorController : MonoBehaviour
    {
        const float DotSize = 10f;

        RectTransform hostRect;
        RectTransform canvasContainer;
        CanvasInteractionController interactionController;
        string hostObjectId;

        RectTransform[] dots;
        bool hovering;
        bool dragging;
        Vector2 dragStartLocal;
        RectTransform previewRect;

        public void Initialize(string objectId, RectTransform host, RectTransform container, CanvasInteractionController controller)
        {
            hostObjectId = objectId;
            hostRect = host;
            canvasContainer = container;
            interactionController = controller;

            dots = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var dotGO = new GameObject($"AnchorDot_{i}");
                dotGO.transform.SetParent(canvasContainer, false);
                var img = dotGO.AddComponent<Image>();
                img.color = new Color(0.3f, 0.7f, 1f, 0.95f);
                var rect = dotGO.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(DotSize, DotSize);
                var handler = dotGO.AddComponent<AnchorDotHandler>();
                handler.owner = this;
                dotGO.SetActive(false);
                dots[i] = rect;
            }

            var previewGO = new GameObject("LinkPreview");
            previewGO.transform.SetParent(canvasContainer, false);
            var previewImg = previewGO.AddComponent<Image>();
            previewImg.color = new Color(0.3f, 0.7f, 1f, 0.7f);
            previewImg.raycastTarget = false;
            previewRect = previewGO.GetComponent<RectTransform>();
            previewRect.pivot = new Vector2(0f, 0.5f);
            previewRect.sizeDelta = new Vector2(0f, 3f);
            previewGO.SetActive(false);
        }

        void Update()
        {
            PositionDots();
            if (dragging) return;
            if (Mouse.current == null) return;

            var screenPos = Mouse.current.position.ReadValue();
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            bool nowHovering = RectTransformUtility.RectangleContainsScreenPoint(hostRect, screenPos, cam);
            if (nowHovering == hovering) return;
            hovering = nowHovering;
            foreach (var dot in dots) dot.gameObject.SetActive(hovering);
        }

        /// <summary>True if screenPos lands on one of this object's 4 anchor dots, regardless
        /// of whether they're currently active — checked before they've had a chance to become
        /// active on the same frame a hover-then-press happens in quick succession.</summary>
        public bool IsScreenPointOverDot(Vector2 screenPos, Camera uiCamera)
        {
            if (!hovering) return false;
            foreach (var dot in dots)
                if (RectTransformUtility.RectangleContainsScreenPoint(dot, screenPos, uiCamera))
                    return true;
            return false;
        }

        void PositionDots()
        {
            Vector2 half = hostRect.sizeDelta * 0.5f;
            Vector2 center = hostRect.anchoredPosition;
            dots[0].anchoredPosition = center + new Vector2(0f, half.y);
            dots[1].anchoredPosition = center + new Vector2(0f, -half.y);
            dots[2].anchoredPosition = center + new Vector2(-half.x, 0f);
            dots[3].anchoredPosition = center + new Vector2(half.x, 0f);

            // Counteract CanvasContainer's zoom scale so the dots stay a constant, comfortably
            // clickable screen size regardless of how far the canvas is zoomed out.
            float zoom = canvasContainer.localScale.x;
            float invZoom = zoom > 0.0001f ? 1f / zoom : 1f;
            foreach (var dot in dots)
                dot.localScale = new Vector3(invZoom, invZoom, 1f);
        }

        public void BeginDrag(Vector2 screenPos)
        {
            dragging = true;
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPos, cam, out dragStartLocal);
            previewRect.gameObject.SetActive(true);
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPos, cam, out var local);
            Vector2 delta = local - dragStartLocal;
            previewRect.anchoredPosition = dragStartLocal;
            previewRect.sizeDelta = new Vector2(delta.magnitude, previewRect.sizeDelta.y);
            previewRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        public void EndDrag(Vector2 screenPos)
        {
            dragging = false;
            previewRect.gameObject.SetActive(false);
            if (interactionController == null || interactionController.canvasController == null) return;

            Camera cam = interactionController.uiCamera;
            string targetId = interactionController.canvasController.FindObjectAt(screenPos, cam, hostObjectId);
            if (targetId != null)
                interactionController.CreateLinkFromAnchorDrag(hostObjectId, targetId);
        }
    }

    /// <summary>One draggable anchor dot; forwards press/drag/release to its owning
    /// LinkAnchorController.</summary>
    class AnchorDotHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public LinkAnchorController owner;
        public void OnPointerDown(PointerEventData eventData) => owner.BeginDrag(eventData.position);
        public void OnDrag(PointerEventData eventData) => owner.UpdateDrag(eventData.position);
        public void OnPointerUp(PointerEventData eventData) => owner.EndDrag(eventData.position);
    }
}
