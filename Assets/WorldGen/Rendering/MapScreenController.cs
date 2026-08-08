using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Notes.Rendering;
using WorldGen.Workspace.Data;
using WorldGen.Workspace.Rendering;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns app-flow POLICY: computes which mutually-exclusive screen should be visible from app
    /// state (map exists? generating? editing a POI?) and delegates the actual show/hide to a
    /// ScreenSwitcher (the MECHANISM). Screens: Generation / Progress / Workspace.
    /// Also runs the world-generation coroutine.
    ///
    /// TASK 10c CHANGED WHAT THIS CLASS IS. It used to be the app's only navigation: six screens, and
    /// "opening a POI" meant switching the whole window to one of them. It now owns exactly two questions —
    /// is there a world yet, and is one being generated — and everything else it used to switch between is a
    /// TAB it opens through WorkspaceController.Open. The Open*/Close* methods below therefore do two things
    /// each: keep the editing state this class has always tracked (editingPoi/editingDungeon/parentTown/
    /// battleGridRoomId, which the screens themselves read through Bind), and open or close the tab that
    /// shows it.
    ///
    /// THE WORKSPACE IS DISCOVERED, NOT WIRED, and that is now a design choice rather than a transitional
    /// state. Task 11 put a WorkspaceBuilder into SampleScene.unity, so Shell() below finds one and every
    /// Open*/Close* works; between Task 10c and Task 11 it did not, and the app genuinely had a world map
    /// and no POI/interior/battle editing at all. Discovery stayed, because it is also what recovers the
    /// reference after a Play-mode domain reload and what lets a shell built later in the session be found
    /// without a restart — see Shell()'s own doc. Every caller still treats null as "do nothing", which is
    /// what keeps a bare scene (a test rig, a scene opened before the shell exists) inert rather than
    /// throwing.
    /// </summary>
    public class MapScreenController : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;
        public GenerationScreenUI generationScreen;
        public GenerationProgressUI progressScreen;
        // ── Map chrome. NO LONGER SWITCHED HERE (Task 10c): MapSurfaceHost owns all three now — it discovers
        // PoiEditPanel/MapLegendUI/MapToolbarUI by type (MapSurfaceHost.Rewire) and drives their active state
        // from whether the WorldMap surface currently owns a pane, which is the seam SurfaceRegistry's class
        // doc had been documenting as KNOWN since Task 9. Verified against the scene rather than assumed:
        // mapEditorPanelGO is the "MapEditorUI" object carrying PoiEditPanel and mapLegendUiGO is "MapLegend"
        // carrying MapLegendUI, i.e. exactly the two objects that discovery finds.
        //
        // The three fields are KEPT rather than deleted, deliberately. Deleting them would orphan three
        // Inspector assignments in SampleScene.unity, and they are the natural things to hand
        // WorkspaceBuilder.mapChrome/mapCamera as explicit overrides if type discovery ever picks the wrong
        // instance. Task 11's scene edit did NOT do that — discovery finds the same two objects these fields
        // name, so pinning them would only add a second place to keep correct — but the assignments are
        // there and ready if a scene ever has two of anything.
        public GameObject mapEditorPanelGO;
        public GameObject mapLegendUiGO;
        [Tooltip("Тулбар вкладок. Со времён Task 10c его видимостью управляет MapSurfaceHost (вкладка «Карта мира»), а не этот класс.")]
        public MapToolbarUI mapToolbar;

        [Header("POI editor screen")]
        public GameObject poiEditorScreenGO;
        public PoiEditorScreen poiEditorScreen;
        public PoiInfoPopup poiInfoPopup;

        [Header("Dungeon editor screen")]
        public GameObject dungeonEditorScreenGO;
        public DungeonEditorScreen dungeonEditorScreen;
        public DungeonManager dungeonManager;

        [Header("Battle grid screen")]
        public GameObject battleGridScreenGO;
        public BattleGridScreen battleGridScreen;

        Coroutine activeGeneration;
        PoiData editingPoi;
        InteriorData editingDungeon;
        // Ц2: the TOWN interior a currently-open BUILDING interior was drilled into from, or null when
        // editingDungeon is not a building-from-town (a top-level dungeon/building/settlement). Set only by
        // OpenBuildingInterior and cleared only by the back-to-town branch of CloseDungeonEditor (or by any
        // reset that also clears editingDungeon — OnWorldRegenerated, OpenDungeonEditor's stale-parent
        // guard). editingDungeon itself still drives DesiredScreen(); this is pure "where does back go" memory.
        InteriorData parentTown;
        int battleGridRoomId;        // 0 = no battle grid tab is open
        // The floor `battleGridRoomId` was resolved on, remembered at open time — see OpenBattleGrid for why
        // it cannot be re-read from the interior editor when the tab is closed.
        int battleGridFloorIndex;
        ScreenSwitcher switcher;

        void Awake()
        {
            progressScreen.OnCancelRequested += CancelGeneration;
        }

        void Start()
        {
            // Build the switcher FIRST — before any event subscription — so the invariant
            // "the switcher exists before any handler that could call RefreshScreenState runs"
            // is structural, not merely incidental to statement order.
            EnsureSwitcher();

            mapRenderer.OnWorldRegenerated += OnWorldRegenerated;
            if (poiEditorScreen != null) poiEditorScreen.OnCloseRequested = ClosePoiEditor;
            if (poiInfoPopup != null) poiInfoPopup.OnEditRequested = OpenPoiEditor;
            if (poiEditorScreen != null) poiEditorScreen.OnOpenDungeonRequested = OpenDungeonEditor;
            if (poiEditorScreen != null) poiEditorScreen.OnOpenPageRequested = OpenPageTab;
            if (dungeonEditorScreen != null) dungeonEditorScreen.OnCloseRequested = CloseDungeonEditor;
            if (battleGridScreen != null) battleGridScreen.OnCloseRequested = CloseBattleGrid;
            if (dungeonEditorScreen != null) dungeonEditorScreen.OnOpenBattleGridRequested = OpenBattleGrid;
            if (dungeonEditorScreen != null) dungeonEditorScreen.OnOpenBuildingRequested = OpenBuildingInterior;
            // Ц2 Task 6: building-interior integrity at the node-delete/regenerate seams — DungeonEditorScreen
            // has no DungeonManager reference of its own, see its own doc on these four fields.
            if (dungeonEditorScreen != null) dungeonEditorScreen.SettlementBuildingHasInterior = SettlementBuildingHasInterior;
            if (dungeonEditorScreen != null) dungeonEditorScreen.RemoveBuildingInterior = RemoveBuildingInterior;
            if (dungeonEditorScreen != null) dungeonEditorScreen.SettlementHasBuildingInteriors = SettlementHasBuildingInteriors;
            if (dungeonEditorScreen != null) dungeonEditorScreen.RemoveAllBuildingInteriors = RemoveAllBuildingInteriors;

            RefreshScreenState();
        }

        /// <summary>Builds `switcher` if it is not already there, and is called from RefreshScreenState as
        /// well as from Start — a lazy rebuild, not merely a null guard, and the difference matters.
        ///
        /// `switcher` is a plain non-[SerializeField] field, the exact construct a Play-mode domain reload
        /// resets while every GameObject it names survives (this arc's recurring defect family; see
        /// WorkspaceController.shellSuppressed's doc for the running count). Unity re-invokes Awake on a
        /// surviving component after such a reload but NOT Start, so a switcher built only in Start would stay
        /// null for the rest of the session and every RefreshScreenState would throw. Rebuilding is genuinely
        /// cheap and genuinely correct here because every input is a SERIALIZED field (generationScreen,
        /// progressScreen) or a fresh lookup (Shell().ShellRoot), all of which DO survive the reload — so
        /// the rebuilt switcher is identical to the original rather than a degraded stand-in. A bare
        /// `if (switcher == null) return;` guard would instead leave the app permanently on whatever screen it
        /// happened to be showing, which is the "live but blind" outcome WorkspaceBuilder.Awake's own comment
        /// argues against.
        ///
        /// One consequence to know about: `Current` restarts at its default after a rebuild, so the first
        /// Show() post-reload re-applies every member's active state unconditionally. That is the desired
        /// direction — the reload cannot have changed which screen SHOULD be visible, and re-asserting is what
        /// ScreenSwitcher exists to do.</summary>
        void EnsureSwitcher()
        {
            // Rebuilt — not merely built once — when the shell it was baked against has changed identity,
            // INCLUDING null -> non-null. Shell()'s own doc claims a shell built later in the session gets
            // picked up without a restart, and a plain `switcher != null` early-out would quietly falsify that:
            // the Workspace member array baked while no shell existed stays empty for the whole session, so
            // the shell's own canvas (navigator + tab strips) would never be deactivated on
            // Generation/Progress. The after-show hook resolves Shell() freshly every call and so would still
            // hide the SURFACES — which is exactly the half-fixed state that reads as "the form floats against
            // a live shell", the defect Step 1 exists to close.
            var shellRoot = Shell()?.ShellRoot;
            if (switcher != null && ReferenceEquals(switcherShellRoot, shellRoot)) return;

            // AppScreen.Workspace's member is the shell's own canvas — that is what extends this class's
            // deactivate-everything-else guarantee to the workspace instead of leaving it merely unpopulated
            // (Task 10c Step 1). An EMPTY array when no workspace exists, not a null entry: ScreenSwitcher
            // iterates the array and null-checks each element, so both are safe, but an empty array says
            // "this screen has no members here" while a null entry reads as a wiring mistake.
            //
            // The four ex-screens (map chrome, POI editor, interior editor, battle grid) are deliberately
            // absent from this table: they are surfaces now, and their GameObjects belong to MapSurfaceHost /
            // ScreenSurfaceHosts. Listing them here too would give each object two independent owners with
            // opposite opinions about when it should be on — precisely the KNOWN SEAM this task closes.
            switcherShellRoot = shellRoot;
            switcher = new ScreenSwitcher(
                new Dictionary<AppScreen, GameObject[]>
                {
                    { AppScreen.Generation, new[] { generationScreen.gameObject } },
                    { AppScreen.Progress,   new[] { progressScreen.gameObject } },
                    { AppScreen.Workspace,  shellRoot != null ? new[] { shellRoot } : new GameObject[0] },
                },
                // The second half of the same guarantee, and the half a GameObject toggle cannot reach: the
                // workspace's SURFACES live outside the shell hierarchy (the map camera and its floating
                // chrome, the five ex-screen canvases), so deactivating the shell's canvas alone would leave
                // them painting over Generation/Progress. See WorkspaceController.SetShellActive.
                screen => Shell()?.SetShellActive(screen == AppScreen.Workspace));
        }

        /// <summary>The ShellRoot `switcher`'s member table was built against, so EnsureSwitcher can tell a
        /// stale table from a current one. Plain field, wiped by a domain reload alongside `switcher` itself —
        /// which is harmless precisely because both are wiped together, so the pair is never half-stale.</summary>
        GameObject switcherShellRoot;

        /// <summary>The workspace shell, discovered rather than wired (see this class's own doc for why
        /// that outlived the window in which it was the only option). Re-searched on every miss rather than
        /// once: that is what recovers the reference after a Play-mode domain reload wipes this field, and
        /// what lets a shell built later in the session be picked up without a restart.
        /// FindObjectsInactive.Include because the shell's component sits on a GameObject that the switcher
        /// itself may have deactivated.
        ///
        /// WorkspaceController, not WorkspaceBuilder, is what this looks for — and the two are on the same
        /// GameObject, so it makes no difference to the search, but it does to the rebuild: Task 11's shell
        /// rebuild destroys the canvas and REUSES the controller component, so a reference found here stays
        /// valid across a recompile rather than pointing at a corpse.
        ///
        /// Returns null in a scene with no workspace, and EVERY caller treats that as "do nothing".</summary>
        WorkspaceController Shell()
        {
            if (workspace != null) return workspace;
            workspace = FindFirstObjectByType<WorkspaceController>(FindObjectsInactive.Include);
            return workspace;
        }

        WorkspaceController workspace;

        void OnWorldRegenerated()
        {
            // PRUNE BY KIND, not by closing the tabs the fields happen to name. An earlier revision closed
            // four named SurfaceRefs derived from editingPoi/editingDungeon/parentTown/battleGridRoomId, and
            // that misses every DUPLICATE — which ordinary navigation produces: open a battle grid for room 1,
            // click back to the interior's tab (it stays live behind, by design), open a battle grid for room
            // 2, and BOTH grid tabs are open while the fields name only one. Same for two POI-editor tabs,
            // two dungeons, or any surface opened in both panes (CloseSurface closes the first match only, by
            // design). A survivor points at a destroyed world and its next Show re-binds to nothing, leaving a
            // stale screen under a live tab — exactly what the close-before-clear ordering existed to prevent.
            //
            // WorkspaceOps.PruneMissing is the designed answer and had no caller until now. The predicate is
            // KIND-BASED rather than "does this id still resolve": at this moment PoiManager/DungeonManager
            // may or may not already have been cleared by their own handlers of the same regeneration, and
            // Unity gives no ordering guarantee between them — a resolution-based predicate would keep or drop
            // tabs depending on which handler ran first. "A fresh world drops any open editor" is the rule
            // this method has always stated, and it is order-independent.
            //
            // The TABS are closed here, by PruneMissing; the four field clears below drop only the ids that
            // NAME them, which is why no CloseSurfaceTab call is left in this method.
            Shell()?.PruneSurfaces(SurvivesWorldChange);

            editingPoi = null; // a fresh world drops any open POI editor
            editingDungeon = null; // a fresh world drops any open dungeon editor
            parentTown = null;   // ...and any building-from-town back-target it was carrying
            battleGridRoomId = 0;   // ...and the room id that named the battle grid's tab
            // ...and the request to CREATE a world, which a world arriving has just satisfied. The asymmetry
            // of leaving this one out was the tell: every other world-scoped field above is cleared here, and
            // this is the only one that gates the whole SCREEN.
            //
            // THE DEFECT IT CLOSES — a regression this arc introduced, whose two halves lived in different
            // tasks, which is why no per-task review could see it. `newWorldRequested` and DesiredScreen's
            // `|| newWorldRequested` clause came from Task 10c; ProjectMenuBar.LoadFrom's project-switch
            // bracket came from Task 11. Neither owned the pair. «Открыть…» is offered unconditionally, so it
            // is live while the generation form is up:
            //   «Создать новый мир…» -> confirm -> the form appears -> «Файл -> Открыть…» -> project B loads
            //   COMPLETELY AND CORRECTLY (bracketed, layout restored, both keys on B) -> and then
            //   DesiredScreen still reads newWorldRequested and returns Generation.
            // The DM opens a project and the form stays on screen, with their freshly loaded world, POIs,
            // notes and restored tabs behind a deactivated shell. Recoverable only through «Вернуться к
            // текущему миру», which nobody would associate with "I just opened a project". At the merge-base
            // DesiredScreen had no such clause, so a successful load made hasMap true and left the form
            // immediately — impossible there, reachable here.
            //
            // SAFE ON THE GENERATION PATH BY CONSTRUCTION, not by luck: OnWorldRegenerated has exactly two
            // raise sites (WorldMapRenderer.cs:294 and :349), both meaning "a new world now exists", and
            // StartGeneration has already cleared this flag before RunGeneration reaches either. So this line
            // is a no-op there and a fix on the load path.
            //
            // ONE REAL BEHAVIOUR CHANGE ON THE FAILURE PATH, and it is the trade this line accepts rather
            // than an oversight. LoadFromCells — which raises this event — is the FIRST loader inside
            // LoadFrom's try, so on a load that throws AFTER it, the flag is already cleared and the DM lands
            // on the workspace over a half-replaced world instead of back on the generation form. That is the
            // better outcome, and for the same reason AbortProjectSwitch gives for its own choices: the world
            // HAS been partly replaced, so the honest screen is the one that shows it. Parking the DM on a
            // generation form they did not ask to return to would hide the damage behind the very screen this
            // fix exists to stop them being stranded on — the same defect, on the other path.
            newWorldRequested = false;
            RefreshScreenState();
        }

        /// <summary>Which surfaces outlive a world regeneration: the document's pages (they are notes, not
        /// world state — the whole point of the world/session separation) and the world map itself, which is
        /// the tab the DM lands back on. Every ex-screen kind describes a POI or an interior of the world that
        /// was just replaced, so all five go.
        ///
        /// A PAGE TAB MUST ALSO STILL NAME A PAGE, and this half was missing until the DM reported the ghost
        /// it let through. Restoring the layout at startup deliberately prunes NOTHING — nothing has loaded
        /// yet, so a real existence check would answer "no" to every tab and delete the whole layout on every
        /// launch (WorkspaceController.RestoreFromPrefs states that trade explicitly). But the notes document
        /// is not loaded, it is BUILT: NotesDocumentController.Awake makes a fresh «Сессия 1» with a fresh
        /// GUID on every single Play. So a Page tab restored from a previous run names a page id that no
        /// longer exists anywhere, while still carrying its old TITLE — and it looked entirely right, because
        /// PageSurfaceHost falls back to whatever page is active. The DM saw it as: a «Сессия 1» tab that
        /// opens a SECOND «Сессия 1» tab when its own row is clicked in the navigator. Two tabs, one name,
        /// and only one of them attached to anything.
        ///
        /// This is the first moment the question can be asked honestly, which is why it is asked here: a world
        /// arriving is long past every Awake, so the document is real. It also stays ORDER-INDEPENDENT, which
        /// is the property this predicate's caller requires — a regeneration does not touch the notes document
        /// at all, so no other handler of the same event can change this answer underneath us. That is exactly
        /// what is NOT true of POIs and interiors, whose managers are cleared by their own handlers of this
        /// same event in an order Unity does not define; those five kinds are therefore still refused by KIND,
        /// never by resolution.
        ///
        /// STILL NOT A SUBSTITUTE FOR THE PROJECT-LOAD PREDICATE. A project load also replaces the document
        /// wholesale (ProjectMenuBar.LoadFrom -> NotesDocumentController.LoadDocument), and it must drop POI
        /// and interior tabs by resolution rather than keep them by kind. SurfaceExists is that predicate, and
        /// WorkspaceController.EndProjectSwitch applies it.</summary>
        bool SurvivesWorldChange(SurfaceRef s)
        {
            if (s == null) return false;
            if (s.Kind == SurfaceKind.WorldMap) return true;
            // Deliberately SurfaceExists' own branches rather than a second copy of the lookup — the same
            // argument that method's doc makes for itself: "the tab survives" and "the page can be opened"
            // must not be able to disagree.
            //
            // Page and Canvas both live in the notes document, which a REGENERATION does not touch — so both
            // survive it, and both are checked by resolution rather than kept by kind, so a tab can never name
            // something that is gone. (The five world kinds below are still refused by KIND: their managers are
            // cleared by sibling handlers of this same event, in an order Unity does not define.)
            if (s.Kind == SurfaceKind.Page || s.Kind == SurfaceKind.Canvas) return SurfaceExists(s);
            return false;
        }

        // ── The project-switch seam (Task 11) ────────────────────────────────────────────────────────
        //
        // ProjectMenuBar owns the file dialog and the load; this class owns the discovered
        // WorkspaceController (Shell()) and every lookup that can turn a stored SurfaceRef back into an
        // object (Pois/ResolveInterior/Notes, the same reversals RebindSurface uses). Routing the three calls
        // through here rather than giving ProjectMenuBar its own shell discovery and its own copy of the
        // resolution rules keeps both of those in one place — the same argument OpenPoiFromMap's doc makes
        // for living here rather than in PoiInteractionController.

        /// <summary>Called by ProjectMenuBar immediately before a project load starts touching the world.
        /// See WorkspaceController.BeginProjectSwitch for the collision it prevents.</summary>
        public void BeginProjectSwitch() => Shell()?.BeginProjectSwitch();

        /// <summary>Called by ProjectMenuBar once the load has finished SUCCESSFULLY, with the path just
        /// opened. The method-group argument is what supplies the "does this still exist" answer that
        /// WorkspaceController.RestoreFromPrefs could not ask at startup.</summary>
        public void EndProjectSwitch(string projectPath) => Shell()?.EndProjectSwitch(projectPath, SurfaceExists);

        /// <summary>Called by ProjectMenuBar instead of EndProjectSwitch when a loader threw part-way — see
        /// WorkspaceController.AbortProjectSwitch for why the two must not be the same call.</summary>
        public void AbortProjectSwitch() => Shell()?.AbortProjectSwitch();

        /// <summary>«Сохранить как…»: the tabs on screen now belong to the new file. No load, so no restore
        /// and no prune — see WorkspaceController.RekeyTo.</summary>
        public void RekeyWorkspaceTo(string projectPath) => Shell()?.RekeyTo(projectPath);

        /// <summary>Does the object a stored tab names still exist? The prune predicate
        /// WorkspaceController.EndProjectSwitch applies to a restored layout.
        ///
        /// EVERY BRANCH REUSES THE LOOKUP RebindSurface ALREADY USES for that kind, and that is a
        /// requirement, not tidiness: this method decides whether a tab is KEPT, and RebindSurface decides
        /// what the tab then SHOWS. If they disagreed in either direction the result is a defect with no
        /// visible cause — a tab kept here but unresolvable there sits under a screen still bound to
        /// something else (RebindSurface's own "wrong but harmless" early return), and a tab dropped here
        /// that would have resolved is a place the DM had open and simply lost.
        ///
        /// WorldMap is always true: there is exactly one, it has no id, and the load just produced it.
        /// A malformed id never reaches here — WorkspaceOps.Restore applies SurfaceIds.IsWellFormed first
        /// (see its doc for the order and why) — so these branches only ever answer the existence question.
        ///
        /// An unknown kind returns FALSE, the opposite of SurfaceIds.IsWellFormed's default: there the
        /// question is "could this id be malformed", and an unencoded kind honestly cannot be; here it is
        /// "can this app show this", and for a kind nothing in this switch can resolve, the honest answer is
        /// no. A new SurfaceKind therefore drops its tabs on a project load until someone adds it here —
        /// visible, and recoverable by reopening the tab, unlike a kept tab that shows the wrong thing.</summary>
        public bool SurfaceExists(SurfaceRef s)
        {
            if (s == null) return false;
            switch (s.Kind)
            {
                case SurfaceKind.WorldMap:
                    return true;

                case SurfaceKind.Page:
                    // NotesDocOps.FindPage, the same pure lookup DocumentPageView.ShowPage resolves the tab's id
                    // through — so "the tab survives" and "the page can be shown" cannot disagree.
                    //
                    // Explicit null checks rather than `Notes()?.DocumentController?.Document`: both are
                    // UnityEngine.Objects, whose overloaded `==` reports a DESTROYED reference as null, and
                    // `?.` bypasses that overload — the same idiom, and the same reason, as
                    // NavigatorView.Rebuild's own read of documentController.
                    var notes = Notes();
                    var docController = notes != null ? notes.DocumentController : null;
                    var doc = docController != null ? docController.Document : null;
                    return doc != null && NotesDocOps.FindPage(doc, s.Id) != null;

                case SurfaceKind.Canvas:
                {
                    // Same lookup chain as Page above, and the same reason for the explicit null checks rather
                    // than `?.`: both are UnityEngine.Objects, whose overloaded == reports a DESTROYED
                    // reference as null while `?.` bypasses that overload.
                    //
                    // The KIND is checked, not merely the block's existence: a row the DM turned into an
                    // ordinary Item is no longer a board, and its tab must close rather than open on nothing.
                    var canvasNotes = Notes();
                    var canvasController = canvasNotes != null ? canvasNotes.DocumentController : null;
                    var canvasDoc = canvasController != null ? canvasController.Document : null;
                    if (canvasDoc == null) return false;
                    var block = NotesDocOps.FindBlock(canvasDoc, s.Id, out _);
                    return block != null && block.Kind == BlockKind.Canvas;
                }

                case SurfaceKind.PoiEditor:
                    return Pois()?.GetPoiById(s.Id) != null;

                case SurfaceKind.Settlement:
                case SurfaceKind.BuildingInterior:
                case SurfaceKind.Dungeon:
                    return ResolveInterior(s.Id) != null;

                case SurfaceKind.BattleGrid:
                    return BattleGridExists(s.Id);

                default:
                    return false;
            }
        }

        /// <summary>The three checks RebindBattleGrid makes before it binds — the interior resolves, the
        /// floor index is in range, and the room is on that floor — asked as a question instead of performed
        /// as a bind. Written out rather than shared with RebindBattleGrid because that method interleaves
        /// them with the state assignments they guard; extracting a common helper would mean threading four
        /// out-parameters through it for no gain in either place. The RULES are what must agree, and they are
        /// listed in the same order here so a diff of the two is a diff of three lines.</summary>
        bool BattleGridExists(string id)
        {
            if (!SurfaceIds.TryParseBattleGrid(id, out string interiorId, out int floorIndex, out int roomId))
                return false;
            var interior = ResolveInterior(interiorId);
            if (interior == null) return false;
            if (floorIndex < 0 || floorIndex >= interior.Floors.Count) return false;
            return interior.Floors[floorIndex].GetRoom(roomId) != null;
        }

        /// <summary>The live notes document's owner, discovered on every miss exactly like Pois() and Shell()
        /// and for the same two reasons: no Inspector slot without a scene edit, and a domain reload wipes
        /// the cached reference while the component survives. Only SurfaceExists needs it — every other path
        /// in this class deals in world objects, not pages.</summary>
        NotesRootBuilder Notes()
        {
            if (notesRoot != null) return notesRoot;
            notesRoot = FindFirstObjectByType<NotesRootBuilder>(FindObjectsInactive.Include);
            return notesRoot;
        }

        NotesRootBuilder notesRoot;

        /// <summary>The app's menu bar, discovered on every miss exactly like Shell(), Pois() and Notes(),
        /// and for the same two reasons: no Inspector slot without a scene edit, and a domain reload wipes
        /// the cached reference while the component survives. Only RunGeneration needs it — to tell the bar
        /// that the project it names no longer describes the world on screen.
        ///
        /// Null in a scene without one, and the single caller treats that as "do nothing", which is correct:
        /// a scene with no menu bar has no «Сохранить» to mislead.</summary>
        ProjectMenuBar Menu()
        {
            if (menuBar != null) return menuBar;
            menuBar = FindFirstObjectByType<ProjectMenuBar>(FindObjectsInactive.Include);
            return menuBar;
        }

        ProjectMenuBar menuBar;

        // ── Surfaces: opening and closing the tabs that used to be screens (Task 10c Step 2) ────────
        //
        // The Open* methods keep every line of the state-keeping they always had — Bind, the resolve-before-
        // mutate guards, parentTown bookkeeping — and only replace their final "switch the window to my
        // screen" with "open (or focus) my tab". That split is why each one still ends in RefreshScreenState:
        // the SCREEN question ("is there a world at all?") is unchanged and still this class's, while WHICH
        // surface is visible is now WorkspaceController's.
        //
        // inOtherPane is FALSE everywhere here except OpenPoiFromMap, deliberately, and that is not an
        // oversight to revisit: the spec reserves the other pane for three explicit gestures — Shift+Enter in
        // Ctrl+K, «Открыть рядом» in the navigator, and double-clicking a place on the map. The third one
        // moved INTO this file in Task 10e (it used to open a page, and the page path lived here too); the
        // gesture is still wired in PoiInteractionController, only its destination is here. Everything else
        // in this file is a drill-down from something already open — the POI editor's «КАРТА ЛОКАЦИИ», a
        // room's «Боевая карта», a building inside a town — where the user is going deeper into one line of
        // work, not putting two things side by side.

        /// <summary>Opens the POI editor for a point (single source of truth for the info-popup's
        /// «Редактировать» button). Hides the popup, binds the editor and opens its TAB — as of Task 10c it no
        /// longer takes over the window, which is the «редактирование точки интереса происходит не в отдельной
        /// вкладке, а раскрывается на весь экран» the user reported at the Task 10a checkpoint.
        ///
        /// IT NO LONGER CREATES A PAGE FOR THE POI (Task 10e). That call existed to make the POI appear in
        /// the navigator's «Мир» group, which was then computed from NotesPage.Bound; «Мир» is now the
        /// world's contents and reads no pages at all (NavigatorTree's class doc), so the page had nothing
        /// left to do but accumulate — one note per place the DM merely glanced at, against their own ruling
        /// that notes are authored deliberately as separate objects.
        ///
        /// This is now the SAME destination double-clicking the POI on the map reaches (OpenPoiFromMap below,
        /// which delegates here through OpenPoiEditorIn) and the same one the navigator and Ctrl+K open —
        /// «двойное нажатие и выбор в навигаторе должен выдавать то же самое меню». The gestures differ only
        /// in which pane they land in.</summary>
        public void OpenPoiEditor(PoiData poi) => OpenPoiEditorIn(poi, inOtherPane: false);

        /// <summary>Opens a NOTES PAGE in a tab of the focused pane — the same call NavigatorView makes for a
        /// row under «Заметки» (NavigatorView.cs:536) and the same SurfaceRef DocumentPageView routes an
        /// inline [[page:…]] link through.
        ///
        /// WHY THE POI EDITORS COME THROUGH HERE (two-panes Task 3). PoiEditPanel and PoiEditorScreen used to
        /// open a place's note by calling NotesDocumentController.OpenPage directly — a call that moved a
        /// document-wide "active page" pointer and left the WORKSPACE, which owns which tab shows what,
        /// entirely out of the loop: no tab appeared, and whichever pane happened to have a Page tab active
        /// silently swapped its content. There is no such pointer any more, so a page can only be opened the
        /// way every other surface is — by putting it in a tab. This method is that door, and it is here
        /// because this class already owns the workspace reference (Shell()) and every other Open* gesture.
        ///
        /// A blank id is a no-op rather than an empty tab: WorkspaceOps.Open would happily create one, and a
        /// tab that can never resolve is worse than no tab (see WorkspaceOps.PruneMissing's own doc).</summary>
        public void OpenPageTab(string pageId, string title)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            OpenSurfaceTab(new SurfaceRef { Kind = SurfaceKind.Page, Id = pageId }, title);
        }

        /// <summary>Closes the POI editor's tab. What the user sees next is whatever tab the workspace makes
        /// active in its place (WorkspaceOps.FixActiveIndexAfterRemoval decides), NOT "the world map" as the
        /// pre-Task-10c version of this comment promised — the map is one tab among many now and may not even
        /// be in the same pane.
        ///
        /// CLEARS ONLY WHAT IT CLOSED. CloseSurfaceTab runs a full SyncSurfaces, which activates the
        /// neighbouring tab and (since Task 10c's rebind hook) re-binds this same screen to it — so with two
        /// POI-editor tabs open, `editingPoi` is already the OTHER one by the time this line runs, and an
        /// unconditional null would blank state that belongs to a tab still on screen. Same discipline in
        /// CloseDungeonEditor and CloseBattleGrid below.</summary>
        public void ClosePoiEditor()
        {
            var closing = editingPoi;
            CloseSurfaceTab(PoiEditorSurface(closing));
            if (ReferenceEquals(editingPoi, closing)) editingPoi = null;
            RefreshScreenState();
        }

        /// <summary>Opens the cave-dungeon editor for a POI (get-or-create its dungeon) in its own tab. The
        /// POI editor's own tab is left OPEN behind it (editingPoi stays set, as it always did), so closing
        /// this one lands back on the POI editor exactly as before — that continuity now comes from the tab
        /// still existing rather than from a screen underneath.
        ///
        /// One method, two SurfaceKinds: a settlement POI yields InteriorKind.Settlement and everything else
        /// Dungeon (Profiles.InteriorKindForPoiType), and InteriorSurface below maps that to Settlement or
        /// Dungeon. Both are served by the same DungeonEditorScreen object — see ScreenSurfaceHosts' class doc
        /// for why that sharing has to be resolved inside the host rather than by registering three of
        /// them.</summary>
        public void OpenDungeonEditor(PoiData poi)
        {
            if (poi == null || dungeonManager == null) return;
            // Stale-parent guard (Ц2): every entry into the editor FROM THE MAP is a top-level interior —
            // clear any building-from-town back-target a PREVIOUS session inside a different (or the same)
            // POI's building left behind. Only OpenBuildingInterior below is allowed to set parentTown.
            parentTown = null;
            var kind = Profiles.InteriorKindForPoiType(poi.Type) ?? InteriorKind.Dungeon;
            editingDungeon = dungeonManager.GetOrCreateForPoi(poi.Id, kind);
            // A freshly created settlement is an empty shell — generate its map once, deterministically from
            // a POI-derived seed. Dungeons/buildings are generated by their own editor path; a settlement has
            // no floor-add flow, so it is generated here on first open. Generate() sets Floors[0].SettlementParams
            // (incl. HasWall), which AddRange carries across — the fence itself is derived from the rooms/roads,
            // not stored (InteriorFloor.Wall was removed), so there is no separate wall assignment.
            if (editingDungeon.Kind == InteriorKind.Settlement && editingDungeon.Floors.Count == 0)
            {
                var (size, activeN) = SettlementDefaults(poi.Type);
                var cfg = new WorldGen.Generation.SettlementConfig
                {
                    Seed = SettlementSeed(poi),
                    Size = size,
                    ActiveBuildings = activeN,
                    HasWall = true,
                };
                editingDungeon.Floors.AddRange(WorldGen.Generation.SettlementGenerator.Generate(cfg, poi.Id).Floors);
            }
            if (dungeonEditorScreen != null)
                dungeonEditorScreen.Bind(editingDungeon, roomsWithInterior: RoomsWithInteriorFor(editingDungeon));
            OpenSurfaceTab(InteriorSurface(editingDungeon), InteriorMapTitle(poi));
            RefreshScreenState();
        }

        /// <summary>«[Название]:карта» — the DM's ruling, verbatim, on what the interior-map tab is called.
        ///
        /// THE DEFECT: a point of interest opens its EDITOR in one tab, and «КАРТА ЛОКАЦИИ» inside that editor
        /// opens the point's INTERIOR MAP as a second tab. Both used PoiTitle(poi), so the strip showed two
        /// tabs with the identical name and they read as one. The editor's tab keeps the bare name (see
        /// OpenPoiEditorIn's own OpenSurfaceTab call) — it is the point itself; only the map is qualified.
        ///
        /// NO SPACE AROUND THE COLON, and this is copied from the ruling rather than styled: «[Название]:карта»
        /// is what the DM wrote. Do not "fix" it to «: карта» or «— карта».
        ///
        /// THE SUFFIX IS NOT APPLIED ANYWHERE ELSE, deliberately. OpenBuildingInterior titles a building's tab
        /// with the ROOM's own name and OpenBattleGrid with the room's name or «Бой: комната N» — neither
        /// collides with a POI's name, and neither was part of the ruling. CloseDungeonEditor's «← Город»
        /// re-open passes InteriorTitle(town) («Город»/«Здание»/«Подземелье») for the case where the DM closed
        /// the town's tab by hand; it cannot use this, because the POI's real name is not reachable from an
        /// InteriorData (InteriorTitle's own doc says why) — so a town tab re-created that way is named for its
        /// KIND, not «[Название]:карта». Recorded rather than papered over.
        ///
        /// ONLY APPLIES TO A TAB BEING CREATED. WorkspaceOps.Open's R1 activates an existing tab without
        /// touching its Title, and WorkspacePrefs stores the title it was created with — so interior-map tabs
        /// that already exist (including ones restored from a previous session) keep the old bare name until
        /// they are closed and reopened. Renaming the POI afterwards leaves it stale in the same way, for the
        /// same reason: nothing calls ISurfaceHost.TitleFor yet (its own doc says so, and
        /// ScreenSurfaceHost.TitleFor returns "" by design). A live title-refresh path is a separate piece of
        /// work, not one line at this site.</summary>
        static string InteriorMapTitle(PoiData poi) => PoiTitle(poi) + ":карта";

        /// <summary>Ц2, Task 5: room ids of `town`'s own buildings that already have their own interior on
        /// file — feeds DungeonFlatRenderer's has-interior corner mark. Settlement-only (null for a
        /// dungeon/building bind, same "omitted = no mark" contract DungeonEditorScreen.Bind documents) so
        /// callers can pass this unconditionally without their own Kind check.</summary>
        HashSet<int> RoomsWithInteriorFor(InteriorData town) =>
            town != null && town.Kind == InteriorKind.Settlement && dungeonManager != null
                ? InteriorOps.RoomsWithInterior(dungeonManager.GetAll(), town.OwnerPoiId)
                : null;

        // ── Ц2 Task 6: building-interior integrity, wired to DungeonEditorScreen's four callbacks above.
        // All four resolve against `editingDungeon` (the currently-open town) — the same interior
        // DungeonEditorScreen itself is bound to, so there is nothing for the callback to be passed that
        // this screen doesn't already agree on.

        /// <summary>RequestDeleteSelected's confirm gate: does settlement building `roomId` (of the OPEN
        /// town) already own an interior on file?</summary>
        bool SettlementBuildingHasInterior(int roomId) =>
            editingDungeon != null && editingDungeon.Kind == InteriorKind.Settlement && dungeonManager != null
            && InteriorOps.FindBuildingInterior(dungeonManager.GetAll(), editingDungeon.OwnerPoiId, roomId) != null;

        /// <summary>Node deletion: removes ONLY `roomId`'s own interior — the town and every sibling
        /// building's interior are untouched.</summary>
        void RemoveBuildingInterior(int roomId)
        {
            if (editingDungeon == null || dungeonManager == null) return;
            dungeonManager.RemoveOwnedInterior(editingDungeon.OwnerPoiId, roomId);
        }

        /// <summary>RegenerateSettlement's confirm gate: does the OPEN town own at least one building
        /// interior?</summary>
        bool SettlementHasBuildingInteriors() =>
            editingDungeon != null && editingDungeon.Kind == InteriorKind.Settlement && dungeonManager != null
            && dungeonManager.HasBuildingInteriors(editingDungeon.OwnerPoiId);

        /// <summary>«Сгенерировать заново»: removes EVERY building interior of the OPEN town (the town's
        /// own interior is untouched — DoRegenerateSettlement replaces its floor in place) and returns the
        /// fresh has-interior mark via the SAME RoomsWithInteriorFor path Bind uses — always empty right
        /// after a full building-interior sweep, but recomputed rather than assumed, so the screen never has
        /// to know the cleanup's shape itself.</summary>
        HashSet<int> RemoveAllBuildingInteriors()
        {
            if (editingDungeon == null || dungeonManager == null) return new HashSet<int>();
            dungeonManager.RemoveBuildingInteriors(editingDungeon.OwnerPoiId);
            // RoomsWithInteriorFor returns null for a non-settlement Kind — can't happen for editingDungeon
            // here (this callback is only ever meaningful mid-DoRegenerateSettlement, which is itself
            // Kind==Settlement-gated), but the null-coalesce keeps this callback's contract exactly
            // "never null" regardless, since the caller's own `?? new HashSet<int>()` only guards a null
            // DELEGATE, not a null RETURN VALUE.
            return RoomsWithInteriorFor(editingDungeon) ?? new HashSet<int>();
        }

        /// <summary>Stable seed for a settlement's first generation, derived from the POI's GUID id. NOT
        /// string.GetHashCode — that is randomized per process in modern .NET, so a town would regenerate
        /// differently on every launch even though Floors.Count==0 never runs twice for the SAME process.</summary>
        static int SettlementSeed(PoiData poi)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in poi.Id) h = h * 31 + c;
                return h;
            }
        }

        /// <summary>What a settlement POI generates as before the DM touches anything: a SIZE CLASS and how
        /// many of its buildings start out active (a real place with a name/description/interior, as opposed
        /// to a decorative dummy).
        ///
        /// ONE ROW, NOT A PER-TYPE TABLE. PoiType.Village no longer exists (task A removed it) and the type
        /// that remains — City — is the only settlement type there is, so switching on it would be a table
        /// with one meaningful row and a `default` nothing reaches. Size is what distinguishes a hamlet from a
        /// capital now, and the DM sets it in the editor; a fresh town starts Medium so it is obviously
        /// adjustable in both directions.
        ///
        /// HasWall is likewise no longer derived from the POI type (see the call site, which passes true): the
        /// wall is the DM's own «Со стеной» choice, stored on SettlementParams.HasWall, and deriving a default
        /// from a type that no longer varies would just be a second opinion about it.
        ///
        /// `type` IS READ BY NOTHING BELOW — kept in the signature anyway rather than dropped, because the
        /// one call site already has poi.Type sitting in hand (it is reading poi.* to build the whole config)
        /// and passing it costs nothing; dropping the parameter would be a pure signature-churn edit with no
        /// behaviour change, not a bug fix, so it is left alone until an actual second settlement POI type
        /// gives it something to read again.</summary>
        static (SettlementSize size, int active) SettlementDefaults(PoiType type) => (SettlementSize.Medium, 5);

        /// <summary>Wired to DungeonEditorScreen.OnCloseRequested (the top-strip back button) — the SAME
        /// single handler for both of its meanings. When a building interior is open (parentTown != null),
        /// «← Город» goes back up one level; otherwise («← Назад» on a top-level dungeon/building/settlement)
        /// the interior closes entirely.
        ///
        /// WHAT «← Город» MEANS UNDER TABS, decided here rather than left ambiguous: the building's tab is
        /// CLOSED and the town's is opened (which, since the town's tab is still there from
        /// OpenDungeonEditor, means FOCUSED — WorkspaceOps.Open's R1 reopen-does-not-duplicate rule). Chosen
        /// over re-targeting the one tab in place because the two are genuinely different places with
        /// different names, and a tab that silently changed what it points at would leave the tab strip
        /// lying about where the user is. Closing rather than leaving both open matches what the button says:
        /// «← Город» is going back, not opening a second thing.
        ///
        /// ORDER MATTERS: close the building's tab BEFORE editingDungeon is reassigned, since
        /// InteriorSurface(editingDungeon) is what names it.</summary>
        public void CloseDungeonEditor()
        {
            // Both captured BEFORE the close: CloseSurfaceTab runs a full SyncSurfaces, and Task 10c's rebind
            // hook lets that re-point `editingDungeon`/`parentTown` at whatever tab becomes active — including,
            // very commonly, the town's own tab, which sets parentTown to null. Reading the fields after the
            // close would then take this method to the wrong place, or to null.
            var closing = editingDungeon;
            var town = parentTown;

            if (town != null)
            {
                CloseSurfaceTab(InteriorSurface(closing));
                // The rebind may already have done this — activating the town's tab binds it. Guarded rather
                // than repeated: DungeonEditorScreen.Bind rebuilds a settlement's whole ~40-node canvas and
                // discards the DM's selection, so a redundant second one is visible, not just wasted.
                if (!ReferenceEquals(editingDungeon, town))
                {
                    editingDungeon = town;
                    // Recomputed, not carried over from the original OpenDungeonEditor bind — the DM may just
                    // have opened a building interior for the FIRST time (OpenBuildingInterior generates and
                    // persists it on demand), which grows the set between the town's original bind and this
                    // return trip.
                    if (dungeonEditorScreen != null)
                        dungeonEditorScreen.Bind(town, roomsWithInterior: RoomsWithInteriorFor(town));
                }
                parentTown = null;
                // Re-Opened, not merely re-Bound: the town's tab normally still exists, so this focuses it.
                // The title is only used if it does not (the DM closed it from the strip while a building
                // interior was open) — re-opening it beats silently landing on whatever tab happened to be
                // next.
                OpenSurfaceTab(InteriorSurface(town), InteriorTitle(town));
                RefreshScreenState();
                return;
            }

            CloseSurfaceTab(InteriorSurface(closing));
            if (ReferenceEquals(editingDungeon, closing)) editingDungeon = null;
            RefreshScreenState();
            // The POI editor's tab was never closed, so it is still bound to the same POI — but it was Bound
            // BEFORE this interior existed, so its «Карта локации» label would keep its pre-dungeon "Создать"
            // text. Refresh it here to reflect a dungeon just created; Bind is otherwise the only place that
            // computes it.
            if (editingPoi != null && poiEditorScreen != null) poiEditorScreen.RefreshMapSection();
        }

        /// <summary>Opens an ACTIVE settlement building's own full building interior (Ц2 recursion), one
        /// level down from the currently-open town. Mirrors OpenBattleGrid's resolve-before-mutate
        /// discipline: the bound editor's Kind, the room, its TypeId and IsDummy are all checked BEFORE any
        /// field is touched, so an id that cannot resolve to an openable building leaves the DM exactly
        /// where they are (same room still selected, same screen shown) — no partial navigation, no error
        /// visible in a standalone build.</summary>
        public void OpenBuildingInterior(int roomId)
        {
            if (editingDungeon == null || roomId == 0 || dungeonManager == null || dungeonEditorScreen == null) return;
            if (editingDungeon.Kind != InteriorKind.Settlement) return;

            int floorIndex = dungeonEditorScreen.CurrentLevelIndex;
            if (floorIndex < 0 || floorIndex >= editingDungeon.Floors.Count) return;
            var room = editingDungeon.Floors[floorIndex].GetRoom(roomId);
            if (room == null || room.TypeId != 1 || room.IsDummy) return;

            string poiId = editingDungeon.OwnerPoiId;
            // GetByPoiId/GetOrCreateForPoi cannot serve this lookup — both resolve the FIRST interior for a
            // poiId, which is the town (see DungeonManager.AddInterior's doc). A building interior is a
            // SECOND interior for the same poiId, distinguished only by OwnerRoomId.
            var building = InteriorOps.FindBuildingInterior(dungeonManager.GetAll(), poiId, roomId);
            if (building == null)
            {
                // Deterministic seed (InteriorOps.BuildingSeed), NOT DungeonEditorScreen.FreshSeed — the SAME
                // node's interior must generate identically every time it's (re-)opened, across sessions and
                // save/load, not just within one. Reuses the exact room/floor counts a fresh top-level
                // building shell gets (DungeonEditorScreen.Bind's Floors.Count==0 path) so the two code paths
                // can never disagree about what a "default" building looks like.
                int seed = InteriorOps.BuildingSeed(poiId, roomId);
                building = BuildingGenerator.Generate(seed, poiId, DungeonEditorScreen.DefaultRooms, DungeonEditorScreen.DefaultBuildingFloors);
                building.OwnerRoomId = roomId;   // Generate() already sets OwnerPoiId + Kind=Building
                // Added NON-empty (Generate always returns floor 0 populated): Bind's Floors.Count==0 path
                // exists precisely to lazily seed an EMPTY interior with FreshSeed(), which would silently
                // overwrite this deterministic seed the very first time the building is opened.
                dungeonManager.AddInterior(building);
            }

            parentTown = editingDungeon;
            editingDungeon = building;
            string header = !string.IsNullOrEmpty(room.Title) ? room.Title : $"Здание {roomId}";
            dungeonEditorScreen.Bind(editingDungeon, header);
            // The town's OWN tab stays open behind this one — that is what CloseDungeonEditor's «← Город»
            // branch focuses on the way back up. Same header string for both the screen's top strip and the
            // tab, so the two cannot disagree about what this building is called.
            OpenSurfaceTab(InteriorSurface(editingDungeon), header);
            RefreshScreenState();
        }

        /// <summary>Opens the battle map of a room on the CURRENTLY open interior floor, in its own tab. The
        /// interior's tab stays open behind it (editingDungeon stays set), so closing this one lands back
        /// there with the same room still selected.</summary>
        public void OpenBattleGrid(int roomId)
        {
            if (editingDungeon == null || roomId == 0 || battleGridScreen == null) return;

            // Final-review fix C2 (belt-and-suspenders): a settlement building has no direct battle map — its
            // interior comes via building-interior recursion, not a room-level grid over the whole town's
            // ~40-node graph (BattleGridGenerator.ProjectDoors routes RoutingMode.Clean, a multi-second hang
            // at settlement scale). The inspector hides its «Боевая карта» button and the graph's
            // double-click is a no-op for a settlement (DungeonInspectorPanel.Rebuild / DungeonEditorScreen's
            // OnRoomDoubleClicked), so this path should be unreachable already; this defensive return backs
            // both of those up so no future entry point can reach BattleGridScreen.Bind -> ProjectDoors for a
            // settlement.
            if (editingDungeon.Kind == InteriorKind.Settlement) return;

            // Resolve BEFORE touching battleGridRoomId/battleGridFloorIndex: those two name the surface (see
            // BattleGridSurface), so setting them for a room that does not resolve would open a tab bound to
            // nothing — blank on a first open, or still showing the PREVIOUS room's map on a later one, with
            // no error the DM can see in a standalone build. An id that cannot be resolved must leave the
            // DM exactly where they are.
            if (dungeonEditorScreen == null) return;
            int floorIndex = dungeonEditorScreen.CurrentLevelIndex;
            if (floorIndex < 0 || floorIndex >= editingDungeon.Floors.Count) return;
            var gridRoom = editingDungeon.Floors[floorIndex].GetRoom(roomId);
            if (gridRoom == null) return;

            battleGridRoomId = roomId;
            // Remembered rather than re-read from dungeonEditorScreen.CurrentLevelIndex when the tab is
            // closed: the interior editor stays live behind this tab, so the DM can switch its level tab
            // while the battle grid is open, and a close would then compute a DIFFERENT id than the open did
            // and fail to find its own tab.
            battleGridFloorIndex = floorIndex;
            battleGridScreen.Bind(editingDungeon, floorIndex, roomId);
            OpenSurfaceTab(BattleGridSurface(),
                !string.IsNullOrEmpty(gridRoom.Title) ? gridRoom.Title : $"Бой: комната {roomId}");
            RefreshScreenState();
        }

        public void CloseBattleGrid()
        {
            // BattleGridSurface is derived from these three, so they are read (and captured) before the close,
            // and cleared afterwards only if the close did not already re-point them at a DIFFERENT battle
            // grid — see ClosePoiEditor's doc for the general rule.
            var closingInterior = editingDungeon;
            int closingRoom = battleGridRoomId;
            int closingFloor = battleGridFloorIndex;

            CloseSurfaceTab(BattleGridSurface());

            if (ReferenceEquals(editingDungeon, closingInterior)
                && battleGridRoomId == closingRoom && battleGridFloorIndex == closingFloor)
            {
                battleGridRoomId = 0;
                battleGridFloorIndex = 0;
            }
            RefreshScreenState();
        }

        // ── Surface refs, titles, and the two calls that reach the workspace ────────────────────────

        /// <summary>The tab a POI editor is shown in. PoiData.Id is a Guid string (PoiData.cs), so it names
        /// one POI unambiguously — the same identity Task 10b's WorldRef{Kind=Poi, Id} already relies on.
        /// Null for a null POI so every call site can pass its field unguarded — the ONLY thing this method
        /// still decides, since the ref itself comes from WorldSurface.PoiEditor, the same factory the
        /// navigator's «Мир» rows and Ctrl+K's world rows go through. That is not tidiness: the map path and
        /// the navigator/Ctrl+K path are precisely the two sides that must produce an identical ref, because
        /// WorkspaceOps.SameSurface compares Kind AND Id and one character of disagreement is a second tab
        /// for one place rather than a visible error.</summary>
        static SurfaceRef PoiEditorSurface(PoiData poi) =>
            poi == null ? null : WorldSurface.PoiEditor(poi.Id);

        /// <summary>The tab an interior is shown in — Settlement, BuildingInterior or Dungeon, decided by the
        /// interior's own Kind rather than by which method opened it, so the two can never disagree.
        ///
        /// THE ID HAS TO CARRY OwnerRoomId, not just OwnerPoiId: a building interior is a SECOND interior for
        /// the SAME poiId, distinguished only by OwnerRoomId (see InteriorData.OwnerRoomId's own doc and
        /// OpenBuildingInterior's comment about why GetByPoiId cannot serve that lookup). Without the room
        /// part, a town and a building inside it would be the same SurfaceRef under WorkspaceOps.SameSurface,
        /// so drilling into a building would silently re-focus the town's tab instead of opening one.
        /// InteriorKind.Building with OwnerRoomId 0 is a TOP-LEVEL building — a POI that IS a building
        /// (Tower/Temple/Fortress/Ruin, per InteriorProfile), not one inside a town. It maps to
        /// BuildingInterior too. An earlier revision gave it a literal "#0" suffix and claimed that kept the
        /// id "a pure function of the data with no special case"; the claim was true of the ENCODER and false
        /// of the PAIR, because the decoder fed InteriorOps.FindBuildingInterior, which refuses roomId 0
        /// (InteriorOps.cs:13) — so those four POI types re-bound to null and re-showed a DIFFERENT interior
        /// on every return to their tab. SurfaceIds now owns both halves, omits the suffix entirely for room
        /// 0, and SurfaceIdsSelfTests pins the round-trip for BOTH shapes rather than only the one a
        /// hand-walk happened to try.
        ///
        /// OwnerRoomId is passed unconditionally rather than only for the Building case: a settlement or a
        /// dungeon always carries 0, so SurfaceIds.Interior yields the bare poiId for them anyway, and reading
        /// the field one way for all three kinds leaves nothing to keep in step.</summary>
        static SurfaceRef InteriorSurface(InteriorData interior)
        {
            if (interior == null) return null;
            string id = SurfaceIds.Interior(interior.OwnerPoiId, interior.OwnerRoomId);
            switch (interior.Kind)
            {
                case InteriorKind.Settlement: return new SurfaceRef { Kind = SurfaceKind.Settlement, Id = id };
                case InteriorKind.Building:   return new SurfaceRef { Kind = SurfaceKind.BuildingInterior, Id = id };
                default:                      return new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = id };
            }
        }

        /// <summary>The tab the battle grid is shown in, named by the interior AND the exact room on the exact
        /// floor — three different rooms of one dungeon are three different battle maps, and each deserves its
        /// own tab rather than one tab whose meaning changes. Null when no battle grid is open, which is what
        /// makes CloseSurfaceTab a no-op on the paths that call it unconditionally.</summary>
        SurfaceRef BattleGridSurface()
        {
            if (editingDungeon == null || battleGridRoomId == 0) return null;
            var interior = InteriorSurface(editingDungeon);
            return new SurfaceRef
            {
                Kind = SurfaceKind.BattleGrid,
                Id = SurfaceIds.BattleGrid(interior.Id, battleGridFloorIndex, battleGridRoomId),
            };
        }

        // ── Re-binding a tab the user switched BACK to ──────────────────────────────────────────────
        //
        // THE DEFECT THIS EXISTS FOR. Every screen these five surfaces wrap holds ONE binding at a time
        // (DungeonEditorScreen.current, BattleGridScreen's bound room, PoiEditorScreen.current), set by the
        // Open* methods above. Clicking a TAB does not go through those: TabStripView -> WorkspaceController.
        // SetActive -> SyncSurfaces -> ISurfaceHost.Show, and nothing on that path binds anything. So without
        // this, the ordinary Ц2 flow breaks — open a town, drill into a building (both tabs now open), click
        // the TOWN's tab: the screen comes back on still showing the BUILDING. Two POI-editor tabs, or two
        // dungeons, do the same. PageSurfaceHost never had this problem because its Show ends in
        // pageView.ShowPage(id), i.e. it re-binds from the id every call; this is the same idea for
        // the five screens that have no such call of their own.
        //
        // WHY THE ID IS ENOUGH: every surface id here is a pure function of the data (see PoiEditorSurface /
        // InteriorSurface / BattleGridSurface), so it can be reversed back to the object without any stored
        // side table. That is the same property WorkspacePrefs relies on to restore tabs across a restart —
        // the id is all that is stored — and SurfaceExists below is the same reversal asked as a yes/no
        // question. Three readers of one contract now; when this comment was written it had none.
        //
        // ACCEPTED CONSEQUENCE — RETURNING TO A MULTI-FLOOR INTERIOR'S TAB LANDS ON FLOOR 0.
        // DungeonEditorScreen.Bind ends in SetLevel(0), so a rebind resets the level tab (and the room
        // selection) exactly the way re-entering the editor from the map always has. Not a regression — it is
        // the pre-tab behaviour — but the rebind hook makes it ROUTINE where it previously took an explicit
        // re-open. Still left as-is: per-tab level/selection is real per-tab VIEW state, and faking it here
        // would mean this class quietly keeping a second copy of the editor's own state. Task 11 did NOT
        // take it on — WorkspaceLayout stores what each tab POINTS AT (kind, id, title, which pane, which is
        // active) and nothing about what the surface behind it was showing, and widening that would mean a
        // stored format that has to know something about every surface kind. Р5's, if anyone's. The early-out below is what stops it
        // happening on syncs that change nothing, which is the common case.
        //
        // EVERY BRANCH EARLY-OUTS WHEN ALREADY BOUND, and that is a requirement, not an optimisation:
        // SyncSurfaces -> Show runs on EVERY layout change (a divider commit, a focus change, opening an
        // unrelated tab), and DungeonEditorScreen.Bind rebuilds the whole node canvas — re-binding a
        // settlement's ~40-node graph on every one of those would be a visible stutter, and it would also
        // discard the DM's current selection and level tab each time.

        /// <summary>Re-binds the screen behind `kind` to whatever `id` names, called from
        /// ScreenSurfaceHosts.Show. Silently does nothing when the id cannot be resolved (a tab pointing at a
        /// POI or interior that has since been deleted) — the screen then keeps its previous binding, which is
        /// wrong but harmless, where a throw here would happen inside a Canvas callback.</summary>
        public void RebindSurface(SurfaceKind kind, string id)
        {
            switch (kind)
            {
                case SurfaceKind.PoiEditor: RebindPoiEditor(id); break;
                case SurfaceKind.Settlement:
                case SurfaceKind.Dungeon:
                case SurfaceKind.BuildingInterior: RebindInterior(id); break;
                case SurfaceKind.BattleGrid: RebindBattleGrid(id); break;
            }
        }

        void RebindPoiEditor(string id)
        {
            if (editingPoi != null && editingPoi.Id == id) return;
            var poi = Pois()?.GetPoiById(id);
            if (poi == null || poiEditorScreen == null) return;
            editingPoi = poi;
            poiEditorScreen.Bind(poi);
        }

        void RebindInterior(string id)
        {
            var interior = ResolveInterior(id);
            if (interior == null || dungeonEditorScreen == null) return;
            if (ReferenceEquals(editingDungeon, interior)) return;

            // EVERY DECISION BELOW READS interior.OwnerRoomId, NOT THE SurfaceKind THE TAB CARRIES. The two
            // agree in practice (InteriorSurface derives one from the other), but only OwnerRoomId is the
            // authority on "is this a building INSIDE a town, or a POI that simply IS a building" — and the
            // Critical this method was fixed for came precisely from treating SurfaceKind.BuildingInterior as
            // if it implied a room. Tower/Temple/Fortress/Ruin are Kind=Building with OwnerRoomId 0.
            bool insideATown = interior.OwnerRoomId != 0;

            // parentTown is re-derived rather than left alone, because it is what «← Город» reads: switching
            // to a building-in-a-town's tab must make the back button go to THAT building's town, and
            // switching to any top-level interior must clear it (the same stale-parent rule OpenDungeonEditor
            // enforces for the map entry path). The town is the FIRST interior for the poiId —
            // DungeonManager.GetByPoiId, the same lookup OpenBuildingInterior's own comment says cannot serve
            // a BUILDING lookup and can therefore serve this one exactly.
            parentTown = insideATown && dungeonManager != null
                ? dungeonManager.GetByPoiId(interior.OwnerPoiId)
                : null;
            if (ReferenceEquals(parentTown, interior)) parentTown = null;   // belt: nothing is its own parent

            editingDungeon = interior;
            if (insideATown)
                dungeonEditorScreen.Bind(interior, BuildingHeaderFor(parentTown, interior.OwnerRoomId));
            else
                // A TOP-LEVEL interior takes the same no-header bind OpenDungeonEditor gives it. Routing it
                // through BuildingHeaderFor instead would title the screen «Здание 0» — there is no room 0 to
                // name, and no parent town to look one up in.
                dungeonEditorScreen.Bind(interior, roomsWithInterior: RoomsWithInteriorFor(interior));
        }

        void RebindBattleGrid(string id)
        {
            // SurfaceIds owns both halves of this parse (and SurfaceIdsSelfTests pins them against each
            // other) — in particular the parse-from-the-RIGHT rule, which matters because the interior part
            // itself contains a separator whenever the grid is inside a building-in-a-town.
            if (!SurfaceIds.TryParseBattleGrid(id, out string interiorId, out int floorIndex, out int roomId)) return;

            var interior = ResolveInterior(interiorId);
            if (interior == null || battleGridScreen == null) return;
            if (floorIndex < 0 || floorIndex >= interior.Floors.Count) return;
            if (interior.Floors[floorIndex].GetRoom(roomId) == null) return;

            if (ReferenceEquals(editingDungeon, interior)
                && battleGridFloorIndex == floorIndex && battleGridRoomId == roomId) return;

            // editingDungeon follows the battle grid's OWN interior, not whatever was open before: CloseBattleGrid
            // derives this tab's SurfaceRef from editingDungeon + these two ints (see BattleGridSurface), so
            // leaving it pointing elsewhere would make «Назад» fail to find the tab it is closing.
            editingDungeon = interior;
            battleGridFloorIndex = floorIndex;
            battleGridRoomId = roomId;
            battleGridScreen.Bind(interior, floorIndex, roomId);
        }

        /// <summary>Reverses InteriorSurface: an id back to the InteriorData it names. Takes NO SurfaceKind,
        /// deliberately — the id's own shape says everything the lookup needs, and the SurfaceKind the tab
        /// carries is what the fixed Critical came from (`BuildingInterior` does not imply a room; a
        /// Tower/Temple/Fortress/Ruin is Kind=Building with OwnerRoomId 0). One argument that cannot disagree
        /// with itself beats two that can.
        ///
        /// roomId 0 → "the FIRST interior for this poiId" (DungeonManager.GetByPoiId), which is what a
        /// settlement, a dungeon and a POI-that-is-a-building all are. Non-zero →
        /// InteriorOps.FindBuildingInterior, the only lookup that can pick out a SECOND interior sharing a
        /// poiId — and the one that refuses roomId 0 outright (InteriorOps.cs:13), which is exactly why the
        /// old encoding's `#0` suffix resolved to null.</summary>
        InteriorData ResolveInterior(string id)
        {
            if (dungeonManager == null) return null;
            if (!SurfaceIds.TryParseInterior(id, out string poiId, out int roomId)) return null;
            return roomId == 0
                ? dungeonManager.GetByPoiId(poiId)
                : InteriorOps.FindBuildingInterior(dungeonManager.GetAll(), poiId, roomId);
        }

        /// <summary>The header OpenBuildingInterior would have used for this building — its room's Title, or
        /// «Здание N». Scans every floor because, unlike OpenBuildingInterior, a rebind has no "the level the
        /// DM was looking at" to read the room from; a room id is unique within a floor and the same node is
        /// not expected on two, so the first hit is the right one.</summary>
        static string BuildingHeaderFor(InteriorData town, int roomId)
        {
            if (town != null)
                foreach (var floor in town.Floors)
                {
                    var room = floor.GetRoom(roomId);
                    if (room != null && !string.IsNullOrEmpty(room.Title)) return room.Title;
                }
            return $"Здание {roomId}";
        }

        /// <summary>The live POI store, discovered on every miss like Shell() and for the same two
        /// reasons: no Inspector slot without a scene edit, and a domain reload wipes the cached reference
        /// while the component survives. Only RebindPoiEditor needs it — every other Rebind* path resolves
        /// through dungeonManager, which this class has held as a serialized field since Ц1.</summary>
        PoiManager Pois()
        {
            if (poiManager != null) return poiManager;
            poiManager = FindFirstObjectByType<PoiManager>(FindObjectsInactive.Include);
            return poiManager;
        }

        PoiManager poiManager;

        /// <summary>A POI's tab title, and — since Task 10e — its «Мир» row's title and its Ctrl+K row's
        /// title too: WorldObjectSource.Collect calls THIS method rather than copying poi.Name, so a nameless
        /// POI reads «Без названия» in all three places instead of being «Без названия» in the tab strip and
        /// blank in the tree. Public for that one caller (the same reason PoiInfoPopup.TypeLabel was made
        /// public in Task 10b), and the fallback string is still the one NotesDocOps.EnsurePageFor's E3
        /// uses, so a page Р3 later binds to a nameless POI still agrees with its row.
        ///
        /// FOUR READERS NOW: InteriorMapTitle builds the interior-map tab's «[Название]:карта» on top of this
        /// rather than reading poi.Name itself, so the map tab and the editor tab cannot disagree about what
        /// the place is called — including for a nameless POI, where both read «Без названия».</summary>
        public static string PoiTitle(PoiData poi) =>
            poi != null && !string.IsNullOrWhiteSpace(poi.Name) ? poi.Name : "Без названия";

        /// <summary>An interior's tab title, used only on CloseDungeonEditor's «← Город» path where the town's
        /// own tab is expected to already exist (so this is the title of a tab that will normally not be
        /// created). The POI's real name is not reachable from an InteriorData, and going to PoiManager for it
        /// would add a manager reference for one fallback string — so this names the KIND, which is honest
        /// about what it knows.</summary>
        static string InteriorTitle(InteriorData interior) =>
            interior == null ? "" : interior.Kind == InteriorKind.Settlement ? "Город"
                : interior.Kind == InteriorKind.Building ? "Здание" : "Подземелье";

        /// <summary>Opens `s` in the FOCUSED pane unless a caller asks otherwise. A no-op when there is no
        /// workspace in the scene (a bare rig — see Shell()) and when `s` is null, which is how the
        /// Close*/Open* paths pass state that is not set.
        ///
        /// "OPENS" IS THREE OUTCOMES, not one, and every Open* method in this file inherits all three. A tab
        /// is CREATED only when `s` is open nowhere; already open in the target pane -> that tab is ACTIVATED
        /// (R1); already open in the OTHER pane -> that tab is MOVED here (R1b). `title` is therefore only
        /// consulted on the create path, which is why InteriorMapTitle's rename does not reach an
        /// interior-map tab that already exists. See WorkspaceOps.Open for why moving rather than duplicating
        /// is what the single-instance hosts require.
        ///
        /// The default is false and every call site but one takes it: OpenPoiFromMap (a double-click on the
        /// map) is the single gesture in this file that opens beside rather than in front — see its own doc,
        /// and the section comment above the Open* methods.</summary>
        void OpenSurfaceTab(SurfaceRef s, string title, bool inOtherPane = false)
        {
            if (s == null) return;
            Shell()?.Open(s, title, inOtherPane);
        }

        /// <summary>Closes `s`'s tab wherever it is. Null-tolerant for the same reason OpenSurfaceTab is, and
        /// tolerant of the surface not being open at all — WorkspaceController.CloseSurface's own doc explains
        /// why that is ordinary rather than an error.</summary>
        void CloseSurfaceTab(SurfaceRef s)
        {
            if (s == null) return;
            Shell()?.CloseSurface(s);
        }

        /// <summary>Double-clicking a place on the map — the Р1-reachable half of the spec's "clicking a
        /// place": a single click only SELECTS (a stray click must never throw the user out of the map), and
        /// the place opens on an explicit action. Wired from PoiInteractionController.OnRelease.
        ///
        /// OPENS THE EDITOR, not a page (Task 10e). Until this task it created and opened the POI's note —
        /// «двойное нажатие ... должен выдавать то же самое меню, именно оно соответствует точке интереса»
        /// settles that: the place IS its editor, and there is no longer a second surface for the same
        /// gesture to disagree about. It is literally the same call the popup's «Редактировать» makes.
        ///
        /// IN THE OTHER PANE WHEN A SPLIT EXISTS, per the spec, otherwise a new tab in the focused pane. That
        /// is the one place in this file that passes inOtherPane: true, and it is the point of the gesture —
        /// the user is opening a place NEXT TO the map they clicked it on, not instead of it.
        ///
        /// LIVES HERE, not in PoiInteractionController, even though the gesture is wired there: this class
        /// already owns the discovered WorkspaceController (see Shell()) and the editor screen, and giving the
        /// interaction controller its own copies would be more references to go stale after a domain reload
        /// for no gain.</summary>
        public void OpenPoiFromMap(PoiData poi)
        {
            var shell = Shell();
            bool split = shell != null && shell.Layout != null && shell.Layout.Secondary != null;
            OpenPoiEditorIn(poi, inOtherPane: split);
        }

        /// <summary>The one implementation behind both entry points above; they differ only in the pane. Kept
        /// private with two named public callers rather than exposed as one method with a bool, so the two
        /// gestures stay readable at their call sites (PoiInfoPopup's «Редактировать» / a double-click) and
        /// nothing else can invent a third pane rule.</summary>
        void OpenPoiEditorIn(PoiData poi, bool inOtherPane)
        {
            if (poi == null) return;
            editingPoi = poi;
            if (poiInfoPopup != null) poiInfoPopup.Hide();
            if (poiEditorScreen != null) poiEditorScreen.Bind(poi);
            OpenSurfaceTab(PoiEditorSurface(poi), PoiTitle(poi), inOtherPane);
            RefreshScreenState();
        }

        void RefreshScreenState()
        {
            EnsureSwitcher();
            switcher.Show(DesiredScreen());
        }

        /// <summary>The single truth table mapping app state to the one visible screen. Three rows now
        /// instead of six (Task 10c): the four editing states this used to switch on — battleGridRoomId,
        /// editingDungeon, editingPoi, and "none of the above" — are all the WORKSPACE now, and which of them
        /// is on screen is a question about which TAB is active, answered by WorkspaceController, not here.
        /// The switcher still hides every other screen's members, so nothing can leak by omission.</summary>
        AppScreen DesiredScreen()
        {
            bool hasMap = mapRenderer.Cells != null;
            bool generating = activeGeneration != null;
            if (generating) return AppScreen.Progress;
            if (!hasMap || newWorldRequested) return AppScreen.Generation;
            return AppScreen.Workspace;
        }

        // ── «Создать новый мир…» (Task 10c Step 4) ──────────────────────────────────────────────
        //
        // The roadmap gap the Р1 spec names: "today there is no way to create a second world without
        // restarting the app, because StartGeneration is only reachable from the generation screen". The
        // generation FORM is the entry point, not StartGeneration directly — the form is where the seed,
        // size, shape and region count come from, and calling StartGeneration without it would silently
        // regenerate with whatever the renderer happens to hold.
        //
        // A plain field, so a Play-mode domain reload resets it to false — which lands the DM back on their
        // existing world rather than on a generation form they no longer asked for. That is the harmless
        // direction of this arc's recurring reload defect, so it needs no recovery of its own.

        bool newWorldRequested;

        /// <summary>Whether a world exists to be replaced. ProjectMenuBar reads it to decide whether
        /// «Создать новый мир…» is worth offering at all: with no world the generation form is ALREADY the
        /// screen (DesiredScreen's `!hasMap`), so the item would be a no-op that looks like a command.</summary>
        public bool HasWorld => mapRenderer != null && mapRenderer.Cells != null;

        /// <summary>Whether the generation form is currently being shown OVER an existing world — the state
        /// «Вернуться к текущему миру» exists to leave. See RequestNewWorld for why an escape is needed.</summary>
        public bool NewWorldRequested => newWorldRequested;

        /// <summary>Shows the generation form over the existing world. The caller confirms first
        /// (ProjectMenuBar does) — generating replaces the world in place, so this is destructive in the same
        /// sense «Сгенерировать заново» is, and this project confirms before destroying.
        ///
        /// REVERSIBLE ON PURPOSE, via CancelNewWorldRequest. The generation form has no back button of its
        /// own (it was only ever reachable when there was nothing to go back TO), so without an escape a DM
        /// who confirmed and then changed their mind would be stuck filling in a form or restarting the app —
        /// the very complaint this feature exists to fix, reintroduced one screen along. The escape lives in
        /// the «Файл» menu because ProjectMenuBar's canvas sorts at 100, above GenerationScreenUI's 50, so
        /// the menu stays reachable while the form is up.</summary>
        public void RequestNewWorld()
        {
            if (!HasWorld || newWorldRequested) return;
            newWorldRequested = true;
            RefreshScreenState();
        }

        /// <summary>Dismisses the generation form and returns to the workspace, leaving the current world
        /// exactly as it was — nothing has been generated yet at this point.</summary>
        public void CancelNewWorldRequest()
        {
            if (!newWorldRequested) return;
            newWorldRequested = false;
            RefreshScreenState();
        }

        public void StartGeneration(WorldGen.Rendering.GenerationRequest uiParams)
        {
            if (activeGeneration != null) return;

            // The request is satisfied the moment generation starts: from here on DesiredScreen is driven by
            // `generating`, and when it finishes there IS a new world, so leaving the flag set would keep the
            // form up over it. Cleared here rather than in RunGeneration's tail so CancelGeneration lands back
            // on the world too, instead of on the form the DM already left.
            newWorldRequested = false;

            ApplyUiParamsToRenderer(uiParams);
            var genParams = BuildGenerationParams(uiParams);

            // Kept for the FIRST-world case, where hasMap is still false and this shows the generation screen
            // again (activeGeneration is not assigned until StartCoroutine returns below, so DesiredScreen
            // cannot see "generating" yet). On the «Создать новый мир…» path it now resolves to Workspace
            // instead, because newWorldRequested was just cleared — a wasted SyncSurfaces (including an
            // interior re-Bind) with no visible flicker, since RunGeneration's first synchronous segment calls
            // RefreshScreenStateForGenerating in this same frame. Left in rather than made conditional: the
            // waste is one frame at the start of a multi-second generation, and a conditional here would be a
            // second place that has to agree with DesiredScreen about what is showing.
            RefreshScreenState();
            activeGeneration = StartCoroutine(RunGeneration(genParams));
        }

        void ApplyUiParamsToRenderer(WorldGen.Rendering.GenerationRequest uiParams)
        {
            mapRenderer.seed = GenerationScreenUI.StableSeedHash(uiParams.SeedText);

            switch (uiParams.Size)
            {
                case MapSizePreset.Small:  mapRenderer.continentWidth = 350f; mapRenderer.continentHeight = 350f; break;
                case MapSizePreset.Medium: mapRenderer.continentWidth = 500f; mapRenderer.continentHeight = 500f; break;
                case MapSizePreset.Large:  mapRenderer.continentWidth = 700f; mapRenderer.continentHeight = 700f; break;
            }

            switch (uiParams.Shape)
            {
                case LandShapePreset.Continent:   mapRenderer.falloffPower = 3.0f; mapRenderer.innerRadius = 0.6f; mapRenderer.seaLevel = 0.30f; break;
                case LandShapePreset.Archipelago:  mapRenderer.falloffPower = 1.8f; mapRenderer.innerRadius = 0.3f; mapRenderer.seaLevel = 0.45f; break;
                case LandShapePreset.Islands:       mapRenderer.falloffPower = 1.5f; mapRenderer.innerRadius = 0.1f; mapRenderer.seaLevel = 0.55f; break;
            }

            mapRenderer.numberOfRegions = uiParams.RegionCount;
        }

        GenerationParams BuildGenerationParams(WorldGen.Rendering.GenerationRequest uiParams)
        {
            // Mirrors WorldMapRenderer.BuildGenerationParams()'s field-by-field copy, since
            // GenerateWorldStepped (unlike GenerateAndRender) is called directly here, not
            // through WorldMapRenderer. mapWidth/mapHeight are DERIVED from the stable
            // continentWidth/Height + oceanPadding, recomputed here before every generation -
            // see WorldMapRenderer.BuildGenerationParams for the runaway-safety rationale.
            mapRenderer.mapWidth = mapRenderer.continentWidth * (1f + 2f * mapRenderer.oceanPadding);
            mapRenderer.mapHeight = mapRenderer.continentHeight * (1f + 2f * mapRenderer.oceanPadding);
            return new GenerationParams
            {
                Seed = mapRenderer.seed,
                Width = mapRenderer.mapWidth,
                Height = mapRenderer.mapHeight,
                ContinentWidth = mapRenderer.continentWidth,
                ContinentHeight = mapRenderer.continentHeight,
                OceanPadding = mapRenderer.oceanPadding,
                MinPointDistance = mapRenderer.minPointDistance,
                LloydRelaxIterations = mapRenderer.lloydIterations,
                NumberOfRegions = mapRenderer.numberOfRegions,
                FalloffPower = mapRenderer.falloffPower,
                InnerRadius = mapRenderer.innerRadius,
                CoastRoughness = (float)(new System.Random(mapRenderer.seed + 6000).NextDouble() * 0.5),
                ContinentCenterJitter = mapRenderer.continentCenterJitter,
                SeaLevel = mapRenderer.seaLevel,
                MinLakeSize = mapRenderer.minLakeSize,
                ElevationCoastWeight = mapRenderer.elevationCoastWeight,
                ElevationNoiseWeight = mapRenderer.elevationNoiseWeight,
                ElevationNoiseFrequency = mapRenderer.elevationNoiseFrequency,
                ElevationNoiseOctaves = mapRenderer.elevationNoiseOctaves,
                ElevationContrast = mapRenderer.elevationContrast,
                MoistureFalloffDistance = mapRenderer.moistureFalloffDistance,
                NumberOfTemperatureEpicenters = mapRenderer.numberOfTemperatureEpicenters,
                EpicenterMinRadius = mapRenderer.epicenterMinRadius,
                EpicenterMaxRadius = mapRenderer.epicenterMaxRadius,
                BaseTemperature = mapRenderer.baseTemperature,
                ElevationTempDrop = mapRenderer.elevationTempDrop,
                NumberOfMoistureEpicenters = mapRenderer.numberOfMoistureEpicenters,
                MoistureEpicenterMinRadius = mapRenderer.moistureEpicenterMinRadius,
                MoistureEpicenterMaxRadius = mapRenderer.moistureEpicenterMaxRadius,
                MoistureEpicenterMinDelta = mapRenderer.moistureEpicenterMinDelta,
                MoistureEpicenterMaxDelta = mapRenderer.moistureEpicenterMaxDelta,
                EnableRivers = mapRenderer.enableRivers,
                NumberOfRivers = mapRenderer.numberOfRivers,
                RiverMinStartElevation = mapRenderer.riverMinStartElevation,
            };
        }

        System.Collections.IEnumerator RunGeneration(GenerationParams genParams)
        {
            RefreshScreenStateForGenerating();

            List<VoronoiCell> generatedCells = null;
            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) => generatedCells = cells);

            mapRenderer.PrepareLoadFromCells(generatedCells, genParams);
            yield return mapRenderer.RebakeAllStepped(bakeFrac => progressScreen.SetStep("Отрисовка карты", (5f + bakeFrac) / 6f));

            // THE OTHER DOOR INTO STEP 4'S COLLISION, and it is the one ProjectMenuBar's bracket cannot
            // reach. The next line raises OnWorldRegenerated, which reaches this class's own handler and
            // prunes every ex-screen tab — and that prune raises OnLayoutChanged, which SAVES. On the
            // «Создать новый мир…» path nothing has changed the workspace's key, so that save lands on the
            // key of the project the DM had open a moment ago: A's stored layout is permanently degraded to
            // "world map plus pages" by generating a world that has nothing to do with A. It bites only when
            // an ex-screen tab is open (PruneSurfaces returns 0 and raises nothing otherwise), which is
            // exactly what would have made it look intermittent.
            //
            // RE-KEY, NOT BRACKET, and the choice is not arbitrary. Begin/EndProjectSwitch would end in a
            // RESTORE of the no-project slot — so generating a new world would swap the DM's tabs for
            // whatever was last open in some unsaved session. What is wanted here is the opposite: the tabs
            // on screen are the ones this new world starts with, minus the editors the prune is about to
            // drop. That is exactly RekeyTo's contract ("point persistence at a different project without
            // restoring anything"), with "" as the destination, since a freshly generated world belongs to
            // no project until it is saved.
            //
            // PLACED HERE, immediately before the line that fires the event, rather than in StartGeneration:
            // CancelGeneration can stop this coroutine at either yield above, and a re-key that had already
            // happened would leave the workspace writing to the no-project slot while project A is still
            // open — staleness in exchange for preventing loss, when waiting one statement prevents both.
            //
            // THE FILE FOLLOWS THE LAYOUT, and it is the same question answered the same way. The re-key
            // above says "these tabs no longer belong to project A"; without the line below,
            // ProjectMenuBar.currentPath would go on saying the opposite about the WORLD — so «Файл →
            // Сохранить» (there is no keyboard shortcut in this app) would silently write the generated
            // world over project A's file, with no prompt and no «Сохранить как…».
            //
            // WHAT THAT COSTS, read from the handlers rather than guessed: PoiManager.ClearAll and
            // DungeonManager.ClearAll are subscribed to this same OnWorldRegenerated, and
            // DungeonManager.ClearAll is `dungeons.Clear()` — every town, dungeon, building interior and
            // battle grid. The written file would be new terrain, zero POIs, zero interiors and the old
            // notes: a campaign's entire world content, permanently. «Сохранить» is also the menu row
            // IMMEDIATELY BELOW «Создать новый мир…» (ProjectMenuBar.OpenMenu), and the bar still shows A's
            // name until this line runs.
            //
            // A REGRESSION THIS ARC INTRODUCED, not inherited debt — an earlier revision of this comment
            // said it "predates the workspace entirely" and that was simply wrong. c14736f (Task 10c) added
            // «Создать новый мир…»; before it the generation form was reachable ONLY when no world existed
            // (DesiredScreen's `!hasMap`), so currentPath was necessarily still null and there was nothing
            // to clear. RequestNewWorld's own doc quotes the spec on exactly that — "there is no way to
            // create a second world without restarting the app". Introduced here, closed here.
            //
            // NOR IS IT A SAVE-SEMANTICS RULING: DoSave already branches on `currentPath == null` into
            // DoSaveAs. This only makes that branch reachable from a state that could not previously exist.
            //
            // THE TWO CALLS MUST STAY TOGETHER — see WorkspaceController.ReconcileKeyWithLiveProject, whose
            // "the stored key and the live path cannot legitimately disagree" rule is what lets a reload
            // resolve the two against each other. Re-keying without forgetting would make this the one
            // moment they diverge, and a recompile after a generation would then reconcile back onto A and
            // resurrect the loss above.
            Shell()?.RekeyTo("");
            Menu()?.ForgetCurrentProject();
            mapRenderer.FinishLoadFromCells();

            progressScreen.SetStep("Готово", 1f);
            activeGeneration = null;
            RefreshScreenState();
        }

        // During generation START, activeGeneration hasn't been assigned yet (StartCoroutine runs
        // RunGeneration's first segment synchronously, before the handle is stored), so DesiredScreen()
        // would not yet see "generating". Show Progress directly. Unlike the old hand-rolled version,
        // the switcher also deactivates the workspace and hides its surfaces (via the hook), so neither the
        // shell nor the map chrome can linger on Progress during a regenerate.
        void RefreshScreenStateForGenerating()
        {
            EnsureSwitcher();
            switcher.Show(AppScreen.Progress);
        }

        void CancelGeneration()
        {
            if (activeGeneration == null) return;
            StopCoroutine(activeGeneration);
            activeGeneration = null;
            RefreshScreenState();
        }
    }
}
