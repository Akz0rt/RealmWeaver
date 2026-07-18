using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for CompactLayout — add to any GameObject, run from the Inspector.
    /// Every assertion targets the geometry a specific rule produces, so deleting that rule flips the test to
    /// FAIL (non-vacuous — the project's #1 past failure was tests that pass whether or not the rule holds).</summary>
    public class CompactLayoutSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Compact Layout")]
        public void SelfTestCompact()
        {
            bool ok = true;

            // ---- 1. Adjacency (the core rule) --------------------------------------------------------
            // A chain 1-2-3-4 (entrance=1). Adjacency-first placement must leave EACH linked pair sharing a
            // wall. Remove the flush snapping in Arrange (e.g. leave every room at the centre) and these pairs
            // OVERLAP instead of touch → AdjacentAlongWall returns false → this fails.
            var f = Chain();
            CompactLayout.Arrange(f);
            foreach (var l in f.Links)
            {
                if (!CompactLayout.AdjacentAlongWall(f.GetRoom(l.RoomA), f.GetRoom(l.RoomB)))
                { Debug.LogError($"FAIL adjacency: link {l.RoomA}-{l.RoomB} is not wall-adjacent after Arrange"); ok = false; }
            }

            // ---- 2. Predicate guard (guards AdjacentAlongWall itself from being trivially-true) -------
            // Hand-placed rooms with KNOWN geometry. If AdjacentAlongWall were `return true`, the gap and
            // corner cases fail; if it were `return false`, the flush case fails.
            var clearA = RoomAt(1, 40, 40, 4, 4);
            var clearB = RoomAt(2, 60, 40, 4, 4);   // 16-tile clear gap on X
            if (CompactLayout.AdjacentAlongWall(clearA, clearB))
            { Debug.LogError("FAIL predicate: a clear 16-tile gap reported as wall-adjacent"); ok = false; }

            var flushA = RoomAt(1, 40, 40, 4, 4);
            var flushB = RoomAt(2, 44, 40, 4, 4);   // flush on X, full Y overlap → shared vertical wall
            if (!CompactLayout.AdjacentAlongWall(flushA, flushB))
            { Debug.LogError("FAIL predicate: a flush pair NOT reported as wall-adjacent"); ok = false; }

            var cornerA = RoomAt(1, 40, 40, 4, 4);
            var cornerB = RoomAt(2, 44, 44, 4, 4);   // touch ONLY at the (42,42) corner
            if (CompactLayout.AdjacentAlongWall(cornerA, cornerB))
            { Debug.LogError("FAIL predicate: a corner-only kiss reported as wall-adjacent"); ok = false; }

            // ---- 3. No overlap after Arrange ---------------------------------------------------------
            // Independent Chebyshev overlap test (reads TilesPerAxis, never hardcodes 128). Flush touching is
            // NOT overlap; stacking rooms IS → fails if Arrange ever places two footprints intersecting.
            for (int i = 0; i < f.Rooms.Count; i++)
                for (int j = i + 1; j < f.Rooms.Count; j++)
                    if (Overlap(f.Rooms[i], f.Rooms[j]))
                    { Debug.LogError($"FAIL overlap: rooms {f.Rooms[i].Id} and {f.Rooms[j].Id} overlap after Arrange"); ok = false; }

            // ---- 4. Door lies inside the shared-wall span --------------------------------------------
            // Rooms 1 and 2 share a vertical wall (2 is flush-right of 1). The door must sit ON that wall (X)
            // and BETWEEN the overlapping Y span — not merely non-zero, not the room centre.
            if (!CompactLayout.DoorOnSharedWall(f.GetRoom(1), f.GetRoom(2), out var door))
            { Debug.LogError("FAIL door: DoorOnSharedWall(1,2) false after Arrange"); ok = false; }
            else
            {
                int T = DungeonLayout.TilesPerAxis;
                var r1 = f.GetRoom(1); var r2 = f.GetRoom(2);
                var (w1, h1) = DungeonProjection.EffectiveSize(r1);
                var (w2, h2) = DungeonProjection.EffectiveSize(r2);
                // The shared wall X = room1's right face (2 sits to its right); computed independently here.
                float wallX = r1.X * T + w1 * 0.5f;
                float yLow = Mathf.Max(r1.Y * T - h1 * 0.5f, r2.Y * T - h2 * 0.5f);
                float yHigh = Mathf.Min(r1.Y * T + h1 * 0.5f, r2.Y * T + h2 * 0.5f);
                if (Mathf.Abs(door.X - wallX) > 0.05f)
                { Debug.LogError($"FAIL door: door.X {door.X:F3} not on the shared wall {wallX:F3}"); ok = false; }
                if (door.Y < yLow - 0.05f || door.Y > yHigh + 0.05f)
                { Debug.LogError($"FAIL door: door.Y {door.Y:F3} outside overlap span [{yLow:F3},{yHigh:F3}]"); ok = false; }
            }

            // ---- 5. Determinism ----------------------------------------------------------------------
            // Two independently-built copies, Arrange both → identical X/Y everywhere. Fails if any ordering
            // (BFS, side choice, room iteration) depends on hashing or insertion order.
            var a = Chain(); var b = Chain();
            CompactLayout.Arrange(a); CompactLayout.Arrange(b);
            for (int i = 0; i < a.Rooms.Count; i++)
                if (!Mathf.Approximately(a.Rooms[i].X, b.Rooms[i].X) || !Mathf.Approximately(a.Rooms[i].Y, b.Rooms[i].Y))
                { Debug.LogError($"FAIL determinism: room {a.Rooms[i].Id} differs between runs"); ok = false; }

            Debug.Log(ok ? "Self-Test Compact Layout: PASS" : "Self-Test Compact Layout: FAIL");
        }

        [ContextMenu("Self-Test: Compact Layout Settle")]
        public void SelfTestCompactSettle()
        {
            bool ok = true;

            // ---- 6. Settle: anchor fixed, links re-adjacent, no overlaps, deterministic --------------
            var f = Chain();
            CompactLayout.Arrange(f);
            float anchorX = f.GetRoom(1).X, anchorY = f.GetRoom(1).Y;

            // Yank room 3 far away so its links (2-3, 3-4) are badly non-adjacent, then Settle around 1.
            f.GetRoom(3).X = 0.95f; f.GetRoom(3).Y = 0.5f;
            CompactLayout.Settle(f, anchorRoomId: 1);

            // Anchor must not have moved AT ALL (exact).
            if (f.GetRoom(1).X != anchorX || f.GetRoom(1).Y != anchorY)
            { Debug.LogError($"FAIL settle: anchor moved to ({f.GetRoom(1).X},{f.GetRoom(1).Y})"); ok = false; }

            // Every link pulled back to wall-adjacency (Settle rebuilds the flush tree — no fallback needed
            // on a connected chain). Fails if Settle no-ops and leaves room 3 stranded at 0.95.
            foreach (var l in f.Links)
                if (!CompactLayout.AdjacentAlongWall(f.GetRoom(l.RoomA), f.GetRoom(l.RoomB)))
                { Debug.LogError($"FAIL settle: link {l.RoomA}-{l.RoomB} not re-adjacent"); ok = false; }

            // No overlaps after Settle.
            for (int i = 0; i < f.Rooms.Count; i++)
                for (int j = i + 1; j < f.Rooms.Count; j++)
                    if (Overlap(f.Rooms[i], f.Rooms[j]))
                    { Debug.LogError($"FAIL settle: rooms {f.Rooms[i].Id} and {f.Rooms[j].Id} overlap"); ok = false; }

            // ---- 6b. Settle's overlap safety net moves a LOOSE (unlinked) room off the tree ----------
            // An orphan dropped ON room 2 is not reachable from the anchor over Links; the anchor-pinned
            // overlap pass must push it clear WITHOUT moving the anchor. Remove that pass → this overlap
            // survives → fails.
            var g = Chain();
            CompactLayout.Arrange(g);
            float gAnchorX = g.GetRoom(1).X, gAnchorY = g.GetRoom(1).Y;
            var orphan = new Room { Id = 10, TypeId = 1, SizeW = 4, SizeH = 4, X = g.GetRoom(2).X, Y = g.GetRoom(2).Y };
            g.Rooms.Add(orphan);   // NO link to it
            CompactLayout.Settle(g, anchorRoomId: 1);
            if (g.GetRoom(1).X != gAnchorX || g.GetRoom(1).Y != gAnchorY)
            { Debug.LogError("FAIL settle: anchor moved while resolving an orphan overlap"); ok = false; }
            for (int i = 0; i < g.Rooms.Count; i++)
                for (int j = i + 1; j < g.Rooms.Count; j++)
                    if (Overlap(g.Rooms[i], g.Rooms[j]))
                    { Debug.LogError($"FAIL settle: orphan overlap {g.Rooms[i].Id}/{g.Rooms[j].Id} not resolved"); ok = false; }

            // ---- 6c. Settle determinism --------------------------------------------------------------
            var c1 = SettleCase(); CompactLayout.Settle(c1, 1);
            var c2 = SettleCase(); CompactLayout.Settle(c2, 1);
            for (int i = 0; i < c1.Rooms.Count; i++)
                if (!Mathf.Approximately(c1.Rooms[i].X, c2.Rooms[i].X) || !Mathf.Approximately(c1.Rooms[i].Y, c2.Rooms[i].Y))
                { Debug.LogError($"FAIL settle: not deterministic at room {c1.Rooms[i].Id}"); ok = false; }

            Debug.Log(ok ? "Self-Test Compact Layout Settle: PASS" : "Self-Test Compact Layout Settle: FAIL");
        }

        // ------------------------------------------------------------------------------------------------
        // Fixtures & independent helpers (read DungeonLayout.TilesPerAxis + EffectiveSize — never copy them).
        // ------------------------------------------------------------------------------------------------

        // Entrance (TypeId 0) linked 1-2-3-4, every room 4×4.
        static InteriorFloor Chain()
        {
            var f = new InteriorFloor { NextRoomId = 5 };
            for (int i = 1; i <= 4; i++)
                f.Rooms.Add(new Room { Id = i, TypeId = i == 1 ? 0 : 1, SizeW = 4, SizeH = 4, X = 0.5f, Y = 0.5f });
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 2, RoomB = 3 });
            f.Links.Add(new Link { RoomA = 3, RoomB = 4 });
            return f;
        }

        // An arranged chain with room 3 yanked far away — the Settle stimulus, built fresh per call.
        static InteriorFloor SettleCase()
        {
            var f = Chain();
            CompactLayout.Arrange(f);
            f.GetRoom(3).X = 0.95f; f.GetRoom(3).Y = 0.5f;
            return f;
        }

        // A room whose CENTRE sits at TILE (tileX, tileY) with a (w,h) footprint. Converts through
        // TilesPerAxis so the hand-built geometry lives in the same space CompactLayout measures.
        static Room RoomAt(int id, float tileX, float tileY, int w, int h)
        {
            int T = DungeonLayout.TilesPerAxis;
            return new Room { Id = id, TypeId = 1, SizeW = w, SizeH = h, X = tileX / (float)T, Y = tileY / (float)T };
        }

        // Independent strict Chebyshev overlap test (reimplements the CONDITION; reads the field scale from
        // the constant, never copies 128). Flush touching (gap ≈ 0) is NOT overlap.
        static bool Overlap(Room a, Room b)
        {
            int T = DungeonLayout.TilesPerAxis;
            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float dx = Mathf.Abs((a.X - b.X) * T) - (aw + bw) * 0.5f;
            float dy = Mathf.Abs((a.Y - b.Y) * T) - (ah + bh) * 0.5f;
            return dx < -0.01f && dy < -0.01f;
        }
    }
}
