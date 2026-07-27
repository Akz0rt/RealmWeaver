using System.Collections.Generic;
using Newtonsoft.Json;

namespace WorldGen.Generation
{
    public enum InteriorKind { Dungeon = 0, Building = 1, Settlement = 2 }

    // Ordered so legacy SecretPassage.Kind ints map straight across:
    // old SecretTargetKind.Room(0)→SecretDoor(0), DungeonExit(1)→DungeonExit(1).
    public enum PortalKind { SecretDoor = 0, DungeonExit = 1, Stairs = 2, Ladder = 3, Trapdoor = 4 }

    /// <summary>An inter-room/inter-floor link attached to a room. Hidden==true reproduces the old
    /// "secret passage" (dungeon). A stair is a NON-hidden portal (building). Kind==DungeonExit leaves
    /// the interior. Bidirectional is authoring intent (enforced live in the play-mode sub-project).</summary>
    public class Portal
    {
        public PortalKind Kind = PortalKind.SecretDoor;
        public bool Hidden = true;                       // legacy secrets (no field in v6) default to hidden
        [JsonProperty("TargetLevelIndex")]              // preserve v6 wire key
        public int TargetFloorIndex = 0;
        public int TargetRoomId = 0;
        public bool Bidirectional = true;
        public string Label = "";
    }

    /// <summary>A room node. Id is stable within its floor and never reused (links/portals reference it).
    /// X,Y are normalized canvas position (0..1) so the layout survives window resize.
    ///
    /// Legacy dungeon room-type ints (kept for readers; do not add a RoomType enum back — TypeId is an
    /// int): Вход(Entrance)=0, Обычная(Normal)=1, Босс(Boss)=2.</summary>
    public class Room
    {
        public int Id;
        [JsonProperty("Type")]                           // was RoomType enum (0/1/2), reads the same int
        public int TypeId = 1;                           // dungeon default = Normal(1); profiles reinterpret
        public string Title = "";
        public string Body = "";
        public float X = 0.5f;
        public float Y = 0.5f;
        public int SizeW = 0;   // footprint width in tiles; 0 = "unset" → defaulted from type on gen/load
        public int SizeH = 0;   // footprint height in tiles
        /// <summary>The room's battle map, or null if the DM never edited one. Null is the norm: an
        /// untouched map is regenerated deterministically by BattleGridGenerator, so storing it would
        /// bloat every save for no information. Non-null therefore means AUTHORED content — which is
        /// exactly what DungeonOps.HasAuthoredContent keys on. Holds world definition only: nothing about
        /// tokens, initiative, fog or where the party stands ever goes in here (that is session state,
        /// and it will live in its own top-level object).
        ///
        /// NullValueHandling.Ignore: without it Newtonsoft writes "Grid": null for every room in every
        /// save, which both contradicts the spec's acceptance criterion that a project with no battle maps
        /// does not change size, and undercuts the "null costs nothing" claim above.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public BattleGrid Grid = null;
        /// <summary>A settlement building's preview image (PNG bytes), or null. Optional and null until the
        /// DM adds one — a town of 60 buildings must not carry 60 images by default. NullValueHandling.Ignore
        /// keeps the key out of every save when absent (the Room.Grid precedent). World definition only.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public byte[] Preview = null;
        /// <summary>A settlement building's status: false = active (a real place — number/name/description/
        /// image/interior), true = a decorative dummy the player can't interact with. Orthogonal to TypeId
        /// (a gate is never a dummy). DefaultValueHandling.Ignore: only the true (dummy) values serialize;
        /// an absent key loads as false (active), the default. World definition only.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsDummy = false;
        /// <summary>A SETTLEMENT BUILDING's occupied cells on the fixed building-cell lattice, flattened to
        /// [i0,j0, i1,j1, …]. The units are ABSOLUTE lattice indices (SettlementFootprint.CellOf / CenterOf),
        /// never tiles and never anything relative to another building — that is what lets a block of flush
        /// buildings be described at all, and what makes a stored index still mean the same place after some
        /// other building moves.
        ///
        /// A SETTLEMENT GATE (TypeId 0) carries ONE cell from v11 on — the ring cell it opens — so the v11
        /// lattice migration, which moves a town by translating its CELLS, moves the gate with it. Null for
        /// everything else: a dungeon room, a building-interior room. NullValueHandling.Ignore keeps the key
        /// out of every such room's JSON entirely (the Room.Grid / Room.Preview precedent), so no dungeon or
        /// building save grows by a byte.
        ///
        /// NOT a re-use of SizeW/SizeH, which stay TILES and stay unread here — one lattice cell is
        /// 0.03 * 128 = 3.84 tiles, so conflating the two units would rewrite every saved town's scale.
        /// World definition only. Absent in a v9/v10 save → null → SettlementFootprint.EnsureFootprints gives
        /// the room a single-cell footprint at load (on the LEGACY pitch — see that method), so an existing
        /// town keeps one building per cell.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int[] Cells = null;
        [JsonProperty("Secrets")]                        // preserve v6 wire key
        public List<Portal> Portals = new List<Portal>();
    }

    /// <summary>A same-floor walkable link between two rooms. Always bidirectional.</summary>
    public class Link
    {
        public int RoomA;
        public int RoomB;
        /// <summary>True only for a link the DM hand-drew via «Связать» (DungeonViewController.
        /// OnRoomActivated -> DungeonOps.AddCorridor(authored: true)). Every generator-made link
        /// (BuildingGenerator.BuildLinks, DungeonGraphGenerator, CompactLayout's fill-phase
        /// AddLinkIfAbsent) leaves this false, its default — same convention as Portal.Hidden above:
        /// absent in an older save (no field before this) deserializes as false. Drives
        /// DungeonOps.HasAuthoredContent, which gates DungeonEditorScreen's «× Этаж» / «Перегенерировать»
        /// confirm so a freshly generated floor's spanning tree does not trip it on its own.</summary>
        public bool Authored = false;
    }

    /// <summary>A settlement's authored composition knobs, stored on its single floor so the editor's
    /// steppers and «Сгенерировать заново» read them and they survive reload. Null for dungeons/buildings.</summary>
    public class SettlementParams
    {
        /// <summary>The town's SIZE CLASS (v11) — what the DM picks, and the only scale knob there is. The
        /// wall radius, gate count and building target all derive from it through SettlementSizing. Serialized
        /// as its int, no attributes: Small is 0, so a DefaultValueHandling.Ignore here would drop the key for
        /// every small town and make "absent" ambiguous between "small" and "pre-v11".</summary>
        public SettlementSize Size;
        /// <summary>The pre-v11 building-count knob, kept ONLY so the v11 migration can bucket an older
        /// town into a Size. Nothing writes it; DefaultValueHandling.Ignore keeps the key off the wire once
        /// it is 0, so a v11 save never carries it. Deleting the property outright would make Newtonsoft
        /// silently drop `"TargetBuildings": 20` on load and every legacy town would bucket as Small.</summary>
        [JsonProperty("TargetBuildings", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LegacyTargetBuildings;
        public int ActiveBuildings;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasWall = false;
        /// <summary>The settlement's STREET cells on the same fixed building-cell lattice, flattened to
        /// [i0,j0, i1,j1, …] — ABSOLUTE lattice indices, decoded with SettlementFootprint.Decode exactly like
        /// Room.Cells. Null until a later task authors blocks-and-streets; a settlement whose streets are
        /// still derived per rebuild simply leaves it unset.
        ///
        /// Null for dungeons and building interiors by construction (SettlementParams itself is null there).
        /// NullValueHandling.Ignore keeps the key off the wire when unset, the same treatment Room.Preview
        /// and Room.Cells carry. World definition only.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int[] StreetCells = null;
    }

    /// <summary>One floor as a graph: rooms + links. NextRoomId hands out stable ids.</summary>
    public class InteriorFloor
    {
        [JsonProperty("Rooms")] public List<Room> Rooms = new List<Room>();
        [JsonProperty("Corridors")] public List<Link> Links = new List<Link>();
        public int NextRoomId = 1;
        /// <summary>A settlement's composition parameters, or null (dungeons/buildings). Stored so the DM's
        /// total/active counts persist and «Сгенерировать заново» re-applies them.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public SettlementParams SettlementParams = null;

        public Room GetRoom(int id) => Rooms.Find(r => r.Id == id);
    }

    /// <summary>An interior owned by one POI (by PoiData.Id). One or more graph floors. Kind distinguishes
    /// a dungeon from a building interior (later sub-project); absent in a v6 save → Dungeon.</summary>
    public class InteriorData
    {
        public string OwnerPoiId;
        /// <summary>Which node within OwnerPoiId's interior this interior belongs to (Ц2, building
        /// recursion): 0, the default, means the interior is owned by the POI directly — every interior
        /// before Ц2, plus a settlement/dungeon/plain building's own interior. A non-zero value is the
        /// room id of the BUILDING node inside OwnerPoiId's settlement whose own interior this is.
        /// DefaultValueHandling.Ignore: only non-zero values serialize, so every pre-Ц2 save (and every
        /// non-building interior) round-trips with no new key at all — mirrors Room.IsDummy's convention
        /// above.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int OwnerRoomId;
        public InteriorKind Kind = InteriorKind.Dungeon; // absent in v6 → Dungeon
        [JsonProperty("Levels")] public List<InteriorFloor> Floors = new List<InteriorFloor>();
    }
}
