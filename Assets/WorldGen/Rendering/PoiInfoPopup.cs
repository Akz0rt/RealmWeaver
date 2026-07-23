using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Lightweight read-only info card shown on a SINGLE click of a POI (world map). Displays the
    /// POI's icon, name, type, political region and a truncated description, plus an «Редактировать»
    /// button. The button invokes <see cref="OnEditRequested"/> (wired by PoiInteractionController to
    /// MapScreenController.OpenPoiEditor) so the popup stays decoupled from the editor screen.
    /// Positions itself near the marker's screen position each frame. Built entirely in code,
    /// following the PoiEditPanel conventions (ThemeService.Tag + LegacyRuntime font + VLG).
    /// </summary>
    public class PoiInfoPopup : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;
        public WorldMapRenderer mapRenderer;
        public RegionManager regionManager;
        [Tooltip("Камера, через которую позиция маркера проецируется на экран. Пусто — Camera.main.")]
        public Camera worldCamera;

        [Header("Вид")]
        public float cardWidth = 240f;
        [Tooltip("Смещение карточки вверх от экранной позиции маркера, в пикселях.")]
        public float screenYOffset = 24f;

        /// <summary>Invoked when «Редактировать» is pressed; carries the shown POI. Wired externally
        /// (PoiInteractionController → MapScreenController.OpenPoiEditor). Null = button is a no-op.</summary>
        public System.Action<PoiData> OnEditRequested;

        GameObject cardGO;
        RectTransform cardRect;
        Image iconImage;
        Text nameText;
        Text typeText;
        Text regionText;
        GameObject regionRowGO;
        Text descText;
        Font font;
        PoiData current;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            cardGO.SetActive(false);
        }

        void OnDisable() => Hide();

        public void Show(PoiData poi)
        {
            if (poi == null) { Hide(); return; }
            current = poi;
            Populate(poi);
            cardGO.SetActive(true);
            Reposition();
        }

        public void Hide()
        {
            current = null;
            if (cardGO != null) cardGO.SetActive(false);
        }

        void LateUpdate()
        {
            if (current == null || cardGO == null || !cardGO.activeSelf) return;
            // POI deleted out from under us → close.
            if (poiManager != null && poiManager.GetPoiById(current.Id) == null) { Hide(); return; }
            Reposition();
        }

        void Populate(PoiData poi)
        {
            iconImage.sprite = IconSpriteFor(poi);
            nameText.text = string.IsNullOrEmpty(poi.Name) ? "Без названия" : poi.Name;
            typeText.text = TypeLabel(poi.Type);

            string region = ResolveRegion(poi);
            regionRowGO.SetActive(!string.IsNullOrEmpty(region));
            regionText.text = region;

            descText.text = Truncate(poi.Description, 160);
        }

        string ResolveRegion(PoiData poi)
        {
            if (mapRenderer == null || regionManager == null) return "";
            return PoiRegionLabel.Resolve(
                poi.OwnerCellId,
                cellId => mapRenderer.GetCellById(cellId)?.RegionId ?? -1,
                rid => regionManager.Get(rid)?.Name);
        }

        Sprite IconSpriteFor(PoiData poi)
        {
            if (poi.CustomIconBytes != null && poi.CustomIconBytes.Length > 0)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(poi.CustomIconBytes))
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return PoiPlaceholderFactory.GetPlaceholder(poi.Type);
        }

        void Reposition()
        {
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null || current == null) return;

            float y = poiManager != null ? poiManager.poiYOffset : 0f;
            Vector3 world = mapRenderer != null
                ? mapRenderer.transform.TransformPoint(new Vector3(current.WorldPosition.X, y, current.WorldPosition.Y))
                : new Vector3(current.WorldPosition.X, y, current.WorldPosition.Y);

            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) { cardGO.SetActive(false); return; } // marker behind camera

            float half = cardWidth * 0.5f;
            float h = cardRect.sizeDelta.y;
            float x = Mathf.Clamp(screen.x, half + 4f, Screen.width - half - 4f);
            float yPos = Mathf.Clamp(screen.y + screenYOffset, 4f, Screen.height - h - 4f);
            cardRect.anchoredPosition = new Vector2(x, yPos);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "…";
        }

        static string TypeLabel(PoiType t) => t switch
        {
            PoiType.City => "Город",
            PoiType.Fortress => "Крепость",
            PoiType.Village => "Деревня",
            PoiType.Tower => "Башня",
            PoiType.Temple => "Храм",
            PoiType.Ruin => "Руины",
            PoiType.Dungeon => "Подземелье",
            PoiType.Encounter => "Встреча",
            PoiType.Port => "Порт",
            _ => "Точка",
        };

        // ── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiInfoPopupCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // above the map UI, below the POI editor (100)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            cardGO = new GameObject("Card");
            cardGO.transform.SetParent(canvasGO.transform, false);
            var bg = cardGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel);
            cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0f, 0f);
            cardRect.anchorMax = new Vector2(0f, 0f);
            cardRect.pivot = new Vector2(0.5f, 0f);
            cardRect.sizeDelta = new Vector2(cardWidth, 10f);
            UiShadow.Add(cardRect);

            var vlg = cardGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fitter = cardGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = cardGO.transform;
            BuildHeader(t);
            BuildRegionRow(t);
            descText = BuildBodyText(t, wrap: true, size: 11, role: ThemeRole.Mut);
            BuildEditButton(t);
        }

        void BuildHeader(Transform t)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(t, false);
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().minHeight = 24f;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(rowGO.transform, false);
            iconImage = iconGO.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconGO.AddComponent<LayoutElement>().preferredWidth = 24f;
            iconGO.GetComponent<LayoutElement>().preferredHeight = 24f;

            var textCol = new GameObject("Text");
            textCol.transform.SetParent(rowGO.transform, false);
            var colVlg = textCol.AddComponent<VerticalLayoutGroup>();
            colVlg.spacing = 0f;
            colVlg.childControlWidth = true;
            colVlg.childControlHeight = true;
            colVlg.childForceExpandWidth = true;
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;

            nameText = MakeText(textCol.transform, "", 13, ThemeRole.Txt, FontStyle.Bold, TextAnchor.LowerLeft);
            typeText = MakeText(textCol.transform, "", 10, ThemeRole.Mut, FontStyle.Normal, TextAnchor.UpperLeft);
        }

        void BuildRegionRow(Transform t)
        {
            regionRowGO = new GameObject("RegionRow");
            regionRowGO.transform.SetParent(t, false);
            regionRowGO.AddComponent<LayoutElement>().minHeight = 14f;
            regionText = regionRowGO.AddComponent<Text>();
            regionText.font = font;
            regionText.fontSize = 11;
            regionText.fontStyle = FontStyle.Italic;
            ThemeService.Tag(regionText, ThemeRole.Accent);
            regionText.alignment = TextAnchor.MiddleLeft;
            regionText.horizontalOverflow = HorizontalWrapMode.Overflow;
            regionRowGO.SetActive(false);
        }

        Text BuildBodyText(Transform t, bool wrap, int size, ThemeRole role)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(t, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            ThemeService.Tag(text, role);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return text;
        }

        void BuildEditButton(Transform t)
        {
            var go = new GameObject("EditButton");
            go.transform.SetParent(t, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                var poi = current;
                Hide();
                if (poi != null) OnEditRequested?.Invoke(poi);
            });
            go.AddComponent<LayoutElement>().preferredHeight = 28f;

            var label = MakeText(go.transform, "Редактировать", 12, ThemeRole.AccentInk, FontStyle.Bold, TextAnchor.MiddleCenter);
            var lr = label.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.sizeDelta = Vector2.zero;
            label.raycastTarget = false;
        }

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
    }
}
