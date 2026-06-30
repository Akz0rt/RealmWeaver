using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldGen.Generation
{
    /// <summary>
    /// Представляет одну реку как последовательность corner.Id от истока до устья (океан/озеро).
    /// </summary>
    public class River
    {
        public List<int> CornerPath = new List<int>();
    }

    /// <summary>
    /// Трассирует реки от случайных высоких точек вниз по downhill-графу до океана/озера/
    /// локального минимума (защита от бесконечного цикла - см. maxSteps). Накопление потока
    /// (flow) на каждом corner-to-corner ребре считается отдельно через RiverFlowAccumulator,
    /// после того как все реки уже трассированы - позволяет потокам сливающихся рек складываться,
    /// как у Patel ("when upper rivers of flow A and B merge, the lower river has flow A+B").
    /// </summary>
    public static class RiverTracer
    {
        public static List<River> TraceRivers(List<Corner> corners, int numberOfRivers, int seed,
                                                 float minStartElevation = 0.6f, int maxSteps = 1000)
        {
            var downhill = DownhillGraph.ComputeDownhill(corners);
            var cornerById = corners.ToDictionary(c => c.Id);
            var rng = new Random(seed + 3000); // отдельный сдвиг seed

            var candidates = corners.Where(c => !c.IsOcean && !c.IsWater && c.Elevation >= minStartElevation).ToList();
            if (candidates.Count == 0) return new List<River>(); // нет подходящих высоких точек - рек не будет

            var rivers = new List<River>();

            for (int i = 0; i < numberOfRivers; i++)
            {
                var start = candidates[rng.Next(candidates.Count)];
                var river = new River();
                river.CornerPath.Add(start.Id);

                int currentId = start.Id;
                var visited = new HashSet<int> { currentId }; // защита от циклов на плато с одинаковой elevation

                for (int step = 0; step < maxSteps; step++)
                {
                    var current = cornerById[currentId];
                    if (current.IsOcean) break; // дошли до моря - река завершена
                    if (current.IsWater) break; // дошли до озера - тоже валидное завершение

                    int? nextId = downhill.TryGetValue(currentId, out var n) ? n : null;
                    if (nextId == null) break; // локальный минимум без стока - река заканчивается тут (редкий edge case)
                    if (visited.Contains(nextId.Value)) break; // защита от цикла (не должно происходить при строго убывающей elevation, но на всякий случай)

                    river.CornerPath.Add(nextId.Value);
                    visited.Add(nextId.Value);
                    currentId = nextId.Value;
                }

                if (river.CornerPath.Count > 1) // отбрасываем вырожденные "реки" длиной 1 corner (старт сразу был концом)
                    rivers.Add(river);
            }

            return rivers;
        }
    }
}
