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
            // A script recompile while already in Play Mode re-invokes Awake() on existing components, and
            // this method builds the entire shell imperatively with `new GameObject(...)` — so without a
            // guard, every hot-reload would stack another full duplicate hierarchy on top of the one already
            // built, exactly as NotesRootBuilder documents. The guard is DemolishForRebuild rather than an
            // early return, and that inversion is the whole of Task 11 Step 5.
            //
            // WHY WHOLESALE, having spent this arc arguing for the opposite. Through Task 10 the guard branch
            // RE-POINTED references and rebuilt nothing, on the principle that a partially-recovered view is
            // worse than an absent one. That principle is still right and is why the branch never grew: what
            // it could not reach kept growing instead. A reload wipes every non-serialized field and every
            // runtime AddListener, which for this shell means TabStripView.controller and its OnLayoutChanged
            // subscription, every tab/close/«+» callback, NavigatorView's controller/documentController/
            // columnLayoutElement and its three subscriptions, its header, search and «+ Группа» listeners
            // (chrome it builds ONCE and never rebuilds, so no Rebuild could re-add them),
            // QuickOpenPopup.controller, and the divider's two drag delegates. Re-pointing all of that is not
            // a smaller job than rebuilding — it is the same job, spread over five files, with a new way to
            // half-do it in each. The shell is built imperatively from Layout and nothing else, so
            // demolishing and re-running this method produces EXACTLY what a cold start produces. That is the
            // "rebuilding the whole shell" the previous revision of this comment named as Task 11's answer.
            //
            // WHAT MAKES IT LOSSLESS is the other half of this task: WorkspaceController.RestoreFromPrefs
            // re-applies the DM's stored tabs/split/navigator width during the rebuild, so a recompile no
            // longer silently resets the workspace to WorkspaceOps.NewDefault() either. Before persistence
            // existed, rebuilding would have thrown the layout away just as re-pointing did — which is part
            // of why this step waited for Task 11 rather than being done in Task 9.
            //
            // WHAT IS NOT DESTROYED, and why the two rules differ. The CHILD hierarchy is demolished; the
            // four components this method AddComponents onto its OWN GameObject (WorkspaceController,
            // QuickOpenPopup, MapSurfaceHost, ScreenSurfaceHosts) are REUSED — see EnsureComponent, and see
            // each Create/Attach, which are reuse-or-add for this reason. Two arguments, both load-bearing:
            //   • MapSurfaceHost/ScreenSurfaceHosts hold the map camera's viewport rect, the disabled pane
            //     backgrounds and which legacy screen is currently ON. Destroy() is deferred to end of frame,
            //     so a destroy-then-AddComponent pair leaves two live hosts asserting opposite things for a
            //     frame; and a destroyed host cannot turn off the screen it was holding. Rewire() — which
            //     both already have, for precisely this reload — re-points them with no such window.
            //   • WorkspaceController carries the ONLY [SerializeField] state in this shell
            //     (prefsProjectPath), and a serialized field survives a domain reload only on a component
            //     that itself survives. Destroying it would lose the very key the restore needs.
            //
            // DestroyImmediate, not Destroy, for the children — and this is not a stylistic preference.
            // Destroy defers to end of frame, so the OLD "WorkspaceCanvas" would still be a child (and still
            // answer Transform.Find, and still read non-null through Unity's lifetime-aware ==) while this
            // method builds a SECOND one with the same name. WorkspaceController.ShellRoot and
            // MapSurfaceHost.ResolveRootRowBackground both resolve by that exact path, so they would have a
            // 50/50 chance of latching onto the corpse for the rest of the session — and
            // MapScreenController.EnsureSwitcher would bake it into AppScreen.Workspace's member table.
            // DestroyImmediate is legal here (Awake is not one of the callbacks Unity forbids it in) and
            // removes the ambiguity outright rather than managing it.
            //
            // The two stranded-overlay cleanups the old branch performed are not lost, they moved: the tab
            // drag's ghost and insertion marker are parented to the canvas root (TabStripView's BuildGhost),
            // so they die with it and TabDragHandler.HideStrandedOverlays is gone; QuickOpenPopup's palette
            // is a ROOT canvas that does NOT, so QuickOpenPopup.Attach now drops it — see there for why a
            // stranded palette is the one that actually matters (its backdrop eats every click, so a revived
            // shell would come back alive and still unusable).
            if (transform.childCount > 0) DemolishForRebuild();
            EnsureEventSystemExists();

            // Resolves the live NotesRootBuilder BEFORE anything below reads documentController — see
            // EnsureDocumentController's own doc for why this must not construct a second NotesDocumentController.
            NotesRootBuilder notesRoot = EnsureDocumentController();

            // WorkspaceController owns Layout independently of any Unity view state, so it is built
            // first — NavigatorColumn's initial width below reads Controller.Layout.NavigatorWidth.
            //
            // EnsureComponent, not AddComponent: on the rebuild path this component already exists and must
            // be kept, not replaced (see Awake's own comment on the two things it carries that a fresh one
            // would not). EnsureLayout is then called EXPLICITLY, because GetComponent — unlike AddComponent
            // — does not invoke Awake() synchronously, and Unity's Awake dispatch order between two
            // components on the same GameObject is undefined: on the rebuild path this class is the
            // PRE-EXISTING component and the controller was AddComponent-ed later, so the builder tends to
            // run first. Same direct-call pattern EnsureDocumentController uses on NotesRootBuilder.EnsureBuilt.
            Controller = EnsureComponent<WorkspaceController>();
            Controller.EnsureLayout();

            // BEFORE the two Build* calls below, because they read the restored values directly:
            // BuildNavigatorColumn seeds the column from Layout.NavigatorWidth and BuildPaneContainer anchors
            // the divider at Layout.SplitRatio. Restoring afterwards would leave the first frame showing the
            // DEFAULT geometry until the next OnLayoutChanged happened to reflow it — a visible flash of the
            // wrong window on every launch. See RestoreFromPrefs for why this is unconditional and why it
            // asks nothing about what exists yet.
            Controller.RestoreFromPrefs();

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

        /// <summary>Tears down everything Awake built LAST time, so Awake can build it again from scratch —
        /// the demolition half of the wholesale rebuild (see Awake's own comment for why wholesale).
        ///
        /// ONE CHILD is expected in practice ("WorkspaceCanvas"), but this loops over all of them rather than
        /// naming it: every other root this shell creates is deliberately NOT a child (QuickOpenPopup's
        /// palette, NavContextMenu's menu and ConfirmDialog's dialog are all root canvases so they can draw
        /// over everything), so anything that IS a child got here from this method and belongs to it. Naming
        /// the canvas would silently leak a child a future step adds beside it.
        ///
        /// DestroyImmediate — see Awake for the argument. In short: a deferred Destroy leaves the old
        /// "WorkspaceCanvas" answering Transform.Find alongside the new one for a frame, and two resolvers in
        /// this arc (WorkspaceController.ShellRoot, MapSurfaceHost.ResolveRootRowBackground) find their
        /// targets by exactly that path. Iterating BACKWARDS because DestroyImmediate re-indexes the child
        /// list on the spot — a forward loop would skip every second child.
        ///
        /// Each demolished view's OnDestroy runs synchronously from here. That is wanted, not tolerated:
        /// NavigatorView.OnDestroy unsubscribes from OnLayoutChanged/OnDocumentChanged/OnPoisChanged, and
        /// PoiManager is a SCENE component that outlives the shell — so on any rebuild where those references
        /// are still live (an Awake re-entered without a domain reload), skipping it would leave a destroyed
        /// MonoBehaviour in the manager's invocation list. After a real reload the references are already
        /// null and every unsubscribe is a no-op, which is why this costs nothing on the common path.</summary>
        void DemolishForRebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        /// <summary>Reuse-or-add for the components Awake puts on its OWN GameObject. Written out rather than
        /// `GetComponent<T>() ?? gameObject.AddComponent<T>()`: `??` bypasses UnityEngine.Object's overloaded
        /// `==`, so a destroyed-but-not-yet-collected component would slip through as if it were live. The
        /// explicit null check goes through that operator. Same idiom, same reason, as
        /// TabDragHandler.EnsureOverlay and NavigatorView.Rebuild's read of documentController.</summary>
        T EnsureComponent<T>() where T : Component
        {
            var existing = GetComponent<T>();
            return existing != null ? existing : gameObject.AddComponent<T>();
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
            // column never competes for a share of the row's leftover space — a fixed-width column must not
            // grow into space the panes need, and flexibleWidth 0 is what says so.
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
            // sized within RootRow. The ORDER is the load-bearing part: a LayoutGroup added first reports
            // its own preferred size upward, and a LayoutElement added after it overrides that report.
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
