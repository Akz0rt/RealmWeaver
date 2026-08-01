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
    /// into whichever pane is currently showing a Page tab — see the public Root accessor below. Because that
    /// makes this view's own visibility something the workspace drives, none of the fields below may quietly
    /// forget what they point at: EnsureWired re-establishes all of them after a Play-mode script reload, and
    /// its doc explains what a page stuck visible over the map camera costs when they do.
    /// </summary>
    public class DocumentPageView : MonoBehaviour
    {
        /// <summary>The names Initialize builds `root`/`placeholderGO` under, hoisted to constants because
        /// EnsureWired re-finds them by name after a domain reload — see RecoverBuiltObjects.</summary>
        const string RootObjectName = "DocumentViewport";
        const string PlaceholderObjectName = "BoardPlaceholder";

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

            root = new GameObject(RootObjectName, typeof(RectTransform));
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
            placeholderGO = new GameObject(PlaceholderObjectName, typeof(RectTransform));
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

        /// <summary>Re-establishes everything a Play-mode script reload wipes on THIS component, so Show/Hide
        /// work again afterwards. Called only from NotesRootBuilder.EnsureBuilt's already-built (early-return)
        /// branch — the one place that knows the difference between "never built" (Initialize's job) and
        /// "built, then reloaded".
        ///
        /// WHAT GOES WRONG WITHOUT IT — and it is NOT merely "the page fails to reappear". `root`/`content`/
        /// `viewportGO`/`placeholderGO`/`documentController`/`font` are plain, non-[SerializeField] fields, so
        /// a domain reload nulls all six, while the DocumentViewport GameObject they described survives as
        /// live native state in whatever pane PageSurfaceHost last re-parented it into — and if it was ACTIVE
        /// at that moment it stays active. Every access below is null-guarded, so nothing throws; the guards
        /// instead turn Hide() -> SetSurfaceVisible(false) -> OnActivePageChanged into a SILENT NO-OP against
        /// a null `root`, so `root.SetActive(false)` never fires and NOTHING in the rest of that session can
        /// ever hide it again. DocumentViewport carries its own opaque ThemeRole.Bg Image, so a pane that
        /// then shows the WorldMap surface gets painted over by it — MapSurfaceHost.SetBackgroundsEnabled
        /// knows about exactly three Images and this stray one is not among them, which is Task 9 review
        /// round 1's Critical (map hidden behind an opaque UI rect) arriving again through a different door.
        ///
        /// The last line is what actually kills that symptom, and it does so INSIDE NotesRootBuilder's own
        /// recovery, without depending on WorkspaceBuilder's path running at all: `surfaceVisible` is a plain
        /// bool that the same reload already reset to false, so re-running OnActivePageChanged with the
        /// recovered `root` deactivates a stuck-visible viewport immediately. WorkspaceController.SyncSurfaces
        /// then re-asserts the correct state a moment later (Show for a pane whose active tab is a Page, Hide
        /// otherwise) — this just makes the stuck case impossible even if it does not.</summary>
        public void EnsureWired(NotesDocumentController docController, Font builtinFont)
        {
            if (docController != null) documentController = docController;
            if (builtinFont != null) font = builtinFont;

            // `root == null` while this component exists at all is the reliable "wiped by a reload" signal —
            // Initialize always assigns it, and it is exactly the kind of field a reload always forgets. On an
            // ordinary (non-reload) call — WorkspaceBuilder.Awake asking NotesRootBuilder for a document that
            // its own Awake already built — this is false and the re-assert below is correctly skipped.
            bool lostToReload = root == null;
            if (lostToReload) RecoverBuiltObjects();
            if (root == null) return;   // not found: stay inert rather than half-wired.

            // C# delegates are never serialized, so this subscription is gone after a reload no matter how
            // the fields above were recovered. `-=` before `+=` keeps it at exactly one for the non-reload
            // caller, where the subscription Initialize made is still live.
            if (documentController != null)
            {
                documentController.OnActivePageChanged -= OnActivePageChanged;
                documentController.OnActivePageChanged += OnActivePageChanged;
            }

            // `rows` is a readonly List<DocBlockView> — Unity serializes neither, so a reload empties it while
            // the row GameObjects it tracked survive as children of the recovered `content`. Re-adopt them, or
            // the next Rebuild() would find nothing to destroy and stack a SECOND full set of rows on top of
            // the first. Their own fields are wiped too, so they are good for nothing except being destroyed —
            // which is precisely what Rebuild does with them, through its existing disposal path rather than a
            // second one added here.
            //
            // Guarded on the hazard ITSELF (a tracking list that disagrees with the live children) rather than
            // on `lostToReload`, which is only a proxy for it: Rebuild is the sole thing that ever parents a
            // child to `content`, so "no rows tracked but children present" IS the desynchronised state and
            // cannot arise any other way. On the ordinary non-reload call this is a no-op — an empty `rows`
            // there means Rebuild never produced any, so childCount is 0 — and it stays correct if a future
            // change ever makes `root` recoverable without `content` being.
            if (rows.Count == 0 && content != null && content.childCount > 0)
                rows.AddRange(content.GetComponentsInChildren<DocBlockView>(true));

            if (!lostToReload) return;

            OnActivePageChanged(documentController != null ? documentController.ActivePage : null);
        }

        /// <summary>Re-finds the four GameObjects Initialize built, after a reload nulled the fields pointing
        /// at them. They cannot be found by hierarchy path the way MapSurfaceHost.ResolveRootRowBackground
        /// finds RootRow: PageSurfaceHost.Show re-parents `root` out of NotesRootBuilder's PageViewHolder and
        /// into whichever pane's ContentArea is showing a Page tab, so its parent is not fixed. What IS fixed
        /// is that it is the only object in the project named "DocumentViewport" carrying a ScrollRect (that
        /// name is constructed in exactly one place — Initialize, above, from the same constant). Inactive
        /// objects must be included: a reload that happens while no Page tab is showing leaves `root`
        /// deactivated, and that case needs recovering just as much as the visible one.
        ///
        /// The remaining three come from the ScrollRect's OWN viewport/content — [SerializeField] fields of a
        /// built-in uGUI component, i.e. native state that survives the reload — and from a plain
        /// Transform.Find for the placeholder, which locates inactive children (it is SetActive(false) for
        /// every Document page).</summary>
        void RecoverBuiltObjects()
        {
            ScrollRect found = null;
            foreach (var scroll in FindObjectsByType<ScrollRect>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (scroll == null || scroll.name != RootObjectName) continue;
                // On the (unexpected) tie of two same-named ScrollRects, prefer one that still has its
                // content wired — a half-built stray is never the live page surface.
                if (found == null || (found.content == null && scroll.content != null)) found = scroll;
            }
            if (found == null) return;

            root = found.gameObject;
            viewportGO = found.viewport != null ? found.viewport.gameObject : null;
            content = found.content;
            var placeholder = found.transform.Find(PlaceholderObjectName);
            placeholderGO = placeholder != null ? placeholder.gameObject : null;
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
                view.Initialize(block, content, font);
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
