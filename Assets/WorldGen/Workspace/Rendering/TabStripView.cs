using UnityEngine;
using UnityEngine.EventSystems;
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
    /// Tab-drag-between-strips (plan Step 4) is deliberately NOT implemented here — see the task report.
    /// Opening a surface in the other pane stays reachable via Ctrl+K's Shift+Enter (Task 8) and the
    /// navigator's context menu (Task 7); WorkspaceOps.MoveTab exists and is tested but stays unexposed on
    /// WorkspaceController, since a public mutation path nothing calls is dead code.
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

        /// <summary>Task 8's hook: the quick-open palette this should launch does not exist yet (out of this
        /// task's scope — see the brief's scope-discipline note), so clicking «+» invokes this with the
        /// strip's own pane index and otherwise does nothing until Task 8 assigns it.</summary>
        public System.Action<int> OnRequestQuickOpen;

        public int PaneIndex { get; private set; }

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
            closeBtn.onClick.AddListener(() => controller.CloseTab(PaneIndex, index));

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
}
