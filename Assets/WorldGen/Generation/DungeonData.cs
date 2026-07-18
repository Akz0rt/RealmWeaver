using System.Collections.Generic;
using Newtonsoft.Json;

namespace WorldGen.Generation
{
    public enum InteriorKind { Dungeon = 0, Building = 1 }

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
        [JsonProperty("Secrets")]                        // preserve v6 wire key
        public List<Portal> Portals = new List<Portal>();
    }

    /// <summary>A same-floor walkable link between two rooms. Always bidirectional.</summary>
    public class Link
    {
        public int RoomA;
        public int RoomB;
    }

    /// <summary>One floor as a graph: rooms + links. NextRoomId hands out stable ids.</summary>
    public class InteriorFloor
    {
        [JsonProperty("Rooms")] public List<Room> Rooms = new List<Room>();
        [JsonProperty("Corridors")] public List<Link> Links = new List<Link>();
        public int NextRoomId = 1;

        public Room GetRoom(int id) => Rooms.Find(r => r.Id == id);
    }

    /// <summary>An interior owned by one POI (by PoiData.Id). One or more graph floors. Kind distinguishes
    /// a dungeon from a building interior (later sub-project); absent in a v6 save → Dungeon.</summary>
    public class InteriorData
    {
        public string OwnerPoiId;
        public InteriorKind Kind = InteriorKind.Dungeon; // absent in v6 → Dungeon
        [JsonProperty("Levels")] public List<InteriorFloor> Floors = new List<InteriorFloor>();
    }
}
