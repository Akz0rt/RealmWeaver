using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// A curved (quadratic Bezier) connector between the edges of two canvas object views,
    /// plus an optional arrowhead. UpdateTransform() must be called whenever either endpoint
    /// moves. The curve bends automatically unless LinkData.ControlPointOffset has been set
    /// by dragging the control handle (added in a later task).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class LinkView : MonoBehaviour
    {
        const int SegmentCount = 16;
        const float LineThickness = 3f;
        const float ArrowSize = 14f;

        const float HandleSize = 10f;

        LinkData data;
        RectTransform fromRect;
        RectTransform toRect;
        RectTransform[] segmentRects;
        Image[] segmentImages;
        RectTransform arrowRect;
        RectTransform handleRect;
        Camera uiCamera;
        bool selected;
        bool hovering;

        static readonly Color NormalColor = ThemedAlpha(ThemeRole.Txt, 0.9f);
        static readonly Color SelectedColor = ThemedAlpha(ThemeRole.Accent, 0.95f);

        public string LinkId => data?.Id;

        public void Initialize(LinkData linkData, RectTransform canvasContainer, RectTransform from, RectTransform to, Camera camera)
        {
            data = linkData;
            fromRect = from;
            toRect = to;
            uiCamera = camera;

            transform.SetParent(canvasContainer, false);
            // Explicit zero-size/centered config (matching CanvasContainer's own convention),
            // rather than relying on RectTransform defaults, so segments/handle/arrow parented
            // under this transform sit in exactly the same coordinate frame as fromRect/toRect.
            var selfRect = (RectTransform)transform;
            selfRect.anchorMin = new Vector2(0.5f, 0.5f);
            selfRect.anchorMax = new Vector2(0.5f, 0.5f);
            selfRect.pivot = new Vector2(0.5f, 0.5f);
            selfRect.anchoredPosition = Vector2.zero;
            selfRect.sizeDelta = Vector2.zero;

            segmentRects = new RectTransform[SegmentCount];
            segmentImages = new Image[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                var segGO = new GameObject($"Segment_{i}");
                segGO.transform.SetParent(transform, false);
                var segImg = segGO.AddComponent<Image>();
                segImg.color = NormalColor;
                var segRect = segGO.GetComponent<RectTransform>();
                segRect.pivot = new Vector2(0f, 0.5f);
                segRect.anchorMin = new Vector2(0f, 0f);
                segRect.anchorMax = new Vector2(0f, 0f);
                segRect.sizeDelta = new Vector2(0f, LineThickness);
                segmentRects[i] = segRect;
                segmentImages[i] = segImg;
            }

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(transform, false);
            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = SelectedColor;
            handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(HandleSize, HandleSize);
            var dragHandler = handleGO.AddComponent<LinkHandleDragHandler>();
            dragHandler.owner = this;
            handleGO.SetActive(false);

            if (data.Directed)
            {
                var arrowGO = new GameObject("Arrow");
                arrowGO.transform.SetParent(transform, false);
                var arrowImg = arrowGO.AddComponent<Image>();
                arrowImg.color = NormalColor;
                arrowRect = arrowGO.GetComponent<RectTransform>();
                arrowRect.pivot = new Vector2(1f, 0.5f);
                arrowRect.anchorMin = new Vector2(0f, 0f);
                arrowRect.anchorMax = new Vector2(0f, 0f);
                arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            }

            UpdateTransform();
        }

        public void UpdateTransform()
        {
            if (fromRect == null || toRect == null || segmentRects == null) return;

            Vector2 fromAnchor = GetAnchorPoint(fromRect, toRect.anchoredPosition);
            Vector2 toAnchor = GetAnchorPoint(toRect, fromRect.anchoredPosition);
            Vector2 control = GetControlPoint(fromAnchor, toAnchor);

            Vector2 prev = SampleQuadraticBezier(fromAnchor, control, toAnchor, 0f);
            for (int i = 0; i < SegmentCount; i++)
            {
                float t = (i + 1) / (float)SegmentCount;
                Vector2 next = SampleQuadraticBezier(fromAnchor, control, toAnchor, t);
                PositionSegment(segmentRects[i], prev, next);
                prev = next;
            }

            if (arrowRect != null)
            {
                Vector2 tangentStart = SampleQuadraticBezier(fromAnchor, control, toAnchor, 1f - 1f / SegmentCount);
                Vector2 delta = toAnchor - tangentStart;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                arrowRect.anchoredPosition = toAnchor;
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            handleRect.anchoredPosition = control;
        }

        void Update()
        {
            if (Mouse.current == null || segmentRects == null) return;

            // Counteract CanvasContainer's zoom scale so the bend handle stays a constant,
            // comfortably clickable screen size regardless of how far the canvas is zoomed out.
            float zoom = transform.parent is RectTransform canvasRect ? canvasRect.localScale.x : 1f;
            float invZoom = zoom > 0.0001f ? 1f / zoom : 1f;
            handleRect.localScale = new Vector3(invZoom, invZoom, 1f);

            var screenPos = Mouse.current.position.ReadValue();
            // Checking the handle's own rect too (not just the curve segments) matters because
            // the handle sits away from the curve itself (at the control point, not on the drawn
            // path) — without this, moving the cursor off the thin curve toward the handle would
            // immediately hide it again before it could ever be clicked.
            bool overHandle = RectTransformUtility.RectangleContainsScreenPoint(handleRect, screenPos, uiCamera);
            bool nowHovering = overHandle || ContainsScreenPoint(screenPos, uiCamera);
            if (nowHovering == hovering) return;
            hovering = nowHovering;
            RefreshHandleVisibility();
        }

        /// <summary>True if screenPos lands on any of this link's curve segments — used both
        /// for hover-driven handle visibility and (by NotesCanvasController.FindLinkAt) for
        /// click-to-select.</summary>
        public bool ContainsScreenPoint(Vector2 screenPos, Camera camera)
        {
            foreach (var seg in segmentRects)
                if (RectTransformUtility.RectangleContainsScreenPoint(seg, screenPos, camera))
                    return true;
            return false;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            RefreshHandleVisibility();
            var color = selected ? SelectedColor : NormalColor;
            foreach (var img in segmentImages) img.color = color;
        }

        void RefreshHandleVisibility()
        {
            handleRect.gameObject.SetActive(selected || hovering);
        }

        /// <summary>Called by LinkHandleDragHandler while the user drags the bend handle.</summary>
        public void OnHandleDragged(Vector2 screenPos)
        {
            var canvasRect = (RectTransform)transform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, uiCamera, out var local);
            Vector2 fromAnchor = GetAnchorPoint(fromRect, toRect.anchoredPosition);
            Vector2 toAnchor = GetAnchorPoint(toRect, fromRect.anchoredPosition);
            Vector2 midpoint = (fromAnchor + toAnchor) * 0.5f;
            Vector2 offset = local - midpoint;
            data.ControlPointOffset = new System.Numerics.Vector2(offset.x, offset.y);
            UpdateTransform();
        }

        static void PositionSegment(RectTransform segRect, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            float distance = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            segRect.anchoredPosition = from;
            segRect.sizeDelta = new Vector2(distance, segRect.sizeDelta.y);
            segRect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Point at the midpoint of whichever side of `rect` (top/bottom/left/right)
        /// faces closest toward `towardPoint`, scaled by the rect's aspect ratio so a wide card
        /// still prefers its top/bottom edge when the other object is roughly above/below it.</summary>
        static Vector2 GetAnchorPoint(RectTransform rect, Vector2 towardPoint)
        {
            Vector2 center = rect.anchoredPosition;
            Vector2 size = rect.sizeDelta;
            Vector2 dir = towardPoint - center;
            float halfW = Mathf.Max(size.x * 0.5f, 0.001f);
            float halfH = Mathf.Max(size.y * 0.5f, 0.001f);

            if (Mathf.Abs(dir.x) / halfW > Mathf.Abs(dir.y) / halfH)
                return center + new Vector2(Mathf.Sign(dir.x == 0f ? 1f : dir.x) * halfW, 0f);
            return center + new Vector2(0f, Mathf.Sign(dir.y == 0f ? 1f : dir.y) * halfH);
        }

        Vector2 GetControlPoint(Vector2 fromAnchor, Vector2 toAnchor)
        {
            Vector2 midpoint = (fromAnchor + toAnchor) * 0.5f;
            if (data.ControlPointOffset.HasValue)
                return midpoint + new Vector2(data.ControlPointOffset.Value.X, data.ControlPointOffset.Value.Y);
            return midpoint + AutoBulge(fromAnchor, toAnchor);
        }

        static Vector2 AutoBulge(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 0.001f) return Vector2.zero;
            Vector2 perp = new Vector2(-delta.y, delta.x).normalized;
            float bulge = Mathf.Clamp(delta.magnitude * 0.2f, 0f, 40f);
            return perp * bulge;
        }

        static Vector2 SampleQuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        /// <summary>Resolves a themed role to a Color with a fixed alpha baked in — used for
        /// NormalColor/SelectedColor, which are copied directly into segment/handle Image.color
        /// at runtime (selected-state toggling) rather than staying attached to one persistent
        /// Graphic, so ThemeService.Tag doesn't apply here.</summary>
        static Color ThemedAlpha(ThemeRole role, float alpha)
        {
            var c = ThemeService.Get(role);
            c.a = alpha;
            return c;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: LinkView — Anchor Point Selection")]
        public void SelfTestAnchorPoint()
        {
            var rectGO = new GameObject("TestRect");
            var rect = rectGO.AddComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(200f, 100f);

            var rightPoint = GetAnchorPoint(rect, new Vector2(500f, 0f));
            bool rightOk = Mathf.Approximately(rightPoint.x, 100f) && Mathf.Approximately(rightPoint.y, 0f);

            var topPoint = GetAnchorPoint(rect, new Vector2(0f, 500f));
            bool topOk = Mathf.Approximately(topPoint.x, 0f) && Mathf.Approximately(topPoint.y, 50f);

            Destroy(rectGO);

            bool ok = rightOk && topOk;
            Debug.Log(ok
                ? "Self-Test LinkView — Anchor Point Selection: PASS"
                : $"Self-Test LinkView — Anchor Point Selection: FAIL (rightOk={rightOk}, topOk={topOk})");
        }
    }

    /// <summary>Forwards drag events from the link's bend handle back to its owning LinkView —
    /// kept as a separate component since the handle is a distinct GameObject from LinkView's.</summary>
    class LinkHandleDragHandler : MonoBehaviour, IDragHandler
    {
        public LinkView owner;
        public void OnDrag(PointerEventData eventData) => owner.OnHandleDragged(eventData.position);
    }
}
