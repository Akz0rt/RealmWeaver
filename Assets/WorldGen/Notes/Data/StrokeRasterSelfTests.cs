using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class StrokeRasterSelfTests : MonoBehaviour
    {
        const int W = 128, H = 128;
        static readonly PaperTone White = PaperPalette.At(PaperPalette.WhiteIndex);

        static Stroke Ink(params StrokePoint[] pts)
            => new Stroke { InkIndex = NotesPalette.InkIndex, Points = new List<StrokePoint>(pts) };

        /// <summary>Пиксель считается закрашенным, если он отличается от бумаги.</summary>
        static bool Painted(byte[] rgba, int x, int y)
        {
            int i = (y * W + x) * 4;
            return !(rgba[i] == White.R && rgba[i + 1] == White.G && rgba[i + 2] == White.B);
        }

        [ContextMenu("Self-Test: Растр — далёкие точки дают сплошную линию")]
        public void SelfTestFarPointsAreConnected()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И СЕГОДНЯШНЕЕ ПОВЕДЕНИЕ: две точки в разных концах рисунка
            // при тонкой кисти. Штамповка «круг только в самих точках» оставит между ними голую
            // бумагу; соединение отрезком не оставит ни одного неокрашенного пикселя.
            var rgba = StrokeRaster.Bake(
                new[] { Ink(new StrokePoint(0.1f, 0.5f, 0.02f), new StrokePoint(0.9f, 0.5f, 0.02f)) },
                W, H, White);

            int gaps = 0;
            for (int x = (int)(0.1f * W) + 2; x < (int)(0.9f * W) - 2; x++)
                if (!Painted(rgba, x, H / 2)) gaps++;

            bool ok = gaps == 0;
            if (!ok) Debug.LogError($"FAIL разрыв: {gaps} неокрашенных пикселей между двумя точками");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — толстая кисть на быстром движении не оставляет зазоров")]
        public void SelfTestThickBrushHasNoGaps()
        {
            // Отдельная проверка от предыдущей: шаг штамповки задаётся в долях РАДИУСА, и слишком
            // крупный шаг ломается именно на толстой кисти, где радиус большой. Мутант — шаг,
            // равный радиусу вместо половины: соседние круги коснутся, но по краям линии появится
            // гребёнка, и вертикальный срез через край даст дыры.
            var rgba = StrokeRaster.Bake(
                new[] { Ink(new StrokePoint(0.1f, 0.5f, 0.15f), new StrokePoint(0.9f, 0.5f, 0.15f)) },
                W, H, White);

            int radiusPx = (int)(0.15f * 0.5f * W);
            int edgeRow = H / 2 - radiusPx;   // строка у самого края полосы
            int gaps = 0;
            for (int x = (int)(0.2f * W); x < (int)(0.8f * W); x++)
                if (!Painted(rgba, x, edgeRow)) gaps++;

            bool ok = gaps == 0;
            if (!ok) Debug.LogError($"FAIL гребёнка по краю толстой линии: {gaps} дыр");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — ластик возвращает цвет листа")]
        public void SelfTestEraserRestoresPaper()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И СЕГОДНЯШНЕЕ ПОВЕДЕНИЕ: лист белый непрозрачный. Правило
            // «вернуть лист» даёт непрозрачный белый; сегодняшнее «писать прозрачный» даст A = 0.
            var strokes = new List<Stroke>
            {
                Ink(new StrokePoint(0.5f, 0.5f, 0.2f)),
                new Stroke { IsEraser = true, Points = { new StrokePoint(0.5f, 0.5f, 0.3f) } },
            };
            var rgba = StrokeRaster.Bake(strokes, W, H, White);
            int i = ((H / 2) * W + W / 2) * 4;

            bool ok = rgba[i] == 255 && rgba[i + 1] == 255 && rgba[i + 2] == 255 && rgba[i + 3] == 255;
            if (!ok) Debug.LogError($"FAIL ластик: {rgba[i]},{rgba[i+1]},{rgba[i+2]},{rgba[i+3]}, ожидался белый непрозрачный");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — ластик на прозрачном листе стирает в прозрачность")]
        public void SelfTestEraserFollowsTransparentPaper()
        {
            var transparent = PaperPalette.At(PaperPalette.TransparentIndex);
            var strokes = new List<Stroke>
            {
                Ink(new StrokePoint(0.5f, 0.5f, 0.2f)),
                new Stroke { IsEraser = true, Points = { new StrokePoint(0.5f, 0.5f, 0.3f) } },
            };
            var rgba = StrokeRaster.Bake(strokes, W, H, transparent);
            int i = ((H / 2) * W + W / 2) * 4;

            // Одно правило на оба листа: ластик возвращает лист к ЕГО цвету, а не к жёстко
            // прошитой прозрачности и не к жёстко прошитому белому.
            bool ok = rgba[i + 3] == 0;
            if (!ok) Debug.LogError($"FAIL ластик на прозрачном листе: прозрачность {rgba[i+3]}, ожидался 0");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — порядок мазков важен")]
        public void SelfTestOrderMatters()
        {
            // Стереть, а потом нарисовать — не то же самое, что нарисовать, а потом стереть.
            var strokes = new List<Stroke>
            {
                new Stroke { IsEraser = true, Points = { new StrokePoint(0.5f, 0.5f, 0.3f) } },
                Ink(new StrokePoint(0.5f, 0.5f, 0.2f)),
            };
            var rgba = StrokeRaster.Bake(strokes, W, H, White);
            int i = ((H / 2) * W + W / 2) * 4;

            bool ok = Painted(rgba, W / 2, H / 2);
            if (!ok) Debug.LogError("FAIL порядок: мазок после ластика обязан быть виден");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — разрешение растёт с размером и упирается в потолок")]
        public void SelfTestResolutionGrowsAndIsCapped()
        {
            StrokeRaster.ChooseSize(256f, 256f, out int w1, out int h1);
            StrokeRaster.ChooseSize(1024f, 1024f, out int w2, out int h2);
            StrokeRaster.ChooseSize(8000f, 8000f, out int w3, out int h3);
            StrokeRaster.ChooseSize(64f, 64f, out int w4, out int h4);

            bool ok = w1 == 256 && h1 == 256
                   && w2 == 1024 && h2 == 1024
                   && w3 == StrokeRaster.MaxSize && h3 == StrokeRaster.MaxSize
                   && w4 == StrokeRaster.MinSize && h4 == StrokeRaster.MinSize;
            if (!ok) Debug.LogError($"FAIL разрешение: {w1}/{w2}/{w3}/{w4}, ожидались 256/1024/потолок/пол");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — вытянутый рисунок не становится квадратом")]
        public void SelfTestAspectIsKept()
        {
            StrokeRaster.ChooseSize(1024f, 256f, out int w, out int h);
            bool ok = w > h;
            if (!ok) Debug.LogError($"FAIL пропорция: {w}×{h} у рисунка 1024×256");
            Done(ok);
        }

        [ContextMenu("Self-Test: Растр — низ рисунка это Y = 0")]
        public void SelfTestYZeroIsBottom()
        {
            // Переворот по Y уже стоил этому проекту отдельного разбора на боевой сетке. Точка у
            // самого низа обязана попасть в НАЧАЛО массива, как этого ждёт LoadRawTextureData.
            var rgba = StrokeRaster.Bake(
                new[] { Ink(new StrokePoint(0.5f, 0.05f, 0.05f)) }, W, H, White);
            bool ok = Painted(rgba, W / 2, 6) && !Painted(rgba, W / 2, H - 6);
            if (!ok) Debug.LogError("FAIL ось Y: точка у низа рисунка оказалась не в начале массива");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
