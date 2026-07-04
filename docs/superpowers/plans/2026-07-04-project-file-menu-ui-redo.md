# Project File Menu UI Redo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Файл" (File) menu bar (Сохранить / Сохранить как… / Открыть… / Открыть последние) to the scene, this time fitting it inside the 20px top margin the map-side panels already leave, so no layout-reservation math needs to change.

**Architecture:** A thin (20px), full-width, always-on-top overlay bar with a single "Файл" button that toggles a popup of the four actions, wired to the already-existing (and already-committed) `ProjectSerializer`/`WorldMapRenderer.LoadFromCells`/`PoiManager.LoadPois`/`NotesDocumentController.LoadDocument`. One line of padding on the notes side's existing layout group reserves matching space there without touching `NotesLayoutController.cs`.

**Tech Stack:** Unity 6000.3.2f1, Built-in Render Pipeline, new Input System, Newtonsoft.Json (already installed), legacy `UnityEngine.UI`, the project's vendored `StandaloneFileBrowser`.

## Global Constraints

- No automated test runner — verification is via `[ContextMenu("Self-Test: ...")]` methods run manually in the Unity Editor (right-click the component in the Inspector, in Play Mode), plus manual Play-mode testing.
- Do not modify `NotesLayoutController.cs`, `MapEditorPanel.cs`, or `MapLegendUI.cs` — these were the files implicated in the previous attempt's unresolved visual bug, and this redo's whole point is avoiding that mechanism. If a task in this plan seems to require touching one of them, stop and reconsider — it means the design has drifted from what was agreed.
- The bar's height (20px) is a plain literal in this plan's code, matching the existing `panelAnchoredPosition.y = -20` literal already used identically in `MapEditorPanel.cs`/`MapLegendUI.cs` — this codebase's established convention is repeating this literal rather than centralizing it in a shared constant, so this plan follows that same convention (no new shared constant is introduced).
- Spec reference: `docs/superpowers/specs/2026-07-04-project-file-menu-ui-redo-design.md`.
- `StandaloneFileBrowser`'s save/open dialogs only work in the Unity Editor — an existing, already-accepted limitation, not something to fix here.

---

### Task 1: Reserve 20px top margin on the notes side

**Files:**
- Modify: `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`

**Interfaces:**
- Produces: a 20px gap at the top of `notesAreaGO`'s rendered content (sidebar + toolbar both shift down by 20px within their own already-fixed-size container), matching the gap the map side already has.

- [ ] **Step 1: Add top padding to the notes area's HorizontalLayoutGroup**

In `Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs`, find:

```csharp
            var hLayout = notesAreaGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = true;
```

Replace with:

```csharp
            var hLayout = notesAreaGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandHeight = true;
            // Reserves the same 20px top margin MapEditorPanel/MapLegendUI already assume
            // (panelAnchoredPosition.y = -20 in both), so the upcoming ProjectMenuBar has
            // room without needing any change to notesAreaRoot's anchors or the map camera's
            // viewport rect (RectOffset order is left, right, top, bottom).
            hLayout.padding = new RectOffset(0, 0, 20, 0);
```

- [ ] **Step 2: Manual verification**

Enter Play mode. `NotesToolbar`'s icon row and `NotesTreeSidebar`'s "☰ Страницы" header should now start about 20px lower than before, leaving a blank 20px strip at the very top of the notes area (same dark background color as the rest of the notes area, no visible seam). The map side is untouched — `MapEditorPanel`/`MapLegendUI` should look exactly as they did before this change.

- [ ] **Step 3: Commit**

```bash
git add Assets/WorldGen/Notes/Rendering/NotesRootBuilder.cs
git commit -m "refactor: reserve 20px top margin on the notes side for the file menu bar"
```

---

### Task 2: Recreate RecentProjectsList and ConfirmDialog.ShowInfo

**Files:**
- Create: `Assets/WorldGen/Persistence/RecentProjectsList.cs`
- Modify: `Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs`

**Interfaces:**
- Produces: `WorldGen.Persistence.RecentProjectsList.Get() : List<string>`, `RecentProjectsList.Push(string path)`; `WorldGen.Notes.Rendering.ConfirmDialog.ShowInfo(Font font, string message, Action onDismiss = null)`.

- [ ] **Step 1: Create RecentProjectsList**

Create `Assets/WorldGen/Persistence/RecentProjectsList.cs`:

```csharp
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
```

- [ ] **Step 2: Add ShowInfo to ConfirmDialog**

`Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs` is currently at its original (pre-file-menu) state — a single `Show(Font, string, Action<bool>)` method plus a private `AddDialogButton` helper. Replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Shared modal dialogs, extracted from NotesUndoManager so canvas-object deletion and
    /// sidebar group/page deletion reuse the same UI instead of duplicating it. Only one
    /// dialog is ever shown at once (Show/ShowInfo both replace the previous one).
    /// </summary>
    public static class ConfirmDialog
    {
        static GameObject activeDialogGO;

        public static void Show(Font font, string message, System.Action<bool> onResult)
        {
            var panelGO = BuildBasePanel(font, message);

            AddDialogButton(font, panelGO.transform, "Отмена", new Vector2(0.05f, 0.1f), new Vector2(0.48f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(false);
            });
            AddDialogButton(font, panelGO.transform, "Удалить", new Vector2(0.52f, 0.1f), new Vector2(0.95f, 0.35f), new Color(0.55f, 0.15f, 0.15f), () =>
            {
                Object.Destroy(activeDialogGO);
                onResult(true);
            });
        }

        /// <summary>Single-button acknowledgement dialog, for errors/warnings that need no
        /// yes/no choice (e.g. project load failures).</summary>
        public static void ShowInfo(Font font, string message, System.Action onDismiss = null)
        {
            var panelGO = BuildBasePanel(font, message);

            AddDialogButton(font, panelGO.transform, "OK", new Vector2(0.25f, 0.1f), new Vector2(0.75f, 0.35f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                Object.Destroy(activeDialogGO);
                onDismiss?.Invoke();
            });
        }

        static GameObject BuildBasePanel(Font font, string message)
        {
            if (activeDialogGO != null) Object.Destroy(activeDialogGO);

            var canvasGO = new GameObject("ConfirmDialogCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            activeDialogGO = canvasGO;

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.7f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(300f, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            var msgGO = new GameObject("Message");
            msgGO.transform.SetParent(panelGO.transform, false);
            var msgText = msgGO.AddComponent<Text>();
            msgText.text = message;
            msgText.font = font;
            msgText.fontSize = 13;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0.4f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.sizeDelta = Vector2.zero;

            return panelGO;
        }

        static void AddDialogButton(Font font, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
        }
    }
}
```

(This is the same refactor as before: the shared panel-building code is extracted into `BuildBasePanel`, reused by both `Show` and the new `ShowInfo`. `Show`'s external behavior is unchanged.)

- [ ] **Step 4: Commit**

```bash
git add Assets/WorldGen/Persistence/RecentProjectsList.cs Assets/WorldGen/Notes/Rendering/ConfirmDialog.cs
git commit -m "feat: recent-projects list and single-button info dialog for the file menu"
```

---

### Task 3: Project menu bar UI + File actions

**Files:**
- Create: `Assets/WorldGen/Rendering/ProjectMenuBar.cs`

**Interfaces:**
- Consumes: `ProjectSerializer.Save/Load` (already committed), `WorldMapRenderer.LoadFromCells`/`LastGenParams`/`Cells` (already committed), `PoiManager.LoadPois`/`GetAllPois` (already committed), `NotesDocumentController.LoadDocument`/`Document` (already committed), `RecentProjectsList` (Task 2), `ConfirmDialog.ShowInfo` (Task 2), `SFB.StandaloneFileBrowser`.

- [ ] **Step 1: Create ProjectMenuBar**

Create `Assets/WorldGen/Rendering/ProjectMenuBar.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SFB;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Notes.Rendering;
using WorldGen.Persistence;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Thin (20px) "Файл" menu bar pinned to the top of the screen. 20px was chosen to fit
    /// inside the top margin MapEditorPanel/MapLegendUI already assume (both use
    /// panelAnchoredPosition.y = -20) — see NotesRootBuilder's matching 20px top padding on
    /// the notes side. Hosts Save/Save As/Open/Open Recent, wired to ProjectSerializer.
    /// Assign mapRenderer/poiManager/notesRoot in the Inspector.
    /// </summary>
    public class ProjectMenuBar : MonoBehaviour
    {
        const float BarHeightPixels = 20f;

        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public PoiManager poiManager;
        public NotesRootBuilder notesRoot;

        static readonly ExtensionFilter[] ProjectFilters =
        {
            new ExtensionFilter("D&D Project", "dndproj")
        };

        string currentPath; // null = never saved yet this session
        Font builtinFont;
        Transform canvasTransform;
        GameObject backdropGO;
        GameObject actionsPopupGO;
        bool recentExpanded;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystemExists();
            BuildUI();
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── File actions ──────────────────────────────────────────────────────

        void DoSave()
        {
            if (currentPath == null) { DoSaveAs(); return; }
            SaveTo(currentPath);
        }

        void DoSaveAs()
        {
            string path = StandaloneFileBrowser.SaveFilePanel("Сохранить проект", "", "project", ProjectFilters);
            if (string.IsNullOrEmpty(path)) return;
            SaveTo(path);
        }

        void SaveTo(string path)
        {
            if (mapRenderer == null)
            {
                Debug.LogWarning("ProjectMenuBar: mapRenderer не назначен в инспекторе.");
                return;
            }
            if (mapRenderer.Cells == null)
            {
                ConfirmDialog.ShowInfo(builtinFont, "Сначала сгенерируйте карту.");
                return;
            }

            var pois = poiManager != null ? poiManager.GetAllPois() : new List<PoiData>();
            var notes = notesRoot != null ? notesRoot.DocumentController.Document : new NotesDocument();

            try
            {
                ProjectSerializer.Save(path, mapRenderer.LastGenParams, mapRenderer.Cells, pois, notes);
            }
            catch (System.Exception ex)
            {
                ConfirmDialog.ShowInfo(builtinFont, $"Не удалось сохранить файл: {ex.Message}");
                return;
            }

            currentPath = path;
            RecentProjectsList.Push(path);
        }

        void DoOpen()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Открыть проект", "", ProjectFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            LoadFrom(paths[0]);
        }

        void LoadFrom(string path)
        {
            if (mapRenderer == null)
            {
                Debug.LogWarning("ProjectMenuBar: mapRenderer не назначен в инспекторе.");
                return;
            }

            var result = ProjectSerializer.Load(path);
            if (!result.Success)
            {
                ConfirmDialog.ShowInfo(builtinFont, result.ErrorMessage);
                return;
            }
            if (!string.IsNullOrEmpty(result.WarningMessage))
                ConfirmDialog.ShowInfo(builtinFont, result.WarningMessage);

            mapRenderer.LoadFromCells(result.Cells, result.GenerationParams);
            poiManager?.LoadPois(result.Pois);
            notesRoot?.DocumentController.LoadDocument(result.Notes);

            currentPath = path;
            RecentProjectsList.Push(path);
        }

        // ── UI construction ──────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("ProjectMenuBarCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvasTransform = canvasGO.transform;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // above the map/notes UI, below dropdown/dialog overlays (30000+)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var barGO = new GameObject("MenuBar");
            barGO.transform.SetParent(canvasTransform, false);
            barGO.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 1f);
            var barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, BarHeightPixels);
            barRect.anchoredPosition = Vector2.zero;

            var fileBtnGO = new GameObject("FileButton");
            fileBtnGO.transform.SetParent(barGO.transform, false);
            var fileBtnImg = fileBtnGO.AddComponent<Image>();
            fileBtnImg.color = new Color(1f, 1f, 1f, 0.06f);
            var fileBtn = fileBtnGO.AddComponent<Button>();
            fileBtn.targetGraphic = fileBtnImg;
            fileBtn.onClick.AddListener(ToggleActionsPopup);
            var fileBtnRect = fileBtnGO.GetComponent<RectTransform>();
            fileBtnRect.anchorMin = new Vector2(0f, 0f);
            fileBtnRect.anchorMax = new Vector2(0f, 1f);
            fileBtnRect.pivot = new Vector2(0f, 0.5f);
            fileBtnRect.sizeDelta = new Vector2(70f, 0f);
            fileBtnRect.anchoredPosition = Vector2.zero;

            var fileLabelGO = new GameObject("Label");
            fileLabelGO.transform.SetParent(fileBtnGO.transform, false);
            var fileLabel = fileLabelGO.AddComponent<Text>();
            fileLabel.text = "Файл";
            fileLabel.font = builtinFont;
            fileLabel.fontSize = 12;
            fileLabel.color = Color.white;
            fileLabel.alignment = TextAnchor.MiddleCenter;
            var fileLabelRect = fileLabelGO.GetComponent<RectTransform>();
            fileLabelRect.anchorMin = Vector2.zero;
            fileLabelRect.anchorMax = Vector2.one;
            fileLabelRect.sizeDelta = Vector2.zero;
        }

        void ToggleActionsPopup()
        {
            if (actionsPopupGO != null) { CloseActionsPopup(); return; }
            recentExpanded = false;
            OpenActionsPopup();
        }

        void OpenActionsPopup()
        {
            // Backdrop first, popup second — under this project's established "later sibling
            // always wins raycasts" rule, the popup (created after) receives clicks over the
            // backdrop, while the backdrop still catches any click outside the popup to close it.
            backdropGO = new GameObject("MenuBackdrop");
            backdropGO.transform.SetParent(canvasTransform, false);
            var backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0f);
            var backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.targetGraphic = backdropImg;
            backdropBtn.onClick.AddListener(() => CloseActionsPopup());
            var backdropRect = backdropGO.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.sizeDelta = Vector2.zero;

            actionsPopupGO = new GameObject("FileActionsPopup");
            actionsPopupGO.transform.SetParent(canvasTransform, false);
            actionsPopupGO.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 0.98f);
            var popupRect = actionsPopupGO.GetComponent<RectTransform>();
            popupRect.anchorMin = new Vector2(0f, 1f);
            popupRect.anchorMax = new Vector2(0f, 1f);
            popupRect.pivot = new Vector2(0f, 1f);
            popupRect.anchoredPosition = new Vector2(0f, -BarHeightPixels);

            var recentPaths = recentExpanded ? RecentProjectsList.Get().Where(File.Exists).ToList() : null;
            int rowCount = 4 + (recentExpanded ? System.Math.Max(recentPaths.Count, 1) : 0);
            popupRect.sizeDelta = new Vector2(200f, rowCount * 26f);

            var vlg = actionsPopupGO.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            AddPopupAction(actionsPopupGO.transform, "Сохранить", () => { CloseActionsPopup(); DoSave(); });
            AddPopupAction(actionsPopupGO.transform, "Сохранить как…", () => { CloseActionsPopup(); DoSaveAs(); });
            AddPopupAction(actionsPopupGO.transform, "Открыть…", () => { CloseActionsPopup(); DoOpen(); });
            AddPopupAction(actionsPopupGO.transform, recentExpanded ? "Открыть последние ▴" : "Открыть последние ▾", () =>
            {
                recentExpanded = !recentExpanded;
                CloseActionsPopup(keepExpandedFlag: true);
                OpenActionsPopup();
            });

            if (recentExpanded)
            {
                if (recentPaths.Count == 0)
                {
                    AddPopupAction(actionsPopupGO.transform, "  (пусто)", () => { });
                }
                else
                {
                    foreach (var path in recentPaths)
                    {
                        string display = "  " + Path.GetFileName(path);
                        string capturedPath = path;
                        AddPopupAction(actionsPopupGO.transform, display, () => { CloseActionsPopup(); LoadFrom(capturedPath); });
                    }
                }
            }
        }

        void CloseActionsPopup(bool keepExpandedFlag = false)
        {
            if (backdropGO != null) Destroy(backdropGO);
            backdropGO = null;
            if (actionsPopupGO != null) Destroy(actionsPopupGO);
            actionsPopupGO = null;
            if (!keepExpandedFlag) recentExpanded = false;
        }

        void AddPopupAction(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Action_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = Vector2.zero;
        }

        // ── Self-test ──────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Recent Projects List")]
        public void SelfTestRecentProjectsList()
        {
            string prefsBackup = PlayerPrefs.GetString("Project.RecentPaths", "");

            RecentProjectsList.Push("a.dndproj");
            RecentProjectsList.Push("b.dndproj");
            RecentProjectsList.Push("c.dndproj");
            RecentProjectsList.Push("a.dndproj"); // re-push moves it back to front, no duplicate

            var list = RecentProjectsList.Get();
            bool ok = list.Count == 3
                && list[0] == "a.dndproj"
                && list[1] == "c.dndproj"
                && list[2] == "b.dndproj";

            PlayerPrefs.SetString("Project.RecentPaths", prefsBackup); // restore whatever was there before the test

            Debug.Log(ok
                ? "Self-Test Recent Projects List: PASS"
                : $"Self-Test Recent Projects List: FAIL (list=[{string.Join(", ", list)}])");
        }
    }
}
```

- [ ] **Step 2: Add ProjectMenuBar to the scene and wire it up**

In the Unity Editor, add a new empty GameObject (e.g. "ProjectMenuBar") to the scene, add the `ProjectMenuBar` component to it, and assign `mapRenderer`, `poiManager`, and `notesRoot` in the Inspector to the same scene objects already referenced by `MapEditorPanel`/`PoiEditPanel`/other existing components.

- [ ] **Step 3: Run the self-test**

In Play mode, right-click `ProjectMenuBar` in the Inspector → **Self-Test: Recent Projects List**.

Expected: Console shows `Self-Test Recent Projects List: PASS`.

- [ ] **Step 4: Manual verification — visual check first**

Enter Play mode. Confirm: a thin dark 20px strip spans the full width of the very top of the screen, with a "Файл" button on the left. Confirm there is no visual duplication/ghosting anywhere near this strip (the specific symptom that caused the previous attempt's rollback) — check both the map side and the notes side (where `NotesToolbar`'s icons and `NotesTreeSidebar`'s "☰ Страницы" header should now start about 20px lower than before, per Task 1).

If ghosting reappears here despite this being a materially different (non-reserving) layout mechanism, stop and report back rather than attempting further fixes — this would indicate the bug's root cause is unrelated to `NotesLayoutController`/anchor math, which changes the diagnosis significantly.

- [ ] **Step 5: Manual end-to-end verification — full save/load round trip**

Click "Файл" → confirm the popup opens with Сохранить / Сохранить как… / Открыть… / Открыть последние. Generate a map, add a POI with a custom icon, create a second notes page with a note card. Click **Сохранить как…**, save to a file. Stop Play mode, start it again (fresh in-memory state). Click **Открыть…**, pick the saved file. Confirm the map, POI (with icon), and notes page all reappear correctly. Then click **Открыть последние** and confirm the file appears and reloads correctly when clicked.

- [ ] **Step 6: Commit**

```bash
git add Assets/WorldGen/Rendering/ProjectMenuBar.cs
git commit -m "feat: project menu bar with Save/Save As/Open/Open Recent (redo, no layout reservation)"
```
