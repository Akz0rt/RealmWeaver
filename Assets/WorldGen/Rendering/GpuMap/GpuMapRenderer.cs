using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>Управляет GPU-рендером карты: cell-id + атрибуты + материал MapTerrain.
    /// Правка = UpdateCells → перезалить атрибуты → GPU перерисует бесплатно (независимо от размера
    /// кисти). См. docs/superpowers/specs/2026-07-08-gpu-map-terrain-render-design.md.</summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class GpuMapRenderer : MonoBehaviour
    {
        public Material Material { get; private set; }
        Texture2D cellIdTex;
        CellAttributeTexture attr;
        MeshRenderer meshRenderer;

        void EnsureMaterial()
        {
            if (Material != null) return;
            Material = new Material(Shader.Find("WorldGen/MapTerrain"));
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.material = Material;
        }

        public void BuildAll(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup,
            int texW, int texH, float mapW, float mapH, MapPaletteTheme theme)
        {
            EnsureMaterial();
            if (cellIdTex != null) Destroy(cellIdTex);
            cellIdTex = CellIdTexture.Build(lookup, texW, texH, mapW, mapH);
            attr = new CellAttributeTexture(cells);

            Material.SetTexture("_CellIdTex", cellIdTex);
            Material.SetTexture("_AttrTex", attr.Texture);
            Material.SetFloat("_AttrWidth", attr.Width);
            Material.SetFloat("_CellRows", attr.CellRows);
            Material.SetVector("_MapSize", new Vector4(mapW, mapH, 0, 0));
            Material.SetFloat("_Mode", 3); // Combined

            // Органичные границы (domain warp) + тёмная обводка. Стартовые значения - крутятся вживую.
            Material.SetFloat("_WarpAmount", 0.012f);
            Material.SetFloat("_WarpScale", 8f);
            Material.SetFloat("_Seed", 0f);
            Material.SetVector("_CellIdTexel", new Vector4(1f / texW, 1f / texH, 0, 0));
            var outline = MapPalette.GetSlotColor(theme, PaletteSlot.Outline);
            Material.SetColor("_OutlineColor", new Color(outline.r / 255f, outline.g / 255f, outline.b / 255f, 1f));

            UploadPalette(theme);
        }

        void UploadPalette(MapPaletteTheme theme)
        {
            var arr = new Vector4[16];
            for (int f = 0; f < 11; f++)
            {
                // У Sea/Lake нет плоского слота - берём Coast как заглушку (вода красится отдельно позже).
                var family = (BiomeFamily)f;
                Color32 c = (family == BiomeFamily.Sea || family == BiomeFamily.Lake)
                    ? MapPalette.GetSlotColor(theme, PaletteSlot.Coast)
                    : MapPalette.GetSlotColor(theme, family);
                arr[f] = new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            }
            Material.SetVectorArray("_Palette", arr);
        }

        public void UpdateCells(IEnumerable<VoronoiCell> cells)
        {
            if (attr == null) return;
            attr.UpdateCells(cells);
            attr.Apply();
        }

        void OnDestroy()
        {
            if (cellIdTex != null) Destroy(cellIdTex);
            if (Material != null) Destroy(Material);
        }
    }
}
