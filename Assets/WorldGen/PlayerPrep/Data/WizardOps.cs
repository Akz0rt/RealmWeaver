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
    /// оказался бы в двух местах и они разошлись бы.
    ///
    /// Стражи на входе — как в SheetMath.Compute: недостающие данные дают разумный пустой ответ,
    /// а не падение. Мастер по построению работает с НЕДОДЕЛАННЫМ персонажем, поэтому «половина
    /// полей пуста» здесь норма, а не сбой.</summary>
    public static class WizardOps
    {
        public static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

        /// <summary>Навыки, которые класс разрешает добрать, с пометкой уже полученных от
        /// предыстории — шаг 6 их прячет, взять навык дважды нельзя.</summary>
        public static List<SkillOption> AvailableSkills(CharacterFile file, RulesData rules)
        {
            var result = new List<SkillOption>();
            if (file == null || rules == null) return result;

            var cls = rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            var bg = rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId);
            // Класс НЕ ВЫБРАН — показываем весь справочник (мастер идёт по шагам, до класса он
            // ещё не дошёл). Класс выбран, а список у него пуст — это его данные, и пустой выбор
            // честнее подмены «бери что хочешь».
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

        /// <summary>Сколько навыков класса ещё можно взять — «выбрано 1 из 2» на шаге 6.
        /// Считаются ТОЛЬКО навыки из списка класса: навык от предыстории приходит даром и
        /// ячейку не занимает, иначе солдат-Плут получил бы на один навык меньше остальных.</summary>
        public static int RemainingSkillPicks(CharacterFile file, RulesData rules)
        {
            if (file == null || rules == null) return 0;
            var cls = rules.Classes.FirstOrDefault(c => c.Id == file.ClassId);
            if (cls == null) return 0;
            int picked = file.SkillIds.Count(id => cls.SkillChoices.Contains(id));
            return System.Math.Max(0, cls.SkillPickCount - picked);
        }

        /// <summary>Порядок, в котором стандартный набор ложится по характеристикам: сперва те, где
        /// у класса владение спасбросками (это и есть его ключевые), потом остальные в обычном
        /// порядке. Предложение, а не запрет — игрок волен переставить.</summary>
        public static List<string> SuggestedAssignment(ClassDef cls)
        {
            var all = new List<string> { "str", "dex", "con", "int", "wis", "cha" };
            if (cls == null) return all;
            // Distinct обязателен: RulesIntegrity уникальность спасбросков не проверяет, а
            // сдвоенный идентификатор дал бы семь позиций против шести значений набора —
            // и раскладка поехала бы вся.
            var key = cls.SaveProficiencies.Where(all.Contains).Distinct().ToList();
            return key.Concat(all.Where(a => !key.Contains(a))).ToList();
        }

        /// <summary>Что потеряется при смене класса. Спрашиваем ДО, а не откатываем ПОСЛЕ:
        /// это единственное разрушительное действие на листе, и отмены на листе нет намеренно.
        /// Каждая строка называет потерю ПОИМЁННО — «два навыка исчезнут» игроку не помогает
        /// решить, соглашаться ли.</summary>
        public static List<string> DescribeClassChange(CharacterFile file, RulesData rules, string newClassId)
        {
            var losses = new List<string>();
            if (file == null || rules == null) return losses;
            var next = rules.Classes.FirstOrDefault(c => c.Id == newClassId);
            if (next == null) return losses;

            foreach (var lost in SkillsLostTo(file, next))
            {
                var def = rules.Skills.FirstOrDefault(s => s.Id == lost.Id);
                string name = def?.Name ?? lost.Id;
                losses.Add(lost.OverCap
                    ? $"Навык «{name}» — класс «{next.Name}» даёт на выбор только {next.SkillPickCount}"
                    : $"Навык «{name}» — у класса «{next.Name}» его в списке нет");
            }
            if (file.ExpertiseIds.Count > 0 && next.ExpertiseLevel == 0)
                losses.Add("Компетентность — у нового класса её нет");
            if (!string.IsNullOrEmpty(file.SubclassId))
                losses.Add("Подкласс — его придётся выбрать заново");
            var classPlan = file.Plan.Where(IsSubclassMark).ToList();
            if (classPlan.Count > 0)
                losses.Add($"Пометок плана прокачки: {classPlan.Count}");
            int orphans = file.Plan.Count(p => PlanMarkHasNoCell(p, next));
            if (orphans > 0)
                losses.Add($"Пометок повышения на уровнях, где у класса «{next.Name}» ячейки нет: {orphans}");
            return losses;
        }

        /// <summary>Правит файл на месте. Не трогает ни вид, ни предысторию, ни характеристики,
        /// ни прибавки: класс — это не персонаж целиком. Снимает РОВНО то, что перечислил
        /// DescribeClassChange — обе стороны считают по одним и тем же двум функциям ниже,
        /// иначе диалог обещал бы одно, а делал другое.</summary>
        public static void ApplyClassChange(CharacterFile file, RulesData rules, string newClassId)
        {
            if (file == null || rules == null) return;
            var next = rules.Classes.FirstOrDefault(c => c.Id == newClassId);
            if (next == null) return;
            file.ClassId = newClassId;

            var lost = new HashSet<string>(SkillsLostTo(file, next).Select(l => l.Id));
            file.SkillIds.RemoveAll(lost.Contains);

            if (next.ExpertiseLevel == 0) file.ExpertiseIds.Clear();
            else file.ExpertiseIds.RemoveAll(id => !file.SkillIds.Contains(id));

            file.SubclassId = null;
            file.Plan.RemoveAll(p => IsSubclassMark(p) || PlanMarkHasNoCell(p, next));
        }

        /// <summary>Навыки, которые не переживут переход в класс next, в порядке показа: сперва
        /// те, кого нет в списке нового класса, затем лишние сверх SkillPickCount.
        ///
        /// Лишние отбираются С КОНЦА списка — правило детерминированное нарочно. Отбор «какой
        /// навык хуже» программе не по силам, а «первые сколько-то» совпадают у описания и у
        /// применения дословно; выбор наугад развёл бы диалог и результат.</summary>
        static List<(string Id, bool OverCap)> SkillsLostTo(CharacterFile file, ClassDef next)
        {
            var result = file.SkillIds
                .Where(id => !next.SkillChoices.Contains(id))
                .Select(id => (Id: id, OverCap: false))
                .ToList();
            var kept = file.SkillIds.Where(id => next.SkillChoices.Contains(id)).ToList();
            foreach (var id in kept.Skip(System.Math.Max(0, next.SkillPickCount)))
                result.Add((Id: id, OverCap: true));
            return result;
        }

        static bool IsSubclassMark(LevelChoice p) => p.Kind == "subclass";

        /// <summary>Пометка плана, которой у нового класса не соответствует ячейка нужного вида на
        /// том же уровне. Ячейки повышения не единообразны — у Воина они есть на 6 и 14, у Плута
        /// нет, — а лист выдаёт по такой пометке черту, не спрашивая, существует ли ячейка.</summary>
        static bool PlanMarkHasNoCell(LevelChoice p, ClassDef next)
        {
            if (IsSubclassMark(p)) return false;         // они уходят все и считаются отдельно
            if (p.Kind != "feat" && p.Kind != "asi") return true;   // вид пометки неизвестен — ячейки нет
            return !next.Levels.Any(l => l.Level == p.Level && l.Choice == "asi");
        }
    }
}
