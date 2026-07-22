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
    /// Contrast BuildRenderGraph(Clean), which was 20–34 s at N=60.
    ///
    /// Gate-less towns (a wall-less village, HasWall=false): there is no gate to root the spanning growth
    /// at, so growth is seeded from a HUB building instead — the one nearest the centroid of all buildings
    /// (ties broken by lower index), deterministic and RNG-free. The gate→far-building trunk pass is a
    /// gated-only embellishment and does not run when there are no gates.</summary>
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

            // A few extra trunks (1..nGates of them): each connects a randomly chosen gate to its farthest
            // building, so the town has cross-routes rather than one minimal tree. Seeded pick keeps it
            // deterministic and varied. Gate-only embellishment — skipped for a gate-less town (nGates == 0
            // would make rng.Next(nGates) degenerate to always picking "gate" index 0, which is actually a
            // BUILDING once there are no gates — an out-of-role index, not merely a no-op).
            if (nGates > 0)
            {
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
