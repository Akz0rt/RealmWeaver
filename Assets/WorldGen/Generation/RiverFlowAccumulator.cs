using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Считает суммарный flow на каждом corner-to-corner ребре, по которому проходит хотя бы
    /// одна река - при слиянии путей нескольких рек поток складывается естественно, просто
    /// инкрементируя счётчик ребра при каждом прохождении любой рекой.
    /// </summary>
    public static class RiverFlowAccumulator
    {
        /// <summary>Ключ - (меньший Id, больший Id) ребра, для однозначности независимо от направления обхода.</summary>
        public static Dictionary<(int, int), int> ComputeFlow(List<River> rivers)
        {
            var flow = new Dictionary<(int, int), int>();

            foreach (var river in rivers)
            {
                for (int i = 0; i < river.CornerPath.Count - 1; i++)
                {
                    int a = river.CornerPath[i];
                    int b = river.CornerPath[i + 1];
                    var key = a < b ? (a, b) : (b, a);

                    flow.TryGetValue(key, out var current);
                    flow[key] = current + 1;
                }
            }

            return flow;
        }

        /// <summary>Множество всех corner.Id, через которые проходит хотя бы одна река - нужно для MoistureField (реки как источник свежей воды).</summary>
        public static HashSet<int> GetRiverCornerIds(List<River> rivers)
        {
            var ids = new HashSet<int>();
            foreach (var river in rivers)
                foreach (var cornerId in river.CornerPath)
                    ids.Add(cornerId);
            return ids;
        }
    }
}
