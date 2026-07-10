using System;
using WorldGen.Rendering.MapRaster; // BiomeFamily

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>One editable region name label. Seeded from a biome-family patch centroid, then
    /// user-owned (rename/move/delete/add) and saved in the .dndproj. Latin name is the default Text.</summary>
    [Serializable]
    public class RegionLabelData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text;                            // shown name (Latin default; DM edits)
        public System.Numerics.Vector2 WorldPosition;  // XZ world anchor (map coords)
        public BiomeFamily SeedFamily;                 // family it was seeded from (reference)
    }
}
