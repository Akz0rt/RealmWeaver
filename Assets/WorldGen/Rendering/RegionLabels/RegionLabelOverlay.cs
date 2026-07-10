using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Screen-space overlay for region labels: one TMP text per label, projected from its world
    /// centroid each frame, alpha driven by camera zoom (visible zoomed-out, fades in when zoomed in).
    /// Render + LOD only — no editing/dragging (see Task 5's click/drag layer).</summary>
    public class RegionLabelOverlay : MonoBehaviour
    {
        [Header("Источники")]
        public RegionLabelManager manager;
        public MapCameraController cameraController;
        public TMP_FontAsset labelFont;

        [Header("LOD (доли от NaturalFitSize)")]
        [Range(0f,1f)] public float farFrac = 0.8f;   // >= этого (отдалено) -> полностью видно
        [Range(0f,1f)] public float nearFrac = 0.35f; // <= этого (приближено) -> скрыто
        public float baseFontSize = 26f;
        public float labelYOffsetWorld = 0.5f;         // приподнять точку привязки над картой

        bool visible = true;
        RectTransform canvasRect;
        readonly Dictionary<string, TextMeshProUGUI> views = new Dictionary<string, TextMeshProUGUI>();

        void Awake()
        {
            BuildCanvas();          // mirror PoiEditPanel's canvas setup; store canvasRect
            if (manager != null) manager.OnLabelsChanged += Rebuild;
            Rebuild();
        }
        void OnDestroy() { if (manager != null) manager.OnLabelsChanged -= Rebuild; }

        public void SetVisible(bool on) { visible = on; if (canvasRect != null) canvasRect.gameObject.SetActive(on); }

        void BuildCanvas()
        {
            var canvasGO = new GameObject("RegionLabelCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasRect = canvasGO.GetComponent<RectTransform>();   // Canvas auto-adds a RectTransform
            EnsureEventSystemExists();
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        void Rebuild()
        {
            foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
            views.Clear();
            if (manager == null) return;
            foreach (var d in manager.GetAll()) views[d.Id] = CreateLabelView(d);
        }

        TextMeshProUGUI CreateLabelView(RegionLabelData d)
        {
            var go = new GameObject($"RegionLabel_{d.Id}");
            go.transform.SetParent(canvasRect, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (labelFont != null) tmp.font = labelFont;
            tmp.text = d.Text;
            tmp.fontSize = baseFontSize;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 8f;                 // letter-spacing
            tmp.color = new Color(0.86f, 0.84f, 0.78f, 1f);
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;                 // click handled by an overlaid button in Task 5
            tmp.outlineWidth = 0.18f;                  // dark halo (needs an outline-capable material preset)
            tmp.outlineColor = new Color32(6, 10, 16, 220);
            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 34f);
            return tmp;
        }

        void LateUpdate()
        {
            if (!visible || manager == null || cameraController == null) return;
            var cam = cameraController.targetCamera;
            float refSize = cameraController.NaturalFitSize;
            if (cam == null || refSize <= 0f) return;

            float alpha = LodAlpha(cam.orthographicSize / refSize);

            // basic collision: keep placed screen rects, nudge overlapping labels down.
            var placed = new List<Rect>();
            foreach (var d in manager.GetAll())
            {
                if (!views.TryGetValue(d.Id, out var tmp) || tmp == null) continue;
                Vector3 world = new Vector3(d.WorldPosition.X, labelYOffsetWorld, d.WorldPosition.Y);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool onScreen = sp.z > 0f && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
                var c = tmp.color; c.a = onScreen ? alpha : 0f; tmp.color = c;
                if (!onScreen || alpha <= 0.01f) { tmp.rectTransform.anchoredPosition = new Vector2(-9999, -9999); continue; }

                // screen -> canvas anchoredPosition (canvas is ScreenSpaceOverlay so 1:1 with screen px, pivot .5)
                Vector2 pos = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);
                var rect = new Rect(pos.x - 110, pos.y - 17, 220, 34);
                int guard = 0;
                while (guard++ < 8 && placed.Exists(r => r.Overlaps(rect))) { pos.y -= 30f; rect.y -= 30f; }
                placed.Add(rect);
                tmp.rectTransform.anchoredPosition = pos;
                // also fade the outline alpha with the text (optional): tmp.fontMaterial... keep simple for v1.
            }
        }

        float LodAlpha(float zoomRatio) // orthoSize/NaturalFitSize; large = zoomed out
        {
            if (zoomRatio >= farFrac) return 1f;
            if (zoomRatio <= nearFrac) return 0f;
            float t = (zoomRatio - nearFrac) / Mathf.Max(1e-4f, (farFrac - nearFrac));
            return Mathf.SmoothStep(0f, 1f, t);
        }
    }
}
