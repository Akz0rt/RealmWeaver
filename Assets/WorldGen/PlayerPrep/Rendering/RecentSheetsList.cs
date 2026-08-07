using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Последние открытые листы персонажей. Устроено как RecentProjectsList, но это
    /// ОТДЕЛЬНЫЙ тип с ОТДЕЛЬНЫМ ключом: файлы разные, и игрок не должен видеть в своём списке
    /// проекты мастера.</summary>
    public static class RecentSheetsList
    {
        const string PrefsKey = "PlayerPrep.RecentSheets";
        const char Delimiter = '|';   // на Windows в путях запрещён, с настоящим путём не столкнётся
        const int MaxEntries = 5;

        public static List<string> Get() =>
            PlayerPrefs.GetString(PrefsKey, "")
                .Split(Delimiter)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

        public static void Push(string path)
        {
            var list = Get();
            list.RemoveAll(p => p == path);
            list.Insert(0, path);
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            PlayerPrefs.SetString(PrefsKey, string.Join(Delimiter.ToString(), list));
        }
    }
}
