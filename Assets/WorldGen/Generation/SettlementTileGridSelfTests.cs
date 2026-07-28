using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public class SettlementTileGridSelfTests : MonoBehaviour
    {
        // The normalized position of lattice point k — i.e. the CENTRE of absolute cell k.
        //
        // FIXTURE CONVENTION, UNCHANGED: building (i,j) sits on the lattice point of cell (i,j), so every
        // world-cell coordinate the assertions below name is literally the index in Floor's argument list.
        // What changed with the fixed lattice is only WHERE that lattice point is. It used to be
        // 0.3 + i*Cell: the grid re-anchored itself on the min-X/min-Y building, so an arbitrary 0.3 became
        // cell 0 by construction. It is now the absolute lattice's own centre, (i + 0.5)*Cell. Since 0.3 was
        // arbitrary, that is a RIGID TRANSLATION of the whole fixture by (0.035 - 0.3) = -0.265 on BOTH axes:
        // every cell index, every inter-building distance, every gate-to-ring-cell distance and every
        // dilation radius is identical, so no assertion's expected value moves.
        static float P(int k) => SettlementFootprint.CenterOf(k);

        // Build a settlement floor: ONE single-cell building (TypeId=1) per listed cell.
        //
        // FOOTPRINTS, NOT POINTS: every room now carries an explicit Room.Cells footprint. The point is set
        // too, and set CONSISTENTLY (X/Y = the footprint cell's own centre) — that is not decoration, it is
        // required: SettlementTileGrid.FootprintOf treats a SINGLE-cell footprint that disagrees with the
        // room's point as STALE and re-derives it from the point, so a fixture that wrote Cells and left X/Y
        // at 0 would silently collapse every building onto cell (0,0).
        //
        // WHY NO EXPECTED VALUE IN THIS FILE MOVED: for a single-cell footprint {(i,j)} with X = CenterOf(i),
        // FootprintOf returns exactly {(i,j)} — the same cell the pre-footprint code computed as
        // (CellI(r.X), CellJ(r.Y)). So every grid this helper produces is bit-identical to the one the same
        // argument list produced before footprints existed, and SelfTestTileMapping / SelfTestWallRing /
        // SelfTestDepth keep their hand-derived numbers untouched.
        static InteriorFloor Floor(bool hasWall, params (int i, int j)[] cells)
        {
            var f = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = hasWall } };
            int id = 1;
            foreach (var (i, j) in cells)
                f.Rooms.Add(One(id++, i, j));
            return f;
        }

        // One single-cell building: footprint {(i,j)} and the matching point.
        static Room One(int id, int i, int j) => new Room
        {
            Id = id, TypeId = 1, X = P(i), Y = P(j),
            Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (i, j) })
        };

        // One MULTI-cell building. `pointCell` is where the room's stored point sits — normally the
        // footprint's representative cell, but the staleness fixtures deliberately put it elsewhere.
        static Room Many(int id, (int i, int j) pointCell, params (int i, int j)[] cells) => new Room
        {
            Id = id, TypeId = 1, X = P(pointCell.i), Y = P(pointCell.j),
            Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)>(cells))
        };

        // A rectangular footprint, w cells east by h cells south of (i0, j0), row-major.
        static (int i, int j)[] Rect(int i0, int j0, int w, int h)
        {
            var cells = new System.Collections.Generic.List<(int i, int j)>();
            for (int j = j0; j < j0 + h; j++)
                for (int i = i0; i < i0 + w; i++)
                    cells.Add((i, j));
            return cells.ToArray();
        }

        // NOTE: there is deliberately NO shared "count cells of type T" helper here, and the counting loops
        // below are spelled out inline instead. A file-level static helper is NOT rebound by the mutant
        // machinery (sync.ps1's New-SettlementRebind rewrites only the catching METHOD's body), so a helper
        // whose signature names SettlementTileGrid/TileType would be handed the mutant's nested types and
        // fail to compile — the rebound copy would never even run.

        [ContextMenu("Self-Test: Tile Mapping")]
        public void SelfTestTileMapping()
        {
            bool ok = true;
            float c = SettlementGenerator.BuildingCell, ax = P(0), ay = P(0);
            var f = Floor(false, (0,0), (2,0), (0,3));
            var g = SettlementTileGrid.Allocate(f.Rooms);

            // each building maps to its lattice cell, centers round-trip
            foreach (var r in f.Rooms)
            {
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (System.Math.Abs(g.CenterX(i) - r.X) > 1e-4f || System.Math.Abs(g.CenterY(j) - r.Y) > 1e-4f)
                { Debug.LogError($"FAIL tilemap: room {r.Id} at ({r.X},{r.Y}) does not round-trip to cell ({i},{j}) center ({g.CenterX(i)},{g.CenterY(j)})"); ok = false; }
            }
            // THE LATTICE IS ABSOLUTE. Cell 0 is the normalized span [0, Cell) — not "wherever the min-X/min-Y
            // building happens to be", which is what the old anchor-derived mapping meant. Two assertions,
            // because the first alone is satisfied by the old code too (the fixture's (0,0) building WAS the
            // min one): the second re-asks the SAME coordinate of a grid allocated from a COMPLETELY DIFFERENT
            // building set, which the anchored mapping cannot answer the same way.
            if (g.CellI(ax) != 0 || g.CellJ(ay) != 0)
            { Debug.LogError($"FAIL tilemap: normalized ({ax},{ay}) is cell ({g.CellI(ax)},{g.CellJ(ay)}), want (0,0)"); ok = false; }
            var elsewhere = SettlementTileGrid.Allocate(Floor(false, (7,9), (9,9)).Rooms);
            if (elsewhere.CellI(ax) != g.CellI(ax) || elsewhere.CellJ(ay) != g.CellJ(ay))
            { Debug.LogError($"FAIL tilemap: normalized ({ax},{ay}) is cell ({g.CellI(ax)},{g.CellJ(ay)}) on one grid but ({elsewhere.CellI(ax)},{elsewhere.CellJ(ay)}) on a grid built from different buildings — the lattice still depends on what is placed"); ok = false; }
            if (System.Math.Abs(elsewhere.CenterX(3) - g.CenterX(3)) > 1e-6f || System.Math.Abs(elsewhere.CenterY(3) - g.CenterY(3)) > 1e-6f)
            { Debug.LogError($"FAIL tilemap: cell (3,3) centres at ({g.CenterX(3)},{g.CenterY(3)}) on one grid and ({elsewhere.CenterX(3)},{elsewhere.CenterY(3)}) on another — the lattice still depends on what is placed"); ok = false; }
            // Half-open span [i*Cell, (i+1)*Cell): pins FLOOR specifically, not merely "some rounding rule".
            // An EXACT integer boundary (e.g. 2*Cell) does NOT discriminate — Floor, Round and Ceiling of an
            // exact integer all agree, so a prior version of this assertion (CellOf(2*Cell) == 2) passed
            // under all three and pinned nothing. 0.9*Cell sits INSIDE cell 0's own span (not yet at the
            // next boundary), so Floor keeps it in cell 0 while Round(0.9)->1 and Ceiling(0.9)->1 would not.
            float nearBoundary = 0.9f * c;
            if (g.CellI(nearBoundary) != 0)
            { Debug.LogError($"FAIL tilemap: 0.9*Cell = {nearBoundary} maps to cell {g.CellI(nearBoundary)}, want 0 (Floor keeps a sub-cell coordinate in its own span; Round/Ceiling would push it into cell 1)"); ok = false; }
            // extent covers bbox (i 0..2, j 0..3) plus MarginCells on each side
            int expW = (2 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            int expH = (3 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            if (g.W != expW || g.H != expH)
            { Debug.LogError($"FAIL tilemap: extent {g.W}x{g.H}, expected {expW}x{expH}"); ok = false; }
            if (g.OriginI != -SettlementTileGrid.MarginCells || g.OriginJ != -SettlementTileGrid.MarginCells)
            { Debug.LogError($"FAIL tilemap: origin ({g.OriginI},{g.OriginJ}) not (-margin,-margin)"); ok = false; }
            // Snap = the centre of the cell the coordinate FALLS IN. ax is cell 0's centre, i.e. already 0.5
            // of a cell into cell 0's span, so an offset of +0.3*Cell lands 0.8 into that span (still cell 0,
            // snaps back DOWN to ax) and +0.7*Cell lands 1.2 in (cell 1, snaps UP to ax + Cell). Together
            // these still pin Floor specifically, with the two roles swapped relative to the old anchored
            // lattice: the 0.3 case now rules out BOTH Round (0.8 -> 1) and Ceiling (0.8 -> 1), and the 0.7
            // case rules out Ceiling (1.2 -> 2). (An idempotency check — SnapX(SnapX(x)) — would hold for
            // Floor/Ceiling/Round alike, since the second call always receives an exact lattice value; it
            // could not tell them apart.)
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
            var g = SettlementTileGrid.Build(f);

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
            var open = SettlementTileGrid.Build(Floor(false, (0,0),(1,0),(2,0),(0,1),(2,1),(0,2),(1,2),(2,2)));
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
            var gHole = SettlementTileGrid.Build(Floor(true, perimeter.ToArray()));
            if (gHole.At(3, 3) != TileType.Void)
            { Debug.LogError($"FAIL wallring: 7x7-perimeter fixture's enclosed centre (3,3) is {gHole.At(3, 3)}, expected Void (outside flood-fill did not reach it — a real hole)"); ok = false; }

            if (ok) Debug.Log("Settlement Wall Ring: PASS");
        }

        [ContextMenu("Self-Test: Roads and Gates")]
        public void SelfTestRoadsAndGates()
        {
            bool ok = true;
            float c = SettlementGenerator.BuildingCell, ax = P(0), ay = P(0);
            float T = DungeonLayout.TilesPerAxis;

            // Six buildings leave the centre column i=1 empty; the STORED streets run along row j=1 across it
            // and then spur south. Streets are SettlementParams.StreetCells now — absolute lattice cells, not
            // routed LinkSegments — so this fixture names the exact cells the grid must classify instead of
            // relying on a rasterizer to land on them.
            var f = Floor(true, (0,0),(2,0),(0,1),(2,1),(0,2),(2,2));

            // THE `roads` FOLD THIS SECTION USED TO PIN IS GONE (Task 5). Allocate took an optional list of
            // routed LinkSegments and folded their endpoints into the extent, for a view fit that had to match
            // a renderer rasterizing those roads. Build stopped passing it at arc A task 2 and FitBoundsFor
            // stopped at Task 5, leaving a parameter with no production caller and this assertion as its only
            // exerciser — so the parameter went and the assertion with it. What replaces it, and what actually
            // matters, is the STREET-cell fold asserted further down: same "an occupied cell outside the
            // buildings' bbox must still be representable" property, on the data the grid really reads.

            // The street cell list, in three deliberate parts:
            //   (a) (0,1),(1,1),(2,1) — the crossing. (0,1) and (2,1) are ALSO building cells, on purpose:
            //       nothing stops a stored street cell from naming a stored footprint cell, so this is what
            //       keeps the Building > Road precedence guard pinned (MutTileGridRoadIgnoresBuilding).
            //   (b) (1,2)..(1,10) — a spur leaving the buildings' bbox entirely. Its far tip sits 10 cells
            //       south of the block; MarginCells (3) alone reaches only 5 cells out from the buildings, so
            //       representing this tip REQUIRES Allocate to fold the STREET cells into the extent.
            var streets = new System.Collections.Generic.List<(int i, int j)> { (0,1), (1,1), (2,1) };
            for (int j = 2; j <= 10; j++) streets.Add((1, j));
            f.SettlementParams.StreetCells = SettlementFootprint.Encode(streets);

            // The gate sits ~1.5 fine tiles west of building (0,1) — a realistic fine-fence clearance
            // (~0.0117 normalized), an order of magnitude closer than the coarse ring (2 cells = 0.14
            // normalized out). Placing it exactly ON the ring cell would never exercise "find the NEAREST ring
            // cell". The whole west ring column (i=-2) is Wall at every j from -2..4, so this offset forces
            // the search to discriminate the target (-2,1) — 0.1283 normalized away — from its column
            // neighbours (-2,0)/(-2,2), 0.1462 away each.
            float gateOffset = 1.5f / T;
            f.Rooms.Add(new Room { Id = 99, TypeId = 0, X = ax + 0 * c - gateOffset, Y = ay + 1 * c });

            var clean = SettlementTileGrid.Build(f);

            // ---- streets: marked Road, and precedence-guarded (Building > ... > Road) ----
            if (clean.At(1,1) != TileType.Road)
            { Debug.LogError($"FAIL roads: courtyard cell (1,1) is {clean.At(1,1)}, expected Road (a stored street cell names it)"); ok = false; }
            if (clean.At(0,1) != TileType.Building)
            { Debug.LogError($"FAIL roads: cell (0,1) is {clean.At(0,1)}, expected Building — a street cell overwrote a building footprint cell (precedence broken)"); ok = false; }

            // ---- the spur's far tip is REPRESENTED (grid extent folded over the STREET cells, past the
            // buildings' own bbox) and ENCLOSED (the wall wraps it — not left as a dropped/Outside cell) ----
            if (!clean.InBounds(1, 10))
            { Debug.LogError($"FAIL roads: far spur cell (1,10) is OUT of bounds — grid is {clean.W}x{clean.H} @ ({clean.OriginI},{clean.OriginJ}) — Allocate did not fold the street cells into the extent"); ok = false; }
            else if (clean.At(1, 10) != TileType.Road)
            { Debug.LogError($"FAIL roads: far spur cell (1,10) is {clean.At(1, 10)}, expected Road (present but misclassified)"); ok = false; }
            // RE-DERIVED for the narrow-spur rule (Task 2 of the street-access arc), not the old flat
            // CourtyardCells + 1 offset. The spur's far cells — (1,5) through the tip (1,10) — sit more than
            // OpenStreetNeighbourhood (2) Chebyshev cells from every building (the nearest is (0,2)/(2,2), and
            // (1,5) is already Chebyshev 3 away), so they take the NARROW branch and dilate by 1, not
            // CourtyardCells + 1 (2). The nearer part of the spur, (1,2)-(1,4), is still within 2 of a building
            // and keeps the wide radius, but its farthest reach (row 4 + 2 = 6) is short of the tip's own
            // dilation (row 10 + 1 = 11), so the tip's narrow radius is what actually determines the outermost
            // occupied row at column 1. Row 12 is therefore unoccupied — Outside — and row 11 is the wall.
            // This is still the assertion that pins "streets are folded into the RING SEED, not merely painted
            // Road afterwards" (MutGridStreetsNotSeeded): seed the ring from the buildings alone and the whole
            // spur — tip and wrapping ring both — falls outside the blob (occupied stops at row 4).
            int spurWallRow = 10 + 1;
            if (clean.At(1, spurWallRow) != TileType.Wall)
            { Debug.LogError($"FAIL roads: cell (1,{spurWallRow}), the NARROW dilation radius (1) beyond the spur's tip (row 10) — the tip has no building within OpenStreetNeighbourhood — is {clean.At(1, spurWallRow)}, expected Wall — the wall must wrap the streets, not just the buildings"); ok = false; }

            // ---- the gate reclassifies the NEAREST ring cell, on the correct side — not just "some ring cell
            // somewhere" (the opposite wall must stay Wall) ----
            if (clean.At(-2, 1) != TileType.Gate)
            { Debug.LogError($"FAIL roads: west wall cell (-2,1) is {clean.At(-2,1)}, expected Gate (nearest ring cell to the realistic-distance gate)"); ok = false; }
            if (clean.At(4, 1) != TileType.Wall)
            { Debug.LogError($"FAIL roads: opposite (east) wall cell (4,1) is {clean.At(4,1)}, expected Wall — only the nearest ring cell should reclassify"); ok = false; }

            // ---- NO stored streets -> no Road cells at all, and the buildings-only extent. This pair
            // replaces the deleted Fast-tier block (there is no Fast/Clean split any more) and carries the
            // same two loads: (a) Road comes from StreetCells and from nothing else — a grid that invented
            // Road cells from the room graph would fail the count; (b) the extent assertion above is not
            // vacuous — without the streets the far spur cell is genuinely out of bounds, so the InBounds
            // check there is really testing the fold. Same rooms, same gate; only StreetCells differs. ----
            var noStreets = Floor(true, (0,0),(2,0),(0,1),(2,1),(0,2),(2,2));
            noStreets.Rooms.Add(new Room { Id = 99, TypeId = 0, X = ax + 0 * c - gateOffset, Y = ay + 1 * c });
            var bare = SettlementTileGrid.Build(noStreets);
            if (bare.InBounds(1, 10))
            { Debug.LogError($"FAIL roads: street-less grid (buildings-only extent) already covers the far spur cell (1,10) — grid is {bare.W}x{bare.H} @ ({bare.OriginI},{bare.OriginJ}) — the extent-fold assertion above is not load-bearing"); ok = false; }
            int roadCells = 0; for (int a=0;a<bare.W;a++) for (int b=0;b<bare.H;b++) if (bare.Cells[a,b]==TileType.Road) roadCells++;
            if (roadCells != 0)
            { Debug.LogError($"FAIL roads: a settlement with StreetCells unset produced {roadCells} Road cells, expected 0 — Road must come from StreetCells and nothing else"); ok = false; }
            if (bare.At(-2, 1) != TileType.Gate)
            { Debug.LogError($"FAIL roads: street-less gate reclassify missing — (-2,1) is {bare.At(-2,1)}, expected Gate (a gate does not depend on streets)"); ok = false; }

            // ---- an UNWALLED settlement (HasWall=false) must still get its streets. Reachable in production:
            // a town is wall-less because the DM cleared «Со стеной», not because of its POI type, and
            // SettlementBlocks lays the same streets either way (HasWall only suppresses the GATES —
            // SettlementGenerator.BuildFloor) — without this, a wall-less town would render with zero streets.
            // Same building layout as `f` above but HasWall=false and no gate room, so this exercises
            // MarkRoads' `inside == null` branch (no Inside test at all) rather than the walled branch above. ----
            var openFloor = Floor(false, (0,0),(2,0),(0,1),(2,1),(0,2),(2,2));
            openFloor.SettlementParams.StreetCells = SettlementFootprint.Encode(
                new System.Collections.Generic.List<(int i, int j)> { (0,1), (1,1), (2,1) });
            var openClean = SettlementTileGrid.Build(openFloor);
            if (openClean.At(1,1) != TileType.Road)
            { Debug.LogError($"FAIL roads: unwalled courtyard cell (1,1) is {openClean.At(1,1)}, expected Road — HasWall=false must not drop streets (village streets would vanish)"); ok = false; }
            if (openClean.At(0,1) != TileType.Building)
            { Debug.LogError($"FAIL roads: unwalled cell (0,1) is {openClean.At(0,1)}, expected Building — a street cell overwrote a building (precedence broken)"); ok = false; }
            int openWalls = 0, openVoids = 0;
            for (int a = 0; a < openClean.W; a++) for (int b = 0; b < openClean.H; b++)
            { if (openClean.Cells[a,b] == TileType.Wall) openWalls++; if (openClean.Cells[a,b] == TileType.Void) openVoids++; }
            if (openWalls != 0 || openVoids != 0)
            { Debug.LogError($"FAIL roads: unwalled+streeted settlement has {openWalls} Wall + {openVoids} Void cells, expected 0/0 — HasWall=false must still mean no Inside/Outside split"); ok = false; }

            if (ok) Debug.Log("Settlement Roads and Gates: PASS");
        }

        [ContextMenu("Self-Test: Depth Order")]
        public void SelfTestDepth()
        {
            bool ok = true;
            // a wall cell directly in front (south, larger row) of a building behind it
            var f = Floor(true, (0,0),(1,0),(0,1),(1,1));
            var g = SettlementTileGrid.Build(f);
            // Cloned BEFORE the first DrawOrder() call (not after) so SpillIsVisualOnly's mutation check below
            // catches a first-call mutation too — a first-call mutation that happens to be idempotent and
            // None-preserving would otherwise escape both that diff check and the idempotency check.
            var before = (TileType[,])g.Cells.Clone();
            int occupied = 0;
            for (int a = 0; a < g.W; a++) for (int b = 0; b < g.H; b++) if (before[a,b] != TileType.None) occupied++;
            var order = g.DrawOrder();
            int Idx(int i, int j) => order.FindIndex(t => t.i == i && t.j == j);

            // OccupiedCellsOnly: DrawOrder's contract is "every occupied (non-None) cell, nothing else" — the
            // count pins over- AND under-inclusion (e.g. dropping the `!= TileType.None` filter, or skipping
            // Void cells), and the None sweep pins the array->world coordinate conversion (OriginI/OriginJ)
            // directly rather than incidentally via the row-sort/fixture checks below.
            if (order.Count != occupied)
            { Debug.LogError($"FAIL depth: DrawOrder returned {order.Count} cells, grid has {occupied} occupied"); ok = false; }
            foreach (var t in order) if (g.At(t.i,t.j) == TileType.None)
            { Debug.LogError($"FAIL depth: DrawOrder returned None cell ({t.i},{t.j})"); ok = false; }

            // NearOccludesFar: for any two occupied cells, larger row => larger draw index
            for (int m = 0; m < order.Count; m++) for (int n = 0; n < order.Count; n++)
                if (order[m].j < order[n].j && !(m < n))
                { Debug.LogError($"FAIL depth: cell {order[m]} (row {order[m].j}) idx {m} must draw before {order[n]} (row {order[n].j}) idx {n}"); ok = false; }

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
            // twice returns an equal list, not just "some list of the same length"). `before` was cloned above,
            // BEFORE the first DrawOrder() call, so this diff check spans both calls.
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

        [ContextMenu("Self-Test: Building Height")]
        public void SelfTestHeight()
        {
            bool ok = true;
            // deterministic (same-process, same call) — necessary but not sufficient: this alone would still
            // pass a per-process-seeded hash (e.g. string.GetHashCode) that is stable WITHIN one process but
            // reshuffles every restart. The pinned check below is what actually rules that out.
            if (SettlementTileGrid.BuildingHeight(7) != SettlementTileGrid.BuildingHeight(7))
            { Debug.LogError("FAIL height: BuildingHeight(7) not deterministic"); ok = false; }

            // PINNED: BuildingHeight(7) computed once from the shipped FNV-1a formula and hard-coded here —
            // guards against silent formula drift (offset basis / prime / byte order, or an accidental switch
            // to a per-process-seeded hash) that the same-process determinism check above cannot catch on its
            // own, since it only compares an output to itself, never to a value fixed outside the function.
            // Mirrors InteriorOpsSelfTests.SelfTestBuildingSeedPin's pinning of InteriorOps.BuildingSeed.
            // Tolerance, not exact equality: confirmed bit-exact under this harness's runtime, but this same
            // self-test also runs inside the Unity Editor (Mono/IL2CPP, possibly a different FMA-contraction
            // choice for Min + t*(Max-Min)), which could legitimately land 1 ulp off on correct code. 1e-6f
            // costs nothing: the smallest drift this pin can EVER actually detect is one bucket of
            // `h % 1024`, i.e. 0.55/1024 ~= 5.4e-4 — about 500x looser than the tolerance — so anything this
            // check can catch at all, it still catches with room to spare. (NOTE: this method's body must
            // never literally spell the ContextMenu attribute's own bracket-prefixed tag — even inside a
            // comment — since Tools/f2-harness's rebinding tool finds a method's end by scanning for that
            // exact tag, wherever it appears.)
            const float PinnedHeight7 = 1.08388671875f;   // BuildingHeight(7), computed once via the harness
            float h7 = SettlementTileGrid.BuildingHeight(7);
            if (System.Math.Abs(h7 - PinnedHeight7) > 1e-6f)
            { Debug.LogError($"FAIL height: BuildingHeight(7) = {h7:G9}, want the pinned {PinnedHeight7:G9} — formula drift?"); ok = false; }

            // in range
            for (int id = 1; id <= 40; id++)
            {
                float h = SettlementTileGrid.BuildingHeight(id);
                if (h < SettlementTileGrid.BuildingHeightMin || h > SettlementTileGrid.BuildingHeightMax)
                { Debug.LogError($"FAIL height: id {id} height {h} out of [{SettlementTileGrid.BuildingHeightMin},{SettlementTileGrid.BuildingHeightMax}]"); ok = false; }
            }

            // varies (not constant) — a MUCH higher bound than "at least a handful of distinct values": the
            // MutHeightConstant mutant (drops the FNV term entirely, always returns BuildingHeightMin)
            // collapses this to a set of size 1, and BuildingHeightMin is itself IN-RANGE, so the in-range
            // loop above cannot catch that mutant — only this check (and the spread check right after it) can.
            // >=20 of 40 ids landing on a distinct height is far above what any accidental near-constant
            // function could produce, while still nowhere near brittle (the real FNV-1a formula produces 40
            // distinct values here — see the task report).
            var set = new System.Collections.Generic.HashSet<float>();
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int id = 1; id <= 40; id++)
            {
                float h = SettlementTileGrid.BuildingHeight(id);
                set.Add(h);
                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
            if (set.Count < 20)
            { Debug.LogError($"FAIL height: only {set.Count} distinct heights across 40 ids (want >= 20) — height is barely varying"); ok = false; }

            // spread: the distinct values must actually SPAN the [Min,Max] range, not cluster near one point.
            // A function that alternates between two nearby values (e.g. Min and Min+epsilon) could pass a
            // bare distinct-count check yet still read as visually constant once drawn — this catches that.
            float spread = maxH - minH;
            float range = SettlementTileGrid.BuildingHeightMax - SettlementTileGrid.BuildingHeightMin;
            if (spread < 0.5f * range)
            { Debug.LogError($"FAIL height: heights span only {spread:G9} of a possible {range:G9} across 40 ids — clustered, not spread"); ok = false; }

            // Strict: the requirement is "WallHeight is ABOVE the tallest house", so the pass/fail boundary
            // must sit exactly at BuildingHeightMax — any slack here (e.g. "- 0.01f") tolerates a real
            // violation (a WallHeight only 0.005 below BuildingHeightMax would then read the wall as SHORTER
            // than the tallest house and still pass).
            // Read both consts into locals first: comparing them directly folds at compile time, which makes
            // the LogError body unreachable and raises CS0162 in the Unity build (the harness NoWarns it, the
            // Editor does not). The locals keep the check a real runtime assertion and the console clean.
            float wallH = SettlementTileGrid.WallHeight, tallestHouse = SettlementTileGrid.BuildingHeightMax;
            if (wallH <= tallestHouse)
            { Debug.LogError($"FAIL height: WallHeight {wallH} not above the tallest house {tallestHouse}"); ok = false; }

            if (ok) Debug.Log("Settlement Height: PASS");
        }

        [ContextMenu("Self-Test: Footprint Tiles")]
        public void SelfTestFootprintTiles()
        {
            bool ok = true;

            // ---- A. A 2x3 footprint marks ALL SIX of its cells, and nothing else -----------------------
            // HasWall=false throughout this test: no ring, no courtyard, so every non-Building cell reads
            // None and a count of Building cells is an exact statement about the footprint pass alone.
            var fA = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            var rectA = Rect(0, 0, 2, 3);                       // (0,0),(1,0),(0,1),(1,1),(0,2),(1,2)
            fA.Rooms.Add(Many(1, (0, 0), rectA));
            var gA = SettlementTileGrid.Build(fA);
            foreach (var cell in rectA)
                if (gA.At(cell.i, cell.j) != TileType.Building)
                { Debug.LogError($"FAIL footprint-tiles: 2x3 footprint cell ({cell.i},{cell.j}) is {gA.At(cell.i, cell.j)}, expected Building — only part of the footprint was drawn"); ok = false; }
            int buildA = 0; for (int a=0;a<gA.W;a++) for (int b=0;b<gA.H;b++) if (gA.Cells[a,b]==TileType.Building) buildA++;
            if (buildA != rectA.Length)
            { Debug.LogError($"FAIL footprint-tiles: a 2x3 footprint produced {buildA} Building cells, expected {rectA.Length}"); ok = false; }
            // The two cells immediately past the footprint's east and south edges must stay empty — named
            // explicitly rather than left to the count above, so an over-marking bug says WHERE.
            if (gA.At(2, 0) != TileType.None)
            { Debug.LogError($"FAIL footprint-tiles: cell (2,0), one east of the 2x3 footprint, is {gA.At(2,0)}, expected None"); ok = false; }
            if (gA.At(0, 3) != TileType.None)
            { Debug.LogError($"FAIL footprint-tiles: cell (0,3), one south of the 2x3 footprint, is {gA.At(0,3)}, expected None"); ok = false; }

            // ---- B. Two FLUSH footprints each keep their own cells --------------------------------------
            // Adjacency between buildings is legal now (that is the whole point of blocks), so this must not
            // merge, drop or overwrite either side. Two 1x2 bars sharing the i=0/i=1 seam.
            var fB = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            var west = Rect(0, 0, 1, 2);                        // (0,0),(0,1)
            var east = Rect(1, 0, 1, 2);                        // (1,0),(1,1)
            fB.Rooms.Add(Many(10, (0, 0), west));
            fB.Rooms.Add(Many(11, (1, 0), east));
            // Non-vacuity: the fixture really is two DISJOINT footprints, so "each keeps its own cells" is a
            // claim about adjacency and not about an overlap that was never there.
            if (SettlementFootprint.Overlaps(SettlementFootprint.Decode(fB.Rooms[0].Cells), SettlementFootprint.Decode(fB.Rooms[1].Cells)))
            { Debug.LogError("FAIL footprint-tiles: the two flush fixture footprints already share a cell — the flush assertions below would be vacuous"); ok = false; }
            var gB = SettlementTileGrid.Build(fB);
            foreach (var cell in west)
                if (gB.At(cell.i, cell.j) != TileType.Building)
                { Debug.LogError($"FAIL footprint-tiles: west building's cell ({cell.i},{cell.j}) is {gB.At(cell.i, cell.j)}, expected Building — a flush neighbour cost it a cell"); ok = false; }
            foreach (var cell in east)
                if (gB.At(cell.i, cell.j) != TileType.Building)
                { Debug.LogError($"FAIL footprint-tiles: east building's cell ({cell.i},{cell.j}) is {gB.At(cell.i, cell.j)}, expected Building — a flush neighbour cost it a cell"); ok = false; }
            int buildB = 0; for (int a=0;a<gB.W;a++) for (int b=0;b<gB.H;b++) if (gB.Cells[a,b]==TileType.Building) buildB++;
            if (buildB != west.Length + east.Length)
            { Debug.LogError($"FAIL footprint-tiles: two flush 1x2 footprints produced {buildB} Building cells, expected {west.Length + east.Length}"); ok = false; }
            // And each ROOM still reports its own cells — a flush neighbour must not have re-derived either
            // footprint (the multi-cell never-re-derive rule, checked here on an adjacency fixture).
            var fpWest = SettlementTileGrid.FootprintOf(fB.Rooms[0]);
            var fpEast = SettlementTileGrid.FootprintOf(fB.Rooms[1]);
            if (fpWest.Count != west.Length || fpEast.Count != east.Length)
            { Debug.LogError($"FAIL footprint-tiles: flush neighbours report {fpWest.Count}/{fpEast.Count} cells, expected {west.Length}/{east.Length}"); ok = false; }

            // ---- C. The EXTENT folds every footprint cell, not one per room -----------------------------
            // A horizontal bar long enough that its far end cannot fit inside a representative-only extent:
            // that extent spans i in [-MarginCells, +MarginCells] around the representative, so the bar is
            // made MarginCells + 4 cells wide and its far cell sits at i = MarginCells + 3. Derived from
            // MarginCells rather than hard-coded so a future margin retune cannot silently make this vacuous.
            int barW = SettlementTileGrid.MarginCells + 4;
            int farI = barW - 1;
            if (farI <= SettlementTileGrid.MarginCells)
            { Debug.LogError($"FAIL footprint-tiles: the bar's far cell i={farI} is within MarginCells ({SettlementTileGrid.MarginCells}) of the representative — a representative-only extent would still contain it and this fixture proves nothing"); ok = false; }
            var fC = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            var bar = Rect(0, 0, barW, 1);
            fC.Rooms.Add(Many(1, (0, 0), bar));
            var gC = SettlementTileGrid.Build(fC);
            if (!gC.InBounds(farI, 0))
            { Debug.LogError($"FAIL footprint-tiles: the bar's far cell ({farI},0) is OUT of bounds — grid is {gC.W}x{gC.H} @ ({gC.OriginI},{gC.OriginJ}) — Allocate sized the extent from one cell per room, so the far cells are silently dropped by the InBounds guards"); ok = false; }
            else if (gC.At(farI, 0) != TileType.Building)
            { Debug.LogError($"FAIL footprint-tiles: the bar's far cell ({farI},0) is {gC.At(farI, 0)}, expected Building (in bounds but never written)"); ok = false; }
            int buildC = 0; for (int a=0;a<gC.W;a++) for (int b=0;b<gC.H;b++) if (gC.Cells[a,b]==TileType.Building) buildC++;
            if (buildC != barW)
            { Debug.LogError($"FAIL footprint-tiles: a 1x{barW} bar produced {buildC} Building cells, expected {barW}"); ok = false; }

            // ---- D. NO footprint at all -> one cell, derived from the room's point ----------------------
            // This is the GENERATED town: SettlementGenerator.BuildFloor does not populate Room.Cells, while a
            // reloaded town has single-cell footprints from the v10 migration. Both must render the same.
            var fD = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            fD.Rooms.Add(new Room { Id = 1, TypeId = 1, X = P(4), Y = P(5) });      // Cells left null
            var gD = SettlementTileGrid.Build(fD);
            if (gD.At(4, 5) != TileType.Building)
            { Debug.LogError($"FAIL footprint-tiles: a room with NO footprint at point cell (4,5) reads {gD.At(4,5)}, expected Building (the point fallback did not fire)"); ok = false; }
            int buildD = 0; for (int a=0;a<gD.W;a++) for (int b=0;b<gD.H;b++) if (gD.Cells[a,b]==TileType.Building) buildD++;
            if (buildD != 1)
            { Debug.LogError($"FAIL footprint-tiles: a room with no footprint produced {buildD} Building cells, expected exactly 1"); ok = false; }

            // ---- E. A STALE single-cell footprint is re-derived from the point ---------------------------
            // Moving a building writes Room.X/Y from eight editor call sites and does not (yet) rewrite
            // Room.Cells, and the v10 migration never overwrites a non-empty footprint — so without this rule
            // a migrated building's tile would freeze at where it used to be and dragging it would stop
            // moving it. Footprint says (0,0); the point says (3,2); the point wins.
            var fE = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            fE.Rooms.Add(Many(1, (3, 2), (0, 0)));
            var gE = SettlementTileGrid.Build(fE);
            if (gE.At(3, 2) != TileType.Building)
            { Debug.LogError($"FAIL footprint-tiles: a building whose stale single-cell footprint says (0,0) but whose point says (3,2) draws {gE.At(3,2)} at (3,2), expected Building — the stale footprint was trusted"); ok = false; }
            if (!gE.InBounds(0, 0))
            { Debug.LogError($"FAIL footprint-tiles: the stale cell (0,0) is out of the grid entirely — the check below would pass for the wrong reason"); ok = false; }
            else if (gE.At(0, 0) == TileType.Building)
            { Debug.LogError("FAIL footprint-tiles: the STALE cell (0,0) is still drawn Building — the building is in two places at once"); ok = false; }
            int buildE = 0; for (int a=0;a<gE.W;a++) for (int b=0;b<gE.H;b++) if (gE.Cells[a,b]==TileType.Building) buildE++;
            if (buildE != 1)
            { Debug.LogError($"FAIL footprint-tiles: a stale single-cell footprint produced {buildE} Building cells, expected exactly 1"); ok = false; }

            // ---- F. A MULTI-cell footprint is NEVER re-derived, even when the point disagrees ------------
            // The other half of rule E, and the reason it is restricted to single cells: a point cannot
            // reconstruct a shape, so "self-healing" a bar or an L would amputate it to one cell. Footprint
            // says (0,0),(1,0); the point says (3,2); the FOOTPRINT wins. (3,2) is inside the extent both
            // rules produce, so this really discriminates rather than reading None for being off-grid.
            var fF = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = false } };
            fF.Rooms.Add(Many(1, (3, 2), (0, 0), (1, 0)));
            var gF = SettlementTileGrid.Build(fF);
            if (gF.At(0, 0) != TileType.Building || gF.At(1, 0) != TileType.Building)
            { Debug.LogError($"FAIL footprint-tiles: a 2-cell footprint whose point sits at (3,2) draws {gF.At(0,0)}/{gF.At(1,0)} at (0,0)/(1,0), expected Building/Building — a multi-cell footprint was re-derived from the point and amputated"); ok = false; }
            if (!gF.InBounds(3, 2))
            { Debug.LogError($"FAIL footprint-tiles: the disagreeing point cell (3,2) is out of the grid entirely — grid is {gF.W}x{gF.H} @ ({gF.OriginI},{gF.OriginJ}) — the check below would pass for the wrong reason"); ok = false; }
            else if (gF.At(3, 2) == TileType.Building)
            { Debug.LogError("FAIL footprint-tiles: the disagreeing point cell (3,2) is drawn Building — a multi-cell footprint must not be re-derived"); ok = false; }
            int buildF = 0; for (int a=0;a<gF.W;a++) for (int b=0;b<gF.H;b++) if (gF.Cells[a,b]==TileType.Building) buildF++;
            if (buildF != 2)
            { Debug.LogError($"FAIL footprint-tiles: a 2-cell footprint produced {buildF} Building cells, expected exactly 2"); ok = false; }

            if (ok) Debug.Log("Settlement Footprint Tiles: PASS");
        }

        /// <summary>Every gate opens onto a road (DM finding ·3). The one-cell courtyard between the wall and
        /// the built-up area stays; a short lane is painted from each gate through it to the nearest road, and
        /// nowhere else.
        ///
        /// WHY THE CORPUS ASSERTION IS "PATH IS EMPTY ON A BUILT GRID" rather than "a Road exists somewhere
        /// near the gate": GateSpurPath returns an EMPTY list exactly when the gate already has a Road
        /// 4-neighbour, so re-asking it on a grid Build has already spurred proves BOTH that the pass ran and
        /// that it is idempotent, in one assertion that cannot pass while the pass is neutered.</summary>
        [ContextMenu("Self-Test: Gate Spur")]
        public void SelfTestGateSpur()
        {
            bool ok = true;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            int gatesSeen = 0;

            foreach (var size in sizes)
                for (int k = 0; k < 20; k++)
                {
                    int seed = 1000 + k;
                    var cfg = new SettlementConfig { Seed = seed, Size = size, ActiveBuildings = 1, HasWall = true };
                    var floor = SettlementGenerator.Generate(cfg, "poi").Floors[0];
                    var g = SettlementTileGrid.Build(floor);

                    int footprintCells = 0;
                    foreach (var r in floor.Rooms)
                        if (r.TypeId == 1) footprintCells += SettlementTileGrid.FootprintOf(r).Count;
                    int buildingTiles = 0;
                    for (int a = 0; a < g.W; a++)
                        for (int b = 0; b < g.H; b++)
                            if (g.Cells[a, b] == TileType.Building) buildingTiles++;
                    if (buildingTiles != footprintCells)
                    {
                        Debug.LogError($"SelfTestGateSpur: {size} seed {seed}: the spur ate a building — "
                                     + $"{buildingTiles} Building tiles for {footprintCells} footprint cells");
                        ok = false;
                    }

                    // "там где ворота и только там": every Road tile is either a STORED street cell or sits
                    // within MaxGateSpurCells of a gate. Bounds the spur's length AND its reach in one claim,
                    // on the real corpus — a fixture cannot, because where the wall ring lands for a given
                    // hand-placed gate is not something a test should be pinning by hand.
                    var stored = new System.Collections.Generic.HashSet<(int i, int j)>(
                        SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
                    var gateCells = new System.Collections.Generic.List<(int i, int j)>();
                    for (int a = 0; a < g.W; a++)
                        for (int b = 0; b < g.H; b++)
                            if (g.Cells[a, b] == TileType.Gate) gateCells.Add((a + g.OriginI, b + g.OriginJ));
                    int spurCells = 0;
                    for (int a = 0; a < g.W; a++)
                        for (int b = 0; b < g.H; b++)
                        {
                            if (g.Cells[a, b] != TileType.Road) continue;
                            var w = (i: a + g.OriginI, j: b + g.OriginJ);
                            if (stored.Contains(w)) continue;
                            spurCells++;
                            int near = int.MaxValue;
                            foreach (var gc in gateCells)
                            {
                                int d = System.Math.Abs(gc.i - w.i) + System.Math.Abs(gc.j - w.j);
                                if (d < near) near = d;
                            }
                            if (near > SettlementTileGrid.MaxGateSpurCells)
                            {
                                Debug.LogError($"SelfTestGateSpur: {size} seed {seed}: Road at world cell "
                                             + $"({w.i},{w.j}) is neither a stored street nor within "
                                             + $"{SettlementTileGrid.MaxGateSpurCells} of a gate (nearest {near})");
                                ok = false;
                            }
                        }
                    // THE PRIMARY BOUND, and the reason the distance test above is not enough on its own: a
                    // small town's ring is short enough that most of its courtyard sits within 3 cells of SOME
                    // gate, so "near a gate" alone would let a pass that paved the whole courtyard through.
                    // Total painted cells cannot exceed one spur per gate at its measured maximum length.
                    int spurBudget = gateCells.Count * SettlementTileGrid.MaxGateSpurCells;
                    if (spurCells > spurBudget)
                    {
                        Debug.LogError($"SelfTestGateSpur: {size} seed {seed}: {spurCells} Road cells are not "
                                     + $"stored streets, over the budget of {gateCells.Count} gates x "
                                     + $"{SettlementTileGrid.MaxGateSpurCells} cells = {spurBudget}");
                        ok = false;
                    }

                    for (int a = 0; a < g.W; a++)
                        for (int b = 0; b < g.H; b++)
                        {
                            if (g.Cells[a, b] != TileType.Gate) continue;
                            gatesSeen++;
                            var again = SettlementTileGrid.GateSpurPath(g, a, b);
                            if (again == null)
                            {
                                Debug.LogError($"SelfTestGateSpur: {size} seed {seed}: gate at array cell "
                                             + $"({a},{b}) reaches no road at all");
                                ok = false;
                            }
                            else if (again.Count != 0)
                            {
                                Debug.LogError($"SelfTestGateSpur: {size} seed {seed}: gate at array cell "
                                             + $"({a},{b}) still wants {again.Count} more cells painted after "
                                             + "Build — the spur pass did not run or is not idempotent");
                                ok = false;
                            }
                        }
                }

            if (gatesSeen < 60)
            {
                Debug.LogError($"SelfTestGateSpur: only {gatesSeen} gates drawn across 60 towns — the corpus "
                             + "assertion above is near-vacuous; expected at least 60");
                ok = false;
            }

            if (ok) Debug.Log("Self-Test Gate Spur: PASS");
        }

        /// <summary>DM finding ·9's shape: a road out to an outlying house makes the wall wrap it, and the
        /// resulting corridor must read wall-road-wall, not wall-void-road-void-wall. The fixture is the one
        /// the spec measured: a 3x3 block, a lone building 10 cells east, a straight street between them.</summary>
        [ContextMenu("Self-Test: Spur Width")]
        public void SelfTestSpurWidth()
        {
            bool ok = true;
            var floor = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = true } };
            int id = 1;
            for (int j = 4; j <= 6; j++)
                for (int i = 4; i <= 6; i++)
                    floor.Rooms.Add(One(id++, i, j));
            floor.Rooms.Add(One(id++, 17, 5));

            var streets = new System.Collections.Generic.List<(int i, int j)>();
            for (int i = 3; i <= 7; i++) { streets.Add((i, 3)); streets.Add((i, 7)); }
            for (int j = 3; j <= 7; j++) { streets.Add((3, j)); streets.Add((7, j)); }
            for (int i = 8; i <= 16; i++) streets.Add((i, 5));
            floor.SettlementParams.StreetCells = SettlementFootprint.Encode(streets);

            var g = SettlementTileGrid.Build(floor);

            // Column 12 sits in the corridor's middle, far from either cluster. Walk it top to bottom and
            // require exactly Wall, Road, Wall with None above and below — a three-cell corridor.
            int col = 12;
            var seen = new System.Collections.Generic.List<string>();
            for (int b = 0; b < g.H; b++)
            {
                var t = g.Cells[col - g.OriginI, b];
                if (t != TileType.None) seen.Add($"{b + g.OriginJ}:{t}");
            }
            string got = string.Join(" ", seen);
            if (seen.Count != 3)
            {
                Debug.LogError($"SelfTestSpurWidth: corridor column {col} is {seen.Count} cells deep, expected "
                             + $"3 (wall, road, wall) — got [{got}]");
                ok = false;
            }
            else if (!seen[0].EndsWith("Wall") || !seen[1].EndsWith("Road") || !seen[2].EndsWith("Wall"))
            {
                Debug.LogError($"SelfTestSpurWidth: corridor column {col} reads [{got}], expected "
                             + "Wall then Road then Wall");
                ok = false;
            }

            // The TOWN's own courtyard must be untouched: column 5 runs through the 3x3 block, where every
            // street cell has a building within 2, so the wide rule still applies and a Void ring survives.
            bool townVoid = false;
            for (int b = 0; b < g.H; b++)
                if (g.Cells[5 - g.OriginI, b] == TileType.Void) townVoid = true;
            if (!townVoid)
            {
                Debug.LogError("SelfTestSpurWidth: the town's own courtyard vanished — the narrow rule is "
                             + "being applied to street cells that sit beside buildings");
                ok = false;
            }

            if (ok) Debug.Log("Self-Test Spur Width: PASS");
        }

        /// <summary>The narrow rule's accepted risk, pinned so it cannot grow silently. A generated town's
        /// street cells are supposed to run beside its buildings; the few that do not will have their wall
        /// pulled in by one cell. 200 seeds x 3 sizes (600 towns) — the SAME corpus SelfTestSizeCalibration
        /// uses — measures 7 of 54,995 street cells (0.013%) far enough to be narrowed. THIS test's own
        /// printed line is that figure's one and only source of truth: OpenStreetNeighbourhood's doc comment
        /// quotes it, and if this corpus or the rule ever changes, re-run this test and update that comment
        /// from what it actually prints — never adjust either number without the other.</summary>
        [ContextMenu("Self-Test: Street Cells Near Buildings")]
        public void SelfTestStreetCellsNearBuildings()
        {
            bool ok = true;
            long total = 0, far = 0;
            foreach (var size in new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large })
                for (int k = 0; k < 200; k++)
                {
                    int seed = 1000 + k;
                    var cfg = new SettlementConfig { Seed = seed, Size = size, ActiveBuildings = 1, HasWall = true };
                    var floor = SettlementGenerator.Generate(cfg, "poi").Floors[0];
                    var b = new System.Collections.Generic.HashSet<(int i, int j)>();
                    foreach (var r in floor.Rooms)
                        if (r.TypeId == 1)
                            foreach (var c in SettlementTileGrid.FootprintOf(r)) b.Add(c);
                    foreach (var s in SettlementFootprint.Decode(floor.SettlementParams.StreetCells))
                    {
                        total++;
                        bool near = false;
                        int rad = SettlementTileGrid.OpenStreetNeighbourhood;
                        for (int di = -rad; di <= rad && !near; di++)
                            for (int dj = -rad; dj <= rad && !near; dj++)
                                if (b.Contains((s.i + di, s.j + dj))) near = true;
                        if (!near) far++;
                    }
                }
            double pct = total > 0 ? 100.0 * far / total : 0.0;
            Debug.Log($"Street cells far from any building: {far}/{total} ({pct:0.000}%)");
            if (pct > 0.10)
            {
                Debug.LogError($"SelfTestStreetCellsNearBuildings: {far}/{total} ({pct:0.000}%) street cells "
                             + "are farther than OpenStreetNeighbourhood from any building, over the 0.10% "
                             + "ceiling — the narrow-spur rule is now dimpling generated town walls");
                ok = false;
            }
            if (ok) Debug.Log("Self-Test Street Cells Near Buildings: PASS");
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
