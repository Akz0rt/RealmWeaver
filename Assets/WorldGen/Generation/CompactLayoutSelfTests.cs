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

            // ---- 5. Determinism (independent of Link insertion order) --------------------------------
            // SAME branching graph, but the two copies insert the identical Link SET in a DIFFERENT order.
            // Arrange must produce identical layouts regardless — that is exactly what BuildAdjacency's
            // neighbour .Sort() guarantees. On the hub (room 1 has THREE unplaced neighbours) removing that
            // sort makes BFS place neighbours in insertion order, so the two copies DIVERGE → this fails. A
            // linear Chain could never detect it (every node has ≤1 unplaced neighbour, so order is moot).
            var a = Branching(false); var b = Branching(true);
            CompactLayout.Arrange(a); CompactLayout.Arrange(b);
            for (int id = 1; id <= 4; id++)
            {
                var ra = a.GetRoom(id); var rb = b.GetRoom(id);
                if (!Mathf.Approximately(ra.X, rb.X) || !Mathf.Approximately(ra.Y, rb.Y))
                { Debug.LogError($"FAIL determinism: room {id} differs under permuted link order"); ok = false; }
            }

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

            // ---- 6c. Settle determinism (independent of Link insertion order) ------------------------
            // Same branching hub, permuted Link order, one leaf perturbed identically in both; Settle both
            // around anchor 1 → identical. Settle's re-pack shares BuildAdjacency, so removing the neighbour
            // .Sort() makes the permuted copy diverge here too.
            var c1 = BranchingSettleCase(false); CompactLayout.Settle(c1, 1);
            var c2 = BranchingSettleCase(true);  CompactLayout.Settle(c2, 1);
            for (int id = 1; id <= 4; id++)
            {
                var r1 = c1.GetRoom(id); var r2 = c2.GetRoom(id);
                if (!Mathf.Approximately(r1.X, r2.X) || !Mathf.Approximately(r1.Y, r2.Y))
                { Debug.LogError($"FAIL settle: not deterministic under permuted link order at room {id}"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Compact Layout Settle: PASS" : "Self-Test Compact Layout Settle: FAIL");
        }

        [ContextMenu("Self-Test: Nudge Off Overlaps")]
        public void SelfTestNudgeOffOverlaps()
        {
            bool ok = true;
            int T = DungeonLayout.TilesPerAxis;

            // ---- 7. Dragged room shoved off overlaps; NO other room moves (the CORE rule) --------------
            // Three flush 4×4 rooms in a row. Room 3 is DRAGGED on top of rooms 1 & 2 (dropped at tile 51 —
            // overlaps BOTH). NudgeRoomOffOverlaps(f, 3) must clear room 3's overlaps by moving ONLY room 3;
            // rooms 1 & 2 must stay EXACTLY put (their X/Y are never written). (a) fails if the anti-overlap
            // shove is removed (room 3 stays overlapping); (b) fails if the drag ever re-packs the floor (the
            // rejected model) — that would move rooms 1/2. Non-vacuous in BOTH directions.
            var f = FlushRow();
            float r1x = f.GetRoom(1).X, r1y = f.GetRoom(1).Y;
            float r2x = f.GetRoom(2).X, r2y = f.GetRoom(2).Y;
            f.GetRoom(3).X = 51f / T; f.GetRoom(3).Y = 50f / T;   // dropped ON rooms 1 & 2
            CompactLayout.NudgeRoomOffOverlaps(f, 3);
            if (Overlap(f.GetRoom(3), f.GetRoom(1)) || Overlap(f.GetRoom(3), f.GetRoom(2)))
            { Debug.LogError("FAIL nudge: room 3 still overlaps another room after NudgeRoomOffOverlaps"); ok = false; }
            if (f.GetRoom(1).X != r1x || f.GetRoom(1).Y != r1y || f.GetRoom(2).X != r2x || f.GetRoom(2).Y != r2y)
            { Debug.LogError("FAIL nudge: a NON-dragged room moved (only the dragged room may move)"); ok = false; }

            // ---- 8. A room dropped in FREE space is NOT moved (stays exactly where dropped) ------------
            // The whole point of the revised model: no overlap ⇒ no correction. Room 3 dropped clear at
            // (58,60). NudgeRoomOffOverlaps must be a NO-OP — fails if it re-packs / relocates a room that
            // isn't overlapping anything.
            var g = FlushRow();
            g.GetRoom(3).X = 58f / T; g.GetRoom(3).Y = 60f / T;
            CompactLayout.NudgeRoomOffOverlaps(g, 3);
            if (Mathf.Abs(g.GetRoom(3).X - 58f / T) > 1e-5f || Mathf.Abs(g.GetRoom(3).Y - 60f / T) > 1e-5f)
            { Debug.LogError("FAIL nudge: a non-overlapping dropped room was moved (must stay put)"); ok = false; }
            // The others obviously must not move either.
            if (g.GetRoom(1).X != 50f / T || g.GetRoom(2).X != 54f / T)
            { Debug.LogError("FAIL nudge: a non-dragged room moved on a free-space drop"); ok = false; }

            // ---- 9. Determinism (independent copies of the overlap case → identical) -------------------
            var d1 = FlushRow(); d1.GetRoom(3).X = 51f / T; d1.GetRoom(3).Y = 50f / T; CompactLayout.NudgeRoomOffOverlaps(d1, 3);
            var d2 = FlushRow(); d2.GetRoom(3).X = 51f / T; d2.GetRoom(3).Y = 50f / T; CompactLayout.NudgeRoomOffOverlaps(d2, 3);
            if (!Mathf.Approximately(d1.GetRoom(3).X, d2.GetRoom(3).X) || !Mathf.Approximately(d1.GetRoom(3).Y, d2.GetRoom(3).Y))
            { Debug.LogError("FAIL nudge: NudgeRoomOffOverlaps not deterministic"); ok = false; }

            // ---- 10. TRAPPED narrow gap — least-pen alone can't clear → guaranteed-clear fallback ------
            // Two FIXED 4×4 rooms at tile 50 and 56 leave a 2-tile gap [52,54] — NARROWER than the 4-wide
            // dragged room. Room 3 dropped at 53 overlaps BOTH; the least-penetration shove alone oscillates
            // 51.9↔54.1 and stops STILL overlapping (no on-axis position fits the gap). NudgeRoomOffOverlaps
            // must fall back to a nearest-free-slot relocation so room 3 ends clear of both — while rooms 1 & 2
            // stay EXACTLY put. Remove the fallback → room 3 stays overlapping room 1 → assert (a) fails.
            var t = new InteriorFloor { NextRoomId = 4 };
            t.Rooms.Add(RoomAt(1, 50, 50, 4, 4));
            t.Rooms.Add(RoomAt(2, 56, 50, 4, 4));
            t.Rooms.Add(RoomAt(3, 53, 50, 4, 4));   // dropped into the too-narrow gap, overlapping both
            float t1x = t.GetRoom(1).X, t1y = t.GetRoom(1).Y, t2x = t.GetRoom(2).X, t2y = t.GetRoom(2).Y;
            CompactLayout.NudgeRoomOffOverlaps(t, 3);
            if (Overlap(t.GetRoom(3), t.GetRoom(1)) || Overlap(t.GetRoom(3), t.GetRoom(2)))
            { Debug.LogError("FAIL nudge-trap: room 3 still overlaps after fallback (least-pen trap not escaped)"); ok = false; }
            if (t.GetRoom(1).X != t1x || t.GetRoom(1).Y != t1y || t.GetRoom(2).X != t2x || t.GetRoom(2).Y != t2y)
            { Debug.LogError("FAIL nudge-trap: a fixed room moved during the fallback relocation"); ok = false; }

            Debug.Log(ok ? "Self-Test Nudge Off Overlaps: PASS" : "Self-Test Nudge Off Overlaps: FAIL");
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

        // A HUB: entrance 1 linked to 2, 3 AND 4 — so room 1 has three unplaced neighbours and neighbour
        // ORDER actually matters (unlike the linear Chain). `permuted` inserts the SAME Link set in a
        // different order; comparing Branching(false) vs Branching(true) is what makes the determinism
        // tests non-vacuous w.r.t. BuildAdjacency's neighbour .Sort().
        static InteriorFloor Branching(bool permuted)
        {
            var f = new InteriorFloor { NextRoomId = 5 };
            for (int i = 1; i <= 4; i++)
                f.Rooms.Add(new Room { Id = i, TypeId = i == 1 ? 0 : 1, SizeW = 4, SizeH = 4, X = 0.5f, Y = 0.5f });
            if (permuted)
            {
                f.Links.Add(new Link { RoomA = 1, RoomB = 4 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 3 });
            }
            else
            {
                f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 3 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 4 });
            }
            return f;
        }

        // An arranged branching hub with one leaf yanked far away — the Settle determinism stimulus. The
        // perturbation is identical regardless of `permuted`, so any divergence comes only from link order.
        static InteriorFloor BranchingSettleCase(bool permuted)
        {
            var f = Branching(permuted);
            CompactLayout.Arrange(f);
            f.GetRoom(4).X = 0.95f; f.GetRoom(4).Y = 0.5f;
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

        // Three flush 4×4 rooms in a row at KNOWN tile centres (50,50)-(54,50)-(58,50) — each pair TOUCHES,
        // none overlaps. No Links needed: NudgeRoomOffOverlaps is pure geometry (it never reads Links). The
        // revised-C4 drag fixture — drop one room ON another (overlap) or into clear space (no-op).
        static InteriorFloor FlushRow()
        {
            var f = new InteriorFloor { NextRoomId = 4 };
            f.Rooms.Add(RoomAt(1, 50, 50, 4, 4));
            f.Rooms.Add(RoomAt(2, 54, 50, 4, 4));
            f.Rooms.Add(RoomAt(3, 58, 50, 4, 4));
            return f;
        }
    }
}
