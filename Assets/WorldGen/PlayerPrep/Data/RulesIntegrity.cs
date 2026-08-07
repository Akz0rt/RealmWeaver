using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Структурные проверки справочника. Запускаются и над синтетическими фикстурами,
    /// и над ПОСТАВЛЯЕМЫМ файлом — поэтому ловят опечатки в данных, а не только в коде.</summary>
    public static class RulesIntegrity
    {
        static readonly string[] Abilities = { "str", "dex", "con", "int", "wis", "cha" };

        public static List<string> Check(RulesData r)
        {
            var errors = new List<string>();
            if (r == null) { errors.Add("справочник пуст"); return errors; }
            if (string.IsNullOrWhiteSpace(r.Attribution))
                errors.Add("не заполнено авторство (Attribution) — требование лицензии CC-BY");

            var skillIds = new HashSet<string>(r.Skills.Select(s => s.Id));
            var itemIds = new HashSet<string>(r.Items.Select(i => i.Id));
            var featIds = new HashSet<string>(r.Feats.Select(f => f.Id));

            foreach (var s in r.Skills)
                if (!Abilities.Contains(s.AbilityId))
                    errors.Add($"навык {s.Id}: характеристика «{s.AbilityId}» не из шести известных");

            foreach (var c in r.Classes)
            {
                var byLevel = new HashSet<int>(c.Levels.Select(l => l.Level));
                for (int lv = 1; lv <= 20; lv++)
                    if (!byLevel.Contains(lv)) errors.Add($"класс {c.Id}: нет уровня {lv}");
                foreach (var group in c.Levels.GroupBy(l => l.Level).Where(g => g.Count() > 1))
                    errors.Add($"класс {c.Id}: уровень {group.Key} встречается {group.Count()} раза");
                foreach (var sk in c.SkillChoices)
                    if (!skillIds.Contains(sk)) errors.Add($"класс {c.Id}: навык {sk} отсутствует в Skills");
                foreach (var save in c.SaveProficiencies)
                    if (!Abilities.Contains(save)) errors.Add($"класс {c.Id}: спасбросок «{save}» не из шести");
                foreach (var it in c.StartingEquipment)
                    if (!itemIds.Contains(it)) errors.Add($"класс {c.Id}: предмет {it} отсутствует в Items");
                foreach (var l in c.Levels.Where(l => l.SpellSlots != null && l.SpellSlots.Length != 9))
                    errors.Add($"класс {c.Id}, уровень {l.Level}: в таблице слотов {l.SpellSlots.Length} чисел вместо 9");
                if (c.ExpertiseLevel > 0 && c.ExpertisePickCount <= 0)
                    errors.Add($"класс {c.Id}: компетентность объявлена на уровне {c.ExpertiseLevel}, но брать нечего");

                // Опечатка в Choice («ASI», «asi ») молча означала бы «выбора нет» — целый уровень
                // повышения характеристик исчез бы из данных, а гейт остался зелёным.
                foreach (var l in c.Levels.Where(l => l.Choice != null
                                                      && l.Choice != "subclass" && l.Choice != "asi"))
                    errors.Add($"класс {c.Id}, уровень {l.Level}: неизвестный вид выбора «{l.Choice}»");

                if (c.Levels.Any(l => l.Choice == "subclass") && c.Subclasses.Count == 0)
                    errors.Add($"класс {c.Id}: уровень выбора подкласса есть, а подклассов нет ни одного");
                foreach (var group in c.Subclasses.GroupBy(s => s.Id).Where(g => g.Count() > 1))
                    errors.Add($"класс {c.Id}: подкласс {group.Key} объявлен {group.Count()} раза");
                foreach (var sub in c.Subclasses)
                    foreach (var sl in sub.Levels.Where(sl => sl.Level < 1 || sl.Level > 20))
                        errors.Add($"класс {c.Id}, подкласс {sub.Id}: уровень {sl.Level} вне 1–20");
            }

            foreach (var b in r.Backgrounds)
            {
                if (b.AbilityChoices.Count != 3)
                    errors.Add($"предыстория {b.Id}: должно быть ровно 3 характеристики на выбор, а их {b.AbilityChoices.Count}");
                foreach (var a in b.AbilityChoices)
                    if (!Abilities.Contains(a)) errors.Add($"предыстория {b.Id}: характеристика «{a}» не из шести");
                foreach (var sk in b.SkillIds)
                    if (!skillIds.Contains(sk)) errors.Add($"предыстория {b.Id}: навык {sk} отсутствует в Skills");
                foreach (var it in b.Equipment)
                    if (!itemIds.Contains(it)) errors.Add($"предыстория {b.Id}: предмет {it} отсутствует в Items");
                if (string.IsNullOrEmpty(b.OriginFeatId) || !featIds.Contains(b.OriginFeatId))
                    errors.Add($"предыстория {b.Id}: черта происхождения «{b.OriginFeatId}» отсутствует в Feats");
            }

            foreach (var f in r.Feats)
                if (f.Category != "origin" && f.Category != "general"
                    && f.Category != "fighting-style" && f.Category != "epic-boon")
                    errors.Add($"черта {f.Id}: неизвестный разряд «{f.Category}»");

            return errors;
        }
    }
}
