# Notes Editor Link Tool Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the notes editor's click-object-then-click-object straight-line "Связь" tool with a Miro-style gesture: drag from a small anchor point on any object's edge (available under any active tool) to draw a bendable curved connector to another object, per `docs/superpowers/specs/2026-07-03-notes-editor-link-tool-design.md`.

**Architecture:** No new subsystems. Rewrites `LinkView.cs` to render/hit-test a segmented quadratic-Bezier curve instead of one straight line, adds one new component (`LinkAnchorController`) for the hover-anchor-drag gesture, and removes the old "Связь" toolbar tool once its replacement exists.

**Tech Stack:** Unity 2022.3+/Unity 6, Built-in RP, New Input System (`Mouse.current`/`Keyboard.current`), legacy `UnityEngine.UI` (no TextMeshPro), code-only UI construction (`new GameObject()` + `AddComponent<>()`), C#.

## Global Constraints

- **New Input System only** — `Mouse.current`/`Keyboard.current`. Never `UnityEngine.Input`.
- **No TextMeshPro** — `Text`/`Image`/`Button` from `UnityEngine.UI` only.
- **UI construction is code-only** — no Editor-authored prefabs.
- **`[ContextMenu]` self-tests** for logic verifiable without manual interaction, matching project convention (`Debug.Log("Self-Test X: PASS/FAIL")`).
- **No placeholders in code** — every method has a real implementation.
- **Out of scope (do not touch):** map/notes 2:1 split redesign, world-generation UI gating, multi-select, note card/image/drawing creation/editing behavior.

---

### Task 1: Curved link data model + segmented rendering

**Files:**
- Modify: `Assets/WorldGen/Notes/Data/NotesData.cs` (`LinkData` class)
- Modify: `Assets/WorldGen/Notes/Rendering/LinkView.cs` (full rewrite)

**Interfaces:**
- Consumes: nothing new — `LinkView.Initialize(LinkData, RectTransform, RectTransform, RectTransform)` keeps its current 4-parameter signature in this task (Task 2 adds a 5th `Camera` parameter).
- Produces: `LinkData.ControlPointOffset` (`System.Numerics.Vector2?`). Curves render correctly for links created via the existing click-click "Связь" tool (unchanged in this task) and update live as either endpoint object moves.

- [ ] **Step 1: Add `ControlPointOffset` to `LinkData`**

In `Assets/WorldGen/Notes/Data/NotesData.cs`, replace:

```csharp
    public class LinkData
    {
        public string Id = Guid.NewGuid().ToString();
        public string FromObjectId;
        public string ToObjectId;
        public bool Directed = true;
    }
```

with:

```csharp
    public class LinkData
    {
        public string Id = Guid.NewGuid().ToString();
        public string FromObjectId;
        public string ToObjectId;
        public bool Directed = true;
        /// <summary>Offset from the straight-line midpoint between the two connected objects'
        /// anchor points, in canvas units. Null = an automatic bend is computed instead.</summary>
        public Vector2? ControlPointOffset;
    }
```

- [ ] **Step 2: Rewrite `LinkView.cs` with curved, segmented rendering**

Replace the entire contents of `Assets/WorldGen/Notes/Rendering/LinkView.cs` with:

```csharp
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
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 4: Self-test**

Enter Play mode. On the `LinkView` prefab instance is not available directly (LinkView objects only exist while a link exists) — instead, create a link first (Курсор tool is fine; the old "Связь" tool still exists at this point), then find the spawned `Link_...` GameObject in the Hierarchy, right-click its `LinkView` component → **Self-Test: LinkView — Anchor Point Selection**. Expected Console output: `Self-Test LinkView — Anchor Point Selection: PASS`.

- [ ] **Step 5: Play-mode verify**

Press Play. Select the "Связь" tool, click one card then another. Expected: a smooth curved line (not straight) with an arrowhead appears between them, bulging slightly to one side. Drag one of the two cards around (Курсор tool). Expected: the curve updates live and keeps a sensible shape as the card moves.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Data/NotesData.cs Assets/WorldGen/Notes/Rendering/LinkView.cs
git commit -m "feat: notes editor links render as curves instead of straight lines"
```

---

### Task 2: Link hover, selection tint, and draggable bend handle

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/LinkView.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs:135-144` (`SpawnLink`)

**Interfaces:**
- Consumes: `CanvasInteractionController.uiCamera` (existing public field) — threaded through so `LinkView` can do screen-to-local conversions.
- Produces: `LinkView.Initialize(LinkData, RectTransform, RectTransform, RectTransform, Camera)` (signature gains a 5th `Camera` parameter — the only caller, `NotesCanvasController.SpawnLink`, is updated in this task). `LinkView.ContainsScreenPoint(Vector2, Camera) → bool` and `LinkView.SetSelected(bool)` are new public members Task 5 will call.

- [ ] **Step 1: Add hover/selection state, the control handle, and camera-aware hit-testing to `LinkView`**

In `Assets/WorldGen/Notes/Rendering/LinkView.cs`, add these `using` statements at the top:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;
```

Replace the field block:

```csharp
        LinkData data;
        RectTransform fromRect;
        RectTransform toRect;
        RectTransform[] segmentRects;
        RectTransform arrowRect;

        public string LinkId => data?.Id;
```

with:

```csharp
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

        static readonly Color NormalColor = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.3f, 0.95f);

        public string LinkId => data?.Id;
```

Replace the `Initialize` signature and the segment-building loop:

```csharp
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
```

with:

```csharp
        public void Initialize(LinkData linkData, RectTransform canvasContainer, RectTransform from, RectTransform to, Camera camera)
        {
            data = linkData;
            fromRect = from;
            toRect = to;
            uiCamera = camera;

            transform.SetParent(canvasContainer, false);

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
```

Replace the end of `Initialize` (the `arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize); }` closing plus the `UpdateTransform();` call) — find:

```csharp
                arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            }

            UpdateTransform();
        }
```

Keep it as-is (no change needed there — the `if (data.Directed)` block itself is unchanged, only what precedes it changed above).

Now add hover polling and hit-testing/selection methods. Insert immediately after the closing brace of `UpdateTransform()` (i.e., right before `static void PositionSegment(...)`):

```csharp
        void Update()
        {
            if (Mouse.current == null || segmentRects == null) return;
            var screenPos = Mouse.current.position.ReadValue();
            bool nowHovering = ContainsScreenPoint(screenPos, uiCamera);
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
            data.ControlPointOffset = new Vector2(offset.x, offset.y);
            UpdateTransform();
        }
```

Update `UpdateTransform()` to also position the handle. Replace:

```csharp
            if (arrowRect != null)
            {
                Vector2 tangentStart = SampleQuadraticBezier(fromAnchor, control, toAnchor, 1f - 1f / SegmentCount);
                Vector2 delta = toAnchor - tangentStart;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                arrowRect.anchoredPosition = toAnchor;
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
```

with:

```csharp
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
```

Finally, add the small drag-forwarder class at the bottom of the file, right before the final closing `}` of the `WorldGen.Notes.Rendering` namespace:

```csharp
    /// <summary>Forwards drag events from the link's bend handle back to its owning LinkView —
    /// kept as a separate component since the handle is a distinct GameObject from LinkView's.</summary>
    class LinkHandleDragHandler : MonoBehaviour, IDragHandler
    {
        public LinkView owner;
        public void OnDrag(PointerEventData eventData) => owner.OnHandleDragged(eventData.position);
    }
```

- [ ] **Step 2: Thread the camera through `NotesCanvasController.SpawnLink`**

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, replace:

```csharp
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
```

with:

```csharp
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
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 4: Play-mode verify**

Press Play, create a link with the (still-present) "Связь" tool. Hover the mouse over the curve (not near either card). Expected: a small yellow square handle appears at the curve's bend point. Drag it. Expected: the curve reshapes to follow the handle, and stays bent that way after you move the mouse away and back (the shape is now stored, not just previewed).

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/LinkView.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs
git commit -m "feat: notes editor links — hover-revealed draggable bend handle"
```

---

### Task 3: Drag-from-anchor link creation gesture

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/LinkAnchorController.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs` (`SpawnView`, new `FindObjectAt`)
- Modify: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs` (new `CreateLinkFromAnchorDrag`)

**Interfaces:**
- Consumes: `CanvasInteractionController.canvasController`/`.uiCamera` (existing public fields), `NotesUndoManager.PushCreateLink` (existing).
- Produces: `LinkAnchorController.Initialize(string objectId, RectTransform host, RectTransform canvasContainer, CanvasInteractionController interactionController)`. `NotesCanvasController.FindObjectAt(Vector2 screenPos, Camera uiCamera, string excludeObjectId) → string`. `CanvasInteractionController.CreateLinkFromAnchorDrag(string fromObjectId, string toObjectId)`. This task does not remove the old "Связь" tool yet — both creation methods coexist so link creation is never unavailable mid-branch.

- [ ] **Step 1: Create `LinkAnchorController.cs`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Attached alongside each canvas object view (NoteCardView/ImageObjectView/
    /// DrawingObjectView). Reveals 4 small anchor dots at the object's edge midpoints on
    /// hover; dragging from one draws a rubber-band preview and, on release over another
    /// object, creates a link via CanvasInteractionController.CreateLinkFromAnchorDrag.
    /// </summary>
    public class LinkAnchorController : MonoBehaviour
    {
        const float DotSize = 10f;

        RectTransform hostRect;
        RectTransform canvasContainer;
        CanvasInteractionController interactionController;
        string hostObjectId;

        RectTransform[] dots;
        bool hovering;
        bool dragging;
        Vector2 dragStartLocal;
        RectTransform previewRect;

        public void Initialize(string objectId, RectTransform host, RectTransform container, CanvasInteractionController controller)
        {
            hostObjectId = objectId;
            hostRect = host;
            canvasContainer = container;
            interactionController = controller;

            dots = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var dotGO = new GameObject($"AnchorDot_{i}");
                dotGO.transform.SetParent(canvasContainer, false);
                var img = dotGO.AddComponent<Image>();
                img.color = new Color(0.3f, 0.7f, 1f, 0.95f);
                var rect = dotGO.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(DotSize, DotSize);
                var handler = dotGO.AddComponent<AnchorDotHandler>();
                handler.owner = this;
                dotGO.SetActive(false);
                dots[i] = rect;
            }

            var previewGO = new GameObject("LinkPreview");
            previewGO.transform.SetParent(canvasContainer, false);
            var previewImg = previewGO.AddComponent<Image>();
            previewImg.color = new Color(0.3f, 0.7f, 1f, 0.7f);
            previewImg.raycastTarget = false;
            previewRect = previewGO.GetComponent<RectTransform>();
            previewRect.pivot = new Vector2(0f, 0.5f);
            previewRect.sizeDelta = new Vector2(0f, 3f);
            previewGO.SetActive(false);
        }

        void Update()
        {
            PositionDots();
            if (dragging) return;
            if (Mouse.current == null) return;

            var screenPos = Mouse.current.position.ReadValue();
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            bool nowHovering = RectTransformUtility.RectangleContainsScreenPoint(hostRect, screenPos, cam);
            if (nowHovering == hovering) return;
            hovering = nowHovering;
            foreach (var dot in dots) dot.gameObject.SetActive(hovering);
        }

        void PositionDots()
        {
            Vector2 half = hostRect.sizeDelta * 0.5f;
            Vector2 center = hostRect.anchoredPosition;
            dots[0].anchoredPosition = center + new Vector2(0f, half.y);
            dots[1].anchoredPosition = center + new Vector2(0f, -half.y);
            dots[2].anchoredPosition = center + new Vector2(-half.x, 0f);
            dots[3].anchoredPosition = center + new Vector2(half.x, 0f);
        }

        public void BeginDrag(Vector2 screenPos)
        {
            dragging = true;
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPos, cam, out dragStartLocal);
            previewRect.gameObject.SetActive(true);
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            Camera cam = interactionController != null ? interactionController.uiCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPos, cam, out var local);
            Vector2 delta = local - dragStartLocal;
            previewRect.anchoredPosition = dragStartLocal;
            previewRect.sizeDelta = new Vector2(delta.magnitude, previewRect.sizeDelta.y);
            previewRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        public void EndDrag(Vector2 screenPos)
        {
            dragging = false;
            previewRect.gameObject.SetActive(false);
            if (interactionController == null || interactionController.canvasController == null) return;

            Camera cam = interactionController.uiCamera;
            string targetId = interactionController.canvasController.FindObjectAt(screenPos, cam, hostObjectId);
            if (targetId != null)
                interactionController.CreateLinkFromAnchorDrag(hostObjectId, targetId);
        }
    }

    /// <summary>One draggable anchor dot; forwards press/drag/release to its owning
    /// LinkAnchorController.</summary>
    class AnchorDotHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public LinkAnchorController owner;
        public void OnPointerDown(PointerEventData eventData) => owner.BeginDrag(eventData.position);
        public void OnDrag(PointerEventData eventData) => owner.UpdateDrag(eventData.position);
        public void OnPointerUp(PointerEventData eventData) => owner.EndDrag(eventData.position);
    }
}
```

- [ ] **Step 2: Add `NotesCanvasController.FindObjectAt` and wire `LinkAnchorController` into `SpawnView`**

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, replace the `SpawnView` method:

```csharp
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
                    break;
                }
            }
        }
```

with:

```csharp
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
        }
```

Now add `FindObjectAt`. Replace:

```csharp
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
```

with:

```csharp
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
```

- [ ] **Step 3: Add `CanvasInteractionController.CreateLinkFromAnchorDrag`**

In `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`, add this method right after `HandleObjectDragEnded`:

```csharp
        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            selectedObjectId = objectId;
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
        }

        /// <summary>Called by LinkAnchorController when an anchor-drag is released over another
        /// object — creates the link through the undo stack, same as the old click-click flow.</summary>
        public void CreateLinkFromAnchorDrag(string fromObjectId, string toObjectId)
        {
            undoManager.PushCreateLink(canvasController, fromObjectId, toObjectId);
        }
```

- [ ] **Step 4: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 5: Play-mode verify**

Press Play. Hover any card. Expected: 4 small blue dots appear at its top/bottom/left/right edges. Press and drag from one of them toward another card — a thin rubber-band preview line follows the cursor. Release over the other card. Expected: a curved link is created between them (same visual as Task 1/2's links). Release over empty canvas instead. Expected: the preview disappears, no link is created.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/LinkAnchorController.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs
git commit -m "feat: notes editor — drag-from-anchor gesture creates links under any tool"
```

---

### Task 4: Remove the old "Связь" tool

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs` (`NotesTool` enum, `SetTool`, `HandleObjectClicked`)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs` (`ToolDefs`)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs` (remove `DrawLink` + its switch case)

**Interfaces:**
- Consumes: nothing new.
- Produces: `NotesTool` no longer has a `Link` value. Toolbar shows 4 buttons (Курсор/Заметка/Рисунок/Изображение). Link creation is now exclusively the Task 3 anchor-drag gesture.

- [ ] **Step 1: Remove `Link` from the `NotesTool` enum and the old click-click flow**

In `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`, replace:

```csharp
    public enum NotesTool { Select, Note, Link, Drawing, Image }
```

with:

```csharp
    public enum NotesTool { Select, Note, Drawing, Image }
```

Replace:

```csharp
        string linkDragSourceId;
        string paintingDrawingObjectId;
        string selectedObjectId;
        bool panning;
        Vector2 lastPanScreenPos;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            linkDragSourceId = null;
            paintingDrawingObjectId = null;
        }
```

with:

```csharp
        string paintingDrawingObjectId;
        string selectedObjectId;
        bool panning;
        Vector2 lastPanScreenPos;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            paintingDrawingObjectId = null;
        }
```

Replace:

```csharp
        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
            {
                selectedObjectId = objectId;
                return;
            }

            if (ActiveTool == NotesTool.Link)
            {
                if (linkDragSourceId == null)
                {
                    linkDragSourceId = objectId;
                }
                else if (linkDragSourceId != objectId)
                {
                    undoManager.PushCreateLink(canvasController, linkDragSourceId, objectId);
                    linkDragSourceId = null;
                }
            }
        }
```

with:

```csharp
        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
                selectedObjectId = objectId;
        }
```

- [ ] **Step 2: Remove the "Связь" toolbar button**

In `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs`, replace:

```csharp
        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Link, "Связь"),
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
        };
```

- [ ] **Step 3: Remove the now-unused link icon from `NotesIconFactory`**

In `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs`, replace:

```csharp
            switch (tool)
            {
                case NotesTool.Select: DrawCursor(tex, size); break;
                case NotesTool.Note: DrawNote(tex, size); break;
                case NotesTool.Link: DrawLink(tex, size); break;
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
            }
```

Then delete the now-unused `DrawLink` method entirely:

```csharp
        static void DrawLink(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.2f, size * 0.2f);
            var to = new Vector2(size * 0.75f, size * 0.75f);
            DrawLine(tex, size, from, to, 2.5f, Color.white);
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.08f);
            Vector2 left = to - dir * (size * 0.06f) + perp * (size * 0.08f);
            Vector2 right = to - dir * (size * 0.06f) - perp * (size * 0.08f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }
```

(Delete this whole method — `DrawLine`/`FillTriangle` themselves are still used by the other icons, only `DrawLink` itself is removed.)

- [ ] **Step 4: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 5: Self-test**

Enter Play mode. On `NotesToolbar`, right-click → **Self-Test: Notes Toolbar — Icon Caching**. Expected: `PASS` (it iterates `System.Enum.GetValues(typeof(NotesTool))`, which now has 4 values, all still resolving to cached icons).

- [ ] **Step 6: Play-mode verify**

Press Play. Expected: toolbar shows exactly 4 buttons (Курсор/Заметка/Рисунок/Изображение), no "Связь". Creating a link via anchor-drag (Task 3's gesture) still works under any tool.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs Assets/WorldGen/Notes/Rendering/NotesToolbar.cs Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs
git commit -m "refactor: remove the old click-click 'Связь' tool, superseded by anchor-drag"
```

---

### Task 5: Link selection and direct deletion

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs` (new `FindLinkAt`, `SetSelectedLink`, `FindLinkData`)
- Modify: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs` (`selectedLinkId`, `HandlePress`, `HandleObjectClicked`, `HandleDeleteKey`)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs` (`DeleteLinkCommand`, `RequestDeleteLink`)

**Interfaces:**
- Consumes: `LinkView.ContainsScreenPoint`/`SetSelected` (from Task 2), `NotesCanvasController.RemoveLink`/`AddLink` (existing).
- Produces: clicking empty canvas near a link selects it; Delete removes just that link, independent of its endpoint objects.

- [ ] **Step 1: Add link lookup/selection methods to `NotesCanvasController`**

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, add these methods right after `RemoveLink`:

```csharp
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
```

- [ ] **Step 2: Track and clear `selectedLinkId` in `CanvasInteractionController`**

In `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`, replace:

```csharp
        string paintingDrawingObjectId;
        string selectedObjectId;
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
```

Replace the `NotesTool.Select` case in `HandlePress`:

```csharp
                case NotesTool.Select:
                    // A press that lands on an object is left to that object's own
                    // IPointerDownHandler/IDragHandler (NoteCardView etc.) — starting a pan here
                    // too would move the whole canvas underneath it at the same time as the
                    // object drags itself, fighting each other.
                    if (canvasController.IsScreenPointOverObject(screenPos, uiCamera))
                        break;
                    selectedObjectId = null;
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
```

with:

```csharp
                case NotesTool.Select:
                    // A press that lands on an object is left to that object's own
                    // IPointerDownHandler/IDragHandler (NoteCardView etc.) — starting a pan here
                    // too would move the whole canvas underneath it at the same time as the
                    // object drags itself, fighting each other.
                    if (canvasController.IsScreenPointOverObject(screenPos, uiCamera))
                        break;

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

Replace `HandleObjectClicked` so selecting an object clears any selected link:

```csharp
        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Select)
                selectedObjectId = objectId;
        }
```

with:

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

- [ ] **Step 3: Extend `HandleDeleteKey` to delete a selected link**

Replace:

```csharp
        void HandleDeleteKey()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.deleteKey.wasPressedThisFrame) return;
            if (selectedObjectId == null) return;

            var data = FindObjectData(selectedObjectId);
            if (data == null) { selectedObjectId = null; return; }

            string idToDelete = selectedObjectId;
            undoManager.RequestDeleteObject(canvasController, data, confirmed =>
            {
                if (confirmed && selectedObjectId == idToDelete)
                    selectedObjectId = null;
            });
        }
```

with:

```csharp
        void HandleDeleteKey()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.deleteKey.wasPressedThisFrame) return;

            if (selectedLinkId != null)
            {
                var linkData = canvasController.FindLinkData(selectedLinkId);
                if (linkData == null) { selectedLinkId = null; return; }

                string linkIdToDelete = selectedLinkId;
                undoManager.RequestDeleteLink(canvasController, linkData, confirmed =>
                {
                    if (confirmed && selectedLinkId == linkIdToDelete)
                        selectedLinkId = null;
                });
                return;
            }

            if (selectedObjectId == null) return;

            var data = FindObjectData(selectedObjectId);
            if (data == null) { selectedObjectId = null; return; }

            string idToDelete = selectedObjectId;
            undoManager.RequestDeleteObject(canvasController, data, confirmed =>
            {
                if (confirmed && selectedObjectId == idToDelete)
                    selectedObjectId = null;
            });
        }
```

- [ ] **Step 4: Add `DeleteLinkCommand` and `RequestDeleteLink` to `NotesUndoManager`**

In `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs`, add this command class right after `DeleteObjectCommand`:

```csharp
        class DeleteLinkCommand : Command
        {
            public NotesCanvasController Canvas;
            public string FromObjectId;
            public string ToObjectId;
            public override void Undo() => Canvas.AddLink(FromObjectId, ToObjectId);
        }
```

Add `RequestDeleteLink` right after `RequestDeleteObject`:

```csharp
        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog($"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void RequestDeleteLink(NotesCanvasController canvas, LinkData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog("Удалить связь?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveLink(data.Id);
                    undoStack.Push(new DeleteLinkCommand { Canvas = canvas, FromObjectId = data.FromObjectId, ToObjectId = data.ToObjectId });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }
```

- [ ] **Step 5: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 6: Play-mode verify**

Press Play. Create a link (anchor-drag). Курсор tool, click directly on the curve (away from either card). Expected: the curve turns yellow and its bend handle appears — it's selected. Press Delete. Expected: the same red/gray confirm dialog used for objects appears, asking to delete the link; confirming removes only the link, both cards remain. Click empty canvas after selecting a link — expected: the highlight clears and canvas panning works normally again.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "feat: notes editor — direct link selection and deletion via Delete key"
```

---

## Post-implementation

Run all self-tests (Play mode, right-click each component → the listed menu item):
- `NotesDocumentController` → **Self-Test: Notes Document CRUD** → `PASS` (pre-existing, must still pass)
- `NotesUndoManager` → **Self-Test: Notes Undo — Create/Undo Card** → `PASS` (pre-existing, must still pass)
- `NotesToolbar` → **Self-Test: Notes Toolbar — Icon Caching** → `PASS` (must still pass with 4 tools)
- `LinkView` (on any spawned link) → **Self-Test: LinkView — Anchor Point Selection** → `PASS` (new, Task 1)

Then a full end-to-end pass:
1. Hover a note card — 4 anchor dots appear; drag from one to another card — curved link with arrowhead appears.
2. Hover the new link's curve — bend handle appears; drag it — curve reshapes and the shape persists.
3. Move either connected card — the curve's attach side updates to whichever edge is now closest, and the curve keeps following.
4. Click the curve away from any handle — it highlights yellow; press Delete — confirm dialog → link removed, cards untouched.
5. Repeat card/drawing/image creation, dragging, and object deletion from the original plans — unaffected by this change.
6. Confirm the toolbar shows exactly 4 buttons and no leftover "Связь" references anywhere (Hierarchy, Console warnings).
