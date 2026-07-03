# Map/Notes Split — Single Source of Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace three independent, unsynchronized copies of the map/notes split fraction (`NotesLayoutController.splitFraction`, `MapLegendUI.rightBoundaryFraction`, `PoiEditPanel.rightBoundaryFraction`) with one shared constant, and remove the unused `mapAreaRoot` field, per `docs/superpowers/specs/2026-07-03-map-notes-split-single-source-design.md`.

**Architecture:** No new subsystems, no visual change. `NotesLayoutController` gains a `public const float SplitFraction`; `MapLegendUI`/`PoiEditPanel` read it directly instead of declaring their own field. A `const` (not a runtime-assigned static) sidesteps a real Unity script-execution-order hazard, since the two panels apply their boundary fraction during their own `Awake()`.

**Tech Stack:** Unity 6000.3.2f1, C#, legacy `UnityEngine.UI`.

## Global Constraints

- **No visual/behavioral change** — the split stays fixed at `2/3`, not user-resizable.
- **No unifying of Canvases/`mapAreaRoot`** — that's a larger, separate restructuring, explicitly out of scope.
- **Out of scope (do not touch):** notes editor internal layout (sidebar/toolbar/canvas proportions) — a separate follow-up topic.

---

### Task 1: Single-source split constant, remove dead `mapAreaRoot`

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`
- Modify: `Assets/WorldGen/Rendering/MapLegendUI.cs`
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `NotesLayoutController.SplitFraction` (`public const float`, value `2f / 3f`) — the one place the fraction is defined; `MapLegendUI`/`PoiEditPanel` read it directly at the point they set their panel's anchors.

- [ ] **Step 1: Add the shared constant and remove the per-instance field in `NotesLayoutController`**

In `Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs`, replace:

```csharp
    public class NotesLayoutController : MonoBehaviour
    {
        [Tooltip("Root RectTransform containing the map/world UI. Anchored to the left two-thirds.")]
        public RectTransform mapAreaRoot;
        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        [Range(0.1f, 0.9f)]
        [Tooltip("Fraction of screen width given to the map area; the rest goes to notes.")]
        public float splitFraction = 2f / 3f;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (mapAreaRoot != null)
            {
                mapAreaRoot.anchorMin = new Vector2(0f, 0f);
                mapAreaRoot.anchorMax = new Vector2(splitFraction, 1f);
                mapAreaRoot.offsetMin = Vector2.zero;
                mapAreaRoot.offsetMax = Vector2.zero;
            }

            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(splitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, splitFraction, 1f);
        }
    }
```

with:

```csharp
    public class NotesLayoutController : MonoBehaviour
    {
        /// <summary>Single source of truth for the map/notes screen split — the ONLY place this
        /// fraction is defined. MapLegendUI and PoiEditPanel read this directly instead of each
        /// declaring their own copy, which is what let them drift out of sync before. A const
        /// (not a runtime-assigned static) is used because those two panels apply it inside their
        /// own Awake(), and Unity does not guarantee this class's Awake() runs first.</summary>
        public const float SplitFraction = 2f / 3f;

        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(SplitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, SplitFraction, 1f);
        }
    }
```

- [ ] **Step 2: Remove the unused `mapAreaRoot` field from `NotesRootBuilder`**

In `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`, replace the class doc-comment:

```csharp
    /// <summary>
    /// Builds the full notes editor UI hierarchy (layout split, sidebar, toolbar, canvas
    /// viewport) at Awake and wires the sub-controllers together. Attach to an empty
    /// GameObject in the scene; assign mapAreaRoot to the existing map UI's root RectTransform
    /// and poiManager to the scene's PoiManager for POI-linked group creation.
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        [Header("External refs")]
        [Tooltip("Root RectTransform of the existing map/editor UI, to be anchored to the left two-thirds.")]
        public RectTransform mapAreaRoot;
        [Tooltip("Camera rendering the 3D map (usually Main Camera / WorldMapRenderer.targetCamera). Its viewport is clamped to the map area.")]
        public Camera mapCamera;
```

with:

```csharp
    /// <summary>
    /// Builds the full notes editor UI hierarchy (layout split, sidebar, toolbar, canvas
    /// viewport) at Awake and wires the sub-controllers together. Attach to an empty
    /// GameObject in the scene; assign mapCamera to the camera rendering the 3D map so its
    /// viewport gets clamped to the map area (NotesLayoutController.SplitFraction).
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        [Header("External refs")]
        [Tooltip("Camera rendering the 3D map (usually Main Camera / WorldMapRenderer.targetCamera). Its viewport is clamped to the map area.")]
        public Camera mapCamera;
```

Replace:

```csharp
            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.mapAreaRoot = mapAreaRoot;
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();
```

with:

```csharp
            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();
```

- [ ] **Step 3: `MapLegendUI` reads the shared constant instead of its own field**

In `Assets/WorldGen/Rendering/MapLegendUI.cs`, replace the `using` block:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;

namespace WorldGen.Rendering
```

with:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;

namespace WorldGen.Rendering
```

Replace:

```csharp
        [Header("Настройки внешнего вида")]
        public Vector2 panelAnchoredPosition = new Vector2(-20f, -20f);
        [Tooltip("Horizontal anchor fraction of the screen this panel's right edge sits at. 1 = full screen right edge; set to the notes split fraction (e.g. 2/3) when the notes editor occupies the right third of the screen, so this panel stays inside the map area instead of overlapping it.")]
        [Range(0.1f, 1f)]
        public float rightBoundaryFraction = 1f;
        public Vector2 swatchSize = new Vector2(20f, 20f);
```

with:

```csharp
        [Header("Настройки внешнего вида")]
        public Vector2 panelAnchoredPosition = new Vector2(-20f, -20f);
        public Vector2 swatchSize = new Vector2(20f, 20f);
```

Replace:

```csharp
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(rightBoundaryFraction, 1f); // привязка к правому верхнему углу карты (не всего экрана)
            panelRect.anchorMax = new Vector2(rightBoundaryFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = panelAnchoredPosition;
```

with:

```csharp
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(NotesLayoutController.SplitFraction, 1f); // привязка к правому верхнему углу карты (не всего экрана)
            panelRect.anchorMax = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = panelAnchoredPosition;
```

- [ ] **Step 4: `PoiEditPanel` reads the shared constant instead of its own field**

In `Assets/WorldGen/Rendering/PoiEditPanel.cs`, replace:

```csharp
        [Header("Внешний вид")]
        [Tooltip("Горизонтальный отступ от правого края экрана.")]
        public float rightMargin = 20f;
        [Tooltip("Horizontal anchor fraction of the screen this panel's right edge sits at. 1 = full screen right edge; set to the notes split fraction (e.g. 2/3) when the notes editor occupies the right third of the screen, so this panel stays inside the map area instead of overlapping it.")]
        [Range(0.1f, 1f)]
        public float rightBoundaryFraction = 1f;
        [Tooltip("Отступ снизу от нижней грани легенды.")]
```

with:

```csharp
        [Header("Внешний вид")]
        [Tooltip("Горизонтальный отступ от правого края экрана.")]
        public float rightMargin = 20f;
        [Tooltip("Отступ снизу от нижней грани легенды.")]
```

Replace:

```csharp
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(rightBoundaryFraction, 1f);
            panelRect.anchorMax = new Vector2(rightBoundaryFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(panelWidth, 0f);
```

with:

```csharp
            panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.anchorMax = new Vector2(NotesLayoutController.SplitFraction, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(panelWidth, 0f);
```

(`PoiEditPanel.cs` already has `using WorldGen.Notes.Rendering;` at the top — it references `NotesRootBuilder` there already — so no new `using` is needed for this file.)

- [ ] **Step 5: Verify compilation**

Open Unity. Expected: no Console errors. The Inspector for `NotesLayoutController`, `MapLegendUI`, and `PoiEditPanel` components in the scene will show the removed fields as gone (Unity silently drops now-unrecognized serialized values — no manual scene editing needed).

- [ ] **Step 6: Play-mode verify — behavior unchanged**

Press Play. Expected: exactly the same visual layout as before this change — map occupies the left two-thirds, notes editor the right third, legend and POI edit panel both sit inside the map area (not overlapping the notes panel).

- [ ] **Step 7: Play-mode verify — single source of truth actually works**

Stop Play mode. Temporarily change `NotesLayoutController.SplitFraction` (`Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs`) from `2f / 3f` to `1f / 2f`. Press Play. Expected: the map camera viewport, the notes panel boundary, the legend panel, and the POI edit panel (open it by selecting a POI) all shift to the screen's horizontal midpoint together, with no scene data edited. Stop Play mode and change the constant back to `2f / 3f`.

- [ ] **Step 8: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs Assets/WorldGen/Rendering/MapLegendUI.cs Assets/WorldGen/Rendering/PoiEditPanel.cs
git commit -m "refactor: single source of truth for map/notes split fraction, remove dead mapAreaRoot"
```

---

## Post-implementation

1. Confirm the scene (`Assets/Scenes/SampleScene.unity`) no longer shows Inspector warnings about missing script fields for `NotesLayoutController`/`MapLegendUI`/`PoiEditPanel` (Unity typically just silently drops orphaned serialized data; if the Inspector shows anything unexpected, that's worth a second look but is not itself a code bug).
2. Confirm the visual layout is byte-for-byte the same as before the change (map 2/3, notes 1/3, legend/POI panel inside the map area) — this task changes architecture only, not appearance.
