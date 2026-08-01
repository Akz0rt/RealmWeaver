using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>What a tab's content actually is, once WorkspaceController decides which SurfaceRef is
    /// active in a pane. One host instance serves EVERY tab of its Kind — there is no per-tab or per-pane
    /// copy — which is why Show/Hide carry no pane index: WorkspaceController passes the physical
    /// RectTransform to show INTO, and the host re-parents itself there.
    ///
    /// "Parent yourself here" (Show's own parameter doc) is load-bearing, not a suggestion:
    /// WorkspaceOps.NormalizeSplit can promote Secondary into Primary's slot, and WorkspaceController.
    /// PaneContent(int) keeps naming the same PHYSICAL container per index regardless — so a host that
    /// only re-reads PaneContent(0) once would still point at the OLD container after a promotion. Every
    /// Show() call re-parents unconditionally, every time, so recomputing PaneContent(pane) fresh on each
    /// call (which WorkspaceController.SyncSurfaces does) makes promotion handled automatically.
    ///
    /// Only one pane can show a given Kind's real content at a time (see PageSurfaceHost's own doc for why
    /// that is an accepted limitation, not a bug, for the Page surface specifically). WorkspaceController
    /// .SyncSurfaces resolves the contest by iterating the FOCUSED pane FIRST and skipping any later pane
    /// whose host reports an already-claimed ShareGroup — so the focused pane gets the host and the other
    /// pane's Show never runs, rather than running and being overwritten.</summary>
    public interface ISurfaceHost
    {
        SurfaceKind Kind { get; }

        /// <summary>Show the surface identified by `id` inside `paneContent`. Must re-parent every call —
        /// see the class doc above.</summary>
        void Show(RectTransform paneContent, string id);

        /// <summary>Called when NO pane's active tab is this Kind any more. Must leave nothing visible
        /// behind — a host that only reparents on Show and never hides would linger in whatever pane it was
        /// last shown in, drawing over/behind whatever that pane shows next.</summary>
        void Hide();

        /// <summary>The display title for `id`, looked up fresh (not cached) — e.g. a page's current Name.
        /// Not yet wired to any call site as of Task 9; kept correct now so a future title-refresh path
        /// (a tab's title going stale after a rename) has something real to call.</summary>
        string TitleFor(string id);

        /// <summary>Identity of the PHYSICAL surface behind this host, for hosts that share one. Two hosts
        /// returning the same reference are two Kinds driving the same object, and WorkspaceController
        /// .SyncSurfaces will let only the higher-priority pane show it — see that method's own doc.
        ///
        /// Most hosts are their own group and return `this`; the exception is ScreenSurfaceHost, where
        /// Settlement, BuildingInterior and Dungeon are three Kinds over one DungeonEditorScreen. Compared by
        /// REFERENCE (a HashSet&lt;object&gt; with default equality), so returning a fresh object per call
        /// would silently disable the sharing rule — return a stable instance.</summary>
        object ShareGroup { get; }
    }

    /// <summary>Surface kind -> the one host object that shows/hides it. Task 9 registered Page and WorldMap;
    /// Task 10c adds the remaining five (PoiEditor/Settlement/BuildingInterior/Dungeon/BattleGrid) via
    /// ScreenSurfaceHosts below, so For() now returns a host for every SurfaceKind in a fully-built scene.
    /// It still returns null for a Kind whose backing screen was not found (a bare/partial scene), and
    /// WorkspaceController.SyncSurfaces still treats "no host for this Kind" as "nothing to show", not an
    /// error.</summary>
    public class SurfaceRegistry
    {
        readonly Dictionary<SurfaceKind, ISurfaceHost> hosts = new Dictionary<SurfaceKind, ISurfaceHost>();

        public void Register(ISurfaceHost host)
        {
            if (host == null) return;
            hosts[host.Kind] = host;
        }

        public ISurfaceHost For(SurfaceKind k) => hosts.TryGetValue(k, out var host) ? host : null;

        /// <summary>Every registered host, regardless of Kind — WorkspaceController.SyncSurfaces walks this
        /// to Hide() whichever hosts no pane wants any more, without needing to track "what was shown last
        /// frame" itself.</summary>
        public IEnumerable<ISurfaceHost> All => hosts.Values;
    }

    /// <summary>Hosts the Page surface: the ONE DocumentPageView NotesRootBuilder builds, re-parented into
    /// whichever pane's content area currently shows a Page-kind tab. Not a MonoBehaviour — it has no
    /// per-frame work, unlike MapSurfaceHost below — just a thin adapter around the view NotesRootBuilder
    /// already owns and keeps owning (see NotesRootBuilder's own class doc: this is the "re-point at that
    /// ONE instance" from the task brief, not a second document).
    ///
    /// SINGLE INSTANCE, ACCEPTED LIMITATION: if both panes end up with a Page tab active at once (an
    /// ordinary sequence — open a page, then «Открыть рядом» a DIFFERENT page), only the FOCUSED pane
    /// actually renders content; the other pane's content area goes empty until its own tab is reactivated.
    /// This is not new here — NotesDocumentController.ActivePage is itself a single field, so the two tabs
    /// could never show DIFFERENT content simultaneously even before Task 9 — and it is exactly what the
    /// brief's ISurfaceHost signature allows (one host, no pane parameter). WorkspaceController.SyncSurfaces's
    /// claim-the-ShareGroup-focused-pane-first rule makes the outcome predictable rather than
    /// order-of-iteration arbitrary, and (since Task 10c's fix round) means the losing pane costs nothing
    /// rather than doing a full show that is immediately overwritten.
    ///
    /// RECOMPILE GAP — CLOSED in round 4, and the round-3 description of it below was wrong in a way worth
    /// keeping on record. WorkspaceBuilder.Awake's guard branch reconstructs a FRESH PageSurfaceHost after a
    /// reload from NotesRootBuilder's (correctly recovered) DocumentController/DocumentView, so this class and
    /// the document MODEL it wraps were already sound. DocumentPageView's OWN `root`/`content`/`viewportGO`/
    /// `placeholderGO` were not — the same class of plain, non-serialized field this whole arc keeps finding.
    /// Round 3 characterised the consequence as "the VIEW may fail to reparent/redisplay", i.e. a page failing
    /// to APPEAR, and deferred it. That framing missed the damaging half: `root` is an OPAQUE ThemeRole.Bg
    /// Image, so a page that was VISIBLE at the moment of the reload stays visible — Hide() -> SetSurfaceVisible
    /// (false) -> OnActivePageChanged no-ops against the null `root`, so `root.SetActive(false)` never fires
    /// and nothing in the session can hide it again, and it then paints over the map camera in whatever pane
    /// it is parented in (MapSurfaceHost.SetBackgroundsEnabled disables three known Images and has no idea
    /// this one exists). Harmless before round 3 only because SyncSurfaces never ran post-reload at all; round
    /// 3 making it run is what exposed it. DocumentPageView.EnsureWired now recovers those fields (and the
    /// OnActivePageChanged subscription, which no serialization scheme could have restored) — see its own doc
    /// and NotesRootBuilder.EnsureBuilt's, which calls it.</summary>
    public class PageSurfaceHost : ISurfaceHost
    {
        readonly NotesDocumentController documentController;
        readonly DocumentPageView pageView;

        public PageSurfaceHost(NotesDocumentController documentController, DocumentPageView pageView)
        {
            this.documentController = documentController;
            this.pageView = pageView;
        }

        public SurfaceKind Kind => SurfaceKind.Page;

        public void Show(RectTransform paneContent, string id)
        {
            if (pageView == null || paneContent == null) return;

            var root = pageView.Root;
            if (root != null)
            {
                root.SetParent(paneContent, false);
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
            }

            // Opens the visibility gate FIRST (see DocumentPageView.SetSurfaceVisible) — it re-evaluates
            // against whatever ActivePage already is, which is what keeps a re-show of an UNCHANGED page
            // visible: OpenPage below no-ops (fires no event) whenever `id` is already ActivePage, e.g.
            // re-showing a page that was Hidden and is now Shown again with nothing else different.
            pageView.SetSurfaceVisible(true);
            documentController?.OpenPage(id);
        }

        public void Hide()
        {
            // Also closes the gate that would otherwise let a Page opened OUTSIDE the workspace (POI editor
            // «Открыть страницу» calls NotesDocumentController.OpenPage directly) pop `root` back on in
            // whichever pane it is still parented in — see DocumentPageView.surfaceVisible's own doc.
            pageView?.SetSurfaceVisible(false);
        }

        /// <summary>Its own group: there is one Page host, so nothing else can be sharing its object. (Two
        /// Page TABS still contend for it — that is the single-instance limitation above — but they contend
        /// through the same host, which the ShareGroup rule handles by identity.)</summary>
        public object ShareGroup => this;

        public string TitleFor(string id)
        {
            if (documentController?.Document?.Groups == null) return "";
            foreach (var group in documentController.Document.Groups)
                foreach (var page in group.Pages)
                    if (page.Id == id) return page.Name;
            return "";
        }
    }

    /// <summary>Hosts the WorldMap surface: the scene's one map camera plus its existing floating chrome
    /// (POI edit panel, legend, toolbar strip + its own docked tool panels) — all come along AS THEY ARE,
    /// per the brief; redesigning them is Р5's job entirely, not this task's.
    ///
    /// A MonoBehaviour (unlike PageSurfaceHost) because the camera's viewport rect must track the pane's
    /// LIVE on-screen rect continuously, not just once at the moment Show() runs — see
    /// ApplyViewportForRender, the Canvas.willRenderCanvases handler that does this every frame.
    ///
    /// PANE-CONFINED CHROME: "as they are" covers their CONTENT, not their extent. Every one of those panels
    /// is its own root ScreenSpaceOverlay canvas anchored to the WINDOW, so once the map became one tab among
    /// many they kept laying themselves out against the whole display: the navigator column swallowed the four
    /// 216px docked panels whole and the tab strip covered the toolbar, and whatever crossed the divider was
    /// painted over by the OTHER pane's still-enabled background at sortingOrder 70 — which reads as a
    /// truncated panel rather than a misplaced one, and is exactly what the user reported («при открытии
    /// отдельно элементы вкладки "Карта мира" не подстраиваются под новые габариты вкладки и поэтому
    /// обрезаются»). PaneChromeFrame fixes it without redesigning a single panel: a stretched frame is
    /// inserted between each canvas and its children, and ApplyViewport drives that frame's offsets from the
    /// SAME pane corners it derives the camera's viewport from. See EnsureFrames for which canvases are in the
    /// list and why, and PaneChromeFrame's own doc for why a frame rather than a reparent.
    ///
    /// mapCamera/chrome are resolved by FindFirstObjectByType when Create's caller passes null/empty,
    /// mirroring the override-or-discover pattern already used elsewhere in this codebase (e.g.
    /// PoiManager.cameraController, DungeonManager.poiManager): WorkspaceBuilder is not wired into the
    /// scene yet (Task 11 does that), so nothing can drag a reference onto it through the Inspector before
    /// then — discovery is what lets this host find the SAME camera/panels MapScreenController already
    /// owns without any scene edit. WorkspaceBuilder still exposes override fields for Task 11 (or a
    /// manual test) to pin down explicitly if discovery ever picks the wrong instance.
    ///
    /// THE KNOWN SEAM IS CLOSED (Task 10c). Through Task 10b this doc recorded that MapScreenController /
    /// ScreenSwitcher independently drove mapEditorPanelGO/mapLegendUiGO's active state via an
    /// `AppScreen.MapEditor` that no longer exists, so the two mechanisms could disagree — concretely, closing
    /// the POI editor re-asserted the map screen and re-activated this chrome behind a Hide() this host had
    /// already issued. Task 10c narrowed AppScreen to Generation/Progress/Workspace and removed those
    /// GameObjects from the switcher's member table entirely (MapScreenController.EnsureSwitcher), so THIS
    /// host is now the only thing that activates or deactivates them. The one remaining mechanism above it is
    /// deliberate and does not fight: while AppScreen is Generation or Progress, WorkspaceController
    /// .SetShellActive suppresses SyncSurfaces, which Hides this host — a strict override, not a second
    /// opinion.
    ///
    /// RECOMPILE GAP — PARTIALLY CLOSED, not left for Task 11: a domain reload (Play-mode script recompile)
    /// resets every plain, non-`[SerializeField]` field on every surviving MonoBehaviour, including
    /// `mapCamera`/`chrome`/`toolbar`/`rootRowBackground`/`shownIn`/`visible` here — while the Unity objects
    /// they used to point at (the Camera, the chrome panels, RootRow's Image, whichever pane's ContentArea)
    /// persist as native, live state completely unaware anything reset. WorkspaceBuilder.Awake's own
    /// recompile guard (`if (transform.childCount > 0) return;`) always stopped a reload from
    /// AddComponent-ing a SECOND MapSurfaceHost, so this component itself always survived — but through this
    /// task's first two review rounds, the guard branch did nothing ELSE, so `WorkspaceController.
    /// surfaceRegistry` (itself a plain field, wiped the same way) stayed null forever after a reload:
    /// `SyncSurfaces` early-returns on a null registry, so NO tab switch/close/promotion showed or hid
    /// anything for the rest of that session — a live-but-blind component, not merely one showing a stale
    /// rect. The guard branch now calls this method (via `GetComponent<MapSurfaceHost>` — recovering the
    /// EXISTING component, never `Create`-ing a second one) and re-registers a freshly-built SurfaceRegistry
    /// with `WorkspaceController.SetSurfaceRegistry`, which is what makes `SyncSurfaces` (and therefore
    /// `Show`/`Hide`, and therefore `rootRowBackground`'s own lazy re-acquisition in
    /// ResolveRootRowBackground) reachable again — see WorkspaceBuilder.Awake's own comment for exactly which
    /// half of "rebuild vs. re-wire" this is.
    ///
    /// ROUND 4 finished that job for the SURFACES specifically: the guard branch now also calls
    /// WorkspaceController.EnsureLayout (without which SyncSurfaces threw an NRE on Layout.FocusedPane inside
    /// this very recovery) and EnsurePaneHandles (without which PaneContent returned null forever, so the
    /// recovered SyncSurfaces showed nothing and Hid every host — a blank workspace). The one SyncSurfaces
    /// call at the end of that branch is therefore now authoritative: the map re-shows in the pane the
    /// recovered (default) Layout says owns it, at the correct rect, with no staleness window.
    ///
    /// STILL OPEN, still Task 11's: the recovered Layout is a fresh WorkspaceOps.NewDefault(), so the user's
    /// actual tabs/split/focus are still discarded by a reload (only WorkspacePrefs can fix that), and the
    /// CHROME around the surface — tab strips, navigator, «+», Ctrl+K, divider drag — stays inert afterwards,
    /// deliberately not partially revived here (see WorkspaceBuilder.Awake's comment for why). So there is no
    /// second interaction that could re-sync anything: post-reload the surface is correct, and nothing the
    /// user clicks changes it until they leave and re-enter Play Mode.</summary>
    public class MapSurfaceHost : MonoBehaviour, ISurfaceHost
    {
        Camera mapCamera;
        GameObject[] chrome;
        MapToolbarUI toolbar;

        /// <summary>The chrome roots whose canvases have not been framed YET, and the frames already built —
        /// see EnsureFrames for why resolution has to be lazy rather than finished inside Rewire.
        ///
        /// NOT `readonly`, and re-assigned in Rewire rather than trusted to these initializers: a Play-mode
        /// domain reload restores a MonoBehaviour by DESERIALIZING it, not by constructing it, so field
        /// initializers do not run a second time and every non-serialized reference field comes back NULL —
        /// the same trap the class doc's RECOMPILE GAP paragraph documents for mapCamera/chrome/toolbar, and
        /// the one this project has now been bitten by four times. The __PaneFrame GameObjects themselves
        /// survive that reload intact, so Rewire's fresh empty lists lose nothing: EnsureFrames RE-FINDS each
        /// surviving frame by name (PaneChromeFrame.Ensure is idempotent) instead of building a second
        /// one — "re-point the references, never rebuild", exactly as everywhere else here.</summary>
        List<GameObject> pendingFrameRoots = new List<GameObject>();
        List<RectTransform> frames = new List<RectTransform>();

        /// <summary>Scratch buffer for GetComponentsInChildren inside EnsureFrames — reused rather than
        /// re-allocated because EnsureFrames runs from ApplyViewport, i.e. once per rendered frame for as long
        /// as any chrome root is still unresolved. Same non-readonly/re-assigned-in-Rewire rule as above.</summary>
        List<Canvas> canvasScratch = new List<Canvas>();

        /// <summary>The full-bleed background WorkspaceBuilder paints behind NavigatorColumn/PaneContainer
        /// (RootRow's own Image) — see SetBackgroundsEnabled's own doc for why THIS one, specifically, needs
        /// a thread-through reference rather than being reached via paneContent.parent the way the pane- and
        /// content-level backgrounds are. Read ONLY through ResolveRootRowBackground below, never directly —
        /// see that method's own doc and the class doc's RECOMPILE GAP paragraph for why a direct read would
        /// be unrecoverable after a domain reload wipes this field mid-session.</summary>
        Image rootRowBackground;

        RectTransform shownIn;
        bool visible;

        /// <summary>REUSE-OR-ADD, not plain AddComponent: WorkspaceBuilder.Awake re-runs this whole method on
        /// every Play-mode shell rebuild (Task 11 Step 5), and a second AddComponent would leave two live
        /// hosts arguing over one camera's viewport rect and one set of background Images. Reusing is also
        /// strictly better than destroy-then-add — a destroyed host cannot turn OFF the chrome it was holding
        /// on, and Destroy is deferred to end of frame — which is the whole reason Rewire below exists and is
        /// called from here rather than being a second, parallel assignment path.</summary>
        public static MapSurfaceHost Create(GameObject owner, Camera cameraOverride, GameObject[] chromeOverride,
            Image rootRowBackground)
        {
            // Explicit null check, never `??`: Unity's lifetime-aware `==` is what distinguishes a destroyed
            // component from a live one, and `??` bypasses the overload.
            var existing = owner.GetComponent<MapSurfaceHost>();
            var host = existing != null ? existing : owner.AddComponent<MapSurfaceHost>();
            host.Rewire(cameraOverride, chromeOverride, rootRowBackground);
            return host;
        }

        /// <summary>Re-runs Create's own discovery/assignment logic against THIS existing component, without
        /// AddComponent-ing a new one — the "re-point the references, don't rebuild" half of
        /// WorkspaceBuilder.Awake's recompile-guard branch. `mapCamera`/`chrome`/`toolbar` are plain private
        /// fields with no `[SerializeField]`, exactly the class of field the RECOMPILE GAP paragraph
        /// documents, so a Play-mode script reload wipes all three even though this MonoBehaviour ITSELF (and
        /// the camera/panels it used to point at) survive as live, findable objects — calling this again is
        /// what actually recovers them, rather than leaving a live-but-blind component behind that
        /// `WorkspaceController.SyncSurfaces` would otherwise call `Show`/`Hide` on for no visible effect.
        /// `rootRowBackground` is deliberately allowed to stay null here (the caller has no local reference to
        /// pass post-reload — see WorkspaceBuilder.Awake's guard branch) — ResolveRootRowBackground's own
        /// hierarchy-path fallback re-acquires it lazily the first time SetBackgroundsEnabled actually needs
        /// it, so there is nothing extra to do for that one specifically.</summary>
        public void Rewire(Camera cameraOverride, GameObject[] chromeOverride, Image rootRowBackground)
        {
            this.rootRowBackground = rootRowBackground;

            mapCamera = cameraOverride != null
                ? cameraOverride
                : FindFirstObjectByType<WorldMapRenderer>()?.targetCamera;

            if (chromeOverride != null && chromeOverride.Length > 0)
            {
                chrome = chromeOverride;
            }
            else
            {
                var discovered = new List<GameObject>();
                var poiPanel = FindFirstObjectByType<PoiEditPanel>();
                var legend = FindFirstObjectByType<MapLegendUI>();
                if (poiPanel != null) discovered.Add(poiPanel.gameObject);
                if (legend != null) discovered.Add(legend.gameObject);
                chrome = discovered.ToArray();
            }

            toolbar = FindFirstObjectByType<MapToolbarUI>();

            // The frame roots are a STRICT SUPERSET of `chrome`, and deliberately a separate list rather than
            // an enlarged `chrome`: `chrome` drives SetActive (see SetChromeActive), and the four docked
            // panels must NOT be driven that way — MapToolbarUI.SetActiveTab owns their activation and its own
            // comment (MapToolbarUI.cs:281) documents why the deactivate-all-then-activate-target ORDER is
            // load-bearing (EditorBrushPanel/RegionsPanel share mutable BrushToolController state through
            // OnEnable/OnDisable). Framing is a purely geometric concern with no such coupling, so the two
            // lists have different memberships on purpose. The four panels are reachable ONLY through the
            // toolbar (they are its siblings, referenced by nothing else), which is why MapToolbarUI exposes
            // DockedPanels instead of this class re-discovering four more types by FindFirstObjectByType.
            //
            // PoiInfoPopup is deliberately absent, as are the two region-label overlays: all three place
            // themselves with cam.WorldToScreenPoint (PoiInfoPopup.cs:142, RegionLabelOverlay.cs:266,
            // PoliticalRegionLabelOverlay.cs:118), which ALREADY accounts for the camera's viewport rect that
            // ApplyViewport sets — insetting them a second time would move them off their own map features.
            //
            // Fresh instances, not .Clear(): after a domain reload all three fields are null (see their own
            // doc), so there is nothing to clear — and on the ordinary path a fresh list is identical to a
            // cleared one, which keeps this single line correct on BOTH paths with no branch to get wrong.
            //
            // Dropping the old `frames` without Reset()ing its entries first is safe ONLY because the root
            // set cannot SHRINK between two Rewire calls: the only two callers are Create (once per
            // component) and WorkspaceBuilder.Awake's reload branch (WorkspaceBuilder.cs:217), and both pass
            // the same `mapChrome` field, so discovery yields the same set both times. A future caller that
            // narrowed the set would strand a __PaneFrame at its last-applied inset with nothing left
            // holding a reference to reset it — Reset the outgoing frames here if that ever becomes possible.
            pendingFrameRoots = new List<GameObject>();
            frames = new List<RectTransform>();
            canvasScratch = new List<Canvas>();

            if (chrome != null)
                foreach (var go in chrome)
                    if (go != null) pendingFrameRoots.Add(go);
            if (toolbar != null)
            {
                pendingFrameRoots.Add(toolbar.gameObject);
                foreach (var panel in toolbar.DockedPanels)
                    if (panel != null) pendingFrameRoots.Add(panel);
            }
        }

        /// <summary>Turns whatever chrome roots have become resolvable into PaneChromeFrames, and is called
        /// EVERY rendered frame (from ApplyViewport) rather than once from Rewire — because at Rewire time
        /// most of these canvases do not exist yet, and three of them may not exist for the rest of the
        /// session.
        ///
        /// Every one of these panels builds its canvas in its OWN Awake (MapLayersPanel.cs:36,
        /// EditorBrushPanel.cs:70, PoiToolPanel.cs:44, RegionsPanel.cs:47, MapLegendUI.cs:42,
        /// PoiEditPanel.cs:63), and Unity's Awake order between sibling scene objects is undefined — so
        /// WorkspaceBuilder.Awake -> Create -> Rewire can easily run BEFORE any of them. Worse, and this is
        /// the case that makes a one-shot resolution unfixable rather than merely racy: MapToolbarUI.Awake
        /// ends in SetActiveTab(0), which SetActive(false)s the three non-selected docked panels. A
        /// GameObject deactivated before its own Awake has run never runs it — so those three have NO canvas
        /// at all until the user first clicks their tab, which may be minutes later or never.
        ///
        /// Hence: skip an inactive root entirely (it costs one bool check, allocates nothing, and an invisible
        /// panel needs no frame), and re-scan an ACTIVE one until it actually yields a canvas. SetActive(true)
        /// runs Awake synchronously inside the click handler's Update, and this method runs from
        /// Canvas.willRenderCanvases — after every Update/LateUpdate, before anything renders — so a panel is
        /// framed on the very frame it appears, with no window-anchored flash.
        ///
        /// `isRootCanvas` is the filter, not "the first Canvas found": EditorBrushPanel's dropdown Template is
        /// a NESTED overrideSorting canvas parented under the dropdown itself (EditorBrushPanel.cs:906), and
        /// nested canvases must be left alone — they are already inside the frame via their parent and
        /// framing them again would inset them twice.</summary>
        void EnsureFrames()
        {
            if (pendingFrameRoots == null || frames == null || canvasScratch == null) return;

            // Prune frames whose canvas was destroyed (scene teardown, or a chrome GameObject someone else
            // Destroy()s). Apply/Reset null-guard each entry anyway, so this is hygiene rather than a fix —
            // without it the list only ever grows, and a fake-null entry is indistinguishable from a live one
            // at a glance in the debugger. n <= 7, downward so RemoveAt does not skip.
            //
            // ONE-WAY DOOR, accepted, and the sibling of the shrinking-root-set trap marked in Rewire above:
            // a root is dropped from pendingFrameRoots the moment it yields a frame, so a root whose frame is
            // pruned here is never re-framed until the next Rewire. Unreachable in normal use — destroying a
            // __PaneFrame destroys the chrome children it holds, and destroying the canvas destroys the frame,
            // so there is no state where a live canvas outlives its pruned frame.
            for (int i = frames.Count - 1; i >= 0; i--)
                if (frames[i] == null) frames.RemoveAt(i);

            // Downward iteration so RemoveAt does not skip the next entry.
            for (int i = pendingFrameRoots.Count - 1; i >= 0; i--)
            {
                var root = pendingFrameRoots[i];
                if (root == null) { pendingFrameRoots.RemoveAt(i); continue; }
                if (!root.activeInHierarchy) continue;

                root.GetComponentsInChildren(true, canvasScratch);
                bool found = false;
                foreach (var canvas in canvasScratch)
                {
                    if (canvas == null || !canvas.isRootCanvas) continue;
                    var frame = PaneChromeFrame.Ensure(canvas.transform);
                    if (frame == null) continue;
                    found = true;
                    // Contains-before-Add, not a bare Add: `frames` must hold each frame exactly once, and
                    // Ensure is idempotent by design, so re-reaching an already-framed canvas (a repeat
                    // Rewire, or a scratch buffer that turned out not to be cleared for us) has to be a
                    // no-op rather than a duplicate entry that Apply/Reset would then write twice. n <= 7,
                    // so the linear scan is cheaper than the HashSet it would otherwise take.
                    if (!frames.Contains(frame)) frames.Add(frame);
                }
                // Only retire the root once it has actually produced a frame — an active GameObject whose
                // Awake has not run yet reports zero canvases and must be retried next frame.
                if (found) pendingFrameRoots.RemoveAt(i);
            }
        }

        public SurfaceKind Kind => SurfaceKind.WorldMap;

        /// <summary>Re-parenting a CAMERA is not "set a Transform.parent" the way a uGUI surface would —
        /// there is nothing to move. What actually has to happen instead: the pane's own opaque backgrounds
        /// (which sit in the SAME ScreenSpaceOverlay canvas that composites on top of every camera,
        /// unconditionally, regardless of any UI-internal sibling order) have to stop covering the exact
        /// screen rect the camera now renders into — see SetBackgroundsEnabled's own doc for exactly which
        /// three Images that is and why each one is safe to disable. If `paneContent` differs from whatever
        /// this host was PREVIOUSLY shown in (a pane promotion, or the other pane winning the single-host
        /// tie-break), the OLD container's backgrounds are restored FIRST — otherwise a container that stops
        /// hosting the camera would be left with a permanently punched hole the next time something else
        /// (a Page tab, a future Task-10 surface) tries to render there.</summary>
        public void Show(RectTransform paneContent, string id)
        {
            if (paneContent == null) return;

            if (shownIn != null && shownIn != paneContent) SetBackgroundsEnabled(shownIn, true);

            shownIn = paneContent;
            visible = true;
            SetChromeActive(true);
            SetBackgroundsEnabled(shownIn, false);

            // Subscribes to Canvas.willRenderCanvases — Unity's own static event fired once per frame, AFTER
            // the CanvasUpdateRegistry has finished that frame's deferred layout rebuild but BEFORE anything
            // actually renders (see ApplyViewportForRender's own doc for why THAT ordering, specifically,
            // matters and LateUpdate's did not). `-=` before `+=` makes this idempotent against repeat Show()
            // calls that never went through Hide() in between (an ordinary SyncSurfaces re-sync where nothing
            // actually changed) — removing an unsubscribed handler is a silent no-op in C#, so this always
            // ends at exactly one subscription, never a growing stack of duplicate ones.
            Canvas.willRenderCanvases -= ApplyViewportForRender;
            Canvas.willRenderCanvases += ApplyViewportForRender;

            // Belt-and-suspenders for the FIRST frame specifically: forces the deferred layout pass to run
            // NOW (rather than waiting for this same frame's willRenderCanvases, which the subscription above
            // already covers) so ApplyViewport's read immediately below is correct even before that event
            // fires — matters here because Show() can run from WorkspaceController.SetSurfaceRegistry, itself
            // called from WorkspaceBuilder.Awake, i.e. before uGUI has ever laid out this hierarchy at all.
            // Same idiom GenerationScreenUI/GenerationProgressUI already use for the same reason.
            Canvas.ForceUpdateCanvases();
            ApplyViewport();
        }

        public void Hide()
        {
            visible = false;
            Canvas.willRenderCanvases -= ApplyViewportForRender;
            // Restore BEFORE clearing shownIn — SetBackgroundsEnabled needs the container reference to know
            // which pane's/content's Images to re-enable. Runs even when shownIn is already null (nothing to
            // restore, the ternary/null-guards inside just no-op) so Hide stays safe to call unconditionally.
            if (shownIn != null) SetBackgroundsEnabled(shownIn, true);
            shownIn = null;
            SetChromeActive(false);
            // Give every frame the whole canvas back MINUS the menu-bar strip (see PaneChromeFrame.Reset's own
            // doc for why not plain zero — the map chrome's top offsets no longer include that strip, so a
            // fully-zeroed frame would put the toolbar underneath ProjectMenuBar), which restores the chrome
            // to pixel-identical pre-Task-10a geometry while this surface owns no pane. Left clamped instead,
            // a panel would render inside a rect belonging to a pane that no longer shows the map. Task 10c
            // removed the legacy path that used to re-show these panels behind this host's back (see the class
            // doc's now-closed KNOWN SEAM paragraph), so this is no longer defending against a second owner —
            // it is defending against this host's OWN next Show landing in a different pane, and against the
            // un-hosted transitional state before Task 11 wires the shell into the scene. Show() -> ApplyViewport re-clamps on the very frame the map comes back, so this costs
            // nothing but a Vector2 write per frame object. Null-guarded because Hide is documented as safe to
            // call unconditionally, and `frames` is null on any path that reaches Hide before Rewire.
            if (frames != null)
                for (int i = 0; i < frames.Count; i++) PaneChromeFrame.Reset(frames[i]);
            // The camera stays ENABLED, and only its viewport goes back to full-screen. Hiding a camera is
            // not "Camera.enabled = false" here, because this is the scene's ONLY camera: disabling it left
            // Unity with nothing rendering Display 1 at all — the literal "Display 1 — No cameras rendering"
            // message, and undefined pixels anywhere a uGUI graphic did not happen to cover, since nothing
            // was performing the frame's clear. Selecting any Page tab reproduced it.
            //
            // Painting over it is what "hide" means for a camera in this app, and the mechanism is already
            // right here: the line above restores the three opaque backgrounds Show() disabled, the outermost
            // of which (RootRow's) is full-bleed — a strict superset of any viewport this host ever sets. So
            // the camera renders and is then completely covered, which is exactly the arrangement the app ran
            // on before the workspace shell existed: NotesLayoutController clamped this same camera's rect to
            // the docked split and painted the notes panel over the rest, and NEVER disabled it. Nothing else
            // in the project reads or writes Camera.enabled (verified by grep — MapScreenController /
            // ScreenSwitcher only toggle chrome GameObjects), so no Task-10-scoped coupling depends on the
            // old behaviour either.
            //
            // The cost is a full-screen clear plus the map draw every frame while hidden. That is the cost
            // the app already paid for its entire life before Task 9 — an orthographic camera over a static
            // map mesh — so this is the status quo restored, not a new per-frame expense. Restoring the FULL
            // rect rather than leaving the last pane's clamp is what makes the guarantee unconditional: the
            // whole display is cleared by a camera every frame no matter which UI happens to be enabled, so
            // "undefined pixels" stops being a possible state rather than merely an unlikely one. Show() ->
            // ApplyViewport re-derives the correct pane rect immediately, so nothing goes stale.
            if (mapCamera != null) mapCamera.rect = FullViewport;
        }

        /// <summary>The whole-display viewport a camera has by default, restored by Hide — see its comment.</summary>
        static readonly Rect FullViewport = new Rect(0f, 0f, 1f, 1f);

        /// <summary>Unsubscribes from the static Canvas.willRenderCanvases event if this component is
        /// destroyed while still shown (scene teardown, an out-of-band Destroy) — without this, a static
        /// event holding a delegate onto a destroyed Unity object throws a "MissingReferenceException" the
        /// next time it fires, or at minimum leaks the subscription for the rest of the process lifetime.
        /// The same class of cleanup DocumentPageView.OnDestroy already does for its own OnActivePageChanged
        /// subscription.</summary>
        void OnDestroy() => Canvas.willRenderCanvases -= ApplyViewportForRender;

        /// <summary>Punches (or restores) the hole a camera-backed surface needs: THREE opaque Images sit in
        /// the SAME ScreenSpaceOverlay canvas the camera composites underneath, all added unconditionally by
        /// WorkspaceBuilder with no awareness a camera might occupy their area — RootRow's own full-bleed
        /// background, `container`'s parent pane's own background (WorkspaceBuilder.BuildPane's `img`), and
        /// `container`'s own background (BuildPane's `contentImg`, the exact rect the camera renders into).
        /// ScreenSpaceOverlay composites ON TOP OF every camera unconditionally — UI-internal sibling order
        /// (which of these three would visually "win" against ANOTHER UI element) is irrelevant to whether
        /// any ONE of them blocks the camera; each independently paints over it wherever its own rect
        /// overlaps, so all three must be disabled together, not just the smallest one.
        ///
        /// Disabling the PANE-level and RootRow backgrounds this broadly (not just container's own rect) is
        /// safe rather than a wider hole than intended: TabStripView carries its OWN complete opaque
        /// background (`ThemeRole.Panel`) independent of the pane root's, so the tab-strip portion of the
        /// pane stays covered when the pane root's background goes transparent; NavigatorColumn likewise
        /// carries its own opaque `Panel` background independent of RootRow's. RootRow's own background is
        /// otherwise a pure safety net for TRANSIENT layout gaps (its own doc comment: "any gap left by a
        /// shrunk/hidden child") — with `HorizontalLayoutGroup.childControlWidth=true` and zero spacing on
        /// both RootRow and PaneContainer, every ACTIVE child tiles its neighbours exactly in steady state, so
        /// nothing here depends on RootRow's background remaining opaque while it is disabled.
        ///
        /// `container.GetComponent<Image>()`/`container.parent.GetComponent<Image>()` are looked up fresh
        /// rather than cached: `container` (a pane's fixed physical ContentArea) never changes IDENTITY across
        /// Show() calls — only which LOGICAL pane it represents does — so this is cheap and needs no extra
        /// state to invalidate. `rootRowBackground` goes through ResolveRootRowBackground rather than the
        /// field directly, for the same reason.</summary>
        void SetBackgroundsEnabled(RectTransform container, bool enabled)
        {
            var contentImg = container.GetComponent<Image>();
            if (contentImg != null) contentImg.enabled = enabled;

            var paneImg = container.parent != null ? container.parent.GetComponent<Image>() : null;
            if (paneImg != null) paneImg.enabled = enabled;

            var rootBg = ResolveRootRowBackground();
            if (rootBg != null) rootBg.enabled = enabled;
        }

        /// <summary>Returns `rootRowBackground`, re-acquiring it by hierarchy path first if the
        /// constructor-injected reference was lost — see the class doc's RECOMPILE GAP paragraph for why a
        /// domain reload can wipe this specific field while leaving its Image's `.enabled` state (and every
        /// other field this class holds) exactly as it was. "WorkspaceCanvas/RootRow" is the exact relative
        /// path WorkspaceBuilder.Awake builds it at: MapSurfaceHost is AddComponent-ed directly onto
        /// WorkspaceBuilder's own GameObject (see Create's `owner` parameter), so `transform` here already IS
        /// WorkspaceBuilder's transform — no separate "find WorkspaceBuilder first" step is needed. The
        /// re-acquired reference is cached back into the field so this Find only ever runs once per loss, not
        /// on every SetBackgroundsEnabled call.</summary>
        Image ResolveRootRowBackground()
        {
            if (rootRowBackground != null) return rootRowBackground;
            var rootRow = transform.Find("WorkspaceCanvas/RootRow");
            rootRowBackground = rootRow != null ? rootRow.GetComponent<Image>() : null;
            return rootRowBackground;
        }

        /// <summary>The Canvas.willRenderCanvases handler — re-derives the camera's viewport rect from the
        /// pane's on-screen rect every frame while shown, rather than converting once inside Show(). A
        /// one-shot conversion goes stale three ways: (1) the very first sync runs from
        /// WorkspaceController.SetSurfaceRegistry, called from WorkspaceBuilder.Awake — before uGUI's first
        /// layout pass, so paneContent's world corners are not yet meaningful (Show()'s own
        /// Canvas.ForceUpdateCanvases call covers this one specifically); (2) WorkspaceController.
        /// SetSplitRatioLive (the divider-drag path) deliberately does NOT raise OnLayoutChanged (see its own
        /// doc comment), so dragging the divider moves the pane without ever calling Show() again; (3) an
        /// ordinary window resize moves every pane's screen rect with no workspace event at all.
        ///
        /// A PREVIOUS version of this method ran from LateUpdate instead, reading shownIn.GetWorldCorners()
        /// EVERY frame — but uGUI's own deferred layout rebuild runs at willRenderCanvases, AFTER every
        /// script's LateUpdate, so during an active divider drag (SetSplitRatioLive changes flexibleWidth
        /// synchronously, but the resulting RESIZE is deferred) LateUpdate was reading STALE, pre-rebuild
        /// corners — one whole frame behind whatever the neighbour pane's still-enabled background had
        /// already shrunk away from, leaving a thin gap covered by neither the old background (shrunk) nor
        /// the camera (rect not yet caught up): the exact "camera rect clamped, nothing clears the rest"
        /// artifact SetBackgroundsEnabled's own backgrounds exist to prevent, reintroduced for the DURATION
        /// of a drag. Subscribing to willRenderCanvases instead — which fires AFTER that same frame's layout
        /// rebuild has already applied — reads corners at the one point in the frame where they are
        /// guaranteed current, closing the gap at its ROOT CAUSE rather than working around it by forcing an
        /// extra rebuild every single frame (Canvas.ForceUpdateCanvases() in LateUpdate would also have
        /// fixed it, at the cost of paying for a full forced rebuild on every frame the map is visible,
        /// whether or not a drag or resize actually happened that frame; this event-based approach pays
        /// nothing beyond an existing Unity-internal per-frame event Unity already dispatches regardless).</summary>
        void ApplyViewportForRender()
        {
            if (!visible || shownIn == null || mapCamera == null) return;
            ApplyViewport();
        }

        void ApplyViewport()
        {
            // Guards the SAME null case ApplyViewportForRender already guards before calling this — needed
            // here too because Show() calls this directly (to avoid a one-frame stale/zero viewport — see its
            // own comment), on a path ApplyViewportForRender's guard does not cover. Discovery finding no
            // camera at all (Create's own null-tolerance — see the class doc) must not throw on Show().
            if (mapCamera == null) return;

            var corners = new Vector3[4];
            shownIn.GetWorldCorners(corners);   // ScreenSpaceOverlay: world position IS screen-pixel position.

            float screenW = Mathf.Max(1f, Screen.width);
            float screenH = Mathf.Max(1f, Screen.height);
            float xMin = corners[0].x / screenW;
            float yMin = corners[0].y / screenH;
            float xMax = corners[2].x / screenW;
            float yMax = corners[2].y / screenH;

            // Hide() no longer disables the camera (see its comment), so this is no longer the counterpart to
            // anything this class does — it is kept purely as belt against some OTHER path having disabled the
            // scene's only camera, which would otherwise leave a Show() with a correct rect and no render.
            mapCamera.enabled = true;
            mapCamera.rect = new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));

            // The chrome rides the SAME corners the viewport was just derived from — one GetWorldCorners read,
            // two consumers. Driving the frames from here rather than from Show() is what makes divider-drag
            // and window-resize free: both move the pane without ever calling Show() again (see this method's
            // caller ApplyViewportForRender for the full list of ways a one-shot conversion goes stale), and
            // both already had to be handled for the camera. Anything that keeps the camera's rect honest now
            // keeps the panels' rect honest by construction, so the two can no longer drift apart.
            EnsureFrames();
            if (frames != null)
                for (int i = 0; i < frames.Count; i++) PaneChromeFrame.Apply(frames[i], corners);
        }

        void SetChromeActive(bool active)
        {
            if (chrome != null)
                foreach (var go in chrome)
                    if (go != null) go.SetActive(active);
            if (toolbar != null) toolbar.SetChromeVisible(active);
        }

        public string TitleFor(string id) => WorkspaceOps.DefaultWorldMapTitle;

        /// <summary>Its own group — one camera, one host, nothing shares it.</summary>
        public object ShareGroup => this;
    }

    /// <summary>Hosts the five surfaces that used to be full-screen SCREENS: PoiEditor, Settlement,
    /// BuildingInterior, Dungeon and BattleGrid (Task 10c Step 3). Each wraps the screen GameObject it
    /// already had — PoiEditorScreen, DungeonEditorScreen, BattleGridScreen — exactly as they are; their
    /// insides are Р5's to redesign, this only decides WHERE they draw and WHEN they are on.
    ///
    /// ONE MonoBehaviour FOR ALL THREE SCREENS, not one per screen, and this is the load-bearing shape:
    /// Settlement, BuildingInterior and Dungeon are three SurfaceKinds served by the SAME GameObject
    /// (DungeonEditorScreen binds an InteriorData whose Kind decides what it draws — see
    /// MapScreenController.OpenDungeonEditor/OpenBuildingInterior). WorkspaceController.SyncSurfaces shows
    /// every wanted host FIRST and then hides every unwanted one (WorkspaceController.cs:492-513), so three
    /// independent hosts each SetActive-ing that one GameObject would break as follows: with a Settlement tab
    /// active, the settlement host's Show() turns the screen on, and then the Dungeon and BuildingInterior
    /// hosts' Hide() — which no pane wants — turn it straight back off. The settlement would go blank on
    /// EVERY sync. A shared SLOT with an explicit owner (see Slot.Owner) is what makes Hide(kind) a no-op for
    /// a kind that does not currently own the screen, so the three cannot fight over it.
    ///
    /// A MonoBehaviour for the same reason MapSurfaceHost is one: these are window-anchored root canvases at
    /// sortingOrder 100-102, so confining them to a pane means driving PaneChromeFrame from the pane's LIVE
    /// on-screen corners every frame (divider drag and window resize both move a pane without any workspace
    /// event — see MapSurfaceHost.ApplyViewportForRender's own doc for the full list). A host that skipped
    /// PaneChromeFrame would reproduce, for all five surfaces at once, exactly the clipping the user reported
    /// for the map («элементы вкладки "Карта мира" не подстраиваются под новые габариты вкладки»).
    ///
    /// NO SetBackgroundsEnabled COUNTERPART, unlike MapSurfaceHost, and deliberately: that method exists
    /// because a CAMERA composites UNDER every ScreenSpaceOverlay canvas, so the pane's own opaque Images had
    /// to be switched off to reveal it. These five are themselves ScreenSpaceOverlay canvases sorting at
    /// 100-102, i.e. ABOVE the shell's 70, so they already paint over the pane's background with nothing to
    /// disable. Leaving those backgrounds ON is also what keeps the pane opaque in the strip the RectMask2D
    /// cuts away.
    ///
    /// SORTING LEFT ALONE, answering the question WorkspaceBuilder.cs's canvas-order comment parked for this
    /// task ("Task 10 revisits this"). They must stay ABOVE the shell's 70 or the pane's own ContentArea
    /// background would cover them; DungeonEditorScreen (101) and BattleGridScreen (102) also sort above
    /// ProjectMenuBar (100), which no longer matters now that the frame's RectMask2D clips them to the pane's
    /// ContentArea — a rect that already excludes the menu-bar strip, since RootRow is inset by
    /// WorkspaceBuilder.MenuBarInset. Re-numbering them would be a change with no visible effect and a real
    /// chance of regressing the un-hosted (Task 10c-to-11 transitional) path.
    ///
    /// SINGLE INSTANCE PER SCREEN, accepted, same limitation PageSurfaceHost's own doc records: if both panes
    /// show interior tabs at once, only the FOCUSED pane's gets the screen. SyncSurfaces enforces that by
    /// claiming ShareGroup (see GroupFor below, which returns the SAME Slot for all three interior kinds) with
    /// the focused pane first, so the losing pane's Show is skipped outright. That skip is what makes
    /// MapScreenController.RebindSurface's already-bound early-out reachable at all in a split — without it
    /// the two kinds alternate and every sync re-Binds the whole node canvas twice.
    ///
    /// THAT SINGLE INSTANCE IS ALSO WHY Show() RE-BINDS. Each of these screens holds ONE binding (the
    /// InteriorData / room / POI it was last given), and the Open* methods that set it are NOT on the
    /// tab-click path — so switching tabs would re-show a screen still bound to the previous tab's subject.
    /// See Show's own comment and MapScreenController.RebindSurface.
    ///
    /// RECOMPILE GAP: every field here is plain and non-[SerializeField], so a Play-mode domain reload wipes
    /// all of them while the screen GameObjects, their canvases and their __PaneFrames survive as live
    /// objects — this arc's recurring defect family, now on its seventh sighting (see MapSurfaceHost's own
    /// RECOMPILE GAP paragraph for the previous six). Rewire() re-runs discovery against THIS existing
    /// component and is called from WorkspaceBuilder.Awake's recompile-guard branch, exactly as
    /// MapSurfaceHost.Rewire is. Slot.HasOwner comes back FALSE after such a reload, which is why Hide()
    /// treats "nobody owns this screen" as "nobody wants it" and deactivates — otherwise a screen that was
    /// visible at the moment of the reload could never be hidden again for the rest of the session, the same
    /// unrecoverable-visible-surface trap DocumentPageView hit (PageSurfaceHost's doc, round 3).</summary>
    public class ScreenSurfaceHosts : MonoBehaviour
    {
        /// <summary>One legacy screen GameObject plus everything needed to confine and retire it. Its Frames
        /// are the __PaneFrames of the canvases UNDER Screen — plural because a screen may build more than one
        /// root canvas, and lazily because DungeonEditorScreen/BattleGridScreen only build theirs on the first
        /// frame they are ACTIVE (`void Awake() { if (isActiveAndEnabled) EnsureBuilt(); }` —
        /// DungeonEditorScreen.cs:106, BattleGridScreen.cs:70), which for a screen the switcher has already
        /// deactivated is never. Same lazy-resolution reason MapSurfaceHost.EnsureFrames documents for the
        /// docked tool panels.</summary>
        class Slot
        {
            public GameObject Screen;
            public List<GameObject> PendingFrameRoots = new List<GameObject>();
            public List<RectTransform> Frames = new List<RectTransform>();
            public RectTransform ShownIn;
            public bool Visible;

            /// <summary>Which SurfaceKind currently has this screen bound to it, and whether anyone does at
            /// all. The three interior kinds share one Slot, so Hide(kind) must only retire the screen when
            /// the RETIRING kind is the one that owns it — see the class doc's SyncSurfaces show-then-hide
            /// argument for what breaks otherwise.</summary>
            public SurfaceKind Owner;
            public bool HasOwner;
        }

        List<Slot> slots = new List<Slot>();
        Dictionary<SurfaceKind, Slot> byKind = new Dictionary<SurfaceKind, Slot>();

        /// <summary>Who re-binds a screen to the surface a tab actually names — see Show's own comment for the
        /// defect this closes. Discovered rather than injected, the same override-or-discover pattern
        /// MapSurfaceHost.Rewire uses for the map chrome, and re-discovered in Rewire so a domain reload
        /// recovers it: this is a plain field, so the reload nulls it while MapScreenController itself survives
        /// (the RECOMPILE GAP paragraph in this class's doc). Null in a scene without one, which makes Show a
        /// pure activate-and-frame exactly as it was before this hook existed.</summary>
        MapScreenController screens;

        /// <summary>Scratch buffer for GetComponentsInChildren inside EnsureFrames — reused rather than
        /// re-allocated, since EnsureFrames runs once per rendered frame for as long as any screen is still
        /// unresolved. Same non-readonly/re-assigned-in-Rewire rule as the two collections above.</summary>
        List<Canvas> canvasScratch = new List<Canvas>();

        /// <summary>REUSE-OR-ADD for the same reason MapSurfaceHost.Create is (see there), and with a sharper
        /// consequence: this component holds WHICH legacy screen is currently switched on, and those screens
        /// are full-pane canvases. A duplicate — or a destroyed original — leaves whichever screen was
        /// visible on with nothing left that can retire it.</summary>
        public static ScreenSurfaceHosts Create(GameObject owner, GameObject poiEditorOverride,
            GameObject interiorOverride, GameObject battleGridOverride)
        {
            var existing = owner.GetComponent<ScreenSurfaceHosts>();
            var hosts = existing != null ? existing : owner.AddComponent<ScreenSurfaceHosts>();
            hosts.Rewire(poiEditorOverride, interiorOverride, battleGridOverride);
            return hosts;
        }

        /// <summary>Re-runs Create's discovery/assignment against THIS existing component — the "re-point the
        /// references, don't rebuild" half of WorkspaceBuilder.Awake's recompile-guard branch, identical in
        /// role to MapSurfaceHost.Rewire (see this class's RECOMPILE GAP paragraph).
        ///
        /// FindObjectsInactive.Include is not optional here, unlike in MapSurfaceHost.Rewire's own discovery:
        /// the map chrome it looks for is active whenever a map exists, but these three screens are
        /// DEACTIVATED almost all of the time (only the one whose surface is showing is on), so the default
        /// active-only overload would find nothing on any call after the first — including, fatally, the
        /// post-reload one, which would leave whichever screen was visible at the reload permanently on with
        /// no host holding a reference to turn it off.
        ///
        /// Fresh collections rather than .Clear(): after a reload all three fields are null, so there is
        /// nothing to clear, and on the ordinary path a fresh collection is identical to a cleared one — one
        /// line that is correct on both paths with no branch to get wrong (the same argument
        /// MapSurfaceHost.Rewire makes for its own lists). The __PaneFrame GameObjects survive the reload and
        /// PaneChromeFrame.Ensure is idempotent, so EnsureFrames RE-FINDS each surviving frame by name instead
        /// of building a second one; nothing is stranded, because the screen set cannot shrink between two
        /// Rewire calls (both callers pass the same three override fields).</summary>
        public void Rewire(GameObject poiEditorOverride, GameObject interiorOverride, GameObject battleGridOverride)
        {
            slots = new List<Slot>();
            byKind = new Dictionary<SurfaceKind, Slot>();
            canvasScratch = new List<Canvas>();
            screens = FindFirstObjectByType<MapScreenController>(FindObjectsInactive.Include);

            GameObject poiScreen = poiEditorOverride != null
                ? poiEditorOverride
                : FindFirstObjectByType<PoiEditorScreen>(FindObjectsInactive.Include)?.gameObject;
            GameObject interiorScreen = interiorOverride != null
                ? interiorOverride
                : FindFirstObjectByType<DungeonEditorScreen>(FindObjectsInactive.Include)?.gameObject;
            GameObject battleScreen = battleGridOverride != null
                ? battleGridOverride
                : FindFirstObjectByType<BattleGridScreen>(FindObjectsInactive.Include)?.gameObject;

            AddSlot(poiScreen, SurfaceKind.PoiEditor);
            // THE three-kinds-one-GameObject case the class doc is about. Listed here rather than split into
            // three AddSlot calls precisely so the sharing is visible at the one place it is decided.
            AddSlot(interiorScreen, SurfaceKind.Settlement, SurfaceKind.BuildingInterior, SurfaceKind.Dungeon);
            AddSlot(battleScreen, SurfaceKind.BattleGrid);
        }

        void AddSlot(GameObject screen, params SurfaceKind[] kinds)
        {
            // A missing screen registers NO host for its kinds, rather than a host with a null screen: that is
            // what makes SurfaceRegistry.For return null and SyncSurfaces treat the kind as "nothing to show"
            // (its own documented contract), instead of a live host silently doing nothing on every call.
            if (screen == null) return;

            var slot = new Slot { Screen = screen };
            slot.PendingFrameRoots.Add(screen);
            slots.Add(slot);
            foreach (var kind in kinds) byKind[kind] = slot;
        }

        /// <summary>One ISurfaceHost per Kind this component actually resolved a screen for — handed straight
        /// to SurfaceRegistry.Register by WorkspaceBuilder. Built fresh on each read rather than cached,
        /// because it is read exactly twice per session (first build, post-reload recovery) and a cache would
        /// be one more plain field to go stale.</summary>
        public IEnumerable<ISurfaceHost> Hosts
        {
            get
            {
                foreach (var kv in byKind) yield return new ScreenSurfaceHost(kv.Key, this);
            }
        }

        /// <summary>The Slot behind `kind` — ISurfaceHost.ShareGroup's answer for these five. Returns the
        /// SAME instance for Settlement, BuildingInterior and Dungeon, which is exactly what tells
        /// WorkspaceController.SyncSurfaces that a second pane asking for a different one of those three is
        /// asking for a screen the focused pane already took. Null for an unregistered kind; the caller falls
        /// back to the host itself, so an unshared host still gets a unique group.</summary>
        public object GroupFor(SurfaceKind kind) => byKind.TryGetValue(kind, out Slot slot) ? slot : null;

        public void Show(SurfaceKind kind, RectTransform paneContent, string id)
        {
            if (paneContent == null) return;
            if (!byKind.TryGetValue(kind, out Slot slot)) return;

            // RE-BIND FIRST, before the screen is activated or framed. `id` is not decoration here: each of
            // these screens holds ONE binding at a time and nothing on the tab-click path (TabStripView ->
            // WorkspaceController.SetActive -> SyncSurfaces -> here) would otherwise change it, so clicking
            // back to a town's tab while a building's tab is also open would re-show the screen still bound to
            // the BUILDING. See MapScreenController.RebindSurface for the full statement of the defect and why
            // every branch of it early-outs when the binding is already correct — this method runs on every
            // layout change, not only on a tab click. PageSurfaceHost.Show has always done the equivalent
            // (`documentController.OpenPage(id)`); this is the same idea for the five screens that have no
            // such call of their own.
            screens?.RebindSurface(kind, id);

            slot.Owner = kind;
            slot.HasOwner = true;
            slot.ShownIn = paneContent;
            slot.Visible = true;
            if (slot.Screen != null) slot.Screen.SetActive(true);

            Canvas.willRenderCanvases -= ApplyFramesForRender;
            Canvas.willRenderCanvases += ApplyFramesForRender;

            // Same first-frame belt MapSurfaceHost.Show takes, and for the same reason: Show can run from
            // WorkspaceController.SetSurfaceRegistry -> WorkspaceBuilder.Awake, i.e. before uGUI has ever laid
            // this hierarchy out, so paneContent's world corners would otherwise be meaningless for one frame.
            // It matters MORE here than for the camera: SetActive(true) runs the screen's own Awake ->
            // EnsureBuilt synchronously, so the canvas being framed was created microseconds ago and has never
            // been laid out at all.
            Canvas.ForceUpdateCanvases();
            ApplyFrames();
        }

        public void Hide(SurfaceKind kind)
        {
            if (!byKind.TryGetValue(kind, out Slot slot)) return;
            // Another kind is currently driving this screen — leave it alone. The whole reason this class is
            // one component with owned slots; see the class doc.
            if (slot.HasOwner && slot.Owner != kind) return;

            slot.HasOwner = false;
            slot.Visible = false;
            slot.ShownIn = null;
            if (slot.Screen != null) slot.Screen.SetActive(false);
            // Hand the whole canvas back (minus the menu-bar strip) so a screen re-shown OUTSIDE the workspace
            // is not stuck inside a pane rect it no longer occupies — the same stale-clamp argument
            // MapSurfaceHost.Hide makes, and the reason PaneChromeFrame.Reset does not simply zero the
            // offsets. Cheap: Reset writes only on change.
            for (int i = 0; i < slot.Frames.Count; i++) PaneChromeFrame.Reset(slot.Frames[i]);

            // Unsubscribe only once NOTHING is visible any more — the handler is shared by all slots, and a
            // per-slot unsubscribe would silently stop driving a slot that is still shown.
            bool anyVisible = false;
            for (int i = 0; i < slots.Count; i++) if (slots[i].Visible) { anyVisible = true; break; }
            if (!anyVisible) Canvas.willRenderCanvases -= ApplyFramesForRender;
        }

        /// <summary>Same static-event cleanup MapSurfaceHost.OnDestroy does, and for the same reason: a static
        /// delegate still pointing at a destroyed Unity object throws on its next dispatch.</summary>
        void OnDestroy() => Canvas.willRenderCanvases -= ApplyFramesForRender;

        void ApplyFramesForRender() => ApplyFrames();

        void ApplyFrames()
        {
            var corners = new Vector3[4];
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (!slot.Visible || slot.ShownIn == null) continue;

                EnsureFrames(slot);
                slot.ShownIn.GetWorldCorners(corners);   // ScreenSpaceOverlay: world position IS screen pixel.
                for (int f = 0; f < slot.Frames.Count; f++) PaneChromeFrame.Apply(slot.Frames[f], corners);
            }
        }

        /// <summary>Turns whatever of this slot's canvases have become resolvable into PaneChromeFrames.
        /// Lazy and retried every frame rather than resolved once in Rewire, for the reason Slot's own doc
        /// gives: these screens build their canvas inside their own Awake, gated on being ACTIVE, so before
        /// the first Show() there is nothing to frame — and Show() itself activates the screen, which runs
        /// that Awake synchronously, so the very first ApplyFrames after a Show is what finds it.
        ///
        /// `isRootCanvas` rather than "the first Canvas found", the same filter (and the same trap) as
        /// MapSurfaceHost.EnsureFrames: a nested overrideSorting canvas is already inside the frame via its
        /// parent, and framing it again would inset it twice.</summary>
        void EnsureFrames(Slot slot)
        {
            for (int i = slot.Frames.Count - 1; i >= 0; i--)
                if (slot.Frames[i] == null) slot.Frames.RemoveAt(i);

            // Downward so RemoveAt does not skip the next entry.
            for (int i = slot.PendingFrameRoots.Count - 1; i >= 0; i--)
            {
                var root = slot.PendingFrameRoots[i];
                if (root == null) { slot.PendingFrameRoots.RemoveAt(i); continue; }
                if (!root.activeInHierarchy) continue;

                root.GetComponentsInChildren(true, canvasScratch);
                bool found = false;
                foreach (var canvas in canvasScratch)
                {
                    if (canvas == null || !canvas.isRootCanvas) continue;
                    var frame = PaneChromeFrame.Ensure(canvas.transform);
                    if (frame == null) continue;
                    found = true;
                    // Contains-before-Add: Ensure is idempotent, so re-reaching an already-framed canvas must
                    // be a no-op rather than a duplicate entry Apply/Reset would then write twice. n is 1-2.
                    if (!slot.Frames.Contains(frame)) slot.Frames.Add(frame);
                }
                if (found) slot.PendingFrameRoots.RemoveAt(i);
            }
        }
    }

    /// <summary>The per-Kind ISurfaceHost adapter over ScreenSurfaceHosts — a plain object, not a
    /// MonoBehaviour, because it holds no state of its own: every Show/Hide is forwarded to the one component
    /// that owns the screens, tagged with the Kind that is asking. Five of these exist (one per Kind), three
    /// of them pointing at the SAME slot; see ScreenSurfaceHosts' class doc for why that sharing has to be
    /// resolved inside the component rather than by the adapters.</summary>
    public class ScreenSurfaceHost : ISurfaceHost
    {
        readonly SurfaceKind kind;
        readonly ScreenSurfaceHosts owner;

        public ScreenSurfaceHost(SurfaceKind kind, ScreenSurfaceHosts owner)
        {
            this.kind = kind;
            this.owner = owner;
        }

        public SurfaceKind Kind => kind;

        public void Show(RectTransform paneContent, string id) => owner?.Show(kind, paneContent, id);

        /// <summary>The shared Slot, so the three interior kinds report ONE group — see
        /// ScreenSurfaceHosts.GroupFor. Falls back to `this` when the owner is gone or the kind unregistered,
        /// which keeps the group unique rather than accidentally merging every such host into null.</summary>
        public object ShareGroup => owner != null ? (owner.GroupFor(kind) ?? this) : this;

        public void Hide() => owner?.Hide(kind);

        /// <summary>Empty by design, not by omission. A tab of one of these kinds gets its title from the
        /// opener, which is the only place that has it: MapScreenController.Open* passes the POI's/room's own
        /// name (see OpenDungeonEditor/OpenBuildingInterior), and there is no cheap re-lookup from here — this
        /// object holds no PoiManager or DungeonManager, and giving it one would add exactly the kind of
        /// live-but-stale reference this arc's recompile gap keeps producing. ISurfaceHost.TitleFor is not
        /// wired to any call site yet (its own doc says so); the day a title-refresh path exists, the right
        /// fix is to hand the resolver in at registration, not to discover managers here.</summary>
        public string TitleFor(string id) => "";
    }
}
