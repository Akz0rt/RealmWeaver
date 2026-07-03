using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// A line (+ optional arrowhead) between the centers of two canvas object views.
    /// UpdateTransform() must be called whenever either endpoint moves.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class LinkView : MonoBehaviour
    {
        LinkData data;
        RectTransform fromRect;
        RectTransform toRect;
        RectTransform lineRect;
        RectTransform arrowRect;

        public string LinkId => data?.Id;

        public void Initialize(LinkData linkData, RectTransform canvasContainer, RectTransform from, RectTransform to)
        {
            data = linkData;
            fromRect = from;
            toRect = to;

            transform.SetParent(canvasContainer, false);

            var lineGO = new GameObject("Line");
            lineGO.transform.SetParent(transform, false);
            var lineImg = lineGO.AddComponent<Image>();
            lineImg.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
            lineRect = lineGO.GetComponent<RectTransform>();
            lineRect.pivot = new Vector2(0f, 0.5f);
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(0f, 0f);
            lineRect.sizeDelta = new Vector2(0f, 3f);

            if (data.Directed)
            {
                var arrowGO = new GameObject("Arrow");
                arrowGO.transform.SetParent(transform, false);
                var arrowImg = arrowGO.AddComponent<Image>();
                arrowImg.color = lineImg.color;
                arrowRect = arrowGO.GetComponent<RectTransform>();
                arrowRect.pivot = new Vector2(1f, 0.5f);
                arrowRect.anchorMin = new Vector2(0f, 0f);
                arrowRect.anchorMax = new Vector2(0f, 0f);
                arrowRect.sizeDelta = new Vector2(14f, 14f);
            }

            UpdateTransform();
        }

        public void UpdateTransform()
        {
            if (fromRect == null || toRect == null || lineRect == null) return;

            Vector2 fromPos = fromRect.anchoredPosition;
            Vector2 toPos = toRect.anchoredPosition;
            Vector2 delta = toPos - fromPos;
            float distance = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            lineRect.anchoredPosition = fromPos;
            lineRect.sizeDelta = new Vector2(distance, lineRect.sizeDelta.y);
            lineRect.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (arrowRect != null)
            {
                arrowRect.anchoredPosition = toPos;
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
    }
}
