using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Exhaustive tests for the keyboard RULES. What these cannot judge — whether the caret visually lands
    /// where it should, whether uGUI actually moves focus, and how it feels to type — is what the user
    /// checkpoint exists for. Everything that is a rule rather than a feeling lives here.
    /// </summary>
    public class DocKeyboardOpsSelfTests : MonoBehaviour
    {
        static List<DocBlock> Page() => new List<DocBlock>
        {
            NotesDocOps.NewBlock(BlockKind.Section, 0, "Важные NPC"),
            NotesDocOps.NewBlock(BlockKind.Item, 1, "Ольга"),
            NotesDocOps.NewBlock(BlockKind.Item, 2, "хочет скрыть ящик"),
            NotesDocOps.NewBlock(BlockKind.Item, 1, "Хель"),
        };

        static string Dump(IReadOnlyList<DocBlock> blocks)
        {
            var parts = new List<string>();
            foreach (var b in blocks) parts.Add($"{b.Text}@{b.Depth}");
            return string.Join("/", parts);
        }

        [ContextMenu("Self-Test: Keyboard Enter")]
        public void SelfTestEnter()
        {
            bool ok = true;

            // At the end of a SECTION heading, Enter starts a row UNDER it — the DM wants to list, not to open
            // a second heading. This is the one case where the new block's kind differs from the old one's.
            var blocks = Page();
            var r = DocKeyboardOps.Apply(blocks, blocks[0].Id, "Важные NPC".Length, false, false, DocKey.Enter);
            var created = blocks.Find(b => b.Id == r.FocusBlockId);
            if (!r.Handled || !r.Rebuild || created == null)
            { Debug.LogError("FAIL enter: Enter at the end of a section must create and focus a row"); ok = false; }
            else
            {
                if (created.Kind != BlockKind.Item || created.Depth != 1)
                { Debug.LogError($"FAIL enter: created {created.Kind}@{created.Depth}, want Item@1"); ok = false; }
                if (blocks.IndexOf(created) != 1)
                { Debug.LogError($"FAIL enter: new row sits at {blocks.IndexOf(created)}, want 1 (directly under the heading)"); ok = false; }
                if (r.CaretOffset != 0)
                { Debug.LogError($"FAIL enter: caret {r.CaretOffset}, want 0"); ok = false; }
            }

            // Mid-heading, both halves stay headings — splitting a title must not silently demote one half.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[0].Id, 6, false, false, DocKey.Enter);
            if (blocks[0].Text != "Важные" || blocks[0].Kind != BlockKind.Section)
            { Debug.LogError($"FAIL enter: first half «{blocks[0].Text}» ({blocks[0].Kind}), want «Важные» (Section)"); ok = false; }
            var half = blocks.Find(b => b.Id == r.FocusBlockId);
            if (half == null || half.Kind != BlockKind.Section || half.Text != " NPC")
            { Debug.LogError($"FAIL enter: second half «{half?.Text}» ({half?.Kind}), want « NPC» (Section)"); ok = false; }

            // A row WITH children: the new sibling lands after the whole subtree, so the children stay with the
            // row they were written under instead of silently re-parenting to the new one.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[1].Id, "Ольга".Length, false, false, DocKey.Enter);
            if (Dump(blocks) != "Важные NPC@0/Ольга@1/хочет скрыть ящик@2/@1/Хель@1")
            { Debug.LogError($"FAIL enter: got [{Dump(blocks)}], want the new row AFTER «хочет скрыть ящик»"); ok = false; }

            // A picture has no caret; Enter still starts a row under it rather than doing nothing.
            blocks = Page();
            var img = NotesDocOps.NewBlock(BlockKind.Image, 1);
            NotesDocOps.Insert(blocks, 1, img);
            r = DocKeyboardOps.Apply(blocks, img.Id, 0, false, false, DocKey.Enter);
            created = blocks.Find(b => b.Id == r.FocusBlockId);
            if (created == null || created.Kind != BlockKind.Item || blocks.IndexOf(created) != 2)
            { Debug.LogError("FAIL enter: Enter on an image must insert a row directly below it"); ok = false; }

            // A BOARD'S CAPTION, mid-word. Three different wrong answers are ruled out by one fixture, which is
            // why the caret is put in the MIDDLE of a caption on a board that HOLDS something:
            //   • propagating the kind (the rule for every other row) → a second Canvas;
            //   • splitting the caption (the rule for mid-text Enter) → caption «До» and a second board;
            //   • both at once, which is what the code did before the Р4 checkpoint.
            // The DM reported it as «после создания доски все последующие Enter создают доски».
            blocks = Page();
            var board = NotesDocOps.NewBlock(BlockKind.Canvas, 1, "Доска");
            board.CanvasObjects = new List<CanvasObjectData> { new NoteCardData { Title = "Гарет" } };
            NotesDocOps.Insert(blocks, 1, board);
            r = DocKeyboardOps.Apply(blocks, board.Id, 2, false, false, DocKey.Enter);
            created = blocks.Find(b => b.Id == r.FocusBlockId);
            if (created == null || created.Kind != BlockKind.Item)
            { Debug.LogError($"FAIL enter: Enter in a board's caption created {created?.Kind}, want Item — a board must never beget a board"); ok = false; }
            if (blocks.IndexOf(created) != 2)
            { Debug.LogError($"FAIL enter: the new row sits at {blocks.IndexOf(created)}, want 2 (directly after the board)"); ok = false; }
            if (board.Text != "Доска")
            { Debug.LogError($"FAIL enter: the caption became «{board.Text}» — Enter must not split a caption"); ok = false; }
            int boardCount = 0;
            foreach (var b2 in blocks) if (b2.Kind == BlockKind.Canvas) boardCount++;
            if (boardCount != 1)
            { Debug.LogError($"FAIL enter: the page holds {boardCount} boards, want 1"); ok = false; }
            if (board.CanvasObjects == null || board.CanvasObjects.Count != 1)
            { Debug.LogError("FAIL enter: the board lost what was on it"); ok = false; }

            Debug.Log(ok ? "Self-Test Keyboard Enter: PASS" : "Self-Test Keyboard Enter: FAIL");
        }

        [ContextMenu("Self-Test: Keyboard Backspace")]
        public void SelfTestBackspace()
        {
            bool ok = true;

            // Anywhere but offset 0 this is ordinary text editing and must NOT be consumed, or typing breaks.
            var blocks = Page();
            var r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 2, false, false, DocKey.Backspace);
            if (r.Handled)
            { Debug.LogError("FAIL backspace: mid-word Backspace must fall through to the input field"); ok = false; }
            if (Dump(blocks) != "Важные NPC@0/Ольга@1/хочет скрыть ящик@2/Хель@1")
            { Debug.LogError($"FAIL backspace: mid-word Backspace changed the list to [{Dump(blocks)}]"); ok = false; }

            // At offset 0 with a mergeable row above: join, and the caret lands where the two texts meet.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 0, false, false, DocKey.Backspace);
            if (!r.Handled || !r.Rebuild)
            { Debug.LogError("FAIL backspace: a real merge must be handled and force a rebuild"); ok = false; }
            if (Dump(blocks) != "Важные NPC@0/Ольга@1/хочет скрыть ящикХель@2")
            { Debug.LogError($"FAIL backspace: got [{Dump(blocks)}], want «Хель» merged into the row above"); ok = false; }
            if (r.CaretOffset != "хочет скрыть ящик".Length)
            { Debug.LogError($"FAIL backspace: caret {r.CaretOffset}, want {"хочет скрыть ящик".Length} (the join)"); ok = false; }

            // The first row under a heading has nothing to merge into — the heading must never be eaten.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[1].Id, 0, false, false, DocKey.Backspace);
            if (r.Handled)
            { Debug.LogError("FAIL backspace: merging the first row into its heading must not be handled"); ok = false; }
            if (blocks.Count != 4)
            { Debug.LogError($"FAIL backspace: the list changed to [{Dump(blocks)}]"); ok = false; }

            // Text cannot merge into a picture, so the first press SELECTS it and changes nothing...
            blocks = Page();
            var img = NotesDocOps.NewBlock(BlockKind.Image, 1);
            NotesDocOps.Insert(blocks, 3, img);
            r = DocKeyboardOps.Apply(blocks, blocks[4].Id, 0, false, false, DocKey.Backspace);
            if (!r.Handled || r.SelectBlockId != img.Id)
            { Debug.LogError($"FAIL backspace: want the image SELECTED, got SelectBlockId «{r.SelectBlockId}»"); ok = false; }
            if (r.Rebuild || blocks.Count != 5)
            { Debug.LogError("FAIL backspace: selecting a picture must not modify the list"); ok = false; }

            // ...and the second press, now focused on the picture, deletes it.
            r = DocKeyboardOps.Apply(blocks, img.Id, 0, false, false, DocKey.Backspace);
            if (!r.Handled || !r.Rebuild || blocks.Count != 4)
            { Debug.LogError($"FAIL backspace: a focused picture must be deleted, list is [{Dump(blocks)}]"); ok = false; }
            if (r.FocusBlockId == null)
            { Debug.LogError("FAIL backspace: focus must fall back to the row above a deleted picture"); ok = false; }

            // The very first block of a page has nothing above it.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[0].Id, 0, false, false, DocKey.Backspace);
            if (r.Handled)
            { Debug.LogError("FAIL backspace: the first block of a page must not be handled"); ok = false; }

            Debug.Log(ok ? "Self-Test Keyboard Backspace: PASS" : "Self-Test Keyboard Backspace: FAIL");
        }

        [ContextMenu("Self-Test: Keyboard Tab and Arrows")]
        public void SelfTestTabAndArrows()
        {
            bool ok = true;

            // Tab indents, Shift+Tab puts it back.
            var blocks = Page();
            var r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 0, false, false, DocKey.Tab);
            if (!r.Handled || !r.Rebuild || blocks[3].Depth != 2)
            { Debug.LogError($"FAIL tab: «Хель» is at depth {blocks[3].Depth}, want 2"); ok = false; }
            r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 0, false, false, DocKey.ShiftTab);
            if (!r.Handled || blocks[3].Depth != 1)
            { Debug.LogError($"FAIL tab: after Shift+Tab depth is {blocks[3].Depth}, want 1"); ok = false; }

            // A refused Tab is still CONSUMED — otherwise Unity's own focus navigation grabs it and the caret
            // jumps to some unrelated widget, which reads as the editor being broken.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[0].Id, 0, false, false, DocKey.Tab);
            if (!r.Handled)
            { Debug.LogError("FAIL tab: Tab on a section must be consumed even though it cannot indent"); ok = false; }
            if (r.Rebuild || blocks[0].Depth != 0)
            { Debug.LogError("FAIL tab: a refused Tab must not change anything"); ok = false; }

            // Arrows only act at the edge of the field; in the middle of a wrapped paragraph the field keeps them.
            blocks = Page();
            r = DocKeyboardOps.Apply(blocks, blocks[2].Id, 3, false, false, DocKey.Up);
            if (r.Handled)
            { Debug.LogError("FAIL arrows: Up away from the first visual line must fall through"); ok = false; }

            // The same rule from the other side. Pinned separately from the Up case above because atFirstLine
            // and atLastLine gate two different branches of Apply, and because this is now the ONLY thing
            // keeping the Down half of the rule alive: DocKeyboardController narrowed the gesture to rows that
            // are a single visual line, so the view passes true for both flags and can no longer exercise
            // either false branch in production. Non-vacuous — blocks[3] «Хель» is below blocks[2], so an
            // atLastLine Down here really would move.
            r = DocKeyboardOps.Apply(blocks, blocks[2].Id, 3, false, false, DocKey.Down);
            if (r.Handled)
            { Debug.LogError("FAIL arrows: Down away from the last visual line must fall through"); ok = false; }

            // At the first line it moves to the previous row, keeping the caret column where it fits.
            r = DocKeyboardOps.Apply(blocks, blocks[2].Id, 3, true, false, DocKey.Up);
            if (!r.Handled || r.FocusBlockId != blocks[1].Id)
            { Debug.LogError("FAIL arrows: Up must move focus to the row above"); ok = false; }
            if (r.CaretOffset != 3)
            { Debug.LogError($"FAIL arrows: caret {r.CaretOffset}, want 3 (same column)"); ok = false; }

            // Moving into a SHORTER row clamps the column to that row's length instead of overshooting.
            r = DocKeyboardOps.Apply(blocks, blocks[2].Id, 15, true, false, DocKey.Up);
            if (r.CaretOffset != "Ольга".Length)
            { Debug.LogError($"FAIL arrows: caret {r.CaretOffset}, want {"Ольга".Length} (clamped)"); ok = false; }

            // Rows hidden inside a collapsed section are SKIPPED — arrowing into something invisible is a bug.
            blocks = new List<DocBlock>
            {
                NotesDocOps.NewBlock(BlockKind.Section, 0, "S0"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "спрятана"),
                NotesDocOps.NewBlock(BlockKind.Section, 0, "S1"),
                NotesDocOps.NewBlock(BlockKind.Item, 1, "видна"),
            };
            blocks[0].Collapsed = true;
            r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 0, true, false, DocKey.Up);
            if (r.FocusBlockId != blocks[2].Id)
            { Debug.LogError("FAIL arrows: Up must skip rows hidden by a collapsed section and land on «S1»"); ok = false; }
            r = DocKeyboardOps.Apply(blocks, blocks[2].Id, 0, true, false, DocKey.Up);
            if (r.FocusBlockId != blocks[0].Id)
            { Debug.LogError("FAIL arrows: Up from «S1» must land on «S0», not on its hidden child"); ok = false; }

            // Nothing below the last visible row.
            r = DocKeyboardOps.Apply(blocks, blocks[3].Id, 0, false, true, DocKey.Down);
            if (r.Handled)
            { Debug.LogError("FAIL arrows: Down at the last visible row must fall through"); ok = false; }

            Debug.Log(ok ? "Self-Test Keyboard Tab and Arrows: PASS" : "Self-Test Keyboard Tab and Arrows: FAIL");
        }
    }
}
