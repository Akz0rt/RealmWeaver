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
    /// Объём держит ЛИНИЯ, а не цвет. Тело карту не красит СОВСЕМ: оно кладётся прозрачным и только
    /// пишет глубину, чтобы линия дальней горы отсекалась телом ближней. Поэтому сквозь гору видна
    /// ровно та карта, что нарисована, — со сглаживанием, зерном и виньеткой, и никакого шва по краю
    /// быть не может в принципе. Прежняя раскладка «ярус → цвет по шкале» ушла вместе с манерой
    /// заливки: цветов на образце ДМ нет вовсе.
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
        /// <summary>Краска туши. Единственная краска слоя: тело карту не красит вовсе.</summary>
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

        /// <summary>
        /// Краска туши по умолчанию — ПОЧТИ ЧЁРНАЯ, и от палитры карты она не зависит.
        ///
        /// Первая редакция брала теневой горный тон и притемняла его втрое. На тёплых палитрах это
        /// давало правдоподобную сепию, а на снежной — бледно-серую линию: тон там сам по себе
        /// светлый, и треть от светлого остаётся светлой. ДМ увидел горы, обведённые почти
        /// невидимой линией. Тушь — это тушь: у неё своя чернота, а не производная от карты.
        /// </summary>
        public static Color32 DefaultInk => new Color32(26, 22, 18, 255);
    }
}
