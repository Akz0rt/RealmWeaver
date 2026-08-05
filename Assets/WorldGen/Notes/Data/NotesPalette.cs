namespace WorldGen.Notes.Data
{
    /// <summary>Цвет палитры БЕЗ UnityEngine. Чистый слой компилируется офлайн-харнессом, где
    /// UnityEngine.Color не существует вовсе; перевод в Color32 делает слой отрисовки.</summary>
    public readonly struct PaletteColor
    {
        public readonly byte R, G, B;
        public PaletteColor(byte r, byte g, byte b) { R = r; G = g; B = b; }
    }

    /// <summary>Одна палитра на рамку карточки и на карандаш.
    ///
    /// СПИСОК ДОПОЛНЯЕТСЯ ТОЛЬКО С КОНЦА. В файле проекта лежит ИНДЕКС, а не цвет, поэтому
    /// перестановка перекрасит уже сохранённые карточки чужих проектов.</summary>
    public static class NotesPalette
    {
        static readonly PaletteColor[] Frames =
        {
            new PaletteColor(120, 120, 128),  // 0 нейтральный — значение по умолчанию
            new PaletteColor(190,  75,  70),  // 1 красный
            new PaletteColor(205, 130,  55),  // 2 оранжевый
            new PaletteColor(215, 190,  75),  // 3 жёлтый
            new PaletteColor( 95, 160,  95),  // 4 зелёный
            new PaletteColor( 80, 145, 190),  // 5 голубой
            new PaletteColor( 75,  90, 175),  // 6 синий
            new PaletteColor(140,  90, 175),  // 7 фиолетовый
            new PaletteColor( 35,  35,  40),  // 8 чернильный — цвет карандаша по умолчанию
        };

        public static readonly string[] Names =
        { "Нейтральный", "Красный", "Оранжевый", "Жёлтый", "Зелёный", "Голубой", "Синий", "Фиолетовый", "Чернильный" };

        public const int NeutralIndex = 0;

        /// <summary>Карандаш до этого арка рисовал почти чёрным (20,20,20). Без такого цвета в
        /// палитре набросок на белом растре стал бы блёклым серым, и вернуть чёрный было бы нечем.</summary>
        public const int InkIndex = 8;
        public static int Count => Frames.Length;

        /// <summary>Индекс вне списка — это файл из будущей версии или битые данные, а не повод
        /// падать: возвращается нейтральный цвет.</summary>
        public static PaletteColor At(int index)
            => index < 0 || index >= Frames.Length ? Frames[NeutralIndex] : Frames[index];

        /// <summary>Относительная яркость, 0…1. Коэффициенты не равные: глаз видит зелёный много
        /// ярче синего, и простое среднее ошибается на насыщенных цветах в обе стороны.</summary>
        public static float Luminance(PaletteColor c)
            => (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

        /// <summary>Порог 0.6, а не 0.5: при 0.55 оранжевый (0.551) и зелёный (0.555) легли бы
        /// вплотную к границе и переворачивались бы от любой правки цвета.</summary>
        public const float DarkTextThreshold = 0.6f;

        public static bool PrefersDarkText(PaletteColor c) => Luminance(c) >= DarkTextThreshold;
    }
}
