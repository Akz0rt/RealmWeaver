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

        /// <summary>A\* pays this many tiles for each 90° turn, so clean routes come out straight and tidy
        /// instead of staircased. TUNABLE — the user eyeballs it.</summary>
        public const float TurnPenalty = 2f;

        /// <summary>A\* pays this many tiles for each already-routed corridor segment a leg intersects, so
        /// it prefers routes that cross other corridors less — but crosses anyway when the clear detour
        /// costs more than this. Rooms, by contrast, are never crossed. TUNABLE.</summary>
        public const float CorridorCrossPenalty = 8f;

        /// <summary>Route SHORT links first so main corridors route around the little stubs rather than
        /// the reverse. TUNABLE — flip and re-Build to compare.</summary>
        public const bool ShortLinksFirst = true;

        /// <summary>Fast = the L/Z scorer (cheap, room-aware only, may cross) for live drag; Clean = A\*
        /// (guaranteed clean, corridor-aware) for a settled layout. The controller picks per frame.</summary>
        public enum RoutingMode { Fast, Clean }

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

        public static LinkGeometry Build(IReadOnlyList<LinkNode> nodes, IReadOnlyList<LinkEdge> edges,
                                         RoutingMode mode = RoutingMode.Clean)
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
            //
            // `forkOrder` records the edge index of each fork in the order pass B RESOLVES it — nearest
            // target first, per wall. The CLEAN emit must route forks in THIS order, not short-first: a
            // recursive fork (a 4th corridor tapping a 3rd's branch) taps geometry that only exists once the
            // branch it taps is drawn, so the branch must be emitted first.
            var forkOrder = new List<int>();
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
                        forkOrder.Add(at.EdgeIndex);   // recursion-safe order for the CLEAN emit
                        built.Add((OrthogonalRoute(
                            fork, default,                                     // a fork has no wall: no normal
                            target, NormalOf(wallOf, at.EdgeIndex, at.OtherId),
                            nodes), at.EdgeIndex));
                    }
                }
            }

            // Route each link with both ends resolved. FAST reuses the L/Z scorer (drag-time); CLEAN uses
            // A*, in a deterministic SHORT-FIRST order, each routed corridor becoming soft cost for the
            // ones after it (spec A4). Order matters only for CLEAN — Fast ignores occupancy.
            var order = new List<int>();
            for (int i = 0; i < edges.Count; i++)
                if (endpoint.ContainsKey((i, edges[i].A)) && endpoint.ContainsKey((i, edges[i].B))) order.Add(i);

            if (mode == RoutingMode.Clean)
                order.Sort((i, j) =>
                {
                    float di = Dist(endpoint[(i, edges[i].A)], endpoint[(i, edges[i].B)]);
                    float dj = Dist(endpoint[(j, edges[j].A)], endpoint[(j, edges[j].B)]);
                    int c = ShortLinksFirst ? di.CompareTo(dj) : dj.CompareTo(di);
                    return c != 0 ? c : i.CompareTo(j);   // total-order tie-break → deterministic (List.Sort is not stable)
                });

            var occupancy = new List<(LinkPoint a, LinkPoint b)>();

            void Emit(int edge, List<LinkPoint> path)
            {
                for (int k = 0; k < path.Count - 1; k++)
                {
                    g.Segments.Add(new LinkSegment { A = path[k], B = path[k + 1], EdgeIndex = edge });
                    if (mode == RoutingMode.Clean) occupancy.Add((path[k], path[k + 1]));
                }
            }

            if (mode == RoutingMode.Fast)
            {
                // Drag-time: the cheap L/Z scorer, one pass, corridor-blind. Pass B's fork points already
                // lie on the FAST trunks (both use OrthogonalRoute), so nothing re-taps.
                foreach (int i in order)
                {
                    var e = edges[i];
                    Emit(i, OrthogonalRoute(endpoint[(i, e.A)], NormalOf(wallOf, i, e.A),
                                            endpoint[(i, e.B)], NormalOf(wallOf, i, e.B), nodes));
                }
                return g;
            }

            // CLEAN (spec A8). A* draws a trunk on a DIFFERENT path than pass B's cheap OrthogonalRoute
            // trunk, so a fork point tapped on the cheap trunk floats off the drawn geometry — the corridor
            // dangles in the void. Route the DOOR corridors first (both ends are room doors, always on a
            // wall, so they never dangle), accumulating occupancy; then route the FORK corridors, RE-TAPPING
            // each fork end onto the nearest point of the geometry actually drawn so it connects. The
            // re-tapped points, not pass B's, are the real junctions.
            bool IsFork(int edge, int node) => !wallOf.ContainsKey((edge, node));

            g.Forks.Clear();   // pass B's cheap-trunk forks are superseded by the re-tapped ones below

            // Route with A* at full clearance; if that had to cross a room FOOTPRINT — a door jammed against
            // a tight neighbour, where no clearance-respecting path out exists — retry with ZERO clearance
            // so the corridor hugs walls and routes around the footprints instead. Clearance is thus a
            // PREFERENCE, not a wall: kept when there's room, given up (never the footprint) when packed.
            // This is the "find the nearest gap it fits through" behaviour rather than crossing the room.
            List<LinkPoint> RouteClean(LinkPoint pa, LinkPoint na, LinkPoint pb, LinkPoint nb, int exA, int exB)
            {
                var p = AStarRoute(pa, na, pb, nb, nodes, occupancy);
                if (CrossesFootprint(p, nodes, exA, exB))
                    // Zero clearance AND zero stub: start right at the door (which sits on its own wall,
                    // outside a jammed neighbour's footprint) so A* can turn immediately and route around,
                    // instead of the fixed stub driving it straight into the neighbour first.
                    p = AStarRoute(pa, na, pb, nb, nodes, occupancy, 0f, 0f);
                return p;
            }

            // Doors first (short-first, for corridor avoidance), so every trunk is in `occupancy` before a
            // fork re-taps onto it.
            foreach (int i in order)
            {
                if (IsFork(i, edges[i].A) || IsFork(i, edges[i].B)) continue;   // a fork edge — routed below
                var e = edges[i];
                Emit(i, RouteClean(endpoint[(i, e.A)], NormalOf(wallOf, i, e.A),
                                   endpoint[(i, e.B)], NormalOf(wallOf, i, e.B), e.A, e.B));
            }

            // Then forks, in pass B's RESOLUTION order (not short-first): a recursive fork taps a shallower
            // fork's branch, so that branch must already be in `occupancy`. Each fork end re-taps onto the
            // nearest drawn segment so it connects instead of dangling in the void (spec A8).
            var orderSet = new HashSet<int>(order);
            var routedForks = new HashSet<int>();
            foreach (int i in forkOrder)
            {
                if (!orderSet.Contains(i) || !routedForks.Add(i)) continue;   // dangling, or already routed
                var e = edges[i];
                var pa = endpoint[(i, e.A)];
                var pb = endpoint[(i, e.B)];
                if (IsFork(i, e.A)) { pa = NearestOnOccupancy(pa, occupancy); g.Forks.Add(pa); }
                if (IsFork(i, e.B)) { pb = NearestOnOccupancy(pb, occupancy); g.Forks.Add(pb); }
                Emit(i, RouteClean(pa, NormalOf(wallOf, i, e.A), pb, NormalOf(wallOf, i, e.B), e.A, e.B));
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
            // leg turning there (spec O8), and the link may then run straight back across its own room — so
            // the stub MUST clear the clearance ring. Clamp it up when the caller passes less.
            float stub = stubTiles > clearanceTiles ? stubTiles : clearanceTiles + 1f;
            // ...but cap it so two stubs on a short link don't overshoot each other and zigzag out and back
            // (a corridor poking into the void on a tightly packed floor): half the door-to-door distance,
            // and half the FACING distance (the separation projected onto each normal) when the doors face
            // each other — offset doors overshoot on their shared axis even when the straight line is long.
            stub = Math.Min(stub, Dist(from, to) * 0.5f);
            float projA = fromNormal.X * (to.X - from.X) + fromNormal.Y * (to.Y - from.Y);
            float projB = toNormal.X * (from.X - to.X) + toNormal.Y * (from.Y - to.Y);
            if (projA > 0f && projB > 0f) stub = Math.Min(stub, Math.Min(projA, projB) * 0.5f);

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

        /// <summary>An orthogonal path from `from` to `to` that NEVER crosses a room and minimizes crossing
        /// already-routed corridors (`occupancy`). A\* over a coordinate-compressed (Hanan) grid whose lines
        /// are every room's inflated edges plus the two endpoints — that grid provably contains a shortest
        /// rectilinear obstacle-avoiding path. Rooms are hard (blocked edges); corridors are soft
        /// (corridorPenalty per intersected segment); a turn costs turnPenalty so routes stay tidy.
        ///
        /// `fromNormal`/`toNormal` are unit wall normals; pass default(LinkPoint) for a fork end (no wall,
        /// no stub). An occupancy segment that CONTAINS `from` or `to` is exempt from the penalty — a fork
        /// taps its trunk there and must be free to branch off it.
        ///
        /// Never returns null: when no grid path exists (a boxed-in door) it falls back to OrthogonalRoute,
        /// a visible artifact the user can drag out of (spec A7). Deterministic: the lattice is sorted, the
        /// heap tie-breaks on state index, and nothing reads input list order.</summary>
        public static List<LinkPoint> AStarRoute(
            LinkPoint from, LinkPoint fromNormal, LinkPoint to, LinkPoint toNormal,
            IReadOnlyList<LinkNode> rooms, IReadOnlyList<(LinkPoint a, LinkPoint b)> occupancy,
            float clearanceTiles = ClearanceTiles, float stubTiles = StubTiles,
            float turnPenalty = TurnPenalty, float corridorPenalty = CorridorCrossPenalty)
        {
            if (rooms == null) rooms = System.Array.Empty<LinkNode>();

            // Stub length: 0 when the caller asks for none (the zero-clearance re-route, which must start
            // right at the door), else at least clear the clearance ring (spec O5).
            float stub = stubTiles <= 0f ? 0f : (stubTiles > clearanceTiles ? stubTiles : clearanceTiles + 1f);
            // Don't let the two door stubs OVERSHOOT each other on a short link: when the rooms are close
            // (a tightly packed floor), a full-length stub from each door sails past the other, and the
            // path zigzags out and doubles back — a corridor that pokes into the void and returns. Cap by
            // half the door-to-door distance, AND — because offset doors overshoot on their SHARED axis
            // even when the straight-line distance looks big enough — by half the FACING distance (the door
            // separation projected onto each normal), whenever the doors face each other.
            stub = Math.Min(stub, Dist(from, to) * 0.5f);
            float projA = fromNormal.X * (to.X - from.X) + fromNormal.Y * (to.Y - from.Y);
            float projB = toNormal.X * (from.X - to.X) + toNormal.Y * (from.Y - to.Y);
            if (projA > 0f && projB > 0f) stub = Math.Min(stub, Math.Min(projA, projB) * 0.5f);
            var a2 = new LinkPoint { X = from.X + fromNormal.X * stub, Y = from.Y + fromNormal.Y * stub };
            var b2 = new LinkPoint { X = to.X + toNormal.X * stub, Y = to.Y + toNormal.Y * stub };

            // Occupancy this link is allowed to touch for free: any segment through its own endpoints (its
            // trunk, for a fork). Filtered ONCE, not per edge.
            var occ = new List<(LinkPoint a, LinkPoint b)>();
            if (occupancy != null)
                foreach (var s in occupancy)
                    if (!PointOnSeg(from, s.a, s.b) && !PointOnSeg(to, s.a, s.b)) occ.Add(s);

            // ── the compressed grid ────────────────────────────────────────────────────────────────
            var xs = new List<float> { a2.X, b2.X };
            var ys = new List<float> { a2.Y, b2.Y };
            foreach (var n in rooms)
            {
                float hw = n.W * 0.5f + clearanceTiles, hh = n.H * 0.5f + clearanceTiles;
                AddDistinct(xs, n.CX - hw); AddDistinct(xs, n.CX + hw);
                AddDistinct(ys, n.CY - hh); AddDistinct(ys, n.CY + hh);
            }
            xs.Sort(); ys.Sort();
            int W = xs.Count, H = ys.Count;

            int sx = GridIndexOf(xs, a2.X), sy = GridIndexOf(ys, a2.Y);
            int gx = GridIndexOf(xs, b2.X), gy = GridIndexOf(ys, b2.Y);
            int startNode = sy * W + sx, goalNode = gy * W + gx;

            // ── A\* over states (node, incomingDir): dir 0=E 1=W 2=S 3=N, 4=none(start) ─────────────
            // g-scores and came-from are keyed by state = node*5 + dir. The heap tie-breaks on state so
            // equal-cost pops are deterministic regardless of push order.
            var gScore = new Dictionary<int, float>();
            var cameFrom = new Dictionary<int, int>();
            var closed = new HashSet<int>();
            var heap = new List<(float f, int state)>();

            void HeapPush(float f, int state)
            {
                heap.Add((f, state));
                int i = heap.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (heap[p].f < heap[i].f || (heap[p].f == heap[i].f && heap[p].state <= heap[i].state)) break;
                    var t = heap[p]; heap[p] = heap[i]; heap[i] = t; i = p;
                }
            }
            (float f, int state) HeapPop()
            {
                var top = heap[0];
                int last = heap.Count - 1;
                heap[0] = heap[last]; heap.RemoveAt(last);
                int i = 0, n = heap.Count;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, m = i;
                    if (l < n && (heap[l].f < heap[m].f || (heap[l].f == heap[m].f && heap[l].state < heap[m].state))) m = l;
                    if (r < n && (heap[r].f < heap[m].f || (heap[r].f == heap[m].f && heap[r].state < heap[m].state))) m = r;
                    if (m == i) break;
                    var t = heap[m]; heap[m] = heap[i]; heap[i] = t; i = m;
                }
                return top;
            }

            float H2Goal(int node)   // Manhattan heuristic — admissible (penalties only ever add)
            {
                int ix = node % W, iy = node / W;
                return Math.Abs(xs[ix] - b2.X) + Math.Abs(ys[iy] - b2.Y);
            }

            int startState = startNode * 5 + 4;
            gScore[startState] = 0f;
            HeapPush(H2Goal(startNode), startState);

            // dir deltas, indexed 0=E 1=W 2=S 3=N
            int[] ddx = { 1, -1, 0, 0 };
            int[] ddy = { 0, 0, 1, -1 };

            int goalState = -1;
            while (heap.Count > 0)
            {
                var (_, state) = HeapPop();
                if (closed.Contains(state)) continue;
                closed.Add(state);

                int node = state / 5, dir = state % 5;
                if (node == goalNode) { goalState = state; break; }

                int ix = node % W, iy = node / W;
                float gHere = gScore[state];
                var p = new LinkPoint { X = xs[ix], Y = ys[iy] };

                for (int d = 0; d < 4; d++)
                {
                    int nx = ix + ddx[d], ny = iy + ddy[d];
                    if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                    var q = new LinkPoint { X = xs[nx], Y = ys[ny] };

                    bool blocked = false;                      // rooms are hard
                    foreach (var rm in rooms)
                        if (SegmentHitsInflatedRect(p, q, rm, clearanceTiles, out _)) { blocked = true; break; }
                    if (blocked) continue;

                    float step = Dist(p, q);
                    if (dir != 4 && dir != d) step += turnPenalty;      // a turn costs
                    foreach (var s in occ)                              // corridors are soft
                        if (SegBBoxOverlap(p, q, s.a, s.b)) step += corridorPenalty;

                    int nNode = ny * W + nx, nState = nNode * 5 + d;
                    float tentative = gHere + step;
                    if (gScore.TryGetValue(nState, out float old) && tentative >= old) continue;
                    gScore[nState] = tentative;
                    cameFrom[nState] = state;
                    HeapPush(tentative + H2Goal(nNode), nState);
                }
            }

            if (goalState < 0)                                          // boxed in → spec A7 fallback
                return OrthogonalRoute(from, fromNormal, to, toNormal, rooms, clearanceTiles, stubTiles);

            // reconstruct lattice nodes start→goal, then wrap with the true endpoints and simplify
            var latticeNodes = new List<int>();
            int cur = goalState;
            latticeNodes.Add(cur / 5);
            while (cameFrom.TryGetValue(cur, out int prev)) { cur = prev; latticeNodes.Add(cur / 5); }
            latticeNodes.Reverse();

            var path = new List<LinkPoint>(latticeNodes.Count + 2);
            path.Add(from);
            foreach (int nd in latticeNodes)
                path.Add(new LinkPoint { X = xs[nd % W], Y = ys[nd / W] });
            path.Add(to);
            Simplify(path);
            if (path.Count < 2) path.Add(to);                          // coincident from/to guard
            return path;
        }

        /// <summary>Index in a sorted, epsilon-deduped coordinate list of the entry equal to `v` (which was
        /// added to the list, so it is present). Linear — the lists are ~2N long.</summary>
        static int GridIndexOf(List<float> sorted, float v)
        {
            for (int i = 0; i < sorted.Count; i++) if (Math.Abs(sorted[i] - v) <= 1e-4f) return i;
            return 0;   // unreachable: v was AddDistinct'd into the list
        }

        /// <summary>Does point `p` lie on the axis-aligned segment a→b (within epsilon)? Used to exempt a
        /// fork's own trunk from the corridor penalty.</summary>
        static bool PointOnSeg(LinkPoint p, LinkPoint a, LinkPoint b)
        {
            float minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
            float minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
            return p.X >= minX - 1e-4f && p.X <= maxX + 1e-4f && p.Y >= minY - 1e-4f && p.Y <= maxY + 1e-4f;
        }

        /// <summary>Do two AXIS-ALIGNED segments intersect? For axis-aligned segments this is exactly
        /// bounding-box overlap (a horizontal and a vertical overlap iff the vertical's x is in the
        /// horizontal's x-range and the horizontal's y is in the vertical's y-range; two collinear ones
        /// overlap iff their shared-axis intervals do). Counts a shared endpoint as an intersection — a
        /// slight over-penalty on T-junctions, which only nudges A\* toward fewer of them.</summary>
        static bool SegBBoxOverlap(LinkPoint a, LinkPoint b, LinkPoint c, LinkPoint d)
        {
            float aMinX = Math.Min(a.X, b.X), aMaxX = Math.Max(a.X, b.X);
            float aMinY = Math.Min(a.Y, b.Y), aMaxY = Math.Max(a.Y, b.Y);
            float bMinX = Math.Min(c.X, d.X), bMaxX = Math.Max(c.X, d.X);
            float bMinY = Math.Min(c.Y, d.Y), bMaxY = Math.Max(c.Y, d.Y);
            return aMinX <= bMaxX + 1e-4f && bMinX <= aMaxX + 1e-4f
                && aMinY <= bMaxY + 1e-4f && bMinY <= aMaxY + 1e-4f;
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

        /// <summary>Nearest point to `p` on any segment of the geometry drawn so far — used to RE-TAP a
        /// fork onto the A* trunk that was actually drawn (spec A8). A fork's pass-B point sat on the CHEAP
        /// OrthogonalRoute trunk; A* draws the trunk elsewhere, so without this the fork dangles in the
        /// void. Returns `p` unchanged when `built` is empty — safe, though a fork then has nothing to
        /// anchor to; in practice a fork's wall carries ≥2 door-trunks routed before it, so `built` is
        /// non-empty by the time any fork snaps.</summary>
        static LinkPoint NearestOnOccupancy(LinkPoint p, List<(LinkPoint a, LinkPoint b)> built)
        {
            var best = p;
            float bestD2 = float.MaxValue;
            foreach (var s in built)
            {
                var c = ClosestOnSegment(s.a, s.b, p);
                float dx = c.X - p.X, dy = c.Y - p.Y, d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = c; }
            }
            return best;
        }

        /// <summary>Does any leg of `path` cross a room FOOTPRINT (its raw rect, at clearance 0), other than
        /// the two endpoint rooms the corridor connects? Used to detect a full-clearance route that had to
        /// fall through a room because a door was jammed against a tight neighbour — the caller then re-routes
        /// at zero clearance to route around the footprint instead. Grazing a wall (a leg on the raw edge) is
        /// NOT a crossing (SegmentHitsInflatedRect's TouchEps shrink), only entering the interior is.</summary>
        static bool CrossesFootprint(List<LinkPoint> path, IReadOnlyList<LinkNode> rooms, int exA, int exB)
        {
            if (path == null) return false;
            for (int i = 0; i < path.Count - 1; i++)
                foreach (var n in rooms)
                {
                    if (n.Id == exA || n.Id == exB) continue;
                    if (SegmentHitsInflatedRect(path[i], path[i + 1], n, 0f, out _)) return true;
                }
            return false;
        }
    }
}
