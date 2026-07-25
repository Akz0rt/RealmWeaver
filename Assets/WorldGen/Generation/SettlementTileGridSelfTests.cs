using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public class SettlementTileGridSelfTests : MonoBehaviour
    {
        // Build a settlement floor: buildings (TypeId=1) at lattice points, optional gate (TypeId=0).
        static InteriorFloor Floor(bool hasWall, params (int i, int j)[] cells)
        {
            float c = SettlementGenerator.BuildingCell, ax = 0.3f, ay = 0.3f;
            var f = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = hasWall } };
            int id = 1;
            foreach (var (i, j) in cells)
                f.Rooms.Add(new Room { Id = id++, TypeId = 1, X = ax + i * c, Y = ay + j * c });
            return f;
        }

        [ContextMenu("Self-Test: Tile Mapping")]
        public void SelfTestTileMapping()
        {
            bool ok = true;
            float c = SettlementGenerator.BuildingCell, ax = 0.3f, ay = 0.3f;
            var f = Floor(false, (0,0), (2,0), (0,3));
            var g = SettlementTileGrid.Allocate(f.Rooms);

            // each building maps to its lattice cell, centers round-trip
            foreach (var r in f.Rooms)
            {
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (System.Math.Abs(g.CenterX(i) - r.X) > 1e-4f || System.Math.Abs(g.CenterY(j) - r.Y) > 1e-4f)
                { Debug.LogError($"FAIL tilemap: room {r.Id} at ({r.X},{r.Y}) does not round-trip to cell ({i},{j}) center ({g.CenterX(i)},{g.CenterY(j)})"); ok = false; }
            }
            // anchor is the min building corner → its cell is 0
            if (g.CellI(ax) != 0 || g.CellJ(ay) != 0)
            { Debug.LogError($"FAIL tilemap: anchor building not at cell 0 (got {g.CellI(ax)},{g.CellJ(ay)})"); ok = false; }
            // extent covers bbox (i 0..2, j 0..3) plus MarginCells on each side
            int expW = (2 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            int expH = (3 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            if (g.W != expW || g.H != expH)
            { Debug.LogError($"FAIL tilemap: extent {g.W}x{g.H}, expected {expW}x{expH}"); ok = false; }
            if (g.OriginI != -SettlementTileGrid.MarginCells || g.OriginJ != -SettlementTileGrid.MarginCells)
            { Debug.LogError($"FAIL tilemap: origin ({g.OriginI},{g.OriginJ}) not (-margin,-margin)"); ok = false; }
            // snap picks the NEAREST cell centre, not floor/ceiling: an offset of 0.3*Cell above a lattice
            // point must snap DOWN to that point's centre; an offset of 0.7*Cell must snap UP to the next
            // cell's centre. Floor gets the 0.7 case wrong (stays at the lower centre); Ceiling gets the 0.3
            // case wrong (jumps to the upper centre) — so together these pin Round specifically. (The old
            // idempotency check — SnapX(SnapX(x)) — held for Floor/Ceiling/Round alike, since the second call
            // always receives an exact lattice value; it could not tell them apart.)
            float snapDownX = g.SnapX(ax + 0.3f * c);
            if (System.Math.Abs(snapDownX - ax) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapX(ax+0.3*Cell) = {snapDownX}, want {ax} (snap DOWN to the lattice point)"); ok = false; }
            float snapUpX = g.SnapX(ax + 0.7f * c);
            float expSnapUpX = ax + c;
            if (System.Math.Abs(snapUpX - expSnapUpX) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapX(ax+0.7*Cell) = {snapUpX}, want {expSnapUpX} (snap UP to the next cell)"); ok = false; }
            float snapDownY = g.SnapY(ay + 0.3f * c);
            if (System.Math.Abs(snapDownY - ay) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapY(ay+0.3*Cell) = {snapDownY}, want {ay} (snap DOWN to the lattice point)"); ok = false; }
            float snapUpY = g.SnapY(ay + 0.7f * c);
            float expSnapUpY = ay + c;
            if (System.Math.Abs(snapUpY - expSnapUpY) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapY(ay+0.7*Cell) = {snapUpY}, want {expSnapUpY} (snap UP to the next cell)"); ok = false; }

            if (ok) Debug.Log("Settlement TileGrid Mapping: PASS");
        }

        [ContextMenu("Self-Test: Wall Ring")]
        public void SelfTestWallRing()
        {
            bool ok = true;
            // 8 buildings ringing an empty centre at (1,1)
            var f = Floor(true, (0,0),(1,0),(2,0),(0,1),(2,1),(0,2),(1,2),(2,2));
            var g = SettlementTileGrid.Build(f, null);

            // no hole: the enclosed centre is Inside → Void, never outside-None
            if (g.At(1,1) != TileType.Void)
            { Debug.LogError($"FAIL wallring: enclosed centre (1,1) is {g.At(1,1)}, expected Void (outside fill leaked into a hole)"); ok = false; }
            // wall is the OUTER ring — one courtyard cell out from the buildings
            if (g.At(-2,1) != TileType.Wall)
            { Debug.LogError($"FAIL wallring: cell (-2,1) is {g.At(-2,1)}, expected Wall (outermost ring)"); ok = false; }
            // the user-chosen gap: Building → Void(courtyard) → Wall, so a building is NEVER flush to a wall
            if (!(g.At(0,1) == TileType.Building && g.At(-1,1) == TileType.Void && g.At(-2,1) == TileType.Wall))
            { Debug.LogError($"FAIL wallring: no Building→Void→Wall gap at row 1 — got {g.At(0,1)}/{g.At(-1,1)}/{g.At(-2,1)} (building is flush to the wall)"); ok = false; }
            // building cells stay Building
            if (g.At(0,0) != TileType.Building)
            { Debug.LogError($"FAIL wallring: building (0,0) is {g.At(0,0)}, expected Building"); ok = false; }
            // HasWall=false → no Wall and no Void (no Inside/Outside split)
            var open = SettlementTileGrid.Build(Floor(false, (0,0),(1,0),(2,0),(0,1),(2,1),(0,2),(1,2),(2,2)), null);
            int walls = 0, voids = 0;
            for (int a=0;a<open.W;a++) for (int b=0;b<open.H;b++)
            { if (open.Cells[a,b]==TileType.Wall) walls++; if (open.Cells[a,b]==TileType.Void) voids++; }
            if (walls != 0 || voids != 0)
            { Debug.LogError($"FAIL wallring: open village has {walls} Wall + {voids} Void cells, expected 0/0"); ok = false; }

            // Second fixture: the no-hole guarantee itself. The 3x3-ring fixture above can't test this — its
            // radius-2 dilation directly occupies the "hole" at (1,1) (Chebyshev-1 from every ring building),
            // so (1,1) is Inside via dilation, never merely enclosed. Buildings on the PERIMETER of a 7x7 block
            // (i,j in 0..6, i in {0,6} or j in {0,6}, 24 buildings) push every building at least 3 cells from
            // the centre: for world cell (i,j), the nearest building is exactly min(i,6-i,j,6-j) cells away
            // (each edge has a building matching the other coordinate exactly), so a cell is occupied by the
            // radius-2 dilation iff that min is <=2. The unique cell with min(i,6-i,j,6-j) >= 3 in range 0..6
            // is (3,3) itself. So (3,3) is genuinely unoccupied, 4-surrounded by occupied cells, and Inside
            // ONLY because the outside flood-fill cannot reach it through the ring — exactly the no-hole
            // guarantee. Correct code -> Void; a fill that never actually walks the outside (e.g. seeded from
            // one border cell, or that leaks through a broken connectivity rule) leaves (3,3) as None.
            var perimeter = new System.Collections.Generic.List<(int i, int j)>();
            for (int i = 0; i <= 6; i++)
                for (int j = 0; j <= 6; j++)
                    if (i == 0 || i == 6 || j == 0 || j == 6)
                        perimeter.Add((i, j));
            var gHole = SettlementTileGrid.Build(Floor(true, perimeter.ToArray()), null);
            if (gHole.At(3, 3) != TileType.Void)
            { Debug.LogError($"FAIL wallring: 7x7-perimeter fixture's enclosed centre (3,3) is {gHole.At(3, 3)}, expected Void (outside flood-fill did not reach it — a real hole)"); ok = false; }

            if (ok) Debug.Log("Settlement Wall Ring: PASS");
        }

        [ContextMenu("Self-Test: Roads and Gates")]
        public void SelfTestRoadsAndGates()
        {
            bool ok = true;
            float c = SettlementGenerator.BuildingCell, ax = 0.3f, ay = 0.3f;
            float T = DungeonLayout.TilesPerAxis;

            // buildings leave the centre (1,1) empty; a road runs along row j=1 across it.
            var f = Floor(true, (0,0),(2,0),(0,1),(2,1),(0,2),(2,2));

            // OVERRIDE 2: the brief's original fixture placed the gate exactly ON the ring cell (-2,1) —
            // that never exercises "find the NEAREST ring cell", since the gate WAS the ring cell already. A
            // real gate sits on the fine fence, ~1.5 fine tiles from the built-up edge (SettlementGenerator
            // places gates on a fence traced tight around the actual buildings) — ~0.0117 normalized, an
            // order of magnitude closer than the coarse ring (2 cells = 0.14 normalized out). The whole west
            // ring column (i=-2) is Wall at every j from -2..4 (this fixture's dilation makes it one solid
            // block, see the class doc), so placing the gate at that realistic distance and offset to row j=1
            // forces the nearest-cell search to actually discriminate the target (-2,1) — 0.1283 normalized
            // away — from its column neighbours (-2,0)/(-2,2) — 0.1462 away each — rather than trivially
            // matching a cell the gate already sits on.
            float gateOffset = 1.5f / T;                 // ~0.0117 normalized: realistic fine-fence clearance
            f.Rooms.Add(new Room { Id = 99, TypeId = 0, X = ax + 0 * c - gateOffset, Y = ay + 1 * c });

            var roads = new System.Collections.Generic.List<LinkSegment> {
                // crossing road: west building row -> east building row, straight through the courtyard gap
                new LinkSegment { A = new LinkPoint { X = (ax + 0*c)*T, Y = (ay + 1*c)*T },
                                   B = new LinkPoint { X = (ax + 2*c)*T, Y = (ay + 1*c)*T }, EdgeIndex = 0 },
                // OVERRIDE 1: a spur leaving the buildings' bbox entirely. Its far tip sits 10 cells south of
                // the building block; MarginCells (3) alone would only ever reach 5 cells out from the
                // buildings, so representing this tip REQUIRES the allocation to fold in the road extent, not
                // just the buildings' bbox. Starts at the courtyard cell the crossing road already occupies,
                // so the whole road network stays one connected blob (no stray-bridge machinery needed here).
                new LinkSegment { A = new LinkPoint { X = (ax + 1*c)*T, Y = (ay + 1*c)*T },
                                   B = new LinkPoint { X = (ax + 1*c)*T, Y = (ay + 10*c)*T }, EdgeIndex = 1 },
            };

            var clean = SettlementTileGrid.Build(f, roads);

            // ---- roads: rasterized, and precedence-guarded (Building > ... > Road) ----
            if (clean.At(1,1) != TileType.Road)
            { Debug.LogError($"FAIL roads: courtyard cell (1,1) is {clean.At(1,1)}, expected Road (a road crosses it)"); ok = false; }
            if (clean.At(0,1) != TileType.Building)
            { Debug.LogError($"FAIL roads: cell (0,1) is {clean.At(0,1)}, expected Building — road overwrote a building (precedence broken)"); ok = false; }

            // ---- OVERRIDE 1: the spur's far tip is REPRESENTED (grid extent folded past the buildings' own
            // bbox) and ENCLOSED (the wall wraps it — not left as a silently-dropped/Outside cell) ----
            if (!clean.InBounds(1, 10))
            { Debug.LogError($"FAIL roads: far spur cell (1,10) is OUT of bounds — grid is {clean.W}x{clean.H} @ ({clean.OriginI},{clean.OriginJ}) — the grid extent was not folded over the routed road (OVERRIDE 1)"); ok = false; }
            else if (clean.At(1, 10) != TileType.Road)
            { Debug.LogError($"FAIL roads: far spur cell (1,10) is {clean.At(1, 10)}, expected Road (present but misclassified)"); ok = false; }
            // Expected row expressed via CourtyardCells (a tunable knob — the dilation radius BuildWallRing
            // uses is CourtyardCells + 1) rather than a bare literal, so a future retune of CourtyardCells
            // still produces an intelligible mismatch instead of a bare "expected Wall" against a stale row.
            int spurWallRow = 10 + SettlementTileGrid.CourtyardCells + 1;
            if (clean.At(1, spurWallRow) != TileType.Wall)
            { Debug.LogError($"FAIL roads: cell (1,{spurWallRow}), CourtyardCells+1 beyond the spur's tip (row 10), is {clean.At(1, spurWallRow)}, expected Wall — the wall must wrap the spur, not just the buildings"); ok = false; }

            // ---- OVERRIDE 2: the gate reclassifies the NEAREST ring cell, on the correct side — not just
            // "some ring cell somewhere" (the opposite wall must stay Wall) ----
            if (clean.At(-2, 1) != TileType.Gate)
            { Debug.LogError($"FAIL roads: west wall cell (-2,1) is {clean.At(-2,1)}, expected Gate (nearest ring cell to the realistic-distance gate)"); ok = false; }
            if (clean.At(4, 1) != TileType.Wall)
            { Debug.LogError($"FAIL roads: opposite (east) wall cell (4,1) is {clean.At(4,1)}, expected Wall — only the nearest ring cell should reclassify"); ok = false; }

            // ---- Fast tier: null roads -> no Road cells, and no folded extent (wall/void/gates still
            // present — gates don't depend on roads) ----
            var fast = SettlementTileGrid.Build(f, null);
            if (fast.InBounds(1, 10))
            { Debug.LogError($"FAIL roads: Fast tier (buildings-only extent) already covers the far spur cell (1,10) — grid is {fast.W}x{fast.H} @ ({fast.OriginI},{fast.OriginJ}) — the OVERRIDE 1 extent-fold assertion above is not load-bearing"); ok = false; }
            int roadCells = 0; for (int a=0;a<fast.W;a++) for (int b=0;b<fast.H;b++) if (fast.Cells[a,b]==TileType.Road) roadCells++;
            if (roadCells != 0)
            { Debug.LogError($"FAIL roads: Fast tier (null roads) produced {roadCells} Road cells, expected 0"); ok = false; }
            if (fast.At(-2, 1) != TileType.Gate)
            { Debug.LogError($"FAIL roads: Fast tier gate reclassify missing — (-2,1) is {fast.At(-2,1)}, expected Gate"); ok = false; }

            // ---- fix: an UNWALLED settlement (HasWall=false) must still get its roads. Reachable in
            // production: MapScreenController sets HasWall = (poi.Type == PoiType.City), so every Village is
            // unwalled, and SettlementStreets still generates streets for gate-less towns (hub-seeded growth,
            // see that file's class doc) — without this, every Village would render as houses with zero
            // streets. Same building layout as `f` above but HasWall=false and no gate room, roaded the same
            // way, so this exercises MarkRoads' `inside == null` branch (no Inside test at all) rather than
            // the walled branch the assertions above already cover. ----
            var openFloor = Floor(false, (0,0),(2,0),(0,1),(2,1),(0,2),(2,2));
            var openRoads = new System.Collections.Generic.List<LinkSegment> {
                new LinkSegment { A = new LinkPoint { X = (ax + 0*c)*T, Y = (ay + 1*c)*T },
                                   B = new LinkPoint { X = (ax + 2*c)*T, Y = (ay + 1*c)*T }, EdgeIndex = 0 },
            };
            var openClean = SettlementTileGrid.Build(openFloor, openRoads);
            if (openClean.At(1,1) != TileType.Road)
            { Debug.LogError($"FAIL roads: unwalled courtyard cell (1,1) is {openClean.At(1,1)}, expected Road — HasWall=false must not drop roads (village streets would vanish)"); ok = false; }
            if (openClean.At(0,1) != TileType.Building)
            { Debug.LogError($"FAIL roads: unwalled cell (0,1) is {openClean.At(0,1)}, expected Building — road overwrote a building (precedence broken)"); ok = false; }
            int openWalls = 0, openVoids = 0;
            for (int a = 0; a < openClean.W; a++) for (int b = 0; b < openClean.H; b++)
            { if (openClean.Cells[a,b] == TileType.Wall) openWalls++; if (openClean.Cells[a,b] == TileType.Void) openVoids++; }
            if (openWalls != 0 || openVoids != 0)
            { Debug.LogError($"FAIL roads: unwalled+roaded settlement has {openWalls} Wall + {openVoids} Void cells, expected 0/0 — HasWall=false must still mean no Inside/Outside split (Task 2's contract)"); ok = false; }

            if (ok) Debug.Log("Settlement Roads and Gates: PASS");
        }

        [ContextMenu("Self-Test: Depth Order")]
        public void SelfTestDepth()
        {
            bool ok = true;
            // a wall cell directly in front (south, larger row) of a building behind it
            var f = Floor(true, (0,0),(1,0),(0,1),(1,1));
            var g = SettlementTileGrid.Build(f, null);
            var order = g.DrawOrder();
            int Idx(int i, int j) => order.FindIndex(t => t.i == i && t.j == j);

            // NearOccludesFar: for any two occupied cells, larger row => larger draw index
            for (int m = 0; m < order.Count; m++) for (int n = 0; n < order.Count; n++)
                if (order[m].j < order[n].j && !(m < n))
                { Debug.LogError($"FAIL depth: cell {order[m]} (row {order[m].j}) must draw before {order[n]} (row {order[n].j})"); ok = false; }

            // WallOccludesBuildingBehind: front wall (row j) after building (row j-1), same column.
            // Verified against the ACTUAL grid this fixture produces (hand-derived and confirmed by this very
            // test running green): the 2x2 building block at (0,0)-(1,1) dilates (radius CourtyardCells+1=2)
            // to a solid occupied square spanning world i,j in [-2,3]; the outside flood-fill cannot enter it
            // (no holes), so Inside == that whole square; its OUTERMOST ring (i==-2, i==3, j==-2, or j==3) is
            // Wall, everything one cell further in that isn't a Building is Void (the courtyard). So column
            // i=1's south face reads Building(1,1) -> Void(1,2) -> Wall(1,3).
            //
            // NOTE: this same-column pair is the physically real case (this renderer's height spill is
            // vertical-only, so only same-i pairs can ever actually occlude on screen) but it does NOT, on
            // its own, discriminate MutDepthKeyNoRowSort: under column-major sorting a tied `i` still breaks
            // ties by ascending `j`, so a same-column pair's relative order survives the mutant unchanged —
            // confirmed empirically (this assertion produces zero errors under that mutant; NearOccludesFar's
            // cross-column sweep is what actually fails it). Kept for the real-geometry documentation and
            // because it is unconditional (it would still catch other defects, e.g. a reversed sort). The
            // SECOND pair below is the one that independently discriminates the required mutant.
            int bi = 1, bj = 1;                    // a building (front row of the block)
            int wi = 1, wj = 3;                    // the south wall cell in front of it (courtyard at j=2 between)
            // Each fixture assumption is asserted UNCONDITIONALLY (not folded into the occlusion check's
            // guard) — a fixture that stopped producing this exact Wall/Building pair must fail loudly here,
            // not silently skip the occlusion assertion below.
            if (g.At(bi,bj) != TileType.Building)
            { Debug.LogError($"FAIL depth: fixture cell ({bi},{bj}) is {g.At(bi,bj)}, expected Building — fixture assumption broken, WallOccludes check would be vacuous"); ok = false; }
            if (g.At(wi,wj) != TileType.Wall)
            { Debug.LogError($"FAIL depth: fixture cell ({wi},{wj}) is {g.At(wi,wj)}, expected Wall — fixture assumption broken, WallOccludes check would be vacuous"); ok = false; }
            if (!(Idx(wi,wj) > Idx(bi,bj)))
            { Debug.LogError($"FAIL depth: front wall ({wi},{wj}) idx {Idx(wi,wj)} does not draw AFTER building behind ({bi},{bj}) idx {Idx(bi,bj)}"); ok = false; }

            // WallOccludesBuildingBehind — CROSS-COLUMN discriminator. Under row-major DepthKey, j is
            // PRIMARY, so any wall in a later row than a building must draw after it regardless of column.
            // Under the required MutDepthKeyNoRowSort mutant (i made primary instead), a wall whose i is
            // SMALLER than the building's i can sort BEFORE it even though the wall's row is larger — which
            // is exactly what independently catches that mutant (the same-column pair above cannot, since a
            // tied i never exercises which of i/j is primary). (-2,3) is the wall ring's SW corner (west wall
            // column i=-2, south wall row j=3 — on the boundary derived above); (1,1) is the block's SE
            // building. Correct: key(-2,3) = 3*1_000_000 + (-2) = 2,999,998 > key(1,1) = 1*1_000_000 + 1 =
            // 1,000,001, so the wall draws after. Column-major mutant: key(-2,3) = -2*1_000_000 + 3 =
            // -1,999,997 < key(1,1) = 1,000,001, so the wall draws BEFORE the building — this assertion fires.
            int bi2 = 1, bj2 = 1;                   // same SE building as above
            int wi2 = -2, wj2 = 3;                  // the wall ring's SW corner — different column, later row
            if (g.At(bi2,bj2) != TileType.Building)
            { Debug.LogError($"FAIL depth: fixture cell ({bi2},{bj2}) is {g.At(bi2,bj2)}, expected Building — fixture assumption broken, cross-column WallOccludes check would be vacuous"); ok = false; }
            if (g.At(wi2,wj2) != TileType.Wall)
            { Debug.LogError($"FAIL depth: fixture cell ({wi2},{wj2}) is {g.At(wi2,wj2)}, expected Wall — fixture assumption broken, cross-column WallOccludes check would be vacuous"); ok = false; }
            if (!(Idx(wi2,wj2) > Idx(bi2,bj2)))
            { Debug.LogError($"FAIL depth: cross-column front wall ({wi2},{wj2}) idx {Idx(wi2,wj2)} does not draw AFTER building behind ({bi2},{bj2}) idx {Idx(bi2,bj2)}"); ok = false; }

            // SpillIsVisualOnly: DrawOrder must not mutate the grid, and must be PURE (idempotent — calling it
            // twice returns an equal list, not just "some list of the same length").
            var before = (TileType[,])g.Cells.Clone();
            var order2 = g.DrawOrder();
            for (int a=0;a<g.W;a++) for (int b=0;b<g.H;b++) if (before[a,b] != g.Cells[a,b])
            { Debug.LogError($"FAIL depth: DrawOrder mutated cell [{a},{b}] {before[a,b]}→{g.Cells[a,b]}"); ok = false; }
            if (order2.Count != order.Count)
            { Debug.LogError($"FAIL depth: DrawOrder not idempotent — second call returned {order2.Count} cells, first returned {order.Count}"); ok = false; }
            else
                for (int k = 0; k < order.Count; k++)
                    if (order[k] != order2[k])
                    { Debug.LogError($"FAIL depth: DrawOrder not idempotent — index {k} was {order[k]} on first call, {order2[k]} on second"); ok = false; }

            if (ok) Debug.Log("Settlement Depth Order: PASS");
        }

        [ContextMenu("Self-Test: TileGrid Sanity")]
        public void SelfTestTileGridSanity()
        {
            // Trailing non-reboundable sentinel: a plain smoke check so mutant-reboundable tests are never last.
            bool ok = true;
            var g = SettlementTileGrid.Allocate(new System.Collections.Generic.List<Room>());
            if (g == null)
            { Debug.LogError("FAIL tilegrid-sanity: empty Allocate returned null"); ok = false; }
            else if (g.W != 1 || g.H != 1)
            { Debug.LogError($"FAIL tilegrid-sanity: empty Allocate yielded {g.W}x{g.H}, want 1x1"); ok = false; }

            if (ok) Debug.Log("Settlement TileGrid Sanity: PASS");
        }
    }
}
