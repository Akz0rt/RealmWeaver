using UnityEngine;
namespace WorldGen.Generation
{
    /// <summary>Per-region political metadata (name + colour). Membership lives on VoronoiCell.RegionId.</summary>
    [System.Serializable]
    public class RegionData
    {
        public int Id;
        public string Name;
        public Color Color;
        public RegionData() { }
        public RegionData(int id, string name, Color color) { Id = id; Name = name; Color = color; }
    }
}
