using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// 46px toolbar strip below the (40px) menu bar: tab segment (Карта/Редактор/Точки/Регионы) on the
    /// left, zoom controls on the right. Owns which of the four docked panels is active.
    /// </summary>
    public class MapToolbarUI : MonoBehaviour
    {
        public const float BarHeightPixels = 46f;

        [Header("Источники")]
        public MapCameraController cameraController;
        [Tooltip("Панели, докающиеся под тулбар - в порядке Карта/Редактор/Точки/Регионы.")]
        public GameObject mapLayersPanel;
        public GameObject editorBrushPanel;
        public GameObject poiToolPanel;
        public GameObject regionsPanel;

        Font builtinFont;
        Button[] tabButtons = new Button[4];
        Text zoomPercentLabel;
        int activeTab;
        GameObject barCanvasGO;   // the toolbar strip's own canvas (hidden/shown by SetChromeVisible)

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            SetActiveTab(0);
        }

        /// <summary>The four docked panels, in tab order (Карта/Редактор/Точки/Регионы) — the SAME array
        /// SetChromeVisible and SetActiveTab both walk, so the tab-index-to-panel mapping has exactly one
        /// definition. Exposed publicly for MapSurfaceHost: these are sibling GameObjects of this one,
        /// referenced by nothing else in the scene, so a component that needs to reach the map's full chrome
        /// (to confine it to a workspace pane — see MapSurfaceHost.EnsureFrames) can only get at them through
        /// the toolbar that owns them. Deliberately NOT folded into MapSurfaceHost's `chrome` array, which
        /// drives SetActive: activating all four at once and letting SetChromeVisible switch three back off
        /// is precisely the OnEnable-before-OnDisable interleaving SetActiveTab's own comment below
        /// (MapToolbarUI.cs:275) exists to prevent, since EditorBrushPanel/RegionsPanel share mutable
        /// BrushToolController state across those callbacks.
        ///
        /// IReadOnlyList over the cached array, not a fresh GameObject[] per get: returning the array itself
        /// would let a caller write through it and silently re-map the tab indices, while re-allocating on
        /// every get made the property look free when it was not. Built lazily in the getter rather than in
        /// Awake because a Play-mode domain reload wipes this plain field while the serialized panel
        /// references above survive — the lazy form recovers with no Awake re-entry, which matters because
        /// this class has no recompile guard of the kind WorkspaceBuilder/NotesRootBuilder carry.</summary>
        public IReadOnlyList<GameObject> DockedPanels =>
            dockedPanels ??= new[] { mapLayersPanel, editorBrushPanel, poiToolPanel, regionsPanel };

        GameObject[] dockedPanels;

        /// <summary>Show/hide the whole map-editor chrome this toolbar owns — its strip AND the docked
        /// tab panels (which are sibling GameObjects, so MapScreenController can't hide them directly).
        /// Called by MapScreenController so the toolbar + panels don't leak onto the generation/POI-editor
        /// screens. On show, the previously-active tab's panel is restored.</summary>
        public void SetChromeVisible(bool visible)
        {
            if (barCanvasGO != null) barCanvasGO.SetActive(visible);
            if (visible)
            {
                SetActiveTab(activeTab); // re-show the active panel, hide the rest
            }
            else
            {
                foreach (var p in DockedPanels)
                    if (p != null) p.SetActive(false);
            }
        }

        void Update()
        {
            if (cameraController != null && zoomPercentLabel != null)
                zoomPercentLabel.text = $"{Mathf.RoundToInt(cameraController.CurrentZoomPercent)}%";
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapToolbarCanvas");
            barCanvasGO = canvasGO;
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // above the map, below floating panels (which use higher orders elsewhere)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var barGO = new GameObject("ToolbarBar");
            barGO.transform.SetParent(canvasTransform, false);
            var barImg = barGO.AddComponent<Image>();
            ThemeService.Tag(barImg, ThemeRole.Panel);
            var barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            // Flush with the TOP of whatever this canvas is confined to. That used to be the window, and the
            // -40f this line carried dodged the 40px menu bar drawn above it. The workspace shell now hosts
            // this canvas inside a pane (PaneChromeFrame, driven from MapSurfaceHost), and the rect it is
            // driven to — the pane's ContentArea — is already below the menu bar AND below the tab strip, so
            // keeping the dodge double-counted 40 pixels and showed as a gap under the tab strip. Full
            // reasoning, shared by all six sites that dropped this term: MapLayersPanel.cs:74.
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, BarHeightPixels);

            BuildTabSegment(barGO.transform);
            BuildZoomControls(barGO.transform);
        }

        void BuildTabSegment(Transform parent)
        {
            var containerGO = new GameObject("TabSegment");
            containerGO.transform.SetParent(parent, false);
            var containerImg = containerGO.AddComponent<Image>();
            ThemeService.Tag(containerImg, ThemeRole.Bg);
            var containerRect = containerGO.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 0.5f);
            containerRect.anchorMax = new Vector2(0f, 0.5f);
            containerRect.pivot = new Vector2(0f, 0.5f);
            containerRect.anchoredPosition = new Vector2(12f, 0f);
            containerRect.sizeDelta = new Vector2(320f, 34f); // widened from 240f (3 tabs) to keep per-tab width when Регионы was added

            var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            string[] labels = { "Карта", "Редактор", "Точки", "Регионы" };
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var btnGO = new GameObject($"Tab_{labels[i]}");
                btnGO.transform.SetParent(containerGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActiveTab(captured));
                tabButtons[i] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = labels[i];
                text.font = builtinFont;
                text.fontSize = 13;
                text.alignment = TextAnchor.MiddleCenter;
                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }
        }

        void BuildZoomControls(Transform parent)
        {
            var rowGO = new GameObject("ZoomControls");
            rowGO.transform.SetParent(parent, false);
            var rowRect = rowGO.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(1f, 0.5f);
            rowRect.anchorMax = new Vector2(1f, 0.5f);
            rowRect.pivot = new Vector2(1f, 0.5f);
            rowRect.anchoredPosition = new Vector2(-12f, 0f);
            rowRect.sizeDelta = new Vector2(220f, 34f);

            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            // childControlWidth=true is required for each button's LayoutElement.preferredWidth to
            // take effect; with it false the widths were ignored and "+"/"По размеру" got pushed
            // off the right edge. forceExpand=false keeps them at their preferred size.
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleRight;

            // "-" zooms out (sees more, orthographicSize grows -> multiplier > 1), "+" zooms in
            // (sees less/magnified, orthographicSize shrinks -> multiplier < 1). See
            // MapCameraController.ZoomBy's doc comment.
            AddIconButton(rowGO.transform, "−", () => cameraController?.ZoomBy(cameraController.buttonZoomStep));
            zoomPercentLabel = AddZoomLabel(rowGO.transform, () => cameraController?.ResetZoom());
            AddIconButton(rowGO.transform, "+", () => cameraController?.ZoomBy(1f / cameraController.buttonZoomStep));
            AddFitButton(rowGO.transform, () => cameraController?.ResetZoom());
        }

        void AddIconButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"ZoomBtn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 30f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        Text AddZoomLabel(Transform parent, System.Action onClick)
        {
            var go = new GameObject("ZoomPercent");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 50f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "100%";
            text.font = builtinFont;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            return text;
        }

        void AddFitButton(Transform parent, System.Action onClick)
        {
            var go = new GameObject("FitButton");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredWidth = 90f;
            go.GetComponent<LayoutElement>().preferredHeight = 30f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "По размеру";
            text.font = builtinFont;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        public void SetActiveTab(int index)
        {
            activeTab = index;

            // Deactivate ALL panels first, then activate only the target, so the target panel's
            // OnEnable always runs last. Several panels (EditorBrushPanel, RegionsPanel) share
            // mutable state on BrushToolController via OnEnable/OnDisable; fixed source-order
            // activation could run the new tab's OnEnable before the old tab's OnDisable, letting
            // the old tab's teardown clobber the state the new tab just set. Deactivate-all-then-
            // activate-target makes "the newly-activated panel wins" true regardless of tab order.
            var panels = DockedPanels;
            foreach (var panel in panels)
                if (panel != null) panel.SetActive(false);
            GameObject target = index >= 0 && index < panels.Count ? panels[index] : null;
            if (target != null) target.SetActive(true);

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                bool active = i == index;
                ThemeService.Tag(tabButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Bg);
                var label = tabButtons[i].GetComponentInChildren<Text>();
                ThemeService.Tag(label, active ? ThemeRole.AccentInk : ThemeRole.Mut);
            }
        }
    }
}
