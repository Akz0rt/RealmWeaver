using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WorldGen.Generation;
using WorldGen.Notes.Data;

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
    }

    /// <summary>
    /// Reads/writes a full project (map cells + POIs + notes document) to a single JSON
    /// file. Pure data-layer code (no MonoBehaviour dependency) so it can be exercised
    /// directly by ProjectSerializerSelfTests without a running scene.
    /// </summary>
    public static class ProjectSerializer
    {
        public const int CurrentFormatVersion = 1;

        static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new CanvasObjectDataConverter() }
        };

        public static void Save(string path, GenerationParams genParams, IReadOnlyList<VoronoiCell> cells,
                                 IReadOnlyList<PoiData> pois, NotesDocument notes)
        {
            var data = new ProjectSaveData
            {
                FormatVersion = CurrentFormatVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                GenerationParams = genParams,
                Cells = new List<VoronoiCell>(cells),
                Pois = new List<PoiData>(pois),
                Notes = notes
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
                Notes = data.Notes ?? new NotesDocument()
            };

            if (data.FormatVersion > CurrentFormatVersion)
                result.WarningMessage = "Файл сохранён более новой версией инструмента — часть данных может не загрузиться.";

            return result;
        }
    }
}
