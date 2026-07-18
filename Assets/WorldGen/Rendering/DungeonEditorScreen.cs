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

        Font font;
        bool built;

        const float StripHeight = 44f;
        const float ToolbarHeight = 36f;

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
            if (current.Floors.Count == 0) current.Floors.Add(DungeonGraphGenerator.Generate(FreshSeed(), 6));
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
            RefreshBody();
            RevalidateAndRefresh();
        }

        public void AddLevel()
        {
            if (current == null) return;
            current.Floors.Add(DungeonGraphGenerator.Generate(FreshSeed(), 6));
            SetLevel(current.Floors.Count - 1);
        }

        public void RemoveCurrentLevel()
        {
            if (current == null || current.Floors.Count <= 1) return;
            DungeonOps.RemoveLevel(current, CurrentLevelIndex);
            SetLevel(Mathf.Min(CurrentLevelIndex, current.Floors.Count - 1));
        }

        /// <summary>«× Этаж» handler: if the level has authored room content, confirm before discarding
        /// it (deleting a floor loses all its rooms/corridors/notes — irreversible once the project is
        /// saved); otherwise remove directly. ConfirmDialog.Show's «Удалить» is the correct label here.</summary>
        void RequestRemoveCurrentLevel()
        {
            var lvl = CurrentLevel;
            if (current == null || current.Floors.Count <= 1 || lvl == null) return;
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

            // BuildToolbar runs at EnsureBuilt() time, which Awake() can trigger BEFORE Bind() ever sets
            // `current` (the scene's DungeonEditorScreen GameObject starts active, so Awake fires at scene
            // load — see DungeonFlatRenderer's identical dungeon!=null guard for the same reason). Fall back
            // to the dungeon profile so this never null-refs; for a dungeon it's the exact same profile
            // Profiles.ForRoom(current) would give once bound.
            var profile = current != null ? Profiles.ForRoom(current) : Profiles.For(InteriorKind.Dungeon);
            AddToolbarButton(bar.transform, "+ " + profile.TermRoom, 110f, ThemeRole.Elev, () => viewController?.AddRoomAtCenter());
            linkToggleImg = AddToolbarButton(bar.transform, "Связать", 90f, ThemeRole.Elev, ToggleLinkMode);
            AddToolbarButton(bar.transform, "Удалить", 90f, ThemeRole.Elev, () => viewController?.DeleteSelected());
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
