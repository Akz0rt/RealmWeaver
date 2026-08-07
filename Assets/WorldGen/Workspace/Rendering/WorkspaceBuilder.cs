using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Builds the workspace shell skeleton imperatively at Awake, following the pattern
    /// NotesRootBuilder established: navigator column | draggable divider | pane container (itself
    /// split Primary|Secondary). Lives on the "WorkspaceBuilder" GameObject in SampleScene.unity — an
    /// otherwise empty object, with every external reference left unassigned so that discovery
    /// (EnsureDocumentController, MapSurfaceHost.Rewire, ScreenSurfaceHosts.Rewire) resolves them. Task 11
    /// added that object; through Tasks 5-10 this component was in no scene at all, which is what several
    /// comments in this arc still explain the consequences of — in the past tense, where they are correct.
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
                 "scene edit (see EnsureDocumentController). Still unassigned now that Task 11 has put this " +
                 "component in the scene, because discovery already finds the one live instance — an explicit " +
                 "Inspector assignment would win over it, and is only worth making if a scene ever has two.")]
        public NotesDocumentController documentController;

        [Header("External refs — WorldMap surface")]
        // The two overrides carry SEPARATE [Tooltip]s on purpose: a PropertyAttribute decorates the single
        // member that follows it, and these are two declarations, not one. The exclusion rule below belongs on
        // mapChrome — the array a human actually populates — and an earlier revision put it on mapCamera,
        // where the Inspector rendered it on the Camera slot and the field it was written for showed nothing.
        [Tooltip("Override for MapSurfaceHost's camera discovery (FindFirstObjectByType<WorldMapRenderer>." +
                 "targetCamera). Left null: Task 11 wired this component into the scene without pinning it, " +
                 "because discovery finds the one camera. Assign only if a scene ever has two.")]
        public Camera mapCamera;

        [Tooltip("Override for MapSurfaceHost's chrome discovery (FindFirstObjectByType<PoiEditPanel>/" +
                 "MapLegendUI). Left empty, for the same reason as the camera slot above.\n\n" +
                 "DO NOT LIST PoiInfoPopup OR THE REGION-LABEL OVERLAYS HERE. Everything in this array is " +
                 "both shown/hidden with the map AND confined to the map's pane by PaneChromeFrame. Those " +
                 "three place themselves with cam.WorldToScreenPoint, which already accounts for the " +
                 "camera's viewport rect, so insetting them a second time moves them off their own map " +
                 "features. Same rule for anything else that positions itself from the camera.")]
        public GameObject[] mapChrome;

        [Header("External refs — the five ex-screen surfaces (Task 10c)")]
        // Overrides for ScreenSurfaceHosts.Rewire's discovery, the same override-or-discover pattern the two
        // map fields above use, and left null for the same reason: discovery finds the one instance of each,
        // so Task 11's scene edit had nothing worth pinning. Three fields, not five — Settlement,
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
            // QuickOpenPopup.controller, PaneFocusOnClick.controller, and the divider's two drag delegates. Re-pointing all of that is not
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
            // STRANDED OVERLAYS, all three of them, because a reload that lands while one is open leaves it
            // on screen with every reference to it wiped. They divide by parentage, not by importance:
            //   • The tab drag's ghost and insertion marker are parented to the workspace canvas
            //     (TabStripView.BuildGhost), so the demolition below takes them — which is why
            //     TabDragHandler.HideStrandedOverlays could be deleted rather than moved.
            //   • QuickOpenPopup's palette and NavContextMenu's menu are ROOT canvases (parented to nothing,
            //     so they can draw over everything) and survive the demolition. Both are full-screen
            //     click-eating backdrops whose dismiss listener the reload deleted, so either one alone
            //     leaves a correctly rebuilt shell alive and still unusable — the palette at sortingOrder
            //     4000, the menu at 1000, both far above the shell's 70.
            //   • ConfirmDialog's modal is a root canvas too, and is the WORST of the three: an OPAQUE
            //     backdrop at sortingOrder 32000 — above everything, including the palette — deliberately
            //     non-dismissing, and with no by-name recovery anywhere in the class to be made reachable.
            //     Same bug, not a different category.
            // The three cleanups live in three places, and the split is by OWNERSHIP, not by importance:
            //   • the palette is a COMPONENT, so its re-wiring and its cleanup belong together in
            //     QuickOpenPopup.Attach (which also has to Close a LIVE palette on a rebuild that did not
            //     follow a reload);
            //   • NavContextMenu is a static class with nothing to attach to and nothing to re-wire, and it
            //     is SHELL-owned — only the navigator raises it — so DemolishForRebuild below, the one
            //     method that runs exactly on a shell rebuild, is its right and only caller;
            //   • ConfirmDialog is APP-wide (this class never raises it; ProjectMenuBar's save/load errors
            //     and «Создать новый мир?» do), so hanging it on a shell rebuild would both miss it in a
            //     scene with no shell and tie an application modal's lifetime to a component that does not
            //     own it. Its cleanup is called from ProjectMenuBar.Awake — the app's one always-present
            //     chrome — and that call site carries the argument.
            // See each DismissStranded for why two of the three could always FIND the strand and never be
            // called, which is the actual shape of this defect.
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

            // Click-to-focus on a pane's CONTENT (clicking a TAB already focuses, via TabStripView). Attached
            // here, beside the palette, because it is the same shape of thing: a persistent component on this
            // GameObject whose plain fields a domain reload wipes and whose Attach re-points them on the
            // rebuild. Not exposed as a property like QuickOpenPopup/the tab strips — nothing reads it back;
            // it only observes. Deliberately needs no Inspector slot and no scene edit.
            PaneFocusOnClick.Attach(gameObject, Controller);

            // Surfaces (Task 9) — built last, once Controller/panes exist to hand hosts a real container and
            // SetSurfaceRegistry can run its first sync against a fully-Initialize()'d Layout. Page is
            // registered only when a live NotesRootBuilder was actually found; the map host is always
            // registered — MapSurfaceHost.Create tolerates a null camera/empty chrome (nothing to Show/Hide),
            // the same null-tolerance NavigatorView/QuickOpenPopup already extend to a null documentController.
            var registry = new SurfaceRegistry();
            if (notesRoot != null)
            {
                // ONE VIEW PER PANE, built by this host inside the pane's own content area (two-panes arc,
                // Task 4). The font is threaded through rather than re-fetched, so every page view and the
                // expanded board are drawn with the one asset NotesRootBuilder names.
                var pageHost = new PageSurfaceHost(notesRoot.DocumentController, notesRoot.BuiltinFont);
                registry.Register(pageHost);

                // Р4: a board expanded out of a page's flow, drawn full-pane over the SAME DocBlock the page
                // keeps drawing inline — see CanvasSurfaceHost. Registered beside the page host because it
                // needs exactly what the page host needs, plus the host itself: undo and redraw have to reach
                // whichever pane is currently showing the board's own page.
                registry.Register(new CanvasSurfaceHost(notesRoot.DocumentController, pageHost, notesRoot.BuiltinFont));

                // What the page's inline links resolve against and where they open — see PageLinkBridge.
                // Here rather than inside PageSurfaceHost because it needs `Controller`, and because it owns a
                // subscription that has to be dropped again on the next rebuild.
                var linkBridge = PageLinkBridge.Attach(gameObject, pageHost, Controller, QuickOpenPopup);

                // Р4: a board tab must not outlive its block, its page or its page's group — see
                // CanvasTabPruner for why all three are answered in one place rather than at each seam.
                var pruner = CanvasTabPruner.Attach(gameObject, notesRoot.DocumentController, pageHost, Controller);

                // WHERE THE KEYS GO (two panes arc, Task 5). One keystroke handler and one «@» popup exist
                // for the whole app — both live on NotesRootBuilder's GameObject — and until this task they
                // were wired to ONE view, chosen by a `pane == 0 || keyboard.pageView == null` branch that
                // this comment used to apologise for. Typing in the other pane then fell through to the raw
                // TMP fields: the text edited, but Enter/Tab/Backspace/undo never reached the block list.
                //
                // NEITHER OF THEM IS PER-VIEW ANY MORE, which is why this wiring sits OUTSIDE the hook
                // below: the router is asked, every frame, which view holds the caret, so there is nothing
                // for a per-view assignment to say. It is built here, beside `pageHost`, and dies with it on
                // the next shell rebuild — see PageFocusRouter's own class doc.
                var keyboard = notesRoot.Keyboard;
                var focusRouter = new PageFocusRouter(pageHost, Controller);
                if (keyboard != null)
                {
                    keyboard.router = focusRouter;
                    // Attach is REUSE-OR-ADD (see its own doc), so this re-points the one popup rather than
                    // building a second. OnTokenInserted is a plain field Attach does not touch, and
                    // MentionPopup.Choose has no other way to tell the keyboard controller where the caret
                    // ended up — see NoteExternalTokenInsertion's own doc.
                    keyboard.mentionPopup = MentionPopup.Attach(notesRoot.gameObject, notesRoot.DocumentController, focusRouter);
                    keyboard.mentionPopup.OnTokenInserted = keyboard.NoteExternalTokenInsertion;
                }

                // EVERY per-view wiring lives in this ONE hook, and it is assigned BEFORE
                // SetSurfaceRegistry below — the first SyncSurfaces runs inside that call, so a hook attached
                // afterwards would silently miss both views (see PageSurfaceHost.OnViewCreated's own doc).
                pageHost.OnViewCreated = (pane, view) =>
                {
                    // The «↗» button on an inline board. Opens in the OTHER pane, the same rule a clicked POI
                    // link follows: a board in one pane and the session text in the other is what two panes
                    // are FOR. Wired here rather than in PageLinkBridge because it needs the document to read
                    // the caption from, and because it is not a link.
                    view.CanvasRouter = blockId =>
                    {
                        var doc = documentController != null ? documentController.Document : null;
                        var block = doc != null ? NotesDocOps.FindBlock(doc, blockId, out _) : null;
                        Controller.Open(NotesSurface.Canvas(blockId), NotesSurface.TitleOf(block), inOtherPane: true);
                    };

                    linkBridge.Configure(view);
                    pruner.Observe(view);

                    // THE ONE CHORD THIS VIEW POLLS FOR ITSELF: Ctrl+F. Everything else the keyboard decides
                    // goes through DocKeyboardController, of which there is exactly one — but PageSearchBar
                    // lives INSIDE each view and reads the hardware from its own Update, so with two views an
                    // ungated Ctrl+F opened two search boxes at once. The probe answers from the same router,
                    // so undo, «@» and search cannot disagree about which pane the DM is in. See
                    // DocumentPageView.KeyboardTargetProbe.
                    view.KeyboardTargetProbe = () => focusRouter.ActiveView() == view;
                };
            }
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

            // The one stranded root canvas that has no component to clean itself up — see Awake's own
            // enumeration of all three overlays, and NavContextMenu.DismissStranded for what its backdrop
            // does to a session if this is skipped. A no-op on every rebuild that did not follow a reload
            // with a menu open, which is nearly all of them.
            NavContextMenu.DismissStranded();

            // A FOURTH stranded root canvas, added here by the two-panes arc's Task 4 rather than left to
            // ride along with something else. The «@» popup's canvas is a root canvas too, and its cleanup
            // used to be a side effect of MentionPopup.Attach, which NotesRootBuilder.EnsureBuilt called
            // unconditionally on every reload. Task 4 moved Attach inside PageSurfaceHost's OnViewCreated
            // hook (i.e. only when some pane actually built a page view) and Task 5 lifted it back out — the
            // popup asks a router now, not a view — but it is STILL conditional: Awake only reaches it when
            // a NotesRootBuilder was found in the scene. So the cleanup stays stated here, where it runs once
            // per shell rebuild and asks nothing about what else exists. See MentionPopup.DismissStranded.
            MentionPopup.DismissStranded();
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
        /// in no scene at all when this was written (Task 9), so there was no Inspector slot to drag a
        /// NotesRootBuilder reference into; FindFirstObjectByType is what let it resolve correctly the moment
        /// a human attached this component to a scene that already had one. Task 11's scene edit did exactly
        /// that and still leaves the slot empty — discovery was never scaffolding, it is how this resolves.
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

    /// <summary>Focus follows a click into a pane's CONTENT AREA, the way it does in every editor with split
    /// panes. Through Task 11 the only two things that focused a pane were clicking one of its TABS
    /// (TabStripView.cs:179) and QuickOpenPopup.OpenForPane — so a DM whose last click was INSIDE pane B still
    /// had pane A focused, and everything they opened from the navigator landed in A. From their side that
    /// reads as "it threw me to the existing tab", which is what the DM reported.
    ///
    /// A PURE OBSERVER, NEVER A HANDLER, and this is the whole design rather than an implementation detail.
    /// The obvious version — an Image + IPointerClickHandler on the ContentArea — fails twice over. It would
    /// not RECEIVE most clicks: PoiEditorScreen/DungeonEditorScreen/BattleGridScreen are root canvases at
    /// sortingOrder 100-102, above the shell's 70, so they consume the raycast before it reaches the pane
    /// underneath; and for the map surface the ContentArea's Image is DISABLED outright
    /// (MapSurfaceHost.SetBackgroundsEnabled), so it is not in the raycast at all. And where it did receive
    /// one it would be a participant in the event — one more thing between a surface and its own input. This
    /// class instead polls the mouse and hit-tests the pane rects itself. It never enters the raycast chain,
    /// never sets eventData.Used, and cannot swallow, delay or reorder a single click: camera drag, POI click,
    /// typing in a page and the tab drag all behave exactly as they did. Focusing is an addition.
    ///
    /// ARMED ON PRESS, APPLIED ON RELEASE, IN LateUpdate. FocusPane raises OnLayoutChanged, which runs
    /// SyncSurfaces, a PlayerPrefs write and every view's rebuild — so firing it on pointer-DOWN would put all
    /// of that between a press and the click uGUI dispatches from it. Every branch of
    /// MapScreenController.RebindSurface early-outs when the binding is already correct (that file states it
    /// as "a requirement, not an optimisation"), so the destructive case — a screen rebuilding and destroying
    /// the button mid-gesture — does not arise today; deferring means it cannot arise from a later change
    /// either. EventSystem.Update() is an Update, so a LateUpdate is guaranteed to run after this frame's
    /// click dispatch. Requiring press and release in the SAME pane also drops a camera drag that leaves the
    /// pane, for free.
    ///
    /// THE OVERLAY VERDICT IS TAKEN ON PRESS, and must be. QuickOpenPopup's palette is a full-screen backdrop
    /// at sortingOrder 4000 whose result rows call Close() during dispatch — by release the raycast would see
    /// the pane underneath and focus would drift to whichever pane the cursor happened to be over, landing the
    /// DM's Ctrl+K result in the wrong pane (OpenForPane focuses the REQUESTING pane, and Open reads
    /// Layout.FocusedPane under the hood). One sorting-order threshold covers every such overlay — the palette
    /// (4000), NavigatorView's context menu (1000), EditorBrushPanel's dropdown template (30000) and
    /// ConfirmDialog (32000) — which beats adding three public IsOpen properties that could each go stale.
    ///
    /// RECOMPILE GAP: every field here is plain and non-[SerializeField], so a Play-mode domain reload wipes
    /// them — this arc's recurring defect family (WorkspaceController.shellSuppressed's doc carries the running
    /// count). `controller` and the scratch list are re-pointed by Attach, which WorkspaceBuilder.Awake re-runs
    /// on every shell rebuild. The two pane fields are deliberately NOT recovered: losing a half-finished click
    /// gesture across a recompile costs the DM one click, and they are stored PLUS ONE precisely so that
    /// default(int) == 0 means "nothing armed" rather than "pane 0 armed" — the same polarity discipline
    /// shellSuppressed and persistSuspended state for their own bools.</summary>
    public class PaneFocusOnClick : MonoBehaviour
    {
        /// <summary>The lowest canvas sortingOrder that means "an overlay owns this click, not a pane". 103 is
        /// the first free number above the ex-screen band (PoiEditorScreen 100, DungeonEditorScreen 101,
        /// BattleGridScreen 102) that WorkspaceBuilder.Awake's canvas-order comment enumerates — those five ARE
        /// pane content, confined to a pane by ScreenSurfaceHosts' PaneChromeFrame, so a click on one of them
        /// SHOULD focus its pane. ProjectMenuBar (100) is inside the band too and needs no exception: it draws
        /// in the strip RootRow is inset out of, so no pane rect contains it. Everything above 103 in this
        /// project is a popup or a modal that draws OVER the panes and must not move focus under itself.</summary>
        const int OverlaySortingFloor = 103;

        WorkspaceController controller;

        /// <summary>Reused across clicks rather than allocated per click, the same scratch-buffer idiom
        /// MapSurfaceHost.canvasScratch uses. NOT `readonly` with an initializer, and for the reason that
        /// field's own doc gives: a domain reload restores a MonoBehaviour by DESERIALIZING it, so field
        /// initializers do not re-run and this would come back NULL on a component that is otherwise alive.
        /// Attach re-assigns it.</summary>
        List<RaycastResult> raycastScratch = new List<RaycastResult>();

        /// <summary>The pane the current press started in, PLUS ONE — 0 means "nothing armed", so the value a
        /// domain reload leaves behind is the inert one (see the class doc's RECOMPILE GAP paragraph). Set to 0
        /// when the press was over an overlay or outside every pane, which is how those presses are dropped
        /// rather than merely ignored at release.</summary>
        int armedPanePlusOne;

        /// <summary>The pane a completed click decided on, PLUS ONE, handed from Update to LateUpdate. Same
        /// plus-one polarity as armedPanePlusOne, and the same one-click loss across a reload.</summary>
        int pendingFocusPlusOne;

        /// <summary>REUSE-OR-ADD and idempotent, exactly as QuickOpenPopup.Attach is and for the same reason:
        /// WorkspaceBuilder.Awake re-runs it on every Play-mode shell rebuild, where a second AddComponent
        /// would leave two observers both calling FocusPane. Explicit null check rather than `??` — see
        /// WorkspaceBuilder.EnsureComponent for why the operator is wrong against Unity's lifetime-aware `==`.
        ///
        /// Clears both gesture fields, so a rebuild that lands mid-click cannot apply a focus decided against
        /// the layout that was just demolished.</summary>
        public static PaneFocusOnClick Attach(GameObject host, WorkspaceController controller)
        {
            var existing = host.GetComponent<PaneFocusOnClick>();
            var focus = existing != null ? existing : host.AddComponent<PaneFocusOnClick>();
            focus.controller = controller;
            focus.raycastScratch = new List<RaycastResult>();
            focus.armedPanePlusOne = 0;
            focus.pendingFocusPlusOne = 0;
            return focus;
        }

        void Update()
        {
            // controller is null between a script reload wiping it and WorkspaceBuilder.Awake's rebuild
            // calling Attach again, and for the whole session in a scene with no WorkspaceBuilder — the same
            // window QuickOpenPopup.Update guards, and for the same reason: Update is a per-frame callback on
            // a component someone else's Awake wires. Mouse.current is null on a device with no mouse.
            if (controller == null) return;
            var mouse = Mouse.current;
            if (mouse == null) { armedPanePlusOne = 0; return; }

            // Both branches, not else-if: the Input System coalesces a press and a release that happen inside
            // one frame, setting BOTH flags, and such a click must still focus.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 pressed = mouse.position.ReadValue();
                armedPanePlusOne = OverlayOwnsClick(pressed) ? 0 : PaneUnder(pressed) + 1;
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                int armed = armedPanePlusOne;
                armedPanePlusOne = 0;
                // Same pane on press AND release, so a drag that started in a pane and ended somewhere else
                // (a camera drag flung past the divider) is not a click into that pane.
                if (armed > 0 && PaneUnder(mouse.position.ReadValue()) == armed - 1) pendingFocusPlusOne = armed;
            }
        }

        /// <summary>Applies the click's focus AFTER this frame's EventSystem dispatch — see the class doc's
        /// armed-on-press/applied-on-release paragraph for why the delay is the point rather than a detail.
        /// FocusPane itself no-ops (and raises nothing) when the pane is already focused, so clicking around
        /// inside the pane the DM is already working in costs one comparison per click, not a resync.</summary>
        void LateUpdate()
        {
            if (pendingFocusPlusOne == 0) return;
            int pane = pendingFocusPlusOne - 1;
            pendingFocusPlusOne = 0;
            if (controller != null) controller.FocusPane(pane);
        }

        /// <summary>Which pane's CONTENT AREA contains `screenPos`, or -1. The tab strips are deliberately
        /// outside this: TabStripView's own onClick already focuses (TabStripView.cs:179), and leaving the
        /// strip out keeps this from having any opinion about the tab-drag gesture.
        ///
        /// The two rects tile without overlapping, so the iteration order does not decide anything. Both
        /// guards below are load-bearing rather than defensive: WorkspaceController.PaneContent(1) returns null
        /// whenever Layout.Secondary is null, and ReflowPanes deactivates the whole secondary pane in the same
        /// state — a collapsed pane's rect is stale, not absent. ShellRoot's active check covers the other
        /// direction: ScreenSwitcher deactivates the shell's canvas while Generation/Progress owns the window
        /// (WorkspaceController.shellSuppressed's doc), and the pane rects survive that deactivation intact, so
        /// without it a click on the generation form would silently re-focus a pane behind it.</summary>
        int PaneUnder(Vector2 screenPos)
        {
            GameObject shell = controller.ShellRoot;
            if (shell == null || !shell.activeInHierarchy) return -1;

            for (int pane = 0; pane <= 1; pane++)
            {
                RectTransform content = controller.PaneContent(pane);
                if (content == null || !content.gameObject.activeInHierarchy) continue;
                // Null camera: every canvas in this project is ScreenSpaceOverlay, the same identity
                // PaneChromeFrame's class doc rests on.
                if (RectTransformUtility.RectangleContainsScreenPoint(content, screenPos, null)) return pane;
            }
            return -1;
        }

        /// <summary>True when a popup or modal ABOVE the pane band is under the pointer — see the class doc for
        /// why this verdict is taken at press and never at release.
        ///
        /// Reads RaycastResult.sortingOrder, which GraphicRaycaster copies verbatim from its canvas
        /// (GraphicRaycaster.cs:279 in com.unity.ugui@52e65280e89e), and results[0] is the topmost hit because
        /// EventSystem's own comparer sorts by module.sortOrderPriority first — which for a ScreenSpaceOverlay
        /// canvas is that same canvas.sortingOrder (GraphicRaycaster.cs:53). All four overlays this has to
        /// catch build their OWN root canvas with its OWN GraphicRaycaster, so each reports its own order
        /// rather than an ancestor's.
        ///
        /// NO HITS AT ALL MEANS "NOT AN OVERLAY", which is the common case and must be: a click on the map
        /// lands on a camera, and MapSurfaceHost has disabled every Image over that rect, so there is nothing
        /// for a graphic raycast to return. Treating an empty result as blocked would disable this whole
        /// feature exactly where the DM needs it most.</summary>
        bool OverlayOwnsClick(Vector2 screenPos)
        {
            EventSystem events = EventSystem.current;
            if (events == null || raycastScratch == null) return false;

            events.RaycastAll(new PointerEventData(events) { position = screenPos }, raycastScratch);
            bool blocked = raycastScratch.Count > 0 && raycastScratch[0].sortingOrder >= OverlaySortingFloor;
            // Cleared rather than left populated: RaycastResult holds a GameObject reference, and this buffer
            // lives for the session between clicks.
            raycastScratch.Clear();
            return blocked;
        }
    }

    /// <summary>
    /// Everything a page's inline links need from OUTSIDE the notes layer: what the world currently contains,
    /// where a clicked link opens, and the one event that says a POI was renamed.
    ///
    /// WHY A COMPONENT AND NOT TWO LINES IN Awake. The world list has to be re-read when POIs change, and
    /// PoiManager is a SCENE component that outlives this shell — so somebody has to unsubscribe, or a
    /// rebuilt workspace leaves a destroyed observer in the manager's invocation list (the same hazard
    /// NavigatorView.OnDestroy exists for). WorkspaceBuilder itself is the wrong owner: its rebuild path
    /// demolishes its CHILDREN, not itself, and it has no OnDestroy at all.
    ///
    /// WHY THE NOTES LAYER DOES NOT DO THIS ITSELF. DocumentPageView has no reference to PoiManager or to
    /// WorkspaceController and gains none here — WorldGen.Notes has never referenced WorldGen.Rendering, and
    /// keeping it that way is what lets its whole Data half run in Tools/notes-harness. This class sits on
    /// the workspace side of that line and hands the page two delegates over it.
    ///
    /// Same Attach shape as PaneFocusOnClick above, for the same reason: WorkspaceBuilder.Awake re-runs on
    /// every Play-mode shell rebuild, and a second AddComponent would leave two bridges refreshing the same
    /// page.
    /// </summary>
    public class PageLinkBridge : MonoBehaviour
    {
        /// <summary>The HOST, not one view: since Task 4 there is a DocumentPageView per pane, each with its
        /// own WorldSource/LinkRouter/LinkPicker fields to fill, and they are born one at a time as panes
        /// first show a page. Holding the host is what lets this reach whichever of them exist right now —
        /// see Configure (called per new view, from WorkspaceBuilder's OnViewCreated hook) and the two loops
        /// below, which must reach ALL of them.</summary>
        PageSurfaceHost pageHost;
        WorkspaceController controller;
        QuickOpenPopup palette;
        PoiManager poiManager;

        public static PageLinkBridge Attach(GameObject host, PageSurfaceHost pageHost, WorkspaceController controller,
                                            QuickOpenPopup palette)
        {
            var existing = host.GetComponent<PageLinkBridge>();
            var bridge = existing != null ? existing : host.AddComponent<PageLinkBridge>();

            // Dropped before the fields are re-pointed: on a rebuild these still hold the PREVIOUS shell's
            // subscription, and re-resolving would otherwise add a second one to the same manager.
            bridge.Unsubscribe();
            bridge.pageHost = pageHost;
            bridge.controller = controller;
            bridge.palette = palette;
            bridge.poiManager = null;

            // Normally none exist yet — a freshly built host has built no views, and OnViewCreated is what
            // reaches each one. Kept because Attach must not depend on being called before the first sync:
            // Configure is idempotent, so a host that HAS views simply gets them all wired here instead.
            if (pageHost != null)
                foreach (var view in pageHost.Views) bridge.Configure(view);
            return bridge;
        }

        /// <summary>Fills one view's three link hooks. Called once per view, as it is built.</summary>
        public void Configure(DocumentPageView view)
        {
            if (view == null) return;
            view.WorldSource = CollectWorld;
            view.LinkRouter = Open;
            // The VIEW is captured, not looked up when the button is clicked: «Ссылка» belongs to the toolbar
            // of the page it sits on, and DocumentPageView.RequestInsertLink carries no argument saying which
            // one that is. A closure per view answers it exactly, and needs none of Task 5's caret routing.
            view.LinkPicker = palette != null ? (System.Action)(() => PickLink(view)) : null;
            // The page may already be showing rows built with no resolver at all (its first Rebuild runs from
            // Initialize, before this is called), so their links would be stored names until the next
            // rebuild. One refresh here settles that.
            view.RefreshLinks();
        }

        /// <summary>Rebuilt per call, never cached — the same "no invalidation protocol" rule
        /// WorldObjectSource's own doc states, and the reason a rename needs nothing but a refresh.</summary>
        List<WorldObjectRef> CollectWorld() => WorldObjectSource.Collect(ResolvePoiManager());

        void Open(SurfaceRef surface, string title, bool inOtherPane)
        {
            if (controller == null || surface == null) return;
            controller.Open(surface, title, inOtherPane);
        }

        /// <summary>Hands the page one fact it cannot learn on its own: a palette is up, so the keys belong to
        /// it. Both the palette and DocKeyboardController poll the hardware directly, and a page row stays
        /// "focused" in the keyboard controller's cache after its field is deactivated — so without this, one
        /// Enter pressed in Ctrl+K would choose a row AND split the row behind it. That collision predates
        /// the link picker; building the picker is what walked into it.
        ///
        /// Polled here rather than pushed from the palette, so the palette keeps knowing nothing about pages.</summary>
        void Update()
        {
            // PaletteOpen, not KeyboardSuspended: that one is now the OR of this flag and the search bar's,
            // and writing it here would have this bridge speak for a bar it knows nothing about.
            //
            // EVERY view, not the focused one: the palette covers the whole window, so the keys belong to it
            // no matter which pane the DM was last typing in, and a view left thinking otherwise would act on
            // the same Enter a second time.
            if (pageHost == null) return;
            bool open = palette != null && palette.IsOpen;
            // ViewFor per index rather than the `Views` iterator, ONLY because this is a per-frame method and
            // an iterator block allocates its state machine on every foreach. The two loops elsewhere in this
            // file run on discrete events and use `Views`. Same 0..1 the workspace uses everywhere else.
            for (int pane = 0; pane <= 1; pane++)
            {
                var view = pageHost.ViewFor(pane);
                if (view != null) view.PaletteOpen = open;
            }
        }

        /// <summary>«Ссылка» on the page toolbar of `pageView`: Ctrl+K's own palette, ending in a token
        /// instead of a tab. The view is the one whose toolbar was clicked — see Configure.</summary>
        void PickLink(DocumentPageView pageView)
        {
            if (palette == null || pageView == null) return;
            palette.OpenForLink(hit =>
            {
                // The mapping itself is QuickOpen.TryTokenFor — pure, tested offline, and the one place that
                // knows which half of a hit carries the identity.
                if (QuickOpen.TryTokenFor(hit, out var kind, out var id, out var name))
                    pageView.InsertTokenAtCaret(kind, id, name);
            });
        }

        /// <summary>Found on demand and re-tried on every miss, exactly as NavigatorView.ResolvePoiManager
        /// does: before the world is generated there is no manager to find, and the page must not be stuck
        /// with that answer for the session. Inactive objects included — the manager can legitimately be on a
        /// deactivated object while another surface is showing.</summary>
        PoiManager ResolvePoiManager()
        {
            if (poiManager != null) return poiManager;
            poiManager = FindFirstObjectByType<PoiManager>(FindObjectsInactive.Include);
            if (poiManager != null) poiManager.OnPoisChanged += OnPoisChanged;
            return poiManager;
        }

        /// <summary>A POI was added, renamed or deleted. EVERY open page re-resolves its links, which is what
        /// makes «переименовал — и в тексте новое имя» true without the page having been edited — and true in
        /// both panes, not just the focused one, since the rename is a fact about the world rather than about
        /// where the DM is looking.</summary>
        void OnPoisChanged()
        {
            if (pageHost == null) return;
            foreach (var view in pageHost.Views) view.RefreshLinks();
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (poiManager != null) poiManager.OnPoisChanged -= OnPoisChanged;
            poiManager = null;
        }
    }

    /// <summary>
    /// A tab dies with the thing it names — a board's block, a page, or the whole group a page was in.
    ///
    /// STILL CALLED CanvasTabPruner, but it prunes PAGE tabs too since the two-panes arc's Task 3. Until then
    /// a deleted page took its own visibility with it: NotesDocumentController.DeletePage blanked the one
    /// document-wide ActivePage, so the view emptied itself and the orphaned tab merely sat there showing
    /// nothing. That pointer is gone — a page view now shows whatever its pane's tab last named, and would go
    /// on showing a deleted page's blocks until some unrelated sync happened — so the tab, which is the thing
    /// that actually names it, is what has to go.
    ///
    /// ONE SINK, NOT THREE CALL SITES. A tab can be orphaned at three seams: the DM deletes the board BLOCK
    /// from the page, deletes the PAGE, or deletes a GROUP — and a group takes its pages with it, which
    /// is the seam NotesDocOps.ClearLinksTo's own doc warns is the one people forget. Pruning at each of the
    /// three means three chances to forget, and a forgotten one LOOKS fine: the surface simply draws nothing
    /// under a live tab, which is the exact defect Р1 shipped for pages (f635cc1). So instead of remembering,
    /// this listens to the document itself — all three raise something. Page and group deletion raise
    /// NotesDocumentController.OnDocumentChanged; a block deleted from a page raises
    /// DocumentPageView.OnDocumentMutated. A fourth seam nobody has built yet raises one of the two as well.
    ///
    /// WHY THE PREDICATE IS MapScreenController.SurfaceExists AND NOT A CANVAS-ONLY ONE. PruneSurfaces judges
    /// EVERY tab, so a predicate that only knew about boards would close the map. SurfaceExists is the tested
    /// answer for all kinds and the same one a project load uses — "the tab survives" and "the surface can be
    /// shown" must not be able to disagree.
    ///
    /// WHY THE GUARD, AND WHY IT DIFFERS BETWEEN THE TWO EVENTS. OnDocumentMutated fires on every edit-visit
    /// to a page row, several times a second while the DM types. With no board open anywhere, HasSurfaceOfKind
    /// turns that into a dozen enum comparisons instead of a walk that resolves every tab against the
    /// document, the POI manager and the interior store. That guard therefore stays CANVAS-ONLY: a page tab is
    /// open almost always, so admitting Page there would delete the guard's whole reason and put a full
    /// resolve-every-tab walk on the typing path. A page tab cannot be orphaned by a keystroke anyway — only
    /// by deleting a page or a group, and both of those raise the STRUCTURAL event, which fires on CRUD alone
    /// and is where Page is admitted.
    ///
    /// A PROJECT LOAD passes through here too — LoadDocument raises OnDocumentChanged — and pruning at that
    /// instant is harmless rather than merely tolerated: the document is loaded LAST, so the answer is the
    /// incoming project's, which is the answer EndProjectSwitch is about to apply anyway, and writes are
    /// suspended until it does (WorkspaceController.persistSuspended).
    ///
    /// WHAT ADMITTING Page CHANGED ABOUT THAT, said plainly because it is a real widening. A prune that
    /// actually drops a tab ends in RaiseChanged -> a FULL SyncSurfaces, i.e. every host re-Shown, in the
    /// middle of ProjectMenuBar.LoadFrom. That used to require an open BOARD tab, which is rare; it now also
    /// happens whenever a page tab is open, which is nearly always. The reasoning above still covers it —
    /// the notes document is the LAST thing LoadFrom loads, so the world, the POIs, the dungeons and the
    /// interiors every other host re-reads are already the incoming project's, and no layout write can
    /// escape while persistSuspended holds. This is argued-safe, not observed-safe: nothing runs Unity in
    /// this repo's checks, so a future defect at this seam will show up as "a surface bound to the wrong
    /// thing right after a project load" and this paragraph is where to start.
    ///
    /// Same Attach shape, and the same rebuild hazard, as PageLinkBridge above: WorkspaceBuilder.Awake re-runs
    /// on every Play-mode shell rebuild while NotesRootBuilder's controller SURVIVES it, so a second
    /// AddComponent — or a re-subscribe without a drop — would leave the old, destroyed pruner in the
    /// document's invocation list.
    /// </summary>
    public class CanvasTabPruner : MonoBehaviour
    {
        NotesDocumentController documentController;
        WorkspaceController controller;
        MapScreenController map;
        bool pruning;

        /// <summary>The page views this pruner is subscribed to — one per pane since Task 4, and they arrive
        /// one at a time (Observe, from WorkspaceBuilder's OnViewCreated hook) rather than all at Attach.
        /// Tracked only so Unsubscribe has something to walk; NOT `readonly` with an initializer, because a
        /// domain reload restores a MonoBehaviour by DESERIALIZING it, so the initializer would not re-run and
        /// this would come back null on a live component. Attach re-assigns it.</summary>
        List<DocumentPageView> observed = new List<DocumentPageView>();

        public static CanvasTabPruner Attach(GameObject host, NotesDocumentController documentController,
                                             PageSurfaceHost pageHost, WorkspaceController controller)
        {
            var existing = host.GetComponent<CanvasTabPruner>();
            var pruner = existing != null ? existing : host.AddComponent<CanvasTabPruner>();

            // Dropped before the fields are re-pointed, exactly as PageLinkBridge does: on a rebuild these
            // still hold the PREVIOUS shell's subscriptions to objects that outlived it.
            pruner.Unsubscribe();
            pruner.documentController = documentController;
            pruner.controller = controller;
            pruner.map = null;

            if (documentController != null) documentController.OnDocumentChanged += pruner.PruneStructural;
            // Normally empty — the fresh host has built no views yet, and Observe is what catches each as it
            // appears. Same reason PageLinkBridge.Attach walks Views: this must not depend on running before
            // the first sync.
            if (pageHost != null)
                foreach (var view in pageHost.Views) pruner.Observe(view);
            return pruner;
        }

        /// <summary>Subscribes to one page view's two events. Idempotent (`-=` before `+=`), so re-observing a
        /// view already tracked adds no second handler and no second list entry.</summary>
        public void Observe(DocumentPageView view)
        {
            if (view == null) return;
            view.OnDocumentMutated -= PruneBlocks;
            view.OnDocumentMutated += PruneBlocks;
            view.OnHistoryApplied -= ReshowSurfaces;
            view.OnHistoryApplied += ReshowSurfaces;
            if (observed == null) observed = new List<DocumentPageView>();
            if (!observed.Contains(view)) observed.Add(view);
        }

        /// <summary>Отмена заменила блоки страницы новыми объектами — развёрнутой доске надо взять свой
        /// блок заново. Она делает это только в ISurfaceHost.Show, а Show случается лишь на клике по
        /// вкладке, поэтому без этого вызова вкладка доски после Ctrl+Z рисовала старое и писала правки в
        /// блок, которого уже нет в документе: при сохранении они пропадали.
        ///
        /// Здесь, а не отдельным компонентом, по той же причине, по которой здесь живёт Prune: Attach уже
        /// умеет переживать пересборку оболочки и отписываться, а заводить рядом второй компонент с той же
        /// механикой значило бы иметь два места, где можно забыть отписку.
        ///
        /// Тот же дешёвый guard: пока ни одной вкладки-доски открыто нет, пересобирать нечего.</summary>
        void ReshowSurfaces()
        {
            if (pruning) return;
            if (controller == null) return;
            if (!WorkspaceOps.HasSurfaceOfKind(controller.Layout, SurfaceKind.Canvas)) return;
            controller.RefreshSurfaces();
        }

        /// <summary>A group, a page or a board was created, renamed or deleted — the CRUD event. Both kinds of
        /// tab can be orphaned here, so both are admitted to the guard. Structural changes are discrete DM
        /// actions (a navigator click, a menu item, a project load), not a typing path, so the walk this
        /// admits costs nothing per keystroke — see the class doc for the split.</summary>
        void PruneStructural() => Prune(includePages: true);

        /// <summary>A page's BLOCK list changed — which fires several times a second while the DM types, and
        /// can only orphan a board tab. Canvas-only guard, deliberately; see the class doc.</summary>
        void PruneBlocks() => Prune(includePages: false);

        void Prune(bool includePages)
        {
            // Closing a tab hides a surface, and a surface being hidden is allowed to write to the document
            // (a board saves its camera). Without this, that write would re-enter mid-prune.
            if (pruning) return;
            if (controller == null) return;
            if (!WorkspaceOps.HasSurfaceOfKind(controller.Layout, SurfaceKind.Canvas)
                && !(includePages && WorkspaceOps.HasSurfaceOfKind(controller.Layout, SurfaceKind.Page)))
                return;

            var screen = ResolveMap();
            if (screen == null) return;

            pruning = true;
            try { controller.PruneSurfaces(screen.SurfaceExists); }
            finally { pruning = false; }
        }

        /// <summary>Found on demand and re-tried on every miss, like PageLinkBridge.ResolvePoiManager and for
        /// the second of its two reasons: a domain reload wipes a plain field while the component it names
        /// survives. Inactive included — the map screen is deactivated whenever another surface is showing,
        /// which is precisely when a board tab is open.</summary>
        MapScreenController ResolveMap()
        {
            if (map != null) return map;
            map = FindFirstObjectByType<MapScreenController>(FindObjectsInactive.Include);
            return map;
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (documentController != null) documentController.OnDocumentChanged -= PruneStructural;
            // The views are usually already DESTROYED by the time a rebuild calls this (they live under the
            // pane hierarchy DemolishForRebuild wipes), which takes their event fields with them — so the
            // null test skips them and nothing is leaked either way. Unity's lifetime-aware `==` is what
            // makes a destroyed component read as null here.
            if (observed != null)
                foreach (var view in observed)
                    if (view != null)
                    {
                        view.OnDocumentMutated -= PruneBlocks;
                        view.OnHistoryApplied -= ReshowSurfaces;
                    }
            observed = new List<DocumentPageView>();
            documentController = null;
        }
    }
}
