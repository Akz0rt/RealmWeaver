using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Room node type. Extensible — append new values (never reorder: the int is serialized).</summary>
    public enum RoomType { Entrance = 0, Normal = 1, Boss = 2 }

    /// <summary>Where a secret passage leads: a specific room in this dungeon, or out of the dungeon.</summary>
    public enum SecretTargetKind { Room = 0, DungeonExit = 1 }

    /// <summary>A "passage mechanic" attached to a room — a hidden link to a fixed location. When
    /// Kind==Room it points at (TargetLevelIndex, TargetRoomId); when DungeonExit it leaves the dungeon.
    /// Bidirectional is authoring intent only (enforced live in sub-project 2).</summary>
    public class SecretPassage
    {
        public SecretTargetKind Kind = SecretTargetKind.Room;
        public int TargetLevelIndex = 0;
        public int TargetRoomId = 0;
        public bool Bidirectional = true;
        public string Label = "";
    }

    /// <summary>A room node. Id is stable within its level and never reused (corridors/secrets reference it).
    /// X,Y are normalized canvas position (0..1) so the layout survives window resize.</summary>
    public class Room
    {
        public int Id;
        public RoomType Type = RoomType.Normal;
        public string Title = "";
        public string Body = "";
        public float X = 0.5f;
        public float Y = 0.5f;
        public List<SecretPassage> Secrets = new List<SecretPassage>();
    }

    /// <summary>A same-floor walkable link between two rooms. Always bidirectional.</summary>
    public class Corridor
    {
        public int RoomA;
        public int RoomB;
    }

    /// <summary>One floor as a graph: rooms + corridors. NextRoomId hands out stable ids.</summary>
    public class DungeonLevel
    {
        public List<Room> Rooms = new List<Room>();
        public List<Corridor> Corridors = new List<Corridor>();
        public int NextRoomId = 1;

        public Room GetRoom(int id) => Rooms.Find(r => r.Id == id);
    }

    /// <summary>A dungeon owned by one POI (by PoiData.Id). One or more graph levels.</summary>
    public class DungeonData
    {
        public string OwnerPoiId;
        public List<DungeonLevel> Levels = new List<DungeonLevel>();
    }
}
