# Main Screen Redesign (Shell) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Main screen shell — 40px menu bar, a new 46px toolbar strip (tabs + real map zoom/pan), the Layers panel, and a repositioned Legend — per `docs/superpowers/specs/2026-07-06-main-screen-redesign-design.md`, without touching the Editor-brush/POI/Modals content (those are separate specs/plans).

**Architecture:** `MapEditorPanel.cs`'s three combined tabs get split into three standalone panel classes (`MapLayersPanel`, `EditorBrushPanel`, `PoiToolPanel` — the latter two are *mechanical, unchanged* extractions in this plan; Screens C/D will rewrite their internals later). A new `MapToolbarUI` owns tab-switching (toggling the three panels' `SetActive`) and zoom controls. A new `MapCameraController` does real orthographic zoom/pan on the map's camera. `ProjectMenuBar` grows to 40px with a logo/wordmark/placeholder items/project name.

**Tech Stack:** C# runtime `UnityEngine.UI` (`new GameObject()` + `AddComponent<Image>/<Text>/<Button>`, `HorizontalLayoutGroup`/`VerticalLayoutGroup`, `LegacyRuntime.ttf`), new Input System (`UnityEngine.InputSystem`), `ThemeService.Tag(...)` for all colors.

## Global Constraints

- No prefabs, no UI Toolkit/UXML — runtime code only, matching every existing file in `Assets/WorldGen/Rendering/`.
- Every `Image`/`Text` gets colored via `ThemeService.Tag(graphic, ThemeRole.X)` immediately after construction — never a plain `.color =` assignment, never a serialized field default (see the project's `unity-canvas-recttransform-gotcha`/serialization-gotcha history: a `Color` field initialized from `ThemeService.Get(...)` gets silently overwritten by Unity scene deserialization on a pre-existing scene component).
- Zoom/pan state is session-only — never persisted to `.dndproj`, resets to fit-to-map on regenerate/load.
- "Правка"/"Вид" menu items are inert (`Text` only, no `Button` component) — no click behavior.
- Legend width fixed at 232px, anchored bottom-left (not top-right).
- Camera pan gesture is **right-mouse-drag** (confirmed) — must not collide with existing left-drag gestures (`CellSelectionController`, `BrushToolController`, `PoiInteractionController` all already claim left-drag).
- `WorldMapRenderer.PositionCameraOverMap()` must only run on first placement per session (guard flag) — never stomp an in-progress zoom/pan on regenerate/load.
- No automated test runner in this project — testing is `[ContextMenu("Self-Test: ...")]` methods plus manual Play-mode verification (established convention, confirmed across every prior plan this session).

---

### Task 1: `MapCameraController` — real zoom + pan

**Files:**
- Create: `Assets/WorldGen/Rendering/MapCameraController.cs`
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs:661-669` (`PositionCameraOverMap`, add a guard)

**Interfaces:**
- Produces: `MapCameraController` public API — `float NaturalFitSize { get; }`, `float CurrentZoomPercent { get; }` (100 = natural fit), `void ZoomBy(float multiplier)`, `void ResetZoom()`, all consumed by Task 2 (`MapToolbarUI`).
- Consumes: `WorldMapRenderer.targetCamera` (existing public field, `WorldMapRenderer.cs:107`), `WorldMapRenderer.mapWidth`/`mapHeight` (existing public fields, used to compute natural fit size the same way `PositionCameraOverMap` does).

- [ ] **Step 1: Guard `PositionCameraOverMap` against repeat calls**

Open `Assets/WorldGen/Rendering/WorldMapRenderer.cs`. Add a field near the other private fields (e.g. next to `meshFilter`/`meshRenderer` around line 109-110):

```csharp
bool cameraPlacedOnce;
```

Replace the body of `PositionCameraOverMap()` (`WorldMapRenderer.cs:661-669`) with:

```csharp
/// <summary>Ставит назначенную камеру по центру карты сверху вниз - но только один раз за
/// сессию, чтобы не сбрасывать пользовательский зум/пан при повторной генерации/загрузке
/// (см. MapCameraController).</summary>
void PositionCameraOverMap()
{
    if (cameraPlacedOnce) return;
    cameraPlacedOnce = true;

    float maxSide = Mathf.Max(mapWidth, mapHeight);
    targetCamera.transform.position = new Vector3(mapWidth * 0.5f, maxSide * 1.5f, mapHeight * 0.5f);
    targetCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

    if (targetCamera.farClipPlane < maxSide * 3f)
        targetCamera.farClipPlane = maxSide * 3f;
}
```

- [ ] **Step 2: Create `MapCameraController.cs`**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Real orthographic zoom (scroll wheel + MapToolbarUI buttons) and pan (right-mouse-drag)
    /// for the map's Camera. Session-only state - never persisted, resets to fit-to-map on
    /// WorldMapRenderer.PositionCameraOverMap's one-time initial placement.
    /// </summary>
    public class MapCameraController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public Camera targetCamera;

        [Header("Настройки зума")]
        [Tooltip("Минимальный orthographicSize (максимальное приближение), доля от naturalFitSize.")]
        public float minSizeFraction = 0.15f;
        [Tooltip("Множитель за одно нажатие кнопки +/- в тулбаре.")]
        public float buttonZoomStep = 1.15f;
        [Tooltip("Чувствительность зума колесом мыши.")]
        public float scrollZoomSensitivity = 0.001f;

        [Header("Настройки пана")]
        [Tooltip("Множитель скорости пана относительно текущего orthographicSize.")]
        public float panSensitivity = 1.0f;
        [Tooltip("Насколько за пределы карты (в тех же мировых единицах) можно панить.")]
        public float panMargin = 50f;

        float naturalFitSize = -1f;
        Vector3 naturalFitPosition;
        bool dragging;
        Vector2 lastMousePos;

        public float NaturalFitSize
        {
            get
            {
                EnsureNaturalFitComputed();
                return naturalFitSize;
            }
        }

        public float CurrentZoomPercent
        {
            get
            {
                EnsureNaturalFitComputed();
                if (targetCamera == null || naturalFitSize <= 0f) return 100f;
                return naturalFitSize / targetCamera.orthographicSize * 100f;
            }
        }

        void EnsureNaturalFitComputed()
        {
            if (naturalFitSize > 0f || mapRenderer == null) return;
            naturalFitSize = Mathf.Max(mapRenderer.mapWidth, mapRenderer.mapHeight) * 0.5f;
            if (targetCamera != null) naturalFitPosition = targetCamera.transform.position;
        }

        void Update()
        {
            if (targetCamera == null) return;
            EnsureNaturalFitComputed();

            HandleScrollZoom();
            HandleRightMouseDragPan();
        }

        void HandleScrollZoom()
        {
            if (Mouse.current == null) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;
            ApplyZoomDelta(-scroll * scrollZoomSensitivity * targetCamera.orthographicSize);
        }

        void ApplyZoomDelta(float sizeDelta)
        {
            float minSize = naturalFitSize * minSizeFraction;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize + sizeDelta, minSize, naturalFitSize);
        }

        /// <summary>Called by MapToolbarUI's "-"/"+" buttons. Positive multiplier > 1 zooms out, &lt; 1 zooms in.</summary>
        public void ZoomBy(float multiplier)
        {
            EnsureNaturalFitComputed();
            float minSize = naturalFitSize * minSizeFraction;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * multiplier, minSize, naturalFitSize);
        }

        /// <summary>Called by MapToolbarUI's "100%"/"По размеру" buttons.</summary>
        public void ResetZoom()
        {
            EnsureNaturalFitComputed();
            targetCamera.orthographicSize = naturalFitSize;
            targetCamera.transform.position = naturalFitPosition;
        }

        void HandleRightMouseDragPan()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                dragging = true;
                lastMousePos = Mouse.current.position.ReadValue();
                return;
            }
            if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                dragging = false;
                return;
            }
            if (!dragging) return;

            Vector2 currentMousePos = Mouse.current.position.ReadValue();
            Vector2 delta = currentMousePos - lastMousePos;
            lastMousePos = currentMousePos;

            // Camera looks straight down (Euler(90,0,0)) - screen X maps to world X, screen Y maps to world Z (inverted).
            float worldPerPixel = (targetCamera.orthographicSize * 2f / Screen.height) * panSensitivity;
            Vector3 move = new Vector3(-delta.x * worldPerPixel, 0f, -delta.y * worldPerPixel);

            Vector3 newPos = targetCamera.transform.position + move;
            float minX = -panMargin, maxX = mapRenderer.mapWidth + panMargin;
            float minZ = -panMargin, maxZ = mapRenderer.mapHeight + panMargin;
            newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
            newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);
            targetCamera.transform.position = newPos;
        }

        [ContextMenu("Self-Test: Zoom Clamp")]
        public void SelfTestZoomClamp()
        {
            EnsureNaturalFitComputed();
            float before = targetCamera.orthographicSize;

            targetCamera.orthographicSize = naturalFitSize * 10f; // way too big
            ApplyZoomDelta(0f); // triggers clamp via ZoomBy path instead
            ZoomBy(1f); // re-clamps at current (still-too-big) value
            bool clampedHigh = targetCamera.orthographicSize <= naturalFitSize + 0.001f;

            targetCamera.orthographicSize = 0.0001f; // way too small
            ZoomBy(1f);
            bool clampedLow = targetCamera.orthographicSize >= naturalFitSize * minSizeFraction - 0.001f;

            targetCamera.orthographicSize = before;
            Debug.Log(clampedHigh && clampedLow
                ? "Self-Test Zoom Clamp: PASS"
                : $"Self-Test Zoom Clamp: FAIL (clampedHigh={clampedHigh}, clampedLow={clampedLow})");
        }
    }
}
```

- [ ] **Step 3: Manual verification (no automated runner in this project)**

In the Unity Editor, enter Play mode with a generated map, select the `MapCameraController` component in the Inspector, run "Self-Test: Zoom Clamp" via its context menu — expect `PASS` in the Console. Then manually: scroll wheel zooms in/out and stops at the clamps; right-mouse-drag pans without triggering cell selection/brush/POI-drag; regenerating the map does not reset an in-progress zoom/pan.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapCameraController.cs Assets/WorldGen/Rendering/MapCameraController.cs.meta Assets/WorldGen/Rendering/WorldMapRenderer.cs
git commit -m "feat: add real map camera zoom (scroll) and pan (right-drag)"
```

(The `.meta` file is generated by the Unity Editor on first import — if it doesn't exist yet when you run `git add`, open the Editor once to let it import the new script, then re-run `git add`.)

---

### Task 2: `MapToolbarUI` — tab segment + zoom controls

**Files:**
- Create: `Assets/WorldGen/Rendering/MapToolbarUI.cs`

**Interfaces:**
- Consumes: `MapCameraController.ZoomBy(float)`, `.ResetZoom()`, `.CurrentZoomPercent` (Task 1). `MapLayersPanel`, `EditorBrushPanel`, `PoiToolPanel` GameObjects (Tasks 3-4, Inspector-assigned).
- Produces: `public void SetActiveTab(int index)` — 0 = Карта, 1 = Редактор, 2 = Точки. Consumed by Screens C/D's future work if they need to programmatically switch tabs (not required by this plan).

- [ ] **Step 1: Create `MapToolbarUI.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// 46px toolbar strip below the (40px) menu bar: tab segment (Карта/Редактор/Точки) on the
    /// left, zoom controls on the right. Owns which of the three docked panels is active.
    /// </summary>
    public class MapToolbarUI : MonoBehaviour
    {
        public const float BarHeightPixels = 46f;

        [Header("Источники")]
        public MapCameraController cameraController;
        [Tooltip("Панели, докающиеся под тулбар - в порядке Карта/Редактор/Точки.")]
        public GameObject mapLayersPanel;
        public GameObject editorBrushPanel;
        public GameObject poiToolPanel;

        Font builtinFont;
        Button[] tabButtons = new Button[3];
        Text zoomPercentLabel;
        int activeTab;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            SetActiveTab(0);
        }

        void Update()
        {
            if (cameraController != null && zoomPercentLabel != null)
                zoomPercentLabel.text = $"{Mathf.RoundToInt(cameraController.CurrentZoomPercent)}%";
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapToolbarCanvas");
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
            barRect.anchoredPosition = new Vector2(0f, -40f); // sits directly below the 40px menu bar
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
            containerRect.sizeDelta = new Vector2(240f, 34f);

            var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            string[] labels = { "Карта", "Редактор", "Точки" };
            for (int i = 0; i < 3; i++)
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
            var rowRect = rowGO.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(1f, 0.5f);
            rowRect.anchorMax = new Vector2(1f, 0.5f);
            rowRect.pivot = new Vector2(1f, 0.5f);
            rowRect.anchoredPosition = new Vector2(-12f, 0f);
            rowRect.sizeDelta = new Vector2(220f, 34f);

            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = false;
            layout.childAlignment = TextAnchor.MiddleRight;

            AddIconButton(rowGO.transform, "−", () => cameraController?.ZoomBy(1f / 1.15f));
            zoomPercentLabel = AddZoomLabel(rowGO.transform, () => cameraController?.ResetZoom());
            AddIconButton(rowGO.transform, "+", () => cameraController?.ZoomBy(1.15f));
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
            if (mapLayersPanel != null) mapLayersPanel.SetActive(index == 0);
            if (editorBrushPanel != null) editorBrushPanel.SetActive(index == 1);
            if (poiToolPanel != null) poiToolPanel.SetActive(index == 2);

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
```

- [ ] **Step 2: Manual verification**

Deferred to Task 5 (scene wiring), since this component needs `mapLayersPanel`/`editorBrushPanel`/`poiToolPanel` references assigned before tab-switching is testable.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Rendering/MapToolbarUI.cs Assets/WorldGen/Rendering/MapToolbarUI.cs.meta
git commit -m "feat: add MapToolbarUI (tab segment + zoom controls)"
```

---

### Task 3: `MapLayersPanel` — extracted Карта tab

**Files:**
- Create: `Assets/WorldGen/Rendering/MapLayersPanel.cs`

**Interfaces:**
- Consumes: `WorldMapRenderer.SetShowReliefLayer(bool)`, `.SetShowBiomeLayer(bool)`, `.SetShowRegionBordersLayer(bool)`, `.SetShowCoastlineLayer(bool)` (existing methods, called today from `MapEditorPanel.BuildMapTab` — confirm the exact method names by opening `MapEditorPanel.cs:336-343` before writing this task's code, since the plan text names them from an earlier code-reading pass and must match exactly).

- [ ] **Step 1: Read the exact current Карта-tab code**

Open `Assets/WorldGen/Rendering/MapEditorPanel.cs`, read lines 336-343 (`BuildMapTab`) and lines 557-570 (`AddLayerToggleRow`) in full — copy their exact content, since this task recreates them verbatim in a new standalone panel.

- [ ] **Step 2: Create `MapLayersPanel.cs`**

Build the file with this shape (fill the `BuildUI`/`AddLayerToggleRow` bodies with the *exact* code copied in Step 1 — same toggle labels, same `WorldMapRenderer` setter calls, same default states — only the surrounding class/positioning is new):

```csharp
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// "Карта" tab content - layer visibility toggles. Extracted unchanged from
    /// MapEditorPanel.BuildMapTab (this project's Main-screen shell redesign,
    /// 2026-07-06) so MapToolbarUI has a standalone panel to dock/undock per tab.
    /// </summary>
    public class MapLayersPanel : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("MapLayersCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var panelGO = new GameObject("LayersPanel");
            panelGO.transform.SetParent(canvasTransform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel, 0.9f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f - MapToolbarUI.BarHeightPixels - 40f); // below 40px menu + 46px toolbar
            panelRect.sizeDelta = new Vector2(216f, 0f);

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // <<< Paste the exact body of MapEditorPanel.BuildMapTab (MapEditorPanel.cs:336-343)
            //     here, replacing `t` (the old tab-content transform) with `panelGO.transform`,
            //     and paste AddLayerToggleRow (MapEditorPanel.cs:557-570) as a method on this
            //     class unchanged. Do not alter labels, default states, or which
            //     WorldMapRenderer setter each row calls. >>>
        }
    }
}
```

- [ ] **Step 3: Manual verification**

Deferred to Task 5 (scene wiring) — panel needs to be parented under the toolbar's tab-switching before it's independently viewable.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/MapLayersPanel.cs Assets/WorldGen/Rendering/MapLayersPanel.cs.meta
git commit -m "feat: extract Карта tab into standalone MapLayersPanel"
```

---

### Task 4: `EditorBrushPanel` and `PoiToolPanel` — mechanical extraction (unchanged content)

**Files:**
- Create: `Assets/WorldGen/Rendering/EditorBrushPanel.cs`
- Create: `Assets/WorldGen/Rendering/PoiToolPanel.cs`

**Interfaces:**
- Produces: both classes expose whatever public fields `MapEditorPanel` currently exposes for these tabs' dependencies (`selectionController`, `brushController`, `poiManager` — confirm exact field names from `MapEditorPanel.cs:20-24` before writing). Screen C's plan will later gut and rebuild `EditorBrushPanel`'s internals; Screen D's plan will do the same for `PoiToolPanel` — this task must not anticipate or duplicate that future work, only preserve current behavior.

- [ ] **Step 1: Read the exact current Редактор/Точки-tab code**

Open `Assets/WorldGen/Rendering/MapEditorPanel.cs`, read lines 345-555 (`BuildEditorTab` through `BuildBrushSection`, including `selectionPanelRoot`/`brushPanelRoot`, `AddModeButton` at L574-597, `BuildSelectionOverrideSection` at L445-470, `BuildBrushSection` at L472-555) and lines 383-435 (`BuildPoiTab`) in full.

- [ ] **Step 2: Create `EditorBrushPanel.cs`**

Same shell pattern as `MapLayersPanel` (Task 3) but width 264px, anchored at the same top-left docking position. Paste `BuildEditorTab`'s exact body (including the `EditorMode` enum, `selectionPanelRoot`/`brushPanelRoot` construction, `AddModeButton`, `BuildSelectionOverrideSection`, `BuildBrushSection`, and every field/slider/dropdown they wire) into this new class, unchanged. Class docstring:

```csharp
/// <summary>
/// "Редактор" tab content - extracted unchanged from MapEditorPanel.BuildEditorTab (Main-screen
/// shell redesign, 2026-07-06). Functionally identical to the pre-redesign panel; Screen C's
/// spec ("Editor-Brush Panel Redesign") will replace this class's internals with the real
/// radius-brush design - do not add radius/shape/biome-target logic here.
/// </summary>
```

- [ ] **Step 3: Create `PoiToolPanel.cs`**

Same shell pattern, width matching the old panel's 300px (mockup's 262px POI-list width doesn't apply here — that's Screen D's new list panel, not this extraction). Paste `BuildPoiTab`'s exact body (count spinner, 3 bulk buttons, hint label) unchanged. Class docstring:

```csharp
/// <summary>
/// "Точки" tab content - extracted unchanged from MapEditorPanel.BuildPoiTab (Main-screen shell
/// redesign, 2026-07-06). Functionally identical to the pre-redesign panel; Screen D's spec
/// ("POI Screen Redesign") will replace this class's internals with the real list+search+filter
/// design - do not add list/search/filter logic here.
/// </summary>
```

- [ ] **Step 4: Manual verification**

Deferred to Task 5 (scene wiring).

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/EditorBrushPanel.cs Assets/WorldGen/Rendering/EditorBrushPanel.cs.meta Assets/WorldGen/Rendering/PoiToolPanel.cs Assets/WorldGen/Rendering/PoiToolPanel.cs.meta
git commit -m "feat: extract Редактор/Точки tabs into standalone panels (unchanged content)"
```

---

### Task 5: Menu bar growth, Legend reposition, scene wiring, retire `MapEditorPanel`

**Files:**
- Modify: `Assets/WorldGen/Rendering/ProjectMenuBar.cs` (height 20→40, logo/wordmark, Правка/Вид, project name)
- Modify: `Assets/WorldGen/Rendering/MapLegendUI.cs` (reposition top-right → bottom-left)
- Delete: `Assets/WorldGen/Rendering/MapEditorPanel.cs` (and its `.meta`)
- Modify: `Assets/Scenes/SampleScene.unity` (via a temporary batchmode Editor script, per this project's established convention)

**Interfaces:**
- Consumes: everything produced by Tasks 1-4 (`MapCameraController`, `MapToolbarUI`, `MapLayersPanel`, `EditorBrushPanel`, `PoiToolPanel`).

- [ ] **Step 1: Grow `ProjectMenuBar.cs` to 40px with logo/wordmark/placeholders/project name**

Open `Assets/WorldGen/Rendering/ProjectMenuBar.cs`. Change `BarHeightPixels` (line 27) from `20f` to `40f`. Before the existing "Файл" button construction (around line 171), insert:

```csharp
// Logo square
var logoGO = new GameObject("Logo");
logoGO.transform.SetParent(barGO.transform, false); // barGO = existing MenuBar Image GameObject
var logoImg = logoGO.AddComponent<Image>();
ThemeService.Tag(logoImg, ThemeRole.Accent);
var logoRect = logoGO.GetComponent<RectTransform>();
logoRect.anchorMin = new Vector2(0f, 0.5f);
logoRect.anchorMax = new Vector2(0f, 0.5f);
logoRect.pivot = new Vector2(0f, 0.5f);
logoRect.anchoredPosition = new Vector2(12f, 0f);
logoRect.sizeDelta = new Vector2(16f, 16f);

// Wordmark
var wordmarkGO = new GameObject("Wordmark");
wordmarkGO.transform.SetParent(barGO.transform, false);
var wordmarkText = wordmarkGO.AddComponent<Text>();
wordmarkText.text = "REALMWEAVER";
wordmarkText.font = builtinFont;
wordmarkText.fontSize = 13;
wordmarkText.fontStyle = FontStyle.Bold;
wordmarkText.alignment = TextAnchor.MiddleLeft;
ThemeService.Tag(wordmarkText, ThemeRole.Txt);
var wordmarkRect = wordmarkGO.GetComponent<RectTransform>();
wordmarkRect.anchorMin = new Vector2(0f, 0.5f);
wordmarkRect.anchorMax = new Vector2(0f, 0.5f);
wordmarkRect.pivot = new Vector2(0f, 0.5f);
wordmarkRect.anchoredPosition = new Vector2(36f, 0f);
wordmarkRect.sizeDelta = new Vector2(140f, 20f);
```

After the existing "Файл" button, insert the two inert placeholders and the right-aligned project name:

```csharp
AddInertMenuLabel(barGO.transform, "Правка", xOffset: 190f);
AddInertMenuLabel(barGO.transform, "Вид", xOffset: 250f);

var projectNameGO = new GameObject("ProjectName");
projectNameGO.transform.SetParent(barGO.transform, false);
projectNameText = projectNameGO.AddComponent<Text>();
projectNameText.font = builtinFont;
projectNameText.fontSize = 12;
projectNameText.alignment = TextAnchor.MiddleRight;
ThemeService.Tag(projectNameText, ThemeRole.Mut);
var projectNameRect = projectNameGO.GetComponent<RectTransform>();
projectNameRect.anchorMin = new Vector2(1f, 0.5f);
projectNameRect.anchorMax = new Vector2(1f, 0.5f);
projectNameRect.pivot = new Vector2(1f, 0.5f);
projectNameRect.anchoredPosition = new Vector2(-12f, 0f);
projectNameRect.sizeDelta = new Vector2(220f, 20f);
UpdateProjectNameText(); // new small helper, see below
```

Add the helper method and field:

```csharp
Text projectNameText;

void AddInertMenuLabel(Transform parent, string label, float xOffset)
{
    var go = new GameObject($"Menu_{label}");
    go.transform.SetParent(parent, false);
    var text = go.AddComponent<Text>();
    text.text = label;
    text.font = builtinFont;
    text.fontSize = 13;
    text.alignment = TextAnchor.MiddleLeft;
    ThemeService.Tag(text, ThemeRole.Mut);
    var rect = go.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0f, 0.5f);
    rect.anchorMax = new Vector2(0f, 0.5f);
    rect.pivot = new Vector2(0f, 0.5f);
    rect.anchoredPosition = new Vector2(xOffset, 0f);
    rect.sizeDelta = new Vector2(50f, 20f);
}

void UpdateProjectNameText()
{
    if (projectNameText == null) return;
    projectNameText.text = string.IsNullOrEmpty(currentProjectPath)
        ? "Проект не сохранён"
        : System.IO.Path.GetFileName(currentProjectPath);
}
```

Find the existing field that tracks the loaded/saved project's path (used by Save/Save As — confirm its exact name, e.g. `currentProjectPath`, by reading `ProjectMenuBar.cs`'s fields and `DoSave`/`DoSaveAs`/`LoadFrom` methods) and call `UpdateProjectNameText()` everywhere that field is assigned (after save, after save-as, after load, and after "new project"/generate if such a reset exists).

- [ ] **Step 2: Reposition `MapLegendUI.cs` to bottom-left**

Open `Assets/WorldGen/Rendering/MapLegendUI.cs`, find the anchor-setting code for the legend panel (per the earlier code exploration, comment references "top-right" placement). Change `anchorMin`/`anchorMax`/`pivot`/`anchoredPosition` from top-right to bottom-left equivalents (e.g. `anchorMin = anchorMax = new Vector2(0f, 0f)`, `pivot = new Vector2(0f, 0f)`, `anchoredPosition = new Vector2(20f, 20f)`), and confirm/set its width to `232f` (per the spec). Leave every other field/behavior untouched.

- [ ] **Step 3: Scene wiring via temporary batchmode Editor script**

Following this project's established convention (see `roadmap` memory's note on `TempSceneBootstrap_MapScreens.cs`): create a temporary `Assets/Editor/TempSceneBootstrap_MainScreenShell.cs` `MenuItem`-driven or `[InitializeOnLoad]`-driven script that, run once via the Unity Editor in batchmode:
1. Removes the old `MapEditorPanel` GameObject from `SampleScene.unity` (or disables+leaves it — prefer removal since the class is being deleted).
2. Adds new root GameObjects for `MapToolbarUI`, `MapLayersPanel`, `EditorBrushPanel`, `PoiToolPanel`, `MapCameraController`, wiring their public fields:
   - `MapToolbarUI.cameraController` → the new `MapCameraController` component.
   - `MapToolbarUI.mapLayersPanel/editorBrushPanel/poiToolPanel` → the three new panels' GameObjects.
   - `MapCameraController.mapRenderer`/`targetCamera` → the existing `WorldMapRenderer` and its camera (same camera already wired as `targetCamera`/`raycastCamera`/`mapCamera` elsewhere in the scene).
   - `MapLayersPanel.mapRenderer`, `EditorBrushPanel`'s equivalents of `MapEditorPanel`'s old `selectionController`/`brushController`/`poiManager` fields, `PoiToolPanel`'s `poiManager` field — all pointing at the same existing scene objects `MapEditorPanel` used to reference.
3. Delete the temporary script after running it (matches established convention — it must not remain in the committed tree).

Run this via `Unity.exe -batchmode -projectPath "D:\D&D" -executeMethod <YourStaticMethod> -quit -logFile -` (or through an interactive Editor session if the user has one open — check before invoking batchmode, since a second Editor instance/batchmode run against an already-open project can conflict).

- [ ] **Step 4: Delete `MapEditorPanel.cs`**

```bash
git rm Assets/WorldGen/Rendering/MapEditorPanel.cs Assets/WorldGen/Rendering/MapEditorPanel.cs.meta
```

- [ ] **Step 5: Verify compile and scene state**

```bash
git status --porcelain
```

Confirm `Assets/Scenes/SampleScene.unity` shows as modified (scene wiring took effect) and no stray temporary Editor script remains untracked. Run a batchmode compile check if no interactive Editor session is open:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -projectPath "D:\D&D" -quit -logFile -
```

Expect exit code 0 and no `error CS` lines in the log output.

- [ ] **Step 6: Manual Play-mode verification**

Per the spec's Testing section: scroll-zoom and toolbar buttons zoom correctly; "100%"/"По размеру" reset the view; right-mouse-drag pans without breaking cell click/brush/POI interactions; regenerating/loading a map does not reset zoom/pan; switching Карта/Редактор/Точки tabs shows the correct panel; Legend renders bottom-left; menu bar shows logo/wordmark/inert Правка/Вид/project name.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: grow menu bar to 40px, reposition Legend bottom-left, wire new toolbar/panels into scene, retire MapEditorPanel"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1 covers zoom/pan + `PositionCameraOverMap` guard; Task 2 covers the toolbar shell; Tasks 3-4 cover panel extraction (Layers real, Editor/POI mechanical placeholders for Screens C/D); Task 5 covers menu bar growth, Legend reposition, and scene wiring. All in-scope items from the design spec are represented.
- **Placeholder scan:** Task 3/4's "paste the exact body of X" instructions are not placeholders in the forbidden sense (they don't hide new logic — they defer transcription of *already-existing, unchanged* code to avoid multi-thousand-line duplication in this plan document; the exact source line ranges are given so there's no ambiguity about what to copy).
- **Type consistency:** `MapCameraController.ZoomBy`/`ResetZoom`/`CurrentZoomPercent` names used in Task 2 match Task 1's definitions exactly. `MapToolbarUI.SetActiveTab` signature matches its own definition and is not called by name-mismatch anywhere else in this plan.
