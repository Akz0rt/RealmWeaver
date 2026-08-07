using System.Collections.Generic;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Файл игрока (.dndchar). Внутри лежат ВЫБОРЫ, а не посчитанные числа: модификаторы,
    /// мастерство, спасброски, КД и навыки считает SheetMath при каждом показе. Найдётся опечатка в
    /// справочнике — листы у всех починятся сами.
    ///
    /// Единственное посчитанное число, которое хранится, — максимум хитов, и только когда игрок
    /// вписал своё вместо среднего (за столом их часто бросают). ТЕКУЩИХ хитов здесь нет вовсе:
    /// файл подготовки хранит персонажа, а не его состояние в бою.</summary>
    public class CharacterFile
    {
        public int FormatVersion = 1;   // СВОЙ счёт, не связан с версией .dndproj
        public string RulesId;
        public string Name = "";
        public byte[] Portrait;         // уменьшен до 512 по большой стороне
        public string RaceId, ClassId, BackgroundId, SubclassId;
        public int Level = 1;
        public AbilityScores Base = new AbilityScores();
        public List<AbilityBump> Bumps = new List<AbilityBump>();
        public List<string> SkillIds = new List<string>();        // добранные вручную
        public List<string> ExpertiseIds = new List<string>();
        public List<string> Equipment = new List<string>();
        public string Backstory = "";
        public string SpellNotes = "";  // заклинатель вписывает заклинания руками — их справочника в арке нет
        public int? MaxHpOverride;      // null = среднее из справочника
        public List<LevelChoice> Plan = new List<LevelChoice>();
    }

    public class AbilityScores
    {
        public int Str, Dex, Con, Int, Wis, Cha;

        public int Get(string abilityId)
        {
            switch (abilityId)
            {
                case "str": return Str;  case "dex": return Dex;  case "con": return Con;
                case "int": return Int;  case "wis": return Wis;  case "cha": return Cha;
                default: return 0;
            }
        }

        public void Add(string abilityId, int amount)
        {
            switch (abilityId)
            {
                case "str": Str += amount; break;  case "dex": Dex += amount; break;
                case "con": Con += amount; break;  case "int": Int += amount; break;
                case "wis": Wis += amount; break;  case "cha": Cha += amount; break;
            }
        }
    }

    /// <summary>Куда игрок положил прибавку. Хранится, а НЕ выводится из вида и предыстории:
    /// распределение прибавок — тоже выбор игрока. В правилах 2024 предыстория даёт +2/+1 (или
    /// +1/+1/+1) и раскладывает их игрок; вывести это неоткуда.</summary>
    public class AbilityBump
    {
        public string Source;      // "background" | "level:8" | "race" (последнее — только для правил 2014)
        public string AbilityId;
        public int Amount;
    }

    public class LevelChoice
    {
        public int Level;
        public string Kind;        // "subclass" | "feat" | "asi"
        public string ValueId;     // id подкласса, id черты, либо null у "asi"
    }
}
