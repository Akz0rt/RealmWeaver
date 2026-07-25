using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Parameters for generating a settlement. Size is a single knob (TargetBuildings); the wall
    /// radius and gate count derive from it, so one generator spans a hamlet to a capital.</summary>
    public class SettlementConfig
    {
        public int Seed;
        public int TargetBuildings = 40;
        public int ActiveBuildings = 10;
        public bool HasWall = true;
    }

    /// <summary>A gate position on the wall, in normalized space. Becomes a Room node (TypeId 0) at assembly.</summary>
    public struct GatePoint { public float X, Y; }

    /// <summary>A placed building's centre, normalized. Becomes a Room node (TypeId 1) at assembly.</summary>
    public struct PlacedBuilding { public float X, Y; }

    /// <summary>Deterministic settlement geometry: wall, gates, building placement (Tasks 2–3), and assembly
    /// into an InteriorData (Task 5). Pure, no Unity — the whole point is headless testability, since the
    /// dungeon packer measured 18–48 overlapping pairs at 40 nodes and cannot be reused.</summary>
    public static class SettlementGenerator
    {
        public const int WallSides = 9;
        public const float WallJitter = 0.12f;

        /// <summary>Nominal footprint (tiles, both axes) a placed building projects as when the preliminary
        /// fence is derived from it (Ц2.6: gates are spaced on a fence traced around the ACTUAL buildings, not
        /// the raw notional wall). Pinned to <see cref="DungeonProjection.EffectiveSize"/>'s default for a
        /// fresh <see cref="Room"/>: TypeId defaults to 1 ("Normal" — the same TypeId BuildFloor assigns every
        /// building room below), SizeW/H default to 0 ("unset"), so EffectiveSize falls through to
        /// RoomSizing.Default(1)'s default case — (6,6), both sides already inside RoomSizing.Clamp's 1..16
        /// range unchanged. 6 is therefore the exact size a building room would render/pack at if it ever
        /// went through the normal room-sizing path, so the preliminary fence hugs buildings at the same
        /// nominal scale the rest of the codebase already assumes for a TypeId-1 room.</summary>
        public const float NominalBuildingTiles = 6f;

        /// <summary>Wall radius (normalized) for a building count. Area scales with the count, so the radius
        /// scales with its SQUARE ROOT — the old linear law under-provisioned big towns, which is what forced
        /// buildings to touch. Tuned so the curve reaches the 0.45 clamp exactly AT the cap (MaxBuildings):
        /// r(8)~0.200, r(20)~0.306, r(40)~0.425, r(45)=0.45 (clamped) — this is deliberate, not incidental,
        /// since measurement showed the cap needs the full clamp radius to be reachable for nearly every seed
        /// (a two-anchor law that undershot r(45) left ~58% of seeds short of 45 buildings). Clamped at 0.45:
        /// beyond it buildings sit outside the normalized field, which makes the editor's settle animation
        /// non-terminating (its remaining-distance measure plateaus above the done-epsilon because positions
        /// are clamped while targets are not).</summary>
        public static float WallRadiusFor(int buildingCount)
        {
            if (buildingCount < 1) buildingCount = 1;
            float r = 0.018f + 0.0644f * (float)System.Math.Sqrt(buildingCount);   // ~0.20 at 8, ~0.425 at 40, =0.45 (clamp) at 45
            return r > 0.45f ? 0.45f : r;
        }

        /// <summary>Gate count for a building count: 2 for a small town, up to 4 for a large one.</summary>
        public static int GateCountFor(int buildingCount)
        {
            if (buildingCount >= 55) return 4;
            if (buildingCount >= 30) return 3;
            return 2;
        }

        /// <summary>Place `gateCount` gates spread around the wall by ARC LENGTH (offset by a seeded phase so
        /// towns differ), each landing exactly on a wall segment. `gateCount` is supplied by the caller (via
        /// GateCountFor), so there is ONE source of truth for the count — PlaceGates never re-derives it.</summary>
        public static List<GatePoint> PlaceGates(WallContour wall, int gateCount, int seed)
        {
            var gates = new List<GatePoint>();
            if (wall == null || !wall.IsClosedSane() || gateCount <= 0) return gates;

            // Perimeter length so gates are spread by ARC LENGTH, not by vertex index (a jittered polygon has
            // uneven sides; index-spacing would cluster gates on the short sides).
            int n = wall.Points.Count;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                cum[i + 1] = cum[i] + (float)System.Math.Sqrt(dx * dx + dy * dy);
            }
            float total = cum[n];

            var rng = new System.Random(seed * 31 + 17);
            float phase = (float)rng.NextDouble() * total;
            for (int g = 0; g < gateCount; g++)
            {
                float target = (phase + total * g / gateCount) % total;
                gates.Add(PointAtArcLength(wall, cum, target));
            }
            return gates;
        }

        static GatePoint PointAtArcLength(WallContour wall, float[] cum, float target)
        {
            int n = wall.Points.Count;
            for (int i = 0; i < n; i++)
            {
                if (target <= cum[i + 1] || i == n - 1)
                {
                    var a = wall.Points[i]; var b = wall.Points[(i + 1) % n];
                    float segLen = cum[i + 1] - cum[i];
                    float t = segLen <= 0f ? 0f : (target - cum[i]) / segLen;
                    return new GatePoint { X = a.X + t * (b.X - a.X), Y = a.Y + t * (b.Y - a.Y) };
                }
            }
            return new GatePoint { X = wall.Points[0].X, Y = wall.Points[0].Y };
        }

        /// <summary>Normalized pitch of the building grid. One building per cell, so no two are closer than
        /// this — the anti-overlap guarantee that replaces the dungeon packer.</summary>
        public const float BuildingCell = 0.07f;

        /// <summary>Hard ceiling on a settlement's building count. Placement forbids two buildings sharing an
        /// edge, so capacity is the EVEN sublattice alone, not the checkerboard as a whole: the odd tier
        /// cannot contribute in practice (see PlaceBuildings) — when the even sublattice already covers the
        /// target the odd loop breaks before its first iteration, and when it falls short every remaining odd
        /// cell has at least one even neighbour already taken (WallContour.DistanceToEdge is unsigned
        /// distance-to-nearest-segment, so stepping inward along a near-straight nonagon edge can only
        /// increase clearance, never create a gap), so it is always rejected. A 1000-seed sweep of PLACED
        /// count at target 45 (clamped 0.45 radius) found a floor of 39 (956/1000 seeds reach exactly 45);
        /// two seeds sampled directly for the raw (uncapped) even-sublattice size gave 49 (seed 9) and 50
        /// (seed 7) — both consistent with review's broader per-seed range of ~39–58. 45 therefore sits NEAR
        /// THE CEILING, not in headroom — a small tail of seeds (~4.4%) legitimately falls short of it,
        /// which is valid, not a bug (see PlaceBuildings). The inspector's stepper clamps to the same
        /// constant so the UI never promises a town the generator cannot build.</summary>
        public const int MaxBuildings = 45;

        static void ShuffleCells(List<(int ix, int iy)> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static List<PlacedBuilding> PlaceBuildings(WallContour wall, int seed, int targetCount)
        {
            var kept = new List<PlacedBuilding>();
            if (wall == null || !wall.IsClosedSane()) return kept;

            // Bounding box of the wall. Seeded from the first point (domain-agnostic — no assumption that
            // the contour lies within 0..1) rather than from 1f/0f sentinels, matching the same hardening
            // WallContour.IsClosedSane applies. IsClosedSane above guarantees Points.Count >= 3.
            float minX = wall.Points[0].X, minY = wall.Points[0].Y, maxX = wall.Points[0].X, maxY = wall.Points[0].Y;
            foreach (var p in wall.Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }

            float half = BuildingCell * 0.5f;
            float x0 = minX + half, y0 = minY + half;
            int nx = (int)System.Math.Floor((maxX - half - x0) / BuildingCell + 1e-6) + 1;
            int ny = (int)System.Math.Floor((maxY - half - y0) / BuildingCell + 1e-6) + 1;

            // Candidates carry INTEGER lattice indices, not accumulated floats: the parity below must be
            // exact, and an accumulating `cy += BuildingCell` drifts across a wide bbox.
            // Split by checkerboard parity — two cells of the same parity can never share an edge.
            var even = new List<(int ix, int iy)>();
            var odd = new List<(int ix, int iy)>();
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float cx = x0 + ix * BuildingCell, cy = y0 + iy * BuildingCell;
                    if (!wall.Contains(cx, cy) || wall.DistanceToEdge(cx, cy) < half) continue;
                    if (((ix + iy) & 1) == 0) even.Add((ix, iy)); else odd.Add((ix, iy));
                }

            var rng = new System.Random(seed * 131 + 71);
            ShuffleCells(even, rng);
            ShuffleCells(odd, rng);

            var taken = new HashSet<(int, int)>();
            bool NoNeighbour(int ix, int iy) =>
                !taken.Contains((ix - 1, iy)) && !taken.Contains((ix + 1, iy)) &&
                !taken.Contains((ix, iy - 1)) && !taken.Contains((ix, iy + 1));

            // Even sublattice FIRST: no two of its cells are 4-adjacent, so every draw is legal BY
            // CONSTRUCTION at any occupancy and the loop can never stall. Naive rejection sampling would
            // jam near 36% coverage — far short of the target at this lattice.
            foreach (var cell in even)
            {
                if (kept.Count >= targetCount) break;
                taken.Add(cell);
                kept.Add(new PlacedBuilding { X = x0 + cell.ix * BuildingCell, Y = y0 + cell.iy * BuildingCell });
            }
            // Top up from the odd sublattice where the rule allows. In practice this NEVER contributes: when
            // the even sublattice already covers the target the loop below breaks before its first iteration;
            // when it falls short, every remaining odd cell already has an even neighbour taken (an unsigned
            // distance-to-edge means stepping inward can only increase clearance, never open a gap), so
            // NoNeighbour always rejects it. The delivered layout is therefore a random SUBSET of the even
            // sublattice, a literal checkerboard — not broken out of one. Kept anyway as correct defensive
            // code (mutant-covered by MutSpacingNoAdjacencyCheck), and a strict checkerboard subset is
            // arguably the better outcome for readability. Only these candidates need the neighbour test.
            foreach (var cell in odd)
            {
                if (kept.Count >= targetCount) break;
                if (!NoNeighbour(cell.ix, cell.iy)) continue;
                taken.Add(cell);
                kept.Add(new PlacedBuilding { X = x0 + cell.ix * BuildingCell, Y = y0 + cell.iy * BuildingCell });
            }
            // Short-placed is valid, not an error: a jittered contour can be tighter than the nominal circle.
            return kept;
        }

        /// <summary>Assemble one settlement floor: gate rooms (TypeId 0) then building rooms (TypeId 1) in
        /// the SAME order the street stage indexes them (gates first), streets → links. Ц2.6: no wall is
        /// stored — a walled town's gates are spaced on a PRELIMINARY fence derived from the buildings
        /// actually placed (SettlementFence.Derive), never on the raw notional wall; the FINAL fence (which
        /// also wraps routed roads) is re-derived by the renderer/fit (Task 7). Room ids are assigned 1..N in
        /// gates-then-buildings order so a StreetEdge index i maps to room id i+1.</summary>
        public static InteriorFloor BuildFloor(SettlementConfig cfg)
        {
            // Placement region: a NOTIONAL contour (identical Rounded call regardless of HasWall) used only
            // to seed the building grid and route streets — never stored, so nothing renders it directly.
            // Clamped once here and reused for both the contour and the placement (and the stored
            // SettlementParams below) so a request above MaxBuildings never quietly asks the contour for a
            // town it cannot legally fill under the no-shared-edge rule.
            int target = cfg.TargetBuildings > MaxBuildings ? MaxBuildings : cfg.TargetBuildings;
            var placement = WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, WallRadiusFor(target), WallSides, WallJitter);
            var buildings = PlaceBuildings(placement, cfg.Seed, target);

            // Gates: derived from a preliminary fence traced around the placed buildings (tile space), then
            // spaced on it (normalized). A wall-less village, or a walled town that placed zero buildings,
            // gets none.
            var gates = new List<GatePoint>();
            if (cfg.HasWall && buildings.Count > 0)
            {
                const float T = DungeonLayout.TilesPerAxis;
                var bNodes = new List<LinkNode>(buildings.Count);
                for (int i = 0; i < buildings.Count; i++)
                    bNodes.Add(new LinkNode { Id = i, CX = buildings[i].X * T, CY = buildings[i].Y * T, W = NominalBuildingTiles, H = NominalBuildingTiles });
                var prelimTile = SettlementFence.Derive(bNodes, new List<LinkNode>(), new List<LinkSegment>(), SettlementFence.FenceMarginTiles);
                if (prelimTile != null)
                {
                    var prelimNorm = new WallContour();
                    foreach (var p in prelimTile.Points)
                        prelimNorm.Points.Add(new WallPoint { X = p.X / T, Y = p.Y / T });
                    gates = PlaceGates(prelimNorm, GateCountFor(target), cfg.Seed);
                }
            }

            var edges = SettlementStreets.GenerateStreets(placement, buildings, gates, cfg.Seed);

            var floor = new InteriorFloor();
            // Node index i (gates first, then buildings) → room id (i+1). Ids are stable and dense.
            var idByIndex = new int[gates.Count + buildings.Count];
            int next = 1;
            for (int i = 0; i < gates.Count; i++)
            {
                idByIndex[i] = next;
                floor.Rooms.Add(new Room { Id = next, TypeId = 0, X = gates[i].X, Y = gates[i].Y });
                next++;
            }
            int activeCount = cfg.ActiveBuildings < 0 ? 0 : cfg.ActiveBuildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                idByIndex[gates.Count + i] = next;
                floor.Rooms.Add(new Room { Id = next, TypeId = 1, X = buildings[i].X, Y = buildings[i].Y, IsDummy = i >= activeCount });
                next++;
            }
            floor.NextRoomId = next;
            foreach (var e in edges)
                floor.Links.Add(new Link { RoomA = idByIndex[e.A], RoomB = idByIndex[e.B] });
            floor.SettlementParams = new SettlementParams { TargetBuildings = target, ActiveBuildings = cfg.ActiveBuildings > target ? target : cfg.ActiveBuildings, HasWall = cfg.HasWall };
            return floor;
        }

        public static InteriorData Generate(SettlementConfig cfg, string ownerPoiId)
        {
            var data = new InteriorData { OwnerPoiId = ownerPoiId, Kind = InteriorKind.Settlement };
            data.Floors.Add(BuildFloor(cfg));
            return data;
        }
    }
}
