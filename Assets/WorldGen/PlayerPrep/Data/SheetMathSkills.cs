using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public static partial class SheetMath
    {
        static void ComputeSkillsAcAndFeatures(DerivedSheet d, CharacterFile file, RulesData rules,
            RaceDef race, ClassDef cls, BackgroundDef bg, int level)
        {
            // ── Навыки ────────────────────────────────────────────────────────────────────────
            // Владение приходит из двух источников — предыстория даёт фиксированные, класс
            // добирается вручную. Набор, а не список: взять один навык дважды нельзя.
            var proficient = new HashSet<string>(file.SkillIds);
            if (bg != null) foreach (var s in bg.SkillIds) proficient.Add(s);

            bool expertiseUnlocked = cls != null && cls.ExpertiseLevel > 0 && level >= cls.ExpertiseLevel;
            var expertise = new HashSet<string>(expertiseUnlocked ? file.ExpertiseIds : new List<string>());

            foreach (var skill in rules.Skills)
            {
                int mod = d.Modifiers.Get(skill.AbilityId);
                bool prof = proficient.Contains(skill.Id);
                bool exp = prof && expertise.Contains(skill.Id);
                // Компетентность удваивает МАСТЕРСТВО, а не весь бонус.
                int bonus = mod + (prof ? d.ProficiencyBonus * (exp ? 2 : 1) : 0);
                string abilityName = AbilityNames[System.Array.IndexOf(AbilityIds, skill.AbilityId)].ToLowerInvariant();
                string explain = $"{Signed(mod)} {abilityName}";
                if (exp) explain += $", {Signed(d.ProficiencyBonus * 2)} компетентность";
                else if (prof) explain += $", {Signed(d.ProficiencyBonus)} мастерство";
                d.Skills.Add(new SkillLine
                {
                    SkillId = skill.Id, Name = skill.Name, AbilityId = skill.AbilityId,
                    Proficient = prof, Expertise = exp, Bonus = bonus, Explain = explain,
                    Hint = abilityName + (exp ? " · компетентность" : "")
                });
            }
            foreach (var id in file.SkillIds.Where(id => rules.Skills.All(s => s.Id != id)))
                d.UnknownIds.Add($"навык: неизвестно «{id}»");

            // ── Класс доспеха ─────────────────────────────────────────────────────────────────
            var worn = file.Equipment
                .Select(id => rules.Items.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null).ToList();
            var armor = worn.FirstOrDefault(i => i.Kind == "armor");
            bool hasShield = worn.Any(i => i.Kind == "shield");
            int shield = hasShield ? 2 : 0;

            if (armor != null)
            {
                int dex = armor.MaxDexBonus < 0 ? d.Modifiers.Dex
                                                : System.Math.Min(d.Modifiers.Dex, armor.MaxDexBonus);
                d.ArmorClass = armor.ArmorBase + dex + shield;
                d.ArmorClassExplain = $"{armor.Name} {armor.ArmorBase}, {Signed(dex)} ловкость"
                                    + (hasShield ? ", +2 щит" : "");
            }
            else if (cls != null && !string.IsNullOrEmpty(cls.UnarmoredDefenseAbility))
            {
                // Защита без доспехов действует ТОЛЬКО без доспехов — надел кольчугу, потерял.
                int extraId = System.Array.IndexOf(AbilityIds, cls.UnarmoredDefenseAbility);
                int extra = d.Modifiers.Get(cls.UnarmoredDefenseAbility);
                d.ArmorClass = 10 + d.Modifiers.Dex + extra + shield;
                d.ArmorClassExplain = $"10, {Signed(d.Modifiers.Dex)} ловкость, {Signed(extra)} "
                                    + AbilityNames[extraId].ToLowerInvariant()
                                    + (hasShield ? ", +2 щит" : "");
            }
            else
            {
                d.ArmorClass = 10 + d.Modifiers.Dex + shield;
                d.ArmorClassExplain = $"10, {Signed(d.Modifiers.Dex)} ловкость" + (hasShield ? ", +2 щит" : "");
            }
            foreach (var id in file.Equipment.Where(id => rules.Items.All(i => i.Id != id)))
                d.UnknownIds.Add($"снаряжение: неизвестно «{id}»");

            // ── Умения ────────────────────────────────────────────────────────────────────────
            if (race != null)
                foreach (var t in race.Traits)
                    d.Features.Add(new FeatureLine { Id = t.Id, Name = t.Name, Text = t.Text, Source = "race" });

            if (bg != null)
            {
                var origin = rules.Feats.FirstOrDefault(f => f.Id == bg.OriginFeatId);
                if (origin != null)
                    d.Features.Add(new FeatureLine { Id = origin.Id, Name = origin.Name, Text = origin.Text, Source = "feat" });
                else if (!string.IsNullOrEmpty(bg.OriginFeatId))
                    d.UnknownIds.Add($"черта происхождения: неизвестно «{bg.OriginFeatId}»");
            }

            if (cls != null)
                foreach (var cl in cls.Levels.Where(l => l.Level <= level).OrderBy(l => l.Level))
                    foreach (var f in cl.Features)
                        d.Features.Add(new FeatureLine { Id = f.Id, Name = f.Name, Text = f.Text, Source = "class", Level = cl.Level });

            // Подкласс — такой же справочный Id, как вид и класс: неизвестный показывается, а не
            // проглатывается. Его умения подчиняются тому же правилу «по текущий уровень».
            if (cls != null && !string.IsNullOrEmpty(file.SubclassId))
            {
                var sub = cls.Subclasses.FirstOrDefault(s => s.Id == file.SubclassId);
                if (sub == null) d.UnknownIds.Add($"подкласс: неизвестно «{file.SubclassId}»");
                else
                    foreach (var sl in sub.Levels.Where(l => l.Level <= level).OrderBy(l => l.Level))
                        foreach (var f in sl.Features)
                            d.Features.Add(new FeatureLine { Id = f.Id, Name = f.Name, Text = f.Text, Source = "class", Level = sl.Level });
            }

            // План — это план, а не состояние: записи выше текущего уровня остаются в файле,
            // но умений не дают.
            foreach (var choice in file.Plan.Where(p => p.Level <= level && p.Kind == "feat"))
            {
                var feat = rules.Feats.FirstOrDefault(f => f.Id == choice.ValueId);
                if (feat != null)
                    d.Features.Add(new FeatureLine { Id = feat.Id, Name = feat.Name, Text = feat.Text, Source = "feat", Level = choice.Level });
                else if (!string.IsNullOrEmpty(choice.ValueId))
                    d.UnknownIds.Add($"черта: неизвестно «{choice.ValueId}»");
            }

            d.SpellSlots = cls?.Levels.FirstOrDefault(l => l.Level == level)?.SpellSlots;

            // ── Чего не хватает ───────────────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(file.Name)) d.Missing.Add("Имя не задано");
            if (race == null) d.Missing.Add("Вид не выбран");
            if (cls == null) d.Missing.Add("Класс не выбран");
            if (bg == null) d.Missing.Add("Предыстория не выбрана");
            if (AbilityIds.All(id => file.Base.Get(id) == 0)) d.Missing.Add("Характеристики не разложены");
            if (bg != null)
            {
                // Не «хоть одна прибавка есть», а «разложены ПОЛНОСТЬЮ и в разрешённые
                // характеристики»: чаще всего мастер оставляет именно половину — положил +2 и ушёл.
                var fromBg = file.Bumps.Where(b => b.Source == "background").ToList();
                int total = fromBg.Sum(b => b.Amount);
                if (total != 3)
                    d.Missing.Add($"Прибавки от предыстории разложены на {total} из 3");
                foreach (var b in fromBg.Where(b => !bg.AbilityChoices.Contains(b.AbilityId)))
                    d.Missing.Add($"Прибавка положена в характеристику «{b.AbilityId}», которой предыстория «{bg.Name}» не даёт");
            }
            if (cls != null)
            {
                int picked = file.SkillIds.Count(id => cls.SkillChoices.Contains(id));
                if (picked < cls.SkillPickCount)
                    d.Missing.Add($"Навыков выбрано {picked} из {cls.SkillPickCount}");
                if (expertiseUnlocked && file.ExpertiseIds.Count < cls.ExpertisePickCount)
                    d.Missing.Add($"Компетентность выбрана в {file.ExpertiseIds.Count} навыках из {cls.ExpertisePickCount}");
                var needsSubclass = cls.Levels.Any(l => l.Level <= level && l.Choice == "subclass");
                if (needsSubclass && string.IsNullOrEmpty(file.SubclassId))
                    d.Missing.Add("Подкласс не выбран");

                // Пустая ЯЧЕЙКА ВЫБОРА на уже взятом уровне. Стало достижимо вместе с планом
                // прокачки: «поднял уровень, закрыл панель, не выбрав» — и игрок сидит на четвёртом
                // уровне с неразложенными прибавками, а лист об этом молчит.
                //
                // Подкласс здесь НАРОЧНО пропущен: про него говорит строка выше, и вторая строка о том
                // же самом была бы шумом. Заодно она умеет то, чего эта не умеет, — видеть подкласс,
                // выбранный мастером и лежащий в file.SubclassId без пометки в плане
                // (LevelPlanOps.Rows устроен так же). Считай мы такую ячейку пустой, лист ругался бы
                // на каждого персонажа, созданного мастером.
                foreach (var cl in cls.Levels.Where(l => l.Level <= level && l.Choice == "asi")
                                             .OrderBy(l => l.Level))
                    if (file.Plan.All(p => p.Level != cl.Level))
                        d.Missing.Add($"На {cl.Level} уровне не выбрано: повышение характеристик или черта");
            }
        }
    }
}
