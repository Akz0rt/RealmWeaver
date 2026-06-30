using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Превращает список клеток в геометрию границ: классифицирует общие рёбра соседних
    /// клеток на границы регионов (суша/суша с разным RegionId) и береговую линию (суша/вода),
    /// и строит из набора рёбер тонкий меш-ленту для рендера. Классификация (ClassifyBorderEdges)
    /// не зависит от UnityEngine - её можно проверять self-check'ом; построение меша (BuildRibbonMesh)
    /// использует UnityEngine.Mesh (типы UnityEngine указаны полным именем, т.к. System.Numerics.Vector2
    /// и UnityEngine.Vector2/Vector3 конфликтуют по короткому имени).
    /// </summary>
    public static class MapBorderBuilder
    {
        public struct Edge
        {
            public Vector2 A;
            public Vector2 B;
            public Edge(Vector2 a, Vector2 b) { A = a; B = b; }
        }

        /// <summary>Округляет точку до целых тысячных карты - чтобы общие вершины соседних
        /// полигонов с микроскопическим float-расхождением попадали в один ключ ребра.</summary>
        static (long, long) Quantize(Vector2 p)
            => ((long)System.Math.Round(p.X * 1000.0), (long)System.Math.Round(p.Y * 1000.0));

        static (long, long, long, long) EdgeKey(Vector2 a, Vector2 b)
        {
            var qa = Quantize(a);
            var qb = Quantize(b);
            // Канонический порядок концов - чтобы (a,b) и (b,a) давали один ключ.
            bool aFirst = qa.Item1 < qb.Item1 || (qa.Item1 == qb.Item1 && qa.Item2 <= qb.Item2);
            return aFirst
                ? (qa.Item1, qa.Item2, qb.Item1, qb.Item2)
                : (qb.Item1, qb.Item2, qa.Item1, qa.Item2);
        }

        public static void ClassifyBorderEdges(
            IReadOnlyList<VoronoiCell> cells,
            out List<Edge> regionEdges,
            out List<Edge> coastEdges)
        {
            regionEdges = new List<Edge>();
            coastEdges = new List<Edge>();
            if (cells == null) return;

            var idToCell = new Dictionary<int, VoronoiCell>();
            foreach (var c in cells) idToCell[c.Id] = c;

            // Ключ ребра -> (геометрия ребра, список Id клеток, которым оно принадлежит).
            var edgeToCells = new Dictionary<(long, long, long, long), (Edge edge, List<int> cellIds)>();

            foreach (var cell in cells)
            {
                var poly = cell.Polygon;
                if (poly == null || poly.Count < 3) continue;
                for (int i = 0; i < poly.Count; i++)
                {
                    var p0 = poly[i];
                    var p1 = poly[(i + 1) % poly.Count];
                    var key = EdgeKey(p0, p1);
                    if (!edgeToCells.TryGetValue(key, out var entry))
                    {
                        entry = (new Edge(p0, p1), new List<int>());
                        edgeToCells[key] = entry;
                    }
                    entry.cellIds.Add(cell.Id); // entry.cellIds - ссылка, общий список, мутируется на месте
                }
            }

            foreach (var kv in edgeToCells)
            {
                var edge = kv.Value.edge;
                var ids = kv.Value.cellIds;
                if (ids.Count != 2) continue; // ребро по краю карты или вырожденное - не граница

                var ca = idToCell[ids[0]];
                var cb = idToCell[ids[1]];
                bool aOcean = ca.EffectiveIsOcean;
                bool bOcean = cb.EffectiveIsOcean;
                bool aWater = aOcean || ca.EffectiveIsLake;
                bool bWater = bOcean || cb.EffectiveIsLake;

                if (aOcean != bOcean)
                {
                    // Берег = граница именно с океаном. Внутренние озёра (озеро<->суша) НЕ обводятся,
                    // чтобы читались как часть региона, а не как отдельный обособленный объект.
                    coastEdges.Add(edge);
                }
                else if (!aWater && !bWater && ca.RegionId != cb.RegionId)
                {
                    regionEdges.Add(edge);
                }
            }
        }

        /// <summary>Строит один меш из тонких quad-лент вдоль каждого ребра (ширина width,
        /// в плоскости XZ на высоте yHeight). Один меш = один draw call на тип границы.</summary>
        public static UnityEngine.Mesh BuildRibbonMesh(IReadOnlyList<Edge> edges, float width, float yHeight)
        {
            var verts = new List<UnityEngine.Vector3>();
            var tris = new List<int>();
            float half = width * 0.5f;

            if (edges != null)
            {
                foreach (var e in edges)
                {
                    var p0 = new UnityEngine.Vector3(e.A.X, yHeight, e.A.Y);
                    var p1 = new UnityEngine.Vector3(e.B.X, yHeight, e.B.Y);
                    var dir = p1 - p0;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-8f) continue;
                    dir.Normalize();
                    var side = new UnityEngine.Vector3(-dir.z, 0f, dir.x) * half;

                    int bi = verts.Count;
                    verts.Add(p0 - side);
                    verts.Add(p0 + side);
                    verts.Add(p1 + side);
                    verts.Add(p1 - side);

                    tris.Add(bi + 0); tris.Add(bi + 2); tris.Add(bi + 1);
                    tris.Add(bi + 0); tris.Add(bi + 3); tris.Add(bi + 2);
                }
            }

            var mesh = new UnityEngine.Mesh();
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
