using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draws a Document page as a scrolling list of rows, and owns the board-vs-document switch. The board
    /// renderer (NotesCanvasController) is NOT modified: this view simply hides its viewport and shows its
    /// own, both of them children of the same RightColumn.
    /// </summary>
    public class DocumentPageView : MonoBehaviour
    {
        NotesDocumentController documentController;
        GameObject boardViewport;
        GameObject root;
        RectTransform content;
        Font font;

        readonly List<DocBlockView> rows = new List<DocBlockView>();

        public NotesPage Page { get; private set; }
        public IReadOnlyList<DocBlockView> Rows => rows;
        public RectTransform Content => content;

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

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(root.transform, false);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            // Leave room at the top for the floating toolbar, which anchors over this same column.
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
            if (root != null) root.SetActive(showDocument);
            if (boardViewport != null) boardViewport.SetActive(!showDocument);

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
