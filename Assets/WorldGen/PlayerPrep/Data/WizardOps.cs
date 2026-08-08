using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public class SkillOption
    {
        public string Id, Name, OwnedFrom;
        public bool AlreadyOwned;
    }

    /// <summary>Чистая часть мастера: что доступно на шаге, что предложить и что потеряется.
    /// Мастер — ВИД над теми же данными, что и лист, а не отдельная модель: иначе «выбор расы»
    /// оказался бы в двух местах и они разошлись бы.</summary>
    public static class WizardOps
    {
        public static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

        /// <summary>Навыки, которые класс разрешает добрать, с пометкой уже полученных от
        /// предыстории — шаг 6 их прячет, взять навык дважды нельзя.</summary>
        public static List<SkillOption> AvailableSkills(CharacterFile file, RulesData rules)
        {
            var cls = rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            var bg = rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId);
            var result = new List<SkillOption>();
            var ids = cls != null ? cls.SkillChoices : rules.Skills.Select(s => s.Id).ToList();
            foreach (var id in ids)
            {
                var def = rules.Skills.FirstOrDefault(s => s.Id == id);
                if (def == null) continue;
                bool fromBg = bg != null && bg.SkillIds.Contains(id);
                result.Add(new SkillOption
                {
                    Id = id, Name = def.Name,
                    AlreadyOwned = fromBg,
                    OwnedFrom = fromBg ? $"предыстория «{bg.Name}»" : null
                });
            }
            return result;
        }

        /// <summary>Порядок, в котором стандартный набор ложится по характеристикам: сперва те, где
        /// у класса владение спасбросками (это и есть его ключевые), потом остальные в обычном
        /// порядке. Предложение, а не запрет — игрок волен переставить.</summary>
        public static List<string> SuggestedAssignment(ClassDef cls)
        {
            var all = new List<string> { "str", "dex", "con", "int", "wis", "cha" };
            if (cls == null) return all;
            var key = cls.SaveProficiencies.Where(all.Contains).ToList();
            return key.Concat(all.Where(a => !key.Contains(a))).ToList();
        }

        /// <summary>Что потеряется при смене класса. Спрашиваем ДО, а не откатываем ПОСЛЕ:
        /// это единственное разрушительное действие на листе, и отмены на листе нет намеренно.</summary>
        public static List<string> DescribeClassChange(CharacterFile file, RulesData rules, string newClassId)
        {
            var losses = new List<string>();
            var next = rules.Classes.FirstOrDefault(c => c.Id == newClassId);
            if (next == null) return losses;

            foreach (var id in file.SkillIds.Where(id => !next.SkillChoices.Contains(id)))
            {
                var def = rules.Skills.FirstOrDefault(s => s.Id == id);
                losses.Add($"Навык «{def?.Name ?? id}» — у класса «{next.Name}» его в списке нет");
            }
            if (file.ExpertiseIds.Count > 0 && next.ExpertiseLevel == 0)
                losses.Add("Компетентность — у нового класса её нет");
            if (!string.IsNullOrEmpty(file.SubclassId))
                losses.Add("Подкласс — его придётся выбрать заново");
            var classPlan = file.Plan.Where(p => p.Kind == "subclass").ToList();
            if (classPlan.Count > 0)
                losses.Add($"Пометок плана прокачки: {classPlan.Count}");
            return losses;
        }

        /// <summary>Правит файл на месте. Не трогает ни вид, ни предысторию, ни характеристики,
        /// ни прибавки: класс — это не персонаж целиком.</summary>
        public static void ApplyClassChange(CharacterFile file, RulesData rules, string newClassId)
        {
            var next = rules.Classes.FirstOrDefault(c => c.Id == newClassId);
            if (next == null) return;
            file.ClassId = newClassId;
            file.SkillIds.RemoveAll(id => !next.SkillChoices.Contains(id));
            if (next.ExpertiseLevel == 0) file.ExpertiseIds.Clear();
            else file.ExpertiseIds.RemoveAll(id => !file.SkillIds.Contains(id));
            file.SubclassId = null;
            file.Plan.RemoveAll(p => p.Kind == "subclass");
        }
    }
}
