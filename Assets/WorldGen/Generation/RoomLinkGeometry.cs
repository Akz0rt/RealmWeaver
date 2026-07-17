using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>One box in a link graph: an axis-aligned rectangle with an id. TILE space.</summary>
    public struct LinkNode { public int Id; public float CX, CY, W, H; }

    /// <summary>An undirected link between two LinkNode ids.</summary>
    public struct LinkEdge { public int A, B; }

    /// <summary>A point in TILE space.</summary>
    public struct LinkPoint { public float X, Y; }

    /// <summary>One drawn piece of a link's route. EdgeIndex is the index into the input edge list, so
    /// a caller can map geometry back to the link that produced it.</summary>
    public class LinkSegment { public LinkPoint A, B; public int EdgeIndex; }

    /// <summary>Everything Build resolved: the routed segments, the door points on node walls, and the
    /// fork points where a link tapped into already-built geometry.</summary>
    public class LinkGeometry
    {
        public List<LinkSegment> Segments = new List<LinkSegment>();
        public List<LinkPoint> Doors = new List<LinkPoint>();
        public List<LinkPoint> Forks = new List<LinkPoint>();
    }

    /// <summary>
    /// Routes links between axis-aligned boxes: each link leaves a box through an explicit DOOR on the
    /// wall facing its target; a wall carries at most MaxDoorsPerWall doors, and any further link on that
    /// wall FORKS off the geometry already built from it.
    ///
    /// DELIBERATELY knows nothing about dungeons — no Room, no RoomType, no DungeonLevel, no UnityEngine.
    /// It takes rectangles and links and returns geometry. That is what lets the same routing serve
    /// building and city maps later (the user's stated plan); a module that knew RoomType would force
    /// either dragging dungeon types into that code or copying this math.
    ///
    /// Everything here is DERIVED — nothing is stored back. Boxes move constantly (drag, cascade, leash),
    /// and a stored door would end up on a wall that no longer faces its neighbour.
    ///
    /// Pure and deterministic: same input → same output, so a caller may re-Build every frame.
    /// </summary>
    public static class RoomLinkGeometry
    {
        /// <summary>Doors a single wall may carry. A further link on a full wall forks instead.</summary>
        public const int MaxDoorsPerWall = 2;

        /// <summary>How far a detour keeps clear of the box it routes around, in tiles. Hugging a corner
        /// exactly makes the link graze the wall. TUNABLE — the user eyeballs it.</summary>
        public const float ClearanceTiles = 1f;

        /// <summary>Cap on detour re-checks. Bending around one box can push a leg into the next, so the
        /// pass iterates; a boxed-in layout must degrade to a visible artifact the user can fix by moving
        /// a box, never to a hang. TUNABLE.</summary>
        public const int MaxDetourIterations = 8;

        enum Wall { North, East, South, West }

        // Hoisted: Build advertises per-frame use, and this was allocating twice per node per call.
        static readonly Wall[] AllWalls = { Wall.North, Wall.East, Wall.South, Wall.West };

        // One link as seen FROM one node.
        struct Attachment
        {
            public int EdgeIndex;
            public int SelfId, OtherId;
            public Wall Wall;
            public float Distance;      // centre-to-centre, orders door priority (nearest wins a door)
            public float AlongAxis;     // the target's position along the wall's axis, orders the doors
        }

        public static LinkGeometry Build(IReadOnlyList<LinkNode> nodes, IReadOnlyList<LinkEdge> edges)
        {
            var g = new LinkGeometry();
            if (nodes == null || edges == null || nodes.Count == 0 || edges.Count == 0) return g;

            var byId = new Dictionary<int, LinkNode>();
            foreach (var n in nodes) byId[n.Id] = n;

            // Every link contributes an attachment at EACH end — a link may earn a door on one side and
            // have to fork on the other.
            var perNode = new Dictionary<int, List<Attachment>>();
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!byId.TryGetValue(e.A, out var a) || !byId.TryGetValue(e.B, out var b)) continue;   // dangling edge — skip, never throw
                AddAttachment(perNode, a, b, i);
                AddAttachment(perNode, b, a, i);
            }

            // Which point does edge i leave node n from? Resolved in TWO passes, and the order matters:
            // a fork must tap the geometry that is actually DRAWN. Drawn segments run door-to-door, so if
            // pass B ran per-node interleaved with pass A, a wall's trunk would only be known as
            // door→other's CENTRE — and the nearest point on that to a distant target is its far end, i.e.
            // a tap point sitting INSIDE the other room, on a stretch that is never drawn.
            var endpoint = new Dictionary<(int edge, int node), LinkPoint>();

            var nodeIds = new List<int>(perNode.Keys);
            nodeIds.Sort();                       // deterministic node order

            // Sort each wall's attachments once: nearest targets earn the doors, farther ones fork off
            // what the near ones built. Ties break on the target's id so nothing depends on list order.
            var wallsOf = new Dictionary<(int node, Wall wall), List<Attachment>>();
            foreach (int nodeId in nodeIds)
                foreach (var at in perNode[nodeId])
                {
                    var key = (nodeId, at.Wall);
                    if (!wallsOf.TryGetValue(key, out var list)) { list = new List<Attachment>(); wallsOf[key] = list; }
                    list.Add(at);
                }
            foreach (var kv in wallsOf)
                kv.Value.Sort((p, q) =>
                {
                    int c = p.Distance.CompareTo(q.Distance);
                    return c != 0 ? c : p.OtherId.CompareTo(q.OtherId);
                });

            // ── PASS A: every door on every wall. Doors depend only on their own node, so this pass is
            // complete and order-independent — which is what lets pass B see real door-to-door trunks.
            foreach (int nodeId in nodeIds)
            {
                var node = byId[nodeId];
                foreach (Wall wall in AllWalls)
                {
                    if (!wallsOf.TryGetValue((nodeId, wall), out var onWall)) continue;

                    int doorCount = Math.Min(MaxDoorsPerWall, onWall.Count);

                    // Door SLOTS are ordered along the wall by the target's own position along that axis,
                    // so two corridors leaving one wall never cross each other right outside it.
                    var doorHolders = onWall.GetRange(0, doorCount);
                    doorHolders.Sort((p, q) =>
                    {
                        int c = p.AlongAxis.CompareTo(q.AlongAxis);
                        return c != 0 ? c : p.OtherId.CompareTo(q.OtherId);
                    });

                    for (int slot = 0; slot < doorHolders.Count; slot++)
                    {
                        var door = DoorPoint(node, wall, slot, doorHolders.Count);
                        g.Doors.Add(door);
                        endpoint[(doorHolders[slot].EdgeIndex, nodeId)] = door;
                    }
                }
            }

            // ── PASS B: forks. Every door is known now, so a trunk's far end is its real door whenever
            // that end earned one. It only falls back to the far node's CENTRE when the far end is itself
            // a fork that this pass has not reached — unavoidable without a fixpoint, rare, and the tap
            // point stays plausible because the fallback end is still in the right direction.
            foreach (int nodeId in nodeIds)
            {
                foreach (Wall wall in AllWalls)
                {
                    if (!wallsOf.TryGetValue((nodeId, wall), out var onWall)) continue;
                    int doorCount = Math.Min(MaxDoorsPerWall, onWall.Count);
                    if (onWall.Count <= doorCount) continue;   // wall not full — nothing forks

                    // Geometry built from THIS wall so far — the fork search's candidate set. It GROWS as
                    // we go, which is exactly what makes the rule self-recursive: a later link may tap a
                    // fork rather than a trunk, with no extra rule.
                    var built = new List<(LinkPoint a, LinkPoint b, int edgeIndex)>();
                    for (int i = 0; i < doorCount; i++)
                    {
                        var at = onWall[i];
                        built.Add((endpoint[(at.EdgeIndex, nodeId)], FarEnd(byId, endpoint, at), at.EdgeIndex));
                    }

                    for (int k = doorCount; k < onWall.Count; k++)
                    {
                        var at = onWall[k];
                        var target = FarEnd(byId, endpoint, at);
                        var fork = NearestPointOn(built, target);
                        g.Forks.Add(fork);
                        endpoint[(at.EdgeIndex, nodeId)] = fork;
                        built.Add((fork, target, at.EdgeIndex));
                    }
                }
            }

            // Emit one segment per edge, between the two resolved endpoints.
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!endpoint.TryGetValue((i, e.A), out var pa)) continue;
                if (!endpoint.TryGetValue((i, e.B), out var pb)) continue;
                g.Segments.Add(new LinkSegment { A = pa, B = pb, EdgeIndex = i });
            }

            return g;
        }

        /// <summary>
        /// Bend `poly` around any `obstacles` its legs cross, in place. Straight/diagonal legs are kept —
        /// a bend is inserted ONLY where a leg actually crosses a box (spec C1: minimal detour, chosen
        /// over full orthogonal routing and over grid pathfinding).
        ///
        /// One blocker per iteration, re-checking from the start each time, because bending around one box
        /// can push a fresh leg into the next. Capped: a boxed-in leg returns as-is rather than hanging.
        ///
        /// Deterministic in every choice — which blocker, which side, which corner order. The caller
        /// re-derives geometry every frame, so a choice left to float noise would make corridors flicker
        /// between sides as rooms move.
        /// </summary>
        public static void DetourAround(List<LinkPoint> poly, IReadOnlyList<LinkNode> obstacles,
            float clearanceTiles = ClearanceTiles, int maxIterations = MaxDetourIterations)
        {
            if (poly == null || poly.Count < 2 || obstacles == null || obstacles.Count == 0) return;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                if (!FindFirstBlock(poly, obstacles, clearanceTiles, out int legIndex, out int obsIndex)) return;

                var p = poly[legIndex];
                var q = poly[legIndex + 1];
                var chain = ChooseDetourChain(p, q, obstacles[obsIndex], clearanceTiles);
                if (chain.Count == 0) return;   // nothing sensible to insert — leave the leg alone

                poly.InsertRange(legIndex + 1, chain);
            }
        }

        /// <summary>The earliest leg that crosses a box, and within that leg the box whose entry is
        /// nearest the leg's start. Ties on box id — never on list order.
        ///
        /// SKIPS any box whose inflated rect already CONTAINS the leg's start (spec C7). You cannot route
        /// around what you are standing on, and this is not hypothetical: a fork taps the nearest point on
        /// built geometry, which is frequently another box's door — exactly ON that box's boundary. Without
        /// the skip, the first check reports a hit at t≈0, a bend is spliced, and the new leg starts inside
        /// the same inflated rect again, spinning to the cap and emitting garbage.</summary>
        static bool FindFirstBlock(List<LinkPoint> poly, IReadOnlyList<LinkNode> obstacles, float clearance,
                                   out int legIndex, out int obsIndex)
        {
            legIndex = -1; obsIndex = -1;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                var p = poly[i]; var q = poly[i + 1];
                float bestT = float.MaxValue; int best = -1;
                for (int o = 0; o < obstacles.Count; o++)
                {
                    if (PointInInflatedRect(p, obstacles[o], clearance)) continue;              // C7
                    if (!SegmentHitsInflatedRect(p, q, obstacles[o], clearance, out float tEntry)) continue;
                    if (tEntry < bestT || (Math.Abs(tEntry - bestT) < 1e-6f && best >= 0 && obstacles[o].Id < obstacles[best].Id))
                    { bestT = tEntry; best = o; }
                }
                if (best >= 0) { legIndex = i; obsIndex = best; return true; }
            }
            return false;
        }

        /// <summary>The corner chain to splice between p and q to get around `blocker`. Inflate the box by
        /// `clearance` → 4 corners (0=NW, 1=NE, 2=SE, 3=SW; tile Y grows SOUTH). Split them by which side
        /// of the line p→q they fall on; each side gives a chain of 1..2 corners, ordered along p→q. Take
        /// the cheaper side by total path length; on a tie take the side holding the LOWER corner index.
        /// A corner exactly ON the line goes to the `cross >= 0` side — by rule, so a box centred dead-on
        /// the path resolves the same way every frame instead of by float noise.</summary>
        static List<LinkPoint> ChooseDetourChain(LinkPoint p, LinkPoint q, LinkNode blocker, float clearance)
        {
            var corners = InflatedCorners(blocker, clearance);

            var sideA = new List<int>();
            var sideB = new List<int>();
            for (int c = 0; c < 4; c++)
            {
                float cross = (q.X - p.X) * (corners[c].Y - p.Y) - (q.Y - p.Y) * (corners[c].X - p.X);
                if (cross >= 0f) sideA.Add(c); else sideB.Add(c);
            }

            var chainA = OrderAlong(p, q, corners, sideA);
            var chainB = OrderAlong(p, q, corners, sideB);

            float costA = ChainCost(p, q, chainA);
            float costB = ChainCost(p, q, chainB);

            if (chainA.Count == 0) return chainB;
            if (chainB.Count == 0) return chainA;
            if (Math.Abs(costA - costB) > 1e-4f) return costA < costB ? chainA : chainB;
            return MinIndex(sideA) <= MinIndex(sideB) ? chainA : chainB;   // deterministic tie-break
        }

        static LinkPoint[] InflatedCorners(LinkNode n, float clearance)
        {
            float hw = n.W * 0.5f + clearance, hh = n.H * 0.5f + clearance;
            return new[]
            {
                new LinkPoint { X = n.CX - hw, Y = n.CY - hh },   // 0 = NW
                new LinkPoint { X = n.CX + hw, Y = n.CY - hh },   // 1 = NE
                new LinkPoint { X = n.CX + hw, Y = n.CY + hh },   // 2 = SE
                new LinkPoint { X = n.CX - hw, Y = n.CY + hh },   // 3 = SW
            };
        }

        /// <summary>The side's corners ordered by their projection along p→q, so the chain runs the same
        /// direction the leg does instead of doubling back.</summary>
        static List<LinkPoint> OrderAlong(LinkPoint p, LinkPoint q, LinkPoint[] corners, List<int> side)
        {
            float dx = q.X - p.X, dy = q.Y - p.Y;
            var idx = new List<int>(side);
            idx.Sort((u, v) =>
            {
                float tu = (corners[u].X - p.X) * dx + (corners[u].Y - p.Y) * dy;
                float tv = (corners[v].X - p.X) * dx + (corners[v].Y - p.Y) * dy;
                int c = tu.CompareTo(tv);
                return c != 0 ? c : u.CompareTo(v);   // deterministic on a tie
            });
            var chain = new List<LinkPoint>(idx.Count);
            foreach (int i in idx) chain.Add(corners[i]);
            return chain;
        }

        static float ChainCost(LinkPoint p, LinkPoint q, List<LinkPoint> chain)
        {
            if (chain.Count == 0) return float.MaxValue;
            float sum = Dist(p, chain[0]);
            for (int i = 1; i < chain.Count; i++) sum += Dist(chain[i - 1], chain[i]);
            return sum + Dist(chain[chain.Count - 1], q);
        }

        static int MinIndex(List<int> side)
        {
            int m = int.MaxValue;
            foreach (int i in side) if (i < m) m = i;
            return m;
        }

        static float Dist(LinkPoint a, LinkPoint b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static bool PointInInflatedRect(LinkPoint p, LinkNode n, float clearance)
        {
            float hw = n.W * 0.5f + clearance, hh = n.H * 0.5f + clearance;
            return p.X >= n.CX - hw && p.X <= n.CX + hw && p.Y >= n.CY - hh && p.Y <= n.CY + hh;
        }

        /// <summary>Segment × inflated-AABB by the slab method. `tEntry` is the entry parameter along
        /// p→q, clamped to [0,1]. False when the segment misses or merely touches the boundary.</summary>
        static bool SegmentHitsInflatedRect(LinkPoint p, LinkPoint q, LinkNode n, float clearance, out float tEntry)
        {
            tEntry = 0f;
            float hw = n.W * 0.5f + clearance, hh = n.H * 0.5f + clearance;
            float minX = n.CX - hw, maxX = n.CX + hw, minY = n.CY - hh, maxY = n.CY + hh;

            float dx = q.X - p.X, dy = q.Y - p.Y;
            float t0 = 0f, t1 = 1f;

            if (!SlabClip(p.X, dx, minX, maxX, ref t0, ref t1)) return false;
            if (!SlabClip(p.Y, dy, minY, maxY, ref t0, ref t1)) return false;
            if (t1 <= t0 + 1e-6f) return false;      // a grazing touch is not a crossing

            tEntry = t0 < 0f ? 0f : t0;
            return true;
        }

        static bool SlabClip(float origin, float delta, float lo, float hi, ref float t0, ref float t1)
        {
            const float Eps = 1e-9f;
            if (Math.Abs(delta) < Eps) return origin >= lo && origin <= hi;   // parallel: in or out of the slab
            float ta = (lo - origin) / delta;
            float tb = (hi - origin) / delta;
            if (ta > tb) { var s = ta; ta = tb; tb = s; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 <= t1;
        }

        static void AddAttachment(Dictionary<int, List<Attachment>> perNode, LinkNode self, LinkNode other, int edgeIndex)
        {
            float dx = other.CX - self.CX, dy = other.CY - self.CY;
            var wall = ChooseWall(self, dx, dy);
            if (!perNode.TryGetValue(self.Id, out var list)) { list = new List<Attachment>(); perNode[self.Id] = list; }
            list.Add(new Attachment
            {
                EdgeIndex = edgeIndex,
                SelfId = self.Id,
                OtherId = other.Id,
                Wall = wall,
                Distance = (float)Math.Sqrt(dx * dx + dy * dy),
                // A north/south wall runs along X; an east/west wall runs along Y.
                AlongAxis = (wall == Wall.North || wall == Wall.South) ? other.CX : other.CY,
            });
        }

        /// <summary>Pick the wall a link leaves through. The direction is normalized BY HALF-EXTENT, not
        /// compared raw: on an elongated box a diagonal neighbour must exit the LONG wall, not the narrow
        /// end cap, and raw |dx| vs |dy| gets that backwards. Ties resolve to the horizontal axis —
        /// arbitrary, but deterministic. Tile Y grows SOUTH.</summary>
        static Wall ChooseWall(LinkNode self, float dx, float dy)
        {
            float hw = Math.Max(1e-4f, self.W * 0.5f);
            float hh = Math.Max(1e-4f, self.H * 0.5f);
            float rx = Math.Abs(dx) / hw;
            float ry = Math.Abs(dy) / hh;
            if (rx >= ry) return dx >= 0f ? Wall.East : Wall.West;
            return dy >= 0f ? Wall.South : Wall.North;
        }

        /// <summary>Where slot `slot` of `count` sits on `wall`. One door → the wall's midpoint; two →
        /// 1/3 and 2/3 along it. Always exactly ON the boundary, never inside the box.</summary>
        static LinkPoint DoorPoint(LinkNode n, Wall wall, int slot, int count)
        {
            float t = count <= 1 ? 0.5f : (slot + 1f) / (count + 1f);   // 1 → .5 ; 2 → .333/.667
            float hw = n.W * 0.5f, hh = n.H * 0.5f;
            switch (wall)
            {
                case Wall.North: return new LinkPoint { X = n.CX - hw + n.W * t, Y = n.CY - hh };
                case Wall.South: return new LinkPoint { X = n.CX - hw + n.W * t, Y = n.CY + hh };
                case Wall.West:  return new LinkPoint { X = n.CX - hw, Y = n.CY - hh + n.H * t };
                default:         return new LinkPoint { X = n.CX + hw, Y = n.CY - hh + n.H * t };   // East
            }
        }

        /// <summary>The far end of `at`'s drawn segment, as best it is known when pass B runs: the far
        /// node's resolved DOOR if that end earned one — the common case, since pass A resolves every door
        /// before pass B starts — otherwise the far node's CENTRE.
        ///
        /// KNOWN LIMITATION, not a safe approximation. The fallback fires only when an edge forks at BOTH
        /// ends AND the far node's id is higher (both passes walk ids ascending, so a lower-id far node's
        /// fork is already resolved). It needs two rooms each carrying 3+ links on the walls facing each
        /// other. When it fires, the recorded trunk runs door → the far room's CENTRE — a stretch that
        /// PENETRATES that room and is never drawn — so a later fork on this wall can project onto it and
        /// land inside the room, which is precisely what the two-pass split exists to prevent. Resolving
        /// it exactly needs a fixpoint. No self-test covers this branch: every fixture has at most one
        /// full wall. Revisit if a double-full-wall layout ever shows a corridor sprouting from inside a
        /// room.</summary>
        static LinkPoint FarEnd(Dictionary<int, LinkNode> byId,
                                Dictionary<(int edge, int node), LinkPoint> endpoint, Attachment at)
        {
            if (endpoint.TryGetValue((at.EdgeIndex, at.OtherId), out var p)) return p;
            var other = byId[at.OtherId];
            return new LinkPoint { X = other.CX, Y = other.CY };
        }

        /// <summary>Closest point to `target` on any already-built piece: perpendicular projection onto
        /// each segment, clamped to its endpoints. WHOLE segments are candidates, not just their door
        /// ends — a target near a trunk's far end should tap in there rather than walk back to the wall.
        /// `built` is never empty here: a fork only happens once the wall's doors are all placed.</summary>
        static LinkPoint NearestPointOn(List<(LinkPoint a, LinkPoint b, int edgeIndex)> built, LinkPoint target)
        {
            var best = built[0].a;
            float bestD2 = float.MaxValue;
            foreach (var s in built)
            {
                var p = ClosestOnSegment(s.a, s.b, target);
                float d2 = (p.X - target.X) * (p.X - target.X) + (p.Y - target.Y) * (p.Y - target.Y);
                if (d2 < bestD2) { bestD2 = d2; best = p; }
            }
            return best;
        }

        static LinkPoint ClosestOnSegment(LinkPoint a, LinkPoint b, LinkPoint p)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-9f) return a;                        // degenerate (coincident boxes) — no NaN
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new LinkPoint { X = a.X + t * dx, Y = a.Y + t * dy };
        }
    }
}
