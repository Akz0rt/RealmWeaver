using UnityEngine;
using WorldGen.Generation.Mountains;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Чем красить горы. Отдельно от геометрии нарочно: манера рисования будет меняться (сейчас
    /// цвет по ярусу, дальше боковой свет, штриховка, другие вершины), а математика — нет.
    ///
    /// Концы шкалы берутся ИЗ ПАЛИТРЫ КАРТЫ, а не из чисел прототипа. В прототипе они подобраны под
    /// тёмный холст браузера и на карте читались бы как чужое тело; в палитре под горы уже есть своя
    /// пара слотов — светлый и теневой. У прототипа в режиме «подсветить ярусы» краски и вовсе
    /// разноцветные (бирюза, янтарь, лосось) — это отладочная подсветка, а не манера рисования.
    /// </summary>
    public struct MountainPaintStyle
    {
        /// <summary>Светлый конец шкалы — внешний ярус массы.</summary>
        public Color32 Far;

        /// <summary>Тёмный конец — сердцевина.</summary>
        public Color32 Near;

        /// <summary>Сколько ярусов различаем. Столько же и красок на шкале.</summary>
        public int TierCount;

        /// <summary>Насколько ярусы расходятся по шкале: 1 — во всю (ядро тёмное), 0 — все ярусы
        /// одного цвета, −1 — наоборот, ядро светлое.</summary>
        public float TierContrast;

        /// <summary>Заливка яруса. Единственное место, где ярус превращается в цвет.</summary>
        public Color32 Fill(int tier)
            => Color32.Lerp(Far, Near, MountainTierRamp.Mix(tier, TierCount, TierContrast));

        /// <summary>Линия гребня. Обводится только он: дуга подошвы прочерчивала бы соседа поперёк
        /// и массив рассыпался бы в чешую.</summary>
        public Color32 Ink;

        /// <summary>Толщина линии гребня в мировых единицах.</summary>
        public float CrestWidth;

        /// <summary>Высота слоя над плоскостью карты.</summary>
        public float LayerY;

        /// <summary>Стиль по палитре карты: MtnL — светлый горный тон, MtnS — теневой, снег — блик
        /// на гребне (полупрозрачный, чтобы не спорить с заливкой). Средний ярус берётся с середины
        /// этой пары: третьего слота под горы в палитре нет, и заводить его до того, как ДМ увидит
        /// механическую середину, — гадание.</summary>
        public static MountainPaintStyle FromPalette(MapPaletteTheme theme, float crestWidth, float layerY,
                                                     int tierCount, float tierContrast, byte crestAlpha = 90)
        {
            var ink = MapPalette.GetSlotColor(theme, PaletteSlot.Snow);
            ink.a = crestAlpha;
            return new MountainPaintStyle
            {
                Far = MapPalette.GetSlotColor(theme, PaletteSlot.MtnL),
                Near = MapPalette.GetSlotColor(theme, PaletteSlot.MtnS),
                TierCount = tierCount,
                TierContrast = tierContrast,
                Ink = ink,
                CrestWidth = crestWidth,
                LayerY = layerY,
            };
        }
    }
}
