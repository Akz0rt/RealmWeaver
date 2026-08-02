using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable image object. Decodes ImageObjectData.ImageBytes into a texture on
    /// Initialize (first frame only for animated GIFs — Texture2D.LoadImage doesn't animate).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ImageObjectView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        ImageObjectData data;
        RectTransform rect;
        RawImage rawImage;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        /// <summary>When set, self-move-drag is only allowed while its ActiveTool is Select.</summary>
        public CanvasInteractionController interactionController;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        /// <summary>The DM has begun moving this object. See NoteCardView.OnDragStarted for why the undo
        /// snapshot cannot wait for OnDragEnded.</summary>
        public event System.Action<string> OnDragStarted;

        public void Initialize(ImageObjectData imageData, RectTransform canvasContainer)
        {
            data = imageData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rawImage = gameObject.AddComponent<RawImage>();

            var tex = new Texture2D(2, 2);
            if (data.ImageBytes != null && data.ImageBytes.Length > 0 && tex.LoadImage(data.ImageBytes))
            {
                rawImage.texture = tex;
                if (data.Size.X <= 0f || data.Size.Y <= 0f)
                    data.Size = new System.Numerics.Vector2(tex.width, tex.height);
            }

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        bool CanSelfMove => interactionController == null || interactionController.ActiveTool == NotesTool.Select;

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanSelfMove) return;
            if (!dragging) OnDragStarted?.Invoke(data.Id);
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
