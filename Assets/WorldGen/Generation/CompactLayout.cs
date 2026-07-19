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
        /// future manual tidy / generation-time re-pack. Rebuild
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
        // ArrangeWithin / NudgeRoomToward — nest a floor inside a tile-space box (multi-floor coherence).
        // ---------------------------------------------------------------------------------------------

        // A footprint bbox counts as FITTING a target box on an axis when it is no wider than the box beyond
        // this slack (tiles) — generous vs the ToNorm/ToTile round-trip, far below one whole tile.
        const float FitEps = 1e-3f;

        /// <summary>Adjacency-first <see cref="Arrange"/>, THEN rigidly translate EVERY room by a single
        /// offset so the whole floor's footprint bbox is centred inside the tile-space box
        /// [minX,maxX]×[minY,maxY]. Room SIZES and relative positions are untouched — a smaller building is
        /// made of FEWER rooms, never squashed ones, so this only ever slides the packed cluster; it does not
        /// scale it. Returns TRUE iff the arranged bbox actually fits the box on BOTH axes (extent ≤ box
        /// extent + FitEps); FALSE means the packed floor is too big for the box and the CALLER must
        /// regenerate with fewer rooms. Deterministic; pure tile space (reads TilesPerAxis + EffectiveSize
        /// through <see cref="DungeonProjection.ContentBoundsTiles"/>, never a hardcoded field size).</summary>
        public static bool ArrangeWithin(InteriorFloor floor, float minX, float minY, float maxX, float maxY)
        {
            Arrange(floor);
            if (floor == null || floor.Rooms.Count == 0) return true;

            var (bMinX, bMinY, bMaxX, bMaxY) = DungeonProjection.ContentBoundsTiles(floor);
            bool fits = (bMaxX - bMinX) <= (maxX - minX) + FitEps
                     && (bMaxY - bMinY) <= (maxY - minY) + FitEps;

            // Slide the cluster so its bbox centre lands on the box centre (as centred as possible even when
            // it does not fit, so an overflowing floor still straddles the box rather than escaping to one
            // side — the caller reduces the room count and tries again).
            float dx = (minX + maxX) * 0.5f - (bMinX + bMaxX) * 0.5f;
            float dy = (minY + maxY) * 0.5f - (bMinY + bMaxY) * 0.5f;
            TranslateAllTiles(floor, dx, dy);
            return fits;
        }

        /// <summary>Rigidly translate the WHOLE floor (single offset — every shared wall preserved) so
        /// <paramref name="roomId"/>'s centre moves as close as possible to (targetXTiles, targetYTiles)
        /// WITHOUT the floor's footprint bbox leaving [minX,maxX]×[minY,maxY]. Returns the residual
        /// (|dx|,|dy| in tiles) between that room's centre and the target after the clamped move — 0 when the
        /// box had enough slack to align exactly. Used to sit an upper floor's stair room "roughly above" the
        /// lower floor's while keeping the upper floor nested in the lower footprint. Deterministic.</summary>
        public static (float dxTiles, float dyTiles) NudgeRoomToward(InteriorFloor floor, int roomId,
            float targetXTiles, float targetYTiles, float minX, float minY, float maxX, float maxY)
        {
            if (floor == null || floor.Rooms.Count == 0) return (0f, 0f);
            var room = floor.GetRoom(roomId);
            if (room == null) return (0f, 0f);

            var (bMinX, bMinY, bMaxX, bMaxY) = DungeonProjection.ContentBoundsTiles(floor);
            float rcx = ToTile(room.X), rcy = ToTile(room.Y);

            // Translation range that keeps the bbox inside the box: [box.min - bbox.min, box.max - bbox.max].
            float desiredX = targetXTiles - rcx, desiredY = targetYTiles - rcy;
            float dx = ClampTranslate(desiredX, minX - bMinX, maxX - bMaxX);
            float dy = ClampTranslate(desiredY, minY - bMinY, maxY - bMaxY);
            TranslateAllTiles(floor, dx, dy);

            return (System.Math.Abs(desiredX - dx), System.Math.Abs(desiredY - dy));
        }

        /// <summary>Clamp a 1-D translation into [lo,hi]. When lo &gt; hi the bbox is WIDER than the box on
        /// this axis (no offset can contain it) — centre it instead (midpoint = boxCentre − bboxCentre).</summary>
        static float ClampTranslate(float desired, float lo, float hi)
        {
            if (lo > hi) return (lo + hi) * 0.5f;
            return desired < lo ? lo : (desired > hi ? hi : desired);
        }

        /// <summary>Slide every room by one TILE-space offset (converted to normalized). Preserves all shared
        /// walls; Clamp01 is a defensive no-op here (nested content sits well inside the field).</summary>
        static void TranslateAllTiles(InteriorFloor floor, float dxTiles, float dyTiles)
        {
            float dnx = ToNorm(dxTiles), dny = ToNorm(dyTiles);
            foreach (var r in floor.Rooms) { r.X = Clamp01(r.X + dnx); r.Y = Clamp01(r.Y + dny); }
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
        /// <see cref="Arrange"/> / <see cref="ArrangeWithin"/> — not an interaction one) and does NOT contain
        /// the room to any contour: a room parked outside the ground-floor contour is LEFT there for the C2'
        /// red-flag, since out-of-contour is a deliberate DM choice, not an error to auto-fix. A room dropped
        /// in free space is not moved at all (no overlap ⇒ no-op). Deterministic, headless. Same behaviour on
        /// every floor — floor 0 and upper floors are treated identically.</summary>
        public static void NudgeRoomOffOverlaps(InteriorFloor floor, int roomId)
        {
            if (floor == null || floor.Rooms.Count == 0) return;
            var room = floor.GetRoom(roomId);
            if (room == null) return;
            ResolveOverlapsMovableOnly(floor, new List<Room> { room });
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
                        // Full one-sided shove (the pinned side does not give) plus a clear margin, so the
                        // resulting edge gap sits comfortably above any round-trip noise / test threshold.
                        if (overlapX < overlapY)
                            mx += (overlapX + 0.1f) * (dx >= 0f ? 1f : -1f);
                        else
                            my += (overlapY + 0.1f) * (dy >= 0f ? 1f : -1f);
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
