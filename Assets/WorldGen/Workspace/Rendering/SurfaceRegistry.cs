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
    /// outcome predictable rather than order-of-iteration arbitrary.</summary>
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
    /// LIVE on-screen rect continuously, not just once at the moment Show() runs — see LateUpdate.
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
    /// RECOMPILE GAP (same known class as WorkspaceController.Layout/WorkspaceBuilder's tab
    /// strips/Navigator/QuickOpenPopup — Task 11 owns fixing all of it together): WorkspaceBuilder.Awake's
    /// own recompile guard (`if (transform.childCount > 0) return;`) stops a Play-mode script reload from
    /// AddComponent-ing a SECOND MapSurfaceHost, so this component itself survives correctly. But a domain
    /// reload runs every field initializer again for every surviving MonoBehaviour, and `visible`/`shownIn`
    /// are plain private fields with no `[SerializeField]` — Unity does NOT preserve those across the
    /// reload, it resets them to `false`/`null`. The Camera component's own `enabled`/`rect`, by contrast,
    /// ARE native Unity object state and DO survive. So the two sides desynchronise in the OPPOSITE
    /// direction from "stale tracking data": the CAMERA correctly remembers whatever it was left at, while
    /// this script forgets it ever showed anything — nothing re-registers with a freshly-reset
    /// WorkspaceController.Layout afterward either, so the map can end up stuck at whatever `mapCamera.rect`/
    /// `.enabled` happened to be at reload time, with no path back until Task 11's real fix (rebuilding/
    /// re-syncing the whole shell on restore).</summary>
    public class MapSurfaceHost : MonoBehaviour, ISurfaceHost
    {
        Camera mapCamera;
        GameObject[] chrome;
        MapToolbarUI toolbar;

        /// <summary>The full-bleed background WorkspaceBuilder paints behind NavigatorColumn/PaneContainer
        /// (RootRow's own Image) — see SetBackgroundsEnabled's own doc for why THIS one, specifically, needs
        /// a thread-through reference rather than being reached via paneContent.parent the way the pane- and
        /// content-level backgrounds are.</summary>
        Image rootRowBackground;

        RectTransform shownIn;
        bool visible;

        public static MapSurfaceHost Create(GameObject owner, Camera cameraOverride, GameObject[] chromeOverride,
            Image rootRowBackground)
        {
            var host = owner.AddComponent<MapSurfaceHost>();
            host.rootRowBackground = rootRowBackground;

            host.mapCamera = cameraOverride != null
                ? cameraOverride
                : FindFirstObjectByType<WorldMapRenderer>()?.targetCamera;

            if (chromeOverride != null && chromeOverride.Length > 0)
            {
                host.chrome = chromeOverride;
            }
            else
            {
                var discovered = new List<GameObject>();
                var poiPanel = FindFirstObjectByType<PoiEditPanel>();
                var legend = FindFirstObjectByType<MapLegendUI>();
                if (poiPanel != null) discovered.Add(poiPanel.gameObject);
                if (legend != null) discovered.Add(legend.gameObject);
                host.chrome = discovered.ToArray();
            }

            host.toolbar = FindFirstObjectByType<MapToolbarUI>();
            return host;
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

            // Forces uGUI's deferred layout pass to run NOW rather than at its usual point (just before
            // rendering, after every Update/LateUpdate this frame) — without it, the very first Show() of a
            // session (called from WorkspaceController.SetSurfaceRegistry, itself called from
            // WorkspaceBuilder.Awake) would read paneContent's PRE-layout world corners and give the camera
            // a zero/garbage viewport for one visible frame. Same idiom GenerationScreenUI/
            // GenerationProgressUI already use for the same reason. Only paid here, not every LateUpdate —
            // by the second frame onward the ordinary end-of-frame layout pass has already run at least
            // once, so ApplyViewport's own reads are already correct without forcing it again.
            Canvas.ForceUpdateCanvases();
            ApplyViewport();
        }

        public void Hide()
        {
            visible = false;
            // Restore BEFORE clearing shownIn — SetBackgroundsEnabled needs the container reference to know
            // which pane's/content's Images to re-enable. Runs even when shownIn is already null (nothing to
            // restore, the ternary/null-guards inside just no-op) so Hide stays safe to call unconditionally.
            if (shownIn != null) SetBackgroundsEnabled(shownIn, true);
            shownIn = null;
            SetChromeActive(false);
            // A camera is not a uGUI element — "hide" means stop it rendering (Camera.enabled = false)
            // rather than anything analogous to SetActive on a RectTransform. Left enabled=false rather
            // than deactivating the whole GameObject: other components (WorldMapRenderer's own click/hover
            // handling, MapCameraController) may live on the same object and must keep running so the map
            // stays in a consistent state for the next Show(), exactly as MapScreenController already
            // assumes elsewhere.
            if (mapCamera != null) mapCamera.enabled = false;
        }

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
        /// state to invalidate.</summary>
        void SetBackgroundsEnabled(RectTransform container, bool enabled)
        {
            var contentImg = container.GetComponent<Image>();
            if (contentImg != null) contentImg.enabled = enabled;

            var paneImg = container.parent != null ? container.parent.GetComponent<Image>() : null;
            if (paneImg != null) paneImg.enabled = enabled;

            if (rootRowBackground != null) rootRowBackground.enabled = enabled;
        }

        /// <summary>Re-derives the camera's viewport rect from the pane's LIVE on-screen rect every frame
        /// while shown, rather than converting once inside Show(). A one-shot conversion goes stale three
        /// ways: (1) the very first sync runs from WorkspaceController.SetSurfaceRegistry, called from
        /// WorkspaceBuilder.Awake — before uGUI's first layout pass, so paneContent's world corners are not
        /// yet meaningful; (2) WorkspaceController.SetSplitRatioLive (the divider-drag path) deliberately
        /// does NOT raise OnLayoutChanged (see its own doc comment), so dragging the divider moves the pane
        /// without ever calling Show() again; (3) an ordinary window resize moves every pane's screen rect
        /// with no workspace event at all. Continuous re-derivation fixes all three for free.</summary>
        void LateUpdate()
        {
            if (!visible || shownIn == null || mapCamera == null) return;
            ApplyViewport();
        }

        void ApplyViewport()
        {
            // Guards the SAME null case LateUpdate already guards before calling this — needed here too
            // because Show() calls this directly (to avoid a one-frame stale/zero viewport — see its own
            // comment), on a path LateUpdate's guard does not cover. Discovery finding no camera at all
            // (Create's own null-tolerance — see the class doc) must not throw on the very first Show().
            if (mapCamera == null) return;

            var corners = new Vector3[4];
            shownIn.GetWorldCorners(corners);   // ScreenSpaceOverlay: world position IS screen-pixel position.

            float screenW = Mathf.Max(1f, Screen.width);
            float screenH = Mathf.Max(1f, Screen.height);
            float xMin = corners[0].x / screenW;
            float yMin = corners[0].y / screenH;
            float xMax = corners[2].x / screenW;
            float yMax = corners[2].y / screenH;

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
