using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Самопроверки чистой части мастера. Каждая названа по мутанту, которого обязана убить:
    /// «выживший мутант — дефект утверждения, а не реализации», поэтому ветки перечислены поимённо,
    /// а не покрыты «в среднем».</summary>
    public class WizardOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: мастер — навык от предыстории помечен как уже полученный")]
        public void SelfTestBackgroundSkillIsMarkedOwned()
        {
            // Шаг 6 мастера прячет уже полученное: взять один навык дважды нельзя.
            // Name сверяется ЗНАЧЕНИЕМ, а не на «непусто»: мутант `Name = id` показал бы игроку
            // «athletics» вместо «Атлетика» и любую проверку на непустоту прошёл бы насквозь.
            var options = WizardOps.AvailableSkills(Fixtures.Character(), Fixtures.Rules());
            var ath = options.First(o => o.Id == "athletics");
            bool ok = ath.Name == "Атлетика" && ath.AlreadyOwned && ath.OwnedFrom.Contains("Солдат");
            if (!ok) Debug.LogError($"FAIL уже полученный навык: name=«{ath.Name}» owned={ath.AlreadyOwned} from=«{ath.OwnedFrom}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — список навыков берётся у класса, а не из всего справочника")]
        public void SelfTestAvailableSkillsFollowClassList()
        {
            // Мутант: `ids` всегда из rules.Skills. В фикстуре список Плута СОВПАДАЕТ со всем
            // справочником, поэтому такой мутант там неразличим — здесь класс нарочно уже.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scout", Name = "Разведчик", HitDie = "d8",
                SkillChoices = { "stealth" }, SkillPickCount = 1 });
            var c = Fixtures.Character(); c.ClassId = "scout";

            var options = WizardOps.AvailableSkills(c, rules);
            bool ok = options.Count == 1 && options[0].Id == "stealth" && !options[0].AlreadyOwned;
            if (!ok) Debug.LogError("FAIL список навыков класса: [" + string.Join(",", options.Select(o => o.Id)) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — без класса предлагается весь справочник навыков")]
        public void SelfTestAvailableSkillsFallBackToWholeReference()
        {
            // Мутант: «класс не найден → пустой список». Тогда шаг 6 у недоделанного листа пуст.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character(); c.ClassId = "bogus";

            var options = WizardOps.AvailableSkills(c, rules);
            bool ok = options.Count == rules.Skills.Count
                   && rules.Skills.All(s => options.Any(o => o.Id == s.Id));
            if (!ok) Debug.LogError($"FAIL запасной список навыков: {options.Count} из {rules.Skills.Count} "
                                  + "[" + string.Join(",", options.Select(o => o.Id)) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — неизвестный навык класса пропускается молча")]
        public void SelfTestAvailableSkillsSkipUnknownId()
        {
            // Мутант: убран `if (def == null) continue` — в списке появляется строка без названия.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scout", Name = "Разведчик", HitDie = "d8",
                SkillChoices = { "stealth", "bogus-skill" }, SkillPickCount = 1 });
            var c = Fixtures.Character(); c.ClassId = "scout";

            var options = WizardOps.AvailableSkills(c, rules);
            bool ok = options.Count == 1 && options[0].Id == "stealth"
                   && options.All(o => !string.IsNullOrEmpty(o.Name));
            if (!ok) Debug.LogError("FAIL неизвестный навык класса: ["
                                  + string.Join(",", options.Select(o => $"{o.Id}/{o.Name ?? "null"}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — без предыстории ничего не помечено полученным")]
        public void SelfTestAvailableSkillsWithoutBackgroundOwnNothing()
        {
            // Мутант: снят guard `bg != null` либо AlreadyOwned выставляется всем.
            var c = Fixtures.Character(); c.BackgroundId = "bogus";
            var options = WizardOps.AvailableSkills(c, Fixtures.Rules());
            bool ok = options.Count == 3 && options.All(o => !o.AlreadyOwned && o.OwnedFrom == null);
            if (!ok) Debug.LogError("FAIL без предыстории: ["
                                  + string.Join(",", options.Select(o => $"{o.Id}:{o.AlreadyOwned}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — стандартный набор это 15/14/13/12/10/8")]
        public void SelfTestStandardArray()
        {
            bool ok = WizardOps.StandardArray.SequenceEqual(new[] { 15, 14, 13, 12, 10, 8 });
            if (!ok) Debug.LogError("FAIL стандартный набор: " + string.Join(",", WizardOps.StandardArray));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — раскладка предлагается под спасброски класса")]
        public void SelfTestSuggestedAssignmentFollowsClass()
        {
            // У тестового Плута владение спасбросками «лов» и «инт» — 15 и 14 должны лечь туда,
            // а не «первым шести подряд». Мутант «всегда сил, лов, тел…» падает.
            // Сверяем ВСЮ последовательность, а не первые два: мутант «вернуть только ключевые,
            // без .Concat остальных» дал бы ровно те же два первых значения и выжил бы.
            var suggested = WizardOps.SuggestedAssignment(Fixtures.Rules().Classes[0]);
            bool ok = suggested.SequenceEqual(new[] { "dex", "int", "str", "con", "wis", "cha" });
            if (!ok) Debug.LogError("FAIL раскладка: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — без класса раскладка идёт обычным порядком")]
        public void SelfTestSuggestedAssignmentWithoutClass()
        {
            // Мутант: `cls == null` возвращает пустой список — мастер на шаге характеристик пуст.
            var suggested = WizardOps.SuggestedAssignment(null);
            bool ok = suggested.SequenceEqual(new[] { "str", "dex", "con", "int", "wis", "cha" });
            if (!ok) Debug.LogError("FAIL раскладка без класса: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — владение спасброском вне шести характеристик не попадает в раскладку")]
        public void SelfTestSuggestedAssignmentIgnoresUnknownSave()
        {
            // Мутант: снят фильтр `.Where(all.Contains)` — в раскладке семь строк, и первая из них
            // не характеристика вовсе.
            var cls = new ClassDef { Id = "bard", Name = "Бард", HitDie = "d8",
                SaveProficiencies = { "cha", "luck" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "cha", "str", "dex", "con", "int", "wis" });
            if (!ok) Debug.LogError("FAIL раскладка с чужим спасброском: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена класса перечисляет, что потеряется")]
        public void SelfTestClassChangeListsLosses()
        {
            var rules = Fixtures.Rules();
            var fighter = new ClassDef { Id = "fighter", Name = "Воин", HitDie = "d10",
                SaveProficiencies = { "str", "con" }, SkillChoices = { "athletics" }, SkillPickCount = 2 };
            for (int lv = 1; lv <= 20; lv++) fighter.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(fighter);

            var c = Fixtures.Character();
            c.SubclassId = "thief";
            var losses = WizardOps.DescribeClassChange(c, rules, "fighter");
            // «Скрытность» и «Магия» не входят в список Воина — они потеряются; компетентность и
            // подкласс тоже. Мутант «молча выбросить» даёт пустой список.
            bool ok = losses.Any(l => l.Contains("Скрытность"))
                   && losses.Any(l => l.Contains("омпетентн"))
                   && losses.Any(l => l.Contains("одкласс"));
            if (!ok) Debug.LogError("FAIL потери при смене класса: " + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена на неизвестный класс ничего не обещает потерять")]
        public void SelfTestClassChangeToUnknownClassSaysNothing()
        {
            // Мутант: снят `if (next == null) return` — на пустом месте либо падение, либо
            // «потеряются все навыки» у класса, которого нет.
            var losses = WizardOps.DescribeClassChange(Fixtures.Character(), Fixtures.Rules(), "bogus");
            bool ok = losses.Count == 0;
            if (!ok) Debug.LogError("FAIL неизвестный класс: " + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — считаются только пометки плана про подкласс")]
        public void SelfTestClassChangeCountsSubclassPlanMarks()
        {
            // Мутант: считается ВЕСЬ план (было бы 3) либо строка не появляется вовсе.
            var rules = KeeperRules(out var c);          // класс, где ничего другого не теряется
            c.Plan.Add(new LevelChoice { Level = 3, Kind = "subclass", ValueId = "thief" });
            c.Plan.Add(new LevelChoice { Level = 9, Kind = "subclass", ValueId = "thief" });
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "asi" });

            var losses = WizardOps.DescribeClassChange(c, rules, "keeper");
            bool ok = losses.Count == 1 && losses[0].Contains("2") && !losses[0].Contains("3");
            if (!ok) Debug.LogError("FAIL пометки плана: " + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — когда терять нечего, список пуст (компетентность сохраняется)")]
        public void SelfTestClassChangeWithNothingLostIsSilent()
        {
            // Страж от ложных срабатываний. Убивает мутантов «строка про подкласс всегда»,
            // «строка про компетентность без проверки next.ExpertiseLevel == 0», «все навыки теряются».
            var rules = KeeperRules(out var c);
            bool hasExpertise = c.ExpertiseIds.Count > 0;
            var losses = WizardOps.DescribeClassChange(c, rules, "keeper");
            bool ok = hasExpertise && losses.Count == 0;
            if (!ok) Debug.LogError($"FAIL ложные потери (компетентность есть={hasExpertise}): "
                                  + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — без компетентности про неё не спрашивают")]
        public void SelfTestClassChangeWithoutExpertiseIsSilent()
        {
            // Вторая половина того же И: мутант, снявший `file.ExpertiseIds.Count > 0`, обещает
            // потерю компетентности тому, у кого её нет.
            var rules = KeeperRules(out var c);
            var plain = rules.Classes.First(x => x.Id == "keeper");
            plain.ExpertiseLevel = 0;                 // у нового класса компетентности нет…
            c.ExpertiseIds.Clear();                   // …но и терять нечего

            var losses = WizardOps.DescribeClassChange(c, rules, "keeper");
            bool ok = losses.Count == 0;
            if (!ok) Debug.LogError("FAIL потеря несуществующей компетентности: " + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — неизвестный навык назван своим идентификатором")]
        public void SelfTestClassChangeNamesUnknownSkillById()
        {
            // Мутант: `def.Name` вместо `def?.Name ?? id` — падение вместо честной строки.
            var rules = KeeperRules(out var c);
            c.SkillIds.Add("bogus-skill");
            var losses = WizardOps.DescribeClassChange(c, rules, "keeper");
            bool ok = losses.Count == 1 && losses[0].Contains("bogus-skill");
            if (!ok) Debug.LogError("FAIL неизвестный навык в потерях: " + string.Join("; ", losses));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена класса не трогает предысторию и характеристики")]
        public void SelfTestClassChangeKeepsBackgroundAndAbilities()
        {
            var rules = Fixtures.Rules();
            var fighter = new ClassDef { Id = "fighter", Name = "Воин", HitDie = "d10",
                SkillChoices = { "athletics" }, SkillPickCount = 2 };
            for (int lv = 1; lv <= 20; lv++) fighter.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(fighter);

            var c = Fixtures.Character();
            WizardOps.ApplyClassChange(c, rules, "fighter");
            bool ok = c.ClassId == "fighter"
                   && c.BackgroundId == "soldier"
                   && c.Base.Dex == 15
                   && c.Bumps.Count == 2                       // прибавки предыстории на месте
                   && !c.SkillIds.Contains("stealth")          // навык не из списка Воина — ушёл
                   && c.ExpertiseIds.Count == 0
                   && c.SubclassId == null;
            if (!ok) Debug.LogError($"FAIL смена класса: класс={c.ClassId}, предыстория={c.BackgroundId}, "
                                  + $"прибавок={c.Bumps.Count}, навыки=[{string.Join(",", c.SkillIds)}], "
                                  + $"подкласс={c.SubclassId ?? "null"}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — новый класс с компетентностью чистит только осиротевшую")]
        public void SelfTestApplyClassChangeKeepsSurvivingExpertise()
        {
            // Ветка else: мутант «Clear() в обоих случаях» оставил бы 0, мутант «в else не чистить»
            // оставил бы 2 (в том числе компетентность в навыке, которого больше нет).
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "shadow", Name = "Тень", HitDie = "d8",
                SkillChoices = { "stealth" }, SkillPickCount = 1,
                ExpertiseLevel = 1, ExpertisePickCount = 1 });

            var c = Fixtures.Character();
            c.ExpertiseIds.Clear();
            c.ExpertiseIds.Add("stealth");
            c.ExpertiseIds.Add("arcana");

            WizardOps.ApplyClassChange(c, rules, "shadow");
            bool ok = c.SkillIds.Count == 1 && c.SkillIds[0] == "stealth"
                   && c.ExpertiseIds.Count == 1 && c.ExpertiseIds[0] == "stealth";
            if (!ok) Debug.LogError($"FAIL уцелевшая компетентность: навыки=[{string.Join(",", c.SkillIds)}], "
                                  + $"компетентность=[{string.Join(",", c.ExpertiseIds)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена на неизвестный класс не трогает файл вовсе")]
        public void SelfTestApplyUnknownClassChangesNothing()
        {
            var c = Fixtures.Character();
            c.Plan.Add(new LevelChoice { Level = 3, Kind = "subclass", ValueId = "thief" });
            WizardOps.ApplyClassChange(c, Fixtures.Rules(), "bogus");
            bool ok = c.ClassId == "rogue" && c.SubclassId == "thief"
                   && c.SkillIds.Count == 2 && c.ExpertiseIds.Count == 1 && c.Plan.Count == 1;
            if (!ok) Debug.LogError($"FAIL неизвестный класс правит файл: класс={c.ClassId}, "
                                  + $"подкласс={c.SubclassId ?? "null"}, навыков={c.SkillIds.Count}, "
                                  + $"компетентность={c.ExpertiseIds.Count}, план={c.Plan.Count}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — из плана уходят только пометки подкласса")]
        public void SelfTestApplyClassChangeKeepsOtherPlanMarks()
        {
            // Мутант: `file.Plan.Clear()` — вместе с подклассом стирается намеченное повышение.
            var rules = KeeperRules(out var c);
            c.Plan.Add(new LevelChoice { Level = 3, Kind = "subclass", ValueId = "thief" });
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "asi" });

            WizardOps.ApplyClassChange(c, rules, "keeper");
            bool ok = c.Plan.Count == 1 && c.Plan[0].Kind == "asi" && c.Plan[0].Level == 4;
            if (!ok) Debug.LogError("FAIL пометки плана после смены: ["
                                  + string.Join(",", c.Plan.Select(p => $"{p.Level}:{p.Kind}")) + "]");
            Done(ok);
        }

        /// <summary>Справочник с классом «keeper», при переходе в который НИЧЕГО не теряется: он
        /// разрешает оба навыка персонажа и у него есть компетентность. Персонаж — без подкласса и
        /// без пометок плана. Всё, что после этого попадёт в список потерь, — ложное срабатывание.</summary>
        static RulesData KeeperRules(out CharacterFile c)
        {
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "keeper", Name = "Хранитель", HitDie = "d8",
                SkillChoices = { "stealth", "arcana", "athletics" }, SkillPickCount = 2,
                ExpertiseLevel = 1, ExpertisePickCount = 1 });
            c = Fixtures.Character();
            c.SubclassId = null;
            c.Plan.Clear();
            return rules;
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
