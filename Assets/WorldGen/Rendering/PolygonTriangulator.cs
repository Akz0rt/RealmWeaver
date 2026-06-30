namespace WorldGen.Rendering
{
    /// <summary>
    /// Fan triangulation от центра полигона. Работает надёжно для convex полигонов,
    /// которыми почти всегда являются Voronoi-клетки (за редким исключением клеток,
    /// искажённых обрезкой по границе карты).
    /// </summary>
    public static class PolygonTriangulator
    {
        /// <summary>
        /// Возвращает индексы треугольников (локальные, начиная с 0) для полигона,
        /// где индекс 0 - это центр (добавляется вызывающим кодом отдельно от периметра),
        /// а индексы 1..vertexCount-1 - вершины периметра по порядку.
        /// vertexCount - общее количество вершин включая центр.
        /// </summary>
        public static int[] TriangulateFan(int vertexCount)
        {
            var triangles = new System.Collections.Generic.List<int>();
            for (int i = 1; i < vertexCount - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }
            return triangles.ToArray();
        }
    }
}
