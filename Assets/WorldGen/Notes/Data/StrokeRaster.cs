using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>Печать мазков в растр.
    ///
    /// ОТДАЁТ byte[] В ПОРЯДКЕ RGBA32, А НЕ Color32[]: Color32 — это UnityEngine, которого в чистом
    /// слое нет (та же причина, по которой существует PaletteColor). Побочная выгода на стороне
    /// отрисовки — Texture2D.LoadRawTextureData принимает такой массив без единого преобразования.
    /// Именно эта подпись и делает утверждение «линия рвётся» проверяемым офлайн.
    ///
    /// Y РАСТЁТ ВВЕРХ: строка 0 массива — низ рисунка, как у SetPixel и как ждёт
    /// LoadRawTextureData. Ни одного переворота по Y здесь быть не должно.</summary>
    public static class StrokeRaster
    {
        public const int MinSize = 256;
        public const int MaxSize = 1024;

        /// <summary>Шаг штамповки вдоль отрезка — доля РАДИУСА. Половина, а не радиус целиком: при
        /// шаге в радиус соседние круги лишь касаются, и по краям линии появляется гребёнка.</summary>
        public const float StepOfRadius = 0.5f;

        /// <summary>Разрешение считается из размера рисунка в единицах доски и зажимается.
        /// Растянутый вчетверо рисунок получает потолок и остаётся чётким; рисунок во весь экран не
        /// просит текстуру, которой не рад никто.
        ///
        /// Зависит ТОЛЬКО от размера объекта, но не от масштаба доски — это делает разрешение
        /// устойчивым и воспроизводимым, и это же причина ограничения «чёткость при увеличении
        /// доски не чинится».</summary>
        public static void ChooseSize(float sizeX, float sizeY, out int w, out int h)
        {
            if (sizeX <= 0.0001f || sizeY <= 0.0001f) { w = MinSize; h = MinSize; return; }

            // Пропорция берётся из объекта и сохраняется, иначе вытянутый рисунок печатался бы в
            // квадрат и терял разрешение по длинной стороне.
            float aspect = sizeY / sizeX;
            w = Mathf.Clamp(Mathf.RoundToInt(sizeX), MinSize, MaxSize);
            h = Mathf.Clamp(Mathf.RoundToInt(w * aspect), MinSize, MaxSize);
        }

        public static byte[] NewPaper(int w, int h, PaperTone paper)
        {
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = paper.R; rgba[i + 1] = paper.G; rgba[i + 2] = paper.B; rgba[i + 3] = paper.A;
            }
            return rgba;
        }

        public static byte[] Bake(IReadOnlyList<Stroke> strokes, int w, int h, PaperTone paper)
        {
            var rgba = NewPaper(w, h, paper);
            if (strokes == null) return rgba;
            // По порядку и только по порядку: ластик это такой же мазок, и «стёр, потом нарисовал»
            // не равно «нарисовал, потом стёр».
            foreach (var s in strokes)
                if (s != null) StampStroke(rgba, w, h, s, paper);
            return rgba;
        }

        public static void StampStroke(byte[] rgba, int w, int h, Stroke stroke, PaperTone paper)
        {
            if (stroke?.Points == null || stroke.Points.Count == 0) return;

            byte r, g, b, a;
            if (stroke.IsEraser)
            {
                // ОДНО ПРАВИЛО: ластик возвращает лист к ЕГО цвету, какой бы он ни был. Ни жёстко
                // прошитой прозрачности, ни жёстко прошитого белого — иначе на каждый тон листа
                // появлялся бы частный случай.
                r = paper.R; g = paper.G; b = paper.B; a = paper.A;
            }
            else
            {
                var c = NotesPalette.At(stroke.InkIndex);
                r = c.R; g = c.G; b = c.B; a = 255;
            }

            if (stroke.Points.Count == 1)
            {
                StampDisc(rgba, w, h, stroke.Points[0], r, g, b, a);
                return;
            }
            for (int i = 1; i < stroke.Points.Count; i++)
                StampSegment(rgba, w, h, stroke.Points[i - 1], stroke.Points[i], r, g, b, a);
        }

        /// <summary>Диски вдоль отрезка. ЭТО И ЕСТЬ ПОЧИНКА «ЛИНИЯ РВЁТСЯ»: сегодня диск ставится
        /// только там, где курсор оказался в момент кадра, и всё между кадрами остаётся голым.
        /// Толщина вдоль отрезка меняется линейно от точки к точке.</summary>
        static void StampSegment(byte[] rgba, int w, int h, StrokePoint a0, StrokePoint b0,
                                 byte r, byte g, byte b, byte a)
        {
            float dxPx = (b0.X - a0.X) * w;
            float dyPx = (b0.Y - a0.Y) * h;
            float lengthPx = Mathf.Sqrt(dxPx * dxPx + dyPx * dyPx);

            float minRadiusPx = Mathf.Max(0.5f, Mathf.Min(a0.W, b0.W) * 0.5f * w);
            float stepPx = Mathf.Max(0.5f, minRadiusPx * StepOfRadius);
            int steps = Mathf.Max(1, Mathf.CeilToInt(lengthPx / stepPx));

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                StampDisc(rgba, w, h,
                          new StrokePoint(Mathf.Lerp(a0.X, b0.X, t),
                                          Mathf.Lerp(a0.Y, b0.Y, t),
                                          Mathf.Lerp(a0.W, b0.W, t)),
                          r, g, b, a);
            }
        }

        /// <summary>Залитый круг без смешивания — байты просто записываются. Мягкие края в этот
        /// спек не входят (ДМ их не выбрал), и запись без смешивания это то, что позволяет ластику
        /// класть прозрачность поверх краски.</summary>
        static void StampDisc(byte[] rgba, int w, int h, StrokePoint p, byte r, byte g, byte b, byte a)
        {
            // Радиус по ШИРИНЕ, как и хранится: при разном растяжении по осям круг станет овалом,
            // и считать по одной оси честнее, чем делать вид, что учтены обе.
            int radius = Mathf.Max(1, Mathf.RoundToInt(p.W * 0.5f * w));
            int cx = Mathf.RoundToInt(p.X * w);
            int cy = Mathf.RoundToInt(p.Y * h);

            for (int y = -radius; y <= radius; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= h) continue;
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int px = cx + x;
                    if (px < 0 || px >= w) continue;
                    int i = (py * w + px) * 4;
                    rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
                }
            }
        }
    }
}
