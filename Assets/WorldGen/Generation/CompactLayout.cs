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

        /// <summary>Width (tiles) of the opening the renderer carves out of a shared wall at a door — the ONE
        /// place that number lives (DungeonFlatRenderer's DoorGapTiles reads this constant, so the drawn door and
        /// the packer's idea of "enough wall for a door" can never drift apart). The lateral slide uses it as the
        /// MINIMUM shared-wall span a flush pair may keep: slide a room further along the wall than that and the
        /// door its Link renders as would no longer fit on the wall it is carved into.</summary>
        public const float DoorGapTiles = 1.4f;

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
            var placed = BfsPlaceCore(floor, root);

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
        /// Every phase generates its candidates through the same slot generator (<see cref="TrySeatAtOffset"/>),
        /// which offers each side of an anchor CENTRED first and then SLID along the shared wall by ±1, ±2 … tiles
        /// while the pair still shares a <see cref="DoorGapTiles"/>-wide span (F4). Without the slide every
        /// candidate keeps the anchor's other coordinate, so the reachable positions form a plus-shaped lattice
        /// radiating from the column and a lobe of an L / T / stepped contour that can only be entered by a room
        /// flush against its neighbour but SHIFTED along their shared wall is unreachable at every distance — the
        /// reported "«Комнаты: 2 из 2»" with a whole empty lobe well inside the contour.
        /// Within phases 2 and 3, each distance tries the anchors the room is ALREADY LINKED to before any other
        /// anchor (see <see cref="SeatAgainstAnyPlaced"/>): a room's original links survive packing, so seating it
        /// next to an unrelated room leaves those links to render as long routed corridors — exactly the
        /// "detached rooms joined by corridors" look this packer exists to avoid.
        /// The pipeline is run up to THREE times and the run that keeps MOST rooms wins (ties → the earliest, i.e.
        /// the most compact-looking one):
        ///   • COMPACT+SLIDE run — phase 1 seats flush only (d == 0), and every phase may slide. Packs tighter, so
        ///     it usually leaves one big contiguous pocket for phase 2 rather than several unusable slivers; this
        ///     is what actually raises the cap, and it is the run that reaches an L's far lobe.
        ///   • SPREAD run — phase 1 is the PRE-FIX search (increasing d against the BFS parent): the same BFS, the
        ///     same neighbour/side/distance orders and the same two accept predicates, PLUS the sound bounding-box
        ///     pre-rejection of <see cref="TrySeatAtDistance"/> (which never discards a slot ContainsRect would
        ///     have accepted), and NO lateral slide anywhere.
        ///   • COMPACT run — flush-only seeding, NO slide anywhere.
        /// The last two are exactly the two runs the pre-F4 packer took, so best-of-three is ≥ what the pre-F4
        /// packer would have kept on ANY input — by construction, not by measurement. That guarantee has to be
        /// bought this way: the slide only ever ADDS candidates (offset 0 is still tried first and still wins), but
        /// a packer that seats rooms greedily is not monotone in its candidate set — one room taking a newly
        /// reachable slot can split the space so that two later rooms no longer fit. Measured: with the slid runs
        /// alone, 34 of 1200 corpus contours ended with a LOWER «из N» than before (worst −2) while 270 rose; with
        /// the slide-free run in the mix, 0 fall and 270 rise (sum 10234 vs 9940). That first figure is re-derived
        /// by the harness variant NoPlainRunLayout — runs 1+2 only — as the sweep's "(f) vs (e)" tally.
        /// Older evidence for the first two runs is unchanged, but note WHICH pipeline it is about: over 2880 packs
        /// the SLIDE-FREE compact run alone (harness variant CompactNoSlideLayout, the sweep's "(c') compact, no
        /// slide" column — NOT CompactOnlyLayout, which since F4 is the compact+SLIDE pipeline and is reported
        /// separately as the "(c'') compact+slide" column) regressed against the ORIGINAL packer on 2.7%
        /// of them (77/2880, worst −3) — which is why it is a FALLBACK and not the only run. The companion
        /// figure "best-of-two beat spread-only on 8.1% of packs and 12.3% of caps" is a PRE-F4 measurement of a
        /// pipeline that no longer exists as a column; today's (c) is best-of-three WITH the slide and beats
        /// spread-only on 16.6% of packs (478/2880) and 33.7% of caps (404/1200) — quoted verbatim from the
        /// sweep's own "(c) vs (b)" lines (M-2; see RunPacks/RunCaps in Sweep.cs).
        /// Each run after the first is SKIPPED once some run has placed every room — it could then only tie, and
        /// ties go to the earlier run anyway, so this is a pure speed-up with a bit-identical result.
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

            var compact = RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds,
                seedMaxDistance: 0, slide: true);
            // A later run can at best MATCH one that already placed everything, and ties go to the earlier run —
            // so skipping it there is a pure speed-up with a bit-identical result.
            var spread = compact.Ids.Count >= ordered.Count
                ? null
                : RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds,
                    seedMaxDistance: DungeonLayout.TilesPerAxis, slide: false);
            var chosen = spread != null && spread.Ids.Count > compact.Ids.Count ? spread : compact;
            // RUN 3 — the pre-F4 packer's OWN compact run, slide and all removed. With the spread run above it,
            // the PAIR reproduces that packer exactly, so max(run 2, run 3) IS what the build the DM has would
            // have kept. That identity is the entire non-regression proof: it is the only reason "F4 can never
            // report a smaller «из N» than the build the DM has" is structural rather than a hope (see the
            // summary above for why the slide alone cannot promise it).
            // NOT a speed/quality knob — do NOT delete this run to buy back its cost. Measured consequence of
            // deleting it: 34 of 1200 corpus contours report a LOWER cap than the DM's current build, worst -2
            // (harness variant NoPlainRunLayout, the sweep's "(f) vs (e)" tally). The COST side is re-measured the
            // same way (M-3): Perf.cs times NoPlainRunLayout — this run missing, nothing else changed — against
            // SHIPPED on the same realistic contours it already reports on, so the percentage comes from the
            // CURRENT tree, not a two-run intermediate build that no longer exists. One representative `perf` run
            // put it at +23-38% per call on the 8/10-room contours the DM waits on (see the F4 report's §14/M-3
            // for that run's exact table) — but per-call wall-clock timing on these sub-25ms calls is genuinely
            // noisy run to run (observed swinging from roughly -25% to +250% on individual rows across repeat
            // runs), so treat the SIGN (run 3 costs something, it is not free) as the load-bearing fact and any
            // single percentage, including this one, as illustrative rather than a contract.
            var plain = chosen.Ids.Count >= ordered.Count
                ? null
                : RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds,
                    seedMaxDistance: 0, slide: false);
            if (plain != null && plain.Ids.Count > chosen.Ids.Count) chosen = plain;
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
        /// <paramref name="slide"/> enables F4's lateral slide for every phase of this run; false makes the whole
        /// run bit-for-bit the pre-F4 pipeline, which is what the two fallback runs are for.
        /// Writes positions onto the rooms as it goes (the phases must measure real geometry) and snapshots them
        /// at the end; the caller keeps only the winning snapshot. Deterministic.</summary>
        static PackResult RunPhases(InteriorFloor floor, Room column, List<Room> ordered,
            Dictionary<int, List<int>> adj, InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int seedMaxDistance, bool slide)
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
                        seated = TrySeatAtDistance(child, cur, d, res.Placed, contourFloor, margin, bounds, slide);
                    if (!seated) continue;
                    res.Placed.Add(child);
                    res.Ids.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // --- Phase 2: fill the space phase 1 left — flush against ANY placed room. --------------------
            FillSweeps(res, ordered, adj, contourFloor, margin, bounds, maxDistance: 0, flushDoneSeq: 0, slide: slide);

            // --- Phase 3: relaxed fallback — outward rays against any placed room. ------------------------
            // Skipped outright when phase 1+2 already placed everything: there is nothing left to seat, and this
            // is the phase whose 129-distance scan dominates the packer's cost.
            // flushDoneSeq: phase 2 ran the FLUSH (d == 0) search to a fixpoint, so every room still unplaced has
            // already been tried at d == 0 — centred AND slid — against every one of the res.Placed.Count anchors
            // that exist right now, and none of those can start working (an anchor never moves, the contour never
            // changes and IsFree only tightens). Phase 3 therefore skips d == 0 for exactly those anchors and
            // spends its flush pass only on anchors IT places. That matters because d == 0 is the one distance the
            // F4 slide widens, so re-walking it here would pay the slide's whole cost a second time for nothing.
            if (res.Ids.Count < ordered.Count)
                FillSweeps(res, ordered, adj, contourFloor, margin, bounds,
                    maxDistance: DungeonLayout.TilesPerAxis, flushDoneSeq: res.Placed.Count, slide: slide);

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
        /// of being re-sorted for every room.
        ///
        /// A re-sweep only ever re-tries a room against the anchors placed SINCE its last attempt (each anchor
        /// carries its placement index; <c>triedUpTo</c> remembers how many existed when the room was last
        /// swept). That is EXACT, not a heuristic: within one run an anchor never moves and the contour never
        /// changes, so a candidate's position, its bbox test and its ContainsRect test are all identical on a
        /// later sweep, while <see cref="IsFree"/> can only get STRICTER (placed rooms are only ever added).
        /// A room that failed against an anchor therefore still fails against it, and skipping it cannot change
        /// which slot wins — only how long it takes to find it. It matters because the sweep is a fixpoint: a
        /// room that fits nowhere used to be re-scanned against every anchor once per placement, and F4's slide
        /// multiplied the cost of each of those scans by the width of the offset ladder. Deterministic.</summary>
        static void FillSweeps(PackResult res, List<Room> ordered, Dictionary<int, List<int>> adj,
            InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int maxDistance, int flushDoneSeq, bool slide)
        {
            if (res.Ids.Count >= ordered.Count) return;   // everything is already placed — nothing to sweep
            var ctx = new SweepContext
            {
                Anchors = SortedById(res.Placed),   // ascending id — the fixed anchor order, kept sorted below
                Adj = adj,
                Placed = res.Placed,
                ContourFloor = contourFloor,
                Margin = margin,
                Bounds = bounds,
                MaxDistance = maxDistance,
                FlushDoneSeq = flushDoneSeq,
                Slide = slide,
            };
            for (int i = 0; i < res.Placed.Count; i++) ctx.Seq[res.Placed[i].Id] = i;
            // How many anchors each unplaced room has already been tried against. Local to this call, like
            // ctx.Seq: phase 3 widens the distance range, so it must re-try every anchor.
            var triedUpTo = new Dictionary<int, int>();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var room in ordered)   // ascending id
                {
                    if (res.Ids.Contains(room.Id)) continue;
                    int minSeq;
                    if (!triedUpTo.TryGetValue(room.Id, out minSeq)) minSeq = 0;
                    triedUpTo[room.Id] = res.Placed.Count;   // recorded BEFORE this room can join the anchors
                    var anchor = SeatAgainstAnyPlaced(room, minSeq, ctx);
                    if (anchor == null) continue;
                    ctx.Seq[room.Id] = res.Placed.Count;
                    res.Placed.Add(room);
                    res.Ids.Add(room.Id);
                    InsertById(ctx.Anchors, room);
                    res.LinkA.Add(anchor.Id); res.LinkB.Add(room.Id);
                    progress = true;
                }
                if (res.Ids.Count >= ordered.Count) return;   // every room placed — no further sweep can do anything
            }
        }

        /// <summary>Everything ONE <see cref="FillSweeps"/> call holds fixed while it sweeps: the anchor list and
        /// its placement indices, the four reusable preference-group buffers, the two accept predicates' inputs,
        /// and this phase's distance/slide settings. Built once per FillSweeps call and handed to
        /// <see cref="SeatAgainstAnyPlaced"/> whole. The alternative was 16 positional parameters — FIVE of them
        /// <c>List&lt;Room&gt;</c> in a row (the two preference groups, the two flush-filtered groups, and the
        /// placed set), where transposing any two would compile silently and mis-pack. <c>Placed</c> and
        /// <c>Anchors</c> are live references that GROW as the sweep seats rooms; the buffers are scratch,
        /// refilled at the top of every Seat call.</summary>
        sealed class SweepContext
        {
            public List<Room> Anchors;                                               // res.Placed, ascending id
            public readonly Dictionary<int, int> Seq = new Dictionary<int, int>();   // anchor id -> placement index
            public readonly List<Room> LinkedAnchors = new List<Room>();             // scratch: the two preference
            public readonly List<Room> OtherAnchors = new List<Room>();              // groups, ascending id
            public readonly List<Room> LinkedFlush = new List<Room>();               // the same split, minus the
            public readonly List<Room> OtherFlush = new List<Room>();                // anchors already flush-tested
            public Dictionary<int, List<int>> Adj;
            public List<Room> Placed;
            public InteriorFloor ContourFloor;
            public float Margin;
            public (float minX, float minY, float maxX, float maxY) Bounds;
            public int MaxDistance;    // 0 = flush only (phase 2); TilesPerAxis = the outward rays (phase 3)
            public int FlushDoneSeq;   // anchors with a placement index below this were already searched at d == 0
            public bool Slide;
        }

        /// <summary>Seat <paramref name="room"/> against the already-placed rooms, nearest distance first: for
        /// each outward distance d (0 = flush) and — at d == 0 — each lateral offset along the shared wall
        /// (magnitude 0, then +1, −1, +2, −2 …), the anchors the room is ALREADY LINKED to (ascending id) and then
        /// every other placed room (ascending id), four sides each. Returns the anchor it was seated against (and
        /// writes room.X/Y), or null when no slot passes both predicates.
        /// DISTANCE-outer so a flush (door) slot always wins over a pushed-out (corridor) one at ANY anchor, and
        /// OFFSET-outer inside d == 0 for the same reason one level down: a CENTRED slot at a later anchor beats a
        /// slid one at an earlier anchor, so the whole pre-F4 d == 0 scan runs first and returns exactly what it
        /// used to whenever it succeeded — the slid candidates are only ever reached once every centred one has
        /// failed at every anchor.
        /// LINKED-anchor first because the room's own links survive packing: seating it next to an unrelated room
        /// leaves those links to route as long corridors across the floor, which is the look this packer exists to
        /// avoid. <c>ctx.Anchors</c> is <c>ctx.Placed</c> in ascending id, maintained by the caller; it is split
        /// ONCE per call into the two preference groups, using the context's reusable
        /// <c>LinkedAnchors</c>/<c>OtherAnchors</c> buffers — skipping, as that split is
        /// made, every anchor whose placement index (<c>ctx.Seq</c>) is below <paramref name="minSeq"/>,
        /// i.e. every anchor this room has already been tried against and provably still fails against (see
        /// <see cref="FillSweeps"/> for why that is exact). The d == 0 pass narrows that further to the anchors
        /// newer than <c>ctx.FlushDoneSeq</c> — phase 3's way of not re-walking the flush search phase 2
        /// already ran to a fixpoint (same exactness argument; see the call site).
        /// The distance loop
        /// stops at <see cref="MaxUsefulDistance"/> instead of the full TilesPerAxis: past it every candidate is
        /// outside the footprint's bounding box, so this only skips work that could not have succeeded — the same
        /// exactness the box pre-test in <see cref="TrySeatAtDistance"/> relies on. Deterministic.</summary>
        static Room SeatAgainstAnyPlaced(Room room, int minSeq, SweepContext ctx)
        {
            List<int> linked = null;
            if (ctx.Adj != null) ctx.Adj.TryGetValue(room.Id, out linked);

            ctx.LinkedAnchors.Clear(); ctx.OtherAnchors.Clear();   // scratch; all four keep the ascending-id order
            ctx.LinkedFlush.Clear(); ctx.OtherFlush.Clear();
            foreach (var anchor in ctx.Anchors)
            {
                int aSeq;
                if (!ctx.Seq.TryGetValue(anchor.Id, out aSeq)) aSeq = 0;
                if (aSeq < minSeq) continue;
                bool isLinked = linked != null && linked.Contains(anchor.Id);
                if (isLinked) ctx.LinkedAnchors.Add(anchor); else ctx.OtherAnchors.Add(anchor);
                if (aSeq < ctx.FlushDoneSeq) continue;   // already searched at d == 0 (phase 2's fixpoint)
                if (isLinked) ctx.LinkedFlush.Add(anchor); else ctx.OtherFlush.Add(anchor);
            }
            if (ctx.LinkedAnchors.Count == 0 && ctx.OtherAnchors.Count == 0) return null;   // no anchor is new since last time

            // Phase 2 (MaxDistance == 0) only ever clamps the result to 0 (or leaves it at -1 when even d == 0
            // is hopeless), so paying for the full four-side/every-anchor scan there is wasted work — skip
            // straight to 0 and let TrySeatAtDistance's own bbox pre-test reject the rare hopeless case cheaply.
            // Both bounds below are taken over the anchors ACTUALLY being tried: each is a max over its input, so
            // narrowing the input can only drop distances/offsets that belong to skipped anchors.
            int limit = ctx.MaxDistance == 0 ? 0
                : Larger(MaxUsefulDistance(room, ctx.LinkedAnchors, ctx.Bounds),
                         MaxUsefulDistance(room, ctx.OtherAnchors, ctx.Bounds));
            if (limit > ctx.MaxDistance) limit = ctx.MaxDistance;
            int maxSlide = ctx.Slide ? Larger(MaxSlideTiles(room, ctx.LinkedFlush), MaxSlideTiles(room, ctx.OtherFlush)) : 0;
            for (int d = 0; d <= limit; d++)
            {
                // d == 0 is the flush pass — the one the F4 offset ladder widens, and the one phase 2 may already
                // have exhausted for the older anchors.
                var linkedNow = d == 0 ? ctx.LinkedFlush : ctx.LinkedAnchors;
                var otherNow = d == 0 ? ctx.OtherFlush : ctx.OtherAnchors;
                int maxK = d == 0 ? maxSlide : 0;
                for (int k = 0; k <= maxK; k++)
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        int t = k * sign;
                        foreach (var anchor in linkedNow)
                            if (TrySeatAtOffset(room, anchor, d, t, ctx.Placed, ctx.ContourFloor, ctx.Margin, ctx.Bounds)) return anchor;
                        foreach (var anchor in otherNow)
                            if (TrySeatAtOffset(room, anchor, d, t, ctx.Placed, ctx.ContourFloor, ctx.Margin, ctx.Bounds)) return anchor;
                        if (k == 0) break;   // +0 and −0 are the same candidate
                    }
            }
            return null;
        }

        /// <summary>Largest outward distance d at which ANY anchor could still put <paramref name="room"/> inside
        /// the footprint bounding box, or -1 when even d = 0 is hopeless. A candidate on the Right side sits at
        /// <c>px + offX + d</c>, so it fits the box only while <c>d ≤ maxX − px − offX − w/2</c>; the other three
        /// sides give the mirrored bounds. Taking the best over the four sides and over every anchor is therefore
        /// an EXACT cut-off — every larger d is rejected by the box pre-test anyway. It matters because phase 3's
        /// nominal range is 0..TilesPerAxis (129 distances) while a real floor-0 footprint spans ~20-30 tiles.
        ///
        /// UNCHANGED by F4's lateral slide, and it does not need re-deriving for the slid geometry: each of the
        /// four terms constrains the candidate's ALONG-axis coordinate (cx for Right/Left, cy for Down/Up), while
        /// a slide moves only the PERPENDICULAR one — so the largest d that can put a candidate inside the box is
        /// the same slid or not. The one place the two could interact is the −1 ("even d = 0 is hopeless") result:
        /// it means every side of every anchor already overshoots the box AT d = 0 on its along-axis face, e.g.
        /// px + offX + cw/2 &gt; maxX on the Right, which a lateral slide cannot repair either — so skipping the
        /// loop entirely still drops only candidates the box pre-test would have rejected.</summary>
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

        /// <summary>Try every LATERAL OFFSET of <paramref name="anchor"/>'s four sides at the FIXED outward
        /// distance <paramref name="d"/>, nearest-to-centred first, taking the first candidate that passes both
        /// accept predicates. Offset magnitudes run 0, 1, 2, … and each magnitude k is tried as +k then −k, so
        /// the CENTRED slot (the compact-looking one, and the only one the pre-F4 packer could produce) always
        /// wins when it is valid: iteration k = 0 IS the old four-side switch, unchanged, and every slid
        /// candidate comes strictly after it. <paramref name="slideFlush"/> false, or d &gt; 0, reduces this to
        /// exactly that old switch (see <see cref="TrySeatAtOffset"/> for why the slide is flush-only).
        /// Used by phase 1, which seats a child against ONE anchor. Deterministic.</summary>
        static bool TrySeatAtDistance(Room child, Room anchor, int d, List<Room> placed,
            InteriorFloor contourFloor, float margin, (float minX, float minY, float maxX, float maxY) bounds,
            bool slideFlush)
        {
            int maxK = slideFlush && d == 0 ? MaxSlideTiles(child, anchor) : 0;
            for (int k = 0; k <= maxK; k++)
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (TrySeatAtOffset(child, anchor, d, k * sign, placed, contourFloor, margin, bounds)) return true;
                    if (k == 0) break;   // +0 and −0 are the same candidate
                }
            return false;
        }

        /// <summary>Try the four sides (Right, Down, Left, Up) of <paramref name="anchor"/> at the FIXED outward
        /// distance <paramref name="d"/> (0 = flush, so the pair shares a wall) and the FIXED lateral offset
        /// <paramref name="t"/> ALONG that shared wall (tiles; 0 = centred on the anchor's perpendicular axis,
        /// which is the only candidate the packer had before F4), taking the first candidate that is free of
        /// overlap AND inside the footprint. Writes child.X/Y normalized+clamped and returns true on success;
        /// leaves the child untouched otherwise. <paramref name="bounds"/> is the footprint's bounding box: a
        /// candidate poking outside it can NEVER be inside the footprint union, so rejecting it there is exact
        /// and skips the arrangement work for the far-out rays of phase 3 — and it prunes an off-shape slid
        /// candidate just as cheaply, since the slide moves the candidate's box too.
        ///
        /// The slide is what lets a floor turn the corner of an L / T / stepped contour: without it every
        /// candidate keeps the anchor's OTHER coordinate (cy == py for Right/Left, cx == px for Down/Up), so the
        /// reachable positions form a plus-shaped lattice radiating from the pinned column and a lobe that needs
        /// a room flush against its neighbour but SHIFTED along their shared wall is unreachable at every
        /// distance — the reported "MAX = 2 with an empty lobe" defect.
        ///
        /// A non-zero <paramref name="t"/> is only ever offered at d == 0 (the caller's gate) and only within
        /// <see cref="MaxSlideTiles"/>: at d &gt; 0 the pair does not touch at all (that link renders as a
        /// corridor, and a lateral offset there would just be a second, redundant way to spell the same free
        /// slot), and past the slide bound the pair would share less wall than a door needs. Deterministic.</summary>
        static bool TrySeatAtOffset(Room child, Room anchor, int d, int t, List<Room> placed,
            InteriorFloor contourFloor, float margin, (float minX, float minY, float maxX, float maxY) bounds)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            var (pw, ph) = DungeonProjection.EffectiveSize(anchor);
            float px = ToTile(anchor.X), py = ToTile(anchor.Y);
            float offX = (pw + cw) * 0.5f, offY = (ph + ch) * 0.5f;
            // The pair's shared wall runs along Y on the Right/Left sides and along X on the Down/Up ones, so
            // each pair of sides has its OWN slide bound (the extents that overlap differ). A magnitude that
            // outruns one of them simply skips those sides.
            int slideY = t == 0 ? 0 : MaxSlideAlong(ph, ch);
            int slideX = t == 0 ? 0 : MaxSlideAlong(pw, cw);
            int mag = t < 0 ? -t : t;
            for (int s = 0; s < 4; s++)
            {
                if (mag > (s == 0 || s == 2 ? slideY : slideX)) continue;
                float cx, cy;
                switch (s)
                {
                    case 0: cx = px + offX + d; cy = py + t; break;   // Right
                    case 1: cx = px + t; cy = py + offY + d; break;   // Down (+Y)
                    case 2: cx = px - offX - d; cy = py + t; break;   // Left
                    default: cx = px + t; cy = py - offY - d; break;  // Up
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

        /// <summary>Largest lateral slide (whole tiles) a flush pair of extents <paramref name="pExtent"/> and
        /// <paramref name="cExtent"/> ALONG their shared wall may take without making the door on that wall any
        /// harder to fit than it already was. Sliding by t leaves a shared span of
        /// <c>span(t) = min(p,c) − max(0, |t| − |p−c| / 2)</c>; the bound is <c>|t| ≤ (p + c) / 2 − DoorGapTiles</c>,
        /// floored to whole tiles (the offsets are integer tile steps) and clamped at 0, so the CENTRED candidate
        /// is never withdrawn — the bound may only ever remove candidates the pre-F4 packer did not have.
        ///
        /// The invariant that bound actually enforces is <c>span(t) ≥ min(span(0), DoorGapTiles)</c> — NOT
        /// span(t) ≥ DoorGapTiles unconditionally. Two regimes:
        ///   • FAT, min(p,c) ≥ DoorGapTiles — there it IS an iff: span(t) ≥ D ⟺ |t| ≤ |p−c|/2 + min(p,c) − D
        ///     = (p+c)/2 − D (the two forms agree because |p−c|/2 + min(p,c) = (p+c)/2). Every admissible slide
        ///     keeps a full door's worth of wall. This is the normal case — generated rooms roll 4..8 tiles.
        ///   • THIN, min(p,c) &lt; DoorGapTiles — REACHABLE: <see cref="RoomSizing.MinSide"/> is 1 and the
        ///     inspector lets the DM set a 1-tile side. E.g. p=6, c=1 → bound = floor(3.5 − 1.4) = 2, while
        ///     span(0) = span(2) = 1 &lt; 1.4, so the "iff" reading is false there. What holds instead is
        ///     stronger where it matters: since (p+c)/2 − D = |p−c|/2 + (min(p,c) − D) &lt; |p−c|/2 when
        ///     min(p,c) &lt; D, EVERY admissible |t| sits inside the plateau |t| ≤ |p−c|/2 on which
        ///     span(t) == span(0) — a thin pair's slide does not shrink the shared wall AT ALL.
        /// So in both regimes the slid slot's door is no worse than the centred slot's, which is the property
        /// the packer needs (the centred slot is what the pre-F4 packer would have used), and a corner kiss stays
        /// impossible: span ≥ min(p,c) ≥ RoomSizing.MinSide = 1 &gt; 0. Clamping the bound to 0 in the thin regime
        /// would make the literal "iff" true, but only by deleting slid candidates that are provably no worse
        /// than the centred one — no safety is bought, so the comment is what was corrected, not the code.
        /// Round-off-safe: extents are integers, so (p+c)/2 is a multiple of 0.5 and (p+c)/2 − 1.4 is never
        /// itself an integer.</summary>
        static int MaxSlideAlong(float pExtent, float cExtent)
        {
            float lim = (pExtent + cExtent) * 0.5f - DoorGapTiles;
            return lim < 0f ? 0 : (int)System.Math.Floor(lim);
        }

        /// <summary>The largest slide magnitude worth trying for this child/anchor pair — the looser of the two
        /// per-side bounds (the Right/Left sides share a wall along Y, the Down/Up ones along X); the tighter
        /// side is skipped per-candidate inside <see cref="TrySeatAtOffset"/>.</summary>
        static int MaxSlideTiles(Room child, Room anchor)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            var (pw, ph) = DungeonProjection.EffectiveSize(anchor);
            int y = MaxSlideAlong(ph, ch), x = MaxSlideAlong(pw, cw);
            return x > y ? x : y;
        }

        /// <summary>The largest slide magnitude worth trying for <paramref name="room"/> against ANY of the
        /// anchors — the offset loop in <see cref="SeatAgainstAnyPlaced"/> is OUTSIDE the anchor loop (so a
        /// centred slot at a later anchor still beats a slid one at an earlier anchor), which means it needs one
        /// bound covering all of them; each anchor's own bound is re-applied per candidate.</summary>
        static int MaxSlideTiles(Room room, List<Room> anchors)
        {
            int best = 0;
            foreach (var anchor in anchors)
            {
                int k = MaxSlideTiles(room, anchor);
                if (k > best) best = k;
            }
            return best;
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
        /// a real overlapping span). Same tile-space Chebyshev measure as Separate / EdgeGapTiles.
        ///
        /// The RENDERER no longer calls this (doors come from RoomLinkGeometry), so its only remaining callers
        /// are the self-tests — but it is deliberately KEPT: it is the DEFINITION of "flush" that ~15
        /// assertions across Arrange / AttachNewRoom / the lateral slide / column packing pin, and deleting it
        /// would mean re-deriving the same Chebyshev predicate inside the test file, where it could silently
        /// drift from the TouchEps the packer actually uses.</summary>
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
        // Internals
        // ---------------------------------------------------------------------------------------------

        /// <summary>Entrance = the TypeId==0 room with the lowest Id; if the floor has no entrance, the
        /// lowest-Id room. Arrange's deterministic root.</summary>
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
        /// the room it was reached through, with the root re-centred on the field first. Returns the rooms
        /// placed (root first). Neighbour expansion is ascending-id for determinism. (It used to carry a
        /// `recenterRoot: false` mode for the removed Settle primitive; Arrange is the only caller now, so the
        /// re-centre is unconditional.)</summary>
        static List<Room> BfsPlaceCore(InteriorFloor f, Room root)
        {
            var placed = new List<Room>();
            if (root == null) return placed;

            var adj = BuildAdjacency(f);
            root.X = 0.5f; root.Y = 0.5f;
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

        static int Larger(int a, int b) => a > b ? a : b;
    }
}
