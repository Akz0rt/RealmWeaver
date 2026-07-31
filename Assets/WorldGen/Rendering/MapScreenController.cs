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
    /// THE WORKSPACE IS DISCOVERED, NOT WIRED (transitional, Task 11 closes it): WorkspaceBuilder is not in
    /// any scene yet, so Shell() below returns null today and every Open*/Close* is a no-op — between
    /// this task and Task 11 the app has a world map and no POI/interior/battle editing at all. That is the
    /// same shape this arc already accepted once for Task 10a's shell-less path, and it is written down here
    /// rather than left to be discovered at a checkpoint.
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
        // Inspector assignments in SampleScene.unity, which this task is not allowed to touch (Task 11 owns
        // the scene), and they are the natural things for Task 11 to hand WorkspaceBuilder.mapChrome/mapCamera
        // as explicit overrides if type discovery ever picks the wrong instance.
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
            if (switcher != null) return;

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
            var shellRoot = Shell()?.ShellRoot;
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

        /// <summary>The workspace shell, discovered rather than wired — WorkspaceBuilder is not in any scene
        /// until Task 11 (see this class's own doc), so there is no Inspector slot to drag it into and nothing
        /// to find until then. Re-searched on every miss rather than once: that is also what recovers the
        /// reference after a Play-mode domain reload wipes this field, and what would let a shell built later
        /// in the session be picked up without a restart. FindObjectsInactive.Include because the shell's
        /// component sits on a GameObject that the switcher itself may have deactivated.
        ///
        /// Returns null in a scene with no workspace, and EVERY caller treats that as "do nothing" — the
        /// transitional no-editing window this class's own doc names.</summary>
        WorkspaceController Shell()
        {
            if (workspace != null) return workspace;
            workspace = FindFirstObjectByType<WorkspaceController>(FindObjectsInactive.Include);
            return workspace;
        }

        WorkspaceController workspace;

        void OnWorldRegenerated()
        {
            // Close the tabs BEFORE clearing the state that names them — every SurfaceRef below is derived
            // from editingPoi/editingDungeon/battleGridRoomId, so a clear-first ordering would leave three
            // tabs open pointing at a world that no longer exists, each of which would then Show() a screen
            // still bound to a dead InteriorData. (Task 11's WorkspacePrefs has its own version of this
            // problem for STORED tabs and its own answer, WorkspaceOps.PruneMissing; this is the live one.)
            CloseSurfaceTab(BattleGridSurface());
            CloseSurfaceTab(InteriorSurface(editingDungeon));
            // parentTown is a SECOND open interior tab, not a duplicate of the line above: inside a building
            // interior `editingDungeon` is the BUILDING and the town's own tab is still open behind it
            // (OpenBuildingInterior leaves it there on purpose). Newly reachable now that Step 4 lets a DM
            // generate a world from inside a building — town, building, «Файл» → «Создать новый мир…».
            CloseSurfaceTab(InteriorSurface(parentTown));
            CloseSurfaceTab(PoiEditorSurface(editingPoi));

            editingPoi = null; // a fresh world drops any open POI editor
            editingDungeon = null; // a fresh world drops any open dungeon editor
            parentTown = null;   // ...and any building-from-town back-target it was carrying
            battleGridRoomId = 0;   // a fresh world drops the battle grid screen too
            RefreshScreenState();
        }

        // ── Surfaces: opening and closing the tabs that used to be screens (Task 10c Step 2) ────────
        //
        // The Open* methods keep every line of the state-keeping they always had — Bind, the resolve-before-
        // mutate guards, parentTown bookkeeping — and only replace their final "switch the window to my
        // screen" with "open (or focus) my tab". That split is why each one still ends in RefreshScreenState:
        // the SCREEN question ("is there a world at all?") is unchanged and still this class's, while WHICH
        // surface is visible is now WorkspaceController's.
        //
        // inOtherPane is FALSE everywhere here, deliberately, and it is not an oversight to revisit: the spec
        // reserves the other pane for two explicit gestures (Shift+Enter in Ctrl+K, «Открыть рядом» in the
        // navigator) plus double-clicking a place on the map, which is Step 2a's and lives in
        // PoiInteractionController. Everything in THIS file is a drill-down from something already open — the
        // POI editor's «КАРТА ЛОКАЦИИ», a room's «Боевая карта», a building inside a town — where the user is
        // going deeper into one line of work, not putting two things side by side.

        /// <summary>Opens the POI editor for a point (single source of truth for the info-popup's
        /// «Редактировать» button). Hides the popup, binds the editor and opens its TAB — as of Task 10c it no
        /// longer takes over the window, which is the «редактирование точки интереса происходит не в отдельной
        /// вкладке, а раскрывается на весь экран» the user reported at the Task 10a checkpoint.
        ///
        /// ALSO ENSURES THE POI HAS A PAGE, which is what puts it in the navigator's «Мир» group — the spec's
        /// "it appears in the tree as a consequence of being worked on rather than through a separate
        /// mechanism", with opening the editor being the act of working on it. The page is CREATED, not
        /// OPENED: a tab for it would be a second thing appearing from one click. Note this is a different
        /// gesture from double-clicking the POI on the map (PoiInteractionController), which opens the page
        /// itself and no editor.</summary>
        public void OpenPoiEditor(PoiData poi)
        {
            if (poi == null) return;
            editingPoi = poi;
            if (poiInfoPopup != null) poiInfoPopup.Hide();
            if (poiEditorScreen != null) poiEditorScreen.Bind(poi);
            EnsurePageForPoi(poi);
            OpenSurfaceTab(PoiEditorSurface(poi), PoiTitle(poi));
            RefreshScreenState();
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
            OpenSurfaceTab(InteriorSurface(editingDungeon), PoiTitle(poi));
            RefreshScreenState();
        }

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
        /// Null for a null POI so every call site can pass its field unguarded.</summary>
        static SurfaceRef PoiEditorSurface(PoiData poi) =>
            poi == null ? null : new SurfaceRef { Kind = SurfaceKind.PoiEditor, Id = poi.Id };

        /// <summary>The tab an interior is shown in — Settlement, BuildingInterior or Dungeon, decided by the
        /// interior's own Kind rather than by which method opened it, so the two can never disagree.
        ///
        /// THE ID HAS TO CARRY OwnerRoomId, not just OwnerPoiId: a building interior is a SECOND interior for
        /// the SAME poiId, distinguished only by OwnerRoomId (see InteriorData.OwnerRoomId's own doc and
        /// OpenBuildingInterior's comment about why GetByPoiId cannot serve that lookup). Without the room
        /// part, a town and a building inside it would be the same SurfaceRef under WorkspaceOps.SameSurface,
        /// so drilling into a building would silently re-focus the town's tab instead of opening one.
        /// InteriorKind.Building with OwnerRoomId 0 is a TOP-LEVEL building (a POI that is a building, not one
        /// inside a town) — it maps to BuildingInterior too, and the "#0" suffix keeps its id a pure function
        /// of the data with no special case.
        ///
        /// '#' as the separator: PoiData.Id is a Guid string, which cannot contain one, so the two parts can
        /// never be ambiguous. Ids are also persisted verbatim by WorkspaceOps.Serialize, which escapes tabs
        /// and newlines and has no opinion about anything else.</summary>
        static SurfaceRef InteriorSurface(InteriorData interior)
        {
            if (interior == null) return null;
            switch (interior.Kind)
            {
                case InteriorKind.Settlement:
                    return new SurfaceRef { Kind = SurfaceKind.Settlement, Id = interior.OwnerPoiId ?? "" };
                case InteriorKind.Building:
                    return new SurfaceRef
                    {
                        Kind = SurfaceKind.BuildingInterior,
                        Id = $"{interior.OwnerPoiId}#{interior.OwnerRoomId}",
                    };
                default:
                    return new SurfaceRef { Kind = SurfaceKind.Dungeon, Id = interior.OwnerPoiId ?? "" };
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
                Id = $"{interior.Id}#{battleGridFloorIndex}#{battleGridRoomId}",
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
        // documentController.OpenPage(id), i.e. it re-binds from the id every call; this is the same idea for
        // the five screens that have no such call of their own.
        //
        // WHY THE ID IS ENOUGH: every surface id here is a pure function of the data (see PoiEditorSurface /
        // InteriorSurface / BattleGridSurface), so it can be reversed back to the object without any stored
        // side table. That is the same property Task 11's WorkspacePrefs needs to restore tabs across a
        // restart, so this reversal is not scaffolding — it is the half of the id contract that had no reader
        // yet.
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
                case SurfaceKind.BuildingInterior: RebindInterior(kind, id); break;
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

        void RebindInterior(SurfaceKind kind, string id)
        {
            var interior = ResolveInterior(kind, id);
            if (interior == null || dungeonEditorScreen == null) return;
            if (ReferenceEquals(editingDungeon, interior)) return;

            // parentTown is re-derived rather than left alone, because it is what «← Город» reads: switching
            // to a BUILDING's tab must make the back button go to THAT building's town, and switching to any
            // top-level interior must clear it (the same stale-parent rule OpenDungeonEditor enforces for the
            // map entry path). The town is the FIRST interior for the poiId — DungeonManager.GetByPoiId, the
            // same lookup OpenBuildingInterior's own comment says cannot serve a BUILDING lookup and can
            // therefore serve this one exactly.
            parentTown = kind == SurfaceKind.BuildingInterior && dungeonManager != null
                ? dungeonManager.GetByPoiId(interior.OwnerPoiId)
                : null;
            if (ReferenceEquals(parentTown, interior)) parentTown = null;   // a town is not its own parent

            editingDungeon = interior;
            if (kind == SurfaceKind.BuildingInterior)
                dungeonEditorScreen.Bind(interior, BuildingHeaderFor(parentTown, interior.OwnerRoomId));
            else
                dungeonEditorScreen.Bind(interior, roomsWithInterior: RoomsWithInteriorFor(interior));
        }

        void RebindBattleGrid(string id)
        {
            // Parsed from the RIGHT: the interior part of this id may itself contain a '#' (a building
            // interior is "poiId#roomId"), so the floor and room are the LAST two segments and everything
            // before them is the interior id. Splitting from the left would mis-read every battle grid inside
            // a building.
            int lastSep = id != null ? id.LastIndexOf('#') : -1;
            if (lastSep <= 0) return;
            int prevSep = id.LastIndexOf('#', lastSep - 1);
            if (prevSep <= 0) return;

            if (!int.TryParse(id.Substring(lastSep + 1), out int roomId)) return;
            if (!int.TryParse(id.Substring(prevSep + 1, lastSep - prevSep - 1), out int floorIndex)) return;
            string interiorId = id.Substring(0, prevSep);

            // The interior id's own shape says which kind it is: a building's carries a '#', a settlement's or
            // dungeon's does not. Settlement and Dungeon resolve identically (GetByPoiId), so guessing between
            // those two costs nothing — and a settlement can never own a battle grid anyway (OpenBattleGrid
            // returns early for one).
            var interior = ResolveInterior(
                interiorId.IndexOf('#') >= 0 ? SurfaceKind.BuildingInterior : SurfaceKind.Dungeon, interiorId);
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

        /// <summary>Reverses InteriorSurface: an id back to the InteriorData it names. Settlement and Dungeon
        /// are both "the first interior for this poiId" (DungeonManager.GetByPoiId); BuildingInterior splits
        /// "poiId#roomId" and goes through InteriorOps.FindBuildingInterior, the only lookup that can
        /// distinguish a SECOND interior for the same poiId.</summary>
        InteriorData ResolveInterior(SurfaceKind kind, string id)
        {
            if (dungeonManager == null || string.IsNullOrEmpty(id)) return null;
            if (kind != SurfaceKind.BuildingInterior) return dungeonManager.GetByPoiId(id);

            int sep = id.LastIndexOf('#');
            if (sep <= 0 || !int.TryParse(id.Substring(sep + 1), out int roomId)) return null;
            return InteriorOps.FindBuildingInterior(dungeonManager.GetAll(), id.Substring(0, sep), roomId);
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

        /// <summary>The live POI store, discovered on every miss like Shell()/Documents() and for the same two
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

        /// <summary>A POI's tab title. Falls back the same way NotesDocOps.EnsurePageFor's E3 does, and to the
        /// same string, so a nameless POI reads identically in the tab strip and in «Мир» rather than being
        /// «Без названия» in one place and blank in the other.</summary>
        static string PoiTitle(PoiData poi) =>
            poi != null && !string.IsNullOrWhiteSpace(poi.Name) ? poi.Name : "Без названия";

        /// <summary>An interior's tab title, used only on CloseDungeonEditor's «← Город» path where the town's
        /// own tab is expected to already exist (so this is the title of a tab that will normally not be
        /// created). The POI's real name is not reachable from an InteriorData, and going to PoiManager for it
        /// would add a manager reference for one fallback string — so this names the KIND, which is honest
        /// about what it knows.</summary>
        static string InteriorTitle(InteriorData interior) =>
            interior == null ? "" : interior.Kind == InteriorKind.Settlement ? "Город"
                : interior.Kind == InteriorKind.Building ? "Здание" : "Подземелье";

        /// <summary>Opens (or focuses, per WorkspaceOps.Open's R1) `s` in the FOCUSED pane. A no-op when there
        /// is no workspace in the scene — the transitional state this class's own doc names — and when `s` is
        /// null, which is how the Close*/Open* paths pass state that is not set.</summary>
        void OpenSurfaceTab(SurfaceRef s, string title)
        {
            if (s == null) return;
            Shell()?.Open(s, title, inOtherPane: false);
        }

        /// <summary>Closes `s`'s tab wherever it is. Null-tolerant for the same reason OpenSurfaceTab is, and
        /// tolerant of the surface not being open at all — WorkspaceController.CloseSurface's own doc explains
        /// why that is ordinary rather than an error.</summary>
        void CloseSurfaceTab(SurfaceRef s)
        {
            if (s == null) return;
            Shell()?.CloseSurface(s);
        }

        /// <summary>Creates the POI's page if it has none, which is the ONLY thing that puts the POI in the
        /// navigator's «Мир» group (NotesDocOps.EnsurePageFor is the single writer of NotesPage.Bound, and
        /// NavigatorTree derives membership from it). Existing pages are reused, never duplicated — E1.
        ///
        /// RAISES OnDocumentChanged EXPLICITLY rather than relying on the OpenSurfaceTab that follows it. The
        /// navigator rebuilds on either OnDocumentChanged or WorkspaceController.OnLayoutChanged
        /// (NavigatorView.cs:99-100), and every call site here does follow this with an Open — so the tree
        /// would refresh anyway, today, by ordering. Task 10b's review flagged that as a trap worth closing
        /// rather than documenting: a future path that creates a page WITHOUT opening a tab (or an Open that
        /// no-ops because the tab was already there and focused — WorkspaceController.Open still raises, but
        /// a future short-circuit might not) would leave the place silently absent from «Мир».
        ///
        /// The document controller is DISCOVERED, like the workspace: NotesRootBuilder owns the live
        /// NotesDocumentController and this class has no Inspector reference to it (adding one would need a
        /// scene edit Task 11 owns). EnsurePageFor(null-doc, ...) already returns null (E4), so a scene
        /// without notes degrades to "no page, no «Мир» row" rather than throwing.</summary>
        string EnsurePageForPoi(PoiData poi)
        {
            if (poi == null) return null;
            var docController = Documents();
            var doc = docController != null ? docController.Document : null;
            if (doc == null) return null;

            string pageId = NotesDocOps.EnsurePageFor(
                doc, new WorldRef { Kind = WorldRefKind.Poi, Id = poi.Id }, PoiTitle(poi));
            if (pageId == null) return null;
            docController.NotifyDocumentChanged();
            return pageId;
        }

        /// <summary>Opens a POI's PAGE — the Р1-reachable half of the spec's "clicking a place on the map":
        /// a single click only SELECTS (a stray click must never throw the user out of the map), and the page
        /// opens on an explicit action. Double-clicking the marker is that action for Р1; the inspector's
        /// «Открыть страницу →» is Р5's. Wired from PoiInteractionController.OnRelease.
        ///
        /// IN THE OTHER PANE WHEN A SPLIT EXISTS, per the spec, otherwise a new tab in the focused pane. That
        /// is the one place in this file that passes inOtherPane: true, and it is the point of the gesture —
        /// the user is reading a place's notes NEXT TO the map they clicked it on, not instead of it.
        ///
        /// DELIBERATELY NOT the POI editor: OpenPoiEditor (the popup's «Редактировать») opens the editor's own
        /// tab and merely ENSURES the page exists. Two gestures, two surfaces, one shared EnsurePageFor.
        ///
        /// LIVES HERE, not in PoiInteractionController, even though the gesture is wired there: this class
        /// already owns the discovered NotesDocumentController and WorkspaceController (see Documents() and
        /// Shell()), and giving the interaction controller its own copies would be two more references to go
        /// stale after a domain reload for no gain.</summary>
        public void OpenPoiPage(PoiData poi)
        {
            string pageId = EnsurePageForPoi(poi);
            if (pageId == null) return;   // no document in this scene, or a null POI — nothing to open.

            var shell = Shell();
            if (shell == null) return;
            bool split = shell.Layout != null && shell.Layout.Secondary != null;
            shell.Open(new SurfaceRef { Kind = SurfaceKind.Page, Id = pageId }, PoiTitle(poi), inOtherPane: split);
        }

        /// <summary>The live notes document controller, discovered on every miss for the same reasons Shell()
        /// is — no Inspector slot until Task 11, and a domain reload wipes the cached reference while the
        /// component survives. FindObjectsInactive.Include because NotesRootBuilder's own GameObject may be
        /// inactive depending on which screen is showing.</summary>
        NotesDocumentController Documents()
        {
            if (documentController != null) return documentController;
            var notesRoot = FindFirstObjectByType<NotesRootBuilder>(FindObjectsInactive.Include);
            if (notesRoot == null) return null;
            notesRoot.EnsureBuilt();   // idempotent; Awake ordering across GameObjects is undefined.
            documentController = notesRoot.DocumentController;
            return documentController;
        }

        NotesDocumentController documentController;

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

            RefreshScreenState(); // hasMap is still false here, but activeGeneration isn't set yet either -- set it first
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
