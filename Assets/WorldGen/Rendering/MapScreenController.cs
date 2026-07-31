using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
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
        int battleGridRoomId;        // 0 = the battle grid screen is closed
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
            editingPoi = null; // a fresh world drops any open POI editor
            editingDungeon = null; // a fresh world drops any open dungeon editor
            parentTown = null;   // ...and any building-from-town back-target it was carrying
            battleGridRoomId = 0;   // a fresh world drops the battle grid screen too
            RefreshScreenState();
        }

        /// <summary>Opens the full-screen POI editor for a point (single source of truth for both the
        /// info-popup «Редактировать» button and double-click). Hides the popup + map view.</summary>
        public void OpenPoiEditor(PoiData poi)
        {
            if (poi == null) return;
            editingPoi = poi;
            if (poiInfoPopup != null) poiInfoPopup.Hide();
            if (poiEditorScreen != null) poiEditorScreen.Bind(poi);
            RefreshScreenState();
        }

        /// <summary>Closes the POI editor and returns to the world map.</summary>
        public void ClosePoiEditor()
        {
            editingPoi = null;
            RefreshScreenState();
        }

        /// <summary>Opens the cave-dungeon editor for a POI (get-or-create its dungeon). Returns to the
        /// POI editor on close (editingPoi stays set), not the world map.</summary>
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
        /// «← Город» rebinds the town interior in place and stays on AppScreen.Dungeon — it must NOT fall
        /// through to the editingDungeon=null branch below, which would instead drop to the POI editor.
        /// Otherwise («← Назад» on a top-level dungeon/building/settlement) behaviour is unchanged.</summary>
        public void CloseDungeonEditor()
        {
            if (parentTown != null)
            {
                editingDungeon = parentTown;
                parentTown = null;
                // Recomputed here, not carried over from the original OpenDungeonEditor bind — the DM may
                // just have opened a building interior for the FIRST time (OpenBuildingInterior generates
                // and persists it on demand), which grows the set between the town's original bind and this
                // return trip.
                if (dungeonEditorScreen != null)
                    dungeonEditorScreen.Bind(editingDungeon, roomsWithInterior: RoomsWithInteriorFor(editingDungeon));
                RefreshScreenState();
                return;
            }

            editingDungeon = null;   // editingPoi is still set → DesiredScreen returns PoiEditor
            RefreshScreenState();
            // The POI editor is re-SHOWN (SetActive), not re-Bound, so its «Карта локации» label would
            // keep its pre-dungeon "Создать" text. Refresh it here to reflect a dungeon just created —
            // Bind is otherwise the only place that computes it.
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
            RefreshScreenState();
        }

        /// <summary>Opens the battle map of a room on the CURRENTLY open interior floor. Returns to the
        /// interior screen on close (editingDungeon stays set), with the same room still selected.</summary>
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

            // Resolve BEFORE touching battleGridRoomId: DesiredScreen() switches on that field alone, so
            // setting it for a room that does not resolve would show the battle-grid screen with nothing
            // bound — blank on a first open, or still showing the PREVIOUS room's map on a later one, with
            // no error the DM can see in a standalone build. An id that cannot be resolved must leave the
            // DM exactly where they are.
            if (dungeonEditorScreen == null) return;
            int floorIndex = dungeonEditorScreen.CurrentLevelIndex;
            if (floorIndex < 0 || floorIndex >= editingDungeon.Floors.Count) return;
            if (editingDungeon.Floors[floorIndex].GetRoom(roomId) == null) return;

            battleGridRoomId = roomId;
            battleGridScreen.Bind(editingDungeon, floorIndex, roomId);
            RefreshScreenState();
        }

        public void CloseBattleGrid()
        {
            battleGridRoomId = 0;   // editingDungeon is still set → DesiredScreen returns Dungeon
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
            if (!hasMap) return AppScreen.Generation;
            return AppScreen.Workspace;
        }

        public void StartGeneration(WorldGen.Rendering.GenerationRequest uiParams)
        {
            if (activeGeneration != null) return;

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
