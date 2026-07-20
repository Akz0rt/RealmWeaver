using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure, headless ADJACENCY-FIRST layout for building interiors: rooms are packed FLUSH against
    /// their linked neighbours so most links render as a shared-wall DOOR rather than a corridor. This is the
    /// Compact profile's counterpart to <see cref="DungeonLayout.Separate"/> (which spreads a dungeon out) —
    /// a building wants tight, touching chambers.
    ///
    /// Positions are normalized 0..1 (Room.X/Y, the field centre); sizes are in tiles; ALL geometry runs in
    /// TILE space via <see cref="DungeonLayout.TilesPerAxis"/> and <see cref="DungeonProjection.EffectiveSize"/>
    /// so this agrees with Separate, the leash and every renderer about how big a room is and what
    /// "touching"/"overlapping" means. No UnityEngine types — self-tests without a scene.
    ///
    /// The overlap / edge-gap convention is exactly Separate's Chebyshev test: two footprints overlap iff
    /// their centre distance is LESS than the summed half-extents on BOTH axes; they touch (share a wall) iff
    /// that distance EQUALS the summed half-extents on one axis while overlapping on the other.</summary>
    public static class CompactLayout
    {
        // Tolerance (tiles) for "the Chebyshev edge gap is ≈ 0" — i.e. two footprints TOUCH on an axis.
        // Generous vs. the ToNorm/ToTile float round-trip (~1e-5 tiles) yet far below any real gap (whole
        // tiles), so a flush pair reads as touching and a one-tile gap never does.
        const float TouchEps = 0.02f;

        // A candidate footprint counts as OVERLAPPING a placed room only when it penetrates by MORE than this
        // (tiles) on BOTH axes. Smaller than TouchEps so a FLUSH candidate (edge gap ≈ 0 ± round-trip) is
        // treated as FREE, not as an overlap — flush placement is the whole point.
        const float OverlapEps = 1e-3f;

        // Extra clearance (tiles) ResolveOverlapsMovableOnly adds on top of the measured penetration when it
        // shoves a movable room clear. Bounded on BOTH sides, not just one:
        //   - ABOVE the ToNorm/ToTile float round-trip noise floor (~1e-5 tiles), so the shoved room provably
        //     ends up outside the other footprint and doesn't get read back as still overlapping; and
        //   - BELOW TouchEps (0.02), so the resolved edge gap still reads as TOUCHING — the shoved room must
        //     land flush, the same as a generated room, not with a visible gap. A too-large clearance here was
        //     the bug: 0.1 (5x TouchEps) resolved overlaps into a gap wide enough that AdjacentAlongWall / a
        //     shared-wall door never triggered, so a manually dropped room diverged from a generated one.
        const float ShoveClearance = 0.005f;

        // Slack (tiles) added to the footprint BOUNDING BOX used as the packer's cheap pre-rejection of candidate
        // slots (see FootprintBoundsTiles). Only has to swallow float round-off — the box is an over-approximation
        // of the footprint either way, and FloorFootprint.ContainsRect remains the real accept predicate.
        const float BoundsSlack = 1e-3f;

        static float ToTile(float norm) => norm * DungeonLayout.TilesPerAxis;
        static float ToNorm(float tile) => tile / DungeonLayout.TilesPerAxis;
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ---------------------------------------------------------------------------------------------
        // Arrange — deterministic adjacency-first placement from the entrance.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Place every room deterministically, packing each linked room FLUSH against the neighbour
        /// it is first reached through (BFS from the entrance). The entrance (TypeId==0, else lowest Id) goes
        /// at the field centre; each next room takes the first FREE side of its BFS parent in the fixed order
        /// Right, Down, Left, Up (free = its footprint overlaps no already-placed room). If no side of the
        /// parent is free, the room is pushed straight outward along the four sides until a free slot is found
        /// (that link will later render as a corridor, not a door). Rooms not reachable from the entrance are
        /// dropped at the nearest free tile outward from the centre. Deterministic (fixed room order = BFS
        /// then ascending id; fixed side order); positions written back normalized and clamped [0,1].</summary>
        public static void Arrange(InteriorFloor floor)
        {
            if (floor == null || floor.Rooms.Count == 0) return;

            var root = PickEntrance(floor);
            var placed = BfsPlaceCore(floor, root, recenterRoot: true);

            // Any room not reached over Links (unlinked, or a separate component): drop it at the nearest
            // free slot outward from the field centre so Arrange stays TOTAL (every room gets a non-overlapping
            // position). Processed in ascending id for determinism.
            var byId = SortedById(floor.Rooms);
            foreach (var r in byId)
            {
                if (Contains(placed, r)) continue;
                PlaceOutwardFromPoint(r, ToTile(0.5f), ToTile(0.5f), placed);
                placed.Add(r);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Settle — compact relaxation with one room pinned.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Compact re-pack primitive: re-pack the floor around a FIXED anchor (the anchor never
        /// moves). Not wired to drag under the revised spec C4 (drag uses <see cref="NudgeRoomOffOverlaps"/>,
        /// which moves only the dragged room); kept as a general "compact this floor" building block for a
        /// future manual tidy / generation-time re-pack. It rebuilds
        /// the flush adjacency tree by BFS from the anchor at its CURRENT position — this pulls every linked
        /// room back toward flush adjacency with its neighbour and, because each room is placed only in a slot
        /// that overlaps nothing already placed, resolves overlaps by construction using the SAME Chebyshev
        /// test Separate uses. Rooms NOT reachable from the anchor over Links (orphans / a separate component)
        /// are then nudged apart by an anchor-and-tree-pinned overlap pass that reuses Separate's
        /// least-penetration push — only those loose rooms move, so the flush tree is never disturbed.
        /// Bounded iterations, deterministic. Unlike Arrange, the anchor is NOT re-centred.</summary>
        public static void Settle(InteriorFloor floor, int anchorRoomId)
        {
            if (floor == null || floor.Rooms.Count == 0) return;

            var anchor = floor.GetRoom(anchorRoomId) ?? PickEntrance(floor);
            if (anchor == null) return;

            // Rebuild the adjacency tree with the anchor pinned where it is (recenterRoot: false).
            var placed = BfsPlaceCore(floor, anchor, recenterRoot: false);

            // Overlap safety net for rooms the BFS never reached. Only these are movable; the anchor and the
            // whole flush tree stay put, so their shared walls survive.
            var movable = new List<Room>();
            foreach (var r in SortedById(floor.Rooms))
                if (!Contains(placed, r)) movable.Add(r);
            ResolveOverlapsMovableOnly(floor, movable);
        }

        // ---------------------------------------------------------------------------------------------
        // NudgeRoomOffOverlaps — building drag-settle: move ONLY the dragged room off overlaps (spec C4).
        // ---------------------------------------------------------------------------------------------

        /// <summary>Building drag-settle under the "stays where dropped, others never move" model (spec C4,
        /// revised 2026-07-19 — the user's hard rule: "перетаскивание комнаты никак не должно влиять на
        /// месторасположение других комнат"). The room the DM just dropped KEEPS its dropped position; the
        /// SOLE correction is anti-overlap — if that room penetrates another room's footprint, IT ALONE is
        /// shoved clear along the axis of least penetration (<see cref="ResolveOverlapsMovableOnly"/> with a
        /// single movable room). Every OTHER room is FIXED, so dragging one room can never relocate another.
        ///
        /// This deliberately does NOT re-pack the floor (compactness is a GENERATION concern — see
        /// <see cref="Arrange"/> — not an interaction one) and does NOT contain
        /// the room to any contour: a room parked outside the ground-floor contour is LEFT there for the C2'
        /// red-flag, since out-of-contour is a deliberate DM choice, not an error to auto-fix. A room dropped
        /// in free space is not moved at all (no overlap ⇒ no-op). Deterministic, headless. Same behaviour on
        /// every floor — floor 0 and upper floors are treated identically.</summary>
        public static void NudgeRoomOffOverlaps(InteriorFloor floor, int roomId)
        {
            if (floor == null || floor.Rooms.Count == 0) return;
            var room = floor.GetRoom(roomId);
            if (room == null) return;

            // Gentle first: shove the dragged room off overlaps along the axis of LEAST penetration — it
            // slides just clear of the neighbour it landed on and stays close to where it was dropped (the
            // common single-overlap case).
            ResolveOverlapsMovableOnly(floor, new List<Room> { room });

            // Guarantee-clear fallback: the least-penetration shove can OSCILLATE and stop STILL overlapping
            // when the dragged room is wedged in a gap NARROWER than itself (blocked on the least-pen axis on
            // both sides — e.g. dropped into a 2-tile gap between two 4-wide fixed rooms). The chosen model is
            // "anti-overlap": a room must never be silently left on top of another. So if it is still
            // overlapping, relocate ONLY it to the NEAREST free slot expanding outward from where it now sits
            // (rooms it isn't overlapping never move). Nearest-slot keeps it as close as possible; the model
            // already accepts moving the dragged room itself, only never the others.
            var others = new List<Room>();
            foreach (var r in floor.Rooms) if (r.Id != room.Id) others.Add(r);
            var (rw, rh) = DungeonProjection.EffectiveSize(room);
            if (!IsFree(ToTile(room.X), ToTile(room.Y), rw, rh, others))
                PlaceOutwardFromPoint(room, ToTile(room.X), ToTile(room.Y), others);
        }

        // ---------------------------------------------------------------------------------------------
        // New-room placement (spec C6 / user 2026-07-19): a + room must land as PART of the building, never
        // floating in empty space outside the contour.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Place a NEWLY ADDED room FLUSH against the existing interior so it becomes part of the
        /// building — used on the GROUND floor, where adding a room GROWS the footprint (the contour is
        /// recomputed from this floor and will wrap the new room). Picks the existing room nearest the new
        /// room's current position as the parent and snaps the new room to that parent's first free side
        /// (Right/Down/Left/Up), exactly like generation's adjacency placement, so it lands touching the
        /// building. Moves ONLY the new room; a lone first room is left where it is. Deterministic, headless.</summary>
        public static void AttachNewRoom(InteriorFloor floor, int roomId)
        {
            if (floor == null || floor.Rooms.Count == 0) return;
            var room = floor.GetRoom(roomId);
            if (room == null) return;
            var others = new List<Room>();
            foreach (var r in SortedById(floor.Rooms)) if (r.Id != room.Id) others.Add(r);
            if (others.Count == 0) return;   // first room on the floor — nothing to attach to
            PlaceAgainst(room, NearestRoom(room, others), others);
        }

        /// <summary>Place a NEWLY ADDED room on an UPPER floor at the spot with the MOST free space INSIDE the
        /// floor-0 contour (user 2026-07-19): if the room fits inside the contour without overlapping another
        /// room it lands there; if the interior is full it goes where the most of its footprint is clear —
        /// which pushes it to the edge, so it may poke OUTSIDE the contour and be red-flagged, but its
        /// position is driven by "where there is the most room". Scans candidate centres on a 1-tile grid over
        /// the contour's bbox, keeping only centres INSIDE the contour; scores each by the fraction of the
        /// room footprint free of other rooms (primary), then the fraction inside the contour (tie-break),
        /// then nearest the contour centroid. Moves ONLY the new room. Deterministic, headless.</summary>
        public static void PlaceNewRoomInContour(InteriorFloor floor, int roomId, InteriorFloor contourFloor, float margin)
        {
            if (floor == null) return;
            var room = floor.GetRoom(roomId);
            if (room == null || contourFloor == null || contourFloor.Rooms.Count == 0) return;
            var (w, h) = DungeonProjection.EffectiveSize(room);

            var others = new List<Room>();
            foreach (var r in SortedById(floor.Rooms)) if (r.Id != room.Id) others.Add(r);

            var (cMinX, cMinY, cMaxX, cMaxY) = DungeonProjection.ContentBoundsTiles(contourFloor);
            float ccx = (cMinX + cMaxX) * 0.5f, ccy = (cMinY + cMaxY) * 0.5f;

            bool found = false;
            float bestFree = -1f, bestInside = -1f, bestDist = 0f, bestX = 0f, bestY = 0f;
            for (float cx = cMinX; cx <= cMaxX + 1e-3f; cx += 1f)
                for (float cy = cMinY; cy <= cMaxY + 1e-3f; cy += 1f)
                {
                    if (!FloorFootprint.CoversPoint(contourFloor, margin, cx, cy)) continue;
                    var (free, inside) = FootprintSampleScores(cx, cy, w, h, contourFloor, margin, others);
                    float dist = (cx - ccx) * (cx - ccx) + (cy - ccy) * (cy - ccy);
                    bool better = !found
                        || free > bestFree + 1e-4f
                        || (Approx(free, bestFree) && inside > bestInside + 1e-4f)
                        || (Approx(free, bestFree) && Approx(inside, bestInside) && dist < bestDist - 1e-4f);
                    if (better) { found = true; bestFree = free; bestInside = inside; bestDist = dist; bestX = cx; bestY = cy; }
                }
            if (found) { room.X = Clamp01(ToNorm(bestX)); room.Y = Clamp01(ToNorm(bestY)); }
        }

        // (freeOfRooms, insideContour) fractions of the room footprint centred at (cx,cy), sampled on a 5×5
        // grid: freeOfRooms = samples not inside any OTHER room; insideContour = samples inside the contour.
        static (float free, float inside) FootprintSampleScores(float cx, float cy, float w, float h,
            InteriorFloor contourFloor, float margin, List<Room> others)
        {
            const int N = 5;
            int freeCount = 0, insideCount = 0, total = 0;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    float sx = cx - w * 0.5f + w * (i + 0.5f) / N;
                    float sy = cy - h * 0.5f + h * (j + 0.5f) / N;
                    total++;
                    if (FloorFootprint.CoversPoint(contourFloor, margin, sx, sy)) insideCount++;
                    bool inOther = false;
                    foreach (var o in others)
                    {
                        var (ow, oh) = DungeonProjection.EffectiveSize(o);
                        float ox = ToTile(o.X), oy = ToTile(o.Y);
                        if (sx > ox - ow * 0.5f && sx < ox + ow * 0.5f && sy > oy - oh * 0.5f && sy < oy + oh * 0.5f)
                        { inOther = true; break; }
                    }
                    if (!inOther) freeCount++;
                }
            return (freeCount / (float)total, insideCount / (float)total);
        }

        /// <summary>The candidate nearest <paramref name="from"/> by normalized centre distance; ties broken
        /// by lowest Id for determinism.</summary>
        static Room NearestRoom(Room from, List<Room> candidates)
        {
            Room best = null; float bestD = float.MaxValue;
            foreach (var c in candidates)
            {
                float dx = c.X - from.X, dy = c.Y - from.Y;
                float d = dx * dx + dy * dy;
                if (best == null || d < bestD - 1e-6f) { bestD = d; best = c; }
            }
            return best;
        }

        static bool Approx(float a, float b) => System.Math.Abs(a - b) <= 1e-4f;

        // ---------------------------------------------------------------------------------------------
        // PackAroundColumnWithinFootprint — shape-aware building-floor packing (spec stairwell-column stage B).
        // ---------------------------------------------------------------------------------------------

        /// <summary>Pack a building floor's rooms flush around a FIXED column room (pinned exactly at
        /// colXTiles,colYTiles) so EVERY placed room stays inside <paramref name="contourFloor"/>'s footprint
        /// shape (+margin — the drawn contour). Returns the number of rooms KEPT (incl. the column); rooms with
        /// no valid in-footprint slot are DROPPED (removed from <c>floor.Rooms</c>, with their Links).
        ///
        /// THREE PHASES, all sharing the two accept predicates — <see cref="IsFree"/> (Chebyshev non-overlap vs.
        /// every room placed so far) AND <see cref="FloorFootprint.ContainsRect"/> (entirely inside the drawn
        /// contour). The column is never moved by any phase.
        ///   1. SEED — BFS over Links from the column (neighbours ascending id), each child seated on the first
        ///      free side of the room it was reached through. Only placed rooms are enqueued.
        ///   2. FILL, swept until stable: every still-unplaced room (ascending id) is tried FLUSH (d == 0) against
        ///      EVERY already-placed room (ascending id), four sides each; a room placed this way becomes an anchor
        ///      for the rest of the sweep, so the sweep repeats until it places nothing new. This is the fix for
        ///      the reported "max rooms still leaves an empty pocket": phase 1 alone tries a room ONLY against its
        ///      own BFS parent, so a boxed-in parent dropped the child (and everything reachable only through it)
        ///      even when a completely different placed room had a free flush side next to the pocket.
        ///   3. RELAXED FALLBACK, swept until stable: whatever is still unplaced gets the ORIGINAL search —
        ///      increasing outward distance d rays (d &gt; 0 renders as a corridor, not a door) — against every
        ///      placed room. Its d loop starts at 0, so it subsumes phase 2's search; phase 2 still earns its keep
        ///      by running to a FIXPOINT first, so that when phase 3 starts NO unplaced room has a flush slot
        ///      against any placed room — a corridor slot is only ever handed out once flush packing is exhausted.
        /// Within phases 2 and 3, each distance tries the anchors the room is ALREADY LINKED to before any other
        /// anchor (see <see cref="SeatAgainstAnyPlaced"/>): a room's original links survive packing, so seating it
        /// next to an unrelated room leaves those links to render as long routed corridors — exactly the
        /// "detached rooms joined by corridors" look this packer exists to avoid.
        /// The pipeline is run TWICE, and the run that keeps MORE rooms wins (ties → the first, compact one):
        ///   • COMPACT run — phase 1 seats flush only (d == 0). Packs tighter, so it usually leaves one big
        ///     contiguous pocket for phase 2 rather than several unusable slivers; this is what actually raises
        ///     the cap.
        ///   • SPREAD run — phase 1 is the PRE-FIX search (increasing d against the BFS parent): the same BFS, the
        ///     same neighbour/side/distance orders and the same two accept predicates, PLUS the sound bounding-box
        ///     pre-rejection of <see cref="TrySeatAtDistance"/> (which never discards a slot ContainsRect would
        ///     have accepted) — so it reproduces the old packer's placements exactly. Phases 2/3 only ADD rooms
        ///     into free slots without moving anything, so this run can never keep fewer rooms than the old packer.
        /// Taking the better of the two makes "never worse than before" a property of the CODE, not a hope, and it
        /// is not free insurance either: measured over 2880 packs on real generated contours, the compact run alone
        /// regressed against the old packer on 2.7% of them (worst −3), while best-of-two beat SPREAD-ONLY on 8.1%
        /// of packs and on 12.3% of the probed «из N» caps (up to +2 rooms). Both runs earn their keep.
        /// If the compact run already placed EVERY room the spread run cannot beat it, so it is skipped — a pure
        /// speed-up that cannot change the result (ties go to compact anyway).
        /// Phases 2 and 3 ADD a Link between the room and the anchor it was seated against when the pair is not
        /// linked yet (deduplicated, either direction): flush ⇒ the shared wall renders as a DOOR, and every
        /// placed room stays connected to the column (the validator's orphan rule, and TrimToRoomCount's BFS
        /// prefix, both read Links). Links are only ever ADDED, never removed except with a dropped room.
        /// Deterministic — no RNG, every iteration order fixed (BFS then ascending id), and the placement decisions
        /// never read the rooms' INPUT positions (only the column pin and rooms already placed in the same run), so
        /// the two runs cannot influence each other. PRECONDITION: <paramref name="contourFloor"/> is a different
        /// floor than <paramref name="floor"/> (packing rewrites floor's positions). NOT IDEMPOTENT: the fill
        /// phases APPEND to <c>floor.Links</c>, so a second call on the SAME floor packs against a different
        /// adjacency and can produce a different layout. Every call site packs a floor built fresh by
        /// <see cref="BuildingGenerator.GenerateFloorAroundColumn"/>, which is what makes that safe. Headless.</summary>
        public static int PackAroundColumnWithinFootprint(InteriorFloor floor, int columnRoomId,
            float colXTiles, float colYTiles, InteriorFloor contourFloor, float margin)
        {
            if (floor == null || floor.Rooms.Count == 0) return 0;
            var column = floor.GetRoom(columnRoomId);
            if (column == null || contourFloor == null) return floor.Rooms.Count;

            column.X = Clamp01(ToNorm(colXTiles));
            column.Y = Clamp01(ToNorm(colYTiles));

            var bounds = FootprintBoundsTiles(contourFloor, margin);
            var ordered = SortedById(floor.Rooms);   // the fixed ascending-id sweep order of phases 2 and 3
            var adj = BuildAdjacency(floor);

            var compact = RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds, seedMaxDistance: 0);
            // The spread run can at best MATCH a compact run that already placed everything, and ties go to
            // compact — so skipping it there is a pure speed-up with a bit-identical result.
            var spread = compact.Ids.Count >= ordered.Count
                ? null
                : RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds,
                    seedMaxDistance: DungeonLayout.TilesPerAxis);
            var chosen = spread != null && spread.Ids.Count > compact.Ids.Count ? spread : compact;
            Apply(floor, chosen);
            return chosen.Ids.Count;
        }

        /// <summary>What one run of the three phases decided: which rooms it placed, where, and which room↔anchor
        /// Links its fill phases want added. Nothing is committed to the floor until <see cref="Apply"/> — so two
        /// runs can be compared and only the winner's layout kept.</summary>
        sealed class PackResult
        {
            public readonly List<Room> Placed = new List<Room>();          // placement order (the IsFree working set)
            public readonly HashSet<int> Ids = new HashSet<int>();
            public readonly Dictionary<int, (float x, float y)> Pos = new Dictionary<int, (float x, float y)>();
            public readonly List<int> LinkA = new List<int>();             // parallel arrays: LinkA[i] ↔ LinkB[i]
            public readonly List<int> LinkB = new List<int>();
        }

        /// <summary>One full run of phases 1-3. <paramref name="seedMaxDistance"/> is the BFS phase's outward
        /// search limit: 0 = flush-only (the compact run), TilesPerAxis = the pre-fix ray search (the spread run).
        /// Writes positions onto the rooms as it goes (the phases must measure real geometry) and snapshots them
        /// at the end; the caller keeps only the winning snapshot. Deterministic.</summary>
        static PackResult RunPhases(InteriorFloor floor, Room column, List<Room> ordered,
            Dictionary<int, List<int>> adj, InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int seedMaxDistance)
        {
            var res = new PackResult();
            res.Placed.Add(column);
            res.Ids.Add(column.Id);

            // --- Phase 1: BFS from the column over Links. ------------------------------------------------
            var queue = new Queue<int>();
            queue.Enqueue(column.Id);
            while (queue.Count > 0)
            {
                var cur = floor.GetRoom(queue.Dequeue());
                if (cur == null || !adj.TryGetValue(cur.Id, out var nbs)) continue;
                foreach (int nb in nbs)   // ascending id
                {
                    if (res.Ids.Contains(nb)) continue;
                    var child = floor.GetRoom(nb);
                    if (child == null) continue;
                    bool seated = false;
                    for (int d = 0; d <= seedMaxDistance && !seated; d++)
                        seated = TrySeatAtDistance(child, cur, d, res.Placed, contourFloor, margin, bounds);
                    if (!seated) continue;
                    res.Placed.Add(child);
                    res.Ids.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // --- Phase 2: fill the space phase 1 left — flush against ANY placed room. --------------------
            FillSweeps(res, ordered, adj, contourFloor, margin, bounds, maxDistance: 0);

            // --- Phase 3: relaxed fallback — outward rays against any placed room. ------------------------
            // Skipped outright when phase 1+2 already placed everything: there is nothing left to seat, and this
            // is the phase whose 129-distance scan dominates the packer's cost.
            if (res.Ids.Count < ordered.Count)
                FillSweeps(res, ordered, adj, contourFloor, margin, bounds, maxDistance: DungeonLayout.TilesPerAxis);

            foreach (var r in res.Placed) res.Pos[r.Id] = (r.X, r.Y);
            return res;
        }

        /// <summary>Commit one run to the floor: restore its positions (the other run may have overwritten them),
        /// add its fill-phase links (deduplicated), then DROP every room it could not place, with their links.</summary>
        static void Apply(InteriorFloor floor, PackResult res)
        {
            foreach (var r in res.Placed)
            {
                var p = res.Pos[r.Id];
                r.X = p.x; r.Y = p.y;
            }
            for (int i = 0; i < res.LinkA.Count; i++) AddLinkIfAbsent(floor, res.LinkA[i], res.LinkB[i]);
            floor.Rooms.RemoveAll(r => !res.Ids.Contains(r.Id));
            floor.Links.RemoveAll(l => !res.Ids.Contains(l.RoomA) || !res.Ids.Contains(l.RoomB));
        }

        /// <summary>Sweep every still-unplaced room (ascending id) against every already-placed room, seating it
        /// at the first slot that is free AND inside the footprint, and repeat the whole sweep until one places
        /// nothing new (a room placed mid-sweep is an anchor for the rest of it). <paramref name="maxDistance"/>
        /// 0 = flush only (phase 2); TilesPerAxis = the original outward-ray search (phase 3). Each placement
        /// records the room↔anchor Link. Terminates: every sweep either places a room (bounded by the room count)
        /// or ends the loop. The ascending-id anchor list is maintained here (inserted into on placement) instead
        /// of being re-sorted for every room. Deterministic.</summary>
        static void FillSweeps(PackResult res, List<Room> ordered, Dictionary<int, List<int>> adj,
            InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int maxDistance)
        {
            if (res.Ids.Count >= ordered.Count) return;   // everything is already placed — nothing to sweep
            var anchors = SortedById(res.Placed);   // ascending id — the fixed anchor order, kept sorted below
            var linkedAnchors = new List<Room>();   // scratch, reused by every SeatAgainstAnyPlaced call below
            var otherAnchors = new List<Room>();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var room in ordered)   // ascending id
                {
                    if (res.Ids.Contains(room.Id)) continue;
                    var anchor = SeatAgainstAnyPlaced(room, anchors, linkedAnchors, otherAnchors, adj,
                        res.Placed, contourFloor, margin, bounds, maxDistance);
                    if (anchor == null) continue;
                    res.Placed.Add(room);
                    res.Ids.Add(room.Id);
                    InsertById(anchors, room);
                    res.LinkA.Add(anchor.Id); res.LinkB.Add(room.Id);
                    progress = true;
                }
                if (res.Ids.Count >= ordered.Count) return;   // every room placed — no further sweep can do anything
            }
        }

        /// <summary>Seat <paramref name="room"/> against the already-placed rooms, nearest distance first: for
        /// each outward distance d (0 = flush), the anchors the room is ALREADY LINKED to (ascending id) and then
        /// every other placed room (ascending id), four sides each. Returns the anchor it was seated against (and
        /// writes room.X/Y), or null when no slot passes both predicates.
        /// DISTANCE-outer so a flush (door) slot always wins over a pushed-out (corridor) one at ANY anchor.
        /// LINKED-anchor first because the room's own links survive packing: seating it next to an unrelated room
        /// leaves those links to route as long corridors across the floor, which is the look this packer exists to
        /// avoid. <paramref name="anchors"/> is <paramref name="placed"/> in ascending id, maintained by the
        /// caller; it is split ONCE per call into the two preference groups, using the caller's reusable
        /// <paramref name="linkedAnchors"/>/<paramref name="otherAnchors"/> buffers. The distance loop
        /// stops at <see cref="MaxUsefulDistance"/> instead of the full TilesPerAxis: past it every candidate is
        /// outside the footprint's bounding box, so this only skips work that could not have succeeded — the same
        /// exactness the box pre-test in <see cref="TrySeatAtDistance"/> relies on. Deterministic.</summary>
        static Room SeatAgainstAnyPlaced(Room room, List<Room> anchors,
            List<Room> linkedAnchors, List<Room> otherAnchors, Dictionary<int, List<int>> adj,
            List<Room> placed, InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int maxDistance)
        {
            List<int> linked = null;
            if (adj != null) adj.TryGetValue(room.Id, out linked);

            linkedAnchors.Clear(); otherAnchors.Clear();   // caller-owned scratch; both keep the ascending-id order
            foreach (var anchor in anchors)
            {
                if (linked != null && linked.Contains(anchor.Id)) linkedAnchors.Add(anchor);
                else otherAnchors.Add(anchor);
            }

            int limit = MaxUsefulDistance(room, anchors, bounds);
            if (limit > maxDistance) limit = maxDistance;
            for (int d = 0; d <= limit; d++)
            {
                foreach (var anchor in linkedAnchors)
                    if (TrySeatAtDistance(room, anchor, d, placed, contourFloor, margin, bounds)) return anchor;
                foreach (var anchor in otherAnchors)
                    if (TrySeatAtDistance(room, anchor, d, placed, contourFloor, margin, bounds)) return anchor;
            }
            return null;
        }

        /// <summary>Largest outward distance d at which ANY anchor could still put <paramref name="room"/> inside
        /// the footprint bounding box, or -1 when even d = 0 is hopeless. A candidate on the Right side sits at
        /// <c>px + offX + d</c>, so it fits the box only while <c>d ≤ maxX − px − offX − w/2</c>; the other three
        /// sides give the mirrored bounds. Taking the best over the four sides and over every anchor is therefore
        /// an EXACT cut-off — every larger d is rejected by the box pre-test anyway. It matters because phase 3's
        /// nominal range is 0..TilesPerAxis (129 distances) while a real floor-0 footprint spans ~20-30 tiles.</summary>
        static int MaxUsefulDistance(Room room, List<Room> anchors,
            (float minX, float minY, float maxX, float maxY) bounds)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(room);
            float best = float.NegativeInfinity;
            foreach (var anchor in anchors)
            {
                var (pw, ph) = DungeonProjection.EffectiveSize(anchor);
                float px = ToTile(anchor.X), py = ToTile(anchor.Y);
                float offX = (pw + cw) * 0.5f, offY = (ph + ch) * 0.5f;
                float r = bounds.maxX - px - offX - cw * 0.5f;   // Right
                float l = px - bounds.minX - offX - cw * 0.5f;   // Left
                float dn = bounds.maxY - py - offY - ch * 0.5f;  // Down (+Y)
                float up = py - bounds.minY - offY - ch * 0.5f;  // Up
                if (r > best) best = r;
                if (l > best) best = l;
                if (dn > best) best = dn;
                if (up > best) best = up;
            }
            // Nudged by the same hair the box itself is grown by, so float round-off can only ever admit one extra
            // distance (which the box pre-test then discards) — never cut a distance that could succeed.
            best += BoundsSlack;
            return best < 0f ? -1 : (int)System.Math.Floor(best);
        }

        /// <summary>Insert a room into an ascending-id list, keeping it sorted (the ids are distinct).</summary>
        static void InsertById(List<Room> sorted, Room room)
        {
            int i = sorted.Count;
            while (i > 0 && sorted[i - 1].Id > room.Id) i--;
            sorted.Insert(i, room);
        }

        /// <summary>Try the four sides (Right, Down, Left, Up) of <paramref name="anchor"/> at the FIXED outward
        /// distance <paramref name="d"/> (0 = flush, so the pair shares a wall), taking the first candidate that
        /// is free of overlap AND inside the footprint. Writes child.X/Y normalized+clamped and returns true on
        /// success; leaves the child untouched otherwise. <paramref name="bounds"/> is the footprint's bounding
        /// box: a candidate poking outside it can NEVER be inside the footprint union, so rejecting it there is
        /// exact and skips the arrangement work for the far-out rays of phase 3. Deterministic.</summary>
        static bool TrySeatAtDistance(Room child, Room anchor, int d, List<Room> placed,
            InteriorFloor contourFloor, float margin, (float minX, float minY, float maxX, float maxY) bounds)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            var (pw, ph) = DungeonProjection.EffectiveSize(anchor);
            float px = ToTile(anchor.X), py = ToTile(anchor.Y);
            float offX = (pw + cw) * 0.5f, offY = (ph + ch) * 0.5f;
            for (int s = 0; s < 4; s++)
            {
                float cx, cy;
                switch (s)
                {
                    case 0: cx = px + offX + d; cy = py; break;   // Right
                    case 1: cx = px; cy = py + offY + d; break;   // Down (+Y)
                    case 2: cx = px - offX - d; cy = py; break;   // Left
                    default: cx = px; cy = py - offY - d; break;  // Up
                }
                if (cx - cw * 0.5f < bounds.minX || cx + cw * 0.5f > bounds.maxX
                    || cy - ch * 0.5f < bounds.minY || cy + ch * 0.5f > bounds.maxY) continue;
                if (IsFree(cx, cy, cw, ch, placed)
                    && FloorFootprint.ContainsRect(contourFloor, margin, cx, cy, cw, ch))
                {
                    child.X = Clamp01(ToNorm(cx));
                    child.Y = Clamp01(ToNorm(cy));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Bounding box (tile space) of the drawn footprint = the contour rooms' content bounds grown by
        /// the contour margin. <see cref="FloorFootprint.ContainsRect"/> can only be true for a rect inside the
        /// union of the margin-expanded room rects, and that union lies inside this box — so failing this box is
        /// a sound (never over-rejecting) O(1) pre-test. Grown by one more hair (BoundsSlack) so a rect sitting
        /// EXACTLY on the contour edge can never be rejected by float round-off: the pre-test must only ever
        /// discard candidates ContainsRect would have discarded anyway.</summary>
        static (float minX, float minY, float maxX, float maxY) FootprintBoundsTiles(InteriorFloor contourFloor, float margin)
        {
            var (minX, minY, maxX, maxY) = DungeonProjection.ContentBoundsTiles(contourFloor);
            float g = margin + BoundsSlack;
            return (minX - g, minY - g, maxX + g, maxY + g);
        }

        /// <summary>Add an undirected Link between two rooms unless the pair is ALREADY linked in EITHER
        /// direction (or is the same room) — the packer's fill phases must never duplicate an edge the graph
        /// builder already made. Same construction as BuildingGenerator's link builder.</summary>
        static void AddLinkIfAbsent(InteriorFloor floor, int a, int b)
        {
            if (a == b) return;
            foreach (var l in floor.Links)
                if ((l.RoomA == a && l.RoomB == b) || (l.RoomA == b && l.RoomB == a)) return;
            floor.Links.Add(new Link { RoomA = a, RoomB = b });
        }

        // ---------------------------------------------------------------------------------------------
        // AdjacentAlongWall — strict shared-wall predicate.
        // ---------------------------------------------------------------------------------------------

        /// <summary>True iff the two footprints TOUCH along a shared wall: the Chebyshev edge gap is ≈ 0 on
        /// exactly one axis (|gap| &lt; TouchEps) AND their projections on the OTHER axis overlap by a positive
        /// span. Returns FALSE for any clear gap and FALSE for a corner-only kiss (both axes ≈ 0, neither with
        /// a real overlapping span). Same tile-space Chebyshev measure as Separate / EdgeGapTiles.</summary>
        public static bool AdjacentAlongWall(Room a, Room b)
        {
            if (a == null || b == null) return false;
            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float gapX = System.Math.Abs(ToTile(a.X) - ToTile(b.X)) - (aw + bw) * 0.5f;
            float gapY = System.Math.Abs(ToTile(a.Y) - ToTile(b.Y)) - (ah + bh) * 0.5f;

            // Vertical wall: touch on X, real overlapping span on Y (gapY < 0 ⇔ Y-projection overlaps).
            bool vertical = System.Math.Abs(gapX) < TouchEps && gapY < -TouchEps;
            // Horizontal wall: touch on Y, real overlapping span on X.
            bool horizontal = System.Math.Abs(gapY) < TouchEps && gapX < -TouchEps;
            return vertical || horizontal;   // mutually exclusive: a corner kiss satisfies neither
        }

        // ---------------------------------------------------------------------------------------------
        // DoorOnSharedWall — door position in TILE space.
        // ---------------------------------------------------------------------------------------------

        /// <summary>If the rooms are AdjacentAlongWall, output the door position in TILE space = the midpoint
        /// of the overlapping span along the shared wall, sitting on the shared edge coordinate; return true.
        /// Otherwise out = default and return false.</summary>
        public static bool DoorOnSharedWall(Room a, Room b, out LayoutPoint doorTile)
        {
            doorTile = default;
            if (!AdjacentAlongWall(a, b)) return false;

            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float axT = ToTile(a.X), ayT = ToTile(a.Y), bxT = ToTile(b.X), byT = ToTile(b.Y);
            float gapX = System.Math.Abs(axT - bxT) - (aw + bw) * 0.5f;

            if (System.Math.Abs(gapX) < TouchEps)
            {
                // Shared VERTICAL wall (touch on X): door runs along the overlapping Y span, at the wall's X.
                float aFace = axT + (bxT >= axT ? aw * 0.5f : -aw * 0.5f);
                float bFace = bxT + (bxT >= axT ? -bw * 0.5f : bw * 0.5f);
                float edgeX = (aFace + bFace) * 0.5f;
                float yLow = Max(ayT - ah * 0.5f, byT - bh * 0.5f);
                float yHigh = Min(ayT + ah * 0.5f, byT + bh * 0.5f);
                doorTile = new LayoutPoint { X = edgeX, Y = (yLow + yHigh) * 0.5f };
            }
            else
            {
                // Shared HORIZONTAL wall (touch on Y): door runs along the overlapping X span, at the wall's Y.
                float aFace = ayT + (byT >= ayT ? ah * 0.5f : -ah * 0.5f);
                float bFace = byT + (byT >= ayT ? -bh * 0.5f : bh * 0.5f);
                float edgeY = (aFace + bFace) * 0.5f;
                float xLow = Max(axT - aw * 0.5f, bxT - bw * 0.5f);
                float xHigh = Min(axT + aw * 0.5f, bxT + bw * 0.5f);
                doorTile = new LayoutPoint { X = (xLow + xHigh) * 0.5f, Y = edgeY };
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------------------------------

        /// <summary>Entrance = the TypeId==0 room with the lowest Id; if the floor has no entrance, the
        /// lowest-Id room. Deterministic root for both Arrange and the Settle fallback.</summary>
        static Room PickEntrance(InteriorFloor f)
        {
            Room ent = null;
            foreach (var r in f.Rooms)
                if (r.TypeId == 0 && (ent == null || r.Id < ent.Id)) ent = r;
            if (ent != null) return ent;
            foreach (var r in f.Rooms)
                if (ent == null || r.Id < ent.Id) ent = r;
            return ent;
        }

        /// <summary>BFS from <paramref name="root"/> over Links, placing each newly-reached room flush against
        /// the room it was reached through. Returns the rooms placed (root first). If recenterRoot, the root
        /// is moved to the field centre first (Arrange); otherwise it keeps its current position (Settle, so
        /// the pinned anchor never moves). Neighbour expansion is ascending-id for determinism.</summary>
        static List<Room> BfsPlaceCore(InteriorFloor f, Room root, bool recenterRoot)
        {
            var placed = new List<Room>();
            if (root == null) return placed;

            var adj = BuildAdjacency(f);
            if (recenterRoot) { root.X = 0.5f; root.Y = 0.5f; }
            placed.Add(root);
            var placedIds = new HashSet<int> { root.Id };

            var queue = new Queue<int>();
            queue.Enqueue(root.Id);
            while (queue.Count > 0)
            {
                var cur = f.GetRoom(queue.Dequeue());
                if (cur == null || !adj.TryGetValue(cur.Id, out var nbs)) continue;
                foreach (int nb in nbs)   // ascending id
                {
                    if (placedIds.Contains(nb)) continue;
                    var child = f.GetRoom(nb);
                    if (child == null) continue;
                    PlaceAgainst(child, cur, placed);
                    placed.Add(child);
                    placedIds.Add(nb);
                    queue.Enqueue(nb);
                }
            }
            return placed;
        }

        /// <summary>Undirected adjacency with each neighbour list sorted ascending — the sole source of BFS
        /// determinism (Link insertion order must not matter).</summary>
        static Dictionary<int, List<int>> BuildAdjacency(InteriorFloor f)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var r in f.Rooms) if (!adj.ContainsKey(r.Id)) adj[r.Id] = new List<int>();
            foreach (var l in f.Links)
            {
                if (l.RoomA == l.RoomB) continue;
                if (adj.ContainsKey(l.RoomA) && adj.ContainsKey(l.RoomB))
                {
                    if (!adj[l.RoomA].Contains(l.RoomB)) adj[l.RoomA].Add(l.RoomB);
                    if (!adj[l.RoomB].Contains(l.RoomA)) adj[l.RoomB].Add(l.RoomA);
                }
            }
            foreach (var kv in adj) kv.Value.Sort();
            return adj;
        }

        /// <summary>Place <paramref name="child"/> flush against <paramref name="parent"/>. Tries the four
        /// sides Right, Down, Left, Up at increasing outward distance d (d==0 = flush → a door; d&gt;0 = pushed
        /// out → a corridor). The child is centred on the parent's perpendicular axis, so the shared span is
        /// as large as possible. Takes the first side/distance whose footprint overlaps nothing already placed.
        /// Deterministic; writes child.X/Y normalized and clamped.</summary>
        static void PlaceAgainst(Room child, Room parent, List<Room> placed)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            var (pw, ph) = DungeonProjection.EffectiveSize(parent);
            float px = ToTile(parent.X), py = ToTile(parent.Y);
            float offX = (pw + cw) * 0.5f, offY = (ph + ch) * 0.5f;
            int max = DungeonLayout.TilesPerAxis;

            for (int d = 0; d <= max; d++)
            {
                for (int s = 0; s < 4; s++)
                {
                    float cx, cy, ux, uy;
                    switch (s)
                    {
                        case 0: cx = px + offX; cy = py; ux = 1f; uy = 0f; break;   // Right
                        case 1: cx = px; cy = py + offY; ux = 0f; uy = 1f; break;   // Down (+Y)
                        case 2: cx = px - offX; cy = py; ux = -1f; uy = 0f; break;  // Left
                        default: cx = px; cy = py - offY; ux = 0f; uy = -1f; break; // Up
                    }
                    cx += ux * d; cy += uy * d;
                    if (IsFree(cx, cy, cw, ch, placed))
                    {
                        child.X = Clamp01(ToNorm(cx));
                        child.Y = Clamp01(ToNorm(cy));
                        return;
                    }
                }
            }
            // Unreachable on a 128-tile field with sane footprints; still write something deterministic.
            child.X = Clamp01(ToNorm(px + offX));
            child.Y = Clamp01(ToNorm(py));
        }

        /// <summary>Place <paramref name="child"/> at the nearest free slot on an expanding cardinal search
        /// out from a point (used for rooms with no placed neighbour). Deterministic; d==0 tests the origin.</summary>
        static void PlaceOutwardFromPoint(Room child, float ox, float oy, List<Room> placed)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            int max = DungeonLayout.TilesPerAxis;
            for (int d = 0; d <= max; d++)
            {
                for (int s = 0; s < 4; s++)
                {
                    float cx = ox, cy = oy;
                    switch (s)
                    {
                        case 0: cx = ox + d; break;   // Right
                        case 1: cy = oy + d; break;   // Down
                        case 2: cx = ox - d; break;   // Left
                        default: cy = oy - d; break;  // Up
                    }
                    if (IsFree(cx, cy, cw, ch, placed))
                    {
                        child.X = Clamp01(ToNorm(cx));
                        child.Y = Clamp01(ToNorm(cy));
                        return;
                    }
                }
            }
            child.X = Clamp01(ToNorm(ox));
            child.Y = Clamp01(ToNorm(oy));
        }

        /// <summary>True iff a footprint of size (cw,ch) centred at TILE (cx,cy) overlaps NO room in
        /// <paramref name="placed"/>. Overlap = Chebyshev penetration &gt; OverlapEps on BOTH axes — the same
        /// condition Separate resolves; a flush touch (gap ≈ 0) is NOT an overlap, so flush placement is free.</summary>
        static bool IsFree(float cx, float cy, float cw, float ch, List<Room> placed)
        {
            foreach (var r in placed)
            {
                var (rw, rh) = DungeonProjection.EffectiveSize(r);
                float dx = System.Math.Abs(cx - ToTile(r.X)) - (cw + rw) * 0.5f;
                float dy = System.Math.Abs(cy - ToTile(r.Y)) - (ch + rh) * 0.5f;
                if (dx < -OverlapEps && dy < -OverlapEps) return false;
            }
            return true;
        }

        /// <summary>Anchor-and-tree-pinned overlap resolution: only rooms in <paramref name="movable"/> move.
        /// For each overlapping (movable, other) pair, shove the MOVABLE room fully clear along the axis of
        /// least penetration — Separate's least-penetration rule, one-sided so the pinned tree stays flush.
        /// Bounded iterations; deterministic (movable and the room list both walked in ascending id).</summary>
        static void ResolveOverlapsMovableOnly(InteriorFloor f, List<Room> movable)
        {
            if (movable.Count == 0) return;
            var all = SortedById(f.Rooms);
            const int maxIterations = 60;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool moved = false;
                foreach (var m in movable)
                {
                    var (mw, mh) = DungeonProjection.EffectiveSize(m);
                    foreach (var other in all)
                    {
                        if (other.Id == m.Id) continue;
                        var (ow, oh) = DungeonProjection.EffectiveSize(other);
                        float mx = ToTile(m.X), my = ToTile(m.Y);
                        float dx = mx - ToTile(other.X), dy = my - ToTile(other.Y);
                        float overlapX = (mw + ow) * 0.5f - System.Math.Abs(dx);
                        float overlapY = (mh + oh) * 0.5f - System.Math.Abs(dy);
                        if (overlapX <= OverlapEps || overlapY <= OverlapEps) continue;   // touching/clear
                        moved = true;
                        // Full one-sided shove (the pinned side does not give) plus ShoveClearance — just
                        // enough to clear round-trip noise, not enough to push the pair out of "touching"
                        // (see ShoveClearance's comment). The room lands FLUSH, like a generated one.
                        if (overlapX < overlapY)
                            mx += (overlapX + ShoveClearance) * (dx >= 0f ? 1f : -1f);
                        else
                            my += (overlapY + ShoveClearance) * (dy >= 0f ? 1f : -1f);
                        m.X = Clamp01(ToNorm(mx));
                        m.Y = Clamp01(ToNorm(my));
                    }
                }
                if (!moved) break;
            }
        }

        static List<Room> SortedById(List<Room> rooms)
        {
            var copy = new List<Room>(rooms);
            copy.Sort((p, q) => p.Id.CompareTo(q.Id));
            return copy;
        }

        static bool Contains(List<Room> list, Room r)
        {
            foreach (var x in list) if (ReferenceEquals(x, r)) return true;
            return false;
        }

        static float Min(float a, float b) => a < b ? a : b;
        static float Max(float a, float b) => a > b ? a : b;
    }
}
