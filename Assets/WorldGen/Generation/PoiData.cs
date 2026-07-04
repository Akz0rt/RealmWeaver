using System;
using System.Numerics;

namespace WorldGen.Generation
{
    public enum PoiType { Unknown, City, Ruin, Dungeon, Fortress }

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
        public float IconScale = 1f;        // multiplier on PoiManager.iconWorldSize, DM-tunable per POI
        public float LabelScale = 1f;       // multiplier on PoiManager.labelCharacterSize, DM-tunable per POI
    }
}
