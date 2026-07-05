# Notes Toolbar Redesign + Icon Glyph Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the 5 procedurally-drawn toolbar icon glyphs to actually read as their intended shapes, and turn the tool toolbar from an opaque row that reserves its own layout strip into a borderless floating overlay (active/hover shown via a circular backdrop) that lets the canvas use the full height of the notes panel's right column.

**Architecture:** `NotesIconFactory.cs`'s `Draw*` methods get corrected/enlarged coordinates (texture convention: `y=0` is the bottom row, confirmed empirically — no render-side flip exists anywhere in this pipeline). `NotesToolbar.cs`'s per-button `Image` switches from an always-visible colored square to a shared cached circular sprite that's fully transparent by default, tinted green when that tool is active or dim white on hover (tracked via a new `activeTool` field and a `RefreshButtonVisuals()` method called from both `SetActive()` and the existing hover-tracking `Update()`), with the button's own row repositioned to float via RectTransform anchors instead of a parent `LayoutElement`. `NotesRootBuilder.cs`'s `RightColumn` drops its `VerticalLayoutGroup` (which used to stack Toolbar-then-Viewport) — `CanvasViewport` now stretches to fill `RightColumn` via anchors, and the toolbar is constructed (and thus parented) after it, so it renders and receives clicks on top instead of being clipped by the viewport's `RectMask2D`.

**Tech Stack:** Unity 6000.3.2f1, Built-in Render Pipeline, legacy `UnityEngine.UI` (no TextMeshPro), code-only UI construction (`new GameObject()` + `AddComponent<>()`).

## Global Constraints

- No automated Unity test runner exists in this project. Verification is via the codebase's established `[ContextMenu("Self-Test: ...")]` method pattern (see `NotesToolbar.SelfTestIconCaching`, `NotesTreeSidebar.SelfTestCollapseToggle`) plus manual Play-mode testing performed by the user — the implementer has no direct Unity Editor access.
- Toolbar stays scoped to the canvas (right column only) — unaffected by this plan, already true today.
- Out of scope (do not implement): sidebar rename/delete/search (next spec in sequence), user-draggable/resizable panel splits (spec after that), any change to tool behavior itself (`CanvasInteractionController.SetTool`, drawing/note/image/link/zoom functionality).

---

### Task 1: Fix icon glyph geometry in `NotesIconFactory.cs`

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs:47-56` (`DrawCursor`), `:68-79` (`DrawPencil`), `:81-92` (`DrawPicture`), `:94-105` (`DrawZoom`)

**Interfaces:**
- Consumes: nothing new — `Build(NotesTool)` and `GetIcon(NotesTool)` (the public API other files call) keep their exact signatures; only the private `Draw*` method bodies change.
- Produces: no interface change. `DrawNote` (lines 58-66) is untouched — already confirmed correct.

- [ ] **Step 1: Fix `DrawCursor` — the notch was drawing white-on-white (a no-op)**

Open `Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs`. Replace lines 47-56:

```csharp
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
```

with:

```csharp
        static void DrawCursor(Texture2D tex, int size)
        {
            var top = new Vector2(size * 0.22f, size * 0.85f);
            var tip = new Vector2(size * 0.22f, size * 0.15f);
            var side = new Vector2(size * 0.78f, size * 0.62f);
            FillTriangle(tex, size, top, tip, side, Color.white);
            // Notch must be drawn transparent, not white — it's cutting the tail flag out of
            // the silhouette above, same technique DrawZoom uses to punch its ring. The
            // previous Color.white here painted white over an already-white triangle, so the
            // notch never actually appeared (silhouette rendered as a plain solid blob).
            var notchA = new Vector2(size * 0.42f, size * 0.62f);
            var notchB = new Vector2(size * 0.6f, size * 0.85f);
            DrawLine(tex, size, notchA, notchB, 3f, Color.clear);
        }
```

- [ ] **Step 2: Enlarge `DrawPencil`'s arrowhead — too small to read at 32px**

Replace lines 68-79:

```csharp
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
```

with:

```csharp
        static void DrawPencil(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.25f, size * 0.8f);
            var to = new Vector2(size * 0.75f, size * 0.3f);
            DrawLine(tex, size, from, to, 4f, Color.white);
            // Pointed tip is the bottom-right end (continuing past `to` in the same
            // direction as the shaft) — enlarged from the original 0.12/0.05 fractions,
            // which produced a ~4px/~2px arrowhead too small to read as a point at 32px.
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.22f);
            Vector2 left = to + perp * (size * 0.09f);
            Vector2 right = to - perp * (size * 0.09f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }
```

- [ ] **Step 3: Fix `DrawPicture` — mountain peak/base were swapped**

Replace lines 81-92:

```csharp
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
```

with:

```csharp
        static void DrawPicture(Texture2D tex, int size)
        {
            float m = size * 0.18f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            FillCircle(tex, size, new Vector2(min.x + (max.x - min.x) * 0.3f, max.y - (max.y - min.y) * 0.28f), size * 0.07f, Color.white);
            // Peak near max.y (top of the frame), base corners near min.y (bottom) — the
            // previous version had these backwards (peak near min.y, base near max.y),
            // drawing the mountain upside-down (a "V" instead of a "/\").
            var peak = new Vector2(min.x + (max.x - min.x) * 0.6f, max.y - (max.y - min.y) * 0.25f);
            var baseL = new Vector2(min.x + 2f, min.y + 2f);
            var baseR = new Vector2(max.x - 2f, min.y + 2f);
            FillTriangle(tex, size, peak, baseL, baseR, Color.white);
        }
```

- [ ] **Step 4: Enlarge `DrawZoom`'s lens/handle for legibility**

Replace lines 94-105:

```csharp
        static void DrawZoom(Texture2D tex, int size)
        {
            var center = new Vector2(size * 0.42f, size * 0.58f);
            float radius = size * 0.2f;
            float thickness = 2.5f;
            FillCircle(tex, size, center, radius, Color.white);
            FillCircle(tex, size, center, radius - thickness, new Color32(0, 0, 0, 0));
            Vector2 handleDir = new Vector2(0.7f, -0.7f).normalized;
            var handleStart = center + handleDir * radius;
            var handleEnd = center + handleDir * (radius + size * 0.28f);
            DrawLine(tex, size, handleStart, handleEnd, 3.5f, Color.white);
        }
```

with:

```csharp
        static void DrawZoom(Texture2D tex, int size)
        {
            // Lens upper-left, handle extending down-right out of the ring — standard
            // magnifying-glass orientation. Radius/thickness/handle length enlarged from the
            // original (0.2/2.5/0.28) for legibility at 32px.
            var center = new Vector2(size * 0.42f, size * 0.58f);
            float radius = size * 0.22f;
            float thickness = 3f;
            FillCircle(tex, size, center, radius, Color.white);
            FillCircle(tex, size, center, radius - thickness, Color.clear);
            Vector2 handleDir = new Vector2(0.7f, -0.7f).normalized;
            var handleStart = center + handleDir * radius;
            var handleEnd = center + handleDir * (radius + size * 0.34f);
            DrawLine(tex, size, handleStart, handleEnd, 4f, Color.white);
        }
```

- [ ] **Step 5: Verify no other `Draw*` method references remain stale**

Run:
```bash
grep -n "Color32(0, 0, 0, 0)\|Color.clear" "Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs"
```
Expected: `Color.clear` appears twice (the new `DrawCursor` notch, the new `DrawZoom` ring punch); the top-of-file `Build()` method's own `Color32(0,0,0,0)` initial clear-fill (unrelated, untouched by this task) still appears once.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesIconFactory.cs
git commit -m "fix: correct upside-down/illegible notes toolbar icon glyphs"
```

---

### Task 2: Borderless toolbar with active/hover circular backdrop (`NotesToolbar.cs`)

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs` (full rewrite)

**Interfaces:**
- Consumes: `NotesIconFactory.GetIcon(NotesTool)` (unchanged, from Task 1's file — signature untouched), `CanvasInteractionController.SetTool(NotesTool)` (unchanged, external).
- Produces: `NotesToolbar.Initialize(CanvasInteractionController, Transform parent)` — **signature unchanged**, Task 3 depends on this. The row `GameObject` this creates is now self-sized (via `ContentSizeFitter`) and anchored to its parent's top-left corner instead of relying on a `LayoutElement.preferredHeight` set by a parent layout group — Task 3's `NotesRootBuilder.cs` changes rely on this (no longer needs to give `RightColumn` a `VerticalLayoutGroup` to make room for the toolbar).

- [ ] **Step 1: Replace the entire contents of `NotesToolbar.cs`**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of borderless icon buttons (Select/Note/Drawing/Image/Zoom) floating over the
    /// notes canvas. Clicking a button calls CanvasInteractionController.SetTool and shows a
    /// circular backdrop behind its icon; hovering shows a dimmer backdrop plus a floating
    /// Russian-label tooltip near the cursor.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public const float ButtonSize = 36f;
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f, 0.65f);
        public Color hoverColor = new Color(1f, 1f, 1f, 0.15f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;
        Canvas rootCanvas;
        RectTransform tooltipRect;
        Text tooltipText;
        CanvasGroup tooltipGroup;
        int hoveredIndex = -1;
        NotesTool activeTool = NotesTool.Select;

        static Sprite cachedBackdropSprite;

        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
            (NotesTool.Zoom, "Лупа"),
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
            var fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Floats over the canvas viewport instead of reserving its own row in a stacked
            // layout — this GameObject has no LayoutGroup parent controlling it (RightColumn
            // no longer stacks Toolbar+Viewport, see NotesRootBuilder), so its anchor
            // (top-left corner) is set directly here; its size comes from ContentSizeFitter
            // reading its own HorizontalLayoutGroup's computed preferred size above.
            var rowRect = (RectTransform)rowGO.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = Vector2.zero;

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
                img.sprite = GetBackdropSprite();
                img.color = Color.clear;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                // This class manages the backdrop color itself (active/hover/neither) — the
                // default ColorTint transition would otherwise fight that by re-tinting
                // targetGraphic.color on every pointer enter/exit/click.
                btn.transition = Selectable.Transition.None;
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

            int newHoveredIndex = -1;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)buttons[i].transform, screenPos, null))
                {
                    newHoveredIndex = i;
                    break;
                }
            }

            // Only touch the tooltip/backdrops when the hovered button actually changes, not
            // every frame — no need to keep re-setting the same state 60 times a second.
            if (newHoveredIndex == hoveredIndex) return;
            hoveredIndex = newHoveredIndex;
            RefreshButtonVisuals();

            if (hoveredIndex >= 0)
                ShowTooltip(ToolDefs[hoveredIndex].label, screenPos);
            else
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

            // Stays active for the tooltip's entire lifetime — visibility is controlled via
            // CanvasGroup.alpha instead of GameObject.SetActive so it keeps being redrawn by
            // the Canvas every frame (see NotesRootBuilder's notesAreaBg comment for why that
            // matters here) rather than needing an OnEnable-time relayout each time it appears.
            tooltipGroup = tooltipGO.AddComponent<CanvasGroup>();
            tooltipGroup.alpha = 0f;
            tooltipGroup.blocksRaycasts = false;
            tooltipGroup.interactable = false;
        }

        void ShowTooltip(string label, Vector2 screenPos)
        {
            tooltipText.text = label;
            tooltipGroup.alpha = 1f;
            var canvasRect = (RectTransform)tooltipRect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local);
            tooltipRect.anchoredPosition = local + new Vector2(12f, -12f);
        }

        void HideTooltip()
        {
            tooltipGroup.alpha = 0f;
        }

        void SetActive(NotesTool tool)
        {
            controller.SetTool(tool);
            activeTool = tool;
            RefreshButtonVisuals();
        }

        void RefreshButtonVisuals()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].GetComponent<Image>();
                if (ToolDefs[i].tool == activeTool)
                    img.color = activeColor;
                else if (i == hoveredIndex)
                    img.color = hoverColor;
                else
                    img.color = Color.clear;
            }
        }

        static Sprite GetBackdropSprite()
        {
            if (cachedBackdropSprite != null) return cachedBackdropSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "NotesToolbarBackdrop";

            var center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = (p - center).sqrMagnitude <= radius * radius;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            tex.Apply();

            cachedBackdropSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return cachedBackdropSprite;
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

        [ContextMenu("Self-Test: Notes Toolbar — Active/Hover Backdrop")]
        public void SelfTestActiveHoverBackdrop()
        {
            if (buttons == null)
            {
                Debug.Log("Self-Test Notes Toolbar — Active/Hover Backdrop: FAIL (not initialized — enter Play Mode first)");
                return;
            }

            bool ok = true;
            string reason = "";

            SetActive(NotesTool.Select);
            if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
            { ok = false; reason = "expected Select button to show activeColor after SetActive(Select)"; }
            if (ok && !ColorsApproximatelyEqual(buttons[1].GetComponent<Image>().color, Color.clear))
            { ok = false; reason = "expected non-active button to be Color.clear"; }

            if (ok)
            {
                hoveredIndex = 1;
                RefreshButtonVisuals();
                if (!ColorsApproximatelyEqual(buttons[1].GetComponent<Image>().color, hoverColor))
                { ok = false; reason = "expected hovered non-active button to show hoverColor"; }
                else if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
                { ok = false; reason = "expected active button to stay activeColor while a different button is hovered"; }
            }

            if (ok)
            {
                hoveredIndex = 0;
                RefreshButtonVisuals();
                if (!ColorsApproximatelyEqual(buttons[0].GetComponent<Image>().color, activeColor))
                { ok = false; reason = "expected active button to stay activeColor (not hoverColor) when hovered"; }
            }

            hoveredIndex = -1;
            RefreshButtonVisuals();

            Debug.Log(ok
                ? "Self-Test Notes Toolbar — Active/Hover Backdrop: PASS"
                : $"Self-Test Notes Toolbar — Active/Hover Backdrop: FAIL ({reason})");
        }

        static bool ColorsApproximatelyEqual(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) &&
            Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
    }
}
```

- [ ] **Step 2: Verify no leftover references to the removed `inactiveColor` field**

Run:
```bash
grep -rn "inactiveColor" Assets/WorldGen/Notes/
```
Expected: no matches (the field is removed; nothing else in the codebase referenced it).

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesToolbar.cs
git commit -m "feat: borderless notes toolbar with active/hover circular backdrop"
```

---

### Task 3: Float the toolbar over a full-height canvas viewport (`NotesRootBuilder.cs`)

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs:82-115`

**Interfaces:**
- Consumes: `NotesToolbar.Initialize(CanvasInteractionController, Transform parent)` (unchanged signature, from Task 2) — now relies on the toolbar being constructed **after** the viewport so it ends up as the later (topmost-rendered, topmost-raycast) sibling under `RightColumn`.
- Produces: no public API change — `CanvasController`, `DocumentController` properties and `CanvasController.Initialize(...)` call are unaffected.

- [ ] **Step 1: Remove `RightColumn`'s `VerticalLayoutGroup`, stretch the viewport, reorder toolbar after it**

Open `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`. Replace lines 82-115:

```csharp
            var rightColumnGO = new GameObject("RightColumn");
            rightColumnGO.transform.SetParent(notesAreaGO.transform, false);
            var rightColumnVLayout = rightColumnGO.AddComponent<VerticalLayoutGroup>();
            rightColumnVLayout.childControlWidth = true;
            rightColumnVLayout.childForceExpandWidth = true;
            rightColumnVLayout.childControlHeight = true;
            rightColumnVLayout.childForceExpandHeight = true;
            rightColumnGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Created before the viewport so CanvasInteractionController exists (as a component
            // reference) when NotesToolbar.Initialize wires button clicks to it; its dependent
            // fields (canvasController/viewportRect) are only read later, after they're assigned
            // below, never during this construction step.
            var undoManager = gameObject.AddComponent<NotesUndoManager>();
            var interaction = gameObject.AddComponent<CanvasInteractionController>();
            interaction.undoManager = undoManager;

            var toolbar = gameObject.AddComponent<NotesToolbar>();
            toolbar.Initialize(interaction, rightColumnGO.transform);

            var viewportGO = new GameObject("CanvasViewport");
            viewportGO.transform.SetParent(rightColumnGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            var viewportLE = viewportGO.AddComponent<LayoutElement>();
            viewportLE.flexibleHeight = 1f;

            CanvasController = gameObject.AddComponent<NotesCanvasController>();
            CanvasController.Initialize(DocumentController, viewportRect, interaction);

            interaction.canvasController = CanvasController;
            interaction.viewportRect = viewportRect;
```

with:

```csharp
            var rightColumnGO = new GameObject("RightColumn");
            rightColumnGO.transform.SetParent(notesAreaGO.transform, false);
            rightColumnGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Created before the viewport so CanvasInteractionController exists (as a component
            // reference) when NotesToolbar.Initialize wires button clicks to it; its dependent
            // fields (canvasController/viewportRect) are only read later, after they're assigned
            // below, never during this construction step.
            var undoManager = gameObject.AddComponent<NotesUndoManager>();
            var interaction = gameObject.AddComponent<CanvasInteractionController>();
            interaction.undoManager = undoManager;

            // CanvasViewport is created (and parented) before the toolbar so it's the
            // back-most sibling under RightColumn — NotesToolbar.Initialize (below) parents
            // its floating row after this, so it renders and raycasts on top of the canvas
            // instead of being clipped by CanvasViewport's RectMask2D. RightColumn no longer
            // has a LayoutGroup of its own (it used to stack Toolbar-then-Viewport); the
            // viewport now stretches to fill 100% of RightColumn directly via anchors, and the
            // toolbar positions itself via its own anchors (see NotesToolbar.Initialize).
            var viewportGO = new GameObject("CanvasViewport");
            viewportGO.transform.SetParent(rightColumnGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            CanvasController = gameObject.AddComponent<NotesCanvasController>();
            CanvasController.Initialize(DocumentController, viewportRect, interaction);

            interaction.canvasController = CanvasController;
            interaction.viewportRect = viewportRect;

            var toolbar = gameObject.AddComponent<NotesToolbar>();
            toolbar.Initialize(interaction, rightColumnGO.transform);
```

- [ ] **Step 2: Verify no other reference to `RightColumn`'s removed `VerticalLayoutGroup` remains**

Run:
```bash
grep -n "rightColumnVLayout\|viewportLE" "Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs"
```
Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs
git commit -m "refactor: float notes toolbar over full-height canvas viewport"
```

- [ ] **Step 4: Manual Play-mode verification (performed by the user, not the implementer)**

In the Unity Editor:
1. Enter Play Mode on the scene that runs `NotesRootBuilder`. Confirm no console errors.
2. Right-click the `NotesToolbar` component in the Inspector and run **Self-Test: Notes Toolbar — Icon Caching** (still PASS, unchanged) and **Self-Test: Notes Toolbar — Active/Hover Backdrop** (new — confirm PASS).
3. Confirm all 5 icons now read clearly and right-side-up: cursor (arrow with a visible notch/tail, tip top-left), note (unchanged), pencil (diagonal line with a clearly visible pointed tip at the bottom-right), image (mountain peak pointing up, sun near the top), zoom (lens upper-left, handle extending down-right).
4. Confirm buttons have no visible background by default — just bare floating icons over the canvas.
5. Hover each button without clicking: confirm a dim circular backdrop appears behind the icon alongside the existing tooltip; move the mouse away and confirm both disappear.
6. Click a tool: confirm a solid green circular backdrop appears behind it; hover a different button and confirm it shows the dim hover backdrop while the active one stays green.
7. Confirm the drawing canvas now extends the full height of the right column (the toolbar floats over its top-left corner instead of leaving a reserved gap above the canvas).
8. Report any icon that still doesn't read clearly, or any layout/hover glitch, for a follow-up tweak before moving on.

---

## Post-plan

Once all three tasks are complete and the user confirms Play-mode verification passes, this sub-project is done. Per the agreed sequence, the next spec to brainstorm is sidebar CRUD (rename/delete groups & pages) + search, followed by user-draggable panel splits.
