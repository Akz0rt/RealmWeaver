using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster; // RegionCategories

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>
    /// Один якорь подписи на политический регион (RegionData). Наивный центроид (простое среднее
    /// координат клеток региона) у невыпуклой / вытянутой / разорванной области падает ВНЕ её -
    /// вплоть до открытого океана (баг «подписи в рандомных местах»: Хадвен/Рунвейл/Каэрморн в море).
    /// Здесь для каждого региона берём его САМЫЙ КРУПНЫЙ связный кусок суши и ставим якорь через
    /// RegionLabelPlacer.OnLandAnchor (взвешенный по площади центроид, притянутый к ближайшей реальной
    /// клетке куска) - та же проверенная логика, что у подписей биом-зон. Результат гарантированно на
    /// суше своего региона, близко к визуальному центру крупнейшего куска.
    /// </summary>
    public static class PoliticalRegionAnchors
    {
        public static Dictionary<int, Vector2> Compute(IReadOnlyList<VoronoiCell> cells)
        {
            var result = new Dictionary<int, Vector2>();
            if (cells == null || cells.Count == 0) return result;

            var byId = new Dictionary<int, VoronoiCell>(cells.Count);
            foreach (var c in cells) byId[c.Id] = c;

            // Клетки суши, сгруппированные по региону (океан/озеро пропускаем - регионы подписываем
            // по суше; лежащие в регионе озёра не должны тянуть якорь в воду).
            var byRegion = new Dictionary<int, List<VoronoiCell>>();
            foreach (var c in cells)
            {
                if (c.RegionId < 0) continue;
                if (!RegionCategories.IsLandCell(c)) continue;
                if (!byRegion.TryGetValue(c.RegionId, out var list))
                    byRegion[c.RegionId] = list = new List<VoronoiCell>();
                list.Add(c);
            }

            foreach (var kv in byRegion)
            {
                var largest = LargestConnectedComponent(kv.Value, byId);
                result[kv.Key] = RegionLabelPlacer.OnLandAnchor(largest);
            }
            return result;
        }

        /// <summary>Крупнейший связный (по NeighborIds) кусок среди клеток региона - чтобы у
        /// разорванного на части региона подпись села на самый большой кусок, а не в пустоту
        /// между ними. Связность считаем только внутри клеток ЭТОГО региона (member).</summary>
        static List<VoronoiCell> LargestConnectedComponent(List<VoronoiCell> regionCells, Dictionary<int, VoronoiCell> byId)
        {
            var member = new HashSet<int>();
            foreach (var c in regionCells) member.Add(c.Id);

            var visited = new HashSet<int>();
            List<VoronoiCell> best = null;
            foreach (var start in regionCells)
            {
                if (visited.Contains(start.Id)) continue;
                var comp = new List<VoronoiCell>();
                var queue = new Queue<VoronoiCell>();
                queue.Enqueue(start); visited.Add(start.Id);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    comp.Add(c);
                    foreach (var nid in c.NeighborIds)
                    {
                        if (visited.Contains(nid)) continue;
                        if (!member.Contains(nid)) continue;
                        if (!byId.TryGetValue(nid, out var nc)) continue;
                        visited.Add(nid);
                        queue.Enqueue(nc);
                    }
                }
                if (best == null || comp.Count > best.Count) best = comp;
            }
            return best ?? regionCells;
        }
    }
}
