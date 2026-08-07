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

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
