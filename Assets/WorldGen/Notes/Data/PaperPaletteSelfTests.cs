using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class PaperPaletteSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Лист — нулевой тон белый непрозрачный")]
        public void SelfTestZeroIsOpaqueWhite()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: отсутствующий в файле ключ читается как 0,
            // и только белый непрозрачный воспроизводит сегодняшний вид рисунка.
            var t = PaperPalette.At(0);
            bool ok = t.R == 255 && t.G == 255 && t.B == 255 && t.A == 255;
            if (!ok) Debug.LogError($"FAIL нулевой тон: {t.R},{t.G},{t.B},{t.A}, ожидался 255,255,255,255");
            Done(ok);
        }

        [ContextMenu("Self-Test: Лист — прозрачный тон не чёрный")]
        public void SelfTestTransparentIsNotBlack()
        {
            // Текстура фильтруется билинейно и всегда растянута: чёрный RGB при нулевой прозрачности
            // даёт тёмную кайму вокруг каждой линии. Мутант — записать сюда 0,0,0,0.
            var t = PaperPalette.At(PaperPalette.TransparentIndex);
            bool ok = t.A == 0 && t.R == 255 && t.G == 255 && t.B == 255;
            if (!ok) Debug.LogError($"FAIL прозрачный: {t.R},{t.G},{t.B},{t.A}, ожидался 255,255,255,0");
            Done(ok);
        }

        [ContextMenu("Self-Test: Лист — индекс вне списка не падает")]
        public void SelfTestOutOfRangeFallsBackToWhite()
        {
            var below = PaperPalette.At(-1);
            var above = PaperPalette.At(PaperPalette.Count + 5);
            bool ok = below.A == 255 && below.R == 255 && above.A == 255 && above.R == 255;
            if (!ok) Debug.LogError("FAIL индекс вне списка: ожидался белый, как у NotesPalette.At");
            Done(ok);
        }

        [ContextMenu("Self-Test: Лист — у каждого тона есть имя")]
        public void SelfTestEveryToneHasName()
        {
            bool ok = PaperPalette.Names.Length == PaperPalette.Count;
            if (!ok) Debug.LogError($"FAIL имена: {PaperPalette.Names.Length} на {PaperPalette.Count} тонов");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
