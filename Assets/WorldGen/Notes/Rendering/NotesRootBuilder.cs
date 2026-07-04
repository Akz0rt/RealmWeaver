using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Builds the full notes editor UI hierarchy (layout split, sidebar, toolbar, canvas
    /// viewport) at Awake and wires the sub-controllers together. Attach to an empty
    /// GameObject in the scene; assign mapCamera to the camera rendering the 3D map so its
    /// viewport gets clamped to the map area (NotesLayoutController.SplitFraction).
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        [Header("External refs")]
        [Tooltip("Camera rendering the 3D map (usually Main Camera / WorldMapRenderer.targetCamera). Its viewport is clamped to the map area.")]
        public Camera mapCamera;

        public NotesDocumentController DocumentController { get; private set; }
        public NotesCanvasController CanvasController { get; private set; }

        Font builtinFont;

        void Awake()
        {
            // A script recompile while already in Play Mode re-invokes Awake() on existing
            // components, but this method builds the entire notes UI imperatively with
            // `new GameObject(...)` — without this guard, every such hot-reload would stack
            // another full duplicate hierarchy on top of the one already built (the child
            // GameObjects survive the reload; only re-running Awake() is new).
            if (transform.childCount > 0) return;

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

            // NotesLayoutController clamps mapCamera.rect to the left split fraction of the
            // screen, so the camera's Clear Flags (Skybox/Solid Color) never touch this right
            // portion of the screen — no camera clears it, ever. This overlay Canvas only paints
            // pixels where an active Graphic currently covers them, so any gap between/around
            // child elements (layout spacing, a shrunk list, a hidden tooltip's old footprint)
            // was left showing whatever pixels a previous frame put there, which read as
            // "ghosted/duplicated" UI until something forced a full backbuffer clear (e.g.
            // resizing the window). A full-bleed opaque background here is redrawn by the Canvas
            // every single frame regardless of what else changes, so it overwrites that stale
            // data unconditionally.
            var notesAreaBg = notesAreaGO.AddComponent<Image>();
            notesAreaBg.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            var layout = gameObject.AddComponent<NotesLayoutController>();
            layout.notesAreaRoot = notesAreaRect;
            layout.mapCamera = mapCamera;
            layout.Apply();

            // NotesArea is a left-to-right split: the page-tree sidebar (fixed width,
            // full height via this group's cross-axis stretch) on the left, and a
            // RightColumn (toolbar + canvas, flexible width) on the right absorbing all
            // remaining space — see NotesTreeSidebar for the sidebar's own fixed/collapsed
            // width handling.
            var hLayout = notesAreaGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = true;

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            var sidebar = gameObject.AddComponent<NotesTreeSidebar>();
            sidebar.Initialize(DocumentController, notesAreaGO.transform);

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

            // NotesDocumentController.Awake() already opened its default page; render it now
            // rather than relying on subscription-order timing across components added this frame.
            CanvasController.RebuildFromPage(DocumentController.ActivePage);
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
