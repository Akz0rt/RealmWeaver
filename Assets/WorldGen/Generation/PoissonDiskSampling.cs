using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Генерация равномерно (но не сеточно) распределённых точек - "blue noise".
    /// Алгоритм Bridson's Poisson Disk Sampling, O(n).
    /// Используется как точки-семена для Voronoi-диаграммы.
    /// </summary>
    public static class PoissonDiskSampling
    {
        public static List<Vector2> Generate(float width, float height, float minDist, int seed, int maxAttempts = 30)
        {
            var rng = new Random(seed);
            float cellSize = minDist / MathF.Sqrt(2);
            int gridW = (int)MathF.Ceiling(width / cellSize);
            int gridH = (int)MathF.Ceiling(height / cellSize);
            var grid = new List<Vector2>[gridW, gridH];

            var points = new List<Vector2>();
            var active = new List<Vector2>();

            Vector2 first = new Vector2((float)rng.NextDouble() * width, (float)rng.NextDouble() * height);
            points.Add(first);
            active.Add(first);
            AddToGrid(grid, first, cellSize);

            while (active.Count > 0)
            {
                int idx = rng.Next(active.Count);
                Vector2 point = active[idx];
                bool found = false;

                for (int i = 0; i < maxAttempts; i++)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    float radius = minDist * (1 + (float)rng.NextDouble()); // диапазон [minDist, 2*minDist]
                    Vector2 candidate = point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

                    if (candidate.X < 0 || candidate.X >= width || candidate.Y < 0 || candidate.Y >= height)
                        continue;

                    if (IsFarEnough(grid, candidate, minDist, cellSize, gridW, gridH))
                    {
                        points.Add(candidate);
                        active.Add(candidate);
                        AddToGrid(grid, candidate, cellSize);
                        found = true;
                        break;
                    }
                }

                if (!found) active.RemoveAt(idx);
            }

            return points;
        }

        static void AddToGrid(List<Vector2>[,] grid, Vector2 p, float cellSize)
        {
            int gx = (int)(p.X / cellSize);
            int gy = (int)(p.Y / cellSize);
            if (grid[gx, gy] == null) grid[gx, gy] = new List<Vector2>();
            grid[gx, gy].Add(p);
        }

        static bool IsFarEnough(List<Vector2>[,] grid, Vector2 candidate, float minDist, float cellSize, int gridW, int gridH)
        {
            int gx = (int)(candidate.X / cellSize);
            int gy = (int)(candidate.Y / cellSize);

            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                int nx = gx + dx, ny = gy + dy;
                if (nx < 0 || ny < 0 || nx >= gridW || ny >= gridH) continue;
                if (grid[nx, ny] == null) continue;
                foreach (var p in grid[nx, ny])
                    if (Vector2.Distance(p, candidate) < minDist) return false;
            }
            return true;
        }
    }
}
