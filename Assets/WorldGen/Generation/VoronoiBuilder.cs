using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DelaunatorSharp;

namespace WorldGen.Generation
{
    /// <summary>
    /// Строит Voronoi-диаграмму из облака точек через двойственный граф Delaunay-триангуляции
    /// (используя пакет DelaunatorSharp). Вершины Voronoi-полигонов = circumcenters треугольников Delaunay,
    /// что даёт математически корректную (а не приближённую через центроиды) диаграмму.
    /// </summary>
    public static class VoronoiBuilder
    {
        /// <summary>
        /// width/height - размеры карты, нужны для обрезки (clipping) бесконечных клеток на границе облака точек.
        /// </summary>
        public static List<VoronoiCell> Build(List<Vector2> points, float width, float height)
        {
            if (points.Count < 4)
                throw new ArgumentException("Нужно минимум 4 точки для корректной Delaunay-триангуляции.");

            // DelaunatorSharp использует свой IPoint/Point - конвертируем туда-обратно на границе этого метода,
            // остальной код проекта работает только с System.Numerics.Vector2.
            var delaunatorPoints = points.Select(p => (IPoint)new Point(p.X, p.Y)).ToArray();
            var delaunator = new Delaunator(delaunatorPoints);

            var cells = new List<VoronoiCell>(points.Count);
            for (int i = 0; i < points.Count; i++)
                cells.Add(new VoronoiCell(i, points[i]));

            // --- 1. Полигоны клеток через circumcenters ---
            // GetVoronoiCellsBasedOnCircumcenters возвращает IVoronoiCell с Points (вершины полигона)
            // и Index (индекс исходной точки-сайта, на которую построена эта клетка).
            foreach (var voronoiCell in delaunator.GetVoronoiCellsBasedOnCircumcenters())
            {
                var polygon = voronoiCell.Points
                    .Select(p => new Vector2((float)p.X, (float)p.Y))
                    .ToList();

                var clipped = PolygonClipper.ClipToRect(polygon, 0, 0, width, height);

                cells[voronoiCell.Index].Polygon = clipped;
            }

            // --- 2. Соседи через рёбра Delaunay-триангуляции ---
            // Каждое ребро Delaunay-треугольника соединяет два сайта, которые являются
            // соседями в Voronoi-диаграмме (их клетки граничат друг с другом).
            var neighborSet = new HashSet<(int, int)>();
            foreach (var edge in delaunator.GetEdges())
            {
                int a = delaunator.Points.ToList().FindIndex(p => p.X == edge.P.X && p.Y == edge.P.Y);
                int b = delaunator.Points.ToList().FindIndex(p => p.X == edge.Q.X && p.Y == edge.Q.Y);
                // Примечание: поиск по координатам выше неэффективен для больших карт (O(n) на каждое ребро).
                // Для production-кода лучше использовать индексы напрямую из Triangles/Halfedges (см. метод BuildNeighborsFast ниже) -
                // здесь оставлен явный вариант для наглядности на первой итерации.

                if (a == b || a < 0 || b < 0) continue;
                var key = a < b ? (a, b) : (b, a);
                if (neighborSet.Add(key))
                {
                    cells[a].NeighborIds.Add(b);
                    cells[b].NeighborIds.Add(a);
                }
            }

            return cells;
        }

        /// <summary>
        /// Более быстрый способ построения графа соседства - напрямую через структуру Triangles/Halfedges,
        /// без поиска индекса точки по координатам. Рекомендуется заменить на этот метод, если профилирование
        /// покажет, что наивный вариант выше (BuildNeighbors через FindIndex) стал bottleneck-ом на больших картах.
        /// </summary>
        public static void BuildNeighborsFast(List<VoronoiCell> cells, IPoint[] delaunatorPoints, Delaunator delaunator)
        {
            foreach (var cell in cells) cell.NeighborIds.Clear();
            var neighborSet = new HashSet<(int, int)>();

            for (int e = 0; e < delaunator.Triangles.Length; e++)
            {
                int p = delaunator.Triangles[e];
                int q = delaunator.Triangles[NextHalfedge(e)];
                var key = p < q ? (p, q) : (q, p);
                if (p != q && neighborSet.Add(key))
                {
                    cells[p].NeighborIds.Add(q);
                    cells[q].NeighborIds.Add(p);
                }
            }
        }

        static int NextHalfedge(int e) => (e % 3 == 2) ? e - 2 : e + 1;
    }
}
