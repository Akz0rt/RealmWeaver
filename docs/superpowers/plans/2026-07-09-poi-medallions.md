# POI Medallions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade POI markers to the handoff "medallion" look (dark disc + glow + accent ring + procedural stone-tone icon) and expand `PoiType` from 5 to 11 types, updating the type-picker and list-filter UI accordingly.

**Architecture:** Extend the existing single-point `PoiPlaceholderFactory.GetPlaceholder(PoiType)` (used by map markers, list rows, and the edit-panel type buttons) so one rewrite upgrades all three. Expand the `PoiType` enum append-only (int serialization). No new files.

**Tech Stack:** Unity 6000.3.2f1, C#, `Texture2D`/`Sprite` procedural drawing, `UnityEngine.UI`.

**Spec:** `docs/superpowers/specs/2026-07-09-poi-medallions-design.md`

## Global Constraints

- Agents cannot run Unity → `[ContextMenu]`/self-tests are the USER's Editor step; reviews are static. Visual medallion look is user-verified.
- **`PoiType` is serialized as int** (`ProjectSerializer` uses Newtonsoft with NO `StringEnumConverter`). Expand the enum **append-only with explicit int values**; NEVER reorder existing values (`Unknown=0, City=1, Ruin=2, Dungeon=3, Fortress=4`). New: `Village=5, Tower=6, Temple=7, Encounter=8, Camp=9, Port=10`.
- `GetPlaceholder(PoiType)` signature + per-type cache unchanged; consumers (`PoiManager`/`PoiMarkerView`/`PoiEditPanel`/`PoiToolPanel`) keep calling it.
- Medallion palette (fixed, theme-independent): disc `#141c25`→`#080d14`, rim `#0a0d12`, accent `#e6b25c`; icon stone tones `dark #2b323d`, `light #414c5b`, `black #0a0d12`, `steel #c9d2dc`, `wood #4a3a28`.
- Sprite: 128×128, `TextureFormat.RGBA32`, `FilterMode.Bilinear`.
- `CustomIconBytes`/`CustomSpritePath` override path in `PoiData` is unchanged (custom icons still override the medallion).
- Deferred (NOT this plan): POI label restyle, theme-tied accent, labels/chrome/fog, real icon art.

---

### Task 1: Expand PoiType enum + serialization round-trip test

**Files:**
- Modify: `Assets/WorldGen/Generation/PoiData.cs`
- Modify: `Assets/WorldGen/Persistence/ProjectSerializerSelfTests.cs`

**Interfaces:**
- Produces: `enum PoiType { Unknown=0, City=1, Ruin=2, Dungeon=3, Fortress=4, Village=5, Tower=6, Temple=7, Encounter=8, Camp=9, Port=10 }`.

- [ ] **Step 1: Expand the enum (append-only, explicit ints)**

In `PoiData.cs` replace the enum line:
```csharp
    public enum PoiType { Unknown = 0, City = 1, Ruin = 2, Dungeon = 3, Fortress = 4, Village = 5, Tower = 6, Temple = 7, Encounter = 8, Camp = 9, Port = 10 }
```
(Explicit ints lock the wire format so a future accidental reorder can't corrupt saves.)

- [ ] **Step 2: Add a backward-compat serialization self-test**

Read `ProjectSerializerSelfTests.cs` and follow its existing self-test pattern (how it serializes/deserializes a `PoiData` and asserts — mirror the existing `poiPlain` round-trip at ~line 60). Add a test that:
```csharp
        // New POI types round-trip (append-only enum); an "old" save with an existing int still loads.
        var poiNew = new PoiData { Type = PoiType.Port, Name = "Гавань", OwnerCellId = 2 };
        string json = SerializePoi(poiNew);              // use the same serialize helper the existing test uses
        var back = DeserializePoi(json);                 // and the same deserialize helper
        bool ok = back.Type == PoiType.Port;
        // Existing int value still maps to Fortress (guards against accidental reorder).
        var oldFortress = DeserializePoi(json.Replace("\"Type\":10", "\"Type\":4"));
        ok &= oldFortress.Type == PoiType.Fortress;
```
Match the file's ACTUAL helper names/serialization call (grep for how it builds the `JsonSerializerSettings`/serializes `PoiData`; the snippet above names are illustrative — use the real ones). Emit a `Debug.Log` PASS/FAIL like the sibling tests.

- [ ] **Step 3: USER runs the self-test in Editor**

Run the project serializer self-test(s) via their `[ContextMenu]`/runner. Expected: PASS (new `Port` type round-trips; `"Type":4` still deserializes to `Fortress`).

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Generation/PoiData.cs Assets/WorldGen/Persistence/ProjectSerializerSelfTests.cs
git commit -m "feat(poi): expand PoiType to 11 (append-only) + serialization round-trip test"
```

---

### Task 2: Medallion sprite — frame + 11 procedural icons (rewrite PoiPlaceholderFactory)

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs`

**Interfaces:**
- Consumes: `PoiType` (Task 1).
- Produces: `PoiPlaceholderFactory.GetPlaceholder(PoiType) : Sprite` (unchanged signature; 128×128 medallion sprite).

- [ ] **Step 1: Rewrite the factory — palette, buffer helpers, frame**

Replace the whole class body with the following (drawing works in a `Color32[]` buffer, y-up = row 0 at bottom, then `SetPixels32` directly — no flip):
```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Builds a 128x128 "medallion" sprite per PoiType (dark disc + glow + accent ring +
    /// procedural stone-tone icon) and caches one shared instance per type — reused by on-map
    /// markers, the POI list rows, and the edit-panel type buttons. Icons are procedural placeholders;
    /// a per-POI CustomIconBytes still overrides this (see PoiData/PoiMarkerView).</summary>
    public static class PoiPlaceholderFactory
    {
        const int S = 128;
        static readonly Dictionary<PoiType, Sprite> cache = new Dictionary<PoiType, Sprite>();

        // Medallion + icon palette (fixed, theme-independent).
        static readonly Color32 DiscC = new Color32(0x14, 0x1c, 0x25, 255); // disc center
        static readonly Color32 DiscE = new Color32(0x08, 0x0d, 0x14, 255); // disc edge
        static readonly Color32 Rim   = new Color32(0x0a, 0x0d, 0x12, 255);
        static readonly Color32 Acc   = new Color32(0xe6, 0xb2, 0x5c, 255);
        static readonly Color32 Dark  = new Color32(0x2b, 0x32, 0x3d, 255);
        static readonly Color32 Light = new Color32(0x41, 0x4c, 0x5b, 255);
        static readonly Color32 Black = new Color32(0x0a, 0x0d, 0x12, 255);
        static readonly Color32 Steel = new Color32(0xc9, 0xd2, 0xdc, 255);
        static readonly Color32 Wood  = new Color32(0x4a, 0x3a, 0x28, 255);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public static Sprite GetPlaceholder(PoiType type)
        {
            if (cache.TryGetValue(type, out var s)) return s;
            s = Build(type);
            cache[type] = s;
            return s;
        }

        static Sprite Build(PoiType type)
        {
            var buf = new Color32[S * S];
            DrawFrame(buf);
            DrawIcon(buf, type);

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = $"PoiMedallion_{type}" };
            tex.SetPixels32(buf);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        }

        // ---- buffer helpers (y-up: buf[y*S + x]) ----
        static void Px(Color32[] b, int x, int y, Color32 c)
        { if ((uint)x < S && (uint)y < S) b[y * S + x] = c; }

        static void FillRect(Color32[] b, int x0, int y0, int x1, int y1, Color32 c)
        { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Px(b, x, y, c); }

        static void HLine(Color32[] b, int x0, int x1, int y, Color32 c)
        { for (int x = x0; x <= x1; x++) Px(b, x, y, c); }

        static void VLine(Color32[] b, int x, int y0, int y1, Color32 c)
        { for (int y = y0; y <= y1; y++) Px(b, x, y, c); }

        static void Disc(Color32[] b, float cx, float cy, float r, Color32 c)
        {
            int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
            int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            { float dx = x - cx, dy = y - cy; if (dx * dx + dy * dy <= r * r) Px(b, x, y, c); }
        }

        // Isoceles triangle pointing up: apex at (cx, apexY), base half-width halfW at baseY (baseY<apexY).
        static void TriUp(Color32[] b, float cx, int baseY, int apexY, float halfW, Color32 c)
        {
            for (int y = baseY; y <= apexY; y++)
            {
                float t = (y - baseY) / (float)(apexY - baseY); // 0 at base, 1 at apex
                int hw = Mathf.RoundToInt(halfW * (1f - t));
                HLine(b, Mathf.RoundToInt(cx) - hw, Mathf.RoundToInt(cx) + hw, y, c);
            }
        }

        static void DrawFrame(Color32[] b)
        {
            float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f;
            float R = S * 0.5f;
            float rDisc = R - 2f;              // disc outer
            float rimW = 5f, accW = 3f;        // 2.6/1.6 * (S/64=2)
            float rAccOut = rDisc - rimW;      // accent ring outer
            float rAccIn = rAccOut - accW;     // disc interior starts here
            float rGlow = R;                   // soft glow reaches the texture edge
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rDisc)
                {
                    // soft dark outer glow, alpha fades out past the disc
                    float t = Mathf.InverseLerp(rGlow, rDisc, d); // 0 at edge → 1 at disc
                    byte a = (byte)Mathf.Clamp(t * 90f, 0, 90);
                    if (a > 0) b[y * S + x] = new Color32(0x0a, 0x0d, 0x14, a);
                }
                else if (d > rAccOut) b[y * S + x] = Rim;
                else if (d > rAccIn) b[y * S + x] = Acc;
                else
                {
                    float t = Mathf.Clamp01(d / rAccIn);        // 0 center → 1 inner edge
                    b[y * S + x] = Color32.Lerp(DiscC, DiscE, t);
                }
            }
        }
```

- [ ] **Step 2: Add the icon dispatcher + all 11 icon routines**

Append inside the class (icons drawn centered on the disc; sizes chosen to sit within ~82% of the disc; helpers above):
```csharp
        static void DrawIcon(Color32[] b, PoiType type)
        {
            switch (type)
            {
                case PoiType.City:      City(b); break;
                case PoiType.Fortress:  Fortress(b); break;
                case PoiType.Village:   Village(b); break;
                case PoiType.Tower:     Tower(b); break;
                case PoiType.Temple:    Temple(b); break;
                case PoiType.Ruin:      Ruin(b); break;
                case PoiType.Dungeon:   Dungeon(b); break;
                case PoiType.Encounter: Encounter(b); break;
                case PoiType.Camp:      Camp(b); break;
                case PoiType.Port:      Port(b); break;
                default:                Unknown(b); break;
            }
        }

        const int C = S / 2; // center

        // Crenellated block with `teeth` merlons across the top. Returns nothing; fills [x0..x1]x[y0..topWithTeeth].
        static void Keep(Color32[] b, int x0, int x1, int y0, int bodyTop, int teeth, Color32 body, Color32 top)
        {
            FillRect(b, x0, y0, x1, bodyTop, body);
            int w = x1 - x0 + 1, step = Mathf.Max(2, w / (teeth * 2 - 1));
            for (int i = 0; i < teeth; i++)
            { int mx = x0 + i * step * 2; FillRect(b, mx, bodyTop + 1, Mathf.Min(mx + step - 1, x1), bodyTop + step, top); }
        }

        static void Flag(Color32[] b, int poleX, int topY, int h) // pole + accent pennant
        { VLine(b, poleX, topY - h, topY, Steel); FillRect(b, poleX + 1, topY - h, poleX + 8, topY - h + 5, Acc); }

        static void Unknown(Color32[] b) // accent "?"
        {
            HLine(b, C - 8, C + 6, C + 18, Acc); VLine(b, C + 6, C + 8, C + 18, Acc);
            VLine(b, C - 8, C + 4, C + 8, Acc); VLine(b, C, C - 6, C + 4, Acc); HLine(b, C - 8, C, C + 4, Acc);
            FillRect(b, C - 2, C - 16, C + 1, C - 13, Acc); // dot
        }

        static void City(Color32[] b) // crenellated keep + flag
        { Keep(b, C - 20, C + 16, C - 22, C + 10, 4, Dark, Light); FillRect(b, C - 6, C - 22, C + 2, C - 8, Black); Flag(b, C + 14, C + 12, 22); }

        static void Fortress(Color32[] b) // three towers, center tall, + flag
        {
            Keep(b, C - 24, C - 10, C - 16, C + 6, 2, Dark, Light);
            Keep(b, C + 8, C + 22, C - 16, C + 6, 2, Dark, Light);
            Keep(b, C - 8, C + 6, C - 22, C + 14, 2, Dark, Light);
            Flag(b, C - 1, C + 16, 20);
        }

        static void Village(Color32[] b) // two gabled houses
        {
            FillRect(b, C - 22, C - 16, C - 4, C + 2, Dark); TriUp(b, C - 13, C + 2, C + 12, 11, Light);
            FillRect(b, C + 2, C - 16, C + 20, C - 2, Dark);  TriUp(b, C + 11, C - 2, C + 8, 11, Light);
        }

        static void Tower(Color32[] b) // single battlemented tower + flag
        { Keep(b, C - 9, C + 9, C - 22, C + 10, 3, Dark, Light); FillRect(b, C - 3, C - 6, C + 3, C + 4, Black); Flag(b, C + 7, C + 14, 22); }

        static void Temple(Color32[] b) // colonnade + pediment
        {
            TriUp(b, C, C + 6, C + 20, 22, Light);            // pediment
            FillRect(b, C - 22, C + 4, C + 22, C + 6, Light); // architrave
            for (int i = -2; i <= 2; i++) VLine(b, C + i * 9, C - 20, C + 3, Steel); // columns
            FillRect(b, C - 24, C - 22, C + 24, C - 20, Dark); // base
        }

        static void Ruin(Color32[] b) // two broken columns + fallen lintel
        {
            VLine(b, C - 12, C - 14, C + 8, Steel); VLine(b, C - 11, C - 14, C + 4, Steel);
            VLine(b, C + 10, C - 14, C + 14, Steel); VLine(b, C + 11, C - 14, C + 10, Steel);
            FillRect(b, C - 20, C - 20, C - 2, C - 16, Dark); // fallen lintel on the ground
        }

        static void Dungeon(Color32[] b) // stone gate + dark arch + portcullis
        {
            FillRect(b, C - 18, C - 20, C + 18, C + 16, Dark); Disc(b, C, C + 16, 18, Dark);
            FillRect(b, C - 12, C - 20, C + 12, C + 12, Black); Disc(b, C, C + 12, 12, Black); // arch void
            for (int i = -2; i <= 2; i++) VLine(b, C + i * 5, C - 20, C + 10, Steel); // portcullis bars
            HLine(b, C - 12, C + 12, C - 4, Steel); HLine(b, C - 12, C + 12, C + 4, Steel);
        }

        static void Encounter(Color32[] b) // crossed swords
        {
            for (int i = -16; i <= 16; i++)
            { Px(b, C + i, C + i, Steel); Px(b, C + i + 1, C + i, Steel); Px(b, C + i, C - i, Steel); Px(b, C + i + 1, C - i, Steel); }
            FillRect(b, C - 6, C - 20, C + 6, C - 16, Acc); // crossguards hint
        }

        static void Camp(Color32[] b) // tent + crossed apex poles
        {
            TriUp(b, C, C - 18, C + 16, 22, Dark);
            VLine(b, C, C - 18, C + 20, Black); // center seam
            Px(b, C - 6, C + 20, Steel); for (int i = 0; i < 8; i++) { Px(b, C - 6 + i, C + 20 - i, Steel); Px(b, C + 6 - i, C + 20 - i, Steel); } // crossed poles
        }

        static void Port(Color32[] b) // anchor
        {
            VLine(b, C, C - 18, C + 16, Steel);              // shank
            HLine(b, C - 10, C + 10, C - 12, Steel);         // stock
            Disc(b, C, C + 16, 4, Steel); Disc(b, C, C + 16, 2, Black); // ring
            for (int i = 0; i <= 12; i++) { Px(b, C - 16 + i, C - 18 + i, Steel); Px(b, C + 16 - i, C - 18 + i, Steel); } // flukes
        }
    }
}
```

- [ ] **Step 3: USER visual-verify in Editor**

Recompile; open the POI edit panel (or dump `GetPlaceholder(t)` sprites) and confirm: each of the 11 types shows a dark medallion (glow + disc gradient + gold accent ring) with a recognizable, distinct stone-tone icon; the icons read at marker size on the map. (First-pass placeholder art — expect to tune shapes/sizes like the pine fix.)

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiPlaceholderFactory.cs
git commit -m "feat(poi): medallion sprite (frame + glow + accent ring) + 11 procedural icons"
```

---

### Task 3: UI — type-picker grid (11) + list filters/TypeName (11)

**Files:**
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs`
- Modify: `Assets/WorldGen/Rendering/PoiToolPanel.cs`

**Interfaces:**
- Consumes: `GetPlaceholder(PoiType)`, `PoiType` (11), `poiManager.UpdatePoiType`.

- [ ] **Step 1: PoiEditPanel — grid of type buttons for all 10 assignable types**

Read `PoiEditPanel.cs` around the type-button block (~line 188–213: the four `AddTypeButton(rowGO.transform, PoiType.City, "Город")` calls + `AddTypeButton`/`ApplyTypeHighlight`). Replace the four hardcoded calls with a loop over all assignable types laid out in a grid:
```csharp
            var pickTypes = new (PoiType t, string label)[]
            {
                (PoiType.City, "Город"), (PoiType.Fortress, "Креп."), (PoiType.Village, "Дер."),
                (PoiType.Tower, "Башня"), (PoiType.Temple, "Храм"), (PoiType.Ruin, "Руины"),
                (PoiType.Dungeon, "Подзем."), (PoiType.Encounter, "Встр."), (PoiType.Camp, "Лагерь"),
                (PoiType.Port, "Порт"),
            };
            foreach (var (t, label) in pickTypes) AddTypeButton(rowGO.transform, t, label);
```
Change the row container `rowGO` from a horizontal row to a `GridLayoutGroup` (remove/replace the existing `HorizontalLayoutGroup` on `rowGO`; add `GridLayoutGroup` with e.g. `cellSize = new Vector2(58, 46)`, `spacing = new Vector2(6,6)`, `constraint = GridLayoutGroup.Constraint.FixedColumnCount`, `constraintCount = 4`) so 10 buttons wrap to ~3 rows within the 308-wide panel. Keep `AddTypeButton`/`typeButtons`/`ApplyTypeHighlight` as-is (they already generalize over `PoiType`). If `AddTypeButton` uses only a text label, additionally set the button's icon to `PoiPlaceholderFactory.GetPlaceholder(type)` (add an `Image` child or set an existing icon Image's sprite) so the medallion shows on the button — match how the button currently renders its content.

- [ ] **Step 2: PoiToolPanel — TypeName for all 11**

In `PoiToolPanel.cs`, extend `TypeName(PoiType t)` (~line 609) to cover all 11:
```csharp
        static string TypeName(PoiType t)
        {
            switch (t)
            {
                case PoiType.City: return "Город";
                case PoiType.Ruin: return "Руины";
                case PoiType.Dungeon: return "Подземелье";
                case PoiType.Fortress: return "Крепость";
                case PoiType.Village: return "Деревня";
                case PoiType.Tower: return "Башня";
                case PoiType.Temple: return "Храм";
                case PoiType.Encounter: return "Встреча";
                case PoiType.Camp: return "Лагерь";
                case PoiType.Port: return "Порт";
                default: return "Точка";
            }
        }
```

- [ ] **Step 3: PoiToolPanel — filter chips for the new types**

In `BuildFilterChips` (~line 355) add chips for the new types after the existing ones, keeping "Все" first. The existing code builds two rows (`row1`, `row2`) via `AddChip(row, label, type)`. Add rows as needed so all 11 are filterable:
```csharp
            AddChip(row2, "Деревни", PoiType.Village);
            AddChip(row2, "Башни", PoiType.Tower);
            var row3 = /* create a third chip row exactly like row1/row2 are created */;
            AddChip(row3, "Храмы", PoiType.Temple);
            AddChip(row3, "Встречи", PoiType.Encounter);
            AddChip(row3, "Лагеря", PoiType.Camp);
            AddChip(row3, "Порты", PoiType.Port);
```
Create `row3` by copying the exact construction of `row2` (grep how `row1`/`row2` GameObjects are built — same parent, layout, spacing). Filter logic (`filterType`, `OnFilterChanged`, `poi.Type != filterType.Value`) is unchanged.

- [ ] **Step 4: USER visual-verify in Editor**

Open the POI panel: the edit-panel type picker shows all 10 assignable types as medallion icon-buttons (wrapping in a grid, fitting the panel width); selecting one sets the POI type. The list filter chips include the new types and filter correctly; each list row's "тип · регион" shows the right localized name for all 11.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Rendering/PoiEditPanel.cs Assets/WorldGen/Rendering/PoiToolPanel.cs
git commit -m "feat(poi): type-picker grid + list filters/TypeName for all 11 types"
```

---

## Self-Review

**Spec coverage:**
- 11 types append-only + int-serialization compat → Task 1 (enum + round-trip test). ✓
- Medallion frame (glow/disc/rim/accent ring) → Task 2 Step 1 (`DrawFrame`). ✓
- 11 procedural stone-tone icons → Task 2 Step 2. ✓
- 128px/Bilinear/RGBA32 → Task 2 Step 1 (`Build`). ✓
- `GetPlaceholder` unchanged + cache → Task 2 Step 1. ✓
- Fixed gold accent → Task 2 palette constants. ✓
- Edit-panel picker grid (10 assignable) → Task 3 Step 1. ✓
- TypeName (11) + filters → Task 3 Steps 2–3. ✓
- CustomIconBytes override unchanged → not touched (PoiData/PoiMarkerView out of scope). ✓
- Serialization backward-compat test → Task 1 Step 2. ✓

**Placeholder scan:** No TBD/TODO. Task 1 Step 2 and Task 3 Steps 1/3 give the code to add but instruct grepping the file's REAL helper/row-construction names/signatures (they live in unchanged code the plan can't fully quote) — the added code is verbatim; only the insertion mechanism is grep-located.

**Type consistency:** `PoiType` values (Task 1) used consistently in Tasks 2–3. `GetPlaceholder(PoiType):Sprite` unchanged across factory (Task 2) and picker (Task 3). Icon helper names (`Keep`/`Flag`/`TriUp`/`Disc`/`FillRect`/`HLine`/`VLine`/`Px`) defined in Task 2 Step 1 and used in Step 2.

**Known verification gaps (user, Editor):** all `[ContextMenu]`/visual checks are user-run. Task 1 Step 2 must match `ProjectSerializerSelfTests`'s real serialize/deserialize helpers; Task 3 must match `PoiEditPanel`'s real `AddTypeButton` content rendering and `PoiToolPanel`'s real chip-row construction — the implementer greps these. Icon shapes are a first pass; expect visual tuning.
