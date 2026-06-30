using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Lloyd's relaxation: сдвигает каждую точку-сайт в центроид (геометрический центр)
    /// её собственного Voronoi-полигона. После 1-2 итераций перестройки диаграммы клетки
    /// становятся более равномерными, без острых вытянутых углов.
    /// </summary>
    public static class LloydRelaxation
    {
        public static List<Vector2> ComputeRelaxedPoints(List<VoronoiCell> cells)
        {
            var newPoints = new List<Vector2>(cells.Count);
            foreach (var cell in cells)
            {
                if (cell.Polygon.Count < 3)
                {
                    // Деградировавший полигон (например, клетка целиком вырезана при clipping) -
                    // оставляем точку на месте, чтобы не потерять её совсем.
                    newPoints.Add(cell.Site);
                    continue;
                }
                newPoints.Add(ComputeCentroid(cell.Polygon));
            }
            return newPoints;
        }

        static Vector2 ComputeCentroid(List<Vector2> polygon)
        {
            float cx = 0, cy = 0, area = 0;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = polygon[i];
                Vector2 p2 = polygon[(i + 1) % n];
                float cross = p1.X * p2.Y - p2.X * p1.Y;
                area += cross;
                cx += (p1.X + p2.X) * cross;
                cy += (p1.Y + p2.Y) * cross;
            }

            area *= 0.5f;
            if (MathF.Abs(area) < 1e-6f)
            {
                // Полигон вырожден (площадь ~0) - fallback на простое среднее вершин.
                float ax = 0, ay = 0;
                foreach (var p in polygon) { ax += p.X; ay += p.Y; }
                return new Vector2(ax / n, ay / n);
            }

            cx /= (6 * area);
            cy /= (6 * area);
            return new Vector2(cx, cy);
        }
    }
}
