using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGen.Persistence
{
    /// <summary>Tracks the last few opened/saved project file paths, persisted via
    /// PlayerPrefs (same mechanism already used for the notes split fraction and sidebar
    /// width).</summary>
    public static class RecentProjectsList
    {
        const string PrefsKey = "Project.RecentPaths";
        const char Delimiter = '|'; // reserved on Windows paths, so it can't collide with a real path
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
            if (list.Count > MaxEntries)
                list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            PlayerPrefs.SetString(PrefsKey, string.Join(Delimiter, list));
        }
    }
}
