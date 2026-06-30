using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>
    /// Точечный "источник" температуры на карте - альтернатива классической широте.
    /// Несколько эпицентров с разными значениями температуры создают плавное
    /// температурное поле через inverse distance weighting (см. TemperatureField).
    /// </summary>
    public class TemperatureEpicenter
    {
        public Vector2 Position;

        /// <summary>Температура в диапазоне [0, 1]. 0 = очень холодно, 1 = очень жарко.</summary>
        public float Temperature;

        /// <summary>Радиус влияния в единицах карты. За пределами радиуса эпицентр не влияет на клетку вообще (hard cutoff).</summary>
        public float Radius;

        public TemperatureEpicenter(Vector2 position, float temperature, float radius)
        {
            Position = position;
            Temperature = temperature;
            Radius = radius;
        }
    }
}
