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
                if (segToNorth != null && segToSouth != null && segToNorth.A.Y > segToSouth.A.Y)
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
                    bool onEdge4 = false;
                    foreach (var leg in legs4)
                        if (DistanceToSegment(seg5.A, leg.A, leg.B) < 1e-2f) { onEdge4 = true; break; }
                    if (!onEdge4)
                    { Debug.LogError($"FAIL four-on-a-wall: edge 5 attached at ({seg5.A.X:F1},{seg5.A.Y:F1}), which is on NO leg of edge 4"); ok = false; }

                    bool onTrunk = false;
                    foreach (int trunkEdge in new[] { 0, 1 })
                        foreach (var leg in SegmentsForEdge(g, trunkEdge))
                            if (DistanceToSegment(seg5.A, leg.A, leg.B) < 1e-2f) { onTrunk = true; break; }
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
                foreach (var s in sg.Segments)
                    if (float.IsNaN(s.A.X) || float.IsNaN(s.A.Y) || float.IsNaN(s.B.X) || float.IsNaN(s.B.Y))
                    { Debug.LogError("FAIL: coincident nodes produced NaN"); ok = false; }
            }

            // ── A link must bend around a room sitting between its two ends ────────────────────────
            // Node 2 sits squarely between 1 and 3. Assert the premise: if it stopped blocking the
            // straight door-to-door line, this fixture would prove nothing.
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
                    // Assert the premise with Build's OWN doors: the straight door-to-door line must really
                    // cross room 2. If room 2 ever stopped blocking it, every assertion below would pass
                    // while proving nothing.
                    if (!StraightLineHits(legs[0].A, legs[legs.Count - 1].B, nodes[1]))
                    { Debug.LogError("FAIL detour-in-build setup: the straight door-to-door line misses room 2 — this fixture would prove nothing"); ok = false; }

                    if (legs.Count < 2)
                    { Debug.LogError($"FAIL detour-in-build: link 1→3 emitted {legs.Count} leg(s) — it still runs straight through room 2"); ok = false; }
                    foreach (var leg in legs)
                        if (SegmentEntersRect(leg.A, leg.B, nodes[1]))
                        { Debug.LogError("FAIL detour-in-build: a leg still crosses room 2"); ok = false; break; }
                }
            }

            Debug.Log(ok ? "Self-Test Room Link Geometry: PASS" : "Self-Test Room Link Geometry: FAIL");
        }

        [ContextMenu("Self-Test: Corridor Detour")]
        public void SelfTestCorridorDetour()
        {
            bool ok = true;

            // ── A clean path must not be touched at all ────────────────────────────────────────────
            // The obstacle sits far off the line. If the detour fired anyway, a straight corridor would
            // gain phantom bends on every frame.
            {
                var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                var obstacles = new List<LinkNode> { N(9, 20f, 40f, 6f, 6f) };
                RoomLinkGeometry.DetourAround(poly, obstacles);
                if (poly.Count != 2)
                { Debug.LogError($"FAIL clean: {poly.Count} points, want 2 — the detour fired when nothing blocked"); ok = false; }
            }

            // ── THE HEADLINE: a link whose straight line crosses a room must bend around it ─────────
            // Room 9 sits dead on the path from (0,0) to (40,0). Assert the premise first: if it ever
            // stopped blocking, everything below would pass vacuously.
            {
                var blocker = N(9, 20f, 0f, 8f, 8f);
                if (!StraightLineHits(P(0f, 0f), P(40f, 0f), blocker))
                { Debug.LogError("FAIL setup: the blocker does not actually block — this fixture would prove nothing"); ok = false; }

                var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { blocker });

                if (poly.Count <= 2)
                { Debug.LogError($"FAIL blocked: {poly.Count} points — the corridor still runs straight through the room"); ok = false; }
                for (int i = 0; i < poly.Count - 1; i++)
                    if (SegmentEntersRect(poly[i], poly[i + 1], blocker))
                    { Debug.LogError($"FAIL blocked: leg {i} ({poly[i].X:F1},{poly[i].Y:F1})→({poly[i+1].X:F1},{poly[i+1].Y:F1}) still crosses the room"); ok = false; break; }
            }

            // ── The detour takes the SHORT way ─────────────────────────────────────────────────────
            // The blocker is pushed NORTH of the path's midline, so going south is clearly shorter. If
            // the side choice were arbitrary, this fails half the time — which is exactly the point.
            {
                var blocker = N(9, 20f, -2f, 8f, 12f);   // spans Y -8..+4 — the path at Y=0 clips its south part
                var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { blocker });

                // Going SOUTH means every bend point is south of the line (Y > 0). Going north would put
                // them at Y < -8 — a much longer way round.
                bool anyNorth = false;
                for (int i = 1; i < poly.Count - 1; i++) if (poly[i].Y < 0f) anyNorth = true;
                if (anyNorth)
                { Debug.LogError("FAIL short-way: the detour went NORTH around a blocker whose south side is nearer"); ok = false; }
            }

            // ── ...and the MIRROR, or "always return sideA" would pass the fixture above ────────────
            // Same geometry reflected: now the NORTH way is shorter. For a west→east leg with Y growing
            // south, `cross >= 0` is always the south side — so without this, a detour hardwired to sideA
            // passes every fixture in this file.
            {
                var blocker = N(9, 20f, 2f, 8f, 12f);   // spans Y -4..+8 — the path at Y=0 clips its north part
                var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { blocker });

                bool anySouth = false;
                for (int i = 1; i < poly.Count - 1; i++) if (poly[i].Y > 0f) anySouth = true;
                if (anySouth)
                { Debug.LogError("FAIL short-way mirror: the detour went SOUTH around a blocker whose north side is nearer"); ok = false; }
            }

            // ── TWO blockers in a row force TWO bends — this is what tests the ITERATION ────────────
            // A single-blocker fixture passes even with the loop capped at one pass. Assert BOTH rooms
            // are cleared, or the re-check after the first bend goes untested.
            {
                var b1 = N(9, 15f, 0f, 6f, 6f);
                var b2 = N(10, 30f, 0f, 6f, 6f);
                var poly = new List<LinkPoint> { P(0f, 0f), P(45f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { b1, b2 });

                foreach (var b in new[] { b1, b2 })
                    for (int i = 0; i < poly.Count - 1; i++)
                        if (SegmentEntersRect(poly[i], poly[i + 1], b))
                        { Debug.LogError($"FAIL two-blockers: leg {i} still crosses room {b.Id} — the detour does not re-check after bending"); ok = false; break; }
            }

            // ── Two same-height rooms in a row: the bend leg runs ALONG the second's inflated edge ─────
            // The ordinary consequence of bending: iteration 1 parks a leg exactly on b1's inflated edge,
            // and b2 shares that edge's Y because it shares CY and H. A leg collinear with an edge puts
            // ALL FOUR of that box's corners on one side of itself — unsplittable. This once emitted a
            // chain visiting all four corners, which drove the corridor through (22,0), b2's dead centre.
            {
                var b1 = N(9, 15f, 0f, 6f, 6f);
                var b2 = N(10, 22f, 0f, 6f, 6f);   // same CY, same H, near-flush → shares b1's inflated edge Y
                var poly = new List<LinkPoint> { P(0f, 0f), P(45f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { b1, b2 });

                foreach (var b in new[] { b1, b2 })
                    for (int i = 0; i < poly.Count - 1; i++)
                        if (SegmentEntersRect(poly[i], poly[i + 1], b))
                        { Debug.LogError($"FAIL flush-pair: leg {i} ({poly[i].X:F1},{poly[i].Y:F1})→({poly[i+1].X:F1},{poly[i+1].Y:F1}) cuts through room {b.Id}"); ok = false; break; }
            }

            // ── Clearance is real ──────────────────────────────────────────────────────────────────
            // Every point must stay at least ClearanceTiles from the blocker's raw rect. The threshold is
            // hardcoded ON PURPOSE: reading ClearanceTiles here would make the fixture vacuous — set the
            // constant to 0 and the comparison could never fire. Bend points land on the INFLATED corner,
            // so they sit at clearance*sqrt(2) ≈ 1.41 from the raw rect; 0.99 leaves float slop under that
            // and still catches a detour that ignored the clearance (its corners would land AT the rect,
            // d = -1).
            {
                var blocker = N(9, 20f, 0f, 8f, 8f);
                if (!StraightLineHits(P(0f, 0f), P(40f, 0f), blocker))
                { Debug.LogError("FAIL clearance setup: the blocker does not block — this fixture would prove nothing"); ok = false; }

                var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { blocker });
                foreach (var pt in poly)
                {
                    float d = DistancePointToRect(pt, blocker);   // negative when inside
                    if (d < 0.99f)
                    { Debug.LogError($"FAIL clearance: point ({pt.X:F1},{pt.Y:F1}) is {d:F2} from the room, want >= 1.0"); ok = false; }
                }
            }

            // ── C7: a leg STARTING on an obstacle must not try to route around it ──────────────────
            // This really happens: a fork taps the nearest point on built geometry, and that point is
            // often another room's DOOR — which sits exactly ON that room's boundary, inside its
            // clearance-inflated rect. Delete C7 and the first check reports a hit at t≈0, splices a
            // bend, and the new leg starts inside the same inflated rect again — spinning to the cap.
            // So assert the path is CLEAN and SHORT, not merely that the call returned.
            {
                var standingOn = N(9, 0f, 0f, 8f, 8f);        // the leg starts on this room's east wall
                var poly = new List<LinkPoint> { P(4f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { standingOn });
                if (poly.Count != 2)
                { Debug.LogError($"FAIL C7: {poly.Count} points — the leg tried to route around the room it starts ON"); ok = false; }
            }

            // ── A boxed-in leg terminates and returns something finite ─────────────────────────────
            {
                var obstacles = new List<LinkNode>();
                for (int i = 0; i < 6; i++) obstacles.Add(N(20 + i, 10f + i * 5f, 0f, 4f, 60f));   // a wall of rooms
                var poly = new List<LinkPoint> { P(0f, 0f), P(45f, 0f) };
                RoomLinkGeometry.DetourAround(poly, obstacles);
                if (poly.Count < 2)
                { Debug.LogError("FAIL boxed-in: the polyline was destroyed"); ok = false; }
                foreach (var pt in poly)
                    if (float.IsNaN(pt.X) || float.IsNaN(pt.Y) || float.IsInfinity(pt.X) || float.IsInfinity(pt.Y))
                    { Debug.LogError("FAIL boxed-in: NaN/Inf in the polyline"); ok = false; break; }
                // Both sides are non-empty (ChooseDetourChain gives up otherwise), so the 4 corners split
                // 3/1 or 2/2 and a chain holds at most 3 — the real per-pass bound. A regression to the
                // old all-four-corners chain would splice 4 a pass and blow straight through this.
                if (poly.Count > 2 + 3 * RoomLinkGeometry.MaxDetourIterations)
                { Debug.LogError($"FAIL boxed-in: {poly.Count} points — a pass may splice at most 3 corners, so the cap bounds this at {2 + 3 * RoomLinkGeometry.MaxDetourIterations}"); ok = false; }
            }

            // ── Determinism 1: the blocker LIST's order must not change the answer ─────────────────
            // Two boxes whose entry parameters are exactly equal (same inflated minX, both straddling the
            // path). Which one is "first" must fall to the id tie-break, never to list position. Reverse
            // the list and the answer must be identical.
            {
                var b1 = N(5, 20f, -2f, 8f, 8f);   // inflated X 15..25, Y -7..3  → entry t = 15/40
                var b2 = N(6, 20f,  2f, 8f, 8f);   // inflated X 15..25, Y -3..7  → entry t = 15/40, an exact tie
                var fwd = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                var rev = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                RoomLinkGeometry.DetourAround(fwd, new List<LinkNode> { b1, b2 });
                RoomLinkGeometry.DetourAround(rev, new List<LinkNode> { b2, b1 });

                if (fwd.Count != rev.Count)
                { Debug.LogError($"FAIL order-independence: {fwd.Count} points vs {rev.Count} from a reordered list"); ok = false; }
                else
                    for (int i = 0; i < fwd.Count; i++)
                        if (Mathf.Abs(fwd[i].X - rev[i].X) > 1e-6f || Mathf.Abs(fwd[i].Y - rev[i].Y) > 1e-6f)
                        { Debug.LogError($"FAIL order-independence: point {i} moved when the blocker list was reversed — list position is deciding, not the id tie-break"); ok = false; break; }
            }

            // ── Determinism 2: a hair's movement must not flip the chosen side ─────────────────────
            // This is the anti-flicker property, and re-running identical input can never test it: the
            // caller re-derives geometry every frame WHILE A ROOM IS BEING DRAGGED. A blocker centred
            // dead-on the path has two equal-cost ways around; nudge it by 1e-5 either way and the cost
            // compare must still read a tie (the 1e-4 epsilon) and defer to the rule. Without that
            // epsilon the two nudges pick OPPOSITE sides and the corridor snaps back and forth.
            {
                float[] nudges = { 0f, 1e-5f, -1e-5f };
                float firstSideY = 0f;
                for (int k = 0; k < nudges.Length; k++)
                {
                    var poly = new List<LinkPoint> { P(0f, 0f), P(40f, 0f) };
                    RoomLinkGeometry.DetourAround(poly, new List<LinkNode> { N(9, 20f, nudges[k], 8f, 8f) });
                    if (poly.Count < 3)
                    { Debug.LogError($"FAIL stability setup: nudge {k} produced {poly.Count} points — nothing bent, so no side was chosen"); ok = false; break; }

                    if (k == 0) firstSideY = poly[1].Y;
                    else if (Mathf.Sign(poly[1].Y) != Mathf.Sign(firstSideY))
                    { Debug.LogError($"FAIL stability: nudging the blocker by {nudges[k]} flipped the detour to the other side (bend Y {firstSideY:F2} → {poly[1].Y:F2}) — corridors will flicker while a room is dragged"); ok = false; break; }
                }
            }

            Debug.Log(ok ? "Self-Test Corridor Detour: PASS" : "Self-Test Corridor Detour: FAIL");
        }

        // ── detour fixture helpers ────────────────────────────────────────────────────────────────

        /// <summary>Every leg of a link's polyline, in emission order. A link no longer owns exactly one
        /// segment — the detour splits it wherever it bends around a box.</summary>
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

        /// <summary>Distance from a point to a rect's boundary; 0 or negative if inside.</summary>
        static float DistancePointToRect(LinkPoint p, LinkNode n)
        {
            float dx = Mathf.Max(Mathf.Abs(p.X - n.CX) - n.W * 0.5f, 0f);
            float dy = Mathf.Max(Mathf.Abs(p.Y - n.CY) - n.H * 0.5f, 0f);
            if (dx == 0f && dy == 0f) return -1f;   // inside
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

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
