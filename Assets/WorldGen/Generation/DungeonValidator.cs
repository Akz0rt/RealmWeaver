using System.Collections.Generic;

namespace WorldGen.Generation
{
    public enum IssueSeverity { Error, Warning }

    public class DungeonIssue
    {
        public IssueSeverity Severity;
        public int LevelIndex;         // 0-based
        public string Message;         // Russian, human-readable
    }

    /// <summary>Read-only rule checks over a dungeon graph. No Unity types — headless + self-testable.</summary>
    public static class DungeonValidator
    {
        public static List<DungeonIssue> Validate(InteriorData dungeon,
            int minBossDistance = DungeonGraphGenerator.DefaultMinBossDistance)
        {
            var issues = new List<DungeonIssue>();
            if (dungeon == null) return issues;

            // Boss rooms are a DUNGEON concept (a building has no boss). And a building's outside entrance is
            // ONLY on floor 0 — upper floors have a stairwell (Лестница), not a Вход — so the "exactly one
            // entrance" rule applies per-floor for dungeons but only to floor 0 for buildings.
            //
            // THE one source of truth for "does this interior have a boss rule". InteriorProfile used to carry
            // a HasBossRule flag saying the same thing, consulted by nobody — it was deleted rather than wired
            // in here, because this validator is HEADLESS and InteriorProfile lives in WorldGen.Rendering
            // (ThemeRole → UnityEngine), so the profile can never be the source for this file.
            bool isBuilding = dungeon.Kind == InteriorKind.Building;

            // A SETTLEMENT reuses this graph but none of these dungeon rules apply: its gates are not "the one
            // entrance", it has no boss, no stairwell. Settlement-specific rules are deferred (Ц1) — until then
            // a settlement is always valid. Gate every dungeon check below off for it.
            bool isSettlement = dungeon.Kind == InteriorKind.Settlement;
            bool bossRule = !isBuilding && !isSettlement;

            for (int li = 0; li < dungeon.Floors.Count; li++)
            {
                var lvl = dungeon.Floors[li];
                int human = li + 1;

                // TypeId 2 is the Boss for a DUNGEON but the Лестница (stairwell column) for a BUILDING — the two
                // never coexist (Kind decides which rules below apply), so one counter serves both. The id is
                // named for the TYPE, not for either meaning: calling it `bossId` read as a leaked dungeon
                // concept at the BUILDING sites below (the orphan root upstairs is the Лестница, not a boss).
                int entrances = 0, entranceId = 0, typeTwo = 0, typeTwoId = 0;
                foreach (var r in lvl.Rooms)
                {
                    if (r.TypeId == 0) { entrances++; entranceId = r.Id; }
                    if (r.TypeId == BuildingGenerator.StairTypeId) { typeTwo++; typeTwoId = r.Id; }
                }

                if ((!isBuilding || li == 0) && !isSettlement && entrances != 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: должен быть ровно один вход (сейчас {entrances}).");
                if (bossRule && typeTwo > 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: не более одной комнаты босса (сейчас {typeTwo}).");
                if (bossRule && typeTwo == 0)
                    Add(issues, IssueSeverity.Warning, li, $"Этаж {human}: нет комнаты босса — глубже только через секретный ход.");
                // Building stairwell: a multi-floor building carries exactly one Лестница per floor (the shared
                // column). A missing or duplicate one means the vertical shaft is broken.
                if (isBuilding && dungeon.Floors.Count > 1 && typeTwo != 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: должна быть ровно одна лестница (сейчас {typeTwo}).");

                var adj = BuildAdj(lvl);
                // Boss distance (dungeon only, entrance present).
                if (bossRule && typeTwo == 1 && entrances == 1)
                {
                    int d = Distance(entranceId, typeTwoId, adj);
                    if (d >= 0 && d < minBossDistance)
                        Add(issues, IssueSeverity.Warning, li,
                            $"Этаж {human}: комната босса слишком близко ко входу ({d} шаг(ов), нужно ≥ {minBossDistance}).");
                }
                // Orphans: rooms unreachable from the floor's ROOT via corridors. Dungeons and a building's floor 0
                // root at the entrance; a building UPPER floor has no Вход, so it roots at the Лестница (the stair
                // arrival) — otherwise deleting a corridor upstairs could strand a room with no warning.
                int rootId = (isBuilding && li > 0) ? typeTwoId : entranceId;
                if (rootId != 0 && !isSettlement)
                {
                    var reached = Reachable(rootId, adj);
                    int orphans = 0;
                    foreach (var r in lvl.Rooms) if (!reached.Contains(r.Id)) orphans++;
                    if (orphans > 0)
                        Add(issues, IssueSeverity.Warning, li,
                            $"Этаж {human}: {orphans} комнат(ы) недостижимы от {(isBuilding && li > 0 ? "лестницы" : "входа")} по коридорам.");
                }

                // Dangling inter-floor targets. A secret door or a stairwell must point at a room that still
                // exists on an existing floor — "no stairs to nowhere" (a level removal / hand-edit could strand it).
                foreach (var r in lvl.Rooms)
                    foreach (var s in r.Portals)
                    {
                        if (s.Kind == PortalKind.SecretDoor)
                        {
                            if (!TargetExists(dungeon, s))
                                Add(issues, IssueSeverity.Error, li,
                                    $"Этаж {human}: секретный ход из комнаты {r.Id} ведёт в несуществующую комнату.");
                        }
                        else if (s.Kind == PortalKind.Stairs)
                        {
                            if (!TargetExists(dungeon, s))
                                Add(issues, IssueSeverity.Error, li,
                                    $"Этаж {human}: лестница из комнаты {r.Id} ведёт на несуществующий этаж.");
                        }
                    }
            }

            // Building SHAFT integrity: every Лестница must share the floor-0 column's (x,y) AND footprint — one
            // vertical shaft. Floor 0 is free-edit, so moving/resizing the column there desyncs the upper floors
            // (they are pinned to the column only at generation time); flag it so the DM re-generates the floor.
            if (isBuilding && dungeon.Floors.Count > 1)
            {
                var col0 = FindStair(dungeon.Floors[0]);
                if (col0 != null)
                    for (int li = 1; li < dungeon.Floors.Count; li++)
                    {
                        var colF = FindStair(dungeon.Floors[li]);
                        if (colF == null) continue;   // a missing column is already flagged per-floor above
                        bool aligned = System.Math.Abs(colF.X - col0.X) < ShaftTol
                                    && System.Math.Abs(colF.Y - col0.Y) < ShaftTol
                                    && colF.SizeW == col0.SizeW && colF.SizeH == col0.SizeH;
                        if (!aligned)
                            Add(issues, IssueSeverity.Error, li,
                                $"Этаж {li + 1}: лестница не совпадает со столбом 1-го этажа — перегенерируйте этаж.");
                    }
            }
            return issues;
        }

        // Normalized-coordinate tolerance for "same column position" — matches the ToNorm/ToTile round-trip.
        const float ShaftTol = 1e-3f;

        static Room FindStair(InteriorFloor lvl)
        {
            foreach (var r in lvl.Rooms) if (r.TypeId == BuildingGenerator.StairTypeId) return r;
            return null;
        }

        static void Add(List<DungeonIssue> list, IssueSeverity sev, int li, string msg)
            => list.Add(new DungeonIssue { Severity = sev, LevelIndex = li, Message = msg });

        // An inter-floor portal's target resolves iff its floor index is in range and the room still exists there.
        static bool TargetExists(InteriorData dungeon, Portal s)
            => s.TargetFloorIndex >= 0 && s.TargetFloorIndex < dungeon.Floors.Count
               && dungeon.Floors[s.TargetFloorIndex].GetRoom(s.TargetRoomId) != null;

        static Dictionary<int, HashSet<int>> BuildAdj(InteriorFloor lvl)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            foreach (var c in lvl.Links)
                if (adj.ContainsKey(c.RoomA) && adj.ContainsKey(c.RoomB)) { adj[c.RoomA].Add(c.RoomB); adj[c.RoomB].Add(c.RoomA); }
            return adj;
        }

        static HashSet<int> Reachable(int startId, Dictionary<int, HashSet<int>> adj)
        {
            var seen = new HashSet<int>();
            if (!adj.ContainsKey(startId)) return seen;
            seen.Add(startId);
            var q = new Queue<int>(); q.Enqueue(startId);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (seen.Add(nb)) q.Enqueue(nb); }
            return seen;
        }

        static int Distance(int a, int b, Dictionary<int, HashSet<int>> adj)
        {
            if (!adj.ContainsKey(a)) return -1;
            var dist = new Dictionary<int, int> { [a] = 0 };
            var q = new Queue<int>(); q.Enqueue(a);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (!dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; q.Enqueue(nb); } }
            return dist.TryGetValue(b, out var d) ? d : -1;
        }
    }
}
