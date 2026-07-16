using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for DungeonGraphGenerator — add to any GameObject, run from
    /// the Inspector, remove after (don't save the scene).</summary>
    public class DungeonGraphGeneratorSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Dungeon Graph Generator Guarantees")]
        public void SelfTestGuarantees()
        {
            bool ok = true;
            for (int seed = 1; seed <= 20 && ok; seed++)
            {
                int want = 4 + seed % 8;                                  // 4..11 rooms
                var lvl = DungeonGraphGenerator.Generate(seed, want);

                bool countOk = lvl.Rooms.Count == want;
                int entrances = 0, bosses = 0, entranceId = 0, bossId = 0;
                var ids = new HashSet<int>();
                foreach (var r in lvl.Rooms)
                {
                    if (!ids.Add(r.Id)) countOk = false;                  // distinct ids
                    if (r.Type == RoomType.Entrance) { entrances++; entranceId = r.Id; }
                    if (r.Type == RoomType.Boss) { bosses++; bossId = r.Id; }
                }
                bool oneEntrance = entrances == 1;
                bool atMostOneBoss = bosses <= 1;
                bool connected = ReachesAll(lvl, entranceId);
                bool nextIdOk = lvl.NextRoomId == want + 1;
                bool bossFar = bosses == 0 || Distance(lvl, entranceId, bossId) >= DungeonGraphGenerator.DefaultMinBossDistance;

                if (!(countOk && oneEntrance && atMostOneBoss && connected && nextIdOk && bossFar))
                {
                    Debug.Log($"Self-Test Dungeon Graph Generator: FAIL seed={seed} (count={countOk}, entrance={oneEntrance}, boss<=1={atMostOneBoss}, connected={connected}, nextId={nextIdOk}, bossFar={bossFar})");
                    ok = false;
                }
            }
            if (ok) Debug.Log("Self-Test Dungeon Graph Generator Guarantees: PASS (seeds 1..20)");
        }

        [ContextMenu("Self-Test: Dungeon Graph Generator Determinism + Degenerate")]
        public void SelfTestDeterminismAndDegenerate()
        {
            bool ok = true;
            var a = DungeonGraphGenerator.Generate(123, 8);
            var b = DungeonGraphGenerator.Generate(123, 8);
            ok &= a.Rooms.Count == b.Rooms.Count && a.Corridors.Count == b.Corridors.Count;
            for (int i = 0; i < a.Rooms.Count && ok; i++)
                ok &= a.Rooms[i].Id == b.Rooms[i].Id && a.Rooms[i].Type == b.Rooms[i].Type
                      && Mathf.Approximately(a.Rooms[i].X, b.Rooms[i].X) && Mathf.Approximately(a.Rooms[i].Y, b.Rooms[i].Y);

            var one = DungeonGraphGenerator.Generate(1, 1);
            ok &= one.Rooms.Count == 1 && one.Rooms[0].Type == RoomType.Entrance && one.Corridors.Count == 0;
            var zero = DungeonGraphGenerator.Generate(1, 0);
            ok &= zero.Rooms.Count == 0 && zero.Corridors.Count == 0;

            Debug.Log(ok ? "Self-Test Dungeon Graph Generator Determinism + Degenerate: PASS"
                         : "Self-Test Dungeon Graph Generator Determinism + Degenerate: FAIL");
        }

        static Dictionary<int, HashSet<int>> BuildAdj(DungeonLevel lvl)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            foreach (var c in lvl.Corridors) { adj[c.RoomA].Add(c.RoomB); adj[c.RoomB].Add(c.RoomA); }
            return adj;
        }

        static bool ReachesAll(DungeonLevel lvl, int startId)
        {
            if (lvl.Rooms.Count == 0) return true;
            var adj = BuildAdj(lvl);
            var seen = new HashSet<int> { startId };
            var q = new Queue<int>(); q.Enqueue(startId);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (seen.Add(nb)) q.Enqueue(nb); }
            return seen.Count == lvl.Rooms.Count;
        }

        static int Distance(DungeonLevel lvl, int a, int b)
        {
            var adj = BuildAdj(lvl);
            var dist = new Dictionary<int, int> { [a] = 0 };
            var q = new Queue<int>(); q.Enqueue(a);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (!dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; q.Enqueue(nb); } }
            return dist.TryGetValue(b, out var d) ? d : -1;
        }
    }
}
