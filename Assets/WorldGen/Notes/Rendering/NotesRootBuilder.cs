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
    /// the workspace-shell spec's Р4). Task 10c then DELETED NotesLayoutController.cs and
    /// NotesTreeSidebar.cs outright, as Task 9 said it would — every comment elsewhere in the project that
    /// cites either of them by name is citing history, and git is where to read it.
    ///
    /// SampleScene.unity WAS EXPECTED to hold "missing script" MonoBehaviour entries pointing at both, left
    /// behind by Task 10c's deletion for Task 11 to clean up. It never did, and Task 11 checked rather than
    /// assumed: neither guid (d2428ca0…, c425a136…, recovered from the .meta files 9abca26 deleted) appears
    /// in the scene at HEAD, at 9abca26, or at 9abca26^. Both classes were only ever CODE-CREATED — this
    /// builder AddComponent-ed them — so deleting the .cs files orphaned nothing in the scene at all. Kept
    /// as a note because "the scene still references the deleted sidebar" was carried as a to-do across
    /// three tasks and was never true.
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

        /// <summary>Idempotent and safe to call from an outside caller (WorkspaceBuilder.Awake) even if THIS
        /// component's own Awake() has not run yet: Unity does not guarantee Awake order across components on
        /// different GameObjects, and WorkspaceBuilder needs DocumentController to exist before it builds the
        /// navigator/Ctrl+K, which read it once at construction time.
        ///
        /// CORRECTION (round 3 of Task 9's review): an EARLIER version of this method guarded on
        /// `DocumentController != null` instead of `transform.childCount > 0`, reasoning that childCount was
        /// only "accidentally" correct once the docked split shrank to a single child. That reasoning was
        /// backwards and the change was a real bug: `DocumentController` is a plain auto-property with no
        /// `[SerializeField]`, so its backing field does NOT survive a Play-mode script reload — the SAME
        /// mechanic WorkspaceController.Awake's own comment documents for `Layout`. A reload would have made
        /// this guard false again, so `EnsureBuilt` would run to completion a SECOND time and
        /// AddComponent a SECOND `NotesDocumentController` onto this GameObject — precisely the "two
        /// divergent NotesDocuments" data-losing bug this class's own doc warns against, and the exact
        /// failure mode WorkspaceBuilder.EnsureDocumentController exists to prevent from the OTHER direction.
        /// `transform.childCount > 0` is reload-SAFE (the GameObject/Transform hierarchy is native Unity
        /// object state, not a plain C# field) and was never accidental — it is the SAME technique
        /// WorkspaceBuilder.Awake tests on for its own reload detection. The two then do OPPOSITE things with
        /// the answer, and deliberately: this class returns early and re-points a handful of references,
        /// because its one child holds no wiring that a reload could break; WorkspaceBuilder demolishes and
        /// rebuilds, because its children are covered in runtime listeners that a reload does break (see its
        /// own Awake for the argument). This method always
        /// creates exactly one child (`PageViewHolder`) when it runs to completion, so the check holds
        /// permanently once true, reload or not.
        ///
        /// The early-return branch re-acquires DocumentController/DocumentView via GetComponent rather than
        /// trusting them to already be set — those are ALSO plain auto-properties with no `[SerializeField]`,
        /// so a reload wipes them to null too, even though the ACTUAL NotesDocumentController/DocumentPageView
        /// COMPONENTS they used to point at persist as live, native state on this same GameObject (Unity
        /// guarantees at most one of each on a GameObject unless AddComponent is called again, which this
        /// branch deliberately never does). Without this, WorkspaceBuilder.Awake's own post-reload recovery
        /// (which reads these two properties to reconstruct a PageSurfaceHost — see its own comment) would
        /// silently register a host wrapping two null references: reachable, harmless to call into, but
        /// unable to reparent or open anything, functionally identical to not being registered at all — the
        /// SAME "non-null-but-inert reference is worse than leaving it null" trap WorkspaceBuilder.Awake's
        /// comment already names for the tab strips/Navigator, closed here instead of merely documented.
        ///
        /// ROUND 4: re-acquiring the two COMPONENTS was necessary but not sufficient. DocumentPageView's own
        /// internals (its root GameObject, viewport, content, placeholder, its document reference and its
        /// OnActivePageChanged subscription) are wiped by the same reload, and every use of them is
        /// null-guarded — so the surface does not throw, it silently stops responding to Show/Hide, which can
        /// leave a page's opaque viewport stuck visible over whatever the pane shows next. EnsureWired (see
        /// its own doc for the full failure mode) is what repairs that, and it is called from HERE rather than
        /// from DocumentPageView.Awake because this branch is the only place that can tell "already built,
        /// possibly after a reload" apart from "never built" — the distinction that decides whether a
        /// re-find-and-re-assert is a repair or a mis-bind.</summary>
        public void EnsureBuilt()
        {
            if (transform.childCount > 0)
            {
                DocumentController = GetComponent<NotesDocumentController>();
                DocumentView = GetComponent<DocumentPageView>();
                if (DocumentView != null) DocumentView.EnsureWired(DocumentController, EnsureFont());
                return;
            }

            builtinFont = EnsureFont();
            EnsureEventSystemExists();

            DocumentController = gameObject.AddComponent<NotesDocumentController>();

            // A bare (non-Canvas) parking spot — DocumentPageView.root lives here, inert and unrendered,
            // until PageSurfaceHost re-parents it into a real pane. See the class doc.
            var holderGO = new GameObject("PageViewHolder", typeof(RectTransform));
            holderGO.transform.SetParent(transform, false);

            DocumentView = gameObject.AddComponent<DocumentPageView>();
            DocumentView.Initialize(DocumentController, holderGO.GetComponent<RectTransform>(), builtinFont);

            var keyboard = gameObject.AddComponent<DocKeyboardController>();
            keyboard.pageView = DocumentView;
        }

        /// <summary>The one place the builtin font resource is named. `builtinFont` is itself a plain field a
        /// reload wipes, and the recovery branch above has to hand a live Font to DocumentPageView.EnsureWired
        /// (without one, any row Rebuild after a reload would draw text with a null font, i.e. nothing at all)
        /// — Resources.GetBuiltinResource is cheap and returns the same shared asset every call.</summary>
        Font EnsureFont()
        {
            if (builtinFont == null) builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return builtinFont;
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
