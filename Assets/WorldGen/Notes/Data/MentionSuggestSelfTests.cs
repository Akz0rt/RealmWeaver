using System.Collections.Generic;
using UnityEngine;
using WorldGen.Workspace.Data;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самопроверки для MentionSuggest. По форме — ровно CharacterOpsSelfTests.cs: обычный MonoBehaviour,
    /// методы SelfTest*, каждый под [ContextMenu] для запуска из Editor, и офлайн через Tools/notes-harness
    /// (`powershell -File sync.ps1`, затем `dotnet run -c Release -- selftests` из bash).
    ///
    /// Каждая фикстура построена так, чтобы правильное и неправильное правило РАСХОДИЛИСЬ — «по порядку
    /// упоминания» и «по алфавиту» в частности (задача 10a, п. 2): имена подобраны так, что сортировка по
    /// алфавиту дала бы другой порядок, чем сортировка по позиции последнего упоминания.
    /// </summary>
    public class MentionSuggestSelfTests : MonoBehaviour
    {
        static NotesPage MakePage(NotesDocument doc, PageGroup g, string name)
        {
            var p = new NotesPage { Name = name };
            g.Pages.Add(p);
            return p;
        }

        [ContextMenu("Self-Test: Empty Query Orders By Recency Then Recent")]
        public void SelfTestEmptyQueryOrdersByRecencyThenRecent()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            // Юлия и Антон — имена НАРОЧНО «встали не по алфавиту» относительно порядка упоминания:
            // алфавитный порядок был бы «Антон, Юлия», а верный (по позиции последнего упоминания) —
            // «Юлия, Антон». Совпадающая фикстура (где оба правила согласны) не различила бы мутанта.
            var antonFirst = MakePage(doc, g, "Антон");
            var yuliaLater = MakePage(doc, g, "Юлия");
            var recentOnly = MakePage(doc, g, "Виктор");
            var current = MakePage(doc, g, "Текущая страница");

            current.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 0,
                "Первым делом заходит " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, antonFirst.Id, "Антон") +
                ", а позже появляется " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, yuliaLater.Id, "Юлия") + "."));

            var recentIds = new List<string> { NotesLinkOps.KindPage + ":" + recentOnly.Id };

            var result = MentionSuggest.Rank(doc, current, null, "", recentIds, 3);

            // Мутант: порядок упоминаний игнорируется (тогда получится Антон, Юлия, Виктор — appearance
            // order вместо recency order).
            if (result.Count != 3 || result[0].Id != yuliaLater.Id || result[1].Id != antonFirst.Id || result[2].Id != recentOnly.Id)
            {
                string actual = result.Count == 0 ? "<пусто>" : string.Join(", ", result.ConvertAll(c => c.Name));
                Debug.LogError($"FAIL: пустой запрос дал «{actual}», хотели «Юлия, Антон, Виктор» (позже упомянутый — выше, затем недавние)");
                ok = false;
            }

            // Мутант: recentIds не добавляются вовсе (тогда результат — только Юлия, Антон, без Виктора).
            if (!result.Exists(c => c.Id == recentOnly.Id))
            { Debug.LogError("FAIL: недавно вставленный (recentIds) отсутствует в выдаче"); ok = false; }

            Debug.Log(ok ? "Self-Test Empty Query Orders By Recency Then Recent: PASS" : "Self-Test Empty Query Orders By Recency Then Recent: FAIL");
        }

        [ContextMenu("Self-Test: No Duplicate Between Mentioned And Recent")]
        public void SelfTestNoDuplicateBetweenMentionedAndRecent()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var both = MakePage(doc, g, "Ольга");
            var current = MakePage(doc, g, "Текущая страница");
            current.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 0,
                "Заходит " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, both.Id, "Ольга") + "."));

            // Тот же объект лежит и в recentIds — мутант: склейка списков без удаления повторов даст
            // Ольгу дважды.
            var recentIds = new List<string> { NotesLinkOps.KindPage + ":" + both.Id };

            var result = MentionSuggest.Rank(doc, current, null, "", recentIds, 5);

            int count = result.FindAll(c => c.Id == both.Id).Count;
            if (count != 1)
            { Debug.LogError($"FAIL: объект и упомянут, и в recentIds — вхождений {count.ToString(System.Globalization.CultureInfo.InvariantCulture)}, хотели ровно 1"); ok = false; }

            Debug.Log(ok ? "Self-Test No Duplicate Between Mentioned And Recent: PASS" : "Self-Test No Duplicate Between Mentioned And Recent: FAIL");
        }

        [ContextMenu("Self-Test: Limit Holds Exactly")]
        public void SelfTestLimitHoldsExactly()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var a = MakePage(doc, g, "Первый");
            var b = MakePage(doc, g, "Второй");
            var c = MakePage(doc, g, "Третий");
            var current = MakePage(doc, g, "Текущая страница");
            current.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 0,
                NotesLinkOps.MakeToken(NotesLinkOps.KindPage, a.Id, "Первый") + " и " +
                NotesLinkOps.MakeToken(NotesLinkOps.KindPage, b.Id, "Второй") + " и " +
                NotesLinkOps.MakeToken(NotesLinkOps.KindPage, c.Id, "Третий") + "."));

            // Мутант: предел игнорируется — вернутся все три кандидата вместо двух.
            var result = MentionSuggest.Rank(doc, current, null, "", null, 2);
            if (result.Count != 2)
            { Debug.LogError($"FAIL: limit=2 при трёх кандидатах дал {result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}, хотели ровно 2"); ok = false; }

            Debug.Log(ok ? "Self-Test Limit Holds Exactly: PASS" : "Self-Test Limit Holds Exactly: FAIL");
        }

        [ContextMenu("Self-Test: Current Page Never Suggests Itself")]
        public void SelfTestCurrentPageNeverSuggestsItself()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var current = MakePage(doc, g, "Тихая Гавань");
            // Ссылка сама на себя — случайно попавшая в текст (например, после копипаста).
            current.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 0,
                "Место действия — " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, current.Id, "Тихая Гавань") + "."));

            // Второй, ГЕНУИННЫЙ элемент recentIds — чтобы «текущая страница исключена» и «recentIds
            // вообще работают» проверялись раздельно: если бы мутант просто занулил AppendRecent целиком,
            // отсутствие currentId в выдаче ничего бы не доказало (его там и так не было бы). «Прочий»
            // должен остаться в выдаче — это позитивная сторона того же теста.
            var other = MakePage(doc, g, "Прочий");
            var recentIds = new List<string> { NotesLinkOps.KindPage + ":" + current.Id, NotesLinkOps.KindPage + ":" + other.Id };

            // Мутант: guard в MentionedOnPage/TrackSpans снят — пустой запрос вернёт саму страницу как
            // «упомянутую на этой странице».
            var emptyResult = MentionSuggest.Rank(doc, current, null, "", recentIds, 5);
            if (emptyResult.Exists(cand => cand.Kind == NotesLinkOps.KindPage && cand.Id == current.Id))
            { Debug.LogError("FAIL: пустой запрос предложил текущую страницу саму себе"); ok = false; }

            // Мутант (отдельный от предыдущего): guard в AppendRecent снят — recentIds содержит id самой
            // текущей страницы («page:» + current.Id), и без собственной проверки AppendRecent её бы
            // резолвнул как обычную страницу (она валидна в doc) и добавил.
            if (!emptyResult.Exists(cand => cand.Id == other.Id))
            { Debug.LogError("FAIL: recentIds отбросил не только саму страницу, но и генуинный недавний элемент — guard слишком широк или recentIds сломан целиком"); ok = false; }

            // Тот же guard должен держаться и при непустом запросе, который совпал бы по имени.
            var queryResult = MentionSuggest.Rank(doc, current, null, "Тихая", null, 5);
            if (queryResult.Exists(cand => cand.Kind == NotesLinkOps.KindPage && cand.Id == current.Id))
            { Debug.LogError("FAIL: запрос по собственному имени предложил текущую страницу саму себе"); ok = false; }

            Debug.Log(ok ? "Self-Test Current Page Never Suggests Itself: PASS" : "Self-Test Current Page Never Suggests Itself: FAIL");
        }

        [ContextMenu("Self-Test: Linked Page Pin Is A Mention")]
        public void SelfTestLinkedPagePinIsAMention()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var pinned = MakePage(doc, g, "Ржавый Якорь");
            var current = MakePage(doc, g, "Текущая страница");

            // 📄-привязка БЕЗ единого инлайн-токена — единственный след этой страницы здесь и есть
            // LinkedPageId, ровно как NotesDocOps.BlockReferencesPage её и считает.
            var pinRow = NotesDocOps.NewBlock(BlockKind.Item, 1, "см. заметку");
            pinRow.LinkedPageId = pinned.Id;
            current.Blocks.Add(pinRow);

            // Фикстура сначала доказывает себе, что «Упомянута в» вообще считает эту привязку
            // упоминанием — иначе тест ничего бы не проверял (rule 1: два способа искать не должны
            // расходиться, и здесь мы опираемся на то, что FindBacklinks — источник истины).
            var backlinks = NotesDocOps.FindBacklinks(doc, pinned.Id);
            if (backlinks.Count == 0)
            { Debug.LogError("FAIL фикстуры: NotesDocOps.FindBacklinks не считает 📄-привязку упоминанием — тест ничего не докажет"); ok = false; }

            // Мутант: обработка LinkedPageId удалена из MentionSuggest — пустой запрос не найдёт
            // «Ржавый Якорь» вовсе, хотя «Упомянута в» её находит.
            var result = MentionSuggest.Rank(doc, current, null, "", null, 5);
            if (!result.Exists(c => c.Id == pinned.Id))
            { Debug.LogError("FAIL: страница, привязанная через 📄 (LinkedPageId) без инлайн-токена, не попала в подсказки пустого запроса"); ok = false; }

            Debug.Log(ok ? "Self-Test Linked Page Pin Is A Mention: PASS" : "Self-Test Linked Page Pin Is A Mention: FAIL");
        }

        [ContextMenu("Self-Test: Linked Page And Inline Token Dedup To One")]
        public void SelfTestLinkedPageAndInlineTokenDedupToOne()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var target = MakePage(doc, g, "Тихая Гавань");
            var current = MakePage(doc, g, "Текущая страница");

            // ОДНА строка несёт ОБА механизма разом — 📄-привязку И инлайн-токен на ту же страницу.
            var row = NotesDocOps.NewBlock(BlockKind.Item, 1,
                "Заходит " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, target.Id, "Тихая Гавань") + ".");
            row.LinkedPageId = target.Id;
            current.Blocks.Add(row);

            // Мутант: два механизма пишут в РАЗНЫЕ order/lastPos (например, TrackLinkedPage со своим
            // отдельным списком вместо общего) — тогда вхождений станет 2 вместо 1.
            var result = MentionSuggest.Rank(doc, current, null, "", null, 5);
            int count = result.FindAll(c => c.Id == target.Id).Count;
            if (count != 1)
            { Debug.LogError($"FAIL: одна строка с 📄-привязкой И инлайн-токеном на ту же страницу дала {count.ToString(System.Globalization.CultureInfo.InvariantCulture)} вхождений в подсказках, хотели ровно 1"); ok = false; }

            Debug.Log(ok ? "Self-Test Linked Page And Inline Token Dedup To One: PASS" : "Self-Test Linked Page And Inline Token Dedup To One: FAIL");
        }

        [ContextMenu("Self-Test: Non-Empty Query Finds World Object")]
        public void SelfTestNonEmptyQueryFindsWorldObject()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);
            var current = MakePage(doc, g, "Текущая страница");

            var world = new List<WorldObjectRef>
            {
                new WorldObjectRef { Kind = WorldRefKind.Poi, Id = "poi-1", Name = "Ржавый Якорь", KindLabel = "город" },
            };

            // Объект нет ни в упоминаниях (страница пуста), ни в recentIds — только обычный поиск по имени.
            var result = MentionSuggest.Rank(doc, current, world, "Ржав", null, 5);
            if (!result.Exists(cand => cand.Kind == "poi" && cand.Id == "poi-1" && cand.Name == "Ржавый Якорь"))
            {
                string actual = result.Count == 0 ? "<пусто>" : string.Join(", ", result.ConvertAll(c => c.Kind + ":" + c.Name));
                Debug.LogError($"FAIL: непустой запрос «Ржав» не нашёл объект мира — выдача «{actual}»");
                ok = false;
            }

            Debug.Log(ok ? "Self-Test Non-Empty Query Finds World Object: PASS" : "Self-Test Non-Empty Query Finds World Object: FAIL");
        }

        [ContextMenu("Self-Test: Non-Empty Query Mentioned Wins Tie And Subtitle Matches")]
        public void SelfTestNonEmptyQueryMentionedWinsTieAndSubtitleMatches()
        {
            bool ok = true;

            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Персонажи" };
            doc.Groups.Add(g);

            // Два персонажа с ИМЕНЕМ, начинающимся одинаково («Кузнец Ольга» и «Кузнец Борис») — оба дают
            // NamePrefix на запрос «кузнец», то есть равный ранг совпадения. Только «Ольга» упомянута на
            // текущей странице — правило «упомянутые всё равно выше при равном совпадении» должно вывести
            // её первой. БОРИС ДОБАВЛЕН В ДОКУМЕНТ ПЕРВЫМ, ОЛЬГА — ВТОРОЙ: без бонуса за упоминание
            // (только по очереди обхода страниц) первым оказался бы Борис, и тест не отличил бы правильный
            // тай-брейк от «побеждает тот, кто раньше лежит в документе» — так уже проверено вручную.
            var boris = MakePage(doc, g, "Кузнец Борис");
            boris.Character = new CharacterCard { Who = "кузнец" };
            var olga = MakePage(doc, g, "Кузнец Ольга");
            olga.Character = new CharacterCard { Who = "кузнец" };

            var current = MakePage(doc, g, "Текущая страница");
            current.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Prose, 0,
                "Заходит " + NotesLinkOps.MakeToken(NotesLinkOps.KindPage, olga.Id, "Кузнец Ольга") + "."));

            var result = MentionSuggest.Rank(doc, current, null, "кузнец", null, 5);
            int idxOlga = result.FindIndex(c => c.Id == olga.Id);
            int idxBoris = result.FindIndex(c => c.Id == boris.Id);
            if (idxOlga < 0 || idxBoris < 0 || idxOlga > idxBoris)
            { Debug.LogError($"FAIL: при равном совпадении упомянутая на странице (Ольга, индекс {idxOlga}) должна идти раньше неупомянутой (Борис, индекс {idxBoris})"); ok = false; }

            // Подпись (роль персонажа) тоже участвует в поиске: запрос по слову «кузнец» находит и Бориса
            // тоже (роль совпадает, хоть имя и не содержит «кузнец» иначе, чем как префикс) — здесь же
            // проверяем отдельно поиск ЧИСТО по подписи, когда имя вообще не совпадает.
            var stranger = MakePage(doc, g, "Просто Иван");
            stranger.Character = new CharacterCard { Who = "трактирщик" };
            var bySubtitle = MentionSuggest.Rank(doc, current, null, "трактирщик", null, 5);
            if (!bySubtitle.Exists(c => c.Id == stranger.Id))
            { Debug.LogError("FAIL: поиск по подписи (роли персонажа) не нашёл совпадение, хотя имя не совпадает вовсе"); ok = false; }

            Debug.Log(ok ? "Self-Test Non-Empty Query Mentioned Wins Tie And Subtitle Matches: PASS" : "Self-Test Non-Empty Query Mentioned Wins Tie And Subtitle Matches: FAIL");
        }
    }
}
