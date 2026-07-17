using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for RoomLinkGeometry — add to any GameObject, run from the
    /// Inspector. Headless: the module under test has no Unity dependency.</summary>
    public class RoomLinkGeometrySelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Room Link Geometry")]
        public void SelfTestRoomLinkGeometry()
        {
            bool ok = true;

            // ── Doors land ON the wall, never inside the room, never floating ──────────────────────
            // A single link east: the door must sit exactly on A's east edge, at the wall's midpoint.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 20f, CY = 20f, W = 6f, H = 6f },
                    new LinkNode { Id = 2, CX = 50f, CY = 20f, W = 6f, H = 6f },
                };
                var edges = new List<LinkEdge> { new LinkEdge { A = 1, B = 2 } };
                var g = RoomLinkGeometry.Build(nodes, edges);

                if (!HasDoorAt(g, 23f, 20f))
                { Debug.LogError("FAIL single-east: no door at A's east wall midpoint (23,20)"); ok = false; }
                if (!HasDoorAt(g, 47f, 20f))
                { Debug.LogError("FAIL single-east: no door at B's west wall midpoint (47,20)"); ok = false; }
                foreach (var d in g.Doors)
                    if (IsStrictlyInside(nodes, d))
                    { Debug.LogError($"FAIL: door ({d.X:F1},{d.Y:F1}) is INSIDE a room"); ok = false; }
            }

            // ── Wall choice must normalize by half-extent, not compare raw |dx| vs |dy| ────────────
            // A is WIDE (W=40,H=4). The target is dx=+12, dy=+10 — raw |dx|>|dy| would pick EAST (the
            // narrow end cap), but normalized, ry = 10/2 = 5 beats rx = 12/20 = 0.6 → SOUTH, the long
            // wall. This case is the whole reason for the normalization; delete it and this fails.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 40f, CY = 40f, W = 40f, H = 4f },
                    new LinkNode { Id = 2, CX = 52f, CY = 50f, W = 6f, H = 6f },
                };
                var edges = new List<LinkEdge> { new LinkEdge { A = 1, B = 2 } };
                var g = RoomLinkGeometry.Build(nodes, edges);

                LinkPoint door = default; bool found = false;
                foreach (var d in g.Doors)
                    if (Mathf.Abs(d.Y - 42f) < 1e-3f) { door = d; found = true; break; }   // A's south edge: CY + H/2
                if (!found)
                { Debug.LogError("FAIL elongated: the door did not land on A's SOUTH (long) wall — raw |dx|vs|dy| would have picked the end cap"); ok = false; }
                else if (door.X < 20f || door.X > 60f)
                { Debug.LogError($"FAIL elongated: door X {door.X:F1} is off A's south wall span"); ok = false; }
            }

            // ── Two links on one wall → two DISTINCT doors, not crossing ──────────────────────────
            // Both targets are east of A, one north one south. Doors go at 1/3 and 2/3 of the east wall,
            // ordered to match the targets' order along the wall axis, so the corridors don't cross.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 20f, CY = 30f, W = 6f, H = 12f },
                    // Id 2 is FARTHER than id 3 on purpose: the distance sort (which picks who gets a
                    // door) then yields [3, 2] while the along-wall sort (which picks WHICH door) yields
                    // [2, 3]. The two orders must disagree, or this fixture cannot tell the along-wall
                    // sort from the distance sort and the non-crossing rule goes untested.
                    new LinkNode { Id = 2, CX = 74f, CY = 24f, W = 6f, H = 6f },   // north target, farther
                    new LinkNode { Id = 3, CX = 50f, CY = 36f, W = 6f, H = 6f },   // south target, nearer
                };
                var edges = new List<LinkEdge>
                {
                    new LinkEdge { A = 1, B = 2 },
                    new LinkEdge { A = 1, B = 3 },
                };
                var g = RoomLinkGeometry.Build(nodes, edges);

                // A's east wall spans Y 24..36 at X = 23. Expect doors at Y = 28 and Y = 32.
                var wallDoors = new List<LinkPoint>();
                foreach (var d in g.Doors) if (Mathf.Abs(d.X - 23f) < 1e-3f) wallDoors.Add(d);
                if (wallDoors.Count != 2)
                { Debug.LogError($"FAIL two-on-a-wall: {wallDoors.Count} doors on A's east wall, want 2"); ok = false; }
                else if (Mathf.Abs(wallDoors[0].Y - wallDoors[1].Y) < 1e-3f)
                { Debug.LogError("FAIL two-on-a-wall: both doors landed on the SAME point"); ok = false; }

                // Guard the premise: if these ever became equidistant, the two sort orders would coincide
                // and everything below would pass vacuously.
                float d2 = Mathf.Sqrt((74f - 20f) * (74f - 20f) + (24f - 30f) * (24f - 30f));
                float d3 = Mathf.Sqrt((50f - 20f) * (50f - 20f) + (36f - 30f) * (36f - 30f));
                if (Mathf.Abs(d2 - d3) < 1f)
                { Debug.LogError("FAIL setup: the two targets are equidistant — the distance and along-wall orders coincide, so this fixture tests nothing"); ok = false; }

                // Non-crossing: the segment to the NORTH target must start at the NORTHER door.
                var segToNorth = FirstLegForEdge(g, 0);
                var segToSouth = FirstLegForEdge(g, 1);
                if (segToNorth == null || segToSouth == null)
                { Debug.LogError("FAIL two-on-a-wall: an edge emitted no legs at all — there is no door order to check"); ok = false; }
                else if (segToNorth.A.Y > segToSouth.A.Y)
                { Debug.LogError("FAIL two-on-a-wall: doors are swapped — the corridors cross at the wall"); ok = false; }

                if (g.Forks.Count != 0)
                { Debug.LogError($"FAIL two-on-a-wall: {g.Forks.Count} forks, want 0 — the wall is not full yet"); ok = false; }
            }

            // ── Three links on one wall → exactly 2 doors + 1 fork, ON a trunk ─────────────────────
            // Setup precondition asserted: all three targets MUST resolve to A's east wall, or this test
            // silently stops testing the limit at all.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 20f, CY = 30f, W = 6f, H = 12f },
                    new LinkNode { Id = 2, CX = 40f, CY = 26f, W = 6f, H = 6f },   // nearest
                    new LinkNode { Id = 3, CX = 44f, CY = 34f, W = 6f, H = 6f },   // 2nd nearest
                    new LinkNode { Id = 4, CX = 70f, CY = 30f, W = 6f, H = 6f },   // farthest → must fork
                };
                var edges = new List<LinkEdge>
                {
                    new LinkEdge { A = 1, B = 2 },
                    new LinkEdge { A = 1, B = 3 },
                    new LinkEdge { A = 1, B = 4 },
                };
                var g = RoomLinkGeometry.Build(nodes, edges);

                var wallDoors = new List<LinkPoint>();
                foreach (var d in g.Doors) if (Mathf.Abs(d.X - 23f) < 1e-3f) wallDoors.Add(d);
                if (wallDoors.Count != RoomLinkGeometry.MaxDoorsPerWall)
                { Debug.LogError($"FAIL three-on-a-wall: {wallDoors.Count} doors on A's east wall, want {RoomLinkGeometry.MaxDoorsPerWall} — the per-wall limit is not enforced"); ok = false; }

                if (g.Forks.Count != 1)
                { Debug.LogError($"FAIL three-on-a-wall: {g.Forks.Count} forks, want exactly 1"); ok = false; }
                else
                {
                    // The fork point must lie ON one of the two trunk segments — not merely near them.
                    var fork = g.Forks[0];
                    bool onTrunk = false;
                    foreach (var s in g.Segments)
                    {
                        if (s.EdgeIndex == 2) continue;          // that's the forked edge itself
                        if (DistanceToSegment(fork, s.A, s.B) < 1e-2f) { onTrunk = true; break; }
                    }
                    if (!onTrunk)
                    { Debug.LogError($"FAIL three-on-a-wall: the fork point ({fork.X:F1},{fork.Y:F1}) does not lie on any trunk segment"); ok = false; }
                }
            }

            // ── A fourth link forks off a FORK, not just a trunk — this is what tests the recursion ─
            // Target 5 is placed beyond target 4, so the nearest already-built geometry is edge 4's own
            // forked segment. If the candidate set were only the two trunks, this would attach elsewhere.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 20f, CY = 30f, W = 6f, H = 12f },
                    new LinkNode { Id = 2, CX = 40f, CY = 26f, W = 6f, H = 6f },
                    new LinkNode { Id = 3, CX = 44f, CY = 34f, W = 6f, H = 6f },
                    new LinkNode { Id = 4, CX = 70f, CY = 30f, W = 6f, H = 6f },   // forks off a trunk
                    new LinkNode { Id = 5, CX = 95f, CY = 30f, W = 6f, H = 6f },   // forks off edge 4's segment
                };
                var edges = new List<LinkEdge>
                {
                    new LinkEdge { A = 1, B = 2 },
                    new LinkEdge { A = 1, B = 3 },
                    new LinkEdge { A = 1, B = 4 },
                    new LinkEdge { A = 1, B = 5 },
                };
                var g = RoomLinkGeometry.Build(nodes, edges);

                if (g.Forks.Count != 2)
                { Debug.LogError($"FAIL four-on-a-wall: {g.Forks.Count} forks, want 2"); ok = false; }

                var seg5 = FirstLegForEdge(g, 3);
                if (seg5 == null)
                { Debug.LogError("FAIL four-on-a-wall: no segment for edge 5"); ok = false; }
                else
                {
                    // With the recursion, edge 5 taps edge 4's OWN polyline. Without it, the candidate set
                    // is only the two trunks and edge 5 taps trunk1 at its endpoint — which an
                    // "is it on edge 4?" check ALONE would still satisfy. Assert BOTH: on edge 4, and NOT
                    // on either trunk.
                    var legs4 = SegmentsForEdge(g, 2);
                    if (legs4.Count == 0)
                    { Debug.LogError("FAIL four-on-a-wall: edge 4 emitted no legs — nothing for edge 5 to tap"); ok = false; }
                    else
                    {
                        bool onEdge4 = false;
                        foreach (var leg in legs4)
                            if (DistanceToSegment(seg5.A, leg.A, leg.B) < 1e-2f) { onEdge4 = true; break; }
                        if (!onEdge4)
                        { Debug.LogError($"FAIL four-on-a-wall: edge 5 attached at ({seg5.A.X:F1},{seg5.A.Y:F1}), which is on NO leg of edge 4"); ok = false; }
                    }

                    bool onTrunk = false;
                    foreach (int trunkEdge in new[] { 0, 1 })
                    {
                        foreach (var leg in SegmentsForEdge(g, trunkEdge))
                            if (DistanceToSegment(seg5.A, leg.A, leg.B) < 1e-2f) { onTrunk = true; break; }
                        if (onTrunk) break;
                    }
                    if (onTrunk)
                    { Debug.LogError($"FAIL four-on-a-wall: edge 5 tapped a TRUNK at ({seg5.A.X:F1},{seg5.A.Y:F1}) — the fork search is NOT recursive"); ok = false; }
                }
            }

            // ── No wall may EVER carry more than MaxDoorsPerWall doors ─────────────────────────────
            // Six targets fanned east of one node. If the limit leaked, this catches it regardless of
            // which walls the fan resolves to.
            {
                var nodes = new List<LinkNode> { new LinkNode { Id = 1, CX = 30f, CY = 30f, W = 8f, H = 8f } };
                var edges = new List<LinkEdge>();
                for (int i = 0; i < 6; i++)
                {
                    nodes.Add(new LinkNode { Id = 10 + i, CX = 60f, CY = 12f + i * 7f, W = 5f, H = 5f });
                    edges.Add(new LinkEdge { A = 1, B = 10 + i });
                }
                var g = RoomLinkGeometry.Build(nodes, edges);

                var perWall = new Dictionary<string, int>();
                foreach (var d in g.Doors)
                {
                    // Bucket by which of node 1's four edges the door sits on (ignore the targets' doors).
                    // BOTH coordinates must be on node 1's wall. Testing one axis alone mislabels a
                    // TARGET's door as node 1's: id 12 sits at CY = 26, so its own west door (57.5, 26)
                    // would land in the "N" bucket and be counted against node 1's north wall.
                    bool onX = d.X >= 26f - 1e-3f && d.X <= 34f + 1e-3f;
                    bool onY = d.Y >= 26f - 1e-3f && d.Y <= 34f + 1e-3f;
                    string key = null;
                    if (Mathf.Abs(d.X - 34f) < 1e-3f && onY) key = "E";
                    else if (Mathf.Abs(d.X - 26f) < 1e-3f && onY) key = "W";
                    else if (Mathf.Abs(d.Y - 26f) < 1e-3f && onX) key = "N";
                    else if (Mathf.Abs(d.Y - 34f) < 1e-3f && onX) key = "S";
                    if (key == null) continue;
                    perWall.TryGetValue(key, out int n);
                    perWall[key] = n + 1;
                }
                foreach (var kv in perWall)
                    if (kv.Value > RoomLinkGeometry.MaxDoorsPerWall)
                    { Debug.LogError($"FAIL fan-out: wall {kv.Key} carries {kv.Value} doors, limit is {RoomLinkGeometry.MaxDoorsPerWall}"); ok = false; }
            }

            // ── Determinism ────────────────────────────────────────────────────────────────────────
            {
                var g1 = RoomLinkGeometry.Build(FanNodes(), FanEdges());
                var g2 = RoomLinkGeometry.Build(FanNodes(), FanEdges());
                if (g1.Segments.Count != g2.Segments.Count || g1.Forks.Count != g2.Forks.Count)
                { Debug.LogError("FAIL determinism: different shape from identical input"); ok = false; }
                else
                    for (int i = 0; i < g1.Segments.Count; i++)
                        if (Mathf.Abs(g1.Segments[i].A.X - g2.Segments[i].A.X) > 1e-5f ||
                            Mathf.Abs(g1.Segments[i].A.Y - g2.Segments[i].A.Y) > 1e-5f)
                        { Debug.LogError("FAIL determinism: segment positions differ"); ok = false; break; }
            }

            // ── Degenerate inputs must not throw ───────────────────────────────────────────────────
            {
                var empty = RoomLinkGeometry.Build(new List<LinkNode>(), new List<LinkEdge>());
                if (empty == null || empty.Segments.Count != 0)
                { Debug.LogError("FAIL: empty input did not yield empty geometry"); ok = false; }

                // An edge naming a node that doesn't exist must be skipped, not crash.
                var orphanNodes = new List<LinkNode> { new LinkNode { Id = 1, CX = 10f, CY = 10f, W = 4f, H = 4f } };
                var orphanEdges = new List<LinkEdge> { new LinkEdge { A = 1, B = 99 } };
                var og = RoomLinkGeometry.Build(orphanNodes, orphanEdges);
                if (og.Segments.Count != 0)
                { Debug.LogError($"FAIL: an edge to a missing node produced {og.Segments.Count} segments, want 0"); ok = false; }

                // Two nodes at the SAME centre — no NaN, no infinite loop.
                var same = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 10f, CY = 10f, W = 4f, H = 4f },
                    new LinkNode { Id = 2, CX = 10f, CY = 10f, W = 4f, H = 4f },
                };
                var sg = RoomLinkGeometry.Build(same, new List<LinkEdge> { new LinkEdge { A = 1, B = 2 } });
                if (sg.Segments.Count == 0)
                { Debug.LogError("FAIL: coincident nodes produced NO segments — this NaN check would scan an empty list and prove nothing"); ok = false; }
                foreach (var s in sg.Segments)
                    if (float.IsNaN(s.A.X) || float.IsNaN(s.A.Y) || float.IsNaN(s.B.X) || float.IsNaN(s.B.Y))
                    { Debug.LogError("FAIL: coincident nodes produced NaN"); ok = false; }
            }

            // ── A link must route around a room sitting between its two ends ───────────────────────
            // Node 2 sits squarely between 1 and 3. Assert the premise with Build's OWN doors: if the
            // straight door-to-door line ever stopped crossing room 2, everything below would pass while
            // proving nothing.
            {
                var nodes = new List<LinkNode>
                {
                    new LinkNode { Id = 1, CX = 10f, CY = 30f, W = 6f, H = 6f },
                    new LinkNode { Id = 2, CX = 40f, CY = 30f, W = 10f, H = 10f },   // the blocker
                    new LinkNode { Id = 3, CX = 70f, CY = 30f, W = 6f, H = 6f },
                };
                var edges = new List<LinkEdge> { new LinkEdge { A = 1, B = 3 } };   // 1→3 only; 2 is unlinked
                var g = RoomLinkGeometry.Build(nodes, edges);

                var legs = SegmentsForEdge(g, 0);
                if (legs.Count == 0)
                { Debug.LogError("FAIL detour-in-build: link 1→3 emitted no legs at all"); ok = false; }
                else
                {
                    if (!StraightLineHits(legs[0].A, legs[legs.Count - 1].B, nodes[1]))
                    { Debug.LogError("FAIL detour-in-build setup: the straight door-to-door line misses room 2 — this fixture would prove nothing"); ok = false; }

                    foreach (var leg in legs)
                    {
                        bool h = Mathf.Abs(leg.A.Y - leg.B.Y) <= 1e-3f;
                        bool v = Mathf.Abs(leg.A.X - leg.B.X) <= 1e-3f;
                        if (!h && !v)
                        { Debug.LogError($"FAIL detour-in-build: leg ({leg.A.X:F1},{leg.A.Y:F1})→({leg.B.X:F1},{leg.B.Y:F1}) is DIAGONAL"); ok = false; break; }
                    }
                    foreach (var leg in legs)
                        if (SegmentEntersRect(leg.A, leg.B, nodes[1]))
                        { Debug.LogError("FAIL detour-in-build: a leg still crosses room 2"); ok = false; break; }
                }
            }

            Debug.Log(ok ? "Self-Test Room Link Geometry: PASS" : "Self-Test Room Link Geometry: FAIL");
        }

        [ContextMenu("Self-Test: A* Route")]
        public void SelfTestAStarRoute()
        {
            bool ok = true;
            var East = P(1f, 0f);
            var West = P(-1f, 0f);
            var North = P(0f, -1f);   // tile Y grows SOUTH, so North is −Y
            var South = P(0f, 1f);
            var noOcc = new List<(LinkPoint a, LinkPoint b)>();

            // Count how many legs of `path` intersect the axis-aligned segment a→b.
            int CrossCount(List<LinkPoint> path, LinkPoint a, LinkPoint b)
            {
                int c = 0;
                for (int i = 0; i < path.Count - 1; i++)
                    if (RoomLinkGeometry_SegOverlap(path[i], path[i + 1], a, b)) c++;
                return c;
            }

            void AssertOrtho(List<LinkPoint> path, string who)
            {
                for (int i = 0; i < path.Count - 1; i++)
                {
                    bool h = Mathf.Abs(path[i].Y - path[i + 1].Y) <= 1e-3f;
                    bool v = Mathf.Abs(path[i].X - path[i + 1].X) <= 1e-3f;
                    if (!h && !v)
                    { Debug.LogError($"FAIL {who}: leg {i} is DIAGONAL"); ok = false; return; }
                }
            }
            int Bends(List<LinkPoint> path)
            {
                int b = 0;
                for (int i = 1; i < path.Count - 1; i++)
                {
                    float ux = path[i].X - path[i - 1].X, uy = path[i].Y - path[i - 1].Y;
                    float vx = path[i + 1].X - path[i].X, vy = path[i + 1].Y - path[i].Y;
                    if (Mathf.Abs(ux * vy - uy * vx) > 1e-3f) b++;
                }
                return b;
            }

            // ── Clean shot, no obstacles → orthogonal, reaches, stays simple ───────────────────────
            // Both ends leave/enter HORIZONTALLY (from East, to entered from its West wall), so the tidy
            // orthogonal path is a 2-bend Z — the minimum for a both-ends-horizontal link with a Y offset,
            // and what A* returns regardless of which of the two equal-cost lattice L's the tie-break picks.
            // A router that returned a staircased mess would exceed the 2-bend bound. (This bounds gross
            // untidiness; the turn penalty's finer tidying is eyeballed at the in-Editor checkpoint — a unit
            // fixture cannot pin it without depending on the tie-break, which is not a router guarantee.)
            {
                var path = RoomLinkGeometry.AStarRoute(P(13f, 30f), East, P(50f, 50f), West, new List<LinkNode>(), noOcc);
                AssertOrtho(path, "clean");
                if (Mathf.Abs(path[0].X - 13f) > 1e-2f || Mathf.Abs(path[0].Y - 30f) > 1e-2f
                    || Mathf.Abs(path[path.Count - 1].X - 50f) > 1e-2f || Mathf.Abs(path[path.Count - 1].Y - 50f) > 1e-2f)
                { Debug.LogError("FAIL clean: path does not run door-to-door"); ok = false; }
                if (Bends(path) > 2)
                { Debug.LogError($"FAIL clean: {Bends(path)} bends for a simple shot — the router is staircasing a clear path"); ok = false; }
            }

            // ── Close facing rooms → ONE straight leg, no stub overshoot ───────────────────────────
            // Two rooms stacked 3 tiles apart, doors facing (south of A, north of B). A full 2-tile stub
            // from each door sails PAST the other, and the path doubles back — a corridor poking into the
            // void, the tight-packing artifact from the checkpoint. The stub cap (half the facing distance)
            // makes the two stubs meet in the middle, so the corridor is ONE straight leg — exactly TWO
            // points. Delete the cap and the stubs overshoot into a 4-point down-up-down wiggle; assert the
            // point COUNT, because that wiggle is collinear (a Bends()/Y-range check would miss it — the
            // vacuity this fixture was rewritten to close).
            {
                var a = N(1, 30f, 20f, 6f, 4f);   // raw Y 18..22
                var b = N(2, 30f, 27f, 6f, 4f);   // raw Y 25..29 — the door gap is 22..25 = 3 tiles
                var path = RoomLinkGeometry.AStarRoute(P(30f, 22f), South, P(30f, 25f), North, new List<LinkNode> { a, b }, noOcc);
                AssertOrtho(path, "close-facing");
                if (path.Count != 2)
                { Debug.LogError($"FAIL close-facing: {path.Count} points, want 2 — the stubs overshot and the path doubled back instead of one straight leg"); ok = false; }
                else if (Mathf.Abs(path[0].X - 30f) > 1e-2f || Mathf.Abs(path[0].Y - 22f) > 1e-2f
                      || Mathf.Abs(path[1].X - 30f) > 1e-2f || Mathf.Abs(path[1].Y - 25f) > 1e-2f)
                { Debug.LogError($"FAIL close-facing: the single leg is not the straight door-to-door segment"); ok = false; }
            }

            // ── A room dead between the ends → routed AROUND it, never through ─────────────────────
            {
                var blocker = N(9, 31f, 30f, 10f, 10f);   // raw X 26..36, Y 25..35 — on the door line
                if (!StraightLineHits(P(13f, 30f), P(50f, 30f), blocker))
                { Debug.LogError("FAIL around setup: the blocker does not block — proves nothing"); ok = false; }
                var path = RoomLinkGeometry.AStarRoute(P(13f, 30f), East, P(50f, 30f), West, new List<LinkNode> { blocker }, noOcc);
                AssertOrtho(path, "around");
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], blocker))
                    { Debug.LogError($"FAIL around: leg {i} crosses the room A* was meant to avoid"); ok = false; break; }
            }

            // ── A sub-clearance gap is NOT threaded — the path goes AROUND ─────────────────────────
            // Two wide rooms stacked so their INFLATED rects strictly overlap across y=30 (raw gap 0, but
            // the point is clearance): top inflated Y [23,31], bottom [29,37], overlap [29,31]. A leg at
            // y=30 is interior to both and blocked, so a clearance-respecting router must detour above the
            // top room (to y=23) or below the bottom one (to y=37). Asserting "crosses neither raw rect"
            // would be VACUOUS — a path staying at y=30 in the RAW gap also crosses neither raw rect. So
            // assert it actually left the gap band. Remove the clearance inflation and the y=30 lane opens
            // and it threads straight → never leaves the band → fails.
            {
                var top = N(1, 31f, 27f, 30f, 6f);    // raw Y 24..30, inflated 23..31
                var bot = N(2, 31f, 33f, 30f, 6f);    // raw Y 30..36, inflated 29..37 — inflated rects OVERLAP [29,31]
                var path = RoomLinkGeometry.AStarRoute(P(5f, 30f), East, P(57f, 30f), West, new List<LinkNode> { top, bot }, noOcc);
                AssertOrtho(path, "narrow-gap");
                bool wentAround = false;
                foreach (var pt in path) if (pt.Y < 28f || pt.Y > 32f) wentAround = true;
                if (!wentAround)
                { Debug.LogError("FAIL narrow-gap: the path stayed in the gap band — it threaded a sub-clearance gap instead of routing around the rooms"); ok = false; }
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], top) || SegmentEntersRect(path[i], path[i + 1], bot))
                    { Debug.LogError($"FAIL narrow-gap: leg {i} clipped a room"); ok = false; break; }
            }

            // ── Corridor soft-cost, AVOID: routed around a corridor when a detour lane exists ───────
            // A room forces link 2 to detour left (x=27, near) or right (x=41, far). Occupancy fills the
            // NEAR lane. Compared with-vs-without: WITHOUT occupancy A* takes the near lane (shorter) and
            // runs along where the corridor sits — the premise. WITH occupancy the near lane is penalized
            // per overlapped edge and A* flips to the far lane, clean. Self-validating: if the "without"
            // run does NOT use the near lane the setup is wrong (it says so); if the "with" run still
            // overlaps, the penalty is too weak for this geometry (it says that instead).
            {
                var room = new List<LinkNode> { N(9, 34f, 30f, 12f, 8f) };   // inflated X 27..41 — lanes at 27 (near) / 41 (far)
                var occSeg = (P(27f, 20f), P(27f, 40f));                     // fills the near lane
                var occ = new List<(LinkPoint a, LinkPoint b)> { occSeg };
                var without = RoomLinkGeometry.AStarRoute(P(31f, 15f), P(0f, 1f), P(31f, 45f), North, room, noOcc);
                var with = RoomLinkGeometry.AStarRoute(P(31f, 15f), P(0f, 1f), P(31f, 45f), North, room, occ);
                AssertOrtho(with, "avoid");
                if (CrossCount(without, occSeg.Item1, occSeg.Item2) == 0)
                { Debug.LogError("FAIL avoid setup: without occupancy the path did NOT take the near lane — this fixture cannot show avoidance"); ok = false; }
                else if (CrossCount(with, occSeg.Item1, occSeg.Item2) != 0)
                { Debug.LogError("FAIL avoid: with occupancy the path still runs along the corridor — CorridorCrossPenalty did not steer it to the clear lane"); ok = false; }
            }

            // ── Corridor soft-cost, CROSS: a field-spanning corridor is crossed, not treated as a wall ─
            // Occupancy spans the whole width at y=30; any vertical link must cross it. A SOFT cost lets
            // A* pay and cross (and reach); if corridors were HARD like rooms, every crossing edge would be
            // blocked and the link could not get through at all. Assert it reaches AND is orthogonal AND
            // does cross — a sanity check that the penalty never blocks routing.
            {
                var occSeg = (P(-40f, 30f), P(80f, 30f));
                var occ = new List<(LinkPoint a, LinkPoint b)> { occSeg };
                var path = RoomLinkGeometry.AStarRoute(P(31f, 15f), P(0f, 1f), P(31f, 45f), North, new List<LinkNode>(), occ);
                AssertOrtho(path, "cross");
                if (Mathf.Abs(path[path.Count - 1].Y - 45f) > 1e-2f)
                { Debug.LogError("FAIL cross: link did not reach its far end — a soft corridor cost blocked routing like a wall"); ok = false; }
                if (CrossCount(path, occSeg.Item1, occSeg.Item2) == 0)
                { Debug.LogError("FAIL cross: link somehow avoided a field-spanning corridor — impossible unless it left the field"); ok = false; }
            }

            // ── Fork exemption: a link STARTING on an occupancy segment routes without hanging ──────
            // A fork's `from` lies ON its trunk. This is a SANITY check — the link starts on the bar and
            // must still return a valid orthogonal path that reaches its goal, never hanging or exploding.
            // The exemption's full effect (the fork branches off the trunk instead of being shoved away by
            // the penalty) is not bulletproof to isolate in a unit fixture — it is verified by the reviewer
            // reading the `PointOnSeg` filter in AStarRoute and by the in-Editor checkpoint (forks visibly
            // branch off their trunks). If this fixture ever HANGS or the goal is unreached, the exemption
            // or the start-on-occupancy handling is broken.
            {
                var occ = new List<(LinkPoint a, LinkPoint b)> { (P(20f, 30f), P(42f, 30f)) };
                var path = RoomLinkGeometry.AStarRoute(P(31f, 30f), default, P(31f, 50f), North, new List<LinkNode>(), occ);
                AssertOrtho(path, "fork-exempt");
                if (Mathf.Abs(path[0].X - 31f) > 1e-2f || Mathf.Abs(path[0].Y - 30f) > 1e-2f
                    || Mathf.Abs(path[path.Count - 1].Y - 50f) > 1e-2f)
                { Debug.LogError("FAIL fork-exempt: a link starting on a corridor did not run from its start to its goal"); ok = false; }
            }

            // ── Boxed in → A7 fallback to OrthogonalRoute, no hang, no empty path ────────────────────
            // A single finite wall can always be gone around (A* slides along its inflated edge), so a
            // "wall" fixture would NOT exercise A7. Instead ENGULF the goal's stub-end: a room centred on
            // b2 makes the goal node interior, every edge into it crosses the room and is blocked, A*
            // fails, and A7 must return the OrthogonalRoute fallback. Assert the path EQUALS that fallback
            // — delete A7 (return an empty list on failure) and this mismatches → fails.
            {
                var rooms = new List<LinkNode> { N(9, 32f, 30f, 20f, 20f) };   // inflated 21..43 × 19..41; b2=(32,30) is deep inside
                var path = RoomLinkGeometry.AStarRoute(P(5f, 5f), East, P(30f, 30f), East, rooms, noOcc);
                var fallback = RoomLinkGeometry.OrthogonalRoute(P(5f, 5f), East, P(30f, 30f), East, rooms);
                AssertOrtho(path, "boxed");
                bool matches = path.Count == fallback.Count;
                for (int i = 0; matches && i < path.Count; i++)
                    if (Mathf.Abs(path[i].X - fallback[i].X) > 1e-4f || Mathf.Abs(path[i].Y - fallback[i].Y) > 1e-4f) matches = false;
                if (!matches)
                { Debug.LogError($"FAIL boxed: an unreachable goal returned {path.Count} points, not the OrthogonalRoute fallback ({fallback.Count}) — A7 is not degrading gracefully"); ok = false; }
            }

            // ── Determinism: reversing the room list AND the occupancy list changes nothing ─────────
            {
                var r1 = N(5, 25f, 26f, 8f, 8f);
                var r2 = N(6, 40f, 34f, 8f, 8f);
                var o1 = (P(15f, 22f), P(15f, 40f));
                var o2 = (P(48f, 22f), P(48f, 40f));
                var fwd = RoomLinkGeometry.AStarRoute(P(13f, 30f), East, P(57f, 30f), West,
                    new List<LinkNode> { r1, r2 }, new List<(LinkPoint a, LinkPoint b)> { o1, o2 });
                var rev = RoomLinkGeometry.AStarRoute(P(13f, 30f), East, P(57f, 30f), West,
                    new List<LinkNode> { r2, r1 }, new List<(LinkPoint a, LinkPoint b)> { o2, o1 });
                if (fwd.Count != rev.Count)
                { Debug.LogError($"FAIL determinism: {fwd.Count} vs {rev.Count} points from reordered inputs"); ok = false; }
                else
                    for (int i = 0; i < fwd.Count; i++)
                        if (Mathf.Abs(fwd[i].X - rev[i].X) > 1e-6f || Mathf.Abs(fwd[i].Y - rev[i].Y) > 1e-6f)
                        { Debug.LogError($"FAIL determinism: point {i} moved when inputs were reordered"); ok = false; break; }
            }

            Debug.Log(ok ? "Self-Test A* Route: PASS" : "Self-Test A* Route: FAIL");
        }

        /// <summary>Test-only axis-aligned segment overlap (bounding-box), independent of the module's own
        /// SegBBoxOverlap so a bug there cannot hide behind an identical test.</summary>
        static bool RoomLinkGeometry_SegOverlap(LinkPoint a, LinkPoint b, LinkPoint c, LinkPoint d)
        {
            float aMinX = Mathf.Min(a.X, b.X), aMaxX = Mathf.Max(a.X, b.X);
            float aMinY = Mathf.Min(a.Y, b.Y), aMaxY = Mathf.Max(a.Y, b.Y);
            float bMinX = Mathf.Min(c.X, d.X), bMaxX = Mathf.Max(c.X, d.X);
            float bMinY = Mathf.Min(c.Y, d.Y), bMaxY = Mathf.Max(c.Y, d.Y);
            return aMinX <= bMaxX + 1e-3f && bMinX <= aMaxX + 1e-3f && aMinY <= bMaxY + 1e-3f && bMinY <= aMaxY + 1e-3f;
        }

        [ContextMenu("Self-Test: Orthogonal Route")]
        public void SelfTestOrthogonalRoute()
        {
            bool ok = true;

            var East = P(1f, 0f);
            var West = P(-1f, 0f);
            var North = P(0f, -1f);   // tile Y grows SOUTH

            // ── EVERY leg is strictly H or V — the headline, asserted on every fixture below ────────────
            // (local function so no fixture can forget it)
            void AssertOrthogonal(List<LinkPoint> path, string who)
            {
                for (int i = 0; i < path.Count - 1; i++)
                {
                    bool h = Mathf.Abs(path[i].Y - path[i + 1].Y) <= 1e-3f;
                    bool v = Mathf.Abs(path[i].X - path[i + 1].X) <= 1e-3f;
                    if (!h && !v)
                    { Debug.LogError($"FAIL {who}: leg {i} ({path[i].X:F1},{path[i].Y:F1})→({path[i+1].X:F1},{path[i+1].Y:F1}) is DIAGONAL"); ok = false; return; }
                }
            }

            // ── A clean L: one bend, and the door's axis is obeyed ─────────────────────────────────
            {
                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(30f, 50f), North, new List<LinkNode>());
                AssertOrthogonal(path, "clean-L");
                if (path.Count != 3)
                { Debug.LogError($"FAIL clean-L: {path.Count} points, want 3 — an unobstructed link needs exactly one bend"); ok = false; }
                else if (Mathf.Abs(path[1].X - 30f) > 1e-3f || Mathf.Abs(path[1].Y - 30f) > 1e-3f)
                { Debug.LogError($"FAIL clean-L: the bend is at ({path[1].X:F1},{path[1].Y:F1}), want (30.0,30.0)"); ok = false; }
            }

            // ── O5: the first leg leaves ALONG the door's normal, for at least StubTiles ───────────
            // Structural, and it is the trap that catches a Simplify() that merges a doubling-back pair:
            // that bug makes the first leg run the OPPOSITE way out of the door.
            {
                var obstacles = new List<LinkNode> { N(1, 20f, 30f, 6f, 6f), N(2, 2f, 30f, 6f, 6f) };
                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(23f, 30f), East, P(5f, 30f), East, obstacles);
                AssertOrthogonal(path, "stub");
                if (path.Count < 2)
                { Debug.LogError($"FAIL stub: {path.Count} points — there is no first leg to check"); ok = false; }
                else
                {
                    float dx = path[1].X - path[0].X, dy = path[1].Y - path[0].Y;
                    if (dx * East.X + dy * East.Y < RoomLinkGeometry.StubTiles - 1e-3f)
                    { Debug.LogError($"FAIL stub: the first leg runs ({dx:F1},{dy:F1}) — it must leave EAST for at least {RoomLinkGeometry.StubTiles} tiles"); ok = false; }
                }
            }

            // ── O5's CLAMP: a stub at or below the clearance must be corrected, not obeyed ─────────
            // Every other fixture uses the defaults (2 > 1), so the clamp's branch never runs — delete the
            // clamp and they all still pass. It is not cosmetic: with stub <= clearance the turn point
            // lands ON or INSIDE the room's own inflated rect, so that room is exempt from the leg turning
            // there (spec O8), the straight shot back across it scores a perfect zero, and it wins on
            // bends. Traced: unclamped at stub=0.5 the winner is [(23,30),(23.5,30),(5,30)] — straight
            // through room 1.
            {
                var self = N(1, 20f, 30f, 6f, 6f);
                var far = N(2, 2f, 30f, 6f, 6f);
                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(23f, 30f), East, P(5f, 30f), East, new List<LinkNode> { self, far },
                    clearanceTiles: 1f, stubTiles: 0.5f);          // below the clearance ring — must be clamped UP
                AssertOrthogonal(path, "clamp");
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], self))
                    { Debug.LogError($"FAIL clamp: leg {i} runs across the link's OWN room 1 — a stub below ClearanceTiles was obeyed instead of clamped"); ok = false; break; }
            }

            // ── O7: a target BEHIND the door's wall must be reached AROUND the room, not across it ──
            // Room 1's east door, target to the WEST. Under the revoked C3 (own rooms are not obstacles)
            // the straight shot wins on bends and is drawn straight through room 1. This is the fixture
            // that rule was invisible to.
            {
                var self = N(1, 20f, 30f, 6f, 6f);
                var far = N(2, 2f, 30f, 6f, 6f);
                if (!StraightLineHits(P(23f, 30f), P(5f, 30f), self))
                { Debug.LogError("FAIL O7 setup: the straight door-to-door line misses room 1 — this fixture would prove nothing"); ok = false; }

                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(23f, 30f), East, P(5f, 30f), East, new List<LinkNode> { self, far });
                AssertOrthogonal(path, "O7");
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], self))
                    { Debug.LogError($"FAIL O7: leg {i} runs across the link's OWN room 1 — own rooms must be obstacles"); ok = false; break; }
            }

            // ── A trivial link is ONE straight leg ─────────────────────────────────────────────────
            // Two facing doors on one line. Note this does NOT test O8: without the "or END" skip the
            // straight line is still the winner (see the O8 fixture below for why).
            {
                var a = N(1, 10f, 30f, 6f, 6f);
                var b = N(2, 40f, 30f, 6f, 6f);
                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(37f, 30f), West, new List<LinkNode> { a, b });
                AssertOrthogonal(path, "trivial");
                if (path.Count != 2)
                { Debug.LogError($"FAIL trivial: {path.Count} points, want 2 — two facing doors on one line need a single straight leg"); ok = false; }
            }

            // ── O8: "or END" is what lets a CLEAN path be told apart from a CROSSING one ───────────
            // Every path's last leg ends inside the far room, so without the "or END" half of the skip
            // EVERY candidate scores at least one dirty leg. That is not a harmless uniform offset: it
            // makes "crosses nothing" (0) and "crosses the blocker" (1) TIE at 1 — and `bends` then hands
            // the win to the straight line THROUGH the blocker. So assert the winner is clean AND bent;
            // asserting cleanliness alone on an unblocked fixture proves nothing.
            {
                var a = N(1, 10f, 30f, 6f, 6f);
                var b = N(2, 40f, 30f, 6f, 6f);
                var blocker = N(9, 25f, 30f, 8f, 8f);
                if (!StraightLineHits(P(13f, 30f), P(37f, 30f), blocker))
                { Debug.LogError("FAIL O8 setup: the blocker does not block — this fixture would prove nothing"); ok = false; }

                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(37f, 30f), West, new List<LinkNode> { a, b, blocker });
                AssertOrthogonal(path, "O8");
                if (path.Count < 4)
                { Debug.LogError($"FAIL O8: {path.Count} points — the router took the straight line through the blocker, so a clean path can no longer be told from a crossing one"); ok = false; }
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], blocker))
                    { Debug.LogError($"FAIL O8: leg {i} crosses the blocker"); ok = false; break; }
            }

            // ── O3: the mid-line comes from the BLOCKER'S EDGE, not just the midpoint ──────────────
            // Both doors sit at Y=30, so every HVH candidate collapses to the straight line and only a VHV
            // can get around — i.e. the ROWS carry this fixture. The blocker straddles the midpoint row,
            // so a router offering only the midpoint has no clean candidate at all: drop obstacle edges
            // from the mid-line set and the best left is the straight line through the blocker.
            {
                var blocker = N(9, 25f, 30f, 10f, 30f);   // spans X 20..30, Y 15..45; the midpoint row is 30
                if (!StraightLineHits(P(13f, 30f), P(37f, 30f), blocker))
                { Debug.LogError("FAIL O3 setup: the blocker does not block — this fixture would prove nothing"); ok = false; }

                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(37f, 30f), West, new List<LinkNode> { blocker });
                AssertOrthogonal(path, "O3");
                if (path.Count < 4)
                { Debug.LogError($"FAIL O3: {path.Count} points — nothing bent, so the router had no clean candidate to pick"); ok = false; }
                for (int i = 0; i < path.Count - 1; i++)
                    if (SegmentEntersRect(path[i], path[i + 1], blocker))
                    { Debug.LogError($"FAIL O3: leg {i} still crosses the blocker — the mid-line candidates do not include its edges"); ok = false; break; }
            }

            // ── Many blockers: the router stays orthogonal, finite, and bounded ────────────────────
            // NOT an O9 fixture, and deliberately not labelled one: "return the least-bad when nothing is
            // clean" is not a branch, it is the ABSENCE of a `return null`, so no rule can be deleted to
            // make such a fixture fail. (The previous version claimed O9 and did not even reach it — the
            // wall's own inflated edge handed the router a clean row around its end.)
            //
            // What IS checkable: a path is [from] + at most 4 mid-points + [to], so 6 points is a hard
            // ceiling. Nothing else pins that, and a candidate builder or Simplify that ran away would
            // blow straight through it.
            {
                var obstacles = new List<LinkNode>();
                for (int i = 0; i < 6; i++) obstacles.Add(N(20 + i, 20f + i * 4f, 30f, 4f, 80f));
                var path = RoomLinkGeometry.OrthogonalRoute(
                    P(5f, 30f), East, P(60f, 30f), West, obstacles);
                AssertOrthogonal(path, "many-blockers");
                if (path.Count < 2 || path.Count > 6)
                { Debug.LogError($"FAIL many-blockers: {path.Count} points — a path is [from] + at most 4 mid-points + [to], so 2..6"); ok = false; }
                foreach (var pt in path)
                    if (float.IsNaN(pt.X) || float.IsNaN(pt.Y) || float.IsInfinity(pt.X) || float.IsInfinity(pt.Y))
                    { Debug.LogError("FAIL many-blockers: NaN/Inf in the path"); ok = false; break; }
            }

            // ── Determinism 1: the obstacle LIST's order must not change the answer ────────────────
            // Re-running identical input proves nothing (deterministic code, identical result). This
            // reverses the list, which is the only thing that exercises the id sort behind the mid-line
            // generation. The boxes MUST straddle the door row or they are filtered out as "not nearby"
            // and the sort runs on an empty list — proving nothing while looking thorough.
            //
            // Rows 21 and 39 are the only clean ones and they tie on length, so candidate ORDER decides.
            // With the sort both directions yield rows [30,21,31,29,39] and r=21 wins. Without it the
            // reversed list yields [30,29,39,21,31], r=39 wins first, and the path flips to the far side.
            {
                var b1 = N(5, 25f, 26f, 8f, 8f);   // inflated Y 21..31 — straddles the door row
                var b2 = N(6, 25f, 34f, 8f, 8f);   // inflated Y 29..39 — straddles it too
                var fwd = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(37f, 30f), West, new List<LinkNode> { b1, b2 });
                var rev = RoomLinkGeometry.OrthogonalRoute(
                    P(13f, 30f), East, P(37f, 30f), West, new List<LinkNode> { b2, b1 });

                if (fwd.Count < 3)
                { Debug.LogError($"FAIL order-independence setup: {fwd.Count} points — nothing bent, so no candidate order was ever consulted"); ok = false; }
                else if (fwd.Count != rev.Count)
                { Debug.LogError($"FAIL order-independence: {fwd.Count} points vs {rev.Count} from a reordered obstacle list"); ok = false; }
                else
                    for (int i = 0; i < fwd.Count; i++)
                        if (Mathf.Abs(fwd[i].X - rev[i].X) > 1e-6f || Mathf.Abs(fwd[i].Y - rev[i].Y) > 1e-6f)
                        { Debug.LogError($"FAIL order-independence: point {i} moved when the obstacle list was reversed — list position is deciding, not the id sort"); ok = false; break; }
            }

            // ── Determinism 2: a hair's movement must not flip the chosen candidate ────────────────
            // The real anti-flicker property: the caller re-routes every frame WHILE A ROOM IS DRAGGED.
            // A blocker dead-on the path has two equal-length ways round; nudge it 1e-5 either way and the
            // length compare must still read a tie (the 1e-4 epsilon) and defer to the candidate order.
            {
                float[] nudges = { 0f, 1e-5f, -1e-5f };
                float firstSide = 0f;
                for (int k = 0; k < nudges.Length; k++)
                {
                    var path = RoomLinkGeometry.OrthogonalRoute(
                        P(13f, 30f), East, P(37f, 30f), West,
                        new List<LinkNode> { N(9, 25f, 30f + nudges[k], 8f, 8f) });

                    // WHICH SIDE it went is the path's furthest excursion off the door line — NOT path[1],
                    // which is the stub's turn point and sits at the door's own Y on every candidate.
                    float side = 0f;
                    foreach (var pt in path)
                        if (Mathf.Abs(pt.Y - 30f) > Mathf.Abs(side)) side = pt.Y - 30f;

                    if (Mathf.Abs(side) < 1e-3f)
                    { Debug.LogError($"FAIL stability setup: nudge {k} left the path on the door line — nothing bent, so no side was chosen"); ok = false; break; }

                    if (k == 0) firstSide = side;
                    else if (Mathf.Sign(side) != Mathf.Sign(firstSide))
                    { Debug.LogError($"FAIL stability: nudging the blocker by {nudges[k]} flipped the route to the other side (excursion {firstSide:F2} → {side:F2}) — corridors will flicker while a room is dragged"); ok = false; break; }
                }
            }

            Debug.Log(ok ? "Self-Test Orthogonal Route: PASS" : "Self-Test Orthogonal Route: FAIL");
        }

        // ── routing fixture helpers ─────────────────────────────────────────────────────────────

        /// <summary>Every leg of a link's path, in emission order. A link no longer owns exactly one
        /// segment — the orthogonal router splits it into H/V legs wherever it turns.</summary>
        static List<LinkSegment> SegmentsForEdge(LinkGeometry g, int edgeIndex)
        {
            var list = new List<LinkSegment>();
            foreach (var s in g.Segments) if (s.EdgeIndex == edgeIndex) list.Add(s);
            return list;
        }

        /// <summary>A link's FIRST leg — the one that starts at its door or fork point. Fixtures asserting
        /// where a link ATTACHES must use this, not "some segment of that edge".</summary>
        static LinkSegment FirstLegForEdge(LinkGeometry g, int edgeIndex)
        {
            foreach (var s in g.Segments) if (s.EdgeIndex == edgeIndex) return s;
            return null;
        }

        static LinkPoint P(float x, float y) => new LinkPoint { X = x, Y = y };
        static LinkNode N(int id, float cx, float cy, float w, float h)
            => new LinkNode { Id = id, CX = cx, CY = cy, W = w, H = h };

        /// <summary>Does the segment enter the node's RAW rect (no clearance)? Independent
        /// reimplementation — a sampling check rather than the slab method the module uses, so a bug in
        /// the module's own intersection math cannot hide behind an identical test.</summary>
        static bool SegmentEntersRect(LinkPoint a, LinkPoint b, LinkNode n)
        {
            const int Samples = 200;
            float hw = n.W * 0.5f - 1e-3f, hh = n.H * 0.5f - 1e-3f;
            for (int i = 1; i < Samples; i++)
            {
                float t = i / (float)Samples;
                float x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                if (x > n.CX - hw && x < n.CX + hw && y > n.CY - hh && y < n.CY + hh) return true;
            }
            return false;
        }

        static bool StraightLineHits(LinkPoint a, LinkPoint b, LinkNode n) => SegmentEntersRect(a, b, n);

        // ── fixtures + helpers ────────────────────────────────────────────────────────────────────

        static List<LinkNode> FanNodes()
        {
            var nodes = new List<LinkNode> { new LinkNode { Id = 1, CX = 30f, CY = 30f, W = 8f, H = 8f } };
            for (int i = 0; i < 5; i++)
                nodes.Add(new LinkNode { Id = 10 + i, CX = 60f + i * 3f, CY = 14f + i * 8f, W = 5f, H = 5f });
            return nodes;
        }

        static List<LinkEdge> FanEdges()
        {
            var edges = new List<LinkEdge>();
            for (int i = 0; i < 5; i++) edges.Add(new LinkEdge { A = 1, B = 10 + i });
            return edges;
        }

        static bool HasDoorAt(LinkGeometry g, float x, float y)
        {
            foreach (var d in g.Doors)
                if (Mathf.Abs(d.X - x) < 1e-2f && Mathf.Abs(d.Y - y) < 1e-2f) return true;
            return false;
        }

        static bool IsStrictlyInside(List<LinkNode> nodes, LinkPoint p)
        {
            foreach (var n in nodes)
                if (p.X > n.CX - n.W * 0.5f + 1e-3f && p.X < n.CX + n.W * 0.5f - 1e-3f &&
                    p.Y > n.CY - n.H * 0.5f + 1e-3f && p.Y < n.CY + n.H * 0.5f - 1e-3f) return true;
            return false;
        }

        static float DistanceToSegment(LinkPoint p, LinkPoint a, LinkPoint b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-9f) return Mathf.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            float t = Mathf.Clamp01(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2);
            float qx = a.X + t * dx, qy = a.Y + t * dy;
            return Mathf.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
        }
    }
}
