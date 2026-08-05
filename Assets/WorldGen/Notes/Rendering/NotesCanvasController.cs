using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    public enum CanvasMode { Inline, Expanded }

    /// <summary>
    /// Renders ONE canvas BLOCK into ONE frame — not "the active page", which is what it rendered until Р4.
    /// That change is the arc: a page can hold several boards, and the same board can be drawn twice at once
    /// (inline in the page and expanded in the other pane), so nothing here may reach for "the current page"
    /// and nothing here owns state. Both copies read the same DocBlock; neither is the original.
    ///
    /// ONE CONTROLLER PER BOARD. The dictionaries below are keyed by object id and shared across the whole
    /// controller, which is correct exactly while there is one controller per block — a single controller
    /// serving two boards would collide on the first duplicated id.
    ///
    /// IT DOES NOT KNOW WHAT UNDO IS. BeforeMutation is called immediately before every change to the block;
    /// whoever built this canvas supplies it (DocumentPageView for an inline board, CanvasSurfaceHost for an
    /// expanded one), because the page's history belongs to the page and a canvas has no way to find its own.
    /// </summary>
    public class NotesCanvasController : MonoBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Viewport RectTransform that clips the canvas content (mask/scroll area).")]
        public RectTransform viewport;
        public CanvasInteractionController interactionController;

        DocBlock block;
        CanvasMode mode;

        public DocBlock Block => block;
        public CanvasMode Mode => mode;

        /// <summary>Remember the page as it is NOW — called immediately BEFORE any change to the block, never
        /// after: the snapshot is what Ctrl+Z goes back to.</summary>
        public System.Action BeforeMutation;
        /// <summary>The document changed: mark the project dirty and let the page redraw.</summary>
        public System.Action AfterMutation;

        public RectTransform CanvasContainer { get; private set; }

        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();
        readonly Dictionary<string, LinkAnchorController> linkAnchors = new Dictionary<string, LinkAnchorController>();
        readonly Dictionary<string, ObjectResizeController> resizeControllers = new Dictionary<string, ObjectResizeController>();

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

        // AddComponent<NotesCanvasController>() runs Awake/OnEnable synchronously before the caller can
        // assign viewport/interactionController, so callers must use Initialize, not the raw fields. There is
        // nothing to subscribe to any more: this controller used to listen for "the active page changed" and
        // redraw itself, which is exactly the reach-for-the-current-page the class comment now forbids. Its
        // owner rebuilds it instead.
        public void Initialize(DocBlock canvasBlock, RectTransform frame, CanvasInteractionController interaction, CanvasMode canvasMode)
        {
            block = canvasBlock;
            viewport = frame;
            interactionController = interaction;
            mode = canvasMode;
            RebuildFromBlock();
        }

        // ── Rebuild ────────────────────────────────────────────────────────────

        public void RebuildFromBlock()
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
            foreach (var resize in resizeControllers.Values)
                if (resize != null) Destroy(resize.gameObject);
            resizeControllers.Clear();
            OnSelectionCleared?.Invoke();

            if (block == null) return;

            ApplyCamera();

            if (block.CanvasObjects != null)
                foreach (var obj in block.CanvasObjects)
                    SpawnView(obj);

            if (block.CanvasLinks != null)
                foreach (var link in block.CanvasLinks)
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
                    view.Initialize(card, CanvasContainer, editable: mode == CanvasMode.Expanded);
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragStarted += ev.onDragStarted; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[card.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
                case ImageObjectData image:
                {
                    var go = new GameObject($"Image_{image.Id}");
                    var view = go.AddComponent<ImageObjectView>();
                    view.Initialize(image, CanvasContainer);
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragStarted += ev.onDragStarted; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[image.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
                case DrawingObjectData drawing:
                {
                    var go = new GameObject($"Drawing_{drawing.Id}");
                    var view = go.AddComponent<DrawingObjectView>();
                    view.Initialize(drawing, CanvasContainer);
                    view.interactionController = interactionController;
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragStarted += ev.onDragStarted; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[drawing.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
            }
        }

        /// <summary>The hover dots that start a link drag. NOT BUILT INLINE, which is the same argument the
        /// inline card makes about its input field: an anchor dot and a resize handle each carry their own
        /// IDragHandler, so they answer the mouse whatever CanvasInteractionController's Update does or does
        /// not do. Making the reduced mode real means not building them, not disabling a poll.</summary>
        void AddLinkAnchors(string objectId, RectTransform hostRect)
        {
            if (interactionController == null || mode != CanvasMode.Expanded) return;
            var anchorGO = new GameObject($"LinkAnchors_{objectId}");
            anchorGO.transform.SetParent(CanvasContainer, false);
            var anchors = anchorGO.AddComponent<LinkAnchorController>();
            anchors.Initialize(objectId, hostRect, CanvasContainer, interactionController);
            linkAnchors[objectId] = anchors;
        }

        /// <summary>The four corner handles of the selected object — expanded view only, for the reason
        /// AddLinkAnchors gives.</summary>
        void AddResizeHandles(string objectId, RectTransform hostRect)
        {
            if (interactionController == null || mode != CanvasMode.Expanded) return;
            var resizeGO = new GameObject($"ResizeHandles_{objectId}");
            resizeGO.transform.SetParent(CanvasContainer, false);
            var resize = resizeGO.AddComponent<ObjectResizeController>();
            resize.Initialize(objectId, hostRect, CanvasContainer, interactionController);
            resizeControllers[objectId] = resize;
        }

        /// <summary>True if screenPos lands on any currently-visible resize handle — used by
        /// CanvasInteractionController to suppress the active tool's own click action, same
        /// reason as IsScreenPointOverLinkAnchor.</summary>
        public bool IsScreenPointOverResizeHandle(Vector2 screenPos, Camera uiCamera)
        {
            foreach (var resize in resizeControllers.Values)
                if (resize != null && resize.IsScreenPointOverHandle(screenPos, uiCamera))
                    return true;
            return false;
        }

        /// <summary>Shows resize handles on exactly the selected object (or none if objectId is
        /// null) — resize handles only appear for the current selection, unlike link anchor dots
        /// which appear on any hover.</summary>
        public void SetSelectedObject(string objectId)
        {
            foreach (var kvp in resizeControllers)
                kvp.Value?.SetSelected(kvp.Key == objectId);

            // ЗДЕСЬ ЖЕ КАРТОЧКА ВЫХОДИТ ИЗ ПРАВКИ. Правку открывает двойной клик, а закрывает уход
            // выделения — клик по пустому месту доски или по другому объекту. Отдельного «клика мимо»
            // для этого заводить не нужно: снятие выделения и есть тот самый клик.
            foreach (var kvp in objectViews)
                if (kvp.Key != objectId && kvp.Value is NoteCardView card) card.ExitEditMode();
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
            System.Action<(System.Action<string> onClicked, System.Action<string> onDragStarted, System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> onDragEnded)> subscribe)
        {
            if (interactionController == null) return;
            subscribe((
                onClicked: id => interactionController.HandleObjectClicked(id),
                onDragStarted: id => interactionController.HandleObjectDragStarted(id),
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

        /// <summary>The block's own lists, created on demand. A block loaded from an older project — or one
        /// built by anything that forgot — carries nulls here, and a mutator that assumed otherwise would
        /// throw on the DM's first card.</summary>
        List<CanvasObjectData> Objects
        {
            get
            {
                if (block.CanvasObjects == null) block.CanvasObjects = new List<CanvasObjectData>();
                return block.CanvasObjects;
            }
        }

        List<LinkData> Links
        {
            get
            {
                if (block.CanvasLinks == null) block.CanvasLinks = new List<LinkData>();
                return block.CanvasLinks;
            }
        }

        public NoteCardData AddNoteCard(System.Numerics.Vector2 position)
        {
            if (block == null) return null;
            BeforeMutation?.Invoke();
            // Новая карточка рождается в том же стиле, что выбрали для прошлой: ДМ красит доску по
            // смыслу («красные — засады»), и заново выбирать цвет на каждой карточке — работа впустую.
            var data = new NoteCardData
            {
                Position = position,
                Title = "Заметка",
                FrameColorIndex = NotesUserPrefs.CardFrameColorIndex,
                FontSize = NotesUserPrefs.CardFont,
            };
            Objects.Add(data);
            SpawnView(data);
            AfterMutation?.Invoke();
            return data;
        }

        public ImageObjectData AddImage(System.Numerics.Vector2 position, byte[] imageBytes)
        {
            if (block == null) return null;
            BeforeMutation?.Invoke();
            var data = new ImageObjectData { Position = position, ImageBytes = imageBytes };
            Objects.Add(data);
            SpawnView(data);
            AfterMutation?.Invoke();
            return data;
        }

        public DrawingObjectData AddDrawing(System.Numerics.Vector2 position, int pixelWidth, int pixelHeight)
        {
            if (block == null) return null;
            BeforeMutation?.Invoke();
            var data = new DrawingObjectData(pixelWidth, pixelHeight) { Position = position };
            Objects.Add(data);
            SpawnView(data);
            AfterMutation?.Invoke();
            return data;
        }

        public LinkData AddLink(string fromObjectId, string toObjectId)
        {
            if (block == null || fromObjectId == toObjectId) return null;
            BeforeMutation?.Invoke();
            var data = new LinkData { FromObjectId = fromObjectId, ToObjectId = toObjectId };
            Links.Add(data);
            SpawnLink(data);
            AfterMutation?.Invoke();
            return data;
        }

        /// <summary>Removes an object and every link that touched it — ONE undo step for the whole cascade.
        /// The orphaned links go through RemoveLinkCore rather than RemoveLink for exactly that reason: a
        /// nested BeforeMutation would snapshot a half-deleted board, and the first Ctrl+Z would restore THAT
        /// — an object still gone with its links back.</summary>
        public void RemoveObject(string objectId)
        {
            if (block == null) return;
            BeforeMutation?.Invoke();
            RemoveObjectCore(objectId);
            AfterMutation?.Invoke();
        }

        void RemoveObjectCore(string objectId)
        {
            Objects.RemoveAll(o => o.Id == objectId);
            var orphanLinks = Links.Where(l => l.FromObjectId == objectId || l.ToObjectId == objectId).ToList();
            foreach (var link in orphanLinks)
                RemoveLinkCore(link.Id);

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
            if (resizeControllers.TryGetValue(objectId, out var resize))
            {
                if (resize != null) Destroy(resize.gameObject);
                resizeControllers.Remove(objectId);
            }
            OnSelectionCleared?.Invoke();
        }

        public void RemoveLink(string linkId)
        {
            if (block == null) return;
            BeforeMutation?.Invoke();
            RemoveLinkCore(linkId);
            AfterMutation?.Invoke();
        }

        void RemoveLinkCore(string linkId)
        {
            Links.RemoveAll(l => l.Id == linkId);
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
            => block?.CanvasLinks?.FirstOrDefault(l => l.Id == linkId);

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

        /// <summary>Re-reads the given object's current Position/Size from its data into its live
        /// view — used during a resize drag instead of duplicating
        /// the per-view-type switch at every call site.</summary>
        public void RefreshView(string objectId)
        {
            if (!objectViews.TryGetValue(objectId, out var view) || view == null) return;
            switch (view)
            {
                case NoteCardView n: n.Refresh(); break;
                case ImageObjectView i: i.Refresh(); break;
                case DrawingObjectView d: d.Refresh(); break;
            }
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

        /// <summary>Where the expanded view was left looking. DELIBERATELY OUTSIDE THE HISTORY: it calls
        /// neither BeforeMutation nor AfterMutation, because pan and zoom are how the DM LOOKS at a board, not
        /// a change to what the board is. Recording them would fill the page's one undo stack with camera
        /// moves and make Ctrl+Z after a paragraph scroll a board instead of restoring the text. This is not
        /// an oversight to be tidied up later.</summary>
        void SaveCameraState()
        {
            if (block == null || CanvasContainer == null) return;
            var pan = new System.Numerics.Vector2(CanvasContainer.anchoredPosition.x, CanvasContainer.anchoredPosition.y);
            float zoom = CanvasContainer.localScale.x;

            // WHICHEVER VIEW THIS IS. The two are stored apart (see DocBlock.CanvasPan's own doc): the frame
            // in the page's flow and the expanded pane are different tasks, and one shared camera made the
            // small one unusable. Writing the flag here — and ONLY here — is what "the DM has pointed this
            // view themselves" means: fitting never touches it, so a board nobody has aimed keeps re-fitting
            // as its contents change, and the moment they aim it, it stays where they put it.
            if (mode == CanvasMode.Expanded)
            {
                block.CanvasPan = pan;
                block.CanvasZoom = zoom;
                block.CanvasViewSet = true;
            }
            else
            {
                block.CanvasInlinePan = pan;
                block.CanvasInlineZoom = zoom;
                block.CanvasInlineViewSet = true;
            }
        }

        // ── smoothed wheel zoom ────────────────────────────────────────────────

        /// <summary>How fast the animated zoom closes the remaining distance, per second, as the rate of an
        /// exponential approach: at 12 the gap is down to a few percent in about a fifth of a second. An
        /// exponential rather than a fixed duration because wheel notches ARRIVE WHILE ONE IS RUNNING — a
        /// duration-based tween has to decide whether to restart its clock (which stalls a fast scroll) or
        /// keep it (which makes the second notch land in a fraction of the time). Approaching a target that
        /// simply moves has no such question: three quick notches read as one continuous glide.</summary>
        const float ZoomSmoothingPerSecond = 12f;

        float zoomTarget;
        Vector2 zoomAnchorScreen;
        Camera zoomAnchorCamera;
        bool zoomAnimating;

        /// <summary>Where the next wheel notch should multiply FROM: the zoom being animated towards if one is
        /// running, otherwise what is on screen. Reading the live scale mid-animation would make every notch
        /// after the first one smaller than the last — the DM spins the wheel and the board slows down.</summary>
        public float ZoomTarget => zoomAnimating ? zoomTarget : (CanvasContainer != null ? CanvasContainer.localScale.x : 1f);

        /// <summary>Zooms to `targetScale` over the next few frames instead of in one jump, keeping the point
        /// under `screenPos` fixed throughout. The anchor is stored as a SCREEN point and re-applied every
        /// frame of the animation, which is what makes a smoothed zoom still land where the cursor is: the
        /// canvas point under that pixel changes as the scale changes, so a canvas-space anchor captured once
        /// would drift.</summary>
        public void ZoomTowards(float targetScale, Vector2 screenPos, Camera uiCamera)
        {
            if (CanvasContainer == null) return;
            zoomTarget = Mathf.Clamp(targetScale, 0.25f, 3f);
            zoomAnchorScreen = screenPos;
            zoomAnchorCamera = uiCamera;
            zoomAnimating = true;
        }

        /// <summary>Drops any running wheel animation. Called when a gesture takes DIRECT control of the zoom —
        /// the Zoom tool's drag — because that drag writes the scale from its own Update and the animation
        /// writes it from LateUpdate, so the later writer would win every frame and the drag would do nothing
        /// visible.</summary>
        public void CancelZoomAnimation() => zoomAnimating = false;

        /// <summary>Forgets that the DM ever aimed THIS view, so the board fits itself again — and goes on
        /// fitting itself as its contents change, until they aim it once more. The way back from a pan or a
        /// zoom that lost the contents off the edge of the frame.
        ///
        /// Clears the flag rather than computing a fit and storing it: those are different states, and only
        /// the flag one keeps re-fitting. LateUpdate does the arithmetic on the next frame, which also means
        /// this is correct while the rect is still being laid out.</summary>
        public void ResetViewToFit()
        {
            if (block == null) return;
            CancelZoomAnimation();
            if (mode == CanvasMode.Expanded) block.CanvasViewSet = false;
            else block.CanvasInlineViewSet = false;
            lastFitViewport = Vector2.zero;
        }

        void StepZoomAnimation()
        {
            if (!zoomAnimating || CanvasContainer == null) { zoomAnimating = false; return; }

            float current = CanvasContainer.localScale.x;
            // unscaledDeltaTime, not deltaTime: a UI gesture must not slow down with Time.timeScale, and
            // nothing in this app sets it to 1 on purpose — it is simply never touched, which is exactly the
            // kind of assumption that breaks silently later.
            float t = 1f - Mathf.Exp(-ZoomSmoothingPerSecond * Time.unscaledDeltaTime);
            float next = Mathf.Lerp(current, zoomTarget, t);

            // Snap and stop rather than approach forever: an exponential never actually arrives, and every
            // frame of it costs a SaveCameraState write plus a layout-affecting scale change.
            if (Mathf.Abs(zoomTarget - next) < 0.002f)
            {
                next = zoomTarget;
                zoomAnimating = false;
            }
            ZoomAroundScreenPoint(next, zoomAnchorScreen, zoomAnchorCamera);
        }

        /// <summary>Points the camera at whatever this view should be looking at: the stored one if the DM has
        /// ever aimed it, otherwise a fit that puts every object on screen at once.
        ///
        /// A FIT MAY HAVE TO WAIT A FRAME, which is the whole reason `awaitingFit` exists rather than this
        /// being three lines inside RebuildFromBlock. The fit needs the viewport's PIXEL SIZE, and a
        /// RectTransform that was created or resized this frame reports a stale (often zero) rect until uGUI's
        /// deferred layout pass has run — and both callers rebuild during exactly that window: the inline
        /// frame is built inside the page's row rebuild, and CanvasSurfaceHost's Show can run from
        /// WorkspaceBuilder.Awake, before uGUI has laid the pane out even once. Fitting against a zero
        /// viewport yields the MinZoom floor and a board that looks empty, so a zero rect defers instead of
        /// guessing. LateUpdate retries until the rect is real.</summary>
        void ApplyCamera()
        {
            if (block == null || CanvasContainer == null) return;

            // CLEARED FIRST, ALWAYS. The expanded controller is REUSED as the DM switches boards, so a fit
            // measured against the previous board would otherwise still be recorded — and LateUpdate, seeing
            // the same viewport size, would decide the new board was already fitted and leave it wearing the
            // old one's camera. Zero means "no fit applied", which is exactly true at this instant.
            lastFitViewport = Vector2.zero;

            bool viewSet = mode == CanvasMode.Expanded ? block.CanvasViewSet : block.CanvasInlineViewSet;
            if (viewSet)
            {
                var storedPan = mode == CanvasMode.Expanded ? block.CanvasPan : block.CanvasInlinePan;
                float storedZoom = mode == CanvasMode.Expanded ? block.CanvasZoom : block.CanvasInlineZoom;
                if (storedZoom < 0.0001f) storedZoom = 1f;
                CanvasContainer.anchoredPosition = new Vector2(storedPan.X, storedPan.Y);
                CanvasContainer.localScale = new Vector3(storedZoom, storedZoom, 1f);
                return;
            }

            TryFitNow();
        }

        /// <summary>True when the viewport was measurable and the fit was applied. Deliberately does NOT write
        /// the result back to the block: a fit is a DEFAULT, not a decision, and storing it would consume the
        /// "never aimed" state that makes the board re-fit as objects are added to it.</summary>
        bool TryFitNow()
        {
            if (block == null || CanvasContainer == null || viewport == null) return false;
            var size = viewport.rect.size;
            if (size.x < 1f || size.y < 1f) return false;

            CanvasFit.Compute(block.CanvasObjects, new System.Numerics.Vector2(size.x, size.y),
                              out var pan, out float zoom);
            CanvasContainer.anchoredPosition = new Vector2(pan.X, pan.Y);
            CanvasContainer.localScale = new Vector3(zoom, zoom, 1f);
            lastFitViewport = size;
            return true;
        }

        /// <summary>The viewport size the current fit was computed against, or zero when no fit has been
        /// applied — which is both "the rect was not measurable yet" and "this view is showing a stored
        /// camera instead". One field covers the retry and the re-fit; see LateUpdate.</summary>
        Vector2 lastFitViewport;

        /// <summary>Re-fits an UNAIMED board whenever the size it was fitted against stops being the size it
        /// has. Two cases, one rule: the first frame, where the rect was still zero when RebuildFromBlock ran
        /// (see ApplyCamera), and a later resize — the inline frame has a grip the DM drags, and the expanded
        /// one follows the divider and the window. A board the DM HAS aimed is left alone: they chose that
        /// view, and having it snap back on every window resize would be the bug this method is preventing,
        /// in the other direction.</summary>
        void LateUpdate()
        {
            StepZoomAnimation();

            if (block == null || CanvasContainer == null || viewport == null) return;
            bool viewSet = mode == CanvasMode.Expanded ? block.CanvasViewSet : block.CanvasInlineViewSet;
            if (viewSet) return;

            var size = viewport.rect.size;
            if (size.x < 1f || size.y < 1f) return;
            if (Mathf.Approximately(size.x, lastFitViewport.x) && Mathf.Approximately(size.y, lastFitViewport.y)) return;
            TryFitNow();
        }
    }
}
