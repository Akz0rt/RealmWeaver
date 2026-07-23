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
        // Ц2 Task 6: self-discovered if left unassigned (mirrors PoiManager.cameraController's own
        // FindObjectOfType fallback) — DungeonManager subscribes to PoiManager.OnPoiDeleted so a deleted
        // POI's interiors (town + every building) are cleaned up wherever DeletePoi is called from,
        // without either caller (PoiEditPanel, PoiEditorScreen) needing a DungeonManager reference of its
        // own. In SampleScene, PoiManager and DungeonManager are sibling GameObjects, so discovery is safe.
        public PoiManager poiManager;

        readonly List<InteriorData> dungeons = new List<InteriorData>();

        void Awake()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated += ClearAll;
            if (poiManager == null) poiManager = FindObjectOfType<PoiManager>();
            if (poiManager != null) poiManager.OnPoiDeleted += RemoveForPoi;
        }
        void OnDestroy()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated -= ClearAll;
            if (poiManager != null) poiManager.OnPoiDeleted -= RemoveForPoi;
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

        /// <summary>Appends a new interior to the project list (Ц2: a building interior opened from an
        /// active settlement building — a SECOND interior for the same OwnerPoiId, distinguished by a
        /// non-zero OwnerRoomId). GetByPoiId/GetOrCreateForPoi cannot serve this: both resolve the FIRST
        /// interior for a poiId, which is always the town (created before any building interior can
        /// exist). No existence check here — the caller (MapScreenController.OpenBuildingInterior) already
        /// probes via InteriorOps.FindBuildingInterior before calling this.</summary>
        public void AddInterior(InteriorData interior)
        {
            if (interior != null) dungeons.Add(interior);
        }

        public void LoadDungeons(List<InteriorData> loaded)
        {
            dungeons.Clear();
            if (loaded != null)
            {
                // Collapse the DROPPED legacy building room types (3/4 — Служебная/Особая) to the plain room
                // so a save from before the type simplification doesn't render random rooms as the entrance
                // (user 2026-07-19). TypeId 2 is NOT collapsed: it is the Лестница in the current palette, and
                // collapsing it would wipe every stairwell in the building (see BuildingGenerator.NormalizeTypes,
                // which also documents the unreleased-dev-data caveat for old TypeId 2 = "Приватная"). Then
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

        /// <summary>Discards EVERY interior owned by a POI — the town AND every building interior under
        /// it (used when a type change makes the saved interior's Kind no longer match the POI's new type,
        /// see PoiEditorScreen.OnTypePicked; and Ц2 Task 6: self-wired to PoiManager.OnPoiDeleted above, so
        /// deleting the POI itself takes its whole interior tree with it). InteriorOps passthrough —
        /// GetAll() returns a read-only view, and InteriorOps' mutators need the real backing List.</summary>
        public void RemoveForPoi(string poiId) => InteriorOps.RemoveOwnedInteriors(dungeons, poiId);

        /// <summary>Ц2 Task 6: one settlement building node's own interior (node deletion) — the town and
        /// every OTHER building's interior are untouched.</summary>
        public int RemoveOwnedInterior(string poiId, int roomId) => InteriorOps.RemoveOwnedInteriors(dungeons, poiId, roomId);

        /// <summary>Ц2 Task 6: every BUILDING interior of a town, town kept («Сгенерировать заново» replaces
        /// the town's own floor in place; its buildings' interiors die with their nodes).</summary>
        public int RemoveBuildingInteriors(string poiId) => InteriorOps.RemoveBuildingInteriors(dungeons, poiId);

        /// <summary>Ц2 Task 6: feeds the «Сгенерировать заново» confirm gate — does this town own at least
        /// one building interior?</summary>
        public bool HasBuildingInteriors(string poiId) => InteriorOps.HasBuildingInteriors(dungeons, poiId);
    }
}
