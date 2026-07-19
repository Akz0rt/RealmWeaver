using System.Collections.Generic;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Pure, headless computation of a room's inter-floor transitions — the up/down/exit chips the
    /// badge strip draws — branching on the interior's <see cref="FloorLinkMode"/>.
    ///
    /// DUNGEON (<see cref="FloorLinkMode.ImplicitDescent"/>): transitions derive from room TYPE — a Boss
    /// (TypeId 2) descends to the next level, an Entrance (TypeId 0) ascends to the previous level or exits on
    /// floor 0 — plus its portals. Behaviour-preserving copy of the old DungeonBadgeStrip logic.
    ///
    /// BUILDING (<see cref="FloorLinkMode.ExplicitStairs"/>): transitions come from EXPLICIT Stairs/Ladder/
    /// Trapdoor portals. A building stores each stair only on the LOWER floor's room, so the UPPER floor's
    /// "down" transition is DERIVED by scanning the other floors for a portal that targets this room. The
    /// building ascends (a higher floor index is physically HIGHER, ⬆), the opposite of a dungeon. Floor 0's
    /// entrance is the exit to the outside. No boss-descent / implicit entrance-ascent for buildings.
    ///
    /// No UnityEngine types — self-testable headless.</summary>
    public static class InteriorTransitions
    {
        public struct Transition
        {
            public string Arrow;          // "⬆" / "⬇" / "⇢"
            public string Label;          // e.g. "Этаж 2", "Выход", "Э2·1"
            public int TargetFloorIndex;  // floor to jump to on click; -1 = informational (no jump)
            public bool Clickable;
        }

        const string Up = "⬆";
        const string Down = "⬇";
        const string Side = "⇢";

        public static List<Transition> For(InteriorData dungeon, FloorLinkMode mode, int floorIndex, Room room)
        {
            return mode == FloorLinkMode.ExplicitStairs
                ? Building(dungeon, floorIndex, room)
                : Dungeon(dungeon, floorIndex, room);
        }

        // ── Building (ExplicitStairs): explicit inter-floor portals + derived reverse + floor-0 exit ──
        static List<Transition> Building(InteriorData d, int floorIndex, Room room)
        {
            var list = new List<Transition>();
            var linkedFloors = new HashSet<int>();

            // Explicit inter-floor portals stored ON this room (a building stores the stair on the lower floor).
            foreach (var p in room.Portals)
            {
                if (p.Hidden || !IsStair(p.Kind) || p.TargetFloorIndex == floorIndex) continue;
                linkedFloors.Add(p.TargetFloorIndex);
                list.Add(new Transition
                {
                    Arrow = p.TargetFloorIndex > floorIndex ? Up : Down,
                    Label = FloorLabel(p.TargetFloorIndex),
                    TargetFloorIndex = p.TargetFloorIndex,
                    Clickable = true
                });
            }

            // Derived reverse: another floor's stair portal that targets THIS room — the transition back to it
            // (the reverse is not stored on the upper floor). One badge per source floor; skip a floor this
            // room already links to explicitly (no double).
            if (d != null)
                for (int f = 0; f < d.Floors.Count; f++)
                {
                    if (f == floorIndex || linkedFloors.Contains(f)) continue;
                    bool found = false;
                    foreach (var src in d.Floors[f].Rooms)
                    {
                        foreach (var p in src.Portals)
                        {
                            if (p.Hidden || !IsStair(p.Kind)) continue;
                            if (p.TargetFloorIndex != floorIndex || p.TargetRoomId != room.Id) continue;
                            found = true; break;
                        }
                        if (found) break;
                    }
                    if (!found) continue;
                    linkedFloors.Add(f);
                    list.Add(new Transition
                    {
                        Arrow = f < floorIndex ? Down : Up,
                        Label = FloorLabel(f),
                        TargetFloorIndex = f,
                        Clickable = true
                    });
                }

            // Non-stair portals (secret passages / exit): the generator makes none, but the shared inspector
            // lets a DM author a secret passage on a building room too — render it the SAME "⇢" way a dungeon
            // does so it is never silently dropped (Stairs/Ladder/Trapdoor are already shown above as ⬆/⬇).
            foreach (var p in room.Portals)
            {
                if (IsStair(p.Kind)) continue;
                bool portalExit = p.Kind == PortalKind.DungeonExit;
                list.Add(new Transition
                {
                    Arrow = Side,
                    Label = portalExit ? "Выход" : $"Э{p.TargetFloorIndex + 1}·{p.TargetRoomId}",
                    TargetFloorIndex = p.TargetFloorIndex,
                    Clickable = p.Kind == PortalKind.SecretDoor
                });
            }

            // The building entrance is the exit to the outside (floor 0). Informational — leaving the building
            // is live navigation (session mode), deferred.
            if (room.TypeId == 0 && floorIndex == 0)
                list.Add(new Transition { Arrow = Up, Label = "Выход", TargetFloorIndex = -1, Clickable = false });

            return list;
        }

        // ── Dungeon (ImplicitDescent): behaviour-preserving copy of the old DungeonBadgeStrip logic ──
        static List<Transition> Dungeon(InteriorData d, int floorIndex, Room room)
        {
            var list = new List<Transition>();

            // Boss descends to the next level (only when one exists).
            if (room.TypeId == 2 && d != null && floorIndex + 1 < d.Floors.Count)
                list.Add(new Transition { Arrow = Down, Label = $"Этаж {floorIndex + 2}", TargetFloorIndex = floorIndex + 1, Clickable = true });

            // Entrance ascends to the previous floor (its boss), or exits on floor 0.
            if (room.TypeId == 0)
            {
                if (floorIndex <= 0)
                    list.Add(new Transition { Arrow = Up, Label = "Выход", TargetFloorIndex = -1, Clickable = false });
                else
                    list.Add(new Transition { Arrow = Up, Label = $"Этаж {floorIndex}", TargetFloorIndex = floorIndex - 1, Clickable = true });
            }

            // Portals (secret passages / dungeon exit). Only a SecretDoor jumps in-editor.
            foreach (var p in room.Portals)
            {
                bool exit = p.Kind == PortalKind.DungeonExit;
                list.Add(new Transition
                {
                    Arrow = Side,
                    Label = exit ? "Выход" : $"Э{p.TargetFloorIndex + 1}·{p.TargetRoomId}",
                    TargetFloorIndex = p.TargetFloorIndex,
                    Clickable = p.Kind == PortalKind.SecretDoor
                });
            }

            return list;
        }

        static bool IsStair(PortalKind k) => k == PortalKind.Stairs || k == PortalKind.Ladder || k == PortalKind.Trapdoor;
        static string FloorLabel(int floorIndex) => $"Этаж {floorIndex + 1}";
    }
}
