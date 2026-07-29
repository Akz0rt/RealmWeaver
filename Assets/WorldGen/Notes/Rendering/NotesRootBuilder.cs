using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Owns the live notes MODEL (NotesDocumentController) and the Page surface's own view
    /// (DocumentPageView), and nothing else. Through Task 8 this class also built a docked
    /// map/notes split (NotesLayoutController), a page-tree sidebar (NotesTreeSidebar) and a
    /// canvas/whiteboard viewport (NotesCanvasController + chrome) — Task 9 (workspace shell
    /// surfaces) retires all three from this build: the split and sidebar are superseded by the
    /// workspace's own navigator and panes, and canvas/board rendering is later work entirely (see
    /// the workspace-shell spec's Р4). NotesLayoutController.cs and NotesTreeSidebar.cs are left on
    /// disk untouched — deleting them is Task 10's job, not this one's; this class simply stops
    /// calling them.
    ///
    /// DocumentView's root starts parked under a bare, non-Canvas holding transform (nothing renders
    /// there — Unity draws uGUI only under a Canvas) and is re-parented into whichever workspace pane
    /// is showing a Page tab by PageSurfaceHost (Assets/WorldGen/Workspace/Rendering/SurfaceRegistry.cs),
    /// the FIRST time that host's Show() runs. WorkspaceBuilder finds THIS component via
    /// FindFirstObjectByType and reads DocumentController/DocumentView from it — it must never
    /// construct its own NotesDocumentController, or the workspace and whatever else references this
    /// instance (PoiEditorScreen.notesRoot, PoiEditPanel.notesRoot, ProjectMenuBar.notesRoot) would
    /// silently diverge into two different documents.
    ///
    /// Attach to a GameObject in the scene (already is — this predates the workspace-shell plan).
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        public NotesDocumentController DocumentController { get; private set; }
        public DocumentPageView DocumentView { get; private set; }

        Font builtinFont;

        void Awake() => EnsureBuilt();

        /// <summary>Idempotent (guarded by DocumentController, not transform.childCount — see below) and
        /// safe to call from an outside caller (WorkspaceBuilder.Awake) even if THIS component's own
        /// Awake() has not run yet: Unity does not guarantee Awake order across components on different
        /// GameObjects, and WorkspaceBuilder needs DocumentController to exist before it builds the
        /// navigator/Ctrl+K, which read it once at construction time.
        ///
        /// Guarded by `DocumentController != null` rather than `transform.childCount > 0` (the guard this
        /// class used through Task 8): the old guard relied on this method always creating at least one
        /// child GameObject, which stopped being true the moment the docked split (many children) shrank
        /// to just a DocumentPageView + a bare holder (still one child, so it happened to still work, but
        /// only by accident). Guarding on the model reference itself states the actual invariant — "do not
        /// build a second NotesDocumentController" — directly, and keeps holding even if a future edit
        /// changes what children this method creates.</summary>
        public void EnsureBuilt()
        {
            if (DocumentController != null) return;

            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            // A bare (non-Canvas) parking spot — DocumentPageView.root lives here, inert and unrendered,
            // until PageSurfaceHost re-parents it into a real pane. See the class doc.
            var holderGO = new GameObject("PageViewHolder", typeof(RectTransform));
            holderGO.transform.SetParent(transform, false);

            DocumentView = gameObject.AddComponent<DocumentPageView>();
            DocumentView.Initialize(DocumentController, holderGO.GetComponent<RectTransform>(), builtinFont,
                                    boardViewportGO: null);

            var keyboard = gameObject.AddComponent<DocKeyboardController>();
            keyboard.pageView = DocumentView;
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
