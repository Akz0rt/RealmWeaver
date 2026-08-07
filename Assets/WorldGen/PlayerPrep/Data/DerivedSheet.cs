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
        public int ProficiencyBonus;
        public List<SaveLine> Saves = new List<SaveLine>();
        public List<SkillLine> Skills = new List<SkillLine>();
        public int ArmorClass;
        public string ArmorClassExplain = "";
        public int Initiative;
        public int Speed;
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
        public int Bonus;
        public bool Proficient, Expertise;
    }

    public class FeatureLine
    {
        public string Id, Name, Text, Source;   // Source: "race" | "class" | "background" | "feat"
        public int Level;                       // 0 у видовых и предысторийных
    }
}
