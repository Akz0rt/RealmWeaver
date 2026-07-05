# Theme System + Generation/Progress Screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Dark/Light theme token system used across all existing runtime-built UI, plus a Generation screen (empty state) and a Progress screen backed by a real staged generation pipeline, per `design_handoff_realmweaver_ui/README.md`.

**Architecture:** A static `ThemeService` holds two `Dictionary<ThemeRole, Color>` palettes and repaints every registered `ThemedGraphic` on `ApplyTheme`. Existing UI files are recolored in place (no layout changes) to tag their `Image`/`Text` components with roles instead of hardcoded `Color` literals. A new `MapScreenController` gates three mutually-exclusive views (Generation / Progress / existing Map-Editor+Legend) off `WorldMapRenderer.Cells == null` and a `generating` flag, driving a new coroutine-based `WorldGenerator.GenerateWorldStepped`.

**Tech Stack:** C#, `UnityEngine.UI` (Canvas/Image/Text/Button, runtime-built, no prefabs), `LegacyRuntime.ttf`.

## Global Constraints

- `ThemeRole` enum: `Bg, Panel, Panel2, Elev, Border, Txt, Mut, Accent, AccentInk, AccentSoft, MapOcean, MapLand, MapCoast, Dot, Danger`.
- Exact hex values (verbatim from the design spec):

  | Role | Dark | Light |
  |---|---|---|
  | Bg | `#141419` | `#E7E1D3` |
  | Panel | `#1C1C22` | `#F4F0E7` |
  | Panel2 | `#23232B` | `#FBF8F1` |
  | Elev | `#2B2B34` | `#FFFFFF` |
  | Border | `#34343F` | `#D5CCB8` |
  | Txt | `#E9E9EE` | `#2B2822` |
  | Mut | `#8E929E` | `#736A59` |
  | Accent | `#C9A24B` | `#4E4E93` |
  | AccentInk | `#1A1710` | `#FFFFFF` |
  | AccentSoft | `#2B2617` | `#E4E2F1` |
  | MapOcean | `#122A40` | `#BFD0D8` |
  | MapLand | `#26352A` | `#D6DBC2` |
  | MapCoast | `#3C5A44` | `#A9B58C` |
  | Dot | `#2A2A33` | `#D3CCBB` |
  | Danger | `#C9605A` | `#C9605A` |

- `ThemeService.Tag(Graphic graphic, ThemeRole role, float? alphaOverride = null)` — applies the current theme's color immediately (RGB from the role, alpha from `alphaOverride` if given, else the role's own alpha, which is `1.0` for every role above) and registers the graphic so `ApplyTheme` recolors it later. Use `alphaOverride` **only** where the existing literal already had `alpha < 1` on a floating panel/popup that isn't being redesigned this phase (preserves current translucency; this phase is recolor-only, no layout/behavior changes).
- Map/biome semantic colors (`RegionColorPalette.cs`, `MapLegendUI.cs`'s legend swatches) are **not** retrofitted — theme-independent by design.
- Content/interaction-feedback colors (POI type markers, drawing-tool colors, map-mesh rendering colors drawn onto the generated mesh itself, selection highlights on map geometry, hover-tint overlays) are **not** retrofitted — confirmed file-by-file below; only static UI-chrome surface/text/border colors get a role.
- Seed hashing uses a hand-rolled stable hash (`unchecked { int hash = 23; foreach (char c in s) hash = hash * 31 + c; return hash; }`) — never `string.GetHashCode()` (randomized per-process in .NET, would silently break "same seed = same map").
- Land-shape presets (approximate, confirmed with user): Материк `falloffPower=3.0f, innerRadius=0.6f, seaLevel=0.30f`; Архипелаг `1.8f, 0.3f, 0.45f`; Острова `1.5f, 0.1f, 0.55f`.
- Map size presets: Малый `350×350`; Средний `500×500` (default); Большой `700×700`.
- Region-count slider: range 4–40, default 24.
- Progress checklist, in this exact order (confirmed safe — biome classification only reads elevation/moisture, never temperature; region growing never depended on temperature either): Генерация высот → Океаны и озёра → Температура и влажность → Расчёт биомов → Границы регионов. No POI-placement step (that feature doesn't exist and isn't being added).
- No automated test runner in this project — verification is `[ContextMenu("Self-Test: ...")]` plus manual Play-mode testing.

---

### Task 1: ThemeService core

**Files:**
- Create: `Assets/WorldGen/Rendering/Theme/ThemeService.cs`

**Interfaces:**
- Produces: `ThemeRole` enum, `Theme` enum (`Dark`/`Light`), `ThemeService.Current`, `ThemeService.ApplyTheme(Theme)`, `ThemeService.Get(ThemeRole)`, `ThemeService.Tag(Graphic, ThemeRole, float? alphaOverride = null)` — every later task (2–9) calls `Tag`.

- [ ] **Step 1: Write ThemeService**

Create `Assets/WorldGen/Rendering/Theme/ThemeService.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Rendering.Theme
{
    public enum ThemeRole
    {
        Bg, Panel, Panel2, Elev, Border, Txt, Mut, Accent, AccentInk, AccentSoft,
        MapOcean, MapLand, MapCoast, Dot, Danger
    }

    public enum Theme { Dark, Light }

    /// <summary>
    /// Global Dark/Light theme. Every runtime-built Image/Text that should repaint on a
    /// theme switch calls ThemeService.Tag(graphic, role) once, right after construction --
    /// no prefabs, matches this project's existing runtime-UI convention.
    /// </summary>
    public static class ThemeService
    {
        const string PrefsKey = "Theme.Current";

        public static Theme Current { get; private set; } = Theme.Dark;

        static readonly List<ThemedGraphic> registered = new List<ThemedGraphic>();
        static bool loadedFromPrefs;

        static readonly Dictionary<ThemeRole, Color> Dark = new Dictionary<ThemeRole, Color>
        {
            { ThemeRole.Bg,         Hex("#141419") },
            { ThemeRole.Panel,      Hex("#1C1C22") },
            { ThemeRole.Panel2,     Hex("#23232B") },
            { ThemeRole.Elev,       Hex("#2B2B34") },
            { ThemeRole.Border,     Hex("#34343F") },
            { ThemeRole.Txt,        Hex("#E9E9EE") },
            { ThemeRole.Mut,        Hex("#8E929E") },
            { ThemeRole.Accent,     Hex("#C9A24B") },
            { ThemeRole.AccentInk,  Hex("#1A1710") },
            { ThemeRole.AccentSoft, Hex("#2B2617") },
            { ThemeRole.MapOcean,   Hex("#122A40") },
            { ThemeRole.MapLand,    Hex("#26352A") },
            { ThemeRole.MapCoast,   Hex("#3C5A44") },
            { ThemeRole.Dot,        Hex("#2A2A33") },
            { ThemeRole.Danger,     Hex("#C9605A") },
        };

        static readonly Dictionary<ThemeRole, Color> Light = new Dictionary<ThemeRole, Color>
        {
            { ThemeRole.Bg,         Hex("#E7E1D3") },
            { ThemeRole.Panel,      Hex("#F4F0E7") },
            { ThemeRole.Panel2,     Hex("#FBF8F1") },
            { ThemeRole.Elev,       Hex("#FFFFFF") },
            { ThemeRole.Border,     Hex("#D5CCB8") },
            { ThemeRole.Txt,        Hex("#2B2822") },
            { ThemeRole.Mut,        Hex("#736A59") },
            { ThemeRole.Accent,     Hex("#4E4E93") },
            { ThemeRole.AccentInk,  Hex("#FFFFFF") },
            { ThemeRole.AccentSoft, Hex("#E4E2F1") },
            { ThemeRole.MapOcean,   Hex("#BFD0D8") },
            { ThemeRole.MapLand,    Hex("#D6DBC2") },
            { ThemeRole.MapCoast,   Hex("#A9B58C") },
            { ThemeRole.Dot,        Hex("#D3CCBB") },
            { ThemeRole.Danger,     Hex("#C9605A") },
        };

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Dictionary<ThemeRole, Color> Palette => Current == Theme.Dark ? Dark : Light;

        static void EnsureLoaded()
        {
            if (loadedFromPrefs) return;
            loadedFromPrefs = true;
            Current = PlayerPrefs.GetInt(PrefsKey, 0) == 1 ? Theme.Light : Theme.Dark;
        }

        public static Color Get(ThemeRole role)
        {
            EnsureLoaded();
            return Palette[role];
        }

        public static void ApplyTheme(Theme theme)
        {
            EnsureLoaded();
            Current = theme;
            PlayerPrefs.SetInt(PrefsKey, theme == Theme.Light ? 1 : 0);

            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var tg = registered[i];
                if (tg == null) { registered.RemoveAt(i); continue; }
                tg.Repaint();
            }
        }

        public static void Tag(Graphic graphic, ThemeRole role, float? alphaOverride = null)
        {
            EnsureLoaded();
            var tg = graphic.GetComponent<ThemedGraphic>();
            if (tg == null) tg = graphic.gameObject.AddComponent<ThemedGraphic>();
            tg.Configure(graphic, role, alphaOverride);
            Register(tg);
            tg.Repaint();
        }

        internal static Color Resolve(ThemeRole role, float? alphaOverride)
        {
            var c = Get(role);
            if (alphaOverride.HasValue) c.a = alphaOverride.Value;
            return c;
        }

        static void Register(ThemedGraphic tg)
        {
            if (!registered.Contains(tg)) registered.Add(tg);
        }

        internal static void Unregister(ThemedGraphic tg)
        {
            registered.Remove(tg);
        }
    }

    /// <summary>Marker placed on a themed Graphic by ThemeService.Tag(); repaints on ApplyTheme.</summary>
    public class ThemedGraphic : MonoBehaviour
    {
        Graphic graphic;
        ThemeRole role;
        float? alphaOverride;

        public void Configure(Graphic g, ThemeRole r, float? alpha)
        {
            graphic = g;
            role = r;
            alphaOverride = alpha;
        }

        public void Repaint()
        {
            if (graphic != null) graphic.color = ThemeService.Resolve(role, alphaOverride);
        }

        void OnDestroy()
        {
            ThemeService.Unregister(this);
        }
    }
}
```

- [ ] **Step 2: Write the self-test**

Add this method to `ThemeService` (inside the class, before the closing brace):

```csharp
#if UNITY_EDITOR
        /// <summary>Self-test, invoked via a temporary caller (see plan) -- not a MonoBehaviour context menu, since ThemeService is static.</summary>
        public static bool SelfTestApplyTheme(out string message)
        {
            var probeGO = new GameObject("ThemeSelfTestProbe");
            var img = probeGO.AddComponent<Image>();
            Tag(img, ThemeRole.Accent);

            ApplyTheme(Theme.Dark);
            bool darkOk = img.color == Dark[ThemeRole.Accent];

            ApplyTheme(Theme.Light);
            bool lightOk = img.color == Light[ThemeRole.Accent];

            Object.DestroyImmediate(probeGO);

            bool ok = darkOk && lightOk;
            message = ok ? "Self-Test Theme Apply: PASS" : $"Self-Test Theme Apply: FAIL (darkOk={darkOk}, lightOk={lightOk})";
            return ok;
        }
#endif
```

- [ ] **Step 3: Run the self-test**

Since `ThemeService` is a static class (no Inspector context menu), invoke it via a one-off Editor script, same pattern used earlier in this project for scene-bootstrap self-tests: create `Assets/Editor/TempThemeSelfTest.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using WorldGen.Rendering.Theme;

public static class TempThemeSelfTest
{
    public static void Run()
    {
        bool ok = ThemeService.SelfTestApplyTheme(out string message);
        Debug.Log(message);
        if (!ok) EditorApplication.Exit(1);
    }
}
```

Run:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -executeMethod TempThemeSelfTest.Run -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/theme_selftest.log"
```

Expected: log contains `Self-Test Theme Apply: PASS`, exit code 0. Then delete `Assets/Editor/TempThemeSelfTest.cs` (and its `.meta`) — it's a one-off harness, not part of the feature.

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/Theme/ThemeService.cs"
git -C "d:/D&D" commit -m "feat: theme token system (ThemeService, Dark/Light palettes)"
```

---

### Task 2: Recolor map/rendering UI

**Files:**
- Modify: `Assets/WorldGen/Rendering/MapEditorPanel.cs`
- Modify: `Assets/WorldGen/Rendering/MapLegendUI.cs`
- No change needed (inspected, confirmed content/semantic-only colors): `WorldMapRenderer.cs` (river/border/coastline/gizmo colors are drawn onto the generated map mesh itself, not UI chrome), `RegionColorPalette.cs` (biome colors, already excluded by design), `PoiPlaceholderFactory.cs` (POI-type marker color-coding + sprite-generation utility colors), `CellSelectionController.cs` (map-geometry selection highlight), `CanvasInteractionController.cs` (default drawing-brush color)

**Interfaces:**
- Consumes: `ThemeService.Tag(Graphic, ThemeRole, float?)` (Task 1).

- [ ] **Step 1: Add the using directive**

In both `MapEditorPanel.cs` and `MapLegendUI.cs`, add near the top:

```csharp
using WorldGen.Rendering.Theme;
```

- [ ] **Step 2: Recolor MapEditorPanel.cs**

Worked example (shows the transformation pattern) — line 226:

```csharp
// Before:
tabBarGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

// After:
var tabBarImg = tabBarGO.AddComponent<Image>();
ThemeService.Tag(tabBarImg, ThemeRole.Panel2);
```

Apply the same pattern (capture the `Image`/`Text` into a local variable if the original was a one-liner, then call `ThemeService.Tag(...)` instead of setting `.color`) to every line below:

| Line | Current literal | New call |
|---|---|---|
| 27 | `panelBackgroundColor = new Color(0f, 0f, 0f, 0.7f)` | `ThemeService.Get(ThemeRole.Panel)` (field default; if used later via `.color = panelBackgroundColor`, replace that call site with `ThemeService.Tag(graphic, ThemeRole.Panel, 0.7f)` instead) |
| 29 | `sectionHeaderColor = new Color(0.7f, 0.85f, 1f)` | same pattern, `ThemeRole.Mut` |
| 30 | `activeModeColor = new Color(0.2f, 0.55f, 0.3f)` | `ThemeRole.Accent` |
| 31 | `inactiveModeColor = new Color(0.3f, 0.3f, 0.3f)` | `ThemeRole.Elev` |
| 226 | `new Color(0f, 0f, 0f, 0.4f)` | `ThemeRole.Panel2` |
| 429 | `new Color(0.2f, 0.45f, 0.2f)` ("Сгенерировать точки интереса" button) | `ThemeRole.Accent` |
| 430 | `new Color(0.2f, 0.4f, 0.6f)` ("Добавить одну точку") | `ThemeRole.Elev` |
| 431 | `new Color(0.5f, 0.2f, 0.2f)` ("Очистить все") | `ThemeRole.Danger` |
| 434 | `new Color(0.7f, 0.7f, 0.7f)` (hint text) | `ThemeRole.Mut` |
| 467 | `new Color(0.5f, 0.5f, 0.5f)` (separator label) | `ThemeRole.Mut` |
| 468 | `new Color(0.2f, 0.5f, 0.2f)` ("Применить к выбору") | `ThemeRole.Accent` |
| 469 | `new Color(0.5f, 0.2f, 0.2f)` ("Очистить все override") | `ThemeRole.Danger` |
| 470 | `new Color(0.25f, 0.35f, 0.5f)` ("Сбросить выбор") | `ThemeRole.Elev` |
| 479 | `new Color(0.15f, 0.15f, 0.25f, 0.95f)` (toolBg) | `ThemeRole.Panel2` |
| 552 | `new Color(0.7f, 0.7f, 0.7f)` (undo hint) | `ThemeRole.Mut` |
| 630 | `new Color(0.12f, 0.12f, 0.18f, 0.98f)` (dropdown template bg) | `ThemeRole.Panel2` |
| 647 | `new Color(1f, 1f, 1f, 0.01f)` (invisible hit-area) | **leave unchanged** — near-zero alpha hitbox, not a themed surface |
| 669 | `new Color(0.25f, 0.4f, 0.6f, 0.6f)` (selected list item) | `ThemeRole.AccentSoft` |
| 679 | `new Color(0.3f, 0.9f, 0.4f)` (checkmark glyph) | `ThemeRole.AccentInk` |
| 775 | `new Color(0.15f, 0.15f, 0.25f, 0.95f)` (dropdown bg) | `ThemeRole.Panel2` |
| 805 | `new Color(0.4f, 0.4f, 0.4f, 0.8f)` (unchecked checkbox bg) | `ThemeRole.Elev` |
| 812 | `new Color(0.3f, 0.9f, 0.4f)` (checkmark glyph) | `ThemeRole.AccentInk` |
| 831 | `new Color(1f, 1f, 1f, 0.15f)` (slider track) | `ThemeRole.Elev` |
| 839 | `new Color(0.3f, 0.6f, 0.9f, 0.9f)` (slider fill) | `ThemeRole.Accent` |
| 870 | `new Color(0.25f, 0.45f, 0.7f, 0.9f)` (default button fallback) | `ThemeRole.Elev` |
| 895 | `new Color(0.35f, 0.35f, 0.35f, 0.9f)` | `ThemeRole.Elev` |

For every row above except line 647 (explicitly left unchanged), replace the `new Color(...)` construction with the corresponding `ThemeService.Tag(<graphic variable>, ThemeRole.X)` call (add `, <alpha>f` as the third argument only where the design's role default alpha of `1.0` would visibly change the element's current translucency on a floating panel/popup that isn't being redesigned this phase — none of these specific lines need it, since they're either opaque already or full UI-chrome surfaces meant to become opaque per the new design).

- [ ] **Step 3: Recolor MapLegendUI.cs**

Only the panel background needs a role — the legend's biome swatch colors (lines 97-101, 119-121, 131) are semantic map content and must **not** change:

```csharp
// Line 27, before:
public Color panelBackgroundColor = new Color(0f, 0f, 0f, 0.55f);

// After: keep the field (still read elsewhere for the panel Image), but tag the actual
// Image component with it at its construction site using:
ThemeService.Tag(panelImg, ThemeRole.Panel, 0.55f);
```

(Find the actual construction site where `panelBackgroundColor` is assigned to an `Image.color` — likely in the panel-building method — and replace that assignment with the `Tag` call above, preserving the `0.55f` alpha since this floating panel isn't being redesigned this phase.)

- [ ] **Step 4: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task2_compile.log"
```

Expected: exit 0, no `error CS` lines. (If it fails with "another Unity instance is running", the user has the Editor open — report this as a concern rather than a code defect, per this project's established environment constraint.)

- [ ] **Step 5: Manual visual check**

Enter Play mode with an existing generated map (or generate one via the Editor's `[ContextMenu] Generate World` on `WorldMapRenderer`, since Task 9 hasn't wired the new Generation screen yet). Confirm the map editor panel and legend still look visually equivalent to before (dark surfaces, no layout shift) — this task only changes *how* colors are assigned, not their values, so nothing should look different yet.

- [ ] **Step 6: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/MapEditorPanel.cs" "Assets/WorldGen/Rendering/MapLegendUI.cs"
git -C "d:/D&D" commit -m "refactor: recolor map editor panel + legend via ThemeService"
```

---

### Task 3: Recolor notes UI

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs`, `NotesTreeSidebar.cs`, `LinkView.cs`, `LinkAnchorController.cs`, `NoteCardView.cs`, `NotesRootBuilder.cs`
- No change needed (inspected, confirmed content/interaction-feedback-only colors): `DrawingObjectView.cs` (blank canvas pixel fill), `DraggableDivider.cs` (hover-tint overlay), `ObjectResizeController.cs` (resize-handle interaction indicator), `NotesIconFactory.cs` (transparent sprite-generation utility)

**Interfaces:**
- Consumes: `ThemeService.Tag(Graphic, ThemeRole, float?)` (Task 1).

- [ ] **Step 1: Add the using directive**

Add `using WorldGen.Rendering.Theme;` near the top of each of the 6 files being changed.

- [ ] **Step 2: Recolor each file per this table**

| File:Line | Current literal | Role (alpha override if shown) |
|---|---|---|
| `NotesToolbar.cs:16` | `activeColor = new Color(0.2f, 0.55f, 0.3f, 0.65f)` | `AccentSoft`, alpha `0.65f` |
| `NotesToolbar.cs:17` | `hoverColor = new Color(1f, 1f, 1f, 0.15f)` | **leave unchanged** — hover-tint overlay |
| `NotesToolbar.cs:152` | `new Color(0.05f, 0.05f, 0.05f, 0.95f)` (toolbar bg) | `Panel2`, alpha `0.95f` |
| `NotesTreeSidebar.cs:83` | `new Color(0.2f, 0.2f, 0.2f, 0.9f)` (header bg) | `Panel2`, alpha `0.9f` |
| `NotesTreeSidebar.cs:107` | `new Color(1f, 1f, 1f, 0.06f)` (search field bg) | `Elev` |
| `NotesTreeSidebar.cs:133` | `new Color(1f, 1f, 1f, 0.4f)` (placeholder text) | `Mut` |
| `NotesTreeSidebar.cs:221` | `isActive ? new Color(0.2f, 0.4f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.02f)` | `isActive ? ThemeService.Get(ThemeRole.AccentSoft) : new Color(1f,1f,1f,0.02f)` — tag only the active branch's role via `ThemeService.Tag(graphic, ThemeRole.AccentSoft, 0.9f)` when `isActive`; leave the `else` branch's near-transparent literal unchanged |
| `NotesTreeSidebar.cs:322` | `new Color(0.7f, 0.85f, 1f)` (title text) | `Mut` |
| `NotesTreeSidebar.cs:355` | same pattern as line 221 | same treatment as line 221 |
| `NotesTreeSidebar.cs:406` | `new Color(1f, 1f, 1f, 0.1f)` (input bg) | `Elev` |
| `NotesTreeSidebar.cs:445` | `new Color(1f, 1f, 1f, 0.06f)` (delete button bg) | `Elev` |
| `NotesTreeSidebar.cs:462` | `new Color(1f, 0.6f, 0.6f)` (delete text) | `Danger` |
| `NotesTreeSidebar.cs:498` | `new Color(0.25f, 0.45f, 0.25f, 0.8f)` (group marker) | `Accent`, alpha `0.8f` |
| `LinkView.cs:35` | `NormalColor = new Color(0.9f, 0.9f, 0.9f, 0.9f)` | `Border`, alpha `0.9f` |
| `LinkView.cs:36` | `SelectedColor = new Color(1f, 0.85f, 0.3f, 0.95f)` | `Accent`, alpha `0.95f` |
| `LinkView.cs:90` | `new Color(0.9f, 0.9f, 0.9f, 0.9f)` (same as NormalColor) | `Border`, alpha `0.9f` |
| `LinkAnchorController.cs:42` | `new Color(0.3f, 0.7f, 1f, 0.95f)` (anchor dot) | `Accent`, alpha `0.95f` |
| `LinkAnchorController.cs:54` | `new Color(0.3f, 0.7f, 1f, 0.7f)` (preview line) | `Accent`, alpha `0.7f` |
| `NoteCardView.cs:45` | `new Color(0.18f, 0.18f, 0.2f, 0.95f)` (card bg) | `Panel2`, alpha `0.95f` |
| `NoteCardView.cs:65` | `new Color(1f, 1f, 1f, 0.01f)` | **leave unchanged** — invisible hit-area |
| `NotesRootBuilder.cs:59` | `new Color(0.12f, 0.12f, 0.14f, 1f)` (notes area bg) | `Bg` |
| `NotesRootBuilder.cs:119` | `new Color(0.08f, 0.08f, 0.1f, 1f)` (viewport bg) | `Bg` |

For every "leave unchanged" row, do not modify that line at all.

- [ ] **Step 3: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task3_compile.log"
```

Expected: exit 0, no `error CS` lines.

- [ ] **Step 4: Manual visual check**

Enter Play mode, open the notes editor, confirm sidebar/toolbar/cards/links still look visually equivalent (no layout shift, same colors as before — this task only changes *how* they're assigned).

- [ ] **Step 5: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Notes/Rendering/NotesToolbar.cs" "Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs" "Assets/WorldGen/Notes/Rendering/LinkView.cs" "Assets/WorldGen/Notes/Rendering/LinkAnchorController.cs" "Assets/WorldGen/Notes/Rendering/NoteCardView.cs" "Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs"
git -C "d:/D&D" commit -m "refactor: recolor notes editor UI via ThemeService"
```

---

### Task 4: Recolor POI edit panel

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs`

**Interfaces:**
- Consumes: `ThemeService.Tag(Graphic, ThemeRole, float?)` (Task 1).

- [ ] **Step 1: Add the using directive**

Add `using WorldGen.Rendering.Theme;` near the top.

- [ ] **Step 2: Recolor per this table**

| Line | Current literal | Role (alpha override if shown) |
|---|---|---|
| 37 | `panelBackgroundColor = new Color(0f, 0f, 0f, 0.75f)` | `Panel`, alpha `0.75f` |
| 39 | `sectionHeaderColor = new Color(0.7f, 0.85f, 1f)` | `Mut` |
| 237 | `new Color(0.15f, 0.15f, 0.25f, 0.95f)` (type-selector bg) | `Panel2`, alpha `0.95f` |
| 300 | `new Color(0.3f, 0.4f, 0.5f, 0.9f)` (snap-to button) | `Elev`, alpha `0.9f` |
| 339 | `new Color(0.3f, 0.5f, 0.3f, 0.9f)` (icon-pick button) | `Accent`, alpha `0.9f` |
| 373 | `new Color(0.55f, 0.15f, 0.15f)` (delete button) | `Danger` |
| 375 | `new Color(0.25f, 0.5f, 0.4f)` ("Открыть страницы") | `Accent` |
| 437 | `new Color(1f, 1f, 1f, 0.15f)` (slider track) | `Elev` |
| 445 | `new Color(0.3f, 0.6f, 0.9f, 0.9f)` (slider fill) | `Accent`, alpha `0.9f` |
| 508 | `new Color(0.25f, 0.45f, 0.7f, 0.9f)` (default button fallback) | `Elev`, alpha `0.9f` |
| 533 | `new Color(0.15f, 0.15f, 0.2f, 0.95f)` | `Panel2`, alpha `0.95f` |
| 558 | `new Color(0.5f, 0.5f, 0.5f)` (placeholder text) | `Mut` |
| 583 | `new Color(0.12f, 0.12f, 0.18f, 0.98f)` (dropdown bg) | `Panel2`, alpha `0.98f` |
| 600 | `new Color(1f, 1f, 1f, 0.01f)` | **leave unchanged** — invisible hit-area |
| 622 | `new Color(0.25f, 0.4f, 0.6f, 0.6f)` (selected list item) | `AccentSoft`, alpha `0.6f` |
| 632 | `new Color(0.3f, 0.9f, 0.4f)` (checkmark glyph) | `AccentInk` |

- [ ] **Step 3: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task4_compile.log"
```

Expected: exit 0, no `error CS` lines.

- [ ] **Step 4: Manual visual check**

Enter Play mode, open the Точки tab, select/edit a POI. Confirm the edit panel looks visually equivalent to before.

- [ ] **Step 5: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/PoiEditPanel.cs"
git -C "d:/D&D" commit -m "refactor: recolor POI edit panel via ThemeService"
```

---

### Task 5: Recolor project menu bar, confirm dialog, update checker + theme toggle

**Files:**
- Modify: `Assets/WorldGen/Rendering/ProjectMenuBar.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs`
- Modify: `Assets/WorldGen/Update/UpdateChecker.cs`

**Interfaces:**
- Consumes: `ThemeService.Tag`/`ApplyTheme`/`Current` (Task 1).

- [ ] **Step 1: Add the using directive**

Add `using WorldGen.Rendering.Theme;` near the top of all three files.

- [ ] **Step 2: Recolor ConfirmDialog.cs**

| Line | Current literal | Role (alpha override) |
|---|---|---|
| 19 | `new Color(0.3f, 0.3f, 0.3f)` (Отмена button) | `Elev` |
| 24 | `new Color(0.55f, 0.15f, 0.15f)` (Удалить button) | `Danger` |
| 37 | `new Color(0.3f, 0.3f, 0.3f)` (OK button) | `Elev` |
| 59 | `new Color(0f, 0f, 0f, 0.7f)` (panel bg) | `Panel`, alpha `0.7f` |

- [ ] **Step 3: Recolor ProjectMenuBar.cs**

| Line | Current literal | Role (alpha override) |
|---|---|---|
| 157 | `new Color(0.08f, 0.08f, 0.1f, 1f)` (menu bar bg) | `Panel` |
| 168 | `new Color(1f, 1f, 1f, 0.06f)` (Файл button bg) | `Elev` |
| 208 | `new Color(0f, 0f, 0f, 0f)` (backdrop, fully transparent) | **leave unchanged** |
| 219 | `new Color(0.12f, 0.12f, 0.16f, 0.98f)` (popup bg) | `Panel2`, alpha `0.98f` |
| 278 | `new Color(1f, 1f, 1f, 0f)` (popup item bg, fully transparent) | **leave unchanged** |

- [ ] **Step 4: Add the theme toggle menu action**

In `ProjectMenuBar.cs`'s `OpenActionsPopup()` method, after the existing `AddPopupAction(actionsPopupGO.transform, recentExpanded ? ... : ..., () => { ... });` block for "Открыть последние" and before its closing, add a new popup action:

```csharp
AddPopupAction(actionsPopupGO.transform, ThemeService.Current == Theme.Dark ? "Светлая тема" : "Тёмная тема", () =>
{
    CloseActionsPopup();
    ThemeService.ApplyTheme(ThemeService.Current == Theme.Dark ? Theme.Light : Theme.Dark);
});
```

Also update the `rowCount` calculation just above (`int rowCount = 4 + (recentExpanded ? ... : 0);`) to `int rowCount = 5 + (recentExpanded ? ... : 0);` — one more fixed row now that the theme toggle is added.

- [ ] **Step 5: Recolor UpdateChecker.cs**

| Line | Current literal | Role (alpha override) |
|---|---|---|
| 121 | `new Color(0.08f, 0.08f, 0.1f, 0.96f)` (banner bg) | `Panel`, alpha `0.96f` |
| 152 | `new Color(1f, 1f, 1f, 0f)` (dismiss button, fully transparent) | **leave unchanged** |
| 183 | `new Color(0.2f, 0.45f, 0.25f, 1f)` (action button bg) | `Accent` |

- [ ] **Step 6: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task5_compile.log"
```

Expected: exit 0, no `error CS` lines.

- [ ] **Step 7: Manual visual check**

Enter Play mode. Confirm modals (delete confirmation, save/load errors), the "Файл" menu bar, and the update banner (if triggered) all look visually equivalent to before. Open "Файл" → confirm a new "Светлая тема"/"Тёмная тема" entry appears at the bottom of the popup; click it and confirm **every** themed screen (map editor, legend, notes, POI panel, this menu, dialogs) recolors together immediately.

- [ ] **Step 8: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/ProjectMenuBar.cs" "Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs" "Assets/WorldGen/Update/UpdateChecker.cs"
git -C "d:/D&D" commit -m "refactor: recolor menu bar/dialogs/update banner via ThemeService, add theme toggle"
```

---

### Task 6: Staged generation pipeline (WorldGenerator.GenerateWorldStepped)

**Files:**
- Modify: `Assets/WorldGen/Generation/WorldGenerator.cs`

**Interfaces:**
- Consumes: nothing new (uses existing `GenerationParams`, `PoissonDiskSampling`, `VoronoiBuilder`, `LloydRelaxation`, `CornerGraphBuilder`, `HeightmapGenerator`, `IslandShapeAssigner`, `CornerOceanFloodFill`, `LakeSizeFilter`, `CellWaterAssigner`, `ElevationField`, `ValueRedistributor`, `MoistureField`, `CellClimateAverager`, `RegionGrowing`, `LakeRegionUnifier`, `TemperatureField` — all already used by the existing `GenerateWorld`).
- Produces: `WorldGenerator.GenerateWorldStepped(GenerationParams p, Action<string, float> onProgress, Action<List<VoronoiCell>, List<TemperatureEpicenter>, List<MoistureEpicenter>, List<River>> onComplete) : IEnumerator` — consumed by Task 8's `GenerationProgressUI`.

- [ ] **Step 1: Add the stepped method**

In `Assets/WorldGen/Generation/WorldGenerator.cs`, add `using System.Collections;` and `using System;` near the top if not already present (the file already has `using System.Collections.Generic;`, `using System.Linq;`, `using System.Numerics;` — check before adding duplicates). Then add this new method inside `WorldGenerator`, after the existing `GenerateWorld`:

```csharp
        /// <summary>
        /// Same pipeline as GenerateWorld, split into 5 progress-reportable stages for the
        /// Generation Progress screen. Temperature is computed right after moisture here
        /// (rather than at the very end, as in GenerateWorld) so the reported step order
        /// matches the UI checklist -- safe because BiomeClassifier only reads elevation and
        /// moisture (see CellClimateAverager.cs:49), and region growing never used temperature
        /// either. GenerateWorld itself is untouched, kept for self-tests/back-compat.
        /// </summary>
        public static IEnumerator GenerateWorldStepped(
            GenerationParams p,
            Action<string, float> onProgress,
            Action<List<VoronoiCell>, List<TemperatureEpicenter>, List<MoistureEpicenter>, List<River>> onComplete)
        {
            // --- Step 1/5: Генерация высот ---
            onProgress?.Invoke("Генерация высот", 0f / 5f);
            var points = PoissonDiskSampling.Generate(p.Width, p.Height, p.MinPointDistance, p.Seed);
            var cells = VoronoiBuilder.Build(points, p.Width, p.Height);

            for (int i = 0; i < p.LloydRelaxIterations; i++)
            {
                var relaxedPoints = LloydRelaxation.ComputeRelaxedPoints(cells);
                cells = VoronoiBuilder.Build(relaxedPoints, p.Width, p.Height);
            }

            var corners = CornerGraphBuilder.Build(cells);

            var islandShapeGen = new HeightmapGenerator(p.Seed, p.Width, p.Height, p.HeightFrequency, p.HeightOctaves,
                                                          p.WarpAmplitude, falloffPower: p.FalloffPower, innerRadius: p.InnerRadius);
            IslandShapeAssigner.AssignWaterCorners(corners, islandShapeGen, p.SeaLevel);
            yield return null;

            // --- Step 2/5: Океаны и озёра ---
            onProgress?.Invoke("Океаны и озёра", 1f / 5f);
            CornerOceanFloodFill.MarkOcean(corners, p.Width, p.Height);
            if (p.MinLakeSize > 1)
                LakeSizeFilter.RemoveSmallLakes(corners, p.MinLakeSize);
            CellWaterAssigner.AssignFromCorners(cells, corners);

            ElevationField.ApplyElevation(corners, p.ElevationCoastWeight, p.ElevationNoiseWeight,
                                            p.Seed, p.ElevationNoiseFrequency, p.ElevationNoiseOctaves);
            ValueRedistributor.RedistributeElevation(corners);
            yield return null;

            // --- Step 3/5: Температура и влажность ---
            onProgress?.Invoke("Температура и влажность", 2f / 5f);
            var rivers = p.EnableRivers
                ? RiverTracer.TraceRivers(corners, p.NumberOfRivers, p.Seed, p.RiverMinStartElevation, p.RiverMaxSteps)
                : new List<River>();
            var riverCornerIds = RiverFlowAccumulator.GetRiverCornerIds(rivers);

            var moistureEpicenters = GenerateRandomMoistureEpicenters(p);
            MoistureField.ApplyMoisture(corners, p.MoistureFalloffDistance, moistureEpicenters, riverCornerIds);

            // Temperature moved up from its original end-of-pipeline position in GenerateWorld --
            // safe reordering, see method doc comment above.
            var temperatureEpicenters = GenerateRandomEpicenters(p);
            yield return null;

            // --- Step 4/5: Расчёт биомов ---
            onProgress?.Invoke("Расчёт биомов", 3f / 5f);
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold);
            RegenerateTemperature(cells, p, temperatureEpicenters);
            yield return null;

            // --- Step 5/5: Границы регионов ---
            onProgress?.Invoke("Границы регионов", 4f / 5f);
            var landCells = cells.Where(c => !c.IsOcean).ToList();
            if (landCells.Count >= p.NumberOfRegions)
                RegionGrowing.GroupCells(cells, landCells, p.NumberOfRegions, p.Seed);
            LakeRegionUnifier.UnifyLakes(cells);
            yield return null;

            onProgress?.Invoke("Готово", 5f / 5f);
            onComplete?.Invoke(cells, temperatureEpicenters, moistureEpicenters, rivers);
        }
```

- [ ] **Step 2: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task6_compile.log"
```

Expected: exit 0, no `error CS` lines.

- [ ] **Step 3: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Generation/WorldGenerator.cs"
git -C "d:/D&D" commit -m "feat: add GenerateWorldStepped, a coroutine-friendly staged generation pipeline"
```

---

### Task 7: Generation screen (empty state)

**Files:**
- Create: `Assets/WorldGen/Rendering/GenerationScreenUI.cs`

**Interfaces:**
- Consumes: `ThemeService.Tag`/`Get` (Task 1), `ProjectMenuBar` (existing, for the "Открыть проект…" button — needs a reference, assigned like other cross-component references in this project), `WorldMapRenderer` (existing public fields `seed`, `mapWidth`, `mapHeight`, `falloffPower`, `innerRadius`, `seaLevel`, `numberOfRegions`).
- Produces: `GenerationScreenUI.OnGenerateRequested : Action<GenerationParams>` (an event/callback Task 9's `MapScreenController` subscribes to, so it can start the staged coroutine from Task 6) — actually simpler and more consistent with this project's existing wiring style (direct field references, like `ProjectMenuBar.mapRenderer`): `GenerationScreenUI` takes a `public MapScreenController controller;` Inspector reference and calls `controller.StartGeneration(seedString, size, shape, regionCount)` directly on button click. Task 9 defines `MapScreenController.StartGeneration`.

- [ ] **Step 1: Write GenerationScreenUI**

Create `Assets/WorldGen/Rendering/GenerationScreenUI.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    public enum MapSizePreset { Small, Medium, Large }
    public enum LandShapePreset { Continent, Archipelago, Islands }

    /// <summary>
    /// Empty-state screen shown when WorldMapRenderer.Cells == null. Collects seed/size/
    /// land-shape/region-detail, then hands off to MapScreenController.StartGeneration.
    /// Self-contained -- add to the scene, assign `controller` and `projectMenuBar` in the Inspector.
    /// </summary>
    public class GenerationScreenUI : MonoBehaviour
    {
        public MapScreenController controller;
        public ProjectMenuBar projectMenuBar;

        const int MinRegions = 4;
        const int MaxRegions = 40;
        const int DefaultRegions = 24;

        Font builtinFont;
        InputField seedField;
        MapSizePreset selectedSize = MapSizePreset.Medium;
        LandShapePreset selectedShape = LandShapePreset.Continent;
        int selectedRegions = DefaultRegions;

        Button[] sizeButtons = new Button[3];
        Button[] shapeButtons = new Button[3];
        Text regionsValueLabel;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        public static int StableSeedHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        static string RandomSeedString()
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            var rng = new System.Random();
            var chars = new char[8];
            for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
            return new string(chars) + "-" + rng.Next(1000, 9999);
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("GenerationScreenCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // below ProjectMenuBar's popups (100+), above nothing else needed
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var bgGO = new GameObject("Backdrop");
            bgGO.transform.SetParent(canvasTransform, false);
            var bgImg = bgGO.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Bg);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var cardGO = new GameObject("GenerationCard");
            cardGO.transform.SetParent(canvasTransform, false);
            var cardImg = cardGO.AddComponent<Image>();
            ThemeService.Tag(cardImg, ThemeRole.Panel);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(560f, 520f);
            cardRect.anchoredPosition = Vector2.zero;

            var layout = cardGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 28, 28);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            AddLabel(cardGO.transform, "Создать карту мира", 20, bold: true, role: ThemeRole.Txt, height: 26f);
            AddLabel(cardGO.transform, "Карта ещё не сгенерирована", 12, bold: false, role: ThemeRole.Mut, height: 18f);

            AddFieldLabel(cardGO.transform, "СИД");
            BuildSeedRow(cardGO.transform);

            AddFieldLabel(cardGO.transform, "РАЗМЕР КАРТЫ");
            BuildSizeSegment(cardGO.transform);

            AddFieldLabel(cardGO.transform, "ФОРМА СУШИ");
            BuildShapeSegment(cardGO.transform);

            AddFieldLabel(cardGO.transform, "ДЕТАЛИЗАЦИЯ · РЕГИОНОВ");
            BuildRegionsSlider(cardGO.transform);

            BuildGenerateButton(cardGO.transform);
            BuildOpenProjectButton(cardGO.transform);
        }

        void AddLabel(Transform parent, string text, int fontSize, bool bold, ThemeRole role, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = builtinFont;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(t, role);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        void AddFieldLabel(Transform parent, string text) => AddLabel(parent, text, 11, bold: true, role: ThemeRole.Mut, height: 16f);

        void BuildSeedRow(Transform parent)
        {
            var rowGO = new GameObject("SeedRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 38f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;

            var fieldGO = new GameObject("SeedField");
            fieldGO.transform.SetParent(rowGO.transform, false);
            var fieldImg = fieldGO.AddComponent<Image>();
            ThemeService.Tag(fieldImg, ThemeRole.Elev);
            fieldGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            seedField = fieldGO.AddComponent<InputField>();
            var seedTextGO = new GameObject("Text");
            seedTextGO.transform.SetParent(fieldGO.transform, false);
            var seedText = seedTextGO.AddComponent<Text>();
            seedText.font = builtinFont;
            seedText.fontSize = 12;
            seedText.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(seedText, ThemeRole.Txt);
            var seedTextRect = seedTextGO.GetComponent<RectTransform>();
            seedTextRect.anchorMin = Vector2.zero;
            seedTextRect.anchorMax = Vector2.one;
            seedTextRect.offsetMin = new Vector2(10f, 4f);
            seedTextRect.offsetMax = new Vector2(-10f, -4f);
            seedField.textComponent = seedText;
            seedField.text = RandomSeedString();

            var randomBtnGO = new GameObject("RandomButton");
            randomBtnGO.transform.SetParent(rowGO.transform, false);
            var randomBtnImg = randomBtnGO.AddComponent<Image>();
            ThemeService.Tag(randomBtnImg, ThemeRole.Elev);
            randomBtnGO.AddComponent<LayoutElement>().preferredWidth = 110f;
            var randomBtn = randomBtnGO.AddComponent<Button>();
            randomBtn.targetGraphic = randomBtnImg;
            randomBtn.onClick.AddListener(() => seedField.text = RandomSeedString());
            var randomTextGO = new GameObject("Text");
            randomTextGO.transform.SetParent(randomBtnGO.transform, false);
            var randomText = randomTextGO.AddComponent<Text>();
            randomText.text = "↻ Случайно";
            randomText.font = builtinFont;
            randomText.fontSize = 12;
            randomText.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(randomText, ThemeRole.Txt);
            var randomTextRect = randomTextGO.GetComponent<RectTransform>();
            randomTextRect.anchorMin = Vector2.zero;
            randomTextRect.anchorMax = Vector2.one;
            randomTextRect.sizeDelta = Vector2.zero;
        }

        void BuildSizeSegment(Transform parent)
        {
            string[] labels = { "Малый", "Средний", "Большой" };
            var rowGO = BuildSegmentRow(parent, "SizeSegment", labels, sizeButtons, 0);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                sizeButtons[i].onClick.AddListener(() => { selectedSize = (MapSizePreset)captured; RefreshSegmentColors(sizeButtons, captured); });
            }
        }

        void BuildShapeSegment(Transform parent)
        {
            string[] labels = { "Материк", "Архипелаг", "Острова" };
            BuildSegmentRow(parent, "ShapeSegment", labels, shapeButtons, 0);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                shapeButtons[i].onClick.AddListener(() => { selectedShape = (LandShapePreset)captured; RefreshSegmentColors(shapeButtons, captured); });
            }
        }

        GameObject BuildSegmentRow(Transform parent, string name, string[] labels, Button[] buttons, int defaultIndex)
        {
            var rowGO = new GameObject(name);
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 38f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;

            for (int i = 0; i < labels.Length; i++)
            {
                var btnGO = new GameObject($"Segment_{labels[i]}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                buttons[i] = btn;

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
                ThemeService.Tag(text, i == defaultIndex ? ThemeRole.AccentInk : ThemeRole.Txt);
                ThemeService.Tag(img, i == defaultIndex ? ThemeRole.Accent : ThemeRole.Elev);
            }

            return rowGO;
        }

        void RefreshSegmentColors(Button[] buttons, int activeIndex)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i].targetGraphic as Image;
                var text = buttons[i].GetComponentInChildren<Text>();
                ThemeService.Tag(img, i == activeIndex ? ThemeRole.Accent : ThemeRole.Elev);
                ThemeService.Tag(text, i == activeIndex ? ThemeRole.AccentInk : ThemeRole.Txt);
            }
        }

        void BuildRegionsSlider(Transform parent)
        {
            var rowGO = new GameObject("RegionsRow");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 24f;
            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(rowGO.transform, false);
            sliderGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = MinRegions;
            slider.maxValue = MaxRegions;
            slider.wholeNumbers = true;
            slider.value = DefaultRegions;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgImg = bgGO.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Elev);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.4f);
            bgRect.anchorMax = new Vector2(1f, 0.6f);
            bgRect.sizeDelta = Vector2.zero;
            slider.targetGraphic = bgImg;

            var fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.4f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.6f);
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            ThemeService.Tag(fillImg, ThemeRole.Accent);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            regionsValueLabel = null;
            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            valueGO.AddComponent<LayoutElement>().preferredWidth = 32f;
            regionsValueLabel = valueGO.AddComponent<Text>();
            regionsValueLabel.font = builtinFont;
            regionsValueLabel.fontSize = 12;
            regionsValueLabel.fontStyle = FontStyle.Bold;
            regionsValueLabel.alignment = TextAnchor.MiddleRight;
            regionsValueLabel.text = DefaultRegions.ToString();
            ThemeService.Tag(regionsValueLabel, ThemeRole.Accent);

            slider.onValueChanged.AddListener(v =>
            {
                selectedRegions = Mathf.RoundToInt(v);
                regionsValueLabel.text = selectedRegions.ToString();
            });
        }

        void BuildGenerateButton(Transform parent)
        {
            var btnGO = new GameObject("GenerateButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 48f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Accent);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnGenerateClicked);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "✦ Сгенерировать карту";
            text.font = builtinFont;
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.AccentInk);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void BuildOpenProjectButton(Transform parent)
        {
            var btnGO = new GameObject("OpenProjectButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => projectMenuBar?.TriggerOpenFromExternal());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "Открыть проект…";
            text.font = builtinFont;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void OnGenerateClicked()
        {
            var p = new GenerationParams(seedField.text, selectedSize, selectedShape, selectedRegions);
            controller?.StartGeneration(p);
        }
    }

    /// <summary>Plain data the Generation screen hands to MapScreenController.StartGeneration.</summary>
    public class GenerationParams
    {
        public readonly string SeedText;
        public readonly MapSizePreset Size;
        public readonly LandShapePreset Shape;
        public readonly int RegionCount;

        public GenerationParams(string seedText, MapSizePreset size, LandShapePreset shape, int regionCount)
        {
            SeedText = seedText;
            Size = size;
            Shape = shape;
            RegionCount = regionCount;
        }
    }
}
```

**Note:** this file defines a class named `GenerationParams` inside `namespace WorldGen.Rendering` — this is a **different type** from `WorldGen.Generation.GenerationParams` (the existing generator's parameter object). They don't collide because they're in different namespaces, but don't confuse them when reading Task 9 — `WorldGen.Rendering.GenerationParams` (this file) is the UI's raw form input, translated by `MapScreenController.StartGeneration` (Task 9) into a real `WorldGen.Generation.GenerationParams` before calling `GenerateWorldStepped`.

- [ ] **Step 2: Add `TriggerOpenFromExternal` to ProjectMenuBar**

`ProjectMenuBar.cs`'s `DoOpen()` is currently private (`void DoOpen()`). Add a thin public wrapper right after it:

```csharp
        /// <summary>Lets other screens (e.g. GenerationScreenUI's "Открыть проект…") trigger the same Open flow.</summary>
        public void TriggerOpenFromExternal() => DoOpen();
```

- [ ] **Step 3: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task7_compile.log"
```

Expected: exit 0, no `error CS` lines. (Task 9 hasn't created `MapScreenController` yet — this task's code references `MapScreenController` by type name only in a field declaration, which won't compile until Task 9 exists. **If Task 9 is executed after this task**, this specific compile check will fail with "MapScreenController not found" — that's expected; note it in the report and move on, Task 9's own compile check is what actually validates this file.)

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/GenerationScreenUI.cs" "Assets/WorldGen/Rendering/ProjectMenuBar.cs"
git -C "d:/D&D" commit -m "feat: Generation screen (empty-state UI: seed/size/shape/detail form)"
```

---

### Task 8: Progress screen

**Files:**
- Create: `Assets/WorldGen/Rendering/GenerationProgressUI.cs`

**Interfaces:**
- Consumes: `ThemeService.Tag`/`Get` (Task 1).
- Produces: `GenerationProgressUI.SetStep(string label, float fraction)`, `GenerationProgressUI.OnCancelRequested : Action` (event Task 9 subscribes to) — used by `MapScreenController` (Task 9).

- [ ] **Step 1: Write GenerationProgressUI**

Create `Assets/WorldGen/Rendering/GenerationProgressUI.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Shown while WorldGenerator.GenerateWorldStepped runs. Self-contained -- add to the
    /// scene, no Inspector wiring needed beyond what MapScreenController assigns at runtime.
    /// </summary>
    public class GenerationProgressUI : MonoBehaviour
    {
        public event Action OnCancelRequested;

        static readonly string[] StepLabels =
        {
            "Генерация высот", "Океаны и озёра", "Температура и влажность",
            "Расчёт биомов", "Границы регионов"
        };

        Font builtinFont;
        Text stepLineLabel;
        Text percentLabel;
        Image progressFill;
        RectTransform progressFillRect;
        readonly List<Text> checklistLabels = new List<Text>();
        readonly List<Image> checklistDots = new List<Image>();
        int currentStepIndex = -1;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("GenerationProgressCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            var canvasTransform = canvasGO.transform;

            var bgGO = new GameObject("Backdrop");
            bgGO.transform.SetParent(canvasTransform, false);
            var bgImg = bgGO.AddComponent<Image>();
            ThemeService.Tag(bgImg, ThemeRole.Bg);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var cardGO = new GameObject("ProgressCard");
            cardGO.transform.SetParent(canvasTransform, false);
            var cardImg = cardGO.AddComponent<Image>();
            ThemeService.Tag(cardImg, ThemeRole.Panel);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(560f, 420f);
            cardRect.anchoredPosition = Vector2.zero;

            var layout = cardGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 28, 28);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(cardGO.transform, false);
            var title = titleGO.AddComponent<Text>();
            title.text = "Создание мира…";
            title.font = builtinFont;
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;
            ThemeService.Tag(title, ThemeRole.Txt);
            titleGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            var stepLineGO = new GameObject("StepLine");
            stepLineGO.transform.SetParent(cardGO.transform, false);
            stepLineGO.AddComponent<LayoutElement>().preferredHeight = 20f;
            var stepLineLayout = stepLineGO.AddComponent<HorizontalLayoutGroup>();
            stepLineLayout.childControlWidth = true;

            var stepGO = new GameObject("StepLabel");
            stepGO.transform.SetParent(stepLineGO.transform, false);
            stepGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            stepLineLabel = stepGO.AddComponent<Text>();
            stepLineLabel.font = builtinFont;
            stepLineLabel.fontSize = 13;
            ThemeService.Tag(stepLineLabel, ThemeRole.Txt);

            var pctGO = new GameObject("Percent");
            pctGO.transform.SetParent(stepLineGO.transform, false);
            pctGO.AddComponent<LayoutElement>().preferredWidth = 50f;
            percentLabel = pctGO.AddComponent<Text>();
            percentLabel.font = builtinFont;
            percentLabel.fontSize = 13;
            percentLabel.fontStyle = FontStyle.Bold;
            percentLabel.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(percentLabel, ThemeRole.Accent);

            BuildProgressBar(cardGO.transform);
            BuildChecklist(cardGO.transform);
            BuildCancelButton(cardGO.transform);
        }

        void BuildProgressBar(Transform parent)
        {
            var trackGO = new GameObject("ProgressTrack");
            trackGO.transform.SetParent(parent, false);
            trackGO.AddComponent<LayoutElement>().preferredHeight = 8f;
            var trackImg = trackGO.AddComponent<Image>();
            ThemeService.Tag(trackImg, ThemeRole.Elev);

            var fillGO = new GameObject("ProgressFill");
            fillGO.transform.SetParent(trackGO.transform, false);
            progressFill = fillGO.AddComponent<Image>();
            ThemeService.Tag(progressFill, ThemeRole.Accent);
            progressFillRect = fillGO.GetComponent<RectTransform>();
            progressFillRect.anchorMin = new Vector2(0f, 0f);
            progressFillRect.anchorMax = new Vector2(0f, 1f);
            progressFillRect.sizeDelta = Vector2.zero;
            progressFillRect.pivot = new Vector2(0f, 0.5f);
        }

        void BuildChecklist(Transform parent)
        {
            var listGO = new GameObject("Checklist");
            listGO.transform.SetParent(parent, false);
            var listLayout = listGO.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandWidth = true;
            listGO.AddComponent<LayoutElement>().preferredHeight = StepLabels.Length * 26f;

            foreach (var label in StepLabels)
            {
                var rowGO = new GameObject($"Step_{label}");
                rowGO.transform.SetParent(listGO.transform, false);
                rowGO.AddComponent<LayoutElement>().preferredHeight = 22f;
                var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childControlWidth = false;

                var dotGO = new GameObject("Dot");
                dotGO.transform.SetParent(rowGO.transform, false);
                dotGO.AddComponent<LayoutElement>().preferredWidth = 16f;
                var dotImg = dotGO.AddComponent<Image>();
                ThemeService.Tag(dotImg, ThemeRole.Border);
                checklistDots.Add(dotImg);

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(rowGO.transform, false);
                textGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = builtinFont;
                text.fontSize = 12;
                ThemeService.Tag(text, ThemeRole.Mut);
                checklistLabels.Add(text);
            }
        }

        void BuildCancelButton(Transform parent)
        {
            var btnGO = new GameObject("CancelButton");
            btnGO.transform.SetParent(parent, false);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 44f;
            var img = btnGO.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnCancelRequested?.Invoke());

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "Отмена";
            text.font = builtinFont;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(text, ThemeRole.Txt);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        /// <summary>Called by MapScreenController's onProgress callback.</summary>
        public void SetStep(string label, float fraction)
        {
            stepLineLabel.text = label;
            percentLabel.text = $"{Mathf.RoundToInt(fraction * 100f)}%";
            progressFillRect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);

            int stepIndex = Array.IndexOf(StepLabels, label);
            if (stepIndex < 0) return; // "Готово" or unrecognized label -- leave checklist as-is (all done)
            currentStepIndex = stepIndex;

            for (int i = 0; i < checklistLabels.Count; i++)
            {
                if (i < currentStepIndex)
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Accent);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Txt);
                }
                else if (i == currentStepIndex)
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Accent);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Txt);
                }
                else
                {
                    ThemeService.Tag(checklistDots[i], ThemeRole.Border);
                    ThemeService.Tag(checklistLabels[i], ThemeRole.Mut);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task8_compile.log"
```

Expected: exit 0, no `error CS` lines.

- [ ] **Step 3: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/GenerationProgressUI.cs"
git -C "d:/D&D" commit -m "feat: Progress screen (staged-generation checklist UI)"
```

---

### Task 9: MapScreenController — screen switching + scene wiring

**Files:**
- Create: `Assets/WorldGen/Rendering/MapScreenController.cs`
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes: `GenerationScreenUI` (Task 7, including its `WorldGen.Rendering.GenerationParams`, `MapSizePreset`, `LandShapePreset`, `GenerationScreenUI.StableSeedHash`), `GenerationProgressUI.SetStep`/`OnCancelRequested` (Task 8), `WorldGenerator.GenerateWorldStepped` (Task 6), `WorldMapRenderer` (existing: `Cells`, `OnWorldRegenerated`, public fields `seed`/`mapWidth`/`mapHeight`/`falloffPower`/`innerRadius`/`seaLevel`/`numberOfRegions`), `MapEditorPanel`/`MapLegendUI` (existing, gated by this controller).

- [ ] **Step 1: Write MapScreenController**

Create `Assets/WorldGen/Rendering/MapScreenController.cs`:

```csharp
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Switches between three mutually-exclusive screens based on whether a map exists yet
    /// and whether generation is in progress: GenerationScreenUI (no map) / GenerationProgressUI
    /// (generating) / the existing MapEditorPanel+MapLegendUI pair (map ready).
    /// </summary>
    public class MapScreenController : MonoBehaviour
    {
        public WorldMapRenderer mapRenderer;
        public GenerationScreenUI generationScreen;
        public GenerationProgressUI progressScreen;
        public GameObject mapEditorPanelGO;
        public GameObject mapLegendUiGO;

        Coroutine activeGeneration;

        void Awake()
        {
            progressScreen.OnCancelRequested += CancelGeneration;
        }

        void Start()
        {
            mapRenderer.OnWorldRegenerated += RefreshScreenState;
            RefreshScreenState();
        }

        void RefreshScreenState()
        {
            bool hasMap = mapRenderer.Cells != null;
            bool generating = activeGeneration != null;

            generationScreen.gameObject.SetActive(!hasMap && !generating);
            progressScreen.gameObject.SetActive(generating);
            mapEditorPanelGO.SetActive(hasMap && !generating);
            mapLegendUiGO.SetActive(hasMap && !generating);
        }

        public void StartGeneration(WorldGen.Rendering.GenerationParams uiParams)
        {
            if (activeGeneration != null) return;

            ApplyUiParamsToRenderer(uiParams);
            var genParams = BuildGenerationParams(uiParams);

            RefreshScreenState(); // hasMap is still false here, but activeGeneration isn't set yet either -- set it first
            activeGeneration = StartCoroutine(RunGeneration(genParams));
        }

        void ApplyUiParamsToRenderer(WorldGen.Rendering.GenerationParams uiParams)
        {
            mapRenderer.seed = GenerationScreenUI.StableSeedHash(uiParams.SeedText);

            switch (uiParams.Size)
            {
                case MapSizePreset.Small:  mapRenderer.mapWidth = 350f; mapRenderer.mapHeight = 350f; break;
                case MapSizePreset.Medium: mapRenderer.mapWidth = 500f; mapRenderer.mapHeight = 500f; break;
                case MapSizePreset.Large:  mapRenderer.mapWidth = 700f; mapRenderer.mapHeight = 700f; break;
            }

            switch (uiParams.Shape)
            {
                case LandShapePreset.Continent:   mapRenderer.falloffPower = 3.0f; mapRenderer.innerRadius = 0.6f; mapRenderer.seaLevel = 0.30f; break;
                case LandShapePreset.Archipelago:  mapRenderer.falloffPower = 1.8f; mapRenderer.innerRadius = 0.3f; mapRenderer.seaLevel = 0.45f; break;
                case LandShapePreset.Islands:       mapRenderer.falloffPower = 1.5f; mapRenderer.innerRadius = 0.1f; mapRenderer.seaLevel = 0.55f; break;
            }

            mapRenderer.numberOfRegions = uiParams.RegionCount;
        }

        GenerationParams BuildGenerationParams(WorldGen.Rendering.GenerationParams uiParams)
        {
            // Mirrors WorldMapRenderer.BuildGenerationParams()'s field-by-field copy, since
            // GenerateWorldStepped (unlike GenerateAndRender) is called directly here, not
            // through WorldMapRenderer.
            return new GenerationParams
            {
                Seed = mapRenderer.seed,
                Width = mapRenderer.mapWidth,
                Height = mapRenderer.mapHeight,
                MinPointDistance = mapRenderer.minPointDistance,
                LloydRelaxIterations = mapRenderer.lloydIterations,
                NumberOfRegions = mapRenderer.numberOfRegions,
                FalloffPower = mapRenderer.falloffPower,
                InnerRadius = mapRenderer.innerRadius,
                SeaLevel = mapRenderer.seaLevel,
                MinLakeSize = mapRenderer.minLakeSize,
                ElevationCoastWeight = mapRenderer.elevationCoastWeight,
                ElevationNoiseWeight = mapRenderer.elevationNoiseWeight,
                ElevationNoiseFrequency = mapRenderer.elevationNoiseFrequency,
                ElevationNoiseOctaves = mapRenderer.elevationNoiseOctaves,
                MoistureFalloffDistance = mapRenderer.moistureFalloffDistance,
                BeachElevationThreshold = mapRenderer.beachElevationThreshold,
                NumberOfTemperatureEpicenters = mapRenderer.numberOfTemperatureEpicenters,
                EpicenterMinRadius = mapRenderer.epicenterMinRadius,
                EpicenterMaxRadius = mapRenderer.epicenterMaxRadius,
                BaseTemperature = mapRenderer.baseTemperature,
                HeightCoolingFactor = mapRenderer.heightCoolingFactor,
                NumberOfMoistureEpicenters = mapRenderer.numberOfMoistureEpicenters,
                MoistureEpicenterMinRadius = mapRenderer.moistureEpicenterMinRadius,
                MoistureEpicenterMaxRadius = mapRenderer.moistureEpicenterMaxRadius,
                MoistureEpicenterMinDelta = mapRenderer.moistureEpicenterMinDelta,
                MoistureEpicenterMaxDelta = mapRenderer.moistureEpicenterMaxDelta,
                EnableRivers = mapRenderer.enableRivers,
                NumberOfRivers = mapRenderer.numberOfRivers,
                RiverMinStartElevation = mapRenderer.riverMinStartElevation,
            };
        }

        System.Collections.IEnumerator RunGeneration(GenerationParams genParams)
        {
            RefreshScreenStateForGenerating();

            yield return WorldGenerator.GenerateWorldStepped(genParams,
                (label, frac) => progressScreen.SetStep(label, frac),
                (cells, tempEpicenters, moistureEpicenters, rivers) =>
                {
                    mapRenderer.LoadFromCells(cells, genParams);
                    activeGeneration = null;
                    RefreshScreenState();
                });
        }

        void RefreshScreenStateForGenerating()
        {
            generationScreen.gameObject.SetActive(false);
            progressScreen.gameObject.SetActive(true);
            mapEditorPanelGO.SetActive(false);
            mapLegendUiGO.SetActive(false);
        }

        void CancelGeneration()
        {
            if (activeGeneration == null) return;
            StopCoroutine(activeGeneration);
            activeGeneration = null;
            RefreshScreenState();
        }
    }
}
```

**Note on `LoadFromCells`:** this project already has `WorldMapRenderer.LoadFromCells(cells, genParams)` (used by the project-load feature) which rebuilds the mesh/border/river renderers from a cell list without re-running generation — reused here as the "commit the generated cells to the renderer" step, exactly matching what `GenerateAndRender()` itself does internally after calling `WorldGenerator.GenerateWorld`. If `LoadFromCells`'s exact signature differs from `(List<VoronoiCell>, GenerationParams)`, adjust this call to match — check `WorldMapRenderer.cs`'s existing `LoadFromCells` declaration before wiring this up (it's called by `ProjectMenuBar.LoadFrom`, search for its signature there).

- [ ] **Step 2: Wire into the scene via a temporary batchmode Editor script**

No one is at an interactive Editor session for this step (following the same pattern already used for `UpdateChecker` in the installer feature). Create `Assets/Editor/TempSceneBootstrap_MapScreens.cs`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldGen.Rendering;

public static class TempSceneBootstrap_MapScreens
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        var mapRenderer = Object.FindFirstObjectByType<WorldMapRenderer>();
        var mapEditorPanel = Object.FindFirstObjectByType<MapEditorPanel>();
        var mapLegend = Object.FindFirstObjectByType<MapLegendUI>();
        var projectMenuBar = Object.FindFirstObjectByType<ProjectMenuBar>();

        var genScreenGO = new GameObject("GenerationScreenUI");
        var genScreen = genScreenGO.AddComponent<GenerationScreenUI>();
        genScreen.projectMenuBar = projectMenuBar;

        var progressGO = new GameObject("GenerationProgressUI");
        var progressScreen = progressGO.AddComponent<GenerationProgressUI>();

        var controllerGO = new GameObject("MapScreenController");
        var controller = controllerGO.AddComponent<MapScreenController>();
        controller.mapRenderer = mapRenderer;
        controller.generationScreen = genScreen;
        controller.progressScreen = progressScreen;
        controller.mapEditorPanelGO = mapEditorPanel.gameObject;
        controller.mapLegendUiGO = mapLegend.gameObject;

        genScreen.controller = controller;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
```

Run:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -executeMethod TempSceneBootstrap_MapScreens.Run -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task9_wire.log"
```

Expected: exit 0, log ends with `Exiting batchmode successfully now!`, no `error CS` lines.

- [ ] **Step 3: Verify the diff, then delete the temporary bootstrap script**

```bash
git -C "d:/D&D" status --porcelain -- "Assets/Scenes/SampleScene.unity" "Assets/Editor/"
```

Expected: scene modified (3 new GameObjects + field wiring), `TempSceneBootstrap_MapScreens.cs` + `.meta` untracked. Delete both:

```bash
rm "d:/D&D/Assets/Editor/TempSceneBootstrap_MapScreens.cs" "d:/D&D/Assets/Editor/TempSceneBootstrap_MapScreens.cs.meta"
```

- [ ] **Step 4: Manual verification in Play mode**

Enter Play mode with a fresh project (no map loaded). Confirm: the Generation screen appears (map editor/legend hidden); filling the form and clicking "Сгенерировать карту" shows the Progress screen with the 5-step checklist advancing; on completion, the normal Карта/Редактор/Точки screen appears with a real generated map matching the chosen size/shape/seed. Click "Отмена" mid-generation on a fresh attempt — confirm it returns to the Generation screen. Toggle Тёмная/Светлая theme (Task 5's menu item) and confirm every screen (including the new two) recolors together.

- [ ] **Step 5: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Rendering/MapScreenController.cs" "Assets/Scenes/SampleScene.unity"
git -C "d:/D&D" commit -m "feat: wire MapScreenController, gate map editor/legend behind generated-map state"
```

---

## Self-Review Notes

- **Spec coverage:** Theme system (Task 1), full 21-file inventory (Tasks 2–5, with 7 files found on inspection to need zero changes — documented per-file rather than silently dropped), staged generation pipeline (Task 6), Generation screen (Task 7), Progress screen (Task 8), screen-switching + scene wiring (Task 9). All spec sections have a corresponding task.
- **Placeholder scan:** no TBD/TODO. The recolor tasks (2–5) use per-line tables instead of prose ("figure out the right color") — every literal has an explicit, named role (or an explicit "leave unchanged" with the reason), which is a complete instruction, not a vague one.
- **Type consistency:** `ThemeRole`/`Theme`/`ThemeService.Tag/Get/ApplyTheme/Current` (Task 1) used identically in Tasks 2–9. `WorldGen.Rendering.GenerationParams`/`MapSizePreset`/`LandShapePreset`/`GenerationScreenUI.StableSeedHash` (Task 7) match exactly what `MapScreenController.StartGeneration` (Task 9) consumes. `GenerationProgressUI.SetStep`/`OnCancelRequested` (Task 8) match exactly what `MapScreenController` (Task 9) calls/subscribes to. `WorldGenerator.GenerateWorldStepped`'s callback signatures (Task 6) match exactly how `MapScreenController.RunGeneration` (Task 9) invokes it.
- **Verified during self-review:** `WorldMapRenderer.LoadFromCells(List<VoronoiCell> loadedCells, GenerationParams referenceParams)` (`WorldMapRenderer.cs:189`) matches exactly how Task 9's `RunGeneration` calls it — confirmed against the actual declaration, not assumed.
