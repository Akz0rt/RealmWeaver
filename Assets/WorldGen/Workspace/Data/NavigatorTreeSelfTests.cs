using System.Collections.Generic;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>
    /// Self-tests for the computed navigator tree. Runs two ways: right-click this component in the
    /// Editor, or offline via Tools/notes-harness (see WorkspaceOpsSelfTests for the exact commands), which
    /// compiles these very sources against UnityEngine stubs.
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would
    /// break (N1..N5 in the plan), not a derived summary number.
    ///
    /// THE FIXTURE DELIBERATELY MAKES A POI AND A PAGE SHARE A NAME («Тихий Брод»), because Task 10e's
    /// central rule is about WHERE each of them appears and titles can no longer tell them apart. Every
    /// assertion below that cares about identity keys on Target.Kind/Target.Id — a title-based check would
    /// have read the POI's «Мир» row as proof the PAGE leaked in, or missed the leak entirely.
    /// </summary>
    public class NavigatorTreeSelfTests : MonoBehaviour
    {
        const string PoiId = "poi-1";
        const string OtherPoiId = "poi-2";

        /// <summary>A document holding one ordinary page and one page BOUND to PoiId — the second is what
        /// «Мир» used to be computed from, and what must now appear only in its own authored group.</summary>
        static NotesDocument Fixture(out NotesPage bound, out NotesPage plain)
        {
            var doc = new NotesDocument();
            var sessions = new PageGroup { Title = "Сессии" };
            plain = new NotesPage { Name = "Сессия 1" };
            sessions.Pages.Add(plain);
            doc.Groups.Add(sessions);

            var places = new PageGroup { Title = "Места" };
            bound = new NotesPage { Name = "Тихий Брод", Bound = new WorldRef { Kind = WorldRefKind.Poi, Id = PoiId } };
            places.Pages.Add(bound);
            doc.Groups.Add(places);
            return doc;
        }

        /// <summary>The world as WorldObjectSource.Collect would hand it over: one POI sharing the bound
        /// page's name, one that no page mentions at all.</summary>
        static List<WorldObjectRef> World() => new List<WorldObjectRef>
        {
            new WorldObjectRef { Kind = WorldRefKind.Poi, Id = PoiId, Name = "Тихий Брод", KindLabel = "город" },
            new WorldObjectRef { Kind = WorldRefKind.Poi, Id = OtherPoiId, Name = "Пиратская застава", KindLabel = "город" },
        };

        static string PageIdOf(NotesDocument doc, string title)
        {
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Name == title) return p.Id;
            return null;
        }

        static string PageGroupIdOf(NotesDocument doc, string title)
        {
            foreach (var g in doc.Groups)
                if (g.Title == title) return g.Id;
            return null;
        }

        static string Describe(NavNode n)
            => n == null ? "<none>" : $"«{n.Title}» → {n.Target?.Kind}/«{n.Target?.Id}»";

        [ContextMenu("Self-Test: Navigator Tree")]
        public void SelfTestTree()
        {
            bool ok = true;
            var doc = Fixture(out var bound, out var plain);
            var world = World();

            var groups = NavigatorTree.Build(doc, world, "");

            // N1 — «Мир» is FIRST, ahead of every authored group. Checked by POSITION (groups[0]), not just
            // "a World group exists somewhere": a mutant appending it last would pass the weaker check and
            // still bury the world map and every place below «Сессии»/«Места».
            if (groups.Count == 0 || groups[0].Kind != NavGroupKind.World)
            {
                string actual = groups.Count == 0 ? "no groups" : $"groups[0].Kind = {groups[0].Kind}";
                Debug.LogError($"FAIL tree: {actual}, want groups[0] to be the «Мир» group (N1)");
                ok = false;
            }
            else
            {
                var worldGroup = groups[0];

                // N1 — the world's contents, in order: the world map, then every world object as given.
                if (worldGroup.Nodes.Count != 1 + world.Count)
                {
                    var titles = new List<string>();
                    foreach (var n in worldGroup.Nodes) titles.Add(Describe(n));
                    Debug.LogError($"FAIL tree: «Мир» = [{string.Join(", ", titles)}], want {1 + world.Count} nodes — the world map plus every world object (N1)");
                    ok = false;
                }
                else
                {
                    // N1, the world map is the FIRST member of «Мир» (not merely present) — it is the row
                    // whose whole job is "the map stays reachable when its tab is closed".
                    var mapNode = worldGroup.Nodes[0];
                    if (mapNode.Title != WorkspaceOps.DefaultWorldMapTitle
                        || mapNode.Target == null || mapNode.Target.Kind != SurfaceKind.WorldMap || mapNode.Target.Id != "")
                    {
                        Debug.LogError($"FAIL tree: «Мир»[0] = {Describe(mapNode)}, want «{WorkspaceOps.DefaultWorldMapTitle}» → WorldMap/«» (N1)");
                        ok = false;
                    }
                    else
                    {
                        // N1's REAL point for that node, not just "the fields look right in isolation": the
                        // ref must be byte-identical to WorkspaceOps.NewDefault's own seed tab, or
                        // WorkspaceOps.Open creates a SECOND world-map tab instead of focusing the one
                        // already open (SameSurface compares Kind AND Id). Opening it against a fresh
                        // NewDefault layout — which already holds exactly that seed tab — must leave the tab
                        // count at 1, not grow it to 2. This is the assertion an Id of "x" actually fails.
                        var freshLayout = WorkspaceOps.NewDefault();
                        WorkspaceOps.Open(freshLayout, mapNode.Target, mapNode.Title, false);
                        if (freshLayout.Primary == null || freshLayout.Primary.Tabs.Count != 1)
                        {
                            int actualCount = freshLayout.Primary?.Tabs.Count ?? -1;
                            Debug.LogError($"FAIL tree: opening «Мир»'s world-map row against NewDefault left {actualCount} tab(s), want 1 — the ref must match NewDefault's seed tab exactly (N1)");
                            ok = false;
                        }
                    }

                    // N1 — every world object follows, IN THE ORDER GIVEN, each targeting its own EDITOR with
                    // its own id. Kind AND Id are both checked per node: a mutant that targeted Page (the old
                    // behaviour), or that reused one POI's id for every row, or that reordered them, each
                    // fails a named assertion here rather than a count somewhere else.
                    for (int i = 0; i < world.Count; i++)
                    {
                        var node = worldGroup.Nodes[i + 1];
                        var want = world[i];
                        if (node.Title != want.Name
                            || node.Target == null || node.Target.Kind != SurfaceKind.PoiEditor || node.Target.Id != want.Id)
                        {
                            Debug.LogError($"FAIL tree: «Мир»[{i + 1}] = {Describe(node)}, want «{want.Name}» → PoiEditor/«{want.Id}» — a place in the navigator opens the PLACE (N1)");
                            ok = false;
                        }
                    }
                }

                // N1, «и только там», the NEGATIVE half: no page reaches «Мир», not even the one bound to a
                // POI that IS listed there. Keyed on Target.Kind, never on Title — the bound page and its POI
                // share a name in this fixture on purpose.
                foreach (var n in worldGroup.Nodes)
                    if (n.Target != null && n.Target.Kind == SurfaceKind.Page)
                    { Debug.LogError($"FAIL tree: «Мир» holds a PAGE row {Describe(n)} — «Мир» is the world's contents and no page belongs in it (N1)"); ok = false; }

                // N5 — the computed group carries no PageGroup id, since there is no PageGroup behind it.
                if (worldGroup.Id != "")
                { Debug.LogError($"FAIL tree: «Мир» group Id = «{worldGroup.Id}», want empty (N5)"); ok = false; }
                if (worldGroup.Title != NavigatorTree.WorldGroupTitle)
                { Debug.LogError($"FAIL tree: «Мир» group Title = «{worldGroup.Title}», want «{NavigatorTree.WorldGroupTitle}» (N1)"); ok = false; }
            }

            // N1, «и только там», the POSITIVE half the negative one above needs to not pass vacuously: the
            // bound page must still be somewhere — in the authored group the user filed it into. Without
            // this, an implementation returning no page rows at all would satisfy "no page in «Мир»".
            var places = groups.Find(g => g.Kind == NavGroupKind.Authored && g.Title == "Места");
            if (places == null || !places.Nodes.Exists(n => n.Target != null && n.Target.Kind == SurfaceKind.Page && n.Target.Id == bound.Id))
            {
                string actual = places == null ? "no «Места» group" : $"{places.Nodes.Count} node(s)";
                Debug.LogError($"FAIL tree: the POI-bound page «{bound.Name}» is not in its authored group «Места» [{actual}] — it must appear THERE and only there (N1)");
                ok = false;
            }

            // N2 — every stored group renders, in stored order, bound page or not.
            var authored = groups.FindAll(g => g.Kind == NavGroupKind.Authored);
            if (authored.Count != 2)
            { Debug.LogError($"FAIL tree: {authored.Count} authored groups, want 2 (N2)"); ok = false; }
            if (!groups.Exists(g => g.Kind == NavGroupKind.Authored && g.Nodes.Exists(n => n.Title == plain.Name)))
            { Debug.LogError($"FAIL tree: the plain page «{plain.Name}» vanished from its authored group (N2)"); ok = false; }

            // N1, doc-independence: nothing in «Мир» is derived from the document, so a null one must return
            // it IN FULL. Losing it there would be this arc's "the map became unreachable" defect relocated
            // to whatever path leaves NavigatorView with no document wired (WorkspaceBuilder.EnsureDocument
            // Controller's discovery finding no NotesRootBuilder). Checked as EXACTLY one group, not just
            // "contains a World group" — a mutant that also let Authored groups survive a null doc (they
            // cannot: there is no doc to read them from) would slip past a weaker Exists check.
            var nullDocGroups = NavigatorTree.Build(null, world, "");
            if (nullDocGroups.Count != 1 || nullDocGroups[0].Kind != NavGroupKind.World)
            {
                string actual = nullDocGroups.Count == 0 ? "no groups"
                    : $"{nullDocGroups.Count} group(s), first Kind={nullDocGroups[0].Kind}";
                Debug.LogError($"FAIL tree: Build(null, world, \"\") = [{actual}], want exactly 1 group, Kind=World (N1, doc-independence)");
                ok = false;
            }
            else if (nullDocGroups[0].Nodes.Count != 1 + world.Count)
            {
                Debug.LogError($"FAIL tree: Build(null, world, \"\")'s «Мир» has {nullDocGroups[0].Nodes.Count} node(s), want {1 + world.Count} — the world map plus every place, undiminished by having no document (N1, doc-independence)");
                ok = false;
            }

            // N1, the other tolerance: a null world is the pre-generation state (no PoiManager yet — see
            // WorldObjectSource.Collect), not an error. The world map survives it alone.
            var noWorld = NavigatorTree.Build(doc, null, "");
            var noWorldGroup = noWorld.Find(g => g.Kind == NavGroupKind.World);
            if (noWorldGroup == null || noWorldGroup.Nodes.Count != 1
                || noWorldGroup.Nodes[0].Target == null || noWorldGroup.Nodes[0].Target.Kind != SurfaceKind.WorldMap)
            {
                string actual = noWorldGroup == null ? "no «Мир» group" : $"{noWorldGroup.Nodes.Count} node(s), first {Describe(noWorldGroup.Nodes.Count > 0 ? noWorldGroup.Nodes[0] : null)}";
                Debug.LogError($"FAIL tree: Build(doc, null, \"\")'s «Мир» = [{actual}], want exactly the world-map row (N1)");
                ok = false;
            }

            // N3 — filtering folds case, and empties whole groups away.
            groups = NavigatorTree.Build(doc, world, "  ТИХИЙ  ");
            if (groups.Exists(g => g.Nodes.Exists(n => n.Title == "Сессия 1")))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » left «Сессия 1» present, want it excluded (N3)"); ok = false; }
            if (groups.Exists(g => g.Nodes.Count == 0))
            {
                string emptyTitle = groups.Find(g => g.Nodes.Count == 0)?.Title ?? "?";
                Debug.LogError($"FAIL tree: group «{emptyTitle}» survived with 0 nodes, want it omitted entirely (N3)");
                ok = false;
            }

            // N3, from the positive side: the two checks above pass VACUOUSLY if case-folding is broken and
            // the filter ends up matching nothing at all (an empty `groups` excludes both "Сессия 1" and
            // every empty group trivially). Require the actual matches — the POI row AND the page row.
            var filteredWorld = groups.Find(g => g.Kind == NavGroupKind.World);
            if (filteredWorld == null || !filteredWorld.Nodes.Exists(n => n.Target != null && n.Target.Kind == SurfaceKind.PoiEditor && n.Target.Id == PoiId))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » matched no «Мир» row for the POI «Тихий Брод», want 1 (N3)"); ok = false; }
            if (!groups.Exists(g => g.Kind == NavGroupKind.Authored && g.Nodes.Exists(n => n.Title == "Тихий Брод")))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » matched 0 pages named «Тихий Брод», want 1 (N3)"); ok = false; }
            // ...and the POI the filter does NOT name must be gone from «Мир», or "the filter applies to
            // world objects" would be satisfied by a filter that silently passes everything.
            if (filteredWorld != null && filteredWorld.Nodes.Exists(n => n.Target != null && n.Target.Id == OtherPoiId))
            { Debug.LogError("FAIL tree: filter «  ТИХИЙ  » left «Пиратская застава» in «Мир», want it excluded — N3 applies to world objects too (N3)"); ok = false; }

            // N3 applies to the world map like any other node, and the group is omitted rather than shown
            // holding only the map: a filter matching no place at all must leave no «Мир» heading.
            var noMatch = NavigatorTree.Build(doc, world, "зюзюка");
            if (noMatch.Exists(g => g.Kind == NavGroupKind.World))
            {
                var leftover = noMatch.Find(g => g.Kind == NavGroupKind.World);
                Debug.LogError($"FAIL tree: filter «зюзюка» still produced a «Мир» group with {leftover.Nodes.Count} node(s), want it omitted — N3 must apply to the world-map row too (N3)");
                ok = false;
            }
            var mapMatch = NavigatorTree.Build(doc, world, "карта");
            if (!mapMatch.Exists(g => g.Kind == NavGroupKind.World && g.Nodes.Exists(n => n.Title == WorkspaceOps.DefaultWorldMapTitle)))
            { Debug.LogError("FAIL tree: filter «карта» dropped the world-map row, want it to still match «Карта мира» (N3)"); ok = false; }

            // N3, THE EMPTY-GROUP SIDE, which nothing above pins. The «  ТИХИЙ  » checks prove a group
            // emptied BY THE FILTER is omitted; this proves the same for a group that stores no pages at
            // all, with no filter in play — a different code path in the reader's head, and the one the
            // navigator now DEPENDS on: «+ Группа» creates a group and its first page in one gesture
            // (NavigatorView.CreateGroupWithFirstPage) precisely because a bare group renders as nothing and
            // the button would look broken. Before Task 10h nothing would have failed if that rule had been
            // narrowed to "the filter can empty a group".
            //
            // A LOCAL document rather than the shared Fixture: a third group there would leave the
            // `authored.Count != 2` assertion above passing while ITS comment («every stored group renders,
            // in stored order») quietly became false — the stale-comment failure this arc keeps rediscovering.
            var mixedDoc = new NotesDocument();
            var filledGroup = new PageGroup { Title = "Полная" };
            filledGroup.Pages.Add(new NotesPage { Name = "Страница 1" });
            mixedDoc.Groups.Add(filledGroup);
            mixedDoc.Groups.Add(new PageGroup { Title = "Пустая" });

            var mixed = NavigatorTree.Build(mixedDoc, null, "");
            // BOTH halves, because either alone passes vacuously: an implementation that dropped every
            // authored group would satisfy "the empty one is gone", and one that kept every group would
            // satisfy "the filled one is here".
            if (!mixed.Exists(g => g.Kind == NavGroupKind.Authored && g.Title == "Полная" && g.Nodes.Count == 1))
            {
                var found = mixed.Find(g => g.Title == "Полная");
                string actual = found == null ? "no «Полная» group" : $"{found.Nodes.Count} node(s)";
                Debug.LogError($"FAIL tree: a group holding ONE page rendered as [{actual}], want 1 node — a group with a page must appear (N3)");
                ok = false;
            }
            if (mixed.Exists(g => g.Title == "Пустая"))
            {
                Debug.LogError("FAIL tree: a group storing NO pages still produced a «Пустая» group, want it omitted entirely — this is what makes «+ Группа» create a first page too (N3)");
                ok = false;
            }

            // N4 — AUTHORED nodes target PAGES, by the page's own id. «Мир» is excluded from this loop on
            // purpose: its nodes deliberately target the world map and POI editors (N1's own checks above pin
            // their exact shape), so folding it in would make a CORRECT implementation fail.
            groups = NavigatorTree.Build(doc, world, "");
            foreach (var g in groups)
            {
                if (g.Kind == NavGroupKind.World) continue;
                foreach (var n in g.Nodes)
                    if (n.Target.Kind != SurfaceKind.Page || n.Target.Id != PageIdOf(doc, n.Title))
                    { Debug.LogError($"FAIL tree: node «{n.Title}» targets {n.Target.Kind}/{n.Target.Id}, want Page/{PageIdOf(doc, n.Title)} (N4)"); ok = false; }
            }

            // N5 — an Authored group carries its backing PageGroup's id (so a caller can rename/delete the
            // group without re-deriving it by title). The positive check (matching the REAL id, not just
            // "non-empty") is what catches a mutant that populates Id with some other stand-in value.
            var sessionsGroup = groups.Find(g => g.Kind == NavGroupKind.Authored && g.Title == "Сессии");
            string wantSessionsId = PageGroupIdOf(doc, "Сессии");
            if (sessionsGroup == null || sessionsGroup.Id != wantSessionsId)
            {
                string actual = sessionsGroup == null ? "no «Сессии» group" : $"Id=«{sessionsGroup.Id}»";
                Debug.LogError($"FAIL tree: authored group «Сессии» = [{actual}], want Id=«{wantSessionsId}» (N5)");
                ok = false;
            }

            // N1 — unbinding a page changes NOTHING about the tree any more. This is the mirror of the old
            // N1's "unbinding removes it from «Мир»" check, kept pointing the other way on purpose: it is the
            // assertion that fails loudly if anyone reintroduces a Bound-driven membership rule.
            bound.Bound = null;
            var afterUnbind = NavigatorTree.Build(doc, world, "");
            var unboundWorld = afterUnbind.Find(g => g.Kind == NavGroupKind.World);
            if (unboundWorld == null || unboundWorld.Nodes.Count != 1 + world.Count)
            {
                string actual = unboundWorld == null ? "no «Мир» group" : $"{unboundWorld.Nodes.Count} node(s)";
                Debug.LogError($"FAIL tree: «Мир» after unbinding a page = [{actual}], want {1 + world.Count} — NotesPage.Bound must not drive the navigator (N1)");
                ok = false;
            }
            if (!afterUnbind.Exists(g => g.Kind == NavGroupKind.Authored && g.Nodes.Exists(n => n.Target.Id == bound.Id)))
            { Debug.LogError("FAIL tree: unbinding a page removed it from its authored group too, want it untouched (N2)"); ok = false; }

            Debug.Log(ok ? "Self-Test Navigator Tree: PASS" : "Self-Test Navigator Tree: FAIL");
        }

        // Мутант: раздел персонажей встаёт над обычными группами.
        // Мутант: группа с флагом рисуется ДВАЖДЫ — и как обычная, и как раздел.
        [ContextMenu("Self-Test: Characters Section Is Last And Not Duplicated")]
        public void SelfTestCharactersSectionIsLastAndNotDuplicated()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var chars = new PageGroup { Title = "Персонажи", IsCharacters = true };
            chars.Pages.Add(new NotesPage { Name = "Ольга Медная", Character = new CharacterCard() });
            var sessions = new PageGroup { Title = "Сессии" };
            sessions.Pages.Add(new NotesPage { Name = "Сессия 1" });
            doc.Groups.Add(chars);      // хранится ПЕРВОЙ — правило «внизу» и порядок хранения расходятся
            doc.Groups.Add(sessions);

            var raw = NavigatorTree.Build(doc, null, "");
            // «Мир» строится ВСЕГДА при пустом фильтре (Matches пропускает всё, «Карта мира» — константа) —
            // проверяем, что она остаётся первой и здесь, а затем отбрасываем её: она к разделу персонажей
            // отношения не имеет, и без этого индексы ниже считали бы лишнюю голову.
            if (raw.Count == 0 || raw[0].Kind != NavGroupKind.World)
            { Debug.LogError("FAIL: «Мир» перестал быть первым, когда появился раздел персонажей"); ok = false; }
            var groups = raw.FindAll(g => g.Kind != NavGroupKind.World);
            if (groups.Count != 2)
            { Debug.LogError("FAIL: разделов не два, а " + groups.Count); ok = false; }
            else
            {
                if (groups[0].Kind != NavGroupKind.Authored || groups[0].Title != "Сессии")
                { Debug.LogError("FAIL: обычная группа не первая"); ok = false; }
                if (groups[1].Kind != NavGroupKind.Characters)
                { Debug.LogError("FAIL: раздел персонажей не последний"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Characters Section Is Last And Not Duplicated: PASS" : "Self-Test Characters Section Is Last And Not Duplicated: FAIL");
        }

        // Мутант: подтягивания нет — раздел показывает только свою группу.
        // Фикстура: персонаж лежит в ЧУЖОЙ группе, иначе «подтягивание» неотличимо от «показать группу».
        [ContextMenu("Self-Test: Character In Foreign Group Is Pulled In And Stays")]
        public void SelfTestCharacterInForeignGroupIsPulledInAndStays()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var town = new PageGroup { Title = "Тихая Гавань" };
            town.Pages.Add(new NotesPage { Name = "Ольга Медная", Character = new CharacterCard { Who = "кузнец" } });
            town.Pages.Add(new NotesPage { Name = "Порт" });
            doc.Groups.Add(town);

            // «Мир» отбрасывается — см. комментарий в SelfTestCharactersSectionIsLastAndNotDuplicated.
            var groups = NavigatorTree.Build(doc, null, "").FindAll(g => g.Kind != NavGroupKind.World);
            if (groups.Count != 2)
            { Debug.LogError("FAIL: нет раздела персонажей при персонаже в чужой группе"); ok = false; }
            else
            {
                var authored = groups[0];
                if (authored.Nodes.Count != 2)
                { Debug.LogError("FAIL: персонаж пропал из своей группы (подавление строк запрещено)"); ok = false; }

                var section = groups[1];
                if (section.Kind != NavGroupKind.Characters)
                { Debug.LogError("FAIL: второй раздел не персонажи"); ok = false; }
                if (section.Nodes.Count != 1 || section.Nodes[0].Title != "Ольга Медная")
                { Debug.LogError("FAIL: персонаж не подтянут в раздел"); ok = false; }
                else if (section.Nodes[0].Subtitle != "кузнец")
                { Debug.LogError("FAIL: подпись «кто» не проставлена"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Character In Foreign Group Is Pulled In And Stays: PASS" : "Self-Test Character In Foreign Group Is Pulled In And Stays: FAIL");
        }

        // Мутант: подтянутые не сортируются / свои сортируются вместе с подтянутыми.
        [ContextMenu("Self-Test: Own Pages Keep Order Pulled Are Sorted")]
        public void SelfTestOwnPagesKeepOrderPulledAreSorted()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var chars = new PageGroup { Title = "Персонажи", IsCharacters = true };
            chars.Pages.Add(new NotesPage { Name = "Яков", Character = new CharacterCard() });
            chars.Pages.Add(new NotesPage { Name = "Анна", Character = new CharacterCard() });
            doc.Groups.Add(chars);

            var town = new PageGroup { Title = "Тихая Гавань" };
            town.Pages.Add(new NotesPage { Name = "Пётр", Character = new CharacterCard() });
            town.Pages.Add(new NotesPage { Name = "Борис", Character = new CharacterCard() });
            doc.Groups.Add(town);

            // «Мир» отбрасывается — см. комментарий в SelfTestCharactersSectionIsLastAndNotDuplicated.
            var section = NavigatorTree.Build(doc, null, "").FindAll(g => g.Kind != NavGroupKind.World)[1];
            var names = new List<string>();
            foreach (var n in section.Nodes) names.Add(n.Title);
            var got = string.Join(",", names);
            if (got != "Яков,Анна,Борис,Пётр")
            { Debug.LogError("FAIL: ожидалось «Яков,Анна,Борис,Пётр» (свои — как разложены, подтянутые — по алфавиту), получено «" + got + "»"); ok = false; }

            Debug.Log(ok ? "Self-Test Own Pages Keep Order Pulled Are Sorted: PASS" : "Self-Test Own Pages Keep Order Pulled Are Sorted: FAIL");
        }

        // Мутант: фильтр не смотрит на подпись.
        [ContextMenu("Self-Test: Filter Matches Subtitle")]
        public void SelfTestFilterMatchesSubtitle()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var chars = new PageGroup { Title = "Персонажи", IsCharacters = true };
            chars.Pages.Add(new NotesPage { Name = "Ольга Медная", Character = new CharacterCard { Who = "кузнец" } });
            chars.Pages.Add(new NotesPage { Name = "Яков", Character = new CharacterCard { Who = "трактирщик" } });
            doc.Groups.Add(chars);

            var groups = NavigatorTree.Build(doc, null, "кузн");
            if (groups.Count != 1)
            { Debug.LogError("FAIL: раздел не найден по подписи"); ok = false; }
            else if (groups[0].Nodes.Count != 1 || groups[0].Nodes[0].Title != "Ольга Медная")
            { Debug.LogError("FAIL: фильтр по подписи отобрал не то"); ok = false; }

            if (NavigatorTree.Build(doc, null, "нетакого").Count != 0)
            { Debug.LogError("FAIL: пустой раздел показан вместо того, чтобы исчезнуть"); ok = false; }

            Debug.Log(ok ? "Self-Test Filter Matches Subtitle: PASS" : "Self-Test Filter Matches Subtitle: FAIL");
        }

        // Мутант: страница без карточки, лежащая в группе персонажей, выбрасывается из раздела.
        [ContextMenu("Self-Test: Own Page Without Card Still Shows")]
        public void SelfTestOwnPageWithoutCardStillShows()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var chars = new PageGroup { Title = "Персонажи", IsCharacters = true };
            chars.Pages.Add(new NotesPage { Name = "Черновик списка имён" });
            doc.Groups.Add(chars);

            // «Мир» отбрасывается — см. комментарий в SelfTestCharactersSectionIsLastAndNotDuplicated.
            var groups = NavigatorTree.Build(doc, null, "").FindAll(g => g.Kind != NavGroupKind.World);
            if (groups.Count != 1 || groups[0].Nodes.Count != 1)
            { Debug.LogError("FAIL: страница без карточки исчезла из своей же группы персонажей"); ok = false; }

            Debug.Log(ok ? "Self-Test Own Page Without Card Still Shows: PASS" : "Self-Test Own Page Without Card Still Shows: FAIL");
        }
    }
}
