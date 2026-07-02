# Notes Editor — Design

**Date:** 2026-07-02
**Status:** Approved, ready for planning
**Branch:** implement off `main`

---

## Goal

Add a notes/canvas editor alongside the map — a lightweight whiteboard for session prep: cards with text, image/GIF pasting, freehand drawings (as a fixed-size object, not a free page-wide layer), and directional links between cards. Notes are organized into pages, pages into groups, and groups can optionally be tied to a specific POI on the map (so clicking a city on the map can jump straight to its notes).

This spec covers the **editor only**. Save/load of the resulting data (together with the map) is a separate, later spec — this system just needs a clean, serializable data model so that spec can consume it.

---

## Screen Layout

The app window splits 2:1 — left two-thirds: existing map generator/editor UI, right third: notes editor. Split uses `RectTransform` anchors (not fixed pixel widths), so both halves resize proportionally and never overlap when the window is resized.

Inside the notes third:
- **Collapsible tree sidebar** (toggled via a ☰ button) — lists groups (accordion), each expands to its pages. Collapsing it gives the canvas the full third-width when not needed.
- **Toolbar** — along the top of the canvas area: Select, Note, Link, Drawing, Image tools.
- **Canvas** — the currently open page, pan + zoom (2D UI camera, analogous to the map's camera but via `RectTransform` scale/offset instead of a 3D camera).

---

## Data Model

**`Assets/WorldGen/Notes/Data/NotesData.cs`** — pure C#, no UnityEngine dependency (parallel to `WorldGen/Generation/`).

```csharp
namespace WorldGen.Notes.Data
{
    public class NotesDocument
    {
        public List<PageGroup> Groups = new();
    }

    public class PageGroup
    {
        public string Id;
        public string Title;
        public string LinkedPoiId;         // null = not tied to a POI
        public List<NotesPage> Pages = new();
    }

    public class NotesPage
    {
        public string Id;
        public string Name;
        public List<CanvasObjectData> Objects = new();
        public List<LinkData> Links = new();
        public Vector2 CameraPan;
        public float CameraZoom = 1f;
    }

    public abstract class CanvasObjectData
    {
        public string Id;
        public Vector2 Position;
        public Vector2 Size;
    }

    public class NoteCardData : CanvasObjectData
    {
        public string Title = "";
        public string Body = "";
    }

    public class ImageObjectData : CanvasObjectData
    {
        public byte[] ImageBytes;          // PNG/JPG/GIF file bytes, embedded directly
    }

    public class DrawingObjectData : CanvasObjectData
    {
        public byte[] PixelDataPng;        // encoded raster content
        public int PixelWidth;
        public int PixelHeight;
    }

    public class LinkData
    {
        public string Id;
        public string FromObjectId;
        public string ToObjectId;
        public bool Directed = true;       // arrow vs plain line
    }
}
```

Notes:
- `Vector2` here is `System.Numerics.Vector2`, matching the existing Generation-layer convention.
- Image bytes are embedded directly in the data model (not file paths) — this keeps `NotesDocument` self-contained, which the future export/import spec needs.
- `DrawingObjectData` has a **fixed size set at creation time**; freehand drawing only happens inside that fixed raster, so there's no infinite-canvas-vs-raster conflict. The canvas itself (positions of objects) is unbounded; only drawing content is bounded per-object.
- GIFs are stored as their raw file bytes but rendered as a static first frame in v1 (no animated playback — would require a GIF decoder Unity doesn't ship with).

---

## Architecture

### New files — Data layer (`Assets/WorldGen/Notes/Data/`)

**`NotesData.cs`** — model above.

### New files — Rendering layer (`Assets/WorldGen/Notes/Rendering/`)

**`NotesLayoutController.cs`**
Root split-screen controller. Anchors the map area to the left two-thirds and the notes area to the right third via `RectTransform` anchor min/max (not pixel offsets), so a window resize rescales both proportionally with no overlap.

**`NotesDocumentController.cs`**
Owns the in-memory `NotesDocument`. CRUD for groups/pages (create/rename/delete), tracks which page is currently open. Fires events (`OnDocumentChanged`, `OnActivePageChanged`) consumed by the sidebar and canvas.

**`NotesTreeSidebar.cs`**
Collapsible accordion UI: groups expand/collapse to show their pages. Buttons: "+ Group", "+ Page" (inside a group), rename/delete via context menu (with confirm dialog for delete, per `NotesUndoManager` below). Selecting a page calls `NotesDocumentController.OpenPage(id)`.

**`NotesCanvasController.cs`**
Renders the currently open `NotesPage`: spawns object views into a pannable/zoomable container `RectTransform` (drag with a held mouse button while Select tool is active pans; scroll wheel zooms — scale the container transform, matching `CameraPan`/`CameraZoom` in the page data). Spawns/destroys `NoteCardView` / `ImageObjectView` / `DrawingObjectView` / `LinkView` to match `NotesPage.Objects` / `.Links`.

**`NotesToolbar.cs`**
Tool selection: Select, Note, Link, Drawing, Image. Active tool changes `CanvasInteractionController` behavior.

**`CanvasInteractionController.cs`**
Routes mouse input based on active tool:
- **Select**: click to select/drag-move an object; click empty canvas to deselect; drag-pan canvas.
- **Note**: click empty canvas → creates a `NoteCardData` there, switches to Select and opens it for editing.
- **Link**: drag from one object's edge to another → creates a `LinkData` (directed by default).
- **Drawing**: click empty canvas → creates a `DrawingObjectData` at fixed default size; while a `DrawingObjectView` is selected and Drawing tool active, mouse drag paints into its raster (`SetPixels32`, same runtime-texture pattern as `PoiPlaceholderFactory`).
- **Image**: click empty canvas → opens a file picker (local disk) for png/jpg/gif; also globally, Ctrl+V pastes clipboard image content into a new `ImageObjectData` at the last mouse position, regardless of active tool.

**`NoteCardView.cs`**
Draggable/resizable UI card: `Image` background, `Text` title, `InputField` body (multiline). Matches existing `MapEditorPanel`/`PoiEditPanel` UI-construction style (`UnityEngine.UI`, built-in font, no TextMeshPro).

**`ImageObjectView.cs`**
Draggable/resizable `RawImage` displaying the decoded texture (first frame only for GIFs, via `Texture2D.LoadImage`).

**`DrawingObjectView.cs`**
Draggable/resizable `RawImage` backed by a `Texture2D` sized to `PixelWidth`×`PixelHeight`. Paint stroke logic lives in `CanvasInteractionController`, mirroring the circle-stamp approach in `PoiPlaceholderFactory.Build`.

**`LinkView.cs`**
UI `Image` stretched and rotated between the centers (or nearest edge points) of its two linked objects; an arrowhead sub-image at the target end when `Directed`. Recomputes transform whenever either endpoint moves (subscribes to both views' drag events).

**`NotesUndoManager.cs`**
Command-pattern undo stack (Create / Delete / Move commands for objects, links, pages, groups). Delete actions show a confirm dialog before pushing the command; Ctrl+Z pops and reverses the last command. Parallel to `BrushUndoManager` but command objects instead of snapshots, since notes objects are heterogeneous (unlike uniform cell snapshots).

### Modified files

**`PoiEditPanel.cs`**
Add an "Open Pages" button. Behavior:
- If no `PageGroup` in the document has `LinkedPoiId == poi.Id`: create one (`Title = poi.Name`, one initial empty page), then open it.
- If one already exists: expand it in `NotesTreeSidebar` and open its first page.
- Renaming a POI does **not** rename its linked group afterward — they're independent once created (the DM may have customized the group title).
- `NotesDocument` never holds a direct reference to `PoiManager`/`WorldMapRenderer` — only the `LinkedPoiId` string, resolved at lookup time via `PoiManager.GetAllPois()`. If the POI is later deleted, the group and its pages remain (just no more map-side entry point to them); this is deliberate — deleting a POI shouldn't destroy prep notes.

---

## Interaction Summary

| Action | Result |
|---|---|
| Click empty canvas (Note tool) | New note card, opens for editing |
| Click empty canvas (Image tool) | File picker → new image object |
| Ctrl+V anywhere on canvas | Clipboard image → new image object at cursor |
| Click empty canvas (Drawing tool) | New fixed-size drawing object |
| Drag inside a drawing object (Drawing tool, object selected) | Paints into its raster |
| Drag from card edge to another card (Link tool) | New directed link |
| Drag object (Select tool) | Moves it; updates any attached links live |
| Delete key / delete button | Confirm dialog → removes object/link/page/group, pushes undo command |
| Ctrl+Z | Reverts last undo-tracked command |
| Scroll wheel over canvas | Zoom |
| Drag empty canvas (Select tool) | Pan |
| Click page in sidebar tree | Switches active page, restores its saved pan/zoom |
| "Open Pages" in PoiEditPanel | Creates or opens the POI's linked group |

---

## Out of Scope (deferred)

- **Save/export/import** of `NotesDocument` (and the map) to disk — separate spec, to follow once this data model is implemented.
- Animated GIF playback (static first frame only for v1).
- Rich text formatting in note card bodies (plain multiline text only).
- Multi-select / bulk operations on canvas objects.
- Resizing drawing objects after creation reflowing their raster content (resizing just scales the existing texture, doesn't re-render strokes).
