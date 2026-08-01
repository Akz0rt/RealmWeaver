using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Ctrl+K quick-open: a centred palette over a dimmed, click-to-dismiss backdrop, rendering
    /// QuickOpen.Search hits over the live document — never reimplementing its matching, ranking, or
    /// folding (see QuickOpen's own class doc; this class only renders QuickHit fields).
    ///
    /// One persistent instance lives on WorkspaceBuilder's own GameObject (WorkspaceBuilder.Awake calls
    /// Attach), polling Keyboard.current every frame for the global chord even while closed. That is why
    /// this is a live MonoBehaviour rather than a static Show/Close pair like NavContextMenu/ConfirmDialog —
    /// those only need Update() while their popup exists; this one also needs it while the popup does NOT,
    /// to notice the chord in the first place. The popup UI itself, once open, is still a disposable root
    /// Canvas built/torn down on demand — same shape as NavContextMenu, just carried by this component's own
    /// Update() (Up/Down/Enter/Shift+Enter/Esc) instead of nothing.
    ///
    /// TabStripView's «+» reaches this through OpenForPane, which focuses the requesting pane FIRST: Enter
    /// and Shift+Enter both route through WorkspaceController.Open(inOtherPane), which reads
    /// Layout.FocusedPane under the hood (see WorkspaceOps.Open) — so focusing the pane here is the whole of
    /// what "land in THAT pane" needs. There is deliberately no separate target-pane field on this class that
    /// could drift from what Layout itself calls focused.
    /// </summary>
    public class QuickOpenPopup : MonoBehaviour
    {
        const float PaletteWidth = 560f;
        const float InputHeight = 40f;
        const float RowHeight = 34f;
        const float FooterHeight = 22f;
        const float KindColumnWidth = 170f;
        const int ResultLimit = 20;

        // Above NavContextMenu (1000), below ConfirmDialog (32000). Nothing in this task opens the palette
        // alongside either, but staying inside that band keeps future stacking sane rather than accidental.
        const int CanvasSortingOrder = 4000;

        WorkspaceController controller;
        NotesDocumentController documentController;

        /// <summary>Discovered once, in Attach, the same override-or-discover pattern MapSurfaceHost.Rewire
        /// uses for mapCamera — except this class has no Rewire counterpart to re-run it after a domain
        /// reload: `controller` itself is already documented (Update's own guard, above) as NOT revived post-
        /// reload, and this field is a plain non-serialized reference beside it, so it is wiped right along
        /// with `controller` and left null — the same accepted gap, not a new one. Passed straight to
        /// WorldObjectSource.Collect, which treats null as "no POIs exist" (its own doc); this class holds no
        /// null branch of its own for it any more, the check went with the mapping in Task 10e.</summary>
        PoiManager poiManager;

        Font builtinFont;

        GameObject popupGO;
        InputField inputField;
        Transform listContent;
        LayoutElement listElement;

        readonly List<QuickHit> hits = new List<QuickHit>();
        readonly List<Image> rowBackgrounds = new List<Image>();
        int highlighted = -1;

        // focusPending/searchPending: both consumed in LateUpdate, never inline where they are set — see
        // LateUpdate's own comment for why (one-frame focus deferral; frame-coalesced search, matching
        // TabStripView/NavigatorView's own RequestRebuild/LateUpdate idiom).
        bool focusPending;
        bool searchPending;

        public static QuickOpenPopup Attach(GameObject host, WorkspaceController controller, NotesDocumentController documentController)
        {
            var popup = host.AddComponent<QuickOpenPopup>();
            popup.controller = controller;
            popup.documentController = documentController;
            // FindFirstObjectByType, not an Inspector slot — WorkspaceBuilder is not wired into the scene
            // until Task 11 (see EnsureDocumentController's own doc for the identical reasoning applied to
            // documentController), and PoiManager already lives wherever the map's own scene objects do, not
            // under WorkspaceBuilder's GameObject. Absent entirely before generation runs — WorldObjectSource
            // .Collect returns null for a null manager and QuickOpen's W5 treats that as "no POIs", the same
            // tolerance this class already extends to a null documentController.
            // FindObjectsInactive.Include, matching NavigatorView.ResolvePoiManager and
            // MapScreenController.Pois(): this discovers ONCE (unlike those two, which retry on every miss),
            // so a PoiManager whose GameObject happens to be inactive at shell-build time would otherwise
            // leave Ctrl+K blind to every POI for the whole session while «Мир» still listed them.
            popup.poiManager = FindFirstObjectByType<PoiManager>(FindObjectsInactive.Include);
            popup.builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return popup;
        }

        // The popup canvas is a ROOT GameObject (parented to nothing — see BuildPopup), same as
        // NavContextMenu's; unlike that static class's own singleton-Close(), nothing else ever destroys
        // popupGO, so if the host (WorkspaceBuilder's GameObject) is destroyed while the palette is open, the
        // canvas would otherwise orphan on screen instead of dying with it.
        void OnDestroy() => Close();

        /// <summary>TabStripView.OnRequestQuickOpen's target — see the class doc for why FocusPane first is
        /// the whole of the per-pane wiring this needs.</summary>
        public void OpenForPane(int pane)
        {
            if (controller == null) return;
            controller.FocusPane(pane);
            Open();
        }

        void Update()
        {
            // controller can be null after a Play-mode script reload re-invokes Awake on the still-live host
            // — WorkspaceBuilder.Awake's own comment documents this exact gap for PrimaryTabStrip/
            // SecondaryTabStrip/Navigator; this component has the same one. Without this guard every Ctrl+K
            // press after a reload would NRE instead of silently doing nothing.
            if (controller == null || Keyboard.current == null) return;

            if (popupGO == null)
            {
                if (Keyboard.current.ctrlKey.isPressed && Keyboard.current.kKey.wasPressedThisFrame) Open();
                return;   // closed-state branch never falls through to the open-state handling below.
            }

            // Palette IS open. Read raw hardware state via Keyboard.current rather than anything routed
            // through the EventSystem/InputSystemUIInputModule — the same reason the brief gives for the
            // chord itself: the focused InputField's own OnMove/OnSubmit/OnCancel (bound to arrows/Enter/Esc
            // by the default UI actions) would otherwise fight this exact handling. Polling the hardware key
            // directly sidesteps that fight entirely; whatever the InputField's own handlers additionally do
            // to it this same frame is harmless, since Close()/Choose() below destroy or leave it alone
            // regardless of its internal edit-mode state.
            if (Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Keyboard.current.downArrowKey.wasPressedThisFrame) MoveHighlight(1);
            else if (Keyboard.current.upArrowKey.wasPressedThisFrame) MoveHighlight(-1);
            else if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
                Choose(Keyboard.current.shiftKey.isPressed);
        }

        /// <summary>Both deferred flags are consumed here, one frame (well, one PHASE — see below) after
        /// they were set, never inline at the set site:
        ///   • focusPending — Open() runs inside Update(), which race-shares the frame with EventSystem's own
        ///     Update() (driving InputSystemUIInputModule.Process()) in an order this component does not
        ///     control. Calling ActivateInputField()/Select() from LateUpdate instead means the field is not
        ///     yet the selected UI object when this frame's input module ran, so even if this frame's 'K'
        ///     keypress produced a text event, there is nothing selected for it to land on. That is the whole
        ///     mechanism behind "the chord cannot ... leak into the field" — deferring the FOCUS, not
        ///     filtering the text.
        ///   • searchPending — set by Open() (initial empty-query search) and by the input field's own
        ///     onValueChanged (every keystroke, per the brief's explicit no-debounce instruction). Coalescing
        ///     into LateUpdate is NOT a debounce: it still runs once per keystroke, zero added latency beyond
        ///     the same frame — it only guards against the same destroy-then-rebuild-twice-in-one-frame trap
        ///     TabStripView and NavigatorView both document for their own RequestRebuild.</summary>
        void LateUpdate()
        {
            if (popupGO == null) return;

            if (focusPending)
            {
                focusPending = false;
                inputField.ActivateInputField();
                inputField.Select();
            }

            if (searchPending)
            {
                searchPending = false;
                RunSearch(inputField.text);
            }
        }

        void Open()
        {
            if (popupGO != null) return;   // defensive; both call sites already guard, see their own comments.

            hits.Clear();
            rowBackgrounds.Clear();
            highlighted = -1;

            BuildPopup();

            focusPending = true;
            searchPending = true;   // empty-query first search; QuickOpen.Search("") already returns [] (Q4).
        }

        void Close()
        {
            if (popupGO == null) return;
            popupGO.SetActive(false);   // same immediate-hide-before-deferred-Destroy precaution every
            Destroy(popupGO);            // Rebuild() in this codebase takes, even though nothing rebuilds
                                          // popupGO's siblings this same frame today — cheap insurance against
                                          // a future edit that calls Close() from somewhere Open() also runs.
            popupGO = null;
            inputField = null;
            listContent = null;
            listElement = null;
            rowBackgrounds.Clear();
            hits.Clear();
            highlighted = -1;
            focusPending = false;
            searchPending = false;
        }

        // ── Search / selection ────────────────────────────────────────────────────

        void RunSearch(string query)
        {
            // Read documentController.Document FRESH every search, never cached across calls —
            // NotesDocumentController.LoadDocument swaps the whole instance, and this class's own
            // OpenForPane/Attach hold no OnDocumentChanged subscription to invalidate a cached copy.
            // documentController CAN still be null here: WorkspaceBuilder.EnsureDocumentController only
            // resolves it when a NotesRootBuilder already exists somewhere in the scene (its own doc), so a
            // scene without one leaves this field null for the component's whole life — no Task revives it
            // later, there is no second assignment site. QuickOpen.Search tolerates a null doc by design
            // (Q-guard), so this needs no null branch of its own to stay crash-free.
            //
            // AND CTRL+K IS NOT BLANK IN THAT SCENE, as of Task 10e's review round: BOTH world candidates —
            // the world map and every POI — are collected above Search's own doc==null guard, because
            // neither is reachable through a page any anymore and CollectWorldHits no longer even takes a
            // document to be gated on (see QuickOpen's class doc). Only PAGE hits need one. A doc-less scene
            // therefore searches the whole world and none of the notes, which is exactly what exists in it.
            var doc = documentController != null ? documentController.Document : null;

            hits.Clear();
            // The world list is built fresh on every search, same as `doc` immediately above, and by the same
            // mapper NavigatorView's «Мир» uses — WorldObjectSource.Collect, which this class's own private
            // copy became in Task 10e (see that class's doc for why the two must not diverge). Subscribing to
            // PoiManager.OnPoisChanged the way the navigator does would buy nothing here: a search runs on the
            // keystroke, so it always reads the POIs as they are at that instant.
            hits.AddRange(QuickOpen.Search(doc, WorldObjectSource.Collect(poiManager), query, ResultLimit));
            highlighted = hits.Count > 0 ? 0 : -1;
            RebuildRows();
        }

        void MoveHighlight(int delta)
        {
            if (hits.Count == 0) { highlighted = -1; return; }
            highlighted = ((highlighted + delta) % hits.Count + hits.Count) % hits.Count;
            RefreshHighlight();
        }

        void Choose(bool inOtherPane) => ChooseIndex(highlighted, inOtherPane);

        void ChooseIndex(int index, bool inOtherPane)
        {
            // The "nothing highlighted" case: Up/Down/Enter pressed before any search ever produced a hit
            // (an empty query returns [] by Q4, and a query matching nothing returns [] too), so highlighted
            // is still -1 and Enter must do nothing rather than index a list that was never populated. This
            // used to be justified by the null-document case as well; since Task 10e's review round a null
            // document still yields world hits, so that is no longer an empty-list state and this guard rests
            // on the empty-query/no-match one alone.
            if (index < 0 || index >= hits.Count) return;

            QuickHit hit = hits[index];
            SurfaceRef target = hit.Target;

            // World != null (Task 10b, W2) means this row is a WORLD OBJECT, not a page: QuickHit.Target
            // carries no usable id for it (Page/"" — no page is involved at all), and the identity to open
            // travels on World instead. Opening it opens THE PLACE — its editor — per the Task 10c checkpoint
            // ruling that a point of interest IS its editor menu; the same SurfaceRef the navigator's «Мир»
            // row, the map popup's «Редактировать» and a double-click on the marker
            // (MapScreenController.OpenPoiFromMap) all produce, through the one factory the four of them
            // share (WorldSurface.PoiEditor), so they focus one tab rather than opening near-identical ones.
            //
            // Task 10b created a PAGE here instead (NotesDocOps.EnsurePageFor) and opened that. Removed, not
            // merely bypassed: auto-creating a note for a place the user only looked at produced clutter with
            // no function now that «Мир» no longer reads NotesPage.Bound, and it contradicted the second half
            // of the same ruling — a note about a place is an ordinary note the user authors deliberately.
            // EnsurePageFor itself stays (Р3's binding work is its real caller); this call site does not.
            //
            // The PAGE-hit path above is untouched: a page found by its own name is a note the user searched
            // for by name, and opening anything else would not be the row they read.
            if (hit.World != null) target = WorldSurface.PoiEditor(hit.World.Id);

            Close();   // before Open() — same ordering NavContextMenu.AddItem uses for its own onClick, so
                       // the palette is never still on screen (or the active canvas) underneath whatever
                       // opening the target surface does next.
            controller.Open(target, hit.Title, inOtherPane);
        }

        // ── Construction (built fresh on every Open, torn down whole on every Close) ─

        void BuildPopup()
        {
            var canvasGO = new GameObject("QuickOpenCanvas", typeof(RectTransform));
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            popupGO = canvasGO;

            var backdropGO = new GameObject("Backdrop", typeof(RectTransform));
            backdropGO.transform.SetParent(canvasGO.transform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            ThemeService.Tag(backdropImg, ThemeRole.Bg, 0.6f);   // dimmed, per the brief's Step 1 — unlike
                                                                  // NavContextMenu's fully invisible one.
            var backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.onClick.AddListener(Close);   // dismiss-on-click: ordinary desktop behaviour for a
                                                        // palette, the same choice NavContextMenu makes and
                                                        // for the same reason ConfirmDialog gives for NOT
                                                        // making it — this isn't a destructive decision.
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            var panelGO = new GameObject("Palette", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);   // AFTER backdrop → wins raycasts, the
                                                                        // same ordering NavContextMenu documents.
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel2);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PaletteWidth, 0f);   // width fixed; height content-driven below.
            panelRect.anchoredPosition = Vector2.zero;

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 10, 8);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;   // each section (input/list/footer) keeps its own preferredHeight.
            // Panel's OWN height is driven by its children's total preferred height — the one place in this
            // file a ContentSizeFitter belongs (mirrors ConfirmDialog.BuildBase); the List section below gets
            // its height from ITS OWN LayoutElement.preferredHeight instead (see RebuildRows), never from a
            // ContentSizeFitter of its own, since the panel's VerticalLayoutGroup is what sizes that child.
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildInputRow(panelGO.transform);
            BuildListContainer(panelGO.transform);
            BuildFooter(panelGO.transform);
        }

        void BuildInputRow(Transform parent)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = InputHeight;

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 15;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            input.textComponent = text;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(go.transform, false);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "Поиск по страницам и тексту...";
            placeholder.font = builtinFont;
            placeholder.fontSize = 15;
            placeholder.fontStyle = FontStyle.Italic;
            ThemeService.Tag(placeholder, ThemeRole.Mut);
            placeholder.alignment = TextAnchor.MiddleLeft;
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(12f, 0f);
            placeholderRect.offsetMax = new Vector2(-12f, 0f);
            input.placeholder = placeholder;

            // Re-run QuickOpen.Search on every keystroke, no debounce (brief Step 3) — flagged pending and
            // consumed in LateUpdate purely for same-frame coalescing, not to delay it; see LateUpdate's doc.
            input.onValueChanged.AddListener(_ => searchPending = true);

            inputField = input;
        }

        void BuildListContainer(Transform parent)
        {
            var go = new GameObject("List", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            // TRUE, not false: this is the group that must APPLY each row's own preferredHeight (RowHeight)
            // to its actual rect — with childControlHeight=false, Unity's layout pass calls the
            // position-only SetChildAlongAxisWithScale overload, which stacks children using their
            // preferredHeight but never writes it into rect.sizeDelta, leaving every row at whatever height
            // its freshly-created RectTransform already had. Mirrors the panel's own vlg 70 lines up
            // (BuildPopup: childControlHeight = true; childForceExpandHeight = false) — "control height, but
            // don't force-expand it past the child's own preferredHeight" is the correct shape for a
            // fixed-row-height list, the same shape NavigatorView.cs and GenerationProgressUI.cs both use
            // by leaving this field at Unity's own default (true).
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;

            listElement = go.AddComponent<LayoutElement>();
            listElement.preferredHeight = 0f;   // updated every RebuildRows to hits.Count * RowHeight; 0 here
                                                  // is exactly right for the empty state — no query typed yet,
                                                  // or a query that matched nothing. (A missing DOCUMENT is no
                                                  // longer such a state: world hits survive it — see
                                                  // RunSearch.) No ScrollRect, no placeholder; the section
                                                  // simply collapses to nothing between Input and Footer.

            listContent = go.transform;
        }

        void BuildFooter(Transform parent)
        {
            var go = new GameObject("Footer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = FooterHeight;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            // The load-bearing substring is «Enter — открыть · Shift+Enter — открыть рядом», verbatim from
            // the brief — «рядом», never «справа»: WorkspaceOps.Open(inOtherPane) means "the pane that is
            // NOT focused" (NavContextMenu's own comment makes the identical point for «Открыть рядом»), and
            // with focus already on the right-hand pane, «справа» would open on the LEFT and lie. ↑↓/Esc are
            // added context around that required phrase, not a replacement for it.
            text.text = "↑↓ — выбор · Enter — открыть · Shift+Enter — открыть рядом · Esc — закрыть";
            text.font = builtinFont;
            text.fontSize = 11;
            ThemeService.Tag(text, ThemeRole.Mut);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
        }

        // ── Rows ─────────────────────────────────────────────────────────────────

        void RebuildRows()
        {
            // SetActive(false) before Destroy() — Destroy() is deferred to end of frame, so without
            // deactivating first, stale and freshly-built rows would both render for one frame; same trap
            // TabStripView.Rebuild and NavigatorView.Rebuild both document for themselves.
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            rowBackgrounds.Clear();

            for (int i = 0; i < hits.Count; i++)
                rowBackgrounds.Add(BuildRow(i, hits[i], i == highlighted));

            listElement.preferredHeight = hits.Count * RowHeight;
        }

        Image BuildRow(int index, QuickHit hit, bool isHighlighted)
        {
            var rowGO = new GameObject($"Row_{index}", typeof(RectTransform));
            rowGO.transform.SetParent(listContent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            // Structural containment for long titles/snippets — Tasks 6/7 both learned arithmetic truncation
            // alone is not a guarantee, because the row's ancestor layout can still land it narrower than
            // whatever width truncation was budgeted against. Clips every child to the row's REAL post-layout
            // rect; any ellipsis is cosmetic (here, QuickOpen's own Snippet already carries one where its
            // context window was cut — nothing further is added at render time).
            rowGO.AddComponent<RectMask2D>();

            var bg = rowGO.AddComponent<Image>();
            TagRowBackground(bg, isHighlighted);

            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            int capturedIndex = index;
            // Mouse click opens the SAME way Enter does — in the focused pane, never the other; Shift-click
            // isn't wired (the brief specifies Shift+ENTER, not a mouse chord, for "other pane").
            btn.onClick.AddListener(() => ChooseIndex(capturedIndex, inOtherPane: false));

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = hit.Title ?? "";
            title.font = builtinFont;
            title.fontSize = 13;
            ThemeService.Tag(title, ThemeRole.Txt);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.raycastTarget = false;   // clicks must reach the row's own Button, not the label.
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(12f, 0f);
            titleRect.offsetMax = new Vector2(-(KindColumnWidth + 8f), 0f);

            // Right column: Snippet when this hit HAS one (a body-text hit), else Kind. This is a deliberate
            // reading of the brief's literal "title left, kind right" beyond QuickHit's own field shapes —
            // see QuickOpen.cs: TryAddBodyHit sets BOTH Title and Kind to the source page's name, so a
            // literal Kind-only render would print the same string twice for every body hit and make two
            // hits inside the same page visually indistinguishable, with no reason shown for why either
            // matched. Snippet ?? Kind loses nothing on either branch — for a name hit Snippet is null and
            // Kind is "" anyway (blank, exactly the literal spec); for a body hit the discarded Kind value
            // was pure duplication of Title.
            var kindGO = new GameObject("Kind", typeof(RectTransform));
            kindGO.transform.SetParent(rowGO.transform, false);
            var kind = kindGO.AddComponent<Text>();
            kind.text = hit.Snippet ?? hit.Kind ?? "";
            kind.font = builtinFont;
            kind.fontSize = 11;
            ThemeService.Tag(kind, ThemeRole.Mut);
            kind.alignment = TextAnchor.MiddleRight;
            kind.horizontalOverflow = HorizontalWrapMode.Overflow;
            kind.verticalOverflow = VerticalWrapMode.Truncate;
            kind.raycastTarget = false;
            var kindRect = kindGO.GetComponent<RectTransform>();
            kindRect.anchorMin = new Vector2(1f, 0f);
            kindRect.anchorMax = Vector2.one;
            kindRect.pivot = new Vector2(1f, 0.5f);
            kindRect.sizeDelta = new Vector2(KindColumnWidth, 0f);
            kindRect.anchoredPosition = new Vector2(-10f, 0f);

            return bg;
        }

        void RefreshHighlight()
        {
            for (int i = 0; i < rowBackgrounds.Count; i++)
            {
                if (rowBackgrounds[i] == null) continue;   // defensive; rows and this list are rebuilt together.
                TagRowBackground(rowBackgrounds[i], i == highlighted);
            }
        }

        static void TagRowBackground(Image bg, bool isHighlighted)
        {
            if (isHighlighted) ThemeService.Tag(bg, ThemeRole.AccentSoft, 0.9f);
            else ThemeService.Tag(bg, ThemeRole.Panel, 0f);   // alpha 0 — invisible but still raycasts (the
                                                                // same trick NavigatorView's row bg uses),
                                                                // needed so an unhighlighted row stays clickable.
        }
    }
}
