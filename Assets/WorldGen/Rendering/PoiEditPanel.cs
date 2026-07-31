using System.Collections.Generic;
using SFB;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Standalone screen-space panel for editing the currently selected POI: type, name,
    /// description, cell binding, custom icon path, and per-POI icon/label scale.
    /// Anchored to the top-right corner of its HOST — the pane frame PaneChromeFrame inserts when the
    /// workspace shell hosts this canvas, and the canvas itself when it does not. Height auto-fits
    /// content but is clamped to the host's remaining height below the toolbar, scrolling if needed.
    /// Shows itself when a POI is selected via PoiManager.OnSelectionChanged, hides on deselect.
    /// legendUI/gapBelowLegend below are INERT: this panel docked under MapLegendUI until the legend
    /// moved to the bottom-left in Screen A, and both survive only because they are serialized into
    /// SampleScene, which no task before 11 may edit. Nothing reads either one.
    /// </summary>
    public class PoiEditPanel : MonoBehaviour
    {
        [Header("Источники")]
        public PoiManager poiManager;
        [Tooltip("НЕ ИСПОЛЬЗУЕТСЯ. Панель докалась под легенду, пока в Screen A легенда не уехала в низ-лево; " +
                 "теперь она встаёт в правый верхний угол своего хозяина (см. RepositionPanel). Поле осталось " +
                 "только потому, что записано в SampleScene, а править сцену нельзя до задачи 11.")]
        public MapLegendUI legendUI;
        [Tooltip("Notes root — resolves/creates the POI's linked page group when \"Открыть страницы\" is clicked.")]
        public NotesRootBuilder notesRoot;

        [Header("Внешний вид")]
        [Tooltip("Горизонтальный отступ от правого края ХОЗЯИНА — рамки вкладки, а без неё самого канваса (= экрана).")]
        public float rightMargin = 20f;
        [Tooltip("НЕ ИСПОЛЬЗУЕТСЯ — см. legendUI выше.")]
        public float gapBelowLegend = 20f;
        [Tooltip("Отступ от нижнего края ХОЗЯИНА, ниже которого панель не опускается.")]
        public float bottomScreenMargin = 20f;
        public float panelWidth = 240f;

        GameObject panelGO;
        RectTransform panelRect;
        RectTransform contentRect;
        LayoutElement scrollAreaLE;
        readonly Dictionary<PoiType, (Image bg, Outline outline)> typeButtons =
            new Dictionary<PoiType, (Image, Outline)>();
        InputField poiNameField;
        InputField poiDescField;
        Image iconPreview;
        Text notesLabel;
        Slider iconScaleSlider;
        Slider labelScaleSlider;
        Font builtinFont;

        static readonly ExtensionFilter[] IconFilters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
            panelGO.SetActive(false);
        }

        void OnEnable()
        {
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged += HandleSelectionChanged;
                poiManager.OnPoisChanged += RefreshFromSelection;
            }
        }

        void OnDisable()
        {
            if (poiManager != null)
            {
                poiManager.OnSelectionChanged -= HandleSelectionChanged;
                poiManager.OnPoisChanged -= RefreshFromSelection;
            }
        }

        void LateUpdate()
        {
            // Content height changes with the selected POI's description length, so re-clamp the
            // scroll area to the remaining screen space every frame (cheap: a couple of RectTransform reads).
            RepositionPanel();
        }

        void RepositionPanel()
        {
            if (panelRect == null) return;

            // Фиксированная позиция: правый верхний угол хозяина (якорь (1,1), pivot справа — см. BuildUI),
            // верх — сразу под 46px тулбаром + отступ. Раньше панель докалась под легенду, но в Screen A
            // легенда уехала в низ-лево, так что отвязываем.
            // Слагаемое «40 меню» УБРАНО: оболочка workspace ограничивает этот канвас областью ContentArea
            // вкладки (PaneChromeFrame через MapSurfaceHost), а тот прямоугольник уже и ниже меню-бара, и
            // ниже полосы вкладок. Полное обоснование: MapLayersPanel.cs:74.
            float topY = -(MapToolbarUI.BarHeightPixels + 20f);
            panelRect.anchoredPosition = new Vector2(-rightMargin, topY);

            // Высота берётся у РОДИТЕЛЯ, а не у Screen.height: родитель — это PaneChromeFrame (или сам канвас,
            // когда рамки ещё нет), т.е. ровно тот прямоугольник, в котором панель живёт. Со Screen.height
            // панель внутри узкой вкладки считала себе высоту целого окна и уезжала за нижний край панели, где
            // RectMask2D рамки её обрезал. Оба случая покрыты одним выражением, спец-веток нет.
            var host = panelRect.parent as RectTransform;
            float hostHeight = host != null ? host.rect.height : Screen.height;
            float availableHeight = hostHeight + topY - bottomScreenMargin;
            float contentHeight = LayoutUtility.GetPreferredHeight(contentRect);
            float desiredHeight = Mathf.Min(contentHeight, Mathf.Max(availableHeight, 0f));

            scrollAreaLE.preferredHeight = desiredHeight;
            panelRect.sizeDelta = new Vector2(panelWidth, desiredHeight + 16f); // +padding
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        void HandleSelectionChanged(PoiData selected)
        {
            if (selected == null)
            {
                panelGO.SetActive(false);
                return;
            }

            panelGO.SetActive(true);
            ApplyTypeHighlight(selected.Type);
            poiNameField.text = selected.Name;
            poiDescField.text = selected.Description;
            iconScaleSlider.SetValueWithoutNotify(selected.IconScale);
            labelScaleSlider.SetValueWithoutNotify(selected.LabelScale);
            UpdateIconPreview(selected);
            UpdateNotesLabel(selected);
        }

        void RefreshFromSelection()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel != null) UpdateNotesLabel(sel);
        }

        void UpdateIconPreview(PoiData poi)
        {
            if (iconPreview == null) return;
            if (poi.CustomIconBytes != null && poi.CustomIconBytes.Length > 0)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(poi.CustomIconBytes))
                {
                    iconPreview.sprite = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    return;
                }
            }
            iconPreview.sprite = PoiPlaceholderFactory.GetPlaceholder(poi.Type);
        }

        void UpdateNotesLabel(PoiData poi)
        {
            if (notesLabel == null) return;
            var doc = notesRoot != null ? notesRoot.DocumentController : null;
            var group = doc != null ? doc.FindGroupByPoiId(poi.Id) : null;
            notesLabel.text = group != null ? $"Группа «{group.Title}»" : "Заметки ещё не созданы";
        }

        // ── Type selector (4 icon buttons, replacing the old dropdown) ────────────

        void BuildTypeSelector(Transform t)
        {
            var rowGO = new GameObject("TypeSelector");
            rowGO.transform.SetParent(t, false);
            var grid = rowGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(58f, 46f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            // 10 buttons at 4 columns = 3 rows. The parent VLG allocates this row's height from the
            // LayoutElement (layoutPriority 1) — which OVERRIDES GridLayoutGroup's computed height
            // (priority 0) — so it MUST be tall enough for 3 rows or the grid clips to one row.
            rowGO.AddComponent<LayoutElement>().preferredHeight = 3 * 46f + 2 * 6f; // 150

            var pickTypes = new (PoiType type, string label)[]
            {
                (PoiType.City, "Город"), (PoiType.Fortress, "Креп."),
                (PoiType.Tower, "Башня"), (PoiType.Temple, "Храм"), (PoiType.Ruin, "Руины"),
                (PoiType.Dungeon, "Подзем."), (PoiType.Encounter, "Встр."),
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
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiType(sel.Id, type);
                ApplyTypeHighlight(type);
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

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var lbl = labelGO.AddComponent<Text>();
            lbl.text = label;
            lbl.font = builtinFont;
            lbl.fontSize = 9;
            ThemeService.Tag(lbl, ThemeRole.Txt);
            lbl.alignment = TextAnchor.LowerCenter;
            lbl.raycastTarget = false;
            var lblRect = labelGO.GetComponent<RectTransform>();
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(1f, 0f);
            lblRect.pivot = new Vector2(0.5f, 0f);
            lblRect.anchoredPosition = new Vector2(0f, 3f);
            lblRect.sizeDelta = new Vector2(0f, 12f);

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

        // ── UI Construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            panelWidth = 280f; // фикс. ширина под макетную раскладку (4 кнопки типа + поля)

            var canvasGO = new GameObject("PoiEditCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            panelGO = new GameObject("PoiEditPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel);
            panelRect = panelGO.GetComponent<RectTransform>();
            // Правый верхний угол ХОЗЯИНА (рамки вкладки, а без неё — самого канваса), а не доля
            // NotesLayoutController.SplitFraction, как было. SplitFraction (по умолчанию 2/3) описывал
            // границу СТАРОГО раздела «карта | заметки», которого больше нет: якорь считается от РОДИТЕЛЯ, то
            // есть внутри вкладки панель вставала на 2/3 её ширины и оставляла треть вкладки пустой.
            // Задача 10b удаляет NotesLayoutController целиком, так что подписка на OnSplitFractionChanged (и
            // UpdateSplitAnchor вместе с ней) убраны здесь, а не оставлены умирать. RepositionPanel так же
            // берёт высоту у родителя — обе оси теперь меряются от одного и того же прямоугольника.
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(panelWidth, 0f);
            UiShadow.Add(panelRect);

            // Scroll area: content auto-sizes via ContentSizeFitter, but RepositionPanel() clamps
            // scrollAreaLE.preferredHeight to whatever screen space remains below the top chrome,
            // so tall content scrolls instead of pushing the panel off-screen.
            var scrollAreaGO = new GameObject("ScrollArea");
            scrollAreaGO.transform.SetParent(panelGO.transform, false);
            scrollAreaLE = scrollAreaGO.AddComponent<LayoutElement>();
            var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
            panelVLG.childControlWidth = true;
            panelVLG.childControlHeight = true;
            panelVLG.childForceExpandWidth = true;
            panelVLG.padding = new RectOffset(8, 8, 8, 8);

            var scrollRect = scrollAreaGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollAreaGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            scrollRect.viewport = viewportRect;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.spacing = 4f;
            contentVLG.childControlWidth = true;
            contentVLG.childForceExpandWidth = true;
            contentVLG.childControlHeight = true;
            contentVLG.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            scrollRect.content = contentRect;

            var t = contentGO.transform;

            BuildHeader(t);

            AddCaption(t, "ИМЯ");
            poiNameField = BuildInputField(t, multiline: false);
            poiNameField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiName(sel.Id, v);
            });

            AddCaption(t, "ТИП");
            BuildTypeSelector(t);

            AddCaption(t, "ОПИСАНИЕ");
            poiDescField = BuildInputField(t, multiline: true);
            poiDescField.onEndEdit.AddListener(v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiDescription(sel.Id, v);
            });

            AddCaption(t, "ИКОНКА НА КАРТЕ");
            BuildIconRow(t);
            iconScaleSlider = AddScaleSliderRow(t, "Иконка", 1f, v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiIconScale(sel.Id, v);
            });
            labelScaleSlider = AddScaleSliderRow(t, "Подпись", 1f, v =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.UpdatePoiLabelScale(sel.Id, v);
            });

            AddCaption(t, "ПРИВЯЗАННЫЕ ЗАМЕТКИ");
            BuildNotesRow(t);

            BuildFooter(t);
        }

        // ── Mockup-style section builders ─────────────────────────────────────────

        void BuildHeader(Transform t)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 20f;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Редактирование точки";
            title.font = builtinFont;
            title.fontSize = 13;
            title.fontStyle = FontStyle.Bold;
            ThemeService.Tag(title, ThemeRole.Txt);
            title.alignment = TextAnchor.MiddleLeft;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = new Vector2(-24f, 0f);

            var closeGO = new GameObject("Close");
            closeGO.transform.SetParent(rowGO.transform, false);
            var closeImg = closeGO.AddComponent<Image>();
            ThemeService.Tag(closeImg, ThemeRole.Elev);
            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.onClick.AddListener(() => poiManager?.DeselectAll());
            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(20f, 20f);
            var xGO = new GameObject("X");
            xGO.transform.SetParent(closeGO.transform, false);
            var xText = xGO.AddComponent<Text>();
            xText.text = "✕";
            xText.font = builtinFont;
            xText.fontSize = 12;
            ThemeService.Tag(xText, ThemeRole.Mut);
            xText.alignment = TextAnchor.MiddleCenter;
            var xtr = xGO.GetComponent<RectTransform>();
            xtr.anchorMin = Vector2.zero;
            xtr.anchorMax = Vector2.one;
            xtr.sizeDelta = Vector2.zero;
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
            iconPreview = iconGO.AddComponent<Image>();
            iconPreview.preserveAspect = true;
            iconPreview.raycastTarget = false;
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

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = glyph;
            text.font = builtinFont;
            text.fontSize = 15;
            ThemeService.Tag(text, role == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt);
            text.alignment = TextAnchor.MiddleCenter;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
        }

        void BuildNotesRow(Transform t)
        {
            var rowGO = new GameObject("NotesRow");
            rowGO.transform.SetParent(t, false);
            var bg = rowGO.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(rowGO.transform, false);
            notesLabel = lblGO.AddComponent<Text>();
            notesLabel.text = "Заметки ещё не созданы";
            notesLabel.font = builtinFont;
            notesLabel.fontSize = 11;
            ThemeService.Tag(notesLabel, ThemeRole.Txt);
            notesLabel.alignment = TextAnchor.MiddleLeft;
            notesLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lr = lblGO.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(0.62f, 1f);
            lr.offsetMin = new Vector2(8f, 0f);
            lr.offsetMax = Vector2.zero;

            var openGO = new GameObject("Open");
            openGO.transform.SetParent(rowGO.transform, false);
            var openText = openGO.AddComponent<Text>();
            openText.text = "Открыть →";
            openText.font = builtinFont;
            openText.fontSize = 11;
            ThemeService.Tag(openText, ThemeRole.Accent);
            openText.alignment = TextAnchor.MiddleRight;
            var openBtn = openGO.AddComponent<Button>();
            openBtn.targetGraphic = openText;
            openBtn.onClick.AddListener(OnOpenPagesClicked);
            var orr = openGO.GetComponent<RectTransform>();
            orr.anchorMin = new Vector2(0.5f, 0f);
            orr.anchorMax = new Vector2(1f, 1f);
            orr.offsetMin = Vector2.zero;
            orr.offsetMax = new Vector2(-8f, 0f);
        }

        void BuildFooter(Transform t)
        {
            var rowGO = new GameObject("Footer");
            rowGO.transform.SetParent(t, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            var delGO = new GameObject("Delete");
            delGO.transform.SetParent(rowGO.transform, false);
            var delText = delGO.AddComponent<Text>();
            delText.text = "Удалить";
            delText.font = builtinFont;
            delText.fontSize = 12;
            ThemeService.Tag(delText, ThemeRole.Danger);
            delText.alignment = TextAnchor.MiddleLeft;
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delText;
            delBtn.onClick.AddListener(() =>
            {
                var sel = poiManager?.GetSelectedPoi();
                if (sel != null) poiManager.DeletePoi(sel.Id);
            });
            var dr = delGO.GetComponent<RectTransform>();
            dr.anchorMin = new Vector2(0f, 0f);
            dr.anchorMax = new Vector2(0.5f, 1f);
            dr.offsetMin = Vector2.zero;
            dr.offsetMax = Vector2.zero;

            var doneGO = new GameObject("Done");
            doneGO.transform.SetParent(rowGO.transform, false);
            var doneBg = doneGO.AddComponent<Image>();
            ThemeService.Tag(doneBg, ThemeRole.Accent);
            var doneBtn = doneGO.AddComponent<Button>();
            doneBtn.targetGraphic = doneBg;
            doneBtn.onClick.AddListener(() => poiManager?.DeselectAll());
            var dor = doneGO.GetComponent<RectTransform>();
            dor.anchorMin = new Vector2(0.6f, 0f);
            dor.anchorMax = new Vector2(1f, 1f);
            dor.offsetMin = Vector2.zero;
            dor.offsetMax = Vector2.zero;
            var doneTextGO = new GameObject("Text");
            doneTextGO.transform.SetParent(doneGO.transform, false);
            var doneText = doneTextGO.AddComponent<Text>();
            doneText.text = "Готово";
            doneText.font = builtinFont;
            doneText.fontSize = 12;
            ThemeService.Tag(doneText, ThemeRole.AccentInk);
            doneText.alignment = TextAnchor.MiddleCenter;
            var dtr = doneTextGO.GetComponent<RectTransform>();
            dtr.anchorMin = Vector2.zero;
            dtr.anchorMax = Vector2.one;
            dtr.sizeDelta = Vector2.zero;
        }

        void ResetIconToType()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null) return;
            poiManager.UpdatePoiIconBytes(sel.Id, null, null);
            UpdateIconPreview(sel);
        }

        void OnOpenPagesClicked()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null || notesRoot == null) return;

            var doc = notesRoot.DocumentController;
            var group = doc.FindGroupByPoiId(sel.Id);
            if (group == null)
            {
                group = doc.CreateGroup(sel.Name, sel.Id);
                doc.CreatePage(group.Id, "Страница 1");
            }

            doc.OpenPage(group.Pages[0].Id);
            UpdateNotesLabel(sel);
        }

        void OnPickIconClicked()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null) return;

            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать иконку", "", IconFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

            byte[] bytes = System.IO.File.ReadAllBytes(paths[0]);
            poiManager.UpdatePoiIconBytes(sel.Id, bytes, paths[0]);
            UpdateIconPreview(sel);
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

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = builtinFont;
            labelText.fontSize = 11;
            ThemeService.Tag(labelText, ThemeRole.Txt);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(68f, 20f);

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 0.25f;
            slider.maxValue = 4f;
            slider.value = defaultValue;
            sliderGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 20f);

            // Тонкий трек: полоса ~3px по центру строки, маленькая ручка — чтобы не «толстить» и не лезть на соседей.
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

            // Контейнер ручки занимает только среднюю полосу по высоте — Slider растягивает ручку
            // на высоту контейнера, поэтому так бегунок получается невысоким; ширину задаём малой.
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
            handleRect.sizeDelta = new Vector2(6f, 0f); // узкий бегунок; высота — по контейнеру ручки
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueText = valueGO.AddComponent<Text>();
            valueText.text = defaultValue.ToString("F2");
            valueText.font = builtinFont;
            valueText.fontSize = 11;
            ThemeService.Tag(valueText, ThemeRole.Txt);
            valueText.alignment = TextAnchor.MiddleRight;
            valueGO.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 20f);

            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = v.ToString("F2");
                onChanged(v);
            });

            return slider;
        }

        Text AddCaption(Transform parent, string text)
        {
            var go = new GameObject("Caption");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = builtinFont;
            label.fontSize = 10;
            label.fontStyle = FontStyle.Bold;
            ThemeService.Tag(label, ThemeRole.Mut);
            label.alignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = 13f;
            return label;
        }

        InputField BuildInputField(Transform parent, bool multiline)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.lineType = multiline
                ? InputField.LineType.MultiLineNewline
                : InputField.LineType.SingleLine;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 12;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phText = phGO.AddComponent<Text>();
            phText.font = builtinFont;
            phText.fontSize = 12;
            ThemeService.Tag(phText, ThemeRole.Mut);
            phText.fontStyle = FontStyle.Italic;
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 40f : 22f;
            le.flexibleWidth = 1f;
            return field;
        }
    }
}
