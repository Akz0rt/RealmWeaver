using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Every operation on a document page, as pure functions over the flat block list. Deliberately free of
    /// any UnityEngine reference so the whole layer runs in Tools/notes-harness without an Editor — the same
    /// arrangement InteriorOps / BrushOps / SettlementFence use.
    ///
    /// Nesting lives in DocBlock.Depth, not in a tree of children. Every structural operation is expressed
    /// through SubtreeLength, so "a block moves with its children" is implemented in exactly one place.
    ///
    /// Invariants the layer maintains (Validate reports violations, Normalize repairs what it can):
    ///   I1  Section => Depth 0; every other kind => Depth >= 1.
    ///   I2  Depth[n] &lt;= Depth[n-1] + 1, and a non-empty page starts with a Section.
    ///   I3  a non-empty LinkedPageId always resolves to a page that exists.
    ///   I4  a block never links to the page that contains it.
    ///   I5  at most one group carries IsReference.
    ///   I6  block ids are unique across the whole document.
    ///   I7  only Item takes children; Detail only on Item; ImageBytes/DisplayHeight only on Image;
    ///       LinkedPageId only on Item (optional) or BoardRef (required).
    ///   I8  a BoardRef's target page has Kind == Board.
    /// </summary>
    public static class NotesDocOps
    {
        public const string ReferenceGroupTitle = "Справочник";

        // ── Creation ───────────────────────────────────────────────────────────

        public static DocBlock NewBlock(BlockKind kind, int depth, string text = "")
            => new DocBlock { Kind = kind, Depth = depth, Text = text ?? "" };

        /// <summary>A blank session page: PageKind.Document, no blocks. Before Task 10f this seeded an
        /// eight-section Lazy-DM scaffold (title/hint pairs, a prose row under «Сильное начало») — a DM
        /// ruling given after running a real session reversed that: sections are the DM's to make, not the
        /// tool's to pre-fill («Страница сессии не должна быть автоматически разбита на разделы, это все то,
        /// что делает сам пользователь»). BlockKind.Section itself is untouched; only the auto-seeding of it
        /// is gone, which is why an empty NotesPage{Kind=Document} is exactly right here — the same shape an
        /// ordinary new page already has.
        ///
        /// NO MIGRATION: a page saved before this change keeps whatever sections it already has. Once typed,
        /// those sections are ordinary user-owned blocks — indistinguishable from ones the DM made by hand —
        /// and stripping them on load would destroy real session content for a rule that only governs what a
        /// NEW page starts with.</summary>
        public static NotesPage CreateSessionSheet(string name)
            => new NotesPage { Name = name, Kind = PageKind.Document };

        // ── Lookup ─────────────────────────────────────────────────────────────

        public static NotesPage FindPage(NotesDocument doc, string pageId)
        {
            if (doc == null || string.IsNullOrEmpty(pageId)) return null;
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Id == pageId) return p;
            return null;
        }

        public static DocBlock FindBlock(NotesDocument doc, string blockId, out NotesPage owner)
        {
            owner = null;
            if (doc == null || string.IsNullOrEmpty(blockId)) return null;
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    foreach (var b in p.Blocks)
                        if (b.Id == blockId) { owner = p; return b; }
            return null;
        }

        static int IndexOf(IReadOnlyList<DocBlock> blocks, string blockId)
        {
            if (blocks == null || string.IsNullOrEmpty(blockId)) return -1;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].Id == blockId) return i;
            return -1;
        }

        /// <summary>1 plus the run of immediately-following blocks deeper than blocks[index]. The single
        /// place "with its children" is defined.</summary>
        public static int SubtreeLength(IReadOnlyList<DocBlock> blocks, int index)
        {
            if (blocks == null || index < 0 || index >= blocks.Count) return 0;
            int d = blocks[index].Depth, len = 1;
            while (index + len < blocks.Count && blocks[index + len].Depth > d) len++;
            return len;
        }

        /// <summary>The block a row at <paramref name="depth"/> would hang under, searching back from
        /// <paramref name="fromIndex"/> (exclusive). Null when that depth is top level.</summary>
        static DocBlock ParentFor(IReadOnlyList<DocBlock> blocks, int fromIndex, int depth)
        {
            for (int k = fromIndex - 1; k >= 0; k--)
                if (blocks[k].Depth == depth - 1) return blocks[k];
                else if (blocks[k].Depth < depth - 1) return null;
            return null;
        }

        static bool TakesChildren(DocBlock b) => b != null && (b.Kind == BlockKind.Item || b.Kind == BlockKind.Section);

        // ── Structure ──────────────────────────────────────────────────────────

        public static void Insert(List<DocBlock> blocks, int index, DocBlock block)
        {
            if (blocks == null || block == null) return;
            if (index < 0) index = 0;
            if (index > blocks.Count) index = blocks.Count;
            blocks.Insert(index, block);
        }

        public static void RemoveWithChildren(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return;
            blocks.RemoveRange(i, SubtreeLength(blocks, i));
        }

        public static bool MoveWithSubtree(List<DocBlock> blocks, string blockId, int dir)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0 || (dir != 1 && dir != -1)) return false;

            int d = blocks[i].Depth;
            int len = SubtreeLength(blocks, i);

            if (dir == 1)
            {
                int j = i + len;                                   // the next SIBLING, past our own subtree
                if (j >= blocks.Count || blocks[j].Depth != d) return false;
                int jlen = SubtreeLength(blocks, j);
                // Post-removal: the sibling's subtree slides down to start at i, so landing at i + jlen puts
                // us immediately after all of it.
                MoveRange(blocks, i, len, i + jlen);
                return true;
            }

            int k = i - 1;
            while (k >= 0 && blocks[k].Depth > d) k--;             // skip the previous sibling's children
            if (k < 0 || blocks[k].Depth < d) return false;        // hit our parent, or the top: no sibling
            MoveRange(blocks, i, len, k);                          // k is below i, so removal does not shift it
            return true;
        }

        public static bool MoveSubtreeTo(List<DocBlock> blocks, string blockId, int targetIndex, int targetDepth)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return false;
            int len = SubtreeLength(blocks, i);

            // A block cannot be dropped into its own subtree, nor onto where it already starts.
            if (targetIndex >= i && targetIndex < i + len) return false;
            if (targetIndex < 0 || targetIndex > blocks.Count) return false;

            var moved = blocks[i];
            if (moved.Kind == BlockKind.Section) targetDepth = 0;
            else if (targetDepth < 1) targetDepth = 1;

            // Shift the whole subtree so it keeps its internal shape, move it, then let ClampDepths pull down
            // anything the destination cannot legally hold. Clamping AFTER the move rather than guessing the
            // destination's neighbour beforehand means "legal depth" is decided in exactly one place, and the
            // drop preview (which asks ClampDepths the same question) can never disagree with the result.
            int shift = targetDepth - moved.Depth;
            if (shift != 0)
                for (int k = i; k < i + len; k++) blocks[k].Depth += shift;

            MoveRange(blocks, i, len, targetIndex > i ? targetIndex - len : targetIndex);
            ClampDepths(blocks);
            return true;
        }

        /// <summary>Moves the run [start, start+len) so that it begins at <paramref name="destination"/>,
        /// which is a POST-REMOVAL index — i.e. an index into the list as it looks once the run has been
        /// lifted out. Every caller converts to that convention itself; having this method "helpfully" adjust
        /// a pre-removal index was the one bug this suite caught, because callers that had already adjusted
        /// then got corrected twice and the move collapsed to a no-op.</summary>
        static void MoveRange(List<DocBlock> blocks, int start, int len, int destination)
        {
            var slice = blocks.GetRange(start, len);
            blocks.RemoveRange(start, len);
            if (destination < 0) destination = 0;
            if (destination > blocks.Count) destination = blocks.Count;
            blocks.InsertRange(destination, slice);
        }

        public static bool Indent(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i <= 0) return false;
            var b = blocks[i];
            if (b.Kind == BlockKind.Section) return false;          // I1

            int newDepth = b.Depth + 1;

            // The parent lookup is the ONLY guard needed here, and it enforces I2 as well as I7. A mutation
            // check proved an explicit `newDepth > blocks[i-1].Depth + 1` test could never fire: that
            // condition means the preceding block is shallower than this one, in which case ParentFor already
            // walks off the top and returns null. Keeping it would read as a safety net that nothing reaches.
            // I2 itself is pinned by Validate, not here.
            var parent = ParentFor(blocks, i, newDepth);
            if (!TakesChildren(parent)) return false;                 // no parent at all, or one that takes none

            int len = SubtreeLength(blocks, i);
            for (int k = i; k < i + len; k++) blocks[k].Depth++;
            return true;
        }

        public static bool Outdent(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return false;
            var b = blocks[i];
            if (b.Kind == BlockKind.Section) return false;           // already at 0
            if (b.Depth <= 1) return false;                          // depth 0 is Section territory (I1)

            int len = SubtreeLength(blocks, i);
            for (int k = i; k < i + len; k++) blocks[k].Depth--;
            ClampDepths(blocks);
            return true;
        }

        public static List<int> VisibleIndices(IReadOnlyList<DocBlock> blocks)
        {
            var visible = new List<int>();
            if (blocks == null) return visible;

            int hideBelow = int.MaxValue;   // anything deeper than this is inside a collapsed block
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Depth > hideBelow) continue;
                hideBelow = blocks[i].Collapsed ? blocks[i].Depth : int.MaxValue;
                visible.Add(i);
            }
            return visible;
        }

        // ── Text editing ───────────────────────────────────────────────────────

        /// <summary>Splits a text row at the caret. The new half is inserted AFTER the original's whole
        /// subtree, so children stay with the block they were written under rather than silently
        /// re-parenting to the new half.</summary>
        public static DocBlock SplitAt(List<DocBlock> blocks, string blockId, int caretOffset)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return null;
            var b = blocks[i];
            if (b.Kind == BlockKind.Image || b.Kind == BlockKind.BoardRef) return null;   // no text to split

            string text = b.Text ?? "";
            if (caretOffset < 0) caretOffset = 0;
            if (caretOffset > text.Length) caretOffset = text.Length;

            var created = NewBlock(b.Kind, b.Depth, text.Substring(caretOffset));
            b.Text = text.Substring(0, caretOffset);
            Insert(blocks, i + SubtreeLength(blocks, i), created);
            return created;
        }

        public static bool MergeWithPrevious(List<DocBlock> blocks, string blockId, out int caretOffset)
        {
            caretOffset = 0;
            int i = IndexOf(blocks, blockId);
            if (i <= 0) return false;

            var b = blocks[i];
            var prev = blocks[i - 1];
            if (b.Kind == BlockKind.Image || b.Kind == BlockKind.BoardRef) return false;
            if (prev.Kind == BlockKind.Section) return false;        // never eat a section heading
            if (prev.Kind == BlockKind.Image || prev.Kind == BlockKind.BoardRef) return false;
            if (SubtreeLength(blocks, i) > 1) return false;          // merging a parent would orphan children

            caretOffset = (prev.Text ?? "").Length;
            prev.Text = (prev.Text ?? "") + (b.Text ?? "");
            blocks.RemoveAt(i);
            return true;
        }

        // ── Pages and links ────────────────────────────────────────────────────

        /// <summary>The single section a freshly promoted page opens with. This is NOT the Lazy-DM scaffold
        /// Task 10f retired from CreateSessionSheet — one generic heading ("Описание"), not eight opinionated
        /// ones — and PromoteToPage/EnsurePageFor were never in that task's scope (its brief), so this seed
        /// is untouched by it.
        ///
        /// Formerly justified here as "I2 requires a page's first block to be a Section, so with no seed the
        /// DM would have nowhere to type until they had somehow created one" — Task 10f review round 1's
        /// Critical found that claim true and unresolved (CreateSessionSheet had just started producing
        /// exactly that unreachable empty state), and the fix was DocumentPageView's «+ Раздел» button, which
        /// now gives ANY empty Document page — including, in principle, one this method could leave unseeded
        /// — a way to create its first block. So the claim as written no longer holds. Left seeding anyway:
        /// whether a promoted/bound page should ALSO start empty is a product question of its own, out of
        /// scope for a review finding about CreateSessionSheet, and changing it here would be exactly the
        /// silent scope creep this arc's reviews keep watching for.</summary>
        public const string PromotedPageSectionTitle = "Описание";

        /// <summary>Trim- and case-insensitive name comparison. ToLowerInvariant rather than the current
        /// culture: these names are Cyrillic and must fold the same way on every machine.</summary>
        public static bool NameMatches(string a, string b)
            => (a ?? "").Trim().ToLowerInvariant() == (b ?? "").Trim().ToLowerInvariant();

        public static PageGroup EnsureReferenceGroup(NotesDocument doc)
        {
            if (doc == null) return null;
            var existing = doc.Groups.Find(g => g.IsReference);
            if (existing != null) return existing;

            var group = new PageGroup { Title = ReferenceGroupTitle, IsReference = true };
            doc.Groups.Add(group);
            return group;
        }

        /// <summary>The page (if any) already bound to a given world object. THE one definition of "is this
        /// world object already represented by a page", shared by the two call sites that need it —
        /// QuickOpen's W4 row-suppression and EnsurePageFor's E1 reuse-not-duplicate check below — so neither
        /// hand-rolls that comparison and a future edit to one cannot silently disagree with the other.
        /// NavigatorTree used to be a third, weaker consumer (`p.Bound != null`, never naming a specific
        /// object); Task 10e removed it — «Мир» is the world's contents now and reads no pages. Takes Kind+Id rather than a WorldRef instance so
        /// QuickOpen's per-keystroke, per-world-object caller (CollectWorldHits) allocates nothing extra per
        /// candidate — the same allocation concern Search's own class doc (Q3) already documents for this
        /// file's search path.</summary>
        public static NotesPage FindPageBoundTo(NotesDocument doc, WorldRefKind kind, string id)
        {
            if (doc == null) return null;
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Bound != null && p.Bound.Kind == kind && p.Bound.Id == id) return p;
            return null;
        }

        /// <summary>Returns the id of the page bound to `bound`, creating it if none exists. THE only writer
        /// of NotesPage.Bound. Identity is decided by FindPageBoundTo above, the same Kind+Id predicate this
        /// method and QuickOpen's W4 both defer to — a second hand-rolled comparison here could silently
        /// disagree with it.
        ///
        /// NOTHING CALLS THIS TODAY, AND THAT IS DELIBERATE — it is not dead code awaiting deletion. Task 10b
        /// wrote it to make «Мир» computable (a place appeared in the tree once some path had auto-created
        /// its page); Task 10e's ruling made «Мир» the world's own contents, so all three call sites
        /// (MapScreenController's editor and double-click paths, QuickOpenPopup's world-object row) were
        /// removed rather than left creating notes the DM never asked for. The function and its self-tests
        /// (NotesDocOpsSelfTests.SelfTestEnsurePageFor) stay because Р3's binding work — «привязать заметку к
        /// месту» — is its real caller, and it is still the only writer of the field QuickOpen's W4 reads. A new page is filed into the
        /// IsReference group («Справочник»), which exists for exactly this — PageGroup.IsReference's own doc
        /// calls it "the single group that promoted pages are filed into" — and the group is created if the
        /// document has none. A dedicated «Места» group was rejected: it would be a second mechanism for the
        /// same job.
        ///
        /// DELIBERATELY does not repeat PromoteToPage's name-collision dedupe (that method links a second
        /// «Староста Ольга» to the first rather than creating a duplicate — see its own doc). Identity HERE is
        /// `Bound` (Kind+Id), not `Name`: a hand-authored reference page that happens to share a POI's name is
        /// a different thing from that POI's own page, and folding them by name would silently hijack a page
        /// the user never tied to a map pin. Two pages can therefore legitimately share a Name under this
        /// method where PromoteToPage would never allow it — not an oversight, a different identity rule for
        /// a different kind of "the same thing".</summary>
        public static string EnsurePageFor(NotesDocument doc, WorldRef bound, string title)
        {
            if (doc == null || bound == null) return null;   // E4

            var existing = FindPageBoundTo(doc, bound.Kind, bound.Id);
            if (existing != null) return existing.Id;   // E1 — reuse; nothing created, nothing duplicated.

            var group = EnsureReferenceGroup(doc);   // E5 — reused when present, created when absent.
            string name = string.IsNullOrWhiteSpace(title) ? "Без названия" : title;   // E3

            var page = new NotesPage
            {
                Name = name,
                Kind = PageKind.Document,   // NotesPage.Kind defaults to Board (NotesData.cs) — must be said
                                            // explicitly, or Validate would flag a Document-shaped page (it
                                            // is about to carry Blocks) claiming to be a Board.
                // E2 — a COPY of `bound`, never the caller's own instance: a later caller-side mutation of
                // the WorldObjectRef/WorldRef it built this call from must not be able to rewrite stored
                // state through a shared reference.
                Bound = new WorldRef { Kind = bound.Kind, Id = bound.Id },
            };
            // Same seed PromoteToPage uses, and the same reasoning — see PromotedPageSectionTitle's own doc
            // for why this is still here and what changed under it.
            page.Blocks.Add(NewBlock(BlockKind.Section, 0, PromotedPageSectionTitle));
            page.Blocks.Add(NewBlock(BlockKind.Item, 1));
            group.Pages.Add(page);
            return page.Id;
        }

        /// <summary>The title of the section a block sits under — the nearest Section at or before it.</summary>
        static string SectionTitleFor(IReadOnlyList<DocBlock> blocks, int index)
        {
            for (int k = index; k >= 0; k--)
                if (blocks[k].Kind == BlockKind.Section) return blocks[k].Text ?? "";
            return "";
        }

        /// <summary>Gives a row its own page. When the reference group already holds a page of the same name
        /// the row is LINKED to it instead of a second one being created — promoting «Староста Ольга» in two
        /// sessions must not produce two Ольгas, which is the very failure backlinks exist to prevent.</summary>
        public static NotesPage PromoteToPage(NotesDocument doc, string blockId, out bool linkedExisting)
        {
            linkedExisting = false;
            var block = FindBlock(doc, blockId, out NotesPage owner);
            if (block == null || block.Kind != BlockKind.Item) return null;

            string name = (block.Text ?? "").Trim();
            if (name.Length == 0) return null;

            var group = EnsureReferenceGroup(doc);
            var match = group.Pages.Find(p => NameMatches(p.Name, name));
            if (match != null)
            {
                if (match == owner) return null;                  // I4: never link a row to its own page
                linkedExisting = true;
                block.LinkedPageId = match.Id;
                return match;
            }

            var page = new NotesPage { Name = name, Kind = PageKind.Document };
            page.Blocks.Add(NewBlock(BlockKind.Section, 0, PromotedPageSectionTitle));
            page.Blocks.Add(NewBlock(BlockKind.Item, 1));
            group.Pages.Add(page);
            block.LinkedPageId = page.Id;
            return page;
        }

        public static void LinkExistingPage(NotesDocument doc, string blockId, string pageId)
        {
            var block = FindBlock(doc, blockId, out NotesPage owner);
            if (block == null) return;
            if (block.Kind != BlockKind.Item && block.Kind != BlockKind.BoardRef) return;   // I7

            var target = FindPage(doc, pageId);
            if (target == null || target == owner) return;                                   // I3, I4
            if (block.Kind == BlockKind.BoardRef && target.Kind != PageKind.Board) return;    // I8

            block.LinkedPageId = target.Id;
        }

        public static void Unlink(NotesDocument doc, string blockId)
        {
            var block = FindBlock(doc, blockId, out _);
            if (block == null) return;
            DegradeIfBoardRef(doc, block);
            block.LinkedPageId = null;
        }

        /// <summary>A BoardRef without a board is a card pointing at nothing, which I8 forbids and which says
        /// nothing to a reader. So it becomes a plain row carrying the board's title: the DM can still see
        /// what used to be there.</summary>
        static void DegradeIfBoardRef(NotesDocument doc, DocBlock block)
        {
            if (block.Kind != BlockKind.BoardRef) return;
            var target = FindPage(doc, block.LinkedPageId);
            if (string.IsNullOrEmpty(block.Text) && target != null) block.Text = target.Name;
            block.Kind = BlockKind.Item;
        }

        /// <summary>One thing a page references, from EITHER link mechanism. See CollectReferences.</summary>
        public class PageReference
        {
            public string Kind;
            public string Id;
            /// <summary>Resolved at collection time — never the copy stored inside a token.</summary>
            public string CurrentName;
            /// <summary>The FIRST block that mentions it (R2), so a summary row can scroll to the mention.</summary>
            public string FirstBlockId;
        }

        /// <summary>THE single answer to "what does this page reference".
        ///
        /// Two link mechanisms exist and neither is going away: DocBlock.LinkedPageId is the structural 📄
        /// link from a row to a page (invariant I3, delete-integrity through ClearLinksTo), while an inline
        /// [[token]] is text carrying no integrity guarantee at all — it resolves lazily and degrades to
        /// prose. Two mechanisms that can DISAGREE about what a page references would be a real failure, the
        /// same shape this file already guards against elsewhere ("LinkedPoiId is null rather than paired
        /// with an IsLinked bool: the two could otherwise disagree"). They cannot disagree here because
        /// nothing reads them separately: «Итоги» and «Упомянута в» read this, and only this.
        ///
        /// R1 document order · R2 a repeat keeps its FIRST position and first block · R3 an unresolvable
        /// target is absent rather than present with a blank name · R4 the two mechanisms naming one target
        /// give one entry.
        ///
        /// Within a block the 📄 link is collected BEFORE that block's text: it belongs to the row as a whole
        /// rather than to a position inside it.</summary>
        public static List<PageReference> CollectReferences(NotesPage page, NotesLinkOps.NameResolver resolve)
        {
            var found = new List<PageReference>();
            if (page == null || page.Blocks == null) return found;

            var seen = new HashSet<string>();

            void Add(string kind, string id, string blockId)
            {
                if (string.IsNullOrEmpty(id)) return;
                // Resolve BEFORE de-duplicating. The other order needs a seen.Remove on the unresolvable
                // path to undo an entry it should never have made, and that Remove would be dead code: the
                // resolver is a pure function of (kind, id), so a target that fails once fails always. Two
                // guards on one condition is the exact shape mutation testing already caught on Indent's
                // redundant depth-jump check — one of them has to go, and it is that one.
                string name = resolve != null ? resolve(kind, id) : null;
                if (name == null) return;                      // R3
                if (!seen.Add(kind + ":" + id)) return;        // R2/R4
                found.Add(new PageReference { Kind = kind, Id = id, CurrentName = name, FirstBlockId = blockId });
            }

            foreach (var block in page.Blocks)
            {
                if (block == null) continue;
                if (!string.IsNullOrEmpty(block.LinkedPageId))
                    Add(NotesLinkOps.KindPage, block.LinkedPageId, block.Id);
                foreach (var span in NotesLinkOps.ParseSpans(block.Text))
                    Add(span.Kind, span.Id, block.Id);
                foreach (var span in NotesLinkOps.ParseSpans(block.Detail))
                    Add(span.Kind, span.Id, block.Id);
            }
            return found;
        }

        /// <summary>True when this block references that page by EITHER mechanism. Extracted so that
        /// FindBacklinks, and nothing else, decides what "mentions" means.
        ///
        /// Kind is part of the identity: a [[poi:…]] token that happens to carry a page's id references a
        /// POI, not that page, exactly as FindPageBoundTo already refuses a matching Id under the wrong
        /// WorldRefKind.</summary>
        static bool BlockReferencesPage(DocBlock block, string pageId)
        {
            if (block == null) return false;
            if (block.LinkedPageId == pageId) return true;
            foreach (var span in NotesLinkOps.ParseSpans(block.Text))
                if (span.Kind == NotesLinkOps.KindPage && span.Id == pageId) return true;
            foreach (var span in NotesLinkOps.ParseSpans(block.Detail))
                if (span.Kind == NotesLinkOps.KindPage && span.Id == pageId) return true;
            return false;
        }

        /// <summary>Every place this page is mentioned, for «Упомянута в».
        ///
        /// THE PREDICATE WIDENED IN Р2 and the walk deliberately did not. Before, only a 📄 LinkedPageId
        /// counted, so a page named ten times in prose had no backlinks at all — which made «Упомянута в»
        /// mean something narrower than it says. Now BlockReferencesPage answers for both mechanisms.
        ///
        /// This still walks the blocks itself rather than reading CollectReferences, because it needs the
        /// block INDEX for SectionTitleFor, and a PageReference cannot supply one. That is the whole reason
        /// this is a widening rather than a rewrite, and why every pre-existing backlink test still holds.</summary>
        public static List<Backlink> FindBacklinks(NotesDocument doc, string pageId)
        {
            var found = new List<Backlink>();
            if (doc == null || string.IsNullOrEmpty(pageId)) return found;

            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    for (int i = 0; i < p.Blocks.Count; i++)
                        if (BlockReferencesPage(p.Blocks[i], pageId))
                            found.Add(new Backlink
                            {
                                SourcePageId = p.Id,
                                SourcePageName = p.Name,
                                SectionTitle = SectionTitleFor(p.Blocks, i),
                                BlockId = p.Blocks[i].Id,
                            });
            return found;
        }

        /// <summary>Strips every reference to a page that is about to be deleted, so no saved document ever
        /// holds a dangling LinkedPageId (I3). Rows keep their text; cards degrade to rows. Returns the two
        /// counts separately because the confirm dialog reports them separately — they are different losses.
        ///
        /// Call this for a single page AND for every page of a group being deleted: DeleteGroup takes its
        /// pages with it, which is the second seam, and missing it is what would leave dangling links across
        /// every session sheet.</summary>
        public static (int lines, int cards) ClearLinksTo(NotesDocument doc, string pageId)
        {
            int lines = 0, cards = 0;
            if (doc == null || string.IsNullOrEmpty(pageId)) return (0, 0);

            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    foreach (var b in p.Blocks)
                    {
                        if (b.LinkedPageId != pageId) continue;
                        if (b.Kind == BlockKind.BoardRef) { DegradeIfBoardRef(doc, b); cards++; }
                        else lines++;
                        b.LinkedPageId = null;
                    }
            return (lines, cards);
        }

        /// <summary>Inserts a card that points at a board page. Index 0 is nudged to 1 on a non-empty page:
        /// a page must start with a Section (I2), so a card can never be its first block.</summary>
        public static DocBlock InsertBoardRef(NotesDocument doc, NotesPage page, int index, string boardPageId)
        {
            if (doc == null || page == null) return null;
            var target = FindPage(doc, boardPageId);
            if (target == null || target == page) return null;        // I3, I4
            if (target.Kind != PageKind.Board) return null;           // I8

            var block = NewBlock(BlockKind.BoardRef, 1);
            block.LinkedPageId = target.Id;
            if (index <= 0 && page.Blocks.Count > 0) index = 1;
            Insert(page.Blocks, index, block);
            ClampDepths(page.Blocks);
            return block;
        }

        // ── Block clipboard ────────────────────────────────────────────────────

        const string ClipboardHeader = "NOTESBLOCKS/1";
        const int ClipboardFieldCount = 8;

        /// <summary>Expands a selection to whole subtrees, in DOCUMENT order — clicking a parent takes its
        /// children, clicking both takes each once, and the order ids were clicked in is irrelevant.</summary>
        static List<int> ExpandSelection(IReadOnlyList<DocBlock> blocks, IReadOnlyList<string> selectedIds)
        {
            var covered = new HashSet<int>();
            if (blocks == null || selectedIds == null) return new List<int>();

            foreach (string id in selectedIds)
            {
                int i = IndexOf(blocks, id);
                if (i < 0) continue;
                int len = SubtreeLength(blocks, i);
                for (int k = i; k < i + len; k++) covered.Add(k);
            }

            var ordered = new List<int>(covered);
            ordered.Sort();
            return ordered;
        }

        static int MinDepth(IReadOnlyList<DocBlock> blocks, List<int> indices)
        {
            int min = int.MaxValue;
            foreach (int i in indices) if (blocks[i].Depth < min) min = blocks[i].Depth;
            return min == int.MaxValue ? 0 : min;
        }

        /// <summary>Serializes a selection for the in-app clipboard. A hand-rolled tab-separated format rather
        /// than JSON, because this layer must stay free of Newtonsoft so it can run in the offline harness.
        /// Depths come out RELATIVE to the shallowest selected block, so the paste site decides the absolute
        /// depth. Image bytes travel base64'd: dropping a picture out of a copy would be a nasty surprise.</summary>
        public static string SerializeBlocks(IReadOnlyList<DocBlock> blocks, IReadOnlyList<string> selectedIds)
        {
            var indices = ExpandSelection(blocks, selectedIds);
            int baseDepth = MinDepth(blocks, indices);

            var sb = new StringBuilder();
            sb.Append(ClipboardHeader);
            foreach (int i in indices)
            {
                var b = blocks[i];
                sb.Append('\n')
                  .Append(b.Kind).Append('\t')
                  .Append((b.Depth - baseDepth).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(b.Collapsed ? '1' : '0').Append('\t')
                  .Append(Escape(b.Text)).Append('\t')
                  .Append(Escape(b.Detail)).Append('\t')
                  .Append(Escape(b.LinkedPageId)).Append('\t')
                  .Append(b.DisplayHeight.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(b.ImageBytes != null ? Convert.ToBase64String(b.ImageBytes) : "");
            }
            return sb.ToString();
        }

        /// <summary>Parses a clipboard payload. Every block gets a FRESH id, since an id must never exist twice
        /// in a document (I6). Returns false on anything malformed rather than throwing or half-parsing — the
        /// system clipboard can contain literally anything.</summary>
        public static bool TryDeserializeBlocks(string payload, out List<DocBlock> blocks)
        {
            blocks = new List<DocBlock>();
            if (string.IsNullOrEmpty(payload)) return false;

            string[] lines = payload.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0] != ClipboardHeader) return false;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] f = lines[i].Split('\t');
                if (f.Length != ClipboardFieldCount) { blocks.Clear(); return false; }

                if (!TryParseKind(f[0], out BlockKind kind)) { blocks.Clear(); return false; }
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth))
                { blocks.Clear(); return false; }
                if (!float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float displayHeight))
                { blocks.Clear(); return false; }

                byte[] imageBytes = null;
                if (f[7].Length > 0)
                {
                    try { imageBytes = Convert.FromBase64String(f[7]); }
                    catch (FormatException) { blocks.Clear(); return false; }
                }

                blocks.Add(new DocBlock
                {
                    Kind = kind,
                    Depth = depth,
                    Collapsed = f[2] == "1",
                    Text = Unescape(f[3]) ?? "",
                    Detail = NullIfEmpty(Unescape(f[4])),
                    LinkedPageId = NullIfEmpty(Unescape(f[5])),
                    DisplayHeight = displayHeight,
                    ImageBytes = imageBytes,
                });
            }

            return blocks.Count > 0;
        }

        /// <summary>The same selection as indented plain text, written to the SYSTEM clipboard alongside the
        /// internal payload so a copied block of secrets can be pasted straight into Discord or a text file.</summary>
        public static string ToIndentedText(IReadOnlyList<DocBlock> blocks, IReadOnlyList<string> selectedIds)
        {
            var indices = ExpandSelection(blocks, selectedIds);
            int baseDepth = MinDepth(blocks, indices);

            var sb = new StringBuilder();
            for (int n = 0; n < indices.Count; n++)
            {
                var b = blocks[indices[n]];
                if (n > 0) sb.Append('\n');
                sb.Append(new string(' ', 2 * (b.Depth - baseDepth)));
                sb.Append(b.Kind == BlockKind.Image && string.IsNullOrEmpty(b.Text) ? "[изображение]" : b.Text);
            }
            return sb.ToString();
        }

        static bool TryParseKind(string raw, out BlockKind kind)
        {
            switch (raw)
            {
                case "Section":  kind = BlockKind.Section;  return true;
                case "Item":     kind = BlockKind.Item;     return true;
                case "Prose":    kind = BlockKind.Prose;    return true;
                case "Image":    kind = BlockKind.Image;    return true;
                case "BoardRef": kind = BlockKind.BoardRef; return true;
            }
            kind = BlockKind.Item;
            return false;
        }

        /// <summary>Tabs separate fields and newlines separate records, so both must be escaped inside a text
        /// value — a multi-line row would otherwise corrupt the whole payload. Public because
        /// WorkspaceOps.Serialize (workspace layout persistence) reuses this exact escaper rather than
        /// hand-rolling a second one that would have to agree with it forever — do not re-privatize.</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        /// <summary>Public for the same reason as Escape above — WorkspaceOps.TryDeserialize calls this one
        /// too.</summary>
        public static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length) { sb.Append(value[i]); continue; }
                char next = value[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }

        static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        // ── Integrity ──────────────────────────────────────────────────────────

        public static List<string> Validate(NotesDocument doc)
        {
            var problems = new List<string>();
            if (doc == null) return problems;

            int referenceGroups = 0;
            var seenIds = new HashSet<string>();

            foreach (var g in doc.Groups)
            {
                if (g.IsReference) referenceGroups++;
                foreach (var p in g.Pages)
                {
                    if (p.Kind == PageKind.Board && p.Blocks != null && p.Blocks.Count > 0)
                        problems.Add($"page «{p.Name}» is a Board but carries {p.Blocks.Count} blocks");
                    if (p.Blocks == null) continue;

                    for (int i = 0; i < p.Blocks.Count; i++)
                    {
                        var b = p.Blocks[i];

                        if (!seenIds.Add(b.Id))
                            problems.Add($"duplicate block id {b.Id} (I6)");

                        if (b.Kind == BlockKind.Section && b.Depth != 0)
                            problems.Add($"section «{b.Text}» has Depth {b.Depth}, want 0 (I1)");
                        if (b.Kind != BlockKind.Section && b.Depth < 1)
                            problems.Add($"{b.Kind} block «{b.Text}» has Depth {b.Depth}, want >= 1 (I1)");

                        if (i == 0 && b.Kind != BlockKind.Section)
                            problems.Add($"page «{p.Name}» starts with {b.Kind}, want Section (I2)");
                        if (i > 0 && b.Depth > p.Blocks[i - 1].Depth + 1)
                            problems.Add($"block «{b.Text}» jumps from Depth {p.Blocks[i - 1].Depth} to {b.Depth} (I2)");
                        if (i > 0 && b.Depth > p.Blocks[i - 1].Depth && !TakesChildren(p.Blocks[i - 1]))
                            problems.Add($"block «{b.Text}» is a child of a {p.Blocks[i - 1].Kind} block (I7)");

                        if (!string.IsNullOrEmpty(b.Detail) && b.Kind != BlockKind.Item)
                            problems.Add($"{b.Kind} block «{b.Text}» carries Detail (I7)");
                        if (b.ImageBytes != null && b.Kind != BlockKind.Image)
                            problems.Add($"{b.Kind} block «{b.Text}» carries ImageBytes (I7)");

                        if (!string.IsNullOrEmpty(b.LinkedPageId))
                        {
                            if (b.Kind != BlockKind.Item && b.Kind != BlockKind.BoardRef)
                                problems.Add($"{b.Kind} block «{b.Text}» carries LinkedPageId (I7)");
                            if (b.LinkedPageId == p.Id)
                                problems.Add($"block «{b.Text}» links to its own page (I4)");
                            var target = FindPage(doc, b.LinkedPageId);
                            if (target == null)
                                problems.Add($"block «{b.Text}» links to missing page {b.LinkedPageId} (I3)");
                            else if (b.Kind == BlockKind.BoardRef && target.Kind != PageKind.Board)
                                problems.Add($"BoardRef «{b.Text}» targets a {target.Kind} page (I8)");
                        }
                        else if (b.Kind == BlockKind.BoardRef)
                            problems.Add($"BoardRef «{b.Text}» has no target (I7)");
                    }
                }
            }

            if (referenceGroups > 1)
                problems.Add($"{referenceGroups} groups carry IsReference, want at most 1 (I5)");

            return problems;
        }

        /// <summary>Idempotent, UNGATED repair — never keyed on a format version, following the convention
        /// ProjectSerializer.Load already documents ("a version guard is a thing the NEXT format bump forgets
        /// to widen"). Only ever touches a field it finds wrong, so re-running it is a no-op.</summary>
        public static void Normalize(NotesDocument doc)
        {
            if (doc == null) return;

            bool referenceSeen = false;
            foreach (var g in doc.Groups)
            {
                if (g.IsReference)
                {
                    if (referenceSeen) g.IsReference = false;      // I5
                    else referenceSeen = true;
                }

                foreach (var p in g.Pages)
                {
                    if (p.Blocks == null) p.Blocks = new List<DocBlock>();
                    if (p.Blocks.Count > 0 && p.Kind == PageKind.Board) p.Kind = PageKind.Document;

                    foreach (var b in p.Blocks)
                    {
                        if (b.Text == null) b.Text = "";
                        if (b.Kind == BlockKind.Section) b.Depth = 0;
                        else if (b.Depth < 1) b.Depth = 1;

                        if (b.Kind != BlockKind.Item && !string.IsNullOrEmpty(b.Detail)) b.Detail = null;
                        if (b.Kind != BlockKind.Image) { b.ImageBytes = null; b.DisplayHeight = 0f; }

                        if (!string.IsNullOrEmpty(b.LinkedPageId))
                        {
                            var target = FindPage(doc, b.LinkedPageId);
                            bool bad = b.Kind != BlockKind.Item && b.Kind != BlockKind.BoardRef
                                       || b.LinkedPageId == p.Id
                                       || target == null
                                       || b.Kind == BlockKind.BoardRef && target.Kind != PageKind.Board;
                            if (bad)
                            {
                                // A BoardRef with no reachable board is meaningless as a card, so it degrades
                                // to a plain row carrying what it used to point at — nothing vanishes silently.
                                if (b.Kind == BlockKind.BoardRef)
                                {
                                    b.Kind = BlockKind.Item;
                                    if (string.IsNullOrEmpty(b.Text) && target != null) b.Text = target.Name;
                                }
                                b.LinkedPageId = null;
                            }
                        }
                        else if (b.Kind == BlockKind.BoardRef)
                            b.Kind = BlockKind.Item;
                    }

                    ClampDepths(p.Blocks);
                }
            }
        }

        /// <summary>Walks a block list once and pulls any depth that violates I2 or I7 back to the deepest
        /// legal value. Shared by Outdent, MoveSubtreeTo and Normalize so "legal depth" has one definition.</summary>
        static void ClampDepths(List<DocBlock> blocks)
        {
            if (blocks == null) return;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Kind == BlockKind.Section) { b.Depth = 0; continue; }
                if (b.Depth < 1) b.Depth = 1;
                if (i == 0) continue;

                var prev = blocks[i - 1];
                int max = TakesChildren(prev) ? prev.Depth + 1 : prev.Depth;
                if (max < 1) max = 1;
                if (b.Depth > max) b.Depth = max;
            }
        }
    }
}
