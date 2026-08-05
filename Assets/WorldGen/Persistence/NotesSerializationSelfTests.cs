using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Rendering.RegionLabels;

namespace WorldGen.Persistence
{
    /// <summary>
    /// Self-tests for persisting document pages. A SEPARATE file from ProjectSerializerSelfTests on purpose:
    /// that one is 900+ lines and is edited by other work in flight, so appending here avoids a merge fight
    /// over a shared file.
    ///
    /// These two cannot run in Tools/notes-harness — they need real Newtonsoft and Application.temporaryCachePath,
    /// while the harness only stubs Newtonsoft's attributes. They run in the Editor at the scene-wiring task.
    /// The parts that CAN be proved offline (that an absent Kind key means Board, and that Normalize survives
    /// a null Blocks list) are pinned in NotesDocOpsSelfTests instead, so the risky claim — "nothing the DM
    /// already wrote is lost" — is not resting on a single manual check.
    /// </summary>
    public class NotesSerializationSelfTests : MonoBehaviour
    {
        static void SaveNotesOnly(string path, NotesDocument notes) =>
            ProjectSerializer.Save(path,
                new GenerationParams { Seed = 1, Width = 10f, Height = 10f },
                new List<VoronoiCell>(), new List<PoiData>(), notes,
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());

        [ContextMenu("Self-Test: Notes Document Round-Trip")]
        public void SelfTestDocumentRoundTrip()
        {
            bool ok = true;

            var page = NotesDocOps.CreateSessionSheet("Сессия 1");
            var img = NotesDocOps.NewBlock(BlockKind.Image, 1);
            img.ImageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 255, 42 };
            img.DisplayHeight = 180f;
            page.Blocks.Add(img);

            var doc = new NotesDocument();
            var group = new PageGroup { Title = "Сессии" };
            group.Pages.Add(page);
            doc.Groups.Add(group);
            var reference = NotesDocOps.EnsureReferenceGroup(doc);

            string path = Path.Combine(Application.temporaryCachePath, "notes-roundtrip.dndproj");
            SaveNotesOnly(path, doc);
            var loaded = ProjectSerializer.Load(path);

            if (!loaded.Success)
            { Debug.LogError($"FAIL round-trip: load failed — {loaded.ErrorMessage}"); ok = false; }

            var back = loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].Pages.Count > 0
                ? loaded.Notes.Groups[0].Pages[0] : null;
            if (back == null)
            { Debug.LogError("FAIL round-trip: no page came back"); ok = false; }
            else
            {
                if (back.Blocks.Count != page.Blocks.Count)
                { Debug.LogError($"FAIL round-trip: {back.Blocks.Count} blocks, want {page.Blocks.Count}"); ok = false; }
                else
                    for (int i = 0; i < back.Blocks.Count; i++)
                    {
                        if (back.Blocks[i].Id != page.Blocks[i].Id)
                        { Debug.LogError($"FAIL round-trip: block {i} Id changed — session state will reference blocks by id"); ok = false; break; }
                        if (back.Blocks[i].Kind != page.Blocks[i].Kind || back.Blocks[i].Depth != page.Blocks[i].Depth)
                        { Debug.LogError($"FAIL round-trip: block {i} Kind/Depth changed"); ok = false; break; }
                        if (back.Blocks[i].Text != page.Blocks[i].Text)
                        { Debug.LogError($"FAIL round-trip: block {i} text «{back.Blocks[i].Text}» != «{page.Blocks[i].Text}»"); ok = false; break; }
                    }

                var backImg = back.Blocks.Find(b => b.Kind == BlockKind.Image);
                if (backImg == null || backImg.ImageBytes == null || backImg.ImageBytes.Length != 11
                    || backImg.ImageBytes[9] != 255 || backImg.ImageBytes[0] != 137)
                { Debug.LogError("FAIL round-trip: image bytes did not survive the base64 trip byte-for-byte"); ok = false; }
                else if (!Mathf.Approximately(backImg.DisplayHeight, 180f))
                { Debug.LogError($"FAIL round-trip: DisplayHeight = {backImg.DisplayHeight}, want 180"); ok = false; }
            }

            // The reference-group ROLE has to survive too, or promoted pages lose their home on reload.
            var backReference = loaded.Notes?.Groups?.Find(g => g.IsReference);
            if (backReference == null || backReference.Title != NotesDocOps.ReferenceGroupTitle)
            { Debug.LogError("FAIL round-trip: the reference group's IsReference flag did not survive"); ok = false; }
            if (reference == null)
            { Debug.LogError("FAIL round-trip: fixture did not create a reference group"); ok = false; }

            if (loaded.Notes != null)
            {
                var problems = NotesDocOps.Validate(loaded.Notes);
                if (problems.Count != 0)
                { Debug.LogError($"FAIL round-trip: loaded document is invalid: {string.Join("; ", problems)}"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Notes Document Round-Trip: PASS" : "Self-Test Notes Document Round-Trip: FAIL");
        }

        /// <summary>THE MIGRATION AT ITS REAL SEAM. NotesMigrationSelfTests pins what Normalize does to a
        /// document held in memory; only this test proves the two things that live outside the pure layer and
        /// so outside the offline harness — that Load actually CALLS Normalize, and that a page's board
        /// survives Newtonsoft's polymorphic reader on the way in. Before Р4 this test asserted the opposite
        /// («an old page comes back as the Board it is»), which was the right assertion while boards were a
        /// kind of page; it is rewritten rather than deleted because the file it reads is the same real
        /// format-9 file, and that file is the whole point.</summary>
        [ContextMenu("Self-Test: Old Format Board Migrates On Load")]
        public void SelfTestOldFormatBoardMigratesOnLoad()
        {
            bool ok = true;

            // A minimal pre-document file: its page carries neither Kind nor Blocks, and its group carries no
            // IsReference. Nothing the DM wrote may be lost — the card must arrive inside a Canvas block.
            // FormatVersion 9 is what this branch's base actually shipped.
            string json =
                "{ \"FormatVersion\": 9, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [], \"Pois\": [], \"RegionLabels\": [], " +
                "\"Notes\": { \"Groups\": [ { \"Title\": \"Заметки\", \"Pages\": [ " +
                // "Kind" is CanvasObjectDataConverter's own discriminator (it deliberately avoids Newtonsoft's
                // TypeNameHandling), and "NoteCard" is the value it writes for a NoteCardData.
                "{ \"Name\": \"Страница 1\", \"Objects\": [ { \"Kind\": \"NoteCard\", \"Title\": \"Гарет\", " +
                "\"Body\": \"должен денег\" } ], \"Links\": [] } ] } ] } }";

            string path = Path.Combine(Application.temporaryCachePath, "notes-preupgrade.dndproj");
            File.WriteAllText(path, json);

            var loaded = ProjectSerializer.Load(path);
            if (!loaded.Success)
            { Debug.LogError($"FAIL old format: the file must still load — {loaded.ErrorMessage}"); ok = false; }

            var page = loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].Pages.Count > 0
                ? loaded.Notes.Groups[0].Pages[0] : null;
            if (page == null)
            { Debug.LogError("FAIL old format: no page came back"); ok = false; }
            else
            {
                if (page.Name != "Страница 1")
                { Debug.LogError($"FAIL old format: name «{page.Name}» — existing content must survive"); ok = false; }
                if (page.Blocks == null || page.Blocks.Count != 2)
                { Debug.LogError($"FAIL old format: {page.Blocks?.Count} blocks, want 2 (a Section then a Canvas) — Load is where Normalize must be called"); ok = false; }
                else
                {
                    if (page.Blocks[0].Kind != BlockKind.Section)
                    { Debug.LogError($"FAIL old format: first block is {page.Blocks[0].Kind}, want Section (I2)"); ok = false; }
                    var canvas = page.Blocks[1];
                    if (canvas.Kind != BlockKind.Canvas)
                    { Debug.LogError($"FAIL old format: second block is {canvas.Kind}, want Canvas"); ok = false; }
                    else if (canvas.CanvasObjects == null || canvas.CanvasObjects.Count != 1)
                    { Debug.LogError($"FAIL old format: the canvas holds {canvas.CanvasObjects?.Count} objects, want 1 — the DM's card must not be dropped"); ok = false; }
                    else if (!(canvas.CanvasObjects[0] is NoteCardData card) || card.Title != "Гарет")
                    { Debug.LogError("FAIL old format: the card came back as the wrong type or lost its title"); ok = false; }
                }
                if (page.Objects != null || page.Links != null)
                { Debug.LogError("FAIL old format: the PAGE still carries its own board after the load"); ok = false; }
            }

            if (loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].IsReference)
            { Debug.LogError("FAIL old format: a pre-upgrade group must not come back flagged as the reference group"); ok = false; }

            if (loaded.Notes != null)
            {
                var problems = NotesDocOps.Validate(loaded.Notes);
                if (problems.Count != 0)
                { Debug.LogError($"FAIL old format: loaded document is invalid: {string.Join("; ", problems)}"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Old Format Board Migrates On Load: PASS" : "Self-Test Old Format Board Migrates On Load: FAIL");
        }

        /// <summary>Файл версии 14 не содержит ключей стиля вовсе, и это НЕ повод карточке
        /// измениться: отсутствующий ключ обязан читаться как нейтральная рамка и средний кегль.
        /// Проверяется здесь, а не в офлайн-харнессе, потому что харнесс не видит Persistence.</summary>
        [ContextMenu("Self-Test: Version 14 Card Gets Default Style")]
        public void SelfTestVersion14CardGetsDefaultStyle()
        {
            bool ok = true;

            string json =
                "{ \"FormatVersion\": 14, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [], \"Pois\": [], \"RegionLabels\": [], " +
                "\"Notes\": { \"Groups\": [ { \"Title\": \"Заметки\", \"Pages\": [ " +
                "{ \"Name\": \"Страница 1\", \"Blocks\": [ { \"Kind\": \"Canvas\", \"CanvasObjects\": [ " +
                "{ \"Kind\": \"NoteCard\", \"Title\": \"Старая\", \"Body\": \"текст\" } ] } ] } ] } ] } }";

            string path = Path.Combine(Application.temporaryCachePath, "notes-v14-style.dndproj");
            File.WriteAllText(path, json);

            var loaded = ProjectSerializer.Load(path);
            if (!loaded.Success)
            { Debug.LogError($"FAIL version 14: the file must still load — {loaded.ErrorMessage}"); ok = false; }

            // Блок ищется ПО ВИДУ, а не по индексу: Normalize вставляет Section перед доской.
            var page = loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].Pages.Count > 0
                ? loaded.Notes.Groups[0].Pages[0] : null;
            var canvas = page?.Blocks?.Find(b => b.Kind == BlockKind.Canvas);
            var card = canvas != null && canvas.CanvasObjects != null && canvas.CanvasObjects.Count > 0
                ? canvas.CanvasObjects[0] as NoteCardData : null;

            if (card == null)
            { Debug.LogError("FAIL version 14: the card did not come back"); ok = false; }
            else
            {
                if (card.FrameColorIndex != NotesPalette.NeutralIndex)
                { Debug.LogError($"FAIL version 14: frame index {card.FrameColorIndex}, want {NotesPalette.NeutralIndex} — an old card must look exactly as it did"); ok = false; }
                if (card.FontSize != CardFontSize.Medium)
                { Debug.LogError($"FAIL version 14: font size {card.FontSize}, want Medium"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Version 14 Card Gets Default Style: PASS" : "Self-Test Version 14 Card Gets Default Style: FAIL");
        }

        /// <summary>Обратная половина: выбранный ДМ стиль обязан пережить сохранение и загрузку.
        /// Значения нарочно НЕ нулевые — забытая строка в конвертере вернула бы ноль и осталась бы
        /// незамеченной на фикстуре со значениями по умолчанию.</summary>
        [ContextMenu("Self-Test: Card Style Round-Trip")]
        public void SelfTestCardStyleRoundTrip()
        {
            bool ok = true;

            var card = new NoteCardData
            {
                Title = "Засада",
                Body = "три гоблина",
                FrameColorIndex = 7,
                FontSize = CardFontSize.Large,
            };
            var canvas = NotesDocOps.NewBlock(BlockKind.Canvas, 1);
            canvas.CanvasObjects = new List<CanvasObjectData> { card };

            var page = NotesDocOps.CreateSessionSheet("Сессия 1");
            page.Blocks.Add(canvas);

            var doc = new NotesDocument();
            var group = new PageGroup { Title = "Сессии" };
            group.Pages.Add(page);
            doc.Groups.Add(group);

            string path = Path.Combine(Application.temporaryCachePath, "notes-card-style.dndproj");
            SaveNotesOnly(path, doc);
            var loaded = ProjectSerializer.Load(path);

            if (!loaded.Success)
            { Debug.LogError($"FAIL card style: load failed — {loaded.ErrorMessage}"); ok = false; }

            var backPage = loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].Pages.Count > 0
                ? loaded.Notes.Groups[0].Pages[0] : null;
            var backCanvas = backPage?.Blocks?.Find(b => b.Kind == BlockKind.Canvas && b.CanvasObjects != null && b.CanvasObjects.Count > 0);
            var backCard = backCanvas != null ? backCanvas.CanvasObjects[0] as NoteCardData : null;

            if (backCard == null)
            { Debug.LogError("FAIL card style: the card did not come back"); ok = false; }
            else
            {
                if (backCard.FrameColorIndex != 7)
                { Debug.LogError($"FAIL card style: frame index {backCard.FrameColorIndex}, want 7 — the DM's colour was dropped by the converter"); ok = false; }
                if (backCard.FontSize != CardFontSize.Large)
                { Debug.LogError($"FAIL card style: font size {backCard.FontSize}, want Large"); ok = false; }
                if (backCard.Title != "Засада" || backCard.Body != "три гоблина")
                { Debug.LogError("FAIL card style: the card's own text did not survive alongside the style"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Card Style Round-Trip: PASS" : "Self-Test Card Style Round-Trip: FAIL");
        }
    }
}
