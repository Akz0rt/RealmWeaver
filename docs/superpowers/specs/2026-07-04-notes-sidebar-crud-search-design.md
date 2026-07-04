# Notes Sidebar — Rename/Delete/Search — Design

**Date:** 2026-07-04
**Status:** Approved, ready for planning
**Branch:** main

---

## Goal

The notes editor's page-tree sidebar (`NotesTreeSidebar.cs`) can only create groups and pages today — there's no way to rename or delete a group/page from the UI, and no way to search a long list. The data layer (`NotesDocumentController.cs`) already exposes `RenameGroup`, `DeleteGroup`, `RenamePage`, `DeletePage` — this is purely a UI gap.

This adds three things to the sidebar: double-click-to-rename on group titles and page rows, a persistent "×" delete button on each with a confirm dialog, and a search box that filters the list by group title or page name.

## Rename (double-click inline edit)

Each group title and each page row gets a small new component, `DoubleClickToRename`, implementing `IPointerClickHandler` and checking `eventData.clickCount == 2`. On double-click:

- The row's `Text` label is hidden and replaced by an `InputField` (pre-created inactive, same rect, shown on demand) pre-filled with the current title/name and focused for editing.
- **Enter** or **losing focus** (`InputField.onEndEdit`) commits: calls `RenameGroup(groupId, newText)` or `RenamePage(pageId, newText)`. Both already raise `OnDocumentChanged`, which the sidebar already turns into a full `Rebuild()` — so the row naturally returns to its normal `Text` display with the new name, no extra state juggling needed.
- **Escape** cancels: reverts to the `Text` label without calling `Rename*`. Since cancelling doesn't go through `OnDocumentChanged`, this path needs to manually re-toggle `Text`/`InputField` visibility itself (unlike commit, which gets it for free from `Rebuild()`).
- Empty/whitespace-only submitted text is rejected (treated the same as cancel) rather than producing a blank row.

## Delete (persistent "×" button + shared confirm dialog)

Each group title row and each page row gets a small "×" button anchored to its right edge (plain `Text` character — no TextMeshPro in this project, and the base font doesn't render emoji glyphs, so a "🗑" is out; "×" is simple, legible, and matches the existing minimal-button style already used for "+ Группа"/"+ Страница").

Clicking "×" shows a confirm dialog:
- Page: `Удалить страницу "X"?`
- Group: `Удалить группу "X" и все её страницы (N)?` — `DeleteGroup` cascades and removes every page inside it, so the message says so up front rather than silently losing pages.

Confirming calls `DeleteGroup`/`DeletePage` directly — **no undo** (per decision: object/link deletion on the canvas already goes through `NotesUndoManager` for Ctrl+Z, but sidebar group/page deletion is confirm-only, avoiding the extra complexity of snapshotting an entire deleted page's objects/links for restoration).

**Shared `ConfirmDialog` extraction:** `NotesUndoManager.ShowConfirmDialog`/`AddDialogButton` (~55 lines) already build exactly this kind of modal (message + Отмена/Удалить buttons, single-instance-at-a-time via a tracked `GameObject`). Extracting this into a new static `ConfirmDialog.cs` utility (a single public method, e.g. `ConfirmDialog.Show(Font font, string message, Action<bool> onResult)`) lets `NotesTreeSidebar` reuse it instead of duplicating the dialog-building code; `NotesUndoManager.RequestDeleteObject`/`RequestDeleteLink` switch to calling the extracted utility instead of their own private copies, with no change in their own behavior.

## Search

A single-line `InputField` (placeholder text, e.g. "Поиск...") is added to `NotesTreeSidebar`'s root, between the collapse-toggle `Header` and the scrollable page `List` — visible only while the sidebar is expanded (hidden via the same `SetActive(false)` toggle already applied to `List`/`headerText`/`addGroupButtonGO` when collapsed).

On every `InputField.onValueChanged`, the query is stored and `Rebuild()` runs (the existing tear-down-and-rebuild-from-`Document`method, already called on every document mutation) with the query threaded through:
- A **group** row is built if its title contains the query (case-insensitive substring), **or** at least one of its pages does.
- A **page** row is built if its own name contains the query, **or** its parent group's title does (so pages stay reachable/visible under a group you searched for by name, even if the page's own name doesn't match).
- Empty query shows everything, same as today.

No auto-expand behavior is needed for matches — groups aren't individually collapsible today (only the whole sidebar collapses to a strip), so a matching page's group is either fully shown or the whole sidebar is collapsed.

## Components Touched

- **`NotesTreeSidebar.cs`** — adds the search `InputField`, threads the search query through `Rebuild()`/`BuildGroupRow()`/`BuildPageRow()`; each row gains a "×" delete button and a double-click-to-rename `Text`/`InputField` pair.
- **`ConfirmDialog.cs`** (new) — static utility extracted from `NotesUndoManager`, holding the message-box UI-building code.
- **`NotesUndoManager.cs`** — `RequestDeleteObject`/`RequestDeleteLink` call the extracted `ConfirmDialog.Show(...)` instead of the removed private `ShowConfirmDialog`/`AddDialogButton` methods; behavior unchanged.

## Edge Cases

- Deleting the active page/group: `DeleteGroup`/`DeletePage` already clear `ActivePage` and fire `OnActivePageChanged(null)` — pre-existing behavior, unaffected by this UI work.
- Deleting the last remaining group/page: sidebar shows an empty list, same as today's zero-groups state.
- Renaming to an empty/whitespace string: rejected, same as cancelling.
- Search matches nothing: list shows no rows (header/search box/"+ Группа" button remain visible so the user can clear the query or add new content).
- Double-clicking a page row still needs to not also fire the row's existing single-click "open page" `Button.onClick` twice in a confusing way — the rename click-catcher only intercepts `clickCount == 2`; the existing `Button.onClick` (single click semantics) is unaffected, though a double-click will still cause "open page" to fire once (from the first click) before the second click promotes it to rename mode. This is acceptable — opening the page you're about to rename isn't harmful.

## Out of Scope

- Undo (Ctrl+Z) for group/page deletion.
- Drag-and-drop reordering of groups/pages.
- Renaming/deleting via right-click context menu (double-click + persistent × button covers it).
- User-draggable/resizable panel splits — separate spec, next in sequence after this one.
