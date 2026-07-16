using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure structural mutations on a dungeon graph, with referential integrity. No Unity
    /// types — headless + self-testable. The editor calls these instead of touching lists directly.</summary>
    public static class DungeonOps
    {
        /// <summary>Add a new Normal room at (x,y) with a fresh stable id.</summary>
        public static Room AddRoom(DungeonLevel lvl, float x, float y)
        {
            var room = new Room { Id = lvl.NextRoomId++, Type = RoomType.Normal, X = x, Y = y };
            var (w, h) = RoomSizing.Default(RoomType.Normal); room.SizeW = w; room.SizeH = h;
            lvl.Rooms.Add(room);
            return room;
        }

        /// <summary>Remove a room and every reference to it: corridors touching it on its level, secret
        /// passages it owns, and secret passages on ANY level that target it (Kind==Room → this level, id).</summary>
        public static void RemoveRoom(DungeonData dungeon, int levelIndex, int roomId)
        {
            if (levelIndex < 0 || levelIndex >= dungeon.Levels.Count) return;
            var lvl = dungeon.Levels[levelIndex];
            lvl.Rooms.RemoveAll(r => r.Id == roomId);
            lvl.Corridors.RemoveAll(c => c.RoomA == roomId || c.RoomB == roomId);
            // Secret passages anywhere in the dungeon that pointed at (levelIndex, roomId).
            foreach (var other in dungeon.Levels)
                foreach (var r in other.Rooms)
                    r.Secrets.RemoveAll(s => s.Kind == SecretTargetKind.Room
                                             && s.TargetLevelIndex == levelIndex && s.TargetRoomId == roomId);
        }

        /// <summary>Add a corridor a–b. Returns null on success, or a Russian reason on rejection.</summary>
        public static string AddCorridor(DungeonLevel lvl, int a, int b)
        {
            if (a == b) return "Нельзя связать комнату с собой.";
            if (lvl.GetRoom(a) == null || lvl.GetRoom(b) == null) return "Комната не найдена.";
            foreach (var c in lvl.Corridors)
                if ((c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a))
                    return "Эти комнаты уже связаны.";
            lvl.Corridors.Add(new Corridor { RoomA = a, RoomB = b });
            return null;
        }

        public static void RemoveCorridor(DungeonLevel lvl, int a, int b)
        {
            lvl.Corridors.RemoveAll(c => (c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a));
        }

        /// <summary>If `type` is a singleton (Entrance/Boss) and another room already holds it, returns
        /// that room's id (the one that would be demoted). Otherwise 0. Does not mutate.</summary>
        public static int FindSingletonConflict(DungeonLevel lvl, int idBeingSet, RoomType type)
        {
            if (type != RoomType.Entrance && type != RoomType.Boss) return 0;
            foreach (var r in lvl.Rooms)
                if (r.Id != idBeingSet && r.Type == type) return r.Id;
            return 0;
        }

        /// <summary>Set a room's type. For a singleton type, first demote any other room of that type to Normal.</summary>
        public static void SetRoomType(DungeonLevel lvl, int id, RoomType type)
        {
            var room = lvl.GetRoom(id);
            if (room == null) return;
            if (type == RoomType.Entrance || type == RoomType.Boss)
                foreach (var r in lvl.Rooms)
                    if (r.Id != id && r.Type == type) r.Type = RoomType.Normal;
            room.Type = type;
        }

        /// <summary>Remove a whole level with referential integrity: drop secret passages (on ANY level)
        /// that targeted the removed level, and decrement TargetLevelIndex for secrets targeting any level
        /// ABOVE the removed one (those floors shift down by one). Then remove the level itself.</summary>
        public static void RemoveLevel(DungeonData dungeon, int levelIndex)
        {
            if (dungeon == null || levelIndex < 0 || levelIndex >= dungeon.Levels.Count) return;
            foreach (var lvl in dungeon.Levels)
                foreach (var r in lvl.Rooms)
                {
                    r.Secrets.RemoveAll(s => s.Kind == SecretTargetKind.Room && s.TargetLevelIndex == levelIndex);
                    foreach (var s in r.Secrets)
                        if (s.Kind == SecretTargetKind.Room && s.TargetLevelIndex > levelIndex)
                            s.TargetLevelIndex--;
                }
            dungeon.Levels.RemoveAt(levelIndex);
        }

        public static SecretPassage AddSecret(Room room)
        {
            var s = new SecretPassage();     // defaults: Kind=Room, target (0,0), bidirectional
            room.Secrets.Add(s);
            return s;
        }

        public static void RemoveSecret(Room room, SecretPassage s) => room.Secrets.Remove(s);
    }
}
