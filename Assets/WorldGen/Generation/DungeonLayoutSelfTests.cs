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

        [ContextMenu("Self-Test: Corridor Leash")]
        public void SelfTestCorridorLeash()
        {
            bool ok = true;

            // A chain 1-2-3. Drag room 1 far away; 2 must follow, and 3 must follow 2 ("stitched with threads").
            var lvl = new DungeonLevel { NextRoomId = 4 };
            lvl.Rooms.Add(new Room { Id = 1, X = 0.5f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            lvl.Rooms.Add(new Room { Id = 2, X = 0.55f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            lvl.Rooms.Add(new Room { Id = 3, X = 0.60f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            lvl.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            lvl.Corridors.Add(new Corridor { RoomA = 2, RoomB = 3 });

            lvl.GetRoom(1).X = 0.05f;                      // the DM yanks room 1 to the far left
            DungeonLayout.EnforceCorridorLeash(lvl, 1);

            // The anchor must not have moved — the dragged room follows the cursor exactly.
            if (Mathf.Abs(lvl.GetRoom(1).X - 0.05f) > 1e-4f)
            { Debug.LogError($"FAIL: anchor moved to {lvl.GetRoom(1).X}"); ok = false; }

            foreach (var c in lvl.Corridors)
            {
                float gap = LeashGap(lvl.GetRoom(c.RoomA), lvl.GetRoom(c.RoomB));
                if (gap > DungeonLayout.MaxCorridorTiles + 0.1f)
                { Debug.LogError($"FAIL: corridor {c.RoomA}-{c.RoomB} still {gap:F1} tiles > {DungeonLayout.MaxCorridorTiles}"); ok = false; }
            }

            // Room 3 is two hops from the anchor — proving the pull PROPAGATES, not just to direct neighbours.
            if (lvl.GetRoom(3).X > 0.55f)
            { Debug.LogError($"FAIL: room 3 (2 hops out) never followed — X={lvl.GetRoom(3).X:F3}"); ok = false; }

            // A layout already within the leash must not be touched at all (no drift on every drag sample).
            var calm = new DungeonLevel { NextRoomId = 3 };
            calm.Rooms.Add(new Room { Id = 1, X = 0.50f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            calm.Rooms.Add(new Room { Id = 2, X = 0.56f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            calm.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            float beforeX = calm.GetRoom(2).X;
            DungeonLayout.EnforceCorridorLeash(calm, 1);
            if (Mathf.Abs(calm.GetRoom(2).X - beforeX) > 1e-5f)
            { Debug.LogError($"FAIL: a slack corridor was disturbed ({beforeX} → {calm.GetRoom(2).X})"); ok = false; }

            // Deterministic: same input, same result.
            var a1 = MakeLeashCase(); DungeonLayout.EnforceCorridorLeash(a1, 1);
            var a2 = MakeLeashCase(); DungeonLayout.EnforceCorridorLeash(a2, 1);
            for (int i = 0; i < a1.Rooms.Count; i++)
                if (Mathf.Abs(a1.Rooms[i].X - a2.Rooms[i].X) > 1e-6f || Mathf.Abs(a1.Rooms[i].Y - a2.Rooms[i].Y) > 1e-6f)
                { Debug.LogError("FAIL: leash is not deterministic"); ok = false; break; }

            // DIAGONAL chain — the only case where one step cannot fully close a Chebyshev gap, so this is
            // what proves the iteration converges rather than stalling.
            var diag = new DungeonLevel { NextRoomId = 3 };
            diag.Rooms.Add(new Room { Id = 1, X = 0.10f, Y = 0.10f, SizeW = 6, SizeH = 6 });
            diag.Rooms.Add(new Room { Id = 2, X = 0.90f, Y = 0.90f, SizeW = 6, SizeH = 6 });
            diag.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            DungeonLayout.EnforceCorridorLeash(diag, 1);
            float dgap = LeashGap(diag.GetRoom(1), diag.GetRoom(2));
            if (dgap > DungeonLayout.MaxCorridorTiles + 0.1f)
            { Debug.LogError($"FAIL diagonal: gap {dgap:F1} > {DungeonLayout.MaxCorridorTiles} — iteration stalled"); ok = false; }

            // LOOP EDGE — two rooms at equal graph distance from the anchor. Exercises the id tie-break;
            // if it were unstable these two would fight and the gap would never settle.
            var loop = new DungeonLevel { NextRoomId = 4 };
            loop.Rooms.Add(new Room { Id = 1, X = 0.50f, Y = 0.20f, SizeW = 6, SizeH = 6 });
            loop.Rooms.Add(new Room { Id = 2, X = 0.05f, Y = 0.80f, SizeW = 6, SizeH = 6 });
            loop.Rooms.Add(new Room { Id = 3, X = 0.95f, Y = 0.80f, SizeW = 6, SizeH = 6 });
            loop.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            loop.Corridors.Add(new Corridor { RoomA = 1, RoomB = 3 });
            loop.Corridors.Add(new Corridor { RoomA = 2, RoomB = 3 });   // the loop edge: 2 and 3 are both 1 hop out
            DungeonLayout.EnforceCorridorLeash(loop, 1);
            foreach (var c in loop.Corridors)
            {
                float g = LeashGap(loop.GetRoom(c.RoomA), loop.GetRoom(c.RoomB));
                if (g > DungeonLayout.MaxCorridorTiles + 0.1f)
                { Debug.LogError($"FAIL loop edge {c.RoomA}-{c.RoomB}: gap {g:F1} never settled"); ok = false; }
            }

            // A DISCONNECTED COMPONENT must never be pulled — this is the int.MaxValue branch. (An orphan
            // room in zero corridors cannot test it: the leash iterates corridors, so it is unreachable by
            // construction rather than by the distance logic.)
            var split = MakeLeashCase();
            split.Rooms.Add(new Room { Id = 10, X = 0.90f, Y = 0.90f, SizeW = 6, SizeH = 6 });
            split.Rooms.Add(new Room { Id = 11, X = 0.95f, Y = 0.95f, SizeW = 6, SizeH = 6 });
            split.Corridors.Add(new Corridor { RoomA = 10, RoomB = 11 });   // linked to each other, not to the anchor
            DungeonLayout.EnforceCorridorLeash(split, 1);
            if (Mathf.Abs(split.GetRoom(10).X - 0.90f) > 1e-5f || Mathf.Abs(split.GetRoom(11).X - 0.95f) > 1e-5f)
            { Debug.LogError("FAIL: a disconnected component was pulled"); ok = false; }

            Debug.Log(ok ? "PASS: Corridor Leash" : "FAIL: Corridor Leash");
        }

        static DungeonLevel MakeLeashCase()
        {
            var l = new DungeonLevel { NextRoomId = 4 };
            l.Rooms.Add(new Room { Id = 1, X = 0.05f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            l.Rooms.Add(new Room { Id = 2, X = 0.55f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            l.Rooms.Add(new Room { Id = 3, X = 0.60f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            l.Corridors.Add(new Corridor { RoomA = 1, RoomB = 2 });
            l.Corridors.Add(new Corridor { RoomA = 2, RoomB = 3 });
            return l;
        }

        // Same Chebyshev edge gap the leash itself uses (independent reimplementation of the CONDITION;
        // the field scale is read from the constant, never copied — see Overlap's comment).
        static float LeashGap(Room a, Room b)
        {
            int T = DungeonLayout.TilesPerAxis;
            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float dx = Mathf.Abs((b.X - a.X) * T) - (aw + bw) * 0.5f;
            float dy = Mathf.Abs((b.Y - a.Y) * T) - (ah + bh) * 0.5f;
            return Mathf.Max(dx, dy);
        }
    }
}
