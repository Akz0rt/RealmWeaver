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
            // Мутант: «потолок 20 не проверяется» — характеристика уезжает в 21.
            var near = Fixtures.Character(); near.Base.Dex = 19;
            LevelChoiceOps.ChooseAsi(near, 8, new List<string> { "dex" });
            int nearTotal = SheetMath.AbilityTotal(near, "dex", 8);

            // На самом потолке прибавка не записывается ВОВСЕ: «+0 Ловкость» в файле выглядела бы
            // выбором, который что-то делает.
            var full = Fixtures.Character(); full.Base.Dex = 20;
            LevelChoiceOps.ChooseAsi(full, 8, new List<string> { "dex" });

            // Мутант «итог считается ДО того, как снята прошлая прибавка»: второй выбор той же
            // характеристики увидел бы собственную прошлую прибавку и урезался бы об неё до нуля.
            var twice = Fixtures.Character(); twice.Base.Dex = 18;
            LevelChoiceOps.ChooseAsi(twice, 8, new List<string> { "dex" });
            LevelChoiceOps.ChooseAsi(twice, 8, new List<string> { "dex" });
            int twiceTotal = SheetMath.AbilityTotal(twice, "dex", 8);

            bool ok = nearTotal == 20
                   && near.Bumps.Count(b => SheetMath.BumpLevel(b.Source) == 8) == 1
                   && !full.Bumps.Any(b => SheetMath.BumpLevel(b.Source) == 8)
                   && twiceTotal == 20;
            if (!ok) Debug.LogError($"FAIL потолок 20: с 19 стало {nearTotal} (ждали 20), "
                                  + $"с 20 прибавок {full.Bumps.Count(b => SheetMath.BumpLevel(b.Source) == 8)} (ждали 0), "
                                  + $"дважды с 18 стало {twiceTotal} (ждали 20)");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — потолок берётся на целевом уровне и держится при любом порядке")]
        public void SelfTestAsiCapUsesTargetLevelAndAnyOrder()
        {
            // ДВЕ находки в одной фикстуре, и обе — про то, что прошлые проверки пропускали.
            //
            // Мутант №1: `AbilityTotal(file, id, file.Level)` вместо `level` в ChooseAsi. Прежние
            // фикстуры его не ловили вовсе: между file.Level (5) и целевым уровнем в них не лежало ни
            // одной прибавки, и оба правила отвечали одинаково. Здесь между ними прибавка ЕСТЬ.
            var byOrder = Fixtures.Character();                                  // 5 уровень
            byOrder.Base.Dex = 17;
            LevelChoiceOps.ChooseAsi(byOrder, 8, new List<string> { "dex" });    // 17 → 19
            LevelChoiceOps.ChooseAsi(byOrder, 12, new List<string> { "dex" });   // на 12-м уже 19 → влезает +1
            int atTwelve = byOrder.Bumps.Where(b => SheetMath.BumpLevel(b.Source) == 12).Sum(b => b.Amount);
            // Сверяется ЗАПИСАННОЕ, а не итог: итог теперь режет потолок в SheetMath.AbilityTotal, и
            // оба правила дали бы 20. Расходятся они ровно в том, что легло в файл, — +1 против +2.
            bool ok = atTwelve == 1 && SheetMath.AbilityTotal(byOrder, "dex", 12) == 20;

            // Мутант №2: «потолок держит только миг выбора». Тот же выбор в ОБРАТНОМ порядке: записи
            // друг про друга не знают, в файле честно лежит 17+2+2, и удержать двадцатку может только
            // счёт. Без потолка в AbilityTotal здесь выходит 21 — ровно то, что нашёл проверяющий.
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

            if (!ok) Debug.LogError($"FAIL потолок и порядок: записано на 12 «{atTwelve}» (ждали 1), "
                                  + $"итог по порядку {SheetMath.AbilityTotal(byOrder, "dex", 12)} (ждали 20), "
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
