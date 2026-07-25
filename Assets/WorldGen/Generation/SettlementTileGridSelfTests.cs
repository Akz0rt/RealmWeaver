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
            { Debug.LogError("FAIL roads: far spur cell (1,10) is OUT of bounds — the grid extent was not folded over the routed road (OVERRIDE 1)"); ok = false; }
            else if (clean.At(1, 10) != TileType.Road)
            { Debug.LogError($"FAIL roads: far spur cell (1,10) is {clean.At(1, 10)}, expected Road (present but misclassified)"); ok = false; }
            if (clean.At(1, 12) != TileType.Wall)
            { Debug.LogError($"FAIL roads: cell (1,12), two cells beyond the spur's tip, is {clean.At(1, 12)}, expected Wall — the wall must wrap the spur, not just the buildings"); ok = false; }

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
            { Debug.LogError("FAIL roads: Fast tier (buildings-only extent) already covers the far spur cell (1,10) — the OVERRIDE 1 extent-fold assertion above is not load-bearing"); ok = false; }
            int roadCells = 0; for (int a=0;a<fast.W;a++) for (int b=0;b<fast.H;b++) if (fast.Cells[a,b]==TileType.Road) roadCells++;
            if (roadCells != 0)
            { Debug.LogError($"FAIL roads: Fast tier (null roads) produced {roadCells} Road cells, expected 0"); ok = false; }
            if (fast.At(-2, 1) != TileType.Gate)
            { Debug.LogError($"FAIL roads: Fast tier gate reclassify missing — (-2,1) is {fast.At(-2,1)}, expected Gate"); ok = false; }

            if (ok) Debug.Log("Settlement Roads and Gates: PASS");
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
