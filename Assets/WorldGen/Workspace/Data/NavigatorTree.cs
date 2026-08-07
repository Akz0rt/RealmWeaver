using System;
using System.Collections.Generic;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>Which kind of group a navigator entry is. World is the one COMPUTED group — see
    /// NavigatorTree.Build. Authored covers every ordinary PageGroup the document happens to contain,
    /// including «Люди» and «Сессии»: those are plain user groups, not a second fixed kind, deliberately —
    /// see NavigatorTree's class comment.
    ///
    /// THERE IS NO Pinned KIND ANY MORE (Task 10e). It existed to carry one hardcoded row — the world map —
    /// above a «Мир» whose membership was "pages bound to a place", a rule the world map could not satisfy
    /// because no page stands behind it. «Мир» is now the world's CONTENTS (see Build), so the world map is
    /// simply its first member and the separate kind, along with NavigatorView's render-without-header
    /// branch, had nothing left to do.
    ///
    /// Characters — ВТОРОЙ вычисляемый вид (после World): его состав собирается, а не хранится.
    /// Значение 2 берётся свободно: NavGroupKind не попадает ни в JSON, ни в PlayerPrefs
    /// (проверено — тип встречается в трёх файлах, все три рисуют).</summary>
    public enum NavGroupKind { World = 0, Authored = 1, Characters = 2 }

    public class NavNode
    {
        public string Title;

        /// <summary>Подпись под именем — поле «кто» карточки персонажа. Пустая у всех прочих строк.
        /// Нужна не для красоты: без неё раздел персонажей — голый столбик имён, а фильтр не может
        /// найти «кузнеца».</summary>
        public string Subtitle = "";

        /// <summary>What this row opens. An Authored row always names a PAGE by its id (N4). A «Мир» row
        /// never does: the world map targets the world-map surface, and a world object targets its EDITOR
        /// (WorldSurface.PoiEditor) — «двойное нажатие и выбор в навигаторе должен выдавать то же самое
        /// меню, именно оно соответствует точке интереса» (the Task 10c checkpoint ruling). A note ABOUT a
        /// place is an ordinary page the user authored, and it opens from its own authored group.</summary>
        public SurfaceRef Target;
    }

    public class NavGroup
    {
        public NavGroupKind Kind;
        public string Title;

        /// <summary>The backing PageGroup's id for an Authored group — carried through so a caller can
        /// rename/delete the group itself (NotesDocumentController.RenameGroup/DeleteGroup) without
        /// re-deriving it by title. Empty for World: it is computed (N1), not a stored PageGroup, so there
        /// is nothing for an id to name.</summary>
        public string Id = "";

        public List<NavNode> Nodes = new List<NavNode>();
    }

    /// <summary>
    /// Builds the navigator tree fresh from a NotesDocument (and the world it is a document ABOUT) on every
    /// call. Nothing here, or anywhere else, stores tree membership: the «Мир» group is computed from the
    /// `world` list the caller hands in, and that is the ONLY predicate that decides membership — no
    /// "visited" flag, no recency list, nothing written into the document. That requirement is unchanged
    /// since Task 7; the PREDICATE it guards is what Task 10e replaced, and it was replaced by editing this
    /// one method, which is the changeability the rule was bought for.
    ///
    /// N1, THE PREDICATE, restated by DM ruling at the Task 10c checkpoint: «все точки интереса, а также
    /// сама карта мира в навигаторе должны находиться под заголовком "Мир" и только там». «Мир» is the
    /// world's CONTENTS — the world map, and every world object, and nothing else. It used to be "every page
    /// whose Bound names a place", which meant a place appeared only after some path had auto-created a note
    /// about it, and appeared twice over (once in «Мир», once in its authored group — the double-listing an
    /// earlier version of this very comment called out). NotesPage.Bound therefore no longer drives the
    /// navigator at all; notes live in the groups the user authored them into, which is the other half of
    /// the same ruling.
    ///
    /// The umbrella spec argued against this — listing every POI "would drown the tree" for a generated
    /// world, which is why membership was narrowed to worked-on places in the first place. Overruled
    /// deliberately by the user, in plain words, after using the narrowed version. Recorded, not acted on:
    /// if it does drown, the predicate below is one loop and Ctrl+K still reaches everything.
    ///
    /// Deliberate narrowing from the umbrella spec: an earlier draft spoke of three fixed groups
    /// «МИР · ЛЮДИ · СЕССИИ». Only Мир is actually computed here — «Люди» and «Сессии» are ordinary
    /// PageGroups the default document happens to ship with, and render through NavGroupKind.Authored like
    /// any other group. There is no page-type or classification mechanism; the design explicitly refuses to
    /// have one.
    ///
    /// Free of any UnityEngine reference, the same arrangement WorkspaceOps and NotesDocOps use, so this
    /// runs in Tools/notes-harness without an Editor.
    /// </summary>
    public static class NavigatorTree
    {
        public const string WorldGroupTitle = "Мир";

        /// <summary>N3: filter matches on title with Trim().ToLowerInvariant().Contains; an empty filter
        /// matches everything; a group left with no surviving nodes is omitted entirely, never shown empty.
        ///
        /// "OMITTED ENTIRELY" HAS A SECOND CONSUMER as of Task 10h, and it is not about filtering at all:
        /// a group that stores no pages produces no nodes and so renders as nothing, which is why the
        /// navigator's «+ Группа» creates a group AND its first page in one gesture
        /// (NavigatorView.CreateGroupWithFirstPage) — a bare new group would leave the user looking at an
        /// unchanged tree. NavigatorTreeSelfTests pins that empty side directly, with no filter in play;
        /// before Task 10h only the filter-emptied case was covered, and the rule this now depends on could
        /// have been narrowed to "filtering" by anyone reading it as a search behaviour.
        ///
        /// `world` is the world's contents as the Data layer is allowed to see it — WorldObjectRef, the pure
        /// DTO Task 10b created for exactly this (see its own doc), never PoiData. That is what keeps this
        /// file free of UnityEngine AND of WorldGen.Generation, so it still runs in Tools/notes-harness.
        /// WorldObjectSource.Collect is the single mapper from the live PoiManager into it, shared with
        /// QuickOpenPopup; null (or empty) is the ordinary pre-generation state, not an error.</summary>
        public static List<NavGroup> Build(NotesDocument doc, IReadOnlyList<WorldObjectRef> world, string filter)
        {
            var groups = new List<NavGroup>();
            string needle = (filter ?? "").Trim().ToLowerInvariant();

            // Мир — N1: the world's contents, in the order given, and nothing else. See the class doc for the
            // ruling, and for why no page appears here even when one is bound to a place.
            //
            // Built BEFORE the `doc == null` guard below, deliberately: nothing in this group is derived from
            // `doc` — the world map is a fixed surface and `world` is the caller's, so gating it on a
            // document would lose the whole of «Мир» in a scene where nothing has wired a
            // NotesDocumentController yet (or where WorkspaceBuilder.EnsureDocumentController's discovery
            // finds no NotesRootBuilder at all). This arc has fixed that same shape twice — the pinned row in
            // the checkpoint-3 round, and the world-map Ctrl+K hit in Task 10b (QuickOpen.Search's own
            // CollectWorldMapHit sits above ITS doc==null guard for the identical reason). Its third
            // relocation is prevented here rather than rediscovered: NavigatorTreeSelfTests pins
            // `Build(null, world, "")` returning «Мир» in full, and NavigatorView.Rebuild passes a null
            // document straight through instead of short-circuiting on it (see its own comment).
            //
            // The world map's SurfaceRef must stay byte-identical to WorkspaceOps.NewDefault's own seed tab
            // (WorkspaceOps.cs:66 — Kind=WorldMap, Id="") — see WorkspaceOps.SameSurface. A merely-
            // equal-LOOKING ref (e.g. a different Id) would make WorkspaceOps.Open create a SECOND world-map
            // tab instead of focusing the one NewDefault already opened, which is exactly the "the map became
            // unreachable" defect this row exists to fix, just relocated. NavigatorTreeSelfTests pins it by
            // opening the target against a fresh NewDefault layout and asserting the tab count stays 1.
            //
            // Every node here obeys N3 like any other, the world map included: a filter matching no place
            // leaves no «Мир» heading at all, rather than an empty one or one holding only the map.
            var worldGroup = new NavGroup { Kind = NavGroupKind.World, Title = WorldGroupTitle };
            if (Matches(WorkspaceOps.DefaultWorldMapTitle, needle))
                worldGroup.Nodes.Add(new NavNode
                {
                    Title = WorkspaceOps.DefaultWorldMapTitle,
                    Target = new SurfaceRef { Kind = SurfaceKind.WorldMap, Id = "" },
                });
            if (world != null)
                foreach (var w in world)
                {
                    // w.Kind is not read: WorldSurface.PoiEditor is the one place that decides which surface a
                    // world object opens, and today every producer (WorldObjectSource.Collect) emits Poi. When
                    // settlements/buildings become world objects in their own right, that factory grows a kind
                    // switch — this loop does not, and neither does QuickOpenPopup's copy of the same decision.
                    if (w != null && Matches(w.Name, needle))
                        worldGroup.Nodes.Add(new NavNode { Title = w.Name, Target = WorldSurface.PoiEditor(w.Id) });
                }
            if (worldGroup.Nodes.Count > 0) groups.Add(worldGroup);

            // Everything below IS document-derived (Authored mirrors doc.Groups as-is), so the null guard
            // belongs HERE, after Мир, not before it.
            if (doc == null) return groups;

            // Authored — N2: every stored group renders as-is, in stored order, with its stored pages. A page
            // bound to a place is an ordinary page here and appears ONLY here — «и только там» cuts both
            // ways, and the double-listing this comment used to describe is gone with the old N1.
            foreach (var g in doc.Groups)
            {
                // Группа персонажей не рисуется среди обычных — она И ЕСТЬ раздел персонажей ниже.
                // Иначе ДМ увидел бы «Персонажи» дважды подряд.
                if (g.IsCharacters) continue;

                var authored = new NavGroup { Kind = NavGroupKind.Authored, Title = g.Title, Id = g.Id };
                foreach (var p in g.Pages)
                    if (MatchesPage(p, needle))
                        authored.Nodes.Add(MakeNode(p));
                if (authored.Nodes.Count > 0) groups.Add(authored);
            }

            // «Персонажи» — N4: ВНИЗУ, ниже обычных групп (ruling ДМ 2026-08-07).
            //
            // Две половины, и обе обязательны. Свои страницы — потому что персонажу, как всякой странице,
            // надо где-то физически лежать, и хранимый порядок здесь принадлежит ДМ. Подтянутые — потому что
            // персонаж, унесённый в группу места, обязан остаться в общем списке; по алфавиту, так как своего
            // порядка у них нет.
            //
            // ПОДТЯНУТАЯ СТРАНИЦА ОСТАЁТСЯ И В СВОЕЙ ГРУППЕ. Двойной показ здесь — решение ДМ, а не
            // недосмотр: «можно держать Ольгу внутри группы Тихая Гавань и одновременно видеть в общем
            // списке». Не добавлять сюда подавление строк по образцу Ctrl+K.
            var home = FindCharactersGroup(doc);
            var section = new NavGroup
            {
                Kind = NavGroupKind.Characters,
                Title = home != null ? home.Title : CharacterOps.CharactersGroupTitle,
                Id = home != null ? home.Id : "",
            };
            if (home != null)
                foreach (var p in home.Pages)
                    if (MatchesPage(p, needle))
                        section.Nodes.Add(MakeNode(p));

            var pulled = new List<NotesPage>();
            foreach (var g in doc.Groups)
            {
                if (g.IsCharacters) continue;
                foreach (var p in g.Pages)
                    if (CharacterOps.IsCharacter(p) && MatchesPage(p, needle))
                        pulled.Add(p);
            }
            pulled.Sort((a, b) => string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.CurrentCultureIgnoreCase));
            foreach (var p in pulled) section.Nodes.Add(MakeNode(p));

            if (section.Nodes.Count > 0) groups.Add(section);
            return groups;
        }

        /// <summary>Группа-владелец раздела персонажей, если такая уже есть. НЕ EnsureCharactersGroup:
        /// сборка дерева ничего не должна создавать в документе — Build вызывается на каждый рендер,
        /// в том числе при подсчёте фильтра, и не имеет права быть источником побочных эффектов.</summary>
        static PageGroup FindCharactersGroup(NotesDocument doc)
        {
            foreach (var g in doc.Groups)
                if (g.IsCharacters) return g;
            return null;
        }

        static bool Matches(string title, string needle)
            => needle.Length == 0 || (title ?? "").Trim().ToLowerInvariant().Contains(needle);

        /// <summary>N3 для страницы: имя ИЛИ подпись «кто» — без второй половины фильтр не может найти
        /// персонажа по роду занятий, только по имени.</summary>
        static bool MatchesPage(NotesPage p, string needle)
            => Matches(p.Name, needle) || (CharacterOps.IsCharacter(p) && Matches(p.Character.Who, needle));

        static NavNode MakeNode(NotesPage p)
            => new NavNode
            {
                Title = p.Name,
                Subtitle = CharacterOps.IsCharacter(p) ? (p.Character.Who ?? "") : "",
                Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = p.Id },
            };
    }
}
