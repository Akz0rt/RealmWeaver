using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Строит и обновляет легенду рядом с картой, подстраиваясь под текущий MapDisplayMode.
    /// Laid out to match design_handoff_realmweaver_ui screens/*/01-main.png: compact card in the
    /// bottom-left with a "ЛЕГЕНДА · …" header + collapse chevron, a short default list, and a
    /// "Показать все N →" toggle that expands the full list. NOT scrollable — an earlier revision of this
    /// sentence promised "(scrollable when it gets tall)" and was never true of the code: BuildRowsArea
    /// records the scroll/mask attempt that was tried and reverted.
    ///
    /// THE CARD FITS THE SPACE IT IS GIVEN, and since the workspace shell that space is a PANE, not the
    /// window. This canvas is one of the map-chrome roots MapSurfaceHost frames (discovered at
    /// SurfaceRegistry.cs:314), so PaneChromeFrame.Ensure has inserted a stretched "__PaneFrame" carrying a
    /// RectMask2D between the canvas and this panel — which means the panel's PARENT rect is the pane's live
    /// content rect, and anything taller is CUT. A content-driven height that was comfortable full-screen
    /// therefore ran off the top of a short or split pane, and the DM could not read the biomes above the cut.
    /// MaxRowsForHeight reads that parent rect and caps how many rows Rebuild builds; LateUpdate re-runs
    /// Rebuild when the cap moves, so a divider drag or a window resize re-fits the card. Un-hosted (a scene
    /// with no shell, where nothing frames this canvas) the parent is the canvas itself — the whole window —
    /// so the cap is simply generous and nothing about the old layout changes.
    ///
    /// TWO ALTERNATIVES REJECTED. (1) A ScrollRect over the rows: BuildRowsArea's own comment records that
    /// exact attempt clipping rows on the left and breaking the card's vertical growth, and this legend is an
    /// explicitly TEMPORARY affordance that disappears once the map's own art lands — a row cap is the
    /// smallest thing that makes it usable. (2) Exempting this canvas from the frame so the mask stops
    /// clipping it: that mask is what stops a panel spilling across the divider into the neighbouring pane
    /// (PaneChromeFrame.cs:70), so the panel is what had to adapt, not the frame.
    ///
    /// KNOWN IMPRECISION, left for the DM to rule on rather than silently designed around: in a pane too
    /// short to hold every entry, "Показать все N →" expands to as many rows as fit, which can be fewer than
    /// N. The string is DM-facing copy matched to the design handoff, so it is kept verbatim instead of being
    /// reworded here. Only reachable in Region mode with many regions in a short pane.
    /// </summary>
    public class MapLegendUI : MonoBehaviour
    {
        [Header("Источник данных")]
        public WorldMapRenderer mapRenderer;

        [Header("Настройки внешнего вида")]
        public Vector2 swatchSize = new Vector2(13f, 13f);
        public int fontSize = 12;

        const float PanelWidth = 232f;
        // Rows shown before "Показать все N →" (biome legend is ≤7 rows now: Ocean+Lake+top-5; only long
        // Region lists collapse). ONE OF TWO GATES since the height cap: this is the DESIGN's short list, and
        // MaxRowsForHeight is how many rows physically fit in the pane. Rebuild takes the smaller.
        const int CompactCount = 14;

        // The card's own vertical arithmetic, named rather than left as literals at the five build sites
        // below, because MaxRowsForHeight has to REPRODUCE it exactly: a literal that drifted from the cap's
        // model would put the cap back off by a row, which is the whole defect the cap exists to close.
        const float RowHeight = 18f;      // LegendRow's / ToggleAll's LayoutElement.preferredHeight
        const float RowSpacing = 3f;      // the Rows container's VerticalLayoutGroup.spacing
        const float HeaderHeight = 18f;   // the Header row's LayoutElement.preferredHeight
        const float HeaderGap = 6f;       // the panel's own VerticalLayoutGroup.spacing (Header ↔ Rows)
        const int PanelPadTop = 8;
        const int PanelPadBottom = 8;
        const float PanelPaddingV = PanelPadTop + PanelPadBottom;
        // The card's offset from its parent's bottom-left corner (its anchoredPosition), and the same gap
        // deliberately kept clear at the TOP so a fitted card never butts against the pane's upper edge —
        // which would read as "cut off" even when nothing was actually clipped.
        const float EdgeMargin = 20f;

        Canvas canvas;
        RectTransform panelRect;
        Text headerTitle;
        Text chevron;
        GameObject rowsContainerGO;
        Transform rowsParent;
        readonly List<GameObject> currentRows = new List<GameObject>();

        bool collapsed;
        bool showAll;

        /// <summary>The row cap the rows on screen were built against — LateUpdate's change detector, so a
        /// resize costs one int comparison per frame instead of a rebuild per frame.
        ///
        /// NO `= -1` INITIALIZER, and that is deliberate rather than an omission. Unity restores a surviving
        /// MonoBehaviour across a Play-mode domain reload by DESERIALIZING it, so field initializers do not
        /// re-run and a plain field comes back at default(T) — the defect family this project has been bitten
        /// by repeatedly (WorkspaceController.shellSuppressed's doc carries the running count). default(int)
        /// is 0, MaxRowsForHeight never returns less than 1, so a reload lands on "the cap has moved" and the
        /// next LateUpdate rebuilds once. That is the safe direction: a spurious rebuild costs one frame's
        /// rows, where a value that happened to match would leave the card frozen at the wrong size.</summary>
        int lastRowCap;

        /// <summary>Legend panel's RectTransform, exposed so other UI can anchor relative to it.</summary>
        public RectTransform PanelRect => panelRect;

        void Awake() => BuildCanvasAndPanel();

        void OnEnable()
        {
            if (mapRenderer != null) mapRenderer.OnDisplayChanged += Rebuild;
        }

        void OnDisable()
        {
            if (mapRenderer != null) mapRenderer.OnDisplayChanged -= Rebuild;
        }

        void Start() => Rebuild();

        // ── Rebuild ──────────────────────────────────────────────────────────────

        public void Rebuild()
        {
            ClearRows();
            if (mapRenderer == null) return;

            if (headerTitle != null) headerTitle.text = "ЛЕГЕНДА · " + CurrentModeName();

            var entries = BuildEntriesForCurrentMode();

            // Recorded on the way past, so LateUpdate can tell "the pane changed size enough to change the
            // answer" from "the pane moved a pixel" without recomputing what Rebuild already knows.
            int cap = lastRowCap = MaxRowsForHeight();
            int want = showAll ? entries.Count : Mathf.Min(entries.Count, CompactCount);
            int rows = Mathf.Min(want, cap);

            // The toggle row costs a row slot of its own, so it is only paid for when the card is ACTUALLY out
            // of room — `rows + 1 > cap`, not "the list was truncated". Without that condition a full-height
            // pane in Region mode would silently drop its 14th region to make space for a toggle it had room
            // for anyway, i.e. the cap would change the un-constrained layout it is supposed to leave alone.
            // And at cap 1 the ROW wins over the toggle: one readable entry beats one link to entries that
            // would not fit either — the "readable at any pane height" floor.
            bool toggle = entries.Count > rows;
            if (toggle && rows + 1 > cap)
            {
                rows = Mathf.Max(1, cap - 1);
                toggle = cap >= 2;
            }

            for (int i = 0; i < rows; i++)
                AddRow(entries[i].color, entries[i].label);

            if (toggle) AddToggleAllRow(entries.Count);

            if (rowsContainerGO != null) rowsContainerGO.SetActive(!collapsed);
        }

        /// <summary>How many rows fit in the space the card is actually given, floor 1.
        ///
        /// THE PARENT RECT IS THE MEASUREMENT, and it is the right one on both paths with no branch to get
        /// wrong: hosted, this panel's parent is the "__PaneFrame" MapSurfaceHost drives from the pane's live
        /// corners, and PaneChromeFrame.Apply sets that frame's offsets so its rect height IS the pane content
        /// area's height in screen pixels; un-hosted, the parent is this class's own ScreenSpaceOverlay canvas,
        /// i.e. the window, exactly the space the pre-shell layout was designed against. Screen.height is only
        /// the fallback for a panel that has somehow not been parented at all.
        ///
        /// ONE FRAME BEHIND, accepted. The frame's offsets are written from Canvas.willRenderCanvases, which
        /// runs AFTER every LateUpdate — so during a divider drag this reads the previous frame's pane height
        /// and the cap lands a frame late. Invisible in practice, and the alternative is a
        /// Canvas.ForceUpdateCanvases() per frame, which MapSurfaceHost.ApplyViewportForRender's own doc
        /// rejects paying for exactly this class of staleness.</summary>
        int MaxRowsForHeight()
        {
            var parent = panelRect != null ? panelRect.parent as RectTransform : null;
            float available = parent != null ? parent.rect.height : Screen.height;
            float forRows = available - 2f * EdgeMargin - PanelPaddingV - HeaderHeight - HeaderGap;
            // n rows occupy n*RowHeight + (n-1)*RowSpacing; adding one RowSpacing to both sides of the divide
            // turns that into an exact floor without a special case for n == 0.
            int n = Mathf.FloorToInt((forRows + RowSpacing) / (RowHeight + RowSpacing));
            // FLOOR OF ONE, never zero: a card showing its header and nothing else is not "adapted", it is
            // broken. In a pane too short even for that, the RectMask2D cutting one row is the better failure.
            return Mathf.Max(1, n);
        }

        /// <summary>Re-fits the card when the space it was given changes size — a divider drag, a window
        /// resize, a pane appearing or collapsing. None of those raise OnDisplayChanged, which is the only
        /// thing that used to call Rebuild, so without this the cap would only ever be applied at the moment
        /// the DM happened to switch display mode.
        ///
        /// GATED ON THE CAP, NOT ON THE HEIGHT. The height moves every frame of a drag while the number of
        /// rows that fit changes a handful of times, and Rebuild destroys and rebuilds every row — rebuilding
        /// per pixel would strobe the card for the whole gesture.
        ///
        /// LateUpdate rather than Update, matching the deferred rebuilds TabStripView, NavigatorView and
        /// QuickOpenPopup all run: Rebuild's Destroy() is deferred to end of frame, so a rebuild issued
        /// mid-frame can overlap the rows it is replacing (see ClearRows, which is what actually closes that).
        ///
        /// Runs only while this chrome is ACTIVE, which is the whole cost argument: MapSurfaceHost.Hide
        /// SetActive(false)s this GameObject whenever the map surface owns no pane, so a session spent in the
        /// notes never executes this at all.</summary>
        void LateUpdate()
        {
            if (panelRect == null || mapRenderer == null) return;
            if (MaxRowsForHeight() != lastRowCap) Rebuild();
        }

        string CurrentModeName()
        {
            switch (mapRenderer.displayMode)
            {
                case MapDisplayMode.Height: return "ВЫСОТА";
                case MapDisplayMode.Region: return "ОБЛАСТИ";
                default: return "БИОМЫ";
            }
        }

        // ── Entry data (unchanged from the pre-redesign legend) ──────────────────

        struct LegendEntry
        {
            public Color color;
            public string label;
            public LegendEntry(Color c, string l) { color = c; label = l; }
        }

        List<LegendEntry> BuildEntriesForCurrentMode()
        {
            var entries = new List<LegendEntry>();

            switch (mapRenderer.displayMode)
            {
                case MapDisplayMode.Height:
                    entries.Add(new LegendEntry(new Color(0.10f, 0.25f, 0.50f), "Океан"));
                    entries.Add(new LegendEntry(new Color(0.30f, 0.55f, 0.65f), "Озеро"));
                    entries.Add(new LegendEntry(new Color(0.90f, 0.85f, 0.60f), "Пляж"));
                    entries.Add(new LegendEntry(new Color(0.35f, 0.55f, 0.25f), "Равнина / лес"));
                    entries.Add(new LegendEntry(new Color(0.45f, 0.40f, 0.35f), "Горы"));
                    entries.Add(new LegendEntry(Color.white, "Снежные пики"));
                    break;

                case MapDisplayMode.Biome:
                    entries.AddRange(GeneralizedBiomeEntries(mapRenderer.paletteTheme));
                    break;

                case MapDisplayMode.Combined:
                    if (mapRenderer.showBiomeLayer)
                    {
                        entries.AddRange(GeneralizedBiomeEntries(mapRenderer.paletteTheme));
                    }
                    else
                    {
                        entries.Add(new LegendEntry(new Color(0.82f, 0.78f, 0.65f), "Суша"));
                        entries.Add(new LegendEntry(new Color(0.10f, 0.25f, 0.50f), "Океан"));
                        entries.Add(new LegendEntry(new Color(0.30f, 0.55f, 0.65f), "Озеро"));
                    }
                    if (mapRenderer.showRegionBordersLayer)
                        entries.Add(new LegendEntry(mapRenderer.regionBorderColor, "Граница региона"));
                    if (mapRenderer.showCoastlineLayer)
                        entries.Add(new LegendEntry(mapRenderer.coastlineColor, "Берег"));
                    break;

                case MapDisplayMode.Region:
                default:
                    entries.Add(new LegendEntry(new Color(0.15f, 0.35f, 0.60f), "Океан"));
                    // Пользовательские регионы (имя + цвет из RegionsPanel); без regionManager (старые
                    // тестовые сцены) или без регионов на карте - список пуст, остаётся только "Океан".
                    if (mapRenderer.regionManager != null)
                        foreach (var r in mapRenderer.regionManager.Regions)
                            entries.Add(new LegendEntry(r.Color, r.Name));
                    break;
            }

            return entries;
        }

        /// <summary>Russian display name for a biome (18 land biomes + water). Isolated from the
        /// rendering/family logic — only used to label legend rows.</summary>
        static string BiomeName(Biome b)
        {
            switch (b)
            {
                case Biome.Ocean: return "Океан";           case Biome.Lake: return "Озеро";
                case Biome.Beach: return "Побережье";        case Biome.IceWaste: return "Ледяная пустошь";
                case Biome.Tundra: return "Тундра";          case Biome.Snow: return "Снега";
                case Biome.Glacier: return "Ледники";        case Biome.ColdSteppe: return "Холодная степь";
                case Biome.ForestTundra: return "Лесотундра";case Biome.Taiga: return "Тайга";
                case Biome.ConiferForest: return "Хвойный лес"; case Biome.Steppe: return "Степь";
                case Biome.Grassland: return "Луга";         case Biome.Forest: return "Лес";
                case Biome.RainForest: return "Дождевой лес";case Biome.SemiDesert: return "Полупустыня";
                case Biome.Shrubland: return "Кустарники";   case Biome.Savanna: return "Саванна";
                case Biome.WarmForest: return "Тёплый лес";  case Biome.Desert: return "Пустыня";
                case Biome.TropicalForest: return "Тропический лес"; default: return b.ToString();
            }
        }

        /// <summary>Legend for Biome/Combined modes: Ocean (always) + Lake (if any) + the top-5 land biomes by
        /// cell count on the current map, colored per-biome from the dark-fantasy palette.</summary>
        List<LegendEntry> GeneralizedBiomeEntries(MapRaster.MapPaletteTheme theme)
        {
            var entries = new List<LegendEntry>();
            Color Water(MapRaster.PaletteSlot s) => MapRaster.MapPalette.GetSlotColor(theme, s);
            entries.Add(new LegendEntry(Water(MapRaster.PaletteSlot.Sea), "Океан"));

            var cells = mapRenderer.Cells;
            bool anyLake = false;
            var counts = new Dictionary<Biome, int>();
            if (cells != null)
                foreach (var c in cells)
                {
                    if (c.EffectiveIsOcean) continue;
                    if (c.EffectiveIsLake) { anyLake = true; continue; }
                    if (c.Biome == Biome.Beach) continue;                 // coast handled as water/edge, not a land row
                    counts.TryGetValue(c.Biome, out int n);
                    counts[c.Biome] = n + 1;
                }

            if (anyLake) entries.Add(new LegendEntry(Water(MapRaster.PaletteSlot.LakeS), "Озеро"));

            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(5))
                entries.Add(new LegendEntry(MapRaster.MapPalette.GetBiomeColor(theme, kv.Key), BiomeName(kv.Key)));

            return entries;
        }

        // ── UI construction ──────────────────────────────────────────────────────

        void BuildCanvasAndPanel()
        {
            var canvasGO = new GameObject("MapLegendCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var panelGO = new GameObject("LegendPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImage = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImage, ThemeRole.Panel);
            AddBorder(panelGO, ThemeRole.Border);
            panelRect = panelGO.GetComponent<RectTransform>();
            // Bottom-left of the map area (Main-screen shell redesign). Hardcoded rather than read
            // from a serialized field, whose stale scene value would otherwise misplace it.
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(EdgeMargin, EdgeMargin);
            panelRect.sizeDelta = new Vector2(PanelWidth, panelRect.sizeDelta.y);

            var panelLayout = panelGO.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, PanelPadTop, PanelPadBottom);
            panelLayout.spacing = HeaderGap;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiShadow.Add(panelRect);

            BuildHeaderRow(panelGO.transform);
            BuildRowsArea(panelGO.transform);
        }

        void BuildHeaderRow(Transform parent)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = HeaderHeight;
            var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childForceExpandWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            headerTitle = titleGO.AddComponent<Text>();
            headerTitle.text = "ЛЕГЕНДА · БИОМЫ";
            headerTitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            headerTitle.fontSize = 11;
            headerTitle.fontStyle = FontStyle.Bold;
            headerTitle.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(headerTitle, ThemeRole.Mut);
            titleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var chevronGO = new GameObject("Chevron");
            chevronGO.transform.SetParent(rowGO.transform, false);
            chevron = chevronGO.AddComponent<Text>();
            chevron.text = "▾";
            chevron.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            chevron.fontSize = 12;
            chevron.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(chevron, ThemeRole.Mut);
            chevronGO.AddComponent<LayoutElement>().preferredWidth = 18f;
            var chevronBtn = chevronGO.AddComponent<Button>();
            chevronBtn.targetGraphic = chevron;
            chevronBtn.transition = Selectable.Transition.None;
            chevronBtn.onClick.AddListener(ToggleCollapsed);
        }

        void BuildRowsArea(Transform parent)
        {
            // Plain container (no ScrollRect/RectMask2D): the panel's own ContentSizeFitter grows
            // the card up from the bottom-left to fit whatever rows are shown. An earlier scroll/
            // mask attempt clipped rows on the left and broke the vertical growth.
            //
            // WHAT MAKES THAT UNBOUNDED GROWTH SAFE is no longer "the window is tall enough" — since the
            // workspace shell this card lives inside a PANE and is masked to it. Rebuild's row cap
            // (MaxRowsForHeight) is what bounds the growth now, upstream of this container, which is why this
            // container still needs no mask or scroll view of its own.
            rowsContainerGO = new GameObject("Rows");
            rowsContainerGO.transform.SetParent(parent, false);
            var cl = rowsContainerGO.AddComponent<VerticalLayoutGroup>();
            cl.spacing = RowSpacing;
            cl.childControlWidth = true;
            cl.childForceExpandWidth = true;
            cl.childControlHeight = true;
            cl.childForceExpandHeight = false;
            rowsParent = rowsContainerGO.transform;
        }

        void ToggleCollapsed()
        {
            collapsed = !collapsed;
            if (chevron != null) chevron.text = collapsed ? "▸" : "▾";
            if (rowsContainerGO != null) rowsContainerGO.SetActive(!collapsed);
        }

        void AddRow(Color color, string label)
        {
            var row = new GameObject("LegendRow");
            row.transform.SetParent(rowsParent, false);
            row.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = false;

            var swatchGO = new GameObject("Swatch");
            swatchGO.transform.SetParent(row.transform, false);
            var swatchImage = swatchGO.AddComponent<Image>();
            swatchImage.color = color;
            // Pin every swatch to exactly swatchSize (min == preferred, no flex) so they are all
            // identical regardless of row/label sizing.
            var swatchLE = swatchGO.AddComponent<LayoutElement>();
            swatchLE.minWidth = swatchLE.preferredWidth = swatchSize.x;
            swatchLE.minHeight = swatchLE.preferredHeight = swatchSize.y;
            swatchLE.flexibleWidth = swatchLE.flexibleHeight = 0f;

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(row.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            // Best-fit shrinks only the labels that would otherwise overrun the card's right edge
            // (e.g. "Умеренный широколиственный лес"); short labels stay at the max size.
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = fontSize;
            var textLE = textGO.AddComponent<LayoutElement>();
            textLE.flexibleWidth = 1f;
            textLE.preferredHeight = 16f;

            currentRows.Add(row);
        }

        void AddToggleAllRow(int total)
        {
            var go = new GameObject("ToggleAll");
            go.transform.SetParent(rowsParent, false);
            go.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            var text = go.AddComponent<Text>();
            // Verbatim DM-facing copy from the design handoff — see the class doc's KNOWN IMPRECISION note
            // for why the height cap does NOT reword «Показать все N →» when it cannot in fact show all N.
            text.text = showAll ? "Свернуть ↑" : $"Показать все {total} →";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(text, ThemeRole.Accent);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = text;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { showAll = !showAll; Rebuild(); });
            currentRows.Add(go);
        }

        void ClearRows()
        {
            foreach (var row in currentRows)
                if (row != null)
                {
                    // SetActive(false) before Destroy(), which is deferred to end of frame. Latent while
                    // Rebuild only ever fired on a display change; REAL now that LateUpdate also fires it, on
                    // a divider drag crossing a cap boundary — without this the stale and the freshly-built
                    // rows would both render for that frame, a visible doubled card mid-gesture. Same trap
                    // TabStripView.Rebuild, NavigatorView.Rebuild and QuickOpenPopup.RebuildRows all document.
                    row.SetActive(false);
                    Destroy(row);
                }
            currentRows.Clear();
        }

        void AddBorder(GameObject go, ThemeRole role)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(role);
            outline.effectDistance = new Vector2(1f, -1f);
        }
    }
}
