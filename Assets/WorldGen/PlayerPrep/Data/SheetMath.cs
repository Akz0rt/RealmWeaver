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

        /// <summary>«Ловкость 15 → 17 (+2)»: база из файла, стрелка, сумма с прибавками. Без прибавок
        /// скобок нет вовсе — «Мудрость 10 → 10», а не «→ 10 (+0)»: ноль в скобках читается как
        /// потерянная прибавка.
        ///
        /// Складываются ВСЕ прибавки характеристики, независимо от Source: игроку важно, откуда
        /// взялось итоговое число, а не какая из строк файла его дала. Правило переехало сюда из
        /// WizardView, где было второй копией; лист и мастер обязаны объяснять одно число одинаково.
        ///
        /// Справочник не нужен: и база, и прибавки лежат в файле, поэтому мастер зовёт это на
        /// полупустом персонаже, у которого ни вида, ни класса ещё нет.</summary>
        public static string ExplainAbility(CharacterFile file, string abilityId)
        {
            if (file == null) return AbilityName(abilityId);
            int fromBase = file.Base.Get(abilityId);
            int bump = file.Bumps.Where(b => b.AbilityId == abilityId).Sum(b => b.Amount);
            string head = $"{AbilityName(abilityId)} {fromBase} → {fromBase + bump}";
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

            // Суммы и модификаторы
            d.Total = new AbilityScores
            {
                Str = file.Base.Str, Dex = file.Base.Dex, Con = file.Base.Con,
                Int = file.Base.Int, Wis = file.Base.Wis, Cha = file.Base.Cha
            };
            foreach (var b in file.Bumps)
            {
                // AbilityScores.Add на неизвестной строке молча ничего не делает — прибавка просто
                // испарилась бы. Говорим вслух, как и про любой другой неизвестный Id.
                if (System.Array.IndexOf(AbilityIds, b.AbilityId) < 0)
                    d.UnknownIds.Add($"прибавка: неизвестная характеристика «{b.AbilityId}»");
                d.Total.Add(b.AbilityId, b.Amount);
            }
            foreach (var id in AbilityIds) d.Modifiers.Add(id, Modifier(d.Total.Get(id)));
            foreach (var id in AbilityIds) d.AbilityExplain[id] = ExplainAbility(file, id);

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

            ComputeSkillsAcAndFeatures(d, file, rules, race, cls, bg, level);   // задача 4
            return d;
        }

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
