using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Чистый C# движок расстановки декораций: клетки → детерминированный список
    /// инстансов. Без Unity-рендера (юнит-тестируемый как генераторы в Generation/).</summary>
    public static class DecorationPlacer
    {
        // --- Классификация: подходит ли тип к клетке + контекст-категория стиля ---

        static bool IsColdCell(VoronoiCell c, DecorationConfig cfg)
        {
            var fam = MapPalette.GetFamily(c.Biome);
            return c.EffectiveTemperature < cfg.coldTemperature
                   || fam == BiomeFamily.Snow || fam == BiomeFamily.Tundra;
        }

        /// <summary>Категория для гор/холмов: Snowy если холодно, иначе Forested над лесом, иначе Bare.</summary>
        static DecorationStyleCategory ReliefStyle(VoronoiCell c, DecorationConfig cfg)
        {
            if (IsColdCell(c, cfg)) return DecorationStyleCategory.Snowy;
            var fam = MapPalette.GetFamily(c.Biome);
            if (fam == BiomeFamily.Forest || fam == BiomeFamily.ForestWarm) return DecorationStyleCategory.Forested;
            return DecorationStyleCategory.Bare;
        }

        public static bool TryClassify(VoronoiCell cell, DecorationConfig cfg,
                                       DecorationType type, out DecorationStyleCategory style)
        {
            style = DecorationStyleCategory.Plain;
            if (!RegionCategories.IsLandCell(cell)) return false;

            float e = cell.EffectiveElevation;
            var fam = MapPalette.GetFamily(cell.Biome);

            switch (type)
            {
                case DecorationType.Mountain:
                    if (e < cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Hill:
                    if (e < cfg.hillMinElevation || e >= cfg.mountainMinElevation) return false;
                    style = ReliefStyle(cell, cfg); return true;
                case DecorationType.Pine:
                    if (fam != BiomeFamily.Forest) return false;
                    style = IsColdCell(cell, cfg) ? DecorationStyleCategory.Snowy : DecorationStyleCategory.Plain;
                    return true;
                case DecorationType.AutumnTree:
                    if (fam != BiomeFamily.ForestWarm) return false;
                    style = DecorationStyleCategory.Plain; return true;
                case DecorationType.Mesa:
                    if (fam != BiomeFamily.Badlands) return false;
                    style = DecorationStyleCategory.Plain; return true;
                default: return false;
            }
        }

        // --- Детерминированный хеш (fract от целочисленного mix) ---
        public static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        static float Hash01(int x, int y, int salt) => Hash(x, y, salt) / 4294967295f;

        // --- Грид-проход: расстановка инстансов по клеткам ---

        static readonly DecorationType[] AllTypes =
        { DecorationType.Mountain, DecorationType.Hill, DecorationType.Pine, DecorationType.AutumnTree, DecorationType.Mesa };

        static int SaltOf(DecorationType t) => (int)t * 101 + 17;

        static Color32 TintFor(DecorationType type, DecorationStyleCategory style, MapPaletteTheme theme, float brightness)
        {
            Color32 baseC = type switch
            {
                DecorationType.Mountain or DecorationType.Hill => style == DecorationStyleCategory.Snowy
                        ? MapPalette.GetSlotColor(theme, PaletteSlot.Snow)
                        : style == DecorationStyleCategory.Forested
                            ? MapPalette.GetSlotColor(theme, PaletteSlot.Forest)
                            : MapPalette.GetSlotColor(theme, PaletteSlot.MtnL),
                DecorationType.Pine => MapPalette.GetSlotColor(theme, PaletteSlot.Forest),
                DecorationType.AutumnTree => MapPalette.GetSlotColor(theme, PaletteSlot.ForestWarm),
                DecorationType.Mesa => MapPalette.GetSlotColor(theme, PaletteSlot.Badlands),
                _ => new Color32(200, 200, 200, 255),
            };
            return new Color32(
                (byte)Mathf.Clamp(baseC.r * brightness, 0, 255),
                (byte)Mathf.Clamp(baseC.g * brightness, 0, 255),
                (byte)Mathf.Clamp(baseC.b * brightness, 0, 255), 255);
        }

        /// <summary>Один тип: джиттер-грид по всей карте (или по rect). Грид-индексы стабильны от 0,
        /// поэтому rect-подвыборка совпадает с полным проходом на пересечении.</summary>
        static void PlaceType(List<DecorationInstance> into, DecorationType type,
            NearestCellLookup lookup, int seed, float mapW, float mapH, DecorationConfig cfg,
            MapPaletteTheme theme, Rect? worldRect)
        {
            float step = cfg.GridStep(type);
            if (step <= 0.01f) return;
            int salt = SaltOf(type) + seed;
            int nx = Mathf.CeilToInt(mapW / step);
            int ny = Mathf.CeilToInt(mapH / step);

            int gx0 = 0, gy0 = 0, gx1 = nx, gy1 = ny;
            if (worldRect.HasValue)
            {
                var r = worldRect.Value;
                gx0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin / step));
                gy0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin / step));
                gx1 = Mathf.Min(nx, Mathf.CeilToInt(r.xMax / step) + 1);
                gy1 = Mathf.Min(ny, Mathf.CeilToInt(r.yMax / step) + 1);
            }

            float prob = cfg.Probability(type);
            float baseSize = cfg.BaseSize(type);

            for (int gy = gy0; gy < gy1; gy++)
            for (int gx = gx0; gx < gx1; gx++)
            {
                if (Hash(gx, gy, salt) / 4294967295f > prob) continue;
                float jx = (Hash(gx, gy, salt + 1) / 4294967295f) * step;
                float jz = (Hash(gx, gy, salt + 2) / 4294967295f) * step;
                float wx = gx * step + jx;
                float wz = gy * step + jz;
                if (wx >= mapW || wz >= mapH) continue;

                if (worldRect.HasValue && !worldRect.Value.Contains(new Vector2(wx, wz))) continue;

                var cell = lookup.FindNearest(new System.Numerics.Vector2(wx, wz));
                if (cell == null) continue;
                if (!TryClassify(cell, cfg, type, out var style)) continue;

                float sizeJit = 1f + (Hash(gx, gy, salt + 3) / 4294967295f - 0.5f) * 2f * cfg.sizeJitter;
                float brightness = 0.88f + (Hash(gx, gy, salt + 4) / 4294967295f) * 0.24f;
                into.Add(new DecorationInstance
                {
                    worldPos = new Vector2(wx, wz),
                    type = type, style = style,
                    artVariant = (int)(Hash(gx, gy, salt + 5) & 0xFFFF),
                    scale = baseSize * sizeJit * cfg.globalScale,
                    tint = TintFor(type, style, theme, brightness),
                    sortZ = wz,
                });
            }
        }

        public static List<DecorationInstance> Place(IReadOnlyList<VoronoiCell> cells,
            NearestCellLookup lookup, int seed, float mapW, float mapH,
            DecorationConfig cfg, MapPaletteTheme theme)
        {
            var list = new List<DecorationInstance>();
            if (cfg == null || !cfg.enabled || lookup == null) return list;
            foreach (var t in AllTypes)
                PlaceType(list, t, lookup, seed, mapW, mapH, cfg, theme, null);

            if (list.Count > cfg.maxInstances)
            {
                Debug.LogWarning($"[Decorations] placed {list.Count} > cap {cfg.maxInstances}; truncated. Increase maxInstances or grid steps.");
                list.RemoveRange(cfg.maxInstances, list.Count - cfg.maxInstances);
            }
            list.Sort((a, b) => b.sortZ.CompareTo(a.sortZ)); // descending: south/nearer (lower Z, drawn last) overlaps north/farther
            return list;
        }

        /// <summary>Дописывает в into инстансы всех типов, чьи грид-точки попадают в worldRect.
        /// Вызывающий сам чистит старые инстансы этого rect и ре-сортирует.</summary>
        public static void PlaceRect(List<DecorationInstance> into,
            NearestCellLookup lookup, int seed, float mapW, float mapH,
            DecorationConfig cfg, MapPaletteTheme theme, Rect worldRect)
        {
            if (cfg == null || !cfg.enabled || lookup == null) return;
            foreach (var t in AllTypes)
                PlaceType(into, t, lookup, seed, mapW, mapH, cfg, theme, worldRect);
        }
    }
}
