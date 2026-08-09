using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Самопроверки пометок плана прокачки. Фикстуры строятся на 5 уровне и выше, а
    /// намечается всё на 8 и 12: на первом уровне верное правило и мутант неотличимы, а половина
    /// здешних правил — про то, ЧТО ИМЕННО пометка НЕ трогает.</summary>
    public class LevelChoiceOpsSelfTests : MonoBehaviour
    {
        /// <summary>Справочник с чертами всех нужных разрядов: в Fixtures.Rules() черта одна и та
        /// «происхождения», на ней списки вариантов не проверить.</summary>
        static RulesData RulesWithFeats()
        {
            var r = Fixtures.Rules();
            r.Feats.Add(new FeatDef { Id = LevelChoiceOps.AsiFeatId, Name = "Повышение характеристик", Text = "…", Category = "general" });
            r.Feats.Add(new FeatDef { Id = "grappler", Name = "Борец", Text = "…", Category = "general" });
            r.Feats.Add(new FeatDef { Id = "actor", Name = "Актёр", Text = "…", Category = "general" });
            r.Feats.Add(new FeatDef { Id = "boon-of-fate", Name = "Дар судьбы", Text = "…", Category = "epic-boon" });
            return r;
        }

        static ClassDef Rogue(RulesData rules) => rules.Classes.First(c => c.Id == "rogue");

        [ContextMenu("Self-Test: план — «повышение характеристик» не показывается в списке черт")]
        public void SelfTestFeatOptionsDropAbilityScoreImprovement()
        {
            // Мутант: «FeatOptions возвращает и ability-score-improvement». Тогда игрок берёт его
            // ЧЕРТОЙ, пометка встаёт, а прибавок не записывает никто — характеристики не растут вовсе,
            // и разобраться в этом по листу нечем.
            var rules = RulesWithFeats();
            var general = LevelChoiceOps.FeatOptions(rules, 8);
            bool ok = !general.Any(f => f.Id == LevelChoiceOps.AsiFeatId)
                   && general.Any(f => f.Id == "grappler")
                   && general.Any(f => f.Id == "actor")
                   && general.Count == 2;
            // Мутант «список отдаётся как лежит в справочнике»: «Борец» добавлен ПЕРЕД «Актёром».
            ok &= general[0].Id == "actor" && general[1].Id == "grappler";
            // Мутант «разряд не смотрится»: черта происхождения «Бдительный» в список попадать не должна.
            ok &= !general.Any(f => f.Id == "alert");
            if (!ok) Debug.LogError("FAIL список черт на 8 уровне: "
                                  + string.Join(", ", general.Select(f => f.Id).ToArray()));
            Done(ok);
        }

        [ContextMenu("Self-Test: план — с 19 уровня предлагаются эпические дары")]
        public void SelfTestFeatOptionsSwitchToEpicBoons()
        {
            // Мутанты: «разряд всегда general» (на 19 уровне дар не найдётся) и «граница на 20»
            // (на 19 предложатся обычные черты). Проверяются ОБА конца границы — 18 и 19.
            var rules = RulesWithFeats();
            var below = LevelChoiceOps.FeatOptions(rules, 18);
            var epic = LevelChoiceOps.FeatOptions(rules, 19);
            bool ok = below.All(f => f.Category == "general") && below.Count == 2
                   && epic.Count == 1 && epic[0].Id == "boon-of-fate";
            if (!ok) Debug.LogError($"FAIL разряд черт: на 18 «{string.Join(",", below.Select(f => f.Id).ToArray())}», "
                                  + $"на 19 «{string.Join(",", epic.Select(f => f.Id).ToArray())}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — подкласс со взятого уровня работает, с будущего только помечен")]
        public void SelfTestChooseSubclassWritesFieldOnlyWhenReached()
        {
            // Мутант «писать SubclassId всегда»: подкласс, намеченный первоуровневым персонажем на
            // третий, начал бы давать умения немедленно.
            var rules = RulesWithFeats();
            var reached = Fixtures.Character(); reached.SubclassId = null;      // 5 уровень
            LevelChoiceOps.ChooseSubclass(reached, 3, "thief");

            var ahead = Fixtures.Character(); ahead.SubclassId = null; ahead.Level = 1;
            LevelChoiceOps.ChooseSubclass(ahead, 3, "thief");

            bool ok = reached.SubclassId == "thief" && ahead.SubclassId == null
                   && reached.Plan.Count == 1 && reached.Plan[0].Kind == "subclass"
                   && reached.Plan[0].ValueId == "thief" && reached.Plan[0].Level == 3
                   && ahead.Plan.Count == 1 && ahead.Plan[0].ValueId == "thief";
            // Умений подкласса у первоуровневого не появилось — то же самое, но глазами листа.
            var d = SheetMath.Compute(ahead, rules);
            ok &= !d.Features.Any(f => f.Id == "fast-hands");
            if (!ok) Debug.LogError($"FAIL выбор подкласса: взятый «{reached.SubclassId ?? "null"}», "
                                  + $"будущий «{ahead.SubclassId ?? "null"}», пометок {reached.Plan.Count}/{ahead.Plan.Count}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — черта вместо прибавок стирает прибавки своего уровня")]
        public void SelfTestChooseFeatDropsThisLevelBumps()
        {
            // Мутант: «ChooseFeat оставляет старые прибавки». Игрок разложил +1/+1 на 8 уровне, потом
            // передумал в пользу черты — и прибавки продолжали бы поднимать характеристики РЯДОМ с
            // чертой, взятой вместо них.
            // Второй мутант: «сносит все прибавки подряд» — прибавка 4 уровня и прибавки предыстории
            // обязаны уцелеть, поэтому в фикстуре есть и те и другие.
            var c = Fixtures.Character();
            c.Bumps.Add(new AbilityBump { Source = SheetMath.BumpSource(4), AbilityId = "int", Amount = 2 });
            LevelChoiceOps.ChooseAsi(c, 8, new List<string> { "dex", "cha" });

            LevelChoiceOps.ChooseFeat(c, 8, "grappler");

            bool ok = !c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 8)
                   && c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 4 && b.AbilityId == "int")
                   && c.Bumps.Count(b => b.Source == "background") == 2
                   && c.Plan.Count == 1 && c.Plan[0].Kind == "feat" && c.Plan[0].ValueId == "grappler";
            if (!ok) Debug.LogError("FAIL черта вместо прибавок: осталось "
                                  + string.Join(", ", c.Bumps.Select(b => $"{b.Source}:{b.AbilityId}{b.Amount}").ToArray())
                                  + $"; пометок {c.Plan.Count}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — прибавки заменяют только свой уровень")]
        public void SelfTestChooseAsiReplacesOnlyItsOwnLevel()
        {
            // Мутант: «ChooseAsi сносит ВСЕ прибавки, а не только своего уровня». Фикстура нарочно
            // такая: прибавка 4 уровня УЖЕ есть, выбираем на 8-м — и та обязана выжить. Вместе с ней
            // выживают прибавки предыстории, иначе разложенное в мастере пропадало бы при первом же
            // повышении.
            var c = Fixtures.Character();
            c.Bumps.Add(new AbilityBump { Source = SheetMath.BumpSource(4), AbilityId = "int", Amount = 2 });

            LevelChoiceOps.ChooseAsi(c, 8, new List<string> { "cha" });          // одна → +2
            LevelChoiceOps.ChooseAsi(c, 8, new List<string> { "wis", "int" });   // передумал: две → по +1

            var atEight = c.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 8).ToList();
            bool ok = atEight.Count == 2 && atEight.All(b => b.Amount == 1)
                   && atEight.Any(b => b.AbilityId == "wis") && atEight.Any(b => b.AbilityId == "int")
                   && c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 4 && b.AbilityId == "int" && b.Amount == 2)
                   && c.Bumps.Count(b => b.Source == "background") == 2
                   && c.Plan.Count == 1 && c.Plan[0].Kind == "asi" && c.Plan[0].ValueId == null;
            if (!ok) Debug.LogError("FAIL прибавки уровня: "
                                  + string.Join(", ", c.Bumps.Select(b => $"{b.Source}:{b.AbilityId}{b.Amount}").ToArray())
                                  + $"; пометок {c.Plan.Count}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — прибавка не поднимает характеристику выше 20")]
        public void SelfTestChooseAsiRespectsTheCapOfTwenty()
        {
            // Мутант: «потолок 20 не держится вовсе» — характеристика уезжает в 21. Спрашивается он у
            // СЧЁТА (AbilityTotal), потому что записи потолок больше не касается.
            var near = Fixtures.Character(); near.Base.Dex = 19;
            LevelChoiceOps.ChooseAsi(near, 8, new List<string> { "dex" });
            int nearTotal = SheetMath.AbilityTotal(near, "dex", 8);
            // Мутант «обрезка вернулась в ChooseAsi»: в файле оказался бы +1. База 19 подобрана ровно
            // для этого — при базе 15 обрезка и её отсутствие пишут одно и то же.
            int nearWritten = near.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 8).Sum(b => b.Amount);

            // НА САМОМ ПОТОЛКЕ ПРИБАВКА ВСЁ РАВНО ЗАПИСЫВАЕТСЯ ЦЕЛИКОМ. Панель такой выбор не даёт
            // (кнопка гаснет при 20), но если он всё-таки сделан, файл обязан помнить обещанное: игрок,
            // понизивший потом базу, получит своё повышение полным.
            var full = Fixtures.Character(); full.Base.Dex = 20;
            LevelChoiceOps.ChooseAsi(full, 8, new List<string> { "dex" });
            int fullWritten = full.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 8).Sum(b => b.Amount);
            int fullTotal = SheetMath.AbilityTotal(full, "dex", 8);

            // Мутант «RemoveBumps из ChooseAsi убрана»: второй выбор той же характеристики положил бы
            // ВТОРУЮ прибавку рядом с первой. Сверяется ЗАПИСАННОЕ, а не итог: итог обе версии дают
            // одинаковый (18+2+2 = 22 режется потолком до 20), и проверка по нему была бы пустой.
            var twice = Fixtures.Character(); twice.Base.Dex = 18;
            LevelChoiceOps.ChooseAsi(twice, 8, new List<string> { "dex" });
            LevelChoiceOps.ChooseAsi(twice, 8, new List<string> { "dex" });
            var twiceBumps = twice.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 8).ToList();

            bool ok = nearTotal == 20 && nearWritten == 2
                   && fullWritten == 2 && fullTotal == 20
                   && twiceBumps.Count == 1 && twiceBumps[0].Amount == 2
                   && SheetMath.AbilityTotal(twice, "dex", 8) == 20;
            if (!ok) Debug.LogError($"FAIL потолок 20: с 19 стало {nearTotal} (ждали 20) при записанных "
                                  + $"{nearWritten} (ждали 2), с 20 записано {fullWritten} (ждали 2) при итоге "
                                  + $"{fullTotal} (ждали 20), дважды с 18 прибавок {twiceBumps.Count} "
                                  + $"(ждали 1) на {twiceBumps.Sum(b => b.Amount)} (ждали 2)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — надкусанное потолком повышение лист называет вслух")]
        public void SelfTestChooseAsiKeepsThePromiseAndTheSheetExplainsTheLoss()
        {
            // ГЛАВНАЯ ПРОВЕРКА НАХОДКИ. Мутант — «обрезка при записи вернулась в ChooseAsi»
            // (`Math.Min(each, AbilityCap - now)` и `continue`): в файле остаётся +1, лист печатает
            // «Ловкость 19 → 20 (+1)», и о пропавшей половине не говорит НИКТО — ни кнопка, ни строка
            // ячейки, ни лист. База 19 нарочно: при 15 обе версии кладут одно и то же.
            var rules = RulesWithFeats();
            var c = Fixtures.Character();       // 5 уровень
            c.Base.Dex = 19;
            LevelChoiceOps.ChooseAsi(c, 4, new List<string> { "dex" });

            var atFour = c.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 4).ToList();
            bool ok = atFour.Count == 1 && atFour[0].AbilityId == "dex" && atFour[0].Amount == 2;

            // И глазами листа: число упёрлось в 20, а объяснение называет обе половины.
            var d = SheetMath.Compute(c, rules);
            const string want = "Ловкость 19 → 20 (+1 из +2: выше 20 характеристика не растёт)";
            string got = d.AbilityExplain.TryGetValue("dex", out var e) ? e : "(нет)";
            ok &= d.Total.Dex == 20 && got == want;

            // Второй выигрыш: игрок понизил базу задним числом в мастере — и повышение вернулось
            // ПОЛНЫМ. С обрезкой при записи оно осталось бы урезанным навсегда.
            c.Base.Dex = 15;
            var after = SheetMath.Compute(c, rules);
            string gotAfter = after.AbilityExplain.TryGetValue("dex", out var e2) ? e2 : "(нет)";
            ok &= after.Total.Dex == 17 && gotAfter == "Ловкость 15 → 17 (+2)";

            if (!ok) Debug.LogError($"FAIL обещанное повышение: записано {atFour.Sum(b => b.Amount)} (ждали 2), "
                                  + $"лист «{got}» (ждали «{want}»), после понижения базы «{gotAfter}» "
                                  + "(ждали «Ловкость 15 → 17 (+2)»)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — потолок держится счётом при любом порядке и после правки базы")]
        public void SelfTestAsiCapUsesTargetLevelAndAnyOrder()
        {
            // ПОТОЛОК ДЕРЖИТ СЧЁТ, А НЕ МИГ ВЫБОРА, и здесь три дороги, на которых миг выбора
            // проигрывает. Прежний «мутант №1» (`AbilityTotal(file, id, file.Level)` вместо `level` в
            // ChooseAsi) из этого списка ушёл вместе с обрезкой при записи: ChooseAsi больше ничего не
            // считает и уровня для счёта не спрашивает вовсе.
            //
            // Дорога первая: два повышения подряд. В файл ложится обещанное целиком — 17+2+2, — а
            // двадцатку держит AbilityTotal. Заодно видно, что прибавка 12 уровня не действует на 8-м.
            var byOrder = Fixtures.Character();                                  // 5 уровень
            byOrder.Base.Dex = 17;
            LevelChoiceOps.ChooseAsi(byOrder, 8, new List<string> { "dex" });    // 17 → 19
            LevelChoiceOps.ChooseAsi(byOrder, 12, new List<string> { "dex" });   // на 12-м 19 → 20, обещано +2
            int atTwelve = byOrder.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 12).Sum(b => b.Amount);
            bool ok = atTwelve == 2 && SheetMath.AbilityTotal(byOrder, "dex", 12) == 20
                   && SheetMath.AbilityTotal(byOrder, "dex", 8) == 19;

            // Дорога вторая: тот же выбор в ОБРАТНОМ порядке — записи друг про друга не знают. Без
            // потолка в AbilityTotal здесь выходит 21 — ровно то, что нашёл проверяющий.
            var reversed = Fixtures.Character();
            reversed.Base.Dex = 17;
            LevelChoiceOps.ChooseAsi(reversed, 12, new List<string> { "dex" });
            LevelChoiceOps.ChooseAsi(reversed, 8, new List<string> { "dex" });
            int written = reversed.Bumps.Where(b => b.AbilityId == "dex" && SheetMath.BumpLevel(b.Source) > 0)
                                        .Sum(b => b.Amount);
            ok &= written == 4 && SheetMath.AbilityTotal(reversed, "dex", 12) == 20;

            // И третья дорога к тому же: БАЗУ подняли задним числом в мастере, когда прибавки уже
            // намечены. Проверять на записи бессмысленно — запись никогда не последняя.
            var raised = Fixtures.Character();
            raised.Base.Dex = 17;
            LevelChoiceOps.ChooseAsi(raised, 8, new List<string> { "dex" });
            raised.Base.Dex = 19;
            ok &= SheetMath.AbilityTotal(raised, "dex", 8) == 20;

            if (!ok) Debug.LogError($"FAIL потолок и порядок: записано на 12 «{atTwelve}» (ждали 2), "
                                  + $"итог по порядку {SheetMath.AbilityTotal(byOrder, "dex", 12)} (ждали 20), "
                                  + $"он же на 8 {SheetMath.AbilityTotal(byOrder, "dex", 8)} (ждали 19), "
                                  + $"итог наоборот {SheetMath.AbilityTotal(reversed, "dex", 12)} (ждали 20, записано {written} из 4), "
                                  + $"после правки базы {SheetMath.AbilityTotal(raised, "dex", 8)} (ждали 20)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — «убрать выбор» снимает и подкласс, выбранный мастером")]
        public void SelfTestClearRemovesSubclassEvenWithoutAMark()
        {
            // Мутант: «Clear смотрит только на пометки». У персонажа, собранного мастером, подкласс
            // лежит в file.SubclassId, а пометки в плане нет вовсе (Fixtures.Character() именно такой,
            // см. SelfTestRowsShowSubclassFromFile) — и «убрать выбор» не делал бы РОВНО НИЧЕГО:
            // строка таблицы после нажатия по-прежнему говорила бы «вор».
            var rules = RulesWithFeats();
            var c = Fixtures.Character();
            bool wizardShape = c.Plan.Count == 0 && c.SubclassId == "thief";
            LevelChoiceOps.Clear(c, 3, Rogue(rules));

            // Мутант «Clear всегда обнуляет подкласс»: пометка на БУДУЩУЮ замену подкласса снимается,
            // а нынешний подкласс остаётся — снимая план, игрок не отказывается от того, что у него уже есть.
            var future = Fixtures.Character();
            future.Plan.Add(new LevelChoice { Level = 8, Kind = "subclass", ValueId = "assassin" });
            LevelChoiceOps.Clear(future, 8, Rogue(rules));

            bool ok = wizardShape && c.SubclassId == null && c.Plan.Count == 0
                   && future.SubclassId == "thief" && future.Plan.Count == 0;
            if (!ok) Debug.LogError($"FAIL убрать подкласс (форма мастера={wizardShape}): "
                                  + $"после снятия «{c.SubclassId ?? "null"}» (ждали null), "
                                  + $"после снятия будущей замены «{future.SubclassId ?? "null"}» (ждали «thief»)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — «убрать выбор» уносит прибавки своего уровня и только их")]
        public void SelfTestClearRemovesThisLevelBumps()
        {
            var rules = RulesWithFeats();
            var c = Fixtures.Character();
            LevelChoiceOps.ChooseAsi(c, 4, new List<string> { "int" });
            LevelChoiceOps.ChooseAsi(c, 8, new List<string> { "cha" });

            LevelChoiceOps.Clear(c, 8, Rogue(rules));

            // Мутант «Clear чистит только пометку»: прибавки 8 уровня остались бы работать без всякой
            // пометки — на листе выросшая характеристика, а в плане пустая ячейка.
            bool ok = !c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 8)
                   && c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 4 && b.AbilityId == "int")
                   && c.Plan.Count == 1 && c.Plan[0].Level == 4
                   && c.SubclassId == "thief";       // 8 уровень у плута не про подкласс
            if (!ok) Debug.LogError("FAIL убрать прибавки: "
                                  + string.Join(", ", c.Bumps.Select(b => $"{b.Source}:{b.AbilityId}{b.Amount}").ToArray())
                                  + $"; пометок {c.Plan.Count}; подкласс «{c.SubclassId ?? "null"}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — намеченный заранее подкласс включается на своём уровне")]
        public void SelfTestApplyPlanForTurnsAMarkIntoASubclass()
        {
            // Мутант: «ApplyPlanFor ничего не делает». Без неё игрок, выбравший подкласс заранее,
            // дорастал бы до третьего уровня и читал на листе «Подкласс не выбран».
            var rules = RulesWithFeats();
            var c = Fixtures.Character();
            c.Level = 2; c.SubclassId = null;
            LevelChoiceOps.ChooseSubclass(c, 3, "thief");      // на будущее: поле не пишется
            bool notYet = c.SubclassId == null;

            LevelPlanOps.LevelUp(c, rules);                    // сам по себе подкласс не включает
            bool levelUpAlone = c.SubclassId == null && c.Level == 3;

            LevelChoiceOps.ApplyPlanFor(c, 3);

            // Мутант «переносит пометки любого уровня»: намеченное на 8 включаться сейчас не должно.
            var ahead = Fixtures.Character();
            ahead.SubclassId = null;                           // 5 уровень
            ahead.Plan.Add(new LevelChoice { Level = 8, Kind = "subclass", ValueId = "thief" });
            LevelChoiceOps.ApplyPlanFor(ahead, 8);

            bool ok = notYet && levelUpAlone && c.SubclassId == "thief" && ahead.SubclassId == null;
            // И глазами листа: умение подкласса 3 уровня теперь на месте.
            var d = SheetMath.Compute(c, rules);
            ok &= d.Features.Any(f => f.Id == "fast-hands");
            if (!ok) Debug.LogError($"FAIL перенос плана: до повышения null={notYet}, "
                                  + $"после LevelUp null={levelUpAlone}, после переноса «{c.SubclassId ?? "null"}», "
                                  + $"с будущего уровня «{ahead.SubclassId ?? "null"}» (ждали null)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — понижение уровня не трогает намеченного")]
        public void SelfTestLevelDownKeepsThePlan()
        {
            // Мутант: «LevelDown чистит план» — один промах мимо кнопки стирал бы всё, что игрок
            // наметил выше. Второй мутант: «ниже первого уровня» — уровень 0 или отрицательный.
            var c = Fixtures.Character();
            c.Plan.Add(new LevelChoice { Level = 8, Kind = "feat", ValueId = "grappler" });
            c.Plan.Add(new LevelChoice { Level = 12, Kind = "asi", ValueId = null });
            c.Bumps.Add(new AbilityBump { Source = SheetMath.BumpSource(12), AbilityId = "dex", Amount = 2 });

            LevelPlanOps.LevelDown(c);

            var first = Fixtures.Character(); first.Level = 1;
            LevelPlanOps.LevelDown(first);

            bool ok = c.Level == 4 && c.Plan.Count == 2
                   && c.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 12)
                   && first.Level == 1;
            if (!ok) Debug.LogError($"FAIL понижение уровня: уровень {c.Level} (ждали 4), "
                                  + $"пометок {c.Plan.Count} (ждали 2), с первого стало {first.Level} (ждали 1)");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
