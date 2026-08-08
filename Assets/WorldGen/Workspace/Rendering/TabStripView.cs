using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// One tab strip per pane. Rebuilt entirely from WorkspaceLayout on every OnLayoutChanged — this view
    /// holds no "which tab is active" state of its own (a second copy would drift from WorkspaceLayout, the
    /// model that actually owns it; see the plan brief). It re-reads WorkspaceOps.PaneAt(controller.Layout,
    /// PaneIndex) fresh on every rebuild rather than caching a PaneState reference, so a pane PROMOTION
    /// (WorkspaceOps.NormalizeSplit moving Secondary into Primary's slot when Primary empties) is handled
    /// for free: whichever physical strip is asked for pane 0 always shows whatever WorkspaceLayout.Primary
    /// currently is, promoted or not.
    ///
    /// Built via the static Create factory, mirroring DraggableDivider.Create — the returned instance IS the
    /// strip's own root GameObject (Image background + HorizontalLayoutGroup + this component), not a
    /// wrapper around a separately-built slot, so there is exactly one place StripHeight is decided.
    ///
    /// Tab-drag-between-strips was cut from Task 6 (its Step 4) and RESTORED as Task 10d at the user's
    /// explicit request, after «Открыть рядом» (Task 7) and Ctrl+K's Shift+Enter (Task 8) — the fallbacks the
    /// cut was justified by — turned out not to cover the gesture they were meant to replace. It lives in
    /// TabDragHandler at the bottom of this file, the same sibling-class arrangement TabHoverReveal uses, and
    /// commits every drop through WorkspaceController.MoveTab -> WorkspaceOps.MoveTab. There is still no
    /// second way to mutate WorkspaceLayout from this layer.
    /// </summary>
    public class TabStripView : MonoBehaviour
    {
        public const float StripHeight = 32f;

        const float MinTabWidth = 72f;
        const float MaxTabWidth = 200f;
        const float TitlePaddingLeft = 10f;
        const float CloseReserve = 26f;       // title's right inset, reserving room for the close button
        const float CloseButtonSize = 16f;
        const float CloseButtonMargin = 6f;
        const float PlusButtonWidth = 26f;
        /// <summary>Thickness of the accent line marking the FOCUSED pane — see BuildActivePaneUnderline.</summary>
        const float ActiveUnderlineHeight = 2f;

        /// <summary>The «+» hook: clicking «+» invokes this with the strip's own pane index. Task 8 built
        /// the palette it launches — WorkspaceBuilder assigns QuickOpenPopup.OpenForPane to both strips, and
        /// OpenForPane focuses the requesting pane before opening. Null (a bare rig) = «+» does nothing.</summary>
        public System.Action<int> OnRequestQuickOpen;

        public int PaneIndex { get; private set; }

        /// <summary>The controller TabDragHandler commits its drop through. Exposed to this file's sibling
        /// classes only (internal, not public): the strip stays the one place that knows which pane it is,
        /// and the drag handler asks it rather than carrying a second copy of the wiring.</summary>
        internal WorkspaceController Controller => controller;

        WorkspaceController controller;
        Font builtinFont;
        bool rebuildPending;

        public static TabStripView Create(Transform parent, WorkspaceController controller, int paneIndex)
        {
            var go = new GameObject($"TabStrip_{paneIndex}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;   // variable-width tabs — force-expand would override each
            layout.childControlHeight = true;        // tab's own preferredWidth (see the plan brief's layout trap).
            layout.childForceExpandHeight = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(6, 6, 0, 0);

            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = StripHeight;
            element.minHeight = 0f;
            element.flexibleHeight = 0f;   // fixed strip height; the pane's ContentArea sibling (the other
                                            // child of the pane's VerticalLayoutGroup) takes the rest.

            var view = go.AddComponent<TabStripView>();
            view.controller = controller;
            view.PaneIndex = paneIndex;
            view.builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            controller.OnLayoutChanged += view.RequestRebuild;
            view.Rebuild();   // first frame already correct, same reasoning as WorkspaceController.Initialize.

            return view;
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnLayoutChanged -= RequestRebuild;
        }

        void RequestRebuild() => rebuildPending = true;

        /// <summary>Coalesces every OnLayoutChanged fired within one frame into a single Rebuild in
        /// LateUpdate. A tab click raises TWO events in the same frame — SetActive then FocusPane (see
        /// BuildTab) — and since Destroy() is deferred to end-of-frame, a second synchronous Rebuild would
        /// iterate children already marked for destruction and build a THIRD, overlapping set on top. Same
        /// fix NavigatorView uses for its own RequestRebuild/LateUpdate pair.</summary>
        void LateUpdate()
        {
            if (!rebuildPending) return;
            rebuildPending = false;
            Rebuild();
        }

        void Rebuild()
        {
            // SetActive(false) takes effect immediately; Destroy() is deferred to end of frame — without
            // deactivating first, the old and newly-built tabs would both render for one frame (same trap
            // NavigatorView.Rebuild documents for its own rows).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            PaneState pane = WorkspaceOps.PaneAt(controller.Layout, PaneIndex);
            if (pane != null)
            {
                for (int i = 0; i < pane.Tabs.Count; i++)
                    BuildTab(pane, i, i == pane.ActiveIndex);
            }

            BuildPlusButton();
            BuildActivePaneUnderline();
        }

        /// <summary>A 2px accent line along the bottom of the FOCUSED pane's tab strip, and nothing at all in
        /// the other one — the two-panes arc's Task 5, Step 4.
        ///
        /// WHY IT IS NEEDED AT ALL, since Task 6 already tints the active TAB. Since Task 4 both panes draw a
        /// full, live page of their own, so both look equally "the one being typed in" — and from Task 5 on,
        /// which of them a Ctrl+Z, a Ctrl+F or an «@» lands in is decided by the caret, falling back to the
        /// FOCUSED pane when there is no caret anywhere (PageFocusRouter.ActiveView). That fallback is
        /// invisible state, and this line is the whole of its readout.
        ///
        /// BUILT HERE RATHER THAN IN Create, because Rebuild's own first loop destroys EVERY child of this
        /// strip: anything built once at construction time would survive exactly until the first
        /// OnLayoutChanged. Rebuild is also precisely where it belongs — FocusPane raises OnLayoutChanged
        /// (WorkspaceController.cs), so the strip is already being rebuilt at the moment focus moves.
        ///
        /// ignoreLayout, because this strip's HorizontalLayoutGroup would otherwise treat the line as a third
        /// kind of tab and give it a slot in the row. With it set, the group skips the child entirely and the
        /// anchors below place it against the strip's own bottom edge at full width.
        ///
        /// Colour from the theme (ThemeService.Tag/ThemeRole.Accent) like every other pixel in this file, so
        /// a theme switch repaints it too — the same rule TabDragHandler's insertion marker follows, and the
        /// same one BuildGhost's CanvasGroup comment explains the alpha exception to.</summary>
        void BuildActivePaneUnderline()
        {
            var layout = controller != null ? controller.Layout : null;
            if (layout == null || layout.FocusedPane != PaneIndex) return;

            var go = new GameObject("ActivePaneUnderline", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;   // a tab drag begun on this line must still reach the strip.
            ThemeService.Tag(img, ThemeRole.Accent);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            // Stretched horizontally by the anchors, so sizeDelta.x is the inset from them (0 = full width)
            // and only sizeDelta.y is a real height; anchoredPosition 0 sits it ON the bottom edge.
            rect.sizeDelta = new Vector2(0f, ActiveUnderlineHeight);
            rect.anchoredPosition = Vector2.zero;
        }

        void BuildTab(PaneState pane, int index, bool active)
        {
            var tab = pane.Tabs[index];

            // Full title in the GameObject's own name (visible in the Hierarchy/Inspector) even when the
            // label on-screen gets truncated below — the accessibility escape hatch review Finding 1 asked
            // for, without building a floating hover tooltip (NotesToolbar has one, but it's wired to a
            // fixed row of icon buttons with its own Update()-driven hover scan; adapting it here is out of
            // this fix's scope).
            var tabGO = new GameObject($"Tab_{index}_{tab.Title}", typeof(RectTransform));
            tabGO.transform.SetParent(transform, false);

            // Clips every CHILD of tabGO (title, close button/glyph) to tabGO's own, ACTUAL post-layout
            // rect — not tabGO's own Image below, which RectMask2D never affects on the GameObject it sits
            // on, only on descendants. This is the structural backstop TruncateTitle's arithmetic budget
            // (see below) cannot provide by itself: the strip's HorizontalLayoutGroup can compress a tab
            // well below MaxTabWidth once the pane holds enough tabs (childControlWidth=true,
            // minWidth/flexibleWidth=0 on every tab — see TabStripView.Create), and a title truncated to
            // fit the COMPILE-TIME MaxTabWidth budget can still overflow a tab that landed SMALLER than
            // that at runtime. Review Finding 1's residual: without this mask, that overflow drew straight
            // over the neighbouring tab. A strip-level mask would stop text escaping the strip but not text
            // drawing over a NEIGHBOURING tab within the same strip, which is the actual symptom — the mask
            // has to sit on each tab, not the strip.
            tabGO.AddComponent<RectMask2D>();

            var bg = tabGO.AddComponent<Image>();
            ThemeService.Tag(bg, active ? ThemeRole.Bg : ThemeRole.Panel);

            // Added BEFORE the two Buttons below, because the close button's listener closes over it (Step 5's
            // "a close that fires on drag-release" trap). Task 10d; see TabDragHandler's own doc for the
            // gesture, and note it deliberately does NOT suppress the TAB's own click: uGUI only delivers that
            // click when the pointer is released back over this same tab, which is a drop that changes
            // nothing, and activating the tab you just put back is the right outcome.
            var drag = tabGO.AddComponent<TabDragHandler>();
            drag.Bind(this, index, tab.Title);

            var tabBtn = tabGO.AddComponent<Button>();
            tabBtn.targetGraphic = bg;
            tabBtn.onClick.AddListener(() =>
            {
                // Both calls are safe to fire unconditionally: SetActiveTab/Focus each no-op (and skip
                // RaiseChanged) when nothing would change, so clicking the already-active tab in an
                // already-focused pane costs one wasted comparison, not a spurious rebuild. Focus follows
                // the click even when the tab was already active — otherwise clicking a tab in an unfocused-
                // but-already-active pane would silently do nothing, and the next navigator open would land
                // in the wrong pane (see the brief's "focus follows the click" note).
                controller.SetActive(PaneIndex, index);
                controller.FocusPane(PaneIndex);
            });

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(tabGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.font = builtinFont;
            title.fontSize = 13;
            title.fontStyle = FontStyle.Bold;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;   // single line, never wrap — width
            title.verticalOverflow = VerticalWrapMode.Truncate;        // is controlled below, not by wrapping.
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;   // clicks must reach tabBtn, not the label.
            ThemeService.Tag(title, active ? ThemeRole.Txt : ThemeRole.Mut);

            // Shrinks the COMMON case tidily: maxTextWidth assumes the tab gets its full MaxTabWidth
            // ceiling, which holds whenever the strip has room to grant every tab its own preferred width.
            // This alone does NOT guarantee containment — once enough tabs are open that the strip's
            // HorizontalLayoutGroup compresses a tab below this compile-time budget, even an
            // already-truncated title can be wider than the tab actually ends up. The tabGO RectMask2D
            // added above is what makes containment unconditional: it clips to the tab's REAL,
            // post-layout rect regardless of how far the layout group squeezed it, so this arithmetic
            // truncation only needs to get the ellipsis to look right, not to be the thing standing
            // between a title and its neighbours.
            float maxTextWidth = MaxTabWidth - TitlePaddingLeft - CloseReserve;
            string displayTitle = TruncateTitle(title, tab.Title, maxTextWidth);
            title.text = displayTitle;

            // Text.preferredWidth is readable immediately after font/fontSize/text are set, with no layout
            // pass needed — the one number in this file that is genuinely unverifiable without opening the
            // Editor (see the task report). If it ever measures 0 (missing font, empty title), the Clamp
            // below degrades to "tab sits at MinTabWidth", not an invisible zero-width tab.
            float tabWidth = Mathf.Clamp(title.preferredWidth + TitlePaddingLeft + CloseReserve, MinTabWidth, MaxTabWidth);

            var tabElement = tabGO.AddComponent<LayoutElement>();
            tabElement.preferredWidth = tabWidth;
            tabElement.minWidth = 0f;
            tabElement.flexibleWidth = 0f;

            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(TitlePaddingLeft, 0f);
            titleRect.offsetMax = new Vector2(-CloseReserve, 0f);

            var closeGO = new GameObject("Close", typeof(RectTransform));
            closeGO.transform.SetParent(tabGO.transform, false);
            var closeBg = closeGO.AddComponent<Image>();
            closeBg.color = Color.clear;   // hit-area only; the "×" glyph below is the visible part. An
                                            // alpha-0 Image still raycasts by default in uGUI — the same
                                            // trick DraggableDivider's idle state relies on — so hover/click
                                            // both work while the button itself stays invisible.
            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            closeBtn.onClick.AddListener(() =>
            {
                // BELT for Step 5's named trap: the tab the user just dragged must not also close on release.
                // The BRACES are uGUI's own — InputSystemUIInputModule.cs:774-783 clears eligibleForClick the
                // moment a drag's handler (this tab) differs from the pressed object (this button), so a drag
                // that STARTED on the "×" already cannot produce a click. This guard covers the case that
                // mechanism does not describe in one line (a drag begun on the tab and released over its own
                // "×") and, more importantly, survives a future input-module swap. It is readable here because
                // the click is dispatched BEFORE endDragHandler in the same release block
                // (InputSystemUIInputModule.cs:707-741), so `drag` is still mid-gesture at this point.
                if (drag != null && drag.DragConsumedPointer) return;
                controller.CloseTab(PaneIndex, index);
            });

            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(CloseButtonSize, CloseButtonSize);
            closeRect.anchoredPosition = new Vector2(-CloseButtonMargin, 0f);

            var closeGlyphGO = new GameObject("Glyph", typeof(RectTransform));
            closeGlyphGO.transform.SetParent(closeGO.transform, false);
            var closeGlyph = closeGlyphGO.AddComponent<Text>();
            // "×" (U+00D7), not "✕" (U+2715) — matches NotesTreeSidebar.AddRenameAndDelete's own delete
            // glyph exactly. LegacyRuntime.ttf is Arial-derived and carries U+00D7; U+2715 is not a
            // guaranteed hit, and an absent glyph would render blank on a button that is ALREADY invisible
            // by default (hover-revealed), giving no visible signal that anything is wrong.
            closeGlyph.text = "×";
            closeGlyph.font = builtinFont;
            closeGlyph.fontSize = 12;
            closeGlyph.alignment = TextAnchor.MiddleCenter;
            closeGlyph.raycastTarget = false;
            ThemeService.Tag(closeGlyph, active ? ThemeRole.Txt : ThemeRole.Mut);
            var closeGlyphRect = closeGlyphGO.GetComponent<RectTransform>();
            closeGlyphRect.anchorMin = Vector2.zero;
            closeGlyphRect.anchorMax = Vector2.one;
            closeGlyphRect.offsetMin = Vector2.zero;
            closeGlyphRect.offsetMax = Vector2.zero;

            closeGO.SetActive(false);   // hover-revealed — see TabHoverReveal below; brief Step 1 asks for
                                         // the "×" to appear on hover, not sit permanently visible.
            var hover = tabGO.AddComponent<TabHoverReveal>();
            hover.Target = closeGO;
        }

        /// <summary>Shrinks `full` one character at a time, appending an ellipsis, until `measurer`
        /// (already carrying the right font/fontSize/fontStyle) reports a preferredWidth that fits within
        /// maxWidth — or returns `full` unchanged if it already fits. `measurer.text` is left holding
        /// whatever this method returns, so the caller does not need to re-assign it. A linear scan rather
        /// than a binary search: tab titles in this project (page/settlement/dungeon names) are short, so
        /// the extra remeasure calls cost nothing per rebuild, and a straight-line scan is easier for a
        /// reviewer to verify by inspection than a binary-search off-by-one.
        ///
        /// This handles the common case (an overlong TITLE) tidily with a conventional ellipsis, which
        /// reads better than a hard clip mid-glyph — but it is arithmetic against a compile-time budget, not
        /// a guarantee: it cannot know how far the strip's HorizontalLayoutGroup will actually compress this
        /// tab once enough tabs are open (that depends on every OTHER tab's width too, at rebuild time).
        /// BuildTab's tabGO RectMask2D is the structural backstop for THAT case — it clips to the tab's
        /// real, post-layout rect regardless of what this method predicted. Keep both: this method for a
        /// readable ellipsis in the ordinary case, the mask so containment holds even when this method's
        /// prediction turns out wrong.</summary>
        static string TruncateTitle(Text measurer, string full, float maxWidth)
        {
            measurer.text = full;
            if (string.IsNullOrEmpty(full) || measurer.preferredWidth <= maxWidth) return full;

            for (int len = full.Length - 1; len > 0; len--)
            {
                string candidate = full.Substring(0, len) + "…";
                measurer.text = candidate;
                if (measurer.preferredWidth <= maxWidth) return candidate;
            }

            measurer.text = "…";
            return "…";
        }

        void BuildPlusButton()
        {
            var go = new GameObject("Plus", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = PlusButtonWidth;
            element.minWidth = 0f;
            element.flexibleWidth = 0f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => OnRequestQuickOpen?.Invoke(PaneIndex));

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "+";
            text.font = builtinFont;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            ThemeService.Tag(text, ThemeRole.Mut);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        // ── Geometry the drop resolver asks for (Task 10d) ────────────────────────

        /// <summary>This strip's own rect. The strip GameObject IS the component's GameObject (see Create),
        /// so there is no wrapper to unwrap. Named StripRect, not Rect: a member called Rect would shadow
        /// UnityEngine.Rect for every later line in this class, the same-name-type trap this project has now
        /// been bitten by three times.</summary>
        internal RectTransform StripRect => (RectTransform)transform;

        /// <summary>Every tab GameObject in this strip, in tab order — identified by carrying a
        /// TabDragHandler, which is what separates them from the «+» button BuildPlusButton appends last.
        /// Identifying them by COMPONENT rather than by the "Tab_{index}_{title}" name is deliberate: the
        /// name embeds a user-typed title, so a page called "0_x" could otherwise be parsed as a tab index.
        /// Child order equals tab order by construction — Rebuild calls BuildTab in ascending index and each
        /// BuildTab appends.</summary>
        internal void CollectTabRects(List<RectTransform> into)
        {
            into.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i) as RectTransform;
                if (child != null && child.GetComponent<TabDragHandler>() != null) into.Add(child);
            }
        }

        /// <summary>The tab index a drop at `pointerX` (a screen X, which for this ScreenSpaceOverlay canvas
        /// is also a world X — same equivalence SurfaceRegistry.cs:629 relies on) would insert BEFORE: 0 left
        /// of the first tab, Count past the last.
        ///
        /// PRE-REMOVAL BY CONSTRUCTION, and that is load-bearing. The dragged tab stays visible in its strip
        /// for the whole gesture, so the list this walks still contains it, and the index this returns is an
        /// index into the pane's tabs AS THEY ARE NOW — which is exactly what WorkspaceOps.MoveTab's toIndex
        /// means (WorkspaceOps.cs:286-289 subtracts one itself when the removal shifted the destination
        /// left). Anyone who later "improves" the drag by hiding or removing the source tab during the
        /// gesture must re-derive this, or every same-pane reorder silently lands one slot off.</summary>
        internal int InsertIndexAt(float pointerX, List<RectTransform> scratch)
        {
            CollectTabRects(scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                Vector3 center = scratch[i].TransformPoint(scratch[i].rect.center);
                if (pointerX < center.x) return i;
            }
            return scratch.Count;
        }

        /// <summary>Where to draw the insertion bar for `index`: the left edge of the tab that would follow
        /// it, or the right edge of the last tab when the drop appends. An EMPTY strip (no tabs at all —
        /// reachable while the other pane holds everything) falls back to the strip's own left inset, so the
        /// marker still has somewhere honest to sit. Half of the layout group's `spacing` is given back so
        /// the bar lands in the GAP between two tabs rather than on top of one.</summary>
        internal float MarkerXFor(int index, List<RectTransform> scratch)
        {
            CollectTabRects(scratch);
            const float HalfSpacing = 1f;   // TabStripView.Create sets HorizontalLayoutGroup.spacing = 2f.

            if (scratch.Count == 0)
            {
                StripRect.GetWorldCorners(WorldCorners);
                return WorldCorners[0].x + 6f;   // Create's layout padding.left
            }
            if (index <= 0)
            {
                scratch[0].GetWorldCorners(WorldCorners);
                return WorldCorners[0].x - HalfSpacing;
            }
            int at = Mathf.Min(index, scratch.Count) - 1;
            scratch[at].GetWorldCorners(WorldCorners);
            return WorldCorners[2].x + HalfSpacing;
        }

        /// <summary>Shared scratch for GetWorldCorners. Static readonly, so a Play-mode domain reload
        /// re-runs the initializer on the class's next touch rather than leaving a null field behind — the
        /// same reason MapSurfaceHost re-assigns its own collections in Rewire rather than trusting
        /// initializers (SurfaceRegistry.cs:242-250). Single-threaded UI code, read and consumed before the
        /// next call.</summary>
        internal static readonly Vector3[] WorldCorners = new Vector3[4];
    }

    /// <summary>Shows/hides a single target GameObject on hover — backs each tab's "×" close button, which
    /// the brief specifies as hover-only rather than permanently visible (contrast NotesTreeSidebar's page
    /// rows, where the delete "×" is always shown). Plain sibling class in this file rather than its own
    /// file, the same arrangement ThemeService.cs uses for ThemedGraphic.</summary>
    class TabHoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Target;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Target != null) Target.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Target != null) Target.SetActive(false);
        }
    }

    /// <summary>
    /// Task 10d — drag a tab to the other pane's strip, reorder it within its own, or drag it out to the
    /// right to CREATE the split. One instance per tab, added by TabStripView.BuildTab, sibling class in this
    /// file for the same reason TabHoverReveal above is one.
    ///
    /// THE THRESHOLD IS THE EVENT SYSTEM'S. Step 1 asks that a drag not start until the pointer has moved a
    /// few pixels, and that is already unconditional: InputSystemUIInputModule.cs:759-770 only dispatches
    /// beginDragHandler once (pressPosition - position).sqrMagnitude has passed
    /// EventSystem.pixelDragThreshold² (Unity's default is 10px), so by the time OnBeginDrag below runs the
    /// pointer has ALREADY moved further than any second threshold this class could sensibly impose — one
    /// added here would be code that reads like a safeguard and can never fire.
    ///
    /// THAT THRESHOLD IS 10 AND HAS NO SCENE DEPENDENCY. There is still no EventSystem in
    /// SampleScene.unity — Task 11's scene edit deliberately did not add one — so every EventSystem in this
    /// project is created in code (EnsureEventSystemExists in WorkspaceBuilder.cs, and six siblings
    /// elsewhere) and AddComponent runs EventSystem's own `m_DragThreshold = 10` field initializer. What must
    /// NOT happen is the inverse: adding a scene EventSystem with a lowered threshold, or lowering
    /// pixelDragThreshold in code. At 0 the casualty is not click-to-activate — that survives, since
    /// pointerPress and pointerDrag are both this tab so the click stays eligible, and Commit's no-op skip
    /// swallows the spurious move, costing one frame of ghost flash. It is the CLOSE BUTTON: a single pixel
    /// of jitter while pressing the "×" makes pointerPress != pointerDrag, which clears eligibleForClick
    /// (InputSystemUIInputModule.cs:774-783), and closing a tab silently stops working.
    ///
    /// THE GHOST AND THE MARKER ARE RE-ACQUIRED BY NAME, never re-created blind (EnsureOverlay). They are two
    /// long-lived GameObjects under the workspace canvas, tracked by plain non-[SerializeField] fields — the
    /// exact shape this arc's recurring defect family takes (WorkspaceController.cs:68-92,
    /// SurfaceRegistry.cs:242-250): a Play-mode domain reload nulls the field while the GameObject survives,
    /// and a blind re-create would then leave an ORPHANED ghost on screen that nothing holds a reference to
    /// and nothing can hide. Transform.Find over the canvas root is the same recovery
    /// WorkspaceController.ShellRoot and MapSurfaceHost's frame lookup use, chosen over rebuilding because
    /// there is nothing to rebuild: the survivor is complete and correct, only unreferenced. The OTHER half
    /// of the reload story is the `strip == null || strip.Controller == null` guard in OnBeginDrag: after a
    /// reload the surviving tabs are inert (WorkspaceBuilder.Awake's non-recovery paragraph), so a drag
    /// begun on one must no-op rather than NRE.
    /// </summary>
    class TabDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        const string GhostName = "__TabDragGhost";
        const string MarkerName = "__TabDropMarker";
        const float GhostAlpha = 0.85f;
        const float MarkerWidth = 3f;

        /// <summary>Where a release would put the tab. `Valid` false is the third outcome — cancel — and is
        /// what a pointer outside every strip and outside the split zone resolves to, so "nothing happens"
        /// needs no separate flag. The marker coordinates travel with it because both are derived from the
        /// same world corners in the same pass; re-deriving them at draw time would mean a second
        /// GetWorldCorners read that could disagree with the one the index came from.</summary>
        struct DropTarget
        {
            public bool Valid;
            public int Pane;
            public int Index;
            public float MarkerX;
            public float MarkerYMin;
            public float MarkerYMax;
        }

        TabStripView strip;
        int tabIndex;
        string title;

        TabStripView otherStrip;      // resolved once per gesture, in OnBeginDrag
        bool dragging;
        bool cancelled;
        DropTarget target;
        Vector2 ghostSize;

        RectTransform ghost;          // read ONLY through EnsureOverlay — see the class doc
        RectTransform marker;

        static readonly List<RectTransform> Scratch = new List<RectTransform>();

        /// <summary>True from the moment this tab's drag begins until OnEndDrag has run. Read by the close
        /// button's onClick, which uGUI dispatches BEFORE endDragHandler in the same release block — see
        /// TabStripView.BuildTab's close listener for the whole reasoning.</summary>
        public bool DragConsumedPointer => dragging;

        public void Bind(TabStripView owner, int index, string tabTitle)
        {
            strip = owner;
            tabIndex = index;
            title = tabTitle ?? "";
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Left button only: a middle-drag is a scroll gesture elsewhere in this project and a right-drag
            // has no meaning here, and both would otherwise raise the full begin/drag/end sequence.
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (strip == null || strip.Controller == null) return;   // post-reload inert tab — see class doc

            dragging = true;
            cancelled = false;
            target = default;
            otherStrip = FindOtherStrip();
            ghostSize = ((RectTransform)transform).rect.size;

            // Both overlays are resolved and ordered ONCE, here, rather than on every drag frame: sibling
            // order cannot change during a gesture (nothing else adds children to the canvas root), and
            // re-asserting it per frame only dirties the canvas hierarchy for no effect. Ghost first, marker
            // second, so the marker ends up ABOVE it — the insertion line has to stay readable through the
            // translucent ghost at the moment of release, which is the pair's whole point.
            var ghostRect = EnsureOverlay(ref ghost, GhostName, BuildGhost);
            if (ghostRect != null)
            {
                ghostRect.SetAsLastSibling();
                // Re-labelled every gesture: ONE ghost serves every tab in both strips, so whatever title it
                // is carrying is the previous drag's.
                var label = ghostRect.GetComponentInChildren<Text>(true);
                if (label != null) label.text = title;
            }
            var markerRect = EnsureOverlay(ref marker, MarkerName, BuildMarker);
            if (markerRect != null) markerRect.SetAsLastSibling();

            UpdateOverlays(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || cancelled) return;
            UpdateOverlays(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging) return;

            HideOverlays();
            if (!cancelled && target.Valid) Commit();

            dragging = false;
            cancelled = false;
            target = default;
            otherStrip = null;
        }

        /// <summary>Escape cancels, and is polled rather than handled: uGUI delivers no keyboard event to the
        /// object being dragged, and the pointer may be perfectly still when the key is pressed, so
        /// OnDrag is not a place this can be noticed. Reads Keyboard.current directly, the same raw-hardware
        /// read QuickOpenPopup.Update makes for its own Escape (QuickOpenPopup.cs:169-188), and tolerates a
        /// null Keyboard (a machine with no keyboard device attached) rather than assuming one.
        ///
        /// Cancels the DROP, not the gesture: the pointer is still down, so uGUI will still deliver OnDrag
        /// and OnEndDrag: those see `cancelled` and leave the layout untouched, which is Step 3's third
        /// outcome.
        ///
        /// A RAW POLL IS SHARED, not owned: every Escape handler in this project reads the same hardware key
        /// independently, so one press reaches all of them whose own guard is open. The only one that can be
        /// open DURING a tab drag is BattleGridScreen.Update (BattleGridScreen.cs:255-257 — guarded merely on
        /// its view being enabled, and its surface can be showing in a pane while the drag happens), so
        /// Escape there both cancels the drag and closes that surface. NavigatorView's is gated on an active
        /// rename, QuickOpenPopup's on the palette being open, DungeonViewController's on an armed brush —
        /// none of which coexist with a drag. Left as it is: routing Escape through a modal stack is a change
        /// to five existing screens, not to this gesture.</summary>
        void Update()
        {
            if (!dragging || cancelled) return;
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            cancelled = true;
            HideOverlays();
        }

        /// <summary>The one path that cleans up a drag NOBODY will end. TabStripView.Rebuild deactivates each
        /// old child before destroying it (TabStripView.cs:112-120) precisely so the deactivation is
        /// synchronous, which means a layout change landing mid-drag runs this on the dragged tab — and after
        /// it, no OnEndDrag can ever arrive to hide the ghost. Without this the ghost would sit on screen for
        /// the rest of the session with the component that owned it destroyed: the orphan case, arrived at
        /// through a live rebuild rather than through a domain reload.</summary>
        void OnDisable()
        {
            if (!dragging) return;
            HideOverlays();
            dragging = false;
            cancelled = false;
            target = default;
            otherStrip = null;
        }

        // ── Drop resolution ────────────────────────────────────────────────────

        void UpdateOverlays(Vector2 screenPos)
        {
            target = ResolveDrop(screenPos);

            var ghostRect = EnsureOverlay(ref ghost, GhostName, BuildGhost);
            if (ghostRect != null)
            {
                ghostRect.gameObject.SetActive(true);
                ghostRect.sizeDelta = ghostSize;
                // ScreenSpaceOverlay: world position IS screen-pixel position (SurfaceRegistry.cs:629), so the
                // pointer's screen coordinate can be written straight into a world position, whatever the
                // ghost's parent happens to be.
                ghostRect.position = new Vector3(screenPos.x, screenPos.y, 0f);
            }

            var markerRect = EnsureOverlay(ref marker, MarkerName, BuildMarker);
            if (markerRect == null) return;
            markerRect.gameObject.SetActive(target.Valid);
            if (!target.Valid) return;
            markerRect.sizeDelta = new Vector2(MarkerWidth, Mathf.Max(0f, target.MarkerYMax - target.MarkerYMin));
            markerRect.position = new Vector3(target.MarkerX, (target.MarkerYMin + target.MarkerYMax) * 0.5f, 0f);
        }

        /// <summary>Step 3's three outcomes, in the order it states them. Resolved from the pointer against
        /// each candidate's GetWorldCorners rather than from eventData's raycast results: a raycast reports
        /// the topmost GRAPHIC, which over a pane's content area is whatever surface happens to be hosted
        /// there (a full-screen ex-screen canvas, or nothing at all where the map punches its hole), none of
        /// which can answer "which pane is this". The corners can, and are already this project's way of
        /// turning a rect into screen pixels (SurfaceRegistry.cs:629).</summary>
        DropTarget ResolveDrop(Vector2 p)
        {
            WorkspaceController controller = strip.Controller;
            WorkspaceLayout layout = controller.Layout;

            // (1) Over a strip — that strip's pane, at the insertion index under the pointer. Own strip or
            // the other one, reorder or move, it is one MoveTab either way. PaneAt is the authority on
            // whether pane 1 exists at all: ReflowPanes leaves the secondary strip's GameObject deactivated
            // (and therefore at a stale rect) whenever there is no split.
            for (int pane = 0; pane <= 1; pane++)
            {
                if (WorkspaceOps.PaneAt(layout, pane) == null) continue;
                TabStripView s = StripFor(pane);
                if (s == null || !Contains(s.StripRect, p)) continue;

                var hit = new DropTarget { Valid = true, Pane = pane, Index = s.InsertIndexAt(p.x, Scratch) };
                hit.MarkerX = s.MarkerXFor(hit.Index, Scratch);
                s.StripRect.GetWorldCorners(TabStripView.WorldCorners);
                hit.MarkerYMin = TabStripView.WorldCorners[0].y;
                hit.MarkerYMax = TabStripView.WorldCorners[1].y;
                return hit;
            }

            // (2) Over the region the SECOND pane would occupy, while there is no second pane — the Chrome
            // gesture the user asked for by name, and the whole point of restoring this step: the drag has to
            // be able to PRODUCE a side-by-side layout, not only move tabs between panes that already exist.
            //
            // THE RIGHT-HAND ZONE ONLY, because Secondary is always the right pane and WorkspaceOps'
            // NormalizeSplit R4 forbids the mirror image (an empty Primary beside a full Secondary is
            // repaired by promotion, so a "split to the left" would immediately undo itself). The boundary is
            // Layout.SplitRatio, not a hardcoded half, so the marker sits exactly where the divider will
            // land.
            //
            // AT LEAST TWO TABS IN THE SOURCE PANE, or there is no split to promise: moving a pane's LAST tab
            // out empties it, and NormalizeSplit then collapses the new pane straight back (R3/R4) — the user
            // would see an insertion marker, release, and watch nothing happen. Refusing the target leaves no
            // marker drawn, which says "not here" honestly.
            //
            // With a split ALREADY on screen, a drop on the other pane's BODY is deliberately not a move: it
            // falls through to (3) and cancels. Step 3 scopes this outcome to the one-pane case, and the
            // other pane's strip — the unambiguous target, with a visible insertion point — is right there.
            if (layout.Secondary == null)
            {
                RectTransform content = controller.PaneContent(0);
                PaneState source = WorkspaceOps.PaneAt(layout, strip.PaneIndex);
                if (content != null && source != null && source.Tabs.Count >= 2 && Contains(content, p))
                {
                    content.GetWorldCorners(TabStripView.WorldCorners);
                    float xMin = TabStripView.WorldCorners[0].x;
                    float xMax = TabStripView.WorldCorners[2].x;
                    float splitX = xMin + (xMax - xMin) * layout.SplitRatio;
                    if (p.x >= splitX)
                        return new DropTarget
                        {
                            Valid = true,
                            Pane = 1,
                            Index = 0,
                            MarkerX = splitX,
                            MarkerYMin = TabStripView.WorldCorners[0].y,
                            MarkerYMax = TabStripView.WorldCorners[1].y,
                        };
                }
            }

            // (3) Anywhere else — cancel, layout untouched.
            return default;
        }

        /// <summary>Commits through WorkspaceController.MoveTab, the only mutation door this gesture has.
        ///
        /// A DROP THAT WOULD NOT MOVE THE TAB IS DROPPED ON THE FLOOR. Within its own pane, both `tabIndex`
        /// (insert before itself) and `tabIndex + 1` (insert directly after itself) put the tab back exactly
        /// where it started — MoveTab would return true for either, raise OnLayoutChanged, rebuild both
        /// strips and, per its own contract, make the dragged tab the ACTIVE one. For a user who nudged a
        /// background tab a few pixels and let go, that silently switches which surface the pane is showing.
        /// Skipping here is what makes "a reorder to the same index changes nothing" true of the gesture;
        /// WorkspaceOpsSelfTests pins the arithmetic underneath it (both no-op forms), since the gesture
        /// itself cannot be harness-tested.</summary>
        void Commit()
        {
            if (target.Pane == strip.PaneIndex && (target.Index == tabIndex || target.Index == tabIndex + 1)) return;
            strip.Controller.MoveTab(strip.PaneIndex, tabIndex, target.Pane, target.Index);
        }

        TabStripView StripFor(int pane)
        {
            if (strip.PaneIndex == pane) return strip;
            return otherStrip != null && otherStrip.PaneIndex == pane ? otherStrip : null;
        }

        /// <summary>The sibling pane's strip, found once per gesture (not per drag frame —
        /// GetComponentsInChildren allocates). Searched from PaneContainer, the parent both panes share:
        /// strip -> its pane -> the container, exactly the hierarchy
        /// WorkspaceBuilder.BuildPaneContainer/BuildPane construct.
        /// includeInactive is required — the secondary pane's whole GameObject is deactivated while there is
        /// no split, which is the state a split-creating drag starts from.</summary>
        TabStripView FindOtherStrip()
        {
            Transform pane = strip.transform.parent;
            Transform container = pane != null ? pane.parent : null;
            if (container == null) return null;

            foreach (var candidate in container.GetComponentsInChildren<TabStripView>(true))
                if (candidate != strip) return candidate;
            return null;
        }

        static bool Contains(RectTransform rect, Vector2 screenPos)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            rect.GetWorldCorners(TabStripView.WorldCorners);
            var c = TabStripView.WorldCorners;
            return screenPos.x >= c[0].x && screenPos.x <= c[2].x
                && screenPos.y >= c[0].y && screenPos.y <= c[2].y;
        }

        // ── The two reused overlays ────────────────────────────────────────────

        // A HideStrandedOverlays(canvasRoot) static used to live here, called from WorkspaceBuilder.Awake's
        // recompile branch: a reload landing mid-drag left the ghost and the marker switched ON with nothing
        // able to switch them off, because every surviving TabDragHandler comes back with `dragging` false
        // and `strip` null, so OnBeginDrag early-returns and HideOverlays below is unreachable forever. Task
        // 11 removed it rather than kept it: both overlays are parented to the WORKSPACE CANVAS (see
        // BuildGhost), and that canvas is now demolished and rebuilt on every reload
        // (WorkspaceBuilder.DemolishForRebuild), so the strand cannot outlive the gesture. QuickOpenPopup's
        // palette is the one overlay in this shell that is NOT a canvas child, which is why the equivalent
        // cleanup still exists there (QuickOpenPopup.DestroyStrandedCanvas) and only there.

        void HideOverlays()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
            if (marker != null) marker.gameObject.SetActive(false);
        }

        /// <summary>Find-then-build, in that order, and never build without having looked: see the class doc
        /// for why a blind re-create leaves an orphan. `cached` is by ref so the found-or-built rect is
        /// written back into the caller's field, which is what keeps this to one Transform.Find per gesture
        /// rather than one per drag frame.</summary>
        RectTransform EnsureOverlay(ref RectTransform cached, string overlayName, System.Func<Transform, RectTransform> build)
        {
            if (cached != null) return cached;

            var canvas = GetComponentInParent<Canvas>();
            // Never null in the built shell; keeps a bare test rig from throwing.
            if (canvas == null) return null;

            // Written out rather than `existing as RectTransform ?? build(...)`: `??` on a UnityEngine.Object
            // bypasses the lifetime-aware == operator, so a destroyed-but-not-null survivor would slip
            // through as if it were live. An explicit null check goes through that operator.
            var existing = canvas.transform.Find(overlayName) as RectTransform;
            cached = existing != null ? existing : build(canvas.transform);
            return cached;
        }

        /// <summary>A translucent copy of the tab that follows the pointer. Parented to the CANVAS ROOT, not
        /// to a strip: Task 6 gave every tab its own RectMask2D (TabStripView.cs:144-155), and a ghost
        /// parented under a strip would be clipped to the very tab it is being dragged out of. Last child of
        /// the canvas, so it also draws over both panes.</summary>
        static RectTransform BuildGhost(Transform canvasRoot)
        {
            var go = new GameObject(GhostName, typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            var rect = Centered((RectTransform)go.transform);

            var bg = go.AddComponent<Image>();
            bg.raycastTarget = false;
            ThemeService.Tag(bg, ThemeRole.Elev);

            // Translucency via CanvasGroup rather than by tinting the Image: ThemeService.Tag OWNS that
            // Image's color and repaints it on every theme switch, so an alpha written into it would be
            // silently reverted the first time the user toggles the theme mid-session.
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = GhostAlpha;
            group.interactable = false;
            group.blocksRaycasts = false;   // the drop must still see what is UNDER the pointer

            var labelGO = new GameObject("Title", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            var label = labelGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            ThemeService.Tag(label, ThemeRole.Txt);

            var labelRect = (RectTransform)labelGO.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);

            return rect;
        }

        /// <summary>The insertion bar: a thin vertical accent line at the index the drop would use. Step 4's
        /// whole justification is that without it a reorder and a no-op look identical before release — and
        /// it does double duty for the split-creating drop, where it is stretched down the pane's full height
        /// to show where the divider will land.</summary>
        static RectTransform BuildMarker(Transform canvasRoot)
        {
            var go = new GameObject(MarkerName, typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            var rect = Centered((RectTransform)go.transform);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            ThemeService.Tag(img, ThemeRole.Accent);

            return rect;
        }

        /// <summary>Point anchors and a centred pivot, so sizeDelta IS the rect's size and `position` places
        /// its CENTRE. A RectTransform created by `new GameObject(..., typeof(RectTransform))` defaults to
        /// stretch anchors, under which sizeDelta means "inset from the parent's edges" and a written world
        /// position fights the parent's own size — both overlays are positioned in absolute screen pixels,
        /// so neither may inherit that.</summary>
        static RectTransform Centered(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
