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
