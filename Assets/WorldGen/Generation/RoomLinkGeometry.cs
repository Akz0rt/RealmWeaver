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

        /// <summary>How far a link runs straight out of its door along the wall normal before it may turn,
        /// in tiles. MUST exceed ClearanceTiles (OrthogonalRoute clamps it): the turn point has to land
        /// strictly OUTSIDE the box's inflated rect, or that box is exempt from the leg turning there
        /// (spec O8) and the link may run back across its own room. TUNABLE — the user eyeballs it.</summary>
        public const float StubTiles = 2f;

        /// <summary>Slop by which the hit test and the containment test shrink a box, so that merely
        /// TOUCHING its boundary is not a hit. A leg running along an inflated edge already sits at exactly
        /// the clearance we asked for. Both tests must use the same value or they disagree about a point on
        /// the boundary.</summary>
        const float TouchEps = 1e-4f;

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

            // The wall each DOOR sits on, so routing knows which way to leave. Absent for a fork: a fork
            // sits mid-corridor and has no wall — which is precisely the "no normal" signal routing wants.
            var wallOf = new Dictionary<(int edge, int node), Wall>();

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
                        wallOf[(doorHolders[slot].EdgeIndex, nodeId)] = wall;
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

                    // Only a full wall forks, and `built` exists SOLELY for forks to tap, so a wall that
                    // cannot fork needs no provisional routing.
                    //
                    // This guard is safe ONLY because nothing downstream reads pass B's paths: the emission
                    // loop re-routes from `endpoint`, which pass A fills for every door. It was NOT safe in
                    // the previous design, where pass B stashed the very polyline the emission loop drew —
                    // this exact line then silently deleted every corridor on a wall carrying one or two
                    // links, i.e. nearly all of them. Do not move the stash back under it.
                    if (onWall.Count <= doorCount) continue;

                    // Spec C2, still: a trunk is routed BEFORE it enters the fork search's candidate set,
                    // so a fork taps geometry that is actually drawn. Route the trunks afterwards and every
                    // fork hangs beside its trunk instead of on it.
                    var built = new List<(List<LinkPoint> poly, int edgeIndex)>();
                    for (int i = 0; i < doorCount; i++)
                    {
                        var at = onWall[i];
                        built.Add((OrthogonalRoute(
                            endpoint[(at.EdgeIndex, nodeId)], NormalOf(wallOf, at.EdgeIndex, nodeId),
                            FarEnd(byId, endpoint, at), NormalOf(wallOf, at.EdgeIndex, at.OtherId),
                            nodes), at.EdgeIndex));
                    }

                    for (int k = doorCount; k < onWall.Count; k++)
                    {
                        var at = onWall[k];
                        var target = FarEnd(byId, endpoint, at);
                        var fork = NearestPointOnBuilt(built, target);
                        g.Forks.Add(fork);
                        endpoint[(at.EdgeIndex, nodeId)] = fork;
                        built.Add((OrthogonalRoute(
                            fork, default,                                     // a fork has no wall: no normal
                            target, NormalOf(wallOf, at.EdgeIndex, at.OtherId),
                            nodes), at.EdgeIndex));
                    }
                }
            }

            // Route each link ONE more time, now that BOTH ends are resolved. Pass B's paths were
            // provisional: built toward FarEnd, which falls back to the far box's CENTRE when that end had
            // not been reached yet, and they existed only for forks to tap.
            //
            // Routing is deterministic, so wherever the far end earned a door this returns pass B's path
            // exactly, and the forks that tapped it are still on it. Where the far end FORKED the final
            // path differs and a fork tapping this trunk can hang beside it — that needs BOTH ends' walls
            // overfull at once, and it is the same fallback the two-pass split has always accepted.
            //
            // Reading `endpoint` (not a pass-B stash) is what makes this whole. Pass A fills it for every
            // door, so every well-formed edge emits.
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!endpoint.TryGetValue((i, e.A), out var pa)) continue;
                if (!endpoint.TryGetValue((i, e.B), out var pb)) continue;

                var path = OrthogonalRoute(pa, NormalOf(wallOf, i, e.A), pb, NormalOf(wallOf, i, e.B), nodes);
                for (int k = 0; k < path.Count - 1; k++)
                    g.Segments.Add(new LinkSegment { A = path[k], B = path[k + 1], EdgeIndex = i });
            }

            return g;
        }

        static float Dist(LinkPoint a, LinkPoint b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static LinkPoint WallNormal(Wall w)
        {
            switch (w)
            {
                case Wall.East: return new LinkPoint { X = 1f, Y = 0f };
                case Wall.West: return new LinkPoint { X = -1f, Y = 0f };
                case Wall.South: return new LinkPoint { X = 0f, Y = 1f };    // tile Y grows SOUTH
                default: return new LinkPoint { X = 0f, Y = -1f };           // North
            }
        }

        /// <summary>The outward normal of the wall this link's end leaves through, or a zero vector when
        /// that end is a fork — a fork sits mid-corridor and has no wall to leave through.</summary>
        static LinkPoint NormalOf(Dictionary<(int edge, int node), Wall> wallOf, int edge, int node)
            => wallOf.TryGetValue((edge, node), out var w) ? WallNormal(w) : default;

        /// <summary>An orthogonal path from `from` to `to` — every leg strictly horizontal or vertical —
        /// that keeps out of every box in `obstacles`.
        ///
        /// `fromNormal`/`toNormal` are unit wall normals; pass default(LinkPoint) when an end has no wall
        /// (a fork sits mid-corridor; a provisional target is a box's CENTRE). An end with a normal runs
        /// straight out along it for `stubTiles` before the path may turn.
        ///
        /// Scores a handful of candidates — the plain L, and Zs whose mid-line sits on a nearby box's
        /// inflated edge — by (dirty legs, bends, length). Going AROUND a box is not a separate pass here;
        /// it is what picking the right mid-line does (spec O3).
        ///
        /// Never returns null and never fails: when every candidate crosses something the least-bad one is
        /// returned, so a boxed-in layout degrades to a visible artifact the user can fix by dragging a box
        /// (spec O9). Deterministic in every choice — the caller re-derives geometry every frame, and a
        /// choice left to float noise or list order would make corridors flicker as boxes move.
        ///
        /// TWO CALLER OBLIGATIONS, neither checked here. Normals must be axis-aligned (±1,0)/(0,±1): the
        /// "every leg is H or V" headline rests on it, and a diagonal normal yields a diagonal stub in
        /// silence. And box Ids must be unique: List.Sort is introsort, i.e. UNSTABLE, so it normalises
        /// order only for distinct Ids — two boxes sharing one leave the mid-line order decided by the
        /// input arrangement, which is the very flicker the sort exists to prevent.</summary>
        public static List<LinkPoint> OrthogonalRoute(
            LinkPoint from, LinkPoint fromNormal,
            LinkPoint to, LinkPoint toNormal,
            IReadOnlyList<LinkNode> obstacles,
            float clearanceTiles = ClearanceTiles, float stubTiles = StubTiles)
        {
            if (obstacles == null) obstacles = System.Array.Empty<LinkNode>();

            // Spec O5. A turn point ON or INSIDE its own box's inflated rect makes that box exempt from the
            // leg turning there (spec O8), and the link may then run straight back across its own room.
            float stub = stubTiles;

            var a2 = new LinkPoint { X = from.X + fromNormal.X * stub, Y = from.Y + fromNormal.Y * stub };
            var b2 = new LinkPoint { X = to.X + toNormal.X * stub, Y = to.Y + toNormal.Y * stub };

            // Candidate mid-lines: the midpoint, plus every NEARBY box's inflated edges — the edges are
            // what make a path skirt a box. Only boxes overlapping the a2..b2 box contribute, which keeps
            // the candidate set near 16 instead of 4N; nothing ever crosses a box undetected. It DOES cost completeness, though: candidates travel
            // outside this box, and a box out there contributes no mid-line to route around, so a clean
            // route that exists can be missed — acceptable under spec O9 (degrade to a visible artifact the
            // user can drag out of), and the price of per-frame cost. Build re-runs every frame during a drag, and drawing
            // mid-lines from all N boxes costs ~1M slab tests a frame at dungeon scale.
            float loX = Math.Min(a2.X, b2.X), hiX = Math.Max(a2.X, b2.X);
            float loY = Math.Min(a2.Y, b2.Y), hiY = Math.Max(a2.Y, b2.Y);

            var nearby = new List<LinkNode>();
            foreach (var n in obstacles)
            {
                float hw = n.W * 0.5f + clearanceTiles, hh = n.H * 0.5f + clearanceTiles;
                if (n.CX + hw < loX || n.CX - hw > hiX) continue;
                if (n.CY + hh < loY || n.CY - hh > hiY) continue;
                nearby.Add(n);
            }
            nearby.Sort((p, q) => p.Id.CompareTo(q.Id));   // generation order never depends on list order

            var columns = new List<float> { (a2.X + b2.X) * 0.5f };
            var rows = new List<float> { (a2.Y + b2.Y) * 0.5f };
            foreach (var n in nearby)
            {
                float hw = n.W * 0.5f + clearanceTiles, hh = n.H * 0.5f + clearanceTiles;
                AddDistinct(columns, n.CX - hw);
                AddDistinct(columns, n.CX + hw);
                AddDistinct(rows, n.CY - hh);
                AddDistinct(rows, n.CY + hh);
            }

            var candidates = new List<List<LinkPoint>>();
            if (Math.Abs(a2.Y - b2.Y) <= TouchEps) candidates.Add(new List<LinkPoint> { a2, b2 });
            if (Math.Abs(a2.X - b2.X) <= TouchEps) candidates.Add(new List<LinkPoint> { a2, b2 });
            candidates.Add(new List<LinkPoint> { a2, new LinkPoint { X = b2.X, Y = a2.Y }, b2 });   // HV
            candidates.Add(new List<LinkPoint> { a2, new LinkPoint { X = a2.X, Y = b2.Y }, b2 });   // VH
            foreach (float c in columns)
                candidates.Add(new List<LinkPoint>
                { a2, new LinkPoint { X = c, Y = a2.Y }, new LinkPoint { X = c, Y = b2.Y }, b2 });  // HVH
            foreach (float r in rows)
                candidates.Add(new List<LinkPoint>
                { a2, new LinkPoint { X = a2.X, Y = r }, new LinkPoint { X = b2.X, Y = r }, b2 });  // VHV

            List<LinkPoint> best = null;
            int bestDirty = int.MaxValue, bestBends = int.MaxValue;
            float bestLen = float.MaxValue;

            foreach (var mid in candidates)
            {
                var path = new List<LinkPoint>(mid.Count + 2);
                path.Add(from);
                path.AddRange(mid);
                path.Add(to);
                Simplify(path);
                if (path.Count < 2) path.Add(to);   // coincident from/to: keep the two-point contract

                int dirty = CountDirtyLegs(path, obstacles, clearanceTiles);
                int bends = path.Count - 2;
                if (bends < 0) bends = 0;
                float len = PathLength(path);

                // Strictly better only — so on a tie the EARLIER candidate stands, which is the
                // "lower candidate index" tie-break. The length epsilon is the anti-flicker guard: a room
                // moving by a hair must not swap two paths that are the same length.
                bool better = dirty < bestDirty
                    || (dirty == bestDirty && bends < bestBends)
                    || (dirty == bestDirty && bends == bestBends && len < bestLen - 1e-4f);
                if (better) { best = path; bestDirty = dirty; bestBends = bends; bestLen = len; }
            }

            return best ?? new List<LinkPoint> { from, to };   // unreachable: HV and VH are always offered
        }

        static void AddDistinct(List<float> xs, float v)
        {
            foreach (float x in xs) if (Math.Abs(x - v) <= TouchEps) return;
            xs.Add(v);
        }

        /// <summary>Drop points the path runs STRAIGHT THROUGH, and exact duplicates. A point goes only
        /// when its two legs are collinear AND point the same way — never when the path doubles back.
        ///
        /// The same-direction half is load-bearing, not tidiness, and the failure it prevents is silent.
        /// Take an east-facing door whose target lies west: the stub runs east, the next leg runs back west
        /// across the room, and all three points share a Y. Merge them and they become ONE leg starting at
        /// the door — and a leg starting inside a box is exempt from that box (spec O8), so the leg that
        /// should have been caught crossing its own room inherits the stub's exemption, scores zero
        /// crossings, wins on bends, and ships a corridor drawn across its own room. Unmerged, the
        /// doubling-back leg starts at the stub's end, which spec O5 keeps strictly OUTSIDE the box, so it
        /// is counted and the candidate loses.</summary>
        static void Simplify(List<LinkPoint> path)
        {
            for (int i = path.Count - 1; i >= 1; i--)
                if (Math.Abs(path[i].X - path[i - 1].X) <= TouchEps &&
                    Math.Abs(path[i].Y - path[i - 1].Y) <= TouchEps)
                    path.RemoveAt(i);

            // Removing one point can make its neighbour removable, so sweep until nothing changes rather
            // than trusting a single pass to catch every cascade.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = path.Count - 2; i >= 1; i--)
                {
                    float ux = path[i].X - path[i - 1].X, uy = path[i].Y - path[i - 1].Y;
                    float vx = path[i + 1].X - path[i].X, vy = path[i + 1].Y - path[i].Y;
                    if (Math.Abs(ux * vy - uy * vx) <= TouchEps && ux * vx + uy * vy > 0f)
                    { path.RemoveAt(i); changed = true; }
                }
            }
        }

        /// <summary>How many LEGS hit at least one box. A leg cutting three boxes counts ONCE — what
        /// matters is how many legs are dirty, not how many (leg, box) pairs exist; per-pair counting would
        /// rank one leg through three rooms as worse than three legs through one room each, a judgement we
        /// do not want to make.
        ///
        /// A box containing the leg's START or END is skipped (spec O8). Both halves are needed: every path
        /// begins at a door on its own box's boundary AND ends at one, and a fork taps a point that is
        /// frequently another box's door. Without the skip no candidate could ever be clean.</summary>
        static int CountDirtyLegs(List<LinkPoint> path, IReadOnlyList<LinkNode> obstacles, float clearance)
        {
            int dirty = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var p = path[i];
                var q = path[i + 1];
                for (int o = 0; o < obstacles.Count; o++)
                {
                    var n = obstacles[o];
                    if (PointInInflatedRect(p, n, clearance) || PointInInflatedRect(q, n, clearance)) continue;
                    if (SegmentHitsInflatedRect(p, q, n, clearance, out _)) { dirty++; break; }
                }
            }
            return dirty;
        }

        static float PathLength(List<LinkPoint> path)
        {
            float sum = 0f;
            for (int i = 0; i < path.Count - 1; i++) sum += Dist(path[i], path[i + 1]);
            return sum;
        }

        /// <summary>Half-extents of a box's inflated rect, shrunk by TouchEps. The hit test and the
        /// containment test MUST use the same rect: they disagree otherwise about a point exactly ON the
        /// boundary, which is precisely where a stub's turn point lands when stub == clearance. One
        /// function, so that agreement is structural rather than a promise two comments make to each
        /// other.</summary>
        static void ShrunkHalfExtents(LinkNode n, float clearance, out float hw, out float hh)
        {
            hw = Math.Max(n.W * 0.5f + clearance - TouchEps, 0f);
            hh = Math.Max(n.H * 0.5f + clearance - TouchEps, 0f);
        }

        /// <summary>Is the point inside the box's inflated rect? Shrunk by the same TouchEps as the hit
        /// test so the two agree about the boundary. That agreement is load-bearing: a point exactly ON the
        /// inflated edge — where a stub's turn lands when stub == clearance — reads as OUTSIDE, so the box
        /// stays a real obstacle for the leg that turns there (spec O5/O7). A door, sitting `clearance`
        /// deep inside, is still contained, which is what exempts a stub from its own room (spec O8).</summary>
        static bool PointInInflatedRect(LinkPoint p, LinkNode n, float clearance)
        {
            ShrunkHalfExtents(n, clearance, out float hw, out float hh);
            return p.X >= n.CX - hw && p.X <= n.CX + hw && p.Y >= n.CY - hh && p.Y <= n.CY + hh;
        }

        /// <summary>Segment × inflated-AABB by the slab method. `tEntry` is the entry parameter along
        /// p→q, clamped to [0,1]. False when the segment misses the box or only TOUCHES its boundary.
        ///
        /// The boundary-touch exclusion (the TouchEps shrink) is load-bearing, not a nicety. An orthogonal
        /// leg routed to skirt a box runs exactly along that box's inflated edge, at precisely the
        /// clearance we asked for; without the shrink it would report itself as a crossing and no candidate
        /// path could ever be clean. Shares the shrunk rect with PointInInflatedRect via ShrunkHalfExtents
        /// so the hit test and the containment test agree about a point on the boundary.</summary>
        static bool SegmentHitsInflatedRect(LinkPoint p, LinkPoint q, LinkNode n, float clearance, out float tEntry)
        {
            tEntry = 0f;
            ShrunkHalfExtents(n, clearance, out float hw, out float hh);
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

        /// <summary>Closest point to `target` on any LEG of any already-built polyline: perpendicular
        /// projection onto each leg, clamped to its endpoints. Whole legs are candidates, not just their
        /// door ends — a target near a trunk's far end should tap in there rather than walk back to the
        /// wall. `built` is never empty here: a fork only happens once the wall's doors are all placed.</summary>
        static LinkPoint NearestPointOnBuilt(List<(List<LinkPoint> poly, int edgeIndex)> built, LinkPoint target)
        {
            var best = built[0].poly[0];
            float bestD2 = float.MaxValue;
            foreach (var b in built)
                for (int i = 0; i < b.poly.Count - 1; i++)
                {
                    var p = ClosestOnSegment(b.poly[i], b.poly[i + 1], target);
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
