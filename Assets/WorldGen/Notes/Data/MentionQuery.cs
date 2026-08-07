namespace WorldGen.Notes.Data
{
    /// <summary>
    /// ЧИСТАЯ логика «есть ли сейчас активный запрос по «@», и если да — какой». Единственный кусок
    /// Задачи 10б, который можно протестировать офлайн (Tools/notes-harness), без UnityEngine: всё
    /// остальное — TMP_InputField, сам попап, его позиционирование на экране — принципиально не пур и
    /// живёт в Notes/Rendering (MentionPopup.cs, DocKeyboardController.cs).
    ///
    /// ПРАВИЛО: ближайшее «@» НАЗАД от каретки, пока не встретился пробельный символ. Разбор нарочно НЕ
    /// требует пробела или начала строки ПЕРЕД «@» — бриф рулинга 1 говорит «"@", набранный в строке,
    /// открывает список» без оговорок о позиции, и рулинг 4 сам называет живой пример, где «@» стоит
    /// СРАЗУ после слова без пробела («почта@…» — обычный e-mail): защита от того, чтобы такой «@» стал
    /// ловушкой, — не в том, что список вообще не откроется, а в том, что ПРОБЕЛ, набранный до выбора,
    /// его закрывает. Эта защита встроена прямо сюда: как только между «@» и кареткой оказывается
    /// пробельный символ, поиск обрывается неудачей — что и значит «активного запроса больше нет», то
    /// есть «список закрылся», без отдельного «closed»-состояния снаружи.
    ///
    /// char.IsWhiteSpace, а не только ' ' — перенос строки внутри текста блока сегодня недостижим
    /// (DocBlockView.Field.onValidateInput отклоняет \n/\r/\t), но пробельных символов в Unicode больше
    /// одного, и дешевле не сужать это до буквального пробела с самого начала.
    /// </summary>
    public static class MentionQuery
    {
        /// <summary>caret — позиция каретки в ИСХОДНОЙ строке (Field.richText=false в DocBlockView делает
        /// позицию каретки и строковый индекс одним и тем же числом — см. DocBlockView.BuildField). Строго
        /// вне диапазона [0, text.Length] безопасно клэмпится, а не бросает исключение: вызывающая сторона
        /// (DocKeyboardController) читает caret с живого поля и не обязана доказывать его в диапазоне
        /// заранее.</summary>
        public static bool TryFind(string text, int caret, out int atIndex, out string query)
        {
            atIndex = -1;
            query = null;
            if (string.IsNullOrEmpty(text)) return false;

            int c = caret;
            if (c < 0) c = 0;
            if (c > text.Length) c = text.Length;

            for (int i = c - 1; i >= 0; i--)
            {
                char ch = text[i];
                if (ch == '@')
                {
                    atIndex = i;
                    query = text.Substring(i + 1, c - i - 1);
                    return true;
                }
                if (char.IsWhiteSpace(ch)) return false;
            }
            return false;
        }
    }
}
