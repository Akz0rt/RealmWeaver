using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Точечная зона аномальной влажности на карте - аналог TemperatureEpicenter.
    /// Используется как АДДИТИВНАЯ поправка к distance-based moisture (не замена) -
    /// сохраняет географическую логику "рядом с озером влажно", добавляя сверху
    /// творческий контроль (заколдованное болото, проклятая сухая пустошь и т.п.).
    /// </summary>
    public class MoistureEpicenter
    {
        public Vector2 Position;

        /// <summary>Поправка к влажности в диапазоне [-1, 1]. Положительное - увеличивает влажность, отрицательное - аномальная сушь.</summary>
        public float MoistureDelta;

        /// <summary>Радиус влияния в единицах карты. За пределами радиуса эпицентр не влияет на клетку вообще (hard cutoff).</summary>
        public float Radius;

        public MoistureEpicenter(Vector2 position, float moistureDelta, float radius)
        {
            Position = position;
            MoistureDelta = moistureDelta;
            Radius = radius;
        }
    }
}
