using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;
namespace WorldGen.Rendering
{
    public class RegionManager : MonoBehaviour
    {
        readonly List<RegionData> regions = new List<RegionData>();
        public IReadOnlyList<RegionData> Regions => regions;
        public RegionData Get(int id) => regions.Find(r => r.Id == id);
        public Color NextColor() => RegionColorPalette.GetRegionColor(regions.Count);
        int nextId;

        public RegionData Add(string name, Color color)
        {
            var r = new RegionData(nextId++, name, color);
            regions.Add(r);
            return r;
        }
        public void Remove(int id) => regions.RemoveAll(r => r.Id == id);
        public void Clear() { regions.Clear(); nextId = 0; }
        public void SetName(int id, string name) { var r = Get(id); if (r != null) r.Name = name; }
        public void SetColor(int id, Color c) { var r = Get(id); if (r != null) r.Color = c; }

        /// <summary>Replace the whole list (generation/load). Ids are taken as-is; nextId set past the max.</summary>
        public void SetAll(IEnumerable<RegionData> src)
        {
            regions.Clear(); nextId = 0;
            foreach (var r in src) { regions.Add(r); if (r.Id >= nextId) nextId = r.Id + 1; }
        }
    }
}
