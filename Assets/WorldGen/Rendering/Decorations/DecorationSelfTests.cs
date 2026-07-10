using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>[ContextMenu] self-tests запускает ПОЛЬЗОВАТЕЛЬ в Editor (агенты не гоняют Unity).
    /// Добавь этот компонент на любой объект сцены, ПКМ по компоненту → выбери тест.</summary>
    public class DecorationSelfTests : MonoBehaviour
    {
        // Клетка суши с заданными biome/elev/temp. VoronoiCell(int id, System.Numerics.Vector2 site).
        // System.Numerics.Vector2 указан полным именем (не через using System.Numerics;), т.к. вместе
        // с using UnityEngine; короткое имя Vector2 было бы неоднозначным (CS0104) - см. тот же
        // приём в WorldGen/Rendering/MapBorderBuilder.cs.
        static VoronoiCell LandCell(int id, Biome biome, float elev, float temp)
        {
            var c = new VoronoiCell(id, new System.Numerics.Vector2(id * 10f, 0f))
            {
                Biome = biome, Height = elev, Temperature = temp,
                IsOcean = false,
            };
            return c;
        }

        [ContextMenu("Self-Test: Decoration Classify")]
        public void SelfTestClassify()
        {
            var cfg = new DecorationConfig();
            bool ok = true;

            // Гора только на высокой клетке.
            var high = LandCell(1, Biome.SemiDesert, 0.80f, 0.5f);
            ok &= DecorationPlacer.TryClassify(high, cfg, DecorationType.Mountain, out var ms) && ms == DecorationStyleCategory.Bare;
            var low = LandCell(2, Biome.Grassland, 0.20f, 0.5f);
            ok &= !DecorationPlacer.TryClassify(low, cfg, DecorationType.Mountain, out _);

            // Снежная гора при холоде.
            var coldHigh = LandCell(3, Biome.SemiDesert, 0.85f, 0.1f);
            ok &= DecorationPlacer.TryClassify(coldHigh, cfg, DecorationType.Mountain, out var cs) && cs == DecorationStyleCategory.Snowy;

            // Лесистая гора над Forest-семейством (тёплой).
            var forestHigh = LandCell(4, Biome.Forest, 0.80f, 0.7f);
            ok &= DecorationPlacer.TryClassify(forestHigh, cfg, DecorationType.Mountain, out var fs) && fs == DecorationStyleCategory.Forested;

            // Хвоя только на Forest-семействе; осень — на ForestWarm.
            ok &= DecorationPlacer.TryClassify(LandCell(5, Biome.Taiga, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= !DecorationPlacer.TryClassify(LandCell(6, Biome.Grassland, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= DecorationPlacer.TryClassify(LandCell(7, Biome.TropicalForest, 0.3f, 0.8f), cfg, DecorationType.AutumnTree, out _);

            // Меса только на Badlands.
            ok &= DecorationPlacer.TryClassify(LandCell(8, Biome.SemiDesert, 0.3f, 0.8f), cfg, DecorationType.Mesa, out _);

            // Вода — всегда пусто.
            var ocean = new VoronoiCell(9, new System.Numerics.Vector2(0, 0)) { Biome = Biome.Ocean, IsOcean = true };
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Mountain, out _);
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Pine, out _);

            Debug.Log(ok ? "Self-Test Decoration Classify: PASS" : "Self-Test Decoration Classify: FAIL");
        }

        // Строит крошечную карту-фикстуру: сетка клеток, левая половина суша-высокая, остальное низина.
        static (System.Collections.Generic.List<VoronoiCell> cells, WorldGen.Rendering.MapRaster.NearestCellLookup lookup)
            Fixture(float mapSize, float spacing)
        {
            var cells = new System.Collections.Generic.List<VoronoiCell>();
            int id = 0;
            for (float z = spacing * 0.5f; z < mapSize; z += spacing)
            for (float x = spacing * 0.5f; x < mapSize; x += spacing)
            {
                float elev = x < mapSize * 0.5f ? 0.85f : 0.15f; // левая половина — горы
                var c = new VoronoiCell(id++, new System.Numerics.Vector2(x, z))
                { Biome = Biome.SemiDesert, Height = elev, Temperature = 0.5f, IsOcean = false };
                // NearestCellLookup исключает вырожденные клетки (Polygon.Count < 3) - без явного
                // полигона вся фикстура была бы отброшена и FindNearest везде возвращал бы null
                // (тот же guard/комментарий, что в WorldMapRenderer.SelfTestNearestCellLookup).
                float half = spacing * 0.5f;
                c.Polygon = new System.Collections.Generic.List<System.Numerics.Vector2>
                {
                    new System.Numerics.Vector2(x - half, z - half),
                    new System.Numerics.Vector2(x + half, z - half),
                    new System.Numerics.Vector2(x + half, z + half),
                    new System.Numerics.Vector2(x - half, z + half),
                };
                cells.Add(c);
            }
            var lookup = new WorldGen.Rendering.MapRaster.NearestCellLookup(cells, spacing);
            return (cells, lookup);
        }

        [ContextMenu("Self-Test: Decoration Placement")]
        public void SelfTestPlacement()
        {
            const float M = 400f;
            var (cells, lookup) = Fixture(M, 40f);
            var cfg = new DecorationConfig();
            var theme = WorldGen.Rendering.MapRaster.MapPaletteTheme.ColdTwilight;
            bool ok = true;

            var a = DecorationPlacer.Place(cells, lookup, 7, M, M, cfg, theme);
            var b = DecorationPlacer.Place(cells, lookup, 7, M, M, cfg, theme);
            ok &= a.Count == b.Count && a.Count > 0; // детерминизм: одинаковый размер
            for (int i = 0; i < a.Count && ok; i++)
                ok &= a[i].worldPos == b[i].worldPos && a[i].type == b[i].type && a[i].style == b[i].style;

            // Горы только в левой половине (высокая суша).
            foreach (var d in a)
                if (d.type == DecorationType.Mountain) ok &= d.worldPos.x < M * 0.5f + 40f;

            // sortZ неубывающий (отсортировано back-to-front).
            for (int i = 1; i < a.Count; i++) ok &= a[i].sortZ >= a[i - 1].sortZ;

            // rect == full: подвыборка правого-нижнего квадранта совпадает с фильтром полного прохода.
            var rect = new Rect(M * 0.5f, M * 0.5f, M * 0.5f, M * 0.5f);
            var rectList = new System.Collections.Generic.List<DecorationInstance>();
            DecorationPlacer.PlaceRect(rectList, lookup, 7, M, M, cfg, theme, rect);
            int fullInRect = 0;
            foreach (var d in a) if (rect.Contains(d.worldPos)) fullInRect++;
            ok &= rectList.Count == fullInRect;

            // Плотность: удвоение вероятности не уменьшает число гор.
            var dense = new DecorationConfig { mountainProbability = 1f };
            var denseList = DecorationPlacer.Place(cells, lookup, 7, M, M, dense, theme);
            int mtnA = 0, mtnD = 0;
            foreach (var d in a) if (d.type == DecorationType.Mountain) mtnA++;
            foreach (var d in denseList) if (d.type == DecorationType.Mountain) mtnD++;
            ok &= mtnD >= mtnA;

            Debug.Log(ok ? "Self-Test Decoration Placement: PASS" : "Self-Test Decoration Placement: FAIL");
        }

        [ContextMenu("Self-Test: Decoration Catalog")]
        public void SelfTestCatalog()
        {
            var cat = DecorationCatalog.BuildPlaceholder(48);
            bool ok = cat.Atlas != null;
            ok &= cat.VariantCount(DecorationType.Mountain, DecorationStyleCategory.Snowy) > 0;
            ok &= cat.VariantCount(DecorationType.Pine, DecorationStyleCategory.Plain) > 0;
            // UV-rect'ы в границах [0..1].
            var uv = cat.UvRect(DecorationType.Mountain, DecorationStyleCategory.Bare, 0);
            ok &= uv.x >= 0 && uv.y >= 0 && uv.x + uv.z <= 1.0001f && uv.y + uv.w <= 1.0001f;
            Debug.Log(ok ? "Self-Test Decoration Catalog: PASS" : "Self-Test Decoration Catalog: FAIL");
        }
    }
}
