using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public static partial class SheetMath
    {
        /// <summary>КАКИМИ НАВЫКАМИ ПЕРСОНАЖ ВЛАДЕЕТ — в порядке показа: сперва добранные у класса
        /// вручную, затем данные предысторией, без повторов. Третьего источника нет: виды навыков по
        /// правилам 2024 не дают, и поля под них у RaceDef тоже нет.
        ///
        /// Функция ОДНА на всё приложение нарочно. Владение спрашивают лист, мастер (список
        /// компетентности и его счётчик) и смена класса; пока каждый складывал два списка у себя —
        /// а мастер и вовсе смотрел в один file.SkillIds, — компетентность нельзя было поставить на
        /// навык предыстории. Правила такого различия не знают: умение Плута говорит «ты выбираешь
        /// два навыка, КОТОРЫМИ ВЛАДЕЕШЬ», не спрашивая, откуда владение пришло.
        ///
        /// Список, а не набор, потому что порядок виден игроку — по нему мастер рисует строки. Для
        /// вопроса «владеет ли» зовущему хватает Contains: навыков у персонажа единицы.</summary>
        public static List<string> ProficientSkillIds(IEnumerable<string> chosenSkillIds, BackgroundDef bg)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            if (chosenSkillIds != null)
                foreach (var id in chosenSkillIds) if (seen.Add(id)) result.Add(id);
            if (bg != null && bg.SkillIds != null)
                foreach (var id in bg.SkillIds) if (seen.Add(id)) result.Add(id);
            return result;
        }

        /// <summary>То же по файлу целиком. Отдельная перегрузка нужна потому, что смена класса
        /// спрашивает про владение, которое ЕЩЁ НЕ записано в файл (часть навыков вот-вот уйдёт), —
        /// но складывает два источника всё равно перегрузка выше, в одном экземпляре.</summary>
        public static List<string> ProficientSkillIds(CharacterFile file, RulesData rules)
        {
            if (file == null) return new List<string>();
            return ProficientSkillIds(file.SkillIds,
                rules == null ? null : rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId));
        }

        /// <summary>Навыки, на которые персонажу МОЖНО поставить компетентность: владение (см.
        /// ProficientSkillIds) И разрешение класса брать её именно в этом навыке
        /// (ClassDef.ExpertiseChoices). Волшебник с предысторией «Мудрец» видит здесь Магию и
        /// Историю — обе от предыстории, обе в шестёрке умения «Учёный», — но не Проницательность:
        /// владеть ею класс даёт, а компетентности в ней не даёт.
        ///
        /// Со справочником навыков список НАМЕРЕННО не сверяется. Неизвестный идентификатор всё
        /// равно займёт ячейку при чтении (фильтр ниже проверяет владение и разрешение, а не
        /// существование), и выброси его отсюда — мастер не нарисовал бы строку, которой ячейка
        /// занята: снять нечем, поставить новую тоже. Про такой навык говорит UnknownIds.</summary>
        public static List<string> ExpertiseEligibleIds(CharacterFile file, RulesData rules, ClassDef cls)
        {
            if (file == null || cls == null) return new List<string>();
            return ProficientSkillIds(file, rules).Where(cls.AllowsExpertiseIn).ToList();
        }

        /// <summary>Компетентность ИЗ ФАЙЛА, которая годится: та, что стоит на подходящем навыке
        /// (ExpertiseEligibleIds). Порядок — как в file.ExpertiseIds, потому что по нему отрезается
        /// лишнее.
        ///
        /// ПОТОЛОК ПО УРОВНЮ ЗДЕСЬ НЕ ПРИМЕНЯЕТСЯ, и это разделение сознательное. Лист берёт
        /// .Take(cls.ExpertisePicksAt(level)) — сверх открытого бонуса не бывает. Мастер считает по
        /// этой же функции БЕЗ потолка: Барду, опустившемуся с 9 уровня на 8, он честно говорит
        /// «выбрано 4 из 2», а не «2 из 2» при четырёх отмеченных строках, и видно, сколько снять.
        /// Само правило годности при этом у обоих одно — расхождение в нём однажды уже заперло
        /// игрока между «выбрано 0 из 1» на листе и погашенными кнопками в мастере.</summary>
        public static List<string> ExpertiseChosenIds(CharacterFile file, RulesData rules, ClassDef cls)
        {
            if (file == null || cls == null) return new List<string>();
            var eligible = ExpertiseEligibleIds(file, rules, cls);
            return file.ExpertiseIds.Where(eligible.Contains).ToList();
        }

        static void ComputeSkillsAcAndFeatures(DerivedSheet d, CharacterFile file, RulesData rules,
            RaceDef race, ClassDef cls, BackgroundDef bg, int level)
        {
            // ── Навыки ────────────────────────────────────────────────────────────────────────
            // Владение приходит из двух источников — предыстория даёт фиксированные, класс
            // добирается вручную. Складывает их ProficientSkillIds, и лист спрашивает ту же
            // функцию, что мастер: «чем персонаж владеет» — один вопрос с одним ответом.
            var proficient = new HashSet<string>(ProficientSkillIds(file.SkillIds, bg));

            // СКОЛЬКО КОМПЕТЕНТНОСТИ ОТКРЫТО — считается ПРИ ЧТЕНИИ, по нынешнему уровню, а не по
            // тому, что записано в файле. Компетентность приходит дважды (Плут на 1 и 6, Бард и
            // Следопыт на 2 и 9), а CharacterFile.ExpertiseIds — плоский список без уровней, и
            // остаётся плоским нарочно: Бард, взявший четыре навыка на 9 уровне и опустившийся до
            // 8-го, сохраняет в файле все четыре и снова получит их, поднявшись обратно.
            // Проверять правило в момент ЗАПИСИ бессмысленно, когда запись не последняя, — тем же
            // приёмом устроен потолок характеристик.
            int expertiseAllowed = cls != null ? cls.ExpertisePicksAt(level) : 0;

            // ФИЛЬТР ПРИ ЧТЕНИИ, а не только на экране мастера, и проверяет он ОБА условия годности
            // (ExpertiseChosenIds): владение навыком и разрешение класса. Файл переживает и то и
            // другое — сменил предысторию, и навык, на котором стояла компетентность, персонажу
            // больше не принадлежит; сузили список класса задним числом, и компетентность оказалась
            // вне него. Не отсеки такую строку здесь — экран её не рисует (навыка-то нет), снять
            // нечем, а счётчик говорит «выбрано 1 из 1»: тупик, из которого игроку не выбраться.
            // С фильтром она отваливается сама, «чего не хватает» просит выбрать заново, и правка
            // файла при смене предыстории не нужна вовсе.
            //
            // Лишнее СВЕРХ открытого отрезается по порядку в ExpertiseIds: правило детерминированное
            // нарочно — «какой навык важнее» программе не по силам, а «первые сколько-то» одинаково
            // считают и лист, и мастер.
            var chosenExpertise = ExpertiseChosenIds(file, rules, cls).Take(expertiseAllowed).ToList();
            var expertise = new HashSet<string>(chosenExpertise);

            foreach (var skill in rules.Skills)
            {
                int mod = d.Modifiers.Get(skill.AbilityId);
                bool prof = proficient.Contains(skill.Id);
                // Владение здесь ВТОРОЙ раз не проверяется: фильтр выше пропускает компетентность
                // только на навыке, которым персонаж владеет, и повторное условие было бы вторым
                // экземпляром того же правила — с ним мутант, снявший проверку из фильтра, оставался
                // бы незаметен на числах.
                bool exp = expertise.Contains(skill.Id);
                // Компетентность удваивает МАСТЕРСТВО, а не весь бонус.
                int bonus = mod + (prof ? d.ProficiencyBonus * (exp ? 2 : 1) : 0);
                // ЧЕРЕЗ AbilityName, а не AbilityNames[IndexOf(...)]: справочник — данные, и опечатка в
                // SkillDef.AbilityId («dexterity» вместо «dex») давала здесь IndexOf = −1 и
                // IndexOutOfRangeException внутри Compute — то есть лист не открывался вовсе. AbilityName
                // на незнакомое отвечает самим идентификатором: опечатка видна на экране, а не роняет
                // весь лист (и её же называет RulesIntegrity при загрузке справочника).
                string abilityName = AbilityName(skill.AbilityId).ToLowerInvariant();
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
                // Название характеристики — тоже через AbilityName: опечатка в
                // ClassDef.UnarmoredDefenseAbility («wisdom» вместо «wis») роняла лист Варвара и Монаха
                // с IndexOutOfRangeException, а теперь просто печатается как есть.
                int extra = d.Modifiers.Get(cls.UnarmoredDefenseAbility);
                d.ArmorClass = 10 + d.Modifiers.Dex + extra + shield;
                d.ArmorClassExplain = $"10, {Signed(d.Modifiers.Dex)} ловкость, {Signed(extra)} "
                                    + AbilityName(cls.UnarmoredDefenseAbility).ToLowerInvariant()
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

            // Дорос ли персонаж до подкласса вообще. Считается ОДИН раз и отвечает сразу на три
            // вопроса: работает ли подкласс, надо ли о нём спрашивать в «чего не хватает» и печатать
            // ли его в подзаголовке листа (d.ActiveSubclassId). Три копии этого условия — в расчёте,
            // в перечне недостающего и в слое рисования — расходились бы по очереди.
            bool subclassUnlocked = cls != null && cls.Levels.Any(l => l.Level <= level && l.Choice == "subclass");
            d.ActiveSubclassId = subclassUnlocked && !string.IsNullOrEmpty(file.SubclassId)
                ? file.SubclassId : null;

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
                // Считаются ТОЛЬКО те, что класс даёт выбрать: компетентность, которую он больше не
                // разрешает, бонуса не даёт (см. фильтр выше), и молчать о ней здесь значило бы
                // оставить игрока с листом, где недостающего не видно, а бонуса нет.
                if (expertiseAllowed > 0 && chosenExpertise.Count < expertiseAllowed)
                    d.Missing.Add($"Компетентность выбрана в {chosenExpertise.Count} "
                                + $"{SkillsInNumber(chosenExpertise.Count)} из {expertiseAllowed}");
                if (subclassUnlocked && string.IsNullOrEmpty(file.SubclassId))
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
