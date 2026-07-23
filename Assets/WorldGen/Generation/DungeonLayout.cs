using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure, headless layout services over an InteriorFloor graph: cascade non-overlap separation
    /// (Separate) and derived corridor-crossing junctions (BuildRenderGraph, Task 3). Positions are
    /// normalized 0..1; sizes are in tiles; all math runs in TILE space via TilesPerAxis. No Unity types.</summary>
    public static class DungeonLayout
    {
        // Normalized 0..1 spans this many tiles (bridges pos↔size units). Raised 48→128 once room
        // footprints grew (Normal up to 8, Boss up to 14): at 48, six BFS layers of 8-tile rooms plus
        // gaps needed 63 tiles, and the generator's Clamp01 silently stacked the overflow at the field
        // edge — the opposite of compaction. This costs nothing visually: DungeonProjection.Fit scales by
        // the level's OCCUPIED bounds (ContentBoundsTiles), so the field size never reaches the renderer.
        // It is pure coordinate headroom.
        public const int TilesPerAxis = 128;

        /// <summary>Longest a corridor may stretch, edge-to-edge in tiles, before its rooms start pulling
        /// each other along (EnforceCorridorLeash). DungeonGraphGenerator.Generate runs the leash itself
        /// as its last step, so a freshly generated floor satisfies this BY CONSTRUCTION and the DM's
        /// first drag never snaps it together. TUNABLE — the user eyeballs the feel, as with
        /// CascadeSmoothTime.</summary>
        public const float MaxCorridorTiles = 8f;

        static float ToTile(float norm) => norm * TilesPerAxis;
        static float ToNorm(float tile) => tile / TilesPerAxis;

        // RoomLinkGeometry works in TILE space; LayoutPoint (and Room.X/Y) are normalized 0..1.
        static LayoutPoint ToLayout(LinkPoint p) => new LayoutPoint { X = ToNorm(p.X), Y = ToNorm(p.Y) };

        /// <summary>Push overlapping room footprints apart (cascade) until none overlap with a minGapTiles
        /// clearance, or maxIterations is reached. Deterministic. Mutates Room.X/Y (kept in [0,1]).</summary>
        public static void Separate(InteriorFloor lvl, float minGapTiles = 0.1f, int maxIterations = 40)
        {
            if (lvl == null || lvl.Rooms.Count < 2) return;
            var rooms = lvl.Rooms;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool anyOverlap = false;
                for (int i = 0; i < rooms.Count; i++)
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        var a = rooms[i]; var b = rooms[j];
                        float ax = ToTile(a.X), ay = ToTile(a.Y), bx = ToTile(b.X), by = ToTile(b.Y);
                        // EffectiveSize, not raw SizeW/H: it substitutes the type default for an unset
                        // (<=0) footprint and clamps out-of-range serialized data. Reading the raw fields
                        // made Separate disagree with the leash and every renderer about how big a legacy
                        // room is.
                        var (aw, ah) = DungeonProjection.EffectiveSize(a);
                        var (bw, bh) = DungeonProjection.EffectiveSize(b);
                        float halfW = (aw + bw) * 0.5f + minGapTiles;   // min center distance on X
                        float halfH = (ah + bh) * 0.5f + minGapTiles;   // …and Y for no overlap
                        float dx = bx - ax, dy = by - ay;
                        float overlapX = halfW - Math.Abs(dx);
                        float overlapY = halfH - Math.Abs(dy);
                        if (overlapX <= 0f || overlapY <= 0f) continue;           // not overlapping
                        anyOverlap = true;
                        // Push apart along the axis of LEAST penetration (smallest shove), split evenly.
                        if (overlapX < overlapY)
                        {
                            float push = (overlapX * 0.5f + 0.01f) * (dx >= 0f ? 1f : -1f);
                            ax -= push; bx += push;
                        }
                        else
                        {
                            float push = (overlapY * 0.5f + 0.01f) * (dy >= 0f ? 1f : -1f);
                            ay -= push; by += push;
                        }
                        a.X = Clamp01(ToNorm(ax)); a.Y = Clamp01(ToNorm(ay));
                        b.X = Clamp01(ToNorm(bx)); b.Y = Clamp01(ToNorm(by));
                    }
                if (!anyOverlap) break;
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Pull linked rooms so no corridor stretches past maxTiles edge-to-edge, as if the floor
        /// were stitched together with threads. `anchorRoomId` is the room the DM is dragging: it NEVER
        /// moves, everything else gives. The pull propagates outward by graph distance from the anchor, so
        /// a room that gets pulled then pulls its OWN neighbours — drag far enough and the whole (always
        /// connected) floor follows. That is the intended feel, not a runaway.
        ///
        /// Cannot fight Separate: Separate only acts on OVERLAP (edge gap &lt; minGapTiles = 0.1), this only
        /// on STRETCH (edge gap &gt; 12). The regimes never meet, so they cannot oscillate.
        ///
        /// Uses the same Chebyshev edge gap as Separate — a second metric here would make the leash and the
        /// cascade disagree about what "touching" means. Deterministic; mutates Room.X/Y (kept in [0,1]).</summary>
        public static void EnforceCorridorLeash(InteriorFloor lvl, int anchorRoomId,
            float maxTiles = MaxCorridorTiles, int maxIterations = 24)
        {
            if (lvl == null || lvl.Rooms.Count < 2 || lvl.Links.Count == 0) return;

            // Graph distance from the anchor decides who yields: on each corridor the room FARTHER from
            // the anchor is the one that moves. Rooms unreachable from the anchor (orphans, or a separate
            // component) get int.MaxValue and are never pulled — nothing stitches them to the anchor.
            // A caller with no drag in progress (the cascade, or generation) passes an id that isn't a
            // room. Fall back to the lowest room id so the pass still does something: with no reachable
            // anchor every room would be at int.MaxValue and the leash would silently no-op.
            int anchor = lvl.GetRoom(anchorRoomId) != null ? anchorRoomId : LowestRoomId(lvl);
            var dist = BfsFromAnchor(lvl, anchor);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool anyPulled = false;
                foreach (var c in lvl.Links)
                {
                    var a = lvl.GetRoom(c.RoomA);
                    var b = lvl.GetRoom(c.RoomB);
                    if (a == null || b == null) continue;

                    int da = dist.TryGetValue(a.Id, out var xa) ? xa : int.MaxValue;
                    int db = dist.TryGetValue(b.Id, out var xb) ? xb : int.MaxValue;
                    if (da == int.MaxValue && db == int.MaxValue) continue;   // neither side reachable

                    // The nearer room holds; the farther one yields. Equal distance (a loop edge) — the
                    // lower id holds, purely so the result is deterministic.
                    Room fixedRoom, movingRoom;
                    if (da < db || (da == db && a.Id < b.Id)) { fixedRoom = a; movingRoom = b; }
                    else { fixedRoom = b; movingRoom = a; }
                    if (movingRoom.Id == anchor) continue;                    // the anchor never yields

                    float gap = EdgeGapTiles(fixedRoom, movingRoom);
                    // Epsilon matters: the steady state of a sustained drag parks corridors at exactly
                    // gap == maxTiles, and the float round-trip through ToNorm/ToTile can leave it a hair
                    // over. Without slack here anyPulled would stay true forever and the loop would burn
                    // all maxIterations every frame instead of exiting after ~2.
                    if (gap <= maxTiles + 1e-3f) continue;                    // slack — leave it alone
                    anyPulled = true;

                    float excess = gap - maxTiles;
                    float fx = ToTile(fixedRoom.X), fy = ToTile(fixedRoom.Y);
                    float mx = ToTile(movingRoom.X), my = ToTile(movingRoom.Y);
                    float dx = fx - mx, dy = fy - my;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);   // > maxTiles: gap > maxTiles implies it

                    // Step the moving room toward the fixed one by the excess, along the centre line.
                    // One step may not fully close a Chebyshev gap on a diagonal; the iteration converges.
                    mx += dx / len * excess;
                    my += dy / len * excess;
                    movingRoom.X = Clamp01(ToNorm(mx));
                    movingRoom.Y = Clamp01(ToNorm(my));
                }
                if (!anyPulled) break;
            }
        }

        static int LowestRoomId(InteriorFloor lvl)
        {
            int best = 0;
            foreach (var r in lvl.Rooms) if (best == 0 || r.Id < best) best = r.Id;
            return best;
        }

        static Dictionary<int, int> BfsFromAnchor(InteriorFloor lvl, int anchorRoomId)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new List<int>();
            foreach (var c in lvl.Links)
            {
                if (adj.ContainsKey(c.RoomA) && adj.ContainsKey(c.RoomB))
                { adj[c.RoomA].Add(c.RoomB); adj[c.RoomB].Add(c.RoomA); }
            }

            var dist = new Dictionary<int, int>();
            if (!adj.ContainsKey(anchorRoomId)) return dist;
            var queue = new Queue<int>();
            dist[anchorRoomId] = 0;
            queue.Enqueue(anchorRoomId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in adj[cur])
                    if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; queue.Enqueue(nb); }
            }
            return dist;
        }

        /// <summary>Chebyshev edge gap in tiles between two footprints: centre distance minus both
        /// half-extents, on the axis of greatest separation. Negative = overlapping. Same measure
        /// Separate's overlap condition uses.</summary>
        static float EdgeGapTiles(Room a, Room b)
        {
            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float dx = Math.Abs(ToTile(b.X) - ToTile(a.X)) - (aw + bw) * 0.5f;
            float dy = Math.Abs(ToTile(b.Y) - ToTile(a.Y)) - (ah + bh) * 0.5f;
            return Math.Max(dx, dy);
        }

        /// <summary>Corridor rendering geometry with junctions resolved: each DM corridor is split at every
        /// point where it crosses another DM corridor, and a junction point is emitted at each crossing.
        /// DERIVED — not stored. Only DM corridors are crossed (no recursion on the split sub-segments).</summary>
        public static RenderGraph BuildRenderGraph(InteriorFloor lvl, RoomLinkGeometry.RoutingMode mode = RoomLinkGeometry.RoutingMode.Clean, bool settlementRoads = false)
        {
            var g = new RenderGraph();
            if (lvl == null) return g;
            var rooms = lvl.Rooms;

            // Resolve each corridor to a segment through RoomLinkGeometry: corridors now leave rooms via
            // DOOR points on the wall facing their target (max 2 per wall) and FORK off already-built
            // geometry past that limit — they no longer run centre-to-centre through the rooms they join.
            // The routing itself lives in RoomLinkGeometry, which knows nothing about dungeons so the same
            // math can serve building/city maps later; this method is just the adapter.
            var nodes = new List<LinkNode>(lvl.Rooms.Count);
            foreach (var r in lvl.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                nodes.Add(new LinkNode
                {
                    Id = r.Id,
                    CX = ToTile(r.X), CY = ToTile(r.Y),   // RoomLinkGeometry works in TILE space
                    W = w, H = h,
                });
            }
            var linkEdges = new List<LinkEdge>(lvl.Links.Count);
            foreach (var c in lvl.Links) linkEdges.Add(new LinkEdge { A = c.RoomA, B = c.RoomB });

            // Ц1.6: a settlement routes ROADS (SettlementRoads' fixed-grid A*) — RoomLinkGeometry's
            // Clean is non-scaling at town size (20–34 s @60) and Fast may cross houses. The flag comes
            // only from DungeonViewController (the seam that knows Kind); default false keeps dungeons,
            // buildings, battle-grid projection and the self-tests byte-identical.
            var routed = settlementRoads
                ? SettlementRoads.Build(nodes, linkEdges)
                : RoomLinkGeometry.Build(nodes, linkEdges, mode);
            foreach (var d in routed.Doors) g.Doors.Add(ToLayout(d));   // door points → renderer's wall gaps

            var segs = new List<(LayoutPoint a, LayoutPoint b)>();
            foreach (var s in routed.Segments)
                segs.Add((ToLayout(s.A), ToLayout(s.B)));

            // Dedup junction points within an epsilon (near-coincident crossings merge).
            var junctions = new List<LayoutPoint>();

            // A fork IS a junction — where a corridor taps into another. Seeding them here means they
            // render with the existing junction marker, and JunctionIndex's dedup keeps a fork that
            // happens to coincide with a crossing from being drawn twice.
            foreach (var f in routed.Forks) junctions.Add(ToLayout(f));

            int JunctionIndex(LayoutPoint p)
            {
                for (int k = 0; k < junctions.Count; k++)
                    if (Math.Abs(junctions[k].X - p.X) < 1e-4f && Math.Abs(junctions[k].Y - p.Y) < 1e-4f) return k;
                junctions.Add(p); return junctions.Count - 1;
            }

            // For each corridor, collect the crossing points with every OTHER corridor, sorted along it, and
            // split it into pieces. Crossings register junction points.
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                var cuts = new List<(float t, LayoutPoint p)>();
                for (int j = 0; j < segs.Count; j++)
                {
                    if (j == i) continue;
                    if (SegmentIntersect(s.a, s.b, segs[j].a, segs[j].b, out var ip, out float t))
                    {
                        JunctionIndex(ip);
                        cuts.Add((t, ip));
                    }
                }
                cuts.Sort((u, v) => u.t.CompareTo(v.t));
                var prev = s.a;
                foreach (var cut in cuts) { g.Segments.Add(new RenderSegment { A = prev, B = cut.p }); prev = cut.p; }
                g.Segments.Add(new RenderSegment { A = prev, B = s.b });
            }
            g.Junctions = junctions;
            return g;
        }

        /// <summary>Proper crossing of open segments p1p2 × p3p4 (shared endpoints / collinear touches don't
        /// count). Outputs the intersection point and its parameter t along p1p2 in (0,1).</summary>
        static bool SegmentIntersect(LayoutPoint p1, LayoutPoint p2, LayoutPoint p3, LayoutPoint p4, out LayoutPoint ip, out float t)
        {
            ip = default; t = 0f;
            float r1 = p2.X - p1.X, r2 = p2.Y - p1.Y;
            float s1 = p4.X - p3.X, s2 = p4.Y - p3.Y;
            float denom = r1 * s2 - r2 * s1;
            if (Math.Abs(denom) < 1e-7f) return false;   // parallel/collinear
            float tt = ((p3.X - p1.X) * s2 - (p3.Y - p1.Y) * s1) / denom;
            float uu = ((p3.X - p1.X) * r2 - (p3.Y - p1.Y) * r1) / denom;
            const float e = 1e-4f;
            if (tt <= e || tt >= 1f - e || uu <= e || uu >= 1f - e) return false;   // exclude endpoints
            ip = new LayoutPoint { X = p1.X + tt * r1, Y = p1.Y + tt * r2 };
            t = tt; return true;
        }
    }

    public struct LayoutPoint { public float X, Y; }
    public class RenderSegment { public LayoutPoint A, B; }
    public class RenderGraph
    {
        public System.Collections.Generic.List<LayoutPoint> Junctions = new System.Collections.Generic.List<LayoutPoint>();
        public System.Collections.Generic.List<RenderSegment> Segments = new System.Collections.Generic.List<RenderSegment>();
        // Door points on room walls (from RoomLinkGeometry) — normalized. The renderer draws room-wall outlines
        // with a gap at each door so a compact building reads as walled rooms with openings, not a colour blob.
        public System.Collections.Generic.List<LayoutPoint> Doors = new System.Collections.Generic.List<LayoutPoint>();
    }
}
