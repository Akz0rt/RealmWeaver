using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draws a Document page as a scrolling list of rows, and owns the board-vs-document switch. The board
    /// renderer (NotesCanvasController) is NOT modified by this class — historically it hid a caller-supplied
    /// boardViewport and showed its own, both children of the same RightColumn.
    ///
    /// Task 9 (workspace shell surfaces): NotesRootBuilder stops building that RightColumn/CanvasViewport
    /// split entirely — canvas rendering is later work (see the workspace-shell spec's Р4) — so its one
    /// remaining caller always passes boardViewportGO: null now. A Board-kind page can still exist (POI
    /// editor «Открыть страницу» flows still create one with the default kind — see
    /// NotesDocumentController.CreatePage's callers), and can still be opened as a workspace tab through the
    /// navigator, so this view now shows a one-line placeholder for that case instead of nothing — see
    /// OnActivePageChanged/placeholderGO. The mutual-exclusion path for a real caller-supplied
    /// boardViewportGO is left intact and untouched for exactly the callers that still pass one.
    ///
    /// `root` is re-parented by PageSurfaceHost (Assets/WorldGen/Workspace/Rendering/SurfaceRegistry.cs)
    /// into whichever pane is currently showing a Page tab — see the public Root accessor below.
    /// </summary>
    public class DocumentPageView : MonoBehaviour
    {
        NotesDocumentController documentController;
        GameObject boardViewport;
        GameObject root;
        GameObject viewportGO;
        GameObject placeholderGO;
        RectTransform content;
        Font font;

        // Gates whether OnActivePageChanged is allowed to make `root` visible at all — set only by
        // PageSurfaceHost.Show/Hide (via SetSurfaceVisible). Without this gate, ActivePage can change from
        // OUTSIDE the workspace's own tab machinery — PoiEditorScreen/PoiEditPanel's «Открыть страницу» flow
        // calls NotesDocumentController.OpenPage directly, bypassing WorkspaceController entirely — and
        // OnActivePageChanged would then re-activate `root` in whichever pane it happens to still be
        // parented in, even though no pane's active TAB points at a Page surface at all. ANDing every
        // visibility decision below with this flag means Hide() is authoritative until the next Show().
        bool surfaceVisible;

        readonly List<DocBlockView> rows = new List<DocBlockView>();

        public NotesPage Page { get; private set; }
        public IReadOnlyList<DocBlockView> Rows => rows;
        public RectTransform Content => content;

        /// <summary>The whole page surface's own root, re-parented by PageSurfaceHost.Show into whichever
        /// pane's content area currently shows a Page tab (Task 9). Null before Initialize.</summary>
        public RectTransform Root => root != null ? (RectTransform)root.transform : null;

        /// <summary>PageSurfaceHost's Show/Hide call this — see the surfaceVisible field doc. Re-evaluates
        /// immediately against whatever NotesDocumentController.ActivePage currently is, so Show(...) makes
        /// an already-active page visible even in the (common) case where the OpenPage call right after it
        /// is a no-op because the id was already ActivePage — see PageSurfaceHost.Show's own comment.</summary>
        public void SetSurfaceVisible(bool visible)
        {
            surfaceVisible = visible;
            OnActivePageChanged(documentController != null ? documentController.ActivePage : null);
        }

        /// <summary>Fires whenever the block list changed shape, so the project can be marked dirty and
        /// dependent panels (backlinks) can refresh.</summary>
        public event System.Action OnDocumentMutated;

        public void Initialize(NotesDocumentController docController, RectTransform parent, Font builtinFont,
                              GameObject boardViewportGO)
        {
            documentController = docController;
            boardViewport = boardViewportGO;
            font = builtinFont;

            root = new GameObject("DocumentViewport", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            ThemeService.Tag(root.AddComponent<Image>(), ThemeRole.Bg);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(root.transform, false);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            // Leave room at the top for the floating toolbar. Task 9 no longer builds that toolbar (see the
            // class doc), so this is now a permanent 44px dead strip rather than a live reservation — kept
            // anyway per this task's "look stays exactly as it is" scope for the Document case; revisit if a
            // workspace-native toolbar ever replaces it.
            viewportRect.offsetMin = new Vector2(0f, 0f);
            viewportRect.offsetMax = new Vector2(0f, -44f);
            viewportGO.AddComponent<RectMask2D>();
            scroll.viewport = viewportRect;

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);
            scroll.content = content;

            var vLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            // MUST stay false: forcing expand height makes the group hand every row an equal share of the
            // leftover space, which turns a one-line row into a tall block.
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 2f;
            vLayout.padding = new RectOffset(8, 8, 8, 24);

            // A ContentSizeFitter is correct HERE — this content's height is not controlled by any parent
            // layout group, it is the scroll body. The trap is putting one on a row inside the group below.
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Board-page placeholder — a sibling of Viewport, not a child of it, so it is never subject to
            // the ScrollRect/mask/content-fitter machinery above. See the class doc for why this exists now.
            placeholderGO = new GameObject("BoardPlaceholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(root.transform, false);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<Text>();
            placeholderText.text = "Холст ещё не переехал в новую оболочку.";
            placeholderText.font = font;
            placeholderText.fontSize = 14;
            ThemeService.Tag(placeholderText, ThemeRole.Mut);
            placeholderText.alignment = TextAnchor.MiddleCenter;
            placeholderGO.SetActive(false);

            documentController.OnActivePageChanged += OnActivePageChanged;
            OnActivePageChanged(documentController.ActivePage);
        }

        void OnDestroy()
        {
            if (documentController != null) documentController.OnActivePageChanged -= OnActivePageChanged;
        }

        void OnActivePageChanged(NotesPage page)
        {
            Page = page != null && page.Kind == PageKind.Document ? page : null;
            bool showDocument = Page != null;

            if (boardViewport != null)
            {
                // A real board viewport was supplied (no current caller does this — see the class doc) —
                // preserve the original mutual-exclusion behaviour exactly, gated by surfaceVisible the same
                // way the no-boardViewport branch below is.
                if (root != null) root.SetActive(surfaceVisible && showDocument);
                boardViewport.SetActive(surfaceVisible && !showDocument);
                if (placeholderGO != null) placeholderGO.SetActive(false);
            }
            else
            {
                // No board viewport to hand off to: a Board-kind page still gets root shown, with the
                // scrollable block list swapped for a one-line placeholder instead of rendering nothing.
                // surfaceVisible is what stops this from firing when NO pane's active tab is a Page at all —
                // see the field's own doc for the concrete PoiEditorScreen/PoiEditPanel path that needs it.
                bool boardPage = page != null && !showDocument;
                if (root != null) root.SetActive(surfaceVisible && (showDocument || boardPage));
                if (viewportGO != null) viewportGO.SetActive(showDocument);
                if (placeholderGO != null) placeholderGO.SetActive(boardPage);
            }

            if (showDocument) Rebuild();
        }

        public DocBlockView ViewOf(string blockId)
        {
            foreach (var row in rows)
                if (row != null && row.BlockId == blockId) return row;
            return null;
        }

        public void SetCollapsedAll(bool collapsed)
        {
            if (Page == null) return;
            foreach (var b in Page.Blocks)
                if (b.Kind == BlockKind.Section) b.Collapsed = collapsed;
            Rebuild();
        }

        /// <summary>Rebuilds every row from the page's currently VISIBLE blocks. Cheap enough at the ~100
        /// blocks a session sheet reaches, which is why no virtualization exists — see the spec's known limits.</summary>
        public void Rebuild()
        {
            if (Page == null || content == null) return;

            foreach (var row in rows)
                if (row != null) Destroy(row.gameObject);
            rows.Clear();

            var visible = NotesDocOps.VisibleIndices(Page.Blocks);
            foreach (int index in visible)
            {
                var block = Page.Blocks[index];
                var rowGO = new GameObject($"Row_{block.Kind}", typeof(RectTransform));
                var view = rowGO.AddComponent<DocBlockView>();
                view.Initialize(block, content, font, NotesDocOps.HintFor(Page.Blocks, index));
                view.OnToggleCollapse += OnToggleCollapse;
                view.OnTextChanged += _ => OnDocumentMutated?.Invoke();
                view.Refresh();
                rows.Add(view);
            }
        }

        void OnToggleCollapse(string blockId)
        {
            if (Page == null) return;
            var block = Page.Blocks.Find(b => b.Id == blockId);
            if (block == null) return;
            block.Collapsed = !block.Collapsed;
            Rebuild();
            OnDocumentMutated?.Invoke();
        }

        /// <summary>Rebuilds and then puts the caret where a keyboard operation asked for it. Rebuild throws
        /// away every row object, so the focus request has to be re-issued against the NEW view rather than
        /// the one the keystroke started in.</summary>
        public void RebuildAndFocus(string blockId, int caretOffset)
        {
            Rebuild();
            var view = ViewOf(blockId);
            if (view != null) view.FocusAt(caretOffset);
            OnDocumentMutated?.Invoke();
        }
    }
}
