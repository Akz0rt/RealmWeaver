using System.Collections.Generic;
using WorldGen.Generation;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>Печёт сглаженные контуры "семейство биома" и "полоса высоты" в целочисленные
    /// label-буферы (−1 = нет метки), rect-scoped. Трассировка через CoastlineContour, категории —
    /// RegionCategories. GPU-путь упаковывает буферы в RG8-текстуру (RegionLabelTexture).</summary>
    public static class RegionLabelBaker
    {
        public static void BakeRect(
            IReadOnlyDictionary<int, VoronoiCell> cellById, List<Corner> corners, int[] cellIdArray,
            int[] familyLabel, int[] bandLabel,
            int texW, int texH, float mapW, float mapH,
            int smoothing, float decimation, int bands,
            int rectX, int rectY, int rectW, int rectH)
        {
            BakeCategory(cellById, corners, cellIdArray, familyLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.FamilyCategoryOf(c), RegionCategories.FamilyPriority, rectX, rectY, rectW, rectH);
            BakeCategory(cellById, corners, cellIdArray, bandLabel, texW, texH, mapW, mapH, smoothing, decimation,
                c => RegionCategories.BandCategoryOf(c, bands), RegionCategories.BandPriorityAscending(bands), rectX, rectY, rectW, rectH);
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
                    int cat = categoryOf(cellById[cellIdArray[y * texW + x]]);
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
