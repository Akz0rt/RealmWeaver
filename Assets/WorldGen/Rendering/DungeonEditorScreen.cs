using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Full-screen dungeon editor (mutually-exclusive AppScreen.Dungeon, opened from the POI
    /// editor). Hosts the draggable room-graph canvas (DungeonViewController + DungeonFlatRenderer) in
    /// MapArea, with a toolbar (+ Комната / Связать / Удалить) below the top strip; the room inspector +
    /// validation panel (DungeonInspectorPanel, Task 5) is hosted in Sidebar. Built imperatively at Awake,
    /// own-canvas pattern (mirrors PoiEditorScreen).</summary>
    public class DungeonEditorScreen : MonoBehaviour
    {
        public System.Action OnCloseRequested;      // wired to MapScreenController.CloseDungeonEditor

        InteriorData current;
        public int CurrentLevelIndex { get; private set; }
        public InteriorFloor CurrentLevel =>
            current != null && CurrentLevelIndex >= 0 && CurrentLevelIndex < current.Floors.Count
                ? current.Floors[CurrentLevelIndex] : null;

        public RectTransform MapArea { get; private set; }     // graph canvas host (Task 4)
        public RectTransform Sidebar { get; private set; }     // inspector host (Task 5)
        Transform levelTabsRow;
        Text titleLabel;

        DungeonViewController viewController;
        DungeonFlatRenderer flatRenderer;
        // DungeonIsoRenderer isoRenderer;   // deferred — not yet built
        DungeonInspectorPanel inspectorPanel;
        Image linkToggleImg;
        int selectedRoomId;   // mirrors DungeonViewController.SelectedRoomId; drives inspectorPanel.ShowRoom

        // Toolbar swaps per floor: free-edit (dungeon / building floor 0) vs generate-only (building upper
        // floors — a room-count stepper + «Перегенерировать» + a failure message). Rebuilt on every SetLevel.
        Transform toolbarBar;
        Text upperCountLabel;
        Text regenMsgLabel;
        int upperRoomCount = DefaultRooms;   // desired room count for the generate-only «Перегенерировать»

        Font font;
        bool built;

        const float StripHeight = 44f;
        const float ToolbarHeight = 36f;
        const int DefaultBuildingFloors = 2;
        const int DefaultRooms = 6;
        const int MaxUpperRooms = 20;    // hard ceiling for the generate-only stepper (below the area cap when it's tighter)
        const int RegenAttempts = 16;    // seeds tried per «Перегенерировать»: within the area cap, one press reliably packs

        void Awake() { if (isActiveAndEnabled) EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            built = true;
        }

        /// <summary>Bind a dungeon; ensure it has at least one level; show level 0.</summary>
        public void Bind(InteriorData dungeon)
        {
            EnsureBuilt();
            current = dungeon;
            if (current.Floors.Count == 0)
            {
                if (current.Kind == InteriorKind.Building)
                    current.Floors.AddRange(BuildingGenerator.Generate(FreshSeed(), current.OwnerPoiId, DefaultRooms, DefaultBuildingFloors).Floors);
                else
                    current.Floors.Add(DungeonGraphGenerator.Generate(FreshSeed(), DefaultRooms));
            }
            SetLevel(0);
        }

        public void SetLevel(int index)
        {
            if (current == null || current.Floors.Count == 0) return;
            CurrentLevelIndex = Mathf.Clamp(index, 0, current.Floors.Count - 1);
            // DungeonViewController.Bind resets ITS OWN SelectedRoomId to 0 on a level switch (different
            // bound InteriorFloor) but doesn't fire OnRoomSelected to say so — reset our mirror here too,
            // otherwise a stale id could coincidentally match an unrelated room on the new level and the
            // inspector would show the wrong room while the canvas shows no selection.
            selectedRoomId = 0;
            RebuildLevelTabs();
            RefreshToolbar();   // free-edit vs generate-only, per the floor we just switched to
            RefreshBody();
            RevalidateAndRefresh();
        }

        public void AddLevel()
        {
            if (current == null) return;
            if (current.Kind == InteriorKind.Building)
            {
                // A new building floor is GENERATED around the shared stairwell column (spec 2026-07-19): a
                // Лестница of the column's footprint at the column (x,y) + rooms packed within floor 0's outline.
                // The column comes from floor 0 (user-placed; auto-designated if missing). The new floor's
                // Лестница is joined to the PREVIOUS top floor's Лестница by a Stairs portal up the column.
                var column = BuildingGenerator.EnsureFloorZeroColumn(current);
                if (column == null) return;   // floor 0 has no room to host a stairwell — nothing to build on
                var floor0 = current.Floors[0];
                int T = DungeonLayout.TilesPerAxis;
                var floor = BuildingGenerator.GenerateFloorAroundColumn(
                    new System.Random(FreshSeed()), DefaultRooms,
                    column.X * T, column.Y * T, column.SizeW, column.SizeH, floor0, out var newStair);

                int lowerIdx = current.Floors.Count - 1;
                var lower = current.Floors[lowerIdx];
                Room lowerStair = null;
                foreach (var r in lower.Rooms) if (r.TypeId == BuildingGenerator.StairTypeId) { lowerStair = r; break; }
                if (lowerStair == null && lower.Rooms.Count > 0) lowerStair = lower.Rooms[0];   // malformed-floor fallback

                current.Floors.Add(floor);
                if (lowerStair != null)
                    lowerStair.Portals.Add(new Portal
                    {
                        Kind = PortalKind.Stairs,
                        Hidden = false,
                        TargetFloorIndex = lowerIdx + 1,
                        TargetRoomId = newStair.Id,
                        Bidirectional = true,
                        Label = "Лестница",
                    });
            }
            else
                current.Floors.Add(DungeonGraphGenerator.Generate(FreshSeed(), DefaultRooms));
            SetLevel(current.Floors.Count - 1);
        }

        // A building is a vertical stack on the stairwell column, so it only ever loses its TOP floor —
        // removing a MIDDLE floor would sever the column (the stair down is dropped and the stair up dies with
        // the floor, leaving disconnected floors). Dungeons remove the currently-selected floor as before.
        int FloorToRemove() => (current != null && current.Kind == InteriorKind.Building)
            ? current.Floors.Count - 1 : CurrentLevelIndex;

        public void RemoveCurrentLevel()
        {
            if (current == null || current.Floors.Count <= 1) return;
            DungeonOps.RemoveLevel(current, FloorToRemove());
            SetLevel(Mathf.Min(CurrentLevelIndex, current.Floors.Count - 1));
        }

        /// <summary>«× Этаж» handler: if the level has authored room content, confirm before discarding
        /// it (deleting a floor loses all its rooms/corridors/notes — irreversible once the project is
        /// saved); otherwise remove directly. ConfirmDialog.Show's «Удалить» is the correct label here.</summary>
        void RequestRemoveCurrentLevel()
        {
            if (current == null || current.Floors.Count <= 1) return;
            var lvl = current.Floors[FloorToRemove()];   // the floor that will actually be removed (top, for buildings)
            bool annotated = lvl.Rooms.Exists(r => !string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.Body));
            if (annotated)
                WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Удалить этаж?",
                    "Все комнаты, связи и заметки этого этажа будут потеряны.", ok => { if (ok) RemoveCurrentLevel(); });
            else
                RemoveCurrentLevel();
        }

        // Body refresh — (re)binds the graph canvas and inspector to the current level. Called from
        // Bind/SetLevel (a real level switch); structural mutations after that go through the lighter
        // RevalidateAndRefresh instead (same level object, just its contents changed).
        void RefreshBody()
        {
            if (current == null) return;
            if (viewController != null) viewController.Bind(current, CurrentLevelIndex, font);
            if (inspectorPanel != null) inspectorPanel.Bind(current, () => CurrentLevelIndex, font);
        }

        // Re-runs validation and re-renders the graph + inspector in place (no rebind — the bound
        // InteriorFloor object is unchanged, only its Rooms/Links/Portals contents mutated via
        // DungeonOps). Wired as DungeonViewController.OnGraphMutated (fires on add/delete/link AND card
        // drag-end) and DungeonInspectorPanel.OnChanged (fires on any inspector edit, including the size
        // steppers), and called once at the end of SetLevel so a level switch also gets a fresh
        // validation pass. This is the SINGLE path that runs the cascade: viewController.BeginCascade()
        // owns the DungeonLayout.Separate call AND the (animated-or-skipped) redraw — it mutates Room.X/Y
        // and fires no callbacks of its own, so calling it here cannot loop back into RevalidateAndRefresh,
        // and both drag-release and a size-stepper edit converge on the same settle-then-redraw sequence.
        // Nothing else may call DungeonLayout.Separate directly — that would double-separate.
        void RevalidateAndRefresh()
        {
            if (viewController != null) viewController.BeginCascade();
            if (inspectorPanel != null)
            {
                inspectorPanel.ShowValidation(DungeonValidator.Validate(current));
                inspectorPanel.ShowRoom(selectedRoomId);
            }
        }

        int FreshSeed() => Random.Range(int.MinValue, int.MaxValue);

        void BuildUI()
        {
            var canvasGO = new GameObject("DungeonEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 101 (not 100) so this full-screen editor draws ABOVE the persistent ProjectMenuBar
            // (sortingOrder 100). At an equal order the tie-break is hierarchy-dependent and the menu
            // bar was winning, occluding this screen's own top strip (← Назад / title / level tabs).
            // Dialogs/dropdowns live at 30000+, so ConfirmDialog still renders above this.
            canvas.sortingOrder = 101;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var root = new GameObject("Root");
            root.transform.SetParent(canvasGO.transform, false);
            var rootImg = root.AddComponent<Image>();
            ThemeService.Tag(rootImg, ThemeRole.Bg);
            Stretch(root.GetComponent<RectTransform>());

            BuildTopStrip(root.transform);
            BuildToolbar(root.transform);
            BuildBody(root.transform);
        }

        void BuildTopStrip(Transform parent)
        {
            var strip = new GameObject("TopStrip", typeof(RectTransform));
            strip.transform.SetParent(parent, false);
            var sr = strip.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f); sr.sizeDelta = new Vector2(0f, StripHeight); sr.anchoredPosition = Vector2.zero;
            var stripBg = strip.AddComponent<Image>();
            ThemeService.Tag(stripBg, ThemeRole.Panel2);

            var backGO = new GameObject("Back");
            backGO.transform.SetParent(strip.transform, false);
            var backImg = backGO.AddComponent<Image>();
            ThemeService.Tag(backImg, ThemeRole.Elev);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.onClick.AddListener(() => OnCloseRequested?.Invoke());
            var backRect = backGO.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0.5f); backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f); backRect.sizeDelta = new Vector2(110f, 28f); backRect.anchoredPosition = new Vector2(12f, 0f);
            var backLbl = MakeText(backGO.transform, "← Назад", 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(backLbl.rectTransform); backLbl.raycastTarget = false;

            titleLabel = MakeText(strip.transform, "Подземелье", 14, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleLeft);
            var tr = titleLabel.rectTransform;
            tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(0f, 0.5f);
            tr.pivot = new Vector2(0f, 0.5f); tr.anchoredPosition = new Vector2(134f, 0f); tr.sizeDelta = new Vector2(200f, 28f);

            var tabsGO = new GameObject("LevelTabs", typeof(RectTransform));
            tabsGO.transform.SetParent(strip.transform, false);
            var hlg = tabsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true; hlg.childAlignment = TextAnchor.MiddleLeft;
            var tabsRect = tabsGO.GetComponent<RectTransform>();
            tabsRect.anchorMin = new Vector2(0f, 0.5f); tabsRect.anchorMax = new Vector2(0f, 0.5f);
            tabsRect.pivot = new Vector2(0f, 0.5f); tabsRect.anchoredPosition = new Vector2(344f, 0f); tabsRect.sizeDelta = new Vector2(300f, 28f);
            levelTabsRow = tabsGO.transform;
        }

        /// <summary>Toolbar row below the top strip: add/link/delete controls for the graph canvas.
        /// «Связать» toggles DungeonViewController.LinkMode and highlights (AccentSoft) while active.</summary>
        void BuildToolbar(Transform parent)
        {
            var bar = new GameObject("Toolbar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var br = bar.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0f, 1f); br.anchorMax = new Vector2(1f, 1f);
            br.pivot = new Vector2(0.5f, 1f); br.sizeDelta = new Vector2(0f, ToolbarHeight);
            br.anchoredPosition = new Vector2(0f, -StripHeight);
            var bg = bar.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel);

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.padding = new RectOffset(12, 12, 4, 4);
            hlg.childControlWidth = false; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            toolbarBar = bar.transform;
            RefreshToolbar();   // fills in the buttons for the current floor (free-edit by default until Bind)
        }

        /// <summary>(Re)build the toolbar for the CURRENT floor. Dungeons and a building's ground floor get the
        /// free-edit controls (+ Комната / Связать / Удалить). A building's UPPER floor is GENERATE-ONLY (spec
        /// stairwell stage B): a room-count stepper + «Перегенерировать» + a failure message — no free editing.
        /// Called on every SetLevel (and once at build time). BuildToolbar can run before Bind sets `current`,
        /// so fall back to the dungeon profile (same profile a dungeon would resolve to).</summary>
        void RefreshToolbar()
        {
            if (toolbarBar == null) return;
            for (int i = toolbarBar.childCount - 1; i >= 0; i--) Destroy(toolbarBar.GetChild(i).gameObject);
            linkToggleImg = null; upperCountLabel = null; regenMsgLabel = null;
            viewController?.SetLinkMode(false);   // link mode is per-floor — a floor switch exits it

            bool generateOnly = current != null && current.Kind == InteriorKind.Building && CurrentLevelIndex > 0;
            if (generateOnly)
            {
                int cap = UpperCap();
                upperRoomCount = Mathf.Clamp(CurrentLevel != null ? CurrentLevel.Rooms.Count : DefaultRooms, 1, cap);
                AddToolbarLabel(toolbarBar, "Комнаты:", 76f);
                AddToolbarButton(toolbarBar, "−", 32f, ThemeRole.Elev, () => AdjustUpperRoomCount(-1));
                upperCountLabel = AddToolbarLabel(toolbarBar, upperRoomCount.ToString(), 30f);
                AddToolbarButton(toolbarBar, "+", 32f, ThemeRole.Elev, () => AdjustUpperRoomCount(+1));
                AddToolbarLabel(toolbarBar, $"из {cap}", 52f);   // the contour's deterministic area capacity
                AddToolbarButton(toolbarBar, "Перегенерировать", 150f, ThemeRole.Accent, RegenerateUpperFloor);
                regenMsgLabel = AddToolbarLabel(toolbarBar, "", 320f);
                ThemeService.Tag(regenMsgLabel, ThemeRole.Danger);
            }
            else
            {
                var profile = current != null ? Profiles.ForRoom(current) : Profiles.For(InteriorKind.Dungeon);
                AddToolbarButton(toolbarBar, "+ " + profile.TermRoom, 110f, ThemeRole.Elev, () => viewController?.AddRoomAtCenter());
                linkToggleImg = AddToolbarButton(toolbarBar, "Связать", 90f, ThemeRole.Elev, ToggleLinkMode);
                AddToolbarButton(toolbarBar, "Удалить", 90f, ThemeRole.Elev, () => viewController?.DeleteSelected());
            }
        }

        void AdjustUpperRoomCount(int delta)
        {
            upperRoomCount = Mathf.Clamp(upperRoomCount + delta, 1, UpperCap());
            if (upperCountLabel != null) upperCountLabel.text = upperRoomCount.ToString();
        }

        /// <summary>Deterministic max total room count (column + rooms) that fits the CURRENT upper floor by AREA,
        /// capped at the stepper's hard ceiling. Non-mutating (reads floor 0's existing column, never designates
        /// one), so it is safe from the toolbar refresh. Falls back to the ceiling when floor 0 has no column
        /// yet — there is nothing to size a capacity against until one exists.</summary>
        int UpperCap()
        {
            var col = current != null ? BuildingGenerator.FindFloorZeroColumn(current) : null;
            if (col == null) return MaxUpperRooms;
            return Mathf.Clamp(BuildingGenerator.MaxRoomsByArea(current.Floors[0], col.SizeW, col.SizeH), 1, MaxUpperRooms);
        }

        /// <summary>«Перегенерировать» for a building UPPER floor: rebuild it around the shared column with the
        /// requested room count. Atomic — if the count can't fit floor 0's contour, NOTHING changes and the DM
        /// is told the reason; otherwise the floor is replaced and its stairs re-linked up/down the column.</summary>
        void RegenerateUpperFloor()
        {
            if (current == null || current.Kind != InteriorKind.Building || CurrentLevelIndex <= 0) return;
            // Regenerate REPLACES the whole floor, discarding any authored room content. Mirror the floor-
            // removal safeguard: confirm first when the floor has named/annotated rooms (there is no undo).
            var lvl = CurrentLevel;
            bool annotated = lvl != null && lvl.Rooms.Exists(r => !string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.Body));
            if (annotated)
                WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Перегенерировать этаж?",
                    "Комнаты этого этажа, их названия и заметки будут заменены.", ok => { if (ok) DoRegenerateUpperFloor(); });
            else
                DoRegenerateUpperFloor();
        }

        void DoRegenerateUpperFloor()
        {
            if (current == null || current.Kind != InteriorKind.Building || CurrentLevelIndex <= 0) return;
            var column = BuildingGenerator.EnsureFloorZeroColumn(current);
            if (column == null) { ShowRegenMsg("Добавьте лестницу на 1-м этаже"); return; }

            // Deterministic verdict FIRST: compare the contour's area against what the requested rooms need. This
            // is seed-independent, so an over-capacity count is rejected the SAME way every press (the old check
            // ran a single greedy pack, which could fail one seed and succeed the next — the flip-flop the DM saw).
            int cap = Mathf.Clamp(BuildingGenerator.MaxRoomsByArea(current.Floors[0], column.SizeW, column.SizeH), 1, MaxUpperRooms);
            if (upperRoomCount > cap)
            {
                upperRoomCount = cap;   // pin the stepper to the real limit so the next press succeeds
                if (upperCountLabel != null) upperCountLabel.text = upperRoomCount.ToString();
                ShowRegenMsg($"В контур помещается не более {cap} комнат");
                return;   // floor unchanged — nothing was generated
            }

            // Within the area cap a single greedy pack can still miss on an unlucky room-size roll; retry fresh
            // seeds so ONE press reliably packs (no "won't fit -> press again -> fits").
            int T = DungeonLayout.TilesPerAxis;
            InteriorFloor newFloor = null; Room newStair = null; bool fits = false;
            for (int a = 0; a < RegenAttempts && !fits; a++)
                fits = BuildingGenerator.TryGenerateFloorAroundColumn(FreshSeed(), upperRoomCount,
                    column.X * T, column.Y * T, column.SizeW, column.SizeH, current.Floors[0], out newFloor, out newStair);
            if (!fits) { ShowRegenMsg($"Не удалось разместить {upperRoomCount} — уменьшите количество"); return; }

            ReplaceCurrentUpperFloor(newFloor, newStair);
            selectedRoomId = 0;   // old room ids are gone — clear the mirror so the inspector doesn't show a stale room
            ShowRegenMsg("");
            RefreshBody();
            RevalidateAndRefresh();
        }

        // Swap the current upper floor for a freshly generated one and re-link the stairwell: the floor BELOW
        // now targets the new floor's Лестница, and (if a floor exists above) the new Лестница links up to it.
        void ReplaceCurrentUpperFloor(InteriorFloor newFloor, Room newStair)
        {
            int k = CurrentLevelIndex;
            current.Floors[k] = newFloor;
            foreach (var r in current.Floors[k - 1].Rooms)
                foreach (var p in r.Portals)
                    if (p.Kind == PortalKind.Stairs && p.TargetFloorIndex == k) p.TargetRoomId = newStair.Id;
            if (k + 1 < current.Floors.Count)
            {
                Room above = null;
                foreach (var r in current.Floors[k + 1].Rooms) if (r.TypeId == BuildingGenerator.StairTypeId) { above = r; break; }
                if (above != null)
                    newStair.Portals.Add(new Portal
                    {
                        Kind = PortalKind.Stairs, Hidden = false, TargetFloorIndex = k + 1,
                        TargetRoomId = above.Id, Bidirectional = true, Label = "Лестница",
                    });
            }
        }

        void ShowRegenMsg(string msg) { if (regenMsgLabel != null) regenMsgLabel.text = msg; }

        Text AddToolbarLabel(Transform parent, string text, float width)
        {
            var lbl = MakeText(parent, text, 12, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleLeft);
            lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            lbl.raycastTarget = false;
            return lbl;
        }

        void ToggleLinkMode()
        {
            if (viewController == null) return;
            viewController.SetLinkMode(!viewController.LinkMode);
            if (linkToggleImg != null) ThemeService.Tag(linkToggleImg, viewController.LinkMode ? ThemeRole.AccentSoft : ThemeRole.Elev);
        }

        Image AddToolbarButton(Transform parent, string label, float width, ThemeRole bgRole, System.Action onClick)
        {
            var go = new GameObject($"Tool_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, bgRole);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var lbl = MakeText(go.transform, label, 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
            return img;
        }

        void BuildBody(Transform parent)
        {
            const float sidebarWidth = 300f;

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(parent, false);
            var br = body.GetComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
            br.offsetMin = Vector2.zero; br.offsetMax = new Vector2(0f, -(StripHeight + ToolbarHeight));

            var mapGO = new GameObject("MapArea", typeof(RectTransform));
            mapGO.transform.SetParent(body.transform, false);
            MapArea = mapGO.GetComponent<RectTransform>();
            MapArea.anchorMin = new Vector2(0f, 0f); MapArea.anchorMax = new Vector2(1f, 1f);
            MapArea.offsetMin = new Vector2(12f, 12f); MapArea.offsetMax = new Vector2(-(sidebarWidth + 18f), -12f);
            var mapBg = mapGO.AddComponent<Image>();
            ThemeService.Tag(mapBg, ThemeRole.Panel2); mapBg.raycastTarget = true;

            // One interaction host stretched over MapArea; the renderer is its child. The controller carries
            // a full-area invisible hit-plate (it IS the raycast target) and hit-tests in TILE space, so the
            // renderer needs no input handling of its own. A second (isometric) renderer is deferred — it
            // would plug into this same host through SetRenderer, same as flatRenderer below.
            var viewGO = new GameObject("DungeonView", typeof(RectTransform));
            viewGO.transform.SetParent(MapArea, false);
            Stretch(viewGO.GetComponent<RectTransform>());
            var hitImg = viewGO.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);   // invisible hit-plate (mirrors PoiEditorScreen's Viewport mask)
            hitImg.raycastTarget = true;
            viewController = viewGO.AddComponent<DungeonViewController>();
            viewController.OnRoomSelected = id => { selectedRoomId = id; inspectorPanel?.ShowRoom(id); };
            viewController.OnGraphMutated = RevalidateAndRefresh;
            viewController.OnJumpToLevel = SetLevel;

            var flatGO = new GameObject("FlatRenderer", typeof(RectTransform));
            flatGO.transform.SetParent(viewGO.transform, false);
            Stretch(flatGO.GetComponent<RectTransform>());
            flatRenderer = flatGO.AddComponent<DungeonFlatRenderer>();

            viewController.SetRenderer(flatRenderer);   // the only renderer today; the seam a deferred iso renderer plugs into

            var sidebarGO = new GameObject("Sidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(body.transform, false);
            Sidebar = sidebarGO.GetComponent<RectTransform>();
            Sidebar.anchorMin = new Vector2(1f, 0f); Sidebar.anchorMax = new Vector2(1f, 1f);
            Sidebar.offsetMin = new Vector2(-(sidebarWidth + 12f), 12f); Sidebar.offsetMax = new Vector2(-12f, -12f);
            var sidebarBg = sidebarGO.AddComponent<Image>();
            ThemeService.Tag(sidebarBg, ThemeRole.Elev); sidebarBg.raycastTarget = false;

            var inspGO = new GameObject("InspectorPanel", typeof(RectTransform));
            inspGO.transform.SetParent(Sidebar, false);
            Stretch(inspGO.GetComponent<RectTransform>());
            inspectorPanel = inspGO.AddComponent<DungeonInspectorPanel>();
            inspectorPanel.OnChanged = RevalidateAndRefresh;
        }

        void RebuildLevelTabs()
        {
            if (levelTabsRow == null || current == null) return;
            var profile = Profiles.ForRoom(current);
            for (int i = levelTabsRow.childCount - 1; i >= 0; i--) Destroy(levelTabsRow.GetChild(i).gameObject);
            for (int i = 0; i < current.Floors.Count; i++)
            {
                int idx = i;
                AddLevelTabButton($"Ур.{i + 1}", 50f, idx == CurrentLevelIndex, () => SetLevel(idx));
            }
            AddLevelTabButton("+ " + profile.TermFloor, 64f, false, AddLevel);
            if (current.Floors.Count > 1) AddLevelTabButton("× " + profile.TermFloor, 64f, false, RequestRemoveCurrentLevel);
        }

        void AddLevelTabButton(string label, float width, bool active, System.Action onClick)
        {
            var go = new GameObject($"Tab_{label}");
            go.transform.SetParent(levelTabsRow, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, active ? ThemeRole.AccentSoft : ThemeRole.Elev);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var lbl = MakeText(go.transform, label, 12, active ? ThemeRole.AccentInk : ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        Text MakeText(Transform parent, string content, int size, ThemeRole role, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = content; text.font = font; text.fontSize = size; text.fontStyle = style;
            ThemeService.Tag(text, role); text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }
    }
}
