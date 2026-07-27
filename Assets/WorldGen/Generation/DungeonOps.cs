using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure structural mutations on a dungeon graph, with referential integrity. No Unity
    /// types — headless + self-testable. The editor calls these instead of touching lists directly.</summary>
    public static class DungeonOps
    {
        /// <summary>Add a new Normal room at (x,y) with a fresh stable id.
        ///
        /// ON A SETTLEMENT FLOOR IT ALSO GETS ITS LATTICE CELL (v11), and that is not a nicety — it is what
        /// keeps the load path's SettlementFootprint.EnsureFootprints honest. That pass gives a cell-less
        /// settlement room a footprint derived on the LEGACY 0.07 pitch, on the reasoning that a room with no
        /// cells can only have come from a pre-v11 save. Without this line that reasoning would be FALSE from
        /// the moment the DM presses «+ Здание»: the new building would carry a current-pitch point and no
        /// cells, and its next load would read that point as a legacy index and teleport it to roughly 3/7 of
        /// where it was put. Writing the cell here makes the premise true by construction instead.
        ///
        /// Gated on lvl.SettlementParams — the same "is this a settlement floor?" test DungeonLayout uses —
        /// so a DUNGEON or BUILDING-INTERIOR floor is byte-identical to before: those have no SettlementParams
        /// and Room.Cells stays null (and NullValueHandling.Ignore keeps it off their wire).</summary>
        public static Room AddRoom(InteriorFloor lvl, float x, float y)
        {
            var room = new Room { Id = lvl.NextRoomId++, TypeId = 1, X = x, Y = y };
            var (w, h) = RoomSizing.Default(1); room.SizeW = w; room.SizeH = h;
            if (lvl.SettlementParams != null)
                room.Cells = SettlementFootprint.Encode(new[] { (SettlementFootprint.CellOf(x), SettlementFootprint.CellOf(y)) });
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

        /// <summary>Add a corridor a–b. Returns null on success, or a Russian reason on rejection.
        /// <paramref name="authored"/> marks the link as hand-made by the DM — the ONLY caller that should
        /// pass true is DungeonViewController.OnRoomActivated (the «Связать» action); every other caller
        /// (generators, self-tests) leaves it false, the default, so a generated floor's links never count
        /// as authored (see <see cref="HasAuthoredContent"/>).</summary>
        public static string AddCorridor(InteriorFloor lvl, int a, int b, bool authored = false)
        {
            if (a == b) return "Нельзя связать комнату с собой.";
            if (lvl.GetRoom(a) == null || lvl.GetRoom(b) == null) return "Комната не найдена.";
            foreach (var c in lvl.Links)
                if ((c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a))
                    return "Эти комнаты уже связаны.";
            lvl.Links.Add(new Link { RoomA = a, RoomB = b, Authored = authored });
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

        /// <summary>Remove a whole level with referential integrity: drop INTER-FLOOR portals (on ANY level)
        /// that targeted the removed level — so a building never keeps a stairwell to a floor that no longer
        /// exists ("лестница вникуда") — and decrement TargetFloorIndex for those targeting any level ABOVE the
        /// removed one (those floors shift down by one). Covers Stairs/Ladder/Trapdoor and SecretDoor alike; a
        /// DungeonExit has no floor target and is left untouched. Then remove the level itself.</summary>
        public static void RemoveLevel(InteriorData dungeon, int levelIndex)
        {
            if (dungeon == null || levelIndex < 0 || levelIndex >= dungeon.Floors.Count) return;
            foreach (var lvl in dungeon.Floors)
                foreach (var r in lvl.Rooms)
                {
                    r.Portals.RemoveAll(s => IsInterFloor(s.Kind) && s.TargetFloorIndex == levelIndex);
                    foreach (var s in r.Portals)
                        if (IsInterFloor(s.Kind) && s.TargetFloorIndex > levelIndex)
                            s.TargetFloorIndex--;
                }
            dungeon.Floors.RemoveAt(levelIndex);
        }

        // A portal that points at another FLOOR (its TargetFloorIndex is meaningful) — everything except the
        // DungeonExit, which just leaves the dungeon.
        static bool IsInterFloor(PortalKind k) =>
            k == PortalKind.SecretDoor || k == PortalKind.Stairs || k == PortalKind.Ladder || k == PortalKind.Trapdoor;

        /// <summary>Does this floor hold content a DM AUTHORED — i.e. content an irreversible, undo-less
        /// destroy (DungeonEditorScreen's «× Этаж» / «Перегенерировать») would silently take away? Four
        /// sources: (1) a room Title/Body (the note layer); (2) any Link with Authored==true (set ONLY by
        /// the «Связать» action via <see cref="AddCorridor"/>'s authored parameter — every generator-made
        /// link leaves this false, so a freshly generated floor's spanning tree does not trip this on its
        /// own); (3) any non-Stairs portal (secret passage / dungeon exit — the only portal kinds the
        /// inspector lets a DM create; Stairs portals are excluded because they are 100% generator-owned —
        /// BuildingGenerator.RewireStairChain builds and rebuilds them and the inspector cannot add one —
        /// so counting them would make every floor of every multi-floor building prompt); and (4) any room
        /// carrying a battle map (Room.Grid != null — the field is null until the DM edits one, so a
        /// generated floor never trips this on its own); and (5) a settlement building carrying a Preview
        /// image (Room.Preview != null — null until the DM adds one, so a freshly generated settlement of
        /// any size never trips this on its own).</summary>
        public static bool HasAuthoredContent(InteriorFloor lvl)
        {
            if (lvl == null) return false;
            foreach (var l in lvl.Links) if (l.Authored) return true;
            foreach (var r in lvl.Rooms)
            {
                if (!string.IsNullOrEmpty(r.Title) || !string.IsNullOrEmpty(r.Body)) return true;
                if (r.Grid != null) return true;
                if (r.Preview != null) return true;
                foreach (var p in r.Portals) if (p.Kind != PortalKind.Stairs) return true;
            }
            return false;
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
