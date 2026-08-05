using UnityEngine;

namespace WorldGen.Notes.Data
{
    public enum BrushWidth { Medium = 0, Thin = 1, Thick = 2 }

    /// <summary>Толщина карандаша. ЗАДАЁТСЯ В ЕДИНИЦАХ ДОСКИ, А НЕ В ПИКСЕЛЯХ РАСТРА, потому что
    /// рисунок растягивается за угол, а растр внутри остаётся 256×256: «тонко» на растянутом вдвое
    /// рисунке рисовало бы вдвое толще, чем показывает кружок в панели.</summary>
    public static class BrushOps
    {
        public static float DiameterInCanvasUnits(BrushWidth width)
        {
            switch (width)
            {
                case BrushWidth.Thin: return 2f;
                case BrushWidth.Thick: return 10f;
                default: return 5f;
            }
        }

        /// <summary>Радиус в пикселях растра, дающий на экране заданную толщину.
        ///
        /// Считается по ШИРИНЕ, а не по обеим сторонам: PaintAt ставит круг в координатах растра, и
        /// при разном растяжении по осям он всё равно станет овалом — считать по одной оси честнее,
        /// чем делать вид, что учтены обе.
        ///
        /// Нижняя граница 0.5 не косметика: PaintAt округляет радиус, и на сильно растянутом рисунке
        /// кисть перестала бы рисовать вовсе.</summary>
        public static float RadiusInPixels(float diameterCanvasUnits, float objectWidth, int texturePixelWidth)
        {
            if (texturePixelWidth <= 0) return 1f;
            // Ширина объекта приходит из файла проекта, поэтому проверяется, а не предполагается.
            if (objectWidth <= 0.0001f) objectWidth = texturePixelWidth;
            float radius = diameterCanvasUnits * 0.5f * texturePixelWidth / objectWidth;
            return Mathf.Clamp(radius, 0.5f, texturePixelWidth * 0.5f);
        }
    }
}
