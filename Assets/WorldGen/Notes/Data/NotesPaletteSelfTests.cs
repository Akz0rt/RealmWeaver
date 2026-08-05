using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>Тесты палитры доски. Каждый метод — отдельный SelfTest*, потому что харнесс
    /// вызывает только public void без параметров, а всё остальное с таким именем валит прогон.</summary>
    public class NotesPaletteSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Палитра — насыщенный зелёный требует ТЁМНОГО текста")]
        public void SelfTestGreenPrefersDarkText()
        {
            // ФИКСТУРА ВЫБРАНА ТАК, ЧТОБЫ ПРАВИЛО И ПОДДЕЛКА РАСХОДИЛИСЬ. Чистый зелёный: взвешенная
            // яркость 0.715 (глаз видит зелёный ярче всего) → тёмный текст; простое среднее (0+255+0)/3
            // = 0.333 → светлый. Тест ловит и подмену формулы средним, и перевёрнутый порог.
            var green = new PaletteColor(0, 255, 0);
            bool ok = NotesPalette.PrefersDarkText(green);
            if (!ok) Debug.LogError("FAIL зелёный: ожидался ТЁМНЫЙ текст на насыщенном зелёном");
            Done(ok);
        }

        [ContextMenu("Self-Test: Палитра — синий требует СВЕТЛОГО текста")]
        public void SelfTestBluePrefersLightText()
        {
            var blue = new PaletteColor(0, 0, 255);
            bool ok = !NotesPalette.PrefersDarkText(blue);
            if (!ok) Debug.LogError("FAIL синий: ожидался СВЕТЛЫЙ текст на синем");
            Done(ok);
        }

        [ContextMenu("Self-Test: Палитра — индекс вне списка даёт нейтральный")]
        public void SelfTestIndexOutOfRangeIsNeutral()
        {
            var neutral = NotesPalette.At(0);
            var below = NotesPalette.At(-1);
            var above = NotesPalette.At(NotesPalette.Count + 5);
            bool ok = Same(below, neutral) && Same(above, neutral);
            if (!ok) Debug.LogError($"FAIL индекс: -1 дал {Show(below)}, {NotesPalette.Count + 5} дал {Show(above)}, ожидался нейтральный {Show(neutral)}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Палитра — девять цветов и столько же названий")]
        public void SelfTestPaletteShape()
        {
            bool ok = NotesPalette.Count == 9 && NotesPalette.Names.Length == NotesPalette.Count;
            if (!ok) Debug.LogError($"FAIL размер: {NotesPalette.Count} цветов и {NotesPalette.Names.Length} названий, ожидалось 9 и 9");
            Done(ok);
        }

        [ContextMenu("Self-Test: Палитра — чернильный тёмный и требует светлого текста")]
        public void SelfTestInkIsDark()
        {
            // Карандаш по умолчанию рисует ИМЕННО этим цветом; если он окажется светлым, набросок
            // на белом растре пропадёт, а тест на яркость это заметит раньше ДМ.
            var ink = NotesPalette.At(NotesPalette.InkIndex);
            bool ok = NotesPalette.Luminance(ink) < 0.25f && !NotesPalette.PrefersDarkText(ink);
            if (!ok) Debug.LogError($"FAIL чернильный: яркость {NotesPalette.Luminance(ink)}, ожидалась меньше 0.25");
            Done(ok);
        }

        static bool Same(PaletteColor a, PaletteColor b) => a.R == b.R && a.G == b.G && a.B == b.B;
        static string Show(PaletteColor c) => $"({c.R},{c.G},{c.B})";

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
