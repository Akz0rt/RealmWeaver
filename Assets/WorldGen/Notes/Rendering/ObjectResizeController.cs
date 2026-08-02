using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Attached alongside each canvas object view (NoteCardView/ImageObjectView/
    /// DrawingObjectView). Shows 4 corner handles while the object is selected; dragging one
    /// freely resizes the object (opposite corner stays fixed), applying the change live via
    /// CanvasInteractionController.ApplyResizePreview and pushing undo via CommitResize on release.
    /// </summary>
    public class ObjectResizeController : MonoBehaviour
    {
        const float HandleSize = 10f;
        const float MinObjectSize = 40f;

        static readonly Vector2[] CornerSign =
        {
            new Vector2(-1f, 1f),  // 0: top-left
            new Vector2(1f, 1f),   // 1: top-right
            new Vector2(-1f, -1f), // 2: bottom-left
            new Vector2(1f, -1f),  // 3: bottom-right
        };

        RectTransform hostRect;
        RectTransform canvasContainer;
        CanvasInteractionController interactionController;
        string hostObjectId;

        RectTransform[] handles;
        bool selected;

        Vector2 fixedCorner;
        Vector2 dragStartPosition;
        Vector2 dragStartSize;
        int draggingCornerIndex = -1;

        public void Initialize(string objectId, RectTransform host, RectTransform container, CanvasInteractionController controller)
        {
            hostObjectId = objectId;
            hostRect = host;
            canvasContainer = container;
            interactionController = controller;

            handles = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var handleGO = new GameObject($"ResizeHandle_{i}");
                handleGO.transform.SetParent(canvasContainer, false);
                var img = handleGO.AddComponent<Image>();
                img.color = new Color(1f, 0.6f, 0.1f, 0.95f);
                var rect = handleGO.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(HandleSize, HandleSize);
                var handler = handleGO.AddComponent<ResizeHandleHandler>();
                handler.owner = this;
                handler.cornerIndex = i;
                handleGO.SetActive(false);
                handles[i] = rect;
            }
        }

        void Update()
        {
            PositionHandles();
        }

        // Handles are parented to canvasContainer (not to this GameObject) so their
        // anchoredPosition lives in the same coordinate space as hostRect — destroying this
        // component alone would otherwise leave them orphaned, active, and still wired to a
        // now-destroyed hostRect via BeginDrag/UpdateDrag.
        void OnDestroy()
        {
            if (handles == null) return;
            foreach (var h in handles)
                if (h != null) Destroy(h.gameObject);
        }

        void PositionHandles()
        {
            Vector2 half = hostRect.sizeDelta * 0.5f;
            Vector2 center = hostRect.anchoredPosition;
            for (int i = 0; i < 4; i++)
                handles[i].anchoredPosition = center + new Vector2(CornerSign[i].x * half.x, CornerSign[i].y * half.y);

            // Counteract CanvasContainer's zoom scale so handles stay a constant, comfortably
            // clickable screen size regardless of how far the canvas is zoomed out.
            float zoom = canvasContainer.localScale.x;
            float invZoom = zoom > 0.0001f ? 1f / zoom : 1f;
            foreach (var h in handles)
                h.localScale = new Vector3(invZoom, invZoom, 1f);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            foreach (var h in handles) h.gameObject.SetActive(selected);
        }

        /// <summary>True if screenPos lands on one of this object's 4 resize handles — used to
        /// suppress the active tool's own click action, same reason IsScreenPointOverLinkAnchor
        /// exists for link anchor dots (CanvasInteractionController.HandlePress polls the mouse
        /// directly and has no other way to know a UI element under the cursor owns this press).</summary>
        public bool IsScreenPointOverHandle(Vector2 screenPos, Camera uiCamera)
        {
            if (!selected) return false;
            foreach (var h in handles)
                if (RectTransformUtility.RectangleContainsScreenPoint(h, screenPos, uiCamera))
                    return true;
            return false;
        }

        /// <summary>Whether THIS drag has already taken its undo snapshot. The snapshot is taken on the first
        /// movement rather than on the press, so pressing a handle and letting go without moving does not
        /// spend a step — and it cannot wait for the release, because ApplyResizePreview has been writing the
        /// object's size into the data all along.</summary>
        bool resizeSnapshotTaken;

        public void BeginDrag(int cornerIndex, Vector2 screenPos)
        {
            draggingCornerIndex = cornerIndex;
            resizeSnapshotTaken = false;
            dragStartPosition = hostRect.anchoredPosition;
            dragStartSize = hostRect.sizeDelta;
            Vector2 half = dragStartSize * 0.5f;
            Vector2 oppositeSign = -CornerSign[cornerIndex];
            fixedCorner = dragStartPosition + new Vector2(oppositeSign.x * half.x, oppositeSign.y * half.y);
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            if (draggingCornerIndex < 0 || interactionController == null) return;
            Camera cam = interactionController.uiCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPos, cam, out var mouseLocal);

            ComputeResize(draggingCornerIndex, fixedCorner, mouseLocal, MinObjectSize, out var newCenter, out var newSize);

            if (!resizeSnapshotTaken)
            {
                resizeSnapshotTaken = true;
                interactionController.BeginResize(hostObjectId);
            }

            interactionController.ApplyResizePreview(hostObjectId,
                new System.Numerics.Vector2(newCenter.x, newCenter.y),
                new System.Numerics.Vector2(newSize.x, newSize.y));
        }

        public void EndDrag(Vector2 screenPos)
        {
            if (draggingCornerIndex < 0) return;
            draggingCornerIndex = -1;
            if (!resizeSnapshotTaken) return;   // a press with no movement changed nothing
            resizeSnapshotTaken = false;
            interactionController.CommitResize(hostObjectId,
                new System.Numerics.Vector2(dragStartPosition.x, dragStartPosition.y),
                new System.Numerics.Vector2(dragStartSize.x, dragStartSize.y));
        }

        /// <summary>Pure geometry: given which corner is being dragged, the opposite (fixed)
        /// corner's position, and the current mouse position (both in the same local space),
        /// returns the new center position and size, clamped to minSize so the object can't be
        /// dragged past the fixed corner or collapse to zero.</summary>
        static void ComputeResize(int cornerIndex, Vector2 fixedCorner, Vector2 mouseLocal, float minSize, out Vector2 newCenter, out Vector2 newSize)
        {
            Vector2 sign = CornerSign[cornerIndex];
            float rawWidth = (mouseLocal.x - fixedCorner.x) * sign.x;
            float rawHeight = (mouseLocal.y - fixedCorner.y) * sign.y;
            float width = Mathf.Max(minSize, rawWidth);
            float height = Mathf.Max(minSize, rawHeight);
            newCenter = fixedCorner + new Vector2(sign.x * width * 0.5f, sign.y * height * 0.5f);
            newSize = new Vector2(width, height);
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: ObjectResizeController — Corner Resize Math")]
        public void SelfTestCornerResize()
        {
            // Bottom-right corner (index 3) dragged so the rect spans x:[-100,100], y:[-50,50] —
            // fixed corner is the opposite (top-left) corner.
            ComputeResize(3, new Vector2(-100f, 50f), new Vector2(100f, -50f), 40f, out var center, out var size);
            bool normalOk = Mathf.Approximately(center.x, 0f) && Mathf.Approximately(center.y, 0f)
                && Mathf.Approximately(size.x, 200f) && Mathf.Approximately(size.y, 100f);

            // Same drag, but the mouse crosses past the fixed corner — width/height must clamp
            // to the minimum instead of going negative or flipping the rect.
            ComputeResize(3, new Vector2(-100f, 50f), new Vector2(-150f, 100f), 40f, out var clampedCenter, out var clampedSize);
            bool clampOk = Mathf.Approximately(clampedSize.x, 40f) && Mathf.Approximately(clampedSize.y, 40f);

            bool ok = normalOk && clampOk;
            Debug.Log(ok
                ? "Self-Test ObjectResizeController — Corner Resize Math: PASS"
                : $"Self-Test ObjectResizeController — Corner Resize Math: FAIL (normalOk={normalOk}, clampOk={clampOk}, center={center}, size={size})");
        }
    }

    /// <summary>One draggable corner handle; forwards press/drag/release to its owning
    /// ObjectResizeController along with which corner it is.</summary>
    class ResizeHandleHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public ObjectResizeController owner;
        public int cornerIndex;
        public void OnPointerDown(PointerEventData eventData) => owner.BeginDrag(cornerIndex, eventData.position);
        public void OnDrag(PointerEventData eventData) => owner.UpdateDrag(eventData.position);
        public void OnPointerUp(PointerEventData eventData) => owner.EndDrag(eventData.position);
    }
}
