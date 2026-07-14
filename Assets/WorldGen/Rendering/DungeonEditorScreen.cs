using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Full-screen cave-dungeon editor (a mutually-exclusive screen toggled by MapScreenController,
    /// opened from PoiEditorScreen's «КАРТА ЛОКАЦИИ» row). Shell only in this task: a top strip
    /// (back button, dungeon title, level tabs Ур.1|2|+, and placeholder slots for the
    /// generation/brush controls added in Tasks 4/6), a MapArea container (filled in Task 4) and a
    /// KeySidebar container (filled in Task 5).
    ///
    /// Built imperatively at Awake, mirroring PoiEditorScreen's own-canvas pattern. Self-contained —
    /// does not reuse PoiEditorScreen's private UI helpers.
    /// </summary>
    public class DungeonEditorScreen : MonoBehaviour
    {
        public System.Action OnCloseRequested;      // wired to MapScreenController.CloseDungeonEditor

        DungeonData current;
        public int CurrentLevelIndex { get; private set; }
        public DungeonLevel CurrentLevel =>
            current != null && CurrentLevelIndex >= 0 && CurrentLevelIndex < current.Levels.Count
                ? current.Levels[CurrentLevelIndex] : null;

        // Containers other tasks fill:
        public RectTransform MapArea { get; private set; }
        public RectTransform KeySidebar { get; private set; }
        Transform levelTabsRow;

        Font font;
        bool built;

        void Awake()
        {
            if (isActiveAndEnabled) EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard (see NotesRootBuilder)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();                               // builds canvas, top strip, MapArea, KeySidebar
            built = true;
        }

        /// <summary>Bind a dungeon; ensure it has at least one level; show level 0. Safe before first activation.</summary>
        public void Bind(DungeonData dungeon)
        {
            EnsureBuilt();
            current = dungeon;
            if (current.Levels.Count == 0)
                current.Levels.Add(CaveGenerator.Generate(FreshSeed(), 48, 48, 6, 0.5f));
            SetLevel(0);
            RebuildLevelTabs();
        }

        public void SetLevel(int index)
        {
            if (current == null || current.Levels.Count == 0) return;
            CurrentLevelIndex = Mathf.Clamp(index, 0, current.Levels.Count - 1);
            RebuildLevelTabs();
            RefreshMap();        // no-op until Task 4
            RefreshKey();        // no-op until Task 5
        }

        public void AddLevel()
        {
            if (current == null) return;
            current.Levels.Add(CaveGenerator.Generate(FreshSeed(), 48, 48, 6, 0.5f));
            SetLevel(current.Levels.Count - 1);
        }

        public void RemoveCurrentLevel()
        {
            if (current == null || current.Levels.Count <= 1) return;   // keep at least one level
            current.Levels.RemoveAt(CurrentLevelIndex);
            SetLevel(Mathf.Min(CurrentLevelIndex, current.Levels.Count - 1));
        }

        // RefreshMap()/RefreshKey() are defined here as empty seams and IMPLEMENTED in Tasks 4/5.
        void RefreshMap() { /* Task 4 */ }
        void RefreshKey() { /* Task 5 */ }

        int FreshSeed() => Random.Range(int.MinValue, int.MaxValue);

        // ── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("DungeonEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // topmost, same as PoiEditorScreen — fully covers the map/POI editor beneath
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var root = new GameObject("Root");
            root.transform.SetParent(canvasGO.transform, false);
            var rootImg = root.AddComponent<Image>();
            ThemeService.Tag(rootImg, ThemeRole.Bg);
            Stretch(root.GetComponent<RectTransform>());

            BuildTopStrip(root.transform);
            BuildBody(root.transform);
        }

        void BuildTopStrip(Transform parent)
        {
            const float stripHeight = 78f;

            var strip = new GameObject("TopStrip", typeof(RectTransform));
            strip.transform.SetParent(parent, false);
            var sr = strip.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 1f);
            sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.sizeDelta = new Vector2(0f, stripHeight);
            sr.anchoredPosition = Vector2.zero;
            var stripBg = strip.AddComponent<Image>();
            ThemeService.Tag(stripBg, ThemeRole.Panel2);

            // Row 1: back button, dungeon title, level tabs (Ур.1|2|+).
            var row1 = new GameObject("Row1", typeof(RectTransform));
            row1.transform.SetParent(strip.transform, false);
            var r1 = row1.GetComponent<RectTransform>();
            r1.anchorMin = new Vector2(0f, 1f);
            r1.anchorMax = new Vector2(1f, 1f);
            r1.pivot = new Vector2(0.5f, 1f);
            r1.sizeDelta = new Vector2(0f, 40f);
            r1.anchoredPosition = Vector2.zero;

            var backGO = new GameObject("Back");
            backGO.transform.SetParent(row1.transform, false);
            var backImg = backGO.AddComponent<Image>();
            ThemeService.Tag(backImg, ThemeRole.Elev);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.onClick.AddListener(() => OnCloseRequested?.Invoke());
            var backRect = backGO.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.sizeDelta = new Vector2(110f, 28f);
            backRect.anchoredPosition = new Vector2(12f, 0f);
            var backLbl = MakeText(backGO.transform, "← Назад", 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(backLbl.rectTransform);
            backLbl.raycastTarget = false;

            var title = MakeText(row1.transform, "Подземелье", 14, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleLeft);
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 0.5f);
            tr.anchorMax = new Vector2(0f, 0.5f);
            tr.pivot = new Vector2(0f, 0.5f);
            tr.anchoredPosition = new Vector2(134f, 0f);
            tr.sizeDelta = new Vector2(200f, 28f);

            var tabsGO = new GameObject("LevelTabs", typeof(RectTransform));
            tabsGO.transform.SetParent(row1.transform, false);
            var hlg = tabsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var tabsRect = tabsGO.GetComponent<RectTransform>();
            tabsRect.anchorMin = new Vector2(0f, 0.5f);
            tabsRect.anchorMax = new Vector2(0f, 0.5f);
            tabsRect.pivot = new Vector2(0f, 0.5f);
            tabsRect.anchoredPosition = new Vector2(344f, 0f);
            tabsRect.sizeDelta = new Vector2(300f, 28f);
            levelTabsRow = tabsGO.transform;

            // Row 2: PLACEHOLDER slots for Generate / brush / sliders — filled in Tasks 4/6.
            var row2 = new GameObject("Row2", typeof(RectTransform));
            row2.transform.SetParent(strip.transform, false);
            var r2 = row2.GetComponent<RectTransform>();
            r2.anchorMin = new Vector2(0f, 0f);
            r2.anchorMax = new Vector2(1f, 0f);
            r2.pivot = new Vector2(0.5f, 0f);
            r2.sizeDelta = new Vector2(0f, 32f);
            r2.anchoredPosition = new Vector2(0f, 4f);
            var hlg2 = row2.AddComponent<HorizontalLayoutGroup>();
            hlg2.spacing = 8f;
            hlg2.padding = new RectOffset(12, 12, 0, 0);
            hlg2.childControlWidth = true;
            hlg2.childForceExpandWidth = false;
            hlg2.childControlHeight = true;
            hlg2.childForceExpandHeight = true;

            AddPlaceholderSlot(row2.transform, "Генерация…", 160f);
            AddPlaceholderSlot(row2.transform, "Кисть: —", 120f);
            AddPlaceholderSlot(row2.transform, "Размер: —", 120f);
        }

        void AddPlaceholderSlot(Transform parent, string label, float width)
        {
            var go = new GameObject("Placeholder");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.6f);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var lbl = MakeText(go.transform, label, 11, ThemeRole.Mut, FontStyle.Italic, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
        }

        void BuildBody(Transform parent)
        {
            const float stripHeight = 78f;
            const float sidebarWidth = 280f;

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(parent, false);
            var br = body.GetComponent<RectTransform>();
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = Vector2.zero;
            br.offsetMax = new Vector2(0f, -stripHeight); // below the top strip

            var mapGO = new GameObject("MapArea", typeof(RectTransform));
            mapGO.transform.SetParent(body.transform, false);
            MapArea = mapGO.GetComponent<RectTransform>();
            MapArea.anchorMin = new Vector2(0f, 0f);
            MapArea.anchorMax = new Vector2(1f, 1f);
            MapArea.offsetMin = new Vector2(12f, 12f);
            MapArea.offsetMax = new Vector2(-(sidebarWidth + 18f), -12f);
            var mapBg = mapGO.AddComponent<Image>();
            ThemeService.Tag(mapBg, ThemeRole.Panel2);
            mapBg.raycastTarget = false;

            var sidebarGO = new GameObject("KeySidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(body.transform, false);
            KeySidebar = sidebarGO.GetComponent<RectTransform>();
            KeySidebar.anchorMin = new Vector2(1f, 0f);
            KeySidebar.anchorMax = new Vector2(1f, 1f);
            KeySidebar.offsetMin = new Vector2(-(sidebarWidth + 12f), 12f);
            KeySidebar.offsetMax = new Vector2(-12f, -12f);
            var sidebarBg = sidebarGO.AddComponent<Image>();
            ThemeService.Tag(sidebarBg, ThemeRole.Elev);
            sidebarBg.raycastTarget = false;
        }

        /// <summary>Rebuilds the Ур.1|2|+ tabs row from `current.Levels`, highlighting CurrentLevelIndex.
        /// Called from Bind/SetLevel — cheap (few small buttons), so a full rebuild each time keeps the
        /// highlight logic trivial (no cached button list to keep in sync).</summary>
        void RebuildLevelTabs()
        {
            if (levelTabsRow == null || current == null) return;
            for (int i = levelTabsRow.childCount - 1; i >= 0; i--)
                Destroy(levelTabsRow.GetChild(i).gameObject);

            for (int i = 0; i < current.Levels.Count; i++)
            {
                int idx = i; // capture for the closure
                AddLevelTabButton($"Ур.{i + 1}", 50f, idx == CurrentLevelIndex, () => SetLevel(idx));
            }
            AddLevelTabButton("+", 28f, false, AddLevel);
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
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
        }

        // ── Small builder primitives (self-contained — not shared with PoiEditorScreen) ──────────

        Text MakeText(Transform parent, string content, int size, ThemeRole role, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            ThemeService.Tag(text, role);
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
