using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public class PlanRow
    {
        public int Level;
        public string Summary;      // что даёт уровень, одной строкой
        public string ChoiceKind;   // null | "subclass" | "asi"
        public string ChosenId;     // что игрок уже наметил, если наметил
        public bool Reached;        // уровень уже взят
    }

    /// <summary>Таблица 1–20 своего класса и повышение уровня по ней. Уровни с выбором помечены
    /// ЗАРАНЕЕ, выше текущего, — это и есть «набросать план прокачки».</summary>
    public static class LevelPlanOps
    {
        public static List<PlanRow> Rows(ClassDef cls, CharacterFile file)
        {
            var rows = new List<PlanRow>();
            if (cls == null) return rows;
            foreach (var lv in cls.Levels.OrderBy(l => l.Level))
            {
                var planned = file.Plan.FirstOrDefault(p => p.Level == lv.Level);
                rows.Add(new PlanRow
                {
                    Level = lv.Level,
                    Summary = lv.Features.Count > 0
                        ? string.Join(", ", lv.Features.Select(f => f.Name))
                        : "—",
                    ChoiceKind = lv.Choice,
                    ChosenId = planned?.ValueId,
                    Reached = lv.Level <= file.Level
                });
            }
            return rows;
        }

        /// <summary>Поднимает уровень на единицу и возвращает список того, что предстоит выбрать.
        /// Пустой список — выбирать нечего, уровень просто взят.</summary>
        public static List<string> LevelUp(CharacterFile file, RulesData rules)
        {
            var pending = new List<string>();
            if (file.Level >= 20) return pending;
            file.Level++;
            var cls = rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            var lv = cls?.Levels.FirstOrDefault(l => l.Level == file.Level);
            if (lv == null) return pending;
            if (lv.Choice == "subclass") pending.Add("Выбери подкласс");
            if (lv.Choice == "asi") pending.Add("Повышение характеристик или черта");
            if (cls.ExpertiseLevel == file.Level)
                pending.Add($"Выбери компетентность в {cls.ExpertisePickCount} навыках");
            return pending;
        }
    }
}
