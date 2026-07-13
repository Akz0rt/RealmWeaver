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

        /// <summary>Invoked by «← К миру» / «Готово» and after delete. Wired to MapScreenController.ClosePoiEditor.</summary>
        public System.Action OnCloseRequested;

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
        readonly Dictionary<PoiType, (Image bg, Outline outline)> typeButtons =
            new Dictionary<PoiType, (Image, Outline)>();

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
        }

        /// <summary>Rebuild the live map-scale preview from the current POI. Stub until Task 4.</summary>
        public void RefreshPreview()
        {
            // Task 4 implements the map-scale icon+label preview inside PreviewContainer.
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
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Full-screen background under the top chrome (menu 40 + toolbar).
            var root = new GameObject("Root");
            root.transform.SetParent(canvasGO.transform, false);
            var rootImg = root.AddComponent<Image>();
            ThemeService.Tag(rootImg, ThemeRole.Bg);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = new Vector2(0f, -(40f + MapToolbarUI.BarHeightPixels));

            BuildTopBar(root.transform);
            BuildColumns(root.transform);
        }

        void BuildTopBar(Transform parent)
        {
            var bar = new GameObject("TopBar");
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
            // Left: preview area (~42% width). Right: edit panel.
            var left = new GameObject("PreviewColumn");
            left.transform.SetParent(parent, false);
            var leftBg = left.AddComponent<Image>();
            ThemeService.Tag(leftBg, ThemeRole.Panel2);
            var lr = left.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(0.42f, 1f);
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

            // Right: scrollable edit panel.
            var right = new GameObject("EditColumn");
            right.transform.SetParent(parent, false);
            var rr = right.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0.42f, 0f);
            rr.anchorMax = new Vector2(1f, 1f);
            rr.offsetMin = new Vector2(6f, 12f);
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
            var rowGO = new GameObject("TypeSelector");
            rowGO.transform.SetParent(t, false);
            var grid = rowGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(58f, 46f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 3 * 46f + 2 * 6f;

            var pickTypes = new (PoiType type, string label)[]
            {
                (PoiType.City, "Город"), (PoiType.Fortress, "Креп."), (PoiType.Village, "Дер."),
                (PoiType.Tower, "Башня"), (PoiType.Temple, "Храм"), (PoiType.Ruin, "Руины"),
                (PoiType.Dungeon, "Подзем."), (PoiType.Encounter, "Встр."), (PoiType.Camp, "Лагерь"),
                (PoiType.Port, "Порт"),
            };
            foreach (var (type, label) in pickTypes) AddTypeButton(rowGO.transform, type, label);
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
            iconRect.anchoredPosition = new Vector2(0f, -4f);
            iconRect.sizeDelta = new Vector2(22f, 22f);

            var lbl = MakeText(go.transform, label, 9, ThemeRole.Txt, FontStyle.Normal, TextAnchor.LowerCenter);
            var lblRect = lbl.rectTransform;
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(1f, 0f);
            lblRect.pivot = new Vector2(0.5f, 0f);
            lblRect.anchoredPosition = new Vector2(0f, 3f);
            lblRect.sizeDelta = new Vector2(0f, 12f);
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
            var rowGO = new GameObject("MapStub");
            rowGO.transform.SetParent(t, false);
            var bg = rowGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.6f);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 40f;
            var lbl = MakeText(rowGO.transform, "Скоро — генерация и редактор подземелья", 11, ThemeRole.Mut, FontStyle.Italic, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;
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
