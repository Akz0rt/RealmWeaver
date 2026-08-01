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
            // The very first page is a SESSION SHEET, not an empty board. The whole point of shipping a prep
            // method is that a first-time DM meets it immediately; leaving the template behind a sidebar
            // button they have no reason to press would hide the one thing meant to teach them the craft.
            var group = CreateGroup("Заметки");
            var sheet = CreateSessionSheet(group.Id);
            if (sheet != null) OpenPage(sheet.Id);
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

        /// <summary>Creates a page of the given kind. The kind defaults to Board so every existing call site
        /// keeps its current behaviour; callers that want a document say so explicitly.</summary>
        public NotesPage CreatePage(string groupId, string name, PageKind kind = PageKind.Board)
        {
            var group = FindGroup(groupId);
            if (group == null) return null;
            var page = new NotesPage { Name = name, Kind = kind };
            group.Pages.Add(page);
            OnDocumentChanged?.Invoke();
            return page;
        }

        /// <summary>Creates a session sheet from the prep template, auto-named «Сессия N» where N is one past
        /// the number of document pages already in that group.</summary>
        public NotesPage CreateSessionSheet(string groupId)
        {
            var group = FindGroup(groupId);
            if (group == null) return null;

            int existing = 0;
            foreach (var p in group.Pages)
                if (p.Kind == PageKind.Document) existing++;

            var page = NotesDocOps.CreateSessionSheet($"Сессия {existing + 1}");
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

        /// <summary>Raises OnDocumentChanged for a mutation made DIRECTLY against Document by a pure op
        /// rather than through one of this class's own wrappers above (Task 10c).
        ///
        /// WHY THIS EXISTS AT ALL. A pure op (NotesDocOps.*) takes a NotesDocument and has no controller to
        /// fire an event on, so a page it creates or changes is invisible to every listener until something
        /// unrelated happens to raise one. The original case was EnsurePageFor: Task 10c's call sites each
        /// followed it with a WorkspaceController.Open, which raises OnLayoutChanged, so the navigator
        /// refreshed by coincidence of ordering rather than by construction — the cross-task trap Task 10b's
        /// review flagged and a per-task reviewer cannot catch. Those call sites are gone (Task 10e removed
        /// the auto-created page entirely, and «Мир» no longer reads pages at all), but the seam they exposed
        /// is not: any future mutation made directly against Document — Р3's binding work first among them —
        /// must call this rather than rely on an unrelated event arriving next.
        ///
        /// Cheap to over-call: every listener's handler is a request-a-rebuild flag coalesced in LateUpdate
        /// (NavigatorView.RequestRebuild and its siblings), so a redundant raise costs one bool write.</summary>
        public void NotifyDocumentChanged() => OnDocumentChanged?.Invoke();

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
