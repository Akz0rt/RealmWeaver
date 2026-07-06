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
using WorldGen.Rendering.Theme;
using ThemeMode = WorldGen.Rendering.Theme.Theme; // "Theme" alone is ambiguous here: it names both the enum and its containing namespace, and namespace wins over the using-directive-imported type when referenced from the parent WorldGen.Rendering namespace.

namespace WorldGen.Rendering
{
    /// <summary>
    /// 40px application menu bar pinned to the top of the screen (Main-screen shell redesign,
    /// 2026-07-06). Left side shows a logo square + "REALMWEAVER" wordmark followed by the menu
    /// items ("Файл" — a real dropdown wired to ProjectSerializer — plus inert "Правка"/"Вид"
    /// placeholders); the right side shows the current project's file name. The 46px MapToolbarUI
    /// strip and the docked panels sit directly below this bar (they offset by 40 + 46).
    /// Assign mapRenderer/poiManager/notesRoot in the Inspector.
    /// </summary>
    public class ProjectMenuBar : MonoBehaviour
    {
        const float BarHeightPixels = 40f;

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
        Text projectNameText;
        Transform canvasTransform;
        GameObject backdropGO;
        GameObject actionsPopupGO;
        bool recentExpanded;

        enum MenuKind { File, View }
        MenuKind openMenuKind;

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
            UpdateProjectNameText();
            RecentProjectsList.Push(path);
        }

        void DoOpen()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Открыть проект", "", ProjectFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            LoadFrom(paths[0]);
        }

        /// <summary>Lets other screens (e.g. GenerationScreenUI's "Открыть проект…") trigger the same Open flow.</summary>
        public void TriggerOpenFromExternal() => DoOpen();

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
            UpdateProjectNameText();
            RecentProjectsList.Push(path);
        }

        // ── UI construction ──────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGO = new GameObject("ProjectMenuBarCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // above the map/notes UI, below dropdown/dialog overlays (30000+)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            // Captured only after AddComponent<Canvas>() — adding Canvas to a GameObject that
            // only has a plain Transform makes Unity destroy that Transform and replace it with
            // a RectTransform (Canvas requires one). A reference grabbed before this conversion
            // (canvasGO.transform, pre-Canvas) is left pointing at the destroyed component, so
            // any later SetParent(that stale reference, ...) silently treats it as a null parent
            // and drops the child to the scene root instead of throwing.
            canvasTransform = canvasGO.transform;

            var barGO = new GameObject("MenuBar");
            barGO.transform.SetParent(canvasTransform, false);
            var barImg = barGO.AddComponent<Image>();
            ThemeService.Tag(barImg, ThemeRole.Panel);
            var barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, BarHeightPixels);
            barRect.anchoredPosition = Vector2.zero;

            // Logo square
            var logoGO = new GameObject("Logo");
            logoGO.transform.SetParent(barGO.transform, false);
            var logoImg = logoGO.AddComponent<Image>();
            ThemeService.Tag(logoImg, ThemeRole.Accent);
            var logoRect = logoGO.GetComponent<RectTransform>();
            logoRect.anchorMin = new Vector2(0f, 0.5f);
            logoRect.anchorMax = new Vector2(0f, 0.5f);
            logoRect.pivot = new Vector2(0f, 0.5f);
            logoRect.anchoredPosition = new Vector2(12f, 0f);
            logoRect.sizeDelta = new Vector2(16f, 16f);

            // Wordmark
            var wordmarkGO = new GameObject("Wordmark");
            wordmarkGO.transform.SetParent(barGO.transform, false);
            var wordmarkText = wordmarkGO.AddComponent<Text>();
            wordmarkText.text = "REALMWEAVER";
            wordmarkText.font = builtinFont;
            wordmarkText.fontSize = 13;
            wordmarkText.fontStyle = FontStyle.Bold;
            wordmarkText.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(wordmarkText, ThemeRole.Txt);
            var wordmarkRect = wordmarkGO.GetComponent<RectTransform>();
            wordmarkRect.anchorMin = new Vector2(0f, 0.5f);
            wordmarkRect.anchorMax = new Vector2(0f, 0.5f);
            wordmarkRect.pivot = new Vector2(0f, 0.5f);
            wordmarkRect.anchoredPosition = new Vector2(36f, 0f);
            wordmarkRect.sizeDelta = new Vector2(140f, 20f);

            var fileBtnGO = new GameObject("FileButton");
            fileBtnGO.transform.SetParent(barGO.transform, false);
            var fileBtnImg = fileBtnGO.AddComponent<Image>();
            ThemeService.Tag(fileBtnImg, ThemeRole.Elev);
            var fileBtn = fileBtnGO.AddComponent<Button>();
            fileBtn.targetGraphic = fileBtnImg;
            fileBtn.onClick.AddListener(() => ToggleMenu(MenuKind.File));
            var fileBtnRect = fileBtnGO.GetComponent<RectTransform>();
            fileBtnRect.anchorMin = new Vector2(0f, 0f);
            fileBtnRect.anchorMax = new Vector2(0f, 1f);
            fileBtnRect.pivot = new Vector2(0f, 0.5f);
            fileBtnRect.sizeDelta = new Vector2(56f, 0f);
            fileBtnRect.anchoredPosition = new Vector2(158f, 0f); // sits after the logo+wordmark (whose text runs to ~146px), ahead of the inert Правка/Вид items

            var fileLabelGO = new GameObject("Label");
            fileLabelGO.transform.SetParent(fileBtnGO.transform, false);
            var fileLabel = fileLabelGO.AddComponent<Text>();
            fileLabel.text = "Файл";
            fileLabel.font = builtinFont;
            fileLabel.fontSize = 12;
            ThemeService.Tag(fileLabel, ThemeRole.Txt);
            fileLabel.alignment = TextAnchor.MiddleCenter;
            var fileLabelRect = fileLabelGO.GetComponent<RectTransform>();
            fileLabelRect.anchorMin = Vector2.zero;
            fileLabelRect.anchorMax = Vector2.one;
            fileLabelRect.sizeDelta = Vector2.zero;

            // Inert menu placeholders (no Button, no click behaviour — muted static labels)
            AddMenuLabel(barGO.transform, "Правка", xOffset: 228f);
            AddMenuLabel(barGO.transform, "Вид", xOffset: 288f, onClick: () => ToggleMenu(MenuKind.View), role: ThemeRole.Txt);

            // Right-aligned current project name
            var projectNameGO = new GameObject("ProjectName");
            projectNameGO.transform.SetParent(barGO.transform, false);
            projectNameText = projectNameGO.AddComponent<Text>();
            projectNameText.font = builtinFont;
            projectNameText.fontSize = 12;
            projectNameText.alignment = TextAnchor.MiddleRight;
            ThemeService.Tag(projectNameText, ThemeRole.Mut);
            var projectNameRect = projectNameGO.GetComponent<RectTransform>();
            projectNameRect.anchorMin = new Vector2(1f, 0.5f);
            projectNameRect.anchorMax = new Vector2(1f, 0.5f);
            projectNameRect.pivot = new Vector2(1f, 0.5f);
            projectNameRect.anchoredPosition = new Vector2(-12f, 0f);
            projectNameRect.sizeDelta = new Vector2(220f, 20f);
            UpdateProjectNameText();
        }

        /// <summary>Top-bar menu label. With onClick null it's an inert muted placeholder
        /// (Правка); with an onClick it becomes a clickable menu (Вид → theme popup).</summary>
        void AddMenuLabel(Transform parent, string label, float xOffset, System.Action onClick = null, ThemeRole role = ThemeRole.Mut)
        {
            var go = new GameObject($"Menu_{label}");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(text, role);
            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = text;
                btn.transition = Selectable.Transition.None; // don't tint the label text on hover/press
                btn.onClick.AddListener(() => onClick());
            }
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(xOffset, 0f);
            rect.sizeDelta = new Vector2(50f, 20f);
        }

        void UpdateProjectNameText()
        {
            if (projectNameText == null) return;
            projectNameText.text = string.IsNullOrEmpty(currentPath)
                ? "Проект не сохранён"
                : System.IO.Path.GetFileName(currentPath);
        }

        void ToggleMenu(MenuKind kind)
        {
            if (actionsPopupGO != null && openMenuKind == kind) { CloseActionsPopup(); return; }
            if (actionsPopupGO != null) CloseActionsPopup();
            recentExpanded = false;
            openMenuKind = kind;
            OpenMenu(kind);
        }

        void OpenMenu(MenuKind kind)
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

            actionsPopupGO = new GameObject("MenuPopup");
            actionsPopupGO.transform.SetParent(canvasTransform, false);
            var popupImg = actionsPopupGO.AddComponent<Image>();
            ThemeService.Tag(popupImg, ThemeRole.Panel2, 0.98f);
            var popupRect = actionsPopupGO.GetComponent<RectTransform>();
            popupRect.anchorMin = new Vector2(0f, 1f);
            popupRect.anchorMax = new Vector2(0f, 1f);
            popupRect.pivot = new Vector2(0f, 1f);
            // Drop below the button that opened this menu (Файл @158 / Вид @288).
            popupRect.anchoredPosition = new Vector2(kind == MenuKind.File ? 158f : 288f, -BarHeightPixels);

            var vlg = actionsPopupGO.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            if (kind == MenuKind.View)
            {
                popupRect.sizeDelta = new Vector2(160f, 26f);
                AddPopupAction(actionsPopupGO.transform, ThemeService.Current == ThemeMode.Dark ? "Светлая тема" : "Тёмная тема", () =>
                {
                    CloseActionsPopup();
                    ThemeService.ApplyTheme(ThemeService.Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
                });
                return;
            }

            // MenuKind.File
            var recentPaths = recentExpanded ? RecentProjectsList.Get().Where(File.Exists).ToList() : null;
            int rowCount = 4 + (recentExpanded ? System.Math.Max(recentPaths.Count, 1) : 0);
            popupRect.sizeDelta = new Vector2(200f, rowCount * 26f);

            AddPopupAction(actionsPopupGO.transform, "Сохранить", () => { CloseActionsPopup(); DoSave(); });
            AddPopupAction(actionsPopupGO.transform, "Сохранить как…", () => { CloseActionsPopup(); DoSaveAs(); });
            AddPopupAction(actionsPopupGO.transform, "Открыть…", () => { CloseActionsPopup(); DoOpen(); });
            AddPopupAction(actionsPopupGO.transform, recentExpanded ? "Открыть последние ▴" : "Открыть последние ▾", () =>
            {
                recentExpanded = !recentExpanded;
                CloseActionsPopup(keepExpandedFlag: true);
                OpenMenu(MenuKind.File);
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
            ThemeService.Tag(text, ThemeRole.Txt);
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
            PlayerPrefs.SetString("Project.RecentPaths", ""); // start from empty, not whatever the real recent list currently holds

            RecentProjectsList.Push("a.dndproj");
            RecentProjectsList.Push("b.dndproj");
            RecentProjectsList.Push("c.dndproj");
            RecentProjectsList.Push("a.dndproj"); // re-push moves it back to front, no duplicate

            var list = RecentProjectsList.Get();
            bool dedupOk = list.Count == 3
                && list[0] == "a.dndproj"
                && list[1] == "c.dndproj"
                && list[2] == "b.dndproj";

            RecentProjectsList.Push("d.dndproj");
            RecentProjectsList.Push("e.dndproj");
            RecentProjectsList.Push("f.dndproj"); // 6th distinct entry — list should cap at 5, dropping the oldest ("b.dndproj")

            var cappedList = RecentProjectsList.Get();
            bool capOk = cappedList.Count == 5
                && cappedList[0] == "f.dndproj"
                && !cappedList.Contains("b.dndproj");

            PlayerPrefs.SetString("Project.RecentPaths", prefsBackup); // restore whatever was there before the test

            bool ok = dedupOk && capOk;
            Debug.Log(ok
                ? "Self-Test Recent Projects List: PASS"
                : $"Self-Test Recent Projects List: FAIL (dedupOk={dedupOk}, list=[{string.Join(", ", list)}], capOk={capOk}, cappedList=[{string.Join(", ", cappedList)}])");
        }
    }
}
