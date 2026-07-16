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
        public static List<DungeonIssue> Validate(DungeonData dungeon,
            int minBossDistance = DungeonGraphGenerator.DefaultMinBossDistance)
        {
            var issues = new List<DungeonIssue>();
            if (dungeon == null) return issues;

            for (int li = 0; li < dungeon.Levels.Count; li++)
            {
                var lvl = dungeon.Levels[li];
                int human = li + 1;

                int entrances = 0, entranceId = 0, bosses = 0, bossId = 0;
                foreach (var r in lvl.Rooms)
                {
                    if (r.Type == RoomType.Entrance) { entrances++; entranceId = r.Id; }
                    if (r.Type == RoomType.Boss) { bosses++; bossId = r.Id; }
                }

                if (entrances != 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: должен быть ровно один вход (сейчас {entrances}).");
                if (bosses > 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: не более одной комнаты босса (сейчас {bosses}).");
                if (bosses == 0)
                    Add(issues, IssueSeverity.Warning, li, $"Этаж {human}: нет комнаты босса — глубже только через секретный ход.");

                var adj = BuildAdj(lvl);
                if (entrances == 1)
                {
                    // Boss distance.
                    if (bosses == 1)
                    {
                        int d = Distance(entranceId, bossId, adj);
                        if (d >= 0 && d < minBossDistance)
                            Add(issues, IssueSeverity.Warning, li,
                                $"Этаж {human}: комната босса слишком близко ко входу ({d} шаг(ов), нужно ≥ {minBossDistance}).");
                    }
                    // Orphans (unreachable from the entrance via corridors).
                    var reached = Reachable(entranceId, adj);
                    int orphans = 0;
                    foreach (var r in lvl.Rooms) if (!reached.Contains(r.Id)) orphans++;
                    if (orphans > 0)
                        Add(issues, IssueSeverity.Warning, li, $"Этаж {human}: {orphans} комнат(ы) недостижимы от входа по коридорам.");
                }

                // Dangling secret targets.
                foreach (var r in lvl.Rooms)
                    foreach (var s in r.Secrets)
                        if (s.Kind == SecretTargetKind.Room)
                        {
                            bool valid = s.TargetLevelIndex >= 0 && s.TargetLevelIndex < dungeon.Levels.Count
                                         && dungeon.Levels[s.TargetLevelIndex].GetRoom(s.TargetRoomId) != null;
                            if (!valid)
                                Add(issues, IssueSeverity.Error, li,
                                    $"Этаж {human}: секретный ход из комнаты {r.Id} ведёт в несуществующую комнату.");
                        }
            }
            return issues;
        }

        static void Add(List<DungeonIssue> list, IssueSeverity sev, int li, string msg)
            => list.Add(new DungeonIssue { Severity = sev, LevelIndex = li, Message = msg });

        static Dictionary<int, HashSet<int>> BuildAdj(DungeonLevel lvl)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            foreach (var c in lvl.Corridors)
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
