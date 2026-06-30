using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Вершина (угол) Voronoi-полигона - общая точка, где сходятся 3+ клетки.
    /// В отличие от cell.Polygon (где каждая клетка хранит свои вершины независимо,
    /// с дублированием общих точек), Corner существует ОДИН раз для каждой уникальной
    /// геометрической точки, с явным Id - это нужно для расчётов "по графу вершин"
    /// (elevation/moisture как расстояние через рёбра, как в Amit Patel's mapgen2),
    /// и закладывает основу для рек в будущем (рёбра corner-to-corner как русла).
    /// </summary>
    public class Corner
    {
        public int Id;
        public Vector2 Position;

        /// <summary>Id соседних corners - через общее ребро Voronoi-полигона.</summary>
        public List<int> NeighborCornerIds = new List<int>();

        /// <summary>Id клеток (VoronoiCell), для которых этот corner является одной из вершин полигона.</summary>
        public List<int> TouchingCellIds = new List<int>();

        /// <summary>true, если этот corner определён как вода (океан или озеро) - назначается на шаге island shape, ДО расчёта elevation.</summary>
        public bool IsWater;

        /// <summary>true, если этот водный corner - именно океан (связан с краем карты), а не озеро.</summary>
        public bool IsOcean;

        /// <summary>Elevation в диапазоне [0, 1] - расстояние от берега (BFS по corner-графу) в гибриде с шумом. Считается ПОСЛЕ определения land/water.</summary>
        public float Elevation;

        /// <summary>Moisture в диапазоне [0, 1] - расстояние от свежей воды (озёр), BFS по corner-графу.</summary>
        public float Moisture;

        public Corner(int id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    }
}
