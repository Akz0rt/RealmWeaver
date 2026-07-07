using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace WorldGen.Generation
{
    public class GenerationParams
    {
        public int Seed = 1337;
        public float Width = 500f;
        public float Height = 500f;
        public float MinPointDistance = 15f;   // влияет на "зерно" карты - меньше = больше клеток
        public int LloydRelaxIterations = 2;
        public int NumberOfRegions = 6;

        // --- Island shape (land/water на corners, ДО elevation - порядок шагов как у Patel) ---
        public float HeightFrequency = 0.01f;
        public int HeightOctaves = 4;
        public float WarpAmplitude = 40f;

        /// <summary>
        /// Степень резкости спада к краю карты в island-shape функции. Больше значение = более
        /// выраженное "плато" в центре карты с более резким спадом только у самого края
        /// (материк занимает больше площади). Разумный диапазон - 1.5 (рваные берега) до 4
        /// (почти прямоугольный материк).
        /// </summary>
        public float FalloffPower = 2.5f;

        /// <summary>
        /// Доля расстояния от центра карты (в [0,1]), внутри которой материк гарантированно
        /// не топится falloff'ом вообще. Без этого (=0) falloff растёт от центра сразу, и при
        /// разумных FalloffPower вода может занимать неожиданно большую долю карты. Стандартное
        /// значение 0.5 даёт сбалансированное соотношение материк/океан.
        /// </summary>
        public float InnerRadius = 0.5f;

        /// <summary>Порог island-shape функции, ниже которого corner считается водой. [0, 1].</summary>
        public float SeaLevel = 0.35f;

        /// <summary>
        /// Минимальный размер связной группы corners, чтобы остаться озером. Группы меньшего
        /// размера "осушаются" (становятся сушей) - прямой контроль над количеством мелких
        /// озёр на карте, независимый от seaLevel/формы шума. Поставь больше значение, если
        /// хочешь меньше озёр; 0 или 1 отключает фильтрацию вовсе.
        /// </summary>
        public int MinLakeSize = 25;

        // --- Elevation (гибрид distance-from-coast + шум, Patel-стиль) ---

        /// <summary>Вес компонента "расстояние от берега" в итоговой elevation.</summary>
        public float ElevationCoastWeight = 0.6f;
        /// <summary>Вес компонента "локальный шум" в итоговой elevation - позволяет горам появляться рядом с побережьем.</summary>
        public float ElevationNoiseWeight = 0.4f;
        public float ElevationNoiseFrequency = 0.015f;
        public int ElevationNoiseOctaves = 4;

        // --- Moisture ---

        /// <summary>
        /// Дистанция в шагах corner-графа от свежей воды (озёр) для полного высыхания.
        /// ВАЖНО: при малом значении (например 6) большинство клеток на карте обычного
        /// размера (несколько тысяч corners) могут оказаться ВНЕ радиуса любого озера и
        /// получить одинаковое сырое значение moisture=0 - после redistribution это даёт
        /// избыток пустынь, потому что redistribution не может содержательно отсортировать
        /// клетки с одинаковым исходным значением. Разумный порядок величины - 15-25 для
        /// карты с несколькими тысячами клеток и умеренным количеством озёр; подбирать по
        /// фактической плотности озёр на конкретной карте.
        /// </summary>
        public float MoistureFalloffDistance = 20f;

        // --- Биом ---
        public float BeachElevationThreshold = 0.1f;

        // --- Температура (отдельная point-based система эпицентров, не из Patel) ---

        public int NumberOfTemperatureEpicenters = 3;
        public float EpicenterMinRadius = 150f;
        public float EpicenterMaxRadius = 300f;
        public float BaseTemperature = 0.5f;
        /// <summary>Насколько сильно elevation (после Patel-расчёта) охлаждает клетку.</summary>
        public float HeightCoolingFactor = 0.6f;

        // --- Влажность: point-based эпицентры (аддитивная поправка к distance-based moisture) ---

        public int NumberOfMoistureEpicenters = 3;
        public float MoistureEpicenterMinRadius = 150f;
        public float MoistureEpicenterMaxRadius = 300f;
        /// <summary>Диапазон случайной поправки к влажности каждого эпицентра. Положительное - влажная зона, отрицательное - аномально сухая.</summary>
        public float MoistureEpicenterMinDelta = -0.5f;
        public float MoistureEpicenterMaxDelta = 0.5f;

        // --- Реки ---

        /// <summary>
        /// Включить трассировку и влияние рек на moisture. Отключено по умолчанию - текущий
        /// рендер рек (прямые линии по corner-to-corner рёбрам) выглядел визуально неестественно/
        /// зигзагообразно без дополнительного сглаживания (у Patel для этого есть отдельный шаг
        /// "noisy edges", который мы не реализовывали). Логика трассировки и влияния на moisture
        /// остаётся в коде - можно включить заново и поработать над рендером отдельно.
        /// </summary>
        public bool EnableRivers = false;
        public int NumberOfRivers = 20;
        /// <summary>Минимальная elevation (после redistribution) для стартовой точки реки - реки начинаются только в достаточно высоких местах.</summary>
        public float RiverMinStartElevation = 0.6f;
        /// <summary>Защита от бесконечного цикла трассировки одной реки (на случай плато/локальных минимумов без явного стока).</summary>
        public int RiverMaxSteps = 1000;
    }

    /// <summary>
    /// Оркестратор полного Patel-style пайплайна генерации карты:
    /// точки -> Voronoi -> релаксация -> corner-граф -> island shape (land/water на corners) ->
    /// flood fill (ocean/lake) -> фильтр мелких озёр -> перенос water-статуса на клетки ->
    /// elevation (distance+noise гибрид) -> redistribution elevation -> moisture (distance от
    /// свежей воды + аддитивные point-based эпицентры, БЕЗ redistribution) -> усреднение на
    /// клетки -> биом (Whittaker 4x6) -> регионы -> температура (point-based эпицентры).
    /// </summary>
    public static class WorldGenerator
    {
        public static List<VoronoiCell> GenerateWorld(GenerationParams p, out List<TemperatureEpicenter> temperatureEpicenters, out List<MoistureEpicenter> moistureEpicenters, out List<River> rivers)
        {
            var points = PoissonDiskSampling.Generate(p.Width, p.Height, p.MinPointDistance, p.Seed);
            var cells = VoronoiBuilder.Build(points, p.Width, p.Height);

            for (int i = 0; i < p.LloydRelaxIterations; i++)
            {
                var relaxedPoints = LloydRelaxation.ComputeRelaxedPoints(cells);
                cells = VoronoiBuilder.Build(relaxedPoints, p.Width, p.Height);
            }

            // --- Corner-граф: фундамент всей Patel-системы ---
            var corners = CornerGraphBuilder.Build(cells);

            // --- Island shape: land/water на corners, ДО elevation ---
            var islandShapeGen = new HeightmapGenerator(p.Seed, p.Width, p.Height, p.HeightFrequency, p.HeightOctaves,
                                                          p.WarpAmplitude, falloffPower: p.FalloffPower, innerRadius: p.InnerRadius);
            IslandShapeAssigner.AssignWaterCorners(corners, islandShapeGen, p.SeaLevel);

            // --- Ocean/lake flood fill на corners ---
            CornerOceanFloodFill.MarkOcean(corners, p.Width, p.Height);

            // --- Фильтрация мелких озёр: прямой контроль количества озёр, независимый от seaLevel/шума ---
            if (p.MinLakeSize > 1)
                LakeSizeFilter.RemoveSmallLakes(corners, p.MinLakeSize);

            // --- Перенос water-статуса с corners на клетки ---
            CellWaterAssigner.AssignFromCorners(cells, corners);

            // --- Elevation: гибрид distance-from-coast + шум, затем redistribution ---
            ElevationField.ApplyElevation(corners, p.ElevationCoastWeight, p.ElevationNoiseWeight,
                                            p.Seed, p.ElevationNoiseFrequency, p.ElevationNoiseOctaves);
            ValueRedistributor.RedistributeElevation(corners);

            // --- Реки: трассировка от случайных высоких точек по downhill до океана/озера (отключено по умолчанию - см. EnableRivers) ---
            rivers = p.EnableRivers
                ? RiverTracer.TraceRivers(corners, p.NumberOfRivers, p.Seed, p.RiverMinStartElevation, p.RiverMaxSteps)
                : new List<River>();
            var riverCornerIds = RiverFlowAccumulator.GetRiverCornerIds(rivers);

            // --- Moisture: distance от свежей воды (озёра + реки) + аддитивные эпицентры ---
            moistureEpicenters = GenerateRandomMoistureEpicenters(p);
            MoistureField.ApplyMoisture(corners, p.MoistureFalloffDistance, moistureEpicenters, riverCornerIds);

            // --- Усреднение elevation/moisture с corners на клетки + классификация биома ---
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold);

            // --- Регионы (растим только по суше, как и раньше) ---
            var landCells = cells.Where(c => !c.IsOcean).ToList();
            if (landCells.Count >= p.NumberOfRegions)
                RegionGrowing.GroupCells(cells, landCells, p.NumberOfRegions, p.Seed);

            // --- Унификация озёр: весь связный водоём → один регион (голосование по соседним сушным клеткам) ---
            LakeRegionUnifier.UnifyLakes(cells);

            // --- Температура: отдельная point-based система эпицентров ---
            temperatureEpicenters = GenerateRandomEpicenters(p);
            RegenerateTemperature(cells, p, temperatureEpicenters);

            return cells;
        }

        /// <summary>
        /// Same pipeline as GenerateWorld, split into 5 of 6 progress-reportable stages for the
        /// Generation Progress screen (the 6th, "Отрисовка карты", is owned by MapScreenController
        /// after this coroutine completes - see WorldMapRenderer.RebakeAllStepped). Temperature is
        /// computed right after moisture here
        /// (rather than at the very end, as in GenerateWorld) so the reported step order
        /// matches the UI checklist -- safe because BiomeClassifier only reads elevation and
        /// moisture (see CellClimateAverager.cs:49), and region growing never used temperature
        /// either. GenerateWorld itself is untouched, kept for self-tests/back-compat.
        /// </summary>
        public static IEnumerator GenerateWorldStepped(
            GenerationParams p,
            Action<string, float> onProgress,
            Action<List<VoronoiCell>, List<TemperatureEpicenter>, List<MoistureEpicenter>, List<River>> onComplete)
        {
            // --- Step 1/6: Генерация высот ---
            onProgress?.Invoke("Генерация высот", 0f / 6f);
            var points = PoissonDiskSampling.Generate(p.Width, p.Height, p.MinPointDistance, p.Seed);
            var cells = VoronoiBuilder.Build(points, p.Width, p.Height);

            for (int i = 0; i < p.LloydRelaxIterations; i++)
            {
                var relaxedPoints = LloydRelaxation.ComputeRelaxedPoints(cells);
                cells = VoronoiBuilder.Build(relaxedPoints, p.Width, p.Height);
            }

            var corners = CornerGraphBuilder.Build(cells);

            var islandShapeGen = new HeightmapGenerator(p.Seed, p.Width, p.Height, p.HeightFrequency, p.HeightOctaves,
                                                          p.WarpAmplitude, falloffPower: p.FalloffPower, innerRadius: p.InnerRadius);
            IslandShapeAssigner.AssignWaterCorners(corners, islandShapeGen, p.SeaLevel);
            yield return null;

            // --- Step 2/6: Океаны и озёра ---
            onProgress?.Invoke("Океаны и озёра", 1f / 6f);
            CornerOceanFloodFill.MarkOcean(corners, p.Width, p.Height);
            if (p.MinLakeSize > 1)
                LakeSizeFilter.RemoveSmallLakes(corners, p.MinLakeSize);
            CellWaterAssigner.AssignFromCorners(cells, corners);

            ElevationField.ApplyElevation(corners, p.ElevationCoastWeight, p.ElevationNoiseWeight,
                                            p.Seed, p.ElevationNoiseFrequency, p.ElevationNoiseOctaves);
            ValueRedistributor.RedistributeElevation(corners);
            yield return null;

            // --- Step 3/6: Температура и влажность ---
            onProgress?.Invoke("Температура и влажность", 2f / 6f);
            var rivers = p.EnableRivers
                ? RiverTracer.TraceRivers(corners, p.NumberOfRivers, p.Seed, p.RiverMinStartElevation, p.RiverMaxSteps)
                : new List<River>();
            var riverCornerIds = RiverFlowAccumulator.GetRiverCornerIds(rivers);

            var moistureEpicenters = GenerateRandomMoistureEpicenters(p);
            MoistureField.ApplyMoisture(corners, p.MoistureFalloffDistance, moistureEpicenters, riverCornerIds);

            // Temperature moved up from its original end-of-pipeline position in GenerateWorld --
            // safe reordering, see method doc comment above.
            var temperatureEpicenters = GenerateRandomEpicenters(p);
            yield return null;

            // --- Step 4/6: Расчёт биомов ---
            onProgress?.Invoke("Расчёт биомов", 3f / 6f);
            CellClimateAverager.ApplyToCells(cells, corners, p.BeachElevationThreshold);
            RegenerateTemperature(cells, p, temperatureEpicenters);
            yield return null;

            // --- Step 5/6: Границы регионов ---
            onProgress?.Invoke("Границы регионов", 4f / 6f);
            var landCells = cells.Where(c => !c.IsOcean).ToList();
            if (landCells.Count >= p.NumberOfRegions)
                RegionGrowing.GroupCells(cells, landCells, p.NumberOfRegions, p.Seed);
            LakeRegionUnifier.UnifyLakes(cells);
            yield return null;

            onComplete?.Invoke(cells, temperatureEpicenters, moistureEpicenters, rivers);
        }

        /// <summary>
        /// Пересчитывает только температуру (point-based эпицентры) на уже готовых клетках -
        /// не трогает elevation/moisture/biome/регионы. Используется при пользовательской
        /// перегенерации/правке эпицентров температуры отдельно от остальной карты.
        /// </summary>
        public static void RegenerateTemperature(List<VoronoiCell> cells, GenerationParams p, List<TemperatureEpicenter> epicenters)
        {
            // cell.Height теперь хранит Patel-style elevation (0=побережье, 1=горы) -
            // TemperatureField использует его для охлаждения с высотой, что концептуально
            // совпадает с прежним смыслом (выше = холоднее), просто шкала elevation теперь иная.
            TemperatureField.ApplyTemperature(cells, epicenters, p.BaseTemperature, p.HeightCoolingFactor, seaLevel: 0f);
        }

        /// <summary>Генерирует N эпицентров со случайной позицией/радиусом/поправкой влажности в заданных пользователем границах.</summary>
        public static List<MoistureEpicenter> GenerateRandomMoistureEpicenters(GenerationParams p)
        {
            var rng = new Random(p.Seed + 2000); // отдельный сдвиг seed - не коррелирует ни с точками, ни с температурными эпицентрами
            var epicenters = new List<MoistureEpicenter>();

            for (int i = 0; i < p.NumberOfMoistureEpicenters; i++)
            {
                var position = new Vector2((float)rng.NextDouble() * p.Width, (float)rng.NextDouble() * p.Height);
                float delta = p.MoistureEpicenterMinDelta + (float)rng.NextDouble() * (p.MoistureEpicenterMaxDelta - p.MoistureEpicenterMinDelta);
                float radius = p.MoistureEpicenterMinRadius + (float)rng.NextDouble() * (p.MoistureEpicenterMaxRadius - p.MoistureEpicenterMinRadius);
                epicenters.Add(new MoistureEpicenter(position, delta, radius));
            }

            return epicenters;
        }

        /// <summary>Генерирует N эпицентров со случайной позицией/температурой/радиусом в заданных пользователем границах.</summary>
        public static List<TemperatureEpicenter> GenerateRandomEpicenters(GenerationParams p)
        {
            var rng = new Random(p.Seed + 1000); // отдельный сдвиг seed, чтобы эпицентры не коррелировали с точками/шумом рельефа
            var epicenters = new List<TemperatureEpicenter>();

            for (int i = 0; i < p.NumberOfTemperatureEpicenters; i++)
            {
                var position = new Vector2((float)rng.NextDouble() * p.Width, (float)rng.NextDouble() * p.Height);
                float temperature = (float)rng.NextDouble();
                float radius = p.EpicenterMinRadius + (float)rng.NextDouble() * (p.EpicenterMaxRadius - p.EpicenterMinRadius);
                epicenters.Add(new TemperatureEpicenter(position, temperature, radius));
            }

            return epicenters;
        }
    }
}
