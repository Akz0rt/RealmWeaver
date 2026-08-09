using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class SheetSkillsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: навыки — владение от предыстории и от класса складываются в один набор")]
        public void SelfTestSkillsComeFromBothSources()
        {
            // «Атлетика» приходит от предыстории, «скрытность» и «магия» добраны классом.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var ath = d.Skills.First(s => s.SkillId == "athletics");
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            bool ok = ath.Proficient && arc.Proficient;
            if (!ok) Debug.LogError($"FAIL источники навыков: атлетика={ath.Proficient}, магия={arc.Proficient}");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — без владения бонус равен голому модификатору")]
        public void SelfTestUntrainedSkillIsBareModifier()
        {
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");            // магию больше не берём
            var d = SheetMath.Compute(c, rules);
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            // ИНТ 12 → +1, мастерства нет. Мутант «мастерство всем навыкам» даёт 4 — расходится.
            bool ok = !arc.Proficient && arc.Bonus == 1;
            if (!ok) Debug.LogError($"FAIL без владения: {arc.Bonus} влад={arc.Proficient}, ждали 1/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность удваивает мастерство, а не бонус")]
        public void SelfTestExpertiseDoublesProficiencyOnly()
        {
            // ЛОВ +2, мастерство +3. Владение → 5. Компетентность → 2 + 6 = 8.
            // Мутант «удваивать весь бонус» даёт 10 — расходится.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = st.Expertise && st.Bonus == 8;
            if (!ok) Debug.LogError($"FAIL компетентность: {st.Bonus} комп={st.Expertise}, ждали 8/да");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность без владения не действует")]
        public void SelfTestExpertiseRequiresProficiency()
        {
            // «Магия» не входит ни в SkillIds, ни в навыки предыстории — значит владения нет вовсе.
            // Компетентность в такой навык всё равно записана (мастер мог натыкать её раньше владения).
            // МУТАНТ: фильтр при чтении (SheetMath.ExpertiseChosenIds) без проверки владения — только
            // AllowsExpertiseIn. На этой фикстуре он не ловится ни одним другим тестом: везде, где
            // есть компетентность, владение тоже есть — Fixtures.Character() кладёт "stealth" в оба
            // списка сразу.
            //
            // ВЫДАЧУ РАСШИРЯЕМ ДО ДВУХ НАВЫКОВ НАРОЧНО. У фикстурного Плута открыт один, а в списке
            // компетентности их станет два («скрытность» и добавленная «магия»); лишнее сверх
            // открытого лист отрезает по порядку — и «магия» отвалилась бы ПО ОТСЕЧЕНИЮ, а не по
            // отсутствию владения. Проверка осталась бы зелёной с убранным `prof &&`, то есть
            // стала бы пустой. Со второй выдачей «магия» лежит ВНУТРИ открытого числа, и не
            // сработать может только правило владения — то самое, ради которого тест написан.
            var rules = Fixtures.Rules();
            rules.Classes[0].ExpertiseGrants[0].PickCount = 2;
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");
            c.ExpertiseIds.Add("arcana");
            var d = SheetMath.Compute(c, rules);
            var arc = d.Skills.First(s => s.SkillId == "arcana");
            // ИНТ 12 → +1, без владения компетентность не считается — бонус остаётся голым модификатором.
            bool ok = !arc.Proficient && !arc.Expertise && arc.Bonus == 1;
            if (!ok) Debug.LogError($"FAIL компетентность без владения: {arc.Bonus} влад={arc.Proficient} комп={arc.Expertise}, ждали 1/нет/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность вне списка класса не удваивает мастерство")]
        public void SelfTestExpertiseOutsideClassChoicesDoesNotDouble()
        {
            // МУТАНТ №1: `AllowsExpertiseIn` всегда true (то есть ExpertiseChoices не читают вовсе).
            // Тогда «скрытность» удвоится и бонус станет 8 вместо 5 — ровно тот дефект, ради
            // которого поле заведено: Волшебник 2 уровня ставил компетентность на Проницательность
            // и получал +4 там, где правила дают +2.
            //
            // МУТАНТ №2: фильтр стоит ТОЛЬКО в мастере, а лист считает по-старому. Здесь файл
            // приходит уже с записанной компетентностью — так выглядит СТАРЫЙ сохранённый лист,
            // сделанный до того, как класс сузил список. Экран такую строку больше не рисует, снять
            // её нечем, и без фильтра при чтении она удваивала бы мастерство вечно.
            //
            // МУТАНТ №3 (в строке «Чего не хватает»): считать file.ExpertiseIds.Count вместо
            // подходящих. Тогда лист молчал бы — компетентности нет, а сказать некому.
            var rules = Fixtures.Rules();
            rules.Classes[0].ExpertiseChoices.Add("arcana");   // только Магия, но НЕ Скрытность
            var c = Fixtures.Character();                      // компетентность лежит в «скрытности»
            var d = SheetMath.Compute(c, rules);
            var st = d.Skills.First(s => s.SkillId == "stealth");
            // ЛОВ +2, мастерство +3, владение есть, компетентности класс тут не даёт → 5, а не 8.
            bool ok = st.Proficient && !st.Expertise && st.Bonus == 5
                   && d.Missing.Any(m => m.Contains("Компетентность выбрана в 0"));
            if (!ok) Debug.LogError($"FAIL компетентность вне списка класса: {st.Bonus} "
                                  + $"комп={st.Expertise}, ждали 5/нет; чего не хватает: ["
                                  + string.Join(" | ", d.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность берётся и из навыков предыстории")]
        public void SelfTestExpertiseEligibleIncludesBackgroundSkills()
        {
            // ЖИВОЙ СЛУЧАЙ ДМа в синтетическом виде: единственный годный навык приходит ОТ
            // ПРЕДЫСТОРИИ, а всё, что взято у класса, класс для компетентности запрещает.
            //
            // МУТАНТ №1: список годных снова строится из одного file.SkillIds. Тогда он пуст —
            // Волшебник-Мудрец не может поставить компетентность ни на Магию, ни на Историю, хотя
            // владеет обеими. Фикстура построена так, что под старым правилом ответ ДРУГОЙ: ни один
            // выбранный у класса навык в шестёрку не входит.
            // МУТАНТ №2: владение сложили, а разрешение класса (AllowsExpertiseIn) читать перестали.
            // Тогда в список попадут все четыре навыка, включая Проницательность от предыстории,
            // которой класс компетентности не даёт. На одном «Мудреце» этот мутант не ловится —
            // оба его навыка внутри шестёрки, — поэтому предыстория здесь даёт навык ВНЕ списка.
            var rules = Fixtures.Rules();
            rules.Skills.Add(new SkillDef { Id = "insight", Name = "Проницательность", AbilityId = "wis" });
            rules.Backgrounds[0].SkillIds.Add("insight");          // Солдат: атлетика + проницательность
            rules.Classes[0].ExpertiseChoices.Add("athletics");    // компетентность ТОЛЬКО в атлетике

            var c = Fixtures.Character();                          // у класса взяты скрытность и магия
            c.ExpertiseIds.Clear();
            c.ExpertiseIds.Add("athletics");

            var eligible = SheetMath.ExpertiseEligibleIds(c, rules, rules.Classes[0]);
            var d = SheetMath.Compute(c, rules);
            var ath = d.Skills.First(s => s.SkillId == "athletics");
            // СИЛ 8 + 2 от предыстории = 10 → +0; мастерство +3, компетентность → +6.
            bool ok = eligible.SequenceEqual(new[] { "athletics" })
                   && ath.Proficient && ath.Expertise && ath.Bonus == 6
                   && !d.Missing.Any(m => m.Contains("Компетентность"));
            if (!ok) Debug.LogError("FAIL компетентность от предыстории: годные ["
                                  + string.Join(",", eligible) + "] (ждали [athletics]), атлетика "
                                  + $"{ath.Bonus} влад={ath.Proficient} комп={ath.Expertise} (ждали 6/да/да); "
                                  + "чего не хватает: [" + string.Join(" | ", d.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — Волшебник-Мудрец видит Магию и Историю, но не Проницательность (поставляемый справочник)")]
        public void SelfTestWizardWithSageExpertiseOnRealRules()
        {
            // ТОТ ЖЕ СЛУЧАЙ НА НАСТОЯЩИХ ДАННЫХ, дословно как его нашёл ДМ: Волшебник 2 уровня с
            // предысторией «Мудрец». Мудрец даёт Магию и Историю — обе в шестёрке умения «Учёный», —
            // а руками у класса взята Проницательность, которой в шестёрке нет.
            //
            // Проверка по поставляемому файлу нужна отдельно от синтетической: правило верное, но
            // прочитанное не из тех полей (скажем, из SkillChoices вместо ExpertiseChoices) на
            // фикстуре могло бы совпасть случайно. Провайдера подкладывают снаружи; его отсутствие —
            // ошибка, а не повод пропустить.
            if (RulesTextSource.Provider == null)
            {
                Debug.LogError("FAIL Волшебник-Мудрец: RulesTextSource.Provider не задан — "
                             + "проверка данных не выполнялась");
                return;
            }
            var rules = RulesLoader.FromJson(RulesTextSource.Provider());
            var wizard = rules.Classes.First(x => x.Id == "wizard");
            var c = new CharacterFile { RulesId = rules.Id, Name = "Тест", Level = 2,
                ClassId = "wizard", BackgroundId = "sage" };
            c.SkillIds.Add("insight");                  // навык класса, но компетентности в нём нет

            var eligible = SheetMath.ExpertiseEligibleIds(c, rules, wizard);
            bool ok = eligible.Contains("arcana") && eligible.Contains("history")
                   && !eligible.Contains("insight");
            if (!ok) Debug.LogError("FAIL Волшебник-Мудрец: годные для компетентности ["
                                  + string.Join(",", eligible) + "], ждали Магию и Историю без Проницательности");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — пустой список класса значит «компетентность в любом навыке»")]
        public void SelfTestEmptyExpertiseChoicesAllowAnySkill()
        {
            // ВТОРАЯ ПОЛОВИНА ТОГО ЖЕ И. Мутант: `AllowsExpertiseIn` без ветки «список пуст» —
            // просто `ExpertiseChoices.Contains(skillId)`. Тогда у Плута, Барда и Следопыта, которые
            // по правилам 2024 выбирают из своих владений без ограничений, компетентность исчезла бы
            // целиком, а справочник остался бы зелёным: пустой список законен.
            var rules = Fixtures.Rules();
            bool listIsEmpty = rules.Classes[0].ExpertiseChoices.Count == 0;
            var d = SheetMath.Compute(Fixtures.Character(), rules);
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = listIsEmpty && st.Expertise && st.Bonus == 8;
            if (!ok) Debug.LogError($"FAIL пустой список компетентности (пуст={listIsEmpty}): "
                                  + $"{st.Bonus} комп={st.Expertise}, ждали 8/да");
            Done(ok);
        }

        /// <summary>Бард по правилам 2024: компетентность приходит ДВАЖДЫ — два навыка на 2 уровне и
        /// ещё два на 9-м. Четыре навыка, все взяты и все с компетентностью в файле; сколько из них
        /// действительно удвоится, решает уровень.
        ///
        /// В фикстуре три навыка, а нужно четыре — четвёртый добавлен здесь: с тремя проверка «на 9
        /// уровне открыто четыре» упёрлась бы в нехватку навыков, а не в правило.</summary>
        static RulesData BardWithTwoGrants(out CharacterFile c, int level)
        {
            var rules = Fixtures.Rules();
            rules.Skills.Add(new SkillDef { Id = "persuasion", Name = "Убеждение", AbilityId = "cha" });
            var bard = new ClassDef { Id = "bard", Name = "Бард", HitDie = "d8",
                SkillChoices = { "stealth", "athletics", "arcana", "persuasion" }, SkillPickCount = 4,
                ExpertiseGrants = { new ExpertiseGrant { Level = 2, PickCount = 2 },
                                    new ExpertiseGrant { Level = 9, PickCount = 2 } } };
            for (int lv = 1; lv <= 20; lv++)
                bard.Levels.Add(new ClassLevel { Level = lv, Choice = lv == 4 || lv == 8 ? "asi" : null });
            rules.Classes.Add(bard);

            c = Fixtures.Character();
            c.ClassId = "bard"; c.Level = level; c.SubclassId = null;
            c.SkillIds.Clear();
            c.ExpertiseIds.Clear();
            foreach (var id in new[] { "stealth", "athletics", "arcana", "persuasion" })
            {
                c.SkillIds.Add(id);
                c.ExpertiseIds.Add(id);
            }
            return rules;
        }

        [ContextMenu("Self-Test: навыки — вторая выдача компетентности открывает ещё два навыка (Бард 9)")]
        public void SelfTestSecondExpertiseGrantOpensMoreSkills()
        {
            // МУТАНТ: «брать только первую выдачу» (ExpertisePicksAt возвращает PickCount первой
            // записи). Бард 9 уровня получил бы 2 навыка вместо 4 — ровно то, что делало
            // приложение до этой правки: кнопки остальных гасли, «Чего не хватает» считало «2 из 2»
            // и молчало, а текст умения на том же листе обещал ещё два навыка.
            var rules = BardWithTwoGrants(out var c, 9);
            var d = SheetMath.Compute(c, rules);
            int doubled = d.Skills.Count(s => s.Expertise);
            bool ok = doubled == 4 && !d.Missing.Any(m => m.Contains("Компетентность"));
            if (!ok) Debug.LogError($"FAIL вторая выдача (Бард 9): удвоено {doubled}, ждали 4; "
                                  + "чего не хватает: [" + string.Join(" | ", d.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — до второй выдачи открыты только два навыка (Бард 2)")]
        public void SelfTestExpertiseGrantsAboveLevelDoNotCount()
        {
            // МУТАНТ: «брать сумму всех выдач независимо от уровня». Бард 2 уровня получил бы
            // четыре навыка вместо двух — компетентность 9 уровня работала бы с самого начала.
            //
            // Заодно проверяется ПОРЯДОК отсечения: удваиваются первые два по списку ExpertiseIds
            // («скрытность» и «атлетика»), а не какие придётся. Мутант «отрезать с начала» оставил
            // бы «магию» и «убеждение».
            var rules = BardWithTwoGrants(out var c, 2);
            var d = SheetMath.Compute(c, rules);
            var doubled = d.Skills.Where(s => s.Expertise).Select(s => s.SkillId).ToList();
            bool ok = doubled.Count == 2
                   && doubled.Contains("stealth") && doubled.Contains("athletics");
            if (!ok) Debug.LogError("FAIL выдача выше уровня (Бард 2): удвоены ["
                                  + string.Join(",", doubled) + "], ждали stealth+athletics");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — понижение уровня отнимает лишнюю компетентность, но не стирает её из файла")]
        public void SelfTestLoweringLevelTrimsExpertiseWithoutErasingIt()
        {
            // МУТАНТ: «не отрезать лишнее при чтении». Бард, взявший четыре навыка на 9 уровне и
            // опустившийся до 8-го, показывал бы удвоенный бонус на всех четырёх.
            //
            // ФАЙЛ ПРИ ЭТОМ НЕ ТРОГАЕТСЯ, и это вторая половина правила: ExpertiseIds — плоский
            // список без уровней, и вычистить его при понижении значило бы стереть работу игрока за
            // один промах мимо кнопки. Правило считается ПРИ ЧТЕНИИ — тем же приёмом устроен
            // потолок характеристик.
            var rules = BardWithTwoGrants(out var c, 9);
            LevelPlanOps.LevelDown(c);                     // 9 → 8, вторая выдача закрылась
            var d = SheetMath.Compute(c, rules);
            int doubled = d.Skills.Count(s => s.Expertise);
            bool ok = c.Level == 8 && doubled == 2 && c.ExpertiseIds.Count == 4
                   && !d.Missing.Any(m => m.Contains("Компетентность"));
            if (!ok) Debug.LogError($"FAIL понижение уровня: уровень {c.Level}, удвоено {doubled} "
                                  + $"(ждали 2), в файле {c.ExpertiseIds.Count} (ждали 4); "
                                  + "чего не хватает: [" + string.Join(" | ", d.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — строка «чего не хватает» согласована по числу")]
        public void SelfTestMissingExpertiseLineAgreesInNumber()
        {
            // Мутант — прежняя строка с жёстким «навыках» при любом числе: «выбрана в 1 навыках».
            // Проверяется обе формы разом: «в 1 навыке из 4» и «в 0 навыках из 2».
            var rules = BardWithTwoGrants(out var nine, 9);
            nine.ExpertiseIds.RemoveRange(1, 3);           // остался один
            var one = SheetMath.Compute(nine, rules);

            var rulesTwo = BardWithTwoGrants(out var two, 2);
            two.ExpertiseIds.Clear();                      // не выбрано ни одного
            var none = SheetMath.Compute(two, rulesTwo);

            bool ok = one.Missing.Any(m => m.Contains("выбрана в 1 навыке из 4"))
                   && none.Missing.Any(m => m.Contains("выбрана в 0 навыках из 2"));
            if (!ok) Debug.LogError("FAIL согласование по числу: [" + string.Join(" | ", one.Missing)
                                  + "] / [" + string.Join(" | ", none.Missing) + "]");
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — компетентность не действует ниже своего уровня")]
        public void SelfTestExpertiseIgnoredBelowItsLevel()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].ExpertiseGrants[0].Level = 6;   // персонаж 5 уровня — ещё рано
            var d = SheetMath.Compute(Fixtures.Character(), rules);
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = !st.Expertise && st.Bonus == 5;
            if (!ok) Debug.LogError($"FAIL ранняя компетентность: {st.Bonus} комп={st.Expertise}, ждали 5/нет");
            Done(ok);
        }

        [ContextMenu("Self-Test: навык объясняет себя")]
        public void SelfTestSkillExplainsItself()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var st = d.Skills.First(s => s.SkillId == "stealth");
            bool ok = st.Explain.Contains("+2") && st.Explain.Contains("ловкость")
                   && st.Explain.Contains("компетентность");
            if (!ok) Debug.LogError($"FAIL объяснение навыка: «{st.Explain}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — лёгкий доспех прибавляет всю ловкость")]
        public void SelfTestLightArmorAddsFullDex()
        {
            // Кожаный 11 + ЛОВ +2 = 13.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.ArmorClass == 13;
            if (!ok) Debug.LogError($"FAIL КД лёгкий: {d.ArmorClass}, ждали 13 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — тяжёлый доспех ловкость не прибавляет")]
        public void SelfTestHeavyArmorIgnoresDex()
        {
            // Кольчуга 16, MaxDexBonus 0 → 16, а не 18. Мутант «всегда прибавлять ловкость» падает.
            var c = Fixtures.Character();
            c.Equipment.Remove("leather"); c.Equipment.Add("chain");
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.ArmorClass == 16;
            if (!ok) Debug.LogError($"FAIL КД тяжёлый: {d.ArmorClass}, ждали 16 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — щит прибавляет 2")]
        public void SelfTestShieldAddsTwo()
        {
            var c = Fixtures.Character(); c.Equipment.Add("shield");
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.ArmorClass == 15;
            if (!ok) Debug.LogError($"FAIL КД со щитом: {d.ArmorClass}, ждали 15 ({d.ArmorClassExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: КД — защита без доспехов действует только без доспехов")]
        public void SelfTestUnarmoredDefenseOnlyWithoutArmor()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].UnarmoredDefenseAbility = "con";     // как у Варвара
            var withArmor = SheetMath.Compute(Fixtures.Character(), rules);
            var bare = Fixtures.Character(); bare.Equipment.Remove("leather");
            var without = SheetMath.Compute(bare, rules);
            // В доспехе 13 (доспех побеждает), без доспеха 10 + ЛОВ 2 + ТЕЛ 2 = 14.
            bool ok = withArmor.ArmorClass == 13 && without.ArmorClass == 14;
            if (!ok) Debug.LogError($"FAIL защита без доспехов: в доспехе {withArmor.ArmorClass} (ждали 13), без {without.ArmorClass} (ждали 14)");
            Done(ok);
        }

        [ContextMenu("Self-Test: умения — только по текущий уровень")]
        public void SelfTestFeaturesStopAtCurrentLevel()
        {
            var rules = Fixtures.Rules();
            rules.Classes[0].Levels.First(l => l.Level == 7).Features.Add(
                new FeatureRef { Id = "evasion", Name = "Уклонение", Text = "…" });
            var d = SheetMath.Compute(Fixtures.Character(), rules);   // персонаж 5 уровня
            bool has2 = d.Features.Any(f => f.Id == "cunning");       // уровень 2 — есть
            bool has7 = d.Features.Any(f => f.Id == "evasion");       // уровень 7 — ещё нет
            bool ok = has2 && !has7;
            if (!ok) Debug.LogError($"FAIL умения по уровню: 2-й={has2} (ждали да), 7-й={has7} (ждали нет)");
            Done(ok);
        }

        [ContextMenu("Self-Test: умения — вид, черта происхождения и умения класса вместе")]
        public void SelfTestFeaturesFromAllSources()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.Features.Any(f => f.Id == "lucky" && f.Source == "race")
                   && d.Features.Any(f => f.Id == "alert" && f.Source == "feat")
                   && d.Features.Any(f => f.Id == "cunning" && f.Source == "class");
            if (!ok) Debug.LogError("FAIL источники умений: " + string.Join(", ", d.Features.Select(f => f.Id + "/" + f.Source)));
            Done(ok);
        }

        [ContextMenu("Self-Test: умения подкласса приходят по своему уровню, а не сразу")]
        public void SelfTestSubclassFeaturesFollowLevel()
        {
            // «Быстрые руки» стоят на 3 уровне подкласса. У пятого они есть, у второго нет —
            // мутант «умения подкласса применяются всегда» падает на второй половине.
            var rules = Fixtures.Rules();
            var five = SheetMath.Compute(Fixtures.Character(), rules);
            var young = Fixtures.Character(); young.Level = 2;
            var two = SheetMath.Compute(young, rules);
            bool ok = five.Features.Any(f => f.Id == "fast-hands")
                   && !two.Features.Any(f => f.Id == "fast-hands");
            if (!ok) Debug.LogError($"FAIL умения подкласса: на 5-м={five.Features.Any(f => f.Id == "fast-hands")}, "
                                  + $"на 2-м={two.Features.Any(f => f.Id == "fast-hands")}");
            Done(ok);
        }

        [ContextMenu("Self-Test: неизвестный подкласс показывается")]
        public void SelfTestUnknownSubclassSurfaces()
        {
            var c = Fixtures.Character(); c.SubclassId = "assassin";   // в фикстуре только «вор»
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("assassin"));
            if (!ok) Debug.LogError("FAIL неизвестный подкласс: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: прибавка в несуществующую характеристику не пропадает молча")]
        public void SelfTestUnknownBumpAbilitySurfaces()
        {
            // AbilityScores.Add на неизвестной строке ничего не делает: без этой проверки прибавка
            // просто испарилась бы, а лист выглядел бы правдоподобно.
            var c = Fixtures.Character();
            c.Bumps.Add(new AbilityBump { Source = "background", AbilityId = "luck", Amount = 1 });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("luck"));
            if (!ok) Debug.LogError("FAIL неизвестная характеристика прибавки: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: наполовину разложенные прибавки считаются недоделкой")]
        public void SelfTestPartialBumpsAreIncomplete()
        {
            // Самое частое состояние на выходе из мастера: положил +2 и ушёл. Мутант
            // «хоть одна прибавка есть — значит готово» на этой фикстуре падает.
            var c = Fixtures.Character();
            c.Bumps.RemoveAll(b => b.AbilityId == "con");     // осталось +2, а нужно 3
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("2 из 3"));
            if (!ok) Debug.LogError("FAIL половина прибавок: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: понижение уровня не выбрасывает план")]
        public void SelfTestLoweringLevelKeepsPlan()
        {
            var c = Fixtures.Character();
            c.Plan.Add(new LevelChoice { Level = 8, Kind = "feat", ValueId = "alert" });
            c.Level = 3;
            var d = SheetMath.Compute(c, Fixtures.Rules());
            // План — это план, а не состояние: запись остаётся в файле, умение просто не применено.
            bool ok = c.Plan.Count == 1 && !d.Features.Any(f => f.Level == 8);
            if (!ok) Debug.LogError($"FAIL понижение уровня: записей плана {c.Plan.Count} (ждали 1)");
            Done(ok);
        }

        [ContextMenu("Self-Test: неизвестный Id показывается, а не удаляется")]
        public void SelfTestUnknownIdSurfaces()
        {
            var c = Fixtures.Character(); c.RaceId = "aarakocra";
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.UnknownIds.Any(u => u.Contains("aarakocra")) && c.RaceId == "aarakocra";
            if (!ok) Debug.LogError("FAIL неизвестный Id: " + string.Join("; ", d.UnknownIds));

            // Тот же NoteUnknown отвечает и за класс, и за предысторию — обе ветки были без стража.
            var cCls = Fixtures.Character(); cCls.ClassId = "warlock";
            var dCls = SheetMath.Compute(cCls, Fixtures.Rules());
            bool okCls = dCls.UnknownIds.Any(u => u.Contains("warlock")) && cCls.ClassId == "warlock";
            if (!okCls) Debug.LogError("FAIL неизвестный класс: " + string.Join("; ", dCls.UnknownIds));

            var cBg = Fixtures.Character(); cBg.BackgroundId = "sage";
            var dBg = SheetMath.Compute(cBg, Fixtures.Rules());
            bool okBg = dBg.UnknownIds.Any(u => u.Contains("sage")) && cBg.BackgroundId == "sage";
            if (!okBg) Debug.LogError("FAIL неизвестная предыстория: " + string.Join("; ", dBg.UnknownIds));

            Done(ok && okCls && okBg);
        }

        [ContextMenu("Self-Test: неизвестные навык, предмет и черты показываются")]
        public void SelfTestUnknownSkillItemAndFeatsSurface()
        {
            // Четыре ветки UnknownIds, которых не задевала ни одна самопроверка. «Ничего не
            // удаляется молча» — обещание листа, и оно должно быть под охраной, а не на слове.
            var rules = Fixtures.Rules();
            rules.Backgrounds[0].OriginFeatId = "ghost-origin";     // черты с таким Id нет
            var c = Fixtures.Character();
            c.SkillIds.Add("juggling");                             // навыка нет
            c.Equipment.Add("mithril");                             // предмета нет
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "feat", ValueId = "ghost-feat" });
            var d = SheetMath.Compute(c, rules);
            bool ok = d.UnknownIds.Any(u => u.Contains("juggling"))
                   && d.UnknownIds.Any(u => u.Contains("mithril"))
                   && d.UnknownIds.Any(u => u.Contains("ghost-origin"))
                   && d.UnknownIds.Any(u => u.Contains("ghost-feat"));
            if (!ok) Debug.LogError("FAIL неизвестные навык/предмет/черты: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: у целого персонажа неизвестных Id нет вовсе")]
        public void SelfTestCompleteCharacterHasNoUnknownIds()
        {
            // Страж от ЛОЖНЫХ срабатываний: без него инвертированный предикат («считать
            // неизвестными известные») проходит весь набор зелёным. У Missing такой страж есть,
            // у UnknownIds не было.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.UnknownIds.Count == 0;
            if (!ok) Debug.LogError("FAIL ложные неизвестные Id: " + string.Join("; ", d.UnknownIds));
            Done(ok);
        }

        [ContextMenu("Self-Test: прибавка в характеристику вне списка предыстории — недоделка")]
        public void SelfTestBumpOutsideBackgroundChoicesIsFlagged()
        {
            // Сумма остаётся ровно 3, поэтому сообщение «разложены на N из 3» НЕ должно сработать:
            // так тест ловит именно проверку разрешённых характеристик, а не общую недоделку.
            // Солдат даёт выбор из сил/лов/тел — мудрость не его.
            var c = Fixtures.Character();
            c.Bumps.RemoveAll(b => b.AbilityId == "con");
            c.Bumps.Add(new AbilityBump { Source = "background", AbilityId = "wis", Amount = 1 });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("wis") && m.Contains("Солдат"))
                   && !d.Missing.Any(m => m.Contains("из 3"));
            if (!ok) Debug.LogError("FAIL прибавка вне списка: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: недобранные навыки, компетентность и подкласс перечисляются")]
        public void SelfTestUnfinishedPicksAreListed()
        {
            // Три сообщения Missing, которые не выдавала ни одна самопроверка. Персонаж во всём
            // остальном целый — значит каждое сообщение приходит именно от своей ветки.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character();
            c.SkillIds.Remove("arcana");     // выбран 1 навык из 2
            c.ExpertiseIds.Clear();          // компетентность не выбрана
            c.SubclassId = null;             // подкласс не выбран, а 3 уровень уже пройден
            var d = SheetMath.Compute(c, rules);
            bool ok = d.Missing.Any(m => m.Contains("Навыков выбрано 1 из 2"))
                   && d.Missing.Any(m => m.Contains("омпетентность"))
                   && d.Missing.Any(m => m.Contains("Подкласс не выбран"));
            if (!ok) Debug.LogError("FAIL недобранные выборы: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: подкласс не требуется раньше своего уровня выбора")]
        public void SelfTestMissingSubclassRespectsChoiceLevel()
        {
            // У «Плута» ячейка выбора подкласса — на 3 уровне. Персонаж 2 уровня без подкласса — это
            // НЕ недоделка, он просто ещё не дорос. Мутант «нужен подкласс, если он вообще
            // где-то в классе есть» (без проверки level <= текущий) требует подкласс уже на 1 уровне —
            // никакой другой тест это не ловит, потому что Fixtures.Character() всегда 5 уровня
            // и всегда с проставленным SubclassId.
            var c = Fixtures.Character();
            c.Level = 2;
            c.SubclassId = null;
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = !d.Missing.Any(m => m.Contains("Подкласс"));
            if (!ok) Debug.LogError("FAIL ранний подкласс: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: незаконченный персонаж перечисляет, чего не хватает")]
        public void SelfTestIncompleteCharacterListsGaps()
        {
            var c = new CharacterFile { RulesId = "test", Level = 1 };
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("Имя"))
                   && d.Missing.Any(m => m.Contains("Вид"))
                   && d.Missing.Any(m => m.Contains("Класс"))
                   && d.Missing.Any(m => m.Contains("Предыстория"));
            if (!ok) Debug.LogError("FAIL чего не хватает: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: законченный персонаж ни о чём не просит")]
        public void SelfTestCompleteCharacterHasNoGaps()
        {
            var c = Fixtures.Character();
            // Ячейка повышения на 4 уровне ЗАПОЛНЕНА нарочно. Пятиуровневый плут, ни разу не решивший
            // «прибавки или черта», законченным больше не считается — с планом прокачки эта пустая
            // ячейка стала достижимой и получила свою строку в «чего не хватает». В самой фикстуре
            // пометки нет и быть не должно: на ней держатся проверки таблицы плана, которым нужен
            // именно пустой 4 уровень.
            c.Plan.Add(new LevelChoice { Level = 4, Kind = "asi", ValueId = null });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Count == 0;
            if (!ok) Debug.LogError("FAIL законченный персонаж: " + string.Join("; ", d.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: навыки — уточнение называет характеристику и компетентность")]
        public void SelfTestSkillHintNamesAbilityAndExpertise()
        {
            // Уточнение — это то, что лист пишет мелким справа от числа. Раньше он собирал его сам,
            // и строка расходилась бы с объяснением под тем же числом при первом же переименовании
            // характеристики в справочнике.
            // Мутант №1: `Hint = abilityName` без хвоста → у скрытности пропадает компетентность.
            // Мутант №2: `Hint = skill.Name` → «скрытность» вместо «ловкость».
            // Обе фикстуры нужны разом: у скрытности компетентность ЕСТЬ, у атлетики НЕТ, поэтому
            // мутант «хвост всегда» падает на второй, а «хвоста нет» — на первой.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var stealth = d.Skills.First(s => s.SkillId == "stealth");
            var athletics = d.Skills.First(s => s.SkillId == "athletics");
            bool ok = stealth.Hint == "ловкость · компетентность" && athletics.Hint == "сила";
            if (!ok) Debug.LogError($"FAIL уточнение навыка: скрытность «{stealth.Hint}» "
                                  + $"(ждали «ловкость · компетентность»), атлетика «{athletics.Hint}» (ждали «сила»)");
            Done(ok);
        }

        [ContextMenu("Self-Test: пустая ячейка выбора на взятом уровне названа вслух")]
        public void SelfTestUnfilledChoiceCellIsListed()
        {
            // Стало достижимо вместе с планом прокачки: «поднял уровень, закрыл панель, не выбрав».
            // Три утверждения разом, и каждое валит своего мутанта:
            //   4 уровень (asi, взят, пометки нет)              → строка ЕСТЬ  [мутант «строки нет вовсе»];
            //   8 уровень (asi, не взят)                        → строки НЕТ   [мутант «по всем 20 строкам»];
            //   3 уровень (подкласс, взят, лежит в SubclassId)  → строки НЕТ   [мутант «пометка — единственный
            //     признак заполненности»: он ругался бы на КАЖДОГО персонажа, созданного мастером,
            //     потому что мастер пишет подкласс в поле, а пометки в плане не оставляет].
            var c = Fixtures.Character();          // 5 уровень, ячейки на 3 (подкласс) и 4, 8 (asi)
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.Missing.Any(m => m.Contains("На 4 уровне не выбрано"))
                   && !d.Missing.Any(m => m.Contains("На 8 уровне"))
                   && !d.Missing.Any(m => m.Contains("На 3 уровне"));

            // А заполненная ячейка молчит — иначе строка была бы вечной и её перестали бы читать.
            var filled = Fixtures.Character();
            filled.Plan.Add(new LevelChoice { Level = 4, Kind = "feat", ValueId = "alert" });
            var dFilled = SheetMath.Compute(filled, Fixtures.Rules());
            ok &= !dFilled.Missing.Any(m => m.Contains("На 4 уровне"));

            if (!ok) Debug.LogError("FAIL пустая ячейка выбора: " + string.Join("; ", d.Missing)
                                  + " || заполненная: " + string.Join("; ", dFilled.Missing));
            Done(ok);
        }

        [ContextMenu("Self-Test: подкласс записан, но раньше своего уровня не действует")]
        public void SelfTestActiveSubclassFollowsTheLevel()
        {
            // Понижение уровня стало достижимым вместе с кнопкой «−», и подзаголовок листа читался
            // «полурослик · плут 2 · вор» — подписи, которой на 2 уровне взяться неоткуда.
            // Мутант «действует, раз записан» падает на первой строке; мутант «раз не действует —
            // стереть из файла» падает на последней: выбор игрока чистить нельзя, он вернётся вместе
            // с уровнем.
            var low = Fixtures.Character(); low.Level = 2;      // подкласс у плута с 3 уровня
            var high = Fixtures.Character();                    // 5 уровень
            var none = Fixtures.Character(); none.SubclassId = null;
            var dLow = SheetMath.Compute(low, Fixtures.Rules());
            var dHigh = SheetMath.Compute(high, Fixtures.Rules());
            var dNone = SheetMath.Compute(none, Fixtures.Rules());
            bool ok = dLow.ActiveSubclassId == null
                   && dHigh.ActiveSubclassId == "thief"
                   && dNone.ActiveSubclassId == null
                   && low.SubclassId == "thief";
            if (!ok) Debug.LogError($"FAIL действующий подкласс: на 2 «{dLow.ActiveSubclassId ?? "null"}» (ждали null), "
                                  + $"на 5 «{dHigh.ActiveSubclassId ?? "null"}» (ждали «thief»), "
                                  + $"без выбора «{dNone.ActiveSubclassId ?? "null"}», в файле «{low.SubclassId ?? "null"}»");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
