using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Строит граф corners (уникальных вершин Voronoi-диаграммы) из уже готового списка
    /// VoronoiCell. Дедуплицирует геометрически совпадающие точки (с точностью до epsilon),
    /// которые иначе существовали бы по несколько раз - по одной копии на каждую клетку,
    /// чья граница через них проходит.
    /// </summary>
    public static class CornerGraphBuilder
    {
        public static List<Corner> Build(List<VoronoiCell> cells, float epsilon = 0.01f)
        {
            // Квантуем координаты в целочисленную сетку с шагом epsilon, чтобы геометрически
            // совпадающие (но не бит-в-бит идентичные из-за погрешности float) точки получили
            // одинаковый ключ и слились в один Corner.
            var cornerByQuantizedPos = new Dictionary<(long, long), Corner>();
            var corners = new List<Corner>();

            (long, long) Quantize(Vector2 p) => ((long)System.MathF.Round(p.X / epsilon), (long)System.MathF.Round(p.Y / epsilon));

            Corner GetOrCreateCorner(Vector2 position)
            {
                var key = Quantize(position);
                if (cornerByQuantizedPos.TryGetValue(key, out var existing))
                    return existing;

                var corner = new Corner(corners.Count, position);
                corners.Add(corner);
                cornerByQuantizedPos[key] = corner;
                return corner;
            }

            // Проходим по полигону каждой клетки, создаём/находим corners для каждой вершины,
            // связываем соседние вершины внутри одного полигона рёбрами, и отмечаем, что
            // эта клетка касается данного corner.
            foreach (var cell in cells)
            {
                if (cell.Polygon.Count < 3) continue;

                var cellCornerIds = new List<int>();
                foreach (var vertex in cell.Polygon)
                {
                    var corner = GetOrCreateCorner(vertex);
                    if (!corner.TouchingCellIds.Contains(cell.Id))
                        corner.TouchingCellIds.Add(cell.Id);
                    cellCornerIds.Add(corner.Id);
                }

                int n = cellCornerIds.Count;
                for (int i = 0; i < n; i++)
                {
                    int aId = cellCornerIds[i];
                    int bId = cellCornerIds[(i + 1) % n];

                    var a = corners[aId];
                    var b = corners[bId];

                    if (!a.NeighborCornerIds.Contains(bId)) a.NeighborCornerIds.Add(bId);
                    if (!b.NeighborCornerIds.Contains(aId)) b.NeighborCornerIds.Add(aId);
                }
            }

            return corners;
        }
    }
}
