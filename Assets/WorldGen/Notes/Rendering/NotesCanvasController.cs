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
        public CanvasInteractionController interactionController;

        public RectTransform CanvasContainer { get; private set; }

        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();
        readonly Dictionary<string, LinkAnchorController> linkAnchors = new Dictionary<string, LinkAnchorController>();

        public event System.Action OnSelectionCleared;

        void EnsureContainer()
        {
            if (CanvasContainer != null) return;
            var containerGO = new GameObject("CanvasContainer");
            containerGO.transform.SetParent(viewport != null ? viewport : transform, false);
            CanvasContainer = containerGO.AddComponent<RectTransform>();
            CanvasContainer.anchorMin = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchorMax = new Vector2(0.5f, 0.5f);
            CanvasContainer.pivot = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchoredPosition = Vector2.zero;
            CanvasContainer.sizeDelta = Vector2.zero;
        }

        // AddComponent<NotesCanvasController>() runs Awake/OnEnable synchronously before the
        // caller can assign documentController/viewport/interactionController — subscribing in
        // OnEnable would silently see a null documentController and never subscribe at all, so
        // page switches would never re-render. Callers must use Initialize, not the raw fields.
        public void Initialize(NotesDocumentController docController, RectTransform viewportRect, CanvasInteractionController interaction)
        {
            documentController = docController;
            viewport = viewportRect;
            interactionController = interaction;
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
            EnsureContainer();
            foreach (var view in objectViews.Values)
                if (view != null) Destroy(view.gameObject);
            objectViews.Clear();
            foreach (var link in linkViews.Values)
                if (link != null) Destroy(link.gameObject);
            linkViews.Clear();
            foreach (var anchors in linkAnchors.Values)
                if (anchors != null) Destroy(anchors.gameObject);
            linkAnchors.Clear();
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
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[card.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
                case ImageObjectData image:
                {
                    var go = new GameObject($"Image_{image.Id}");
                    var view = go.AddComponent<ImageObjectView>();
                    view.Initialize(image, CanvasContainer);
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[image.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
                case DrawingObjectData drawing:
                {
                    var go = new GameObject($"Drawing_{drawing.Id}");
                    var view = go.AddComponent<DrawingObjectView>();
                    view.Initialize(drawing, CanvasContainer);
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[drawing.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
            }
        }

        void AddLinkAnchors(string objectId, RectTransform hostRect)
        {
            if (interactionController == null) return;
            var anchorGO = new GameObject($"LinkAnchors_{objectId}");
            anchorGO.transform.SetParent(CanvasContainer, false);
            var anchors = anchorGO.AddComponent<LinkAnchorController>();
            anchors.Initialize(objectId, hostRect, CanvasContainer, interactionController);
            linkAnchors[objectId] = anchors;
        }

        /// <summary>True if screenPos lands on any currently-visible link-creation anchor dot —
        /// used by CanvasInteractionController to suppress the active tool's own click action
        /// (e.g. Note/Drawing/Image creation) so it doesn't fire at the same time as an
        /// anchor-drag gesture starting on the same press.</summary>
        public bool IsScreenPointOverLinkAnchor(Vector2 screenPos, Camera uiCamera)
        {
            foreach (var anchors in linkAnchors.Values)
                if (anchors != null && anchors.IsScreenPointOverDot(screenPos, uiCamera))
                    return true;
            return false;
        }

        void WireEvents(string objectId,
            System.Action<(System.Action<string> onClicked, System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> onDragEnded)> subscribe)
        {
            if (interactionController == null) return;
            subscribe((
                onClicked: id => interactionController.HandleObjectClicked(id),
                onDragEnded: (id, oldPos, newPos) => interactionController.HandleObjectDragEnded(id, oldPos, newPos)
            ));
        }

        void SpawnLink(LinkData link)
        {
            var fromRect = GetRectTransform(link.FromObjectId);
            var toRect = GetRectTransform(link.ToObjectId);
            if (fromRect == null || toRect == null) return;

            var go = new GameObject($"Link_{link.Id}");
            var view = go.AddComponent<LinkView>();
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            view.Initialize(link, CanvasContainer, fromRect, toRect, cam);
            linkViews[link.Id] = view;
        }

        RectTransform GetRectTransform(string objectId)
        {
            if (!objectViews.TryGetValue(objectId, out var view) || view == null) return null;
            return RectOf(view);
        }

        static RectTransform RectOf(MonoBehaviour view) => view switch
        {
            NoteCardView n => n.RectTransform,
            ImageObjectView i => i.RectTransform,
            DrawingObjectView d => d.RectTransform,
            _ => null
        };

        /// <summary>True if screenPos lands on any currently-spawned object view's rect — used
        /// by CanvasInteractionController to avoid starting a canvas pan under an object drag.</summary>
        public bool IsScreenPointOverObject(Vector2 screenPos, Camera uiCamera)
        {
            foreach (var view in objectViews.Values)
            {
                var rt = view != null ? RectOf(view) : null;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, uiCamera))
                    return true;
            }
            return false;
        }

        /// <summary>Returns the objectId of the topmost spawned object view whose rect contains
        /// screenPos, excluding excludeObjectId (the link-drag source) — used by
        /// LinkAnchorController to find a drop target when an anchor drag is released.</summary>
        public string FindObjectAt(Vector2 screenPos, Camera uiCamera, string excludeObjectId)
        {
            foreach (var kvp in objectViews)
            {
                if (kvp.Key == excludeObjectId) continue;
                var rt = kvp.Value != null ? RectOf(kvp.Value) : null;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, uiCamera))
                    return kvp.Key;
            }
            return null;
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
            if (linkAnchors.TryGetValue(objectId, out var anchors))
            {
                if (anchors != null) Destroy(anchors.gameObject);
                linkAnchors.Remove(objectId);
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

        /// <summary>Returns the linkId of the topmost link whose curve contains screenPos, or
        /// null — used by CanvasInteractionController for click-to-select.</summary>
        public string FindLinkAt(Vector2 screenPos, Camera uiCamera)
        {
            foreach (var kvp in linkViews)
                if (kvp.Value != null && kvp.Value.ContainsScreenPoint(screenPos, uiCamera))
                    return kvp.Key;
            return null;
        }

        /// <summary>Marks exactly one link (by id, or none if null) as selected, showing its bend
        /// handle and highlight color.</summary>
        public void SetSelectedLink(string linkId)
        {
            foreach (var kvp in linkViews)
                kvp.Value?.SetSelected(kvp.Key == linkId);
        }

        public LinkData FindLinkData(string linkId)
        {
            var page = documentController?.ActivePage;
            return page?.Links.FirstOrDefault(l => l.Id == linkId);
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

        /// <summary>Sets CanvasContainer's zoom to newScale (clamped to [0.25, 3]) while keeping
        /// the canvas point currently under screenPos visually fixed on screen — used by the
        /// click-drag Zoom tool (unlike Zoom() above, which always scales around the viewport
        /// center, used by scroll-wheel zoom).</summary>
        public void ZoomAroundScreenPoint(float newScale, Vector2 screenPos, Camera uiCamera)
        {
            float oldScale = CanvasContainer.localScale.x;
            float clampedScale = Mathf.Clamp(newScale, 0.25f, 3f);
            if (Mathf.Approximately(oldScale, clampedScale)) return;

            var parentRect = (RectTransform)CanvasContainer.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, uiCamera, out var pivotInParent);

            Vector2 offsetFromOrigin = pivotInParent - CanvasContainer.anchoredPosition;
            float factor = clampedScale / oldScale;
            CanvasContainer.anchoredPosition += offsetFromOrigin * (1f - factor);
            CanvasContainer.localScale = new Vector3(clampedScale, clampedScale, 1f);
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
