using System.Collections.Generic;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Справочник правил: только чтение, поставляется со сборкой JSON-файлом.
    /// Данными, а не кодом, по трём причинам: перевод и опечатки правятся без пересборки логики;
    /// другая редакция правил — это второй файл, а не второй код; строка об авторстве живёт прямо
    /// в данных, где её нельзя потерять.</summary>
    public class RulesData
    {
        public string Id;            // "srd-5.2-ru"
        public string Title;
        public string Attribution;   // CC-BY 4.0 — ПОКАЗЫВАЕТСЯ в программе (задача 9)
        public List<RaceDef> Races = new List<RaceDef>();
        public List<ClassDef> Classes = new List<ClassDef>();
        public List<BackgroundDef> Backgrounds = new List<BackgroundDef>();
        public List<FeatDef> Feats = new List<FeatDef>();
        public List<SkillDef> Skills = new List<SkillDef>();
        public List<ItemDef> Items = new List<ItemDef>();
    }

    public class RaceDef
    {
        public string Id, Name, Blurb;
        public int Speed;                                   // футов
        public List<FeatureRef> Traits = new List<FeatureRef>();
        // В правилах 2024 виды прибавок к характеристикам НЕ дают. Поле оставлено ради возможного
        // набора данных по правилам 2014; в srd-5.2-ru.json оно везде пустое.
        public List<string> AbilityChoices = new List<string>();
        public int AbilityChoiceCount;
    }

    public class ClassDef
    {
        public string Id, Name, Blurb;                      // Blurb — «чем ты занят в бою», одна строка
        public string HitDie;                               // "d8"
        public List<string> SaveProficiencies = new List<string>();
        /// <summary>Ключевая характеристика класса — та, что правила 2024 называют ВСЛУХ (у
        /// некоторых классов их две). Заведена отдельным полем, потому что из спасбросков она НЕ
        /// выводится: у Чародея спасброски Тел+Хар, а первое место принадлежит Харизме; у Монаха
        /// спасброски Сил+Лов, а важнее Ловкость и Мудрость. По ней мастер предлагает раскладку
        /// стандартного набора (WizardOps.SuggestedAssignment).
        ///
        /// ПУСТО — ЗАКОННО: чужой справочник (скажем, по правилам 2014, где такого понятия нет)
        /// не обязан его заполнять, и тогда мастер возвращается к прежнему запасному ходу —
        /// спасброскам. Номера версии у справочника нет, и добавлять его незачем: старый файл без
        /// этого поля читается и работает, только советует хуже.</summary>
        public List<string> PrimaryAbilities = new List<string>();
        public List<string> SkillChoices = new List<string>();
        public int SkillPickCount;
        public List<string> StartingEquipment = new List<string>();
        public string UnarmoredDefenseAbility;              // null; "con" у Варвара
        public int ExpertiseLevel;                          // 0 = нет; 1 у Плута
        public int ExpertisePickCount;                      // 2 у Плута
        /// <summary>Из КАКИХ навыков берётся компетентность. ПУСТО — «из любого, которым персонаж
        /// владеет», и это самый частый случай: Плут, Бард и Следопыт по правилам 2024 выбирают из
        /// своих владений без всяких ограничений.
        ///
        /// Заведено ради Волшебника. Его умение «Учёный» (2 уровень) даёт компетентность в одном
        /// навыке ИЗ ШЕСТИ — Магия, История, Медицина, Природа, Расследование, Религия, — а в
        /// списке навыков класса есть ещё и Проницательность. Пока список компетентности строился
        /// из всех выбранных навыков, Волшебник, взявший Проницательность, ставил компетентность на
        /// неё, и лист показывал +4 там, где правила дают +2. Ограничение живёт ДАННЫМИ, а не
        /// условием «если класс — Волшебник»: второй справочник со своим списком не должен
        /// требовать правки кода.
        ///
        /// Каждый идентификатор обязан лежать и в SkillChoices — это стережёт RulesIntegrity:
        /// навык, которым класс владеть не даёт, компетентности получить не может, и такая строка
        /// была бы опечаткой, невидимой на экране.</summary>
        public List<string> ExpertiseChoices = new List<string>();

        /// <summary>Даёт ли класс ставить компетентность именно на этот навык. Пустой (и, из-за
        /// Newtonsoft, возможно null) список значит «на любой» — см. ExpertiseChoices.
        ///
        /// Страж от null стоит ЗДЕСЬ, а не у зовущих: их четверо (лист, мастер, целостность,
        /// смена класса), и забытая проверка в одном из них уронила бы экран на чужом
        /// справочнике с «"ExpertiseChoices": null».</summary>
        public bool AllowsExpertiseIn(string skillId)
        {
            var allowed = ExpertiseChoices;
            return allowed == null || allowed.Count == 0 || allowed.Contains(skillId);
        }
        /// <summary>Магия договора Колдуна. Его ячейки живут не по общей таблице: их мало, все они
        /// ОДНОГО круга, и круг этот с уровнем переезжает выше — на 1-м уровне ячейка 1 круга, на
        /// 3-м две ячейки 2 круга, и так далее. В девяти числах SpellSlots это выражается честно
        /// (нули везде, кроме одной позиции), но нарушает общее правило «ячейки круг за кругом не
        /// убывают»: первый круг у Колдуна на третьем уровне обнуляется.
        ///
        /// Поэтому флаг ЯВНЫЙ, а не выведенный из формы таблицы («в строке один ненулевой круг»).
        /// Выведенный флаг молча снимал бы охрану с полного заклинателя, у которого опечатка
        /// обнулила нижние круги, — то есть был бы ровно тем «зелёным от отсутствия», против
        /// которого написан весь RulesIntegrity. Взамен общей проверки Колдуна стережёт своя,
        /// более строгая: круг один, число ячеек не убывает, круг не понижается.
        ///
        /// ЧТО ФЛАГ НЕ ВЫРАЖАЕТ: возврат ячеек уже после Короткого отдыха. Это сказано словами в
        /// умении «Магия договора» — числами такое не записывается.</summary>
        public bool PactMagic;
        public List<ClassLevel> Levels = new List<ClassLevel>();   // ровно 1..20
        public List<SubclassDef> Subclasses = new List<SubclassDef>();
    }

    /// <summary>Подкласс. В SRD 5.2 у каждого класса ровно один, но список — потому что своя
    /// вторая редакция данных или доморощенный подкласс не должны требовать правки кода.</summary>
    public class SubclassDef
    {
        public string Id, Name, Text;
        public List<SubclassLevel> Levels = new List<SubclassLevel>();
    }

    public class SubclassLevel
    {
        public int Level;
        public List<FeatureRef> Features = new List<FeatureRef>();
    }

    public class ClassLevel
    {
        public int Level;
        public List<FeatureRef> Features = new List<FeatureRef>();
        public string Choice;        // null | "subclass" | "asi"  (см. Global Constraints)
        public int[] SpellSlots;     // null у неколдующих; иначе ровно 9 чисел
    }

    public class BackgroundDef
    {
        public string Id, Name, Text;
        // Правила 2024: игрок кладёт +2/+1 либо +1/+1/+1 в пределах этих трёх характеристик.
        public List<string> AbilityChoices = new List<string>();   // ровно 3 идентификатора
        public string OriginFeatId;                                // ФИКСИРОВАН, игрок не выбирает
        public List<string> SkillIds = new List<string>();         // ровно 2
        public List<string> Equipment = new List<string>();
    }

    public class FeatDef
    {
        public string Id, Name, Text;
        public string Category;      // "origin" | "general" | "fighting-style" | "epic-boon"
    }

    public class SkillDef
    {
        public string Id, Name;
        public string AbilityId;     // одна из шести строк
    }

    public class ItemDef
    {
        public string Id, Name;
        public string Kind;          // "armor" | "shield" | "weapon" | "gear"
        public int ArmorBase;        // 0 у не-доспехов
        public int MaxDexBonus;      // -1 = не ограничен; 2 у среднего; 0 у тяжёлого
    }

    public class FeatureRef
    {
        public string Id, Name, Text;
    }
}
