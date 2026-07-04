using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Owns the in-memory NotesDocument: group/page CRUD, active-page tracking.
    /// Attach to any GameObject in the notes UI hierarchy.
    /// </summary>
    public class NotesDocumentController : MonoBehaviour
    {
        public NotesDocument Document { get; private set; } = new NotesDocument();
        public NotesPage ActivePage { get; private set; }

        public event Action OnDocumentChanged;
        public event Action<NotesPage> OnActivePageChanged;

        void Awake()
        {
            var group = CreateGroup("Заметки");
            CreatePage(group.Id, "Страница 1");
            OpenPage(group.Pages[0].Id);
        }

        // ── Group CRUD ─────────────────────────────────────────────────────────

        public PageGroup CreateGroup(string title, string linkedPoiId = null)
        {
            var group = new PageGroup { Title = title, LinkedPoiId = linkedPoiId };
            Document.Groups.Add(group);
            OnDocumentChanged?.Invoke();
            return group;
        }

        public void RenameGroup(string groupId, string title)
        {
            var group = FindGroup(groupId);
            if (group == null) return;
            group.Title = title;
            OnDocumentChanged?.Invoke();
        }

        public void DeleteGroup(string groupId)
        {
            var group = FindGroup(groupId);
            if (group == null) return;
            bool activeWasInGroup = ActivePage != null && group.Pages.Any(p => p.Id == ActivePage.Id);
            Document.Groups.Remove(group);
            if (activeWasInGroup)
            {
                ActivePage = null;
                OnActivePageChanged?.Invoke(null);
            }
            OnDocumentChanged?.Invoke();
        }

        // ── Page CRUD ──────────────────────────────────────────────────────────

        public NotesPage CreatePage(string groupId, string name)
        {
            var group = FindGroup(groupId);
            if (group == null) return null;
            var page = new NotesPage { Name = name };
            group.Pages.Add(page);
            OnDocumentChanged?.Invoke();
            return page;
        }

        public void RenamePage(string pageId, string name)
        {
            var page = FindPage(pageId);
            if (page == null) return;
            page.Name = name;
            OnDocumentChanged?.Invoke();
        }

        public void DeletePage(string pageId)
        {
            var group = Document.Groups.FirstOrDefault(g => g.Pages.Any(p => p.Id == pageId));
            if (group == null) return;
            group.Pages.RemoveAll(p => p.Id == pageId);
            if (ActivePage != null && ActivePage.Id == pageId)
            {
                ActivePage = null;
                OnActivePageChanged?.Invoke(null);
            }
            OnDocumentChanged?.Invoke();
        }

        public void OpenPage(string pageId)
        {
            var page = FindPage(pageId);
            if (page == null || page == ActivePage) return;
            ActivePage = page;
            OnActivePageChanged?.Invoke(page);
        }

        public PageGroup FindGroupByPoiId(string poiId) =>
            Document.Groups.FirstOrDefault(g => g.LinkedPoiId == poiId);

        /// <summary>Replaces the entire document with a previously-saved one — used when
        /// loading a project. Unlike OpenPage, this always fires both events unconditionally
        /// (a load is a completely fresh state, so listeners must refresh regardless of
        /// whether the new ActivePage happens to share an Id with the old one).</summary>
        public void LoadDocument(NotesDocument doc)
        {
            Document = doc;
            ActivePage = Document.Groups.SelectMany(g => g.Pages).FirstOrDefault();
            OnDocumentChanged?.Invoke();
            OnActivePageChanged?.Invoke(ActivePage);
        }

        // ── Internals ──────────────────────────────────────────────────────────

        PageGroup FindGroup(string groupId) => Document.Groups.FirstOrDefault(g => g.Id == groupId);

        NotesPage FindPage(string pageId) =>
            Document.Groups.SelectMany(g => g.Pages).FirstOrDefault(p => p.Id == pageId);

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Document CRUD")]
        public void SelfTestDocumentCrud()
        {
            var doc = new NotesDocument();
            // Exercise the same logic paths as the instance methods, against a scratch document,
            // so the test doesn't disturb whatever document is currently loaded in the scene.
            var group = new PageGroup { Title = "Test Group" };
            doc.Groups.Add(group);

            var pageA = new NotesPage { Name = "A" };
            var pageB = new NotesPage { Name = "B" };
            group.Pages.Add(pageA);
            group.Pages.Add(pageB);

            bool twoPages = group.Pages.Count == 2;

            group.Pages.RemoveAll(p => p.Id == pageA.Id);
            bool onePageLeft = group.Pages.Count == 1 && group.Pages[0].Id == pageB.Id;

            doc.Groups.Remove(group);
            bool noGroupsLeft = doc.Groups.Count == 0;

            bool ok = twoPages && onePageLeft && noGroupsLeft;
            Debug.Log(ok
                ? "Self-Test Notes Document CRUD: PASS"
                : $"Self-Test Notes Document CRUD: FAIL (twoPages={twoPages}, onePageLeft={onePageLeft}, noGroupsLeft={noGroupsLeft})");
        }

        [ContextMenu("Self-Test: Notes Document Load")]
        public void SelfTestLoadDocument()
        {
            var doc = new NotesDocument();
            var group = new PageGroup { Title = "Loaded Group" };
            var page = new NotesPage { Name = "Loaded Page" };
            group.Pages.Add(page);
            doc.Groups.Add(group);

            bool docChangedFired = false;
            NotesPage activePageFromEvent = null;
            System.Action onDocChanged = () => docChangedFired = true;
            System.Action<NotesPage> onActivePageChanged = p => activePageFromEvent = p;
            OnDocumentChanged += onDocChanged;
            OnActivePageChanged += onActivePageChanged;

            LoadDocument(doc);

            OnDocumentChanged -= onDocChanged;
            OnActivePageChanged -= onActivePageChanged;

            bool ok = docChangedFired && Document == doc && ActivePage == page && activePageFromEvent == page;
            Debug.Log(ok
                ? "Self-Test Notes Document Load: PASS"
                : $"Self-Test Notes Document Load: FAIL (docChangedFired={docChangedFired}, activePageMatches={ActivePage == page})");
        }
    }
}
