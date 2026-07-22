using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Rendering.RegionLabels;

namespace WorldGen.Persistence
{
    public class ProjectLoadResult
    {
        public bool Success;
        public string ErrorMessage;    // set only when Success == false
        public string WarningMessage;  // set when Success == true but something is worth flagging (e.g. newer format version)
        public GenerationParams GenerationParams;
        public List<VoronoiCell> Cells;
        public List<PoiData> Pois;
        public NotesDocument Notes;
        public List<RegionLabelData> RegionLabels;
        public List<RegionData> Regions;
        public List<InteriorData> Dungeons;
    }

    /// <summary>
    /// Reads/writes a full project (map cells + POIs + notes document) to a single JSON
    /// file. Pure data-layer code (no MonoBehaviour dependency) so it can be exercised
    /// directly by ProjectSerializerSelfTests without a running scene.
    /// </summary>
    public static class ProjectSerializer
    {
        public const int CurrentFormatVersion = 9;   // 9: Room.Preview (settlement building preview image)
                                                     // + InteriorFloor.Wall (settlement wall contour).
                                                     // Reading v8 needs no migration — both keys are
                                                     // absent in a v8 file and deserialize to null.
                                                     // Bumped anyway so a v0.3.7 build WARNS on open
                                                     // instead of silently dropping every settlement wall
                                                     // and preview image on its next save.

        static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new CanvasObjectDataConverter(), new ColorJsonConverter() }
        };

        public static void Save(string path, GenerationParams genParams, IReadOnlyList<VoronoiCell> cells,
                                 IReadOnlyList<PoiData> pois, NotesDocument notes,
                                 IReadOnlyList<RegionLabelData> regionLabels,
                                 IReadOnlyList<RegionData> regions,
                                 IReadOnlyList<InteriorData> dungeons)
        {
            var data = new ProjectSaveData
            {
                FormatVersion = CurrentFormatVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                GenerationParams = genParams,
                Cells = new List<VoronoiCell>(cells),
                Pois = new List<PoiData>(pois),
                Notes = notes,
                RegionLabels = new List<RegionLabelData>(regionLabels),
                Regions = new List<RegionData>(regions ?? new List<RegionData>()),
                Dungeons = new List<InteriorData>(dungeons ?? new List<InteriorData>())
            };

            string json = JsonConvert.SerializeObject(data, BuildSettings());
            File.WriteAllText(path, json);
        }

        public static ProjectLoadResult Load(string path)
        {
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return new ProjectLoadResult { Success = false, ErrorMessage = $"Не удалось прочитать файл: {ex.Message}" };
            }

            ProjectSaveData data;
            try
            {
                data = JsonConvert.DeserializeObject<ProjectSaveData>(json, BuildSettings());
            }
            catch (Exception ex)
            {
                return new ProjectLoadResult { Success = false, ErrorMessage = $"Файл повреждён или имеет неверный формат: {ex.Message}" };
            }

            if (data == null)
                return new ProjectLoadResult { Success = false, ErrorMessage = "Файл повреждён или имеет неверный формат." };

            var result = new ProjectLoadResult
            {
                Success = true,
                GenerationParams = data.GenerationParams,
                Cells = data.Cells ?? new List<VoronoiCell>(),
                Pois = data.Pois ?? new List<PoiData>(),
                Notes = data.Notes ?? new NotesDocument(),
                RegionLabels = data.RegionLabels ?? new List<RegionLabelData>(),
                Regions = data.Regions ?? new List<RegionData>(),
                // FormatVersion 5 replaced the tile dungeon with a room-graph. Older tile dungeons cannot be
                // migrated (a graph can't be recovered from the old floor "blob"), so they are dropped; the rest
                // of the project loads normally.
                Dungeons = data.FormatVersion >= 5
                    ? (data.Dungeons ?? new List<InteriorData>())
                    : new List<InteriorData>()
            };

            foreach (var d in result.Dungeons) RoomSizing.ApplyDefaults(d);

            if (data.FormatVersion > CurrentFormatVersion)
                result.WarningMessage = "Файл сохранён более новой версией инструмента — часть данных может не загрузиться.";

            // Legacy migration (v1 ONLY): cells stored a per-cell BiomeOverride, removed in v2. Convert any
            // land-biome override to the canonical climate levels that produce it; water/invalid values are
            // ignored (the cell reclassifies from its stored climate). Best-effort — never fail the load
            // over migration.
            //
            // Guard on the literal 2, NOT CurrentFormatVersion: every line inside this block targets a field
            // that has not existed since v2, so gating it on "older than today's format" makes EVERY v(N-1)
            // file re-run a full second JObject.Parse of the whole (possibly multi-megabyte) save on every
            // load, for a migration that then finds no BiomeOverride key and does nothing. That is exactly
            // what happened once CurrentFormatVersion reached 8: every v7 file (i.e. every project any user
            // had) paid the cost on every open. Do not "simplify" this back to CurrentFormatVersion — the
            // cost returns the next time the format version is bumped.
            if (data.FormatVersion < 2)
            {
                try
                {
                    var cellsToken = JObject.Parse(json)["Cells"] as JArray;
                    if (cellsToken != null)
                    {
                        for (int i = 0; i < cellsToken.Count && i < result.Cells.Count; i++)
                        {
                            var bo = cellsToken[i]?["BiomeOverride"];
                            if (bo == null || bo.Type == JTokenType.Null) continue;
                            var rep = BiomeMatrix.RepresentativeClimate((Biome)bo.Value<int>());
                            if (rep.HasValue)
                            {
                                result.Cells[i].TemperatureOverride = BiomeMatrix.LevelCenter(rep.Value.t);
                                result.Cells[i].MoistureOverride    = BiomeMatrix.LevelCenter(rep.Value.m);
                            }
                        }
                    }
                }
                catch { /* migration is best-effort */ }
            }

            // Legacy migration (pre-v3): regions used to be pure cell membership (VoronoiCell.RegionId)
            // with no RegionData metadata (name/colour) at all - RegionData/RegionManager didn't exist
            // yet. If the file carries no region metadata but its cells still reference region ids,
            // synthesize a default RegionData per distinct id so borders/fill/names work after loading
            // (same fantasy-name + palette-colour scheme GenerateRegionsOnly uses for freshly-generated
            // regions). Keyed off the data itself (empty Regions + assigned cells), not FormatVersion,
            // so it also recovers a would-be-inconsistent v3 file. Best-effort - never fail the load.
            if (result.Regions.Count == 0)
            {
                try
                {
                    var distinctIds = new SortedSet<int>();
                    foreach (var c in result.Cells)
                        if (c.RegionId >= 0) distinctIds.Add(c.RegionId);

                    if (distinctIds.Count > 0)
                    {
                        int seed = data.GenerationParams?.Seed ?? 0;
                        foreach (int id in distinctIds)
                        {
                            string name = RegionLabelNames.ContinentName(seed, id);
                            result.Regions.Add(new RegionData(id, name, WorldGen.Rendering.RegionColorPalette.GetRegionColor(id)));
                        }
                    }
                }
                catch { /* migration is best-effort */ }
            }

            return result;
        }
    }
}
