# Installer & Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Windows installer for the tool (renamed **RealmWeaver**) plus an in-app update checker that notices new GitHub Releases and installs them with one click, no browser round-trip.

**Architecture:** A git-tag-triggered GitHub Actions pipeline builds the Unity project, wraps it in an Inno Setup installer, and publishes it as a GitHub Release. Inside the running game, a self-contained `UpdateChecker` component polls the GitHub Releases API on launch, shows a small banner if a newer tag exists, and — on click — downloads the installer and launches it silently with Inno Setup's built-in "close app / reinstall / restart app" flags, then quits.

**Tech Stack:** Unity 6000.3.2f1, C#, Newtonsoft.Json (already a project dependency), UnityWebRequest, Inno Setup 6, GitHub Actions, `game-ci/unity-builder`.

## Global Constraints

- Unity `productName` = `RealmWeaver`, `companyName` = `Akz0rt` (from `ProjectSettings/ProjectSettings.asset`).
- GitHub repository name: `RealmWeaver`, **public**, added as git remote `origin`.
- Inno Setup `AppId` is fixed forever once first released: `{73312D00-F8DE-4552-9DB0-16AA78F9B7E1}`. Never regenerate this.
- Inno Setup `AppMutex` is fixed forever once first released: `RealmWeaverSingleInstanceMutex`.
- Silent self-update install flags (exact, verbatim): `/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NOICONS`.
- Version source of truth: git tag `vX.Y.Z` (SemVer). `PlayerSettings.bundleVersion` is set from the tag automatically by CI (`game-ci/unity-builder`'s `versioning: Tag`) — never hand-edited.
- Runtime version comparison is a plain 3-part numeric compare (major, minor, patch) — no pre-release/build-metadata handling.
- CI pipeline: 3 jobs (`build` on `ubuntu-latest`, `package` on `windows-latest`, `release`), triggered by pushing a tag matching `v*.*.*`.
- Unity Editor version for CI: `6000.3.2f1` (must match `ProjectSettings/ProjectVersion.txt`).
- GitHub API endpoint used by the client: `https://api.github.com/repos/Akz0rt/RealmWeaver/releases/latest`.
- Update banner: top-right corner, `LegacyRuntime.ttf` (matches existing UI), `sortingOrder` strictly between `ProjectMenuBar`'s `100` and `ConfirmDialog`'s `32000` — use `500`. Dismissing hides it for the current session only (no persisted "skip this version").
- Error handling: version-check failures (network/parse) log a `Debug.LogWarning` only, no dialog. Download failures use the existing `ConfirmDialog.ShowInfo(font, message)` pattern (`Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs`).
- Out of scope (do not build): delta/incremental updates, macOS/Linux builds, "skip this version" persistence, installer code signing, retry/backoff scheduling for the version check, hand-authored changelogs (GitHub's auto-generated release notes are used as-is).
- **Execution workspace:** all work happens in the git worktree at `D:\D&D\.claude\worktrees\installer-auto-update`, on local branch `worktree-installer-auto-update` — every `git`/Unity-batchmode command in this plan targets that path, not the original `D:\D&D` checkout. That reconciliation with the original checkout's local `main` happens once, at the end, via `superpowers:finishing-a-development-branch`.
- **Pushing to origin:** the local branch name (`worktree-installer-auto-update`) never matches `origin`'s branch name (`main`), so under git's default `push.default=simple`, a bare `git push` fails with "the upstream branch of your current branch does not match the name of your current branch" — even after `-u` sets the tracking ref. Every push in this plan (Tasks 2, 4, 5) must use the explicit form: `git push origin HEAD:main`.
- **Unity license activation (corrected 2026-07-05):** `game-ci/unity-request-activation-file@v2` is deprecated ("no longer supported", rejects `unityVersion`). The current process (per https://game.ci/docs/github/activation) needs no GitHub Actions activation-request step at all — a Personal license `.ulf` is generated locally via Unity Hub (Preferences → Licenses → Add → "Get a free personal license"), found on Windows at `C:\ProgramData\Unity\Unity_lic.ulf`. Three repo secrets are required for the `build` job: `UNITY_LICENSE` (the `.ulf` file's full contents), `UNITY_EMAIL` (Unity account email), `UNITY_PASSWORD` (Unity account password) — all three, not just the license file, per current game-ci docs for Personal licenses.

---

### Task 1: Rename product identity

**Files:**
- Modify: `ProjectSettings/ProjectSettings.asset:15-16`

**Interfaces:**
- Produces: the `productName`/`companyName` values every later task (installer script, CI, GitHub repo naming) assumes.

- [ ] **Step 1: Edit the two settings**

In `ProjectSettings/ProjectSettings.asset`, change:

```yaml
  companyName: DefaultCompany
  productName: D&D
```

to:

```yaml
  companyName: Akz0rt
  productName: RealmWeaver
```

- [ ] **Step 2: Verify the project still loads cleanly**

Run (adjust the Unity path only if it differs on this machine):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D\.claude\worktrees\installer-auto-update" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/rename_check.log"
```

Expected: process exits with code 0 and the log ends with `Exiting batchmode successfully now!` (no `error CS` lines — this change touches no code, but confirms Unity still opens the project fine after a hand-edited settings file).

- [ ] **Step 3: Confirm the diff is exactly these two lines**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" diff -- ProjectSettings/ProjectSettings.asset
```

Expected: only `companyName`/`productName` lines changed.

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "ProjectSettings/ProjectSettings.asset"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "rename: productName -> RealmWeaver, companyName -> Akz0rt"
```

---

### Task 2: Create the public GitHub repository and push

**Files:** none (repo-level operation only)

**Interfaces:**
- Produces: `origin` remote and a public `RealmWeaver` GitHub repo that Tasks 4/5's CI workflows and Task 7's client both depend on existing.

**This task pushes the entire project's source history to a public GitHub repository. This is a one-way door — STOP and get explicit user confirmation immediately before the push step, even though the design was already approved**, per this project's policy on actions visible to others / hard to reverse.

- [ ] **Step 1: Create the repository (ask for confirmation first)**

```bash
gh repo create RealmWeaver --public --description "Procedural fantasy world-map + notes tool" --source="d:/D&D/.claude/worktrees/installer-auto-update" --remote=origin
```

Expected: command reports the new repo URL, and `git -C "d:/D&D/.claude/worktrees/installer-auto-update" remote -v` now lists `origin` pointing at `https://github.com/Akz0rt/RealmWeaver.git` (or the actual authenticated `gh` account if different from `Akz0rt` — use whatever account `gh auth status` reports, and use that same owner name in Task 7's API URL if it differs).

- [ ] **Step 2: Publish the worktree branch as origin's `main`**

All work so far (Task 1's rename commit) lives on the local branch `worktree-installer-auto-update`, not on any local `main` — push it directly as `origin/main` with an explicit refspec, and set that as this branch's upstream so later plain `git push` calls (Tasks 4, 5) land in the same place:

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" push -u origin worktree-installer-auto-update:main
```

Expected: push succeeds, GitHub shows this branch's commits as `main`, and `git -C "d:/D&D/.claude/worktrees/installer-auto-update" status` now reports the local branch as up to date with `origin/main`.

- [ ] **Step 3: Verify on GitHub**

```bash
gh repo view Akz0rt/RealmWeaver --web
```

Confirm the repository is visible, public, and shows the expected commit history.

---

### Task 3: Inno Setup installer script

**Files:**
- Create: `installer/RealmWeaver.iss`
- Modify: `.gitignore` (add Inno Setup's output folder)

**Interfaces:**
- Consumes: Unity build output expected at repo-root `build/StandaloneWindows64/` (this exact path is what Task 5's CI `package` job downloads to before compiling this script).
- Produces: `RealmWeaver-Setup-<version>.exe` in `installer/Output/`, consumed by Task 5's `release` job and by the in-app downloader (Task 8) as the asset name pattern to look for (`*.exe`).

- [ ] **Step 1: Add the Inno Setup output folder to .gitignore**

Add this line to `.gitignore` (anywhere in the "Builds" section is fine):

```
/installer/Output/
```

- [ ] **Step 2: Write the installer script**

Create `installer/RealmWeaver.iss`:

```ini
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "RealmWeaver"
#define MyAppPublisher "Akz0rt"
#define MyAppExeName "RealmWeaver.exe"
#define MyAppMutex "RealmWeaverSingleInstanceMutex"

[Setup]
AppId={{73312D00-F8DE-4552-9DB0-16AA78F9B7E1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppMutex={#MyAppMutex}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=RealmWeaver-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\build\StandaloneWindows64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 3: Verify it compiles, only if both Inno Setup and a local Unity build already exist**

The script's `[Files]` section reads from `..\build\StandaloneWindows64\`, which only exists once *something* has built the Unity project there — nothing in this plan has done that yet at this point (the first real build happens in Task 5's CI `build` job). So this step only applies if a local `build/StandaloneWindows64/` folder already happens to exist on this machine from prior manual testing, and Inno Setup is installed:

```bash
"/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "d:/D&D/installer/RealmWeaver.iss" "/DMyAppVersion=0.0.1-test"
```

Expected (only if both preconditions above are met): `installer/Output/RealmWeaver-Setup-0.0.1-test.exe` is created, no compile errors. **Otherwise, skip this step entirely** — the script gets its first real compile in Task 5's CI `package` job, which builds the project and installs Inno Setup itself. Note in the commit message that this step was skipped and why.

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "installer/RealmWeaver.iss" ".gitignore"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "feat: Inno Setup installer script for RealmWeaver"
```

---

### Task 4: Unity license activation (manual prerequisite — no workflow file)

**Files:** none. (The original design for this task — a GitHub Actions "request activation file" workflow — turned out to use a deprecated action; `game-ci/unity-request-activation-file@v2` now fails with "This action is no longer supported" and rejects the `unityVersion` input. The corrected, current process needs no CI-side step at all: the `.ulf` license is generated locally via Unity Hub. A commit already removed the obsolete `.github/workflows/unity-activation.yml` from this branch.)

**Interfaces:**
- Produces: the `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` repository secrets that Task 5's `build` job requires. **Nothing in Task 5 can run successfully until all three exist.**

- [ ] **Step 1: STOP — manual, interactive steps only you can do**

This needs your own Unity Hub login and your Unity account password; it cannot be scripted or done by an agent. Do this yourself, then come back:

1. Open **Unity Hub** on this machine (already installed, since it's how the Unity Editor is managed here).
2. Go to **Preferences → Licenses** (the gear/account icon in Unity Hub).
3. Click **Add**, choose **"Get a free personal license"**, and complete the activation (sign in with your Unity ID if prompted).
4. This creates a `.ulf` license file on disk at:
   ```
   C:\ProgramData\Unity\Unity_lic.ulf
   ```
   (`ProgramData` is hidden by default — enable "Show hidden items" in File Explorer if you don't see it.)
5. Open that file in a text editor and copy its entire contents (it's XML).
6. On GitHub: `RealmWeaver` repo → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**, and create all three of these:
   - Name `UNITY_LICENSE` — value: the full `.ulf` file contents from step 5.
   - Name `UNITY_EMAIL` — value: the email address for your Unity account.
   - Name `UNITY_PASSWORD` — value: the password for your Unity account.

Confirm back in this conversation once all three secrets are saved before starting Task 5 — Task 5's `build` job will fail without them. (`UNITY_EMAIL`/`UNITY_PASSWORD` going into GitHub Secrets is worth pausing on: GitHub encrypts secrets at rest and never prints them in logs, but it is still your real account password living in a third-party system — say now if you'd rather not do this and want to explore an alternative before Task 5 is built around it.)

---

### Task 5: CI/CD release pipeline

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `UNITY_LICENSE` secret (Task 4), `installer/RealmWeaver.iss` (Task 3).
- Produces: on any `vX.Y.Z` tag push, a GitHub Release with `RealmWeaver-Setup-X.Y.Z.exe` attached — this is the exact release Task 7's client-side fetch logic reads from, and the exact asset Task 8's downloader fetches.

- [ ] **Step 1: Write the pipeline**

Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - "v*.*.*"

jobs:
  build:
    name: Build Unity project
    runs-on: ubuntu-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4
        with:
          lfs: true

      - name: Cache Unity Library
        uses: actions/cache@v4
        with:
          path: Library
          key: Library-StandaloneWindows64-${{ github.sha }}
          restore-keys: Library-StandaloneWindows64-

      - name: Build project
        uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
        with:
          targetPlatform: StandaloneWindows64
          versioning: Tag
          unityVersion: 6000.3.2f1

      - name: Upload build artifact
        uses: actions/upload-artifact@v4
        with:
          name: windows-build
          path: build/StandaloneWindows64

  package:
    name: Build installer
    needs: build
    runs-on: windows-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Download build artifact
        uses: actions/download-artifact@v4
        with:
          name: windows-build
          path: build/StandaloneWindows64

      - name: Compute version from tag
        id: version
        shell: bash
        run: echo "version=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Install Inno Setup
        run: choco install innosetup --no-progress -y

      - name: Compile installer
        shell: pwsh
        run: |
          & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RealmWeaver.iss "/DMyAppVersion=${{ steps.version.outputs.version }}"

      - name: Upload installer artifact
        uses: actions/upload-artifact@v4
        with:
          name: installer
          path: installer/Output/RealmWeaver-Setup-*.exe

  release:
    name: Publish GitHub Release
    needs: package
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Download installer artifact
        uses: actions/download-artifact@v4
        with:
          name: installer
          path: installer-output

      - name: Create release
        uses: softprops/action-gh-release@v2
        with:
          files: installer-output/RealmWeaver-Setup-*.exe
          generate_release_notes: true
```

- [ ] **Step 2: Commit and push**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add ".github/workflows/release.yml"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "ci: build, package, and release pipeline triggered by version tags"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" push origin HEAD:main
```

- [ ] **Step 3: Confirm the manual prerequisite is done, then confirm with the user before tagging**

Verify all three secrets exist: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` (Task 4). Tagging and pushing triggers a real public CI run and a real public GitHub Release — confirm with the user before doing this, since it's the first artifact this repo will ever publish.

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" tag v0.0.1
git -C "d:/D&D/.claude/worktrees/installer-auto-update" push origin v0.0.1
```

- [ ] **Step 4: Watch the pipeline run**

```bash
gh run watch --repo Akz0rt/RealmWeaver
```

Expected: all three jobs (`build`, `package`, `release`) finish green. **If `game-ci/unity-builder` fails because it doesn't have an image for `6000.3.2f1` yet** (this Unity version may be very recent), check `https://game.ci` for currently supported versions and note this back to the user — don't silently substitute a different Unity version without asking, since that would diverge from the Editor version actually used locally (`ProjectSettings/ProjectVersion.txt`).

- [ ] **Step 5: Confirm the release exists**

```bash
gh release view v0.0.1 --repo Akz0rt/RealmWeaver
```

Expected: shows the release with `RealmWeaver-Setup-0.0.1.exe` attached. This is the first real release Task 7 will test against.

---

### Task 6: Version-compare logic + UpdateChecker shell

**Files:**
- Create: `Assets/WorldGen/Update/GitHubReleaseInfo.cs`
- Create: `Assets/WorldGen/Update/UpdateChecker.cs`

**Interfaces:**
- Produces: `UpdateVersionCompare.IsNewer(string remoteTag, string localVersion) : bool` — used by Task 7. `GitHubRelease` / `GitHubReleaseAsset` classes — deserialization targets used by Task 7's fetch. `UpdateChecker` MonoBehaviour shell (just `Awake()` + the self-test) — extended by Tasks 7 and 8.

- [ ] **Step 1: Write the version-compare pure logic**

Create `Assets/WorldGen/Update/GitHubReleaseInfo.cs`:

```csharp
using System.Collections.Generic;

namespace WorldGen.Update
{
    // Field names match GitHub's REST API JSON exactly (snake_case) so Newtonsoft can
    // deserialize with zero attributes/converters, matching this project's plain-POCO
    // convention (see ProjectSaveData).
    public class GitHubRelease
    {
        public string tag_name;
        public List<GitHubReleaseAsset> assets;
    }

    public class GitHubReleaseAsset
    {
        public string name;
        public string browser_download_url;
    }

    /// <summary>
    /// Pure SemVer (major.minor.patch only) comparison, no MonoBehaviour dependency —
    /// exercised directly by UpdateChecker's self-test without a running scene.
    /// </summary>
    public static class UpdateVersionCompare
    {
        public static bool IsNewer(string remoteTag, string localVersion)
        {
            var remote = ParseVersion(remoteTag);
            var local = ParseVersion(localVersion);
            if (remote == null || local == null) return false;

            if (remote.Value.major != local.Value.major) return remote.Value.major > local.Value.major;
            if (remote.Value.minor != local.Value.minor) return remote.Value.minor > local.Value.minor;
            return remote.Value.patch > local.Value.patch;
        }

        public static (int major, int minor, int patch)? ParseVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string s = raw.TrimStart('v', 'V');
            var parts = s.Split('.');
            if (parts.Length != 3) return null;
            if (!int.TryParse(parts[0], out int major)) return null;
            if (!int.TryParse(parts[1], out int minor)) return null;
            if (!int.TryParse(parts[2], out int patch)) return null;

            return (major, minor, patch);
        }
    }
}
```

- [ ] **Step 2: Write the UpdateChecker shell with its self-test**

Create `Assets/WorldGen/Update/UpdateChecker.cs`:

```csharp
using UnityEngine;

namespace WorldGen.Update
{
    /// <summary>
    /// Checks GitHub Releases for a newer version on launch and offers a one-click
    /// silent update. Self-contained — add to any GameObject, no Inspector wiring needed.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        const string ApiUrl = "https://api.github.com/repos/Akz0rt/RealmWeaver/releases/latest";

        Font builtinFont;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ── Self-test ──────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Version Compare")]
        public void SelfTestVersionCompare()
        {
            bool t1 = UpdateVersionCompare.IsNewer("v1.2.0", "1.1.9");   // minor bump -> newer
            bool t2 = UpdateVersionCompare.IsNewer("v1.1.0", "1.1.0");   // equal -> not newer
            bool t3 = UpdateVersionCompare.IsNewer("v1.0.9", "1.1.0");   // remote older -> not newer
            bool t4 = UpdateVersionCompare.IsNewer("v2.0.0", "1.9.9");   // major bump -> newer
            bool t5 = !UpdateVersionCompare.IsNewer("garbage", "1.0.0"); // unparseable -> not newer

            bool ok = t1 && !t2 && !t3 && t4 && t5;
            Debug.Log(ok
                ? "Self-Test Version Compare: PASS"
                : $"Self-Test Version Compare: FAIL (t1={t1}, t2={t2}, t3={t3}, t4={t4}, t5={t5})");
        }
    }
}
```

- [ ] **Step 3: Run the self-test**

In the Unity Editor, open the currently active scene, create a temporary empty GameObject (name it anything, e.g. "TempTest"), add the `UpdateChecker` component to it. Right-click the component header in the Inspector → **Self-Test: Version Compare**. Check the Console.

Expected: `Self-Test Version Compare: PASS`. Then delete the temporary GameObject — do **not** save the scene with it (real wiring happens in Task 9).

- [ ] **Step 4: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D\.claude\worktrees\installer-auto-update" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task6_compile.log"
```

Expected: exits 0, log ends with `Exiting batchmode successfully now!`, no `error CS` lines.

- [ ] **Step 5: Commit (including the new .meta files Unity generates for the two new scripts)**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" status --porcelain -- "Assets/WorldGen/Update/"
```

Expected: both `.cs` files and their `.cs.meta` files listed (Unity generates the `.meta`s the moment it imports the new files — confirmed by Step 4's batchmode run).

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "Assets/WorldGen/Update/"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "feat: SemVer compare helper and UpdateChecker shell"
```

---

### Task 7: Fetch latest release + update banner

**Files:**
- Modify: `Assets/WorldGen/Update/UpdateChecker.cs`

**Interfaces:**
- Consumes: `UpdateVersionCompare.IsNewer`, `GitHubRelease`/`GitHubReleaseAsset` (Task 6). Real data from the `v0.0.1` release published in Task 5.
- Produces: `downloadUrl : string` and `latestVersion : string` fields, populated once a newer release is found — consumed by Task 8's download step.

- [ ] **Step 1: Add the fetch coroutine and banner UI**

Replace the full contents of `Assets/WorldGen/Update/UpdateChecker.cs` with:

```csharp
using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace WorldGen.Update
{
    /// <summary>
    /// Checks GitHub Releases for a newer version on launch and offers a one-click
    /// silent update. Self-contained — add to any GameObject, no Inspector wiring needed.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        const string ApiUrl = "https://api.github.com/repos/Akz0rt/RealmWeaver/releases/latest";
        const int BannerSortingOrder = 500; // above ProjectMenuBar's 100, below ConfirmDialog's 32000

        Font builtinFont;
        Transform bannerCanvasTransform;
        GameObject bannerGO;
        Text statusText;
        Text actionLabel;

        string downloadUrl;
        string latestVersion;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
        }

        void Start()
        {
            StartCoroutine(CheckForUpdate());
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        IEnumerator CheckForUpdate()
        {
            using var request = UnityWebRequest.Get(ApiUrl);
            request.SetRequestHeader("User-Agent", "RealmWeaver-UpdateChecker");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"UpdateChecker: version check failed: {request.error}");
                yield break;
            }

            GitHubRelease release;
            try
            {
                release = JsonConvert.DeserializeObject<GitHubRelease>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UpdateChecker: failed to parse release JSON: {ex.Message}");
                yield break;
            }

            if (release == null || string.IsNullOrEmpty(release.tag_name)) yield break;
            if (!UpdateVersionCompare.IsNewer(release.tag_name, Application.version)) yield break;

            GitHubReleaseAsset installerAsset = null;
            if (release.assets != null)
            {
                foreach (var asset in release.assets)
                {
                    if (asset.name != null && asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerAsset = asset;
                        break;
                    }
                }
            }

            if (installerAsset == null)
            {
                Debug.LogWarning("UpdateChecker: newer release found but no .exe asset attached.");
                yield break;
            }

            latestVersion = release.tag_name.TrimStart('v', 'V');
            downloadUrl = installerAsset.browser_download_url;
            ShowBanner();
        }

        // ── Banner UI ──────────────────────────────────────────────────────────

        void ShowBanner()
        {
            var canvasGO = new GameObject("UpdateBannerCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BannerSortingOrder;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            // Captured only after AddComponent<Canvas>() — see ProjectMenuBar.cs for why a
            // reference grabbed before that conversion silently points at a destroyed Transform.
            bannerCanvasTransform = canvasGO.transform;

            bannerGO = new GameObject("UpdateBanner");
            bannerGO.transform.SetParent(bannerCanvasTransform, false);
            bannerGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.96f);
            var rect = bannerGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(260f, 64f);
            rect.anchoredPosition = new Vector2(-10f, -10f);

            var statusGO = new GameObject("Status");
            statusGO.transform.SetParent(bannerGO.transform, false);
            statusText = statusGO.AddComponent<Text>();
            statusText.text = $"Доступна версия {latestVersion}";
            statusText.font = builtinFont;
            statusText.fontSize = 12;
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.MiddleLeft;
            var statusRect = statusGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0.55f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.offsetMin = new Vector2(10f, 0f);
            statusRect.offsetMax = new Vector2(-8f, -4f);

            AddDismissButton();
            AddActionButton();
        }

        void AddDismissButton()
        {
            var go = new GameObject("Dismiss");
            go.transform.SetParent(bannerGO.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Dismiss);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(20f, 20f);
            rect.anchoredPosition = new Vector2(-4f, -4f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = "×";
            text.font = builtinFont;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void AddActionButton()
        {
            var go = new GameObject("ActionButton");
            go.transform.SetParent(bannerGO.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.45f, 0.25f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnDownloadClicked);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.05f, 0.1f);
            rect.anchorMax = new Vector2(0.95f, 0.5f);
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            actionLabel = textGO.AddComponent<Text>();
            actionLabel.text = "Скачать и установить";
            actionLabel.font = builtinFont;
            actionLabel.fontSize = 12;
            actionLabel.color = Color.white;
            actionLabel.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }

        void Dismiss()
        {
            if (bannerGO != null) Destroy(bannerGO.transform.parent.gameObject); // destroy the whole banner canvas
            bannerGO = null;
        }

        void OnDownloadClicked()
        {
            // Implemented in Task 8.
        }

        // ── Self-test ──────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Version Compare")]
        public void SelfTestVersionCompare()
        {
            bool t1 = UpdateVersionCompare.IsNewer("v1.2.0", "1.1.9");
            bool t2 = UpdateVersionCompare.IsNewer("v1.1.0", "1.1.0");
            bool t3 = UpdateVersionCompare.IsNewer("v1.0.9", "1.1.0");
            bool t4 = UpdateVersionCompare.IsNewer("v2.0.0", "1.9.9");
            bool t5 = !UpdateVersionCompare.IsNewer("garbage", "1.0.0");

            bool ok = t1 && !t2 && !t3 && t4 && t5;
            Debug.Log(ok
                ? "Self-Test Version Compare: PASS"
                : $"Self-Test Version Compare: FAIL (t1={t1}, t2={t2}, t3={t3}, t4={t4}, t5={t5})");
        }
    }
}
```

- [ ] **Step 2: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D\.claude\worktrees\installer-auto-update" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task7_compile.log"
```

Expected: exits 0, no `error CS` lines.

- [ ] **Step 3: Manual verification against the real v0.0.1 release**

In the Unity Editor, temporarily add an empty GameObject with `UpdateChecker` to the open scene (don't save the scene yet), enter Play mode. Since `Application.version` in the Editor is whatever `bundleVersion` currently is in `ProjectSettings` (not necessarily below `0.0.1`), temporarily also lower `PlayerSettings.bundleVersion` via **Edit → Project Settings → Player → Version** to `0.0.0` for this test only, then re-enter Play mode.

Expected: within a few seconds, a dark banner appears in the top-right corner reading "Доступна версия 0.0.1" with a "Скачать и установить" button and a "×". Click "×" — banner disappears. Stop Play mode, restore `bundleVersion` to its real value, remove the temporary GameObject without saving the scene.

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "Assets/WorldGen/Update/UpdateChecker.cs"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "feat: fetch latest GitHub release and show update banner"
```

---

### Task 8: Download installer, launch silently, quit

**Files:**
- Modify: `Assets/WorldGen/Update/UpdateChecker.cs`

**Interfaces:**
- Consumes: `downloadUrl`, `latestVersion` (Task 7), `ConfirmDialog.ShowInfo` (`WorldGen.Notes.Rendering`).
- Produces: nothing further downstream — this is the last piece of the update flow.

- [ ] **Step 1: Replace the `OnDownloadClicked` stub and add the download coroutine**

In `Assets/WorldGen/Update/UpdateChecker.cs`, add these usings at the top:

```csharp
using System.Diagnostics;
using System.IO;
using WorldGen.Notes.Rendering;
using Debug = UnityEngine.Debug;
```

Replace the `OnDownloadClicked` method:

```csharp
void OnDownloadClicked()
{
    StartCoroutine(DownloadAndInstall());
}
```

with:

```csharp
void OnDownloadClicked()
{
    StartCoroutine(DownloadAndInstall());
}

IEnumerator DownloadAndInstall()
{
    string tempPath = Path.Combine(Path.GetTempPath(), $"RealmWeaver-Setup-{latestVersion}.exe");

    actionLabel.text = "Загрузка... 0%";

    using var request = UnityWebRequest.Get(downloadUrl);
    request.downloadHandler = new DownloadHandlerFile(tempPath);
    var op = request.SendWebRequest();

    while (!op.isDone)
    {
        actionLabel.text = $"Загрузка... {Mathf.RoundToInt(request.downloadProgress * 100f)}%";
        yield return null;
    }

    if (request.result != UnityWebRequest.Result.Success)
    {
        Debug.LogWarning($"UpdateChecker: download failed: {request.error}");
        ConfirmDialog.ShowInfo(builtinFont, $"Не удалось скачать обновление: {request.error}");
        actionLabel.text = "Скачать и установить";
        yield break;
    }

    try
    {
        var psi = new ProcessStartInfo(tempPath, "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NOICONS")
        {
            UseShellExecute = true
        };
        Process.Start(psi);
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"UpdateChecker: failed to launch installer: {ex.Message}");
        ConfirmDialog.ShowInfo(builtinFont, $"Не удалось запустить установщик: {ex.Message}");
        actionLabel.text = "Скачать и установить";
        yield break;
    }

    Application.Quit();
}
```

(`System.Exception`/`System.Collections.IEnumerator` are already covered by the `using System;` / `using System.Collections;` added in Task 7.)

- [ ] **Step 2: Verify compile via batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D\.claude\worktrees\installer-auto-update" -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task8_compile.log"
```

Expected: exits 0, no `error CS` lines.

- [ ] **Step 3: Note on testing this task in isolation**

`Application.Quit()` is a no-op in the Editor (Unity only quits real standalone builds), and Inno Setup's `/CLOSEAPPLICATIONS`/`AppMutex` handshake only matters against a real running `RealmWeaver.exe`, not the Editor process. This means the download progress UI *can* be smoke-tested in Play mode against the real `v0.0.1` release from Task 5 (same temporary-GameObject-and-lowered-version setup as Task 7 Step 3), confirming the progress percentage updates and the file lands in `%TEMP%` — but the actual silent-install-and-relaunch behavior can only be verified from a **built** `RealmWeaver.exe`, which happens in Task 10.

- [ ] **Step 4: Commit**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "Assets/WorldGen/Update/UpdateChecker.cs"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "feat: download and silently install updates from the banner"
```

---

### Task 9: Wire UpdateChecker into the scene

**Files:**
- Create (temporary, deleted before commit): `Assets/Editor/TempSceneBootstrap_UpdateChecker.cs`
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:** none (scene-only change; `UpdateChecker` needs no Inspector field assignments).

No one is at an interactive Unity Editor window to click through this manually, so this task adds the GameObject programmatically via a one-off batchmode Editor script, then deletes that script — the only change that ends up committed is the scene file diff, exactly as if it had been done by hand.

- [ ] **Step 1: Write the temporary bootstrap script**

Create `Assets/Editor/TempSceneBootstrap_UpdateChecker.cs`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldGen.Update;

public static class TempSceneBootstrap_UpdateChecker
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var go = new GameObject("UpdateChecker");
        go.AddComponent<UpdateChecker>();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
```

- [ ] **Step 2: Run it via Unity batchmode**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "D:\D&D\.claude\worktrees\installer-auto-update" -executeMethod TempSceneBootstrap_UpdateChecker.Run -logFile "C:/Users/User/AppData/Local/Temp/claude/d--D-D/9cfb52f3-f574-4e44-afcd-2e81ce2c79c0/scratchpad/task9_wire.log"
```

Expected: exits 0, log ends with `Exiting batchmode successfully now!`, no `error CS` lines.

- [ ] **Step 3: Verify the diff**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" status --porcelain -- "Assets/Scenes/SampleScene.unity" "Assets/Editor/"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" diff --stat -- "Assets/Scenes/SampleScene.unity"
```

Expected: `Assets/Scenes/SampleScene.unity` shows as modified (new GameObject + `UpdateChecker` component added), and `Assets/Editor/TempSceneBootstrap_UpdateChecker.cs` + its `.meta` show as untracked.

- [ ] **Step 4: Delete the temporary bootstrap script**

```bash
rm "d:/D&D/.claude/worktrees/installer-auto-update/Assets/Editor/TempSceneBootstrap_UpdateChecker.cs" \
   "d:/D&D/.claude/worktrees/installer-auto-update/Assets/Editor/TempSceneBootstrap_UpdateChecker.cs.meta"
```

If `Assets/Editor/` is now empty, also remove `Assets/Editor/Editor.meta` if Unity generated one for the folder itself and no other files remain in it — check with `ls "d:/D&D/.claude/worktrees/installer-auto-update/Assets/Editor/"` first.

- [ ] **Step 5: Confirm in Play mode**

Enter Play mode with the real (current, non-lowered) `bundleVersion`. Expected: no banner appears (assuming the installed/running version is already the latest tag) and no console errors from `UpdateChecker`.

- [ ] **Step 6: Commit**

```bash
git -C "d:/D&D/.claude/worktrees/installer-auto-update" add "Assets/Scenes/SampleScene.unity"
git -C "d:/D&D/.claude/worktrees/installer-auto-update" commit -m "feat: wire UpdateChecker into the scene"
```

Confirm the temporary Editor script was NOT added (`git -C "d:/D&D/.claude/worktrees/installer-auto-update" status --porcelain -- "Assets/Editor/"` should show nothing, since Step 4 deleted it before this commit).

---

### Task 10: End-to-end verification with a real new release

**Files:** none (verification only)

**Interfaces:** none — this exercises everything from Tasks 1–9 together.

This task can't be completed by an agent alone — it needs an actual newer tagged release to update *to*, and a real built/installed `RealmWeaver.exe` to update *from* (not the Editor). Run through this checklist yourself once Tasks 1–9 are done, reviewed, and merged into the original checkout's `main` via `superpowers:finishing-a-development-branch` (by this point the worktree from earlier tasks may already be gone — use the original checkout at `d:/D&D` for these commands, not the worktree path):

- [ ] **Step 1:** Confirm the current released version is `v0.0.1` (`gh release list --repo Akz0rt/RealmWeaver`).
- [ ] **Step 2:** Install `RealmWeaver-Setup-0.0.1.exe` on this machine via the normal interactive wizard (Start → Далее → ... → Готово). Confirm Start Menu/Desktop shortcuts exist and the app launches.
- [ ] **Step 3:** From the original checkout, on `main`, tag and push `v0.0.2` (any trivial change, e.g. a comment tweak, is enough to have something to build):
  ```bash
  git -C "d:/D&D" tag v0.0.2
  git -C "d:/D&D" push origin v0.0.2
  ```
  Wait for the pipeline (`gh run watch --repo Akz0rt/RealmWeaver`) to publish the `v0.0.2` release.
- [ ] **Step 4:** Launch the **installed** `RealmWeaver.exe` (not the Editor). Confirm the update banner appears within a few seconds reading "Доступна версия 0.0.2".
- [ ] **Step 5:** Click "Скачать и установить". Confirm the button shows download progress, the app then closes on its own, the installer runs silently (no visible wizard window), and `RealmWeaver.exe` relaunches automatically once done.
- [ ] **Step 6:** Confirm the relaunched app is now version `0.0.2` (check via Task Manager → right-click `RealmWeaver.exe` → Properties → Details, or add a temporary on-screen version label if easier to eyeball) and that no update banner appears this time.

If any step fails, note exactly which one and what happened — that pinpoints whether the problem is in the CI pipeline, the installer script, or the client-side updater, rather than needing to re-debug the whole chain.

**Specific risk flagged by the final whole-branch review, watch closely at Step 5:** the app calls `Application.Quit()` itself immediately after `Process.Start(installer)`, rather than waiting for Inno Setup's Restart Manager (`/CLOSEAPPLICATIONS`) to close it. If the app has already fully exited by the time Setup reaches its close-applications phase, Setup has nothing registered to hand to `/RESTARTAPPLICATIONS`, and the app may not relaunch automatically even though the silent install itself succeeds. **If the app does not come back after Step 5:** this confirms the race — the fix is to remove the app's own `Application.Quit()` call from `DownloadAndInstall()`'s success path in `Assets/WorldGen/Update/UpdateChecker.cs` and let Inno Setup's `AppMutex`/`CloseApplications`/`RestartApplications` mechanism detect and manage the running process entirely on its own (this may also require the app to create a matching named `System.Threading.Mutex` called `RealmWeaverSingleInstanceMutex` on startup, since Inno's `AppMutex` directive detects the app via that exact mutex name, which nothing in the current code creates).

---

## Self-Review Notes

- **Spec coverage:** Naming/repo (Task 1–2), Versioning (Task 1 constraint + Task 5's `versioning: Tag`), Installer (Task 3), CI/CD (Tasks 4–5), In-app checker (Tasks 6–8), Scene wiring (Task 9, implied by spec's "add to scene as-is"), Error handling (folded into Tasks 7/8 per spec's exact rules), Testing (Task 10 + self-test in Task 6). All spec sections have a corresponding task.
- **Placeholder scan:** no TBD/TODO; the one open external variable (GitHub account owner name, if `gh auth status` differs from `Akz0rt`) is called out explicitly in Task 2 Step 1 with an instruction to propagate it, not left as a silent assumption.
- **Type consistency:** `UpdateVersionCompare.IsNewer(string, string) : bool` and `GitHubRelease`/`GitHubReleaseAsset` field names are identical everywhere they're used (Tasks 6–7). `downloadUrl`/`latestVersion`/`actionLabel`/`builtinFont` fields introduced in Task 7 are the exact names Task 8 extends.
