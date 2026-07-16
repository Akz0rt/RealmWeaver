using System;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for DungeonLayout — add to any GameObject, run from the Inspector.</summary>
    public class DungeonLayoutSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Dungeon Layout Separate")]
        public void SelfTestSeparate()
        {
            bool ok = true;

            // Two rooms dropped on the same point → after Separate they must not overlap.
            var lvl = new DungeonLevel();
            lvl.Rooms.Add(new Room { Id = 1, X = 0.5f, Y = 0.5f, SizeW = 4, SizeH = 4 });
            lvl.Rooms.Add(new Room { Id = 2, X = 0.5f, Y = 0.5f, SizeW = 4, SizeH = 4 });
            DungeonLayout.Separate(lvl);
            ok &= !Overlap(lvl.Rooms[0], lvl.Rooms[1], 1f);

            // A cluster of 5 overlapping rooms → none overlap after Separate.
            var lvl2 = new DungeonLevel();
            for (int i = 0; i < 5; i++) lvl2.Rooms.Add(new Room { Id = i + 1, X = 0.5f, Y = 0.5f, SizeW = 3, SizeH = 3 });
            DungeonLayout.Separate(lvl2);
            for (int i = 0; i < lvl2.Rooms.Count && ok; i++)
                for (int j = i + 1; j < lvl2.Rooms.Count && ok; j++)
                    ok &= !Overlap(lvl2.Rooms[i], lvl2.Rooms[j], 1f);

            // Positions stay in [0,1].
            foreach (var r in lvl2.Rooms) ok &= r.X >= 0f && r.X <= 1f && r.Y >= 0f && r.Y <= 1f;

            // Determinism: same input twice → same output.
            var a = Cluster(); var b = Cluster();
            DungeonLayout.Separate(a); DungeonLayout.Separate(b);
            for (int i = 0; i < a.Rooms.Count && ok; i++)
                ok &= Mathf.Approximately(a.Rooms[i].X, b.Rooms[i].X) && Mathf.Approximately(a.Rooms[i].Y, b.Rooms[i].Y);

            Debug.Log(ok ? "Self-Test Dungeon Layout Separate: PASS" : "Self-Test Dungeon Layout Separate: FAIL");
        }

        static DungeonLevel Cluster()
        {
            var l = new DungeonLevel();
            for (int i = 0; i < 6; i++) l.Rooms.Add(new Room { Id = i + 1, X = 0.4f + i * 0.02f, Y = 0.5f, SizeW = 3 + i % 2, SizeH = 3 });
            return l;
        }

        // Tile-space overlap check mirroring Separate's condition (independent reimplementation).
        static bool Overlap(Room a, Room b, float gap)
        {
            const int T = 48;
            float dx = Mathf.Abs((b.X - a.X) * T), dy = Mathf.Abs((b.Y - a.Y) * T);
            float minX = (a.SizeW + b.SizeW) * 0.5f + gap, minY = (a.SizeH + b.SizeH) * 0.5f + gap;
            return dx < minX - 0.05f && dy < minY - 0.05f;   // small epsilon for the 0.01 shove margin
        }
    }
}
