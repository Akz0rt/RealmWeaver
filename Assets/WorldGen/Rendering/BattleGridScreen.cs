using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Full-screen battle-grid editor (AppScreen.BattleGrid, opened from the room inspector on
    /// the dungeon/interior screen it sits above). Hosts BattleGridRenderer + BattleGridViewController in
    /// MapArea, a paint toolbar (material / tool / brush size / grid size / regenerate) in Sidebar. Built
    /// imperatively at Awake, own-canvas pattern (mirrors DungeonEditorScreen).
    ///
    /// Two dialogs (resize-shrink, regenerate) borrow ConfirmDialog, which is a top-level canvas of its
    /// own (not a child of this screen) whose backdrop blocks MOUSE input to everything beneath it but
    /// cannot block a raw keyboard poll. So while either is open this screen disables `view`
    /// (BattleGridViewController) — that both silences its own Update()'s Ctrl+Z (which would otherwise
    /// mutate the buffer a pending resize/regenerate confirm is about to replace or measure) and, via
    /// Unity's event system skipping disabled handlers, stops pointer painting from reaching a hidden
    /// grid. Escape here is guarded the same way (`view.enabled` doubles as "no dialog is pending"), so
    /// pressing it while a confirm is up cannot close the screen out from under the modal.</summary>
    public class BattleGridScreen : MonoBehaviour
    {
        public System.Action OnCloseRequested;      // wired to MapScreenController.CloseBattleGrid

        InteriorData interior;
        int floorIndex;
        int roomId;

        BattleGridViewController view;
        BattleGridRenderer gridRenderer;   // named to avoid shadowing Component.renderer (CS0108)
        Text titleLabel;
        Text widthLabel;
        Text heightLabel;

        readonly List<(GridCell cell, Image img, Text lbl)> materialButtons = new List<(GridCell, Image, Text)>();
        readonly List<(GridTool tool, Image img, Text lbl)> toolButtons = new List<(GridTool, Image, Text)>();
        readonly List<(int size, Image img, Text lbl)> brushSizeButtons = new List<(int, Image, Text)>();
        VerticalLayoutGroup brushSizeSection;

        Font font;
        bool built;

        const float StripHeight = 44f;

        static readonly (GridCell cell, string label)[] MaterialOptions =
        {
            (GridCell.Empty,  "Пусто"),
            (GridCell.Floor,  "Пол"),
            (GridCell.Wall,   "Стена"),
            (GridCell.Door,   "Дверь"),
            (GridCell.Rough,  "Труднопроходимо"),
            (GridCell.Liquid, "Вода"),
            (GridCell.Chasm,  "Пропасть"),
        };

        static readonly (GridTool tool, string label)[] ToolOptions =
        {
            (GridTool.Brush, "Кисть"),
            (GridTool.Rect,  "Прямоугольник"),
            (GridTool.Fill,  "Заливка"),
        };

        void Awake() { if (isActiveAndEnabled) EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            built = true;
        }

        /// <summary>Open the battle map of one room. Decodes Room.Grid if present; otherwise generates a
        /// fresh grid in memory WITHOUT writing it to the model — an untouched map is deterministic, so
        /// storing it would bloat every save for no information.
        ///
        /// EnsureBuilt() runs FIRST: MapScreenController.OpenBattleGrid calls this screen's Bind BEFORE
        /// RefreshScreenState() activates the screen's GameObject, so Awake (which would otherwise build
        /// `view`/`renderer`) has not run yet on first open — without this call `view` is still null and
        /// the view.Bind(...) three lines down would NullReferenceException.</summary>
        public void Bind(InteriorData interior, int floorIndex, int roomId)
        {
            EnsureBuilt();
            this.interior = interior;
            this.floorIndex = floorIndex;
            this.roomId = roomId;

            var floor = interior.Floors[floorIndex];
            var room = floor.GetRoom(roomId);

            var buf = GridBuffer.FromModel(room.Grid);
            if (buf == null)
            {
                if (room.Grid != null)
                    Debug.LogWarning($"Боевая карта комнаты {roomId} повреждена и создана заново.");
                buf = BattleGridGenerator.Generate(room);
            }

            view.Bind(gridRenderer, buf);
            // view.Bind() resets the undo stack and the buffer but does NOT touch the renderer's own
            // highlight list (that belongs to the renderer, not the controller) — without this, a hover
            // highlight left over from the PREVIOUS room's grid would flash on this one's texture (right
            // dimensions or not) until the DM's next pointer move recomputes it.
            gridRenderer.SetHighlight(null);
            RefreshDoors();
            RefreshTitle();
            RefreshSizeLabels();
        }

        void RefreshDoors()
        {
            var floor = interior.Floors[floorIndex];
            var room = floor.GetRoom(roomId);
            gridRenderer.SetDoors(BattleGridGenerator.ProjectDoors(floor, room, view.Buffer));
            gridRenderer.Repaint();
        }

        // Any edit materialises the grid on the room — this is the single site where an in-memory map
        // becomes saved content, and therefore where it starts counting as authored.
        void Persist()
        {
            var room = interior.Floors[floorIndex].GetRoom(roomId);
            room.Grid = view.Buffer.ToModel();
        }

        void RefreshTitle()
        {
            if (titleLabel == null) return;
            var floor = interior.Floors[floorIndex];
            var room = floor.GetRoom(roomId);
            string name = string.IsNullOrEmpty(room.Title) ? $"Комната {roomId}" : room.Title;
            titleLabel.text = $"Боевая карта — {name}";
        }

        // Width/Height stepper readouts. Not covered by view.OnChanged's Persist/RefreshDoors pair (those
        // are model-level); wired in separately (Bind, and view.OnChanged below) since this is purely this
        // screen's own toolbar chrome reflecting view.Buffer's CURRENT dimensions.
        void RefreshSizeLabels()
        {
            if (view == null || view.Buffer == null) return;
            if (widthLabel != null) widthLabel.text = view.Buffer.Width.ToString();
            if (heightLabel != null) heightLabel.text = view.Buffer.Height.ToString();
        }

        void RequestResize(int newWidth, int newHeight)
        {
            newWidth = BattleGridCodec.Clamp(newWidth);
            newHeight = BattleGridCodec.Clamp(newHeight);
            int lost = BattleGridOps.CountLostOnResize(view.Buffer, newWidth, newHeight);
            if (lost > 0)
            {
                BeginModal();
                WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Уменьшить сетку?",
                    $"Будет потеряно закрашенных клеток: {lost}.",
                    confirmed =>
                    {
                        EndModal();
                        if (confirmed) view.ReplaceBuffer(BattleGridOps.Resize(view.Buffer, newWidth, newHeight));
                    });
                return;
            }
            view.ReplaceBuffer(BattleGridOps.Resize(view.Buffer, newWidth, newHeight));
        }

        void RequestRegenerate()
        {
            var room = interior.Floors[floorIndex].GetRoom(roomId);
            // Confirm only when the map has actually been edited: room.Grid is null until the first edit,
            // exactly like Link.Authored gates the floor-regeneration confirm.
            if (room.Grid == null) { Regenerate(room); return; }
            BeginModal();
            WorldGen.Notes.Rendering.ConfirmDialog.Show(font, "Пересобрать боевую карту?",
                "Карта будет создана заново. Всё нарисованное пропадёт.",
                confirmed => { EndModal(); if (confirmed) Regenerate(room); });
        }

        void Regenerate(Room room)
        {
            view.ReplaceBuffer(BattleGridGenerator.Generate(room));
            RefreshDoors();
        }

        // Disables `view` for the duration of a ConfirmDialog raised BY THIS SCREEN — see the class doc.
        // ConfirmDialog's own backdrop already swallows mouse clicks to whatever is beneath it, but a raw
        // Input.GetKeyDown poll (view's own Ctrl+Z, and this screen's Escape below) is not routed through
        // any raycast and so is NOT blocked by the backdrop on its own.
        void BeginModal() { if (view != null) view.enabled = false; }
        void EndModal() { if (view != null) view.enabled = true; }

        void Update()
        {
            // view.enabled doubles as "no confirm dialog raised by this screen is currently open" (see
            // BeginModal/EndModal) — without this guard, Escape would close the whole screen out from
            // under an open «Уменьшить сетку?» / «Пересобрать боевую карту?» dialog, stranding it floating
            // over whatever screen we fall back to.
            if (Input.GetKeyDown(KeyCode.Escape) && view != null && view.enabled) OnCloseRequested?.Invoke();
        }

        // ── UI construction (mirrors DungeonEditorScreen's composition recipe) ──────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("BattleGridCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 102 — one above DungeonEditorScreen's 101: this screen is opened FROM the dungeon/interior
            // screen and must occlude it, per MapScreenController.DesiredScreen's truth table. Below
            // ConfirmDialog (32000), which still renders on top of this screen's own dialogs.
            canvas.sortingOrder = 102;
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

            // Placeholder text only — RefreshTitle replaces it once Bind gives us a room to name.
            titleLabel = MakeText(strip.transform, "", 14, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleLeft);
            var tr = titleLabel.rectTransform;
            tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(0f, 0.5f);
            tr.pivot = new Vector2(0f, 0.5f); tr.anchoredPosition = new Vector2(134f, 0f); tr.sizeDelta = new Vector2(420f, 28f);
        }

        void BuildBody(Transform parent)
        {
            const float sidebarWidth = 260f;

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(parent, false);
            var br = body.GetComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
            br.offsetMin = Vector2.zero; br.offsetMax = new Vector2(0f, -StripHeight);

            var mapGO = new GameObject("MapArea", typeof(RectTransform));
            mapGO.transform.SetParent(body.transform, false);
            var mapArea = mapGO.GetComponent<RectTransform>();
            mapArea.anchorMin = new Vector2(0f, 0f); mapArea.anchorMax = new Vector2(1f, 1f);
            mapArea.offsetMin = new Vector2(12f, 12f); mapArea.offsetMax = new Vector2(-(sidebarWidth + 18f), -12f);
            var mapBg = mapGO.AddComponent<Image>();
            ThemeService.Tag(mapBg, ThemeRole.Panel2); mapBg.raycastTarget = true;
            mapGO.AddComponent<RectMask2D>();   // clip the grid texture/overlay to its own panel

            // One interaction host stretched over MapArea, carrying BOTH the controller and the renderer:
            // BattleGridRenderer.Build(hostRect) parents its own "Cells" RawImage (raycastTarget = true)
            // under this rect, and Unity's event system bubbles a pointer event from that RawImage up to
            // the nearest ancestor implementing the handler interfaces — this GameObject. The host's own
            // transparent Image (raycastTarget = true) additionally catches the pointer over any gutter
            // AspectRatioFitter leaves around the (possibly non-square) grid, so hover/exit still fire
            // there too. Mirrors DungeonViewController's host (viewGO + hitImg) in DungeonEditorScreen.
            var hostGO = new GameObject("BattleGridView", typeof(RectTransform));
            hostGO.transform.SetParent(mapArea, false);
            var hostRect = hostGO.GetComponent<RectTransform>();
            Stretch(hostRect);
            var hitImg = hostGO.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;
            view = hostGO.AddComponent<BattleGridViewController>();
            gridRenderer = hostGO.AddComponent<BattleGridRenderer>();
            gridRenderer.Build(hostRect);
            view.OnChanged = () => { Persist(); RefreshDoors(); RefreshSizeLabels(); };

            var sidebarGO = new GameObject("Sidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(body.transform, false);
            var sidebar = sidebarGO.GetComponent<RectTransform>();
            sidebar.anchorMin = new Vector2(1f, 0f); sidebar.anchorMax = new Vector2(1f, 1f);
            sidebar.offsetMin = new Vector2(-(sidebarWidth + 12f), 12f); sidebar.offsetMax = new Vector2(-12f, -12f);
            var sidebarBg = sidebarGO.AddComponent<Image>();
            ThemeService.Tag(sidebarBg, ThemeRole.Elev); sidebarBg.raycastTarget = false;

            BuildToolbar(sidebar);
        }

        // Scrollable toolbar content (mirrors DungeonInspectorPanel.BuildScroll: ScrollRect + Viewport
        // (RectMask2D) + Content (VerticalLayoutGroup + ContentSizeFitter)) — content-driven height with a
        // scroll fallback, same reasoning as every other sidebar panel in this project (cards/dialogs need
        // it; this toolbar has seven material buttons + three tools + brush size + two steppers + a
        // regenerate button stacked in a fixed-width column).
        void BuildToolbar(Transform sidebarParent)
        {
            var scrollGO = new GameObject("Scroll", typeof(RectTransform));
            scrollGO.transform.SetParent(sidebarParent, false);
            Stretch(scrollGO.GetComponent<RectTransform>());
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            Stretch(viewportGO.GetComponent<RectTransform>());
            scroll.viewport = viewportGO.GetComponent<RectTransform>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;

            BuildMaterialsSection(content.transform);
            BuildToolsSection(content.transform);
            BuildBrushSizeSection(content.transform);
            BuildSizeSection(content.transform);
            BuildRegenSection(content.transform);
        }

        // One button per row, full width — several labels here are long ("Труднопроходимо") and would
        // overflow (MakeText uses HorizontalWrapMode.Overflow, matching this project's convention) if
        // packed 2-3 to a row in a 260px sidebar. AddSelectableButton's flexibleWidth=1 fills the row
        // when it is the row's only child.
        void BuildMaterialsSection(Transform parent)
        {
            var sec = AddSection(parent, "MaterialsSection");
            AddCaption(sec.transform, "МАТЕРИАЛ");
            foreach (var opt in MaterialOptions)
            {
                var row = AddRow(sec.transform, $"Mat_{opt.cell}", 24f, 0f);
                AddMaterialButton(row.transform, opt);
            }
        }

        void AddMaterialButton(Transform parent, (GridCell cell, string label) opt)
        {
            bool active = view.Material == opt.cell;
            var (img, lbl) = AddSelectableButton(parent, opt.label, active, () => SetMaterial(opt.cell));
            materialButtons.Add((opt.cell, img, lbl));
        }

        void SetMaterial(GridCell cell)
        {
            view.Material = cell;
            foreach (var b in materialButtons) Retint(b.img, b.lbl, b.cell == cell);
        }

        // One per row too — "Прямоугольник" is long enough to overflow a 1/3-width slot.
        void BuildToolsSection(Transform parent)
        {
            var sec = AddSection(parent, "ToolsSection");
            AddCaption(sec.transform, "ИНСТРУМЕНТ");
            foreach (var opt in ToolOptions)
            {
                var row = AddRow(sec.transform, $"Tool_{opt.tool}", 24f, 0f);
                bool active = view.Tool == opt.tool;
                var (img, lbl) = AddSelectableButton(row.transform, opt.label, active, () => SetTool(opt.tool));
                toolButtons.Add((opt.tool, img, lbl));
            }
        }

        void SetTool(GridTool tool)
        {
            view.Tool = tool;
            foreach (var b in toolButtons) Retint(b.img, b.lbl, b.tool == tool);
            if (brushSizeSection != null) brushSizeSection.gameObject.SetActive(tool == GridTool.Brush);
        }

        void BuildBrushSizeSection(Transform parent)
        {
            brushSizeSection = AddSection(parent, "BrushSizeSection");
            AddCaption(brushSizeSection.transform, "РАЗМЕР КИСТИ");
            var row = AddRow(brushSizeSection.transform, "BrushSizeRow", 26f, 4f);
            foreach (int size in new[] { 1, 3, 5 })
            {
                bool active = view.BrushSize == size;
                var (img, lbl) = AddSelectableButton(row.transform, size.ToString(), active, () => SetBrushSize(size));
                brushSizeButtons.Add((size, img, lbl));
            }
            brushSizeSection.gameObject.SetActive(view.Tool == GridTool.Brush);
        }

        void SetBrushSize(int size)
        {
            view.BrushSize = size;
            foreach (var b in brushSizeButtons) Retint(b.img, b.lbl, b.size == size);
        }

        void BuildSizeSection(Transform parent)
        {
            var sec = AddSection(parent, "SizeSection");
            AddCaption(sec.transform, "РАЗМЕР СЕТКИ");
            widthLabel = BuildSizeStepper(sec.transform, "Ширина", () => view.Buffer.Width,
                delta => RequestResize(view.Buffer.Width + delta, view.Buffer.Height));
            heightLabel = BuildSizeStepper(sec.transform, "Высота", () => view.Buffer.Height,
                delta => RequestResize(view.Buffer.Width, view.Buffer.Height + delta));
        }

        void BuildRegenSection(Transform parent)
        {
            var sec = AddSection(parent, "RegenSection");
            AddFullWidthButton(sec.transform, "Пересобрать", ThemeRole.Accent, RequestRegenerate);
        }

        // ── Small builder primitives (self-contained, mirrors DungeonEditorScreen/DungeonInspectorPanel) ──

        VerticalLayoutGroup AddSection(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            return vlg;
        }

        RectTransform AddRow(Transform parent, string name, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go.GetComponent<RectTransform>();
        }

        Text AddCaption(Transform parent, string text)
        {
            var t = MakeText(parent, text, 10, ThemeRole.Mut, FontStyle.Bold, TextAnchor.UpperLeft);
            t.raycastTarget = false;
            return t;
        }

        // Shares row width equally with sibling buttons (flexibleWidth=1) — used for material/tool/brush
        // choices alike, same idiom as DungeonInspectorPanel.AddChoiceButton.
        (Image img, Text lbl) AddSelectableButton(Transform parent, string label, bool active, System.Action onClick)
        {
            var go = new GameObject($"Sel_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 11, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
            Retint(img, lbl, active);
            return (img, lbl);
        }

        void Retint(Image img, Text lbl, bool active)
        {
            ThemeService.Tag(img, active ? ThemeRole.AccentSoft : ThemeRole.Elev);
            ThemeService.Tag(lbl, active ? ThemeRole.AccentInk : ThemeRole.Txt);
        }

        // ◄ value ► stepper for the grid's width/height; returns the value Text so the caller can refresh
        // it after a resize/regenerate (this toolbar is built once at Awake, not rebuilt per Bind, so the
        // displayed number has to be pushed rather than recomputed on redraw). getValue/onStep read/act on
        // view.Buffer live at click time, never a value captured when the stepper was built.
        Text BuildSizeStepper(Transform parent, string caption, System.Func<int> getValue, System.Action<int> onStep)
        {
            var row = AddRow(parent, $"Step_{caption}", 22f, 4f);
            var cap = MakeText(row.transform, caption + ":", 11, ThemeRole.Mut, FontStyle.Normal, TextAnchor.MiddleLeft);
            cap.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;
            cap.raycastTarget = false;
            AddStepBtn(row.transform, "◄", () => onStep(-1));
            var valTxt = MakeText(row.transform, getValue().ToString(), 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            valTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            valTxt.raycastTarget = false;
            AddStepBtn(row.transform, "►", () => onStep(1));
            return valTxt;
        }

        void AddStepBtn(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Step_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            go.AddComponent<LayoutElement>().preferredWidth = 24f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        void AddFullWidthButton(Transform parent, string label, ThemeRole bgRole, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, bgRole);
            go.AddComponent<LayoutElement>().preferredHeight = 30f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
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
