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
