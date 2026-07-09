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
            var high = LandCell(1, Biome.Bare, 0.80f, 0.5f);
            ok &= DecorationPlacer.TryClassify(high, cfg, DecorationType.Mountain, out var ms) && ms == DecorationStyleCategory.Bare;
            var low = LandCell(2, Biome.Grassland, 0.20f, 0.5f);
            ok &= !DecorationPlacer.TryClassify(low, cfg, DecorationType.Mountain, out _);

            // Снежная гора при холоде.
            var coldHigh = LandCell(3, Biome.Bare, 0.85f, 0.1f);
            ok &= DecorationPlacer.TryClassify(coldHigh, cfg, DecorationType.Mountain, out var cs) && cs == DecorationStyleCategory.Snowy;

            // Лесистая гора над Forest-семейством (тёплой).
            var forestHigh = LandCell(4, Biome.TemperateDeciduousForest, 0.80f, 0.7f);
            ok &= DecorationPlacer.TryClassify(forestHigh, cfg, DecorationType.Mountain, out var fs) && fs == DecorationStyleCategory.Forested;

            // Хвоя только на Forest-семействе; осень — на ForestWarm.
            ok &= DecorationPlacer.TryClassify(LandCell(5, Biome.Taiga, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= !DecorationPlacer.TryClassify(LandCell(6, Biome.Grassland, 0.3f, 0.6f), cfg, DecorationType.Pine, out _);
            ok &= DecorationPlacer.TryClassify(LandCell(7, Biome.TropicalRainForest, 0.3f, 0.8f), cfg, DecorationType.AutumnTree, out _);

            // Меса только на Badlands.
            ok &= DecorationPlacer.TryClassify(LandCell(8, Biome.SubtropicalDesert, 0.3f, 0.8f), cfg, DecorationType.Mesa, out _);

            // Вода — всегда пусто.
            var ocean = new VoronoiCell(9, new System.Numerics.Vector2(0, 0)) { Biome = Biome.Ocean, IsOcean = true };
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Mountain, out _);
            ok &= !DecorationPlacer.TryClassify(ocean, cfg, DecorationType.Pine, out _);

            Debug.Log(ok ? "Self-Test Decoration Classify: PASS" : "Self-Test Decoration Classify: FAIL");
        }
    }
}
