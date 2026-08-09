using System.Collections.Generic;
using System.Linq;

namespace WorldGen.PlayerPrep.Data
{
    public class SkillOption
    {
        public string Id, Name, OwnedFrom;
        public bool AlreadyOwned;
    }

    /// <summary>Строка стартового набора: что предлагается, как это называется и откуда пришло.
    /// Source — готовая подпись для игрока («класс», «предыстория», «класс и предыстория»), потому
    /// что предмет бывает и там и там сразу.</summary>
    public class EquipmentOption
    {
        public string Id, Name, Source;
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

        /// <summary>Характеристики, которые идут СРАЗУ ЗА КЛЮЧЕВЫМИ, в этом порядке.
        ///
        /// Телосложение — единственная характеристика, полезная любому персонажу без исключений:
        /// это хиты, и лишняя единица модификатора прибавляется за КАЖДЫЙ уровень до двадцатого.
        /// Ловкость — вторая по всеобщности: класс доспеха, инициатива и самый частый спасбросок
        /// в игре.
        ///
        /// Заведено потому, что «остальное обычным порядком» начинается с Силы, а Сила нужна
        /// меньшинству. У семи классов из двенадцати четырнадцать уезжала в Силу, которой
        /// Волшебник или Жрец не пользуются вовсе, а Телосложению доставалась двенадцать —
        /// то есть минус хит за каждый уровень на всю игру.</summary>
        static readonly string[] UniversallyUseful = { "con", "dex" };

        /// <summary>Порядок, в котором стандартный набор ложится по характеристикам: сперва
        /// КЛЮЧЕВЫЕ характеристики класса, затем Телосложение и Ловкость (см.
        /// UniversallyUseful), потом остальные в обычном порядке. Предложение, а не запрет —
        /// игрок волен переставить.
        ///
        /// У Воина ключевые ОБЕ — Сила и Ловкость, поэтому он получает Сил 15 / Лов 14 / Тел 13,
        /// а не Сил 15 / Тел 14. Это решение принято сознательно: у Воина ключевые действительно
        /// обе, и 15/14/13 по Силе, Ловкости и Телосложению — нормальный воин.
        ///
        /// РАНЬШЕ КЛЮЧЕВЫЕ ВЫВОДИЛИСЬ ИЗ СПАСБРОСКОВ, и на четырёх первых классах это сходилось
        /// случайно. На двенадцати не сходится: у Чародея спасброски Тел+Хар, и мастер предложил бы
        /// первым делом Телосложение вместо Харизмы; у Монаха спасброски Сил+Лов, а важнее Ловкость
        /// и Мудрость. Первому в жизни ДМ-игроку такой совет прямо вреден — арка существует ровно
        /// затем, чтобы не требовать знания правил. Правила 2024 называют ключевую характеристику
        /// вслух, и теперь она лежит в справочнике полем ClassDef.PrimaryAbilities.
        ///
        /// Спасброски остались ЗАПАСНЫМ ходом — на случай справочника, где поле не заполнено
        /// (правила 2014 такого понятия не знают). Поле заполнено — решает только оно: смешивать
        /// два правила значило бы получить порядок, которого не хотел ни один из них.</summary>
        public static List<string> SuggestedAssignment(ClassDef cls)
        {
            var all = SheetMath.AbilityOrder();
            if (cls == null) return all;
            // `?? new List<string>()` — не украшение: Newtonsoft на «"PrimaryAbilities": null»
            // кладёт в поле null, минуя инициализатор, и чужой справочник уронил бы мастер.
            var named = cls.PrimaryAbilities ?? new List<string>();
            var source = named.Count > 0 ? named : (cls.SaveProficiencies ?? new List<string>());
            // Distinct обязателен: RulesIntegrity уникальность спасбросков не проверяет, а
            // сдвоенный идентификатор дал бы семь позиций против шести значений набора —
            // и раскладка поехала бы вся.
            var key = source.Where(all.Contains).Distinct().ToList();
            // Ключевые → Телосложение и Ловкость → остальное обычным порядком. Каждый следующий
            // кусок отбрасывает то, что уже стоит в предыдущих, иначе характеристика попала бы в
            // список дважды и шесть значений набора легли бы на семь позиций.
            var next = UniversallyUseful.Where(a => !key.Contains(a)).ToList();
            return key.Concat(next)
                      .Concat(all.Where(a => !key.Contains(a) && !next.Contains(a)))
                      .ToList();
        }

        // ── Покупка очков ────────────────────────────────────────────────────────
        //
        // Таблица стоимости и бюджет живут ЗДЕСЬ, а не в мастере, и это решение ДМ дословно:
        // приложение существует ровно затем, чтобы не требовать от игрока хороших знаний правил, —
        // режим, молча пускающий невозможного персонажа, замыслу арки противоречит. Значит правило
        // обязано лежать в чистом слое, под самопроверками и мутационной проверкой, а мастеру
        // остаётся показать счётчик и погасить «+».

        public const int PointBuyBudget = 27;
        public const int PointBuyFloor = 8;
        public const int PointBuyCeiling = 15;

        /// <summary>Во сколько очков обходится значение характеристики. −1 значит «покупкой
        /// НЕДОСТИЖИМО»: ниже 8 и выше 15 таблицы стоимости не существует, за 18 из бросков
        /// заплатить нечем. Это не «бесплатно» и не «дорого» — это другое состояние, и путать его
        /// с нулём нельзя, иначе восемнадцать выглядели бы как восьмёрка.</summary>
        public static int PointBuyCost(int score)
        {
            switch (score)
            {
                case 8:  return 0;
                case 9:  return 1;
                case 10: return 2;
                case 11: return 3;
                case 12: return 4;
                case 13: return 5;
                case 14: return 7;
                case 15: return 9;
                default: return -1;
            }
        }

        /// <summary>Сколько очков стоят шесть характеристик разом. −1, если хотя бы одна из них
        /// покупкой недостижима: такой набор не «дорогой», его нельзя собрать покупкой вовсе, и
        /// счётчик «потрачено столько-то» о нём соврал бы.</summary>
        public static int PointBuySpent(AbilityScores scores)
        {
            if (scores == null) return -1;
            int total = 0;
            foreach (var id in SheetMath.AbilityOrder())
            {
                int cost = PointBuyCost(scores.Get(id));
                if (cost < 0) return -1;
                total += cost;
            }
            return total;
        }

        /// <summary>Законен ли набор для покупки очков: все шесть достижимы И бюджет не перебран.
        /// По этому вопросу мастер решает, входить ли в режим на нынешних числах или заменить их
        /// восьмёрками.</summary>
        public static bool IsPointBuyLegal(AbilityScores scores)
        {
            int spent = PointBuySpent(scores);
            return spent >= 0 && spent <= PointBuyBudget;
        }

        /// <summary>Начало покупки: во все шесть по 8. Потрачено 0, в запасе все 27 — единственное
        /// состояние, из которого покупка честно начинается. Наследовать чужие числа нельзя: с 18
        /// из бросков игрок оказался бы в режиме, где за его персонажа не заплатить.</summary>
        public static void ApplyPointBuyStart(AbilityScores scores)
        {
            if (scores == null) return;
            // Set у AbilityScores нет — присваиваем через разницу, как это делает и мастер.
            foreach (var id in SheetMath.AbilityOrder()) scores.Add(id, PointBuyFloor - scores.Get(id));
        }

        /// <summary>Можно ли поднять характеристику на единицу: и потолок не пробит, и очков хватает
        /// на ПОДОРОЖАНИЕ (14→15 стоит два очка, а не одно — таблица нелинейна нарочно).
        /// Мастер этим гасит «+» заранее, а не сообщает о перерасходе задним числом.</summary>
        public static bool CanRaiseByPointBuy(AbilityScores scores, string abilityId)
        {
            int spent = PointBuySpent(scores);
            if (spent < 0) return false;
            int now = scores.Get(abilityId);
            int next = PointBuyCost(now + 1);
            if (next < 0) return false;                     // выше потолка покупка не поднимается
            return spent - PointBuyCost(now) + next <= PointBuyBudget;
        }

        /// <summary>Можно ли опустить характеристику: 8 — пол покупки. Сравнение со ЗНАЧЕНИЕМ, а не
        /// со стоимостью: набор, попавший сюда незаконным (скажем, 18 из бросков), обязан иметь
        /// дорогу вниз, иначе игрок заперт в режиме, где «+» погашен бюджетом, а «−» — законом.</summary>
        public static bool CanLowerByPointBuy(AbilityScores scores, string abilityId)
        {
            return scores != null && scores.Get(abilityId) > PointBuyFloor;
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

            var lostSkills = SkillsLostTo(file, next);
            foreach (var lost in lostSkills)
            {
                var def = rules.Skills.FirstOrDefault(s => s.Id == lost.Id);
                string name = def?.Name ?? lost.Id;
                losses.Add(lost.OverCap
                    ? $"Навык «{name}» — класс «{next.Name}» даёт на выбор только {next.SkillPickCount}"
                    : $"Навык «{name}» — у класса «{next.Name}» его в списке нет");
            }
            // Снаряжение названо здесь, потому что ЧИНИТСЯ оно тоже при смене класса: ResetEquipment
            // пересобирает набор, и вещи прежнего класса из файла уходят. Диалог, умолчавший об этом,
            // обещал бы меньше, чем делает.
            var gear = EquipmentLostTo(file, rules, next);
            if (gear.Count > 0)
                losses.Add($"Снаряжение: {string.Join(", ", gear.ToArray())} — набор соберётся заново "
                           + $"под класс «{next.Name}»");
            if (file.ExpertiseIds.Count > 0 && next.ExpertiseLevel == 0)
                losses.Add("Компетентность — у нового класса её нет");
            else
            {
                // Класс вправе сузить, из каких навыков берётся компетентность (ExpertiseChoices).
                // Тогда САМ НАВЫК уцелевает, а компетентность на нём — нет, и промолчать значило бы
                // снять её тихо: ApplyClassChange ниже считает по тому же AllowsExpertiseIn.
                //
                // Про навыки, которые и так теряются, здесь не говорим ВТОРОЙ раз: их потеря уже
                // названа выше, и компетентность уходит вместе с ними по давнему инварианту
                // «ExpertiseIds ⊆ SkillIds».
                var alsoLost = new HashSet<string>(lostSkills.Select(l => l.Id));
                foreach (var name in file.ExpertiseIds
                             .Where(id => !alsoLost.Contains(id) && !next.AllowsExpertiseIn(id))
                             .Select(id => rules.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id))
                    losses.Add($"Компетентность в навыке «{name}» — класс «{next.Name}» "
                             + "даёт компетентность не в нём");
            }
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
        /// иначе диалог обещал бы одно, а делал другое.
        ///
        /// СНАРЯЖЕНИЕ — ЕДИНСТВЕННОЕ ИСКЛЮЧЕНИЕ, и оно парное: его пересобирает ResetEquipment,
        /// которую мастер зовёт следом. Обещание про снаряжение стоит в DescribeClassChange
        /// (EquipmentLostTo), и обе стороны опять считают по одному коду — StartingEquipment. Зовущий
        /// обязан звать обе: ApplyClassChange без ResetEquipment оставит в файле вещи прежнего
        /// класса, про потерю которых диалог уже сказал вслух.</summary>
        public static void ApplyClassChange(CharacterFile file, RulesData rules, string newClassId)
        {
            if (file == null || rules == null) return;
            var next = rules.Classes.FirstOrDefault(c => c.Id == newClassId);
            if (next == null) return;
            file.ClassId = newClassId;

            var lost = new HashSet<string>(SkillsLostTo(file, next).Select(l => l.Id));
            file.SkillIds.RemoveAll(lost.Contains);

            if (next.ExpertiseLevel == 0) file.ExpertiseIds.Clear();
            // Два условия, а не одно: компетентность держится на владении навыком (давний инвариант
            // «ExpertiseIds ⊆ SkillIds») И на разрешении класса брать её именно в этом навыке
            // (ExpertiseChoices). У Волшебника, например, Проницательность в списке навыков есть, а
            // компетентности в ней класс не даёт — и оставленную строку нечем было бы снять.
            else file.ExpertiseIds.RemoveAll(id => !file.SkillIds.Contains(id)
                                                   || !next.AllowsExpertiseIn(id));

            file.SubclassId = null;
            file.Plan.RemoveAll(p => IsSubclassMark(p) || PlanMarkHasNoCell(p, next));
        }

        /// <summary>Смена предыстории. Ставит новую И СНИМАЕТ ИЗ РУЧНОГО ВЫБОРА навыки, которые она
        /// даёт даром: останься они в file.SkillIds — ячейка класса считалась бы потраченной на то,
        /// что у персонажа и так есть.
        ///
        /// Живой пример на поставляемых данных: Плут (четыре навыка) берёт на шестом шаге
        /// «Скрытность» и «Ловкость рук», а потом выбирает «Преступника», который даёт ровно эти
        /// два. Строки навыков становятся серыми надписями «уже есть», но из файла они не делись —
        /// RemainingSkillPicks считает их потраченными ячейками, заголовок говорит «выбрано 4 из 4»,
        /// галочек видно две, взять ещё два нельзя, а SheetMath.Missing молчит (в файле их ровно
        /// столько, сколько класс даёт). Персонаж молча недосчитывается двух навыков, и выбраться
        /// человеку неоткуда.
        ///
        /// ВЛАДЕНИЕ ПРИ ЭТОМ НЕ ТЕРЯЕТСЯ: снятый навык теперь приходит от предыстории, и лист
        /// показывает его ровно так же. Уходит только пометка «выбран вручную».
        ///
        /// Компетентность на снятом навыке уходит вместе с ним — тот же инвариант «ExpertiseIds ⊆
        /// SkillIds», что соблюдает ApplyClassChange. Оставить её нельзя: список компетентности
        /// мастер строит из file.SkillIds, так что осиротевшую не было бы ни видно, ни как снять.
        ///
        /// Неизвестная предыстория не меняет НИЧЕГО, включая сам идентификатор, — как и
        /// ApplyClassChange с неизвестным классом.</summary>
        public static void ApplyBackgroundChange(CharacterFile file, RulesData rules, string newBackgroundId)
        {
            if (file == null || rules == null) return;
            var next = rules.Backgrounds.FirstOrDefault(b => b.Id == newBackgroundId);
            if (next == null) return;
            file.BackgroundId = newBackgroundId;

            file.SkillIds.RemoveAll(next.SkillIds.Contains);
            file.ExpertiseIds.RemoveAll(id => !file.SkillIds.Contains(id));
        }

        // ── Стартовый набор ──────────────────────────────────────────────────────

        /// <summary>Стартовый набор: снаряжение класса, затем предыстории, БЕЗ ПОВТОРОВ.
        ///
        /// Правило живёт здесь, а не в слое рисования, потому что по нему теперь ПРАВЯТ ФАЙЛ
        /// (ResetEquipment), а не только рисуют список. «Что должно лежать в снаряжении» обязано
        /// считаться одним куском кода: разойдись показ с правкой — в файле оказались бы вещи,
        /// которых на экране нет.
        ///
        /// Один предмет бывает и у класса, и у предыстории (Плут-Солдат приходит с коротким луком
        /// дважды); в файле он должен лежать один раз, поэтому источник у такой строки составной, а
        /// список складывается с проверкой на повтор, а не конкатенацией.</summary>
        public static List<EquipmentOption> StartingEquipment(ClassDef cls, BackgroundDef bg, RulesData rules)
        {
            var result = new List<EquipmentOption>();
            if (cls != null) foreach (var id in cls.StartingEquipment) Offer(result, rules, id, "класс");
            if (bg != null) foreach (var id in bg.Equipment) Offer(result, rules, id, "предыстория");
            return result;
        }

        /// <summary>Тот же набор, но по идентификаторам из файла. Отдельная перегрузка нужна потому,
        /// что DescribeClassChange считает набор для класса, которого в файле ЕЩЁ НЕТ.</summary>
        public static List<EquipmentOption> StartingEquipment(CharacterFile file, RulesData rules)
        {
            if (file == null || rules == null) return new List<EquipmentOption>();
            return StartingEquipment(
                rules.Classes.FirstOrDefault(c => c.Id == file.ClassId),
                rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId), rules);
        }

        static void Offer(List<EquipmentOption> into, RulesData rules, string id, string source)
        {
            if (string.IsNullOrEmpty(id)) return;
            var already = into.FirstOrDefault(o => o.Id == id);
            if (already != null)
            {
                if (already.Source != source) already.Source = already.Source + " и " + source;
                return;
            }
            into.Add(new EquipmentOption
            {
                Id = id,
                // Неизвестный предмет называется своим идентификатором, а не пропадает: строка
                // «bogus-item» на экране — это видимая опечатка в справочнике, а пустая — загадка.
                Name = rules?.Items.FirstOrDefault(i => i.Id == id)?.Name ?? id,
                Source = source
            });
        }

        /// <summary>Пересобирает снаряжение персонажа под нынешние класс и предысторию: чего в наборе
        /// больше нет — убирает, чего не хватает — кладёт.
        ///
        /// ЗОВЁТСЯ В МОМЕНТ СМЕНЫ класса или предыстории, а не при показе седьмого шага, и в этом всё
        /// дело. Пока пересборкой ведал показ, до него можно было не дойти: сменил класс на третьем
        /// шаге, ушёл сразу на восьмой — и в файле остались вещи прежнего класса. Строк для них на
        /// шаге снаряжения больше нет, значит они невидимы и неснимаемы; при этом лист считает по ним
        /// класс доспеха (SheetMath берёт первый предмет с Kind == "armor"), а «Чего не хватает» о
        /// снаряжении не говорит ни слова. Тихо испорченный файл — худшее, что мастер может сделать.
        ///
        /// Правило простое и оттого предсказуемое: сменил класс — получил его набор, потерял прежний.
        /// Цена — снятая вручную галочка возвращается, если после неё сменить класс; это видно и
        /// поправимо, в отличие от невидимой вещи в файле.</summary>
        public static void ResetEquipment(CharacterFile file, RulesData rules)
        {
            if (file == null || rules == null) return;
            var offered = StartingEquipment(file, rules);
            var ids = new HashSet<string>(offered.Select(o => o.Id));
            file.Equipment.RemoveAll(id => !ids.Contains(id));
            foreach (var o in offered)
                if (!file.Equipment.Contains(o.Id)) file.Equipment.Add(o.Id);
        }

        /// <summary>Названия вещей, которых после перехода в класс next в наборе не окажется.
        /// Предысторию смотрит ТОЖЕ: верёвка Солдата остаётся при любом классе, и обещать её потерю
        /// значило бы соврать ровно так же, как молчать про кожаный доспех Плута.</summary>
        static List<string> EquipmentLostTo(CharacterFile file, RulesData rules, ClassDef next)
        {
            var bg = rules.Backgrounds.FirstOrDefault(b => b.Id == file.BackgroundId);
            var keeps = new HashSet<string>(StartingEquipment(next, bg, rules).Select(o => o.Id));
            return file.Equipment.Where(id => !keeps.Contains(id))
                .Select(id => rules.Items.FirstOrDefault(i => i.Id == id)?.Name ?? id)
                .ToList();
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
