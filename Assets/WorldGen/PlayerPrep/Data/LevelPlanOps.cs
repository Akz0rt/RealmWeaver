using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public class PlanRow
    {
        public int Level;
        public string Summary;      // что даёт уровень, одной строкой
        public string ChoiceKind;   // null | "subclass" | "asi" — какая ячейка есть у КЛАССА
        public string ChosenKind;   // null = ячейка пуста; "subclass" | "feat" | "asi" — чем ЗАНЯТА
        public string ChosenId;     // id подкласса или черты; null у ячейки, занятой прибавками
        public bool Reached;        // уровень уже взят
    }

    /// <summary>Что предстоит выбрать на новом уровне. ВИД, а не русская строка: показывать текст —
    /// дело экрана, а решать «открыть выбор подкласса или выбор черты» — дело кода, и разбирать для
    /// этого обратно человеческую фразу значит поставить логику в зависимость от корректуры.</summary>
    public class LevelStep
    {
        public string Kind;         // "subclass" | "asi" | "expertise"
        public string Text;         // то же самое для человека
    }

    /// <summary>Таблица 1–20 своего класса и повышение уровня по ней. Уровни с выбором помечены
    /// ЗАРАНЕЕ, выше текущего, — это и есть «набросать план прокачки».</summary>
    public static class LevelPlanOps
    {
        public static List<PlanRow> Rows(ClassDef cls, CharacterFile file)
        {
            var rows = new List<PlanRow>();
            if (cls == null || file == null) return rows;
            foreach (var lv in cls.Levels.OrderBy(l => l.Level))
            {
                var planned = file.Plan.FirstOrDefault(p => p.Level == lv.Level);
                string chosenKind = planned?.Kind;
                string chosenId = planned?.ValueId;
                // Подкласс живёт в ДВУХ местах: в file.SubclassId (его читает лист) и пометкой в
                // плане. Строка обязана показать выбранное независимо от того, где оно лежит, —
                // иначе таблица уверяет «не выбрано» рядом с листом, где подкласс уже работает.
                if (chosenKind == null && lv.Choice == "subclass" && !string.IsNullOrEmpty(file.SubclassId))
                {
                    chosenKind = "subclass";
                    chosenId = file.SubclassId;
                }
                rows.Add(new PlanRow
                {
                    Level = lv.Level,
                    Summary = lv.Features.Count > 0
                        ? string.Join(", ", lv.Features.Select(f => f.Name))
                        : "—",
                    ChoiceKind = lv.Choice,
                    ChosenKind = chosenKind,
                    ChosenId = chosenId,
                    Reached = lv.Level <= file.Level
                });
            }
            return rows;
        }

        /// <summary>Поднимает уровень на единицу и возвращает то, что предстоит выбрать.
        /// Пустой список — выбирать нечего, уровень просто взят.
        ///
        /// Файл правится ПОСЛЕДНИМ. Отмены на листе нет намеренно, поэтому «поднял уровень, а
        /// дальше не смог» — состояние, из которого игрок не выберется: справочник и класс
        /// проверяются до записи, а не после.</summary>
        public static List<LevelStep> LevelUp(CharacterFile file, RulesData rules)
        {
            var pending = new List<LevelStep>();
            if (file == null || rules == null) return pending;
            if (file.Level >= 20) return pending;

            var cls = rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            if (cls == null) return pending;          // класса не знаем — не трогаем файл вовсе
            int next = file.Level + 1;
            var lv = cls.Levels.FirstOrDefault(l => l.Level == next);

            file.Level = next;                        // класс на месте, номер уровня законный
            if (lv == null) return pending;           // строки на этот уровень у класса нет

            if (lv.Choice == "subclass")
                pending.Add(new LevelStep { Kind = "subclass", Text = "Выбери подкласс" });
            if (lv.Choice == "asi")
                pending.Add(new LevelStep { Kind = "asi", Text = "Повышение характеристик или черта" });
            if (cls.ExpertiseLevel == next)
                pending.Add(new LevelStep { Kind = "expertise",
                    Text = $"Выбери компетентность в {cls.ExpertisePickCount} навыках" });
            return pending;
        }

        /// <summary>Понизить уровень на единицу, не ниже первого. Нужно затем, что «Повысить уровень» —
        /// кнопка, а по кнопке промахиваются.
        ///
        /// ПОМЕТКИ ПЛАНА ОСТАЮТСЯ ВСЕ, включая те, что оказались выше нового уровня, — и это не
        /// недоделка, а то же правило «план — это план, а не состояние»: чистить их значило бы стирать
        /// работу игрока за один промах мимо кнопки. Умения и прибавки перестают действовать сами,
        /// потому что и те и другие отбираются по уровню (SheetMathSkills, SheetMath.AbilityTotal).
        /// Проверено LevelChoiceOpsSelfTests.SelfTestLevelDownKeepsThePlan — а НЕ
        /// SheetSkillsSelfTests.SelfTestLoweringLevelKeepsPlan, как стояло здесь сперва: та проверка
        /// про то же правило, но уровень в ней меняют присваиванием, и эту функцию она не зовёт вовсе.
        ///
        /// Справочник не нужен: номер уровня от класса не зависит.</summary>
        public static void LevelDown(CharacterFile file)
        {
            if (file == null) return;
            if (file.Level <= 1) return;
            file.Level--;
        }
    }
}
