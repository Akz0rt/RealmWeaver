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

            // Each end's resolved polyline (door/fork → far end), already bent around obstacles.
            var polyOf = new Dictionary<(int edge, int node), List<LinkPoint>>();

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

                    // Runs for EVERY wall, not only overfull ones. This pass resolves each link's
                    // polyline and the emission loop draws nothing without it, so gating it on "the wall
                    // is full" silently deletes every corridor on a wall carrying one or two links — which
                    // is nearly all of them. No guard is needed: when the wall is not full,
                    // doorCount == onWall.Count and the fork loop below simply runs zero times.
                    //
                    // `built` holds BENT polylines, not straight segments — that is spec C2. A trunk is
                    // detoured BEFORE it enters the fork search's candidate set, so a fork taps geometry
                    // that will actually be drawn. Bending trunks afterwards would leave every fork hanging
                    // beside its trunk instead of on it.
                    var built = new List<(List<LinkPoint> poly, int edgeIndex)>();
                    for (int i = 0; i < doorCount; i++)
                    {
                        var at = onWall[i];
                        var poly = new List<LinkPoint> { endpoint[(at.EdgeIndex, nodeId)], FarEnd(byId, endpoint, at) };
                        DetourAround(poly, ObstaclesFor(nodes, at.SelfId, at.OtherId));
                        built.Add((poly, at.EdgeIndex));
                    }

                    for (int k = doorCount; k < onWall.Count; k++)
                    {
                        var at = onWall[k];
                        var target = FarEnd(byId, endpoint, at);
                        var fork = NearestPointOnBuilt(built, target);
                        g.Forks.Add(fork);
                        endpoint[(at.EdgeIndex, nodeId)] = fork;
                        var poly = new List<LinkPoint> { fork, target };
                        DetourAround(poly, ObstaclesFor(nodes, at.SelfId, at.OtherId));
                        built.Add((poly, at.EdgeIndex));
                    }

                    // Stash each resolved polyline so the emission loop below can lay out its legs.
                    foreach (var b in built) polyOf[(b.edgeIndex, nodeId)] = b.poly;
                }
            }

            // Emit one segment per LEG of each edge's polyline. A link no longer owns exactly one segment:
            // the detour splits it wherever it bends around a box.
            //
            // Take the A-end's polyline, but FORCE its last point to B's RESOLVED attachment. Pass B built
            // that polyline toward FarEnd, which falls back to the far box's CENTRE when the far end is a
            // fork it had not reached yet — emitting that verbatim would draw a link ending inside a box,
            // the very defect the two-pass split exists to prevent. Pass B's own bends are kept (they are
            // what the forks tapped) and the corrected leg is re-checked below.
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!endpoint.TryGetValue((i, e.A), out _)) continue;
                if (!endpoint.TryGetValue((i, e.B), out var pb)) continue;
                if (!polyOf.TryGetValue((i, e.A), out var poly) || poly.Count < 2) continue;

                var final = new List<LinkPoint>(poly);
                final[final.Count - 1] = pb;                 // the far end's real door or fork

                // Moving the terminus can invalidate the last leg, so re-check it. Pass B bent this
                // polyline toward FarEnd, which falls back to the far box's CENTRE when that end is a fork
                // this pass had not reached yet; snapping the end to the real fork point can push that leg
                // through a box the detour never saw. A no-op in the common case (the far end earned a
                // door, so FarEnd == pb and the polyline is already clean).
                DetourAround(final, ObstaclesFor(nodes, e.A, e.B));

                for (int k = 0; k < final.Count - 1; k++)
                    g.Segments.Add(new LinkSegment { A = final[k], B = final[k + 1], EdgeIndex = i });
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
                    if (tEntry < bestT - 1e-6f ||
                        (Math.Abs(tEntry - bestT) <= 1e-6f && (best < 0 || obstacles[o].Id < obstacles[best].Id)))
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
        /// the path resolves the same way every frame instead of by float noise.
        /// An empty side means the line only touches the boundary — see the give-up below.</summary>
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

            // Unreachable: a line through the box's INTERIOR always leaves at least one corner strictly on
            // each side, and SegmentHitsInflatedRect's TouchEps shrink means we are only called for lines
            // that DO cross the interior. Kept as an honest give-up (spec C6 — degrade to a visible
            // artifact, never to garbage). The old code returned the non-empty side here: a chain visiting
            // ALL FOUR corners, which necessarily cuts straight through the box it was meant to avoid.
            if (sideA.Count == 0 || sideB.Count == 0) return new List<LinkPoint>();

            var chainA = OrderAlong(p, q, corners, sideA);
            var chainB = OrderAlong(p, q, corners, sideB);

            float costA = ChainCost(p, q, chainA);
            float costB = ChainCost(p, q, chainB);

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
        /// The boundary-touch exclusion is load-bearing, not a nicety. Every bend this module inserts
        /// lands ON a blocker's inflated corner, so the new leg runs exactly along that blocker's inflated
        /// edge — at precisely the clearance we asked for, needing no further detour. Two rooms of the
        /// same height side by side (ordinary, not exotic) would otherwise make the SECOND report a
        /// spurious hit against a leg collinear with its edge; and a collinear leg puts all four corners
        /// on one side of itself, which ChooseDetourChain cannot split. Shrinking the test rect by
        /// TouchEps dissolves that at the root.</summary>
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

        /// <summary>Every box except the link's own two ends (spec C3) — a link starts and ends at their
        /// doors, which sit ON their boundaries, so they can never be things to route around.</summary>
        static List<LinkNode> ObstaclesFor(IReadOnlyList<LinkNode> nodes, int selfId, int otherId)
        {
            var list = new List<LinkNode>(nodes.Count);
            foreach (var n in nodes) if (n.Id != selfId && n.Id != otherId) list.Add(n);
            return list;
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
