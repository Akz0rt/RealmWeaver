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
                if (back.Kind != PageKind.Document)
                { Debug.LogError($"FAIL round-trip: Kind = {back.Kind}, want Document"); ok = false; }
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

        [ContextMenu("Self-Test: Old Format Pages Are Boards")]
        public void SelfTestOldFormatDefaultsToBoard()
        {
            bool ok = true;

            // A minimal pre-document file: its page carries neither Kind nor Blocks, and its group carries no
            // IsReference. Nothing the DM wrote may be lost, and the page must come back as the board it is.
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
                if (page.Kind != PageKind.Board)
                { Debug.LogError($"FAIL old format: Kind = {page.Kind}, want Board — this is why Board must be the zero value"); ok = false; }
                if (page.Blocks == null)
                { Debug.LogError("FAIL old format: Blocks came back null; every consumer expects an empty list"); ok = false; }
                else if (page.Blocks.Count != 0)
                { Debug.LogError($"FAIL old format: Blocks holds {page.Blocks.Count} entries, want 0"); ok = false; }
                if (page.Objects.Count != 1)
                { Debug.LogError($"FAIL old format: {page.Objects.Count} board objects survived, want 1 — the DM's card must not be dropped"); ok = false; }
            }

            if (loaded.Notes != null && loaded.Notes.Groups.Count > 0 && loaded.Notes.Groups[0].IsReference)
            { Debug.LogError("FAIL old format: a pre-upgrade group must not come back flagged as the reference group"); ok = false; }

            if (loaded.Notes != null)
            {
                var problems = NotesDocOps.Validate(loaded.Notes);
                if (problems.Count != 0)
                { Debug.LogError($"FAIL old format: loaded document is invalid: {string.Join("; ", problems)}"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Old Format Pages Are Boards: PASS" : "Self-Test Old Format Pages Are Boards: FAIL");
        }
    }
}
