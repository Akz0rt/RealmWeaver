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
        const int CompactCount = 5;      // rows shown before "Показать все N →"
        const float MaxRowsHeight = 320f; // rows area caps here and scrolls beyond it

        Canvas canvas;
        RectTransform panelRect;
        Text headerTitle;
        Text chevron;
        GameObject rowsViewportGO;
        RectTransform rowsContentRect;
        LayoutElement rowsViewportLE;
        ScrollRect rowsScroll;
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

            if (rowsViewportGO != null) rowsViewportGO.SetActive(!collapsed);
            ClampRowsHeight();
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

        void ClampRowsHeight()
        {
            if (rowsViewportLE == null || rowsContentRect == null) return;
            Canvas.ForceUpdateCanvases();
            float h = collapsed ? 0f : LayoutUtility.GetPreferredHeight(rowsContentRect);
            float capped = Mathf.Min(h, MaxRowsHeight);
            rowsViewportLE.preferredHeight = capped;
            if (rowsScroll != null) rowsScroll.vertical = h > MaxRowsHeight;
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
                    foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
                        entries.Add(new LegendEntry(RegionColorPalette.GetBiomeColor(biome), GetBiomeLabel(biome)));
                    break;

                case MapDisplayMode.Combined:
                    if (mapRenderer.showBiomeLayer)
                    {
                        foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
                            entries.Add(new LegendEntry(RegionColorPalette.GetBiomeColor(biome), GetBiomeLabel(biome)));
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

        static string GetBiomeLabel(Biome biome)
        {
            switch (biome)
            {
                case Biome.Ocean: return "Океан";
                case Biome.Lake: return "Озеро";
                case Biome.Beach: return "Пляж";
                case Biome.Snow: return "Снег";
                case Biome.Tundra: return "Тундра";
                case Biome.Bare: return "Скалы (bare)";
                case Biome.Scorched: return "Выжженная земля";
                case Biome.Taiga: return "Тайга";
                case Biome.Shrubland: return "Кустарники";
                case Biome.TemperateDesert: return "Умеренная пустыня";
                case Biome.TemperateRainForest: return "Умеренный дождевой лес";
                case Biome.TemperateDeciduousForest: return "Умеренный широколиственный лес";
                case Biome.Grassland: return "Луга";
                case Biome.TropicalRainForest: return "Тропический дождевой лес";
                case Biome.TropicalSeasonalForest: return "Тропический сезонный лес";
                case Biome.SubtropicalDesert: return "Субтропическая пустыня";
                default: return biome.ToString();
            }
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
            ThemeService.Tag(panelImage, ThemeRole.Panel, 0.92f);
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
            rowsViewportGO = new GameObject("RowsViewport");
            rowsViewportGO.transform.SetParent(parent, false);
            rowsViewportGO.AddComponent<RectMask2D>();
            rowsViewportLE = rowsViewportGO.AddComponent<LayoutElement>();
            rowsScroll = rowsViewportGO.AddComponent<ScrollRect>();
            rowsScroll.horizontal = false;
            rowsScroll.vertical = false;
            rowsScroll.movementType = ScrollRect.MovementType.Clamped;
            rowsScroll.scrollSensitivity = 18f;
            var viewportRect = rowsViewportGO.GetComponent<RectTransform>();
            rowsScroll.viewport = viewportRect;

            var contentGO = new GameObject("RowsContent");
            contentGO.transform.SetParent(rowsViewportGO.transform, false);
            var cl = contentGO.AddComponent<VerticalLayoutGroup>();
            cl.spacing = 3f;
            cl.childControlWidth = true;
            cl.childForceExpandWidth = true;
            cl.childControlHeight = true;
            cl.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowsContentRect = contentGO.GetComponent<RectTransform>();
            rowsContentRect.anchorMin = new Vector2(0f, 1f);
            rowsContentRect.anchorMax = new Vector2(1f, 1f);
            rowsContentRect.pivot = new Vector2(0.5f, 1f);
            rowsScroll.content = rowsContentRect;
        }

        void ToggleCollapsed()
        {
            collapsed = !collapsed;
            if (chevron != null) chevron.text = collapsed ? "▸" : "▾";
            if (rowsViewportGO != null) rowsViewportGO.SetActive(!collapsed);
            ClampRowsHeight();
        }

        void AddRow(Color color, string label)
        {
            var row = new GameObject("LegendRow");
            row.transform.SetParent(rowsContentRect, false);
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
            var swatchLE = swatchGO.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = swatchSize.x;
            swatchLE.preferredHeight = swatchSize.y;

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(row.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.fontSize = fontSize;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var textLE = textGO.AddComponent<LayoutElement>();
            textLE.flexibleWidth = 1f;
            textLE.preferredHeight = 16f;

            currentRows.Add(row);
        }

        void AddToggleAllRow(int total)
        {
            var go = new GameObject("ToggleAll");
            go.transform.SetParent(rowsContentRect, false);
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
