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
    }
}
