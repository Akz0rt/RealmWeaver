using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// The navigator column: a header (with the collapse toggle), a search field, and a scrolling tree built
    /// fresh from NavigatorTree.Build(doc, filter) on every rebuild. This view holds no "which row is active"
    /// or "which group is expanded" state of its own — every rebuild re-derives both the row list (from
    /// NavigatorTree, never reimplemented here) and which row counts as active (from WorkspaceLayout, via
    /// WorkspaceOps.PaneAt/SameSurface — see ActiveSurface below), the same "rebuild from the model" rule
    /// TabStripView follows for tabs.
    ///
    /// documentController is an EXTERNAL reference (see WorkspaceBuilder.documentController's own comment) —
    /// NotesRootBuilder owns the live NotesDocumentController, not this class, and nothing wires the two
    /// together before Task 11. Until then this is null and NavigatorView still builds its chrome (header,
    /// search box, empty scroll area) but renders zero groups — NavigatorTree.Build(null, filter) already
    /// returns an empty list, so the only extra guard needed is around the rename/delete calls that would
    /// otherwise NRE on a null documentController.
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

        WorkspaceController controller;
        NotesDocumentController documentController;
        LayoutElement columnLayoutElement;
        Font builtinFont;

        Text headerText;
        GameObject searchGO;
        GameObject scrollGO;
        Transform listContent;

        string filter = "";
        bool rebuildPending;

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

            // Stacks header/search/list top-to-bottom inside the column WorkspaceBuilder already sized.
            // childForceExpandHeight=false so the list's own flexibleHeight=1 (below) is what claims
            // whatever height header+search don't use — the same pattern BuildPane's own vLayout uses for
            // TabStripView-over-ContentArea.
            var vLayout = columnRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 0f;

            view.BuildHeader(root);
            view.BuildSearch(root);
            view.BuildScroll(root);

            controller.OnLayoutChanged += view.RequestRebuild;
            if (documentController != null) documentController.OnDocumentChanged += view.RequestRebuild;

            view.Rebuild();   // first frame already correct, same reasoning as WorkspaceController.Initialize.
            return view;
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnLayoutChanged -= RequestRebuild;
            if (documentController != null) documentController.OnDocumentChanged -= RequestRebuild;
        }

        // ── Construction (chrome — built once, never destroyed by Rebuild) ───────

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

        // ── Rebuild (content only — chrome above is never touched) ───────────────

        void RequestRebuild() => rebuildPending = true;

        /// <summary>Coalesces every OnLayoutChanged/OnDocumentChanged/search-keystroke fired within one frame
        /// into a single Rebuild in LateUpdate — the same fix TabStripView.LateUpdate and
        /// NotesTreeSidebar.LateUpdate use, and for the same reason: committing a rename fires
        /// OnDocumentChanged from inside the InputField's own onEndEdit callback, and Destroy() is deferred
        /// to end of frame, so a synchronous second Rebuild reached from that callback would destroy rows
        /// already marked for destruction and stack a third set on top.
        ///
        /// While a rename is in flight (activeRenameInput != null), the rebuild is held rather than run: this
        /// view subscribes to OnLayoutChanged as well as OnDocumentChanged — unlike NotesTreeSidebar, which
        /// only had the latter — and OnLayoutChanged fires from something as unrelated as clicking a tab.
        /// Without this guard, that would destroy the row the user is mid-rename on, silently discarding
        /// whatever they had typed with no commit and no cancel. rebuildPending stays true (not cleared),
        /// so the held rebuild runs on the first LateUpdate after the rename actually ends (StartRename's
        /// onEndEdit or CancelActiveRename both clear activeRenameInput) — the list is one rename stale in
        /// the meantime, which is the deliberate trade-off, not a bug.</summary>
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

            bool collapsed = controller.Layout.NavigatorCollapsed;
            // Applied every rebuild (not just on the toggle click) so a Task-11 restore that sets
            // NavigatorCollapsed/NavigatorWidth before this view's first Rebuild lands correctly with no
            // extra wiring — see the class doc's "every rebuild re-derives" rule.
            columnLayoutElement.preferredWidth = collapsed ? CollapsedWidth : controller.Layout.NavigatorWidth;
            headerText.text = collapsed ? "☰" : "☰ НАВИГАТОР";
            searchGO.SetActive(!collapsed);
            scrollGO.SetActive(!collapsed);

            // SetActive(false) takes effect immediately; Destroy() is deferred to end of frame — without
            // deactivating first, the old and newly-built rows would both render for one frame (same trap
            // NotesTreeSidebar.Rebuild documents).
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            if (documentController == null) return;   // no document wired yet — Task 11's seam; chrome above still built.

            var activeSurface = ActiveSurface();
            var groups = NavigatorTree.Build(documentController.Document, filter);
            foreach (var group in groups)
                BuildGroup(group, activeSurface);
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

        void BuildGroup(NavGroup group, SurfaceRef activeSurface)
        {
            var groupGO = new GameObject($"Group_{group.Kind}_{group.Title}", typeof(RectTransform));
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildGroupHeader(groupGO.transform, group);

            foreach (var node in group.Nodes)
            {
                bool isActive = activeSurface != null && WorkspaceOps.SameSurface(node.Target, activeSurface);
                BuildNodeRow(groupGO.transform, node, isActive);
            }
        }

        /// <summary>Group headers render for both NavGroupKind values, but only Authored ones get a
        /// rename/delete menu wired on: NavGroup.Id is only populated for Authored groups (see its own doc
        /// comment) — the computed Мир group is not a stored PageGroup at all, so there is nothing behind it
        /// for «Переименовать»/«Удалить» to act on. This is the one place NotesTreeSidebar's ported behaviour
        /// narrows: its group-delete confirm (with the page-count cost report) only has a home here for
        /// Authored groups.</summary>
        void BuildGroupHeader(Transform parent, NavGroup group)
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

            string groupId = group.Id;
            string rawTitle = group.Title;

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
        }

        /// <summary>The live PageGroup's actual page count, looked up fresh at call time (not baked in at
        /// row-build time) — mirrors NotesTreeSidebar.BuildGroupRow's own `group.Pages.Count`, just reached
        /// through documentController instead of holding a direct PageGroup reference the way the old
        /// sidebar did. Null when the group can't be found (already deleted, or no document wired).</summary>
        int? PageGroupPageCount(string groupId)
        {
            var groups = documentController?.Document?.Groups;
            if (groups == null) return null;
            foreach (var g in groups)
                if (g.Id == groupId) return g.Pages.Count;
            return null;
        }

        void BuildNodeRow(Transform parent, NavNode node, bool isActive)
        {
            var rowGO = new GameObject($"Node_{node.Target.Id}", typeof(RectTransform));
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

            // Rename overlay — built (but hidden) for every row up front, same rect as the label, exactly
            // mirroring NotesTreeSidebar.AddRenameAndDelete. The trigger differs: the old sidebar started
            // this from a double-click; here it starts from the context menu's «Переименовать», since the
            // brief moves rename/delete behind the right-click menu instead of always-on affordances.
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

            string pageId = node.Target.Id;
            string rawTitle = node.Title;

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

            var click = rowGO.AddComponent<NavRowClickRouter>();
            click.OnLeftClick = () => controller.Open(node.Target, node.Title, inOtherPane: false);
            click.OnRightClick = screenPos => NavContextMenu.Show(builtinFont, screenPos,
                ("Открыть рядом", () => controller.Open(node.Target, node.Title, inOtherPane: true), false),
                ("Переименовать", () => StartRename(labelGO, input, rawTitle), false),
                ("Удалить", () => ConfirmDialog.Show(builtinFont, "Удалить страницу?", $"«{rawTitle}»", confirmed =>
                {
                    if (confirmed) documentController?.DeletePage(pageId);
                }), true));
        }

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

    /// <summary>The right-click menu, shared by page rows (Открыть рядом / Переименовать / Удалить — see
    /// BuildNodeRow) and Authored group headers (Переименовать / Удалить — see BuildGroupHeader; the World
    /// group gets none, since it is computed with no PageGroup behind it). «рядом», not «справа» —
    /// WorkspaceOps.Open(inOtherPane) means "the pane that is NOT focused" (see the plan ledger's Task 1
    /// decision, carried to this task); with focus already on the right pane, "справа" would open on the
    /// LEFT and the label would lie.
    ///
    /// Structurally follows ConfirmDialog.BuildBase — own overlay Canvas, one instance at a time via a static
    /// activeMenuGO — with one deliberate difference: this backdrop DISMISSES on click. ConfirmDialog's
    /// blocks without dismissing because a confirm/cancel decision must be explicit; a context menu is
    /// ordinary desktop behaviour to click away from.
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
            // instead of introducing a second one — and staying correct under a non-1 CanvasScaler.scaleFactor,
            // which `.position` would not.
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);   // top-left pivot: the menu hangs DOWN-right from the click.

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
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
            // reason. The menu hangs DOWN-RIGHT from the click (pivot (0,1) above), so `local` names its
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

        static void Close()
        {
            if (activeMenuGO != null) UnityEngine.Object.Destroy(activeMenuGO);
            activeMenuGO = null;
        }
    }
}
