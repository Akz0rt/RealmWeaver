using System.Collections.Generic;
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

            // ---- 10b. A REAL overlap resolves FLUSH, not with a gap (the ShoveClearance bug) -----------
            // Room 2 penetrates room 1 deeper on X (dx=0, full X overlap: overlapX=4) than on Y (dy=3, a
            // shallow 1-tile Y overlap: overlapY=1) — a genuine penetration, not a touch. The least-penetration
            // rule must shove along Y (the shallower axis, overlapY=1 < overlapX=4). Three assertions:
            // (a) the overlap predicate itself reports clear (IsFree semantics); (b) AdjacentAlongWall reports
            // the pair as sharing a wall, i.e. the resulting Chebyshev edge gap is <= TouchEps, so a door
            // will render — before the ShoveClearance fix, the shove added a hard-coded 0.1 tiles (5x TouchEps)
            // of clearance, so the resolved gap sat at ≈0.1, OUTSIDE TouchEps (0.02), and (b) FAILED; and
            // (c) the axis that actually MOVED is Y (room 2's Y coordinate changed) and X did NOT move.
            // (a) and (b) alone do NOT pin down which axis moved: shoving fully along EITHER axis by that
            // axis's own (overlap + ShoveClearance) drives that axis's edge gap to ≈ShoveClearance (a touch)
            // while the other axis keeps its full original overlap — and AdjacentAlongWall only checks "one
            // axis touches, the other overlaps," which is equally true whichever axis was shoved (it just
            // reports a vertical wall instead of a horizontal one, or vice versa). Hand-verified: with the
            // branch flipped to `if (overlapX > overlapY)`, this fixture shoves X instead (mx += overlapX +
            // ShoveClearance = 54.005, my stays 53) — room 2 lands at (54.005, 53) instead of (50, 54.005);
            // (a) and (b) both STILL pass (AdjacentAlongWall now reports a vertical wall), but (c) catches it:
            // X moved (50→54.005) when it must not, and Y never moved. (c) is what actually proves the axis
            // choice; (a)+(b) alone are vacuous to it.
            var h = new InteriorFloor { NextRoomId = 3 };
            h.Rooms.Add(RoomAt(1, 50, 50, 4, 4));
            h.Rooms.Add(RoomAt(2, 50, 53, 4, 4));   // dx=0 (deep X overlap), dy=3 (shallow Y overlap)
            float h2xBefore = h.GetRoom(2).X, h2yBefore = h.GetRoom(2).Y;
            CompactLayout.NudgeRoomOffOverlaps(h, 2);
            if (Overlap(h.GetRoom(2), h.GetRoom(1)))
            { Debug.LogError("FAIL nudge-flush: room 2 still overlaps room 1 after the shove"); ok = false; }
            if (!CompactLayout.AdjacentAlongWall(h.GetRoom(1), h.GetRoom(2)))
            { Debug.LogError("FAIL nudge-flush: shove left a gap wider than TouchEps — no door would render (this is the pre-fix bug: a hard-coded 0.1-tile clearance)"); ok = false; }
            if (Mathf.Abs(h.GetRoom(2).X - h2xBefore) > 1e-4f)
            { Debug.LogError("FAIL nudge-flush-axis: room 2's X moved — the least-penetration axis here is Y, X must stay put"); ok = false; }
            if (Mathf.Abs(h.GetRoom(2).Y - h2yBefore) <= 1e-4f)
            { Debug.LogError("FAIL nudge-flush-axis: room 2's Y did not move — the least-penetration axis (Y) must be the one shoved"); ok = false; }

            // ---- 10c. Least-penetration AXIS choice — mirror of 10b, deep/shallow axes swapped -----------
            // Same pair, but now room 2 penetrates deeper on Y (dy=0, overlapY=4) than on X (dx=3, overlapX=1):
            // the shove must go along X this time (overlapX=1 < overlapY=4). Mirrors 10b's three assertions
            // with the moved/unmoved axes swapped: (a) no overlap; (b) AdjacentAlongWall; (c) X moved, Y did
            // not. As in 10b, (a)+(b) alone are vacuous to axis choice — see 10b's comment for why. Hand-
            // verified: with the branch flipped to `if (overlapX > overlapY)`, this fixture shoves Y instead
            // (my += overlapY + ShoveClearance = 54.005, mx stays 53) — room 2 lands at (53, 54.005) instead
            // of (54.005, 50); (a) and (b) both STILL pass (AdjacentAlongWall reports a horizontal wall
            // instead of the correct vertical one), but (c) catches it: X never moved when it must, and Y
            // moved (50→54.005) when it must not. Together, 10b and 10c's (c) assertions are what actually
            // prove the fix cannot be "passed" by shoving the wrong axis — (a) and (b) cannot.
            var k = new InteriorFloor { NextRoomId = 3 };
            k.Rooms.Add(RoomAt(1, 50, 50, 4, 4));
            k.Rooms.Add(RoomAt(2, 53, 50, 4, 4));   // dy=0 (deep Y overlap), dx=3 (shallow X overlap)
            float k2xBefore = k.GetRoom(2).X, k2yBefore = k.GetRoom(2).Y;
            CompactLayout.NudgeRoomOffOverlaps(k, 2);
            if (Overlap(k.GetRoom(2), k.GetRoom(1)))
            { Debug.LogError("FAIL nudge-flush-axis: room 2 still overlaps room 1 after the shove"); ok = false; }
            if (!CompactLayout.AdjacentAlongWall(k.GetRoom(1), k.GetRoom(2)))
            { Debug.LogError("FAIL nudge-flush-axis: shove along the wrong axis left a gap wider than TouchEps"); ok = false; }
            if (Mathf.Abs(k.GetRoom(2).Y - k2yBefore) > 1e-4f)
            { Debug.LogError("FAIL nudge-flush-axis: room 2's Y moved — the least-penetration axis here is X, Y must stay put"); ok = false; }
            if (Mathf.Abs(k.GetRoom(2).X - k2xBefore) <= 1e-4f)
            { Debug.LogError("FAIL nudge-flush-axis: room 2's X did not move — the least-penetration axis (X) must be the one shoved"); ok = false; }

            Debug.Log(ok ? "Self-Test Nudge Off Overlaps: PASS" : "Self-Test Nudge Off Overlaps: FAIL");
        }

        [ContextMenu("Self-Test: Floor Footprint")]
        public void SelfTestFloorFootprint()
        {
            bool ok = true;
            float m = FloorFootprint.ContourMargin;

            // L-shaped floor 0: rooms at (50,50), (56,50), (50,56) — the bottom-right quadrant (~57,57) is
            // EMPTY. A bbox contour would COVER that corner; the footprint SHAPE must not. 6×6 rooms so the
            // empty corner survives the outward margin.
            var f = new InteriorFloor { NextRoomId = 4 };
            f.Rooms.Add(RoomAt(1, 50, 50, 6, 6));
            f.Rooms.Add(RoomAt(2, 56, 50, 6, 6));
            f.Rooms.Add(RoomAt(3, 50, 56, 6, 6));

            // ---- 11. Room interiors covered; empty bbox corner NOT (the "hug the shape" property) ----------
            if (!FloorFootprint.CoversPoint(f, m, 50, 50) || !FloorFootprint.CoversPoint(f, m, 56, 50) || !FloorFootprint.CoversPoint(f, m, 50, 56))
            { Debug.LogError("FAIL footprint: a room centre is not inside the footprint"); ok = false; }
            if (FloorFootprint.CoversPoint(f, m, 57, 57))
            { Debug.LogError("FAIL footprint: the empty bbox corner is inside the footprint (bbox, not shape)"); ok = false; }

            // ---- 12. ContainsRect: over a room = inside; over the empty corner = outside (→ red-flagged) ----
            if (!FloorFootprint.ContainsRect(f, m, 50, 50, 6, 6))
            { Debug.LogError("FAIL footprint: a floor-0 room reads as outside its own footprint"); ok = false; }
            if (FloorFootprint.ContainsRect(f, m, 57, 57, 6, 6))
            { Debug.LogError("FAIL footprint: a rect in the empty corner reads as inside the footprint"); ok = false; }

            // ---- 13. Outline has a CONCAVE-NOTCH segment strictly inside the bbox ---------------------------
            // Impossible for a bbox rectangle (its every edge lies on the bbox border), so this proves the
            // outline traces the SHAPE. Fails if OutlineSegments returns a plain bounding rectangle.
            var segs = FloorFootprint.OutlineSegments(f, m);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in segs)
            {
                minX = Mathf.Min(minX, Mathf.Min(s.x0, s.x1)); maxX = Mathf.Max(maxX, Mathf.Max(s.x0, s.x1));
                minY = Mathf.Min(minY, Mathf.Min(s.y0, s.y1)); maxY = Mathf.Max(maxY, Mathf.Max(s.y0, s.y1));
            }
            bool interiorSeg = false;
            foreach (var s in segs)
            {
                bool vertical = Mathf.Approximately(s.x0, s.x1);
                if (vertical && s.x0 > minX + 0.1f && s.x0 < maxX - 0.1f) interiorSeg = true;
                if (!vertical && s.y0 > minY + 0.1f && s.y0 < maxY - 0.1f) interiorSeg = true;
            }
            if (segs.Count == 0 || !interiorSeg)
            { Debug.LogError("FAIL footprint: outline has no interior (concave-notch) segment — tracing a bbox, not the shape"); ok = false; }

            Debug.Log(ok ? "Self-Test Floor Footprint: PASS" : "Self-Test Floor Footprint: FAIL");
        }

        [ContextMenu("Self-Test: New Room Placement")]
        public void SelfTestNewRoomPlacement()
        {
            bool ok = true;
            int T = DungeonLayout.TilesPerAxis;
            float m = FloorFootprint.ContourMargin;

            // ---- 14. AttachNewRoom (ground floor): a + room snaps flush to the building --------------------
            // Two flush rooms + a new room dropped FAR away. AttachNewRoom must snap it wall-adjacent to the
            // nearest existing room (touching, non-overlapping) — it becomes part of the building, never floats
            // outside. Fails if it no-ops (stays far, not adjacent).
            var f = new InteriorFloor { NextRoomId = 4 };
            f.Rooms.Add(RoomAt(1, 50, 50, 4, 4));
            f.Rooms.Add(RoomAt(2, 54, 50, 4, 4));
            f.Rooms.Add(RoomAt(3, 90, 90, 4, 4));   // the "new" room, dropped far away
            CompactLayout.AttachNewRoom(f, 3);
            var nr = f.GetRoom(3);
            if (Overlap(nr, f.GetRoom(1)) || Overlap(nr, f.GetRoom(2)))
            { Debug.LogError("FAIL attach: new room overlaps an existing room"); ok = false; }
            if (!CompactLayout.AdjacentAlongWall(nr, f.GetRoom(1)) && !CompactLayout.AdjacentAlongWall(nr, f.GetRoom(2)))
            { Debug.LogError("FAIL attach: new room not wall-adjacent to the building (didn't attach)"); ok = false; }

            // ---- 15. PlaceNewRoomInContour (upper floor): a + room lands INSIDE the contour, not outside ---
            // Contour = one big ground room; an upper floor with one room in the middle + a new room dropped
            // far outside. It must be moved to a spot whose CENTRE is inside the contour, and (space existing)
            // not overlapping the existing upper room. Fails if it no-ops (stays far outside the contour).
            var contour = new InteriorFloor { NextRoomId = 2 };
            contour.Rooms.Add(RoomAt(1, 64, 64, 12, 12));   // ground footprint ≈ [58,70]²
            var upper = new InteriorFloor { NextRoomId = 3 };
            upper.Rooms.Add(RoomAt(1, 64, 64, 4, 4));        // existing upper room, centre
            upper.Rooms.Add(RoomAt(2, 100, 100, 4, 4));      // the "new" room, dropped far outside
            CompactLayout.PlaceNewRoomInContour(upper, 2, contour, m);
            var un = upper.GetRoom(2);
            if (!FloorFootprint.CoversPoint(contour, m, un.X * T, un.Y * T))
            { Debug.LogError("FAIL place-in-contour: new room centre not inside the contour"); ok = false; }
            if (Overlap(un, upper.GetRoom(1)))
            { Debug.LogError("FAIL place-in-contour: new room overlaps the existing upper room (a free spot existed)"); ok = false; }

            Debug.Log(ok ? "Self-Test New Room Placement: PASS" : "Self-Test New Room Placement: FAIL");
        }

        [ContextMenu("Self-Test: Column Packing")]
        public void SelfTestColumnPacking()
        {
            bool ok = true;
            int T = DungeonLayout.TilesPerAxis;
            float m = FloorFootprint.ContourMargin;

            // ---- 16. THE FIX: a pocket the BFS parent can never reach is filled by phase 2 ------------------
            // Fixture (all tile-space numbers hand-derived, see LContour/LPocketFloor): an L-shaped contour whose
            // horizontal arm is [50,62]x[58.5,65.5] and vertical arm [54.5,61.5]x[62,74]. Exactly FOUR 4x4 rooms
            // fit: (54,62) [the pinned column], (58,62), (58,66), (58,70). Links are 1-2, 1-3, 4-3 — so room 3's
            // BFS parent is the COLUMN, and the column is boxed: Right is taken by room 2, Left/Up leave the
            // contour, and Down (54,66) pokes into the empty x<54.5 pocket that neither arm covers. Under the
            // OLD packer (and equally under PHASE 1 ALONE, either seeding variant) room 3 is therefore dropped at
            // EVERY distance d, and room 4 — reachable only through room 3 — is never even attempted: 2 rooms kept
            // out of 4. The FILL phases retry room 3 against room 2 (a DIFFERENT already-placed anchor), which has
            // a free flush Down side at (58,66), and then room 4 against room 3 at (58,70). NON-VACUOUS: strip the
            // fill phases (2 and 3, the two that try EVERY placed anchor) and this fixture packs 2 rooms instead
            // of 4 — hand-derived and confirmed by running the pre-fix packer on it. The assertion below is
            // exactly the user-reported "the stated max still leaves room for another room" defect.
            var contour = LContour();
            var f = LPocketFloor(reversed: false);
            int kept = CompactLayout.PackAroundColumnWithinFootprint(f, 1, 54f, 62f, contour, m);
            if (kept != 4 || f.Rooms.Count != 4)
            { Debug.LogError($"FAIL pack-fill: kept {kept} rooms ({f.Rooms.Count} left on the floor), want 4 — the far pocket was not filled"); ok = false; }
            if (f.GetRoom(3) == null || f.GetRoom(4) == null)
            { Debug.LogError("FAIL pack-fill: room 3 (boxed-in BFS parent) and/or room 4 (reachable only through it) was dropped"); ok = false; }

            // ---- 17. Phase 2 must ADD a Link when it seats a room flush against a new anchor ----------------
            // Room 3 was seated flush against room 2 (hand-derived above: (58,66) under (58,62)) — geometrically
            // the ONLY slot available on this fixture, so a "the edge gap is ~0" check here would pass under
            // EVERY distance-ordered search, correct or broken (confirmed against MutAnchorOuter and against
            // phase 3 run alone) — that part was deleted per review I3 as unpinned; assertions 23 and 25 are what
            // actually exercise the distance/anchor ordering. What this fixture DOES pin down: room 3's original
            // links (1-2, 1-3, 4-3) do NOT include 2-3, so a fresh Link must appear between the newly-flush pair.
            // Non-vacuous: remove the link-add step and this reads 0 (blank wall + a corridor routed around it,
            // instead of the door the shared wall should render) — no other assertion here pins "a fresh flush
            // pair gains a link" specifically (18/25 pin non-duplication of an ALREADY-linked pair instead).
            if (f.GetRoom(3) != null && LinkCount(f, 2, 3) != 1)
            { Debug.LogError($"FAIL pack-door: {LinkCount(f, 2, 3)} link(s) between the flush pair 2-3, want exactly 1 (phase 2 must add the shared-wall door link)"); ok = false; }

            // ---- 18. No DUPLICATE links: an already-linked pair must not gain a second edge -----------------
            // Room 4 is seated against room 3, so the packer offers the pair as (anchor 3, room 4) — while the
            // fixture stores that pre-existing link the OTHER way round, as {RoomA = 4, RoomB = 3}. So only
            // AddLinkIfAbsent's REVERSE half (l.RoomA == b && l.RoomB == a) can recognise it: keep just the
            // forward half and this pair gets a second edge. Removing the guard entirely fails too, and the
            // general sweep below catches a duplicate of any other pair.
            if (LinkCount(f, 3, 4) != 1)
            { Debug.LogError($"FAIL pack-dedup: {LinkCount(f, 3, 4)} links between 3-4, want exactly 1 (the pair was already linked before packing)"); ok = false; }
            for (int i = 0; i < f.Rooms.Count; i++)
                for (int j = i + 1; j < f.Rooms.Count; j++)
                    if (LinkCount(f, f.Rooms[i].Id, f.Rooms[j].Id) > 1)
                    { Debug.LogError($"FAIL pack-dedup: duplicate link between {f.Rooms[i].Id} and {f.Rooms[j].Id}"); ok = false; }

            // ---- 19. Invariants on a mixed-size floor + the dropped-parent subtree IS retried ---------------
            // Contour = one 16x16 room at (64,64) → footprint [54.5,73.5]². Room 2 is 16x16: seating it against
            // ANY anchor puts its centre at least (4+16)/2 = 10 tiles from that anchor, while its own centre must
            // stay within [62.5,65.5]² to fit — impossible for any in-contour anchor, so it is legitimately
            // DROPPED at every phase/distance. Room 3 hangs off room 2 ONLY (link 2-3), so the old packer never
            // even tried it (it kept 3 rooms here); the fill phases must place it (hand-derived: flush under the
            // column at (64,68)) and ADD the
            // 1-3 link — otherwise, once room 2 and its links are dropped, room 3 would be an unreachable orphan
            // (the validator's "недостижимы от лестницы" rule) and TrimToRoomCount's BFS prefix would miss it.
            // Assertions: exact kept set, containment, non-overlap, the column bit-identical to its input, and
            // connectivity from the column. Non-vacuous: dropping the link-add breaks connectivity; a packer that
            // ignores ContainsRect/IsFree breaks containment/overlap; moving the column breaks the pin.
            var bigContour = BigContour();
            var g = OrphanSubtreeFloor();
            int keptG = CompactLayout.PackAroundColumnWithinFootprint(g, 1, 64f, 64f, bigContour, m);
            if (keptG != 4 || g.GetRoom(2) != null)
            { Debug.LogError($"FAIL pack-drop: kept {keptG} rooms (room 2 present: {g.GetRoom(2) != null}), want 4 with the oversized room 2 dropped"); ok = false; }
            if (g.GetRoom(3) == null)
            { Debug.LogError("FAIL pack-retry: room 3 (reachable only through the DROPPED room 2) was never placed"); ok = false; }
            else if (!CompactLayout.AdjacentAlongWall(g.GetRoom(1), g.GetRoom(3)))
            { Debug.LogError("FAIL pack-retry: room 3 was not seated flush against the column"); ok = false; }
            foreach (var r in g.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                if (!FloorFootprint.ContainsRect(bigContour, m, r.X * T, r.Y * T, w, h))
                { Debug.LogError($"FAIL pack-contour: room {r.Id} is not inside the contour after packing"); ok = false; }
            }
            for (int i = 0; i < g.Rooms.Count; i++)
                for (int j = i + 1; j < g.Rooms.Count; j++)
                    if (Overlap(g.Rooms[i], g.Rooms[j]))
                    { Debug.LogError($"FAIL pack-overlap: rooms {g.Rooms[i].Id} and {g.Rooms[j].Id} overlap after packing"); ok = false; }
            var col = g.GetRoom(1);
            if (col == null || col.X != 64f / T || col.Y != 64f / T || col.SizeW != 4 || col.SizeH != 4)
            { Debug.LogError("FAIL pack-column: the column is not bit-identical to its input position/size"); ok = false; }
            var reached = ReachableOverLinks(g, 1);
            foreach (var r in g.Rooms)
                if (!reached.Contains(r.Id))
                { Debug.LogError($"FAIL pack-connect: room {r.Id} is unreachable from the column over Links (a phase-2/3 placement must add its anchor link)"); ok = false; }

            // ---- 20. Determinism under a PERMUTED input room AND link order ---------------------------------
            // The same L fixture, but floor.Rooms AND floor.Links are both built in REVERSE order. Packing must
            // yield identical positions per room id — that is exactly what the ascending-id orders guarantee:
            // SortedById over the swept rooms, SortedById over the anchor list, and BuildAdjacency's neighbour
            // .Sort() for phase 1. NON-VACUOUS: sweeping floor.Rooms in list order instead makes the reversed
            // copy try room 4 before room 3, so 4 takes (58,66) and 3 takes (58,70) — the two runs then disagree
            // on both rooms; and without the neighbour .Sort() phase 1 would follow link-insertion order.
            // (Re-packing an identical copy would assert nothing.)
            var f2 = LPocketFloor(reversed: true);
            CompactLayout.PackAroundColumnWithinFootprint(f2, 1, 54f, 62f, LContour(), m);
            if (f2.Rooms.Count != f.Rooms.Count)
            { Debug.LogError($"FAIL pack-determinism: permuted input kept {f2.Rooms.Count} rooms vs {f.Rooms.Count}"); ok = false; }
            for (int id = 1; id <= 4; id++)
            {
                var ra = f.GetRoom(id); var rb = f2.GetRoom(id);
                if ((ra == null) != (rb == null))
                { Debug.LogError($"FAIL pack-determinism: room {id} kept in one run and dropped in the other"); ok = false; continue; }
                if (ra == null) continue;
                if (ra.X != rb.X || ra.Y != rb.Y)
                { Debug.LogError($"FAIL pack-determinism: room {id} at ({ra.X * T:F2},{ra.Y * T:F2}) vs ({rb.X * T:F2},{rb.Y * T:F2}) under permuted room order"); ok = false; }
            }

            // ---- 21. The SPREAD run must exist: a contour where flush-first seeding packs FEWER -------------
            // The packer runs its three phases TWICE — once seeding the BFS flush-only (compact) and once with the
            // pre-fix increasing-distance search (spread) — and keeps whichever placed more rooms. Here the
            // COMPACT run loses: seeding rooms 2 and 3 flush down the middle arm fills it, and the 5x4 room 4 then
            // only fits beside room 3 while the 4x5 room 5 has nowhere left (4 kept). The SPREAD run pushes room 4
            // one step further down the lower arm, which leaves room 5 a slot beside it (5 kept).
            // NON-VACUOUS (measured, harness `design`): compact-run-only keeps 4 here and the pre-fix packer keeps
            // 4; the shipped best-of-two keeps 5. Delete the spread run and this test fails.
            var sw = SpreadWinsFloor();
            int keptS = CompactLayout.PackAroundColumnWithinFootprint(sw, 1, 60f, 56f, SpreadWinsContour(), m);
            if (keptS != 5 || sw.GetRoom(5) == null)
            { Debug.LogError($"FAIL pack-spread-run: kept {keptS} rooms (room 5 present: {sw.GetRoom(5) != null}), want 5 — the SPREAD run must win here"); ok = false; }

            // ---- 22. The COMPACT run must exist: the mirror image of 21 -------------------------------------
            // Same structure, opposite winner: on this L-shaped contour the SPREAD run's phase 1 pushes room 3
            // down the long arm at a distance, which costs it room 4 — room 5 is still re-seated (phase 2 finds
            // it a different anchor) — netting 4 kept, while the COMPACT run's flush-only seeding keeps room 3
            // tight beside the column and fits all five.
            // NON-VACUOUS (measured, harness `design`): spread-run-only keeps 4 here and the pre-fix packer keeps
            // 3; the shipped best-of-two keeps 5. Delete the COMPACT run and this test fails.
            // 21 and 22 together are why the packer runs the pipeline twice: neither seeding dominates the other.
            var cw = CompactWinsFloor();
            int keptC = CompactLayout.PackAroundColumnWithinFootprint(cw, 1, 58f, 56f, CompactWinsContour(), m);
            if (keptC != 5 || cw.GetRoom(4) == null || cw.GetRoom(5) == null)
            { Debug.LogError($"FAIL pack-compact-run: kept {keptC} rooms, want 5 — the COMPACT run must win here"); ok = false; }

            // ---- 23. DISTANCE-outer: the nearest slot wins over an earlier anchor's farther one -------------
            // Contour arms (margin-inflated): [56.5,71.5]x[60.5,67.5] (top), [54.5,73.5]x[52.5,59.5] (middle) and
            // [48.5,63.5]² (left block) — connected, but the top and middle arms are separated by an uncovered
            // band y in (59.5,60.5), so a 6-tall room can never straddle them.
            // Column 1 (4x4) is pinned at (64,64); room 2 (4x5) seats flush right of it at (68,64); the 6x6 room 3
            // has no flush slot anywhere, so it falls through to the outward-ray phase. There its first valid slot
            // against the COLUMN (id 1, and the anchor it is LINKED to, so it is tried first at every distance) is
            // Up at d = 3 -> (64,56); its first valid slot against room 2 (id 2, unlinked) is Up at d = 2 ->
            // (68,56.5). Distance-outer therefore takes the NEARER one: (68,56.5), a 2-tile corridor.
            // NON-VACUOUS: swap SeatAgainstAnyPlaced's loops to anchor-outer/distance-inner and anchor 1 wins the
            // whole search at d = 3, putting room 3 at (64,56) with a 3-tile corridor — verified by running that
            // mutant in the offline harness. It also shows the distance ordering outranks the linked-anchor
            // preference of assertion 25, which is the intended precedence.
            var dz = DistanceOrderFloor();
            int keptD = CompactLayout.PackAroundColumnWithinFootprint(dz, 1, 64f, 64f, DistanceOrderContour(), m);
            var r3d = dz.GetRoom(3);
            if (keptD != 3 || r3d == null || !Approx(r3d.X * T, 68f) || !Approx(r3d.Y * T, 56.5f))
            {
                Debug.LogError($"FAIL pack-distance-order: kept {keptD}, room 3 at "
                    + (r3d == null ? "DROPPED" : $"({r3d.X * T:F2},{r3d.Y * T:F2})") + ", want (68.00,56.50) — the"
                    + " nearest slot must win even though it is at a LATER anchor");
                ok = false;
            }

            // ---- 24. The footprint bbox pre-test must not clip the contour's OUTER edge ----------------------
            // TrySeatAtDistance rejects a candidate outside the footprint's bounding BOX before paying for
            // ContainsRect. The box is the contour rooms' content bounds grown by the CONTOUR MARGIN (+ a hair):
            // drop the margin term and rooms sitting flush against the drawn outline stop being placeable, which
            // is a silent capacity loss with no other symptom. Fixture: a horizontal strip whose footprint is
            // [51,78]x[60.5,67.5] (content bounds [52.5,76.5]); the chain 1-2-3-4 of 4x4 rooms seats right of the
            // column at 68, 72 and 76, and room 4 then spans x[74,78] — its right edge EXACTLY on the outer edge.
            // NON-VACUOUS: with the margin term stripped from FootprintBoundsTiles the box ends at 76.5, room 4's
            // slot is rejected and it lands at (60,64) on the other side instead — verified with that mutant.
            var eg = EdgeChainFloor();
            int keptE = CompactLayout.PackAroundColumnWithinFootprint(eg, 1, 64f, 64f, EdgeStripContour(), m);
            var r4e = eg.GetRoom(4);
            if (keptE != 4 || r4e == null || !Approx(r4e.X * T, 76f) || !Approx(r4e.Y * T, 64f))
            {
                Debug.LogError($"FAIL pack-bounds-margin: kept {keptE}, room 4 at "
                    + (r4e == null ? "DROPPED" : $"({r4e.X * T:F2},{r4e.Y * T:F2})") + ", want (76.00,64.00) — a room"
                    + " flush against the contour's outer edge must survive the bounding-box pre-test");
                ok = false;
            }

            // ---- 25. LINKED anchors are preferred, so a room's own link renders as a door -------------------
            // A room's ORIGINAL links survive packing. Seat it against an unrelated room and those links route as
            // long corridors across the floor — the "detached rooms joined by corridors" look this packer exists
            // to avoid. So at every distance the anchors the room is already linked to are tried first.
            // Fixture: the same strip contour ([46.5,81.5]x[60.5,67.5], one row of 4x4 rooms). Links are 1-2 and
            // 3-4 only, so phase 1 seats just room 2 (right of the column, at (68,64)); phase 2 then seats room 3
            // against the column at (60,64) (its own link partner 4 is not placed yet), and finally room 4 — whose
            // link partner 3 IS placed and has a free flush side at (56,64), while the LOWER-id, unrelated room 2
            // also has one at (72,64). The linked anchor must win: room 4 lands at (56,64), flush against room 3.
            // NON-VACUOUS: drop the preference (plain ascending-id anchors) and room 4 goes to (72,64) with a NEW
            // 2-4 link, leaving its 3-4 link stretched 16 tiles across the floor — that is exactly what the packer
            // as first written did, verified against both that mutant and the pre-fix build.
            var lp = LinkedAnchorFloor();
            int keptL = CompactLayout.PackAroundColumnWithinFootprint(lp, 1, 64f, 64f, StripContour(), m);
            var r4l = lp.GetRoom(4);
            if (keptL != 4 || r4l == null || !Approx(r4l.X * T, 56f) || !Approx(r4l.Y * T, 64f))
            {
                Debug.LogError($"FAIL pack-linked-anchor: kept {keptL}, room 4 at "
                    + (r4l == null ? "DROPPED" : $"({r4l.X * T:F2},{r4l.Y * T:F2})") + ", want (56.00,64.00) — a room"
                    + " must be seated against an anchor it is ALREADY linked to when one is viable");
                ok = false;
            }
            else if (!CompactLayout.AdjacentAlongWall(r4l, lp.GetRoom(3)))
            { Debug.LogError("FAIL pack-linked-anchor: room 4 is not flush against its linked room 3"); ok = false; }
            if (LinkCount(lp, 3, 4) != 1)
            { Debug.LogError($"FAIL pack-linked-anchor: {LinkCount(lp, 3, 4)} links between 3-4, want exactly 1"); ok = false; }

            // ---- 26. MaxUsefulDistance's cut-off is EXACT — pins the boundary itself, not "large enough" ----
            // Contour = ONE room, raw x=[78,80] y=[62,66] -> margin-expanded footprint [76.5,81.5]x[60.5,67.5].
            // Column 1 (4x4) at (64,64); room 2 (4x4, UNLINKED, so it only ever reaches the fill phases) has no
            // flush slot (phase 2, d==0, fails on all four sides) and no Down/Left/Up slot at ANY distance — the
            // footprint never covers x<=76.5, so those three sides (which keep cx==64 or move further LEFT) are
            // hopeless regardless of d. Its ONLY reachable slot is Right, and only once the ray clears the
            // footprint's left edge: cx = 64+4+d, rect = [cx-2,cx+2]; this fits [76.5,81.5] first at d = 11
            // (cx=79, rect [77,81]) — d = 10 (rect [76,80]) still pokes 0.5 tiles past the left edge.
            // MaxUsefulDistance's own formula for this exact case: r = bounds.maxX - px - offX - cw*0.5
            // = 81.501 - 64 - 4 - 2 = 11.501 -> floor 11 -> limit 11, and it is the UNIQUE positive contributor
            // (l, up and down are all negative here) — so this fixture pins the Right-side term exactly, with no
            // other anchor or side able to accidentally cover for a broken cut-off.
            // NON-VACUOUS in two different ways: (1) TIGHTEN the cut-off by even ONE (limit 10 instead of 11) and
            // the d==11 slot is never tried -> room 2 is dropped; (2) COLLAPSE the four-side max down to a single
            // side (e.g. "best = up" instead of the max over r/l/dn/up) and the cut-off goes negative (up here is
            // -2.499) -> the distance loop never runs at all -> room 2 is dropped. Both are caught by the SAME
            // "kept == 2, room 2 at (79,64)" assertion below — verified against the harness mutants MutTightCut
            // and MutOneSideMax (`dotnet run -c Release -- mutants`).
            var tc = TightCutFloor();
            int keptT = CompactLayout.PackAroundColumnWithinFootprint(tc, 1, 64f, 64f, TightCutContour(), m);
            var r2t = tc.GetRoom(2);
            if (keptT != 2 || r2t == null || !Approx(r2t.X * T, 79f) || !Approx(r2t.Y * T, 64f))
            {
                Debug.LogError($"FAIL pack-tight-cut: kept {keptT}, room 2 at "
                    + (r2t == null ? "DROPPED" : $"({r2t.X * T:F2},{r2t.Y * T:F2})") + ", want (79.00,64.00) — the"
                    + " farthest useful distance must be found EXACTLY, not approximately");
                ok = false;
            }

            Debug.Log(ok ? "Self-Test Column Packing: PASS" : "Self-Test Column Packing: FAIL");
        }

        // Tile-space float compare for the packer fixtures (positions are exact multiples of 1/128).
        static bool Approx(float a, float b) => Mathf.Abs(a - b) <= 1e-3f;

        // ------------------------------------------------------------------------------------------------
        // Fixtures & independent helpers (read DungeonLayout.TilesPerAxis + EffectiveSize — never copy them).
        // ------------------------------------------------------------------------------------------------

        // L-shaped floor-0 contour. Inflated by ContourMargin (1.5) the two rooms give
        //   horizontal arm  9x4 at (56,62) -> [50,62] x [58.5,65.5]
        //   vertical arm    4x9 at (58,68) -> [54.5,61.5] x [62,74]
        // Only ONE row of 4x4 rooms fits the horizontal arm (7 tiles tall) and one column of them the vertical
        // arm (7 tiles wide), and the bottom-left region x<54.5, y>65.5 is OUTSIDE the shape — the "pocket"
        // geometry that boxes a parent in.
        static InteriorFloor LContour()
        {
            var c = new InteriorFloor { NextRoomId = 3 };
            c.Rooms.Add(RoomAt(1, 56, 62, 9, 4));
            c.Rooms.Add(RoomAt(2, 58, 68, 4, 9));
            return c;
        }

        // The upper floor packed into LContour: column 1 + three 4x4 rooms, linked 1-2, 1-3 and 4-3. Start
        // positions are irrelevant (the packer assigns every placed room's position), so they are all stacked at
        // the column. `reversed` inserts the SAME rooms AND the SAME links in the opposite order — the
        // determinism stimulus for both the ascending-id sweeps and BuildAdjacency's neighbour sort.
        // NOTE the 4-3 link is deliberately stored with the HIGHER id first: room 4 is seated against room 3, so
        // the packer offers the pair as (3,4) and only the REVERSE half of AddLinkIfAbsent's test can recognise
        // it as already linked. That is what assertion 18 guards (see the comment there).
        static InteriorFloor LPocketFloor(bool reversed)
        {
            var f = new InteriorFloor { NextRoomId = 5 };
            if (reversed) for (int id = 4; id >= 1; id--) f.Rooms.Add(RoomAt(id, 54, 62, 4, 4));
            else for (int id = 1; id <= 4; id++) f.Rooms.Add(RoomAt(id, 54, 62, 4, 4));
            if (reversed)
            {
                f.Links.Add(new Link { RoomA = 4, RoomB = 3 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 3 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            }
            else
            {
                f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
                f.Links.Add(new Link { RoomA = 1, RoomB = 3 });
                f.Links.Add(new Link { RoomA = 4, RoomB = 3 });
            }
            return f;
        }

        // A roomy square floor-0 contour: one 16x16 room at (64,64) -> footprint [54.5,73.5]² (19x19 tiles).
        static InteriorFloor BigContour()
        {
            var c = new InteriorFloor { NextRoomId = 2 };
            c.Rooms.Add(RoomAt(1, 64, 64, 16, 16));
            return c;
        }

        // Column 1 (4x4) + an UNPLACEABLE 16x16 room 2 + room 3 hanging off room 2 ONLY + a 6x6/5x4 chain off
        // the column. Links: 1-2, 2-3, 1-4, 4-5. The old packer dropped room 2 and never tried room 3.
        static InteriorFloor OrphanSubtreeFloor()
        {
            var f = new InteriorFloor { NextRoomId = 6 };
            f.Rooms.Add(RoomAt(1, 64, 64, 4, 4));
            f.Rooms.Add(RoomAt(2, 64, 64, 16, 16));
            f.Rooms.Add(RoomAt(3, 64, 64, 4, 4));
            f.Rooms.Add(RoomAt(4, 64, 64, 6, 6));
            f.Rooms.Add(RoomAt(5, 64, 64, 5, 4));
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 2, RoomB = 3 });
            f.Links.Add(new Link { RoomA = 1, RoomB = 4 });
            f.Links.Add(new Link { RoomA = 4, RoomB = 5 });
            return f;
        }

        // ---- assertion 21: a contour where the SPREAD run wins ------------------------------------------
        // Three OVERLAPPING rooms (a real floor 0 is packed flush by CompactLayout.Arrange, so its footprint is
        // always one connected blob — a fixture must not rely on a disconnected one). Margin-inflated boxes:
        //   [56.5,63.5]x[50.5,61.5]  (top arm, holds the column at (60,56))
        //   [54.5,65.5]x[54.5,65.5]  (middle block, overlaps both others)
        //   [56.5,67.5]x[60.5,71.5]  (lower arm, offset RIGHT)
        static InteriorFloor SpreadWinsContour()
        {
            var c = new InteriorFloor { NextRoomId = 4 };
            c.Rooms.Add(RoomAt(1, 60, 56, 4, 8));
            c.Rooms.Add(RoomAt(2, 62, 66, 8, 8));
            c.Rooms.Add(RoomAt(3, 60, 60, 8, 8));
            return c;
        }

        // Column 1 (4x4) + rooms 2 (6x5), 3 (4x4), 4 (5x4), 5 (4x5); every room linked to the column (a star).
        static InteriorFloor SpreadWinsFloor()
        {
            var f = new InteriorFloor { NextRoomId = 6 };
            f.Rooms.Add(RoomAt(1, 60, 56, 4, 4));
            f.Rooms.Add(RoomAt(2, 60, 56, 6, 5));
            f.Rooms.Add(RoomAt(3, 60, 56, 4, 4));
            f.Rooms.Add(RoomAt(4, 60, 56, 5, 4));
            f.Rooms.Add(RoomAt(5, 60, 56, 4, 5));
            for (int id = 2; id <= 5; id++) f.Links.Add(new Link { RoomA = id, RoomB = 1 });
            return f;
        }

        // ---- assertion 22: the mirror — a contour where the COMPACT run wins ----------------------------
        // Two overlapping rooms forming an L. Margin-inflated boxes:
        //   [52.5,63.5]x[52.5,59.5]  (top arm, holds the column at (58,56))
        //   [50.5,61.5]x[56.5,67.5]  (lower arm, offset LEFT; overlaps the top arm on [52.5,61.5]x[56.5,59.5])
        static InteriorFloor CompactWinsContour()
        {
            var c = new InteriorFloor { NextRoomId = 3 };
            c.Rooms.Add(RoomAt(1, 58, 56, 8, 4));
            c.Rooms.Add(RoomAt(2, 56, 62, 8, 8));
            return c;
        }

        // Column 1 (4x4) + rooms 2 (6x4), 3 (4x4), 4 (5x5), 5 (5x4); links 2-1, 3-1, 4-2, 5-4.
        static InteriorFloor CompactWinsFloor()
        {
            var f = new InteriorFloor { NextRoomId = 6 };
            f.Rooms.Add(RoomAt(1, 58, 56, 4, 4));
            f.Rooms.Add(RoomAt(2, 58, 56, 6, 4));
            f.Rooms.Add(RoomAt(3, 58, 56, 4, 4));
            f.Rooms.Add(RoomAt(4, 58, 56, 5, 5));
            f.Rooms.Add(RoomAt(5, 58, 56, 5, 4));
            f.Links.Add(new Link { RoomA = 2, RoomB = 1 });
            f.Links.Add(new Link { RoomA = 3, RoomB = 1 });
            f.Links.Add(new Link { RoomA = 4, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 5, RoomB = 4 });
            return f;
        }

        // ---- assertion 23: distance-outer vs anchor-outer -----------------------------------------------
        // Margin-inflated boxes: [56.5,71.5]x[60.5,67.5] (top, holds the column at (64,64)),
        // [54.5,73.5]x[52.5,59.5] (middle) and [48.5,63.5]² (left block, which connects the other two).
        // The uncovered band y in (59.5,60.5) between the top and middle arms is what forces the 6x6 room out to
        // a distance: it can sit ENTIRELY in the middle arm, but never across the two.
        static InteriorFloor DistanceOrderContour()
        {
            var c = new InteriorFloor { NextRoomId = 4 };
            c.Rooms.Add(RoomAt(1, 64, 64, 12, 4));
            c.Rooms.Add(RoomAt(2, 64, 56, 16, 4));
            c.Rooms.Add(RoomAt(3, 56, 56, 12, 12));
            return c;
        }

        // Column 1 (4x4) + room 2 (4x5) + room 3 (6x6); links 2-1 and 3-1 (so room 3 is linked to the COLUMN).
        static InteriorFloor DistanceOrderFloor()
        {
            var f = new InteriorFloor { NextRoomId = 4 };
            f.Rooms.Add(RoomAt(1, 64, 64, 4, 4));
            f.Rooms.Add(RoomAt(2, 64, 64, 4, 5));
            f.Rooms.Add(RoomAt(3, 64, 64, 6, 6));
            f.Links.Add(new Link { RoomA = 2, RoomB = 1 });
            f.Links.Add(new Link { RoomA = 3, RoomB = 1 });
            return f;
        }

        // ---- assertions 24 & 25: horizontal strip contours ----------------------------------------------
        // RoomSizing.MaxSide is 16, so one contour room can only span 16 tiles — these use two overlapping
        // 16x4 rooms. Both strips are 7 tiles tall, so exactly ONE row of 4-tall rooms fits and every placement
        // decision is a pure left/right one.

        // Footprint [46.5,81.5] x [60.5,67.5], centred on the column at (64,64).
        static InteriorFloor StripContour()
        {
            var c = new InteriorFloor { NextRoomId = 3 };
            c.Rooms.Add(RoomAt(1, 56, 64, 16, 4));
            c.Rooms.Add(RoomAt(2, 72, 64, 16, 4));
            return c;
        }

        // Footprint [51,78] x [60.5,67.5] — shifted so the flush chain right of the column (68, 72, 76) ends
        // with a room whose right edge is EXACTLY the outer edge x = 78.
        static InteriorFloor EdgeStripContour()
        {
            var c = new InteriorFloor { NextRoomId = 3 };
            c.Rooms.Add(RoomAt(1, 60.5f, 64, 16, 4));
            c.Rooms.Add(RoomAt(2, 68.5f, 64, 16, 4));
            return c;
        }

        // Column 1 + three 4x4 rooms in a CHAIN 1-2-3-4 — seated right of the column at 68, 72, 76.
        static InteriorFloor EdgeChainFloor()
        {
            var f = new InteriorFloor { NextRoomId = 5 };
            for (int id = 1; id <= 4; id++) f.Rooms.Add(RoomAt(id, 64, 64, 4, 4));
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 2, RoomB = 3 });
            f.Links.Add(new Link { RoomA = 3, RoomB = 4 });
            return f;
        }

        // Column 1 + three 4x4 rooms, linked ONLY 1-2 and 3-4: the BFS reaches room 2 and stops, so rooms 3 and
        // 4 are seated by the fill phases — and room 4's only link partner (3) is placed by then.
        static InteriorFloor LinkedAnchorFloor()
        {
            var f = new InteriorFloor { NextRoomId = 5 };
            for (int id = 1; id <= 4; id++) f.Rooms.Add(RoomAt(id, 64, 64, 4, 4));
            f.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            f.Links.Add(new Link { RoomA = 3, RoomB = 4 });
            return f;
        }

        // ---- assertion 26: MaxUsefulDistance's exact cut-off ---------------------------------------------
        // ONE contour room, raw x=[78,80] y=[62,66] -> margin-expanded footprint [76.5,81.5]x[60.5,67.5]. Chosen
        // so the column's Right-side term is the UNIQUE positive contributor to MaxUsefulDistance's four-side/
        // every-anchor max (see assertion 26's comment for the worked arithmetic), so the fixture pins that one
        // term exactly rather than whichever side/anchor happens to be loosest.
        static InteriorFloor TightCutContour()
        {
            var c = new InteriorFloor { NextRoomId = 2 };
            c.Rooms.Add(RoomAt(1, 79, 64, 2, 4));
            return c;
        }

        // Column 1 (4x4) + an UNLINKED 4x4 room 2 — unlinked so it never reaches phase 1 (BFS over Links) and is
        // seated purely by the MaxUsefulDistance-bounded phase 3 search.
        static InteriorFloor TightCutFloor()
        {
            var f = new InteriorFloor { NextRoomId = 3 };
            f.Rooms.Add(RoomAt(1, 64, 64, 4, 4));
            f.Rooms.Add(RoomAt(2, 64, 64, 4, 4));
            return f;
        }

        // Links between an unordered pair, counted in BOTH directions (the duplicate-link probe).
        static int LinkCount(InteriorFloor f, int a, int b)
        {
            int n = 0;
            foreach (var l in f.Links)
                if ((l.RoomA == a && l.RoomB == b) || (l.RoomA == b && l.RoomB == a)) n++;
            return n;
        }

        // Independent BFS over Links from a root room id — reimplemented here so it never shares code with the
        // packer's own adjacency.
        static HashSet<int> ReachableOverLinks(InteriorFloor f, int rootId)
        {
            var seen = new HashSet<int> { rootId };
            var queue = new Queue<int>();
            queue.Enqueue(rootId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (var l in f.Links)
                {
                    int other = l.RoomA == cur ? l.RoomB : (l.RoomB == cur ? l.RoomA : -1);
                    if (other >= 0 && seen.Add(other)) queue.Enqueue(other);
                }
            }
            return seen;
        }

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
