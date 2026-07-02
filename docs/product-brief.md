# Product Brief — D&D Campaign Toolkit (Notes Editor focus)

## What this is

A Unity desktop tool for a Dungeon Master (DM) to **prepare and run** tabletop D&D campaigns. It started as a procedural fantasy world-map generator and is growing into a unified, personal session-prep workspace — think "a private, offline Notion + map generator" for one DM, not a multiplayer or cloud product.

Two halves of the screen, side by side:
- **Left two-thirds — the Map.** Procedurally generated world map (biomes, climate, coastlines, regions) with manual post-generation editing (paint climate/terrain, override individual cells) and a Points-of-Interest layer (cities, ruins, dungeons, fortresses) that the DM places and names.
- **Right third — the Notes Editor.** A whiteboard-style canvas for prep notes: index cards, pasted images, freehand sketches, and arrows connecting them, organized into pages and page-groups. This is the part that needs a real UX/UI design pass.

## Who uses it

One user: the DM, working alone before and during a session. No accounts, no collaboration, no sync — everything is local. Optimize for *personal, fast, low-friction note-taking while also glancing at the map*, not for polished presentation to other people.

## The Notes Editor — what it does today

**Layout:** Fixed 2:1 split with the map. Inside the notes third: a collapsible tree sidebar (list of page-groups, each expandable to its pages) sits above/beside a toolbar, which sits above the canvas viewport.

**Canvas objects** (freely draggable, positioned anywhere on an unbounded 2D plane, pan/zoom camera):
- **Note card** — title + free-text body, edited inline.
- **Image** — pasted from clipboard or loaded from a local file (static image; animated GIFs show only their first frame).
- **Drawing** — a fixed-size rectangle you freehand-paint into with a brush (not resizable after creation, not infinite canvas — a deliberate simplification).
- **Link** — a directional arrow drawn between the centers of any two objects.

**Tools** (one active at a time, chosen from a toolbar): Select (drag objects to move them, drag empty canvas to pan), Note (click empty canvas → new card), Link (click one object then another → arrow), Drawing (click empty canvas → new paintable square; click/drag on an existing one → paint into it), Image (click → opens a native file-picker dialog).

**Organization:** Notes live in **pages**; pages are grouped into **page-groups** (a group has a title and, optionally, is linked to one specific map POI). Clicking a POI on the map has an "Open Pages" button that jumps straight to its linked group, creating one on first use.

**Editing safety:** Deleting anything shows a confirm dialog first; there's a linear undo stack for create/move/delete.

**Not yet built:** save/export/import of the whole document (map + notes) to disk — planned as a separate follow-up once the notes data model stabilizes. Also not built: animated GIF playback, rich text formatting, multi-select, resizable drawings.

## Established visual language (please keep consistent)

Every existing panel (map layer toggles, cell-override editor, POI editor, legend) already shares one look, and the notes editor should match it rather than invent a new one:

| Role | Color |
|---|---|
| Panel background | black, ~70% opacity |
| Body text | white |
| Section headers | pale blue `#B3D9FF`-ish |
| Active/selected state | muted green `#338C4D`-ish |
| Inactive state | neutral gray `#4D4D4D`-ish |
| Destructive action (delete) | muted red `#8C2626`-ish |

Icons are simple flat-color shapes with a thin dark outline, generated at runtime as tiny pixel-grid glyphs (there's no external art pipeline) — e.g. POI markers are a solid-color circle with a bold single-letter glyph. Any new iconography for the notes editor should be describable as similarly simple shapes.

## Current UX/UI problems worth designing around

This is the honest state as of the first working build — a functional prototype, not a designed interface:

1. **Toolbar was unreadably cramped** (just fixed structurally, but the fix only restores correct sizing — the actual button design, iconography, and hover/active affordances still need real design thought).
2. **Density/readability throughout is rough**: small fonts, tight rows, minimal padding, no clear visual hierarchy between sections.
3. **Note cards need a distinct "card on a corkboard" identity** rather than reading as a generic form field.
4. **Interaction feedback is minimal** — no clear indication of which object is selected, no visible affordance for "you're about to draw a link, click a target," no hover states, no drag-in-progress feedback.
5. **The sidebar tree** (groups → pages) is the primary navigation and currently the least thought-out part visually.
6. **The map/notes split is fixed** at 2:1 — not resizable, no responsive behavior beyond simple proportional rescaling on window resize.

## Constraints for any redesign

- UI is built entirely in C# code with Unity's legacy `UnityEngine.UI` system (no visual editor authoring, no TextMeshPro — plain `Text`/`Font`/`Image`/`InputField` components only). Designs should be things that are feasible to construct this way — arbitrary custom fonts, rich typography, or complex custom shaders are out of reach without added tooling.
- No external image assets currently — all icons/graphics are procedurally drawn pixel data at runtime. A redesign *could* introduce real image assets if that's judged worth the tooling change, but that's a decision point, not an assumption.
- Screen is desktop-only, fixed 2:1 split, single resolution class (a normal desktop window) — no mobile/touch considerations.
