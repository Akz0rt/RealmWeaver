using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class RulesIntegritySelfTests : MonoBehaviour
    {
        static RulesData Minimal()
        {
            var r = new RulesData { Id = "t", Title = "т", Attribution = "CC-BY 4.0" };
            r.Skills.Add(new SkillDef { Id = "stealth", Name = "Скрытность", AbilityId = "dex" });
            r.Items.Add(new ItemDef { Id = "rope", Name = "Верёвка", Kind = "gear" });
            var c = new ClassDef { Id = "rogue", Name = "Плут", HitDie = "d8" };
            for (int i = 1; i <= 20; i++) c.Levels.Add(new ClassLevel { Level = i });
            r.Classes.Add(c);
            return r;
        }

        [ContextMenu("Self-Test: справочник — целый набор проходит")]
        public void SelfTestCleanRulesPass()
        {
            var errors = RulesIntegrity.Check(Minimal());
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL целый набор: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — пропущенный уровень класса ловится")]
        public void SelfTestMissingClassLevelCaught()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: убираем уровень 13 из СЕРЕДИНЫ, а не с конца.
            // Мутант «проверять только Levels.Count == 20» на этой фикстуре тоже упадёт (их 19),
            // поэтому вторая самопроверка ниже сохраняет счёт, но ломает номера.
            var r = Minimal();
            r.Classes[0].Levels.RemoveAll(l => l.Level == 13);
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("rogue") && e.Contains("13"));
            if (!ok) Debug.LogError("FAIL пропущенный уровень: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — сдвоенный уровень при верном счёте ловится")]
        public void SelfTestDuplicateLevelCaughtEvenWhenCountIsRight()
        {
            // Мутант, который эта фикстура убивает: «проверять Levels.Count == 20». Уровней ровно
            // 20, но два тринадцатых и ни одного четырнадцатого — счёт верен, набор нет.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 14).Level = 13;
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("rogue") && e.Contains("14"));
            if (!ok) Debug.LogError("FAIL сдвоенный уровень: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — навык класса, которого нет в Skills, ловится")]
        public void SelfTestUnknownClassSkillCaught()
        {
            var r = Minimal();
            r.Classes[0].SkillChoices.Add("acrobatics");   // в Skills её нет
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("acrobatics"));
            if (!ok) Debug.LogError("FAIL неизвестный навык класса: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — снаряжение предыстории, которого нет в Items, ловится")]
        public void SelfTestUnknownBackgroundItemCaught()
        {
            var r = Minimal();
            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "sage", Name = "Мудрец", OriginFeatId = "magic-initiate",
                AbilityChoices = new List<string> { "int", "wis", "cha" },
                SkillIds = new List<string> { "stealth" },
                Equipment = new List<string> { "quill" }      // в Items его нет
            });
            r.Feats.Add(new FeatDef { Id = "magic-initiate", Name = "Посвящённый в магию", Category = "origin" });
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("quill"));
            if (!ok) Debug.LogError("FAIL неизвестное снаряжение: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — опечатка в виде выбора ловится")]
        public void SelfTestUnknownChoiceKindCaught()
        {
            // Мутант, который эта фикстура убивает: не проверять Choice вовсе. «ASI» с большой
            // буквы молча означало бы «выбора на этом уровне нет» — целый уровень повышения
            // характеристик исчез бы из двенадцати классов, набранных руками, а гейт остался бы зелёным.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 4).Choice = "ASI";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("ASI"));
            if (!ok) Debug.LogError("FAIL вид выбора: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — уровень выбора подкласса без подклассов ловится")]
        public void SelfTestSubclassLevelWithoutSubclassesCaught()
        {
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 3).Choice = "subclass";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("подкласс"));
            if (!ok) Debug.LogError("FAIL подкласс без подклассов: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — пустая строка авторства ловится")]
        public void SelfTestEmptyAttributionCaught()
        {
            var r = Minimal(); r.Attribution = "";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("авторств"));
            if (!ok) Debug.LogError("FAIL пустое авторство: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — предыстория даёт ровно три характеристики на выбор")]
        public void SelfTestBackgroundNeedsThreeAbilityChoices()
        {
            var r = Minimal();
            r.Feats.Add(new FeatDef { Id = "alert", Name = "Бдительный", Category = "origin" });
            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "criminal", Name = "Преступник", OriginFeatId = "alert",
                AbilityChoices = new List<string> { "dex", "int" },   // две вместо трёх
                SkillIds = new List<string> { "stealth" }
            });
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("criminal"));
            if (!ok) Debug.LogError("FAIL три характеристики: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — ПОСТАВЛЯЕМЫЙ файл целостен")]
        public void SelfTestShippedRulesAreConsistent()
        {
            // Провайдера подкладывают снаружи (харнесс — с диска, Unity — из Resources). Его
            // отсутствие — ОШИБКА, а не повод пропустить: иначе единственная проверка настоящих
            // данных стала бы зелёной от того, что её не запустили.
            if (RulesTextSource.Provider == null)
            {
                Debug.LogError("FAIL поставляемый справочник: RulesTextSource.Provider не задан — "
                             + "проверка данных не выполнялась");
                return;
            }
            var rules = RulesLoader.FromJson(RulesTextSource.Provider());
            var errors = RulesIntegrity.Check(rules);
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL поставляемый справочник:\n  " + string.Join("\n  ", errors));
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
