using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самотесты правила «инструмент, который вставил объект, привязывается к нему».
    /// Запускается двумя способами, как всякий набор здесь: правым кликом по компоненту в Editor
    /// или офлайн через Tools/notes-harness.
    ///
    /// ФИКСТУРЫ ПОСТРОЕНЫ ТАК, ЧТОБЫ НЕПРАВИЛЬНОЕ ПРАВИЛО РАСХОДИЛОСЬ С ПРАВИЛЬНЫМ. Ключевой приём:
    /// в тестах на привязку под курсором лежит рисунок с id, ОТЛИЧНЫМ от привязанного. Тест, где
    /// привязанный объект и объект под курсором — один и тот же id, не различает «клик внутрь
    /// привязанного» и «клик по любому рисунку» и потому здесь запрещён.
    ///
    /// КАЖДЫЙ ТЕСТ — ОТДЕЛЬНЫЙ public void SelfTest*, а не раздел внутри общего метода: офлайн-раннер
    /// зовёт рефлексией именно такие, и сваливать их в один метод значило бы терять адресность отчёта.
    /// </summary>
    public class CanvasToolOpsSelfTests : MonoBehaviour
    {
        const string Bound = "рисунок-A";
        const string Other = "рисунок-B";

        static CanvasClickResult Click(CanvasToolKind tool, string bound, string underCursor)
            => CanvasToolOps.Decide(new CanvasClickInput(tool, bound, underCursor));

        /// <summary>Имя теста берётся у вызывающего, поэтому хвост у всех одинаковый и его нельзя
        /// перепутать с чужим — типичная опечатка в наборе, где шесть похожих методов.</summary>
        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            if (ok) Debug.Log($"PASS {name}");
        }

        /// <summary>Жалоба ДМ дословно: клик мимо привязанного рисунка НЕ создаёт второй рисунок.
        /// Убивает мутанта «вернуть CreateDrawing вместо ReleaseBinding» — то есть возврат дефекта,
        /// который чинил 4e517cf. Проверяются оба «мимо»: пустое место и ЧУЖОЙ рисунок.</summary>
        [ContextMenu("Self-Test: клик мимо привязанного рисунка не создаёт второй")]
        public void SelfTestBoundDrawingOutsideClickReleasesInsteadOfCreating()
        {
            bool ok = true;

            var onEmpty = Click(CanvasToolKind.Drawing, Bound, null);
            if (onEmpty.Action != CanvasClickAction.ReleaseBinding)
            {
                Debug.LogError($"FAIL клик мимо привязанного рисунка: {onEmpty.Action}, ждём ReleaseBinding — " +
                               "CreateDrawing здесь и есть тот самый баг «плодятся рисунки»");
                ok = false;
            }
            if (onEmpty.BoundObjectId != null)
            { Debug.LogError($"FAIL после промаха привязка осталась: {onEmpty.BoundObjectId}"); ok = false; }
            if (!onEmpty.ReturnToSelect)
            { Debug.LogError("FAIL после промаха инструмент обязан стать «Курсором»"); ok = false; }

            // ЧУЖОЙ рисунок под курсором — тоже «мимо». Правило «внутрь привязанного» и правило
            // «по любому рисунку» расходятся ровно здесь и больше нигде.
            var onOther = Click(CanvasToolKind.Drawing, Bound, Other);
            if (onOther.Action != CanvasClickAction.ReleaseBinding)
            {
                Debug.LogError($"FAIL клик по ДРУГОМУ рисунку при активной привязке: {onOther.Action}, " +
                               "ждём ReleaseBinding — привязка адресует клик своему объекту, а не любому");
                ok = false;
            }

            Done(ok);
        }

        /// <summary>Клик внутрь привязанного рисунка — мазок, привязка держится.
        /// Убивает мутанта «возвращать ReleaseBinding и при попадании внутрь» (тогда рисовать вообще
        /// нечем) и мутанта «обнулять привязку после мазка» (тогда второй мазок промахивается).</summary>
        [ContextMenu("Self-Test: клик внутрь привязанного рисунка рисует")]
        public void SelfTestBoundDrawingInsideClickPaints()
        {
            bool ok = true;
            var r = Click(CanvasToolKind.Drawing, Bound, Bound);
            if (r.Action != CanvasClickAction.PaintExisting)
            { Debug.LogError($"FAIL клик внутрь привязанного рисунка: {r.Action}, ждём PaintExisting"); ok = false; }
            if (r.TargetObjectId != Bound)
            { Debug.LogError($"FAIL мазок адресован {r.TargetObjectId}, ждём {Bound}"); ok = false; }
            if (r.BoundObjectId != Bound)
            { Debug.LogError($"FAIL привязка после мазка: {r.BoundObjectId}, ждём {Bound} — иначе второй мазок промахнётся"); ok = false; }
            if (r.ReturnToSelect)
            { Debug.LogError("FAIL инструмент положил себя посреди рисования"); ok = false; }
            Done(ok);
        }

        /// <summary>Клик «Рисунком» без привязки по пустому месту — создаём холст И привязываемся к нему.
        /// Убивает мутанта «после создания привязки нет» (BindCreatedObject = false), при котором
        /// следующий клик создаёт ЕЩЁ один рисунок — то есть ровно старое поведение.</summary>
        [ContextMenu("Self-Test: рисунок по пустому месту создаётся и становится привязанным")]
        public void SelfTestUnboundDrawingOnEmptyCreatesAndBinds()
        {
            bool ok = true;
            var r = Click(CanvasToolKind.Drawing, null, null);
            if (r.Action != CanvasClickAction.CreateDrawing)
            { Debug.LogError($"FAIL «Рисунок» по пустому месту: {r.Action}, ждём CreateDrawing"); ok = false; }
            if (!r.BindCreatedObject)
            { Debug.LogError("FAIL созданный рисунок не привязан — следующий клик создаст второй"); ok = false; }
            if (r.ReturnToSelect)
            { Debug.LogError("FAIL инструмент положил себя сразу после создания — это и есть чинимый дефект"); ok = false; }
            Done(ok);
        }

        /// <summary>Клик «Рисунком» без привязки по СУЩЕСТВУЮЩЕМУ рисунку — мазок И привязка к нему.
        /// Убивает мутанта «привязывать только созданное»: разница между ним и правильным правилом
        /// видна лишь на следующем клике мимо, то есть в самом неочевидном месте.</summary>
        [ContextMenu("Self-Test: рисунок по существующему рисует и привязывается")]
        public void SelfTestUnboundDrawingOnExistingPaintsAndBinds()
        {
            bool ok = true;
            var r = Click(CanvasToolKind.Drawing, null, Other);
            if (r.Action != CanvasClickAction.PaintExisting)
            { Debug.LogError($"FAIL «Рисунок» по существующему рисунку: {r.Action}, ждём PaintExisting"); ok = false; }
            if (r.TargetObjectId != Other)
            { Debug.LogError($"FAIL мазок адресован {r.TargetObjectId}, ждём {Other}"); ok = false; }
            if (r.BoundObjectId != Other)
            { Debug.LogError($"FAIL существующий рисунок не стал привязанным: {r.BoundObjectId}"); ok = false; }
            Done(ok);
        }

        /// <summary>Заметка и картинка НЕ привязываются и кладут инструмент. Убивает мутанта
        /// «привязать и их» — тогда сломается «поставил заметку, кликнул мимо, поставил следующую»,
        /// а редактирование заметки инструмента «Заметка» не требует вовсе.</summary>
        [ContextMenu("Self-Test: заметка и картинка не привязываются")]
        public void SelfTestNoteAndImageDoNotBind()
        {
            bool ok = true;

            var note = Click(CanvasToolKind.Note, null, null);
            if (note.Action != CanvasClickAction.CreateNote)
            { Debug.LogError($"FAIL «Заметка»: {note.Action}, ждём CreateNote"); ok = false; }
            if (note.BindCreatedObject || note.BoundObjectId != null)
            { Debug.LogError("FAIL заметка привязалась — привязка существует только ради мазков"); ok = false; }
            if (!note.ReturnToSelect)
            { Debug.LogError("FAIL после заметки инструмент обязан стать «Курсором»"); ok = false; }

            var image = Click(CanvasToolKind.Image, null, null);
            if (image.Action != CanvasClickAction.CreateImage)
            { Debug.LogError($"FAIL «Изображение»: {image.Action}, ждём CreateImage"); ok = false; }
            if (image.BindCreatedObject || image.BoundObjectId != null)
            { Debug.LogError("FAIL картинка привязалась"); ok = false; }
            if (!image.ReturnToSelect)
            { Debug.LogError("FAIL после картинки инструмент обязан стать «Курсором»"); ok = false; }

            // Рисунок под курсором не должен превращать «Заметку» в мазок: инструмент решает первым.
            var noteOverDrawing = Click(CanvasToolKind.Note, null, Other);
            if (noteOverDrawing.Action != CanvasClickAction.CreateNote)
            { Debug.LogError($"FAIL «Заметка» поверх рисунка: {noteOverDrawing.Action}, ждём CreateNote"); ok = false; }

            Done(ok);
        }

        /// <summary>Подтверждение уже активного инструмента НЕ бросает мазок, а настоящая смена —
        /// бросает. Жалоба ДМ дословно: второй тычок быстрой пары пропадал, потому что
        /// EnterDrawingEditMode звал SetTool(Drawing), даже когда «Рисунок» уже был активен, и SetTool
        /// бросал мазок безусловно. Убивает мутанта «ShouldAbandonStroke всегда возвращает true» —
        /// то самое сегодняшнее поведение, из-за которого дефект и существует.</summary>
        [ContextMenu("Self-Test: мазок бросают только при настоящей смене инструмента")]
        public void SelfTestStrokeAbandonedOnlyOnRealToolChange()
        {
            bool ok = true;

            bool sameTool = CanvasToolOps.ShouldAbandonStroke(CanvasToolKind.Drawing, CanvasToolKind.Drawing);
            if (sameTool)
            {
                Debug.LogError("FAIL подтверждение того же инструмента («Рисунок» → «Рисунок») бросает мазок — " +
                               "это и есть баг «пропадает второй тычок быстрой пары»");
                ok = false;
            }

            bool realChange = CanvasToolOps.ShouldAbandonStroke(CanvasToolKind.Drawing, CanvasToolKind.Select);
            if (!realChange)
            {
                Debug.LogError("FAIL настоящая смена инструмента («Рисунок» → «Курсор») не бросает мазок — " +
                               "это сломало бы Esc посреди мазка и «клик мимо» привязанного рисунка");
                ok = false;
            }

            Done(ok);
        }

        /// <summary>«Курсор» и «Лупа» не вставляют и не рисуют ничего — их ветки остаются во вью.
        /// Убивает мутанта «пропустить Select в switch» (провал в ветку Drawing по умолчанию), при
        /// котором обычное выделение начало бы создавать рисунки.</summary>
        [ContextMenu("Self-Test: «Курсор» и «Лупа» ничего не создают")]
        public void SelfTestSelectToolNeverCreates()
        {
            bool ok = true;
            foreach (var tool in new[] { CanvasToolKind.Select, CanvasToolKind.Zoom })
            {
                // Под курсором нарочно ЛЕЖИТ рисунок и привязка нарочно активна: если правило
                // провалится в ветку Drawing, ответ будет PaintExisting, а не None.
                var r = Click(tool, Bound, Other);
                if (r.Action != CanvasClickAction.None)
                { Debug.LogError($"FAIL {tool}: {r.Action}, ждём None — эти два инструмента живут во вью"); ok = false; }
                if (r.ReturnToSelect)
                { Debug.LogError($"FAIL {tool} требует сменить инструмент"); ok = false; }
            }
            Done(ok);
        }
    }
}
