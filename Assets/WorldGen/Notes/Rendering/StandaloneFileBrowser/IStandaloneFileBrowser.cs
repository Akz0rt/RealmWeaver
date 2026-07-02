// Vendored from https://github.com/gkngkc/UnityStandaloneFileBrowser (MIT license).
// This repo has no package.json, so it can't be added as a git UPM dependency —
// vendored directly instead, matching this project's FastNoiseLite.cs convention.
// Only the Editor-mode implementation is included (see StandaloneFileBrowserEditor.cs);
// a real standalone build would additionally need a platform-specific implementation
// (e.g. StandaloneFileBrowserWindows.cs + its native Ookii.Dialogs/System.Windows.Forms
// plugin DLLs) not vendored here.
using System;

namespace SFB {
    public interface IStandaloneFileBrowser {
        string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect);
        string[] OpenFolderPanel(string title, string directory, bool multiselect);
        string SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions);

        void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<string[]> cb);
        void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<string[]> cb);
        void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<string> cb);
    }
}
