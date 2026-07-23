using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>A street: an undirected edge between two settlement nodes, by index into the combined node
    /// list (gates at [0..gateCount), buildings after). Topology only — no polyline. The editor derives the
    /// on-screen shape itself (spec §8.1).</summary>
    public struct StreetEdge { public int A, B; }

    /// <summary>THE ISOLATED, SWAPPABLE STREET STAGE (spec §6.4). References only WallContour/GatePoint/
    /// PlacedBuilding/StreetEdge and System.* — never InteriorData, Room, Link, SettlementGenerator or
    /// UnityEngine. When the street approach changes, ONLY this file changes.
    ///
    /// Ц1.6: gates are connected among THEMSELVES first, into a deterministic arterial spanning tree (Prim
    /// from gate 0, pure distance, ties by lower index) — these gate-gate edges are emitted FIRST in the
    /// returned list. Ordering contract consumed by the road router (SettlementRoads, Task 6/7): it routes
    /// edges in input order and gives later roads a lane-reuse discount, so the arterials must claim their
    /// lanes before any branch merges in. After the arterials, a Prim-style growth pass attaches every
    /// remaining building to the nearest already-connected node (gate or building). The growth rescans all
    /// connected×unconnected pairs per step, so cost is ~O(nBuild²·nNodes) — still well under a millisecond
    /// at the Ц1 town cap (≤80 buildings; the &lt;50 ms self-test confirms it), and this file is the isolated
    /// swap point if far larger settlements ever need a faster router. Contrast BuildRenderGraph(Clean),
    /// which was 20–34 s at N=60. The old random gate→farthest-building trunk pass is removed — the
    /// arterials are the cross-routes now.
    ///
    /// Gate-less towns (a wall-less village, HasWall=false): there is no gate, so no arterial pass runs and
    /// there is no gate to root the building growth at either — growth is seeded from a HUB building instead,
    /// the one nearest the centroid of all buildings (ties broken by lower index), deterministic and
    /// RNG-free.</summary>
    public static class SettlementStreets
    {
        public static IReadOnlyList<StreetEdge> GenerateStreets(
            WallContour wall, IReadOnlyList<PlacedBuilding> buildings, IReadOnlyList<GatePoint> gates, int seed)
        {
            var edges = new List<StreetEdge>();
            int nGates = gates != null ? gates.Count : 0;
            int nBuild = buildings != null ? buildings.Count : 0;
            int nNodes = nGates + nBuild;
            if (nBuild == 0) return edges;   // nothing to connect (a lone building needs no edges either —
                                              // remaining below naturally comes out 0 for it)

            // Node positions, gates first.
            var px = new float[nNodes];
            var py = new float[nNodes];
            for (int i = 0; i < nGates; i++) { px[i] = gates[i].X; py[i] = gates[i].Y; }
            for (int i = 0; i < nBuild; i++) { px[nGates + i] = buildings[i].X; py[nGates + i] = buildings[i].Y; }

            // Ц1.6 ARTERIALS (spec §2.1): connect the gates among THEMSELVES into a deterministic
            // spanning tree (Prim from gate 0, pure distance, ties by lower index) and emit these
            // gate-gate edges FIRST. Ordering contract: the road router (SettlementRoads) routes edges
            // in input order with a reuse discount for later roads, so arterials must claim their lanes
            // before any branch merges in. Replaces the old random gate→farthest-building trunk pass —
            // arterials are the cross-routes now. `seed` stays in the signature (swap-point stability).
            if (nGates > 1)
            {
                var inNet = new bool[nGates];
                inNet[0] = true;
                for (int added = 1; added < nGates; added++)
                {
                    int bestFrom = -1, bestTo = -1;
                    float bestD = float.MaxValue;
                    for (int u = 0; u < nGates; u++)
                    {
                        if (!inNet[u]) continue;
                        for (int v = 0; v < nGates; v++)
                        {
                            if (inNet[v]) continue;
                            float dx = px[u] - px[v], dy = py[u] - py[v];
                            float d = dx * dx + dy * dy;
                            if (d < bestD) { bestD = d; bestFrom = u; bestTo = v; }
                        }
                    }
                    if (bestTo < 0) break;
                    edges.Add(new StreetEdge { A = bestFrom, B = bestTo });
                    inNet[bestTo] = true;
                }
            }

            // Prim-style growth: repeatedly attach the nearest unconnected building to the connected set.
            // Deterministic — pure distance, ties broken by lower index. Seeded from all gates when the
            // town has any; otherwise seeded from a single HUB building (centroid-nearest) so a gate-less
            // village still ends up as one connected spanning tree.
            var connected = new bool[nNodes];
            int remaining;
            if (nGates > 0)
            {
                for (int g = 0; g < nGates; g++) connected[g] = true;
                remaining = nBuild;
            }
            else
            {
                float cx = 0f, cy = 0f;
                for (int i = 0; i < nBuild; i++) { cx += px[i]; cy += py[i]; }
                cx /= nBuild; cy /= nBuild;
                int hub = 0; float hubD = float.MaxValue;
                for (int i = 0; i < nBuild; i++)
                {
                    float dx = px[i] - cx, dy = py[i] - cy;
                    float d = dx * dx + dy * dy;
                    if (d < hubD) { hubD = d; hub = i; }
                }
                connected[hub] = true;
                remaining = nBuild - 1;
            }
            while (remaining > 0)
            {
                int bestFrom = -1, bestTo = -1;
                float bestD = float.MaxValue;
                for (int u = 0; u < nNodes; u++)
                {
                    if (!connected[u]) continue;
                    for (int v = nGates; v < nNodes; v++)
                    {
                        if (connected[v]) continue;
                        float dx = px[u] - px[v], dy = py[u] - py[v];
                        float d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; bestFrom = u; bestTo = v; }
                    }
                }
                if (bestTo < 0) break;             // no reachable node left (should not happen)
                edges.Add(new StreetEdge { A = bestFrom, B = bestTo });
                connected[bestTo] = true;
                remaining--;
            }
            return edges;
        }
    }
}
