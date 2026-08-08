using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самопроверки для CharacterOps. По форме — ровно NotesLinkOpsSelfTests.cs: обычный MonoBehaviour,
    /// методы SelfTest*, каждый под [ContextMenu] для запуска из Editor, и офлайн через Tools/notes-harness
    /// (`powershell -File sync.ps1`, затем `dotnet run -c Release -- selftests` из bash).
    ///
    /// Каждый провал печатает ЧТО получилось и ЧЕГО хотели. Утверждения нацелены на правило, которое
    /// сломало бы изменение, а не на производное число.
    /// </summary>
    public class CharacterOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Plain Page Is Not A Character")]
        public void SelfTestPlainPageIsNotCharacter()
        {
            bool ok = true;

            // Мутант, которого ловит этот тест: IsCharacter возвращает true для любой страницы.
            var page = new NotesPage { Name = "Тихая Гавань" };
            if (CharacterOps.IsCharacter(page))
            { Debug.LogError("FAIL: страница без карточки сочтена персонажем"); ok = false; }
            page.Character = new CharacterCard();
            if (!CharacterOps.IsCharacter(page))
            { Debug.LogError("FAIL: страница с карточкой не сочтена персонажем"); ok = false; }

            Debug.Log(ok ? "Self-Test Plain Page Is Not A Character: PASS" : "Self-Test Plain Page Is Not A Character: FAIL");
        }

        [ContextMenu("Self-Test: Copy Card Does Not Alias")]
        public void SelfTestCopyCardDoesNotAlias()
        {
            bool ok = true;

            // Мутант: CopyCard возвращает тот же экземпляр (return card).
            // Фикстура построена так, чтобы правильное и неправильное правило РАСХОДИЛИСЬ: после копии
            // правим ОРИГИНАЛ и смотрим на копию.
            var portrait = new byte[] { 1, 2, 3 };
            var card = new CharacterCard
            {
                Who = "кузнец", Where = "Тихая Гавань", Wants = "выкупить долг",
                HowToPlay = "вытирает руки о фартук", Portrait = portrait,
            };
            var copy = CharacterOps.CopyCard(card);

            card.Who = "ИЗМЕНЕНО";
            card.Wants = "ИЗМЕНЕНО";
            if (copy.Who != "кузнец")
            { Debug.LogError($"FAIL: Who протёк в копию ({copy.Who})"); ok = false; }
            if (copy.Wants != "выкупить долг")
            { Debug.LogError($"FAIL: Wants протёк в копию ({copy.Wants})"); ok = false; }
            if (copy.Where != "Тихая Гавань" || copy.HowToPlay != "вытирает руки о фартук")
            { Debug.LogError($"FAIL: копия потеряла поле (Where={copy.Where}, HowToPlay={copy.HowToPlay})"); ok = false; }
            // Байты портрета РАЗДЕЛЯЮТСЯ намеренно — портрет заменяется целиком, никогда не правится
            // на месте (тот же договор, что у DocBlock.ImageBytes).
            if (!ReferenceEquals(copy.Portrait, portrait))
            { Debug.LogError("FAIL: портрет скопирован, хотя должен разделяться"); ok = false; }

            Debug.Log(ok ? "Self-Test Copy Card Does Not Alias: PASS" : "Self-Test Copy Card Does Not Alias: FAIL");
        }

        [ContextMenu("Self-Test: Copy Null Card")]
        public void SelfTestCopyNullCard()
        {
            bool ok = true;

            // Мутант: CopyCard(null) кидает исключение вместо null.
            if (CharacterOps.CopyCard(null) != null)
            { Debug.LogError("FAIL: копия отсутствующей карточки не null"); ok = false; }

            Debug.Log(ok ? "Self-Test Copy Null Card: PASS" : "Self-Test Copy Null Card: FAIL");
        }

        [ContextMenu("Self-Test: Group Found By Flag Not Title")]
        public void SelfTestGroupFoundByFlagNotTitle()
        {
            bool ok = true;

            // Мутант: EnsureCharactersGroup ищет группу по названию, а не по флагу.
            var doc = new NotesDocument();
            var renamed = new PageGroup { Title = "Мои люди", IsCharacters = true };
            doc.Groups.Add(renamed);

            var found = CharacterOps.EnsureCharactersGroup(doc);
            if (!ReferenceEquals(found, renamed))
            { Debug.LogError("FAIL: переименованная группа персонажей не найдена"); ok = false; }
            if (doc.Groups.Count != 1)
            { Debug.LogError("FAIL: заведена вторая группа персонажей"); ok = false; }

            Debug.Log(ok ? "Self-Test Group Found By Flag Not Title: PASS" : "Self-Test Group Found By Flag Not Title: FAIL");
        }

        [ContextMenu("Self-Test: Group Created When Missing")]
        public void SelfTestGroupCreatedWhenMissing()
        {
            bool ok = true;

            // Мутант: EnsureCharactersGroup не помечает созданную группу флагом.
            var doc = new NotesDocument();
            var g = CharacterOps.EnsureCharactersGroup(doc);
            if (g == null || !g.IsCharacters)
            { Debug.LogError("FAIL: созданная группа не помечена флагом"); ok = false; }
            if (g.Title != CharacterOps.CharactersGroupTitle)
            { Debug.LogError("FAIL: у созданной группы чужое название"); ok = false; }
            if (doc.Groups.Count != 1 || !ReferenceEquals(doc.Groups[0], g))
            { Debug.LogError("FAIL: группа не добавлена в документ"); ok = false; }

            Debug.Log(ok ? "Self-Test Group Created When Missing: PASS" : "Self-Test Group Created When Missing: FAIL");
        }

        [ContextMenu("Self-Test: Create Character Files Into Group With Card")]
        public void SelfTestCreateCharacterFilesIntoGroupWithCard()
        {
            bool ok = true;

            // Мутант: CreateCharacter кладёт страницу без карточки (тогда она не попадёт в раздел).
            var doc = new NotesDocument();
            var page = CharacterOps.CreateCharacter(doc, "Ольга Медная");
            if (page == null)
            { Debug.LogError("FAIL: страница не создана"); ok = false; }
            else
            {
                if (page.Name != "Ольга Медная")
                { Debug.LogError($"FAIL: имя не проставлено ({page.Name})"); ok = false; }
                if (!CharacterOps.IsCharacter(page))
                { Debug.LogError("FAIL: у созданного персонажа нет карточки"); ok = false; }

                var group = CharacterOps.EnsureCharactersGroup(doc);
                if (group.Pages.Count != 1 || !ReferenceEquals(group.Pages[0], page))
                { Debug.LogError("FAIL: страница не легла в группу персонажей"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Create Character Files Into Group With Card: PASS" : "Self-Test Create Character Files Into Group With Card: FAIL");
        }

        [ContextMenu("Self-Test: Make Character Keeps Existing Card")]
        public void SelfTestMakeCharacterKeepsExistingCard()
        {
            bool ok = true;

            // Мутант: MakeCharacter затирает уже существующую карточку.
            // Фикстура: у страницы УЖЕ есть заполненная карточка — правило «создать пустую» и правило
            // «не трогать имеющуюся» здесь расходятся.
            var page = new NotesPage { Character = new CharacterCard { Who = "кузнец" } };
            if (CharacterOps.MakeCharacter(page))
            { Debug.LogError("FAIL: назначение сообщило об изменении, хотя карточка была"); ok = false; }
            if (page.Character.Who != "кузнец")
            { Debug.LogError($"FAIL: имеющаяся карточка затёрта ({page.Character.Who})"); ok = false; }

            var plain = new NotesPage();
            if (!CharacterOps.MakeCharacter(plain))
            { Debug.LogError("FAIL: назначение не сообщило об изменении"); ok = false; }
            if (!CharacterOps.IsCharacter(plain))
            { Debug.LogError("FAIL: карточка не появилась"); ok = false; }

            Debug.Log(ok ? "Self-Test Make Character Keeps Existing Card: PASS" : "Self-Test Make Character Keeps Existing Card: FAIL");
        }

        [ContextMenu("Self-Test: Remove Card Keeps Page")]
        public void SelfTestRemoveCardKeepsPage()
        {
            bool ok = true;

            // Мутант: RemoveCard удаляет страницу целиком, а не только карточку.
            var doc = new NotesDocument();
            var page = CharacterOps.CreateCharacter(doc, "Ольга Медная");
            page.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 1, "живёт у пристани"));

            if (!CharacterOps.RemoveCard(page))
            { Debug.LogError("FAIL: снятие не сообщило об изменении"); ok = false; }
            if (CharacterOps.IsCharacter(page))
            { Debug.LogError("FAIL: карточка осталась"); ok = false; }
            if (page.Blocks.Count != 1 || page.Blocks[0].Text != "живёт у пристани")
            { Debug.LogError("FAIL: снятие карточки потеряло текст страницы"); ok = false; }
            if (CharacterOps.EnsureCharactersGroup(doc).Pages.Count != 1)
            { Debug.LogError("FAIL: страница исчезла из группы"); ok = false; }
            if (CharacterOps.RemoveCard(page))
            { Debug.LogError("FAIL: повторное снятие сообщило об изменении"); ok = false; }

            Debug.Log(ok ? "Self-Test Remove Card Keeps Page: PASS" : "Self-Test Remove Card Keeps Page: FAIL");
        }

        [ContextMenu("Self-Test: Is Character Link")]
        public void SelfTestIsCharacterLink()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var group = new PageGroup();
            doc.Groups.Add(group);
            var hero = new NotesPage { Name = "Ольга Медная", Character = new CharacterCard() };
            var plain = new NotesPage { Name = "Тихая Гавань" };
            group.Pages.Add(hero);
            group.Pages.Add(plain);

            // Мутант: IsCharacterLink игнорирует kind и просто ищет id по всем страницам.
            // Фикстура: id персонажа существует, но ссылка помечена как "poi" — правило «только page
            // может быть персонажем» и правило «нашли страницу — значит персонаж» здесь расходятся.
            if (CharacterOps.IsCharacterLink(doc, "poi", hero.Id))
            { Debug.LogError("FAIL: ссылка вида poi на id персонажа сочтена персонажем"); ok = false; }

            if (!CharacterOps.IsCharacterLink(doc, NotesLinkOps.KindPage, hero.Id))
            { Debug.LogError("FAIL: ссылка на страницу-персонажа не распознана"); ok = false; }
            if (CharacterOps.IsCharacterLink(doc, NotesLinkOps.KindPage, plain.Id))
            { Debug.LogError("FAIL: ссылка на обычную страницу сочтена персонажем"); ok = false; }
            if (CharacterOps.IsCharacterLink(doc, NotesLinkOps.KindPage, "нет-такого-id"))
            { Debug.LogError("FAIL: ссылка на несуществующую страницу сочтена персонажем"); ok = false; }
            if (CharacterOps.IsCharacterLink(null, NotesLinkOps.KindPage, hero.Id))
            { Debug.LogError("FAIL: null-документ не пережит"); ok = false; }

            Debug.Log(ok ? "Self-Test Is Character Link: PASS" : "Self-Test Is Character Link: FAIL");
        }

        [ContextMenu("Self-Test: Null Document Is Survived")]
        public void SelfTestNullDocumentIsSurvived()
        {
            bool ok = true;

            // Мутант: EnsureCharactersGroup лишился проверки null-документа — бросает NullReferenceException
            // вместо возврата null, как это делает NotesDocOps.EnsureReferenceGroup.
            if (CharacterOps.EnsureCharactersGroup(null) != null)
            { Debug.LogError("FAIL: EnsureCharactersGroup(null) вернул не null"); ok = false; }

            // Мутант: CreateCharacter не проверяет null-группу и падает на следующей строке.
            if (CharacterOps.CreateCharacter(null, "Ольга Медная") != null)
            { Debug.LogError("FAIL: CreateCharacter(null, ...) вернул не null"); ok = false; }

            Debug.Log(ok ? "Self-Test Null Document Is Survived: PASS" : "Self-Test Null Document Is Survived: FAIL");
        }
    }
}
