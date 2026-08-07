using System.IO;
using System.Text;
using SFB;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Открытие и сохранение .dndchar. В сцене игрока НЕТ и не должно появиться ни одной
    /// ссылки на .dndproj: дверь между подготовкой игрока и подготовкой мастера односторонняя.</summary>
    public static class SheetFileService
    {
        static readonly ExtensionFilter[] Filters = { new ExtensionFilter("Лист персонажа", "dndchar") };

        public static CharacterFile LoadFrom(string path)
        {
            var file = CharacterSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
            RecentSheetsList.Push(path);
            return file;
        }

        public static (CharacterFile file, string path) Open()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Открыть лист персонажа", "", Filters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return (null, null);
            return (LoadFrom(paths[0]), paths[0]);
        }

        public static void Save(CharacterFile file, string path)
        {
            File.WriteAllText(path, CharacterSerializer.ToJson(file), Encoding.UTF8);
            RecentSheetsList.Push(path);
        }

        public static string SaveAs(CharacterFile file)
        {
            string suggested = string.IsNullOrWhiteSpace(file.Name) ? "Персонаж" : file.Name;
            string path = StandaloneFileBrowser.SaveFilePanel("Сохранить лист персонажа", "", suggested, "dndchar");
            if (string.IsNullOrEmpty(path)) return null;
            Save(file, path);
            return path;
        }
    }
}
