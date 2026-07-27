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
    }
}
