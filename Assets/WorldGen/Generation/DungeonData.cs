using System.Collections.Generic;

namespace WorldGen.Generation
{
    public enum DungeonTile { Wall = 0, Floor = 1 }

    /// <summary>One numbered key entry — a chamber the DM annotates. In v1 these come only from
    /// generation (no manual add/move); Number restarts at 1 per level.</summary>
    public class KeyChamber
    {
        public int Number;
        public string Title = "";
        public string Body = "";
        public int MarkerCellX;   // chamber-node grid cell (marker anchor)
        public int MarkerCellY;
    }

    public class DungeonLevel
    {
        public int Width = 48;
        public int Height = 48;
        public DungeonTile[] Tiles;   // row-major, length Width*Height; null until generated
        public List<KeyChamber> Chambers = new List<KeyChamber>();

        public DungeonTile Get(int x, int y) => Tiles[y * Width + x];
        public void Set(int x, int y, DungeonTile t) => Tiles[y * Width + x] = t;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    }

    /// <summary>A cave dungeon owned by one POI (by PoiData.Id). One or more levels.</summary>
    public class DungeonData
    {
        public string OwnerPoiId;
        public List<DungeonLevel> Levels = new List<DungeonLevel>();
    }
}
