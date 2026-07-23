using System;
using System.Numerics;
using Newtonsoft.Json;

namespace WorldGen.Generation
{
    public enum PoiType { Unknown = 0, City = 1, Ruin = 2, Dungeon = 3, Fortress = 4, Village = 5, Tower = 6, Temple = 7, Encounter = 8, Port = 10 }

    public class PoiData
    {
        public string Id = Guid.NewGuid().ToString();
        public PoiType Type;
        public string Name = "";
        public string Description = "";     // free text; field ready for future media embedding
        public int OwnerCellId = -1;        // logical owner cell (for region/biome queries)
        public Vector2 WorldPosition;       // visual position in map XZ (draggable)
        public string CustomSpritePath;     // null = type placeholder; kept only for display (filename shown in the edit panel) once CustomIconBytes is set
        public byte[] CustomIconBytes;      // null = use type placeholder; the authoritative, self-contained icon (survives save/load, unlike a path)
        /// <summary>A POI's preview image (PNG bytes), or null. Optional and null until the DM adds one —
        /// a map of dozens of POIs must not carry dozens of images by default. NullValueHandling.Ignore
        /// keeps the key out of every save when absent (the Room.Preview precedent in DungeonData.cs).
        /// World definition only.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public byte[] Preview = null;
        public float IconScale = 1f;        // multiplier on PoiManager.iconWorldSize, DM-tunable per POI
        public float LabelScale = 1f;       // multiplier on PoiManager.labelCharacterSize, DM-tunable per POI
    }
}
