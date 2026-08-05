namespace WorldGen.Notes.Data
{
    /// <summary>Размер ТЕКСТА карточки. Заголовок не меняется — у него свой постоянный кегль.
    ///
    /// MEDIUM ОБЪЯВЛЕН НУЛЁМ НАМЕРЕННО, и это не стиль. Файлы версии 14 поля не содержат, при
    /// чтении получается 0, и ноль обязан означать прежний вид карточки. Порядок кнопок на экране
    /// задаётся отдельно — «Малый / Средний / Крупный».</summary>
    public enum CardFontSize { Medium = 0, Small = 1, Large = 2 }

    /// <summary>Размеры «обвязки» карточки: рамка, шапка с заголовком, скругление.</summary>
    public static class CardChrome
    {
        public const float BorderPx = 3f;
        public const float HeaderHeightPx = 22f;
        public const float CornerRadiusPx = 8f;
        public const float TitlePointSize = 14f;

        public static bool HasTitle(string title) => !string.IsNullOrWhiteSpace(title);

        /// <summary>Пустой заголовок прячет шапку ТОЛЬКО в просмотре. В режиме правки шапка
        /// остаётся, иначе по ней некуда кликнуть, чтобы заголовок вообще появился.</summary>
        public static float HeaderHeight(bool hasTitle, bool editable)
            => !editable && !hasTitle ? 0f : HeaderHeightPx;

        public static float BodyPointSize(CardFontSize size)
        {
            switch (size)
            {
                case CardFontSize.Small: return 10f;
                case CardFontSize.Large: return 16f;
                default: return 12f;
            }
        }
    }
}
