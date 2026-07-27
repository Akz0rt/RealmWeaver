using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Self-tests for the pure document layer. Runs two ways: right-click this component in the Editor, or
    /// offline via Tools/notes-harness (`powershell -File sync.ps1` then `dotnet run -c Release -- selftests`
    /// from bash), which compiles these very sources against UnityEngine stubs. The offline route is the one
    /// used during development — it needs no scene, no Editor, and no Library import.
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would break,
    /// not a derived summary number — a test that only checks a count keeps passing while the rule rots.
    /// </summary>
    public class NotesDocOpsSelfTests : MonoBehaviour
    {
        // ── Fixtures ───────────────────────────────────────────────────────────

        /// <summary>Wraps one page in a single-group document.</summary>
        static NotesDocument Doc(NotesPage page)
        {
            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            g.Pages.Add(page);
            doc.Groups.Add(g);
            return doc;
        }

        /// <summary>The standard structural fixture: S0 / a / a1 / b / S1 / c.</summary>
        static List<DocBlock> Sheet() => new List<DocBlock>
        {
            NotesDocOps.NewBlock(BlockKind.Section, 0, "S0"),
            NotesDocOps.NewBlock(BlockKind.Item, 1, "a"),
            NotesDocOps.NewBlock(BlockKind.Item, 2, "a1"),
            NotesDocOps.NewBlock(BlockKind.Item, 1, "b"),
            NotesDocOps.NewBlock(BlockKind.Section, 0, "S1"),
            NotesDocOps.NewBlock(BlockKind.Item, 1, "c"),
        };

        static string Dump(IReadOnlyList<DocBlock> blocks)
        {
            var parts = new List<string>();
            foreach (var b in blocks) parts.Add(b.Text);
            return string.Join("/", parts);
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Session Sheet Template")]
        public void SelfTestTemplate()
        {
            bool ok = true;
            var page = NotesDocOps.CreateSessionSheet("Сессия 1");

            if (page.Kind != PageKind.Document)
            { Debug.LogError($"FAIL template: Kind = {page.Kind}, want Document"); ok = false; }

            var sections = page.Blocks.FindAll(b => b.Kind == BlockKind.Section);
            if (sections.Count != 8)
            { Debug.LogError($"FAIL template: {sections.Count} sections, want 8"); ok = false; }
            if (ok && sections[0].Text != "Персонажи игроков")
            { Debug.LogError($"FAIL template: first section «{sections[0].Text}», want «Персонажи игроков»"); ok = false; }
            if (ok && sections[7].Text != "Награды")
            { Debug.LogError($"FAIL template: last section «{sections[7].Text}», want «Награды»"); ok = false; }
            foreach (var s in sections)
                if (s.Depth != 0)
                { Debug.LogError($"FAIL template: section «{s.Text}» has Depth {s.Depth}, want 0 (I1)"); ok = false; }

            // Section 2 is the ONLY seeded child, and it is Prose — that is what makes the read-aloud
            // paragraph a paragraph rather than a bullet.
            int idx2 = page.Blocks.FindIndex(b => b.Kind == BlockKind.Section && b.Text == "Сильное начало");
            var seeded = page.Blocks.FindAll(b => b.Kind != BlockKind.Section);
            if (seeded.Count != 1 || seeded[0].Kind != BlockKind.Prose)
            { Debug.LogError($"FAIL template: {seeded.Count} seeded child blocks, want exactly 1 Prose"); ok = false; }
            else if (page.Blocks.IndexOf(seeded[0]) != idx2 + 1)
            { Debug.LogError("FAIL template: the Prose block must sit directly under «Сильное начало»"); ok = false; }

            if (NotesDocOps.SectionHints.Count != 8)
            { Debug.LogError($"FAIL template: {NotesDocOps.SectionHints.Count} hints, want 8"); ok = false; }
            foreach (var s in sections)
                if (!NotesDocOps.SectionHints.ContainsKey(s.Text))
                { Debug.LogError($"FAIL template: no hint for section «{s.Text}»"); ok = false; }

            var problems = NotesDocOps.Validate(Doc(page));
            if (problems.Count != 0)
            { Debug.LogError($"FAIL template: a fresh sheet must be valid, got: {string.Join("; ", problems)}"); ok = false; }

            Debug.Log(ok ? "Self-Test Session Sheet Template: PASS" : "Self-Test Session Sheet Template: FAIL");
        }

        [ContextMenu("Self-Test: Collapse Visibility")]
        public void SelfTestCollapse()
        {
            bool ok = true;
            var blocks = Sheet();   // S0 / a / a1 / b / S1 / c

            var vis = NotesDocOps.VisibleIndices(blocks);
            if (vis.Count != 6)
            { Debug.LogError($"FAIL collapse: nothing collapsed gave {vis.Count} visible, want 6"); ok = false; }

            // Collapsing S0 hides a, a1 and b — and NOTHING from S1 onward.
            blocks[0].Collapsed = true;
            vis = NotesDocOps.VisibleIndices(blocks);
            if (vis.Count != 3 || vis[0] != 0 || vis[1] != 4 || vis[2] != 5)
            { Debug.LogError($"FAIL collapse: visible = [{string.Join(",", vis)}], want [0,4,5]"); ok = false; }

            // Collapsing the INNER item hides only its own child.
            blocks[0].Collapsed = false;
            blocks[1].Collapsed = true;
            vis = NotesDocOps.VisibleIndices(blocks);
            if (vis.Count != 5 || vis.Contains(2))
            { Debug.LogError($"FAIL collapse: inner collapse gave [{string.Join(",", vis)}], want index 2 hidden and 5 visible"); ok = false; }

            Debug.Log(ok ? "Self-Test Collapse Visibility: PASS" : "Self-Test Collapse Visibility: FAIL");
        }

        [ContextMenu("Self-Test: Structure Ops")]
        public void SelfTestStructure()
        {
            bool ok = true;
            var blocks = Sheet();

            // RemoveWithChildren takes the deeper followers with it, and nothing else.
            NotesDocOps.RemoveWithChildren(blocks, blocks[1].Id);          // removes a AND a1
            if (blocks.Count != 4 || Dump(blocks) != "S0/b/S1/c")
            { Debug.LogError($"FAIL structure: after removing «a» got [{Dump(blocks)}], want [S0/b/S1/c]"); ok = false; }

            // MoveWithSubtree on a SECTION jumps the whole neighbouring section, never enters it.
            blocks = Sheet();
            if (!NotesDocOps.MoveWithSubtree(blocks, blocks[0].Id, +1))
            { Debug.LogError("FAIL structure: moving the first section down must succeed"); ok = false; }
            if (Dump(blocks) != "S1/c/S0/a/a1/b")
            { Debug.LogError($"FAIL structure: section move down gave [{Dump(blocks)}], want [S1/c/S0/a/a1/b]"); ok = false; }

            // At the end it is a no-op that reports false rather than mangling the list.
            blocks = Sheet();
            if (NotesDocOps.MoveWithSubtree(blocks, blocks[4].Id, +1))
            { Debug.LogError("FAIL structure: moving the last section down must return false"); ok = false; }
            if (Dump(blocks) != "S0/a/a1/b/S1/c")
            { Debug.LogError($"FAIL structure: a refused move must not modify the list, got [{Dump(blocks)}]"); ok = false; }

            // Indent cannot break I1 (a Section is never indentable) or I2 (no depth jump of 2).
            blocks = Sheet();
            if (NotesDocOps.Indent(blocks, blocks[0].Id))
            { Debug.LogError("FAIL structure: indenting a Section must be refused (I1)"); ok = false; }
            if (NotesDocOps.Indent(blocks, blocks[1].Id))
            { Debug.LogError("FAIL structure: indenting the FIRST child of a section must be refused (I2)"); ok = false; }
            if (!NotesDocOps.Indent(blocks, blocks[3].Id) || blocks[3].Depth != 2)
            { Debug.LogError($"FAIL structure: «b» should indent to Depth 2, got {blocks[3].Depth}"); ok = false; }

            // Outdent is its inverse and stops at 1 for a non-Section.
            if (!NotesDocOps.Outdent(blocks, blocks[3].Id) || blocks[3].Depth != 1)
            { Debug.LogError($"FAIL structure: «b» should outdent back to Depth 1, got {blocks[3].Depth}"); ok = false; }
            if (NotesDocOps.Outdent(blocks, blocks[3].Id))
            { Debug.LogError("FAIL structure: outdenting a Depth-1 row to 0 must be refused — it would become a Section (I1)"); ok = false; }

            // A drop inside the moved block's own subtree is refused.
            blocks = Sheet();
            if (NotesDocOps.MoveSubtreeTo(blocks, blocks[1].Id, 2, 2))
            { Debug.LogError("FAIL structure: dropping «a» inside its own subtree must be refused"); ok = false; }
            if (Dump(blocks) != "S0/a/a1/b/S1/c")
            { Debug.LogError($"FAIL structure: a refused drop must not modify the list, got [{Dump(blocks)}]"); ok = false; }

            // Prose takes no children (I7).
            blocks = new List<DocBlock>
            {
                NotesDocOps.NewBlock(BlockKind.Section, 0, "S"),
                NotesDocOps.NewBlock(BlockKind.Prose, 1, "p"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "x"),
            };
            if (NotesDocOps.Indent(blocks, blocks[2].Id))
            { Debug.LogError("FAIL structure: indenting under a Prose block must be refused (I7)"); ok = false; }

            Debug.Log(ok ? "Self-Test Structure Ops: PASS" : "Self-Test Structure Ops: FAIL");
        }

        [ContextMenu("Self-Test: Split and Merge")]
        public void SelfTestSplitMerge()
        {
            bool ok = true;
            var blocks = new List<DocBlock>
            {
                NotesDocOps.NewBlock(BlockKind.Section, 0, "S"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "Староста Ольга"),
            };

            var created = NotesDocOps.SplitAt(blocks, blocks[1].Id, 8);   // "Староста" | " Ольга"
            if (blocks[1].Text != "Староста" || created == null || created.Text != " Ольга")
            { Debug.LogError($"FAIL split: got «{blocks[1].Text}» + «{created?.Text}», want «Староста» + « Ольга»"); ok = false; }
            if (created != null && (created.Depth != 1 || created.Kind != BlockKind.Item))
            { Debug.LogError($"FAIL split: new block Kind/Depth = {created.Kind}/{created.Depth}, want Item/1"); ok = false; }
            if (created != null && blocks[1].Id == created.Id)
            { Debug.LogError("FAIL split: the two halves must not share an Id (I6)"); ok = false; }
            if (created != null && blocks.IndexOf(created) != 2)
            { Debug.LogError($"FAIL split: the new half sits at index {blocks.IndexOf(created)}, want 2 (directly after)"); ok = false; }

            // Merge restores the original text exactly and reports where the caret lands.
            if (created != null)
            {
                if (!NotesDocOps.MergeWithPrevious(blocks, created.Id, out int caret))
                { Debug.LogError("FAIL merge: merging the second half must succeed"); ok = false; }
                else
                {
                    if (blocks.Count != 2 || blocks[1].Text != "Староста Ольга")
                    { Debug.LogError($"FAIL merge: got [{Dump(blocks)}], want [S/Староста Ольга]"); ok = false; }
                    if (caret != 8)
                    { Debug.LogError($"FAIL merge: caret = {caret}, want 8"); ok = false; }
                }
            }

            // The first child of a section has nothing to merge into — refuse, don't eat the section.
            if (NotesDocOps.MergeWithPrevious(blocks, blocks[1].Id, out _))
            { Debug.LogError("FAIL merge: merging the first child into its Section must be refused"); ok = false; }
            if (blocks.Count != 2)
            { Debug.LogError($"FAIL merge: a refused merge changed the list to [{Dump(blocks)}]"); ok = false; }

            // A row that HAS children cannot be merged away — its children would be left hanging under the
            // row it merged into. Here every other guard passes (both rows are Items, the previous one is not
            // a Section), so only the children check can refuse it.
            var withKids = new List<DocBlock>
            {
                NotesDocOps.NewBlock(BlockKind.Section, 0, "S"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "a"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "b"),
                NotesDocOps.NewBlock(BlockKind.Item, 2, "b1"),
            };
            if (NotesDocOps.MergeWithPrevious(withKids, withKids[2].Id, out _))
            { Debug.LogError("FAIL merge: merging a row that has children must be refused — they would be orphaned"); ok = false; }
            if (Dump(withKids) != "S/a/b/b1")
            { Debug.LogError($"FAIL merge: a refused merge changed the list to [{Dump(withKids)}], want [S/a/b/b1]"); ok = false; }

            Debug.Log(ok ? "Self-Test Split and Merge: PASS" : "Self-Test Split and Merge: FAIL");
        }

        [ContextMenu("Self-Test: Validate and Normalize")]
        public void SelfTestValidateNormalize()
        {
            bool ok = true;
            var page = NotesDocOps.CreateSessionSheet("С");
            var doc = Doc(page);

            // A depth jump of 2 is a violation, and Normalize clamps it instead of failing.
            page.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Item, 3, "too deep"));
            if (NotesDocOps.Validate(doc).Count == 0)
            { Debug.LogError("FAIL validate: a depth jump of 2 must be reported (I2)"); ok = false; }
            NotesDocOps.Normalize(doc);
            var after = NotesDocOps.Validate(doc);
            if (after.Count != 0)
            { Debug.LogError($"FAIL normalize: still invalid after repair: {string.Join("; ", after)}"); ok = false; }

            // A dangling LinkedPageId is cleared, not left for a renderer to trip over. Use the Item that
            // Normalize just clamped, so this checks I3 and nothing else.
            var item = page.Blocks[page.Blocks.Count - 1];
            if (item.Kind != BlockKind.Item)
            { Debug.LogError($"FAIL normalize: expected the appended block to still be an Item, got {item.Kind}"); ok = false; }
            item.LinkedPageId = "no-such-page";
            NotesDocOps.Normalize(doc);
            if (!string.IsNullOrEmpty(item.LinkedPageId))
            { Debug.LogError($"FAIL normalize: unresolvable LinkedPageId «{item.LinkedPageId}» must be cleared (I3)"); ok = false; }

            // A page carrying blocks but claiming Board is repaired to Document.
            page.Kind = PageKind.Board;
            NotesDocOps.Normalize(doc);
            if (page.Kind != PageKind.Document)
            { Debug.LogError($"FAIL normalize: Kind = {page.Kind}, want Document for a page that has blocks"); ok = false; }

            // Idempotence: a second pass changes nothing — neither text, nor depths, nor ids.
            string beforeText = Dump(page.Blocks);
            var beforeIds = new List<string>();
            var beforeDepths = new List<int>();
            foreach (var b in page.Blocks) { beforeIds.Add(b.Id); beforeDepths.Add(b.Depth); }
            NotesDocOps.Normalize(doc);
            if (Dump(page.Blocks) != beforeText)
            { Debug.LogError("FAIL normalize: not idempotent — a second pass changed the text"); ok = false; }
            for (int i = 0; i < page.Blocks.Count && i < beforeIds.Count; i++)
            {
                if (page.Blocks[i].Id != beforeIds[i])
                { Debug.LogError($"FAIL normalize: block {i} changed Id on a second pass — ids must be stable"); ok = false; break; }
                if (page.Blocks[i].Depth != beforeDepths[i])
                { Debug.LogError($"FAIL normalize: block {i} changed Depth on a second pass"); ok = false; break; }
            }

            Debug.Log(ok ? "Self-Test Validate and Normalize: PASS" : "Self-Test Validate and Normalize: FAIL");
        }

        // ── Task 2 fixture ─────────────────────────────────────────────────────

        /// <summary>A «Сессии» group holding one session sheet, a reference group holding an «Староста Ольга»
        /// document page and a «Связи культа» BOARD page, one row in the sheet linked to Ольга, and one
        /// BoardRef card pointing at the board. The board carries 7 objects so the card's «7 объектов»
        /// subtitle has something real to read.</summary>
        static NotesDocument BuildDocWithOlgaAndBoard(out NotesPage sheet, out NotesPage olga,
                                                      out NotesPage board, out DocBlock line, out DocBlock card)
        {
            var doc = new NotesDocument();
            var sessions = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(sessions);

            sheet = NotesDocOps.CreateSessionSheet("Сессия 1");
            sessions.Pages.Add(sheet);

            var reference = NotesDocOps.EnsureReferenceGroup(doc);
            olga = new NotesPage { Name = "Староста Ольга", Kind = PageKind.Document };
            board = new NotesPage { Name = "Связи культа", Kind = PageKind.Board };
            for (int i = 0; i < 7; i++) board.Objects.Add(new NoteCardData());
            reference.Pages.Add(olga);
            reference.Pages.Add(board);

            line = NotesDocOps.NewBlock(BlockKind.Item, 1, "Староста Ольга");
            line.LinkedPageId = olga.Id;
            NotesDocOps.Insert(sheet.Blocks, sheet.Blocks.FindIndex(b => b.Text == "Важные NPC") + 1, line);

            card = NotesDocOps.NewBlock(BlockKind.BoardRef, 1);
            card.LinkedPageId = board.Id;
            NotesDocOps.Insert(sheet.Blocks, sheet.Blocks.Count, card);

            return doc;
        }

        // ── Task 2 tests ───────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Promote and Backlinks")]
        public void SelfTestPromoteBacklinks()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);
            var s1 = NotesDocOps.CreateSessionSheet("Сессия 1"); g.Pages.Add(s1);
            var s3 = NotesDocOps.CreateSessionSheet("Сессия 3"); g.Pages.Add(s3);

            var olga1 = NotesDocOps.NewBlock(BlockKind.Item, 1, "Староста Ольга");
            NotesDocOps.Insert(s1.Blocks, s1.Blocks.FindIndex(b => b.Text == "Важные NPC") + 1, olga1);

            var page = NotesDocOps.PromoteToPage(doc, olga1.Id, out bool linkedExisting);
            if (page == null || page.Name != "Староста Ольга")
            { Debug.LogError($"FAIL promote: page name «{page?.Name}», want «Староста Ольга»"); ok = false; }
            if (linkedExisting)
            { Debug.LogError("FAIL promote: the first promotion must CREATE, not link"); ok = false; }
            if (page != null && olga1.LinkedPageId != page.Id)
            { Debug.LogError("FAIL promote: the source row must point at the new page"); ok = false; }
            if (page != null && page.Kind != PageKind.Document)
            { Debug.LogError($"FAIL promote: promoted page Kind = {page.Kind}, want Document"); ok = false; }

            var refGroup = doc.Groups.Find(x => x.IsReference);
            if (refGroup == null || refGroup.Title != NotesDocOps.ReferenceGroupTitle || !refGroup.Pages.Contains(page))
            { Debug.LogError("FAIL promote: the new page must be filed in the reference group"); ok = false; }

            // The duplicate guard: the same name in another session LINKS, it does not create a second Ольга.
            var olga2 = NotesDocOps.NewBlock(BlockKind.Item, 1, "  староста ольга  ");
            NotesDocOps.Insert(s3.Blocks, s3.Blocks.FindIndex(b => b.Text == "Важные NPC") + 1, olga2);
            var again = NotesDocOps.PromoteToPage(doc, olga2.Id, out linkedExisting);
            if (!linkedExisting || again != page)
            { Debug.LogError("FAIL promote: a matching name (case- and space-insensitive) must LINK to the existing page"); ok = false; }
            if (refGroup != null && refGroup.Pages.Count != 1)
            { Debug.LogError($"FAIL promote: reference group holds {refGroup.Pages.Count} pages, want 1"); ok = false; }

            // Exactly one reference group ever exists (I5).
            NotesDocOps.EnsureReferenceGroup(doc); NotesDocOps.EnsureReferenceGroup(doc);
            if (doc.Groups.FindAll(x => x.IsReference).Count != 1)
            { Debug.LogError($"FAIL promote: {doc.Groups.FindAll(x => x.IsReference).Count} reference groups, want 1 (I5)"); ok = false; }

            // Backlinks find every referencing row, across pages, and name the SECTION it sits under.
            var backs = NotesDocOps.FindBacklinks(doc, page.Id);
            if (backs.Count != 2)
            { Debug.LogError($"FAIL backlinks: {backs.Count} found, want 2"); ok = false; }
            else
            {
                if (backs[0].SourcePageName != "Сессия 1" || backs[0].SectionTitle != "Важные NPC")
                { Debug.LogError($"FAIL backlinks: first = «{backs[0].SourcePageName}» / «{backs[0].SectionTitle}», want «Сессия 1» / «Важные NPC»"); ok = false; }
                if (backs[1].SourcePageName != "Сессия 3")
                { Debug.LogError($"FAIL backlinks: second came from «{backs[1].SourcePageName}», want «Сессия 3»"); ok = false; }
                if (backs[0].BlockId != olga1.Id)
                { Debug.LogError("FAIL backlinks: the entry must name the block it came from"); ok = false; }
            }
            if (NotesDocOps.FindBacklinks(doc, "no-such-page").Count != 0)
            { Debug.LogError("FAIL backlinks: an unreferenced page must produce an empty list, not throw"); ok = false; }

            // Unlink removes exactly one reference and leaves the text alone.
            NotesDocOps.Unlink(doc, olga1.Id);
            if (!string.IsNullOrEmpty(olga1.LinkedPageId) || olga1.Text != "Староста Ольга")
            { Debug.LogError($"FAIL unlink: link «{olga1.LinkedPageId}» / text «{olga1.Text}» — want no link, text intact"); ok = false; }
            if (NotesDocOps.FindBacklinks(doc, page.Id).Count != 1)
            { Debug.LogError("FAIL unlink: one backlink should remain"); ok = false; }

            var problems = NotesDocOps.Validate(doc);
            if (problems.Count != 0)
            { Debug.LogError($"FAIL promote: document invalid: {string.Join("; ", problems)}"); ok = false; }

            Debug.Log(ok ? "Self-Test Promote and Backlinks: PASS" : "Self-Test Promote and Backlinks: FAIL");
        }

        [ContextMenu("Self-Test: Delete Integrity Both Seams")]
        public void SelfTestDeleteIntegrity()
        {
            bool ok = true;

            // Seam 1 — deleting one referenced page: the row's text survives, its link goes, nothing dangles.
            var doc = BuildDocWithOlgaAndBoard(out NotesPage sheet, out NotesPage olga, out NotesPage board,
                                               out DocBlock line, out DocBlock card);
            if (NotesDocOps.Validate(doc).Count != 0)
            { Debug.LogError($"FAIL delete: the fixture itself is invalid: {string.Join("; ", NotesDocOps.Validate(doc))}"); ok = false; }

            var counts = NotesDocOps.ClearLinksTo(doc, olga.Id);
            if (counts.lines != 1 || counts.cards != 0)
            { Debug.LogError($"FAIL delete: counted {counts.lines} lines / {counts.cards} cards, want 1 / 0"); ok = false; }
            if (line.Text != "Староста Ольга")
            { Debug.LogError($"FAIL delete: row text became «{line.Text}» — it must survive"); ok = false; }
            if (!string.IsNullOrEmpty(line.LinkedPageId))
            { Debug.LogError("FAIL delete: the link must be cleared (I3)"); ok = false; }

            // A deleted BOARD degrades its card to a row carrying the old title — it never orphans it.
            counts = NotesDocOps.ClearLinksTo(doc, board.Id);
            if (counts.cards != 1 || counts.lines != 0)
            { Debug.LogError($"FAIL delete: counted {counts.lines} lines / {counts.cards} cards, want 0 / 1"); ok = false; }
            if (card.Kind != BlockKind.Item)
            { Debug.LogError($"FAIL delete: card Kind = {card.Kind}, want Item once its board died"); ok = false; }
            if (card.Text != "Связи культа")
            { Debug.LogError($"FAIL delete: degraded card text «{card.Text}», want «Связи культа»"); ok = false; }
            if (!string.IsNullOrEmpty(card.LinkedPageId))
            { Debug.LogError("FAIL delete: the degraded card must hold no LinkedPageId (I8)"); ok = false; }

            // Seam 2 — deleting the whole GROUP. This is the seam the spec's first draft missed entirely.
            doc = BuildDocWithOlgaAndBoard(out sheet, out olga, out board, out line, out card);
            var refGroup = doc.Groups.Find(x => x.IsReference);
            int lines = 0, cards = 0;
            foreach (var p in new List<NotesPage>(refGroup.Pages))
            { var c = NotesDocOps.ClearLinksTo(doc, p.Id); lines += c.lines; cards += c.cards; }
            doc.Groups.Remove(refGroup);

            if (lines != 1 || cards != 1)
            { Debug.LogError($"FAIL delete: group deletion counted {lines} lines / {cards} cards, want 1 / 1"); ok = false; }
            var problems = NotesDocOps.Validate(doc);
            if (problems.Count != 0)
            { Debug.LogError($"FAIL delete: deleting a group left dangling references: {string.Join("; ", problems)}"); ok = false; }
            if (line.Text != "Староста Ольга" || card.Text != "Связи культа")
            { Debug.LogError($"FAIL delete: group deletion lost text — row «{line.Text}», card «{card.Text}»"); ok = false; }

            Debug.Log(ok ? "Self-Test Delete Integrity Both Seams: PASS" : "Self-Test Delete Integrity Both Seams: FAIL");
        }

        [ContextMenu("Self-Test: BoardRef Rules")]
        public void SelfTestBoardRef()
        {
            bool ok = true;
            var doc = BuildDocWithOlgaAndBoard(out NotesPage sheet, out NotesPage olga, out NotesPage board,
                                               out _, out _);

            if (board.Objects.Count != 7)
            { Debug.LogError($"FAIL boardref: fixture board holds {board.Objects.Count} objects, want 7"); ok = false; }

            // A BoardRef may only target a Board page (I8).
            var bad = NotesDocOps.InsertBoardRef(doc, sheet, sheet.Blocks.Count, olga.Id);
            if (bad != null)
            { Debug.LogError("FAIL boardref: pointing a BoardRef at a Document page must be refused (I8)"); ok = false; }

            var good = NotesDocOps.InsertBoardRef(doc, sheet, sheet.Blocks.Count, board.Id);
            if (good == null || good.Kind != BlockKind.BoardRef || good.LinkedPageId != board.Id)
            { Debug.LogError("FAIL boardref: a Board target must produce a BoardRef pointing at it"); ok = false; }
            if (good != null && good.Depth < 1)
            { Debug.LogError($"FAIL boardref: Depth = {good?.Depth}, want >= 1 (I1)"); ok = false; }
            if (good != null && !string.IsNullOrEmpty(good.Detail))
            { Debug.LogError("FAIL boardref: a BoardRef must carry no Detail (I7)"); ok = false; }

            // Linking a card at its own page is refused (I4).
            if (NotesDocOps.InsertBoardRef(doc, sheet, sheet.Blocks.Count, sheet.Id) != null)
            { Debug.LogError("FAIL boardref: a card pointing at its own page must be refused (I4)"); ok = false; }

            var problems = NotesDocOps.Validate(doc);
            if (problems.Count != 0)
            { Debug.LogError($"FAIL boardref: document invalid: {string.Join("; ", problems)}"); ok = false; }

            Debug.Log(ok ? "Self-Test BoardRef Rules: PASS" : "Self-Test BoardRef Rules: FAIL");
        }
    }
}
