namespace WorldGen.Generation
{
    /// <summary>A tile's role in the 2.5D volumetric settlement render. Task 2 rasterizes Building/Wall/Void;
    /// Road/Gate are Task 3 (roads reclassify Void→Road and Wall→Gate at crossings). None is the array default
    /// (0), so an untouched Cells slot reads as empty without any fill pass.</summary>
    public enum TileType { None = 0, Building, Road, Void, Wall, Gate }

    /// <summary>A settlement floor rasterized onto the building-cell lattice (SettlementGenerator.BuildingCell),
    /// UnityEngine-free and fully derived per rebuild — nothing here is stored/serialized. Allocate sizes the
    /// grid shell (cell↔normalized mapping, buildings-derived extent, snap). Build (Task 2) places buildings
    /// and, when the settlement HasWall, derives the wall ring + one-cell courtyard void from an outside
    /// flood-fill — the same no-holes guarantee SettlementFence uses at tile resolution (SettlementFence.cs
    /// class doc), just at the coarser building-cell grid instead of SettlementFence's continuous-tile grid.
    /// NOT full parity, though: unlike SettlementFence, this pass does not bridge disconnected stray building
    /// clusters (SettlementFence.BridgeStrays) — each cluster gets its own independent dilation ring and
    /// flood-fill, with no attempt to connect separate clusters into one boundary. Roads (Task 3) reclassify
    /// on top; depth sort-keys (Task 4) and per-building height (Task 5) build further.</summary>
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

        // Allocate the empty grid (Cells all None), sized to the BUILDINGS-ONLY cell bbox + MarginCells.
        // A later task (roads rasterization) extends this extent so a routed road leaving the buildings'
        // bbox is still covered — that extension belongs there, not here.
        public static SettlementTileGrid Allocate(System.Collections.Generic.IReadOnlyList<Room> buildings)
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

            g.OriginI = minCellI - MarginCells;
            g.OriginJ = minCellJ - MarginCells;
            g.W = (maxCellI - minCellI + 1) + 2 * MarginCells;
            g.H = (maxCellJ - minCellJ + 1) + 2 * MarginCells;
            g.Cells = new TileType[g.W, g.H];
            return g;
        }

        // Build the settlement's tile grid: buildings placed onto the lattice, plus — when the settlement
        // HasWall — the wall-ring/courtyard classification. Task 2 implements the buildings+wall+void pass in
        // full (roads == null exercises exactly that path); Task 3 adds routed-road rasterization on top,
        // reclassifying some Void cells to Road and the Wall cells at road crossings to Gate. The wall pass
        // itself runs unconditionally whenever HasWall — it does not gate on roads == null — so a Task-3 build
        // that already passes a non-null roads list still gets a correct base wall/courtyard, roads simply not
        // yet layered on (that layering is Task 3's job, not this one's).
        public static SettlementTileGrid Build(InteriorFloor floor, System.Collections.Generic.IReadOnlyList<LinkSegment> roads)
        {
            var g = Allocate(floor.Rooms);
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 1) continue;
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (g.InBounds(i, j)) g.Cells[i - g.OriginI, j - g.OriginJ] = TileType.Building;
            }

            bool hasWall = floor.SettlementParams != null && floor.SettlementParams.HasWall;
            if (hasWall) BuildWallRing(g);
            // roads: Task 3 reclassifies Void -> Road and, at road/wall crossings, Wall -> Gate. Unused here.

            return g;
        }

        // ---- wall ring (Task 2) ----------------------------------------------------------------------------
        // (a) occupied = building cells dilated by CourtyardCells + 1 (buildings + a one-cell courtyard skirt +
        //     the wall layer itself). (b) flood-fill Outside from the grid border through !occupied
        //     (4-connected) — SettlementFence.InsideFromOutsideFill's technique, at cell resolution. (c) Inside
        //     = !Outside, so an enclosed pocket among the buildings can never read as outside-None (no holes).
        //     (d) Wall = Inside cells that are not Building and have >=1 non-Inside 4-neighbour — the OUTERMOST
        //     ring of Inside only, since every cell further in already has all-Inside neighbours. (e) Void =
        //     whatever Inside is left (neither Building nor Wall) — the one-cell courtyard ring plus any
        //     enclosed interior courtyard, so a building is never flush to a wall tile.
        static void BuildWallRing(SettlementTileGrid g)
        {
            int w = g.W, h = g.H;
            var occupied = Dilate(g.Cells, w, h, CourtyardCells + 1);
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
        }

        // Square (Chebyshev) dilation: every array cell within `radius` cells on EACH axis of a Building cell
        // becomes occupied — the discrete analogue of SettlementFence's inflated-rect building footprint
        // (RasterizeRoad/Derive's hw/hh inflation), just applied to a single-cell building instead of a rect.
        static bool[,] Dilate(TileType[,] cells, int w, int h, int radius)
        {
            var occupied = new bool[w, h];
            for (int a = 0; a < w; a++)
                for (int b = 0; b < h; b++)
                {
                    if (cells[a, b] != TileType.Building) continue;
                    int a0 = System.Math.Max(0, a - radius), a1 = System.Math.Min(w - 1, a + radius);
                    int b0 = System.Math.Max(0, b - radius), b1 = System.Math.Min(h - 1, b + radius);
                    for (int da = a0; da <= a1; da++)
                        for (int db = b0; db <= b1; db++)
                            occupied[da, db] = true;
                }
            return occupied;
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
