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
        /// <summary>The names Initialize builds `root`/`placeholderGO`/`addSectionBarGO`/its button under,
        /// hoisted to constants because EnsureWired re-finds them by name after a domain reload — see
        /// RecoverBuiltObjects.</summary>
        const string RootObjectName = "DocumentViewport";
        const string PlaceholderObjectName = "BoardPlaceholder";
        const string AddSectionBarObjectName = "AddSectionBar";
        const string AddSectionButtonObjectName = "Button";

        /// <summary>Height of the «+ Раздел» strip, in the SAME reserved region `viewportRect.offsetMax`
        /// carves out in Initialize below. One constant read by both, per review round 2's Minor — two
        /// unlinked literals that happened to agree were an assertion ("the two never disagree about its
        /// size") the code didn't actually enforce.</summary>
        const float AddSectionBarHeight = 44f;

        NotesDocumentController documentController;
        GameObject boardViewport;
        GameObject root;
        GameObject viewportGO;
        GameObject placeholderGO;
        GameObject addSectionBarGO;
        // The «+ Раздел» Button component itself, not just its GameObject — needed because
        // Button.onClick is a runtime UnityEvent listener list, NOT [SerializeField]-persisted the way the
        // GameObject hierarchy is, so RecoverBuiltObjects re-finding addSectionBarGO after a reload does NOT
        // restore this listener; EnsureWired has to re-add it explicitly. See EnsureWired's own doc for the
        // review round 2 Important this field exists to fix.
        Button addSectionButton;
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
            // class doc), so this was a dead strip until Task 10f's «+ Раздел» bar (BuildAddSectionBar,
            // below) claimed it — the Critical review finding that an empty session page had no way to
            // create its first block. Reserved here, built there, off the SAME AddSectionBarHeight constant,
            // so the two cannot silently drift apart the way two independent literals could.
            viewportRect.offsetMin = new Vector2(0f, 0f);
            viewportRect.offsetMax = new Vector2(0f, -AddSectionBarHeight);
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

            // «+ Раздел» — Task 10f review Critical: CreateSessionSheet stopped seeding a Section
            // (the DM's own ruling — sections are theirs to make), but I2 still requires a non-empty
            // page's FIRST block to be one, and until now nothing could put one there. This lives in the
            // 44px strip viewportRect reserves above (see offsetMax there) rather than opening new screen
            // real estate. Deliberately ONE button, not the «Вставка»/«Ссылка» editor toolbar the DM has
            // separately asked for — that one is Р2 per the umbrella spec.
            addSectionBarGO = BuildAddSectionBar(root.transform);

            documentController.OnActivePageChanged += OnActivePageChanged;
            OnActivePageChanged(documentController.ActivePage);
        }

        /// <summary>Builds the «+ Раздел» bar — see its call site in Initialize for why it exists. Sits in
        /// the strip `viewportGO`'s own offsetMax already carves out (AddSectionBarHeight), as a SIBLING of
        /// viewportGO under `root`, not a child of Content — it must stay fixed at the top of the page, not
        /// scroll away with the rows underneath it. Assigns `addSectionButton` as a side effect — see that
        /// field's own doc for why the GameObject alone is not enough to recover after a reload.</summary>
        GameObject BuildAddSectionBar(Transform parent)
        {
            var barGO = new GameObject(AddSectionBarObjectName, typeof(RectTransform));
            barGO.transform.SetParent(parent, false);
            var barRect = (RectTransform)barGO.transform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, AddSectionBarHeight);
            ThemeService.Tag(barGO.AddComponent<Image>(), ThemeRole.Panel2);

            var btnGO = new GameObject(AddSectionButtonObjectName, typeof(RectTransform));
            btnGO.transform.SetParent(barGO.transform, false);
            var btnRect = (RectTransform)btnGO.transform;
            btnRect.anchorMin = new Vector2(0f, 0.5f);
            btnRect.anchorMax = new Vector2(0f, 0.5f);
            btnRect.pivot = new Vector2(0f, 0.5f);
            btnRect.anchoredPosition = new Vector2(8f, 0f);
            btnRect.sizeDelta = new Vector2(112f, 30f);

            var btnImg = btnGO.AddComponent<Image>();
            ThemeService.Tag(btnImg, ThemeRole.AccentSoft);
            addSectionButton = btnGO.AddComponent<Button>();
            addSectionButton.targetGraphic = btnImg;
            addSectionButton.onClick.AddListener(AddSection);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(btnGO.transform, false);
            var labelRect = (RectTransform)labelGO.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGO.AddComponent<Text>();
            label.text = "+ Раздел";
            label.font = font;
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            ThemeService.Tag(label, ThemeRole.AccentInk);

            return barGO;
        }

        /// <summary>Creates a new top-level Section at the END of the page and focuses it for immediate
        /// typing — the only affordance a session page needs to become usable at all, now that
        /// CreateSessionSheet no longer seeds one (NotesDocOps.CreateSessionSheet's own doc; Task 10f review
        /// Critical). Routed through the SAME NotesDocOps.NewBlock/Insert primitives DocKeyboardOps.OnEnter
        /// uses to create every other block, and the same RebuildAndFocus -> DocBlockView.FocusAt ->
        /// InputField.ActivateInputField path already used to focus a block Enter just created (and the
        /// idiom NavigatorView.StartRename uses for a freshly created name) — no second block-creation or
        /// focus mechanism. Appends rather than inserting at the caret, so the button's meaning does not
        /// depend on what row (if any) happens to be focused — "add a new top-level section", full stop.</summary>
        public void AddSection()
        {
            if (Page == null) return;
            var section = NotesDocOps.NewBlock(BlockKind.Section, 0);
            NotesDocOps.Insert(Page.Blocks, Page.Blocks.Count, section);
            RebuildAndFocus(section.Id, 0);
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
        /// `viewportGO`/`placeholderGO`/`addSectionBarGO`/`addSectionButton`/`documentController`/`font` are
        /// plain, non-[SerializeField] fields, so a domain reload nulls all eight, while the DocumentViewport
        /// GameObject they described survives as live native state in whatever pane PageSurfaceHost last
        /// re-parented it into — and if it was ACTIVE at that moment it stays active. Every access below is
        /// null-guarded, so nothing throws; the guards
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
        /// otherwise) — this just makes the stuck case impossible even if it does not.
        ///
        /// RECOVERING A REFERENCE IS NOT RECOVERING A WIRING (Task 10f review round 2's Important). Every
        /// GameObject/Component field above only needs its REFERENCE restored, because everything they do —
        /// SetActive, ScrollRect.content, being a Transform.Find anchor — is read fresh off the live object
        /// each time. `addSectionButton.onClick` is different: it is a runtime `UnityEvent` listener LIST,
        /// not [SerializeField]-persisted the way the Button's own visible state is, so
        /// `RecoverBuiltObjects` finding the Button again does not mean the click still calls `AddSection` —
        /// it means a Button that highlights on hover and does nothing, indistinguishable from the original
        /// Critical, on the one page (freshly empty) where nothing else can create a first block either. This
        /// was missed once already, in the same paragraph, in the same review's previous round: the field was
        /// added to the "seven" (now eight) above and to RecoverBuiltObjects without re-establishing what
        /// makes the button DO something. Fixed a few lines down with the identical -=/+= discipline the
        /// documentController subscription already uses, for the identical reason: idempotent under repeat
        /// calls, whether or not this particular call is actually recovering from a reload.</summary>
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

            // Same discipline, same reason, for the «+ Раздел» button's onClick — a UnityEvent listener list
            // is exactly as unserialized as the C# event above, and RecoverBuiltObjects only restored WHICH
            // Button this field points at, not what clicking it does. See EnsureWired's own doc for the
            // review finding this fixes.
            if (addSectionButton != null)
            {
                addSectionButton.onClick.RemoveListener(AddSection);
                addSectionButton.onClick.AddListener(AddSection);
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

        /// <summary>Re-finds the six GameObjects/Components Initialize built, after a reload nulled the
        /// fields pointing at them. They cannot be found by hierarchy path the way
        /// MapSurfaceHost.ResolveRootRowBackground finds RootRow: PageSurfaceHost.Show re-parents `root` out
        /// of NotesRootBuilder's PageViewHolder and into whichever pane's ContentArea is showing a Page tab,
        /// so its parent is not fixed. What IS fixed is that it is the only object in the project named
        /// "DocumentViewport" carrying a ScrollRect (that name is constructed in exactly one place —
        /// Initialize, above, from the same constant). Inactive objects must be included: a reload that
        /// happens while no Page tab is showing leaves `root` deactivated, and that case needs recovering
        /// just as much as the visible one.
        ///
        /// The next three come from the ScrollRect's OWN viewport/content — [SerializeField] fields of a
        /// built-in uGUI component, i.e. native state that survives the reload — and from a plain
        /// Transform.Find for the placeholder, which locates inactive children (it is SetActive(false) for
        /// every Document page). `addSectionBarGO`/`addSectionButton` are found the same Transform.Find way —
        /// the bar is never deactivated for a Document page (see its own bar-visibility line in
        /// OnActivePageChanged), but the FindObjectsByType search above and every Find here already return
        /// null gracefully on a page that has never called Initialize, and the null-guarded access pattern
        /// this whole method exists to keep working is worth applying uniformly rather than assuming any one
        /// child can never go missing.
        ///
        /// THIS METHOD ONLY RESTORES REFERENCES. `addSectionButton`'s onClick LISTENER is a separate thing
        /// this method does NOT and cannot restore — see EnsureWired's own doc for why, and where that
        /// actually happens.</summary>
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
            var addSectionBar = found.transform.Find(AddSectionBarObjectName);
            addSectionBarGO = addSectionBar != null ? addSectionBar.gameObject : null;
            var addSectionBtn = addSectionBar != null ? addSectionBar.Find(AddSectionButtonObjectName) : null;
            addSectionButton = addSectionBtn != null ? addSectionBtn.GetComponent<Button>() : null;
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
                if (addSectionBarGO != null) addSectionBarGO.SetActive(showDocument);
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
                // «+ Раздел» only makes sense for a Document page — a Board page has no Blocks/Sections to
                // add one to, and its full-rect placeholder already spans this strip (and the rest of root)
                // with its own message, so there is nothing left for the button to sit over.
                if (addSectionBarGO != null) addSectionBarGO.SetActive(showDocument);
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
