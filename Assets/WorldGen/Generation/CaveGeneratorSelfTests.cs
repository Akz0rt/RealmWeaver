using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for CaveGenerator — add to any GameObject, run from the
    /// Inspector, remove after (don't save the scene). Verifies the generator's guarantees.</summary>
    public class CaveGeneratorSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Cave Generator Guarantees")]
        public void SelfTestGuarantees()
        {
            bool ok = true;
            for (int seed = 1; seed <= 20 && ok; seed++)
            {
                int want = 4 + seed % 7;                       // 4..10 chambers
                var lvl = CaveGenerator.Generate(seed, 48, 48, want, 0.5f);

                bool countOk = lvl.Chambers.Count == want;
                bool chambersOnFloor = true, chambersDistinct = true;
                var seen = new System.Collections.Generic.HashSet<(int, int)>();
                foreach (var c in lvl.Chambers)
                {
                    if (lvl.Get(c.MarkerCellX, c.MarkerCellY) != DungeonTile.Floor) chambersOnFloor = false;
                    if (!seen.Add((c.MarkerCellX, c.MarkerCellY))) chambersDistinct = false;
                }
                // one connected component: flood from chamber 0 must reach ALL floor tiles
                bool oneComponent = FloodReachesAllFloor(lvl);
                bool numbered = true;
                for (int i = 0; i < lvl.Chambers.Count; i++) if (lvl.Chambers[i].Number != i + 1) numbered = false;

                if (!(countOk && chambersOnFloor && chambersDistinct && oneComponent && numbered))
                {
                    Debug.Log($"Self-Test Cave Generator: FAIL seed={seed} (count={countOk}, onFloor={chambersOnFloor}, distinct={chambersDistinct}, connected={oneComponent}, numbered={numbered})");
                    ok = false;
                }
            }
            if (ok) Debug.Log("Self-Test Cave Generator Guarantees: PASS (seeds 1..20)");
        }

        static bool FloodReachesAllFloor(DungeonLevel lvl)
        {
            var c0 = lvl.Chambers.Count > 0 ? lvl.Chambers[0] : null;
            if (c0 == null) return false;
            var reached = new bool[lvl.Tiles.Length];
            var q = new System.Collections.Generic.Queue<int>();
            int start = c0.MarkerCellY * lvl.Width + c0.MarkerCellX;
            reached[start] = true; q.Enqueue(start);
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int idx = q.Dequeue(); int x = idx % lvl.Width, y = idx / lvl.Width;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (!lvl.InBounds(nx, ny)) continue;
                    int ni = ny * lvl.Width + nx;
                    if (reached[ni] || lvl.Tiles[ni] != DungeonTile.Floor) continue;
                    reached[ni] = true; q.Enqueue(ni);
                }
            }
            for (int i = 0; i < lvl.Tiles.Length; i++)
                if (lvl.Tiles[i] == DungeonTile.Floor && !reached[i]) return false;
            return true;
        }
    }
}
