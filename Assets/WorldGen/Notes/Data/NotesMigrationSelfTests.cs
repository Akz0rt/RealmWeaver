using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// The v13 → v14 migration: a page that WAS a board becomes a page that HAS one.
    ///
    /// The fixture is built so that a working transfer and no transfer at all give DIFFERENT answers — an
    /// empty board cannot tell them apart, which is why there are two cards, a link between them, a non-zero
    /// pan and a zoom that is not 1, and a second page whose BoardRef row points at the first.
    ///
    /// Runs offline via Tools/notes-harness (`powershell -File sync.ps1`, then `dotnet run -c Release --
    /// selftests` from bash) or by right-clicking this component in the Editor.
    /// </summary>
    public class NotesMigrationSelfTests : MonoBehaviour
    {
        /// <summary>A document exactly as format 13 wrote it: one board page carrying objects on the PAGE,
        /// and one document page holding a BoardRef row that points at it.</summary>
        static NotesDocument LegacyDocument(out NotesPage board, out NotesPage sheet, out DocBlock boardRef)
        {
            var doc = new NotesDocument();
            var group = new PageGroup { Title = "Заметки" };

            var a = new NoteCardData { Title = "Ольга", Body = "хозяйка таверны" };
            a.Position = new System.Numerics.Vector2(-60f, 40f);
            var b = new NoteCardData { Title = "Гильдия", Body = "должна ей денег" };
            b.Position = new System.Numerics.Vector2(120f, -30f);

            board = new NotesPage { Name = "Связи в городе", Kind = PageKind.Board };
            // Assigned, not .Add()ed: step 5 of this task drops the fields' `= new List<>()` initializers, and
            // assignment is the form that compiles on both sides of that change.
            board.Objects = new List<CanvasObjectData> { a, b };
            board.Links = new List<LinkData> { new LinkData { FromObjectId = a.Id, ToObjectId = b.Id } };
            board.CameraPan = new System.Numerics.Vector2(-15f, 33f);
            board.CameraZoom = 1.75f;

            sheet = new NotesPage { Name = "Сессия 1", Kind = PageKind.Document };
            sheet.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "Сцены"));
            boardRef = NotesDocOps.NewBlock(BlockKind.BoardRef, 1);
            boardRef.LinkedPageId = board.Id;
            sheet.Blocks.Add(boardRef);

            group.Pages.Add(board);
            group.Pages.Add(sheet);
            doc.Groups.Add(group);
            return doc;
        }

        [ContextMenu("Self-Test: Board Page Migration")]
        public void SelfTestBoardPageMigration()
        {
            bool ok = true;
            var doc = LegacyDocument(out var board, out var sheet, out var boardRef);
            string cardAId = ((NoteCardData)board.Objects[0]).Id;

            NotesDocOps.Normalize(doc);

            // Форма мигрированной страницы: секция с именем страницы, затем доска.
            if (board.Blocks.Count != 2)
            { Debug.LogError($"FAIL migration: the board page has {board.Blocks.Count} blocks, want 2 (a Section then a Canvas)"); ok = false; }
            else
            {
                if (board.Blocks[0].Kind != BlockKind.Section || board.Blocks[0].Text != "Связи в городе")
                { Debug.LogError($"FAIL migration: first block is {board.Blocks[0].Kind} «{board.Blocks[0].Text}», want Section «Связи в городе» (I2)"); ok = false; }
                if (board.Blocks[1].Kind != BlockKind.Canvas || board.Blocks[1].Text != "Связи в городе")
                { Debug.LogError($"FAIL migration: second block is {board.Blocks[1].Kind} «{board.Blocks[1].Text}», want Canvas «Связи в городе»"); ok = false; }
            }

            var canvas = board.Blocks.Count == 2 ? board.Blocks[1] : null;

            // Содержимое доехало ЦЕЛИКОМ — карточки, связь, пан и зум.
            if (canvas == null || canvas.CanvasObjects == null || canvas.CanvasObjects.Count != 2)
            { Debug.LogError($"FAIL migration: the canvas holds {canvas?.CanvasObjects?.Count} objects, want 2"); ok = false; }
            else if (canvas.CanvasObjects[0].Id != cardAId || ((NoteCardData)canvas.CanvasObjects[0]).Title != "Ольга")
            { Debug.LogError("FAIL migration: the first card lost its identity or its title"); ok = false; }
            if (canvas == null || canvas.CanvasLinks == null || canvas.CanvasLinks.Count != 1)
            { Debug.LogError($"FAIL migration: the canvas holds {canvas?.CanvasLinks?.Count} links, want 1"); ok = false; }
            if (canvas == null || canvas.CanvasPan.X != -15f || canvas.CanvasPan.Y != 33f)
            { Debug.LogError($"FAIL migration: pan = {canvas?.CanvasPan}, want (-15, 33) — an empty board could not tell this mutant apart"); ok = false; }
            if (canvas == null || canvas.CanvasZoom != 1.75f)
            { Debug.LogError($"FAIL migration: zoom = {canvas?.CanvasZoom}, want 1.75"); ok = false; }

            // Страница больше ничего не держит — иначе второй прогон мигрировал бы её снова.
            if (board.Objects != null || board.Links != null)
            { Debug.LogError($"FAIL migration: the PAGE still carries objects/links ({board.Objects?.Count}/{board.Links?.Count})"); ok = false; }
            if (board.CameraZoom != 1f || board.CameraPan != default)
            { Debug.LogError($"FAIL migration: the PAGE still carries a camera ({board.CameraPan}/{board.CameraZoom})"); ok = false; }
            if (board.Name != "Связи в городе")
            { Debug.LogError($"FAIL migration: the page was renamed to «{board.Name}»"); ok = false; }

            // BoardRef деградировал в обычную строку с именем доски.
            if (boardRef.Kind != BlockKind.Item)
            { Debug.LogError($"FAIL migration: the BoardRef is still {boardRef.Kind}, want Item"); ok = false; }
            if (boardRef.Text != "Связи в городе")
            { Debug.LogError($"FAIL migration: the degraded row says «{boardRef.Text}», want the board's name"); ok = false; }
            if (!string.IsNullOrEmpty(boardRef.LinkedPageId))
            { Debug.LogError("FAIL migration: the degraded row kept its LinkedPageId"); ok = false; }
            if (sheet.Blocks.Count != 2)
            { Debug.LogError($"FAIL migration: the sheet has {sheet.Blocks.Count} blocks, want the 2 it started with"); ok = false; }

            var problems = NotesDocOps.Validate(doc);
            if (problems.Count != 0)
            { Debug.LogError($"FAIL migration: the migrated document is invalid: {string.Join("; ", problems)}"); ok = false; }

            Debug.Log(ok ? "Self-Test Board Page Migration: PASS" : "Self-Test Board Page Migration: FAIL");
        }

        /// <summary>Normalize is UNGATED by version, so it runs on every load of an already-migrated file. A
        /// second run must change NOTHING — the mutant this kills inserts a second Section and a second, empty
        /// canvas every time the DM opens their project.</summary>
        [ContextMenu("Self-Test: Migration Is Idempotent")]
        public void SelfTestMigrationIsIdempotent()
        {
            bool ok = true;
            var doc = LegacyDocument(out var board, out _, out _);

            NotesDocOps.Normalize(doc);
            int blocksAfterFirst = board.Blocks.Count;
            int objectsAfterFirst = board.Blocks[1].CanvasObjects.Count;
            string shapeAfterFirst = board.Blocks[0].Kind + "/" + board.Blocks[1].Kind;

            NotesDocOps.Normalize(doc);
            NotesDocOps.Normalize(doc);

            if (board.Blocks.Count != blocksAfterFirst)
            { Debug.LogError($"FAIL idempotence: {board.Blocks.Count} blocks after three runs, want {blocksAfterFirst} — every load would grow the page"); ok = false; }
            string shapeNow = board.Blocks[0].Kind + "/" + board.Blocks[1].Kind;
            if (shapeNow != shapeAfterFirst)
            { Debug.LogError($"FAIL idempotence: shape {shapeNow}, want {shapeAfterFirst}"); ok = false; }
            if (board.Blocks[1].CanvasObjects.Count != objectsAfterFirst)
            { Debug.LogError($"FAIL idempotence: {board.Blocks[1].CanvasObjects.Count} objects, want {objectsAfterFirst}"); ok = false; }

            Debug.Log(ok ? "Self-Test Migration Is Idempotent: PASS" : "Self-Test Migration Is Idempotent: FAIL");
        }
    }
}
