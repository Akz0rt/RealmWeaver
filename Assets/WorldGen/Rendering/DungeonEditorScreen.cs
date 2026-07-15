using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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

        // Level render (Task 4): a square RawImage inside MapArea, baked from CurrentLevel.
        RawImage mapImage;
        Texture2D mapTex;
        const int PxPerTile = 10;

        // Wall/Floor brush (Task 4).
        DungeonTile brushTile = DungeonTile.Floor;
        int brushSize = 1;   // 1..3 (radius 0..2; clamp)
        readonly Image[] brushTileButtons = new Image[2];   // [0]=Wall,[1]=Floor — highlight tracking
        readonly Image[] brushSizeButtons = new Image[3];   // sizes 1..3 — highlight tracking

        // Numbered key + markers (Task 5).
        int selectedChamber = -1;                 // KeyChamber.Number, or -1 for none
        RectTransform keyContent;                 // scroll content that holds the key rows
        ScrollRect keyScroll;
        readonly List<KeyRowUI> keyRows = new List<KeyRowUI>();       // one per chamber (re-highlight without rebuild)
        readonly List<GameObject> markerGOs = new List<GameObject>(); // numbered markers overlaid on mapImage

        /// <summary>Per-key-row UI refs kept so SelectChamber can re-highlight and toggle the inline
        /// editor without rebuilding the whole list (a rebuild would drop an in-progress edit).</summary>
        class KeyRowUI { public int number; public Image headerBg; public GameObject editor; public Text titleLabel; }

        // Generation controls (Task 6). The Сгенерировать button + Камеры/Размер sliders write these;
        // they take effect on the NEXT Generate (no auto-regenerate on drag).
        int genChambers = 6;
        float genSize = 0.5f;

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
            RefreshMap();        // bakes CurrentLevel into mapTex
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

        /// <summary>Removes the current level, but if its key has any authored Title/Body, confirms first
        /// (that text is lost). Wired to the «×» level tab (shown only when there's more than one level).</summary>
        void RequestRemoveCurrentLevel()
        {
            var lvl = CurrentLevel;
            if (current == null || current.Levels.Count <= 1 || lvl == null) return;
            bool annotated = lvl.Chambers.Exists(c => !string.IsNullOrEmpty(c.Title) || !string.IsNullOrEmpty(c.Body));
            if (annotated)
                WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Удалить уровень?",
                    "Ключ этого уровня будет потерян.", ok => { if (ok) RemoveCurrentLevel(); });
            else
                RemoveCurrentLevel();
        }

        /// <summary>Set the active brush tile (Wall/Floor); updates the top-strip toggle highlight.</summary>
        public void SetBrush(DungeonTile t)
        {
            brushTile = t;
            UpdateBrushHighlights();
        }

        /// <summary>Set the brush radius (clamped 1..3); updates the top-strip size-button highlight.</summary>
        public void SetBrushSize(int s)
        {
            brushSize = Mathf.Clamp(s, 1, 3);
            UpdateBrushHighlights();
        }

        // RefreshMap() bakes CurrentLevel into mapTex and shows it on mapImage.
        void RefreshMap()
        {
            var lvl = CurrentLevel;
            if (mapImage == null || lvl == null || lvl.Tiles == null) return;
            if (mapTex != null) Destroy(mapTex);
            mapTex = DungeonLevelRenderer.Bake(lvl, PxPerTile);
            mapImage.texture = mapTex;
        }

        // ── Numbered key sidebar + marker overlay (Task 5) ───────────────────────

        /// <summary>Rebuilds the key list for CurrentLevel (one collapsible row per chamber) and the
        /// numbered marker overlay. Selection resets on every rebuild (level switch / regenerate).</summary>
        void RefreshKey()
        {
            selectedChamber = -1;
            keyRows.Clear();
            if (keyContent != null)
                for (int i = keyContent.childCount - 1; i >= 0; i--)
                    Destroy(keyContent.GetChild(i).gameObject);

            var lvl = CurrentLevel;
            if (keyContent != null && lvl != null)
                foreach (var c in lvl.Chambers)
                    BuildKeyRow(c);

            RefreshMarkers();
        }

        /// <summary>Selects a chamber by Number (or -1). Re-highlights every key row + marker and opens
        /// the selected row's inline Title/Body editor — a light update, no list rebuild.</summary>
        public void SelectChamber(int number)
        {
            selectedChamber = number;
            foreach (var r in keyRows)
            {
                bool sel = r.number == number;
                if (r.headerBg != null) ThemeService.Tag(r.headerBg, sel ? ThemeRole.AccentSoft : ThemeRole.Panel2);
                if (r.editor != null) r.editor.SetActive(sel);
            }
            RefreshMarkers();
        }

        void BuildKeyRow(KeyChamber c)
        {
            int num = c.Number;

            var rowGO = new GameObject($"KeyRow_{num}", typeof(RectTransform));
            rowGO.transform.SetParent(keyContent, false);
            var rowVlg = rowGO.AddComponent<VerticalLayoutGroup>();
            rowVlg.spacing = 2f;
            rowVlg.childControlWidth = true;
            rowVlg.childForceExpandWidth = true;
            rowVlg.childControlHeight = true;
            rowVlg.childForceExpandHeight = false;

            // Header: [number badge] [title] — click to select/expand.
            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rowGO.transform, false);
            var headerBg = headerGO.AddComponent<Image>();
            ThemeService.Tag(headerBg, ThemeRole.Panel2);
            headerGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerBg;
            headerBtn.onClick.AddListener(() => SelectChamber(num));
            var hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 0, 0);
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var badge = new GameObject("Badge");
            badge.transform.SetParent(headerGO.transform, false);
            var badgeImg = badge.AddComponent<Image>();
            ThemeService.Tag(badgeImg, ThemeRole.Accent);
            badgeImg.raycastTarget = false;
            badge.AddComponent<LayoutElement>().preferredWidth = 24f;
            var badgeTxt = MakeText(badge.transform, num.ToString(), 12, ThemeRole.AccentInk, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(badgeTxt.rectTransform);
            badgeTxt.raycastTarget = false;

            var titleLabel = MakeText(headerGO.transform, KeyTitleLabel(c.Title), 12,
                string.IsNullOrEmpty(c.Title) ? ThemeRole.Mut : ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleLeft);
            titleLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            titleLabel.raycastTarget = false;

            // Inline editor: Title + Body, hidden until the row is selected.
            var editorGO = new GameObject("Editor", typeof(RectTransform));
            editorGO.transform.SetParent(rowGO.transform, false);
            var evlg = editorGO.AddComponent<VerticalLayoutGroup>();
            evlg.spacing = 3f;
            evlg.padding = new RectOffset(6, 6, 2, 6);
            evlg.childControlWidth = true;
            evlg.childForceExpandWidth = true;
            evlg.childControlHeight = true;
            evlg.childForceExpandHeight = false;

            var titleField = BuildInputField(editorGO.transform, false, "Название комнаты");
            titleField.text = c.Title;
            titleField.onEndEdit.AddListener(v => { c.Title = v; UpdateKeyRowTitle(num, v); });

            var bodyField = BuildInputField(editorGO.transform, true, "Описание: что здесь, ловушки, добыча…");
            bodyField.text = c.Body;
            bodyField.onEndEdit.AddListener(v => c.Body = v);

            editorGO.SetActive(false);

            keyRows.Add(new KeyRowUI { number = num, headerBg = headerBg, editor = editorGO, titleLabel = titleLabel });
        }

        void UpdateKeyRowTitle(int number, string title)
        {
            foreach (var r in keyRows)
                if (r.number == number && r.titleLabel != null)
                {
                    r.titleLabel.text = KeyTitleLabel(title);
                    ThemeService.Tag(r.titleLabel, string.IsNullOrEmpty(title) ? ThemeRole.Mut : ThemeRole.Txt);
                }
        }

        static string KeyTitleLabel(string title) => string.IsNullOrEmpty(title) ? "(без названия)" : title;

        /// <summary>Rebuilds the numbered marker overlay on top of mapImage. Markers are anchored by
        /// NORMALIZED position (not rect reads) so they stay correct through window resizes and before
        /// first layout — sidestepping the rect==0-before-activation gotcha.</summary>
        void RefreshMarkers()
        {
            foreach (var go in markerGOs) if (go != null) Destroy(go);
            markerGOs.Clear();

            var lvl = CurrentLevel;
            if (lvl == null || mapImage == null) return;
            foreach (var c in lvl.Chambers)
                markerGOs.Add(BuildMarker(c, lvl));
        }

        GameObject BuildMarker(KeyChamber c, DungeonLevel lvl)
        {
            int num = c.Number;
            bool sel = num == selectedChamber;
            float nx = (c.MarkerCellX + 0.5f) / lvl.Width;
            float ny = 1f - (c.MarkerCellY + 0.5f) / lvl.Height;   // grid y0 = top → bottom-origin anchor

            var go = new GameObject($"Marker_{num}", typeof(RectTransform));
            go.transform.SetParent(mapImage.rectTransform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(nx, ny);
            rt.anchorMax = new Vector2(nx, ny);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = sel ? new Vector2(26f, 26f) : new Vector2(20f, 20f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, sel ? ThemeRole.Danger : ThemeRole.Accent);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SelectChamber(num));

            var lbl = MakeText(go.transform, num.ToString(), 11, ThemeRole.AccentInk, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
            return go;
        }

        // ── Key sidebar construction ─────────────────────────────────────────────

        void BuildKeySidebar(RectTransform parent)
        {
            var caption = MakeText(parent, "КЛЮЧ КОМНАТ", 10, ThemeRole.Mut, FontStyle.Bold, TextAnchor.UpperLeft);
            var cr = caption.rectTransform;
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.sizeDelta = new Vector2(0f, 16f);
            cr.anchoredPosition = new Vector2(10f, -8f);
            caption.raycastTarget = false;

            var scrollGO = new GameObject("KeyScroll", typeof(RectTransform));
            scrollGO.transform.SetParent(parent, false);
            var scRect = scrollGO.GetComponent<RectTransform>();
            scRect.anchorMin = Vector2.zero;
            scRect.anchorMax = Vector2.one;
            scRect.offsetMin = new Vector2(6f, 6f);
            scRect.offsetMax = new Vector2(-6f, -28f);   // leave room for the caption
            keyScroll = scrollGO.AddComponent<ScrollRect>();
            keyScroll.horizontal = false;
            keyScroll.vertical = true;
            keyScroll.scrollSensitivity = 30f;
            keyScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            Stretch(viewportGO.GetComponent<RectTransform>());
            keyScroll.viewport = viewportGO.GetComponent<RectTransform>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keyContent = contentGO.GetComponent<RectTransform>();
            keyContent.anchorMin = new Vector2(0f, 1f);
            keyContent.anchorMax = new Vector2(1f, 1f);
            keyContent.pivot = new Vector2(0.5f, 1f);
            keyContent.sizeDelta = Vector2.zero;
            keyContent.anchoredPosition = Vector2.zero;
            keyScroll.content = keyContent;
        }

        /// <summary>Self-contained InputField builder (mirrors PoiEditorScreen's, with a placeholder arg).</summary>
        InputField BuildInputField(Transform parent, bool multiline, string placeholder)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

            var text = MakeText(go.transform, "", 12, ThemeRole.Txt, FontStyle.Normal, TextAnchor.UpperLeft);
            text.supportRichText = false;
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0.03f, 0f);
            textRect.anchorMax = new Vector2(0.98f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var ph = MakeText(go.transform, placeholder, 12, ThemeRole.Mut, FontStyle.Italic, TextAnchor.UpperLeft);
            var phRect = ph.rectTransform;
            phRect.anchorMin = new Vector2(0.03f, 0f);
            phRect.anchorMax = new Vector2(0.98f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = ph;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 54f : 22f;
            le.flexibleWidth = 1f;
            return field;
        }

        /// <summary>Pointer down/drag handler wired via EventTrigger on mapImage. Converts the screen
        /// point to a normalized position within the RawImage rect, resolves the grid cell, and paints
        /// brushTile in the brush radius (skipping the outer border frame), then rebakes.</summary>
        void PaintAt(Vector2 screenPos)
        {
            var lvl = CurrentLevel; if (lvl == null) return;
            var rt = mapImage.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out var local)) return;
            var rect = rt.rect;
            float nx = (local.x - rect.xMin) / rect.width;
            float ny = (local.y - rect.yMin) / rect.height;
            if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return;
            if (!DungeonLevelRenderer.NormalizedToCell(lvl, nx, ny, out int gx, out int gy)) return;
            int r = brushSize - 1;
            for (int y = gy - r; y <= gy + r; y++)
                for (int x = gx - r; x <= gx + r; x++)
                    if (lvl.InBounds(x, y) && !(x == 0 || y == 0 || x == lvl.Width - 1 || y == lvl.Height - 1))
                        lvl.Set(x, y, brushTile);
            RefreshMap();   // rebake (48x48x10px is cheap; no dirty-rect needed in v1)
        }

        void OnDestroy()
        {
            if (mapTex != null) { Destroy(mapTex); mapTex = null; }
        }

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

            BuildGenControls(row2.transform);
            BuildBrushTileSelector(row2.transform);
            BuildBrushSizeSelector(row2.transform);
        }

        // ── Brush controls (Task 4) ─────────────────────────────────────────────

        void BuildBrushTileSelector(Transform parent)
        {
            var go = new GameObject("BrushTile", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 120f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            brushTileButtons[0] = AddBrushButton(go.transform, "Стена", () => SetBrush(DungeonTile.Wall));
            brushTileButtons[1] = AddBrushButton(go.transform, "Пол", () => SetBrush(DungeonTile.Floor));
            UpdateBrushHighlights();
        }

        void BuildBrushSizeSelector(Transform parent)
        {
            var go = new GameObject("BrushSize", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 120f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            for (int i = 0; i < brushSizeButtons.Length; i++)
            {
                int size = i + 1; // capture for the closure
                brushSizeButtons[i] = AddBrushButton(go.transform, size.ToString(), () => SetBrushSize(size));
            }
            UpdateBrushHighlights();
        }

        Image AddBrushButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Brush_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
            return img;
        }

        /// <summary>Re-tags the Wall/Floor and size button backgrounds so the active brushTile/brushSize
        /// reads as highlighted (AccentSoft) and the rest as plain (Elev). Safe to call before the
        /// buttons exist (skips — BuildBrushTileSelector/BuildBrushSizeSelector call it again once built).</summary>
        void UpdateBrushHighlights()
        {
            if (brushTileButtons[0] != null)
                ThemeService.Tag(brushTileButtons[0], brushTile == DungeonTile.Wall ? ThemeRole.AccentSoft : ThemeRole.Elev);
            if (brushTileButtons[1] != null)
                ThemeService.Tag(brushTileButtons[1], brushTile == DungeonTile.Floor ? ThemeRole.AccentSoft : ThemeRole.Elev);
            for (int i = 0; i < brushSizeButtons.Length; i++)
                if (brushSizeButtons[i] != null)
                    ThemeService.Tag(brushSizeButtons[i], brushSize == i + 1 ? ThemeRole.AccentSoft : ThemeRole.Elev);
        }

        // ── Generation controls (Task 6) ─────────────────────────────────────────

        /// <summary>Top-strip generation cluster: Камеры (int 4..12) + Размер (0..1) sliders and the
        /// Сгенерировать button. A flat HorizontalLayoutGroup with explicit child sizeDelta widths
        /// (childControl*=false), matching PoiEditorScreen.AddScaleSliderRow's proven slider recipe.</summary>
        void BuildGenControls(Transform parent)
        {
            var go = new GameObject("GenControls", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 412f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            AddStripText(go.transform, "Камеры", 48f);
            BuildSliderTrack(go.transform, 4f, 12f, true, genChambers, 64f, 22f, v => genChambers = Mathf.RoundToInt(v));
            AddStripText(go.transform, "Размер", 48f);
            BuildSliderTrack(go.transform, 0f, 1f, false, genSize, 64f, 22f, v => genSize = v);
            AddStripButton(go.transform, "Сгенерировать", 108f, ThemeRole.Accent, OnGenerateClicked);
        }

        void AddStripText(Transform parent, string text, float width)
        {
            var lbl = MakeText(parent, text, 11, ThemeRole.Mut, FontStyle.Normal, TextAnchor.MiddleLeft);
            lbl.rectTransform.sizeDelta = new Vector2(width, 20f);
            lbl.raycastTarget = false;
        }

        void AddStripButton(Transform parent, string label, float width, ThemeRole bgRole, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, bgRole);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 26f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 12,
                bgRole == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
        }

        /// <summary>Builds a compact [track][value] slider pair as two consecutive flat-row children.
        /// Fill/handle wiring mirrors PoiEditorScreen.AddScaleSliderRow (the Slider drives their anchors).</summary>
        void BuildSliderTrack(Transform parent, float min, float max, bool whole, float value,
                              float trackWidth, float valueWidth, System.Action<float> onChanged)
        {
            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(parent, false);
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(trackWidth, 18f);

            var bg = new GameObject("Bg");
            bg.transform.SetParent(sliderGO.transform, false);
            var bgImg = bg.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Panel2);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.35f);
            bgRect.anchorMax = new Vector2(1f, 0.65f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent, 0.9f);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.35f);
            fillRect.anchorMax = new Vector2(0f, 0.65f);
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderGO.transform, false);
            Stretch(handleArea.AddComponent<RectTransform>());

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImg = handle.AddComponent<Image>();
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(8f, 0f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            var valTxt = MakeText(parent, FormatSliderValue(value, whole), 11, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleRight);
            valTxt.rectTransform.sizeDelta = new Vector2(valueWidth, 20f);
            valTxt.raycastTarget = false;

            slider.onValueChanged.AddListener(v =>
            {
                valTxt.text = FormatSliderValue(v, whole);
                onChanged(v);
            });
        }

        static string FormatSliderValue(float v, bool whole) => whole ? Mathf.RoundToInt(v).ToString() : v.ToString("F1");

        /// <summary>Regenerates ONLY the current level (tiles + chambers) from the Камеры/Размер values;
        /// other levels are untouched. Selection/key rebuild via RefreshKey.</summary>
        void OnGenerateClicked()
        {
            if (current == null) return;
            current.Levels[CurrentLevelIndex] = CaveGenerator.Generate(FreshSeed(), 48, 48, Mathf.Clamp(genChambers, 4, 12), genSize);
            RefreshMap();
            RefreshKey();
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

            // The level render — a centered SQUARE inside MapArea (a 48x48 grid must never be
            // distorted, regardless of MapArea's own aspect). AspectRatioFitter.FitInParent keeps it
            // square and centered while MapArea itself just stretches to fill the body.
            var mapImgGO = new GameObject("MapImage", typeof(RectTransform));
            mapImgGO.transform.SetParent(mapGO.transform, false);
            mapImage = mapImgGO.AddComponent<RawImage>();
            mapImage.raycastTarget = true;   // needed so the EventTrigger below receives pointer events
            Stretch(mapImage.rectTransform);
            var fitter = mapImgGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;

            // Wall/Floor brush painting: PointerDown + Drag both paint at the current pointer position.
            var trigger = mapImgGO.AddComponent<EventTrigger>();
            AddEventTriggerEntry(trigger, EventTriggerType.PointerDown, PaintAt);
            AddEventTriggerEntry(trigger, EventTriggerType.Drag, PaintAt);

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

            BuildKeySidebar(KeySidebar);
        }

        /// <summary>Registers an EventTrigger entry whose callback forwards the pointer's screen
        /// position to `handler` (used to wire PointerDown/Drag on mapImage to PaintAt).</summary>
        static void AddEventTriggerEntry(EventTrigger trigger, EventTriggerType type, System.Action<Vector2> handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(((PointerEventData)data).position));
            trigger.triggers.Add(entry);
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
            if (current.Levels.Count > 1)
                AddLevelTabButton("×", 28f, false, RequestRemoveCurrentLevel);
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
