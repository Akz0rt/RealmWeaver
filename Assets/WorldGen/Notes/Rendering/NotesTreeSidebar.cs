using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Collapsible accordion tree: groups expand to show their pages. Selecting a page
    /// opens it via NotesDocumentController. Collapsible via a header toggle button, which
    /// shrinks the whole sidebar column down to a narrow strip (just the toggle) so the
    /// canvas can reclaim that width when the tree isn't needed. A search box filters the
    /// list by group title or page name; each row supports double-click-to-rename and a
    /// persistent "×" delete button (confirmed via ConfirmDialog). The column's expanded
    /// width is user-draggable and persisted across sessions in PlayerPrefs.
    /// </summary>
    public class NotesTreeSidebar : MonoBehaviour
    {
        const string ExpandedWidthPrefsKey = "NotesSidebar.ExpandedWidth";
        const float DefaultExpandedWidth = 200f;
        public const float MinExpandedWidth = 40f;
        public const float MaxExpandedWidth = 400f;
        public const float CollapsedWidth = 28f;
        const float DividerWidth = 8f;

        NotesDocumentController documentController;
        Font builtinFont;
        Transform listContent;
        GameObject listGO;
        GameObject headerTextGO;
        GameObject addGroupButtonGO;
        GameObject searchInputGO;
        GameObject dividerGO;
        InputField searchInput;
        LayoutElement rootLayoutElement;
        bool expanded = true;
        bool rebuildPending;
        string searchQuery = "";
        float expandedWidth;

        // Keyed by page Id so OnActivePageChanged can recolor just the affected rows in place
        // instead of going through Rebuild() — Rebuild() destroys and recreates every row's
        // GameObject, which would reset Unity's double-click tracking (it's keyed by GameObject
        // identity) before a second click could ever register as a double-click.
        readonly Dictionary<string, Image> pageRowImages = new Dictionary<string, Image>();

        InputField activeRenameInput;
        GameObject activeRenameLabelGO;
        bool renameCancelled;

        public void Initialize(NotesDocumentController docController, Transform parent)
        {
            documentController = docController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootGO = new GameObject("NotesTreeSidebar");
            rootGO.transform.SetParent(parent, false);
            // Solid panel background so the sidebar reads as its own column instead of blending
            // into the notes canvas behind it.
            var rootImg = rootGO.AddComponent<Image>();
            ThemeService.Tag(rootImg, ThemeRole.Panel);
            var vLayout = rootGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;
            expandedWidth = PlayerPrefs.GetFloat(ExpandedWidthPrefsKey, DefaultExpandedWidth);
            rootLayoutElement = rootGO.AddComponent<LayoutElement>();
            // minWidth/flexibleWidth default to -1 ("ignore this component's opinion"). Left
            // unset, Unity falls back to vLayout's OWN report of these properties to ITS
            // parent (notesAreaGO) — vLayout (as a LayoutGroup) reports minWidth from its
            // content and flexibleWidth as the sum of its children's flexible values (each
            // forced to >=1 by vLayout.childForceExpandWidth above, needed so header/search/
            // list stretch to fill this column — but that same flag makes vLayout report a
            // nonzero flexibleWidth upward too, causing this whole column to compete for and
            // absorb a share of notesAreaGO's leftover width on top of preferredWidth). Pinning
            // both to 0 here makes preferredWidth (set via SetExpandedWidth's own clamp) the
            // only thing that determines this column's width.
            rootLayoutElement.minWidth = 0f;
            rootLayoutElement.flexibleWidth = 0f;
            rootLayoutElement.preferredWidth = expandedWidth;

            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rootGO.transform, false);
            var headerImg = headerGO.AddComponent<Image>();
            ThemeService.Tag(headerImg, ThemeRole.Panel2, 0.9f);
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            headerBtn.onClick.AddListener(ToggleExpanded);

            headerTextGO = new GameObject("Text");
            headerTextGO.transform.SetParent(headerGO.transform, false);
            var headerText = headerTextGO.AddComponent<Text>();
            headerText.text = "☰ СТРАНИЦЫ";
            headerText.font = builtinFont;
            headerText.fontSize = 11;
            headerText.fontStyle = FontStyle.Bold;
            ThemeService.Tag(headerText, ThemeRole.Mut);
            headerText.alignment = TextAnchor.MiddleLeft;
            var headerTextRect = headerTextGO.GetComponent<RectTransform>();
            headerTextRect.anchorMin = new Vector2(0f, 0f);
            headerTextRect.anchorMax = new Vector2(1f, 1f);
            headerTextRect.offsetMin = new Vector2(6f, 0f);
            headerTextRect.offsetMax = Vector2.zero;

            searchInputGO = new GameObject("SearchInput");
            searchInputGO.transform.SetParent(rootGO.transform, false);
            searchInputGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            var searchImg = searchInputGO.AddComponent<Image>();
            ThemeService.Tag(searchImg, ThemeRole.Elev);
            searchInput = searchInputGO.AddComponent<InputField>();
            searchInput.targetGraphic = searchImg;

            var searchTextGO = new GameObject("Text");
            searchTextGO.transform.SetParent(searchInputGO.transform, false);
            var searchText = searchTextGO.AddComponent<Text>();
            searchText.font = builtinFont;
            searchText.fontSize = 12;
            ThemeService.Tag(searchText, ThemeRole.Txt);
            searchText.alignment = TextAnchor.MiddleLeft;
            searchText.supportRichText = false;
            var searchTextRect = searchTextGO.GetComponent<RectTransform>();
            searchTextRect.anchorMin = Vector2.zero;
            searchTextRect.anchorMax = Vector2.one;
            searchTextRect.offsetMin = new Vector2(6f, 0f);
            searchTextRect.offsetMax = new Vector2(-6f, 0f);
            searchInput.textComponent = searchText;

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(searchInputGO.transform, false);
            var placeholderText = placeholderGO.AddComponent<Text>();
            placeholderText.text = "Поиск...";
            placeholderText.font = builtinFont;
            placeholderText.fontSize = 12;
            placeholderText.fontStyle = FontStyle.Italic;
            ThemeService.Tag(placeholderText, ThemeRole.Mut);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(6f, 0f);
            placeholderRect.offsetMax = new Vector2(-6f, 0f);
            searchInput.placeholder = placeholderText;

            searchInput.onValueChanged.AddListener(value =>
            {
                searchQuery = value;
                Rebuild();
            });

            listGO = new GameObject("List");
            listGO.transform.SetParent(rootGO.transform, false);
            listGO.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var listScrollRect = listGO.AddComponent<ScrollRect>();
            listScrollRect.horizontal = false;
            listScrollRect.vertical = true;
            listScrollRect.scrollSensitivity = 30f;
            listScrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport uses RectMask2D — no Image needed (same pattern as MapEditorPanel's
            // scroll area), avoids the alpha=0 stencil issue that made Mask+Image(clear)
            // clip all children.
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(listGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            listScrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLayout.spacing = 2f;
            contentVLayout.childControlWidth = true;
            contentVLayout.childControlHeight = false;
            contentVLayout.childForceExpandWidth = true;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            listScrollRect.content = contentRect;
            listContent = contentGO.transform;

            addGroupButtonGO = AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
            });

            documentController.OnDocumentChanged += RequestRebuild;
            documentController.OnActivePageChanged += OnActivePageChanged;
            Rebuild();

            // Anchored at this column's own right edge, pivot=(1,0.5) makes the bar extend
            // LEFTWARD — staying entirely within the sidebar's own bounds — rather than
            // straddling into RightColumn, where RightColumn's own content (built later, by
            // NotesRootBuilder, after this Initialize() call returns) would win any raycast in
            // the overlapping region.
            var divider = DraggableDivider.Create(rootGO.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), DividerWidth);
            dividerGO = divider.gameObject;
            divider.OnDragDeltaX += dx => SetExpandedWidth(expandedWidth + dx);
            divider.OnDragEnd += SaveExpandedWidth;
            var doubleClick = dividerGO.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () =>
            {
                SetExpandedWidth(DefaultExpandedWidth);
                SaveExpandedWidth();
            };
        }

        /// <summary>Recolors just the previously/newly active page rows in place — deliberately
        /// does NOT call Rebuild() (see pageRowImages' field comment for why).</summary>
        void OnActivePageChanged(NotesPage page)
        {
            foreach (var kvp in pageRowImages)
            {
                bool isActive = page != null && kvp.Key == page.Id;
                if (isActive)
                    ThemeService.Tag(kvp.Value, ThemeRole.AccentSoft, 0.9f);
                else
                    kvp.Value.color = new Color(1f, 1f, 1f, 0.02f);
            }
        }

        void ToggleExpanded()
        {
            expanded = !expanded;
            listGO.SetActive(expanded);
            headerTextGO.SetActive(expanded);
            addGroupButtonGO.SetActive(expanded);
            searchInputGO.SetActive(expanded);
            dividerGO.SetActive(expanded);
            rootLayoutElement.preferredWidth = expanded ? expandedWidth : CollapsedWidth;
        }

        void SetExpandedWidth(float value)
        {
            expandedWidth = Mathf.Clamp(value, MinExpandedWidth, MaxExpandedWidth);
            if (expanded) rootLayoutElement.preferredWidth = expandedWidth;
        }

        void SaveExpandedWidth() => PlayerPrefs.SetFloat(ExpandedWidthPrefsKey, expandedWidth);

        void RequestRebuild()
        {
            rebuildPending = true;
        }

        void LateUpdate()
        {
            if (!rebuildPending) return;
            rebuildPending = false;
            Rebuild();
        }

        void Update()
        {
            if (activeRenameInput != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CancelActiveRename();
        }

        bool MatchesSearch(string text) =>
            string.IsNullOrEmpty(searchQuery) || text.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant());

        void Rebuild()
        {
            // Any in-progress rename's InputField/label are about to be destroyed below along
            // with the rest of the old rows — clear the tracking fields so Escape (in Update())
            // can't touch already-destroyed GameObjects afterward.
            activeRenameInput = null;
            activeRenameLabelGO = null;
            renameCancelled = false;
            pageRowImages.Clear();

            // SetActive(false) takes effect immediately; Destroy() is deferred to end of
            // frame, so without deactivating first, the old and newly-built rows below would
            // both render for one frame, showing as overlapping/doubled UI.
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            foreach (var group in documentController.Document.Groups)
                BuildGroupRow(group);
        }

        void BuildGroupRow(PageGroup group)
        {
            bool titleMatches = MatchesSearch(group.Title);
            bool hasMatchingPage = false;
            if (!titleMatches)
            {
                foreach (var p in group.Pages)
                {
                    if (MatchesSearch(p.Name)) { hasMatchingPage = true; break; }
                }
            }
            if (!titleMatches && !hasMatchingPage) return;

            var groupGO = new GameObject($"Group_{group.Id}");
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(groupGO.transform, false);
            titleGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            var titleTextGO = new GameObject("Text");
            titleTextGO.transform.SetParent(titleGO.transform, false);
            var titleText = titleTextGO.AddComponent<Text>();
            string suffix = group.LinkedPoiId != null ? " 📍" : "";
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            ThemeService.Tag(titleText, ThemeRole.Mut);
            titleText.alignment = TextAnchor.MiddleLeft;
            var titleTextRect = titleTextGO.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.offsetMin = Vector2.zero;
            titleTextRect.offsetMax = Vector2.zero;

            AddRenameAndDelete(titleTextGO, titleGO.transform, titleText, titleTextRect, group.Title,
                newTitle => documentController.RenameGroup(group.Id, newTitle),
                () => ConfirmDialog.Show(builtinFont, $"Удалить группу \"{group.Title}\" и все её страницы ({group.Pages.Count})?", confirmed =>
                {
                    if (confirmed) documentController.DeleteGroup(group.Id);
                }));

            foreach (var page in group.Pages)
            {
                if (titleMatches || MatchesSearch(page.Name))
                    BuildPageRow(groupGO.transform, group, page);
            }

            AddSmallActionButton(groupGO.transform, "  + Страница", () =>
            {
                documentController.CreatePage(group.Id, $"Страница {group.Pages.Count + 1}");
            });
        }

        void BuildPageRow(Transform parent, PageGroup group, NotesPage page)
        {
            var rowGO = new GameObject($"Page_{page.Id}");
            rowGO.transform.SetParent(parent, false);
            var img = rowGO.AddComponent<Image>();
            bool isActive = documentController.ActivePage != null && documentController.ActivePage.Id == page.Id;
            if (isActive)
                ThemeService.Tag(img, ThemeRole.AccentSoft, 0.9f);
            else
                img.color = new Color(1f, 1f, 1f, 0.02f);
            pageRowImages[page.Id] = img;
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = img;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            // Deliberately does NOT call Rebuild() here — OnActivePageChanged (subscribed in
            // Initialize) recolors the affected rows in place instead, so this row's
            // GameObject survives a click (see pageRowImages' field comment for why that
            // matters for double-click-to-rename).
            btn.onClick.AddListener(() => documentController.OpenPage(page.Id));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(rowGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = $"• {page.Name}";
            text.font = builtinFont;
            text.fontSize = 13;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = Vector2.zero;

            // clickCatcherGO is rowGO (not textGO) here specifically so Button and
            // DoubleClickHandler end up on the SAME GameObject — see this task's header
            // comment on why attaching it to the child Text instead would break "open page".
            AddRenameAndDelete(rowGO, rowGO.transform, text, textRect, page.Name,
                newName => documentController.RenamePage(page.Id, newName),
                () => ConfirmDialog.Show(builtinFont, $"Удалить страницу \"{page.Name}\"?", confirmed =>
                {
                    if (confirmed) documentController.DeletePage(page.Id);
                }));
        }

        /// <summary>Shrinks `label`'s rect to leave room for a new "×" delete button anchored to
        /// the row's right edge, and wires up a double-click-to-rename InputField (same rect as
        /// the shrunk label) that commits via onRename on Enter/blur or cancels on Escape.</summary>
        void AddRenameAndDelete(GameObject clickCatcherGO, Transform rowTransform, Text label, RectTransform labelRect, string rawValue, System.Action<string> onRename, System.Action onDeleteRequested)
        {
            labelRect.offsetMax = new Vector2(labelRect.offsetMax.x - 20f, labelRect.offsetMax.y);

            var inputGO = new GameObject("RenameInput");
            inputGO.transform.SetParent(labelRect.parent, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = labelRect.anchorMin;
            inputRect.anchorMax = labelRect.anchorMax;
            inputRect.offsetMin = labelRect.offsetMin;
            inputRect.offsetMax = labelRect.offsetMax;
            var inputImg = inputGO.AddComponent<Image>();
            ThemeService.Tag(inputImg, ThemeRole.Elev);
            var input = inputGO.AddComponent<InputField>();
            input.targetGraphic = inputImg;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(inputGO.transform, false);
            var inputText = inputTextGO.AddComponent<Text>();
            inputText.font = builtinFont;
            inputText.fontSize = label.fontSize;
            inputText.color = label.color;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            var inputTextRect = inputTextGO.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(4f, 0f);
            inputTextRect.offsetMax = new Vector2(-4f, 0f);
            input.textComponent = inputText;
            inputGO.SetActive(false);

            var doubleClick = clickCatcherGO.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () => StartRename(label.gameObject, input, rawValue);

            input.onEndEdit.AddListener(newText =>
            {
                bool wasCancelled = renameCancelled;
                activeRenameInput = null;
                activeRenameLabelGO = null;
                renameCancelled = false;
                if (wasCancelled) return;
                inputGO.SetActive(false);
                label.gameObject.SetActive(true);
                if (!string.IsNullOrWhiteSpace(newText))
                    onRename(newText.Trim());
            });

            var deleteGO = new GameObject("Delete");
            deleteGO.transform.SetParent(rowTransform, false);
            var deleteImg = deleteGO.AddComponent<Image>();
            ThemeService.Tag(deleteImg, ThemeRole.Elev);
            var deleteBtn = deleteGO.AddComponent<Button>();
            deleteBtn.targetGraphic = deleteImg;
            deleteBtn.onClick.AddListener(() => onDeleteRequested());
            var deleteRect = deleteGO.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(1f, 0f);
            deleteRect.anchorMax = new Vector2(1f, 1f);
            deleteRect.pivot = new Vector2(1f, 0.5f);
            deleteRect.sizeDelta = new Vector2(20f, 0f);
            deleteRect.anchoredPosition = Vector2.zero;

            var deleteTextGO = new GameObject("Text");
            deleteTextGO.transform.SetParent(deleteGO.transform, false);
            var deleteText = deleteTextGO.AddComponent<Text>();
            deleteText.text = "×";
            deleteText.font = builtinFont;
            deleteText.fontSize = 14;
            ThemeService.Tag(deleteText, ThemeRole.Danger);
            deleteText.alignment = TextAnchor.MiddleCenter;
            deleteText.raycastTarget = false;
            var deleteTextRect = deleteTextGO.GetComponent<RectTransform>();
            deleteTextRect.anchorMin = Vector2.zero;
            deleteTextRect.anchorMax = Vector2.one;
            deleteTextRect.sizeDelta = Vector2.zero;
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

        GameObject AddSmallActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 18f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            ThemeService.Tag(text, ThemeRole.AccentInk);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = Vector2.zero;

            return go;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Tree Sidebar — Collapse Toggle")]
        public void SelfTestCollapseToggle()
        {
            if (rootLayoutElement == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Collapse Toggle: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            if (!expanded) { ok = false; reason = "expected to start expanded"; }
            if (ok && !Mathf.Approximately(rootLayoutElement.preferredWidth, expandedWidth))
            { ok = false; reason = $"expected preferredWidth={expandedWidth} while expanded, got {rootLayoutElement.preferredWidth}"; }

            if (ok)
            {
                ToggleExpanded();
                if (expanded) { ok = false; reason = "expected collapsed after first toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, CollapsedWidth))
                { ok = false; reason = $"expected preferredWidth={CollapsedWidth} while collapsed, got {rootLayoutElement.preferredWidth}"; }
                else if (listGO.activeSelf || headerTextGO.activeSelf || addGroupButtonGO.activeSelf || searchInputGO.activeSelf || dividerGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput/divider all inactive while collapsed"; }
            }

            if (ok)
            {
                ToggleExpanded();
                if (!expanded) { ok = false; reason = "expected expanded after second toggle"; }
                else if (!Mathf.Approximately(rootLayoutElement.preferredWidth, expandedWidth))
                { ok = false; reason = $"expected preferredWidth={expandedWidth} after re-expanding, got {rootLayoutElement.preferredWidth}"; }
                else if (!listGO.activeSelf || !headerTextGO.activeSelf || !addGroupButtonGO.activeSelf || !searchInputGO.activeSelf || !dividerGO.activeSelf)
                { ok = false; reason = "expected list/headerText/addGroupButton/searchInput/divider all active after re-expanding"; }
            }

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Collapse Toggle: PASS"
                : $"Self-Test Notes Tree Sidebar — Collapse Toggle: FAIL ({reason})");
        }

        [ContextMenu("Self-Test: Notes Tree Sidebar — Search Filter")]
        public void SelfTestSearchFilter()
        {
            if (documentController == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Search Filter: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            searchQuery = "";
            Rebuild();
            int totalRows = listContent.childCount;
            if (totalRows == 0) { ok = false; reason = "expected at least one group row with no search query"; }

            if (ok)
            {
                searchQuery = "zzz_no_such_page_or_group_zzz";
                Rebuild();
                if (listContent.childCount != 0)
                { ok = false; reason = "expected zero rows for a query matching nothing"; }
            }

            searchQuery = "";
            Rebuild();

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Search Filter: PASS"
                : $"Self-Test Notes Tree Sidebar — Search Filter: FAIL ({reason})");
        }

        [ContextMenu("Self-Test: Notes Tree Sidebar — Width Clamp")]
        public void SelfTestWidthClamp()
        {
            if (rootLayoutElement == null)
            {
                Debug.Log("Self-Test Notes Tree Sidebar — Width Clamp: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            float original = expandedWidth;
            bool ok = true;
            string reason = "";

            SetExpandedWidth(10f);
            if (!Mathf.Approximately(expandedWidth, MinExpandedWidth))
            { ok = false; reason = $"expected clamp to {MinExpandedWidth}, got {expandedWidth}"; }

            if (ok)
            {
                SetExpandedWidth(1000f);
                if (!Mathf.Approximately(expandedWidth, MaxExpandedWidth))
                { ok = false; reason = $"expected clamp to {MaxExpandedWidth}, got {expandedWidth}"; }
            }

            SetExpandedWidth(original);

            Debug.Log(ok
                ? "Self-Test Notes Tree Sidebar — Width Clamp: PASS"
                : $"Self-Test Notes Tree Sidebar — Width Clamp: FAIL ({reason})");
        }
    }
}
