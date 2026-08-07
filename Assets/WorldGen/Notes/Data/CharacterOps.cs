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

        /// <summary>Персонаж ли то, на что указывает ссылка [[kind:id|Имя]]. Только «page» вообще
        /// может им быть — у poi/building/room карточки нет и быть не может, так что для них ответ
        /// всегда false, а не null: «не удалось узнать» и «точно не персонаж» тут неразличимы для
        /// отрисовки и не должны различаться в сигнатуре.
        ///
        /// ИДЁТ В ТОТ ЖЕ ДОКУМЕНТ, НО НЕ ТЕМ ЖЕ ПУТЁМ, ЧТО ИМЯ — ПОПРАВКА К БОЛЕЕ РАННЕЙ ВЕРСИИ ЭТОГО
        /// КОММЕНТАРИЯ, которая ошибочно утверждала обратное. DocumentPageView.ResolveName ищет имя
        /// страницы через QuickOpen.ResolverFor (QuickOpen.cs:316-334), а тот для kind=="page" несёт
        /// СВОЙ СОБСТВЕННЫЙ обход doc.Groups/g.Pages — FindPage он не вызывает и никогда не вызывал.
        /// NotesDocOps.FindPage взят здесь не потому, что им уже пользуется ResolveName, а потому что
        /// это уже существующий публичный поиск страницы по id (NotesDocOps.cs:55), которым уже
        /// пользуется, например, MapScreenController — так что это переиспользование готового поиска,
        /// а не третий, отдельно написанный обход doc.Groups.</summary>
        public static bool IsCharacterLink(NotesDocument doc, string kind, string id)
            => kind == NotesLinkOps.KindPage && IsCharacter(NotesDocOps.FindPage(doc, id));

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

        /// <summary>Группа, куда падают новые персонажи. Ищется ПО ФЛАГУ, а не по названию — ДМ вправе
        /// переименовать её. Нет — заводится. Ровно тот же приём, что NotesDocOps.EnsureReferenceGroup.</summary>
        public static PageGroup EnsureCharactersGroup(NotesDocument doc)
        {
            if (doc == null) return null;
            foreach (var g in doc.Groups)
                if (g.IsCharacters) return g;

            var group = new PageGroup { Title = CharactersGroupTitle, IsCharacters = true };
            doc.Groups.Add(group);
            return group;
        }

        /// <summary>Новый персонаж: страница с пустой карточкой в группе персонажей. Без документа положить
        /// страницу некуда — возвращает null, а не выдумывает документ на ходу.</summary>
        public static NotesPage CreateCharacter(NotesDocument doc, string name)
        {
            var group = EnsureCharactersGroup(doc);
            if (group == null) return null;

            var page = new NotesPage { Name = string.IsNullOrWhiteSpace(name) ? "Новый персонаж" : name.Trim() };
            page.Character = new CharacterCard();
            group.Pages.Add(page);
            return page;
        }

        /// <summary>Делает персонажем уже написанную страницу. Страница ОСТАЁТСЯ ЛЕЖАТЬ ТАМ, ГДЕ ЛЕЖАЛА —
        /// в раздел её подтянет NavigatorTree. Это главный вход, а не запасной: у ДМ люди уже написаны
        /// обычными страницами, и без него все они остались бы невидимы для нового раздела.
        /// Возвращает false, если карточка уже была — повторное назначение не должно её затирать.</summary>
        public static bool MakeCharacter(NotesPage page)
        {
            if (page == null || page.Character != null) return false;
            page.Character = new CharacterCard();
            return true;
        }

        /// <summary>Снимает карточку. Страница цела, текст цел, ссылки на неё продолжают работать как
        /// ссылки на обычную страницу.</summary>
        public static bool RemoveCard(NotesPage page)
        {
            if (page == null || page.Character == null) return false;
            page.Character = null;
            return true;
        }
    }
}
