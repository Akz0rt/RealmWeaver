# Notes Editor — Zoom Tool + Object Resize Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a click-and-drag "Zoom" tool (Photoshop-style: drag right to zoom in, left to zoom out, pivoting around the press point) and corner-drag resizing for note cards/images/drawings, per `docs/superpowers/specs/2026-07-03-notes-editor-zoom-tool-and-object-resize-design.md`.

**Architecture:** No new subsystems. The zoom tool adds one new pivot-aware zoom method to `NotesCanvasController` plus a press/drag gesture in `CanvasInteractionController`. Object resize adds one new per-object component, `ObjectResizeController`, mirroring the existing `LinkAnchorController` pattern (per-object corner handles, zoom-counter-scaled, wired into `SpawnView`/cleanup the same way).

**Tech Stack:** Unity 6000.3.2f1, Built-in RP, New Input System (`Mouse.current`/`Keyboard.current`), legacy `UnityEngine.UI` (no TextMeshPro), code-only UI construction (`new GameObject()` + `AddComponent<>()`), C#.

## Global Constraints

- **New Input System only** — `Mouse.current`/`Keyboard.current`. Never `UnityEngine.Input`.
- **No TextMeshPro** — `Text`/`Image`/`Button` from `UnityEngine.UI` only.
- **UI construction is code-only** — no Editor-authored prefabs.
- **`[ContextMenu]` self-tests** for logic verifiable without manual interaction, matching project convention (`Debug.Log("Self-Test X: PASS/FAIL")`).
- **No placeholders in code** — every method has a real implementation.
- **Zoom range stays `[0.25, 3.0]`**, matching the existing scroll-wheel zoom clamp.
- **Minimum object size on resize: 40×40 canvas units.**
- **Resize only changes display size** — does not resample `ImageObjectData.ImageBytes` / `DrawingObjectData.PixelDataPng`.
- **Neither zoom nor resize live on the undo stack for zoom itself** — resize IS undoable (symmetric to move); zoom/pan are not, matching existing behavior.
- **Out of scope (do not touch):** aspect-ratio-locked resize, pixel resampling on resize, Link tool internals beyond the existing `RefreshLinksFor` call, undo for pan/zoom.

---

### Task 1: Zoom tool (click-drag, pivots around press point)

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs` (add `ZoomAroundScreenPoint`)
- Modify: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs` (`NotesTool.Zoom`, press/drag/release wiring)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs` (`ToolDefs` entry)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs` (`DrawZoom` + switch case)

**Interfaces:**
- Consumes: nothing new — `NotesCanvasController.CanvasContainer` (existing public getter).
- Produces: `NotesCanvasController.ZoomAroundScreenPoint(float newScale, Vector2 screenPos, Camera uiCamera)`. `NotesTool.Zoom` enum value. Toolbar shows a 6th button (magnifying glass) after Изображение.

- [ ] **Step 1: Add `ZoomAroundScreenPoint` to `NotesCanvasController`**

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, replace:

```csharp
        public void Zoom(float scrollDelta, Vector2 screenPivot)
        {
            float newScale = Mathf.Clamp(CanvasContainer.localScale.x + scrollDelta, 0.25f, 3f);
            CanvasContainer.localScale = new Vector3(newScale, newScale, 1f);
            SaveCameraState();
        }
```

with:

```csharp
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
```

- [ ] **Step 2: Add `NotesTool.Zoom` and the press/drag/release gesture in `CanvasInteractionController`**

In `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`, replace:

```csharp
    public enum NotesTool { Select, Note, Drawing, Image }
```

with:

```csharp
    public enum NotesTool { Select, Note, Drawing, Image, Zoom }
```

Replace:

```csharp
        string paintingDrawingObjectId;
        string selectedObjectId;
        string selectedLinkId;
        bool panning;
        Vector2 lastPanScreenPos;
```

with:

```csharp
        string paintingDrawingObjectId;
        string selectedObjectId;
        string selectedLinkId;
        bool panning;
        Vector2 lastPanScreenPos;
        bool zooming;
        Vector2 zoomStartScreenPos;
        float zoomStartScale;
        const float ZoomDragSensitivity = 0.005f;
```

Replace:

```csharp
            if (Mouse.current.leftButton.wasPressedThisFrame)
                HandlePress();
            else if (Mouse.current.leftButton.isPressed && panning)
                HandlePan();
            else if (Mouse.current.leftButton.isPressed && paintingDrawingObjectId != null)
                HandlePaintDrag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                HandleRelease();
```

with:

```csharp
            if (Mouse.current.leftButton.wasPressedThisFrame)
                HandlePress();
            else if (Mouse.current.leftButton.isPressed && panning)
                HandlePan();
            else if (Mouse.current.leftButton.isPressed && zooming)
                HandleZoomDrag();
            else if (Mouse.current.leftButton.isPressed && paintingDrawingObjectId != null)
                HandlePaintDrag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                HandleRelease();
```

Replace the `switch (ActiveTool)` block's `NotesTool.Image` case (its last case) — find:

```csharp
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                        undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
                    break;
            }
        }
```

with:

```csharp
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                        undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
                    break;
                case NotesTool.Zoom:
                    zooming = true;
                    zoomStartScreenPos = screenPos;
                    zoomStartScale = canvasController.CanvasContainer.localScale.x;
                    break;
            }
        }
```

Add a new `HandleZoomDrag` method right after `HandlePan`:

```csharp
        void HandlePan()
        {
            var screenPos = Mouse.current.position.ReadValue();
            Vector2 delta = screenPos - lastPanScreenPos;
            lastPanScreenPos = screenPos;
            canvasController.Pan(delta);
        }

        void HandleZoomDrag()
        {
            var screenPos = Mouse.current.position.ReadValue();
            float deltaX = screenPos.x - zoomStartScreenPos.x;
            float newScale = zoomStartScale * Mathf.Pow(2f, deltaX * ZoomDragSensitivity);
            canvasController.ZoomAroundScreenPoint(newScale, zoomStartScreenPos, uiCamera);
        }
```

Replace `HandleRelease`:

```csharp
        void HandleRelease()
        {
            panning = false;
            if (paintingDrawingObjectId != null)
            {
                if (canvasController.GetView(paintingDrawingObjectId) is DrawingObjectView view)
                    view.CommitToData();
                paintingDrawingObjectId = null;
            }
        }
```

with:

```csharp
        void HandleRelease()
        {
            panning = false;
            zooming = false;
            if (paintingDrawingObjectId != null)
            {
                if (canvasController.GetView(paintingDrawingObjectId) is DrawingObjectView view)
                    view.CommitToData();
                paintingDrawingObjectId = null;
            }
        }
```

- [ ] **Step 3: Add the Zoom toolbar button**

In `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs`, replace:

```csharp
        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
        };
```

with:

```csharp
        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
            (NotesTool.Zoom, "Лупа"),
        };
```

- [ ] **Step 4: Draw the magnifying-glass icon**

In `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs`, replace:

```csharp
            switch (tool)
            {
                case NotesTool.Select: DrawCursor(tex, size); break;
                case NotesTool.Note: DrawNote(tex, size); break;
                case NotesTool.Drawing: DrawPencil(tex, size); break;
                case NotesTool.Image: DrawPicture(tex, size); break;
            }
```

with:

```csharp
            switch (tool)
            {
                case NotesTool.Select: DrawCursor(tex, size); break;
                case NotesTool.Note: DrawNote(tex, size); break;
                case NotesTool.Drawing: DrawPencil(tex, size); break;
                case NotesTool.Image: DrawPicture(tex, size); break;
                case NotesTool.Zoom: DrawZoom(tex, size); break;
            }
```

Add a new `DrawZoom` method right after `DrawPicture`:

```csharp
        static void DrawPicture(Texture2D tex, int size)
        {
            float m = size * 0.18f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            FillCircle(tex, size, new Vector2(min.x + (max.x - min.x) * 0.3f, max.y - (max.y - min.y) * 0.28f), size * 0.07f, Color.white);
            var peak = new Vector2(min.x + (max.x - min.x) * 0.6f, min.y + (max.y - min.y) * 0.25f);
            var baseL = new Vector2(min.x + 2f, max.y - 2f);
            var baseR = new Vector2(max.x - 2f, max.y - 2f);
            FillTriangle(tex, size, peak, baseL, baseR, Color.white);
        }

        static void DrawZoom(Texture2D tex, int size)
        {
            var center = new Vector2(size * 0.42f, size * 0.58f);
            float radius = size * 0.2f;
            float thickness = 2.5f;
            FillCircle(tex, size, center, radius, Color.white);
            FillCircle(tex, size, center, radius - thickness, new Color32(0, 0, 0, 0));
            Vector2 handleDir = new Vector2(0.7f, -0.7f).normalized;
            var handleStart = center + handleDir * radius;
            var handleEnd = center + handleDir * (radius + size * 0.28f);
            DrawLine(tex, size, handleStart, handleEnd, 3.5f, Color.white);
        }
```

- [ ] **Step 5: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 6: Self-test**

Enter Play mode. On `NotesToolbar`, right-click → **Self-Test: Notes Toolbar — Icon Caching**. Expected: `PASS` (now iterates 6 tools, all still resolving to distinct cached icons).

- [ ] **Step 7: Play-mode verify**

Press Play. Toolbar shows a 6th button (magnifying glass) after Изображение. Select it, press and hold over the canvas, drag right — canvas zooms in continuously, with the point where you pressed staying fixed under the cursor. Drag left instead (from a fresh press) — canvas zooms out, same fixed point. Release — zoom stops changing. Scroll-wheel zoom still works exactly as before regardless of which tool is active.

- [ ] **Step 8: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs Assets/WorldGen/Notes/Rendering/NotesToolbar.cs Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs
git commit -m "feat: notes editor — click-drag Zoom tool, pivots around press point"
```

---

### Task 2: Object resize via corner handles

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/ObjectResizeController.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs` (wiring, cleanup, `SetSelectedObject`, `IsScreenPointOverResizeHandle`, `RefreshView`)
- Modify: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs` (`SetSelectedObjectId` helper, `ApplyResizePreview`, `CommitResize`, tool-conflict guard)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs` (`ResizeCommand`, `PushResize`)

**Interfaces:**
- Consumes: `CanvasInteractionController.uiCamera` (existing public field), Task 1's `NotesTool.Zoom` (no interaction — resize handles work the same regardless of tool, blocked from firing other tools' click actions the same way link anchors already are).
- Produces: `ObjectResizeController.Initialize(string objectId, RectTransform host, RectTransform canvasContainer, CanvasInteractionController controller)`, `.SetSelected(bool)`, `.IsScreenPointOverHandle(Vector2, Camera) → bool`. `NotesCanvasController.SetSelectedObject(string objectId)`, `.IsScreenPointOverResizeHandle(Vector2, Camera) → bool`, `.RefreshView(string objectId)`. `CanvasInteractionController.ApplyResizePreview(string, System.Numerics.Vector2, System.Numerics.Vector2)`, `.CommitResize(string, System.Numerics.Vector2, System.Numerics.Vector2)`.

- [ ] **Step 1: Create `ObjectResizeController.cs`**

```csharp
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

        public void BeginDrag(int cornerIndex, Vector2 screenPos)
        {
            draggingCornerIndex = cornerIndex;
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

            interactionController.ApplyResizePreview(hostObjectId,
                new System.Numerics.Vector2(newCenter.x, newCenter.y),
                new System.Numerics.Vector2(newSize.x, newSize.y));
        }

        public void EndDrag(Vector2 screenPos)
        {
            if (draggingCornerIndex < 0) return;
            draggingCornerIndex = -1;
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
```

- [ ] **Step 2: Wire `ObjectResizeController` into `NotesCanvasController`**

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, replace:

```csharp
        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();
        readonly Dictionary<string, LinkAnchorController> linkAnchors = new Dictionary<string, LinkAnchorController>();
```

with:

```csharp
        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();
        readonly Dictionary<string, LinkAnchorController> linkAnchors = new Dictionary<string, LinkAnchorController>();
        readonly Dictionary<string, ObjectResizeController> resizeControllers = new Dictionary<string, ObjectResizeController>();
```

Replace the cleanup block in `RebuildFromPage`:

```csharp
            foreach (var anchors in linkAnchors.Values)
                if (anchors != null) Destroy(anchors.gameObject);
            linkAnchors.Clear();
            OnSelectionCleared?.Invoke();
```

with:

```csharp
            foreach (var anchors in linkAnchors.Values)
                if (anchors != null) Destroy(anchors.gameObject);
            linkAnchors.Clear();
            foreach (var resize in resizeControllers.Values)
                if (resize != null) Destroy(resize.gameObject);
            resizeControllers.Clear();
            OnSelectionCleared?.Invoke();
```

Replace all three `AddLinkAnchors(view.ObjectId, view.RectTransform);` call sites in `SpawnView` — find each occurrence (NoteCardData, ImageObjectData, DrawingObjectData cases) and add a resize-handle call right after it. For the `NoteCardData` case:

```csharp
                    objectViews[card.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
```

becomes:

```csharp
                    objectViews[card.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
```

For the `ImageObjectData` case:

```csharp
                    objectViews[image.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
```

becomes:

```csharp
                    objectViews[image.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
```

For the `DrawingObjectData` case:

```csharp
                    objectViews[drawing.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    break;
                }
            }
        }
```

becomes:

```csharp
                    objectViews[drawing.Id] = view;
                    AddLinkAnchors(view.ObjectId, view.RectTransform);
                    AddResizeHandles(view.ObjectId, view.RectTransform);
                    break;
                }
            }
        }
```

Add `AddResizeHandles` right after `AddLinkAnchors`:

```csharp
        void AddLinkAnchors(string objectId, RectTransform hostRect)
        {
            if (interactionController == null) return;
            var anchorGO = new GameObject($"LinkAnchors_{objectId}");
            anchorGO.transform.SetParent(CanvasContainer, false);
            var anchors = anchorGO.AddComponent<LinkAnchorController>();
            anchors.Initialize(objectId, hostRect, CanvasContainer, interactionController);
            linkAnchors[objectId] = anchors;
        }

        void AddResizeHandles(string objectId, RectTransform hostRect)
        {
            if (interactionController == null) return;
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
        }
```

Replace the cleanup block in `RemoveObject`:

```csharp
            if (linkAnchors.TryGetValue(objectId, out var anchors))
            {
                if (anchors != null) Destroy(anchors.gameObject);
                linkAnchors.Remove(objectId);
            }
            OnSelectionCleared?.Invoke();
```

with:

```csharp
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
```

Add `RefreshView` right after `RefreshLinksFor`:

```csharp
        public void RefreshLinksFor(string objectId)
        {
            foreach (var link in linkViews.Values)
                if (link.LinkId != null) link.UpdateTransform();
        }

        /// <summary>Re-reads the given object's current Position/Size from its data into its live
        /// view — used during a resize drag (and by ResizeCommand.Undo) instead of duplicating
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
```

- [ ] **Step 3: Wire selection tracking + resize preview/commit into `CanvasInteractionController`**

In `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`, add a `SetSelectedObjectId` helper right after `SetTool`:

```csharp
        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            paintingDrawingObjectId = null;
        }

        void SetSelectedObjectId(string objectId)
        {
            selectedObjectId = objectId;
            canvasController.SetSelectedObject(objectId);
        }
```

Replace the tool-conflict guard added in Task 1 (right after the `IsScreenPointOverLinkAnchor` check) — find:

```csharp
            if (canvasController.IsScreenPointOverLinkAnchor(screenPos, uiCamera))
                return;

            switch (ActiveTool)
```

with:

```csharp
            if (canvasController.IsScreenPointOverLinkAnchor(screenPos, uiCamera))
                return;
            if (canvasController.IsScreenPointOverResizeHandle(screenPos, uiCamera))
                return;

            switch (ActiveTool)
```

Replace both `selectedObjectId = null;` assignments in the `NotesTool.Select` case of `HandlePress`:

```csharp
                    string linkAt = canvasController.FindLinkAt(screenPos, uiCamera);
                    if (linkAt != null)
                    {
                        selectedObjectId = null;
                        selectedLinkId = linkAt;
                        canvasController.SetSelectedLink(linkAt);
                        break;
                    }

                    selectedObjectId = null;
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
```

with:

```csharp
                    string linkAt = canvasController.FindLinkAt(screenPos, uiCamera);
                    if (linkAt != null)
                    {
                        SetSelectedObjectId(null);
                        selectedLinkId = linkAt;
                        canvasController.SetSelectedLink(linkAt);
                        break;
                    }

                    SetSelectedObjectId(null);
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
```

Replace `HandleObjectClicked`:

```csharp
        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
            {
                selectedObjectId = objectId;
                if (selectedLinkId != null)
                {
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                }
            }
        }
```

with:

```csharp
        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
            {
                SetSelectedObjectId(objectId);
                if (selectedLinkId != null)
                {
                    selectedLinkId = null;
                    canvasController.SetSelectedLink(null);
                }
            }
        }
```

Replace `HandleObjectDragEnded`:

```csharp
        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            selectedObjectId = objectId;
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
        }
```

with:

```csharp
        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            SetSelectedObjectId(objectId);
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
        }
```

Replace the `HandleDeleteKey` object-delete branch:

```csharp
            string idToDelete = selectedObjectId;
            undoManager.RequestDeleteObject(canvasController, data, confirmed =>
            {
                if (confirmed && selectedObjectId == idToDelete)
                    selectedObjectId = null;
            });
```

with:

```csharp
            string idToDelete = selectedObjectId;
            undoManager.RequestDeleteObject(canvasController, data, confirmed =>
            {
                if (confirmed && selectedObjectId == idToDelete)
                    SetSelectedObjectId(null);
            });
```

Add `ApplyResizePreview`/`CommitResize` right after `CreateLinkFromAnchorDrag`:

```csharp
        /// <summary>Called by LinkAnchorController when an anchor-drag is released over another
        /// object — creates the link through the undo stack, same as the old click-click flow.</summary>
        public void CreateLinkFromAnchorDrag(string fromObjectId, string toObjectId)
        {
            undoManager.PushCreateLink(canvasController, fromObjectId, toObjectId);
        }

        /// <summary>Called live while ObjectResizeController drags a corner handle — applies the
        /// new size/position immediately for responsive feedback; the undo entry is only pushed
        /// once, in CommitResize, when the drag ends.</summary>
        public void ApplyResizePreview(string objectId, System.Numerics.Vector2 newPosition, System.Numerics.Vector2 newSize)
        {
            var data = FindObjectData(objectId);
            if (data == null) return;
            data.Position = newPosition;
            data.Size = newSize;
            canvasController.RefreshView(objectId);
            canvasController.RefreshLinksFor(objectId);
        }

        public void CommitResize(string objectId, System.Numerics.Vector2 oldPosition, System.Numerics.Vector2 oldSize)
        {
            var data = FindObjectData(objectId);
            if (data == null) return;
            undoManager.PushResize(canvasController, data, oldPosition, oldSize);
        }
```

- [ ] **Step 4: Add `ResizeCommand`/`PushResize` to `NotesUndoManager`**

In `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs`, add a new command class right after `MoveCommand`:

```csharp
        class MoveCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public System.Numerics.Vector2 OldPosition;
            public override void Undo()
            {
                Data.Position = OldPosition;
                var view = Canvas.GetView(Data.Id);
                switch (view)
                {
                    case NoteCardView n: n.Refresh(); break;
                    case ImageObjectView i: i.Refresh(); break;
                    case DrawingObjectView d: d.Refresh(); break;
                }
                Canvas.RefreshLinksFor(Data.Id);
            }
        }

        class ResizeCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public System.Numerics.Vector2 OldPosition;
            public System.Numerics.Vector2 OldSize;
            public override void Undo()
            {
                Data.Position = OldPosition;
                Data.Size = OldSize;
                Canvas.RefreshView(Data.Id);
                Canvas.RefreshLinksFor(Data.Id);
            }
        }
```

Add `PushResize` right after `PushMove`:

```csharp
        public void PushMove(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            undoStack.Push(new MoveCommand { Canvas = canvas, Data = data, OldPosition = oldPos });
        }

        public void PushResize(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPosition, System.Numerics.Vector2 oldSize)
        {
            undoStack.Push(new ResizeCommand { Canvas = canvas, Data = data, OldPosition = oldPosition, OldSize = oldSize });
        }
```

- [ ] **Step 5: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 6: Self-test**

Enter Play mode. Create a note card. Find its `ResizeHandles_...` GameObject in the Hierarchy, right-click its `ObjectResizeController` component → **Self-Test: ObjectResizeController — Corner Resize Math**. Expected: `PASS`.

- [ ] **Step 7: Play-mode verify**

Press Play, Курсор tool. Click a note card to select it — 4 small orange square handles appear at its corners. Drag the bottom-right handle outward — the card grows, its top-left corner stays fixed in place. Drag it back in past the minimum size — the card stops shrinking at 40×40, doesn't invert or vanish. Release — click elsewhere to deselect — handles disappear. Undo (however Undo is currently triggered in this project) after a resize — card returns to its pre-resize size and position. Attach a link between two cards, resize one of them — the link's attachment point follows the resize live, not just after release. Switch to the Заметка (Note) tool while a card is still selected (handles visible), then press directly on one of the visible handles — expected: a resize drag starts, no new note card gets created at that point. Zoom the canvas out — handles stay a comfortable constant size rather than shrinking with the canvas content.

- [ ] **Step 8: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/ObjectResizeController.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "feat: notes editor — corner-drag resize for cards/images/drawings"
```

---

## Post-implementation

Run all self-tests (Play mode, right-click each component → the listed menu item):
- `NotesDocumentController` → **Self-Test: Notes Document CRUD** → `PASS` (pre-existing, must still pass)
- `NotesUndoManager` → **Self-Test: Notes Undo — Create/Undo Card** → `PASS` (pre-existing, must still pass)
- `NotesToolbar` → **Self-Test: Notes Toolbar — Icon Caching** → `PASS` (now 6 tools)
- `LinkView` (on any spawned link) → **Self-Test: LinkView — Anchor Point Selection** → `PASS` (pre-existing, must still pass)
- `ObjectResizeController` (on any spawned object's resize handles) → **Self-Test: ObjectResizeController — Corner Resize Math** → `PASS` (new, Task 2)

Then a full end-to-end pass:
1. Select the Zoom tool, click-drag right/left over the canvas — zooms in/out around the press point; release stops it. Scroll-wheel zoom still works under any tool.
2. Select a note card — 4 corner handles appear; drag one — card resizes freely, opposite corner stays put, can't shrink below 40×40.
3. Resize an image and a drawing the same way — both just stretch their display, content isn't redrawn/resampled.
4. Resize an object with an attached link — the link stays correctly anchored throughout the drag.
5. Undo after a resize — object returns to its prior size/position.
6. With an object selected (handles visible), switch to a different tool (Note/Drawing/Image/Zoom) and press directly on a handle — a resize starts, the other tool's click action does not also fire.
7. Zoom the canvas out — both link anchor dots/handle (from the previous feature) and the new resize handles stay a constant, clickable screen size.
