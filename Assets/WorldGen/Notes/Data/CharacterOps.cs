using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>Всё, что значит «страница — персонаж». Без UnityEngine: файл собирается
    /// офлайн-харнессом Tools/notes-harness.</summary>
    public static class CharacterOps
    {
        public const string CharactersGroupTitle = "Персонажи";

        /// <summary>Единственный предикат «это персонаж» на весь проект. Один предикат, а не
        /// перечисление видов страниц — см. CharacterCard.</summary>
        public static bool IsCharacter(NotesPage page) => page != null && page.Character != null;

        /// <summary>Копия карточки для снимка отмены. Байты портрета РАЗДЕЛЯЮТСЯ — портрет
        /// заменяется целиком, никогда не правится на месте (тот же договор, что у
        /// DocBlock.ImageBytes в DocHistory.Copy).
        ///
        /// Поле, добавленное в CharacterCard и забытое здесь, не упадёт громко — оно молча
        /// сбросится в умолчание на ближайшей отмене ДМ. Это форма ошибки, которую никто не
        /// находит, пока данные уже не потеряны.</summary>
        public static CharacterCard CopyCard(CharacterCard card)
        {
            if (card == null) return null;
            return new CharacterCard
            {
                Who = card.Who,
                Where = card.Where,
                Wants = card.Wants,
                HowToPlay = card.HowToPlay,
                Portrait = card.Portrait,
            };
        }
    }
}
