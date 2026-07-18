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

        readonly List<InteriorData> dungeons = new List<InteriorData>();

        void Awake()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated += ClearAll;
        }
        void OnDestroy()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated -= ClearAll;
        }

        public IReadOnlyList<InteriorData> GetAll() => dungeons;
        public bool HasDungeon(string poiId) => GetByPoiId(poiId) != null;
        public InteriorData GetByPoiId(string poiId) =>
            poiId != null ? dungeons.FirstOrDefault(d => d.OwnerPoiId == poiId) : null;

        public InteriorData GetOrCreateForPoi(string poiId, InteriorKind kind = InteriorKind.Dungeon)
        {
            var d = GetByPoiId(poiId);
            // Kind only matters on CREATE — an existing interior keeps its stored Kind.
            if (d == null) { d = new InteriorData { OwnerPoiId = poiId, Kind = kind }; dungeons.Add(d); }
            return d;
        }

        public void LoadDungeons(List<InteriorData> loaded)
        {
            dungeons.Clear();
            if (loaded != null) dungeons.AddRange(loaded);
        }

        public void ClearAll() => dungeons.Clear();
    }
}
