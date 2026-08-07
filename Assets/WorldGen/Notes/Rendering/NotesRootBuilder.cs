using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Owns the live notes MODEL (NotesDocumentController) and the two input helpers that hang off the same
    /// GameObject (DocKeyboardController and, through it, MentionPopup) — and nothing else. Through Task 8
    /// this class also built a docked map/notes split (NotesLayoutController), a page-tree sidebar
    /// (NotesTreeSidebar) and a canvas/whiteboard viewport (NotesCanvasController + chrome) — Task 9
    /// (workspace shell surfaces) retired all three from this build: the split and sidebar are superseded by
    /// the workspace's own navigator and panes, and canvas/board rendering is later work entirely (see the
    /// workspace-shell spec's Р4). Task 10c then DELETED NotesLayoutController.cs and NotesTreeSidebar.cs
    /// outright, as Task 9 said it would — every comment elsewhere in the project that cites either of them by
    /// name is citing history, and git is where to read it.
    ///
    /// SampleScene.unity WAS EXPECTED to hold "missing script" MonoBehaviour entries pointing at both, left
    /// behind by Task 10c's deletion for Task 11 to clean up. It never did, and Task 11 checked rather than
    /// assumed: neither guid (d2428ca0…, c425a136…, recovered from the .meta files 9abca26 deleted) appears
    /// in the scene at HEAD, at 9abca26, or at 9abca26^. Both classes were only ever CODE-CREATED — this
    /// builder AddComponent-ed them — so deleting the .cs files orphaned nothing in the scene at all. Kept
    /// as a note because "the scene still references the deleted sidebar" was carried as a to-do across
    /// three tasks and was never true.
    ///
    /// THE PAGE VIEW NO LONGER LIVES HERE, and that is the whole of the two-panes arc's Task 4. Until then
    /// this class AddComponent-ed the ONE DocumentPageView in the project onto its own GameObject and parked
    /// its root under a bare, non-Canvas "PageViewHolder" transform, from which PageSurfaceHost re-parented
    /// it into whichever pane showed a Page tab. One view cannot show two pages, so «две страницы рядом»
    /// needed one view PER PANE — and a per-pane view is built INSIDE that pane's own content area, by
    /// PageSurfaceHost (Assets/WorldGen/Workspace/Rendering/SurfaceRegistry.cs). The parking spot, the
    /// property that exposed the view and the post-reload repair that re-found it all went with it; see
    /// EnsureBuilt for what took over the "already built" test the parking spot used to carry.
    ///
    /// WorkspaceBuilder finds THIS component via FindFirstObjectByType and reads DocumentController from it —
    /// it must never construct its own NotesDocumentController, or the workspace and whatever else references
    /// this instance (PoiEditorScreen.notesRoot, PoiEditPanel.notesRoot, ProjectMenuBar.notesRoot) would
    /// silently diverge into two different documents.
    ///
    /// Attach to a GameObject in the scene (already is — this predates the workspace-shell plan).
    /// </summary>
    public class NotesRootBuilder : MonoBehaviour
    {
        public NotesDocumentController DocumentController { get; private set; }

        /// <summary>The keystroke handler, which needs a DocumentPageView to act on and no longer has one to
        /// be handed at build time: the views are born inside the panes, later, and are destroyed and rebuilt
        /// with the shell. It is handed a PageFocusRouter instead (WorkspaceBuilder.Awake, once per shell
        /// rebuild) and asks IT, every frame, which pane's view holds the caret — the two-panes arc's Task 5.
        /// Through Task 4 it was pointed at one view per creation and pane 0 won, which is why some comments
        /// nearby still explain the shape of that gap in the past tense.
        ///
        /// Re-acquired by GetComponent on BOTH branches of EnsureBuilt rather than trusted to survive: this is
        /// a plain auto-property with no [SerializeField], so a Play-mode domain reload nulls it while the
        /// COMPONENT it named stays live on this GameObject — the same mechanic EnsureBuilt's own doc spells
        /// out for DocumentController.</summary>
        public DocKeyboardController Keyboard { get; private set; }

        Font builtinFont;

        /// <summary>The builtin font every page view is drawn with. Public because PageSurfaceHost builds
        /// those views now and needs the SAME asset this class would have used — one namer of the resource,
        /// so the two cannot drift apart.</summary>
        public Font BuiltinFont => EnsureFont();

        void Awake() => EnsureBuilt();

        /// <summary>Idempotent and safe to call from an outside caller (WorkspaceBuilder.Awake) even if THIS
        /// component's own Awake() has not run yet: Unity does not guarantee Awake order across components on
        /// different GameObjects, and WorkspaceBuilder needs DocumentController to exist before it builds the
        /// navigator/Ctrl+K, which read it once at construction time.
        ///
        /// THE "ALREADY BUILT" TEST MUST SURVIVE A DOMAIN RELOAD, and that requirement has outlived two
        /// different tests. Round 3 of Task 9's review established the rule the hard way: an EARLIER version
        /// guarded on `DocumentController != null`, i.e. on this class's own auto-property, which has no
        /// `[SerializeField]` and so does NOT survive a Play-mode script reload — the SAME mechanic
        /// WorkspaceController.Awake's own comment documents for `Layout`. A reload made that guard false
        /// again, so EnsureBuilt ran to completion a SECOND time and AddComponent-ed a SECOND
        /// NotesDocumentController onto this GameObject: two divergent notes documents, i.e. lost data, and
        /// the exact failure WorkspaceBuilder.EnsureDocumentController exists to prevent from the other side.
        /// `transform.childCount > 0` replaced it and was reload-SAFE, because the GameObject/Transform
        /// hierarchy is native Unity state rather than a plain C# field.
        ///
        /// THAT TEST LOST ITS FOOTING IN TASK 4 of the two-panes arc, which deleted the one child this method
        /// ever created (the "PageViewHolder" parking spot — see the class doc). This class now builds NO
        /// children at all, so childCount is permanently 0 and the guard would never fire again: the very bug
        /// above, back through the door the fix came in.
        ///
        /// `GetComponent&lt;NotesDocumentController&gt;() != null` is the replacement, and it is the same KIND
        /// of test rather than a weaker one. It does not ask a C# field whether it remembers something; it
        /// asks Unity's live component list what is actually attached to this GameObject — native state, wiped
        /// by nothing short of Destroy, exactly like the Transform hierarchy. It is emphatically NOT the
        /// rejected `DocumentController != null`: the property is the wiped reference, GetComponent is the
        /// live lookup that RECOVERS it, which is why the branch below assigns from it. And it is
        /// self-reinforcing in the way childCount was: the completing path AddComponents exactly one
        /// NotesDocumentController, Unity permits at most one per GameObject unless AddComponent is called
        /// again (which this branch deliberately never does), so the test holds permanently once true.
        ///
        /// The early-return branch re-acquires BOTH components rather than trusting the properties — same
        /// reason, and without it WorkspaceBuilder's rebuild would read a null DocumentController off a
        /// perfectly live builder and register a page host wrapping nothing: reachable, harmless to call into,
        /// and unable to open anything — the "non-null-but-inert reference is worse than leaving it null" trap
        /// WorkspaceBuilder.Awake's comment already names for the tab strips/Navigator.
        ///
        /// WHAT USED TO BE REPAIRED HERE AND NO LONGER NEEDS TO BE. Through Task 3 this branch also called
        /// DocumentPageView.EnsureWired (to re-find the view's own root/viewport/content/placeholder and
        /// re-establish its subscriptions after a reload) and re-Attached MentionPopup. The view is gone from
        /// this GameObject, and with it the reason: a page view now lives under a pane's ContentArea, i.e.
        /// under WorkspaceCanvas, which WorkspaceBuilder.DemolishForRebuild DestroyImmediate-s and rebuilds on
        /// every reload — there is no half-wiped survivor left to repair. MentionPopup is re-Attached from the
        /// same rebuild, beside the router it now asks for a view (WorkspaceBuilder.Awake, Task 5).</summary>
        public void EnsureBuilt()
        {
            var existingDocument = GetComponent<NotesDocumentController>();
            if (existingDocument != null)
            {
                DocumentController = existingDocument;
                Keyboard = GetComponent<DocKeyboardController>();
                return;
            }

            EnsureEventSystemExists();

            DocumentController = gameObject.AddComponent<NotesDocumentController>();
            // Built here, wired elsewhere: `router`/`mentionPopup` stay null until a workspace shell exists
            // to point them somewhere. Every use of both is null-guarded in DocKeyboardController itself
            // (its LateUpdate returns immediately when the router is null OR names no view), so an unwired
            // controller is inert rather than broken — the state a workspace showing no page should be in.
            Keyboard = gameObject.AddComponent<DocKeyboardController>();
        }

        /// <summary>The one place the builtin font resource is named — Resources.GetBuiltinResource is cheap
        /// and returns the same shared asset every call, so the lazy field is a convenience rather than a
        /// requirement (and it is itself a plain field a reload wipes, which the lazy re-fetch covers).</summary>
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
