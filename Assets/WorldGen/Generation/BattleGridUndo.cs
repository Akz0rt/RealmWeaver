using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Undo history for one battle map. Two entry shapes: a per-cell DELTA for painting, and a
    /// full SNAPSHOT for operations that replace the whole grid (resize, regenerate). The snapshot costs a
    /// few KB and buys uniform behaviour — without it, an accidental «Пересобрать» would be the one
    /// irreversible action on the screen. No redo.</summary>
    public class BattleGridUndo
    {
        public const int MaxDepth = 64;

        class Entry
        {
            public int[] Indices;          // delta entry: null for a snapshot
            public GridCell[] Previous;
            public GridBuffer Snapshot;    // snapshot entry: null for a delta
        }

        readonly List<Entry> stack = new List<Entry>();

        public int Count => stack.Count;

        public void Clear() => stack.Clear();

        public void PushStroke(BattleGridStroke stroke)
        {
            if (stroke == null || stroke.IsEmpty) return;   // a gesture that changed nothing is not a step
            Push(new Entry { Indices = stroke.Indices.ToArray(), Previous = stroke.Previous.ToArray() });
        }

        /// <summary>Record the buffer as it is NOW, before a whole-grid operation. Stores a CLONE — the
        /// caller keeps painting the live buffer.</summary>
        public void PushSnapshot(GridBuffer buf)
        {
            if (buf == null) return;
            Push(new Entry { Snapshot = buf.Clone() });
        }

        void Push(Entry e)
        {
            stack.Add(e);
            if (stack.Count > MaxDepth) stack.RemoveAt(0);   // drop the OLDEST
        }

        /// <summary>Undo one step. `buf` is passed by reference because a snapshot entry can restore a
        /// DIFFERENT size, which means replacing the buffer, not writing into it.</summary>
        public bool TryUndo(ref GridBuffer buf)
        {
            if (stack.Count == 0) return false;
            var e = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);

            if (e.Snapshot != null) { buf = e.Snapshot.Clone(); return true; }

            for (int i = 0; i < e.Indices.Length; i++)
            {
                int idx = e.Indices[i];
                if (idx >= 0 && idx < buf.Cells.Length) buf.Cells[idx] = e.Previous[i];
            }
            return true;
        }
    }
}
