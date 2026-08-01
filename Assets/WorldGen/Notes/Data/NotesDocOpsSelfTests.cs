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

        // Formerly SelfTestTemplate: pinned an 8-section Lazy-DM scaffold (8 sections, first «Персонажи
        // игроков», last «Награды», 8 hints). Task 10f's DM ruling reversed that — sections are the DM's to
        // make, not the tool's to pre-fill — so this asserts the opposite of what it used to: a fresh sheet
        // starts with NOTHING. Replaced, not deleted, so "a new sheet stays empty" is still a checked rule and
        // not just a fact nobody guards.
        [ContextMenu("Self-Test: Session Sheet Starts Empty")]
        public void SelfTestSessionSheetStartsEmpty()
        {
            bool ok = true;
            var page = NotesDocOps.CreateSessionSheet("Сессия 1");

            if (page.Kind != PageKind.Document)
            { Debug.LogError($"FAIL empty sheet: Kind = {page.Kind}, want Document"); ok = false; }

            if (page.Blocks == null || page.Blocks.Count != 0)
            { Debug.LogError($"FAIL empty sheet: {page.Blocks?.Count} blocks, want 0 — the DM makes the sections now, not CreateSessionSheet"); ok = false; }

            // I2 only requires a Section-first block on a NON-empty page, so a zero-block page is valid as-is
            // — no seed is needed to satisfy Validate, unlike EnsurePageFor/PromoteToPage which DO seed a
            // Section for exactly that reason (see PromotedPageSectionTitle's own doc).
            var problems = NotesDocOps.Validate(Doc(page));
            if (problems.Count != 0)
            { Debug.LogError($"FAIL empty sheet: an empty sheet must already be valid, got: {string.Join("; ", problems)}"); ok = false; }

            Debug.Log(ok ? "Self-Test Session Sheet Starts Empty: PASS" : "Self-Test Session Sheet Starts Empty: FAIL");
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

            // CreateSessionSheet no longer seeds a Section (Task 10f) — this fixture adds its own, since I2
            // requires a non-empty page's first block to BE one, and Normalize clamps depths against
            // structure that already exists rather than inventing a missing leading Section.
            page.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "S"));

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

        [ContextMenu("Self-Test: Defaults Match Absent Json Keys")]
        public void SelfTestDefaultsMatchAbsentKeys()
        {
            bool ok = true;

            // This is the whole basis of "an older project loses nothing": a file written before document
            // pages existed carries no Kind, Blocks or IsReference key, and Newtonsoft leaves such fields at
            // their default. So the defaults ARE the migration, and they are worth pinning here rather than
            // only in the Editor-only serializer test.
            var page = new NotesPage();
            if (page.Kind != PageKind.Board)
            { Debug.LogError($"FAIL defaults: a new page's Kind is {page.Kind}, want Board — Board must be the enum's zero value"); ok = false; }
            if ((int)PageKind.Board != 0)
            { Debug.LogError($"FAIL defaults: PageKind.Board = {(int)PageKind.Board}, want 0"); ok = false; }
            if (page.Blocks == null || page.Blocks.Count != 0)
            { Debug.LogError("FAIL defaults: a new page must start with an EMPTY, non-null block list"); ok = false; }

            if (new PageGroup().IsReference)
            { Debug.LogError("FAIL defaults: a new group must not claim the reference role"); ok = false; }

            var block = new DocBlock();
            if (block.Kind != BlockKind.Section)
            { Debug.LogError($"FAIL defaults: a new block's Kind is {block.Kind}, want Section (the zero value)"); ok = false; }
            if (block.Text != "" || block.Detail != null || block.LinkedPageId != null || block.ImageBytes != null)
            { Debug.LogError("FAIL defaults: optional block fields must start empty/null so they stay off the wire"); ok = false; }
            if (string.IsNullOrEmpty(block.Id))
            { Debug.LogError("FAIL defaults: a new block must already carry an id"); ok = false; }

            // A hand-edited or truncated file can present a page with a null Blocks list. Normalize must
            // repair that rather than let every consumer guard for null.
            var doc = Doc(new NotesPage { Name = "Доска", Kind = PageKind.Board });
            doc.Groups[0].Pages[0].Blocks = null;
            NotesDocOps.Normalize(doc);
            if (doc.Groups[0].Pages[0].Blocks == null)
            { Debug.LogError("FAIL defaults: Normalize must replace a null block list with an empty one"); ok = false; }
            if (doc.Groups[0].Pages[0].Kind != PageKind.Board)
            { Debug.LogError("FAIL defaults: a board with no blocks must stay a Board"); ok = false; }
            if (NotesDocOps.Validate(doc).Count != 0)
            { Debug.LogError($"FAIL defaults: an empty board is valid, got: {string.Join("; ", NotesDocOps.Validate(doc))}"); ok = false; }

            Debug.Log(ok ? "Self-Test Defaults Match Absent Json Keys: PASS" : "Self-Test Defaults Match Absent Json Keys: FAIL");
        }

        [ContextMenu("Self-Test: Block Clipboard")]
        public void SelfTestClipboard()
        {
            bool ok = true;
            var blocks = Sheet();                                    // S0 / a / a1 / b / S1 / c

            // Copying a parent brings its children, and depths come back RELATIVE to the shallowest copied
            // block so the selection can be pasted at any depth.
            string payload = NotesDocOps.SerializeBlocks(blocks, new List<string> { blocks[1].Id });
            if (!NotesDocOps.TryDeserializeBlocks(payload, out var copied))
            { Debug.LogError("FAIL clipboard: a payload we just produced failed to parse"); ok = false; }
            else
            {
                if (copied.Count != 2 || copied[0].Text != "a" || copied[1].Text != "a1")
                { Debug.LogError($"FAIL clipboard: copied [{Dump(copied)}], want [a/a1] — a parent must bring its children"); ok = false; }
                else
                {
                    if (copied[0].Depth != 0 || copied[1].Depth != 1)
                    { Debug.LogError($"FAIL clipboard: depths {copied[0].Depth}/{copied[1].Depth}, want 0/1 (relative)"); ok = false; }
                    if (copied[0].Id == blocks[1].Id || copied[1].Id == blocks[2].Id)
                    { Debug.LogError("FAIL clipboard: pasted blocks must get FRESH ids (I6)"); ok = false; }
                    if (copied[0].Kind != BlockKind.Item)
                    { Debug.LogError($"FAIL clipboard: kind came back as {copied[0].Kind}, want Item"); ok = false; }
                }
            }

            // Selecting a parent AND its child must not duplicate the child.
            payload = NotesDocOps.SerializeBlocks(blocks, new List<string> { blocks[1].Id, blocks[2].Id });
            NotesDocOps.TryDeserializeBlocks(payload, out copied);
            if (copied == null || copied.Count != 2)
            { Debug.LogError($"FAIL clipboard: overlapping selection produced {copied?.Count} blocks, want 2"); ok = false; }

            // Selection order follows the DOCUMENT, not the order ids were clicked in.
            payload = NotesDocOps.SerializeBlocks(blocks, new List<string> { blocks[3].Id, blocks[1].Id });
            NotesDocOps.TryDeserializeBlocks(payload, out copied);
            if (copied == null || copied.Count != 3 || copied[0].Text != "a" || copied[2].Text != "b")
            { Debug.LogError($"FAIL clipboard: got [{Dump(copied)}], want [a/a1/b] in document order"); ok = false; }

            // Text carrying tabs and newlines survives, or a multi-line row would corrupt the payload.
            var tricky = Sheet();
            tricky[1].Text = "строка\tс табом\nи переносом";
            tricky[1].Detail = "деталь\nв две строки";
            payload = NotesDocOps.SerializeBlocks(tricky, new List<string> { tricky[1].Id });
            NotesDocOps.TryDeserializeBlocks(payload, out copied);
            if (copied == null || copied.Count < 1 || copied[0].Text != "строка\tс табом\nи переносом")
            { Debug.LogError($"FAIL clipboard: escaped text came back as «{(copied != null && copied.Count > 0 ? copied[0].Text : "<none>")}»"); ok = false; }
            else if (copied[0].Detail != "деталь\nв две строки")
            { Debug.LogError($"FAIL clipboard: detail came back as «{copied[0].Detail}»"); ok = false; }

            // A picture survives the trip byte-for-byte — silently dropping it from a copy would be a nasty
            // surprise, so it is base64'd into the payload rather than skipped.
            var withImage = Sheet();
            var img = NotesDocOps.NewBlock(BlockKind.Image, 1);
            img.ImageBytes = new byte[] { 137, 80, 0, 255, 42 };
            img.DisplayHeight = 123.5f;
            NotesDocOps.Insert(withImage, 2, img);
            payload = NotesDocOps.SerializeBlocks(withImage, new List<string> { img.Id });
            NotesDocOps.TryDeserializeBlocks(payload, out copied);
            var backImg = copied != null ? copied.Find(b => b.Kind == BlockKind.Image) : null;
            if (backImg == null || backImg.ImageBytes == null || backImg.ImageBytes.Length != 5 || backImg.ImageBytes[3] != 255)
            { Debug.LogError("FAIL clipboard: image bytes did not survive the payload"); ok = false; }
            else if (System.Math.Abs(backImg.DisplayHeight - 123.5f) > 0.001f)
            { Debug.LogError($"FAIL clipboard: DisplayHeight came back {backImg.DisplayHeight}, want 123.5 — check invariant-culture formatting"); ok = false; }

            // Junk must be refused, not half-parsed and not thrown out of.
            foreach (string junk in new[] { "", "не json вообще", "NOTESBLOCKS/1\nItem" })
                if (NotesDocOps.TryDeserializeBlocks(junk, out _))
                { Debug.LogError($"FAIL clipboard: «{junk}» must be refused"); ok = false; }

            // The system clipboard gets plain indented text, so a copied block can go straight into Discord.
            string text = NotesDocOps.ToIndentedText(blocks, new List<string> { blocks[1].Id });
            if (text != "a\n  a1")
            { Debug.LogError($"FAIL clipboard: indented text «{text.Replace("\n", "\\n")}», want «a\\n  a1»"); ok = false; }

            Debug.Log(ok ? "Self-Test Block Clipboard: PASS" : "Self-Test Block Clipboard: FAIL");
        }

        // SelfTestHints (NotesDocOps.HintFor/SectionHints) removed in Task 10f: the hint text it rendered was
        // placeholder copy for the Lazy-DM scaffold's titles, keyed by section Title. With the scaffold gone
        // there is no title set left to key from, so HintFor would always return null — an always-empty
        // lookup kept "for completeness" is the exact shape this branch keeps paying for (see the brief).
        // DocBlockView's hint Text/GameObject and DocumentPageView's HintFor call went with it.

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
            // CreateSessionSheet stopped seeding sections in Task 10f — this fixture adds the one section its
            // own line/card need to hang under, the same way a DM would type it in by hand.
            sheet.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "Важные NPC"));

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
            // Each sheet needs a Section to hang its NPC row under — CreateSessionSheet no longer seeds one
            // (Task 10f), so this fixture makes its own rather than relying on the retired scaffold.
            s1.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "Важные NPC"));
            s3.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "Важные NPC"));

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

        // ── Task 10b: EnsurePageFor / FindPageBoundTo ─────────────────────────────

        [ContextMenu("Self-Test: Ensure Page For World Object")]
        public void SelfTestEnsurePageFor()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var sessions = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(sessions);

            var bound = new WorldRef { Kind = WorldRefKind.Poi, Id = "poi-1" };
            string firstId = NotesDocOps.EnsurePageFor(doc, bound, "Тихий Брод");

            var refGroup = doc.Groups.Find(x => x.IsReference);
            if (firstId == null || refGroup == null || !refGroup.Pages.Exists(p => p.Id == firstId))
            { Debug.LogError("FAIL ensurepage: a fresh call must create a page in the reference group"); ok = false; }

            var created = NotesDocOps.FindPage(doc, firstId);
            if (created == null || created.Name != "Тихий Брод")
            { Debug.LogError($"FAIL ensurepage: page name «{created?.Name}», want «Тихий Брод» (E3)"); ok = false; }
            if (created != null && created.Kind != PageKind.Document)
            { Debug.LogError($"FAIL ensurepage: page Kind = {created?.Kind}, want Document — it defaults to Board (NotesData.cs) and must be said explicitly"); ok = false; }
            if (created != null && (created.Bound == null || created.Bound.Kind != WorldRefKind.Poi || created.Bound.Id != "poi-1"))
            { Debug.LogError("FAIL ensurepage: created page's Bound must carry Kind=Poi, Id=«poi-1»"); ok = false; }
            // E2 — Bound is a COPY of `bound`, not the same instance: mutating the caller's WorldRef after
            // the call must not reach into stored state through a shared reference.
            if (created != null && ReferenceEquals(created.Bound, bound))
            { Debug.LogError("FAIL ensurepage: Bound must be a COPY of the caller's WorldRef, not the same instance (E2)"); ok = false; }
            if (created != null)
            {
                bound.Id = "mutated-after-the-fact";
                if (created.Bound.Id != "poi-1")
                { Debug.LogError($"FAIL ensurepage: mutating the caller's WorldRef after the call changed the stored Bound to «{created.Bound.Id}» (E2)"); ok = false; }
                bound.Id = "poi-1";   // restore for the calls below
            }

            var problems = NotesDocOps.Validate(doc);
            if (problems.Count != 0)
            { Debug.LogError($"FAIL ensurepage: a freshly created page must be valid, got: {string.Join("; ", problems)}"); ok = false; }

            // E1 — the load-bearing rule: calling AGAIN for the SAME world object returns the SAME id and
            // creates NOTHING. Asserting the page COUNT, not just the returned id — an implementation that
            // creates a duplicate and happens to return the new id would still pass an id-only check.
            int pagesBefore = refGroup != null ? refGroup.Pages.Count : -1;
            string secondId = NotesDocOps.EnsurePageFor(doc, bound, "A different title entirely");
            int pagesAfter = refGroup != null ? refGroup.Pages.Count : -1;
            if (secondId != firstId)
            { Debug.LogError($"FAIL ensurepage: second call returned «{secondId}», want the same id «{firstId}» (E1)"); ok = false; }
            if (pagesAfter != pagesBefore)
            { Debug.LogError($"FAIL ensurepage: reference group held {pagesBefore} page(s) before, {pagesAfter} after — a matching Bound must create NOTHING (E1)"); ok = false; }

            // E3 — an empty/whitespace title falls back to «Без названия» rather than a nameless row.
            string blankId = NotesDocOps.EnsurePageFor(doc, new WorldRef { Kind = WorldRefKind.Poi, Id = "poi-2" }, "   ");
            var blankPage = NotesDocOps.FindPage(doc, blankId);
            if (blankPage == null || blankPage.Name != "Без названия")
            { Debug.LogError($"FAIL ensurepage: blank-title page name «{blankPage?.Name}», want «Без названия» (E3)"); ok = false; }

            // E4 — a null doc or a null bound creates nothing and returns null. Both calls wrapped in
            // try/catch, a deliberate departure from this file's usual bare-if idiom: a mutant that dropped
            // just the `bound == null` half of the guard would NRE on `bound.Kind` a few lines further in
            // (FindPageBoundTo) rather than return null, and an uncaught exception here would fail the whole
            // suite with a bare stack trace instead of a message naming E4.
            try
            {
                if (NotesDocOps.EnsurePageFor(null, bound, "X") != null)
                { Debug.LogError("FAIL ensurepage: a null doc must return null (E4)"); ok = false; }
            }
            catch (System.Exception ex)
            { Debug.LogError($"FAIL ensurepage: EnsurePageFor(null, bound, ...) threw {ex.GetType().Name}, want a null return (E4)"); ok = false; }
            try
            {
                if (NotesDocOps.EnsurePageFor(doc, null, "X") != null)
                { Debug.LogError("FAIL ensurepage: a null bound must return null (E4)"); ok = false; }
            }
            catch (System.Exception ex)
            { Debug.LogError($"FAIL ensurepage: EnsurePageFor(doc, null, ...) threw {ex.GetType().Name}, want a null return (E4)"); ok = false; }
            int pagesAfterNullAttempts = doc.Groups.Find(x => x.IsReference)?.Pages.Count ?? -1;
            if (pagesAfterNullAttempts != pagesAfter + 1)   // +1 for the E3 blank-title page just added
            { Debug.LogError("FAIL ensurepage: a refused (null doc / null bound) call must create nothing (E4)"); ok = false; }

            // E5 — the SAME reference-group INSTANCE is reused across calls, not a second one filed alongside
            // it. Identity, not just count: a mutant that filed a new page straight into doc.Groups[0] would
            // still leave exactly one IsReference group and pass a count-only check.
            var refGroupsNow = doc.Groups.FindAll(x => x.IsReference);
            if (refGroupsNow.Count != 1 || !ReferenceEquals(refGroupsNow[0], refGroup))
            { Debug.LogError("FAIL ensurepage: every created page must land in the SAME reference-group instance (E5)"); ok = false; }

            // E5, the "created when absent" half: a document with NO reference group yet still gets exactly
            // one, on first use.
            var freshDoc = new NotesDocument();
            freshDoc.Groups.Add(new PageGroup { Title = "Сессии" });
            string freshId = NotesDocOps.EnsurePageFor(freshDoc, new WorldRef { Kind = WorldRefKind.Poi, Id = "poi-3" }, "Форт");
            var freshRefGroups = freshDoc.Groups.FindAll(x => x.IsReference);
            if (freshId == null || freshRefGroups.Count != 1)
            { Debug.LogError($"FAIL ensurepage: a document with no reference group must get exactly one on first use, got {freshRefGroups.Count} (E5)"); ok = false; }

            // FindPageBoundTo itself: the shared predicate EnsurePageFor (E1) and QuickOpen's W4 both defer
            // to. An unmatched kind/id must return null, not the wrong page.
            if (NotesDocOps.FindPageBoundTo(doc, WorldRefKind.Poi, "no-such-poi") != null)
            { Debug.LogError("FAIL findboundto: an unmatched (kind, id) must return null"); ok = false; }
            if (NotesDocOps.FindPageBoundTo(doc, WorldRefKind.Building, "poi-1") != null)
            { Debug.LogError("FAIL findboundto: matching Id but the WRONG Kind must still return null — Kind is part of the identity"); ok = false; }
            if (NotesDocOps.FindPageBoundTo(doc, WorldRefKind.Poi, "poi-1") != created)
            { Debug.LogError("FAIL findboundto: a matching (kind, id) must return the bound page"); ok = false; }

            Debug.Log(ok ? "Self-Test Ensure Page For World Object: PASS" : "Self-Test Ensure Page For World Object: FAIL");
        }
    }
}
