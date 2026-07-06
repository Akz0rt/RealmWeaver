# Modals Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `title` parameter to `ConfirmDialog.Show`/`ShowInfo`, a real blocking backdrop, icon plates, and update all 10 existing call sites, per `docs/superpowers/specs/2026-07-06-modals-redesign-design.md`. Last of four sub-projects (A→C→D→**F**).

**Architecture:** `ConfirmDialog.cs` is rewritten in full (small file, ~110 lines today). Its two public methods gain a `title` parameter and `ShowInfo` gains an optional `onDetails` callback. All 10 call sites across 4 files are updated to pass a title.

**Tech Stack:** C# runtime `UnityEngine.UI`, `ThemeService.Tag(...)`.

## Global Constraints

- Backdrop blocks clicks on what's behind but does **not** dismiss the dialog on outside-click.
- Danger button text is **literal `Color.white`**, not a `ThemeRole` — the mockup specifies "деструктив ... белый текст" in both themes, and `ThemeRole.AccentInk` is WRONG here (it's near-black in Dark theme, meant only for text on a solid `Accent` background — using it on `Danger` would repeat the exact `AccentInk`-misuse contrast bug already found and fixed twice earlier in this project's theme-system work). Every other button label uses a `ThemeRole` (`AccentInk` on `Accent`, `Txt` on `Elev`).
- "Подробнее" only renders when `onDetails != null` — none of the 10 call sites pass one, so none of them show it.
- No automated test runner — `[ContextMenu("Self-Test: ...")]` + manual Play-mode verification.

---

### Task 1: `ConfirmDialog.cs` full rewrite

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs` (full rewrite — the whole file is ~110 lines today, shown in full below)

**Interfaces:**
- Produces: `public static void Show(Font font, string title, string message, System.Action<bool> onResult)`, `public static void ShowInfo(Font font, string title, string message, System.Action onDismiss = null, System.Action onDetails = null)`. Consumed by Task 2 (all 10 call sites).

- [ ] **Step 1: Replace `ConfirmDialog.cs` in full**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Shared modal dialogs. Only one dialog is ever shown at once (Show/ShowInfo both replace
    /// the previous one). Backdrop blocks clicks on everything behind but does NOT dismiss on
    /// outside-click - destructive confirmations should require an explicit button press.
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject activeDialogGO;

        public static void Show(Font font, string title, string message, System.Action<bool> onResult)
        {
            var panelGO = BuildBasePanel(font, title, message, ThemeRole.Danger, "!");

            AddButtonRow(font, panelGO.transform, new (string, ThemeRole, System.Action)[]
            {
                ("Отмена", ThemeRole.Elev, () => { Object.Destroy(activeDialogGO); onResult(false); }),
                ("Удалить", ThemeRole.Danger, () => { Object.Destroy(activeDialogGO); onResult(true); }),
            });
        }

        /// <summary>Single/double-button acknowledgement dialog, for errors/warnings/info.
        /// onDetails is optional - the "Подробнее" button only renders when it's non-null.</summary>
        public static void ShowInfo(Font font, string title, string message, System.Action onDismiss = null, System.Action onDetails = null)
        {
            var panelGO = BuildBasePanel(font, title, message, ThemeRole.Accent, "i");

            var buttons = new List<(string, ThemeRole, System.Action)>();
            if (onDetails != null)
                buttons.Add(("Подробнее", ThemeRole.Elev, onDetails));
            buttons.Add(("Ок", ThemeRole.Accent, () => { Object.Destroy(activeDialogGO); onDismiss?.Invoke(); }));

            AddButtonRow(font, panelGO.transform, buttons.ToArray());
        }

        static GameObject BuildBasePanel(Font font, string title, string message, ThemeRole iconRole, string glyph)
        {
            if (activeDialogGO != null) Object.Destroy(activeDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeDialogGO = canvasGO;

            var backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(canvasGO.transform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.55f);
            var backdropBtn = backdropGO.AddComponent<Button>(); // swallows clicks, no listener - does not dismiss
            backdropBtn.transition = Selectable.Transition.None;
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.sizeDelta = Vector2.zero;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            ThemeService.Tag(panelImg, ThemeRole.Panel);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(388f, 0f);
            panelRect.anchoredPosition = Vector2.zero;

            var layout = panelGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeaderRow(font, panelGO.transform, title, iconRole, glyph);
            BuildBodyText(font, panelGO.transform, message);

            return panelGO;
        }

        static void BuildHeaderRow(Font font, Transform parent, string title, ThemeRole iconRole, string glyph)
        {
            var rowGO = new GameObject("Header");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var iconGO = new GameObject("IconPlate");
            iconGO.transform.SetParent(rowGO.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            ThemeService.Tag(iconImg, iconRole, 0.2f);
            iconGO.AddComponent<LayoutElement>().preferredWidth = 36f;
            iconGO.GetComponent<LayoutElement>().preferredHeight = 36f;

            var glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(iconGO.transform, false);
            var glyphText = glyphGO.AddComponent<Text>();
            glyphText.text = glyph;
            glyphText.font = font;
            glyphText.fontSize = 18;
            glyphText.fontStyle = FontStyle.Bold;
            glyphText.alignment = TextAnchor.MiddleCenter;
            ThemeService.Tag(glyphText, iconRole);
            var glyphRect = glyphGO.GetComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.sizeDelta = Vector2.zero;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(rowGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = title;
            titleText.font = font;
            titleText.fontSize = 15;
            titleText.fontStyle = FontStyle.Bold;
            ThemeService.Tag(titleText, ThemeRole.Txt);
            titleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        static void BuildBodyText(Font font, Transform parent, string message)
        {
            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(parent, false);
            var msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = font;
            msgText.fontSize = 12;
            ThemeService.Tag(msgText, ThemeRole.Mut);
            msgGO.AddComponent<LayoutElement>().preferredHeight = string.IsNullOrEmpty(message) ? 0f : 40f;
        }

        static void AddButtonRow(Font font, Transform parent, (string label, ThemeRole role, System.Action onClick)[] buttons)
        {
            var rowGO = new GameObject("Buttons");
            rowGO.transform.SetParent(parent, false);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            foreach (var (label, role, onClick) in buttons)
            {
                var go = new GameObject($"Btn_{label}");
                go.transform.SetParent(rowGO.transform, false);
                var img = go.AddComponent<Image>();
                ThemeService.Tag(img, role);
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onClick?.Invoke());
                go.AddComponent<LayoutElement>().preferredWidth = 100f;
                go.GetComponent<LayoutElement>().preferredHeight = 36f;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(go.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = font;
                text.fontSize = 12;
                text.alignment = TextAnchor.MiddleCenter;
                if (role == ThemeRole.Danger)
                    // Mockup: destructive button text is always white in both themes, same as the
                    // danger red itself being a theme-independent literal - NOT ThemeRole.AccentInk,
                    // which is near-black in Dark theme and only correct for text on Accent bg.
                    text.color = Color.white;
                else
                    ThemeService.Tag(text, role == ThemeRole.Accent ? ThemeRole.AccentInk : ThemeRole.Txt);

                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }
        }

        [ContextMenu("Self-Test: Details Button Visibility")]
        public static void SelfTestDetailsButtonVisibility()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            ShowInfo(font, "Test", "no details callback");
            bool noDetailsButtonShown = activeDialogGO.transform.Find("Panel/Buttons")?.childCount == 1;

            ShowInfo(font, "Test", "with details callback", onDetails: () => { });
            bool detailsButtonShown = activeDialogGO.transform.Find("Panel/Buttons")?.childCount == 2;

            Object.Destroy(activeDialogGO);

            bool ok = noDetailsButtonShown && detailsButtonShown;
            Debug.Log(ok
                ? "Self-Test Details Button Visibility: PASS"
                : $"Self-Test Details Button Visibility: FAIL (noDetailsButtonShown={noDetailsButtonShown}, detailsButtonShown={detailsButtonShown})");
        }
    }
}
```

- [ ] **Step 2: Manual verification**

This is a static class — the self-test is a `[ContextMenu]` on the class itself, which Unity only shows in the Inspector for MonoBehaviour components, not static classes. Run it instead via a temporary call from any existing MonoBehaviour's `Start()`, or verify manually: Play mode, trigger any `ShowInfo` call site (e.g. try to Save without a generated map first, at `ProjectMenuBar.cs:85`) — confirm title/body/backdrop/single-button layout; temporarily pass a non-null `onDetails` lambda at one call site to visually confirm the second button appears, then revert that temporary change before committing.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs
git commit -m "feat: rewrite ConfirmDialog with title, backdrop, icon plate, optional details button"
```

---

### Task 2: Update all 10 call sites

**Files:**
- Modify: `Assets/WorldGen/Update/UpdateChecker.cs` (2 call sites)
- Modify: `Assets/WorldGen/Rendering/ProjectMenuBar.cs` (4 call sites)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs` (2 call sites)
- Modify: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs` (2 call sites)

**Interfaces:**
- Consumes: `ConfirmDialog.Show(Font, string, string, Action<bool>)`, `.ShowInfo(Font, string, string, Action, Action)` (Task 1).

- [ ] **Step 1: `Assets/WorldGen/Update/UpdateChecker.cs`**

Line 242 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, $"Не удалось скачать обновление: {request.error}");
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Не удалось скачать обновление", request.error);
```

Line 261 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, $"Не удалось запустить установщик: {ex.Message}");
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Не удалось запустить установщик", ex.Message);
```

- [ ] **Step 2: `Assets/WorldGen/Rendering/ProjectMenuBar.cs`**

Line 85 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Сначала сгенерируйте карту.");
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Карта ещё не создана", "Сначала сгенерируйте карту.");
```

Line 98 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, $"Не удалось сохранить файл: {ex.Message}");
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Не удалось сохранить файл", ex.Message);
```

Line 127 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, result.ErrorMessage);
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Ошибка", result.ErrorMessage);
```

Line 131 — replace:
```csharp
ConfirmDialog.ShowInfo(builtinFont, result.WarningMessage);
```
with:
```csharp
ConfirmDialog.ShowInfo(builtinFont, "Предупреждение", result.WarningMessage);
```

- [ ] **Step 3: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs`**

Line 336 — replace:
```csharp
() => ConfirmDialog.Show(builtinFont, $"Удалить группу \"{group.Title}\" и все её страницы ({group.Pages.Count})?", confirmed =>
```
with:
```csharp
() => ConfirmDialog.Show(builtinFont, "Удалить группу?", $"«{group.Title}» и все её страницы ({group.Pages.Count})", confirmed =>
```

Line 392 — replace:
```csharp
() => ConfirmDialog.Show(builtinFont, $"Удалить страницу \"{page.Name}\"?", confirmed =>
```
with:
```csharp
() => ConfirmDialog.Show(builtinFont, "Удалить страницу?", $"«{page.Name}»", confirmed =>
```

- [ ] **Step 4: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs`**

Line 143 — replace:
```csharp
ConfirmDialog.Show(builtinFont, $"Удалить \"{DescribeObject(data)}\"?", confirmed =>
```
with:
```csharp
ConfirmDialog.Show(builtinFont, "Удалить объект?", $"«{DescribeObject(data)}»", confirmed =>
```

Line 156 — replace:
```csharp
ConfirmDialog.Show(builtinFont, "Удалить связь?", confirmed =>
```
with:
```csharp
ConfirmDialog.Show(builtinFont, "Удалить связь?", "", confirmed =>
```

- [ ] **Step 5: Verify compile**

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -projectPath "D:\D&D" -quit -logFile -
```

Expect exit code 0, no `error CS` lines — this specifically confirms every call site's argument count/order matches `ConfirmDialog`'s new signatures.

- [ ] **Step 6: Manual verification**

Play mode: trigger each of the 10 call sites where feasible (Save without a map, delete a Notes group/page, delete a canvas object/link, etc.) and confirm the correct title/body text renders per the table in the design spec.

- [ ] **Step 7: Commit**

```bash
git add Assets/WorldGen/Update/UpdateChecker.cs Assets/WorldGen/Rendering/ProjectMenuBar.cs Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "feat: update all ConfirmDialog call sites with new title parameter"
```

---

## Self-Review Notes

- **Spec coverage:** Task 1 covers the new API/backdrop/icon plate/title-body split; Task 2 covers every one of the 10 call sites listed in the spec's table, with exact matching title/body text.
- **Placeholder scan:** none — every call site's before/after code is shown verbatim.
- **Type consistency:** `Show`/`ShowInfo`'s new signatures defined once in Task 1 and used identically (argument order: font, title, message, then callbacks) at every Task 2 call site.
- **Danger contrast check:** explicitly does NOT reuse `ThemeRole.AccentInk` for the Danger button's text (would repeat this project's own previously-fixed `AccentInk`-on-non-Accent-background contrast bug) — uses literal `Color.white` instead, matching the mockup's explicit "always white" rule for the destructive button.
