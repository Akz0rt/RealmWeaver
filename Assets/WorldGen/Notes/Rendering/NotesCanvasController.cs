using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Renders the NotesDocumentController's active page: spawns/destroys object and link
    /// views to match the page data, and owns pan/zoom of the canvas content root.
    /// </summary>
    public class NotesCanvasController : MonoBehaviour
    {
        [Header("Dependencies")]
        public NotesDocumentController documentController;
        [Tooltip("Viewport RectTransform that clips the canvas content (mask/scroll area).")]
        public RectTransform viewport;

        public RectTransform CanvasContainer { get; private set; }

        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();

        public event System.Action OnSelectionCleared;

        void Awake()
        {
            var containerGO = new GameObject("CanvasContainer");
            containerGO.transform.SetParent(viewport != null ? viewport : transform, false);
            CanvasContainer = containerGO.AddComponent<RectTransform>();
            CanvasContainer.anchorMin = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchorMax = new Vector2(0.5f, 0.5f);
            CanvasContainer.pivot = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchoredPosition = Vector2.zero;
            CanvasContainer.sizeDelta = Vector2.zero;
        }

        void OnEnable()
        {
            if (documentController != null)
                documentController.OnActivePageChanged += HandleActivePageChanged;
        }

        void OnDisable()
        {
            if (documentController != null)
                documentController.OnActivePageChanged -= HandleActivePageChanged;
        }

        void HandleActivePageChanged(NotesPage page)
        {
            RebuildFromPage(page);
        }

        // ── Rebuild ────────────────────────────────────────────────────────────

        public void RebuildFromPage(NotesPage page)
        {
            foreach (var view in objectViews.Values)
                if (view != null) Destroy(view.gameObject);
            objectViews.Clear();
            foreach (var link in linkViews.Values)
                if (link != null) Destroy(link.gameObject);
            linkViews.Clear();
            OnSelectionCleared?.Invoke();

            if (page == null) return;

            CanvasContainer.anchoredPosition = new Vector2(page.CameraPan.X, page.CameraPan.Y);
            CanvasContainer.localScale = new Vector3(page.CameraZoom, page.CameraZoom, 1f);

            foreach (var obj in page.Objects)
                SpawnView(obj);

            foreach (var link in page.Links)
                SpawnLink(link);
        }

        void SpawnView(CanvasObjectData obj)
        {
            switch (obj)
            {
                case NoteCardData card:
                {
                    var go = new GameObject($"Note_{card.Id}");
                    var view = go.AddComponent<NoteCardView>();
                    view.Initialize(card, CanvasContainer);
                    objectViews[card.Id] = view;
                    break;
                }
                case ImageObjectData image:
                {
                    var go = new GameObject($"Image_{image.Id}");
                    var view = go.AddComponent<ImageObjectView>();
                    view.Initialize(image, CanvasContainer);
                    objectViews[image.Id] = view;
                    break;
                }
                case DrawingObjectData drawing:
                {
                    var go = new GameObject($"Drawing_{drawing.Id}");
                    var view = go.AddComponent<DrawingObjectView>();
                    view.Initialize(drawing, CanvasContainer);
                    objectViews[drawing.Id] = view;
                    break;
                }
            }
        }

        void SpawnLink(LinkData link)
        {
            var fromRect = GetRectTransform(link.FromObjectId);
            var toRect = GetRectTransform(link.ToObjectId);
            if (fromRect == null || toRect == null) return;

            var go = new GameObject($"Link_{link.Id}");
            var view = go.AddComponent<LinkView>();
            view.Initialize(link, CanvasContainer, fromRect, toRect);
            linkViews[link.Id] = view;
        }

        RectTransform GetRectTransform(string objectId)
        {
            if (!objectViews.TryGetValue(objectId, out var view) || view == null) return null;
            return view switch
            {
                NoteCardView n => n.RectTransform,
                ImageObjectView i => i.RectTransform,
                DrawingObjectView d => d.RectTransform,
                _ => null
            };
        }

        // ── Mutation ───────────────────────────────────────────────────────────

        public NoteCardData AddNoteCard(System.Numerics.Vector2 position)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new NoteCardData { Position = position, Title = "Заметка" };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public ImageObjectData AddImage(System.Numerics.Vector2 position, byte[] imageBytes)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new ImageObjectData { Position = position, ImageBytes = imageBytes };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public DrawingObjectData AddDrawing(System.Numerics.Vector2 position, int pixelWidth, int pixelHeight)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new DrawingObjectData(pixelWidth, pixelHeight) { Position = position };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public LinkData AddLink(string fromObjectId, string toObjectId)
        {
            var page = documentController.ActivePage;
            if (page == null || fromObjectId == toObjectId) return null;
            var data = new LinkData { FromObjectId = fromObjectId, ToObjectId = toObjectId };
            page.Links.Add(data);
            SpawnLink(data);
            return data;
        }

        public void RemoveObject(string objectId)
        {
            var page = documentController.ActivePage;
            if (page == null) return;

            page.Objects.RemoveAll(o => o.Id == objectId);
            var orphanLinks = page.Links.Where(l => l.FromObjectId == objectId || l.ToObjectId == objectId).ToList();
            foreach (var link in orphanLinks)
                RemoveLink(link.Id);

            if (objectViews.TryGetValue(objectId, out var view))
            {
                if (view != null) Destroy(view.gameObject);
                objectViews.Remove(objectId);
            }
            OnSelectionCleared?.Invoke();
        }

        public void RemoveLink(string linkId)
        {
            var page = documentController.ActivePage;
            if (page == null) return;
            page.Links.RemoveAll(l => l.Id == linkId);
            if (linkViews.TryGetValue(linkId, out var view))
            {
                if (view != null) Destroy(view.gameObject);
                linkViews.Remove(linkId);
            }
        }

        public MonoBehaviour GetView(string objectId)
        {
            objectViews.TryGetValue(objectId, out var view);
            return view;
        }

        public void RefreshLinksFor(string objectId)
        {
            foreach (var link in linkViews.Values)
                if (link.LinkId != null) link.UpdateTransform();
        }

        // ── Pan / Zoom ─────────────────────────────────────────────────────────

        public void Pan(Vector2 screenDelta)
        {
            CanvasContainer.anchoredPosition += screenDelta;
            SaveCameraState();
        }

        public void Zoom(float scrollDelta, Vector2 screenPivot)
        {
            float newScale = Mathf.Clamp(CanvasContainer.localScale.x + scrollDelta, 0.25f, 3f);
            CanvasContainer.localScale = new Vector3(newScale, newScale, 1f);
            SaveCameraState();
        }

        void SaveCameraState()
        {
            var page = documentController?.ActivePage;
            if (page == null) return;
            page.CameraPan = new System.Numerics.Vector2(CanvasContainer.anchoredPosition.x, CanvasContainer.anchoredPosition.y);
            page.CameraZoom = CanvasContainer.localScale.x;
        }
    }
}
