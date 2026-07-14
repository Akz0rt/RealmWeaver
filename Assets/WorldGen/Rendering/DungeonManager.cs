using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Owns the in-memory cave dungeons (one per POI). Wired into save/load by ProjectMenuBar
    /// and cleared on world regenerate. Mirrors PoiManager's ownership role.</summary>
    public class DungeonManager : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldMapRenderer mapRenderer;

        readonly List<DungeonData> dungeons = new List<DungeonData>();

        void Awake()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated += ClearAll;
        }
        void OnDestroy()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated -= ClearAll;
        }

        public IReadOnlyList<DungeonData> GetAll() => dungeons;
        public bool HasDungeon(string poiId) => GetByPoiId(poiId) != null;
        public DungeonData GetByPoiId(string poiId) =>
            poiId != null ? dungeons.FirstOrDefault(d => d.OwnerPoiId == poiId) : null;

        public DungeonData GetOrCreateForPoi(string poiId)
        {
            var d = GetByPoiId(poiId);
            if (d == null) { d = new DungeonData { OwnerPoiId = poiId }; dungeons.Add(d); }
            return d;
        }

        public void LoadDungeons(List<DungeonData> loaded)
        {
            dungeons.Clear();
            if (loaded != null) dungeons.AddRange(loaded);
        }

        public void ClearAll() => dungeons.Clear();
    }
}
