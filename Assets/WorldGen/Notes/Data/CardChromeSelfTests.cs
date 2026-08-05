using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class CardChromeSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Карточка — шапка пропадает только в просмотре без заголовка")]
        public void SelfTestHeaderCollapsesOnlyWhenViewingUntitled()
        {
            // Все четыре сочетания сразу: правило и «шапки нет всегда, когда заголовок пуст»
            // расходятся ровно на второй строке, поэтому проверяются обе.
            bool ok =
                CardChrome.HeaderHeight(hasTitle: false, editable: false) == 0f &&
                CardChrome.HeaderHeight(hasTitle: false, editable: true) == CardChrome.HeaderHeightPx &&
                CardChrome.HeaderHeight(hasTitle: true, editable: false) == CardChrome.HeaderHeightPx &&
                CardChrome.HeaderHeight(hasTitle: true, editable: true) == CardChrome.HeaderHeightPx;
            if (!ok) Debug.LogError(
                $"FAIL шапка: просмотр/без={CardChrome.HeaderHeight(false, false)}, правка/без={CardChrome.HeaderHeight(false, true)}, " +
                $"просмотр/с={CardChrome.HeaderHeight(true, false)}, правка/с={CardChrome.HeaderHeight(true, true)}; " +
                $"ожидалось 0/{CardChrome.HeaderHeightPx}/{CardChrome.HeaderHeightPx}/{CardChrome.HeaderHeightPx}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Карточка — заголовок из пробелов считается пустым")]
        public void SelfTestWhitespaceTitleIsEmpty()
        {
            bool ok = !CardChrome.HasTitle(null) && !CardChrome.HasTitle("") && !CardChrome.HasTitle("   \n")
                      && CardChrome.HasTitle("Засада");
            if (!ok) Debug.LogError("FAIL заголовок: пробельный заголовок должен считаться пустым, а непустой — непустым");
            Done(ok);
        }

        [ContextMenu("Self-Test: Карточка — по умолчанию средний шрифт")]
        public void SelfTestDefaultFontSizeIsMedium()
        {
            // ПИНОК ПОРЯДКУ ПЕРЕЧИСЛЕНИЯ. В старом файле поля нет вовсе, при чтении получается 0 —
            // и ноль обязан означать «Средний», иначе все сохранённые карточки поменяют размер.
            bool ok = default(CardFontSize) == CardFontSize.Medium
                      && CardChrome.BodyPointSize(default) == 12f;
            if (!ok) Debug.LogError($"FAIL умолчание: default(CardFontSize) = {default(CardFontSize)}, кегль {CardChrome.BodyPointSize(default)}, ожидались Medium и 12");
            Done(ok);
        }

        [ContextMenu("Self-Test: Карточка — три кегля различаются")]
        public void SelfTestFontSizesDiffer()
        {
            bool ok = CardChrome.BodyPointSize(CardFontSize.Small) == 10f
                   && CardChrome.BodyPointSize(CardFontSize.Medium) == 12f
                   && CardChrome.BodyPointSize(CardFontSize.Large) == 16f;
            if (!ok) Debug.LogError($"FAIL кегли: {CardChrome.BodyPointSize(CardFontSize.Small)}/{CardChrome.BodyPointSize(CardFontSize.Medium)}/{CardChrome.BodyPointSize(CardFontSize.Large)}, ожидались 10/12/16");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
