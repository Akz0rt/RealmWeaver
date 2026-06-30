using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns the POI list. Handles generation, add/delete, marker spawning.
    /// Attach to any GameObject in the scene alongside WorldMapRenderer.
    /// Assign mapRenderer in the Inspector.
    /// </summary>
    public class PoiManager : MonoBehaviour
    {
        [Header("Dependencies")]
        public WorldMapRenderer mapRenderer;

        [Header("Marker settings")]
        [Tooltip("Y height above map surface for POI markers.")]
        public float poiYOffset = 0.5f;
        [Tooltip("Icon side length in world units. Tune to fit ~half a cell diameter (~7-10).")]
        public float iconWorldSize = 8f;

        readonly List<PoiData> pois = new List<PoiData>();
        readonly Dictionary<string, PoiMarkerView> markers = new Dictionary<string, PoiMarkerView>();
        Transform poiContainer;
        string selectedPoiId;

        public event Action<PoiData> OnSelectionChanged;
        public event Action OnPoisChanged;

        public IReadOnlyList<PoiData> GetAllPois() => pois;

        public PoiData GetSelectedPoi() =>
            selectedPoiId != null && pois.Any(p => p.Id == selectedPoiId)
                ? pois.First(p => p.Id == selectedPoiId)
                : null;

        void Awake()
        {
            var containerGO = new GameObject("PoiContainer");
            // Parent to mapRenderer so markers share the map's local coordinate space
            containerGO.transform.SetParent(mapRenderer != null ? mapRenderer.transform : transform, false);
            poiContainer = containerGO.transform;

            if (mapRenderer != null)
                mapRenderer.OnWorldRegenerated += ClearAll;
        }

        void OnDestroy()
        {
            if (mapRenderer != null)
                mapRenderer.OnWorldRegenerated -= ClearAll;
        }

        // ── Generation ─────────────────────────────────────────────────────────

        /// <summary>Clears existing POIs and generates new ones from counts per type.</summary>
        public void GenerateAll(Dictionary<PoiType, int> counts)
        {
            ClearAll();
            if (mapRenderer?.Cells == null) return;

            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean)
                .OrderBy(_ => Guid.NewGuid()) // shuffle
                .ToList();

            var occupiedCellIds = new HashSet<int>();

            foreach (var kv in counts)
            {
                var type = kv.Key;
                int remaining = kv.Value;
                foreach (var cell in candidates)
                {
                    if (remaining <= 0) break;
                    if (occupiedCellIds.Contains(cell.Id)) continue;

                    var poi = MakePoi(type, cell);
                    pois.Add(poi);
                    occupiedCellIds.Add(cell.Id);
                    SpawnMarker(poi);
                    remaining--;
                }
            }

            OnPoisChanged?.Invoke();
        }

        /// <summary>Adds a single POI of the given type to a random unoccupied non-ocean cell.</summary>
        public void AddOne(PoiType type)
        {
            if (mapRenderer?.Cells == null) return;

            var occupied = new HashSet<int>(pois.Select(p => p.OwnerCellId));
            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean && !occupied.Contains(c.Id))
                .ToList();

            if (candidates.Count == 0) return;

            var cell = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            var poi = MakePoi(type, cell);
            pois.Add(poi);
            SpawnMarker(poi);
            OnPoisChanged?.Invoke();
        }

        // ── CRUD ───────────────────────────────────────────────────────────────

        public void DeletePoi(string id)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            pois.Remove(poi);
            DestroyMarker(id);
            if (selectedPoiId == id) { selectedPoiId = null; OnSelectionChanged?.Invoke(null); }
            OnPoisChanged?.Invoke();
        }

        public void ClearAll()
        {
            foreach (var id in markers.Keys.ToList())
                DestroyMarker(id);
            pois.Clear();
            selectedPoiId = null;
            OnSelectionChanged?.Invoke(null);
            OnPoisChanged?.Invoke();
        }

        public void UpdatePoiName(string id, string name)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.Name = name;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void UpdatePoiDescription(string id, string desc)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi != null) poi.Description = desc;
        }

        public void UpdatePoiSpritePath(string id, string path)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.CustomSpritePath = path;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void MovePoiTo(string id, System.Numerics.Vector2 pos, int newOwnerCellId)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.WorldPosition = pos;
            poi.OwnerCellId = newOwnerCellId;
            if (markers.TryGetValue(id, out var m)) m.SetVisualPosition(pos);
        }

        // ── Selection ──────────────────────────────────────────────────────────

        public void SelectPoi(string id)
        {
            if (selectedPoiId == id) return;
            if (selectedPoiId != null && markers.TryGetValue(selectedPoiId, out var prev))
                prev.SetHighlighted(false);
            selectedPoiId = id;
            if (id != null && markers.TryGetValue(id, out var next))
                next.SetHighlighted(true);
            OnSelectionChanged?.Invoke(GetSelectedPoi());
        }

        public void DeselectAll() => SelectPoi(null);

        /// <summary>Returns the PoiMarkerView for the given POI id, or null if not found.</summary>
        public PoiMarkerView GetMarkerView(string id)
        {
            if (id == null) return null;
            markers.TryGetValue(id, out var m);
            return m;
        }

        // ── Internals ──────────────────────────────────────────────────────────

        PoiData MakePoi(PoiType type, VoronoiCell cell) => new PoiData
        {
            Type = type,
            Name = DefaultName(type),
            OwnerCellId = cell.Id,
            WorldPosition = new System.Numerics.Vector2(cell.Site.X, cell.Site.Y),
        };

        void SpawnMarker(PoiData poi)
        {
            var go = new GameObject($"POI_{poi.Id}");
            go.transform.SetParent(poiContainer, false);
            var view = go.AddComponent<PoiMarkerView>();
            view.Initialize(poi, poiYOffset, iconWorldSize);
            markers[poi.Id] = view;
        }

        void DestroyMarker(string id)
        {
            if (!markers.TryGetValue(id, out var m)) return;
            if (m != null) Destroy(m.gameObject);
            markers.Remove(id);
        }

        static string DefaultName(PoiType type)
        {
            switch (type)
            {
                case PoiType.City:     return "Город";
                case PoiType.Ruin:     return "Руины";
                case PoiType.Dungeon:  return "Подземелье";
                case PoiType.Fortress: return "Крепость";
                default: return type.ToString();
            }
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: POI Generation")]
        public void SelfTestPoiGeneration()
        {
            // Build fixture: 5 fake non-ocean cells with sequential IDs and Sites.
            var fixtureCells = new List<VoronoiCell>();
            for (int i = 0; i < 5; i++)
                fixtureCells.Add(new VoronoiCell(i, new System.Numerics.Vector2(i * 10f, 0f))
                    { IsOcean = false });

            // Directly exercise the placement logic (without WorldMapRenderer).
            var occupiedCellIds = new HashSet<int>();
            var placed = new List<PoiData>();
            var counts = new Dictionary<PoiType, int>
            {
                { PoiType.City,    2 },
                { PoiType.Dungeon, 1 },
            };

            var candidates = fixtureCells.OrderBy(_ => Guid.NewGuid()).ToList();
            foreach (var kv in counts)
            {
                int rem = kv.Value;
                foreach (var cell in candidates)
                {
                    if (rem <= 0) break;
                    if (occupiedCellIds.Contains(cell.Id)) continue;
                    placed.Add(MakePoi(kv.Key, cell));
                    occupiedCellIds.Add(cell.Id);
                    rem--;
                }
            }

            bool countOk = placed.Count == 3;
            bool cellsValid = placed.All(p => p.OwnerCellId >= 0 && p.OwnerCellId < 5);
            bool noDuplicates = placed.Select(p => p.OwnerCellId).Distinct().Count() == placed.Count;

            bool ok = countOk && cellsValid && noDuplicates;
            Debug.Log(ok
                ? "Self-Test POI Generation: PASS"
                : $"Self-Test POI Generation: FAIL (count={placed.Count} wantOk={countOk}, cellsValid={cellsValid}, noDuplicates={noDuplicates})");
        }

        [ContextMenu("Self-Test: POI Placeholder Factory")]
        public void SelfTestPlaceholderFactory()
        {
            bool ok = true;
            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
            {
                var sprite = PoiPlaceholderFactory.GetPlaceholder(type);
                bool spriteOk = sprite != null
                    && sprite.texture.width == 32
                    && sprite.texture.height == 32;
                if (!spriteOk)
                {
                    Debug.Log($"Self-Test POI Placeholder Factory: FAIL — {type} sprite invalid");
                    ok = false;
                }
            }
            if (ok) Debug.Log("Self-Test POI Placeholder Factory: PASS");
        }
    }
}
