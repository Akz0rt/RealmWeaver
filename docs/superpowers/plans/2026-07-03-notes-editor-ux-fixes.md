# Notes Editor UX/UI Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the notes editor's broken toolbar layout (root cause: an un-RectTransform'd wrapper GameObject) and do a first visual pass so the panel matches the app's existing dark/green theme, per `docs/superpowers/specs/2026-07-03-notes-editor-ux-fixes-design.md`.

**Architecture:** No new subsystems. This plan only edits existing `Assets/WorldGen/Notes/Rendering/*` files from the 2026-07-02 notes-editor plan, plus adds one new static helper (`NotesIconFactory`, parallel to the existing `PoiPlaceholderFactory` pattern) for runtime-drawn toolbar glyphs.

**Tech Stack:** Unity 2022.3 LTS, Built-in RP, New Input System, legacy `UnityEngine.UI` (no TextMeshPro), C#.

**Continues on branch:** `worktree-notes-editor` (already checked out at `.claude/worktrees/notes-editor` — no new worktree needed).

## Global Constraints

- **New Input System only** — `Mouse.current`/`Keyboard.current`. Never `UnityEngine.Input`. (No task here touches input handling, but don't regress it.)
- **No TextMeshPro** — `Text`/`Image`/`Button` from `UnityEngine.UI` only, builtin font (`Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`).
- **UI construction is code-only** — `new GameObject(...)`, `AddComponent<...>()`, manual `RectTransform` anchors. No Editor-authored prefabs.
- **Shared palette (from the spec) — use these exact values everywhere in this plan:**
  | Role | Color |
  |---|---|
  | Panel background | `(0, 0, 0, 0.7)` |
  | Text | `Color.white` |
  | Section header | `(0.7, 0.85, 1)` |
  | Active/selected state | `(0.2, 0.55, 0.3)` |
  | Inactive state | `(0.3, 0.3, 0.3)` |
  | Destructive action | `(0.55, 0.15, 0.15)` |
- **`[ContextMenu]` self-tests** for logic verifiable without manual interaction, matching project convention (`Debug.Log("Self-Test X: PASS")` or `FAIL` with a reason). Pure layout/visual changes have no self-test — verify by entering Play mode.
- **No placeholders in code** — every method has a real implementation.
- **Out of scope (do not touch):** resizable map/notes split, interaction/tool-routing logic, animated transitions, custom fonts/icon packs beyond simple runtime-drawn glyphs.

---

### Task 1: Fix the structural layout bug

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs:27-90` (`Awake`)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs:65-72` (dead `addGroupGO`)
- Modify: `Assets/WorldGen/Notes/Rendering/LinkView.cs:11` (class declaration)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs:27-37,58` (`Awake`, `RebuildFromPage`)

**Interfaces:**
- Consumes: `NotesToolbar.Initialize(CanvasInteractionController, Transform)`, `NotesTreeSidebar.Initialize(NotesDocumentController, Transform)`, `NotesCanvasController.RebuildFromPage(NotesPage)` — all pre-existing, signatures unchanged.
- Produces: a correctly-ordered `NotesRootBuilder.Awake()` (sidebar → toolbar → viewport, all direct children of `notesAreaGO` so `VerticalLayoutGroup` sizes all three), plus `NotesCanvasController.EnsureContainer()` (new private method) so `CanvasContainer` is only ever built once `viewport` is non-null. Tasks 2–5 render inside this corrected layout — no interface changes for them.

**Root cause recap:** `toolbarRowGO` was a bare `GameObject` (no `RectTransform`), so `notesAreaGO`'s `VerticalLayoutGroup` skipped it entirely, and the real toolbar row (added as *its* child later) inherited Unity's default bare-RectTransform values (`anchorMin=anchorMax=(0,0)`, `sizeDelta=(100,100)`) instead of stretching to the panel width.

**Additional finding #2 (discovered live during Play-mode testing of this plan):** a script recompile while already in Play Mode re-invokes `Awake()` on existing components. Since `NotesRootBuilder.Awake()` builds the entire notes UI imperatively via `new GameObject(...)` with no guard, every hot-reload during an active Play session stacked another full duplicate hierarchy (sidebar, toolbar, canvas, document with its own default group/page) on top of the previous one — the child GameObjects survive the reload, only re-running `Awake()`'s construction logic is new. Fixed with an early-return guard (`if (transform.childCount > 0) return;`) at the top of `Awake()`. **This only prevents future duplication — any duplicates already accumulated in a live Play session must be cleared with a full Stop → Play cycle, not another hot-reload.**

**Additional finding #1 (beyond the spec text, needed for this task's own verification to hold):** `NotesCanvasController.Awake()` builds `CanvasContainer` and parents it to `viewport` — but `viewport` is a public field assigned by `NotesRootBuilder` *after* `gameObject.AddComponent<NotesCanvasController>()` returns. Unity calls `Awake()` synchronously and immediately inside `AddComponent` when the GameObject is already active, so `viewport` is still `null` at that point and `CanvasContainer` silently parents to `transform` (`NotesRootBuilder`'s own bare GameObject, outside the `Canvas` entirely) — meaning note cards/images/drawings would never render at all, regardless of the toolbar fix. Fixed here by building the container lazily on first `RebuildFromPage` instead of in `Awake`.

- [ ] **Step 1: Rewrite `NotesRootBuilder.Awake()` — remove the wrapper, fix creation order**

Replace the entire `Awake()` method (lines 27-90) with:

```csharp
        void Awake()
        {
            // A script recompile while already in Play Mode re-invokes Awake() on existing
            // components, but this method builds the entire notes UI imperatively with
            // `new GameObject(...)` — without this guard, every such hot-reload would stack
            // another full duplicate hierarchy on top of the one already built (the child
            // GameObjects survive the reload; only re-running Awake() is new).
            if (transform.childCount > 0) return;

            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();

            var canvasGO = new GameObject("NotesCanvas");
            canvasGO.transform.SetParent(transform, false);
            var rootCanvas = canvasGO.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var notesAreaGO = new GameObject("NotesArea");
            notesAreaGO.transform.SetParent(canvasGO.transform, false);
            var notesAreaRect = notesAreaGO.AddComponent<RectTransform>();

            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.mapAreaRoot = mapAreaRoot;
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();

            var vLayout = notesAreaGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = true;

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            var sidebar = gameObject.AddComponent<NotesTreeSidebar>();
            sidebar.Initialize(DocumentController, notesAreaGO.transform);

            // Created before the viewport so CanvasInteractionController exists (as a component
            // reference) when NotesToolbar.Initialize wires button clicks to it; its dependent
            // fields (canvasController/viewportRect) are only read later, after they're assigned
            // below, never during this construction step.
            var undoManager = gameObject.AddComponent<NotesUndoManager>();
            var interaction = gameObject.AddComponent<CanvasInteractionController>();
            interaction.undoManager = undoManager;

            var toolbar = gameObject.AddComponent<NotesToolbar>();
            toolbar.Initialize(interaction, notesAreaGO.transform);

            var viewportGO = new GameObject("CanvasViewport");
            viewportGO.transform.SetParent(notesAreaGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            var viewportLE = viewportGO.AddComponent<LayoutElement>();
            viewportLE.flexibleHeight = 1f;

            CanvasController = gameObject.AddComponent<NotesCanvasController>();
            CanvasController.documentController = DocumentController;
            CanvasController.viewport = viewportRect;
            CanvasController.interactionController = interaction;

            interaction.canvasController = CanvasController;
            interaction.viewportRect = viewportRect;

            // NotesDocumentController.Awake() already opened its default page; render it now
            // rather than relying on subscription-order timing across components added this frame.
            CanvasController.RebuildFromPage(DocumentController.ActivePage);
        }
```

This changes sibling order under `notesAreaGO` to: `NotesTreeSidebar` (index 0) → `NotesToolbar` row, created inside `toolbar.Initialize` (index 1) → `CanvasViewport` (index 2) — matching the sidebar/toolbar/viewport visual stack, with every one of them a direct `RectTransform` child so `VerticalLayoutGroup` sizes all three correctly.

- [ ] **Step 2: Remove dead code in `NotesTreeSidebar.cs`**

Replace (lines 65-72):

```csharp
            var addGroupGO = new GameObject("AddGroupRow");
            addGroupGO.transform.SetParent(rootGO.transform, false);
            AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
                Rebuild();
            });
```

with:

```csharp
            AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
                Rebuild();
            });
```

(`addGroupGO` was created and parented but never referenced again — `AddSmallActionButton` creates its own `GameObject` parented straight to `rootGO.transform`.)

- [ ] **Step 3: Add `[RequireComponent(typeof(RectTransform))]` to `LinkView`**

Replace (line 11):

```csharp
    public class LinkView : MonoBehaviour
```

with:

```csharp
    [RequireComponent(typeof(RectTransform))]
    public class LinkView : MonoBehaviour
```

- [ ] **Step 4: Make `NotesCanvasController` build its container lazily**

Replace `Awake()` (lines 27-37):

```csharp
        void Awake()
        {
            var containerGO = new GameObject("CanvasContainer");
            containerGO.transform.SetParent(viewport != null ? viewport : transform, false);
            CanvasContainer = containerGO.AddComponent<RectTransform>();
            CanvasContainer.anchorMin = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchorMax = new Vector2(0.5f, 0.5f);
            CanvasContainer.pivot = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchoredPosition = Vector2.zero;
            CanvasContainer.sizeDelta = Vector2.zero;
        }
```

with:

```csharp
        void EnsureContainer()
        {
            if (CanvasContainer != null) return;
            var containerGO = new GameObject("CanvasContainer");
            containerGO.transform.SetParent(viewport != null ? viewport : transform, false);
            CanvasContainer = containerGO.AddComponent<RectTransform>();
            CanvasContainer.anchorMin = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchorMax = new Vector2(0.5f, 0.5f);
            CanvasContainer.pivot = new Vector2(0.5f, 0.5f);
            CanvasContainer.anchoredPosition = Vector2.zero;
            CanvasContainer.sizeDelta = Vector2.zero;
        }
```

Then in `RebuildFromPage` (line 58), add a call to it as the first line of the method:

```csharp
        public void RebuildFromPage(NotesPage page)
        {
            EnsureContainer();
            foreach (var view in objectViews.Values)
```

- [ ] **Step 5: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 6: Play-mode verify**

Press Play. Expected:
1. The toolbar renders as a row of 5 roughly-equal-width buttons spanning the notes panel's width (not a cramped ~100×100px cluster), positioned between the sidebar and the canvas viewport.
2. Click "Заметка", then click inside the (now visibly dark) canvas viewport → a note card appears **inside the visible viewport area** (not off-screen, not invisible).
3. Click "Курсор", drag the card → it moves; drag empty canvas → it pans.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs Assets/WorldGen/Notes/Rendering/LinkView.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs
git commit -m "fix: notes editor toolbar layout — remove non-RectTransform wrapper, fix child order, lazy-init canvas container"
```

---

### Task 2: Toolbar icons + hover tooltip

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs` (full rewrite)

**Interfaces:**
- Consumes: `NotesTool` enum (existing, from `CanvasInteractionController.cs`).
- Produces: `NotesIconFactory.GetIcon(NotesTool) → Sprite` (cached per tool). `NotesToolbar.Initialize` signature unchanged — still `(CanvasInteractionController, Transform)`.

- [ ] **Step 1: Create `NotesIconFactory.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Generates a 32x32 flat glyph sprite per NotesTool at first request and caches it,
    /// same runtime-drawn-texture technique as PoiPlaceholderFactory. No external assets.
    /// </summary>
    public static class NotesIconFactory
    {
        static readonly Dictionary<NotesTool, Sprite> cache = new Dictionary<NotesTool, Sprite>();

        public static Sprite GetIcon(NotesTool tool)
        {
            if (cache.TryGetValue(tool, out var cached)) return cached;
            var sprite = Build(tool);
            cache[tool] = sprite;
            return sprite;
        }

        static Sprite Build(NotesTool tool)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.name = $"NotesIcon_{tool}";

            var transparent = new Color32(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, transparent);

            switch (tool)
            {
                case NotesTool.Select: DrawCursor(tex, size); break;
                case NotesTool.Note: DrawNote(tex, size); break;
                case NotesTool.Link: DrawLink(tex, size); break;
                case NotesTool.Drawing: DrawPencil(tex, size); break;
                case NotesTool.Image: DrawPicture(tex, size); break;
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static void DrawCursor(Texture2D tex, int size)
        {
            var top = new Vector2(size * 0.22f, size * 0.85f);
            var tip = new Vector2(size * 0.22f, size * 0.15f);
            var side = new Vector2(size * 0.78f, size * 0.62f);
            FillTriangle(tex, size, top, tip, side, Color.white);
            var notchA = new Vector2(size * 0.42f, size * 0.62f);
            var notchB = new Vector2(size * 0.6f, size * 0.85f);
            DrawLine(tex, size, notchA, notchB, 2.5f, Color.white);
        }

        static void DrawNote(Texture2D tex, int size)
        {
            float m = size * 0.2f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            float fold = (max.x - min.x) * 0.35f;
            DrawLine(tex, size, new Vector2(max.x - fold, max.y), new Vector2(max.x, max.y - fold), 2f, Color.white);
        }

        static void DrawLink(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.2f, size * 0.2f);
            var to = new Vector2(size * 0.75f, size * 0.75f);
            DrawLine(tex, size, from, to, 2.5f, Color.white);
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.08f);
            Vector2 left = to - dir * (size * 0.06f) + perp * (size * 0.08f);
            Vector2 right = to - dir * (size * 0.06f) - perp * (size * 0.08f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }

        static void DrawPencil(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.25f, size * 0.8f);
            var to = new Vector2(size * 0.75f, size * 0.3f);
            DrawLine(tex, size, from, to, 4f, Color.white);
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.12f);
            Vector2 left = to + perp * (size * 0.05f);
            Vector2 right = to - perp * (size * 0.05f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }

        static void DrawPicture(Texture2D tex, int size)
        {
            float m = size * 0.18f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            FillCircle(tex, size, new Vector2(min.x + (max.x - min.x) * 0.3f, max.y - (max.y - min.y) * 0.28f), size * 0.07f, Color.white);
            var peak = new Vector2(min.x + (max.x - min.x) * 0.6f, min.y + (max.y - min.y) * 0.25f);
            var baseL = new Vector2(min.x + 2f, max.y - 2f);
            var baseR = new Vector2(max.x - 2f, max.y - 2f);
            FillTriangle(tex, size, peak, baseL, baseR, Color.white);
        }

        static void FillTriangle(Texture2D tex, int size, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (PointInTriangle(p, a, b, c))
                        tex.SetPixel(x, y, color);
                }
        }

        static void DrawLine(Texture2D tex, int size, Vector2 a, Vector2 b, float width, Color color)
        {
            float halfWidth = width * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (PointNearSegment(p, a, b, halfWidth))
                        tex.SetPixel(x, y, color);
                }
        }

        static void DrawRectOutline(Texture2D tex, int size, Vector2 min, Vector2 max, float thickness, Color color)
        {
            DrawLine(tex, size, new Vector2(min.x, min.y), new Vector2(max.x, min.y), thickness, color);
            DrawLine(tex, size, new Vector2(max.x, min.y), new Vector2(max.x, max.y), thickness, color);
            DrawLine(tex, size, new Vector2(max.x, max.y), new Vector2(min.x, max.y), thickness, color);
            DrawLine(tex, size, new Vector2(min.x, max.y), new Vector2(min.x, min.y), thickness, color);
        }

        static void FillCircle(Texture2D tex, int size, Vector2 center, float radius, Color color)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if ((p - center).sqrMagnitude <= radius * radius)
                        tex.SetPixel(x, y, color);
                }
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointNearSegment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 closest = a + ab * t;
            return (p - closest).magnitude <= halfWidth;
        }
    }
}
```

- [ ] **Step 2: Rewrite `NotesToolbar.cs`**

Replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of fixed-size icon buttons (Select/Note/Link/Drawing/Image) above the notes
    /// canvas. Clicking a button calls CanvasInteractionController.SetTool and highlights
    /// itself; hovering shows a floating Russian-label tooltip near the cursor. Tooltip
    /// visibility is driven by polling the cursor position each frame (not
    /// IPointerEnter/ExitHandler) — more robust against uGUI event-ordering edge cases
    /// between adjacent buttons, matching CanvasInteractionController's existing
    /// polling-based input pattern.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public const float ButtonSize = 36f;
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f);
        public Color inactiveColor = new Color(0.3f, 0.3f, 0.3f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;
        Canvas rootCanvas;
        RectTransform tooltipRect;
        Text tooltipText;

        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Link, "Связь"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
        };

        public void Initialize(CanvasInteractionController interactionController, Transform parent)
        {
            controller = interactionController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rootCanvas = parent.GetComponentInParent<Canvas>();

            var rowGO = new GameObject("NotesToolbar");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.padding = new RectOffset(6, 6, 4, 4);
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleLeft;
            rowGO.AddComponent<LayoutElement>().preferredHeight = ButtonSize + 8f;

            BuildTooltip(rootCanvas.transform);

            buttons = new Button[ToolDefs.Length];
            for (int i = 0; i < ToolDefs.Length; i++)
            {
                int index = i;
                var (tool, label) = ToolDefs[i];

                var btnGO = new GameObject($"Tool_{tool}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var btnRect = btnGO.AddComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
                var le = btnGO.AddComponent<LayoutElement>();
                le.preferredWidth = ButtonSize;
                le.preferredHeight = ButtonSize;

                var img = btnGO.AddComponent<Image>();
                img.color = inactiveColor;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActive(tool));
                buttons[index] = btn;

                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(btnGO.transform, false);
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = NotesIconFactory.GetIcon(tool);
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                var iconRect = iconImg.rectTransform;
                iconRect.anchorMin = new Vector2(0.15f, 0.15f);
                iconRect.anchorMax = new Vector2(0.85f, 0.85f);
                iconRect.sizeDelta = Vector2.zero;

            }

            SetActive(NotesTool.Select);
        }

        void Update()
        {
            if (Mouse.current == null) return;
            var screenPos = Mouse.current.position.ReadValue();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)buttons[i].transform, screenPos, null))
                {
                    ShowTooltip(ToolDefs[i].label, screenPos);
                    return;
                }
            }
            HideTooltip();
        }

        void BuildTooltip(Transform canvasRoot)
        {
            var tooltipGO = new GameObject("Tooltip");
            tooltipGO.transform.SetParent(canvasRoot, false);
            var img = tooltipGO.AddComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
            img.raycastTarget = false;
            tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);
            tooltipRect.sizeDelta = new Vector2(90f, 20f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(tooltipGO.transform, false);
            tooltipText = textGO.AddComponent<Text>();
            tooltipText.font = builtinFont;
            tooltipText.fontSize = 11;
            tooltipText.color = Color.white;
            tooltipText.alignment = TextAnchor.MiddleCenter;
            tooltipText.raycastTarget = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            tooltipGO.SetActive(false);
        }

        void ShowTooltip(string label, Vector2 screenPos)
        {
            tooltipText.text = label;
            tooltipRect.gameObject.SetActive(true);
            var canvasRect = (RectTransform)tooltipRect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local);
            tooltipRect.anchoredPosition = local + new Vector2(12f, -12f);
        }

        void HideTooltip()
        {
            tooltipRect.gameObject.SetActive(false);
        }

        void SetActive(NotesTool tool)
        {
            controller.SetTool(tool);
            for (int i = 0; i < ToolDefs.Length; i++)
                buttons[i].GetComponent<Image>().color = ToolDefs[i].tool == tool ? activeColor : inactiveColor;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Toolbar — Icon Caching")]
        public void SelfTestIconCaching()
        {
            bool ok = true;
            foreach (NotesTool tool in System.Enum.GetValues(typeof(NotesTool)))
            {
                var a = NotesIconFactory.GetIcon(tool);
                var b = NotesIconFactory.GetIcon(tool);
                if (a == null || !ReferenceEquals(a, b)) { ok = false; break; }
            }
            Debug.Log(ok
                ? "Self-Test Notes Toolbar — Icon Caching: PASS"
                : "Self-Test Notes Toolbar — Icon Caching: FAIL (icon missing or not cached for some tool)");
        }
    }
}
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 4: Self-test**

Enter Play mode. In the Inspector, find the `NotesToolbar` component (on the `NotesRootBuilder` GameObject) → right-click its header → **Self-Test: Notes Toolbar — Icon Caching**. Expected Console output: `Self-Test Notes Toolbar — Icon Caching: PASS`.

- [ ] **Step 5: Play-mode verify**

Press Play. Expected:
1. Toolbar shows 5 fixed 36×36px square buttons with distinct flat-white glyphs (arrow cursor, note-with-folded-corner, arrow link, pencil, picture frame) — no text labels, no word-wrap.
2. Hovering any button shows a small dark tooltip near the cursor with its Russian name (Курсор/Заметка/Связь/Рисунок/Изображение); moving off the button hides it immediately.
3. Clicking a button highlights it green and un-highlights the previous one (gray).

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs Assets/WorldGen/Notes/Rendering/NotesToolbar.cs
git commit -m "feat: notes toolbar — runtime-drawn icon buttons + hover tooltips"
```

---

### Task 3: Note card dark restyle

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NoteCardView.cs:44-45,53,80`

**Interfaces:**
- Consumes/produces: none — visual-only, no signature changes.

- [ ] **Step 1: Dark card background**

Replace (lines 44-45):

```csharp
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.9f, 0.6f, 0.95f);
```

with:

```csharp
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.2f, 0.95f);
```

- [ ] **Step 2: White title text**

Replace (line 53):

```csharp
            titleText.color = Color.black;
```

with:

```csharp
            titleText.color = Color.white;
```

- [ ] **Step 3: White body text**

Replace (line 80):

```csharp
            bodyText.color = Color.black;
```

with:

```csharp
            bodyText.color = Color.white;
```

- [ ] **Step 4: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 5: Play-mode verify**

Press Play, click "Заметка", click the canvas. Expected: new card has a dark background (a shade lighter than the black canvas, matching the rest of the app) with legible white title/body text — no yellow sticky-note look.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NoteCardView.cs
git commit -m "style: note cards — dark card background matching app theme"
```

---

### Task 4: Page tree sidebar restyle

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs:107-113,144-152`

**Interfaces:**
- Consumes/produces: none — visual-only, no signature changes.

**Additional finding #3 (discovered live during Play-mode testing):** `CreateGroup` and `CreatePage` (in `NotesDocumentController`, from the 2026-07-02 plan) each independently fire `OnDocumentChanged`. Any action that calls both in sequence — e.g. the "+ Группа" button, which does `CreateGroup(...)` then `CreatePage(...)` — fired `Rebuild()` twice (plus a third redundant explicit call that was also present) within a single frame. Since `Rebuild()` destroys old rows via the deferred `Destroy()`, multiple synchronous passes within one frame left old-and-new rows alive simultaneously for that frame, rendering as overlapping/garbled UI (confirmed via Hierarchy screenshots showing a correct single-copy structure while the Game view showed doubled text/rows/toolbar for that frame). Fixed by: (1) removing the redundant explicit `Rebuild()` calls after `CreateGroup`/`CreatePage` in both the "+ Группа" and "+ Страница" handlers (they already trigger a rebuild via `OnDocumentChanged`), and (2) debouncing `Rebuild()` itself — `OnDocumentChanged` now calls `RequestRebuild()` (sets a `rebuildPending` flag) instead of `Rebuild()` directly, and a new `LateUpdate()` performs the actual `Rebuild()` at most once per frame, only if `rebuildPending` is set. The page-row click handler's explicit `Rebuild()` (needed to refresh the active-page highlight after `OpenPage`, which doesn't fire `OnDocumentChanged`) is unaffected and still runs immediately.

**Additional finding #4:** even a single `Rebuild()` call still showed the overlap, because `Destroy()` is deferred to end-of-frame while the newly-built rows render immediately — for at least one frame, the about-to-be-destroyed old rows and the brand-new rows both exist and both render, which a screenshot taken right after a click reliably captures. Fixed by calling `child.SetActive(false)` immediately before `Destroy(child)` in `Rebuild()`'s cleanup loop, so old rows stop rendering the instant `Rebuild()` runs instead of lingering until the deferred destroy completes.

**Additional finding #5 (the actual root cause, confirmed live in-Editor, not just in screenshots):** even after the above, doubled/overlapping rendering of both the sidebar rows and the (never-rebuilt) `NotesToolbar` persisted — live, while paused, and regardless of render pipeline (reproduced under both URP and Built-in RP, ruling those out; also reproduced on the toolbar from pure mouse-hover with no `Rebuild()` involved at all, ruling out `Rebuild()` as the sole trigger). Forcing a full Canvas relayout (resizing the Game view) made it render correctly again every time, which pinpoints a stale `CanvasRenderer`-cached-geometry issue: rapid runtime mutation of `Text`/`RectTransform` (via `Rebuild()`'s Destroy/create churn, or `NotesToolbar`'s per-frame tooltip repositioning) left Unity's Canvas batching rendering old geometry alongside the new state until something forced a full rebuild. `Canvas.ForceUpdateCanvases()` alone (queues/flushes pending *layout* rebuilds) was insufficient, and neither was toggling `Canvas.enabled` or the whole Canvas GameObject's active state — and the bug was confirmed to reproduce identically in a Standalone build (not just the Editor), ruling out any Editor/Game-view-only explanation.

**Additional finding #6 (disproven — kept for the record):** theorized the doubling was a shared-font-atlas rebuild artifact (`Font.RequestCharactersInTexture`) and added a `FontPrewarmer.cs` pre-warming every glyph/size/style up front. User re-tested: no change. Later, `MapLegendUI` — which shares the exact same `Font` instance and goes through its own rebuild cycle — was confirmed to stay visually clean throughout, which rules out the font atlas theory entirely. `FontPrewarmer.cs` has been deleted; it was never load-bearing.

**Additional finding #7 (the actual root cause, confirmed by the user — supersedes #5 and #6):** `NotesLayoutController.Apply()` sets `mapCamera.rect = new Rect(0f, 0f, splitFraction, 1f)`, clamping the map camera's viewport to the left ⅔ of the screen so the 3D map doesn't render under the notes panel. Unity cameras only clear (Skybox/Solid Color Clear Flags) *within their own viewport rect* — nothing ever clears the right ⅓ of the screen where the notes panel lives. A Screen Space Overlay `Canvas` only paints pixels currently covered by an active `Graphic`; any gap around/between elements (`HorizontalLayoutGroup` spacing in the toolbar, freed space when the sidebar list shrinks, the footprint left behind when the tooltip's `CanvasGroup.alpha` drops to 0) was left showing whatever the *previous* frame put there — read as "duplicated/ghosted" UI — until something forced a full backbuffer reallocation (a window resize), which is exactly the one fix that reliably worked every time. It reproduced identically under both render pipelines and in a Standalone build because the bug is genuine screen-clearing behavior, not an Editor artifact. It didn't reproduce in the `BisectionTest.cs` bisection scene because that test never touched `mapCamera`/`NotesLayoutController` — its camera kept its default full-screen viewport and cleared normally every frame.

**Fix:** `NotesRootBuilder.Awake()` now adds a full-bleed opaque `Image` directly on `notesAreaGO` (`new Color(0.12f, 0.12f, 0.14f, 1f)`), sitting behind the sidebar/toolbar/viewport. Screen Space Overlay Canvases redraw every enabled `Graphic` every frame regardless of what else changed, so this background unconditionally overwrites any stale pixels in the uncleared screen region — confirmed by the user: duplication is gone with no resize needed. As part of the same cleanup pass: removed the (now unconfirmed-necessary) `LayoutRebuilder.ForceRebuildLayoutImmediate` call and now-unused `panelRect` field from `NotesTreeSidebar.Rebuild()`; corrected the misleading "Canvas UI-batching issue" comment on `NotesToolbar`'s tooltip `CanvasGroup` (the `CanvasGroup`-instead-of-`SetActive` approach itself is kept — it's a reasonable, harmless simplification); deleted `BisectionTest.cs` (temporary diagnostic scene, no longer needed) and `FontPrewarmer.cs` (disproven theory, not load-bearing).

**Additional finding #8 (page switching never re-rendered the canvas):** `NotesCanvasController` subscribed to `documentController.OnActivePageChanged` in `OnEnable()`, but `NotesRootBuilder.Awake()` calls `gameObject.AddComponent<NotesCanvasController>()` — which runs `Awake`/`OnEnable` synchronously — *before* the next line assigns `CanvasController.documentController`. `OnEnable`'s null-check on `documentController` silently failed and the subscription never happened, for the lifetime of the component. Every subsequent sidebar page click called `OpenPage` → fired the event → nobody was listening → the canvas kept showing whatever page had rendered first. Same root-cause category as finding #1, just for an event subscription instead of a parent reference. Fixed by replacing the public-field-plus-`OnEnable`pattern with an explicit `Initialize(NotesDocumentController, RectTransform, CanvasInteractionController)` method (matching `NotesToolbar`/`NotesTreeSidebar`'s existing convention) that assigns the fields *and* subscribes in the same call, invoked by `NotesRootBuilder` right after `AddComponent<NotesCanvasController>()`.

**Additional finding #9 (Delete was never wired to anything):** `NotesUndoManager.RequestDeleteObject` existed and worked, but no code path called it — no keyboard handler, no button, and no concept of a "currently selected object" existed for `NotesTool.Select` at all (`CanvasInteractionController.HandleObjectClicked` only handled `NotesTool.Link`). This was a gap in the original 2026-07-02 plan: its own spec table lists "Delete key / delete button" and "Select: click to select an object", neither of which had been implemented. Fixed by adding a `selectedObjectId` field to `CanvasInteractionController`, set on a Select-tool object click (`HandleObjectClicked`) or drag-end (`HandleObjectDragEnded`), cleared on an empty-canvas click; a new `HandleDeleteKey()` (polled from `Update()`, matching the class's existing input style) calls `undoManager.RequestDeleteObject` for the selected object when `Keyboard.current.deleteKey.wasPressedThisFrame`.

**Additional finding #10 (dragging an object also panned the canvas underneath it):** `CanvasInteractionController.HandlePress()` unconditionally started a canvas pan on any mouse-down inside the viewport while the Select tool was active, with no check for whether the press landed on an object. `NoteCardView`/`ImageObjectView`/`DrawingObjectView` separately handle their own dragging via Unity's `IPointerDownHandler`/`IDragHandler` (routed through the `EventSystem`, independent of `CanvasInteractionController`'s manual polling), so dragging a card triggered both the card's own move *and* a canvas pan in the same gesture, fighting each other. Fixed by adding `NotesCanvasController.IsScreenPointOverObject(Vector2, Camera)` (reusing the same view→`RectTransform` lookup as the existing `GetRectTransform`/`FindDrawingObjectAt`) and skipping the pan-start in `HandlePress` when the press lands on an object, leaving it to the object's own drag handling.

- [ ] **Step 1: Bigger group title row**

Replace (lines 107-113):

```csharp
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 12;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleGO.AddComponent<LayoutElement>().preferredHeight = 18f;
```

with:

```csharp
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleGO.AddComponent<LayoutElement>().preferredHeight = 30f;
```

- [ ] **Step 2: Bigger page rows with real indent padding (no more literal leading spaces)**

Replace (lines 144-152):

```csharp
            text.text = $"   • {page.Name}";
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
```

with:

```csharp
            text.text = $"• {page.Name}";
            text.font = builtinFont;
            text.fontSize = 13;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = Vector2.zero;
```

Also update the page row's height so the bigger 13pt text isn't cramped — replace, in `BuildPageRow` (a few lines above the text block):

```csharp
            rowGO.AddComponent<LayoutElement>().preferredHeight = 18f;
```

with:

```csharp
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 4: Play-mode verify**

Press Play. Expected: group and page rows in the sidebar are visibly taller (30px) with 13pt text; page rows show a real `• PageName` indent (no triple-space hack) that lines up consistently under their group.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "style: page tree sidebar — bigger rows/font, real indent padding"
```

---

### Task 5: Delete-confirm dialog restyle

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs:156-215`

**Interfaces:**
- Consumes/produces: `AddDialogButton` gains a `Color bgColor` parameter (internal to this file only — its only two call sites are updated in the same step).

- [ ] **Step 1: Panel background → shared palette**

Replace (line 157):

```csharp
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
```

with:

```csharp
            panelImg.color = new Color(0f, 0f, 0f, 0.7f);
```

- [ ] **Step 2: Pass a color into each dialog button**

Replace (lines 177-186):

```csharp
            AddDialogButton(panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(false);
            });
            AddDialogButton(panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(true);
            });
```

with:

```csharp
            AddDialogButton(panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(false);
            });
            AddDialogButton(panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), new Color(0.55f, 0.15f, 0.15f), () =>
            {
                Destroy(confirmDialogGO);
                onResult(true);
            });
```

- [ ] **Step 3: `AddDialogButton` takes the color instead of hardcoding gray**

Replace (lines 189-201, the method signature and its `img.color` line):

```csharp
        void AddDialogButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
```

with:

```csharp
        void AddDialogButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = bgColor;
```

- [ ] **Step 4: Verify compilation**

Open Unity. Expected: no Console errors.

- [ ] **Step 5: Self-test regression check**

Enter Play mode. On the `NotesUndoManager` component, right-click → **Self-Test: Notes Undo — Create/Undo Card**. Expected: `Self-Test Notes Undo — Create/Undo Card: PASS` (this task changes only colors, so the existing logic self-test must still pass unchanged).

- [ ] **Step 6: Play-mode verify**

Create any object, select it, trigger delete. Expected: confirm dialog shows a black/70%-opacity panel, white message text, gray "Отмена" button, and a muted-red "Удалить" button — no more ad-hoc uniform gray buttons.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "style: delete-confirm dialog — shared panel palette, red destructive button"
```

---

## Post-implementation

Run all self-tests (Play mode, right-click each component → the listed menu item):
- `NotesDocumentController` → **Self-Test: Notes Document CRUD** → `PASS` (pre-existing, must still pass)
- `NotesUndoManager` → **Self-Test: Notes Undo — Create/Undo Card** → `PASS` (pre-existing, must still pass)
- `NotesToolbar` → **Self-Test: Notes Toolbar — Icon Caching** → `PASS` (new, Task 2)

Then repeat the full end-to-end flow from the original 2026-07-02 plan's Task 12 Step 5 (create card/drawing/link, drag, delete+confirm, sidebar group/page navigation, POI "Открыть страницы" link, window resize) — everything there should still work, now inside a correctly-laid-out, on-theme panel.
