# Installer & Auto-Update — Design

**Date:** 2026-07-05
**Status:** Approved, ready for implementation
**Branch:** implement off `main` (project has no separate feature branches / no git remote)

---

## Goal

Give the tool a real distribution story: a Windows installer that can be handed to users beyond the immediate group, plus an in-app mechanism that tells a running installation when a newer version exists and lets the user update without leaving the app or hunting for a download link.

Today the project has no build/release tooling at all — no installer, no versioning discipline, no git remote. This is the first release infrastructure added to the tool.

Audience: broader public distribution (not just one person or a small fixed group) — this shapes several choices below (public repo, trademark-safe naming, a real CI pipeline rather than a manual one-off script).

---

## Scope

**In scope:**
- Rename the public-facing product from "D&D" to **RealmWeaver** (Unity `productName`, GitHub repo name, installer text) to avoid trading on the Dungeons & Dragons trademark while distributing publicly.
- A public GitHub repository named `RealmWeaver` as the project's new git remote.
- A Windows installer built with Inno Setup.
- A GitHub Actions pipeline that builds the Unity project, packages the installer, and publishes a GitHub Release whenever a version tag is pushed.
- An in-app update checker: on launch, compares the running version against the latest GitHub Release; if newer, shows a small dismissible banner with a "Скачать и установить" button.
- Clicking that button downloads the installer, launches it silently with auto-restart flags, and quits the running app — no browser round-trip, no separate updater process.

**Out of scope (v1):**
- Delta/incremental updates (each update re-downloads the full installer).
- Any non-Windows platform.
- A "skip this version" preference — dismissing the banner just hides it until the next launch.
- Code signing / Authenticode certificate for the installer (SmartScreen will warn on first run; acceptable for v1, revisit if it becomes a real friction point).
- Automatic retry/backoff scheduling for the version check — it runs once per launch, and a failure just means no banner this session.

---

## 1. Naming & repository

- Unity `ProjectSettings`: `productName` → `RealmWeaver`. `companyName` moves off the Unity default `DefaultCompany` to `Akz0rt` (the existing git user/handle) — good enough for a hobby-scale release; easy to change later since it's a single field.
- New public GitHub repository named `RealmWeaver`, added as `origin`. This is the project's first git remote.
- Changing `productName`/`companyName` moves where Windows stores `PlayerPrefs` (registry path is keyed by both). This orphans the recent-projects list and split-fraction/sidebar-width prefs saved so far — a non-issue today since the tool has never been distributed, but the reason to lock in real names now rather than after the first public release.
- The Inno Setup `AppId` (a fixed GUID, generated once) and `AppMutex` are derived from this name and must never change after the first public release — Inno Setup uses `AppId` to recognize "this is an upgrade of an existing install" rather than a fresh side-by-side install.
- Because the repo becomes public, existing history and source are visible to anyone. This is a one-way door — worth a last look at the repo before the first push, but not re-litigated here since it was already confirmed.

---

## 2. Versioning

- A git tag `vX.Y.Z` (SemVer) is the single source of truth for a release's version — not a value hand-edited in `ProjectSettings`.
- The CI build job sets `PlayerSettings.bundleVersion` from the triggering tag automatically (`game-ci/unity-builder`'s built-in `Tag` versioning strategy), so `Application.version` in the running build always matches the tag it was built from.
- The in-app update checker does a plain three-part numeric comparison (major, minor, patch) between `Application.version` and the latest release's `tag_name` (stripped of its leading `v`). No pre-release/build-metadata handling — not needed for this project's release cadence.

---

## 3. Installer (Inno Setup)

- `installer/RealmWeaver.iss`, compiled to `RealmWeaver-Setup-X.Y.Z.exe`.
- Fixed `AppId` (GUID) and `AppMutex` (a stable string naming the running game's mutex), set once and never changed — see [Naming & repository](#1-naming--repository).
- Normal interactive run (user double-clicks the downloaded installer themselves): standard Inno Setup wizard, no special flags.
- Silent self-update run (launched by the game itself — see [In-app update checker](#4-in-app-update-checker)): `/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NOICONS`.
  - `/CLOSEAPPLICATIONS` + `AppMutex` let Inno Setup's Restart Manager integration close the running game process even if it hasn't fully exited yet by the time the installer starts.
  - `/RESTARTAPPLICATIONS` relaunches the game automatically once install finishes — this is Inno Setup's built-in mechanism, no custom relaunch code needed on the app side.
  - `/NOICONS` skips recreating Start Menu/Desktop shortcuts that a prior install already created.

---

## 4. CI/CD pipeline (GitHub Actions)

Triggered by pushing a tag matching `v*.*.*`. Three sequential jobs:

1. **build** (`ubuntu-latest`, `game-ci/unity-builder@v4`) — builds `StandaloneWindows64` with `versioning: Tag` (see [Versioning](#2-versioning)). Uploads the build output as a workflow artifact.
2. **package** (`windows-latest`, needs `build`) — downloads the artifact, installs Inno Setup via `choco install innosetup`, compiles `installer/RealmWeaver.iss` with the tag's version passed in as a preprocessor define. Produces `RealmWeaver-Setup-X.Y.Z.exe`.
3. **release** (needs `package`) — creates a GitHub Release for the tag (`softprops/action-gh-release`), attaches the installer as a release asset. Release notes are GitHub's auto-generated commit-based notes — no separate changelog file to maintain.

**Manual prerequisite (cannot be done by an agent):** `game-ci/unity-builder` needs an activated Unity Personal license available to the CI runner as a secret. Getting one requires a one-time interactive activation: run a "request activation file" job to produce a `.alf`, upload it to `license.unity3d.com` under your own Unity ID in a browser, download the resulting `.ulf`, and add its contents as the `UNITY_LICENSE` repository secret. This step needs your own Unity account login and can't be scripted end-to-end; the workflow file and instructions for it will be provided, but you run this part yourself once.

Secrets used: `UNITY_LICENSE` (manual, above), `GITHUB_TOKEN` (provided automatically by Actions).

---

## 5. In-app update checker

New self-contained component, `Assets/WorldGen/Update/UpdateChecker.cs` — unlike `ProjectMenuBar`, it needs no Inspector wiring and can just be added to the scene as-is.

- On `Start()`, issues a `UnityWebRequest` `GET` to `https://api.github.com/repos/<owner>/RealmWeaver/releases/latest`, with an explicit `User-Agent` header (GitHub's API rejects requests without one). Parsed with the Newtonsoft.Json dependency already added for project persistence.
- Reads `tag_name` and the `browser_download_url` of the asset whose name ends in `.exe`. Compares against `Application.version` per [Versioning](#2-versioning). If the release is newer, shows the update banner.
- **Banner:** a small non-modal panel in the top-right corner (clear of the existing 20px top margin `ProjectMenuBar`/`NotesRoot` already reserve), styled consistently with the rest of the tool's runtime-built UI (`LegacyRuntime.ttf`, dark panel background), with a `sortingOrder` between `ProjectMenuBar`'s bar (100) and `ConfirmDialog`'s modals (32000). Contents: "Доступна версия X.Y.Z", a "Скачать и установить" button, and a dismiss (×) that hides the banner for the rest of the session only.
- **On "Скачать и установить":** a `UnityWebRequest` with `DownloadHandlerFile` downloads the installer `.exe` to a temp path (`Path.GetTempPath()`), with the button's contents swapped for a progress readout while it downloads. On completion, launches it via `Process.Start` with the silent/auto-restart flags from [Installer](#3-installer-inno-setup), then calls `Application.Quit()`.

---

## 6. Error handling

- **Version-check network failure** (offline, GitHub API down, rate-limited) — logged as a warning, no user-facing dialog. This is a background check; failing it just means no banner appears this session, and it's retried on the next launch.
- **Download failure** (network drop mid-download, disk full in temp) — shown via the existing `ConfirmDialog.ShowInfo` single-button pattern; the banner stays up so the user can retry.
- The installer's own failure modes (disk full at install time, permissions, antivirus interference) are Inno Setup's problem to surface, same as for any normal install — nothing extra to build on the app side.

---

## 7. Testing

No automated test runner in this project (established convention). Verification is manual, once a real tagged release exists:
- Temporarily point `Application.version` below the latest published tag (or run against a low-numbered test tag) and confirm the banner appears, downloads, silently installs, and the app relaunches at the new version.
- Confirm a fresh install (interactive wizard, no flags) still works normally end to end.
- Confirm the banner does not appear when already on the latest version, and that a dismissed banner stays hidden for the rest of that session.

---

## Out of Scope (v1)

- Delta/incremental updates.
- macOS/Linux builds.
- "Skip this version" persistence.
- Installer code signing.
- Update-check retry/backoff beyond "once per launch."
- Changelog authoring beyond GitHub's auto-generated release notes.
