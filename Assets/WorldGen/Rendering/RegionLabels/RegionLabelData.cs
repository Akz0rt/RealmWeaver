using System;
using WorldGen.Rendering.MapRaster; // BiomeFamily

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Which zoom-LOD tier a label belongs to. Biome = mid-zoom biome zone; Continent = far-zoom
    /// landmass name; Sea = far-zoom ocean name. Defaults to Biome (0) so legacy saves load unchanged.</summary>
    public enum LabelKind { Biome = 0, Continent = 1, Sea = 2 }

    /// <summary>One editable region name label. Seeded from a biome-family patch centroid, then
    /// user-owned (rename/move/delete/add) and saved in the .dndproj.</summary>
    [Serializable]
    public class RegionLabelData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text;                            // shown name (Russian descriptive; DM edits)
        public System.Numerics.Vector2 WorldPosition;  // XZ world anchor (map coords)
        public BiomeFamily SeedFamily;                 // family it was seeded from (reference)
        public LabelKind Kind;                         // LOD tier: Biome (default) / Continent / Sea
        public float Priority;                         // higher wins overlap culling (biome = zone cells; continent/sea = large)
    }
}
