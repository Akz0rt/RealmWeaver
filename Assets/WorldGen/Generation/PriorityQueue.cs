using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Простой бинарный min-heap, заменяющий System.Collections.Generic.PriorityQueue.
    /// Нужен, потому что в большинстве версий Unity 2022.3 (даже с Api Compatibility Level
    /// = .NET Standard 2.1) встроенный PriorityQueue из BCL .NET 6+ физически недоступен -
    /// Unity ещё не подтянула эту часть стандартной библиотеки в свою сборку netstandard.dll.
    ///
    /// API намеренно максимально похож на System.Collections.Generic.PriorityQueue,
    /// чтобы замена в коде, который её использует, была минимальной (по сути - замена типа).
    /// </summary>
    public class PriorityQueue<TElement, TPriority> where TPriority : IComparable<TPriority>
    {
        readonly List<(TElement Element, TPriority Priority)> heap = new List<(TElement, TPriority)>();

        public int Count => heap.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            heap.Add((element, priority));
            SiftUp(heap.Count - 1);
        }

        public TElement Dequeue()
        {
            if (heap.Count == 0)
                throw new InvalidOperationException("PriorityQueue пуста.");

            var root = heap[0];
            int lastIndex = heap.Count - 1;
            heap[0] = heap[lastIndex];
            heap.RemoveAt(lastIndex);

            if (heap.Count > 0)
                SiftDown(0);

            return root.Element;
        }

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (heap.Count == 0)
            {
                element = default;
                priority = default;
                return false;
            }

            var root = heap[0];
            element = root.Element;
            priority = root.Priority;

            int lastIndex = heap.Count - 1;
            heap[0] = heap[lastIndex];
            heap.RemoveAt(lastIndex);

            if (heap.Count > 0)
                SiftDown(0);

            return true;
        }

        void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[index].Priority.CompareTo(heap[parent].Priority) >= 0) break;

                Swap(index, parent);
                index = parent;
            }
        }

        void SiftDown(int index)
        {
            int count = heap.Count;
            while (true)
            {
                int left = index * 2 + 1;
                int right = index * 2 + 2;
                int smallest = index;

                if (left < count && heap[left].Priority.CompareTo(heap[smallest].Priority) < 0)
                    smallest = left;
                if (right < count && heap[right].Priority.CompareTo(heap[smallest].Priority) < 0)
                    smallest = right;

                if (smallest == index) break;

                Swap(index, smallest);
                index = smallest;
            }
        }

        void Swap(int a, int b)
        {
            var tmp = heap[a];
            heap[a] = heap[b];
            heap[b] = tmp;
        }
    }
}
