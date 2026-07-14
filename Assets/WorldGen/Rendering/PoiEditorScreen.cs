using System.Collections.Generic;
using SFB;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Full-screen POI editor (a mutually-exclusive screen toggled by MapScreenController). Left
    /// column = live map-scale preview (populated in RefreshPreview — sub-project A Task 4); right
    /// column = the migrated PoiEditPanel controls (name/type/description/icon/scales/notes/delete),
    /// plus a stub «КАРТА ЛОКАЦИИ» section reserved for the cave-dungeon (sub-project B).
    ///
    /// Decoupled from the screen controller: the back/Готово button and delete call
    /// <see cref="OnCloseRequested"/> (wired to MapScreenController.ClosePoiEditor). Edits go through
    /// PoiManager.UpdatePoi*; every edit calls RefreshPreview() so the live preview stays in sync.
    /// UI is built lazily (EnsureBuilt) so Bind works even before the GameObject is first activated.
    /// </summary>
    public class PoiEditorScreen : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;
        [Tooltip("Notes root — резолвит/создаёт привязанную к POI группу страниц при «Открыть →».")]
        public NotesRootBuilder notesRoot;
        [Tooltip("Карта — превью показывает фрагмент реальной карты под точкой (для оценки масштаба иконки).")]
        public WorldMapRenderer mapRenderer;
        [Tooltip("Владелец подземелий — используется, чтобы показать статус «КАРТА ЛОКАЦИИ» и открыть редактор подземелья.")]
        public DungeonManager dungeonManager;

        /// <summary>Invoked by «← К миру» / «Готово» and after delete. Wired to MapScreenController.ClosePoiEditor.</summary>
        public System.Action OnCloseRequested;
        /// <summary>Invoked by the «КАРТА ЛОКАЦИИ» row. Wired to MapScreenController.OpenDungeonEditor.</summary>
        public System.Action<PoiData> OnOpenDungeonRequested;

        /// <summary>Reserved container for the live map-scale preview (filled in Task 4).</summary>
        public RectTransform PreviewContainer { get; private set; }

        PoiData current;
        bool built;

        Font font;
        InputField nameField;
        InputField descField;
        Image iconThumb;
        Slider iconScaleSlider;
        Slider labelScaleSlider;
        Text notesLabel;
        Text mapSectionLabel;
        Image previewIcon;
        Text previewLabel;
        RawImage previewMapImage;   // shows a fragment of the real map (RenderTexture) under the icon
        Camera previewCamera;
        RenderTexture previewRT;
        readonly Dictionary<PoiType, (Image bg, Outline outline)> typeButtons =
            new Dictionary<PoiType, (Image, Outline)>();

        // Preview camera half-height in world units (fixed reference zoom). The icon is drawn at its
        // TRUE world size relative to this fragment, so the DM can judge icon-vs-map scale. ~100 → the
        // fragment spans 200 world units; a default 36-unit icon reads at ~18% of the preview height.
        const float PreviewOrthoSize = 100f;
        const int PreviewRTSize = 512;

        static readonly ExtensionFilter[] IconFilters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        void Awake()
        {
            if (isActiveAndEnabled) EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (built) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            built = true;
        }

        /// <summary>Bind the editor to a POI and populate all controls. Safe before first activation.</summary>
        public void Bind(PoiData poi)
        {
            EnsureBuilt();
            current = poi;
            if (poi == null) return;
            ApplyTypeHighlight(poi.Type);
            nameField.SetTextWithoutNotify(poi.Name);
            descField.SetTextWithoutNotify(poi.Description);
            iconScaleSlider.SetValueWithoutNotify(poi.IconScale);
            labelScaleSlider.SetValueWithoutNotify(poi.LabelScale);
            UpdateIconThumb(poi);
            UpdateNotesLabel(poi);
            RefreshPreview();
            RefreshMapSectionLabel();
        }

        /// <summary>Refresh the preview: move the fragment camera over the POI's map location and draw the
        /// icon (and label) at their TRUE world size relative to that fragment, so the DM can judge how big
        /// the icon is on the map. Icon px = worldSize·IconScale × (previewSquare / fragmentWorldHeight).</summary>
        public void RefreshPreview()
        {
            if (PreviewContainer == null) return;
            EnsurePreviewWidgets();
            bool has = current != null;
            previewIcon.enabled = has;
            previewLabel.enabled = has;
            previewMapImage.enabled = has;
            if (!has) return;

            float h = PreviewContainer.rect.height;
            float w = PreviewContainer.rect.width;
            if (h < 1f || w < 1f) return; // not laid out yet (Bind runs before activation) — LateUpdate retries

            // Match the camera's aspect to the container so the fragment fills it without distortion,
            // and place the top-down fragment camera over the POI's world position.
            if (previewCamera != null)
            {
                previewCamera.aspect = w / h;
                float y = poiManager != null ? poiManager.poiYOffset : 0.5f;
                Vector3 world = mapRenderer != null
                    ? mapRenderer.transform.TransformPoint(new Vector3(current.WorldPosition.X, y, current.WorldPosition.Y))
                    : new Vector3(current.WorldPosition.X, y, current.WorldPosition.Y);
                previewCamera.transform.position = new Vector3(world.x, world.y + 1000f, world.z);
            }

            // Icon at TRUE scale: worldSize·IconScale → px via (containerHeight / fragmentWorldHeight),
            // where fragmentWorldHeight = 2·orthoSize.
            float worldToPx = h / (2f * PreviewOrthoSize);
            previewIcon.sprite = IconSpriteFor(current);
            float iconWorld = (poiManager != null ? poiManager.iconWorldSize : 36f) * Mathf.Max(0.01f, current.IconScale);
            float iconPx = iconWorld * worldToPx;
            previewIcon.rectTransform.sizeDelta = new Vector2(iconPx, iconPx);

            previewLabel.text = string.IsNullOrEmpty(current.Name) ? "Без названия" : current.Name;
            float labelWorld = (poiManager != null ? poiManager.labelCharacterSize : 1.5f) * Mathf.Max(0.01f, current.LabelScale) * 6f;
            previewLabel.fontSize = Mathf.Clamp(Mathf.RoundToInt(labelWorld * worldToPx), 8, 120);
            previewLabel.rectTransform.anchoredPosition = new Vector2(0f, -(iconPx * 0.5f) - 6f);
        }

        void EnsurePreviewWidgets()
        {
            if (previewIcon != null) return;

            // Fragment camera → RenderTexture: renders the real map WITHOUT POI markers (culls the "POI"
            // layer). Child of this screen, so it only renders while the editor is open.
            previewRT = new RenderTexture(PreviewRTSize, PreviewRTSize, 16) { name = "PoiPreviewRT" };
            var camGO = new GameObject("PoiPreviewCamera");
            camGO.transform.SetParent(transform, false);
            camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // top-down, like the main map camera
            previewCamera = camGO.AddComponent<Camera>();
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = PreviewOrthoSize;
            previewCamera.nearClipPlane = 0.3f;
            previewCamera.farClipPlane = 5000f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            previewCamera.targetTexture = previewRT;
            int poiLayer = LayerMask.NameToLayer("POI");
            previewCamera.cullingMask = poiLayer >= 0 ? ~(1 << poiLayer) : ~0; // everything except POI markers

            // Map fragment (centered square RawImage).
            var mapGO = new GameObject("PreviewMap");
            mapGO.transform.SetParent(PreviewContainer, false);
            previewMapImage = mapGO.AddComponent<RawImage>();
            previewMapImage.texture = previewRT;
            previewMapImage.raycastTarget = false;
            var mrr = previewMapImage.rectTransform;
            mrr.anchorMin = Vector2.zero;   // fill the whole preview container
            mrr.anchorMax = Vector2.one;
            mrr.offsetMin = Vector2.zero;
            mrr.offsetMax = Vector2.zero;

            // Icon overlay (on top of the fragment, centered = camera centre = POI position).
            var iconGO = new GameObject("PreviewIcon");
            iconGO.transform.SetParent(PreviewContainer, false);
            previewIcon = iconGO.AddComponent<Image>();
            previewIcon.preserveAspect = true;
            previewIcon.raycastTarget = false;
            var ir = previewIcon.rectTransform;
            ir.anchorMin = new Vector2(0.5f, 0.5f);
            ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot = new Vector2(0.5f, 0.5f);

            var lblGO = new GameObject("PreviewLabel");
            lblGO.transform.SetParent(PreviewContainer, false);
            previewLabel = lblGO.AddComponent<Text>();
            previewLabel.font = font;
            previewLabel.alignment = TextAnchor.UpperCenter;
            previewLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            previewLabel.verticalOverflow = VerticalWrapMode.Overflow;
            ThemeService.Tag(previewLabel, ThemeRole.Txt);
            previewLabel.raycastTarget = false;
            var lr = previewLabel.rectTransform;
            lr.anchorMin = new Vector2(0.5f, 0.5f);
            lr.anchorMax = new Vector2(0.5f, 0.5f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(PreviewContainer.rect.width, 30f);
        }

        void LateUpdate()
        {
            // Bind() runs before the editor GameObject is activated (container rect = 0 then), so re-run
            // the layout-dependent preview sizing each frame while open — this makes the fragment fill the
            // section and the icon read at the right size once the layout is computed.
            if (built && current != null) RefreshPreview();
        }

        void OnDestroy()
        {
            if (previewRT != null) { previewRT.Release(); Destroy(previewRT); previewRT = null; }
        }

        // ── Data helpers ─────────────────────────────────────────────────────────

        void UpdateIconThumb(PoiData poi)
        {
            if (iconThumb == null) return;
            iconThumb.sprite = IconSpriteFor(poi);
        }

        static Sprite IconSpriteFor(PoiData poi)
        {
            if (poi.CustomIconBytes != null && poi.CustomIconBytes.Length > 0)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(poi.CustomIconBytes))
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return PoiPlaceholderFactory.GetPlaceholder(poi.Type);
        }

        void UpdateNotesLabel(PoiData poi)
        {
            if (notesLabel == null) return;
            var doc = notesRoot != null ? notesRoot.DocumentController : null;
            var group = doc != null ? doc.FindGroupByPoiId(poi.Id) : null;
            notesLabel.text = group != null ? $"Группа «{group.Title}»" : "Заметки ещё не созданы";
        }

        void OnPickIconClicked()
        {
            if (current == null) return;
            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать иконку", "", IconFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            byte[] bytes = System.IO.File.ReadAllBytes(paths[0]);
            poiManager.UpdatePoiIconBytes(current.Id, bytes, paths[0]);
            UpdateIconThumb(current);
            RefreshPreview();
        }

        void ResetIconToType()
        {
            if (current == null) return;
            poiManager.UpdatePoiIconBytes(current.Id, null, null);
            UpdateIconThumb(current);
            RefreshPreview();
        }

        void OnOpenPagesClicked()
        {
            if (current == null || notesRoot == null) return;
            var doc = notesRoot.DocumentController;
            var group = doc.FindGroupByPoiId(current.Id);
            if (group == null)
            {
                group = doc.CreateGroup(current.Name, current.Id);
                doc.CreatePage(group.Id, "Страница 1");
            }
            doc.OpenPage(group.Pages[0].Id);
            // Notes live on the map screen (docked right), so return there to see them.
            OnCloseRequested?.Invoke();
        }

        // ── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("PoiEditorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // topmost: fully covers the map UI (toolbar/panels/legend) beneath
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Opaque full-screen background — a dedicated editing screen that covers everything; the
            // «← К миру» button is the only navigation out (no leftover map chrome peeking through).
            var root = new GameObject("Root");
            root.transform.SetParent(canvasGO.transform, false);
            var rootImg = root.AddComponent<Image>();
            ThemeService.Tag(rootImg, ThemeRole.Bg);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            BuildTopBar(root.transform);
            BuildColumns(root.transform);
        }

        void BuildTopBar(Transform parent)
        {
            var bar = new GameObject("TopBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var br = bar.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0f, 1f);
            br.anchorMax = new Vector2(1f, 1f);
            br.pivot = new Vector2(0.5f, 1f);
            br.sizeDelta = new Vector2(0f, 40f);
            br.anchoredPosition = Vector2.zero;

            var backGO = new GameObject("Back");
            backGO.transform.SetParent(bar.transform, false);
            var backImg = backGO.AddComponent<Image>();
            ThemeService.Tag(backImg, ThemeRole.Elev);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.onClick.AddListener(() => OnCloseRequested?.Invoke());
            var backRect = backGO.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.sizeDelta = new Vector2(120f, 28f);
            backRect.anchoredPosition = new Vector2(12f, 0f);
            var backLbl = MakeText(backGO.transform, "← К миру", 12, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(backLbl.rectTransform);
            backLbl.raycastTarget = false;

            var title = MakeText(bar.transform, "Редактирование локации", 14, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleLeft);
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(148f, 0f);
            tr.offsetMax = new Vector2(-12f, 0f);
        }

        void BuildColumns(Transform parent)
        {
            // Left: preview area (~34% width). Right: edit panel (with a clear gap so it never sits under the preview).
            var left = new GameObject("PreviewColumn");
            left.transform.SetParent(parent, false);
            var leftBg = left.AddComponent<Image>();
            ThemeService.Tag(leftBg, ThemeRole.Panel2);
            leftBg.raycastTarget = false;
            var lr = left.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(0.34f, 1f);
            lr.offsetMin = new Vector2(12f, 12f);
            lr.offsetMax = new Vector2(-6f, -52f);

            var caption = MakeText(left.transform, "КАК НА КАРТЕ", 10, ThemeRole.Mut, FontStyle.Bold, TextAnchor.UpperLeft);
            var cr = caption.rectTransform;
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.sizeDelta = new Vector2(0f, 16f);
            cr.anchoredPosition = new Vector2(10f, -8f);

            var preview = new GameObject("PreviewContainer");
            preview.transform.SetParent(left.transform, false);
            PreviewContainer = preview.AddComponent<RectTransform>();
            PreviewContainer.anchorMin = Vector2.zero;
            PreviewContainer.anchorMax = Vector2.one;
            PreviewContainer.offsetMin = new Vector2(10f, 10f);
            PreviewContainer.offsetMax = new Vector2(-10f, -28f);

            // Right: scrollable edit panel. Starts at 0.38 (preview ends at 0.34) → a clear ~4% gap.
            var right = new GameObject("EditColumn", typeof(RectTransform));
            right.transform.SetParent(parent, false);
            var rr = right.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0.38f, 0f);
            rr.anchorMax = new Vector2(1f, 1f);
            rr.offsetMin = new Vector2(14f, 12f);
            rr.offsetMax = new Vector2(-12f, -52f);

            var scrollRect = right.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(right.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0);
            var vpRect = viewportGO.GetComponent<RectTransform>();
            Stretch(vpRect);
            scrollRect.viewport = vpRect;

            var content = new GameObject("Content");
            content.transform.SetParent(viewportGO.transform, false);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 8, 4, 4);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            // Canonical ScrollRect content: fill the viewport width exactly (no inherited horizontal
            // offset that would push content left of the viewport and get it clipped by the mask).
            contentRect.sizeDelta = Vector2.zero;         // width = viewport; height set by ContentSizeFitter
            contentRect.anchoredPosition = Vector2.zero;  // top-centered on the anchor line
            scrollRect.content = contentRect;

            var t = content.transform;
            AddCaption(t, "ИМЯ");
            nameField = BuildInputField(t, multiline: false);
            nameField.onEndEdit.AddListener(v => { if (current != null) { poiManager.UpdatePoiName(current.Id, v); RefreshPreview(); } });

            AddCaption(t, "ТИП");
            BuildTypeSelector(t);

            AddCaption(t, "ОПИСАНИЕ");
            descField = BuildInputField(t, multiline: true);
            descField.onEndEdit.AddListener(v => { if (current != null) poiManager.UpdatePoiDescription(current.Id, v); });

            AddCaption(t, "ИКОНКА НА КАРТЕ");
            BuildIconRow(t);
            iconScaleSlider = AddScaleSliderRow(t, "Иконка", 1f, v => { if (current != null) { poiManager.UpdatePoiIconScale(current.Id, v); RefreshPreview(); } });
            labelScaleSlider = AddScaleSliderRow(t, "Подпись", 1f, v => { if (current != null) { poiManager.UpdatePoiLabelScale(current.Id, v); RefreshPreview(); } });

            AddCaption(t, "ПРИВЯЗАННЫЕ ЗАМЕТКИ");
            BuildNotesRow(t);

            BuildMapStub(t);
            BuildDeleteRow(t);
        }

        // ── Section builders (adapted from PoiEditPanel; the old panel is retired in Task 7) ──

        void BuildTypeSelector(Transform t)
        {
            var pickTypes = new (PoiType type, string label)[]
            {
                (PoiType.City, "Город"), (PoiType.Fortress, "Креп."), (PoiType.Village, "Дер."),
                (PoiType.Tower, "Башня"), (PoiType.Temple, "Храм"), (PoiType.Ruin, "Руины"),
                (PoiType.Dungeon, "Подзем."), (PoiType.Encounter, "Встр."), (PoiType.Camp, "Лагерь"),
                (PoiType.Port, "Порт"),
            };
            const int perRow = 5;
            const float rowHeight = 68f;
            const float rowSpacing = 6f;

            // Rows of HorizontalLayoutGroup with childForceExpandWidth → buttons stretch to fill the
            // full section width equally (GridLayoutGroup can't auto-fit its fixed cellSize to width).
            var container = new GameObject("TypeSelector");
            container.transform.SetParent(t, false);
            var vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = rowSpacing;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            int rows = (pickTypes.Length + perRow - 1) / perRow;
            container.AddComponent<LayoutElement>().preferredHeight = rows * rowHeight + (rows - 1) * rowSpacing;

            Transform currentRow = null;
            for (int i = 0; i < pickTypes.Length; i++)
            {
                if (i % perRow == 0)
                {
                    var rowGO = new GameObject($"Row{i / perRow}");
                    rowGO.transform.SetParent(container.transform, false);
                    var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = 6f;
                    hlg.childControlWidth = true;
                    hlg.childForceExpandWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandHeight = true;
                    rowGO.AddComponent<LayoutElement>().preferredHeight = rowHeight;
                    currentRow = rowGO.transform;
                }
                AddTypeButton(currentRow, pickTypes[i].type, pickTypes[i].label);
            }
        }

        void AddTypeButton(Transform parent, PoiType type, string label)
        {
            var go = new GameObject($"Type_{type}");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = false;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                if (current != null) poiManager.UpdatePoiType(current.Id, type);
                ApplyTypeHighlight(type);
                RefreshPreview();
            });

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var icon = iconGO.AddComponent<Image>();
            icon.sprite = PoiPlaceholderFactory.GetPlaceholder(type);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -8f);
            iconRect.sizeDelta = new Vector2(40f, 40f);

            var lbl = MakeText(go.transform, label, 11, ThemeRole.Txt, FontStyle.Normal, TextAnchor.LowerCenter);
            var lblRect = lbl.rectTransform;
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(1f, 0f);
            lblRect.pivot = new Vector2(0.5f, 0f);
            lblRect.anchoredPosition = new Vector2(0f, 5f);
            lblRect.sizeDelta = new Vector2(0f, 14f);
            lbl.raycastTarget = false;

            typeButtons[type] = (bg, outline);
        }

        void ApplyTypeHighlight(PoiType type)
        {
            foreach (var kvp in typeButtons)
            {
                bool on = kvp.Key == type;
                ThemeService.Tag(kvp.Value.bg, on ? ThemeRole.AccentSoft : ThemeRole.Elev);
                kvp.Value.outline.effectColor = ThemeService.Get(ThemeRole.Accent);
                kvp.Value.outline.enabled = on;
            }
        }

        void BuildIconRow(Transform t)
        {
            var rowGO = new GameObject("IconRow");
            rowGO.transform.SetParent(t, false);
            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = false;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            var previewGO = new GameObject("Preview");
            previewGO.transform.SetParent(rowGO.transform, false);
            var pbg = previewGO.AddComponent<Image>();
            ThemeService.Tag(pbg, ThemeRole.Elev);
            previewGO.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 28f);
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(previewGO.transform, false);
            iconThumb = iconGO.AddComponent<Image>();
            iconThumb.preserveAspect = true;
            iconThumb.raycastTarget = false;
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(3f, 3f);
            iconRect.offsetMax = new Vector2(-3f, -3f);

            AddIconActionButton(rowGO.transform, "+", ThemeRole.Accent, OnPickIconClicked);
            AddIconActionButton(rowGO.transform, "↺", ThemeRole.Elev, ResetIconToType);
        }

        void AddIconActionButton(Transform parent, string glyph, ThemeRole role, System.Action onClick)
        {
            var go = new GameObject($"IconBtn_{glyph}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, role);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 28f);
            var text = MakeText(go.transform, glyph, 15, role == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.raycastTarget = false;
        }

        void BuildNotesRow(Transform t)
        {
            var rowGO = new GameObject("NotesRow");
            rowGO.transform.SetParent(t, false);
            var bg = rowGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            notesLabel = MakeText(rowGO.transform, "Заметки ещё не созданы", 11, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleLeft);
            var lr = notesLabel.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(0.62f, 1f);
            lr.offsetMin = new Vector2(8f, 0f);
            lr.offsetMax = Vector2.zero;

            var open = MakeText(rowGO.transform, "Открыть →", 11, ThemeRole.Accent, FontStyle.Normal, TextAnchor.MiddleRight);
            var openBtn = open.gameObject.AddComponent<Button>();
            openBtn.targetGraphic = open;
            openBtn.onClick.AddListener(OnOpenPagesClicked);
            var orr = open.rectTransform;
            orr.anchorMin = new Vector2(0.5f, 0f);
            orr.anchorMax = new Vector2(1f, 1f);
            orr.offsetMin = Vector2.zero;
            orr.offsetMax = new Vector2(-8f, 0f);
        }

        void BuildMapStub(Transform t)
        {
            AddCaption(t, "КАРТА ЛОКАЦИИ");
            var rowGO = new GameObject("MapDungeonRow");
            rowGO.transform.SetParent(t, false);
            var bg = rowGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Elev);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 40f;
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => { if (current != null) OnOpenDungeonRequested?.Invoke(current); });
            mapSectionLabel = MakeText(rowGO.transform, "Создать карту подземелья", 12, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(mapSectionLabel.rectTransform);
            mapSectionLabel.raycastTarget = false;
        }

        void RefreshMapSectionLabel()
        {
            if (mapSectionLabel == null) return;
            int levels = (dungeonManager != null && current != null && dungeonManager.HasDungeon(current.Id))
                ? dungeonManager.GetByPoiId(current.Id).Levels.Count : 0;
            mapSectionLabel.text = levels > 0 ? $"Открыть карту подземелья ({levels} ур.)" : "Создать карту подземелья";
        }

        void BuildDeleteRow(Transform t)
        {
            var rowGO = new GameObject("DeleteRow");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            var del = MakeText(rowGO.transform, "Удалить точку", 12, ThemeRole.Danger, FontStyle.Bold, TextAnchor.MiddleLeft);
            var delBtn = del.gameObject.AddComponent<Button>();
            delBtn.targetGraphic = del;
            delBtn.onClick.AddListener(() =>
            {
                if (current != null) poiManager.DeletePoi(current.Id);
                OnCloseRequested?.Invoke();
            });
            var dr = del.rectTransform;
            dr.anchorMin = Vector2.zero;
            dr.anchorMax = Vector2.one;
            dr.offsetMin = new Vector2(8f, 0f);
            dr.offsetMax = Vector2.zero;
        }

        // ── Small builder primitives ───────────────────────────────────────────────

        InputField BuildInputField(Transform parent, bool multiline)
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
            textRect.anchorMin = new Vector2(0.02f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var ph = MakeText(go.transform, "", 12, ThemeRole.Mut, FontStyle.Italic, TextAnchor.UpperLeft);
            var phRect = ph.rectTransform;
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = ph;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 56f : 22f;
            le.flexibleWidth = 1f;
            return field;
        }

        Slider AddScaleSliderRow(Transform parent, string label, float defaultValue, System.Action<float> onChanged)
        {
            var rowGO = new GameObject($"{label}ScaleRow");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var lbl = MakeText(rowGO.transform, label, 11, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleLeft);
            lbl.rectTransform.sizeDelta = new Vector2(68f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 0.25f;
            slider.maxValue = 4f;
            slider.value = defaultValue;
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 20f);

            var bg = new GameObject("Bg");
            bg.transform.SetParent(sliderGO.transform, false);
            var bgImg = bg.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.42f);
            bgRect.anchorMax = new Vector2(1f, 0.58f);
            bgRect.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(sliderGO.transform, false);
            var fillImg = fill.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent, 0.9f);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.42f);
            fillRect.anchorMax = new Vector2(0f, 0.58f);
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRect = handleArea.AddComponent<RectTransform>();
            haRect.anchorMin = new Vector2(0f, 0.25f);
            haRect.anchorMax = new Vector2(1f, 0.75f);
            haRect.sizeDelta = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImg = handle.AddComponent<Image>();
            ThemeService.Tag(handleImg, ThemeRole.Accent);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(6f, 0f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            var valueText = MakeText(rowGO.transform, defaultValue.ToString("F2"), 11, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleRight);
            valueText.rectTransform.sizeDelta = new Vector2(40f, 20f);

            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = v.ToString("F2");
                onChanged(v);
            });
            return slider;
        }

        Text AddCaption(Transform parent, string text)
        {
            var label = MakeText(parent, text, 10, ThemeRole.Mut, FontStyle.Bold, TextAnchor.MiddleLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 13f;
            return label;
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

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
