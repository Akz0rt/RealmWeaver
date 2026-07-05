# StandaloneFileBrowser Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Save/Open/icon-picker file dialogs actually work in a real standalone Windows build of the tool — they currently throw a silent, uncaught `NullReferenceException` outside the Unity Editor.

**Architecture:** Add `StandaloneFileBrowserWindows.cs`, a Windows-standalone implementation of the existing `IStandaloneFileBrowser` interface, using direct Win32 P/Invoke (`comdlg32.dll`'s `GetOpenFileNameW`/`GetSaveFileNameW`, `shell32.dll`'s `SHBrowseForFolder`) — no COM interop, no native plugin DLLs. Fix `StandaloneFileBrowser.cs`'s static constructor to select this new class outside the Editor.

**Tech Stack:** C#, `System.Runtime.InteropServices` (P/Invoke), classic Win32 Common Dialog APIs.

## Global Constraints

- New file implements all 6 `IStandaloneFileBrowser` members (`OpenFilePanel`, `OpenFolderPanel`, `SaveFilePanel`, and their 3 async variants) — the interface requires all of them to compile.
- Multi-select is accepted as a parameter (interface conformance) but not implemented — always single-select. No caller in this project passes `multiselect: true`.
- Every dialog's owner window is `GetActiveWindow()` (modal to the game's own window).
- Any P/Invoke failure or user cancellation is treated identically: `OpenFilePanel`/`OpenFolderPanel` return `new string[0]`, `SaveFilePanel` returns `""`. No exceptions, no dialogs of our own. This matches what every existing call site (`ProjectMenuBar`, `PoiEditPanel`, `ImagePicker`) already expects.
- `StandaloneFileBrowser.cs`'s static constructor must check `UNITY_EDITOR` **before** `UNITY_STANDALONE_WIN` (the latter is also defined while running in the Editor with Windows as the active build target — checking it first would break Editor-mode file dialogs).
- Async variants (`OpenFilePanelAsync`, `OpenFolderPanelAsync`, `SaveFilePanelAsync`) just call the synchronous method and invoke the callback immediately — no real threading, matching `StandaloneFileBrowserEditor`'s existing pattern (and nothing in this project currently calls the async variants).
- Out of scope: macOS/Linux implementations, real multi-select, any change to calling code (`ProjectMenuBar.cs`, `PoiEditPanel.cs`, `ImagePicker.cs`).

---

### Task 1: Windows file/folder dialogs via Win32 P/Invoke

**Files:**
- Create: `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowserWindows.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowser.cs:26-30`

**Interfaces:**
- Consumes: `IStandaloneFileBrowser` (`Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/IStandaloneFileBrowser.cs`), `ExtensionFilter` struct (`Name : string`, `Extensions : string[]`, both already defined in `StandaloneFileBrowser.cs`).
- Produces: `StandaloneFileBrowserWindows` class, selected by `StandaloneFileBrowser`'s static constructor whenever the running process is a real Windows standalone build (not the Editor). Nothing downstream depends on this class directly — all existing callers go through the static `StandaloneFileBrowser.OpenFilePanel`/`SaveFilePanel` API, unchanged.

- [ ] **Step 1: Write the Windows implementation**

Create `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowserWindows.cs`:

```csharp
// Vendored from https://github.com/gkngkc/UnityStandaloneFileBrowser (MIT license).
// Windows-standalone implementation via direct Win32 P/Invoke (classic Common Dialogs) --
// see IStandaloneFileBrowser.cs for why this project vendors its own implementation
// instead of upstream's native plugin DLLs. On Windows 10/11 these classic APIs already
// render the modern Explorer-style dialog for basic usage -- no COM interop needed.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SFB {
    public class StandaloneFileBrowserWindows : IStandaloneFileBrowser {
        const int OFN_PATHMUSTEXIST = 0x00000800;
        const int OFN_FILEMUSTEXIST = 0x00001000;
        const int OFN_EXPLORER = 0x00080000;
        const int OFN_OVERWRITEPROMPT = 0x00000002;
        const int BIF_RETURNONLYFSDIRS = 0x00000001;
        const int BIF_NEWDIALOGSTYLE = 0x00000040;
        const int MAX_PATH = 260;
        const int FileBufferChars = 4096;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class OpenFileName {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex = 1;
            public IntPtr lpstrFile;
            public int nMaxFile = FileBufferChars;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BROWSEINFO {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            public string lpszTitle;
            public int ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

        public string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect) {
            IntPtr fileBuffer = Marshal.AllocHGlobal(FileBufferChars * sizeof(char));
            try {
                Marshal.WriteInt16(fileBuffer, 0, 0); // empty null-terminated string as initial value

                var ofn = new OpenFileName {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = BuildFilterString(extensions),
                    lpstrFile = fileBuffer,
                    lpstrInitialDir = string.IsNullOrEmpty(directory) ? null : directory,
                    lpstrTitle = title,
                    Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST
                };

                if (!GetOpenFileName(ofn)) return new string[0];

                string path = Marshal.PtrToStringUni(fileBuffer);
                return string.IsNullOrEmpty(path) ? new string[0] : new[] { path };
            }
            finally {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<string[]> cb) {
            cb.Invoke(OpenFilePanel(title, directory, extensions, multiselect));
        }

        public string[] OpenFolderPanel(string title, string directory, bool multiselect) {
            IntPtr displayName = Marshal.AllocHGlobal(MAX_PATH * sizeof(char));
            try {
                var bi = new BROWSEINFO {
                    hwndOwner = GetActiveWindow(),
                    pszDisplayName = displayName,
                    lpszTitle = title,
                    ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
                };

                IntPtr pidl = SHBrowseForFolder(ref bi);
                if (pidl == IntPtr.Zero) return new string[0];

                try {
                    var pathBuilder = new StringBuilder(MAX_PATH);
                    if (!SHGetPathFromIDList(pidl, pathBuilder)) return new string[0];
                    string path = pathBuilder.ToString();
                    return string.IsNullOrEmpty(path) ? new string[0] : new[] { path };
                }
                finally {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            finally {
                Marshal.FreeHGlobal(displayName);
            }
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<string[]> cb) {
            cb.Invoke(OpenFolderPanel(title, directory, multiselect));
        }

        public string SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions) {
            IntPtr fileBuffer = Marshal.AllocHGlobal(FileBufferChars * sizeof(char));
            try {
                byte[] initialBytes = Encoding.Unicode.GetBytes((defaultName ?? "") + "\0");
                Marshal.Copy(initialBytes, 0, fileBuffer, initialBytes.Length);

                string defExt = (extensions != null && extensions.Length > 0 && extensions[0].Extensions.Length > 0)
                    ? extensions[0].Extensions[0]
                    : null;

                var ofn = new OpenFileName {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = BuildFilterString(extensions),
                    lpstrFile = fileBuffer,
                    lpstrInitialDir = string.IsNullOrEmpty(directory) ? null : directory,
                    lpstrTitle = title,
                    lpstrDefExt = defExt,
                    Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT
                };

                if (!GetSaveFileName(ofn)) return "";

                return Marshal.PtrToStringUni(fileBuffer) ?? "";
            }
            finally {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<string> cb) {
            cb.Invoke(SaveFilePanel(title, directory, defaultName, extensions));
        }

        // Win32 filter format: pairs of (description, "*.ext1;*.ext2"), each null-terminated,
        // with one extra null terminator at the very end.
        static string BuildFilterString(ExtensionFilter[] extensions) {
            if (extensions == null || extensions.Length == 0)
                return "All Files\0*.*\0\0";

            var sb = new StringBuilder();
            foreach (var filter in extensions) {
                sb.Append(filter.Name).Append('\0');
                for (int i = 0; i < filter.Extensions.Length; i++) {
                    if (i > 0) sb.Append(';');
                    sb.Append("*.").Append(filter.Extensions[i]);
                }
                sb.Append('\0');
            }
            sb.Append('\0');
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 2: Fix the static constructor to select this class outside the Editor**

In `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowser.cs`, replace:

```csharp
        static StandaloneFileBrowser() {
#if UNITY_EDITOR
            _platformWrapper = new StandaloneFileBrowserEditor();
#endif
        }
```

with:

```csharp
        static StandaloneFileBrowser() {
            // UNITY_EDITOR must be checked before UNITY_STANDALONE_WIN -- the latter is
            // also defined while running in the Editor with Windows as the active build
            // target, so checking it first would break Editor-mode file dialogs.
#if UNITY_EDITOR
            _platformWrapper = new StandaloneFileBrowserEditor();
#elif UNITY_STANDALONE_WIN
            _platformWrapper = new StandaloneFileBrowserWindows();
#endif
        }
```

- [ ] **Step 3: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/sfb_windows_compile.log"
```

Expected: exit code 0, log ends with `Exiting batchmode successfully now!`, no `error CS` lines. (If the Unity Editor is currently open on this project interactively, this batchmode run will fail with "another Unity instance is running" — that's a pre-existing environment constraint, not a problem with this change; ask the user to close the Editor first, or defer this check.)

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D" add "Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowserWindows.cs" "Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/StandaloneFileBrowser.cs"
git -C "d:/D&D" commit -m "fix: implement Windows-standalone file dialogs via Win32 P/Invoke"
```

- [ ] **Step 5: Real-build verification (manual, whenever the next release is built)**

This can't be verified without a real standalone Windows build (the bug this fixes only reproduces outside the Editor). No separate build is needed just for this — verify it the next time a tagged release is built and installed (e.g. the v0.0.3 test already planned for the auto-update mechanism): confirm **Файл → Сохранить как…**, **Файл → Открыть…**, and the POI/notes image pickers all open a native dialog and complete successfully, instead of silently doing nothing.

---

## Self-Review Notes

- **Spec coverage:** all 6 interface methods implemented (Step 1), static constructor ordering bug fixed (Step 2), error/cancel handling matches spec (every failure path returns empty, no exceptions), `GetActiveWindow()` used for dialog ownership on every call, multi-select accepted but not implemented (matches spec's explicit non-goal). Testing section covered by Step 5, deferred to the next real build per the spec's own testing section (no automated test runner in this project).
- **Placeholder scan:** no TBD/TODO; Step 5 explicitly is NOT a placeholder — it documents that verification is bound to the next real build, which is a factual constraint from the spec (this bug is unreproducible in the Editor), not a deferred decision.
- **Type consistency:** `StandaloneFileBrowserWindows` implements `IStandaloneFileBrowser` with method signatures copied verbatim from `IStandaloneFileBrowser.cs`; `ExtensionFilter.Name`/`ExtensionFilter.Extensions` field names match the existing struct in `StandaloneFileBrowser.cs` exactly.
