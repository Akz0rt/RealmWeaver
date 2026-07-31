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
    /// that is an accepted limitation, not a bug, for the Page surface specifically) — a second pane whose
    /// active tab shares that Kind is left showing whatever the host was last Show()n as, which
    /// WorkspaceController.SyncSurfaces resolves by always Show()ing the FOCUSED pane last.</summary>
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
    }

    /// <summary>Surface kind -> the one host object that shows/hides it. Task 9 registers Page and WorldMap;
    /// Settlement/BuildingInterior/Dungeon/BattleGrid stay unregistered until Task 10 wraps their existing
    /// screen objects — For() simply returns null for those today, and WorkspaceController.SyncSurfaces
    /// treats "no host for this Kind" as "nothing to show", not an error.</summary>
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
    /// ordinary sequence — open a page, then «Открыть рядом» a DIFFERENT page), only the pane
    /// WorkspaceController showed LAST actually renders content; the other pane's content area goes empty
    /// until its own tab is reactivated. This is not new here — NotesDocumentController.ActivePage is
    /// itself a single field, so the two tabs could never show DIFFERENT content simultaneously even before
    /// Task 9 — and it is exactly what the brief's ISurfaceHost signature allows (one host, no pane
    /// parameter). WorkspaceController.SyncSurfaces's "focused pane shown last" rule at least makes the
    /// outcome predictable rather than order-of-iteration arbitrary.
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
    /// mapCamera/chrome are resolved by FindFirstObjectByType when Create's caller passes null/empty,
    /// mirroring the override-or-discover pattern already used elsewhere in this codebase (e.g.
    /// PoiManager.cameraController, DungeonManager.poiManager): WorkspaceBuilder is not wired into the
    /// scene yet (Task 11 does that), so nothing can drag a reference onto it through the Inspector before
    /// then — discovery is what lets this host find the SAME camera/panels MapScreenController already
    /// owns without any scene edit. WorkspaceBuilder still exposes override fields for Task 11 (or a
    /// manual test) to pin down explicitly if discovery ever picks the wrong instance.
    ///
    /// KNOWN SEAM (not fixed here — out of this task's scope, see the brief's point 5): MapScreenController
    /// / ScreenSwitcher still independently drives mapEditorPanelGO/mapLegendUiGO's active state via
    /// AppScreen.MapEditor, completely unaware this host exists — that coupling is only removed by Task
    /// 10's screen-layer rework, which this task must not anticipate. Until then the two mechanisms are
    /// event-driven, not per-frame, so whichever fires LAST wins with no continuous fight; the one concrete
    /// case that can disagree is closing the POI editor while a non-map tab is focused, which re-asserts
    /// AppScreen.MapEditor (and therefore the chrome's active state) out from under a Hide() this host
    /// already issued.
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

        /// <summary>The full-bleed background WorkspaceBuilder paints behind NavigatorColumn/PaneContainer
        /// (RootRow's own Image) — see SetBackgroundsEnabled's own doc for why THIS one, specifically, needs
        /// a thread-through reference rather than being reached via paneContent.parent the way the pane- and
        /// content-level backgrounds are. Read ONLY through ResolveRootRowBackground below, never directly —
        /// see that method's own doc and the class doc's RECOMPILE GAP paragraph for why a direct read would
        /// be unrecoverable after a domain reload wipes this field mid-session.</summary>
        Image rootRowBackground;

        RectTransform shownIn;
        bool visible;

        public static MapSurfaceHost Create(GameObject owner, Camera cameraOverride, GameObject[] chromeOverride,
            Image rootRowBackground)
        {
            var host = owner.AddComponent<MapSurfaceHost>();
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
        }

        void SetChromeActive(bool active)
        {
            if (chrome != null)
                foreach (var go in chrome)
                    if (go != null) go.SetActive(active);
            if (toolbar != null) toolbar.SetChromeVisible(active);
        }

        public string TitleFor(string id) => WorkspaceOps.DefaultWorldMapTitle;
    }
}
