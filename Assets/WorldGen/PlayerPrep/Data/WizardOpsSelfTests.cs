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

        [ContextMenu("Self-Test: мастер — без ключевой характеристики раскладка идёт по спасброскам")]
        public void SelfTestSuggestedAssignmentFollowsClass()
        {
            // ЗАПАСНАЯ ВЕТКА: у тестового Плута PrimaryAbilities не заполнены (так выглядит чужой
            // справочник по правилам 2014), и тогда решают спасброски — «лов» и «инт». Мутант
            // «нет ключевых → обычный порядок сил, лов, тел…» падает.
            // Сверяем ВСЮ последовательность, а не первые два: мутант «вернуть только ключевые,
            // без .Concat остальных» дал бы ровно те же два первых значения и выжил бы.
            // Третьим стоит «тел», а не «сил»: Ловкость уже занята ключевыми, и из пары
            // «Телосложение, Ловкость» остаётся одно Телосложение.
            var suggested = WizardOps.SuggestedAssignment(Fixtures.Rules().Classes[0]);
            bool ok = suggested.SequenceEqual(new[] { "dex", "int", "con", "str", "wis", "cha" });
            if (!ok) Debug.LogError("FAIL раскладка: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — ключевая характеристика важнее спасбросков (Чародей)")]
        public void SelfTestSuggestedAssignmentPrefersPrimaryAbilityOverSaves()
        {
            // ФИКСТУРА, ГДЕ СТАРОЕ И НОВОЕ ПРАВИЛО РАСХОДЯТСЯ. Настоящий Чародей: спасброски
            // Тел+Хар, ключевая — одна Харизма.
            //   • мутант «порядок по спасбросках» (прежний код) даёт «тел» первым — 15 ляжет в
            //     Телосложение, и первый в жизни игрок соберёт Чародея, который плохо колдует;
            //   • мутант «сперва ключевые, потом спасброски, потом Телосложение с Ловкостью, потом
            //     остальные» даёт «хар, тел, сил, лов, инт, мдр» — Ловкость уезжает на четвёртое
            //     место; его убивает сверка ВСЕЙ последовательности, а не первой позиции.
            var cls = new ClassDef { Id = "sorcerer", Name = "Чародей", HitDie = "d6",
                PrimaryAbilities = { "cha" }, SaveProficiencies = { "con", "cha" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "cha", "con", "dex", "str", "int", "wis" });
            if (!ok) Debug.LogError("FAIL раскладка Чародея: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — в раскладку идут ОБЕ ключевые характеристики (Монах)")]
        public void SelfTestSuggestedAssignmentTakesEveryPrimaryAbility()
        {
            // Вторая расходящаяся фикстура. Настоящий Монах: спасброски Сил+Лов, ключевые —
            // Ловкость И Мудрость, причём Мудрости в спасбросках нет вовсе.
            //   • мутант «порядок по спасброскам» даёт «сил» первым;
            //   • мутант «взять только первую ключевую» даёт «лов, тел, сил, инт, мдр, хар» —
            //     Мудрость уезжает на пятое место, к десятке.
            var cls = new ClassDef { Id = "monk", Name = "Монах", HitDie = "d8",
                PrimaryAbilities = { "dex", "wis" }, SaveProficiencies = { "str", "dex" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "dex", "wis", "con", "str", "int", "cha" });
            if (!ok) Debug.LogError("FAIL раскладка Монаха: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — за ключевой идут Телосложение и Ловкость, а не Сила (Волшебник)")]
        public void SelfTestSuggestedAssignmentPutsConAndDexRightAfterPrimary()
        {
            // МУТАНТ, КОТОРОГО ЭТА ПРОВЕРКА ОБЯЗАНА УБИТЬ: «после ключевых — остальное обычным
            // порядком» (то есть UniversallyUseful выкинут). Он даёт «инт, СИЛ, лов, ТЕЛ, мдр, хар»,
            // и Волшебник получает Силу 14 при Телосложении 12 — Сила ему не нужна ни для чего, а
            // каждая единица Телосложения это хит за КАЖДЫЙ уровень до двадцатого.
            //
            // ФИКСТУРА — ЗАКЛИНАТЕЛЬ С ОДНОЙ КЛЮЧЕВОЙ, и это не украшение. У Воина ключевые «сил» и
            // «дех», у Варвара — «сил», и там старое правило со новым расходятся на одну позицию
            // из шести: проверка вышла бы почти пустой. С одной ключевой характеристикой они
            // расходятся сразу со второй позиции, где и лежит четырнадцать.
            //
            // Спасброски у Волшебника «инт»+«мдр» — они здесь заполнены нарочно, чтобы заодно
            // умер мутант «ключевые и спасброски вперемешку» (он дал бы «инт, мдр, тел, лов, …»).
            var cls = new ClassDef { Id = "wizard", Name = "Волшебник", HitDie = "d6",
                PrimaryAbilities = { "int" }, SaveProficiencies = { "int", "wis" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "int", "con", "dex", "str", "wis", "cha" });
            if (!ok) Debug.LogError("FAIL раскладка Волшебника: " + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — чужая и сдвоенная ключевая характеристика не ломают раскладку")]
        public void SelfTestSuggestedAssignmentIgnoresUnknownAndDoubledPrimaryAbility()
        {
            // Та же пара мутантов, что стережёт спасброски, но на НОВОЙ ветке — они не общие:
            //   • снят фильтр `.Where(all.Contains)` — в раскладке семь строк, и вторая из них
            //     не характеристика вовсе;
            //   • снят `.Distinct()` — «хар» встречается дважды, строк опять семь против шести
            //     значений стандартного набора.
            var cls = new ClassDef { Id = "bogus", Name = "Кривой", HitDie = "d8",
                PrimaryAbilities = { "cha", "luck", "cha" }, SaveProficiencies = { "con" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "cha", "con", "dex", "str", "int", "wis" });
            if (!ok) Debug.LogError($"FAIL кривая ключевая характеристика ({suggested.Count} шт.): "
                                  + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «PrimaryAbilities: null» из JSON не роняет раскладку")]
        public void SelfTestSuggestedAssignmentSurvivesNullPrimaryAbilities()
        {
            // Newtonsoft на «"PrimaryAbilities": null» кладёт в поле null МИМО инициализатора поля,
            // так что чужой справочник это состояние выдаёт легко. Мутант — снятый `?? new List`:
            // не FAIL, а падение мастера на шаге характеристик, и это тоже FAIL.
            var cls = new ClassDef { Id = "old", Name = "Старый", HitDie = "d8",
                SaveProficiencies = { "wis", "cha" } };
            cls.PrimaryAbilities = null;
            var suggested = WizardOps.SuggestedAssignment(cls);
            // Ключевых нет — работает запасной ход по спасброскам.
            bool ok = suggested.SequenceEqual(new[] { "wis", "cha", "con", "dex", "str", "int" });
            if (!ok) Debug.LogError("FAIL раскладка при null: " + string.Join(",", suggested));
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
            bool ok = suggested.SequenceEqual(new[] { "cha", "con", "dex", "str", "int", "wis" });
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
            // «строка про компетентность без проверки !next.HasExpertise()», «все навыки теряются».
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
            plain.ExpertiseGrants.Clear();            // у нового класса компетентности нет…
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
                ExpertiseGrants = { new ExpertiseGrant { Level = 1, PickCount = 1 } } });

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

        [ContextMenu("Self-Test: мастер — новый класс снимает компетентность, которой не даёт (и говорит об этом)")]
        public void SelfTestClassChangeDropsExpertiseOutsideNewClassChoices()
        {
            // Живой случай на поставляемых данных: Плут с компетентностью в Проницательности
            // переходит в Волшебника. НАВЫК уцелевает — он есть в списке Волшебника, — а
            // компетентность в нём класс не даёт (умение «Учёный» называет шесть других навыков).
            //
            // МУТАНТ №1: в ApplyClassChange осталось только `!file.SkillIds.Contains(id)`. Тогда
            // компетентность в Проницательности остаётся в файле, экран её больше не рисует
            // (снять нечем), и лист удваивал бы мастерство вопреки правилам.
            // МУТАНТ №2: снятие есть, а строки в диалоге нет — мастер молча отбирает то, о чём
            // обещал спросить. Обе стороны обязаны считать по одному AllowsExpertiseIn.
            // МУТАНТ №3: строка выдаётся и на навыки, которые и так теряются, — та же потеря
            // названа игроку дважды.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scholar", Name = "Учёный", HitDie = "d6",
                SkillChoices = { "stealth", "arcana" }, SkillPickCount = 2,
                ExpertiseGrants = { new ExpertiseGrant { Level = 1, PickCount = 1 } },
                ExpertiseChoices = { "arcana" } });          // компетентность только в Магии

            var c = Fixtures.Character();                     // навыки: скрытность, магия
            c.ExpertiseIds.Clear();
            c.ExpertiseIds.Add("stealth");                    // …а компетентность — в Скрытности

            var losses = WizardOps.DescribeClassChange(c, rules, "scholar");
            bool warned = losses.Any(l => l.Contains("Компетентность") && l.Contains("Скрытность"));
            bool noDoubleTalk = losses.Count(l => l.Contains("Скрытность")) == 1;

            WizardOps.ApplyClassChange(c, rules, "scholar");
            bool ok = warned && noDoubleTalk
                   && c.SkillIds.Count == 2                   // сам навык уцелел
                   && c.SkillIds.Contains("stealth")
                   && c.ExpertiseIds.Count == 0;              // а компетентность на нём — нет
            if (!ok) Debug.LogError($"FAIL компетентность вне списка нового класса: предупредил="
                                  + $"{warned}, без повтора={noDoubleTalk}, навыки=["
                                  + string.Join(",", c.SkillIds) + "], компетентность=["
                                  + string.Join(",", c.ExpertiseIds) + "], потери=["
                                  + string.Join(" | ", losses) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — компетентность на навыке предыстории переживает смену класса")]
        public void SelfTestClassChangeKeepsExpertiseOnBackgroundSkill()
        {
            // Предыстория смену класса переживает целиком — и навык «атлетика», который она даёт, и
            // компетентность на нём, раз новый класс компетентность в этом навыке разрешает.
            //
            // МУТАНТ: в ApplyClassChange владение снова считается по одному file.SkillIds. Навыка
            // предыстории там нет по построению (ApplyBackgroundChange его оттуда убирает), поэтому
            // компетентность молча исчезала бы при первой же смене класса — а диалог о ней даже не
            // предупреждал, потому что терять её не собирался.
            // ВТОРОЙ МУТАНТ (ложная тревога): строка потери выдаётся на компетентность, которая
            // никуда не денется, — мастер обещает отобрать то, что оставляет.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "warden", Name = "Страж", HitDie = "d10",
                SkillChoices = { "arcana" }, SkillPickCount = 1,
                ExpertiseGrants = { new ExpertiseGrant { Level = 1, PickCount = 1 } } });

            var c = Fixtures.Character();               // предыстория «Солдат» даёт «атлетику»
            c.ExpertiseIds.Clear();
            c.ExpertiseIds.Add("athletics");            // компетентность на навыке ПРЕДЫСТОРИИ

            var losses = WizardOps.DescribeClassChange(c, rules, "warden");
            bool silent = !losses.Any(l => l.Contains("омпетентность"));

            WizardOps.ApplyClassChange(c, rules, "warden");
            var ath = SheetMath.Compute(c, rules).Skills.First(s => s.SkillId == "athletics");
            bool ok = silent
                   && c.ExpertiseIds.SequenceEqual(new[] { "athletics" })
                   && ath.Expertise && ath.Bonus == 6;   // СИЛ 10 → +0, мастерство +3 вдвое
            if (!ok) Debug.LogError($"FAIL компетентность предыстории при смене класса: молчал={silent}, "
                                  + "компетентность=[" + string.Join(",", c.ExpertiseIds) + "], атлетика "
                                  + $"{ath.Bonus} комп={ath.Expertise} (ждали 6/да); потери=["
                                  + string.Join(" | ", losses) + "]");
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

        [ContextMenu("Self-Test: мастер — лишние сверх нормы навыки названы и сняты")]
        public void SelfTestClassChangeTrimsSkillsOverTheCap()
        {
            // РАЗВЕДЕНИЕ «отсеяно по списку» и «отсеяно по количеству»: ОБА навыка персонажа
            // входят в список Стража, но Страж даёт на выбор один. Мутант «фильтровать только по
            // членству в списке» оставит оба и промолчит.
            var rules = Fixtures.Rules();
            // Стартовый набор — тот же, что у персонажа: иначе в потерях появилась бы вторая строка,
            // про снаряжение, и счёт строк перестал бы говорить о навыках.
            var warden = new ClassDef { Id = "warden", Name = "Страж", HitDie = "d10",
                SkillChoices = { "stealth", "athletics", "arcana" }, SkillPickCount = 1,
                StartingEquipment = { "leather" } };
            for (int lv = 1; lv <= 20; lv++) warden.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(warden);

            var c = Fixtures.Character();
            c.SubclassId = null; c.Plan.Clear(); c.ExpertiseIds.Clear();
            c.SkillIds.Clear(); c.SkillIds.Add("stealth"); c.SkillIds.Add("arcana");

            var losses = WizardOps.DescribeClassChange(c, rules, "warden");
            // Отбор с КОНЦА: уходит «Магия», остаётся «Скрытность». И причина другая — не
            // «в списке нет», а «класс даёт только столько».
            bool ok = losses.Count == 1
                   && losses[0].Contains("Магия")
                   && !losses[0].Contains("в списке нет");

            WizardOps.ApplyClassChange(c, rules, "warden");
            // Описание и применение обязаны совпасть дословно: снят ровно названный навык.
            ok = ok && c.SkillIds.Count == 1 && c.SkillIds[0] == "stealth";
            if (!ok) Debug.LogError($"FAIL лишние навыки: потери=[{string.Join("; ", losses)}], "
                                  + $"осталось=[{string.Join(",", c.SkillIds)}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — пометки на несуществующих ячейках названы и сняты")]
        public void SelfTestClassChangeDropsPlanMarksWithoutCell()
        {
            // У Плута ячейки повышения на 4 и 8; на 6 (как у Воина) её нет. Пометка на 6 уровне
            // дошла бы до листа и выдала бы черту по ячейке, которой не существует.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.SubclassId = null; c.Plan.Clear();
            c.Plan.Add(new LevelChoice { Level = 6, Kind = "feat", ValueId = "alert" });  // ячейки нет
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "feat", ValueId = "alert" });  // ячейка есть
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "bogus" });                    // вид неизвестен

            var losses = WizardOps.DescribeClassChange(c, rules, "rogue");
            // Ровно ДВЕ осиротевшие: пометка не на своём уровне и пометка неизвестного вида.
            // Мутант «не смотреть на уровень» насчитал бы одну, мутант «неизвестный вид оставить» —
            // тоже одну, а мутант «ячейка всегда есть» — ни одной.
            bool ok = losses.Count == 1 && losses[0].Contains("2") && !losses[0].Contains("1");

            WizardOps.ApplyClassChange(c, rules, "rogue");
            // Мутант «снять все пометки повышения» оставил бы 0, мутант «не снимать ничего» — 3.
            ok = ok && c.Plan.Count == 1 && c.Plan[0].Level == 4 && c.Plan[0].Kind == "feat";
            if (!ok) Debug.LogError($"FAIL пометки без ячейки: потери=[{string.Join("; ", losses)}], "
                                  + $"план=[{string.Join(",", c.Plan.Select(p => $"{p.Level}:{p.Kind}"))}]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «осталось выбрать» считает только навыки класса")]
        public void SelfTestRemainingSkillPicksCountOnlyClassSkills()
        {
            // У Разведчика в списке «скрытность» и «атлетика», даёт два. Персонаж взял
            // «скрытность» (из списка) и «магию» (НЕ из списка), а «атлетика» пришла даром от
            // предыстории. Верно: занята ОДНА ячейка из двух.
            // Мутант «минус все взятые» даст 0; мутант «минус ещё и навыки предыстории» — тоже 0.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scout", Name = "Разведчик", HitDie = "d8",
                SkillChoices = { "stealth", "athletics" }, SkillPickCount = 2 });
            var c = Fixtures.Character(); c.ClassId = "scout";

            int left = WizardOps.RemainingSkillPicks(c, rules);
            bool ok = left == 1;
            if (!ok) Debug.LogError($"FAIL осталось выбрать: {left}, ждали 1 "
                                  + $"(навыки=[{string.Join(",", c.SkillIds)}], предыстория даёт «athletics»)");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «осталось выбрать» не уходит в минус")]
        public void SelfTestRemainingSkillPicksNeverGoNegative()
        {
            // Класс даёт один навык, а в файле их два из его списка — так бывает после смены
            // класса в старом файле. Мутант без Math.Max покажет «осталось −1».
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scout", Name = "Разведчик", HitDie = "d8",
                SkillChoices = { "stealth", "arcana" }, SkillPickCount = 1 });
            var c = Fixtures.Character(); c.ClassId = "scout";

            int left = WizardOps.RemainingSkillPicks(c, rules);
            bool ok = left == 0;
            if (!ok) Debug.LogError($"FAIL осталось выбрать при переборе: {left}, ждали 0");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — сдвоенный спасбросок не удваивает раскладку")]
        public void SelfTestSuggestedAssignmentDropsDoubledSave()
        {
            // RulesIntegrity уникальность спасбросков не проверяет. Без Distinct вышло бы семь
            // идентификаторов против шести значений стандартного набора.
            var cls = new ClassDef { Id = "twin", Name = "Двойник", HitDie = "d8",
                SaveProficiencies = { "wis", "wis", "cha" } };
            var suggested = WizardOps.SuggestedAssignment(cls);
            bool ok = suggested.SequenceEqual(new[] { "wis", "cha", "con", "dex", "str", "int" });
            if (!ok) Debug.LogError($"FAIL сдвоенный спасбросок ({suggested.Count} шт.): "
                                  + string.Join(",", suggested));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — класс без списка навыков даёт пустой выбор, а не весь справочник")]
        public void SelfTestEmptySkillChoicesGiveEmptyList()
        {
            // Мутант «нет класса ИЛИ список пуст → весь справочник» подсунул бы игроку навыки,
            // которых класс не даёт.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "blank", Name = "Пустой", HitDie = "d8" });
            var c = Fixtures.Character(); c.ClassId = "blank";
            var options = WizardOps.AvailableSkills(c, rules);
            bool ok = options.Count == 0;
            if (!ok) Debug.LogError("FAIL пустой список класса: ["
                                  + string.Join(",", options.Select(o => o.Id)) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — на недостающих данных отвечает пусто, а не падает")]
        public void SelfTestGuardsAgainstMissingData()
        {
            // То же соглашение, что у SheetMath.Compute: мастер по построению работает с
            // НЕДОДЕЛАННЫМ персонажем. Снятый страж даёт не FAIL, а падение — и это тоже FAIL.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            bool ok = WizardOps.AvailableSkills(null, rules).Count == 0
                   && WizardOps.AvailableSkills(c, null).Count == 0
                   && WizardOps.RemainingSkillPicks(null, rules) == 0
                   && WizardOps.RemainingSkillPicks(c, null) == 0
                   && WizardOps.DescribeClassChange(null, rules, "rogue").Count == 0
                   && WizardOps.DescribeClassChange(c, null, "rogue").Count == 0;

            ok = ok
                   && WizardOps.StartingEquipment(null, rules).Count == 0
                   && WizardOps.StartingEquipment(c, null).Count == 0
                   && WizardOps.PointBuySpent(null) < 0
                   && !WizardOps.IsPointBuyLegal(null)
                   && !WizardOps.CanLowerByPointBuy(null, "str");

            WizardOps.ApplyClassChange(null, rules, "rogue");
            WizardOps.ApplyClassChange(c, null, "rogue");
            WizardOps.ApplyBackgroundChange(null, rules, "soldier");
            WizardOps.ApplyBackgroundChange(c, null, "soldier");
            WizardOps.ResetEquipment(null, rules);
            WizardOps.ResetEquipment(c, null);
            WizardOps.ApplyPointBuyStart(null);
            ok = ok && c.ClassId == "rogue" && c.SkillIds.Count == 2
                    && c.BackgroundId == "soldier" && c.Equipment.Count == 2;
            if (!ok) Debug.LogError($"FAIL стражи мастера: класс={c.ClassId}, навыков={c.SkillIds.Count}, "
                                  + $"предыстория={c.BackgroundId}, снаряжения={c.Equipment.Count}");
            Done(ok);
        }

        // ── Покупка очков ────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: мастер — таблица стоимости очков нелинейна на 14 и 15")]
        public void SelfTestPointBuyCostTable()
        {
            // Сверяется ВСЯ таблица разом. Главный мутант — «цена = значение минус восемь»: он
            // совпадает с правилами на 8…13 и расходится ровно там, где таблица ломается, — 14
            // стоит 7, а не 6, и 15 стоит 9, а не 7.
            var got = new[] { 8, 9, 10, 11, 12, 13, 14, 15 }.Select(WizardOps.PointBuyCost).ToList();
            bool ok = got.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5, 7, 9 });
            if (!ok) Debug.LogError("FAIL таблица стоимости: " + string.Join(",", got));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — значения вне 8…15 покупкой недостижимы")]
        public void SelfTestPointBuyCostRefusesUnreachableScores()
        {
            // Мутант: «чего нет в таблице — стоит 0». Тогда 18 из бросков въезжает в покупку очков
            // бесплатно, и режим, существующий ради законности персонажа, пропускает незаконного.
            var got = new[] { 0, 3, 7, 16, 18, 20 }.Select(WizardOps.PointBuyCost).ToList();
            bool ok = got.All(v => v < 0);
            if (!ok) Debug.LogError("FAIL недостижимые значения: " + string.Join(",", got));
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — потрачено считается по всем шести характеристикам")]
        public void SelfTestPointBuySpentSumsAllSix()
        {
            // 15,14,13,12,11,10 = 9+7+5+4+3+2 = 30. Ни одна из шести не стоит 0 и все стоимости
            // разные — значит мутант, пропустивший ЛЮБУЮ характеристику, даст другое число (а на
            // наборе с восьмёркой пропуск именно её остался бы незаметен).
            var scores = new AbilityScores { Str = 15, Dex = 14, Con = 13, Int = 12, Wis = 11, Cha = 10 };
            int spent = WizardOps.PointBuySpent(scores);
            // 30 больше бюджета — заодно падает мутант «законно всё, что лежит в 8…15».
            bool ok = spent == 30 && !WizardOps.IsPointBuyLegal(scores);
            if (!ok) Debug.LogError($"FAIL потрачено: {spent} (ждали 30), "
                                  + $"законность={WizardOps.IsPointBuyLegal(scores)} (ждали ложь)");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — стандартный набор стоит ровно весь бюджет")]
        public void SelfTestStandardArrayCostsTheWholeBudget()
        {
            // Правило вслух: 15,14,13,12,10,8 — это и есть 27 очков, поэтому оба способа дают
            // персонажей одной силы. Опечатка в ЛЮБОЙ из шести использованных строк таблицы сдвигает
            // эту сумму, а значит проверка стережёт таблицу целиком, а не одну её клетку.
            var scores = new AbilityScores { Str = 15, Dex = 14, Con = 13, Int = 12, Wis = 10, Cha = 8 };
            int spent = WizardOps.PointBuySpent(scores);
            bool ok = spent == WizardOps.PointBuyBudget && spent == 27 && WizardOps.IsPointBuyLegal(scores);
            if (!ok) Debug.LogError($"FAIL стоимость стандартного набора: {spent} (ждали 27), "
                                  + $"бюджет={WizardOps.PointBuyBudget}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — набор с недостижимым значением не считается вовсе")]
        public void SelfTestPointBuySpentRefusesUnreachableSet()
        {
            // Мутант: пропускать недостижимые молча (или считать их нулём) — тогда персонаж с 18
            // показал бы «потрачено 18 из 27», и покупка очков подтвердила бы невозможное.
            var rolled = new AbilityScores { Str = 18, Dex = 14, Con = 13, Int = 12, Wis = 10, Cha = 8 };
            bool ok = WizardOps.PointBuySpent(rolled) < 0 && !WizardOps.IsPointBuyLegal(rolled);
            if (!ok) Debug.LogError($"FAIL набор с 18: потрачено={WizardOps.PointBuySpent(rolled)}, "
                                  + $"законность={WizardOps.IsPointBuyLegal(rolled)}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — покупка начинается с восьмёрок во всех шести")]
        public void SelfTestPointBuyStartLaysDownEights()
        {
            // Мутант «трогать только то, что вне 8…15» оставил бы 15, 14 и 13 на месте, и игрок
            // начал бы покупку, уже потратив 21 очко из 27, ничего об этом не зная.
            var scores = new AbilityScores { Str = 18, Dex = 15, Con = 14, Int = 13, Wis = 10, Cha = 3 };
            WizardOps.ApplyPointBuyStart(scores);
            var got = SheetMath.AbilityOrder().Select(scores.Get).ToList();
            bool ok = got.All(v => v == 8) && WizardOps.PointBuySpent(scores) == 0;
            if (!ok) Debug.LogError("FAIL начало покупки: " + string.Join(",", got)
                                  + $", потрачено={WizardOps.PointBuySpent(scores)}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «+» упирается и в потолок, и в бюджет")]
        public void SelfTestCanRaiseStopsAtCeilingAndBudget()
        {
            // ДВА РАЗНЫХ ЗАПРЕТА в одной фикстуре, потому что мутант, забывший один из них, второй
            // сохраняет и «в среднем» выглядит рабочим.
            //   • 15 не поднимается НИКОГДА — даже когда очков вагон (здесь потрачено 9 из 27);
            //   • 8 поднимается, пока бюджет позволяет.
            var roomy = new AbilityScores { Str = 15, Dex = 8, Con = 8, Int = 8, Wis = 8, Cha = 8 };
            bool ceiling = !WizardOps.CanRaiseByPointBuy(roomy, "str")
                        && WizardOps.CanRaiseByPointBuy(roomy, "dex");

            // 15,15,15,8,8,8 = 9+9+9 = 27, бюджет исчерпан ровно. Мутант «бюджет не проверять»
            // разрешит поднять восьмёрку за последнее несуществующее очко.
            var spent = new AbilityScores { Str = 15, Dex = 15, Con = 15, Int = 8, Wis = 8, Cha = 8 };
            bool budget = !WizardOps.CanRaiseByPointBuy(spent, "int")
                       && WizardOps.PointBuySpent(spent) == 27;

            bool ok = ceiling && budget;
            if (!ok) Debug.LogError($"FAIL «+»: потолок={ceiling}, бюджет={budget} "
                                  + $"(потрачено {WizardOps.PointBuySpent(spent)})");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «+» считает подорожание, а не одно очко")]
        public void SelfTestCanRaisePaysTheStepPrice()
        {
            // 15,15,14,9,8,8 = 9+9+7+1 = 26. В запасе РОВНО ОДНО очко, и оно всё решает:
            //   • 14 → 15 стоит ДВА (7→9) — нельзя;
            //   • 9 → 10 стоит одно (1→2) — можно, впритык, ровно в 27.
            // Мутант «шаг стоит одно очко» разрешил бы первое; мутант «строго меньше бюджета»
            // запретил бы второе. Проверка убивает обоих сразу.
            var scores = new AbilityScores { Str = 15, Dex = 15, Con = 14, Int = 9, Wis = 8, Cha = 8 };
            int spent = WizardOps.PointBuySpent(scores);
            bool ok = spent == 26
                   && !WizardOps.CanRaiseByPointBuy(scores, "con")
                   && WizardOps.CanRaiseByPointBuy(scores, "int");
            if (!ok) Debug.LogError($"FAIL цена шага: потрачено={spent} (ждали 26), "
                                  + $"тел(14→15)={WizardOps.CanRaiseByPointBuy(scores, "con")} (ждали ложь), "
                                  + $"инт(9→10)={WizardOps.CanRaiseByPointBuy(scores, "int")} (ждали истину)");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — «−» упирается в восьмёрку, но выпускает из незаконного")]
        public void SelfTestCanLowerStopsAtTheFloor()
        {
            // Мутант «смотреть на стоимость, а не на значение»: у 18 стоимости нет вовсе (−1),
            // и такой мутант запер бы игрока в режиме, где «+» погашен бюджетом, а «−» — законом.
            var scores = new AbilityScores { Str = 18, Dex = 9, Con = 8, Int = 8, Wis = 8, Cha = 8 };
            bool ok = WizardOps.CanLowerByPointBuy(scores, "str")     // из незаконного есть дорога вниз
                   && WizardOps.CanLowerByPointBuy(scores, "dex")
                   && !WizardOps.CanLowerByPointBuy(scores, "con");   // 8 — пол
            if (!ok) Debug.LogError($"FAIL «−»: сил18={WizardOps.CanLowerByPointBuy(scores, "str")}, "
                                  + $"лов9={WizardOps.CanLowerByPointBuy(scores, "dex")}, "
                                  + $"тел8={WizardOps.CanLowerByPointBuy(scores, "con")}");
            Done(ok);
        }

        // ── Стартовый набор ──────────────────────────────────────────────────────

        [ContextMenu("Self-Test: мастер — набор складывается из класса и предыстории без повторов")]
        public void SelfTestStartingEquipmentJoinsClassAndBackground()
        {
            // У Плута «leather» и «rope», у Солдата — «rope». Мутант «просто сложить списки» даст
            // три строки и положит верёвку в файл дважды; мутант «источник у повтора переписать»
            // покажет верёвку как вещь одной лишь предыстории.
            var rules = Fixtures.Rules();
            var offered = WizardOps.StartingEquipment(Fixtures.Character(), rules);
            bool ok = offered.Select(o => o.Id).SequenceEqual(new[] { "leather", "rope" })
                   && offered[0].Name == "Кожаный доспех"
                   && offered[0].Source == "класс"
                   && offered[1].Source == "класс и предыстория";
            if (!ok) Debug.LogError("FAIL стартовый набор: ["
                                  + string.Join("; ", offered.Select(o => $"{o.Id}/{o.Name}/{o.Source}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — без класса набор всё равно даёт вещи предыстории")]
        public void SelfTestStartingEquipmentWithoutClassStillOffersBackground()
        {
            // Мутант: «класса нет → набора нет». Тогда игрок, выбравший предысторию раньше класса,
            // видит на седьмом шаге пустоту, а верёвка Солдата не попадает в файл вовсе.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character(); c.ClassId = null;
            var offered = WizardOps.StartingEquipment(c, rules);
            bool ok = offered.Count == 1 && offered[0].Id == "rope" && offered[0].Source == "предыстория";
            if (!ok) Debug.LogError("FAIL набор без класса: ["
                                  + string.Join("; ", offered.Select(o => $"{o.Id}/{o.Source}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — неизвестный предмет назван своим идентификатором")]
        public void SelfTestStartingEquipmentNamesUnknownItemById()
        {
            // Мутант: `def.Name` вместо `?.Name ?? id` — падение мастера на опечатке в справочнике;
            // мутант «пропустить неизвестное» — тихо пропавшая строка и вещь, которой нет ни на
            // экране, ни в файле, хотя класс её даёт.
            var rules = Fixtures.Rules();
            rules.Classes.Add(new ClassDef { Id = "scout", Name = "Разведчик", HitDie = "d8",
                StartingEquipment = { "bogus-item" } });
            var c = Fixtures.Character(); c.ClassId = "scout";
            var offered = WizardOps.StartingEquipment(c, rules);
            bool ok = offered.Count == 2 && offered[0].Id == "bogus-item" && offered[0].Name == "bogus-item";
            if (!ok) Debug.LogError("FAIL неизвестный предмет: ["
                                  + string.Join("; ", offered.Select(o => $"{o.Id}/{o.Name}")) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена класса пересобирает снаряжение целиком")]
        public void SelfTestResetEquipmentSwapsTheKitOnClassChange()
        {
            // Тот самый тихо испорченный файл: Плут → Воин. Кольчуга обязана появиться, кожаный
            // доспех — исчезнуть (иначе лист посчитает по нему класс доспеха), а верёвка остаться:
            // её даёт предыстория, и класс к ней отношения не имеет.
            //   • мутант «только дописывать» оставит «leather» — доспех, которого у персонажа нет;
            //   • мутант «стереть всё и положить набор класса» унесёт верёвку Солдата;
            //   • мутант «добавлять не глядя» после второго вызова даст «chain» дважды.
            var rules = Fixtures.Rules();
            var fighter = new ClassDef { Id = "fighter", Name = "Воин", HitDie = "d10",
                SkillChoices = { "athletics" }, SkillPickCount = 2, StartingEquipment = { "chain" } };
            for (int lv = 1; lv <= 20; lv++) fighter.Levels.Add(new ClassLevel { Level = lv });
            rules.Classes.Add(fighter);

            var c = Fixtures.Character();
            WizardOps.ApplyClassChange(c, rules, "fighter");
            WizardOps.ResetEquipment(c, rules);
            WizardOps.ResetEquipment(c, rules);           // второй раз ничего не удваивает

            bool ok = c.Equipment.Count == 2
                   && c.Equipment.Contains("chain")
                   && c.Equipment.Contains("rope")
                   && !c.Equipment.Contains("leather");
            if (!ok) Debug.LogError("FAIL пересборка набора: [" + string.Join(",", c.Equipment) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена класса называет теряемое снаряжение поимённо")]
        public void SelfTestClassChangeNamesTheEquipmentItLoses()
        {
            // В4: диалог обещал меньше, чем делает. Названы должны быть ровно те вещи, которых
            // ПОСЛЕ пересборки не станет.
            //   • мутант «перечислить всё снаряжение персонажа» назовёт верёвку, которая останется;
            //   • мутант «строки нет» промолчит про доспех, который уйдёт.
            var rules = Fixtures.Rules();
            var fighter = new ClassDef { Id = "fighter", Name = "Воин", HitDie = "d10",
                SkillChoices = { "stealth", "arcana" }, SkillPickCount = 2,
                ExpertiseGrants = { new ExpertiseGrant { Level = 1, PickCount = 1 } },
                StartingEquipment = { "chain" } };
            for (int lv = 1; lv <= 20; lv++)
                fighter.Levels.Add(new ClassLevel { Level = lv, Choice = lv == 4 || lv == 8 ? "asi" : null });
            rules.Classes.Add(fighter);

            var c = Fixtures.Character();
            c.SubclassId = null; c.Plan.Clear();          // чтобы в потерях осталось одно снаряжение
            var losses = WizardOps.DescribeClassChange(c, rules, "fighter");
            bool ok = losses.Count == 1
                   && losses[0].Contains("Кожаный доспех")
                   && !losses[0].Contains("Верёвка");
            if (!ok) Debug.LogError("FAIL потери снаряжения: [" + string.Join("; ", losses) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — про снаряжение молчат, когда набор тот же")]
        public void SelfTestClassChangeSaysNothingWhenTheKitSurvives()
        {
            // Страж от ложного срабатывания, и он же — страж молчаливого ПЕРВОГО выбора класса:
            // непустой список потерь заставляет мастера показать диалог, а первый в жизни выбор
            // класса вопросов задавать не должен.
            var rules = KeeperRules(out var c);           // у Хранителя тот же кожаный доспех
            var losses = WizardOps.DescribeClassChange(c, rules, "keeper");

            var fresh = new CharacterFile { BackgroundId = "soldier" };   // класса нет вовсе
            var firstPick = WizardOps.DescribeClassChange(fresh, rules, "keeper");

            bool ok = losses.Count == 0 && firstPick.Count == 0;
            if (!ok) Debug.LogError($"FAIL ложные потери снаряжения: [{string.Join("; ", losses)}] / "
                                  + $"первый выбор: [{string.Join("; ", firstPick)}]");
            Done(ok);
        }

        // ── Смена предыстории ────────────────────────────────────────────────────

        [ContextMenu("Self-Test: мастер — предыстория освобождает ячейки навыков, которые даёт даром")]
        public void SelfTestBackgroundChangeFreesSkillSlotsItGrants()
        {
            // В1 целиком: Плут взял «атлетику» и «магию» руками, потом выбрал предысторию, которая
            // «атлетику» даёт даром. Верно — «атлетика» уходит из ручного выбора, ячейка
            // освобождается, и остаётся выбрать ещё один навык. Мутант «просто поменять
            // идентификатор предыстории» оставит остаток 0: заголовок скажет «выбрано 2 из 2»,
            // галочек будет видно одна, и взять второй навык будет нельзя.
            var rules = Fixtures.Rules();
            rules.Backgrounds.Add(new BackgroundDef { Id = "thug", Name = "Громила", Text = "…",
                AbilityChoices = { "str", "dex", "con" }, OriginFeatId = "alert",
                SkillIds = { "athletics" } });

            var c = Fixtures.Character();
            c.BackgroundId = "soldier";
            c.SkillIds.Clear(); c.SkillIds.Add("athletics"); c.SkillIds.Add("arcana");
            c.ExpertiseIds.Clear();

            WizardOps.ApplyBackgroundChange(c, rules, "thug");
            int left = WizardOps.RemainingSkillPicks(c, rules);
            bool ok = c.BackgroundId == "thug"
                   && c.SkillIds.SequenceEqual(new[] { "arcana" })     // «магию» не тронули
                   && left == 1;
            if (!ok) Debug.LogError($"FAIL смена предыстории: предыстория={c.BackgroundId}, "
                                  + $"навыки=[{string.Join(",", c.SkillIds)}], осталось={left} (ждали 1)");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — владение навыком предыстории не теряется")]
        public void SelfTestBackgroundChangeKeepsProficiencyItself()
        {
            // Снятый из ручного выбора навык обязан остаться У ПЕРСОНАЖА — его теперь даёт
            // предыстория. Мутант «снять и не дать взамен» лишил бы игрока владения молча, и
            // это была бы починка хуже поломки. Спрашиваем лист, а не мастер.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.BackgroundId = null;
            c.SkillIds.Clear(); c.SkillIds.Add("athletics");
            c.ExpertiseIds.Clear();

            WizardOps.ApplyBackgroundChange(c, rules, "soldier");   // Солдат даёт «атлетику»
            var line = SheetMath.Compute(c, rules).Skills.First(s => s.SkillId == "athletics");
            bool ok = !c.SkillIds.Contains("athletics") && line.Proficient;
            if (!ok) Debug.LogError($"FAIL владение после смены предыстории: в ручном выборе="
                                  + $"{c.SkillIds.Contains("athletics")}, владение на листе={line.Proficient}");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — компетентность переживает переезд навыка в предысторию")]
        public void SelfTestBackgroundChangeKeepsExpertiseOnSkillItGrants()
        {
            // Новая предыстория даёт «скрытность» даром, поэтому из РУЧНОГО выбора она уходит. Но
            // владение навыком остаётся, а компетентность держится именно на владении — снять её
            // было бы починкой хуже поломки: игрок терял бы выбор, ничего не меняя по существу.
            //
            // МУТАНТ — прежняя строка `file.ExpertiseIds.RemoveAll(id => !file.SkillIds.Contains(id))`
            // в ApplyBackgroundChange: она снимала компетентность ровно с тех навыков, которые новая
            // предыстория и даёт. Спрашиваем и файл, и ЛИСТ: удвоение обязано остаться на месте.
            var rules = Fixtures.Rules();
            rules.Backgrounds.Add(new BackgroundDef { Id = "thug", Name = "Громила", Text = "…",
                AbilityChoices = { "str", "dex", "con" }, OriginFeatId = "alert",
                SkillIds = { "stealth" } });

            var c = Fixtures.Character();
            c.SkillIds.Clear(); c.SkillIds.Add("stealth"); c.SkillIds.Add("arcana");
            c.ExpertiseIds.Clear(); c.ExpertiseIds.Add("stealth");

            WizardOps.ApplyBackgroundChange(c, rules, "thug");
            var st = SheetMath.Compute(c, rules).Skills.First(s => s.SkillId == "stealth");
            bool ok = c.SkillIds.SequenceEqual(new[] { "arcana" })      // из ручного выбора ушла
                   && c.ExpertiseIds.SequenceEqual(new[] { "stealth" })  // а компетентность осталась
                   && st.Proficient && st.Expertise && st.Bonus == 8;    // ЛОВ +2, мастерство +3 вдвое
            if (!ok) Debug.LogError("FAIL компетентность после смены предыстории: навыки=["
                                  + string.Join(",", c.SkillIds) + "], компетентность=["
                                  + string.Join(",", c.ExpertiseIds) + $"], скрытность {st.Bonus} "
                                  + $"влад={st.Proficient} комп={st.Expertise} (ждали 8/да/да)");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — компетентность на потерянном навыке перестаёт считаться, и лист просит выбрать заново")]
        public void SelfTestExpertiseOnSkillTheNewBackgroundDoesNotGrantStopsCounting()
        {
            // Обратный случай к предыдущему и САМАЯ ОПАСНАЯ половина починки. Компетентность стояла
            // на навыке, который давала СТАРАЯ предыстория; новая его не даёт, в ручном выборе его
            // тоже нет — владения не осталось.
            //
            // МУТАНТ: фильтр при чтении (SheetMath.ExpertiseChosenIds) проверяет только разрешение
            // класса, без владения. Тогда счёт занят навыком, которого у персонажа нет: «выбрано 1
            // из 1», строки в мастере не рисуется (навыком не владеем — в список годных он не
            // попадает), снять нечем, выбрать вместо неё нельзя. Тупик без выхода, ради которого
            // фильтр и стоит при ЧТЕНИИ, а не при записи.
            //
            // Файл при этом НЕ чистится нарочно: вернёт игрок прежнюю предысторию — вернётся и
            // компетентность, тем же приёмом устроено понижение уровня.
            var rules = Fixtures.Rules();
            rules.Backgrounds.Add(new BackgroundDef { Id = "thug", Name = "Громила", Text = "…",
                AbilityChoices = { "str", "dex", "con" }, OriginFeatId = "alert",
                SkillIds = { "stealth" } });

            var c = Fixtures.Character();
            c.BackgroundId = "thug";                       // «скрытность» приходит от предыстории…
            c.SkillIds.Clear(); c.SkillIds.Add("arcana");
            c.ExpertiseIds.Clear(); c.ExpertiseIds.Add("stealth");

            WizardOps.ApplyBackgroundChange(c, rules, "soldier");   // …а Солдат даёт «атлетику»
            var d = SheetMath.Compute(c, rules);
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = !st.Proficient && !st.Expertise
                   && d.Missing.Any(m => m.Contains("Компетентность выбрана в 0"))
                   && c.ExpertiseIds.SequenceEqual(new[] { "stealth" });
            if (!ok) Debug.LogError($"FAIL потерянная компетентность: скрытность влад={st.Proficient} "
                                  + $"комп={st.Expertise} (ждали нет/нет), в файле ["
                                  + string.Join(",", c.ExpertiseIds) + "]; чего не хватает: ["
                                  + string.Join(" | ", d.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: мастер — смена на неизвестную предысторию не трогает файл вовсе")]
        public void SelfTestUnknownBackgroundChangesNothing()
        {
            // То же правило, что у ApplyClassChange с неизвестным классом: неизвестный
            // идентификатор не записывается даже сам по себе, иначе лист получил бы предысторию,
            // которой в справочнике нет, и потерял бы вместе с ней прибавки.
            var c = Fixtures.Character();
            WizardOps.ApplyBackgroundChange(c, Fixtures.Rules(), "bogus");
            bool ok = c.BackgroundId == "soldier" && c.SkillIds.Count == 2 && c.ExpertiseIds.Count == 1;
            if (!ok) Debug.LogError($"FAIL неизвестная предыстория: {c.BackgroundId}, "
                                  + $"навыков={c.SkillIds.Count}, компетентность={c.ExpertiseIds.Count}");
            Done(ok);
        }

        /// <summary>Справочник с классом «keeper», при переходе в который НИЧЕГО не теряется: он
        /// разрешает оба навыка персонажа, даёт ровно столько же навыков на выбор, имеет
        /// компетентность, ячейки повышения там же, где Плут, И ТОТ ЖЕ СТАРТОВЫЙ НАБОР. Персонаж —
        /// без подкласса и без пометок плана. Всё, что после этого попадёт в список потерь, —
        /// ложное срабатывание.
        ///
        /// Кожаный доспех у Хранителя стоит нарочно: у персонажа в снаряжении «leather» и «rope»,
        /// верёвку даёт предыстория, и без доспеха в этом списке «ничего не теряется» перестало бы
        /// быть правдой — а тесты, считающие строки потерь, начали бы падать по делу.</summary>
        static RulesData KeeperRules(out CharacterFile c)
        {
            var rules = Fixtures.Rules();
            var keeper = new ClassDef { Id = "keeper", Name = "Хранитель", HitDie = "d8",
                SkillChoices = { "stealth", "arcana", "athletics" }, SkillPickCount = 2,
                ExpertiseGrants = { new ExpertiseGrant { Level = 1, PickCount = 1 } },
                StartingEquipment = { "leather" } };
            for (int lv = 1; lv <= 20; lv++)
                keeper.Levels.Add(new ClassLevel
                {
                    Level = lv,
                    Choice = lv == 3 ? "subclass" : (lv == 4 || lv == 8 ? "asi" : null)
                });
            rules.Classes.Add(keeper);
            c = Fixtures.Character();
            c.SubclassId = null;
            c.Plan.Clear();
            return rules;
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
