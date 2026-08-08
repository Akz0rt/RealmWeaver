using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The page as a place you can drop a thing into: drag a row out of the navigator, let go over the prose,
    /// and a link appears where you aimed.
    ///
    /// A THIRD GESTURE, NOT A THIRD LINK MODEL. It ends in NotesLinkOps.MakeToken exactly as the toolbar
    /// button does — through DocumentPageView.InsertTokenInto, the same method — so there is nothing here a
    /// link can be that it cannot be by any other route.
    ///
    /// WHERE THE DROP LANDS, and the one rule that decides:
    ///   • inside a row that holds text — at the character nearest the cursor, mapped through the SAME
    ///     display→source mapping a click uses (DocBlockView.SourceCaretFromScreen), never a second one;
    ///   • anywhere else — above a row, below the last one, or over a picture — as a new row of its own.
    ///
    /// THE MARKER IS NOT DECORATION. A drop whose destination is invisible is a drop the DM cannot aim, so
    /// the same Resolve that decides the drop also drives a caret line drawn every frame while a drag is in
    /// flight. One function answers both questions, which is what makes "it landed where the line was" true
    /// rather than approximately true.
    ///
    /// ATTACHED TO THE PAGE'S ROOT, which is where uGUI delivers a drop that landed on any descendant: the
    /// event system runs the drop handler up the hierarchy from whatever graphic the pointer was over
    /// (ExecuteEvents.ExecuteHierarchy), and neither a row, nor its input field, nor the ScrollRect handles
    /// IDropHandler. That also settles the two-pane case structurally rather than by lookup, though the
    /// reason changed in Task 4 of the two-panes arc and the old one is worth keeping. It used to be that
    /// there was only ONE page surface, re-parented by PageSurfaceHost into whichever pane showed a Page tab,
    /// so "the pane it lands on" was wherever that root happened to be. There is now one page view per pane,
    /// each with its own root and its own LinkDropTarget on it — so the drop is delivered to the view whose
    /// hierarchy the pointer was actually over, which is the same answer reached without any re-parenting.
    ///
    /// KNOWN COST, accepted: a navigator row that handles IDragHandler no longer passes a drag to the
    /// navigator's ScrollRect, so the list can no longer be scrolled by grabbing a row and pulling. The wheel
    /// still scrolls it. There is no way to have both without guessing at the DM's intent from the direction
    /// of the first few pixels of movement.
    /// </summary>
    public class LinkDropTarget : MonoBehaviour, IDropHandler
    {
        public const string ObjectName = "LinkDropMarker";
        const float MarkerThickness = 2f;

        /// <summary>What is being dragged right now, or null. A static slot rather than a payload carried on
        /// the event, because the drag starts in the workspace layer and lands in this one — and the DRAG
        /// SOURCE clears it unconditionally on end-drag, including when the drop landed nowhere, so a
        /// cancelled drag cannot leave the next one holding a stale object.</summary>
        public static LinkDragPayload Dragging;

        public class LinkDragPayload
        {
            public string Kind;
            public string Id;
            public string Name;
        }

        DocumentPageView page;
        RectTransform self;
        RectTransform marker;

        public static LinkDropTarget Attach(DocumentPageView page)
        {
            if (page == null || page.Root == null) return null;

            // Replaced rather than repaired, for the same reason PageToolbar.Attach is: a domain reload nulls
            // this component's own fields while the object it lives on survives.
            var existing = page.Root.GetComponent<LinkDropTarget>();
            if (existing != null) DestroyImmediate(existing);

            // AND THE MARKER WITH IT, which is not tidiness. A reload never runs OnDisable, so a marker left
            // drawn by a drag that was in flight survives as a live GameObject — while the fresh component's
            // own `marker` field is null, so HideMarker has nothing to hide and Update never reaches it. The
            // page would carry an accent line across it for the rest of the session with nothing able to
            // remove it.
            var staleMarker = page.Root.Find(ObjectName);
            if (staleMarker != null) DestroyImmediate(staleMarker.gameObject);

            var target = page.Root.gameObject.AddComponent<LinkDropTarget>();
            target.page = page;
            target.self = page.Root;
            return target;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var payload = Dragging;
            if (payload == null || page == null || page.Page == null) return;

            HideMarker();
            if (!Resolve(eventData.position, eventData.pressEventCamera, out var spot)) return;

            if (spot.Row != null)
                page.InsertTokenInto(spot.Row.BlockId, spot.SourceCaret, payload.Kind, payload.Id, payload.Name);
            else
                page.DropLinkAsNewBlock(spot.BlockIndex, payload.Kind, payload.Id, payload.Name);
        }

        /// <summary>Draws the marker while a drag is in flight. Polled rather than driven by a hover event
        /// because uGUI has no "drag moved over me" notification for an object that is not the drag SOURCE —
        /// IDragHandler goes to whatever started the drag, which is a navigator row on the other side of the
        /// window.</summary>
        void Update()
        {
            if (Dragging == null || page == null || page.Page == null) { HideMarker(); return; }

            var mouse = Mouse.current;
            if (mouse == null) { HideMarker(); return; }

            Vector2 screenPos = mouse.position.ReadValue();
            var cam = MarkerCamera();
            if (!RectTransformUtility.RectangleContainsScreenPoint(self, screenPos, cam)) { HideMarker(); return; }
            if (!Resolve(screenPos, cam, out var spot)) { HideMarker(); return; }

            ShowMarker(spot.MarkerA, spot.MarkerB);
        }

        /// <summary>A page whose root was deactivated — the pane switched to another tab mid-drag — never
        /// gets another Update, so the marker would stay drawn under a hidden object and reappear with it.
        /// Cleared here rather than trusted to a frame that will not run.</summary>
        void OnDisable() => HideMarker();

        Camera MarkerCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary>Where a drop at this point would go, and where to draw the line that says so.</summary>
        struct DropSpot
        {
            /// <summary>Non-null: into this row's text. Null: a new row at BlockIndex.</summary>
            public DocBlockView Row;
            public int SourceCaret;
            /// <summary>An index into Page.Blocks, NOT into the visible rows — a collapsed section makes the
            /// two differ by its whole hidden subtree.</summary>
            public int BlockIndex;
            public Vector3 MarkerA;
            public Vector3 MarkerB;
        }

        bool Resolve(Vector2 screenPos, Camera cam, out DropSpot spot)
        {
            spot = default;
            var blocks = page.Page.Blocks;
            var rows = page.Rows;
            var visible = NotesDocOps.VisibleIndices(blocks);

            RectTransform lastRect = null;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                var rect = (RectTransform)row.transform;
                lastRect = rect;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, cam, out var local);

                if (local.y > rect.rect.yMax)
                {
                    // Above this row entirely — a new row goes in front of it.
                    spot.BlockIndex = i < visible.Count ? visible[i] : blocks.Count;
                    SetGapMarker(rect, top: true, ref spot);
                    return true;
                }
                if (local.y < rect.rect.yMin) continue;   // still above the pointer; keep walking down

                if (row.AcceptsText)
                {
                    spot.SourceCaret = row.SourceCaretFromScreen(screenPos, cam, out int displayCaret);
                    if (!row.TryGetDropMarker(displayCaret, out spot.MarkerA, out spot.MarkerB)) return false;
                    spot.Row = row;
                    return true;
                }

                // A picture (or a card) cannot hold a caret — the link goes in the row after it.
                spot.BlockIndex = i + 1 < visible.Count ? visible[i + 1] : blocks.Count;
                SetGapMarker(rect, top: false, ref spot);
                return true;
            }

            // Past the last row, in the empty space under the page.
            spot.BlockIndex = blocks.Count;
            if (lastRect != null) SetGapMarker(lastRect, top: false, ref spot);
            else SetGapMarker(page.Content, top: true, ref spot);
            return true;
        }

        /// <summary>A horizontal line along one edge of a row — the seam the new row would open at. Spans
        /// that row's own width, so it reads as "here, in this column" rather than as a rule across the
        /// whole pane.</summary>
        static void SetGapMarker(RectTransform rect, bool top, ref DropSpot spot)
        {
            if (rect == null) return;
            rect.GetWorldCorners(corners);
            // corners: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            spot.MarkerA = top ? corners[1] : corners[0];
            spot.MarkerB = top ? corners[2] : corners[3];
        }

        static readonly Vector3[] corners = new Vector3[4];

        void ShowMarker(Vector3 worldA, Vector3 worldB)
        {
            if (marker == null) marker = BuildMarker();
            if (marker == null) return;

            var a = self.InverseTransformPoint(worldA);
            var b = self.InverseTransformPoint(worldB);

            marker.gameObject.SetActive(true);
            marker.anchoredPosition = new Vector2((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f);
            marker.sizeDelta = new Vector2(Mathf.Max(Mathf.Abs(a.x - b.x), MarkerThickness),
                                           Mathf.Max(Mathf.Abs(a.y - b.y), MarkerThickness));
        }

        void HideMarker()
        {
            if (marker != null) marker.gameObject.SetActive(false);
        }

        RectTransform BuildMarker()
        {
            if (self == null) return null;

            // A child of the page ROOT rather than of the scrolling Content: Content is driven by a
            // VerticalLayoutGroup, which would take a marker parented there for another row and lay it out
            // as one. No look-up for an existing one — Attach has already destroyed any, deliberately (see
            // its own note), so finding one here could only ever mean finding the stale object that note is
            // about.
            var go = new GameObject(ObjectName, typeof(RectTransform));
            go.transform.SetParent(self, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.AddComponent<Image>();
            ThemeService.Tag(image, ThemeRole.Accent);
            // The drop is decided by where the POINTER is, and a marker that raycasts would sometimes be what
            // the pointer is over instead of the row underneath it.
            image.raycastTarget = false;
            return rect;
        }
    }
}
