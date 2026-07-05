# StandaloneFileBrowser — Windows Standalone Implementation — Design

**Date:** 2026-07-05
**Status:** Approved, ready for implementation
**Branch:** implement off `main` (project has no separate feature branches/worktrees)

---

## Goal

`StandaloneFileBrowser` (the vendored `SFB` native file-dialog wrapper, `Assets/WorldGen/Notes/Rendering/StandaloneFileBrowser/`) only has an Editor-mode implementation today. In a real standalone Windows build — which now exists for the first time thanks to the installer/auto-update feature — `_platformWrapper` stays `null`, so every Save/Open/icon-picker click throws an uncaught `NullReferenceException`: no dialog, no error, nothing visibly happens. Caught during real-machine testing of the v0.0.2 installed build.

This adds the missing Windows-standalone implementation via direct Win32 P/Invoke, so Save/Open/icon-picker work in the shipped `.exe`, not just in the Editor.

---

## Scope

**In scope:**
- `StandaloneFileBrowserWindows.cs` implementing `IStandaloneFileBrowser` for `UNITY_STANDALONE_WIN && !UNITY_EDITOR`, covering all 6 interface methods (`OpenFilePanel`, `OpenFolderPanel`, `SaveFilePanel`, and their 3 async variants).
- Fixing `StandaloneFileBrowser.cs`'s static constructor to actually select this new class outside the Editor (see [Bug in the static constructor](#bug-in-the-static-constructor)).

**Out of scope:**
- Multi-select (`multiselect: true`) — no call site in this project passes `true`; the parameter is accepted for interface conformance but Windows multi-select isn't implemented.
- macOS/Linux implementations — this project is Windows-only throughout (matches every other platform-specific decision made so far: the installer, the auto-updater, `StandaloneFileBrowser`'s own original design).
- Any UI/UX change to the callers (`ProjectMenuBar`, `PoiEditPanel`, `ImagePicker`) — they already handle empty results as "cancelled" correctly; nothing there needs to change.

---

## Bug in the static constructor

```csharp
static StandaloneFileBrowser() {
#if UNITY_EDITOR
    _platformWrapper = new StandaloneFileBrowserEditor();
#endif
}
```

This needs an `#elif UNITY_STANDALONE_WIN` branch selecting the new class — and the order matters. `UNITY_STANDALONE_WIN` is defined by Unity whenever the active build target is Windows standalone, **including while running inside the Editor** with that target selected. Only `UNITY_EDITOR` distinguishes "running in the Editor" from "running as a real standalone build." So `UNITY_EDITOR` must be checked first:

```csharp
static StandaloneFileBrowser() {
#if UNITY_EDITOR
    _platformWrapper = new StandaloneFileBrowserEditor();
#elif UNITY_STANDALONE_WIN
    _platformWrapper = new StandaloneFileBrowserWindows();
#endif
}
```

(This is exactly the ordering mistake the original vendoring comment in `StandaloneFileBrowser.cs` describes — upstream's `#elif` chain put `UNITY_STANDALONE_WIN` ahead of `UNITY_EDITOR`, so the previous developer stripped the Windows branch out entirely rather than fixing the order. Now that a real Windows implementation exists, fixing the order is simpler than continuing to omit it.)

---

## Implementation: `StandaloneFileBrowserWindows.cs`

Win32 Common Dialogs via P/Invoke — `comdlg32.dll`'s `GetOpenFileNameW`/`GetSaveFileNameW` for files, `shell32.dll`'s `SHBrowseForFolder` for folders. On Windows 10/11 these classic APIs already render the modern Explorer-style dialog (sidebar, search box) for basic usage — there's no visual difference from the newer COM-based `IFileOpenDialog`, and P/Invoke-ing the classic API needs no COM interop, matching this project's preference for minimal/simple native interop.

**Dialog ownership:** every call passes `GetActiveWindow()` as the owner `HWND`, so the dialog is modal to the game's window (won't get lost behind it on Alt+Tab).

**`OpenFilePanel`** — builds an `OPENFILENAMEW` struct, sets `lpstrFilter` from the `ExtensionFilter[]` (each filter's `Name` + `Extensions` joined into the Win32 double-null-terminated filter string format), calls `GetOpenFileNameW`. Returns `new[] { path }` on success, `new string[0]` on cancel (`GetOpenFileNameW` returns `false` — covers both "user cancelled" and any dialog-level error; this project doesn't need to distinguish the two, matching the Editor implementation's behavior).

**`SaveFilePanel`** — same struct, `GetSaveFileNameW`, with `lpstrDefExt` set to the first filter's first extension so Windows auto-appends it if the user doesn't type one (matches native Explorer save-dialog behavior, and preserves the existing call sites' assumption that the returned path already has the right extension). Returns the path, or `""` on cancel.

**`OpenFolderPanel`** — `SHBrowseForFolder` with `BIF_RETURNONLYFSDIRS`, converting the returned `PIDL` to a path via `SHGetPathFromIDList`. Returns `new[] { path }` or `new string[0]` on cancel. (Not currently called anywhere in this project, but implemented properly rather than stubbed — it's part of the public interface, and the marginal cost of a real implementation over a throwing stub is small.)

**Async variants** — synchronous call immediately followed by `cb.Invoke(result)`, identical in spirit to `StandaloneFileBrowserEditor`'s async methods. No real threading/async needed since nothing in this project calls them yet, and Windows file dialogs are inherently blocking/modal anyway.

**Multiselect parameter** — accepted (interface requires it) but ignored; always behaves as single-select. No call site passes `true`.

---

## Error handling

Any P/Invoke failure (dialog fails to open, `SHBrowseForFolder`/`GetOpenFileNameW`/`GetSaveFileNameW` return a failure code) is treated identically to a user cancelling — empty array / empty string, no exception, no dialog. This matches what every existing call site already expects (`ProjectMenuBar.DoSave/DoSaveAs/DoOpen`, `PoiEditPanel`'s icon picker, `ImagePicker`), so no caller-side changes are needed.

---

## Testing

No automated test runner in this project (established convention). Verification is manual, against a real standalone build (this bug only reproduces outside the Editor):
- Build (or use the next tagged release's installed `.exe`) and confirm: **Файл → Сохранить как…** opens a native Save dialog, saves a `.dndproj` file, confirms the extension is appended if omitted.
- **Файл → Открыть…** opens a native Open dialog filtered to `.dndproj`, loads the picked file.
- POI edit panel's "Сменить иконку" and the notes editor's image picker both open a native Open dialog filtered to image extensions, and successfully load the picked image.
- Cancelling any of the above dialogs (Escape or the Cancel button) does nothing destructive — same as today's Editor-mode behavior.

---

## Out of Scope

- Multi-select support.
- macOS/Linux platform implementations.
- Any behavior change to the calling code (`ProjectMenuBar`, `PoiEditPanel`, `ImagePicker`).
