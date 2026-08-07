using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;
using WorldGen.Workspace.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Draws a Document page as a scrolling list of rows. Every page is a document — there is no longer a
    /// board-vs-document switch, and this class no longer takes a board viewport to hide.
    ///
    /// THE HISTORY, because the shape of this file still shows it. A board used to be a KIND OF PAGE: the
    /// caller handed this view a `boardViewport` GameObject, and it hid that and showed its own rows, or the
    /// reverse, both children of one RightColumn. Task 9 of the workspace-shell arc stopped building that
    /// split (its one remaining caller passed null), which left a placeholder row standing in for a board
    /// page; Р4 finished the job by making a board a BLOCK inside a page instead (BlockKind.Canvas), drawn
    /// inline by BuildInlineCanvas and full-pane by CanvasSurfaceHost. The parameter and the field went with
    /// it — nothing in the project ever passed one, so nothing had to be migrated. `placeholderGO` outlived
    /// all of it, spent a while built-but-never-shown, and has since been given a job it is genuinely needed
    /// for: it is what the pane shows when its tab names a page the document does not hold (see
    /// ApplyVisibility). Its GameObject is still called "BoardPlaceholder" — the name is history now that
    /// nothing re-finds it by name, and renaming it would only make the git trail harder to follow.
    ///
    /// LIFETIME — ONE VIEW PER PANE, BORN AND BURIED WITH THE SHELL, and this is the whole of the two-panes
    /// arc's Task 4. There used to be exactly ONE of these in the project: NotesRootBuilder AddComponent-ed
    /// it onto its own GameObject, parked `root` under a bare holding transform, and PageSurfaceHost
    /// re-parented that root into whichever pane showed a Page tab. One view cannot show two pages, so
    /// PageSurfaceHost (Assets/WorldGen/Workspace/Rendering/SurfaceRegistry.cs) now BUILDS one of these
    /// inside each pane's own content area, keyed by physical pane index, and never moves it between panes.
    ///
    /// That change deleted a whole mechanism from this class rather than working around it. Every field below
    /// is plain and non-[SerializeField], so a Play-mode domain reload wipes all of them — and `EnsureWired`
    /// + `RecoverBuiltObjects` existed to re-find the built objects and re-establish the subscriptions
    /// afterwards, because the view hung off NotesRootBuilder's GameObject, which SURVIVES that reload. It no
    /// longer does: a view lives under a pane's ContentArea, i.e. under WorkspaceCanvas, which
    /// WorkspaceBuilder.DemolishForRebuild DestroyImmediate-s and rebuilds on every reload. There is no
    /// half-wiped survivor left to repair — the wiped instance is destroyed and a fully-Initialize()d one
    /// takes its place. RecoverBuiltObjects could not have survived the change in any case: it found `root`
    /// as "the only object in the project named DocumentViewport carrying a ScrollRect", which two panes
    /// makes false.
    /// </summary>
    public class DocumentPageView : MonoBehaviour
    {
        /// <summary>Height of the «+ Раздел» strip, in the SAME reserved region `viewportRect.offsetMax`
        /// carves out in Initialize below. One constant read by both, per review round 2's Minor — two
        /// unlinked literals that happened to agree were an assertion ("the two never disagree about its
        /// size") the code didn't actually enforce.</summary>
        const float AddSectionBarHeight = 44f;

        /// <summary>Width of the vertical scrollbar strip. Read twice — by the bar itself and by the viewport
        /// that steps aside for it — for the same reason AddSectionBarHeight is: two literals that happen to
        /// agree are not the same thing as one number.</summary>
        const float ScrollbarWidth = 10f;

        NotesDocumentController documentController;
        GameObject root;
        GameObject viewportGO;
        GameObject placeholderGO;
        GameObject addSectionBarGO;
        RectTransform content;
        Font font;

        /// <summary>The row group whose left/right padding the column measure is expressed through. Fetched
        /// lazily rather than cached at Initialize, because a destroyed component compares equal to null in
        /// Unity, so the lookup simply repeats itself rather than needing a recovery site of its own. (It once
        /// had a named counterpart — a post-reload repair method this class no longer has; see the class doc's
        /// LIFETIME paragraph for why nothing here needs repairing any more.)</summary>
        VerticalLayoutGroup rowLayout;

        /// <summary>Last side padding written, so the measure does not dirty the layout every frame.</summary>
        float appliedSidePadding = -1f;

        /// <summary>How close prose may come to the pane's edge when the pane is narrower than the measure.
        ///
        /// Raised from 8 to 28 at the DM's second checkpoint. The measure only does something once the pane is
        /// wider than 40em (640px), and a pane in a two-pane workspace often is not — so in practice the DM
        /// was almost always seeing this number rather than the measure, and 8px of it put the text all but
        /// against the edge. Air at the margin is not decoration: it is what tells the eye where the line
        /// ends, and text flush to a border reads as text that has been cut off.</summary>
        const float MinSideMargin = 28f;

        // Whether any pane's active tab currently points at this surface — set only by PageSurfaceHost
        // .Show/Hide (via SetSurfaceVisible), and ANDed into every `root` visibility decision below, so
        // Hide() is authoritative until the next Show().
        //
        // WHY THIS IS STILL A SEPARATE AXIS FROM `Page`, now that nothing can bind a page behind the
        // workspace's back. Task 3 deleted NotesDocumentController.ActivePage and its event, which is the
        // reason the original version of this flag gives (a page opened from the POI editor could re-activate
        // `root` in whichever pane it was still parented in, with no pane showing a Page tab at all). That
        // path is gone. What is NOT gone is the asymmetry between the two things Show/Hide have to say:
        // showing names a PAGE, hiding names none — and «hidden» must not be expressed as ShowPage(null),
        // because binding a different page (null included) CLEARS THE UNDO HISTORY, and SyncSurfaces calls
        // Hide for every host crossed with both panes on every sync (see PageSurfaceHost.Hide's own doc).
        // Every tab click would then cost the DM their undo. So: this flag carries VISIBILITY, ShowPage
        // carries WHICH PAGE, and Hide only ever touches the first.
        bool surfaceVisible;

        readonly List<DocBlockView> rows = new List<DocBlockView>();

        public NotesPage Page { get; private set; }
        public IReadOnlyList<DocBlockView> Rows => rows;
        public RectTransform Content => content;

        /// <summary>The whole page surface's own root — everything this view draws hangs off it, and
        /// `ApplyVisibility` switches it on and off wholesale. Null before Initialize.
        ///
        /// IT DOES NOT MOVE BETWEEN PANES, and the doc here said the opposite until Task 4 of the two-panes
        /// arc: PageSurfaceHost used to re-parent the ONE root into whichever pane's content area showed a
        /// Page tab. There is now a view per pane, each built inside its own pane's content area under a
        /// "PageSurface" wrapper, and nothing ever re-homes one — see the class doc's LIFETIME paragraph.
        /// PageSurfaceHost.Show still re-asserts the parent and the stretch on every call, per
        /// ISurfaceHost's contract, but that is a no-op restatement rather than a move.</summary>
        public RectTransform Root => root != null ? (RectTransform)root.transform : null;

        /// <summary>Whether a pane's active tab currently points at this view — the `surfaceVisible` axis,
        /// read-only to the outside. Exposed because «which view is SHOWING page X» is a question the
        /// workspace side has to be able to ask and could not: `Page` alone cannot answer it, since a Hide
        /// deliberately leaves the binding in place (see the field's own doc), so a view stays bound to a page
        /// long after it stopped showing it. CanvasSurfaceHost.ViewShowing is the caller, and got the wrong
        /// view without this.
        ///
        /// A GETTER RATHER THAN A SECOND COPY OF THE FACT, deliberately. The alternative was to have
        /// PageSurfaceHost remember which pane it last Showed — which is this same bool written down twice,
        /// in two objects with different lifetimes, free to disagree with the SetActive calls
        /// ApplyVisibility actually makes. This project has been bitten by exactly that shape often enough
        /// (see WorkspaceController.shellSuppressed's doc for the running count) to prefer one owner.</summary>
        public bool SurfaceVisible => surfaceVisible;

        /// <summary>PageSurfaceHost's Show/Hide call this — see the surfaceVisible field doc for why this is
        /// an axis of its own and not just ShowPage(null). Re-asserts visibility against the page ALREADY
        /// bound, without rebinding anything: a Hide therefore costs neither the undo history nor a rebuild,
        /// and a Show that is followed by ShowPage(theSameId) leaves the rows exactly as they were.</summary>
        public void SetSurfaceVisible(bool visible)
        {
            surfaceVisible = visible;
            ApplyVisibility();
        }

        /// <summary>Binds this view to the page named by `pageId` and draws it. `null`, an empty id, or an id
        /// no group holds all mean "show nothing" — the pane goes empty rather than keeping a page whose tab
        /// no longer points at it.
        ///
        /// THE ONE WAY A PAGE GETS ONTO THIS VIEW, since Task 3: the document no longer remembers an "active
        /// page" and no longer raises an event when it changes, so the page is NAMED by whoever owns the tab
        /// (PageSurfaceHost.Show, from WorkspaceController.SyncSurfaces) instead of being pushed here from
        /// under the workspace.
        ///
        /// A HISTORY BELONGS TO ONE PAGE. Its snapshots are that page's whole block list, so applying one to
        /// another page would replace that page's content with this one's — which is why the clear below (and
        /// the selection and the search box with it) is gated on the page IDENTITY changing, and not merely on
        /// this method having been called. It is called on every sync, including the many that re-name the
        /// page already shown; clearing unconditionally would erase the DM's undo on every tab click, divider
        /// commit and pane focus change.</summary>
        public void ShowPage(string pageId)
        {
            var doc = documentController != null ? documentController.Document : null;
            var page = doc != null && !string.IsNullOrEmpty(pageId) ? NotesDocOps.FindPage(doc, pageId) : null;

            var previous = Page;
            Page = page;

            if (!ReferenceEquals(previous, Page))
            {
                History.Clear();
                SetSelectedBlock(null);
                OnHistoryChanged?.Invoke();
                // Results belong to the page they were found on: a switch to a DIFFERENT page must not leave
                // «3 из 12» standing over text that never said it.
                if (searchBar != null) searchBar.Close();
            }

            ApplyVisibility();
            if (Page != null) Rebuild();
        }

        /// <summary>Turns the two independent facts — is a page bound, and does any pane's active tab point
        /// here — into the four SetActive calls that express them.
        ///
        /// `root` FOLLOWS surfaceVisible ALONE, and the placeholder is what fills it when no page is bound.
        /// Task 3 needed that: a tab can name a page the document does not hold, and until this change the
        /// pane simply went BLANK — a live tab over nothing, with no way for the DM to tell a missing page
        /// from a broken app. The reachable case is a layout restored from PlayerPrefs before any project is
        /// open (WorkspaceController.RestoreFromPrefs deliberately restores without an existence check, and
        /// its own doc names "falls through to the placeholder" as what makes that acceptable). Reviving the
        /// placeholder is what makes that doc true again, and it retires the blank-pane STATE rather than the
        /// one path that reached it.
        ///
        /// THE PLACEHOLDER CANNOT APPEAR WHERE IT SHOULD NOT. It lives under `root`, which stays off entirely
        /// while `surfaceVisible` is false — so a pane that simply is not showing a page shows nothing at all,
        /// not an explanation of an absence nobody asked about. Before Initialize every field here is null and
        /// all four calls are skipped, so a view that PageSurfaceHost has built but not yet Initialize()d
        /// stays inert rather than half-wired.</summary>
        void ApplyVisibility()
        {
            bool showDocument = Page != null;
            if (root != null) root.SetActive(surfaceVisible);
            if (viewportGO != null) viewportGO.SetActive(showDocument);
            if (placeholderGO != null) placeholderGO.SetActive(surfaceVisible && !showDocument);
            if (addSectionBarGO != null) addSectionBarGO.SetActive(showDocument);
        }

        /// <summary>Fires whenever THIS view's block list changed shape.
        ///
        /// WHAT IT IS ACTUALLY FOR, corrected in Task 4 of the two-panes arc after the old wording was
        /// checked against the code and found false. It said "so the project can be marked dirty and
        /// dependent panels (backlinks) can refresh". There is no dirty flag in this project and no
        /// save-on-close prompt, and grep finds exactly ONE subscriber: CanvasTabPruner.PruneBlocks, which
        /// asks whether an open board tab's block still exists. The backlinks half is done by the view itself
        /// (PageFooterView.Refresh from RefreshLinks), not by anyone listening here.
        ///
        /// That mattered enough to fix rather than tidy: believing this event carries "the project must be
        /// saved" makes any narrowing of who raises it look like data loss, and would invite someone to widen
        /// CanvasSurfaceHost.AfterMutation back to notifying a view that is not showing the changed page. See
        /// CanvasSurfaceHost.ViewShowing for what that would actually cost.</summary>
        public event System.Action OnDocumentMutated;

        /// <summary>Raises OnDocumentMutated for a change made OUTSIDE this view — today only by a board
        /// opened as its own surface (CanvasSurfaceHost), which edits a DocBlock this page owns without going
        /// through any of this class's own mutators.
        ///
        /// A C# event can only be raised from the class that declares it, so an outside editor of this
        /// document has no way to announce the change without a method like this one. Rebuild() is
        /// deliberately NOT folded in: the two are separate decisions, and CanvasSurfaceHost happens to make
        /// them together only because it aims BOTH at the same thing — the view that is currently SHOWING the
        /// board's owner page. It used to make them differently (notify always, redraw only the open page) on
        /// the strength of a "must still mark the project dirty" rule that the event's own doc above now
        /// records as never having been true.</summary>
        public void MarkDocumentMutated() => OnDocumentMutated?.Invoke();

        /// <summary>Raises NotesDocumentController.OnDocumentChanged — the event NavigatorView rebuilds
        /// on (NavigatorView.cs:203), which OnDocumentMutated above is NOT: that one only reaches the
        /// workspace's surface pruner. CharacterHeaderView writes straight into Page.Character without
        /// going through any wrapper of this class, so it has no other way to tell the navigator its
        /// «Персонажи» row (role text, portrait thumbnail) is stale. Thin forwarder for the same reason
        /// MarkDocumentMutated is one — CharacterHeaderView holds `this`, not `documentController`, which
        /// stays private — and mirrors the explicit-null-check idiom every other read of
        /// `documentController` in this file uses (lines 611, 627, 697, 733, 986, 1047), not `?.`, because
        /// that operator tests reference identity and would miss a destroyed-but-not-null Unity object.
        /// Cheap to over-call, same reasoning as NotifyDocumentChanged's own doc
        /// (NotesDocumentController.cs:157-158): every listener just coalesces into a LateUpdate flag.</summary>
        public void NotifyDocumentChanged()
        {
            if (documentController != null) documentController.NotifyDocumentChanged();
        }

        // ── undo/redo and selection (Р2 Task 8) ─────────────────────────────────

        /// <summary>The page's editing toolbar, so Task 9 can fill its «Ссылка» hook. Built once by
        /// Initialize and destroyed with this whole view, so nothing has to re-find it — see the class doc's
        /// LIFETIME paragraph.</summary>
        public PageToolbar Toolbar { get; private set; }

        /// <summary>«Итоги» and «Упомянута в», drawn under the last row. Lives in `content` so it scrolls
        /// with the prose it summarises.</summary>
        PageFooterView footer;

        /// <summary>The character card — portrait and four fields — drawn ABOVE the rows when
        /// CharacterOps.IsCharacter(Page) is true, absent otherwise. Lives in `content`, like the footer,
        /// but is built ONCE and updated in place rather than rebuilt every refresh — see its own class
        /// doc for why an editable field needs that and a read-only summary does not.</summary>
        CharacterHeaderView header;

        /// <summary>Ctrl+F. Lives on `root` rather than being the hidden box itself — see its own doc.</summary>
        PageSearchBar searchBar;

        /// <summary>Fires whenever undo or redo becomes possible or impossible, so the toolbar's ↶/↷ can be
        /// enabled or disabled without polling.</summary>
        public event System.Action OnHistoryChanged;

        /// <summary>Страницу целиком ЗАМЕНИЛИ снимком истории — Ctrl+Z или Ctrl+Y. Отдельно от
        /// OnDocumentMutated, и это принципиально: та срабатывает по несколько раз в секунду при обычном
        /// наборе текста, а на это событие подписан пересбор поверхностей, который слишком дорог для
        /// каждого нажатия клавиши.
        ///
        /// ЗАЧЕМ ОНО ВООБЩЕ. Apply не правит блоки на месте, а кладёт на страницу НОВЫЕ объекты. Всё, что
        /// держит ссылку на блок и живёт вне этой страницы — прежде всего развёрнутая доска в своей
        /// вкладке, — после отмены указывает на выброшенный объект: рисует старое и пишет правки туда,
        /// откуда их никто не сохранит. Свои строки страница перестроит сама, чужим держателям нужен
        /// сигнал.</summary>
        public event System.Action OnHistoryApplied;

        /// <summary>NOT a field initializer and not readonly: a domain reload restores a MonoBehaviour by
        /// deserializing it, and neither runs again — the same trap PaneFocusOnClick.raycastScratch documents.
        /// A history lost to a reload is correct, since the rows it described are lost too.</summary>
        DocHistory history;
        DocHistory History => history ?? (history = new DocHistory());

        public bool CanUndo => History.CanUndo;
        public bool CanRedo => History.CanRedo;

        /// <summary>The row drawn as selected — a picture, which cannot hold a caret. At most one, which is
        /// why this lives on the page and not on a row.</summary>
        public string SelectedBlockId { get; private set; }

        public void SetSelectedBlock(string blockId)
        {
            if (SelectedBlockId == blockId) return;
            SelectedBlockId = blockId;
            foreach (var row in rows)
                if (row != null) row.SetSelected(row.BlockId == blockId);
        }

        /// <summary>Remembers the page as it is NOW, before a change is made to it. Every caller does this
        /// immediately before mutating — the snapshot is where Ctrl+Z goes back to.</summary>
        public void PushHistory(string focusId, int caret)
        {
            if (Page == null) return;
            // Page.Character читается ЗДЕСЬ, в том же вызове, что и Page.Blocks — это единственный момент,
            // когда снимок берётся, и мутация ещё не случилась.
            History.Push(Page.Blocks, Page.Character, focusId, caret);
            OnHistoryChanged?.Invoke();
        }

        /// <summary>Remembers a state the caller already holds a copy of — DocKeyboardController takes one
        /// before a key it cannot know in advance will change anything, and commits it only if the key did.</summary>
        public void PushHistoryOf(IReadOnlyList<DocBlock> blocks, string focusId, int caret)
        {
            // Карточка читается СЕЙЧАС, а не когда-то раньше у вызывающего — все вызывающие правят BLOCKS,
            // а не карточку, так что Page.Character в этот момент всё ещё несёт значение ДО правки, что и
            // требуется. Если бы будущий вызывающий сначала поправил карточку и только потом позвал этот
            // метод, снимок унёс бы уже новое значение, и отмена молча стала бы пустышкой.
            History.Push(blocks, Page != null ? Page.Character : null, focusId, caret);
            OnHistoryChanged?.Invoke();
        }

        /// <summary>Remembers the page with ONE row's text as it was before the DM's first keystroke in it —
        /// the rest of the page is already correct, since nothing else has changed yet. Reconstructed rather
        /// than snapshotted on focus, so a row that is only looked at never becomes a step.</summary>
        void PushHistoryBeforeFirstEdit(string blockId, string textBefore)
        {
            if (Page == null) return;

            var before = DocHistory.Copy(Page.Blocks);
            foreach (var b in before)
                if (b.Id == blockId) { b.Text = textBefore; break; }

            // Этот метод вызывается ПОСЛЕ того, как нажатие уже попало в строку, и восстанавливает
            // список строк вручную (см. цикл выше) — единственное место в файле, где «сейчас» не значит
            // «до правки». Карточку восстанавливать так не нужно: правка ТЕКСТА строки её не трогает,
            // так что Page.Character, прочитанный здесь и сейчас, и есть состояние до правки. Но это
            // держится только пока верно ИМЕННО ЭТО — если задача 6 когда-нибудь заставит поле карточки
            // звать этот же метод напрямую (а не завести свой аналог для полей шапки), карточка тоже
            // окажется уже мутированной к этому моменту, и её придётся восстанавливать вручную, как
            // строку выше.
            History.Push(before, Page.Character, blockId, (textBefore ?? "").Length);
            OnHistoryChanged?.Invoke();
        }

        public void Undo() => Apply(History.Undo(Page != null ? Page.Blocks : null, Page != null ? Page.Character : null, LastFocusedBlockId, LastFocusedCaret));
        public void Redo() => Apply(History.Redo(Page != null ? Page.Blocks : null, Page != null ? Page.Character : null, LastFocusedBlockId, LastFocusedCaret));

        /// <summary>Puts a remembered state back on the page. The block list is replaced IN PLACE rather than
        /// reassigned: NotesPage.Blocks is what the serializer writes and what every op holds, and swapping
        /// the list object would leave anything holding the old one editing a document that is no longer
        /// saved.</summary>
        void Apply(DocSnapshot snapshot)
        {
            if (snapshot == null || Page == null) return;

            Page.Blocks.Clear();
            Page.Blocks.AddRange(snapshot.Blocks);
            // Экземпляр из снимка отдаётся живой странице БЕЗ копии — и это правильно ровно потому, что
            // снимок снят со стека и больше нигде не хранится (так же поступают Blocks). Копия здесь стоила
            // бы копирования на каждое нажатие и не защищала бы ни от чего.
            Page.Character = snapshot.Character;
            SetSelectedBlock(null);
            OnHistoryChanged?.Invoke();
            OnDocumentMutated?.Invoke();

            // The remembered row may not exist in the state being restored — undoing the creation of a row
            // whose id the snapshot recorded as focused, for instance — and RebuildAndFocus would then ask a
            // row that is not there to take the caret.
            bool focusExists = false;
            if (!string.IsNullOrEmpty(snapshot.FocusId))
                foreach (var b in Page.Blocks)
                    if (b.Id == snapshot.FocusId) { focusExists = true; break; }

            if (focusExists) RebuildAndFocus(snapshot.FocusId, snapshot.Caret);
            else Rebuild();

            // Снимок мог вернуть карточку персонажа (Page.Character выше) к прежнему виду — навигатор
            // рисует её роль и портрет только по OnDocumentChanged (см. NotifyDocumentChanged), а Undo/
            // Redo сюда не заходят вовсе: без этой строки Ctrl+Z по полю шапки или по портрету оставлял
            // бы «Персонажи» в панели навигации устаревшими до случайного пересбора. Дёшево звать и на
            // обычной прозе — см. NotesDocumentController.cs:157-158 — а отмена сама по себе разовое
            // действие ДМ, не нажатие клавиши на каждый символ.
            NotifyDocumentChanged();

            // ПОСЛЕДНИМ, после перестроения строк: подписчик пересобирает поверхности, которые держат
            // блоки этой страницы, и делать это до того, как страница привела себя в порядок, значило бы
            // показать им промежуточное состояние.
            OnHistoryApplied?.Invoke();
        }

        /// <summary>The row the caret was last in, and where in it — TOLD to this class by
        /// DocKeyboardController rather than worked out here, so there is exactly one answer to "where is the
        /// caret" and it lives where the keyboard already tracks it.
        ///
        /// LAST, not current, and that is the whole point: clicking a toolbar button moves Unity's selection
        /// to the button, which deactivates the input field before onClick runs. A toolbar that asked which
        /// row is focused AT THE MOMENT OF THE CLICK would always be told "none".</summary>
        public string LastFocusedBlockId { get; private set; }
        public int LastFocusedCaret { get; private set; } = -1;

        public void NoteFocus(string blockId, int caret)
        {
            LastFocusedBlockId = blockId;
            LastFocusedCaret = caret;
        }

        // ── what the toolbar asks for (the rules stay in NotesDocOps; this is where they are applied) ──

        /// <summary>Turns the row the caret was last in into another kind. False when there was no such row
        /// or the change was refused — NotesDocOps.SetKind owns every reason, and the undo step is pushed
        /// only once it has said yes, so a refused button press does not leave a Ctrl+Z that does nothing.</summary>
        public bool ConvertFocused(BlockKind kind)
        {
            if (Page == null || string.IsNullOrEmpty(LastFocusedBlockId)) return false;

            var before = DocHistory.Copy(Page.Blocks);
            if (!NotesDocOps.SetKind(Page.Blocks, LastFocusedBlockId, kind)) return false;

            PushHistoryOf(before, LastFocusedBlockId, LastFocusedCaret);
            OnDocumentMutated?.Invoke();
            RebuildAndFocus(LastFocusedBlockId, LastFocusedCaret);
            return true;
        }

        /// <summary>The Ctrl+K palette is up, whether opened as itself or as the link picker. Set by
        /// PageLinkBridge.</summary>
        public bool PaletteOpen { get; set; }

        /// <summary>The Ctrl+F field has the caret. Set by PageSearchBar.</summary>
        public bool SearchOwnsKeys { get; set; }

        /// <summary>Правда ли, что каретка сейчас в одном из четырёх полей шапки-карточки персонажа
        /// («Кто»/«Где искать»/«Чего хочет»/«Как играть»). НЕ входит в KeyboardSuspended ниже — см.
        /// DocKeyboardController.LateUpdate, узкий гейт на Enter/Tab, для чего это вообще заведено:
        /// KeyboardSuspended отсекает кадр целиком ВЫШЕ проверки Ctrl+Z (той же LateUpdate) и выше
        /// CharacterHeaderView.Update, где живёт Ctrl+V-вставка портрета — оба уже проверены ДМ и не
        /// должны перестать работать, пока поле шапки в фокусе.</summary>
        public bool CharacterHeaderOwnsKeys => header != null && header.AnyFieldFocused;

        /// <summary>True while something else owns the keyboard. Read by DocKeyboardController, which stands
        /// down entirely: the page's keys are only the page's while nothing is over it.
        ///
        /// AN OR OF TWO INDEPENDENT FLAGS, not one shared bool. Two unrelated things can suspend this at
        /// once — a DM can open the search bar and then press Ctrl+K — and with a single switch whichever of
        /// them finished last would speak for both, handing the page's keys back while the other still had
        /// them.</summary>
        public bool KeyboardSuspended => PaletteOpen || SearchOwnsKeys;

        /// <summary>Задача 5 арки «две страницы рядом». Является ли ЭТОТ вид тем, кому сейчас адресованы
        /// клавиши. Подставляется WorkspaceBuilder'ом в том же крючке OnViewCreated, что и CanvasRouter/
        /// WorldSource/LinkRouter/LinkPicker, и спрашивает ровно один источник истины — PageFocusRouter.
        ///
        /// ЗАЧЕМ ВООБЩЕ. Аккорды, которые ловит DocKeyboardController, приходят в одно место на всё
        /// приложение, а вот Ctrl+F опрашивает клавиатуру САМ, из PageSearchBar, — а этих панелей теперь
        /// две, и каждая со своим видом и своей строкой поиска. Без этой проверки один Ctrl+F открывал бы
        /// ОБЕ строки поиска, и каждая звала бы ActivateInputField: кому достанется каретка, решал бы
        /// порядок вызова Update у двух компонентов, который Unity не определяет.
        ///
        /// ДЕЛЕГАТ, А НЕ ФЛАЖОК, который кто-то обязан вовремя переставить: вторая копия факта «чей сейчас
        /// фокус» — это ровно та ошибка, которой посвящён класс-док PageFocusRouter (и добрая половина
        /// дефектов этой оболочки). Здесь не хранится ничего, вопрос каждый раз задаётся заново.
        ///
        /// НЕПРОВЕДЁННЫЙ ДЕЛЕГАТ ЗНАЧИТ «ДА»: в сцене без оболочки (голый стенд, notes без workspace) вид
        /// в проекте один и Ctrl+F обязан работать ровно как работал.</summary>
        public System.Func<bool> KeyboardTargetProbe;

        public bool IsKeyboardTarget => KeyboardTargetProbe == null || KeyboardTargetProbe();

        /// <summary>Opens the link picker, injected by PageLinkBridge because the picker is Ctrl+K's own
        /// palette and lives on the workspace side of the layer boundary. Null in a scene without a
        /// workspace, which is why the toolbar's «Ссылка» reads CanInsertLink rather than assuming.</summary>
        public System.Action LinkPicker;

        public bool CanInsertLink => LinkPicker != null;

        public void RequestInsertLink() => LinkPicker?.Invoke();

        /// <summary>Writes a link into the row the caret was last in, at the offset it was last at.
        ///
        /// THE TOKEN IS BUILT BY NotesLinkOps.MakeToken AND BY NOTHING ELSE, so what is inserted here and
        /// what ParseSpans reads back cannot drift. The name goes in as the object is called right now; from
        /// then on it is only a fallback, since rendering resolves the id every time (see NotesLinkOps' class
        /// doc).
        ///
        /// The caret lands AFTER the token, where the DM was going to keep typing.</summary>
        public bool InsertTokenAtCaret(string kind, string id, string name)
            => InsertTokenInto(LastFocusedBlockId, LastFocusedCaret, kind, id, name);

        /// <summary>Writes a link into a NAMED row at a named offset — the toolbar's route above passes the
        /// row the caret was last in, the drag (Task 11) passes the row the pointer was over. One
        /// implementation, so a dropped link and a chosen one are the same edit.</summary>
        public bool InsertTokenInto(string blockId, int caretOffset, string kind, string id, string name)
        {
            if (Page == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id)) return false;
            if (string.IsNullOrEmpty(blockId)) return false;

            DocBlock target = null;
            foreach (var b in Page.Blocks)
                if (b.Id == blockId) { target = b; break; }
            if (target == null) return false;

            string text = target.Text ?? "";
            // -1 is the "end of the text" convention DocKeyResult uses, and a caret recorded against a
            // LONGER version of this row (an undo since, say) must not index past its end.
            int caret = caretOffset < 0 || caretOffset > text.Length ? text.Length : caretOffset;

            PushHistory(blockId, caret);

            string token = NotesLinkOps.MakeToken(kind, id, name ?? "");
            target.Text = text.Substring(0, caret) + token + text.Substring(caret);

            OnDocumentMutated?.Invoke();
            RebuildAndFocus(target.Id, caret + token.Length);
            return true;
        }

        /// <summary>Задача 10б: заменяет ДИАПАЗОН [start, end) в названной строке токеном ссылки — «@» и
        /// набранный после него запрос заменяются токеном одним действием, а не вставляются рядом с ними.
        /// Тот же PushHistory/RebuildAndFocus, что и InsertTokenInto прямо выше (один Ctrl+Z откатывает
        /// всю замену целиком), только читает диапазон вместо одной точки.
        ///
        /// КАРЕТКА ПОСЛЕ ЗАМЕНЫ ВСТАЁТ СРАЗУ ЗА ТОКЕНОМ — ровно там же, где она стояла бы, продолжи ДМ
        /// печатать «вручную»: start плюс длина нового токена, а не end плюс что-то — end может лежать
        /// дальше start на длину query, которая при замене исчезает.</summary>
        public bool ReplaceRangeWithToken(string blockId, int start, int end, string kind, string id, string name)
        {
            if (Page == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id)) return false;
            if (string.IsNullOrEmpty(blockId)) return false;

            DocBlock target = null;
            foreach (var b in Page.Blocks)
                if (b.Id == blockId) { target = b; break; }
            if (target == null) return false;

            string text = target.Text ?? "";
            int s = Mathf.Clamp(start, 0, text.Length);
            int e = Mathf.Clamp(end, s, text.Length);

            PushHistory(blockId, s);

            string token = NotesLinkOps.MakeToken(kind, id, name ?? "");
            target.Text = text.Substring(0, s) + token + text.Substring(e);

            OnDocumentMutated?.Invoke();
            RebuildAndFocus(target.Id, s + token.Length);
            return true;
        }

        /// <summary>A link dropped BETWEEN rows becomes a row of its own, holding nothing else.
        ///
        /// I2 IS THIS METHOD'S TO KEEP: NotesDocOps.Insert is a bare list insert that clamps no depths, so a
        /// wrong answer here ships silently until Validate runs. A non-empty page's first block must be a
        /// Section, so a drop aimed above the very first row lands just BELOW it instead of displacing it —
        /// the DM sees the link one row further down than they aimed, which is a visible, correctable
        /// surprise rather than an invalid document. On a page with no blocks at all the drop makes the
        /// Section itself: there is no other block whose depth it could take, and a page has to start with
        /// one.
        ///
        /// ONE UNDO for the whole drop, pushed before anything changes — Ctrl+Z removes the row, not just
        /// the token inside it.</summary>
        public bool DropLinkAsNewBlock(int blockIndex, string kind, string id, string name)
        {
            if (Page == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id)) return false;

            bool empty = Page.Blocks.Count == 0;
            int at = empty ? 0 : Mathf.Clamp(blockIndex, 1, Page.Blocks.Count);
            // The row above is what the new one belongs with — dropping under a sub-point puts the link at
            // that sub-point's level rather than snapping back out to the section.
            int depth = empty ? 0 : Mathf.Max(1, Page.Blocks[at - 1].Depth);

            PushHistory(LastFocusedBlockId, LastFocusedCaret);

            string token = NotesLinkOps.MakeToken(kind, id, name ?? "");
            var block = NotesDocOps.NewBlock(empty ? BlockKind.Section : BlockKind.Item, depth, token);
            NotesDocOps.Insert(Page.Blocks, at, block);

            OnDocumentMutated?.Invoke();
            RebuildAndFocus(block.Id, token.Length);
            return true;
        }

        /// <summary>Puts a picture into the prose, immediately after the row the caret was last in — and
        /// after that row's CHILDREN, so an image dropped under a row with sub-rows lands below the whole
        /// thought rather than inside it.
        ///
        /// It goes in at the focused row's depth, never at 0: I2 says a non-empty page starts with a Section,
        /// and a picture is not one. It also becomes the SELECTED row, so the DM can undo it with Ctrl+Z or
        /// delete it with the very next Backspace.</summary>
        public bool InsertImageAfterFocused(byte[] bytes)
        {
            if (Page == null || bytes == null || bytes.Length == 0) return false;
            if (string.IsNullOrEmpty(LastFocusedBlockId)) return false;

            int at = -1;
            for (int i = 0; i < Page.Blocks.Count; i++)
                if (Page.Blocks[i].Id == LastFocusedBlockId) { at = i; break; }
            if (at < 0) return false;

            PushHistory(LastFocusedBlockId, LastFocusedCaret);

            int depth = Page.Blocks[at].Depth < 1 ? 1 : Page.Blocks[at].Depth;
            var block = NotesDocOps.NewBlock(BlockKind.Image, depth);
            block.ImageBytes = bytes;
            NotesDocOps.Insert(Page.Blocks, at + NotesDocOps.SubtreeLength(Page.Blocks, at), block);

            OnDocumentMutated?.Invoke();
            Rebuild();
            SetSelectedBlock(block.Id);
            return true;
        }

        /// <summary>Puts a board into the prose, after the focused row and its children — the same placement
        /// rule an inserted picture follows, and for the same reason: a board dropped under a row with
        /// sub-rows belongs below the whole thought, not inside it.</summary>
        public bool InsertCanvasAfterFocused()
        {
            if (Page == null || string.IsNullOrEmpty(LastFocusedBlockId)) return false;

            int at = -1;
            for (int i = 0; i < Page.Blocks.Count; i++)
                if (Page.Blocks[i].Id == LastFocusedBlockId) { at = i; break; }
            if (at < 0) return false;

            PushHistory(LastFocusedBlockId, LastFocusedCaret);

            int depth = Page.Blocks[at].Depth < 1 ? 1 : Page.Blocks[at].Depth;
            var block = NotesDocOps.NewBlock(BlockKind.Canvas, depth, "Доска");
            block.CanvasObjects = new List<CanvasObjectData>();
            block.CanvasLinks = new List<LinkData>();
            NotesDocOps.Insert(Page.Blocks, at + NotesDocOps.SubtreeLength(Page.Blocks, at), block);

            OnDocumentMutated?.Invoke();
            Rebuild();
            SetSelectedBlock(block.Id);
            return true;
        }

        /// <summary>Draws a board inside its row's frame, in the reduced mode.
        ///
        /// BUILT PER REBUILD, like the row itself, and holding no state worth keeping: everything the board
        /// shows lives in the DocBlock. That is what lets Undo simply rebuild the page.
        ///
        /// THE PAGE'S HISTORY IS THE BOARD'S HISTORY. The canvas knows nothing about undo and asks whoever
        /// built it to remember the state before each change — one stack, one Ctrl+Z. See the spec's
        /// «Отмена — один стек».
        ///
        /// WHICH PAGE THAT Ctrl+Z REACHES IS NOT DECIDED BY THE MOUSE, and this paragraph used to say it was
        /// («whatever the mouse happens to be over») — true under Р4, when one pane rendered and the pointer
        /// was therefore always over the only live page. The two-panes arc replaced it: both Ctrl+Z handlers
        /// in this layer (DocKeyboardController's ordinary path and its HandleUndoOverBoard) address through
        /// PageFocusRouter, which reads EventSystem.currentSelectedGameObject and, failing that, the focused
        /// pane. Pointer position is read nowhere. So with the caret in pane 0's prose and the pointer over an
        /// inline board in pane 1, Ctrl+Z undoes pane 0.</summary>
        void BuildInlineCanvas(DocBlock block, DocBlockView view)
        {
            var interactionGO = new GameObject($"CanvasInput_{block.Id}", typeof(RectTransform));
            interactionGO.transform.SetParent(view.CanvasFrame, false);
            var interaction = interactionGO.AddComponent<CanvasInteractionController>();
            interaction.Mode = CanvasMode.Inline;
            interaction.viewportRect = view.CanvasFrame;
            interaction.builtinFont = font;

            var canvasGO = new GameObject($"Canvas_{block.Id}", typeof(RectTransform));
            canvasGO.transform.SetParent(view.CanvasFrame, false);
            var canvasController = canvasGO.AddComponent<NotesCanvasController>();
            interaction.canvasController = canvasController;
            // THE BOARD'S OWN ROW IS THE FOCUS TO RESTORE, not LastFocusedBlockId. A DM who drags a card on a
            // board they never typed in leaves that pointing at some earlier row — or at a row that no longer
            // exists — and undo would put the caret somewhere unrelated to what it just undid. The block id is
            // captured as a string because Apply replaces every DocBlock with a copy from the snapshot.
            string canvasBlockId = block.Id;
            canvasController.BeforeMutation = () => PushHistory(canvasBlockId, -1);
            canvasController.AfterMutation = () => OnDocumentMutated?.Invoke();
            canvasController.Initialize(block, view.CanvasFrame, interaction, CanvasMode.Inline);

            // BEHIND THE FRAME'S OWN CHROME. The container is created as a child of the frame at Initialize
            // time, which makes it the LAST sibling — and in uGUI the last sibling draws on top. Left alone it
            // would cover «развернуть» and the resize grip with the board's own cards.
            if (canvasController.CanvasContainer != null)
                canvasController.CanvasContainer.SetAsFirstSibling();

            // Ctrl+wheel zooms this board; a plain wheel goes on scrolling the page. On the FRAME rather than
            // on either child, because uGUI hands a scroll to the first IScrollHandler above whatever the
            // pointer is over, and the frame is the one object that is above ALL of the board — its cards and
            // its empty space alike. See CanvasWheelRelay for why the plain wheel has to be forwarded by hand.
            var wheel = view.CanvasFrame.gameObject.AddComponent<CanvasWheelRelay>();
            wheel.canvasController = canvasController;

            // Drag the board to move it, double-click it to fit it back. On the FRAME for the same reason the
            // wheel relay is: a drag that starts on a card is delivered to the CARD and never reaches here, so
            // "move the board" and "move a card" are told apart by where the press landed.
            var pan = view.CanvasFrame.gameObject.AddComponent<CanvasInlinePan>();
            pan.canvasController = canvasController;
        }

        // ── the board rows' own gestures (Р4 Task 8) ─────────────────────────────

        /// <summary>The widest a board block may be: the pane, less the margin the prose already keeps. The
        /// column's own measure does not apply — a board is the one row deliberately allowed past it.</summary>
        float MaxCanvasWidthNow()
        {
            if (viewportGO == null) return 0f;
            float available = ((RectTransform)viewportGO.transform).rect.width;
            return available > 1f ? available - MinSideMargin * 2f : 0f;
        }

        List<DocBlock> canvasResizeBefore;

        void BeginCanvasResize(string blockId)
        {
            if (Page == null) return;
            canvasResizeBefore = DocHistory.Copy(Page.Blocks);
        }

        /// <summary>ONE undo step per drag, pushed against the state the drag STARTED from — which is why the
        /// snapshot is taken at the beginning and used here, rather than pushed at either end alone.
        ///
        /// THE RESIZED ROW IS THE FOCUS TO RESTORE, not LastFocusedBlockId — the same rule the two board-
        /// mutation paths carry (BuildInlineCanvas and CanvasSurfaceHost.Show). It matters more here than it
        /// reads: RebuildAndFocus asks to SCROLL the caret into view, so undoing a resize while
        /// LastFocusedBlockId pointed at some earlier row would carry the page away from the board the DM had
        /// hold of a moment ago. Caret -1 means "end of that row", and a board row has no field to put a
        /// caret in — DocBlockView.BeginEdit returns at once — so the focus is only ever the scroll target.</summary>
        void EndCanvasResize(string blockId)
        {
            if (Page == null || canvasResizeBefore == null) return;
            PushHistoryOf(canvasResizeBefore, blockId, -1);
            canvasResizeBefore = null;
            OnDocumentMutated?.Invoke();
        }

        /// <summary>Opens a board as a surface of its own. Assigned from outside, exactly like LinkRouter, so
        /// the notes layer keeps knowing nothing about WorkspaceController — null until Task 10 wires it, and
        /// a null one simply makes «развернуть» do nothing.</summary>
        public System.Action<string> CanvasRouter;

        void ExpandCanvas(string blockId) => CanvasRouter?.Invoke(blockId);

        // ── inline links (Р2 Task 7) ─────────────────────────────────────────────
        //
        // BOTH OF THESE ARE INJECTED, and from the same place — PageLinkBridge, which WorkspaceBuilder
        // attaches beside this view. The notes layer knows nothing about PoiManager or WorkspaceController,
        // and this is the seam that keeps it that way: it deals in a list of plain WorldObjectRefs and a
        // SurfaceRef to open, both of which live in the Data half that already crosses this boundary (the
        // harness syncs Notes/Data and Workspace/Data side by side for exactly this reason).
        //
        // Null is a working state, not a broken one. Before the bridge runs — and in any scene without a
        // workspace at all — links render as their stored names and a click on one does nothing, which is
        // the same graceful degradation NavigatorView/QuickOpenPopup extend to a null document.

        /// <summary>Every world object as it is RIGHT NOW. A function rather than a list because a rename or
        /// a new POI must not need an invalidation protocol — see RefreshLinks for when it is called.</summary>
        public System.Func<IReadOnlyList<WorldObjectRef>> WorldSource;

        /// <summary>Opens a surface: (what, tab title, in the other pane). WorkspaceController.Open, reached
        /// as a delegate.</summary>
        public System.Action<SurfaceRef, string, bool> LinkRouter;

        /// <summary>The world list as of the last RefreshLinks. Cached because the resolver is asked once per
        /// token per render and collecting is a walk over every POI — but never cached ACROSS a refresh, so
        /// nothing has to remember to invalidate it.</summary>
        IReadOnlyList<WorldObjectRef> worldCache;

        /// <summary>What a [[token]] points at right now. Handed to every row, which holds it for its whole
        /// life: it reads `documentController` and `worldCache` live, so a refresh changes what it answers
        /// without any row being told anything.</summary>
        string ResolveName(string kind, string id)
            => QuickOpen.ResolverFor(documentController != null ? documentController.Document : null, worldCache)(kind, id);

        /// <summary>The same resolver, handed out so the page search renders the text it searches exactly as
        /// the rows do. A search that folded names differently from the display would report matches at
        /// offsets the highlight could not use.</summary>
        public NotesLinkOps.NameResolver ResolveNameFor => ResolveName;

        /// <summary>Whether a [[kind:id]] token's target carries a character card, so DocBlockView can draw it
        /// as a plate instead of a plain link (Task 9). Reads THE SAME documentController.Document ResolveName
        /// already reaches — but NOT through the same lookup. ResolveName goes through QuickOpen.ResolverFor
        /// (QuickOpen.cs:316-334), which for "page" hand-rolls its own doc.Groups/g.Pages walk rather than
        /// calling NotesDocOps.FindPage; CharacterOps.IsCharacterLink calls FindPage directly, because that is
        /// the existing canonical id→page lookup (already used elsewhere, e.g. MapScreenController), not
        /// because it is the one ResolveName happens to use. Either way this is the SAME document object, so
        /// the two answers about the same token can never disagree about which page they mean.</summary>
        bool IsCharacterLink(string kind, string id)
            => CharacterOps.IsCharacterLink(documentController != null ? documentController.Document : null, kind, id);

        /// <summary>Re-reads the world and re-renders every resting row, so a POI renamed in the other pane
        /// changes the prose here with no edit to the page — D1's whole promise.
        ///
        /// Refresh(), NOT Rebuild(): a rebuild destroys every row, and the DM may well be typing in one of
        /// them while the rename lands in the pane next door. Refresh leaves a row that is being edited
        /// exactly as it is (see DocBlockView.Refresh) and re-renders the rest.</summary>
        public void RefreshLinks()
        {
            worldCache = WorldSource != null ? WorldSource() : null;
            foreach (var row in rows)
                if (row != null) row.Refresh();
            // «Итоги» is a list of NAMES, so it goes stale on exactly the events this method exists for — a
            // POI renamed or deleted in the other pane. Refreshing the rows and not the summary would leave
            // the page telling the DM two different things about the same object. Safe to rebuild here for
            // the same reason the rows are not: the footer holds no editing state to interrupt.
            if (footer != null) footer.Refresh();
        }

        /// <summary>Where a clicked link goes. The rules, in one place:
        ///
        /// A PAGE OPENS IN THE OTHER PANE — Р1's rule, unchanged: a page is opened explicitly and never on
        /// top of what the DM is reading.
        ///
        /// A POI OPENS ITS EDITOR, per the Task 10c checkpoint ruling that a point of interest IS its editor,
        /// through the one WorldSurface.PoiEditor factory the navigator, Ctrl+K and the map all share — so
        /// this focuses the same tab they do rather than opening a near-identical second one. ALSO in the
        /// other pane, which the plan left to this task: the DM clicked a name inside a sentence they are
        /// reading, and replacing that sentence with the editor is the one outcome they cannot have meant.
        /// Say so at the next checkpoint if it should differ.
        ///
        /// BUILDING AND ROOM cannot be produced yet — nothing emits a token for them, because they are not
        /// addressable by a bare id (WorldSurface's own doc) — so they are logged and ignored rather than
        /// guessed at.</summary>
        public void RouteLink(string kind, string id)
        {
            if (LinkRouter == null || string.IsNullOrEmpty(id)) return;

            if (kind == NotesLinkOps.KindPage)
            {
                string title = PageNameOf(id);
                if (title == null) return;   // the page is gone; the row shows muted prose, not a dead link
                LinkRouter(new SurfaceRef { Kind = SurfaceKind.Page, Id = id }, title, true);
                return;
            }

            if (kind == NotesLinkOps.KindOf(WorldRefKind.Poi))
            {
                LinkRouter(WorldSurface.PoiEditor(id), ResolveName(kind, id) ?? "", true);
                return;
            }

            Debug.Log($"DocumentPageView: a [[{kind}:{id}]] link has no surface to open yet — " +
                      "building and room become addressable in Р3.");
        }

        // ── what the footer reads (Р2 Task 10) ───────────────────────────────────
        //
        // Three thin accessors rather than handing PageFooterView the controller: the footer is a list of
        // labels and a click, and giving it the document would let it grow opinions about what a page
        // references. CollectReferences and FindBacklinks already are the answer — see their docs.

        /// <summary>Everything this page references, resolved as of the CURRENT world. Never stored: the
        /// summary is a view of the prose, not a second copy of it that could disagree with it.</summary>
        public List<NotesDocOps.PageReference> PageReferences()
            => NotesDocOps.CollectReferences(Page, ResolveName);

        /// <summary>Every place this page is mentioned from.</summary>
        public List<Backlink> PageBacklinks()
            => documentController != null && Page != null
                ? NotesDocOps.FindBacklinks(documentController.Document, Page.Id)
                : new List<Backlink>();

        /// <summary>What to call a referenced object's kind, in the DM's words.
        ///
        /// A world object answers with its OWN label — «город», «деревня» — because that is what the
        /// navigator and Ctrl+K already show for it, and «точка» would be a third, vaguer name for the same
        /// thing in a third place. The generic word is the fallback for an object the world list does not
        /// have (a page, or a kind nothing produces yet), never the first choice.
        ///
        /// Reads `worldCache` rather than calling WorldSource: Rebuild refreshed it on the very pass that is
        /// about to draw this row, so a second collection would be the same list, more slowly.</summary>
        public string KindLabelFor(string kind, string id)
        {
            if (kind == NotesLinkOps.KindPage) return "страница";

            if (worldCache != null && !string.IsNullOrEmpty(id))
                foreach (var obj in worldCache)
                    if (obj != null && obj.Id == id && !string.IsNullOrEmpty(obj.KindLabel)
                        && NotesLinkOps.KindOf(obj.Kind) == kind)
                        return obj.KindLabel;

            if (kind == NotesLinkOps.KindOf(WorldRefKind.Poi)) return "точка";
            if (kind == NotesLinkOps.KindOf(WorldRefKind.Building)) return "здание";
            if (kind == NotesLinkOps.KindOf(WorldRefKind.Room)) return "комната";
            return "";
        }

        /// <summary>The font the page was built with, read live rather than copied into the footer: one
        /// namer of the asset (NotesRootBuilder.BuiltinFont, threaded through PageSurfaceHost into
        /// Initialize), so a footer cannot end up drawing with a different one — or, holding a stale copy
        /// of its own, with none at all.</summary>
        public Font BodyFont => font;

        string PageNameOf(string pageId)
        {
            var doc = documentController != null ? documentController.Document : null;
            if (doc == null) return null;
            foreach (var group in doc.Groups)
                foreach (var page in group.Pages)
                    if (page.Id == pageId) return page.Name;
            return null;
        }

        public void Initialize(NotesDocumentController docController, RectTransform parent, Font builtinFont)
        {
            documentController = docController;
            font = builtinFont;

            root = new GameObject("DocumentViewport", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            ThemeService.Tag(root.AddComponent<Image>(), ThemeRole.Bg);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // ZERO, and not because the wheel is unwanted — because PageWheelRelay below now owns it. The
            // ScrollRect receives the same scroll event no matter what (see that class's doc: an event goes to
            // every handler on the object), and a sensitivity of zero is the only way to stop it acting on the
            // wheel a second time, in its own instant, unsmoothed way, on top of ours.
            scroll.scrollSensitivity = 0f;
            root.AddComponent<PageWheelRelay>().page = this;

            viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(root.transform, false);
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            // Leave room at the top for the floating toolbar. Task 9 no longer builds that toolbar (see the
            // class doc), so this was a dead strip until Task 10f's «+ Раздел» bar (BuildAddSectionBar,
            // below) claimed it — the Critical review finding that an empty session page had no way to
            // create its first block. Reserved here, built there, off the SAME AddSectionBarHeight constant,
            // so the two cannot silently drift apart the way two independent literals could.
            // The right edge is given to the scrollbar (BuildVerticalScrollbar, below) rather than shared with
            // it. An overlaying bar would sit on top of the last few characters of every line, and — less
            // visibly but worse — ApplyColumnMeasure centres the prose column inside THIS rect, so a viewport
            // that still claimed the bar's width would centre the column a few pixels left of true.
            viewportRect.offsetMin = new Vector2(0f, 0f);
            viewportRect.offsetMax = new Vector2(-ScrollbarWidth, -AddSectionBarHeight);
            viewportGO.AddComponent<RectMask2D>();
            scroll.viewport = viewportRect;

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);
            scroll.content = content;

            var vLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childControlHeight = true;
            // MUST stay false: forcing expand height makes the group hand every row an equal share of the
            // leftover space, which turns a one-line row into a tall block.
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 2f;
            vLayout.padding = new RectOffset(8, 8, 8, 24);

            // A ContentSizeFitter is correct HERE — this content's height is not controlled by any parent
            // layout group, it is the scroll body. The trap is putting one on a row inside the group below.
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // What the pane shows when its tab names a page the document does not hold — a sibling of
            // Viewport, not a child of it, so it is never subject to the ScrollRect/mask/content-fitter
            // machinery above. See ApplyVisibility for when it comes on, and the class doc for the board
            // page it was originally built for.
            //
            // THE WORDING IS FOR A GAME MASTER, NOT A PROGRAMMER. It states the fact («this tab points at a
            // page that is not here») and then the overwhelmingly likely cause, because the one way to reach
            // this in practice is a workspace restored from the previous session before the project holding
            // those pages has been opened. No id, no kind, no «surface» — none of that would tell the person
            // reading it what to DO, and «открыть проект» does.
            placeholderGO = new GameObject("BoardPlaceholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(root.transform, false);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<Text>();
            placeholderText.text = "Страницы, на которую смотрит эта вкладка, здесь нет.\n" +
                                   "Скорее всего, ещё не открыт проект, в котором она лежит.";
            placeholderText.font = font;
            placeholderText.fontSize = 14;
            ThemeService.Tag(placeholderText, ThemeRole.Mut);
            placeholderText.alignment = TextAnchor.MiddleCenter;
            placeholderGO.SetActive(false);

            // «+ Раздел» — Task 10f review Critical: CreateSessionSheet stopped seeding a Section
            // (the DM's own ruling — sections are theirs to make), but I2 still requires a non-empty
            // page's FIRST block to be one, and until now nothing could put one there. This lives in the
            // 44px strip viewportRect reserves above (see offsetMax there) rather than opening new screen
            // real estate. Deliberately ONE button, not the «Вставка»/«Ссылка» editor toolbar the DM has
            // separately asked for — that one is Р2 per the umbrella spec.
            BuildVerticalScrollbar(root.transform, scroll);

            addSectionBarGO = BuildAddSectionBar(root.transform);
            // The editing toolbar shares that strip, anchored to its right edge — one band of chrome above
            // the prose rather than two.
            Toolbar = PageToolbar.Attach((RectTransform)addSectionBarGO.transform, this, font);
            // ABOVE the rows, inside the scrolling column — see CharacterHeaderView's own doc for why it
            // is a child of Content rather than another fixed strip, and why it is attached once here
            // rather than rebuilt on every Rebuild the way the footer below is.
            header = CharacterHeaderView.Attach(content, this);
            // Under the rows, inside the scrolling column — see PageFooterView's own doc for why it is a
            // child of Content rather than another fixed strip.
            footer = PageFooterView.Attach(content, this);
            // On `root`, which is where a drop that landed on any row is delivered — see LinkDropTarget's
            // own doc. Needs nothing from the workspace, so it is wired here rather than through the bridge.
            LinkDropTarget.Attach(this);
            searchBar = PageSearchBar.Attach(this, font);

            // Structural document changes only — creating, renaming or deleting a group or a page, never a
            // keystroke (NotesDocumentController raises this from its CRUD alone). A [[page:…]] link renders
            // the target page's CURRENT name, so a rename in the navigator has to reach the prose here, and
            // this is the event that carries it.
            documentController.OnDocumentChanged += RefreshLinks;
            // Born unbound and hidden: `root` is created ACTIVE by the construction above, and nothing has
            // named a page yet (PageSurfaceHost.Show does that, on the first sync). Without this the freshly
            // built, opaque viewport would paint over whatever the pane really shows until then.
            ApplyVisibility();
        }

        /// <summary>Builds the page's vertical scrollbar, at the DM's first-checkpoint request: the page
        /// scrolled with the wheel but showed nothing about WHERE in the page it was, which on a session
        /// sheet a hundred blocks long is most of what a scrollbar is for.
        ///
        /// Hand-built rather than taken from a prefab because this whole shell is runtime-constructed, and
        /// laid out in the standard uGUI three-part shape the Scrollbar component expects — bar ▸ sliding
        /// area ▸ handle — because Scrollbar.UpdateVisuals drives the HANDLE's anchors inside its parent and
        /// needs that parent to be a rect it can treat as the full travel.
        ///
        /// VISIBILITY IS AutoHide AND MUST NOT BE AutoHideAndExpandViewport. The latter makes the ScrollRect
        /// drive the viewport's own sizeDelta, and this viewport has a hand-set offsetMax carving out both the
        /// «+ Раздел» strip and this bar's width — the two would fight every frame, one of them winning by
        /// frame order. AutoHide only toggles the bar's GameObject, leaving viewport geometry alone, so a page
        /// too short to scroll simply has no bar and the prose column does not shift when one appears.</summary>
        void BuildVerticalScrollbar(Transform parent, ScrollRect scroll)
        {
            var barGO = new GameObject("VScrollbar", typeof(RectTransform));
            barGO.transform.SetParent(parent, false);
            var barRect = (RectTransform)barGO.transform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            // Spans the full height MINUS the «+ Раздел» strip, so it lines up with the viewport it scrolls
            // rather than running underneath a bar it has nothing to do with.
            barRect.offsetMin = new Vector2(-ScrollbarWidth, 0f);
            barRect.offsetMax = new Vector2(0f, -AddSectionBarHeight);
            ThemeService.Tag(barGO.AddComponent<Image>(), ThemeRole.Panel2);

            var bar = barGO.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var slidingGO = new GameObject("SlidingArea", typeof(RectTransform));
            slidingGO.transform.SetParent(barGO.transform, false);
            var slidingRect = (RectTransform)slidingGO.transform;
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = Vector2.zero;
            slidingRect.offsetMax = Vector2.zero;

            var handleGO = new GameObject("Handle", typeof(RectTransform));
            handleGO.transform.SetParent(slidingGO.transform, false);
            var handleRect = (RectTransform)handleGO.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handleImage = handleGO.AddComponent<Image>();
            ThemeService.Tag(handleImage, ThemeRole.Border);

            bar.targetGraphic = handleImage;
            bar.handleRect = handleRect;

            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// <summary>Builds the «+ Раздел» bar — see its call site in Initialize for why it exists. Sits in
        /// the strip `viewportGO`'s own offsetMax already carves out (AddSectionBarHeight), as a SIBLING of
        /// viewportGO under `root`, not a child of Content — it must stay fixed at the top of the page, not
        /// scroll away with the rows underneath it.</summary>
        GameObject BuildAddSectionBar(Transform parent)
        {
            var barGO = new GameObject("AddSectionBar", typeof(RectTransform));
            barGO.transform.SetParent(parent, false);
            var barRect = (RectTransform)barGO.transform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, AddSectionBarHeight);
            ThemeService.Tag(barGO.AddComponent<Image>(), ThemeRole.Panel2);

            var btnGO = new GameObject("Button", typeof(RectTransform));
            btnGO.transform.SetParent(barGO.transform, false);
            var btnRect = (RectTransform)btnGO.transform;
            btnRect.anchorMin = new Vector2(0f, 0.5f);
            btnRect.anchorMax = new Vector2(0f, 0.5f);
            btnRect.pivot = new Vector2(0f, 0.5f);
            btnRect.anchoredPosition = new Vector2(8f, 0f);
            btnRect.sizeDelta = new Vector2(112f, 30f);

            var btnImg = btnGO.AddComponent<Image>();
            ThemeService.Tag(btnImg, ThemeRole.AccentSoft);
            var addSectionButton = btnGO.AddComponent<Button>();
            addSectionButton.targetGraphic = btnImg;
            addSectionButton.onClick.AddListener(AddSection);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(btnGO.transform, false);
            var labelRect = (RectTransform)labelGO.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGO.AddComponent<Text>();
            label.text = "+ Раздел";
            label.font = font;
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            ThemeService.Tag(label, ThemeRole.AccentInk);

            return barGO;
        }

        /// <summary>Creates a new top-level Section at the END of the page and focuses it for immediate
        /// typing — the only affordance a session page needs to become usable at all, now that
        /// CreateSessionSheet no longer seeds one (NotesDocOps.CreateSessionSheet's own doc; Task 10f review
        /// Critical). Routed through the SAME NotesDocOps.NewBlock/Insert primitives DocKeyboardOps.OnEnter
        /// uses to create every other block, and the same RebuildAndFocus -> DocBlockView.FocusAt ->
        /// InputField.ActivateInputField path already used to focus a block Enter just created (and the
        /// idiom NavigatorView.StartRename uses for a freshly created name) — no second block-creation or
        /// focus mechanism. Appends rather than inserting at the caret, so the button's meaning does not
        /// depend on what row (if any) happens to be focused — "add a new top-level section", full stop.</summary>
        public void AddSection()
        {
            if (Page == null) return;
            PushHistory(LastFocusedBlockId, LastFocusedCaret);
            var section = NotesDocOps.NewBlock(BlockKind.Section, 0);
            NotesDocOps.Insert(Page.Blocks, Page.Blocks.Count, section);
            RebuildAndFocus(section.Id, 0);
        }

        void OnDestroy()
        {
            if (documentController != null)
                documentController.OnDocumentChanged -= RefreshLinks;
        }

        public DocBlockView ViewOf(string blockId)
        {
            foreach (var row in rows)
                if (row != null && row.BlockId == blockId) return row;
            return null;
        }

        public void SetCollapsedAll(bool collapsed)
        {
            if (Page == null) return;
            foreach (var b in Page.Blocks)
                if (b.Kind == BlockKind.Section) b.Collapsed = collapsed;
            Rebuild();
        }

        /// <summary>Rebuilds every row from the page's currently VISIBLE blocks. Cheap enough at the ~100
        /// blocks a session sheet reaches, which is why no virtualization exists — see the spec's known limits.</summary>
        public void Rebuild()
        {
            if (Page == null || content == null) return;

            // WHERE THE DM WAS READING, remembered across the rebuild. See SettleHeights for the rest of the
            // «после Enter страницу перелистывает наверх» defect; this is only the half that remembers.
            float keepY = content.anchoredPosition.y;

            foreach (var row in rows)
                if (row != null)
                {
                    // OUT of the layout group NOW, not merely marked for destruction. Destroy is deferred to
                    // the end of the frame, so a row that is only destroyed is still a child of `content`
                    // with a live LayoutElement — and SettleHeights below forces a layout THIS frame, which
                    // would then measure the old rows and the new ones stacked together.
                    row.transform.SetParent(null, false);
                    Destroy(row.gameObject);
                }
            rows.Clear();

            // The world as of THIS rebuild, so every row on the page resolves its links against one list
            // rather than each row collecting its own — and so a page opened after a rename never renders the
            // old name even once.
            worldCache = WorldSource != null ? WorldSource() : null;

            var visible = NotesDocOps.VisibleIndices(Page.Blocks);
            foreach (int index in visible)
            {
                var block = Page.Blocks[index];
                var rowGO = new GameObject($"Row_{block.Kind}", typeof(RectTransform));
                var view = rowGO.AddComponent<DocBlockView>();
                view.Initialize(block, content, font, ResolveName, IsCharacterLink);
                view.OnToggleCollapse += OnToggleCollapse;
                view.OnLinkActivated += RouteLink;
                view.OnSelectRequested += SetSelectedBlock;
                view.OnBeforeFirstEdit += PushHistoryBeforeFirstEdit;
                view.OnExpandCanvasRequested += ExpandCanvas;
                view.OnCanvasResizeBegan += BeginCanvasResize;
                view.OnCanvasResizeEnded += EndCanvasResize;
                // ASSIGNED AFTER Initialize AND THEREFORE RE-APPLIED. Initialize builds the board's frame and
                // sizes it, and at that moment the row does not yet know how wide the pane is — so a board
                // stored wider than the pane it is reopened in would draw unclamped. Harmless on every other
                // kind of row: ApplyCanvasFrameSize returns at once when there is no frame.
                view.MaxCanvasWidth = MaxCanvasWidthNow();
                view.ApplyCanvasFrameSize();
                view.SetSelected(block.Id == SelectedBlockId);
                view.OnTextChanged += _ => OnDocumentMutated?.Invoke();
                // THE WHEEL STOPS AT AN EDITING ROW WITHOUT THIS. A scroll event travels up only as far as
                // the first object carrying ANY scroll handler, and TMP_InputField is one — it takes the
                // wheel to scroll text within itself, and forwards to a parent only when it is single-line
                // (TMP_InputField.cs:2414-2423). So the page never heard about a wheel turned over the row the
                // DM was writing in. Every handler on the SAME object still runs, which is what makes adding
                // one here work: the field does its nothing, and the page scrolls. Wired from here rather
                // than from inside the row, so a row still knows nothing about the page it sits on.
                if (view.Field != null) view.Field.gameObject.AddComponent<PageWheelRelay>().page = this;
                if (block.Kind == BlockKind.Canvas && view.CanvasFrame != null) BuildInlineCanvas(block, view);
                view.Refresh();
                rows.Add(view);
            }

            // AFTER the rows and BEFORE the heights settle — same placement as the footer just below, and
            // for the same reason on the other end: Show() puts the header back at the START of the
            // column (SetAsFirstSibling), which only matters once the rows above have been (re)created as
            // its later siblings. Absent, not merely empty, exactly like the footer: an ordinary page must
            // not carry a zero-height ghost of this object.
            if (header != null)
            {
                if (CharacterOps.IsCharacter(Page)) header.Show(Page);
                else header.Hide();
            }

            // AFTER the rows and BEFORE the heights settle. After, because Refresh puts the footer back at
            // the end of the column — the rows above were just created and are therefore its later siblings.
            // Before, because SettleHeights is what turns every LayoutElement.preferredHeight on this column
            // into the column's own height, and the footer's is one of them.
            if (footer != null) footer.Refresh();
            // The rows the search had painted were just destroyed and remade, and the block list may have
            // changed under the query too — see PageSearchBar.Reapply.
            if (searchBar != null) searchBar.Reapply();

            SettleHeights();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, keepY);
        }

        /// <summary>Gives every freshly built row its real height BEFORE this frame ends.
        ///
        /// THE DEFECT THIS FIXES, because the mechanism is not obvious from the symptom. A new row starts at
        /// MinRowHeight (22px) and learns its true height in DocBlockView's own LateUpdate. So for one frame
        /// after every rebuild, a page of forty rows was ~880px tall instead of several thousand — and a
        /// ScrollRect in Clamped movement will not let content sit scrolled past its own end, so it dragged
        /// the page back to the top. Restoring the remembered offset afterwards would not have helped either:
        /// the collapse happened a frame AFTER the restore. The heights have to be right within the same
        /// frame, which is what this does.
        ///
        /// TWO PASSES, and neither is redundant. A row cannot say how TALL it is until it knows how WIDE it
        /// is, and its width comes from the layout group; so the first pass hands out widths, ApplyHeightNow
        /// then measures each row's text against the width it just received, and the second pass turns those
        /// heights into the column's own height through the ContentSizeFitter. ApplyColumnMeasure runs first
        /// because the prose column's side padding is part of that width, and it would otherwise be applied
        /// one LateUpdate later — measuring every row against a width that is about to change.</summary>
        void SettleHeights()
        {
            if (content == null) return;
            // Not laid out yet — the very first Rebuild runs from Initialize, before this column has a width
            // at all. Measuring text against a zero-wide box makes TMP wrap it one character per line and
            // report an absurd height, so the rows are left at MinRowHeight and DocBlockView's own LateUpdate
            // sizes them a frame later, exactly as it did before this method existed. Nothing is lost: a page
            // being built for the first time has no scroll position to protect.
            if (content.rect.width <= 1f) return;
            ApplyColumnMeasure();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            // The header first, same reason as every row: it cannot say how tall its four fields are
            // until it knows how wide the column is, which the pass just above is what hands out.
            if (header != null) header.ApplyHeightNow();
            foreach (var row in rows)
                if (row != null) row.ApplyHeightNow();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        void OnToggleCollapse(string blockId)
        {
            if (Page == null) return;
            var block = Page.Blocks.Find(b => b.Id == blockId);
            if (block == null) return;
            block.Collapsed = !block.Collapsed;
            Rebuild();
            OnDocumentMutated?.Invoke();
        }

        void LateUpdate()
        {
            ApplyColumnMeasure();
            // Order is deliberate: the wheel glides toward wherever it was aimed, and THEN the caret gets its
            // say. A reveal cancels the glide outright (see RevealCaretIfMoved's write), which is right — the
            // DM has started typing somewhere, and finishing a scroll they began a moment earlier would carry
            // them away from it.
            AnimateScroll();
            RevealCaretIfMoved();
        }

        // Where the wheel is aiming. Invalid means "nothing is gliding" — not "aiming at zero".
        float scrollTarget;
        bool scrollTargetValid;
        // The last value THIS class wrote, so a position that changed by any other means (a drag, the
        // scrollbar handle, the ScrollRect's own clamp after the page got shorter) is recognised and the glide
        // steps aside instead of yanking the page back to a target nobody is aiming at any more.
        float lastScrollWritten;

        /// <summary>How far one wheel notch travels, in pixels. The ScrollRect's own sensitivity was 24 and
        /// moved the page in visible jerks of about a line and a half; the DM asked for faster AND smoother,
        /// which are not the same request — this is the faster half.</summary>
        const float WheelStep = 100f;

        /// <summary>How quickly the page catches up with the wheel, per second, as an exponential rate. ~18
        /// settles in about a fifth of a second: fast enough that the page never feels like it is lagging
        /// behind the hand, slow enough that the eye can follow the text past rather than losing its place.
        /// This is the smoother half.</summary>
        const float ScrollSmoothing = 18f;

        /// <summary>A wheel notch, relayed by PageWheelRelay. Aims the glide; it does not move anything
        /// itself.
        ///
        /// AIMED FROM THE EXISTING TARGET, not from where the page happens to be at this instant — that is
        /// what makes several quick notches ADD UP into one long travel instead of each one restarting the
        /// same short hop from wherever the previous one had got to.</summary>
        public void WheelScroll(float wheelDeltaY)
        {
            if (content == null || viewportGO == null) return;
            if (Mathf.Abs(wheelDeltaY) < 0.01f) return;

            float travel = Mathf.Max(0f, content.rect.height - ((RectTransform)viewportGO.transform).rect.height);
            float from = scrollTargetValid ? scrollTarget : content.anchoredPosition.y;

            // The sign mirrors ScrollRect.OnScroll, which flips scrollDelta.y before applying it: a wheel
            // pushed away from the DM gives a POSITIVE delta and must move the page towards its start, which
            // is a SMALLER anchoredPosition.y on a top-pivoted content.
            scrollTarget = Mathf.Clamp(from - wheelDeltaY * WheelStep, 0f, travel);
            scrollTargetValid = true;
            lastScrollWritten = content.anchoredPosition.y;
        }

        /// <summary>Advances the glide by one frame. Exponential rather than linear, so the page starts
        /// immediately and eases into its stop instead of arriving at full speed; unscaled time, because the
        /// project pauses Time.timeScale during a project load and a scroll frozen mid-glide would look like
        /// the window had hung.</summary>
        void AnimateScroll()
        {
            if (!scrollTargetValid || content == null) return;

            float current = content.anchoredPosition.y;
            // Moved by something that is not us — a drag, the scrollbar, a rebuild's clamp. Whoever did that
            // is the one the DM is watching; drop the glide rather than compete with it.
            if (Mathf.Abs(current - lastScrollWritten) > 0.5f) { scrollTargetValid = false; return; }

            float next = Mathf.Lerp(current, scrollTarget, 1f - Mathf.Exp(-ScrollSmoothing * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - scrollTarget) < 0.5f) { next = scrollTarget; scrollTargetValid = false; }

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, next);
            lastScrollWritten = next;
        }

        // What RevealCaretIfMoved saw last time, so it can tell "the caret moved" from "the DM scrolled".
        string revealedRowId;
        int revealedCaret = -1;
        // Set by RebuildAndFocus: after a rebuild the caret can land on the same index of the same block id
        // it had before (a merge, a collapse), which would look like "nothing moved" — but the row it lives
        // in is a different object at a different height, so the page must reveal it anyway.
        bool revealRequested;

        /// <summary>Keeps the caret on screen while the DM types — «ты всегда должен видеть то, что
        /// печатаешь».
        ///
        /// ONLY WHEN THE CARET MOVED, which is what stops this from fighting the DM's own scrolling. Reading
        /// a page with the caret parked somewhere off screen is an ordinary thing to do, and a reveal that
        /// fired every frame would yank the page back the moment they let go of the wheel. Typing always
        /// changes the caret INDEX — a character inserted, a character deleted, an arrow pressed — so the
        /// index is a complete signal for "the DM is doing something at this position" and scrolling is not
        /// part of it.
        ///
        /// The caret's LINE is what gets revealed, not the row: see DocBlockView.TryGetCaretLineWorldY. That
        /// method also answers false while the caret is still pending, so the frames between FocusAt and the
        /// caret actually being placed reveal nothing — otherwise a rebuild would scroll to wherever the
        /// field's leftover position happened to point. `revealRequested` survives those frames precisely so
        /// the reveal still happens once the answer becomes truthful.</summary>
        void RevealCaretIfMoved()
        {
            if (content == null || viewportGO == null) return;

            DocBlockView editing = null;
            foreach (var row in rows)
                if (row != null && row.Field != null && row.Field.isFocused) { editing = row; break; }

            if (editing == null)
            {
                revealedRowId = null;
                revealedCaret = -1;
                return;
            }

            int caret = editing.Field.caretPosition;
            bool moved = revealRequested || editing.BlockId != revealedRowId || caret != revealedCaret;
            if (!moved) return;

            if (!editing.TryGetCaretLineWorldY(out float caretTop, out float caretBottom)) return;

            revealedRowId = editing.BlockId;
            revealedCaret = caret;
            revealRequested = false;

            var viewRect = (RectTransform)viewportGO.transform;
            viewRect.GetWorldCorners(viewportCorners);
            float viewBottom = viewportCorners[0].y;
            float viewTop = viewportCorners[1].y;

            // World units are canvas units times the canvas scale, so the margin has to be scaled too or it
            // would mean something different on every display resolution.
            float scale = content.lossyScale.y;
            if (scale <= 0.0001f) return;
            float margin = RevealMargin * scale;

            // Positive shift moves the content UP in the world, which is what reveals something BELOW the
            // window. The below case is tested first: on a row taller than the window both tests can pass at
            // once, and the DM typing at its bottom is the case that matters.
            float shift = 0f;
            if (caretBottom < viewBottom + margin) shift = viewBottom + margin - caretBottom;
            else if (caretTop > viewTop - margin) shift = viewTop - margin - caretTop;
            if (Mathf.Abs(shift) < 0.5f) return;

            // Written straight onto anchoredPosition rather than through verticalNormalizedPosition, which
            // divides by (content height − viewport height) and loses all precision exactly when a page is
            // barely taller than its window. The ScrollRect's own Clamped movement corrects any overshoot.
            content.anchoredPosition = new Vector2(content.anchoredPosition.x,
                                                   content.anchoredPosition.y + shift / scale);
            // A wheel glide in flight is abandoned here rather than re-aimed: the DM has just typed
            // somewhere, and carrying on to a target they chose a moment BEFORE that would take them straight
            // back off the line they are writing on. Without this the two would also fight — AnimateScroll
            // would see a position it did not write and stand down anyway, one frame later and less clearly.
            scrollTargetValid = false;
        }

        /// <summary>Scrolls a whole ROW into view — what a search hit needs, where RevealCaretIfMoved answers
        /// for a caret in a row being edited. Separate because the trigger is different in kind: that one
        /// fires every frame and must decide for itself whether anything moved, while this is a single
        /// deliberate jump, asked for once.
        ///
        /// Does nothing when the row is already on screen, so stepping between two matches in the same
        /// paragraph does not jerk the page for each one.
        ///
        /// The row must EXIST as a view — a hit inside a collapsed section has no rect to scroll to, which is
        /// why PageSearchBar.Reveal opens the section and rebuilds before calling this.</summary>
        public void RevealRow(string blockId)
        {
            var view = ViewOf(blockId);
            if (view == null || content == null || viewportGO == null) return;

            ((RectTransform)view.transform).GetWorldCorners(viewportCorners);
            float rowBottom = viewportCorners[0].y;
            float rowTop = viewportCorners[1].y;

            ((RectTransform)viewportGO.transform).GetWorldCorners(viewportCorners);
            float viewBottom = viewportCorners[0].y;
            float viewTop = viewportCorners[1].y;

            float scale = content.lossyScale.y;
            if (scale <= 0.0001f) return;
            float margin = RevealMargin * scale;

            // Below the window is tested first, for the same reason RevealCaretIfMoved does it: a row taller
            // than the viewport satisfies both tests at once, and its top is the part worth showing.
            float shift = 0f;
            if (rowTop > viewTop - margin) shift = viewTop - margin - rowTop;
            else if (rowBottom < viewBottom + margin) shift = viewBottom + margin - rowBottom;
            if (Mathf.Abs(shift) < 0.5f) return;

            content.anchoredPosition = new Vector2(content.anchoredPosition.x,
                                                   content.anchoredPosition.y + shift / scale);
            // A wheel glide in flight would carry the page straight back off the match — the DM asked to be
            // taken somewhere, and that request is newer than the wheel's.
            scrollTargetValid = false;
        }

        /// <summary>How much clear space to leave between the caret's line and the edge it was approaching, so
        /// the line the DM is typing does not sit flush against the window's rim.</summary>
        const float RevealMargin = 24f;

        static readonly Vector3[] viewportCorners = new Vector3[4];

        /// <summary>THE MEASURE: prose never gets wider than ~34em, however wide the pane is.
        ///
        /// This is the single most consequential typographic rule on the page and the one a pane split makes
        /// easy to lose. A line the full width of a maximised window is a line the eye cannot reliably return
        /// from — it lands on the wrong next line — which is why long-form text has been set at roughly 70
        /// characters since long before screens. The rejected first editor read badly for several reasons and
        /// this was one of them.
        ///
        /// Expressed as the row group's PADDING rather than as a narrower Content rect on purpose: Content is
        /// the ScrollRect's content and must keep spanning the viewport, or the scrollbar geometry and the
        /// mask stop agreeing with it. Padding leaves that structure alone and moves only the rows.
        ///
        /// When the pane is NARROWER than the measure the rule does nothing but keep MinSideMargin, so a
        /// half-width pane behaves exactly as it did before the cap existed.</summary>
        void ApplyColumnMeasure()
        {
            if (content == null || viewportGO == null) return;
            if (rowLayout == null) rowLayout = content.GetComponent<VerticalLayoutGroup>();
            if (rowLayout == null) return;

            float available = ((RectTransform)viewportGO.transform).rect.width;
            if (available <= 1f) return;   // not laid out yet — measuring into a zero-wide box proves nothing

            float column = Mathf.Min(available - MinSideMargin * 2f, NotesTypography.MeasureWidthPx);
            int side = Mathf.RoundToInt(Mathf.Max(MinSideMargin, (available - column) * 0.5f));

            if (Mathf.Abs(side - appliedSidePadding) < 0.5f) return;
            appliedSidePadding = side;
            rowLayout.padding.left = side;
            rowLayout.padding.right = side;
            LayoutRebuilder.MarkLayoutForRebuild(content);
        }

        /// <summary>Rebuilds and then puts the caret where a keyboard operation asked for it. Rebuild throws
        /// away every row object, so the focus request has to be re-issued against the NEW view rather than
        /// the one the keystroke started in.</summary>
        public void RebuildAndFocus(string blockId, int caretOffset)
        {
            Rebuild();
            var view = ViewOf(blockId);
            if (view != null) view.FocusAt(caretOffset);
            // The row this focuses may be off screen — a new block created below the fold, or the row a merge
            // pulled the caret up into. Rebuild has already restored the DM's scroll position; this asks for
            // it to be adjusted, once, as soon as the caret is real. See RevealCaretIfMoved.
            revealRequested = true;
            OnDocumentMutated?.Invoke();
        }
    }
}
