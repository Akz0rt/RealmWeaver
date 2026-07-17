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

        enum Wall { North, East, South, West }

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
                foreach (Wall wall in new[] { Wall.North, Wall.East, Wall.South, Wall.West })
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
                foreach (Wall wall in new[] { Wall.North, Wall.East, Wall.South, Wall.West })
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
        /// node's resolved DOOR if that end earned one (the common case — pass A resolved every door
        /// before pass B started), otherwise the far node's centre. The fallback only fires when the far
        /// end is itself a fork that has not been resolved yet; resolving that exactly would need a
        /// fixpoint, and the fallback still points the same way, so a tap computed against it lands
        /// plausibly rather than exactly.</summary>
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
