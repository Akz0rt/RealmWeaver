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
    }

    /// <summary>
    /// Reads/writes a full project (map cells + POIs + notes document) to a single JSON
    /// file. Pure data-layer code (no MonoBehaviour dependency) so it can be exercised
    /// directly by ProjectSerializerSelfTests without a running scene.
    /// </summary>
    public static class ProjectSerializer
    {
        public const int CurrentFormatVersion = 2;

        static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new CanvasObjectDataConverter() }
        };

        public static void Save(string path, GenerationParams genParams, IReadOnlyList<VoronoiCell> cells,
                                 IReadOnlyList<PoiData> pois, NotesDocument notes,
                                 IReadOnlyList<RegionLabelData> regionLabels)
        {
            var data = new ProjectSaveData
            {
                FormatVersion = CurrentFormatVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                GenerationParams = genParams,
                Cells = new List<VoronoiCell>(cells),
                Pois = new List<PoiData>(pois),
                Notes = notes,
                RegionLabels = new List<RegionLabelData>(regionLabels)
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
                RegionLabels = data.RegionLabels ?? new List<RegionLabelData>()
            };

            if (data.FormatVersion > CurrentFormatVersion)
                result.WarningMessage = "Файл сохранён более новой версией инструмента — часть данных может не загрузиться.";

            // Legacy migration (v1): cells stored a per-cell BiomeOverride (now removed). Convert any land-biome
            // override to the canonical climate levels that produce it; water/invalid values are ignored (the cell
            // reclassifies from its stored climate). Best-effort — never fail the load over migration.
            if (data.FormatVersion < CurrentFormatVersion)
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

            return result;
        }
    }
}
