using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Строит и обновляет легенду (список "цветной квадрат + название") рядом с картой,
    /// автоматически подстраиваясь под текущий MapDisplayMode у назначенного WorldMapRenderer.
    ///
    /// Создаёт весь UI программно (Canvas/Panel/строки) при старте - не требует ручной
    /// раскладки элементов в инспекторе. Использует встроенный Unity UI (uGUI), без TextMeshPro,
    /// чтобы не требовать дополнительных пакетов "из коробки".
    /// </summary>
    public class MapLegendUI : MonoBehaviour
    {
        [Header("Источник данных")]
        [Tooltip("WorldMapRenderer, легенду которого нужно отображать.")]
        public WorldMapRenderer mapRenderer;

        [Header("Настройки внешнего вида")]
        public Vector2 panelAnchoredPosition = new Vector2(-20f, -20f);
        public Vector2 swatchSize = new Vector2(20f, 20f);
        public int fontSize = 14;
        public Color panelBackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        public Color textColor = Color.white;

        Canvas canvas;
        RectTransform panelRect;
        VerticalLayoutGroup layoutGroup;
        readonly List<GameObject> currentRows = new List<GameObject>();

        /// <summary>Legend panel's RectTransform, exposed so other UI (e.g. PoiEditPanel) can anchor below it.</summary>
        public RectTransform PanelRect => panelRect;

        void Awake()
        {
            BuildCanvasAndPanel();
            NotesLayoutController.OnSplitFractionChanged += UpdateSplitAnchor;
        }

        void OnDestroy()
        {
            NotesLayoutController.OnSplitFractionChanged -= UpdateSplitAnchor;
        }

        void UpdateSplitAnchor(float fraction)
        {
            panelRect.anchorMin = new Vector2(fraction, 1f);
            panelRect.anchorMax = new Vector2(fraction, 1f);
        }

        void OnEnable()
        {
            if (mapRenderer != null)
                mapRenderer.OnDisplayChanged += Rebuild;
        }

        void OnDisable()
        {
            if (mapRenderer != null)
                mapRenderer.OnDisplayChanged -= Rebuild;
        }

        void Start()
        {
            Rebuild(); // первичная отрисовка - на случай, если карта уже была сгенерирована до того, как легенда включилась
        }

        /// <summary>Пересобирает все строки легенды под текущий displayMode источника. Безопасно вызывать несколько раз - старые строки очищаются перед перестроением.</summary>
        public void Rebuild()
        {
            ClearRows();
            if (mapRenderer == null) return;

            var entries = BuildEntriesForCurrentMode();
            foreach (var entry in entries)
                AddRow(entry.color, entry.label);
        }

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
                        // Нейтральные тона базовой заливки (совпадают с RegionColorPalette.GetNeutralBaseColor).
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

        // --- Построение UI ---

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
            ThemeService.Tag(panelImage, ThemeRole.Panel, 0.55f);

            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(NotesLayoutController.SplitFraction, 1f); // привязка к правому верхнему углу карты (не всего экрана)
            panelRect.anchorMax = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = panelAnchoredPosition;

            layoutGroup = panelGO.AddComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            layoutGroup.spacing = 4f;
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;

            var fitter = panelGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void AddRow(Color color, string label)
        {
            var row = new GameObject("LegendRow");
            row.transform.SetParent(panelRect, false);

            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;

            var rowFitter = row.AddComponent<ContentSizeFitter>();
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Цветной квадратик
            var swatchGO = new GameObject("Swatch");
            swatchGO.transform.SetParent(row.transform, false);
            var swatchImage = swatchGO.AddComponent<Image>();
            swatchImage.color = color;
            var swatchRect = swatchGO.GetComponent<RectTransform>();
            swatchRect.sizeDelta = swatchSize;

            // Текст-подпись
            var textGO = new GameObject("Label");
            textGO.transform.SetParent(row.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.color = textColor;
            text.fontSize = fontSize;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // встроенный шрифт Unity - доступен без дополнительных импортов
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(220f, swatchSize.y);

            currentRows.Add(row);
        }

        void ClearRows()
        {
            foreach (var row in currentRows)
                if (row != null) Destroy(row);
            currentRows.Clear();
        }
    }
}
