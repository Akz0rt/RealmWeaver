using System;
using UnityEngine;
using WorldGen.Generation.Mountains;
using WorldGen.Rendering.MapRaster;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Чем красить горы — манера «тушь» (§13 спеки).
    ///
    /// Объём держит ЛИНИЯ, а не цвет. Тело заливается цветом карты ПОД НИМ, поэтому освещённый склон
    /// остаётся нетронутым куском карты и гора вырастает из неё без шва; наружу выходит только
    /// силуэт. Прежняя раскладка «ярус → цвет по шкале» ушла вместе с манерой заливки: цветов на
    /// образце ДМ нет вовсе.
    ///
    /// Градация по слоям массы никуда не делась — она переехала из цвета в ГУСТОТУ: внутренние слои
    /// получают жирнее линию и плотнее крошку. Требование ДМ «внешний / средний / внутренний должны
    /// различаться» выполняется, просто на другом языке.
    ///
    /// Ни одного поля, которое ничего не делает: мёртвая настройка, которую видно, хуже
    /// отсутствующей — по ней уводят ползунок в край, не понимая, отчего ничего не меняется.
    /// </summary>
    public struct MountainPaintStyle
    {
        /// <summary>Цвет земли под точкой. null — карты нет (стенд, шип): тело красится ЗапаснойЦвет.</summary>
        public Func<Vec2, Color32> Ground;

        /// <summary>Чем крыть тело, если снимка карты нет.</summary>
        public Color32 FallbackBody;

        /// <summary>Краска туши.</summary>
        public Color32 Ink;

        /// <summary>Чернота: доля непрозрачности линии, крошки и промоин.</summary>
        public float PenAlpha;

        /// <summary>Жирность линии у вершины, в мировых единицах.</summary>
        public float PenWidth;

        /// <summary>Сход линии на нет книзу: толщина = жирность·(1 − r)^сход. У ближнего края r = 1,
        /// толщина ноль — подошва не обводится сама собой.</summary>
        public float PenTaper;

        /// <summary>Крошка в тени: сколько меток на единицу площади подошвы, в долях R².</summary>
        public float Grit;

        /// <summary>Насколько быстро крошка редеет вниз по склону.</summary>
        public float GritFall;

        /// <summary>Промоины: доля лучей освещённой стороны, вдоль которых идёт штрих.</summary>
        public float Gully;

        /// <summary>Откуда светит, в градусах. Одно направление на всю карту: разное освещение у
        /// соседних гор рассыпает картинку.</summary>
        public float LightAngle;

        /// <summary>Насколько слой массы меняет густоту туши.</summary>
        public float TierInk;

        /// <summary>Сколько слоёв массы различаем и насколько они расходятся.</summary>
        public int TierCount;
        public float TierContrast;

        /// <summary>Глубина: дальние горы бледнее. Гасит тушь ПОСЛЕДНИМ слоем, поэтому со слоями
        /// массы не спорит. Считается от МИРОВОЙ координаты, а не от размаха нарисованного — иначе
        /// новый хребет на другом конце карты молча перекрасил бы все прежние.</summary>
        public float DepthTone;

        /// <summary>Высота карты — мера для глубины.</summary>
        public float MapHeight;

        /// <summary>Высота слоя над плоскостью карты.</summary>
        public float LayerY;

        /// <summary>Единичный вектор света в Site-координатах.</summary>
        public Vec2 Light
        {
            get
            {
                float a = LightAngle * (float)Math.PI / 180f;
                return new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
            }
        }

        /// <summary>Густота туши для слоя массы: множит и толщину линии, и плотность крошки.</summary>
        public float Density(int tier) => MountainInk.Density(tier, TierCount, TierContrast, TierInk);

        /// <summary>Гашение по глубине. depth — Y ближайшей точки подошвы в мировых единицах.</summary>
        public float Haze(float depth) => MountainInk.Haze(depth, MapHeight, DepthTone);

        /// <summary>Краска туши с учётом слоя массы и глубины.</summary>
        public Color32 InkAt(int tier, float depth, float scale = 1f)
        {
            float a = PenAlpha * Haze(depth) * scale;
            if (a < 0f) a = 0f; else if (a > 1f) a = 1f;
            return new Color32(Ink.r, Ink.g, Ink.b, (byte)(a * 255f + 0.5f));
        }

        /// <summary>Стиль по палитре карты: тушь берётся от теневого горного тона, притемнённого до
        /// почти чёрного, — так линия сидит в теме карты, а не приклеена поверх неё чужой краской.
        /// Запасной цвет тела — светлый горный тон: он нужен только там, где карты нет вовсе.</summary>
        public static MountainPaintStyle FromPalette(MapPaletteTheme theme)
        {
            Color32 shade = MapPalette.GetSlotColor(theme, PaletteSlot.MtnS);
            const float k = 0.35f;
            return new MountainPaintStyle
            {
                FallbackBody = MapPalette.GetSlotColor(theme, PaletteSlot.MtnL),
                Ink = new Color32((byte)(shade.r * k), (byte)(shade.g * k), (byte)(shade.b * k), 255),
            };
        }
    }
}
