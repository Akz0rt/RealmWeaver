using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Что игрок делает с ячейкой выбора в плане прокачки: наметить подкласс, взять черту,
    /// разложить прибавки, передумать. Чистый слой — панель плана (LevelPlanView) только показывает
    /// то, что решено здесь, и ни одного правила у себя не держит.
    ///
    /// ПОМЕТКА — ЭТО ПЛАН, А НЕ СОСТОЯНИЕ. Наметить можно на любой уровень, хоть на двадцатый с
    /// первого; пометка выше нынешнего уровня в файле лежит, но на лист не влияет: умения отбирает
    /// SheetMathSkills по уровню, прибавки — SheetMath.AbilityTotal по своему «level:N».</summary>
    public static class LevelChoiceOps
    {
        /// <summary>Идентификатор «повышения характеристик» в справочнике. В SRD 5.2 оно записано
        /// ЧЕРТОЙ разряда «general» — и именно поэтому его приходится называть по имени.</summary>
        public const string AsiFeatId = "ability-score-improvement";

        /// <summary>Черты, которые можно взять на этом уровне: с 19 уровня — «эпические дары»
        /// (epic-boon), ниже — обычные (general).
        ///
        /// «Повышение характеристик» из списка ВЫЧЕРКНУТО, хотя справочник числит его чертой. В панели
        /// это отдельная дорога («Повысить характеристики»), которая раскладывает прибавки; останься
        /// та же вещь ещё и в списке черт, игрок выбрал бы её как черту — пометка встала бы, а прибавок
        /// не записал никто, и характеристики не выросли бы вовсе.
        ///
        /// Порядок — по имени: справочник хранит черты в порядке своего файла, а игрок ищет глазами.</summary>
        public static List<FeatDef> FeatOptions(RulesData rules, int level)
        {
            var list = new List<FeatDef>();
            if (rules == null) return list;
            string category = level >= 19 ? "epic-boon" : "general";
            foreach (var f in rules.Feats)
            {
                if (f == null || f.Category != category) continue;
                if (f.Id == AsiFeatId) continue;
                list.Add(f);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Name ?? "", b.Name ?? ""));
            return list;
        }

        /// <summary>Наметить (или сменить) подкласс на этом уровне.
        ///
        /// На УЖЕ ВЗЯТОМ уровне подкласс пишется и в file.SubclassId — по нему SheetMathSkills достаёт
        /// умения подкласса, а Subtitle листа — его имя. На будущем уровне остаётся только пометка:
        /// подкласс, намеченный на 3 уровень персонажем 1 уровня, работать сейчас не должен.</summary>
        public static void ChooseSubclass(CharacterFile file, int level, string subclassId)
        {
            if (file == null) return;
            SetMark(file, level, "subclass", subclassId);
            if (level <= file.Level) file.SubclassId = subclassId;
        }

        /// <summary>Взять черту на этом уровне.
        ///
        /// Прибавки этого уровня УБИРАЮТСЯ. Ячейка одна на двоих: игрок мог сперва разложить «+1/+1», а
        /// потом передумать в пользу черты — и старые прибавки, оставшись в файле, продолжали бы
        /// поднимать характеристики рядом с чертой, которую он взял ВМЕСТО них.</summary>
        public static void ChooseFeat(CharacterFile file, int level, string featId)
        {
            if (file == null) return;
            SetMark(file, level, "feat", featId);
            RemoveBumps(file, level);
        }

        /// <summary>Разложить прибавки этого уровня: одна характеристика — +2, две — по +1.
        ///
        /// ПОТОЛОК 20. Характеристика выше двадцати не поднимается, поэтому прибавка урезается до
        /// остатка, а нулевая не записывается вовсе: пустая строчка «+0 Ловкость» в файле выглядела бы
        /// выбором, который что-то делает.
        ///
        /// «Сколько сейчас» спрашивается у SheetMath.AbilityTotal — там же, где считает лист. Своё
        /// сложение здесь означало бы второй ответ на вопрос «сколько у характеристики», и разошлись бы
        /// они ровно на прибавках, намеченных на будущее.
        ///
        /// СТАРЫЕ ПРИБАВКИ СНИМАЮТСЯ ДО ПОДСЧЁТА, и порядок здесь — не стиль. Игрок, второй раз
        /// открывший ту же ячейку и выбравший ту же Ловкость, иначе увидел бы в итоге собственную
        /// прошлую прибавку и урезал бы новую об неё.
        ///
        /// Уровень для подсчёта берётся ТОТ, НА КОТОРОМ ставится прибавка: к этому мигу действуют все
        /// прибавки по него включительно. Считай мы по нынешнему уровню персонажа, план «+2 на 12, +2
        /// на 16» упёрся бы в потолок не там, где упрётся на самом деле.</summary>
        public static void ChooseAsi(CharacterFile file, int level, IList<string> abilityIds)
        {
            if (file == null) return;
            SetMark(file, level, "asi", null);
            RemoveBumps(file, level);
            if (abilityIds == null) return;

            var picked = abilityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (picked.Count == 0) return;
            // Одна характеристика — +2, любое другое число — по +1. Правила разрешают ровно два случая
            // (+2 в одну либо +1 в две), а панель большего и не даёт выбрать.
            int each = picked.Count == 1 ? 2 : 1;
            foreach (var id in picked)
            {
                int now = SheetMath.AbilityTotal(file, id, level);
                int amount = System.Math.Min(each, 20 - now);
                if (amount <= 0) continue;
                file.Bumps.Add(new AbilityBump
                {
                    Source = SheetMath.BumpSource(level), AbilityId = id, Amount = amount
                });
            }
        }

        /// <summary>Передумать: снять пометку этого уровня и всё, что она за собой привела.
        ///
        /// КЛАСС НУЖЕН, и это не лишний довод. Подкласс живёт в ДВУХ местах — пометкой в плане и полем
        /// file.SubclassId, — а у персонажа, собранного мастером, пометки нет вовсе (см.
        /// LevelPlanOps.Rows, который ровно поэтому показывает подкласс из файла). Знай эта функция
        /// только про пометки, «убрать выбор» на строке подкласса такого персонажа не сделал бы
        /// ничего: панель показала бы «вор» сразу после нажатия «убрать».
        ///
        /// А вот пометка подкласса, не совпадающая с нынешним file.SubclassId, поле НЕ трогает: это
        /// намеченная на будущее замена, и снимая её, игрок не отказывается от подкласса, который у
        /// него уже есть.</summary>
        public static void Clear(CharacterFile file, int level, ClassDef cls)
        {
            if (file == null) return;
            var mark = file.Plan.FirstOrDefault(p => p.Level == level);
            file.Plan.RemoveAll(p => p.Level == level);
            RemoveBumps(file, level);

            bool subclassCell = (mark != null && mark.Kind == "subclass")
                             || (cls != null && cls.Levels.Any(l => l.Level == level && l.Choice == "subclass"));
            bool sameSubclass = mark == null || string.IsNullOrEmpty(mark.ValueId)
                             || mark.ValueId == file.SubclassId;
            if (subclassCell && sameSubclass) file.SubclassId = null;
        }

        /// <summary>Перенести намеченное на ДОСТИГНУТОМ уровне туда, где это работает.
        ///
        /// Без неё план оставался бы планом навсегда. LevelPlanOps.LevelUp поднимает номер уровня и
        /// только; подкласс, намеченный заранее на 3 уровень, так и лежал бы пометкой, а лист на
        /// третьем уровне писал бы «Подкласс не выбран» игроку, который выбрал его первым делом.
        ///
        /// Прибавки и черты переносить не надо: их и так читают по уровню (SheetMath.AbilityTotal,
        /// SheetMathSkills). Второе место есть ровно у подкласса — file.SubclassId.</summary>
        public static void ApplyPlanFor(CharacterFile file, int level)
        {
            if (file == null) return;
            if (level > file.Level) return;          // до этого уровня ещё не дожили
            var mark = file.Plan.FirstOrDefault(p => p.Level == level && p.Kind == "subclass");
            if (mark == null || string.IsNullOrEmpty(mark.ValueId)) return;
            file.SubclassId = mark.ValueId;
        }

        /// <summary>Пометка уровня — ровно одна: ячейка выбора у уровня одна, и «черта плюс прибавки на
        /// четвёртом» не бывает.</summary>
        static void SetMark(CharacterFile file, int level, string kind, string valueId)
        {
            file.Plan.RemoveAll(p => p.Level == level);
            file.Plan.Add(new LevelChoice { Level = level, Kind = kind, ValueId = valueId });
        }

        /// <summary>Убрать прибавки ЭТОГО уровня и только его. Разбирает Source не сама, а через
        /// SheetMath.BumpLevel: формат «level:N» знает одно место на весь проект, иначе прибавка,
        /// записанная одним правилом, не находилась бы другим.</summary>
        static void RemoveBumps(CharacterFile file, int level)
        {
            file.Bumps.RemoveAll(b => SheetMath.BumpLevel(b.Source) == level);
        }
    }
}
