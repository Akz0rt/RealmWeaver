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
            var wall = SettlementGenerator.BuildWall(cfg);

            // ---- 1. A walled settlement has a non-null, sane contour ------------------------------------
            if (wall == null || !wall.IsClosedSane())
            { Debug.LogError("FAIL gates: BuildWall returned null/insane for HasWall=true"); ok = false; }

            // ---- 2. A wall-less village has NO contour --------------------------------------------------
            // Return a wall anyway and a village would render an unwanted perimeter.
            var villageCfg = new SettlementConfig { Seed = 3, TargetBuildings = 8, HasWall = false };
            if (SettlementGenerator.BuildWall(villageCfg) != null)
            { Debug.LogError("FAIL gates: BuildWall returned a contour for HasWall=false"); ok = false; }

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

            if (ok) Debug.Log("Settlement Gates: PASS");
        }

        [ContextMenu("Self-Test: Settlement Buildings")]
        public void SelfTestBuildings()
        {
            bool ok = true;
            var cfg = new SettlementConfig { Seed = 11, TargetBuildings = 40, HasWall = true };
            var wall = SettlementGenerator.BuildWall(cfg);
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
            var wall = SettlementGenerator.BuildWall(cfg);
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
            var bw = SettlementGenerator.BuildWall(bigCfg);
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

            // ---- 2. The wall is stored on the floor ----------------------------------------------------
            if (floor.Wall == null || !floor.Wall.IsClosedSane())
            { Debug.LogError("FAIL assembly: floor.Wall is null/insane"); ok = false; }

            // ---- 3. Gate nodes are TypeId 0 and sit on the wall; building nodes are TypeId 1 -----------
            int gateNodes = 0, buildNodes = 0;
            foreach (var r in floor.Rooms)
            {
                if (r.TypeId == 0) { gateNodes++;
                    if (floor.Wall.DistanceToEdge(r.X, r.Y) > 1e-3f)
                    { Debug.LogError($"FAIL assembly: gate room {r.Id} at ({r.X:F3},{r.Y:F3}) is off the wall"); ok = false; } }
                else if (r.TypeId == 1) { buildNodes++;
                    if (!floor.Wall.Contains(r.X, r.Y))
                    { Debug.LogError($"FAIL assembly: building room {r.Id} at ({r.X:F3},{r.Y:F3}) is outside the wall"); ok = false; } }
                else
                { Debug.LogError($"FAIL assembly: room {r.Id} has TypeId {r.TypeId}, want 0 (gate) or 1 (building)"); ok = false; }
            }
            if (gateNodes < 2)
            { Debug.LogError($"FAIL assembly: {gateNodes} gate nodes, want ≥2"); ok = false; }
            if (buildNodes < 20)
            { Debug.LogError($"FAIL assembly: {buildNodes} building nodes, want ≥20 for a 40-target town"); ok = false; }

            // ---- 4. Links map StreetEdge indices to the RIGHT room ids (the load-bearing invariant) -----
            // Reconstruct the exact gates/buildings/edges BuildFloor used (all deterministic from floor.Wall +
            // seed), then verify (a) rooms were created in gates-then-buildings order — the room at combined
            // index i carries node i's position and type — and (b) every street edge {A,B} became a link
            // between room ids A+1 and B+1. A reversed or scrambled index→id mapping (the "every street links
            // the wrong pair" bug) fails here; a ContainsKey check could not, because idByIndex is a bijection.
            var exGates = SettlementGenerator.PlaceGates(floor.Wall, SettlementGenerator.GateCountFor(cfg.TargetBuildings), cfg.Seed);
            var exBuildings = SettlementGenerator.PlaceBuildings(floor.Wall, cfg.Seed, cfg.TargetBuildings);
            var exEdges = SettlementStreets.GenerateStreets(floor.Wall, exBuildings, exGates, cfg.Seed);
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

            // ---- 5. NextRoomId is past every id, so the editor's «add» never collides -----------------
            int maxId = 0; foreach (var r in floor.Rooms) if (r.Id > maxId) maxId = r.Id;
            if (floor.NextRoomId <= maxId)
            { Debug.LogError($"FAIL assembly: NextRoomId {floor.NextRoomId} is not past maxId {maxId}"); ok = false; }

            // ---- 6. Determinism: same seed → same room count and first room position ------------------
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

            // ---- 1. A wall-less village stores NO wall --------------------------------------------------
            // Store the notional placement contour here and a village would render an unwanted perimeter.
            if (floor.Wall != null)
            { Debug.LogError("FAIL village: floor.Wall is non-null for a HasWall=false village"); ok = false; }

            // ---- 2. NO gate rooms; a substantial building count is STILL placed ------------------------
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
            // A jitter-free octagon radius 0.3 at (0.5,0.5): vertex 0 at angle 0 is (0.8,0.5) → max X 0.8;
            // the opposite vertex is (0.2,0.5) → min X 0.2. In tiles that is ×TilesPerAxis.
            var wall = WallContour.Rounded(seed: 1, cx: 0.5f, cy: 0.5f, radius: 0.3f, sides: 8, jitter: 0f);
            var (minX, minY, maxX, maxY) = DungeonProjection.WallBoundsTiles(wall);
            float expMax = 0.8f * DungeonLayout.TilesPerAxis, expMin = 0.2f * DungeonLayout.TilesPerAxis;
            if (System.Math.Abs(maxX - expMax) > 0.5f)
            { Debug.LogError($"FAIL wallbounds: maxX {maxX:F1}, want ~{expMax:F1}"); ok = false; }
            if (System.Math.Abs(minX - expMin) > 0.5f)
            { Debug.LogError($"FAIL wallbounds: minX {minX:F1}, want ~{expMin:F1}"); ok = false; }

            // The union of room bounds and wall bounds must extend past the ROOMS alone (the clip the fix
            // repairs): a real city's wall reaches past its inner buildings on at least one side.
            var floor = SettlementGenerator.Generate(
                new SettlementConfig { Seed = 8, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true }, "poi-city").Floors[0];
            var (rMinX, rMinY, rMaxX, rMaxY) = DungeonProjection.ContentBoundsTiles(floor);
            var (wMinX, wMinY, wMaxX, wMaxY) = DungeonProjection.WallBoundsTiles(floor.Wall);
            float uMinX = System.Math.Min(rMinX, wMinX), uMaxX = System.Math.Max(rMaxX, wMaxX);
            float uMinY = System.Math.Min(rMinY, wMinY), uMaxY = System.Math.Max(rMaxY, wMaxY);
            bool grew = uMinX < rMinX - 0.01f || uMaxX > rMaxX + 0.01f || uMinY < rMinY - 0.01f || uMaxY > rMaxY + 0.01f;
            if (!grew)
            { Debug.LogError($"FAIL wallbounds: wall AABB [{wMinX:F1},{wMinY:F1}]-[{wMaxX:F1},{wMaxY:F1}] does not extend past rooms [{rMinX:F1},{rMinY:F1}]-[{rMaxX:F1},{rMaxY:F1}] — the union would not fix the clip"); ok = false; }

            if (ok) Debug.Log("Settlement Wall Bounds: PASS");
        }

        [ContextMenu("Self-Test: Settlement Roads")]
        public void SelfTestRoads()
        {
            bool ok = true;
            // Field names verified against SettlementConfig (Seed / TargetBuildings / ActiveBuildings /
            // HasWall) and the initializer shape the other settlement tests use.
            var cfg = new SettlementConfig { Seed = 7, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
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
            }

            AssertClean(floor, "generated");

            // 5. THE DRAG CASE: a building dragged off the BuildingCell grid must still be routed AROUND.
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
            var cfg = new SettlementConfig { Seed = 7, TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
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
            SettlementRoads.Build(nodes, edges);                       // warm-up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SettlementRoads.Build(nodes, edges);
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 50)
                Debug.LogError($"FAIL roads perf: Build at 80 buildings took {sw.ElapsedMilliseconds} ms, want <50");
            else
                Debug.Log($"Settlement Roads Perf: PASS ({sw.ElapsedMilliseconds} ms)");
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
