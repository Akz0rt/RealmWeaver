using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Самопроверки таблицы 1–20 и повышения уровня. Фикстуры подобраны так, чтобы верное
    /// правило и правдоподобный мутант РАСХОДИЛИСЬ: уровни перемешаны, умения стоят не на первом
    /// уровне, компетентность — не на том уровне, куда мы поднимаемся дважды.</summary>
    public class LevelPlanOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: план — ровно двадцать строк")]
        public void SelfTestTwentyRows()
        {
            var rows = LevelPlanOps.Rows(Fixtures.Rules().Classes[0], Fixtures.Character());
            bool ok = rows.Count == 20 && rows[0].Level == 1 && rows[19].Level == 20;
            if (!ok) Debug.LogError($"FAIL строк плана: {rows.Count}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — без класса таблицы нет")]
        public void SelfTestNoClassNoRows()
        {
            // Мутант: снят `if (cls == null) return rows` — падение на листе с чужим классом.
            var rows = LevelPlanOps.Rows(null, Fixtures.Character());
            bool ok = rows != null && rows.Count == 0;
            if (!ok) Debug.LogError($"FAIL строк без класса: {rows?.Count.ToString() ?? "null"}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — достигнутые уровни помечены по текущему уровню")]
        public void SelfTestReachedFollowsCurrentLevel()
        {
            // Персонаж 5 уровня: строки 1–5 достигнуты, 6-я нет. Мутант «все достигнуты» падает.
            var rows = LevelPlanOps.Rows(Fixtures.Rules().Classes[0], Fixtures.Character());
            bool ok = rows[4].Reached && !rows[5].Reached;
            if (!ok) Debug.LogError($"FAIL достигнутые уровни: 5-й={rows[4].Reached}, 6-й={rows[5].Reached}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — уровни с выбором помечены заранее")]
        public void SelfTestChoiceLevelsFlaggedAhead()
        {
            var rows = LevelPlanOps.Rows(Fixtures.Rules().Classes[0], Fixtures.Character());
            bool ok = rows[2].ChoiceKind == "subclass"      // 3 уровень
                   && rows[7].ChoiceKind == "asi"           // 8 уровень — ВЫШЕ текущего, но помечен
                   && !rows[7].Reached;
            if (!ok) Debug.LogError($"FAIL пометки выбора: 3-й={rows[2].ChoiceKind}, 8-й={rows[7].ChoiceKind}");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — строка показывает уже намеченный выбор")]
        public void SelfTestRowsCarryPlannedChoice()
        {
            // Мутант: ChosenId не заполняется (всегда null) — намеченное игроком в таблице не видно.
            // Мутант: ChosenId = planned?.Kind — вместо «Вор» встал бы «subclass».
            var c = Fixtures.Character();
            c.Plan.Add(new LevelChoice { Level = 3, Kind = "subclass", ValueId = "thief" });
            var rows = LevelPlanOps.Rows(Fixtures.Rules().Classes[0], c);
            bool ok = rows[2].ChosenId == "thief" && rows[0].ChosenId == null && rows[3].ChosenId == null;
            if (!ok) Debug.LogError($"FAIL намеченный выбор: 3-й=«{rows[2].ChosenId ?? "null"}», "
                                  + $"1-й=«{rows[0].ChosenId ?? "null"}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — уровень без умений подписан прочерком")]
        public void SelfTestRowsSummarizeFeaturesOrDash()
        {
            // В фикстуре умение стоит ровно на 2 уровне. Мутант «всегда склеивать названия» дал бы
            // на 1 уровне пустую строку, мутант «всегда прочерк» — прочерк на втором.
            var rows = LevelPlanOps.Rows(Fixtures.Rules().Classes[0], Fixtures.Character());
            bool ok = rows[0].Summary == "—" && rows[1].Summary == "Хитрое действие";
            if (!ok) Debug.LogError($"FAIL подписи строк: 1-й=«{rows[0].Summary}», 2-й=«{rows[1].Summary}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: план — строки идут по возрастанию уровня, как бы ни лежали данные")]
        public void SelfTestRowsSortByLevel()
        {
            // Мутант: снят OrderBy — таблица показывает уровни в том порядке, в каком их занесли
            // в JSON. Подписи разные, поэтому мутант «пронумеровать строки заново» тоже падает:
            // у него на первой строке окажется умение третьего уровня.
            var cls = new ClassDef { Id = "shuffled", Name = "Перемешанный", HitDie = "d8" };
            foreach (int lv in new[] { 3, 1, 2 })
                cls.Levels.Add(new ClassLevel
                {
                    Level = lv,
                    Features = { new FeatureRef { Id = "f" + lv, Name = "умение-" + lv, Text = "…" } }
                });

            var rows = LevelPlanOps.Rows(cls, Fixtures.Character());
            bool ok = rows.Count == 3
                   && rows[0].Level == 1 && rows[0].Summary == "умение-1"
                   && rows[1].Level == 2 && rows[1].Summary == "умение-2"
                   && rows[2].Level == 3 && rows[2].Summary == "умение-3";
            if (!ok) Debug.LogError("FAIL порядок строк: ["
                                  + string.Join(",", rows.Select(r => $"{r.Level}/{r.Summary}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня поднимает уровень и называет предстоящие выборы")]
        public void SelfTestLevelUpRaisesAndAsks()
        {
            // Строк РОВНО одна: мутант «любой выбор — это ещё и подкласс» (`lv.Choice != null`)
            // добавил бы вторую и при проверке одним Any остался бы жив.
            var c = Fixtures.Character(); c.Level = 7;
            var pending = LevelPlanOps.LevelUp(c, Fixtures.Rules());
            bool ok = c.Level == 8 && pending.Count == 1 && pending[0].Contains("характеристик");
            if (!ok) Debug.LogError($"FAIL повышение: уровень {c.Level}, ожидания [{string.Join(";", pending)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня до подкласса спрашивает про подкласс, а не про характеристики")]
        public void SelfTestLevelUpAsksForSubclass()
        {
            // Мутант: обе ветки выбора обрабатываются одинаково («любой выбор — это повышение»).
            var c = Fixtures.Character(); c.Level = 2;
            var pending = LevelPlanOps.LevelUp(c, Fixtures.Rules());
            bool ok = c.Level == 3
                   && pending.Count == 1
                   && pending[0].Contains("одкласс")
                   && !pending[0].Contains("характеристик");
            if (!ok) Debug.LogError($"FAIL выбор подкласса: уровень {c.Level}, [{string.Join(";", pending)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня — про уровень без выбора не спрашивают ничего")]
        public void SelfTestLevelUpToQuietLevelAsksNothing()
        {
            // Страж от ложных срабатываний: 5→6 у Плута — уровень без выбора и без компетентности.
            var c = Fixtures.Character(); c.Level = 5;
            var pending = LevelPlanOps.LevelUp(c, Fixtures.Rules());
            bool ok = c.Level == 6 && pending.Count == 0;
            if (!ok) Debug.LogError($"FAIL тихий уровень: уровень {c.Level}, [{string.Join(";", pending)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня — компетентность спрашивают ровно на своём уровне")]
        public void SelfTestLevelUpAsksForExpertiseOnItsLevelOnly()
        {
            // Класс с компетентностью на 6 уровне и БЕЗ выборов вокруг — чтобы строка про
            // компетентность стояла в списке одна. Мутанты `>=`, `<=` и «есть компетентность —
            // спрашиваем всегда» падают на втором повышении (6→7), где строки быть не должно.
            var rules = Fixtures.Rules();
            var cls = new ClassDef { Id = "hunter", Name = "Охотник", HitDie = "d10",
                ExpertiseLevel = 6, ExpertisePickCount = 2 };
            for (int lv = 1; lv <= 20; lv++) cls.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(cls);

            var c = Fixtures.Character(); c.ClassId = "hunter"; c.Level = 5;
            var atSix = LevelPlanOps.LevelUp(c, rules);
            var atSeven = LevelPlanOps.LevelUp(c, rules);

            bool ok = c.Level == 7
                   && atSix.Count == 1 && atSix[0].Contains("омпетентн") && atSix[0].Contains("2")
                   && atSeven.Count == 0;
            if (!ok) Debug.LogError($"FAIL компетентность: уровень {c.Level}, "
                                  + $"на 6-м [{string.Join(";", atSix)}], на 7-м [{string.Join(";", atSeven)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня — класс без данных на новом уровне не мешает поднять уровень")]
        public void SelfTestLevelUpWithoutLevelDataStillRaises()
        {
            // Мутант: снят `if (lv == null) return pending` — падение вместо тихого повышения.
            // Проверяем оба провала поиска: класса нет вовсе и у класса нет строки на этот уровень.
            var rules = Fixtures.Rules();
            var stub = new ClassDef { Id = "short", Name = "Короткий", HitDie = "d6" };
            for (int lv = 1; lv <= 5; lv++) stub.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(stub);

            var shortCls = Fixtures.Character(); shortCls.ClassId = "short"; shortCls.Level = 5;
            var pendingShort = LevelPlanOps.LevelUp(shortCls, rules);

            var noCls = Fixtures.Character(); noCls.ClassId = "bogus"; noCls.Level = 5;
            var pendingNone = LevelPlanOps.LevelUp(noCls, rules);

            bool ok = shortCls.Level == 6 && pendingShort.Count == 0
                   && noCls.Level == 6 && pendingNone.Count == 0;
            if (!ok) Debug.LogError($"FAIL повышение без данных: короткий класс {shortCls.Level} "
                                  + $"[{string.Join(";", pendingShort)}], без класса {noCls.Level} "
                                  + $"[{string.Join(";", pendingNone)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: повышение уровня останавливается на 20")]
        public void SelfTestLevelUpStopsAtTwenty()
        {
            var c = Fixtures.Character(); c.Level = 20;
            var pending = LevelPlanOps.LevelUp(c, Fixtures.Rules());
            bool ok = c.Level == 20 && pending.Count == 0;
            if (!ok) Debug.LogError($"FAIL потолок уровня: {c.Level}, [{string.Join(";", pending)}]");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
