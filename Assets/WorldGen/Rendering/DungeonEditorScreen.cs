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
        int currentUpperCap = DefaultRooms;  // probed packable max for the current upper floor (set in RefreshToolbar)

        Font font;
        bool built;

        const float StripHeight = 44f;
        const float ToolbarHeight = 36f;
        const int DefaultBuildingFloors = 2;
        const int DefaultRooms = 6;
        const int MaxUpperRooms = 20;    // hard ceiling for the generate-only stepper (below the area cap when it's tighter)
        const int RegenAttempts = 24;    // seeds tried per «Перегенерировать»: with the conservative area cap, one press reliably packs

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
                    lowerStair.Portals.Add(BuildingGenerator.MakeStairPortal(lowerIdx + 1, newStair.Id));
            }
            else
                current.Floors.Add(DungeonGraphGenerator.Generate(FreshSeed(), DefaultRooms));
            SetLevel(current.Floors.Count - 1);
        }

        // A building can lose ANY floor except floor 0 (which defines the footprint and hosts the shared
        // stairwell column — never removable). Removing any OTHER floor used to sever the column (the floor
        // below loses its up-portal, since a Stairs portal targeting the removed floor is dropped), but
        // RemoveCurrentLevel now re-wires the whole chain from the surviving floors right after the removal
        // (BuildingGenerator.RewireStairChain), so the shaft never stays broken. Returns -1 — a value no
        // Floors index can be — as a defensive guard so a Building can never remove floor 0 even from an
        // unexpected call path; RebuildLevelTabs' canRemove already keeps the button off floor 0 in the normal
        // flow. Dungeons remove the currently-selected floor, unrestricted, as before.
        int FloorToRemove()
        {
            if (current != null && BuildingGenerator.IsFloorZeroLocked(current.Kind, CurrentLevelIndex)) return -1;
            return CurrentLevelIndex;
        }

        public void RemoveCurrentLevel()
        {
            if (current == null || current.Floors.Count <= 1) return;
            int idx = FloorToRemove();
            if (idx < 0) return;   // guarded: a Building's floor 0 is never removable
            DungeonOps.RemoveLevel(current, idx);
            // Re-wire the vertical Stairs chain from the surviving floors — removing a MIDDLE floor drops the
            // floor below's up-portal (RemoveLevel strips any Stairs portal that targeted the removed index),
            // so without this the shaft above the removal point would be stranded. A no-op when the removal
            // didn't touch the chain (e.g. the previously-supported top-floor case) or on a Dungeon.
            if (current.Kind == InteriorKind.Building) BuildingGenerator.RewireStairChain(current);
            SetLevel(Mathf.Min(CurrentLevelIndex, current.Floors.Count - 1));
        }

        /// <summary>«× Этаж» handler: if the level has authored room content, confirm before discarding
        /// it (deleting a floor loses all its rooms/corridors/notes — irreversible once the project is
        /// saved); otherwise remove directly. ConfirmDialog.Show's «Удалить» is the correct label here.</summary>
        void RequestRemoveCurrentLevel()
        {
            if (current == null || current.Floors.Count <= 1) return;
            int idx = FloorToRemove();
            if (idx < 0) return;   // guarded: a Building's floor 0 is never removable
            var lvl = current.Floors[idx];   // the floor that will actually be removed — the SELECTED one
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
            // Keep the vertical shaft aligned: if the DM moved/resized the column on floor 0, slide every upper
            // floor so its Лестница re-lands on the column (auto-realign). Idempotent — a no-op when already aligned.
            if (current != null && current.Kind == InteriorKind.Building)
                BuildingGenerator.RealignUpperFloorsToColumn(current);
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
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;   // honour each item's preferredWidth
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
                currentUpperCap = UpperCap();   // probe once per floor switch; +/- clamps against this cached value
                int cap = currentUpperCap;
                upperRoomCount = Mathf.Clamp(CurrentLevel != null ? CurrentLevel.Rooms.Count : DefaultRooms, 1, cap);
                AddToolbarLabel(toolbarBar, "Комнаты:", 76f);
                AddToolbarButton(toolbarBar, "−", 32f, ThemeRole.Elev, () => AdjustUpperRoomCount(-1));
                upperCountLabel = AddToolbarLabel(toolbarBar, upperRoomCount.ToString(), 30f);
                AddToolbarButton(toolbarBar, "+", 32f, ThemeRole.Elev, () => AdjustUpperRoomCount(+1));
                AddToolbarLabel(toolbarBar, $"из {cap}", 52f);   // the contour's deterministic area capacity
                AddToolbarButton(toolbarBar, "Перегенерировать", 185f, ThemeRole.Accent, RegenerateUpperFloor);
                // The layout is generate-only, but the DM may still author TRANSITIONS between rooms: «Связать»
                // adds a corridor (click two rooms); removal is in the inspector. Links don't move rooms, so the
                // generated layout is undisturbed. Drag stays locked (OnBeginDrag early-returns on upper floors).
                linkToggleImg = AddToolbarButton(toolbarBar, "Связать", 90f, ThemeRole.Elev, ToggleLinkMode);
                regenMsgLabel = AddToolbarLabel(toolbarBar, "", 300f);
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
            upperRoomCount = Mathf.Clamp(upperRoomCount + delta, 1, currentUpperCap);   // cached cap — no re-probe per click
            if (upperCountLabel != null) upperCountLabel.text = upperRoomCount.ToString();
        }

        /// <summary>Floor 0's stairwell column + the deterministic area capacity (total rooms, incl. the column)
        /// for the current building, both NON-mutating. False (with a null column) when floor 0 has no Лестница —
        /// the caller shows "add a stair on floor 0" rather than silently designating one (that stays a +этаж
        /// action). Used by the toolbar readout, the pre-confirm check, and the regen itself so all three agree.</summary>
        bool TryGetColumnAndCap(out Room column, out int cap)
        {
            column = current != null ? BuildingGenerator.FindFloorZeroColumn(current) : null;
            cap = 1;
            if (column == null) return false;
            int T = DungeonLayout.TilesPerAxis;
            var contour = current.Floors[0];
            var key = CapKey(column, contour);
            if (ReferenceEquals(capMemoData, current) && ReferenceEquals(capMemoContour, contour)
                && SameCapKey(capMemoKey, key))
            { cap = capMemoCap; return true; }
            // The displayed max is the ACTUAL packable count (probed), not an area estimate — so «из N» is real and
            // any count ≤ N is guaranteed to generate.
            cap = Mathf.Clamp(BuildingGenerator.MaxRoomsPackable(column.X * T, column.Y * T, column.SizeW, column.SizeH, contour), 1, MaxUpperRooms);
            capMemoData = current; capMemoContour = contour; capMemoKey = key; capMemoCap = cap;
            return true;
        }

        // ---- Probed-cap memo ---------------------------------------------------------------------------------
        // MaxRoomsPackable is TEN real packs (12-26 ms on a realistic floor 0 since the F4 lateral slide), and it
        // is asked for on EVERY floor-tab click (RefreshToolbar -> UpperCap) and AGAIN inside
        // DoRegenerateUpperFloor — almost always for a floor 0 that has not changed at all. This memo returns the
        // previous answer when the probe's input is unchanged and recomputes otherwise.
        //
        // A STALE cap would be a CORRECTNESS bug, not just a wrong readout: the DM would be shown an «из N» the
        // packer can no longer build, and "any count <= N is guaranteed to generate" would become false. So the
        // key is not a cheap fingerprint or a mutation counter — it is the probe's COMPLETE input, compared
        // element by element:
        //   • the InteriorData and the floor-0 InteriorFloor OBJECT identities (a different building, or a
        //     replaced floor-0 object, can never hit the memo);
        //   • the column pin — X, Y, SizeW, SizeH, the four scalars passed alongside the contour; and
        //   • floor 0's whole room list, in list order: Id, TypeId, X, Y, SizeW, SizeH for every room.
        // That is exhaustive, not a guess: MaxRoomsPackable touches the contour only through MaxRoomsByArea and
        // GenerateFloorAroundColumn, and BOTH reach it only via FloorFootprint, which reads exactly room.X/Y and
        // DungeonProjection.EffectiveSize(room) (= SizeW/SizeH, falling back to RoomSizing.Default(TypeId) when a
        // side is <= 0). Nothing else about a floor-0 room is observable to the probe. So every way the DM can
        // move the cap — free-editing floor 0, dragging a room, the size steppers, add/delete room, and
        // RealignUpperFloorsToColumn resizing/moving the column on RevalidateAndRefresh — changes this key first.
        // (Room list ORDER is part of the key too, which is strictly finer than needed; that direction is safe.)
        //
        // M-6: the column pin (key[0..3]) and the room list are NOT two independent parts of the key that happen
        // to agree — TryGetColumnAndCap's `column` comes from FindFloorZeroColumn -> FindStairRoom, which can only
        // ever return a member of `contour.Rooms` (floor 0's own Лестница room). So key[0..3] is always a byte-for-
        // byte copy of that same room's tail entry (Id/TypeId/X/Y/SizeW/SizeH) already present later in the array.
        // Redundant by construction, kept anyway so the key stands alone (and this proof doesn't have to be
        // re-derived) if the column ever stops being a floor-0 room.
        InteriorData capMemoData;       // the interior the memo belongs to
        InteriorFloor capMemoContour;   // its floor-0 object
        float[] capMemoKey;
        int capMemoCap;

        // The probe's whole input, flattened: [colX, colY, colW, colH] then 6 numbers per floor-0 room.
        static float[] CapKey(Room column, InteriorFloor contour)
        {
            var key = new float[4 + contour.Rooms.Count * 6];
            key[0] = column.X; key[1] = column.Y; key[2] = column.SizeW; key[3] = column.SizeH;
            int i = 4;
            foreach (var r in contour.Rooms)
            {
                key[i++] = r.Id; key[i++] = r.TypeId;
                key[i++] = r.X; key[i++] = r.Y;
                key[i++] = r.SizeW; key[i++] = r.SizeH;
            }
            return key;
        }

        // EXACT equality on purpose (no epsilon): these are copied field values, not computed ones, so a bit that
        // differs means the DM changed something. NaN compares unequal, which recomputes — the safe direction.
        static bool SameCapKey(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // The area cap for the current upper floor (ceiling when floor 0 has no column yet — nothing to size against).
        int UpperCap() => TryGetColumnAndCap(out _, out int cap) ? cap : MaxUpperRooms;

        /// <summary>«Перегенерировать» for a building UPPER floor: rebuild it around the shared column with the
        /// requested room count. Atomic — if the count can't fit floor 0's contour, NOTHING changes and the DM
        /// is told the reason; otherwise the floor is replaced and its stairs re-linked up/down the column.</summary>
        void RegenerateUpperFloor()
        {
            if (current == null || current.Kind != InteriorKind.Building || CurrentLevelIndex <= 0) return;
            // Cheap pre-check BEFORE the confirm (no re-probe — reuse the cap cached at floor-switch): bail if
            // floor 0 lost its column, and reject an over-capacity count without asking the DM to discard a floor
            // that isn't going to change. DoRegenerateUpperFloor re-derives the cap authoritatively.
            if (BuildingGenerator.FindFloorZeroColumn(current) == null) { ShowRegenMsg("Добавьте лестницу на 1-м этаже"); return; }
            if (upperRoomCount > currentUpperCap) { ClampToCapWithMessage(currentUpperCap); return; }

            // Regenerate REPLACES the whole floor, discarding any authored room content. Mirror the floor-removal
            // safeguard: confirm first when the floor has named/annotated rooms (there is no undo).
            var lvl = CurrentLevel;
            bool annotated = lvl != null && lvl.Rooms.Exists(r => !string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.Body));
            if (annotated)
                WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Перегенерировать этаж?",
                    "Комнаты этого этажа, их названия и заметки будут заменены.", ok => { if (ok) DoRegenerateUpperFloor(); });
            else
                DoRegenerateUpperFloor();
        }

        // Pin the stepper to the real limit and tell the DM. Called when the requested count exceeds the area cap.
        void ClampToCapWithMessage(int cap)
        {
            upperRoomCount = cap;
            if (upperCountLabel != null) upperCountLabel.text = upperRoomCount.ToString();
            ShowRegenMsg($"В контур помещается не более {cap} комнат");
        }

        void DoRegenerateUpperFloor()
        {
            if (current == null || current.Kind != InteriorKind.Building || CurrentLevelIndex <= 0) return;
            // Re-read the column non-mutatingly (the DM could have edited floor 0 between the confirm and here);
            // never designate one on this path — that would mutate floor 0 on a run that may still reject.
            if (!TryGetColumnAndCap(out var column, out int cap)) { ShowRegenMsg("Добавьте лестницу на 1-м этаже"); return; }
            if (upperRoomCount > cap) { ClampToCapWithMessage(cap); return; }

            // GUARANTEED for an in-range count (upperRoomCount ≤ cap, and cap is a real packing result): search
            // fresh seeds for a natural layout, then fall back to the probe seeds that reach the packable max —
            // so «Перегенерировать» always finds a valid arrangement instead of a "won't fit" dead end.
            int T = DungeonLayout.TilesPerAxis;
            bool ok = BuildingGenerator.TryBuildUpperFloorExact(upperRoomCount, FreshSeed(), RegenAttempts,
                column.X * T, column.Y * T, column.SizeW, column.SizeH, current.Floors[0], out var newFloor, out var newStair);
            if (!ok) { ShowRegenMsg($"Не удалось разместить {upperRoomCount} — уменьшите количество"); return; }

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
            // A building can lose any SELECTED floor except floor 0 (it defines the footprint and hosts the
            // shared stairwell column) — RewireStairChain re-rebuilds the vertical Stairs chain from the
            // surviving floors right after a removal (see RemoveCurrentLevel), so removing a middle floor no
            // longer severs the shaft the way it used to. Dungeons remove the current floor, so show it always.
            bool canRemove = current.Kind == InteriorKind.Building
                ? current.Floors.Count > 1 && CurrentLevelIndex > 0
                : current.Floors.Count > 1;
            if (canRemove) AddLevelTabButton("× " + profile.TermFloor, 64f, false, RequestRemoveCurrentLevel);
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
