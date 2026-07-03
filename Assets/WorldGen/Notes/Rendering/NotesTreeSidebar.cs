using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Collapsible accordion tree: groups expand to show their pages. Selecting a page
    /// opens it via NotesDocumentController. Collapsible via a header toggle button so the
    /// canvas can reclaim the full width when the tree isn't needed.
    /// </summary>
    public class NotesTreeSidebar : MonoBehaviour
    {
        NotesDocumentController documentController;
        Font builtinFont;
        Transform listContent;
        GameObject listGO;
        bool expanded = true;
        bool rebuildPending;

        public void Initialize(NotesDocumentController docController, Transform parent)
        {
            documentController = docController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootGO = new GameObject("NotesTreeSidebar");
            rootGO.transform.SetParent(parent, false);
            var vLayout = rootGO.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;

            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rootGO.transform, false);
            var headerImg = headerGO.AddComponent<Image>();
            headerImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            var headerBtn = headerGO.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            headerBtn.onClick.AddListener(ToggleExpanded);

            var headerTextGO = new GameObject("Text");
            headerTextGO.transform.SetParent(headerGO.transform, false);
            var headerText = headerTextGO.AddComponent<Text>();
            headerText.text = "☰ Страницы";
            headerText.font = builtinFont;
            headerText.fontSize = 12;
            headerText.color = Color.white;
            headerText.alignment = TextAnchor.MiddleLeft;
            var headerTextRect = headerTextGO.GetComponent<RectTransform>();
            headerTextRect.anchorMin = new Vector2(0f, 0f);
            headerTextRect.anchorMax = new Vector2(1f, 1f);
            headerTextRect.offsetMin = new Vector2(6f, 0f);
            headerTextRect.offsetMax = Vector2.zero;

            listGO = new GameObject("List");
            listGO.transform.SetParent(rootGO.transform, false);
            var listVLayout = listGO.AddComponent<VerticalLayoutGroup>();
            listVLayout.spacing = 2f;
            listVLayout.childControlWidth = true;
            listVLayout.childForceExpandWidth = true;
            listGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listContent = listGO.transform;

            AddSmallActionButton(rootGO.transform, "+ Группа", () =>
            {
                var group = documentController.CreateGroup("Новая группа");
                documentController.CreatePage(group.Id, "Страница 1");
            });

            documentController.OnDocumentChanged += RequestRebuild;
            Rebuild();
        }

        void ToggleExpanded()
        {
            expanded = !expanded;
            listGO.SetActive(expanded);
        }

        void RequestRebuild()
        {
            rebuildPending = true;
        }

        void LateUpdate()
        {
            if (!rebuildPending) return;
            rebuildPending = false;
            Rebuild();
        }

        void Rebuild()
        {
            // SetActive(false) takes effect immediately; Destroy() is deferred to end of
            // frame, so without deactivating first, the old and newly-built rows below would
            // both render for one frame, showing as overlapping/doubled UI.
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            foreach (var group in documentController.Document.Groups)
                BuildGroupRow(group);
        }

        void BuildGroupRow(PageGroup group)
        {
            var groupGO = new GameObject($"Group_{group.Id}");
            groupGO.transform.SetParent(listContent, false);
            var groupVLayout = groupGO.AddComponent<VerticalLayoutGroup>();
            groupVLayout.spacing = 1f;
            groupVLayout.childControlWidth = true;
            groupVLayout.childForceExpandWidth = true;
            groupGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(groupGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            string suffix = group.LinkedPoiId != null ? " 📍" : "";
            titleText.text = $"▾ {group.Title}{suffix}";
            titleText.font = builtinFont;
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.7f, 0.85f, 1f);
            titleText.alignment = TextAnchor.MiddleLeft;
            titleGO.AddComponent<LayoutElement>().preferredHeight = 30f;

            foreach (var page in group.Pages)
                BuildPageRow(groupGO.transform, group, page);

            AddSmallActionButton(groupGO.transform, "  + Страница", () =>
            {
                documentController.CreatePage(group.Id, $"Страница {group.Pages.Count + 1}");
            });
        }

        void BuildPageRow(Transform parent, PageGroup group, NotesPage page)
        {
            var rowGO = new GameObject($"Page_{page.Id}");
            rowGO.transform.SetParent(parent, false);
            var img = rowGO.AddComponent<Image>();
            bool isActive = documentController.ActivePage != null && documentController.ActivePage.Id == page.Id;
            img.color = isActive ? new Color(0.2f, 0.4f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.02f);
            var btn = rowGO.AddComponent<Button>();
            btn.targetGraphic = img;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 30f;
            btn.onClick.AddListener(() =>
            {
                documentController.OpenPage(page.Id);
                Rebuild();
            });

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(rowGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = $"• {page.Name}";
            text.font = builtinFont;
            text.fontSize = 13;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = Vector2.zero;
        }

        void AddSmallActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.45f, 0.25f, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            go.AddComponent<LayoutElement>().preferredHeight = 18f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = builtinFont;
            text.fontSize = 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = Vector2.zero;
        }
    }
}
