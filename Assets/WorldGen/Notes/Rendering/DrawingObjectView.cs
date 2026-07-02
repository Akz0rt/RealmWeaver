using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable, fixed-resolution paintable raster. CanvasInteractionController calls
    /// PaintAt while the Drawing tool is active and the mouse is held over this object;
    /// CommitToData() persists the current pixels back into DrawingObjectData for saving.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DrawingObjectView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        DrawingObjectData data;
        RectTransform rect;
        RawImage rawImage;
        Texture2D texture;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        /// <summary>When set, self-move-drag is only allowed while its ActiveTool is Select — otherwise
        /// (e.g. Drawing tool) CanvasInteractionController handles the drag itself (painting).</summary>
        public CanvasInteractionController interactionController;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        public void Initialize(DrawingObjectData drawingData, RectTransform canvasContainer)
        {
            data = drawingData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rawImage = gameObject.AddComponent<RawImage>();

            texture = new Texture2D(data.PixelWidth, data.PixelHeight, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            if (data.PixelDataPng != null && data.PixelDataPng.Length > 0)
            {
                texture.LoadImage(data.PixelDataPng);
            }
            else
            {
                var blank = new Color32[data.PixelWidth * data.PixelHeight];
                for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(blank);
                texture.Apply();
            }
            rawImage.texture = texture;

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        /// <summary>Paints a filled circle at a local point (RectTransform space, origin at center) onto the raster.</summary>
        public void PaintAt(Vector2 localPoint, float brushRadius, Color32 color)
        {
            float u = localPoint.x / rect.rect.width + 0.5f;
            float v = localPoint.y / rect.rect.height + 0.5f;
            int cx = Mathf.RoundToInt(u * texture.width);
            int cy = Mathf.RoundToInt(v * texture.height);
            int pixelRadius = Mathf.Max(1, Mathf.RoundToInt(brushRadius));

            for (int y = -pixelRadius; y <= pixelRadius; y++)
            {
                for (int x = -pixelRadius; x <= pixelRadius; x++)
                {
                    if (x * x + y * y > pixelRadius * pixelRadius) continue;
                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= texture.width || py < 0 || py >= texture.height) continue;
                    texture.SetPixel(px, py, color);
                }
            }
            texture.Apply();
        }

        public void CommitToData()
        {
            if (data == null || texture == null) return;
            data.PixelDataPng = texture.EncodeToPNG();
        }

        bool CanSelfMove => interactionController == null || interactionController.ActiveTool == NotesTool.Select;

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanSelfMove) return;
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
