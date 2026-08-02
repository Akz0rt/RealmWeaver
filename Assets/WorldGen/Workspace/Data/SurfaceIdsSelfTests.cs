using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>[ContextMenu] self-tests for SurfaceIds, matching this project's convention (see
    /// WorkspaceOpsSelfTests / NotesDocOpsSelfTests).
    ///
    /// EXISTS BECAUSE A ONE-SHAPE TEST IS WHAT LET THE BUG THROUGH. Task 10c's rebind path was walked by hand
    /// against a building INSIDE a town and looked right; the shape that was never tried — a POI that is
    /// itself a building (Tower/Temple/Fortress/Ruin), whose OwnerRoomId is 0 — encoded to something the
    /// decoder resolved to null. So every assertion below that has a room-scoped case has a top-level case
    /// beside it, and the round-trip is asserted as a round-trip (encode, decode, compare the PARTS) rather
    /// than by eyeballing the string.</summary>
    public class SurfaceIdsSelfTests : MonoBehaviour
    {
        const string Guid1 = "3f2a1c88-5b6d-4e21-9a70-0c1d2e3f4a5b";
        const string Guid2 = "aa11bb22-cc33-4d44-8e55-ff6677889900";

        [ContextMenu("Self-Test: Surface Ids")]
        public void SelfTestSurfaceIds()
        {
            bool ok = true;

            // ── Interiors: both shapes round-trip ────────────────────────────────────
            // TOP-LEVEL (OwnerRoomId 0) — a settlement, a dungeon, or a POI that IS a building. This is the
            // shape the shipped bug got wrong, so it is asserted first.
            ok &= InteriorRoundTrips(Guid1, 0);
            // ROOM-SCOPED (a building inside a town).
            ok &= InteriorRoundTrips(Guid1, 7);

            // The encoding itself, pinned separately from the round-trip: a round-trip alone would still pass
            // if BOTH halves agreed on a `#0` suffix, which is exactly the state that was broken — the suffix
            // must not be there, because InteriorOps.FindBuildingInterior refuses roomId 0.
            string topLevel = SurfaceIds.Interior(Guid1, 0);
            if (topLevel != Guid1)
            { Debug.LogError($"FAIL surfaceids: Interior(poi, 0) = «{topLevel}», want the bare poi id «{Guid1}» with no separator"); ok = false; }

            string roomScoped = SurfaceIds.Interior(Guid1, 7);
            if (roomScoped != Guid1 + "#7")
            { Debug.LogError($"FAIL surfaceids: Interior(poi, 7) = «{roomScoped}», want «{Guid1}#7»"); ok = false; }

            // Two different rooms of the same town are DIFFERENT surfaces, and neither is the town — the
            // property WorkspaceOps.SameSurface relies on to stop a drill-down re-focusing the town's tab.
            if (SurfaceIds.Interior(Guid1, 7) == SurfaceIds.Interior(Guid1, 8)
                || SurfaceIds.Interior(Guid1, 7) == SurfaceIds.Interior(Guid1, 0))
            { Debug.LogError("FAIL surfaceids: rooms 7, 8 and the town itself must all yield distinct ids"); ok = false; }

            // A blank poi id yields "", never a bare separator that would decode as something else.
            if (SurfaceIds.Interior(null, 7) != "" || SurfaceIds.Interior("", 0) != "")
            { Debug.LogError($"FAIL surfaceids: Interior with a blank poi id = «{SurfaceIds.Interior(null, 7)}»/«{SurfaceIds.Interior("", 0)}», want «»"); ok = false; }

            // ── Interiors: what the decoder must REFUSE ──────────────────────────────
            // An explicit "#0" is refused rather than read as room 0: the encoder never produces it, so it can
            // only come from a hand-edited stored value, and accepting it would re-create the ambiguity.
            foreach (string bad in new[] { "", null, "#7", Guid1 + "#", Guid1 + "#0", Guid1 + "#abc" })
                if (SurfaceIds.TryParseInterior(bad, out string p, out int r))
                { Debug.LogError($"FAIL surfaceids: TryParseInterior(«{bad ?? "<null>"}») returned true with {p}/{r}, want false"); ok = false; }

            // ── Battle grids: both interior shapes round-trip ────────────────────────
            ok &= BattleGridRoundTrips(SurfaceIds.Interior(Guid1, 0), 2, 5);   // grid in a top-level interior
            ok &= BattleGridRoundTrips(SurfaceIds.Interior(Guid2, 7), 3, 9);   // grid in a building-in-a-town

            // THE PARSE-FROM-THE-RIGHT RULE, asserted on the only shape that can catch it: the interior part
            // itself contains a separator, so a left-to-right split would return "<guid>" as the interior and
            // read the ROOM id as the floor.
            string nested = SurfaceIds.BattleGrid(SurfaceIds.Interior(Guid2, 7), 3, 9);
            if (!SurfaceIds.TryParseBattleGrid(nested, out string nestedInterior, out int nestedFloor, out int nestedRoom)
                || nestedInterior != Guid2 + "#7" || nestedFloor != 3 || nestedRoom != 9)
            {
                Debug.LogError($"FAIL surfaceids: nested battle-grid id «{nested}» decoded to " +
                               $"«{nestedInterior}»/{nestedFloor}/{nestedRoom}, want «{Guid2}#7»/3/9");
                ok = false;
            }

            // And the decoded interior part must itself still decode — the two levels compose, which is what
            // MapScreenController.RebindBattleGrid does (parse the grid, then resolve its interior).
            if (!SurfaceIds.TryParseInterior(nestedInterior, out string nestedPoi, out int nestedOwnerRoom)
                || nestedPoi != Guid2 || nestedOwnerRoom != 7)
            { Debug.LogError($"FAIL surfaceids: the interior part of a nested grid id did not re-decode to {Guid2}/7"); ok = false; }

            foreach (string bad in new[] { "", null, Guid1, Guid1 + "#2", "#2#5", Guid1 + "#x#5", Guid1 + "#2#y" })
                if (SurfaceIds.TryParseBattleGrid(bad, out string i2, out int f2, out int r2))
                { Debug.LogError($"FAIL surfaceids: TryParseBattleGrid(«{bad ?? "<null>"}») returned true with «{i2}»/{f2}/{r2}, want false"); ok = false; }

            Debug.Log(ok ? "Self-Test Surface Ids: PASS" : "Self-Test Surface Ids: FAIL");
        }

        /// <summary>IsWellFormed, the guard WorkspacePrefs' stored ids pass through (Task 11). Asserted
        /// against the ENCODERS, not against hand-written strings, wherever an encoder can produce the value
        /// — a fixture that disagreed with Interior/BattleGrid would pin the wrong contract.
        ///
        /// The rejection cases are the point: each one is a string SOME path could put in PlayerPrefs (a
        /// corrupted PoiData.Id carrying a '#', a hand edit, a future encoder that drifted) and none of them
        /// is one the encoders above can emit.</summary>
        [ContextMenu("Self-Test: Surface Ids Well-Formed")]
        public void SelfTestSurfaceIdsWellFormed()
        {
            bool ok = true;

            // ── Accepted: everything the encoders actually write ─────────────────────
            foreach (var kind in new[] { SurfaceKind.Settlement, SurfaceKind.BuildingInterior, SurfaceKind.Dungeon })
            {
                if (!SurfaceIds.IsWellFormed(kind, SurfaceIds.Interior(Guid1, 0)))
                { Debug.LogError($"FAIL wellformed: {kind}/«{SurfaceIds.Interior(Guid1, 0)}» (top-level) rejected, want accepted"); ok = false; }
                if (!SurfaceIds.IsWellFormed(kind, SurfaceIds.Interior(Guid1, 7)))
                { Debug.LogError($"FAIL wellformed: {kind}/«{SurfaceIds.Interior(Guid1, 7)}» (room-scoped) rejected, want accepted"); ok = false; }
            }
            string grid = SurfaceIds.BattleGrid(SurfaceIds.Interior(Guid2, 7), 3, 9);
            if (!SurfaceIds.IsWellFormed(SurfaceKind.BattleGrid, grid))
            { Debug.LogError($"FAIL wellformed: BattleGrid/«{grid}» rejected, want accepted"); ok = false; }
            if (!SurfaceIds.IsWellFormed(SurfaceKind.BattleGrid, SurfaceIds.BattleGrid(SurfaceIds.Interior(Guid1, 0), 0, 1)))
            { Debug.LogError("FAIL wellformed: a grid in a TOP-LEVEL interior was rejected, want accepted"); ok = false; }

            // ── Accepted unconditionally: the kinds with no encoding at all ──────────
            // A '#' inside a page id or a PoiData.Id is inert — nothing splits those — so refusing them would
            // drop tabs for a character that costs nothing. WorldMap's id is "" by contract.
            foreach (var kind in new[] { SurfaceKind.Page, SurfaceKind.PoiEditor })
                if (!SurfaceIds.IsWellFormed(kind, "a#b#c") || !SurfaceIds.IsWellFormed(kind, ""))
                { Debug.LogError($"FAIL wellformed: {kind} must accept any id, including «a#b#c» and «»"); ok = false; }
            if (!SurfaceIds.IsWellFormed(SurfaceKind.WorldMap, ""))
            { Debug.LogError("FAIL wellformed: WorldMap/«» rejected, want accepted"); ok = false; }

            // ── Refused: nothing here can be produced by Interior() ──────────────────
            // «#7» no poi part; «<guid>#» empty room part; «#0» the suffix the encoder omits; «#abc» a poi id
            // carrying a separator (the corrupted-PoiData.Id case, which is the whole reason this exists);
            // «#07» a padded number, which DECODES fine (room 7) and re-encodes to «#7» — caught only by the
            // round-trip comparison, not by TryParseInterior alone.
            foreach (string bad in new[] { null, "", "#7", Guid1 + "#", Guid1 + "#0", Guid1 + "#abc", Guid1 + "#07" })
                if (SurfaceIds.IsWellFormed(SurfaceKind.Settlement, bad))
                { Debug.LogError($"FAIL wellformed: Settlement/«{bad ?? "<null>"}» accepted, want refused"); ok = false; }

            // ── Refused: battle grids, including a rotten INTERIOR part ──────────────
            // «<guid>#bb#1#2» parses as a grid (last two separators are numeric) and its interior part
            // «<guid>#bb» is what must fail — the recursion into the interior check is the only thing that
            // catches it, and without it the tab would resolve against a poi id that is not the stored one.
            foreach (string bad in new[]
            {
                null, "", Guid1, Guid1 + "#2", "#2#5", Guid1 + "#x#5", Guid1 + "#2#y",
                Guid1 + "#bb#1#2", Guid1 + "#0#1#2",
            })
                if (SurfaceIds.IsWellFormed(SurfaceKind.BattleGrid, bad))
                { Debug.LogError($"FAIL wellformed: BattleGrid/«{bad ?? "<null>"}» accepted, want refused"); ok = false; }

            Debug.Log(ok ? "Self-Test Surface Ids Well-Formed: PASS" : "Self-Test Surface Ids Well-Formed: FAIL");
        }

        /// <summary>The canvas surface's id is a bare DocBlock id, so SurfaceIds.IsWellFormed has nothing to
        /// check and must not start refusing it — WorkspaceOps.Restore applies that predicate BEFORE any
        /// existence check, so a false here silently drops a restored tab. And the ref is built by ONE helper:
        /// WorkspaceOps.SameSurface compares Kind AND Id, so a hand-built ref that differs by one character is
        /// not a visible error, it is a second tab for the same board.</summary>
        [ContextMenu("Self-Test: Canvas Surface Ref")]
        public void SelfTestCanvasSurfaceRef()
        {
            bool ok = true;

            var block = NotesDocOps.NewBlock(BlockKind.Canvas, 1, "  Схема сюжета  ");
            var surface = NotesSurface.Canvas(block.Id);

            if (surface.Kind != SurfaceKind.Canvas)
            { Debug.LogError($"FAIL canvas ref: kind = {surface.Kind}, want Canvas"); ok = false; }
            if (surface.Id != block.Id)
            { Debug.LogError($"FAIL canvas ref: id = «{surface.Id}», want the block id"); ok = false; }
            if (NotesSurface.Canvas(null).Id != "")
            { Debug.LogError("FAIL canvas ref: a null id must become \"\", never null — it flows into Serialize and SameSurface unchecked"); ok = false; }

            if (!SurfaceIds.IsWellFormed(SurfaceKind.Canvas, block.Id))
            { Debug.LogError("FAIL canvas ref: IsWellFormed refuses a bare block id — restored tabs would be dropped"); ok = false; }

            if (NotesSurface.TitleOf(block) != "Схема сюжета")
            { Debug.LogError($"FAIL canvas title: «{NotesSurface.TitleOf(block)}», want the trimmed caption"); ok = false; }
            if (NotesSurface.TitleOf(NotesDocOps.NewBlock(BlockKind.Canvas, 1, "   ")) != "Доска")
            { Debug.LogError("FAIL canvas title: a blank caption must give «Доска», not an empty tab"); ok = false; }
            if (NotesSurface.TitleOf(null) != "Доска")
            { Debug.LogError("FAIL canvas title: a null block must still give a title"); ok = false; }

            Debug.Log(ok ? "Self-Test Canvas Surface Ref: PASS" : "Self-Test Canvas Surface Ref: FAIL");
        }

        /// <summary>Encode, decode, compare the PARTS — never the string. A helper rather than two copies so
        /// the top-level and room-scoped cases cannot drift into asserting different things.</summary>
        static bool InteriorRoundTrips(string poiId, int ownerRoomId)
        {
            string id = SurfaceIds.Interior(poiId, ownerRoomId);
            if (!SurfaceIds.TryParseInterior(id, out string backPoi, out int backRoom))
            {
                Debug.LogError($"FAIL surfaceids: Interior({poiId}, {ownerRoomId}) = «{id}» did not decode at all");
                return false;
            }
            if (backPoi != poiId || backRoom != ownerRoomId)
            {
                Debug.LogError($"FAIL surfaceids: Interior({poiId}, {ownerRoomId}) round-tripped to " +
                               $"{backPoi}/{backRoom} via «{id}»");
                return false;
            }
            return true;
        }

        static bool BattleGridRoundTrips(string interiorId, int floorIndex, int roomId)
        {
            string id = SurfaceIds.BattleGrid(interiorId, floorIndex, roomId);
            if (!SurfaceIds.TryParseBattleGrid(id, out string backInterior, out int backFloor, out int backRoom))
            {
                Debug.LogError($"FAIL surfaceids: BattleGrid({interiorId}, {floorIndex}, {roomId}) = «{id}» did not decode at all");
                return false;
            }
            if (backInterior != interiorId || backFloor != floorIndex || backRoom != roomId)
            {
                Debug.LogError($"FAIL surfaceids: BattleGrid({interiorId}, {floorIndex}, {roomId}) round-tripped to " +
                               $"«{backInterior}»/{backFloor}/{backRoom} via «{id}»");
                return false;
            }
            return true;
        }
    }
}
