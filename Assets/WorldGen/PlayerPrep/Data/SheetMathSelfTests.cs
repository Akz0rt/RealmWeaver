using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class SheetMathSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: лист — прибавки складываются с базой")]
        public void SelfTestBumpsAddToBase()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            // СИЛ 8+2=10, ТЕЛ 14+1=15, ЛОВ 15 без прибавки.
            bool ok = d.Total.Str == 10 && d.Total.Con == 15 && d.Total.Dex == 15;
            if (!ok) Debug.LogError($"FAIL прибавки: СИЛ {d.Total.Str} (ждали 10), ТЕЛ {d.Total.Con} (ждали 15), ЛОВ {d.Total.Dex} (ждали 15)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — модификатор считается от суммы, а не от базы")]
        public void SelfTestModifierUsesTotalNotBase()
        {
            // Расхождение: база СИЛ 8 даёт −1, сумма 10 даёт 0. Мутант «модификатор от Base» падает.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.Modifiers.Str == 0 && d.Modifiers.Dex == 2 && d.Modifiers.Con == 2;
            if (!ok) Debug.LogError($"FAIL модификаторы: СИЛ {d.Modifiers.Str} (ждали 0), ЛОВ {d.Modifiers.Dex} (ждали 2), ТЕЛ {d.Modifiers.Con} (ждали 2)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — мастерство на 5 уровне равно +3")]
        public void SelfTestProficiencyAtLevelFive()
        {
            // ИМЕННО 5 УРОВЕНЬ: на первом мутанты «всегда +2» и «2 + (уровень−1)/4» неразличимы.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.ProficiencyBonus == 3;
            if (!ok) Debug.LogError($"FAIL мастерство: {d.ProficiencyBonus}, ждали 3");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — мастерство растёт на 5, 9, 13, 17")]
        public void SelfTestProficiencyBreakpoints()
        {
            var rules = Fixtures.Rules();
            var expected = new[] { (1, 2), (4, 2), (5, 3), (8, 3), (9, 4), (12, 4), (13, 5), (16, 5), (17, 6), (20, 6) };
            bool ok = true;
            foreach (var (level, bonus) in expected)
            {
                var c = Fixtures.Character(); c.Level = level;
                int got = SheetMath.Compute(c, rules).ProficiencyBonus;
                if (got != bonus) { ok = false; Debug.LogError($"FAIL мастерство на {level} уровне: {got}, ждали {bonus}"); }
            }
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — владение спасбросками применяется выборочно")]
        public void SelfTestSaveProficiencyIsSelective()
        {
            // Расхождение №2: «лов» во владении, «тел» нет, а модификаторы у них ОДИНАКОВЫЕ (+2).
            // Значит числа отличаются ровно на мастерство: +5 против +2. Мутанты «мастерство ко всем»
            // и «ни к одному» падают оба.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var dex = d.Saves.First(s => s.AbilityId == "dex");
            var con = d.Saves.First(s => s.AbilityId == "con");
            bool ok = dex.Proficient && dex.Bonus == 5 && !con.Proficient && con.Bonus == 2;
            if (!ok) Debug.LogError($"FAIL спасброски: ЛОВ {dex.Bonus} влад={dex.Proficient} (ждали 5/да), ТЕЛ {con.Bonus} влад={con.Proficient} (ждали 2/нет)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — спасбросок объясняет себя")]
        public void SelfTestSaveExplainsItself()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            var dex = d.Saves.First(s => s.AbilityId == "dex");
            bool ok = dex.Explain.Contains("+2") && dex.Explain.Contains("+3");
            if (!ok) Debug.LogError($"FAIL объяснение спасброска: «{dex.Explain}», ждали упоминания +2 и +3");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — максимум хитов по среднему")]
        public void SelfTestMaxHpAverage()
        {
            // d8, 5 уровень, ТЕЛ +2: первый уровень 8, дальше четыре раза по 5 → 8+20=28, плюс
            // 5×2 за телосложение = 38. Мутант «средний и на первом уровне» даёт 35 — расходится.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.MaxHp == 38;
            if (!ok) Debug.LogError($"FAIL хиты: {d.MaxHp}, ждали 38 ({d.MaxHpExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — вписанный максимум хитов побеждает средний")]
        public void SelfTestMaxHpOverrideWins()
        {
            var c = Fixtures.Character(); c.MaxHpOverride = 44;
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.MaxHp == 44 && d.MaxHpExplain.Contains("вписан");
            if (!ok) Debug.LogError($"FAIL вписанные хиты: {d.MaxHp} «{d.MaxHpExplain}», ждали 44 и пометку");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — инициатива и скорость")]
        public void SelfTestInitiativeAndSpeed()
        {
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.Initiative == 2 && d.Speed == 30 && d.HitDie == "d8";
            if (!ok) Debug.LogError($"FAIL инициатива/скорость: {d.Initiative}/{d.Speed}/{d.HitDie}, ждали 2/30/d8");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — модификатор округляется ВНИЗ, а не к нулю")]
        public void SelfTestModifierFloorsDownOnOddLowScores()
        {
            // Целочисленное деление в C# усекает К НУЛЮ: (7-10)/2 даёт −1, а верный ответ −2.
            // Расходятся правило и подделка ТОЛЬКО на нечётных значениях ниже 10, поэтому фикстуре
            // нужна именно такая характеристика — в основной её нет, и мутант жил незамеченным.
            var rules = Fixtures.Rules();
            var c = Fixtures.Character(); c.Base.Wis = 7;    // мудрость прибавок не получает
            bool ok = SheetMath.Compute(c, rules).Modifiers.Wis == -2;
            var c9 = Fixtures.Character(); c9.Base.Wis = 9;  // (9-10)/2 усечением даёт 0, верно −1
            ok &= SheetMath.Compute(c9, rules).Modifiers.Wis == -1;
            if (!ok) Debug.LogError($"FAIL округление вниз: МДР 7 дало {SheetMath.Compute(c, rules).Modifiers.Wis} (ждали −2), "
                                  + $"МДР 9 дало {SheetMath.Compute(c9, rules).Modifiers.Wis} (ждали −1)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — хитов не бывает меньше одного")]
        public void SelfTestMaxHpNeverDropsBelowOne()
        {
            // Кость d4 и телосложение 1 (модификатор −5) на 5 уровне дают −9 без нижней границы.
            // У основной фикстуры телосложение положительное, поэтому граница не проверялась ничем.
            var rules = Fixtures.Rules();
            rules.Classes[0].HitDie = "d4";
            var c = Fixtures.Character();
            c.Bumps.RemoveAll(b => b.AbilityId == "con");
            c.Base.Con = 1;
            var d = SheetMath.Compute(c, rules);
            bool ok = d.MaxHp == 1;
            if (!ok) Debug.LogError($"FAIL нижняя граница хитов: {d.MaxHp}, ждали 1 ({d.MaxHpExplain})");
            Done(ok);
        }

        [ContextMenu("Self-Test: уровень вне 1–20 прижимается к границе")]
        public void SelfTestLevelIsClampedToOneTwenty()
        {
            // Мастерство клэмпится у себя внутри и мутанта не ловит — ловят ХИТЫ.
            // d8, ТЕЛ +2: на 20 уровне 8 + 5×19 + 2×20 = 143; на 1-м 8 + 2 = 10.
            var rules = Fixtures.Rules();
            var over = Fixtures.Character(); over.Level = 25;
            var under = Fixtures.Character(); under.Level = 0;
            int hi = SheetMath.Compute(over, rules).MaxHp;
            int lo = SheetMath.Compute(under, rules).MaxHp;
            bool ok = hi == 143 && lo == 10;
            if (!ok) Debug.LogError($"FAIL прижатие уровня: 25-й дал {hi} (ждали 143 как у 20-го), "
                                  + $"0-й дал {lo} (ждали 10 как у 1-го)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — название характеристики берётся по идентификатору")]
        public void SelfTestAbilityNameMapsAllSix()
        {
            // Сверяются ВСЕ ШЕСТЬ пар и по порядку. Мутант «сдвиг на единицу» назвал бы «лов»
            // Телосложением, а проверка одной пары его бы пропустила; мутант «вернуть сам
            // идентификатор» дал бы «dex» в мастере, где по-русски написано всё остальное.
            var got = SheetMath.AbilityOrder().Select(SheetMath.AbilityName).ToList();
            bool ok = got.SequenceEqual(new[]
                { "Сила", "Ловкость", "Телосложение", "Интеллект", "Мудрость", "Харизма" });
            if (!ok) Debug.LogError("FAIL названия характеристик: " + string.Join(",", got));
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — неизвестная характеристика называется своим идентификатором")]
        public void SelfTestAbilityNameEchoesUnknownId()
        {
            // Мутант: `AbilityNames[i]` без проверки i < 0 — падение вместо честной строки. Мастер
            // зовёт это на идентификаторах ИЗ СПРАВОЧНИКА (прибавки предыстории), где опечатка
            // возможна, и уронить показ она не должна.
            bool ok = SheetMath.AbilityName("bogus") == "bogus" && SheetMath.AbilityName(null) == null;
            if (!ok) Debug.LogError($"FAIL неизвестная характеристика: «{SheetMath.AbilityName("bogus")}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — порядок характеристик отдаётся копией")]
        public void SelfTestAbilityOrderIsAFreshCopy()
        {
            // Мутант: отдавать сам массив (или один общий список). Раскладка мастера полученный
            // список ПЕРЕСТАВЛЯЕТ (SuggestedAssignment), и один общий она перемешала бы всем сразу —
            // лист начал бы показывать характеристики в порядке чужого класса.
            var first = SheetMath.AbilityOrder();
            bool order = first.SequenceEqual(new[] { "str", "dex", "con", "int", "wis", "cha" });
            first.Reverse();
            first.Add("bogus");
            var second = SheetMath.AbilityOrder();
            bool ok = order && second.SequenceEqual(new[] { "str", "dex", "con", "int", "wis", "cha" });
            if (!ok) Debug.LogError("FAIL порядок характеристик: первый вызов "
                                  + (order ? "верен" : "неверен") + ", второй = " + string.Join(",", second));
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — характеристика объясняет себя базой, стрелкой и прибавкой")]
        public void SelfTestAbilityExplainShowsBaseArrowTotal()
        {
            // Строка сверяется ЦЕЛИКОМ, потому что самопроверка и есть её единственная спецификация:
            // ни харнесс, ни компиляция не видят, что написано у игрока на экране.
            // Мутанты, которых валит именно пара «с прибавкой + без прибавки»:
            //   «прибавки потеряны»          → «Сила 8 → 8»;
            //   «база берётся из суммы»      → «Сила 10 → 12 (+2)»;
            //   «скобки печатаются всегда»   → «Мудрость 10 → 10 (+0)».
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            string str = d.AbilityExplain.TryGetValue("str", out var s) ? s : "(нет)";
            string wis = d.AbilityExplain.TryGetValue("wis", out var w) ? w : "(нет)";
            bool ok = str == "Сила 8 → 10 (+2)" && wis == "Мудрость 10 → 10";
            // Мутант «объяснять только те характеристики, у которых есть прибавка» не виден ни на
            // одной отдельной строке — виден только на полноте набора.
            var missing = SheetMath.AbilityOrder().Where(id => !d.AbilityExplain.ContainsKey(id)).ToList();
            ok &= missing.Count == 0;
            if (!ok) Debug.LogError($"FAIL объяснение характеристики: СИЛ «{str}» (ждали «Сила 8 → 10 (+2)»), "
                                  + $"МДР «{wis}» (ждали «Мудрость 10 → 10»), без объяснения: "
                                  + (missing.Count == 0 ? "нет" : string.Join(",", missing)));
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — отрицательная прибавка показывается со знаком минус")]
        public void SelfTestAbilityExplainKeepsNegativeBump()
        {
            // Мутант «печатать плюс всегда» даёт «Мудрость 10 → 8 (+-2)» либо «(+2)» — и то и другое
            // враньё. В основной фикстуре отрицательных прибавок нет, поэтому мутант в ней невидим.
            var c = Fixtures.Character();
            c.Bumps.Add(new AbilityBump { Source = "level:4", AbilityId = "wis", Amount = -2 });
            var d = SheetMath.Compute(c, Fixtures.Rules());
            bool ok = d.AbilityExplain["wis"] == "Мудрость 10 → 8 (-2)";
            if (!ok) Debug.LogError($"FAIL отрицательная прибавка: «{d.AbilityExplain["wis"]}», ждали «Мудрость 10 → 8 (-2)»");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — нулевой бонус пишется со знаком плюс")]
        public void SelfTestSignedKeepsPlusOnZero()
        {
            // Мутант «ноль печатается без знака» (v > 0 вместо v >= 0) невидим во всех остальных
            // проверках: в фикстуре нулевой бонус попадается только у силы, чьё объяснение целиком
            // никто не сверяет. А на листе «0» рядом с «+2» читается как «здесь ничего не посчитано».
            bool ok = SheetMath.Signed(0) == "+0" && SheetMath.Signed(3) == "+3" && SheetMath.Signed(-1) == "-1";
            if (!ok) Debug.LogError($"FAIL знак бонуса: 0 → «{SheetMath.Signed(0)}» (ждали «+0»), "
                                  + $"3 → «{SheetMath.Signed(3)}», −1 → «{SheetMath.Signed(-1)}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — мастерство объясняет себя уровнем")]
        public void SelfTestProficiencyExplainNamesTheLevel()
        {
            // ФИКСТУРА 5 УРОВНЯ, а не 1: на первом «+2 на 1 уровне» получается и у верной формулы, и
            // у мутанта «всегда +2 на 1 уровне», и проверка была бы вакуозной.
            var rules = Fixtures.Rules();
            var d = SheetMath.Compute(Fixtures.Character(), rules);
            const string want = "+3 на 5 уровне (мастерство растёт на 5, 9, 13 и 17)";
            bool ok = d.ProficiencyExplain == want;

            // Второй мутант: объяснение берёт file.Level, а бонус — прижатый уровень. Тогда число и
            // подпись расходятся ровно на листах с испорченным уровнем — там, где проверить некому.
            var over = Fixtures.Character(); over.Level = 25;
            var d25 = SheetMath.Compute(over, rules);
            const string want25 = "+6 на 20 уровне (мастерство растёт на 5, 9, 13 и 17)";
            ok &= d25.ProficiencyExplain == want25;

            if (!ok) Debug.LogError($"FAIL объяснение мастерства: «{d.ProficiencyExplain}» (ждали «{want}»), "
                                  + $"на 25 уровне «{d25.ProficiencyExplain}» (ждали «{want25}»)");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — инициатива объясняет себя ловкостью")]
        public void SelfTestInitiativeExplainNamesDexterity()
        {
            // Слово в строке важно не меньше числа: мутант «инициатива от силы» в этой фикстуре даёт
            // «+0 сила», а мутант «взять число ловкости, но назвать другую характеристику» — «+2 сила».
            // Проверка «содержит +2» пропустила бы второго.
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.InitiativeExplain == "+2 ловкость";
            if (!ok) Debug.LogError($"FAIL объяснение инициативы: «{d.InitiativeExplain}», ждали «+2 ловкость»");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — скорость объясняет себя видом")]
        public void SelfTestSpeedExplainNamesTheRace()
        {
            // Три мутанта: «30 зашито числом» (падает на второй фикстуре с 25), «названо не то, откуда
            // скорость» — класс вместо вида (даёт «вид «Плут»»), «у неизвестного вида молчим»
            // (пустая строка под нулём читается как «персонаж не ходит»).
            var d = SheetMath.Compute(Fixtures.Character(), Fixtures.Rules());
            bool ok = d.SpeedExplain == "30 футов, вид «Полурослик»";

            var slow = Fixtures.Rules(); slow.Races[0].Speed = 25;
            var dSlow = SheetMath.Compute(Fixtures.Character(), slow);
            ok &= dSlow.SpeedExplain == "25 футов, вид «Полурослик»";

            var noRace = Fixtures.Character(); noRace.RaceId = null;
            var dNone = SheetMath.Compute(noRace, Fixtures.Rules());
            ok &= dNone.Speed == 0 && dNone.SpeedExplain == "вид не выбран — скорость приходит от него";

            if (!ok) Debug.LogError($"FAIL объяснение скорости: «{d.SpeedExplain}» (ждали «30 футов, вид «Полурослик»»), "
                                  + $"на 25 футах «{dSlow.SpeedExplain}», без вида «{dNone.SpeedExplain}»");
            Done(ok);
        }

        [ContextMenu("Self-Test: лист — объяснение характеристики не требует справочника")]
        public void SelfTestExplainAbilityWorksOnHalfBuiltCharacter()
        {
            // Мастер зовёт ExplainAbility на шаге характеристик, когда ни вида, ни класса ещё нет —
            // и до сих пор звал СВОЮ копию правила. Мутант «считать через Compute» уронил бы мастер
            // на полупустом персонаже либо потребовал справочника, которого на том шаге может не быть.
            var half = new CharacterFile { Base = new AbilityScores { Dex = 15 } };
            half.Bumps.Add(new AbilityBump { Source = "background", AbilityId = "dex", Amount = 2 });
            bool ok = SheetMath.ExplainAbility(half, "dex") == "Ловкость 15 → 17 (+2)"
                   && SheetMath.ExplainAbility(half, "cha") == "Харизма 0 → 0"
                   && SheetMath.ExplainAbility(null, "dex") == "Ловкость";
            if (!ok) Debug.LogError($"FAIL объяснение без справочника: «{SheetMath.ExplainAbility(half, "dex")}», "
                                  + $"«{SheetMath.ExplainAbility(half, "cha")}», «{SheetMath.ExplainAbility(null, "dex")}»");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
