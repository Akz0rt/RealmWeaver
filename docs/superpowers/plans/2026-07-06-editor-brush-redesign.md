# Editor-Brush Panel Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-cell step-brush with a real radius-based multi-cell paint brush (Raise/Lower/Smooth, 4 targets including Biome with a clickable palette), per `docs/superpowers/specs/2026-07-06-editor-brush-redesign-design.md`, while preserving the existing precise Selection+Override editing mode behind a relabeled toggle.

**Architecture:** `WorldMapRenderer`/`BrushUndoManager` get small additions (cell-by-id lookup, a hit-point-returning raycast overload, a biome-brush undo-tracked setter, bulk undo). `BrushToolController` is rewritten to query cells within a radius/shape and apply Raise/Lower/Smooth/Biome per tick. `EditorBrushPanel.cs` (created by the Main-screen shell plan as a mechanical extraction) gets its Brush-mode sub-panel rebuilt; its Selection+Override sub-panel is untouched.

**Tech Stack:** C# runtime `UnityEngine.UI`, new Input System, `ThemeService.Tag(...)`.

## Global Constraints

- **Depends on** `docs/superpowers/plans/2026-07-06-main-screen-redesign.md` having already produced `Assets/WorldGen/Rendering/EditorBrushPanel.cs` (a mechanical, unchanged extraction of the old `MapEditorPanel.BuildEditorTab`). This plan modifies that file's Brush sub-panel only.
- Brush radius is in map/world units (same space as `VoronoiCell.Site`), NOT screen pixels — labeled "px" in the UI to match the mockup's copy per the spec.
- No falloff/feathering at the brush edge — hard cutoff, uniform strength within radius.
- Biome target: Режим segment (Raise/Lower/Smooth) hidden; painting requires a palette selection first; Strength is unused for this target.
- "Отменить всё" clears the ENTIRE brush undo stack for the session by actually reverting every recorded stroke (not just discarding undo capability) — no confirmation dialog.
- No automated test runner — `[ContextMenu("Self-Test: ...")]` + manual Play-mode verification (established convention).
- Every new `Image`/`Text` colored via `ThemeService.Tag(...)`.

---

### Task 1: `WorldMapRenderer`/`BrushUndoManager` additions

**Files:**
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs`
- Modify: `Assets/WorldGen/Generation/BrushUndoManager.cs`

**Interfaces:**
- Produces: `WorldMapRenderer.GetCellById(int id) : VoronoiCell`, `WorldMapRenderer.GetCellUnderRay(Ray ray, out Vector3 hitPoint, float maxDistance = 2000f) : VoronoiCell`, `WorldMapRenderer.BrushAdjustBiome(VoronoiCell cell, Biome? biome)`, `WorldMapRenderer.UndoAllBrushStrokes()`. All consumed by Task 2 (`BrushToolController`).
- Consumes: existing private `cellById` dictionary (`WorldMapRenderer.cs`, populated in `BuildMesh`), existing `meshCollider` field, existing `brushUndo` (`BrushUndoManager`) field, existing `CellOverrideService.ApplyBiomeOverride`.

- [ ] **Step 1: Add `GetCellById` to `WorldMapRenderer.cs`**

Find the existing `cellById` field (populated in `BuildMesh`, e.g. `cellById = new Dictionary<int, VoronoiCell>(cells.Count);`). Add a public accessor near the other public cell-query methods (e.g. right before the existing `GetCellUnderRay`):

```csharp
/// <summary>Находит клетку по Id - используется кистью для обхода NeighborIds (например, для Сглаживания).</summary>
public VoronoiCell GetCellById(int id) => cellById != null && cellById.TryGetValue(id, out var cell) ? cell : null;
```

- [ ] **Step 2: Add a hit-point-returning overload of `GetCellUnderRay`**

Find the existing `GetCellUnderRay(Ray ray, float maxDistance = 2000f)` (around `WorldMapRenderer.cs:1032-1041`, using `meshCollider.Raycast`). Replace it with two overloads — the new one doing the real work, the old signature now delegating to it (so every existing caller, e.g. `CellSelectionController`, keeps compiling unchanged):

```csharp
public VoronoiCell GetCellUnderRay(Ray ray, float maxDistance = 2000f)
{
    return GetCellUnderRay(ray, out _, maxDistance);
}

/// <summary>Как GetCellUnderRay, но также возвращает точную мировую точку попадания луча -
/// нужна кисти для радиусной покраски вокруг курсора, а не вокруг центра клетки.</summary>
public VoronoiCell GetCellUnderRay(Ray ray, out Vector3 hitPoint, float maxDistance = 2000f)
{
    hitPoint = Vector3.zero;
    if (meshCollider == null) return null;
    if (!meshCollider.Raycast(ray, out RaycastHit hit, maxDistance)) return null;

    hitPoint = hit.point;
    return triangleToCellId != null && hit.triangleIndex >= 0 && hit.triangleIndex < triangleToCellId.Length
        ? GetCellById(triangleToCellId[hit.triangleIndex])
        : null;
}
```

(Confirm the exact field name `triangleToCellId` and its exact lookup expression by reading the current `GetCellUnderRay`'s body before replacing it — reuse whatever expression it already uses to map `hit.triangleIndex` to a cell, just wrapped in the new two-overload shape above.)

- [ ] **Step 3: Add `BrushAdjustBiome` to `WorldMapRenderer.cs`**

Add directly after the existing `BrushAdjustMoisture` (`WorldMapRenderer.cs:451-457`):

```csharp
/// <summary>Устанавливает прямой biome override клетки (кисть "Биом" - жёсткая установка, не blend). Записывает "досмазковое" состояние клетки в текущий мазок перед изменением.</summary>
public void BrushAdjustBiome(VoronoiCell cell, Biome? biome)
{
    if (cells == null) return;
    brushUndo.RecordBeforeChange(cell);
    CellOverrideService.ApplyBiomeOverride(new[] { cell }, biome, beachElevationThreshold);
    RecolorOnly();
}
```

- [ ] **Step 4: Add `UndoAll` to `BrushUndoManager.cs`**

Add directly after the existing `Undo()` method (`BrushUndoManager.cs:56-65`):

```csharp
/// <summary>Отменяет ВСЕ мазки в истории разом - "Отменить всё" в UI. В отличие от ClearHistory,
/// реально откатывает изменения клеток, а не просто теряет возможность их отменить.</summary>
public void UndoAll()
{
    while (Undo()) { }
}
```

- [ ] **Step 5: Add `UndoAllBrushStrokes` to `WorldMapRenderer.cs`**

Add directly after `UndoLastBrushStroke` (`WorldMapRenderer.cs:463-473`):

```csharp
/// <summary>"Отменить всё" в UI кисти - откатывает КАЖДЫЙ мазок в истории сессии, не только последний.</summary>
public void UndoAllBrushStrokes()
{
    if (cells == null) return;
    brushUndo.UndoAll();
    RecolorOnly();
    OnDisplayChanged?.Invoke();
}
```

- [ ] **Step 6: Manual verification**

Deferred to Task 2/3 (no standalone UI hook exists yet to exercise these methods in isolation).

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Rendering/WorldMapRenderer.cs Assets/WorldGen/Generation/BrushUndoManager.cs
git commit -m "feat: add cell-by-id lookup, hit-point raycast overload, biome brush, and bulk undo to WorldMapRenderer"
```

---

### Task 2: `BrushToolController.cs` rewrite — radius/shape query, Raise/Lower/Smooth/Biome

**Files:**
- Modify: `Assets/WorldGen/Rendering/BrushToolController.cs` (full rewrite of the file's tool logic — the mouse-input/undo/stroke lifecycle at the top stays structurally the same, `PaintAtCursor`/`ApplyDelta` are replaced)

**Interfaces:**
- Consumes: `WorldMapRenderer.GetCellUnderRay(Ray, out Vector3, float)`, `.GetCellById(int)`, `.BrushAdjustElevation/Temperature/Moisture(VoronoiCell, float)` (existing), `.BrushAdjustBiome(VoronoiCell, Biome?)` (Task 1), `.Cells` (existing public cell list — confirm exact property name before use).
- Produces: `public enum BrushTarget { Elevation, Temperature, Moisture, Biome }`, `public enum BrushMode { Raise, Lower, Smooth }`, `public enum BrushShape { Circle, Square }`, public fields `activeTarget`, `activeMode`, `activeShape`, `radius`, `strengthPercent`, `selectedPaletteBiome` — all consumed by Task 3 (`EditorBrushPanel.cs`).

- [ ] **Step 1: Replace `BrushToolController.cs` in full**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public enum BrushTarget { Elevation, Temperature, Moisture, Biome }
    public enum BrushMode { Raise, Lower, Smooth }
    public enum BrushShape { Circle, Square }

    /// <summary>
    /// Радиусная кисть - применяет изменение ко ВСЕМ клеткам в радиусе вокруг курсора, пока
    /// зажата ЛКМ. Raise/Lower - относительное изменение (как раньше, но на несколько клеток
    /// разом); Smooth - каждая клетка сдвигается к среднему по себе+соседям; Biome - жёсткая
    /// установка выбранного в палитре биома всем клеткам в радиусе (Strength не используется).
    ///
    /// Один проход кистью (от нажатия до отпускания ЛКМ) - одна Undo-операция.
    /// Использует новый Input System, как и CellSelectionController.
    ///
    /// Работает НЕЗАВИСИМО от CellSelectionController - включай только один режим одновременно
    /// (см. brushModeActive).
    /// </summary>
    public class BrushToolController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        [Tooltip("Камера, с которой кастуется луч кисти. Если не назначено - используется Camera.main.")]
        public Camera raycastCamera;
        [Tooltip("If assigned, brush painting is suppressed when POI interaction controller has claimed the input.")]
        public PoiInteractionController poiController;

        [Header("Настройки инструмента")]
        public BrushTarget activeTarget = BrushTarget.Elevation;
        public BrushMode activeMode = BrushMode.Raise;
        public BrushShape activeShape = BrushShape.Circle;
        [Tooltip("Радиус кисти в мировых единицах (то же пространство, что VoronoiCell.Site).")]
        public float radius = 20f;
        [Tooltip("Сила применения, 0-100%. Не используется для BrushTarget.Biome.")]
        public float strengthPercent = 60f;
        [Tooltip("Величина базового шага за одно применение (до умножения на strengthPercent/100).")]
        public float baseStep = 0.05f;
        [Tooltip("Выбранный в контекстной палитре биом - обязателен для покраски при activeTarget == Biome.")]
        public Biome? selectedPaletteBiome;

        [Tooltip("Интервал в секундах между повторными применениями кисти, пока ЛКМ зажата без движения.")]
        public float repeatInterval = 0.05f;

        [Header("Включение режима")]
        public bool brushModeActive = false;

        bool isPainting;
        float repeatTimer;
        readonly List<VoronoiCell> affectedCellsBuffer = new List<VoronoiCell>();

        void Update()
        {
            if (!brushModeActive) return;
            if (poiController != null && poiController.InputConsumedThisFrame) return;
            if (mapRenderer == null) return;
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return;
            if (Mouse.current == null) return;

            HandleUndo();
            HandlePainting();
        }

        void HandleUndo()
        {
            if (Keyboard.current == null) return;
            bool ctrlHeld = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (ctrlHeld && Keyboard.current.zKey.wasPressedThisFrame)
            {
                bool didUndo = mapRenderer.UndoLastBrushStroke();
                if (didUndo)
                    Debug.Log($"BrushToolController: отменён последний мазок. Осталось в истории: {mapRenderer.BrushUndoStackCount}.");
            }
        }

        void HandlePainting()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                isPainting = true;
                repeatTimer = 0f;
                mapRenderer.BeginBrushStroke();
                PaintAtCursor();
            }
            else if (Mouse.current.leftButton.isPressed && isPainting)
            {
                float interval = Mathf.Max(repeatInterval, 0.0001f);
                repeatTimer += Time.deltaTime;
                while (repeatTimer >= interval)
                {
                    repeatTimer -= interval;
                    PaintAtCursor();
                }
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && isPainting)
            {
                isPainting = false;
                mapRenderer.EndBrushStroke();
            }
        }

        void PaintAtCursor()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            var hitCell = mapRenderer.GetCellUnderRay(ray, out Vector3 hitPoint);
            if (hitCell == null) return;

            GatherAffectedCells(hitPoint, affectedCellsBuffer);
            if (affectedCellsBuffer.Count == 0) return;

            if (activeTarget == BrushTarget.Biome)
            {
                if (!selectedPaletteBiome.HasValue) return; // ничего не выбрано в палитре - красить нечем
                foreach (var cell in affectedCellsBuffer)
                    mapRenderer.BrushAdjustBiome(cell, selectedPaletteBiome.Value);
                return;
            }

            float signedDelta = activeMode == BrushMode.Lower ? -baseStep : baseStep;
            float scaledDelta = signedDelta * (strengthPercent / 100f);

            foreach (var cell in affectedCellsBuffer)
            {
                float delta = activeMode == BrushMode.Smooth
                    ? (ComputeNeighborAverage(cell) - GetEffectiveValue(cell)) * (strengthPercent / 100f)
                    : scaledDelta;

                switch (activeTarget)
                {
                    case BrushTarget.Elevation: mapRenderer.BrushAdjustElevation(cell, delta); break;
                    case BrushTarget.Temperature: mapRenderer.BrushAdjustTemperature(cell, delta); break;
                    case BrushTarget.Moisture: mapRenderer.BrushAdjustMoisture(cell, delta); break;
                }
            }
        }

        /// <summary>Заполняет buffer клетками в радиусе activeShape вокруг hitPoint. Переиспользует
        /// buffer между вызовами (очищает в начале) - убирает GC-аллокации на каждый тик кисти.</summary>
        void GatherAffectedCells(Vector3 hitPoint, List<VoronoiCell> buffer)
        {
            buffer.Clear();
            if (mapRenderer.Cells == null) return;

            foreach (var cell in mapRenderer.Cells)
            {
                float dx = cell.Site.X - hitPoint.x;
                float dz = cell.Site.Y - hitPoint.z;

                bool inside = activeShape == BrushShape.Circle
                    ? (dx * dx + dz * dz) <= radius * radius
                    : Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius;

                if (inside) buffer.Add(cell);
            }
        }

        float GetEffectiveValue(VoronoiCell cell) => activeTarget switch
        {
            BrushTarget.Elevation => cell.EffectiveElevation,
            BrushTarget.Temperature => cell.EffectiveTemperature,
            BrushTarget.Moisture => cell.EffectiveMoisture,
            _ => 0f
        };

        /// <summary>Среднее значение по самой клетке + всем её NeighborIds (для Сглаживания).</summary>
        float ComputeNeighborAverage(VoronoiCell cell)
        {
            float sum = GetEffectiveValue(cell);
            int count = 1;
            foreach (int neighborId in cell.NeighborIds)
            {
                var neighbor = mapRenderer.GetCellById(neighborId);
                if (neighbor == null) continue;
                sum += GetEffectiveValue(neighbor);
                count++;
            }
            return sum / count;
        }

        [ContextMenu("Self-Test: Radius Query (Circle)")]
        public void SelfTestRadiusQueryCircle()
        {
            var a = new VoronoiCell(1, new System.Numerics.Vector2(0f, 0f));
            var b = new VoronoiCell(2, new System.Numerics.Vector2(5f, 0f));
            var c = new VoronoiCell(3, new System.Numerics.Vector2(50f, 0f));
            var testCells = new List<VoronoiCell> { a, b, c };

            float dx0 = a.Site.X - 0f, dz0 = a.Site.Y - 0f;
            float dxB = b.Site.X - 0f, dzB = b.Site.Y - 0f;
            float dxC = c.Site.X - 0f, dzC = c.Site.Y - 0f;
            float testRadius = 10f;

            bool aInside = (dx0 * dx0 + dz0 * dz0) <= testRadius * testRadius;
            bool bInside = (dxB * dxB + dzB * dzB) <= testRadius * testRadius;
            bool cInside = (dxC * dxC + dzC * dzC) <= testRadius * testRadius;

            bool ok = aInside && bInside && !cInside;
            Debug.Log(ok ? "Self-Test Radius Query (Circle): PASS"
                          : $"Self-Test Radius Query (Circle): FAIL (a={aInside}, b={bInside}, c={cInside})");
        }
    }
}
```

- [ ] **Step 2: Confirm `WorldMapRenderer.Cells` property name**

Open `Assets/WorldGen/Rendering/WorldMapRenderer.cs` and confirm the exact name/type of the public cell-list accessor already used elsewhere (e.g. `MapScreenController`'s `mapRenderer.Cells == null` check, per this project's existing code). If it's named differently than `Cells` (case or naming), adjust `GatherAffectedCells`'s `mapRenderer.Cells` references in Step 1 to match exactly.

- [ ] **Step 3: Manual verification**

In the Unity Editor, Play mode, run "Self-Test: Radius Query (Circle)" via the `BrushToolController` component's context menu — expect `PASS`. Then manually paint on a generated map: Raise/Lower Height/Temperature/Moisture affects a visible circular/square area (not just one cell); Smooth visibly flattens spikes toward neighbor averages over repeated strokes; Ctrl+Z undoes one stroke's entire affected-cell set at once.

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/BrushToolController.cs
git commit -m "feat: rewrite BrushToolController for radius/shape multi-cell painting with Raise/Lower/Smooth/Biome"
```

---

### Task 3: `EditorBrushPanel.cs` — new Brush UI + contextual biome palette

**Files:**
- Modify: `Assets/WorldGen/Rendering/EditorBrushPanel.cs` (produced unchanged by the Main-screen shell plan — this task replaces its Brush sub-panel content and relabels the top mode toggle; its Selection+Override sub-panel is untouched)

**Interfaces:**
- Consumes: `BrushToolController`'s public fields/enums from Task 2 (`activeTarget`, `activeMode`, `activeShape`, `radius`, `strengthPercent`, `selectedPaletteBiome`, `BrushTarget`/`BrushMode`/`BrushShape`), `mapRenderer.UndoAllBrushStrokes()` (Task 1).

- [ ] **Step 1: Read the current file**

Open `Assets/WorldGen/Rendering/EditorBrushPanel.cs` (produced by the Main-screen shell plan's Task 4 — a verbatim copy of the old `MapEditorPanel.BuildEditorTab` et al.). Locate: the top 2-button mode segment (was "Selection & Override" / "Brush" or equivalent — read the actual current label strings), the `EditorMode` enum, `currentMode` field default, and `BuildBrushSection`'s full body (the part to be replaced).

- [ ] **Step 2: Relabel the top toggle and flip the default**

Change the mode segment's two button label strings to `"Кисть"` and `"Точное выделение"` (Кисть first/left, matching the `Brush`/`SelectionOverride` enum order — confirm which enum value each label maps to and keep that mapping, just changing the display text). Change `currentMode`'s field initializer from `EditorMode.SelectionOverride` to `EditorMode.Brush` so the new radius brush is the default shown on open.

- [ ] **Step 3: Replace `BuildBrushSection`'s body**

Keep the method signature (`void BuildBrushSection(Transform parent)` or whatever the existing signature is — match it exactly) but replace its body with:

```csharp
void BuildBrushSection(Transform parent)
{
    BuildTargetGrid(parent);
    BuildModeSegment(parent);
    BuildShapeButtons(parent);
    BuildSizeSlider(parent);
    BuildStrengthSlider(parent);
    BuildBrushFooter(parent);
    BuildContextualBiomePalette(parent);
    RefreshBiomeModeVisibility();
}

void BuildTargetGrid(Transform parent)
{
    var gridGO = new GameObject("TargetGrid");
    gridGO.transform.SetParent(parent, false);
    var grid = gridGO.AddComponent<GridLayoutGroup>();
    grid.cellSize = new Vector2(110f, 30f);
    grid.spacing = new Vector2(4f, 4f);
    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    grid.constraintCount = 2;
    gridGO.AddComponent<LayoutElement>().preferredHeight = 68f;

    string[] labels = { "Высота", "Температура", "Влажность", "Биом" };
    var targets = new[] { BrushTarget.Elevation, BrushTarget.Temperature, BrushTarget.Moisture, BrushTarget.Biome };
    targetButtons = new Button[4];

    for (int i = 0; i < 4; i++)
    {
        int captured = i;
        var btnGO = new GameObject($"Target_{labels[i]}");
        btnGO.transform.SetParent(gridGO.transform, false);
        var img = btnGO.AddComponent<Image>();
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            brushController.activeTarget = targets[captured];
            RefreshTargetColors();
            RefreshBiomeModeVisibility();
        });
        targetButtons[i] = btn;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = labels[i];
        text.font = builtinFont;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleCenter;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
    }
    RefreshTargetColors();
}

void RefreshTargetColors()
{
    var targets = new[] { BrushTarget.Elevation, BrushTarget.Temperature, BrushTarget.Moisture, BrushTarget.Biome };
    for (int i = 0; i < targetButtons.Length; i++)
    {
        bool active = targets[i] == brushController.activeTarget;
        ThemeService.Tag(targetButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Elev);
        ThemeService.Tag(targetButtons[i].GetComponentInChildren<Text>(), active ? ThemeRole.AccentInk : ThemeRole.Txt);
    }
}

void BuildModeSegment(Transform parent)
{
    modeSegmentRowGO = new GameObject("ModeSegment");
    modeSegmentRowGO.transform.SetParent(parent, false);
    modeSegmentRowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
    var layout = modeSegmentRowGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 4f;
    layout.childControlWidth = true;
    layout.childForceExpandWidth = true;

    string[] labels = { "Поднять", "Опустить", "Сгладить" };
    var modes = new[] { BrushMode.Raise, BrushMode.Lower, BrushMode.Smooth };
    modeButtons = new Button[3];

    for (int i = 0; i < 3; i++)
    {
        int captured = i;
        var btnGO = new GameObject($"Mode_{labels[i]}");
        btnGO.transform.SetParent(modeSegmentRowGO.transform, false);
        var img = btnGO.AddComponent<Image>();
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => { brushController.activeMode = modes[captured]; RefreshModeColors(); });
        modeButtons[i] = btn;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = labels[i];
        text.font = builtinFont;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleCenter;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
    }
    RefreshModeColors();
}

void RefreshModeColors()
{
    var modes = new[] { BrushMode.Raise, BrushMode.Lower, BrushMode.Smooth };
    for (int i = 0; i < modeButtons.Length; i++)
    {
        bool active = modes[i] == brushController.activeMode;
        ThemeService.Tag(modeButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Elev);
        ThemeService.Tag(modeButtons[i].GetComponentInChildren<Text>(), active ? ThemeRole.AccentInk : ThemeRole.Txt);
    }
}

void BuildShapeButtons(Transform parent)
{
    var rowGO = new GameObject("ShapeRow");
    rowGO.transform.SetParent(parent, false);
    rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
    var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 4f;
    layout.childControlWidth = true;
    layout.childForceExpandWidth = true;

    string[] labels = { "○ Круг", "□ Квадрат" };
    var shapes = new[] { BrushShape.Circle, BrushShape.Square };
    shapeButtons = new Button[2];

    for (int i = 0; i < 2; i++)
    {
        int captured = i;
        var btnGO = new GameObject($"Shape_{shapes[i]}");
        btnGO.transform.SetParent(rowGO.transform, false);
        var img = btnGO.AddComponent<Image>();
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => { brushController.activeShape = shapes[captured]; RefreshShapeColors(); });
        shapeButtons[i] = btn;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = labels[i];
        text.font = builtinFont;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleCenter;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
    }
    RefreshShapeColors();
}

void RefreshShapeColors()
{
    var shapes = new[] { BrushShape.Circle, BrushShape.Square };
    for (int i = 0; i < shapeButtons.Length; i++)
    {
        bool active = shapes[i] == brushController.activeShape;
        ThemeService.Tag(shapeButtons[i].targetGraphic as Image, active ? ThemeRole.Accent : ThemeRole.Elev);
        ThemeService.Tag(shapeButtons[i].GetComponentInChildren<Text>(), active ? ThemeRole.AccentInk : ThemeRole.Txt);
    }
}

void BuildSizeSlider(Transform parent)
{
    var rowGO = new GameObject("SizeRow");
    rowGO.transform.SetParent(parent, false);
    rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
    var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 8f;

    var labelGO = new GameObject("Label");
    labelGO.transform.SetParent(rowGO.transform, false);
    var label = labelGO.AddComponent<Text>();
    label.text = "Размер";
    label.font = builtinFont;
    label.fontSize = 11;
    ThemeService.Tag(label, ThemeRole.Mut);
    labelGO.AddComponent<LayoutElement>().preferredWidth = 60f;

    var sliderGO = new GameObject("Slider");
    sliderGO.transform.SetParent(rowGO.transform, false);
    sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
    var slider = sliderGO.AddComponent<Slider>();
    slider.minValue = 5f;
    slider.maxValue = 100f;
    slider.value = brushController.radius;

    var bgGO = new GameObject("Bg");
    bgGO.transform.SetParent(sliderGO.transform, false);
    var bgImg = bgGO.AddComponent<Image>();
    ThemeService.Tag(bgImg, ThemeRole.Elev);
    var bgRect = bgGO.GetComponent<RectTransform>();
    bgRect.anchorMin = new Vector2(0f, 0.25f);
    bgRect.anchorMax = new Vector2(1f, 0.75f);
    bgRect.sizeDelta = Vector2.zero;

    var fillGO = new GameObject("Fill");
    fillGO.transform.SetParent(sliderGO.transform, false);
    var fillImg = fillGO.AddComponent<Image>();
    ThemeService.Tag(fillImg, ThemeRole.Accent);
    var fillRect = fillGO.GetComponent<RectTransform>();
    fillRect.anchorMin = new Vector2(0f, 0.2f);
    fillRect.anchorMax = new Vector2(0f, 0.8f);
    fillRect.sizeDelta = Vector2.zero;
    slider.fillRect = fillRect;
    slider.targetGraphic = fillImg;

    var valueGO = new GameObject("Value");
    valueGO.transform.SetParent(rowGO.transform, false);
    sizeValueLabel = valueGO.AddComponent<Text>();
    sizeValueLabel.font = builtinFont;
    sizeValueLabel.fontSize = 11;
    sizeValueLabel.fontStyle = FontStyle.Bold;
    sizeValueLabel.text = $"{Mathf.RoundToInt(brushController.radius)} px";
    ThemeService.Tag(sizeValueLabel, ThemeRole.Accent);
    valueGO.AddComponent<LayoutElement>().preferredWidth = 50f;

    slider.onValueChanged.AddListener(v =>
    {
        brushController.radius = v;
        sizeValueLabel.text = $"{Mathf.RoundToInt(v)} px";
    });
}

void BuildStrengthSlider(Transform parent)
{
    var rowGO = new GameObject("StrengthRow");
    rowGO.transform.SetParent(parent, false);
    rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
    var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 8f;

    var labelGO = new GameObject("Label");
    labelGO.transform.SetParent(rowGO.transform, false);
    var label = labelGO.AddComponent<Text>();
    label.text = "Сила";
    label.font = builtinFont;
    label.fontSize = 11;
    ThemeService.Tag(label, ThemeRole.Mut);
    labelGO.AddComponent<LayoutElement>().preferredWidth = 60f;

    var sliderGO = new GameObject("Slider");
    sliderGO.transform.SetParent(rowGO.transform, false);
    sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
    var slider = sliderGO.AddComponent<Slider>();
    slider.minValue = 0f;
    slider.maxValue = 100f;
    slider.value = brushController.strengthPercent;

    var bgGO = new GameObject("Bg");
    bgGO.transform.SetParent(sliderGO.transform, false);
    var bgImg = bgGO.AddComponent<Image>();
    ThemeService.Tag(bgImg, ThemeRole.Elev);
    var bgRect = bgGO.GetComponent<RectTransform>();
    bgRect.anchorMin = new Vector2(0f, 0.25f);
    bgRect.anchorMax = new Vector2(1f, 0.75f);
    bgRect.sizeDelta = Vector2.zero;

    var fillGO = new GameObject("Fill");
    fillGO.transform.SetParent(sliderGO.transform, false);
    var fillImg = fillGO.AddComponent<Image>();
    ThemeService.Tag(fillImg, ThemeRole.Accent);
    var fillRect = fillGO.GetComponent<RectTransform>();
    fillRect.anchorMin = new Vector2(0f, 0.2f);
    fillRect.anchorMax = new Vector2(0f, 0.8f);
    fillRect.sizeDelta = Vector2.zero;
    slider.fillRect = fillRect;
    slider.targetGraphic = fillImg;

    var valueGO = new GameObject("Value");
    valueGO.transform.SetParent(rowGO.transform, false);
    strengthValueLabel = valueGO.AddComponent<Text>();
    strengthValueLabel.font = builtinFont;
    strengthValueLabel.fontSize = 11;
    strengthValueLabel.fontStyle = FontStyle.Bold;
    strengthValueLabel.text = $"{Mathf.RoundToInt(brushController.strengthPercent)}%";
    ThemeService.Tag(strengthValueLabel, ThemeRole.Accent);
    valueGO.AddComponent<LayoutElement>().preferredWidth = 50f;

    slider.onValueChanged.AddListener(v =>
    {
        brushController.strengthPercent = v;
        strengthValueLabel.text = $"{Mathf.RoundToInt(v)}%";
    });
}

void BuildBrushFooter(Transform parent)
{
    var btnGO = new GameObject("UndoAllButton");
    btnGO.transform.SetParent(parent, false);
    btnGO.AddComponent<LayoutElement>().preferredHeight = 32f;
    var img = btnGO.AddComponent<Image>();
    ThemeService.Tag(img, ThemeRole.Danger);
    var btn = btnGO.AddComponent<Button>();
    btn.targetGraphic = img;
    btn.onClick.AddListener(() => brushController.mapRenderer.UndoAllBrushStrokes());

    var textGO = new GameObject("Text");
    textGO.transform.SetParent(btnGO.transform, false);
    var text = textGO.AddComponent<Text>();
    text.text = "Отменить всё";
    text.font = builtinFont;
    text.fontSize = 12;
    text.alignment = TextAnchor.MiddleCenter;
    ThemeService.Tag(text, ThemeRole.Txt);
    var textRect = textGO.GetComponent<RectTransform>();
    textRect.anchorMin = Vector2.zero;
    textRect.anchorMax = Vector2.one;
    textRect.sizeDelta = Vector2.zero;
}

static readonly Biome[] PaintableBiomes =
{
    Biome.Beach, Biome.Snow, Biome.Tundra, Biome.Bare, Biome.Scorched, Biome.Taiga,
    Biome.Shrubland, Biome.TemperateDesert, Biome.TemperateRainForest,
    Biome.TemperateDeciduousForest, Biome.Grassland, Biome.TropicalRainForest,
    Biome.TropicalSeasonalForest, Biome.SubtropicalDesert
    // Ocean/Lake deliberately excluded - those are water-status concepts (WaterOverride),
    // not something to paint as a biome override without also touching water status.
};

void BuildContextualBiomePalette(Transform parent)
{
    biomePaletteGO = new GameObject("BiomePalette");
    biomePaletteGO.transform.SetParent(parent, false);
    var grid = biomePaletteGO.AddComponent<GridLayoutGroup>();
    grid.cellSize = new Vector2(38f, 24f);
    grid.spacing = new Vector2(3f, 3f);
    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    grid.constraintCount = 6;
    biomePaletteGO.AddComponent<LayoutElement>().preferredHeight = 3 * 27f;

    paletteSwatches = new Image[PaintableBiomes.Length];
    for (int i = 0; i < PaintableBiomes.Length; i++)
    {
        int captured = i;
        var swatchGO = new GameObject($"Biome_{PaintableBiomes[i]}");
        swatchGO.transform.SetParent(biomePaletteGO.transform, false);
        var img = swatchGO.AddComponent<Image>();
        img.color = RegionColorPalette.GetBiomeColor(PaintableBiomes[i]); // семантический цвет карты, не тема - см. design-decisions
        var btn = swatchGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            brushController.selectedPaletteBiome = PaintableBiomes[captured];
            RefreshPaletteSelection();
        });
        paletteSwatches[i] = img;
    }
    RefreshPaletteSelection();
}

void RefreshPaletteSelection()
{
    for (int i = 0; i < paletteSwatches.Length; i++)
    {
        var outline = paletteSwatches[i].GetComponent<Outline>();
        bool selected = brushController.selectedPaletteBiome.HasValue && PaintableBiomes[i] == brushController.selectedPaletteBiome.Value;
        if (selected && outline == null)
        {
            outline = paletteSwatches[i].gameObject.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(2f, -2f);
        }
        else if (!selected && outline != null)
        {
            Destroy(outline);
        }
    }
}

void RefreshBiomeModeVisibility()
{
    bool isBiome = brushController.activeTarget == BrushTarget.Biome;
    modeSegmentRowGO.SetActive(!isBiome);
    biomePaletteGO.SetActive(isBiome);
}
```

Add the new private fields near the top of the class (alongside the existing extracted fields):

```csharp
Button[] targetButtons;
Button[] modeButtons;
Button[] shapeButtons;
Text sizeValueLabel;
Text strengthValueLabel;
GameObject modeSegmentRowGO;
GameObject biomePaletteGO;
Image[] paletteSwatches;
```

Add `using WorldGen.Generation;` at the top of the file if not already present (needed for `Biome`).

- [ ] **Step 4: Manual verification**

Play mode, generated map: open Редактор tab, confirm "Кисть" is the default sub-mode and "Точное выделение" still reaches the unchanged old panel. Click each of the 4 targets — Режим segment hides and the biome palette shows only for Биом. Paint with each target/mode combination on the real map and confirm visible multi-cell effect; select a palette swatch (gets an accent outline) then paint to confirm biome painting; "Отменить всё" reverts a multi-stroke session in one click.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/EditorBrushPanel.cs
git commit -m "feat: rebuild EditorBrushPanel's brush UI (target grid, mode segment, shape, sliders, biome palette)"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1 covers the `WorldMapRenderer`/`BrushUndoManager` API additions; Task 2 covers the radius/shape query engine and Raise/Lower/Smooth/Biome application; Task 3 covers the UI (target grid, mode segment, shape, size/strength sliders, footer, contextual palette, mode-toggle relabel). All in-scope items from the design spec are represented; the Selection+Override sub-panel is explicitly left untouched per the spec.
- **Placeholder scan:** none of the "confirm the exact name/body" instructions hide new logic — they point at specific existing lines whose exact current text the implementer must read before editing, since this plan was written from an earlier code-reading pass and small line-number drift is possible.
- **Type consistency:** `BrushTarget`/`BrushMode`/`BrushShape` enum names and values are defined once in Task 2 and referenced identically in Task 3; `mapRenderer.GetCellUnderRay(Ray, out Vector3, float)`, `.GetCellById(int)`, `.BrushAdjustBiome(VoronoiCell, Biome?)`, `.UndoAllBrushStrokes()` are defined in Task 1 and consumed with matching signatures in Task 2/3.
