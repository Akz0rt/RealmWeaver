using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation
{
    /// <summary>Тип принудительного переопределения water-статуса клетки.</summary>
    public enum WaterOverrideType
    {
        None,       // не переопределено - используется статус из corner-графа
        ForceLand,  // принудительно суша (убрать озеро/осушить)
        ForceLake,  // принудительно озеро
        ForceOcean  // принудительно океан
    }

    /// <summary>
    /// Базовая единица карты - одна ячейка Voronoi-диаграммы.
    /// Чистый C# класс без зависимостей от UnityEngine, чтобы его можно было
    /// тестировать и пересчитывать вне Play Mode (например, в фоновом потоке).
    /// </summary>
    public class VoronoiCell
    {
        public int Id;

        /// <summary>Исходная точка-семя (центр клетки до/после релаксации).</summary>
        public Vector2 Site;

        /// <summary>Вершины полигона клетки по периметру, по порядку (CW или CCW - см. примечание в VoronoiBuilder).</summary>
        public List<Vector2> Polygon = new List<Vector2>();

        /// <summary>Id соседних клеток (через общее ребро Delaunay/Voronoi).</summary>
        public List<int> NeighborIds = new List<int>();

        /// <summary>Высота (elevation) в диапазоне [0, 1]. В Patel-системе: 0 = побережье, 1 = горы.</summary>
        public float Height;

        /// <summary>
        /// true - клетка относится к внешнему океану (за пределами материка).
        /// false при Height ниже уровня моря означает внутренний водоём (озеро) на самом материке.
        /// </summary>
        public bool IsOcean;

        /// <summary>Id региона (области), к которому принадлежит клетка. -1 = ещё не назначено.</summary>
        public int RegionId = -1;

        /// <summary>Температура в диапазоне [0, 1]. 0 = очень холодно, 1 = очень жарко.</summary>
        public float Temperature;

        /// <summary>Влажность в диапазоне [0, 1]. 0 = очень сухо, 1 = максимально влажно.</summary>
        public float Humidity;

        // ---- Climate override ----

        /// <summary>Ручной override температуры. null = использовать вычисленное Temperature.</summary>
        public float? TemperatureOverride;

        /// <summary>Ручной override влажности. null = использовать вычисленное Humidity.</summary>
        public float? MoistureOverride;

        /// <summary>Эффективная температура с учётом override.</summary>
        public float EffectiveTemperature => TemperatureOverride ?? Temperature;

        /// <summary>Эффективная влажность с учётом override.</summary>
        public float EffectiveMoisture => MoistureOverride ?? Humidity;

        // ---- Landscape override ----

        /// <summary>
        /// Ручной override elevation [0,1]. null = использовать вычисленное Height.
        /// Влияет на биом-классификацию (пляж/горы/снег) и на охлаждение температуры с высотой.
        /// </summary>
        public float? ElevationOverride;

        /// <summary>
        /// Ручной override water-статуса. None = не переопределено, используется статус из corner-графа.
        /// ForceLand - принудительно суша (убрать озеро/осушить).
        /// ForceLake - принудительно озеро.
        /// ForceOcean - принудительно океан.
        /// </summary>
        public WaterOverrideType WaterOverride = WaterOverrideType.None;

        /// <summary>Эффективный elevation с учётом override - используется для биом-классификации и рендера Height-mode.</summary>
        public float EffectiveElevation => ElevationOverride ?? Height;

        /// <summary>Форма рельефа (равнина/холмы/горы/вершины), производная от EffectiveElevation.
        /// Не сериализуется — вычисляется на лету. См. spec §5 (пока «только хранить»).</summary>
        public Landform Landform => LandformClassifier.Of(EffectiveElevation);

        /// <summary>Эффективный IsOcean с учётом WaterOverride.</summary>
        public bool EffectiveIsOcean =>
            WaterOverride == WaterOverrideType.ForceOcean ||
            (WaterOverride == WaterOverrideType.None && IsOcean);

        /// <summary>Эффективный IsLake с учётом WaterOverride. Озеро = вода, но не океан.</summary>
        public bool EffectiveIsLake =>
            WaterOverride == WaterOverrideType.ForceLake ||
            (WaterOverride == WaterOverrideType.None && !IsOcean && Biome == Biome.Lake);

        /// <summary>Итоговый биом, всегда производный от EffectiveTemperature/EffectiveMoisture/EffectiveElevation. Пересчитывается в cell.Biome вызовом CellOverrideService.RecomputeBiome.</summary>
        public Biome Biome;

        /// <summary>Parameterless constructor for Newtonsoft.Json deserialization (ProjectSerializer) — fields are populated by the serializer afterward.</summary>
        public VoronoiCell() { }

        public VoronoiCell(int id, Vector2 site)
        {
            Id = id;
            Site = site;
        }
    }
}
