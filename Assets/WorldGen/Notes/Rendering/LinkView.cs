using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

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

        LinkData data;
        RectTransform fromRect;
        RectTransform toRect;
        RectTransform[] segmentRects;
        RectTransform arrowRect;

        public string LinkId => data?.Id;

        public void Initialize(LinkData linkData, RectTransform canvasContainer, RectTransform from, RectTransform to)
        {
            data = linkData;
            fromRect = from;
            toRect = to;

            transform.SetParent(canvasContainer, false);

            segmentRects = new RectTransform[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                var segGO = new GameObject($"Segment_{i}");
                segGO.transform.SetParent(transform, false);
                var segImg = segGO.AddComponent<Image>();
                segImg.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
                var segRect = segGO.GetComponent<RectTransform>();
                segRect.pivot = new Vector2(0f, 0.5f);
                segRect.anchorMin = new Vector2(0f, 0f);
                segRect.anchorMax = new Vector2(0f, 0f);
                segRect.sizeDelta = new Vector2(0f, LineThickness);
                segmentRects[i] = segRect;
            }

            if (data.Directed)
            {
                var arrowGO = new GameObject("Arrow");
                arrowGO.transform.SetParent(transform, false);
                var arrowImg = arrowGO.AddComponent<Image>();
                arrowImg.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
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
}
