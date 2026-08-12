using UnityEngine;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Чем красить горы. Отдельно от геометрии нарочно: манера рисования будет меняться (сейчас тон
    /// по глубине, дальше боковой свет, штриховка, другие вершины), а математика — нет.
    ///
    /// Концы тональной шкалы берутся ИЗ ПАЛИТРЫ КАРТЫ, а не из чисел прототипа. В прототипе они
    /// подобраны под тёмный холст браузера и на карте читались бы как чужое тело; в палитре под горы
    /// уже есть своя пара слотов — светлый и теневой.
    /// </summary>
    public struct MountainPaintStyle
    {
        /// <summary>Цвет самой ДАЛЬНЕЙ горы массива — светлый конец шкалы.</summary>
        public Color32 Far;

        /// <summary>Цвет самой ближней — тёмный конец.</summary>
        public Color32 Near;

        /// <summary>Линия гребня. Обводится только он: дуга подошвы прочерчивала бы соседа поперёк
        /// и массив рассыпался бы в чешую.</summary>
        public Color32 Ink;

        /// <summary>Толщина линии гребня в мировых единицах.</summary>
        public float CrestWidth;

        /// <summary>Высота слоя над плоскостью карты.</summary>
        public float LayerY;

        /// <summary>Стиль по палитре карты: MtnL — светлый горный тон, MtnS — теневой, снег — блик
        /// на гребне (полупрозрачный, чтобы не спорить с заливкой).</summary>
        public static MountainPaintStyle FromPalette(MapPaletteTheme theme, float crestWidth, float layerY,
                                                     byte crestAlpha = 90)
        {
            var ink = MapPalette.GetSlotColor(theme, PaletteSlot.Snow);
            ink.a = crestAlpha;
            return new MountainPaintStyle
            {
                Far = MapPalette.GetSlotColor(theme, PaletteSlot.MtnL),
                Near = MapPalette.GetSlotColor(theme, PaletteSlot.MtnS),
                Ink = ink,
                CrestWidth = crestWidth,
                LayerY = layerY,
            };
        }
    }
}
