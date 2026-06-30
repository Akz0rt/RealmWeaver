using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Обрезка полигона прямоугольником через алгоритм Sutherland-Hodgman.
    /// Нужна, потому что Voronoi-клетки на краю облака точек могут иметь вершины,
    /// уходящие далеко за пределы карты (нет соседнего треугольника, чтобы их "закрыть").
    /// </summary>
    public static class PolygonClipper
    {
        public static List<Vector2> ClipToRect(List<Vector2> polygon, float minX, float minY, float maxX, float maxY)
        {
            var result = polygon;
            if (result.Count == 0) return result;

            result = ClipEdge(result, p => p.X >= minX, (a, b) => IntersectVertical(a, b, minX));
            if (result.Count == 0) return result;

            result = ClipEdge(result, p => p.X <= maxX, (a, b) => IntersectVertical(a, b, maxX));
            if (result.Count == 0) return result;

            result = ClipEdge(result, p => p.Y >= minY, (a, b) => IntersectHorizontal(a, b, minY));
            if (result.Count == 0) return result;

            result = ClipEdge(result, p => p.Y <= maxY, (a, b) => IntersectHorizontal(a, b, maxY));
            return result;
        }

        static List<Vector2> ClipEdge(List<Vector2> polygon, System.Func<Vector2, bool> isInside, System.Func<Vector2, Vector2, Vector2> intersect)
        {
            var output = new List<Vector2>();
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 current = polygon[i];
                Vector2 previous = polygon[(i - 1 + n) % n];

                bool currentInside = isInside(current);
                bool previousInside = isInside(previous);

                if (currentInside)
                {
                    if (!previousInside)
                        output.Add(intersect(previous, current));
                    output.Add(current);
                }
                else if (previousInside)
                {
                    output.Add(intersect(previous, current));
                }
            }

            return output;
        }

        static Vector2 IntersectVertical(Vector2 a, Vector2 b, float x)
        {
            float t = (x - a.X) / (b.X - a.X);
            return new Vector2(x, a.Y + t * (b.Y - a.Y));
        }

        static Vector2 IntersectHorizontal(Vector2 a, Vector2 b, float y)
        {
            float t = (y - a.Y) / (b.Y - a.Y);
            return new Vector2(a.X + t * (b.X - a.X), y);
        }
    }
}
