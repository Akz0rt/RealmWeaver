using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Owns the editable region-label list. Auto-seeds from biome-family patches on world
    /// generation, then labels are user-owned (rename/move/delete/add) and saved in the project.</summary>
    public class RegionLabelManager : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;

        readonly List<RegionLabelData> labels = new List<RegionLabelData>();
        string selectedId;
        int nameSalt;   // bumped by RerollNames() so "Пересоздать названия" yields fresh names; reset per world gen

        public event Action OnLabelsChanged;
        public event Action<RegionLabelData> OnSelectionChanged;

        void Awake()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated += HandleWorldRegenerated;
        }

        void OnDestroy()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated -= HandleWorldRegenerated;
        }

        // Auto-seed on world (re)generation: reset the reroll salt so a freshly generated map gets
        // its stable base names (deterministic per seed).
        void HandleWorldRegenerated() { nameSalt = 0; SeedFromCells(); }

        /// <summary>"Пересоздать названия": bump the salt so the names come out DIFFERENT, then reseed at
        /// the current density (which also discards manual edits — the list is fully replaced).</summary>
        public void RerollNames() { nameSalt++; SeedFromCells(); }

        public IReadOnlyList<RegionLabelData> GetAll() => labels;
        public RegionLabelData GetSelected() =>
            selectedId != null ? labels.FirstOrDefault(l => l.Id == selectedId) : null;

        /// <summary>Runs the placer over the current map and REPLACES the list (fresh seed per generation).</summary>
        public void SeedFromCells()
        {
            if (mapRenderer == null || mapRenderer.Cells == null) return;
            var seeded = RegionLabelPlacer.Place(mapRenderer.Cells, mapRenderer.NearestLookup,
                mapRenderer.mapWidth, mapRenderer.mapHeight,
                unchecked(mapRenderer.seed + nameSalt), mapRenderer.labelDensity);
            labels.Clear();
            labels.AddRange(seeded);
            selectedId = null;
            OnLabelsChanged?.Invoke();
        }

        public void LoadLabels(List<RegionLabelData> loaded)
        {
            labels.Clear();
            if (loaded != null) labels.AddRange(loaded);
            selectedId = null;
            OnLabelsChanged?.Invoke();
        }

        public void ClearAll()
        {
            labels.Clear(); selectedId = null; OnLabelsChanged?.Invoke();
        }

        public string AddLabel(System.Numerics.Vector2 worldPos, string text)
        {
            var d = new RegionLabelData { Text = string.IsNullOrEmpty(text) ? "Новый Край" : text, WorldPosition = worldPos };
            labels.Add(d);
            OnLabelsChanged?.Invoke();
            SelectLabel(d.Id);
            return d.Id;
        }

        public void DeleteLabel(string id)
        {
            int n = labels.RemoveAll(l => l.Id == id);
            if (n > 0) { if (selectedId == id) selectedId = null; OnLabelsChanged?.Invoke(); }
        }

        public void RenameLabel(string id, string text)
        {
            var d = labels.FirstOrDefault(l => l.Id == id);
            if (d != null) { d.Text = text; OnLabelsChanged?.Invoke(); }
        }

        public void MoveLabel(string id, System.Numerics.Vector2 worldPos)
        {
            var d = labels.FirstOrDefault(l => l.Id == id);
            if (d != null) { d.WorldPosition = worldPos; OnLabelsChanged?.Invoke(); }
        }

        public void SelectLabel(string id)
        {
            selectedId = id;
            OnSelectionChanged?.Invoke(GetSelected());
        }

        public void DeselectAll()
        {
            selectedId = null;
            OnSelectionChanged?.Invoke(null);
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Region Label CRUD")]
        public void SelfTestCrud()
        {
            // Start from an empty list, regardless of scene state, so the test is repeatable.
            ClearAll();

            bool ok = true;

            // Add → count 1 + auto-selected.
            var pos1 = new System.Numerics.Vector2(10f, 20f);
            string id = AddLabel(pos1, "X");
            bool addOk = GetAll().Count == 1 && GetSelected() != null && GetSelected().Id == id;
            ok &= addOk;

            // Rename → Text changed.
            RenameLabel(id, "Y");
            bool renameOk = GetAll().Count == 1 && GetAll()[0].Text == "Y";
            ok &= renameOk;

            // Move → WorldPosition changed.
            var pos2 = new System.Numerics.Vector2(30f, 40f);
            MoveLabel(id, pos2);
            var moved = GetAll().FirstOrDefault(l => l.Id == id);
            bool moveOk = moved != null && moved.WorldPosition.X == pos2.X && moved.WorldPosition.Y == pos2.Y;
            ok &= moveOk;

            // Delete → count 0 + selection cleared.
            DeleteLabel(id);
            bool deleteOk = GetAll().Count == 0 && GetSelected() == null;
            ok &= deleteOk;

            Debug.Log(ok
                ? "Self-Test Region Label CRUD: PASS"
                : $"Self-Test Region Label CRUD: FAIL (add={addOk}, rename={renameOk}, move={moveOk}, delete={deleteOk})");
        }
    }
}
