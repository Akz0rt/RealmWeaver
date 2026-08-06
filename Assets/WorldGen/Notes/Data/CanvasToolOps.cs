namespace WorldGen.Notes.Data
{
    /// <summary>Зеркало NotesTool в чистом слое. NotesTool живёт в Rendering рядом с MonoBehaviour и в
    /// офлайн-харнесс не поедет; отображение одно-в-одно, конвертация — в HandlePress.</summary>
    public enum CanvasToolKind { Select, Note, Drawing, Image, Zoom }

    /// <summary>Что делает этот клик. None означает «правило молчит»: клик обрабатывает вью по-своему.</summary>
    public enum CanvasClickAction { None, CreateDrawing, CreateNote, CreateImage, PaintExisting, ReleaseBinding }

    /// <summary>Всё, что нужно знать о клике, чтобы решить его судьбу — и ничего больше.</summary>
    public readonly struct CanvasClickInput
    {
        public readonly CanvasToolKind Tool;

        /// <summary>Объект, к которому привязан активный инструмент, или null.</summary>
        public readonly string BoundObjectId;

        /// <summary>Id рисунка под курсором, иначе null. Заметки и картинки сюда не попадают: по ним
        /// не рисуют, и их наличие под курсором ни на одно решение здесь не влияет.</summary>
        public readonly string DrawingUnderCursor;

        public CanvasClickInput(CanvasToolKind tool, string boundObjectId, string drawingUnderCursor)
        {
            Tool = tool;
            BoundObjectId = boundObjectId;
            DrawingUnderCursor = drawingUnderCursor;
        }
    }

    /// <summary>Решение по клику: что сделать, какой быть привязке после и надо ли положить инструмент.</summary>
    public readonly struct CanvasClickResult
    {
        public readonly CanvasClickAction Action;

        /// <summary>Привязка ПОСЛЕ этого клика. Для CreateDrawing здесь null — id ещё не существует,
        /// см. BindCreatedObject.</summary>
        public readonly string BoundObjectId;

        /// <summary>Только для CreateDrawing: привязаться к объекту, который этот клик создаст.</summary>
        public readonly bool BindCreatedObject;

        /// <summary>Кого красить. Не null только при PaintExisting.</summary>
        public readonly string TargetObjectId;

        /// <summary>Инструмент кладёт себя в «Курсор» после того, как клик сделал своё дело.</summary>
        public readonly bool ReturnToSelect;

        public CanvasClickResult(CanvasClickAction action, string boundObjectId, bool bindCreatedObject,
                                 string targetObjectId, bool returnToSelect)
        {
            Action = action;
            BoundObjectId = boundObjectId;
            BindCreatedObject = bindCreatedObject;
            TargetObjectId = targetObjectId;
            ReturnToSelect = returnToSelect;
        }
    }

    /// <summary>
    /// ПРАВИЛО: инструмент, который вставил объект, не кладёт себя — он ПРИВЯЗЫВАЕТСЯ к вставленному.
    ///
    /// Отсюда и до конца файла нет ни одной ссылки на UnityEngine, и это не стилевое предпочтение.
    /// Спутать «клик вне привязанного объекта» с «кликом по пустому месту» ничего не стоит, а разница
    /// между ними — это разница между «ничего не создалось» и «создался лишний рисунок», то есть ровно
    /// та жалоба ДМ, ради которой всё это пишется. Такое проверяют офлайн, а не глазами в Editor.
    ///
    /// ГРАНИЦА ОТВЕТСТВЕННОСТИ НАМЕРЕННО УЗКАЯ. Decide отвечает на один вопрос: «этот клик что-то
    /// вставляет, рисует или снимает привязку?». Для Select и Zoom ответ всегда None, и ветки этих двух
    /// инструментов остаются в HandlePress вместе с панорамированием, выделением связей и попаданием по
    /// объекту. Затащить их сюда значило бы затащить и хит-тесты связей — другой объём, другой риск, и к
    /// жалобе ДМ отношения не имеет.
    ///
    /// ЭТО НЕ ВОЗВРАТ К СОСТОЯНИЮ ДО 4e517cf. Дефект, который тот коммит чинил — армированный инструмент
    /// штампует второй объект под курсором, — остаётся закрытым: клик мимо привязанного рисунка не
    /// создаёт ничего, он снимает привязку.
    /// </summary>
    public static class CanvasToolOps
    {
        static readonly CanvasClickResult Nothing =
            new CanvasClickResult(CanvasClickAction.None, null, false, null, false);

        public static CanvasClickResult Decide(in CanvasClickInput input)
        {
            switch (input.Tool)
            {
                case CanvasToolKind.Note:
                    // Не привязывается: редактирование заметки — это набор текста, для которого
                    // инструмент «Заметка» не нужен вовсе. Каретку в текст ставит вью.
                    return new CanvasClickResult(CanvasClickAction.CreateNote, null, false, null, true);

                case CanvasToolKind.Image:
                    // Пиксели картинки мы не редактируем, поэтому «редактирование» для неё — это размер
                    // и связи, то есть режим «Курсор».
                    return new CanvasClickResult(CanvasClickAction.CreateImage, null, false, null, true);

                case CanvasToolKind.Drawing:
                    return DecideDrawing(input);

                default:
                    return Nothing;
            }
        }

        /// <summary>ПРАВИЛО: недоведённый мазок бросают только при НАСТОЯЩЕЙ смене инструмента.
        /// Подтверждение уже активного инструмента — не смена: раньше SetTool бросал мазок безусловно,
        /// и двойной клик по рисунку (EnterDrawingEditMode зовёт SetTool(Drawing), даже когда «Рисунок»
        /// уже активен) обнулял мазок, начатый первым же кликом второй пары, раньше, чем цикл Update
        /// успевал дописать в него точку — второй тычок быстрой пары пропадал целиком, без следа ни на
        /// экране, ни в данных.</summary>
        public static bool ShouldAbandonStroke(CanvasToolKind current, CanvasToolKind next) => current != next;

        static CanvasClickResult DecideDrawing(in CanvasClickInput input)
        {
            if (input.BoundObjectId != null)
            {
                // ВНУТРЬ ПРИВЯЗАННОГО — мазок, и привязка держится: рисунок делается многими мазками
                // подряд, и инструмент нужен для каждого.
                if (input.DrawingUnderCursor == input.BoundObjectId)
                    return new CanvasClickResult(CanvasClickAction.PaintExisting, input.BoundObjectId,
                                                 false, input.BoundObjectId, false);

                // МИМО — привязка снята, инструмент положен, НИЧЕГО НЕ СОЗДАНО. Чужой рисунок под
                // курсором — это тоже «мимо»: привязка адресует клик своему объекту, а не любому.
                return new CanvasClickResult(CanvasClickAction.ReleaseBinding, null, false, null, true);
            }

            // Существующий рисунок тоже становится привязанным намеренно: иначе правил было бы два
            // похожих («привязан только новый»), и разница между ними проявлялась бы только через
            // клик мимо — то есть в самом неочевидном месте.
            if (input.DrawingUnderCursor != null)
                return new CanvasClickResult(CanvasClickAction.PaintExisting, input.DrawingUnderCursor,
                                             false, input.DrawingUnderCursor, false);

            // Клик, создавший рисунок, НЕ оставляет мазка: он ставит холст. Первый мазок — следующее
            // нажатие. Иначе каждый рисунок рождался бы с точкой, которую никто не просил.
            return new CanvasClickResult(CanvasClickAction.CreateDrawing, null, true, null, false);
        }
    }
}
