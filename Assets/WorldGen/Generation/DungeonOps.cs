using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure structural mutations on a dungeon graph, with referential integrity. No Unity
    /// types — headless + self-testable. The editor calls these instead of touching lists directly.</summary>
    public static class DungeonOps
    {
        /// <summary>Add a new Normal room at (x,y) with a fresh stable id.</summary>
        public static Room AddRoom(InteriorFloor lvl, float x, float y)
        {
            var room = new Room { Id = lvl.NextRoomId++, TypeId = 1, X = x, Y = y };
            var (w, h) = RoomSizing.Default(1); room.SizeW = w; room.SizeH = h;
            lvl.Rooms.Add(room);
            return room;
        }

        /// <summary>Remove a room and every reference to it: corridors touching it on its level, secret
        /// passages it owns, and secret passages on ANY level that target it (Kind==SecretDoor → this level, id).</summary>
        public static void RemoveRoom(InteriorData dungeon, int levelIndex, int roomId)
        {
            if (levelIndex < 0 || levelIndex >= dungeon.Floors.Count) return;
            var lvl = dungeon.Floors[levelIndex];
            lvl.Rooms.RemoveAll(r => r.Id == roomId);
            lvl.Links.RemoveAll(c => c.RoomA == roomId || c.RoomB == roomId);
            // Secret passages anywhere in the dungeon that pointed at (levelIndex, roomId).
            foreach (var other in dungeon.Floors)
                foreach (var r in other.Rooms)
                    r.Portals.RemoveAll(s => s.Kind == PortalKind.SecretDoor
                                             && s.TargetFloorIndex == levelIndex && s.TargetRoomId == roomId);
        }

        /// <summary>Add a corridor a–b. Returns null on success, or a Russian reason on rejection.</summary>
        public static string AddCorridor(InteriorFloor lvl, int a, int b)
        {
            if (a == b) return "Нельзя связать комнату с собой.";
            if (lvl.GetRoom(a) == null || lvl.GetRoom(b) == null) return "Комната не найдена.";
            foreach (var c in lvl.Links)
                if ((c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a))
                    return "Эти комнаты уже связаны.";
            lvl.Links.Add(new Link { RoomA = a, RoomB = b });
            return null;
        }

        public static void RemoveCorridor(InteriorFloor lvl, int a, int b)
        {
            lvl.Links.RemoveAll(c => (c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a));
        }

        /// <summary>If `type` is a singleton (Entrance/Boss) and another room already holds it, returns
        /// that room's id (the one that would be demoted). Otherwise 0. Does not mutate.</summary>
        public static int FindSingletonConflict(InteriorFloor lvl, int idBeingSet, int type)
        {
            if (type != 0 && type != 2) return 0;
            foreach (var r in lvl.Rooms)
                if (r.Id != idBeingSet && r.TypeId == type) return r.Id;
            return 0;
        }

        /// <summary>Set a room's type. For a singleton type, first demote any other room of that type to Normal.</summary>
        public static void SetRoomType(InteriorFloor lvl, int id, int type)
        {
            var room = lvl.GetRoom(id);
            if (room == null) return;
            if (type == 0 || type == 2)
                foreach (var r in lvl.Rooms)
                    if (r.Id != id && r.TypeId == type) r.TypeId = 1;
            room.TypeId = type;
        }

        /// <summary>Remove a whole level with referential integrity: drop secret passages (on ANY level)
        /// that targeted the removed level, and decrement TargetFloorIndex for secrets targeting any level
        /// ABOVE the removed one (those floors shift down by one). Then remove the level itself.</summary>
        public static void RemoveLevel(InteriorData dungeon, int levelIndex)
        {
            if (dungeon == null || levelIndex < 0 || levelIndex >= dungeon.Floors.Count) return;
            foreach (var lvl in dungeon.Floors)
                foreach (var r in lvl.Rooms)
                {
                    r.Portals.RemoveAll(s => s.Kind == PortalKind.SecretDoor && s.TargetFloorIndex == levelIndex);
                    foreach (var s in r.Portals)
                        if (s.Kind == PortalKind.SecretDoor && s.TargetFloorIndex > levelIndex)
                            s.TargetFloorIndex--;
                }
            dungeon.Floors.RemoveAt(levelIndex);
        }

        public static Portal AddSecret(Room room)
        {
            var s = new Portal();     // defaults: Kind=SecretDoor, Hidden=true, target (0,0), bidirectional
            room.Portals.Add(s);
            return s;
        }

        public static void RemoveSecret(Room room, Portal s) => room.Portals.Remove(s);
    }
}
