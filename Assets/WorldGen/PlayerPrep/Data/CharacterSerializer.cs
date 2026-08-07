using Newtonsoft.Json;

namespace WorldGen.PlayerPrep.Data
{
    public class CharacterFormatException : System.Exception
    {
        public CharacterFormatException(string message) : base(message) { }
    }

    /// <summary>Чтение и запись .dndchar. Версия формата — СВОЯ, не связанная с .dndproj: иначе
    /// каждая арка мастера двигала бы версию файлов игроков.</summary>
    public static class CharacterSerializer
    {
        public const int CurrentFormatVersion = 1;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public static string ToJson(CharacterFile file)
        {
            file.FormatVersion = CurrentFormatVersion;
            return JsonConvert.SerializeObject(file, Settings);
        }

        public static CharacterFile FromJson(string json)
        {
            var file = JsonConvert.DeserializeObject<CharacterFile>(json, Settings);
            if (file == null) throw new CharacterFormatException("файл не разобрался как лист персонажа");
            if (file.FormatVersion > CurrentFormatVersion)
                throw new CharacterFormatException(
                    $"файл сохранён более новой версией программы (формат {file.FormatVersion}, "
                    + $"эта программа знает {CurrentFormatVersion})");
            return file;
        }
    }
}
