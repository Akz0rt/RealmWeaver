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
            ok &= !Overlap(lvl.Rooms[0], lvl.Rooms[1], 0.1f);

            // A cluster of 5 overlapping rooms → none overlap after Separate.
            var lvl2 = new DungeonLevel();
            for (int i = 0; i < 5; i++) lvl2.Rooms.Add(new Room { Id = i + 1, X = 0.5f, Y = 0.5f, SizeW = 3, SizeH = 3 });
            DungeonLayout.Separate(lvl2);
            for (int i = 0; i < lvl2.Rooms.Count && ok; i++)
                for (int j = i + 1; j < lvl2.Rooms.Count && ok; j++)
                    ok &= !Overlap(lvl2.Rooms[i], lvl2.Rooms[j], 0.1f);

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
        // The CONDITION is what this reimplements independently; the field scale is not — it reads
        // DungeonLayout.TilesPerAxis. A local copy of it (this was `const int T = 48;`) silently went
        // stale the moment the field grew to 128, leaving the check running at the wrong scale.
        static bool Overlap(Room a, Room b, float gap)
        {
            int T = DungeonLayout.TilesPerAxis;
            float dx = Mathf.Abs((b.X - a.X) * T), dy = Mathf.Abs((b.Y - a.Y) * T);
            float minX = (a.SizeW + b.SizeW) * 0.5f + gap, minY = (a.SizeH + b.SizeH) * 0.5f + gap;
            return dx < minX - 0.05f && dy < minY - 0.05f;   // small epsilon for the 0.01 shove margin
        }

        [ContextMenu("Self-Test: Dungeon Render Graph Junctions")]
        public void SelfTestRenderGraph()
        {
            bool ok = true;

            // Two corridors that cross (an X) → 1 junction, 4 segments.
            var lvl = new DungeonLevel { NextRoomId = 5 };
            lvl.Rooms.Add(new Room { Id = 1, X = 0.2f, Y = 0.2f });
            lvl.Rooms.Add(new Room { Id = 2, X = 0.8f, Y = 0.8f });
            lvl.Rooms.Add(new Room { Id = 3, X = 0.8f, Y = 0.2f });
            lvl.Rooms.Add(new Room { Id = 4, X = 0.2f, Y = 0.8f });
            lvl.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });   // ↘
            lvl.Corridors.Add(new Corridor { RoomA = 3, RoomB = 4 });   // ↙  crosses at center
            var g = DungeonLayout.BuildRenderGraph(lvl);
            ok &= g.Junctions.Count == 1 && g.Segments.Count == 4;
            ok &= Mathf.Abs(g.Junctions[0].X - 0.5f) < 0.02f && Mathf.Abs(g.Junctions[0].Y - 0.5f) < 0.02f;

            // Two corridors that DON'T cross → 0 junctions, 2 segments.
            var lvl2 = new DungeonLevel();
            lvl2.Rooms.Add(new Room { Id = 1, X = 0.1f, Y = 0.1f });
            lvl2.Rooms.Add(new Room { Id = 2, X = 0.3f, Y = 0.1f });
            lvl2.Rooms.Add(new Room { Id = 3, X = 0.1f, Y = 0.9f });
            lvl2.Rooms.Add(new Room { Id = 4, X = 0.3f, Y = 0.9f });
            lvl2.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            lvl2.Corridors.Add(new Corridor { RoomA = 3, RoomB = 4 });
            var g2 = DungeonLayout.BuildRenderGraph(lvl2);
            ok &= g2.Junctions.Count == 0 && g2.Segments.Count == 2;

            // Corridors that share a room endpoint must NOT count as a crossing.
            var lvl3 = new DungeonLevel();
            lvl3.Rooms.Add(new Room { Id = 1, X = 0.5f, Y = 0.2f });
            lvl3.Rooms.Add(new Room { Id = 2, X = 0.2f, Y = 0.8f });
            lvl3.Rooms.Add(new Room { Id = 3, X = 0.8f, Y = 0.8f });
            lvl3.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            lvl3.Corridors.Add(new Corridor { RoomA = 1, RoomB = 3 });
            var g3 = DungeonLayout.BuildRenderGraph(lvl3);
            ok &= g3.Junctions.Count == 0 && g3.Segments.Count == 2;

            Debug.Log(ok ? "Self-Test Dungeon Render Graph Junctions: PASS" : "Self-Test Dungeon Render Graph Junctions: FAIL");
        }
    }
}
