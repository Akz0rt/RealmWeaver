using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Последний выбор ДМ — цвет и толщина карандаша, цвет рамки и кегль карточки, — чтобы
    /// следующий объект родился таким же. Живёт в PlayerPrefs, а не в файле проекта: это настройка
    /// РУКИ, а не мира, и переезжать вместе с .dndproj к другому ДМ ей незачем.
    ///
    /// Читается только из методов и никогда из инициализатора поля: PlayerPrefs в инициализаторе
    /// выполняется до того, как Unity его поднимет.
    ///
    /// СВОЙСТВА НАЗВАНЫ НЕ ТАК, КАК ИХ ТИПЫ. Свойство `BrushWidth` типа `BrushWidth` затенило бы имя
    /// типа внутри этого класса, и `BrushWidth.Medium` перестало бы компилироваться — в этом проекте
    /// на такое затенение натыкались трижды.</summary>
    public static class NotesUserPrefs
    {
        const string BrushColorKey = "notes.brush.color";
        const string BrushWidthKey = "notes.brush.width";
        const string CardFrameKey  = "notes.card.frame";
        const string CardFontKey   = "notes.card.font";

        /// <summary>Карандаш по умолчанию — чернильный, тот же почти чёрный, каким доска рисовала до
        /// этого арка.</summary>
        public static int BrushColorIndex
        {
            get => PlayerPrefs.GetInt(BrushColorKey, NotesPalette.InkIndex);
            set { PlayerPrefs.SetInt(BrushColorKey, value); PlayerPrefs.Save(); }
        }

        public static BrushWidth BrushStroke
        {
            get => (BrushWidth)PlayerPrefs.GetInt(BrushWidthKey, (int)BrushWidth.Medium);
            set { PlayerPrefs.SetInt(BrushWidthKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Рамка по умолчанию — нейтральная: первый запуск обязан дать карточку прежнего вида.</summary>
        public static int CardFrameColorIndex
        {
            get => PlayerPrefs.GetInt(CardFrameKey, NotesPalette.NeutralIndex);
            set { PlayerPrefs.SetInt(CardFrameKey, value); PlayerPrefs.Save(); }
        }

        public static CardFontSize CardFont
        {
            get => (CardFontSize)PlayerPrefs.GetInt(CardFontKey, (int)CardFontSize.Medium);
            set { PlayerPrefs.SetInt(CardFontKey, (int)value); PlayerPrefs.Save(); }
        }
    }
}
