using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class RulesIntegritySelfTests : MonoBehaviour
    {
        /// <summary>Крошечный, но ПОЛНЫЙ справочник: по одной записи в каждом обязательном разделе.
        /// Раньше здесь были только навык, предмет и класс — а проверка «обязательный раздел не
        /// пуст» на таком наборе краснела бы вечно. Пустоту теперь проверяет не фикстура, а
        /// отдельная самопроверка, которая опустошает разделы по одному.</summary>
        static RulesData Minimal()
        {
            var r = new RulesData { Id = "t", Title = "т", Attribution = "CC-BY 4.0" };
            r.Skills.Add(new SkillDef { Id = "stealth", Name = "Скрытность", AbilityId = "dex" });
            r.Skills.Add(new SkillDef { Id = "athletics", Name = "Атлетика", AbilityId = "str" });
            r.Skills.Add(new SkillDef { Id = "arcana", Name = "Магия", AbilityId = "int" });
            r.Items.Add(new ItemDef { Id = "rope", Name = "Верёвка", Kind = "gear" });
            r.Feats.Add(new FeatDef { Id = "alert", Name = "Бдительный", Category = "origin" });
            r.Races.Add(new RaceDef { Id = "halfling", Name = "Полурослик", Speed = 30 });
            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "soldier", Name = "Солдат", OriginFeatId = "alert",
                AbilityChoices = new List<string> { "str", "dex", "con" },
                SkillIds = new List<string> { "stealth", "athletics" },
                Equipment = new List<string> { "rope" }
            });
            var c = new ClassDef { Id = "rogue", Name = "Плут", HitDie = "d8" };
            // Уровни повышения проставлены: без них класс теперь неполон (проверка
            // CheckClassHasAbilityScoreLevels), и «целый набор проходит» краснел бы на пустом месте.
            for (int i = 1; i <= 20; i++)
                c.Levels.Add(new ClassLevel
                {
                    Level = i,
                    Choice = (i == 4 || i == 8 || i == 12 || i == 16 || i == 19) ? "asi" : null
                });
            r.Classes.Add(c);
            return r;
        }

        /// <summary>Второй класс — законно целый, чтобы фикстуры могли разводить «в пределах
        /// класса» и «во всём справочнике».</summary>
        static ClassDef SecondClass(string id)
        {
            var c = new ClassDef { Id = id, Name = id, HitDie = "d10" };
            for (int i = 1; i <= 20; i++)
                c.Levels.Add(new ClassLevel { Level = i, Choice = i == 4 ? "asi" : null });
            return c;
        }

        [ContextMenu("Self-Test: справочник — целый набор проходит")]
        public void SelfTestCleanRulesPass()
        {
            var errors = RulesIntegrity.Check(Minimal());
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL целый набор: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — пропущенный уровень класса ловится")]
        public void SelfTestMissingClassLevelCaught()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: убираем уровень 13 из СЕРЕДИНЫ, а не с конца.
            // Мутант «проверять только Levels.Count == 20» на этой фикстуре тоже упадёт (их 19),
            // поэтому вторая самопроверка ниже сохраняет счёт, но ломает номера.
            var r = Minimal();
            r.Classes[0].Levels.RemoveAll(l => l.Level == 13);
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("rogue") && e.Contains("13"));
            if (!ok) Debug.LogError("FAIL пропущенный уровень: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — сдвоенный уровень при верном счёте ловится")]
        public void SelfTestDuplicateLevelCaughtEvenWhenCountIsRight()
        {
            // Мутант, который эта фикстура убивает: «проверять Levels.Count == 20». Уровней ровно
            // 20, но два тринадцатых и ни одного четырнадцатого — счёт верен, набор нет.
            //
            // ПРОВЕРЯЕМ ИМЕННО СООБЩЕНИЕ ПРО ПОВТОР, а не просто «в ошибках есть 14»: та же фикстура
            // порождает и ошибку «нет уровня 14» из соседней проверки, и утверждение по числу 14
            // проходило бы даже с полностью выключенной проверкой повторов. Ветка осталась бы без
            // покрытия внутри самопроверки, написанной ради неё.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 14).Level = 13;
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("rogue") && e.Contains("13") && e.Contains("встречается"))
                   && errors.Any(e => e.Contains("rogue") && e.Contains("нет уровня 14"));
            if (!ok) Debug.LogError("FAIL сдвоенный уровень: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — навык класса, которого нет в Skills, ловится")]
        public void SelfTestUnknownClassSkillCaught()
        {
            var r = Minimal();
            r.Classes[0].SkillChoices.Add("acrobatics");   // в Skills её нет
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("acrobatics"));
            if (!ok) Debug.LogError("FAIL неизвестный навык класса: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — снаряжение предыстории, которого нет в Items, ловится")]
        public void SelfTestUnknownBackgroundItemCaught()
        {
            var r = Minimal();
            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "sage", Name = "Мудрец", OriginFeatId = "magic-initiate",
                AbilityChoices = new List<string> { "int", "wis", "cha" },
                SkillIds = new List<string> { "stealth", "athletics" },
                Equipment = new List<string> { "quill" }      // в Items его нет
            });
            r.Feats.Add(new FeatDef { Id = "magic-initiate", Name = "Посвящённый в магию", Category = "origin" });
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("quill"));
            if (!ok) Debug.LogError("FAIL неизвестное снаряжение: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — опечатка в виде выбора ловится")]
        public void SelfTestUnknownChoiceKindCaught()
        {
            // Мутант, который эта фикстура убивает: не проверять Choice вовсе. «ASI» с большой
            // буквы молча означало бы «выбора на этом уровне нет» — целый уровень повышения
            // характеристик исчез бы из двенадцати классов, набранных руками, а гейт остался бы зелёным.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 4).Choice = "ASI";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("ASI"));
            if (!ok) Debug.LogError("FAIL вид выбора: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — уровень выбора подкласса без подклассов ловится")]
        public void SelfTestSubclassLevelWithoutSubclassesCaught()
        {
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 3).Choice = "subclass";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("подкласс"));
            if (!ok) Debug.LogError("FAIL подкласс без подклассов: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — пустая строка авторства ловится")]
        public void SelfTestEmptyAttributionCaught()
        {
            var r = Minimal(); r.Attribution = "";
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("авторств"));
            if (!ok) Debug.LogError("FAIL пустое авторство: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — предыстория даёт ровно три характеристики на выбор")]
        public void SelfTestBackgroundNeedsThreeAbilityChoices()
        {
            // Черту «alert» больше не добавляем: она уже есть в Minimal(), и второй экземпляр
            // теперь был бы повтором идентификатора — лишняя ошибка в фикстуре про другое.
            var r = Minimal();
            r.Backgrounds.Add(new BackgroundDef
            {
                Id = "criminal", Name = "Преступник", OriginFeatId = "alert",
                AbilityChoices = new List<string> { "dex", "int" },   // две вместо трёх
                SkillIds = new List<string> { "stealth", "athletics" }
            });
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("criminal"));
            if (!ok) Debug.LogError("FAIL три характеристики: " + string.Join("; ", errors));
            Done(ok);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // ПРОВЕРКИ НАЛИЧИЯ И ЗНАЧЕНИЙ.
        // Всё, что ниже, появилось после находки: справочник с «"Classes": []» давал ТОТ ЖЕ самый
        // PASS, что и справочник с четырьмя классами. Проверки согласованности — это `foreach`, а
        // `foreach` по пустому списку молчит, поэтому зелёный гейт не доказывал наличия данных.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: справочник — пустой обязательный раздел ловится")]
        public void SelfTestEmptyRequiredSectionCaught()
        {
            // Мутант, которого убивает КАЖДАЯ строка списка: «проверять только Classes». Разделы
            // опустошаются по одному, и промах хотя бы по одному разделу валит самопроверку —
            // поэтому нельзя закрыть находку одной проверкой на самый заметный раздел.
            var sections = new (string Field, Action<RulesData> Clear)[]
            {
                ("Skills",      x => x.Skills.Clear()),
                ("Items",       x => x.Items.Clear()),
                ("Feats",       x => x.Feats.Clear()),
                ("Races",       x => x.Races.Clear()),
                ("Backgrounds", x => x.Backgrounds.Clear()),
                ("Classes",     x => x.Classes.Clear())
            };
            var missed = new List<string>();
            foreach (var (field, clear) in sections)
            {
                var r = Minimal();
                clear(r);
                var errors = RulesIntegrity.Check(r);
                if (!errors.Any(e => e.Contains(field) && e.Contains("пуст"))) missed.Add(field);
            }
            bool ok = missed.Count == 0;
            if (!ok) Debug.LogError("FAIL пустой раздел не пойман: " + string.Join(", ", missed));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — повторённый идентификатор ловится")]
        public void SelfTestDuplicateIdsCaught()
        {
            // Мутант: «проверять уникальность только у подклассов» (как было). Разрешимость по
            // HashSet на дубль отвечает «есть, конечно», поэтому дубль был вечнозелёным.
            var dupes = new (string Id, Action<RulesData> Add)[]
            {
                ("stealth",  x => x.Skills.Add(new SkillDef { Id = "stealth", Name = "Ещё скрытность", AbilityId = "dex" })),
                ("rope",     x => x.Items.Add(new ItemDef { Id = "rope", Name = "Ещё верёвка", Kind = "gear" })),
                ("alert",    x => x.Feats.Add(new FeatDef { Id = "alert", Name = "Ещё бдительный", Category = "origin" })),
                ("halfling", x => x.Races.Add(new RaceDef { Id = "halfling", Name = "Ещё полурослик", Speed = 30 })),
                ("soldier",  x => x.Backgrounds.Add(new BackgroundDef
                {
                    Id = "soldier", Name = "Ещё солдат", OriginFeatId = "alert",
                    AbilityChoices = new List<string> { "str", "dex", "con" },
                    SkillIds = new List<string> { "stealth", "athletics" }
                })),
                ("rogue",    x => x.Classes.Add(SecondClass("rogue")))
            };
            var missed = new List<string>();
            foreach (var (id, add) in dupes)
            {
                var r = Minimal();
                add(r);
                var errors = RulesIntegrity.Check(r);
                if (!errors.Any(e => e.Contains(id) && e.Contains("объявлен"))) missed.Add(id);
            }
            bool ok = missed.Count == 0;
            if (!ok) Debug.LogError("FAIL повтор идентификатора не пойман: " + string.Join(", ", missed));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — неизвестный вид предмета ловится")]
        public void SelfTestUnknownItemKindCaught()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ в обе стороны: четыре законных вида обязаны
            // пройти молча, английская опечатка «armour» — упасть. Мутант «ругаться на любой Kind,
            // кроме armor» умирает на первой половине, мутант «не проверять вовсе» — на второй.
            var good = Minimal();
            good.Items.Add(new ItemDef { Id = "plate", Name = "Латы", Kind = "armor", ArmorBase = 18 });
            good.Items.Add(new ItemDef { Id = "shield", Name = "Щит", Kind = "shield" });
            good.Items.Add(new ItemDef { Id = "dagger", Name = "Кинжал", Kind = "weapon" });
            bool quietOnGood = !RulesIntegrity.Check(good).Any(e => e.Contains("неизвестный вид"));

            var bad = Minimal();
            bad.Items.Add(new ItemDef { Id = "brigandine", Name = "Бригантина", Kind = "armour" });
            bool loudOnBad = RulesIntegrity.Check(bad)
                .Any(e => e.Contains("brigandine") && e.Contains("armour"));

            bool ok = quietOnGood && loudOnBad;
            if (!ok) Debug.LogError($"FAIL вид предмета: молчит на верных = {quietOnGood}, ловит armour = {loudOnBad}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — вид без скорости ловится")]
        public void SelfTestRaceWithoutSpeedCaught()
        {
            var zero = Minimal(); zero.Races[0].Speed = 0;
            var negative = Minimal(); negative.Races[0].Speed = -5;
            bool loudOnZero = RulesIntegrity.Check(zero).Any(e => e.Contains("halfling") && e.Contains("скорость"));
            bool loudOnNegative = RulesIntegrity.Check(negative).Any(e => e.Contains("halfling") && e.Contains("скорость"));
            // Скорость 35 футов законна (Голиаф, Лесной эльф) — мутант «скорость обязана быть 30»
            // умирает здесь.
            var fast = Minimal(); fast.Races[0].Speed = 35;
            bool quietOnFast = !RulesIntegrity.Check(fast).Any(e => e.Contains("скорость"));

            bool ok = loudOnZero && loudOnNegative && quietOnFast;
            if (!ok) Debug.LogError($"FAIL скорость вида: 0 = {loudOnZero}, −5 = {loudOnNegative}, 35 молча = {quietOnFast}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — предыстория даёт ровно два навыка")]
        public void SelfTestBackgroundNeedsExactlyTwoSkills()
        {
            // Мутант «навыков должно быть НЕ МЕНЬШЕ двух» умирает на третьем навыке: в правилах
            // 2024 предыстория даёт ровно два, третий — это подарок, которого нет в SRD.
            var one = Minimal();
            one.Backgrounds[0].SkillIds = new List<string> { "stealth" };
            var three = Minimal();
            three.Backgrounds[0].SkillIds = new List<string> { "stealth", "athletics", "arcana" };

            bool loudOnOne = RulesIntegrity.Check(one).Any(e => e.Contains("soldier") && e.Contains("ровно 2 навыка"));
            bool loudOnThree = RulesIntegrity.Check(three).Any(e => e.Contains("soldier") && e.Contains("ровно 2 навыка"));
            bool ok = loudOnOne && loudOnThree;
            if (!ok) Debug.LogError($"FAIL число навыков предыстории: один = {loudOnOne}, три = {loudOnThree}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — кость хитов не вида d8 ловится")]
        public void SelfTestBadHitDieCaught()
        {
            // Условие проверки повторяет SheetMath.ParseDie, поэтому фикстура перебирает ровно те
            // строки, на которых ParseDie возвращает 0 и максимум хитов остаётся нулевым. Мутант
            // «проверять только пустую строку» умирает на кириллической «к8» — самой вероятной
            // опечатке в русском файле.
            var bad = new[] { "", "8", "к8", "d0", "dX" };
            var missed = new List<string>();
            foreach (var die in bad)
            {
                var r = Minimal(); r.Classes[0].HitDie = die;
                if (!RulesIntegrity.Check(r).Any(e => e.Contains("rogue") && e.Contains("кость хитов")))
                    missed.Add($"«{die}»");
            }
            // Все четыре законные кости обязаны пройти молча — и вместе с ними «d 8».
            //
            // «d 8» ЗДЕСЬ НАРОЧНО, И ЭТО НЕ ПОБЛАЖКА. Фикстура сперва объявляла его негодным, и
            // самопроверка покраснела: int.TryParse глотает ведущий пробел, поэтому ParseDie
            // возвращает 8 и хиты считаются ВЕРНО. Проверка обязана совпадать с арифметикой, а не
            // быть строже неё: строже — значит краснеть на данных, которые работают. Мутант
            // «запретить пробел» умирает именно тут.
            foreach (var die in new[] { "d6", "d8", "d10", "d12", "d 8" })
            {
                var r = Minimal(); r.Classes[0].HitDie = die;
                if (RulesIntegrity.Check(r).Any(e => e.Contains("кость хитов"))) missed.Add($"ложно на «{die}»");
            }
            bool ok = missed.Count == 0;
            if (!ok) Debug.LogError("FAIL кость хитов: " + string.Join(", ", missed));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — защита без доспехов по неизвестной характеристике ловится")]
        public void SelfTestUnknownUnarmoredDefenseAbilityCaught()
        {
            // Не «молча неверно», а ПАДЕНИЕ: SheetMathSkills ищет характеристику через
            // Array.IndexOf и обращается к AbilityNames[-1] — лист Варвара не открывается вовсе.
            //
            // Фикстура разводит правило и две подделки сразу: пустое значение законно (у одиннадцати
            // классов из двенадцати), «con» законно (Варвар), «тел» — нет. Мутант «любое непустое
            // значение — ошибка» умирает на «con», мутант «не проверять» — на «тел».
            var absent = Minimal(); absent.Classes[0].UnarmoredDefenseAbility = null;
            var empty = Minimal(); empty.Classes[0].UnarmoredDefenseAbility = "";
            var valid = Minimal(); valid.Classes[0].UnarmoredDefenseAbility = "con";
            var russian = Minimal(); russian.Classes[0].UnarmoredDefenseAbility = "тел";

            bool Quiet(RulesData r) => !RulesIntegrity.Check(r).Any(e => e.Contains("защита без доспехов"));
            bool ok = Quiet(absent) && Quiet(empty) && Quiet(valid)
                   && RulesIntegrity.Check(russian).Any(e => e.Contains("rogue") && e.Contains("тел"));
            if (!ok) Debug.LogError($"FAIL защита без доспехов: null = {Quiet(absent)}, пусто = {Quiet(empty)}, "
                                  + $"con = {Quiet(valid)}, «тел» ловится = {!Quiet(russian)}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — класс без уровней повышения характеристик ловится")]
        public void SelfTestClassWithoutAbilityScoreLevelsCaught()
        {
            // Уровни повышения — ДАННЫЕ, а не формула «уровень % 4»: у Воина их семь, у Плута
            // шесть, у прочих пять. Потерянный уровень нечем восстановить и нечем заметить —
            // проверка согласованности на его отсутствие не реагирует никак.
            var r = Minimal();
            foreach (var l in r.Classes[0].Levels.Where(l => l.Choice == "asi")) l.Choice = null;
            bool loud = RulesIntegrity.Check(r).Any(e => e.Contains("rogue") && e.Contains("повышения характеристик"));

            // ...а один-единственный оставшийся уровень повышения — уже законный набор: мутант
            // «требовать пять уровней» сломал бы Воина с семью и любой доморощенный класс.
            var one = Minimal();
            foreach (var l in one.Classes[0].Levels.Where(l => l.Choice == "asi" && l.Level != 4)) l.Choice = null;
            bool quiet = !RulesIntegrity.Check(one).Any(e => e.Contains("повышения характеристик"));

            bool ok = loud && quiet;
            if (!ok) Debug.LogError($"FAIL уровни повышения: пусто ловится = {loud}, один проходит = {quiet}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — отрицательное число ячеек ловится")]
        public void SelfTestNegativeSpellSlotCaught()
        {
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 3).SpellSlots = new[] { 4, -1, 0, 0, 0, 0, 0, 0, 0 };
            var errors = RulesIntegrity.Check(r);
            // Утверждение НЕ по числу ошибок и НЕ по слову «ячеек»: обе соседние проверки таблицы
            // говорят про ячейки, и утверждение по общему слову проходило бы с выключенной
            // проверкой знака. Проверяется именно сообщение про «меньше нуля» и именно 2 круг.
            bool ok = errors.Any(e => e.Contains("rogue") && e.Contains("уровень 3")
                                      && e.Contains("2 круга") && e.Contains("меньше нуля"));
            if (!ok) Debug.LogError("FAIL отрицательные ячейки: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — убывающая таблица ячеек ловится")]
        public void SelfTestShrinkingSpellSlotsCaught()
        {
            // ФИКСТУРА РАЗВОДИТ ПОКОЛОНОЧНОЕ И ПОСТРОЧНОЕ СРАВНЕНИЕ. Второй круг сперва РАСТЁТ
            // (0 → 1 на пятом уровне) и лишь потом падает (1 → 0 на шестом). Мутант «строки обязаны
            // совпадать» упал бы на росте, мутант «смотреть только первый круг» — не заметил бы
            // падения, потому что первый круг ровный везде.
            var r = Minimal();
            foreach (var l in r.Classes[0].Levels)
                l.SpellSlots = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 };
            r.Classes[0].Levels.First(l => l.Level == 5).SpellSlots = new[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 };
            var errors = RulesIntegrity.Check(r);

            bool loudOnDrop = errors.Any(e => e.Contains("rogue") && e.Contains("2 круга")
                                              && e.Contains("уровне 6") && e.Contains("убывает"));
            bool quietOnGrowth = !errors.Any(e => e.Contains("уровне 5") && e.Contains("убывает"));
            bool ok = loudOnDrop && quietOnGrowth;
            if (!ok) Debug.LogError($"FAIL убывающая таблица (падение = {loudOnDrop}, рост молча = "
                                  + $"{quietOnGrowth}): " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — таблица Магии договора не считается убывающей")]
        public void SelfTestPactMagicSlotsMayMoveUpARing()
        {
            // Настоящая таблица Колдуна: одна ячейка 1 круга, потом две ячейки 1 круга, потом две
            // ячейки 2 круга — и первый круг обнуляется. Общее правило «круг за кругом не убывает»
            // на этом краснеет, и краснеет ПО ДЕЛУ для всех прочих: Магия договора устроена иначе.
            // Мутант — снятый `if (c.PactMagic) return`: поставляемый Колдун становится незаконным,
            // и единственный способ его вписать — соврать в таблице.
            var r = PactRules();
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL таблица Магии договора: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — у Магии договора ячейки только одного круга")]
        public void SelfTestPactMagicSlotsLiveInOneRing()
        {
            // Замена общего правила обязана быть СТРОЖЕ его, иначе флаг просто снимал бы охрану.
            // Три мутанта, по одному на правило:
            //   • снята проверка «ненулевой круг один» — Колдун с таблицей полного заклинателя
            //     проходит молча, хотя ячеек у него столько не бывает;
            //   • снята проверка числа — две ячейки превращаются в одну и никто не замечает;
            //   • снята проверка круга — на 5 уровне круг падает со 2 на 1, то есть колдун слабеет.
            var many = PactRules();
            many.Classes[0].Levels.First(l => l.Level == 5).SpellSlots = new[] { 2, 2, 0, 0, 0, 0, 0, 0, 0 };
            var fewer = PactRules();
            fewer.Classes[0].Levels.First(l => l.Level == 5).SpellSlots = new[] { 0, 1, 0, 0, 0, 0, 0, 0, 0 };
            var lower = PactRules();
            lower.Classes[0].Levels.First(l => l.Level == 5).SpellSlots = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 };

            bool loudOnTwoRings = RulesIntegrity.Check(many)
                .Any(e => e.Contains("уровень 5") && e.Contains("2 кругах"));
            bool loudOnFewer = RulesIntegrity.Check(fewer)
                .Any(e => e.Contains("уровне 5") && e.Contains("убывает"));
            bool loudOnLowerRing = RulesIntegrity.Check(lower)
                .Any(e => e.Contains("уровне 5") && e.Contains("понижается"));

            bool ok = loudOnTwoRings && loudOnFewer && loudOnLowerRing;
            if (!ok) Debug.LogError($"FAIL охрана Магии договора: два круга = {loudOnTwoRings}, "
                                  + $"меньше ячеек = {loudOnFewer}, круг ниже = {loudOnLowerRing}");
            Done(ok);
        }

        /// <summary>Справочник, где единственный класс колдует Магией договора: одна ячейка 1 круга
        /// на 1–2 уровнях, две ячейки 2 круга с 3-го. Обнуление первого круга на третьем уровне —
        /// то самое место, где общее правило и правило Колдуна расходятся.</summary>
        static RulesData PactRules()
        {
            var r = Minimal();
            foreach (var l in r.Classes[0].Levels)
                l.SpellSlots = l.Level < 3
                    ? new[] { l.Level, 0, 0, 0, 0, 0, 0, 0, 0 }
                    : new[] { 0, 2, 0, 0, 0, 0, 0, 0, 0 };
            r.Classes[0].PactMagic = true;
            return r;
        }

        [ContextMenu("Self-Test: справочник — выбрать навыков больше, чем есть в списке, ловится")]
        public void SelfTestSkillPickCountBeyondChoicesCaught()
        {
            var r = Minimal();
            r.Classes[0].SkillChoices = new List<string> { "stealth", "athletics" };
            r.Classes[0].SkillPickCount = 3;                     // из двух выбрать три
            bool loudOnTooMany = RulesIntegrity.Check(r).Any(e => e.Contains("rogue") && e.Contains("в списке их 2"));

            var negative = Minimal();
            negative.Classes[0].SkillPickCount = -1;
            bool loudOnNegative = RulesIntegrity.Check(negative).Any(e => e.Contains("rogue") && e.Contains("меньше нуля"));

            // Взять ВСЕ навыки из списка законно (так устроен Плут с четырьмя из одиннадцати, а в
            // пределе — любой класс): мутант «выбирать можно строго меньше, чем предложено» умирает.
            var all = Minimal();
            all.Classes[0].SkillChoices = new List<string> { "stealth", "athletics" };
            all.Classes[0].SkillPickCount = 2;
            bool quietOnAll = !RulesIntegrity.Check(all).Any(e => e.Contains("выбрать нужно"));

            bool ok = loudOnTooMany && loudOnNegative && quietOnAll;
            if (!ok) Debug.LogError($"FAIL число выбираемых навыков: три из двух = {loudOnTooMany}, "
                                  + $"минус один = {loudOnNegative}, два из двух молча = {quietOnAll}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — компетентность в навыке, которого класс не даёт, ловится")]
        public void SelfTestExpertiseChoiceOutsideSkillChoicesCaught()
        {
            // МУТАНТ №1: проверки нет вовсе. Тогда «компетентность в Магии» у класса, который Магией
            // владеть не даёт, лежала бы в данных молча: экран такую строку не покажет НИКОГДА
            // (компетентность требует владения), и заметить опечатку смог бы только знаток правил.
            var r = Minimal();
            r.Classes[0].SkillChoices = new List<string> { "stealth", "athletics" };
            r.Classes[0].ExpertiseChoices = new List<string> { "arcana" };
            bool loudOnStranger = RulesIntegrity.Check(r).Any(e => e.Contains("rogue") && e.Contains("arcana"));

            // МУТАНТ №2: «любой непустой список — ошибка». Сужение — законное и нужное состояние
            // (так устроен поставляемый Волшебник), и без этой половины проверка запрещала бы ровно
            // то, ради чего заведена.
            var narrowed = Minimal();
            narrowed.Classes[0].SkillChoices = new List<string> { "stealth", "athletics" };
            narrowed.Classes[0].ExpertiseChoices = new List<string> { "stealth" };
            bool quietOnSubset = !RulesIntegrity.Check(narrowed).Any(e => e.Contains("компетентност"));

            // МУТАНТ №3: повтор не ловится. Дубль ничего не роняет и оттого невидим — ровно как
            // сдвоенный спасбросок, который стережёт Distinct в мастере.
            var doubled = Minimal();
            doubled.Classes[0].SkillChoices = new List<string> { "stealth", "athletics" };
            doubled.Classes[0].ExpertiseChoices = new List<string> { "stealth", "stealth" };
            bool loudOnDouble = RulesIntegrity.Check(doubled).Any(e => e.Contains("2 раза"));

            bool ok = loudOnStranger && quietOnSubset && loudOnDouble;
            if (!ok) Debug.LogError($"FAIL список компетентности: чужой навык = {loudOnStranger}, "
                                  + $"законное сужение молча = {quietOnSubset}, повтор = {loudOnDouble}");
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — сдвоенный уровень подкласса ловится")]
        public void SelfTestDuplicateSubclassLevelCaught()
        {
            // У КЛАССА сдвоенный уровень проверялся с самого начала, у ПОДКЛАССА — нет. Фикстура
            // это и разводит: уровни самого класса остаются безупречными, повтор только у
            // подкласса, поэтому мутант «класcная проверка покрывает и подклассы» умирает.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 3).Choice = "subclass";
            r.Classes[0].Subclasses.Add(new SubclassDef
            {
                Id = "thief", Name = "Вор",
                Levels = new List<SubclassLevel>
                {
                    new SubclassLevel { Level = 3, Features = { new FeatureRef { Id = "fast-hands", Name = "Быстрые руки" } } },
                    new SubclassLevel { Level = 3, Features = { new FeatureRef { Id = "second-story-work", Name = "Работа на втором этаже" } } }
                }
            });
            var errors = RulesIntegrity.Check(r);
            bool loudOnSubclass = errors.Any(e => e.Contains("подкласс thief") && e.Contains("уровень 3")
                                                  && e.Contains("встречается"));
            bool quietOnClass = !errors.Any(e => e.Contains("класс rogue: уровень"));
            bool ok = loudOnSubclass && quietOnClass;
            if (!ok) Debug.LogError($"FAIL сдвоенный уровень подкласса (ловится = {loudOnSubclass}, "
                                  + $"класс чист = {quietOnClass}): " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — умение, объявленное в классе дважды, ловится")]
        public void SelfTestDuplicateFeatureIdInClassCaught()
        {
            // ФИКСТУРА РАЗВОДИТ «В ПРЕДЕЛАХ КЛАССА» И «ВО ВСЁМ СПРАВОЧНИКЕ». Одно и то же умение
            // стоит у ДВУХ классов — это законно («Дополнительная атака» есть и у Воина, и у
            // Варвара), и мутант «идентификаторы умений уникальны глобально» умирает именно здесь.
            // Внутри же первого класса оно повторено на двух уровнях — это скопированный уровень.
            var r = Minimal();
            r.Classes[0].Levels.First(l => l.Level == 1).Features
                .Add(new FeatureRef { Id = "extra-attack", Name = "Дополнительная атака" });
            r.Classes[0].Levels.First(l => l.Level == 5).Features
                .Add(new FeatureRef { Id = "extra-attack", Name = "Дополнительная атака" });
            var barbarian = SecondClass("barbarian");
            barbarian.Levels.First(l => l.Level == 5).Features
                .Add(new FeatureRef { Id = "extra-attack", Name = "Дополнительная атака" });
            r.Classes.Add(barbarian);

            var errors = RulesIntegrity.Check(r);
            bool loudOnRogue = errors.Any(e => e.Contains("класс rogue") && e.Contains("extra-attack")
                                               && e.Contains("объявлено"));
            bool quietOnBarbarian = !errors.Any(e => e.Contains("barbarian") && e.Contains("extra-attack"));
            bool ok = loudOnRogue && quietOnBarbarian;
            if (!ok) Debug.LogError($"FAIL повтор умения (внутри класса = {loudOnRogue}, между классами "
                                  + $"молча = {quietOnBarbarian}): " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — чужая ключевая характеристика названа вслух")]
        public void SelfTestUnknownPrimaryAbilityIsReported()
        {
            // Мутант: снятая CheckClassPrimaryAbilities. Опечатка здесь ничего не роняет и оттого
            // невидима — «charisma» просто отфильтруется в SuggestedAssignment, ключевых окажется
            // ноль, и мастер предложит обычный порядок, то есть ровно то, ради отмены чего поле и
            // заведено. Сдвоенная проверяется тем же вызовом: её так же молча съедает Distinct.
            var r = Minimal();
            r.Classes[0].PrimaryAbilities = new List<string> { "charisma", "dex", "dex" };
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Any(e => e.Contains("charisma") && e.Contains("не из шести"))
                   && errors.Any(e => e.Contains("dex") && e.Contains("названа"));
            if (!ok) Debug.LogError("FAIL ключевая характеристика: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — пустая ключевая характеристика законна")]
        public void SelfTestEmptyPrimaryAbilityIsLegal()
        {
            // Страж от ложного срабатывания И вторая половина решения: чужой справочник (правила
            // 2014 такого понятия не знают) обязан проходить целостность, а мастер — вернуться к
            // спасброскам. Мутант «ключевая характеристика обязательна» покраснел бы на каждом
            // классе такого набора и запретил бы его целиком.
            var r = Minimal();
            r.Classes[0].PrimaryAbilities = null;      // именно так это приходит из JSON «: null»
            var errors = RulesIntegrity.Check(r);
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL пустая ключевая характеристика: " + string.Join("; ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — ПОСТАВЛЯЕМЫЙ файл целостен")]
        public void SelfTestShippedRulesAreConsistent()
        {
            // Провайдера подкладывают снаружи (харнесс — с диска, Unity — из Resources). Его
            // отсутствие — ОШИБКА, а не повод пропустить: иначе единственная проверка настоящих
            // данных стала бы зелёной от того, что её не запустили.
            if (RulesTextSource.Provider == null)
            {
                Debug.LogError("FAIL поставляемый справочник: RulesTextSource.Provider не задан — "
                             + "проверка данных не выполнялась");
                return;
            }
            var rules = RulesLoader.FromJson(RulesTextSource.Provider());
            var errors = RulesIntegrity.Check(rules);
            bool ok = errors.Count == 0;
            if (!ok) Debug.LogError("FAIL поставляемый справочник:\n  " + string.Join("\n  ", errors));
            Done(ok);
        }

        [ContextMenu("Self-Test: справочник — КАЖДЫЙ поставляемый класс называет ключевую характеристику")]
        public void SelfTestShippedClassesNamePrimaryAbility()
        {
            // Целостность пустое поле РАЗРЕШАЕТ — чужому справочнику по правилам 2014 иначе нельзя.
            // А наш поставляемый файл сделан по правилам 2024, где ключевая характеристика названа
            // у каждого класса вслух, и пустое поле здесь значит «забыли дописать». Мутант —
            // забытая строка у одного класса: целостность зелена, а мастер тихо возвращается к
            // спасброскам и советует не то. Ловится только сверкой по самим данным.
            if (RulesTextSource.Provider == null)
            {
                Debug.LogError("FAIL ключевые характеристики: RulesTextSource.Provider не задан — "
                             + "проверка данных не выполнялась");
                return;
            }
            var rules = RulesLoader.FromJson(RulesTextSource.Provider());
            var silent = rules.Classes
                .Where(c => c.PrimaryAbilities == null || c.PrimaryAbilities.Count == 0)
                .Select(c => c.Id).ToList();
            bool ok = rules.Classes.Count > 0 && silent.Count == 0;
            if (!ok) Debug.LogError($"FAIL ключевые характеристики (классов {rules.Classes.Count}): "
                                  + "молчат [" + string.Join(",", silent) + "]");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
