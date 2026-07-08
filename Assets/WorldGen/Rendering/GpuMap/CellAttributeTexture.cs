using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RGBAFloat текстура атрибутов клеток, индекс = cellId. Крошечная (~2 тексела/клетку),
    /// перезаливается целиком при правке (&lt;0.1мс). 2 тексела на клетку: слот A (family, elevation,
    /// temperature, waterType) в строке cellId; слот B (regionId,...) в строке cellId+cellRows.
    /// Point-фильтр. Соответствие раскладки — с MapTerrain.shader (см. attr() в шейдере).</summary>
    public class CellAttributeTexture
    {
        public Texture2D Texture { get; private set; }
        public int Width { get; private set; }
        int cellRows;               // строк на один "слот" (полная высота текстуры = cellRows*2)
        Color[] pixels;
        int cellCount;

        public int CellRows => cellRows;

        public CellAttributeTexture(IReadOnlyList<VoronoiCell> cells) => Rebuild(cells);

        public void Rebuild(IReadOnlyList<VoronoiCell> cells)
        {
            cellCount = cells.Count;
            Width = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(cellCount)));
            cellRows = Mathf.Max(1, Mathf.CeilToInt(cellCount / (float)Width));
            int h = cellRows * 2;   // 2 слота на клетку
            if (Texture != null) Object.Destroy(Texture);
            Texture = new Texture2D(Width, h, TextureFormat.RGBAFloat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color[Width * h];
            foreach (var cell in cells) Write(cell);
            Apply();
        }

        void Write(VoronoiCell cell)
        {
            int id = cell.Id;
            int x = id % Width, y = id / Width;
            float waterType = cell.EffectiveIsLake ? 2f : (cell.EffectiveIsOcean ? 1f : 0f);
            pixels[y * Width + x] = new Color(
                (float)MapPalette.GetFamily(cell.Biome),
                cell.EffectiveElevation,
                cell.EffectiveTemperature,
                waterType);
            int yB = cellRows + y;   // слот B
            pixels[yB * Width + x] = new Color(cell.RegionId, 0, 0, 0);
        }

        public void UpdateCell(VoronoiCell cell) => Write(cell);
        public void UpdateCells(IEnumerable<VoronoiCell> cells) { foreach (var c in cells) Write(c); }
        public void Apply() { Texture.SetPixels(pixels); Texture.Apply(false); }
    }
}
