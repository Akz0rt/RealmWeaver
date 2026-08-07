using System.Collections.Generic;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Синтетический справочник для самопроверок арифметики. НЕ SRD: числа подобраны так,
    /// чтобы верная формула и правдоподобный мутант давали РАЗНЫЕ ответы.
    ///
    /// Три расхождения встроены нарочно:
    ///   1. персонаж 5 УРОВНЯ, а не 1 — мастерство +3 против +2; на первом уровне неверная формула
    ///      неотличима от верной;
    ///   2. у класса владение спасбросками ЕСТЬ в «лов» и НЕТ в «тел» — мутант «мастерство ко всем
    ///      спасброскам» и мутант «ни к одному» оба падают;
    ///   3. прибавка предыстории кладётся НЕ в ключевую характеристику класса — мутант «прибавлять
    ///      к лучшей характеристике» падает.</summary>
    public static class Fixtures
    {
        public static RulesData Rules()
        {
            var r = new RulesData { Id = "test", Title = "Тестовый", Attribution = "CC-BY 4.0" };
            r.Skills.Add(new SkillDef { Id = "stealth",  Name = "Скрытность",  AbilityId = "dex" });
            r.Skills.Add(new SkillDef { Id = "athletics", Name = "Атлетика",   AbilityId = "str" });
            r.Skills.Add(new SkillDef { Id = "arcana",   Name = "Магия",       AbilityId = "int" });
            r.Items.Add(new ItemDef { Id = "leather", Name = "Кожаный доспех", Kind = "armor", ArmorBase = 11, MaxDexBonus = -1 });
            r.Items.Add(new ItemDef { Id = "chain",   Name = "Кольчуга",       Kind = "armor", ArmorBase = 16, MaxDexBonus = 0 });
            r.Items.Add(new ItemDef { Id = "shield",  Name = "Щит",            Kind = "shield" });
            r.Items.Add(new ItemDef { Id = "rope",    Name = "Верёвка",        Kind = "gear" });
            r.Feats.Add(new FeatDef { Id = "alert", Name = "Бдительный", Text = "…", Category = "origin" });

            r.Races.Add(new RaceDef
            {
                Id = "halfling", Name = "Полурослик", Speed = 30,
                Traits = { new FeatureRef { Id = "lucky", Name = "Везучий", Text = "…" } }
            });

            // Ключевая характеристика этого класса — Ловкость. Прибавка предыстории ниже пойдёт
            // НЕ в неё (см. Character()).
            var rogue = new ClassDef
            {
                Id = "rogue", Name = "Плут", Blurb = "…", HitDie = "d8",
                SaveProficiencies = { "dex", "int" },      // «тел» НЕТ — это и есть расхождение №2
                SkillChoices = { "stealth", "athletics", "arcana" }, SkillPickCount = 2,
                ExpertiseLevel = 1, ExpertisePickCount = 1,
                StartingEquipment = { "leather", "rope" }
            };
            for (int lv = 1; lv <= 20; lv++)
                rogue.Levels.Add(new ClassLevel
                {
                    Level = lv,
                    Choice = lv == 3 ? "subclass" : (lv == 4 || lv == 8 ? "asi" : null),
                    Features = lv == 2
                        ? new List<FeatureRef> { new FeatureRef { Id = "cunning", Name = "Хитрое действие", Text = "…" } }
                        : new List<FeatureRef>()
                });
            // Умение подкласса стоит на 3 уровне — ниже текущего пятого, но ВЫШЕ второго.
            // Так фикстура разводит правило «умения подкласса по текущий уровень» и мутанта
            // «умения подкласса применяются всегда».
            rogue.Subclasses.Add(new SubclassDef
            {
                Id = "thief", Name = "Вор", Text = "…",
                Levels =
                {
                    new SubclassLevel { Level = 3, Features = { new FeatureRef { Id = "fast-hands", Name = "Быстрые руки", Text = "…" } } }
                }
            });
            r.Classes.Add(rogue);

            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "soldier", Name = "Солдат", Text = "…",
                AbilityChoices = { "str", "dex", "con" },
                OriginFeatId = "alert",
                SkillIds = { "athletics" },
                Equipment = { "rope" }
            });
            return r;
        }

        /// <summary>Плут 5 уровня. СИЛ 8, ЛОВ 15, ТЕЛ 14, ИНТ 12, МДР 10, ХАР 13 до прибавок;
        /// предыстория даёт +2 в СИЛУ и +1 в ТЕЛ — то есть НЕ в ключевую Ловкость.</summary>
        public static CharacterFile Character()
        {
            return new CharacterFile
            {
                RulesId = "test", Name = "Тест", Level = 5,
                RaceId = "halfling", ClassId = "rogue", BackgroundId = "soldier",
                SubclassId = "thief",     // на 3 уровне у класса есть выбор подкласса, а персонаж
                                          // 5-го — без него он считался бы незаконченным
                Base = new AbilityScores { Str = 8, Dex = 15, Con = 14, Int = 12, Wis = 10, Cha = 13 },
                Bumps =
                {
                    new AbilityBump { Source = "background", AbilityId = "str", Amount = 2 },
                    new AbilityBump { Source = "background", AbilityId = "con", Amount = 1 }
                },
                SkillIds = { "stealth", "arcana" },
                ExpertiseIds = { "stealth" },
                Equipment = { "leather", "rope" }
            };
        }
    }
}
