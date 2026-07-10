using System;

namespace WorldGen.Generation
{
    /// <summary>Форма рельефа клетки по высоте — «разновидность» биома (spec §5).
    /// Производная от EffectiveElevation; НЕ сериализуется (всегда восстановима).
    /// Пока хранится/вычисляется для декораций и будущего использования (без UI/оттенка).</summary>
    public enum Landform { Plain = 0, Hills = 1, Mountains = 2, Peaks = 3 } // равнина/холмы/горы/вершины

    public static class LandformClassifier
    {
        /// <summary>4 равные полосы по [0,1]: 0=Plain, 1=Hills, 2=Mountains, 3=Peaks (1.0 → Peaks).</summary>
        public static Landform Of(float elevation) => (Landform)Math.Clamp((int)(elevation * 4f), 0, 3);
    }
}
