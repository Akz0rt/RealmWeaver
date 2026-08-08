using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class SheetSkillsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: навыки — владение от предыстории и от класса складываются в один набор")]
        public void SelfTestSkillsComeFromBothSources()
        {
            // «Атлетика» приходит от предыстории, «скрытность» и «магия» добраны классом.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var ath = d.Skills.First(s => s.SkillId == "athletics");
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            bool ok = ath.Proficient && arc.Proficient;
            if (!ok) Debug.LogError($"FAIL источники навыков: атлетика={ath.Proficient}, магия={arc.Proficient}");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — без владения бонус равен голому модификатору")]
        public void SelfTestUntrainedSkillIsBareModifier()
        {
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");            // магию больше не берём
            var d = SheetMath.Compute(c, rules);
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            // ИНТ 12 → +1, мастерства нет. Мутант «мастерство всем навыкам» даёт 4 — расходится.
            bool ok = !arc.Proficient && arc.Bonus == 1;
            if (!ok) Debug.LogError($"FAIL без владения: {arc.Bonus} влад={arc.Proficient}, ждали 1/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность удваивает мастерство, а не бонус")]
        public void SelfTestExpertiseDoublesProficiencyOnly()
        {
            // ЛОВ +2, мастерство +3. Владение → 5. Компетентность → 2 + 6 = 8.
            // Мутант «удваивать весь бонус» даёт 10 — расходится.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = st.Expertise && st.Bonus == 8;
            if (!ok) Debug.LogError($"FAIL компетентность: {st.Bonus} комп={st.Expertise}, ждали 8/да");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность без владения не действует")]
        public void SelfTestExpertiseRequiresProficiency()
        {
            // «Магия» не входит ни в SkillIds, ни в навыки предыстории — значит владения нет вовсе.
            // Компетентность в такой навык всё равно записана (мастер мог натыкать её раньше владения).
            // Мутант «компетентность без проверки владения» (exp = expertise.Contains(...) без prof &&)
            // на этой фикстуре не ловится ни одним другим тестом: везде, где есть компетентность,
            // владение тоже есть — Fixtures.Character() кладёт "stealth" в оба списка сразу.
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");
            c.ExpertiseIds.Add("arcana");
            var d = SheetMath.Compute(c, Fixtures.Rules());
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            // ИНТ 12 → +1, без владения компетентность не считается — бонус остаётся голым модификатором.
            bool ok = !arc.Proficient && !arc.Expertise && arc.Bonus == 1;
            if (!ok) Debug.LogError($"FAIL компетентность без владения: {arc.Bonus} влад={arc.Proficient} комп={arc.Expertise}, ждали 1/нет/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность не действует ниже своего уровня")]
        public void SelfTestExpertiseIgnoredBelowItsLevel()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].ExpertiseLevel = 6;     // персонаж 5 уровня — ещё рано
            var d = SheetMath.Compute(Fixtures.Character(), rules);
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = !st.Expertise && st.Bonus == 5;
            if (!ok) Debug.LogError($"FAIL ранняя компетентность: {st.Bonus} комп={st.Expertise}, ждали 5/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навык объясняет себя")]
        public void SelfTestSkillExplainsItself()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = st.Explain.Contains("+2") && st.Explain.Contains("ловкость")
                   && st.Explain.Contains("компетентность");
            if (!ok) Debug.LogError($"FAIL объяснение навыка: «{st.Explain}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — лёгкий доспех прибавляет всю ловкость")]
        public void SelfTestLightArmorAddsFullDex()
        {
            // Кожаный 11 + ЛОВ +2 = 13.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.ArmorClass == 13;
            if (!ok) Debug.LogError($"FAIL КД лёгкий: {d.ArmorClass}, ждали 13 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — тяжёлый доспех ловкость не прибавляет")]
        public void SelfTestHeavyArmorIgnoresDex()
        {
            // Кольчуга 16, MaxDexBonus 0 → 16, а не 18. Мутант «всегда прибавлять ловкость» падает.
            var c = Fixtures.Character();
            c.Equipment.Remove("leather"); c.Equipment.Add("chain");
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.ArmorClass == 16;
            if (!ok) Debug.LogError($"FAIL КД тяжёлый: {d.ArmorClass}, ждали 16 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — щит прибавляет 2")]
        public void SelfTestShieldAddsTwo()
        {
            var c = Fixtures.Character(); c.Equipment.Add("shield");
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.ArmorClass == 15;
            if (!ok) Debug.LogError($"FAIL КД со щитом: {d.ArmorClass}, ждали 15 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — защита без доспехов действует только без доспехов")]
        public void SelfTestUnarmoredDefenseOnlyWithoutArmor()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].UnarmoredDefenseAbility = "con";     // как у Варвара
            var withArmor = SheetMath.Compute(Fixtures.Character(), rules);
            var bare = Fixtures.Character(); bare.Equipment.Remove("leather");
            var without = SheetMath.Compute(bare, rules);
            // В доспехе 13 (доспех побеждает), без доспеха 10 + ЛОВ 2 + ТЕЛ 2 = 14.
            bool ok = withArmor.ArmorClass == 13 && without.ArmorClass == 14;
            if (!ok) Debug.LogError($"FAIL защита без доспехов: в доспехе {withArmor.ArmorClass} (ждали 13), без {without.ArmorClass} (ждали 14)");
            Done(ok);
        }

        [ContextMenu("Self-Test: умения — только по текущий уровень")]
        public void SelfTestFeaturesStopAtCurrentLevel()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].Levels.First(l => l.Level == 7).Features.Add(
                new FeatureRef { Id = "evasion", Name = "Уклонение", Text = "…" });
            var d = SheetMath.Compute(Fixtures.Character(), rules);   // персонаж 5 уровня
            bool has2 = d.Features.Any(f => f.Id == "cunning");       // уровень 2 — есть
            bool has7 = d.Features.Any(f => f.Id == "evasion");       // уровень 7 — ещё нет
            bool ok = has2 && !has7;
            if (!ok) Debug.LogError($"FAIL умения по уровню: 2-й={has2} (ждали да), 7-й={has7} (ждали нет)");
            Done(ok);
        }

        [ContextMenu("Self-Test: умения — вид, черта происхождения и умения класса вместе")]
        public void SelfTestFeaturesFromAllSources()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.Features.Any(f => f.Id == "lucky" && f.Source == "race")
                   && d.Features.Any(f => f.Id == "alert" && f.Source == "feat")
                   && d.Features.Any(f => f.Id == "cunning" && f.Source == "class");
            if (!ok) Debug.LogError("FAIL источники умений: " + string.Join(", ", d.Features.Select(f => f.Id + "/" + f.Source)));
            Done(ok);
        }

        [ContextMenu("Self-Test: умения подкласса приходят по своему уровню, а не сразу")]
        public void SelfTestSubclassFeaturesFollowLevel()
        {
            // «Быстрые руки» стоят на 3 уровне подкласса. У пятого они есть, у второго нет —
            // мутант «умения подкласса применяются всегда» падает на второй половине.
            var rules = Fixtures.Rules();
            var five = SheetMath.Compute(Fixtures.Character(), rules);
            var young = Fixtures.Character(); young.Level = 2;
            var two = SheetMath.Compute(young, rules);
            bool ok = five.Features.Any(f => f.Id == "fast-hands")
                   && !two.Features.Any(f => f.Id == "fast-hands");
            if (!ok) Debug.LogError($"FAIL умения подкласса: на 5-м={five.Features.Any(f => f.Id == "fast-hands")}, "
                                  + $"на 2-м={two.Features.Any(f => f.Id == "fast-hands")}");
            Done(ok);
        }

        [ContextMenu("Self-Test: неизвестный подкласс показывается")]
        public void SelfTestUnknownSubclassSurfaces()
        {
            var c = Fixtures.Character(); c.SubclassId = "assassin";   // в фикстуре только «вор»
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("assassin"));
            if (!ok) Debug.LogError("FAIL неизвестный подкласс: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: прибавка в несуществующую характеристику не пропадает молча")]
        public void SelfTestUnknownBumpAbilitySurfaces()
        {
            // AbilityScores.Add на неизвестной строке ничего не делает: без этой проверки прибавка
            // просто испарилась бы, а лист выглядел бы правдоподобно.
            var c = Fixtures.Character();
            c.Bumps.Add(new AbilityBump { Source = "background", AbilityId = "luck", Amount = 1 });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("luck"));
            if (!ok) Debug.LogError("FAIL неизвестная характеристика прибавки: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: наполовину разложенные прибавки считаются недоделкой")]
        public void SelfTestPartialBumpsAreIncomplete()
        {
            // Самое частое состояние на выходе из мастера: положил +2 и ушёл. Мутант
            // «хоть одна прибавка есть — значит готово» на этой фикстуре падает.
            var c = Fixtures.Character();
            c.Bumps.RemoveAll(b => b.AbilityId == "con");     // осталось +2, а нужно 3
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("2 из 3"));
            if (!ok) Debug.LogError("FAIL половина прибавок: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: понижение уровня не выбрасывает план")]
        public void SelfTestLoweringLevelKeepsPlan()
        {
            var c = Fixtures.Character();
            c.Plan.Add(new LevelChoice { Level = 8, Kind = "feat", ValueId = "alert" });
            c.Level = 3;
            var d = SheetMath.Compute(c, Fixtures.Rules());
            // План — это план, а не состояние: запись остаётся в файле, умение просто не применено.
            bool ok = c.Plan.Count == 1 && !d.Features.Any(f => f.Level == 8);
            if (!ok) Debug.LogError($"FAIL понижение уровня: записей плана {c.Plan.Count} (ждали 1)");
            Done(ok);
        }

        [ContextMenu("Self-Test: неизвестный Id показывается, а не удаляется")]
        public void SelfTestUnknownIdSurfaces()
        {
            var c = Fixtures.Character(); c.RaceId = "aarakocra";
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("aarakocra")) && c.RaceId == "aarakocra";
            if (!ok) Debug.LogError("FAIL неизвестный Id: " + string.Join("; ", d.UnknownIds));

            // Тот же NoteUnknown отвечает и за класс, и за предысторию — обе ветки были без стража.
            var cCls = Fixtures.Character(); cCls.ClassId = "warlock";
            var dCls = SheetMath.Compute(cCls, Fixtures.Rules());
            bool okCls = dCls.UnknownIds.Any(u => u.Contains("warlock")) && cCls.ClassId == "warlock";
            if (!okCls) Debug.LogError("FAIL неизвестный класс: " + string.Join("; ", dCls.UnknownIds));

            var cBg = Fixtures.Character(); cBg.BackgroundId = "sage";
            var dBg = SheetMath.Compute(cBg, Fixtures.Rules());
            bool okBg = dBg.UnknownIds.Any(u => u.Contains("sage")) && cBg.BackgroundId == "sage";
            if (!okBg) Debug.LogError("FAIL неизвестная предыстория: " + string.Join("; ", dBg.UnknownIds));

            Done(ok && okCls && okBg);
        }

        [ContextMenu("Self-Test: неизвестные навык, предмет и черты показываются")]
        public void SelfTestUnknownSkillItemAndFeatsSurface()
        {
            // Четыре ветки UnknownIds, которых не задевала ни одна самопроверка. «Ничего не
            // удаляется молча» — обещание листа, и оно должно быть под охраной, а не на слове.
            var rules = Fixtures.Rules();
            rules.Backgrounds[0].OriginFeatId = "ghost-origin";     // черты с таким Id нет
            var c = Fixtures.Character();
            c.SkillIds.Add("juggling");                             // навыка нет
            c.Equipment.Add("mithril");                             // предмета нет
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "feat", ValueId = "ghost-feat" });
            var d = SheetMath.Compute(c, rules);
            bool ok = d.UnknownIds.Any(u => u.Contains("juggling"))
                   && d.UnknownIds.Any(u => u.Contains("mithril"))
                   && d.UnknownIds.Any(u => u.Contains("ghost-origin"))
                   && d.UnknownIds.Any(u => u.Contains("ghost-feat"));
            if (!ok) Debug.LogError("FAIL неизвестные навык/предмет/черты: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: у целого персонажа неизвестных Id нет вовсе")]
        public void SelfTestCompleteCharacterHasNoUnknownIds()
        {
            // Страж от ЛОЖНЫХ срабатываний: без него инвертированный предикат («считать
            // неизвестными известные») проходит весь набор зелёным. У Missing такой страж есть,
            // у UnknownIds не было.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.UnknownIds.Count == 0;
            if (!ok) Debug.LogError("FAIL ложные неизвестные Id: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: прибавка в характеристику вне списка предыстории — недоделка")]
        public void SelfTestBumpOutsideBackgroundChoicesIsFlagged()
        {
            // Сумма остаётся ровно 3, поэтому сообщение «разложены на N из 3» НЕ должно сработать:
            // так тест ловит именно проверку разрешённых характеристик, а не общую недоделку.
            // Солдат даёт выбор из сил/лов/тел — мудрость не его.
            var c = Fixtures.Character();
            c.Bumps.RemoveAll(b => b.AbilityId == "con");
            c.Bumps.Add(new AbilityBump { Source = "background", AbilityId = "wis", Amount = 1 });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("wis") && m.Contains("Солдат"))
                   && !d.Missing.Any(m => m.Contains("из 3"));
            if (!ok) Debug.LogError("FAIL прибавка вне списка: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: недобранные навыки, компетентность и подкласс перечисляются")]
        public void SelfTestUnfinishedPicksAreListed()
        {
            // Три сообщения Missing, которые не выдавала ни одна самопроверка. Персонаж во всём
            // остальном целый — значит каждое сообщение приходит именно от своей ветки.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");     // выбран 1 навык из 2
            c.ExpertiseIds.Clear();          // компетентность не выбрана
            c.SubclassId = null;             // подкласс не выбран, а 3 уровень уже пройден
            var d = SheetMath.Compute(c, rules);
            bool ok = d.Missing.Any(m => m.Contains("Навыков выбрано 1 из 2"))
                   && d.Missing.Any(m => m.Contains("омпетентность"))
                   && d.Missing.Any(m => m.Contains("Подкласс не выбран"));
            if (!ok) Debug.LogError("FAIL недобранные выборы: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: подкласс не требуется раньше своего уровня выбора")]
        public void SelfTestMissingSubclassRespectsChoiceLevel()
        {
            // У «Плута» ячейка выбора подкласса — на 3 уровне. Персонаж 2 уровня без подкласса — это
            // НЕ недоделка, он просто ещё не дорос. Мутант «нужен подкласс, если он вообще
            // где-то в классе есть» (без проверки level <= текущий) требует подкласс уже на 1 уровне —
            // никакой другой тест это не ловит, потому что Fixtures.Character() всегда 5 уровня
            // и всегда с проставленным SubclassId.
            var c = Fixtures.Character();
            c.Level = 2;
            c.SubclassId = null;
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = !d.Missing.Any(m => m.Contains("Подкласс"));
            if (!ok) Debug.LogError("FAIL ранний подкласс: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: незаконченный персонаж перечисляет, чего не хватает")]
        public void SelfTestIncompleteCharacterListsGaps()
        {
            var c = new CharacterFile { RulesId = "test", Level = 1 };
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("Имя"))
                   && d.Missing.Any(m => m.Contains("Вид"))
                   && d.Missing.Any(m => m.Contains("Класс"))
                   && d.Missing.Any(m => m.Contains("Предыстория"));
            if (!ok) Debug.LogError("FAIL чего не хватает: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: законченный персонаж ни о чём не просит")]
        public void SelfTestCompleteCharacterHasNoGaps()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.Missing.Count == 0;
            if (!ok) Debug.LogError("FAIL законченный персонаж: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — уточнение называет характеристику и компетентность")]
        public void SelfTestSkillHintNamesAbilityAndExpertise()
        {
            // Уточнение — это то, что лист пишет мелким справа от числа. Раньше он собирал его сам,
            // и строка расходилась бы с объяснением под тем же числом при первом же переименовании
            // характеристики в справочнике.
            // Мутант №1: `Hint = abilityName` без хвоста → у скрытности пропадает компетентность.
            // Мутант №2: `Hint = skill.Name` → «скрытность» вместо «ловкость».
            // Обе фикстуры нужны разом: у скрытности компетентность ЕСТЬ, у атлетики НЕТ, поэтому
            // мутант «хвост всегда» падает на второй, а «хвоста нет» — на первой.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var stealth = d.Skills.First(s => s.SkillId == "stealth");
            var athletics = d.Skills.First(s => s.SkillId == "athletics");
            bool ok = stealth.Hint == "ловкость · компетентность" && athletics.Hint == "сила";
            if (!ok) Debug.LogError($"FAIL уточнение навыка: скрытность «{stealth.Hint}» "
                                  + $"(ждали «ловкость · компетентность»), атлетика «{athletics.Hint}» (ждали «сила»)");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
