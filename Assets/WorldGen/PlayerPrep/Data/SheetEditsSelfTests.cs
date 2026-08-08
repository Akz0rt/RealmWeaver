using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Самопроверки мелких правок на самом листе.</summary>
    public class SheetEditsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: хиты — пустое поле возвращает среднее из справочника")]
        public void SelfTestEmptyTextMeansAverage()
        {
            // Мутант: пустая строка становится нулём (или единицей). Тогда вписанное число нельзя
            // было бы убрать с клавиатуры вовсе — стёртое поле означало бы «максимум хитов ноль».
            // Три вида пустоты, потому что все три приходят с экрана: поля не было, поле стёрли,
            // в поле остался пробел.
            bool ok = !SheetEdits.ParseMaxHpOverride(null).HasValue
                   && !SheetEdits.ParseMaxHpOverride("").HasValue
                   && !SheetEdits.ParseMaxHpOverride("   ").HasValue;
            if (!ok) Debug.LogError($"FAIL хиты: пусто дало {SheetEdits.ParseMaxHpOverride("")}");
            Done(ok);
        }

        [ContextMenu("Self-Test: хиты — вписанное число доходит до файла")]
        public void SelfTestNumberIsKept()
        {
            // Число 44 взято НЕ равным ни MinMaxHp, ни нулю: мутант «возвращать всегда единицу»
            // и мутант «возвращать всегда ноль» здесь оба падают.
            var plain = SheetEdits.ParseMaxHpOverride("44");
            var padded = SheetEdits.ParseMaxHpOverride("  44 ");
            bool ok = plain.HasValue && plain.Value == 44 && padded.HasValue && padded.Value == 44;
            if (!ok) Debug.LogError($"FAIL хиты: «44» дало {plain}, «  44 » дало {padded}");
            Done(ok);
        }

        [ContextMenu("Self-Test: хиты — не число тоже значит «среднее»")]
        public void SelfTestNonsenseMeansAverage()
        {
            // Мутант: неразобранный текст превращается в 0 (типичное `int.TryParse(...); return v;`,
            // где v после неудачи равно нулю). Такой лист показал бы персонажа с нулём хитов, а
            // объяснение под числом сказало бы «вписан вручную» — про число, которого никто не писал.
            bool ok = !SheetEdits.ParseMaxHpOverride("сорок").HasValue
                   && !SheetEdits.ParseMaxHpOverride("12абв").HasValue;
            if (!ok) Debug.LogError($"FAIL хиты: «сорок» дало {SheetEdits.ParseMaxHpOverride("сорок")}, "
                                  + $"«12абв» дало {SheetEdits.ParseMaxHpOverride("12абв")}");
            Done(ok);
        }

        [ContextMenu("Self-Test: хиты — ноль и минус поднимаются до единицы")]
        public void SelfTestBelowOneIsRaised()
        {
            // Мутант: убрать прижатие и вернуть разобранное как есть. Ноль отличается от пустоты
            // намеренно — «0» это НЕ «считай среднее», это попытка вписать невозможное число, и
            // молча подменять её средним было бы враньём в другую сторону.
            var zero = SheetEdits.ParseMaxHpOverride("0");
            var negative = SheetEdits.ParseMaxHpOverride("-5");
            bool ok = zero.HasValue && zero.Value == SheetEdits.MinMaxHp
                   && negative.HasValue && negative.Value == SheetEdits.MinMaxHp;
            if (!ok) Debug.LogError($"FAIL хиты: «0» дало {zero}, «-5» дало {negative} (ждали {SheetEdits.MinMaxHp})");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
