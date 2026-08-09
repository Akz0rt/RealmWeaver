using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>
    /// Один "мазок кистью" - от нажатия ЛКМ до отпускания. Хранит состояние каждой затронутой
    /// клетки ДО первого изменения в рамках этого мазка (если клетка задета кистью несколько
    /// раз за один проход, сохраняется только самое первое, "досмазковое" состояние).
    ///
    /// Кроме клеток мазок может нести ДЕЙСТВИЯ отмены (UndoActions) — для правок, которые в
    /// клетках не живут вовсе: нарисованная кисть-река лежит отдельным списком, и «вернуть как
    /// было» для неё значит убрать/вернуть саму реку. Без этого речной мазок не трогал бы ни
    /// одной клетки, считался бы пустым и молча не попадал в историю.
    /// </summary>
    public class BrushStroke
    {
        public readonly Dictionary<VoronoiCell, CellSnapshot> PreStrokeState = new Dictionary<VoronoiCell, CellSnapshot>();

        /// <summary>Действия отмены этого мазка, в порядке применения (откатываются с конца).</summary>
        public readonly List<Action> UndoActions = new List<Action>();

        /// <summary>Сохраняет состояние клетки, если она ещё не была затронута в этом мазке. Вызывать ПЕРЕД применением изменения к клетке.</summary>
        public void RecordIfNew(VoronoiCell cell)
        {
            if (!PreStrokeState.ContainsKey(cell))
                PreStrokeState[cell] = CellSnapshot.Capture(cell);
        }

        public bool IsEmpty => PreStrokeState.Count == 0 && UndoActions.Count == 0;
    }

    /// <summary>
    /// Стек истории мазков кистью для Undo (Ctrl+Z). Одна операция отмены = один мазок
    /// (от нажатия до отпускания ЛКМ) - откатывает все клетки, затронутые за этот мазок, разом
    /// к состоянию, которое было непосредственно перед началом мазка.
    /// </summary>
    public class BrushUndoManager
    {
        readonly Stack<BrushStroke> undoStack = new Stack<BrushStroke>();

        BrushStroke currentStroke;

        /// <summary>Начинает новый мазок - вызывать при нажатии ЛКМ (начало drag кистью).</summary>
        public void BeginStroke()
        {
            currentStroke = new BrushStroke();
        }

        /// <summary>Сохраняет "досмазковое" состояние клетки, если она ещё не была затронута в текущем мазке. Вызывать перед каждым применением изменения кистью.</summary>
        public void RecordBeforeChange(VoronoiCell cell)
        {
            currentStroke?.RecordIfNew(cell);
        }

        /// <summary>Кладёт в текущий мазок действие «вернуть как было» для правки, которая не живёт
        /// в клетках (нарисованная река). Вызывать ПОСЛЕ самой правки — действие её отменяет.</summary>
        public void RecordUndoAction(System.Action undo)
        {
            if (undo != null) currentStroke?.UndoActions.Add(undo);
        }

        /// <summary>Завершает текущий мазок и кладёт его в стек истории - вызывать при отпускании ЛКМ.</summary>
        public void EndStroke()
        {
            if (currentStroke != null && !currentStroke.IsEmpty)
                undoStack.Push(currentStroke);
            currentStroke = null;
        }

        /// <summary>Отменяет последний завершённый мазок - восстанавливает все затронутые им клетки к состоянию до мазка.</summary>
        public bool Undo()
        {
            if (undoStack.Count == 0) return false;

            var stroke = undoStack.Pop();
            foreach (var kvp in stroke.PreStrokeState)
                kvp.Value.RestoreOverridesTo(kvp.Key);
            // С конца: если мазок успел сделать два действия, снимать их надо в обратном порядке.
            for (int i = stroke.UndoActions.Count - 1; i >= 0; i--)
                stroke.UndoActions[i]?.Invoke();

            return true;
        }

        public int UndoStackCount => undoStack.Count;

        public void ClearHistory()
        {
            undoStack.Clear();
            currentStroke = null;
        }
    }
}
