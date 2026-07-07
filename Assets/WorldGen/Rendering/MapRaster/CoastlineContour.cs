using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Строит гладкую границу суша/вода из уже посчитанного графа Corner (см. CornerGraphBuilder):
    /// трассирует упорядоченные замкнутые петли вдоль рёбер клеточных полигонов, где по одну
    /// сторону вода (океан или озеро), по другую суша, и сглаживает их Chaikin corner-cutting.
    /// См. docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md.
    /// </summary>
    public static class CoastlineContour
    {
        static bool IsWaterCell(VoronoiCell cell) => cell.EffectiveIsOcean || cell.EffectiveIsLake;

        /// <summary>Трассирует все замкнутые петли границы категории (клетки, где inRegion различается
        /// по разные стороны ребра) и сглаживает каждую smoothingIterations итерациями Chaikin, с
        /// предварительным прореживанием вершин на decimationDistance (см. DecimateClosedLoop).
        /// Берег - частный случай (inRegion = IsWaterCell); семейства/полосы - другие предикаты (см.
        /// MapRasterizer.RasterizeSmoothedCategoryRect). Разомкнутые/вырожденные цепочки пропускаются.</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById,
            Func<VoronoiCell, bool> inRegion, int smoothingIterations, float decimationDistance)
        {
            var cornerById = new Dictionary<int, Corner>(corners.Count);
            foreach (var c in corners) cornerById[c.Id] = c;

            var boundaryNeighbors = new Dictionary<int, List<int>>();

            foreach (var corner in corners)
            {
                foreach (var neighborId in corner.NeighborCornerIds)
                {
                    if (neighborId <= corner.Id) continue; // каждое неориентированное ребро - один раз
                    if (!cornerById.TryGetValue(neighborId, out var neighbor)) continue;

                    var shared = SharedCellIds(corner, neighbor);
                    if (shared.Count != 2) continue; // ребро по краю карты (1 клетка) - не граница

                    bool in0 = cellById.TryGetValue(shared[0], out var c0) && inRegion(c0);
                    bool in1 = cellById.TryGetValue(shared[1], out var c1) && inRegion(c1);
                    if (in0 == in1) continue; // обе клетки одной категории - не граница

                    AddBoundaryNeighbor(boundaryNeighbors, corner.Id, neighbor.Id);
                    AddBoundaryNeighbor(boundaryNeighbors, neighbor.Id, corner.Id);
                }
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<int>();

            foreach (var startId in boundaryNeighbors.Keys)
            {
                if (visited.Contains(startId)) continue;
                var loopIds = WalkClosedLoop(startId, boundaryNeighbors, visited);
                if (loopIds == null) continue; // разомкнутая/вырожденная цепочка - пропускаем

                var points = loopIds.Select(id => cornerById[id].Position).ToList();
                points = DecimateClosedLoop(points, decimationDistance, MinDecimateVertices);
                loops.Add(ChaikinSmoothClosed(points, smoothingIterations));
            }

            return loops;
        }

        /// <summary>Водная обёртка (контур берега): inRegion = IsWaterCell. Сохраняет старую 3-арг
        /// сигнатуру (decimationDistance по умолчанию 0 = без прореживания).</summary>
        public static List<List<Vector2>> TraceSmoothedLoops(
            IReadOnlyList<Corner> corners, IReadOnlyDictionary<int, VoronoiCell> cellById,
            int smoothingIterations, float decimationDistance = 0f)
            => TraceSmoothedLoops(corners, cellById, IsWaterCell, smoothingIterations, decimationDistance);

        /// <summary>Петли с ≤ этого числа вершин не прорежаются (мелкие острова/полоски биома -
        /// защита от схлопывания).</summary>
        const int MinDecimateVertices = 8;

        /// <summary>Прорежает замкнутую петлю по длине дуги: оставляет вершину каждые minSegmentDistance
        /// мировых единиц вдоль контура. Реже вершины → крупнее радиусы Chaikin → круглее граница.
        /// minSegmentDistance ≤ 0 ИЛИ петля ≤ minKeepGuard вершин → возвращает исходную петлю без
        /// изменений; результат никогда не короче 4 вершин (иначе откат к исходной петле).</summary>
        static List<Vector2> DecimateClosedLoop(List<Vector2> points, float minSegmentDistance, int minKeepGuard)
        {
            if (minSegmentDistance <= 0f || points.Count <= minKeepGuard) return points;

            var kept = new List<Vector2> { points[0] };
            float acc = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                acc += Vector2.Distance(points[i - 1], points[i]);
                if (acc >= minSegmentDistance)
                {
                    kept.Add(points[i]);
                    acc = 0f;
                }
            }
            if (kept.Count < 4) return points; // прорезали слишком агрессивно - откат
            return kept;
        }

        /// <summary>Растеризует набор замкнутых петель (см. TraceSmoothedLoops) в булеву маску
        /// even-odd правилом (стандартный scanline polygon fill) - петли озёр внутри острова
        /// автоматически становятся "дырками", без явного различения типа петли. Пишет ТОЛЬКО
        /// внутри [rectX, rectX+rectW) x [rectY, rectY+rectH) в уже существующий массив isLand -
        /// безопасно вызывать повторно для под-прямоугольника (кисть), не трогая остальную маску.</summary>
        public static void RasterizeIsLand(
            IReadOnlyList<List<Vector2>> loops, bool[] isLand,
            int texWidth, int texHeight, float mapWidth, float mapHeight,
            int rectX, int rectY, int rectW, int rectH)
        {
            var crossings = new List<float>();

            for (int y = rectY; y < rectY + rectH; y++)
            {
                float worldY = (y + 0.5f) / texHeight * mapHeight;

                crossings.Clear();
                foreach (var loop in loops)
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % n];
                        if ((a.Y <= worldY) == (b.Y <= worldY)) continue; // ребро не пересекает эту строку
                        float t = (worldY - a.Y) / (b.Y - a.Y);
                        crossings.Add(a.X + t * (b.X - a.X));
                    }
                }
                crossings.Sort();

                int rowBase = y * texWidth;
                bool inside = false;
                int crossingIdx = 0;
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    float worldX = (x + 0.5f) / texWidth * mapWidth;
                    while (crossingIdx < crossings.Count && crossings[crossingIdx] <= worldX)
                    {
                        inside = !inside;
                        crossingIdx++;
                    }
                    isLand[rowBase + x] = inside;
                }
            }
        }

        /// <summary>Как RasterizeIsLand (even-odd scanline), но пишет целочисленную метку labelValue
        /// ТОЛЬКО там, где пиксель ВНУТРИ петель, не затирая внешние пиксели. Позволяет растеризовать
        /// несколько категорий последовательно в один буфер-метку (старшая перезаписывает младшую на
        /// перекрытиях). Пишет только в [rectX,rectX+rectW) x [rectY,rectY+rectH).</summary>
        public static void RasterizeRegionLabel(
            IReadOnlyList<List<Vector2>> loops, int[] label, int labelValue,
            int texWidth, int texHeight, float mapWidth, float mapHeight,
            int rectX, int rectY, int rectW, int rectH)
        {
            var crossings = new List<float>();

            for (int y = rectY; y < rectY + rectH; y++)
            {
                float worldY = (y + 0.5f) / texHeight * mapHeight;

                crossings.Clear();
                foreach (var loop in loops)
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % n];
                        if ((a.Y <= worldY) == (b.Y <= worldY)) continue;
                        float t = (worldY - a.Y) / (b.Y - a.Y);
                        crossings.Add(a.X + t * (b.X - a.X));
                    }
                }
                crossings.Sort();

                int rowBase = y * texWidth;
                bool inside = false;
                int crossingIdx = 0;
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    float worldX = (x + 0.5f) / texWidth * mapWidth;
                    while (crossingIdx < crossings.Count && crossings[crossingIdx] <= worldX)
                    {
                        inside = !inside;
                        crossingIdx++;
                    }
                    if (inside) label[rowBase + x] = labelValue; // пишем ТОЛЬКО внутри
                }
            }
        }

        static List<int> SharedCellIds(Corner a, Corner b)
        {
            var result = new List<int>();
            foreach (var id in a.TouchingCellIds)
                if (b.TouchingCellIds.Contains(id))
                    result.Add(id);
            return result;
        }

        static void AddBoundaryNeighbor(Dictionary<int, List<int>> map, int fromId, int toId)
        {
            if (!map.TryGetValue(fromId, out var list))
            {
                list = new List<int>();
                map[fromId] = list;
            }
            if (!list.Contains(toId)) list.Add(toId);
        }

        /// <summary>Идёт по цепочке boundary-соседей от startId, каждый раз выбирая соседа,
        /// отличного от предыдущего corner, пока не вернётся в startId. null, если цепочка не
        /// замкнулась (открытый конец или вырожденный узел с числом boundary-соседей != 2).</summary>
        static List<int> WalkClosedLoop(int startId, Dictionary<int, List<int>> boundaryNeighbors, HashSet<int> visited)
        {
            var loop = new List<int> { startId };
            visited.Add(startId);

            int previousId = -1;
            int currentId = startId;
            int maxSteps = boundaryNeighbors.Count + 1;

            for (int step = 0; step < maxSteps; step++)
            {
                var neighbors = boundaryNeighbors[currentId];
                if (neighbors.Count != 2) return null; // открытый конец или вырожденный узел

                int nextId = neighbors[0] == previousId ? neighbors[1] : neighbors[0];

                if (nextId == startId) return loop; // петля замкнулась

                if (visited.Contains(nextId)) return null; // защита от вырожденной топологии

                loop.Add(nextId);
                visited.Add(nextId);
                previousId = currentId;
                currentId = nextId;
            }

            return null; // не замкнулась за разумное число шагов - вырождение, пропускаем
        }

        /// <summary>Chaikin corner-cutting: заменяет каждый отрезок A-B двумя точками
        /// 0.75A+0.25B и 0.25A+0.75B. iterations=0 - без изменений (текущее "гранёное" поведение).
        /// Сохраняет замкнутость петли на каждой итерации (индексация по модулю).</summary>
        static List<Vector2> ChaikinSmoothClosed(List<Vector2> points, int iterations)
        {
            if (points.Count < 3) return points;

            for (int iter = 0; iter < iterations; iter++)
            {
                var next = new List<Vector2>(points.Count * 2);
                int n = points.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % n];
                    next.Add(a + (b - a) * 0.25f);
                    next.Add(a + (b - a) * 0.75f);
                }
                points = next;
            }

            return points;
        }
    }
}
