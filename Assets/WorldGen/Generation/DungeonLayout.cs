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

        /// <summary>THE ONE ADAPTER from a Room to the tile-space rect the FENCE reads. DeriveTownFence calls
        /// it, SettlementSelfTests' fence fixtures call it, and nothing else projects a settlement room to a
        /// rect — so they cannot disagree about how big a building is. (Until Task 5 the ROAD ROUTER was the
        /// other caller; it is gone, and with it the only per-frame consumer of this adapter.)
        ///
        /// WHY IT EXISTS (arc A, task 3). Every caller used to build its LinkNode straight from
        /// DungeonProjection.EffectiveSize, which for a settlement building room (TypeId 1, SizeW/H unset)
        /// falls through to RoomSizing.Default(1) = 6x6 TILES. A cell is BuildingCell * TilesPerAxis =
        /// 3.84 tiles at the v11 pitch (8.96 before it), so that fixed 6x6 rect was 0.67 of one cell then and
        /// is 1.56 cells now — WRONG IN BOTH DIRECTIONS, which is the point: it was never the building's real
        /// size, only a number that happened to be close. It stopped being harmless the moment a building
        /// became a FOOTPRINT: a house spanning 2x2 cells is 7.7 tiles across, so a 6x6 rect would have the
        /// fence wrapping the wrong shape and the roads clearing the wrong shape.
        ///
        /// NOTE WHAT STILL READS EffectiveSize FOR A SETTLEMENT, and is out of this adapter's reach: a GATE
        /// (TypeId 0) keeps RoomSizing.Default(0) = 7x5 tiles, which at the v11 pitch is 1.82 x 1.30 CELLS
        /// rather than the 0.78 x 0.56 it was. That used to matter because the road router inflated it into
        /// an obstacle mask wider than the ring lane the gate stands in; with the router gone the only thing
        /// that reads a gate node at all is SettlementFence, which rasterizes a gate as a bare centre POINT
        /// regardless of its W/H — so the 7x5 is now inert.
        ///
        /// A SETTLEMENT BUILDING therefore projects as its FOOTPRINT's cell bounding box, in tiles. The bbox
        /// (not the exact cell set) because the consumer wants a rect: SettlementFence rasterizes an
        /// inflated rect. An L-shaped footprint is
        /// consequently read as its filled bbox — deliberately conservative: the fence wraps a little more
        /// than the house, which is the safe direction.
        /// Note the single-cell case is EXACTLY unchanged in position: a footprint {(i,j)} has bbox centre
        /// (i+0.5)*Pitch, which is CenterOf(i) — the same point ToTile(r.X) already produced.
        ///
        /// EVERYTHING ELSE — a gate (TypeId 0), a dungeon room, a building-interior room — keeps
        /// EffectiveSize verbatim. For a dungeon or building-interior room there is no footprint to read at
        /// all. A GATE, HOWEVER, NOW HAS ONE and this branch deliberately does not use it: from v11
        /// SettlementGenerator.BuildFloor stores the gate's single ring cell in Room.Cells (so the v11
        /// recentring, which moves a town by moving CELLS, does not leave the gates behind), and the
        /// `r.TypeId == 1` guard above still sends every gate down to EffectiveSize's 7x5-tile rect. It no
        /// longer costs anything: the only consumer left is SettlementFence, which rasterizes a gate as a
        /// bare centre POINT regardless of its W/H, so the 7x5 never reaches a raster. (While the road router
        /// existed this WAS a live deferral — a gate inflated into its obstacle mask wider than the one-cell
        /// ring lane it stands in.)</summary>
        public static LinkNode LinkNodeFor(Room r, bool settlement)
        {
            if (settlement && r.TypeId == 1)
            {
                var fp = SettlementTileGrid.FootprintOf(r);
                if (fp.Count > 0)
                {
                    var (minI, minJ, maxI, maxJ) = SettlementFootprint.Bounds(fp);
                    const float pitchT = SettlementFootprint.Pitch * TilesPerAxis;   // 3.84 tiles per cell (v11)
                    return new LinkNode
                    {
                        Id = r.Id,
                        CX = (minI + maxI + 1) * 0.5f * pitchT,
                        CY = (minJ + maxJ + 1) * 0.5f * pitchT,
                        W = (maxI - minI + 1) * pitchT,
                        H = (maxJ - minJ + 1) * pitchT,
                    };
                }
            }
            var (w, h) = DungeonProjection.EffectiveSize(r);
            return new LinkNode { Id = r.Id, CX = ToTile(r.X), CY = ToTile(r.Y), W = w, H = h };
        }

        /// <summary>True when this floor is a SETTLEMENT floor — the gate on LinkNodeFor's footprint path.
        /// Read off the floor's own SettlementParams rather than off any caller-supplied flag, so no caller
        /// can put the adapter in the wrong model.</summary>
        static bool IsSettlementFloor(InteriorFloor lvl) => lvl != null && lvl.SettlementParams != null;

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
        public static RenderGraph BuildRenderGraph(InteriorFloor lvl, RoomLinkGeometry.RoutingMode mode = RoomLinkGeometry.RoutingMode.Clean)
        {
            var g = new RenderGraph();
            if (lvl == null) return g;
            var rooms = lvl.Rooms;

            // Resolve each corridor to a segment through RoomLinkGeometry: corridors now leave rooms via
            // DOOR points on the wall facing their target (max 2 per wall) and FORK off already-built
            // geometry past that limit — they no longer run centre-to-centre through the rooms they join.
            // The routing itself lives in RoomLinkGeometry, which knows nothing about dungeons so the same
            // math can serve building/city maps later; this method is just the adapter.
            // LinkNodeFor, not EffectiveSize: a settlement BUILDING is a footprint of lattice cells and must
            // be routed around at its real size (see LinkNodeFor). TILE space either way, and byte-identical
            // for every non-settlement floor.
            bool settlement = IsSettlementFloor(lvl);
            var nodes = new List<LinkNode>(lvl.Rooms.Count);
            foreach (var r in lvl.Rooms) nodes.Add(LinkNodeFor(r, settlement));
            var linkEdges = new List<LinkEdge>(lvl.Links.Count);
            foreach (var c in lvl.Links) linkEdges.Add(new LinkEdge { A = c.RoomA, B = c.RoomB });

            // ONE ROUTER for every model now (Task 5). A settlement used to take a second one — SettlementRoads'
            // fixed-grid A* — because its links were a generated STREET network that RoomLinkGeometry could not
            // route at town scale (Clean measured 20–34 s at 60 nodes) and that Fast would run through houses.
            // A generated settlement has no links at all any more (streets are stored cells,
            // SettlementParams.StreetCells), so there is no street network here to route: lvl.Links is empty
            // for a fresh town and holds only what the DM authored otherwise. DungeonViewController still
            // forces Fast for a settlement (RouteMode) so a hand-linked town cannot reach the non-scaling
            // Clean path.
            var routed = RoomLinkGeometry.Build(nodes, linkEdges, mode);
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

        /// <summary>The routed corridor legs for a building floor, in TILE space (RoomLinkGeometry's
        /// LinkGeometry.Segments — no junction-splitting; the union is the same either way). Built with the
        /// SAME rooms→LinkNodes / links→LinkEdges adapter BuildRenderGraph feeds the router, so the floor-0
        /// contour (FloorFootprint) wraps exactly the corridors the map draws. Clean by default — a settled-
        /// layout derive, matching the contour's once-per-RebuildView cadence. Empty for a null/empty floor.
        /// Never stored (boxes move
        /// every frame); route, then derive — no circularity, since building corridors ignore the contour.</summary>
        public static IReadOnlyList<LinkSegment> BuildBuildingCorridors(InteriorFloor lvl,
            RoomLinkGeometry.RoutingMode mode = RoomLinkGeometry.RoutingMode.Clean)
        {
            if (lvl == null) return System.Array.Empty<LinkSegment>();
            var nodes = new List<LinkNode>(lvl.Rooms.Count);
            foreach (var r in lvl.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                nodes.Add(new LinkNode { Id = r.Id, CX = ToTile(r.X), CY = ToTile(r.Y), W = w, H = h });
            }
            var edges = new List<LinkEdge>(lvl.Links.Count);
            foreach (var c in lvl.Links) edges.Add(new LinkEdge { A = c.RoomA, B = c.RoomB });
            return RoomLinkGeometry.Build(nodes, edges, mode).Segments;
        }

        /// <summary>THE settlement fence, DERIVED (never stored — the field was removed): the ONE place a
        /// caller gets the fence, so two of them can never disagree about where it is. Returns null unless this
        /// floor is a walled settlement (SettlementParams?.HasWall == true) — a wall-less village/camp, a
        /// dungeon or a building has no fence.
        ///
        /// WHO STILL CALLS IT, honestly stated. TWO callers: DungeonFlatRenderer.RebuildTownWall and
        /// SettlementSelfTests.SelfTestWallBounds. The renderer one is UNREACHABLE IN THE SHIPPED HOST — a
        /// settlement is drawn by SettlementVolumeRenderer, which builds SettlementTileGrid and derives its own
        /// Wall ring; the flat renderer only ever sees a settlement in a host that installed no volumetric
        /// renderer (RendererForKind's degradation fallback), and DungeonEditorScreen code-constructs both.
        /// FitBoundsFor stopped calling this at Task 8 of the 2.5D arc (it fits the tile grid instead). So the
        /// fence survives as the flat renderer's fallback drawing and as a tested derivation, NOT as something
        /// on any per-frame path.
        ///
        /// Splits the floor's rooms into buildings (TypeId 1) and gates (TypeId 0) through LinkNodeFor, folds
        /// the floor's STORED STREET CELLS in as one cell-sized rect each, and traces the fence around the
        /// inflated union (SettlementFence.Derive). TILE space out (SettlementFence's convention), UNLIKE the
        /// old normalized stored wall — so a caller must NOT re-scale by TilesPerAxis.
        ///
        /// THE STREETS ARE WHAT THE ROADS USED TO BE, AND FOLDING THEM IS A MEASURED CHOICE, NOT A GUESS
        /// (Task 5). Until this task the third input was the ROUTED roads: SettlementRoads.Build over
        /// lvl.Links, re-run on every derive at ~12.5–18 ms, which is why the method carried an includeRoads
        /// flag two-tiering it against drag frames. Both are gone. What replaces them has to be the streets
        /// themselves, because those are what the town is now MADE of and what the drawn wall ring is seeded
        /// from: SettlementTileGrid.Build dilates Building ∪ StreetCells (see BuildWallRing), so a fence that
        /// ignored the streets would wrap a different town than the one on screen.
        ///
        /// MEASURED on generated walled towns, 5 seeds × Small/Medium/Large, counting stored street cells
        /// whose centre falls OUTSIDE the derived fence:
        ///   • with the old ROUTED roads: 9–38 cells outside (the router only ever laid lanes between linked
        ///     nodes, so most of the stored network was never wrapped at all);
        ///   • with nothing folded: 14–41 outside, and the point list differs from the roads one on every
        ///     seed — so the roads were load-bearing and dropping them silently would have been a change;
        ///   • with the streets folded, as here: 0 outside on every seed, buildings likewise 0 outside.
        /// The fence therefore encloses MORE than it used to, not less, and now agrees with the tile grid
        /// about what the town covers. SelfTestWallBounds asserts both halves — every stored street cell
        /// inside, and (the non-vacuity control) a buildings+gates-only fence leaving at least one outside.
        ///
        /// A street cell is folded as a RECT (pitch × pitch at the cell centre, inflated by marginTiles like
        /// every other building rect), not as a degenerate road segment: it is the same shape the tile grid
        /// seeds from, and it cannot depend on how SettlementFence samples a zero-length capsule.</summary>
        public static WallContour DeriveTownFence(InteriorFloor lvl)
        {
            if (lvl == null || lvl.SettlementParams == null || !lvl.SettlementParams.HasWall) return null;

            // LinkNodeFor, not EffectiveSize: the fence must wrap the buildings' actual FOOTPRINTS, or a
            // multi-cell house pokes out through its own town wall (see LinkNodeFor). Always the settlement
            // path here — this method returns null above for anything that is not a walled settlement floor.
            var buildings = new List<LinkNode>();
            var gates = new List<LinkNode>();
            foreach (var r in lvl.Rooms)
            {
                var node = LinkNodeFor(r, settlement: true);
                if (r.TypeId == 1) buildings.Add(node); else gates.Add(node);   // gate = TypeId 0 (rasterized as a point)
            }
            // No buildings, no town, no fence — SettlementFence.Derive's own rule, kept explicit here so that
            // folding the streets in below cannot resurrect a fence around a building-less floor.
            if (buildings.Count == 0) return null;

            // The stored streets, one cell-sized rect each (see the method doc for the measurement). Id is
            // unused by SettlementFence — it rasterizes CX/CY/W/H and nothing else — so these carry 0 rather
            // than pretending to be rooms.
            const float pitchT = SettlementFootprint.Pitch * TilesPerAxis;   // 3.84 tiles per cell (v11)
            foreach (var c in SettlementFootprint.Decode(lvl.SettlementParams.StreetCells))
                buildings.Add(new LinkNode
                {
                    Id = 0,
                    CX = SettlementFootprint.CenterOf(c.i) * TilesPerAxis,
                    CY = SettlementFootprint.CenterOf(c.j) * TilesPerAxis,
                    W = pitchT, H = pitchT,
                });

            return SettlementFence.Derive(buildings, gates, System.Array.Empty<LinkSegment>(), SettlementFence.FenceMarginTiles);
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
