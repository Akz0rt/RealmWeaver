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
                    new LinkNode { Id = 2, CX = 50f, CY = 24f, W = 6f, H = 6f },   // north target
                    new LinkNode { Id = 3, CX = 50f, CY = 36f, W = 6f, H = 6f },   // south target
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

                // Non-crossing: the segment to the NORTH target must start at the NORTHER door.
                var segToNorth = SegmentForEdge(g, 0);
                var segToSouth = SegmentForEdge(g, 1);
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

                var seg5 = SegmentForEdge(g, 3);
                var seg4 = SegmentForEdge(g, 2);
                if (seg5 == null || seg4 == null)
                { Debug.LogError("FAIL four-on-a-wall: missing segments for edges 3/4"); ok = false; }
                else if (DistanceToSegment(seg5.A, seg4.A, seg4.B) > 1e-2f)
                { Debug.LogError($"FAIL four-on-a-wall: edge 5 attached at ({seg5.A.X:F1},{seg5.A.Y:F1}), which is NOT on edge 4's segment — the fork search is not recursive"); ok = false; }
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
                    string key = null;
                    if (Mathf.Abs(d.X - 34f) < 1e-3f) key = "E";
                    else if (Mathf.Abs(d.X - 26f) < 1e-3f) key = "W";
                    else if (Mathf.Abs(d.Y - 26f) < 1e-3f) key = "N";
                    else if (Mathf.Abs(d.Y - 34f) < 1e-3f) key = "S";
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

            Debug.Log(ok ? "Self-Test Room Link Geometry: PASS" : "Self-Test Room Link Geometry: FAIL");
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

        static LinkSegment SegmentForEdge(LinkGeometry g, int edgeIndex)
        {
            foreach (var s in g.Segments) if (s.EdgeIndex == edgeIndex) return s;
            return null;
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
