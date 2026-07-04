# Draggable Panel Splits (Map/Notes + Sidebar) — Design

**Date:** 2026-07-04
**Status:** Approved, ready for planning
**Branch:** main

---

## Goal

Two panel boundaries are currently fixed at compile time:

- The map/notes screen split (`NotesLayoutController.SplitFraction`, `2f/3f`) — a `const`, deliberately, per the prior [map-notes-split-single-source-design](2026-07-03-map-notes-split-single-source-design.md) spec, specifically to sidestep a script-execution-order hazard: `MapLegendUI` and `PoiEditPanel` each apply this fraction to their own panel anchors inside their own `Awake()`, and Unity does not guarantee `NotesLayoutController`'s `Awake()` runs first.
- The notes editor's sidebar width (`NotesTreeSidebar.ExpandedWidth`, `200f`) — also a `const`, explicitly marked out of scope in the [notes-editor-layout-redesign](2026-07-04-notes-editor-layout-redesign-design.md) spec ("no user-draggable/resizable sidebar width").

This adds a draggable divider to both, letting the user resize each segment (map, notes editor, sidebar) by hand, with sizes persisted across sessions via `PlayerPrefs` (this project has no existing settings-persistence mechanism — this is the first).

## Shared `DraggableDivider` component

A single reusable component handles both dividers' drag *gesture* — it has zero awareness of fractions, pixel widths, or what it's resizing:

```
DraggableDivider : MonoBehaviour, IDragHandler, IPointerEnterHandler, IPointerExitHandler
    public Action<float> OnDragDeltaX;   // raw screen-space delta.x from PointerEventData, per OnDrag call
```

- On hover, its `Image` background fades in (transparent → subtle highlight); on exit, fades back out — matching the toolbar's established hover-reveals-affordance style from the earlier redesign.
- Each caller (`NotesLayoutController` for the map/notes split, `NotesTreeSidebar` for the sidebar) subscribes to `OnDragDeltaX` and interprets the raw pixel delta its own way:
  - Map/notes split: `SetSplitFraction(SplitFraction + dx / Screen.width)` — this project's root Canvas uses `CanvasScaler`'s default `ConstantPixelSize` mode (never switched to "Scale With Screen Size"), so canvas units equal screen pixels 1:1, making `Screen.width` a safe, direct denominator.
  - Sidebar: `SetExpandedWidth(ExpandedWidth + dx)` — already pixel-denominated, no conversion needed.
- Also gets a **double-click to reset to default**, via the existing double-click detector — renamed `DoubleClickToRename` → `DoubleClickHandler` (it was always a generic `IPointerClickHandler`/`clickCount == 2` wrapper; the name was just never generalized before it got a second use).

## Map/notes split becomes runtime-mutable

`NotesLayoutController.SplitFraction` changes from `const float` to:

```
public static float SplitFraction { get; private set; }   // lazily initialized from PlayerPrefs (default 2f/3f) on first access
public static event Action<float> OnSplitFractionChanged;
public static void SetSplitFraction(float value)           // clamps to [0.3, 0.85], saves to PlayerPrefs, fires the event
```

Reading a `static` field/property still resolves before any `Awake()` touches it regardless of script execution order (the CLR initializes it on first access to the type, the same ordering guarantee the `const` had) — so this preserves the exact property the prior spec relied on, while also allowing later mutation. `MapLegendUI`/`PoiEditPanel` each read the current value once at their own `Awake()` (unchanged) **and** additionally subscribe to `OnSplitFractionChanged` to update their anchor's X coordinate live while the user drags. `NotesLayoutController` itself subscribes to its own event (`Apply()` re-runs on change), and owns the divider — added as a child of `notesAreaRoot`, straddling its own left edge (which already sits exactly on the split boundary), so no new references need threading through `NotesRootBuilder`.

## Sidebar width becomes runtime-mutable

`NotesTreeSidebar.ExpandedWidth` changes from `public const float` to an instance-level, PlayerPrefs-backed value with a setter that clamps to `[120, 400]`, updates `rootLayoutElement.preferredWidth` (only takes visible effect while expanded — collapsed stays a fixed 28px strip, unaffected), and saves to PlayerPrefs. The divider is added as a child of the sidebar's own root, anchored to its right edge (same "straddle the boundary" placement as the map/notes divider).

## Persistence

Two new `PlayerPrefs` float keys (first use of `PlayerPrefs` in this project): one for the split fraction, one for the sidebar's expanded width. Both are read once (lazily, on first access) and written on every committed change (drag-end or double-click reset) — not on every intermediate drag frame, to avoid hammering `PlayerPrefs.Save()`-adjacent I/O during a drag gesture.

## Edge Cases

- Dragging past the clamp range: the divider simply stops moving further in that direction (value clamps, no error, no visual glitch — the drag `delta` keeps accumulating against an unmoving value until the user drags back the other way).
- Collapsed sidebar: its divider is hidden/inactive while collapsed (same `SetActive` toggle already applied to the list/search/add-group button), since there's nothing meaningful to resize when the column is a fixed 28px strip.
- First-ever run (no saved `PlayerPrefs` value yet): falls back to today's defaults (`2f/3f` split, `200f` sidebar width) — identical starting appearance to before this feature existed.
- Double-click resets to the *default*, not the last-saved value — consistent with "reset" meaning "back to how it started," not "undo my last drag."

## Out of Scope

- Custom OS cursor changes on hover (e.g. a ↔ resize cursor) — the color-highlight feedback is the only hover affordance for now.
- Any other panel becoming resizable beyond these two dividers.
- Any settings/preferences UI panel — `PlayerPrefs` values are only ever written by dragging/double-clicking the dividers themselves, never exposed as editable settings elsewhere.
