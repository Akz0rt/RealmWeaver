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
        [Tooltip("Base icon side length in world units, before per-POI IconScale is applied.")]
        public float iconWorldSize = 36f;
        [Tooltip("Base label character size, before per-POI LabelScale is applied.")]
        public float labelCharacterSize = 1.5f;

        readonly List<PoiData> pois = new List<PoiData>();
        readonly Dictionary<string, PoiMarkerView> markers = new Dictionary<string, PoiMarkerView>();
        Transform poiContainer;
        string selectedPoiId;

        public event Action<PoiData> OnSelectionChanged;
        public event Action OnPoisChanged;
        public event Action<bool> OnPlacementArmedChanged;

        /// <summary>true — «+ Добавить точку» взведён: следующий клик по пустой клетке карты создаст точку.</summary>
        public bool PlacementArmed { get; private set; }

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

        /// <summary>Clears existing POIs and generates count new ones, all typed Unknown. DM assigns types afterwards.</summary>
        public void GenerateAll(int count)
        {
            ClearAll();
            if (mapRenderer?.Cells == null) return;

            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean)
                .OrderBy(_ => Guid.NewGuid())
                .Take(count)
                .ToList();

            foreach (var cell in candidates)
            {
                var poi = MakePoi(PoiType.Unknown, cell);
                pois.Add(poi);
                SpawnMarker(poi);
            }

            OnPoisChanged?.Invoke();
        }

        /// <summary>Adds a single typeless POI to a random unoccupied non-ocean cell.</summary>
        public void AddOne()
        {
            if (mapRenderer?.Cells == null) return;

            var occupied = new HashSet<int>(pois.Select(p => p.OwnerCellId));
            var candidates = mapRenderer.Cells
                .Where(c => !c.IsOcean && !occupied.Contains(c.Id))
                .ToList();

            if (candidates.Count == 0) return;

            var cell = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            var poi = MakePoi(PoiType.Unknown, cell);
            pois.Add(poi);
            SpawnMarker(poi);
            OnPoisChanged?.Invoke();
        }

        /// <summary>Replaces all current POIs with a previously-saved list — used when loading a project.</summary>
        public void LoadPois(List<PoiData> loadedPois)
        {
            ClearAll();
            foreach (var poi in loadedPois)
            {
                pois.Add(poi);
                SpawnMarker(poi);
            }
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

        public void UpdatePoiType(string id, PoiType type)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.Type = type;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void UpdatePoiIconBytes(string id, byte[] bytes, string displayPath)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.CustomIconBytes = bytes;
            poi.CustomSpritePath = displayPath;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void UpdatePoiIconScale(string id, float scale)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.IconScale = scale;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void UpdatePoiLabelScale(string id, float scale)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null) return;
            poi.LabelScale = scale;
            if (markers.TryGetValue(id, out var m)) m.Refresh();
        }

        public void SnapOwnerCellToPosition(string id)
        {
            var poi = pois.FirstOrDefault(p => p.Id == id);
            if (poi == null || mapRenderer?.Cells == null) return;
            float bestDistSq = float.MaxValue;
            int bestCell = poi.OwnerCellId;
            foreach (var cell in mapRenderer.Cells)
            {
                float dx = cell.Site.X - poi.WorldPosition.X;
                float dy = cell.Site.Y - poi.WorldPosition.Y;
                float dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq) { bestDistSq = dSq; bestCell = cell.Id; }
            }
            poi.OwnerCellId = bestCell;
            OnPoisChanged?.Invoke();
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

        // ── Placement arming (arm-then-click на карте) ───────────────────────────

        public void ArmPlacement()
        {
            if (PlacementArmed) return;
            PlacementArmed = true;
            OnPlacementArmedChanged?.Invoke(true);
        }

        public void DisarmPlacement()
        {
            if (!PlacementArmed) return;
            PlacementArmed = false;
            OnPlacementArmedChanged?.Invoke(false);
        }

        public void TogglePlacement()
        {
            if (PlacementArmed) DisarmPlacement();
            else ArmPlacement();
        }

        /// <summary>
        /// Создаёт точку в конкретной позиции карты (arm-then-click), владелец — ближайшая
        /// не-океаническая клетка, и выделяет её. null, если карта пуста.
        /// </summary>
        public PoiData AddAt(System.Numerics.Vector2 pos)
        {
            if (mapRenderer?.Cells == null) return null;

            VoronoiCell best = null;
            float bestSq = float.MaxValue;
            foreach (var c in mapRenderer.Cells)
            {
                if (c.IsOcean) continue;
                float dx = c.Site.X - pos.X, dy = c.Site.Y - pos.Y;
                float d = dx * dx + dy * dy;
                if (d < bestSq) { bestSq = d; best = c; }
            }
            if (best == null) return null;

            var poi = new PoiData
            {
                Type = PoiType.Unknown,
                Name = DefaultName(PoiType.Unknown),
                OwnerCellId = best.Id,
                WorldPosition = pos,
            };
            pois.Add(poi);
            SpawnMarker(poi);
            OnPoisChanged?.Invoke();
            SelectPoi(poi.Id);
            return poi;
        }

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
            view.Initialize(poi, poiYOffset, iconWorldSize, labelCharacterSize);
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
                case PoiType.Unknown:  return "Точка интереса";
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
            const int wantCount = 3;
            var placed = fixtureCells
                .OrderBy(_ => Guid.NewGuid())
                .Take(wantCount)
                .Select(cell => MakePoi(PoiType.Unknown, cell))
                .ToList();

            bool countOk = placed.Count == wantCount;
            bool cellsValid = placed.All(p => p.OwnerCellId >= 0 && p.OwnerCellId < 5);
            bool noDuplicates = placed.Select(p => p.OwnerCellId).Distinct().Count() == placed.Count;

            bool ok = countOk && cellsValid && noDuplicates;
            Debug.Log(ok
                ? "Self-Test POI Generation: PASS"
                : $"Self-Test POI Generation: FAIL (count={placed.Count} wantOk={countOk}, cellsValid={cellsValid}, noDuplicates={noDuplicates})");
        }

        [ContextMenu("Self-Test: POI Load")]
        public void SelfTestLoadPois()
        {
            var loaded = new List<PoiData>
            {
                new PoiData { Type = PoiType.City, Name = "A", OwnerCellId = 0 },
                new PoiData { Type = PoiType.Ruin, Name = "B", OwnerCellId = 1 }
            };

            LoadPois(loaded);

            bool ok = GetAllPois().Count == 2
                && GetAllPois().Any(p => p.Name == "A")
                && GetAllPois().Any(p => p.Name == "B");

            ClearAll(); // leave the scene as it was before this test ran

            Debug.Log(ok
                ? "Self-Test POI Load: PASS"
                : $"Self-Test POI Load: FAIL (count={loaded.Count})");
        }

        [ContextMenu("Self-Test: POI Placeholder Factory")]
        public void SelfTestPlaceholderFactory()
        {
            bool ok = true;
            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
            {
                var sprite = PoiPlaceholderFactory.GetPlaceholder(type);
                bool spriteOk = sprite != null
                    && sprite.texture.width == 64
                    && sprite.texture.height == 64;
                // Иконка должна быть ОДНИМ кэшированным экземпляром (её делят маркеры, список и
                // панель редактирования) — проверяем идентичность ссылки при повторном запросе.
                bool sameInstance = ReferenceEquals(sprite, PoiPlaceholderFactory.GetPlaceholder(type));
                if (!spriteOk || !sameInstance)
                {
                    Debug.Log($"Self-Test POI Placeholder Factory: FAIL — {type} (valid={spriteOk}, cached={sameInstance})");
                    ok = false;
                }
            }
            if (ok) Debug.Log("Self-Test POI Placeholder Factory: PASS");
        }
    }
}
