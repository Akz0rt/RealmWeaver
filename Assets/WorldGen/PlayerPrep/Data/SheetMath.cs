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

        public static int Modifier(int score) => (int)System.Math.Floor((score - 10) / 2.0);

        /// <summary>2 на 1–4, 3 на 5–8, 4 на 9–12, 5 на 13–16, 6 на 17–20.</summary>
        public static int Proficiency(int level) => 2 + (Clamp(level, 1, 20) - 1) / 4;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();

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

            d.ProficiencyBonus = Proficiency(level);
            d.Initiative = d.Modifiers.Dex;
            d.Speed = race?.Speed ?? 0;
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
