using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for the settlement primitive. Every assertion names the exact
    /// point/building/edge the rule changes — never a bare count (the project's #1 past failure mode was a
    /// test that passes whether or not the rule holds; see CompactLayoutSelfTests for the discipline).</summary>
    public class SettlementSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Wall Contour")]
        public void SelfTestWallContour()
        {
            bool ok = true;

            // ---- 1. A rounded contour is closed, non-degenerate, and has the requested vertex count -----
            var wall = WallContour.Rounded(seed: 7, cx: 0.5f, cy: 0.5f, radius: 0.4f, sides: 8, jitter: 0.1f);
            if (wall.Points.Count != 8)
            { Debug.LogError($"FAIL wall: asked for 8 sides, got {wall.Points.Count} points"); ok = false; }
            if (!wall.IsClosedSane())
            { Debug.LogError("FAIL wall: IsClosedSane false for a fresh rounded contour"); ok = false; }

            // ---- 2. Contains: the centre is inside, a far point is outside ------------------------------
            // Flip the ray-cast parity and the centre reads as outside.
            if (!wall.Contains(0.5f, 0.5f))
            { Debug.LogError("FAIL wall: the centre (0.5,0.5) is not Contains-inside a radius-0.4 contour"); ok = false; }
            if (wall.Contains(0.99f, 0.99f))
            { Debug.LogError("FAIL wall: far corner (0.99,0.99) reads as inside a radius-0.4 contour"); ok = false; }

            // ---- 3. DistanceToEdge is ~0 on the wall line, large at the centre -------------------------
            // A point exactly on the first vertex must have edge-distance 0; the centre must be ~radius away.
            var p0 = wall.Points[0];
            if (wall.DistanceToEdge(p0.X, p0.Y) > 1e-4f)
            { Debug.LogError($"FAIL wall: a vertex reports edge-distance {wall.DistanceToEdge(p0.X, p0.Y)}, want ~0"); ok = false; }
            if (wall.DistanceToEdge(0.5f, 0.5f) < 0.2f)
            { Debug.LogError($"FAIL wall: the centre reports edge-distance {wall.DistanceToEdge(0.5f, 0.5f)}, want ≥0.2 for radius 0.4"); ok = false; }

            // ---- 4. Determinism: same seed → identical points ------------------------------------------
            var wallB = WallContour.Rounded(seed: 7, cx: 0.5f, cy: 0.5f, radius: 0.4f, sides: 8, jitter: 0.1f);
            for (int i = 0; i < wall.Points.Count; i++)
                if (wall.Points[i].X != wallB.Points[i].X || wall.Points[i].Y != wallB.Points[i].Y)
                { Debug.LogError($"FAIL wall: point {i} differs between two seed-7 contours — not deterministic"); ok = false; break; }

            // ---- 5. Jitter actually perturbs: a different seed moves at least one point -----------------
            // Drop the jitter term and every seed yields the same regular polygon; this catches that.
            var wallC = WallContour.Rounded(seed: 8, cx: 0.5f, cy: 0.5f, radius: 0.4f, sides: 8, jitter: 0.1f);
            bool anyMoved = false;
            for (int i = 0; i < wall.Points.Count; i++)
                if (wall.Points[i].X != wallC.Points[i].X || wall.Points[i].Y != wallC.Points[i].Y) { anyMoved = true; break; }
            if (!anyMoved)
            { Debug.LogError("FAIL wall: seed 7 and seed 8 produced identical contours — jitter is inert"); ok = false; }

            // ---- 6. IsClosedSane negative cases: degenerate contours must read as NOT sane --------------
            // A stub that always returns true would pass every assertion above; these two catch that.
            var twoPoint = new WallContour();
            twoPoint.Points.Add(new WallPoint { X = 0.2f, Y = 0.2f });
            twoPoint.Points.Add(new WallPoint { X = 0.8f, Y = 0.8f });
            if (twoPoint.IsClosedSane())
            { Debug.LogError("FAIL wall: a 2-point contour (Points.Count < 3) reports IsClosedSane true"); ok = false; }

            var zeroSpan = new WallContour();
            zeroSpan.Points.Add(new WallPoint { X = 0.5f, Y = 0.5f });
            zeroSpan.Points.Add(new WallPoint { X = 0.5f, Y = 0.5f });
            zeroSpan.Points.Add(new WallPoint { X = 0.5f, Y = 0.5f });
            if (zeroSpan.IsClosedSane())
            { Debug.LogError("FAIL wall: a 3-point contour with all points at (0.5,0.5) (zero bbox span) reports IsClosedSane true"); ok = false; }

            if (ok) Debug.Log("Settlement Wall Contour: PASS");
        }

        [ContextMenu("Self-Test: Settlement Gates")]
        public void SelfTestGates()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 3, Size = SettlementSize.Medium, HasWall = true };
            var wall = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(cfg.Size), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);

            // ---- 1. The notional wall contour is a non-null, sane contour --------------------------------
            if (wall == null || !wall.IsClosedSane())
            { Debug.LogError("FAIL gates: notional wall contour null/insane"); ok = false; }

            // ---- 3. PlaceGates honours the requested count, and GateCountFor is in 2..4 ----------------
            int want = SettlementGenerator.GateCountFor(cfg.Size);
            var gates = SettlementGenerator.PlaceGates(wall, want, cfg.Seed);
            if (gates.Count != want)
            { Debug.LogError($"FAIL gates: asked for {want} gates, placed {gates.Count}"); ok = false; }
            if (want < 2 || want > 4)
            { Debug.LogError($"FAIL gates: GateCountFor({cfg.Size}) = {want}, want 2..4"); ok = false; }

            // ---- 4. EVERY gate lies ON the wall line ----------------------------------------------------
            // Place gates at the centre instead of on the wall and each of these fires with its distance.
            foreach (var g in gates)
            {
                float d = wall.DistanceToEdge(g.X, g.Y);
                if (d > 1e-3f)
                { Debug.LogError($"FAIL gates: gate at ({g.X:F3},{g.Y:F3}) is {d:F3} off the wall line, want ~0"); ok = false; }
            }

            // ---- 5. Gates are SPREAD, not clustered: no two share a position ---------------------------
            for (int i = 0; i < gates.Count; i++)
                for (int j = i + 1; j < gates.Count; j++)
                {
                    float dx = gates[i].X - gates[j].X, dy = gates[i].Y - gates[j].Y;
                    if (dx * dx + dy * dy < 1e-4f)
                    { Debug.LogError($"FAIL gates: gates {i} and {j} sit on top of each other"); ok = false; }
                }

            // ---- 6. Determinism ------------------------------------------------------------------------
            var gates2 = SettlementGenerator.PlaceGates(wall, want, cfg.Seed);
            if (gates2.Count != gates.Count || (gates.Count > 0 && (gates2[0].X != gates[0].X || gates2[0].Y != gates[0].Y)))
            { Debug.LogError("FAIL gates: two seed-3 gate placements differ — not deterministic"); ok = false; }

            // ---- 7. In an ACTUAL generated floor, every gate room sits ON THE WALL RING and IS one of the
            // floor's own stored street cells.
            //
            // REPLACES (arc A, task 3) the Ц2.6 assertion that every gate hugged a PRELIMINARY fence derived
            // from the placed buildings. That mechanism no longer exists: BuildFloor does not derive a fence
            // and does not call PlaceGates at all — SettlementBlocks.PlaceGateCells picks the gates straight
            // out of the RING STREET (the one-cell lap just inside the contour), spread by ANGLE around the
            // ring's own centroid with a seeded phase, and the arterials are routed inward FROM them. The old
            // assertion could only be kept by re-asserting a fence nothing builds; these two assert the rule
            // that replaced it, and assert it MORE tightly (exact cell membership, plus a hand-derived
            // distance bound) than a 1.5-tile proximity band did.
            //
            // (a) STORED: the gate room's point must land in a cell that is one of the floor's stored street
            //     cells. This also pins the round-trip CenterOf -> CellOf that every renderer depends on.
            // (b) ON THE RING: that cell must be one of the RING cells specifically — the one-cell street
            //     just inside the wall — not merely some street cell somewhere in town. MEMBERSHIP, not a
            //     distance band: an earlier draft asserted "within 2 pitches of the wall line" and called
            //     that exact, which it is not — SettlementBlocks.RingStreet's reconnect pass iterates to a
            //     FIXED POINT, so a cell promoted in a second round can sit three pitches out. Membership
            //     needs no bound at all and is strictly tighter.
            // (c) NON-VACUITY, asserted rather than assumed: "on the ring" must be strictly stronger than "a
            //     street cell" (there must exist a stored street cell that is NOT a ring cell), and the ring
            //     must not contain the middle of town.
            var floor = SettlementGenerator.BuildFloor(cfg);
            var placement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f,
                SettlementGenerator.WallRadiusFor(cfg.Size), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var streetSet = new System.Collections.Generic.HashSet<(int i, int j)>(
                SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
            var interior = SettlementBlocks.InteriorCells(placement);
            var interiorSet = new System.Collections.Generic.HashSet<(int i, int j)>(interior);
            var ringSet = new System.Collections.Generic.HashSet<(int i, int j)>(
                SettlementBlocks.RingStreet(interior, interiorSet));

            int nonRingStreets = 0;
            foreach (var c in streetSet) if (!ringSet.Contains(c)) nonRingStreets++;
            if (nonRingStreets == 0)
            { Debug.LogError($"FAIL gates: all {streetSet.Count} stored street cells are ring cells — the ring-membership check below would be no stronger than the street check"); ok = false; }
            var centreCell = (i: SettlementFootprint.CellOf(0.5f), j: SettlementFootprint.CellOf(0.5f));
            if (ringSet.Contains(centreCell))
            { Debug.LogError($"FAIL gates: the town-centre cell ({centreCell.i},{centreCell.j}) reads as a RING cell — the check below would not exclude the middle of town"); ok = false; }

            int gateRooms = 0;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 0) continue;
                gateRooms++;
                var cell = (i: SettlementFootprint.CellOf(r.X), j: SettlementFootprint.CellOf(r.Y));
                if (!streetSet.Contains(cell))
                { Debug.LogError($"FAIL gates: gate room {r.Id} at ({r.X:F3},{r.Y:F3}) sits in cell ({cell.i},{cell.j}), which is NOT one of the floor's {streetSet.Count} stored street cells"); ok = false; }
                if (!ringSet.Contains(cell))
                { Debug.LogError($"FAIL gates: gate room {r.Id} sits in cell ({cell.i},{cell.j}), which is NOT one of the {ringSet.Count} ring cells — it is inside the town, not on the wall ring"); ok = false; }
            }
            // A walled town of ANY size opens at least two gates, and there is no fallback pass that makes it
            // so — the guarantee is the composition of two facts. (i) SettlementSizing.GateCount is >= 2 for
            // every size class (2/3/4), so PlaceGateCells is always ASKED for at least two. (ii) It only ever
            // drops one on a DEGENERATE ring — shorter than roughly 3 x gateCount cells, where every cell is
            // within MinGateSeparationCells of every other (its own doc: an 8-cell ring, i.e. a 3x3 town).
            // This fixture is Medium, wall radius 7.0 cells, a ring of ~40, so nothing is dropped. A future
            // edit that lowered a GateCount row to 1, or shrank a radius to the degenerate regime, is exactly
            // what this catches.
            if (gateRooms < 2)
            { Debug.LogError($"FAIL gates: a walled {cfg.Size} town produced {gateRooms} gate rooms, want >=2"); ok = false; }

            if (ok) Debug.Log("Settlement Gates: PASS");
        }

        [ContextMenu("Self-Test: Settlement Buildings")]
        public void SelfTestBuildings()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 11, Size = SettlementSize.Medium, HasWall = true };
            var wall = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(cfg.Size), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var buildings = SettlementGenerator.PlaceBuildings(wall, cfg.Seed, SettlementSizing.TargetBuildings(cfg.Size));

            // ---- 1. EVERY building centre is inside the wall -------------------------------------------
            // Drop the Contains filter and cells outside the rounded contour leak in at the bbox corners.
            foreach (var b in buildings)
                if (!wall.Contains(b.X, b.Y))
                { Debug.LogError($"FAIL buildings: building at ({b.X:F3},{b.Y:F3}) is OUTSIDE the wall"); ok = false; break; }

            // ---- 2. NO two buildings SHARE A LATTICE CELL — the anti-overlap guarantee ------------------
            // This is the exact defect that disqualified the dungeon packer (18–48 overlapping pairs at 40).
            //
            // WHY THIS IS NOT A DISTANCE TEST ANY MORE. It used to assert that no two buildings were closer
            // than one BuildingCell. A building is a FOOTPRINT of cells now, and a town is meant to be blocks
            // of FLUSH buildings separated by streets — two buildings sharing a wall are exactly one cell
            // apart, so the old assertion forbade the shape this arc exists to produce. What actually matters
            // — and what the old distance test was a proxy for — is that no two buildings occupy the SAME
            // cell: overlapping is a defect, touching is the design. Expressed on SettlementFootprint's
            // absolute lattice (CellOf), which is the same lattice the tile grid and Room.Cells use, so this
            // says the same thing about a point building as SettlementFootprint.Overlaps says about a shaped
            // one. PlaceBuildings still emits one point per building on a grid of pitch BuildingCell, so two
            // distinct buildings differ by at least one full pitch on some axis and therefore land in
            // different cells (floor(x/p) and floor((x+p)/p) differ by exactly 1) — the rule holds without
            // any tolerance term, and no longer needs the old 0.9 fudge factor.
            for (int i = 0; i < buildings.Count && ok; i++)
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    int ci = SettlementFootprint.CellOf(buildings[i].X), cj = SettlementFootprint.CellOf(buildings[i].Y);
                    int dj = SettlementFootprint.CellOf(buildings[j].X), dk = SettlementFootprint.CellOf(buildings[j].Y);
                    if (ci == dj && cj == dk)
                    { Debug.LogError($"FAIL buildings: buildings {i} ({buildings[i].X:F4},{buildings[i].Y:F4}) and {j} ({buildings[j].X:F4},{buildings[j].Y:F4}) share lattice cell ({ci},{cj})"); ok = false; break; }
                }

            // ---- 3. Every building sits at least half a cell from the wall (card doesn't straddle it) ---
            foreach (var b in buildings)
                if (wall.DistanceToEdge(b.X, b.Y) < SettlementGenerator.BuildingCell * 0.5f * 0.9f)
                { Debug.LogError($"FAIL buildings: building at ({b.X:F3},{b.Y:F3}) hugs the wall line"); ok = false; break; }

            // ---- 4. A big town gets as many buildings as the grid holds, up to the target --------------
            // We do not require EXACTLY target (a small wall may not fit 40), but a 40-target radius-0.34
            // wall must fit a substantial fraction; catch a placement that silently yields near-zero.
            if (buildings.Count < 20)
            { Debug.LogError($"FAIL buildings: a 40-target town produced only {buildings.Count} buildings"); ok = false; }
            if (buildings.Count > SettlementSizing.TargetBuildings(cfg.Size))
            { Debug.LogError($"FAIL buildings: produced {buildings.Count}, more than the {SettlementSizing.TargetBuildings(cfg.Size)} target for {cfg.Size}"); ok = false; }

            // ---- 5. Determinism ------------------------------------------------------------------------
            var b2 = SettlementGenerator.PlaceBuildings(wall, cfg.Seed, SettlementSizing.TargetBuildings(cfg.Size));
            if (b2.Count != buildings.Count || (buildings.Count > 0 && (b2[0].X != buildings[0].X || b2[0].Y != buildings[0].Y)))
            { Debug.LogError("FAIL buildings: two seed-11 placements differ — not deterministic"); ok = false; }

            if (ok) Debug.Log("Settlement Buildings: PASS");
        }

        [ContextMenu("Self-Test: Settlement Assembly")]
        public void SelfTestAssembly()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 2, Size = SettlementSize.Medium, HasWall = true };
            var data = SettlementGenerator.Generate(cfg, "poi-town");

            // ---- 1. Shape: one floor, Kind Settlement, owner set --------------------------------------
            if (data.Kind != InteriorKind.Settlement || data.OwnerPoiId != "poi-town")
            { Debug.LogError("FAIL assembly: Kind/OwnerPoiId not set from Generate's arguments"); ok = false; }
            if (data.Floors.Count != 1)
            { Debug.LogError($"FAIL assembly: expected 1 floor, got {data.Floors.Count}"); ok = false; }
            var floor = data.Floors[0];

            // ---- 2. Gate nodes are TypeId 0; building nodes are TypeId 1 (Ц2.6+Task 7: no wall is stored on
            // the floor — InteriorFloor.Wall was removed; gate/fence geometry is covered separately by
            // SelfTestGates, which re-derives the preliminary building fence and checks gate proximity) --
            int gateNodes = 0, buildNodes = 0;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == 0) gateNodes++;
                else if (r.TypeId == 1) buildNodes++;
                else
                { Debug.LogError($"FAIL assembly: room {r.Id} has TypeId {r.TypeId}, want 0 (gate) or 1 (building)"); ok = false; }
            }
            if (gateNodes < 2)
            { Debug.LogError($"FAIL assembly: {gateNodes} gate nodes, want ≥2"); ok = false; }
            // THE FLOOR IS THE SIZE TABLE'S OWN PROMISE, not a number chosen here (final-review fix). It used
            // to be a hardcoded 12, documented as "0.8 x the measured minimum count at this fixture's own
            // target, 15 (target 40's own row: 15..24 over seeds 1..60)". Every clause of that has expired:
            // the measurement was taken under RECURSIVE SUBDIVISION (deleted — streets are laid by frontage
            // now), at the old count-derived radius, for a fixture whose scale was an exact building COUNT
            // (retired — this fixture is Size = Medium). Medium's measured minimum over the shipped 200-seed
            // sweep is 42, so a floor of 12 left ~4x slack: a regression that HALVED Medium's yield would
            // still have passed, which is a floor that has stopped constraining anything.
            //
            // GuaranteedMinBuildings(cfg.Size) instead of any new magic number, because it is the same
            // quantity, already measured and already test-enforced. buildNodes here IS layout.Buildings.Count
            // — SettlementGenerator.BuildFloor writes exactly one TypeId 1 room per footprint, in order — and
            // SettlementBlocksSelfTests.SelfTestSizeCalibration asserts that count meets the guarantee on
            // EVERY one of its 200 seeds at this exact contour (both go through SettlementSizing.
            // WallRadiusNorm(size); SettlementGenerator.WallRadiusFor is a one-line delegation to it). For
            // Medium the guarantee is 37 = floor(0.9 x the observed minimum 42) — a 10% margin below the
            // worst seed the sweep has ever seen.
            //
            // WHAT THIS TEST ADDS OVER THAT SWEEP, and why it is not a duplicate: the sweep measures the
            // LAYOUT (SettlementBlocks.Generate's footprints), this measures the ASSEMBLED FLOOR's ROOMS. A
            // BuildFloor that dropped, filtered or mis-typed buildings on their way into lvl.Rooms fails here
            // and nowhere else. It is also the only place the promise is checked at a seed OUTSIDE the sweep
            // (this fixture is seed 2; the sweep runs 1000..1199) — so it holds the table honest as a
            // PROMISE about the size class rather than about 200 particular seeds. If a future calibration
            // lowers a guarantee, this floor follows it automatically instead of silently going slack.
            int minBuildNodes = SettlementSizing.GuaranteedMinBuildings(cfg.Size);
            if (buildNodes < minBuildNodes)
            { Debug.LogError($"FAIL assembly: {buildNodes} building nodes, want ≥{minBuildNodes} — SettlementSizing.GuaranteedMinBuildings({cfg.Size}), the size table's own measured promise"); ok = false; }

            // ---- 3. The floor is assembled in the RIGHT order, from the RIGHT layout ---------------------
            // Reconstruct the exact placement/layout BuildFloor used (all deterministic from cfg alone —
            // there is no stored wall), then verify (a) rooms were created in gates-then-buildings order —
            // the room at combined index i carries node i's position, type AND FOOTPRINT — (a2) the layout's
            // whole street list was stored, and (b) the floor carries NO LINKS.
            //
            // RE-DERIVED (arc A, task 3) from the old PlaceBuildings + preliminary-fence + PlaceGates
            // reconstruction: BuildFloor calls none of those three any more. Gates and buildings both come
            // out of SettlementBlocks.Generate now, so the reconstruction calls THAT — still recomputed from
            // primitives here, never read back off `floor`, so it stays an independent check of the assembly
            // wiring rather than a tautology.
            var exPlacement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f,
                SettlementGenerator.WallRadiusFor(cfg.Size), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var exLayout = SettlementBlocks.Generate(exPlacement, cfg.Seed, cfg.Size);
            var exGates = new System.Collections.Generic.List<GatePoint>();
            foreach (var gc in exLayout.GateCells)   // cfg.HasWall is true for this fixture
                exGates.Add(new GatePoint { X = SettlementFootprint.CenterOf(gc.i), Y = SettlementFootprint.CenterOf(gc.j) });
            var exBuildings = new System.Collections.Generic.List<PlacedBuilding>();
            foreach (var fp in exLayout.Buildings)
            {
                var rep = SettlementFootprint.Representative(fp);
                exBuildings.Add(new PlacedBuilding { X = SettlementFootprint.CenterOf(rep.i), Y = SettlementFootprint.CenterOf(rep.j) });
            }
            int nG = exGates.Count;
            // (a) creation order == gates-then-buildings, by position/type at each combined index.
            for (int i = 0; i < nG && ok; i++)
                if (floor.Rooms[i].TypeId != 0 || floor.Rooms[i].X != exGates[i].X || floor.Rooms[i].Y != exGates[i].Y)
                { Debug.LogError($"FAIL assembly: room index {i} (id {floor.Rooms[i].Id}) is not gate {i} at ({exGates[i].X:F3},{exGates[i].Y:F3})"); ok = false; }
            for (int i = 0; i < exBuildings.Count && ok; i++)
            {
                var rm = floor.Rooms[nG + i];
                if (rm.TypeId != 1 || rm.X != exBuildings[i].X || rm.Y != exBuildings[i].Y)
                { Debug.LogError($"FAIL assembly: room index {nG + i} (id {rm.Id}) is not building {i} at ({exBuildings[i].X:F3},{exBuildings[i].Y:F3})"); ok = false; }
                // THE FOOTPRINT IS THE BUILDING NOW: the room must carry building i's WHOLE cell set, not a
                // point that merely happens to agree with it. Cell-for-cell and in order — Room.Cells is
                // serialized, so a reordering rewrites every saved town.
                var got = SettlementFootprint.Decode(rm.Cells);
                if (got.Count != exLayout.Buildings[i].Count)
                { Debug.LogError($"FAIL assembly: room index {nG + i} (id {rm.Id}) carries {got.Count} footprint cells, want {exLayout.Buildings[i].Count}"); ok = false; }
                else
                    for (int k = 0; k < got.Count; k++)
                        if (got[k] != exLayout.Buildings[i][k])
                        { Debug.LogError($"FAIL assembly: room index {nG + i} (id {rm.Id}) footprint cell {k} is ({got[k].i},{got[k].j}), want ({exLayout.Buildings[i][k].i},{exLayout.Buildings[i][k].j})"); ok = false; break; }
            }
            // (a2) THE STREETS ARE STORED, and stored exactly once: the floor's SettlementParams must carry
            // the layout's whole street list, cell-for-cell. Nothing else in the codebase re-routes them.
            var exStreets = SettlementFootprint.Decode(floor.SettlementParams?.StreetCells);
            if (exStreets.Count != exLayout.StreetCells.Count)
            { Debug.LogError($"FAIL assembly: floor stores {exStreets.Count} street cells, want {exLayout.StreetCells.Count}"); ok = false; }
            else
                for (int k = 0; k < exStreets.Count; k++)
                    if (exStreets[k] != exLayout.StreetCells[k])
                    { Debug.LogError($"FAIL assembly: stored street cell {k} is ({exStreets[k].i},{exStreets[k].j}), want ({exLayout.StreetCells[k].i},{exLayout.StreetCells[k].j})"); ok = false; break; }
            // (b) NO LINKS AT ALL (Task 5). A generated settlement's streets are the stored cells asserted in
            // (a2); the Link list that used to mirror a street tree is gone, along with the two stages that
            // produced and re-routed it. This is the ONLY assertion on that removal, and it is deliberately
            // stated as an exact count rather than "no link between gates" or some weaker shape: any future
            // edit that starts writing links here again — for any reason — trips it and has to justify itself.
            // (Link itself is untouched: a dungeon and a building interior still use it, and the settlement
            // EDITOR can still create one by hand — this is about what the GENERATOR emits.)
            if (floor.Links.Count != 0)
            { Debug.LogError($"FAIL assembly: a generated settlement floor carries {floor.Links.Count} links, want 0 (first is {floor.Links[0].RoomA}→{floor.Links[0].RoomB})"); ok = false; }

            // ---- 4. NextRoomId is past every id, so the editor's «add» never collides -----------------
            int maxId = 0; foreach (var r in floor.Rooms) if (r.Id > maxId) maxId = r.Id;
            if (floor.NextRoomId <= maxId)
            { Debug.LogError($"FAIL assembly: NextRoomId {floor.NextRoomId} is not past maxId {maxId}"); ok = false; }

            // ---- 5. Determinism: same seed → same room count and first room position ------------------
            var data2 = SettlementGenerator.Generate(cfg, "poi-town");
            if (data2.Floors[0].Rooms.Count != floor.Rooms.Count ||
                data2.Floors[0].Rooms[0].X != floor.Rooms[0].X)
            { Debug.LogError($"FAIL assembly: seed-2 rerun has {data2.Floors[0].Rooms.Count} rooms / first X {data2.Floors[0].Rooms[0].X} vs {floor.Rooms.Count} / {floor.Rooms[0].X} — not deterministic"); ok = false; }

            if (ok) Debug.Log("Settlement Assembly: PASS");
        }

        [ContextMenu("Self-Test: Settlement Village")]
        public void SelfTestVillage()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 4, Size = SettlementSize.Medium, HasWall = false };
            var data = SettlementGenerator.Generate(cfg, "poi-village");
            var floor = data.Floors[0];

            // ---- 1. NO gate rooms; a substantial building count is STILL placed ------------------------
            // (InteriorFloor.Wall was removed — a wall-less village has no perimeter to store; "no wall" is now
            // structural, so what remains to check is that a HasWall=false town produces zero gate rooms below.)
            // FIXED LONG AGO — kept as HISTORY, not a live warning: BuildFloor used to derive placement from
            // the (null) wall, so a wall-less village yielded 0 buildings, 0 gates, 0 streets, a completely
            // empty map. The building-count floor a few lines down is what now pins the fix in place; if a
            // future change regressed it, an empty map is exactly the failure mode that floor would catch.
            int gateNodes = 0, buildNodes = 0;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == 0) gateNodes++;
                else if (r.TypeId == 1) buildNodes++;
                else
                { Debug.LogError($"FAIL village: room {r.Id} has TypeId {r.TypeId}, want 0 (gate) or 1 (building)"); ok = false; }
            }
            if (gateNodes != 0)
            { Debug.LogError($"FAIL village: {gateNodes} gate rooms in a wall-less village, want 0"); ok = false; }
            // THE FLOOR IS THE SIZE TABLE'S OWN PROMISE, not a number chosen here — the SAME final-review fix
            // as SelfTestAssembly's identical floor above, applied a second time. It too used to be a
            // hardcoded 12; this fixture's cfg.Size is Medium, so the SAME guarantee applies: Medium's
            // measured minimum over the shipped 200-seed sweep is 42 (SettlementSizing's own class doc), so a
            // floor of 12 left it ~3.5x slack — a regression cutting Medium's yield by two-thirds would still
            // have passed here.
            //
            // GuaranteedMinBuildings(cfg.Size) instead of any new magic number, for the same reason as
            // SelfTestAssembly: it is the same quantity, already measured and already test-enforced by
            // SettlementBlocksSelfTests' own size-calibration sweep on every one of its 200 seeds at this
            // exact contour. What THIS fixture (seed 4, wall-less) adds over that sweep and over
            // SelfTestAssembly: it holds the promise through the GATE-LESS BuildFloor branch (HasWall =
            // false), which neither the sweep nor SelfTestAssembly's gated fixture ever exercises.
            int minVillageBuildings = SettlementSizing.GuaranteedMinBuildings(cfg.Size);
            if (buildNodes < minVillageBuildings)
            { Debug.LogError($"FAIL village: a village produced only {buildNodes} buildings, want >={minVillageBuildings}"); ok = false; }

            // ---- 3. NO LINKS on the gate-less path either (Task 5) ---------------------------------------
            // This section used to be the hub-connectivity check: it called SettlementStreets.GenerateStreets
            // directly and compared its spanning tree against floor.Links, which is what made the gate-less
            // hub-seeding rule (MutStreetsNoHub) observable. Both the street stage and the links are gone, so
            // what is left to say about a village's graph is that it has none — asserted here as well as in
            // SelfTestAssembly because this is the HasWall=false branch of BuildFloor, which that fixture
            // never takes.
            if (floor.Links.Count != 0)
            { Debug.LogError($"FAIL village: a generated wall-less village carries {floor.Links.Count} links, want 0 (first is {floor.Links[0].RoomA}→{floor.Links[0].RoomB})"); ok = false; }

            // ---- 3c. A WALL-LESS VILLAGE STILL GETS ITS STREETS. HasWall suppresses gates and nothing else
            // — SettlementTileGrid.MarkRoads has an explicit no-wall path for exactly this, and it would draw
            // nothing if the generator stopped storing the cells. The expected list is re-derived the way
            // BuildFloor derives it (the SAME notional contour — identical seed/radius formula, never stored
            // — then SettlementBlocks.Generate), never read back off `floor`.
            var exPlacement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f,
                SettlementGenerator.WallRadiusFor(cfg.Size), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var blLayout = SettlementBlocks.Generate(exPlacement, cfg.Seed, cfg.Size);
            var villageStreets = SettlementFootprint.Decode(floor.SettlementParams?.StreetCells);
            if (villageStreets.Count != blLayout.StreetCells.Count)
            { Debug.LogError($"FAIL village: a wall-less village stored {villageStreets.Count} street cells, want {blLayout.StreetCells.Count}"); ok = false; }
            if (villageStreets.Count == 0)
            { Debug.LogError("FAIL village: a wall-less village stored NO street cells at all"); ok = false; }

            // ---- 4. Determinism: two seed-4 villages have identical room count and first-room position ---
            var data2 = SettlementGenerator.Generate(cfg, "poi-village");
            var floor2 = data2.Floors[0];
            if (floor2.Rooms.Count != floor.Rooms.Count ||
                (floor.Rooms.Count > 0 && (floor2.Rooms[0].X != floor.Rooms[0].X || floor2.Rooms[0].Y != floor.Rooms[0].Y)))
            { Debug.LogError($"FAIL village: seed-4 rerun has {floor2.Rooms.Count} rooms vs {floor.Rooms.Count} — not deterministic"); ok = false; }

            if (ok) Debug.Log("Settlement Village: PASS");
        }

        [ContextMenu("Self-Test: Settlement Authored Content")]
        public void SelfTestSettlementAuthored()
        {
            bool ok = true;
            var floor = new InteriorFloor { NextRoomId = 3 };
            floor.Rooms.Add(new Room { Id = 1, TypeId = 1 });
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1 });
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });   // generator link: Authored stays false

            // ---- 1. A freshly generated settlement is NOT authored -------------------------------------
            if (DungeonOps.HasAuthoredContent(floor))
            { Debug.LogError("FAIL authored: a plain generated settlement floor (rooms 1,2) counts as authored"); ok = false; }

            // ---- 2. A building carrying a Preview image IS authored ------------------------------------
            // Drop the Preview check and «Сгенерировать заново» silently destroys images.
            floor.Rooms[0].Preview = new byte[] { 1, 2, 3 };
            if (!DungeonOps.HasAuthoredContent(floor))
            { Debug.LogError($"FAIL authored: room {floor.Rooms[0].Id} with a Preview did not count as authored"); ok = false; }

            if (ok) Debug.Log("Settlement Authored Content: PASS");
        }

        [ContextMenu("Self-Test: Settlement Active/Dummy")]
        public void SelfTestActiveBuildings()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 6, Size = SettlementSize.Small, ActiveBuildings = 5, HasWall = true };
            var floor = SettlementGenerator.Generate(cfg, "poi-city").Floors[0];

            // ---- 1. Exactly min(Active, placed) buildings active; rest dummy; gates never dummy ----------
            int placed = 0, active = 0, dummy = 0, gateDummies = 0;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == 0) { if (r.IsDummy) gateDummies++; continue; }
                placed++;
                if (r.IsDummy) dummy++; else active++;
            }
            int wantActive = System.Math.Min(cfg.ActiveBuildings, placed);
            if (active != wantActive)
            { Debug.LogError($"FAIL active: {active} active buildings, want min({cfg.ActiveBuildings},{placed})={wantActive}"); ok = false; }
            if (dummy != placed - wantActive)
            { Debug.LogError($"FAIL active: {dummy} dummies, want {placed - wantActive}"); ok = false; }
            if (gateDummies != 0)
            { Debug.LogError($"FAIL active: {gateDummies} gate(s) marked dummy — a gate is never a dummy"); ok = false; }

            // ---- 2. The stored params carry the config ------------------------------------------------
            if (floor.SettlementParams == null || floor.SettlementParams.Size != cfg.Size
                || floor.SettlementParams.ActiveBuildings != cfg.ActiveBuildings)
            { Debug.LogError($"FAIL active: floor.SettlementParams stored {floor.SettlementParams?.Size}/{floor.SettlementParams?.ActiveBuildings}, want the config's {cfg.Size}/{cfg.ActiveBuildings}"); ok = false; }

            // ---- 3. Determinism: same seed → identical IsDummy per room ---------------------------------
            var f2 = SettlementGenerator.Generate(cfg, "poi-city").Floors[0];
            for (int i = 0; i < floor.Rooms.Count && i < f2.Rooms.Count; i++)
                if (floor.Rooms[i].IsDummy != f2.Rooms[i].IsDummy)
                { Debug.LogError($"FAIL active: room index {i} (id {floor.Rooms[i].Id}) IsDummy differs between two seed-6 runs"); ok = false; break; }

            // ---- 4. A wall-less camp with a tiny active count marks correctly too -----------------------
            var camp = SettlementGenerator.Generate(new SettlementConfig { Seed = 6, Size = SettlementSize.Small, ActiveBuildings = 2, HasWall = false }, "poi-camp").Floors[0];
            int campPlaced = 0, campActive = 0;
            foreach (var r in camp.Rooms) if (r.TypeId == 1) { campPlaced++; if (!r.IsDummy) campActive++; }
            if (campActive != System.Math.Min(2, campPlaced))
            { Debug.LogError($"FAIL active: camp has {campActive} active, want min(2,{campPlaced})"); ok = false; }

            // ---- 5. Edge cases on the request itself: zero and negative requests mark NO building active;
            // a request above the placed count marks ALL of them active — the min(activeCount, placed)
            // contract from both ends. -----------------------------------------------------------------
            var zeroCfg = new SettlementConfig { Seed = 6, Size = SettlementSize.Small, ActiveBuildings = 0, HasWall = true };
            var zeroFloor = SettlementGenerator.Generate(zeroCfg, "poi-zero").Floors[0];
            int zeroActive = 0, zeroPlaced = 0;
            foreach (var r in zeroFloor.Rooms) if (r.TypeId == 1) { zeroPlaced++; if (!r.IsDummy) zeroActive++; }
            if (zeroActive != 0)
            { Debug.LogError($"FAIL active: a zero request produced {zeroActive} active buildings (of {zeroPlaced}), want 0"); ok = false; }

            var negCfg = new SettlementConfig { Seed = 6, Size = SettlementSize.Small, ActiveBuildings = -3, HasWall = true };
            var negFloor = SettlementGenerator.Generate(negCfg, "poi-neg").Floors[0];
            int negActive = 0, negPlaced = 0;
            foreach (var r in negFloor.Rooms) if (r.TypeId == 1) { negPlaced++; if (!r.IsDummy) negActive++; }
            if (negActive != 0)
            { Debug.LogError($"FAIL active: a request of -3 produced {negActive} active buildings (of {negPlaced}), want 0 — the defensive floor must clamp a negative request to zero"); ok = false; }

            var overCfg = new SettlementConfig { Seed = 6, Size = SettlementSize.Small, ActiveBuildings = 1000000, HasWall = true };
            var overFloor = SettlementGenerator.Generate(overCfg, "poi-over").Floors[0];
            int overActive = 0, overPlaced = 0;
            foreach (var r in overFloor.Rooms) if (r.TypeId == 1) { overPlaced++; if (!r.IsDummy) overActive++; }
            if (overActive != overPlaced)
            { Debug.LogError($"FAIL active: a request of 1000000 produced {overActive} active of {overPlaced} placed, want ALL {overPlaced} active"); ok = false; }

            // ---- 6. THE DM DEFECT ITSELF, IN BOTH AXES: active buildings are spread by real 2D lattice
            // distance across the town, never clustered near one wall — the bug report was every active
            // building landing in one corner of a Large town with a small active count.
            //
            // A first cut of this fix (and this test) partitioned EMISSION ORDER into contiguous bands and
            // put one active pick per band. That was caught on review as only a partial fix: buildings are
            // emitted in ROW-MAJOR order (SettlementBlocks' block sort is j-then-i), so a band of emission
            // order is literally a horizontal STRIP — the old test could pass while every active pick still
            // hugged the same wall on the OTHER axis. Generation now marks activity by greedy farthest-point
            // sampling over the buildings' lattice cells instead (see BuildFloor's own doc), and this section
            // re-derives THAT rule directly rather than a 1-axis proxy of it.
            //
            // RE-DERIVES THE EXACT RULE INDEPENDENTLY — the SAME idiom SelfTestAssembly/SelfTestGates/
            // SelfTestVillage already use elsewhere in this file (reconstruct the SAME deterministic pipeline
            // from primitives, then compare element-for-element against the real BuildFloor output) rather
            // than trusting a derived metric (a span or a variance across positions would still read "spread
            // enough" with the underlying rule broken — this arc's own named failure mode). The reference
            // below is built ONLY from the floor's own room cells and the SAME seed formula BuildFloor uses
            // for this pass (cfg.Seed*3001+293) — it never reads BuildFloor's internal isActiveBuilding array
            // — so if a future edit dropped the RNG, hardcoded the starting pick, or reverted to any 1-axis
            // rule, the real output would diverge from this independently-computed set and the comparison
            // below fires.
            int[] spreadSeeds = { 501, 502, 503, 504, 505, 506 };
            foreach (int spreadSeed in spreadSeeds)
            {
                var spreadCfg = new SettlementConfig { Seed = spreadSeed, Size = SettlementSize.Large, ActiveBuildings = 5, HasWall = true };
                var spreadFloor = SettlementGenerator.Generate(spreadCfg, "poi-spread").Floors[0];
                var spreadBuildings = new System.Collections.Generic.List<Room>();
                foreach (var r in spreadFloor.Rooms) if (r.TypeId == 1) spreadBuildings.Add(r);
                int spreadPlaced = spreadBuildings.Count;
                int spreadGoal = spreadCfg.ActiveBuildings > spreadPlaced ? spreadPlaced : spreadCfg.ActiveBuildings;
                // NON-VACUITY: fewer than 2 picks says nothing about spread at all. This fixture's own
                // ActiveBuildings (5) keeps spreadGoal well above 2; this guard makes that assumption an
                // assertion instead of a silent precondition.
                if (spreadGoal < 2)
                { Debug.LogError($"FAIL active: seed {spreadSeed} spread fixture has spreadGoal {spreadGoal}, want >=2 — the farthest-point re-derivation below needs at least two picks to say anything about spread"); ok = false; continue; }

                var cellI = new int[spreadPlaced];
                var cellJ = new int[spreadPlaced];
                for (int i = 0; i < spreadPlaced; i++)
                {
                    cellI[i] = SettlementFootprint.CellOf(spreadBuildings[i].X);
                    cellJ[i] = SettlementFootprint.CellOf(spreadBuildings[i].Y);
                }

                // Independent re-derivation: same greedy farthest-point algorithm, same seed formula, computed
                // here from scratch — see the section comment above for why this is not circular.
                var refActive = new bool[spreadPlaced];
                var refRng = new System.Random(spreadSeed * 3001 + 293);
                int refFirst = refRng.Next(spreadPlaced);
                refActive[refFirst] = true;
                var minDist = new long[spreadPlaced];
                for (int x = 0; x < spreadPlaced; x++)
                {
                    long dx = cellI[x] - cellI[refFirst], dy = cellJ[x] - cellJ[refFirst];
                    minDist[x] = dx * dx + dy * dy;
                }
                for (int picked = 1; picked < spreadGoal; picked++)
                {
                    int best = -1; long bestDist = -1;
                    for (int x = 0; x < spreadPlaced; x++)
                    {
                        if (refActive[x]) continue;
                        if (minDist[x] > bestDist) { bestDist = minDist[x]; best = x; }
                    }
                    refActive[best] = true;
                    for (int x = 0; x < spreadPlaced; x++)
                    {
                        if (refActive[x]) continue;
                        long dx = cellI[x] - cellI[best], dy = cellJ[x] - cellJ[best];
                        long d = dx * dx + dy * dy;
                        if (d < minDist[x]) minDist[x] = d;
                    }
                }

                // Compare EXACTLY, cell for cell — the literal set the rule is supposed to produce, not a
                // count or a span of it.
                var realActiveCells = new System.Collections.Generic.HashSet<(int i, int j)>();
                var refActiveCells = new System.Collections.Generic.HashSet<(int i, int j)>();
                for (int i = 0; i < spreadPlaced; i++)
                {
                    if (!spreadBuildings[i].IsDummy) realActiveCells.Add((cellI[i], cellJ[i]));
                    if (refActive[i]) refActiveCells.Add((cellI[i], cellJ[i]));
                }
                if (realActiveCells.Count != refActiveCells.Count || !realActiveCells.SetEquals(refActiveCells))
                { Debug.LogError($"FAIL active: seed {spreadSeed} real active cells ({realActiveCells.Count}) do not match the independently re-derived farthest-point set ({refActiveCells.Count}) — the marking rule has drifted from greedy farthest-point sampling"); ok = false; }

                // ---- 6b. THE DM-VISIBLE PROPERTY ITSELF, NOT JUST AGREEMENT WITH A SECOND COPY OF THE RULE.
                // 6 above proves production agrees with an independently re-derived farthest-point formula —
                // it would still pass if the SAME misunderstanding were baked into both copies (e.g. both
                // measuring distance on the wrong axis, or both drawing from the wrong candidate set); it
                // says nothing about geometry on its own. This computes the cell BOUNDING BOX of every placed
                // building and of the active ones alone, and requires the active box's span on EACH axis to
                // cover at least a loose fraction of the full box's own span on that axis.
                //
                // ANCHORED TO THIS TOWN'S OWN MEASURED EXTENT, not a fixed cell count, so the bound survives
                // a sizing change instead of silently going slack or flaking when SettlementSizing's table
                // is re-measured. Farthest-point sampling pushes its picks toward the town's own hull
                // regardless of which building the seeded roll starts from — that is what greedy farthest-
                // point selection IS — so a loose (50%) fraction holds comfortably for the real rule without
                // being tight enough to flake; see the report for the hand-mutation proof that the discarded
                // one-per-emission-band rule fails this specific check on the i-axis while still passing it
                // on j (the asymmetry IS the finding: that rule only ever constrained j).
                int fullMinI = int.MaxValue, fullMaxI = int.MinValue, fullMinJ = int.MaxValue, fullMaxJ = int.MinValue;
                for (int i = 0; i < spreadPlaced; i++)
                {
                    if (cellI[i] < fullMinI) fullMinI = cellI[i];
                    if (cellI[i] > fullMaxI) fullMaxI = cellI[i];
                    if (cellJ[i] < fullMinJ) fullMinJ = cellJ[i];
                    if (cellJ[i] > fullMaxJ) fullMaxJ = cellJ[i];
                }
                int activeMinI = int.MaxValue, activeMaxI = int.MinValue, activeMinJ = int.MaxValue, activeMaxJ = int.MinValue;
                for (int i = 0; i < spreadPlaced; i++)
                {
                    if (spreadBuildings[i].IsDummy) continue;
                    if (cellI[i] < activeMinI) activeMinI = cellI[i];
                    if (cellI[i] > activeMaxI) activeMaxI = cellI[i];
                    if (cellJ[i] < activeMinJ) activeMinJ = cellJ[i];
                    if (cellJ[i] > activeMaxJ) activeMaxJ = cellJ[i];
                }
                int fullSpanI = fullMaxI - fullMinI, fullSpanJ = fullMaxJ - fullMinJ;
                int activeSpanI = activeMaxI - activeMinI, activeSpanJ = activeMaxJ - activeMinJ;
                const float SpreadSpanFraction = 0.5f;
                if (activeSpanI < fullSpanI * SpreadSpanFraction)
                { Debug.LogError($"FAIL active: seed {spreadSeed} active i-span {activeSpanI} ({activeMinI}..{activeMaxI}) covers less than {SpreadSpanFraction:P0} of the full i-span {fullSpanI} ({fullMinI}..{fullMaxI}) — active buildings are clustered on the i-axis"); ok = false; }
                if (activeSpanJ < fullSpanJ * SpreadSpanFraction)
                { Debug.LogError($"FAIL active: seed {spreadSeed} active j-span {activeSpanJ} ({activeMinJ}..{activeMaxJ}) covers less than {SpreadSpanFraction:P0} of the full j-span {fullSpanJ} ({fullMinJ}..{fullMaxJ}) — active buildings are clustered on the j-axis"); ok = false; }
            }

            // ---- 7. Perf: the farthest-point pass at its own worst case (activeGoal == buildings.Count, so
            // every one of buildings.Count picks rescans every remaining candidate) must not blow up town
            // generation. MEASURED, not Big-O reasoned: timed as a DELTA between a zero-active and a
            // max-active run on the identical seed/size, since block generation and street routing dominate
            // BuildFloor's overall cost and are unaffected by ActiveBuildings — subtracting them out isolates
            // what this pass alone costs. Logged either way so the number is on record even when comfortably
            // under threshold.
            var perfZeroCfg = new SettlementConfig { Seed = 9, Size = SettlementSize.Large, ActiveBuildings = 0, HasWall = true };
            var swZero = System.Diagnostics.Stopwatch.StartNew();
            SettlementGenerator.Generate(perfZeroCfg, "poi-perf-zero");
            swZero.Stop();
            var perfMaxCfg = new SettlementConfig { Seed = 9, Size = SettlementSize.Large, ActiveBuildings = 1000000, HasWall = true };
            var swMax = System.Diagnostics.Stopwatch.StartNew();
            SettlementGenerator.Generate(perfMaxCfg, "poi-perf-max");
            swMax.Stop();
            long farthestPointMs = swMax.ElapsedMilliseconds - swZero.ElapsedMilliseconds;
            Debug.Log($"Settlement Active/Dummy: farthest-point worst case (Large, activeGoal == buildings.Count) ~{farthestPointMs} ms (zero-active {swZero.ElapsedMilliseconds} ms, max-active {swMax.ElapsedMilliseconds} ms)");
            if (farthestPointMs > 50)
            { Debug.LogError($"FAIL active: farthest-point worst-case pass cost ~{farthestPointMs} ms (zero-active {swZero.ElapsedMilliseconds} ms vs max-active {swMax.ElapsedMilliseconds} ms), want well under a frame"); ok = false; }

            if (ok) Debug.Log("Settlement Active/Dummy: PASS");
        }

        [ContextMenu("Self-Test: Settlement Wall Bounds")]
        public void SelfTestWallBounds()
        {
            bool ok = true;
            // ---- 1. WallBoundsTiles' NORMALIZED path (test-only since InteriorFloor.Wall was removed): a
            // jitter-free octagon radius 0.3 at (0.5,0.5): vertex 0 at angle 0 is (0.8,0.5) → max X 0.8; the
            // opposite vertex is (0.2,0.5) → min X 0.2. In tiles that is ×TilesPerAxis (tileSpace: false). ----
            var wall = WallContour.Rounded(seed: 1, cx: 0.5f, cy: 0.5f, radius: 0.3f, sides: 8, jitter: 0f);
            var (minX, minY, maxX, maxY) = DungeonProjection.WallBoundsTiles(wall, tileSpace: false);
            float expMax = 0.8f * DungeonLayout.TilesPerAxis, expMin = 0.2f * DungeonLayout.TilesPerAxis;
            if (System.Math.Abs(maxX - expMax) > 0.5f)
            { Debug.LogError($"FAIL wallbounds: maxX {maxX:F1}, want ~{expMax:F1}"); ok = false; }
            if (System.Math.Abs(minX - expMin) > 0.5f)
            { Debug.LogError($"FAIL wallbounds: minX {minX:F1}, want ~{expMin:F1}"); ok = false; }

            // ---- 2. The DERIVED fence's bounds extend past the rooms alone (retargeted Task 7 from the removed
            // floor.Wall assertion): DeriveTownFence clears the outermost buildings by FenceMarginTiles, so its
            // AABB pokes past the footprint-only ContentBoundsTiles — unioning them keeps the whole walled town
            // on screen (exactly what FitBoundsFor does). The fence is TILE space already, so read it with
            // tileSpace: true (no ×T); a double-scale would both falsely satisfy "extends past" AND blow the
            // field, so the explicit magnitude guard below pins the tile-space path. ----
            //
            // Re-pinned 3->1 (arc C.2, task C). The frontage layout moved every building, and this fixture
            // turns out to be sensitive to WHICH node is extreme rather than to the fence at all: a GATE
            // projects into ContentBoundsTiles as RoomSizing.Default(0)'s 7x5 TILES (see DungeonLayout
            // LinkNodeFor's own note — 1.82 x 1.30 cells at the v11 pitch) while SettlementFence rasterizes it
            // as a bare centre POINT, so on a town whose extreme node on every side is a gate, the room bounds
            // out-reach the fence and nothing pokes past. Seed 3 became one of those.
            //
            // SCANNED before re-pinning, the same discipline every pinned fixture in this file uses: seeds
            // 1..60, this exact fixture shape (Medium / walled). The fence pokes past the room bounds on 51 of
            // 60 — so the property is emphatically still there and this is a fixture ripple, not a regression.
            // 1 is the FIRST poking seed (the head of the list is 1, 2, 4, 5, 6, 8, 9, 12, 13, 14) and is
            // otherwise unremarkable.
            //
            // BOTH PARAGRAPHS ABOVE WERE MEASURED ON THE ROUTED-ROADS FENCE, i.e. before Task 5 (see
            // DeriveTownFence's own doc). They are kept because their CONCLUSIONS survive, but read them as
            // bounds rather than as current figures: the fence now folds the stored streets and is strictly
            // LARGER on every measured seed, so 51-of-60 is a LOWER bound on how often it pokes past, and the
            // gate mechanism that disqualified seed 3 is correspondingly weaker (a street cell one lane
            // outside the outermost house now reaches past a gate's 7x5 rect on most sides). Seed 1 is
            // re-verified green under the new fence; nothing else was re-scanned.
            var cfg = new SettlementConfig { Seed = 1, Size = SettlementSize.Medium, HasWall = true };
            var floor = SettlementGenerator.BuildFloor(cfg);
            var fence = DungeonLayout.DeriveTownFence(floor);
            if (fence == null || !fence.IsClosedSane())
            { Debug.LogError("FAIL wallbounds: DeriveTownFence returned null/insane for a walled city"); ok = false; }
            else
            {
                var (rMinX, rMinY, rMaxX, rMaxY) = DungeonProjection.ContentBoundsTiles(floor);
                var (wMinX, wMinY, wMaxX, wMaxY) = DungeonProjection.WallBoundsTiles(fence, tileSpace: true);
                bool pokesPast = wMinX < rMinX - 0.5f || wMinY < rMinY - 0.5f
                              || wMaxX > rMaxX + 0.5f || wMaxY > rMaxY + 0.5f;
                if (!pokesPast)
                { Debug.LogError($"FAIL wallbounds: derived-fence bounds ({wMinX:F0}..{wMaxX:F0},{wMinY:F0}..{wMaxY:F0}) do not extend past the room bounds ({rMinX:F0}..{rMaxX:F0},{rMinY:F0}..{rMaxY:F0}) on any side"); ok = false; }
                // A silent ×T mix-up (tile-space fence read as normalized) would multiply every point by ~128,
                // pushing maxX far past the field — this pins tileSpace: true actually skips the scale.
                if (wMaxX > DungeonLayout.TilesPerAxis * 2f)
                { Debug.LogError($"FAIL wallbounds: fence maxX {wMaxX:F0} exceeds the tile field — WallBoundsTiles double-scaled a tile-space fence"); ok = false; }
            }

            // ---- 3. THE FENCE ENCLOSES THE STORED STREETS (Task 5), WITH ITS OWN CONTROL ------------------
            // DeriveTownFence's third input used to be the ROUTED roads (SettlementRoads.Build over
            // floor.Links). Both are gone; what it folds instead is SettlementParams.StreetCells, because
            // those ARE the town's streets now and because the drawn wall ring is dilated from exactly that
            // union (SettlementTileGrid.BuildWallRing seeds on Building ∪ StreetCells) — a fence that ignored
            // them would wrap a different town than the one on screen.
            //
            // (a) EVERY stored street cell's centre must be inside the derived fence, and the failure names
            // the offending cell. (b) THE CONTROL, and it is what makes (a) worth anything: re-derive the
            // fence from buildings + gates ALONE — exactly what SettlementFence sees if the streets fold is
            // ever dropped — and require that it leaves at least one street cell OUTSIDE. Measured on this
            // fixture: 21 of 78 street cells fall outside the buildings-only fence, and 0 fall outside the
            // real one. Without (b), (a) would pass just as happily on a fence that reached everywhere by
            // accident.
            var streetCells = SettlementFootprint.Decode(floor.SettlementParams?.StreetCells);
            if (streetCells.Count == 0)
            { Debug.LogError("FAIL wallbounds: the seed-1 Medium fixture stored NO street cells — assertions 3a/3b below would both be vacuous"); ok = false; }
            else if (fence != null && fence.IsClosedSane())
            {
                const float T = DungeonLayout.TilesPerAxis;
                foreach (var c in streetCells)
                {
                    float tx = SettlementFootprint.CenterOf(c.i) * T, ty = SettlementFootprint.CenterOf(c.j) * T;
                    if (!fence.Contains(tx, ty))
                    { Debug.LogError($"FAIL wallbounds: stored street cell ({c.i},{c.j}) — tile x={tx:F1} y={ty:F1} — is OUTSIDE the derived fence"); ok = false; break; }
                }

                var bareBuildings = new System.Collections.Generic.List<LinkNode>();
                var bareGates = new System.Collections.Generic.List<LinkNode>();
                foreach (var r in floor.Rooms)
                {
                    var n = DungeonLayout.LinkNodeFor(r, settlement: true);
                    if (r.TypeId == 1) bareBuildings.Add(n); else bareGates.Add(n);
                }
                var bareFence = SettlementFence.Derive(bareBuildings, bareGates,
                    new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
                // The control fence must EXIST before its cell count means anything: a null/degenerate derive
                // would leave `outside` at 0 and make the check below report the exact opposite of what
                // happened ("already encloses all N"). Separate assertion, separate message.
                if (bareFence == null || !bareFence.IsClosedSane())
                { Debug.LogError($"FAIL wallbounds: the buildings+gates-only CONTROL fence came back {(bareFence == null ? "null" : "not-sane")} from {bareBuildings.Count} buildings + {bareGates.Count} gates — the non-vacuity check below cannot run"); ok = false; }
                else
                {
                    int outside = 0;
                    foreach (var c in streetCells)
                        if (!bareFence.Contains(SettlementFootprint.CenterOf(c.i) * T, SettlementFootprint.CenterOf(c.j) * T)) outside++;
                    if (outside == 0)
                    { Debug.LogError($"FAIL wallbounds: a buildings+gates-only fence already encloses all {streetCells.Count} street cells — the street fold in DeriveTownFence is not load-bearing, so assertion 3a proves nothing"); ok = false; }
                }
            }

            if (ok) Debug.Log("Settlement Wall Bounds: PASS");
        }

        [ContextMenu("Self-Test: Building Footprint Corridors")]
        public void SelfTestBuildingFootprintCorridors()
        {
            bool ok = true;
            float m = FloorFootprint.ContourMargin;
            int T = DungeonLayout.TilesPerAxis;

            // Two 8×8 rooms far apart on one row, one link. Their connecting corridor runs straight door-to-
            // door through the OPEN GAP between them — outside the union of the two room rects (the "bow
            // outside the room-union outline" the building wall must wrap). Far enough apart that the whole
            // middle of the corridor sits clear of both inflated room rects.
            var floor = new InteriorFloor { NextRoomId = 3 };
            floor.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 8, SizeH = 8, X = 32f / T, Y = 64f / T });
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 8, SizeH = 8, X = 96f / T, Y = 64f / T });
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });

            // Route with the SAME Generation router the building renderer runs for the contour (TILE space).
            var corridors = DungeonLayout.BuildBuildingCorridors(floor);
            if (corridors.Count == 0)
            {
                Debug.LogError("FAIL building-footprint: the router produced no corridor — the fixture proves nothing");
                Debug.Log("Self-Test Building Footprint Corridors: FAIL");
                return;
            }

            // Arc-length midpoint of the whole routed polyline — any point on it lies in the wide gap, so
            // this is robust to however many legs the router returns.
            float Dist(LinkPoint a, LinkPoint b) => (float)System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            float total = 0f;
            foreach (var s in corridors) total += Dist(s.A, s.B);
            float half = total * 0.5f, acc = 0f, mx = corridors[0].A.X, my = corridors[0].A.Y;
            foreach (var s in corridors)
            {
                float len = Dist(s.A, s.B);
                if (acc + len >= half)
                {
                    float t = len > 1e-6f ? (half - acc) / len : 0f;
                    mx = s.A.X + (s.B.X - s.A.X) * t; my = s.A.Y + (s.B.Y - s.A.Y) * t;
                    break;
                }
                acc += len;
            }

            // LOAD-BEARING. The midpoint reads OUTSIDE the ROOMS-ONLY footprint (proof the corridor genuinely
            // bows out — else this test passes vacuously) and INSIDE once the corridor is folded in. The
            // MutFootprintNoCorridors mutant, which never adds the corridor rects, makes the second read
            // false and is caught right here.
            if (FloorFootprint.CoversPoint(floor, m, mx, my))
            { Debug.LogError($"FAIL building-footprint: corridor midpoint ({mx:F1},{my:F1}) is already inside the ROOMS-ONLY footprint — the corridor does not bow outside, so the test proves nothing"); ok = false; }
            if (!FloorFootprint.CoversPoint(floor, m, mx, my, corridors))
            { Debug.LogError($"FAIL building-footprint: corridor midpoint ({mx:F1},{my:F1}) reads OUTSIDE the rooms+corridor footprint — the wall does not wrap the corridor"); ok = false; }

            // Sanity: every routed corridor cell keeps >= ClearanceTiles from every room, EXCEPT the door
            // approaches (a corridor legitimately meets its rooms AT the wall — those cells sit within a door
            // band of an endpoint). This is the last surviving assertion of that shape — the settlement road
            // suite it mirrored retired with the road router (Task 5). The fixture is well-separated, so the
            // router's zero-clearance retry never fires; a corridor tunnelling a room would register interior
            // cells far from any door with distance ~0 → caught. Names the offending value.
            float doorBand = RoomLinkGeometry.ClearanceTiles + RoomLinkGeometry.DoorMargin;
            var ends = new[] { corridors[0].A, corridors[corridors.Count - 1].B };
            foreach (var s in corridors)
            {
                int ax = (int)System.Math.Round(s.A.X), ay = (int)System.Math.Round(s.A.Y);
                int bx = (int)System.Math.Round(s.B.X), by = (int)System.Math.Round(s.B.Y);
                int steps = System.Math.Max(System.Math.Abs(bx - ax), System.Math.Abs(by - ay));
                for (int i = 0; i <= steps; i++)
                {
                    int cx = ax + System.Math.Sign(bx - ax) * i, cy = ay + System.Math.Sign(by - ay) * i;
                    bool nearDoor = false;
                    foreach (var e in ends)
                        if (System.Math.Max(System.Math.Abs(cx - e.X), System.Math.Abs(cy - e.Y)) <= doorBand) { nearDoor = true; break; }
                    if (nearDoor) continue;
                    foreach (var r in floor.Rooms)
                    {
                        var (w, h) = DungeonProjection.EffectiveSize(r);
                        float dx = System.Math.Max(0f, System.Math.Abs(cx - r.X * T) - w * 0.5f);
                        float dy = System.Math.Max(0f, System.Math.Abs(cy - r.Y * T) - h * 0.5f);
                        float dist = System.Math.Max(dx, dy);
                        if (dist < RoomLinkGeometry.ClearanceTiles - 1e-3f)
                        { Debug.LogError($"FAIL building-footprint: corridor cell ({cx},{cy}) is {dist:F2} tiles from room {r.Id} — want >= {RoomLinkGeometry.ClearanceTiles}"); ok = false; }
                    }
                }
            }

            Debug.Log(ok ? "Self-Test Building Footprint Corridors: PASS" : "Self-Test Building Footprint Corridors: FAIL");
        }

        [ContextMenu("Self-Test: Settlement Fence")]
        public void SelfTestFence()
        {
            bool ok = true;

            // Fixture A: a compact 3x3 block of buildings (tile space), no gates.
            var block = new System.Collections.Generic.List<LinkNode>();
            int id = 1;
            for (int gy = 0; gy < 3; gy++)
                for (int gx = 0; gx < 3; gx++)
                    block.Add(new LinkNode { Id = id++, CX = 20 + gx * 6, CY = 20 + gy * 6, W = 5, H = 5 });
            var noGates = new System.Collections.Generic.List<LinkNode>();

            var fenceA = SettlementFence.Derive(block, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceA == null || !fenceA.IsClosedSane())
            { Debug.LogError("FAIL fence[A]: derive returned null or not-sane for a compact block"); ok = false; }
            else
            {
                // 1. every building RECT CORNER is strictly inside the fence (spec #1 — stronger than centre).
                foreach (var b in block)
                {
                    float hw = b.W * 0.5f, hh = b.H * 0.5f;
                    var corners = new (float x, float y)[] {
                        (b.CX - hw, b.CY - hh), (b.CX + hw, b.CY - hh),
                        (b.CX - hw, b.CY + hh), (b.CX + hw, b.CY + hh) };
                    foreach (var (cxp, cyp) in corners)
                        if (!fenceA.Contains(cxp, cyp))
                        { Debug.LogError($"FAIL fence[A]: building {b.Id} corner ({cxp},{cyp}) is NOT inside the fence"); ok = false; }
                }
                // margin: the fence clears the outermost building rect by ~FenceMarginTiles (within 1 tile of quantization).
                var far = block[8]; // CX=32,CY=32, top-right
                float d = fenceA.DistanceToEdge(far.CX + far.W * 0.5f, far.CY + far.H * 0.5f);
                if (System.Math.Abs(d - SettlementFence.FenceMarginTiles) > 1.0f)
                { Debug.LogError($"FAIL fence[A]: fence margin off — edge-dist {d} vs expected {SettlementFence.FenceMarginTiles}"); ok = false; }
            }

            // Fixture B: a DONUT — a ring of buildings with an empty centre. Rule 1: no hole.
            var ring = new System.Collections.Generic.List<LinkNode>();
            id = 1;
            int[,] rc = { {0,0},{1,0},{2,0},{0,1},{2,1},{0,2},{1,2},{2,2} }; // centre (1,1) omitted
            for (int k = 0; k < rc.GetLength(0); k++)
                ring.Add(new LinkNode { Id = id++, CX = 20 + rc[k,0] * 8, CY = 20 + rc[k,1] * 8, W = 6, H = 6 });
            var fenceB = SettlementFence.Derive(ring, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceB == null || !fenceB.IsClosedSane())
            { Debug.LogError("FAIL fence[B]: donut derive null/not-sane"); ok = false; }
            else if (!fenceB.Contains(20 + 8, 20 + 8)) // the empty centre (1,1) must read as INSIDE — filled, not a hole.
            { Debug.LogError("FAIL fence[B]: the donut centre (28,28) is OUTSIDE the fence — a hole leaked (Rule 1 broken)"); ok = false; }

            // Fixture C: buildings + a gate 3 tiles outside the block. Rule 2/3: the gate sits ~on the fence.
            var gate = new System.Collections.Generic.List<LinkNode> {
                new LinkNode { Id = 100, CX = 32 + 5, CY = 26, W = 1, H = 1 } };  // just outside building 9's right edge
            var fenceC = SettlementFence.Derive(block, gate, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceC == null || !fenceC.IsClosedSane())
            { Debug.LogError("FAIL fence[C]: gate fixture null/not-sane"); ok = false; }
            else
            {
                float gd = fenceC.DistanceToEdge(gate[0].CX, gate[0].CY);
                if (gd > 1.5f)
                { Debug.LogError($"FAIL fence[C]: gate centre ({gate[0].CX},{gate[0].CY}) is {gd} tiles from the fence — must be ~0 so the render gap opens"); ok = false; }
                if (!fenceC.Contains(20, 20))
                { Debug.LogError("FAIL fence[C]: interior building no longer inside after adding a gate"); ok = false; }
            }

            // Fixture D: one building dragged 20 tiles away — the stray-bridge must yield ONE closed loop containing it.
            var stray = new System.Collections.Generic.List<LinkNode>(block) {
                new LinkNode { Id = 200, CX = 60, CY = 60, W = 5, H = 5 } };
            var fenceD = SettlementFence.Derive(stray, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceD == null || !fenceD.IsClosedSane())
            { Debug.LogError("FAIL fence[D]: stray-building derive null/not-sane"); ok = false; }
            else if (!fenceD.Contains(60, 60) || !fenceD.Contains(20, 20))
            { Debug.LogError("FAIL fence[D]: stray building 200 and the main block are not BOTH inside one fence — bridge failed"); ok = false; }

            // Fixture E: a gate DRAGGED ~6 tiles outward (spec self-test #4). Re-derive; the fence must still
            // pass through the gate (bridge wraps its tip) and stay ONE sane loop. (At the DM checkpoint this
            // renders as a ~3-tile spike to the gate tip — eyeball it there; smoother "bend" is a later polish.)
            var gateNear = new System.Collections.Generic.List<LinkNode> {
                new LinkNode { Id = 300, CX = 32 + 5, CY = 26, W = 1, H = 1 } };
            var gateFar = new System.Collections.Generic.List<LinkNode> {
                new LinkNode { Id = 300, CX = 32 + 11, CY = 26, W = 1, H = 1 } };   // +6 tiles further out
            var fenceE0 = SettlementFence.Derive(block, gateNear, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            var fenceE1 = SettlementFence.Derive(block, gateFar, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceE1 == null || !fenceE1.IsClosedSane())
            { Debug.LogError("FAIL fence[E]: dragged-gate derive null/not-sane (not one loop)"); ok = false; }
            else
            {
                float ge = fenceE1.DistanceToEdge(gateFar[0].CX, gateFar[0].CY);
                if (ge > 1.5f)
                { Debug.LogError($"FAIL fence[E]: dragged gate ({gateFar[0].CX},{gateFar[0].CY}) is {ge} tiles from the fence — the fence did not bend through it"); ok = false; }
                // the fence MOVED with the gate: its edge must now sit farther right than before the drag.
                if (fenceE0 != null && fenceE1.DistanceToEdge(gateNear[0].CX, gateNear[0].CY) <= fenceE0.DistanceToEdge(gateNear[0].CX, gateNear[0].CY))
                { Debug.LogError("FAIL fence[E]: dragging the gate outward did not push the fence outward at the old gate spot"); ok = false; }
            }

            // Fixture F: ONE connected cluster (fixture A's 3x3 block) + a road SPUR reaching a far EMPTY
            // point. Task 2's ORIGINAL fixture F used two DISCONNECTED clusters, so Task 1's BridgeStrays —
            // which spans ANY two disjoint town components with its own straight bridge, road or no road —
            // filled part of the gap between them regardless of the road, confounding the road's effect (and
            // its query point sat on a fragile 1-tile fringe of that pre-existing bridge). Here there is only
            // ONE building component (`block`, from fixture A) and no other stray building, so BridgeStrays
            // never runs either way (components.Count <= 1, road present or not) — the far point can be
            // pulled inside ONLY by the road's own rasterized ribbon.
            var spurRoad = new System.Collections.Generic.List<LinkSegment> {
                new LinkSegment { A = new LinkPoint { X = 30, Y = 20 }, B = new LinkPoint { X = 45, Y = 20 }, EdgeIndex = 0 } };
            // Spur start (30,20) sits deep inside building 3's own inflated rect (CX=32,CY=20; half-extent
            // 2.5+margin=4.5 -> covers X 27.5..36.5, Y 15.5..24.5), so the ribbon merges into the block's
            // raster from its very first sample — no bridge needed. Its far end (45,20) IS the query point,
            // deep in the ribbon's own end-cap (marginTiles=2 clearance in every direction from the sample
            // point itself, not a fringe cell) and ~10 tiles clear of the block's own inflated edge (34.5),
            // so it reads OUTSIDE with no road at all.
            float fx = 45, fy = 20;
            var noSpurFence = SettlementFence.Derive(block, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            var withSpurFence = SettlementFence.Derive(block, noGates, spurRoad, SettlementFence.FenceMarginTiles);
            if (withSpurFence == null || !withSpurFence.IsClosedSane())
            { Debug.LogError("FAIL fence[F]: road-spur derive null/not-sane"); ok = false; }
            else if (noSpurFence == null || !noSpurFence.IsClosedSane())
            { Debug.LogError("FAIL fence[F]: no-road derive null/not-sane"); ok = false; }
            else
            {
                if (!withSpurFence.Contains(fx, fy))
                { Debug.LogError($"FAIL fence[F]: far spur point ({fx},{fy}) NOT inside the fence though a road reaches it"); ok = false; }
                if (noSpurFence.Contains(fx, fy))
                { Debug.LogError($"FAIL fence[F]: far point is inside even WITHOUT the road — the road input is not load-bearing (vacuous)"); ok = false; }
            }

            // Fixture G: THE FOOTPRINT FIXTURE. Every other fixture above hands SettlementFence hand-written
            // rects; this one starts from ROOMS carrying real multi-cell footprints on SettlementFootprint's
            // absolute lattice and projects them exactly the way production does — DungeonLayout.LinkNodeFor,
            // the ONE adapter DeriveTownFence itself calls. That is what makes "the fence
            // wraps footprints, not a nominal room size" a claim about shipped code: LinkNodeFor reads the
            // footprint's cell bounding box, so a settlement building no longer projects as
            // DungeonProjection.EffectiveSize's 6x6 tiles (which was 1.56 cells at the v11 pitch — never the
            // building's real size, merely a number that happened to be close).
            //
            // The bar is FOUR cells long on purpose. One cell is 3.84 tiles and the fence inflates by
            // FenceMarginTiles = 2, so a rect covering only the representative cell still reaches 3.92 tiles —
            // just past a 2-cell neighbour's centre at 3.84. A 2-cell footprint therefore CANNOT tell a
            // whole-footprint rasterization from a representative-cell one, and an assertion built on one
            // would be vacuous. At four cells the far cell's centre is 11.52 tiles out, far beyond that reach.
            // (Generation currently emits 1- and 2-cell footprints only; Room.Cells is a stored array of
            // arbitrary shape — SettlementFootprint's class doc commits to L, bar and ring — so a 4-cell bar
            // is a shape the format allows and the renderer already honours.)
            var fpRooms = new System.Collections.Generic.List<Room>();
            var bar = new System.Collections.Generic.List<(int i, int j)> { (10, 10), (11, 10), (12, 10), (13, 10) };
            var ell = new System.Collections.Generic.List<(int i, int j)> { (10, 13), (10, 14), (11, 14) };
            var oneCell = new System.Collections.Generic.List<(int i, int j)> { (14, 13) };
            foreach (var (roomId, fp) in new[] { (1, bar), (2, ell), (3, oneCell) })
            {
                var rep = SettlementFootprint.Representative(fp);
                fpRooms.Add(new Room
                {
                    Id = roomId, TypeId = 1,
                    X = SettlementFootprint.CenterOf(rep.i), Y = SettlementFootprint.CenterOf(rep.j),
                    Cells = SettlementFootprint.Encode(fp),
                });
            }
            var fpNodes = new System.Collections.Generic.List<LinkNode>();
            foreach (var r in fpRooms) fpNodes.Add(DungeonLayout.LinkNodeFor(r, settlement: true));
            var fenceG = SettlementFence.Derive(fpNodes, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            float cellT = SettlementFootprint.Pitch * DungeonLayout.TilesPerAxis;   // 3.84 tiles per cell
            if (fenceG == null || !fenceG.IsClosedSane())
            { Debug.LogError("FAIL fence[G]: footprint-fixture derive returned null or not-sane"); ok = false; }
            else
            {
                // EVERY cell of EVERY footprint is inside — not the representative, not the centroid, each
                // cell, by its own centre.
                foreach (var r in fpRooms)
                    foreach (var c in SettlementTileGrid.FootprintOf(r))
                    {
                        float tx = SettlementFootprint.CenterOf(c.i) * DungeonLayout.TilesPerAxis;
                        float ty = SettlementFootprint.CenterOf(c.j) * DungeonLayout.TilesPerAxis;
                        if (!fenceG.Contains(tx, ty))
                        { Debug.LogError($"FAIL fence[G]: building {r.Id} footprint cell ({c.i},{c.j}) — tile x={tx} y={ty} — is OUTSIDE the derived fence"); ok = false; }
                    }
                // NON-VACUITY of the enclosure test itself: a point THREE cells past the bar's far end (cell
                // 13 + 2 = 15's centre, plus one more cell) must read OUTSIDE, or "inside" would be saying
                // nothing about where the fence actually runs.
                float outX = SettlementFootprint.CenterOf(13 + 2) * DungeonLayout.TilesPerAxis + cellT;
                float outY = SettlementFootprint.CenterOf(10) * DungeonLayout.TilesPerAxis;
                if (fenceG.Contains(outX, outY))
                { Debug.LogError($"FAIL fence[G]: a point 3 cells past the bar's far end — tile x={outX} y={outY} — reads INSIDE, so the enclosure assertions above are vacuous"); ok = false; }
            }

            // 6. determinism: same inputs → identical point list.
            var again = SettlementFence.Derive(block, noGates, new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceA != null && again != null)
            {
                if (again.Points.Count != fenceA.Points.Count)
                { Debug.LogError($"FAIL fence[det]: point count {again.Points.Count} != {fenceA.Points.Count}"); ok = false; }
                else
                    for (int i = 0; i < again.Points.Count; i++)
                        if (System.Math.Abs(again.Points[i].X - fenceA.Points[i].X) > 1e-4f ||
                            System.Math.Abs(again.Points[i].Y - fenceA.Points[i].Y) > 1e-4f)
                        { Debug.LogError($"FAIL fence[det]: point {i} differs across identical derives"); ok = false; break; }
            }

            if (ok) Debug.Log("Settlement Fence: PASS");
        }

        [ContextMenu("Self-Test: Footprint")]
        public void SelfTestFootprint()
        {
            bool ok = true;

            // Encode/Decode round-trips, and an odd-length or null array decodes to empty rather than throwing.
            var cells = new System.Collections.Generic.List<(int i, int j)> { (3, 4), (4, 4), (4, 5) };
            var flat = SettlementFootprint.Encode(cells);
            var back = SettlementFootprint.Decode(flat);
            if (back.Count != 3 || back[0] != (3, 4) || back[1] != (4, 4) || back[2] != (4, 5))
            { Debug.LogError($"FAIL footprint: round-trip gave {back.Count} cells, first {(back.Count > 0 ? back[0].ToString() : "none")}"); ok = false; }
            if (SettlementFootprint.Decode(null).Count != 0 || SettlementFootprint.Decode(new[] { 1, 2, 3 }).Count != 0)
            { Debug.LogError("FAIL footprint: a null or odd-length array must decode to an EMPTY footprint, not throw"); ok = false; }

            // The lattice is FIXED: a cell index depends only on the coordinate, never on what is placed.
            if (SettlementFootprint.CellOf(0f) != 0)
            { Debug.LogError($"FAIL footprint: CellOf(0) = {SettlementFootprint.CellOf(0f)}, want 0"); ok = false; }
            float c = SettlementFootprint.Pitch;
            if (SettlementFootprint.CellOf(c * 3.5f) != 3)
            { Debug.LogError($"FAIL footprint: CellOf(3.5 pitch) = {SettlementFootprint.CellOf(c * 3.5f)}, want 3"); ok = false; }
            if (System.Math.Abs(SettlementFootprint.CenterOf(3) - c * 3.5f) > 1e-5f)
            { Debug.LogError($"FAIL footprint: CenterOf(3) = {SettlementFootprint.CenterOf(3)}, want {c * 3.5f}"); ok = false; }
            if (SettlementFootprint.CellOf(SettlementFootprint.CenterOf(7)) != 7)
            { Debug.LogError($"FAIL footprint: CenterOf/CellOf do not round-trip at cell 7"); ok = false; }

            // 4-connectivity: the L above is connected; a diagonal-only pair is NOT.
            if (!SettlementFootprint.IsConnected4(cells))
            { Debug.LogError("FAIL footprint: the L-shaped fixture must be 4-connected"); ok = false; }
            var diag = new System.Collections.Generic.List<(int i, int j)> { (0, 0), (1, 1) };
            if (SettlementFootprint.IsConnected4(diag))
            { Debug.LogError("FAIL footprint: two diagonal cells must NOT count as 4-connected"); ok = false; }

            // A ring with a hole is LEGAL (the DM chose arbitrary shapes).
            var ring = new System.Collections.Generic.List<(int i, int j)>();
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (i != 1 || j != 1) ring.Add((i, j));
            if (!SettlementFootprint.IsConnected4(ring))
            { Debug.LogError("FAIL footprint: a 3x3 ring around a hole must be legal and 4-connected"); ok = false; }

            // Overlap.
            var other = new System.Collections.Generic.List<(int i, int j)> { (4, 5), (5, 5) };
            if (!SettlementFootprint.Overlaps(cells, other))
            { Debug.LogError("FAIL footprint: footprints sharing cell (4,5) must overlap"); ok = false; }
            if (SettlementFootprint.Overlaps(cells, new System.Collections.Generic.List<(int i, int j)> { (9, 9) }))
            { Debug.LogError("FAIL footprint: disjoint footprints must not overlap"); ok = false; }

            // The representative cell must be ON the building — an L's true centroid can fall outside it.
            var rep = SettlementFootprint.Representative(cells);
            if (!cells.Contains(rep))
            { Debug.LogError($"FAIL footprint: representative cell {rep} is not one of the footprint's own cells"); ok = false; }
            if (rep != (3, 4))
            { Debug.LogError($"FAIL footprint: representative {rep} is not the lowest row-major cell (3,4) — it must be deterministic"); ok = false; }

            // Translate preserves shape and connectivity.
            var moved = SettlementFootprint.Translate(cells, 10, -2);
            if (moved.Count != 3 || moved[0] != (13, 2) || !SettlementFootprint.IsConnected4(moved))
            { Debug.LogError($"FAIL footprint: translate by (10,-2) gave first cell {(moved.Count > 0 ? moved[0].ToString() : "none")}"); ok = false; }

            // Bounds: the L above spans i 3..4, j 4..5. Hand-derived from the fixture, not read back from the
            // implementation, so a Bounds that silently returned the FIRST cell twice would fail here.
            var box = SettlementFootprint.Bounds(cells);
            if (box != (3, 4, 4, 5))
            { Debug.LogError($"FAIL footprint: Bounds of the L fixture = {box}, want (3,4,4,5)"); ok = false; }

            if (ok) Debug.Log("Settlement Footprint: PASS");
        }

        [ContextMenu("Self-Test: Footprint Migration")]
        public void SelfTestFootprintMigration()
        {
            bool ok = true;

            // A settlement floor exactly as an EXISTING save carries it: building rooms with X/Y and NO Cells
            // key at all. Expected cells are HAND-DERIVED from the LEGACY lattice — EnsureFootprints derives a
            // missing footprint with LegacyCellOf, because a room with no footprint can only have come from a
            // pre-v11 save, which was authored on the 0.07 pitch (cell i spans [i*LegacyPitch,
            // (i+1)*LegacyPitch)). Never read back from the implementation:
            //     0.30 / 0.07 =  4.2857… -> floor 4        0.05 / 0.07 =  0.7142… -> floor 0
            //     0.72 / 0.07 = 10.2857… -> floor 10       0.50 / 0.07 =  7.1428… -> floor 7
            var floor = new InteriorFloor();
            floor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.3f, Y = 0.3f, SizeW = 6, SizeH = 6 });
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = 0.05f, Y = 0.72f, SizeW = 6, SizeH = 6 });
            floor.Rooms.Add(new Room { Id = 3, TypeId = 0, X = 0.5f, Y = 0.5f, SizeW = 7, SizeH = 5 });
            // Room 4 ALREADY carries a footprint, and its own point maps to (4,4) — so an overwrite would be
            // plainly visible as (4,4) instead of the stored (9,9).
            floor.Rooms.Add(new Room { Id = 4, TypeId = 1, X = 0.3f, Y = 0.3f, SizeW = 6, SizeH = 6,
                Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (9, 9) }) });
            var town = new InteriorData { OwnerPoiId = "poi-migration", Kind = InteriorKind.Settlement };
            town.Floors.Add(floor);

            SettlementFootprint.EnsureFootprints(town, legacyLattice: true);

            void Expect(int id, int ei, int ej, string why)
            {
                var r = floor.GetRoom(id);
                var got = SettlementFootprint.Decode(r.Cells);
                if (got.Count != 1)
                { Debug.LogError($"FAIL footprint-migration: room {id} ({why}) ended with {got.Count} cells, want exactly 1"); ok = false; return; }
                if (got[0] != (ei, ej))
                { Debug.LogError($"FAIL footprint-migration: room {id} ({why}) got cell {got[0]}, want ({ei},{ej})"); ok = false; }
            }

            Expect(1, 4, 4, "no footprint in the save");
            Expect(2, 0, 10, "no footprint in the save");
            Expect(4, 9, 9, "already had a footprint, must NOT be overwritten");
            // A GATE (TypeId 0) GETS ONE TOO, from v11. It used to be required to stay footprint-less; the
            // v11 lattice migration moves a town by translating its CELLS, so a cell-less gate would be the
            // one node left standing in the middle of the field while the town moved around it. Its cell is
            // LegacyCellOf(0.5) = 7 on both axes — hand-derived above, and deliberately NOT CellOf(0.5) = 16,
            // which is what a migration reading a legacy point on the current lattice would produce.
            Expect(3, 7, 7, "a gate gets its ring cell too (v11), on the LEGACY pitch");

            // THE LANDMINE. The footprint is a SEPARATE field: SizeW/SizeH are TILES (one lattice cell is
            // 0.03 * 128 = 3.84 tiles), so a migration that reinterpreted them as cells would rewrite every
            // saved town's scale, silently, on load. Pin that this pass never writes them.
            foreach (var r in floor.Rooms)
            {
                int wantW = r.TypeId == 0 ? 7 : 6, wantH = r.TypeId == 0 ? 5 : 6;
                if (r.SizeW != wantW || r.SizeH != wantH)
                { Debug.LogError($"FAIL footprint-migration: room {r.Id} SizeW/SizeH became {r.SizeW}x{r.SizeH}, want {wantW}x{wantH} — the footprint must never touch the TILE size"); ok = false; }
            }

            // IDEMPOTENCE, proved by assertion: a second pass over the already-normalized floor changes
            // nothing at all — not the freshly-migrated rooms, not the pre-existing footprint, not the gate.
            string Dump(Room r) => r.Cells == null ? "null" : string.Join(",", r.Cells);
            var snapshot = new System.Collections.Generic.List<string>();
            foreach (var r in floor.Rooms) snapshot.Add(Dump(r));
            SettlementFootprint.EnsureFootprints(town, legacyLattice: true);
            for (int k = 0; k < floor.Rooms.Count; k++)
                if (Dump(floor.Rooms[k]) != snapshot[k])
                { Debug.LogError($"FAIL footprint-migration: a SECOND EnsureFootprints changed room {floor.Rooms[k].Id} from '{snapshot[k]}' to '{Dump(floor.Rooms[k])}' — the normalization is not idempotent"); ok = false; }

            // A BUILDING interior (Ц2 recursion) also holds TypeId==1 rooms. Only Kind == Settlement carries
            // footprints — without the Kind guard every room of every building interior would acquire a
            // meaningless one on load.
            var bfloor = new InteriorFloor();
            bfloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.3f, Y = 0.3f });
            var building = new InteriorData { OwnerPoiId = "poi-migration", OwnerRoomId = 4, Kind = InteriorKind.Building };
            building.Floors.Add(bfloor);
            SettlementFootprint.EnsureFootprints(building, legacyLattice: true);
            if (bfloor.Rooms[0].Cells != null)
            { Debug.LogError($"FAIL footprint-migration: a Building interior's room got a footprint of {bfloor.Rooms[0].Cells.Length} ints, want none"); ok = false; }

            // SELF-HEAL (review fix): an ODD-LENGTH Cells array (corrupt or hand-edited) is non-empty by
            // Length but Decodes to zero cells. A guard on Length alone would read that as "already
            // footprinted" and skip it — and because the pass never overwrites, a Length-only guard would
            // make that bad array PERMANENT, unlike everything else Decode is hardened against. Guarding on
            // Decode(...).Count instead means this exact input self-heals on the very next load: room 5
            // carries a 3-int (odd) array and must come out with exactly ONE cell at its own X/Y, same as a
            // room with no Cells at all.
            var oddFloor = new InteriorFloor();
            oddFloor.Rooms.Add(new Room { Id = 5, TypeId = 1, X = 0.3f, Y = 0.3f, SizeW = 6, SizeH = 6,
                Cells = new[] { 9, 9, 1 } });
            var oddTown = new InteriorData { OwnerPoiId = "poi-migration-odd", Kind = InteriorKind.Settlement };
            oddTown.Floors.Add(oddFloor);
            SettlementFootprint.EnsureFootprints(oddTown, legacyLattice: true);
            var oddRoom = oddFloor.GetRoom(5);
            var oddGot = SettlementFootprint.Decode(oddRoom.Cells);
            if (oddGot.Count != 1 || oddGot[0] != (4, 4))
            { Debug.LogError($"FAIL footprint-migration: room 5 (odd-length Cells [9,9,1]) ended with {oddGot.Count} cells ({(oddGot.Count > 0 ? oddGot[0].ToString() : "none")}), want exactly 1 cell (4,4) — an odd-length array must self-heal, not stay footprint-less forever"); ok = false; }

            // A corrupt/absent interior must degrade, not throw, exactly like Decode.
            SettlementFootprint.EnsureFootprints(null, legacyLattice: true);

            if (ok) Debug.Log("Settlement Footprint Migration: PASS");
        }

        [ContextMenu("Self-Test: Settlement Sizing")]
        public void SelfTestSizing()
        {
            bool ok = true;

            // The table must be monotone in every column — a bigger town is never smaller, never has fewer
            // gates, never promises fewer buildings. This is the property the whole UI rests on.
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            for (int k = 1; k < sizes.Length; k++)
            {
                if (SettlementSizing.WallRadiusCells(sizes[k]) <= SettlementSizing.WallRadiusCells(sizes[k - 1]))
                { Debug.LogError($"FAIL sizing: radius {SettlementSizing.WallRadiusCells(sizes[k])} at {sizes[k]} is not greater than {SettlementSizing.WallRadiusCells(sizes[k - 1])} at {sizes[k - 1]}"); ok = false; }
                if (SettlementSizing.TargetBuildings(sizes[k]) <= SettlementSizing.TargetBuildings(sizes[k - 1]))
                { Debug.LogError($"FAIL sizing: target {SettlementSizing.TargetBuildings(sizes[k])} at {sizes[k]} is not greater than at {sizes[k - 1]}"); ok = false; }
                if (SettlementSizing.GateCount(sizes[k]) < SettlementSizing.GateCount(sizes[k - 1]))
                { Debug.LogError($"FAIL sizing: gate count {SettlementSizing.GateCount(sizes[k])} at {sizes[k]} is below {SettlementSizing.GateCount(sizes[k - 1])} at {sizes[k - 1]}"); ok = false; }
            }

            // The guarantee must be a PROMISE, i.e. strictly below the target it is promised against —
            // a minimum equal to the target is the exact lie TargetBuildings used to tell.
            foreach (var s in sizes)
            {
                if (SettlementSizing.GuaranteedMinBuildings(s) >= SettlementSizing.TargetBuildings(s))
                { Debug.LogError($"FAIL sizing: {s} guarantees {SettlementSizing.GuaranteedMinBuildings(s)} against a target of {SettlementSizing.TargetBuildings(s)} — a guarantee must be below the target"); ok = false; }
                if (SettlementSizing.GuaranteedMinBuildings(s) < 1)
                { Debug.LogError($"FAIL sizing: {s} guarantees {SettlementSizing.GuaranteedMinBuildings(s)} buildings"); ok = false; }
            }

            // THE FIELD BOUND. The largest town plus its wall must fit inside the drag clamp's 0.04..0.96,
            // centred on 0.5 — this is the constraint that forced the pitch change, and nothing may quietly
            // grow past it later.
            float rNorm = SettlementSizing.WallRadiusNorm(SettlementSize.Large);
            if (0.5f - rNorm < 0.04f || 0.5f + rNorm > 0.96f)
            { Debug.LogError($"FAIL sizing: a Large town's wall radius {rNorm} normalized leaves the 0.04..0.96 field (spans {0.5f - rNorm}..{0.5f + rNorm})"); ok = false; }

            // Legacy bucketing: the two shipped defaults (10 and 20) and the old default 40 must land where
            // the spec says, and the boundaries are inclusive on the low side.
            if (SettlementSizing.FromLegacyTarget(10) != SettlementSize.Small ||
                SettlementSizing.FromLegacyTarget(20) != SettlementSize.Small ||
                SettlementSizing.FromLegacyTarget(30) != SettlementSize.Small)
            { Debug.LogError($"FAIL sizing: legacy 10/20/30 bucketed to {SettlementSizing.FromLegacyTarget(10)}/{SettlementSizing.FromLegacyTarget(20)}/{SettlementSizing.FromLegacyTarget(30)}, want Small"); ok = false; }
            if (SettlementSizing.FromLegacyTarget(31) != SettlementSize.Medium || SettlementSizing.FromLegacyTarget(80) != SettlementSize.Medium)
            { Debug.LogError($"FAIL sizing: legacy 31/80 bucketed to {SettlementSizing.FromLegacyTarget(31)}/{SettlementSizing.FromLegacyTarget(80)}, want Medium"); ok = false; }
            if (SettlementSizing.FromLegacyTarget(81) != SettlementSize.Large)
            { Debug.LogError($"FAIL sizing: legacy 81 bucketed to {SettlementSizing.FromLegacyTarget(81)}, want Large"); ok = false; }

            if (ok) Debug.Log("Settlement Sizing: PASS");
        }

        [ContextMenu("Self-Test: Settlement Size Migration")]
        public void SelfTestSizeMigration()
        {
            bool ok = true;

            // A pre-v11 settlement floor exactly as a save carries one, and deliberately OFF-CENTRE — the
            // whole point of the migration is that a legacy town's cell indices, re-read on the finer v11
            // lattice, land at 3/7 of their old normalized position, i.e. off in a corner. Every expected
            // value below is HAND-DERIVED from the two pitches, never read back from the implementation:
            //
            //   LEGACY pitch 0.07:  0.60 / 0.07 = 8.571… -> cell 8      0.66 / 0.07 = 9.428… -> cell 9
            //   CURRENT pitch 0.03: 0.50 / 0.03 = 16.66… -> cell 16   (the field's centre cell)
            //
            // Room 1 is a GATE with X/Y and NO cells (a pre-v11 save never stored one) — EnsureFootprints
            // must give it exactly one, at the LEGACY cell (8,9). Rooms 2 and 3 are buildings that ALREADY
            // carry adjacent legacy cells (8,8) and (9,8). Two street cells sit a row below at j = 7.
            var floor = new InteriorFloor { NextRoomId = 4 };
            floor.Rooms.Add(new Room { Id = 1, TypeId = 0, X = 0.60f, Y = 0.66f, SizeW = 7, SizeH = 5 });
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = 0.595f, Y = 0.595f, SizeW = 6, SizeH = 6,
                Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (8, 8) }) });
            floor.Rooms.Add(new Room { Id = 3, TypeId = 1, X = 0.665f, Y = 0.595f, SizeW = 6, SizeH = 6,
                Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (9, 8) }) });
            floor.SettlementParams = new SettlementParams
            {
                Size = SettlementSize.Small,
                ActiveBuildings = 2,
                HasWall = true,
                StreetCells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (8, 7), (9, 7) }),
            };
            var town = new InteriorData { OwnerPoiId = "poi-size-migration", Kind = InteriorKind.Settlement };
            town.Floors.Add(floor);

            // ---- 1. The gate gains EXACTLY ONE cell, at LegacyCellOf of its stored point ----------------
            // (8,9), not CellOf(0.60)/CellOf(0.66) = (20,22): a footprint-less room can only have come from a
            // pre-v11 save, so its point means a LEGACY cell. Reading it on the current lattice would fling
            // the gate 12 cells clear of the town it belongs to — MutMigrationCurrentPitch is exactly that.
            SettlementFootprint.EnsureFootprints(town, legacyLattice: true);
            var gateCells = SettlementFootprint.Decode(floor.GetRoom(1).Cells);
            int wantGi = SettlementFootprint.LegacyCellOf(0.60f), wantGj = SettlementFootprint.LegacyCellOf(0.66f);
            if (gateCells.Count != 1)
            { Debug.LogError($"FAIL size-migration: the gate (room 1) ended with {gateCells.Count} cells, want exactly 1"); ok = false; }
            else if (gateCells[0] != (8, 9) || gateCells[0] != (wantGi, wantGj))
            { Debug.LogError($"FAIL size-migration: the gate's cell is {gateCells[0]}, want (8,9) — LegacyCellOf(0.60),LegacyCellOf(0.66) = ({wantGi},{wantGj}), NOT CellOf's ({SettlementFootprint.CellOf(0.60f)},{SettlementFootprint.CellOf(0.66f)})"); ok = false; }

            // Non-vacuity, asserted rather than assumed: the two pitches must actually DISAGREE here, or the
            // assertion above would pass for a migration that used either one.
            if (SettlementFootprint.LegacyCellOf(0.60f) == SettlementFootprint.CellOf(0.60f))
            { Debug.LogError($"FAIL size-migration: LegacyCellOf and CellOf both map 0.60 to cell {SettlementFootprint.CellOf(0.60f)} — the pitch assertion above cannot discriminate"); ok = false; }

            // ---- 2. The town is OFF-CENTRE before the migration runs (the fixture is worth something) ---
            var (b0MinI, b0MinJ, b0MaxI, b0MaxJ) = TownCellBounds(floor);
            int centreCell = SettlementFootprint.CellOf(0.5f);
            if (b0MinI != 8 || b0MaxI != 9 || b0MinJ != 7 || b0MaxJ != 9)
            { Debug.LogError($"FAIL size-migration: the fixture's pre-migration cell bbox is ({b0MinI}..{b0MaxI},{b0MinJ}..{b0MaxJ}), want (8..9,7..9)"); ok = false; }
            if ((b0MinI + b0MaxI) / 2 == centreCell)
            { Debug.LogError($"FAIL size-migration: the fixture is ALREADY centred on cell {centreCell} — RecentreFloor would be a no-op and assertion 3 would prove nothing"); ok = false; }

            // ---- 3. RecentreFloor puts the cell bbox's CENTRE on the field's centre cell, both axes -----
            // 8+9 = 17, halved (floor) = 8, so every cell moves by 16-8 = +8; likewise 7+9 = 16 -> 8 -> +8.
            SettlementMigration.RecentreFloor(floor);
            var (minI, minJ, maxI, maxJ) = TownCellBounds(floor);
            if ((minI + maxI) / 2 != centreCell || (minJ + maxJ) / 2 != centreCell)
            { Debug.LogError($"FAIL size-migration: after RecentreFloor the cell bbox ({minI}..{maxI},{minJ}..{maxJ}) has centre ({(minI + maxI) / 2},{(minJ + maxJ) / 2}), want ({centreCell},{centreCell}) on both axes"); ok = false; }
            if (minI != 16 || maxI != 17 || minJ != 15 || maxJ != 17)
            { Debug.LogError($"FAIL size-migration: after RecentreFloor the cell bbox is ({minI}..{maxI},{minJ}..{maxJ}), want the hand-derived (16..17,15..17)"); ok = false; }

            // ---- 4. RELATIVE GEOMETRY SURVIVES: the two buildings are STILL flush neighbours ------------
            // The offset is one COMMON integer delta, so every fact about the town's shape is preserved. A
            // per-room recentring (each building centred on its own bbox) would collapse both onto one cell.
            var a = SettlementFootprint.Decode(floor.GetRoom(2).Cells);
            var b = SettlementFootprint.Decode(floor.GetRoom(3).Cells);
            if (a.Count != 1 || b.Count != 1)
            { Debug.LogError($"FAIL size-migration: buildings 2/3 came out with {a.Count}/{b.Count} cells, want 1 each — the translation must not add or drop cells"); ok = false; }
            else if (b[0].i - a[0].i != 1 || b[0].j != a[0].j)
            { Debug.LogError($"FAIL size-migration: buildings 2 and 3 are at {a[0]} and {b[0]} — they were flush 4-neighbours (8,8)/(9,8) and must still be"); ok = false; }
            else if (a[0] != (16, 16) || b[0] != (17, 16))
            { Debug.LogError($"FAIL size-migration: buildings 2/3 landed at {a[0]}/{b[0]}, want the hand-derived (16,16)/(17,16)"); ok = false; }

            // The streets moved by the SAME delta — they are stored separately from the rooms, so a
            // migration that translated only Room.Cells would drive every street through a house.
            var st = SettlementFootprint.Decode(floor.SettlementParams.StreetCells);
            if (st.Count != 2 || st[0] != (16, 15) || st[1] != (17, 15))
            { Debug.LogError($"FAIL size-migration: the street cells came out {(st.Count > 0 ? st[0].ToString() : "none")}/{(st.Count > 1 ? st[1].ToString() : "none")} ({st.Count} of them), want (16,15)/(17,15)"); ok = false; }

            // ---- 5. RederivePositions writes each room's point from its OWN cells ------------------------
            // The load-bearing property is the ROUND TRIP: SettlementTileGrid.FootprintOf treats a
            // single-cell footprint whose room point falls in a DIFFERENT cell as stale and re-derives it
            // from the point, so a point that does not land back in its own cell would silently relocate
            // every one-cell house in town. Checked per room, plus one hand-derived absolute value:
            // CenterOf(16) = 16.5 * 0.03 = 0.495.
            SettlementMigration.RederivePositions(town);
            foreach (var r in floor.Rooms)
            {
                var cells = SettlementFootprint.Decode(r.Cells);
                var (cMinI, cMinJ, cMaxI, cMaxJ) = SettlementFootprint.Bounds(cells);
                float wantX = (SettlementFootprint.CenterOf(cMinI) + SettlementFootprint.CenterOf(cMaxI)) * 0.5f;
                float wantY = (SettlementFootprint.CenterOf(cMinJ) + SettlementFootprint.CenterOf(cMaxJ)) * 0.5f;
                if (System.Math.Abs(r.X - wantX) > SettlementFootprint.Pitch * 0.5f ||
                    System.Math.Abs(r.Y - wantY) > SettlementFootprint.Pitch * 0.5f)
                { Debug.LogError($"FAIL size-migration: room {r.Id} sits at ({r.X:F4},{r.Y:F4}), more than half a pitch from its cells' bbox centre ({wantX:F4},{wantY:F4})"); ok = false; }
                if (SettlementFootprint.CellOf(r.X) != cMinI || SettlementFootprint.CellOf(r.Y) != cMinJ)
                { Debug.LogError($"FAIL size-migration: room {r.Id}'s point ({r.X:F4},{r.Y:F4}) falls in cell ({SettlementFootprint.CellOf(r.X)},{SettlementFootprint.CellOf(r.Y)}), not its own ({cMinI},{cMinJ}) — FootprintOf would read the footprint as stale"); ok = false; }
            }
            var b2 = floor.GetRoom(2);
            if (System.Math.Abs(b2.X - 0.495f) > 1e-4f || System.Math.Abs(b2.Y - 0.495f) > 1e-4f)
            { Debug.LogError($"FAIL size-migration: building 2 (cell (16,16)) sits at ({b2.X:F4},{b2.Y:F4}), want the hand-derived (0.4950,0.4950) = CenterOf(16) on both axes"); ok = false; }

            // ---- 6. IDEMPOTENCE: a second RecentreFloor changes nothing at all ---------------------------
            // It is version-gated at the call site, but a pass that MOVED an already-centred town would make
            // that gate the only thing standing between the DM and a town that drifts on every load.
            string Dump(Room r) => r.Cells == null ? "null" : string.Join(",", r.Cells);
            var snapshot = new System.Collections.Generic.List<string>();
            foreach (var r in floor.Rooms) snapshot.Add(Dump(r));
            string streetSnapshot = string.Join(",", floor.SettlementParams.StreetCells);
            SettlementMigration.RecentreFloor(floor);
            for (int k = 0; k < floor.Rooms.Count; k++)
                if (Dump(floor.Rooms[k]) != snapshot[k])
                { Debug.LogError($"FAIL size-migration: a SECOND RecentreFloor changed room {floor.Rooms[k].Id} from '{snapshot[k]}' to '{Dump(floor.Rooms[k])}' — recentring is not idempotent"); ok = false; }
            if (string.Join(",", floor.SettlementParams.StreetCells) != streetSnapshot)
            { Debug.LogError($"FAIL size-migration: a SECOND RecentreFloor changed the street cells from '{streetSnapshot}' to '{string.Join(",", floor.SettlementParams.StreetCells)}'"); ok = false; }

            // ---- 7. A NEGATIVE, ODD BBOX SUM — the case plain `/` gets wrong -----------------------------
            // RecentreFloor halves the bbox sum with FLOOR division, not C#'s truncate-toward-zero `/`, and
            // fixture 1 above CANNOT tell the two apart: its sums are 17 and 16, both positive, where
            // FloorHalf(17) == 17 / 2 == 8. Every assertion above therefore passes verbatim against the
            // truncating implementation the floor-division exists to replace, which is no coverage at all.
            //
            // Cells at i = -2..-1 sum to -3: ODD and NEGATIVE, the only regime where the two disagree.
            //   floor:    floor(-3/2) = -2  ->  delta 16-(-2) = +18  ->  i = 16..17, sum 33, floor -> 16 ✓
            //   truncate: (-3)/2      = -1  ->  delta 16-(-1) = +17  ->  i = 15..16, sum 31, trunc -> 15 ✗
            // So truncation lands the town ONE CELL off target AND leaves a second RecentreFloor with a
            // non-zero delta — the town moves again on the next load. Both halves are asserted below, and
            // MutMigrationTruncatingHalf pins them.
            //
            // Negative indices are reachable in production, not a synthetic curiosity: the lattice origin is
            // normalized 0 and CellOf floors, so any pre-v11 town whose buildings sat near the field's origin
            // has cells at or below 0 once the ring street and the courtyard margin are counted outward.
            var negFloor = new InteriorFloor { NextRoomId = 3 };
            negFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = -0.1f, Y = 0.2f, SizeW = 6, SizeH = 6,
                Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (-2, 3) }) });
            negFloor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = -0.05f, Y = 0.2f, SizeW = 6, SizeH = 6,
                Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (-1, 3) }) });
            negFloor.SettlementParams = new SettlementParams { Size = SettlementSize.Small, ActiveBuildings = 2, HasWall = true };

            var (nMinI0, nMinJ0, nMaxI0, nMaxJ0) = TownCellBounds(negFloor);
            if (nMinI0 + nMaxI0 != -3 || (nMinI0 + nMaxI0) % 2 == 0)
            { Debug.LogError($"FAIL size-migration: the negative fixture's i bbox is {nMinI0}..{nMaxI0}, sum {nMinI0 + nMaxI0} — it must be ODD and NEGATIVE (-3) or it cannot discriminate floor from truncating division"); ok = false; }

            SettlementMigration.RecentreFloor(negFloor);
            var (nMinI, nMinJ, nMaxI, nMaxJ) = TownCellBounds(negFloor);
            if (nMinI != 16 || nMaxI != 17)
            { Debug.LogError($"FAIL size-migration: a bbox summing to -3 on i recentred to {nMinI}..{nMaxI}, want the hand-derived 16..17 — truncating division would give 15..16, one cell short of centre {centreCell}"); ok = false; }
            if ((nMinI + nMaxI) / 2 != centreCell)
            { Debug.LogError($"FAIL size-migration: a bbox summing to -3 on i recentred to centre {(nMinI + nMaxI) / 2}, want {centreCell}"); ok = false; }

            // The idempotence half, which is the one that actually bites the DM: truncation leaves a non-zero
            // delta behind, so the town keeps walking one cell per load.
            string negSnapshot = string.Join(";", negFloor.Rooms.ConvertAll(Dump));
            SettlementMigration.RecentreFloor(negFloor);
            string negAfter = string.Join(";", negFloor.Rooms.ConvertAll(Dump));
            if (negAfter != negSnapshot)
            { Debug.LogError($"FAIL size-migration: a SECOND RecentreFloor on the negative-odd fixture changed the cells from '{negSnapshot}' to '{negAfter}' — halving is truncating toward zero instead of flooring, so the town moves again on every load"); ok = false; }

            // ---- 8. THE CURRENT-LATTICE BRANCH of EnsureFootprints (v11 file) ----------------------------
            // legacyLattice is the caller's decision and follows the FILE'S FORMAT VERSION. Fixture 1 above
            // exercises only the TRUE branch; without this, forcing the pitch to legacy unconditionally would
            // pass every assertion in this file while writing a 0.07-pitch index into a v11 save that no
            // later pass repairs (RecentreFloor/RederivePositions are gated off at v11) and that the render
            // masks (FootprintOf rule (b)). MutMigrationAlwaysLegacyPitch pins this branch.
            //
            // The SAME point 0.60/0.66 as fixture 1's gate, so the two branches are directly comparable:
            //   CellOf:       0.60 / 0.03 = 20.0  -> 20      0.66 / 0.03 = 22.0  -> 22
            //   LegacyCellOf: 0.60 / 0.07 =  8.57 ->  8      0.66 / 0.07 =  9.43 ->  9
            var v11Floor = new InteriorFloor { NextRoomId = 2 };
            v11Floor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.60f, Y = 0.66f, SizeW = 6, SizeH = 6 });
            v11Floor.SettlementParams = new SettlementParams { Size = SettlementSize.Small, ActiveBuildings = 1 };
            var v11Town = new InteriorData { OwnerPoiId = "poi-v11-lattice", Kind = InteriorKind.Settlement };
            v11Town.Floors.Add(v11Floor);

            SettlementFootprint.EnsureFootprints(v11Town, legacyLattice: false);
            var v11Cells = SettlementFootprint.Decode(v11Floor.GetRoom(1).Cells);
            if (v11Cells.Count != 1)
            { Debug.LogError($"FAIL size-migration: the v11-lattice room ended with {v11Cells.Count} cells, want exactly 1"); ok = false; }
            else if (v11Cells[0] != (20, 22))
            { Debug.LogError($"FAIL size-migration: EnsureFootprints(legacyLattice: false) stamped cell {v11Cells[0]}, want CellOf(0.60),CellOf(0.66) = (20,22) — NOT LegacyCellOf's ({SettlementFootprint.LegacyCellOf(0.60f)},{SettlementFootprint.LegacyCellOf(0.66f)}). A v11 file stamped on the legacy pitch is wrong data at rest that no later pass repairs."); ok = false; }

            // A null floor/interior must degrade, not throw — same contract as EnsureFootprints.
            SettlementMigration.RecentreFloor(null);
            SettlementMigration.RederivePositions(null);

            if (ok) Debug.Log("Settlement Size Migration: PASS");
        }

        // The cell bbox over EVERYTHING on a settlement floor — every TypeId 0/1 room's footprint plus the
        // stored street cells. Re-derived here rather than read off SettlementMigration, so the assertions
        // above measure the migration instead of asking it to grade itself.
        static (int minI, int minJ, int maxI, int maxJ) TownCellBounds(InteriorFloor floor)
        {
            var all = new System.Collections.Generic.List<(int i, int j)>();
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId != 0 && r.TypeId != 1) continue;
                all.AddRange(SettlementFootprint.Decode(r.Cells));
            }
            all.AddRange(SettlementFootprint.Decode(floor.SettlementParams?.StreetCells));
            return SettlementFootprint.Bounds(all);
        }

        /// <summary>One settlement floor holding exactly the rooms given, as (TypeId, id, cells) triples, plus
        /// the street cells. Lives OUTSIDE every self-test method so the mutant rebind — which extracts one
        /// method body — never has to carry it, exactly like SettlementBlocksSelfTests' own helpers.
        /// Room.X/Y is the REPRESENTATIVE cell's centre, the same rule SettlementGenerator.BuildFloor follows,
        /// so SettlementTileGrid.FootprintOf's rule (b) never reads a single-cell footprint as stale.</summary>
        static InteriorData TownOf((int typeId, int id, (int i, int j)[] cells)[] rooms, (int i, int j)[] streets)
        {
            var floor = new InteriorFloor();
            foreach (var (typeId, id, cells) in rooms)
            {
                var list = new System.Collections.Generic.List<(int i, int j)>(cells ?? new (int, int)[0]);
                var rep = SettlementFootprint.Representative(list);
                floor.Rooms.Add(new Room
                {
                    Id = id, TypeId = typeId,
                    X = SettlementFootprint.CenterOf(rep.i), Y = SettlementFootprint.CenterOf(rep.j),
                    Cells = cells == null ? null : SettlementFootprint.Encode(list),
                });
            }
            floor.SettlementParams = new SettlementParams
            {
                Size = SettlementSize.Small, ActiveBuildings = 99, HasWall = true,
                StreetCells = streets == null ? null
                    : SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)>(streets)),
            };
            var data = new InteriorData { OwnerPoiId = "poi-validation", Kind = InteriorKind.Settlement };
            data.Floors.Add(floor);
            return data;
        }

        [ContextMenu("Self-Test: Settlement Validation")]
        public void SelfTestSettlementValidation()
        {
            bool ok = true;

            // ---- 1. A GENERATED town is clean, at every size, over several seeds ------------------------
            // This is what the old version of this test claimed for one seed, and it USED to be true for a
            // reason that has now gone: the validator simply had no settlement rules. It is a real claim now
            // — four footprint rules evaluated against every building of every town — so it is made over a
            // spread of seeds and all three size classes rather than one Small/seed-7 town.
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            foreach (var size in sizes)
                foreach (int seed in new[] { 1, 7, 23 })
                {
                    var town = SettlementGenerator.Generate(new WorldGen.Generation.SettlementConfig
                    { Seed = seed, Size = size, ActiveBuildings = 5, HasWall = true }, "poi-city");
                    foreach (var iss in DungeonValidator.Validate(town))
                    { Debug.LogError($"FAIL settlement-validation: a freshly generated {size} town (seed {seed}) reported '{iss.Message}'"); ok = false; }
                }
            // …and a WALL-LESS village, which takes none of the layout's gates and so exercises a floor with
            // TypeId-1 rooms only.
            var village = SettlementGenerator.Generate(new WorldGen.Generation.SettlementConfig
            { Seed = 4, Size = SettlementSize.Small, ActiveBuildings = 3, HasWall = false }, "poi-village");
            foreach (var iss in DungeonValidator.Validate(village))
            { Debug.LogError($"FAIL settlement-validation: a wall-less village reported '{iss.Message}'"); ok = false; }

            // The DUNGEON rules stay gated OFF: a walled town has 2-4 gates and no boss, which under the
            // dungeon rules would wrongly read as «должен быть ровно один вход» + «нет комнаты босса». Part 1
            // above already fails on ANY issue, so this is the same claim narrowed to a nameable cause — it
            // survives even if a later task legitimately adds a settlement issue to some generated town.
            var walled = SettlementGenerator.Generate(new WorldGen.Generation.SettlementConfig
            { Seed = 7, Size = SettlementSize.Small, ActiveBuildings = 5, HasWall = true }, "poi-city");
            foreach (var iss in DungeonValidator.Validate(walled))
                if (iss.Message.Contains("вход") || iss.Message.Contains("босс") || iss.Message.Contains("лестниц"))
                { Debug.LogError($"FAIL settlement-validation: a dungeon/building rule leaked into a settlement: '{iss.Message}'"); ok = false; }

            // ---- 2. each rule FIRES, one at a time, and names the exact offender ------------------------
            // Both helpers are LOCAL functions rather than file-level ones, and that is a hard requirement of
            // the mutant harness, not a style choice: sync.ps1's rebind lifts THIS METHOD's text and retypes
            // DungeonValidator/DungeonIssue/IssueSeverity inside it. A helper declared outside would keep the
            // REAL DungeonIssue in its signature while the rebound body passes it the mutant's, which is a
            // compile error rather than a failing assertion.
            int Count(System.Collections.Generic.List<DungeonIssue> list, IssueSeverity sev, string needle)
            {
                int n = 0;
                foreach (var iss in list) if (iss.Severity == sev && iss.Message.Contains(needle)) n++;
                return n;
            }
            // Every issue on one line, so an assertion can show what WAS reported when the expected issue
            // was not.
            string Join(System.Collections.Generic.List<DungeonIssue> list)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var iss in list)
                {
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(iss.Severity).Append(": ").Append(iss.Message);
                }
                return sb.Length == 0 ? "no issues" : sb.ToString();
            }

            // (a) EMPTY footprint. Room 1 carries no Cells at all. Read through SettlementTileGrid.FootprintOf
            // this could never fail — rule (a) there substitutes the room's point cell — so this assertion is
            // also what pins that the rule reads the STORED array.
            var emptyFp = TownOf(new[] { (1, 1, ((int, int)[])null), (1, 2, new[] { (6, 5) }) }, null);
            var emptyIssues = DungeonValidator.Validate(emptyFp);
            if (Count(emptyIssues, IssueSeverity.Error, "у здания 1 нет ни одной клетки") != 1)
            { Debug.LogError($"FAIL settlement-validation: a building with NO Cells produced {emptyIssues.Count} issue(s), none naming «у здания 1 нет ни одной клетки»: [{Join(emptyIssues)}]"); ok = false; }

            // (b) DISCONNECTED footprint: two cells that touch at nothing. The message must name BOTH cells,
            // not just a count — a DM cannot repair "the footprint is broken".
            var splitFp = TownOf(new[] { (1, 3, new[] { (5, 5), (9, 9) }) }, null);
            var splitIssues = DungeonValidator.Validate(splitFp);
            if (Count(splitIssues, IssueSeverity.Error, "здания 3 распадается") != 1
                || Count(splitIssues, IssueSeverity.Error, "(5, 5) (9, 9)") != 1)
            { Debug.LogError($"FAIL settlement-validation: a 2-island footprint on room 3 did not produce one Error naming both cells «(5, 5) (9, 9)»: [{Join(splitIssues)}]"); ok = false; }
            // A 4-connected L on the SAME two-cell-count shape must NOT fire — otherwise the rule is just
            // "more than one cell is bad".
            var lFp = TownOf(new[] { (1, 3, new[] { (5, 5), (5, 6), (6, 6) }) }, null);
            if (DungeonValidator.Validate(lFp).Count != 0)
            { Debug.LogError($"FAIL settlement-validation: a legal 4-connected L-shaped footprint was reported: [{Join(DungeonValidator.Validate(lFp))}]"); ok = false; }

            // (c) OVERLAP — the data-side twin of SettlementVolumeRenderer.AreCellsFree, and the rule this
            // task exists to add. Rooms 10 and 11 are 2-cell bars sharing exactly cell (7,5); each also has a
            // cell of its own, so this is a PARTIAL overlap (a whole-footprint duplicate would also pass a
            // rule that only compared representative cells).
            var overlap = TownOf(new[] {
                (1, 10, new[] { (6, 5), (7, 5) }),
                (1, 11, new[] { (7, 5), (8, 5) }) }, null);
            var overlapIssues = DungeonValidator.Validate(overlap);
            if (Count(overlapIssues, IssueSeverity.Error, "здания 10 и 11 занимают одну клетку (7, 5)") != 1)
            { Debug.LogError($"FAIL settlement-validation: rooms 10/11 sharing cell (7,5) did not produce exactly one Error naming «здания 10 и 11 занимают одну клетку (7, 5)»: [{Join(overlapIssues)}]"); ok = false; }
            // …and the SHARED cell is the only one reported: (6,5) and (8,5) belong to one room each.
            if (Count(overlapIssues, IssueSeverity.Error, "занимают одну клетку") != 1)
            { Debug.LogError($"FAIL settlement-validation: a single shared cell produced {Count(overlapIssues, IssueSeverity.Error, "занимают одну клетку")} overlap Errors, want exactly 1 — an unshared cell is being reported too: [{Join(overlapIssues)}]"); ok = false; }
            // NEGATIVE CONTROL for the same fixture shifted apart by one cell: no overlap, no issue at all.
            var apart = TownOf(new[] {
                (1, 10, new[] { (6, 5), (7, 5) }),
                (1, 11, new[] { (8, 5), (9, 5) }) }, null);
            if (DungeonValidator.Validate(apart).Count != 0)
            { Debug.LogError($"FAIL settlement-validation: two FLUSH but disjoint 2-cell buildings were reported — adjacency is legal: [{Join(DungeonValidator.Validate(apart))}]"); ok = false; }

            // (d) A BUILDING STANDING ON A STREET — Warning, not Error, and the cell is named. This is the one
            // of the four a DM reaches by ordinary dragging (a stored street cell is owned by no room, so the
            // drag verdict AreCellsFree does not refuse it).
            var onStreet = TownOf(new[] { (1, 20, new[] { (6, 5), (6, 6) }) }, new[] { (6, 6), (6, 7) });
            var streetIssues = DungeonValidator.Validate(onStreet);
            if (Count(streetIssues, IssueSeverity.Warning, "здание 20 стоит на улице — клетка (6, 6)") != 1)
            { Debug.LogError($"FAIL settlement-validation: a building on street cell (6,6) did not produce exactly one Warning naming it: [{Join(streetIssues)}]"); ok = false; }
            if (Count(streetIssues, IssueSeverity.Error, "стоит на улице") != 0)
            { Debug.LogError($"FAIL settlement-validation: standing on a street was reported as an Error, want a Warning: [{Join(streetIssues)}]"); ok = false; }
            // Building 20 spans (6,5) and (6,6); only (6,6) is a street cell, so exactly ONE Warning is due.
            // A rule that reported the whole building rather than the offending cell would raise two.
            if (Count(streetIssues, IssueSeverity.Warning, "стоит на улице") != 1)
            { Debug.LogError($"FAIL settlement-validation: building 20 has ONE cell on a street ((6,6); (6,5) is not one) yet {Count(streetIssues, IssueSeverity.Warning, "стоит на улице")} street Warning(s) were raised, want exactly 1: [{Join(streetIssues)}]"); ok = false; }

            // ---- 3. THE GATE SCOPE (the reason these rules are TypeId==1 only) -------------------------
            // A gate's own cell IS a street cell by construction — SettlementBlocks.PlaceGateCells picks it
            // off the ring street. Applied to gates, rule (d) would fire on every gate of every town. This
            // fixture is exactly that shape: gate 30 sits on street cell (4,4).
            var gateOnStreet = TownOf(new[] {
                (0, 30, new[] { (4, 4) }),
                (1, 31, new[] { (6, 5) }) }, new[] { (4, 4), (5, 4) });
            var gateIssues = DungeonValidator.Validate(gateOnStreet);
            if (gateIssues.Count != 0)
            { Debug.LogError($"FAIL settlement-validation: a GATE on its own ring-street cell was reported — the footprint rules must be buildings-only: [{Join(gateIssues)}]"); ok = false; }
            // A gate with a BROKEN footprint (empty, and disconnected) is likewise out of scope.
            var brokenGates = TownOf(new[] {
                (0, 32, ((int, int)[])null),
                (0, 33, new[] { (2, 2), (8, 8) }),
                (1, 34, new[] { (6, 5) }) }, null);
            if (DungeonValidator.Validate(brokenGates).Count != 0)
            { Debug.LogError($"FAIL settlement-validation: a gate with an empty/disconnected footprint was reported — buildings-only scope broken: [{Join(DungeonValidator.Validate(brokenGates))}]"); ok = false; }

            // ---- 4. THE RULES ARE SETTLEMENT-ONLY ------------------------------------------------------
            // The identical broken data under Kind == Building must raise none of these four — a building
            // interior's TypeId-1 rooms are ROOMS, not footprints on the settlement lattice, and its own
            // rules are untouched by this task. Kind is flipped on the SAME fixture so the only difference is
            // the Kind itself.
            var asBuilding = TownOf(new[] {
                (1, 10, new[] { (6, 5), (7, 5) }),
                (1, 11, new[] { (7, 5), (8, 5) }) }, null);
            asBuilding.Kind = InteriorKind.Building;
            foreach (var iss in DungeonValidator.Validate(asBuilding))
                if (iss.Message.Contains("клетк") || iss.Message.Contains("улиц"))
                { Debug.LogError($"FAIL settlement-validation: a settlement footprint rule leaked into a BUILDING interior: '{iss.Message}'"); ok = false; }

            if (ok) Debug.Log("Settlement Validation: PASS");
        }

        [ContextMenu("Self-Test: Settlement Gate Opening")]
        public void SelfTestGateOpening()
        {
            bool ok = true;

            // THE GATE-OPENING PROPERTY, RE-VERIFIED WHERE IT NOW LIVES. "The wall opens at every gate" was
            // pinned on SettlementFence's synthetic fixtures (SelfTestFence C/E), where a gate is rasterized
            // as a bare point that the traced fence bulges out to hug. That is still true OF THAT MODULE, but
            // it is no longer how a town the DM sees opens its wall, and the street rework is what moved it:
            //
            //   • a gate is now a RING-STREET CELL (SettlementBlocks.PlaceGateCells), sitting inside the
            //     outermost street lane rather than out on a spur, and an arterial reaches the ring exactly
            //     there;
            //   • a settlement is drawn by SettlementVolumeRenderer, which builds SettlementTileGrid and
            //     never calls DungeonLayout.DeriveTownFence at all. The wall the DM sees is the grid's Wall
            //     ring and the opening is a Gate TILE, produced by SettlementTileGrid's gate pass, which
            //     retargets the ring cell NEAREST the gate room.
            //
            // So the property to hold is a property of the TILE GRID, and it is asserted here on GENERATED
            // towns rather than on a hand-built fixture (SelfTestRoadsAndGates already pins the gate pass on
            // one) — a topology change is exactly the kind of thing a fixture cannot notice.
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            foreach (var size in sizes)
                foreach (int seed in new[] { 1, 7, 23, 30 })
                {
                    var town = SettlementGenerator.Generate(new WorldGen.Generation.SettlementConfig
                    { Seed = seed, Size = size, ActiveBuildings = 5, HasWall = true }, "poi-gates");
                    var floor = town.Floors[0];
                    var grid = SettlementTileGrid.Build(floor);

                    int gateRooms = 0;
                    foreach (var r in floor.Rooms) if (r.TypeId == 0) gateRooms++;
                    int gateTiles = 0;
                    for (int a = 0; a < grid.W; a++)
                        for (int b = 0; b < grid.H; b++)
                            if (grid.Cells[a, b] == TileType.Gate) gateTiles++;

                    // ONE OPENING PER GATE, EXACTLY — and this is DELIBERATELY STRICTER THAN THE RULE IT
                    // TESTS, so read this before "fixing" a failure. SettlementTileGrid.MarkGates' own doc
                    // permits two gate rooms to collapse onto one ring cell ("at this resolution, two gates
                    // sharing one cell genuinely ARE one opening") — that is the price of making the search
                    // idempotent by accepting Gate as well as Wall as a candidate.
                    //
                    // The strict form is kept anyway, as a CANARY on gate separation. Collapse is unreachable
                    // for a generated town: SettlementBlocks.MinGateSeparationCells keeps gate cells >= 3
                    // apart in Chebyshev, and this was measured at 0 collapses over 120 towns across all
                    // three size classes. So if this ever fires, the thing that changed is the ring geometry
                    // or the gate spacing, not the gate pass — and that is exactly what a fixed-seed test
                    // should surface. THE CORRECT RESPONSE TO A FAILURE HERE IS TO RELAX THIS TO >= 1 per
                    // gate (keeping the counts in the message), not to change MarkGates.
                    //
                    // The count also fails at 0 tiles, which is what catches the gate pass being neutered
                    // outright (MutGateOpeningNoGates).
                    if (gateTiles != gateRooms)
                    { Debug.LogError($"FAIL gate-opening: {size} seed {seed} has {gateRooms} gate room(s) but {gateTiles} Gate tile(s) — the wall does not open once per gate"); ok = false; }

                    // AND EACH OPENING IS THE GATE'S OWN. A count alone would pass if every gate in town
                    // opened the ring on the far side.
                    //
                    // THE BOUND IS MEASURED, NOT DERIVED, AND IT HAS ZERO HEADROOM — stated plainly because
                    // an earlier draft of this comment claimed a derivation, and the derivation does not
                    // actually reach 3. BuildWallRing dilates the seed (buildings UNION streets, so a gate's
                    // own ring-street cell is IN it) by CourtyardCells + 1 = 2 and writes Wall to the
                    // outermost layer of the resulting inside region, which puts the ring 2 cells from a seed
                    // cell — not 3. The measured worst case over 120 generated towns, all three size classes,
                    // is exactly 3, so the extra cell comes from ring geometry the dilation argument does not
                    // capture (a gate cell that is not itself on the blob's outer edge in the direction of
                    // its nearest Wall).
                    //
                    // SettlementTileGrid.MarginCells (= CourtyardCells + 2 = 3) is used as the bound because
                    // it moves with CourtyardCells, not because it is the derivation's answer. Zero headroom
                    // means this WILL fire on a legitimate retune of the courtyard or the ring; re-measure and
                    // re-state the number then rather than padding it now — a padded bound would stop saying
                    // anything about where the opening is.
                    foreach (var r in floor.Rooms)
                    {
                        if (r.TypeId != 0) continue;
                        var cell = SettlementFootprint.Representative(SettlementTileGrid.FootprintOf(r));
                        int best = int.MaxValue;
                        for (int a = 0; a < grid.W; a++)
                            for (int b = 0; b < grid.H; b++)
                            {
                                if (grid.Cells[a, b] != TileType.Gate) continue;
                                int di = System.Math.Abs(a + grid.OriginI - cell.i);
                                int dj = System.Math.Abs(b + grid.OriginJ - cell.j);
                                int cheb = di > dj ? di : dj;
                                if (cheb < best) best = cheb;
                            }
                        if (best > SettlementTileGrid.MarginCells)
                        {
                            // "none at all" is reported as such rather than as int.MaxValue cells away — the
                            // two are different defects (the wall opened nowhere vs. it opened in the wrong
                            // place) and the message a DM or a mutant report shows must say which.
                            string howFar = best == int.MaxValue ? "there is NO Gate tile anywhere in the grid"
                                : $"its nearest Gate tile is {best} cells away (Chebyshev), want <= {SettlementTileGrid.MarginCells}";
                            Debug.LogError($"FAIL gate-opening: {size} seed {seed} gate room {r.Id} at cell ({cell.i},{cell.j}) — {howFar}; the wall did not open at this gate");
                            ok = false;
                        }
                    }

                    // THE STREETS ARE INSIDE THE TOWN. Every stored street cell must be represented in the
                    // grid AND classified — Road, or Building where a footprint claims the same cell. A cell
                    // that came back None (or out of bounds, which At() reports as None) is a street the wall
                    // never wrapped, the exact defect the fine-fence arc existed to close. This is the half of
                    // the fence brief's "the fence encloses every street cell" that is TRUE of the town the DM
                    // sees; the DERIVED vector fence does not enclose them — see the report.
                    foreach (var c in SettlementFootprint.Decode(floor.SettlementParams?.StreetCells))
                    {
                        var t = grid.At(c.i, c.j);
                        if (t != TileType.Road && t != TileType.Building)
                        { Debug.LogError($"FAIL gate-opening: {size} seed {seed} street cell ({c.i},{c.j}) is {t}, want Road (or Building where a house claims it) — the wall ring did not wrap it"); ok = false; }
                    }
                }

            if (ok) Debug.Log("Settlement Gate Opening: PASS");
        }

        /// <summary>A gate is where the DM sees it (DM finding ·10). Two independent claims:
        ///   (1) the drawn Gate tile resolves to the gate ROOM through SettlementTileGrid.GateRoomAt — the
        ///       tile and the room are 2-4 cells apart, so before this the click landed on nothing;
        ///   (2) that mapping AGREES with a nearest-wall-cell search run the same way MarkGates runs it, so a
        ///       drag that snaps to "the nearest wall cell" cannot land somewhere MarkGates would not redraw.
        /// Claim (2) is asserted here rather than in the renderer because the renderer needs a Unity Canvas and
        /// this suite does not: the search is replicated verbatim below and pinned against GateRoomAt, so a
        /// future edit to either rule breaks this test rather than the DM's click.</summary>
        [ContextMenu("Self-Test: Gate Handles")]
        public void SelfTestGateHandles()
        {
            bool ok = true;
            var sizes = new[] { SettlementSize.Small, SettlementSize.Medium, SettlementSize.Large };
            int gatesChecked = 0;

            foreach (var size in sizes)
                for (int k = 0; k < 20; k++)
                {
                    int seed = 1000 + k;
                    var cfg = new SettlementConfig { Seed = seed, Size = size, ActiveBuildings = 1, HasWall = true };
                    var floor = SettlementGenerator.Generate(cfg, "poi").Floors[0];
                    var g = SettlementTileGrid.Build(floor);

                    foreach (var r in floor.Rooms)
                    {
                        if (r.TypeId != 0) continue;
                        gatesChecked++;

                        bool found = false;
                        for (int a = 0; a < g.W && !found; a++)
                            for (int b = 0; b < g.H && !found; b++)
                            {
                                if (g.Cells[a, b] != TileType.Gate) continue;
                                long key = SettlementTileGrid.DepthKey(a + g.OriginI, b + g.OriginJ);
                                if (g.GateRoomAt.TryGetValue(key, out int id) && id == r.Id) found = true;
                            }
                        if (!found)
                        {
                            Debug.LogError($"SelfTestGateHandles: {size} seed {seed}: gate room {r.Id} owns no "
                                         + "drawn Gate cell — a click on the visible gate selects nothing");
                            ok = false;
                            continue;
                        }

                        // The same search TryNearestWallCell performs, on the gate's own stored cell.
                        var fp = SettlementTileGrid.FootprintOf(r);
                        if (fp.Count == 0) { Debug.LogError($"SelfTestGateHandles: gate room {r.Id} has no cell"); ok = false; continue; }
                        int bestA = -1, bestB = -1; float bestD2 = float.MaxValue;
                        for (int a = 0; a < g.W; a++)
                            for (int b = 0; b < g.H; b++)
                            {
                                if (g.Cells[a, b] != TileType.Wall && g.Cells[a, b] != TileType.Gate) continue;
                                float dx = g.CenterX(a + g.OriginI) - g.CenterX(fp[0].i);
                                float dy = g.CenterY(b + g.OriginJ) - g.CenterY(fp[0].j);
                                float d2 = dx * dx + dy * dy;
                                if (d2 < bestD2) { bestD2 = d2; bestA = a; bestB = b; }
                            }
                        long nearestKey = SettlementTileGrid.DepthKey(bestA + g.OriginI, bestB + g.OriginJ);
                        if (!g.GateRoomAt.TryGetValue(nearestKey, out int owner) || owner != r.Id)
                        {
                            Debug.LogError($"SelfTestGateHandles: {size} seed {seed}: nearest wall cell to gate "
                                         + $"room {r.Id} is array cell ({bestA},{bestB}), which GateRoomAt does "
                                         + "not attribute to that room — the drag snap and MarkGates disagree");
                            ok = false;
                        }
                    }
                }

            if (gatesChecked < 60)
            {
                Debug.LogError($"SelfTestGateHandles: only {gatesChecked} gates checked across 60 towns — "
                             + "expected at least 60");
                ok = false;
            }

            if (ok) Debug.Log("Self-Test Gate Handles: PASS");
        }

        [ContextMenu("Self-Test: Settlement Sentinel")]
        public void SelfTestSettlementSentinel()
        {
            // Trailing non-reboundable sentinel, matching PoiMigrationSelfTests: the mutant rebind extracts a
            // method body by scanning forward for the NEXT attribute marker and throws when there is none, so
            // a mutant-reboundable test must never be a file's last one. Asserts nothing.
            Debug.Log("Settlement Sentinel: PASS");
        }
    }
}
