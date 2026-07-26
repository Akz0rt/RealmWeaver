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
            var cfg = new SettlementConfig { Seed = 3, TargetBuildings = 40, HasWall = true };
            var wall = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(cfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);

            // ---- 1. The notional wall contour is a non-null, sane contour --------------------------------
            if (wall == null || !wall.IsClosedSane())
            { Debug.LogError("FAIL gates: notional wall contour null/insane"); ok = false; }

            // ---- 3. PlaceGates honours the requested count, and GateCountFor is in 2..4 ----------------
            int want = SettlementGenerator.GateCountFor(cfg.TargetBuildings);
            var gates = SettlementGenerator.PlaceGates(wall, want, cfg.Seed);
            if (gates.Count != want)
            { Debug.LogError($"FAIL gates: asked for {want} gates, placed {gates.Count}"); ok = false; }
            if (want < 2 || want > 4)
            { Debug.LogError($"FAIL gates: GateCountFor({cfg.TargetBuildings}) = {want}, want 2..4"); ok = false; }

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

            // ---- 7. In an ACTUAL generated floor, gates hug the PRELIMINARY fence derived from its OWN
            // buildings (Ц2.6: BuildFloor no longer places gates on the raw notional wall above — it derives
            // a fence from the placed buildings first, then spaces gates on THAT). Independently re-derive
            // the same building fence from the floor's TypeId-1 rooms and assert every TypeId-0 gate sits
            // within ~1.5 normalized tiles (converted via ÷T) of it. MutGateAtCentre collapses every gate to
            // the town centre, which sits far from this fence's boundary — caught here.
            var floor = SettlementGenerator.BuildFloor(cfg);
            const float T = DungeonLayout.TilesPerAxis;
            var bNodes = new System.Collections.Generic.List<LinkNode>();
            foreach (var r in floor.Rooms)
                if (r.TypeId == 1)
                    bNodes.Add(new LinkNode { Id = r.Id, CX = r.X * T, CY = r.Y * T, W = SettlementGenerator.NominalBuildingTiles, H = SettlementGenerator.NominalBuildingTiles });
            var fenceTile = SettlementFence.Derive(bNodes, new System.Collections.Generic.List<LinkNode>(), new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
            if (fenceTile == null || !fenceTile.IsClosedSane())
            { Debug.LogError("FAIL gates: the building fence re-derived from the generated floor's rooms is null/insane"); ok = false; }
            else
            {
                var fenceNorm = new WallContour();
                foreach (var p in fenceTile.Points)
                    fenceNorm.Points.Add(new WallPoint { X = p.X / T, Y = p.Y / T });
                float tol = 1.5f / T;
                foreach (var r in floor.Rooms)
                    if (r.TypeId == 0)
                    {
                        float d = fenceNorm.DistanceToEdge(r.X, r.Y);
                        if (d > tol)
                        { Debug.LogError($"FAIL gates: gate room {r.Id} at ({r.X:F3},{r.Y:F3}) is {d * T:F2} tiles from the derived building fence, want <=1.5"); ok = false; }
                    }
            }

            if (ok) Debug.Log("Settlement Gates: PASS");
        }

        [ContextMenu("Self-Test: Settlement Buildings")]
        public void SelfTestBuildings()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 11, TargetBuildings = 40, HasWall = true };
            var wall = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(cfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var buildings = SettlementGenerator.PlaceBuildings(wall, cfg.Seed, cfg.TargetBuildings);

            // ---- 1. EVERY building centre is inside the wall -------------------------------------------
            // Drop the Contains filter and cells outside the rounded contour leak in at the bbox corners.
            foreach (var b in buildings)
                if (!wall.Contains(b.X, b.Y))
                { Debug.LogError($"FAIL buildings: building at ({b.X:F3},{b.Y:F3}) is OUTSIDE the wall"); ok = false; break; }

            // ---- 2. NO two buildings are closer than one cell — the anti-overlap guarantee -------------
            // This is the exact defect that disqualified the dungeon packer (18–48 overlapping pairs at 40).
            for (int i = 0; i < buildings.Count && ok; i++)
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    float dx = buildings[i].X - buildings[j].X, dy = buildings[i].Y - buildings[j].Y;
                    if (dx * dx + dy * dy < SettlementGenerator.BuildingCell * SettlementGenerator.BuildingCell * 0.9f)
                    { Debug.LogError($"FAIL buildings: buildings {i} and {j} overlap (closer than a cell)"); ok = false; break; }
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
            if (buildings.Count > cfg.TargetBuildings)
            { Debug.LogError($"FAIL buildings: produced {buildings.Count}, more than the {cfg.TargetBuildings} target"); ok = false; }

            // ---- 5. Determinism ------------------------------------------------------------------------
            var b2 = SettlementGenerator.PlaceBuildings(wall, cfg.Seed, cfg.TargetBuildings);
            if (b2.Count != buildings.Count || (buildings.Count > 0 && (b2[0].X != buildings[0].X || b2[0].Y != buildings[0].Y)))
            { Debug.LogError("FAIL buildings: two seed-11 placements differ — not deterministic"); ok = false; }

            if (ok) Debug.Log("Settlement Buildings: PASS");
        }

        [ContextMenu("Self-Test: Settlement Streets")]
        public void SelfTestStreets()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 5, TargetBuildings = 40, HasWall = true };
            var wall = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(cfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var gates = SettlementGenerator.PlaceGates(wall, SettlementGenerator.GateCountFor(cfg.TargetBuildings), cfg.Seed);
            var buildings = SettlementGenerator.PlaceBuildings(wall, cfg.Seed, cfg.TargetBuildings);
            var edges = SettlementStreets.GenerateStreets(wall, buildings, gates, cfg.Seed);

            int nGates = gates.Count, nNodes = gates.Count + buildings.Count;

            // ---- 1. Edges reference valid, distinct node indices ---------------------------------------
            foreach (var e in edges)
                if (e.A < 0 || e.B < 0 || e.A >= nNodes || e.B >= nNodes || e.A == e.B)
                { Debug.LogError($"FAIL streets: edge ({e.A},{e.B}) is out of range or a self-loop (nNodes={nNodes})"); ok = false; break; }

            // ---- 2. EVERY building is reachable from SOME gate (connected graph) ------------------------
            // Drop the spanning growth and isolated buildings appear; this BFS from all gates names the
            // first unreachable building.
            var adj = new System.Collections.Generic.List<int>[nNodes];
            for (int i = 0; i < nNodes; i++) adj[i] = new System.Collections.Generic.List<int>();
            foreach (var e in edges) { adj[e.A].Add(e.B); adj[e.B].Add(e.A); }
            var seen = new bool[nNodes];
            var q = new System.Collections.Generic.Queue<int>();
            for (int g = 0; g < nGates; g++) { seen[g] = true; q.Enqueue(g); }
            while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in adj[u]) if (!seen[v]) { seen[v] = true; q.Enqueue(v); } }
            for (int b = 0; b < buildings.Count; b++)
                if (!seen[nGates + b])
                { Debug.LogError($"FAIL streets: building index {b} (node {nGates + b}) is unreachable from any gate"); ok = false; break; }

            // ---- 3. Determinism ------------------------------------------------------------------------
            var edges2 = SettlementStreets.GenerateStreets(wall, buildings, gates, cfg.Seed);
            if (edges2.Count != edges.Count)
            { Debug.LogError($"FAIL streets: seed-5 rerun produced {edges2.Count} edges vs {edges.Count} — not deterministic"); ok = false; }
            else
                for (int i = 0; i < edges.Count; i++)
                    if (edges2[i].A != edges[i].A || edges2[i].B != edges[i].B)
                    { Debug.LogError($"FAIL streets: seed-5 rerun edge {i} is ({edges2[i].A},{edges2[i].B}) vs ({edges[i].A},{edges[i].B}) — not deterministic"); ok = false; break; }

            // ---- 4. Perf threshold: 80 buildings route in well under a frame ----------------------------
            // The whole reason this stage exists instead of BuildRenderGraph(Clean), which took 20–34 s at 60.
            var bigCfg = new SettlementConfig { Seed = 9, TargetBuildings = 80, HasWall = true };
            var bw = WallContour.Rounded(bigCfg.Seed, 0.5f, 0.5f, SettlementGenerator.WallRadiusFor(bigCfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var bg = SettlementGenerator.PlaceGates(bw, SettlementGenerator.GateCountFor(bigCfg.TargetBuildings), bigCfg.Seed);
            var bb = SettlementGenerator.PlaceBuildings(bw, bigCfg.Seed, bigCfg.TargetBuildings);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SettlementStreets.GenerateStreets(bw, bb, bg, bigCfg.Seed);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 50)
            { Debug.LogError($"FAIL streets: routing 80 buildings took {sw.ElapsedMilliseconds} ms, want <50"); ok = false; }

            // ---- Ц1.6: gate-gate arterials — every gate on one connected arterial net, emitted FIRST --
            // (a) Collect gate-gate edges and where they sit in the list.
            int lastArterial = -1, firstBranch = int.MaxValue;
            var gateAdj = new System.Collections.Generic.List<int>[nGates];
            for (int i = 0; i < nGates; i++) gateAdj[i] = new System.Collections.Generic.List<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                bool arterial = edges[i].A < nGates && edges[i].B < nGates;
                if (arterial) { lastArterial = i; gateAdj[edges[i].A].Add(edges[i].B); gateAdj[edges[i].B].Add(edges[i].A); }
                else if (i < firstBranch) firstBranch = i;
            }
            // (b) Ordering contract: arterials strictly precede branches (the road router routes in input
            // order; a branch routed before an arterial would claim the lane the arterial should own).
            if (lastArterial >= 0 && firstBranch < lastArterial)
            { Debug.LogError($"FAIL streets: arterial at index {lastArterial} comes after branch at {firstBranch} — ordering contract broken"); ok = false; }
            // (c) The arterial subgraph spans ALL gates (connected, every gate in it) when nGates > 1.
            if (nGates > 1)
            {
                var seenGate = new bool[nGates];
                var stack = new System.Collections.Generic.Stack<int>(); stack.Push(0); seenGate[0] = true; int seenCount = 1;
                while (stack.Count > 0)
                    foreach (int nb in gateAdj[stack.Pop()])
                        if (!seenGate[nb]) { seenGate[nb] = true; seenCount++; stack.Push(nb); }
                if (seenCount != nGates)
                { Debug.LogError($"FAIL streets: arterial net spans {seenCount} of {nGates} gates — some gate has no арterial"); ok = false; }
            }

            if (ok) Debug.Log("Settlement Streets: PASS");
        }

        [ContextMenu("Self-Test: Settlement Assembly")]
        public void SelfTestAssembly()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 2, TargetBuildings = 40, HasWall = true };
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
            if (buildNodes < 20)
            { Debug.LogError($"FAIL assembly: {buildNodes} building nodes, want ≥20 for a 40-target town"); ok = false; }

            // ---- 3. Links map StreetEdge indices to the RIGHT room ids (the load-bearing invariant) -----
            // Reconstruct the exact placement/buildings/gates/edges BuildFloor used (all deterministic from
            // cfg alone — there is no stored wall), then verify (a) rooms were created in
            // gates-then-buildings order — the room at combined index i carries node i's position and type —
            // and (b) every street edge {A,B} became a link between room ids A+1 and B+1. A reversed or
            // scrambled index→id mapping (the "every street links the wrong pair" bug) fails here; a
            // ContainsKey check could not, because idByIndex is a bijection. Gates are re-derived by the SAME
            // preliminary-fence-then-space steps BuildFloor itself runs (byte-identical: same NominalBuildingTiles,
            // empty gates/roads, FenceMarginTiles, ÷T, GateCountFor, seed) — recomputed from primitives here,
            // never read back off `floor`, so this stays an independent check of the assembly wiring.
            var exPlacement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f,
                SettlementGenerator.WallRadiusFor(cfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var exBuildings = SettlementGenerator.PlaceBuildings(exPlacement, cfg.Seed, cfg.TargetBuildings);
            var exGates = new System.Collections.Generic.List<GatePoint>();
            if (exBuildings.Count > 0)
            {
                const float T = DungeonLayout.TilesPerAxis;
                var bNodes = new System.Collections.Generic.List<LinkNode>(exBuildings.Count);
                for (int i = 0; i < exBuildings.Count; i++)
                    bNodes.Add(new LinkNode { Id = i, CX = exBuildings[i].X * T, CY = exBuildings[i].Y * T, W = SettlementGenerator.NominalBuildingTiles, H = SettlementGenerator.NominalBuildingTiles });
                var prelimTile = SettlementFence.Derive(bNodes, new System.Collections.Generic.List<LinkNode>(), new System.Collections.Generic.List<LinkSegment>(), SettlementFence.FenceMarginTiles);
                if (prelimTile != null)
                {
                    var prelimNorm = new WallContour();
                    foreach (var p in prelimTile.Points)
                        prelimNorm.Points.Add(new WallPoint { X = p.X / T, Y = p.Y / T });
                    exGates = SettlementGenerator.PlaceGates(prelimNorm, SettlementGenerator.GateCountFor(cfg.TargetBuildings), cfg.Seed);
                }
            }
            var exEdges = SettlementStreets.GenerateStreets(exPlacement, exBuildings, exGates, cfg.Seed);
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
            }
            // (b) each street edge {A,B} → a link between room ids A+1 and B+1 (order-insensitive).
            foreach (var e in exEdges)
            {
                int idA = e.A + 1, idB = e.B + 1;
                bool found = false;
                foreach (var l in floor.Links)
                    if ((l.RoomA == idA && l.RoomB == idB) || (l.RoomA == idB && l.RoomB == idA)) { found = true; break; }
                if (!found)
                { Debug.LogError($"FAIL assembly: street edge ({e.A},{e.B}) has no link between room ids {idA} and {idB}"); ok = false; break; }
            }
            if (floor.Links.Count != exEdges.Count)
            { Debug.LogError($"FAIL assembly: {floor.Links.Count} links vs {exEdges.Count} street edges"); ok = false; }

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
            var cfg = new SettlementConfig { Seed = 4, TargetBuildings = 40, HasWall = false };
            var data = SettlementGenerator.Generate(cfg, "poi-village");
            var floor = data.Floors[0];

            // ---- 1. NO gate rooms; a substantial building count is STILL placed ------------------------
            // (InteriorFloor.Wall was removed — a wall-less village has no perimeter to store; "no wall" is now
            // structural, so what remains to check is that a HasWall=false town produces zero gate rooms below.)
            // This is the bug under fix: today BuildFloor derives placement from the (null) wall, so a
            // village yields 0 buildings, 0 gates, 0 streets — a completely empty map.
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
            if (buildNodes < 20)
            { Debug.LogError($"FAIL village: a village produced only {buildNodes} buildings, want >=20"); ok = false; }

            // ---- 3. Hub connectivity: every building is reachable from the hub, and floor.Links agrees ---
            // Re-derive the SAME notional placement contour BuildFloor now falls back to when HasWall=false
            // (identical seed/radius formula, never stored), then call SettlementStreets.GenerateStreets
            // DIRECTLY — this is what makes a hub-seeding regression (MutStreetsNoHub) observable through
            // this self-test, exactly like SelfTestStreets does for the gated path (Generate/BuildFloor's
            // OWN internal call always binds to the real, un-rebound SettlementStreets, so only a direct
            // call here is mutant-observable). BFS from building 0 must reach every other building; then
            // floor.Links (built by the real BuildFloor) must carry that identical edge set, one-to-one by
            // room id (id = node index + 1 for a gate-less town, since gates.Count == 0).
            var exPlacement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f,
                SettlementGenerator.WallRadiusFor(cfg.TargetBuildings), SettlementGenerator.WallSides, SettlementGenerator.WallJitter);
            var exBuildings = SettlementGenerator.PlaceBuildings(exPlacement, cfg.Seed, cfg.TargetBuildings);
            var exGates = new System.Collections.Generic.List<GatePoint>();
            var exEdges = SettlementStreets.GenerateStreets(exPlacement, exBuildings, exGates, cfg.Seed);

            if (exBuildings.Count > 0)
            {
                var adj = new System.Collections.Generic.List<int>[exBuildings.Count];
                for (int i = 0; i < adj.Length; i++) adj[i] = new System.Collections.Generic.List<int>();
                foreach (var e in exEdges) { adj[e.A].Add(e.B); adj[e.B].Add(e.A); }
                var seen = new bool[exBuildings.Count];
                var q = new System.Collections.Generic.Queue<int>();
                seen[0] = true; q.Enqueue(0);
                while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in adj[u]) if (!seen[v]) { seen[v] = true; q.Enqueue(v); } }
                for (int i = 0; i < exBuildings.Count; i++)
                    if (!seen[i])
                    { Debug.LogError($"FAIL village: building index {i} (room id {i + 1}) is unreachable from the hub"); ok = false; break; }
            }

            // floor.Links must carry the SAME edges (mapped to room ids i+1, order-insensitive) — proves
            // BuildFloor actually wires this GenerateStreets output into the floor for the gate-less path.
            if (floor.Links.Count != exEdges.Count)
            { Debug.LogError($"FAIL village: floor.Links has {floor.Links.Count} links vs {exEdges.Count} street edges"); ok = false; }
            foreach (var e in exEdges)
            {
                int idA = e.A + 1, idB = e.B + 1;
                bool found = false;
                foreach (var l in floor.Links)
                    if ((l.RoomA == idA && l.RoomB == idB) || (l.RoomA == idB && l.RoomB == idA)) { found = true; break; }
                if (!found)
                { Debug.LogError($"FAIL village: street edge ({e.A},{e.B}) has no link between room ids {idA} and {idB}"); ok = false; break; }
            }

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
            var cfg = new SettlementConfig { Seed = 6, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
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
            if (floor.SettlementParams == null || floor.SettlementParams.TargetBuildings != cfg.TargetBuildings
                || floor.SettlementParams.ActiveBuildings != cfg.ActiveBuildings)
            { Debug.LogError("FAIL active: floor.SettlementParams did not store the config's Target/Active"); ok = false; }

            // ---- 3. Determinism: same seed → identical IsDummy per room ---------------------------------
            var f2 = SettlementGenerator.Generate(cfg, "poi-city").Floors[0];
            for (int i = 0; i < floor.Rooms.Count && i < f2.Rooms.Count; i++)
                if (floor.Rooms[i].IsDummy != f2.Rooms[i].IsDummy)
                { Debug.LogError($"FAIL active: room index {i} (id {floor.Rooms[i].Id}) IsDummy differs between two seed-6 runs"); ok = false; break; }

            // ---- 4. A wall-less camp with a tiny active count marks correctly too -----------------------
            var camp = SettlementGenerator.Generate(new SettlementConfig { Seed = 6, TargetBuildings = 5, ActiveBuildings = 2, HasWall = false }, "poi-camp").Floors[0];
            int campPlaced = 0, campActive = 0;
            foreach (var r in camp.Rooms) if (r.TypeId == 1) { campPlaced++; if (!r.IsDummy) campActive++; }
            if (campActive != System.Math.Min(2, campPlaced))
            { Debug.LogError($"FAIL active: camp has {campActive} active, want min(2,{campPlaced})"); ok = false; }

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
            var cfg = new SettlementConfig { Seed = 3, TargetBuildings = 40, HasWall = true };
            var floor = SettlementGenerator.BuildFloor(cfg);
            var fence = DungeonLayout.DeriveTownFence(floor, includeRoads: true);
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
            // band of an endpoint). Mirrors SelfTestRoads' assertion 5. The fixture is well-separated, so the
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

        [ContextMenu("Self-Test: Settlement Roads")]
        public void SelfTestRoads()
        {
            bool ok = true;
            // Field names verified against SettlementConfig (Seed / TargetBuildings / ActiveBuildings /
            // HasWall) and the initializer shape the other settlement tests use.
            // Seed pinned to 1 (kept from the Ц1.7 fixture — still a normal generated+dragged town, no
            // reason to re-pin now the wall obstacle is gone).
            var cfg = new SettlementConfig { Seed = 1, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
            var floor = SettlementGenerator.BuildFloor(cfg);

            void AssertClean(InteriorFloor lvl, string label)
            {
                var nodes = RoadNodes(lvl); var edges = RoadEdges(lvl);

                var g = SettlementRoads.Build(nodes, edges);
                var byId = new System.Collections.Generic.Dictionary<int, LinkNode>();
                foreach (var n in nodes) byId[n.Id] = n;

                // 1. Every segment is axis-aligned (a diagonal = the straight-line fallback fired — on a
                //    freshly generated town that means routing FAILED somewhere).
                foreach (var s in g.Segments)
                    if (System.Math.Abs(s.A.X - s.B.X) > 1e-3f && System.Math.Abs(s.A.Y - s.B.Y) > 1e-3f)
                    { Debug.LogError($"FAIL roads[{label}]: segment ({s.A.X:F1},{s.A.Y:F1})→({s.B.X:F1},{s.B.Y:F1}) is diagonal (fallback fired)"); ok = false; break; }

                // 2. No segment enters ANY building/gate rect interior (shrunk by 0.05), except the two
                //    rects of its OWN edge (the door stubs). THE §2 acceptance: roads go around houses.
                foreach (var s in g.Segments)
                {
                    var link = lvl.Links[s.EdgeIndex];
                    foreach (var n in nodes)
                    {
                        if (n.Id == link.RoomA || n.Id == link.RoomB) continue;
                        float hw = n.W * 0.5f - 0.05f, hh = n.H * 0.5f - 0.05f;
                        // axis-aligned segment vs rect: interval overlap on both axes.
                        float sMinX = System.Math.Min(s.A.X, s.B.X), sMaxX = System.Math.Max(s.A.X, s.B.X);
                        float sMinY = System.Math.Min(s.A.Y, s.B.Y), sMaxY = System.Math.Max(s.A.Y, s.B.Y);
                        if (sMaxX > n.CX - hw && sMinX < n.CX + hw && sMaxY > n.CY - hh && sMinY < n.CY + hh)
                        { Debug.LogError($"FAIL roads[{label}]: edge {s.EdgeIndex} segment crosses node {n.Id} at ({n.CX:F1},{n.CY:F1})"); ok = false; }
                    }
                }

                // 3. Every node has at least one door on its rect boundary («дорога к каждому дому»).
                foreach (var n in nodes)
                {
                    bool has = false;
                    float hw = n.W * 0.5f, hh = n.H * 0.5f;
                    foreach (var d in g.Doors)
                        if (System.Math.Abs(d.X - n.CX) <= hw + 0.1f && System.Math.Abs(d.Y - n.CY) <= hh + 0.1f) { has = true; break; }
                    if (!has) { Debug.LogError($"FAIL roads[{label}]: node {n.Id} has no door — no road reaches it"); ok = false; }
                }

                // 4. Deterministic: an identical re-Build yields identical segments.
                var g2 = SettlementRoads.Build(nodes, edges);
                if (g2.Segments.Count != g.Segments.Count)
                { Debug.LogError($"FAIL roads[{label}]: re-Build produced {g2.Segments.Count} segments vs {g.Segments.Count}"); ok = false; }
                else
                    for (int i = 0; i < g.Segments.Count; i++)
                        if (System.Math.Abs(g.Segments[i].A.X - g2.Segments[i].A.X) > 1e-4f || System.Math.Abs(g.Segments[i].B.Y - g2.Segments[i].B.Y) > 1e-4f)
                        { Debug.LogError($"FAIL roads[{label}]: re-Build segment {i} differs — not deterministic"); ok = false; break; }

                // Buildings vs gates, split by TypeId — shared by assertions 5 and 6 below.
                var buildingIds = new System.Collections.Generic.HashSet<int>();
                var buildingNodes = new System.Collections.Generic.List<LinkNode>();
                var gateNodes = new System.Collections.Generic.List<LinkNode>();
                foreach (var r in lvl.Rooms) if (r.TypeId == 1) buildingIds.Add(r.Id);
                foreach (var n in nodes)
                {
                    if (buildingIds.Contains(n.Id)) buildingNodes.Add(n); else gateNodes.Add(n);
                }

                // 5. Ц2.6: every routed CELL keeps >= RoadClearanceTiles (Chebyshev, matching the square
                //    obstacle-mask inflation) from every BUILDING rect other than its own edge's two
                //    endpoints (which get the own-endpoint carve down to the door on the rect boundary —
                //    a legitimate 0-distance approach). Cell-walk technique borrowed from SelfTestRoadJunctions.
                foreach (var s in g.Segments)
                {
                    var link = lvl.Links[s.EdgeIndex];
                    int ax = (int)System.Math.Round(s.A.X), ay = (int)System.Math.Round(s.A.Y);
                    int bx = (int)System.Math.Round(s.B.X), by = (int)System.Math.Round(s.B.Y);
                    int steps = System.Math.Max(System.Math.Abs(bx - ax), System.Math.Abs(by - ay));
                    for (int i = 0; i <= steps; i++)
                    {
                        int cx = ax + System.Math.Sign(bx - ax) * i, cy = ay + System.Math.Sign(by - ay) * i;
                        foreach (var n in buildingNodes)
                        {
                            if (n.Id == link.RoomA || n.Id == link.RoomB) continue;
                            float dx = System.Math.Max(0f, System.Math.Abs(cx - n.CX) - n.W * 0.5f);
                            float dy = System.Math.Max(0f, System.Math.Abs(cy - n.CY) - n.H * 0.5f);
                            float dist = System.Math.Max(dx, dy);
                            if (dist < SettlementRoads.RoadClearanceTiles - 1e-3f)
                            { Debug.LogError($"FAIL roads[{label}]: edge {s.EdgeIndex} cell ({cx},{cy}) is {dist:F2} tiles from building {n.Id} — want >= {SettlementRoads.RoadClearanceTiles}"); ok = false; }
                        }
                    }
                }

                // 6. Ц2.6 THE FENCE FOLLOWS THE ROADS: the derived fence (from these very buildings/gates/
                //    roads) must enclose every routed road cell — the enclosure the fence is FOR.
                var fence = SettlementFence.Derive(buildingNodes, gateNodes, g.Segments, SettlementFence.FenceMarginTiles);
                if (fence == null || !fence.IsClosedSane())
                { Debug.LogError($"FAIL roads[{label}]: derived fence is null or not-sane"); ok = false; }
                else
                    foreach (var s in g.Segments)
                    {
                        int ax = (int)System.Math.Round(s.A.X), ay = (int)System.Math.Round(s.A.Y);
                        int bx = (int)System.Math.Round(s.B.X), by = (int)System.Math.Round(s.B.Y);
                        int steps = System.Math.Max(System.Math.Abs(bx - ax), System.Math.Abs(by - ay));
                        for (int i = 0; i <= steps; i++)
                        {
                            int cx = ax + System.Math.Sign(bx - ax) * i, cy = ay + System.Math.Sign(by - ay) * i;
                            if (!fence.Contains(cx, cy))
                            { Debug.LogError($"FAIL roads[{label}]: edge {s.EdgeIndex} cell ({cx},{cy}) is outside the derived fence"); ok = false; }
                        }
                    }
            }

            AssertClean(floor, "generated");

            // THE DRAG CASE: a building dragged off the BuildingCell grid must still be routed AROUND.
            //    +1.3/+0.7 tiles is a non-multiple of the pitch and small enough (< half the ~3-tile free
            //    gap) to never create a room overlap.
            Room moved = null;
            foreach (var r in floor.Rooms) if (r.TypeId == 1) { moved = r; break; }
            moved.X += 1.3f / DungeonLayout.TilesPerAxis;
            moved.Y += 0.7f / DungeonLayout.TilesPerAxis;
            AssertClean(floor, "dragged");

            if (ok) Debug.Log("Settlement Roads: PASS");
        }

        // Adapter identical to DungeonLayout.BuildRenderGraph's: rooms → tile-space LinkNodes, links → LinkEdges
        // (ids). Hoisted out of SelfTestRoads (Task 7) so SelfTestRoadJunctions/SelfTestRoadsPerf share them too.
        private System.Collections.Generic.List<LinkNode> RoadNodes(InteriorFloor lvl)
        {
            var ns = new System.Collections.Generic.List<LinkNode>();
            foreach (var r in lvl.Rooms)
            {
                var (w, h) = DungeonProjection.EffectiveSize(r);
                ns.Add(new LinkNode { Id = r.Id, CX = r.X * DungeonLayout.TilesPerAxis, CY = r.Y * DungeonLayout.TilesPerAxis, W = w, H = h });
            }
            return ns;
        }
        private System.Collections.Generic.List<LinkEdge> RoadEdges(InteriorFloor lvl)
        {
            var es = new System.Collections.Generic.List<LinkEdge>();
            foreach (var c in lvl.Links) es.Add(new LinkEdge { A = c.RoomA, B = c.RoomB });
            return es;
        }

        [ContextMenu("Self-Test: Settlement Road Junctions")]
        public void SelfTestRoadJunctions()
        {
            bool ok = true;
            // Walled city, ≥2 gates → arterials exist. Seed pinned: the assertion is fixture-specific
            // (like the other pinned fixtures in this file); if a code change legitimately re-routes the
            // town, re-pin the seed — but FIRST convince yourself the merge behaviour is still there.
            // Re-pinned 7->2 (Ц2.6 Task 6): gates now come off the derived building fence instead of the
            // raw notional wall, which shifts every gate position and re-routes seed 7's town such that no
            // branch merges into an arterial lane at 20 buildings/2 gates any more. Proxy-scanned seeds
            // 1..200 with the SAME fixture shape first (gates=2 throughout) — merges are still common
            // (2,3,6,9,12,15,17,19,22,25,... ~40% of seeds), confirming the reuse-discount merge behaviour
            // itself is intact and this is a fixture ripple, not a regression; seed 2 is the first that
            // merges and is otherwise unremarkable.
            var cfg = new SettlementConfig { Seed = 2, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
            var floor = SettlementGenerator.BuildFloor(cfg);
            var nodes = RoadNodes(floor); var edges = RoadEdges(floor);
            var g = SettlementRoads.Build(nodes, edges);

            // Rebuild each edge's routed CELLS from its segments (integer walk along axis-aligned runs).
            var gateIds = new System.Collections.Generic.HashSet<int>();
            foreach (var r in floor.Rooms) if (r.TypeId == 0) gateIds.Add(r.Id);
            var cellsByEdge = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<(int, int)>>();
            foreach (var s in g.Segments)
            {
                if (!cellsByEdge.TryGetValue(s.EdgeIndex, out var set))
                    cellsByEdge[s.EdgeIndex] = set = new System.Collections.Generic.HashSet<(int, int)>();
                int ax = (int)System.Math.Round(s.A.X), ay = (int)System.Math.Round(s.A.Y);
                int bx = (int)System.Math.Round(s.B.X), by = (int)System.Math.Round(s.B.Y);
                int steps = System.Math.Max(System.Math.Abs(bx - ax), System.Math.Abs(by - ay));
                for (int i = 0; i <= steps; i++)
                    set.Add((ax + System.Math.Sign(bx - ax) * i, ay + System.Math.Sign(by - ay) * i));
            }

            // A branch must MERGE into an arterial lane: ≥3 shared cells with one arterial edge — a lane
            // stretch, NOT a single crossing cell (a plain perpendicular crossing shares exactly 1 cell,
            // which the no-discount mutant would still produce; 3+ in one pair = riding the lane).
            bool merged = false;
            foreach (var kvB in cellsByEdge)
            {
                var lb = floor.Links[kvB.Key];
                if (gateIds.Contains(lb.RoomA) && gateIds.Contains(lb.RoomB)) continue;   // arterial itself
                foreach (var kvA in cellsByEdge)
                {
                    var la = floor.Links[kvA.Key];
                    if (!gateIds.Contains(la.RoomA) || !gateIds.Contains(la.RoomB)) continue;
                    int shared = 0;
                    foreach (var c in kvB.Value) if (kvA.Value.Contains(c)) shared++;
                    if (shared >= 3) { merged = true; break; }
                }
                if (merged) break;
            }
            if (!merged) { Debug.LogError("FAIL road junctions: no branch shares a >=3-cell lane stretch with any arterial — branches never merge (T-junctions lost)"); ok = false; }
            if (ok) Debug.Log("Settlement Road Junctions: PASS");
        }

        [ContextMenu("Self-Test: Settlement Roads Perf")]
        public void SelfTestRoadsPerf()
        {
            // THE acceptance gate from the Ц1.6 spec (§2.4): a full road Build at the 80-building cap
            // must stay under the settle-path budget. 50 ms mirrors SelfTestStreets' bound; the per-frame
            // budget (if RoadsDuringDrag) was measured separately by the spike.
            var cfg = new SettlementConfig { Seed = 9, TargetBuildings = 80, ActiveBuildings = 10, HasWall = true };   // SelfTestStreets' bigCfg shape
            var floor = SettlementGenerator.BuildFloor(cfg);
            var nodes = RoadNodes(floor); var edges = RoadEdges(floor);

            // Ц2.6: the wall is no longer a road obstacle, so there is no wall sweep cost left to pay here —
            // the gate now times the plain building-obstacle Build at the largest grid (80-building cap).
            SettlementRoads.Build(nodes, edges);             // warm-up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SettlementRoads.Build(nodes, edges);
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 50)
                Debug.LogError($"FAIL roads perf: Build at 80 buildings took {sw.ElapsedMilliseconds} ms, want <50");
            else
                Debug.Log($"Settlement Roads Perf: PASS ({sw.ElapsedMilliseconds} ms)");
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
            // key at all. Expected cells are HAND-DERIVED from the fixed lattice (cell i spans
            // [i*Pitch, (i+1)*Pitch), Pitch = 0.07), never read back from the implementation:
            //     0.30 / 0.07 =  4.2857… -> floor 4        0.05 / 0.07 =  0.7142… -> floor 0
            //     0.72 / 0.07 = 10.2857… -> floor 10
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

            SettlementFootprint.EnsureFootprints(town);

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

            // A gate (TypeId 0) is not a building: it must stay footprint-less.
            var gate = floor.GetRoom(3);
            if (gate.Cells != null)
            { Debug.LogError($"FAIL footprint-migration: gate room 3 got a footprint of {gate.Cells.Length} ints, want none (Cells stays null)"); ok = false; }

            // THE LANDMINE. The footprint is a SEPARATE field: SizeW/SizeH are TILES (one lattice cell is
            // 0.07 * 128 ≈ 8.96 tiles), so a migration that reinterpreted them as cells would inflate every
            // saved town ~54x in area, silently, on load. Pin that this pass never writes them.
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
            SettlementFootprint.EnsureFootprints(town);
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
            SettlementFootprint.EnsureFootprints(building);
            if (bfloor.Rooms[0].Cells != null)
            { Debug.LogError($"FAIL footprint-migration: a Building interior's room got a footprint of {bfloor.Rooms[0].Cells.Length} ints, want none"); ok = false; }

            // A corrupt/absent interior must degrade, not throw, exactly like Decode.
            SettlementFootprint.EnsureFootprints(null);

            if (ok) Debug.Log("Settlement Footprint Migration: PASS");
        }

        [ContextMenu("Self-Test: Settlement Validation")]
        public void SelfTestSettlementValidation()
        {
            bool ok = true;
            // A walled city has 2-4 gates and no boss room — under the dungeon rules that WRONGLY yields
            // "должен быть ровно один вход" + "нет комнаты босса". A settlement must yield NO issues.
            var city = SettlementGenerator.Generate(
                new WorldGen.Generation.SettlementConfig { Seed = 7, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true }, "poi-city");
            var issues = DungeonValidator.Validate(city);
            if (issues.Count != 0)
            {
                foreach (var iss in issues) Debug.LogError($"FAIL settlement-validation: settlement produced dungeon issue '{iss.Message}'");
                ok = false;
            }
            if (ok) Debug.Log("Settlement Validation: PASS");
        }
    }
}
