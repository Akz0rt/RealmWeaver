using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Builds the workspace shell skeleton imperatively at Awake, following the pattern
    /// NotesRootBuilder established: navigator column | draggable divider | pane container (itself
    /// split Primary|Secondary). Attach to an empty GameObject; not yet wired into any scene — that
    /// is Task 11, the only task allowed to touch the scene.
    ///
    /// Scope for Task 5 was deliberately narrow (see the plan's Task 5 step 6): navigator column as
    /// an empty sized container, the two pane containers, and the one divider that matters here —
    /// the Primary|Secondary seam driving SplitRatio. Task 7 is what fills the navigator column with
    /// NavigatorView (tree, search, collapse toggle) — see BuildNavigatorColumn's own comment. There
    /// is still only the one divider Task 5 built: no step in this plan wires a drag gesture to
    /// NavigatorWidth, only the collapse toggle Task 7 Step 4 asks for.
    ///
    /// Task 9 wires the SurfaceRegistry last in Awake (see the end of the method): it re-points
    /// documentController at the live NotesRootBuilder already in the scene (EnsureDocumentController)
    /// and registers the page/map hosts, so a tab's active surface actually renders instead of the
    /// content area sitting empty. See SurfaceRegistry.cs for what each host does and does not cover yet.
    /// </summary>
    public class WorkspaceBuilder : MonoBehaviour
    {
        const float DividerWidth = 6f;

        /// <summary>Top strip reserved for ProjectMenuBar, whose own BarHeightPixels is 40f and whose canvas
        /// sorts at 100 — ABOVE this shell's 70 — so anything the shell draws up there is simply covered by
        /// it. This is the "minus whatever genuinely draws above the shell" inset, derived from that bar
        /// rather than copied: the retired NotesLayoutController reserved TopChromeHeight = 86f (40 menu bar +
        /// 46 map toolbar) and NotesRootBuilder reserved 20 via layout padding, and neither number is right
        /// here.
        ///
        /// MapToolbarUI's 46px strip (which sits at anchoredPosition Y = -40, i.e. directly below the menu
        /// bar, making up the old 86) is deliberately NOT reserved, because its canvas sorts at 40 — BELOW
        /// this shell. It is therefore hidden by the shell rather than drawing over it, so reserving space
        /// would not fix an overlap; it would carve out a permanent 46px band the shell does not own. That
        /// band would be visibly EMPTY whenever a non-map surface is active, since MapSurfaceHost.Hide already
        /// calls MapToolbarUI.SetChromeVisible(false) — a dead strip showing the raw camera, in exchange for
        /// legacy chrome that Р5/Task 10 replaces outright.
        ///
        /// CONSEQUENCE, accepted: while the map surface is shown, the toolbar is partly occluded — its left
        /// portion (the map/regions/layers tab segment starts 12px in and runs 320px wide) disappears behind
        /// the 236px navigator column, while the part crossing the pane's content area still shows through,
        /// because the map surface disables that area's backgrounds and the shell paints no pixels there. Task
        /// 10/Р5 re-hosts that toolbar as shell-native chrome, which is what actually resolves it.
        ///
        /// PUBLIC, and read from ProjectMenuBar rather than re-typing 40f, because PaneChromeFrame.Reset needs
        /// the SAME inset: Task 10a removed the menu-bar term from the map chrome's own top offsets (see
        /// MapLayersPanel.cs:74), so an UNHOSTED frame has to reproduce the strip this constant reserves or
        /// that chrome lands under the bar. Deriving it from ProjectMenuBar.BarHeightPixels means the two
        /// cannot drift, which the sentence at the top of this doc ("whose own BarHeightPixels is 40f") was
        /// previously only asserting.</summary>
        public const float MenuBarInset = ProjectMenuBar.BarHeightPixels;

        [Header("External refs")]
        [Tooltip("The document NavigatorView/QuickOpenPopup render. Left unassigned in the Inspector — Task 9 " +
                 "auto-discovers the live NotesRootBuilder already in the scene (FindFirstObjectByType) and " +
                 "reads its DocumentController, so the SAME instance is used everywhere without requiring a " +
                 "scene edit this task is not allowed to make (see EnsureDocumentController). An explicit " +
                 "Inspector assignment, once WorkspaceBuilder itself is wired into the scene (Task 11), still " +
                 "wins over discovery.")]
        public NotesDocumentController documentController;

        [Header("External refs — WorldMap surface")]
        // The two overrides carry SEPARATE [Tooltip]s on purpose: a PropertyAttribute decorates the single
        // member that follows it, and these are two declarations, not one. The exclusion rule below belongs on
        // mapChrome — the array a human actually populates — and an earlier revision put it on mapCamera,
        // where the Inspector rendered it on the Camera slot and the field it was written for showed nothing.
        [Tooltip("Override for MapSurfaceHost's camera discovery (FindFirstObjectByType<WorldMapRenderer>." +
                 "targetCamera). Left null until Task 11 (or a manual test) has a reason to pin a specific " +
                 "instance instead of whatever discovery finds.")]
        public Camera mapCamera;

        [Tooltip("Override for MapSurfaceHost's chrome discovery (FindFirstObjectByType<PoiEditPanel>/" +
                 "MapLegendUI). Left empty until Task 11 (or a manual test) has a reason to pin specific " +
                 "instances instead of whatever discovery finds.\n\n" +
                 "DO NOT LIST PoiInfoPopup OR THE REGION-LABEL OVERLAYS HERE. Everything in this array is " +
                 "both shown/hidden with the map AND confined to the map's pane by PaneChromeFrame. Those " +
                 "three place themselves with cam.WorldToScreenPoint, which already accounts for the " +
                 "camera's viewport rect, so insetting them a second time moves them off their own map " +
                 "features. Same rule for anything else that positions itself from the camera.")]
        public GameObject[] mapChrome;

        [Header("External refs — the five ex-screen surfaces (Task 10c)")]
        // Overrides for ScreenSurfaceHosts.Rewire's discovery, the same override-or-discover pattern the two
        // map fields above use, and left null for the same reason: WorkspaceBuilder is not in the scene until
        // Task 11, so nothing can drag a reference in before then. Three fields, not five — Settlement,
        // BuildingInterior and Dungeon are three SurfaceKinds served by the ONE DungeonEditorScreen object
        // (see ScreenSurfaceHosts' class doc), so there is only one thing to pin for all three.
        [Tooltip("Override for the PoiEditor surface's screen object (FindFirstObjectByType<PoiEditorScreen>).")]
        public GameObject poiEditorScreenOverride;

        [Tooltip("Override for the Settlement/BuildingInterior/Dungeon surfaces' shared screen object " +
                 "(FindFirstObjectByType<DungeonEditorScreen>). One object serves all three kinds.")]
        public GameObject interiorScreenOverride;

        [Tooltip("Override for the BattleGrid surface's screen object (FindFirstObjectByType<BattleGridScreen>).")]
        public GameObject battleGridScreenOverride;

        public WorkspaceController Controller { get; private set; }

        /// <summary>The two tab strips, exposed the same way Controller is — Task 8 reaches these to wire
        /// TabStripView.OnRequestQuickOpen, without GetComponentsInChildren/index-guessing at the call site.
        /// Unlike Controller, these do NOT get recovered after a Play-mode script reload — see Awake()'s
        /// comment on why a recovery attempt here would be worse than useless.</summary>
        public TabStripView PrimaryTabStrip { get; private set; }
        public TabStripView SecondaryTabStrip { get; private set; }

        /// <summary>Same non-recovery caveat as PrimaryTabStrip/SecondaryTabStrip — see Awake()'s comment.</summary>
        public NavigatorView Navigator { get; private set; }

        /// <summary>Task 8's Ctrl+K palette — a persistent component (see its own class doc for why, over a
        /// static Show/Close pair like NavContextMenu/ConfirmDialog), attached to this GameObject so its
        /// Update() polls the chord for the lifetime of the shell. Same non-recovery caveat as
        /// PrimaryTabStrip/SecondaryTabStrip/Navigator below — see Awake()'s comment.</summary>
        public QuickOpenPopup QuickOpenPopup { get; private set; }

        void Awake()
        {
            // A script recompile while already in Play Mode re-invokes Awake() on existing
            // components, but this method builds the entire shell imperatively with
            // `new GameObject(...)` — without this guard, every such hot-reload would stack another
            // full duplicate hierarchy on top of the one already built, exactly as NotesRootBuilder
            // documents. The child GameObjects (and the WorkspaceController sibling component)
            // survive the reload; only re-running Awake() is new, so recover the Controller
            // reference here rather than leaving it null — the auto-property's backing field does
            // NOT survive the reload (it is not a serialized Unity field), even though the component
            // it points at does. Recovering Controller this way is genuinely useful: WorkspaceController's
            // OWN Awake() re-runs on the same reload and rebuilds a fresh, USABLE (if reset) Layout.
            //
            // PrimaryTabStrip/SecondaryTabStrip/Navigator/QuickOpenPopup are deliberately NOT recovered the
            // same way. A naive GetComponentsInChildren<TabStripView>() keyed by PaneIndex would not even find the right
            // instance — PaneIndex is a plain auto-property, the exact construct this comment just said
            // does not survive reload, so it reads 0 on BOTH surviving strips post-reload. But fixing the
            // lookup (e.g. by GameObject name, which DOES survive) would still recover a functionally DEAD
            // object: TabStripView has no Awake() of its own that re-wires anything, so its `controller`
            // field, its `OnLayoutChanged` subscription, and every tab/close/plus Button's runtime
            // AddListener callback are all gone too (none of that is Unity-serialized, exactly like
            // Layout) — and NavigatorView is built the same imperative way, with the same gap.
            // QuickOpenPopup has the mirror-image version of this same gap: its own `controller` field is
            // gone too (see QuickOpenPopup.Update's `if (controller == null) return;` guard, which exists
            // precisely for this case), so a naively-recovered instance would poll Keyboard.current forever
            // and never actually be able to open anything. A non-null-but-inert reference is worse than
            // leaving these properties null — Task 8 assigning OnRequestQuickOpen onto a dead strip would
            // silently do nothing, which is harder to notice than a null reference. This is the same known
            // gap WorkspaceController's own Awake() documents for Layout, extended to the tab strips and the
            // navigator: Task 11 owns fixing THOSE, by rebuilding the whole shell (or at minimum re-running
            // the affected Create methods) on restore, not by a partial reference recovery here.
            //
            // Surfaces (Task 9, review round 3) are the ONE exception to "don't partially recover here", and
            // deliberately so: WorkspaceController.surfaceRegistry is ALSO a plain, non-serialized field, so
            // without re-wiring it here `SyncSurfaces` early-returns on every call for the rest of the
            // session — not merely a stale rect (the already-accepted class of gap above), but the ENTIRE
            // surface system going silently dead: no tab switch, close or promotion shows or hides anything,
            // with nothing in a standalone build to tell the user why. This branch re-wires it WITHOUT
            // rebuilding any UI — the distinction the rest of this comment draws for tab strips/Navigator
            // still holds and is not being abandoned: MapSurfaceHost is RECOVERED via GetComponent (never
            // Create, which would AddComponent a duplicate) and only re-runs its OWN discovery
            // (MapSurfaceHost.Rewire — camera/chrome/toolbar, the same plain-field class of gap on THAT
            // component); NotesRootBuilder.EnsureDocumentController/EnsureBuilt were already reload-safe
            // (childCount-guarded — see NotesRootBuilder's own doc) and cost nothing extra to call again;
            // and `new SurfaceRegistry()` / `new PageSurfaceHost(...)` are cheap plain C# objects, not
            // GameObjects, so reconstructing them is not "stacking a duplicate hierarchy" the way a second
            // BuildPaneContainer call would be — there is no hierarchy involved in either at all.
            //
            // Round 4 extends that same exception to the two things SyncSurfaces reads BESIDES the registry,
            // for the same reason and by the same rule (re-point references, never rebuild):
            // WorkspaceController.Layout (null after a reload -> an NRE on Layout.FocusedPane INSIDE this
            // recovery path) and WorkspaceController's six pane handles (null after a reload -> PaneContent
            // returns null forever, so SyncSurfaces shows nothing and Hides every host: a blank workspace
            // rather than a revived one). Both are recovered by WorkspaceController's own Ensure* methods —
            // see their docs. NOTHING here revives the tab strips/Navigator/QuickOpenPopup/divider-drag
            // delegates, so after a reload the correct SURFACE renders but the chrome around it stays inert
            // until Task 11: clicking a tab, the navigator, «+», Ctrl+K or dragging the divider all still do
            // nothing. That is the line — pane handles are read by SyncSurfaces, which runs post-reload; the
            // strips are read by nothing that runs post-reload.
            if (transform.childCount > 0)
            {
                Controller = GetComponent<WorkspaceController>();
                if (Controller != null)
                {
                    // GetComponent, unlike the AddComponent below, does NOT invoke Awake() synchronously, and
                    // Unity's Awake dispatch order between two components on the SAME GameObject is undefined
                    // — so WorkspaceController.Awake may not have run yet when SetSurfaceRegistry (and its
                    // immediate SyncSurfaces) fires at the end of this branch. Call both Ensure* directly
                    // instead of trusting that ordering, the same way EnsureDocumentController below calls
                    // NotesRootBuilder.EnsureBuilt() directly rather than trusting ITS Awake. Layout first:
                    // EnsurePaneHandles ends in ReflowPanes, which reads Layout.Secondary/SplitRatio.
                    Controller.EnsureLayout();
                    Controller.EnsurePaneHandles();
                }

                var recoveredNotesRoot = EnsureDocumentController();
                var recoveredMapHost = GetComponent<MapSurfaceHost>();
                recoveredMapHost?.Rewire(mapCamera, mapChrome, rootRowBackground: null);
                // Same GetComponent-then-Rewire recovery as the map host on the line above, and needed for the
                // same reason (ScreenSurfaceHosts' own RECOMPILE GAP paragraph): the component survives the
                // reload, every reference inside it does not. Recovering it also has a visible consequence the
                // map host's does not — a screen that was ON at the moment of the reload can only be turned
                // off again by a host that still holds it.
                var recoveredScreenHosts = GetComponent<ScreenSurfaceHosts>();
                recoveredScreenHosts?.Rewire(poiEditorScreenOverride, interiorScreenOverride, battleGridScreenOverride);

                var recoveredRegistry = new SurfaceRegistry();
                if (recoveredNotesRoot != null)
                    recoveredRegistry.Register(new PageSurfaceHost(recoveredNotesRoot.DocumentController, recoveredNotesRoot.DocumentView));
                if (recoveredMapHost != null) recoveredRegistry.Register(recoveredMapHost);
                if (recoveredScreenHosts != null)
                    foreach (var host in recoveredScreenHosts.Hosts) recoveredRegistry.Register(host);
                if (Controller != null) Controller.SetSurfaceRegistry(recoveredRegistry);

                return;
            }

            EnsureEventSystemExists();

            // Resolves the live NotesRootBuilder BEFORE anything below reads documentController — see
            // EnsureDocumentController's own doc for why this must not construct a second NotesDocumentController.
            NotesRootBuilder notesRoot = EnsureDocumentController();

            // WorkspaceController owns Layout independently of any Unity view state, so it is built
            // first — NavigatorColumn's initial width below reads Controller.Layout.NavigatorWidth.
            Controller = gameObject.AddComponent<WorkspaceController>();

            var canvasGO = new GameObject("WorkspaceCanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 70: ABOVE the whole legacy map-chrome band, BELOW the persistent menu bar and the legacy
            // full-screen screens. Leaving this at Unity's default 0 is what made the shell invisible at the
            // first Editor checkpoint — it built correctly, resolved correct rects and clamped the camera
            // correctly, and then drew UNDERNEATH every pre-existing canvas in the project, so the window
            // read as "the old app, unchanged".
            //
            // Above (must be, or the shell stays buried): MapLegendUI (0, and an order-0 tie is broken by
            // hierarchy, which is not something to rely on), MapToolbarUI (40), GenerationScreenUI /
            // GenerationProgressUI / PoiInfoPopup (50), and the four docked tool panels — MapLayersPanel /
            // EditorBrushPanel / PoiToolPanel / RegionsPanel (60). The 60 band is the binding constraint:
            // those panels are 216px wide at the top-left, i.e. entirely inside the navigator's 236px column.
            //
            // Below, deliberately: ProjectMenuBar (100) and the three ex-screen canvases — PoiEditorScreen
            // (100), DungeonEditorScreen (101), BattleGridScreen (102).
            //
            // TASK 10c REVISITED THIS, as the previous revision of this comment said it would, and the answer
            // is that all three STAY where they are — for a reason that is now the opposite of the original
            // one. They are no longer window-owning: ScreenSurfaceHosts confines each to its pane with a
            // PaneChromeFrame + RectMask2D, so the shell is never "hidden underneath" one of them any more.
            // What keeps them above 70 now is that they are opaque uGUI canvases with NOTHING disabling the
            // pane's own ContentArea background (unlike the camera, which needs
            // MapSurfaceHost.SetBackgroundsEnabled to punch a hole) — drop them below 70 and that background
            // would simply cover them. Their sorting above ProjectMenuBar (101/102) stopped mattering in the
            // same change: the mask clips them to a ContentArea that already excludes the menu-bar strip,
            // since RootRow is inset by MenuBarInset. See ScreenSurfaceHosts' class doc.
            //
            // Also below the shell's OWN popups, which already sit far higher and must stay there:
            // NavigatorView's context menu (1000) and QuickOpenPopup (4000), plus the modal band —
            // EditorBrushPanel's dropdown template (30000) and ConfirmDialog (32000).
            //
            // 71-99 is left free for future shell-owned chrome that belongs above the panes but below the
            // menu bar.
            canvas.sortingOrder = 70;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var rootRowGO = new GameObject("RootRow", typeof(RectTransform));
            rootRowGO.transform.SetParent(canvasGO.transform, false);
            var rootRowRect = rootRowGO.GetComponent<RectTransform>();
            Stretch(rootRowRect);
            // ...then give the top MenuBarInset pixels back to ProjectMenuBar (see the constant's doc). Both
            // canvases use a default CanvasScaler (ConstantPixelSize, scaleFactor 1 — nothing in this project
            // configures one), so 40 here is the same 40 pixels the bar occupies. Insetting RootRow itself,
            // rather than padding its HorizontalLayoutGroup, also keeps the shell's own full-bleed background
            // out of that strip, and every descendant follows for free — including the ContentArea whose world
            // corners MapSurfaceHost converts into the camera's viewport rect.
            rootRowRect.offsetMax = new Vector2(0f, -MenuBarInset);

            // A full-bleed opaque background on the row itself (not a separate child) — this Canvas
            // paints only where a Graphic currently covers a pixel, so any gap left by a
            // shrunk/hidden child would show whatever a previous frame drew there ("ghosting"),
            // exactly the trap NotesRootBuilder's CanvasViewport/notesAreaBg comment documents. An
            // Image here does not conflict with the HorizontalLayoutGroup added below — Image does
            // not implement ILayoutElement, so it has no opinion on how the row itself is sized.
            var rootRowBg = rootRowGO.AddComponent<Image>();
            ThemeService.Tag(rootRowBg, ThemeRole.Bg);

            var rootLayout = rootRowGO.AddComponent<HorizontalLayoutGroup>();
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandWidth = false;   // see BuildPaneContainer's comment: force-expand would
            rootLayout.childControlHeight = true;        // override each child's own flexibleWidth opinion.
            rootLayout.childForceExpandHeight = true;
            rootLayout.spacing = 0f;

            // NavigatorView.Create fills the column BuildNavigatorColumn just sized, and immediately
            // overwrites navLayoutElement.preferredWidth from Layout.NavigatorCollapsed/NavigatorWidth in
            // its own first Rebuild() — so the value BuildNavigatorColumn seeds it with here is only a
            // harmless placeholder for the same reason BuildPane's flexibleWidth placeholder comment gives.
            var (navRect, navLayoutElement) = BuildNavigatorColumn(rootRowGO.transform, Controller.Layout.NavigatorWidth);
            Navigator = NavigatorView.Create(navRect, navLayoutElement, Controller, documentController);

            var (_, primaryStrip, secondaryStrip) = BuildPaneContainer(rootRowGO.transform, Controller);
            PrimaryTabStrip = primaryStrip;
            SecondaryTabStrip = secondaryStrip;

            // Attached last, once both strips exist — wires the «+» hook Task 6 left unassigned
            // (TabStripView.OnRequestQuickOpen's own doc comment: "clicking «+» invokes this ... and
            // otherwise does nothing until Task 8 assigns it"). Method-group assignment straight to the
            // Action<int> delegate; OpenForPane itself focuses the requesting pane before opening, which is
            // how a per-pane «+» ends up landing its result in THAT pane rather than merely the focused one
            // (see QuickOpenPopup's own class doc). Note pane 1's «+» is unreachable while the workspace is
            // unsplit regardless — ReflowPanes deactivates the WHOLE secondary pane GameObject, strip
            // included, whenever Layout.Secondary is null — so WorkspaceOps.Focus's own "ignore a
            // nonexistent pane" guard is defence in depth here, not what actually keeps this safe day to day.
            QuickOpenPopup = QuickOpenPopup.Attach(gameObject, Controller, documentController);
            PrimaryTabStrip.OnRequestQuickOpen = QuickOpenPopup.OpenForPane;
            SecondaryTabStrip.OnRequestQuickOpen = QuickOpenPopup.OpenForPane;

            // Surfaces (Task 9) — built last, once Controller/panes exist to hand hosts a real container and
            // SetSurfaceRegistry can run its first sync against a fully-Initialize()'d Layout. Page is
            // registered only when a live NotesRootBuilder was actually found; the map host is always
            // registered — MapSurfaceHost.Create tolerates a null camera/empty chrome (nothing to Show/Hide),
            // the same null-tolerance NavigatorView/QuickOpenPopup already extend to a null documentController.
            var registry = new SurfaceRegistry();
            if (notesRoot != null) registry.Register(new PageSurfaceHost(notesRoot.DocumentController, notesRoot.DocumentView));
            registry.Register(MapSurfaceHost.Create(gameObject, mapCamera, mapChrome, rootRowBg));
            // Task 10c: the five ex-screens. Registered unconditionally — ScreenSurfaceHosts registers a host
            // only for the kinds whose screen it actually found (AddSlot's null check), so a scene missing one
            // of them yields fewer hosts rather than a host that silently does nothing.
            foreach (var host in ScreenSurfaceHosts.Create(gameObject, poiEditorScreenOverride,
                         interiorScreenOverride, battleGridScreenOverride).Hosts)
                registry.Register(host);
            Controller.SetSurfaceRegistry(registry);
        }

        /// <summary>Finds the NotesRootBuilder already living in the scene (this predates the workspace-shell
        /// plan) and, if `documentController` was not assigned in the Inspector, points it at THAT SAME
        /// instance's NotesDocumentController — never constructs a new one. WorkspaceBuilder itself is not
        /// wired into the scene until Task 11, so there is no Inspector slot to drag a NotesRootBuilder
        /// reference into before then; FindFirstObjectByType is what lets this resolve correctly the moment a
        /// human (or Task 11) attaches this component anywhere in a scene that already has NotesRootBuilder in
        /// it — no scene edit required for Task 9 itself.
        ///
        /// Calls NotesRootBuilder.EnsureBuilt() explicitly rather than trusting its own Awake() already ran:
        /// Unity does not guarantee Awake ordering across components on different GameObjects, and this method
        /// runs from INSIDE WorkspaceBuilder's own Awake, so NotesRootBuilder's Awake may not have fired yet.
        /// EnsureBuilt is idempotent (see its own doc), so calling it here is always safe.</summary>
        NotesRootBuilder EnsureDocumentController()
        {
            var notesRoot = FindFirstObjectByType<NotesRootBuilder>();
            if (notesRoot == null) return null;

            notesRoot.EnsureBuilt();
            if (documentController == null) documentController = notesRoot.DocumentController;
            return notesRoot;
        }

        static (RectTransform rect, LayoutElement element) BuildNavigatorColumn(Transform parent, float navigatorWidth)
        {
            var navGO = new GameObject("NavigatorColumn", typeof(RectTransform));
            navGO.transform.SetParent(parent, false);

            var navImg = navGO.AddComponent<Image>();
            ThemeService.Tag(navImg, ThemeRole.Panel);

            // Fixed pixel width from preferredWidth alone: minWidth/flexibleWidth pinned to 0 so this
            // column never competes for a share of the row's leftover space, the same reasoning
            // NotesTreeSidebar.cs documents for its own root LayoutElement.
            var navLayoutElement = navGO.AddComponent<LayoutElement>();
            navLayoutElement.minWidth = 0f;
            navLayoutElement.flexibleWidth = 0f;
            navLayoutElement.preferredWidth = navigatorWidth;

            return (navGO.GetComponent<RectTransform>(), navLayoutElement);
        }

        static (RectTransform paneContainerRect, TabStripView primaryStrip, TabStripView secondaryStrip)
            BuildPaneContainer(Transform parent, WorkspaceController controller)
        {
            var paneContainerGO = new GameObject("PaneContainer", typeof(RectTransform));
            paneContainerGO.transform.SetParent(parent, false);
            var paneContainerRect = paneContainerGO.GetComponent<RectTransform>();

            var paneHLayout = paneContainerGO.AddComponent<HorizontalLayoutGroup>();
            paneHLayout.childControlWidth = true;
            paneHLayout.childForceExpandWidth = false;   // each pane's width must come from its OWN
            paneHLayout.childControlHeight = true;        // flexibleWidth (set from SplitRatio below), never
            paneHLayout.childForceExpandHeight = true;    // from force-expand overriding that opinion.
            paneHLayout.spacing = 0f;

            // Added AFTER the HorizontalLayoutGroup, with explicit non-negative values, so THIS
            // LayoutElement — not the group's own upward self-report — decides how PaneContainer is
            // sized within RootRow. Mirrors NotesTreeSidebar.cs's own root LayoutElement-after-
            // LayoutGroup ordering and its documented reasoning.
            var paneContainerLayoutElement = paneContainerGO.AddComponent<LayoutElement>();
            paneContainerLayoutElement.minWidth = 0f;
            paneContainerLayoutElement.preferredWidth = 0f;
            paneContainerLayoutElement.flexibleWidth = 1f;   // absorbs every pixel NavigatorColumn didn't take.

            var (primaryRect, primaryElement, primaryContentRect, primaryStrip) =
                BuildPane(paneContainerGO.transform, "PrimaryPane", controller, 0);
            var (secondaryRect, secondaryElement, secondaryContentRect, secondaryStrip) =
                BuildPane(paneContainerGO.transform, "SecondaryPane", controller, 1);

            // Created LAST so it sits frontmost among PaneContainer's children — DraggableDivider.Create's
            // own doc: "a UI sibling created later always wins raycasts over one created earlier there".
            // Anchored at SplitRatio's fraction across PaneContainer's width; WorkspaceController.ReflowPanes
            // keeps this in sync with SplitRatio afterward (both on structural change and live drag).
            var divider = DraggableDivider.Create(paneContainerGO.transform,
                new Vector2(controller.Layout.SplitRatio, 0f), new Vector2(controller.Layout.SplitRatio, 1f),
                new Vector2(0.5f, 0.5f), DividerWidth);
            var dividerRect = divider.GetComponent<RectTransform>();

            divider.OnDragDeltaX += dx =>
                controller.SetSplitRatioLive(controller.Layout.SplitRatio + dx / Mathf.Max(1f, paneContainerRect.rect.width));
            divider.OnDragEnd += controller.CommitSplitRatio;

            controller.Initialize(
                new WorkspaceController.PaneHandles(primaryRect, primaryElement, primaryContentRect),
                new WorkspaceController.PaneHandles(secondaryRect, secondaryElement, secondaryContentRect),
                dividerRect);

            return (paneContainerRect, primaryStrip, secondaryStrip);
        }

        /// <summary>One pane: a VerticalLayoutGroup stacking a fixed-height TabStripView on top of a
        /// flexible-height ContentArea. Task 5 left this bare on purpose (scope discipline); Task 6 is what
        /// carves the strip off the top — TabStripView.Create is called FIRST (so it lands as the first,
        /// topmost child) and ContentArea second. The outer pane rect's own LayoutElement keeps
        /// minWidth/preferredWidth pinned to 0 exactly as Task 5 set it: flexibleWidth (set by
        /// WorkspaceController.ReflowPanes from SplitRatio) is still the ONLY thing deciding this pane's
        /// width — that pin is what stops the tab strip's own fixed preferredHeight/width opinions from
        /// leaking into the pane's WIDTH the way Task 5's own comment warned about.</summary>
        static (RectTransform rect, LayoutElement element, RectTransform contentRect, TabStripView tabStrip)
            BuildPane(Transform parent, string name, WorkspaceController controller, int paneIndex)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Bg);

            var vLayout = go.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;    // strip and content both stretch to the pane's full width.
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;   // strip keeps its own fixed preferredHeight; ContentArea's
            vLayout.spacing = 0f;                     // flexibleHeight=1 (below) is what claims the rest.

            var element = go.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 0f;
            // flexibleWidth is set live by WorkspaceController.ReflowPanes (from SplitRatio); the value
            // here is a harmless placeholder overwritten before the first frame renders (Initialize calls
            // ReflowPanes immediately).
            element.flexibleWidth = 1f;

            var tabStrip = TabStripView.Create(go.transform, controller, paneIndex);

            var contentGO = new GameObject("ContentArea", typeof(RectTransform));
            contentGO.transform.SetParent(go.transform, false);
            var contentImg = contentGO.AddComponent<Image>();
            ThemeService.Tag(contentImg, ThemeRole.Bg);
            var contentElement = contentGO.AddComponent<LayoutElement>();
            contentElement.minHeight = 0f;
            contentElement.preferredHeight = 0f;
            contentElement.flexibleHeight = 1f;

            return (go.GetComponent<RectTransform>(), element, contentGO.GetComponent<RectTransform>(), tabStrip);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
