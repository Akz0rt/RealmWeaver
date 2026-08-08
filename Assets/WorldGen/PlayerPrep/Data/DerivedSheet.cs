using System.Collections.Generic;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Всё, что лист показывает. Каждое число несёт с собой строку Explain — «откуда оно
    /// взялось». Объяснение считается ЗДЕСЬ, а не в интерфейсе, потому что именно оно проверяется
    /// самопроверками: игрок видит не результат, а его происхождение, и это единственное обучение
    /// правилам, которое арка даёт.</summary>
    public class DerivedSheet
    {
        public AbilityScores Total = new AbilityScores();
        public AbilityScores Modifiers = new AbilityScores();

        /// <summary>«Сила 8 → 10 (+2)» по каждой из шести характеристик, ключ — идентификатор.
        /// Заполняются ВСЕ ШЕСТЬ, включая те, что прибавок не получили: лист показывает объяснение по
        /// нажатию на любое число, и «у этой характеристики объяснения нет» читалось бы как поломка.
        ///
        /// Живёт здесь, а не в слое рисования, по той же причине, что и все остальные Explain: у
        /// мастера была своя копия этого правила (WizardView.ExplainAbility), и ничто не мешало ей
        /// разойтись с листом — одно и то же число объяснялось бы на двух экранах по-разному.</summary>
        public Dictionary<string, string> AbilityExplain = new Dictionary<string, string>();

        /// <summary>Уровень, ПРИЖАТЫЙ к 1–20 — тот самый, по которому посчитано всё остальное.
        /// Отдаётся наружу именно затем, чтобы лист не прижимал его у себя второй раз: своя копия
        /// правила в слое рисования разошлась бы с числами, которые она подписывает, ровно на файлах
        /// с испорченным уровнем — там, где проверить некому.</summary>
        public int Level;

        public int ProficiencyBonus;
        /// <summary>«+3 на 5 уровне (мастерство растёт на 5, 9, 13 и 17)». Уровень назван ВНУТРИ
        /// строки намеренно: без него игрок не видит, почему бонус именно такой, и не знает, когда
        /// ждать следующего.</summary>
        public string ProficiencyExplain = "";
        public List<SaveLine> Saves = new List<SaveLine>();
        public List<SkillLine> Skills = new List<SkillLine>();
        public int ArmorClass;
        public string ArmorClassExplain = "";
        public int Initiative;
        public string InitiativeExplain = "";
        public int Speed;
        /// <summary>«30 футов, вид «Полурослик»». Вид назван, потому что скорость приходит ровно от
        /// него: без имени числу неоткуда взяться на глазах у игрока.</summary>
        public string SpeedExplain = "";
        public int MaxHp;
        public string MaxHpExplain = "";
        public string HitDie = "";
        public List<FeatureLine> Features = new List<FeatureLine>();
        public int[] SpellSlots;                        // null у неколдующих
        public List<string> UnknownIds = new List<string>();
        public List<string> Missing = new List<string>();
    }

    public class SaveLine
    {
        public string AbilityId, Name, Explain;
        public int Bonus;
        public bool Proficient;
    }

    public class SkillLine
    {
        public string SkillId, Name, AbilityId, Explain;
        /// <summary>Короткое уточнение рядом с числом — «ловкость», «ловкость · компетентность».
        /// То же, чем начинается Explain, но без чисел. Собирается ЗДЕСЬ, а не на листе: своя копия
        /// в слое рисования называла бы характеристику одним словом, а объяснение под ней —
        /// другим, стоит справочнику переименовать характеристику.</summary>
        public string Hint;
        public int Bonus;
        public bool Proficient, Expertise;
    }

    public class FeatureLine
    {
        public string Id, Name, Text, Source;   // Source: "race" | "class" | "background" | "feat"
        public int Level;                       // 0 у видовых и предысторийных
    }
}
