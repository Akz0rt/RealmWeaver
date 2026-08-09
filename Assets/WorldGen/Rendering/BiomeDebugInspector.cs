using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Debug-инструмент: СРЕДНЯЯ кнопка мыши по карте → печатает и показывает на экране, какой
    /// биом в этой клетке и ПОЧЕМУ (уровни температуры/влажности из 5, охлаждение высотой, форма
    /// рельефа, override'ы). Средняя кнопка выбрана намеренно — не конфликтует с кистью/выделением/POI
    /// (ЛКМ) и панорамой камеры (ПКМ). Повесь компонент на любой объект сцены и задай mapRenderer.
    /// </summary>
    public class BiomeDebugInspector : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public Camera raycastCamera;

        [Tooltip("Включить дебаг-инспектор (средняя кнопка мыши по клетке карты).")]
        public bool enableInspector = true;

        static readonly string[] TempNames = { "Ледяной", "Холодный", "Умеренный", "Тёплый", "Жаркий" };
        static readonly string[] MoistNames = { "Сухой", "Полусухой", "Умеренный", "Влажный", "Мокрый" };
        static readonly string[] LandformNames = { "Равнина", "Холмы", "Горы", "Вершины" };

        string info;       // текст последней инспекции (для OnGUI); null/empty → ничего не рисуем
        Vector2 guiPos;    // где рисовать плашку (экранные координаты OnGUI, начало сверху-слева)

        void Awake()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
        }

        void Update()
        {
            if (!enableInspector || mapRenderer == null || raycastCamera == null) return;
            if (Mouse.current == null) return;
            if (!Mouse.current.middleButton.wasPressedThisFrame) return;
            // Не инспектируем "сквозь" UI-панели.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            if (!mapRenderer.TryGetSiteHitPoint(ray, out Vector2 site))
            {
                info = null; // клик мимо карты
                return;
            }

            var cell = mapRenderer.NearestLookup != null
                ? mapRenderer.NearestLookup.FindNearest(new System.Numerics.Vector2(site.x, site.y))
                : null;
            if (cell == null) { info = null; return; }

            info = BuildInfo(cell, site);
            // Input System: Y снизу; OnGUI: Y сверху → инвертируем и чуть смещаем от курсора.
            guiPos = new Vector2(mousePos.x + 14f, Screen.height - mousePos.y + 14f);
            Debug.Log("[Biome Debug]\n" + info);
        }

        string BuildInfo(VoronoiCell c, Vector2 site)
        {
            int tL = BiomeMatrix.Level5(c.EffectiveTemperature);
            int mL = BiomeMatrix.Level5(c.EffectiveMoisture);
            var landform = LandformClassifier.Of(c.EffectiveElevation);
            var family = MapPalette.GetFamily(c.Biome);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Клетка #{c.Id}  @ ({site.x:F0}, {site.y:F0})");
            sb.AppendLine($"Биом: {c.Biome}   семейство: {family}");
            if (c.EffectiveIsOcean) sb.AppendLine("Вода: океан");
            else if (c.EffectiveIsLake) sb.AppendLine("Вода: озеро");
            sb.AppendLine($"Температура: {c.EffectiveTemperature:F2}  →  ур. {tL + 1}/5 ({TempNames[tL]})");
            sb.AppendLine($"Влажность: {c.EffectiveMoisture:F2}  →  ур. {mL + 1}/5 ({MoistNames[mL]})");
            sb.AppendLine($"Высота: {c.EffectiveElevation:F2}  →  {LandformNames[(int)landform]}");
            bool anyOverride = c.TemperatureOverride.HasValue || c.MoistureOverride.HasValue
                            || c.ElevationOverride.HasValue || c.WaterOverride != WaterOverrideType.None;
            if (anyOverride)
                sb.Append($"Override: T={Fmt(c.TemperatureOverride)} M={Fmt(c.MoistureOverride)} " +
                          $"E={Fmt(c.ElevationOverride)} вода={c.WaterOverride}");
            return sb.ToString();
        }

        static string Fmt(float? v) => v.HasValue ? v.Value.ToString("F2") : "-";

        void OnGUI()
        {
            if (!enableInspector || string.IsNullOrEmpty(info)) return;

            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6),
            };
            style.normal.textColor = Color.white;

            var content = new GUIContent(info);
            const float w = 360f;
            float h = style.CalcHeight(content, w);
            float x = Mathf.Clamp(guiPos.x, 4f, Screen.width - w - 4f);
            float y = Mathf.Clamp(guiPos.y, 4f, Screen.height - h - 4f);

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.05f, 0.08f, 0.11f, 0.95f);
            GUI.Box(new Rect(x, y, w, h), content, style);
            GUI.backgroundColor = prevBg;
        }
    }
}
