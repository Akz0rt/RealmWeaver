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
    /// "Показать все N →" toggle that expands the full list (scrollable when it gets tall).
    /// </summary>
    public class MapLegendUI : MonoBehaviour
    {
        [Header("Источник данных")]
        public WorldMapRenderer mapRenderer;

        [Header("Настройки внешнего вида")]
        public Vector2 swatchSize = new Vector2(13f, 13f);
        public int fontSize = 12;

        const float PanelWidth = 232f;
        const int CompactCount = 14;     // rows shown before "Показать все N →" (all 13 biome families fit; only long Region lists collapse)

        Canvas canvas;
        RectTransform panelRect;
        Text headerTitle;
        Text chevron;
        GameObject rowsContainerGO;
        Transform rowsParent;
        readonly List<GameObject> currentRows = new List<GameObject>();

        bool collapsed;
        bool showAll;

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
            var visible = showAll ? entries : entries.Take(CompactCount).ToList();
            foreach (var entry in visible)
                AddRow(entry.color, entry.label);

            if (entries.Count > CompactCount)
                AddToggleAllRow(entries.Count);

            if (rowsContainerGO != null) rowsContainerGO.SetActive(!collapsed);
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
                    int regionCount = mapRenderer.GetActualRegionCount();
                    for (int i = 0; i < regionCount; i++)
                        entries.Add(new LegendEntry(RegionColorPalette.GetRegionColor(i), $"Область {i + 1}"));
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
            panelRect.anchoredPosition = new Vector2(20f, 20f);
            panelRect.sizeDelta = new Vector2(PanelWidth, panelRect.sizeDelta.y);

            var panelLayout = panelGO.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, 8, 8);
            panelLayout.spacing = 6f;
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
            rowGO.AddComponent<LayoutElement>().preferredHeight = 18f;
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
            rowsContainerGO = new GameObject("Rows");
            rowsContainerGO.transform.SetParent(parent, false);
            var cl = rowsContainerGO.AddComponent<VerticalLayoutGroup>();
            cl.spacing = 3f;
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
            row.AddComponent<LayoutElement>().preferredHeight = 18f;

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
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var text = go.AddComponent<Text>();
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
                if (row != null) Destroy(row);
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
