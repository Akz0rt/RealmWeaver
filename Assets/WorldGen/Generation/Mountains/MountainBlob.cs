using System;
using System.Collections.Generic;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Пятно (§2) — множество мазков, попарно связанных пересечением. Единица расчёта: маска, поле
    /// расстояний, оси и ярусы считаются по пятну целиком, а не по мазку.
    /// </summary>
    public sealed class MountainBlob
    {
        /// <summary>Рисующие мазки пятна, в порядке рисования.</summary>
        public List<MountainStroke> Strokes = new List<MountainStroke>();

        /// <summary>Стирающие мазки, задевшие это пятно. Хранятся отдельно: они вычитаются из маски
        /// и НЕ участвуют в слиянии пятен — иначе одно движение ластика между двумя массивами
        /// объявило бы их одним объектом.</summary>
        public List<MountainStroke> Erasers = new List<MountainStroke>();

        /// <summary>Зерно разброса, выведенное из САМОГО СТАРОГО мазка пятна.
        ///
        /// Это не мелочь оформления, а условие живой кисти. Если зерно зависит от состава пятна, то
        /// каждый новый мазок, слившийся с массивом, перетасует ВСЕ его звенья: ДМ дорисовал
        /// отросток — а весь хребет прыгнул. Зерно от старшего мазка это лечит: дорисовка не трогает
        /// уже нарисованное, при слиянии двух пятен выживает старшее, а отмена возвращает прежнее
        /// зерно сама собой, потому что вернётся прежний состав мазков.</summary>
        public uint Seed
        {
            get
            {
                int oldest = int.MaxValue;
                foreach (var s in Strokes) if (s.Id < oldest) oldest = s.Id;
                return oldest == int.MaxValue ? 0u : Fnv(oldest);
            }
        }

        /// <summary>FNV-1a от номера мазка — тем же способом проект уже сеет внутренности зданий.</summary>
        public static uint Fnv(int value)
        {
            uint hash = 2166136261u;
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (uint)((value >> (i * 8)) & 0xFF);
                    hash *= 16777619u;
                }
            }
            return hash;
        }
    }

    /// <summary>
    /// Раскладывает мазки по пятнам. Транзитивность (A задевает B, B задевает C — все трое одно
    /// пятно) обеспечена системой непересекающихся множеств, а не порядком обхода: иначе результат
    /// зависел бы от того, в каком порядке ДМ рисовал.
    /// </summary>
    public static class BlobBuilder
    {
        public static List<MountainBlob> Group(IReadOnlyList<MountainStroke> strokes)
        {
            var result = new List<MountainBlob>();
            if (strokes == null || strokes.Count == 0) return result;

            var paint = new List<MountainStroke>();
            var erasers = new List<MountainStroke>();
            foreach (var s in strokes)
            {
                if (s == null || s.Points.Count == 0) continue;
                (s.Erase ? erasers : paint).Add(s);
            }
            if (paint.Count == 0) return result;

            var parent = new int[paint.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            for (int i = 0; i < paint.Count; i++)
                for (int j = i + 1; j < paint.Count; j++)
                    if (StrokeGeometry.Overlap(paint[i], paint[j])) Union(parent, i, j);

            var byRoot = new Dictionary<int, MountainBlob>();
            for (int i = 0; i < paint.Count; i++)
            {
                int root = Find(parent, i);
                if (!byRoot.TryGetValue(root, out var blob))
                {
                    blob = new MountainBlob();
                    byRoot[root] = blob;
                    result.Add(blob);
                }
                blob.Strokes.Add(paint[i]);
            }

            // Стирающий мазок приписывается КАЖДОМУ пятну, которого коснулся: одно движение ластика
            // может отгрызть край сразу у двух соседних массивов.
            foreach (var eraser in erasers)
                foreach (var blob in result)
                    foreach (var stroke in blob.Strokes)
                        if (StrokeGeometry.Overlap(eraser, stroke)) { blob.Erasers.Add(eraser); break; }

            return result;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }
}
