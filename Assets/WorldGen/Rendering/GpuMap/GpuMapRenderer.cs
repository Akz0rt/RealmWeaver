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
        Texture2D coastDistTex;
        CellAttributeTexture attr;
        MeshRenderer meshRenderer;

        HashSet<int> waterIds = new HashSet<int>(); // id клеток-воды на момент последнего пересчёта берега
        int[] cellIdArray;  // кэш cell-id на пиксель (не меняется при правке) - для дешёвого пересчёта берега
        int bakedTexW, bakedTexH;
        bool coastDirty;   // во время мазка менялась топология суша/вода → берег пересчитать на отпускании
        const int CoastDownscale = 4;  // поле дистанции считается в 1/4 разрешения (гладкое, билинейное)

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
            FinishBuild(cells, texW, texH, mapW, mapH, theme);
        }

        /// <summary>Как BuildAll, но тяжёлый бейк cell-id идёт чанково с прогрессом - экран генерации
        /// не подвисает. Остальное (атрибуты/берег/uniform'ы) быстрое и делается разом в конце.</summary>
        public System.Collections.IEnumerator BuildAllStepped(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup,
            int texW, int texH, float mapW, float mapH, MapPaletteTheme theme, System.Action<float> onProgress)
        {
            EnsureMaterial();
            if (cellIdTex != null) Destroy(cellIdTex);
            Texture2D built = null;
            var e = CellIdTexture.BuildStepped(lookup, texW, texH, mapW, mapH, t => built = t, onProgress);
            while (e.MoveNext()) yield return e.Current;
            cellIdTex = built;
            FinishBuild(cells, texW, texH, mapW, mapH, theme);
        }

        void FinishBuild(IReadOnlyList<VoronoiCell> cells, int texW, int texH, float mapW, float mapH, MapPaletteTheme theme)
        {
            if (attr != null && attr.Texture != null) Destroy(attr.Texture); // не течём при перегенерации
            attr = new CellAttributeTexture(cells);

            // Поле дистанции берега (для плавной глубины воды + свечения). Строится из cell-id.
            if (coastDistTex != null) Destroy(coastDistTex);
            bakedTexW = texW; bakedTexH = texH;
            // Кэшируем cell-id на пиксель (геометрия неизменна) - пересчёт берега при правке без GetPixels.
            var idPixels = cellIdTex.GetPixels();
            cellIdArray = new int[idPixels.Length];
            for (int i = 0; i < idPixels.Length; i++) cellIdArray[i] = Mathf.RoundToInt(idPixels[i].r);
            waterIds.Clear();
            foreach (var c in cells)
                if (c.EffectiveIsOcean || c.EffectiveIsLake) waterIds.Add(c.Id);
            coastDirty = false;
            coastDistTex = CoastDistanceTexture.Build(cellIdArray, cid => waterIds.Contains(cid), texW, texH, CoastDownscale, 96f);

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

            // Рельеф: ступени высоты + hillshade + холодный лунный подсвет. Стартовые из CPU-дефолтов.
            Material.SetFloat("_ElevBands", 5f);
            Material.SetFloat("_BandContrast", 40f);
            Material.SetFloat("_ReliefStrength", 3f);
            Material.SetFloat("_ReliefStep", 0.012f);
            Material.SetFloat("_LightAzimuth", 315f);
            Material.SetFloat("_ReliefAmbient", 0.5f);
            Material.SetFloat("_ColdLight", 0.12f);
            SetSlot("_LightColor", theme, PaletteSlot.Light);

            // Тонировка по температуре / цвета воды / зерно / виньетка (Task 7).
            SetSlot("_TintCool", theme, PaletteSlot.TintCool);
            SetSlot("_TintWarm", theme, PaletteSlot.TintWarm);
            SetSlot("_SeaShallow", theme, PaletteSlot.Shallow);
            SetSlot("_SeaDeep", theme, PaletteSlot.Abyss);
            SetSlot("_LakeShallow", theme, PaletteSlot.LakeS);
            SetSlot("_LakeDeep", theme, PaletteSlot.LakeD);
            Material.SetFloat("_Darkness", 72f);
            Material.SetFloat("_GrainAmount", 0.03f);
            Material.SetFloat("_GrainScale", 700f);
            Material.SetFloat("_TintStrength", 0.1f);

            // Берег: плавная глубина воды + свечение (Task 8).
            Material.SetTexture("_CoastTex", coastDistTex);
            Material.SetFloat("_WaterDepthRange", 70f);
            Material.SetFloat("_GlowWidth", 16f);
            SetSlot("_GlowColor", theme, PaletteSlot.Glow);

            Material.SetFloat("_ShowBiome", 1f);
            Material.SetFloat("_ShowRelief", 1f);

            UploadPalette(theme);
        }

        /// <summary>Слои Биом/Рельеф - мгновенно через uniform (без пере-бейка). См. шейдер _ShowBiome/_ShowRelief.</summary>
        public void SetLayers(bool showBiome, bool showRelief)
        {
            if (Material == null) return;
            Material.SetFloat("_ShowBiome", showBiome ? 1f : 0f);
            Material.SetFloat("_ShowRelief", showRelief ? 1f : 0f);
        }

        void SetSlot(string uniform, MapPaletteTheme theme, PaletteSlot slot)
        {
            Color32 c = MapPalette.GetSlotColor(theme, slot);
            Material.SetColor(uniform, new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
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
            foreach (var c in cells)
            {
                attr.UpdateCell(c);
                // Смена статуса суша/вода → берег устарел (пересчёт отложен до FinalizeCoast).
                bool nowWater = c.EffectiveIsOcean || c.EffectiveIsLake;
                bool wasWater = waterIds.Contains(c.Id);
                if (nowWater != wasWater)
                {
                    coastDirty = true;
                    if (nowWater) waterIds.Add(c.Id); else waterIds.Remove(c.Id);
                }
            }
            attr.Apply();
        }

        /// <summary>Пересчитать поле дистанции берега, если за мазок менялась топология суша/вода.
        /// Вызывать на отпускании ЛКМ (EndBrushStroke): дорогой пересчёт один раз на мазок, а не на
        /// каждый штамп - во время протяжки суша/вода уже переключаются мгновенно (через атрибуты),
        /// а свечение/градиент глубины подтягиваются на отпускании.</summary>
        public void FinalizeCoast()
        {
            if (!coastDirty || cellIdArray == null) return;
            if (coastDistTex != null) Destroy(coastDistTex);
            coastDistTex = CoastDistanceTexture.Build(cellIdArray, cid => waterIds.Contains(cid), bakedTexW, bakedTexH, CoastDownscale, 96f);
            Material.SetTexture("_CoastTex", coastDistTex);
            coastDirty = false;
        }

        void OnDestroy()
        {
            if (cellIdTex != null) Destroy(cellIdTex);
            if (coastDistTex != null) Destroy(coastDistTex);
            if (attr != null && attr.Texture != null) Destroy(attr.Texture);
            if (Material != null) Destroy(Material);
        }
    }
}
