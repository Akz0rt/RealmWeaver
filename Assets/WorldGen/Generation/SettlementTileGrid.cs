namespace WorldGen.Generation
{
    /// <summary>A tile's role in the 2.5D volumetric settlement render (Task 1 scaffold — no wall/road
    /// rasterization, depth or height yet; those are Tasks 2–5). None is the array default (0), so an
    /// untouched Cells slot reads as empty without any fill pass.</summary>
    public enum TileType { None = 0, Building, Road, Void, Wall, Gate }

    /// <summary>A settlement floor rasterized onto the building-cell lattice (SettlementGenerator.BuildingCell),
    /// UnityEngine-free and fully derived per rebuild — nothing here is stored/serialized. Task 1 provides only
    /// the grid shell: the cell↔normalized mapping, the buildings-derived extent, and snap. Wall/road/gate
    /// rasterization (Tasks 2–3), depth sort-keys (Task 4) and per-building height (Task 5) build on top.</summary>
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
    }
}
