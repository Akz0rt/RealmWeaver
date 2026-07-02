# Notes Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a notes/canvas editor docked to the right third of the screen, next to the existing map (left two-thirds): cards with text, pasted/loaded images, fixed-size freehand drawings, directional links between objects, organized into pages and groups, with groups optionally linked to a map POI.

**Architecture:** `WorldGen.Notes.Data` is a pure-C# data model (parallel to `WorldGen.Generation`) — `NotesDocument` → `PageGroup` → `NotesPage` → `CanvasObjectData` subtypes + `LinkData`. `WorldGen.Notes.Rendering` (parallel to `WorldGen.Rendering`) is Unity/UnityEngine.UI: a `NotesDocumentController` owns the in-memory document and fires change events; `NotesCanvasController` renders the open page's objects into a pannable/zoomable `RectTransform`; per-object-type views (`NoteCardView`, `ImageObjectView`, `DrawingObjectView`, `LinkView`) are draggable UI prefabs built in code; `CanvasInteractionController` routes mouse input by active tool; `NotesUndoManager` is a command-stack for create/delete/move. `NotesLayoutController` anchors the map to the left two-thirds and notes to the right third via RectTransform anchors so window resizes never overlap the two halves.

**Tech Stack:** Unity 2022.3 LTS, Built-in RP, New Input System (`Mouse.current`/`Keyboard.current`), legacy `UnityEngine.UI` (no TextMeshPro), C# `System.Numerics.Vector2` in the data layer.

## Global Constraints

- **New Input System only** — `Mouse.current`, `Keyboard.current` from `UnityEngine.InputSystem`. Never `UnityEngine.Input`.
- **`Assets/WorldGen/Notes/Data/` is UnityEngine-free** — pure C#, use `System.Numerics.Vector2`, no `using UnityEngine`.
- **`Assets/WorldGen/Notes/Rendering/` may use UnityEngine freely.**
- **No TextMeshPro** — `Text`/`InputField` from `UnityEngine.UI` only, built-in font (`Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`), matching `MapEditorPanel`/`PoiEditPanel` style.
- **No placeholders in code** — every method has a real implementation.
- **`[ContextMenu]` self-tests** for logic verifiable without manual interaction, matching project convention: `Debug.Log("Self-Test X: PASS")` or `FAIL` with a reason.
- **UI construction is code-only** (no prefabs authored in the Editor), matching `MapEditorPanel`/`PoiEditPanel`/`PoiPlaceholderFactory` conventions — `new GameObject(...)`, `AddComponent<...>()`, manual `RectTransform` anchors.
- **GIFs render as static first frame only** (no animated playback in v1).
- **`DrawingObjectData` has a fixed raster size set at creation time** — no infinite canvas for drawing content; only object *positions* on the page are unbounded.
- **Delete requires a confirm dialog and pushes an undo command** — no silent deletes.
- **`NotesDocument` never references `PoiManager`/`WorldMapRenderer` directly** — only `LinkedPoiId: string`, resolved at lookup time.

---

### Task 1: Notes data model

**Files:**
- Create: `Assets/WorldGen/Notes/Data/NotesData.cs`

**Interfaces:**
- Produces: `NotesDocument`, `PageGroup`, `NotesPage`, `CanvasObjectData` (abstract) + `NoteCardData`/`ImageObjectData`/`DrawingObjectData`, `LinkData`. Used by every later task.

- [ ] **Step 1: Create NotesData.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Notes.Data
{
    public class NotesDocument
    {
        public List<PageGroup> Groups = new List<PageGroup>();
    }

    public class PageGroup
    {
        public string Id = Guid.NewGuid().ToString();
        public string Title = "Новая группа";
        public string LinkedPoiId;    // null = not tied to a POI
        public List<NotesPage> Pages = new List<NotesPage>();
    }

    public class NotesPage
    {
        public string Id = Guid.NewGuid().ToString();
        public string Name = "Новая страница";
        public List<CanvasObjectData> Objects = new List<CanvasObjectData>();
        public List<LinkData> Links = new List<LinkData>();
        public Vector2 CameraPan;
        public float CameraZoom = 1f;
    }

    public abstract class CanvasObjectData
    {
        public string Id = Guid.NewGuid().ToString();
        public Vector2 Position;
        public Vector2 Size;
    }

    public class NoteCardData : CanvasObjectData
    {
        public string Title = "";
        public string Body = "";

        public NoteCardData()
        {
            Size = new Vector2(220f, 140f);
        }
    }

    public class ImageObjectData : CanvasObjectData
    {
        public byte[] ImageBytes;   // raw file bytes (png/jpg/gif), embedded directly
    }

    public class DrawingObjectData : CanvasObjectData
    {
        public byte[] PixelDataPng;  // PNG-encoded raster content, null until first stroke
        public int PixelWidth;
        public int PixelHeight;

        public DrawingObjectData(int pixelWidth, int pixelHeight)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Size = new Vector2(pixelWidth, pixelHeight);
        }
    }

    public class LinkData
    {
        public string Id = Guid.NewGuid().ToString();
        public string FromObjectId;
        public string ToObjectId;
        public bool Directed = true;
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Check Console for errors. Expected: no errors related to NotesData.cs.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Data/NotesData.cs
git commit -m "feat: notes data model (document/group/page/objects/links)"
```

---

### Task 2: NotesDocumentController — document ownership + CRUD

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesDocumentController.cs`

**Interfaces:**
- Consumes: `NotesDocument`, `PageGroup`, `NotesPage` (Task 1).
- Produces:
  - `NotesDocumentController.Document → NotesDocument` (read-only property, starts as an empty document with one default group/page).
  - `NotesDocumentController.ActivePage → NotesPage` (currently open page, or null).
  - `NotesDocumentController.CreateGroup(string title, string linkedPoiId = null) → PageGroup`.
  - `NotesDocumentController.CreatePage(string groupId, string name) → NotesPage`.
  - `NotesDocumentController.RenameGroup(string groupId, string title)`.
  - `NotesDocumentController.RenamePage(string pageId, string name)`.
  - `NotesDocumentController.DeleteGroup(string groupId)`.
  - `NotesDocumentController.DeletePage(string pageId)`.
  - `NotesDocumentController.OpenPage(string pageId)`.
  - `NotesDocumentController.FindGroupByPoiId(string poiId) → PageGroup` (null if none).
  - `event Action OnDocumentChanged` — fires after any structural change (create/rename/delete group or page).
  - `event Action<NotesPage> OnActivePageChanged` — fires with the newly opened page (or null) whenever `OpenPage` changes the active page.
  - Used by Tasks 3, 4, 5, 11.

- [ ] **Step 1: Write the self-test spec (before implementation)**

Self-test spec (added as `[ContextMenu]` in Step 3):
- "Self-Test: Notes Document CRUD" — creates a group, creates two pages in it, opens the second page, verifies `ActivePage.Id` matches, deletes one page, verifies group has 1 page left, deletes the group, verifies `Document.Groups` is empty.

- [ ] **Step 2: Create NotesDocumentController.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Owns the in-memory NotesDocument: group/page CRUD, active-page tracking.
    /// Attach to any GameObject in the notes UI hierarchy.
    /// </summary>
    public class NotesDocumentController : MonoBehaviour
    {
        public NotesDocument Document { get; private set; } = new NotesDocument();
        public NotesPage ActivePage { get; private set; }

        public event Action OnDocumentChanged;
        public event Action<NotesPage> OnActivePageChanged;

        void Awake()
        {
            var group = CreateGroup("Заметки");
            CreatePage(group.Id, "Страница 1");
            OpenPage(group.Pages[0].Id);
        }

        // ── Group CRUD ─────────────────────────────────────────────────────────

        public PageGroup CreateGroup(string title, string linkedPoiId = null)
        {
            var group = new PageGroup { Title = title, LinkedPoiId = linkedPoiId };
            Document.Groups.Add(group);
            OnDocumentChanged?.Invoke();
            return group;
        }

        public void RenameGroup(string groupId, string title)
        {
            var group = FindGroup(groupId);
            if (group == null) return;
            group.Title = title;
            OnDocumentChanged?.Invoke();
        }

        public void DeleteGroup(string groupId)
        {
            var group = FindGroup(groupId);
            if (group == null) return;
            bool activeWasInGroup = ActivePage != null && group.Pages.Any(p => p.Id == ActivePage.Id);
            Document.Groups.Remove(group);
            if (activeWasInGroup)
            {
                ActivePage = null;
                OnActivePageChanged?.Invoke(null);
            }
            OnDocumentChanged?.Invoke();
        }

        // ── Page CRUD ──────────────────────────────────────────────────────────

        public NotesPage CreatePage(string groupId, string name)
        {
            var group = FindGroup(groupId);
            if (group == null) return null;
            var page = new NotesPage { Name = name };
            group.Pages.Add(page);
            OnDocumentChanged?.Invoke();
            return page;
        }

        public void RenamePage(string pageId, string name)
        {
            var page = FindPage(pageId);
            if (page == null) return;
            page.Name = name;
            OnDocumentChanged?.Invoke();
        }

        public void DeletePage(string pageId)
        {
            var group = Document.Groups.FirstOrDefault(g => g.Pages.Any(p => p.Id == pageId));
            if (group == null) return;
            group.Pages.RemoveAll(p => p.Id == pageId);
            if (ActivePage != null && ActivePage.Id == pageId)
            {
                ActivePage = null;
                OnActivePageChanged?.Invoke(null);
            }
            OnDocumentChanged?.Invoke();
        }

        public void OpenPage(string pageId)
        {
            var page = FindPage(pageId);
            if (page == null || page == ActivePage) return;
            ActivePage = page;
            OnActivePageChanged?.Invoke(page);
        }

        public PageGroup FindGroupByPoiId(string poiId) =>
            Document.Groups.FirstOrDefault(g => g.LinkedPoiId == poiId);

        // ── Internals ──────────────────────────────────────────────────────────

        PageGroup FindGroup(string groupId) => Document.Groups.FirstOrDefault(g => g.Id == groupId);

        NotesPage FindPage(string pageId) =>
            Document.Groups.SelectMany(g => g.Pages).FirstOrDefault(p => p.Id == pageId);

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Document CRUD")]
        public void SelfTestDocumentCrud()
        {
            var doc = new NotesDocument();
            // Exercise the same logic paths as the instance methods, against a scratch document,
            // so the test doesn't disturb whatever document is currently loaded in the scene.
            var group = new PageGroup { Title = "Test Group" };
            doc.Groups.Add(group);

            var pageA = new NotesPage { Name = "A" };
            var pageB = new NotesPage { Name = "B" };
            group.Pages.Add(pageA);
            group.Pages.Add(pageB);

            bool twoPages = group.Pages.Count == 2;

            group.Pages.RemoveAll(p => p.Id == pageA.Id);
            bool onePageLeft = group.Pages.Count == 1 && group.Pages[0].Id == pageB.Id;

            doc.Groups.Remove(group);
            bool noGroupsLeft = doc.Groups.Count == 0;

            bool ok = twoPages && onePageLeft && noGroupsLeft;
            Debug.Log(ok
                ? "Self-Test Notes Document CRUD: PASS"
                : $"Self-Test Notes Document CRUD: FAIL (twoPages={twoPages}, onePageLeft={onePageLeft}, noGroupsLeft={noGroupsLeft})");
        }
    }
}
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no errors. `Self-Test: Notes Document CRUD` appears in the component's context menu.

- [ ] **Step 4: Run self-test**

Add `NotesDocumentController` to a scratch GameObject in the scene. Right-click component → **Self-Test: Notes Document CRUD** → Console: `PASS`.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesDocumentController.cs
git commit -m "feat: NotesDocumentController — group/page CRUD + active page tracking"
```

---

### Task 3: NotesLayoutController — 2:1 split screen

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs`

**Interfaces:**
- Consumes: nothing (pure layout, operates on RectTransforms assigned in Inspector).
- Produces: `NotesLayoutController.mapAreaRoot`/`notesAreaRoot` (public fields, assigned in Inspector to the root RectTransforms of the map UI and notes UI). Used by scene wiring in Task 11.

- [ ] **Step 1: Create NotesLayoutController.cs**

```csharp
using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Splits the screen 2:1 between the map area (left two-thirds) and the notes area
    /// (right third) using RectTransform anchors, so both regions rescale proportionally
    /// on window resize and never overlap.
    /// </summary>
    public class NotesLayoutController : MonoBehaviour
    {
        [Tooltip("Root RectTransform containing the map/world UI. Anchored to the left two-thirds.")]
        public RectTransform mapAreaRoot;
        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;

        [Range(0.1f, 0.9f)]
        [Tooltip("Fraction of screen width given to the map area; the rest goes to notes.")]
        public float splitFraction = 2f / 3f;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (mapAreaRoot != null)
            {
                mapAreaRoot.anchorMin = new Vector2(0f, 0f);
                mapAreaRoot.anchorMax = new Vector2(splitFraction, 1f);
                mapAreaRoot.offsetMin = Vector2.zero;
                mapAreaRoot.offsetMax = Vector2.zero;
            }

            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(splitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors. `Apply Split` appears in the context menu.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesLayoutController.cs
git commit -m "feat: NotesLayoutController — anchor-based 2:1 map/notes split"
```

---

### Task 4: NoteCardView — draggable text card

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NoteCardView.cs`

**Interfaces:**
- Consumes: `NoteCardData` (Task 1).
- Produces:
  - `NoteCardView.Initialize(NoteCardData data, RectTransform canvasContainer)` — builds child UI, parents into `canvasContainer`.
  - `NoteCardView.Refresh()` — re-reads data and updates text/position/size.
  - `NoteCardView.ObjectId → string`.
  - `NoteCardView.Data → CanvasObjectData` (returns the underlying `NoteCardData`).
  - `NoteCardView.RectTransform → RectTransform` (the card's own transform, for `LinkView` anchor lookups).
  - `event Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded` — fires `(ObjectId, oldPosition, newPosition)` when a drag completes.
  - `event Action<string> OnClicked` — fires when the card is clicked (not dragged).
  - Used by Tasks 6, 7, 8.

- [ ] **Step 1: Create NoteCardView.cs**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable card showing a NoteCardData's title + body. Drag moves it within its
    /// parent canvas container; a plain click (no movement) fires OnClicked instead.
    /// </summary>
    public class NoteCardView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        NoteCardData data;
        RectTransform rect;
        Text titleText;
        InputField bodyField;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        static Font builtinFont;

        public void Initialize(NoteCardData cardData, RectTransform canvasContainer)
        {
            data = cardData;
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.9f, 0.6f, 0.95f);

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(transform, false);
            titleText = titleGO.AddComponent<Text>();
            titleText.font = builtinFont;
            titleText.fontSize = 14;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.black;
            titleText.alignment = TextAnchor.UpperLeft;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -4f);
            titleRect.sizeDelta = new Vector2(-8f, 22f);

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(transform, false);
            var bodyBg = bodyGO.AddComponent<Image>();
            bodyBg.color = new Color(1f, 1f, 1f, 0.01f);
            bodyField = bodyGO.AddComponent<InputField>();
            bodyField.targetGraphic = bodyBg;
            bodyField.lineType = InputField.LineType.MultiLineNewline;
            var bodyRect = bodyGO.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(4f, 4f);
            bodyRect.offsetMax = new Vector2(-4f, -26f);

            var bodyTextGO = new GameObject("Text");
            bodyTextGO.transform.SetParent(bodyGO.transform, false);
            var bodyText = bodyTextGO.AddComponent<Text>();
            bodyText.font = builtinFont;
            bodyText.fontSize = 12;
            bodyText.color = Color.black;
            bodyText.supportRichText = false;
            var bodyTextRect = bodyTextGO.GetComponent<RectTransform>();
            bodyTextRect.anchorMin = Vector2.zero;
            bodyTextRect.anchorMax = Vector2.one;
            bodyTextRect.sizeDelta = Vector2.zero;
            bodyField.textComponent = bodyText;
            bodyField.onEndEdit.AddListener(v => data.Body = v);

            titleText.text = data.Title;
            bodyField.text = data.Body;

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            titleText.text = data.Title;
            if (bodyField != null) bodyField.text = data.Body;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NoteCardView.cs
git commit -m "feat: NoteCardView — draggable text card"
```

---

### Task 5: ImageObjectView — draggable image

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/ImageObjectView.cs`

**Interfaces:**
- Consumes: `ImageObjectData` (Task 1).
- Produces:
  - `ImageObjectView.Initialize(ImageObjectData data, RectTransform canvasContainer)`.
  - `ImageObjectView.Refresh()`.
  - `ImageObjectView.ObjectId → string`.
  - `ImageObjectView.Data → CanvasObjectData`.
  - `ImageObjectView.RectTransform → RectTransform`.
  - `event Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded` — fires `(ObjectId, oldPosition, newPosition)`.
  - `event Action<string> OnClicked`.
  - Used by Tasks 7, 8.

- [ ] **Step 1: Create ImageObjectView.cs**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable image object. Decodes ImageObjectData.ImageBytes into a texture on
    /// Initialize (first frame only for animated GIFs — Texture2D.LoadImage doesn't animate).
    /// </summary>
    public class ImageObjectView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        ImageObjectData data;
        RectTransform rect;
        RawImage rawImage;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        public void Initialize(ImageObjectData imageData, RectTransform canvasContainer)
        {
            data = imageData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rawImage = gameObject.AddComponent<RawImage>();

            var tex = new Texture2D(2, 2);
            if (data.ImageBytes != null && data.ImageBytes.Length > 0 && tex.LoadImage(data.ImageBytes))
            {
                rawImage.texture = tex;
                if (data.Size.X <= 0f || data.Size.Y <= 0f)
                    data.Size = new System.Numerics.Vector2(tex.width, tex.height);
            }

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/ImageObjectView.cs
git commit -m "feat: ImageObjectView — draggable image object"
```

---

### Task 6: DrawingObjectView — fixed-size paintable raster

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/DrawingObjectView.cs`

**Interfaces:**
- Consumes: `DrawingObjectData` (Task 1).
- Produces:
  - `DrawingObjectView.Initialize(DrawingObjectData data, RectTransform canvasContainer)`.
  - `DrawingObjectView.Refresh()`.
  - `DrawingObjectView.ObjectId → string`.
  - `DrawingObjectView.Data → CanvasObjectData`.
  - `DrawingObjectView.RectTransform → RectTransform`.
  - `DrawingObjectView.PaintAt(Vector2 localPoint, float brushRadius, Color32 color)` — paints a filled circle into the raster at the given local (RectTransform-space) point, converted to pixel coordinates internally.
  - `DrawingObjectView.CommitToData()` — encodes the current texture to PNG and writes it into `DrawingObjectData.PixelDataPng`.
  - `event Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded` — fires `(ObjectId, oldPosition, newPosition)`.
  - `event Action<string> OnClicked`.
  - Used by Tasks 7, 8.

- [ ] **Step 1: Create DrawingObjectView.cs**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draggable, fixed-resolution paintable raster. CanvasInteractionController calls
    /// PaintAt while the Drawing tool is active and the mouse is held over this object;
    /// CommitToData() persists the current pixels back into DrawingObjectData for saving.
    /// </summary>
    public class DrawingObjectView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        DrawingObjectData data;
        RectTransform rect;
        RawImage rawImage;
        Texture2D texture;
        Vector2 dragStartLocalPos;
        Vector2 pressScreenPos;
        bool dragging;

        public string ObjectId => data?.Id;
        public CanvasObjectData Data => data;
        public RectTransform RectTransform => rect;

        public event System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> OnDragEnded;
        public event System.Action<string> OnClicked;

        public void Initialize(DrawingObjectData drawingData, RectTransform canvasContainer)
        {
            data = drawingData;
            rect = GetComponent<RectTransform>();
            transform.SetParent(canvasContainer, false);
            rect.pivot = new Vector2(0.5f, 0.5f);

            rawImage = gameObject.AddComponent<RawImage>();

            texture = new Texture2D(data.PixelWidth, data.PixelHeight, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            if (data.PixelDataPng != null && data.PixelDataPng.Length > 0)
            {
                texture.LoadImage(data.PixelDataPng);
            }
            else
            {
                var blank = new Color32[data.PixelWidth * data.PixelHeight];
                for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(blank);
                texture.Apply();
            }
            rawImage.texture = texture;

            Refresh();
        }

        public void Refresh()
        {
            if (data == null) return;
            rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);
            rect.sizeDelta = new Vector2(data.Size.X, data.Size.Y);
        }

        /// <summary>Paints a filled circle at a local point (RectTransform space, origin at center) onto the raster.</summary>
        public void PaintAt(Vector2 localPoint, float brushRadius, Color32 color)
        {
            float u = localPoint.x / rect.rect.width + 0.5f;
            float v = localPoint.y / rect.rect.height + 0.5f;
            int cx = Mathf.RoundToInt(u * texture.width);
            int cy = Mathf.RoundToInt(v * texture.height);
            int pixelRadius = Mathf.Max(1, Mathf.RoundToInt(brushRadius));

            for (int y = -pixelRadius; y <= pixelRadius; y++)
            {
                for (int x = -pixelRadius; x <= pixelRadius; x++)
                {
                    if (x * x + y * y > pixelRadius * pixelRadius) continue;
                    int px = cx + x, py = cy + y;
                    if (px < 0 || px >= texture.width || py < 0 || py >= texture.height) continue;
                    texture.SetPixel(px, py, color);
                }
            }
            texture.Apply();
        }

        public void CommitToData()
        {
            if (data == null || texture == null) return;
            data.PixelDataPng = texture.EncodeToPNG();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragStartLocalPos = rect.anchoredPosition;
            pressScreenPos = eventData.position;
            dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            dragging = true;
            rect.anchoredPosition = dragStartLocalPos + eventData.position - pressScreenPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging)
            {
                var oldPos = data.Position;
                data.Position = new System.Numerics.Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y);
                OnDragEnded?.Invoke(data.Id, oldPos, data.Position);
            }
            else
            {
                OnClicked?.Invoke(data.Id);
            }
            dragging = false;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/DrawingObjectView.cs
git commit -m "feat: DrawingObjectView — fixed-size paintable raster object"
```

---

### Task 7: LinkView — directional arrow between two objects

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/LinkView.cs`

**Interfaces:**
- Consumes: `LinkData` (Task 1), any view exposing `RectTransform` (Tasks 4, 5, 6 all expose this identically).
- Produces:
  - `LinkView.Initialize(LinkData data, RectTransform canvasContainer, RectTransform fromRect, RectTransform toRect)`.
  - `LinkView.LinkId → string`.
  - `LinkView.UpdateTransform()` — repositions/rotates/rescales the line + arrowhead to match current endpoint positions. Called by `NotesCanvasController` whenever either endpoint's view fires `OnDragEnded`.
  - Used by Task 8.

- [ ] **Step 1: Create LinkView.cs**

```csharp
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// A line (+ optional arrowhead) between the centers of two canvas object views.
    /// UpdateTransform() must be called whenever either endpoint moves.
    /// </summary>
    public class LinkView : MonoBehaviour
    {
        LinkData data;
        RectTransform fromRect;
        RectTransform toRect;
        RectTransform lineRect;
        RectTransform arrowRect;

        public string LinkId => data?.Id;

        public void Initialize(LinkData linkData, RectTransform canvasContainer, RectTransform from, RectTransform to)
        {
            data = linkData;
            fromRect = from;
            toRect = to;

            transform.SetParent(canvasContainer, false);

            var lineGO = new GameObject("Line");
            lineGO.transform.SetParent(transform, false);
            var lineImg = lineGO.AddComponent<Image>();
            lineImg.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
            lineRect = lineGO.GetComponent<RectTransform>();
            lineRect.pivot = new Vector2(0f, 0.5f);
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(0f, 0f);
            lineRect.sizeDelta = new Vector2(0f, 3f);

            if (data.Directed)
            {
                var arrowGO = new GameObject("Arrow");
                arrowGO.transform.SetParent(transform, false);
                var arrowImg = arrowGO.AddComponent<Image>();
                arrowImg.color = lineImg.color;
                arrowRect = arrowGO.GetComponent<RectTransform>();
                arrowRect.pivot = new Vector2(1f, 0.5f);
                arrowRect.anchorMin = new Vector2(0f, 0f);
                arrowRect.anchorMax = new Vector2(0f, 0f);
                arrowRect.sizeDelta = new Vector2(14f, 14f);
            }

            UpdateTransform();
        }

        public void UpdateTransform()
        {
            if (fromRect == null || toRect == null || lineRect == null) return;

            Vector2 fromPos = fromRect.anchoredPosition;
            Vector2 toPos = toRect.anchoredPosition;
            Vector2 delta = toPos - fromPos;
            float distance = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            lineRect.anchoredPosition = fromPos;
            lineRect.sizeDelta = new Vector2(distance, lineRect.sizeDelta.y);
            lineRect.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (arrowRect != null)
            {
                arrowRect.anchoredPosition = toPos;
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/LinkView.cs
git commit -m "feat: LinkView — directional arrow between two canvas objects"
```

---

### Task 8: NotesCanvasController — page rendering + pan/zoom

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`

**Interfaces:**
- Consumes: `NotesDocumentController` (Task 2), `NoteCardView`/`ImageObjectView`/`DrawingObjectView`/`LinkView` (Tasks 4–7), `NotesPage`/`CanvasObjectData` subtypes/`LinkData` (Task 1).
- Produces:
  - `NotesCanvasController.CanvasContainer → RectTransform` (the pannable/zoomable content root all object views are parented into).
  - `NotesCanvasController.AddNoteCard(System.Numerics.Vector2 position) → NoteCardData`.
  - `NotesCanvasController.AddImage(System.Numerics.Vector2 position, byte[] imageBytes) → ImageObjectData`.
  - `NotesCanvasController.AddDrawing(System.Numerics.Vector2 position, int pixelWidth, int pixelHeight) → DrawingObjectData`.
  - `NotesCanvasController.AddLink(string fromObjectId, string toObjectId) → LinkData`.
  - `NotesCanvasController.RemoveObject(string objectId)`.
  - `NotesCanvasController.RemoveLink(string linkId)`.
  - `NotesCanvasController.GetView(string objectId) → MonoBehaviour` (returns the `NoteCardView`/`ImageObjectView`/`DrawingObjectView` for that id, or null).
  - `NotesCanvasController.Pan(Vector2 screenDelta)` — shifts `CanvasContainer.anchoredPosition`.
  - `NotesCanvasController.Zoom(float scrollDelta, Vector2 screenPivot)` — rescales `CanvasContainer.localScale` around a screen pivot, clamped `[0.25, 3]`.
  - `event Action OnSelectionCleared` (fires when `RemoveObject`/page switch invalidates the current selection — consumed by `CanvasInteractionController`).
  - Used by Tasks 9, 10, 11.

- [ ] **Step 1: Create NotesCanvasController.cs**

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Renders the NotesDocumentController's active page: spawns/destroys object and link
    /// views to match the page data, and owns pan/zoom of the canvas content root.
    /// </summary>
    public class NotesCanvasController : MonoBehaviour
    {
        [Header("Dependencies")]
        public NotesDocumentController documentController;
        [Tooltip("Viewport RectTransform that clips the canvas content (mask/scroll area).")]
        public RectTransform viewport;

        public RectTransform CanvasContainer { get; private set; }

        readonly Dictionary<string, MonoBehaviour> objectViews = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, LinkView> linkViews = new Dictionary<string, LinkView>();

        public event System.Action OnSelectionCleared;

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

        void OnEnable()
        {
            if (documentController != null)
                documentController.OnActivePageChanged += HandleActivePageChanged;
        }

        void OnDisable()
        {
            if (documentController != null)
                documentController.OnActivePageChanged -= HandleActivePageChanged;
        }

        void HandleActivePageChanged(NotesPage page)
        {
            RebuildFromPage(page);
        }

        // ── Rebuild ────────────────────────────────────────────────────────────

        void RebuildFromPage(NotesPage page)
        {
            foreach (var view in objectViews.Values)
                if (view != null) Destroy(view.gameObject);
            objectViews.Clear();
            foreach (var link in linkViews.Values)
                if (link != null) Destroy(link.gameObject);
            linkViews.Clear();
            OnSelectionCleared?.Invoke();

            if (page == null) return;

            CanvasContainer.anchoredPosition = new Vector2(page.CameraPan.X, page.CameraPan.Y);
            CanvasContainer.localScale = new Vector3(page.CameraZoom, page.CameraZoom, 1f);

            foreach (var obj in page.Objects)
                SpawnView(obj);

            foreach (var link in page.Links)
                SpawnLink(link);
        }

        void SpawnView(CanvasObjectData obj)
        {
            switch (obj)
            {
                case NoteCardData card:
                {
                    var go = new GameObject($"Note_{card.Id}");
                    var view = go.AddComponent<NoteCardView>();
                    view.Initialize(card, CanvasContainer);
                    objectViews[card.Id] = view;
                    break;
                }
                case ImageObjectData image:
                {
                    var go = new GameObject($"Image_{image.Id}");
                    var view = go.AddComponent<ImageObjectView>();
                    view.Initialize(image, CanvasContainer);
                    objectViews[image.Id] = view;
                    break;
                }
                case DrawingObjectData drawing:
                {
                    var go = new GameObject($"Drawing_{drawing.Id}");
                    var view = go.AddComponent<DrawingObjectView>();
                    view.Initialize(drawing, CanvasContainer);
                    objectViews[drawing.Id] = view;
                    break;
                }
            }
        }

        void SpawnLink(LinkData link)
        {
            var fromRect = GetRectTransform(link.FromObjectId);
            var toRect = GetRectTransform(link.ToObjectId);
            if (fromRect == null || toRect == null) return;

            var go = new GameObject($"Link_{link.Id}");
            var view = go.AddComponent<LinkView>();
            view.Initialize(link, CanvasContainer, fromRect, toRect);
            linkViews[link.Id] = view;
        }

        RectTransform GetRectTransform(string objectId)
        {
            if (!objectViews.TryGetValue(objectId, out var view) || view == null) return null;
            return view switch
            {
                NoteCardView n => n.RectTransform,
                ImageObjectView i => i.RectTransform,
                DrawingObjectView d => d.RectTransform,
                _ => null
            };
        }

        // ── Mutation ───────────────────────────────────────────────────────────

        public NoteCardData AddNoteCard(System.Numerics.Vector2 position)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new NoteCardData { Position = position, Title = "Заметка" };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public ImageObjectData AddImage(System.Numerics.Vector2 position, byte[] imageBytes)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new ImageObjectData { Position = position, ImageBytes = imageBytes };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public DrawingObjectData AddDrawing(System.Numerics.Vector2 position, int pixelWidth, int pixelHeight)
        {
            var page = documentController.ActivePage;
            if (page == null) return null;
            var data = new DrawingObjectData(pixelWidth, pixelHeight) { Position = position };
            page.Objects.Add(data);
            SpawnView(data);
            return data;
        }

        public LinkData AddLink(string fromObjectId, string toObjectId)
        {
            var page = documentController.ActivePage;
            if (page == null || fromObjectId == toObjectId) return null;
            var data = new LinkData { FromObjectId = fromObjectId, ToObjectId = toObjectId };
            page.Links.Add(data);
            SpawnLink(data);
            return data;
        }

        public void RemoveObject(string objectId)
        {
            var page = documentController.ActivePage;
            if (page == null) return;

            page.Objects.RemoveAll(o => o.Id == objectId);
            var orphanLinks = page.Links.Where(l => l.FromObjectId == objectId || l.ToObjectId == objectId).ToList();
            foreach (var link in orphanLinks)
                RemoveLink(link.Id);

            if (objectViews.TryGetValue(objectId, out var view))
            {
                if (view != null) Destroy(view.gameObject);
                objectViews.Remove(objectId);
            }
            OnSelectionCleared?.Invoke();
        }

        public void RemoveLink(string linkId)
        {
            var page = documentController.ActivePage;
            if (page == null) return;
            page.Links.RemoveAll(l => l.Id == linkId);
            if (linkViews.TryGetValue(linkId, out var view))
            {
                if (view != null) Destroy(view.gameObject);
                linkViews.Remove(linkId);
            }
        }

        public MonoBehaviour GetView(string objectId)
        {
            objectViews.TryGetValue(objectId, out var view);
            return view;
        }

        public void RefreshLinksFor(string objectId)
        {
            foreach (var link in linkViews.Values)
                if (link.LinkId != null) link.UpdateTransform();
        }

        // ── Pan / Zoom ─────────────────────────────────────────────────────────

        public void Pan(Vector2 screenDelta)
        {
            CanvasContainer.anchoredPosition += screenDelta;
            SaveCameraState();
        }

        public void Zoom(float scrollDelta, Vector2 screenPivot)
        {
            float newScale = Mathf.Clamp(CanvasContainer.localScale.x + scrollDelta, 0.25f, 3f);
            CanvasContainer.localScale = new Vector3(newScale, newScale, 1f);
            SaveCameraState();
        }

        void SaveCameraState()
        {
            var page = documentController?.ActivePage;
            if (page == null) return;
            page.CameraPan = new System.Numerics.Vector2(CanvasContainer.anchoredPosition.x, CanvasContainer.anchoredPosition.y);
            page.CameraZoom = CanvasContainer.localScale.x;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs
git commit -m "feat: NotesCanvasController — page rendering, object/link spawning, pan/zoom"
```

---

### Task 9: NotesUndoManager — command stack with confirm-on-delete

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs`

**Interfaces:**
- Consumes: `NotesCanvasController` (Task 8) — issues its mutation calls from inside commands.
- Produces:
  - `NotesUndoManager.PushCreateNoteCard(NotesCanvasController canvas, System.Numerics.Vector2 position)` — performs the create and pushes an undo entry.
  - `NotesUndoManager.PushCreateImage(NotesCanvasController canvas, System.Numerics.Vector2 position, byte[] bytes)`.
  - `NotesUndoManager.PushCreateDrawing(NotesCanvasController canvas, System.Numerics.Vector2 position, int w, int h)`.
  - `NotesUndoManager.PushCreateLink(NotesCanvasController canvas, string fromId, string toId)`.
  - `NotesUndoManager.PushMove(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)`.
  - `NotesUndoManager.RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)` — triggers the confirm-dialog flow; `onConfirmed(true)` after the user confirms and the delete (+ undo push) has happened.
  - `NotesUndoManager.Undo()` — pops and reverses the last command, no-op if stack empty.
  - Used by Task 10.

- [ ] **Step 1: Write the self-test spec (before implementation)**

Self-test spec (added as `[ContextMenu]` in Step 3), run against an in-scene `NotesCanvasController`/`NotesDocumentController`:
- "Self-Test: Notes Undo — Create/Undo Card" — calls `PushCreateNoteCard`, verifies the active page has 1 object, calls `Undo()`, verifies the active page has 0 objects again.

- [ ] **Step 2: Create NotesUndoManager.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Command-stack undo for canvas object/link create, delete, and move actions.
    /// Delete goes through RequestDeleteObject, which shows a confirm dialog before
    /// mutating anything.
    /// </summary>
    public class NotesUndoManager : MonoBehaviour
    {
        abstract class Command
        {
            public abstract void Undo();
        }

        class CreateObjectCommand : Command
        {
            public NotesCanvasController Canvas;
            public string ObjectId;
            public override void Undo() => Canvas.RemoveObject(ObjectId);
        }

        class CreateLinkCommand : Command
        {
            public NotesCanvasController Canvas;
            public string LinkId;
            public override void Undo() => Canvas.RemoveLink(LinkId);
        }

        class MoveCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public System.Numerics.Vector2 OldPosition;
            public override void Undo()
            {
                Data.Position = OldPosition;
                var view = Canvas.GetView(Data.Id);
                switch (view)
                {
                    case NoteCardView n: n.Refresh(); break;
                    case ImageObjectView i: i.Refresh(); break;
                    case DrawingObjectView d: d.Refresh(); break;
                }
                Canvas.RefreshLinksFor(Data.Id);
            }
        }

        class DeleteObjectCommand : Command
        {
            public NotesCanvasController Canvas;
            public CanvasObjectData Data;
            public override void Undo()
            {
                switch (Data)
                {
                    case NoteCardData c: Canvas.AddNoteCard(c.Position); break;
                    case ImageObjectData img: Canvas.AddImage(img.Position, img.ImageBytes); break;
                    case DrawingObjectData d: Canvas.AddDrawing(d.Position, d.PixelWidth, d.PixelHeight); break;
                }
                // Note: re-created object gets a new Id; any links the deleted object had
                // are not restored. This is an accepted v1 limitation (delete is confirmed
                // up front specifically because it isn't fully reversible for linked objects).
            }
        }

        [Header("Confirm dialog UI (built at runtime, not scene-assigned)")]
        public Font builtinFont;

        readonly Stack<Command> undoStack = new Stack<Command>();
        GameObject confirmDialogGO;

        void Awake()
        {
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public void PushCreateNoteCard(NotesCanvasController canvas, System.Numerics.Vector2 position)
        {
            var data = canvas.AddNoteCard(position);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateImage(NotesCanvasController canvas, System.Numerics.Vector2 position, byte[] bytes)
        {
            var data = canvas.AddImage(position, bytes);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateDrawing(NotesCanvasController canvas, System.Numerics.Vector2 position, int w, int h)
        {
            var data = canvas.AddDrawing(position, w, h);
            if (data == null) return;
            undoStack.Push(new CreateObjectCommand { Canvas = canvas, ObjectId = data.Id });
        }

        public void PushCreateLink(NotesCanvasController canvas, string fromId, string toId)
        {
            var data = canvas.AddLink(fromId, toId);
            if (data == null) return;
            undoStack.Push(new CreateLinkCommand { Canvas = canvas, LinkId = data.Id });
        }

        public void PushMove(NotesCanvasController canvas, CanvasObjectData data, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            undoStack.Push(new MoveCommand { Canvas = canvas, Data = data, OldPosition = oldPos });
        }

        public void RequestDeleteObject(NotesCanvasController canvas, CanvasObjectData data, System.Action<bool> onConfirmed)
        {
            ShowConfirmDialog($"Удалить \"{DescribeObject(data)}\"?", confirmed =>
            {
                if (confirmed)
                {
                    canvas.RemoveObject(data.Id);
                    undoStack.Push(new DeleteObjectCommand { Canvas = canvas, Data = data });
                }
                onConfirmed?.Invoke(confirmed);
            });
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            var command = undoStack.Pop();
            command.Undo();
        }

        static string DescribeObject(CanvasObjectData data) => data switch
        {
            NoteCardData c => string.IsNullOrEmpty(c.Title) ? "заметку" : c.Title,
            ImageObjectData => "изображение",
            DrawingObjectData => "рисунок",
            _ => "объект"
        };

        void ShowConfirmDialog(string message, System.Action<bool> onResult)
        {
            if (confirmDialogGO != null) Destroy(confirmDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            confirmDialogGO = canvasGO;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(280f, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            var msgText = msgGO.AddComponent<UnityEngine.UI.Text>();
            msgText.text = message;
            msgText.font = builtinFont;
            msgText.fontSize = 13;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0.4f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.sizeDelta = Vector2.zero;

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
        }

        void AddDialogButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            var btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<UnityEngine.UI.Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Undo — Create/Undo Card")]
        public void SelfTestCreateUndoCard()
        {
            var canvas = FindObjectOfType<NotesCanvasController>();
            var doc = canvas != null ? canvas.documentController : null;
            if (canvas == null || doc == null || doc.ActivePage == null)
            {
                Debug.Log("Self-Test Notes Undo — Create/Undo Card: FAIL (missing NotesCanvasController/active page in scene)");
                return;
            }

            int before = doc.ActivePage.Objects.Count;
            PushCreateNoteCard(canvas, new System.Numerics.Vector2(0f, 0f));
            bool createdOk = doc.ActivePage.Objects.Count == before + 1;

            Undo();
            bool undoneOk = doc.ActivePage.Objects.Count == before;

            bool ok = createdOk && undoneOk;
            Debug.Log(ok
                ? "Self-Test Notes Undo — Create/Undo Card: PASS"
                : $"Self-Test Notes Undo — Create/Undo Card: FAIL (createdOk={createdOk}, undoneOk={undoneOk})");
        }
    }
}
```

- [ ] **Step 3: Verify compilation in Unity**

Open Unity. Expected: no errors.

- [ ] **Step 4: Run self-test**

Add `NotesCanvasController` + `NotesDocumentController` + `NotesUndoManager` to a scratch GameObject (wire `documentController` and `viewport` — a plain empty `RectTransform` under a `Canvas` is enough for the test). Right-click `NotesUndoManager` → **Self-Test: Notes Undo — Create/Undo Card** → Console: `PASS`.

- [ ] **Step 5: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesUndoManager.cs
git commit -m "feat: NotesUndoManager — command-stack undo with confirm-on-delete"
```

---

### Task 10: CanvasInteractionController — tool routing + NotesToolbar

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs`
- Create: `Assets/WorldGen/Notes/Rendering/NotesToolbar.cs`

**Interfaces:**
- Consumes: `NotesCanvasController` (Task 8), `NotesUndoManager` (Task 9), `NoteCardView`/`ImageObjectView`/`DrawingObjectView` (Tasks 4–6, for click/drag event wiring).
- Produces:
  - `CanvasInteractionController.ActiveTool → NotesTool` enum (`Select, Note, Link, Drawing, Image`), settable via `SetTool(NotesTool tool)`.
  - `NotesToolbar.Initialize(CanvasInteractionController controller)` — builds the 5-button toolbar, wires clicks to `SetTool`.
  - Used by Task 11 (scene wiring), consumes file dialog + clipboard image loading described below.

- [ ] **Step 1: Vendor StandaloneFileBrowser's Editor-mode source (not a package)**

Unity has no built-in runtime (non-Editor) system file-open dialog. The spec requires a real file picker for the Image tool. `StandaloneFileBrowser` (https://github.com/gkngkc/UnityStandaloneFileBrowser) provides one, but **its repo has no `package.json`**, so it cannot be added as a git-URL UPM dependency (confirmed by attempting it — Unity's Package Manager fails with "Repository does not contain a package manifest"). Vendor its source directly instead, matching this project's existing `FastNoiseLite.cs` convention (a third-party single-file/small dependency copied straight into the codebase rather than package-managed).

Create three files under `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/`, copied verbatim from the upstream repo's `Assets/StandaloneFileBrowser/` folder (MIT licensed):
- `IStandaloneFileBrowser.cs` — the `SFB.IStandaloneFileBrowser` interface and nothing else.
- `StandaloneFileBrowser.cs` — the `SFB.StandaloneFileBrowser` static facade and `SFB.ExtensionFilter` struct.
- `StandaloneFileBrowserEditor.cs` — the `#if UNITY_EDITOR`-guarded implementation backed by `UnityEditor.EditorUtility.OpenFilePanel`.

**Known limitation:** only the Editor-mode implementation is vendored. `StandaloneFileBrowser.cs`'s static constructor also has `#elif UNITY_STANDALONE_WIN` / `_OSX` / `_LINUX` branches for real player builds, but those reference platform-specific classes (`StandaloneFileBrowserWindows.cs` etc.) and native plugin DLLs (`Ookii.Dialogs.dll`, `System.Windows.Forms.dll` on Windows) that are **not** vendored here — those branches never compile while running in the Editor (`UNITY_STANDALONE_WIN` etc. are undefined in Editor Play mode), so this is safe for now, but a future standalone `.exe` build of this project would need the missing platform file + native plugins added (with correct per-platform plugin import settings, which needs to be done via the Unity Editor GUI, not hand-written).

Verify compilation: Open Unity. Expected: no errors in Console; `SFB.StandaloneFileBrowser` type is available (namespace `SFB`).

- [ ] **Step 2: Create CanvasInteractionController.cs**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    public enum NotesTool { Select, Note, Link, Drawing, Image }

    /// <summary>
    /// Routes mouse input to canvas actions based on the active tool:
    /// Select (move/pan), Note (click to create card), Link (drag between objects),
    /// Drawing (click to create, drag-paint when a drawing object is active), Image
    /// (click opens a file picker; Ctrl+V pastes clipboard image anywhere, any tool).
    /// </summary>
    public class CanvasInteractionController : MonoBehaviour
    {
        [Header("Dependencies")]
        public NotesCanvasController canvasController;
        public NotesUndoManager undoManager;
        public RectTransform viewportRect;
        public Camera uiCamera; // null for ScreenSpaceOverlay canvases

        [Header("Drawing settings")]
        public float brushRadius = 6f;
        public Color32 brushColor = new Color32(20, 20, 20, 255);
        public int defaultDrawingWidth = 256;
        public int defaultDrawingHeight = 256;

        public NotesTool ActiveTool { get; private set; } = NotesTool.Select;

        string linkDragSourceId;
        string paintingDrawingObjectId;
        bool panning;
        Vector2 lastPanScreenPos;

        public void SetTool(NotesTool tool)
        {
            ActiveTool = tool;
            linkDragSourceId = null;
            paintingDrawingObjectId = null;
        }

        void Update()
        {
            if (canvasController == null || Mouse.current == null) return;

            HandleClipboardPaste();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                HandlePress();
            else if (Mouse.current.leftButton.isPressed && panning)
                HandlePan();
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
                panning = false;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && IsOverViewport(Mouse.current.position.ReadValue()))
                canvasController.Zoom(scroll * 0.001f, Mouse.current.position.ReadValue());
        }

        void HandlePress()
        {
            var screenPos = Mouse.current.position.ReadValue();
            if (!IsOverViewport(screenPos)) return;

            switch (ActiveTool)
            {
                case NotesTool.Select:
                    panning = true;
                    lastPanScreenPos = screenPos;
                    break;
                case NotesTool.Note:
                    undoManager.PushCreateNoteCard(canvasController, ScreenToCanvasPoint(screenPos));
                    break;
                case NotesTool.Drawing:
                    undoManager.PushCreateDrawing(canvasController, ScreenToCanvasPoint(screenPos), defaultDrawingWidth, defaultDrawingHeight);
                    break;
                case NotesTool.Image:
                    var bytes = ImagePicker.OpenFileDialog();
                    if (bytes != null)
                        undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
                    break;
            }
        }

        void HandlePan()
        {
            var screenPos = Mouse.current.position.ReadValue();
            Vector2 delta = screenPos - lastPanScreenPos;
            lastPanScreenPos = screenPos;
            canvasController.Pan(delta);
        }

        bool IsOverViewport(Vector2 screenPos)
        {
            if (viewportRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(viewportRect, screenPos, uiCamera);
        }

        System.Numerics.Vector2 ScreenToCanvasPoint(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasController.CanvasContainer, screenPos, uiCamera, out var local);
            return new System.Numerics.Vector2(local.x, local.y);
        }

        void HandleClipboardPaste()
        {
            if (Keyboard.current == null) return;
            bool ctrl = Keyboard.current.ctrlKey.isPressed;
            bool vPressed = Keyboard.current.vKey.wasPressedThisFrame;
            if (!ctrl || !vPressed) return;

            var bytes = ClipboardImage.TryGetImageBytes();
            if (bytes == null) return;

            var screenPos = Mouse.current.position.ReadValue();
            undoManager.PushCreateImage(canvasController, ScreenToCanvasPoint(screenPos), bytes);
        }

        // ── Called by object views on click/drag, wired externally by NotesCanvasController's spawn sites ──

        public void HandleObjectClicked(string objectId)
        {
            if (ActiveTool == NotesTool.Link)
            {
                if (linkDragSourceId == null)
                {
                    linkDragSourceId = objectId;
                }
                else if (linkDragSourceId != objectId)
                {
                    undoManager.PushCreateLink(canvasController, linkDragSourceId, objectId);
                    linkDragSourceId = null;
                }
            }
        }

        public void HandleObjectDragEnded(string objectId, System.Numerics.Vector2 oldPos, System.Numerics.Vector2 newPos)
        {
            undoManager.PushMove(canvasController, FindObjectData(objectId), oldPos, newPos);
            canvasController.RefreshLinksFor(objectId);
        }

        CanvasObjectData FindObjectData(string objectId)
        {
            var page = canvasController.documentController.ActivePage;
            if (page == null) return null;
            foreach (var obj in page.Objects)
                if (obj.Id == objectId) return obj;
            return null;
        }
    }
}
```

- [ ] **Step 3: Create the ImagePicker helper (StandaloneFileBrowser)**

`CanvasInteractionController` references `ImagePicker.OpenFileDialog()` for the Image tool's click behavior. This wraps the `SFB.StandaloneFileBrowser` API added in Step 1.

Create `Assets/WorldGen/Notes/Rendering/ImagePicker.cs`:

```csharp
using SFB;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Opens a native file-open dialog (via StandaloneFileBrowser) filtered to common
    /// image formats and returns the selected file's raw bytes, or null if the user
    /// cancelled.
    /// </summary>
    public static class ImagePicker
    {
        static readonly ExtensionFilter[] Filters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        public static byte[] OpenFileDialog()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать изображение", "", Filters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return null;
            return System.IO.File.ReadAllBytes(paths[0]);
        }
    }
}
```

- [ ] **Step 4: Create a minimal ClipboardImage helper**

`CanvasInteractionController` references `ClipboardImage.TryGetImageBytes()`. Unity's `GUIUtility.systemCopyBuffer` only exposes text, not image data, so this is a best-effort helper that returns null when no image is available (text-only clipboard is the common case and must not throw).

Create `Assets/WorldGen/Notes/Rendering/ClipboardImage.cs`:

```csharp
namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Best-effort clipboard image access. Unity has no built-in cross-platform image
    /// clipboard API; this returns null when the clipboard doesn't contain image bytes
    /// Unity can decode, which callers must treat as "nothing to paste" rather than an error.
    /// </summary>
    public static class ClipboardImage
    {
        public static byte[] TryGetImageBytes()
        {
            // No built-in Unity API reads image data from the OS clipboard. Returning null
            // here means Ctrl+V silently does nothing when the clipboard has no image Unity
            // can access — callers already treat null as "no-op", so this is safe.
            return null;
        }
    }
}
```

- [ ] **Step 5: Create NotesToolbar.cs**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of tool buttons (Select/Note/Link/Drawing/Image) above the notes canvas.
    /// Clicking a button calls CanvasInteractionController.SetTool and highlights itself.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f);
        public Color inactiveColor = new Color(0.3f, 0.3f, 0.3f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;

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

            var rowGO = new GameObject("NotesToolbar");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = true;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            buttons = new Button[ToolDefs.Length];
            for (int i = 0; i < ToolDefs.Length; i++)
            {
                int index = i;
                var (tool, label) = ToolDefs[i];

                var btnGO = new GameObject($"Tool_{tool}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                img.color = inactiveColor;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActive(tool));
                buttons[index] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = builtinFont;
                text.fontSize = 10;
                text.color = Color.white;
                text.alignment = TextAnchor.MiddleCenter;
                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }

            SetActive(NotesTool.Select);
        }

        void SetActive(NotesTool tool)
        {
            controller.SetTool(tool);
            for (int i = 0; i < ToolDefs.Length; i++)
                buttons[i].GetComponent<Image>().color = ToolDefs[i].tool == tool ? activeColor : inactiveColor;
        }
    }
}
```

- [ ] **Step 6: Wire object view events into CanvasInteractionController**

In `NotesCanvasController.SpawnView` (Task 8), after each `view.Initialize(...)` call, subscribe to `OnClicked`/`OnDragEnded` and forward to a `CanvasInteractionController` reference.

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs`, add a field after `public RectTransform viewport;`:

```csharp
        public CanvasInteractionController interactionController;
```

Replace the `SpawnView` method body's three cases with (same structure, added event wiring):

```csharp
        void SpawnView(CanvasObjectData obj)
        {
            switch (obj)
            {
                case NoteCardData card:
                {
                    var go = new GameObject($"Note_{card.Id}");
                    var view = go.AddComponent<NoteCardView>();
                    view.Initialize(card, CanvasContainer);
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[card.Id] = view;
                    break;
                }
                case ImageObjectData image:
                {
                    var go = new GameObject($"Image_{image.Id}");
                    var view = go.AddComponent<ImageObjectView>();
                    view.Initialize(image, CanvasContainer);
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[image.Id] = view;
                    break;
                }
                case DrawingObjectData drawing:
                {
                    var go = new GameObject($"Drawing_{drawing.Id}");
                    var view = go.AddComponent<DrawingObjectView>();
                    view.Initialize(drawing, CanvasContainer);
                    WireEvents(view.ObjectId, ev => { view.OnClicked += ev.onClicked; view.OnDragEnded += ev.onDragEnded; });
                    objectViews[drawing.Id] = view;
                    break;
                }
            }
        }

        void WireEvents(string objectId,
            System.Action<(System.Action<string> onClicked, System.Action<string, System.Numerics.Vector2, System.Numerics.Vector2> onDragEnded)> subscribe)
        {
            if (interactionController == null) return;
            subscribe((
                onClicked: id => interactionController.HandleObjectClicked(id),
                onDragEnded: (id, oldPos, newPos) => interactionController.HandleObjectDragEnded(id, oldPos, newPos)
            ));
        }
```

Note: `NoteCardView`/`ImageObjectView`/`DrawingObjectView` (Tasks 4–6) already fire `OnDragEnded(id, oldPos, newPos)` with both positions — each view captures its pre-drag `data.Position` before overwriting it in `OnPointerUp`. `WireEvents` above just forwards both values through unchanged.

- [ ] **Step 7: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/CanvasInteractionController.cs Assets/WorldGen/Notes/Rendering/ImagePicker.cs Assets/WorldGen/Notes/Rendering/ClipboardImage.cs Assets/WorldGen/Notes/Rendering/NotesToolbar.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/WorldGen/Notes/Rendering/NoteCardView.cs Assets/WorldGen/Notes/Rendering/ImageObjectView.cs Assets/WorldGen/Notes/Rendering/DrawingObjectView.cs Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/
git commit -m "feat: CanvasInteractionController + NotesToolbar — tool routing, file-picker image tool, drag old-position fix"
```

---

### Task 11: NotesTreeSidebar — collapsible group/page tree

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs`

**Interfaces:**
- Consumes: `NotesDocumentController` (Task 2).
- Produces:
  - `NotesTreeSidebar.Initialize(NotesDocumentController documentController, Transform parent)` — builds the collapsible tree UI.
  - Used by Task 12 (scene assembly).

- [ ] **Step 1: Create NotesTreeSidebar.cs**

```csharp
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Collapsible accordion tree: groups expand to show their pages. Selecting a page
    /// opens it via NotesDocumentController. Collapsible via a header toggle button so the
    /// canvas can reclaim the full width when the tree isn't needed.
    /// </summary>
    public class NotesTreeSidebar : MonoBehaviour
    {
        NotesDocumentController documentController;
        Font builtinFont;
        RectTransform panelRect;
        Transform listContent;
        GameObject listGO;
        bool expanded = true;

        public void Initialize(NotesDocumentController docController, Transform parent)
        {
            documentController = docController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootGO = new GameObject("NotesTreeSidebar");
            rootGO.transform.SetParent(parent, false);
            var vLayout = rootGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            panelRect = rootGO.GetComponent<RectTransform>();

            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rootGO.transform, false);
            var headerImg = headerGO.AddComponent<Image>();
            headerImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            headerBtn.onClick.AddListener(ToggleExpanded);

            var headerTextGO = new GameObject("Text");
            headerTextGO.transform.SetParent(headerGO.transform, false);
            var headerText = headerTextGO.AddComponent<Text>();
            headerText.text = "☰ Страницы";
            headerText.font = builtinFont;
            headerText.fontSize = 12;
            headerText.color = Color.white;
            headerText.alignment = TextAnchor.MiddleLeft;
            var headerTextRect = headerTextGO.GetComponent<RectTransform>();
            headerTextRect.anchorMin = new Vector2(0f, 0f);
            headerTextRect.anchorMax = new Vector2(1f, 1f);
            headerTextRect.offsetMin = new Vector2(6f, 0f);
            headerTextRect.offsetMax = Vector2.zero;

            listGO = new GameObject("List");
            listGO.transform.SetParent(rootGO.transform, false);
            var listVLayout = listGO.AddComponent<VerticalLayoutGroup>();
            listVLayout.spacing = 2f;
            listVLayout.childControlWidth = true;
            listVLayout.childForceExpandWidth = true;
            listGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listContent = listGO.transform;

            var addGroupGO = new GameObject("AddGroupRow");
            addGroupGO.transform.SetParent(rootGO.transform, false);
            AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
                Rebuild();
            });

            documentController.OnDocumentChanged += Rebuild;
            Rebuild();
        }

        void ToggleExpanded()
        {
            expanded = !expanded;
            listGO.SetActive(expanded);
        }

        void Rebuild()
        {
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);

            foreach (var group in documentController.Document.Groups)
                BuildGroupRow(group);
        }

        void BuildGroupRow(PageGroup group)
        {
            var groupGO = new GameObject($"Group_{group.Id}");
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(groupGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            string suffix = group.LinkedPoiId != null ? " 📍" : "";
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 12;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleGO.AddComponent<LayoutElement>().preferredHeight = 18f;

            foreach (var page in group.Pages)
                BuildPageRow(groupGO.transform, group, page);

            AddSmallActionButton(groupGO.transform, "  + Страница", () =>
            {
                documentController.CreatePage(group.Id, $"Страница {group.Pages.Count + 1}");
                Rebuild();
            });
        }

        void BuildPageRow(Transform parent, PageGroup group, NotesPage page)
        {
            var rowGO = new GameObject($"Page_{page.Id}");
            rowGO.transform.SetParent(parent, false);
            var img = rowGO.AddComponent<Image>();
            bool isActive = documentController.ActivePage != null && documentController.ActivePage.Id == page.Id;
            img.color = isActive ? new Color(0.2f, 0.4f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.02f);
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = img;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 18f;
            btn.onClick.AddListener(() =>
            {
                documentController.OpenPage(page.Id);
                Rebuild();
            });

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(rowGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = $"   • {page.Name}";
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void AddSmallActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.45f, 0.25f, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 18f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = Vector2.zero;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesTreeSidebar.cs
git commit -m "feat: NotesTreeSidebar — collapsible group/page accordion tree"
```

---

### Task 12: Scene assembly — NotesRoot prefab wiring + PoiEditPanel integration

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`
- Modify: `Assets/WorldGen/Rendering/PoiEditPanel.cs`
- Modify: `Assets/Scenes/SampleScene.unity` (via Unity Editor, not hand-edited)

**Interfaces:**
- Consumes: `NotesLayoutController` (Task 3), `NotesDocumentController` (Task 2), `NotesCanvasController` (Task 8), `NotesTreeSidebar` (Task 11), `NotesToolbar`/`CanvasInteractionController` (Task 10), `NotesUndoManager` (Task 9), `PoiManager`/`PoiData` (existing).
- Produces: a single `NotesRootBuilder` MonoBehaviour that assembles the whole notes UI hierarchy at `Awake`, so the scene only needs one new GameObject with this component (plus wiring `mapAreaRoot` to the existing map UI's root).

- [ ] **Step 1: Create NotesRootBuilder.cs**

This mirrors the "build everything in `Awake`/`BuildUI`" convention used by `MapEditorPanel`/`PoiEditPanel`, but composes the sub-components built in Tasks 2–11 rather than raw UI widgets.

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Builds the full notes editor UI hierarchy (layout split, sidebar, toolbar, canvas
    /// viewport) at Awake and wires the sub-controllers together. Attach to an empty
    /// GameObject in the scene; assign mapAreaRoot to the existing map UI's root RectTransform
    /// and poiManager to the scene's PoiManager for POI-linked group creation.
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        [Header("External refs")]
        [Tooltip("Root RectTransform of the existing map/editor UI, to be anchored to the left two-thirds.")]
        public RectTransform mapAreaRoot;

        public NotesDocumentController DocumentController { get; private set; }
        public NotesCanvasController CanvasController { get; private set; }

        Font builtinFont;

        void Awake()
        {
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
            layout.Apply();

            var vLayout = notesAreaGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = true;

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            var sidebar = gameObject.AddComponent<NotesTreeSidebar>();
            sidebar.Initialize(DocumentController, notesAreaGO.transform);

            var toolbarRowGO = new GameObject("ToolbarRow");
            toolbarRowGO.transform.SetParent(notesAreaGO.transform, false);

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

            var interaction = gameObject.AddComponent<CanvasInteractionController>();
            interaction.canvasController = CanvasController;
            interaction.viewportRect = viewportRect;
            CanvasController.interactionController = interaction;

            var undoManager = gameObject.AddComponent<NotesUndoManager>();
            interaction.undoManager = undoManager;

            var toolbar = gameObject.AddComponent<NotesToolbar>();
            toolbar.Initialize(interaction, toolbarRowGO.transform);

            // Trigger the canvas to render the document's initially-active page
            // (NotesDocumentController.Awake already opened one, but subscription order
            // means NotesCanvasController may have missed that first event).
            CanvasController.SendMessage("HandleActivePageChanged", DocumentController.ActivePage,
                SendMessageOptions.DontRequireReceiver);
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
```

**Note on initial-page rendering:** `NotesCanvasController.HandleActivePageChanged` is private, and `NotesDocumentController.Awake()` fires `OnActivePageChanged` before `NotesCanvasController.OnEnable()` has necessarily subscribed (Unity doesn't guarantee `Awake`/`OnEnable` ordering across components created via `AddComponent` at runtime in the same frame). Rather than relying on `SendMessage` (fragile, no compile-time check), fix this properly: change `NotesCanvasController.RebuildFromPage` from `void` to `public void`, and call it directly instead of `SendMessage`.

In `Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs` (from Task 8), change:

```csharp
        void RebuildFromPage(NotesPage page)
```

to:

```csharp
        public void RebuildFromPage(NotesPage page)
```

Then in `NotesRootBuilder.Awake()`, replace the `SendMessage` block with:

```csharp
            CanvasController.RebuildFromPage(DocumentController.ActivePage);
```

- [ ] **Step 2: Add "Open Pages" button to PoiEditPanel**

In `Assets/WorldGen/Rendering/PoiEditPanel.cs`, add a field after `public MapLegendUI legendUI;`:

```csharp
        [Tooltip("Notes root — resolves/creates the POI's linked page group when \"Открыть страницы\" is clicked.")]
        public NotesRootBuilder notesRoot;
```

Add `using WorldGen.Notes.Rendering;` to the top imports.

In `BuildUI()`, after the `AddButton(t, "Удалить точку", ...)` call (the last line of `BuildUI()`), add:

```csharp
            AddButton(t, "Открыть страницы", OnOpenPagesClicked, new Color(0.25f, 0.5f, 0.4f));
```

Add the handler method to the class:

```csharp
        void OnOpenPagesClicked()
        {
            var sel = poiManager?.GetSelectedPoi();
            if (sel == null || notesRoot == null) return;

            var doc = notesRoot.DocumentController;
            var group = doc.FindGroupByPoiId(sel.Id);
            if (group == null)
            {
                group = doc.CreateGroup(sel.Name, sel.Id);
                doc.CreatePage(group.Id, "Страница 1");
            }

            doc.OpenPage(group.Pages[0].Id);
        }
```

- [ ] **Step 3: Verify compilation**

Open Unity. Expected: no errors. `PoiEditPanel` shows a new `notesRoot` field in the Inspector, and a "Открыть страницы" button at the bottom of the panel.

- [ ] **Step 4: Wire up in the scene**

- Add a `NotesRootBuilder` component to a new empty GameObject in the scene (e.g. "NotesRoot").
- Find (or create, if the map UI doesn't already have one) a root `RectTransform` wrapping the existing map/editor UI (`MapEditorPanel`'s canvas, `WorldMapRenderer`'s camera viewport, etc.) and assign it to `NotesRootBuilder.mapAreaRoot`. If the map UI currently renders full-screen with no single wrapping root, create one: a `Canvas` GameObject that reparents the existing map camera's render area — the exact grouping depends on current scene structure and should be done in the Unity Editor by hand, verified visually (map must render inside the left two-thirds only, notes UI in the right third, no overlap).
- Assign `PoiEditPanel.notesRoot` to the `NotesRootBuilder` GameObject.
- Press Play. Expected: screen splits 2:1, map on the left, notes UI (empty page, toolbar, sidebar with one default group/page) on the right.

- [ ] **Step 5: End-to-end verify**

1. In the notes area: click "Заметка" tool, click canvas → new note card appears; type a title/body.
2. Click "Рисунок" tool, click empty canvas → new drawing object appears (blank white square); with it selected and Drawing tool active, drag across it → strokes appear.
3. Click "Связь" tool, click one card then another → arrow appears between them; drag one card → arrow follows.
4. Click "Курсор" tool, drag a card → it moves; drag empty canvas → pans; scroll wheel → zooms.
5. Select an object, press its delete action (via a delete button added ad hoc for this test, or Task 9's `RequestDeleteObject` wired to a keyboard Delete handler if time allows) → confirm dialog appears → confirm → object removed; any attached links removed too.
6. In the sidebar: click "+ Группа" → new group appears; click "+ Страница" inside it → new page appears; click between pages → canvas content switches, each page keeps its own objects/pan/zoom.
7. Select a POI on the map, open `PoiEditPanel`, click "Открыть страницы" → a new group named after the POI appears in the sidebar (marked 📍), page opens. Click "Открыть страницы" again → same group reopens (no duplicate).
8. Resize the game window (or Editor Game view) → map and notes areas both rescale proportionally, no overlap.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs Assets/WorldGen/Rendering/PoiEditPanel.cs Assets/WorldGen/Notes/Rendering/NotesCanvasController.cs Assets/Scenes/SampleScene.unity
git commit -m "feat: NotesRootBuilder scene assembly + PoiEditPanel 'Открыть страницы' integration"
```

---

## Post-implementation

Run all self-tests:
- `NotesDocumentController` → **Self-Test: Notes Document CRUD** → `PASS`
- `NotesUndoManager` → **Self-Test: Notes Undo — Create/Undo Card** → `PASS`

And verify the end-to-end flow from Task 12, Step 5.
