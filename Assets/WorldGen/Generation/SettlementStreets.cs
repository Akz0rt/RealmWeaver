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
    /// Variant A: grow a spanning structure from the gates (each unconnected building joins via its nearest
    /// connected node), then add a few gate→far-building trunks so the town has through-routes, not a pure
    /// tree. The growth rescans all connected×unconnected pairs per step, so cost is ~O(nBuild²·nNodes) —
    /// still well under a millisecond at the Ц1 town cap (≤80 buildings; the &lt;50 ms self-test confirms
    /// it), and this file is the isolated swap point if far larger settlements ever need a faster router.
    /// Contrast BuildRenderGraph(Clean), which was 20–34 s at N=60.</summary>
    public static class SettlementStreets
    {
        public static IReadOnlyList<StreetEdge> GenerateStreets(
            WallContour wall, IReadOnlyList<PlacedBuilding> buildings, IReadOnlyList<GatePoint> gates, int seed)
        {
            var edges = new List<StreetEdge>();
            int nGates = gates != null ? gates.Count : 0;
            int nBuild = buildings != null ? buildings.Count : 0;
            int nNodes = nGates + nBuild;
            if (nNodes < 2 || nGates == 0 || nBuild == 0) return edges;

            // Node positions, gates first.
            var px = new float[nNodes];
            var py = new float[nNodes];
            for (int i = 0; i < nGates; i++) { px[i] = gates[i].X; py[i] = gates[i].Y; }
            for (int i = 0; i < nBuild; i++) { px[nGates + i] = buildings[i].X; py[nGates + i] = buildings[i].Y; }

            // Prim-style growth seeded from all gates: repeatedly attach the nearest unconnected node to the
            // connected set. Deterministic — pure distance, ties broken by lower index.
            var connected = new bool[nNodes];
            for (int g = 0; g < nGates; g++) connected[g] = true;
            int remaining = nBuild;
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

            // A few extra trunks (1..nGates of them): each connects a randomly chosen gate to its farthest
            // building, so the town has cross-routes rather than one minimal tree. Seeded pick keeps it
            // deterministic and varied.
            var rng = new System.Random(seed * 977 + 13);
            int trunks = 1 + rng.Next(nGates);
            for (int t = 0; t < trunks; t++)
            {
                int g = rng.Next(nGates);
                int far = -1; float farD = -1f;
                for (int v = nGates; v < nNodes; v++)
                {
                    float dx = px[g] - px[v], dy = py[g] - py[v];
                    float d = dx * dx + dy * dy;
                    if (d > farD) { farD = d; far = v; }
                }
                if (far >= 0 && !EdgeExists(edges, g, far)) edges.Add(new StreetEdge { A = g, B = far });
            }
            return edges;
        }

        static bool EdgeExists(List<StreetEdge> edges, int a, int b)
        {
            foreach (var e in edges) if ((e.A == a && e.B == b) || (e.A == b && e.B == a)) return true;
            return false;
        }
    }
}
