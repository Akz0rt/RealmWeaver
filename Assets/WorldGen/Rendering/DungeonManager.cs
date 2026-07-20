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
            if (loaded != null)
            {
                // Collapse any legacy building room types (2/3/4) to the plain room so a save from before the
                // 2-type simplification doesn't render random rooms as the entrance (user 2026-07-19). Then
                // re-wire the vertical Stairs chain (F3): a project saved while a middle-floor removal could
                // still sever the shaft (pre-fix builds of this dev branch) must still load correctly, and
                // RewireStairChain is both IDEMPOTENT (a no-op on an already-correct chain) and REPAIR-SHAPED
                // (it recomputes the chain from each floor's Лестница, never from a stale portal), so running
                // it unconditionally here is safe — same pattern as NormalizeTypes. A no-op for a Dungeon.
                foreach (var d in loaded)
                {
                    BuildingGenerator.NormalizeTypes(d);
                    BuildingGenerator.RewireStairChain(d);
                }
                dungeons.AddRange(loaded);
            }
        }

        public void ClearAll() => dungeons.Clear();

        /// <summary>Discards the interior owned by a POI (used when a type change makes the saved
        /// interior's Kind no longer match the POI's new type — see PoiEditorScreen.OnTypePicked).</summary>
        public void RemoveForPoi(string poiId) => dungeons.RemoveAll(d => d.OwnerPoiId == poiId);
    }
}
