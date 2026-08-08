using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Чистая функция «выборы игрока + справочник → всё, что показывает лист».
    /// Ничего не хранит и ничего не правит: те же входные данные всегда дают тот же лист.</summary>
    public static partial class SheetMath
    {
        static readonly string[] AbilityIds = { "str", "dex", "con", "int", "wis", "cha" };
        static readonly string[] AbilityNames = { "Сила", "Ловкость", "Телосложение", "Интеллект", "Мудрость", "Харизма" };

        /// <summary>Русское название характеристики по идентификатору. ЕДИНСТВЕННАЯ таблица названий
        /// в проекте: у мастера была своя копия, и ничто не мешало им разойтись — «Телосложение» на
        /// листе и «Выносливость» в мастере читались бы как две разные характеристики.
        ///
        /// Неизвестный идентификатор возвращается как есть. Это честнее пустой строки: показ не падает,
        /// а опечатка в справочнике видна на экране, а не проглочена.</summary>
        public static string AbilityName(string abilityId)
        {
            int i = System.Array.IndexOf(AbilityIds, abilityId);
            return i < 0 ? abilityId : AbilityNames[i];
        }

        /// <summary>Шесть характеристик в каноническом порядке — том самом, в котором их показывают
        /// лист и мастер. Отдаётся КОПИЯ, а не сам массив: раскладка мастера полученный список
        /// переставляет, и общий на всех массив она перемешала бы всем сразу.</summary>
        public static List<string> AbilityOrder() => new List<string>(AbilityIds);

        public static int Modifier(int score) => (int)System.Math.Floor((score - 10) / 2.0);

        /// <summary>2 на 1–4, 3 на 5–8, 4 на 9–12, 5 на 13–16, 6 на 17–20.</summary>
        public static int Proficiency(int level) => 2 + (Clamp(level, 1, 20) - 1) / 4;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Бонус со знаком: «+3», «+0», «−1» (минус обычный, ASCII). НОЛЬ ПИШЕТСЯ «+0», а не
        /// «0», потому что на бумажном листе он пишется так же: у строки со знаком читается «бонус,
        /// равный нулю», у строки без знака — «здесь ничего не посчитано».
        ///
        /// Публичный, потому что тем же способом подписывает числа лист (SheetView): своя копия этого
        /// соглашения в слое рисования разошлась бы со строками Explain, которые оно же и собирает —
        /// «+0 ловкость» в объяснении и «0» в самом числе на одной строке.</summary>
        public static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();

        /// <summary>Как записывается прибавка, полученная НА УРОВНЕ. Формат знают ровно два места —
        /// эта строка и BumpLevel, которая её разбирает обратно, — и стоят они рядом нарочно: разойдись
        /// запись с разбором, прибавка, записанная планом прокачки, перестала бы находиться при
        /// пересчёте и молча стала бы вечной.</summary>
        public static string BumpSource(int level) => "level:" + level;

        /// <summary>На каком уровне получена прибавка: N из «level:N», либо 0, если Source к уровню
        /// не привязан вовсе («background», «race») или разобрать его не удалось.
        ///
        /// НЕРАЗБИРАЕМЫЙ Source — ЭТО «ДЕЙСТВУЕТ ВСЕГДА», а не «не действует никогда»: чужая или более
        /// новая редакция файла может пометить прибавку по-своему, и потерять из-за этого выбор игрока
        /// хуже, чем показать его на уровень раньше срока.</summary>
        public static int BumpLevel(string source)
        {
            const string prefix = "level:";
            if (string.IsNullOrEmpty(source)) return 0;
            if (!source.StartsWith(prefix, System.StringComparison.Ordinal)) return 0;
            return int.TryParse(source.Substring(prefix.Length), out int at) ? at : 0;
        }

        /// <summary>Предел характеристики: прибавками выше двадцати не поднимаются.</summary>
        public const int AbilityCap = 20;

        /// <summary>Сколько у характеристики СЕЙЧАС — база плюс те прибавки, которые на этом уровне уже
        /// действуют, но не выше потолка. ЕДИНСТВЕННОЕ место, где прибавки складываются: и лист
        /// (Compute), и объяснение (ExplainAbility), и план прокачки (LevelChoiceOps.ChooseAsi)
        /// спрашивают здесь.
        ///
        /// УРОВЕНЬ УЧИТЫВАЕТСЯ, и это не мелочь. Прибавка вида «level:12» лежит в файле с того мига,
        /// как игрок наметил её в плане прокачки, — то есть задолго до двенадцатого уровня. Складывай
        /// мы всё подряд, «+2 Ловкость на 12 уровне» подняли бы Ловкость на листе четвёртого, молча и
        /// без единой строчки объяснения.
        ///
        /// ПОТОЛОК 20 ДЕРЖИТСЯ ЗДЕСЬ, А НЕ В МИГ ВЫБОРА, и это исправление настоящей находки. Пока
        /// урезал только ChooseAsi, потолок был верен ровно до следующей правки файла: выбрал +2 на 12
        /// уровне, потом +2 на 8-м — и на двенадцатом выходило 21, потому что вторая запись про первую
        /// ничего не знала. То же давали «убрать выбор» на младшем уровне и поднятая задним числом
        /// БАЗА в мастере. Проверять на записи бессмысленно — запись никогда не последняя.
        ///
        /// БАЗА ВЫШЕ ДВАДЦАТИ НЕ СРЕЗАЕТСЯ: потолок останавливает ПРИБАВКИ, а не число, которое игрок
        /// вписал своей рукой. Срежь мы его — файл с базой 22 показывал бы 20, и лист молча спорил бы
        /// с мастером, где то же самое число стоит как есть.
        ///
        /// Уровень прижимается к 1–20 здесь же: снаружи его передают и из файла (где он бывает
        /// испорчен), и из плана.</summary>
        public static int AbilityTotal(CharacterFile file, string abilityId, int level)
        {
            if (file == null) return 0;
            int fromBase = file.Base.Get(abilityId);
            int ceiling = fromBase > AbilityCap ? fromBase : AbilityCap;
            int raw = RawTotal(file, abilityId, level);
            return raw > ceiling ? ceiling : raw;
        }

        /// <summary>Сумма БЕЗ потолка — то, что игрок себе записал. Сложение живёт тут и больше нигде:
        /// потолок отрезает уже посчитанное, а объяснение сравнивает записанное с дошедшим. Три места,
        /// каждое со своим циклом, разошлись бы на первой же правке правил.</summary>
        static int RawTotal(CharacterFile file, string abilityId, int level)
        {
            int lv = Clamp(level, 1, 20);
            int total = file.Base.Get(abilityId);
            foreach (var b in file.Bumps)
            {
                if (b.AbilityId != abilityId) continue;
                int at = BumpLevel(b.Source);
                if (at > lv) continue;             // намечено на будущее — на этом листе ещё не действует
                total += b.Amount;
            }
            return total;
        }

        /// <summary>«Ловкость 15 → 17 (+2)»: база из файла, стрелка, сумма с прибавками. Без прибавок
        /// скобок нет вовсе — «Мудрость 10 → 10», а не «→ 10 (+0)»: ноль в скобках читается как
        /// потерянная прибавка.
        ///
        /// Складываются те же прибавки, что и на листе, — через AbilityTotal, а не своим циклом:
        /// объяснение обязано объяснять ровно то число, которое стоит рядом с ним.
        ///
        /// Справочник не нужен: и база, и прибавки лежат в файле, поэтому мастер зовёт это на
        /// полупустом персонаже, у которого ни вида, ни класса ещё нет. Уровень такому персонажу
        /// берётся из файла — другого взять негде.</summary>
        public static string ExplainAbility(CharacterFile file, string abilityId)
        {
            if (file == null) return AbilityName(abilityId);
            return ExplainAbility(file, abilityId, file.Level);
        }

        /// <summary>То же объяснение, но на ЗАДАННОМ уровне. Compute зовёт эту перегрузку со своим уже
        /// прижатым уровнем: прижми объяснение уровень второй раз само — правило «уровень прижимается
        /// к 1–20» оказалось бы записано в двух местах, а «одно место» и есть весь смысл AbilityTotal.
        ///
        /// ПРО ПОТОЛОК ГОВОРИТСЯ ВСЛУХ. Прибавка, упёршаяся в двадцать, иначе просто не появлялась бы
        /// в числе — а игрок помнит, что выбирал «+2», и видит «+1» либо вовсе ничего. Молчание тут
        /// неотличимо от потерянного выбора: «Ловкость 19 → 20 (+1 из +2: выше 20 характеристика не
        /// растёт)» объясняет ровно то, что случилось.</summary>
        static string ExplainAbility(CharacterFile file, string abilityId, int level)
        {
            int fromBase = file.Base.Get(abilityId);
            int total = AbilityTotal(file, abilityId, level);
            int bump = total - fromBase;
            int written = RawTotal(file, abilityId, level) - fromBase;
            string head = $"{AbilityName(abilityId)} {fromBase} → {total}";
            if (written != bump)
                return $"{head} ({Signed(bump)} из {Signed(written)}: выше {AbilityCap} характеристика не растёт)";
            return bump == 0 ? head : $"{head} ({(bump > 0 ? "+" : "")}{bump})";
        }

        public static DerivedSheet Compute(CharacterFile file, RulesData rules)
        {
            var d = new DerivedSheet();
            if (file == null || rules == null) { d.Missing.Add("нет данных"); return d; }

            var race = rules.Races.FirstOrDefault(x => x.Id == file.RaceId);
            var cls = rules.Classes.FirstOrDefault(x => x.Id == file.ClassId);
            var bg = rules.Backgrounds.FirstOrDefault(x => x.Id == file.BackgroundId);
            NoteUnknown(d, file.RaceId, race, "вид");
            NoteUnknown(d, file.ClassId, cls, "класс");
            NoteUnknown(d, file.BackgroundId, bg, "предыстория");

            int level = Clamp(file.Level, 1, 20);
            // Прижатый уровень уезжает наружу вместе со всем остальным: лист подписывает им класс
            // («плут 5»), и прижимай он его сам — на файле с уровнем 25 подпись сказала бы «плут 25»
            // над числами, посчитанными для двадцатого.
            d.Level = level;

            // Суммы и модификаторы. Складывает AbilityTotal — здесь не осталось ни одного сложения
            // своими руками: прибавка, намеченная на будущий уровень, не должна попасть на этот лист,
            // и решает это ОДНО место (см. SheetMath.AbilityTotal).
            d.Total = new AbilityScores();
            foreach (var id in AbilityIds) d.Total.Add(id, AbilityTotal(file, id, level));
            // Неизвестные характеристики перечисляются по ВСЕМ прибавкам, включая намеченные на
            // будущее: опечатка в идентификаторе — это опечатка независимо от того, когда прибавка
            // начнёт действовать, и молчать о ней до двенадцатого уровня незачем.
            foreach (var b in file.Bumps)
                if (System.Array.IndexOf(AbilityIds, b.AbilityId) < 0)
                    d.UnknownIds.Add($"прибавка: неизвестная характеристика «{b.AbilityId}»");
            foreach (var id in AbilityIds) d.Modifiers.Add(id, Modifier(d.Total.Get(id)));
            foreach (var id in AbilityIds) d.AbilityExplain[id] = ExplainAbility(file, id, level);

            d.ProficiencyBonus = Proficiency(level);
            // Уровень берётся ПРИЖАТЫЙ, а не file.Level: лист с уровнем 25 показал бы бонус 20-го
            // уровня и подпись «на 25 уровне» — число и его объяснение разошлись бы прямо на экране.
            d.ProficiencyExplain = $"{Signed(d.ProficiencyBonus)} на {level} уровне "
                                 + "(мастерство растёт на 5, 9, 13 и 17)";
            d.Initiative = d.Modifiers.Dex;
            d.InitiativeExplain = $"{Signed(d.Modifiers.Dex)} {AbilityName("dex").ToLowerInvariant()}";
            d.Speed = race?.Speed ?? 0;
            // Ноль без объяснения выглядел бы как «персонаж не ходит». Говорим, чего не хватает.
            d.SpeedExplain = race == null
                ? "вид не выбран — скорость приходит от него"
                : $"{race.Speed} футов, вид «{race.Name}»";
            d.HitDie = cls?.HitDie ?? "";

            // Спасброски
            for (int i = 0; i < AbilityIds.Length; i++)
            {
                string id = AbilityIds[i];
                bool prof = cls != null && cls.SaveProficiencies.Contains(id);
                int mod = d.Modifiers.Get(id);
                int bonus = mod + (prof ? d.ProficiencyBonus : 0);
                d.Saves.Add(new SaveLine
                {
                    AbilityId = id, Name = AbilityNames[i], Proficient = prof, Bonus = bonus,
                    Explain = prof
                        ? $"{Signed(mod)} {AbilityNames[i].ToLowerInvariant()}, {Signed(d.ProficiencyBonus)} мастерство"
                        : $"{Signed(mod)} {AbilityNames[i].ToLowerInvariant()}"
                });
            }

            // Максимум хитов: первый уровень — полная кость, дальше среднее (кость/2 + 1),
            // и телосложение на каждом уровне.
            int die = ParseDie(cls?.HitDie);
            if (die > 0)
            {
                int average = die / 2 + 1;
                int rolled = die + average * (level - 1);
                int fromCon = d.Modifiers.Con * level;
                int computed = System.Math.Max(1, rolled + fromCon);
                if (file.MaxHpOverride.HasValue)
                {
                    d.MaxHp = file.MaxHpOverride.Value;
                    d.MaxHpExplain = $"вписан вручную (среднее дало бы {computed})";
                }
                else
                {
                    d.MaxHp = computed;
                    d.MaxHpExplain = $"{die} на 1 уровне, дальше по {average} за уровень, "
                                   + $"{Signed(d.Modifiers.Con)} телосложение × {level}";
                }
            }
            else if (file.MaxHpOverride.HasValue)
            {
                // Кости хитов нет, а число вписано рукой — оно и есть ответ. Раньше эта ветка теряла
                // вписанное молча: лист показывал 0 там, где игрок своей рукой написал 44.
                d.MaxHp = file.MaxHpOverride.Value;
                d.MaxHpExplain = $"вписан вручную ({NoHitDieReason(cls)}, среднее считать не по чему)";
            }
            else
            {
                // Ноль без объяснения — не просто некрасиво: лист делает число КНОПКОЙ ровно тогда,
                // когда объяснение непустое, поэтому молчание здесь превращало «Хиты 0» в единственную
                // строку листа, у которой нельзя спросить, откуда она взялась.
                d.MaxHpExplain = NoHitDieReason(cls)
                               + (cls == null ? " — кость хитов приходит от него" : "");
            }

            ComputeSkillsAcAndFeatures(d, file, rules, race, cls, bg, level);   // задача 4
            return d;
        }

        /// <summary>Почему кости хитов нет — и «класс не выбран» здесь НЕ то же самое, что «класс
        /// выбран, а кости у него нет». Одна строчка на обе ветки объяснения хитов: разойдись они,
        /// вписанный вручную максимум сказал бы «класс не выбран» персонажу с классом «Плут» —
        /// ровно та подмена случая, из-за которой врал подзаголовок листа.</summary>
        static string NoHitDieReason(ClassDef cls) => cls == null
            ? "класс не выбран"
            : $"класс «{cls.Name}» не называет кость хитов";

        static int ParseDie(string hitDie)
        {
            if (string.IsNullOrEmpty(hitDie) || hitDie[0] != 'd') return 0;
            return int.TryParse(hitDie.Substring(1), out int v) ? v : 0;
        }

        static void NoteUnknown(DerivedSheet d, string id, object resolved, string what)
        {
            if (!string.IsNullOrEmpty(id) && resolved == null)
                d.UnknownIds.Add($"{what}: неизвестно «{id}»");
        }
    }
}
