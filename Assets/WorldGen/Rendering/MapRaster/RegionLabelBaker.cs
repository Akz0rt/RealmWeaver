using System.Collections.Generic;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Печёт сглаженные контуры "семейство биома" и "полоса высоты" в целочисленные
    /// label-буферы (−1 = нет метки) плюс сглаженную маску суша/вода (тем же контуром, водный
    /// предикат), rect-scoped. Трассировка через CoastlineContour, категории — RegionCategories.
    /// GPU-путь упаковывает буферы в RGBA32-текстуру (RegionLabelTexture): R/G/B.</summary>
    public static class RegionLabelBaker
    {
        public static void BakeRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray,
            int[] familyLabel, int[] bandLabel, bool[] isLandMask,
            int texW, int texH, float mapW, float mapH,
            int smoothing, float decimation, int bands,
            int rectX, int rectY, int rectW, int rectH)
        {
            BakeCategory(cellById, corners, cellIdArray, familyLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.FamilyCategoryOf(c), RegionCategories.FamilyPriority, rectX, rectY, rectW, rectH);
            BakeCategory(cellById, corners, cellIdArray, bandLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.BandCategoryOf(c, bands), RegionCategories.BandPriorityAscending(bands), rectX, rectY, rectW, rectH);

            // Сглаженная маска суша/вода (тем же контуром) — для гладкого берега в шейдере.
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                    isLandMask[y * texW + x] = false;
            var waterLoops = CoastlineContour.TraceSmoothedLoops(corners, cellById, smoothing, decimation);
            CoastlineContour.RasterizeIsLand(waterLoops, isLandMask, texW, texH, mapW, mapH, rectX, rectY, rectW, rectH);
        }

        static void BakeCategory(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray,
            int[] label, int texW, int texH, float mapW, float mapH, int smoothing, float decimation,
            System.Func<VoronoiCell, int> categoryOf, IReadOnlyList<int> priorityOrder,
            int rectX, int rectY, int rectW, int rectH)
        {
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                    label[y * texW + x] = -1;

            var present = new HashSet<int>();
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                {
                    if (!cellById.TryGetValue(cellIdArray[y * texW + x], out var cell)) continue; // -1 = нет ближайшей клетки (недостижимо на реальном масштабе, но не роняем генерацию)
                    int cat = categoryOf(cell);
                    if (cat >= 0) present.Add(cat);
                }
            if (present.Count == 0) return;

            foreach (int category in priorityOrder)
            {
                if (!present.Contains(category)) continue;
                int cat = category;
                var loops = CoastlineContour.TraceSmoothedLoops(corners, cellById, c => categoryOf(c) == cat, smoothing, decimation);
                if (loops.Count == 0) continue;
                CoastlineContour.RasterizeRegionLabel(loops, label, category, texW, texH, mapW, mapH, rectX, rectY, rectW, rectH);
            }
        }
    }
}
