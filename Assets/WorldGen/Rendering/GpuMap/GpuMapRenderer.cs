using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering;
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
        Texture2D landDistTex;
        CellAttributeTexture attr;
        MeshRenderer meshRenderer;

        HashSet<int> waterIds = new HashSet<int>(); // id клеток-воды на момент последнего пересчёта берега
        int[] cellIdArray;  // кэш cell-id на пиксель (не меняется при правке) - для дешёвого пересчёта берега
        int bakedTexW, bakedTexH;
        bool coastDirty;   // во время мазка менялась топология суша/вода → берег пересчитать на отпускании
        const int CoastDownscale = 4;  // поле дистанции считается в 1/4 разрешения (гладкое, билинейное)

        RegionLabelTexture labelTex;
        int[] familyLabel, bandLabel;
        bool[] isLandMask;
        List<Corner> bakedCorners;
        IReadOnlyDictionary<int, VoronoiCell> bakedCellById;
        int bakedBands = 5;
        int bakedSmoothing = 2;
        float bakedDecimation = 0f;
        float bakedMapW, bakedMapH;

        // Угловатая заплатка label'ов во время мазка (UpdateCells) копит dirty-rect; сглаженный
        // пере-бейк (FinalizeLabels) на отпускании ЛКМ пере-печёт этот rect (с запасом) и сбросит флаг.
        bool labelDirty;
        int lblMinX, lblMinY, lblMaxX, lblMaxY;

        void EnsureMaterial()
        {
            if (Material != null) return;
            Material = new Material(Shader.Find("WorldGen/MapTerrain"));
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.material = Material;
        }

        public void BuildAll(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup,
            int texW, int texH, float mapW, float mapH, MapPaletteTheme theme, IReadOnlyList<Corner> corners)
        {
            EnsureMaterial();
            if (cellIdTex != null) Destroy(cellIdTex);
            cellIdTex = CellIdTexture.Build(lookup, texW, texH, mapW, mapH);
            FinishBuild(cells, texW, texH, mapW, mapH, theme, corners);
        }

        /// <summary>Как BuildAll, но тяжёлый бейк cell-id идёт чанково с прогрессом - экран генерации
        /// не подвисает. Остальное (атрибуты/берег/uniform'ы) быстрое и делается разом в конце.</summary>
        public System.Collections.IEnumerator BuildAllStepped(IReadOnlyList<VoronoiCell> cells, NearestCellLookup lookup,
            int texW, int texH, float mapW, float mapH, MapPaletteTheme theme, IReadOnlyList<Corner> corners,
            System.Action<float> onProgress)
        {
            EnsureMaterial();
            if (cellIdTex != null) Destroy(cellIdTex);
            Texture2D built = null;
            var e = CellIdTexture.BuildStepped(lookup, texW, texH, mapW, mapH, t => built = t, onProgress);
            while (e.MoveNext()) yield return e.Current;
            cellIdTex = built;
            FinishBuild(cells, texW, texH, mapW, mapH, theme, corners);
        }

        void FinishBuild(IReadOnlyList<VoronoiCell> cells, int texW, int texH, float mapW, float mapH, MapPaletteTheme theme,
            IReadOnlyList<Corner> corners)
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

            // Сглаженные области (семейство+пояс+берег) → label-текстура; шейдер заливает сушу и
            // решает суша/вода из неё (B-канал - сглаженная маска берега).
            bakedCorners = new List<Corner>(corners);
            bakedCellById = BuildCellById(cells);
            bakedMapW = mapW; bakedMapH = mapH;
            labelDirty = false;
            int labelLen = texW * texH;
            familyLabel = new int[labelLen];
            bandLabel = new int[labelLen];
            isLandMask = new bool[labelLen];
            RegionLabelBaker.BakeRect(bakedCellById, bakedCorners, cellIdArray, familyLabel, bandLabel, isLandMask,
                texW, texH, mapW, mapH, bakedSmoothing, bakedDecimation, bakedBands, 0, 0, texW, texH);
            if (labelTex == null) labelTex = new RegionLabelTexture();
            labelTex.Build(familyLabel, bandLabel, isLandMask, texW, texH);
            Material.SetTexture("_LabelTex", labelTex.Texture);
            Material.SetVector("_LabelTexel", labelTex.Texel);

            waterIds.Clear();
            foreach (var c in cells)
                if (c.EffectiveIsOcean || c.EffectiveIsLake) waterIds.Add(c.Id);
            coastDirty = false;
            coastDistTex = CoastDistanceTexture.BuildFromMask(isLandMask, false, texW, texH, CoastDownscale, 96f);

            // Поле дистанции суша→вода (для мягкого пляжа): 0 на воде, растёт вглубь суши.
            if (landDistTex != null) Destroy(landDistTex);
            landDistTex = CoastDistanceTexture.BuildFromMask(isLandMask, true, texW, texH, CoastDownscale, 64f);
            Material.SetTexture("_LandDistTex", landDistTex);

            Material.SetTexture("_CellIdTex", cellIdTex);
            Material.SetTexture("_AttrTex", attr.Texture);
            Material.SetFloat("_AttrWidth", attr.Width);
            Material.SetFloat("_CellRows", attr.CellRows);
            Material.SetVector("_MapSize", new Vector4(mapW, mapH, 0, 0));
            Material.SetFloat("_Mode", 3); // Combined

            // Тёмная обводка берега. Стартовые значения - крутятся вживую.
            Material.SetVector("_CellIdTexel", new Vector4(1f / texW, 1f / texH, 0, 0));
            var outline = MapPalette.GetSlotColor(theme, PaletteSlot.Outline);
            Material.SetColor("_OutlineColor", new Color(outline.r / 255f, outline.g / 255f, outline.b / 255f, 1f));
            SetSlot("_BiomeLineColor", theme, PaletteSlot.Outline);
            Material.SetFloat("_BiomeLineStrength", 0.5f);

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
            Material.SetFloat("_ShowCoast", 1f); // фактическое состояние выставит SetLayers сразу после бейка

            UploadPalette(theme);
        }

        /// <summary>Параметры сглаживания контуров label'ов (используются при следующем bake).
        /// smoothing = число итераций Chaikin; decimation = мировая дистанция прореживания вершин
        /// (больше = меньше вершин = круглее край, как старый BorderRoundnessDistance).</summary>
        public void SetContourParams(int smoothing, float decimation)
        {
            bakedSmoothing = Mathf.Max(0, smoothing);
            bakedDecimation = Mathf.Max(0f, decimation);
        }

        /// <summary>Слои Биом/Рельеф/Берег - мгновенно через uniform (без пере-бейка). См. шейдер _ShowBiome/_ShowRelief/_ShowCoast.</summary>
        public void SetLayers(bool showBiome, bool showRelief, bool showCoast)
        {
            if (Material == null) return;
            Material.SetFloat("_ShowBiome", showBiome ? 1f : 0f);
            Material.SetFloat("_ShowRelief", showRelief ? 1f : 0f);
            Material.SetFloat("_ShowCoast", showCoast ? 1f : 0f);
        }

        /// <summary>Заливает массив цветов регионов (индекс = RegionData.Id) в _RegionColor -
        /// режим "Регионы" (см. SetRegionFill). Пробелы (id без записи в rm.Regions) остаются
        /// чёрными - не видны, т.к. только клетки с валидным RegionId читают этот индекс.</summary>
        public void UploadRegionColors(RegionManager rm)
        {
            if (Material == null) return;
            var arr = new Vector4[128];
            if (rm != null)
                foreach (var r in rm.Regions)
                    if (r.Id >= 0 && r.Id < 128)
                        arr[r.Id] = new Vector4(r.Color.r, r.Color.g, r.Color.b, 1f);
            Material.SetVectorArray("_RegionColor", arr);
        }

        /// <summary>Режим "Регионы": плоская заливка цветом региона вместо биома/рельефа - мгновенно
        /// через uniform, развязано от _Mode (не завязано на порядковый номер enum'а режима).</summary>
        public void SetRegionFill(bool on)
        {
            if (Material == null) return;
            Material.SetFloat("_RegionFill", on ? 1f : 0f);
        }

        /// <summary>Параметры пляжа (песок у берега) - мгновенно через uniform (без пере-бейка).
        /// width - px глубины перехода вглубь суши, strength - 0..1 сила подмешивания цвета песка,
        /// hardness - резкость перехода (степень в pow: больше = резче/уже кайма).</summary>
        public void SetBeachParams(float width, float strength, float hardness, Color color)
        {
            if (Material == null) return;
            Material.SetFloat("_BeachWidth", Mathf.Max(0.001f, width));
            Material.SetFloat("_BeachStrength", Mathf.Clamp01(strength));
            Material.SetFloat("_BeachHardness", Mathf.Max(0.05f, hardness));
            Material.SetColor("_BeachColor", color);
        }

        void SetSlot(string uniform, MapPaletteTheme theme, PaletteSlot slot)
        {
            Color32 c = MapPalette.GetSlotColor(theme, slot);
            Material.SetColor(uniform, new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
        }

        void UploadPalette(MapPaletteTheme theme)
        {
            // Indexed by (int)Biome (0..20), sized past the max ordinal. Land + Beach get per-biome colors;
            // Ocean/Lake have no flat slot (GetBiomeColor throws) → fall back to the Coast color so no index
            // is left black for the rare sentinel-pixel where a per-cell water biome reaches the land branch.
            var arr = new Vector4[24];
            Color32 coast = MapPalette.GetSlotColor(theme, PaletteSlot.Coast);
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
            {
                Color32 c = (b == Biome.Ocean || b == Biome.Lake) ? coast : MapPalette.GetBiomeColor(theme, b);
                arr[(int)b] = new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            }
            Material.SetVectorArray("_Palette", arr);
        }

        public void UpdateCells(IEnumerable<VoronoiCell> cells)
        {
            if (attr == null) return;
            foreach (var c in cells)
            {
                attr.UpdateCell(c);
                PatchCellLabelFaceted(c);
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

            if (labelDirty)
                labelTex.PatchRect(familyLabel, bandLabel, isLandMask, lblMinX, lblMinY, lblMaxX - lblMinX + 1, lblMaxY - lblMinY + 1);
        }

        // Угловатая (по клеткам) заплатка label'ов для одной изменённой клетки: ставит family/band/
        // isLand её пикселям (без трассировки/сглаживания - мгновенно во время мазка). Копит dirty-rect.
        void PatchCellLabelFaceted(VoronoiCell cell)
        {
            if (cellIdArray == null || familyLabel == null) return;
            RectPixels(cell, out int x0, out int y0, out int x1, out int y1);
            if (x1 < x0 || y1 < y0) return; // вырожденный полигон (пустая клетка-призрак) - без патча
            int fam = MapRaster.RegionCategories.BiomeCategoryOf(cell);
            int bnd = MapRaster.RegionCategories.BandCategoryOf(cell, bakedBands);
            bool land = MapRaster.RegionCategories.IsLandCell(cell);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * bakedTexW + x;
                    if (cellIdArray[i] != cell.Id) continue;
                    familyLabel[i] = fam; bandLabel[i] = bnd; isLandMask[i] = land;
                }
            ExpandLabelDirty(x0, y0, x1, y1);
        }

        // Пиксельный bbox клетки из её полигона (мировые координаты → пиксели), клампнут в текстуру.
        void RectPixels(VoronoiCell cell, out int x0, out int y0, out int x1, out int y1)
        {
            if (cell.Polygon == null || cell.Polygon.Count == 0) { x0 = y0 = 1; x1 = y1 = 0; return; }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in cell.Polygon)
            { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
            x0 = Mathf.Clamp(Mathf.FloorToInt(minX / bakedMapW * bakedTexW), 0, bakedTexW - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt (maxX / bakedMapW * bakedTexW), 0, bakedTexW - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt(minY / bakedMapH * bakedTexH), 0, bakedTexH - 1);
            y1 = Mathf.Clamp(Mathf.CeilToInt (maxY / bakedMapH * bakedTexH), 0, bakedTexH - 1);
        }

        void ExpandLabelDirty(int x0, int y0, int x1, int y1)
        {
            if (!labelDirty) { lblMinX = x0; lblMinY = y0; lblMaxX = x1; lblMaxY = y1; labelDirty = true; }
            else { lblMinX = Mathf.Min(lblMinX, x0); lblMinY = Mathf.Min(lblMinY, y0); lblMaxX = Mathf.Max(lblMaxX, x1); lblMaxY = Mathf.Max(lblMaxY, y1); }
        }

        /// <summary>Пересчитать поле дистанции берега, если за мазок менялась топология суша/вода.
        /// Вызывать на отпускании ЛКМ (EndBrushStroke): дорогой пересчёт один раз на мазок, а не на
        /// каждый штамп - во время протяжки суша/вода уже переключаются мгновенно (через атрибуты),
        /// а свечение/градиент глубины подтягиваются на отпускании.</summary>
        public void FinalizeCoast()
        {
            if (!coastDirty || cellIdArray == null) return;
            if (coastDistTex != null) Destroy(coastDistTex);
            coastDistTex = CoastDistanceTexture.BuildFromMask(isLandMask, false, bakedTexW, bakedTexH, CoastDownscale, 96f);
            Material.SetTexture("_CoastTex", coastDistTex);

            if (landDistTex != null) Destroy(landDistTex);
            landDistTex = CoastDistanceTexture.BuildFromMask(isLandMask, true, bakedTexW, bakedTexH, CoastDownscale, 64f);
            Material.SetTexture("_LandDistTex", landDistTex);

            coastDirty = false;
        }

        /// <summary>Пере-печь СГЛАЖЕННЫЕ label'ы (family/band/берег) в затронутой кистью области -
        /// на отпускании ЛКМ. Во время мазка была угловатая заплатка (PatchCellLabelFaceted); здесь
        /// контуры оседают в гладкие. rect расширяется на запас под сглаживание/децимацию.</summary>
        public void FinalizeLabels()
        {
            if (!labelDirty || cellIdArray == null) return;
            // Запас под отклонение сглаженного контура от границы клетки: децимация (мир) + база под
            // Chaikin/размер клетки, в пиксели. borderRoundness растит bakedDecimation, поэтому пад тоже.
            int pad = Mathf.Max(48, Mathf.CeilToInt((bakedDecimation * 2f + 40f) / Mathf.Max(1f, bakedMapW) * bakedTexW));
            int rx = Mathf.Clamp(lblMinX - pad, 0, bakedTexW - 1);
            int ry = Mathf.Clamp(lblMinY - pad, 0, bakedTexH - 1);
            int rx1 = Mathf.Clamp(lblMaxX + pad, 0, bakedTexW - 1);
            int ry1 = Mathf.Clamp(lblMaxY + pad, 0, bakedTexH - 1);
            int rw = rx1 - rx + 1, rh = ry1 - ry + 1;
            RegionLabelBaker.BakeRect(bakedCellById, bakedCorners, cellIdArray, familyLabel, bandLabel, isLandMask,
                bakedTexW, bakedTexH, bakedMapW, bakedMapH, bakedSmoothing, bakedDecimation, bakedBands, rx, ry, rw, rh);
            labelTex.PatchRect(familyLabel, bandLabel, isLandMask, rx, ry, rw, rh);
            labelDirty = false;
        }

        static IReadOnlyDictionary<int, VoronoiCell> BuildCellById(IReadOnlyList<VoronoiCell> cells)
        {
            var d = new Dictionary<int, VoronoiCell>(cells.Count);
            foreach (var c in cells) d[c.Id] = c;
            return d;
        }

        void OnDestroy()
        {
            if (cellIdTex != null) Destroy(cellIdTex);
            if (coastDistTex != null) Destroy(coastDistTex);
            if (landDistTex != null) Destroy(landDistTex);
            if (attr != null && attr.Texture != null) Destroy(attr.Texture);
            if (Material != null) Destroy(Material);
            labelTex?.Destroy();
        }
    }
}
