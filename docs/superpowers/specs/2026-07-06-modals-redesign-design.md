# Modals Redesign (Screen F) — Design

**Date:** 2026-07-06
**Status:** Approved, ready for implementation planning
**Branch:** implement off `main`

---

## Goal

Bring Screen F ("Модальные диалоги") from `design_handoff_realmweaver_ui/README.md` into
`ConfirmDialog.cs`. Last of four sub-projects (A→C→D→**F**).

---

## Current State

`Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs`, static class:
- `Show(Font font, string message, System.Action<bool> onResult)` — 2-button confirm/cancel ("Отмена"/"Удалить").
- `ShowInfo(Font font, string message, System.Action onDismiss = null)` — 1-button acknowledge ("OK").
- No backdrop (nothing blocks clicks on what's behind the dialog). Dialog is a plain 300×120px `Image` panel, no icon, no separate title (single message `Text` only).
- 10 call sites across 4 files depend on the current signatures (see table below).

---

## Scope

**In scope:**
1. New signatures:
   - `Show(Font font, string title, string message, System.Action<bool> onResult)`
   - `ShowInfo(Font font, string title, string message, System.Action onDismiss = null, System.Action onDetails = null)`
2. Update all 10 existing call sites with a title/body split (table below).
3. Real dimmed backdrop (blocks input on what's behind, does **not** dismiss on outside-click — forces an explicit button, appropriate for destructive confirmations).
4. Icon plate: `Show` gets a red-tinted plate with a drawn "!" glyph (danger); `ShowInfo` gets an accent-tinted plate with a drawn "i" glyph (info) — plain ASCII glyphs drawn the same pixel-grid way `PoiPlaceholderFactory` already does, not the Unicode ⚠/ⓘ characters (font-coverage risk).
5. Separate bold Title text + muted body Text (was: one message text).
6. Dialog width 388px (was 300×120, height now content-driven), radius ~14 (approximated per existing project convention for rounded corners).
7. `ShowInfo`'s new "Подробнее" (secondary) button only renders when `onDetails != null` is passed — all 10 current call sites pass `null`, so they keep today's single-button ("Ок") layout unchanged; the parameter exists for future use.

**Call site title/body split:**

| Call site | Title | Body |
|---|---|---|
| `Assets/WorldGen/Update/UpdateChecker.cs:242` | `"Не удалось скачать обновление"` | `request.error` |
| `Assets/WorldGen/Update/UpdateChecker.cs:261` | `"Не удалось запустить установщик"` | `ex.Message` |
| `Assets/WorldGen/Rendering/ProjectMenuBar.cs:85` | `"Карта ещё не создана"` | `"Сначала сгенерируйте карту."` |
| `Assets/WorldGen/Rendering/ProjectMenuBar.cs:98` | `"Не удалось сохранить файл"` | `ex.Message` |
| `Assets/WorldGen/Rendering/ProjectMenuBar.cs:127` | `"Ошибка"` | `result.ErrorMessage` |
| `Assets/WorldGen/Rendering/ProjectMenuBar.cs:131` | `"Предупреждение"` | `result.WarningMessage` |
| `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs:336` | `"Удалить группу?"` | `$"«{group.Title}» и все её страницы ({group.Pages.Count})"` |
| `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs:392` | `"Удалить страницу?"` | `$"«{page.Name}»"` |
| `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs:143` | `"Удалить объект?"` | `$"«{DescribeObject(data)}»"` |
| `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs:156` | `"Удалить связь?"` | `""` (empty — nothing more to say) |

**Out of scope (this phase):**
- Main-screen shell, Editor-brush panel, POI screen — other specs (done).
- Any actual "Подробнее" content/behavior — the button is wired but unused by every current call site.
- New call sites beyond the 10 that exist today.

---

## Design

### Backdrop
A full-screen `Image` (semi-transparent black, `rgba(0,0,0,.55)` per the mockup) with a `Button` component that swallows clicks but has an empty `onClick` (no listener) — blocks interaction with anything behind the dialog without dismissing it. Sits behind the dialog panel in the same overlay `Canvas` (`sortingOrder = 32000`, unchanged).

### Dialog panel
`Image`, `ThemeRole.Panel`, width 388px, height content-driven (title + body + icon + buttons, laid out top-to-bottom via `VerticalLayoutGroup` similar to other panels in the project), centered (`anchorMin/Max = (0.5,0.5)`), radius ~14 (rounded-corner sprite or plain rect, per the project's existing allowance for skipping exact radii — see the theme-system spec's precedent).

### Icon plate
36×36-ish tinted square/circle (implementer's call on exact size, matching the mockup's proportions) — `Danger`-tinted (using `ThemeService`'s existing danger role/alpha) for `Show`, `AccentSoft`/`Accent`-tinted for `ShowInfo`. Glyph: a hand-drawn "!" or "i" via the same pixel-grid technique `PoiPlaceholderFactory` uses (a small static helper, doesn't need its own file — a private method in `ConfirmDialog.cs` is fine given it's only 2 simple glyphs).

### Title + body text
Title: bold, `Txt` role, above the body. Body: `Mut` role, smaller, below the title. Empty body string (the "Удалить связь?" case) just renders an empty `Text` — no special-case handling needed.

### Buttons
- `Show`: "Отмена" (`Elev`, secondary) + "Удалить" (`Danger`, primary danger) — same roles as today, repositioned to the new layout.
- `ShowInfo`: "Ок" (`Accent`, primary) always; "Подробнее" (`Elev`, secondary) additionally rendered, to the left of "Ок", only when `onDetails != null`.

---

## Error Handling

No new error paths. Empty body string is valid input, not an error.

---

## Testing

- **Self-Test: Backdrop Blocks Input** — verify the backdrop `Button`'s raycast target is enabled and it sits behind the dialog panel in sibling order (so it visually renders behind but still blocks raycasts to anything further behind it).
- **Self-Test: Details Button Visibility** — call `ShowInfo` once with `onDetails = null` and once with a non-null callback, assert the "Подробнее" button GameObject is inactive/active respectively.
- **Manual:** all 10 call sites still show the correct title/body; backdrop dims the background and blocks clicks without dismissing; danger vs info icon plates render with correct tint/glyph; dialog width/radius match the mockup closely.

---

## Out of Scope (this phase)

- All other screens (done).
- Real "Подробнее" content at any call site.
