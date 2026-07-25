namespace WorldGen.Generation
{
    /// <summary>A tile's role in the 2.5D volumetric settlement render. Building/Wall/Void come from Task 2's
    /// wall-ring pass; Road/Gate are Task 3 — a routed road reclassifies a Void cell to Road, and a stored gate
    /// room reclassifies its nearest Wall ring cell to Gate. Precedence (highest wins; enforced by write
    /// ORDER, never by re-checking every rule at every write): Building > Gate > Wall > Road > Void > None.
    /// None is the array default (0), so an untouched Cells slot reads as empty without any fill pass.</summary>
    public enum TileType { None = 0, Building, Road, Void, Wall, Gate }

    /// <summary>A settlement floor rasterized onto the building-cell lattice (SettlementGenerator.BuildingCell),
    /// UnityEngine-free and fully derived per rebuild — nothing here is stored/serialized. Allocate sizes the
    /// grid shell (cell↔normalized mapping, buildings-derived extent, snap). Build places buildings and, when
    /// the settlement HasWall, derives the wall ring + one-cell courtyard void from an outside flood-fill — the
    /// same no-holes guarantee SettlementFence uses at tile resolution (SettlementFence.cs class doc), just at
    /// the coarser building-cell grid instead of SettlementFence's continuous-tile grid. NOT full parity,
    /// though: unlike SettlementFence, this pass does not bridge disconnected stray building clusters
    /// (SettlementFence.BridgeStrays) — each cluster gets its own independent dilation ring and flood-fill,
    /// with no attempt to connect separate clusters into one boundary.
    ///
    /// TWO-TIER, mirroring the shipped DungeonLayout.DeriveTownFence(lvl, includeRoads): roads == null is
    /// FAST (drag frames — buildings-only ring/void, no Road cells, gates still applied since they don't
    /// depend on roads); roads != null is CLEAN (routed roads are rasterized and folded into the occupied
    /// blob BEFORE the ring/void pass, so the wall wraps the roads too, then reclassified Void→Road on top).
    /// Depth sort-keys (Task 4) and per-building height (Task 5) build further on top of this.</summary>
    public sealed class SettlementTileGrid
    {
        public TileType[,] Cells;          // [a, b]: a = col (i - OriginI), b = row (j - OriginJ)
        public int W, H, OriginI, OriginJ; // array is W×H; world cell (i,j) at (i-OriginI, j-OriginJ)
        public float AnchorX, AnchorY, Cell;

        public const int CourtyardCells = 1;               // empty Void ring kept between buildings and the wall
        public const int MarginCells = CourtyardCells + 2; // courtyard(1) + wall(1) + flood-fill border(1) = 3

        public int CellI(float xNorm) => (int)System.Math.Round((xNorm - AnchorX) / Cell);
        public int CellJ(float yNorm) => (int)System.Math.Round((yNorm - AnchorY) / Cell);
        public float CenterX(int i) => AnchorX + i * Cell;
        public float CenterY(int j) => AnchorY + j * Cell;
        public bool InBounds(int i, int j) => (i-OriginI)>=0 && (i-OriginI)<W && (j-OriginJ)>=0 && (j-OriginJ)<H;
        public TileType At(int i, int j) => InBounds(i,j) ? Cells[i-OriginI, j-OriginJ] : TileType.None;
        public float SnapX(float xNorm) => CenterX(CellI(xNorm));
        public float SnapY(float yNorm) => CenterY(CellJ(yNorm));

        // Allocate the empty grid (Cells all None), sized to the buildings' cell bbox + MarginCells — OR, when
        // roads is supplied (the Clean tier), the (buildings ∪ road-endpoints) bbox + MarginCells, so a
        // routed road leaving the buildings' bbox is still representable (OVERRIDE 1: a dropped road cell
        // could never be wrapped by the wall — the exact bug the fine-fence arc had to fix). `roads` defaults
        // to null so every existing buildings-only call site (this method's own doc history, and Task 8's
        // view-fitting) keeps calling `Allocate(buildings)` unchanged and gets byte-identical output.
        public static SettlementTileGrid Allocate(System.Collections.Generic.IReadOnlyList<Room> buildings,
            System.Collections.Generic.IReadOnlyList<LinkSegment> roads = null)
        {
            var g = new SettlementTileGrid { Cell = SettlementGenerator.BuildingCell };

            // Pass 1: anchor = min X/Y over TypeId==1 rooms. AnchorX/Y must be fixed before CellI/CellJ mean
            // anything, since both read them.
            bool any = false;
            float minX = 0f, minY = 0f;
            foreach (var r in buildings)
            {
                if (r.TypeId != 1) continue;
                if (!any || r.X < minX) minX = r.X;
                if (!any || r.Y < minY) minY = r.Y;
                any = true;
            }
            g.AnchorX = minX;
            g.AnchorY = minY;

            if (!any)
            {
                // No buildings: a minimal 1x1 None grid, no throw.
                g.OriginI = 0; g.OriginJ = 0; g.W = 1; g.H = 1;
                g.Cells = new TileType[1, 1];
                return g;
            }

            // Pass 2: cell-index bbox over the same buildings, via the grid's own mapping (now that Anchor/Cell
            // are set). The anchor building maps to cell (0,0) by construction.
            int minCellI = int.MaxValue, minCellJ = int.MaxValue, maxCellI = int.MinValue, maxCellJ = int.MinValue;
            foreach (var r in buildings)
            {
                if (r.TypeId != 1) continue;
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (i < minCellI) minCellI = i;
                if (i > maxCellI) maxCellI = i;
                if (j < minCellJ) minCellJ = j;
                if (j > maxCellJ) maxCellJ = j;
            }

            // Fold in every road segment's ENDPOINTS (Clean tier only). A LinkSegment is a straight line, so
            // both X and Y are monotonic along it — the cell-coordinate extremes of every intermediate sample
            // Build will later rasterize are already bounded by the two endpoints' own cell coordinates, so
            // folding just the endpoints (not the full step-sampled path) is exact for bbox purposes and far
            // cheaper. Endpoints are TILE space (RoomLinkGeometry convention); divide by TilesPerAxis first.
            if (roads != null)
                foreach (var seg in roads)
                {
                    int ai = g.CellI(seg.A.X / DungeonLayout.TilesPerAxis), aj = g.CellJ(seg.A.Y / DungeonLayout.TilesPerAxis);
                    int bi = g.CellI(seg.B.X / DungeonLayout.TilesPerAxis), bj = g.CellJ(seg.B.Y / DungeonLayout.TilesPerAxis);
                    if (ai < minCellI) minCellI = ai;
                    if (ai > maxCellI) maxCellI = ai;
                    if (aj < minCellJ) minCellJ = aj;
                    if (aj > maxCellJ) maxCellJ = aj;
                    if (bi < minCellI) minCellI = bi;
                    if (bi > maxCellI) maxCellI = bi;
                    if (bj < minCellJ) minCellJ = bj;
                    if (bj > maxCellJ) maxCellJ = bj;
                }

            g.OriginI = minCellI - MarginCells;
            g.OriginJ = minCellJ - MarginCells;
            g.W = (maxCellI - minCellI + 1) + 2 * MarginCells;
            g.H = (maxCellJ - minCellJ + 1) + 2 * MarginCells;
            g.Cells = new TileType[g.W, g.H];
            return g;
        }

        // Build the settlement's tile grid: buildings placed onto the lattice, plus — when the settlement
        // HasWall — the wall-ring/courtyard classification, roads rasterized on top, and gates reclassifying
        // the ring. TWO-TIER: roads == null is FAST (buildings-only ring/void, no Road cells — byte-identical
        // to the Task-2 wall-ring algorithm); roads != null is CLEAN (routed roads folded into the occupied
        // blob BEFORE the ring/void pass, so the wall wraps them too, then reclassified onto the result).
        // Gates apply in BOTH tiers whenever HasWall — a gate's existence has nothing to do with whether roads
        // were supplied this particular rebuild.
        //
        // Precedence Building > Gate > Wall > Road > Void > None is enforced by WRITE ORDER, never by
        // re-deciding every rule at every cell: buildings are written first and every later pass explicitly
        // skips Building cells; MarkRoads additionally skips Wall cells (so a road can only ever land on Void);
        // MarkGates only ever retargets a cell that currently reads Wall (so it can't clobber a Road cell, and
        // trivially can't clobber Building either).
        public static SettlementTileGrid Build(InteriorFloor floor, System.Collections.Generic.IReadOnlyList<LinkSegment> roads)
        {
            var g = Allocate(floor.Rooms, roads);
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 1) continue;
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (g.InBounds(i, j)) g.Cells[i - g.OriginI, j - g.OriginJ] = TileType.Building;
            }

            bool hasWall = floor.SettlementParams != null && floor.SettlementParams.HasWall;
            // Road/Gate concepts both ride on "Inside", which only exists when the settlement HasWall (Task
            // 2: HasWall == false means no Wall/Void, no Inside/Outside split at all) — so roads are only
            // rasterized when there is a wall pass for them to fold into and be judged against.
            bool[,] roadMask = (roads != null && hasWall) ? RasterizeRoads(g, roads) : null;

            bool[,] inside = hasWall ? BuildWallRing(g, roadMask) : null;

            if (roadMask != null) MarkRoads(g, roadMask, inside);
            if (hasWall) MarkGates(g, floor.Rooms);

            return g;
        }

        // ---- wall ring ------------------------------------------------------------------------------------
        // (a) occupied = (building cells ∪ extraSeed cells, when supplied) dilated by CourtyardCells + 1
        //     (buildings/roads + a one-cell courtyard skirt + the wall layer itself). extraSeed == null (the
        //     Fast tier) reduces this to exactly Task 2's buildings-only dilation — byte-identical. (b)
        //     flood-fill Outside from the grid border through !occupied (4-connected) —
        //     SettlementFence.InsideFromOutsideFill's technique, at cell resolution. (c) Inside = !Outside, so
        //     an enclosed pocket among the buildings can never read as outside-None (no holes). (d) Wall =
        //     Inside cells that are not Building and have >=1 non-Inside 4-neighbour — the OUTERMOST ring of
        //     Inside only, since every cell further in already has all-Inside neighbours. (e) Void = whatever
        //     Inside is left (neither Building nor Wall) — the one-cell courtyard ring plus any enclosed
        //     interior courtyard, so a building is never flush to a wall tile. Returns Inside so callers
        //     (MarkRoads, MarkGates) can reuse it without recomputing the flood-fill.
        static bool[,] BuildWallRing(SettlementTileGrid g, bool[,] extraSeed)
        {
            int w = g.W, h = g.H;
            var seed = new bool[w, h];
            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                    seed[a, b] = g.Cells[a, b] == TileType.Building || (extraSeed != null && extraSeed[a, b]);

            var occupied = Dilate(seed, w, h, CourtyardCells + 1);
            var outside = FloodOutside(occupied, w, h);

            var inside = new bool[w, h];
            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                    inside[a, b] = !outside[a, b];

            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                {
                    if (!inside[a, b] || g.Cells[a, b] == TileType.Building) continue;
                    if (HasNonInsideNeighbour(inside, w, h, a, b))
                        g.Cells[a, b] = TileType.Wall;
                }

            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                    if (inside[a, b] && g.Cells[a, b] != TileType.Building && g.Cells[a, b] != TileType.Wall)
                        g.Cells[a, b] = TileType.Void;

            return inside;
        }

        // Square (Chebyshev) dilation: every array cell within `radius` cells on EACH axis of a seed cell
        // becomes occupied — the discrete analogue of SettlementFence's inflated-rect building footprint
        // (RasterizeRoad/Derive's hw/hh inflation), just applied to single cells instead of rects. `seed` is
        // Building-only for the Fast tier, Building ∪ raw-road-cells for Clean (BuildWallRing builds it).
        static bool[,] Dilate(bool[,] seed, int w, int h, int radius)
        {
            var occupied = new bool[w, h];
            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                {
                    if (!seed[a, b]) continue;
                    int a0 = System.Math.Max(0, a - radius), a1 = System.Math.Min(w - 1, a + radius);
                    int b0 = System.Math.Max(0, b - radius), b1 = System.Math.Min(h - 1, b + radius);
                    for (int da = a0; da <= a1; da++)
                        for (int db = b0; db <= b1; db++)
                            occupied[da, db] = true;
                }
            return occupied;
        }

        // ---- roads (Task 3) --------------------------------------------------------------------------------
        // Rasterize every road segment onto the cell lattice as a RAW cell-membership mask, independent of
        // Inside/Wall/Building — used both (a) as BuildWallRing's extraSeed, so the occupied blob (and hence
        // the wall ring derived from it) includes the road, and (b) after Inside/Wall/Void are known, to
        // reclassify Void cells to Road. Every write is bounds-checked (InBounds) and never throws, even if a
        // sample's nearest cell falls outside the grid — belt-and-suspenders alongside the Allocate extent
        // fold above (OVERRIDE 1): a caller that ever passes roads without routing them through this Build's
        // own Allocate call still can't crash this pass, it just silently loses the out-of-range samples.
        static bool[,] RasterizeRoads(SettlementTileGrid g, System.Collections.Generic.IReadOnlyList<LinkSegment> roads)
        {
            var mask = new bool[g.W, g.H];
            foreach (var seg in roads)
            {
                float ax = seg.A.X / DungeonLayout.TilesPerAxis, ay = seg.A.Y / DungeonLayout.TilesPerAxis;
                float bx = seg.B.X / DungeonLayout.TilesPerAxis, by = seg.B.Y / DungeonLayout.TilesPerAxis;
                float dx = bx - ax, dy = by - ay;
                float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
                int steps = (int)System.Math.Ceiling(len / (0.5f * g.Cell));
                if (steps < 1) steps = 1;
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    int i = g.CellI(ax + dx * t), j = g.CellJ(ay + dy * t);
                    if (g.InBounds(i, j)) mask[i - g.OriginI, j - g.OriginJ] = true;
                }
            }
            return mask;
        }

        // Reclassify every rasterized road cell that is Inside and not Building/Wall (i.e. currently Void,
        // since every road cell was folded into BuildWallRing's occupied seed and so is guaranteed Inside) to
        // Road. The Building/Wall guard is what keeps the precedence promise Building > Wall > Road: dropping
        // it would let a road cell overwrite whatever already occupies that cell. The Wall half is currently
        // unreachable by construction (not dead code, just never exercised by any legal input): a road cell is
        // itself part of BuildWallRing's dilation seed, and radius >= 1 dilation always covers a seed cell's
        // own 4-neighbours — so a road cell can never have a non-Inside neighbour and so can never satisfy
        // HasNonInsideNeighbour, i.e. never gets written Wall in the first place. Kept because the brief's
        // precedence rule names both terms explicitly, and because that invariant depends on CourtyardCells +
        // 1 staying >= 1 — not something this method should silently assume forever.
        static void MarkRoads(SettlementTileGrid g, bool[,] roadMask, bool[,] inside)
        {
            for (int a = 0; a < g.W; a++)
                for (int b = 0; b < g.H; b++)
                {
                    if (!roadMask[a, b] || !inside[a, b]) continue;
                    if (g.Cells[a, b] == TileType.Building || g.Cells[a, b] == TileType.Wall) continue;
                    g.Cells[a, b] = TileType.Road;
                }
        }

        // For every stored gate room (TypeId == 0), find the Wall ring cell whose CENTRE is nearest the gate's
        // normalized position (plain Euclidean distance over every current Wall cell — the ring is small
        // enough per settlement that an O(W*H) scan per gate costs nothing measurable) and reclassify it to
        // Gate. Runs whenever HasWall, independent of roads — a gate's ring cell doesn't depend on whether
        // this particular rebuild routed any roads. No separate Inside test needed: scanning for
        // TileType.Wall already implies Inside (BuildWallRing only ever writes Wall to Inside cells).
        static void MarkGates(SettlementTileGrid g, System.Collections.Generic.IReadOnlyList<Room> rooms)
        {
            foreach (var r in rooms)
            {
                if (r.TypeId != 0) continue;
                int bestA = -1, bestB = -1;
                float bestD2 = float.MaxValue;
                for (int a = 0; a < g.W; a++)
                    for (int b = 0; b < g.H; b++)
                    {
                        if (g.Cells[a, b] != TileType.Wall) continue;
                        float cx = g.CenterX(a + g.OriginI), cy = g.CenterY(b + g.OriginJ);
                        float dx = cx - r.X, dy = cy - r.Y;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < bestD2) { bestD2 = d2; bestA = a; bestB = b; }
                    }
                if (bestA >= 0) g.Cells[bestA, bestB] = TileType.Gate;
            }
        }

        // 4-connected BFS of !occupied cells seeded from every border cell -> the OUTSIDE set.
        static bool[,] FloodOutside(bool[,] occupied, int w, int h)
        {
            var outside = new bool[w, h];
            var stack = new System.Collections.Generic.List<(int a, int b)>();
            for (int a = 0; a < w; a++)
            {
                Seed(occupied, outside, stack, a, 0);
                Seed(occupied, outside, stack, a, h - 1);
            }
            for (int b = 0; b < h; b++)
            {
                Seed(occupied, outside, stack, 0, b);
                Seed(occupied, outside, stack, w - 1, b);
            }
            while (stack.Count > 0)
            {
                var (a, b) = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (a > 0) Seed(occupied, outside, stack, a - 1, b);
                if (a < w - 1) Seed(occupied, outside, stack, a + 1, b);
                if (b > 0) Seed(occupied, outside, stack, a, b - 1);
                if (b < h - 1) Seed(occupied, outside, stack, a, b + 1);
            }
            return outside;
        }

        static void Seed(bool[,] occupied, bool[,] outside, System.Collections.Generic.List<(int a, int b)> stack, int a, int b)
        {
            if (occupied[a, b] || outside[a, b]) return;
            outside[a, b] = true;
            stack.Add((a, b));
        }

        static bool HasNonInsideNeighbour(bool[,] inside, int w, int h, int a, int b)
        {
            if (a == 0 || !inside[a - 1, b]) return true;
            if (a == w - 1 || !inside[a + 1, b]) return true;
            if (b == 0 || !inside[a, b - 1]) return true;
            if (b == h - 1 || !inside[a, b + 1]) return true;
            return false;
        }
    }
}
