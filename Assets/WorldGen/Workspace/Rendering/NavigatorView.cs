using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// The navigator column: a header (with the collapse toggle), a search field, a scrolling tree built
    /// fresh from NavigatorTree.Build(doc, world, filter) on every rebuild, and — since Task 10h — a «+ Группа»
    /// bar under the tree. This view holds no "which row is active"
    /// or "which group is expanded" state of its own — every rebuild re-derives both the row list (from
    /// NavigatorTree, never reimplemented here) and which row counts as active (from WorkspaceLayout, via
    /// WorkspaceOps.PaneAt/SameSurface — see ActiveSurface below), the same "rebuild from the model" rule
    /// TabStripView follows for tabs.
    ///
    /// documentController is an EXTERNAL reference (see WorkspaceBuilder.documentController's own comment) —
    /// NotesRootBuilder owns the live NotesDocumentController, not this class. WorkspaceBuilder.Awake wires
    /// the two together via EnsureDocumentController's FindFirstObjectByType discovery (Task 9), so this is
    /// non-null in the normal running app; it stays null only where that discovery finds no NotesRootBuilder
    /// in the scene at all (a bare/partial scene, e.g. a harness or test host). A NULL DOCUMENT IS PASSED
    /// STRAIGHT THROUGH to NavigatorTree.Build rather than short-circuited here, as of Task 10e: «Мир» is now
    /// the world's contents (the world map plus every POI) and depends on no document at all, so the
    /// `if (documentController == null) return;` this method used to open with would throw away the whole of
    /// «Мир» in exactly the scene state that most needs the map to stay reachable. That is the same defect
    /// Build's own comment says this arc has already fixed twice, and the version of this paragraph written
    /// in Task 10b explicitly named THIS guard as where it would come back. The guard the view does still
    /// need is around every call that reaches INTO the document — rename, delete, and (Task 10h) create.
    /// Rename/delete are self-limiting: they hang off Authored rows/headers, which a null document produces
    /// none of anyway. «+ Группа» is NOT — it is chrome (BuildCreateGroupBar, built once, never rebuilt),
    /// so it exists and is clickable in exactly the bare scene where documentController is null, and
    /// CreateGroupWithFirstPage therefore opens with an explicit null check of its own.
    ///
    /// poiManager is the OTHER external reference, discovered rather than injected for the same reason
    /// QuickOpenPopup.Attach discovers its own (no Inspector wiring until Task 11), but re-tried on every
    /// miss instead of once — see ResolvePoiManager, which is also the single place this view subscribes to
    /// OnPoisChanged so a newly placed POI appears in «Мир» without waiting for an unrelated layout change.
    ///
    /// Built via the static Create factory onto the GameObject WorkspaceBuilder.BuildNavigatorColumn already
    /// constructed (Image + fixed-width LayoutElement) — this class adds the VerticalLayoutGroup and children
    /// to that SAME GameObject rather than nesting a second root under it, matching "you fill it" from the
    /// brief. WorkspaceController owns Layout.NavigatorCollapsed/NavigatorWidth; this view only reads them
    /// (every rebuild) and writes NavigatorCollapsed back through WorkspaceController.SetNavigatorCollapsed —
    /// never through a parallel PlayerPrefs key, and never NavigatorWidth (no gesture in this plan moves it;
    /// see the task report for that gap).
    /// </summary>
    public class NavigatorView : MonoBehaviour
    {
        public const float CollapsedWidth = 26f;

        const float HeaderHeight = 32f;
        const float SearchHeight = 30f;
        const float GroupHeaderHeight = 24f;
        const float RowHeight = 38f;

        /// <summary>Height of the root «+ Группа» bar. NotesTreeSidebar's AddSmallActionButton used 18f, but
        /// that button lived in a denser sidebar; here it has to read as a peer of this column's own controls,
        /// so it sits between the group header (24) and the search field (30). Cited rather than silently
        /// changed, per Task 10h's "say where you deviate from the restored source".</summary>
        const float ActionBarHeight = 22f;

        /// <summary>Side of the square «+» on an Authored group header. Kept under GroupHeaderHeight so the
        /// button cannot force the header taller than the 24f its LayoutElement asks for.</summary>
        const float AddPageButtonSize = 18f;

        WorkspaceController controller;
        NotesDocumentController documentController;
        PoiManager poiManager;
        LayoutElement columnLayoutElement;
        Font builtinFont;

        Text headerText;
        GameObject searchGO;
        // The search field itself, not just its GameObject — ClearFilter has to write "" back into the
        // VISIBLE field, not only into `filter`, or the box keeps showing a needle that no longer applies.
        InputField searchInput;
        GameObject scrollGO;
        GameObject createGroupGO;
        Transform listContent;

        string filter = "";
        bool rebuildPending;

        /// <summary>"Rename this the moment its row exists" — a note left for the NEXT Rebuild and consumed
        /// by that one only, since Rebuild reads both into locals and nulls both fields on its very first
        /// lines. Whatever was just created has no row yet at the moment of creation (creation raises
        /// OnDocumentChanged, which only sets rebuildPending; rows are built one LateUpdate later), so the
        /// rename cannot simply be called at the creation site.
        ///
        /// EXACTLY ONE of the two is ever set, by whichever button was pressed: «+ Страница» names the page
        /// it made, «+ Группа» names the GROUP it made and leaves that group's scaffolding page as
        /// «Страница 1» — see CreateGroupWithFirstPage for the ruling and its reasoning. They are separate
        /// fields rather than one id plus a kind flag because they are consumed in different places
        /// (BuildNodeRow for a page row, BuildGroupHeader for a header) and a single field would have to be
        /// tested for "is this id a page or a group?" at both.
        ///
        /// REJECTED: letting either live until it is consumed. The new thing can fail to appear in the very
        /// next rebuild (a search filter its name does not match), and a pending id that survives would then
        /// fire a rename at some arbitrary later moment — a row the user is merely looking at suddenly
        /// turning into an edit box. CreatePageAndOpen clears the filter instead, which makes the row's
        /// appearance certain; the one-rebuild lifetime is the belt to that braces.</summary>
        string pendingRenamePageId;
        string pendingRenameGroupId;

        // Rename bookkeeping, ported from NotesTreeSidebar — see StartRename/CancelActiveRename. A non-null
        // activeRenameInput both drives Update()'s Escape handler AND holds LateUpdate's rebuild (see its own
        // comment) — that hold is what keeps Rebuild() from ever destroying the row these fields point at
        // while a rename is in flight, so Rebuild's own clearing of them is defensive, not the thing
        // preventing a dangling reference.
        InputField activeRenameInput;
        GameObject activeRenameLabelGO;
        bool renameCancelled;

        public static NavigatorView Create(RectTransform columnRect, LayoutElement columnLayoutElement,
            WorkspaceController controller, NotesDocumentController documentController)
        {
            var view = columnRect.gameObject.AddComponent<NavigatorView>();
            view.controller = controller;
            view.documentController = documentController;
            view.columnLayoutElement = columnLayoutElement;
            view.builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = columnRect.transform;

            // Stacks header/search/list/create-bar top-to-bottom inside the column WorkspaceBuilder already
            // sized. childForceExpandHeight=false so the list's own flexibleHeight=1 (below) is what claims
            // whatever height the three FIXED-height children around it don't use — the same pattern
            // BuildPane's own vLayout uses for TabStripView-over-ContentArea. That is also what pins the
            // «+ Группа» bar to the bottom of the column; see BuildCreateGroupBar's call site below.
            var vLayout = columnRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 0f;

            view.BuildHeader(root);
            view.BuildSearch(root);
            view.BuildScroll(root);
            // AFTER BuildScroll, so the bar lands BELOW the tree: the List's flexibleHeight=1 claims every
            // pixel header+search+this bar do not, which puts this at the bottom of the column and keeps it
            // there at any column height. That is the brief's "a root-level action below the tree".
            view.BuildCreateGroupBar(root);

            controller.OnLayoutChanged += view.RequestRebuild;
            if (documentController != null) documentController.OnDocumentChanged += view.RequestRebuild;

            view.Rebuild();   // first frame already correct, same reasoning as WorkspaceController.Initialize.
            return view;
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnLayoutChanged -= RequestRebuild;
            if (documentController != null) documentController.OnDocumentChanged -= RequestRebuild;
            // PoiManager outlives this view (it is a scene component; the workspace column is built and torn
            // down with the shell), so an un-removed handler would keep this destroyed MonoBehaviour alive in
            // the manager's invocation list and fire RequestRebuild on it forever — the leak half of the pair
            // ResolvePoiManager's own comment describes. Unity's `!=` reads a DESTROYED manager as null, in
            // which case there is no list left to remove from and skipping is correct.
            if (poiManager != null) poiManager.OnPoisChanged -= RequestRebuild;
        }

        /// <summary>The live POI store, discovered on every miss (the same shape MapScreenController.Pois()
        /// uses, and for the first of its two reasons: no Inspector slot before Task 11's scene edit) — NOT
        /// once at Create like QuickOpenPopup.Attach, because this view must also be able to find a
        /// PoiManager that did not exist yet when the shell was built.
        ///
        /// SUBSCRIBING HERE, on the null→found transition, is what makes the subscription exactly-once
        /// without a separate bool: the field IS the guard. Rebuild reads this every pass, so a re-discovery
        /// can only happen after the field went null again (the manager was destroyed — its invocation list
        /// died with it), never while a live handler is registered. Without any subscription at all this
        /// view would only rebuild on OnLayoutChanged/OnDocumentChanged (an earlier review flagged that gap
        /// while nothing world-shaped was in the tree yet), so placing a POI would leave «Мир» stale until
        /// the user happened to click a tab.
        ///
        /// The other half of the domain-reload hazard is absent by construction here: a Play-mode recompile
        /// wipes both this field and PoiManager's own event (both plain non-serialized), and nothing re-runs
        /// Create — WorkspaceBuilder.Awake deliberately does NOT revive this view (see its own comment), so
        /// there is no path that re-subscribes a still-subscribed instance and double-fires.</summary>
        PoiManager ResolvePoiManager()
        {
            if (poiManager != null) return poiManager;
            poiManager = FindFirstObjectByType<PoiManager>(FindObjectsInactive.Include);
            if (poiManager != null) poiManager.OnPoisChanged += RequestRebuild;
            return poiManager;
        }

        // ── Construction (chrome — built once, never destroyed by Rebuild) ───────
        // The one exception in this section is AddActionButton at its end: it is a shared BUILDER, and its
        // other caller (BuildGroupHeader's «+») runs on every rebuild like the rest of the tree.

        void BuildHeader(Transform parent)
        {
            var headerGO = new GameObject("Header", typeof(RectTransform));
            headerGO.transform.SetParent(parent, false);
            var headerImg = headerGO.AddComponent<Image>();
            ThemeService.Tag(headerImg, ThemeRole.Panel2, 0.9f);
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerGO.AddComponent<LayoutElement>().preferredHeight = HeaderHeight;
            // Toggling routes through the controller (the ONLY place that mutates Layout — see
            // WorkspaceController's own class doc) rather than writing Layout.NavigatorCollapsed here
            // directly. Reads Layout fresh on click rather than caching a local `collapsed` bool, so this
            // stays correct even if something else (Task 11's restore) changes NavigatorCollapsed between
            // clicks without this view's Rebuild having run yet.
            headerBtn.onClick.AddListener(() => controller.SetNavigatorCollapsed(!controller.Layout.NavigatorCollapsed));

            var headerTextGO = new GameObject("Text", typeof(RectTransform));
            headerTextGO.transform.SetParent(headerGO.transform, false);
            var text = headerTextGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 11;
            text.fontStyle = FontStyle.Bold;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = headerTextGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = Vector2.zero;
            headerText = text;
        }

        void BuildSearch(Transform parent)
        {
            var go = new GameObject("Search", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = SearchHeight;
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);
            input.textComponent = text;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(go.transform, false);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "Поиск...";
            placeholder.font = builtinFont;
            placeholder.fontSize = 12;
            placeholder.fontStyle = FontStyle.Italic;
            ThemeService.Tag(placeholder, ThemeRole.Mut);
            placeholder.alignment = TextAnchor.MiddleLeft;
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(6f, 0f);
            placeholderRect.offsetMax = new Vector2(-6f, 0f);
            input.placeholder = placeholder;

            // Deferred through RequestRebuild (not an immediate Rebuild() call here) so a keystroke and a
            // same-frame OnDocumentChanged/OnLayoutChanged coalesce into one rebuild instead of two — see
            // RequestRebuild/LateUpdate below. The field itself is never destroyed by Rebuild (it lives
            // outside listContent), so deferring costs nothing but one frame of latency.
            input.onValueChanged.AddListener(value =>
            {
                filter = value;
                RequestRebuild();
            });

            searchGO = go;
            searchInput = input;
        }

        void BuildScroll(Transform parent)
        {
            var go = new GameObject("List", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(go.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLayout.spacing = 2f;
            contentVLayout.childControlWidth = true;
            contentVLayout.childControlHeight = false;
            contentVLayout.childForceExpandWidth = true;
            // On the SCROLL CONTENT only — its size is driven by its own children's total height, and no
            // parent LayoutGroup controls it (Content is not itself managed by anything but the ScrollRect),
            // which is exactly the one case where a ContentSizeFitter belongs on a layout-managed child.
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            listContent = contentGO.transform;
            scrollGO = go;
        }

        /// <summary>«+ Группа», restored from NotesTreeSidebar.cs:198-201 (the pre-deletion file, commit
        /// 9abca26^). Task 7 carried rename and delete into this view and Task 10c then deleted the sidebar,
        /// so BOTH create affordances left the app with it — «не вижу кнопок для добавления страниц заметок».
        /// The visual treatment is the deleted AddSmallActionButton's (Accent 0.8 fill, AccentInk 11px label,
        /// left-aligned with a 6px inset), so the button the DM lost is recognisably the button they get
        /// back. Its HEIGHT is the one changed value — see ActionBarHeight's own doc.
        ///
        /// CHROME, not content: built once here, never destroyed by Rebuild — the same arrangement the header
        /// and the search field use. Rebuild only toggles its visibility with the collapse state (see there).
        ///
        /// THE DOMAIN-RELOAD QUESTION this arc has now had to answer eight times, answered explicitly rather
        /// than left open. A runtime onClick listener is not [SerializeField]-persisted, so a Play-mode script
        /// reload wipes it while this GameObject survives — the shape DocumentPageView.EnsureWired exists to
        /// repair for its own «+ Раздел». It needs no repair HERE, and the difference is not luck: that class
        /// HAS a post-reload recovery path (EnsureWired/RecoverBuiltObjects re-find its objects by name), so a
        /// button it recovered without re-adding the listener came back visible and inert. This view has no
        /// recovery path at all — WorkspaceBuilder.Awake refuses to revive it on purpose and says why ("a
        /// functionally DEAD object… every tab/close/plus Button's runtime AddListener callback are all gone
        /// too"). After a reload `controller`/`documentController` are null, every row's NavRowClickRouter
        /// delegate is gone, and all three subscriptions that could set rebuildPending died with them, so
        /// LateUpdate never rebuilds again either. This button is dead exactly like the collapse toggle, the
        /// search box and every row — one inert view, Task 11's to revive wholesale, not a new gap to patch
        /// with an EnsureWired that would have nothing to be called from.</summary>
        void BuildCreateGroupBar(Transform parent)
        {
            createGroupGO = AddActionButton(parent, "+ Группа", ActionBarHeight, 11,
                TextAnchor.MiddleLeft, 6f, CreateGroupWithFirstPage);
        }

        /// <summary>The one small-action button shape, shared by the root «+ Группа» bar and each Authored
        /// group header's «+». Ported from the deleted NotesTreeSidebar.AddSmallActionButton (:521-547) with
        /// the label metrics parameterised, because the header variant is a square «+» rather than a
        /// full-width labelled bar — one builder over two near-copies, since the two must not drift apart in
        /// colour or in the "invisible-fill trick does not apply here" sense (both are meant to be SEEN; that
        /// was the whole complaint).</summary>
        GameObject AddActionButton(Transform parent, string label, float height, int fontSize,
            TextAnchor alignment, float leftInset, Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = fontSize;
            ThemeService.Tag(text, ThemeRole.AccentInk);
            text.alignment = alignment;
            text.raycastTarget = false;   // clicks belong to btn's own image; same rule as every label here.
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(leftInset, 0f);
            textRect.offsetMax = Vector2.zero;

            return go;
        }

        // ── Creation (the two affordances Task 10c's deletion took with it) ──────

        /// <summary>«+ Группа»: create the group, then create its first page — the SAME two-step
        /// NotesTreeSidebar.cs:198-201 performed, and for a reason that is now written down in
        /// NavigatorTree.Build's N3: a group whose node list comes out empty is omitted from the tree
        /// entirely. Creating a bare group would therefore render as nothing at all, and the button would
        /// look exactly as broken as the missing button it replaces.
        ///
        /// THE RENAME IS ARMED ON THE GROUP, NOT ON THE PAGE — review ruling, and the reasoning is worth
        /// keeping because the first cut did the opposite for a plausible-sounding reason («both buttons
        /// share one post-create behaviour, and the seed value is page-flavoured»). What the DM pressed was
        /// "add group", so the GROUP is the thing they are naming; the page inside it is scaffolding N3
        /// forces on us, not the thing being created. It keeps «Страница 1» and can be renamed later from
        /// its own row like any other page.
        ///
        /// Hence renameNewPage: false below — CreatePageAndOpen is still the one path a new page takes
        /// (create, clear filter, open), and this only declines the naming prompt that would otherwise land
        /// on the wrong object.</summary>
        void CreateGroupWithFirstPage()
        {
            // Explicit ternary/null-check, never `?.`: documentController is a UnityEngine.Object whose
            // overloaded `==` reports a DESTROYED reference as null, and `?.` bypasses that overload — the
            // same idiom and the same reason as Rebuild's and PageGroupPageCount's own reads. Reachable in a
            // way the rename/delete calls are not: this button is chrome, so it exists even in the bare scene
            // that has no NotesRootBuilder to discover (see the class doc).
            if (documentController == null) return;
            var group = documentController.CreateGroup("Новая группа");
            if (group == null) return;
            CreatePageAndOpen(group.Id, renameNewPage: false);
            // AFTER the page exists, not before: N3 omits a group with no nodes, so if CreatePageAndOpen had
            // bailed there would be no header for this note to land on and it would simply be dropped by the
            // rebuild that cannot find it — which is the correct outcome, and needs no guard of its own.
            pendingRenameGroupId = group.Id;
        }

        /// <summary>The single path a newly created page takes, reached from both «+ Группа» and a group
        /// header's «+» — create, show, and (for «+ Страница») offer the name. Not two paths: Step 4's "one
        /// path for «a page is now on screen»" is what stops the two buttons from drifting into different
        /// post-create behaviour. `renameNewPage` is the ONE thing they differ on, and it is a parameter
        /// rather than a second method precisely so the difference is a single visible word at each call
        /// site instead of two bodies to keep in step.
        ///
        /// NAMED BY RENAMING, NOT BY NUMBERING — a DELIBERATE improvement on the restored behaviour, not an
        /// accident of reimplementation, and flagged here because a reviewer diffing against
        /// NotesTreeSidebar.cs:362-364 will otherwise read it as a deviation to correct. The old sidebar
        /// named the page «Страница N» and left the user to find rename afterwards (which, behind a
        /// right-click menu since Task 7, is now less discoverable than it was). «Страница N» is still the
        /// value the field STARTS with, computed exactly as the old call site computed it (the group's page
        /// count before this insert, +1), so dismissing the rename with Escape still leaves a sensible name
        /// rather than an empty one — and it is the name the page simply KEEPS when «+ Группа» declines the
        /// prompt in favour of naming the group.
        ///
        /// OPENED THROUGH WorkspaceController.Open, the same call BuildNodeRow's left-click makes — not
        /// through NotesDocumentController.OpenPage, which would change the notes controller's ActivePage
        /// without any tab pointing at it (the exact drift ActiveSurface's own doc describes). PageSurfaceHost
        /// .Show is what calls OpenPage, once the tab exists.
        ///
        /// THE FILTER IS CLEARED. With a needle in the search box, a page named «Страница N» almost certainly
        /// does not match it, N3 then omits it (and possibly its whole group), and the button silently appears
        /// to do nothing — the DM's original complaint reproduced one layer down. Rejected alternative:
        /// leaving the filter alone and letting the pending rename wait for a rebuild that shows the row —
        /// see pendingRenamePageId's own doc for why a long-lived pending rename is worse.
        ///
        /// Every step below raises something this view is subscribed to (OnDocumentChanged from CreatePage,
        /// OnLayoutChanged from Open, onValueChanged from the cleared search field). All of them only set
        /// rebuildPending, so they coalesce into ONE Rebuild in the next LateUpdate — the ordering here
        /// carries no meaning beyond readability.</summary>
        void CreatePageAndOpen(string groupId, bool renameNewPage)
        {
            if (documentController == null || string.IsNullOrEmpty(groupId)) return;

            int existing = PageGroupPageCount(groupId) ?? 0;
            // PageKind.Document EXPLICITLY, overriding CreatePage's `kind = PageKind.Board` default. The
            // deleted sidebar relied on that default and so did the first cut of this method — faithful to
            // the restored source, and wrong for the DM. A Board page has no editor in this shell:
            // DocumentPageView.OnActivePageChanged (DocumentPageView.cs:395-419) nulls `Page` for anything
            // that is not Document, hides the block viewport, shows the one-line placeholder «Холст ещё не
            // переехал в новую оболочку.» and hides the «+ Раздел» bar with it. So «+ Страница» would hand
            // back a page with nothing to type into — «не вижу кнопок для добавления страниц заметок» for
            // the third time, one layer deeper each time (no button → a button the search filter hid → a
            // button whose page cannot be written on).
            //
            // The DEFAULT stays Board deliberately and must not be flipped: CreatePage's own doc says it is
            // there so every pre-existing call site keeps its behaviour, and the canvas work a later
            // sub-project owns is what will need it. The kind is decided HERE, at the one call site that
            // wants a writable page, which is also why no test can carry this rule — see the task report.
            var page = documentController.CreatePage(groupId, $"Страница {existing + 1}", PageKind.Document);
            if (page == null) return;   // the group id named nothing — CreatePage's own contract.

            ClearFilter();
            controller.Open(new SurfaceRef { Kind = SurfaceKind.Page, Id = page.Id }, page.Name, inOtherPane: false);
            if (renameNewPage) pendingRenamePageId = page.Id;
        }

        /// <summary>Writes the empty needle into BOTH the model and the visible field. `filter` first, so a
        /// scene where the InputField somehow never got built (or was destroyed) still ends up with the right
        /// model rather than a half-applied one; the field's own onValueChanged then redundantly assigns the
        /// same "" and requests a rebuild, which is harmless. Setting .text to a value it already holds is a
        /// no-op in uGUI's InputField (its setter compares first), so this fires nothing in the common case
        /// of an empty search box.</summary>
        void ClearFilter()
        {
            filter = "";
            if (searchInput != null) searchInput.text = "";
        }

        // ── Rebuild (content only — chrome above is never touched) ───────────────

        void RequestRebuild() => rebuildPending = true;

        /// <summary>Coalesces every OnLayoutChanged/OnDocumentChanged/OnPoisChanged/search-keystroke fired
        /// within one frame into a single Rebuild in LateUpdate — the same fix TabStripView.LateUpdate and
        /// QuickOpenPopup.LateUpdate use, and for the same reason: committing a rename fires
        /// OnDocumentChanged from inside the InputField's own onEndEdit callback, and Destroy() is deferred
        /// to end of frame, so a synchronous second Rebuild reached from that callback would destroy rows
        /// already marked for destruction and stack a third set on top.
        ///
        /// While a rename is in flight (activeRenameInput != null), the rebuild is held rather than run: this
        /// view subscribes to OnLayoutChanged and PoiManager.OnPoisChanged as well as OnDocumentChanged —
        /// unlike the retired notes sidebar, which only had the last — and either of the first two fires from
        /// something as unrelated as clicking a tab or renaming a POI in a different pane's editor
        /// (PoiManager.UpdatePoiName raises OnPoisChanged as of Task 10e's review round).
        /// Without this guard, that would destroy the row the user is mid-rename on, silently discarding
        /// whatever they had typed with no commit and no cancel. rebuildPending stays true (not cleared),
        /// so the held rebuild runs on the first LateUpdate after the rename actually ends (StartRename's
        /// onEndEdit or CancelActiveRename both clear activeRenameInput) — the list is one rename stale in
        /// the meantime, which is the deliberate trade-off, not a bug.
        ///
        /// Since Task 10h a rename can also be started by Rebuild ITSELF, not only by a user picking
        /// «Переименовать»: «+ Страница» arms one on the page row it builds (BuildNodeRow's `autoRename`)
        /// and «+ Группа» on the group HEADER it builds (BuildGroupHeader's). Nothing above changes — either
        /// rename is held, ended and cleared by the same three paths as any other — but the entry points are
        /// named here so the next reader does not have to conclude from the field alone that only a gesture
        /// can set it.</summary>
        void LateUpdate()
        {
            if (!rebuildPending) return;
            if (activeRenameInput != null) return;
            rebuildPending = false;
            Rebuild();
        }

        void Update()
        {
            if (activeRenameInput != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CancelActiveRename();
        }

        void Rebuild()
        {
            // Defensive, not load-bearing: LateUpdate's own guard above (activeRenameInput != null → hold
            // the rebuild) already stops this method from ever running while a rename is in flight, so these
            // three fields are normally already at rest by the time this line runs. Cleared again here only
            // in case a future call site invokes Rebuild() directly, bypassing that guard — Create() itself
            // does exactly that for the first frame, where they are trivially already null/false anyway.
            activeRenameInput = null;
            activeRenameLabelGO = null;
            renameCancelled = false;

            // Read-and-clear on the FIRST lines, before anything can throw or early-return below: the notes
            // are for THIS rebuild only, and a pass that fails to find the row must drop one rather than
            // leave it armed for a later one (see pendingRenamePageId's own doc).
            string renamePageId = pendingRenamePageId;
            string renameGroupId = pendingRenameGroupId;
            pendingRenamePageId = null;
            pendingRenameGroupId = null;

            bool collapsed = controller.Layout.NavigatorCollapsed;

            // DROPPED, not merely unused, when the column is collapsed. Collapsing does NOT stop rows from
            // being built — only scrollGO is deactivated below, while listContent (its descendant) is still
            // destroyed and repopulated on every pass — so without these two lines an auto-rename would
            // happily arm itself inside a deactivated subtree. StartRename would then SetActive(true) an
            // object whose parent chain is still off: activeInHierarchy stays false, so
            // Select()/ActivateInputField no-op, the field never focuses, and onEndEdit can never fire.
            // LateUpdate's rename-in-flight hold would then block every further rebuild — until the user
            // pressed Escape, which Update() polls off the keyboard directly and which needs no focus, so
            // the wedge is recoverable in principle. Only in principle: nothing on screen would look
            // focused, so nobody would think to press it. Severe, not literally permanent.
            //
            // Unreachable today, and these lines are what keep it that way rather than luck: both create
            // affordances are hidden while collapsed (createGroupGO below; the per-group «+» lives inside
            // scrollGO), and the click→LateUpdate round trip is same-frame, so nothing can collapse in
            // between. Cheaper to drop the notes here than to rely on that argument staying true.
            if (collapsed)
            {
                renamePageId = null;
                renameGroupId = null;
            }
            // Applied every rebuild (not just on the toggle click) so a Task-11 restore that sets
            // NavigatorCollapsed/NavigatorWidth before this view's first Rebuild lands correctly with no
            // extra wiring — see the class doc's "every rebuild re-derives" rule.
            columnLayoutElement.preferredWidth = collapsed ? CollapsedWidth : controller.Layout.NavigatorWidth;
            headerText.text = collapsed ? "☰" : "☰ НАВИГАТОР";
            searchGO.SetActive(!collapsed);
            scrollGO.SetActive(!collapsed);
            // Hidden with the rest of the content, not merely narrowed: a 26px-wide column (CollapsedWidth)
            // would render «+ Группа» as a clipped gold stub with no readable label — and the collapsed
            // column's whole contract is "the ☰ toggle and nothing else".
            createGroupGO.SetActive(!collapsed);

            // SetActive(false) takes effect immediately; Destroy() is deferred to end of frame — without
            // deactivating first, the old and newly-built rows would both render for one frame (same trap
            // TabStripView.Rebuild documents for its own tabs).
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            // Explicit ternary, not `?.`: `documentController` is a UnityEngine.Object, whose overloaded `==`
            // reports a DESTROYED-but-not-null reference as null — `?.` bypasses that overload and would hand
            // Build a live-looking corpse. Same idiom, same reason, at QuickOpenPopup.cs:218 and
            // DocumentPageView.cs:70. A null document is no longer short-circuited before this line — see
            // the class doc.
            var doc = documentController != null ? documentController.Document : null;
            var activeSurface = ActiveSurface();
            var groups = NavigatorTree.Build(doc, WorldObjectSource.Collect(ResolvePoiManager()), filter);
            foreach (var group in groups)
                BuildGroup(group, activeSurface, renamePageId, renameGroupId);
        }

        /// <summary>The row highlighted as "active" is the surface shown by the FOCUSED pane's active tab —
        /// not NotesDocumentController.ActivePage. Those two can differ the moment a page is open in a tab
        /// without also being the notes controller's own active page (e.g. a background tab), so keying off
        /// ActivePage the way NotesTreeSidebar does would drift from what the workspace actually shows.</summary>
        SurfaceRef ActiveSurface()
        {
            PaneState pane = WorkspaceOps.PaneAt(controller.Layout, controller.Layout.FocusedPane);
            if (pane?.Tabs == null || pane.ActiveIndex < 0 || pane.ActiveIndex >= pane.Tabs.Count) return null;
            return pane.Tabs[pane.ActiveIndex].Surface;
        }

        void BuildGroup(NavGroup group, SurfaceRef activeSurface, string renamePageId, string renameGroupId)
        {
            var groupGO = new GameObject($"Group_{group.Kind}_{group.Title}", typeof(RectTransform));
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // EVERY group has a header now. Task 10b's render-without-header branch existed for the one
            // headerless Pinned group (the world map, above «Мир»); Task 10e folded that row into «Мир» as
            // its first member and deleted the kind, so there is no longer any group whose Title is "".
            // Non-empty id only, so a null/empty renameGroupId can never match the World group's Id — which
            // is "" by contract (NavGroup.Id's own doc) and would otherwise compare equal to a cleared note
            // and arm a rename on a header that has no rename overlay built at all.
            bool renameThisGroup = !string.IsNullOrEmpty(renameGroupId) && group.Id == renameGroupId;
            BuildGroupHeader(groupGO.transform, group, renameThisGroup);

            foreach (var node in group.Nodes)
            {
                bool isActive = activeSurface != null && WorkspaceOps.SameSurface(node.Target, activeSurface);
                // Matched on Target.Kind AND Target.Id, never on title: a page id and a POI guid are both
                // opaque strings, and «Мир» rows carry POI guids — the same identity rule
                // NavigatorTreeSelfTests' own fixture (a POI and a page deliberately sharing a name) exists
                // to enforce. Decided HERE rather than inside BuildNodeRow so the Kind test sits next to the
                // isActive test it parallels, and so BuildNodeRow's Page-only branch receives a flag that is
                // already known to be false for every other row.
                bool autoRename = renamePageId != null && node.Target != null
                    && node.Target.Kind == SurfaceKind.Page && node.Target.Id == renamePageId;
                BuildNodeRow(groupGO.transform, node, isActive, autoRename);
            }
        }

        /// <summary>Called for every group, but only Authored ones get the rename/delete menu — or, since
        /// Task 10h, the «+» that adds a page to them. All three want the same thing and are refused for the
        /// same reason: NavGroup.Id is only populated for Authored groups (see its own doc comment), the
        /// computed «Мир» group is not a stored PageGroup at all, so there is no group for
        /// «Переименовать»/«Удалить» to act on and none for CreatePage to add into. A «+» on «Мир» would be
        /// an affordance for creating a page inside the world's contents, which is not a thing that exists.
        /// This is the one place NotesTreeSidebar's ported behaviour narrows: its group-delete confirm (with
        /// the page-count cost report) and its per-group «+ Страница» both only have a home here for
        /// Authored groups.
        ///
        /// `autoRename` is true for at most one header per rebuild: the group «+ Группа» just made. Like
        /// `autoRename` on a node row it can only be true where the rename overlay actually exists — the
        /// early return below is what guarantees that, and BuildGroup additionally refuses to match an empty
        /// id so the World group's «» can never satisfy it.</summary>
        void BuildGroupHeader(Transform parent, NavGroup group, bool autoRename)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = GroupHeaderHeight;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            // Display-only uppercasing — NavGroup.Title/NavigatorTree.WorldGroupTitle stay whatever case the
            // model uses ("Мир"); the brief's "10px uppercase" is a rendering rule, not a data rule, so it
            // has no business changing NavigatorTree's own constant.
            text.text = (group.Title ?? "").ToUpperInvariant();
            text.font = builtinFont;
            text.fontSize = 10;
            text.fontStyle = FontStyle.Bold;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.LowerLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 2f);
            textRect.offsetMax = Vector2.zero;

            if (group.Kind != NavGroupKind.Authored) return;

            // Clicks must land on the router below, not the label — same reasoning as BuildNodeRow's label.
            text.raycastTarget = false;
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel, 0f);   // invisible, still raycasts (same trick as row bg).

            string groupId = group.Id;
            string rawTitle = group.Title;

            // «+ Страница», restored from NotesTreeSidebar.cs:362-364. The old sidebar rendered it as a
            // full-width labelled bar BELOW the group's last page; here it is a square «+» on the header
            // itself, per the brief. Reachable at all times — the old placement scrolled away with a long
            // group, and after Task 10c there is no other way to add a page to an existing group at all.
            //
            // The label's right edge is pulled in by the button's width FIRST, and this line must stay above
            // the rename overlay below, which copies textRect's four values verbatim. That is what keeps the
            // two out of each other's way: the overlay inherits the narrowed rect, so an active rename never
            // reaches under the «+» and the «+» never steals a click from the input field's right end.
            // Moving this line (or the button) below the overlay would hand the overlay the full-width rect
            // and put them back in contention. It also keeps a long group title off the «+» — by uGUI's own
            // text layout (this Text is left at the default Wrap/Truncate), NOT by a mask: this header has no
            // RectMask2D, unlike a node row, so the containment BuildNodeRow's own comment insists on is
            // absent here and the narrowed rect is all there is.
            textRect.offsetMax = new Vector2(-(AddPageButtonSize + 6f), 0f);
            //
            // The «+» therefore stays live DURING a rename, which is the harmless order: clicking it
            // deactivates the input field, so uGUI fires onEndEdit — the rename commits — before this
            // button's own onClick runs.
            //
            // KNOWN, ACCEPTED: this button's rect is a right-click dead zone. NavRowClickRouter is reached
            // today only through StandaloneInputModule's fallback — no component on the header handles
            // pointerDOWN, so the module searches the hierarchy for an IPointerClickHandler and finds the
            // router. A Button (Selectable) DOES handle pointerDown, and it handles it for every mouse
            // button (its own early-out for non-left runs after the handler has already been counted), so
            // over these 18 pixels the module settles on the button and dispatches the click to it alone.
            // Button.OnPointerClick then ignores the right button and nothing happens: the
            // «Переименовать»/«Удалить» menu is unreachable from the «+». The rest of the header still
            // offers it.
            // renameNewPage: true — unlike «+ Группа», the page IS the thing the DM asked for here, so it is
            // the thing they get to name. No default on that parameter, deliberately: it is the single point
            // the two buttons differ on, and a default would let a third call site inherit one behaviour
            // silently.
            var addPageGO = AddActionButton(go.transform, "+", AddPageButtonSize, 14,
                TextAnchor.MiddleCenter, 0f, () => CreatePageAndOpen(groupId, renameNewPage: true));
            // Anchored to the header's right edge by hand: this GameObject's parent is a plain header rect
            // with no LayoutGroup on it, so the LayoutElement AddActionButton attaches (which the root bar's
            // VerticalLayoutGroup parent DOES read) governs nothing here and the rect must be sized outright.
            var addPageRect = addPageGO.GetComponent<RectTransform>();
            addPageRect.anchorMin = new Vector2(1f, 0.5f);
            addPageRect.anchorMax = new Vector2(1f, 0.5f);
            addPageRect.pivot = new Vector2(1f, 0.5f);
            addPageRect.sizeDelta = new Vector2(AddPageButtonSize, AddPageButtonSize);
            addPageRect.anchoredPosition = new Vector2(-4f, 0f);

            // Rename overlay — same construction and StartRename/onEndEdit shape BuildNodeRow uses for a
            // page row, retargeted at RenameGroup. The input shows/submits the RAW stored title, not the
            // uppercased display string above.
            var inputGO = new GameObject("RenameInput", typeof(RectTransform));
            inputGO.transform.SetParent(go.transform, false);
            var inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.anchorMin = textRect.anchorMin;
            inputRect.anchorMax = textRect.anchorMax;
            inputRect.offsetMin = textRect.offsetMin;
            inputRect.offsetMax = textRect.offsetMax;
            var inputImg = inputGO.AddComponent<Image>();
            ThemeService.Tag(inputImg, ThemeRole.Elev);
            var input = inputGO.AddComponent<InputField>();
            input.targetGraphic = inputImg;

            var inputTextGO = new GameObject("Text", typeof(RectTransform));
            inputTextGO.transform.SetParent(inputGO.transform, false);
            var inputText = inputTextGO.AddComponent<Text>();
            inputText.font = builtinFont;
            inputText.fontSize = 11;
            ThemeService.Tag(inputText, ThemeRole.Txt);
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            var inputTextRect = inputTextGO.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(4f, 0f);
            inputTextRect.offsetMax = new Vector2(-4f, 0f);
            input.textComponent = inputText;
            inputGO.SetActive(false);

            input.onEndEdit.AddListener(newText =>
            {
                bool wasCancelled = renameCancelled;
                activeRenameInput = null;
                activeRenameLabelGO = null;
                renameCancelled = false;
                if (wasCancelled) return;
                inputGO.SetActive(false);
                textGO.SetActive(true);
                if (!string.IsNullOrWhiteSpace(newText)) documentController?.RenameGroup(groupId, newText.Trim());
            });

            var click = go.AddComponent<NavRowClickRouter>();
            click.OnRightClick = screenPos => NavContextMenu.Show(builtinFont, screenPos,
                ("Переименовать", () => StartRename(textGO, input, rawTitle), false),
                ("Удалить", () => ConfirmDialog.Show(builtinFont, "Удалить группу?",
                    // The REAL page count, not group.Nodes.Count — Nodes reflects the current search
                    // filter (NavigatorTree's N3), and the cost report must name what deletion actually
                    // takes, not just what a filtered view happens to be showing right now.
                    $"«{rawTitle}» и все её страницы ({PageGroupPageCount(groupId) ?? group.Nodes.Count})", confirmed =>
                {
                    if (confirmed) documentController?.DeleteGroup(groupId);
                }), true));

            // Last in this method, for the same reason BuildNodeRow arms its own rename last: the header is
            // fully built (label, «+», overlay, onEndEdit, menu) before StartRename hides the label and
            // focuses the field. `rawTitle` is «Новая группа» here, pre-selected — so the DM types over it.
            if (autoRename) StartRename(textGO, input, rawTitle);
        }

        /// <summary>The live PageGroup's actual page count, looked up fresh at call time (not baked in at
        /// row-build time) — mirrors NotesTreeSidebar.BuildGroupRow's own `group.Pages.Count`, just reached
        /// through documentController instead of holding a direct PageGroup reference the way the old
        /// sidebar did. Null when the group can't be found (already deleted, or no document wired).
        ///
        /// TWO callers now, wanting the same number for opposite reasons: the group-delete confirm reports it
        /// as a cost, and CreatePageAndOpen seeds «Страница N» from it. Both are call-time reads of the SAME
        /// count the deleted sidebar's two call sites read, which is why they share one lookup rather than
        /// each re-deriving it. They differ only in how they take the null: the confirm falls back to the
        /// filtered node count it can see, the creator to 0 — after which CreatePage returns null for the
        /// same missing group and the whole action stops.</summary>
        int? PageGroupPageCount(string groupId)
        {
            // Explicit ternary, not `documentController?.Document`: `?.` skips Unity's overloaded `==` and
            // would treat a DESTROYED controller as alive — the same idiom, and the same reason, as Rebuild's
            // own read a few methods up. Harmless here today (a null flows to the `return null` the caller
            // already handles), but a file that states the rule in one place and breaks it in another teaches
            // the wrong one.
            var doc = documentController != null ? documentController.Document : null;
            var groups = doc != null ? doc.Groups : null;
            if (groups == null) return null;
            foreach (var g in groups)
                if (g.Id == groupId) return g.Pages.Count;
            return null;
        }

        /// <summary>`autoRename` is true for exactly one row per rebuild at most: the page CreatePageAndOpen
        /// just made (see pendingRenamePageId). It can only ever be true inside the Page branch below —
        /// BuildGroup computes it from Target.Kind==Page — which is what keeps the "no rename overlay is
        /// even CONSTRUCTED for a non-Page row" rule that branch's own comment establishes.</summary>
        void BuildNodeRow(Transform parent, NavNode node, bool isActive, bool autoRename)
        {
            // Falls back to Target.Kind when Id is empty (the world-map row, whose Id is "" by contract with
            // WorkspaceOps.NewDefault's seed tab) — otherwise this reads as "Node_" in the Hierarchy,
            // indistinguishable from a bug, rather than naming what the row actually is. A «Мир» POI row does
            // carry an Id (the POI's guid), so it names itself.
            string idPart = string.IsNullOrEmpty(node.Target.Id) ? node.Target.Kind.ToString() : node.Target.Id;
            var rowGO = new GameObject($"Node_{idPart}", typeof(RectTransform));
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            // Structural containment for long titles — Task 6 learned twice that arithmetic truncation
            // alone is not a guarantee, because the ancestor layout can still compress a row below whatever
            // width truncation was budgeted against. Clips every child (label, active edge, rename input) to
            // this row's REAL post-layout rect; any ellipsis is cosmetic, and this file adds none.
            rowGO.AddComponent<RectMask2D>();

            var bg = rowGO.AddComponent<Image>();
            if (isActive) ThemeService.Tag(bg, ThemeRole.AccentSoft, 0.9f);
            else ThemeService.Tag(bg, ThemeRole.Panel, 0f);   // alpha 0 — invisible but still raycasts (the
                                                                // same trick DraggableDivider's idle state uses),
                                                                // needed so the row is clickable when inactive.

            var edgeGO = new GameObject("ActiveEdge", typeof(RectTransform));
            edgeGO.transform.SetParent(rowGO.transform, false);
            var edgeImg = edgeGO.AddComponent<Image>();
            ThemeService.Tag(edgeImg, ThemeRole.Accent);
            edgeImg.raycastTarget = false;   // purely decorative; same reasoning as the label above.
            var edgeRect = edgeGO.GetComponent<RectTransform>();
            edgeRect.anchorMin = new Vector2(0f, 0f);
            edgeRect.anchorMax = new Vector2(0f, 1f);
            edgeRect.pivot = new Vector2(0f, 0.5f);
            edgeRect.sizeDelta = new Vector2(2f, 0f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeGO.SetActive(isActive);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(rowGO.transform, false);
            var label = labelGO.AddComponent<Text>();
            label.text = node.Title;
            label.font = builtinFont;
            label.fontSize = 13;
            ThemeService.Tag(label, isActive ? ThemeRole.Txt : ThemeRole.Mut);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            // Clicks must land on rowGO's own NavRowClickRouter unambiguously — same reasoning TabStripView
            // gives for its own title.raycastTarget=false ("clicks must reach tabBtn, not the label").
            label.raycastTarget = false;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            string pageId = node.Target.Id;
            string rawTitle = node.Title;

            var click = rowGO.AddComponent<NavRowClickRouter>();
            click.OnLeftClick = () => controller.Open(node.Target, node.Title, inOtherPane: false);
            // Branch on Target.Kind, not on which group the row came from — and as of Task 10e that
            // distinction carries real weight rather than being a precaution: EVERY «Мир» row is a non-Page
            // node now (the world map, and one PoiEditor row per POI), where before it was only the single
            // pinned map row. The reason is about the TARGET: a node with no page behind it has nothing for
            // «Переименовать»/«Удалить» to act on. «Удалить» would call documentController.DeletePage(pageId)
            // with an id that is a POI's guid and matches no page — a silent no-op at best, and at worst a
            // page id collision away from deleting an unrelated note; «Переименовать» would edit a label with
            // no backing store to persist the new name into. Both stay off the menu (see the else branch:
            // «Открыть рядом» is the only item a non-Page row gets) rather than being wired to quietly do
            // nothing, and no path from here reaches documentController with a POI id.
            if (node.Target.Kind == SurfaceKind.Page)
            {
                // Rename overlay — built (but hidden) here, same rect as the label, exactly mirroring
                // NotesTreeSidebar.AddRenameAndDelete. The triggers differ: the old sidebar started this
                // from a double-click; here it starts from the context menu's «Переименовать», since the
                // brief moves rename/delete behind the right-click menu instead of always-on affordances —
                // and, since Task 10h, from `autoRename` at the end of this branch, for a page the user has
                // just created and has not named yet.
                //
                // Built inside this Page-only branch, not unconditionally for every row (a prior round built
                // it for every row and guarded only the menu item that could reach it): a hidden
                // RenamePage(pageId, …) listener with pageId=="" for a non-Page row was harmless ONLY because
                // no trigger could reach it today — the menu item was never wired for that branch. That guard
                // sat one level away from the actual hazard: any future affordance that calls StartRename
                // directly (the double-click trigger this comment used to describe, before rename moved
                // behind the context menu) would silently re-arm a rename that persists nothing. Not
                // constructing the overlay at all for a non-Page row removes the hazard instead of merely
                // leaving it untriggered. That future affordance has since arrived — `autoRename` — and the
                // rule held: it is computed in BuildGroup from Target.Kind==Page, so it cannot be true
                // outside this branch, and the parameter's own doc says so rather than relying on it.
                var inputGO = new GameObject("RenameInput", typeof(RectTransform));
                inputGO.transform.SetParent(rowGO.transform, false);
                var inputRect = inputGO.GetComponent<RectTransform>();
                inputRect.anchorMin = labelRect.anchorMin;
                inputRect.anchorMax = labelRect.anchorMax;
                inputRect.offsetMin = labelRect.offsetMin;
                inputRect.offsetMax = labelRect.offsetMax;
                var inputImg = inputGO.AddComponent<Image>();
                ThemeService.Tag(inputImg, ThemeRole.Elev);
                var input = inputGO.AddComponent<InputField>();
                input.targetGraphic = inputImg;

                var inputTextGO = new GameObject("Text", typeof(RectTransform));
                inputTextGO.transform.SetParent(inputGO.transform, false);
                var inputText = inputTextGO.AddComponent<Text>();
                inputText.font = builtinFont;
                inputText.fontSize = 13;
                ThemeService.Tag(inputText, ThemeRole.Txt);
                inputText.alignment = TextAnchor.MiddleLeft;
                inputText.supportRichText = false;
                var inputTextRect = inputTextGO.GetComponent<RectTransform>();
                inputTextRect.anchorMin = Vector2.zero;
                inputTextRect.anchorMax = Vector2.one;
                inputTextRect.offsetMin = new Vector2(4f, 0f);
                inputTextRect.offsetMax = new Vector2(-4f, 0f);
                input.textComponent = inputText;
                inputGO.SetActive(false);

                input.onEndEdit.AddListener(newText =>
                {
                    bool wasCancelled = renameCancelled;
                    activeRenameInput = null;
                    activeRenameLabelGO = null;
                    renameCancelled = false;
                    if (wasCancelled) return;
                    inputGO.SetActive(false);
                    labelGO.SetActive(true);
                    if (!string.IsNullOrWhiteSpace(newText)) documentController?.RenamePage(pageId, newText.Trim());
                });

                click.OnRightClick = screenPos => NavContextMenu.Show(builtinFont, screenPos,
                    ("Открыть рядом", () => controller.Open(node.Target, node.Title, inOtherPane: true), false),
                    ("Переименовать", () => StartRename(labelGO, input, rawTitle), false),
                    ("Удалить", () => ConfirmDialog.Show(builtinFont, "Удалить страницу?", $"«{rawTitle}»", confirmed =>
                    {
                        if (confirmed) documentController?.DeletePage(pageId);
                    }), true));

                // Last in this method, so the row is fully built (label, overlay, onEndEdit, menu) before the
                // rename arms itself — StartRename hides the label and focuses the field, and doing that
                // mid-construction would leave a half-wired row holding the user's keystrokes.
                //
                // Safe to set activeRenameInput from INSIDE a Rebuild: Rebuild's own defensive clearing of
                // that field happens on its first lines, well above this, and LateUpdate already set
                // rebuildPending=false before calling in — so this arms the rename-in-flight hold for the
                // NEXT frame rather than fighting the pass that is running.
                if (autoRename) StartRename(labelGO, input, rawTitle);
            }
            else
            {
                click.OnRightClick = screenPos => NavContextMenu.Show(builtinFont, screenPos,
                    ("Открыть рядом", () => controller.Open(node.Target, node.Title, inOtherPane: true), false));
            }
        }

        /// <summary>The text arrives PRE-SELECTED, which Task 10h's auto-rename depends on (typing must
        /// replace «Страница N», not append to it) and which the context-menu rename gets for free: uGUI's
        /// ActivateInputField calls SelectAll on focus whenever InputField.onFocusSelectAll is set, and that
        /// property defaults to true and is never turned off here. Written down so nobody adds a redundant
        /// SelectAll — or, worse, "fixes" the defaults elsewhere and quietly breaks this.</summary>
        void StartRename(GameObject labelGO, InputField input, string rawValue)
        {
            activeRenameLabelGO = labelGO;
            activeRenameInput = input;
            renameCancelled = false;
            labelGO.SetActive(false);
            input.gameObject.SetActive(true);
            input.text = rawValue;
            input.Select();
            input.ActivateInputField();
        }

        void CancelActiveRename()
        {
            if (activeRenameInput == null) return;
            renameCancelled = true;
            activeRenameInput.gameObject.SetActive(false);
            if (activeRenameLabelGO != null) activeRenameLabelGO.SetActive(true);
            activeRenameInput = null;
            activeRenameLabelGO = null;
        }
    }

    /// <summary>Routes a row's left-click to "open" and right-click to "context menu", both through ONE
    /// IPointerClickHandler on the row's own GameObject. Deliberately not a Button (whose own OnPointerClick
    /// would fire alongside this one on the SAME GameObject, double-dispatching the left-click) — a plain
    /// sibling class in this file, the same arrangement TabStripView.cs uses for TabHoverReveal.</summary>
    class NavRowClickRouter : MonoBehaviour, IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action<Vector2> OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke(eventData.position);
        }
    }

    /// <summary>The right-click menu, shared by three callers, all in BuildNodeRow/BuildGroupHeader above:
    /// Page rows (Открыть рядом / Переименовать / Удалить), non-Page rows — the world map and every POI, i.e.
    /// the whole of «Мир» since Task 10e (Открыть рядом ONLY — there is no page behind any of them for the
    /// other two items to act on), and Authored group headers (Переименовать / Удалить; the World group gets
    /// none, since it is computed with no PageGroup behind it). «рядом», not «справа» —
    /// WorkspaceOps.Open(inOtherPane) means "the pane that is NOT focused" (see the plan ledger's Task 1
    /// decision, carried to this task); with focus already on the right pane, "справа" would open on the
    /// LEFT and the label would lie.
    ///
    /// Structurally follows ConfirmDialog.BuildBase — own overlay Canvas, one instance at a time via a static
    /// activeMenuGO — with one deliberate difference: this backdrop DISMISSES on click. ConfirmDialog's
    /// blocks without dismissing because a confirm/cancel decision must be explicit; a context menu is
    /// ordinary desktop behaviour to click away from.
    ///
    /// activeMenuGO survives a domain reload only as a dangling reference — see Close()'s own doc for the
    /// RECOMPILE GAP this class shares with SurfaceRegistry.cs's MapSurfaceHost, and the by-name fallback
    /// that recovers from it.
    /// </summary>
    static class NavContextMenu
    {
        const float MenuWidth = 176f;
        const float ItemHeight = 30f;

        static GameObject activeMenuGO;

        public static void Show(Font font, Vector2 screenPos, params (string Label, Action OnClick, bool Danger)[] items)
        {
            Close();

            var canvasGO = new GameObject("NavContextMenuCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeMenuGO = canvasGO;

            var backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(canvasGO.transform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = Color.clear;   // invisible, but still raycasts — see NavigatorView's row bg.
            var backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.onClick.AddListener(Close);
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            var panelGO = new GameObject("Menu");
            panelGO.transform.SetParent(canvasGO.transform, false);   // after backdrop → wins raycasts over it.
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel2);
            var panelRect = panelGO.GetComponent<RectTransform>();
            // Anchored to the canvas's CENTRE, not its bottom-left corner (Vector2.zero) — see the block
            // below where `local` is computed for the full reasoning. Fixed here rather than switched to
            // `panelRect.position = screenPos`, matching the established idiom (NotesToolbar.cs:195-196)
            // instead of introducing a second one — and this anchor/local pairing itself stays correct
            // regardless of CanvasScaler.scaleFactor, unlike `.position`. The screen CLAMP below this block
            // does NOT share that independence — it bounds against raw Screen.width/height, which is only
            // equal to this canvas's own local units because the CanvasScaler added a few lines down keeps
            // its defaults (ConstantPixelSize, scaleFactor 1); see the clamp's own comment.
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);   // top-left pivot: the menu hangs DOWN-right from the click.

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            // TRUE, not false: this is the group that must APPLY each item's own preferredHeight (ItemHeight,
            // set via LayoutElement in AddItem below) to its actual rect.sizeDelta. With
            // childControlHeight=false, uGUI's HorizontalOrVerticalLayoutGroup.GetChildSizes takes the OTHER
            // branch entirely: it sets BOTH min and preferred from child.rect.sizeDelta[axis] directly and
            // never consults LayoutUtility/the LayoutElement at all — not "uses preferredHeight for stacking
            // but forgets to write it back", the read itself never happens. So every item would be stacked
            // (not just rendered) at whatever height its freshly-created RectTransform's OWN sizeDelta
            // already carried (never touched by AddItem, which sets only LayoutElement.preferredHeight), AND
            // the group's own total preferred height — what the ContentSizeFitter above reads — would
            // likewise collapse to sum-of-those-sizeDeltas + spacing + padding, not to ItemHeight-per-item.
            // Exactly the defect QuickOpenPopup.cs shipped once already in this same arc (commit b187ceb,
            // "quick-open rows need childControlHeight=true, not false") and ProjectMenuBar.cs's «Файл»/«Вид»
            // dropdown (ProjectMenuBar.cs:376-377) avoids by using this same true/false pair for the
            // identical shape: control height, but don't force-expand it past the child's own preferredHeight.
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelRect.sizeDelta = new Vector2(MenuWidth, 0f);

            foreach (var item in items)
                AddItem(font, panelGO.transform, item.Label, item.OnClick, item.Danger);

            // Forces the deferred layout pass (ContentSizeFitter above, plus the Canvas's own first-frame
            // sizing) to run NOW, before anything below reads a size off either rect — the same idiom
            // MapSurfaceHost.Show uses before reading corners (SurfaceRegistry.cs) and
            // GenerationProgressUI/GenerationScreenUI use before reading a content-fitted card's height.
            // Without this, `canvasRect` can still be unsized on the very first menu of the session (Show()
            // can run before uGUI has ever laid this hierarchy out), and `panelRect`'s ContentSizeFitter has
            // not yet written a real height back — either one reading 0 would make the clamp below silently
            // no-op, reproducing this exact defect one line later.
            Canvas.ForceUpdateCanvases();

            // ScreenSpaceOverlay + camera=null is the established conversion for this canvas kind — see
            // NotesToolbar.cs and DungeonViewController.cs's own ScreenPointToLocalPointInRectangle calls.
            // ScreenPointToLocalPointInRectangle returns coordinates relative to canvasRect's OWN pivot — a
            // freshly-AddComponent<Canvas> RectTransform defaults to pivot (0.5, 0.5), i.e. CENTRE-relative,
            // the exact fact DungeonViewController.cs:1703-1705 documents for its own projection ("...the
            // projection's local space is CENTRE-relative. area is stretched with a 0.5 pivot, so they
            // already coincide"). panelRect's anchor was set to that same (0.5, 0.5) point above, so `local`
            // can be used as anchoredPosition directly.
            var canvasRect = canvasGO.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local);

            // Clamp to the screen, in the same centre-relative space `local` is already in. Bounds are
            // Screen.width/height (not canvasRect.rect — a fresh ScreenSpaceOverlay canvas is not guaranteed
            // to have finished sizing to the screen on its own creation frame, forced rebuild above or not),
            // the same bound the in-repo clamp precedent (PoiInfoPopup.Reposition) uses for exactly that
            // reason. Unlike the anchor/`local` pairing above, this specific bound is NOT scaleFactor-robust:
            // it is correct only because the CanvasScaler this menu's canvas got a few lines up was added
            // with defaults (ConstantPixelSize, scaleFactor 1), making one canvas-local unit == one screen
            // pixel. A future non-1 scaleFactor on this canvas would need `canvasRect.rect` here instead.
            // The menu hangs DOWN-RIGHT from the click (pivot (0,1) above), so `local` names its
            // LEFT/TOP edge — the RIGHT/BOTTOM edges (MenuWidth and the fitted height further out) have to
            // stay on screen too, not just the anchor point, or a click near an edge still pushes part of
            // the menu off it. Height comes from LayoutUtility.GetPreferredHeight, not panelRect.rect.height
            // or .sizeDelta.y, for the same "measure the real post-fitter value" reason
            // GenerationProgressUI.ApplyCardHeight (GenerationProgressUI.cs:152-158) reads its own card the
            // same way instead of trusting a size that may not have been written back yet.
            float menuHeight = LayoutUtility.GetPreferredHeight(panelRect);
            float halfScreenW = Screen.width * 0.5f;
            float halfScreenH = Screen.height * 0.5f;
            const float ScreenMargin = 4f;
            float x = Mathf.Clamp(local.x, -halfScreenW + ScreenMargin, halfScreenW - MenuWidth - ScreenMargin);
            float y = Mathf.Clamp(local.y, -halfScreenH + menuHeight + ScreenMargin, halfScreenH - ScreenMargin);
            panelRect.anchoredPosition = new Vector2(x, y);
        }

        static void AddItem(Font font, Transform parent, string label, Action onClick, bool danger = false)
        {
            var go = new GameObject($"Item_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = ItemHeight;

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Panel2, 0f);   // invisible hit-area; no hover state (out of scope).
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            // Closes the menu BEFORE invoking the callback — «Удалить» opens ConfirmDialog next, and the
            // menu must not still be on screen (or worse, still be the active singleton) underneath it.
            btn.onClick.AddListener(() => { Close(); onClick?.Invoke(); });

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 12;
            ThemeService.Tag(text, danger ? ThemeRole.Danger : ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
        }

        /// <summary>Destroys the current menu (if any) and clears the singleton — re-acquiring the
        /// GameObject BY NAME first when the static reference itself is already gone. A Play-mode script
        /// recompile wipes a plain, non-[SerializeField] field like `activeMenuGO` while the Unity
        /// GameObject it pointed at survives the reload completely untouched (this arc's recurring
        /// "RECOMPILE GAP" family — see SurfaceRegistry.cs's MapSurfaceHost class doc for the first sighting
        /// and ResolveRootRowBackground for its own by-hierarchy-path recovery). Without this fallback, the
        /// very first Close() after a reload sees `activeMenuGO == null` and does nothing — but
        /// "NavContextMenuCanvas" (a full-screen, sortingOrder-1000, click-to-dismiss backdrop) is still
        /// alive in the scene, and nothing is left able to dismiss it: every click for the rest of the
        /// session lands on an invisible backdrop instead of whatever is underneath it.
        ///
        /// ResolveRootRowBackground re-acquires via `transform.Find` on a known relative path, because that
        /// class is a MonoBehaviour anchored to a fixed parent. This is a static class with no such anchor
        /// (the canvas is a root-level GameObject — see Show()'s `new GameObject("NavContextMenuCanvas")`
        /// with no SetParent call), so `GameObject.Find` by that same exact name is the equivalent lookup
        /// here. Not cached anywhere beyond the static field itself — this method already nulls that field
        /// out on every call, so there is nothing extra to invalidate.</summary>
        static void Close()
        {
            if (activeMenuGO == null)
                activeMenuGO = GameObject.Find("NavContextMenuCanvas");
            if (activeMenuGO != null) UnityEngine.Object.Destroy(activeMenuGO);
            activeMenuGO = null;
        }
    }
}
