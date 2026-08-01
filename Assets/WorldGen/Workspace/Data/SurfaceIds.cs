using System.Globalization;

namespace WorldGen.Workspace.Data
{
    /// <summary>Encodes and decodes the `SurfaceRef.Id` strings for the surfaces whose identity needs more
    /// than one value: an interior (a POI, plus the room inside it for a building-in-a-town) and a battle
    /// grid (an interior, plus a floor and a room).
    ///
    /// WHY THIS IS A SEPARATE, PURE FILE rather than four private helpers on MapScreenController, which is
    /// where it started. The two halves have to be exact inverses, and nothing was checking that they were —
    /// Task 10c's review found the encoder emitting a shape (`"&lt;guid&gt;#0"` for a POI that is itself a
    /// building: Tower/Temple/Fortress/Ruin) that the decoder resolved to null, so switching back to such a
    /// tab silently re-showed the screen bound to a DIFFERENT interior, under a correctly-labelled tab.
    /// Living here — free of UnityEngine and of WorldGen.Generation, like everything else in this folder —
    /// means the round-trip is exercised by the offline harness for BOTH shapes instead of only the one a
    /// hand-walk happened to try.
    ///
    /// TOTALITY IS THE POINT, not tidiness: for every value the encoder can produce, the decoder must return
    /// the same parts back. The old encoding failed that because it wrote a `#0` suffix that meant "no room"
    /// while the lookup it fed (InteriorOps.FindBuildingInterior) treats roomId 0 as "not a building
    /// interior — refuse" (InteriorOps.cs:13). Room 0 is not a room: InteriorData.OwnerRoomId's own doc says
    /// 0 means "owned by the POI directly". So the encoding now OMITS the suffix entirely for room 0, and the
    /// presence of a separator is what distinguishes a room-scoped interior from a top-level one.
    ///
    /// SEPARATOR SAFETY, AND EXACTLY HOW FAR IT GOES. The poiId part is a PoiData.Id, which every id this
    /// app MINTS is a Guid string (PoiData.cs's `= Guid.NewGuid().ToString()` initializer) and therefore
    /// free of '#'. That is what makes "the first '#' splits poi from room" unambiguous for interiors, and
    /// "the last two '#' segments are floor and room" unambiguous for battle grids — a battle grid inside a
    /// building has an interior part that itself contains one, so parsing a battle grid from the LEFT would
    /// mis-read every one of those.
    ///
    /// It is a property of the ids this app writes, NOT an invariant anything enforces: PoiData.Id is a
    /// public field Newtonsoft deserializes from the .dndproj with no validation, so a corrupted or
    /// hand-edited project CAN carry a '#' in it. Task 11 is what made that matter, because Task 11 is what
    /// started PERSISTING these ids: a transient wrong id died with the session, a stored one round-trips
    /// into a different (poi, room) on every launch. IsWellFormed below is the guard, and its LIMIT is stated
    /// there — it rejects an id no encoder here could have produced, and cannot possibly reject one that is
    /// merely attributed to the wrong POI.
    ///
    /// These ids are persisted verbatim by WorkspaceOps.Serialize (which escapes only tabs and newlines and
    /// has no opinion about anything else), so WorkspacePrefs restores tabs through this exact round-trip —
    /// the decoder is not test scaffolding, it is half the stored format.</summary>
    public static class SurfaceIds
    {
        public const char Separator = '#';

        /// <summary>`poiId` for an interior owned by the POI directly (a settlement, a dungeon, or a POI that
        /// IS a building), `poiId#roomId` for a building inside a town. Returns "" for a null/empty poiId
        /// rather than a bare separator, so an unidentifiable interior yields an id that decodes back to
        /// nothing instead of one that collides with another.</summary>
        public static string Interior(string poiId, int ownerRoomId)
        {
            if (string.IsNullOrEmpty(poiId)) return "";
            return ownerRoomId == 0
                ? poiId
                : poiId + Separator + ownerRoomId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Exact inverse of Interior. `ownerRoomId` comes back 0 for a top-level interior, which is
        /// the same value InteriorData.OwnerRoomId itself carries for one — so a caller can compare the two
        /// directly rather than needing a separate "is this room-scoped" flag.
        ///
        /// Refuses (returns false) rather than guessing on: a null/empty id, an empty poi part, a non-numeric
        /// room part, and an EXPLICIT `#0` — the last because Interior never produces it, so seeing one means
        /// the id came from somewhere else (a hand-edited PlayerPrefs value, or a future encoder that drifted
        /// from this one), and accepting it would silently resurrect the very ambiguity this file exists to
        /// remove.</summary>
        public static bool TryParseInterior(string id, out string poiId, out int ownerRoomId)
        {
            poiId = "";
            ownerRoomId = 0;
            if (string.IsNullOrEmpty(id)) return false;

            int sep = id.IndexOf(Separator);
            if (sep < 0) { poiId = id; return true; }
            if (sep == 0) return false;                       // no poi part

            if (!int.TryParse(id.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int room) || room == 0)
                return false;

            poiId = id.Substring(0, sep);
            ownerRoomId = room;
            return true;
        }

        /// <summary>`interiorId#floorIndex#roomId`, where `interiorId` is whatever Interior produced — so it
        /// may itself contain one separator. Returns "" for an empty interiorId, same reasoning as Interior.</summary>
        public static string BattleGrid(string interiorId, int floorIndex, int roomId)
        {
            if (string.IsNullOrEmpty(interiorId)) return "";
            return interiorId + Separator + floorIndex.ToString(CultureInfo.InvariantCulture)
                              + Separator + roomId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Exact inverse of BattleGrid. Splits at the LAST two separators, so the interior part
        /// survives intact whether or not it contains one of its own — see the class doc.</summary>
        public static bool TryParseBattleGrid(string id, out string interiorId, out int floorIndex, out int roomId)
        {
            interiorId = "";
            floorIndex = 0;
            roomId = 0;
            if (string.IsNullOrEmpty(id)) return false;

            int lastSep = id.LastIndexOf(Separator);
            if (lastSep <= 0) return false;
            int prevSep = id.LastIndexOf(Separator, lastSep - 1);
            if (prevSep <= 0) return false;                   // needs two separators AND a non-empty interior part

            if (!int.TryParse(id.Substring(lastSep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int room)) return false;
            if (!int.TryParse(id.Substring(prevSep + 1, lastSep - prevSep - 1), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int floor)) return false;

            interiorId = id.Substring(0, prevSep);
            floorIndex = floor;
            roomId = room;
            return true;
        }

        /// <summary>Could THIS file have written `id` for a tab of `kind`? The test is a round-trip — decode,
        /// re-encode, compare — which is the totality property this class's doc already states, turned into a
        /// predicate. Anything an encoder above cannot produce is rejected: a poi part containing a separator
        /// («aa#bb», where the room part is not a number), an explicit «#0», a leading separator, a padded
        /// number («aa#07» decodes to room 7 and re-encodes to «aa#7», which is not what was stored).
        ///
        /// WHY IT EXISTS AT ALL — Task 11 (persistence). Until then a SurfaceRef.Id was minted from live data
        /// and consumed in the same session, so a malformed one could only appear if the DATA was malformed,
        /// and it died with the session. WorkspacePrefs stores these strings in PlayerPrefs, a plain
        /// user-writable file, and re-feeds them to MapScreenController.RebindSurface on every launch — so a
        /// bad id stops being a transient and starts being a fixture. WorkspaceOps.Restore is the one caller;
        /// see there for what a rejection costs (that ONE tab, silently, exactly as a deleted target does).
        ///
        /// THE LIMIT, stated because a reader will otherwise assume more: this CANNOT detect an id that is
        /// well-formed but names the wrong thing. If a project's PoiData.Id genuinely were «aa#7», the
        /// encoder writes «aa#7» and this accepts it — as it must, since a building in room 7 of town «aa»
        /// writes the identical string. What separates those two is whether a POI «aa» with a room 7 actually
        /// exists, which is the `exists` predicate's question, not this one's; WorkspaceOps.Restore applies
        /// both, in that order. The residue is a world holding BOTH a POI literally named «aa#7» AND a POI
        /// «aa» owning a room 7 — accepted, and not defended against.
        ///
        /// EVERY OTHER KIND RETURNS TRUE, and that is not laziness: Page and PoiEditor ids are opaque strings
        /// (a page guid, a PoiData.Id) that nothing here splits, so a separator inside one is inert, and
        /// WorldMap's id is "" by contract. There is no encoding to check, so there is nothing this method
        /// could add that `exists` does not already say better. Written as an explicit switch with a `default`
        /// rather than a list of "encoded" kinds, so a NEW SurfaceKind defaults to unvalidated-but-existence-
        /// checked instead of silently rejected.</summary>
        public static bool IsWellFormed(SurfaceKind kind, string id)
        {
            switch (kind)
            {
                case SurfaceKind.Settlement:
                case SurfaceKind.BuildingInterior:
                case SurfaceKind.Dungeon:
                    return TryParseInterior(id, out string poiId, out int ownerRoomId)
                           && Interior(poiId, ownerRoomId) == id;

                case SurfaceKind.BattleGrid:
                    // The interior PART is re-checked through this same method rather than merely parsed:
                    // TryParseBattleGrid splits at the last two separators and hands back whatever precedes
                    // them untouched, so «aa#bb#1#2» parses cleanly into an interior part «aa#bb» that
                    // TryParseInterior itself would refuse. Kind.Dungeon is passed only to select the interior
                    // branch above — the three interior kinds are indistinguishable to this check, which is
                    // why the branch groups them.
                    return TryParseBattleGrid(id, out string interiorId, out int floorIndex, out int roomId)
                           && BattleGrid(interiorId, floorIndex, roomId) == id
                           && IsWellFormed(SurfaceKind.Dungeon, interiorId);

                default:
                    return true;
            }
        }
    }
}
