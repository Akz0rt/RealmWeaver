using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// ProjectMenuBar, for ReconcileKeyWithLiveProject's second opinion on the current project path. The same
// direction WorkspaceBuilder already depends in (it reads ProjectMenuBar.BarHeightPixels for MenuBarInset),
// so this adds no new coupling between the two namespaces — and it stays one-way: nothing in
// WorldGen.Rendering reaches into this class except through the public methods MapScreenController calls.
using WorldGen.Rendering;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// Owns the in-memory WorkspaceLayout and is the ONLY place that mutates it. Every structural
    /// change (open/close/activate/focus) routes through WorkspaceOps — this class never touches
    /// Tabs lists, or PaneState.ActiveIndex, itself; WorkspaceOps.SetActiveTab owns that scalar the
    /// same way CloseTab owns removal (see WorkspaceLayout.cs:46-47: "WorkspaceOps.
    /// FixActiveIndexAfterRemoval is the one place that keeps this true" — SetActiveTab is the other
    /// place). SplitRatio is the one genuine exception: its own doc comment on WorkspaceLayout says
    /// "no op in this layer moves it", so it is set directly here — see SetSplitRatioLive below.
    ///
    /// WorkspaceBuilder builds the RectTransform/LayoutElement hierarchy and hands the pieces this
    /// class needs to Initialize(); from then on this class applies Layout onto them and reports
    /// gestures (drag, click) back into Layout.
    ///
    /// SINCE TASK 11 IT ALSO OWNS WHEN THE LAYOUT IS STORED, which follows from owning the mutations: every
    /// one of them ends in RaiseChanged, so that is the one place a write can be issued from without a
    /// caller having to remember. The storage itself is WorkspacePrefs'; the project-switch ordering, which
    /// is the part with a real rule in it, is BeginProjectSwitch/EndProjectSwitch below.
    /// </summary>
    public class WorkspaceController : MonoBehaviour
    {
        /// <summary>The three RectTransform/LayoutElement handles WorkspaceBuilder hands over per pane.
        /// PaneRect is the WHOLE pane (tab strip + content, sized by PaneElement.flexibleWidth from
        /// SplitRatio); ContentRect is the narrower child below the tab strip that PaneContent(int) returns.
        /// A plain struct rather than three more Initialize parameters, now that Task 6 needs a ContentRect
        /// distinct from PaneRect for the first time.</summary>
        public readonly struct PaneHandles
        {
            public readonly RectTransform PaneRect;
            public readonly LayoutElement PaneElement;
            public readonly RectTransform ContentRect;

            public PaneHandles(RectTransform paneRect, LayoutElement paneElement, RectTransform contentRect)
            {
                PaneRect = paneRect;
                PaneElement = paneElement;
                ContentRect = contentRect;
            }
        }

        public WorkspaceLayout Layout { get; private set; }

        public event System.Action OnLayoutChanged;

        RectTransform primaryContent;
        LayoutElement primaryLayoutElement;
        RectTransform secondaryContent;
        LayoutElement secondaryLayoutElement;
        // Only Secondary needs a whole-pane show/hide handle — ReflowPanes toggles this GameObject (tab
        // strip AND content together) off when there is no split. Primary has no equivalent field because
        // it is never hidden, so nothing here would ever read it.
        RectTransform secondaryPaneRect;
        RectTransform dividerRect;

        SurfaceRegistry surfaceRegistry;

        /// <summary>True while a NON-workspace AppScreen (Generation/Progress) owns the window, in which case
        /// SyncSurfaces hides every host instead of showing the panes' active tabs. Task 10c Step 1.
        ///
        /// STORED NEGATED — `suppressed`, not `active` — and that is the whole point of the field's shape. A
        /// plain non-[SerializeField] field is reset to default() by a Play-mode domain reload while every
        /// Unity object it describes survives untouched: this arc's recurring defect family, eight sightings
        /// by the time Task 10h stopped to count them (NavigatorView.BuildCreateGroupBar's own paragraph; see
        /// also ScreenSurfaceHosts' and MapSurfaceHost's RECOMPILE GAP paragraphs). `default(bool)` is false,
        /// so with this polarity a reload lands on "the workspace owns the window", which is the state the app
        /// is in essentially all of the time once a world exists. An `active` field with an `= true`
        /// initializer would NOT survive — field initializers do not re-run on deserialization — so a reload
        /// would silently blank the entire workspace until the next AppScreen change, which for a user who
        /// never leaves the map is never.
        ///
        /// DEACTIVATING THE SHELL'S CANVAS IS THE OTHER HALF, and it is ScreenSwitcher's, not this class's:
        /// MapScreenController registers ShellRoot below as AppScreen.Workspace's member GameObject, so the
        /// switcher's existing "deactivate every screen except the target" guarantee covers the workspace
        /// without a second mechanism. This field covers what a GameObject toggle cannot reach — the hosts,
        /// whose surfaces live OUTSIDE the shell hierarchy (the map camera, the five ex-screen canvases) and
        /// would otherwise keep drawing over a deactivated shell. Both halves are needed: the canvas toggle
        /// alone leaves MapSurfaceHost's disabled backgrounds and active map chrome painting over the
        /// generation form (GenerationScreenUI at sortingOrder 50 under MapLayersPanel at 60 — the defect the
        /// user reported at the Task 10a checkpoint), and this field alone leaves the shell's own navigator
        /// and tab strips drawn across it.</summary>
        bool shellSuppressed;

        /// <summary>The shell's own canvas GameObject — what MapScreenController hands ScreenSwitcher as
        /// AppScreen.Workspace's member (see shellSuppressed's doc). Resolved by hierarchy path rather than
        /// stored at build time for the reason MapSurfaceHost.ResolveRootRowBackground gives for its own
        /// re-acquisition: a domain reload wipes the field while the GameObject itself survives, so a lazy
        /// re-Find is recoverable where a build-time assignment is not. "WorkspaceCanvas" is exactly where
        /// WorkspaceBuilder.Awake creates it, relative to THIS transform — WorkspaceBuilder AddComponents this
        /// class onto its own GameObject, so `transform` here already is WorkspaceBuilder's.
        ///
        /// Transform.Find locates INACTIVE children, which is what lets this keep working after the switcher
        /// has deactivated the canvas once.</summary>
        public GameObject ShellRoot
        {
            get
            {
                if (shellRoot != null) return shellRoot;
                var canvas = transform.Find("WorkspaceCanvas");
                shellRoot = canvas != null ? canvas.gameObject : null;
                return shellRoot;
            }
        }

        GameObject shellRoot;

        // ── Persistence (Task 11) ──────────────────────────────────────────────
        //
        // THE KEY IS [SerializeField], AND THAT IS THE POINT OF THE FIELD'S SHAPE. Every other reference in
        // this class is a plain field a Play-mode domain reload wipes — this arc's recurring defect family
        // (see shellSuppressed's own doc for the running count). This one may NOT
        // be wiped: the shell is demolished and rebuilt on that reload (WorkspaceBuilder.Awake), the rebuild
        // restores from prefs, and a forgotten key would restore the WRONG project's tabs — or, worse, the
        // next OnLayoutChanged would write this project's tabs into the no-project slot and leave the
        // project's own stored layout frozen at whatever it held before the recompile. Unity restores
        // [SerializeField] members across a domain reload by serialising and deserialising the component, so
        // this survives exactly where the plain fields do not. It is also why WorkspaceBuilder REUSES this
        // component instead of destroying it: a serialized field survives only on a component that does.
        //
        // "" IS A REAL VALUE, not "unset" — the session that has never been saved to a project. See
        // WorkspacePrefs.KeyFor, which gives it its own slot on purpose.
        [SerializeField] string prefsProjectPath = "";

        /// <summary>True while a project is being swapped underneath the workspace, during which NOTHING is
        /// written to PlayerPrefs. See BeginProjectSwitch for what this exists to prevent.
        ///
        /// STORED SUSPENDED-IS-TRUE so that `default(bool)` — the value a domain reload leaves behind — means
        /// "persistence works", the same polarity argument shellSuppressed's doc makes. A reload landing
        /// mid-load then resumes saving rather than silently never saving again.</summary>
        bool persistSuspended;

        void Awake() => EnsureLayout();

        /// <summary>Guarantees Layout exists, whoever asks first. Awake() calls this, and so does
        /// WorkspaceBuilder.Awake DIRECTLY — it obtains this component with GetComponent on the rebuild path
        /// (which, unlike AddComponent, does NOT invoke Awake synchronously), so whether this class's own
        /// Awake has already run by then depends on Unity's undefined Awake-dispatch order between two
        /// components on the same GameObject. The likely order is the bad one: WorkspaceBuilder is the
        /// PRE-EXISTING component and WorkspaceController was AddComponent-ed later, so the builder tends to
        /// run first and would otherwise reach RestoreFromPrefs -> ApplyRestored -> RaiseChanged (and later
        /// SetSurfaceRegistry -> SyncSurfaces -> Layout.FocusedPane) on a null Layout. Calling this explicitly
        /// is the same direct-call pattern NotesRootBuilder.EnsureBuilt already uses for exactly this reason.
        ///
        /// `if (Layout == null)` GUARDS ONLY A RE-ENTRANT Awake() ON A LIVE OBJECT — some other code path
        /// calling Awake() again without a reload in between, where Layout genuinely still holds what it held
        /// before. It does NOT protect the case it looks like it protects: a Play-mode script recompile.
        /// Layout is a plain auto-property, not a [SerializeField], so its backing field does NOT survive a
        /// script reload the way the GameObject/component hierarchy does. On that exact path Layout IS null
        /// when this runs, the condition is true, and a fresh NewDefault() is created — tabs, split and focus
        /// discarded. Do not read this line as "recompiles are handled".
        ///
        /// WHAT HANDLES THEM is RestoreFromPrefs, which WorkspaceBuilder calls immediately after this one on
        /// the very same path, and which is deliberately NOT gated on `Layout == null` — that condition is
        /// not a "first run" signal (see the paragraph above: it is also true after every recompile, which is
        /// precisely when the saved state must come back). This method's job is only to guarantee something
        /// non-null exists for the restore to overwrite.</summary>
        public void EnsureLayout()
        {
            if (Layout == null) Layout = WorkspaceOps.NewDefault();
        }

        /// <summary>Wires the RectTransforms/LayoutElements WorkspaceBuilder just constructed, then applies
        /// the current Layout onto them once so the initial frame is already correct (the right pane widths,
        /// the secondary + divider shown or hidden) instead of waiting for the first mutation. "Current", not
        /// "freshly-created default": RestoreFromPrefs has already run by the time WorkspaceBuilder reaches
        /// this, so on a launch with a stored split this applies the RESTORED ratio, not 0.5.</summary>
        public void Initialize(PaneHandles primary, PaneHandles secondary, RectTransform dividerRectTransform)
        {
            primaryContent = primary.ContentRect;
            primaryLayoutElement = primary.PaneElement;
            secondaryContent = secondary.ContentRect;
            secondaryLayoutElement = secondary.PaneElement;
            secondaryPaneRect = secondary.PaneRect;
            dividerRect = dividerRectTransform;

            ReflowPanes();
        }

        // An EnsurePaneHandles() used to sit here: it re-acquired the six fields Initialize writes by walking
        // "WorkspaceCanvas/RootRow/PaneContainer/…" with Transform.Find, because a domain reload nulls all
        // six (they are plain non-[SerializeField] fields) while Initialize's only caller runs on the
        // first-build path a recompile skipped. Task 11 deleted it because that premise is gone: the shell is
        // now demolished and rebuilt on every reload (WorkspaceBuilder.Awake), so Initialize runs again with
        // freshly constructed handles and there is nothing left to re-find. Recorded rather than silently
        // dropped — the failure it existed for (PaneContent returning null forever, so SyncSurfaces showed
        // nothing and Hid every host: a blank workspace) is real, and a future change that stops rebuilding
        // the hierarchy would bring it straight back.

        /// <summary>Wires the surface registry (Task 9) and runs the first sync immediately — WorkspaceBuilder
        /// calls this AFTER registering every host it built (page, map), which is necessarily after
        /// Initialize/ReflowPanes have already applied the default Layout. Without an immediate sync here,
        /// the workspace's default WorldMap tab (WorkspaceOps.NewDefault) would sit there un-Shown until
        /// the next unrelated layout change happened to trigger one.</summary>
        public void SetSurfaceRegistry(SurfaceRegistry registry)
        {
            surfaceRegistry = registry;
            SyncSurfaces();
        }

        /// <summary>Tells the workspace whether it currently owns the window — called from
        /// MapScreenController's ScreenSwitcher after-show hook with `screen == AppScreen.Workspace`, so it
        /// fires on every screen change and nowhere else. See shellSuppressed's own doc for the field's
        /// polarity and for which half of the hiding job this covers.
        ///
        /// Re-syncs UNCONDITIONALLY rather than only when the value changed. The state this asserts lives in
        /// objects nothing else here owns — MapSurfaceHost's three background Images and its chrome
        /// GameObjects, five ex-screen GameObjects — and the legacy paths that also touch them are exactly
        /// what Task 10c is unpicking, so re-asserting on every screen change is what makes the ScreenSwitcher
        /// guarantee ("nothing leaks onto the wrong screen") hold rather than merely usually hold. It is
        /// affordable because this is an event, not a frame: RefreshScreenState runs on discrete user actions
        /// (open/close an editor, start/finish a generation), a handful of times per session.</summary>
        public void SetShellActive(bool active)
        {
            shellSuppressed = !active;
            SyncSurfaces();
        }

        // ── Persistence: restore, re-key, and the project-switch seam (Task 11) ─

        /// <summary>Restores the layout stored for the project this controller is currently keyed to, called
        /// once by WorkspaceBuilder.Awake — on a cold start AND on every Play-mode shell rebuild, which is
        /// what makes the rebuild lossless instead of a reset.
        ///
        /// UNCONDITIONAL, never gated on `Layout == null`, and this has been recorded since Task 1 (see
        /// EnsureLayout's own doc, which names it): Awake seeds a default Layout eagerly, so a
        /// `Layout == null` guard would mean the restore never fires and the defect reads as "persistence
        /// silently does nothing".
        ///
        /// A NULL `exists` IS PASSED, DELIBERATELY, and it is the one thing about this method a reader is
        /// likely to want changed. At the moment this runs nothing the tabs point at has loaded: no project
        /// has been opened, so the document is empty and PoiManager/DungeonManager hold nothing. A real
        /// existence predicate would therefore answer "no" to every page, POI and interior tab and prune the
        /// whole layout away — persistence that deletes itself on every launch. WorkspaceOps.Restore's own
        /// doc states what null means there. The existence prune is not skipped, only DEFERRED to the one
        /// moment the answer is knowable: EndProjectSwitch, below.
        ///
        /// CONSEQUENCE, accepted: between launch and the first project open, a restored tab may name a page
        /// or a place that does not exist in the empty world. Each host already tolerates that — PageSurface
        /// Host.Show falls through to the placeholder, MapScreenController.RebindSurface silently keeps its
        /// previous binding (its own doc calls that "wrong but harmless") — and opening the project is what
        /// resolves it, in the direction the DM wanted.</summary>
        public void RestoreFromPrefs()
        {
            ReconcileKeyWithLiveProject();
            ApplyRestored(WorkspacePrefs.Load(prefsProjectPath, null));
        }

        /// <summary>Cross-checks the stored key against the app's LIVE notion of which project is open, and
        /// takes the live one when the two disagree. Called only from RestoreFromPrefs, which is called only
        /// from WorkspaceBuilder.Awake — i.e. exactly once per shell, immediately after the one event that
        /// could have desynchronised them.
        ///
        /// WHY A SECOND OPINION AT ALL. prefsProjectPath is [SerializeField] precisely so a Play-mode domain
        /// reload does not lose it, and Unity's documented behaviour is that serialized members survive such
        /// a reload. But this component is AddComponent-ed at RUNTIME, and "a serialized field on a
        /// runtime-added component survives a domain reload" is the one assumption in this task that could
        /// not be checked without an Editor. If it turned out false, the failure would be the worst kind
        /// this arc knows: the key silently reverts to "" while the project stays open, so the DM's tabs come
        /// back from the no-project slot and — far worse — the NEXT save writes this project's tabs into that
        /// slot, freezing the project's real stored layout at whatever it held before the recompile.
        ///
        /// ProjectMenuBar.CurrentProjectPath is wiped by the same reload (its own doc says so), so this is
        /// not a reliable field standing in for an unreliable one. It is a SECOND field that fails in the
        /// SAME direction — both come back empty — and the rule below is what turns that into safety:
        ///   • both empty            -> "", the no-project slot. Correct for a cold start, and the harmless
        ///                              answer for a reload where both were lost (wrong tabs, not lost ones).
        ///   • one empty, one not    -> the non-empty one. Either survivor is enough.
        ///   • both non-empty        -> the live one. They cannot legitimately disagree — EndProjectSwitch
        ///                              and RekeyTo set this field at the same two moments ProjectMenuBar
        ///                              sets currentPath — so a disagreement means the stored one is stale.
        ///
        /// THE RESULT IS WRITTEN BACK, so PersistNow and every later save read one already-reconciled field
        /// rather than repeating this discovery. That is also why FindFirstObjectByType is affordable here:
        /// once per shell, not once per write.</summary>
        void ReconcileKeyWithLiveProject()
        {
            var menuBar = FindFirstObjectByType<ProjectMenuBar>(FindObjectsInactive.Include);
            string live = menuBar != null ? menuBar.CurrentProjectPath : null;
            if (!string.IsNullOrEmpty(live)) prefsProjectPath = live;
        }

        /// <summary>Announces that a project is about to be loaded UNDER the live workspace. Suspends every
        /// write to PlayerPrefs until EndProjectSwitch; changes nothing else.
        ///
        /// THE COLLISION THIS EXISTS FOR, spelled out because it is invisible and destructive.
        /// WorldMapRenderer.LoadFromCells raises OnWorldRegenerated (via FinishLoadFromCells), which reaches
        /// MapScreenController.OnWorldRegenerated, which calls PruneSurfaces to drop every ex-screen tab —
        /// correct on its own terms, and harmless while nothing was persisted. With persistence wired,
        /// PruneSurfaces raises OnLayoutChanged, which SAVES. And at that instant the key still names the
        /// OUTGOING project, because ProjectMenuBar.LoadFrom only assigns currentPath after the load
        /// succeeds. So opening project B would overwrite project A's stored layout with A's tabs minus every
        /// editor — silently, permanently, and only discoverable the next time A was opened.
        ///
        /// SUSPENDING, rather than re-keying first, is what makes the fix order-INDEPENDENT. Re-keying up
        /// front would merely move the bad write onto B's key, where the restore below happens to overwrite
        /// it — correct only for as long as those two stay in that order. With writes suspended there is no
        /// window at all: whatever the load raises, nothing reaches storage, and the in-memory layout it
        /// mangles is replaced wholesale by EndProjectSwitch anyway.
        ///
        /// PAIRED WITH EndProjectSwitch VIA try/finally at the call site, so a load that throws cannot leave
        /// the workspace permanently unable to save.</summary>
        public void BeginProjectSwitch() => persistSuspended = true;

        /// <summary>Completes the switch begun above: re-keys to the project just loaded, restores THAT
        /// project's stored layout over whatever the load left behind, resumes writing, and stores the
        /// result once.
        ///
        /// THE ORDER IS THE WHOLE DESIGN, and it is stated here rather than left to emerge from statement
        /// order in ProjectMenuBar:
        ///   1. the key moves to the incoming project — before any write can happen, so no write can land on
        ///      the outgoing project's slot;
        ///   2. the stored layout is restored, which REPLACES Layout outright. That is why the mid-load
        ///      prune's effect on the in-memory layout does not need to be undone, or even reasoned about:
        ///      nothing of it survives a successful restore;
        ///   3. and when the restore yields nothing, the fallback is a PRUNE of the layout the DM is
        ///      carrying in from the previous project. That prune is not optional and is not covered by the
        ///      one OnWorldRegenerated already ran: that one is KIND-based (SurvivesWorldChange keeps every
        ///      Page, because notes are not world state), while a project load REPLACES the document too —
        ///      so without this, page tabs from the previous project survive into a world that has never
        ///      heard of them.
        ///      "YIELDS NOTHING" IS TWO CASES, not one, and the second is easy to misread: nothing was
        ///      stored (a project opened for the first time), OR something WAS stored and every tab in it
        ///      pruned away, which WorkspaceOps.Restore also reports as null. In that second case the
        ///      trailing save below replaces the incoming project's stored slot with the carried-in layout.
        ///      That is correct — a stored layout none of whose tabs can be shown is worth nothing, and
        ///      keeping it would mean the DM's next session opens onto the same nothing — but the code reads
        ///      as "nothing was stored" when something was, so it is said here rather than inferred;
        ///   4. writes resume, and one save records the result, so the incoming project immediately owns a
        ///      stored layout rather than waiting for the DM's next click.
        ///
        /// `exists` is the real predicate here, unlike RestoreFromPrefs' null: everything a tab can name has
        /// just been loaded, so "does this still exist" finally has an answer.</summary>
        public void EndProjectSwitch(string projectPath, System.Func<SurfaceRef, bool> exists)
        {
            prefsProjectPath = projectPath ?? "";

            WorkspaceLayout restored = WorkspacePrefs.Load(prefsProjectPath, exists);
            persistSuspended = false;

            if (restored != null) ApplyRestored(restored);
            else PruneSurfaces(exists ?? (_ => true));

            PersistNow();
        }

        /// <summary>Ends a project switch that did NOT complete — a loader threw part-way through, so the
        /// world is half-replaced and no project was actually opened. Resumes writing and does nothing else:
        /// no re-key, no restore, no prune, no save.
        ///
        /// EACH OMISSION IS THE POINT. Re-keying would associate the incoming project's slot with a world it
        /// does not have; ProjectMenuBar's own `currentPath` is likewise assigned only on the success path,
        /// so leaving this alone is what keeps the two agreeing about which project is open. Not pruning
        /// matters more than it looks: EndProjectSwitch's prune asks MapScreenController.SurfaceExists, whose
        /// Page branch reads the live document — and the document is loaded LAST, so a throw from any earlier
        /// loader leaves the PREVIOUS project's document in place. Pruning against it would keep the old
        /// project's page tabs and, with the key already moved, write them into the new project's slot: a
        /// failed load permanently mis-associating pages with a project that never opened.
        ///
        /// The layout left on screen is whatever the mid-load prune (OnWorldRegenerated) made of it, which is
        /// wrong in the same way the half-loaded world is. Nothing here tries to repair that — the next
        /// ordinary user action saves whatever state the app ends up in, under the key that still names the
        /// project the app still (partly) has.</summary>
        public void AbortProjectSwitch() => persistSuspended = false;

        /// <summary>Points persistence at a different project WITHOUT restoring anything. The layout on
        /// screen is the one the destination should have, so it is written under the new key and the OLD key
        /// is left exactly as it was.
        ///
        /// TWO CALLERS, and they want this for opposite-looking reasons that turn out to be the same one:
        ///   • ProjectMenuBar.SaveTo (both «Сохранить» and «Сохранить как…» — see there) — the world did not
        ///     change, so there is nothing to prune and nothing to restore over, and saving a COPY must not
        ///     disturb the original project's stored layout.
        ///   • MapScreenController.RunGeneration, with "" — the world is about to be REPLACED by one that
        ///     belongs to no project, so the tabs must stop being attributed to the project the DM had open.
        ///     A full Begin/EndProjectSwitch would be wrong there precisely because it restores; see that
        ///     call site for the argument.
        /// In both cases the answer to "should the tabs on screen follow the key?" is yes, which is what
        /// separates this from EndProjectSwitch.</summary>
        public void RekeyTo(string projectPath)
        {
            prefsProjectPath = projectPath ?? "";
            PersistNow();
        }

        /// <summary>Replaces Layout with a restored one and makes the whole shell agree with it. Null is a
        /// no-op — WorkspaceOps.Restore returns null for "keep what you have", and this is the one place
        /// that contract is honoured, so no caller repeats the check.
        ///
        /// RaiseChanged, not just ReflowPanes: ReflowPanes turns Secondary/SplitRatio into pixels, but
        /// NavigatorCollapsed and NavigatorWidth become pixels only in NavigatorView.Rebuild, which runs on
        /// OnLayoutChanged and nothing else — SetNavigatorCollapsed's own doc flags this as LOAD-BEARING FOR
        /// TASK 11 in exactly those words. Without it a restored collapsed navigator would stay open at the
        /// wrong width until something unrelated happened to fire a rebuild.
        ///
        /// Harmless on the startup path, where WorkspaceBuilder calls this before any view or host exists:
        /// ReflowPanes no-ops before Initialize, SyncSurfaces early-returns on a null registry, and
        /// OnLayoutChanged has no subscribers yet. The build that follows reads the restored Layout directly
        /// (NavigatorWidth for the column, SplitRatio for the divider), so the first frame is already
        /// correct.</summary>
        void ApplyRestored(WorkspaceLayout restored)
        {
            if (restored == null) return;
            Layout = restored;
            ReflowPanes();
            RaiseChanged();
        }

        /// <summary>Writes the current layout under the current key, unless a project switch is in flight.
        ///
        /// CALLED DIRECTLY FROM RaiseChanged rather than subscribed to OnLayoutChanged, and that is not a
        /// style choice: an event subscription is a runtime delegate, i.e. precisely the thing a Play-mode
        /// domain reload wipes while the component survives. A `OnLayoutChanged += PersistNow` in Awake would
        /// therefore work until the first recompile and then silently stop saving for the rest of the
        /// session — the ninth instance of this arc's recurring defect, introduced by the fix for it. A call
        /// inside RaiseChanged cannot come unsubscribed.</summary>
        void PersistNow()
        {
            if (persistSuspended) return;
            WorkspacePrefs.Save(prefsProjectPath, Layout);
        }

        /// <summary>Belt to RaiseChanged's braces. Everything that changes the layout already saves, so this
        /// normally writes a payload identical to the stored one; it exists for the changes that reach Layout
        /// WITHOUT going through RaiseChanged. There is one today and it is not hypothetical:
        /// SetSplitRatioLive moves SplitRatio on every drag frame and deliberately raises nothing (its own
        /// doc: firing per-frame would write to PlayerPrefs dozens of times per drag) — CommitSplitRatio on
        /// drag-end is what saves it, and a drag the DM never released, because they alt-F4'd mid-gesture,
        /// would otherwise be lost.
        ///
        /// Unity does not call this at all when the process is killed or when Play Mode is stopped in the
        /// Editor, which is why it is not the mechanism persistence relies on — see WorkspacePrefs.Save on
        /// why every ordinary write already flushes to disk by itself.</summary>
        void OnApplicationQuit() => PersistNow();

        // ── Interface WorkspaceController produces ───────────────────────────────

        /// <summary>Where a surface parents itself. Pane 0 (Primary) always exists — WorkspaceLayout's own
        /// invariant guarantees there is never a moment with an empty/absent Primary. Pane 1 (Secondary) is
        /// absent (returns null) whenever Layout.Secondary is null, matching WorkspaceOps.PaneAt's own
        /// "null means absent" contract.
        ///
        /// Returns the pane's ContentArea child — the narrower rect BELOW the tab strip TabStripView now
        /// occupies, not the whole pane (Task 5 returned the whole pane; Task 6 re-points this here, per
        /// Task 5's own handoff note). WorkspaceOps.NormalizeSplit can still promote Secondary into
        /// Primary's slot; PaneContent(0)/PaneContent(1) keep naming the same PHYSICAL containers regardless
        /// (Task 5's carried note to Task 9, still open, now pointing at these narrower rects): whoever
        /// hosts a surface here must re-parent it on promotion rather than assume index 0 always means the
        /// same logical pane's tabs.</summary>
        public RectTransform PaneContent(int pane)
        {
            if (pane == 0) return primaryContent;
            if (pane == 1) return Layout.Secondary != null ? secondaryContent : null;
            return null;
        }

        public void Open(SurfaceRef s, string title, bool inOtherPane)
        {
            // WorkspaceOps.Open no-ops on a null surface without reporting it, which would otherwise make
            // this fire a spurious OnLayoutChanged for a call that changed nothing.
            if (s == null) return;

            WorkspaceOps.Open(Layout, s, title, inOtherPane);
            ReflowPanes();
            RaiseChanged();
        }

        /// <summary>Closes whichever tab currently shows `s`, wherever it is — the shape every Close* path in
        /// MapScreenController needs, since those hold a SurfaceRef and were never told an index (see
        /// WorkspaceOps.FindSurface's own doc). Silently does nothing when the surface is open nowhere, which
        /// is an ORDINARY outcome, not an error: the user may have closed the tab themselves from the strip,
        /// and the editor screen's own «Назад» button would then still call through to here.
        ///
        /// Closes ONE tab, the first FindSurface reports. A surface open in both panes therefore survives in
        /// the other — deliberate: «Открыть рядом» on a POI editor is the user asking for two views, and a
        /// «Назад» in one of them should not reach across and shut the other.</summary>
        public void CloseSurface(SurfaceRef s)
        {
            if (!WorkspaceOps.FindSurface(Layout, s, out int pane, out int index)) return;
            CloseTab(pane, index);
        }

        /// <summary>Drops every tab whose surface `exists` rejects, across BOTH panes, through the tested
        /// WorkspaceOps.PruneMissing (which also collapses the split via NormalizeSplit when that empties the
        /// secondary pane).
        ///
        /// CloseSurface above cannot serve this: it closes ONE tab, the first match, which is right for
        /// "«Назад» in this editor" and wrong for "the world these tabs describe no longer exists". A surface
        /// can be open in both panes, and two DIFFERENT surfaces of a dead kind (two battle grids, two POI
        /// editors) are reachable by ordinary navigation — a caller that closes named refs one at a time
        /// leaves every duplicate behind, pointing at a destroyed world.</summary>
        public int PruneSurfaces(System.Func<SurfaceRef, bool> exists)
        {
            int dropped = WorkspaceOps.PruneMissing(Layout, exists);
            if (dropped == 0) return 0;
            ReflowPanes();
            RaiseChanged();
            return dropped;
        }

        public void CloseTab(int pane, int index)
        {
            if (!WorkspaceOps.CloseTab(Layout, pane, index)) return;
            ReflowPanes();
            RaiseChanged();
        }

        /// <summary>Activates a tab within its pane, through WorkspaceOps.SetActiveTab — the tested op that
        /// validates the pane and index range and reports whether anything actually changed (false for an
        /// absent pane, an out-of-range index, or re-activating the already-active tab).</summary>
        public void SetActive(int pane, int index)
        {
            if (!WorkspaceOps.SetActiveTab(Layout, pane, index)) return;
            RaiseChanged();
        }

        public void FocusPane(int pane)
        {
            int before = Layout.FocusedPane;
            WorkspaceOps.Focus(Layout, pane);
            if (Layout.FocusedPane != before) RaiseChanged();
        }

        /// <summary>Moves one tab, possibly across panes, through the tested WorkspaceOps.MoveTab — the drop
        /// half of Task 10d's tab drag (TabStripView's TabDragHandler is the only caller). `toIndex` is a
        /// PRE-REMOVAL index, an index into the destination pane's tab list AS IT LOOKS RIGHT NOW, including
        /// the dragged tab when the destination is its own pane; WorkspaceOps.cs:210-213 is what
        /// adjusts for the removal, and doing it a second time here would cancel every same-pane reorder.
        ///
        /// A drop that creates the split needs no separate door: MoveTab creates the destination pane on
        /// demand exactly as Open(inOtherPane: true) does, and NormalizeSplit collapses it again if the move
        /// emptied the source — so "drag the last tab out of a pane" self-corrects instead of leaving a hole.
        ///
        /// RAISES OnLayoutChanged (via RaiseChanged), unlike SetSplitRatioLive above: a drop is a discrete
        /// commit, one per gesture, the same shape as CloseTab or CommitSplitRatio — not a per-frame value
        /// Task 11 would then write to PlayerPrefs dozens of times. The DRAG itself raises nothing; only this.
        ///
        /// FOCUS FOLLOWS THE DROP, and not merely because a tab you just dragged is the one you are looking
        /// at: SyncSurfaces iterates the FOCUSED pane first and lets it claim a shared ShareGroup, skipping
        /// any other pane backed by the same physical surface (see SyncSurfaces' own doc). A cross-pane drop
        /// that left focus behind could therefore land the tab in the other pane and never show it. Routed
        /// through WorkspaceOps.Focus, which ignores a pane that no longer exists — after a move that emptied
        /// the source, NormalizeSplit may have collapsed `toPane` away and already set FocusedPane
        /// itself.</summary>
        public void MoveTab(int fromPane, int fromIndex, int toPane, int toIndex)
        {
            if (!WorkspaceOps.MoveTab(Layout, fromPane, fromIndex, toPane, toIndex)) return;
            WorkspaceOps.Focus(Layout, toPane);
            ReflowPanes();
            RaiseChanged();
        }

        // ── Split-ratio drag (wired by WorkspaceBuilder to the DraggableDivider) ─

        /// <summary>Called on every drag-delta frame: updates SplitRatio and reflows the pane widths + divider
        /// position immediately (the "live" half of "drags SplitRatio live and saves on drag-end"), but does
        /// NOT raise OnLayoutChanged. That is what keeps a drag to ONE PlayerPrefs write instead of dozens:
        /// RaiseChanged calls PersistNow, so raising per drag-frame would persist per drag-frame.
        /// CommitSplitRatio below is the single write, and OnApplicationQuit is the belt for a drag the DM
        /// never released.</summary>
        public void SetSplitRatioLive(float desiredRatio)
        {
            if (Layout.Secondary == null) return;   // nothing to drag against; the divider is hidden anyway

            float clamped = WorkspaceOps.ClampSplitRatio(desiredRatio);
            if (Mathf.Approximately(clamped, Layout.SplitRatio)) return;

            Layout.SplitRatio = clamped;
            ReflowPanes();
        }

        /// <summary>The "saves on drag-end" half: raises OnLayoutChanged once, which is what reaches
        /// PersistNow (see RaiseChanged). Called unconditionally on drag-end, matching the
        /// drag-end-always-saves behaviour of the retired notes split (NotesLayoutController.SaveSplitFraction,
        /// deleted in Task 10c — git history, not a file to open).</summary>
        public void CommitSplitRatio() => RaiseChanged();

        // ── Navigator collapse (wired by NavigatorView to its header button) ─────

        /// <summary>Writes Layout.NavigatorCollapsed directly rather than adding a WorkspaceOps op, the same
        /// exception SetSplitRatioLive takes for SplitRatio and for the same reason: WorkspaceLayout's own doc
        /// comment says "no op in this layer moves it", because NavigatorCollapsed is a free bool with no
        /// cross-field invariant to protect. That is NOT the same situation Task 5's review sent back —
        /// PaneState.ActiveIndex had to move to WorkspaceOps.SetActiveTab because the ops layer owns "in
        /// range, -1 iff empty", an invariant a Rendering-layer write could violate. NavigatorCollapsed has no
        /// such invariant; there is nothing for WorkspaceOps to protect. Unlike the drag-live/drag-commit split
        /// for SplitRatio, a collapse toggle is one discrete click, not a per-frame gesture, so there is no
        /// "live" phase here — it raises OnLayoutChanged immediately, matching CommitSplitRatio's timing.
        ///
        /// LOAD-BEARING FOR TASK 11: this method does not touch any pixel itself — NavigatorView.Rebuild is
        /// what turns NavigatorCollapsed/NavigatorWidth into columnLayoutElement.preferredWidth, and it only
        /// runs when OnLayoutChanged fires. Any future path that changes either field (a WorkspacePrefs
        /// restore, say) MUST raise OnLayoutChanged afterward, or the navigator column will silently stay
        /// the wrong width until something unrelated happens to trigger a rebuild.</summary>
        public void SetNavigatorCollapsed(bool collapsed)
        {
            if (Layout.NavigatorCollapsed == collapsed) return;
            Layout.NavigatorCollapsed = collapsed;
            RaiseChanged();
        }

        // ── Applying Layout onto the built hierarchy ──────────────────────────────

        /// <summary>The single place Layout.Secondary/SplitRatio become pixels: shows/hides the secondary
        /// pane and the divider together, and sizes both panes from SplitRatio via flexibleWidth (with
        /// preferredWidth pinned to 0 on both — see WorkspaceBuilder — so flexibleWidth alone decides the
        /// split, exactly as the brief specifies). Safe to call before Initialize (no-ops rather than
        /// throwing) so an out-of-order call from a self-test cannot NRE.</summary>
        void ReflowPanes()
        {
            if (primaryLayoutElement == null) return;

            bool split = Layout.Secondary != null;
            secondaryPaneRect.gameObject.SetActive(split);   // hides the WHOLE pane (tab strip + content)
            dividerRect.gameObject.SetActive(split);          // together, not just the content area.

            float ratio = Layout.SplitRatio;
            primaryLayoutElement.flexibleWidth = split ? ratio : 1f;
            secondaryLayoutElement.flexibleWidth = split ? (1f - ratio) : 0f;

            dividerRect.anchorMin = new Vector2(ratio, 0f);
            dividerRect.anchorMax = new Vector2(ratio, 1f);
        }

        /// <summary>The one place a structural change becomes visible: surfaces re-sync, the layout is
        /// stored, and every view rebuilds. PersistNow sits BETWEEN the two rather than after both, purely so
        /// that a subscriber which reacts by mutating the layout again (none does today) cannot save a
        /// half-applied state — the pixels a subscriber draws are derived from Layout, which is already
        /// final by then either way.</summary>
        void RaiseChanged()
        {
            SyncSurfaces();
            PersistNow();
            OnLayoutChanged?.Invoke();
        }

        // ── Surfaces (Task 9) ──────────────────────────────────────────────────

        /// <summary>Shows each pane's active surface through its registered host, and Hides every registered
        /// host that no pane wants any more. Re-reads PaneContent(pane) fresh on every call rather than
        /// caching it, which is what makes a Secondary->Primary promotion (WorkspaceOps.NormalizeSplit) work
        /// for free: PaneContent(0) already returns the correct NEW physical container after a promotion, and
        /// ISurfaceHost.Show is required to re-parent unconditionally every call — see SurfaceRegistry.cs's
        /// own class doc.
        ///
        /// ONE PHYSICAL SURFACE IS SHOWN ONCE, INTO THE FOCUSED PANE. Several hosts can be backed by the same
        /// object — the three interior kinds all drive the one DungeonEditorScreen (ScreenSurfaceHosts), and
        /// two Page tabs share the one DocumentPageView — so when both panes want the same physical surface,
        /// only one of them can actually have it. This iterates the FOCUSED pane first and skips any later
        /// pane whose host reports an already-claimed ISurfaceHost.ShareGroup, so the focused pane wins and
        /// the other pane's Show never runs at all.
        ///
        /// EARLIER THIS WAS "focused pane shown LAST", relying on the last Show to overwrite the first. That
        /// produced the same visible result and did redundant work to get there — but once Task 10c gave
        /// ScreenSurfaceHosts.Show a re-bind (a tab click has to re-point the screen at its own subject), the
        /// redundant work stopped being free: with an interior tab active in EACH pane, the two kinds
        /// alternate, so every single sync performed two full DungeonEditorScreen.Bind calls, each rebuilding
        /// a settlement's ~40-node canvas and discarding the DM's selection and level tab — and a single tab
        /// click produces several syncs (SetActive -> RaiseChanged, FocusPane -> RaiseChanged, plus
        /// RefreshScreenState's SetShellActive). MapScreenController.RebindSurface's own doc calls its
        /// already-bound early-out "a requirement, not an optimisation", and under focused-last that early-out
        /// could never fire. Claiming first and skipping is what makes the stated contract true.
        ///
        /// The skipped pane is NOT then Hidden: its Kind is deliberately left out of `shownKinds`, but for a
        /// shared-object host the Hide that follows is a no-op anyway (ScreenSurfaceHosts.Hide returns early
        /// when another kind owns the screen), and for Page both panes share one Kind so no Hide is reached.
        /// The skipped pane's content area is simply left empty, which is the accepted single-instance
        /// limitation PageSurfaceHost's own doc records — unchanged by this, only made cheaper.</summary>
        void SyncSurfaces()
        {
            // The real guard: no hosts registered yet (before WorkspaceBuilder's SetSurfaceRegistry call)
            // means there is nothing to show or hide.
            if (surfaceRegistry == null) return;

            // Belt, not the fix: EnsureLayout is what actually guarantees a Layout on every path that
            // reaches here — Awake, plus WorkspaceBuilder.Awake's own explicit call, which covers both a
            // first build and a post-reload rebuild (see EnsureLayout's own doc). This stays because SetSurfaceRegistry is public and calls straight
            // through to here, so a future caller that skips EnsureLayout gets an inert no-op instead of an
            // NRE on Layout.FocusedPane / PaneContent / ActiveSurfaceOf below.
            if (Layout == null) return;

            var shownKinds = new HashSet<SurfaceKind>();
            var claimedGroups = new HashSet<object>();

            // shellSuppressed leaves shownKinds EMPTY, so the Hide loop below retires every registered host —
            // the workspace's surfaces stop drawing while Generation/Progress owns the window (Task 10c
            // Step 1). Expressed as "show nothing" rather than an early return so the Hide half still runs:
            // an early return would leave whatever was last shown (the map camera's punched hole and its
            // active chrome, or a full-screen ex-screen canvas) painting over the screen that took over.
            //
            // FOCUSED PANE FIRST — see this method's own doc for why that flipped, and why it is now paired
            // with claimedGroups rather than relying on a later Show to overwrite an earlier one.
            int[] order = shellSuppressed
                ? new int[0]
                : Layout.FocusedPane == 1 ? new[] { 1, 0 } : new[] { 0, 1 };

            foreach (int pane in order)
            {
                SurfaceRef active = ActiveSurfaceOf(pane);
                RectTransform paneContent = PaneContent(pane);
                if (active == null || paneContent == null) continue;

                ISurfaceHost host = surfaceRegistry.For(active.Kind);
                if (host == null) continue;   // not registered yet — Task 10 registers the rest.

                // A host whose ShareGroup is already claimed is backed by a physical surface an earlier
                // (higher-priority) pane already took. Skipping is not merely an optimisation: showing it
                // would re-bind that one object away from the pane the user is looking at, and then be
                // undone by nothing, since nothing shows it a third time.
                if (!claimedGroups.Add(host.ShareGroup ?? host)) continue;

                host.Show(paneContent, active.Id);
                shownKinds.Add(active.Kind);
            }

            foreach (var host in surfaceRegistry.All)
                if (!shownKinds.Contains(host.Kind))
                    host.Hide();
        }

        /// <summary>The surface the pane's active tab points at, or null for an absent pane / an empty pane.
        /// Same shape as NavigatorView.ActiveSurface, kept separate rather than shared — this one takes an
        /// explicit pane index (both panes, one at a time) where NavigatorView only ever asks about the
        /// FOCUSED pane.</summary>
        SurfaceRef ActiveSurfaceOf(int pane)
        {
            PaneState p = WorkspaceOps.PaneAt(Layout, pane);
            if (p?.Tabs == null || p.ActiveIndex < 0 || p.ActiveIndex >= p.Tabs.Count) return null;
            return p.Tabs[p.ActiveIndex].Surface;
        }
    }
}
