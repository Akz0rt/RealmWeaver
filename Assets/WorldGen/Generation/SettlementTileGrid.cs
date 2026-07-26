namespace WorldGen.Generation
{
    /// <summary>A tile's role in the 2.5D volumetric settlement render. Building comes from a room's stored
    /// FOOTPRINT (every cell of it, not just one), Wall/Void from the wall-ring pass; Road is a STORED street
    /// cell (SettlementParams.StreetCells) reclassifying a Void cell when the settlement HasWall, or a None
    /// cell when it does not (a wall-less village still gets streets — see MarkRoads), and a stored gate room
    /// reclassifies its nearest Wall ring cell to Gate. Precedence (highest wins; enforced by write ORDER,
    /// never by re-checking every rule at every write): Building > Gate > Wall > Road > Void > None. None is
    /// the array default (0), so an untouched Cells slot reads as empty without any fill pass.</summary>
    public enum TileType { None = 0, Building, Road, Void, Wall, Gate }

    /// <summary>A settlement floor rasterized onto the building-cell lattice (SettlementGenerator.BuildingCell),
    /// UnityEngine-free and fully derived per rebuild — nothing here is stored/serialized. The cell↔normalized
    /// mapping is <see cref="SettlementFootprint"/>'s FIXED, absolute one (cell (0,0) spans normalized
    /// [0,Pitch)), so a cell index never depends on which buildings exist; Allocate therefore sizes only the
    /// grid's EXTENT (buildings-derived bbox + margin) and its array offset. Build places buildings and, when
    /// the settlement HasWall, derives the wall ring + one-cell courtyard void from an outside flood-fill — the
    /// same no-holes guarantee SettlementFence uses at tile resolution (SettlementFence.cs class doc), just at
    /// the coarser building-cell grid instead of SettlementFence's continuous-tile grid. NOT full parity,
    /// though: unlike SettlementFence, this pass does not bridge disconnected stray building clusters
    /// (SettlementFence.BridgeStrays) — each cluster gets its own independent dilation ring and flood-fill,
    /// with no attempt to connect separate clusters into one boundary.
    ///
    /// A BUILDING IS A FOOTPRINT, NOT A POINT. Every cell of every settlement building room's footprint
    /// (Room.Cells, read through FootprintOf) is written Building, so a block of FLUSH multi-cell buildings
    /// renders as the solid mass it is. Adjacency between two buildings is now legal and ordinary — the
    /// separation that used to be guaranteed by "one building per cell" is gone, and nothing here re-imposes
    /// it.
    ///
    /// STREETS ARE STORED, NOT ROUTED. Road cells come from SettlementParams.StreetCells — the same absolute
    /// lattice, decoded by SettlementFootprint.Decode — so this pass no longer rasterizes routed LinkSegments
    /// and no longer has a Fast/Clean two-tier split: there is exactly ONE grid for a given floor, and it
    /// costs no road A*. Street cells are folded into the occupied blob BEFORE the ring/void pass, so the
    /// wall wraps the streets exactly as it used to wrap the routed roads; when the settlement does NOT
    /// HasWall there is no ring/void pass to fold into, so the raw street cells are reclassified None→Road
    /// directly — a wall-less village still gets its streets, see MarkRoads. Depth sort-keys and per-building
    /// height build further on top of this.</summary>
    public sealed class SettlementTileGrid
    {
        public TileType[,] Cells;          // [a, b]: a = col (i - OriginI), b = row (j - OriginJ)
        public int W, H, OriginI, OriginJ; // array is W×H; world cell (i,j) at (i-OriginI, j-OriginJ)
        public float Cell;

        public const int CourtyardCells = 1;               // empty Void ring kept between buildings and the wall
        public const int MarginCells = CourtyardCells + 2; // courtyard(1) + wall(1) + flood-fill border(1) = 3

        // The cell↔normalized mapping is SettlementFootprint's and nothing else's — one lattice for the data
        // model (Room.Cells), the grid, and every renderer, so a stored cell index and a drawn cell index can
        // never disagree. Floor, not round: a cell is the half-open span [i*Pitch,(i+1)*Pitch).
        public int CellI(float xNorm) => SettlementFootprint.CellOf(xNorm);
        public int CellJ(float yNorm) => SettlementFootprint.CellOf(yNorm);
        public float CenterX(int i) => SettlementFootprint.CenterOf(i);
        public float CenterY(int j) => SettlementFootprint.CenterOf(j);
        public bool InBounds(int i, int j) => (i-OriginI)>=0 && (i-OriginI)<W && (j-OriginJ)>=0 && (j-OriginJ)<H;
        public TileType At(int i, int j) => InBounds(i,j) ? Cells[i-OriginI, j-OriginJ] : TileType.None;
        public float SnapX(float xNorm) => CenterX(CellI(xNorm));
        public float SnapY(float yNorm) => CenterY(CellJ(yNorm));

        // ---- footprints ------------------------------------------------------------------------------------
        // The cells a settlement BUILDING room occupies, and the single place both Allocate and Build ask.
        // Room.Cells is AUTHORITATIVE WHEN PRESENT — a multi-cell footprint is the only description of that
        // shape there is, and nothing here may second-guess it. Two rules cover the cases where it is not
        // present or not trustworthy:
        //
        //   (a) NO FOOTPRINT -> one cell, derived from the room's point. A freshly GENERATED town has this:
        //       SettlementGenerator.BuildFloor does not populate Cells (block generation is a later task),
        //       while a RELOADED town has single-cell footprints from SettlementFootprint.EnsureFootprints'
        //       v10 migration. Both must render identically, and this is what makes them.
        //
        //   (b) A SINGLE-CELL FOOTPRINT THAT DISAGREES WITH THE POINT IS STALE -> re-derived from the point.
        //       Moving a building writes Room.X/Y from eight editor call sites and does NOT (yet) rewrite
        //       Room.Cells, and the migration never overwrites a non-empty footprint — so a migrated
        //       building's one cell would be frozen at where it USED to be, and dragging it would stop moving
        //       its tile. Re-deriving is exact for the single-cell case (one cell IS the point's cell, by
        //       construction) and it is why this rule stays correct even after moves start maintaining the
        //       footprint properly: at that point the two simply never disagree, and the rule never fires.
        //
        //       A MULTI-CELL footprint is NEVER re-derived, deliberately: a point cannot reconstruct a shape,
        //       so "self-healing" one would silently amputate an L or a bar down to a single cell — far worse
        //       than the staleness it would be trying to fix. Both halves are asserted (SelfTestFootprintTiles).
        // Shared, NEVER-MUTATED empty list — the null/empty-Cells short-circuit below reuses this ONE instance
        // instead of letting Decode allocate its own throwaway empty list every call. Safe to share: `cells`
        // below is only ever read (.Count, [0]) or returned outright when NEITHER fallback rule fires, and
        // this instance's Count is always 0, which always fires rule (a) a few lines down — so it can never
        // reach that final `return cells;` and escape into a caller that might mutate it.
        static readonly System.Collections.Generic.List<(int i, int j)> s_noCells = new System.Collections.Generic.List<(int i, int j)>();

        public static System.Collections.Generic.List<(int i, int j)> FootprintOf(Room r)
        {
            var point = (i: SettlementFootprint.CellOf(r.X), j: SettlementFootprint.CellOf(r.Y));
            // r.Cells is null for EVERY building in a freshly generated town (block generation is a later
            // task) and this function runs twice per building per rebuild (Allocate's fold, then Build's
            // write) — skip Decode's own throwaway empty-list allocation for that hot case by handing it the
            // shared s_noCells instead. This does NOT duplicate rule (a): an odd-length/corrupt array (not
            // null, not empty, but still Decodes to zero cells) still reaches Decode() below and falls through
            // to the SAME `cells.Count == 0` check two lines down, so that check stays the one and only place
            // rule (a) is decided — deliberately, so a mutant on it (MutFootprintNoNullFallback) still has
            // exactly one line to break.
            var cells = (r.Cells == null || r.Cells.Length == 0) ? s_noCells : SettlementFootprint.Decode(r.Cells);
            if (cells.Count == 0) return new System.Collections.Generic.List<(int i, int j)> { point };            // (a)
            if (cells.Count == 1 && cells[0] != point)
                return new System.Collections.Generic.List<(int i, int j)> { point };                              // (b)
            return cells;
        }

        // Allocate the empty grid (Cells all None), sized to the (building FOOTPRINT cells ∪ street cells)
        // bbox + MarginCells — EVERY cell, never one representative cell per room. That "every" is
        // load-bearing rather than tidy: every write in this file is bounds-guarded (InBounds), so a footprint
        // whose far cells fall outside an under-sized extent is dropped SILENTLY, with no error anywhere —
        // the same class of bug as a road cell the wall could never wrap, which is what the fine-fence arc
        // had to fix. `roads` (optional, and no longer passed by Build — see below) additionally folds routed
        // road ENDPOINTS in, for the view-fit call site.
        //
        // ORIGIN vs EXTENT — the distinction this method now turns on. The EXTENT still depends on what is
        // placed (it is the occupied bbox plus MarginCells, and it must be, or a building would fall off the
        // array). The ORIGIN does NOT: the lattice is SettlementFootprint's absolute one, so cell (i,j) means
        // the same patch of normalized space no matter which buildings exist. There is no anchor pass any
        // more, deliberately. The old one took the min-X/min-Y building as cell (0,0), which made every other
        // building's cell index a function of THAT ONE building's position — move it and the whole town
        // renumbers (and, whenever it moved off-pitch, slides). OriginI/OriginJ stay: they are the array's
        // offset into the absolute lattice, not a redefinition of it.
        public static SettlementTileGrid Allocate(System.Collections.Generic.IReadOnlyList<Room> buildings,
            System.Collections.Generic.IReadOnlyList<LinkSegment> roads = null,
            System.Collections.Generic.IReadOnlyList<(int i, int j)> streets = null)
        {
            var g = new SettlementTileGrid { Cell = SettlementGenerator.BuildingCell };

            // ONE pass now, not two: with a fixed lattice the cell-index bbox needs nothing established first.
            bool any = false;
            int minCellI = int.MaxValue, minCellJ = int.MaxValue, maxCellI = int.MinValue, maxCellJ = int.MinValue;
            void Fold(int i, int j)
            {
                if (i < minCellI) minCellI = i;
                if (i > maxCellI) maxCellI = i;
                if (j < minCellJ) minCellJ = j;
                if (j > maxCellJ) maxCellJ = j;
                any = true;
            }

            foreach (var r in buildings)
            {
                if (r.TypeId != 1) continue;
                foreach (var c in FootprintOf(r)) Fold(c.i, c.j);
            }
            // Streets count towards `any`, unlike the road ENDPOINTS below: a street cell is stored data that
            // must be representable in its own right, whereas a routed road is derived and its fold is only
            // ever a widening of an extent the buildings already established.
            if (streets != null)
                foreach (var c in streets) Fold(c.i, c.j);

            if (!any)
            {
                // Nothing placed at all: a minimal 1x1 None grid, no throw. Roads are NOT folded in on this
                // path — the pre-existing empty-input contract, unchanged.
                g.OriginI = 0; g.OriginJ = 0; g.W = 1; g.H = 1;
                g.Cells = new TileType[1, 1];
                return g;
            }

            // Fold in every road segment's ENDPOINTS. A LinkSegment is a straight line, so both X and Y are
            // monotonic along it — every intermediate point's cell coordinates are already bounded by the two
            // endpoints' own, so folding just the endpoints is exact for bbox purposes and far cheaper.
            // Endpoints are TILE space (RoomLinkGeometry convention); divide by TilesPerAxis first.
            //
            // NOTE: Build no longer passes this — streets are stored cells now, not routed segments — so the
            // only live caller is DungeonViewController.FitBoundsFor, which sizes the VIEW to the routed road
            // network so a road leaving the buildings' bbox is not fitted off-panel. That means this fold has
            // no self-test coverage of its own any more; it is not dead, and deleting it as "unused" would
            // silently narrow the fit.
            if (roads != null)
                foreach (var seg in roads)
                {
                    Fold(g.CellI(seg.A.X / DungeonLayout.TilesPerAxis), g.CellJ(seg.A.Y / DungeonLayout.TilesPerAxis));
                    Fold(g.CellI(seg.B.X / DungeonLayout.TilesPerAxis), g.CellJ(seg.B.Y / DungeonLayout.TilesPerAxis));
                }

            g.OriginI = minCellI - MarginCells;
            g.OriginJ = minCellJ - MarginCells;
            g.W = (maxCellI - minCellI + 1) + 2 * MarginCells;
            g.H = (maxCellJ - minCellJ + 1) + 2 * MarginCells;
            g.Cells = new TileType[g.W, g.H];
            return g;
        }

        // Build the settlement's tile grid: every cell of every building's FOOTPRINT placed onto the lattice,
        // every STORED street cell marked Road, plus — when the settlement HasWall — the wall-ring/courtyard
        // classification and the gates reclassifying the ring. ONE TIER: the streets are read out of
        // SettlementParams.StreetCells rather than routed, so there is nothing expensive left to defer and no
        // Fast/Clean split to keep in sync (that split existed only because the Clean tier had to pay for a
        // ~12.5 ms road A*).
        //
        // Precedence Building > Gate > Wall > Road > Void > None is enforced by WRITE ORDER, never by
        // re-deciding every rule at every cell: buildings are written first and every later pass explicitly
        // skips Building cells; MarkRoads additionally skips Wall cells (so under HasWall a street can only
        // ever land on Void — see MarkRoads for why Wall is unreachable there in the first place; without
        // HasWall there is no Wall to skip and a street lands on None instead); MarkGates only ever retargets
        // a cell that currently reads Wall or (idempotently) Gate (so it can't clobber a Road cell, and
        // trivially can't clobber Building either) — see MarkGates for why Gate is accepted too.
        public static SettlementTileGrid Build(InteriorFloor floor)
        {
            // Decode ONCE and hand the same list to Allocate (extent) and StreetMask (cells): the extent must
            // be sized for exactly the cells that will be written, or a street outside it is dropped silently.
            var streets = SettlementFootprint.Decode(floor.SettlementParams?.StreetCells);
            var g = Allocate(floor.Rooms, null, streets);
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 1) continue;
                var fp = FootprintOf(r);
                foreach (var c in fp)
                    if (g.InBounds(c.i, c.j)) g.Cells[c.i - g.OriginI, c.j - g.OriginJ] = TileType.Building;
            }

            bool hasWall = floor.SettlementParams != null && floor.SettlementParams.HasWall;
            // Streets are marked regardless of HasWall: a wall-less village (every Village —
            // MapScreenController sets HasWall = City-only) still gets streets, and those streets must
            // render. Only the WALL/gate machinery below is conditional on HasWall — "Inside" is a wall-ring
            // concept and simply doesn't exist without a wall, so when HasWall is false `inside` stays null
            // and MarkRoads is told (via that null) not to apply an Inside test at all.
            bool[,] streetMask = StreetMask(g, streets);

            bool[,] inside = hasWall ? BuildWallRing(g, streetMask) : null;

            MarkRoads(g, streetMask, inside);
            if (hasWall) MarkGates(g, floor.Rooms);

            return g;
        }

        // ---- depth (Task 4) --------------------------------------------------------------------------------
        // Painter's-algorithm sort key: ROW-MAJOR — j (the row) is the primary term, i (the column) only
        // breaks ties within a row — so a cell further south (larger j) always sorts after EVERY cell in any
        // smaller row, regardless of column. That is the exact invariant the renderer (Task 7) needs to draw
        // back-to-front and get near-occludes-far for free, including the one the user called out twice: a
        // front (south) Wall tile is never overlapped by a Building behind it, because the Wall's row is
        // always >= the courtyard/building rows in front of which it stands (SelfTestDepth's
        // WallOccludesBuildingBehind pins this on the actual grid a 2x2 building block produces).
        //
        // Negative i is safe: this grid's world coordinates (OriginI/OriginJ, set by Allocate as
        // minCellI/minCellJ - MarginCells) only ever go a few cells negative — MarginCells is a small constant
        // (CourtyardCells + 2) and real settlements span tens of building-lattice cells, not hundreds of
        // thousands — so |i| never approaches the 1_000_000 row spacing this key relies on to keep i's
        // contribution from ever crossing a row boundary. A cell would need |i| >= 500,000 to invert the
        // ordering between two adjacent rows, which is many orders of magnitude past anything this grid can
        // produce.
        public static long DepthKey(int i, int j) => (long)j * 1_000_000 + i;

        // Every occupied (non-None) cell, ascending by DepthKey — the exact back-to-front order the renderer
        // must paint in. PURE: only reads Cells, never writes it; two calls return equal lists (same cells,
        // same order) since sorting is a function of each cell's own (i,j), not of any external state.
        public System.Collections.Generic.List<(int i, int j)> DrawOrder()
        {
            var order = new System.Collections.Generic.List<(int i, int j)>(W * H);
            for (int a = 0; a < W; a++)
                for (int b = 0; b < H; b++)
                    if (Cells[a, b] != TileType.None)
                        order.Add((a + OriginI, b + OriginJ));
            // Sort MUST stay keyed on DepthKey, even though this a-outer/b-outer emission order happens to come
            // out row-sorted already for the current a/b loop nesting: a future cleanup that "simplifies away"
            // this Sort as redundant would silently make MutDepthKeyNoRowSort undetectable (DepthKey would then
            // have no caller left at all, so a broken key could never surface through DrawOrder's output).
            order.Sort((p, q) => DepthKey(p.i, p.j).CompareTo(DepthKey(q.i, q.j)));
            return order;
        }

        // ---- wall ring ------------------------------------------------------------------------------------
        // (a) occupied = (building cells ∪ extraSeed cells, when supplied) dilated by CourtyardCells + 1
        //     (buildings/streets + a one-cell courtyard skirt + the wall layer itself). extraSeed is the
        //     STREET mask; a null/all-false one reduces this to a buildings-only dilation. (b)
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
        // Building ∪ the stored street cells (BuildWallRing builds it).
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

        // ---- streets ---------------------------------------------------------------------------------------
        // The STORED street cells as a RAW cell-membership mask, independent of Inside/Wall/Building — used
        // both (a) as BuildWallRing's extraSeed, so the occupied blob (and hence the wall ring derived from
        // it) includes the streets, and (b) after Inside/Wall/Void are known, to reclassify Void cells to
        // Road. Every write is bounds-checked (InBounds) and never throws even if a stored cell falls outside
        // the grid — belt-and-suspenders alongside Allocate's street fold above, which is what actually makes
        // that impossible for a grid Build itself allocated.
        static bool[,] StreetMask(SettlementTileGrid g, System.Collections.Generic.IReadOnlyList<(int i, int j)> streets)
        {
            var mask = new bool[g.W, g.H];
            if (streets == null) return mask;
            foreach (var c in streets)
                if (g.InBounds(c.i, c.j)) mask[c.i - g.OriginI, c.j - g.OriginJ] = true;
            return mask;
        }

        // Reclassify every street cell to Road, subject to two guard terms. `inside` is NULLABLE: non-null
        // (HasWall) means "only reclassify cells the wall-ring pass judged Inside"; null (no wall at all —
        // see Build) means there is no Inside/Outside concept to test against, so every street cell is
        // eligible regardless — a wall-less village still gets its streets. Each guard has a live half and
        // a defensive/unreachable half, kept deliberately rather than folded away:
        //   - `inside != null && !inside[a, b]`: the `inside != null` half is LIVE — it is what makes the
        //     no-wall path above work at all. The `!inside[a, b]` half is defensive-only: whenever `inside` IS
        //     non-null, every street cell is itself part of BuildWallRing's dilation seed, and radius >= 1
        //     dilation always covers a seed cell's own 4-neighbours, so a street cell can never actually end
        //     up Outside. Not dead code — this depends on CourtyardCells + 1 staying >= 1, not something this
        //     method should silently assume forever.
        //   - `Building || Wall`: the Building half is LIVE and load-bearing — it is what keeps the precedence
        //     promise Building > ... > Road (dropping it lets a street overwrite a building, which stored
        //     street cells make MORE reachable than routed roads did: nothing forces a stored street cell to
        //     miss a stored footprint cell). The Wall half is defensive-only, unreachable by the SAME
        //     seed/dilation argument as above when `inside` is non-null (a street cell can never have been
        //     written Wall by BuildWallRing); when `inside` is null there are no Wall cells anywhere in the
        //     grid, so it is vacuously false for the unrelated reason that HasWall is false. Kept because the
        //     precedence rule names the Wall term explicitly.
        static void MarkRoads(SettlementTileGrid g, bool[,] roadMask, bool[,] inside)
        {
            for (int a = 0; a < g.W; a++)
                for (int b = 0; b < g.H; b++)
                {
                    if (!roadMask[a, b]) continue;
                    if (inside != null && !inside[a, b]) continue;
                    if (g.Cells[a, b] == TileType.Building || g.Cells[a, b] == TileType.Wall) continue;
                    g.Cells[a, b] = TileType.Road;
                }
        }

        // For every stored gate room (TypeId == 0), find the ring cell whose CENTRE is nearest the gate's
        // normalized position (plain Euclidean distance over every current Wall/Gate cell — the ring is small
        // enough per settlement that an O(W*H) scan per gate costs nothing measurable) and reclassify it to
        // Gate. Runs whenever HasWall. No separate Inside test needed: scanning for
        // TileType.Wall/Gate already implies Inside (BuildWallRing only ever writes Wall to Inside cells, and
        // a Gate cell only ever comes from THIS method retargeting a former Wall cell).
        //
        // DESIGN DECISION: candidates are Wall OR Gate, not Wall alone, so the search is idempotent across
        // multiple gate rooms. At this coarse building-cell resolution one grid cell is ~9 fine tiles (0.07
        // normalized), so two gate rooms on the same wall segment easily share a single true-nearest ring
        // cell. If candidates were Wall-only, the first gate's write would remove that cell from candidacy
        // before the second gate's search runs, and the second gate would silently claim the NEXT-nearest
        // ring cell instead — a 2-cell-wide opening where the room graph models one gate. Accepting Gate too
        // makes a second gate collapse onto the same cell as the first: at this resolution, two gates sharing
        // one cell genuinely ARE one opening.
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
                        if (g.Cells[a, b] != TileType.Wall && g.Cells[a, b] != TileType.Gate) continue;
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

        // ---- height (Task 5) -------------------------------------------------------------------------------
        // Per-building height, in CELL UNITS (the renderer — Task 7 — multiplies by pixel cell size). PURE
        // function of the room id alone: a house must not change height when the settlement is redrawn or
        // when some OTHER building is dragged/regenerated, so nothing here reads draw order, position, or any
        // other building's state — only `roomId`. WallHeight sits above BuildingHeightMax so the wall reads
        // as taller than the tallest possible house (SelfTestHeight pins this — strictly, WallHeight >
        // BuildingHeightMax, not merely close to it). GateHeight is a fixed constant, not derived per-gate —
        // only WallHeight/BuildingHeight vary; its value is not asserted by any self-test (out of this task's
        // scope, see the task brief's Interfaces block), so a future change to it is NOT caught here.
        public const float BuildingHeightMin = 0.55f, BuildingHeightMax = 1.10f;
        public const float WallHeight = 1.25f, GateHeight = 0.85f;

        // Explicit FNV-1a, byte-wise over roomId's 4 bytes (same offset basis/prime as InteriorOps.BuildingSeed,
        // which XORs the whole int in one step instead — this one is per-byte, per this task's brief). NEVER
        // string.GetHashCode / object.GetHashCode: both are randomized per .NET process, not stable across
        // runs — see InteriorOps.BuildingSeed's doc comment and this repo's regen-seed lesson (a stable
        // char-hash was substituted there for exactly this reason). h % 1024 keeps t's numerator a small
        // non-negative int so `/ 1024f` is an EXACT power-of-two division, with no accumulated rounding beyond
        // the final Min + t*(Max-Min) lerp.
        public static float BuildingHeight(int roomId)
        {
            unchecked
            {
                uint h = 2166136261u;
                uint v = (uint)roomId;
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(v & 0xFF);
                    h *= 16777619u;
                    v >>= 8;
                }
                float t = (h % 1024) / 1024f;
                return BuildingHeightMin + t * (BuildingHeightMax - BuildingHeightMin);
            }
        }
    }
}
