using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Структурные проверки справочника. Запускаются и над синтетическими фикстурами,
    /// и над ПОСТАВЛЯЕМЫМ файлом — поэтому ловят опечатки в данных, а не только в коде.
    ///
    /// ДВА РОДА ПРОВЕРОК, И ВТОРОЙ ПОЯВИЛСЯ НЕ СРАЗУ. Первый — СОГЛАСОВАННОСТЬ: каждая такая
    /// проверка это `foreach` по списку, и над ПУСТЫМ списком она даёт ноль ошибок. Пока других не
    /// было, справочник с `"Classes": []` проходил гейт ровно так же, как справочник с четырьмя
    /// классами: зелёный цвет означал «ничего не противоречит», а не «данные есть». Второй род —
    /// НАЛИЧИЕ: обязательный раздел не может быть пуст, у класса не может не быть уровней повышения
    /// характеристик. Без него первый род не доказывает ничего, потому что пустота ему нравится.
    ///
    /// Отдельно проверяется всё, отсутствие чего роняет АРИФМЕТИКУ листа, а не просто молчит:
    /// `UnarmoredDefenseAbility` вне шести характеристик даёт `AbilityNames[-1]` в
    /// SheetMathSkills (лист Варвара перестаёт открываться вовсе), `Kind` с опечаткой роняет КД до
    /// 10 + ловкость, а `HitDie` не вида d8 оставляет максимум хитов нулём.</summary>
    public static class RulesIntegrity
    {
        static readonly string[] Abilities = { "str", "dex", "con", "int", "wis", "cha" };
        static readonly string[] ItemKinds = { "armor", "shield", "weapon", "gear" };

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

            // Проверки вынесены в отдельные методы НЕ ради красоты: каждая закомментированная
            // строка ниже — готовый мутант, а каждая самопроверка ниже названа так, чтобы было
            // видно, какую строку она держит. Список вызовов и список самопроверок сверяются глазом
            // за секунду.
            CheckRequiredSectionsPresent(r, errors);
            CheckUniqueIds(r, errors);
            CheckItemKinds(r, errors);
            CheckRaces(r, errors);
            CheckBackgroundSkillCount(r, errors);

            foreach (var c in r.Classes)
            {
                CheckClassHitDie(c, errors);
                CheckClassPrimaryAbilities(c, errors);
                CheckClassUnarmoredDefense(c, errors);
                CheckClassHasAbilityScoreLevels(c, errors);
                CheckClassSpellSlotsNonNegative(c, errors);
                CheckClassSpellSlotsNeverShrink(c, errors);
                CheckClassPactMagicSlots(c, errors);
                CheckClassSkillPickCount(c, errors);
                CheckClassExpertiseChoices(c, errors);
                CheckClassSubclassLevels(c, errors);
                CheckClassFeatureIdsUnique(c, errors);
            }

            return errors;
        }

        /// <summary>Пустой обязательный раздел. Единственная проверка, которая отличает
        /// «справочник целостен» от «справочника нет»: всё остальное здесь — циклы, а цикл по
        /// пустому списку молчит.</summary>
        static void CheckRequiredSectionsPresent(RulesData r, List<string> errors)
        {
            void Section(int count, string field, string human)
            {
                if (count == 0) errors.Add($"справочник: раздел {field} ({human}) пуст");
            }
            Section(r.Skills.Count, "Skills", "навыки");
            Section(r.Items.Count, "Items", "предметы");
            Section(r.Feats.Count, "Feats", "черты");
            Section(r.Races.Count, "Races", "виды");
            Section(r.Backgrounds.Count, "Backgrounds", "предыстории");
            Section(r.Classes.Count, "Classes", "классы");
        }

        /// <summary>Повторённый идентификатор. Разрешимость («такой предмет есть») проверялась и
        /// раньше, но она смотрит в HashSet и на дубль отвечает «есть, конечно». Дубль с другим
        /// текстом — это две разные записи, из которых берётся всегда первая.</summary>
        static void CheckUniqueIds(RulesData r, List<string> errors)
        {
            void Unique<T>(IEnumerable<T> items, Func<T, string> id, string human)
            {
                foreach (var g in items.GroupBy(id).Where(g => g.Count() > 1))
                    errors.Add($"справочник: {human} {g.Key} объявлен {g.Count()} раза");
            }
            Unique(r.Skills, s => s.Id, "навык");
            Unique(r.Items, i => i.Id, "предмет");
            Unique(r.Feats, f => f.Id, "черта");
            Unique(r.Races, x => x.Id, "вид");
            Unique(r.Backgrounds, b => b.Id, "предыстория");
            Unique(r.Classes, c => c.Id, "класс");
        }

        /// <summary>Разряд предмета. У черт разряд проверялся с самого начала, у предметов — нет,
        /// хотя последствие тяжелее: доспех с Kind «armour» перестаёт быть доспехом, и КД молча
        /// падает до 10 + ловкость.</summary>
        static void CheckItemKinds(RulesData r, List<string> errors)
        {
            foreach (var i in r.Items.Where(i => !ItemKinds.Contains(i.Kind)))
                errors.Add($"предмет {i.Id}: неизвестный вид «{i.Kind}»");
        }

        static void CheckRaces(RulesData r, List<string> errors)
        {
            foreach (var x in r.Races.Where(x => x.Speed <= 0))
                errors.Add($"вид {x.Id}: скорость {x.Speed} — должна быть больше нуля");
        }

        /// <summary>Предыстория даёт РОВНО два навыка (правила 2024), не «хотя бы два».</summary>
        static void CheckBackgroundSkillCount(RulesData r, List<string> errors)
        {
            foreach (var b in r.Backgrounds.Where(b => b.SkillIds.Count != 2))
                errors.Add($"предыстория {b.Id}: должно быть ровно 2 навыка, а их {b.SkillIds.Count}");
        }

        /// <summary>Условие повторяет SheetMath.ParseDie слово в слово — проверять надо ровно то,
        /// что разберёт арифметика, иначе «d 8» пройдёт здесь и даст 0 хитов там.</summary>
        static void CheckClassHitDie(ClassDef c, List<string> errors)
        {
            bool ok = !string.IsNullOrEmpty(c.HitDie) && c.HitDie[0] == 'd'
                      && int.TryParse(c.HitDie.Substring(1), out int sides) && sides > 0;
            if (!ok) errors.Add($"класс {c.Id}: кость хитов «{c.HitDie}» не вида d6, d8, d10 или d12");
        }

        /// <summary>Ключевая характеристика класса. Пусто — законно (чужой справочник по правилам
        /// 2014 такого понятия не знает, и мастер вернётся к спасброскам). Незаконно — чужой
        /// идентификатор и повтор.
        ///
        /// Проверка нужна именно потому, что опечатка здесь НИЧЕГО НЕ РОНЯЕТ и оттого невидима:
        /// «charisma» вместо «cha» SuggestedAssignment молча отфильтрует, ключевых окажется ноль,
        /// и игроку предложат раскладку в обычном порядке — ровно то поведение, ради отмены
        /// которого поле и заведено. Повтор так же молча съедается Distinct.</summary>
        static void CheckClassPrimaryAbilities(ClassDef c, List<string> errors)
        {
            if (c.PrimaryAbilities == null) return;    // «: null» из JSON — это «не заполнено»
            foreach (var a in c.PrimaryAbilities.Where(a => !Abilities.Contains(a)))
                errors.Add($"класс {c.Id}: ключевая характеристика «{a}» не из шести известных");
            foreach (var g in c.PrimaryAbilities.GroupBy(a => a).Where(g => g.Count() > 1))
                errors.Add($"класс {c.Id}: ключевая характеристика {g.Key} названа {g.Count()} раза");
        }

        /// <summary>Пусто — это законно (у одиннадцати классов из двенадцати). Незаконно —
        /// значение вне шести: SheetMathSkills ищет его через Array.IndexOf и на −1 падает с
        /// IndexOutOfRange, то есть лист не открывается вовсе.</summary>
        static void CheckClassUnarmoredDefense(ClassDef c, List<string> errors)
        {
            if (!string.IsNullOrEmpty(c.UnarmoredDefenseAbility)
                && !Abilities.Contains(c.UnarmoredDefenseAbility))
                errors.Add($"класс {c.Id}: защита без доспехов по «{c.UnarmoredDefenseAbility}» — "
                         + "характеристика не из шести известных");
        }

        /// <summary>Уровни повышения — данные без формулы (у Воина их семь, у Плута шесть, у
        /// прочих пять), поэтому потерянный уровень нечем восстановить и некому заметить.</summary>
        static void CheckClassHasAbilityScoreLevels(ClassDef c, List<string> errors)
        {
            if (!c.Levels.Any(l => l.Choice == "asi"))
                errors.Add($"класс {c.Id}: нет ни одного уровня повышения характеристик");
        }

        static void CheckClassSpellSlotsNonNegative(ClassDef c, List<string> errors)
        {
            foreach (var l in Rows(c))
                for (int ring = 0; ring < 9; ring++)
                    if (l.SpellSlots[ring] < 0)
                        errors.Add($"класс {c.Id}, уровень {l.Level}: ячеек {ring + 1} круга "
                                 + $"{l.SpellSlots[ring]} — меньше нуля");
        }

        /// <summary>Ячейки КРУГ ЗА КРУГОМ не убывают с уровнем — сравнение поколоночное, а не
        /// построчное. Разводит эти два правила ФИКСТУРА самопроверки (второй круг сперва растёт на
        /// пятом уровне и лишь потом падает на шестом), а не поставляемые данные: таблица Жреца
        /// растёт почти на каждом уровне, так что построчное «строки равны» упало бы на ней сразу и
        /// повсюду — это не тонкий случай, а очевидный.</summary>
        static void CheckClassSpellSlotsNeverShrink(ClassDef c, List<string> errors)
        {
            // Магия договора этому правилу не подчиняется НАРОЧНО: её единственный круг переезжает
            // выше, и нижний обнуляется. Её стережёт CheckClassPactMagicSlots — строже, а не слабее.
            if (c.PactMagic) return;
            var rows = Rows(c).OrderBy(l => l.Level).ToList();
            for (int i = 1; i < rows.Count; i++)
                for (int ring = 0; ring < 9; ring++)
                    if (rows[i].SpellSlots[ring] < rows[i - 1].SpellSlots[ring])
                        errors.Add($"класс {c.Id}: ячеек {ring + 1} круга на уровне {rows[i].Level} "
                                 + $"{rows[i].SpellSlots[ring]}, а на {rows[i - 1].Level} было "
                                 + $"{rows[i - 1].SpellSlots[ring]} — таблица убывает");
        }

        /// <summary>Ячейки Магии договора. Заменяет общее «круг за кругом не убывает» тремя более
        /// узкими правилами, потому что общее для Колдуна ложно, а без замены он остался бы вовсе
        /// без охраны — то есть отключённый флагом гейт был бы хуже, чем никакого флага:
        ///   • в строке ненулевой РОВНО ОДИН круг — все ячейки Колдуна одного круга;
        ///   • число ячеек с уровнем не убывает;
        ///   • круг с уровнем не понижается.
        /// Строка без ячеек вовсе законна и просто пропускается: у поставляемого Колдуна такой нет,
        /// но чужой справочник вправе начать колдовать не с первого уровня.</summary>
        static void CheckClassPactMagicSlots(ClassDef c, List<string> errors)
        {
            if (!c.PactMagic) return;
            int prevRing = 0, prevCount = 0, prevLevel = 0;
            foreach (var l in Rows(c).OrderBy(l => l.Level))
            {
                var filled = Enumerable.Range(0, 9).Where(i => l.SpellSlots[i] != 0).ToList();
                if (filled.Count > 1)
                {
                    errors.Add($"класс {c.Id}, уровень {l.Level}: ячейки Магии договора стоят сразу "
                             + $"в {filled.Count} кругах, а они все одного круга");
                    continue;
                }
                if (filled.Count == 0) continue;
                int ring = filled[0] + 1, count = l.SpellSlots[filled[0]];
                if (ring < prevRing)
                    errors.Add($"класс {c.Id}: круг ячеек Магии договора на уровне {l.Level} — {ring}, "
                             + $"а на уровне {prevLevel} был {prevRing}: круг понижается");
                if (count < prevCount)
                    errors.Add($"класс {c.Id}: ячеек Магии договора на уровне {l.Level} — {count}, "
                             + $"а на уровне {prevLevel} было {prevCount}: таблица убывает");
                prevRing = ring; prevCount = count; prevLevel = l.Level;
            }
        }

        /// <summary>Строки с таблицей ячеек ПРАВИЛЬНОЙ длины. Кривую длину ловит отдельная
        /// проверка выше; здесь она дала бы вдобавок выход за границу массива.</summary>
        static IEnumerable<ClassLevel> Rows(ClassDef c)
            => c.Levels.Where(l => l.SpellSlots != null && l.SpellSlots.Length == 9);

        static void CheckClassSkillPickCount(ClassDef c, List<string> errors)
        {
            if (c.SkillPickCount < 0)
                errors.Add($"класс {c.Id}: выбрать нужно {c.SkillPickCount} навыков — меньше нуля");
            else if (c.SkillPickCount > c.SkillChoices.Count)
                errors.Add($"класс {c.Id}: выбрать нужно {c.SkillPickCount} навыков, "
                         + $"а в списке их {c.SkillChoices.Count}");
        }

        /// <summary>Список навыков, из которых класс даёт компетентность, — ПОДМНОЖЕСТВО его же
        /// SkillChoices. Компетентность требует владения, а владение класс раздаёт только из
        /// SkillChoices, поэтому идентификатор вне этого списка не может быть выбран НИКОГДА:
        /// строка в данных есть, а на экране её нет и быть не может.
        ///
        /// Проверка нужна именно потому, что такая опечатка НИЧЕГО НЕ РОНЯЕТ: список на экране
        /// просто окажется короче, чем задумано, и заметить это может только тот, кто знает
        /// правила наизусть, — то есть ровно не тот, для кого написана эта арка. Пустой список
        /// законен: он значит «из любого навыка».</summary>
        static void CheckClassExpertiseChoices(ClassDef c, List<string> errors)
        {
            if (c.ExpertiseChoices == null) return;      // «: null» из JSON — это «не заполнено»
            foreach (var sk in c.ExpertiseChoices.Where(sk => !c.SkillChoices.Contains(sk)))
                errors.Add($"класс {c.Id}: компетентность объявлена в навыке {sk}, "
                         + "которого нет в списке навыков этого класса");
            foreach (var g in c.ExpertiseChoices.GroupBy(sk => sk).Where(g => g.Count() > 1))
                errors.Add($"класс {c.Id}: навык {g.Key} назван в списке компетентности "
                         + $"{g.Count()} раза");
        }

        /// <summary>Сдвоенный уровень У ПОДКЛАССА. У класса это проверялось с самого начала, у
        /// подкласса — нет, хотя список умений подкласса собирается тем же перебором.</summary>
        static void CheckClassSubclassLevels(ClassDef c, List<string> errors)
        {
            foreach (var sub in c.Subclasses)
                foreach (var g in sub.Levels.GroupBy(sl => sl.Level).Where(g => g.Count() > 1))
                    errors.Add($"класс {c.Id}, подкласс {sub.Id}: уровень {g.Key} "
                             + $"встречается {g.Count()} раза");
        }

        /// <summary>Идентификатор умения уникален В ПРЕДЕЛАХ КЛАССА, а не всего справочника:
        /// «Дополнительная атака» законно есть и у Воина, и у Варвара, а вот дважды у одного
        /// класса — это скопированный уровень.</summary>
        static void CheckClassFeatureIdsUnique(ClassDef c, List<string> errors)
        {
            foreach (var g in c.Levels.SelectMany(l => l.Features)
                               .GroupBy(f => f.Id).Where(g => g.Count() > 1))
                errors.Add($"класс {c.Id}: умение {g.Key} объявлено {g.Count()} раза");
            foreach (var sub in c.Subclasses)
                foreach (var g in sub.Levels.SelectMany(sl => sl.Features)
                                   .GroupBy(f => f.Id).Where(g => g.Count() > 1))
                    errors.Add($"класс {c.Id}, подкласс {sub.Id}: умение {g.Key} "
                             + $"объявлено {g.Count()} раза");
        }
    }
}
