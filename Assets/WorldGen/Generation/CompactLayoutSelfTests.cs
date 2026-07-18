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

        [ContextMenu("Self-Test: Settle Within Contour")]
        public void SelfTestSettleWithinContour()
        {
            bool ok = true;

            // ---- 7. CONTAIN-IF-IT-FITS: a compact floor that pokes out is slid fully inside ------------
            // The entrance sits near the box's right edge; its one linked room packs FLUSH to the right and
            // so pokes past maxX. The pair (span 8) fits the 20-wide box, so SettleWithinContour must
            // translate the whole cluster inside. Remove the contain step → the room stays out → this fails.
            const float aMinX = 40, aMinY = 40, aMaxX = 60, aMaxY = 60;
            var fitFloor = TwoRoom(entranceTileX: 58, entranceTileY: 50);
            CompactLayout.SettleWithinContour(fitFloor, anchorRoomId: 0, aMinX, aMinY, aMaxX, aMaxY);
            foreach (var r in fitFloor.Rooms)
                if (!AabbInside(r, aMinX, aMinY, aMaxX, aMaxY, 0.05f))
                { Debug.LogError($"FAIL contour-fit: room {r.Id} not inside the box after contain"); ok = false; }

            // ---- 8. TOO SMALL: a box narrower than the compact floor stays SOFT (centre, don't force) ---
            // The same pair (span 8) but a 5-wide box cannot hold it. SettleWithinContour must CENTRE the
            // cluster (as contained as possible) and LEAVE the overflow poking out — never hard-clamp both
            // rooms in. Two asserts, each non-vacuous: (a) the bbox is centred on the box (fails if the
            // contain translate is dropped); (b) some room still pokes out (fails if a hard clamp is added —
            // exactly the C2-HARD behaviour C4 replaced).
            const float bMinX = 50, bMinY = 48, bMaxX = 55, bMaxY = 52;
            var bigFloor = TwoRoom(entranceTileX: 50, entranceTileY: 50);
            CompactLayout.SettleWithinContour(bigFloor, anchorRoomId: 0, bMinX, bMinY, bMaxX, bMaxY);
            var (gMinX, _, gMaxX, _) = Bounds(bigFloor);
            if (Mathf.Abs((gMinX + gMaxX) * 0.5f - (bMinX + bMaxX) * 0.5f) > 0.05f)
            { Debug.LogError("FAIL contour-toosmall: oversized floor not centred on the box"); ok = false; }
            bool anyOut = false;
            foreach (var r in bigFloor.Rooms)
                if (!AabbInside(r, bMinX, bMinY, bMaxX, bMaxY, 0.05f)) anyOut = true;
            if (!anyOut)
            { Debug.LogError("FAIL contour-toosmall: a floor bigger than the box was hard-clamped fully inside"); ok = false; }

            // ---- 9. LEAVE-WHAT-THE-DM-PARKED-OUT: a room dropped mostly outside stays outside -----------
            // Chain 1-2-3, entrance inside the box; room 3 is DRAGGED fully outside (⇒ "mostly outside") and
            // passed as the anchor. SettleWithinContour must LEAVE room 3 where it was dropped (C2' flags it)
            // while rooms 1 & 2 re-pack compactly INSIDE the box. Remove the leave-parked rule → room 3 is
            // re-absorbed flush inside → its centre lands ≤ maxX → this fails.
            const float cMinX = 40, cMinY = 40, cMaxX = 70, cMaxY = 70;
            int T = DungeonLayout.TilesPerAxis;
            var parkFloor = ChainParked(entranceTileX: 55, entranceTileY: 50, parkedTileX: 95, parkedTileY: 50);
            CompactLayout.SettleWithinContour(parkFloor, anchorRoomId: 3, cMinX, cMinY, cMaxX, cMaxY);
            var parked = parkFloor.GetRoom(3);
            if (parked.X * T <= cMaxX)
            { Debug.LogError($"FAIL parked: room 3 was pulled back inside (cx={parked.X * T:F1}, box maxX={cMaxX})"); ok = false; }
            if (!AabbInside(parkFloor.GetRoom(1), cMinX, cMinY, cMaxX, cMaxY, 0.05f) ||
                !AabbInside(parkFloor.GetRoom(2), cMinX, cMinY, cMaxX, cMaxY, 0.05f))
            { Debug.LogError("FAIL parked: rooms 1/2 are not compact inside the box"); ok = false; }
            if (!CompactLayout.AdjacentAlongWall(parkFloor.GetRoom(1), parkFloor.GetRoom(2)))
            { Debug.LogError("FAIL parked: rooms 1-2 not wall-adjacent after settle (lost compactness)"); ok = false; }

            // ---- 10. Determinism (independent copies → identical) -------------------------------------
            var d1 = ChainParked(55, 50, 95, 50); CompactLayout.SettleWithinContour(d1, 3, cMinX, cMinY, cMaxX, cMaxY);
            var d2 = ChainParked(55, 50, 95, 50); CompactLayout.SettleWithinContour(d2, 3, cMinX, cMinY, cMaxX, cMaxY);
            for (int id = 1; id <= 3; id++)
            {
                var r1 = d1.GetRoom(id); var r2 = d2.GetRoom(id);
                if (!Mathf.Approximately(r1.X, r2.X) || !Mathf.Approximately(r1.Y, r2.Y))
                { Debug.LogError($"FAIL determinism: room {id} differs between runs"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Settle Within Contour: PASS" : "Self-Test Settle Within Contour: FAIL");
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

        // Entrance (TypeId 0) at a TILE centre + one linked 4×4 room (start pos irrelevant — Settle re-places
        // it flush from the entrance). The SettleWithinContour contain-vs-leave fixtures.
        static InteriorFloor TwoRoom(float entranceTileX, float entranceTileY)
        {
            int T = DungeonLayout.TilesPerAxis;
            var f = new InteriorFloor { NextRoomId = 3 };
            f.Rooms.Add(new Room { Id = 1, TypeId = 0, SizeW = 4, SizeH = 4, X = entranceTileX / T, Y = entranceTileY / T });
            f.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 4, SizeH = 4, X = entranceTileX / T, Y = entranceTileY / T });
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            return f;
        }

        // Chain 1(entrance)-2-3 with the entrance inside the box and room 3 pre-dragged to a mostly-outside
        // TILE position — the "parked" drop the settle must respect (room 3 is the drag anchor).
        static InteriorFloor ChainParked(float entranceTileX, float entranceTileY, float parkedTileX, float parkedTileY)
        {
            int T = DungeonLayout.TilesPerAxis;
            var f = new InteriorFloor { NextRoomId = 4 };
            f.Rooms.Add(new Room { Id = 1, TypeId = 0, SizeW = 4, SizeH = 4, X = entranceTileX / T, Y = entranceTileY / T });
            f.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 4, SizeH = 4, X = entranceTileX / T, Y = entranceTileY / T });
            f.Rooms.Add(new Room { Id = 3, TypeId = 1, SizeW = 4, SizeH = 4, X = parkedTileX / T, Y = parkedTileY / T });
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 2, RoomB = 3 });
            return f;
        }

        // Tile-space footprint bbox over all rooms (independent of ContentBoundsTiles; reads TilesPerAxis).
        static (float minX, float minY, float maxX, float maxY) Bounds(InteriorFloor f)
        {
            int T = DungeonLayout.TilesPerAxis;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var r in f.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                float cx = r.X * T, cy = r.Y * T;
                minX = Mathf.Min(minX, cx - w * 0.5f); maxX = Mathf.Max(maxX, cx + w * 0.5f);
                minY = Mathf.Min(minY, cy - h * 0.5f); maxY = Mathf.Max(maxY, cy + h * 0.5f);
            }
            return (minX, minY, maxX, maxY);
        }

        // True iff room r's tile-space footprint AABB is fully inside [minX,maxX]×[minY,maxY] (± eps).
        static bool AabbInside(Room r, float minX, float minY, float maxX, float maxY, float eps)
        {
            int T = DungeonLayout.TilesPerAxis;
            var (w, h) = DungeonProjection.EffectiveSize(r);
            float cx = r.X * T, cy = r.Y * T;
            return cx - w * 0.5f >= minX - eps && cx + w * 0.5f <= maxX + eps
                && cy - h * 0.5f >= minY - eps && cy + h * 0.5f <= maxY + eps;
        }
    }
}
