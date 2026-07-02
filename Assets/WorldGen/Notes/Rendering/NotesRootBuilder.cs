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
