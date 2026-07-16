using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Full-screen dungeon editor (mutually-exclusive AppScreen.Dungeon, opened from the POI
    /// editor). Hosts the draggable room-graph canvas (DungeonGraphView, Task 4) in MapArea, with a
    /// toolbar (+ Комната / Связать / Удалить) below the top strip; the inspector (Task 5) is added
    /// to Sidebar. Built imperatively at Awake, own-canvas pattern (mirrors PoiEditorScreen).</summary>
    public class DungeonEditorScreen : MonoBehaviour
    {
        public System.Action OnCloseRequested;      // wired to MapScreenController.CloseDungeonEditor

        DungeonData current;
        public int CurrentLevelIndex { get; private set; }
        public DungeonLevel CurrentLevel =>
            current != null && CurrentLevelIndex >= 0 && CurrentLevelIndex < current.Levels.Count
                ? current.Levels[CurrentLevelIndex] : null;

        public RectTransform MapArea { get; private set; }     // graph canvas host (Task 4)
        public RectTransform Sidebar { get; private set; }     // inspector host (Task 5)
        Transform levelTabsRow;
        Text titleLabel;

        DungeonGraphView graphView;
        Image linkToggleImg;
        int selectedRoomId;   // stored selection seam — Task 5's inspector will consume this

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
        public void Bind(DungeonData dungeon)
        {
            EnsureBuilt();
            current = dungeon;
            if (current.Levels.Count == 0) current.Levels.Add(DungeonGraphGenerator.Generate(FreshSeed(), 6));
            SetLevel(0);
            RebuildLevelTabs();
        }

        public void SetLevel(int index)
        {
            if (current == null || current.Levels.Count == 0) return;
            CurrentLevelIndex = Mathf.Clamp(index, 0, current.Levels.Count - 1);
            RebuildLevelTabs();
            RefreshBody();
        }

        public void AddLevel()
        {
            if (current == null) return;
            current.Levels.Add(DungeonGraphGenerator.Generate(FreshSeed(), 6));
            SetLevel(current.Levels.Count - 1);
        }

        public void RemoveCurrentLevel()
        {
            if (current == null || current.Levels.Count <= 1) return;
            current.Levels.RemoveAt(CurrentLevelIndex);
            SetLevel(Mathf.Min(CurrentLevelIndex, current.Levels.Count - 1));
        }

        // Body refresh — (re)binds the graph canvas to the current level. Also the target of
        // DungeonGraphView.OnGraphMutated, so every add/delete/link round-trips through here; Task 5
        // may later swap this for a lighter re-validate instead of a full rebind.
        void RefreshBody()
        {
            if (graphView == null || current == null) return;
            graphView.Bind(current, CurrentLevelIndex, font);
        }

        int FreshSeed() => Random.Range(int.MinValue, int.MaxValue);

        void BuildUI()
        {
            var canvasGO = new GameObject("DungeonEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
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
        /// «Связать» toggles DungeonGraphView.LinkMode and highlights (AccentSoft) while active.</summary>
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

            AddToolbarButton(bar.transform, "+ Комната", 110f, ThemeRole.Elev, () => graphView?.AddRoomAtCenter());
            linkToggleImg = AddToolbarButton(bar.transform, "Связать", 90f, ThemeRole.Elev, ToggleLinkMode);
            AddToolbarButton(bar.transform, "Удалить", 90f, ThemeRole.Elev, () => graphView?.DeleteSelected());
        }

        void ToggleLinkMode()
        {
            if (graphView == null) return;
            graphView.SetLinkMode(!graphView.LinkMode);
            if (linkToggleImg != null) ThemeService.Tag(linkToggleImg, graphView.LinkMode ? ThemeRole.AccentSoft : ThemeRole.Elev);
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

            var graphGO = new GameObject("GraphView", typeof(RectTransform));
            graphGO.transform.SetParent(mapGO.transform, false);
            Stretch(graphGO.GetComponent<RectTransform>());
            graphView = graphGO.AddComponent<DungeonGraphView>();
            graphView.OnRoomSelected = id => selectedRoomId = id;   // Task 5 will consume this seam
            graphView.OnGraphMutated = () => RefreshBody();
            graphView.OnJumpToLevel = SetLevel;

            var sidebarGO = new GameObject("Sidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(body.transform, false);
            Sidebar = sidebarGO.GetComponent<RectTransform>();
            Sidebar.anchorMin = new Vector2(1f, 0f); Sidebar.anchorMax = new Vector2(1f, 1f);
            Sidebar.offsetMin = new Vector2(-(sidebarWidth + 12f), 12f); Sidebar.offsetMax = new Vector2(-12f, -12f);
            var sidebarBg = sidebarGO.AddComponent<Image>();
            ThemeService.Tag(sidebarBg, ThemeRole.Elev); sidebarBg.raycastTarget = false;
        }

        void RebuildLevelTabs()
        {
            if (levelTabsRow == null || current == null) return;
            for (int i = levelTabsRow.childCount - 1; i >= 0; i--) Destroy(levelTabsRow.GetChild(i).gameObject);
            for (int i = 0; i < current.Levels.Count; i++)
            {
                int idx = i;
                AddLevelTabButton($"Ур.{i + 1}", 50f, idx == CurrentLevelIndex, () => SetLevel(idx));
            }
            AddLevelTabButton("+", 28f, false, AddLevel);
            if (current.Levels.Count > 1) AddLevelTabButton("×", 28f, false, RemoveCurrentLevel);
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
